// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

#if STRIDE_GRAPHICS_API_DIRECT3D12
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Stride.Core.Collections;
using Stride.Core.Threading;
using Stride.Graphics.Direct3D;
using Stride.Graphics.Direct3D12;

namespace Stride.Graphics
{
    public unsafe partial class GraphicsDevice
    {
        // D3D12_CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT (not exposed by SharpDX)
        internal readonly int ConstantBufferDataPlacementAlignment = 256;

        private D3D12 d3d12;
        private DXGI dxgi;

        private const GraphicsPlatform GraphicPlatform = GraphicsPlatform.Direct3D12;

        internal readonly ConcurrentPool<List<GraphicsResource>> StagingResourceLists = new ConcurrentPool<List<GraphicsResource>>(() => new List<GraphicsResource>());
        internal readonly ConcurrentPool<List<ID3D12DescriptorHeap>> DescriptorHeapLists = new (() => new List<ID3D12DescriptorHeap>());

        private bool simulateReset = false;
        private string rendererName;

        private ComPtr<ID3D12Device> nativeDevice;
        internal ComPtr<ID3D12CommandQueue> NativeCommandQueue;

        internal CommandQueue CommandQueue;

        internal GraphicsProfile RequestedProfile;
        internal D3DFeatureLevel CurrentFeatureLevel;

        internal ComPtr<ID3D12CommandQueue> NativeCopyCommandQueue;
        internal ComPtr<ID3D12CommandAllocator> NativeCopyCommandAllocator;
        internal ComPtr<ID3D12GraphicsCommandList> NativeCopyCommandList;
        private ID3D12Fence nativeCopyFence;
        private long nextCopyFenceValue = 1;

        internal CommandAllocatorPool CommandAllocators;
        internal HeapPool SrvHeaps;
        internal HeapPool SamplerHeaps;
        internal const int SrvHeapSize = 2048;
        internal const int SamplerHeapSize = 64;

        internal DescriptorAllocator SamplerAllocator;
        internal DescriptorAllocator ShaderResourceViewAllocator;
        internal DescriptorAllocator UnorderedAccessViewAllocator => ShaderResourceViewAllocator;
        internal DescriptorAllocator DepthStencilViewAllocator;
        internal DescriptorAllocator RenderTargetViewAllocator;

        private ComPtr<ID3D12Resource> nativeUploadBuffer;
        private IntPtr nativeUploadBufferStart;
        private int nativeUploadBufferOffset;

        internal int SrvHandleIncrementSize;
        internal int SamplerHandleIncrementSize;

        private ID3D12Fence nativeFence;
        private long lastCompletedFence;
        internal ulong NextFenceValue = 1;
        private AutoResetEvent fenceEvent = new AutoResetEvent(false);

        // Temporary or destroyed resources kept around until the GPU doesn't need them anymore
        internal Queue<KeyValuePair<long, object>> TemporaryResources = new Queue<KeyValuePair<long, object>>();

        private readonly FastList<ComPtr<ID3D12GraphicsCommandList>> nativeCommandLists = new ();

        /// <summary>
        /// The tick frquency of timestamp queries in Hertz.
        /// </summary>
        public long TimestampFrequency { get; private set; }

        /// <summary>
        ///     Gets the status of this device.
        /// </summary>
        /// <value>The graphics device status.</value>
        public GraphicsDeviceStatus GraphicsDeviceStatus
        {
            get
            {
                if (simulateReset)
                {
                    simulateReset = false;
                    return GraphicsDeviceStatus.Reset;
                }

                DXGIError result = (DXGIError)NativeDevice.Get().GetDeviceRemovedReason();

                return result switch
                {
                    DXGIError.DeviceRemoved => GraphicsDeviceStatus.Removed,
                    DXGIError.DeviceReset => GraphicsDeviceStatus.Reset,
                    DXGIError.DeviceHung => GraphicsDeviceStatus.Hung,
                    DXGIError.DriverInternalError => GraphicsDeviceStatus.InternalError,
                    DXGIError.InvalidCall => GraphicsDeviceStatus.InvalidCall,
                    < 0 => GraphicsDeviceStatus.Reset,
                    _ => GraphicsDeviceStatus.Normal
                };
            }
        }

        /// <summary>
        ///     Gets the native device.
        /// </summary>
        /// <value>The native device.</value>
        internal ComPtr<ID3D12Device> NativeDevice => nativeDevice;

        /// <summary>
        ///     Marks context as active on the current thread.
        /// </summary>
        public void Begin()
        {
            FrameTriangleCount = 0;
            FrameDrawCalls = 0;
        }

        /// <summary>
        /// Enables profiling.
        /// </summary>
        /// <param name="enabledFlag">if set to <c>true</c> [enabled flag].</param>
        public void EnableProfile(bool enabledFlag)
        {
        }

        /// <summary>
        ///     Unmarks context as active on the current thread.
        /// </summary>
        public void End()
        {
        }

        /// <summary>
        /// Executes a deferred command list.
        /// </summary>
        /// <param name="commandList">The deferred command list.</param>
        public void ExecuteCommandList(CompiledCommandList commandList)
        {
            ulong fenceValue = ExecuteCommandListInternal(commandList);
        }

        /// <summary>
        /// Executes multiple deferred command lists.
        /// </summary>
        /// <param name="count">Number of command lists to execute.</param>
        /// <param name="commandLists">The deferred command lists.</param>
        public void ExecuteCommandLists(int count, CompiledCommandList[] commandLists)
        {
            if (commandLists == null)
            {
                throw new ArgumentNullException(nameof(commandLists));
            }

            if (count > commandLists.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            ulong fenceValue = NextFenceValue++;

            // Recycle resources
            for (int index = 0; index < count; index++)
            {
                CompiledCommandList commandList = commandLists[index];

                nativeCommandLists.Add(commandList.NativeCommandList);
                RecycleCommandListResources(commandList, fenceValue);
            }

            // Submit and signal fence
            var ppCommandLists = stackalloc ComPtr<ID3D12CommandList>[count]
            {
                (ID3D12CommandList*)nativeCommandLists.To
            };

            NativeCommandQueue.Get().ExecuteCommandLists(count, nativeCommandLists.Items);
            NativeCommandQueue.Get().Signal(Fence, fenceValue);

            ReleaseTemporaryResources();

            nativeCommandLists.Clear();
        }

        public void SimulateReset()
        {
            simulateReset = true;
        }

        private void InitializePostFeatures()
        {
        }

        private string GetRendererName()
        {
            return rendererName;
        }


        internal void RecycleCommandListResources(CompiledCommandList commandList, ulong fenceValue)
        {
            // Set fence on staging textures
            foreach (var stagingResource in commandList.StagingResources)
            {
                stagingResource.StagingFenceValue = fenceValue;
            }

            StagingResourceLists.Release(commandList.StagingResources);
            commandList.StagingResources.Clear();

            // Recycle resources
            foreach (var heap in commandList.SrvHeaps)
            {
                SrvHeaps.RecycleObject(fenceValue, heap);
            }
            commandList.SrvHeaps.Clear();
            DescriptorHeapLists.Release(commandList.SrvHeaps);

            foreach (var heap in commandList.SamplerHeaps)
            {
                SamplerHeaps.RecycleObject(fenceValue, heap);
            }
            commandList.SamplerHeaps.Clear();
            DescriptorHeapLists.Release(commandList.SamplerHeaps);

            commandList.Builder.NativeCommandLists.Enqueue(commandList.NativeCommandList);
            CommandAllocators.RecycleObject(fenceValue, commandList.NativeCommandAllocator);
        }

        /// <summary>
        ///     Initializes the specified device.
        /// </summary>
        /// <param name="graphicsProfiles">The graphics profiles.</param>
        /// <param name="deviceCreationFlags">The device creation flags.</param>
        /// <param name="windowHandle">The window handle.</param>
        private void InitializePlatformDevice(GraphicsProfile[] graphicsProfiles, DeviceCreationFlags deviceCreationFlags, object windowHandle)
        {
            d3d12 = D3D12.GetApi();
            dxgi = DXGI.GetApi();

            if (nativeDevice.Handle != null)
            {
                // Destroy previous device
                ReleaseDevice();
            }

            AdapterDesc* pDesc = null;
            Adapter.NativeAdapter.Get().GetDesc(pDesc);
            rendererName = SilkMarshal.PtrToString((nint)pDesc->Description);

            // Profiling is supported through pix markers
            IsProfilingSupported = true;

            // Command lists are thread-safe and execute deferred
            IsDeferred = true;

            bool isDebug = (deviceCreationFlags & DeviceCreationFlags.Debug) != 0;

            if (isDebug)
            {
                using ComPtr<ID3D12Debug> debugController = null;
                Guid iid = ID3D12Debug.Guid;
                int hResult = d3d12.GetDebugInterface(&iid, (void**)&debugController);

                if (HResult.IndicatesSuccess(hResult))
                {
                    debugController.Get().EnableDebugLayer();
                }
                else
                {
                    isDebug = false;
                }
            }

            // Create Device D3D12 with feature Level based on profile
            for (int index = 0; index < graphicsProfiles.Length; index++)
            {
                GraphicsProfile graphicsProfile = graphicsProfiles[index];
                try
                {
                    // D3D12 supports only feature level 11+
                    D3DFeatureLevel level = graphicsProfile.ToFeatureLevel();

                    if (level < D3DFeatureLevel.D3DFeatureLevel110)
                    {
                        level = D3DFeatureLevel.D3DFeatureLevel110;
                    }

                    ID3D12Device* d3dDevice;
                    Guid deviceIID = ID3D12Device.Guid;
                    D3DFeatureLevel minFeatureLevel = D3DFeatureLevel.D3DFeatureLevel110;
                    IUnknown* adapter = (IUnknown*)Adapter.NativeAdapter.GetAddressOf();
                    int deviceHResult = d3d12.CreateDevice(adapter, minFeatureLevel, &deviceIID, (void**)&d3dDevice);
                    
                    SilkMarshal.ThrowHResult(deviceHResult);

                    nativeDevice = d3dDevice;

                    RequestedProfile = graphicsProfile;
                    CurrentFeatureLevel = level;
                    break;
                }
                catch (Exception)
                {
                    if (index == graphicsProfiles.Length - 1)
                        throw;
                }
            }

            // Describe and create the command queue.
            CommandQueueDesc queueDesc = new (CommandListType.CommandListTypeDirect);
            Guid commandQuquqIID = ID3D12CommandQueue.Guid;
            ID3D12CommandQueue** pCommandQueue = NativeCommandQueue.GetAddressOf();
            int commandQueueHResult = nativeDevice.Get().CreateCommandQueue(&queueDesc, &commandQuquqIID, (void**)pCommandQueue);

            SilkMarshal.ThrowHResult(commandQueueHResult);

            //queueDesc.Type = CommandListType.Copy;
            NativeCopyCommandQueue = nativeDevice.CreateCommandQueue(queueDesc);
            TimestampFrequency = NativeCommandQueue.TimestampFrequency;

            SrvHandleIncrementSize = NativeDevice.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            SamplerHandleIncrementSize = NativeDevice.GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);

            if (isDebug)
            {
                var debugDevice = nativeDevice.QueryInterfaceOrNull<DebugDevice>();
                if (debugDevice != null)
                {
                    var infoQueue = debugDevice.QueryInterfaceOrNull<InfoQueue>();
                    if (infoQueue != null)
                    {
                        MessageId[] disabledMessages =
                        {
                            // This happens when render target or depth stencil clear value is diffrent
                            // than provided during resource allocation.
                            MessageId.CleardepthstencilviewMismatchingclearvalue,
                            MessageId.ClearrendertargetviewMismatchingclearvalue,

                            // This occurs when there are uninitialized descriptors in a descriptor table,
                            // even when a shader does not access the missing descriptors.
                            MessageId.InvalidDescriptorHandle,
                            
                            // These happen when capturing with VS diagnostics
                            MessageId.MapInvalidNullRange,
                            MessageId.UnmapInvalidNullRange,
                        };

                        // Disable irrelevant debug layer warnings
                        InfoQueueFilter filter = new InfoQueueFilter
                        {
                            DenyList = new InfoQueueFilterDescription
                            {
                                Ids = disabledMessages
                            }
                        };
                        infoQueue.AddStorageFilterEntries(filter);

                        //infoQueue.SetBreakOnSeverity(MessageSeverity.Error, true);
                        //infoQueue.SetBreakOnSeverity(MessageSeverity.Warning, true);

                        infoQueue.Dispose();
                    }
                    debugDevice.Dispose();
                }
            }

            // Prepare pools
            CommandAllocators = new CommandAllocatorPool(this);
            SrvHeaps = new HeapPool(this, SrvHeapSize, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            SamplerHeaps = new HeapPool(this, SamplerHeapSize, DescriptorHeapType.Sampler);

            // Prepare descriptor allocators
            SamplerAllocator = new DescriptorAllocator(this, DescriptorHeapType.Sampler);
            ShaderResourceViewAllocator = new DescriptorAllocator(this, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            DepthStencilViewAllocator = new DescriptorAllocator(this, DescriptorHeapType.DepthStencilView);
            RenderTargetViewAllocator = new DescriptorAllocator(this, DescriptorHeapType.RenderTargetView);

            // Prepare copy command list (start it closed, so that every new use start with a Reset)
            NativeCopyCommandAllocator = NativeDevice.CreateCommandAllocator(CommandListType.Direct);
            NativeCopyCommandList = NativeDevice.CreateCommandList(CommandListType.Direct, NativeCopyCommandAllocator, null);
            NativeCopyCommandList.Close();

            // Fence for next frame and resource cleaning
            nativeFence = NativeDevice.CreateFence(0, FenceFlags.None);
            nativeCopyFence = NativeDevice.CreateFence(0, FenceFlags.None);
        }

        internal IntPtr AllocateUploadBuffer(int size, out SharpDX.Direct3D12.Resource resource, out int offset, int alignment = 0)
        {
            // TODO D3D12 thread safety, should we simply use locks?

            // Align
            if (alignment > 0)
                nativeUploadBufferOffset = (nativeUploadBufferOffset + alignment - 1) / alignment * alignment;

            if (nativeUploadBuffer == null || nativeUploadBufferOffset + size > nativeUploadBuffer.Description.Width)
            {
                if (nativeUploadBuffer != null)
                {
                    nativeUploadBuffer.Unmap(0);
                    TemporaryResources.Enqueue(new KeyValuePair<long, object>(NextFenceValue, nativeUploadBuffer));
                }

                // Allocate new buffer
                // TODO D3D12 recycle old ones (using fences to know when GPU is done with them)
                // TODO D3D12 ResourceStates.CopySource not working?
                var bufferSize = Math.Max(4 * 1024*1024, size);
                nativeUploadBuffer = NativeDevice.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(bufferSize), ResourceStates.GenericRead);
                nativeUploadBufferStart = nativeUploadBuffer.Map(0, new SharpDX.Direct3D12.Range());
                nativeUploadBufferOffset = 0;
            }

            // Bump allocate
            resource = nativeUploadBuffer;
            offset = nativeUploadBufferOffset;
            nativeUploadBufferOffset += size;
            return nativeUploadBufferStart + offset;
        }

        internal void WaitCopyQueue()
        {
            NativeCommandQueue.ExecuteCommandList(NativeCopyCommandList);
            NativeCommandQueue.Signal(nativeCopyFence, nextCopyFenceValue);
            NativeCommandQueue.Wait(nativeCopyFence, nextCopyFenceValue);
            nextCopyFenceValue++;
        }

        internal void ReleaseTemporaryResources()
        {
            lock (TemporaryResources)
            {
                // Release previous frame resources
                while (TemporaryResources.Count > 0 && IsFenceCompleteInternal(TemporaryResources.Peek().Key))
                {
                    var temporaryResource = TemporaryResources.Dequeue().Value;
                    //temporaryResource.Value.Dispose();
                    var comObject = temporaryResource as SharpDX.ComObject;
                    if (comObject != null)
                        ((SharpDX.IUnknown)comObject).Release();
                    else
                    {
                        var referenceLink = temporaryResource as GraphicsResourceLink;
                        if (referenceLink != null)
                        {
                            referenceLink.ReferenceCount--;
                        }
                    }
                }
            }
        }

        private void AdjustDefaultPipelineStateDescription(ref PipelineStateDescription pipelineStateDescription)
        {
        }

        protected void DestroyPlatformDevice()
        {
            ReleaseDevice();
        }

        private void ReleaseDevice()
        {
            // Wait for completion of everything queued
            NativeCommandQueue.Signal(nativeFence, NextFenceValue);
            NativeCommandQueue.Wait(nativeFence, NextFenceValue);

            // Release command queue
            NativeCommandQueue.Dispose();
            NativeCommandQueue = null;

            NativeCopyCommandQueue.Dispose();
            NativeCopyCommandQueue = null;

            NativeCopyCommandAllocator.Dispose();
            NativeCopyCommandList.Dispose();

            nativeUploadBuffer.Dispose();

            // Release temporary resources
            ReleaseTemporaryResources();
            nativeFence.Dispose();
            nativeFence = null;
            nativeCopyFence.Dispose();
            nativeCopyFence = null;

            // Release pools
            CommandAllocators.Dispose();
            SrvHeaps.Dispose();
            SamplerHeaps.Dispose();

            // Release allocators
            SamplerAllocator.Dispose();
            ShaderResourceViewAllocator.Dispose();
            DepthStencilViewAllocator.Dispose();
            RenderTargetViewAllocator.Dispose();

            if (IsDebugMode)
            {
                var debugDevice = NativeDevice.QueryInterfaceOrNull<SharpDX.Direct3D12.DebugDevice>();
                if (debugDevice != null)
                {
                    debugDevice.ReportLiveDeviceObjects(SharpDX.Direct3D12.ReportingLevel.Detail);
                    debugDevice.Dispose();
                }
            }

            nativeDevice.Dispose();
            nativeDevice = null;
        }

        internal void OnDestroyed()
        {
        }

        internal void TagResource(GraphicsResourceLink resourceLink)
        {
            var texture = resourceLink.Resource as Texture;
            if (texture != null && texture.Usage == GraphicsResourceUsage.Dynamic)
            {
                // Increase the reference count until GPU is done with the resource
                resourceLink.ReferenceCount++;
                TemporaryResources.Enqueue(new KeyValuePair<long, object>(NextFenceValue, resourceLink));
            }

            var buffer = resourceLink.Resource as Buffer;
            if (buffer != null && buffer.Usage == GraphicsResourceUsage.Dynamic)
            {
                // Increase the reference count until GPU is done with the resource
                resourceLink.ReferenceCount++;
                TemporaryResources.Enqueue(new KeyValuePair<long, object>(NextFenceValue, resourceLink));
            }
        }
    }
}
#endif

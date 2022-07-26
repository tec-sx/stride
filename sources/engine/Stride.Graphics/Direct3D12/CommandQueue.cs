using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics.Direct3D12
{
    internal unsafe sealed class CommandQueue : IDisposable
    {
        private ulong lastCompletedFence;
        
        public GraphicsDevice Device { get; }

        public ComPtr<ID3D12CommandQueue> NativeCommandQueue { get; }

        public ComPtr<ID3D12CommandQueue> NativeCopyCommandQueue { get; }

        public ComPtr<ID3D12Fence> Fence { get; }
        
        public IntPtr FenceEvent { get; private set; }

        public ulong NextFenceValue { get; private set; } = 1;

        public CommandQueue(GraphicsDevice device, CommandListType commandListType)
        {
            Device = device;
            ID3D12CommandQueue* pNativeCommandQueue = CreateCommandQueue(commandListType);
            NativeCommandQueue = new(pNativeCommandQueue);
            Fence = new(CreateFence());
            FenceEvent = CreateFenceEvent();
        }

        public void Dispose()
        {
            // Wait for completion of everything queued
            NativeCommandQueue.Get().Signal(Fence, NextFenceValue);
            NativeCommandQueue.Get().Wait(Fence, NextFenceValue);
            
            // Release command queue
            NativeCommandQueue.Dispose();

            DestroyFenceEvent();
            
            Fence.Dispose();
        }

        public bool FenceIsComplete(ID3D12Fence* fence, ulong fenceValue)
        {
            return fence->GetCompletedValue() >= fenceValue;
        }

        public void WaitForFence(ID3D12Fence* fence, ulong fenceValue)
        {
            if (FenceIsComplete(fence, fenceValue))
            {
                return;
            }

            fence->SetEventOnCompletion(fenceValue, FenceEvent.ToPointer());

            _ = SilkMarshal.WaitWindowsObjects(FenceEvent);
        }

        private ID3D12CommandQueue* CreateCommandQueue(CommandListType commandListType)
        {
            CommandQueueDesc queueDesc = new(commandListType);
            Guid commandQueueIID = ID3D12CommandQueue.Guid;
            ID3D12CommandQueue* pCommandQueue = null;
            ID3D12Device nativeDevice = Device.NativeDevice.Get();
            int hResult = nativeDevice.CreateCommandQueue(&queueDesc, &commandQueueIID, (void**)&pCommandQueue);

            SilkMarshal.ThrowHResult(hResult);

            return pCommandQueue;
        }

        private ID3D12Fence* CreateFence()
        {
            ulong initialValue = 0;
            Guid fenceIID = ID3D12Fence.Guid;
            ID3D12Fence* pFence = null;
            ID3D12Device nativeDevice = Device.NativeDevice.Get();
            int hResult = nativeDevice.CreateFence(initialValue, FenceFlags.FenceFlagNone, &fenceIID, (void**)&pFence);

            SilkMarshal.ThrowHResult(hResult);

            return pFence;
        }

        private IntPtr CreateFenceEvent()
        {
            var fenceEvent = SilkMarshal.CreateWindowsEvent(null, false, false, null);

            if (fenceEvent == IntPtr.Zero)
            {
                var hResult = Marshal.GetHRForLastWin32Error();
                Marshal.ThrowExceptionForHR(hResult);
            }

            return fenceEvent;
        }

        void DestroyFenceEvent()
        {
            IntPtr fenceEvent = FenceEvent;

            if (fenceEvent != IntPtr.Zero)
            {
                FenceEvent = IntPtr.Zero;
                _ = SilkMarshal.CloseWindowsHandle(FenceEvent);
            }
        }

        /// <summary>
        /// Executes multiple deferred command lists.
        /// </summary>
        /// <param name="count">Number of command lists to execute.</param>
        /// <param name="commandLists">The deferred command lists.</param>
        public ulong ExecuteCommandLists(int count, CompiledCommandList[] commandLists)
        {
            ulong fenceValue = NextFenceValue++;

            ID3D12CommandList** ppCommandList = stackalloc ID3D12CommandList*[count];
            
            for (int i = 0; i < count; i++)
            {
                ppCommandList[i] = (ID3D12CommandList*)commandLists[i].NativeCommandList.Handle;
            }

            // Submit and signal fence

            NativeCommandQueue.Get().ExecuteCommandLists(1, ppCommandList);
            NativeCommandQueue.Get().Signal(Fence, fenceValue);

            return fenceValue;
        }

        internal ulong ExecuteCommandList(CompiledCommandList commandList)
        {
            const int count = 1;
            ulong fenceValue = NextFenceValue++;
            
            ID3D12CommandList** ppCommandList = stackalloc ID3D12CommandList*[count]
            {
                (ID3D12CommandList*)commandList.NativeCommandList.Handle
            };

            // Submit and signal fence

            NativeCommandQueue.Get().ExecuteCommandLists(count, ppCommandList);
            NativeCommandQueue.Get().Signal(Fence, fenceValue);

            return fenceValue;
        }

        internal bool IsFenceComplete(ulong fenceValue)
        {
            // Try to avoid checking the fence if possible
            if (fenceValue > lastCompletedFence)
            {
                // Protect against race conditions
                lastCompletedFence = Math.Max(lastCompletedFence, Fence.Get().GetCompletedValue());
            }

            return fenceValue <= lastCompletedFence;
        }

        internal void WaitForFence(ulong fenceValue)
        {
            if (IsFenceComplete(fenceValue))
            {
                return;
            }

            ID3D12Fence* nativeFence = Fence.Handle;

            // TODO D3D12 in case of concurrency, this lock could end up blocking too
            // long a second thread with lower fenceValue then first one
            
            Fence.Get().SetEventOnCompletion(fenceValue, FenceEvent.ToPointer());
            _ = SilkMarshal.WaitWindowsObjects(FenceEvent);
            lastCompletedFence = fenceValue;
        }

        internal void WaitCopyQueue()
        {
            NativeCommandQueue.Get().ExecuteCommandList(NativeCopyCommandList);
            NativeCommandQueue.Get().Signal(nativeCopyFence, nextCopyFenceValue);
            NativeCommandQueue.Get().Wait(nativeCopyFence, nextCopyFenceValue);
            nextCopyFenceValue++;
        }
    }
}

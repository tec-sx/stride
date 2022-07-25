using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics.Direct3D12
{
    /// <summary>
    /// Allocate descriptor handles. For now a simple bump alloc, but at some point we will have to make a real allocator with free
    /// </summary>
    internal unsafe class DescriptorAllocator : IDisposable
    {
        private const uint DescriptorPerHeap = 256;

        private GraphicsDevice device;
        private DescriptorHeapType descriptorHeapType;
        private ComPtr<ID3D12DescriptorHeap> currentHeap;
        private CpuDescriptorHandle currentHandle;
        private uint remainingHandles;
        private readonly uint descriptorSize;

        public DescriptorAllocator(GraphicsDevice device, DescriptorHeapType descriptorHeapType)
        {
            this.device = device;
            this.descriptorHeapType = descriptorHeapType;

            ID3D12Device nativeDevice = device.NativeDevice.Get();
            descriptorSize = nativeDevice.GetDescriptorHandleIncrementSize(descriptorHeapType);
        }

        public void Dispose()
        {
            currentHeap.Dispose();
            currentHeap = null;
        }

        public CpuDescriptorHandle Allocate(uint count)
        {
            if (currentHeap.Handle == null || remainingHandles < count)
            {
                ID3D12Device nativeDevice = device.NativeDevice.Get();
                DescriptorHeapFlags heapFlags = DescriptorHeapFlags.DescriptorHeapFlagNone;
                uint nodeMask = 1;
                DescriptorHeapDesc descripor = new(descriptorHeapType, DescriptorPerHeap, heapFlags, nodeMask);
                Guid iid = ID3D12DescriptorHeap.Guid;
                ID3D12DescriptorHeap* pDescriptorHeap = null;
                int hResult = nativeDevice.CreateDescriptorHeap(&descripor, &iid, (void**)&pDescriptorHeap);

                SilkMarshal.ThrowHResult(hResult);

                remainingHandles = DescriptorPerHeap;
                currentHandle = currentHeap.Get().GetCPUDescriptorHandleForHeapStart();
            }

            currentHandle.Ptr += descriptorSize;
            remainingHandles -= count;

            return currentHandle;
        }
    }
}

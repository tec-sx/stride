using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics.Direct3D12
{
    internal unsafe class HeapPool : ResourcePool<ID3D12DescriptorHeap>
    {
        private readonly int heapSize;
        private readonly DescriptorHeapType heapType;

        public HeapPool(GraphicsDevice graphicsDevice, int heapSize, DescriptorHeapType heapType) : base(graphicsDevice)
        {
            this.heapSize = heapSize;
            this.heapType = heapType;
        }

        protected override ComPtr<ID3D12DescriptorHeap> CreateObject()
        {
            // No allocator ready to be used, let's create a new one
            ID3D12Device nativeDevice = GraphicsDevice.NativeDevice.Get();
            DescriptorHeapFlags heapFlags = DescriptorHeapFlags.DescriptorHeapFlagShaderVisible;
            DescriptorHeapDesc descripor = new(heapType, (uint)heapSize, heapFlags);
            Guid iid = ID3D12DescriptorHeap.Guid;
            ID3D12DescriptorHeap* pDescriptorHeap = null;
            int hResult = nativeDevice.CreateDescriptorHeap(&descripor, &iid, (void**)&pDescriptorHeap);

            SilkMarshal.ThrowHResult(hResult);

            return new ComPtr<ID3D12DescriptorHeap>(pDescriptorHeap);
        }

        protected override void ResetObject(ComPtr<ID3D12DescriptorHeap> obj)
        { }
    }
}

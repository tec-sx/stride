using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics.Direct3D12
{
    internal unsafe class CommandAllocatorPool : ResourcePool<ID3D12CommandAllocator>
    {
        public CommandAllocatorPool(GraphicsDevice graphicsDevice) : base(graphicsDevice)
        { }

        protected override ComPtr<ID3D12CommandAllocator> CreateObject()
        {
            // No allocator ready to be used, let's create a new one
            CommandListType commandListType = CommandListType.CommandListTypeDirect;
            Guid iid = ID3D12CommandAllocator.Guid;
            ID3D12CommandAllocator* pCommandAllocator = null;
            ID3D12Device nativeDevice = GraphicsDevice.NativeDevice.Get();

            int hResult = nativeDevice.CreateCommandAllocator(commandListType, &iid, (void**)&pCommandAllocator);

            SilkMarshal.ThrowHResult(hResult);

            return new ComPtr<ID3D12CommandAllocator>(pCommandAllocator);
        }

        protected override void ResetObject(ComPtr<ID3D12CommandAllocator> obj)
        {
            obj.Get().Reset();
        }
    }
}

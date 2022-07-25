using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics.Direct3D12
{
    internal unsafe abstract class ResourcePool<T> : IDisposable where T : unmanaged
    {
        protected readonly GraphicsDevice GraphicsDevice;
        private readonly Queue<KeyValuePair<ulong, ComPtr<T>>> liveObjects = new ();

        protected ResourcePool(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
        }

        public void Dispose()
        {
            lock (liveObjects)
            {
                foreach (var liveObject in liveObjects)
                {
                    liveObject.Value.Dispose();
                }

                liveObjects.Clear();
            }
        }

        public ComPtr<T> GetObject()
        {
            // TODO D3D12: SpinLock
            lock (liveObjects)
            {
                // Check if first allocator is ready for reuse
                if (liveObjects.Count > 0)
                {
                    KeyValuePair<ulong, ComPtr<T>> firstAllocator = liveObjects.Peek();
                    ID3D12Fence fence = GraphicsDevice.CommandQueue.Fence.Get();

                    if (firstAllocator.Key <= fence.GetCompletedValue())
                    {
                        liveObjects.Dequeue();

                        ResetObject(firstAllocator.Value);

                        return firstAllocator.Value;
                    }
                }

                return CreateObject();
            }
        }

        public void RecycleObject(ulong fenceValue, ComPtr<T> obj)
        {
            // TODO D3D12: SpinLock
            lock (liveObjects)
            {
                KeyValuePair<ulong, ComPtr<T>> pair = new(fenceValue, obj);
                liveObjects.Enqueue(pair);
            }
        }

        protected abstract ComPtr<T> CreateObject();

        protected abstract void ResetObject(ComPtr<T> obj);
    }
}

// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_GRAPHICS_API_DIRECT3D12
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Stride.Graphics
{
    public partial struct CompiledCommandList
    {
        internal CommandList Builder;
        internal ComPtr<ID3D12GraphicsCommandList> NativeCommandList;
        internal ComPtr<ID3D12CommandAllocator> NativeCommandAllocator;
        internal List<HeapDesc> SrvHeaps;
        internal List<HeapDesc> SamplerHeaps;
        internal List<GraphicsResource> StagingResources;
    }
}
#endif

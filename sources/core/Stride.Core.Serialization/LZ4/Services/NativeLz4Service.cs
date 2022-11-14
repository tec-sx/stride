// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Diagnostics;
using Stride.Core.Native;

namespace Stride.Core.LZ4.Services
{
    internal class NativeLz4Service : NativeLz4Base, ILZ4Service
    {
        public string CodecName => $"NativeMode {(IntPtr.Size == 4 ? "32" : "64")}";

        public unsafe int Decode(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength, bool knownOutputLength)
        {
            Debug.Assert(
                (inputOffset | inputLength) >= 0 &&
                (uint)inputOffset + (uint)inputLength <= (uint)(input?.Length ?? 0));
            Debug.Assert(
                (outputOffset | outputLength) >= 0 &&
                (uint)outputOffset + (uint)outputLength <= (uint)(output?.Length ?? 0));
            fixed (byte* pInput = input)
            fixed (byte* pOutput = output)
            {
                if (knownOutputLength)
                {
                    LZ4_uncompress(pInput + inputOffset, pOutput + outputOffset, outputLength);

                    return outputLength;
                }

                return LZ4_uncompress_unknownOutputSize(pInput + inputOffset, pOutput + outputOffset, inputLength, outputLength);
            }
        }

        public unsafe int Encode(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
        {
            Debug.Assert(
                (inputOffset | inputLength) >= 0 &&
                (uint)inputOffset + (uint)inputLength <= (uint)(input?.Length ?? 0));
            Debug.Assert(
                (outputOffset | outputLength) >= 0 &&
                (uint)outputOffset + (uint)outputLength <= (uint)(output?.Length ?? 0));
            fixed (byte* pInput = input)
            fixed (byte* pOutput = output)
            {
                return LZ4_compress_limitedOutput(pInput + inputOffset, pOutput + outputOffset, inputLength, outputLength);
            }
        }

        public unsafe int EncodeHC(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
        {
            Debug.Assert(
                (inputOffset | inputLength) >= 0 &&
                (uint)inputOffset + (uint)inputLength <= (uint)(input?.Length ?? 0));
            Debug.Assert(
                (outputOffset | outputLength) >= 0 &&
                (uint)outputOffset + (uint)outputLength <= (uint)(output?.Length ?? 0));
            fixed (byte* pInput = input)
            fixed (byte* pOutput = output)
            {
                return LZ4_compressHC_limitedOutput(pInput + inputOffset, pOutput + outputOffset, inputLength, outputLength);
            }
        }
    }
}

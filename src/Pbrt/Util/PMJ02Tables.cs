// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/pmj02tables.h and pmj02tables.cpp
//
// The table is converted from pmj02tables.cpp by tools/gen-tables.ps1 into
// src/Pbrt/Data/PMJ02BN.bin (uint32 little-endian), which is embedded in the
// assembly. pmj02bnSamples[s][i][c] is
// PMJ02bnSamples[(s * nPMJ02bnSamples + i) * 2 + c].

using System.Runtime.InteropServices;

namespace Pbrt;

public static partial class Util
{
    // PMJ02BN Table Declarations
    public const int nPMJ02bnSets = 5;
    public const int nPMJ02bnSamples = 65536;

    public static ReadOnlySpan<uint> PMJ02bnSamples => PMJ02Data.samples;

    // PMJ02BN Table Inline Functions
    public static Point2f GetPMJ02BNSample(int setIndex, int sampleIndex)
    {
        setIndex %= nPMJ02bnSets;
        DCHECK_LT(sampleIndex, nPMJ02bnSamples);
        sampleIndex %= nPMJ02bnSamples;

        // Convert from fixed-point.
        // Double precision is key here for the pixel sample sorting, but not
        // necessary otherwise.
        int i = (setIndex * nPMJ02bnSamples + sampleIndex) * 2;
        return new Point2f((Float)(PMJ02Data.samples[i] / 4294967296.0), (Float)(PMJ02Data.samples[i + 1] / 4294967296.0));
    }

    // Separate class so the table is loaded on first use.
    private static class PMJ02Data
    {
        public static readonly uint[] samples = Load();

        private static uint[] Load()
        {
            const string name = "Pbrt.Data.PMJ02BN.bin";
            using Stream stream = typeof(PMJ02Data).Assembly.GetManifestResourceStream(name) ??
                                  throw new FatalErrorException($"{name}: resource not found");
            uint[] t = new uint[nPMJ02bnSets * nPMJ02bnSamples * 2];
            CHECK_EQ(4L * t.Length, stream.Length);
            stream.ReadExactly(MemoryMarshal.AsBytes(t.AsSpan()));
            if (!BitConverter.IsLittleEndian)
                System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(t, t);
            return t;
        }
    }
}

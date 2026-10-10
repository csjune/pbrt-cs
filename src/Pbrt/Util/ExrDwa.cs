// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// DWAA/DWAB decompression for Exr.cs: a port of the decoding parts of
// OpenEXR's src/lib/OpenEXRCore/internal_dwa*.h (OpenEXR 3.3.1,
// BSD-3-Clause, Copyright (c) DreamWorks Animation LLC and Contributors of
// the OpenEXR Project).
//
// OpenEXR picks its inverse DCT and float->half conversion at run time
// (scalar, SSE2 or AVX DCT; scalar or F16C conversion), and the DCT variants
// sum their terms in different orders, so its DWA decoding isn't bit-for-bit
// the same on all CPUs. This port follows the AVX + F16C code paths, which
// are what OpenEXR uses on x86-64 CPUs with AVX (and what pbrt was compared
// against); it emulates the AVX inline assembly's operations in order, in
// scalar code.

using System.Buffers.Binary;

namespace Pbrt;

internal static partial class Exr
{
    private enum DwaScheme { Unknown = 0, LossyDct = 1, Rle = 2 }

    private readonly record struct DwaClassifier(string suffix, DwaScheme scheme, PixelType type, int cscIdx,
                                                 bool caseInsensitive);

    private static readonly DwaClassifier[] sLegacyChannelRules = MakeLegacyRules();

    private static DwaClassifier[] MakeLegacyRules()
    {
        List<DwaClassifier> rules = [];
        void Lossy(string s, int csc)
        {
            rules.Add(new(s, DwaScheme.LossyDct, PixelType.Half, csc, true));
            rules.Add(new(s, DwaScheme.LossyDct, PixelType.Float, csc, true));
        }
        Lossy("r", 0);
        Lossy("red", 0);
        Lossy("g", 1);
        Lossy("grn", 1);
        Lossy("green", 1);
        Lossy("b", 2);
        Lossy("blu", 2);
        Lossy("blue", 2);
        Lossy("y", -1);
        Lossy("by", -1);
        Lossy("ry", -1);
        rules.Add(new("a", DwaScheme.Rle, PixelType.UInt, -1, true));
        rules.Add(new("a", DwaScheme.Rle, PixelType.Half, -1, true));
        rules.Add(new("a", DwaScheme.Rle, PixelType.Float, -1, true));
        return [.. rules];
    }

    // The dwaCompressorToLinear table from dwaLookups.h (written by
    // tools/gen-dwa-table.py).
    private static readonly Lazy<ushort[]> dwaToLinear = new(() =>
    {
        const string name = "Pbrt.Data.DwaToLinear.bin";
        using Stream stream = typeof(Exr).Assembly.GetManifestResourceStream(name) ??
                              throw new InvalidOperationException($"missing resource {name}");
        byte[] bytes = new byte[65536 * 2];
        stream.ReadExactly(bytes);
        ushort[] table = new ushort[65536];
        for (int i = 0; i < 65536; ++i)
            table[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i));
        return table;
    });

    private static ExrException DwaCorrupt() => new("corrupt DWA chunk");

    // exr_compress_max_buffer_size(): libdeflate's zlib bound plus 9.
    private static ulong CompressMaxBufferSize(ulong n) =>
        6 + 5 * Math.Max((n + 9999) / 10000, 1) + n + 1 + 8 + 9;

    private static float HalfToFloat(ushort h) => (float)BitConverter.UInt16BitsToHalf(h);

    // float_to_half() from internal_coding.h (round to nearest even).
    private static ushort FloatToHalf(float fv)
    {
        uint vi = BitConverter.SingleToUInt32Bits(fv);
        uint ui = vi & ~0x80000000u;
        ushort ret = (ushort)((vi >> 16) & 0x8000);

        if (ui >= 0x38800000)
        {
            if (ui >= 0x7f800000)
            {
                ret |= 0x7c00;
                if (ui == 0x7f800000)
                    return ret;
                uint m = (ui & 0x7fffff) >> 13;
                return (ushort)(ret | m | (m == 0 ? 1u : 0u));
            }

            if (ui > 0x477fefff)
                return (ushort)(ret | 0x7c00);

            ui -= 0x38000000;
            ui = (ui + 0x00000fff + ((ui >> 13) & 1)) >> 13;
            return (ushort)(ret | ui);
        }

        // zero or flush to 0
        if (ui < 0x33000001)
            return ret;

        // produce a denormalized half
        uint e = ui >> 23;
        int shift = (int)(0x7e - e);
        uint mm = 0x800000 | (ui & 0x7fffff);
        uint r = mm << (32 - shift);
        ret |= (ushort)(mm >> shift);
        if (r > 0x80000000 || (r == 0x80000000 && (ret & 0x1) != 0))
            ++ret;
        return ret;
    }

    // The F16C instruction (vcvtps2ph with round to nearest even) that
    // convertFloatToHalf64_f16c() uses: the same as float_to_half() except
    // that NaNs are made quiet.
    private static ushort FloatToHalfF16C(float f)
    {
        uint vi = BitConverter.SingleToUInt32Bits(f);
        if ((vi & 0x7fffffff) > 0x7f800000)
            return (ushort)(((vi >> 16) & 0x8000) | 0x7e00 | ((vi & 0x7fffff) >> 13));
        return FloatToHalf(f);
    }

    // Zig-zag index of each position of an 8x8 block (fromHalfZigZag()).
    private static readonly int[] sZigZag =
    [
        0, 1, 5, 6, 14, 15, 27, 28,
        2, 4, 7, 13, 16, 26, 29, 42,
        3, 8, 12, 17, 25, 30, 41, 43,
        9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    // The coefficients in sAvxCoef.
    private const float DctA = 3.535536e-01f, DctB = 4.903927e-01f, DctC = 4.619398e-01f, DctD = 4.157349e-01f,
                        DctE = 2.777855e-01f, DctF = 1.913422e-01f, DctG = 9.754573e-02f;

    // The row-wise 1D DCT of one row of the AVX iDCT (IDCT_AVX_SETUP_2_ROWS,
    // IDCT_AVX_MMULT_ROWS and IDCT_AVX_EO_TO_ROW_HALVES), in place.
    private static void DctInverseRowAvx(Span<float> row)
    {
        float y0 = row[0], y1 = row[1], y2 = row[2], y3 = row[3];
        float y4 = row[4], y5 = row[5], y6 = row[6], y7 = row[7];
        // M1 and M2 in column-major order.
        ReadOnlySpan<float> m1 = [DctA, DctA, DctA, DctA, DctC, DctF, -DctF, -DctC,
                                  DctA, -DctA, -DctA, DctA, DctF, -DctC, DctC, -DctF];
        ReadOnlySpan<float> m2 = [DctB, DctD, DctE, DctG, DctD, -DctG, -DctB, -DctE,
                                  DctE, -DctB, DctG, DctD, DctG, -DctE, DctD, -DctB];
        Span<float> even = stackalloc float[4], odd = stackalloc float[4];
        for (int i = 0; i < 4; ++i)
        {
            even[i] = (y0 * m1[i] + y2 * m1[4 + i]) + (y4 * m1[8 + i] + y6 * m1[12 + i]);
            odd[i] = (y1 * m2[i] + y3 * m2[4 + i]) + (y5 * m2[8 + i] + y7 * m2[12 + i]);
        }
        for (int i = 0; i < 4; ++i)
        {
            row[i] = even[i] + odd[i];
            row[7 - i] = even[i] - odd[i];
        }
    }

    // dctInverse8x8_avx_<zeroedRows>(): the last _zeroedRows_ rows are known
    // to be zero, and the instructions that only involve them are skipped.
    private static void DctInverse8x8Avx(Span<float> data, int zeroedRows)
    {
        if (zeroedRows == 7)
        {
            // dctInverse8x8_avx_7(): only the first row is nonzero.
            DctInverseRowAvx(data[..8]);
            for (int r = 1; r < 8; ++r)
                data[..8].CopyTo(data.Slice(8 * r, 8));
            for (int i = 0; i < 64; ++i)
                data[i] *= DctA;
            return;
        }

        bool R(int row) => row < 8 - zeroedRows;

        // Row pass: rows are processed in pairs (guarded by the even row).
        for (int row = 0; row < 8; row += 2)
            if (R(row))
            {
                DctInverseRowAvx(data.Slice(8 * row, 8));
                DctInverseRowAvx(data.Slice(8 * (row + 1), 8));
            }

        // Column pass
        const float a = DctA, b = DctB, c = DctC, d = DctD, e = DctE, f = DctF, g = DctG;
        for (int col = 0; col < 8; ++col)
        {
            float y0 = data[col], y1 = data[8 + col], y2 = data[16 + col], y3 = data[24 + col];
            float y4 = data[32 + col], y5 = data[40 + col], y6 = data[48 + col], y7 = data[56 + col];

            // ymm12 = b y1 + d y3 + e y5 + g y7
            float o0 = b * y1;
            if (R(3)) o0 = o0 + d * y3;
            float t14 = 0;
            if (R(5)) t14 = e * y5;
            if (R(7)) t14 = t14 + g * y7;
            if (R(5)) o0 = o0 + t14;

            // ymm13 = d y1 - g y3 - b y5 - e y7
            float o1 = d * y1;
            float u14 = 0;
            if (R(3)) u14 = g * y3;
            if (R(5)) u14 = u14 + b * y5;
            if (R(3)) o1 = o1 - u14;
            if (R(7)) o1 = o1 - e * y7;

            // ymm14 = e y1 - b y3 + g y5 + d y7
            float o2 = e * y1;
            if (R(3)) o2 = o2 - b * y3;
            if (R(5)) o2 = o2 + g * y5;
            if (R(7)) o2 = o2 + d * y7;

            // ymm15 = g y1 - e y3 + d y5 - b y7
            float o3 = g * y1;
            float v3 = 0;
            if (R(3)) v3 = e * y3;
            if (R(5)) o3 = o3 + d * y5;
            if (R(7)) v3 = v3 + b * y7;
            if (R(3)) o3 = o3 - v3;

            // Even part
            float e11 = a * y0, e1 = e11;
            if (R(4))
            {
                float a4 = a * y4;
                e11 = e11 + a4;
                e1 = e1 - a4;
            }
            float e8 = e11;
            if (R(2))
            {
                float c7 = c * y2;
                if (R(6)) c7 = c7 + f * y6;
                e8 = e8 + c7;
                e11 = e11 - c7;
            }
            float e9 = e1, e10 = e1;
            if (R(2))
            {
                float d7 = f * y2;
                if (R(6)) d7 = d7 - c * y6;
                e9 = e1 + d7;
                e10 = e1 - d7;
            }

            data[col] = e8 + o0;
            data[8 + col] = e9 + o1;
            data[16 + col] = e10 + o2;
            data[24 + col] = e11 + o3;
            data[32 + col] = e11 - o3;
            data[40 + col] = e10 - o2;
            data[48 + col] = e9 - o1;
            data[56 + col] = e8 - o0;
        }
    }

    // csc709Inverse(), Y'CbCr -> R'G'B'
    private static void Csc709Inverse(ref float comp0, ref float comp1, ref float comp2)
    {
        float s0 = comp0, s1 = comp1, s2 = comp2;
        comp0 = s0 + 1.5747f * s2;
        comp1 = s0 - 0.1873f * s1 - 0.4682f * s2;
        comp2 = s0 + 1.8556f * s1;
    }

    // One channel's rows in the output buffer.
    private sealed class DctChannel(PixelType type, int[] rows)
    {
        public readonly PixelType type = type;
        public readonly int[] rows = rows;
        public readonly float[] dctData = new float[64];
        public readonly ushort[] halfZigData = new ushort[64];
    }

    // LossyDctDecoder: decodes 1 channel, or 3 color-space converted ones.
    // Returns the number of AC and DC values used.
    private static (int acCount, int dcCount) LossyDctDecode(DctChannel[] chanData, ushort[] packedAc, int acStart,
                                                             int acEnd, ushort[] packedDc, int dcStart, long remDcCount,
                                                             ushort[]? toLinear, int width, int height, byte[] dst)
    {
        int numComp = chanData.Length;
        int numBlocksX = (width + 7) / 8, numBlocksY = (height + 7) / 8;
        int leftoverX = width - (numBlocksX - 1) * 8, leftoverY = height - (numBlocksY - 1) * 8;
        int numFullBlocksX = width / 8;
        int currAcComp = acStart;
        int packedAcCount = 0, packedDcCount = 0;

        if (remDcCount < (long)numComp * numBlocksX * numBlocksY)
            throw DwaCorrupt();

        ushort[][] rowBlock = new ushort[numComp][];
        int[] currDcComp = new int[numComp];
        for (int comp = 0; comp < numComp; ++comp)
        {
            rowBlock[comp] = new ushort[numBlocksX * 64];
            currDcComp[comp] = dcStart + comp * numBlocksX * numBlocksY;
        }

        for (int blocky = 0; blocky < numBlocksY; ++blocky)
        {
            int maxY = 8, maxX = 8;
            if (blocky == numBlocksY - 1)
                maxY = leftoverY;

            for (int blockx = 0; blockx < numBlocksX; ++blockx)
            {
                bool blockIsConstant = true;
                if (blockx == numBlocksX - 1)
                    maxX = leftoverX;

                for (int comp = 0; comp < numComp; ++comp)
                {
                    ushort[] halfZigData = chanData[comp].halfZigData;
                    float[] dctData = chanData[comp].dctData;

                    // DC component is stored separately
                    Array.Clear(halfZigData);
                    halfZigData[0] = packedDc[currDcComp[comp]++];
                    packedDcCount++;

                    // UnRLE the AC (LossyDctDecoder_unRleAc()).
                    int dctComp = 1, lastNonZero = 0;
                    while (dctComp < 64)
                    {
                        if (currAcComp >= acEnd)
                            throw DwaCorrupt();
                        ushort val = packedAc[currAcComp];
                        if (val == 0xff00)
                            // End of block
                            dctComp = 64;
                        else if ((val >> 8) == 0xff)
                            // Run detected! Insert 0's.
                            dctComp += val & 0xff;
                        else
                        {
                            // Not a run, just copy over the value
                            lastNonZero = dctComp;
                            halfZigData[dctComp] = val;
                            dctComp++;
                        }
                        packedAcCount++;
                        currAcComp++;
                    }

                    if (lastNonZero == 0)
                    {
                        // DC only case - AC components are all 0
                        float val = HalfToFloat(halfZigData[0]) * 3.535536e-01f * 3.535536e-01f;
                        Array.Fill(dctData, val);
                    }
                    else
                    {
                        // We have some AC components that are non-zero.
                        // Can't use the 'constant block' optimization
                        blockIsConstant = false;

                        // Un-Zig zag
                        for (int i = 0; i < 64; ++i)
                            dctData[i] = HalfToFloat(halfZigData[sZigZag[i]]);

                        int zeroedRows = lastNonZero < 2 ? 7 : lastNonZero < 3 ? 6 : lastNonZero < 9 ? 5 :
                                         lastNonZero < 10 ? 4 : lastNonZero < 20 ? 3 : lastNonZero < 21 ? 2 :
                                         lastNonZero < 35 ? 1 : 0;
                        DctInverse8x8Avx(dctData, zeroedRows);
                    }
                }

                // Perform the CSC
                if (numComp == 3)
                {
                    float[] c0 = chanData[0].dctData, c1 = chanData[1].dctData, c2 = chanData[2].dctData;
                    int n = blockIsConstant ? 1 : 64;
                    for (int i = 0; i < n; ++i)
                        Csc709Inverse(ref c0[i], ref c1[i], ref c2[i]);
                }

                // Float -> Half conversion. If the block has a constant
                // value, just convert the first pixel.
                for (int comp = 0; comp < numComp; ++comp)
                {
                    Span<ushort> block = rowBlock[comp].AsSpan(blockx * 64, 64);
                    float[] dctData = chanData[comp].dctData;
                    if (!blockIsConstant)
                        for (int i = 0; i < 64; ++i)
                            block[i] = FloatToHalfF16C(dctData[i]);
                    else
                        block.Fill(FloatToHalf(dctData[0]));
                }
            }

            // Unblock the data, transfer back to linear, and write the
            // results to the output rows (as halfs; FLOAT channels are
            // converted below).
            for (int comp = 0; comp < numComp; ++comp)
            {
                for (int y = 8 * blocky; y < 8 * blocky + maxY; ++y)
                {
                    int dst0 = chanData[comp].rows[y];
                    int nx = numFullBlocksX * 8 + (numFullBlocksX != numBlocksX ? maxX : 0);
                    for (int x = 0; x < nx; ++x)
                    {
                        ushort h = rowBlock[comp][(x / 8) * 64 + (y & 7) * 8 + (x & 7)];
                        if (toLinear != null)
                            h = toLinear[h];
                        BinaryPrimitives.WriteUInt16LittleEndian(dst.AsSpan(dst0 + 2 * x), h);
                    }
                }
            }
        }

        // Walk over all the channels that are of type FLOAT. Convert from
        // HALF back to FLOAT, in place in reverse.
        for (int chan = 0; chan < numComp; ++chan)
        {
            if (chanData[chan].type != PixelType.Float)
                continue;
            for (int y = 0; y < height; ++y)
            {
                int row = chanData[chan].rows[y];
                for (int x = width - 1; x >= 0; --x)
                {
                    ushort h = BinaryPrimitives.ReadUInt16LittleEndian(dst.AsSpan(row + 2 * x));
                    BinaryPrimitives.WriteSingleLittleEndian(dst.AsSpan(row + 4 * x), HalfToFloat(h));
                }
            }
        }

        return (packedAcCount, packedDcCount);
    }

    // DwaCompressor_uncompress()
    private static byte[] UndoDwa(ReadOnlySpan<byte> inPtr, int outSize, List<Channel> channels, int width,
                                  int height)
    {
        const int NUM_SIZES_SINGLE = 11;
        long headerSize = NUM_SIZES_SINGLE * 8;
        if (inPtr.Length < headerSize)
            throw DwaCorrupt();

        byte[] dst = new byte[outSize];

        ulong[] counters = new ulong[NUM_SIZES_SINGLE];
        for (int i = 0; i < NUM_SIZES_SINGLE; ++i)
            counters[i] = BinaryPrimitives.ReadUInt64LittleEndian(inPtr[(8 * i)..]);

        ulong version = counters[0];
        ulong unknownUncompressedSize = counters[1];
        ulong unknownCompressedSize = counters[2];
        ulong acCompressedSize = counters[3];
        ulong dcCompressedSize = counters[4];
        ulong rleCompressedSize = counters[5];
        ulong rleUncompressedSize = counters[6];
        ulong rleRawSize = counters[7];
        ulong totalAcUncompressedCount = counters[8];
        ulong totalDcUncompressedCount = counters[9];
        ulong acCompression = counters[10];

        ulong iSize = (ulong)inPtr.Length;
        ulong compressedSize = unknownCompressedSize + acCompressedSize + dcCompressedSize + rleCompressedSize;
        // Both the sum and individual sizes are checked in case of overflow.
        if (iSize < (ulong)headerSize + compressedSize || iSize < unknownCompressedSize ||
            iSize < acCompressedSize || iSize < dcCompressedSize || iSize < rleCompressedSize)
            throw DwaCorrupt();
        for (int i = 1; i < 10; ++i)
            if ((long)counters[i] < 0)
                throw DwaCorrupt();

        int dataPtr = (int)headerSize;
        int dataLeft = inPtr.Length - dataPtr;

        // Channel rules
        DwaClassifier[] channelRules;
        if (version < 2)
            channelRules = sLegacyChannelRules;
        else
            channelRules = ReadDwaChannelRules(inPtr, ref dataPtr, ref dataLeft);

        // Classify the channels (DwaCompressor_classifyChannels()).
        int numChannels = channels.Count;
        DwaScheme[] compression = new DwaScheme[numChannels];
        List<(string prefix, int[] idx)> prefixMap = [];
        for (int c = 0; c < numChannels; ++c)
        {
            string name = channels[c].name;
            int dot = name.LastIndexOf('.');
            string suffix = dot >= 0 ? name[(dot + 1)..] : name;
            string prefix = name[..(name.Length - suffix.Length)];
            int mi = prefixMap.FindIndex(p => p.prefix == prefix);
            if (mi < 0)
            {
                prefixMap.Add((prefix, [-1, -1, -1]));
                mi = prefixMap.Count - 1;
            }

            foreach (DwaClassifier rule in channelRules)
            {
                bool match = rule.type == channels[c].type &&
                             (rule.caseInsensitive ? string.Equals(suffix, rule.suffix,
                                                                   StringComparison.OrdinalIgnoreCase)
                                                   : suffix == rule.suffix);
                if (match)
                {
                    compression[c] = rule.scheme;
                    if (rule.cscIdx >= 0)
                        prefixMap[mi].idx[rule.cscIdx] = c;
                }
            }
        }
        // (Subsampled channels aren't supported, so all channels of a set
        // have the same sampling.)
        List<int[]> cscChannelSets = [];
        foreach (var (_, idx) in prefixMap)
            if (idx[0] >= 0 && idx[1] >= 0 && idx[2] >= 0)
                cscChannelSets.Add(idx);

        // Buffer sizes (DwaCompressor_initializeBuffers()), for the sanity
        // checks.
        ulong blocks = (ulong)MathF.Ceiling(height / 8.0f) * (ulong)MathF.Ceiling(width / 8.0f);
        ulong maxLossyDctAcSize = blocks * 63 * 2, maxLossyDctDcSize = blocks * 2;
        ulong pixelCount = (ulong)height * (ulong)width;
        ulong numLossyDctChans = 0, rleBufferSize = 0, planarUncRleSize = 0, planarUncUnknownSize = 0;
        for (int c = 0; c < numChannels; ++c)
        {
            ulong bytes = (ulong)channels[c].BytesPerSample;
            switch (compression[c])
            {
                case DwaScheme.LossyDct:
                    numLossyDctChans++;
                    break;
                case DwaScheme.Rle:
                    rleBufferSize += 2 * pixelCount * bytes;
                    planarUncRleSize += 2 * pixelCount * bytes;
                    break;
                default:
                    planarUncUnknownSize += pixelCount * bytes;
                    break;
            }
        }
        if (planarUncUnknownSize > 0)
            planarUncUnknownSize = CompressMaxBufferSize(planarUncUnknownSize);
        ulong packedAcBufferSize = maxLossyDctAcSize * numLossyDctChans;
        ulong packedDcBufferSize = maxLossyDctDcSize * numLossyDctChans;

        // UNKNOWN data is packed first, followed by the Huffman-compressed
        // AC, then the DC values, and then the zlib compressed RLE data.
        int compressedUnknownBuf = dataPtr;
        long compressedAcBuf = compressedUnknownBuf + (long)unknownCompressedSize;
        long compressedDcBuf = compressedAcBuf + (long)acCompressedSize;
        long compressedRleBuf = compressedDcBuf + (long)dcCompressedSize;
        if (compressedAcBuf > inPtr.Length || compressedDcBuf > inPtr.Length || compressedRleBuf > inPtr.Length ||
            compressedRleBuf + (long)rleCompressedSize > inPtr.Length)
            throw DwaCorrupt();

        // Sanity check that the version is something we expect.
        if (version > 2)
            throw new ExrException("unsupported DWA version");

        // Uncompress the UNKNOWN data
        byte[] planarUncUnknown = new byte[planarUncUnknownSize];
        if (unknownCompressedSize > 0)
        {
            if (unknownUncompressedSize > planarUncUnknownSize)
                throw DwaCorrupt();
            Inflate(inPtr.Slice(compressedUnknownBuf, (int)unknownCompressedSize), (int)unknownUncompressedSize)
                .CopyTo(planarUncUnknown, 0);
        }

        // Uncompress the AC data
        ushort[] packedAc = new ushort[packedAcBufferSize / 2];
        if (acCompressedSize > 0)
        {
            if (packedAcBufferSize == 0 || totalAcUncompressedCount * 2 > packedAcBufferSize)
                throw DwaCorrupt();
            ReadOnlySpan<byte> acSrc = inPtr.Slice((int)compressedAcBuf, (int)acCompressedSize);
            switch (acCompression)
            {
                case 0:  // STATIC_HUFFMAN
                {
                    ushort[] raw = new ushort[totalAcUncompressedCount];
                    Huffman.Decompress(acSrc, raw);
                    raw.CopyTo(packedAc, 0);
                    break;
                }
                case 1:  // DEFLATE
                {
                    byte[] raw = Inflate(acSrc, (int)(totalAcUncompressedCount * 2));
                    for (int i = 0; i < (int)totalAcUncompressedCount; ++i)
                        packedAc[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2 * i));
                    break;
                }
                default:
                    throw DwaCorrupt();
            }
        }

        // Uncompress the DC data
        ushort[] packedDc = new ushort[packedDcBufferSize / 2];
        if (dcCompressedSize > 0)
        {
            ulong uncompBytes = totalDcUncompressedCount * 2;
            if (uncompBytes > packedDcBufferSize)
                throw DwaCorrupt();
            byte[] dc = UnpredictAndReorder(Inflate(inPtr.Slice((int)compressedDcBuf, (int)dcCompressedSize),
                                                    (int)uncompBytes));
            for (int i = 0; i < (int)totalDcUncompressedCount; ++i)
                packedDc[i] = BinaryPrimitives.ReadUInt16LittleEndian(dc.AsSpan(2 * i));
        }
        else if (totalDcUncompressedCount != 0)
            // if the compressed size is 0, then the uncompressed size must
            // also be zero
            throw DwaCorrupt();

        // Uncompress the RLE data, then unRLE the results.
        byte[] planarUncRle = new byte[planarUncRleSize];
        if (rleRawSize > 0)
        {
            if (rleUncompressedSize > rleBufferSize || rleRawSize > planarUncRleSize)
                throw DwaCorrupt();
            byte[] rle = Inflate(inPtr.Slice((int)compressedRleBuf, (int)rleCompressedSize),
                                 (int)rleUncompressedSize);
            RleDecompress(rle, (int)rleRawSize).CopyTo(planarUncRle, 0);
        }

        // Determine the start of each row in the output buffer
        int[][] rows = new int[numChannels][];
        for (int c = 0; c < numChannels; ++c)
            rows[c] = new int[height];
        int outPos = 0;
        for (int y = 0; y < height; ++y)
            for (int c = 0; c < numChannels; ++c)
            {
                rows[c][y] = outPos;
                outPos += width * channels[c].BytesPerSample;
            }

        bool[] processed = new bool[numChannels];
        int acPos = 0, dcPos = 0;
        long remDc = (long)totalDcUncompressedCount;
        ushort[] toLinear = dwaToLinear.Value;
        // (OpenEXR passes each decoder the end of the AC data relative to
        // where that decoder starts; reading past the buffer is an error.)
        int AcEnd(int start) => (int)Math.Min((ulong)start + totalAcUncompressedCount, (ulong)packedAc.Length);

        // Decode each block of 3 channels that need to be handled together
        foreach (int[] cset in cscChannelSets)
        {
            if (compression[cset[0]] != DwaScheme.LossyDct || compression[cset[1]] != DwaScheme.LossyDct ||
                compression[cset[2]] != DwaScheme.LossyDct)
                throw DwaCorrupt();
            DctChannel[] cd = [.. cset.Select(c => new DctChannel(channels[c].type, rows[c]))];
            var (acCount, dcCount) = LossyDctDecode(cd, packedAc, acPos, AcEnd(acPos),
                                                    packedDc, dcPos, remDc, toLinear, width, height, dst);
            acPos += acCount;
            dcPos += dcCount;
            remDc -= dcCount;
            foreach (int c in cset)
                processed[c] = true;
        }

        // Handle the remaining channels by themselves
        int rlePlane = 0, unknownPos = 0;
        for (int c = 0; c < numChannels; ++c)
        {
            int pixelSize = channels[c].BytesPerSample;
            int uncSize = width * height * pixelSize;
            int rleStart = rlePlane;
            if (compression[c] == DwaScheme.Rle)
                rlePlane += uncSize;
            if (processed[c])
                continue;

            switch (compression[c])
            {
                case DwaScheme.LossyDct:
                {
                    // Setup a single-channel lossy DCT decoder pointing at
                    // the output buffer
                    DctChannel[] cd = [new DctChannel(channels[c].type, rows[c])];
                    var (acCount, dcCount) = LossyDctDecode(cd, packedAc, acPos, AcEnd(acPos), packedDc, dcPos,
                                                            remDc, channels[c].pLinear ? null : toLinear, width,
                                                            height, dst);
                    acPos += acCount;
                    dcPos += dcCount;
                    remDc -= dcCount;
                    break;
                }

                case DwaScheme.Rle:
                {
                    // The data has been un-RLE'd but is still split out by
                    // bytes: rearrange the bytes back into the right order.
                    int[] planeEnd = new int[pixelSize];
                    for (int b = 0; b < pixelSize; ++b)
                        planeEnd[b] = rleStart + b * width * height;
                    for (int y = 0; y < height; ++y)
                    {
                        int d = rows[c][y];
                        for (int x = 0; x < width; ++x)
                            for (int b = 0; b < pixelSize; ++b)
                                dst[d++] = planarUncRle[planeEnd[b]++];
                    }
                    break;
                }

                default:
                {
                    // In the UNKNOWN case, data is already in the planar
                    // buffer and just needs to copied over
                    int dstScanlineSize = width * pixelSize;
                    for (int y = 0; y < height; ++y)
                    {
                        if ((ulong)(unknownPos + dstScanlineSize) > planarUncUnknownSize)
                            throw DwaCorrupt();
                        planarUncUnknown.AsSpan(unknownPos, dstScanlineSize).CopyTo(dst.AsSpan(rows[c][y]));
                        unknownPos += dstScanlineSize;
                    }
                    break;
                }
            }
            processed[c] = true;
        }

        return dst;
    }

    // DwaCompressor_readChannelRules()
    private static DwaClassifier[] ReadDwaChannelRules(ReadOnlySpan<byte> inPtr, ref int dataPtr, ref int dataLeft)
    {
        if (dataLeft <= 2)
            throw DwaCorrupt();
        int ruleSize = BinaryPrimitives.ReadUInt16LittleEndian(inPtr[dataPtr..]);
        if (ruleSize < 2 || dataLeft < ruleSize)
            throw DwaCorrupt();

        int readPtr = dataPtr + 2;
        dataPtr += ruleSize;
        dataLeft -= ruleSize;

        List<DwaClassifier> rules = [];
        int size = ruleSize - 2;
        while (size > 0)
        {
            // Classifier_read()
            if (size <= 3)
                throw DwaCorrupt();
            int len = 0;
            for (; len < 128 + 1; ++len)
            {
                if (len > size - 3)
                    throw DwaCorrupt();
                if (inPtr[readPtr + len] == 0)
                    break;
            }
            if (len == 128 + 1)
                throw DwaCorrupt();
            string suffix = System.Text.Encoding.UTF8.GetString(inPtr.Slice(readPtr, len));
            len += 1;
            if (size < len + 2)
                throw DwaCorrupt();
            byte value = inPtr[readPtr + len], type = inPtr[readPtr + len + 1];
            readPtr += len + 2;
            size -= len + 2;

            int cscIdx = (value >> 4) - 1;
            if (cscIdx < -1 || cscIdx >= 3)
                throw DwaCorrupt();
            DwaScheme scheme = (DwaScheme)((value >> 2) & 3);
            if ((int)scheme >= 3)
                throw DwaCorrupt();
            if (type >= 3)
                throw DwaCorrupt();
            rules.Add(new DwaClassifier(suffix, scheme, (PixelType)type, cscIdx, (value & 1) != 0));
        }
        return [.. rules];
    }
}

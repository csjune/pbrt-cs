// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// OpenEXR reading and writing for ImageIO.cs, in place of the OpenEXR
// library (src/ext/openexr) that pbrt uses.
//
// Reading supports scanline and tiled images (level 0 of mipmapped/ripmapped
// ones) with NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A, DWAA and DWAB
// compression; the decompressors are ports of OpenEXR's
// (src/lib/OpenEXRCore/internal_{rle,zip,piz,huf,pxr24,b44,dwa*}.c, OpenEXR
// 3.3.1, BSD-3-Clause, Copyright Contributors to the OpenEXR Project; DWA is
// in ExrDwa.cs). Of a multi-part file, the first part is read, as pbrt's
// Imf::InputFile does. Deep images and subsampled channels are not
// supported (pbrt's Imf::InputFile can't read subsampled channels into its
// frame buffer either). Writing produces
// scanline files with ZIP compression, which is what pbrt's
// Imf::OutputFile writes by default.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Pbrt;

internal static partial class Exr
{
    public sealed class ExrException(string message) : Exception(message);

    public enum PixelType { UInt = 0, Half = 1, Float = 2 }

    public enum Compression
    {
        None = 0, RLE = 1, ZIPS = 2, ZIP = 3, PIZ = 4, PXR24 = 5, B44 = 6, B44A = 7, DWAA = 8, DWAB = 9,
    }

    public readonly record struct Channel(string name, PixelType type, bool pLinear, int xSampling, int ySampling)
    {
        public int BytesPerSample => type == PixelType.Half ? 2 : 4;
    }

    public sealed record Attribute(string name, string type, byte[] value);

    private const int Magic = 20000630;
    private const int TiledFlag = 0x200, LongNamesFlag = 0x400, NonImageFlag = 0x800, MultipartFlag = 0x1000;

    private static int LinesPerBlock(Compression c) => c switch
    {
        Compression.None or Compression.RLE or Compression.ZIPS => 1,
        Compression.ZIP or Compression.PXR24 => 16,
        Compression.DWAB => 256,
        _ => 32,  // PIZ, B44, B44A, DWAA
    };

    ///////////////////////////////////////////////////////////////////////////
    // Reading

    public sealed class File
    {
        private readonly byte[] data;
        private readonly int chunkTableStart;

        public readonly List<Attribute> attributes = [];
        public readonly List<Channel> channels = [];
        public readonly Compression compression;
        // Non-inclusive bounds (pbrt's convention); OpenEXR's are inclusive.
        public readonly Bounds2i dataWindow, displayWindow;
        private readonly bool tiled, multipart;
        private readonly int tileXSize, tileYSize;

        internal File(byte[] data)
        {
            this.data = data;
            if (data.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(data) != Magic)
                throw new ExrException("not an OpenEXR file");
            int version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            if ((version & 0xff) != 2)
                throw new ExrException($"unsupported OpenEXR version {version & 0xff}");
            multipart = (version & MultipartFlag) != 0;
            if (!multipart && (version & NonImageFlag) != 0)
                throw new ExrException("deep OpenEXR files are not supported");
            tiled = (version & TiledFlag) != 0;

            // Read the header attributes. A multi-part file has a header for
            // each part, followed by an empty one; only the first part is
            // read.
            int pos = 8;
            for (int part = 0; ; ++part)
            {
                if (multipart && part > 0 && pos < data.Length && data[pos] == 0)
                {
                    ++pos;
                    break;
                }
                while (true)
                {
                    string name = ReadNullTerminated(data, ref pos);
                    if (name.Length == 0)
                        break;
                    string type = ReadNullTerminated(data, ref pos);
                    int size = ReadInt(data, ref pos);
                    if (size < 0 || pos + size > data.Length)
                        throw new ExrException($"attribute \"{name}\" extends past the end of the file");
                    if (part == 0)
                        attributes.Add(new Attribute(name, type, data.AsSpan(pos, size).ToArray()));
                    pos += size;
                }
                if (!multipart)
                    break;
            }
            // The first part's chunk offset table comes first.
            chunkTableStart = pos;

            if (Find("type", "string") is byte[] partType)
            {
                string t = DecodeString(partType);
                if (t is "deepscanline" or "deeptile")
                    throw new ExrException("deep OpenEXR files are not supported");
                if (multipart)
                    tiled = t == "tiledimage";
            }
            else if (multipart)
                throw new ExrException("missing type attribute in multi-part file");

            byte[] value = Find("channels", "chlist") ?? throw new ExrException("missing channels attribute");
            int vpos = 0;
            while (true)
            {
                string name = ReadNullTerminated(value, ref vpos);
                if (name.Length == 0)
                    break;
                PixelType type = (PixelType)ReadInt(value, ref vpos);
                bool pLinear = value[vpos] != 0;
                vpos += 4;
                int xSampling = ReadInt(value, ref vpos), ySampling = ReadInt(value, ref vpos);
                if (type is not (PixelType.UInt or PixelType.Half or PixelType.Float))
                    throw new ExrException($"channel \"{name}\" has invalid pixel type {(int)type}");
                if (xSampling != 1 || ySampling != 1)
                    throw new ExrException($"channel \"{name}\" is subsampled, which is not supported");
                channels.Add(new Channel(name, type, pLinear, xSampling, ySampling));
            }

            value = Find("compression", "compression") ?? throw new ExrException("missing compression attribute");
            compression = (Compression)value[0];
            if (compression > Compression.DWAB)
                throw new ExrException($"unknown compression type {(int)compression}");

            dataWindow = ReadBox2i(Find("dataWindow", "box2i") ?? throw new ExrException("missing dataWindow attribute"));
            displayWindow = ReadBox2i(Find("displayWindow", "box2i") ?? throw new ExrException("missing displayWindow attribute"));
            if (dataWindow.IsEmpty())
                throw new ExrException("empty data window");

            if (tiled)
            {
                value = Find("tiles", "tiledesc") ?? throw new ExrException("missing tiles attribute");
                tileXSize = BinaryPrimitives.ReadInt32LittleEndian(value);
                tileYSize = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(4));
                if (tileXSize <= 0 || tileYSize <= 0)
                    throw new ExrException("invalid tile size");
            }
        }

        public byte[]? Find(string name, string type)
        {
            foreach (Attribute a in attributes)
                if (a.name == name && a.type == type)
                    return a.value;
            return null;
        }

        public float? FindFloat(string name) =>
            Find(name, "float") is byte[] v ? BinaryPrimitives.ReadSingleLittleEndian(v) : null;

        public int? FindInt(string name) =>
            Find(name, "int") is byte[] v ? BinaryPrimitives.ReadInt32LittleEndian(v) : null;

        public float[]? FindM44f(string name) => Find(name, "m44f") is byte[] v ? ReadFloats(v, 16) : null;

        // red, green, blue and white xy.
        public float[]? FindChromaticities() =>
            Find("chromaticities", "chromaticities") is byte[] v ? ReadFloats(v, 8) : null;

        // Reads all pixels into _dst_: channel-interleaved, channels in file
        // order, rows from dataWindow.pMin.y, samples little-endian.
        public void ReadPixels(Span<byte> dst)
        {
            int w = dataWindow.pMax.x - dataWindow.pMin.x, h = dataWindow.pMax.y - dataWindow.pMin.y;
            int pixelBytes = 0;
            int[] channelOffset = new int[channels.Count];
            for (int c = 0; c < channels.Count; ++c)
            {
                channelOffset[c] = pixelBytes;
                pixelBytes += channels[c].BytesPerSample;
            }
            if (dst.Length != (long)w * h * pixelBytes)
                throw new ExrException("pixel buffer size doesn't match the image");

            int blockW, blockH, nChunks, nx = 1;
            if (tiled)
            {
                blockW = tileXSize;
                blockH = tileYSize;
                nx = (w + blockW - 1) / blockW;
                nChunks = nx * ((h + blockH - 1) / blockH);
            }
            else
            {
                blockW = w;
                blockH = LinesPerBlock(compression);
                nChunks = (h + blockH - 1) / blockH;
            }
            if (chunkTableStart + 8L * nChunks > data.Length)
                throw new ExrException("truncated chunk offset table");

            for (int i = 0; i < nChunks; ++i)
            {
                long offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(chunkTableStart + 8 * i));
                if (offset <= 0 || offset + 8 > data.Length)
                    throw new ExrException($"invalid offset for chunk {i}");
                int pos = (int)offset;
                // Chunks of multi-part files start with the part number.
                if (multipart && ReadInt(data, ref pos) != 0)
                    throw new ExrException($"chunk {i} belongs to another part");
                int x0, y0;
                if (tiled)
                {
                    int tx = ReadInt(data, ref pos), ty = ReadInt(data, ref pos);
                    int lx = ReadInt(data, ref pos), ly = ReadInt(data, ref pos);
                    // Level 0 tiles come first in the offset table.
                    if (lx != 0 || ly != 0 || tx < 0 || ty < 0 || tx >= nx)
                        throw new ExrException($"unexpected tile ({tx}, {ty}, {lx}, {ly})");
                    x0 = tx * blockW;
                    y0 = ty * blockH;
                }
                else
                {
                    x0 = 0;
                    y0 = ReadInt(data, ref pos) - dataWindow.pMin.y;
                    if (y0 < 0 || y0 >= h || y0 % blockH != 0)
                        throw new ExrException($"invalid scanline chunk y {y0 + dataWindow.pMin.y}");
                }
                int size = ReadInt(data, ref pos);
                if (size < 0 || pos + size > data.Length)
                    throw new ExrException($"chunk {i} extends past the end of the file");

                int bw = Math.Min(blockW, w - x0), bh = Math.Min(blockH, h - y0);
                if (bw <= 0 || bh <= 0)
                    throw new ExrException($"chunk {i} is outside the data window");
                byte[] block = Decompress(data.AsSpan(pos, size), compression, channels, bw, bh,
                                          y0 + dataWindow.pMin.y);

                // Scatter the block (for each line, for each channel, bw
                // samples) into _dst_.
                int bpos = 0;
                for (int y = 0; y < bh; ++y)
                    for (int c = 0; c < channels.Count; ++c)
                    {
                        int bps = channels[c].BytesPerSample;
                        int o = ((y0 + y) * w + x0) * pixelBytes + channelOffset[c];
                        for (int x = 0; x < bw; ++x, bpos += bps, o += pixelBytes)
                            block.AsSpan(bpos, bps).CopyTo(dst.Slice(o, bps));
                    }
            }
        }
    }

    public static File Read(byte[] data)
    {
        try
        {
            return new File(data);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new ExrException("truncated or corrupt header");
        }
    }

    public static string DecodeString(byte[] v) => Encoding.UTF8.GetString(v);

    public static List<string> DecodeStringVector(byte[] v)
    {
        List<string> result = [];
        int pos = 0;
        while (pos + 4 <= v.Length)
        {
            int n = ReadInt(v, ref pos);
            if (n < 0 || pos + n > v.Length)
                throw new ExrException("invalid string vector attribute");
            result.Add(Encoding.UTF8.GetString(v, pos, n));
            pos += n;
        }
        return result;
    }

    private static string ReadNullTerminated(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0)
            ++pos;
        if (pos >= data.Length)
            throw new ExrException("truncated header");
        string s = Encoding.UTF8.GetString(data, start, pos - start);
        ++pos;
        return s;
    }

    private static int ReadInt(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
            throw new ExrException("truncated file");
        int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        return v;
    }

    private static float[] ReadFloats(byte[] v, int n)
    {
        if (v.Length < 4 * n)
            throw new ExrException("attribute is too small");
        float[] f = new float[n];
        for (int i = 0; i < n; ++i)
            f[i] = BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(4 * i));
        return f;
    }

    private static Bounds2i ReadBox2i(byte[] v)
    {
        if (v.Length < 16)
            throw new ExrException("box2i attribute is too small");
        int xMin = BinaryPrimitives.ReadInt32LittleEndian(v), yMin = BinaryPrimitives.ReadInt32LittleEndian(v.AsSpan(4));
        int xMax = BinaryPrimitives.ReadInt32LittleEndian(v.AsSpan(8)), yMax = BinaryPrimitives.ReadInt32LittleEndian(v.AsSpan(12));
        return new Bounds2i(new Point2i(xMin, yMin), new Point2i(xMax + 1, yMax + 1));
    }

    ///////////////////////////////////////////////////////////////////////////
    // Decompression

    // Returns the uncompressed block: for each of the _height_ lines, for
    // each channel, _width_ little-endian samples.
    private static byte[] Decompress(ReadOnlySpan<byte> src, Compression compression, List<Channel> channels,
                                     int width, int height, int startY)
    {
        int lineBytes = 0;
        foreach (Channel c in channels)
            lineBytes += width * c.BytesPerSample;
        int outSize = lineBytes * height;

        // Chunks that don't compress are stored as is.
        if (compression == Compression.None || src.Length == outSize)
        {
            if (src.Length != outSize)
                throw new ExrException("uncompressed chunk has the wrong size");
            return src.ToArray();
        }

        byte[] result = compression switch
        {
            Compression.RLE => UndoRLE(src, outSize),
            Compression.ZIPS or Compression.ZIP => UndoZip(src, outSize),
            Compression.PIZ => UndoPiz(src, outSize, channels, width, height),
            Compression.PXR24 => UndoPxr24(src, outSize, channels, width, height),
            Compression.B44 or Compression.B44A => UndoB44(src, outSize, channels, width, height),
            Compression.DWAA or Compression.DWAB => UndoDwa(src, outSize, channels, width, height),
            _ => throw new ExrException($"{compression} compression is not supported"),
        };
        if (result.Length != outSize)
            throw new ExrException("corrupt chunk");
        return result;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> src, int expectedSize)
    {
        try
        {
            using MemoryStream input = new(src.ToArray());
            using ZLibStream z = new(input, CompressionMode.Decompress);
            byte[] result = new byte[expectedSize];
            int n = 0;
            while (n < expectedSize)
            {
                int r = z.Read(result, n, expectedSize - n);
                if (r == 0)
                    break;
                n += r;
            }
            if (n != expectedSize)
                throw new ExrException("corrupt chunk: zlib data is too short");
            return result;
        }
        catch (InvalidDataException e)
        {
            throw new ExrException($"corrupt chunk: {e.Message}");
        }
    }

    // internal_rle_decompress()
    private static byte[] RleDecompress(ReadOnlySpan<byte> src, int outsz)
    {
        byte[] dst = new byte[outsz];
        int @in = 0, outbytes = 0;
        while (@in < src.Length)
        {
            sbyte n = (sbyte)src[@in];
            if (n < 0)
            {
                int count = -n;
                ++@in;
                if (@in + count > src.Length || outbytes + count > outsz)
                    throw new ExrException("corrupt RLE chunk");
                src.Slice(@in, count).CopyTo(dst.AsSpan(outbytes));
                @in += count;
                outbytes += count;
            }
            else
            {
                int count = n + 1;
                if (@in + 2 > src.Length || outbytes + count > outsz)
                    throw new ExrException("corrupt RLE chunk");
                dst.AsSpan(outbytes, count).Fill(src[@in + 1]);
                outbytes += count;
                @in += 2;
            }
        }
        if (outbytes != outsz)
            throw new ExrException("corrupt RLE chunk");
        return dst;
    }

    // unpredict_and_reorder() / reconstruct() + interleave(): undoes the
    // byte predictor and the split into even and odd bytes.
    private static byte[] UnpredictAndReorder(byte[] t)
    {
        for (int i = 1; i < t.Length; ++i)
            t[i] = (byte)(t[i - 1] + t[i] - 128);

        byte[] @out = new byte[t.Length];
        int t1 = 0, t2 = (t.Length + 1) / 2, s = 0;
        while (s < t.Length)
        {
            @out[s++] = t[t1++];
            if (s < t.Length)
                @out[s++] = t[t2++];
        }
        return @out;
    }

    private static byte[] UndoRLE(ReadOnlySpan<byte> src, int outSize) =>
        UnpredictAndReorder(RleDecompress(src, outSize));

    private static byte[] UndoZip(ReadOnlySpan<byte> src, int outSize) =>
        UnpredictAndReorder(Inflate(src, outSize));

    // PIZ: wavelet transform + Huffman coding (internal_piz.c)
    private const int USHORT_RANGE = 1 << 16;
    private const int BITMAP_SIZE = USHORT_RANGE >> 3;

    private static ushort ReverseLutFromBitmap(ReadOnlySpan<byte> bitmap, ushort[] lut)
    {
        int k = 0;
        for (int i = 0; i < USHORT_RANGE; ++i)
        {
            if ((i == 0) || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                lut[k++] = (ushort)i;
        }
        int n = k - 1;
        while (k < USHORT_RANGE)
            lut[k++] = 0;
        return (ushort)n;
    }

    private static void wdec14(ushort l, ushort h, out ushort a, out ushort b)
    {
        int hi = (short)h;
        int li = (short)l;
        int ai = li + (hi & 1) + (hi >> 1);
        a = (ushort)(short)ai;
        b = (ushort)(short)(ai - hi);
    }

    private const int A_OFFSET = 1 << 15;
    private const int MOD_MASK = (1 << 16) - 1;

    private static void wdec16(ushort l, ushort h, out ushort a, out ushort b)
    {
        int m = l;
        int d = h;
        int bb = (m - (d >> 1)) & MOD_MASK;
        int aa = (d + bb - A_OFFSET) & MOD_MASK;
        b = (ushort)bb;
        a = (ushort)aa;
    }

    // wav_2D_decode(): _in_ is indexed from _i0_ with strides _ox_ and _oy_.
    private static void Wav2DDecode(ushort[] @in, int i0, int nx, int ox, int ny, int oy, ushort mx)
    {
        bool w14 = mx < (1 << 14);
        int n = (nx > ny) ? ny : nx;
        int p = 1;
        int p2;

        // Search max level
        while (p <= n)
            p <<= 1;
        p >>= 1;
        p2 = p;
        p >>= 1;

        // Hierarchical loop on smaller dimension n
        while (p >= 1)
        {
            int py = i0;
            int ey = i0 + oy * (ny - p2);
            int oy1 = oy * p;
            int oy2 = oy * p2;
            int ox1 = ox * p;
            int ox2 = ox * p2;
            ushort i00, i01, i10, i11;

            // Y loop
            for (; py <= ey; py += oy2)
            {
                int px = py;
                int ex = py + ox * (nx - p2);

                // X loop
                for (; px <= ex; px += ox2)
                {
                    int p01 = px + ox1;
                    int p10 = px + oy1;
                    int p11 = p10 + ox1;

                    // 2D wavelet decoding
                    if (w14)
                    {
                        // wdec14_4()
                        int a = (short)@in[px], b = (short)@in[p10], c = (short)@in[p01], d = (short)@in[p11];
                        int ii00 = a + (b & 1) + (b >> 1);
                        int ii10 = ii00 - b;
                        int ii01 = c + (d & 1) + (d >> 1);
                        int ii11 = ii01 - d;
                        a = ii00 + (ii01 & 1) + (ii01 >> 1);
                        b = a - ii01;
                        c = ii10 + (ii11 & 1) + (ii11 >> 1);
                        d = c - ii11;
                        @in[px] = (ushort)a;
                        @in[p01] = (ushort)b;
                        @in[p10] = (ushort)c;
                        @in[p11] = (ushort)d;
                    }
                    else
                    {
                        wdec16(@in[px], @in[p10], out i00, out i10);
                        wdec16(@in[p01], @in[p11], out i01, out i11);
                        wdec16(i00, i01, out @in[px], out @in[p01]);
                        wdec16(i10, i11, out @in[p10], out @in[p11]);
                    }
                }

                // Decode (1D) odd column (still in Y loop)
                if ((nx & p) != 0)
                {
                    int p10 = px + oy1;
                    if (w14)
                        wdec14(@in[px], @in[p10], out i00, out @in[p10]);
                    else
                        wdec16(@in[px], @in[p10], out i00, out @in[p10]);
                    @in[px] = i00;
                }
            }

            // Decode (1D) odd line (must loop in X)
            if ((ny & p) != 0)
            {
                int px = py;
                int ex = py + ox * (nx - p2);

                for (; px <= ex; px += ox2)
                {
                    int p01 = px + ox1;
                    if (w14)
                        wdec14(@in[px], @in[p01], out i00, out @in[p01]);
                    else
                        wdec16(@in[px], @in[p01], out i00, out @in[p01]);
                    @in[px] = i00;
                }
            }

            // Next level
            p2 = p;
            p >>= 1;
        }
    }

    private static byte[] UndoPiz(ReadOnlySpan<byte> packed, int outsz, List<Channel> channels, int width, int height)
    {
        // Read range compression data
        byte[] bitmap = new byte[BITMAP_SIZE];
        int nBytes = 0;
        if (4 > packed.Length)
            throw new ExrException("corrupt PIZ chunk");
        ushort minNonZero = BinaryPrimitives.ReadUInt16LittleEndian(packed);
        ushort maxNonZero = BinaryPrimitives.ReadUInt16LittleEndian(packed[2..]);
        nBytes += 4;
        if (maxNonZero >= BITMAP_SIZE)
            throw new ExrException("corrupt PIZ chunk");
        if (minNonZero <= maxNonZero)
        {
            int bytesToRead = maxNonZero - minNonZero + 1;
            if (nBytes + bytesToRead > packed.Length)
                throw new ExrException("corrupt PIZ chunk");
            packed.Slice(nBytes, bytesToRead).CopyTo(bitmap.AsSpan(minNonZero));
            nBytes += bytesToRead;
        }

        ushort[] lut = new ushort[USHORT_RANGE];
        ushort maxValue = ReverseLutFromBitmap(bitmap, lut);

        // Huffman decoding
        if (nBytes + 4 > packed.Length)
            throw new ExrException("corrupt PIZ chunk");
        int hufbytes = BinaryPrimitives.ReadInt32LittleEndian(packed[nBytes..]);
        nBytes += 4;
        if (hufbytes < 0 || nBytes + hufbytes > packed.Length)
            throw new ExrException("corrupt PIZ chunk");

        ushort[] wavbuf = new ushort[outsz / 2];
        Huffman.Decompress(packed.Slice(nBytes, hufbytes), wavbuf);

        // Wavelet decoding
        int wpos = 0;
        foreach (Channel c in channels)
        {
            int wcount = c.BytesPerSample / 2;
            for (int j = 0; j < wcount; ++j)
                Wav2DDecode(wavbuf, wpos + j, width, wcount, height, wcount * width, maxValue);
            wpos += width * height * wcount;
        }

        // Expand the pixel data to their original range
        for (int i = 0; i < wavbuf.Length; ++i)
            wavbuf[i] = lut[wavbuf[i]];

        // Rearrange the pixel data into the format expected by the caller:
        // wavbuf has each channel's full block in turn.
        byte[] @out = new byte[outsz];
        int o = 0;
        for (int y = 0; y < height; ++y)
        {
            int scratch = 0;
            foreach (Channel c in channels)
            {
                int n = width * (c.BytesPerSample / 2);  // ushorts per line
                int tmp = scratch + y * n;
                for (int i = 0; i < n; ++i, o += 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(@out.AsSpan(o), wavbuf[tmp + i]);
                scratch += height * n;
            }
        }
        return @out;
    }

    // The classic OpenEXR Huffman decoder (internal_huf.c).
    private static class Huffman
    {
        private const int HUF_ENCBITS = 16;
        private const int HUF_DECBITS = 14;

        private const int HUF_ENCSIZE = (1 << HUF_ENCBITS) + 1;
        private const int HUF_DECSIZE = 1 << HUF_DECBITS;
        private const int HUF_DECMASK = HUF_DECSIZE - 1;

        private const int SHORT_ZEROCODE_RUN = 59;
        private const int LONG_ZEROCODE_RUN = 63;
        private const int SHORTEST_LONG_RUN = 2 + LONG_ZEROCODE_RUN - SHORT_ZEROCODE_RUN;

        private struct HufDec
        {
            public int len;
            public uint lit;
            public List<uint>? p;
        }

        private static int hufLength(ulong code) => (int)(code & 63);

        private static ulong hufCode(ulong code) => code >> 6;

        private ref struct BitReader
        {
            public ReadOnlySpan<byte> data;
            public int pos;
            public ulong c;
            public int lc;
        }

        private static ulong getBits(int nBits, ref BitReader r)
        {
            while (r.lc < nBits)
            {
                if (r.pos >= r.data.Length)
                    throw new ExrException("corrupt Huffman table");
                r.c = (r.c << 8) | r.data[r.pos++];
                r.lc += 8;
            }
            r.lc -= nBits;
            return (r.c >> r.lc) & ((1UL << nBits) - 1);
        }

        // Build a "canonical" Huffman code table:
        //	- for each (uncompressed) symbol, hcode contains the length
        //	  of the corresponding code (in the compressed data)
        //	- canonical codes are computed and stored in hcode
        //	- the rules for constructing canonical codes are as follows:
        //	  * shorter codes (if filled with zeroes to the right)
        //	    have a numerically higher value than longer codes
        //	  * for codes with the same length, numerical values
        //	    increase with numerical symbol values
        //	- because the canonical code table can be constructed from
        //	  symbol lengths alone, the code table can be transmitted
        //	  without sending the actual code values
        //	- see http://www.compressconsult.com/huffman/
        private static void hufCanonicalCodeTable(ulong[] hcode)
        {
            ulong[] n = new ulong[59];

            // For each i from 0 through 58, count the
            // number of different codes of length i, and
            // store the count in n[i].
            for (int i = 0; i < HUF_ENCSIZE; ++i)
                n[hcode[i]] += 1;

            // For each i from 58 through 1, compute the
            // numerically lowest code with length i, and
            // store that code in n[i].
            ulong c = 0;
            for (int i = 58; i > 0; --i)
            {
                ulong nc = ((c + n[i]) >> 1);
                n[i] = c;
                c = nc;
            }

            // hcode[i] contains the length, l, of the
            // code for symbol i.  Assign the next available
            // code of length l to the symbol and store both
            // l and the code in hcode[i].
            for (int i = 0; i < HUF_ENCSIZE; ++i)
            {
                ulong l = hcode[i];
                if (l > 0)
                    hcode[i] = l | (n[l]++ << 6);
            }
        }

        private static void hufUnpackEncTable(ref BitReader r, uint im, uint iM, ulong[] hcode)
        {
            for (; im <= iM; im++)
            {
                ulong l = hcode[im] = getBits(6, ref r);  // code length

                if (l == LONG_ZEROCODE_RUN)
                {
                    ulong zerun = getBits(8, ref r) + SHORTEST_LONG_RUN;

                    if (im + zerun > iM + 1)
                        throw new ExrException("corrupt Huffman table");

                    while (zerun-- > 0)
                        hcode[im++] = 0;

                    im--;
                }
                else if (l >= SHORT_ZEROCODE_RUN)
                {
                    ulong zerun = l - SHORT_ZEROCODE_RUN + 2;

                    if (im + zerun > iM + 1)
                        throw new ExrException("corrupt Huffman table");

                    while (zerun-- > 0)
                        hcode[im++] = 0;

                    im--;
                }
            }

            hufCanonicalCodeTable(hcode);
        }

        // Build a decoding hash table based on the encoding table hcode:
        //	- short codes (<= HUF_DECBITS) are resolved with a single table access;
        //	- long code entry allocations are not optimized, because long codes are
        //	  unfrequent;
        //	- decoding tables are used by hufDecode();
        private static void hufBuildDecTable(ulong[] hcode, uint im, uint iM, HufDec[] hdecod)
        {
            for (; im <= iM; im++)
            {
                ulong c = hufCode(hcode[im]);
                int l = hufLength(hcode[im]);

                if ((c >> l) != 0)
                    // Error: c is supposed to be an l-bit code,
                    // but c contains a value that is greater
                    // than the largest l-bit number.
                    throw new ExrException("corrupt Huffman table");

                if (l > HUF_DECBITS)
                {
                    // Long code: add a secondary entry
                    ref HufDec pl = ref hdecod[c >> (l - HUF_DECBITS)];

                    if (pl.len != 0)
                        // Error: a short code has already
                        // been stored in table entry *pl.
                        throw new ExrException("corrupt Huffman table");

                    pl.lit++;
                    pl.p ??= [];
                    pl.p.Add(im);
                }
                else if (l != 0)
                {
                    // Short code: init all primary entries
                    int pl = (int)(c << (HUF_DECBITS - l));

                    for (ulong i = 1UL << (HUF_DECBITS - l); i > 0; i--, pl++)
                    {
                        if (hdecod[pl].len != 0 || hdecod[pl].p != null)
                            // Error: a short code or a long code has
                            // already been stored in table entry *pl.
                            throw new ExrException("corrupt Huffman table");

                        hdecod[pl].len = l;
                        hdecod[pl].lit = im;
                    }
                }
            }
        }

        // getCode(): outputs symbol _po_, or a run of the previous symbol if
        // _po_ is the run-length code.
        private static void getCode(uint po, uint rlc, ref BitReader r, ReadOnlySpan<byte> @in, int ie,
                                    ushort[] @out, ref int o)
        {
            if (po == rlc)
            {
                if (r.lc < 8)
                {
                    if (r.pos >= ie)
                        throw new ExrException("corrupt Huffman data");
                    r.c = (r.c << 8) | @in[r.pos++];
                    r.lc += 8;
                }

                r.lc -= 8;

                byte cs = (byte)(r.c >> r.lc);

                if (o + cs > @out.Length)
                    throw new ExrException("corrupt Huffman data");
                else if (o - 1 < 0)
                    throw new ExrException("corrupt Huffman data");

                ushort s = @out[o - 1];

                while (cs-- > 0)
                    @out[o++] = s;
            }
            else if (o < @out.Length)
                @out[o++] = (ushort)po;
            else
                throw new ExrException("corrupt Huffman data");
        }

        // Decode (uncompress) ni bits based on encoding & decoding tables:
        private static void hufDecode(ulong[] hcode, HufDec[] hdecod, ReadOnlySpan<byte> @in, ulong ni,
                                      uint rlc, ushort[] @out)
        {
            BitReader r = new() { data = @in, pos = 0, c = 0, lc = 0 };
            int o = 0;
            int ie = (int)((ni + 7) / 8);  // input byte size

            // Loop on input bytes
            while (r.pos < ie)
            {
                r.c = (r.c << 8) | @in[r.pos++];
                r.lc += 8;

                // Access decoding table
                while (r.lc >= HUF_DECBITS)
                {
                    ulong decoffset = (r.c >> (r.lc - HUF_DECBITS)) & HUF_DECMASK;
                    ref HufDec pl = ref hdecod[decoffset];

                    if (pl.len != 0)
                    {
                        // Get short code
                        if (pl.len > r.lc)
                            throw new ExrException("corrupt Huffman data");

                        r.lc -= pl.len;
                        getCode(pl.lit, rlc, ref r, @in, ie, @out, ref o);
                    }
                    else
                    {
                        if (pl.p == null)
                            throw new ExrException("corrupt Huffman data");  // wrong code
                        List<uint> decbuf = pl.p;

                        // Search long code
                        int j;
                        for (j = 0; j < pl.lit; j++)
                        {
                            int l = hufLength(hcode[decbuf[j]]);

                            while (r.lc < l && r.pos < ie)  // get more bits
                            {
                                r.c = (r.c << 8) | @in[r.pos++];
                                r.lc += 8;
                            }

                            if (r.lc >= l)
                            {
                                if (hufCode(hcode[decbuf[j]]) == ((r.c >> (r.lc - l)) & ((1UL << l) - 1)))
                                {
                                    // Found : get long code
                                    r.lc -= l;
                                    getCode(decbuf[j], rlc, ref r, @in, ie, @out, ref o);
                                    break;
                                }
                            }
                        }

                        if (j == pl.lit)
                            throw new ExrException("corrupt Huffman data");
                    }
                }
            }

            // Get remaining (short) codes
            int i = (int)((8 - ni) & 7);
            r.c >>= i;
            r.lc -= i;

            while (r.lc > 0)
            {
                ulong decoffset = (r.c << (HUF_DECBITS - r.lc)) & HUF_DECMASK;
                ref HufDec pl = ref hdecod[decoffset];

                if (pl.len != 0)
                {
                    if (pl.len > r.lc)
                        throw new ExrException("corrupt Huffman data");
                    r.lc -= pl.len;
                    getCode(pl.lit, rlc, ref r, @in, ie, @out, ref o);
                }
                else
                    throw new ExrException("corrupt Huffman data");
            }

            if (o != @out.Length)
                throw new ExrException("corrupt Huffman data");
        }

        // internal_huf_decompress()
        public static void Decompress(ReadOnlySpan<byte> compressed, ushort[] raw)
        {
            // need at least 20 bytes for header
            if (compressed.Length < 20)
            {
                if (raw.Length != 0)
                    throw new ExrException("corrupt Huffman data");
                return;
            }

            uint im = BinaryPrimitives.ReadUInt32LittleEndian(compressed);
            uint iM = BinaryPrimitives.ReadUInt32LittleEndian(compressed[4..]);
            // uint tableLength = readUInt (compressed + 8);
            uint nBits = BinaryPrimitives.ReadUInt32LittleEndian(compressed[12..]);
            // uint future = readUInt (compressed + 16);

            if (im >= HUF_ENCSIZE || iM >= HUF_ENCSIZE)
                throw new ExrException("corrupt Huffman data");

            const int hufInfoBlockSize = 20;
            ulong nBytes = (nBits + 7UL) / 8;

            // must be nBytes remaining in buffer
            if (hufInfoBlockSize + nBytes > (ulong)compressed.Length)
                throw new ExrException("corrupt Huffman data");

            ulong[] freq = new ulong[HUF_ENCSIZE];
            HufDec[] hdec = new HufDec[HUF_DECSIZE];
            BitReader r = new() { data = compressed[hufInfoBlockSize..], pos = 0, c = 0, lc = 0 };
            hufUnpackEncTable(ref r, im, iM, freq);
            int tableBytes = r.pos;
            ulong nLeft = (ulong)(compressed.Length - hufInfoBlockSize - tableBytes);

            if (nBits > 8 * nLeft)
                throw new ExrException("corrupt Huffman data");

            hufBuildDecTable(freq, im, iM, hdec);
            hufDecode(freq, hdec, compressed[(hufInfoBlockSize + tableBytes)..], nBits, iM, raw);
        }
    }

    // PXR24 (internal_pxr24.c)
    private static byte[] UndoPxr24(ReadOnlySpan<byte> src, int outSize, List<Channel> channels, int width, int height)
    {
        // The compressed data is smaller than outSize for FLOAT channels.
        int decSize = 0;
        foreach (Channel c in channels)
            decSize += width * height * (c.type == PixelType.Float ? 3 : c.BytesPerSample);
        byte[] scratch = Inflate(src, decSize);

        byte[] @out = new byte[outSize];
        int o = 0, lastIn = 0;
        for (int y = 0; y < height; ++y)
        {
            foreach (Channel c in channels)
            {
                int w = width;
                switch (c.type)
                {
                    case PixelType.UInt:
                    {
                        int p0 = lastIn, p1 = p0 + w, p2 = p1 + w, p3 = p2 + w;
                        lastIn += 4 * w;
                        uint pixel = 0;
                        for (int x = 0; x < w; ++x, o += 4)
                        {
                            uint diff = ((uint)scratch[p0++] << 24) | ((uint)scratch[p1++] << 16) |
                                        ((uint)scratch[p2++] << 8) | scratch[p3++];
                            pixel += diff;
                            BinaryPrimitives.WriteUInt32LittleEndian(@out.AsSpan(o), pixel);
                        }
                        break;
                    }
                    case PixelType.Half:
                    {
                        int p0 = lastIn, p1 = p0 + w;
                        lastIn += 2 * w;
                        uint pixel = 0;
                        for (int x = 0; x < w; ++x, o += 2)
                        {
                            uint diff = ((uint)scratch[p0++] << 8) | scratch[p1++];
                            pixel += diff;
                            BinaryPrimitives.WriteUInt16LittleEndian(@out.AsSpan(o), (ushort)pixel);
                        }
                        break;
                    }
                    case PixelType.Float:
                    {
                        int p0 = lastIn, p1 = p0 + w, p2 = p1 + w;
                        lastIn += 3 * w;
                        uint pixel = 0;
                        for (int x = 0; x < w; ++x, o += 4)
                        {
                            uint diff = ((uint)scratch[p0++] << 24) | ((uint)scratch[p1++] << 16) |
                                        ((uint)scratch[p2++] << 8);
                            pixel += diff;
                            BinaryPrimitives.WriteUInt32LittleEndian(@out.AsSpan(o), pixel);
                        }
                        break;
                    }
                }
            }
        }
        return @out;
    }

    // B44 and B44A (internal_b44.c)
    private static void unpack14(ReadOnlySpan<byte> b, Span<ushort> s)
    {
        s[0] = (ushort)((b[0] << 8) | b[1]);

        int shift = (b[2] >> 2);
        uint bias = (uint)(0x20u << shift);

        s[4] = (ushort)(s[0] + (((((uint)b[2] << 4) | ((uint)b[3] >> 4)) & 0x3fu) << shift) - bias);
        s[8] = (ushort)(s[4] + (((((uint)b[3] << 2) | ((uint)b[4] >> 6)) & 0x3fu) << shift) - bias);
        s[12] = (ushort)(s[8] + (((uint)b[4] & 0x3fu) << shift) - bias);

        s[1] = (ushort)(s[0] + (((uint)b[5] >> 2) << shift) - bias);
        s[5] = (ushort)(s[4] + (((((uint)b[5] << 4) | ((uint)b[6] >> 4)) & 0x3fu) << shift) - bias);
        s[9] = (ushort)(s[8] + (((((uint)b[6] << 2) | ((uint)b[7] >> 6)) & 0x3fu) << shift) - bias);
        s[13] = (ushort)(s[12] + (((uint)b[7] & 0x3fu) << shift) - bias);

        s[2] = (ushort)(s[1] + (((uint)b[8] >> 2) << shift) - bias);
        s[6] = (ushort)(s[5] + (((((uint)b[8] << 4) | ((uint)b[9] >> 4)) & 0x3fu) << shift) - bias);
        s[10] = (ushort)(s[9] + (((((uint)b[9] << 2) | ((uint)b[10] >> 6)) & 0x3fu) << shift) - bias);
        s[14] = (ushort)(s[13] + (((uint)b[10] & 0x3fu) << shift) - bias);

        s[3] = (ushort)(s[2] + (((uint)b[11] >> 2) << shift) - bias);
        s[7] = (ushort)(s[6] + (((((uint)b[11] << 4) | ((uint)b[12] >> 4)) & 0x3fu) << shift) - bias);
        s[11] = (ushort)(s[10] + (((((uint)b[12] << 2) | ((uint)b[13] >> 6)) & 0x3fu) << shift) - bias);
        s[15] = (ushort)(s[14] + (((uint)b[13] & 0x3fu) << shift) - bias);

        for (int i = 0; i < 16; ++i)
        {
            if ((s[i] & 0x8000) != 0)
                s[i] &= 0x7fff;
            else
                s[i] = (ushort)~s[i];
        }
    }

    private static void unpack3(ReadOnlySpan<byte> b, Span<ushort> s)
    {
        s[0] = (ushort)((b[0] << 8) | b[1]);

        if ((s[0] & 0x8000) != 0)
            s[0] &= 0x7fff;
        else
            s[0] = (ushort)~s[0];

        for (int i = 1; i < 16; ++i)
            s[i] = s[0];
    }

    // The logTable that undoes B44's encoding of perceptually linear
    // channels, x -> 8 log(x) (written by tools/gen-b44-table.py from
    // internal_b44_table.c)
    private static readonly Lazy<ushort[]> b44LogTable = new(() =>
    {
        const string name = "Pbrt.Data.B44LogTable.bin";
        using Stream stream = typeof(Exr).Assembly.GetManifestResourceStream(name) ??
                              throw new InvalidOperationException($"missing resource {name}");
        byte[] bytes = new byte[65536 * 2];
        stream.ReadExactly(bytes);
        ushort[] table = new ushort[65536];
        for (int i = 0; i < 65536; ++i)
            table[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i));
        return table;
    });

    private static void convertToLinear(Span<ushort> s)
    {
        ushort[] logTable = b44LogTable.Value;
        for (int i = 0; i < 16; ++i)
            s[i] = logTable[s[i]];
    }

    private static byte[] UndoB44(ReadOnlySpan<byte> @in, int outSize, List<Channel> channels, int width, int height)
    {
        // Decode each channel's block into _scratch_ (channel after channel).
        byte[] scratch = new byte[outSize];
        int sc = 0, bIn = 0;
        Span<ushort> s = stackalloc ushort[16];
        foreach (Channel c in channels)
        {
            int nx = width, ny = height;
            int nBytes = ny * nx * c.BytesPerSample;

            if (c.type != PixelType.Half)
            {
                if (bIn + nBytes > @in.Length)
                    throw new ExrException("corrupt B44 chunk");
                @in.Slice(bIn, nBytes).CopyTo(scratch.AsSpan(sc));
                bIn += nBytes;
                sc += nBytes;
                continue;
            }

            for (int y = 0; y < ny; y += 4)
            {
                for (int x = 0; x < nx; x += 4)
                {
                    if (bIn + 3 > @in.Length)
                        throw new ExrException("corrupt B44 chunk");

                    // check if 3-byte encoded flat field
                    if (@in[bIn + 2] >= (13 << 2))
                    {
                        unpack3(@in.Slice(bIn, 3), s);
                        bIn += 3;
                    }
                    else
                    {
                        if (bIn + 14 > @in.Length)
                            throw new ExrException("corrupt B44 chunk");
                        unpack14(@in.Slice(bIn, 14), s);
                        bIn += 14;
                    }

                    if (c.pLinear)
                        convertToLinear(s);

                    int n = (x + 3 < nx) ? 4 : nx - x;
                    for (int row = 0; row < 4 && y + row < ny; ++row)
                        for (int i = 0; i < n; ++i)
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                scratch.AsSpan(sc + 2 * ((y + row) * nx + x + i)), s[4 * row + i]);
                }
            }
            sc += nBytes;
        }

        // now put it back so each scanline has channel data
        byte[] @out = new byte[outSize];
        int o = 0;
        for (int y = 0; y < height; ++y)
        {
            int scr = 0;
            foreach (Channel c in channels)
            {
                int bpl = width * c.BytesPerSample;
                scratch.AsSpan(scr + y * bpl, bpl).CopyTo(@out.AsSpan(o));
                o += bpl;
                scr += height * bpl;
            }
        }
        return @out;
    }

    ///////////////////////////////////////////////////////////////////////////
    // Writing

    public sealed class Writer
    {
        private readonly Bounds2i displayWindow, dataWindow;
        private readonly PixelType type;
        private readonly string[] channelNames;
        private readonly List<Attribute> attributes = [];

        // _channelNames_ is the order of the channels in the pixels passed
        // to Write().
        public Writer(Bounds2i displayWindow, Bounds2i dataWindow, PixelType type, string[] channelNames)
        {
            CHECK(type == PixelType.Half || type == PixelType.Float);
            this.displayWindow = displayWindow;
            this.dataWindow = dataWindow;
            this.type = type;
            this.channelNames = channelNames;
        }

        private void Insert(string name, string type, byte[] value)
        {
            attributes.RemoveAll(a => a.name == name);
            attributes.Add(new Attribute(name, type, value));
        }

        private static byte[] Floats(ReadOnlySpan<float> f)
        {
            byte[] v = new byte[4 * f.Length];
            for (int i = 0; i < f.Length; ++i)
                BinaryPrimitives.WriteSingleLittleEndian(v.AsSpan(4 * i), f[i]);
            return v;
        }

        private static byte[] Int(int i)
        {
            byte[] v = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(v, i);
            return v;
        }

        private static byte[] Box2i(Bounds2i b)
        {
            // OpenEXR's bounds are inclusive.
            byte[] v = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(v, b.pMin.x);
            BinaryPrimitives.WriteInt32LittleEndian(v.AsSpan(4), b.pMin.y);
            BinaryPrimitives.WriteInt32LittleEndian(v.AsSpan(8), b.pMax.x - 1);
            BinaryPrimitives.WriteInt32LittleEndian(v.AsSpan(12), b.pMax.y - 1);
            return v;
        }

        public void InsertFloat(string name, float v) => Insert(name, "float", Floats([v]));

        public void InsertInt(string name, int v) => Insert(name, "int", Int(v));

        public void InsertM44f(string name, SquareMatrix4 m)
        {
            float[] f = new float[16];
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    f[4 * i + j] = (float)m[i, j];
            Insert(name, "m44f", Floats(f));
        }

        public void InsertString(string name, string s) => Insert(name, "string", Encoding.UTF8.GetBytes(s));

        public void InsertStringVector(string name, List<string> sv)
        {
            using MemoryStream m = new();
            foreach (string s in sv)
            {
                byte[] b = Encoding.UTF8.GetBytes(s);
                m.Write(Int(b.Length));
                m.Write(b);
            }
            Insert(name, "stringvector", m.ToArray());
        }

        public void InsertChromaticities(Point2f r, Point2f g, Point2f b, Point2f w) =>
            Insert("chromaticities", "chromaticities", Floats([(float)r.x, (float)r.y, (float)g.x, (float)g.y, (float)b.x, (float)b.y, (float)w.x,
                                                       (float)w.y]));

        // Writes the file; _pixels_ are channel-interleaved little-endian
        // samples in the order of the _channelNames_ given to the constructor.
        public byte[] Write(ReadOnlySpan<byte> pixels)
        {
            int w = dataWindow.pMax.x - dataWindow.pMin.x, h = dataWindow.pMax.y - dataWindow.pMin.y;
            int nc = channelNames.Length;
            int bps = type == PixelType.Half ? 2 : 4;
            CHECK_EQ(w * h * nc * bps, pixels.Length);

            // OpenEXR stores channels sorted by name.
            int[] order = Enumerable.Range(0, nc).ToArray();
            Array.Sort(order, (a, b) => string.CompareOrdinal(channelNames[a], channelNames[b]));

            List<Attribute> all = [.. attributes];
            using (MemoryStream chlist = new())
            {
                foreach (int c in order)
                {
                    chlist.Write(Encoding.UTF8.GetBytes(channelNames[c]));
                    chlist.WriteByte(0);
                    chlist.Write(Int((int)type));
                    chlist.Write([0, 0, 0, 0]);  // pLinear, reserved
                    chlist.Write(Int(1));        // xSampling
                    chlist.Write(Int(1));        // ySampling
                }
                chlist.WriteByte(0);
                all.Add(new Attribute("channels", "chlist", chlist.ToArray()));
            }
            all.Add(new Attribute("compression", "compression", [(byte)Compression.ZIP]));
            all.Add(new Attribute("dataWindow", "box2i", Box2i(dataWindow)));
            all.Add(new Attribute("displayWindow", "box2i", Box2i(displayWindow)));
            all.Add(new Attribute("lineOrder", "lineOrder", [0]));  // INCREASING_Y
            all.Add(new Attribute("pixelAspectRatio", "float", Floats([1f])));
            all.Add(new Attribute("screenWindowCenter", "v2f", Floats([0f, 0f])));
            all.Add(new Attribute("screenWindowWidth", "float", Floats([1f])));
            // Like OpenEXR's Header, which is a std::map.
            all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            bool longNames = false;
            foreach (Attribute a in all)
                longNames |= Encoding.UTF8.GetByteCount(a.name) > 31 || Encoding.UTF8.GetByteCount(a.type) > 31;
            foreach (string name in channelNames)
                longNames |= Encoding.UTF8.GetByteCount(name) > 31;

            using MemoryStream file = new();
            file.Write(Int(Magic));
            file.Write(Int(2 | (longNames ? LongNamesFlag : 0)));
            foreach (Attribute a in all)
            {
                file.Write(Encoding.UTF8.GetBytes(a.name));
                file.WriteByte(0);
                file.Write(Encoding.UTF8.GetBytes(a.type));
                file.WriteByte(0);
                file.Write(Int(a.value.Length));
                file.Write(a.value);
            }
            file.WriteByte(0);

            // Offset table, filled in as the chunks are written.
            int linesPerBlock = LinesPerBlock(Compression.ZIP);
            int nChunks = (h + linesPerBlock - 1) / linesPerBlock;
            long tableStart = file.Position;
            file.Write(new byte[8 * nChunks]);
            long[] offsets = new long[nChunks];

            for (int i = 0; i < nChunks; ++i)
            {
                int y0 = i * linesPerBlock, lines = Math.Min(linesPerBlock, h - y0);
                // Gather the block: for each line, for each channel (sorted),
                // w samples.
                byte[] block = new byte[lines * w * nc * bps];
                int bpos = 0;
                for (int y = y0; y < y0 + lines; ++y)
                    foreach (int c in order)
                        for (int x = 0; x < w; ++x, bpos += bps)
                            pixels.Slice(((y * w + x) * nc + c) * bps, bps).CopyTo(block.AsSpan(bpos));

                byte[] compressed = ApplyZip(block);
                offsets[i] = file.Position;
                file.Write(Int(dataWindow.pMin.y + y0));
                file.Write(Int(compressed.Length));
                file.Write(compressed);
            }

            byte[] result = file.ToArray();
            for (int i = 0; i < nChunks; ++i)
                BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan((int)tableStart + 8 * i), offsets[i]);
            return result;
        }
    }

    // internal_exr_apply_zip(): split into even and odd bytes, apply the
    // byte predictor and deflate; stores the block as is if that doesn't
    // make it smaller.
    private static byte[] ApplyZip(byte[] packed)
    {
        byte[] t = new byte[packed.Length];
        int t1 = 0, t2 = (packed.Length + 1) / 2, s = 0;
        while (s < packed.Length)
        {
            t[t1++] = packed[s++];
            if (s < packed.Length)
                t[t2++] = packed[s++];
        }

        int p = t.Length > 0 ? t[0] : 0;
        for (int i = 1; i < t.Length; ++i)
        {
            int d = t[i] - p + (128 + 256);
            p = t[i];
            t[i] = (byte)d;
        }

        using MemoryStream compressed = new();
        using (ZLibStream z = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(t);
        return compressed.Length < packed.Length ? compressed.ToArray() : packed;
    }
}

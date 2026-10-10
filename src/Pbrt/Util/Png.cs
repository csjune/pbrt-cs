// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// PNG reading and writing for ImageIO.cs, in place of the lodepng library
// (src/ext/lodepng) that pbrt uses. Provides the parts of lodepng's API that
// pbrt calls: lodepng_inspect(), lodepng::decode() to grey, RGB or RGBA at 8
// or 16 bits (16-bit samples are big-endian, as lodepng returns them), and
// lodepng_encode_memory() for 8-bit images.
//
// Decoding supports all PNG color types and bit depths, tRNS transparency
// and Adam7 interlacing; ancillary chunks other than tRNS are ignored.
// zlib (de)compression comes from System.IO.Compression.

using System.Buffers.Binary;
using System.IO.Compression;

namespace Pbrt;

// LodePNGColorType
internal enum PngColorType
{
    Grey = 0,
    RGB = 2,
    Palette = 3,
    GreyAlpha = 4,
    RGBA = 6,
}

internal sealed class PngException(string message) : Exception(message);

internal static class Png
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    // The header fields that pbrt looks at after lodepng_inspect().
    public readonly record struct Info(int Width, int Height, PngColorType ColorType, int BitDepth, bool Interlaced);

    public static Info Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 33 || !data[..8].SequenceEqual(Signature))
            throw new PngException("not a PNG file");
        if (BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new PngException("missing IHDR chunk");
        ReadOnlySpan<byte> ihdr = data.Slice(16, 13);
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr);
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(ihdr[4..]);
        int bitDepth = ihdr[8];
        PngColorType colorType = (PngColorType)ihdr[9];
        if (width <= 0 || height <= 0)
            throw new PngException("invalid image size");
        if (ihdr[10] != 0 || ihdr[11] != 0)
            throw new PngException("unsupported compression or filter method");
        if (ihdr[12] > 1)
            throw new PngException("invalid interlace method");
        bool valid = colorType switch
        {
            PngColorType.Grey => bitDepth is 1 or 2 or 4 or 8 or 16,
            PngColorType.Palette => bitDepth is 1 or 2 or 4 or 8,
            PngColorType.RGB or PngColorType.GreyAlpha or PngColorType.RGBA => bitDepth is 8 or 16,
            _ => false,
        };
        if (!valid)
            throw new PngException($"invalid color type {(int)colorType} / bit depth {bitDepth} combination");
        return new Info(width, height, colorType, bitDepth, ihdr[12] == 1);
    }

    private static int SamplesPerPixel(PngColorType t) => t switch
    {
        PngColorType.Grey or PngColorType.Palette => 1,
        PngColorType.GreyAlpha => 2,
        PngColorType.RGB => 3,
        _ => 4,
    };

    // lodepng::decode(): decodes to _outType_ (Grey, RGB or RGBA) with
    // _outBits_ (8 or 16) bits per sample.
    public static byte[] Decode(ReadOnlySpan<byte> data, PngColorType outType, int outBits)
    {
        CHECK(outType is PngColorType.Grey or PngColorType.RGB or PngColorType.RGBA);
        CHECK(outBits is 8 or 16);
        Info info = Inspect(data);

        // Gather the palette, transparency and image data chunks.
        byte[]? palette = null, trns = null;
        using MemoryStream idat = new();
        int pos = 8;
        bool sawEnd = false;
        while (pos + 12 <= data.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
            if (length < 0 || pos + 12 + length > data.Length)
                throw new PngException("chunk extends past the end of the file");
            ReadOnlySpan<byte> type = data.Slice(pos + 4, 4);
            ReadOnlySpan<byte> body = data.Slice(pos + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 8 + length)..]);
            if (Crc32(data.Slice(pos + 4, 4 + length)) != crc)
                throw new PngException($"CRC mismatch in {System.Text.Encoding.ASCII.GetString(type)} chunk");

            if (type.SequenceEqual("IDAT"u8))
                idat.Write(body);
            else if (type.SequenceEqual("PLTE"u8))
                palette = body.ToArray();
            else if (type.SequenceEqual("tRNS"u8))
                trns = body.ToArray();
            else if (type.SequenceEqual("IEND"u8))
            {
                sawEnd = true;
                break;
            }
            else if ((type[0] & 0x20) == 0 && !type.SequenceEqual("IHDR"u8))
                throw new PngException($"unknown critical chunk {System.Text.Encoding.ASCII.GetString(type)}");
            pos += 12 + length;
        }
        if (!sawEnd)
            throw new PngException("missing IEND chunk");
        if (info.ColorType == PngColorType.Palette && palette == null)
            throw new PngException("missing PLTE chunk");

        // Inflate the image data.
        byte[] raw;
        idat.Position = 0;
        using (ZLibStream z = new(idat, CompressionMode.Decompress))
        using (MemoryStream rawStream = new())
        {
            z.CopyTo(rawStream);
            raw = rawStream.ToArray();
        }

        // Unfilter (and de-interlace) into 16-bit RGBA, then convert.
        int w = info.Width, h = info.Height;
        ushort[] rgba = new ushort[4 * w * h];
        int bpp = SamplesPerPixel(info.ColorType) * info.BitDepth;  // bits per pixel
        int rawPos = 0;
        if (!info.Interlaced)
            rawPos = DecodePass(raw, rawPos, info, palette, trns, rgba, w, h, 0, 0, 1, 1, bpp);
        else
        {
            // Adam7
            ReadOnlySpan<int> xStart = [0, 4, 0, 2, 0, 1, 0], yStart = [0, 0, 4, 0, 2, 0, 1];
            ReadOnlySpan<int> xStep = [8, 8, 4, 4, 2, 2, 1], yStep = [8, 8, 8, 4, 4, 2, 2];
            for (int pass = 0; pass < 7; ++pass)
            {
                int pw = (w - xStart[pass] + xStep[pass] - 1) / xStep[pass];
                int ph = (h - yStart[pass] + yStep[pass] - 1) / yStep[pass];
                if (pw <= 0 || ph <= 0)
                    continue;
                rawPos = DecodePass(raw, rawPos, info, palette, trns, rgba, pw, ph,
                                    xStart[pass], yStart[pass], xStep[pass], yStep[pass], bpp);
            }
        }

        // lodepng only converts to a grey output at 8 bits: a 16-bit grey
        // output works only when no conversion is needed (16-bit grey
        // without tRNS), so pbrt can't read 16-bit grey+alpha PNGs.
        if (outType == PngColorType.Grey && outBits == 16 &&
            !(info.ColorType == PngColorType.Grey && info.BitDepth == 16 && trns == null))
            throw new PngException("given output image colortype or bitdepth not supported for color conversion");

        int nOut = SamplesPerPixel(outType);
        byte[] result = new byte[w * h * nOut * (outBits / 8)];
        Span<ushort> v = stackalloc ushort[4];
        for (int i = 0; i < w * h; ++i)
        {
            if (outType == PngColorType.Grey)
                v[0] = rgba[4 * i];  // (lodepng requires grey input for grey output)
            else
                for (int c = 0; c < nOut; ++c)
                    v[c] = rgba[4 * i + c];
            for (int c = 0; c < nOut; ++c)
            {
                if (outBits == 16)
                    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2 * (nOut * i + c)), v[c]);
                else
                    result[nOut * i + c] = (byte)(v[c] >> 8);
            }
        }
        return result;
    }

    // Unfilters one (sub)image of _pw_ x _ph_ pixels starting at raw[pos]
    // and stores its pixels, as 16-bit RGBA, at (x0 + x * dx, y0 + y * dy).
    private static int DecodePass(byte[] raw, int pos, Info info, byte[]? palette, byte[]? trns,
                                  ushort[] rgba, int pw, int ph, int x0, int y0, int dx, int dy, int bpp)
    {
        int stride = (pw * bpp + 7) / 8;
        int filterBpp = Math.Max(1, bpp / 8);  // bytes per complete pixel, rounded up to 1
        byte[] prev = new byte[stride], cur = new byte[stride];
        for (int y = 0; y < ph; ++y)
        {
            if (pos + 1 + stride > raw.Length)
                throw new PngException("image data is truncated");
            byte filter = raw[pos++];
            Array.Copy(raw, pos, cur, 0, stride);
            pos += stride;
            Unfilter(filter, cur, prev, filterBpp);

            for (int x = 0; x < pw; ++x)
            {
                int o = 4 * ((y0 + y * dy) * info.Width + x0 + x * dx);
                ReadPixel(cur, x, info, palette, trns, rgba.AsSpan(o, 4));
            }
            (prev, cur) = (cur, prev);
        }
        return pos;
    }

    private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        switch (filter)
        {
            case 0:
                break;
            case 1:  // Sub
                for (int i = bpp; i < cur.Length; ++i)
                    cur[i] += cur[i - bpp];
                break;
            case 2:  // Up
                for (int i = 0; i < cur.Length; ++i)
                    cur[i] += prev[i];
                break;
            case 3:  // Average
                for (int i = 0; i < cur.Length; ++i)
                {
                    int left = i >= bpp ? cur[i - bpp] : 0;
                    cur[i] += (byte)((left + prev[i]) >> 1);
                }
                break;
            case 4:  // Paeth
                for (int i = 0; i < cur.Length; ++i)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] += (byte)Paeth(a, b, c);
                }
                break;
            default:
                throw new PngException($"invalid filter type {filter}");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    // Reads pixel _x_ of an unfiltered scanline as 16-bit RGBA.
    private static void ReadPixel(byte[] line, int x, Info info, byte[]? palette, byte[]? trns, Span<ushort> rgba)
    {
        int bits = info.BitDepth;
        // Sample _i_ of this pixel, as stored (not rescaled).
        int sample(int i)
        {
            int n = SamplesPerPixel(info.ColorType);
            if (bits == 16)
                return BinaryPrimitives.ReadUInt16BigEndian(line.AsSpan(2 * (n * x + i)));
            if (bits == 8)
                return line[n * x + i];
            // 1, 2 or 4 bits: only grey and palette images, so n == 1
            int bitPos = x * bits;
            return (line[bitPos >> 3] >> (8 - bits - (bitPos & 7))) & ((1 << bits) - 1);
        }
        // Rescale a sample to 16 bits like lodepng (replicating the bits).
        ushort scale(int v) => bits switch
        {
            16 => (ushort)v,
            8 => (ushort)(v * 257),
            _ => (ushort)(v * 65535 / ((1 << bits) - 1)),
        };

        switch (info.ColorType)
        {
            case PngColorType.Grey:
            {
                int g = sample(0);
                ushort v = scale(g);
                bool transparent = trns != null && trns.Length >= 2 &&
                                   BinaryPrimitives.ReadUInt16BigEndian(trns) == g;
                rgba[0] = rgba[1] = rgba[2] = v;
                rgba[3] = transparent ? (ushort)0 : (ushort)65535;
                break;
            }
            case PngColorType.GreyAlpha:
                rgba[0] = rgba[1] = rgba[2] = scale(sample(0));
                rgba[3] = scale(sample(1));
                break;
            case PngColorType.RGB:
            {
                int r = sample(0), g = sample(1), b = sample(2);
                bool transparent = trns != null && trns.Length >= 6 &&
                                   BinaryPrimitives.ReadUInt16BigEndian(trns) == r &&
                                   BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2)) == g &&
                                   BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4)) == b;
                rgba[0] = scale(r);
                rgba[1] = scale(g);
                rgba[2] = scale(b);
                rgba[3] = transparent ? (ushort)0 : (ushort)65535;
                break;
            }
            case PngColorType.RGBA:
                for (int c = 0; c < 4; ++c)
                    rgba[c] = scale(sample(c));
                break;
            case PngColorType.Palette:
            {
                int index = sample(0);
                if (3 * index + 2 >= palette!.Length)
                    throw new PngException("palette index out of range");
                for (int c = 0; c < 3; ++c)
                    rgba[c] = (ushort)(palette[3 * index + c] * 257);
                rgba[3] = (ushort)((trns != null && index < trns.Length ? trns[index] : 255) * 257);
                break;
            }
        }
    }

    // lodepng_encode_memory() for 8-bit grey, RGB and RGBA pixels.
    public static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, PngColorType colorType)
    {
        CHECK(colorType is PngColorType.Grey or PngColorType.RGB or PngColorType.RGBA);
        int n = SamplesPerPixel(colorType);
        int stride = width * n;
        CHECK_EQ(stride * height, pixels.Length);

        // Filter each scanline with the filter that minimizes the sum of
        // absolute values (lodepng's default heuristic).
        byte[] filtered = new byte[(stride + 1) * height];
        byte[] prev = new byte[stride], best = new byte[stride], trial = new byte[stride];
        for (int y = 0; y < height; ++y)
        {
            ReadOnlySpan<byte> cur = pixels.Slice(y * stride, stride);
            long bestSum = long.MaxValue;
            byte bestFilter = 0;
            for (byte f = 0; f < 5; ++f)
            {
                long sum = 0;
                for (int i = 0; i < stride; ++i)
                {
                    int a = i >= n ? cur[i - n] : 0, b = prev[i], c = i >= n ? prev[i - n] : 0;
                    int predicted = f switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) >> 1,
                        _ => Paeth(a, b, c),
                    };
                    byte v = (byte)(cur[i] - predicted);
                    trial[i] = v;
                    sum += f == 0 ? v : Math.Abs((int)(sbyte)v);
                }
                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestFilter = f;
                    (best, trial) = (trial, best);
                }
            }
            filtered[y * (stride + 1)] = bestFilter;
            best.CopyTo(filtered, y * (stride + 1) + 1);
            cur.CopyTo(prev);
        }

        using MemoryStream compressed = new();
        using (ZLibStream z = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(filtered);

        using MemoryStream png = new();
        png.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8;
        ihdr[9] = (byte)colorType;
        ihdr[10] = ihdr[11] = ihdr[12] = 0;
        WriteChunk(png, "IHDR"u8, ihdr);
        WriteChunk(png, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, (uint)body.Length);
        s.Write(word);
        byte[] typeAndBody = new byte[4 + body.Length];
        type.CopyTo(typeAndBody);
        body.CopyTo(typeAndBody.AsSpan(4));
        s.Write(typeAndBody);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(typeAndBody));
        s.Write(word);
    }

    // The CRC-32 used by PNG chunks.
    private static readonly uint[] crcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; ++n)
        {
            uint c = n;
            for (int k = 0; k < 8; ++k)
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xffffffffu;
        foreach (byte b in data)
            c = crcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffffu;
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Radiance RGBE (.hdr) reading for ImageIO.cs: a C# port of stbi__hdr_load()
// from stb_image (src/ext/stb/stb_image.h, by Sean Barrett et al., public
// domain / MIT), which pbrt uses via stbi_loadf(). (StbImageSharp, used for
// the other stb_image formats, misreads images narrower than 8 pixels.)

using System.Text;

namespace Pbrt;

internal static class Hdr
{
    public sealed class HdrException(string message) : Exception(message);

    private static string ReadLine(byte[] data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != '\n')
            ++pos;
        string line = Encoding.ASCII.GetString(data, start, pos - start);
        if (pos < data.Length)
            ++pos;
        return line;
    }

    // stbi__hdr_convert() with req_comp == 3
    private static void Convert(Span<float> output, ReadOnlySpan<byte> input)
    {
        if (input[3] != 0)
        {
            // Exponent
            float f1 = MathF.ScaleB(1.0f, input[3] - (128 + 8));
            output[0] = input[0] * f1;
            output[1] = input[1] * f1;
            output[2] = input[2] * f1;
        }
        else
            output[0] = output[1] = output[2] = 0;
    }

    // Returns RGB float pixels (3 per pixel), like stbi_loadf(..., 0) for
    // HDR files.
    public static float[] Load(byte[] data, out int width, out int height)
    {
        int pos = 0;

        // Check identifier
        string headerToken = ReadLine(data, ref pos);
        if (headerToken != "#?RADIANCE" && headerToken != "#?RGBE")
            throw new HdrException("not HDR");

        // Parse header
        bool valid = false;
        while (true)
        {
            if (pos >= data.Length)
                throw new HdrException("unsupported data layout");
            string token = ReadLine(data, ref pos);
            if (token.Length == 0)
                break;
            if (token == "FORMAT=32-bit_rle_rgbe")
                valid = true;
        }

        if (!valid)
            throw new HdrException("unsupported format");

        // Parse width and height
        // can't use sscanf() if we're not using stdio!
        string dims = ReadLine(data, ref pos);
        string[] parts = dims.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X" ||
            !int.TryParse(parts[1], out height) || !int.TryParse(parts[3], out width))
            throw new HdrException("unsupported data layout");
        if (width <= 0 || height <= 0 || (long)width * height > (1 << 24) * 16L)
            throw new HdrException("too large");

        float[] hdr_data = new float[width * height * 3];
        Span<byte> rgbe = stackalloc byte[4];

        byte get8()
        {
            if (pos >= data.Length)
                throw new HdrException("unexpected end of file");
            return data[pos++];
        }

        // Load image data
        // image data is stored as some number of sca
        int i = 0, j = 0;
        if (width < 8 || width >= 32768)
        {
            // Read flat data
            for (; j < height; ++j)
            {
                for (; i < width; ++i)
                {
                    for (int k = 0; k < 4; ++k)
                        rgbe[k] = get8();
                    Convert(hdr_data.AsSpan((j * width + i) * 3), rgbe);
                }
                i = 0;
            }
        }
        else
        {
            // Read RLE-encoded data
            byte[] scanline = new byte[width * 4];

            for (j = 0; j < height; ++j)
            {
                int c1 = get8();
                int c2 = get8();
                int len = get8();
                if (c1 != 2 || c2 != 2 || (len & 0x80) != 0)
                {
                    // not run-length encoded, so we have to actually use THIS data as a decoded
                    // pixel (note this can't be a valid pixel--one of RGB must be >= 128)
                    rgbe[0] = (byte)c1;
                    rgbe[1] = (byte)c2;
                    rgbe[2] = (byte)len;
                    rgbe[3] = get8();
                    Convert(hdr_data.AsSpan(0), rgbe);
                    i = 1;
                    j = 0;
                    // (stb_image jumps into the flat decoding loop here.)
                    for (; j < height; ++j)
                    {
                        for (; i < width; ++i)
                        {
                            for (int k = 0; k < 4; ++k)
                                rgbe[k] = get8();
                            Convert(hdr_data.AsSpan((j * width + i) * 3), rgbe);
                        }
                        i = 0;
                    }
                    return hdr_data;
                }
                len <<= 8;
                len |= get8();
                if (len != width)
                    throw new HdrException("invalid decoded scanline length");

                for (int k = 0; k < 4; ++k)
                {
                    int nleft;
                    i = 0;
                    while ((nleft = width - i) > 0)
                    {
                        int count = get8();
                        if (count > 128)
                        {
                            // Run
                            byte value = get8();
                            count -= 128;
                            if (count > nleft)
                                throw new HdrException("bad RLE data in HDR");
                            for (int z = 0; z < count; ++z)
                                scanline[i++ * 4 + k] = value;
                        }
                        else
                        {
                            // Dump
                            if (count == 0 || count > nleft)
                                throw new HdrException("bad RLE data in HDR");
                            for (int z = 0; z < count; ++z)
                                scanline[i++ * 4 + k] = get8();
                        }
                    }
                }
                for (i = 0; i < width; ++i)
                    Convert(hdr_data.AsSpan((j * width + i) * 3), scanline.AsSpan(i * 4, 4));
            }
        }

        return hdr_data;
    }
}

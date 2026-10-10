// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of qoi_encode() and qoi_decode() from src/ext/qoi/qoi.h, the
// "Quite OK Image Format" codec that pbrt uses (by Dominic Szablewski,
// MIT licensed).

namespace Pbrt;

internal static class Qoi
{
    public const int QOI_SRGB = 0;
    public const int QOI_LINEAR = 1;

    public record struct Desc(int width, int height, int channels, int colorspace);

    private const int QOI_OP_INDEX = 0x00; /* 00xxxxxx */
    private const int QOI_OP_DIFF = 0x40;  /* 01xxxxxx */
    private const int QOI_OP_LUMA = 0x80;  /* 10xxxxxx */
    private const int QOI_OP_RUN = 0xc0;   /* 11xxxxxx */
    private const int QOI_OP_RGB = 0xfe;   /* 11111110 */
    private const int QOI_OP_RGBA = 0xff;  /* 11111111 */

    private const int QOI_MASK_2 = 0xc0; /* 11000000 */

    private const uint QOI_MAGIC = ((uint)'q') << 24 | ((uint)'o') << 16 | ((uint)'i') << 8 | ((uint)'f');
    private const int QOI_HEADER_SIZE = 14;

    /* 2GB is the max file size that this implementation can safely handle. We guard
    against anything larger than that, assuming the worst case with 5 bytes per
    pixel, rounded down to a nice clean value. 400 million pixels ought to be
    enough for anybody. */
    private const uint QOI_PIXELS_MAX = 400000000;

    private record struct Rgba(byte r, byte g, byte b, byte a);

    private static int ColorHash(Rgba c) => c.r * 3 + c.g * 5 + c.b * 7 + c.a * 11;

    private static ReadOnlySpan<byte> qoi_padding => [0, 0, 0, 0, 0, 0, 0, 1];

    private static void qoi_write_32(byte[] bytes, ref int p, uint v)
    {
        bytes[p++] = (byte)((0xff000000 & v) >> 24);
        bytes[p++] = (byte)((0x00ff0000 & v) >> 16);
        bytes[p++] = (byte)((0x0000ff00 & v) >> 8);
        bytes[p++] = (byte)(0x000000ff & v);
    }

    private static uint qoi_read_32(ReadOnlySpan<byte> bytes, ref int p)
    {
        uint a = bytes[p++];
        uint b = bytes[p++];
        uint c = bytes[p++];
        uint d = bytes[p++];
        return a << 24 | b << 16 | c << 8 | d;
    }

    // Returns null on failure, like qoi_encode().
    public static byte[]? Encode(ReadOnlySpan<byte> pixels, Desc desc)
    {
        if (desc.width == 0 || desc.height == 0 ||
            desc.channels < 3 || desc.channels > 4 ||
            desc.colorspace > 1 ||
            (uint)desc.height >= QOI_PIXELS_MAX / (uint)desc.width)
            return null;

        int max_size = desc.width * desc.height * (desc.channels + 1) + QOI_HEADER_SIZE + qoi_padding.Length;
        int p = 0;
        byte[] bytes = new byte[max_size];

        qoi_write_32(bytes, ref p, QOI_MAGIC);
        qoi_write_32(bytes, ref p, (uint)desc.width);
        qoi_write_32(bytes, ref p, (uint)desc.height);
        bytes[p++] = (byte)desc.channels;
        bytes[p++] = (byte)desc.colorspace;

        Span<Rgba> index = stackalloc Rgba[64];
        index.Clear();

        int run = 0;
        Rgba px_prev = new(0, 0, 0, 255);
        Rgba px = px_prev;

        int px_len = desc.width * desc.height * desc.channels;
        int px_end = px_len - desc.channels;
        int channels = desc.channels;

        for (int px_pos = 0; px_pos < px_len; px_pos += channels)
        {
            px.r = pixels[px_pos + 0];
            px.g = pixels[px_pos + 1];
            px.b = pixels[px_pos + 2];

            if (channels == 4)
                px.a = pixels[px_pos + 3];

            if (px == px_prev)
            {
                run++;
                if (run == 62 || px_pos == px_end)
                {
                    bytes[p++] = (byte)(QOI_OP_RUN | (run - 1));
                    run = 0;
                }
            }
            else
            {
                if (run > 0)
                {
                    bytes[p++] = (byte)(QOI_OP_RUN | (run - 1));
                    run = 0;
                }

                int index_pos = ColorHash(px) % 64;

                if (index[index_pos] == px)
                {
                    bytes[p++] = (byte)(QOI_OP_INDEX | index_pos);
                }
                else
                {
                    index[index_pos] = px;

                    if (px.a == px_prev.a)
                    {
                        sbyte vr = (sbyte)(px.r - px_prev.r);
                        sbyte vg = (sbyte)(px.g - px_prev.g);
                        sbyte vb = (sbyte)(px.b - px_prev.b);

                        sbyte vg_r = (sbyte)(vr - vg);
                        sbyte vg_b = (sbyte)(vb - vg);

                        if (vr > -3 && vr < 2 &&
                            vg > -3 && vg < 2 &&
                            vb > -3 && vb < 2)
                        {
                            bytes[p++] = (byte)(QOI_OP_DIFF | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2));
                        }
                        else if (vg_r > -9 && vg_r < 8 &&
                                 vg > -33 && vg < 32 &&
                                 vg_b > -9 && vg_b < 8)
                        {
                            bytes[p++] = (byte)(QOI_OP_LUMA | (vg + 32));
                            bytes[p++] = (byte)((vg_r + 8) << 4 | (vg_b + 8));
                        }
                        else
                        {
                            bytes[p++] = QOI_OP_RGB;
                            bytes[p++] = px.r;
                            bytes[p++] = px.g;
                            bytes[p++] = px.b;
                        }
                    }
                    else
                    {
                        bytes[p++] = QOI_OP_RGBA;
                        bytes[p++] = px.r;
                        bytes[p++] = px.g;
                        bytes[p++] = px.b;
                        bytes[p++] = px.a;
                    }
                }
            }
            px_prev = px;
        }

        for (int i = 0; i < qoi_padding.Length; i++)
            bytes[p++] = qoi_padding[i];

        return bytes.AsSpan(0, p).ToArray();
    }

    // Returns null on failure, like qoi_decode(). _channels_ 0 means the
    // number of channels in the file.
    public static byte[]? Decode(ReadOnlySpan<byte> bytes, out Desc desc, int channels)
    {
        desc = default;
        if ((channels != 0 && channels != 3 && channels != 4) ||
            bytes.Length < QOI_HEADER_SIZE + qoi_padding.Length)
            return null;

        int p = 0, run = 0;
        uint header_magic = qoi_read_32(bytes, ref p);
        desc.width = (int)qoi_read_32(bytes, ref p);
        desc.height = (int)qoi_read_32(bytes, ref p);
        desc.channels = bytes[p++];
        desc.colorspace = bytes[p++];

        if (desc.width == 0 || desc.height == 0 ||
            desc.channels < 3 || desc.channels > 4 ||
            desc.colorspace > 1 ||
            header_magic != QOI_MAGIC ||
            (uint)desc.height >= QOI_PIXELS_MAX / (uint)desc.width)
            return null;

        if (channels == 0)
            channels = desc.channels;

        int px_len = desc.width * desc.height * channels;
        byte[] pixels = new byte[px_len];

        Span<Rgba> index = stackalloc Rgba[64];
        index.Clear();
        Rgba px = new(0, 0, 0, 255);

        int chunks_len = bytes.Length - qoi_padding.Length;
        for (int px_pos = 0; px_pos < px_len; px_pos += channels)
        {
            if (run > 0)
            {
                run--;
            }
            else if (p < chunks_len)
            {
                int b1 = bytes[p++];

                if (b1 == QOI_OP_RGB)
                {
                    px.r = bytes[p++];
                    px.g = bytes[p++];
                    px.b = bytes[p++];
                }
                else if (b1 == QOI_OP_RGBA)
                {
                    px.r = bytes[p++];
                    px.g = bytes[p++];
                    px.b = bytes[p++];
                    px.a = bytes[p++];
                }
                else if ((b1 & QOI_MASK_2) == QOI_OP_INDEX)
                {
                    px = index[b1];
                }
                else if ((b1 & QOI_MASK_2) == QOI_OP_DIFF)
                {
                    px.r += (byte)(((b1 >> 4) & 0x03) - 2);
                    px.g += (byte)(((b1 >> 2) & 0x03) - 2);
                    px.b += (byte)((b1 & 0x03) - 2);
                }
                else if ((b1 & QOI_MASK_2) == QOI_OP_LUMA)
                {
                    int b2 = bytes[p++];
                    int vg = (b1 & 0x3f) - 32;
                    px.r += (byte)(vg - 8 + ((b2 >> 4) & 0x0f));
                    px.g += (byte)vg;
                    px.b += (byte)(vg - 8 + (b2 & 0x0f));
                }
                else if ((b1 & QOI_MASK_2) == QOI_OP_RUN)
                {
                    run = (b1 & 0x3f);
                }

                index[ColorHash(px) % 64] = px;
            }

            pixels[px_pos + 0] = px.r;
            pixels[px_pos + 1] = px.g;
            pixels[px_pos + 2] = px.b;

            if (channels == 4)
                pixels[px_pos + 3] = px.a;
        }

        return pixels;
    }
}

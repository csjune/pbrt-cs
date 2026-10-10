// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the parts of the Ptex library (pbrt's src/ext/ptex; Ptex is
// Copyright 2014 Disney Enterprises, Inc., BSD-licensed) that pbrt uses:
// Ptexture.h's types, PtexHalf, PtexUtils and PtexIO.h's file structures.
// The library is in the Pbrt.Ptex namespace (pbrt keeps it in its own
// namespace too), since its names (Res, FaceInfo, DataType ...) would
// otherwise clash with pbrt's.
//
// Texel data is kept in byte arrays, as Ptex keeps it in char buffers;
// "pointers" into them are (array, offset) pairs. Ptex's templates over the
// four data types read and write the texels through Texel.Get()/Set().

using System.Runtime.CompilerServices;

namespace Pbrt.Ptex;

// Ptexture.h enums
public enum MeshType { Triangle, Quad }

public enum DataType { UInt8, UInt16, Half, Float }

public enum EdgeFilterMode { None, TanVec }

public enum BorderMode { Clamp, Black, Periodic }

public enum EdgeId { Bottom, Right, Top, Left }

// Res Definition
public struct Res : IEquatable<Res>
{
    public sbyte ulog2, vlog2;

    public Res(sbyte ulog2, sbyte vlog2)
    {
        this.ulog2 = ulog2;
        this.vlog2 = vlog2;
    }

    public Res(int ulog2, int vlog2) : this((sbyte)ulog2, (sbyte)vlog2) { }

    public static Res FromValue(ushort value) => new((sbyte)(value & 0xff), (sbyte)((value >> 8) & 0xff));

    public readonly int u() => 1 << (int)(uint)ulog2;
    public readonly int v() => 1 << (int)(uint)vlog2;
    public readonly ushort val() => (ushort)((byte)ulog2 | ((byte)vlog2 << 8));
    public readonly int size() => u() * v();

    public static bool operator ==(Res a, Res b) => a.ulog2 == b.ulog2 && a.vlog2 == b.vlog2;
    public static bool operator !=(Res a, Res b) => !(a == b);
    public readonly bool Equals(Res r) => this == r;
    public override readonly bool Equals(object? o) => o is Res r && this == r;
    public override readonly int GetHashCode() => val();

    public readonly Res swappeduv() => new(vlog2, ulog2);
    public void swapuv() => this = swappeduv();

    public readonly int ntilesu(Res tileres) => 1 << (ulog2 - tileres.ulog2);
    public readonly int ntilesv(Res tileres) => 1 << (vlog2 - tileres.vlog2);
    public readonly int ntiles(Res tileres) => ntilesu(tileres) * ntilesv(tileres);

    public override readonly string ToString() => $"{u()}x{v()}";
}

// FaceInfo Definition (20 bytes in the file)
public struct FaceInfo
{
    public Res res;
    public byte adjedges;
    public byte flags;
    public int adjface0, adjface1, adjface2, adjface3;

    public const int flag_constant = 1, flag_hasedits = 2, flag_nbconstant = 4, flag_subface = 8;
    public const int Size = 20;

    public FaceInfo()
    {
        adjface0 = adjface1 = adjface2 = adjface3 = -1;
    }

    public static FaceInfo Read(ReadOnlySpan<byte> b)
    {
        FaceInfo f = new()
        {
            res = new Res((sbyte)b[0], (sbyte)b[1]),
            adjedges = b[2],
            flags = b[3],
            adjface0 = BitConverter.ToInt32(b[4..]),
            adjface1 = BitConverter.ToInt32(b[8..]),
            adjface2 = BitConverter.ToInt32(b[12..]),
            adjface3 = BitConverter.ToInt32(b[16..])
        };
        return f;
    }

    public readonly int adjedge(int eid) => (adjedges >> (2 * eid)) & 3;

    public readonly int adjface(int eid) => eid switch
    {
        0 => adjface0,
        1 => adjface1,
        2 => adjface2,
        _ => adjface3
    };

    public readonly bool isConstant() => (flags & flag_constant) != 0;
    public readonly bool isNeighborhoodConstant() => (flags & flag_nbconstant) != 0;
    public readonly bool hasEdits() => (flags & flag_hasedits) != 0;
    public readonly bool isSubface() => (flags & flag_subface) != 0;
}

// PtexHalf Definition
public static class PtexHalf
{
    private static readonly uint[] h2fTable = new uint[65536];
    private static readonly ushort[] f2hTable = new ushort[512];

    // (PtexHalfTableGen.cpp's PtexHalfInit(), which generates
    // PtexHalfTables.h)
    static PtexHalf()
    {
        for (int h = 0; h < 65536; h++)
        {
            int s = (h & 0x8000) << 16;
            int m = h & 0x03ff;
            int e = h & 0x7c00;

            uint i;
            if (unchecked((uint)(e - 1)) < ((31 << 10) - 1))
                // normal case
                i = (uint)(s | (((e + 0x1c000) | m) << 13));
            else if (e == 0)
            {
                // denormalized
                float f = (h & 0x8000) == 0 ? (float)(5.9604644775390625e-08 * m)
                                            : (float)(-5.9604644775390625e-08 * m);
                i = BitConverter.SingleToUInt32Bits(f);
            }
            else
                // inf/nan, preserve low bits of m for nan code
                i = (uint)(s | 0x7f800000 | (m << 13));
            h2fTable[h] = i;
        }

        for (int i = 0; i < 512; i++)
        {
            int f = i << 23;
            int e = (f & 0x7f800000) - 0x38000000;
            // normalized iff (0 < e < 31)
            if (unchecked((uint)(e - 1)) < ((31 << 23) - 1))
            {
                int s = (f >> 16) & 0x8000;
                int m = f & 0x7fe000;
                // add bit 12 to round
                f2hTable[i] = (ushort)((s | ((e | m) >> 13)) + ((f >> 12) & 1));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToFloat(ushort h) => BitConverter.UInt32BitsToSingle(h2fTable[h]);

    public static ushort FromFloat(float val)
    {
        if (val == 0)
            return 0;
        uint i = BitConverter.SingleToUInt32Bits(val);
        int e = f2hTable[(i >> 23) & 0x1ff];
        if (e != 0)
            return (ushort)(e + (((i & 0x7fffff) + 0x1000) >> 13));
        return FromFloatExcept(i);
    }

    // Handle exceptional cases for half-to-float conversion
    private static ushort FromFloatExcept(uint i)
    {
        uint s = (i >> 16) & 0x8000;
        int e = (int)((i >> 13) & 0x3fc00) - 0x1c000;

        if (e <= 0)
        {
            // denormalized
            float f = BitConverter.UInt32BitsToSingle(i);
            return (ushort)(s | (uint)(int)(Math.Abs((double)f) * 1.6777216e7 + .5));
        }

        if (e == 0x23c00)
            // inf/nan, preserve msb bits of m for nan code
            return (ushort)(s | 0x7c00 | ((i & 0x7fffff) >> 13));
        else
            // overflow - convert to inf
            return (ushort)(s | 0x7c00);
    }
}

// Texel access by data type
internal static class Texel
{
    public static int DataSize(DataType dt) => dt switch
    {
        DataType.UInt8 => 1,
        DataType.UInt16 => 2,
        DataType.Half => 2,
        _ => 4
    };

    public static float OneValue(DataType dt) => dt switch
    {
        DataType.UInt8 => 255f,
        DataType.UInt16 => 65535f,
        _ => 1f
    };

    public static float OneValueInv(DataType dt) => dt switch
    {
        DataType.UInt8 => 1f / 255f,
        DataType.UInt16 => 1f / 65535f,
        _ => 1f
    };

    // (float)p[i] for element i at the byte offset
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Get(byte[] data, int offset, int i, DataType dt) => dt switch
    {
        DataType.UInt8 => data[offset + i],
        DataType.UInt16 => Unsafe.ReadUnaligned<ushort>(ref data[offset + 2 * i]),
        DataType.Half => PtexHalf.ToFloat(Unsafe.ReadUnaligned<ushort>(ref data[offset + 2 * i])),
        _ => Unsafe.ReadUnaligned<float>(ref data[offset + 4 * i])
    };
}

// PtexUtils
internal static class PtexUtils
{
    public static uint ones(uint x)
    {
        // count number of ones
        x = (x & 0x55555555) + ((x >> 1) & 0x55555555); // add pairs of bits
        x = (x & 0x33333333) + ((x >> 2) & 0x33333333); // add bit pairs
        x = (x & 0x0f0f0f0f) + ((x >> 4) & 0x0f0f0f0f); // add nybbles
        x += x >> 8;                                    // add bytes
        x += x >> 16;                                   // add words
        return x & 0xff;
    }

    public static float reciprocalPow2(int power) => BitConverter.Int32BitsToSingle((127 - power) << 23);

    // equiv to ceil(log2(1.0/w)) but much faster and no error
    public static int calcResFromWidth(float w) => 127 - ((BitConverter.SingleToInt32Bits(w) >> 23) & 0xff);

    public static float abs(float x) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x) & 0x7fffffff);

    public static int min(int a, int b) => a < b ? a : b;
    public static int max(int a, int b) => a > b ? a : b;
    public static float min(float a, float b) => a < b ? a : b;
    public static float max(float a, float b) => a > b ? a : b;
    public static int clamp(int x, int lo, int hi) => min(max(x, lo), hi);
    public static float clamp(float x, float lo, float hi) => min(max(x, lo), hi);

    // (SSE4.1 rounding, as the x86-64 build with -march=native uses)
    public static float floor(float f) => MathF.Floor(f);
    public static float ceil(float f) => MathF.Ceiling(f);

    public static void ConvertToFloat(Span<float> dst, byte[] src, int offset, DataType dt, int numChannels)
    {
        float scale = dt switch
        {
            DataType.UInt8 => 1f / 255f,
            DataType.UInt16 => 1f / 65535f,
            _ => 1f
        };
        for (int i = 0; i < numChannels; i++)
        {
            if (dt == DataType.Float)
                dst[i] = Texel.Get(src, offset, i, dt);
            else
                // (ConvertArray(): DST((float)src[i] * scale + round), round = 0)
                dst[i] = Texel.Get(src, offset, i, dt) * scale + 0f;
        }
    }

    public static void interleave(byte[] src, int soff, int sstride, int uw, int vw, byte[] dst, int doff,
                                  int dstride, DataType dt, int nchan)
    {
        // (half and uint16 are copied as 16-bit values)
        int size = Texel.DataSize(dt);
        sstride /= size;
        dstride /= size;
        // for each channel
        for (int c = 0; c < nchan; c++)
        {
            int s = soff / size + c * sstride * vw;
            int drow = doff / size + c;
            // for each row
            for (int y = 0; y < vw; y++, s += sstride, drow += dstride)
            {
                // copy each pixel across the row
                int dp = drow;
                for (int x = 0; x < uw; x++, dp += nchan)
                    Buffer.BlockCopy(src, (s + x) * size, dst, dp * size, size);
            }
        }
    }

    public static void decodeDifference(byte[] data, int size, DataType dt)
    {
        switch (dt)
        {
            case DataType.UInt8:
            {
                byte prev = 0;
                for (int i = 0; i < size; i++)
                {
                    data[i] = (byte)(data[i] + prev);
                    prev = data[i];
                }
                break;
            }
            case DataType.UInt16:
            {
                ushort prev = 0;
                for (int i = 0; i < size / 2; i++)
                {
                    ushort v = (ushort)(BitConverter.ToUInt16(data, 2 * i) + prev);
                    BitConverter.TryWriteBytes(data.AsSpan(2 * i), v);
                    prev = v;
                }
                break;
            }
            default:
                break; // skip other types
        }
    }

    // Reductions: T(quarter(...)) and T(halve(...)) of the texels (integers
    // sum and shift as ints; halves sum as floats).
    public delegate void ReduceFn(byte[] src, int soff, int sstride, int ures, int vres, byte[] dst, int doff,
                                  int dstride, DataType dt, int nchannels);

    private static void Store(byte[] dst, int offset, int i, DataType dt, float v)
    {
        switch (dt)
        {
            case DataType.Half:
                BitConverter.TryWriteBytes(dst.AsSpan(offset + 2 * i), PtexHalf.FromFloat(v));
                break;
            default:
                BitConverter.TryWriteBytes(dst.AsSpan(offset + 4 * i), v);
                break;
        }
    }

    private static int GetInt(byte[] data, int offset, int i, DataType dt) =>
        dt == DataType.UInt8 ? data[offset + i] : BitConverter.ToUInt16(data, offset + 2 * i);

    private static void StoreInt(byte[] dst, int offset, int i, DataType dt, int v)
    {
        if (dt == DataType.UInt8)
            dst[offset + i] = (byte)v;
        else
            BitConverter.TryWriteBytes(dst.AsSpan(offset + 2 * i), (ushort)v);
    }

    private static bool IsInt(DataType dt) => dt == DataType.UInt8 || dt == DataType.UInt16;

    // sum of 4 elements, then quarter()
    private static void Quarter(byte[] src, int soff, int i0, int i1, int i2, int i3, byte[] dst, int doff, int di,
                                DataType dt)
    {
        if (IsInt(dt))
            StoreInt(dst, doff, di, dt,
                     (GetInt(src, soff, i0, dt) + GetInt(src, soff, i1, dt) + GetInt(src, soff, i2, dt) +
                      GetInt(src, soff, i3, dt)) >> 2);
        else
            Store(dst, doff, di, dt, 0.25f * (Texel.Get(src, soff, i0, dt) + Texel.Get(src, soff, i1, dt) +
                                              Texel.Get(src, soff, i2, dt) + Texel.Get(src, soff, i3, dt)));
    }

    private static void Halve(byte[] src, int soff, int i0, int i1, byte[] dst, int doff, int di, DataType dt)
    {
        if (IsInt(dt))
            StoreInt(dst, doff, di, dt, (GetInt(src, soff, i0, dt) + GetInt(src, soff, i1, dt)) >> 1);
        else
            Store(dst, doff, di, dt, 0.5f * (Texel.Get(src, soff, i0, dt) + Texel.Get(src, soff, i1, dt)));
    }

    public static void reduce(byte[] src, int soff, int sstride, int uw, int vw, byte[] dst, int doff, int dstride,
                              DataType dt, int nchan)
    {
        int size = Texel.DataSize(dt);
        sstride /= size;
        dstride /= size;
        int rowlen = uw * nchan;
        int srowskip = 2 * sstride - rowlen;
        int drowskip = dstride - rowlen / 2;
        int s = 0, d = 0;
        for (int end = vw * sstride; s != end; s += srowskip, d += drowskip)
            for (int rowend = s + rowlen; s != rowend; s += nchan)
                for (int pixend = s + nchan; s != pixend; s++)
                    Quarter(src, soff, s, s + nchan, s + sstride, s + sstride + nchan, dst, doff, d++, dt);
    }

    public static void reduceu(byte[] src, int soff, int sstride, int uw, int vw, byte[] dst, int doff, int dstride,
                               DataType dt, int nchan)
    {
        int size = Texel.DataSize(dt);
        sstride /= size;
        dstride /= size;
        int rowlen = uw * nchan;
        int srowskip = sstride - rowlen;
        int drowskip = dstride - rowlen / 2;
        int s = 0, d = 0;
        for (int end = vw * sstride; s != end; s += srowskip, d += drowskip)
            for (int rowend = s + rowlen; s != rowend; s += nchan)
                for (int pixend = s + nchan; s != pixend; s++)
                    Halve(src, soff, s, s + nchan, dst, doff, d++, dt);
    }

    public static void reducev(byte[] src, int soff, int sstride, int uw, int vw, byte[] dst, int doff, int dstride,
                               DataType dt, int nchan)
    {
        int size = Texel.DataSize(dt);
        sstride /= size;
        dstride /= size;
        int rowlen = uw * nchan;
        int srowskip = 2 * sstride - rowlen;
        int drowskip = dstride - rowlen;
        int s = 0, d = 0;
        for (int end = vw * sstride; s != end; s += srowskip, d += drowskip)
            for (int rowend = s + rowlen; s != rowend; s++)
                Halve(src, soff, s, s + sstride, dst, doff, d++, dt);
    }

    // generate a reduction of a packed-triangle texture
    // note: this method won't work for tiled textures
    public static void reduceTri(byte[] src, int soff, int sstride, int w, int vw, byte[] dst, int doff,
                                 int dstride, DataType dt, int nchan)
    {
        int size = Texel.DataSize(dt);
        sstride /= size;
        dstride /= size;
        int rowlen = w * nchan;
        int s = 0, s2 = (w - 1) * sstride + rowlen - nchan, d = 0;
        int srowinc2 = -2 * sstride - nchan;
        int srowskip = 2 * sstride - rowlen;
        int srowskip2 = w * sstride - 2 * nchan;
        int drowskip = dstride - rowlen / 2;
        for (int end = w * sstride; s != end; s += srowskip, s2 += srowskip2, d += drowskip)
            for (int rowend = s + rowlen; s != rowend; s += nchan, s2 += srowinc2)
                for (int pixend = s + nchan; s != pixend; s++, s2++)
                    Quarter(src, soff, s, s + nchan, s + sstride, s2, dst, doff, d++, dt);
    }

    public static void fill(byte[] src, int soff, byte[] dst, int doff, int dstride, int ures, int vres,
                            int pixelsize)
    {
        // fill first row
        int rowlen = ures * pixelsize;
        for (int p = doff; p != doff + rowlen; p += pixelsize)
            Buffer.BlockCopy(src, soff, dst, p, pixelsize);

        // fill remaining rows from first row
        for (int p = doff + dstride; p != doff + vres * dstride; p += dstride)
            Buffer.BlockCopy(dst, doff, dst, p, rowlen);
    }

    public static void copy(byte[] src, int soff, int sstride, byte[] dst, int doff, int dstride, int vres,
                            int rowlen)
    {
        for (int y = 0; y < vres; y++)
            Buffer.BlockCopy(src, soff + y * sstride, dst, doff + y * dstride, rowlen);
    }

    // data[i] = T((float)data[i] * aval)
    public static void multalpha(byte[] data, int offset, int npixels, DataType dt, int nchannels, int alphachan)
    {
        float scale = Texel.OneValueInv(dt);
        int alphaoffset; // offset to alpha chan from data ptr
        int nchanmult;   // number of channels to alpha-multiply
        int start = 0;
        if (alphachan == 0)
        {
            // first channel is alpha chan: mult the rest of the channels
            start = 1;
            alphaoffset = -1;
            nchanmult = nchannels - 1;
        }
        else
        {
            // mult all channels up to alpha chan
            alphaoffset = alphachan;
            nchanmult = alphachan;
        }

        for (int p = start; p != start + npixels * nchannels; p += nchannels)
        {
            float aval = scale * Texel.Get(data, offset, p + alphaoffset, dt);
            for (int i = 0; i < nchanmult; i++)
            {
                float v = Texel.Get(data, offset, p + i, dt) * aval;
                switch (dt)
                {
                    case DataType.UInt8: data[offset + p + i] = (byte)(int)v; break;
                    case DataType.UInt16:
                        BitConverter.TryWriteBytes(data.AsSpan(offset + 2 * (p + i)), (ushort)(int)v);
                        break;
                    default: Store(data, offset, p + i, dt, v); break;
                }
            }
        }
    }

    // stable_sort faceids by smaller dimension (u or v) in descending order
    // treat const faces as having res of 1
    public static void genRfaceids(FaceInfo[] faces, uint[] rfaceids, uint[] faceids)
    {
        int nfaces = faces.Length;
        // init faceids
        for (int i = 0; i < nfaces; i++)
            faceids[i] = (uint)i;

        // sort faceids by rfaceid (a stable sort: ties keep the face order)
        int Min(uint id) => faces[id].isConstant() ? 1 : Math.Min(faces[id].res.ulog2, faces[id].res.vlog2);
        uint[] sorted = faceids.OrderByDescending(Min).ToArray();
        sorted.CopyTo(faceids, 0);

        // generate mapping from faceid to rfaceid
        for (int i = 0; i < nfaces; i++)
            // note: i is the rfaceid
            rfaceids[faceids[i]] = (uint)i;
    }

    // dst[i] += (float)data[i] * weight
    public static void applyConst(float weight, Span<float> dst, byte[] data, int offset, DataType dt, int nChan)
    {
        for (int i = 0; i < nChan; i++)
            dst[i] += Texel.Get(data, offset, i, dt) * weight;
    }
}

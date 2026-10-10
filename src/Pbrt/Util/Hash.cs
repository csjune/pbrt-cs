// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/hash.h
//
// pbrt's variadic Hash(args...) memcpy()s its arguments back to back and
// hashes the bytes; the fixed-arity generic overloads below do the same, so
// as long as argument types match the C++ ones (same sizes, no padding
// differences), hash values match pbrt's.

using System.Runtime.InteropServices;

namespace Pbrt;

public static partial class Util
{
    // https://github.com/explosion/murmurhash/blob/master/murmurhash/MurmurHash2.cpp
    public static ulong MurmurHash64A(ReadOnlySpan<byte> key, ulong seed)
    {
        const ulong m = 0xc6a4a7935bd1e995ul;
        const int r = 47;

        int len = key.Length;
        ulong h = seed ^ ((ulong)len * m);

        int nBlocks = len / 8;
        for (int b = 0; b < nBlocks; ++b)
        {
            ulong k = MemoryMarshal.Read<ulong>(key.Slice(8 * b, 8));

            k *= m;
            k ^= k >> r;
            k *= m;

            h ^= k;
            h *= m;
        }

        ReadOnlySpan<byte> tail = key.Slice(8 * nBlocks);
        switch (len & 7)
        {
            case 7: h ^= (ulong)tail[6] << 48; goto case 6;
            case 6: h ^= (ulong)tail[5] << 40; goto case 5;
            case 5: h ^= (ulong)tail[4] << 32; goto case 4;
            case 4: h ^= (ulong)tail[3] << 24; goto case 3;
            case 3: h ^= (ulong)tail[2] << 16; goto case 2;
            case 2: h ^= (ulong)tail[1] << 8; goto case 1;
            case 1:
                h ^= tail[0];
                h *= m;
                break;
        }

        h ^= h >> r;
        h *= m;
        h ^= h >> r;

        return h;
    }

    // Hashing Inline Functions
    // http://zimbry.blogspot.ch/2011/09/better-bit-mixing-improving-on.html
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MixBits(ulong v)
    {
        v ^= (v >> 31);
        v *= 0x7fb5d329728ea185;
        v ^= (v >> 27);
        v *= 0x81dadef4bc2dd44d;
        v ^= (v >> 33);
        return v;
    }

    public static ulong HashBuffer<T>(ReadOnlySpan<T> data, ulong seed = 0) where T : unmanaged =>
        MurmurHash64A(MemoryMarshal.AsBytes(data), seed);

    public static unsafe ulong Hash<T1>(T1 a) where T1 : unmanaged
    {
        Span<byte> buf = stackalloc byte[sizeof(T1)];
        MemoryMarshal.Write(buf, in a);
        return MurmurHash64A(buf, 0);
    }

    public static unsafe ulong Hash<T1, T2>(T1 a, T2 b)
        where T1 : unmanaged where T2 : unmanaged
    {
        Span<byte> buf = stackalloc byte[sizeof(T1) + sizeof(T2)];
        int o = 0;
        MemoryMarshal.Write(buf.Slice(o), in a); o += sizeof(T1);
        MemoryMarshal.Write(buf.Slice(o), in b);
        return MurmurHash64A(buf, 0);
    }

    public static unsafe ulong Hash<T1, T2, T3>(T1 a, T2 b, T3 c)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
    {
        Span<byte> buf = stackalloc byte[sizeof(T1) + sizeof(T2) + sizeof(T3)];
        int o = 0;
        MemoryMarshal.Write(buf.Slice(o), in a); o += sizeof(T1);
        MemoryMarshal.Write(buf.Slice(o), in b); o += sizeof(T2);
        MemoryMarshal.Write(buf.Slice(o), in c);
        return MurmurHash64A(buf, 0);
    }

    public static unsafe ulong Hash<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
    {
        Span<byte> buf = stackalloc byte[sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4)];
        int o = 0;
        MemoryMarshal.Write(buf.Slice(o), in a); o += sizeof(T1);
        MemoryMarshal.Write(buf.Slice(o), in b); o += sizeof(T2);
        MemoryMarshal.Write(buf.Slice(o), in c); o += sizeof(T3);
        MemoryMarshal.Write(buf.Slice(o), in d);
        return MurmurHash64A(buf, 0);
    }

    public static unsafe ulong Hash<T1, T2, T3, T4, T5>(T1 a, T2 b, T3 c, T4 d, T5 e)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
        where T5 : unmanaged
    {
        Span<byte> buf =
            stackalloc byte[sizeof(T1) + sizeof(T2) + sizeof(T3) + sizeof(T4) + sizeof(T5)];
        int o = 0;
        MemoryMarshal.Write(buf.Slice(o), in a); o += sizeof(T1);
        MemoryMarshal.Write(buf.Slice(o), in b); o += sizeof(T2);
        MemoryMarshal.Write(buf.Slice(o), in c); o += sizeof(T3);
        MemoryMarshal.Write(buf.Slice(o), in d); o += sizeof(T4);
        MemoryMarshal.Write(buf.Slice(o), in e);
        return MurmurHash64A(buf, 0);
    }

    public static Float HashFloat<T1>(T1 a) where T1 : unmanaged =>
        (uint)Hash(a) * OneOver2Pow32;

    public static Float HashFloat<T1, T2>(T1 a, T2 b)
        where T1 : unmanaged where T2 : unmanaged =>
        (uint)Hash(a, b) * OneOver2Pow32;

    public static Float HashFloat<T1, T2, T3>(T1 a, T2 b, T3 c)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged =>
        (uint)Hash(a, b, c) * OneOver2Pow32;

    public static Float HashFloat<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d)
        where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged =>
        (uint)Hash(a, b, c, d) * OneOver2Pow32;
}

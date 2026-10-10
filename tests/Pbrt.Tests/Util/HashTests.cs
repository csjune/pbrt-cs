// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/hash_test.cpp

namespace Pbrt.Tests;

public class HashTests
{
    [Fact]
    public void VarArgs()
    {
        long[] buf = [1, -12511, 31415821, 37];
        for (int i = 0; i < 4; ++i)
            Assert.Equal(HashBuffer<long>(buf.AsSpan(i, 1)), Hash(buf[i]));
    }

    [Fact]
    public void Collisions()
    {
        // (As in pbrt's test, nothing is ever inserted into the sets, so
        // this only checks that no hash equals its input.)
        HashSet<uint> low = [], high = [];
        HashSet<ulong> full = [];

        int lowCollisions = 0, highCollisions = 0;
        int fullCollisions = 0;
        int same = 0;
        for (int i = 0; i < 10000000; ++i)
        {
            ulong h = Hash(i);

            if (h == (ulong)i)
                ++same;

            if (low.Contains((uint)h))
                ++lowCollisions;
            if (high.Contains((uint)(h >> 32)))
                ++highCollisions;
            if (full.Contains(h >> 32))
                ++fullCollisions;
        }

        // It's actually potentially legit if any of these hit; it should
        // shouldn't happen a lot.
        Assert.Equal(0, same);
        Assert.Equal(0, lowCollisions);
        Assert.Equal(0, highCollisions);
        Assert.Equal(0, fullCollisions);
    }

    [Fact]
    public void Unaligned()
    {
        ulong[] buf = [0xfacebeef, 0x65028088, 0x13372048];
        byte[] cbuf = new byte[sizeof(ulong) * 3 + 8];
        for (int delta = 0; delta < 8; ++delta)
        {
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(buf.AsSpan()).CopyTo(cbuf.AsSpan(delta));
            Assert.Equal(HashBuffer<ulong>(buf), HashBuffer<byte>(cbuf.AsSpan(delta, sizeof(ulong) * 3)));
        }
    }
}

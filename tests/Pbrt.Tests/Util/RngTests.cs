// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/rng_test.cpp

namespace Pbrt.Tests;

public class RNGTests
{
    [Fact]
    public void Reseed()
    {
        RNG rng = new(1234);
        var values = new List<uint>();
        for (int i = 0; i < 100; ++i)
            values.Add(rng.Uniform<uint>());

        rng.SetSequence(1234);
        for (int i = 0; i < values.Count; ++i)
            Assert.Equal(values[i], rng.Uniform<uint>());
    }

    [Fact]
    public void Advance()
    {
        // Note: must use float not Float, since doubles consume two 32-bit
        // values from the stream, so the advance tests (as written)
        // consequently fail.
        RNG rng = new();
        rng.SetSequence(1234, 6502);
        var v = new List<float>();
        for (int i = 0; i < 1000; ++i)
            v.Add(rng.Uniform<float>());

        rng.SetSequence(1234, 6502);
        rng.Advance(16);
        Assert.Equal(v[16], rng.Uniform<float>());

        for (int i = v.Count - 1; i >= 0; --i)
        {
            rng.SetSequence(1234, 6502);
            rng.Advance(i);
            Assert.Equal(v[i], rng.Uniform<float>());
        }

        // Switch to another sequence
        rng.SetSequence(32);
        rng.Uniform<float>();

        // Go back and check one last time
        foreach (int i in new[] { 5, 998, 552, 37, 16 })
        {
            rng.SetSequence(1234, 6502);
            rng.Advance(i);
            Assert.Equal(v[i], rng.Uniform<float>());
        }
    }

    [Fact]
    public void OperatorMinus()
    {
        RNG ra = new(1337), rb = new(1337);
        RNG rng = new();
        for (int i = 0; i < 10; ++i)
        {
            int step = 1 + (int)rng.Uniform(1000u);
            for (int j = 0; j < step; ++j)
                ra.Uniform<uint>();
            Assert.Equal(step, ra - rb);
            Assert.Equal(-step, rb - ra);

            // Reysnchronize them
            if ((rng.Uniform<uint>() & 1) != 0u)
                rb.Advance(step);
            else
                ra.Advance(-step);
            Assert.Equal(0, ra - rb);
            Assert.Equal(0, rb - ra);
        }
    }

    [Fact]
    public void Int()
    {
        RNG rng = new();
        int positive = 0, negative = 0, zero = 0;
        int count = 10000;
        for (int i = 0; i < count; ++i)
        {
            int v = rng.Uniform<int>();
            if (v < 0)
                ++negative;
            else if (v == 0)
                ++zero;
            else
                ++positive;
        }

        Assert.True(positive > .48 * count);
        Assert.True(positive < .52 * count);
        Assert.True(negative > .48 * count);
        Assert.True(negative < .52 * count);
        Assert.True(zero < .001 * count);
    }

    [Fact]
    public void Uint64()
    {
        RNG rng = new();
        var bitCounts = new int[64];
        int count = 10000;
        for (int i = 0; i < count; ++i)
        {
            ulong v = rng.Uniform<ulong>();
            for (int b = 0; b < 64; ++b)
                if ((v & (1ul << b)) != 0u)
                    ++bitCounts[b];
        }

        for (int b = 0; b < 64; ++b)
        {
            Assert.True(bitCounts[b] > .48 * count);
            Assert.True(bitCounts[b] < .52 * count);
        }
    }

    [Fact]
    public void Double()
    {
        RNG rng = new();
        for (int i = 0; i < 10; ++i)
        {
            double v = rng.Uniform<double>();
            Assert.NotEqual(v, (double)(float)v);
        }
    }
}

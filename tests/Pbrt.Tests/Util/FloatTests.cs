// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/float_test.cpp
// (AtomicFloat is tested with parallel.h.)

namespace Pbrt.Tests;

public class FloatingPointTests
{
    [Fact]
    public void Constants()
    {
#if PBRT_FLOAT_AS_DOUBLE
        Assert.Equal(0x3ca0000000000000UL, FloatToBits(MachineEpsilon)); // 2^-53
        Assert.Equal(0x3fefffffffffffffUL, FloatToBits(OneMinusEpsilon));
#else
        Assert.Equal(0x33800000u, FloatToBits(MachineEpsilon));          // 2^-24
        Assert.Equal(0x3f7fffffu, FloatToBits(OneMinusEpsilon));
#endif
        Assert.Equal(0x3f7fffffu, FloatToBits(FloatOneMinusEpsilon));    // 0x1.fffffep-1
        Assert.Equal(0x3fefffffffffffffUL, FloatToBits(DoubleOneMinusEpsilon));
        Assert.Equal(0x00800000u, FloatToBits(FloatMinNormal));
        Assert.Equal(0x2f800000u, FloatToBits(OneOver2Pow32));
        Assert.Equal(0x3bf0000000000000UL, FloatToBits(OneOver2Pow64));
    }

    [Fact]
    public void Pieces()
    {
        Assert.Equal(1, Exponent(2f));
        Assert.Equal(-1, Exponent(0.5f));
        Assert.Equal(0b10000000000000000000000, Significand(3f));
        Assert.Equal(0b11000000000000000000000, Significand(7f));

        Assert.Equal(1, Exponent(2.0));
        Assert.Equal(-1, Exponent(0.5));
        Assert.Equal(0b1000000000000000000000000000000000000000000000000000UL, Significand(3.0));
        Assert.Equal(0b1100000000000000000000000000000000000000000000000000UL, Significand(7.0));
    }

    private static float GetFloat(ref RNG rng)
    {
        float f;
        do
        {
            f = BitsToFloat(rng.Uniform<uint>());
        } while (float.IsNaN(f));
        return f;
    }

    private static double GetDouble(ref RNG rng)
    {
        double d;
        do
        {
            d = BitsToFloat((ulong)rng.Uniform<uint>() | ((ulong)rng.Uniform<uint>() << 32));
        } while (double.IsNaN(d));
        return d;
    }

    [Fact]
    public void NextUpDownFloat()
    {
        Assert.True(NextFloatUp(-0f) > 0f);
        Assert.True(NextFloatDown(0f) < 0f);

        Assert.Equal(float.PositiveInfinity, NextFloatUp(float.PositiveInfinity));
        Assert.True(NextFloatDown(float.PositiveInfinity) < float.PositiveInfinity);

        Assert.Equal(float.NegativeInfinity, NextFloatDown(float.NegativeInfinity));
        Assert.True(NextFloatUp(float.NegativeInfinity) > float.NegativeInfinity);

        RNG rng = new();
        for (int i = 0; i < 100000; ++i)
        {
            float f = GetFloat(ref rng);
            if (float.IsInfinity(f))
                continue;

            Assert.Equal(MathF.BitIncrement(f), NextFloatUp(f));
            Assert.Equal(MathF.BitDecrement(f), NextFloatDown(f));
        }
    }

    [Fact]
    public void NextUpDownDouble()
    {
        Assert.True(NextFloatUp(-0.0) > 0.0);
        Assert.True(NextFloatDown(0.0) < 0.0);

        Assert.Equal(double.PositiveInfinity, NextFloatUp(double.PositiveInfinity));
        Assert.True(NextFloatDown(double.PositiveInfinity) < double.PositiveInfinity);

        Assert.Equal(double.NegativeInfinity, NextFloatDown(double.NegativeInfinity));
        Assert.True(NextFloatUp(double.NegativeInfinity) > double.NegativeInfinity);

        RNG rng = new(3);
        for (int i = 0; i < 100000; ++i)
        {
            double d = GetDouble(ref rng);
            if (double.IsInfinity(d))
                continue;

            Assert.Equal(Math.BitIncrement(d), NextFloatUp(d));
            Assert.Equal(Math.BitDecrement(d), NextFloatDown(d));
        }
    }

    [Fact]
    public void FloatBits()
    {
        RNG rng = new(1);
        for (int i = 0; i < 100000; ++i)
        {
            uint ui = rng.Uniform<uint>();
            float f = BitsToFloat(ui);
            if (float.IsNaN(f))
                continue;

            Assert.Equal(ui, FloatToBits(f));
        }
    }

    [Fact]
    public void DoubleBits()
    {
        RNG rng = new(2);
        for (int i = 0; i < 100000; ++i)
        {
            ulong ui = (ulong)rng.Uniform<uint>() | ((ulong)rng.Uniform<uint>() << 32);
            double f = BitsToFloat(ui);

            if (double.IsNaN(f))
                continue;

            Assert.Equal(ui, FloatToBits(f));
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void AtomicFloat()
    {
        AtomicFloat af = new(0);
        Float f = 0;
        Assert.Equal(f, af);
        af.Add(1.0251f);
        f += 1.0251f;
        Assert.Equal(f, af);
        af.Add(2f);
        f += 2f;
        Assert.Equal(f, af);
    }
}

// pbrt's Half is System.Half; these are float_test.cpp's Half tests on it.
public class HalfTests
{
    private const ushort HalfPositiveZero = 0, HalfNegativeZero = 0x8000, HalfPositiveInfinity = 0x7c00,
                         HalfNegativeInfinity = 0xfc00;

    private static ushort Bits(Half h) => BitConverter.HalfToUInt16Bits(h);
    private static Half FromBits(ushort b) => BitConverter.UInt16BitsToHalf(b);

    [Fact]
    public void Basics()
    {
        Assert.Equal(HalfPositiveZero, Bits((Half)0f));
        Assert.Equal(HalfNegativeZero, Bits((Half)(-0f)));
        Assert.Equal(HalfPositiveInfinity, Bits((Half)float.PositiveInfinity));
        Assert.Equal(HalfNegativeInfinity, Bits((Half)float.NegativeInfinity));

        Assert.True(Half.IsNaN((Half)float.NaN));
        Assert.False(Half.IsNaN(FromBits(HalfPositiveInfinity)));

        for (uint bits = 0; bits < 65536; ++bits)
        {
            Half h = FromBits((ushort)bits);
            if (Half.IsInfinity(h) || Half.IsNaN(h))
                continue;
            Assert.True(h == -(-h));
            Assert.True(-h == (Half)(float)(-(float)h));
        }
    }

    [Fact]
    public void ExactConversions()
    {
        // Test round-trip conversion of integers that are perfectly
        // representable.
        for (Float i = -2048; i <= 2048; ++i)
            Assert.Equal(i, (Float)(Half)(float)(i));

        // Similarly for some well-behaved floats
        float limit = 1024, delta = 0.5f;
        for (int i = 0; i < 10; ++i)
        {
            for (float f = -limit; f <= limit; f += delta)
                Assert.Equal(f, (Float)(Half)(float)(f));
            limit /= 2;
            delta /= 2;
        }
    }

    [Fact]
    public void Randoms()
    {
        RNG rng = new();
        // Choose a bunch of random positive floats and make sure that they
        // convert to reasonable values.
        for (int i = 0; i < 1024; ++i)
        {
            float f = rng.Uniform<float>() * 512;
            ushort h = Bits((Half)f);
            float fh = (float)FromBits(h);
            if (fh == f)
            {
                // Very unlikely, but we happened to pick a value exactly
                // representable as a half.
                continue;
            }

            // The other half value that brackets the float.
            ushort hother;
            if (fh > f)
            {
                // The closest half was a bit bigger; therefore, the half before
                // it s the other one.
                hother = (ushort)(h - 1);
                if (hother > h)
                    continue; // test for wrapping around zero
            }
            else
            {
                hother = (ushort)(h + 1);
                if (hother < h)
                    continue; // test for wrapping around zero
            }

            // Make sure the two half values bracket the float.
            float fother = (float)FromBits(hother);
            float dh = MathF.Abs(fh - f);
            float dother = MathF.Abs(fother - f);
            if (fh > f)
                Assert.True(fother < f);
            else
                Assert.True(fother > f);

            // Make sure rounding to the other one of them wouldn't have given a
            // closer half.
            Assert.True(dh <= dother);
        }
    }

    [Fact]
    public void NextUp()
    {
        Half h = FromBits(HalfNegativeInfinity);
        int iters = 0;
        while (Bits(h) != HalfPositiveInfinity)
        {
            Assert.True(iters < 65536);
            ++iters;

            Half hup = Half.BitIncrement(h);
            Assert.True((float)hup > (float)h);
            h = hup;
        }
        // NaNs use the maximum exponent and then the sign bit and have a
        // non-zero significand.
        Assert.Equal(65536 - (1 << 11), iters);
    }

    [Fact]
    public void NextDown()
    {
        Half h = FromBits(HalfPositiveInfinity);
        int iters = 0;
        while (Bits(h) != HalfNegativeInfinity)
        {
            Assert.True(iters < 65536);
            ++iters;

            Half hdown = Half.BitDecrement(h);
            Assert.True((float)hdown < (float)h, $"{Bits(hdown)} {Bits(h)}");
            h = hdown;
        }
        Assert.Equal(65536 - (1 << 11), iters);
    }

    [Fact]
    public void Equal()
    {
        for (uint bits = 0; bits < 65536; ++bits)
        {
            Half h = FromBits((ushort)bits);
            if (Half.IsInfinity(h) || Half.IsNaN(h))
                continue;
#pragma warning disable CS1718 // comparison to self is the point
            Assert.True(h == h);
#pragma warning restore CS1718
        }

        // Check that +/- zero behave sensibly.
        Half hpz = (Half)0f, hnz = (Half)(-0f);
        Assert.NotEqual(Bits(hpz), Bits(hnz));
        Assert.True(hpz == hnz);
        // Smallest representable non-zero half value
        Half hmin = (Half)5.9605e-08f;
        Assert.True(hpz != hmin);
        Assert.True(hnz != -hmin);
    }

    [Fact]
    public void RoundToNearestEven()
    {
        // For all floats halfway between two half values, make sure that they
        // round to the "even" half (i.e., zero low bit).
        Half h0 = (Half)0f, h1 = Half.BitIncrement(h0);
        while (!Half.IsInfinity(h1))
        {
            float mid = ((float)h0 + (float)h1) / 2;
            if ((Bits(h0) & 1) == 0)
                Assert.True(h0 == (Half)(float)(mid));
            else
                Assert.True(h1 == (Half)(float)(mid));

            h0 = h1;
            h1 = Half.BitIncrement(h0);
        }

        h0 = (Half)(float)(-0f);
        h1 = Half.BitDecrement(h0);
        while (!Half.IsInfinity(h1))
        {
            float mid = ((float)h0 + (float)h1) / 2;
            if ((Bits(h0) & 1) == 0)
                Assert.True(h0 == (Half)(float)(mid));
            else
                Assert.True(h1 == (Half)(float)(mid));

            h0 = h1;
            h1 = Half.BitDecrement(h0);
        }
    }
}

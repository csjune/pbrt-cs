// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/math_test.cpp
// TODO: SquareMatrix * vector tests once vecmath is ported.

namespace Pbrt.Tests;

public class MathTests
{
    [Fact]
    public void Pow2Basics()
    {
        for (int i = 0; i < 32; ++i)
        {
            uint ui = 1u << i;
            Assert.True(IsPowerOf2(ui));
            if (ui > 1)
                Assert.False(IsPowerOf2(ui + 1));
            if (ui > 2)
                Assert.False(IsPowerOf2(ui - 1));
        }
    }

    [Fact]
    public void RoundUpPow2Basics()
    {
        Assert.Equal(8, RoundUpPow2(7));
        for (int i = 1; i < (1 << 24); ++i)
            if (IsPowerOf2(i))
                Assert.Equal(i, RoundUpPow2(i));
            else
                Assert.Equal(1 << (Log2Int(i) + 1), RoundUpPow2(i));

        for (long i = 1; i < (1 << 24); ++i)
            if (IsPowerOf2(i))
                Assert.Equal(i, RoundUpPow2(i));
            else
                Assert.Equal(1 << (Log2Int(i) + 1), RoundUpPow2(i));

        for (int i = 0; i < 30; ++i)
        {
            int v = 1 << i;
            Assert.Equal(v, RoundUpPow2(v));
            if (v > 2)
                Assert.Equal(v, RoundUpPow2(v - 1));
            Assert.Equal(2 * v, RoundUpPow2(v + 1));
        }

        for (int i = 0; i < 62; ++i)
        {
            long v = 1L << i;
            Assert.Equal(v, RoundUpPow2(v));
            if (v > 2)
                Assert.Equal(v, RoundUpPow2(v - 1));
            Assert.Equal(2 * v, RoundUpPow2(v + 1));
        }
    }

    [Fact]
    public void Morton2Basics()
    {
        ushort x = 0b01010111, y = 0b11000101;
        ulong m = EncodeMorton2(x, y);
        Assert.Equal(0b1011000100110111UL, m);

        RNG rng = new(12351);
        for (int i = 0; i < 100000; ++i)
        {
            uint xx = rng.Uniform<uint>();
            uint yy = rng.Uniform<uint>();
            ulong mm = EncodeMorton2(xx, yy);

            DecodeMorton2(mm, out uint xp, out uint yp);
            Assert.Equal(xx, xp);
            Assert.Equal(yy, yp);
        }
    }

    [Fact]
    public void Pow()
    {
        for (int n = 0; n <= 29; ++n)
            Assert.Equal((float)(1 << n), Util.Pow(2f, n));
    }

    [Fact]
    public void NewtonBisection()
    {
        Assert.Equal(1f, Util.NewtonBisection(0, 10, x => (-1 + x, 1)), 5);
        Assert.Equal(Pi / 2, Util.NewtonBisection(0, 2, x => (FMath.Cos(x), -FMath.Sin(x))), 5);

        // The derivative is a lie--pointing in the wrong direction, even--but
        // it should still work.
        Float bad = Util.NewtonBisection(0, 2, x => (FMath.Cos(x), 10 * FMath.Sin(x)));
        Assert.True(FMath.Abs(Pi / 2 - bad) < 1e-5);

        // Multiple zeros in the domain; make sure we find one.
        Float zero = Util.NewtonBisection(.1f, 10.1f, x => (FMath.Sin(x), FMath.Cos(x)));
        Assert.True(FMath.Abs(FMath.Sin(zero)) < 1e-6);

        // Ill-behaved function with derivatives that go to infinity (and also
        // multiple zeros).
        static (Float, Float) f(Float x) =>
            ((Float)(Math.Pow(Sqr(FMath.Sin(x)), .05) - 0.3),
             (Float)(0.1 * FMath.Cos(x) * FMath.Sin(x) / Math.Pow(Sqr(FMath.Sin(x)), 0.95)));
        zero = Util.NewtonBisection(.01f, 9.42477798f, f);
        // Extra slop for a messy function.
        Assert.True(FMath.Abs(f(zero).Item1) < 1e-2);
    }

    [Fact]
    public void EvaluatePolynomial()
    {
        Assert.Equal(4, Util.EvaluatePolynomial(100, 4));
        Assert.Equal(10, Util.EvaluatePolynomial(2, 4, 3));

        Assert.Equal(1.5 + 2.75 * .5 - 4.25 * Util.Pow(.5, 2) + 15.125 * Util.Pow(.5, 3),
                     Util.EvaluatePolynomial(.5, 1.5, 2.75, -4.25, 15.125));
    }

    [Fact]
    public void CompensatedSum()
    {
        // In order of decreasing accuracy...
        CompensatedSum<double> kahanSumD = default;
        double doubleSum = 0;
        CompensatedSum<float> kahanSumF = default;
        float floatSum = 0;

        RNG rng = new();
        for (int i = 0; i < 16 * 1024 * 1024; ++i)
        {
            // Hard to sum accurately since the values span many magnitudes.
            float v = (float)FMath.Exp(Lerp(rng.Uniform<Float>(), -5, 20));
            kahanSumD += v;
            doubleSum += v;
            kahanSumF += v;
            floatSum += v;
        }

        long kahanDBits = (long)FloatToBits((double)kahanSumD);
        long doubleBits = (long)FloatToBits(doubleSum);
        long kahanFBits = (long)FloatToBits((double)(float)kahanSumF);
        long floatBits = (long)FloatToBits((double)floatSum);

        long doubleErrorUlps = Math.Abs(doubleBits - kahanDBits);
        long kahanFErrorUlps = Math.Abs(kahanFBits - kahanDBits);
        long floatErrorUlps = Math.Abs(floatBits - kahanDBits);

        // Less slop between double and Kahan with floats.
        Assert.True(doubleErrorUlps * 1000 < kahanFErrorUlps);
        Assert.True(kahanFErrorUlps * 10000 < floatErrorUlps);
    }

    [Fact]
    public void Log2IntBasics()
    {
        for (int i = 0; i < 32; ++i)
        {
            uint ui = 1u << i;
            Assert.Equal(i, Log2Int(ui));
            Assert.Equal(i, Log2Int((ulong)ui));
        }

        for (int i = 1; i < 31; ++i)
        {
            uint ui = 1u << i;
            Assert.Equal(i, Log2Int(ui + 1));
            Assert.Equal(i, Log2Int((ulong)(ui + 1)));
        }

        for (int i = 0; i < 64; ++i)
        {
            ulong ui = 1ul << i;
            Assert.Equal(i, Log2Int(ui));
        }

        for (int i = 1; i < 64; ++i)
        {
            ulong ui = 1ul << i;
            Assert.Equal(i, Log2Int(ui + 1));
        }

        // Exact powers of two
        for (int i = 120; i >= -120; --i)
            Assert.Equal(i, Log2Int(MathF.Pow(2f, i)));
        for (int i = 120; i >= -120; --i)
            Assert.Equal(i, Log2Int(Math.Pow(2.0, i)));

        // Specifically exercise round to nearest even stuff for the Float version
        for (int i = -31; i < 31; ++i)
        {
            float vlow = MathF.Pow(2, i + 0.499f);
            Assert.Equal(i, Log2Int(vlow));
            float vhigh = MathF.Pow(2, i + 0.501f);
            Assert.Equal(i + 1, Log2Int(vhigh));
        }
        for (int i = -31; i < 31; ++i)
        {
            double vlow = Math.Pow(2.0, i + 0.4999999);
            Assert.Equal(i, Log2Int(vlow));
            double vhigh = Math.Pow(2.0, i + 0.5000001);
            Assert.Equal(i + 1, Log2Int(vhigh));
        }
    }

    [Fact]
    public void Log4IntBasics()
    {
        Assert.Equal(0, Log4Int(1));
        Assert.Equal(1, Log4Int(4));
        Assert.Equal(2, Log4Int(16));
        Assert.Equal(3, Log4Int(64));

        int log4 = 0, next = 4;
        for (int v = 1; v < 16385; ++v)
        {
            if (v == next)
            {
                ++log4;
                next *= 4;
            }
            Assert.Equal(log4, Log4Int(v));
        }
    }

    [Fact]
    public void Pow4Basics()
    {
        for (int i = 0; i < 12; ++i)
        {
            int p4 = 1 << (2 * i);
            Assert.True(IsPowerOf4(p4));
            Assert.Equal(p4, RoundUpPow4(p4));
            if (i > 0)
                Assert.Equal(p4, RoundUpPow4(p4 - 1));
            Assert.Equal(p4 * 4, RoundUpPow4(p4 + 1));
        }
    }

    [Fact]
    public void NextPrimeBasics()
    {
        Assert.Equal(3, NextPrime(2));
        Assert.Equal(11, NextPrime(10));
        Assert.Equal(37, NextPrime(32));
        Assert.Equal(37, NextPrime(37));

        static bool isPrime(int x)
        {
            if ((x & 1) == 0)
                return false;
            for (int i = 3; i < Math.Sqrt(x) + 1; i += 3)
            {
                if ((x % i) == 0)
                    return false;
            }
            return true;
        }

        for (int n = 3; n < 8000; n += 3)
        {
            if (isPrime(n))
                Assert.Equal(n, NextPrime(n));
            else
                Assert.True(isPrime(NextPrime(n)));
        }
    }

    [Fact]
    public void Erf()
    {
        // Not in pbrt (it uses std::erf); check against reference values.
        // (Erf() is glibc's erff, which is within an ulp or so of erf.)
        (double x, double erf)[] refs =
        [
            (0, 0), (0.1, 0.1124629160182849), (0.25, 0.2763263901682369),
            (0.5, 0.5204998778130465), (1, 0.8427007929497149), (1.5, 0.9661051464753108),
            (2, 0.9953222650189527), (3, 0.9999779095030014),
        ];
        foreach (var (x, e) in refs)
        {
            Assert.True(Math.Abs(Util.Erf((float)x) - e) < 2e-7, $"erf({x})");
            Assert.True(Math.Abs(Util.Erf((float)-x) + e) < 2e-7, $"erf({-x})");
        }
    }

    [Fact]
    public void ErfInv()
    {
        float[] xvl = [0f, 0.1125f, 0.25f, .753f, 1.521f, 2.5115f];
        foreach (float x in xvl)
        {
            Float e = Util.Erf(x);
            if (e < 1)
            {
                Float ei = Util.ErfInv(e);
                if (x == 0)
                    Assert.Equal(0, ei);
                else
                {
                    Float err = FMath.Abs(ei - x) / x;
                    Assert.True(err < 1e-4, $"{x} erf {e} inv {ei}");
                }
            }
        }
    }

    // The next two expect a higher-precision option to verify with.
    [Fact(Skip = DoubleMode.NeedsHigherPrecision)]
    public void DifferenceOfProducts()
    {
        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Float r()
            {
                Float logu = Lerp(rng.Uniform<Float>(), -8, 8);
                return FMath.Pow(10, logu);
            }
            Float a = r(), b = r(), c = r(), d = r();
            Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
            b *= sign;
            c *= sign;
            Float dp = Util.DifferenceOfProducts(a, b, c, d);
            Float dp2 = (Float)FMA((double)a, (double)b, -(double)c * (double)d);
            Float err = FMath.Abs(dp - dp2);
            Float ulp = NextFloatUp(FMath.Abs(dp2)) - FMath.Abs(dp2);
            Assert.True(err < 2 * ulp);
        }
    }

    [Fact(Skip = DoubleMode.NeedsHigherPrecision)]
    public void SumOfProducts()
    {
        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Float r()
            {
                Float logu = Lerp(rng.Uniform<Float>(), -8, 8);
                return FMath.Pow(10, logu);
            }
            // Make sure mixed signs...
            Float a = r(), b = r(), c = r(), d = -r();
            Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
            b *= sign;
            c *= sign;
            Float sp = Util.SumOfProducts(a, b, c, d);
            Float sp2 = (Float)FMA((double)a, (double)b, (double)c * (double)d);
            Float err = FMath.Abs(sp - sp2);
            Float ulp = NextFloatUp(FMath.Abs(sp2)) - FMath.Abs(sp2);
            Assert.True(err < 2 * ulp);
        }
    }

    [Fact]
    public void FastExpAccuracy()
    {
        Assert.Equal(1, FastExp(0));

        RNG rng = new(6502);
        for (int i = 0; i < 100; ++i)
        {
            Float v = Lerp(rng.Uniform<Float>(), -20f, 20f);
            Float f = FastExp((float)v);
            Float e = FMath.Exp(v);
            Float err = FMath.Abs((f - e) / e);
            Assert.True(err <= 0.0003f,
                        $"At {v}, fast = {f}, accurate = {e} -> relative error = {err}");
        }
    }

    [Fact]
    public void GaussianIntegral()
    {
        Float[,] muSigma = { { 0, 1 }, { 0, 2 }, { 0, .1f }, { 1, 2 }, { -2, 1 } };
        for (int i = 0; i < muSigma.GetLength(0); ++i)
        {
            RNG rng = new();
            for (int j = 0; j < 5; ++j)
            {
                Float x0 = -5 + 10 + rng.Uniform<Float>();
                Float x1 = -5 + 10 + rng.Uniform<Float>();
                if (x0 > x1)
                    (x0, x1) = (x1, x0);

                Float mu = muSigma[i, 0], sigma = muSigma[i, 1];
                Float sum = 0;
                int n = 8192;
                for (int k = 0; k < n; ++k)
                {
                    Float u = (k + rng.Uniform<Float>()) / n;
                    Float x = Lerp(u, x0, x1);
                    sum += Gaussian(x, mu, sigma);
                }
                Float est = (x1 - x0) * sum / n;

                static bool compareFloats(Float rf, Float v)
                {
                    if (FMath.Abs(rf) < 1e-4)
                        return FMath.Abs(rf - v) < 1e-5;
                    return FMath.Abs((rf - v) / rf) < 1e-3;
                }
                Float integral = Util.GaussianIntegral(x0, x1, mu, sigma);
                Assert.True(compareFloats(est, integral), $"{est} vs {integral}");
            }
        }
    }

    [Fact]
    public void FindIntervalBasics()
    {
        float[] a = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        // Check clamping for out of range
        Assert.Equal(0, FindInterval(a.Length, index => a[index] <= -1));
        Assert.Equal(a.Length - 2, FindInterval(a.Length, index => a[index] <= 100));

        for (int i = 0; i < a.Length - 1; ++i)
        {
            Assert.Equal(i, FindInterval(a.Length, index => a[index] <= i));
            Assert.Equal(i, FindInterval(a.Length, index => a[index] <= i + 0.5));
            if (i > 0)
                Assert.Equal(i - 1, FindInterval(a.Length, index => a[index] <= i - 0.5));

            // Span specialization
            Assert.Equal(i, FindInterval(a, i));
            Assert.Equal(i, FindInterval(a, i + 0.5f));
            if (i > 0)
                Assert.Equal(i - 1, FindInterval(a, i - 0.5f));
        }
        Assert.Equal(0, FindInterval(a, -1));
        Assert.Equal(a.Length - 2, FindInterval(a, 100));
    }

    [Fact]
    public void TwoProd()
    {
        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Float r()
            {
                Float logu = Lerp(rng.Uniform<Float>(), -10, 10);
                Float val = FMath.Pow(10, logu);
                Float sign = rng.Uniform<Float>() < .5 ? -1 : 1;
                return val * sign;
            }

            Float a = r(), b = r();
            CompensatedFloat tp = Util.TwoProd(a, b);
            Assert.Equal(a * b, (Float)tp);
            Assert.Equal((double)a * (double)b, (double)tp);
        }
    }

    [Fact]
    public void TwoSum()
    {
        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Float r()
            {
                Float logu = Lerp(rng.Uniform<Float>(), -10, 10);
                Float val = FMath.Pow(10, logu);
                Float sign = rng.Uniform<Float>() < .5 ? -1 : 1;
                return val * sign;
            }

            Float a = r(), b = r();
            CompensatedFloat tp = Util.TwoSum(a, b);
            Assert.Equal(a + b, (Float)tp);
            Assert.Equal((double)a + (double)b, (double)tp);
        }
    }

    [Fact(Skip = DoubleMode.NeedsHigherPrecision)]
    public void InnerProduct()
    {
        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Float r()
            {
                Float logu = Lerp(rng.Uniform<Float>(), -10, 10);
                Float val = FMath.Pow(10, logu);
                Float sign = rng.Uniform<Float>() < .5 ? -1 : 1;
                return val * sign;
            }

            Float[] a = [r(), r(), r(), r()];
            Float[] b = [r(), r(), r(), r()];
            Float ab = Util.InnerProduct(a[0], b[0], a[1], b[1], a[2], b[2], a[3], b[3]);
            Float dab = (Float)((double)a[0] * b[0] + (double)a[1] * b[1] +
                                (double)a[2] * b[2] + (double)a[3] * b[3]);
            Assert.Equal(dab, ab);
        }
    }

    // Make sure that the permute function is in fact a valid permutation.
    [Fact]
    public void PermutationElementValid()
    {
        for (int len = 2; len < 1024; ++len)
        {
            for (int iter = 0; iter < 10; ++iter)
            {
                var seen = new bool[len];

                for (int i = 0; i < len; ++i)
                {
                    int offset = PermutationElement((uint)i, (uint)len, (uint)MixBits((ulong)(1 + iter)));
                    Assert.True(offset >= 0 && offset < seen.Length, $"{offset}");
                    Assert.False(seen[offset], $"len {len} index {i}");
                    seen[offset] = true;
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PermutationElementUniform(bool delta)
    {
        foreach (int n in new[] { 2, 3, 4, 5, 9, 14, 16, 22, 27, 36 })
        {
            var count = new int[n * n];

            int numIters = 60000 * n;
            for (int seed = 0; seed < numIters; ++seed)
            {
                for (int i = 0; i < n; ++i)
                {
                    int ip = PermutationElement((uint)i, (uint)n, (uint)MixBits((ulong)seed));
                    int j = ip;
                    if (delta)
                    {
                        j = ip - i;
                        if (j < 0)
                            j += n;
                    }
                    ++count[j * n + i];
                }
            }

            for (int i = 0; i < n; ++i)
            {
                for (int j = 0; j < n; ++j)
                {
                    Float tol = 0.03f;
                    int offset = j * n + i;
                    Assert.True(count[offset] >= (1 - tol) * numIters / n &&
                                count[offset] <= (1 + tol) * numIters / n,
                                $"Got count {count[offset]} for {i} -> {j} (perm size {n}). Expected +/- {numIters / n}.");
                }
            }
        }
    }
}

public class SquareMatrixTests
{
    [Fact]
    public void Basics3()
    {
        SquareMatrix3 m3 = new();
        Assert.True(m3.IsIdentity());

        Assert.Equal(m3, new SquareMatrix3(1, 0, 0, 0, 1, 0, 0, 0, 1));
        Assert.NotEqual(m3, new SquareMatrix3(0, 1, 0, 0, 1, 0, 0, 0, 1));

        Assert.Equal(new SquareMatrix3(2, 0, 0, 0, -1, 0, 0, 0, 3), SquareMatrix3.Diag(2, -1, 3));

        SquareMatrix3 m = new(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Assert.False(m.IsIdentity());
        SquareMatrix3 mt = new(1, 4, 7, 2, 5, 8, 3, 6, 9);
        Assert.Equal(mt, Transpose(m));

        SquareMatrix3? inv = Inverse(m3);
        Assert.True(inv.HasValue);
        Assert.Equal(m3, inv.Value);

        SquareMatrix3 ms = new(2, 0, 0, 0, 4, 0, 0, 0, -1);
        inv = Inverse(ms);
        Assert.True(inv.HasValue);
        Assert.Equal(new SquareMatrix3(0.5f, 0, 0, 0, .25f, 0, 0, 0, -1), inv.Value);

        SquareMatrix3 degen = new(0, 0, 2, 0, 0, 0, 1, 1, 1);
        inv = Inverse(degen);
        Assert.False(inv.HasValue);
    }

    [Fact]
    public void Basics4()
    {
        SquareMatrix4 m4 = new();
        Assert.True(m4.IsIdentity());
        Assert.False(default(SquareMatrix4).IsIdentity());

        Assert.Equal(m4, new SquareMatrix4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
        Assert.NotEqual(m4, new SquareMatrix4(0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0));

        SquareMatrix4 diag = new(8, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0, .5f);
        Assert.Equal(diag, SquareMatrix4.Diag(8, 2, 1, .5f));

        SquareMatrix4 m = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
        Assert.False(m.IsIdentity());
        SquareMatrix4 mt = new(1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15, 4, 8, 12, 16);
        Assert.Equal(mt, Transpose(m));

        SquareMatrix4? inv = Inverse(m4);
        Assert.True(inv.HasValue);
        Assert.Equal(m4, inv.Value);

        inv = Inverse(diag);
        Assert.True(inv.HasValue);
        Assert.Equal(SquareMatrix4.Diag(.125f, .5f, 1, 2), inv.Value);

        SquareMatrix4 degen = new(2, 0, 0, 0, 0, 4, 0, 0, 0, -3, 0, 1, 0, 0, 0, 0);
        inv = Inverse(degen);
        Assert.False(inv.HasValue);
    }

    private static bool Equal(Float a, Float b, Float tol = 1e-4f)
    {
        if (FMath.Abs(a) < 1e-5 || FMath.Abs(b) < 1e-5)
            return FMath.Abs(a) - FMath.Abs(b) < tol;
        return (FMath.Abs(a) - FMath.Abs(b)) / ((FMath.Abs(a) + FMath.Abs(b)) / 2) < tol;
    }

    [Fact]
    public void InverseRandom()
    {
        int nFail = 0;
        int nIters = 1000;
        for (int i = 0; i < nIters; ++i)
        {
            RNG rng = new((ulong)i);
            SquareMatrix3 m = default;
            for (int r = 0; r < 3; ++r)
                for (int c = 0; c < 3; ++c)
                    m[r, c] = -10 + 20 * rng.Uniform<Float>();
            SquareMatrix3? inv = Inverse(m);
            if (!inv.HasValue)
            {
                ++nFail;
                continue;
            }
            SquareMatrix3 id = m * inv.Value;

            for (int j = 0; j < 3; ++j)
                for (int k = 0; k < 3; ++k)
                {
                    if (j == k)
                        Assert.True(Equal(id[j, k], 1), $"{m}, inv {inv} prod {id}");
                    else
                        Assert.True(FMath.Abs(id[j, k]) < 1e-4, $"{m}, inv {inv} prod {id}");
                }
        }
        for (int i = 0; i < nIters; ++i)
        {
            RNG rng = new((ulong)i);
            SquareMatrix4 m = default;
            for (int r = 0; r < 4; ++r)
                for (int c = 0; c < 4; ++c)
                    m[r, c] = -10 + 20 * rng.Uniform<Float>();
            SquareMatrix4? inv = Inverse(m);
            if (!inv.HasValue)
            {
                ++nFail;
                continue;
            }
            SquareMatrix4 id = m * inv.Value;

            for (int j = 0; j < 4; ++j)
                for (int k = 0; k < 4; ++k)
                {
                    if (j == k)
                        Assert.True(Equal(id[j, k], 1), $"{m}, inv {inv} prod {id}");
                    else
                        Assert.True(FMath.Abs(id[j, k]) < 1e-4, $"{m}, inv {inv} prod {id}");
                }
        }

        Assert.True(nFail < 3);
    }
}

public class IntervalTests
{
    private const int kFloatIntervalIters = 1000000;

    // Return an exponentially-distributed floating-point value.
    private static Interval GetFloat(ref RNG rng, Float minExp = -6, Float maxExp = 6)
    {
        Float logu = Lerp(rng.Uniform<Float>(), minExp, maxExp);
        Float val = FMath.Pow(10, logu);

        // Choose a random error bound.
        Float err = 0;
        switch (rng.Uniform(4u))
        {
            case 0:
                // no error
                break;
            case 1:
            {
                // small typical/reasonable error
                uint ulpError = rng.Uniform(1024u);
                Float offset = BitsToFloat(FloatToBits(val) + ulpError);
                err = FMath.Abs(offset - val);
                break;
            }
            case 2:
            {
                // bigger ~reasonable error
                uint ulpError = rng.Uniform(1024u * 1024u);
                Float offset = BitsToFloat(FloatToBits(val) + ulpError);
                err = FMath.Abs(offset - val);
                break;
            }
            case 3:
                err = (4 * rng.Uniform<Float>()) * FMath.Abs(val);
                break;
        }
        Float sign = rng.Uniform<Float>() < .5 ? -1 : 1;
        return Interval.FromValueAndError(sign * val, err);
    }

    // Given an Interval covering some range, choose a double-precision
    // "precise" value that is in the Interval's range.
    private static double GetPrecise(Interval ef, ref RNG rng)
    {
        switch (rng.Uniform(3u))
        {
            // 2/3 of the time, pick a value that is right at the end of the range;
            // this is a maximally difficult / adversarial choice, so should help
            // ferret out any bugs.
            case 0:
                return ef.LowerBound;
            case 1:
                return ef.UpperBound;
            default:
            {
                // Otherwise choose a value uniformly inside the Interval's range.
                Float t = rng.Uniform<Float>();
                double p = (1 - t) * (double)ef.LowerBound + t * (double)ef.UpperBound;
                if (p > ef.UpperBound)
                    p = ef.UpperBound;
                if (p < ef.LowerBound)
                    p = ef.LowerBound;
                return p;
            }
        }
    }

    private static void ExpectInRange(double v, Interval i, string? msg = null)
    {
        Assert.True(v >= i.LowerBound, msg ?? $"{v} < {i}");
        Assert.True(v <= i.UpperBound, msg ?? $"{v} > {i}");
    }

    [Fact]
    public void Abs()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef = GetFloat(ref rng);
            double precise = GetPrecise(ef, ref rng);
            ExpectInRange(Math.Abs(precise), Util.Abs(ef));
        }
    }

    [Fact]
    public void Sqrt()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef = GetFloat(ref rng);
            double precise = GetPrecise(ef, ref rng);
            ExpectInRange(Math.Sqrt(Math.Abs(precise)), Util.Sqrt(Util.Abs(ef)));
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Add()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef0 = GetFloat(ref rng), ef1 = GetFloat(ref rng);
            double p0 = GetPrecise(ef0, ref rng), p1 = GetPrecise(ef1, ref rng);
            ExpectInRange((float)(p0 + p1), ef0 + ef1);
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Sub()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef0 = GetFloat(ref rng), ef1 = GetFloat(ref rng);
            double p0 = GetPrecise(ef0, ref rng), p1 = GetPrecise(ef1, ref rng);
            ExpectInRange((float)(p0 - p1), ef0 - ef1);
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Mul()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef0 = GetFloat(ref rng), ef1 = GetFloat(ref rng);
            double p0 = GetPrecise(ef0, ref rng), p1 = GetPrecise(ef1, ref rng);
            ExpectInRange((float)(p0 * p1), ef0 * ef1);
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Div()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);
            Interval ef0 = GetFloat(ref rng), ef1 = GetFloat(ref rng);
            double p0 = GetPrecise(ef0, ref rng), p1 = GetPrecise(ef1, ref rng);

            // Things get messy if the denominator's interval straddles zero...
            if (ef1.LowerBound * ef1.UpperBound < 0)
                continue;

            ExpectInRange((float)(p0 / p1), ef0 / ef1);
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void FMA()
    {
        int nTrials = 10000, nIters = 400;
        for (int i = 0; i < nTrials; ++i)
        {
            RNG rng = new((ulong)i);
            Interval v = Util.Abs(GetFloat(ref rng));
            for (int j = 0; j < nIters; ++j)
            {
                Interval a = v;
                Interval b = GetFloat(ref rng);
                Interval c = GetFloat(ref rng);

                v = Util.FMA(a, b, c);

                if (IsInf(v.LowerBound) || IsInf(v.UpperBound))
                    break;

                double pa = GetPrecise(a, ref rng);
                double pb = GetPrecise(b, ref rng);
                double pc = GetPrecise(c, ref rng);
                float preciseResult = (float)Util.FMA(pa, pb, pc);
                ExpectInRange(preciseResult, v);

                Interval vp = a * b + c;
                Assert.True(v.LowerBound >= vp.LowerBound, $"{v} vs {vp}");
                Assert.True(v.UpperBound <= vp.UpperBound, $"{v} vs {vp}");
            }
        }
    }

    [Fact]
    public void Sqr()
    {
        Interval a = new(1.75f, 2.25f);
        Interval sq = Util.Sqr(a), at = a * a;
        Assert.Equal(at.UpperBound, sq.UpperBound);
        Assert.Equal(at.LowerBound, sq.LowerBound);

        // Straddle 0
        Interval b = new(-.75f, 1.25f);
        Interval bs = Util.Sqr(b), b2 = b * b;
        Assert.Equal(b2.UpperBound, bs.UpperBound);
        Assert.Equal(0, bs.LowerBound);
        Assert.True(b2.LowerBound < 0);
    }

    [Fact]
    public void SumSquares()
    {
        Interval a = new(1), b = new(2), c = new(3);
        Assert.Equal(1, (Float)Util.SumSquares(a));
        Assert.Equal(4, (Float)Util.SumSquares(b));
        Assert.Equal(5, (Float)Util.SumSquares(a, b));
        Assert.Equal(14, (Float)Util.SumSquares(a, b, c));
    }

    [Fact]
    public void DifferenceOfProducts()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);

            Interval a = Util.Abs(GetFloat(ref rng));
            Interval b = Util.Abs(GetFloat(ref rng));
            Interval c = Util.Abs(GetFloat(ref rng));
            Interval d = Util.Abs(GetFloat(ref rng));

            Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
            b *= sign;
            c *= sign;

            double pa = GetPrecise(a, ref rng);
            double pb = GetPrecise(b, ref rng);
            double pc = GetPrecise(c, ref rng);
            double pd = GetPrecise(d, ref rng);

            Interval r = Util.DifferenceOfProducts(a, b, c, d);
            double pr = Util.DifferenceOfProducts(pa, pb, pc, pd);

            ExpectInRange(pr, r, $"{trial}");
        }
    }

    [Fact]
    public void SumOfProducts()
    {
        for (int trial = 0; trial < kFloatIntervalIters; ++trial)
        {
            RNG rng = new((ulong)trial);

            // Make sure signs are mixed
            Interval a = Util.Abs(GetFloat(ref rng));
            Interval b = Util.Abs(GetFloat(ref rng));
            Interval c = Util.Abs(GetFloat(ref rng));
            Interval d = -Util.Abs(GetFloat(ref rng));

            Float sign = rng.Uniform<Float>() < -0.5 ? -1 : 1;
            b *= sign;
            c *= sign;

            double pa = GetPrecise(a, ref rng);
            double pb = GetPrecise(b, ref rng);
            double pc = GetPrecise(c, ref rng);
            double pd = GetPrecise(d, ref rng);

            Interval r = Util.SumOfProducts(a, b, c, d);
            double pr = Util.SumOfProducts(pa, pb, pc, pd);

            ExpectInRange(pr, r, $"{trial}");
        }
    }
}

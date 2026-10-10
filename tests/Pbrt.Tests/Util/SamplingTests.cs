// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/sampling_test.cpp (WeightedReservoir tests run
// sequentially until parallel.h is ported).

namespace Pbrt.Tests;

internal static class TestHelpers
{
    // pbrt's frequent SampleUniformSphere({rng.Uniform<Float>(), rng.Uniform<Float>()}).
    public static Vector3f RandomSphere(ref RNG rng)
    {
        Float u0 = rng.Uniform<Float>();
        Float u1 = rng.Uniform<Float>();
        return SampleUniformSphere(new Point2f(u0, u1));
    }

    // gtest's EXPECT_FLOAT_EQ: within 4 ulps.
    public static void ExpectFloatEq(double expected, Float actualF, string? msg = null)
    {
        float e = (float)expected, actual = (float)actualF;
        if (e == actual)
            return;
        long diff = Math.Abs((long)(int)FloatToBits(e) - (long)(int)FloatToBits(actual));
        Assert.True(Math.Sign(e) == Math.Sign(actual) && diff <= 4, msg ?? $"expected {e}, got {actual}");
    }

    // Like EXPECT_FALSE(checkErr(a, b)) in sampling_test.cpp.
    public static bool CheckErr(Float a, Float b)
    {
        Float err;
        if (Min(FMath.Abs(a), FMath.Abs(b)) < 1e-2)
            err = FMath.Abs(a - b);
        else
            err = FMath.Abs(2 * (a - b) / (a + b));
        return err > 1e-2;
    }

    // PiecewiseConstant1D::TestCompareDistributions
    public static void CompareDistributions(PiecewiseConstant1D da, PiecewiseConstant1D db, Float eps = 1e-5f)
    {
        Assert.Equal(da.func.Length, db.func.Length);
        Assert.Equal(da.cdf.Length, db.cdf.Length);
        Assert.Equal(da.min, db.min);
        Assert.Equal(da.max, db.max);
        for (int i = 0; i < da.func.Length; ++i)
        {
            Float pdfa = da.func[i] / da.funcInt, pdfb = db.func[i] / db.funcInt;
            Float err = FMath.Abs(pdfa - pdfb) / ((pdfa + pdfb) / 2);
            Assert.True(err < eps, $"{pdfa} - {pdfb}");
        }
    }

    // PiecewiseConstant2D::TestCompareDistributions
    public static void CompareDistributions(PiecewiseConstant2D da, PiecewiseConstant2D db, Float eps = 1e-5f)
    {
        CompareDistributions(da.Marginal, db.Marginal, eps);

        Assert.Equal(da.Conditionals.Count, db.Conditionals.Count);
        Assert.Equal(da.Domain(), db.Domain());
        for (int i = 0; i < da.Conditionals.Count; ++i)
            CompareDistributions(da.Conditionals[i], db.Conditionals[i], eps);
    }
}

public class SampleDiscreteTests
{
    [Fact]
    public void Basics()
    {
        Assert.Equal(0, SampleDiscrete([5f], 0.251f, out Float pdf, out _));
        Assert.Equal(1, pdf);

        Assert.Equal(0, SampleDiscrete([0.5f, 0.5f], 0f, out pdf, out _));
        Assert.Equal(0.5f, pdf);

        Assert.Equal(0, SampleDiscrete([0.5f, 0.5f], 0.499f, out pdf, out _));
        Assert.Equal(0.5f, pdf);

        Assert.Equal(1, SampleDiscrete([0.5f, 0.5f], 0.5f, out pdf, out Float uRemapped));
        Assert.Equal(0.5f, pdf);
        Assert.Equal(0, uRemapped);
    }

    [Fact]
    public void VsPiecewiseConstant1D()
    {
        var values = new List<Float>();
        RNG rng = new();
        for (int i = 0; i < 15; ++i)
            values.Add(Max(0, rng.Uniform<Float>() - .2f));

        PiecewiseConstant1D dist = new(values.ToArray());

        for (int i = 0; i < 100; ++i)
        {
            Float u = rng.Uniform<Float>();
            int sdIndex = SampleDiscrete(values.ToArray(), u, out Float sdPDF, out _);

            Float pcOffset = dist.Sample(u, out Float pcPDF, out _);
            int pcIndex = (int)(pcOffset * values.Count);
            pcPDF /= values.Count;

            Assert.Equal(sdIndex, pcIndex);
            Assert.True(FMath.Abs(sdPDF - pcPDF) < 1e-3, $"{u}");
        }
    }
}

public class SamplingInversionTests
{
    private static void CheckClose(Point2f u, Point2f up, object mid)
    {
        Assert.True(FMath.Abs(u.x - up.x) < 1e-3, $"u {u} -> {mid} -> {up}");
        Assert.True(FMath.Abs(u.y - up.y) < 1e-3, $"u {u} -> {mid} -> {up}");
    }

    [Fact]
    public void InvertUniformHemisphere()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Vector3f v = SampleUniformHemisphere(u);
            CheckClose(u, InvertUniformHemisphereSample(v), v);
        }
    }

    [Fact]
    public void InvertCosineHemisphere()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Vector3f v = SampleCosineHemisphere(u);
            CheckClose(u, InvertCosineHemisphereSample(v), v);
        }
    }

    [Fact]
    public void InvertUniformSphere()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Vector3f v = SampleUniformSphere(u);
            CheckClose(u, InvertUniformSphereSample(v), v);
        }
    }

    [Fact]
    public void InvertUniformTriangle()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Array3<Float> b = SampleUniformTriangle(u);
            CheckClose(u, InvertUniformTriangleSample(b), $"{b[0]}, {b[1]}, {b[2]}");
        }
    }

    [Fact]
    public void InvertUniformCone()
    {
        RNG rng = new();
        foreach (Point2f u in Uniform2D(1000))
        {
            Float cosThetaMax = rng.Uniform<Float>();
            Vector3f v = SampleUniformCone(u, cosThetaMax);
            CheckClose(u, InvertUniformConeSample(v, cosThetaMax), v);
        }
    }

    [Fact]
    public void InvertUniformDiskPolar()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Point2f p = SampleUniformDiskPolar(u);
            CheckClose(u, InvertUniformDiskPolarSample(p), p);
        }
    }

    [Fact]
    public void InvertUniformDiskConcentric()
    {
        foreach (Point2f u in Uniform2D(1000))
        {
            Point2f p = SampleUniformDiskConcentric(u);
            CheckClose(u, InvertUniformDiskConcentricSample(p), p);
        }
    }
}

public class LowDiscrepancyTests
{
    [Fact]
    public void RadicalInverseBase2()
    {
        for (int a = 0; a < 1024; ++a)
            Assert.Equal(ReverseBits32((uint)a) * 2.3283064365386963e-10f, RadicalInverse(0, (ulong)a));
    }

    [Fact]
    public void SobolFirstDimension()
    {
        // Make sure first dimension is the regular base 2 radical inverse
        for (int i = 0; i < 8192; ++i)
            Assert.Equal(ReverseBits32((uint)i) * 2.3283064365386963e-10f,
                         SobolSample(i, 0, new NoRandomizer()));
    }

    [Fact]
    public void SobolIntervalToIndexBasics()
    {
        for (int logRes = 0; logRes < 8; ++logRes)
        {
            int res = 1 << logRes;
            for (int y = 0; y < res; ++y)
            {
                for (int x = 0; x < res; ++x)
                {
                    // For each pixel sample.
                    bool sawCorner = false;
                    for (int s = 0; s < 16; ++s)
                    {
                        ulong seqIndex = SobolIntervalToIndex((uint)logRes, (ulong)s, new Point2i(x, y));
                        Point2f samp = new(SobolSample((long)seqIndex, 0, new NoRandomizer()),
                                           SobolSample((long)seqIndex, 1, new NoRandomizer()));
                        Point2f ss = new(samp[0] * res, samp[1] * res);
                        // Make sure all samples aren't landing at the
                        // lower-left corner.
                        if (ss == new Point2f(x, y))
                        {
                            Assert.False(sawCorner, $"Multi corner ({x}, {y}), res {res}, samp {s}");
                            sawCorner = true;
                        }
                        // Most of the time, the Sobol sample should be within the
                        // pixel's extent. Due to rounding, it may rarely be at the
                        // upper end of the extent; the check here is written
                        // carefully to only accept points just at the upper limit
                        // but not into the next pixel.
                        Assert.True(new Point2i(x, y) == new Point2i(ss) ||
                                    (x == (int)ss.x && (Float)(y + 1) == ss.y) ||
                                    ((Float)(x + 1) == ss.x && y == (int)ss.y),
                                    $"Pixel ({x}, {y}), sample {s}, got {ss}");
                    }
                }
            }
        }
    }

    [Fact]
    public void SobolIntervalToIndexRandoms()
    {
        RNG rng = new();
        for (int i = 0; i < 100000; ++i)
        {
            int logRes = rng.Uniform(16);
            int res = 1 << logRes;
            int x = rng.Uniform(res), y = rng.Uniform(res);
            int s = rng.Uniform(8192);

            ulong seqIndex = SobolIntervalToIndex((uint)logRes, (ulong)s, new Point2i(x, y));
            Point2f samp = new(SobolSample((long)seqIndex, 0, new NoRandomizer()),
                               SobolSample((long)seqIndex, 1, new NoRandomizer()));
            Point2f ss = new((int)(samp[0] * res), (int)(samp[1] * res));
            Assert.True(new Point2i(x, y) == new Point2i(ss) ||
                        (x == (int)ss.x && (Float)(y + 1) == ss.y) ||
                        ((Float)(x + 1) == ss.x && y == (int)ss.y),
                        $"Pixel ({x}, {y}), sample {s}, got {ss}");
        }
    }

    // C# port: randomizers and scrambled radical inverses stay in [0, 1),
    // and digit permutations are permutations.
    [Fact]
    public void ScramblersAndPermutations()
    {
        DigitPermutation[] perms = ComputeRadicalInversePermutations(1234);
        Assert.Equal(PrimeTableSize, perms.Length);
        for (int baseIndex = 0; baseIndex < 8; ++baseIndex)
        {
            int b = Primes[baseIndex];
            var seen = new bool[b];
            for (int v = 0; v < b; ++v)
            {
                int p = perms[baseIndex].Permute(0, v);
                Assert.False(seen[p]);
                seen[p] = true;
            }
            for (ulong a = 0; a < 1000; ++a)
            {
                Float x = ScrambledRadicalInverse(baseIndex, a, perms[baseIndex]);
                Assert.True(x >= 0 && x < 1);
                x = OwenScrambledRadicalInverse(baseIndex, a, 0xdeadbeef);
                Assert.True(x >= 0 && x < 1);
            }
        }

        for (int i = 0; i < 1024; ++i)
        {
            Float a = SobolSample(i, 3, new FastOwenScrambler(17));
            Float b = SobolSample(i, 3, new OwenScrambler(17));
            Float c = SobolSample(i, 3, new BinaryPermuteScrambler(0xabcdef01));
            Assert.True(a >= 0 && a < 1 && b >= 0 && b < 1 && c >= 0 && c < 1);
        }

        for (int y = 0; y < 16; ++y)
            for (int x = 0; x < 16; ++x)
            {
                Float bn = BlueNoiseSample(new Point2i(x, y), 3);
                Assert.True(bn >= 0 && bn < 1);
            }

        // Primes table spot checks
        Assert.Equal(2, Primes[0]);
        Assert.Equal(7919, Primes[PrimeTableSize - 1]);
    }
}

public class PiecewiseConstant1DTests
{
    [Fact]
    public void Continuous()
    {
        PiecewiseConstant1D dist = new([1f, 1f, 2f, 4f, 8f]);
        Assert.Equal(5, dist.Size());

        Assert.Equal(0f, dist.Sample(0f, out Float pdf, out int offset));
        TestHelpers.ExpectFloatEq(dist.Size() * 1.0 / 16.0, pdf);
        Assert.Equal(0, offset);

        // Right at the boundary between the 4 and the 8 segments.
        TestHelpers.ExpectFloatEq(.8, dist.Sample(0.5f, out pdf, out offset));

        // Middle of the 8 segment
        TestHelpers.ExpectFloatEq(.9, dist.Sample(0.75f, out pdf, out offset));
        TestHelpers.ExpectFloatEq(dist.Size() * 8.0 / 16.0, pdf);
        Assert.Equal(4, offset);

        TestHelpers.ExpectFloatEq(0.0, dist.Sample(0f));
        TestHelpers.ExpectFloatEq(1.0, dist.Sample(1f));
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Range()
    {
        Float[] values = Sample1DFunction(x => 1 + x, 65536, 4, -1f, 3f);
        PiecewiseConstant1D dist = new(values, -1f, 3f);
        // p(x) = (1+x) / 8
        // xi = int_{-1}^x p(x) ->
        // xi = 1/16 x^2 + x/8 + 1/16 ->
        // Solve 0 = 1/16 x^2 + x/8 + 1/16 - xi to sample

        foreach (Float u in Uniform1D(100))
        {
            Float xd = dist.Sample(u, out Float pd, out _);

            Assert.True(Quadratic(1f / 16f, 1f / 8f, 1f / 16 - u, out Float t0, out Float t1));
            Float xa = (t0 >= -1 && t0 <= 3) ? t0 : t1;
            Float pa = (1 + xa) / 8;

            Assert.True(FMath.Abs(xd - xa) / xa < 2e-3, $"{xd} vs {xa}");
            Assert.True(FMath.Abs(pd - pa) / pa < 2e-3, $"{pd} vs {pa}");
        }
    }

    [Fact]
    public void InverseUniform()
    {
        Float[] values = [1, 1, 1];

        PiecewiseConstant1D dist = new(values);
        Assert.Equal(0f, dist.Invert(0)!.Value);
        Assert.Equal(0.5f, dist.Invert(0.5f)!.Value);
        Assert.Equal(0.75f, dist.Invert(0.75f)!.Value);

        PiecewiseConstant1D dist2 = new(values, -1, 3);
        Assert.Equal(0f, dist2.Invert(-1)!.Value);
        Assert.Equal(0.25f, dist2.Invert(0)!.Value);
        Assert.Equal(0.5f, dist2.Invert(1)!.Value);
        Assert.Equal(0.75f, dist2.Invert(2)!.Value);
        Assert.Equal(1f, dist2.Invert(3)!.Value);
    }

    [Fact]
    public void InverseGeneral()
    {
        Float[] values = [0, 0.25f, 0.5f, 0.25f];

        PiecewiseConstant1D dist = new(values);
        Assert.Equal(0f, dist.Invert(0)!.Value);
        Assert.Equal(1f, dist.Invert(1)!.Value);
        Assert.Equal(0.25f, dist.Invert(0.5f)!.Value);
        Assert.Equal(0.5f, dist.Invert(0.625f)!.Value);
        Assert.Equal(0.75f, dist.Invert(0.75f)!.Value);
        TestHelpers.ExpectFloatEq(0.825, dist.Invert(0.825f)!.Value);

        PiecewiseConstant1D dist2 = new(values, -1, 3);
        Assert.Equal(0f, dist2.Invert(-1)!.Value);
        Assert.Equal(1f, dist2.Invert(3)!.Value);
        Assert.Equal(0.25f, dist2.Invert(Lerp(0.5f, -1, 3))!.Value);
        Assert.Equal(0.5f, dist2.Invert(Lerp(0.625f, -1, 3))!.Value);
        Assert.Equal(0.75f, dist2.Invert(Lerp(0.75f, -1, 3))!.Value);
        TestHelpers.ExpectFloatEq(0.825, dist2.Invert(Lerp(0.825f, -1, 3))!.Value);
    }

    [Fact]
    public void InverseRandoms()
    {
        Float[] values = [0, 1.25f, 0.5f, 0.25f, 3.7f];

        PiecewiseConstant1D dist = new(values);
        foreach (Float u in Uniform1D(100))
        {
            Float v = dist.Sample(u);
            Float? inv = dist.Invert(v);
            Assert.True(inv.HasValue);
            Float err = Min(FMath.Abs(inv.Value - u), FMath.Abs(inv.Value - u) / u);
            Assert.True(err < 1e-4, $"u {u} vs inv {inv}");
        }

        // (pbrt's test samples dist rather than dist2 here as well.)
        PiecewiseConstant1D dist2 = new(values, -1, 3);
        foreach (Float u in Uniform1D(100))
        {
            Float v = dist.Sample(u);
            Float? inv = dist.Invert(v);
            Assert.True(inv.HasValue);
            Float err = Min(FMath.Abs(inv.Value - u), FMath.Abs(inv.Value - u) / u);
            Assert.True(err < 1e-4, $"u {u} vs inv {inv}");
        }
    }

    [Fact]
    public void Integral()
    {
        Float[] values = [1, 0.5f, 0, 0.25f];

        Assert.Equal(0.4375f, new PiecewiseConstant1D(values).Integral());
        Assert.Equal(1.75f, new PiecewiseConstant1D(values, 0, 4).Integral());
        Assert.Equal(3.5f, new PiecewiseConstant1D(values, -3, 5).Integral());
    }
}

public class PiecewiseConstant2DTests
{
    [Fact]
    public void InverseUniform()
    {
        Float[] values = [1, 1, 1, 1, 1, 1];

        PiecewiseConstant2D dist = new(values, 3, 2);
        Assert.Equal(new Point2f(0, 0), dist.Invert(new Point2f(0, 0))!.Value);
        Assert.Equal(new Point2f(1, 1), dist.Invert(new Point2f(1, 1))!.Value);
        Assert.Equal(new Point2f(0.5f, 0.5f), dist.Invert(new Point2f(0.5f, 0.5f))!.Value);
        Assert.Equal(new Point2f(0.25f, 0.75f), dist.Invert(new Point2f(0.25f, 0.75f))!.Value);

        Bounds2f domain = new(new Point2f(-1, -0.5f), new Point2f(3, 1.5f));
        PiecewiseConstant2D dist2 = new(values, 3, 2, domain);
        Assert.Equal(new Point2f(0, 0), dist2.Invert(domain.Lerp(new Point2f(0, 0)))!.Value);
        Assert.Equal(new Point2f(1, 1), dist2.Invert(domain.Lerp(new Point2f(1, 1)))!.Value);
        Assert.Equal(new Point2f(0.5f, 0.5f), dist2.Invert(domain.Lerp(new Point2f(0.5f, 0.5f)))!.Value);
        Assert.Equal(new Point2f(0.25f, 0.75f), dist2.Invert(domain.Lerp(new Point2f(0.25f, 0.75f)))!.Value);
    }

    [Fact]
    public void InverseRandoms()
    {
        int nx = 4, ny = 5;
        var values = new Float[nx * ny];
        RNG rng = new();
        for (int i = 0; i < nx * ny; ++i)
            values[i] = rng.Uniform<Float>();

        void Check(PiecewiseConstant2D dist, IEnumerable<Point2f> us)
        {
            foreach (Point2f u in us)
            {
                Point2f v = dist.Sample(u);
                Point2f? inv = dist.Invert(v);
                Assert.True(inv.HasValue);
                Point2f err = new(
                    Min(FMath.Abs(inv.Value[0] - u[0]), FMath.Abs(inv.Value[0] - u[0]) / u[0]),
                    Min(FMath.Abs(inv.Value[1] - u[1]), FMath.Abs(inv.Value[1] - u[1]) / u[1]));
                Assert.True(err.x < 1e-4, $"u.x {u.x} vs inv {inv.Value.x} at {v.x}");
                Assert.True(err.y < 1e-4, $"u.y {u.y} vs inv {inv.Value.y} at {v.y}");
            }
        }

        Check(new PiecewiseConstant2D(values, nx, ny), Uniform2D(100));

        Bounds2f domain = new(new Point2f(-1, -0.5f), new Point2f(3, 1.5f));
        Check(new PiecewiseConstant2D(values, nx, ny, domain), Uniform2D(100, 235351));
    }

    [Fact]
    public void FromFuncLInfinity()
    {
        static Float f(Float x, Float y) => x * x * y;
        Array2D<Float> values = Sample2DFunction(f, 4, 2, 1, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
        PiecewiseConstant2D dSampled = new(values.AsSpan(), 4, 2);

        Float[] exact =
        [
            Sqr(0.25f) * 0.5f, Sqr(0.5f) * 0.5f, Sqr(0.75f) * 0.5f, Sqr(1f) * 0.5f,
            Sqr(0.25f) * 1f, Sqr(0.5f) * 1f, Sqr(0.75f) * 1f, Sqr(1f) * 1f,
        ];
        PiecewiseConstant2D dExact = new(exact, 4, 2);
        TestHelpers.CompareDistributions(dSampled, dExact);
    }

    [Fact]
    public void Integral()
    {
        Float[] values = [1, 2, 0, 7, 3, 1, 2, 0];

        Assert.Equal(2f, new PiecewiseConstant2D(values, 4, 2).Integral());
        Assert.Equal(2f, new PiecewiseConstant2D(values, 2, 4).Integral());
        Assert.Equal(16f, new PiecewiseConstant2D(values, 4, 2,
            new Bounds2f(new Point2f(0, 0), new Point2f(4, 2))).Integral());
        Assert.Equal(8f, new PiecewiseConstant2D(values, 4, 2,
            new Bounds2f(new Point2f(-1, 0), new Point2f(0, 4))).Integral());
    }
}

public class SamplingTests
{
    [Fact]
    public void SphericalTriangle()
    {
        int count = 1024 * 1024;
        Point3f[] v = [new(4, 1, 1), new(-10, 3, 3), new(-2, -8, 10)];
        Float A = 0.5f * Length(Cross(v[1] - v[0], v[2] - v[0]));
        Vector3f N = Normalize(Cross(v[1] - v[0], v[2] - v[0]));
        Point3f p = new(.5f, -.4f, .7f);

        // Integrate this function over the projection of the triangle given by
        // |v| at the unit sphere surrounding |p|.
        static Float f(Point3f p) => p.x * p.y * p.z;

        Float sphSum = 0, areaSum = 0;
        for (int i = 0; i < count; ++i)
        {
            Point2f u = new(RadicalInverse(0, (ulong)i), RadicalInverse(1, (ulong)i));

            Array3<Float> bs = SampleSphericalTriangle(v, p, u, out Float pdf);
            Point3f pTri = bs[0] * v[0] + bs[1] * v[1] + bs[2] * v[2];
            sphSum += f(pTri) / pdf;

            Array3<Float> ba = SampleUniformTriangle(u);
            pdf = 1 / A;
            pTri = ba[0] * v[0] + ba[1] * v[1] + (1 - ba[0] - ba[1]) * v[2];
            areaSum += f(pTri) * AbsDot(N, Normalize(pTri - p)) / (pdf * DistanceSquared(p, pTri));
        }
        Float sphInt = sphSum / count;
        Float areaInt = areaSum / count;

        Assert.True(FMath.Abs(areaInt - sphInt) < 1e-3, $"{areaInt} vs {sphInt}");
    }

    [Fact]
    public void SphericalTriangleInverse()
    {
        RNG rng = new();
        Point3f rp(Float low = -10, Float high = 10) =>
            new(Lerp(rng.Uniform<Float>(), low, high), Lerp(rng.Uniform<Float>(), low, high),
                Lerp(rng.Uniform<Float>(), low, high));

        for (int i = 0; i < 100; ++i)
        {
            Point3f[] v = [rp(), rp(), rp()];
            Point3f p = rp(-1, 1);
            if (SphericalTriangleArea(Normalize(v[0] - p), Normalize(v[1] - p), Normalize(v[2] - p)) < 5e-3)
                continue;

            for (int j = 0; j < 100; ++j)
            {
                Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Array3<Float> bs = SampleSphericalTriangle(v, p, u, out _);

                Point3f pTri = bs[0] * v[0] + bs[1] * v[1] + bs[2] * v[2];

                Point2f ui = InvertSphericalTriangleSample(v, p, Normalize(pTri - p));

                static Float err(Float a, Float rf) =>
                    rf < 0.05f ? FMath.Abs(a - rf) : FMath.Abs((a - rf) / rf);
                // The tolerance has to be fairly high, unfortunately...
                Assert.True(FMath.Abs(ui[0] - u[0]) < 0.025f, $"{u} vs inverse {ui}");
                Assert.True(err(ui[1], u[1]) < 0.005f, $"{u} vs inverse {ui}");
            }
        }
    }

    [Fact]
    public void SphericalQuad()
    {
        int count = 1024 * 1024;
        Point3f[] v = [new(4, 1, 1), new(6, 1, -2), new(4, 4, 1), new(6, 4, -2)];
        Float A = Length(v[0] - v[1]) * Length(v[0] - v[2]);
        Vector3f N = Normalize(Cross(v[1] - v[0], v[2] - v[0]));
        Point3f p = new(.5f, -.4f, .7f);

        // Integrate this function over the projection of the quad given by
        // |v| at the unit sphere surrounding |p|.
        static Float f(Point3f p) => p.x * p.y * p.z;

        Float sphSum = 0, areaSum = 0;
        foreach (Point2f u in Hammersley2D(count))
        {
            Point3f pq = SampleSphericalRectangle(p, v[0], v[1] - v[0], v[2] - v[0], u, out Float pdf);
            sphSum += f(pq) / pdf;

            pq = Lerp(u[1], Lerp(u[0], v[0], v[1]), Lerp(u[0], v[2], v[3]));
            pdf = 1 / A;
            areaSum += f(pq) * AbsDot(N, Normalize(pq - p)) / (pdf * DistanceSquared(p, pq));
        }
        Float sphInt = sphSum / count;
        Float areaInt = areaSum / count;

        Assert.True(FMath.Abs(areaInt - sphInt) < 1e-3, $"area {areaInt} sph {sphInt}");
    }

    [Fact]
    public void SphericalQuadInverse()
    {
        int count = 64 * 1024;
        Point3f[,] v = { { new(4, 1, 1), new(6, 1, -2) }, { new(4, 4, 1), new(6, 4, -2) } };

        RNG rng = new();
        int nTested = 0;
        foreach (Point2f u in Hammersley2D(count))
        {
            int a = rng.Uniform<int>() & 1, b = rng.Uniform<int>() & 1;

            Float px = Lerp(rng.Uniform<Float>(), -10, 10);
            Float py = Lerp(rng.Uniform<Float>(), -10, 10);
            Float pz = Lerp(rng.Uniform<Float>(), -10, 10);
            Point3f p = new(px, py, pz);
            Point3f pq = SampleSphericalRectangle(p, v[a, b], v[1 - a, b] - v[a, b],
                                                  v[a, 1 - b] - v[a, b], u, out Float pdf);

            Float solidAngle = 1 / pdf;
            if (solidAngle < .01)
                continue;
            ++nTested;
            Point2f ui = InvertSphericalRectangleSample(p, v[a, b], v[1 - a, b] - v[a, b],
                                                        v[a, 1 - b] - v[a, b], pq);

            static Float err(Float a, Float rf) =>
                rf < 1e-2 ? FMath.Abs(a - rf) : FMath.Abs((a - rf) / rf);
            // The tolerance has to be fairly high, unfortunately...
            // FIXME: super high for now to find the really bad cases...
            Assert.True(err(ui[0], u[0]) < 0.01, $"{u} vs inverse {ui}, solid angle {1 / pdf}");
            Assert.True(err(ui[1], u[1]) < 0.01, $"{u} vs inverse {ui}, solid angle {1 / pdf}");
        }
        Assert.True(nTested > count / 2);
    }

    [Fact]
    public void SmoothStep()
    {
        Float start = SampleSmoothStep(0, 10, 20);
        // Fairly high slop since lots of values close to the start are close
        // to zero.
        Assert.True(FMath.Abs(start - 10) < .1, $"{start}");

        Float end = SampleSmoothStep(1, -10, -5);
        Assert.True(FMath.Abs(end - -5) < 1e-5, $"{end}");

        Float mid = SampleSmoothStep(0.5f, 0, 1);
        // Solved this numericalla in Mathematica.
        Assert.True(FMath.Abs(mid - 0.733615f) < 1e-5, $"{mid}");

        foreach (Float u in Uniform1D(1000))
        {
            Float x = SampleSmoothStep(u, -3, 5);
            Float ratio = Util.SmoothStep(x, -3, 5) / SmoothStepPDF(x, -3, 5);
            // SmoothStep over [-3,5] integrates to 4.
            Assert.True(FMath.Abs(ratio - 4) < 1e-5, $"{ratio}");

            // NOTE: pbrt's InvertSmoothStepSample() returns NaN (its P()
            // ignores its argument), for which checkErr() is false, so this
            // passes there as well.
            Assert.False(TestHelpers.CheckErr(u, InvertSmoothStepSample(x, -3, 5)));
        }

        Float[] values = Sample1DFunction(v => Util.SmoothStep(v, 0, 1), 1024, 64 * 1024, 0f, 1f);
        PiecewiseConstant1D distrib = new(values);
        foreach (Float u in Uniform1D(100, 62351))
        {
            Float cx = SampleSmoothStep(u, 0, 1);
            Float cp = SmoothStepPDF(cx, 0, 1);

            Float dx = distrib.Sample(u, out Float dp, out _);
            Assert.True(FMath.Abs(cx - dx) < 3e-3, $"Closed form = {cx}, distrib = {dx}");
            Assert.True(FMath.Abs(cp - dp) < 3e-3, $"Closed form PDF = {cp}, distrib PDF = {dp}");
        }
    }

    [Fact]
    public void Linear()
    {
        int nBuckets = 32;
        var buckets = new int[nBuckets];

        int[][] ranges = [[0, 1], [1, 2], [5, 50], [100, 0], [75, 50]];
        foreach (int[] r in ranges)
        {
            Float f0 = r[0], f1 = r[1];
            int nSamples = 1000000;
            for (int i = 0; i < nSamples; ++i)
            {
                Float u = (Float)((i + .5) / nSamples);
                Float t = SampleLinear(u, f0, f1);
                ++buckets[Min((int)(t * nBuckets), nBuckets - 1)];
            }

            for (int i = 0; i < nBuckets; ++i)
            {
                int expected = (int)Lerp((Float)i / (nBuckets - 1), buckets[0], buckets[nBuckets - 1]);
                Assert.True(buckets[i] >= .99 * expected);
                Assert.True(buckets[i] <= 1.01 * expected);
            }
        }

        Float[] values = Sample1DFunction(v => 1 + 3 * v, 1024, 64 * 1024, 0f, 1f);
        PiecewiseConstant1D distrib = new(values);
        foreach (Float u in Uniform1D(100))
        {
            Float cx = SampleLinear(u, 1, 4);
            Float cp = LinearPDF(cx, 1, 4);

            Float dx = distrib.Sample(u, out Float dp, out _);
            Assert.True(FMath.Abs(cx - dx) < 3e-3, $"Closed form = {cx}, distrib = {dx}");
            Assert.True(FMath.Abs(cp - dp) < 3e-3, $"Closed form PDF = {cp}, distrib PDF = {dp}");
        }

        RNG rng = new();
        foreach (Float u in Uniform1D(100))
        {
            Float low = rng.Uniform<Float>() * 10;
            Float high = rng.Uniform<Float>() * 10;
            if (low < high)
                (low, high) = (high, low);
            Float x = SampleLinear(u, low, high);

            Assert.False(TestHelpers.CheckErr(u, InvertLinearSample(x, low, high)),
                         $" u = {u} -> x {x} -> {InvertLinearSample(x, low, high)} (over {low} - {high}");
        }
    }

    [Fact]
    public void Tent()
    {
        // Make sure stratification is preserved at the midpoint of the
        // sampling domain.
        Float dist = FMath.Abs(SampleTent(.501f, 1) - SampleTent(.499f, 1));
        Assert.True(dist < .01);

        Float[] rad = [1, 2.5f, .125f];
        RNG rng = new();
        foreach (Float radius in rad)
        {
            Float tent(Float x) => Max(0, 1 - FMath.Abs(x) / radius);

            Float[] values = Sample1DFunction(tent, 8192, 64, -radius, radius);
            PiecewiseConstant1D distrib = new(values, -radius, radius);
            for (int i = 0; i < 100; ++i)
            {
                Float u = rng.Uniform<Float>();
                Float tx = SampleTent(u, radius);
                Float tp = TentPDF(tx, radius);

                Float dx = distrib.Sample(u, out Float dp, out _);
                Assert.True(FMath.Abs(tx - dx) < 3e-3, $"Closed form = {tx}, distrib = {dx}");
                Assert.True(FMath.Abs(tp - dp) < 3e-3, $"Closed form PDF = {tp}, distrib PDF = {dp}");

                Assert.False(TestHelpers.CheckErr(u, InvertTentSample(tx, radius)),
                             $"u {u} radius {radius} x {tx} inverse {InvertTentSample(tx, radius)}");
            }
        }
    }

    [Fact]
    public void CatmullRom()
    {
        Float[] nodes = [0, .1f, .4f, .88f, 1];
        Float[] values = [0, 5, 2, 10, 5];
        Float[] cdf = new Float[values.Length];

        IntegrateCatmullRom(nodes, values, cdf);

        PiecewiseConstant1D distrib = new(Sample1DFunction(v => Util.CatmullRom(nodes, values, v), 8192, 1024, 0f, 1f));
        foreach (Float u in Uniform1D(100))
        {
            Float cx = SampleCatmullRom(nodes, values, cdf, u, out _, out Float cp);

            Float dx = distrib.Sample(u, out Float dp, out _);
            Assert.True(FMath.Abs(cx - dx) < 3e-3, $"Closed form = {cx}, distrib = {dx}");
            Assert.True(FMath.Abs(cp - dp) < 3e-3, $"Closed form PDF = {cp}, distrib PDF = {dp}");
        }
    }

    // C# port: SampleCatmullRom2D() with a single (repeated) row matches the
    // 1D version.
    [Fact]
    public void CatmullRom2D()
    {
        Float[] nodes1 = [0, 1, 2];
        Float[] nodes2 = [0, .1f, .4f, .88f, 1];
        Float[] row = [0, 5, 2, 10, 5];
        Float[] rowCdf = new Float[row.Length];
        IntegrateCatmullRom(nodes2, row, rowCdf);
        Float[] values = [.. row, .. row, .. row];
        Float[] cdf = [.. rowCdf, .. rowCdf, .. rowCdf];

        foreach (Float u in Uniform1D(100))
        {
            Float x1 = SampleCatmullRom(nodes2, row, rowCdf, u, out Float f1, out Float p1);
            Float x2 = SampleCatmullRom2D(nodes1, nodes2, values, cdf, 0.7f, u, out Float f2, out Float p2);
            Assert.True(FMath.Abs(x1 - x2) < 1e-4f, $"{x1} vs {x2}");
            Assert.True(FMath.Abs(f1 - f2) < 1e-3f, $"{f1} vs {f2}");
            Assert.True(FMath.Abs(p1 - p2) < 1e-3f, $"{p1} vs {p2}");
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void Bilinear()
    {
        Float[][] quads = [[0, .5f, 1.3f, 4.7f], [1, 1, 1, 1], [11, .25f, 1, 20]];
        foreach (Float[] v in quads)
        {
            Float bilerp(Float x, Float y) =>
                (1 - x) * (1 - y) * v[0] + x * (1 - y) * v[1] + (1 - x) * y * v[2] + x * y * v[3];

            Array2D<Float> values = Sample2DFunction(bilerp, 1024, 1024, 16,
                                                     new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            PiecewiseConstant2D distrib = new(values.AsSpan(), 1024, 1024);
            foreach (Point2f u in Uniform2D(100))
            {
                Point2f pb = SampleBilinear(u, v);
                Float bp = BilinearPDF(pb, v);

                Point2f pd = distrib.Sample(u, out Float dp, out _);
                Assert.True(FMath.Abs(pb[0] - pd[0]) < 3e-3, $"X: Closed form = {pb[0]}, distrib = {pd[0]}");
                Assert.True(FMath.Abs(pb[1] - pd[1]) < 3e-3, $"Y: Closed form = {pb[1]}, distrib = {pd[1]}");
                Assert.True(FMath.Abs(bp - dp) < 3e-3, $"Closed form PDF = {bp}, distrib PDF = {dp}");

                // Check InvertBilinear...
                Point2f up = InvertBilinearSample(pb, v);
                Assert.True(FMath.Abs(up[0] - u[0]) < 3e-3, $"Invert failure: u[0] = {u[0]}, p = {pb[0]}, up = {up[0]}");
                Assert.True(FMath.Abs(up[1] - u[1]) < 3e-3, $"Invert failure: u[1] = {u[1]}, p = {pb[1]}, up = {up[1]}");
            }
        }
    }

    [Fact]
    public void Logistic()
    {
        Float[][] parameters = [[1, -Pi, Pi], [5, 0, 3], [.25f, -5, -3]];
        foreach (Float[] p in parameters)
        {
            Float s = p[0], a = p[1], b = p[2];

            Float[] values = Sample1DFunction(v => TrimmedLogistic(v, s, a, b), 8192, 16, a, b);
            PiecewiseConstant1D distrib = new(values, a, b);
            foreach (Float u in Uniform1D(100))
            {
                Float cx = SampleTrimmedLogistic(u, s, a, b);
                Float cp = TrimmedLogisticPDF(cx, s, a, b);

                Float dx = distrib.Sample(u, out Float dp, out _);
                Assert.True(FMath.Abs(cx - dx) < 3e-3, $"Closed form = {cx}, distrib = {dx}");
                Assert.True(FMath.Abs(cp - dp) < 3e-3, $"Closed form PDF = {cp}, distrib PDF = {dp}");

                Assert.False(TestHelpers.CheckErr(u, InvertTrimmedLogisticSample(cx, s, a, b)),
                             $"u = {u} -> x = {cx} -> ... {InvertTrimmedLogisticSample(cx, s, a, b)}");
            }
        }
    }

    [Fact]
    public void TrimmedExponential()
    {
        Float[][] parameters = [[1, 2], [5, 10], [.25f, 20]];
        foreach (Float[] p in parameters)
        {
            Float c = p[0], xMax = p[1];

            Float[] values = Sample1DFunction(x => FMath.Exp(-c * x), 32768, 16, 0, xMax);
            PiecewiseConstant1D distrib = new(values, 0, xMax);
            foreach (Float u in Uniform1D(100))
            {
                Float cx = SampleTrimmedExponential(u, c, xMax);
                Float cp = TrimmedExponentialPDF(cx, c, xMax);

                Float dx = distrib.Sample(u, out Float dp, out _);
                Assert.True(FMath.Abs(cx - dx) < 1e-2, $"Closed form = {cx}, distrib = {dx}");
                Assert.True(FMath.Abs(cp - dp) < 1e-2, $"Closed form PDF = {cp}, distrib PDF = {dp}");

                Assert.False(TestHelpers.CheckErr(u, InvertTrimmedExponentialSample(cx, c, xMax)),
                             $"u = {u} -> x = {cx} -> ... {InvertTrimmedExponentialSample(cx, c, xMax)}");
            }
        }
    }

    [Fact]
    public void Normal()
    {
        Float[][] parameters = [[0, 1], [-.5f, .8f], [.25f, .005f], [3.6f, 1.6f]];
        foreach (Float[] p in parameters)
        {
            Float mu = p[0], sigma = p[1];
            Float normal(Float x) =>
                1 / FMath.Sqrt(2 * Pi * sigma * sigma) * FMath.Exp(-Sqr(x - mu) / (2 * sigma * sigma));
            Float[] values = Sample1DFunction(normal, 8192, 16, mu - 7 * sigma, mu + 7 * sigma);
            PiecewiseConstant1D distrib = new(values, mu - 7 * sigma, mu + 7 * sigma);

            foreach (Float u in Uniform1D(100))
            {
                Float cx = SampleNormal(u, mu, sigma);
                Float cp = NormalPDF(cx, mu, sigma);

                Float dx = distrib.Sample(u, out Float dp, out _);
                Assert.True(FMath.Abs(cx - dx) < 3e-3, $"Closed form = {cx}, distrib = {dx}");
                Assert.True(FMath.Abs(cp - dp) / dp < .025, $"Closed form PDF = {cp}, distrib PDF = {dp}");

                Assert.False(TestHelpers.CheckErr(u, InvertNormalSample(cx, mu, sigma)),
                             $" u {u} -> x = {cx} -> {InvertNormalSample(cx, mu, sigma)} with mu {mu} and sigma {sigma}");
            }
        }
    }

    [Fact]
    public void HGExtremes()
    {
        foreach (Float g in new Float[] { -.999f, .999f, -1f, 1f })
        {
            foreach (Float u in new Float[] { 0, OneMinusEpsilon })
            {
                Vector3f w = SampleHenyeyGreenstein(new Vector3f(0, 0, -1), g, new Point2f(u, 0.5f), out _);
                Assert.False(w.HasNaN());
            }
        }
    }

    // C# port: the HG sampling pdf matches the phase function.
    [Fact]
    public void HenyeyGreensteinPdf()
    {
        Vector3f wo = Normalize(new Vector3f(.3f, -.2f, .9f));
        foreach (Float g in new Float[] { -.7f, 0, .3f, .9f })
        {
            foreach (Point2f u in Uniform2D(100))
            {
                Vector3f wi = SampleHenyeyGreenstein(wo, g, u, out Float pdf);
                Assert.True(FMath.Abs(Length(wi) - 1) < 1e-4f);
                Assert.True(FMath.Abs(pdf - HenyeyGreenstein(Dot(wo, wi), g)) < 1e-3f * Max(1, pdf));
            }
        }
    }
}

public class VarianceEstimatorTests
{
    [Fact]
    public void Zero()
    {
        VarianceEstimator<Float> ve = default;
        for (int i = 0; i < 100; ++i)
            ve.Add(10);
        Assert.Equal(0, ve.Variance());
    }

    [Fact]
    public void VsClosedForm()
    {
        VarianceEstimator<double> ve = default;
        int count = 10000;
        double sum = 0;
        foreach (Float u in Stratified1D(count))
        {
            Float v = Lerp(u, -1, 1);
            ve.Add(v);
            sum += v;
        }

        // f(x) = 0, random variables x_i uniform in [-1,1] ->
        // variance is E[x^2] on [-1,1] == 1/3
        double err = Math.Abs(ve.Variance() - 1.0 / 3.0);
        Assert.True(err < 1e-3, $"{ve.Variance()}");

        err = Math.Abs((sum / count - ve.Mean()) / (sum / count));
        Assert.True(err < 1e-5);
    }

    [Fact]
    public void Merge()
    {
        int n = 16;
        var ve = new VarianceEstimator<double>[n];

        RNG rng = new();
        int count = 10000;
        double sum = 0;
        foreach (Float u in Stratified1D(count))
        {
            Float v = Lerp(u, -1, 1);
            int index = rng.Uniform(ve.Length);
            ve[index].Add(v);
            sum += v;
        }

        VarianceEstimator<double> veFinal = default;
        foreach (var v in ve)
            veFinal.Merge(v);

        // f(x) = 0, random variables x_i uniform in [-1,1] ->
        // variance is E[x^2] on [-1,1] == 1/3
        double err = Math.Abs(veFinal.Variance() - 1.0 / 3.0);
        Assert.True(err < 1e-3, $"{veFinal.Variance()}");

        err = Math.Abs((sum / count - veFinal.Mean()) / (sum / count));
        Assert.True(err < 1e-5);
    }

    [Fact]
    public void MergeTwo()
    {
        var ve = new VarianceEstimator<double>[2];
        VarianceEstimator<double> veBoth = default;

        int index = 0;
        foreach (Float u in Stratified1D(1000))
        {
            if (index++ < 400)
                ve[0].Add(u * u * u);
            else
                ve[1].Add(u * u * u);
            veBoth.Add(u * u * u);
        }

        ve[0].Merge(ve[1]);

        double meanErr = Math.Abs(ve[0].Mean() - veBoth.Mean()) / veBoth.Mean();
        Assert.True(meanErr < 1e-5);

        double varError = Math.Abs(ve[0].Variance() - veBoth.Variance()) / veBoth.Variance();
        Assert.True(varError < 1e-5);
    }
}

public class WeightedReservoirTests
{
    [Fact]
    public void Basic()
    {
        RNG rng = new();
        const int n = 16;
        var weights = new float[n];
        for (int i = 0; i < n; ++i)
            weights[i] = (float)(.05f + Sqr(rng.Uniform<Float>()));

        var count = new int[n];
        long nTrials = 1000000;
        for (long i = 0; i < nTrials; ++i)
        {
            WeightedReservoirSampler<int> wrs = new((ulong)i);
            for (int j = 0; j < n; ++j)
                wrs.Add(j, weights[j]);

            int index = wrs.GetSample();
            Assert.True(index >= 0 && index < n);
            ++count[index];
        }

        Float sumW = weights.Sum();
        for (int i = 0; i < n; ++i)
        {
            Assert.True(.97 * count[i] / (double)nTrials <= weights[i] / sumW);
            Assert.True(1.03 * count[i] / (double)nTrials >= weights[i] / sumW);
        }
    }

    [Fact]
    public void MergeReservoirs()
    {
        RNG rng = new(6502);
        const int n = 8;
        var weights = new float[n];
        for (int i = 0; i < n; ++i)
            weights[i] = (float)(.01f + rng.Uniform<Float>());

        var count = new int[n];
        long nTrials = 1000000;
        for (long i = 0; i < nTrials; ++i)
        {
            WeightedReservoirSampler<int> wrs0 = new((ulong)i);
            WeightedReservoirSampler<int> wrs1 = new((ulong)(i + 1));

            for (int j = 0; j < n; ++j)
            {
                if ((j & 1) != 0)
                    wrs0.Add(j, weights[j]);
                else
                    wrs1.Add(j, weights[j]);
            }

            wrs0.Merge(wrs1);
            ++count[wrs0.GetSample()];
        }

        Float sumW = weights.Sum();
        for (int i = 0; i < n; ++i)
        {
            Assert.True(.98 * count[i] / (double)nTrials <= weights[i] / sumW);
            Assert.True(1.02 * count[i] / (double)nTrials >= weights[i] / sumW);
        }
    }
}

public class GeneratorTests
{
    [Fact]
    public void Uniform1DBasics()
    {
        int count = 0;
        foreach (Float u in Uniform1D(120))
        {
            Assert.True(u >= 0 && u < 1);
            ++count;
        }
        Assert.Equal(120, count);
    }

    [Fact]
    public void Uniform1DSeed()
    {
        var samples = Uniform1D(1250).ToList();

        // Different seed
        int i = 0;
        foreach (Float u in Uniform1D(samples.Count, 1))
        {
            Assert.NotEqual(samples[i], u);
            ++i;
        }
    }

    [Fact]
    public void Uniform2DBasics()
    {
        int count = 0;
        foreach (Point2f u in Uniform2D(32))
        {
            Assert.True(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1);
            ++count;
        }
        Assert.Equal(32, count);
    }

    [Fact]
    public void Uniform2DSeed()
    {
        var samples = Uniform2D(83).ToList();

        // Different seed
        int i = 0;
        foreach (Point2f u in Uniform2D(samples.Count, 1))
        {
            Assert.NotEqual(samples[i], u);
            ++i;
        }
    }

    [Fact]
    public void Uniform3DBasics()
    {
        int count = 0;
        foreach (Point3f u in Uniform3D(32))
        {
            Assert.True(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1 && u[2] >= 0 && u[2] < 1);
            ++count;
        }
        Assert.Equal(32, count);
    }

    [Fact]
    public void Stratified1DBasics()
    {
        int count = 0, n = 128; // power of 2
        foreach (Float u in Stratified1D(n))
        {
            Assert.True(u >= (Float)count / n && u < (Float)(count + 1) / n);
            ++count;
        }
        Assert.Equal(n, count);
    }

    [Fact]
    public void Stratified2DBasics()
    {
        int count = 0, nx = 16, ny = 4; // power of 2
        foreach (Point2f u in Stratified2D(nx, ny))
        {
            int ix = count % nx;
            int iy = count / nx;
            Assert.True(u[0] >= (Float)ix / nx && u[0] < (Float)(ix + 1) / nx);
            Assert.True(u[1] >= (Float)iy / ny && u[1] < (Float)(iy + 1) / ny);
            ++count;
        }
        Assert.Equal(nx * ny, count);
    }

    [Fact]
    public void Stratified3DBasics()
    {
        int count = 0, nx = 4, ny = 32, nz = 8; // power of 2
        foreach (Point3f u in Stratified3D(nx, ny, nz))
        {
            int ix = count % nx;
            int iy = (count / nx) % ny;
            int iz = count / (nx * ny);
            Assert.True(u[0] >= (Float)ix / nx && u[0] < (Float)(ix + 1) / nx);
            Assert.True(u[1] >= (Float)iy / ny && u[1] < (Float)(iy + 1) / ny);
            Assert.True(u[2] >= (Float)iz / nz && u[2] < (Float)(iz + 1) / nz);
            ++count;
        }
        Assert.Equal(nx * ny * nz, count);
    }

    [Fact]
    public void Hammersley2DBasics()
    {
        int count = 0;
        foreach (Point2f u in Hammersley2D(32))
        {
            Assert.Equal((Float)count / 32f, u[0]);
            Assert.Equal(RadicalInverse(0, (ulong)count), u[1]);
            ++count;
        }
        Assert.Equal(32, count);
    }

    [Fact]
    public void Hammersley3DBasics()
    {
        int count = 0;
        foreach (Point3f u in Hammersley3D(128))
        {
            Assert.Equal((Float)count / 128f, u[0]);
            Assert.Equal(RadicalInverse(0, (ulong)count), u[1]);
            Assert.Equal(RadicalInverse(1, (ulong)count), u[2]);
            ++count;
        }
        Assert.Equal(128, count);
    }
}

public class AliasTableTests
{
    [Fact]
    public void Uniform()
    {
        Float[] values = [2, 2, 2, 2, 2];

        AliasTable table = new(values);
        Assert.Equal(5, table.Size());

        var count = new int[5];
        int iters = 10000;
        foreach (Float u in Stratified1D(iters))
        {
            int offset = table.Sample(u, out Float pdf, out _);
            Assert.True(offset >= 0 && offset < 5);
            ++count[offset];
            Assert.True(pdf > .99 * 1f / 5f);
            Assert.True(pdf < 1.01 * 1f / 5f);
        }

        for (int i = 0; i < 5; ++i)
        {
            Assert.True((Float)count[i] / iters > .99f * 1f / 5f);
            Assert.True((Float)count[i] / iters < 1.01f * 1f / 5f);
        }
    }

    [Fact]
    public void Varying()
    {
        RNG rng = new();
        var values = new List<Float>();
        int n = 103;
        for (int i = 0; i < n; ++i)
            values.Add(.25f * 2 * rng.Uniform<Float>());

        Float sum = 0;
        foreach (Float v in values)
            sum += v;

        AliasTable table = new(values.ToArray());
        Assert.Equal(n, values.Count);

        var count = new int[n];
        int iters = 1000000;
        foreach (Float u in Stratified1D(iters))
        {
            int offset = table.Sample(u, out Float pdf, out _);
            Assert.True(offset >= 0 && offset < n);
            ++count[offset];

            Float refPdf = values[offset] / sum;
            Assert.True(pdf > .99 * refPdf);
            Assert.True(pdf < 1.01 * refPdf);
        }

        for (int i = 0; i < n; ++i)
        {
            Float pdf = values[i] / sum;
            Assert.True((Float)count[i] / iters > .99f * pdf);
            Assert.True((Float)count[i] / iters < 1.01f * pdf);
        }
    }
}

public class SummedAreaTests
{
    [Fact]
    public void Constant()
    {
        Array2D<Float> v = new(4, 4);

        for (int y = 0; y < v.YSize(); ++y)
            for (int x = 0; x < v.XSize(); ++x)
                v[x, y] = 1;

        SummedAreaTable sat = new(v);

        Assert.Equal(1f, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(1, 1))));
        Assert.Equal(0.5f, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(1, 0.5f))));
        Assert.Equal(0.5f, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(0.5f, 1))));
        Assert.Equal(3f / 16f, sat.Integral(new Bounds2f(new Point2f(0, 0), new Point2f(.25f, .75f))));
        Assert.Equal(3f / 16f, sat.Integral(new Bounds2f(new Point2f(0.5f, 0.25f), new Point2f(0.75f, 1))));
    }

    [Fact]
    public void Rect()
    {
        Array2D<Float> v = new(8, 4);

        for (int y = 0; y < v.YSize(); ++y)
            for (int x = 0; x < v.XSize(); ++x)
                v[x, y] = x + y;

        SummedAreaTable sat = new(v);

        // All boxes that line up with boundaries exactly
        for (int y0 = 0; y0 < v.YSize(); ++y0)
            for (int x0 = 0; x0 < v.XSize(); ++x0)
                for (int y1 = y0; y1 < v.YSize(); ++y1)
                    for (int x1 = x0; x1 < v.XSize(); ++x1)
                    {
                        Float mySum = 0;
                        for (int y = y0; y < y1; ++y)
                            for (int x = x0; x < x1; ++x)
                                mySum += v[x, y];

                        Bounds2f b = new(new Point2f((Float)x0 / v.XSize(), (Float)y0 / v.YSize()),
                                         new Point2f((Float)x1 / v.XSize(), (Float)y1 / v.YSize()));
                        Assert.Equal(mySum / (v.XSize() * v.YSize()), sat.Integral(b));
                    }
    }

    private static readonly (int, int)[] Dims = [(1, 6), (6, 1), (12, 19), (16, 16), (100, 300), (49, 2)];

    [Fact]
    public void Randoms()
    {
        RNG rng = new();
        foreach (var (dx, dy) in Dims)
        {
            Array2D<Float> v = new(dx, dy);

            for (int y = 0; y < v.YSize(); ++y)
                for (int x = 0; x < v.XSize(); ++x)
                    v[x, y] = rng.Uniform(32);

            SummedAreaTable sat = new(v);

            for (int i = 0; i < 100; ++i)
            {
                int ax = rng.Uniform(v.XSize()), ay = rng.Uniform(v.YSize());
                int bx = rng.Uniform(v.XSize()), by = rng.Uniform(v.YSize());
                Bounds2i bi = new(new Point2i(ax, ay), new Point2i(bx, by));
                Bounds2f bf = new(new Point2f((Float)bi.pMin.x / v.XSize(), (Float)bi.pMin.y / v.YSize()),
                                  new Point2f((Float)bi.pMax.x / v.XSize(), (Float)bi.pMax.y / v.YSize()));
                double rf = 0;

                foreach (Point2i p in bi)
                    rf += v[p];
                rf /= v.XSize() * v.YSize();

                double s = sat.Integral(bf);
                if (rf != s)
                    Assert.True(Math.Abs((rf - s) / rf) < 1e-3f, $"ref {rf} s {s}");
            }
        }
    }

    [Fact]
    public void NonCellAligned()
    {
        RNG rng = new();
        foreach (var (dx, dy) in Dims)
        {
            Array2D<Float> v = new(dx, dy);

            for (int y = 0; y < v.YSize(); ++y)
                for (int x = 0; x < v.XSize(); ++x)
                    v[x, y] = rng.Uniform(32);

            SummedAreaTable sat = new(v);

            Float b0 = rng.Uniform<Float>(), b1 = rng.Uniform<Float>();
            Float b2 = rng.Uniform<Float>(), b3 = rng.Uniform<Float>();
            Bounds2f b = new(new Point2f(b0, b1), new Point2f(b2, b3));

            Float sampledSum = 0;
            int nSamples = 100000;
            foreach (Point2f u in Hammersley2D(nSamples))
            {
                Point2f p = b.Lerp(u);
                Point2i pi = new((int)(p.x * v.XSize()), (int)(p.y * v.YSize()));
                sampledSum += v[pi];
            }
            Float sampledResult = sampledSum * b.Area() / nSamples;

            double s = sat.Integral(b);
            if (sampledResult != s)
                Assert.True(Math.Abs((sampledResult - s) / sampledResult) < 1e-3f,
                            $"sampled {sampledResult} s {s}");
        }
    }
}

// C# port: WindowedPiecewiseConstant2D and PiecewiseLinear2D have no tests
// in sampling_test.cpp; basic consistency checks.
public class WindowedAndLinearDistributionTests
{
    [Fact]
    public void WindowedPiecewiseConstant2DPdfMatchesSamples()
    {
        Array2D<Float> f = new(16, 8);
        RNG rng = new(3);
        for (int y = 0; y < 8; ++y)
            for (int x = 0; x < 16; ++x)
                f[x, y] = .1f + rng.Uniform<Float>();
        WindowedPiecewiseConstant2D dist = new(f);

        Bounds2f window = new(new Point2f(.2f, .1f), new Point2f(.7f, .9f));
        foreach (Point2f u in Uniform2D(200))
        {
            Point2f? p = dist.Sample(u, window, out Float pdf);
            Assert.True(p.HasValue);
            Assert.True(Inside(p.Value, window), $"{p}");
            Assert.True(FMath.Abs(pdf - dist.PDF(p.Value, window)) < 1e-4f * Max(1, pdf));
        }
    }

    [Fact]
    public void PiecewiseLinear2DSampleInvert()
    {
        int nx = 9, ny = 7;
        var data = new float[nx * ny];
        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x)
                data[y * nx + x] = 1 + MathF.Sin(x * .7f) * MathF.Cos(y * .4f) * .5f;

        PiecewiseLinear2D dist = new(data, nx, ny);
        foreach (Point2f u in Uniform2D(200))
        {
            PLSample s = dist.Sample(u);
            Assert.True(FMath.Abs(s.pdf - dist.Evaluate(s.p)) < 1e-3f * s.pdf, $"{s.pdf} vs {dist.Evaluate(s.p)}");
            PLSample inv = dist.Invert(s.p);
            Assert.True(FMath.Abs(inv.p.x - u.x) < 1e-3f && FMath.Abs(inv.p.y - u.y) < 1e-3f, $"{u} vs {inv.p}");
        }

        // Conditional on one parameter: two slices.
        var data2 = new float[2 * nx * ny];
        for (int i = 0; i < nx * ny; ++i)
        {
            data2[i] = data[i];
            data2[nx * ny + i] = 2 - data[i] * .5f;
        }
        PiecewiseLinear2D cond = new(data2, nx, ny, [2], [[0f, 1f]]);
        foreach (Point2f u in Uniform2D(100, 7))
        {
            PLSample s0 = cond.Sample(u, 0f);
            PLSample a = dist.Sample(u);
            Assert.True(Distance(s0.p, a.p) < 1e-4f, $"{s0.p} vs {a.p}");
            PLSample s = cond.Sample(u, .3f);
            Assert.True(FMath.Abs(s.pdf - cond.Evaluate(s.p, .3f)) < 1e-3f * s.pdf);
        }
    }
}

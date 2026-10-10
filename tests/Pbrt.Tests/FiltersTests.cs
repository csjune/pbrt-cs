// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/filters_test.cpp, plus checks of Filter.Sample() and
// Filter.Create() (not in pbrt's tests).

namespace Pbrt.Tests;

public class FiltersTests
{
    [Fact]
    public void Sinc_ZeroHandling()
    {
        Float x = 0;
        Float prev = 1;
        for (int i = 0; i < 10000; ++i)
        {
            Float cur = Sinc(x);
            Assert.True(cur <= prev);
            x = NextFloatUp(x);
            prev = cur;
        }

        x = -0f;
        prev = 1;
        for (int i = 0; i < 10000; ++i)
        {
            Float cur = Sinc(x);
            Assert.True(cur <= prev);
            x = NextFloatDown(x);
            prev = cur;
        }
    }

    private static Filter[] MakeFilters(Vector2f radius) =>
        [new BoxFilter(radius), new GaussianFilter(radius), new MitchellFilter(radius),
         new LanczosSincFilter(radius), new TriangleFilter(radius)];

    [Fact]
    public void Filter_ZeroPastRadius()
    {
        foreach (Vector2f r in (Vector2f[])[new(1, 1), new(1.5f, .25f), new(.33f, 5.2f), new(.1f, .1f), new(3, 3)])
        {
            foreach (Filter f in MakeFilters(r))
            {
                Float ry = (Float)(r.y + 1e-3), rx = (Float)(r.x + 1e-3);
                Float mry = (Float)(-r.y - 1e-3), mrx = (Float)(-r.x - 1e-3);
                Assert.Equal(0, f.Evaluate(new Point2f(0, ry)));
                Assert.Equal(0, f.Evaluate(new Point2f(r.x, ry)));
                Assert.Equal(0, f.Evaluate(new Point2f(-r.x, ry)));
                Assert.Equal(0, f.Evaluate(new Point2f(0, mry)));
                Assert.Equal(0, f.Evaluate(new Point2f(r.x, mry)));
                Assert.Equal(0, f.Evaluate(new Point2f(-r.x, mry)));
                Assert.Equal(0, f.Evaluate(new Point2f(rx, 0)));
                Assert.Equal(0, f.Evaluate(new Point2f(rx, r.y)));
                Assert.Equal(0, f.Evaluate(new Point2f(rx, -r.y)));
                Assert.Equal(0, f.Evaluate(new Point2f(mrx, 0)));
                Assert.Equal(0, f.Evaluate(new Point2f(mrx, r.y)));
                Assert.Equal(0, f.Evaluate(new Point2f(mrx, -r.y)));
            }
        }
    }

    private static Float IntegrateFilter(Filter f)
    {
        Float sum = 0;
        int sqrtSamples = 256;
        int nSamples = sqrtSamples * sqrtSamples;
        Float area = 2 * f.Radius().x * 2 * f.Radius().y;
        foreach (Point2f u in Stratified2D(sqrtSamples, sqrtSamples))
        {
            Point2f p = new(Lerp(u.x, -f.Radius().x, f.Radius().x), Lerp(u.y, -f.Radius().y, f.Radius().y));
            sum += f.Evaluate(p);
        }
        return sum / nSamples * area;
    }

    [Fact]
    public void Filter_Integral()
    {
        static bool ApproxEqual(Float a, Float b)
        {
            if (FMath.Max(FMath.Abs(a), FMath.Abs(b)) < 1e-3)
                return FMath.Abs(a - b) < 1e-5;
            else
                return 2 * FMath.Abs(a - b) / FMath.Abs(a + b) < 1e-2;
        }

        foreach (Vector2f r in (Vector2f[])[new(1, 1), new(2.5f, 1), new(1, 2.5f), new(3.4f, 2.5f)])
            foreach (Filter f in MakeFilters(r))
                Assert.True(ApproxEqual(f.Integral(), IntegrateFilter(f)), f.ToString());
    }

    [Fact]
    public void Filter_Sample()
    {
        // The sampled positions are inside the filter's radius, and the
        // weights (f/pdf) average to the filter's integral; for the
        // tabulated filters, the weight is the filter's sign times the
        // integral of its absolute value.
        foreach (Filter f in MakeFilters(new Vector2f(1.5f, 2)))
        {
            double sum = 0;
            int n = 0;
            foreach (Point2f u in Stratified2D(64, 64))
            {
                FilterSample fs = f.Sample(u);
                Assert.True(FMath.Abs(fs.p.x) <= f.Radius().x && FMath.Abs(fs.p.y) <= f.Radius().y);
                sum += fs.weight;
                ++n;
            }
            Float avg = (Float)(sum / n);
            if (f is BoxFilter or TriangleFilter)
                Assert.Equal(1, avg);
            else
            {
                Float expected = f.Integral();
                Assert.True(FMath.Abs(avg - expected) < 0.05f * FMath.Abs(expected) + 0.01f,
                            $"{f.GetType().Name}: {avg} vs {expected}");
            }
        }
    }

    [Fact]
    public void Filter_Create()
    {
        ParameterDictionary empty = new();
        Assert.Equal(new Vector2f(0.5f, 0.5f), Filter.Create("box", empty, null).Radius());
        Assert.Equal(new Vector2f(1.5f, 1.5f), Filter.Create("gaussian", empty, null).Radius());
        Assert.Equal(new Vector2f(2, 2), Filter.Create("mitchell", empty, null).Radius());
        Assert.Equal(new Vector2f(4, 4), Filter.Create("sinc", empty, null).Radius());
        Assert.Equal(new Vector2f(2, 2), Filter.Create("triangle", empty, null).Radius());
        Assert.Throws<FatalErrorException>(() => Filter.Create("lanczos", empty, null));
    }
}

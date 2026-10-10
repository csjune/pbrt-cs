// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/splines_test.cpp

namespace Pbrt.Tests;

public class SplineTests
{
    [Fact]
    public void BezierBounds()
    {
        // Simple
        Point3f[] cp = new Point3f[4];
        Bounds3f b = BoundCubicBezier(cp, 0f, 1f);
        b = Expand(b, 1e-3f * Length(b.Diagonal()));
        for (Float u = 0; u <= 1f; u += 1f / 1024f)
        {
            Point3f p = EvaluateCubicBezier(cp, u);
            Assert.True(Inside(p, b), $"{p} @ u = {u} not in {b}");
        }

        // Randoms...
        RNG rng = new();
        for (int i = 0; i < 1000; ++i)
        {
            for (int j = 0; j < 4; ++j)
                for (int c = 0; c < 3; ++c)
                    cp[j][c] = -5f + 10f * rng.Uniform<Float>();

            b = BoundCubicBezier(cp, 0f, 1f);
            b = Expand(b, 1e-3f * Length(b.Diagonal()));
            for (Float u = 0; u <= 1f; u += 1f / 1024f)
            {
                Point3f p = EvaluateCubicBezier(cp, u);
                Assert.True(Inside(p, b), $"{p} @ u = {u} not in {b}");
            }
        }
    }

    // Not in splines_test.cpp: checks for the port.

    [Fact]
    public void SubdivideAndSegments()
    {
        RNG rng = new(7);
        Point3f[] cp = new Point3f[4];
        for (int j = 0; j < 4; ++j)
            for (int c = 0; c < 3; ++c)
                cp[j][c] = rng.Uniform<Float>();

        // The two halves of a subdivided curve match the original.
        Point3f[] sub = SubdivideCubicBezier(cp);
        Array4<Point3f> seg0 = CubicBezierControlPoints(cp, 0, .5f), seg1 = CubicBezierControlPoints(cp, .5f, 1);
        for (int i = 0; i < 4; ++i)
        {
            Assert.True(Distance(sub[i], seg0[i]) < 1e-5f);
            Assert.True(Distance(sub[3 + i], seg1[i]) < 1e-5f);
        }
        for (Float u = 0; u <= 1; u += 1f / 64)
        {
            Point3f a = EvaluateCubicBezier(cp, u / 2), b = EvaluateCubicBezier(sub.AsSpan(0, 4), u);
            Assert.True(Distance(a, b) < 1e-5f, $"{a} vs {b}");
        }

        // Derivative matches finite differences.
        Point3f p = EvaluateCubicBezier(cp, .3f, out Vector3f d);
        Vector3f fd = (EvaluateCubicBezier(cp, .3f + 1e-3f) - EvaluateCubicBezier(cp, .3f - 1e-3f)) / 2e-3f;
        Assert.True(Length(d - fd) < 1e-2f * Length(d), $"{d} vs {fd}");
        Assert.True(Distance(p, EvaluateCubicBezier(cp, .3f)) < 1e-6f);

        // Elevating a quadratic doesn't change the curve.
        Point3f[] q = [cp[0], cp[1], cp[2]];
        Array4<Point3f> e = ElevateQuadraticBezierToCubic(q);
        for (Float u = 0; u <= 1; u += 1f / 16)
        {
            Point3f quad = Lerp(u, Lerp(u, q[0], q[1]), Lerp(u, q[1], q[2]));
            Assert.True(Distance(quad, EvaluateCubicBezier(e, u)) < 1e-5f);
        }
    }
}

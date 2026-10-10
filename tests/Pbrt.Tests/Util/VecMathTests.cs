// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/vecmath_test.cpp
//
// The DirectionCone Union and SphericalTriangleArea rotation tests are in
// TransformTests.cs.

namespace Pbrt.Tests;

public class Vector2Tests
{
    [Fact]
    public void Basics()
    {
        Vector2f vf = new(-1, 10);
        Assert.Equal(vf, new Vector2f(new Vector2i(-1, 10)));
        Assert.NotEqual(vf, new Vector2f(-1, 100));
        Assert.Equal(new Vector2f(-2, 20), vf + vf);
        Assert.Equal(new Vector2f(0, 0), vf - vf);
        Assert.Equal(new Vector2f(-2, 20), vf * 2);
        Assert.Equal(new Vector2f(-2, 20), 2 * vf);
        Assert.Equal(new Vector2f(-0.5f, 5), vf / 2);
        Assert.Equal(new Vector2f(1, 10), Abs(vf));
        Assert.Equal(vf, Ceil(new Vector2f(-1.5f, 9.9f)));
        Assert.Equal(vf, Floor(new Vector2f(-.5f, 10.01f)));
        Assert.Equal(new Vector2f(-20, 10), Min(vf, new Vector2f(-20, 20)));
        Assert.Equal(new Vector2f(-1, 20), Max(vf, new Vector2f(-20, 20)));
        Assert.Equal(-1, MinComponentValue(vf));
        Assert.Equal(-10, MinComponentValue(-vf));
        Assert.Equal(10, MaxComponentValue(vf));
        Assert.Equal(1, MaxComponentValue(-vf));
        Assert.Equal(1, MaxComponentIndex(vf));
        Assert.Equal(0, MaxComponentIndex(-vf));
        Assert.Equal(0, MinComponentIndex(vf));
        Assert.Equal(1, MinComponentIndex(-vf));
        Assert.Equal(vf, Permute(vf, [0, 1]));
        Assert.Equal(new Vector2f(10, -1), Permute(vf, [1, 0]));
        Assert.Equal(new Vector2f(10, 10), Permute(vf, [1, 1]));
    }
}

public class Vector3Tests
{
    [Fact]
    public void Basics()
    {
        Vector3f vf = new(-1, 10, 2);
        Assert.Equal(vf, new Vector3f(new Vector3i(-1, 10, 2)));
        Assert.NotEqual(vf, new Vector3f(-1, 100, 2));
        Assert.Equal(new Vector3f(-2, 20, 4), vf + vf);
        Assert.Equal(new Vector3f(0, 0, 0), vf - vf);
        Assert.Equal(new Vector3f(-2, 20, 4), vf * 2);
        Assert.Equal(new Vector3f(-2, 20, 4), 2 * vf);
        Assert.Equal(new Vector3f(-0.5f, 5, 1), vf / 2);
        Assert.Equal(new Vector3f(1, 10, 2), Abs(vf));
        Assert.Equal(vf, Ceil(new Vector3f(-1.5f, 9.9f, 1.01f)));
        Assert.Equal(vf, Floor(new Vector3f(-.5f, 10.01f, 2.99f)));
        Assert.Equal(new Vector3f(-20, 10, 1.5f), Min(vf, new Vector3f(-20, 20, 1.5f)));
        Assert.Equal(new Vector3f(-1, 20, 2), Max(vf, new Vector3f(-20, 20, 0)));
        Assert.Equal(-1, MinComponentValue(vf));
        Assert.Equal(-10, MinComponentValue(-vf));
        Assert.Equal(10, MaxComponentValue(vf));
        Assert.Equal(1, MaxComponentValue(-vf));
        Assert.Equal(1, MaxComponentIndex(vf));
        Assert.Equal(0, MaxComponentIndex(-vf));
        Assert.Equal(0, MinComponentIndex(vf));
        Assert.Equal(1, MinComponentIndex(-vf));
        Assert.Equal(vf, Permute(vf, [0, 1, 2]));
        Assert.Equal(new Vector3f(10, -1, 2), Permute(vf, [1, 0, 2]));
        Assert.Equal(new Vector3f(2, -1, 10), Permute(vf, [2, 0, 1]));
        Assert.Equal(new Vector3f(10, 10, -1), Permute(vf, [1, 1, 0]));
    }

    // From math_test.cpp's SquareMatrix.Basics3 (needs Vector3f).
    [Fact]
    public void SquareMatrixMul()
    {
        SquareMatrix3 m = new(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Vector3f v3 = new(1, -2, 4);
        Vector3f v3t = m * v3;
        Assert.Equal(1 - 4 + 12, v3t[0]);
        Assert.Equal(4 - 10 + 24, v3t[1]);
        Assert.Equal(7 - 16 + 36, v3t[2]);
    }
}

public class Point2Tests
{
    [Fact]
    public void InvertBilinear()
    {
        static Point2f bilerp(Point2f u, Point2f[] v) =>
            Lerp(u[0], Lerp(u[1], v[0], v[2]), Lerp(u[1], v[1], v[3]));

        RNG rng = new();
        Point2f[] vRealSimple = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
        for (int i = 0; i < 100; ++i)
        {
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Point2f up = bilerp(u, vRealSimple);
            Assert.True(Distance(u, up) < 1e-5f); // check bilerp
            Assert.True(Distance(u, Util.InvertBilinear(up, vRealSimple)) < 1e-5f);
        }

        Point2f[] vSimple = [new(10, -4), new(10, -9), new(15, -4), new(15, -9)];
        for (int i = 0; i < 100 + 100 * 100; ++i)
        {
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Point2f up = bilerp(u, vSimple);
            Point2f uinv = Util.InvertBilinear(up, vSimple);
            Assert.True(Distance(u, uinv) < 1e-5f);
        }
    }
}

public class VectorTests
{
    private static double AcosDot(Vector3f a, Vector3f b)
    {
        // Vector3<double> in pbrt's test.
        double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
        double la = Math.Sqrt(ax * ax + ay * ay + az * az);
        double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
        ax /= la; ay /= la; az /= la;
        bx /= lb; by /= lb; bz /= lb;
        return Math.Acos(Math.Clamp(ax * bx + ay * by + az * bz, -1, 1));
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void AngleBetween()
    {
        Assert.Equal(0, Util.AngleBetween(new Vector3f(1, 0, 0), new Vector3f(1, 0, 0)));

        Assert.True(FMath.Abs(Util.AngleBetween(new Vector3f(0, 0, 1), new Vector3f(0, 0, -1))) - Pi < 1e-7);
        Assert.True(FMath.Abs(Util.AngleBetween(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0))) - Pi / 2 < 1e-7);

        Vector3f x = Normalize(new Vector3f(1, -3, 10));
        Assert.Equal(0, Util.AngleBetween(x, x));
        Assert.True(FMath.Abs(Util.AngleBetween(x, -x) - Pi) < 3e-7);

        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)i);
            Vector3f a = Normalize(new Vector3f(-1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>()));
            Vector3f b = Normalize(new Vector3f(-1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>()));

            Float v0 = (Float)AcosDot(a, b), v1 = Util.AngleBetween(a, b);
            Float err = FMath.Abs(v0 - v1) / v0;
            Assert.True(err < 5e-6, $"{v0} vs {v1}, a: {a}, b: {b}");
        }

        for (int i = 0; i < 100000; ++i)
        {
            RNG rng = new((ulong)(i + 10000000));
            Vector3f a = Normalize(new Vector3f(-1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>()));
            Vector3f b = Normalize(new Vector3f(-1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>(),
                                                -1 + 2 * rng.Uniform<Float>()));
            // Make them face in opposite-ish directions
            a.x = FMath.CopySign(a.x, -b.x);
            a.y = FMath.CopySign(a.y, -b.y);
            a.z = FMath.CopySign(a.z, -b.z);

            Float v0 = (Float)AcosDot(a, b), v1 = Util.AngleBetween(a, b);
            Float err = FMath.Abs(v0 - v1) / v0;
            Assert.True(err < 5e-6, $"{v0} vs {v1}, a: {a}, b: {b}");
        }

        {
            Vector3f a = Normalize(new Vector3f(1, 1, 1));
            Vector3f b = Normalize(new Vector3f(-1, -1.0001f, -1));
            // pbrt computes the reference in long double, which is plain
            // double with MSVC.
            double ax = 1, ay = 1, az = 1, bx = -1, by = -1.0001, bz = -1;
            double la = Math.Sqrt(3), lb = Math.Sqrt(bx * bx + by * by + bz * bz);
            Float precise = (Float)Math.Acos(Math.Clamp((ax * bx + ay * by + az * bz) / (la * lb), -1, 1));
            Float abet = Util.AngleBetween(a, b);
            Assert.Equal(precise, abet);
        }
    }

    [Fact]
    public void CoordinateSystem()
    {
        // Duff et al 2017 footnote 1
        static Float error(Vector3f a, Vector3f b, Vector3f c) =>
            (Sqr(Length(a) - 1) + Sqr(Length(b) - 1) + Sqr(Length(c) - 1) +
             Sqr(Dot(a, b)) + Sqr(Dot(b, c)) + Sqr(Dot(c, a))) / 6;

        // Coordinate axes.
        foreach (Vector3f v in new Vector3f[] { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
                                                new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) })
        {
            Util.CoordinateSystem(v, out Vector3f a, out Vector3f b);
            for (int c = 0; c < 3; ++c)
            {
                if (v[c] != 0)
                {
                    Assert.Equal(0, a[c]);
                    Assert.Equal(0, b[c]);
                }
            }
        }

        // Bad vectors from Duff et al
        foreach (Vector3f v in new Vector3f[] { new(0.00038527316f, 0.00038460016f, -0.99999988079f),
                                                new(-0.00019813581f, -0.00008946839f, -0.99999988079f) })
        {
            Util.CoordinateSystem(v, out Vector3f a, out Vector3f b);
            Assert.True(error(v, a, b) < 1e-10);
        }

        // Random vectors
        RNG rng = new();
        for (int i = 0; i < 1000; ++i)
        {
            Vector3f v = TestHelpers.RandomSphere(ref rng);
            Util.CoordinateSystem(v, out Vector3f a, out Vector3f b);
            Assert.True(error(v, a, b) < 1e-10);
        }
    }
}

public class BoundsTests
{
    [Fact]
    public void Bounds2IteratorBasic()
    {
        Bounds2i b = new(new Point2i(0, 1), new Point2i(2, 3));
        Point2i[] e = [new(0, 1), new(1, 1), new(0, 2), new(1, 2)];
        int offset = 0;
        foreach (Point2i p in b)
        {
            Assert.True(offset < e.Length);
            Assert.Equal(e[offset], p);
            ++offset;
        }
        Assert.Equal(e.Length, offset);
    }

    [Fact]
    public void Bounds2IteratorDegenerate()
    {
        Bounds2i b = new(new Point2i(0, 0), new Point2i(0, 10));
        foreach (Point2i p in b)
            Assert.Fail($"p = {p}");

        Bounds2i b2 = new(new Point2i(0, 0), new Point2i(4, 0));
        foreach (Point2i p in b2)
            Assert.Fail($"p = {p}");

        Bounds2i b3 = new();
        foreach (Point2i p in b3)
            Assert.Fail($"p = {p}");
    }

    [Fact]
    public void Bounds3PointDistance()
    {
        {
            Bounds3f b = new(new Point3f(0, 0, 0), new Point3f(1, 1, 1));

            // Points inside the bounding box or on faces
            Assert.Equal(0f, Distance(new Point3f(.5f, .5f, .5f), b));
            Assert.Equal(0f, Distance(new Point3f(0, 1, 1), b));
            Assert.Equal(0f, Distance(new Point3f(.25f, .8f, 1), b));
            Assert.Equal(0f, Distance(new Point3f(0, .25f, .8f), b));
            Assert.Equal(0f, Distance(new Point3f(.7f, 0, .8f), b));

            // Aligned with the plane of one of the faces
            Assert.Equal(5f, Distance(new Point3f(6, 1, 1), b));
            Assert.Equal(10f, Distance(new Point3f(0, -10, 1), b));

            // 2 of the dimensions inside the box's extent
            Assert.Equal(2f, Distance(new Point3f(0.5f, 0.5f, 3), b));
            Assert.Equal(3f, Distance(new Point3f(0.5f, 0.5f, -3), b));
            Assert.Equal(2f, Distance(new Point3f(0.5f, 3, 0.5f), b));
            Assert.Equal(3f, Distance(new Point3f(0.5f, -3, 0.5f), b));
            Assert.Equal(2f, Distance(new Point3f(3, 0.5f, 0.5f), b));
            Assert.Equal(3f, Distance(new Point3f(-3, 0.5f, 0.5f), b));

            // General points
            Assert.Equal(3 * 3 + 7 * 7 + 10 * 10, DistanceSquared(new Point3f(4, 8, -10), b));
            Assert.Equal(6 * 6 + 10 * 10 + 7 * 7, DistanceSquared(new Point3f(-6, -10, 8), b));
        }

        {
            // A few with a more irregular box, just to be sure
            Bounds3f b = new(new Point3f(-1, -3, 5), new Point3f(2, -2, 18));
            Assert.Equal(0f, Distance(new Point3f(-.99f, -2, 5), b));
            Assert.Equal(2 * 2 + 6 * 6 + 4 * 4, DistanceSquared(new Point3f(-3, -9, 22), b));
        }
    }

    [Fact]
    public void Bounds2Union()
    {
        Bounds2f a = new(new Point2f(-10, -10), new Point2f(0, 20));
        Bounds2f b = new(); // degenerate
        Bounds2f c = Union(a, b);
        Assert.Equal(a, c);

        Assert.Equal(b, Union(b, b));

        Bounds2f d = new(new Point2f(-15, 10));
        Bounds2f e = Union(a, d);
        Assert.Equal(new Bounds2f(new Point2f(-15, -10), new Point2f(0, 20)), e);
    }

    [Fact]
    public void Bounds3Union()
    {
        Bounds3f a = new(new Point3f(-10, -10, 5), new Point3f(0, 20, 10));
        Bounds3f b = new(); // degenerate
        Bounds3f c = Union(a, b);
        Assert.Equal(a, c);

        Assert.Equal(b, Union(b, b));

        Bounds3f d = new(new Point3f(-15, 10, 30));
        Bounds3f e = Union(a, d);
        Assert.Equal(new Bounds3f(new Point3f(-15, -10, 5), new Point3f(0, 20, 30)), e);
    }

    [Fact]
    public void BoundsConversions()
    {
        // Empty bounds must stay empty rather than overflowing.
        Assert.True(new Bounds2i(new Bounds2f()).IsEmpty());
        Assert.True(new Bounds3f(new Bounds3i()).IsEmpty());
        Assert.Equal(new Bounds2i(new Point2i(1, 2), new Point2i(3, 4)),
                     new Bounds2i(new Bounds2f(new Point2f(1.5f, 2.5f), new Point2f(3.5f, 4.9f))));
    }
}

public class EqualAreaTests
{
    [Fact]
    public void Randoms()
    {
        foreach (Point2f u in Uniform2D(100))
        {
            Vector3f v = SampleUniformSphere(u);
            Point2f c = EqualAreaSphereToSquare(v);
            Vector3f vp = EqualAreaSquareToSphere(c);
            Assert.True(Length(vp) > 0.9999 && Length(vp) < 1.0001, $"{Length(vp)}");
            Assert.True(Dot(v, vp) > 0.9999, $"{v}");
        }
    }

    [Fact]
    public void RemapEdges()
    {
        static void checkClose(Point2f a, Point2f b)
        {
            Vector3f av = EqualAreaSquareToSphere(a);
            b = WrapEqualAreaSquare(b);
            Vector3f bv = EqualAreaSquareToSphere(b);
            Assert.True(Dot(av, bv) > .99);
        }

        checkClose(new(.25f, .01f), new(.25f, -.01f));
        checkClose(new(.89f, .01f), new(.89f, -.01f));

        checkClose(new(.25f, .99f), new(.25f, 1.01f));
        checkClose(new(.89f, .99f), new(.89f, 1.01f));

        checkClose(new(.01f, .66f), new(-.01f, .66f));
        checkClose(new(.01f, .15f), new(-.01f, .15f));

        checkClose(new(.99f, .66f), new(1.01f, .66f));
        checkClose(new(.99f, .15f), new(1.01f, .15f));

        checkClose(new(.01f, .01f), new(-.01f, -.01f));
        checkClose(new(.99f, .01f), new(1.01f, -.01f));
        checkClose(new(.01f, .99f), new(-.01f, 1.01f));
        checkClose(new(.99f, .99f), new(1.01f, 1.01f));
    }
}

public class DirectionConeTests
{
    private static DirectionCone RandomCone(ref RNG rng)
    {
        Vector3f w = TestHelpers.RandomSphere(ref rng);
        return new DirectionCone(w, -1 + 2 * rng.Uniform<Float>());
    }

    [Fact]
    public void Defaults()
    {
        Assert.True(new DirectionCone().IsEmpty());
        Assert.False(Inside(new DirectionCone(), new Vector3f(0, 0, 1)));
        Assert.True(Inside(DirectionCone.EntireSphere(), new Vector3f(0, 0, -1)));
    }

    [Fact]
    public void BoundBounds()
    {
        Bounds3f b = new(new Point3f(0, 0, 0), new Point3f(1, 1, 1));

        // Point inside the bbox
        DirectionCone c = BoundSubtendedDirections(b, new Point3f(.1f, .2f, .3f));
        Assert.Equal(FMath.Cos(Pi), c.cosTheta);

        // Outside, .5 units away in the middle (so using the direction to the
        // center gives the best bound).
        //
        // tan theta = (sqrt(.5^2 + .5^2)) / .5
        c = BoundSubtendedDirections(b, new Point3f(-.5f, .5f, .5f));
        Float theta = FMath.Acos(c.cosTheta);
        Float precise = (Float)Math.Atan(Math.Sqrt(.5 * .5 + .5 * .5) / .5);
        // Make sure the returned bound isn't too small.
        Assert.True(theta >= 1.0001 * precise);
        // It's fine for it to be a bit big (as it is in practice due to
        // approximations for performance), but it shouldn't be too big.
        Assert.True(theta < 1.1 * precise);

        RNG rng = new(512);
        for (int i = 0; i < 1000; ++i)
        {
            Bounds3f bb = new(
                new Point3f(Lerp(rng.Uniform<Float>(), -1, 1), Lerp(rng.Uniform<Float>(), -1, 1),
                            Lerp(rng.Uniform<Float>(), -1, 1)),
                new Point3f(Lerp(rng.Uniform<Float>(), -1, 1), Lerp(rng.Uniform<Float>(), -1, 1),
                            Lerp(rng.Uniform<Float>(), -1, 1)));

            Point3f p = new(Lerp(rng.Uniform<Float>(), -4, 4), Lerp(rng.Uniform<Float>(), -4, 4),
                            Lerp(rng.Uniform<Float>(), -4, 4));

            c = BoundSubtendedDirections(bb, p);
            if (Inside(p, bb))
                Assert.Equal(FMath.Cos(Pi), c.cosTheta);
            else
            {
                for (int j = 0; j < 1000; ++j)
                {
                    Vector3f w = TestHelpers.RandomSphere(ref rng);
                    bool hit = bb.IntersectP(p, w);
                    if (hit)
                        Assert.True(Inside(c, w));
                    if (!Inside(c, w))
                        Assert.False(hit);
                }
            }
        }
    }

    [Fact]
    public void VectorInCone()
    {
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            DirectionCone dc = RandomCone(ref rng);

            for (int j = 0; j < 100; ++j)
            {
                Vector3f wRandom = TestHelpers.RandomSphere(ref rng);
                Vector3f wClosest = dc.ClosestVectorInCone(wRandom);

                if (Inside(dc, wRandom))
                    Assert.True(Dot(wClosest, wRandom) > .999);
                else
                {
                    // Uniformly sample the circle at the cone's boundary and
                    // keep the vector that's closest to wRandom.
                    Float sinTheta = SafeSqrt(1 - dc.cosTheta * dc.cosTheta);
                    Frame f = Frame.FromZ(dc.w);

                    Vector3f wBest = default;
                    Float bestDot = -1;
                    const int nk = 1000;
                    for (int k = 0; k < nk; ++k)
                    {
                        Float phi = (k + .5f) / nk * 2 * Pi;
                        Vector3f w = SphericalDirection(sinTheta, dc.cosTheta, phi);
                        w = f.FromLocal(w);
                        if (Dot(w, wRandom) > bestDot)
                        {
                            wBest = w;
                            bestDot = Dot(w, wRandom);
                        }
                    }
                    Assert.True(Dot(wBest, wClosest) > .999,
                                $"{wBest} vs {wClosest}, dot {Dot(wBest, wClosest)}");
                }
            }
        }
    }
}

public class SphericalTriangleAreaTests
{
    [Fact]
    public void Basics()
    {
        {
            Float a = SphericalTriangleArea(new Vector3f(1, 0, 0), new Vector3f(0, 1, 0),
                                            new Vector3f(0, 0, 1));
            Assert.True(a >= .9999 * Pi / 2 && a <= 1.00001 * Pi / 2, $"{a}");
        }

        {
            Float a = SphericalTriangleArea(new Vector3f(1, 0, 0), Normalize(new Vector3f(1, 1, 0)),
                                            new Vector3f(0, 0, 1));
            Assert.True(a >= .9999 * Pi / 4 && a <= 1.00001 * Pi / 4, $"{a}");
        }
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void RandomSampling()
    {
        for (int i = 0; i < 100; ++i)
        {
            RNG rng = new((ulong)i);
            Vector3f a = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Vector3f b = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Vector3f c = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));

            Vector3f axis = Normalize(a + b + c);
            Frame frame = Frame.FromZ(axis);
            Float cosTheta = FMath.Min(Dot(a, axis), FMath.Min(Dot(b, axis), Dot(c, axis)));

            Float area = SphericalTriangleArea(a, b, c);
            bool sampleSphere = area > Pi;
            int sqrtN = 200;
            int count = 0;
            foreach (Point2f u in Hammersley2D(sqrtN * sqrtN))
            {
                Vector3f v;
                if (sampleSphere)
                    v = SampleUniformSphere(u);
                else
                {
                    v = SampleUniformCone(u, cosTheta);
                    v = frame.FromLocal(v);
                }

                if (IntersectTriangle(new Ray(new Point3f(0, 0, 0), v), Infinity, new Point3f(a), new Point3f(b),
                                      new Point3f(c)).HasValue)
                    ++count;
            }

            Float pdf = sampleSphere ? UniformSpherePDF() : UniformConePDF(cosTheta);
            Float estA = count / (sqrtN * sqrtN * pdf);

            Float error = FMath.Abs((estA - area) / area);
            Assert.True(error < 0.035f, $"Area {area}, estimate {estA}, va = {a}, vb = {b}, vc = {c}");
        }
    }
}

public class PointVectorTests
{
    [Fact]
    public void Interval()
    {
        // This is really just to make sure that various expected things
        // compile in the first place when using the interval variants of
        // these...
        Point3fi p = new(1, 2, 3), q = new(6, 9, 1);
        Vector3fi v = new(4, 5, 6);

        p += v;
        p = p - v;
        Vector3fi scaled = new Pbrt.Interval(4) * v;
        p = p + scaled;
        Pbrt.Interval d = Dot(v, v);
        Assert.Equal(16 + 25 + 36, (Float)d);
        d = DistanceSquared(p, q);
        d = Distance(p, q);

        Vector3fi vv = Cross(v, v);
        Assert.Equal(new Vector3f(0, 0, 0), new Vector3f(vv));

        // Mixed Point3fi / Float-vector arithmetic via implicit conversions.
        // p = (1,2,3) + v - v + 4v = (17, 22, 27), with conservative bounds.
        Point3fi p2 = p + new Vector3f(1, 1, 1);
        Assert.True(InRange(18, p2.x) && InRange(23, p2.y) && InRange(28, p2.z), $"{p2}");
        Assert.False(p2.IsExact());
    }
}

public class OctahedralVectorTests
{
    [Fact]
    public void EncodeDecode()
    {
        foreach (Point2f u in Hammersley2D(65535))
        {
            Vector3f v = SampleUniformSphere(u);

            OctahedralVector ov = new(v);
            Vector3f v2 = (Vector3f)ov;

            Assert.True(Length(v2) > .999f);
            Assert.True(Length(v2) < 1.001f);
            Assert.True(FMath.Abs(1 - Dot(v2, v)) < .001f);
        }
    }
}

public class FrameTests
{
    [Fact]
    public void RoundTrip()
    {
        Assert.Equal(new Vector3f(1, 2, 3), new Frame().ToLocal(new Vector3f(1, 2, 3)));

        RNG rng = new(7);
        for (int i = 0; i < 1000; ++i)
        {
            Frame f = Frame.FromZ(TestHelpers.RandomSphere(ref rng));
            Vector3f v = TestHelpers.RandomSphere(ref rng);
            Vector3f vl = f.ToLocal(v);
            Assert.True(FMath.Abs(vl.z - Dot(v, f.z)) < 1e-6f);
            Assert.True(Length(f.FromLocal(vl) - v) < 1e-5f);
        }
    }
}

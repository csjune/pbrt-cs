// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/transform_test.cpp, plus additional checks for
// the C# port (marked as such).

namespace Pbrt.Tests;

public class TransformTests
{
    private static Transform RandomTransform(ref RNG rng)
    {
        Transform t = new();
        for (int i = 0; i < 10; ++i)
        {
            switch (rng.Uniform(3u))
            {
                case 0:
                {
                    Float sx = FMath.Abs(R(ref rng)), sy = FMath.Abs(R(ref rng)), sz = FMath.Abs(R(ref rng));
                    t = t * Scale(sx, sy, sz);
                    break;
                }
                case 1:
                {
                    Float x = R(ref rng), y = R(ref rng), z = R(ref rng);
                    t = t * Translate(new Vector3f(x, y, z));
                    break;
                }
                case 2:
                {
                    Float theta = R(ref rng) * 20;
                    t = t * Rotate(theta, TestHelpers.RandomSphere(ref rng));
                    break;
                }
            }
        }
        return t;
    }

    private static Float R(ref RNG rng) => (Float)(-10.0 + 20.0 * rng.Uniform<Float>());

    [Fact]
    public void AnimatedTransformRandoms()
    {
        RNG rng = new();

        for (int i = 0; i < 200; ++i)
        {
            // Generate a pair of random transformation matrices.
            Transform t0 = RandomTransform(ref rng);
            Transform t1 = RandomTransform(ref rng);
            AnimatedTransform at = new(t0, 0, t1, 1);

            for (int j = 0; j < 5; ++j)
            {
                // Generate a random bounding box and find the bounds of its motion.
                Point3f p0 = new(R(ref rng), R(ref rng), R(ref rng));
                Point3f p1 = new(R(ref rng), R(ref rng), R(ref rng));
                Bounds3f bounds = new(p0, p1);
                Bounds3f motionBounds = at.MotionBounds(bounds);

                for (Float t = 0; t <= 1; t += 1e-2f * rng.Uniform<Float>())
                {
                    // Now, interpolate the transformations at a bunch of times
                    // along the time range and then transform the bounding box
                    // with the result.
                    Transform tr = at.Interpolate(t);
                    Bounds3f tb = tr.Apply(bounds);

                    // Add a little slop to allow for floating-point round-off
                    // error in computing the motion extrema times.
                    tb.pMin += (Float)1e-4 * tb.Diagonal();
                    tb.pMax -= (Float)1e-4 * tb.Diagonal();

                    // Now, the transformed bounds should be inside the motion
                    // bounds.
                    Assert.True(tb.pMin.x >= motionBounds.pMin.x);
                    Assert.True(tb.pMax.x <= motionBounds.pMax.x);
                    Assert.True(tb.pMin.y >= motionBounds.pMin.y);
                    Assert.True(tb.pMax.y <= motionBounds.pMax.y);
                    Assert.True(tb.pMin.z >= motionBounds.pMin.z);
                    Assert.True(tb.pMax.z <= motionBounds.pMax.z);
                }
            }
        }
    }

    [Fact]
    public void RotateFromToSimple()
    {
        {
            // Same directions...
            Vector3f from = new(0, 0, 1), to = new(0, 0, 1);
            Transform r = RotateFromTo(from, to);
            Vector3f toNew = r.Apply(from);
            Assert.Equal(to, toNew);
        }

        {
            Vector3f from = new(0, 0, 1), to = new(1, 0, 0);
            Transform r = RotateFromTo(from, to);
            Vector3f toNew = r.Apply(from);
            Assert.Equal(to, toNew);
        }

        {
            Vector3f from = new(0, 0, 1), to = new(0, 1, 0);
            Transform r = RotateFromTo(from, to);
            Vector3f toNew = r.Apply(from);
            Assert.Equal(to, toNew);
        }
    }

    [Fact]
    public void RotateFromToRandoms()
    {
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Vector3f from = TestHelpers.RandomSphere(ref rng);
            Vector3f to = TestHelpers.RandomSphere(ref rng);

            Transform r = RotateFromTo(from, to);
            Vector3f toNew = r.Apply(from);
            Assert.True(FMath.Abs(Length(toNew) - 1) < 1e-3f);
            Assert.True(Dot(to, toNew) > .999f);
        }
    }

    // C# port: basic sanity checks.
    [Fact]
    public void Basics()
    {
        Transform id = new();
        Assert.True(id.IsIdentity());
        Assert.False(id.HasScale());
        Assert.True(Scale(2, 1, 1).HasScale());
        Assert.True(Scale(-1, 1, 1).SwapsHandedness());
        Assert.False(RotateY(30).SwapsHandedness());

        Transform t = Translate(new Vector3f(1, 2, 3));
        Assert.Equal(new Point3f(2, 3, 4), t.Apply(new Point3f(1, 1, 1)));
        Assert.Equal(new Vector3f(1, 1, 1), t.Apply(new Vector3f(1, 1, 1)));
        Assert.Equal(new Point3f(1, 1, 1), t.ApplyInverse(new Point3f(2, 3, 4)));
        Assert.Equal(new Point3f(1, 1, 1), Inverse(t).Apply(new Point3f(2, 3, 4)));

        // Normals transform by the inverse transpose.
        Transform s = Scale(2, 1, 1);
        Normal3f n = s.Apply(new Normal3f(1, 1, 0));
        Assert.Equal(new Normal3f(.5f, 1, 0), n);

        Assert.True(Translate(new Vector3f(1, 2, 3)) == Translate(new Vector3f(1, 2, 3)));
        Assert.Equal(Translate(new Vector3f(1, 2, 3)).GetHashCode(),
                     Translate(new Vector3f(1, 2, 3)).GetHashCode());
        Assert.True(Translate(new Vector3f(1, 2, 3)) != Translate(new Vector3f(1, 2, 4)));
    }

    // C# port: RotateX/Y/Z agree with the general Rotate().
    [Fact]
    public void AxisRotations()
    {
        Vector3f v = new(.3f, -.7f, 2);
        for (Float theta = -180; theta <= 180; theta += 7.5f)
        {
            Vector3f a = RotateX(theta).Apply(v), b = Rotate(theta, new Vector3f(1, 0, 0)).Apply(v);
            Assert.True(Length(a - b) < 1e-5f);
            a = RotateY(theta).Apply(v); b = Rotate(theta, new Vector3f(0, 1, 0)).Apply(v);
            Assert.True(Length(a - b) < 1e-5f);
            a = RotateZ(theta).Apply(v); b = Rotate(theta, new Vector3f(0, 0, 1)).Apply(v);
            Assert.True(Length(a - b) < 1e-5f);
        }
    }

    // C# port: Point3fi transformation produces conservative bounds.
    [Fact]
    public void PointErrorBounds()
    {
        for (int i = 0; i < 1000; ++i)
        {
            RNG rng = new((ulong)i);
            Transform t = RandomTransform(ref rng);
            Point3f p = new(R(ref rng), R(ref rng), R(ref rng));

            // Reference result in double precision.
            ref readonly SquareMatrix4 m = ref t.GetMatrix();
            double[] r = new double[4];
            for (int row = 0; row < 4; ++row)
                r[row] = (double)m[row, 0] * p.x + (double)m[row, 1] * p.y +
                         (double)m[row, 2] * p.z + m[row, 3];

            Point3fi pi = t.Apply(new Point3fi(p));
            Assert.True(InRange((Float)(r[0] / r[3]), pi.x), $"{r[0] / r[3]} {pi.x}");
            Assert.True(InRange((Float)(r[1] / r[3]), pi.y), $"{r[1] / r[3]} {pi.y}");
            Assert.True(InRange((Float)(r[2] / r[3]), pi.z), $"{r[2] / r[3]} {pi.z}");

            // ApplyInverse: bounds contain the exact result of applying mInv.
            // (A full round trip is not guaranteed to contain p: the bounds
            // assume mInv is the exact inverse of m, which it is not.)
            ref readonly SquareMatrix4 mi = ref t.GetInverseMatrix();
            double[] ri = new double[4];
            for (int row = 0; row < 4; ++row)
                ri[row] = (double)mi[row, 0] * p.x + (double)mi[row, 1] * p.y +
                          (double)mi[row, 2] * p.z + mi[row, 3];
            Point3fi pinv = t.ApplyInverse(new Point3fi(p));
            Assert.True(InRange((Float)(ri[0] / ri[3]), pinv.x), $"i={i} {ri[0] / ri[3]} {pinv.x}");
            Assert.True(InRange((Float)(ri[1] / ri[3]), pinv.y), $"i={i} {ri[1] / ri[3]} {pinv.y}");
            Assert.True(InRange((Float)(ri[2] / ri[3]), pinv.z), $"i={i} {ri[2] / ri[3]} {pinv.z}");
        }
    }

    // C# port: transformed rays keep tMax consistent with the offset origin.
    [Fact]
    public void RayTransform()
    {
        Transform t = Translate(new Vector3f(1, 2, 3)) * RotateY(30) * Scale(2, 2, 2);
        Ray r = new(new Point3f(.5f, -.25f, 1), new Vector3f(0, 0, 1), .5f);
        Float tMax = 10;
        Ray rt = t.Apply(r, ref tMax);
        Assert.Equal(.5f, rt.time);
        Assert.True(tMax <= 10);
        Assert.True(Distance(rt.o, t.Apply(r.o)) < 1e-4f);

        RayDifferential rd = new(r.o, r.d) { hasDifferentials = true, rxOrigin = r.o, rxDirection = r.d };
        RayDifferential rdt = t.Apply(rd);
        Assert.True(rdt.hasDifferentials);
        Assert.Equal(t.Apply(r.d), rdt.rxDirection);

        Ray back = t.ApplyInverse(rt);
        Assert.True(Distance(back.o, r.o) < 1e-4f);
        Assert.True(Length(back.d - r.d) < 1e-5f);
    }

    // C# port: LookAt / Perspective / Orthographic basics.
    [Fact]
    public void CameraTransforms()
    {
        Transform cameraFromWorld = LookAt(new Point3f(0, 0, -5), new Point3f(0, 0, 0), new Vector3f(0, 1, 0));
        Assert.Equal(new Point3f(0, 0, 5), cameraFromWorld.Apply(new Point3f(0, 0, 0)));
        Assert.Throws<FatalErrorException>(() =>
            LookAt(new Point3f(0, 0, 0), new Point3f(0, 1, 0), new Vector3f(0, 1, 0)));

        Transform persp = Perspective(90, 1, 100);
        Point3f pNear = persp.Apply(new Point3f(1, 1, 1));
        Assert.True(FMath.Abs(pNear.z) < 1e-6f);
        Assert.True(FMath.Abs(pNear.x - 1) < 1e-6f && FMath.Abs(pNear.y - 1) < 1e-6f);
        Assert.True(FMath.Abs(persp.Apply(new Point3f(0, 0, 100)).z - 1) < 1e-5f);

        Transform ortho = Orthographic(1, 3);
        Assert.Equal(0f, ortho.Apply(new Point3f(0, 0, 1)).z);
        Assert.Equal(1f, ortho.Apply(new Point3f(0, 0, 3)).z);
    }

    // C# port: Transform <-> Quaternion round trip (pbrt's disabled
    // Quaternion.RoundTripBug test covers a known-problematic matrix).
    [Fact]
    public void QuaternionRoundTrip()
    {
        RNG rng = new(11);
        for (int i = 0; i < 1000; ++i)
        {
            Transform rot = Rotate(360 * rng.Uniform<Float>(), TestHelpers.RandomSphere(ref rng));
            Quaternion q = (Quaternion)rot;
            Assert.True(FMath.Abs(Length(q) - 1) < 1e-4f);
            Transform qt = new(q);
            for (int r = 0; r < 4; ++r)
                for (int c = 0; c < 4; ++c)
                    Assert.True(FMath.Abs(rot.GetMatrix()[r, c] - qt.GetMatrix()[r, c]) < 1e-3f);
        }
    }

    // C# port: Decompose() recovers T * R * S.
    [Fact]
    public void Decompose()
    {
        Transform t = Translate(new Vector3f(1, -2, 3)) * Rotate(40, new Vector3f(1, 1, 0)) * Scale(2, 3, .5f);
        t.Decompose(out Vector3f T, out SquareMatrix4 R, out SquareMatrix4 S);
        Assert.Equal(new Vector3f(1, -2, 3), T);
        Transform recomposed = Translate(T) * new Transform(R) * new Transform(S);
        for (int r = 0; r < 4; ++r)
            for (int c = 0; c < 4; ++c)
                Assert.True(FMath.Abs(t.GetMatrix()[r, c] - recomposed.GetMatrix()[r, c]) < 1e-4f);
        Assert.True(FMath.Abs(S[0, 0] - 2) < 1e-4f);
        Assert.True(FMath.Abs(S[1, 1] - 3) < 1e-4f);
        Assert.True(FMath.Abs(S[2, 2] - .5f) < 1e-4f);
    }

    // C# port: AnimatedTransform endpoints and interpolation.
    [Fact]
    public void AnimatedTransformBasics()
    {
        AnimatedTransform still = new(Translate(new Vector3f(1, 0, 0)));
        Assert.False(still.IsAnimated());
        Assert.Equal(new Point3f(1, 0, 0), still.Apply(new Point3f(0, 0, 0), .5f));

        Transform t0 = Translate(new Vector3f(0, 0, 0)), t1 = Translate(new Vector3f(10, 0, 0));
        AnimatedTransform at = new(t0, 0, t1, 1);
        Assert.True(at.IsAnimated());
        Assert.Equal(new Point3f(0, 0, 0), at.Apply(new Point3f(0, 0, 0), 0));
        Assert.Equal(new Point3f(10, 0, 0), at.Apply(new Point3f(0, 0, 0), 1));
        Point3f mid = at.Apply(new Point3f(0, 0, 0), .5f);
        Assert.True(Distance(mid, new Point3f(5, 0, 0)) < 1e-5f);

        Ray r = new(new Point3f(0, 0, 0), new Vector3f(0, 0, 1), .25f);
        Assert.True(Distance(at.Apply(r).o, new Point3f(2.5f, 0, 0)) < 1e-4f);
    }
}

public class DirectionConeUnionTests
{
    private static DirectionCone RandomCone(ref RNG rng)
    {
        Vector3f w = TestHelpers.RandomSphere(ref rng);
        return new DirectionCone(w, -1 + 2 * rng.Uniform<Float>());
    }

    [Fact]
    public void UnionBasics()
    {
        // First encloses second
        DirectionCone c = Union(new DirectionCone(new Vector3f(0, 0, 1), FMath.Cos(Pi / 2)),
                                new DirectionCone(new Vector3f(.1f, .1f, 1), MathF.Cos(.1f)));
        Assert.Equal(new Vector3f(0, 0, 1), c.w);
        Assert.Equal(FMath.Cos(Pi / 2), c.cosTheta);

        // Second encloses first
        c = Union(new DirectionCone(new Vector3f(.1f, .1f, 1), MathF.Cos(.1f)),
                  new DirectionCone(new Vector3f(0, 0, 1), FMath.Cos(Pi / 2)));
        Assert.Equal(new Vector3f(0, 0, 1), c.w);
        Assert.Equal(FMath.Cos(Pi / 2), c.cosTheta);

        // Same direction, first wider
        Vector3f w = new(1, .5f, -.25f);
        c = Union(new DirectionCone(w, MathF.Cos(.12f)), new DirectionCone(w, MathF.Cos(.03f)));
        Assert.Equal(Normalize(w), c.w);
        Assert.Equal(MathF.Cos(.12f), c.cosTheta, 5);

        // Same direction, second wider
        c = Union(new DirectionCone(w, MathF.Cos(.1f)), new DirectionCone(w, MathF.Cos(.2f)));
        Assert.Equal(Normalize(w), c.w);
        Assert.Equal(MathF.Cos(.2f), c.cosTheta, 5);

        // Exactly pointing in opposite directions and covering the sphere when
        // it's all said and done.
        c = Union(new DirectionCone(new Vector3f(-1, -1, -1), FMath.Cos(Pi / 2)),
                  new DirectionCone(new Vector3f(1, 1, 1), FMath.Cos(Pi / 2)));
        Assert.Equal(FMath.Cos(Pi), c.cosTheta);

        // Basically opposite and a bit more than pi/2: should also be the
        // whole sphere.
        c = Union(new DirectionCone(new Vector3f(-1, -1, -1), FMath.Cos(1.01f * Pi / 2)),
                  new DirectionCone(new Vector3f(1.001f, 1, 1), FMath.Cos(1.01f * Pi / 2)));
        Assert.Equal(FMath.Cos(Pi), c.cosTheta);

        // Narrow and at right angles; angle should be their midpoint
        c = Union(new DirectionCone(new Vector3f(1, 0, 0), MathF.Cos(1e-3f)),
                  new DirectionCone(new Vector3f(0, 1, 0), MathF.Cos(1e-3f)));
        Assert.Equal(1f, Dot(c.w, Normalize(new Vector3f(1, 1, 0))), 5);
        Assert.True(FMath.Abs(FMath.Cos((Pi / 2 + 2e-3f) / 2) - c.cosTheta) < 1e-3);
    }

    [Fact]
    public void UnionRandoms()
    {
        RNG rng = new(16);

        for (int i = 0; i < 100; ++i)
        {
            DirectionCone a = RandomCone(ref rng), b = RandomCone(ref rng);
            DirectionCone c = Union(a, b);

            for (int j = 0; j < 100; ++j)
            {
                Vector3f w = TestHelpers.RandomSphere(ref rng);
                if (Inside(a, w) || Inside(b, w))
                    Assert.True(Inside(c, w), $"a: {a}, b: {b}, union: {c}, w: {w}");
            }
        }
    }
}

public class SphericalTriangleAreaRotationTests
{
    // The random-rotation part of vecmath_test.cpp's SphericalTriangleArea.Basics.
    [Fact]
    public void RandomRotations()
    {
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Vector3f axis = TestHelpers.RandomSphere(ref rng);
            Float theta = 2 * Pi * rng.Uniform<Float>();
            Transform t = Rotate(theta, axis);
            Vector3f va = t.Apply(new Vector3f(1, 0, 0));
            Vector3f vb = t.Apply(new Vector3f(0, 1, 0));
            Vector3f vc = t.Apply(new Vector3f(0, 0, 1));
            Float a = SphericalTriangleArea(va, vb, vc);
            Assert.True(a >= .9999 * Pi / 2 && a <= 1.0001 * Pi / 2, $"{a}");
        }

        for (int i = 0; i < 100; ++i)
        {
            Vector3f axis = TestHelpers.RandomSphere(ref rng);
            Float theta = 2 * Pi * rng.Uniform<Float>();
            Transform t = Rotate(theta, axis);
            Vector3f va = t.Apply(new Vector3f(1, 0, 0));
            Vector3f vb = t.Apply(Normalize(new Vector3f(1, 1, 0)));
            Vector3f vc = t.Apply(new Vector3f(0, 0, 1));
            Float a = SphericalTriangleArea(va, vb, vc);
            Assert.True(a >= .9999 * Pi / 4 && a <= 1.0001 * Pi / 4, $"{a}");
        }
    }
}

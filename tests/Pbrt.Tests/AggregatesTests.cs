// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// pbrt has no tests for the primitives and aggregates, so these check the
// BVH and kd-tree against brute-force intersection of every primitive, and
// the standard library algorithms against their specified postconditions.

namespace Pbrt.Tests;

public class AggregatesTests
{
    private static List<Primitive> RandomScene(RNG rng, int nTris, int nSpheres, bool quantize)
    {
        Float Coord() => quantize ? (int)(rng.Uniform<Float>() * 17 - 8) * 0.5f : Lerp(rng.Uniform<Float>(), -10, 10);

        List<Point3f> p = [];
        List<int> indices = [];
        for (int i = 0; i < nTris; ++i)
        {
            Point3f c = new(Coord(), Coord(), Coord());
            for (int v = 0; v < 3; ++v)
            {
                Vector3f offset = quantize
                    ? new Vector3f((int)(rng.Uniform<Float>() * 3) * 0.25f, (int)(rng.Uniform<Float>() * 3) * 0.25f,
                                   (int)(rng.Uniform<Float>() * 3) * 0.25f)
                    : new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f) * 2;
                indices.Add(p.Count);
                p.Add(c + offset);
            }
        }

        List<Primitive> prims = [];
        if (nTris > 0)
        {
            TriangleMesh mesh = new(new Transform(), false, indices, p, null, null, null, null);
            foreach (Shape s in Triangle.CreateTriangles(mesh))
                prims.Add(new SimplePrimitive(s, null));
        }
        for (int i = 0; i < nSpheres; ++i)
        {
            Transform renderFromObject = Translate(new Vector3f(Coord(), Coord(), Coord()));
            Float radius = 0.1f + rng.Uniform<Float>() * 2;
            Sphere sphere = new(renderFromObject, Inverse(renderFromObject), false, radius, -radius, radius, 360);
            prims.Add(new GeometricPrimitive(sphere, null, null, new MediumInterface(), null));
        }
        return prims;
    }

    private static Ray RandomRay(ref RNG rng)
    {
        Point3f o = new(Lerp(rng.Uniform<Float>(), -15, 15), Lerp(rng.Uniform<Float>(), -15, 15),
                        Lerp(rng.Uniform<Float>(), -15, 15));
        Point3f t = new(Lerp(rng.Uniform<Float>(), -8, 8), Lerp(rng.Uniform<Float>(), -8, 8),
                        Lerp(rng.Uniform<Float>(), -8, 8));
        return new Ray(o, t - o);
    }

    private static IEnumerable<Primitive> Accelerators(List<Primitive> prims)
    {
        foreach (BVHAggregate.SplitMethod m in Enum.GetValues<BVHAggregate.SplitMethod>())
        {
            yield return new BVHAggregate(prims, 1, m);
            yield return new BVHAggregate(prims, 4, m);
        }
        yield return new KdTreeAggregate(prims);
        yield return new KdTreeAggregate(prims, 80, 1, 0.5f, 4, -1);
        yield return new KdTreeAggregate(prims, 5, 1, 0.5f, 1, 3);
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(500, 20, false)]
    [InlineData(500, 20, true)]
    [InlineData(3000, 0, true)]
    public void MatchesBruteForce(int nTris, int nSpheres, bool quantize)
    {
        RNG rng = new((ulong)(nTris * 31 + nSpheres), 7);
        List<Primitive> prims = RandomScene(rng, nTris, nSpheres, quantize);
        Bounds3f sceneBounds = new();
        foreach (Primitive prim in prims)
            sceneBounds = Union(sceneBounds, prim.Bounds());

        Ray[] rays = new Ray[300];
        Float[] tMax = new Float[rays.Length];
        for (int i = 0; i < rays.Length; ++i)
        {
            rays[i] = RandomRay(ref rng);
            tMax[i] = (i % 3) switch { 0 => Infinity, 1 => 1, _ => 0.5f };
        }

        foreach (Primitive accel in Accelerators(prims))
        {
            Bounds3f b = accel.Bounds();
            Assert.True(Inside(sceneBounds.pMin, b) && Inside(sceneBounds.pMax, b));
            int nHits = 0;
            for (int i = 0; i < rays.Length; ++i)
            {
                // Closest hit over all primitives
                Float t = tMax[i];
                bool any = false;
                foreach (Primitive prim in prims)
                    if (prim.Intersect(rays[i], t) is { } si)
                    {
                        t = si.tHit;
                        any = true;
                    }

                ShapeIntersection? hit = accel.Intersect(rays[i], tMax[i]);
                Assert.Equal(any, hit.HasValue);
                Assert.Equal(any, accel.IntersectP(rays[i], tMax[i]));
                if (any)
                {
                    Assert.Equal(t, hit!.Value.tHit);
                    ++nHits;
                }
            }
            if (prims.Count > 10)
                Assert.True(nHits > 10, $"nHits {nHits}");
        }
    }

    [Fact]
    public void TransformedAndAnimated()
    {
        RNG rng = new(3);
        List<Primitive> prims = RandomScene(rng, 200, 5, false);
        BVHAggregate bvh = new(prims, 4);
        Transform renderFromPrimitive = Translate(new Vector3f(1, 2, 3)) * RotateY(30) * Scale(1, 2, 0.5f);
        TransformedPrimitive tp = new(bvh, renderFromPrimitive);
        Assert.Equal(renderFromPrimitive.Apply(bvh.Bounds()), tp.Bounds());

        AnimatedTransform at = new(new Transform(), 0, Translate(new Vector3f(3, 0, 1)), 1);
        AnimatedPrimitive ap = new(bvh, at);
        Bounds3f ab = ap.Bounds();
        Assert.True(Inside(bvh.Bounds().pMin, ab) && Inside(bvh.Bounds().pMax + new Vector3f(3, 0, 1), ab));

        int nHits = 0;
        for (int i = 0; i < 300; ++i)
        {
            Ray r = RandomRay(ref rng);
            r.time = rng.Uniform<Float>();

            // Transformed: same as intersecting the ray in primitive space
            Float tMax = Infinity;
            Ray rp = renderFromPrimitive.ApplyInverse(r, ref tMax);
            ShapeIntersection? si = tp.Intersect(r);
            ShapeIntersection? sip = bvh.Intersect(rp, tMax);
            Assert.Equal(sip.HasValue, si.HasValue);
            Assert.Equal(sip.HasValue, tp.IntersectP(r));
            if (si.HasValue)
            {
                ++nHits;
                Assert.Equal(sip!.Value.tHit, si.Value.tHit);
                Point3f p = renderFromPrimitive.Apply(sip.Value.intr.p());
                Assert.True(Distance(p, si.Value.intr.p()) < 1e-3f);
            }

            // Animated: same as the transformed primitive at the ray's time
            Transform interp = at.Interpolate(r.time);
            TransformedPrimitive tpAtTime = new(bvh, interp);
            ShapeIntersection? sia = ap.Intersect(r), sit = tpAtTime.Intersect(r);
            Assert.Equal(sit.HasValue, sia.HasValue);
            Assert.Equal(sit.HasValue, ap.IntersectP(r));
            if (sia.HasValue)
                Assert.Equal(sit!.Value.tHit, sia.Value.tHit);
        }
        Assert.True(nHits > 10);
    }

    private sealed class ConstantAlpha(Float a) : FloatTexture
    {
        public override Float Evaluate(in TextureEvalContext ctx) => a;
    }

    [Fact]
    public void AlphaCutout()
    {
        Sphere sphere = new(new Transform(), new Transform(), false, 1, -1, 1, 360);
        Ray r = new(new Point3f(0, 0, -5), new Vector3f(0, 0, 1));

        // Fully transparent: the ray passes through both sides of the sphere.
        GeometricPrimitive transparent = new(sphere, null, null, new MediumInterface(), new ConstantAlpha(0));
        Assert.Null(transparent.Intersect(r));
        Assert.False(transparent.IntersectP(r));

        // Opaque
        GeometricPrimitive opaque = new(sphere, null, null, new MediumInterface(), new ConstantAlpha(1));
        Assert.Equal(4, opaque.Intersect(r)!.Value.tHit, 1e-5f);
        Assert.True(opaque.IntersectP(r));
    }

    [Fact]
    public void CreateAccelerator()
    {
        RNG rng = new(5);
        List<Primitive> prims = RandomScene(rng, 50, 0, false);
        ParameterDictionary empty = new();
        Assert.IsType<BVHAggregate>(Util.CreateAccelerator("bvh", prims, empty));
        Assert.IsType<KdTreeAggregate>(Util.CreateAccelerator("kdtree", prims, empty));
        Assert.Throws<FatalErrorException>(() => Util.CreateAccelerator("octree", prims, empty));
    }

    private struct IntLess : IStdLess<int>
    {
        public readonly bool Less(in int a, in int b) => a < b;
    }

    private struct IsEven : IStdPredicate<int>
    {
        public readonly bool Evaluate(in int v) => (v & 1) == 0;
    }

    [Fact]
    public void StdAlgorithms()
    {
        RNG rng = new(11);
        foreach (int n in (int[])[0, 1, 2, 3, 4, 15, 16, 17, 100, 1000, 10000])
        {
            for (int trial = 0; trial < 5; ++trial)
            {
                int range = trial switch { 0 => 3, 1 => n + 1, _ => 1 << 20 };
                int[] v = new int[n];
                for (int i = 0; i < n; ++i)
                    v[i] = (int)rng.Uniform<uint>() % range;
                if (trial == 3)
                    Array.Sort(v);
                if (trial == 4)
                    Array.Reverse(v);

                int[] sorted = (int[])v.Clone();
                Array.Sort(sorted);

                int[] s = (int[])v.Clone();
                StdSort<int, IntLess>(s, new IntLess());
                Assert.Equal(sorted, s);

                if (n > 0)
                {
                    int nth = (int)(rng.Uniform<uint>() % (uint)n);
                    int[] e = (int[])v.Clone();
                    StdNthElement<int, IntLess>(e, nth, new IntLess());
                    Assert.Equal(sorted[nth], e[nth]);
                    for (int i = 0; i < nth; ++i)
                        Assert.True(e[i] <= e[nth]);
                    for (int i = nth + 1; i < n; ++i)
                        Assert.True(e[i] >= e[nth]);
                    Array.Sort(e);
                    Assert.Equal(sorted, e);
                }

                int[] p = (int[])v.Clone();
                int mid = StdPartition<int, IsEven>(p, new IsEven());
                Assert.Equal(v.Count(x => (x & 1) == 0), mid);
                for (int i = 0; i < n; ++i)
                    Assert.Equal(i < mid, (p[i] & 1) == 0);
                Array.Sort(p);
                Assert.Equal(sorted, p);
            }
        }
    }
}

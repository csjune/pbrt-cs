// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/shapes_test.cpp (Triangle.Watertight is disabled in
// pbrt too). The ParallelFor tests collect their results and check them
// afterward, since assertions can't fail on the worker threads.

namespace Pbrt.Tests;

public class ShapesTests
{
    private static Float pExp(RNG rng, Float exp = 8)
    {
        Float logu = Lerp(rng.Uniform<Float>(), -exp, exp);
        return FMath.Pow(10, logu);
    }

    private static Float pUnif(RNG rng, Float range = 10) => Lerp(rng.Uniform<Float>(), -range, range);

    private static Triangle? GetRandomTriangle(Func<Float> value)
    {
        // Triangle vertices
        Point3f[] v = new Point3f[3];
        for (int j = 0; j < 3; ++j)
            for (int k = 0; k < 3; ++k)
                v[j][k] = value();

        if (LengthSquared(Cross(v[1] - v[0], v[2] - v[0])) < 1e-20)
            // Don't into trouble with ~degenerate triangles.
            return null;

        // Create the corresponding Triangle.
        TriangleMesh mesh = new(new Transform(), false, (int[])[0, 1, 2], v, null, null, null, null);
        List<Shape> triVec = Triangle.CreateTriangles(mesh);
        Assert.Single(triVec);
        return triVec[0] as Triangle;
    }

    [Fact]
    public void TriangleReintersect()
    {
        for (int i = 0; i < 1000; ++i)
        {
            RNG rng = new((ulong)i);
            Shape? tri = GetRandomTriangle(() => pExp(rng));
            if (tri == null)
                continue;

            // Sample a point on the triangle surface to shoot the ray toward.
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            ShapeSample? ts = tri.Sample(u);
            Assert.True(ts.HasValue);

            // Choose a ray origin.
            Point3f o = default;
            for (int j = 0; j < 3; ++j)
                o[j] = pExp(rng);

            // Intersect the ray with the triangle.
            Ray r = new(o, ts!.Value.intr.p() - o);
            ShapeIntersection? si = tri.Intersect(r);
            if (!si.HasValue)
                // We should almost always find an intersection, but rarely
                // miss, due to round-off error. Just do another go-around in
                // this case.
                continue;
            SurfaceInteraction isect = si.Value.intr;

            // Now trace a bunch of rays leaving the intersection point.
            for (int j = 0; j < 1000; ++j)
            {
                // Random direction leaving the intersection point.
                Point2f u2 = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Vector3f w = SampleUniformSphere(u2);
                Ray rOut = isect.SpawnRay(w);
                Assert.False(tri.IntersectP(rOut));
                Assert.False(tri.Intersect(rOut).HasValue);

                // Choose a random point to trace rays to.
                Point3f p2 = default;
                for (int k = 0; k < 3; ++k)
                    p2[k] = pExp(rng);
                rOut = isect.SpawnRayTo(p2);

                Assert.False(tri.IntersectP(rOut, 1));
                Assert.False(tri.Intersect(rOut, 1).HasValue);
            }
        }
    }

    // Checks the closed-form solid angle computation for triangles against a
    // Monte Carlo estimate of it.
    [Fact]
    public void TriangleSolidAngle()
    {
        for (int i = 0; i < 50; ++i)
        {
            const Float range = 10;
            RNG rng = new((ulong)(100 + i)); // Use different triangles than the Triangle/Sample test.
            Triangle? tri = GetRandomTriangle(() => pUnif(rng, range));
            if (tri == null)
                continue;

            // Ensure that the reference point isn't too close to the
            // triangle's surface (which makes the Monte Carlo stuff have more
            // variance, thus requiring more samples).
            Point3f pc = new(pUnif(rng, range), pUnif(rng, range), pUnif(rng, range));
            pc[(int)(rng.Uniform<uint>() % 3)] = rng.Uniform<Float>() > .5 ? (-range - 3) : (range + 3);

            // Compute a reference value using Triangle::Sample()
            const int count = 64 * 1024;
            ShapeSampleContext ctx = new(new Point3fi(pc), new Normal3f(), new Normal3f(), 0);
            double triSampleEstimate = 0;
            for (int j = 0; j < count; ++j)
            {
                Point2f u = new(RadicalInverse(0, (ulong)j), RadicalInverse(1, (ulong)j));
                ShapeSample? ss = tri.Sample(ctx, u);
                Assert.True(ss.HasValue);
                Assert.True(ss!.Value.pdf > 0);
                triSampleEstimate += 1.0 / (count * ss.Value.pdf);
            }

            static double error(double a, double b)
            {
                if (Math.Abs(a) < 1e-4 || Math.Abs(b) < 1e-4)
                    return Math.Abs(a - b);
                return Math.Abs((a - b) / b);
            }

            // Now compute the subtended solid angle of the triangle in closed
            // form.
            Float sphericalArea = tri.SolidAngle(pc);

            Assert.True(error(sphericalArea, triSampleEstimate) < .015,
                        $"spherical area: {sphericalArea}, tri sampling: {triSampleEstimate}, pc = {pc}, tri index {i}");
        }
    }

    // Check for incorrect self-intersection: assumes that the shape is convex,
    // such that if the dot product of an outgoing ray and the surface normal
    // at a point is positive, then a ray leaving that point in that direction
    // should never intersect the shape.
    private static int TestReintersectConvex(Shape shape, RNG rng)
    {
        // Ray origin
        Point3f o = default;
        for (int c = 0; c < 3; ++c)
            o[c] = pExp(rng);

        // Destination: a random point in the shape's bounding box.
        Bounds3f bbox = shape.Bounds();
        Point3f t = default;
        for (int c = 0; c < 3; ++c)
            t[c] = rng.Uniform<Float>();
        Point3f p2 = bbox.Lerp(t);

        // Ray to intersect with the shape.
        Ray r = new(o, p2 - o);
        if (rng.Uniform<Float>() < .5)
            r.d = Normalize(r.d);

        // We should usually (but not always) find an intersection.
        ShapeIntersection? si = shape.Intersect(r);
        if (!si.HasValue)
            return 0;
        SurfaceInteraction isect = si.Value.intr;

        // Now trace a bunch of rays leaving the intersection point.
        int n = 0;
        for (int j = 0; j < 1000; ++j)
        {
            // Random direction leaving the intersection point.
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Vector3f w = SampleUniformSphere(u);
            // Make sure it's in the same hemisphere as the surface normal.
            w = FaceForward(w, isect.n);
            Ray rOut = isect.SpawnRay(w);

            if (shape.IntersectP(rOut))
                ++n;
            if (shape.Intersect(rOut).HasValue)
                ++n;

            // Choose a random point to trace rays to.
            Point3f p2b = default;
            for (int c = 0; c < 3; ++c)
                p2b[c] = pExp(rng);
            // Make sure that the point we're tracing rays toward is in the
            // hemisphere about the intersection point's surface normal.
            w = p2b - isect.p();
            w = FaceForward(w, isect.n);
            p2b = isect.p() + w;
            rOut = isect.SpawnRayTo(p2b);

            if (shape.IntersectP(rOut, 1))
                ++n;
            if (shape.Intersect(rOut, 1).HasValue)
                ++n;
        }

        return n;
    }

    private static Transform randomTransform(RNG rng)
    {
        Transform t = new();
        Float rr() => -10 + 20 * rng.Uniform<Float>();
        Float rt()
        {
            Float f = pExp(rng);
            return rng.Uniform<Float>() > .5 ? f : -f;
        }

        for (int i = 0; i < 1; ++i)
        {
            Float s0 = pExp(rng, 4), s1 = pExp(rng, 4), s2 = pExp(rng, 4);
            t = t * Scale(s0, s1, s2);
            Float t0 = rt(), t1 = rt(), t2 = rt();
            t = t * Translate(new Vector3f(t0, t1, t2));
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Float angle = rr() * 20;
            t = t * Rotate(angle, SampleUniformSphere(u));
        }
        return t;
    }

    private const int nReintersect = 1000;

    // Runs _test_ for 0..nReintersect-1 in parallel and returns the indices
    // where it returned non-zero.
    private static List<string> ParallelFailures(Func<int, string?> test)
    {
        string?[] results = new string?[nReintersect];
        ParallelFor(0, nReintersect, (long i) => results[i] = test((int)i));
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    [Fact]
    public void FullSphereReintersect()
    {
        Assert.Empty(ParallelFailures(i =>
        {
            RNG rng = new((ulong)i);
            Transform identity = new();
            Float radius = pExp(rng, 4);
            Float zMin = -radius;
            Float zMax = radius;
            Float phiMax = 360;
            Sphere sphere = new(identity, identity, false, radius, zMin, zMax, phiMax);
            int n0 = TestReintersectConvex(sphere, rng);

            Transform tr = randomTransform(rng);
            Transform ti = Inverse(tr);
            Sphere tsphere = new(tr, ti, false, radius, zMin, zMax, phiMax);
            int n1 = TestReintersectConvex(tsphere, rng);
            return n0 != 0 || n1 != 0 ? $"{i}: {n0} {n1}, tr {tr}" : null;
        }));
    }

    [Fact]
    public void PartialSphereNormal()
    {
        for (int i = 0; i < 100; ++i)
        {
            RNG rng = new((ulong)i);
            Transform identity = new();
            Float radius = pExp(rng, 4);
            Float zMin = rng.Uniform<Float>() < 0.5 ? -radius : Lerp(rng.Uniform<Float>(), -radius, radius);
            Float zMax = rng.Uniform<Float>() < 0.5 ? radius : Lerp(rng.Uniform<Float>(), -radius, radius);
            Float phiMax = rng.Uniform<Float>() < 0.5 ? 360 : rng.Uniform<Float>() * 360;
            Sphere sphere = new(identity, identity, false, radius, zMin, zMax, phiMax);

            // Ray origin
            Point3f o = default;
            for (int c = 0; c < 3; ++c)
                o[c] = pExp(rng);

            // Destination: a random point in the shape's bounding box.
            Bounds3f bbox = sphere.Bounds();
            Point3f t = default;
            for (int c = 0; c < 3; ++c)
                t[c] = rng.Uniform<Float>();
            Point3f p2 = bbox.Lerp(t);

            // Ray to intersect with the shape.
            Ray r = new(o, p2 - o);
            if (rng.Uniform<Float>() < .5)
                r.d = Normalize(r.d);

            // We should usually (but not always) find an intersection.
            ShapeIntersection? si = sphere.Intersect(r, Infinity);
            if (!si.HasValue)
                continue;

            Float dot = Dot(Normalize(si.Value.intr.n), Normalize(new Vector3f(si.Value.intr.p())));
            // (EXPECT_FLOAT_EQ: within 4 ulps)
            Assert.True(FMath.Abs(1 - dot) <= 4 * MachineEpsilon, $"{dot}");
        }
    }

    [Fact]
    public void PartialSphereReintersect()
    {
        Assert.Empty(ParallelFailures(i =>
        {
            RNG rng = new((ulong)i);
            Transform identity = new();
            Float radius = pExp(rng, 4);
            Float zMin = rng.Uniform<Float>() < 0.5 ? -radius : Lerp(rng.Uniform<Float>(), -radius, radius);
            Float zMax = rng.Uniform<Float>() < 0.5 ? radius : Lerp(rng.Uniform<Float>(), -radius, radius);
            Float phiMax = rng.Uniform<Float>() < 0.5 ? 360 : rng.Uniform<Float>() * 360;
            Sphere sphere = new(identity, identity, false, radius, zMin, zMax, phiMax);

            int n = TestReintersectConvex(sphere, rng);
            return n != 0 ? $"{i}: {n}" : null;
        }));
    }

    [Fact]
    public void CylinderReintersect()
    {
        Assert.Empty(ParallelFailures(i =>
        {
            RNG rng = new((ulong)i);
            Transform identity = new();
            Float radius = pExp(rng, 4);
            Float zMin = pExp(rng, 4) * (rng.Uniform<Float>() < 0.5 ? -1 : 1);
            Float zMax = pExp(rng, 4) * (rng.Uniform<Float>() < 0.5 ? -1 : 1);
            Float phiMax = rng.Uniform<Float>() < 0.5 ? 360 : rng.Uniform<Float>() * 360;
            Cylinder cyl = new(identity, identity, false, radius, zMin, zMax, phiMax);

            int n0 = TestReintersectConvex(cyl, rng);

            Transform tr = randomTransform(rng);
            Transform ti = Inverse(tr);
            Cylinder tcyl = new(tr, ti, false, radius, zMin, zMax, phiMax);
            int n1 = TestReintersectConvex(tcyl, rng);
            return n0 != 0 || n1 != 0 ? $"{i}: {n0} {n1}" : null;
        }));
    }

    [Fact]
    public void TriangleBadCases()
    {
        Point3f[] p = [new(-1113.45459f, -79.049614f, -56.2431908f), new(-1113.45459f, -87.0922699f, -56.2431908f),
                       new(-1113.45459f, -79.2090149f, -56.2431908f)];
        TriangleMesh mesh = new(new Transform(), false, (int[])[0, 1, 2], p, null, null, null, null);
        List<Shape> tris = Triangle.CreateTriangles(mesh);
        Assert.Single(tris);

        Ray ray = new(new Point3f(-1081.47925f, 99.9999542f, 87.7701111f),
                      new Vector3f(-32.1072998f, -183.355865f, -144.607635f), 0.9999f);

        Assert.False(tris[0].Intersect(ray).HasValue);
    }

    [Fact]
    public void BilinearPatchOffset()
    {
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Float height = -400 + 800 * rng.Uniform<Float>();
            Float x0 = -400 + 800 * rng.Uniform<Float>();
            Float x1 = x0 + 400 * rng.Uniform<Float>();
            Float z0 = -400 + 800 * rng.Uniform<Float>();
            Float z1 = x0 + 400 * rng.Uniform<Float>();
            Point3f[] p = [new(x0, height, z0), new(x1, height, z0), new(x0, height, x1), new(x1, height, z1)];

            BilinearPatchMesh mesh = new(new Transform(), false, (int[])[0, 1, 2, 3], p, null, null, null, null);
            for (int j = 0; j < 100; ++j)
            {
                Point3f o = new(-20 + 40 * rng.Uniform<Float>(), -20 + 40 * rng.Uniform<Float>(),
                                -20 + 40 * rng.Uniform<Float>());

                Point2f uv = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Point3f pPatch = (1 - uv[0]) * (1 - uv[1]) * p[0] + uv[0] * (1 - uv[1]) * p[1] +
                                 (1 - uv[0]) * uv[1] * p[2] + uv[0] * uv[1] * p[3];
                Ray r = new(o, pPatch - o);
                BilinearIntersection? isect = IntersectBilinearPatch(r, Infinity, p[0], p[1], p[2], p[3]);
                if (!isect.HasValue)
                    // Hmm...
                    continue;

                SurfaceInteraction intr = BilinearPatch.InteractionFromIntersection(mesh, 0, isect.Value.uv, 0f, -r.d);

                Ray spawned = intr.SpawnRay(r.d);
                BilinearIntersection? spawnedIsect = IntersectBilinearPatch(spawned, Infinity, p[0], p[1], p[2], p[3]);
                Assert.False(spawnedIsect.HasValue);
            }
        }
    }
    [Fact]
    public void PlyMeshDisplacement()
    {
        string fn = ImageTests.InTestDir("displace.ply");
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        Point2f[] uv = [new(0, 0), new(1, 0), new(0, 1)];
        Assert.True(WritePLY(fn, [0, 1, 2], [], p, [], uv, []));

        ParsedParameter filename = new(new FileLoc()) { type = "string", name = "filename" };
        filename.strings.Add(fn);
        ParsedParameter disp = new(new FileLoc()) { type = "texture", name = "displacement" };
        disp.strings.Add("d");
        ParsedParameter edge = new(new FileLoc()) { type = "float", name = "edgelength" };
        edge.floats.Add(0.3f);
        Dictionary<string, FloatTexture> textures = new() { ["d"] = new FloatConstantTexture(0.1f) };
        List<Shape> shapes = Shape.Create("plymesh", new Transform(), new Transform(), false,
                                          new ParameterDictionary([filename, disp, edge], RGBColorSpace.sRGB),
                                          textures, null);
        // An undefined texture is an error.
        Assert.Throws<FatalErrorException>(() =>
            Shape.Create("plymesh", new Transform(), new Transform(), false,
                         new ParameterDictionary([filename, disp], RGBColorSpace.sRGB),
                         new Dictionary<string, FloatTexture>(), null));
        Assert.True(RemoveFile(fn));

        // The triangle is split so that its edges are shorter than 0.3, and
        // every vertex moves 0.1 along the normal.
        Assert.True(shapes.Count > 8);
        Float area = 0;
        foreach (Shape s in shapes)
        {
            area += s.Area();
            Bounds3f b = s.Bounds();
            Assert.Equal(0.1f, FMath.Abs(b.pMin.z), 1e-6f);
            Assert.Equal(0.1f, FMath.Abs(b.pMax.z), 1e-6f);
        }
        Assert.Equal(0.5f, area, 1e-4f);
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/lightsamplers_test.cpp, plus a check of
// LightSampler.Create().

namespace Pbrt.Tests;

[Collection("Options")]
public class LightSamplersTests
{
    // gtest's EXPECT_FLOAT_EQ: within 4 ULPs.
    private static void AssertFloatEq(Float expected, Float actual) =>
        Assert.True(Math.Abs(BitConverter.SingleToInt32Bits((float)expected) - BitConverter.SingleToInt32Bits((float)actual)) <= 4,
                    $"{expected} != {actual}");

    [Fact]
    public void BVHLightSampling_OneSpot()
    {
        Transform id = new();
        ConstantSpectrum one = new(1f);
        Light[] lights = [new SpotLight(id, new MediumInterface(), one, 1f /* scale */, 45f /* total width */,
                                        44f /* falloff start */)];
        BVHLightSampler distrib = new(lights);

        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            // Random point in [-5, 5]
            Point3f p = new(Lerp(rng.Uniform<Float>(), -5, 5), Lerp(rng.Uniform<Float>(), -5, 5),
                            Lerp(rng.Uniform<Float>(), -5, 5));

            // Avoid possibly ambiguous cases right at the edge.
            Float angle = Degrees(FMath.Acos(Normalize(new Vector3f(p)).z));
            if (44.75f < angle && angle < 45.25f)
            {
                --i;
                continue;
            }

            Interaction intr = new(p, 0f, (Medium?)null);
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            LightLiSample? ls = lights[0].SampleLi(intr, u, lambda);

            SampledLight? sampledLight = distrib.Sample(intr, rng.Uniform<Float>());

            if (sampledLight is not SampledLight sl)
            {
                Assert.False(ls.HasValue);
                continue;
            }
            else
                Assert.True(ls.HasValue);

            Assert.Equal(1, sl.p);
            Assert.Same(lights[0], sl.light);
            Assert.True(!!ls!.Value.L, $"{ls.Value.L} @ {p}");
        }
    }

    // For a random collection of point lights, make sure that they're all sampled
    // with an appropriate ratio of frequency to pdf.
    [Fact]
    public void BVHLightSampling_Point()
    {
        RNG rng = new();
        List<Light> lights = [];
        Dictionary<Light, int> lightToIndex = new(ReferenceEqualityComparer.Instance);
        ConstantSpectrum one = new(1f);
        for (int i = 0; i < 33; ++i)
        {
            // Random point in [-5, 5]
            Vector3f p = new(Lerp(rng.Uniform<Float>(), -5, 5), Lerp(rng.Uniform<Float>(), -5, 5),
                             Lerp(rng.Uniform<Float>(), -5, 5));
            lights.Add(new PointLight(Translate(p), new MediumInterface(), one, 1f));
            lightToIndex[lights[^1]] = i;
        }
        BVHLightSampler distrib = new(lights.ToArray());

        for (int i = 0; i < 10; ++i)
        {
            // Don't get too close to the light bbox
            Float r() => rng.Uniform<Float>() < .5f ? Lerp(rng.Uniform<Float>(), -15, -7)
                                                    : Lerp(rng.Uniform<Float>(), 7, 16);
            Point3f p = new(r(), r(), r());

            Float[] sumWt = new Float[lights.Count];
            const int nSamples = 10000;
            foreach (Float u in Stratified1D(nSamples))
            {
                Interaction intr = new(p, 0, (Medium?)null);
                SampledLight? sampledLight = distrib.Sample(intr, u);
                // Can assume this because it's all point lights
                Assert.True(sampledLight.HasValue);
                SampledLight sl = sampledLight.Value;

                Assert.True(sl.p > 0);
                sumWt[lightToIndex[sl.light]] += 1 / (sl.p * nSamples);

                AssertFloatEq(sl.p, distrib.PMF(intr, sl.light));
            }

            for (int j = 0; j < lights.Count; ++j)
            {
                Assert.True(sumWt[j] >= .98f);
                Assert.True(sumWt[j] < 1.02f);
            }
        }
    }

    // Similar to BVHLightSampling.Point, but vary light power
    [Fact(Skip = DoubleMode.PbrtFails)]
    public void BVHLightSampling_PointVaryPower()
    {
        RNG rng = new(53251);
        List<Light> lights = [];
        List<Float> lightPower = [];
        Float sumPower = 0;
        Dictionary<Light, int> lightToIndex = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < 82; ++i)
        {
            // Random point in [-5, 5]
            Vector3f p = new(Lerp(rng.Uniform<Float>(), -5, 5), Lerp(rng.Uniform<Float>(), -5, 5),
                             Lerp(rng.Uniform<Float>(), -5, 5));
            lightPower.Add(rng.Uniform<Float>());
            sumPower += lightPower[^1];
            lights.Add(new PointLight(Translate(p), new MediumInterface(), new ConstantSpectrum(lightPower[^1]), 1f));
            lightToIndex[lights[^1]] = i;
        }
        BVHLightSampler distrib = new(lights.ToArray());

        Float r() => rng.Uniform<Float>() < .5f ? Lerp(rng.Uniform<Float>(), -15, -7)
                                                : Lerp(rng.Uniform<Float>(), 7, 16);

        for (int i = 0; i < 10; ++i)
        {
            // Don't get too close to the light bbox
            Point3f p = new(r(), r(), r());

            Float[] sumWt = new Float[lights.Count];
            const int nSamples = 100000;
            foreach (Float u in Stratified1D(nSamples))
            {
                Interaction intr = new(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
                SampledLight? sampledLight = distrib.Sample(intr, u);
                // Again because it's all point lights...
                Assert.True(sampledLight.HasValue);

                Light light = sampledLight.Value.light;
                Float pdf = sampledLight.Value.p;
                Assert.True(pdf > 0);
                sumWt[lightToIndex[light]] += 1 / (pdf * nSamples);

                Assert.True(FMath.Abs(distrib.PMF(intr, light) - pdf) / pdf < 1e-4f);
            }

            for (int j = 0; j < lights.Count; ++j)
            {
                Assert.True(sumWt[j] >= .95f);
                Assert.True(sumWt[j] < 1.05f);
            }
        }

        // Now, for very far away points (so d^2 is about the same for all
        // lights), make sure that sampling frequencies for each light are
        // basically proportional to their power
        for (int i = 0; i < 10; ++i)
        {
            // Don't get too close to the light bbox
            Point3f p = new(10000 * r(), 10000 * r(), 10000 * r());
            Interaction intr = new(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));

            int[] counts = new int[lights.Count];
            const int nSamples = 100000;
            foreach (Float u in Stratified1D(nSamples))
            {
                SampledLight? sampledLight = distrib.Sample(intr, u);
                Assert.True(sampledLight.HasValue);
                Light light = sampledLight.Value.light;
                Float pdf = sampledLight.Value.p;
                Assert.True(pdf > 0);
                ++counts[lightToIndex[light]];

                AssertFloatEq(pdf, distrib.PMF(intr, light));
            }

            for (int j = 0; j < lights.Count; ++j)
            {
                Float expected = nSamples * lightPower[j] / sumPower;
                Assert.True(counts[j] >= .97f * expected);
                Assert.True(counts[j] < 1.03f * expected);
            }
        }
    }

    [Fact]
    public void BVHLightSampling_OneTri()
    {
        RNG rng = new(5251);
        Transform id = new();
        // Light is illuminating points with z > 0
        TriangleMesh mesh = new(id, false /* rev orientation */, [0, 1, 2],
                                [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], null, null, null, null);
        List<Shape> tris = Triangle.CreateTriangles(mesh);

        Assert.Single(tris);
        ConstantSpectrum one = new(1f);
        Light[] lights = [new DiffuseAreaLight(id, new MediumInterface(), one, 1f, tris[0], null, new Image(), null,
                                               false /* two sided */)];

        BVHLightSampler distrib = new(lights);

        for (int i = 0; i < 10; ++i)
        {
            // Random point in [-5, 5]
            Point3f p = new(Lerp(rng.Uniform<Float>(), -5, 5), Lerp(rng.Uniform<Float>(), -5, 5),
                            Lerp(rng.Uniform<Float>(), -5, 5));

            Interaction intr = new(p, 0f, (Medium?)null);
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            LightLiSample? ls = lights[0].SampleLi(intr, u, lambda);

            SampledLight? sampledLight = distrib.Sample(intr, rng.Uniform<Float>());

            // Note that the converse (sampledLight ->
            // ls) isn't always true since the light importance
            // metric is conservative.
            if (!sampledLight.HasValue)
                Assert.False(ls.HasValue);
        }
    }

    private static Light[] RandomLights(int n)
    {
        List<Light> lights = [];
        RNG rng = new(6502);
        Float r() => rng.Uniform<Float>();

        Transform id = new();
        for (int i = 0; i < n; ++i)
        {
            // Triangle
            {
                // (GCC evaluates the braced initializers from left to right.)
                Point3f p0 = new(r(), r(), r()), p1 = new(r(), r(), r()), p2 = new(r(), r(), r());
                TriangleMesh mesh = new(id, false /* rev orientation */, [0, 1, 2], [p0, p1, p2], null, null, null,
                                        null);
                List<Shape> tris = Triangle.CreateTriangles(mesh);
                CHECK_EQ(1, tris.Count);
                lights.Add(new DiffuseAreaLight(id, new MediumInterface(), new ConstantSpectrum(r()), 1f, tris[0], null,
                                                new Image(), null, false /* two sided */));
            }

            // Random point light
            {
                Vector3f p = new(Lerp(rng.Uniform<Float>(), -5, 5), Lerp(rng.Uniform<Float>(), -5, 5),
                                 Lerp(rng.Uniform<Float>(), -5, 5));
                lights.Add(new PointLight(Translate(p), new MediumInterface(), new ConstantSpectrum(r()), 1f));
            }
        }

        return lights.ToArray();
    }

    private static void CheckPdfMethod(LightSampler distrib, bool alwaysSamples)
    {
        RNG rng = new(5251);
        Float r() => rng.Uniform<Float>();
        for (int i = 0; i < 100; ++i)
        {
            Point3f p = new(-1 + 3 * r(), -1 + 3 * r(), -1 + 3 * r());
            Float u = rng.Uniform<Float>();
            Interaction intr = new(new Point3fi(p), new Normal3f(0, 0, 0), new Point2f(0, 0));
            SampledLight? sampledLight = distrib.Sample(intr, u);
            if (alwaysSamples)
                Assert.True(sampledLight.HasValue, $"{i} - {p}");
            // It's actually legit to sometimes get no lights from the BVH; as
            // the bounds tighten up as we get deeper in the tree, it may turn
            // out that the path we followed didn't have any lights after all.
            if (sampledLight is SampledLight sl)
                AssertFloatEq(sl.p, distrib.PMF(intr, sl.light));
        }
    }

    [Fact]
    public void BVHLightSampling_PdfMethod() => CheckPdfMethod(new BVHLightSampler(RandomLights(20)), false);

    [Fact]
    public void ExhaustiveLightSampling_PdfMethod() =>
        CheckPdfMethod(new ExhaustiveLightSampler(RandomLights(20)), true);

    [Fact]
    public void UniformLightSampling_PdfMethod() =>
        CheckPdfMethod(new UniformLightSampler(RandomLights(20)), true);

    [Fact]
    public void PowerLightSampling_PdfMethod() => CheckPdfMethod(new PowerLightSampler(RandomLights(20)), true);

    [Fact]
    public void Create()
    {
        Light[] lights = RandomLights(2);
        Assert.IsType<UniformLightSampler>(LightSampler.Create("uniform", lights));
        Assert.IsType<PowerLightSampler>(LightSampler.Create("power", lights));
        Assert.IsType<BVHLightSampler>(LightSampler.Create("bvh", lights));
        Assert.IsType<ExhaustiveLightSampler>(LightSampler.Create("exhaustive", lights));
        // An unknown name is an error, but a BVH sampler is used.
        Assert.IsType<BVHLightSampler>(LightSampler.Create("other", lights));
        Assert.Null(LightSampler.Create("bvh", []).Sample(new LightSampleContext(), 0.5f));
    }
}

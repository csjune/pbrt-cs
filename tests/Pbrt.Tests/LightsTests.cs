// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/lights_test.cpp, plus a few checks of Light.Create().

namespace Pbrt.Tests;

[Collection("Options")]
public class LightsTests
{
    [Fact]
    public void SpotLight_Power()
    {
        ConstantSpectrum I = new(10);
        Transform id = new();
        SpotLight light = new(id, new MediumInterface(), I, 1f /* scale */, 60 /* total width */,
                              40 /* falloff start */);

        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
        SampledSpectrum phi = light.Phi(lambda);

        int nSamples = 1024 * 1024;
        double phiSampled = 0;
        for (int i = 0; i < nSamples; ++i)
        {
            Vector3f w = SampleUniformSphere(new Point2f(RadicalInverse(0, (ulong)i), RadicalInverse(1, (ulong)i)));
            phiSampled += light.I(w, lambda)[0];
        }
        phiSampled /= nSamples * UniformSpherePDF();

        Assert.True(Math.Abs(phiSampled - phi[0]) < 1e-3, $" qmc: {phiSampled}, closed-form: {phi[0]}");
    }

    [Fact]
    public void SpotLight_Sampling()
    {
        ConstantSpectrum I = new(10);
        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);

        int[][] widthStart = [[50, 0], [40, 10], [60, 5], [70, 70]];
        Transform id = new();
        foreach (int[] ws in widthStart)
        {
            SpotLight light = new(id, new MediumInterface(), I, 1f /* scale */, ws[0] /* total width */,
                                  ws[1] /* falloff start */);

            RNG rng = new();
            for (int i = 0; i < 100; ++i)
            {
                Point2f u1 = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Point2f u2 = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                LightLeSample? lso = light.SampleLe(u1, u2, ref lambda, 0 /* time */);
                Assert.True(lso.HasValue);
                LightLeSample ls = lso.Value;
                Assert.True(ls.ray.o == new Point3f(0, 0, 0));
                Assert.Equal(1, ls.pdfPos);
                // Importance should be perfect, so a single sample should
                // compute power with zero variance.
                Assert.True(FMath.Abs(light.Phi(lambda)[0] - (ls.L / ls.pdfDir)[0]) < 1e-3f);
            }
        }
    }

    private static Image MakeLightImage(Point2i res)
    {
        Image image = new(PixelFormat.Float, res, ["R", "G", "B"]);
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
            {
                Float val = 0;
                if (((x >= 30 && x <= 200) || x > 400) && y >= 40 && y <= 220)
                {
                    val = (Float)(.2 + FMath.Sin(100 * x * y / (Float)(res[0] * res[1])));
                    val = FMath.Max(0, val);
                }
                image.SetChannels(new Point2i(x, y), [val, val, val]);
            }
        return image;
    }

    [Fact]
    public void GoniometricLight_Power()
    {
        Image image = MakeLightImage(new Point2i(256, 256));
        image = image.SelectChannels(image.GetChannelDesc(["R"]));

        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
        ConstantSpectrum I = new(10);
        GoniometricLight light = new(new Transform(), new MediumInterface(), I, 1f, image);

        SampledSpectrum phi = light.Phi(lambda);

        int sqrtSamples = 1024;
        int nSamples = sqrtSamples * sqrtSamples;
        double phiSampled = 0;
        foreach (Point2f u in Hammersley2D(nSamples))
        {
            Vector3f w = SampleUniformSphere(u);
            phiSampled += light.I(w, lambda).Average();
        }
        phiSampled /= nSamples * UniformSpherePDF();

        Assert.True(Math.Abs(phiSampled - phi[0]) < 3e-3, $" qmc: {phiSampled}, closed-form: {phi[0]}");
    }

    private static void TestPhiVsSampled(Light light, ref SampledWavelengths lambda)
    {
        double sum = 0;
        int count = 100000;
        for (int i = 0; i < count; ++i)
        {
            Point2f u1 = new(RadicalInverse(0, (ulong)i), RadicalInverse(1, (ulong)i));
            Point2f u2 = new(RadicalInverse(2, (ulong)i), RadicalInverse(3, (ulong)i));
            if (light.SampleLe(u1, u2, ref lambda, 0 /* time */) is not LightLeSample ls)
                continue;

            Assert.True(ls.ray.o == new Point3f(0, 0, 0));
            Assert.Equal(1, ls.pdfPos);
            sum += ls.L[0] / ls.pdfDir;
        }
        SampledSpectrum Phi = light.Phi(lambda);
        Assert.True(Math.Abs(sum / count - Phi[0]) / Phi[0] < 1e-2, $"{Phi[0]}, sampled {sum / count}");
    }

    [Fact]
    public void GoniometricLight_Sampling()
    {
        Image image = MakeLightImage(new Point2i(256, 256));
        image = image.SelectChannels(image.GetChannelDesc(["R"]));

        ConstantSpectrum I = new(10);
        GoniometricLight light = new(new Transform(), new MediumInterface(), I, 1f, image);
        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
        TestPhiVsSampled(light, ref lambda);
    }

    [Fact]
    public void ProjectionLight_Power()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
        foreach (Point2i res in (Point2i[])[new(512, 256), new(300, 900)])
        {
            Image image = MakeLightImage(res);

            ProjectionLight light = new(new Transform(), new MediumInterface(), image, RGBColorSpace.sRGB,
                                        10 /* scale */, 30 /* fov */);

            SampledSpectrum phi = light.Phi(lambda);

            int nSamples = 1024 * 1024;
            double phiSampled = 0;
            foreach (Point2f u in Hammersley2D(nSamples))
            {
                Vector3f w = SampleUniformSphere(u);
                phiSampled += light.I(w, lambda)[0];
            }
            phiSampled /= nSamples * UniformSpherePDF();

            Assert.True(Math.Abs(phiSampled - phi[0]) < 1e-3, $"res: {res} qmc: {phiSampled}, closed-form: {phi[0]}");
        }
    }

    [Fact]
    public void ProjectionLight_Sampling()
    {
        foreach (Point2i res in (Point2i[])[new(512, 256), new(300, 900)])
        {
            Image image = MakeLightImage(res);

            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            ProjectionLight light = new(new Transform(), new MediumInterface(), image, RGBColorSpace.sRGB,
                                        10 /* scale */, 30 /* fov */);

            TestPhiVsSampled(light, ref lambda);
        }
    }

    [Fact]
    public void LightBounds_Basics()
    {
        LightBounds bounds = new(new Bounds3f(new Point3f(0, 0, 0), new Point3f(.1f, .1f, .01f)),
                                 new Vector3f(0, 0, 1), 1f /* phi */,
                                 MathF.Cos(0f) /* theta_o: normal spread */,
                                 FMath.Cos(Pi / 2) /* theta_e: falloff given visible normal */,
                                 false /* two-sided */);

        // Positive importance for point on the emissive side
        {
            Interaction intr = new(new Point3f(1, 1, 1), 0f /* time */, (MediumInterface?)null);
            Assert.True(bounds.Importance(intr.p(), intr.n) > 0);
        }

        // Zero importance for point on the non-emissive side
        {
            Interaction intr = new(new Point3f(1, 1, -1), 0f /* time */, (MediumInterface?)null);
            Assert.Equal(0, bounds.Importance(intr.p(), intr.n));
        }

        // Low importance when close to the emitter's plane (since cos theta)
        {
            Interaction intrHigh = new(new Point3f(1, 1, 1), 0f /* time */, (MediumInterface?)null);
            Float impHigh = bounds.Importance(intrHigh.p(), intrHigh.n);

            Interaction intrLow = new(new Point3f(1, 1, .02f), 0f /* time */, (MediumInterface?)null);
            Float impLow = bounds.Importance(intrLow.p(), intrLow.n);

            Assert.True(impLow / impHigh < .2f);
        }
    }

    private static ParameterDictionary Dict(params ParsedParameter[] ps) => new(ps, RGBColorSpace.sRGB);

    private static ParsedParameter P(string type, string name, params Float[] v)
    {
        ParsedParameter p = new(new FileLoc()) { type = type, name = name };
        p.floats.AddRange(v);
        return p;
    }

    [Fact]
    public void Create()
    {
        CameraTransform camT = new(new AnimatedTransform(new Transform()));
        Assert.IsType<PointLight>(Light.Create("point", Dict(), new Transform(), camT, null, null));
        Assert.IsType<SpotLight>(Light.Create("spot", Dict(), new Transform(), camT, null, null));
        Assert.IsType<DistantLight>(Light.Create("distant", Dict(), new Transform(), camT, null, null));
        Assert.IsType<UniformInfiniteLight>(Light.Create("infinite", Dict(), new Transform(), camT, null, null));
        Assert.IsType<UniformInfiniteLight>(Light.Create("infinite", Dict(P("rgb", "L", 1, 2, 3)), new Transform(),
                                                         camT, null, null));
        // A portal with "L" makes a one-pixel image.
        Light portal = Light.Create("infinite",
                                    Dict(P("rgb", "L", 1, 2, 3),
                                         P("point3", "portal", 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0)),
                                    new Transform(), camT, null, null);
        Assert.IsType<PortalImageInfiniteLight>(portal);
        Assert.Equal(LightType.Infinite, portal.Type());
        Assert.Throws<FatalErrorException>(() => Light.Create("sun", Dict(), new Transform(), camT, null, null));
        Assert.Throws<FatalErrorException>(() => Light.Create("projection", Dict(), new Transform(), camT, null,
                                                              null));
    }

    [Fact]
    public void AreaLight()
    {
        TriangleMesh mesh = new(new Transform(), false, [0, 1, 2],
                                [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], null, null, null, null);
        Shape tri = Triangle.CreateTriangles(mesh)[0];
        Light light = Light.CreateArea("diffuse", Dict(P("rgb", "L", 1, 1, 1)), new Transform(),
                                       new MediumInterface(), tri, null, null);
        Assert.Equal(LightType.Area, light.Type());
        SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
        // Emits on the side the normal faces only.
        SampledSpectrum front = light.L(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), new Point2f(),
                                        new Vector3f(0, 0, 1), lambda);
        SampledSpectrum back = light.L(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), new Point2f(),
                                       new Vector3f(0, 0, -1), lambda);
        Assert.True(front[0] > 0);
        Assert.Equal(0, back[0]);

        // A constant zero alpha makes it an invisible DeltaPosition light.
        Light invisible = Light.CreateArea("diffuse", Dict(), new Transform(), new MediumInterface(), tri,
                                           new FloatConstantTexture(0), null);
        Assert.Equal(LightType.DeltaPosition, invisible.Type());
        Assert.Throws<CheckFailedException>(() =>
            invisible.L(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), new Point2f(), new Vector3f(0, 0, 1), lambda));
    }
}

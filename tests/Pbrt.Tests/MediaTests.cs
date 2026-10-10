// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the media (pbrt has no media tests beyond the phase function's;
// rendered images were compared with pbrt's separately).

namespace Pbrt.Tests;

[Collection("Options")]
public class MediaTests
{
    private static Medium MakeMedium(string type, string parameters, Transform? renderFromMedium = null) =>
        Medium.Create(type, ParamDictTests.Dict(parameters), renderFromMedium ?? new Transform(), null);

    private static readonly SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

    // Ratio-tracking estimate of the transmittance along the ray to tMax.
    private static Float RatioTrackingTr(Ray ray, Float tMax, int n)
    {
        RNG rng = new(1234);
        double sum = 0;
        for (int i = 0; i < n; ++i)
        {
            Float Tr = 1;
            SampleT_maj(ray, tMax, rng.Uniform<Float>(), ref rng, lambda,
                (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj, ref RNG rng) =>
                {
                    Float sigma_n = sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0];
                    Assert.True(sigma_n >= -1e-4f);
                    Tr *= FMath.Max(0, sigma_n) / sigma_maj[0];
                    return Tr > 0;
                });
            // (The final T_maj over its pdf is one for the first wavelength.)
            sum += Tr;
        }
        return (Float)(sum / n);
    }

    [Fact]
    public void Create()
    {
        Assert.IsType<HomogeneousMedium>(MakeMedium("homogeneous", ""));
        Assert.IsType<GridMedium>(MakeMedium("uniformgrid", "\"float density\" [1]"));
        Assert.IsType<RGBGridMedium>(MakeMedium("rgbgrid", "\"rgb sigma_s\" [1 1 1]"));
        Assert.IsType<CloudMedium>(MakeMedium("cloud", ""));
        Assert.Throws<FatalErrorException>(() => MakeMedium("nope", ""));
        Assert.Throws<FatalErrorException>(() => MakeMedium("uniformgrid", "\"float density\" [1 2] \"integer nx\" 3"));

        Assert.False(MakeMedium("homogeneous", "").IsEmissive());
        Assert.True(MakeMedium("homogeneous", "\"rgb Le\" [1 1 1]").IsEmissive());
        Assert.True(MakeMedium("uniformgrid", "\"float density\" [1] \"float temperature\" [1000]").IsEmissive());
        Assert.True(MakeMedium("rgbgrid", "\"rgb sigma_a\" [1 1 1] \"rgb Le\" [1 1 1]").IsEmissive());
        Assert.False(MakeMedium("cloud", "").IsEmissive());
    }

    [Fact]
    public void HomogeneousProperties()
    {
        Medium m = MakeMedium("homogeneous", "\"spectrum sigma_a\" [300 0.5 800 0.5] " +
                                         "\"spectrum sigma_s\" [300 2 800 2] \"float scale\" 2");
        MediumProperties mp = m.SamplePoint(new Point3f(1, 2, 3), lambda);
        Assert.Equal(1f, mp.sigma_a[0], 1e-6f);
        Assert.Equal(4f, mp.sigma_s[0], 1e-6f);
        Assert.False(mp.Le.IsNonZero());

        // A single segment with the majorant sigma_t.
        RayMajorantIterator iter = m.SampleRay(new Ray(new Point3f(), new Vector3f(1, 0, 0)), 3, lambda);
        RayMajorantSegment seg = iter.Next()!.Value;
        Assert.Equal(0f, seg.tMin);
        Assert.Equal(3f, seg.tMax);
        Assert.Equal(5f, seg.sigma_maj[0], 1e-6f);
        Assert.Null(iter.Next());
    }

    [Fact]
    public void DDASegments()
    {
        RNG rng = new(7);
        Float[] density = new Float[5 * 4 * 3];
        for (int i = 0; i < density.Length; ++i)
            density[i] = rng.Uniform<Float>();
        Medium m = MakeMedium("uniformgrid", $"\"float density\" [{string.Join(" ", density)}] \"integer nx\" 5 " +
                                         "\"integer ny\" 4 \"integer nz\" 3 \"point3 p0\" [-1 -1 -1] " +
                                         "\"point3 p1\" [1 1 1]");
        for (int i = 0; i < 100; ++i)
        {
            Point3f o = new(-3 + rng.Uniform<Float>(), -3 + rng.Uniform<Float>(), -3 + rng.Uniform<Float>());
            Point3f target = new(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f);
            Ray ray = new(o, Normalize(target - o));
            RayMajorantIterator iter = m.SampleRay(ray, 10, lambda);
            // The segments are contiguous, and each one's majorant bounds the
            // density at its midpoint.
            Float? prevEnd = null;
            int n = 0;
            while (iter.Next() is RayMajorantSegment seg)
            {
                if (prevEnd is Float pe)
                    Assert.Equal(pe, seg.tMin, 1e-5f);
                Assert.True(seg.tMax >= seg.tMin);
                MediumProperties mp = m.SamplePoint(ray.At((seg.tMin + seg.tMax) / 2), lambda);
                Assert.True(mp.sigma_a[0] + mp.sigma_s[0] <= seg.sigma_maj[0] + 1e-5f);
                prevEnd = seg.tMax;
                ++n;
            }
            Assert.True(n >= 1);
        }

        // A ray that misses the bounds has no segments.
        Assert.Null(m.SampleRay(new Ray(new Point3f(5, 5, 5), new Vector3f(1, 0, 0)), 10, lambda).Next());
    }

    [Theory]
    [InlineData("homogeneous", "\"rgb sigma_a\" [0.2 0.2 0.2] \"rgb sigma_s\" [0.5 0.5 0.5]")]
    [InlineData("uniformgrid", "\"float density\" [0.1 0.9 0.4 0.3 0.7 0.2 1 0.5] \"integer nx\" 2 " +
                               "\"integer ny\" 2 \"integer nz\" 2 \"point3 p0\" [-1 -1 -1] \"point3 p1\" [1 1 1]")]
    [InlineData("rgbgrid", "\"rgb sigma_a\" [0.1 0.2 0.3 0.5 0.5 0.5] \"rgb sigma_s\" [1 0.5 0.2 0.1 0.1 0.1] " +
                           "\"integer nx\" 2 \"point3 p0\" [-1 -1 -1] \"point3 p1\" [1 1 1]")]
    [InlineData("cloud", "\"float density\" 2 \"point3 p0\" [-1 -1 -1] \"point3 p1\" [1 1 1]")]
    public void RatioTrackingTransmittance(string type, string parameters)
    {
        Transform renderFromMedium = Translate(new Vector3f(0.1f, 0.2f, 0.3f)) * RotateY(30);
        Medium m = MakeMedium(type, parameters, renderFromMedium);
        Point3f o = new(-2, 0.2f, 0.4f), p1 = new(2, 0.4f, 0.1f);
        Ray ray = new(o, p1 - o, 0, m);

        // Numerically integrate sigma_t along the ray for the reference. The
        // media are only sampled inside their bounds (the homogeneous one is
        // everywhere).
        const int nSteps = 20000;
        double tau = 0;
        Bounds3f bounds = new(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
        for (int i = 0; i < nSteps; ++i)
        {
            Float t = (i + 0.5f) / nSteps;
            if (type != "homogeneous" && !Inside(renderFromMedium.ApplyInverse(ray.At(t)), bounds))
                continue;
            MediumProperties mp = m.SamplePoint(ray.At(t), lambda);
            tau += (mp.sigma_a[0] + mp.sigma_s[0]) * Length(p1 - o) / nSteps;
        }
        Float expected = (Float)Math.Exp(-tau);
        Assert.True(expected < 0.95f);
        Assert.Equal(expected, RatioTrackingTr(ray, 1, 20000), 0.01f);
    }

    [Fact]
    public void GridEmission()
    {
        Medium m = MakeMedium("uniformgrid", "\"float density\" [1 1] \"integer nx\" 2 \"rgb Le\" [1 1 1] " +
                                         "\"float Lescale\" [0 2]");
        Assert.False(m.SamplePoint(new Point3f(0.1f, 0.5f, 0.5f), lambda).Le.IsNonZero());
        Assert.True(m.SamplePoint(new Point3f(0.9f, 0.5f, 0.5f), lambda).Le.IsNonZero());
    }
}

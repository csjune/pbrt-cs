// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the port of src/pbrt/util/scattering.h (pbrt tests these
// functions through the BSDFs, which aren't ported yet).

namespace Pbrt.Tests;

public class ScatteringTests
{
    [Fact]
    public void ReflectRefract()
    {
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
            Vector3f n = new(0, 0, 1);
            Vector3f wr = Reflect(wo, n);
            Assert.True(FMath.Abs(Dot(wr, n) - Dot(wo, n)) < 1e-5f);
            Assert.True(Length(wr + wo - 2 * Dot(wo, n) * n) < 1e-5f);

            Float eta = 1.5f;
            if (Refract(wo, new Normal3f(n), eta, out Float etap, out Vector3f wt))
            {
                // Snell's law: eta_i sin(theta_i) = eta_t sin(theta_t)
                Float sinI = FMath.Sqrt(Max<Float>(0, 1 - Sqr(wo.z))), sinT = FMath.Sqrt(Max<Float>(0, 1 - Sqr(wt.z)));
                Assert.True(FMath.Abs(sinI - etap * sinT) < 1e-4f, $"{wo} {wt} {etap}");
                Assert.Equal(wo.z > 0 ? eta : 1 / eta, etap);
                Assert.True(wt.z * wo.z < 0);
                Assert.True(FMath.Abs(Length(wt) - 1) < 1e-5f);
            }
            else
                // Total internal reflection only happens going out.
                Assert.True(wo.z < 0 && Sqr(eta) * (1 - Sqr(wo.z)) >= 1);
        }
    }

    [Fact]
    public void Fresnel()
    {
        // Normal incidence: ((eta - 1) / (eta + 1))^2.
        Assert.Equal(Sqr(.5f / 2.5f), FrDielectric(1, 1.5f), 1e-6f);
        Assert.Equal(Sqr(.5f / 2.5f), FrDielectric(-1, 1.5f), 1e-6f);
        // Grazing: 1; total internal reflection: 1.
        Assert.Equal(1, FrDielectric(0, 1.5f), 1e-5f);
        Assert.Equal(1, FrDielectric(-0.2f, 1.5f));
        // No interface: 0.
        Assert.Equal(0, FrDielectric(.7f, 1));

        // FrComplex with k = 0 matches FrDielectric for incident light outside.
        for (Float c = 0.05f; c <= 1; c += .05f)
            Assert.Equal(FrDielectric(c, 1.33f), FrComplex(c, new Complex(1.33f, 0)), 1e-5f);
        // Conductors reflect more.
        Assert.True(FrComplex(1, new Complex(.2f, 3)) > .8f);

        SampledSpectrum r = FrComplex(.5f, new SampledSpectrum(1.33f), new SampledSpectrum(0));
        Assert.Equal(FrDielectric(.5f, 1.33f), r[2], 1e-5f);
    }

    [Fact]
    public void ComplexOps()
    {
        Complex a = new(3, 4), b = new(1, -2);
        Assert.Equal(new Complex(4, 2), a + b);
        Assert.Equal(new Complex(11, -2), a * b);
        Complex q = a / b;
        Assert.Equal(-1, q.re, 1e-6f);
        Assert.Equal(2, q.im, 1e-6f);
        Assert.Equal(25, Complex.Norm(a));
        Assert.Equal(5, Complex.Abs(a));
        Complex s = Complex.Sqrt(new Complex(-4, 0));
        Assert.Equal(new Complex(0, 2), s);
        Complex t = Complex.Sqrt(a);
        Complex t2 = t * t;
        Assert.True(FMath.Abs(t2.re - 3) < 1e-5f && FMath.Abs(t2.im - 4) < 1e-5f);
        Assert.Equal(new Complex(0), Complex.Sqrt(0));
    }

    [Fact]
    public void FresnelMoments()
    {
        // The polynomial fits give values in [0, 1] (to within the fit
        // error: FresnelMoment1(1.01) is -3e-4).
        foreach (Float eta in (Float[])[0.7f, 0.99f, 1.01f, 1.33f, 1.5f])
        {
            Float m1 = FresnelMoment1(eta), m2 = FresnelMoment2(eta);
            Assert.True(m1 >= -1e-3f && m1 <= 1, $"{eta}: {m1}");
            Assert.True(m2 >= -1e-3f && m2 <= 1, $"{eta}: {m2}");
        }
    }

    [Fact]
    public void TrowbridgeReitz()
    {
        RNG rng = new();
        foreach ((Float ax, Float ay) in (ReadOnlySpan<(Float, Float)>)[(.3f, .3f), (.1f, .5f), (.8f, .2f)])
        {
            TrowbridgeReitzDistribution distrib = new(ax, ay);
            Assert.False(distrib.EffectivelySmooth());

            // The projected microfacet area integrates to one: int D(wm) cos(theta_m) dwm = 1.
            int n = 200000;
            double sum = 0;
            for (int i = 0; i < n; ++i)
            {
                Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Vector3f wm = SampleUniformHemisphere(u);
                sum += distrib.D(wm) * CosTheta(wm) / UniformHemispherePDF();
            }
            Assert.True(Math.Abs(sum / n - 1) < .03, $"{ax} {ay}: {sum / n}");

            // Sample_wm() samples visible normals: its PDF integrates to
            // one, and matches PDF().
            Vector3f wo = Normalize(new Vector3f(.3f, -.2f, .7f));
            double pdfSum = 0;
            for (int i = 0; i < n; ++i)
            {
                Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Vector3f wm = SampleUniformHemisphere(u);
                pdfSum += distrib.PDF(wo, wm) / UniformHemispherePDF();
            }
            Assert.True(Math.Abs(pdfSum / n - 1) < .03, $"{ax} {ay}: pdf integral {pdfSum / n}");

            for (int i = 0; i < 100; ++i)
            {
                Vector3f wm = distrib.Sample_wm(wo, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Assert.True(wm.z > 0 && FMath.Abs(Length(wm) - 1) < 1e-4f);
                Assert.True(Dot(wo, wm) >= -1e-4f);
            }
        }

        TrowbridgeReitzDistribution smooth = new(0, 0);
        Assert.True(smooth.EffectivelySmooth());
        Assert.Equal(FMath.Sqrt((Float).25f), TrowbridgeReitzDistribution.RoughnessToAlpha(.25f));
        TrowbridgeReitzDistribution reg = new(.01f, .5f);
        reg.Regularize();
        // (Regularize() clamps to 0.1f, which is 0.10000000149011612 as a double.)
        Assert.Equal($"[ TrowbridgeReitzDistribution alpha_x: {FloatToString((Float)0.1f)} alpha_y: 0.5 ]", reg.ToString());
    }
}

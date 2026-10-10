// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/spectrum_test.cpp

namespace Pbrt.Tests;

public class SpectrumTests
{
    [Fact]
    public void Blackbody()
    {
        // Relative error.
        static Float err(Float val, Float @ref) => FMath.Abs(val - @ref) / @ref;

        // Planck's law.
        // A few values via
        // http://www.spectralcalc.com/blackbody_calculator/blackbody.php
        // lambda, T, expected radiance
        Float[,] v = {
            {483, 6000, 3.1849e13f},
            {600, 6000, 2.86772e13f},
            {500, 3700, 1.59845e12f},
            {600, 4500, 7.46497e12f},
        };
        int n = v.GetLength(0);
        for (int i = 0; i < n; ++i)
        {
            Float lambda = v[i, 0], T = v[i, 1], LeExpected = v[i, 2];
            Assert.True(err(Util.Blackbody(lambda, T), LeExpected) < .001);
        }

        // Use Wien's displacement law to compute maximum wavelength for a few
        // temperatures, then confirm that the value returned by Blackbody() is
        // consistent with this.
        foreach (Float T in (Float[])[2700, 3000, 4500, 5600, 6000])
        {
            Float lambdaMax = (Float)(2.8977721e-3 / T * 1e9);
            Float[] lambda = [(Float)(.99 * lambdaMax), lambdaMax, (Float)(1.01 * lambdaMax)];
            Assert.True(Util.Blackbody(lambda[0], T) < Util.Blackbody(lambda[1], T));
            Assert.True(Util.Blackbody(lambda[1], T) > Util.Blackbody(lambda[2], T));
        }
    }

    [Fact]
    public void XYZ()
    {
        {
            // Make sure the integral of all matching function sample values is
            // basically one in x, y, and z.
            Float xx = 0, yy = 0, zz = 0;
            for (Float lambda = 360; lambda < 831; ++lambda)
            {
                xx += Spectra.X().Evaluate(lambda);
                yy += Spectra.Y().Evaluate(lambda);
                zz += Spectra.Z().Evaluate(lambda);
            }
            const Float CIE_Y_integral = 106.856895f;
            xx /= CIE_Y_integral;
            yy /= CIE_Y_integral;
            zz /= CIE_Y_integral;
            Assert.True(FMath.Abs(1 - xx) < .005, $"{xx}");
            Assert.True(FMath.Abs(1 - yy) < .005, $"{yy}");
            Assert.True(FMath.Abs(1 - zz) < .005, $"{zz}");
        }
        {
            // Make sure the xyz of a constant spectrum are basically one.
            Float[] xyzSum = [0, 0, 0];
            int n = 100;
            foreach (Float u in Stratified1D(n))
            {
                SampledWavelengths lambda = SampledWavelengths.SampleUniform(u, 360, 830);
                XYZ xyz = new SampledSpectrum(1f).ToXYZ(lambda);
                for (int c = 0; c < 3; ++c)
                    xyzSum[c] += xyz[c];
            }
            for (int c = 0; c < 3; ++c)
                xyzSum[c] /= n;

            Assert.True(FMath.Abs(1 - xyzSum[0]) < .035, $"{xyzSum[0]}");
            Assert.True(FMath.Abs(1 - xyzSum[1]) < .035, $"{xyzSum[1]}");
            Assert.True(FMath.Abs(1 - xyzSum[2]) < .035, $"{xyzSum[2]}");
        }
    }

    [Fact]
    public void MaxValue()
    {
        Assert.Equal(2.5f, new ConstantSpectrum(2.5f).MaxValue());

        Assert.Equal((Float)10.1,
                     new PiecewiseLinearSpectrum(
                         [(Float)300, (Float)380, (Float)510, (Float)662, (Float)700],
                         [(Float)1.5, (Float)2.6, (Float)10.1, (Float)5.3, (Float)7.7])
                         .MaxValue());

        Assert.True(new BlackbodySpectrum(5000).MaxValue() > .9999);
        Assert.True(new BlackbodySpectrum(5000).MaxValue() < 1.0001);

        BlackbodySpectrum bb = new(5000);
        Assert.True(new DenselySampledSpectrum(bb).MaxValue() > .9999);
        Assert.True(new DenselySampledSpectrum(bb).MaxValue() < 1.0001);

        RNG rng = new();
        for (int i = 0; i < 20; ++i)
        {
            RGB rgb = new(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
            RGBAlbedoSpectrum sr = new(RGBColorSpace.sRGB, rgb);
            Float m = sr.MaxValue() * 1.00001f;
            for (Float lambda = 360; lambda < 830; lambda += .92f)
                Assert.True(sr.Evaluate(lambda) <= m);

            RGBUnboundedSpectrum su = new(RGBColorSpace.sRGB, 10 * rgb);
            m = su.MaxValue() * 1.00001f;
            for (Float lambda = 360; lambda < 830; lambda += .92f)
                Assert.True(su.Evaluate(lambda) <= m);

            RGBIlluminantSpectrum si = new(RGBColorSpace.sRGB, rgb);
            m = si.MaxValue() * 1.00001f;
            for (Float lambda = 360; lambda < 830; lambda += .92f)
                Assert.True(si.Evaluate(lambda) <= m);
        }
    }

    [Fact]
    public void SamplingPdfY()
    {
        // Make sure we can integrate the y matching curve correctly
        Float ysum = 0;
        int n = 1000;
        foreach (Float u in Stratified1D(n))
        {
            Float lambda = SampleVisibleWavelengths(u);
            Float pdf = VisibleWavelengthsPDF(lambda);
            if (pdf > 0)
                ysum += Spectra.Y().Evaluate(lambda) / pdf;
        }
        Float yint = ysum / n;

        Assert.True(FMath.Abs((yint - CIE_Y_integral) / CIE_Y_integral) < 1e-3,
                    $"{yint} vs. {CIE_Y_integral}");
    }

    [Fact]
    public void SamplingPdfXYZ()
    {
        // Make sure we can integrate the sum of the x+y+z matching curves correctly
        Float impSum = 0, unifSum = 0;
        int n = 10000;
        foreach (Float u in Stratified1D(n))
        {
            {
                // Uniform
                Float lambda = Lerp(u, Lambda_min, Lambda_max);
                Float pdf = 1f / (Lambda_max - Lambda_min);
                unifSum +=
                    (Spectra.X().Evaluate(lambda) + Spectra.Y().Evaluate(lambda) + Spectra.Z().Evaluate(lambda)) /
                    pdf;
            }

            {
                Float lambda = SampleVisibleWavelengths(u);
                Float pdf = VisibleWavelengthsPDF(lambda);
                if (pdf > 0)
                    impSum +=
                        (Spectra.X().Evaluate(lambda) + Spectra.Y().Evaluate(lambda) + Spectra.Z().Evaluate(lambda)) /
                        pdf;
            }
        }
        Float impInt = impSum / n, unifInt = unifSum / n;

        Assert.True(FMath.Abs((impInt - unifInt) / unifInt) < 1e-3, $"{impInt} vs. {unifInt}");
    }

    // Not in spectrum_test.cpp: checks for the port.

    [Fact]
    public void NamedSpectra()
    {
        Assert.NotNull(GetNamedSpectrum("stdillum-D65"));
        Assert.NotNull(GetNamedSpectrum("metal-Cu-eta"));
        Assert.NotNull(GetNamedSpectrum("sony_ilce_9_b"));
        Assert.Null(GetNamedSpectrum("no-such-spectrum"));

        // Normalized illuminants have unit luminance.
        Spectrum d65 = GetNamedSpectrum("stdillum-D65")!;
        Assert.True(FMath.Abs(SpectrumToXYZ(d65).Y - 1) < 1e-3, $"{SpectrumToXYZ(d65)}");

        Assert.Equal("metal-Au-k", FindMatchingNamedSpectrum(GetNamedSpectrum("metal-Au-k")!));
        Assert.Equal("", FindMatchingNamedSpectrum(new ConstantSpectrum(1)));
        Assert.Equal("\"glass-BK7\"", GetNamedSpectrum("glass-BK7")!.ToString());
    }

    [Fact]
    public void DIlluminant()
    {
        // The CIE D spectrum for 6504K is D65.
        DenselySampledSpectrum d = Spectra.D(6504);
        Spectrum d65 = GetNamedSpectrum("stdillum-D65")!;
        Float scale = d65.Evaluate(560) / d.Evaluate(560);
        for (Float lambda = 400; lambda <= 700; lambda += 10)
            Assert.True(FMath.Abs(scale * d.Evaluate(lambda) - d65.Evaluate(lambda)) < .02f * d65.Evaluate(lambda),
                        $"lambda {lambda}: {scale * d.Evaluate(lambda)} vs {d65.Evaluate(lambda)}");

        // Below 4000K, D() falls back to a (normalized) blackbody.
        DenselySampledSpectrum bb = Spectra.D(3000);
        BlackbodySpectrum bbRef = new((Float)3000 * 1.4388f / 1.4380f);
        for (int lambda = 360; lambda <= 830; lambda += 10)
            Assert.Equal(bbRef.Evaluate(lambda), bb.Evaluate(lambda));
    }

    [Fact]
    public void SampledSpectrumOps()
    {
        SampledSpectrum a = new([1, 2, 3, 4]), b = new(2);
        Assert.Equal(new SampledSpectrum([3, 4, 5, 6]), a + b);
        Assert.Equal(new SampledSpectrum([.5f, 1, 1.5f, 2]), a / b);
        Assert.Equal(new SampledSpectrum([0, -1, -2, -3]), 1 - a);
        Assert.Equal(2.5f, a.Average());
        Assert.Equal(1, a.MinComponentValue());
        Assert.Equal(4, a.MaxComponentValue());

        Assert.True((bool)a);
        Assert.False((bool)new SampledSpectrum(0));
        bool taken = false;
        if (new SampledSpectrum([0, 0, 0, 1e-20f]))
            taken = true;
        Assert.True(taken);

        Assert.Equal(new SampledSpectrum([1, 0, 1.5f, 0]),
                     SafeDiv(new SampledSpectrum([2, 1, 3, 0]), new SampledSpectrum([2, 0, 2, 0])));

        SampledWavelengths swl = SampledWavelengths.SampleUniform(0.5f);
        Assert.False(swl.SecondaryTerminated());
        swl.TerminateSecondary();
        Assert.True(swl.SecondaryTerminated());
        Assert.Equal(1 / (Lambda_max - Lambda_min) / NSpectrumSamples, swl.PDF()[0]);
        Assert.Equal(0, swl.PDF()[3]);
    }

    [Fact]
    public void DenselySampledEquality()
    {
        BlackbodySpectrum bb = new(5000);
        DenselySampledSpectrum a = new(bb), b = new(bb), c = new(new BlackbodySpectrum(6000));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
        Assert.True(new DenselySampledSpectrum(a) == a);
    }

    [Fact]
    public void ReadSpectrumFile()
    {
        string fn = Path.GetTempFileName();
        try
        {
            File.WriteAllText(fn, "# lambda value\n400 1.5\n500 2.5 # comment\n600 0.5\n");
            Spectrum? s = PiecewiseLinearSpectrum.Read(fn);
            Assert.NotNull(s);
            Assert.Equal(2.5f, s!.MaxValue());
            Assert.Equal(2f, s.Evaluate(450));

            File.WriteAllText(fn, "400 1.5\n300 2.5\n");
            Assert.Null(PiecewiseLinearSpectrum.Read(fn));
        }
        finally
        {
            File.Delete(fn);
        }
    }
}

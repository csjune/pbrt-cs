// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the tabulated BSSRDF (pbrt has no bssrdf tests).

namespace Pbrt.Tests;

public class BssrdfTests
{
    private static readonly BSSRDFTable table = MakeTable();

    private static BSSRDFTable MakeTable()
    {
        BSSRDFTable t = new(100, 64);
        Util.ComputeBeamDiffusionBSSRDF(0, 1.33f, t);
        return t;
    }

    [Fact]
    public void Table_RhoEffMonotonic()
    {
        for (int i = 1; i < table.rhoEff.Length; ++i)
            Assert.True(table.rhoEff[i] >= table.rhoEff[i - 1]);
        Assert.True(table.rhoEff[0] >= 0 && table.rhoEff[0] < 0.01f);
        Assert.True(table.rhoEff[^1] > 0.5f);
    }

    [Fact]
    public void SubsurfaceFromDiffuse_MeanFreePath()
    {
        SampledSpectrum rhoEff = new(0.5f), mfp = new(2);
        Util.SubsurfaceFromDiffuse(table, rhoEff, mfp, out SampledSpectrum sigma_a, out SampledSpectrum sigma_s);
        for (int c = 0; c < Util.NSpectrumSamples; ++c)
        {
            Assert.Equal(0.5f, sigma_a[c] + sigma_s[c], 1e-5f);
            Assert.True(sigma_s[c] > sigma_a[c]);
        }
    }

    [Fact]
    public void SampleSr_PositivePdf()
    {
        SampledSpectrum sigma_a = new(0.2f), sigma_s = new(1.5f);
        TabulatedBSSRDF bssrdf = new(new Point3f(0, 0, 0), new Normal3f(0, 0, 1), new Vector3f(0, 0, 1),
                                     1.33f, sigma_a, sigma_s, table);
        RNG rng = new();
        int nValid = 0;
        for (int i = 0; i < 1000; ++i)
        {
            Float? r = bssrdf.SampleSr(rng.Uniform<Float>());
            if (r is not Float rr)
                continue;
            ++nValid;
            Assert.True(rr >= 0);
            Assert.True(bssrdf.PDF_Sr(rr)[0] > 0);
            Assert.True(bssrdf.Sr(rr)[0] >= 0);
        }
        Assert.True(nValid > 900);
    }
}

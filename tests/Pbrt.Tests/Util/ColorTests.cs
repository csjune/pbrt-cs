// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/color_test.cpp

namespace Pbrt.Tests;

public class RGBColorSpaceTests
{
    [Fact]
    public void RGBXYZ()
    {
        foreach (RGBColorSpace cs in (RGBColorSpace[])[RGBColorSpace.ACES2065_1, RGBColorSpace.Rec2020, RGBColorSpace.sRGB])
        {
            XYZ xyz = cs.ToXYZ(new RGB(1, 1, 1));
            RGB rgb = cs.ToRGB(xyz);
            Assert.True(FMath.Abs(1 - rgb[0]) < 1e-4);
            Assert.True(FMath.Abs(1 - rgb[1]) < 1e-4);
            Assert.True(FMath.Abs(1 - rgb[2]) < 1e-4);
        }
    }

    [Fact]
    public void sRGB()
    {
        RGBColorSpace sRGB = RGBColorSpace.sRGB;

        // Make sure the matrix values are sensible by throwing the x, y, and z
        // basis vectors at it to pull out columns.
        RGB rgb = sRGB.ToRGB(new XYZ(1, 0, 0));
        Assert.True(FMath.Abs(3.2406f - rgb[0]) < 1e-3);
        Assert.True(FMath.Abs(-.9689f - rgb[1]) < 1e-3);
        Assert.True(FMath.Abs(.0557f - rgb[2]) < 1e-3);

        rgb = sRGB.ToRGB(new XYZ(0, 1, 0));
        Assert.True(FMath.Abs(-1.5372f - rgb[0]) < 1e-3);
        Assert.True(FMath.Abs(1.8758f - rgb[1]) < 1e-3);
        Assert.True(FMath.Abs(-.2040f - rgb[2]) < 1e-3);

        rgb = sRGB.ToRGB(new XYZ(0, 0, 1));
        Assert.True(FMath.Abs(-.4986f - rgb[0]) < 1e-3);
        Assert.True(FMath.Abs(.0415f - rgb[1]) < 1e-3);
        Assert.True(FMath.Abs(1.0570f - rgb[2]) < 1e-3);
    }

    private static void CheckIllumWhite(RGBColorSpace cs)
    {
        XYZ xyz = SpectrumToXYZ(cs.illuminant);
        RGB rgb = cs.ToRGB(xyz);
        Assert.True(rgb.r >= .99);
        Assert.True(rgb.r <= 1.01);
        Assert.True(rgb.g >= .99);
        Assert.True(rgb.g <= 1.01);
        Assert.True(rgb.b >= .99);
        Assert.True(rgb.b <= 1.01);
    }

    [Fact]
    public void StdIllumWhitesRGB() => CheckIllumWhite(RGBColorSpace.sRGB);

    [Fact]
    public void StdIllumWhiteRec2020() => CheckIllumWhite(RGBColorSpace.Rec2020);

    [Fact]
    public void StdIllumWhiteACES2065_1() => CheckIllumWhite(RGBColorSpace.ACES2065_1);

    // Not in color_test.cpp: checks for the port.

    [Fact]
    public void NamedAndLookup()
    {
        Assert.Same(RGBColorSpace.sRGB, RGBColorSpace.GetNamed("sRGB"));
        Assert.Same(RGBColorSpace.ACES2065_1, RGBColorSpace.GetNamed("aces2065-1"));
        Assert.Same(RGBColorSpace.DCI_P3, RGBColorSpace.GetNamed("DCI-P3"));
        Assert.Null(RGBColorSpace.GetNamed("adobe"));

        foreach (RGBColorSpace cs in (RGBColorSpace[])[RGBColorSpace.ACES2065_1, RGBColorSpace.DCI_P3,
                                                        RGBColorSpace.Rec2020, RGBColorSpace.sRGB])
            Assert.Same(cs, RGBColorSpace.Lookup(cs.r, cs.g, cs.b, cs.w));

        Assert.True(ConvertRGBColorSpace(RGBColorSpace.sRGB, RGBColorSpace.sRGB).IsIdentity());
        RGB rgb = new(.2f, .5f, .7f);
        RGB p3 = ConvertRGBColorSpace(RGBColorSpace.sRGB, RGBColorSpace.DCI_P3) * rgb;
        RGB back = ConvertRGBColorSpace(RGBColorSpace.DCI_P3, RGBColorSpace.sRGB) * p3;
        Assert.True(FMath.Abs(back.r - rgb.r) < 1e-5 && FMath.Abs(back.g - rgb.g) < 1e-5 &&
                    FMath.Abs(back.b - rgb.b) < 1e-5, $"{rgb} -> {p3} -> {back}");
    }

    [Fact]
    public void WhiteBalanceIdentity()
    {
        Point2f w = RGBColorSpace.sRGB.w;
        SquareMatrix3 m = WhiteBalance(w, w);
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
                Assert.True(FMath.Abs(m[i, j] - (i == j ? 1 : 0)) < 1e-3, $"{m}");
    }
}

public class RGBUnboundedSpectrumTests
{
    [Fact]
    public void MaxValue()
    {
        RNG rng = new();
        foreach (RGBColorSpace cs in (RGBColorSpace[])[RGBColorSpace.sRGB, RGBColorSpace.Rec2020, RGBColorSpace.ACES2065_1])
        {
            for (int i = 0; i < 100; ++i)
            {
                RGB rgb = new(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                rgb *= 10f;
                RGBUnboundedSpectrum rs = new(cs, rgb);

                Float m = rs.MaxValue();
                Float sm = 0;
                for (Float lambda = 360; lambda <= 830; lambda += 1f / 16f)
                    sm = Max(sm, rs.Evaluate(lambda));
                Assert.True(FMath.Abs((sm - m) / sm) < 1e-4, $"sampled {sm} MaxValue {m} for {rs}");
            }
        }
    }
}

public class RGBAlbedoSpectrumTests
{
    [Fact]
    public void MaxValue()
    {
        RNG rng = new();
        foreach (RGBColorSpace cs in (RGBColorSpace[])[RGBColorSpace.sRGB, RGBColorSpace.Rec2020, RGBColorSpace.ACES2065_1])
        {
            for (int i = 0; i < 100; ++i)
            {
                RGB rgb = new(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                RGBAlbedoSpectrum rs = new(cs, rgb);

                Float m = rs.MaxValue();
                Float sm = 0;
                for (Float lambda = 360; lambda <= 830; lambda += 1f / 16f)
                    sm = Max(sm, rs.Evaluate(lambda));
                Assert.True(FMath.Abs((sm - m) / sm) < 1e-4, $"sampled {sm} MaxValue {m} for {rs}");
            }
        }
    }

    // Some error comes from the fact that piecewise linear (at 5nm)
    // CIE curves were used for the optimization while we use piecewise
    // linear at 1nm spacing converted to 1nm constant / densely
    // sampled.
    internal static void CheckRoundTrip(RGBColorSpace cs, RGB rgb, Spectrum rs, bool multiplyIlluminant)
    {
        DenselySampledSpectrum rsIllum = DenselySampledSpectrum.SampleFunction(
            lambda => multiplyIlluminant ? rs.Evaluate(lambda) * cs.illuminant.Evaluate(lambda)
                                         : rs.Evaluate(lambda));
        XYZ xyz = SpectrumToXYZ(rsIllum);
        RGB rgb2 = cs.ToRGB(xyz);

        Float eps = .01f;
        Assert.True(FMath.Abs(rgb.r - rgb2.r) < eps, $"{rgb} vs {rgb2} xyz {xyz}");
        Assert.True(FMath.Abs(rgb.g - rgb2.g) < eps, $"{rgb} vs {rgb2} xyz {xyz}");
        Assert.True(FMath.Abs(rgb.b - rgb2.b) < eps, $"{rgb} vs {rgb2} xyz {xyz}");
    }

    [Fact]
    public void RoundTripsRGB()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.sRGB;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
            CheckRoundTrip(cs, rgb, new RGBAlbedoSpectrum(cs, rgb), true);
        }
    }

    [Fact]
    public void RoundTripRec2020()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.Rec2020;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(.1f + .7f * rng.Uniform<Float>(), .1f + .7f * rng.Uniform<Float>(),
                          .1f + .7f * rng.Uniform<Float>());
            CheckRoundTrip(cs, rgb, new RGBAlbedoSpectrum(cs, rgb), true);
        }
    }

    [Fact]
    public void RoundTripACES()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.ACES2065_1;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(.3f + .4f * rng.Uniform<Float>(), .3f + .4f * rng.Uniform<Float>(),
                          .3f + .4f * rng.Uniform<Float>());
            CheckRoundTrip(cs, rgb, new RGBAlbedoSpectrum(cs, rgb), true);
        }
    }
}

public class RGBIlluminantSpectrumTests
{
    [Fact]
    public void RoundTripsRGB()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.sRGB;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
            RGBAlbedoSpectrumTests.CheckRoundTrip(cs, rgb, new RGBIlluminantSpectrum(cs, rgb), false);
        }
    }

    [Fact]
    public void RoundTripRec2020()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.Rec2020;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(.1f + .7f * rng.Uniform<Float>(), .1f + .7f * rng.Uniform<Float>(),
                          .1f + .7f * rng.Uniform<Float>());
            RGBAlbedoSpectrumTests.CheckRoundTrip(cs, rgb, new RGBIlluminantSpectrum(cs, rgb), false);
        }
    }

    [Fact]
    public void RoundTripACES()
    {
        RNG rng = new();
        RGBColorSpace cs = RGBColorSpace.ACES2065_1;

        for (int i = 0; i < 100; ++i)
        {
            RGB rgb = new(.3f + .4f * rng.Uniform<Float>(), .3f + .4f * rng.Uniform<Float>(),
                          .3f + .4f * rng.Uniform<Float>());
            RGBAlbedoSpectrumTests.CheckRoundTrip(cs, rgb, new RGBIlluminantSpectrum(cs, rgb), false);
        }
    }
}

public class SRGBTests
{
    [Fact]
    public void Conversion()
    {
        // Check the basic 8 bit values
        for (int i = 0; i < 256; ++i)
        {
            Float x = SRGBToLinear(i * (1f / 255f));
            Float y = SRGB8ToLinear((byte)i);
            Assert.True(FMath.Abs(x - y) < 1e-5);
        }

        // Round trip to linear and back
        for (int i = 0; i < 256; ++i)
        {
            Float x = SRGBToLinear(i * (1f / 255f));
            Float y = LinearToSRGB(x) * 255f;
            Assert.True(FMath.Abs(i - y) < 1e-4);
        }

        // Round trip the other way
        for (int i = 0; i < 256; ++i)
        {
            Float x = LinearToSRGB(i * (1f / 255f));
            Float y = SRGBToLinear((float)x) * 255f;
            Assert.True(FMath.Abs(i - y) < 3e-4, $"i = {i} -> linear {x} -> srgb {y}");
        }
    }

    // Not in color_test.cpp: checks for the port.

    [Fact]
    public void ColorEncodings()
    {
        byte[] bytes = new byte[256];
        for (int i = 0; i < 256; ++i)
            bytes[i] = (byte)i;
        Float[] linear = new Float[256];
        byte[] back = new byte[256];

        foreach (ColorEncoding enc in (ColorEncoding[])[ColorEncoding.Linear, ColorEncoding.sRGB,
                                                         ColorEncoding.Get("gamma 2.2")])
        {
            enc.ToLinear(bytes, linear);
            enc.FromLinear(linear, back);
            if (enc is GammaColorEncoding)
            {
                // pbrt's GammaColorEncoding goes back through a 1024-entry
                // table, which loses the darkest values and is off by one
                // elsewhere.
                for (int i = 64; i < 256; ++i)
                    Assert.True(Math.Abs(bytes[i] - back[i]) <= 1, $"{i} -> {back[i]}");
            }
            else
                Assert.Equal(bytes, back);
            for (int i = 0; i < 256; i += 15)
                Assert.True(FMath.Abs(enc.ToFloatLinear(i / 255f) - linear[i]) < 1e-4, $"{enc} {i}");
        }

        Assert.Same(ColorEncoding.Get("gamma 2.2"), ColorEncoding.Get("gamma 2.2"));
        Assert.Same(ColorEncoding.sRGB, ColorEncoding.Get("sRGB"));
        Assert.Throws<FatalErrorException>(() => ColorEncoding.Get("gamma"));
    }
}

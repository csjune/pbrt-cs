// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the port of src/pbrt/util/mipmap.h (pbrt has no mipmap tests).

namespace Pbrt.Tests;

// (In the Options collection for the DisableImageTextures test.)
[Collection("Options")]
public class MIPMapTests
{
    [Fact]
    public void DisableImageTextures()
    {
        Assert.Equal(5, Make(Ramp(16, 16), FilterFunction.Trilinear).Levels());
        Options.disableImageTextures = true;
        try
        {
            // Only the top level (the average) is kept.
            MIPMap m = Make(Ramp(16, 16), FilterFunction.Trilinear);
            Assert.Equal(1, m.Levels());
            Assert.Equal(new Point2i(1, 1), m.LevelResolution(0));
            RGB rgb = m.Filter<RGB>(new Point2f(0.1f, 0.9f), new Vector2f(0, 0), new Vector2f(0, 0));
            Assert.Equal(0.5f, rgb.r, 1e-5f);
            Assert.Equal(0.5f, rgb.g, 1e-5f);
        }
        finally
        {
            Options.disableImageTextures = false;
        }
    }

    private static readonly FilterFunction[] AllFilters =
        [FilterFunction.Point, FilterFunction.Bilinear, FilterFunction.Trilinear, FilterFunction.EWA];

    private static MIPMap Make(Image image, FilterFunction filter, WrapMode wrap = WrapMode.Repeat) =>
        new(image, RGBColorSpace.sRGB, wrap, new MIPMapFilterOptions { filter = filter });

    private static Image Ramp(int w, int h)
    {
        // RGB = (s, t, 0.25)
        Image image = new(PixelFormat.Float, new Point2i(w, h), ["R", "G", "B"]);
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
                image.SetChannels(new Point2i(x, y), [(x + .5f) / w, (y + .5f) / h, .25f]);
        return image;
    }

    [Fact]
    public void ConstantImage()
    {
        Image image = new(PixelFormat.Float, new Point2i(13, 6), ["R", "G", "B"]);
        for (int y = 0; y < 6; ++y)
            for (int x = 0; x < 13; ++x)
                image.SetChannels(new Point2i(x, y), [.2f, .4f, .6f]);
        RNG rng = new();
        foreach (FilterFunction f in AllFilters)
        {
            MIPMap mip = Make(image, f);
            for (int i = 0; i < 200; ++i)
            {
                Point2f st = new(rng.Uniform<Float>(), rng.Uniform<Float>());
                Float scale = FMath.Pow(10, -4 + 4 * rng.Uniform<Float>());
                Vector2f d0 = new((rng.Uniform<Float>() - .5f) * scale, (rng.Uniform<Float>() - .5f) * scale);
                Vector2f d1 = new((rng.Uniform<Float>() - .5f) * scale, (rng.Uniform<Float>() - .5f) * scale);
                RGB rgb = mip.Filter<RGB>(st, d0, d1);
                Assert.True(FMath.Abs(rgb.r - .2f) < 1e-4 && FMath.Abs(rgb.g - .4f) < 1e-4 &&
                            FMath.Abs(rgb.b - .6f) < 1e-4, $"{f} {st} {d0} {d1}: {rgb}");
                // Float lookups of RGB images return the average when they
                // bilerp, but pbrt's Float Texel() (used by point sampling,
                // EWA and very wide footprints) returns the first channel.
                Float v = mip.Filter<Float>(st, d0, d1);
                Assert.True(FMath.Abs(v - .4f) < 1e-4 || FMath.Abs(v - .2f) < 1e-4, $"{f}: {v}");
                if (f == FilterFunction.Point || f == FilterFunction.EWA)
                    Assert.True(FMath.Abs(v - .2f) < 1e-4, $"{f}: {v}");
            }
        }
    }

    [Fact]
    public void Pyramid()
    {
        MIPMap mip = Make(Ramp(16, 4), FilterFunction.Bilinear);
        Assert.Equal(5, mip.Levels());
        Assert.Equal(new Point2i(16, 4), mip.LevelResolution(0));
        Assert.Equal(new Point2i(1, 1), mip.LevelResolution(4));
        Assert.Same(RGBColorSpace.sRGB, mip.GetRGBColorSpace());
    }

    [Fact]
    public void SmallFootprints()
    {
        // With tiny derivatives, lookups come from level 0.
        Image image = Ramp(32, 16);
        Vector2f d0 = new(1e-6f, 0), d1 = new(0, 1e-6f);
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Point2f st = new(.1f + .8f * rng.Uniform<Float>(), .1f + .8f * rng.Uniform<Float>());

            RGB point = Make(image, FilterFunction.Point).Filter<RGB>(st, d0, d1);
            Point2i pi = new((int)FMath.Round(st.x * 32 - .5f, MidpointRounding.AwayFromZero),
                             (int)FMath.Round(st.y * 16 - .5f, MidpointRounding.AwayFromZero));
            Assert.Equal(image.GetChannel(pi, 0), point.r);
            Assert.Equal(image.GetChannel(pi, 1), point.g);

            foreach (FilterFunction f in (FilterFunction[])[FilterFunction.Bilinear, FilterFunction.Trilinear])
            {
                RGB b = Make(image, f).Filter<RGB>(st, d0, d1);
                Assert.Equal(image.BilerpChannel(st, 0, WrapMode.Repeat), b.r);
                Assert.Equal(image.BilerpChannel(st, 1, WrapMode.Repeat), b.g);
            }

            // EWA with a tiny footprint filters over about a texel at level
            // 0, so on a linear ramp it's close to the ramp value (within a
            // fraction of a texel: the set of texels inside the ellipse isn't
            // symmetric about _st_).
            RGB e = Make(image, FilterFunction.EWA).Filter<RGB>(st, d0, d1);
            Assert.True(FMath.Abs(e.r - st.x) < .25f / 32 && FMath.Abs(e.g - st.y) < .25f / 16, $"{st}: {e}");
        }
    }

    [Fact]
    public void WideFootprints()
    {
        // A footprint covering the whole texture returns the top level, the
        // image average.
        Image image = Ramp(8, 8);
        foreach (FilterFunction f in AllFilters)
        {
            RGB v = Make(image, f).Filter<RGB>(new Point2f(.3f, .7f), new Vector2f(2, 0), new Vector2f(0, 2));
            Assert.True(FMath.Abs(v.r - .5f) < 1e-5 && FMath.Abs(v.g - .5f) < 1e-5, $"{f}: {v}");
        }
    }

    [Fact]
    public void EWAAnisotropic()
    {
        // A long, thin footprint along s averages over s but not t.
        Image image = Ramp(64, 64);
        MIPMap mip = Make(image, FilterFunction.EWA, WrapMode.Clamp);
        RGB v = mip.Filter<RGB>(new Point2f(.5f, .3f), new Vector2f(.2f, 0), new Vector2f(0, .01f));
        Assert.True(FMath.Abs(v.r - .5f) < .02f, $"{v}");
        Assert.True(FMath.Abs(v.g - .3f) < .02f, $"{v}");

        // maxAnisotropy limits how thin the ellipse can get.
        MIPMap iso = new(image, RGBColorSpace.sRGB, WrapMode.Clamp,
                         new MIPMapFilterOptions { filter = FilterFunction.EWA, maxAnisotropy = 1 });
        RGB w = iso.Filter<RGB>(new Point2f(.5f, .3f), new Vector2f(.2f, 0), new Vector2f(0, .01f));
        Assert.True(FMath.Abs(w.r - .5f) < .02f, $"{w}");
    }

    [Fact]
    public void TrilinearBetweenLevels()
    {
        Image image = new(PixelFormat.Float, new Point2i(4, 1), ["Y"]);
        for (int x = 0; x < 4; ++x)
            image.SetChannel(new Point2i(x, 0), 0, x * x);
        MIPMap mip = Make(image, FilterFunction.Trilinear, WrapMode.Clamp);
        Assert.Equal(3, mip.Levels());
        Point2f st = new(.3f, .5f);
        Float bilerp(int level) => mip.GetLevel(level).BilerpChannel(st, 0, WrapMode.Clamp);

        // width = 2 * max |d| = 2^(level - (nLevels - 1)).
        Vector2f dAt(Float level) => new(FMath.Pow(2, level - 2) / 2, 0);

        // Halfway between levels 1 and 2.
        Assert.Equal(Lerp(.5f, bilerp(1), bilerp(2)), mip.Filter<Float>(st, dAt(1.5f), dAt(1.5f)), 1e-5f);
        // pbrt doesn't interpolate below level 1.
        Assert.Equal(bilerp(0), mip.Filter<Float>(st, dAt(.5f), dAt(.5f)));
    }

    [Fact]
    public void ChannelHandling()
    {
        // Float lookups of 4-channel images return alpha; RGB lookups of
        // 1-channel images replicate it.
        Image rgba = new(PixelFormat.Float, new Point2i(4, 4), ["R", "G", "B", "A"]);
        for (int y = 0; y < 4; ++y)
            for (int x = 0; x < 4; ++x)
                rgba.SetChannels(new Point2i(x, y), [1, 0, 0, .75f]);
        Vector2f d = new(1e-4f, 0);
        Assert.Equal(.75f, Make(rgba, FilterFunction.Bilinear).Filter<Float>(new Point2f(.5f, .5f), d, d));

        Image y1 = new(PixelFormat.Float, new Point2i(4, 4), ["Y"]);
        for (int y = 0; y < 4; ++y)
            for (int x = 0; x < 4; ++x)
                y1.SetChannel(new Point2i(x, y), 0, .3f);
        RGB rgb = Make(y1, FilterFunction.EWA).Filter<RGB>(new Point2f(.5f, .5f), d, d);
        Assert.Equal(new RGB(.3f, .3f, .3f), rgb);

        Assert.Throws<NotSupportedException>(() => Make(y1, FilterFunction.EWA).Filter<int>(new Point2f(.5f, .5f), d, d));
    }

    [Fact]
    public void CreateFromFile()
    {
        string dir = ImageTests.TestDir;
        Image rgba = new(PixelFormat.Float, new Point2i(4, 2), ["R", "G", "B", "A"]);
        for (int y = 0; y < 2; ++y)
            for (int x = 0; x < 4; ++x)
                rgba.SetChannels(new Point2i(x, y), [x / 4f, y / 2f, .5f, 1]);

        // All-one alpha is dropped...
        string fn = Path.Combine(dir, "mip-opaque.exr");
        Assert.True(rgba.Write(fn));
        MIPMap mip = MIPMap.CreateFromFile(fn, new MIPMapFilterOptions(), WrapMode.Repeat, null);
        Assert.Equal(3, mip.GetLevel(0).NChannels());
        Assert.Equal(["R", "G", "B"], mip.GetLevel(0).ChannelNames());
        Assert.True(RemoveFile(fn));

        // ...otherwise it's kept, in RGBA order.
        rgba.SetChannel(new Point2i(1, 1), 3, .5f);
        fn = Path.Combine(dir, "mip-alpha.exr");
        Assert.True(rgba.Write(fn));
        mip = MIPMap.CreateFromFile(fn, new MIPMapFilterOptions(), WrapMode.Repeat, null);
        Assert.Equal(["R", "G", "B", "A"], mip.GetLevel(0).ChannelNames());
        Assert.True(RemoveFile(fn));

        // Images need R, G and B.
        Image xy = new(PixelFormat.Float, new Point2i(2, 2), ["X", "Y"]);
        fn = Path.Combine(dir, "mip-xy.exr");
        Assert.True(xy.Write(fn));
        Assert.Throws<FatalErrorException>(() =>
            MIPMap.CreateFromFile(fn, new MIPMapFilterOptions(), WrapMode.Repeat, null));
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ParseAndOptions()
    {
        Assert.Equal(FilterFunction.EWA, ParseFilter("ewa"));
        Assert.Equal(FilterFunction.EWA, ParseFilter("EWA"));
        Assert.Equal(FilterFunction.Trilinear, ParseFilter("trilinear"));
        Assert.Equal(FilterFunction.Bilinear, ParseFilter("bilinear"));
        Assert.Equal(FilterFunction.Point, ParseFilter("point"));
        Assert.Null(ParseFilter("gaussian"));

        MIPMapFilterOptions defaults = new();
        Assert.Equal(FilterFunction.EWA, defaults.filter);
        Assert.Equal(8f, defaults.maxAnisotropy);
        Assert.True(new MIPMapFilterOptions { filter = FilterFunction.Point } < defaults);
        Assert.True(new MIPMapFilterOptions { maxAnisotropy = 4 } < defaults);
        Assert.Equal(new MIPMapFilterOptions(), defaults);
    }
}

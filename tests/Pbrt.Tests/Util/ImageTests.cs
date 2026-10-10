// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/image_test.cpp
//
// The tests write their files to a temporary directory rather than the
// current one.

namespace Pbrt.Tests;

public class ImageTests
{
    // (With forward slashes, since tests put these paths in scene
    // descriptions, where backslashes start escape sequences.)
    internal static readonly string TestDir =
        Directory.CreateTempSubdirectory("pbrt-image-test").FullName.Replace('\\', '/');

    internal static string InTestDir(string path) => TestDir + "/" + path;

    [Fact]
    public void Basics()
    {
        ColorEncoding encoding = ColorEncoding.Linear;
        Image y8 = new(PixelFormat.U256, new Point2i(4, 8), ["Y"], encoding);
        Assert.Equal(1, y8.NChannels());
        Assert.Equal(y8.Resolution()[0] * y8.Resolution()[1], y8.BytesUsed());

        Image y16 = new(PixelFormat.Half, new Point2i(4, 8), ["Y"]);
        Assert.Equal(1, y16.NChannels());
        Assert.Equal(2 * y16.Resolution()[0] * y16.Resolution()[1], y16.BytesUsed());

        Image y32 = new(PixelFormat.Float, new Point2i(4, 8), ["Y"]);
        Assert.Equal(1, y32.NChannels());
        Assert.Equal(4 * y32.Resolution()[0] * y32.Resolution()[1], y32.BytesUsed());

        Image rgb8 = new(PixelFormat.U256, new Point2i(4, 8), ["R", "G", "B"], encoding);
        Assert.Equal(3, rgb8.NChannels());
        Assert.Equal(3 * rgb8.Resolution()[0] * rgb8.Resolution()[1], rgb8.BytesUsed());

        Image rgb16 = new(PixelFormat.Half, new Point2i(4, 16), ["R", "G", "B"]);
        Assert.Equal(3, rgb16.NChannels());
        Assert.Equal(2 * 3 * rgb16.Resolution()[0] * rgb16.Resolution()[1], rgb16.BytesUsed());

        Image rgb32 = new(PixelFormat.Float, new Point2i(4, 32), ["R", "G", "B"]);
        Assert.Equal(3, rgb32.NChannels());
        Assert.Equal(4 * 3 * rgb32.Resolution()[0] * rgb32.Resolution()[1], rgb32.BytesUsed());
    }

    internal static float[] GetFloatPixels(Point2i res, int nc)
    {
        List<float> p = [];
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                for (int c = 0; c < nc; ++c)
                    if (c == 0)
                        p.Add((float)(Math.Sin(x / 4.0) * Math.Cos(y / 8.0)));
                    else
                        p.Add((float)(-.25 + 2.0 * (c + 3 * x + 3 * y * res[0]) / (res[0] * res[1])));
        return p.ToArray();
    }

    internal static byte[] GetU8Pixels(Point2i res, int nc)
    {
        List<byte> p = [];
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                for (int c = 0; c < nc; ++c)
                    if (c == 0)
                        p.Add((byte)(x + res[0] * y));
                    else
                        p.Add((byte)(x * y + c * (x - y)));
        return p.ToArray();
    }

    private static Float modelQuantization(Float value, PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.U256:
                return Clamp((value * 255f) + 0.5f, 0, 255) * (1f / 255f);
            case PixelFormat.Half:
                return (Float)(Half)(float)value;
            case PixelFormat.Float:
                return value;
            default:
                LOG_FATAL("Unhandled pixel format");
                return 0;
        }
    }

    private static readonly PixelFormat[] AllFormats = [PixelFormat.U256, PixelFormat.Half, PixelFormat.Float];

    [Fact]
    public void GetSetY()
    {
        Point2i res = new(9, 3);
        float[] yPixels = GetFloatPixels(res, 1);

        foreach (PixelFormat format in AllFormats)
        {
            Image image = new(format, res, ["Y"], ColorEncoding.Linear);
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    image.SetChannel(new Point2i(x, y), 0, yPixels[y * res[0] + x]);

            ImageChannelDesc yDesc = image.GetChannelDesc(["Y"]);
            Assert.True((bool)yDesc);
            Assert.False((bool)image.GetChannelDesc(["Y0L0"]));

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    Float v = image.GetChannel(new Point2i(x, y), 0);
                    ImageChannelValues cv = image.GetChannels(new Point2i(x, y));
                    Assert.Equal(1, cv.Size);
                    Assert.Equal(v, cv[0]);

                    cv = image.GetChannels(new Point2i(x, y), yDesc);
                    Assert.Equal(1, cv.Size);
                    Assert.Equal(v, cv[0]);

                    if (format == PixelFormat.U256)
                        Assert.True(FMath.Abs(v - Clamp(yPixels[y * res[0] + x], 0, 1)) < 0.501f / 255f,
                                    $"{v} vs {Clamp(yPixels[y * res[0] + x], 0, 1)}");
                    else
                        Assert.Equal(modelQuantization(yPixels[y * res[0] + x], format), v);
                }
        }
    }

    [Fact]
    public void GetSetRGB()
    {
        Point2i res = new(7, 32);
        float[] rgbPixels = GetFloatPixels(res, 3);

        foreach (PixelFormat format in AllFormats)
        {
            Image image = new(format, res, ["R", "G", "B"], ColorEncoding.Linear);
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < 3; ++c)
                        image.SetChannel(new Point2i(x, y), c, rgbPixels[3 * y * res[0] + 3 * x + c]);

            ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
            Assert.True((bool)rgbDesc);
            Assert.False((bool)image.GetChannelDesc(["R", "Gxxx", "B"]));

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    ImageChannelValues rgb = image.GetChannels(new Point2i(x, y));
                    Assert.Equal(3, rgb.Size);

                    ImageChannelValues rgb2 = image.GetChannels(new Point2i(x, y), rgbDesc);
                    Assert.Equal(rgb[0], rgb2[0]);
                    Assert.Equal(rgb[1], rgb2[1]);
                    Assert.Equal(rgb[2], rgb2[2]);

                    for (int c = 0; c < 3; ++c)
                    {
                        Assert.Equal(rgb[c], image.GetChannel(new Point2i(x, y), c));
                        int offset = 3 * y * res[0] + 3 * x + c;
                        if (format == PixelFormat.U256)
                            Assert.True(FMath.Abs(rgb[c] - Clamp(rgbPixels[offset], 0, 1)) < 0.501f / 255f);
                        else
                            Assert.Equal(modelQuantization(rgbPixels[offset], format), rgb[c]);
                    }
                }
        }
    }

    [Fact]
    public void GetSetBGR()
    {
        Point2i res = new(7, 32);
        float[] rgbPixels = GetFloatPixels(res, 3);

        foreach (PixelFormat format in AllFormats)
        {
            Image image = new(format, res, ["R", "G", "B"], ColorEncoding.Linear);
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < 3; ++c)
                        image.SetChannel(new Point2i(x, y), c, rgbPixels[3 * y * res[0] + 3 * x + c]);

            ImageChannelDesc bgrDesc = image.GetChannelDesc(["B", "G", "R"]);
            Assert.True((bool)bgrDesc);

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    ImageChannelValues rgb = image.GetChannels(new Point2i(x, y));
                    Assert.Equal(3, rgb.Size);

                    ImageChannelValues bgr = image.GetChannels(new Point2i(x, y), bgrDesc);
                    Assert.Equal(rgb[0], bgr[2]);
                    Assert.Equal(rgb[1], bgr[1]);
                    Assert.Equal(rgb[2], bgr[0]);

                    for (int c = 0; c < 3; ++c)
                    {
                        Assert.Equal(rgb[c], image.GetChannel(new Point2i(x, y), c));
                        int offset = 3 * y * res[0] + 3 * x + c;
                        if (format == PixelFormat.U256)
                            Assert.True(FMath.Abs(rgb[c] - Clamp(rgbPixels[offset], 0, 1)) < 0.501f / 255f);
                        else
                            Assert.Equal(modelQuantization(rgbPixels[offset], format), rgb[c]);
                    }
                }
        }
    }

    [Fact]
    public void CopyRectOut()
    {
        Point2i res = new(29, 14);

        foreach (PixelFormat format in AllFormats)
        {
            // for (int nc : { 1, 3 }) {
            for (int nc = 1; nc < 4; ++nc)
            {
                float[] orig = GetFloatPixels(res, nc);

                List<string> channelNames = ["A"];
                for (int i = 1; i < nc; ++i)
                    channelNames.Add(new string((char)('A' + 1), 1));

                Image image = new(format, res, channelNames.ToArray(), ColorEncoding.Linear);

                int origIter = 0;
                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                        for (int c = 0; c < nc; ++c, ++origIter)
                            image.SetChannel(new Point2i(x, y), c, orig[origIter]);

                Bounds2i extent = new(new Point2i(2, 3), new Point2i(5, 10));
                float[] buf = new float[extent.Area() * nc];

                image.CopyRectOut(extent, buf);

                // Iterate through the points in the extent and the buffer
                // together.
                int bufIter = 0;
                foreach (Point2i p in extent)
                {
                    for (int c = 0; c < nc; ++c)
                    {
                        Assert.True(bufIter < buf.Length);
                        Assert.Equal(buf[bufIter], image.GetChannel(p, c));
                        ++bufIter;
                    }
                }
            }
        }
    }

    [Fact]
    public void CopyRectIn()
    {
        Point2i res = new(17, 32);
        RNG rng = new();

        foreach (PixelFormat format in AllFormats)
        {
            for (int nc = 1; nc < 4; ++nc)
            {
                float[] orig = GetFloatPixels(res, nc);

                List<string> channelNames = ["A"];
                for (int i = 1; i < nc; ++i)
                    channelNames.Add(new string((char)('A' + 1), 1));

                Image image = new(format, res, channelNames.ToArray(), ColorEncoding.Linear);
                int origIter = 0;
                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                        for (int c = 0; c < nc; ++c, ++origIter)
                            image.SetChannel(new Point2i(x, y), c, orig[origIter]);

                Bounds2i extent = new(new Point2i(10, 23), new Point2i(17, 28));
                float[] buf = new float[extent.Area() * nc];
                for (int i = 0; i < buf.Length; ++i)
                    buf[i] = rng.Uniform<float>();

                image.CopyRectIn(extent, buf);

                // Iterate through the points in the extent and the buffer
                // together.
                int bufIter = 0;
                foreach (Point2i p in extent)
                {
                    for (int c = 0; c < nc; ++c)
                    {
                        Assert.True(bufIter < buf.Length);
                        if (format == PixelFormat.U256)
                        {
                            Float err = FMath.Abs(image.GetChannel(p, c) - Clamp(buf[bufIter], 0, 1));
                            Assert.True(err < 0.501f / 255f);
                        }
                        else
                        {
                            Float qv = modelQuantization(buf[bufIter], format);
                            Assert.Equal(qv, image.GetChannel(p, c));
                        }
                        ++bufIter;
                    }
                }
            }
        }
    }

    [Fact]
    public void PfmIO()
    {
        Point2i res = new(16, 49);
        float[] rgbPixels = GetFloatPixels(res, 3);

        Image image = new(rgbPixels, res, ["R", "G", "B"]);
        string fn = InTestDir("test.pfm");
        Assert.True(image.Write(fn));
        ImageAndMetadata read = Image.Read(fn);
        Assert.True(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

        Assert.Equal(image.Resolution(), read.image.Resolution());
        Assert.Equal(PixelFormat.Float, read.image.Format());
        Assert.Equal(3, read.image.NChannels());

        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                for (int c = 0; c < 3; ++c)
                    Assert.Equal(image.GetChannel(new Point2i(x, y), c), read.image.GetChannel(new Point2i(x, y), c));

        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ExrIO()
    {
        Point2i res = new(16, 49);
        float[] rgbPixels = GetFloatPixels(res, 3);

        foreach (PixelFormat format in AllFormats)
        {
            Image image = new(format, res, ["R", "G", "B"], ColorEncoding.Linear);
            image.CopyRectIn(new Bounds2i(new Point2i(0, 0), res), rgbPixels);

            // Check CopyRectIn()
            int offset = 0;
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    for (int c = 0; c < 3; ++c, ++offset)
                    {
                        if (format == PixelFormat.U256)
                        {
                        }
                        else if (format == PixelFormat.Half)
                            Assert.Equal((Float)(Half)(float)rgbPixels[offset], image.GetChannel(new Point2i(x, y), c));
                        else if (format == PixelFormat.Float)
                            Assert.Equal(rgbPixels[offset], image.GetChannel(new Point2i(x, y), c));
                    }

            ImageMetadata metadata = new();
            metadata.colorSpace = RGBColorSpace.ACES2065_1;
            string fn = InTestDir("test.exr");
            Assert.True(image.Write(fn, metadata));

            ImageAndMetadata read = Image.Read(fn);

            Assert.Equal(image.Resolution(), read.image.Resolution());
            Assert.True(RGBColorSpace.ACES2065_1 == read.metadata.GetColorSpace());
            if (!Is8Bit(format))
                Assert.Equal(format, read.image.Format());

            ImageChannelDesc rgbDesc = read.image.GetChannelDesc(["R", "G", "B"]);
            Assert.True((bool)rgbDesc);

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    ImageChannelValues v = read.image.GetChannels(new Point2i(x, y), rgbDesc);
                    for (int c = 0; c < 3; ++c)
                        if (Is8Bit(format))
                            Assert.Equal((Float)(Half)(float)image.GetChannel(new Point2i(x, y), c), v[c]);
                        else if (Is16Bit(format))
                            Assert.Equal((Float)(Half)(float)image.GetChannel(new Point2i(x, y), c), v[c]);
                        else
                            Assert.Equal(image.GetChannel(new Point2i(x, y), c), v[c]);
                }

            Assert.True(RemoveFile(fn));
        }
    }

    [Fact]
    public void ExrNoMetadata()
    {
        Point2i res = new(16, 32);
        float[] rgbPixels = GetFloatPixels(res, 3);
        Image image = new(rgbPixels, res, ["R", "G", "B"]);

        string filename = InTestDir("nometadata.exr");
        ImageMetadata writeMetadata = new();
        writeMetadata.colorSpace = RGBColorSpace.ACES2065_1;
        Assert.True(image.Write(filename, writeMetadata));

        ImageAndMetadata read = Image.Read(filename);
        ImageMetadata metadata = read.metadata;
        Assert.True(RGBColorSpace.ACES2065_1 == metadata.GetColorSpace());

        // All of the metadata should be unset
        Assert.False(metadata.renderTimeSeconds.HasValue);
        Assert.False(metadata.cameraFromWorld.HasValue);
        Assert.False(metadata.NDCFromWorld.HasValue);
        Assert.True(metadata.pixelBounds.HasValue);
        Assert.Equal(new Bounds2i(new Point2i(0, 0), res), metadata.pixelBounds!.Value);
        Assert.True(metadata.fullResolution.HasValue);
        Assert.Equal(res, metadata.fullResolution!.Value);
        Assert.Empty(metadata.strings);
        Assert.Empty(metadata.stringVectors);

        Assert.True(RemoveFile(filename));
    }

    [Fact(Skip = DoubleMode.PbrtFails)]
    public void ExrMetadata()
    {
        Point2i res = new(16, 32);
        float[] rgbPixels = GetFloatPixels(res, 3);
        Image image = new(rgbPixels, res, ["R", "G", "B"]);

        string filename = InTestDir("metadata.exr");
        ImageMetadata outMetadata = new();
        outMetadata.colorSpace = RGBColorSpace.Rec2020;
        outMetadata.renderTimeSeconds = 1234;
        SquareMatrix4 w2c = new(3, 1, 4, 1, 5, 9, 2, Pi, 2, 7, 1, 8, 2, 8, 1, MathF.Exp(1f));
        SquareMatrix4 w2n = new(1.5f, 2.5f, 3.5f, 4.75f, 5.333f, 6.2135f, -351.2f, -552f, 63.2f, 47.2f, Pi,
                                MathF.Cos(1f), 0, -14, 6, 1e-10f);
        // Must be the same area as image resolution.
        Bounds2i pb = new(new Point2i(2, 10), new Point2i(18, 42));
        Point2i fullRes = new(1000, 200);
        SortedDictionary<string, List<string>> stringVectors = new(StringComparer.Ordinal);
        stringVectors["yolo"] = ["foo", "bar"];

        outMetadata.cameraFromWorld = w2c;
        outMetadata.NDCFromWorld = w2n;
        outMetadata.pixelBounds = pb;
        outMetadata.fullResolution = fullRes;
        outMetadata.strings["pbrt"] = "v4";
        outMetadata.stringVectors = stringVectors;
        Assert.True(image.Write(filename, outMetadata));

        ImageAndMetadata read = Image.Read(filename);
        ImageMetadata inMetadata = read.metadata;

        Assert.True(RGBColorSpace.Rec2020 == inMetadata.GetColorSpace());

        Assert.True(inMetadata.renderTimeSeconds.HasValue);
        Assert.Equal(1234, inMetadata.renderTimeSeconds!.Value);

        Assert.True(inMetadata.cameraFromWorld.HasValue);
        Assert.Equal(w2c, inMetadata.cameraFromWorld!.Value);

        Assert.True(inMetadata.NDCFromWorld.HasValue);
        Assert.Equal(w2n, inMetadata.NDCFromWorld!.Value);

        Assert.True(inMetadata.pixelBounds.HasValue);
        Assert.Equal(pb, inMetadata.pixelBounds!.Value);

        Assert.True(inMetadata.fullResolution.HasValue);
        Assert.Equal(fullRes, inMetadata.fullResolution!.Value);

        Assert.Single(inMetadata.strings);
        Assert.Equal("v4", inMetadata.strings["pbrt"]);

        Assert.Single(inMetadata.stringVectors);
        // (pbrt checks the written stringVectors here; this checks what was read.)
        Assert.True(inMetadata.stringVectors.TryGetValue("yolo", out List<string>? sv));
        Assert.Equal("foo", sv![0]);
        Assert.Equal("bar", sv[1]);

        Assert.True(RemoveFile(filename));
    }

    [Fact]
    public void PngYIO()
    {
        Point2i res = new(11, 50);
        byte[] rgbPixels = GetU8Pixels(res, 1);

        Image image = new(rgbPixels, res, ["Y"], ColorEncoding.sRGB);
        string fn = InTestDir("test.png");
        Assert.True(image.Write(fn));
        ImageAndMetadata read = Image.Read(fn);

        Assert.Equal(image.Resolution(), read.image.Resolution());
        Assert.Equal(PixelFormat.U256, read.image.Format());
        Assert.NotNull(read.image.Encoding());

        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                Assert.Equal(SRGB8ToLinear(rgbPixels[y * res[0] + x]), read.image.GetChannel(new Point2i(x, y), 0));

        Assert.True(RemoveFile(fn));
    }

    private static void CheckPngRgb(string fn)
    {
        Point2i res = new(11, 50);
        byte[] rgbPixels = GetU8Pixels(res, 3);

        Image image = new(rgbPixels, res, ["R", "G", "B"], ColorEncoding.sRGB);
        Assert.True(image.Write(fn));
        ImageAndMetadata read = Image.Read(fn);

        Assert.Equal(image.Resolution(), read.image.Resolution());
        Assert.Equal(PixelFormat.U256, read.image.Format());
        Assert.NotNull(read.image.Encoding());
        // EXPECT_EQ(*read.image.Encoding(), *ColorEncoding::sRGB);
        Assert.NotNull(read.metadata.colorSpace);
        Assert.True(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

        // (pbrt compares against _image_ here; this checks _read.image_.)
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                for (int c = 0; c < 3; ++c)
                    Assert.Equal(SRGB8ToLinear(rgbPixels[c + 3 * (y * res[0] + x)]),
                                 read.image.GetChannel(new Point2i(x, y), c));

        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void PngRgbIO() => CheckPngRgb(InTestDir("test-rgb.png"));

    [Fact]
    public void PngEmojiIO() =>
        // trex.png
        CheckPngRgb(InTestDir("\U0001F996.png"));

    [Fact]
    public void QoiRgbIO()
    {
        Point2i res = new(11, 50);
        byte[] rgbPixels = GetU8Pixels(res, 3);

        Image image = new(rgbPixels, res, ["R", "G", "B"], ColorEncoding.sRGB);
        string fn = InTestDir("test.qoi");
        Assert.True(image.Write(fn));
        ImageAndMetadata read = Image.Read(fn);

        Assert.Equal(image.Resolution(), read.image.Resolution());
        Assert.Equal(PixelFormat.U256, read.image.Format());
        Assert.NotNull(read.image.Encoding());
        Assert.NotNull(read.metadata.colorSpace);
        Assert.True(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                for (int c = 0; c < 3; ++c)
                    Assert.Equal(SRGB8ToLinear(rgbPixels[c + 3 * (y * res[0] + x)]),
                                 read.image.GetChannel(new Point2i(x, y), c));

        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void QoiRgbaIO()
    {
        Point2i res = new(11, 50);
        byte[] rgbPixels = GetU8Pixels(res, 4);

        // Intentionally out of normal order...
        Image image = new(rgbPixels, res, ["A", "R", "G", "B"], ColorEncoding.sRGB);
        string fn = InTestDir("test-rgba.qoi");
        Assert.True(image.Write(fn));
        ImageAndMetadata read = Image.Read(fn);

        Assert.Equal(image.Resolution(), read.image.Resolution());
        Assert.Equal(PixelFormat.U256, read.image.Format());
        Assert.NotNull(read.image.Encoding());
        Assert.NotNull(read.metadata.colorSpace);
        Assert.True(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

        ImageChannelDesc desc = image.GetChannelDesc(["A", "R", "G", "B"]);
        Assert.True((bool)desc);

        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
            {
                ImageChannelValues v = image.GetChannels(new Point2i(x, y), desc);
                for (int c = 0; c < 4; ++c)
                    Assert.Equal(SRGB8ToLinear(rgbPixels[c + 4 * (y * res[0] + x)]), v[c]);
            }

        // Not in image_test.cpp: the file has the channels in RGBA order.
        ImageChannelDesc readDesc = read.image.GetChannelDesc(["A", "R", "G", "B"]);
        Assert.True((bool)readDesc);
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
            {
                ImageChannelValues v = read.image.GetChannels(new Point2i(x, y), readDesc);
                for (int c = 0; c < 4; ++c)
                    Assert.Equal(SRGB8ToLinear(rgbPixels[c + 4 * (y * res[0] + x)]), v[c]);
            }

        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void SampleSimple()
    {
        float[] texels = [0, 1, 0, 0];
        Image zeroOne = new(texels, new Point2i(2, 2), ["Y"]);
        PiecewiseConstant2D distrib = new(zeroOne.GetSamplingDistribution());
        RNG rng = new();
        for (int i = 0; i < 1000; ++i)
        {
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Point2f p = distrib.Sample(u, out Float pdf, out _);
            // Due to bilerp on lookup, the non-zero range goes out a bit.
            Assert.True(p.x >= 0.5);
            Assert.True(p.y <= 0.5);
        }
    }

    [Fact]
    public void SampleLinear()
    {
        int w = 500, h = 500;
        List<float> v = [];
        for (int y = 0; y < h; ++y)
        {
            Float fy = (y + .5f) / h;
            for (int x = 0; x < w; ++x)
            {
                Float fx = (x + .5f) / w;
                // This integrates to 1 over [0,1]^2
                Float f = fx + fy;
                v.Add((float)f);
            }
        }

        Image image = new(v.ToArray(), new Point2i(w, h), ["Y"]);
        PiecewiseConstant2D distrib = new(image.GetSamplingDistribution());
        RNG rng = new();
        for (int i = 0; i < 1000; ++i)
        {
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Point2f p = distrib.Sample(u, out Float pdf, out _);
            Float f = p.x + p.y;
            // Allow some error since PiecewiseConstant2D uses a piecewise constant
            // sampling distribution.
            Assert.True(FMath.Abs(f - pdf) <= 3e-3, $"{u}, f: {f}, pdf: {pdf}");
        }
    }

    [Fact]
    public void SampleSinCos()
    {
        int w = 500, h = 500;
        static Float f(Point2f p) => (Float)Math.Abs(Math.Sin(3.0 * p.x) * Sqr(Math.Cos(4.0 * p.y)));
        // Integral of f over [0,1]^2
        Float integral = (Float)(1.0 / 24.0 * Sqr(Math.Sin(1.5)) * (8 + Math.Sin(8.0)));

        List<float> v = [];
        for (int y = 0; y < h; ++y)
        {
            Float fy = (y + .5f) / h;
            for (int x = 0; x < w; ++x)
            {
                Float fx = (x + .5f) / w;
                v.Add((float)f(new Point2f(fx, fy)));
            }
        }

        Image image = new(v.ToArray(), new Point2i(w, h), ["Y"]);
        PiecewiseConstant2D distrib = new(image.GetSamplingDistribution());
        RNG rng = new();
        for (int i = 0; i < 1000; ++i)
        {
            Point2f u = new(rng.Uniform<Float>(), rng.Uniform<Float>());
            Point2f p = distrib.Sample(u, out Float pdf, out _);
            Float fp = f(p);
            // Allow some error since PiecewiseConstant2D uses a piecewise constant
            // sampling distribution.
            Assert.True(FMath.Abs(fp - pdf * integral) <= .02f, $"{u}, fp: {fp}, pdf: {pdf}");
        }
    }

    [Fact]
    public void Wrap2D()
    {
        float[] texels = [0, 1, 0, 0, 0, 0, 0, 0, 0];
        Image zeroOne = new(texels, new Point2i(3, 3), ["Y"]);

        Assert.Equal(1, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Clamp)));
        Assert.Equal(1, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Black, WrapMode.Clamp)));
        Assert.Equal(0, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Black, WrapMode.Repeat)));
        Assert.Equal(0, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Black)));

        Assert.Equal(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Clamp)));
        Assert.Equal(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Repeat, WrapMode.Clamp)));
        Assert.Equal(1, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Black, WrapMode.Repeat)));
        Assert.Equal(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Black)));

        Assert.Equal(0.5f, zeroOne.BilerpChannel(new Point2f(0.5f, 0f), 0, WrapMode.Repeat));
        Assert.Equal(0.5f, zeroOne.BilerpChannel(new Point2f(0.5f, 0f), 0, WrapMode.Black));
        Assert.Equal(1, zeroOne.BilerpChannel(new Point2f(0.5f, 0f), 0, WrapMode.Clamp));
    }

    [Fact]
    public void Select()
    {
        Point2i res = new(4, 9);
        float[] pix = GetFloatPixels(res, 4);
        Image image = new(pix, res, ["A", "B", "G", "R"]);

        ImageChannelDesc aDesc = image.GetChannelDesc(["A"]);
        Assert.True((bool)aDesc);
        Image aImage = image.SelectChannels(aDesc);

        Assert.Equal(aImage.Resolution(), image.Resolution());
        Assert.Equal(1, aImage.NChannels());
        Assert.Single(aImage.ChannelNames());
        Assert.Equal("A", aImage.ChannelNames()[0]);
        for (int y = 0; y < res.y; ++y)
            for (int x = 0; x < res.x; ++x)
                Assert.Equal(aImage.GetChannel(new Point2i(x, y), 0), image.GetChannel(new Point2i(x, y), 0));

        ImageChannelDesc rgDesc = image.GetChannelDesc(["R", "G"]);
        Assert.True((bool)rgDesc);
        Image rgImage = image.SelectChannels(rgDesc);
        Assert.Equal(rgImage.Resolution(), image.Resolution());
        Assert.Equal(2, rgImage.NChannels());
        Assert.Equal(2, rgImage.ChannelNames().Count);
        Assert.Equal("R", rgImage.ChannelNames()[0]);
        Assert.Equal("G", rgImage.ChannelNames()[1]);
        for (int y = 0; y < res.y; ++y)
            for (int x = 0; x < res.x; ++x)
            {
                Assert.Equal(rgImage.GetChannel(new Point2i(x, y), 0), image.GetChannel(new Point2i(x, y), 3));
                Assert.Equal(rgImage.GetChannel(new Point2i(x, y), 1), image.GetChannel(new Point2i(x, y), 2));
            }
    }
}

public class ImageIOTests
{
    private static void TestRoundTrip(string fn)
    {
        Point2i res = new(16, 29);
        Image image = new(PixelFormat.Float, res, ["R", "G", "B"]);
        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
                image.SetChannels(new Point2i(x, y),
                                  [(Float)x / (Float)(res[0] - 1), (Float)y / (Float)(res[1] - 1), -1.5f]);

        string filename = ImageTests.InTestDir(fn);
        Assert.True(image.Write(filename));

        ImageAndMetadata readImage = Image.Read(filename);
        Assert.Equal(res, readImage.image.Resolution());

        ImageChannelDesc rgbDesc = readImage.image.GetChannelDesc(["R", "G", "B"]);
        Assert.True((bool)rgbDesc);

        for (int y = 0; y < res[1]; ++y)
            for (int x = 0; x < res[0]; ++x)
            {
                ImageChannelValues rgb = readImage.image.GetChannels(new Point2i(x, y), rgbDesc);

                for (int c = 0; c < 3; ++c)
                {
                    float wrote = (float)image.GetChannel(new Point2i(x, y), c);
                    float delta = (float)(wrote - rgb[c]);
                    string where = $"{filename}:({x}, {y}) c = {c} wrote {wrote}, read {rgb[c]}, delta = {delta}";
                    if (HasExtension(filename, "pfm"))
                        // Everything should come out exact.
                        Assert.True(delta == 0, where);
                    else if (HasExtension(filename, "exr"))
                    {
                        if (c == 2)
                            // -1.5 is exactly representable as a float.
                            Assert.True(delta == 0, where);
                        else
                            Assert.True(MathF.Abs(delta) < .001, where);
                    }
                    else
                    {
                        // 8 bit format...
                        if (c == 2)
                            // -1.5 should be clamped to zero.
                            Assert.True(rgb[c] == 0, where + " (expected 0 back)");
                        else
                            // Allow a fair amount of slop, since there's an sRGB
                            // conversion before quantization to 8-bits...
                            Assert.True(MathF.Abs(delta) < .02, where);
                    }
                }
            }

        // Clean up
        Assert.True(RemoveFile(filename));
    }

    [Fact]
    public void RoundTripEXR() => TestRoundTrip("out.exr");

    [Fact]
    public void RoundTripPFM() => TestRoundTrip("out.pfm");

    [Fact]
    public void RoundTripPNG() => TestRoundTrip("out.png");

    [Fact]
    public void RoundTripQOI() => TestRoundTrip("out.qoi");

    // Not in image_test.cpp: checks for the port.

    [Fact]
    public void ReadHDR()
    {
        // A tiny uncompressed Radiance RGBE file (widths < 8 aren't RLE
        // encoded): pixel (x, y) = (x + 1, y + 1, 0.5) * 2^e.
        int w = 3, h = 2;
        using MemoryStream m = new();
        m.Write(System.Text.Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {h} +X {w}\n"));
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
                m.Write([(byte)(64 * (x + 1)), (byte)(64 * (y + 1)), 32, 128 + 1]);
        string fn = ImageTests.InTestDir("test.hdr");
        File.WriteAllBytes(fn, m.ToArray());

        ImageAndMetadata read = Image.Read(fn);
        Assert.Equal(new Point2i(w, h), read.image.Resolution());
        Assert.Equal(PixelFormat.Float, read.image.Format());
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                // stb_image decodes m * 2^(e - (128 + 8)).
                ImageChannelValues v = read.image.GetChannels(new Point2i(x, y));
                Assert.Equal(64 * (x + 1) / 256f * 2, v[0]);
                Assert.Equal(64 * (y + 1) / 256f * 2, v[1]);
                Assert.Equal(32 / 256f * 2, v[2]);
            }
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ReadOtherFormats()
    {
        // Other formats go through stb_image (StbImageSharp); a 24-bit BMP.
        int w = 5, h = 3, stride = (3 * w + 3) & ~3;
        using MemoryStream m = new();
        using BinaryWriter b = new(m);
        b.Write((byte)'B');
        b.Write((byte)'M');
        b.Write(54 + stride * h);
        b.Write(0);
        b.Write(54);
        b.Write(40);
        b.Write(w);
        b.Write(h);
        b.Write((short)1);
        b.Write((short)24);
        b.Write(0);
        b.Write(stride * h);
        b.Write(2835);
        b.Write(2835);
        b.Write(0);
        b.Write(0);
        for (int y = h - 1; y >= 0; --y)  // bottom-up
        {
            for (int x = 0; x < w; ++x)
            {
                b.Write((byte)(10 * y));      // B
                b.Write((byte)(20 * x));      // G
                b.Write((byte)(x + 7 * y));   // R
            }
            for (int i = 3 * w; i < stride; ++i)
                b.Write((byte)0);
        }
        b.Flush();
        string fn = ImageTests.InTestDir("test.bmp");
        File.WriteAllBytes(fn, m.ToArray());

        ImageAndMetadata read = Image.Read(fn);
        Assert.Equal(new Point2i(w, h), read.image.Resolution());
        Assert.Equal(PixelFormat.U256, read.image.Format());
        ImageChannelDesc rgb = read.image.GetChannelDesc(["R", "G", "B"]);
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                ImageChannelValues v = read.image.GetChannels(new Point2i(x, y), rgb);
                Assert.Equal(SRGB8ToLinear((byte)(x + 7 * y)), v[0]);
                Assert.Equal(SRGB8ToLinear((byte)(20 * x)), v[1]);
                Assert.Equal(SRGB8ToLinear((byte)(10 * y)), v[2]);
            }
        Assert.True(RemoveFile(fn));

        File.WriteAllText(fn, "not an image");
        Assert.Throws<FatalErrorException>(() => Image.Read(fn));
        Assert.True(RemoveFile(fn));
    }

    // pbrt asks lodepng for 16-bit grey for 16-bit grey and grey+alpha
    // PNGs, which lodepng can only give without a conversion, so pbrt fails
    // on 16-bit grey+alpha and on 16-bit grey with tRNS; these read them
    // (alpha is dropped, as pbrt does for 8-bit grey+alpha).
    [Theory]
    [InlineData(0, 8, false)]
    [InlineData(0, 16, false)]
    [InlineData(0, 8, true)]
    [InlineData(0, 16, true)]
    [InlineData(4, 8, false)]
    [InlineData(4, 16, false)]
    public void PngGrey(int colorType, int bitDepth, bool trns)
    {
        static uint Crc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xffffffff;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; ++k)
                    crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            }
            return ~crc;
        }
        static byte[] Chunk(string type, byte[] data)
        {
            byte[] c = new byte[12 + data.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(c, data.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
            data.CopyTo(c, 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                c.AsSpan(8 + data.Length), Crc32(c.AsSpan(4, 4 + data.Length)));
            return c;
        }
        int w = 5, h = 3, bytesPerPixel = (colorType == 4 ? 2 : 1) * bitDepth / 8;
        MemoryStream raw = new();
        using (System.IO.Compression.ZLibStream z = new(raw, System.IO.Compression.CompressionLevel.Optimal, true))
            for (int y = 0; y < h; ++y)
            {
                z.WriteByte(0);
                for (int i = 0; i < w * bytesPerPixel; ++i)
                    z.WriteByte((byte)(37 * i + 11 * y));
            }
        byte[] ihdr = [0, 0, 0, (byte)w, 0, 0, 0, (byte)h, (byte)bitDepth, (byte)colorType, 0, 0, 0];
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, .. Chunk("IHDR", ihdr),
                      .. (trns ? Chunk("tRNS", [0, 37]) : []), .. Chunk("IDAT", raw.ToArray()), .. Chunk("IEND", [])];
        string fn = ImageTests.InTestDir($"grey-{colorType}-{bitDepth}-{trns}.png");
        File.WriteAllBytes(fn, png);
        Image image = Image.Read(fn).image;
        Assert.Equal(new Point2i(w, h), image.Resolution());
        Assert.Equal(["Y"], image.ChannelNames());
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                // The grey sample is the first of each pixel's samples.
                int i = x * bytesPerPixel;
                byte hi = (byte)(37 * i + 11 * y), lo = (byte)(37 * (i + 1) + 11 * y);
                Float v = image.GetChannel(new Point2i(x, y), 0);
                if (bitDepth == 16)
                    Assert.Equal(ColorEncoding.sRGB.ToFloatLinear(((hi << 8) + lo) / 65535f), v, 1e-3f);
                else
                    Assert.Equal(SRGB8ToLinear(hi), v);
            }
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void PyramidAndResize()
    {
        // Non-power-of-2 images are resized up; each level averages the
        // previous one.
        Point2i res = new(5, 3);
        float[] pix = ImageTests.GetFloatPixels(res, 1);
        for (int i = 0; i < pix.Length; ++i)
            pix[i] = MathF.Abs(pix[i]);
        Image image = new(pix, res, ["Y"]);
        List<Image> pyramid = Image.GeneratePyramid(image, WrapMode.Clamp);
        Assert.Equal(4, pyramid.Count);
        Assert.Equal(new Point2i(8, 4), pyramid[0].Resolution());
        Assert.Equal(new Point2i(1, 1), pyramid[^1].Resolution());
        for (int l = 1; l < pyramid.Count; ++l)
        {
            Image prev = pyramid[l - 1], cur = pyramid[l];
            for (int y = 0; y < cur.Resolution().y; ++y)
                for (int x = 0; x < cur.Resolution().x; ++x)
                {
                    Float avg = 0;
                    int n = 0;
                    for (int dy = 0; dy < 2; ++dy)
                        for (int dx = 0; dx < 2; ++dx)
                        {
                            Point2i p = new(Math.Min(2 * x + dx, prev.Resolution().x - 1),
                                            Math.Min(2 * y + dy, prev.Resolution().y - 1));
                            avg += prev.GetChannel(p, 0);
                            ++n;
                        }
                    Assert.Equal(avg / n, cur.GetChannel(new Point2i(x, y), 0), 1e-5f);
                }
        }
    }
}


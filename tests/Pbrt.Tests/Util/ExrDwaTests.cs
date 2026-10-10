// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for reading DWAA/DWAB-compressed, B44 perceptually linear and
// multi-part OpenEXR files
// (Util/ExrDwa.cs, Util/Exr.cs), which pbrt has no tests for. The files in
// Data/ were written with the OpenEXR Python bindings (Data/gen-exr-dwa.py);
// the expected values are what pbrt-v4's C++ imgtool reads from them (the
// spot-checked pixels are from "imgtool cat", and the hashes cover all pixel
// values, which matched "imgtool cat" exactly).

using System.Security.Cryptography;
using System.Text;

namespace Pbrt.Tests;

public class ExrDwaTests
{
    private static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private static string PixelHash(Image image)
    {
        StringBuilder sb = new($"{image.Resolution()} {string.Join(",", image.ChannelNames())}");
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                for (int c = 0; c < image.NChannels(); ++c)
                    sb.Append(' ').Append(BitConverter.SingleToInt32Bits((float)image.GetChannel(new Point2i(x, y), c)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static void CheckPixel(Image image, Point2i p, params float[] expected)
    {
        Assert.Equal(expected.Length, image.NChannels());
        for (int c = 0; c < expected.Length; ++c)
            Assert.Equal(expected[c], image.GetChannel(p, c));
    }

    [Fact]
    public void DwaaHalfRGBA()
    {
        // R, G, B are color space converted together; A is RLE compressed
        // and N.X zlib compressed.
        ImageAndMetadata im = Image.Read(DataFile("dwaa-rgba.exr"));
        Image image = im.image;
        Assert.Equal(PixelFormat.Half, image.Format());
        Assert.Equal(new Point2i(37, 29), image.Resolution());
        Assert.Equal(["A", "B", "G", "N.X", "R"], image.ChannelNames());
        CheckPixel(image, new Point2i(0, 0), 0.25f, 0.68115234f, 0.70410156f, 1.7929688f, 0.4987793f);
        CheckPixel(image, new Point2i(25, 2), 0.25f, 1.2236328f, 0.48876953f, 0.7348633f, 0.123168945f);
        Assert.Equal("A1386CE3028E5915", PixelHash(image));
    }

    [Fact]
    public void DwabFloatY()
    {
        // HDR values, constant and zero (and negative zero) blocks, and no
        // quantization.
        Image image = Image.Read(DataFile("dwab-y.exr")).image;
        Assert.Equal(PixelFormat.Float, image.Format());
        Assert.Equal(new Point2i(70, 40), image.Resolution());
        CheckPixel(image, new Point2i(0, 0), 0.75f);
        CheckPixel(image, new Point2i(29, 1), 44.25f);
        Assert.Equal("DFEE33435D85138E", PixelHash(image));
    }

    [Fact]
    public void DwaaTiledLayers()
    {
        ImageAndMetadata im = Image.Read(DataFile("dwaa-tiled-layers.exr"));
        Image image = im.image;
        Assert.Equal(new Point2i(45, 33), image.Resolution());
        Assert.Equal(new Bounds2i(new Point2i(-5, 7), new Point2i(40, 40)), im.metadata.pixelBounds);
        CheckPixel(image, new Point2i(0, 0), 0.3322754f, 0.7006836f, 0.50341797f, 1.9013672f, 3.6054688f, 2.1953125f);
        CheckPixel(image, new Point2i(9, 2), 0.5488281f, 0.5883789f, 0.9086914f, 2.5234375f, 3.2246094f, 4.5390625f);
        Assert.Equal("EB72049816991834", PixelHash(image));
    }

    [Fact]
    public void MultiPart()
    {
        // The first part is read, as pbrt's Imf::InputFile does.
        ImageAndMetadata im = Image.Read(DataFile("multipart.exr"));
        Image image = im.image;
        Assert.Equal(new Point2i(300, 20), image.Resolution());
        Assert.Equal(["B", "G", "R"], image.ChannelNames());
        Assert.Equal("beauty", im.metadata.strings["name"]);
        CheckPixel(image, new Point2i(0, 0), 0.7182617f, 0.3798828f, 0.3803711f);
        CheckPixel(image, new Point2i(99, 0), 0.65966797f, 0.93066406f, 0.5776367f);
        Assert.Equal("F2B0C53473EA38B3", PixelHash(image));
    }

    [Fact]
    public void B44PerceptuallyLinear()
    {
        // R and B are pLinear (stored as 8 log(x), mapped back through
        // OpenEXR's logTable); G isn't.
        Image image = Image.Read(DataFile("b44-plinear.exr")).image;
        Assert.Equal(PixelFormat.Half, image.Format());
        Assert.Equal(new Point2i(17, 9), image.Resolution());
        Assert.Equal(["B", "G", "R"], image.ChannelNames());
        CheckPixel(image, new Point2i(0, 0), 4.5117188f, 10.953125f, 9.3515625f);
        CheckPixel(image, new Point2i(5, 2), 6.0390625f, 27.828125f, 7.734375f);
        Assert.Equal("C34BB0A4FFD163FE", PixelHash(image));
    }
}

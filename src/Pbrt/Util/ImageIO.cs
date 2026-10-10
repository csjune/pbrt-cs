// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the image file I/O in src/pbrt/util/image.cpp.
//
// pbrt's third-party libraries are replaced as follows:
//   OpenEXR -> Exr.cs (a port of the OpenEXR file format)
//   lodepng -> Png.cs
//   qoi.h   -> Qoi.cs (a direct port)
//   stb_image -> Hdr.cs (a port of its HDR loader) and, for the other
//     formats, StbImageSharp, the C# port of stb_image (NuGet package)

using System.Globalization;
using StbImageSharp;

namespace Pbrt;

// ImageAndMetadata Definition
public sealed class ImageAndMetadata
{
    public Image image;
    public ImageMetadata metadata;

    public ImageAndMetadata(Image image, ImageMetadata metadata)
    {
        this.image = image;
        this.metadata = metadata;
    }
}

public sealed partial class Image
{
    // ImageIO Function Definitions
    public static ImageAndMetadata Read(string name, ColorEncoding? encoding = null)
    {
        if (HasExtension(name, "exr"))
            return ReadEXR(name);
        else if (HasExtension(name, "png"))
            return ReadPNG(name, encoding);
        else if (HasExtension(name, "pfm"))
            return ReadPFM(name);
        else if (HasExtension(name, "hdr"))
            return ReadHDR(name);
        else if (HasExtension(name, "qoi"))
            return ReadQOI(name);
        else
        {
            ImageResult? result = null;
            try
            {
                // (stbi_load() quietly fails if the file can't be read.)
                result = ImageResult.FromMemory(File.ReadAllBytes(name), ColorComponents.Default);
            }
            catch (Exception)
            {
                // stbi_load() failed; handled below.
            }
            if (result != null)
            {
                int x = result.Width, y = result.Height, n = (int)result.Comp;
                byte[] pixels = result.Data;
                CHECK_EQ(x * y * n, pixels.Length);
                switch (n)
                {
                    case 1:
                        return new ImageAndMetadata(
                            new Image(pixels, new Point2i(x, y), ["Y"], ColorEncoding.sRGB),
                            new ImageMetadata());
                    case 2:
                    {
                        Image image = new(pixels, new Point2i(x, y), ["Y", "A"], ColorEncoding.sRGB);
                        return new ImageAndMetadata(image.SelectChannels(image.GetChannelDesc(["Y"])),
                                                    new ImageMetadata());
                    }
                    case 3:
                        return new ImageAndMetadata(new Image(pixels, new Point2i(x, y), ["R", "G", "B"],
                                                              ColorEncoding.sRGB),
                                                    new ImageMetadata());
                    case 4:
                    {
                        Image image = new(pixels, new Point2i(x, y), ["R", "G", "B", "A"],
                                          ColorEncoding.sRGB);
                        return new ImageAndMetadata(
                            image.SelectChannels(image.GetChannelDesc(["R", "G", "B"])),
                            new ImageMetadata());
                    }
                    default:
                        ErrorExit($"{name}: {n} channel image unsupported.");
                        break;
                }
            }
            else
                ErrorExit($"{name}: no support for reading images with this extension");
            return null!;
        }
    }

    public bool Write(string name) => Write(name, new ImageMetadata());

    public bool Write(string name, ImageMetadata metadata)
    {
        if (metadata.pixelBounds.HasValue)
            CHECK_EQ(metadata.pixelBounds.Value.Area(), resolution.x * resolution.y);

        if (HasExtension(name, "exr"))
            return WriteEXR(name, metadata);

        // This copy will sometimes be wasteful but simplifies logic below...
        Image outImage = Clone();

        switch (NChannels())
        {
            case 1:
                // all good; onward
                break;
            case 2:
                Error($"{name}: unable to write a 2 channel image in this format.");
                return false;
            case 3:
            {
                ImageChannelDesc desc = outImage.GetChannelDesc(["R", "G", "B"]);
                if (!desc)
                    // Still go for it with 3 channels.
                    Warning($"{name}: image has 3 channels but they are not R, G, and B. Image may be garbled.");
                else
                    // Reorder them as RGB.
                    outImage = outImage.SelectChannels(desc);
                break;
            }
            case 4:
            {
                ImageChannelDesc desc = outImage.GetChannelDesc(["R", "G", "B", "A"]);
                if (!desc)
                    // Still go for it.
                    Warning($"{name}: image has 4 channels but they are not R, G, B, and A. Image may be garbled.");
                else
                    // Reorder them as RGBA.
                    outImage = outImage.SelectChannels(desc);
                break;
            }
            default:
            {
                ImageChannelDesc desc = outImage.GetChannelDesc(["R", "G", "B"]);
                if (!desc)
                {
                    Error($"{name}: multi-channel image does not have R, G, and B. Unable to write to this format.");
                    return false;
                }
                else
                {
                    string err = $"{name}: ignoring additional channels for this image format: ";
                    foreach (string ch in outImage.ChannelNames())
                        if (ch != "R" && ch != "G" && ch != "B")
                            err += $"\"{ch}\" ";
                    Error(err);
                }

                outImage = outImage.SelectChannels(desc);
                break;
            }
        }
        CHECK(outImage.NChannels() == 1 || outImage.NChannels() == 3 ||
              outImage.NChannels() == 4);

        ImageMetadata outMetadata = metadata.Clone();
        if (outImage.NChannels() != 1 && metadata.GetColorSpace() != RGBColorSpace.sRGB)
        {
            Warning($"{name}: converting pixel colors to sRGB to match output image format.");
            SquareMatrix3 m = ConvertRGBColorSpace(metadata.GetColorSpace(), RGBColorSpace.sRGB);
            ImageChannelDesc rgbDesc = outImage.GetChannelDesc(["R", "G", "B"]);
            // May not have RGB for weird non-RGB 3 channel images...
            if (rgbDesc)
            {
                for (int y = 0; y < outImage.Resolution().y; ++y)
                    for (int x = 0; x < outImage.Resolution().x; ++x)
                    {
                        ImageChannelValues channels = outImage.GetChannels(new Point2i(x, y), rgbDesc);
                        RGB rgb = m * new RGB(channels[0], channels[1], channels[2]);
                        outImage.SetChannels(new Point2i(x, y), rgbDesc, [rgb.r, rgb.g, rgb.b]);
                    }
                outMetadata.colorSpace = RGBColorSpace.sRGB;
            }
        }

        if (HasExtension(name, "pfm"))
            return outImage.WritePFM(name, outMetadata);
        else if (HasExtension(name, "png"))
            return outImage.WritePNG(name, outMetadata);
        else if (HasExtension(name, "qoi"))
            return outImage.WriteQOI(name, outMetadata);
        else
        {
            Error($"{name}: no support for writing images with this extension");
            return false;
        }
    }

    ///////////////////////////////////////////////////////////////////////////
    // OpenEXR

    private static ImageAndMetadata ReadEXR(string name)
    {
        try
        {
            Exr.File file = Exr.Read(ReadFileContents(name));
            Bounds2i dw = file.dataWindow;

            ImageMetadata metadata = new();
            if (file.FindFloat("renderTimeSeconds") is float renderTime)
                metadata.renderTimeSeconds = renderTime;

            if (file.FindM44f("worldToCamera") is float[] worldToCamera)
                metadata.cameraFromWorld = new SquareMatrix4(Array.ConvertAll(worldToCamera, v => (Float)v));

            if (file.FindM44f("worldToNDC") is float[] worldToNDC)
                metadata.NDCFromWorld = new SquareMatrix4(Array.ConvertAll(worldToNDC, v => (Float)v));

            // OpenEXR uses inclusive pixel bounds; Exr.File's are already
            // non-inclusive (the convention pbrt uses).
            metadata.pixelBounds = dw;

            Bounds2i dispw = file.displayWindow;
            metadata.fullResolution = new Point2i(dispw.pMax.x - dispw.pMin.x, dispw.pMax.y - dispw.pMin.y);

            if (file.FindInt("samplesPerPixel") is int spp)
                metadata.samplesPerPixel = spp;

            if (file.FindFloat("MSE") is float mse)
                metadata.MSE = mse;

            // Find any string or string vector attributes
            foreach (Exr.Attribute attrib in file.attributes)
            {
                if (attrib.type == "string")
                    metadata.strings[attrib.name] = Exr.DecodeString(attrib.value);
                if (attrib.type == "stringvector")
                    metadata.stringVectors[attrib.name] = Exr.DecodeStringVector(attrib.value);
            }

            // Figure out the color space
            if (file.FindChromaticities() is float[] c)
            {
                RGBColorSpace? cs = RGBColorSpace.Lookup(
                    new Point2f(c[0], c[1]), new Point2f(c[2], c[3]),
                    new Point2f(c[4], c[5]), new Point2f(c[6], c[7]));
                if (cs == null)
                {
                    Warning(FormattableString.Invariant(
                        $"Couldn't find supported color space that matches chromaticities: r ({c[0]}, {c[1]}) g ({c[2]}, {c[3]}) b ({c[4]}, {c[5]}), w ({c[6]}, {c[7]}). Using sRGB."));
                    metadata.colorSpace = RGBColorSpace.sRGB;
                }
                else
                    metadata.colorSpace = cs;
            }

            int width = dw.pMax.x - dw.pMin.x;
            int height = dw.pMax.y - dw.pMin.y;

            List<string> channelNames = [];
            int nChannels = 0;
            Exr.PixelType pixelType = Exr.PixelType.Half;
            foreach (Exr.Channel channel in file.channels)
            {
                if (nChannels++ == 0)
                    pixelType = channel.type;
                else
                {
                    // TODO: someday handle mixed types but seems like a
                    // bother...
                    if (pixelType != channel.type)
                        LOG_FATAL("ReadEXR() doesn't currently support images with multiple channel types.");
                }
                channelNames.Add(channel.name);
            }

            CHECK(pixelType == Exr.PixelType.Half || pixelType == Exr.PixelType.Float);
            Image image = new(pixelType == Exr.PixelType.Half ? PixelFormat.Half : PixelFormat.Float,
                              new Point2i(width, height), channelNames.ToArray());
            // The file's pixels are channel-interleaved in channel order, as
            // the image's are.
            if (pixelType == Exr.PixelType.Half)
                file.ReadPixels(System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.p16.AsSpan()));
            else
                file.ReadPixels(System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.p32.AsSpan()));

            LOG_VERBOSE($"Read EXR image {name} ({width} x {height})");
            return new ImageAndMetadata(image, metadata);
        }
        catch (Exr.ExrException e)
        {
            ErrorExit($"Unable to read image file \"{name}\": {e.Message}");
        }
        catch (IOException e)
        {
            ErrorExit($"Unable to read image file \"{name}\": {e.Message}");
        }

        return null!;
    }

    private bool WriteEXR(string name, ImageMetadata metadata)
    {
        if (Is8Bit(format))
            return ConvertToFormat(PixelFormat.Half).WriteEXR(name, metadata);
        CHECK(Is16Bit(format) || Is32Bit(format));

        try
        {
            Bounds2i displayWindow, dataWindow;
            if (metadata.fullResolution.HasValue)
                displayWindow = new Bounds2i(new Point2i(0, 0), metadata.fullResolution.Value);
            else
                displayWindow = new Bounds2i(new Point2i(0, 0), resolution);

            if (metadata.pixelBounds.HasValue)
                dataWindow = metadata.pixelBounds.Value;
            else
                dataWindow = new Bounds2i(new Point2i(0, 0), resolution);

            Exr.Writer header = new(displayWindow, dataWindow,
                                    Is16Bit(format) ? Exr.PixelType.Half : Exr.PixelType.Float, channelNames);

            if (metadata.renderTimeSeconds.HasValue)
                header.InsertFloat("renderTimeSeconds", metadata.renderTimeSeconds.Value);
            if (metadata.cameraFromWorld.HasValue)
                header.InsertM44f("worldToCamera", metadata.cameraFromWorld.Value);
            if (metadata.NDCFromWorld.HasValue)
                header.InsertM44f("worldToNDC", metadata.NDCFromWorld.Value);
            if (metadata.samplesPerPixel.HasValue)
                header.InsertInt("samplesPerPixel", metadata.samplesPerPixel.Value);
            if (metadata.MSE.HasValue)
                header.InsertFloat("MSE", metadata.MSE.Value);
            foreach (var (k, v) in metadata.strings)
                header.InsertString(k, v);
            foreach (var (k, v) in metadata.stringVectors)
                header.InsertStringVector(k, v);

            // The OpenEXR spec says that the default is sRGB if no
            // chromaticities are provided.  It should be innocuous to write
            // the sRGB primaries anyway, but for completely indecipherable
            // reasons, OSX's Preview.app decides to gamma correct the pixels
            // in EXR files if it finds primaries.  So, we don't write them in
            // that case in the interests of nicer looking images on the
            // screen.
            if (metadata.GetColorSpace() != RGBColorSpace.sRGB)
            {
                RGBColorSpace cs = metadata.GetColorSpace();
                header.InsertChromaticities(cs.r, cs.g, cs.b, cs.w);
            }

            byte[] contents = Is16Bit(format)
                ? header.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(p16.AsSpan()))
                : header.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(p32.AsSpan()));
            if (!WriteFileContents(name, contents))
                return false;
        }
        catch (Exr.ExrException exc)
        {
            Error($"{name}: error writing EXR: {exc.Message}");
            return false;
        }

        return true;
    }

    ///////////////////////////////////////////////////////////////////////////
    // PNG Function Definitions

    private static ImageAndMetadata ReadPNG(string name, ColorEncoding? encoding)
    {
        byte[] contents = ReadFileContents(name);

        encoding ??= ColorEncoding.sRGB;

        Png.Info info = default;
        try
        {
            info = Png.Inspect(contents);
        }
        catch (PngException e)
        {
            ErrorExit($"{name}: {e.Message}");
        }
        int width = info.Width, height = info.Height;

        Image image;
        try
        {
            switch (info.ColorType)
            {
                case PngColorType.Grey:
                case PngColorType.GreyAlpha:
                {
                    int bpp = info.BitDepth == 16 ? 16 : 8;
                    // (pbrt asks lodepng for 16-bit grey here, which lodepng
                    // can't convert to, so pbrt fails on 16-bit grey+alpha and
                    // on 16-bit grey with tRNS. 16-bit RGB conversion works for
                    // all of them and repeats the grey value, so this decodes
                    // to RGB and uses R.)
                    int outStride = info.BitDepth == 16 ? 6 : 1;
                    byte[] buf = Png.Decode(contents, info.BitDepth == 16 ? PngColorType.RGB : PngColorType.Grey, bpp);

                    if (info.BitDepth == 16)
                    {
                        image = new Image(PixelFormat.Half, new Point2i(width, height), ["Y"]);
                        int bufIter = 0;
                        for (int y = 0; y < height; ++y)
                            for (int x = 0; x < width; ++x, bufIter += outStride)
                            {
                                // Convert from big endian.
                                Float v = ((buf[bufIter] << 8) + buf[bufIter + 1]) / 65535f;
                                v = encoding.ToFloatLinear(v);
                                image.SetChannel(new Point2i(x, y), 0, v);
                            }
                        DCHECK(bufIter == buf.Length);
                    }
                    else
                        image = new Image(buf, new Point2i(width, height), ["Y"], encoding);
                    return new ImageAndMetadata(image, new ImageMetadata());
                }
                default:
                {
                    int bpp = info.BitDepth == 16 ? 16 : 8;
                    bool hasAlpha = (info.ColorType == PngColorType.RGBA);
                    // Force RGB if it's paletted or whatever.
                    byte[] buf = Png.Decode(contents, hasAlpha ? PngColorType.RGBA : PngColorType.RGB, bpp);

                    ImageMetadata metadata = new();
                    metadata.colorSpace = RGBColorSpace.sRGB;
                    if (info.BitDepth == 16)
                    {
                        int nc = hasAlpha ? 4 : 3;
                        image = new Image(PixelFormat.Half, new Point2i(width, height),
                                          hasAlpha ? ["R", "G", "B", "A"] : ["R", "G", "B"]);
                        int bufIter = 0;
                        for (int y = 0; y < height; ++y)
                            for (int x = 0; x < width; ++x)
                                for (int c = 0; c < nc; ++c, bufIter += 2)
                                {
                                    DCHECK(bufIter < buf.Length);
                                    // Convert from big endian.
                                    Float v = ((buf[bufIter] << 8) + buf[bufIter + 1]) / 65535f;
                                    v = encoding.ToFloatLinear(v);
                                    image.SetChannel(new Point2i(x, y), c, v);
                                }
                        DCHECK(bufIter == buf.Length);
                    }
                    else if (hasAlpha)
                        image = new Image(buf, new Point2i(width, height), ["R", "G", "B", "A"], encoding);
                    else
                        image = new Image(buf, new Point2i(width, height), ["R", "G", "B"], encoding);
                    return new ImageAndMetadata(image, metadata);
                }
            }
        }
        catch (Exception e) when (e is PngException or InvalidDataException)
        {
            ErrorExit($"{name}: {e.Message}");
            return null!;
        }
    }

    private byte[] QuantizePixelsToU256(ref int nOutOfGamut)
    {
        byte[] u256 = new byte[NChannels() * resolution.x * resolution.y];
        for (int y = 0; y < resolution.y; ++y)
            for (int x = 0; x < resolution.x; ++x)
                for (int c = 0; c < NChannels(); ++c)
                {
                    Float dither = -.5f + BlueNoise(c, new Point2i(x, y));
                    Float v = GetChannel(new Point2i(x, y), c);
                    if (v < 0 || v > 1)
                        ++nOutOfGamut;
                    u256[NChannels() * (y * resolution.x + x) + c] = LinearToSRGB8(v, dither);
                }
        return u256;
    }

    private bool WritePNG(string filename, ImageMetadata metadata)
    {
        int nOutOfGamut = 0;

        PngColorType pngColor = PngColorType.Grey;
        switch (NChannels())
        {
            case 1:
                pngColor = PngColorType.Grey;
                break;
            case 3:
                // TODO: it would be nice to store the color encoding used in the PNG metadata...
                pngColor = PngColorType.RGB;
                break;
            case 4:
                pngColor = PngColorType.RGBA;
                break;
            default:
                LOG_FATAL("Unexpected number of channels in WritePNG()");
                break;
        }

        byte[] png;
        if (format == PixelFormat.U256)
            png = Png.Encode(p8, resolution.x, resolution.y, pngColor);
        else
        {
            byte[] pix8 = QuantizePixelsToU256(ref nOutOfGamut);
            png = Png.Encode(pix8, resolution.x, resolution.y, pngColor);
        }

        if (nOutOfGamut > 0)
            Warning($"{filename}: {nOutOfGamut} out of gamut pixel channels clamped to [0,1].");

        if (!WriteFileContents(filename, png))
        {
            Error($"{filename}: error writing PNG.");
            return false;
        }

        return true;
    }

    private bool WriteQOI(string filename, ImageMetadata metadata)
    {
        Image image = this;

        if (Encoding() != null && Encoding() is not LinearColorEncoding &&
            Encoding() is not sRGBColorEncoding)
        {
            Error($"{Encoding()}: only linear and sRGB encodings are supported by QOI.");  // filename);
            return false;
        }
        Qoi.Desc desc = new(resolution.x, resolution.y, NChannels(),
                            Encoding() is LinearColorEncoding ? Qoi.QOI_LINEAR : Qoi.QOI_SRGB);

        if (NChannels() == 4)
        {
            // Try to order it as QOI expects. Though continue on if we don't
            // find these particular channel names.a
            ImageChannelDesc cdesc = GetChannelDesc(["R", "G", "B", "A"]);
            if (cdesc)
                image = SelectChannels(cdesc);
        }
        else if (NChannels() == 3)
        {
            // Similarly try to get the channels in order..
            ImageChannelDesc cdesc = GetChannelDesc(["R", "G", "B"]);
            if (cdesc)
                image = SelectChannels(cdesc);
        }
        else
        {
            Error($"{filename}: only 3 and 4 channel images are supported for QOI");
            return false;
        }

        byte[]? qoiPixels;
        if (format == PixelFormat.U256)
            qoiPixels = Qoi.Encode(image.p8, desc);
        else
        {
            int nOutOfGamut = 0;
            byte[] rgba8 = image.QuantizePixelsToU256(ref nOutOfGamut);
            if (nOutOfGamut > 0)
                Warning($"{filename}: {nOutOfGamut} out of gamut pixel channels clamped to [0,1].");

            qoiPixels = Qoi.Encode(rgba8, desc);
        }

        bool success = qoiPixels != null && WriteFileContents(filename, qoiPixels);
        if (!success)
            Error($"{filename}: error writing QOI file.");

        return success;
    }

    ///////////////////////////////////////////////////////////////////////////
    // PFM Function Definitions

    /*
     * PFM reader/writer code courtesy Jiawen "Kevin" Chen
     * (http://people.csail.mit.edu/jiawen/)
     */

    private static bool hostLittleEndian => BitConverter.IsLittleEndian;

    private const int BUFFER_SIZE = 80;

    private static bool isWhitespace(byte c) => c == ' ' || c == '\n' || c == '\t';

    // Reads a "word" from the data and returns it, i.e. it keeps reading
    // until whitespace is reached (consuming that whitespace character).
    // Returns null on an error.
    private static string? readWord(byte[] data, ref int pos)
    {
        int n = 0;
        int start = pos;
        while (pos < data.Length && !isWhitespace(data[pos]) && n < BUFFER_SIZE)
        {
            ++n;
            ++pos;
        }

        if (n < BUFFER_SIZE)
        {
            string word = System.Text.Encoding.ASCII.GetString(data, start, n);
            if (pos < data.Length)
                ++pos;  // the whitespace character that ended the word
            return word;
        }

        return null;
    }

    private static ImageAndMetadata ReadPFM(string filename)
    {
        int nChannels, width = 0, height = 0;
        float scale = 0;
        ImageMetadata metadata = new();

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filename);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorExit($"{filename}: unable to open PFM file");
            return null!;
        }
        int pos = 0;

        // read either "Pf" or "PF"
        string? buffer = readWord(data, ref pos);
        if (buffer == null)
            ErrorExit($"{filename}: unable to read PFM file");

        if (buffer == "Pf")
            nChannels = 1;
        else if (buffer == "PF")
            nChannels = 3;
        else
        {
            ErrorExit($"{filename}: unable to decode PFM file type \"{buffer[..Math.Min(2, buffer.Length)]}\"");
            return null!;
        }

        // read the rest of the header
        // read width
        if ((buffer = readWord(data, ref pos)) == null)
            goto fail;
        if (!int.TryParse(buffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out width))
            ErrorExit($"{filename}: unable to decode width \"{buffer}\"");

        // read height
        if ((buffer = readWord(data, ref pos)) == null)
            goto fail;
        if (!int.TryParse(buffer, NumberStyles.Integer, CultureInfo.InvariantCulture, out height))
            ErrorExit($"{filename}: unable to decode height \"{buffer}\"");

        // read scale
        if ((buffer = readWord(data, ref pos)) == null)
            goto fail;
        if (!float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
            ErrorExit($"{filename}: unable to decode scale \"{buffer}\"");

        {
            // read the data
            int nFloats = nChannels * width * height;
            float[] rgb32 = new float[nFloats];
            for (int y = height - 1; y >= 0; --y)
            {
                int nBytes = 4 * nChannels * width;
                if (pos + nBytes > data.Length)
                    goto fail;
                Buffer.BlockCopy(data, pos, rgb32, 4 * nChannels * y * width, nBytes);
                pos += nBytes;
            }

            // apply endian conversian and scale if appropriate
            bool fileLittleEndian = (scale < 0f);
            if (hostLittleEndian ^ fileLittleEndian)
            {
                Span<uint> bits = System.Runtime.InteropServices.MemoryMarshal.Cast<float, uint>(rgb32.AsSpan());
                System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(bits, bits);
            }
            if (MathF.Abs(scale) != 1f)
                for (int i = 0; i < nFloats; ++i)
                    rgb32[i] *= MathF.Abs(scale);

            LOG_VERBOSE($"Read PFM image {filename} ({width} x {height})");
            metadata.colorSpace = RGBColorSpace.sRGB;
            if (nChannels == 1)
                return new ImageAndMetadata(new Image(rgb32, new Point2i(width, height), ["Y"]), metadata);
            else
                return new ImageAndMetadata(new Image(rgb32, new Point2i(width, height), ["R", "G", "B"]),
                                            metadata);
        }

    fail:
        ErrorExit($"{filename}: premature end of file in PFM file");
        return null!;
    }

    private static ImageAndMetadata ReadHDR(string filename)
    {
        float[] pixels;
        int x, y;
        try
        {
            pixels = Hdr.Load(ReadFileContents(filename), out x, out y);
        }
        catch (Hdr.HdrException e)
        {
            ErrorExit($"{filename}: {e.Message}");
            return null!;
        }
        // (stbi_loadf() returns 3 channels for HDR files.)
        int n = 3;
        CHECK_EQ(x * y * n, pixels.Length);

        switch (n)
        {
            case 1:
                return new ImageAndMetadata(new Image(pixels, new Point2i(x, y), ["Y"]), new ImageMetadata());
            case 2:
            {
                Image image = new(pixels, new Point2i(x, y), ["Y", "A"]);
                return new ImageAndMetadata(image.SelectChannels(image.GetChannelDesc(["Y"])),
                                            new ImageMetadata());
            }
            case 3:
                return new ImageAndMetadata(new Image(pixels, new Point2i(x, y), ["R", "G", "B"]),
                                            new ImageMetadata());
            case 4:
            {
                Image image = new(pixels, new Point2i(x, y), ["R", "G", "B", "A"]);
                return new ImageAndMetadata(
                    image.SelectChannels(image.GetChannelDesc(["R", "G", "B"])), new ImageMetadata());
            }
            default:
                ErrorExit($"{filename}: {n} channel image unsupported.");
                return null!;
        }
    }

    private static ImageAndMetadata ReadQOI(string filename)
    {
        byte[] contents = ReadFileContents(filename);
        byte[]? pixels = Qoi.Decode(contents, out Qoi.Desc desc, 0 /* channels */);
        CHECK(pixels != null);  // qoi failure

        ImageMetadata metadata = new();
        metadata.colorSpace = RGBColorSpace.sRGB;

        List<string> channelNames = ["R", "G", "B"];
        if (desc.channels == 4)
            channelNames.Add("A");
        else
            CHECK_EQ(3, desc.channels);

        CHECK(desc.colorspace == Qoi.QOI_SRGB || desc.colorspace == Qoi.QOI_LINEAR);
        ColorEncoding encoding =
            (desc.colorspace == Qoi.QOI_SRGB) ? ColorEncoding.sRGB : ColorEncoding.Linear;

        Image image = new(pixels, new Point2i(desc.width, desc.height), channelNames.ToArray(), encoding);

        return new ImageAndMetadata(image, metadata);
    }

    private bool WritePFM(string filename, ImageMetadata metadata)
    {
        if (NChannels() != 3)
        {
            Error($"{filename}: only 3-channel images are supported for PFM.");
            return false;
        }

        using MemoryStream fp = new();
        // only write 3 channel PFMs here...
        fp.Write("PF\n"u8);

        // write the width and height, which must be positive
        fp.Write(System.Text.Encoding.ASCII.GetBytes($"{resolution.x} {resolution.y}\n"));

        // write the scale, which encodes endianness
        float scale = hostLittleEndian ? -1f : 1f;
        fp.Write(System.Text.Encoding.ASCII.GetBytes(scale.ToString("F6", CultureInfo.InvariantCulture) + "\n"));

        // write the data from bottom left to upper right as specified by
        // http://netpbm.sourceforge.net/doc/pfm.html
        // The raster is a sequence of pixels, packed one after another, with no
        // delimiters of any kind. They are grouped by row, with the pixels in each
        // row ordered left to right and the rows ordered bottom to top.
        float[] scanline = new float[3 * resolution.x];
        for (int y = resolution.y - 1; y >= 0; y--)
        {
            for (int x = 0; x < resolution.x; ++x)
            {
                if (NChannels() == 1)
                {
                    Float v = GetChannel(new Point2i(x, y), 0);
                    scanline[3 * x] = scanline[3 * x + 1] = scanline[3 * x + 2] = (float)v;
                }
                else
                {
                    CHECK_EQ(3, NChannels());
                    for (int c = 0; c < 3; ++c)
                        // Assign element-wise in case Float is typedefed as
                        // 'double'.
                        scanline[3 * x + c] = (float)GetChannel(new Point2i(x, y), c);
                }
            }
            fp.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(scanline.AsSpan()));
        }

        if (!WriteFileContents(filename, fp.GetBuffer().AsSpan(0, (int)fp.Length)))
        {
            Error($"Error writing PFM file \"{filename}\"");
            return false;
        }
        return true;
    }
}

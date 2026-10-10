// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cmd/imgtool.cpp. The GPU-only denoise-optix and
// scale-optix commands aren't ported.

using System.Text;
using Pbrt.ImgTool.SkyModel;
using static Pbrt.ImgTool.CStd;

namespace Pbrt.ImgTool;

public static partial class Program
{
    // printf()/Printf() output. (Flushed before exiting.)
    private static readonly StreamWriter stdout =
        new(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };

    private static void exit(int code)
    {
        stdout.Flush();
        Environment.Exit(code);
    }

    // (*iter)[0] == '-'
    private static bool IsFlag(string s) => s.Length > 0 && s[0] == '-';

    private static void usage(string cmd, string? msg = null)
    {
        stdout.Flush();
        if (msg != null)
            Console.Error.Write($"imgtool {cmd}: {msg}\n\n");

        CHECK(commandUsage.ContainsKey(cmd));
        CommandUsage u = commandUsage[cmd];
        Console.Error.Write($"usage: imgtool {u.usage}\n\n");
        if (u.options.Length > 0)
            Console.Error.Write($"options:{u.options}\n");

        exit(1);
    }

    private static void help()
    {
        Console.Error.Write("usage: imgtool <command> [options]\n\n");
        Console.Error.Write("where <command> is:\n\n");
        foreach (var (name, u) in commandUsage)
            Console.Error.Write($"{name}: {u.description}\n\n");
        Console.Error.Write("\n");
        Console.Error.Write("\"imgtool help <command>\" provides detailed information about <command>.\n");
    }

    private static int help(List<string> args)
    {
        if (args.Count == 0)
        {
            help();
            return 0;
        }
        foreach (string cmd in args)
        {
            if (!commandUsage.TryGetValue(cmd, out CommandUsage u))
            {
                Console.Error.Write($"imgtool help: command \"{cmd}\" not known.\n");
                help();
                return 1;
            }
            else
            {
                Console.Error.Write($"usage: imgtool {u.usage}\n\n");
                Console.Error.Write($"options:{u.options}\n");
            }
        }
        return 0;
    }

    private static Float[] Values(in ImageChannelValues v)
    {
        Float[] a = new Float[v.Size];
        for (int i = 0; i < a.Length; ++i)
            a[i] = v[i];
        return a;
    }

    // Mul<RGB>(m, v)
    private static RGB Mul(in SquareMatrix3 m, in ImageChannelValues v) => m * new RGB(v[0], v[1], v[2]);

    private static bool checkImageCompatibility(string fn1, Image im1, RGBColorSpace cs1, string fn2, Image im2,
                                                RGBColorSpace cs2)
    {
        if (im1.Resolution() != im2.Resolution())
        {
            Error($"{fn1}: image resolution ({im1.Resolution().x}, {im1.Resolution().y}) doesn't match \"{fn2}\" " +
                  $"({im2.Resolution().x}, {im2.Resolution().y}).");
            return false;
        }
        if (im1.NChannels() != im2.NChannels())
        {
            Error($"{fn1}: image channel count {im1.NChannels()} doesn't match \"{fn2}\", {im2.NChannels()}.");
            return false;
        }
        if (!im1.ChannelNames().SequenceEqual(im2.ChannelNames()))
        {
            static string print(List<string> n) => string.Join(", ", n);
            Error($"{fn1}: warning: image channel names \"{print(im1.ChannelNames())}\" don't match \"{fn2}\" " +
                  $"with \"{print(im2.ChannelNames())}\".");
            return false;
        }

        if (cs1 != cs2)
        {
            Error($"{fn1}: color space does not match \"{fn2}\".");
            return false;
        }

        return true;
    }

    private static int makesky(List<string> args)
    {
        string outfile = "";
        Float albedo = 0.5f;
        Float turbidity = 3f;
        Float elevation = 10;
        int resolution = 2048;

        static void onError(string err)
        {
            usage("makesky", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outfile, onError) ||
                ParseArg(args, ref iter, "albedo", ref albedo, onError) ||
                ParseArg(args, ref iter, "turbidity", ref turbidity, onError) ||
                ParseArg(args, ref iter, "elevation", ref elevation, onError) ||
                ParseArg(args, ref iter, "resolution", ref resolution, onError))
            {
                // success
            }
            else
                onError($"argument {args[iter]} invalid");
        }

        if (outfile.Length == 0)
            usage("makesky", "--outfile must be specified");
        if (albedo < 0.0 || albedo > 1.0)
            usage("makesky", "--albedo must be between 0 and 1");
        if (turbidity < 1.7 || turbidity > 10.0)
            usage("makesky", "--turbidity must be between 1.7 and 10.");
        if (elevation < 0.0 || elevation > 90.0)
            usage("makesky", "--elevation must be between 0. and 90.");
        elevation = Radians(elevation);
        if (resolution < 1)
            usage("makesky", "--resolution must be >= 1");

        // Vector pointing at the sun. Note that elevation is measured from the
        // horizon--not the zenith, as it is elsewhere in pbrt.
        Vector3f sunDir = new(0, FMath.Cos(elevation), FMath.Sin(elevation));

        Image img = new(PixelFormat.Float, new Point2i(resolution, resolution), ["R", "G", "B"]);

        // They assert wavelengths are in this range...
        int nLambda = 1 + (720 - 320) / 32;
        Float[] lambda = new Float[nLambda];
        for (int i = 0; i < nLambda; ++i)
            lambda[i] = Lerp(i / (Float)(nLambda - 1), 320, 720);

        // Assume a uniform spectral albedo
        ArHosekSkyModelState skymodel_state =
            ArHosekSkyModel.arhosekskymodelstate_alloc_init(elevation, turbidity, albedo);

        RGBColorSpace colorSpace = RGBColorSpace.ACES2065_1;

        ParallelFor(0, resolution, (long start, long end) =>
        {
            Float[] skyv = new Float[lambda.Length];
            for (long iy = start; iy < end; ++iy)
            {
                Float y = (iy + 0.5f) / resolution;
                for (int ix = 0; ix < resolution; ++ix)
                {
                    Float x = (ix + 0.5f) / resolution;
                    Vector3f v = EqualAreaSquareToSphere(new Point2f(x, y));
                    if (v.z <= 0)
                        // downward hemisphere
                        continue;

                    Float theta = SphericalTheta(v);

                    // Compute the angle between the pixel's direction and the sun
                    // direction.
                    Float gamma = SafeACos(Dot(v, sunDir));
                    DCHECK(gamma >= 0 && gamma <= Pi);

                    for (int i = 0; i < lambda.Length; ++i)
                        skyv[i] = (Float)ArHosekSkyModel.arhosekskymodel_solar_radiance(skymodel_state, theta, gamma,
                                                                                         lambda[i]);

                    PiecewiseLinearSpectrum spec = new(lambda, skyv);
                    XYZ xyz = SpectrumToXYZ(spec);
                    RGB rgb = colorSpace.ToRGB(xyz);

                    for (int c = 0; c < 3; ++c)
                        img.SetChannel(new Point2i(ix, (int)iy), c, rgb[c]);
                }
            }
        });

        ImageMetadata metadata = new();
        metadata.colorSpace = colorSpace;
        // (std::to_string() formats floats with "%f".)
        metadata.stringVectors["makesky.albedo"] = [F(albedo)];
        metadata.stringVectors["makesky.elevation"] = [F(elevation)];
        metadata.stringVectors["makesky.turbidity"] = [F(turbidity)];
        CHECK(img.Write(outfile, metadata));

        return 0;
    }

    private static int assemble(List<string> args)
    {
        if (args.Count == 0)
            usage("assemble", "no filenames provided to \"assemble\"?");
        string outfile = "";
        List<string> infiles = [];

        static void onError(string err) => usage("assemble", err);
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outfile, onError))
            {
                // success
            }
            else if (IsFlag(args[iter]))
                usage("assemble", $"{args[iter]}: unknown command flag");
            else
                infiles.Add(args[iter]);
        }

        if (outfile.Length == 0)
            usage("assemble", "--outfile not provided for \"assemble\"");

        Image fullImage = new();
        bool[] seenPixel = [];
        int seenMultiple = 0;
        Bounds2i fullBounds = new();
        RGBColorSpace? colorSpace = null;
        foreach (string file in infiles)
        {
            if (!HasExtension(file, "exr"))
                usage("assemble", "only EXR images include the image bounding boxes that \"assemble\" needs.");

            ImageAndMetadata im = Image.Read(file);
            Image image = im.image;
            ImageMetadata metadata = im.metadata;

            if (metadata.fullResolution is not Point2i metadataFullResolution)
            {
                Error($"{file}: doesn't have full resolution in image metadata. Skipping.");
                continue;
            }
            if (metadata.pixelBounds is not Bounds2i metadataPixelBounds)
            {
                Error($"{file}: doesn't have pixel bounds in image metadata. Skipping.");
                continue;
            }

            if (fullImage.Resolution() == new Point2i(0, 0))
            {
                // First image read.
                fullImage = new Image(image.Format(), metadataFullResolution, image.ChannelNames().ToArray(),
                                      image.Encoding());
                colorSpace = metadata.GetColorSpace();
                seenPixel = new bool[fullImage.Resolution().x * fullImage.Resolution().y];
                fullBounds = new Bounds2i(new Point2i(0, 0), fullImage.Resolution());
            }
            else
            {
                // Make sure that this image's info is compatible with the
                // first image's.
                if (metadataFullResolution != fullImage.Resolution())
                {
                    Warning($"{file}: full resolution ({metadataFullResolution.x}, {metadataFullResolution.y}) in " +
                            "EXR file doesn't match the full resolution of first EXR file " +
                            $"({fullImage.Resolution().x}, {fullImage.Resolution().y}). Ignoring this file.");
                    continue;
                }
                if (Union(metadataPixelBounds, fullBounds) != fullBounds)
                {
                    Warning($"{file}: pixel bounds ({metadataPixelBounds.pMin.x}, {metadataPixelBounds.pMin.y}) - " +
                            $"({metadataPixelBounds.pMax.x}, {metadataPixelBounds.pMax.y}) in EXR file isn't inside " +
                            $"the full image (0, 0) - ({fullBounds.pMax.x}, {fullBounds.pMax.y}). Ignoring this file.");
                    continue;
                }
                if (fullImage.NChannels() != image.NChannels())
                {
                    Warning($"{file}: {image.NChannels()} channel image; expecting {fullImage.NChannels()} channels.");
                    continue;
                }
                RGBColorSpace cs = metadata.GetColorSpace();
                if (cs != colorSpace)
                {
                    Warning($"{file}: color space ({cs}) doesn't match first image's color space ({colorSpace}).");
                    continue;
                }
            }

            // Copy pixels.
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Point2i fullp = new(x + metadataPixelBounds.pMin.x, y + metadataPixelBounds.pMin.y);
                    CHECK(InsideExclusive(fullp, fullBounds));
                    int fullOffset = fullp.x + fullp.y * fullImage.Resolution().x;
                    if (seenPixel[fullOffset])
                        ++seenMultiple;
                    seenPixel[fullOffset] = true;
                    for (int c = 0; c < fullImage.NChannels(); ++c)
                        fullImage.SetChannel(fullp, c, image.GetChannel(new Point2i(x, y), c));
                }
        }

        int unseenPixels = 0;
        for (int y = 0; y < fullImage.Resolution().y; ++y)
            for (int x = 0; x < fullImage.Resolution().x; ++x)
                if (!seenPixel[y * fullImage.Resolution().x + x])
                    ++unseenPixels;

        if (seenMultiple > 0)
            Warning($"{outfile}: {seenMultiple} pixels present in multiple images.");
        if (unseenPixels > 0)
            Warning($"{outfile}: {unseenPixels} pixels not present in any images.");

        ImageMetadata outMetadata = new();
        outMetadata.colorSpace = colorSpace;

        fullImage.Write(outfile, outMetadata);

        return 0;
    }

    private static int splitn(List<string> args)
    {
        if (args.Count == 0)
            usage("splitn", "no filenames provided to \"splitn\"?");
        string outfile = "";
        List<string> infiles = [];
        List<Bounds2i> crops = [];
        int cropSize = 96;

        string crop = "";
        static void onError(string err) => usage("splitn", err);
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outfile, onError) ||
                ParseArg(args, ref iter, "cropsize", ref cropSize, onError))
            {
                // success
            }
            else if (ParseArg(args, ref iter, "crop", ref crop, onError))
            {
                List<int> c = SplitStringToInts(crop, ',');
                if (c.Count != 2)
                    usage("splitn", "2 values not provided to --crop");
                crops.Add(new Bounds2i(new Point2i(c[0], c[1]), new Point2i(c[0] + cropSize, c[1] + cropSize)));
            }
            else if (IsFlag(args[iter]))
                usage("splitn", $"{args[iter]}: unknown command flag");
            else
                infiles.Add(args[iter]);
        }

        if (outfile.Length == 0)
            usage("splitn", "--outfile not provided for \"splitn\"");

        List<Image> images = [];
        RGBColorSpace? colorSpace = null;
        foreach (string file in infiles)
        {
            ImageAndMetadata im = Image.Read(file);
            Image image = im.image;

            if (colorSpace == null)
                colorSpace = im.metadata.GetColorSpace();
            else if (!checkImageCompatibility(infiles[0], images[0], colorSpace, file, image,
                                              im.metadata.GetColorSpace()))
                return 0;  // (pbrt returns false here.)

            images.Add(image);
        }

        Image result = new(images[0].Format(), images[0].Resolution(), images[0].ChannelNames().ToArray(),
                           images[0].Encoding());
        float m = 15f;
        int pad = 6;
        for (int y = 0; y < result.Resolution().y; ++y)
        {
            int x = 0;
            for (int im = 0; im < images.Count; ++im)
            {
                if (im == images.Count - 1)
                    for (; x < result.Resolution().x; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, images[im].GetChannel(new Point2i(x, y), c));
                else
                {
                    int x1 = (int)((float)(im + 1) / (float)images.Count * result.Resolution().x +
                                   (2 * ((float)y / (float)result.Resolution().y) - 1) * result.Resolution().x / -m);

                    for (; x < x1 - pad / 2; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, images[im].GetChannel(new Point2i(x, y), c));
                    for (; x < x1 + pad / 2; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, 0);
                }
            }
        }

        // Crops
        RGB[] edges = [new RGB((Float)0.8, (Float).15, (Float).15), new RGB((Float).15, (Float)0.8, (Float).15),
                        new RGB((Float).15, (Float).15, (Float)0.8)];
        int borderWidth = 5;
        int xCropRes = (cropSize + 2 * borderWidth) * images.Count + pad * (images.Count - 1);
        int yCropRes = (cropSize + 2 * borderWidth) * crops.Count + pad * (crops.Count - 1);
        if (xCropRes < 1)
            xCropRes = 1;
        if (yCropRes < 1)
            yCropRes = 1;
        Image cropsImage = new(PixelFormat.Float, new Point2i(xCropRes, yCropRes), ["R", "G", "B"]);
        for (int y = 0; y < yCropRes; ++y)
            for (int x = 0; x < xCropRes; ++x)
                for (int c = 0; c < 3; ++c)
                    cropsImage.SetChannel(new Point2i(x, y), c, 1f);

        static void drawBox(Bounds2i b, int borderWidth, RGB rgb, Image result)
        {
            ImageChannelDesc desc = result.GetChannelDesc(["R", "G", "B"]);
            CHECK((bool)desc);
            Float[] v = [rgb.r, rgb.g, rgb.b];

            for (int y = b.pMin.y - borderWidth; y < b.pMin.y; ++y)
                for (int x = b.pMin.x - borderWidth; x < b.pMax.x + borderWidth; ++x)
                    for (int ch = 0; ch < 3; ++ch)
                        result.SetChannels(new Point2i(x, y), desc, v);
            for (int y = b.pMax.y; y < b.pMax.y + borderWidth; ++y)
                for (int x = b.pMin.x - borderWidth; x < b.pMax.x + borderWidth; ++x)
                    for (int ch = 0; ch < 3; ++ch)
                        result.SetChannels(new Point2i(x, y), desc, v);
            for (int x = b.pMin.x - borderWidth; x < b.pMin.x; ++x)
                for (int y = b.pMin.y; y < b.pMax.y; ++y)
                    for (int ch = 0; ch < 3; ++ch)
                        result.SetChannels(new Point2i(x, y), desc, v);
            for (int x = b.pMax.x; x < b.pMax.x + borderWidth; ++x)
                for (int y = b.pMin.y; y < b.pMax.y; ++y)
                    for (int ch = 0; ch < 3; ++ch)
                        result.SetChannels(new Point2i(x, y), desc, v);
        }

        CHECK_LE(crops.Count, edges.Length);
        ImageChannelDesc cropDesc = cropsImage.GetChannelDesc(["R", "G", "B"]);
        CHECK((bool)cropDesc);

        for (int c = 0; c < crops.Count; ++c)
        {
            Bounds2i cb = crops[c];
            drawBox(cb, borderWidth, edges[c], result);

            // extract crops from images
            for (int i = 0; i < images.Count; ++i)
            {
                // output start point
                int y0 = c * (cropSize + 2 * borderWidth + pad);
                int x0 = i * (cropSize + 2 * borderWidth + pad);
                drawBox(new Bounds2i(new Point2i(x0 + borderWidth, y0 + borderWidth),
                                     new Point2i(x0 + borderWidth + cropSize, y0 + borderWidth + cropSize)),
                        borderWidth, edges[c], cropsImage);

                ImageChannelDesc imageDesc = images[i].GetChannelDesc(["R", "G", "B"]);
                CHECK((bool)imageDesc);
                foreach (Point2i p in cb)
                {
                    Point2i pOut = new Point2i(x0 + borderWidth, y0 + borderWidth) + (p - cb.pMin);
                    cropsImage.SetChannels(pOut, cropDesc, Values(images[i].GetChannels(p, imageDesc)));
                }
            }
        }

        // TODO: we could try to be more clever about carrying through more
        // metadata, when applicable.
        ImageMetadata outMetadata = new();
        outMetadata.colorSpace = colorSpace;

        result.Write(outfile, outMetadata);
        if (crops.Count > 0)
            cropsImage.Write("crops-" + outfile, outMetadata);

        return 0;
    }

    private static int scalenormalmap(List<string> args)
    {
        string filename = "", outfile = "";
        Float scale = 1;

        // (pbrt reports errors here as coming from the "error" command.)
        static void onError(string err)
        {
            usage("error", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, "outfile", ref outfile, onError))
            {
                // success
            }
            else if (filename.Length == 0 && !IsFlag(args[iter]))
                filename = args[iter];
            else
                usage("error", $"{args[iter]}: unknown argument");
        }

        if (filename.Length == 0)
            usage("error", "Must provide base filename.");

        ImageAndMetadata im = Image.Read(filename);
        Image image = im.image;
        ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
        if (!rgbDesc)
        {
            Error($"{filename}: doesn't have R, G, B channels.");
            return 1;
        }

        Image scaledImage = image.Clone();
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                ImageChannelValues rgb = image.GetChannels(new Point2i(x, y), rgbDesc);
                for (int c = 0; c < 3; ++c)
                    rgb[c] = 2 * rgb[c] - 1;

                rgb[0] *= scale;
                rgb[1] *= scale;
                rgb[2] = SafeSqrt(1 - Sqr(rgb[0]) - Sqr(rgb[1]));

                for (int c = 0; c < 3; ++c)
                    rgb[c] = (rgb[c] + 1) / 2;

                scaledImage.SetChannels(new Point2i(x, y), rgbDesc, Values(rgb));
            }

        if (!scaledImage.Write(outfile, im.metadata))
            return 1;

        return 0;
    }

    private static int cat(List<string> args)
    {
        if (args.Count == 0)
            usage("cat", "no filenames provided to \"cat\"?");
        bool sort = false;
        bool csv = false;
        bool list = false;

        foreach (string arg in args)
        {
            if (arg == "--sort" || arg == "-sort")
            {
                sort = !sort;
                continue;
            }
            else if (arg == "--csv" || arg == "-csv")
            {
                csv = !csv;
                continue;
            }
            else if (arg == "--list" || arg == "-list")
            {
                list = !list;
                continue;
            }

            if (sort && csv)
            {
                stdout.Flush();
                Console.Error.Write("imgtool: --sort and --csv don't make sense to use together.\n");
                return 1;
            }
            if (sort && list)
            {
                stdout.Flush();
                Console.Error.Write("imgtool: --sort and --list don't make sense to use together.\n");
                return 1;
            }

            ImageAndMetadata im = Image.Read(arg);
            ImageMetadata metadata = im.metadata;
            Image image = im.image;

            Bounds2i pixelBounds = metadata.pixelBounds ?? new Bounds2i(new Point2i(0, 0), image.Resolution());
            if (sort)
            {
                List<(Point2i p, ImageChannelValues v)> sorted = new(pixelBounds.Area());
                foreach (Point2i p in pixelBounds)
                {
                    ImageChannelValues v = image.GetChannels(new Point2i(p.x - pixelBounds.pMin.x,
                                                                         p.y - pixelBounds.pMin.y));
                    sorted.Add((p, v));
                }

                Sort(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sorted),
                     (a, b) => a.v.Average() < b.v.Average());
                foreach (var (p, values) in sorted)
                {
                    if (!csv)
                        stdout.Write($"({p.x}, {p.y}): ");
                    for (int i = 0; i < values.Size; ++i)
                        stdout.Write($"{FloatToString(values[i])}{((i == values.Size - 1) ? '\n' : ',')}");
                }
            }
            else
            {
                if (list)
                {
                    CHECK_EQ(image.NChannels(), 1);
                    for (int y = pixelBounds.pMin.y; y < pixelBounds.pMax.y; ++y)
                    {
                        for (int x = pixelBounds.pMin.x; x < pixelBounds.pMax.x; ++x)
                            stdout.Write(F(image.GetChannel(new Point2i(x - pixelBounds.pMin.x,
                                                                        y - pixelBounds.pMin.y), 0)) + " ");
                        stdout.Write("\n");
                    }
                }
                else
                {
                    foreach (Point2i p in pixelBounds)
                    {
                        ImageChannelValues values = image.GetChannels(new Point2i(p.x - pixelBounds.pMin.x,
                                                                                  p.y - pixelBounds.pMin.y));
                        if (!csv)
                            stdout.Write($"({p.x}, {p.y}): ");
                        for (int i = 0; i < values.Size; ++i)
                            stdout.Write($"{FloatToString(values[i])}{((i == values.Size - 1) ? '\n' : ',')}");
                    }
                }
            }
        }
        return 0;
    }

    private static int average(List<string> args)
    {
        string avgFile = "", filenameBase = "";

        static void onError(string err)
        {
            usage("average", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref avgFile, onError))
            {
                // success
            }
            else if (filenameBase.Length == 0 && !IsFlag(args[iter]))
                filenameBase = args[iter];
            else
                usage("average", $"{args[iter]}: unknown argument");
        }

        if (filenameBase.Length == 0)
            usage("average", "must provide base filename.");
        if (avgFile.Length == 0)
            usage("average", "must provide --outfile.");

        List<string> filenames = MatchingFilenames(filenameBase);
        if (filenames.Count == 0)
        {
            Error($"{filenameBase}: no matching filenames!");
            return 1;
        }

        // Compute average image
        using ThreadLocal<Image> avgImages = new(() => new Image(), trackAllValues: true);
        using ThreadLocal<RGBColorSpace?> colorSpaces = new(() => null, trackAllValues: true);
        bool failed = false;

        ParallelFor(0, filenames.Count, (long i) =>
        {
            ImageAndMetadata imRead = Image.Read(filenames[(int)i]);
            Image im = imRead.image;

            Image avg = avgImages.Value!;
            if (avg.Resolution() == new Point2i(0, 0))
            {
                avg = avgImages.Value = new Image(PixelFormat.Float, im.Resolution(), im.ChannelNames().ToArray());
                colorSpaces.Value = imRead.metadata.GetColorSpace();
            }
            else if (!checkImageCompatibility(filenames[(int)i], im, imRead.metadata.GetColorSpace(), filenames[0],
                                              avg, colorSpaces.Value!))
            {
                Volatile.Write(ref failed, true);
                return;
            }

            for (int y = 0; y < avg.Resolution().y; ++y)
                for (int x = 0; x < avg.Resolution().x; ++x)
                    for (int c = 0; c < avg.NChannels(); ++c)
                    {
                        Float v = im.GetChannel(new Point2i(x, y), c) / filenames.Count;
                        if (Float.IsNaN(v))
                            LOG_FATAL($"NAN Pixel at {new Point2f(x, y)} in {filenames[(int)i]}");
                        if (Float.IsInfinity(v))
                            v = 0;
                        avg.SetChannel(new Point2i(x, y), c, avg.GetChannel(new Point2i(x, y), c) + v);
                    }
        });

        if (failed)
            return 1;

        // Average per-thread average images
        Image avgImage = new();
        foreach (Image im in avgImages.Values)
        {
            CHECK(im.Resolution() != new Point2i(0, 0));
            if (avgImage.Resolution() == new Point2i(0, 0))
            {
                // First valid one
                avgImage = im.Clone();
            }
            else
            {
                for (int y = 0; y < avgImage.Resolution().y; ++y)
                    for (int x = 0; x < avgImage.Resolution().x; ++x)
                        for (int c = 0; c < avgImage.NChannels(); ++c)
                        {
                            Float v = im.GetChannel(new Point2i(x, y), c);
                            if (!Float.IsInfinity(v))
                                avgImage.SetChannel(new Point2i(x, y), c, avgImage.GetChannel(new Point2i(x, y), c) + v);
                        }
            }
        }

        ImageMetadata avgMetadata = new();
        foreach (RGBColorSpace? cs in colorSpaces.Values)
        {
            // Will redundantly set it, but whatever.
            avgMetadata.colorSpace = cs;
        }

        if (!avgImage.Write(avgFile, avgMetadata))
            return 1;

        return 0;
    }

    // If the last 2 crop window values are negative, they're taken as
    // deltas; returns the crop bounds if one was given.
    private static Bounds2i? CropBounds(int[] cropWindow)
    {
        if (cropWindow[1] < 0)
            cropWindow[1] = cropWindow[0] - cropWindow[1];
        if (cropWindow[3] < 0)
            cropWindow[3] = cropWindow[2] - cropWindow[3];
        if (cropWindow[0] >= 0 && cropWindow[2] >= 0)
            return new Bounds2i(new Point2i(cropWindow[0], cropWindow[2]), new Point2i(cropWindow[1], cropWindow[3]));
        return null;
    }

    private static int error(List<string> args)
    {
        string referenceFile = "", errorFile = "", metric = "MSE";
        string filenameBase = "";
        int[] cropWindow = [-1, 0, -1, 0];

        static void onError(string err)
        {
            usage("error", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "reference", ref referenceFile, onError) ||
                ParseArg(args, ref iter, "errorfile", ref errorFile, onError) ||
                ParseArg(args, ref iter, "metric", ref metric, onError) ||
                ParseArg(args, ref iter, "crop", cropWindow.AsSpan(), onError))
            {
                // success
            }
            else if (filenameBase.Length == 0 && !IsFlag(args[iter]))
                filenameBase = args[iter];
            else
                usage("error", $"{args[iter]}: unknown argument");
        }

        if (filenameBase.Length == 0)
            usage("error", "Must provide base filename.");
        if (metric != "MSE" && metric != "MRSE" && metric != "MAE")
            usage("error", $"{metric}: --metric must be \"MAE\", \"MSE\", or \"MRSE\".");

        List<string> filenames = MatchingFilenames(filenameBase);
        if (filenames.Count == 0)
        {
            Error($"{filenameBase}: no matching filenames!");
            return 1;
        }

        if (referenceFile.Length == 0)
            usage("error", "must provide --reference file.");
        ImageAndMetadata @ref = Image.Read(referenceFile);
        Image referenceImage = @ref.image;

        Bounds2i? cropBounds = CropBounds(cropWindow);
        Image crop(Image image) => cropBounds is Bounds2i b ? image.Crop(b) : image;

        referenceImage = crop(referenceImage);

        // Compute error and error image
        int refXRes = referenceImage.Resolution().x, refYRes = referenceImage.Resolution().y;
        int refNc = referenceImage.NChannels();
        using ThreadLocal<VarianceEstimator<double>[]> pixelVariances =
            new(() => new VarianceEstimator<double>[refXRes * refYRes * refNc], trackAllValues: true);

        using ThreadLocal<double> sumErrors = new(() => 0, trackAllValues: true);
        int[] spp = new int[filenames.Count];
        ParallelFor(0, filenames.Count, (long i) =>
        {
            ImageAndMetadata imRead = Image.Read(filenames[(int)i]);
            Image im = crop(imRead.image);

            CHECK(imRead.metadata.samplesPerPixel.HasValue);
            spp[i] = imRead.metadata.samplesPerPixel!.Value;

            ImageChannelValues error;
            if (metric == "MAE")
                error = im.MAE(im.AllChannelsDesc(), referenceImage, out _);
            else if (metric == "MSE")
                error = im.MSE(im.AllChannelsDesc(), referenceImage, out _);
            else
                error = im.MRSE(im.AllChannelsDesc(), referenceImage, out _);
            sumErrors.Value += error.Average();

            VarianceEstimator<double>[] pixelVariance = pixelVariances.Value!;
            for (int y = 0; y < im.Resolution().y; ++y)
                for (int x = 0; x < im.Resolution().x; ++x)
                {
                    int offset = (y * refXRes + x) * refNc;
                    for (int c = 0; c < im.NChannels(); ++c)
                        if (metric == "MRSE")
                            pixelVariance[offset + c].Add(im.GetChannel(new Point2i(x, y), c) /
                                                          (0.01f + referenceImage.GetChannel(new Point2i(x, y), c)));
                        else
                            pixelVariance[offset + c].Add(im.GetChannel(new Point2i(x, y), c));
                }
        });

        for (int i = 1; i < filenames.Count; ++i)
        {
            if (spp[i] != spp[0])
            {
                Error($"{filenames[i]}: spp {spp[i]} mismatch. {filenames[0]} has {spp[0]}.");
                return 1;
            }
        }

        double sumError = 0.0;
        foreach (double err in sumErrors.Values)
            sumError += err;

        VarianceEstimator<double>[] pixelVarianceSum = new VarianceEstimator<double>[refXRes * refYRes * refNc];
        foreach (VarianceEstimator<double>[] pixVar in pixelVariances.Values)
            for (int i = 0; i < pixelVarianceSum.Length; ++i)
                pixelVarianceSum[i].Merge(pixVar[i]);

        Image errorImage = new(PixelFormat.Float, new Point2i(refXRes, refYRes), [metric]);
        for (int y = 0; y < refYRes; ++y)
            for (int x = 0; x < refXRes; ++x)
            {
                Float varSum = 0;
                for (int c = 0; c < refNc; ++c)
                    // (Float += double is computed in double precision.)
                    varSum = (Float)(varSum + pixelVarianceSum[(y * refXRes + x) * refNc + c].Variance());
                errorImage.SetChannel(new Point2i(x, y), 0, varSum / refNc);
            }

        // MSE is the average over all of the pixels
        double errorValue = sumError / (double)(ulong)(filenames.Count - 1);
        stdout.Write($"{metric} estimate = {G(errorValue, 9)}\n");

        if (errorFile.Length > 0 && !errorImage.Write(errorFile))
            return 1;

        return 0;
    }

    private static int diff(List<string> args)
    {
        string outFile = "", imageFile = "", referenceFile = "", metric = "MSE";
        string channels = "R,G,B";
        int[] cropWindow = [-1, 0, -1, 0];

        static void onError(string err)
        {
            usage("diff", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, "reference", ref referenceFile, onError) ||
                ParseArg(args, ref iter, "metric", ref metric, onError) ||
                ParseArg(args, ref iter, "channels", ref channels, onError) ||
                ParseArg(args, ref iter, "crop", cropWindow.AsSpan(), onError))
            {
                // success
            }
            else if (IsFlag(args[iter]))
                usage("diff", $"{args[iter]}: unknown command flag");
            else if (imageFile.Length > 0)
                usage("diff", $"{args[iter]}: excess argument");
            else
                imageFile = args[iter];
        }

        if (imageFile.Length == 0)
            usage("diff", "must specify image to compute difference with.");

        if (referenceFile.Length == 0)
            usage("diff", "must specify --reference image");

        if (metric != "MAE" && metric != "MSE" && metric != "MRSE" && metric != "FLIP")
            usage("diff", $"{metric}: --metric must be \"MAE\", \"MSE\", \"MRSE\", or \"FLIP\".");

        ImageAndMetadata refRead = Image.Read(referenceFile);
        Image refImage = refRead.image;
        ImageMetadata refMetadata = refRead.metadata;

        Bounds2i? cropBounds = CropBounds(cropWindow);
        if (cropBounds is Bounds2i cb0)
            refImage = refImage.Crop(cb0);

        ImageAndMetadata im = Image.Read(imageFile);
        Image image = im.image;

        // Select channels
        List<string> splitChannels = SplitString(channels, ',');
        ImageChannelDesc refDesc = refImage.GetChannelDesc(splitChannels.ToArray());
        ImageChannelDesc imgDesc = image.GetChannelDesc(splitChannels.ToArray());
        if (refDesc.Size != splitChannels.Count)
        {
            Error($"{referenceFile}: image does not have \"{channels}\" channels.");
            return 1;
        }
        refImage = refImage.SelectChannels(refDesc);

        if (imgDesc.Size != splitChannels.Count)
        {
            Error($"{imageFile}: image does not have \"{channels}\" channels.");
            return 1;
        }
        image = image.SelectChannels(imgDesc);

        // Crop before comparing resolutions.
        if (cropBounds is Bounds2i cb1)
            image = image.Crop(cb1);

        if (!checkImageCompatibility(imageFile, image, im.metadata.GetColorSpace(), referenceFile, refImage,
                                     refMetadata.GetColorSpace()))
            return 1;

        // Clamp Infs
        int nClamped = 0, nRefClamped = 0;
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                for (int c = 0; c < image.NChannels(); ++c)
                {
                    if (Float.IsInfinity(image.GetChannel(new Point2i(x, y), c)))
                    {
                        ++nClamped;
                        image.SetChannel(new Point2i(x, y), c, 0);
                    }
                    if (Float.IsInfinity(refImage.GetChannel(new Point2i(x, y), c)))
                    {
                        ++nRefClamped;
                        refImage.SetChannel(new Point2i(x, y), c, 0);
                    }
                }
        if (nClamped > 0)
            Warning($"{imageFile}: clamped {nClamped} infinite pixel values.");
        if (nRefClamped > 0)
            Warning($"{referenceFile}: clamped {nRefClamped} infinite pixel values.");

        // Image averages. Compute before FLIP potentially goes and clamps things...
        Float refAverage = refImage.Average(refImage.AllChannelsDesc()).Average();
        Float imageAverage = image.Average(image.AllChannelsDesc()).Average();

        Image errorImage;
        ImageChannelValues error = new(refImage.NChannels());
        if (metric == "MAE")
            error = image.MAE(image.AllChannelsDesc(), refImage, out errorImage);
        else if (metric == "MSE")
            error = image.MSE(image.AllChannelsDesc(), refImage, out errorImage);
        else if (metric == "MRSE")
            error = image.MRSE(image.AllChannelsDesc(), refImage, out errorImage);
        else
        {
            // FLIP
            if (refImage.NChannels() != 3)
            {
                Error($"{referenceFile}: only 3 channel images are currently supported for FLIP.");
                return 1;
            }

            errorImage = new Image(PixelFormat.Float, image.Resolution(), ["Error"]);
            FLIPOptions opt = new(); /* use defaults for now */
            image = image.ConvertToFormat(PixelFormat.Float);
            refImage = refImage.ConvertToFormat(PixelFormat.Float);

            // Clamp to [0,1]...
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    for (int c = 0; c < image.NChannels(); ++c)
                    {
                        image.SetChannel(new Point2i(x, y), c, Clamp(image.GetChannel(new Point2i(x, y), c), 0, 1));
                        refImage.SetChannel(new Point2i(x, y), c,
                                            Clamp(refImage.GetChannel(new Point2i(x, y), c), 0, 1));
                    }

            Flip.ComputeFLIPError(image.RawFloat(), refImage.RawFloat(), errorImage.RawFloat(), image.Resolution().x,
                                  image.Resolution().y, opt);

            for (int c = 0; c < image.NChannels(); ++c)
                error[c] = 0;
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    for (int c = 0; c < image.NChannels(); ++c)
                        error[c] += errorImage.GetChannel(new Point2i(x, y), 0);
            for (int c = 0; c < image.NChannels(); ++c)
                error[c] /= image.Resolution().x * image.Resolution().y;
        }

        if (error.MaxValue() == 0)
            // Same same.
            return 0;

        float delta = (float)(100f * (imageAverage - refAverage) / refAverage);
        string deltaString = $"{FloatToString(delta)}% delta";
        if (Math.Abs(delta) > 0.1)
            deltaString = Red(deltaString);
        else if (Math.Abs(delta) > 0.001)
            deltaString = Yellow(deltaString);
        stdout.Write($"Images differ:\n\t{imageFile} {referenceFile}\n\tavg = {FloatToString(imageAverage)} / " +
                     $"{FloatToString(refAverage)} ({deltaString}), {metric} = {FloatToString(error.Average())}\n");

        if (outFile.Length > 0)
        {
            if (!errorImage.Write(outFile, im.metadata))
                return 1;
        }

        return 1;
    }

    private static void printImageStats(string name, Image image, ImageMetadata metadata)
    {
        TextWriter o = stdout;
        o.Write($"{name}:\n\tresolution ({image.Resolution().x}, {image.Resolution().y})\n");
        o.Write($"\tpixel format: {image.Format()}\n");

        o.Write("\tcolor space : ");
        if (metadata.colorSpace is RGBColorSpace cs)
        {
            if (cs == RGBColorSpace.sRGB)
                o.Write("sRGB");
            else if (cs == RGBColorSpace.DCI_P3)
                o.Write("DCI-P3");
            else if (cs == RGBColorSpace.Rec2020)
                o.Write("rec2020");
            else if (cs == RGBColorSpace.ACES2065_1)
                o.Write("ACES");
            else
                o.Write($" r: {F(cs.r.x)} {F(cs.r.y)} g: {F(cs.g.x)} {F(cs.g.y)} b: {F(cs.b.x)} {F(cs.b.y)} " +
                        $"w: {F(cs.w.x)} {F(cs.w.y)}");
            o.Write("\n");
        }
        else
            o.Write(" (unspecified)\n");

        if (metadata.fullResolution is Point2i fullResolution)
            o.Write($"\tfull resolution ({fullResolution.x}, {fullResolution.y})\n");
        if (metadata.pixelBounds is Bounds2i pixelBounds)
            o.Write($"\tpixel bounds ({pixelBounds.pMin.x}, {pixelBounds.pMin.y}) - ({pixelBounds.pMax.x}, " +
                    $"{pixelBounds.pMax.y})\n");
        if (metadata.renderTimeSeconds is float renderTimeSeconds)
        {
            float s = renderTimeSeconds;
            int h = (int)s / 3600;
            s -= h * 3600;
            int m = (int)s / 60;
            s -= m * 60;

            o.Write($"\trender time: {h}h {m}m {F(s, 2)}s (total {F(renderTimeSeconds, 2)}s)\n");
        }
        if (metadata.cameraFromWorld is SquareMatrix4 cameraFromWorld)
            o.Write($"\tcamera from world: {cameraFromWorld}\n");
        if (metadata.NDCFromWorld is SquareMatrix4 NDCFromWorld)
            o.Write($"\tNDC from world: {NDCFromWorld}\n");
        if (metadata.samplesPerPixel is int samplesPerPixel)
            o.Write($"\tsamples per pixel: {samplesPerPixel}\n");

        if (metadata.MSE is float MSE)
            o.Write($"\tMSE vs. reference image: {G(MSE)}\n");

        foreach (var (key, value) in metadata.strings)
            o.Write($"\t\"{key}\": \"{value}\"\n");

        foreach (var (key, value) in metadata.stringVectors)
        {
            o.Write($"\t\"{key}\": [ ");
            foreach (string str in value)
                o.Write($"\"{str}\" ");
            o.Write("]\n");
        }

        o.Write("\tChannels:\n");

        List<string> channelNames = image.ChannelNames();
        foreach (string channel in channelNames)
        {
            Float min = Infinity, max = -Infinity;
            double sum = 0.0;
            int nNaN = 0, nInf = 0, nValid = 0;
            ImageChannelDesc desc = image.GetChannelDesc([channel]);

            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Float v = image.GetChannels(new Point2i(x, y), desc);

                    if (Float.IsNaN(v))
                        ++nNaN;
                    else if (Float.IsInfinity(v))
                        ++nInf;
                    else
                    {
                        min = (v < min) ? v : min;
                        max = (max < v) ? v : max;
                        sum += v;
                        ++nValid;
                    }
                }

            o.Write($"\t    {channel,20}: min {G(min, 6, 12)} max {G(max, 6, 12)} avg {G(sum / nValid, 6, 12)} " +
                    $"({nInf} infinite, {nNaN} not-a-number)\n");
        }
    }

    private static int falsecolor(List<string> args)
    {
        string outFile = "", inFile = "";
        bool plusMinus = false, ramp = false;
        Float maxValue = -Infinity;

        static void onError(string err)
        {
            usage("falsecolor", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, "plusminus", ref plusMinus, onError) ||
                ParseArg(args, ref iter, "ramp", ref ramp, onError) ||
                ParseArg(args, ref iter, "maxValue", ref maxValue, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && !IsFlag(args[iter]))
                inFile = args[iter];
            else
                usage("falsecolor", $"{args[iter]}: unknown command flag");
        }

        if (!ramp && inFile.Length == 0)
            usage("falsecolor", "expecting input image filename.");
        if (outFile.Length == 0)
            usage("falsecolor", "expecting --outfile filename.");

        Image image;
        if (ramp)
        {
            int xres = 10, yres = 300;
            image = new Image(PixelFormat.Float, new Point2i(xres, yres), ["Y"]);
            for (int y = 0; y < yres; ++y)
                for (int x = 0; x < xres; ++x)
                    image.SetChannel(new Point2i(x, y), 0, (Float)(yres - 1 - y) / (Float)(yres - 1));
        }
        else
            image = Image.Read(inFile).image;

        if (maxValue == -Infinity)
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Float a = FMath.Abs(image.GetChannels(new Point2i(x, y)).Average());
                    maxValue = (maxValue < a) ? a : maxValue;
                }

        Image outImage = new(PixelFormat.Half, image.Resolution(), ["R", "G", "B"]);
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                Float relativeValue = image.GetChannels(new Point2i(x, y)).Average() / maxValue;
                RGB rgb;
                if (plusMinus)
                {
                    if (relativeValue > 0)
                        rgb = new RGB(0, relativeValue, 0);
                    else
                        rgb = new RGB(FMath.Abs(relativeValue), 0, 0);
                }
                else
                {
                    relativeValue = Clamp(relativeValue, 0, 1);
                    int index = (int)(relativeValue * falseColorValues.Length);
                    index = Math.Min(index, falseColorValues.Length - 1);
                    rgb = falseColorValues[index];
                }

                outImage.SetChannels(new Point2i(x, y),
                                     [SRGBToLinear((float)rgb[0]), SRGBToLinear((float)rgb[1]), SRGBToLinear((float)rgb[2])]);
            }

        if (!outImage.Write(outFile))
            return 1;

        return 0;
    }

    private static int info(List<string> args)
    {
        int err = 0;
        foreach (string fn in args)
        {
            ImageAndMetadata im = Image.Read(fn);
            printImageStats(fn, im.image, im.metadata);
        }
        return err;
    }

    private static int bloom(List<string> args)
    {
        string inFile = "", outFile = "";
        Float level = Infinity;
        int width = 15;
        Float scale = (Float).3;
        int iterations = 5;

        static void onError(string err)
        {
            usage("bloom", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, "level", ref level, onError) ||
                ParseArg(args, ref iter, "width", ref width, onError) ||
                ParseArg(args, ref iter, "iterations", ref iterations, onError) ||
                ParseArg(args, ref iter, "scale", ref scale, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && !IsFlag(args[iter]))
                inFile = args[iter];
            else
                onError($"argument {args[iter]} invalid");
        }

        if (outFile.Length == 0)
            usage("bloom", "--outfile must be specified");
        if (inFile.Length == 0)
            usage("bloom", "input filename must be specified");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;

        List<Image> blurred = [];

        // First, threshold the source image
        int nSurvivors = 0;
        Point2i res = image.Resolution();
        int nc = image.NChannels();
        Image thresholdedImage = new(PixelFormat.Float, image.Resolution(), image.ChannelNames().ToArray());
        for (int y = 0; y < res.y; ++y)
        {
            for (int x = 0; x < res.x; ++x)
            {
                bool overThreshold = false;
                for (int c = 0; c < nc; ++c)
                    if (image.GetChannel(new Point2i(x, y), c) > level)
                        overThreshold = true;
                if (overThreshold)
                {
                    ++nSurvivors;
                    for (int c = 0; c < nc; ++c)
                        thresholdedImage.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(x, y), c));
                }
                else
                    for (int c = 0; c < nc; ++c)
                        thresholdedImage.SetChannel(new Point2i(x, y), c, 0f);
            }
        }
        if (nSurvivors == 0)
        {
            Warning($"No pixels were above bloom threshold {FloatToString(level)}");
            return 1;
        }
        blurred.Add(thresholdedImage);

        if ((width % 2) == 0)
        {
            ++width;
            Warning($"Bloom width must be an odd value. Rounding up to {width}.");
        }
        int radius = width / 2;

        // Blur thresholded image.
        Float sigma = (Float)(radius / 2.0);  // TODO: make a parameter

        for (int iter = 0; iter < iterations; ++iter)
        {
            Image blur = blurred[^1].GaussianFilter(image.AllChannelsDesc(), radius, sigma);
            blurred.Add(blur);
        }

        // Finally, add all of the blurred images, scaled, to the original.
        for (int y = 0; y < res.y; ++y)
        {
            for (int x = 0; x < res.x; ++x)
            {
                for (int c = 0; c < nc; ++c)
                {
                    Float blurredSum = 0f;
                    // Skip the thresholded image, since it's already
                    // present in the original; just add pixels from the
                    // blurred ones.
                    for (int j = 1; j < blurred.Count; ++j)
                        blurredSum += blurred[j].GetChannel(new Point2i(x, y), c);
                    image.SetChannel(new Point2i(x, y), c,
                                     image.GetChannel(new Point2i(x, y), c) + (scale / iterations) * blurredSum);
                }
            }
        }

        image.Write(outFile, imRead.metadata);

        return 0;
    }

    private static int convert(List<string> args)
    {
        bool acesFilmic = false;
        float scale = 1f, gamma = 1f;
        int repeat = 1;
        bool flipy = false;
        bool tonemap = false;
        Float maxY = 1;
        Float despikeLimit = Infinity;
        bool preserveColors = false;
        bool bw = false;
        bool fp16 = false;
        string inFile = "", outFile = "";
        string colorspace = "";
        string channelNames = "";
        int[] cropWindow = [-1, 0, -1, 0];
        Float clamp = Infinity;

        static void onError(string err)
        {
            usage("convert", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "acesfilmic", ref acesFilmic, onError) ||
                ParseArg(args, ref iter, "bw", ref bw, onError) ||
                ParseArg(args, ref iter, "channels", ref channelNames, onError) ||
                ParseArg(args, ref iter, "clamp", ref clamp, onError) ||
                ParseArg(args, ref iter, "colorspace", ref colorspace, onError) ||
                ParseArg(args, ref iter, "crop", cropWindow.AsSpan(), onError) ||
                ParseArg(args, ref iter, "despike", ref despikeLimit, onError) ||
                ParseArg(args, ref iter, "fp16", ref fp16, onError) ||
                ParseArg(args, ref iter, "flipy", ref flipy, onError) ||
                ParseArg(args, ref iter, "gamma", ref gamma, onError) ||
                ParseArg(args, ref iter, "maxluminance", ref maxY, onError) ||
                ParseArg(args, ref iter, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, "preservecolors", ref preserveColors, onError) ||
                ParseArg(args, ref iter, "repeatpix", ref repeat, onError) ||
                ParseArg(args, ref iter, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, "tonemap", ref tonemap, onError))
            {
                // success
            }
            else if (!IsFlag(args[iter]) && inFile.Length == 0)
                inFile = args[iter];
            else
                usage("convert", $"{args[iter]}: unknown command flag");
        }

        if (maxY <= 0)
            usage("convert", "--maxluminance value must be greater than zero");
        if (repeat <= 0)
            usage("convert", "--repeatpix value must be greater than zero");
        if (scale == 0)
            usage("convert", "--scale value must be non-zero");
        if (outFile.Length == 0)
            usage("convert", "--outfile filename must be specified");
        if (inFile.Length == 0)
            usage("convert", "input filename not specified");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;
        ImageMetadata metadata = imRead.metadata;

        ImageMetadata outMetadata = new();
        // These are the only entries it makes sense to copy along (so far).
        outMetadata.colorSpace = metadata.colorSpace;
        outMetadata.cameraFromWorld = metadata.cameraFromWorld;
        outMetadata.NDCFromWorld = metadata.NDCFromWorld;
        outMetadata.pixelBounds = metadata.pixelBounds;
        outMetadata.fullResolution = metadata.fullResolution;

        if (channelNames.Length == 0)
        {
            // If the input image has AOVs and the target image is a regular
            // format, then just grab R,G,B...
            bool hasAOVs = false;
            foreach (string name in image.ChannelNames())
                if (name != "R" && name != "G" && name != "B" && name != "A")
                {
                    hasAOVs = true;
                    break;
                }

            if (hasAOVs && !HasExtension(outFile, "exr"))
            {
                Warning($"{inFile}: image has non-RGBA channels but converting to an image format that can't " +
                        "store them. Converting RGB only.");
                channelNames = "R,G,B";
            }
        }

        if (channelNames.Length > 0)
        {
            List<string> splitChannelNames = SplitString(channelNames, ',');
            ImageChannelDesc desc = image.GetChannelDesc(splitChannelNames.ToArray());
            if (!desc)
            {
                Error($"{inFile}: image doesn't have channels \"{channelNames}\".");
                return 1;
            }
            image = image.SelectChannels(desc);
        }

        Point2i res = image.Resolution();
        int nc = image.NChannels();

        // Crop
        if (CropBounds(cropWindow) is Bounds2i cropBounds)
        {
            Point2i fullRes = metadata.fullResolution ?? image.Resolution();

            if (metadata.pixelBounds is Bounds2i pb && !Inside(cropBounds, pb))
            {
                Error($"{inFile}: crop window bounds ({cropBounds.pMin.x},{cropBounds.pMin.y})-" +
                      $"({cropBounds.pMax.x},{cropBounds.pMax.y}) are not inside image's pixel bounds " +
                      $"({pb.pMin.x},{pb.pMin.y})-({pb.pMax.x},{pb.pMax.y}).");
                return 1;
            }
            else if (!Inside(cropBounds, new Bounds2i(new Point2i(0, 0), fullRes)))
            {
                Error($"{inFile}: crop window bounds ({cropBounds.pMin.x},{cropBounds.pMin.y})-" +
                      $"({cropBounds.pMax.x},{cropBounds.pMax.y}) are not inside image's resolution " +
                      $"({fullRes.x},{fullRes.y}).");
                return 1;
            }

            outMetadata.fullResolution = fullRes;
            outMetadata.pixelBounds = cropBounds;

            // Adjust crop bounds for call to Crop()
            if (metadata.pixelBounds is Bounds2i mpb)
            {
                cropBounds.pMin = new Point2i(cropBounds.pMin - mpb.pMin);
                cropBounds.pMax = new Point2i(cropBounds.pMax - mpb.pMin);
            }
            image = image.Crop(cropBounds);

            res = image.Resolution();
        }

        // Count how many of the following operations modify pixel values
        // rather than copying existing ones.
        static int I(bool b) => b ? 1 : 0;
        int nModifyingOps = I(colorspace.Length > 0) + I(scale != 1) + I(gamma != 1) + I(tonemap) +
                            I(preserveColors) + I(acesFilmic);
        // Convert to a 32-bit format for maximum accuracy if we're applying
        // multiple operations to the pixel values.
        if (nModifyingOps > 1 && !Is32Bit(image.Format()))
            image = image.ConvertToFormat(PixelFormat.Float);

        if (clamp < Infinity)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float maxValue = 0;
                    for (int c = 0; c < nc; ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        maxValue = (maxValue < v) ? v : maxValue;
                    }
                    if (maxValue > clamp)
                    {
                        Float s = clamp / maxValue;
                        for (int c = 0; c < nc; ++c)
                            image.SetChannel(new Point2i(x, y), c, s * image.GetChannel(new Point2i(x, y), c));
                    }
                }
        }

        if (colorspace.Length > 0)
        {
            RGBColorSpace? dest = RGBColorSpace.GetNamed(colorspace);
            if (dest == null)
            {
                Error($"{colorspace}: color space unknown.");
                return 1;
            }
            ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
            if (!rgbDesc)
            {
                Error($"{inFile}: doesn't have R, G, B channels.");
                return 1;
            }

            RGBColorSpace srcColorSpace = metadata.GetColorSpace();
            SquareMatrix3 m = ConvertRGBColorSpace(srcColorSpace, dest);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    ImageChannelValues channels = image.GetChannels(new Point2i(x, y), rgbDesc);
                    RGB rgb = Mul(m, channels);
                    image.SetChannels(new Point2i(x, y), rgbDesc, [rgb.r, rgb.g, rgb.b]);
                }
            outMetadata.colorSpace = dest;
        }

        if (bw)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float sum = 0;
                    for (int c = 0; c < nc; ++c)
                        sum += image.GetChannel(new Point2i(x, y), c);
                    sum /= nc;
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, sum);
                }
        }

        if (despikeLimit < Infinity)
        {
            Image filteredImg = image.Clone();
            int despikeCount = 0;
            ImageChannelValues[] neighbors = new ImageChannelValues[9];
            for (int i = 0; i < 9; ++i)
                neighbors[i] = new ImageChannelValues(image.NChannels());

            for (int y = 0; y < res.y; ++y)
            {
                for (int x = 0; x < res.x; ++x)
                {
                    if (image.GetChannels(new Point2i(x, y)).Average() < despikeLimit)
                        continue;

                    // Copy all of the valid neighbor pixels into neighbors[].
                    ++despikeCount;
                    int validNeighbors = 0;
                    for (int dy = -1; dy <= 1; ++dy)
                    {
                        if (y + dy < 0 || y + dy >= res.y)
                            continue;
                        for (int dx = -1; dx <= 1; ++dx)
                        {
                            if (x + dx < 0 || x + dx > res.x)
                                continue;
                            neighbors[validNeighbors++] = image.GetChannels(new Point2i(x + dx, y + dy));
                        }
                    }

                    // Find the median of the neighbors, sorted by average value.
                    int mid = validNeighbors / 2;
                    NthElement(neighbors.AsSpan(0, validNeighbors), mid, (a, b) => a.Average() < b.Average());
                    filteredImg.SetChannels(new Point2i(x, y), neighbors[mid]);
                }
            }
            (image, filteredImg) = (filteredImg, image);
            stdout.Flush();
            Console.Error.Write($"{inFile}: despiked {despikeCount} pixels\n");
        }

        if (scale != 1)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, scale * image.GetChannel(new Point2i(x, y), c));
        }

        if (gamma != 1)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    for (int c = 0; c < nc; ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        image.SetChannel(new Point2i(x, y), c, FMath.Pow((0 < v) ? v : 0, gamma));
                    }
        }

        if (tonemap)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float lum = image.GetChannels(new Point2i(x, y)).Average();
                    // Reinhard et al. photographic tone mapping operator.
                    Float s = (1 + lum / (maxY * maxY)) / (1 + lum);
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, s * image.GetChannel(new Point2i(x, y), c));
                }
        }

        if (preserveColors)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float m = image.GetChannel(new Point2i(x, y), 0);
                    for (int c = 1; c < nc; ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        m = (m < v) ? v : m;
                    }
                    if (m > 1)
                    {
                        for (int c = 0; c < nc; ++c)
                            image.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(x, y), c) / m);
                    }
                }
        }

        if (acesFilmic)
        {
            // Approximation via
            // https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
            static Float ACESFilm(Float x)
            {
                if (x <= 0)
                    return 0;
                Float a = 2.51f;
                Float b = 0.03f;
                Float c = 2.43f;
                Float d = 0.59f;
                Float e = 0.14f;
                return Clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0, 1);
            }

            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    for (int c = 0; c < nc; ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        v = ACESFilm(v);
                        image.SetChannel(new Point2i(x, y), c, v);
                    }
                }
        }

        if (repeat > 1)
        {
            Image scaledImage = new(image.Format(), new Point2i(res.x * repeat, res.y * repeat),
                                    image.ChannelNames().ToArray(), image.Encoding());
            for (int y = 0; y < repeat * res.y; ++y)
            {
                int yy = y / repeat;
                for (int x = 0; x < repeat * res.x; ++x)
                {
                    int xx = x / repeat;
                    for (int c = 0; c < nc; ++c)
                        scaledImage.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(xx, yy), c));
                }
            }
            image = scaledImage;
            res = image.Resolution();

            if (outMetadata.fullResolution is Point2i fr)
                outMetadata.fullResolution = fr * repeat;
            if (outMetadata.pixelBounds is Bounds2i pb)
                outMetadata.pixelBounds = new Bounds2i(pb.pMin * repeat, pb.pMax * repeat);
        }

        if (flipy)
        {
            image.FlipY();
            if (outMetadata.pixelBounds is Bounds2i pb && outMetadata.fullResolution is Point2i fr)
            {
                // Flip the pixel bounds in the metadata as well
                int by0 = fr.y - 1 - pb.pMax.y, by1 = fr.y - 1 - pb.pMin.y;
                pb.pMin.y = by0;
                pb.pMax.y = by1;
                outMetadata.pixelBounds = pb;
            }
        }

        if (fp16)
            image = image.ConvertToFormat(PixelFormat.Half);

        if (!image.Write(outFile, outMetadata))
            return 1;

        return 0;
    }

    private static int whitebalance(List<string> args)
    {
        string inFile = "", outFile = "";
        Float temperature = 0;
        Float[] xy = [0, 0];
        string illuminant = "";

        static void onError(string err)
        {
            usage("whitebalance", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, "primaries", xy.AsSpan(), onError) ||
                ParseArg(args, ref iter, "illuminant", ref illuminant, onError) ||
                ParseArg(args, ref iter, "temperature", ref temperature, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && !IsFlag(args[iter]))
                inFile = args[iter];
            else
                onError($"argument {args[iter]} invalid");
        }

        if (outFile.Length == 0)
            usage("whitebalance", "--outfile must be specified");
        if (inFile.Length == 0)
            usage("whitebalance", "input filename must be specified");

        static int I(bool b) => b ? 1 : 0;
        if ((I(illuminant.Length > 0) + I(temperature > 0) + I(xy[0] != 0)) > 1)
            usage("whitebalance", "can only provide one of --illuminant, --primaries, --temperature");
        if (illuminant.Length == 0 && temperature == 0 && xy[0] == 0)
            usage("whitebalance", "must provide one of --illuminant, --primaries, or --temperature");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;

        ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
        if (!rgbDesc)
        {
            Error($"{inFile}: doesn't have R, G, B channels.");
            return 1;
        }

        RGBColorSpace colorSpace = imRead.metadata.GetColorSpace();
        Point2f srcWhite, targetWhite = colorSpace.w;
        if (illuminant.Length > 0)
        {
            string name = "stdillum-" + illuminant;
            Spectrum? illum = GetNamedSpectrum(name);
            if (illum == null)
            {
                Error($"{name}: illuminant unknown.");
                return 1;
            }
            srcWhite = SpectrumToXYZ(illum).xy();
        }
        else if (temperature > 0)
        {
            // Sensor::Create uses Spectra::D() rather than BlackbodySpectrum
            // here---make consistent?
            DenselySampledSpectrum illum = Spectra.D(temperature);
            srcWhite = SpectrumToXYZ(illum).xy();
        }
        else
            srcWhite = new Point2f(xy[0], xy[1]);

        SquareMatrix3 ccMatrix = colorSpace.RGBFromXYZ * WhiteBalance(srcWhite, targetWhite) * colorSpace.XYZFromRGB;

        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                ImageChannelValues channels = image.GetChannels(new Point2i(x, y), rgbDesc);
                RGB rgb = Mul(ccMatrix, channels);
                image.SetChannels(new Point2i(x, y), rgbDesc, [rgb.r, rgb.g, rgb.b]);
            }

        image.Write(outFile, imRead.metadata);

        return 0;
    }

    private static int makeemitters(List<string> args)
    {
        string filename = "";
        int downsampleRate = 1;

        static void onError(string err)
        {
            usage("makeemitters", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "downsample", ref downsampleRate, onError))
            {
                // success
            }
            else if (IsFlag(args[iter]))
                usage("makeemitters", $"{args[iter]}: unknown command flag");
            else if (filename.Length == 0)
                filename = args[iter];
            else
                usage("makeemitters", "multiple input filenames provided.");
        }

        if (filename.Length == 0)
            usage("makeemitters", "missing image filename");

        ImageAndMetadata im = Image.Read(filename);
        Image image = im.image;

        ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
        if (!rgbDesc)
        {
            Error($"{filename}: didn't find R, G, and B channels");
            return 1;
        }

        Point2i res = image.Resolution();
        float aspect = (float)res.x / (float)res.y;
        stdout.Write("AttributeBegin\n");
        stdout.Write("Material \"matte\" \"rgb Kd\" [0 0 0]\n");
        for (int y = 0; y < image.Resolution().y; y += downsampleRate)
            for (int x = 0; x < image.Resolution().x; x += downsampleRate)
            {
                ImageChannelValues pSum = new(rgbDesc.Size);
                for (int dy = 0; dy < downsampleRate; ++dy)
                    for (int dx = 0; dx < downsampleRate; ++dx)
                    {
                        Point2i pp = new(x + dx, y + dy);
                        if (pp.x >= res.x && pp.y >= res.y)
                            continue;

                        ImageChannelValues p = image.GetChannels(pp, rgbDesc);
                        for (int c = 0; c < p.Size; ++c)
                            pSum[c] += p[c];
                    }
                for (int c = 0; c < pSum.Size; ++c)
                    pSum[c] /= downsampleRate * downsampleRate;

                stdout.Write($"AreaLightSource \"diffuse\" \"rgb L\" [ {F(pSum[0])} {F(pSum[1])} {F(pSum[2])} ]\n");

                float x0 = aspect * (1 - (float)x / image.Resolution().x) - aspect / 2;
                float x1 = aspect * (1 - (float)Math.Min(x + downsampleRate, res.x) / image.Resolution().x) -
                           aspect / 2;
                float y0 = 1 - (float)y / image.Resolution().y;
                float y1 = 1 - (float)Math.Min(y + downsampleRate, res.y) / image.Resolution().y;
                stdout.Write($"Shape \"bilinear\" \"point3 P\" [ {F(x0)} {F(y0)} 0 {F(x1)} {F(y0)} 0 {F(x0)} {F(y1)} 0 " +
                             $"{F(x1)} {F(y1)} 0 ]\n");
            }
        stdout.Write("AttributeEnd\n");

        return 0;
    }

    private static int makeequiarea(List<string> args)
    {
        string inFilename = "", outFilename = "";
        int resolution = 0;

        static void onError(string err)
        {
            usage("makeequiarea", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "resolution", ref resolution, onError) ||
                ParseArg(args, ref iter, "outfile", ref outFilename, onError))
            {
                // success
            }
            else if (IsFlag(args[iter]))
                usage("makeequiarea", $"{args[iter]}: unknown command flag");
            else if (inFilename.Length == 0)
                inFilename = args[iter];
            else
                usage("makeequiarea", "multiple input filenames provided.");
        }
        if (inFilename.Length == 0)
            usage("makeequiarea", "input image filename must be provided.");
        if (outFilename.Length == 0)
            usage("makeequiarea", "output image filename must be provided.");

        ImageAndMetadata latlong = Image.Read(inFilename);
        Image latlongImage = latlong.image;

        if (2 * latlongImage.Resolution().y != latlongImage.Resolution().x)
            Warning($"{inFilename}: esolution ({latlongImage.Resolution().x}, {latlongImage.Resolution().y}) " +
                    "doesn't have a 2:1 aspect ratio. It is doubtful that this is a lat-long environment map.");

        if (resolution == 0)
            // resolution = 1.25f * latlongImage.Resolution().x * std::sqrt(2.f) /
            // 4;
            resolution = latlongImage.Resolution().x;

        // TODO: should we check that lat-long has RGB here?
        // Should we only copy over RGB?
        Image equiRectImage = new(latlongImage.Format(), new Point2i(resolution, resolution),
                                  latlongImage.ChannelNames().ToArray(), latlongImage.Encoding());

        GaussianFilter filter = new(new Vector2f(1.5f, 1.5f), 2);
        // MitchellFilter filter(Vector2f(2.f, 2.f), 1.f/3.f, 1.f/3.f);
        int sqrtSamples = 6;
        WrapMode2D latlongWrap = new(WrapMode.Repeat, WrapMode.Clamp);

        ParallelFor(0, resolution, (long v0, long v1) =>
        {
            RNG rng = new((ulong)v0);
            for (int v = (int)v0; v < v1; ++v)
                for (int u = 0; u < resolution; ++u)
                {
                    Float sumWeight = 0;
                    ImageChannelValues sumSamples = new(latlongImage.NChannels(), 0f);

                    for (int dv = 0; dv < sqrtSamples; ++dv)
                        for (int du = 0; du < sqrtSamples; ++du)
                        {
                            // Stratified samples
                            // (GCC evaluates the Point2f constructor arguments
                            // right to left.)
                            Float sy = (dv + rng.Uniform<Float>()) / sqrtSamples;
                            Float sx = (du + rng.Uniform<Float>()) / sqrtSamples;
                            Point2f s2 = new(sx, sy);
                            FilterSample fs = filter.Sample(s2);
                            // Map to a point in the equirect map for the current
                            // pixel.
                            Point2f pSquare = new((u + 0.5f + fs.p.x) / resolution, (v + 0.5f + fs.p.y) / resolution);
                            pSquare = WrapEqualAreaSquare(pSquare);

                            // Get corresponding pixel values from the lat-long map.
                            Vector3f dir = EqualAreaSquareToSphere(pSquare);
                            Float theta = SphericalTheta(dir), phi = SphericalPhi(dir);
                            Point2f p = new(phi / (2 * Pi), theta / Pi);
                            ImageChannelValues values = latlongImage.Bilerp(p, latlongWrap);

                            // Accumulate
                            for (int i = 0; i < values.Size; ++i)
                                sumSamples[i] += fs.weight * values[i];
                            sumWeight += fs.weight;
                        }
                    for (int i = 0; i < sumSamples.Size; ++i)
                    {
                        Float s = sumSamples[i] / sumWeight;
                        sumSamples[i] = (0 < s) ? s : 0;
                    }
                    equiRectImage.SetChannels(new Point2i(u, v), sumSamples);
                }
        });

        ImageMetadata equiRectMetadata = new();
        equiRectMetadata.cameraFromWorld = latlong.metadata.cameraFromWorld;
        equiRectMetadata.colorSpace = latlong.metadata.colorSpace;
        equiRectMetadata.strings = latlong.metadata.strings;
        equiRectMetadata.stringVectors = latlong.metadata.stringVectors;
        equiRectImage.Write(outFilename, equiRectMetadata);

        return 0;
    }

    // hack for brute force computation of ideal filter weights.
    private static int noisybit(List<string> args)
    {
        string filename = "";
        int[] pixel = [0, 0];
        int width = 10;
        Float sigma = 1;
        int nInstances = 100;

        // (pbrt passes "%s" as the command name here.)
        static void onError(string err)
        {
            usage("%s", err);
            exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "pixel", pixel.AsSpan(), onError) ||
                ParseArg(args, ref iter, "width", ref width, onError) ||
                ParseArg(args, ref iter, "sigma", ref sigma, onError) ||
                ParseArg(args, ref iter, "n", ref nInstances, onError))
            {
                // yaay
            }
            else if (filename.Length == 0)
                filename = args[iter];
            else
                onError($"unexpected argument \"{args[iter]}\"");
        }
        CHECK(filename.Length > 0);

        ImageAndMetadata imRead = Image.Read(filename);
        ImageChannelDesc rgbDesc = imRead.image.GetChannelDesc(["R", "G", "B"]);
        CHECK((bool)rgbDesc);
        Image image = imRead.image.SelectChannels(rgbDesc);

        if (pixel[0] - width < 0 || pixel[0] + width >= image.Resolution().x || pixel[0] - width < 0 ||
            pixel[0] + width >= image.Resolution().y)
        {
            Error($"{filename}: pixel ({pixel[0]}, {pixel[0]}) with width {width} doesn't work with resolution " +
                  $"({image.Resolution().x}, {image.Resolution().y}).");
            return 1;
        }

        int nPixels = Sqr(2 * width + 1);
        CHECK_GE(3 * nInstances, nPixels);  // want to be overconstrained

        RNG rng = new();
        //
        using (StreamWriter f = new("m.csv", false, new UTF8Encoding(false)))
        {
            for (int i = 0; i < nInstances; ++i)
                for (int c = 0; c < 3; ++c)
                {
                    for (int dx = -width; dx <= width; ++dx)
                        for (int dy = -width; dy <= width; ++dy)
                        {
                            // Float noise = .1 * std::exp(-rng.Uniform<Float>() *
                            // 3); if (rng.Uniform<Float>() < .5) noise = -noise;
                            Float noise = SampleNormal(rng.Uniform<Float>(), 0, (Float).1);
                            // TODO: use sigma, make this controllable, etc.
                            f.Write($"{((dx > -width || dy > -width) ? ',' : ' ')}" +
                                    $"{F(image.GetChannel(new Point2i(pixel[0] + dx, pixel[1] + dy), c) + noise)} ");
                        }
                    f.Write("\n");
                }
        }

        // what it should equal
        using (StreamWriter f = new("b.csv", false, new UTF8Encoding(false)))
        {
            for (int i = 0; i < nInstances; ++i)
                for (int c = 0; c < 3; ++c)
                    f.Write($"{F(image.GetChannel(new Point2i(pixel[0], pixel[1]), c))}\n");
        }

        /*
          LeastSquares[Import["m.csv"], Import["b.csv"]]
          ArrayPlot[ArrayReshape[ %, {21, 21}], ColorFunction -> Function[a,
          GrayLevel[4 a]]]
        */
        return 0;
    }

    public static int Main(string[] argv)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is FatalErrorException)
            {
                stdout.Flush();
                Environment.Exit(1);
            }
        };
        try
        {
            int ret = Run(argv);
            stdout.Flush();
            return ret;
        }
        catch (FatalErrorException)
        {
            // (The error has been reported.)
            stdout.Flush();
            return 1;
        }
    }

    private static int Run(string[] argv)
    {
        PBRTOptions opt = new();
        InitPBRT(opt);

        if (argv.Length < 1)
        {
            help();
            return 0;
        }

        List<string> args = GetCommandLineArguments(argv);
        string cmd = args[0];
        args.RemoveAt(0);

        if (cmd == "average")
            return average(args);
        else if (cmd == "assemble")
            return assemble(args);
        else if (cmd == "bloom")
            return bloom(args);
        else if (cmd == "cat")
            return cat(args);
        else if (cmd == "convert")
            return convert(args);
        else if (cmd == "diff")
            return diff(args);
        else if (cmd == "error")
            return error(args);
        else if (cmd == "falsecolor")
            return falsecolor(args);
        else if (cmd == "help" || cmd == "-help" || cmd == "--help" || cmd == "-h")
            help(args);
        else if (cmd == "info")
            return info(args);
        else if (cmd == "makeequiarea")
            return makeequiarea(args);
        else if (cmd == "makeemitters")
            return makeemitters(args);
        else if (cmd == "makesky")
            return makesky(args);
        else if (cmd == "whitebalance")
            return whitebalance(args);
        else if (cmd == "scalenormalmap")
            return scalenormalmap(args);
        else if (cmd == "splitn")
            return splitn(args);
        else if (cmd == "noisybit")
        {
            int ret = noisybit(args);
            if (ret != 0)
                return ret;
        }
        else
        {
            Console.Error.Write($"imgtool: unknown command \"{cmd}\".\n");
            help();
            CleanupPBRT();
            return 1;
        }

        CleanupPBRT();

        return 0;
    }
}

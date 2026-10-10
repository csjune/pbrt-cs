// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/image.h and image.cpp (the parts other than file
// I/O, which is in ImageIO.cs).
//
// Image is a class. pbrt copies Image values (e.g. `Image outImage = *this`);
// here that is Clone(). C# default arguments must be constants, so the
// `WrapMode2D wrapMode = WrapMode::Clamp` defaults become overloads.

using System.Text;

namespace Pbrt;

// PixelFormat Definition
// (The enum names are what pbrt's ToString(PixelFormat) returns.)
public enum PixelFormat { U256, Half, Float }

// ResampleWeight Definition
public struct ResampleWeight
{
    public int firstPixel;
    public Array4<Float> weight;
}

// WrapMode Definitions
public enum WrapMode { Black, Clamp, Repeat, OctahedralSphere }

public struct WrapMode2D
{
    public Array2<WrapMode> wrap;

    public WrapMode2D(WrapMode w)
    {
        wrap = new Array2<WrapMode>(w, w);
    }

    public WrapMode2D(WrapMode x, WrapMode y)
    {
        wrap = new Array2<WrapMode>(x, y);
    }

    public static implicit operator WrapMode2D(WrapMode w) => new(w);

    public override readonly string ToString() =>
        $"[ WrapMode2D {Util.ToString(wrap[0])} {Util.ToString(wrap[1])} ]";
}

// ImageMetadata Definition
public sealed class ImageMetadata
{
    // ImageMetadata Public Members
    public float? renderTimeSeconds;
    public SquareMatrix4? cameraFromWorld, NDCFromWorld;
    public Bounds2i? pixelBounds;
    public Point2i? fullResolution;
    public int? samplesPerPixel;
    public float? MSE;
    // pstd::optional<const RGBColorSpace *>: null is both "unset" and nullptr.
    public RGBColorSpace? colorSpace;
    public SortedDictionary<string, string> strings = new(StringComparer.Ordinal);
    public SortedDictionary<string, List<string>> stringVectors = new(StringComparer.Ordinal);

    // ImageMetadata Public Methods
    public RGBColorSpace GetColorSpace() => colorSpace ?? RGBColorSpace.sRGB;

    // pbrt copies ImageMetadata by value.
    public ImageMetadata Clone()
    {
        ImageMetadata m = (ImageMetadata)MemberwiseClone();
        m.strings = new SortedDictionary<string, string>(strings, StringComparer.Ordinal);
        m.stringVectors = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (k, v) in stringVectors)
            m.stringVectors[k] = [.. v];
        return m;
    }

    private static string Opt<T>(T? v) where T : struct => v.HasValue ? $"{v.Value}" : "(unset)";

    public override string ToString() =>
        FormattableString.Invariant(
            $"[ ImageMetadata renderTimeSeconds: {Opt(renderTimeSeconds)} cameraFromWorld: {Opt(cameraFromWorld)} NDCFromWorld: {Opt(NDCFromWorld)} pixelBounds: {Opt(pixelBounds)} fullResolution: {Opt(fullResolution)} samplesPerPixel: {Opt(samplesPerPixel)} MSE: {Opt(MSE)} colorSpace: {colorSpace?.ToString() ?? "(unset)"} ]");
}

// ImageChannelDesc Definition
//
// pbrt's operator bool (true if any channels were found) is operator
// true/false, so `if (desc)` and `!desc` work as in C++.
public readonly struct ImageChannelDesc
{
    private readonly int[]? offsets;

    public ImageChannelDesc(int[] offset)
    {
        offsets = offset;
    }

    public ReadOnlySpan<int> offset => offsets;

    public int Size => offsets?.Length ?? 0;

    public static explicit operator bool(ImageChannelDesc d) => d.Size > 0;
    public static bool operator true(ImageChannelDesc d) => d.Size > 0;
    public static bool operator false(ImageChannelDesc d) => d.Size == 0;
    public static bool operator !(ImageChannelDesc d) => d.Size == 0;

    public bool IsIdentity()
    {
        for (int i = 0; i < Size; ++i)
            if (offset[i] != i)
                return false;
        return true;
    }

    public override string ToString() => $"[ ImageChannelDesc offset: [ {string.Join(", ", offsets ?? [])} ] ]";
}

// ImageChannelValues Definition
//
// pbrt's InlinedVector<Float, 4>: up to four values are stored inline, so
// the common 1-4 channel lookups don't allocate.
public struct ImageChannelValues
{
    private Array4<Float> inline;
    private readonly Float[]? heap;
    private readonly int size;

    public ImageChannelValues(int sz, Float v = 0)
    {
        size = sz;
        if (sz > 4)
        {
            heap = new Float[sz];
            Array.Fill(heap, v);
        }
        else
            for (int i = 0; i < sz; ++i)
                inline[i] = v;
    }

    public readonly int Size => size;

    public Float this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            DCHECK(i >= 0 && i < size);
            return heap != null ? heap[i] : inline[i];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            DCHECK(i >= 0 && i < size);
            if (heap != null)
                heap[i] = value;
            else
                inline[i] = value;
        }
    }

    public static implicit operator Float(in ImageChannelValues v)
    {
        CHECK_EQ(1, v.Size);
        return v[0];
    }

    public static implicit operator Array3<Float>(in ImageChannelValues v)
    {
        CHECK_EQ(3, v.Size);
        return new Array3<Float>(v[0], v[1], v[2]);
    }

    public readonly Float MaxValue()
    {
        Float m = this[0];
        for (int i = 1; i < Size; ++i)
            m = Max(m, this[i]);
        return m;
    }

    public readonly Float Average()
    {
        Float sum = 0;
        for (int i = 0; i < Size; ++i)
            sum += this[i];
        return sum / Size;
    }

    public override readonly string ToString()
    {
        var s = new StringBuilder("[ ImageChannelValues [ ");
        for (int i = 0; i < Size; ++i)
            s.Append(FormattableString.Invariant($"{this[i]}")).Append(i + 1 < Size ? ", " : " ");
        return s.Append("] ]").ToString();
    }
}

// Image Definition
public sealed partial class Image
{
    // Image Private Members
    private PixelFormat format;
    private Point2i resolution;
    private string[] channelNames;
    private ColorEncoding? encoding = null;
    private byte[] p8;
    private Half[] p16;
    private float[] p32;

    // Image Public Methods
    public Image()
    {
        format = PixelFormat.U256;
        resolution = new Point2i(0, 0);
        channelNames = [];
        p8 = [];
        p16 = [];
        p32 = [];
    }

    public Image(byte[] p8c, Point2i resolution, ReadOnlySpan<string> channels, ColorEncoding encoding)
    {
        format = PixelFormat.U256;
        this.resolution = resolution;
        channelNames = channels.ToArray();
        this.encoding = encoding;
        p8 = p8c;
        p16 = [];
        p32 = [];
        CHECK_EQ(p8.Length, NChannels() * resolution[0] * resolution[1]);
    }

    public Image(Half[] p16c, Point2i resolution, ReadOnlySpan<string> channels)
    {
        format = PixelFormat.Half;
        this.resolution = resolution;
        channelNames = channels.ToArray();
        p8 = [];
        p16 = p16c;
        p32 = [];
        CHECK_EQ(p16.Length, NChannels() * resolution[0] * resolution[1]);
        CHECK(Is16Bit(format));
    }

    public Image(float[] p32c, Point2i resolution, ReadOnlySpan<string> channels)
    {
        format = PixelFormat.Float;
        this.resolution = resolution;
        channelNames = channels.ToArray();
        p8 = [];
        p16 = [];
        p32 = p32c;
        CHECK_EQ(p32.Length, NChannels() * resolution[0] * resolution[1]);
        CHECK(Is32Bit(format));
    }

    public Image(PixelFormat format, Point2i resolution, ReadOnlySpan<string> channels,
                 ColorEncoding? encoding = null)
    {
        this.format = format;
        this.resolution = resolution;
        channelNames = channels.ToArray();
        this.encoding = encoding;
        p8 = [];
        p16 = [];
        p32 = [];
        if (Is8Bit(format))
        {
            p8 = new byte[NChannels() * resolution[0] * resolution[1]];
            CHECK(encoding != null);
        }
        else if (Is16Bit(format))
            p16 = new Half[NChannels() * resolution[0] * resolution[1]];
        else if (Is32Bit(format))
            p32 = new float[NChannels() * resolution[0] * resolution[1]];
        else
            LOG_FATAL("Unhandled format in Image::Image()");
    }

    // pbrt's copy constructor.
    public Image Clone()
    {
        Image image = (Image)MemberwiseClone();
        image.channelNames = (string[])channelNames.Clone();
        image.p8 = (byte[])p8.Clone();
        image.p16 = (Half[])p16.Clone();
        image.p32 = (float[])p32.Clone();
        return image;
    }

    public PixelFormat Format() => format;
    public Point2i Resolution() => resolution;
    public int NChannels() => channelNames.Length;
    public List<string> ChannelNames() => [.. channelNames];
    public ColorEncoding? Encoding() => encoding;

    // pbrt's operator bool: true if the image is non-empty.
    public static explicit operator bool(Image image) => image.resolution.x > 0 && image.resolution.y > 0;
    public static bool operator true(Image image) => (bool)image;
    public static bool operator false(Image image) => !(bool)image;

    public int PixelOffset(Point2i p)
    {
        DCHECK(InsideExclusive(p, new Bounds2i(new Point2i(0, 0), resolution)));
        return NChannels() * (p.y * resolution.x + p.x);
    }

    public Float GetChannel(Point2i p, int c) => GetChannel(p, c, WrapMode.Clamp);

    public Float GetChannel(Point2i p, int c, WrapMode2D wrapMode)
    {
        // Remap provided pixel coordinates before reading channel
        if (!RemapPixelCoords(ref p, resolution, wrapMode))
            return 0;

        switch (format)
        {
            case PixelFormat.U256:
            {
                // Return _U256_-encoded pixel channel value
                Float r = 0;
                encoding!.ToLinear(p8.AsSpan(PixelOffset(p) + c, 1), new Span<Float>(ref r));
                return r;
            }
            case PixelFormat.Half:
                // Return _Half_-encoded pixel channel value
                return (Float)p16[PixelOffset(p) + c];
            case PixelFormat.Float:
                // Return _Float_-encoded pixel channel value
                return p32[PixelOffset(p) + c];
            default:
                LOG_FATAL("Unhandled PixelFormat");
                return 0;
        }
    }

    public Float BilerpChannel(Point2f p, int c) => BilerpChannel(p, c, WrapMode.Clamp);

    public Float BilerpChannel(Point2f p, int c, WrapMode2D wrapMode)
    {
        // Compute discrete pixel coordinates and offsets for _p_
        Float x = p[0] * resolution.x - 0.5f, y = p[1] * resolution.y - 0.5f;
        int xi = (int)FMath.Floor(x), yi = (int)FMath.Floor(y);
        Float dx = x - xi, dy = y - yi;

        // Load pixel channel values and return bilinearly interpolated value
        Array4<Float> v = new(GetChannel(new Point2i(xi, yi), c, wrapMode),
                              GetChannel(new Point2i(xi + 1, yi), c, wrapMode),
                              GetChannel(new Point2i(xi, yi + 1), c, wrapMode),
                              GetChannel(new Point2i(xi + 1, yi + 1), c, wrapMode));
        return ((1 - dx) * (1 - dy) * v[0] + dx * (1 - dy) * v[1] + (1 - dx) * dy * v[2] +
                dx * dy * v[3]);
    }

    // Image Inline Method Definitions
    public void SetChannel(Point2i p, int c, Float value)
    {
        // CHECK(!IsNaN(value));
        if (IsNaN(value))
        {
            LOG_ERROR($"NaN at pixel {p.x},{p.y} comp {c}");
            value = 0;
        }

        switch (format)
        {
            case PixelFormat.U256:
                encoding!.FromLinear(new ReadOnlySpan<Float>(in value), p8.AsSpan(PixelOffset(p) + c, 1));
                break;
            case PixelFormat.Half:
                // (Through float, as pbrt converts Float to half.)
                p16[PixelOffset(p) + c] = (Half)(float)value;
                break;
            case PixelFormat.Float:
                p32[PixelOffset(p) + c] = (float)value;
                break;
            default:
                LOG_FATAL("Unhandled PixelFormat in Image::SetChannel()");
                break;
        }
    }

    public ImageChannelDesc AllChannelsDesc()
    {
        int[] offset = new int[NChannels()];
        for (int i = 0; i < NChannels(); ++i)
            offset[i] = i;
        return new ImageChannelDesc(offset);
    }

    public Float LookupNearestChannel(Point2f p, int c) => LookupNearestChannel(p, c, WrapMode.Clamp);

    public Float LookupNearestChannel(Point2f p, int c, WrapMode2D wrapMode)
    {
        Point2i pi = new((int)(p.x * resolution.x), (int)(p.y * resolution.y));
        return GetChannel(pi, c, wrapMode);
    }

    public int BytesUsed() => p8.Length + 2 * p16.Length + 4 * p32.Length;

    // pbrt's RawPointer(): the pixel storage of the image's format and the
    // offset of _p_ in it.
    public Span<byte> RawU256() => p8;
    public Span<Half> RawHalf() => p16;
    public Span<float> RawFloat() => p32;

    public Array2D<Float> GetSamplingDistribution() =>
        GetSamplingDistribution(_ => 1, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));

    public Array2D<Float> GetSamplingDistribution(Func<Point2f, Float> dxdA) =>
        GetSamplingDistribution(dxdA, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));

    public Array2D<Float> GetSamplingDistribution(Func<Point2f, Float> dxdA, Bounds2f domain)
    {
        Array2D<Float> dist = new(resolution[0], resolution[1]);
        ParallelFor(0, resolution[1], (long y0, long y1) =>
        {
            for (int y = (int)y0; y < y1; ++y)
            {
                for (int x = 0; x < resolution[0]; ++x)
                {
                    // This is noticeably better than MaxValue: discuss / show
                    // example..
                    Float value = GetChannels(new Point2i(x, y)).Average();

                    // Assume Jacobian term is basically constant over the
                    // region.
                    Point2f p = domain.Lerp(
                        new Point2f((x + .5f) / resolution[0], (y + .5f) / resolution[1]));
                    dist[x, y] = value * dxdA(p);
                }
            }
        });
        return dist;
    }

    // Image Method Definitions
    public bool HasAnyInfinitePixels()
    {
        if (format == PixelFormat.U256)
            return false;

        for (int y = 0; y < resolution.y; ++y)
            for (int x = 0; x < resolution.x; ++x)
                for (int c = 0; c < NChannels(); ++c)
                    if (IsInf(GetChannel(new Point2i(x, y), c)))
                        return true;
        return false;
    }

    public bool HasAnyNaNPixels()
    {
        if (format == PixelFormat.U256)
            return false;

        for (int y = 0; y < resolution.y; ++y)
            for (int x = 0; x < resolution.x; ++x)
                for (int c = 0; c < NChannels(); ++c)
                    if (IsNaN(GetChannel(new Point2i(x, y), c)))
                        return true;
        return false;
    }

    public Image GaussianFilter(ImageChannelDesc desc, int halfWidth, Float sigma)
    {
        // Compute filter weights
        Float[] wts = new Float[2 * halfWidth + 1];
        for (int d = 0; d < 2 * halfWidth + 1; ++d)
            wts[d] = Gaussian(d - halfWidth, 0, sigma);

        // Normalize weights
        Float wtSum = 0;
        foreach (Float w in wts)
            wtSum += w;
        for (int i = 0; i < wts.Length; ++i)
            wts[i] /= wtSum;

        // Separable blur; first blur in x into blurx, selecting out the
        // desired channels along the way.
        Image blurx = new(PixelFormat.Float, resolution, ChannelNames(desc).ToArray());
        int nc = desc.Size;
        ParallelFor(0, resolution.y, (long y0, long y1) =>
        {
            for (int y = (int)y0; y < y1; ++y)
            {
                for (int x = 0; x < resolution.x; ++x)
                {
                    ImageChannelValues result = new(desc.Size);
                    for (int r = -halfWidth; r <= halfWidth; ++r)
                    {
                        ImageChannelValues cv = GetChannels(new Point2i(x + r, y), desc);
                        for (int c = 0; c < nc; ++c)
                            result[c] += wts[r + halfWidth] * cv[c];
                    }
                    blurx.SetChannels(new Point2i(x, y), result);
                }
            }
        });

        // Now blur in y from blur x to the result; blurx has just the
        // channels we want already.
        Image blury = new(PixelFormat.Float, resolution, ChannelNames(desc).ToArray());
        ParallelFor(0, resolution.y, (long y0, long y1) =>
        {
            for (int y = (int)y0; y < y1; ++y)
            {
                for (int x = 0; x < resolution.x; ++x)
                {
                    ImageChannelValues result = new(desc.Size);
                    for (int r = -halfWidth; r <= halfWidth; ++r)
                    {
                        ImageChannelValues cv = blurx.GetChannels(new Point2i(x, y + r));
                        for (int c = 0; c < nc; ++c)
                            result[c] += wts[r + halfWidth] * cv[c];
                    }
                    blury.SetChannels(new Point2i(x, y), result);
                }
            }
        });
        return blury;
    }

    private static ResampleWeight[] ResampleWeights(int oldRes, int newRes)
    {
        CHECK_GE(newRes, oldRes);
        ResampleWeight[] wt = new ResampleWeight[newRes];
        Float filterRadius = 2, tau = 2;
        for (int i = 0; i < newRes; ++i)
        {
            // Compute image resampling weights for _i_th pixel
            Float center = (i + .5f) * oldRes / newRes;
            wt[i].firstPixel = (int)FMath.Floor((center - filterRadius) + 0.5f);
            for (int j = 0; j < 4; ++j)
            {
                Float pos = wt[i].firstPixel + j + .5f;
                wt[i].weight[j] = WindowedSinc(pos - center, filterRadius, tau);
            }

            // Normalize filter weights for pixel resampling
            Float invSumWts =
                1 / (wt[i].weight[0] + wt[i].weight[1] + wt[i].weight[2] + wt[i].weight[3]);
            for (int j = 0; j < 4; ++j)
                wt[i].weight[j] *= invSumWts;
        }
        return wt;
    }

    public Image FloatResizeUp(Point2i newRes, WrapMode2D wrapMode)
    {
        CHECK_GE(newRes.x, resolution.x);
        CHECK_GE(newRes.y, resolution.y);
        Image resampledImage = new(PixelFormat.Float, newRes, channelNames);
        // Compute $x$ and $y$ resampling weights for image resizing
        ResampleWeight[] xWeights, yWeights;
        xWeights = ResampleWeights(resolution[0], newRes[0]);
        yWeights = ResampleWeights(resolution[1], newRes[1]);

        // Resize image in parallel, working by tiles
        ParallelFor2D(new Bounds2i(new Point2i(0, 0), newRes), (Bounds2i outExtent) =>
        {
            // Determine extent in source image and copy pixel values to _inBuf_
            Bounds2i inExtent = new(new Point2i(xWeights[outExtent.pMin.x].firstPixel,
                                                yWeights[outExtent.pMin.y].firstPixel),
                                    new Point2i(xWeights[outExtent.pMax.x - 1].firstPixel + 4,
                                                yWeights[outExtent.pMax.y - 1].firstPixel + 4));
            float[] inBuf = new float[NChannels() * inExtent.Area()];
            CopyRectOut(inExtent, inBuf, wrapMode);

            // Resize image in the $x$ dimension
            // Compute image extents and allocate _xBuf_
            int nxOut = outExtent.pMax.x - outExtent.pMin.x;
            int nyOut = outExtent.pMax.y - outExtent.pMin.y;
            int nxIn = inExtent.pMax.x - inExtent.pMin.x;
            int nyIn = inExtent.pMax.y - inExtent.pMin.y;
            float[] xBuf = new float[NChannels() * nyIn * nxOut];

            int xBufOffset = 0;
            for (int yOut = inExtent.pMin.y; yOut < inExtent.pMax.y; ++yOut)
            {
                for (int xOut = outExtent.pMin.x; xOut < outExtent.pMax.x; ++xOut)
                {
                    // Resample image pixel _(xOut, yOut)_
                    DCHECK(xOut >= 0 && xOut < xWeights.Length);
                    ref ResampleWeight rsw = ref xWeights[xOut];
                    // Compute _inOffset_ into _inBuf_ for _(xOut, yOut)_
                    // w.r.t. inBuf
                    int xIn = rsw.firstPixel - inExtent.pMin.x;
                    DCHECK_GE(xIn, 0);
                    DCHECK_LT(xIn + 3, nxIn);
                    int yIn = yOut - inExtent.pMin.y;
                    int inOffset = NChannels() * (xIn + yIn * nxIn);
                    DCHECK_GE(inOffset, 0);
                    DCHECK_LT(inOffset + 3 * NChannels(), inBuf.Length);

                    for (int c = 0; c < NChannels(); ++c, ++xBufOffset, ++inOffset)
                        xBuf[xBufOffset] = (float)(rsw.weight[0] * inBuf[inOffset] +
                                                   rsw.weight[1] * inBuf[inOffset + NChannels()] +
                                                   rsw.weight[2] * inBuf[inOffset + 2 * NChannels()] +
                                                   rsw.weight[3] * inBuf[inOffset + 3 * NChannels()]);
                }
            }

            // Resize image in the $y$ dimension
            float[] outBuf = new float[NChannels() * nxOut * nyOut];
            for (int x = 0; x < nxOut; ++x)
            {
                for (int y = 0; y < nyOut; ++y)
                {
                    int yOut = y + outExtent.pMin.y;
                    DCHECK(yOut >= 0 && yOut < yWeights.Length);
                    ref ResampleWeight rsw = ref yWeights[yOut];

                    DCHECK_GE(rsw.firstPixel - inExtent.pMin.y, 0);
                    int xBufOffset2 =
                        NChannels() * (x + nxOut * (rsw.firstPixel - inExtent.pMin.y));
                    DCHECK_GE(xBufOffset2, 0);
                    int step = NChannels() * nxOut;
                    DCHECK_LT(xBufOffset2 + 3 * step, xBuf.Length);

                    int outOffset = NChannels() * (x + y * nxOut);
                    for (int c = 0; c < NChannels(); ++c, ++outOffset, ++xBufOffset2)
                        outBuf[outOffset] =
                            (float)Max<Float>(0, (rsw.weight[0] * xBuf[xBufOffset2] +
                                                  rsw.weight[1] * xBuf[xBufOffset2 + step] +
                                                  rsw.weight[2] * xBuf[xBufOffset2 + 2 * step] +
                                                  rsw.weight[3] * xBuf[xBufOffset2 + 3 * step]));
                }
            }

            // Copy resampled image pixels out into _resampledImage_
            resampledImage.CopyRectIn(outExtent, outBuf);
        });

        return resampledImage;
    }

    public static List<Image> GeneratePyramid(Image image, WrapMode2D wrapMode)
    {
        PixelFormat origFormat = image.format;
        int nChannels = image.NChannels();
        ColorEncoding? origEncoding = image.encoding;
        // Prepare _image_ for building pyramid
        if (!IsPowerOf2(image.resolution[0]) || !IsPowerOf2(image.resolution[1]))
            image = image.FloatResizeUp(
                new Point2i(RoundUpPow2(image.resolution[0]), RoundUpPow2(image.resolution[1])),
                wrapMode);
        else if (!Is32Bit(image.format))
            image = image.ConvertToFormat(PixelFormat.Float);
        CHECK(Is32Bit(image.format));

        // Initialize levels of pyramid from _image_
        int nLevels = 1 + Log2Int(Math.Max(image.resolution[0], image.resolution[1]));
        List<Image> pyramid = new(nLevels);
        for (int i = 0; i < nLevels - 1; ++i)
        {
            // Initialize $i+1$st level from $i$th level and copy $i$th into pyramid
            pyramid.Add(new Image(origFormat, image.resolution, image.channelNames, origEncoding));
            // Initialize _nextImage_ for $i+1$st level
            Point2i nextResolution = new(Math.Max(1, image.resolution[0] / 2),
                                         Math.Max(1, image.resolution[1] / 2));
            Image nextImage = new(image.format, nextResolution, image.channelNames, origEncoding);

            // Compute offsets from pixels to the 4 pixels used for downsampling
            int[] srcDeltas = [0, nChannels, nChannels * image.resolution[0],
                               nChannels * (image.resolution[0] + 1)];
            if (image.resolution[0] == 1)
            {
                srcDeltas[1] = 0;
                srcDeltas[3] -= nChannels;
            }
            if (image.resolution[1] == 1)
            {
                srcDeltas[2] = 0;
                srcDeltas[3] -= nChannels * image.resolution[0];
            }

            // Downsample _image_ to create next level and update _pyramid_
            Image level = pyramid[i], src = image;
            ParallelFor(0, nextResolution[1], (long y) =>
            {
                // Loop over pixels in scanline $y$ and downsample for the next pyramid level
                int srcOffset = src.PixelOffset(new Point2i(0, 2 * (int)y));
                int nextOffset = nextImage.PixelOffset(new Point2i(0, (int)y));
                for (int x = 0; x < nextResolution[0]; ++x, srcOffset += nChannels)
                    for (int c = 0; c < nChannels; ++c, ++srcOffset, ++nextOffset)
                        nextImage.p32[nextOffset] =
                            (src.p32[srcOffset] + src.p32[srcOffset + srcDeltas[1]] +
                             src.p32[srcOffset + srcDeltas[2]] +
                             src.p32[srcOffset + srcDeltas[3]]) /
                            4;

                // Copy two scanlines from _image_ out to its pyramid level
                int yStart = 2 * (int)y;
                int yEnd = Math.Min(2 * (int)y + 2, src.resolution[1]);
                int offset = src.PixelOffset(new Point2i(0, yStart));
                int count = (yEnd - yStart) * nChannels * src.resolution[0];
                level.CopyRectIn(
                    new Bounds2i(new Point2i(0, yStart), new Point2i(src.resolution[0], yEnd)),
                    src.p32.AsSpan(offset, count));
            });
            image = nextImage;
        }

        // Initialize top level of pyramid and return it
        CHECK(image.resolution[0] == 1 && image.resolution[1] == 1);
        pyramid.Add(new Image(origFormat, new Point2i(1, 1), image.channelNames, origEncoding));
        pyramid[nLevels - 1].CopyRectIn(new Bounds2i(new Point2i(0, 0), new Point2i(1, 1)),
                                        image.p32.AsSpan(0, nChannels));
        return pyramid;
    }

    public ImageChannelDesc GetChannelDesc(ReadOnlySpan<string> requestedChannels)
    {
        int[] offset = new int[requestedChannels.Length];
        for (int i = 0; i < requestedChannels.Length; ++i)
        {
            int j;
            for (j = 0; j < channelNames.Length; ++j)
                if (requestedChannels[i] == channelNames[j])
                {
                    offset[i] = j;
                    break;
                }
            if (j == channelNames.Length)
                return default;
        }

        return new ImageChannelDesc(offset);
    }

    public ImageChannelValues GetChannels(Point2i p, ImageChannelDesc desc) =>
        GetChannels(p, desc, WrapMode.Clamp);

    public ImageChannelValues GetChannels(Point2i p, ImageChannelDesc desc, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(desc.Size, 0);
        if (!RemapPixelCoords(ref p, resolution, wrapMode))
            return cv;

        int pixelOffset = PixelOffset(p);
        ReadOnlySpan<int> offset = desc.offset;
        switch (format)
        {
            case PixelFormat.U256:
            {
                for (int i = 0; i < offset.Length; ++i)
                {
                    Float v = 0;
                    encoding!.ToLinear(p8.AsSpan(pixelOffset + offset[i], 1), new Span<Float>(ref v));
                    cv[i] = v;
                }
                break;
            }
            case PixelFormat.Half:
            {
                for (int i = 0; i < offset.Length; ++i)
                    cv[i] = (Float)p16[pixelOffset + offset[i]];
                break;
            }
            case PixelFormat.Float:
            {
                for (int i = 0; i < offset.Length; ++i)
                    cv[i] = p32[pixelOffset + offset[i]];
                break;
            }
            default:
                LOG_FATAL("Unhandled PixelFormat");
                break;
        }

        return cv;
    }

    public void SetChannels(Point2i p, ImageChannelDesc desc, ReadOnlySpan<Float> values)
    {
        CHECK_LE(values.Length, NChannels());
        for (int i = 0; i < values.Length; ++i)
            SetChannel(p, desc.offset[i], values[i]);
    }

    public Image ConvertToFormat(PixelFormat newFormat, ColorEncoding? encoding = null)
    {
        if (newFormat == format)
            return Clone();

        Image newImage = new(newFormat, resolution, channelNames, encoding);
        for (int y = 0; y < resolution.y; ++y)
            for (int x = 0; x < resolution.x; ++x)
                for (int c = 0; c < NChannels(); ++c)
                    newImage.SetChannel(new Point2i(x, y), c, GetChannel(new Point2i(x, y), c));
        return newImage;
    }

    public ImageChannelValues GetChannels(Point2i p) => GetChannels(p, WrapMode.Clamp);

    public ImageChannelValues GetChannels(Point2i p, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(NChannels(), 0);
        if (!RemapPixelCoords(ref p, resolution, wrapMode))
            return cv;

        int pixelOffset = PixelOffset(p);
        switch (format)
        {
            case PixelFormat.U256:
            {
                Span<Float> v = stackalloc Float[NChannels()];
                encoding!.ToLinear(p8.AsSpan(pixelOffset, NChannels()), v);
                for (int i = 0; i < NChannels(); ++i)
                    cv[i] = v[i];
                break;
            }
            case PixelFormat.Half:
            {
                for (int i = 0; i < NChannels(); ++i)
                    cv[i] = (Float)p16[pixelOffset + i];
                break;
            }
            case PixelFormat.Float:
            {
                for (int i = 0; i < NChannels(); ++i)
                    cv[i] = p32[pixelOffset + i];
                break;
            }
            default:
                LOG_FATAL("Unhandled PixelFormat");
                break;
        }

        return cv;
    }

    public List<string> ChannelNames(ImageChannelDesc desc)
    {
        List<string> names = [];
        for (int i = 0; i < desc.Size; ++i)
            names.Add(channelNames[desc.offset[i]]);
        return names;
    }

    // pbrt's optional Image* output parameter is an `out Image?` overload.
    public ImageChannelValues MAE(ImageChannelDesc desc, Image @ref) => MAE(desc, @ref, false, out _);

    public ImageChannelValues MAE(ImageChannelDesc desc, Image @ref, out Image errorImage)
    {
        ImageChannelValues r = MAE(desc, @ref, true, out Image? e);
        errorImage = e!;
        return r;
    }

    private ImageChannelValues MAE(ImageChannelDesc desc, Image @ref, bool wantImage, out Image? errorImage)
    {
        double[] sumError = new double[desc.Size];

        ImageChannelDesc refDesc = @ref.GetChannelDesc(ChannelNames(desc).ToArray());
        CHECK((bool)refDesc);
        CHECK(Resolution() == @ref.Resolution());

        errorImage = wantImage ? new Image(PixelFormat.Float, Resolution(), channelNames) : null;

        for (int y = 0; y < Resolution().y; ++y)
            for (int x = 0; x < Resolution().x; ++x)
            {
                ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                ImageChannelValues vref = @ref.GetChannels(new Point2i(x, y), refDesc);

                for (int c = 0; c < desc.Size; ++c)
                {
                    double error = Math.Abs((double)v[c] - (double)vref[c]);
                    if (double.IsInfinity(error))
                        continue;
                    sumError[c] += error;
                    errorImage?.SetChannel(new Point2i(x, y), c, (Float)error);
                }
            }

        ImageChannelValues result = new(desc.Size);
        for (int c = 0; c < desc.Size; ++c)
            result[c] = (Float)(sumError[c] / ((Float)Resolution().x * (Float)Resolution().y));
        return result;
    }

    public ImageChannelValues MSE(ImageChannelDesc desc, Image @ref) => MSE(desc, @ref, false, out _);

    public ImageChannelValues MSE(ImageChannelDesc desc, Image @ref, out Image mseImage)
    {
        ImageChannelValues r = MSE(desc, @ref, true, out Image? e);
        mseImage = e!;
        return r;
    }

    private ImageChannelValues MSE(ImageChannelDesc desc, Image @ref, bool wantImage, out Image? mseImage)
    {
        double[] sumSE = new double[desc.Size];

        ImageChannelDesc refDesc = @ref.GetChannelDesc(ChannelNames(desc).ToArray());
        if (!refDesc)
            ErrorExit($"Channels not found in image: [ {string.Join(", ", ChannelNames(desc))} ]");

        CHECK(Resolution() == @ref.Resolution());

        mseImage = wantImage ? new Image(PixelFormat.Float, Resolution(), channelNames) : null;

        for (int y = 0; y < Resolution().y; ++y)
            for (int x = 0; x < Resolution().x; ++x)
            {
                ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                ImageChannelValues vref = @ref.GetChannels(new Point2i(x, y), refDesc);

                for (int c = 0; c < desc.Size; ++c)
                {
                    double se = Sqr((double)v[c] - (double)vref[c]);
                    if (double.IsInfinity(se))
                        continue;
                    sumSE[c] += se;
                    mseImage?.SetChannel(new Point2i(x, y), c, (Float)se);
                }
            }

        ImageChannelValues mse = new(desc.Size);
        for (int c = 0; c < desc.Size; ++c)
            mse[c] = (Float)(sumSE[c] / ((Float)Resolution().x * (Float)Resolution().y));
        return mse;
    }

    public ImageChannelValues MRSE(ImageChannelDesc desc, Image @ref) => MRSE(desc, @ref, false, out _);

    public ImageChannelValues MRSE(ImageChannelDesc desc, Image @ref, out Image mrseImage)
    {
        ImageChannelValues r = MRSE(desc, @ref, true, out Image? e);
        mrseImage = e!;
        return r;
    }

    private ImageChannelValues MRSE(ImageChannelDesc desc, Image @ref, bool wantImage, out Image? mrseImage)
    {
        double[] sumRSE = new double[desc.Size];

        ImageChannelDesc refDesc = @ref.GetChannelDesc(ChannelNames(desc).ToArray());
        CHECK((bool)refDesc);
        CHECK(Resolution() == @ref.Resolution());

        mrseImage = wantImage ? new Image(PixelFormat.Float, Resolution(), channelNames) : null;

        for (int y = 0; y < Resolution().y; ++y)
            for (int x = 0; x < Resolution().x; ++x)
            {
                ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                ImageChannelValues vref = @ref.GetChannels(new Point2i(x, y), refDesc);

                for (int c = 0; c < desc.Size; ++c)
                {
                    double rse = Sqr((double)v[c] - (double)vref[c]) / Sqr(vref[c] + 0.01);
                    if (double.IsInfinity(rse))
                        continue;
                    sumRSE[c] += rse;
                    mrseImage?.SetChannel(new Point2i(x, y), c, (Float)rse);
                }
            }

        ImageChannelValues mrse = new(desc.Size);
        for (int c = 0; c < desc.Size; ++c)
            mrse[c] = (Float)(sumRSE[c] / ((Float)Resolution().x * (Float)Resolution().y));
        return mrse;
    }

    public ImageChannelValues Average(ImageChannelDesc desc)
    {
        double[] sum = new double[desc.Size];

        for (int y = 0; y < Resolution().y; ++y)
            for (int x = 0; x < Resolution().x; ++x)
            {
                ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                for (int c = 0; c < desc.Size; ++c)
                    sum[c] += v[c];
            }

        ImageChannelValues average = new(desc.Size);
        for (int c = 0; c < desc.Size; ++c)
            average[c] = (Float)(sum[c] / ((Float)Resolution().x * (Float)Resolution().y));
        return average;
    }

    // pbrt's ForExtent(): calls op with the offset of each channel of each
    // pixel in _extent_, in order.
    private void ForExtent<F>(Bounds2i extent, WrapMode2D wrapMode, ref F op) where F : struct, IOffsetOp
    {
        CHECK_LT(extent.pMin.x, extent.pMax.x);
        CHECK_LT(extent.pMin.y, extent.pMax.y);

        int nx = extent.pMax[0] - extent.pMin[0];
        int nc = NChannels();
        if (Intersect(extent, new Bounds2i(new Point2i(0, 0), Resolution())) == extent)
        {
            // All in bounds
            for (int y = extent.pMin[1]; y < extent.pMax[1]; ++y)
            {
                int offset = PixelOffset(new Point2i(extent.pMin[0], y));
                for (int x = 0; x < nx; ++x)
                    for (int c = 0; c < nc; ++c)
                        op.Apply(offset++);
            }
        }
        else
        {
            for (int y = extent.pMin[1]; y < extent.pMax[1]; ++y)
            {
                for (int x = 0; x < nx; ++x)
                {
                    Point2i p = new(extent.pMin[0] + x, y);
                    // FIXME: this will return false on Black wrap mode
                    CHECK(RemapPixelCoords(ref p, Resolution(), wrapMode));
                    int offset = PixelOffset(p);
                    for (int c = 0; c < nc; ++c)
                        op.Apply(offset++);
                }
            }
        }
    }

    private interface IOffsetOp
    {
        void Apply(int offset);
    }

    // The ForExtent() lambdas of CopyRectOut()/CopyRectIn(), as structs
    // holding the buffer position (lambdas can't capture spans).
    private unsafe struct ReadU256(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset)
        {
#if PBRT_FLOAT_AS_DOUBLE
            Float v = 0;
            image.encoding!.ToLinear(image.p8.AsSpan(offset, 1), new Span<Float>(ref v));
            *buf = (float)v;
#else
            image.encoding!.ToLinear(image.p8.AsSpan(offset, 1), new Span<Float>(buf, 1));
#endif
            ++buf;
        }
    }

    private unsafe struct ReadHalf(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset) => *buf++ = (float)image.p16[offset];
    }

    private unsafe struct ReadFloat(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset) => *buf++ = image.p32[offset];
    }

    private unsafe struct WriteU256(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset)
        {
            Float v = *buf++;
            image.encoding!.FromLinear(new ReadOnlySpan<Float>(in v), image.p8.AsSpan(offset, 1));
        }
    }

    private unsafe struct WriteHalf(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset) => image.p16[offset] = (Half)(*buf++);
    }

    private unsafe struct WriteFloat(Image image, float* buf) : IOffsetOp
    {
        private float* buf = buf;
        public void Apply(int offset) => image.p32[offset] = *buf++;
    }

    public void CopyRectOut(Bounds2i extent, Span<float> buf) => CopyRectOut(extent, buf, WrapMode.Clamp);

    public unsafe void CopyRectOut(Bounds2i extent, Span<float> buf, WrapMode2D wrapMode)
    {
        CHECK_GE(buf.Length, extent.Area() * NChannels());

        fixed (float* bufPtr = buf)
        {
            switch (format)
            {
                case PixelFormat.U256:
                    if (Intersect(extent, new Bounds2i(new Point2i(0, 0), resolution)) == extent)
                    {
                        // All in bounds
                        int count = NChannels() * (extent.pMax.x - extent.pMin.x);
                        int bufIter = 0;
                        for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
                        {
                            // Convert scanlines all at once.
                            int offset = PixelOffset(new Point2i(extent.pMin.x, y));
#if PBRT_FLOAT_AS_DOUBLE
                            for (int i = 0; i < count; ++i)
                            {
                                Float v = 0;
                                encoding!.ToLinear(p8.AsSpan(offset + i, 1), new Span<Float>(ref v));
                                buf[bufIter++] = (float)v;
                            }
#else
                            encoding!.ToLinear(p8.AsSpan(offset, count), buf.Slice(bufIter, count));
                            bufIter += count;
#endif
                        }
                    }
                    else
                    {
                        ReadU256 op = new(this, bufPtr);
                        ForExtent(extent, wrapMode, ref op);
                    }
                    break;

                case PixelFormat.Half:
                {
                    ReadHalf op = new(this, bufPtr);
                    ForExtent(extent, wrapMode, ref op);
                    break;
                }

                case PixelFormat.Float:
                {
                    ReadFloat op = new(this, bufPtr);
                    ForExtent(extent, wrapMode, ref op);
                    break;
                }

                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }
        }
    }

    public unsafe void CopyRectIn(Bounds2i extent, ReadOnlySpan<float> buf)
    {
        CHECK_GE(buf.Length, extent.Area() * NChannels());

        fixed (float* bufPtr = buf)
        {
            switch (format)
            {
                case PixelFormat.U256:
                    if (Intersect(extent, new Bounds2i(new Point2i(0, 0), resolution)) == extent)
                    {
                        // All in bounds
                        int count = NChannels() * (extent.pMax.x - extent.pMin.x);
                        int bufIter = 0;
                        for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
                        {
                            // Convert scanlines all at once.
                            int offset = PixelOffset(new Point2i(extent.pMin.x, y));
#if PBRT_FLOAT_AS_DOUBLE
                            for (int i = 0; i < count; ++i)
                            {
                                Float v = buf[bufIter++];
                                encoding!.FromLinear(new ReadOnlySpan<Float>(in v), p8.AsSpan(offset + i, 1));
                            }
#else
                            encoding!.FromLinear(buf.Slice(bufIter, count), p8.AsSpan(offset, count));
                            bufIter += count;
#endif
                        }
                    }
                    else
                    {
                        WriteU256 op = new(this, bufPtr);
                        ForExtent(extent, WrapMode.Clamp, ref op);
                    }
                    break;

                case PixelFormat.Half:
                {
                    WriteHalf op = new(this, bufPtr);
                    ForExtent(extent, WrapMode.Clamp, ref op);
                    break;
                }

                case PixelFormat.Float:
                {
                    WriteFloat op = new(this, bufPtr);
                    ForExtent(extent, WrapMode.Clamp, ref op);
                    break;
                }

                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }
        }
    }

    public ImageChannelValues LookupNearest(Point2f p) => LookupNearest(p, WrapMode.Clamp);

    public ImageChannelValues LookupNearest(Point2f p, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(NChannels(), 0);
        for (int c = 0; c < NChannels(); ++c)
            cv[c] = LookupNearestChannel(p, c, wrapMode);
        return cv;
    }

    public ImageChannelValues LookupNearest(Point2f p, ImageChannelDesc desc) =>
        LookupNearest(p, desc, WrapMode.Clamp);

    public ImageChannelValues LookupNearest(Point2f p, ImageChannelDesc desc, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(desc.Size, 0);
        for (int i = 0; i < desc.Size; ++i)
            cv[i] = LookupNearestChannel(p, desc.offset[i], wrapMode);
        return cv;
    }

    public ImageChannelValues Bilerp(Point2f p) => Bilerp(p, WrapMode.Clamp);

    public ImageChannelValues Bilerp(Point2f p, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(NChannels(), 0);
        for (int c = 0; c < NChannels(); ++c)
            cv[c] = BilerpChannel(p, c, wrapMode);
        return cv;
    }

    public ImageChannelValues Bilerp(Point2f p, ImageChannelDesc desc) => Bilerp(p, desc, WrapMode.Clamp);

    public ImageChannelValues Bilerp(Point2f p, ImageChannelDesc desc, WrapMode2D wrapMode)
    {
        ImageChannelValues cv = new(desc.Size, 0);
        for (int i = 0; i < desc.Size; ++i)
            cv[i] = BilerpChannel(p, desc.offset[i], wrapMode);
        return cv;
    }

    public void SetChannels(Point2i p, in ImageChannelValues values)
    {
        CHECK_LE(values.Size, NChannels());
        for (int i = 0; i < values.Size; ++i)
            SetChannel(p, i, values[i]);
    }

    public void SetChannels(Point2i p, ReadOnlySpan<Float> values)
    {
        CHECK_LE(values.Length, NChannels());
        for (int i = 0; i < values.Length; ++i)
            SetChannel(p, i, values[i]);
    }

    public void FlipY()
    {
        for (int y = 0; y < resolution.y / 2; ++y)
        {
            for (int x = 0; x < resolution.x; ++x)
            {
                int o1 = PixelOffset(new Point2i(x, y)), o2 = PixelOffset(new Point2i(x, resolution.y - 1 - y));
                for (int c = 0; c < NChannels(); ++c)
                {
                    if (Is8Bit(format))
                        (p8[o1 + c], p8[o2 + c]) = (p8[o2 + c], p8[o1 + c]);
                    else if (Is16Bit(format))
                        (p16[o1 + c], p16[o2 + c]) = (p16[o2 + c], p16[o1 + c]);
                    else if (Is32Bit(format))
                        (p32[o1 + c], p32[o2 + c]) = (p32[o2 + c], p32[o1 + c]);
                    else
                        LOG_FATAL("unexpected format");
                }
            }
        }
    }

    public Image JointBilateralFilter(ImageChannelDesc toFilterDesc, int halfWidth,
                                      ReadOnlySpan<Float> xySigma, ImageChannelDesc jointDesc,
                                      ImageChannelValues jointSigma)
    {
        CHECK_EQ(jointDesc.Size, jointSigma.Size);
        Image result = new(PixelFormat.Float, resolution, ChannelNames(toFilterDesc).ToArray());

        List<Float> fx = [], fy = [];
        for (int i = 0; i <= halfWidth; ++i)
        {
            fx.Add(Gaussian(i, 0, xySigma[0]));
            fy.Add(Gaussian(i, 0, xySigma[1]));
        }

        ParallelFor(0, resolution.y, (long y0, long y1) =>
        {
            for (int y = (int)y0; y < y1; ++y)
            {
                for (int x = 0; x < resolution.x; ++x)
                {
                    ImageChannelValues jointPixelChannels = GetChannels(new Point2i(x, y), jointDesc);
                    ImageChannelValues filteredSum = new(toFilterDesc.Size, 0);
                    Float weightSum = 0;

                    for (int dy = -halfWidth + 1; dy < halfWidth; ++dy)
                    {
                        if (y + dy < 0 || y + dy >= resolution.y)
                            continue;
                        for (int dx0 = -halfWidth + 1; dx0 < halfWidth; ++dx0)
                        {
                            // (pbrt has this inner loop twice, shadowing dx.)
                            for (int dx = -halfWidth + 1; dx < halfWidth; ++dx)
                            {
                                if (x + dx < 0 || x + dx >= resolution.x)
                                    continue;
                                ImageChannelValues jointOtherChannels =
                                    GetChannels(new Point2i(x + dx, y + dy), jointDesc);
                                Float weight = fx[Math.Abs(dx)] * fy[Math.Abs(dy)];
                                for (int c = 0; c < jointDesc.Size; ++c)
                                    weight *= Gaussian(jointPixelChannels[c],
                                                       jointOtherChannels[c], jointSigma[c]);
                                weightSum += weight;

                                ImageChannelValues filterChannels =
                                    GetChannels(new Point2i(x + dx, y + dy), toFilterDesc);
                                for (int c = 0; c < filterChannels.Size; ++c)
                                    filteredSum[c] += weight * filterChannels[c];
                            }
                        }
                    }
                    if (weightSum > 0)
                        for (int c = 0; c < filteredSum.Size; ++c)
                            filteredSum[c] /= weightSum;
                    result.SetChannels(new Point2i(x, y), filteredSum);
                }
            }
        });

        return result;
    }

    public Image SelectChannels(ImageChannelDesc desc)
    {
        List<string> descChannelNames = [];
        // TODO: descChannelNames = ChannelNames(desc)
        for (int i = 0; i < desc.Size; ++i)
            descChannelNames.Add(channelNames[desc.offset[i]]);

        Image image = new(format, resolution, descChannelNames.ToArray(), encoding);
        int nc = NChannels(), nd = desc.Size;
        ReadOnlySpan<int> offset = desc.offset;
        for (int y = 0; y < resolution.y; ++y)
            for (int x = 0; x < resolution.x; ++x)
            {
                int src = PixelOffset(new Point2i(x, y));
                int dst = image.PixelOffset(new Point2i(x, y));
                switch (format)
                {
                    case PixelFormat.U256:
                        for (int i = 0; i < nd; ++i)
                            image.p8[dst + i] = p8[src + offset[i]];
                        break;
                    case PixelFormat.Half:
                        for (int i = 0; i < nd; ++i)
                            image.p16[dst + i] = p16[src + offset[i]];
                        break;
                    case PixelFormat.Float:
                        for (int i = 0; i < nd; ++i)
                            image.p32[dst + i] = p32[src + offset[i]];
                        break;
                    default:
                        LOG_FATAL("Unhandled PixelFormat");
                        break;
                }
            }

        return image;
    }

    public Image Crop(Bounds2i bounds)
    {
        CHECK_GT(bounds.Area(), 0);
        CHECK(bounds.pMin.x >= 0 && bounds.pMin.y >= 0);
        Image image = new(format, new Point2i(bounds.pMax - bounds.pMin), channelNames, encoding);
        foreach (Point2i p in bounds)
            for (int c = 0; c < NChannels(); ++c)
                image.SetChannel(new Point2i(p - bounds.pMin), c, GetChannel(p, c));
        return image;
    }

    public override string ToString() =>
        $"[ Image format: {format} resolution: {resolution} channelNames: [ {string.Join(", ", channelNames)} ] encoding: {encoding?.ToString() ?? "(nullptr)"} ]";
}

public static partial class Util
{
    // PixelFormat Inline Functions
    public static bool Is8Bit(PixelFormat format) => format == PixelFormat.U256;
    public static bool Is16Bit(PixelFormat format) => format == PixelFormat.Half;
    public static bool Is32Bit(PixelFormat format) => format == PixelFormat.Float;

    public static int TexelBytes(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.U256:
                return 1;
            case PixelFormat.Half:
                return 2;
            case PixelFormat.Float:
                return 4;
            default:
                LOG_FATAL("Unhandled PixelFormat in TexelBytes()");
                return 0;
        }
    }

    public static WrapMode? ParseWrapMode(string w)
    {
        if (w == "clamp")
            return WrapMode.Clamp;
        else if (w == "repeat")
            return WrapMode.Repeat;
        else if (w == "black")
            return WrapMode.Black;
        else if (w == "octahedralsphere")
            return WrapMode.OctahedralSphere;
        else
            return null;
    }

    // Inside types with their own ToString(), call this as Util.ToString().
    public static string ToString(WrapMode mode)
    {
        switch (mode)
        {
            case WrapMode.Clamp:
                return "clamp";
            case WrapMode.Repeat:
                return "repeat";
            case WrapMode.Black:
                return "black";
            case WrapMode.OctahedralSphere:
                return "octahedralsphere";
            default:
                LOG_FATAL("Unhandled wrap mode");
                return "";
        }
    }

    // Image Wrapping Inline Functions
    public static bool RemapPixelCoords(ref Point2i p, Point2i resolution, WrapMode2D wrapMode)
    {
        if (wrapMode.wrap[0] == WrapMode.OctahedralSphere)
        {
            CHECK(wrapMode.wrap[1] == WrapMode.OctahedralSphere);
            if (p[0] < 0)
            {
                p[0] = -p[0];                     // mirror across u = 0
                p[1] = resolution[1] - 1 - p[1];  // mirror across v = 0.5
            }
            else if (p[0] >= resolution[0])
            {
                p[0] = 2 * resolution[0] - 1 - p[0];  // mirror across u = 1
                p[1] = resolution[1] - 1 - p[1];      // mirror across v = 0.5
            }

            if (p[1] < 0)
            {
                p[0] = resolution[0] - 1 - p[0];  // mirror across u = 0.5
                p[1] = -p[1];                     // mirror across v = 0;
            }
            else if (p[1] >= resolution[1])
            {
                p[0] = resolution[0] - 1 - p[0];      // mirror across u = 0.5
                p[1] = 2 * resolution[1] - 1 - p[1];  // mirror across v = 1
            }

            // Bleh: things don't go as expected for 1x1 images.
            if (resolution[0] == 1)
                p[0] = 0;
            if (resolution[1] == 1)
                p[1] = 0;

            return true;
        }

        for (int c = 0; c < 2; ++c)
        {
            if (p[c] >= 0 && p[c] < resolution[c])
                // in bounds
                continue;

            switch (wrapMode.wrap[c])
            {
                case WrapMode.Repeat:
                    p[c] = Mod(p[c], resolution[c]);
                    break;
                case WrapMode.Clamp:
                    p[c] = Clamp(p[c], 0, resolution[c] - 1);
                    break;
                case WrapMode.Black:
                    return false;
                default:
                    LOG_FATAL("Unhandled WrapMode mode");
                    break;
            }
        }
        return true;
    }
}

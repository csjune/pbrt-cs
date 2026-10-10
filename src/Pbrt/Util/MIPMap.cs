// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/mipmap.h and mipmap.cpp
//
// pbrt's Filter<T>() is defined for T = Float and RGB; here the shared code
// is generic over a struct that provides the per-type texel lookups (so the
// JIT specializes it like the C++ templates), and Filter<T>() picks it.

namespace Pbrt;

// FilterFunction Definition
// (The enum names are what pbrt's ToString(FilterFunction) returns.)
public enum FilterFunction { Point, Bilinear, Trilinear, EWA }

// MIPMapFilterOptions Definition
public record struct MIPMapFilterOptions : IComparable<MIPMapFilterOptions>
{
    public FilterFunction filter = FilterFunction.EWA;
    public Float maxAnisotropy = 8f;

    public MIPMapFilterOptions() { }

    public readonly int CompareTo(MIPMapFilterOptions o)
    {
        int c = filter.CompareTo(o.filter);
        return c != 0 ? c : maxAnisotropy.CompareTo(o.maxAnisotropy);
    }

    public static bool operator <(MIPMapFilterOptions a, MIPMapFilterOptions b) => a.CompareTo(b) < 0;
    public static bool operator >(MIPMapFilterOptions a, MIPMapFilterOptions b) => a.CompareTo(b) > 0;

    public override readonly string ToString() =>
        FormattableString.Invariant($"[ MIPMapFilterOptions filter: {filter} maxAnisotropy: {maxAnisotropy} ]");
}

// MIPMap Definition
public sealed class MIPMap
{
    // MIPMap Private Members
    private static readonly StatMemoryCounter imageMapBytes = new("Memory/Image maps");

    private readonly List<Image> pyramid;
    private readonly RGBColorSpace colorSpace;
    private readonly WrapMode wrapMode;
    private readonly MIPMapFilterOptions options;

    // MIPMap Method Definitions
    public MIPMap(Image image, RGBColorSpace colorSpace, WrapMode wrapMode, MIPMapFilterOptions options)
    {
        CHECK(colorSpace != null);
        this.colorSpace = colorSpace;
        this.wrapMode = wrapMode;
        this.options = options;
        pyramid = Image.GeneratePyramid(image, wrapMode);
        if (Options.disableImageTextures)
        {
            Image top = pyramid[^1];
            pyramid.Clear();
            pyramid.Add(top);
        }
        foreach (Image im in pyramid)
            imageMapBytes.Add(im.BytesUsed());
    }

    public static MIPMap CreateFromFile(string filename, MIPMapFilterOptions options, WrapMode wrapMode,
                                        ColorEncoding? encoding)
    {
        ImageAndMetadata imageAndMetadata = Image.Read(filename, encoding);

        Image image = imageAndMetadata.image;
        if (image.NChannels() != 1)
        {
            // Get the channels in a canonical order..
            ImageChannelDesc rgbaDesc = image.GetChannelDesc(["R", "G", "B", "A"]);
            ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
            if (rgbaDesc)
            {
                // Is alpha all ones?
                bool allOne = true;
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                        if (image.GetChannels(new Point2i(x, y), rgbaDesc)[3] != 1)
                            allOne = false;
                if (allOne)
                    image = image.SelectChannels(rgbDesc);
                else
                    image = image.SelectChannels(rgbaDesc);
            }
            else
            {
                if (rgbDesc)
                    image = image.SelectChannels(rgbDesc);
                else
                    ErrorExit($"{filename}: image doesn't have R, G, and B channels");
            }
        }

        RGBColorSpace colorSpace = imageAndMetadata.metadata.GetColorSpace();
        return new MIPMap(image, colorSpace, wrapMode, options);
    }

    // MIPMap Public Methods
    // T must be Float or RGB.
    public T Filter<T>(Point2f st, Vector2f dst0, Vector2f dst1) where T : struct
    {
        if (typeof(T) == typeof(Float))
            return (T)(object)Filter<Float, FloatTexels>(st, dst0, dst1);
        if (typeof(T) == typeof(RGB))
            return (T)(object)Filter<RGB, RGBTexels>(st, dst0, dst1);
        throw new NotSupportedException($"MIPMap.Filter<{typeof(T).Name}>() is not supported");
    }

    public override string ToString() =>
        $"[ MIPMap pyramid: [ {string.Join(", ", pyramid)} ] colorSpace: {colorSpace} wrapMode: {Util.ToString(wrapMode)} options: {options} ]";

    public Point2i LevelResolution(int level)
    {
        CHECK(level >= 0 && level < pyramid.Count);
        return pyramid[level].Resolution();
    }

    public int Levels() => pyramid.Count;
    public RGBColorSpace GetRGBColorSpace() => colorSpace;
    public Image GetLevel(int level) => pyramid[level];

    // The operations Filter() needs on T: texel lookups and arithmetic.
    private interface ITexels<T>
    {
        static abstract T Texel(MIPMap m, int level, Point2i st);
        static abstract T Bilerp(MIPMap m, int level, Point2f st);
        static abstract T Lerp(Float t, T a, T b);
        static abstract T MulAdd(T sum, Float w, T v);  // sum + w * v
        static abstract T Div(T v, Float d);
    }

    private readonly struct FloatTexels : ITexels<Float>
    {
        public static Float Texel(MIPMap m, int level, Point2i st)
        {
            DCHECK(level >= 0 && level < m.pyramid.Count);
            return m.pyramid[level].GetChannel(st, 0, m.wrapMode);
        }

        public static Float Bilerp(MIPMap m, int level, Point2f st)
        {
            CHECK(level >= 0 && level < m.pyramid.Count);
            switch (m.pyramid[level].NChannels())
            {
                case 1:
                    return m.pyramid[level].BilerpChannel(st, 0, m.wrapMode);
                case 3:
                    return m.pyramid[level].Bilerp(st, m.wrapMode).Average();
                case 4:
                    // Return alpha
                    return m.pyramid[level].BilerpChannel(st, 3, m.wrapMode);
                default:
                    LOG_FATAL($"Unexpected number of image channels: {m.pyramid[level].NChannels()}");
                    return 0;
            }
        }

        public static Float Lerp(Float t, Float a, Float b) => Util.Lerp(t, a, b);
        public static Float MulAdd(Float sum, Float w, Float v) => sum + w * v;
        public static Float Div(Float v, Float d) => v / d;
    }

    private readonly struct RGBTexels : ITexels<RGB>
    {
        public static RGB Texel(MIPMap m, int level, Point2i st)
        {
            DCHECK(level >= 0 && level < m.pyramid.Count);
            Image image = m.pyramid[level];
            if (image.NChannels() is 3 or 4)
                return new RGB(image.GetChannel(st, 0, m.wrapMode),
                               image.GetChannel(st, 1, m.wrapMode),
                               image.GetChannel(st, 2, m.wrapMode));
            else
            {
                CHECK_EQ(1, image.NChannels());
                Float v = image.GetChannel(st, 0, m.wrapMode);
                return new RGB(v, v, v);
            }
        }

        public static RGB Bilerp(MIPMap m, int level, Point2f st)
        {
            DCHECK(level >= 0 && level < m.pyramid.Count);
            Image image = m.pyramid[level];
            if (image.NChannels() is 3 or 4)
                return new RGB(image.BilerpChannel(st, 0, m.wrapMode),
                               image.BilerpChannel(st, 1, m.wrapMode),
                               image.BilerpChannel(st, 2, m.wrapMode));
            else
            {
                DCHECK_EQ(1, image.NChannels());
                Float v = image.BilerpChannel(st, 0, m.wrapMode);
                return new RGB(v, v, v);
            }
        }

        public static RGB Lerp(Float t, RGB a, RGB b) => Util.Lerp(t, a, b);
        public static RGB MulAdd(RGB sum, Float w, RGB v) => sum + w * v;
        public static RGB Div(RGB v, Float d) => v / d;
    }

    private T Filter<T, O>(Point2f st, Vector2f dst0, Vector2f dst1) where O : struct, ITexels<T>
    {
        if (options.filter != FilterFunction.EWA)
        {
            // Handle non-EWA MIP Map filter
            Float width = 2 * Max(Max(FMath.Abs(dst0[0]), FMath.Abs(dst0[1])),
                                  Max(FMath.Abs(dst1[0]), FMath.Abs(dst1[1])));
            // Compute MIP Map level for _width_ and handle very wide filter
            int nLevels = Levels();
            Float level = nLevels - 1 + Log2(Max<Float>(width, (Float)1e-8));
            if (level >= Levels() - 1)
                return O.Texel(this, Levels() - 1, new Point2i(0, 0));
            int iLevel = Math.Max(0, (int)FMath.Floor(level));

            if (options.filter == FilterFunction.Point)
            {
                // Return point-sampled value at selected MIP level
                Point2i resolution = LevelResolution(iLevel);
                Point2i sti = new((int)FMath.Round(st[0] * resolution[0] - 0.5f, MidpointRounding.AwayFromZero),
                                  (int)FMath.Round(st[1] * resolution[1] - 0.5f, MidpointRounding.AwayFromZero));
                return O.Texel(this, iLevel, sti);
            }
            else if (options.filter == FilterFunction.Bilinear)
            {
                // Return bilinear-filtered value at selected MIP level
                return O.Bilerp(this, iLevel, st);
            }
            else
            {
                // Return trilinear-filtered value at selected MIP level
                DCHECK(options.filter == FilterFunction.Trilinear);
                if (iLevel == 0)
                    return O.Bilerp(this, 0, st);
                else
                {
                    DCHECK_LE(level - iLevel, 1);
                    return O.Lerp(level - iLevel, O.Bilerp(this, iLevel, st),
                                  O.Bilerp(this, iLevel + 1, st));
                }
            }
        }
        // Compute EWA ellipse axes
        if (LengthSquared(dst0) < LengthSquared(dst1))
            (dst0, dst1) = (dst1, dst0);
        Float longerVecLength = Length(dst0), shorterVecLength = Length(dst1);

        // Clamp ellipse vector ratio if too large
        if (shorterVecLength * options.maxAnisotropy < longerVecLength && shorterVecLength > 0)
        {
            Float scale = longerVecLength / (shorterVecLength * options.maxAnisotropy);
            dst1 *= scale;
            shorterVecLength *= scale;
        }
        if (shorterVecLength == 0)
            return O.Bilerp(this, 0, st);

        // Choose level of detail for EWA lookup and perform EWA filtering
        Float lod = Max<Float>(0, Levels() - 1 + Log2(shorterVecLength));
        int ilod = (int)FMath.Floor(lod);
        return O.Lerp(lod - ilod, EWA<T, O>(ilod, st, dst0, dst1), EWA<T, O>(ilod + 1, st, dst0, dst1));
    }

    private T EWA<T, O>(int level, Point2f st, Vector2f dst0, Vector2f dst1) where O : struct, ITexels<T>
    {
        if (level >= Levels())
            return O.Texel(this, Levels() - 1, new Point2i(0, 0));
        // Convert EWA coordinates to appropriate scale for level
        Point2i levelRes = LevelResolution(level);
        st[0] = st[0] * levelRes[0] - 0.5f;
        st[1] = st[1] * levelRes[1] - 0.5f;
        dst0[0] *= levelRes[0];
        dst0[1] *= levelRes[1];
        dst1[0] *= levelRes[0];
        dst1[1] *= levelRes[1];

        // Find ellipse coefficients that bound EWA filter region
        Float A = Sqr(dst0[1]) + Sqr(dst1[1]) + 1;
        Float B = -2 * (dst0[0] * dst0[1] + dst1[0] * dst1[1]);
        Float C = Sqr(dst0[0]) + Sqr(dst1[0]) + 1;
        Float invF = 1 / (A * C - Sqr(B) * 0.25f);
        A *= invF;
        B *= invF;
        C *= invF;

        // Compute the ellipse's $(s,t)$ bounding box in texture space
        Float det = -Sqr(B) + 4 * A * C;
        Float invDet = 1 / det;
        Float uSqrt = SafeSqrt(det * C), vSqrt = SafeSqrt(A * det);
        int s0 = (int)FMath.Ceiling(st[0] - 2 * invDet * uSqrt);
        int s1 = (int)FMath.Floor(st[0] + 2 * invDet * uSqrt);
        int t0 = (int)FMath.Ceiling(st[1] - 2 * invDet * vSqrt);
        int t1 = (int)FMath.Floor(st[1] + 2 * invDet * vSqrt);

        // Scan over ellipse bound and evaluate quadratic equation to filter image
        T sum = default!;
        Float sumWts = 0;
        for (int it = t0; it <= t1; ++it)
        {
            Float tt = it - st[1];
            for (int @is = s0; @is <= s1; ++@is)
            {
                Float ss = @is - st[0];
                // Compute squared radius and filter texel if it is inside the ellipse
                Float r2 = A * Sqr(ss) + B * ss * tt + C * Sqr(tt);
                if (r2 < 1)
                {
                    int index = Math.Min((int)(r2 * MIPFilterLUTSize), MIPFilterLUTSize - 1);
                    Float weight = MIPFilterLUT[index];
                    sum = O.MulAdd(sum, weight, O.Texel(this, level, new Point2i(@is, it)));
                    sumWts += weight;
                }
            }
        }
        return O.Div(sum, sumWts);
    }

    /*
            for (int i = 0; i < WeightLUTSize; ++i) {
                Float alpha = 2;
                Float r2 = Float(i) / Float(WeightLUTSize - 1);
                weightLut[i] = std::exp(-alpha * r2) - std::exp(-alpha);
            }
    */
    // MIPMap EWA Lookup Table Definition
    private const int MIPFilterLUTSize = 128;
    private static ReadOnlySpan<Float> MIPFilterLUT =>
    [
        // MIPMap EWA Lookup Table Values
        0.864664733f, 0.849040031f, 0.83365953f, 0.818519294f, 0.80361563f, 0.788944781f,
        0.774503231f, 0.760287285f, 0.746293485f, 0.732518315f, 0.718958378f, 0.705610275f,
        0.692470789f, 0.679536581f, 0.666804492f, 0.654271305f, 0.641933978f, 0.629789352f,
        0.617834508f, 0.606066525f, 0.594482362f, 0.583079159f, 0.571854174f, 0.560804546f,
        0.549927592f, 0.539220572f, 0.528680861f, 0.518305838f, 0.50809288f, 0.498039544f,
        0.488143265f, 0.478401601f, 0.468812168f, 0.45937258f, 0.450080454f, 0.440933526f,
        0.431929469f, 0.423066139f, 0.414341331f, 0.405752778f, 0.397298455f, 0.388976216f,
        0.380784035f, 0.372719884f, 0.364781618f, 0.356967449f, 0.34927541f, 0.341703475f,
        0.334249914f, 0.32691282f, 0.319690347f, 0.312580705f, 0.305582166f, 0.298692942f,
        0.291911423f, 0.285235822f, 0.278664529f, 0.272195935f, 0.265828371f, 0.259560347f,
        0.253390193f, 0.247316495f, 0.241337672f, 0.235452279f, 0.229658857f, 0.223955944f,
        0.21834214f, 0.212816045f, 0.207376286f, 0.202021524f, 0.196750447f, 0.191561714f,
        0.186454013f, 0.181426153f, 0.176476851f, 0.171604887f, 0.166809067f, 0.162088141f,
        0.157441005f, 0.152866468f, 0.148363426f, 0.143930718f, 0.139567271f, 0.135272011f,
        0.131043866f, 0.126881793f, 0.122784719f, 0.11875169f, 0.114781633f, 0.11087364f,
        0.107026696f, 0.103239879f, 0.0995122194f, 0.0958427936f, 0.0922307223f, 0.0886750817f,
        0.0851749927f, 0.0817295909f, 0.0783380121f, 0.0749994367f, 0.0717130303f, 0.0684779733f,
        0.0652934611f, 0.0621587038f, 0.0590728968f, 0.0560353249f, 0.0530452281f, 0.0501018465f,
        0.0472044498f, 0.0443523228f, 0.0415447652f, 0.0387810767f, 0.0360605568f, 0.0333825648f,
        0.0307464004f, 0.0281514227f, 0.0255970061f, 0.0230824798f, 0.0206072628f, 0.0181707144f,
        0.0157722086f, 0.013411209f, 0.0110870898f, 0.0087992847f, 0.0065472275f, 0.00433036685f,
        0.0021481365f, 0f,
    ];
}

public static partial class Util
{
    public static FilterFunction? ParseFilter(string f)
    {
        if (f == "ewa" || f == "EWA")
            return FilterFunction.EWA;
        else if (f == "trilinear")
            return FilterFunction.Trilinear;
        else if (f == "bilinear")
            return FilterFunction.Bilinear;
        else if (f == "point")
            return FilterFunction.Point;
        else
            return null;
    }
}

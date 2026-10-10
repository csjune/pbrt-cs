// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/color.h and color.cpp
//
// RGBSigmoidPolynomial's and RGBToSpectrumTable's function call operators
// become Evaluate(). The RGB to spectrum tables are generated at build time
// by src/Pbrt.Rgb2SpecOpt and embedded in the assembly (see Pbrt.csproj).

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Pbrt;

// RGB Definition
public struct RGB : IEquatable<RGB>
{
    // RGB Public Members
    public Float r, g, b;

    // RGB Public Methods
    public RGB(Float r, Float g, Float b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public static RGB operator +(RGB a, RGB s) => new(a.r + s.r, a.g + s.g, a.b + s.b);
    public static RGB operator -(RGB a, RGB s) => new(a.r - s.r, a.g - s.g, a.b - s.b);
    public static RGB operator -(Float a, RGB s) => new(a - s.r, a - s.g, a - s.b);
    public static RGB operator *(RGB a, RGB s) => new(a.r * s.r, a.g * s.g, a.b * s.b);

    public static RGB operator *(RGB s, Float a)
    {
        DCHECK(!IsNaN(a));
        return new(a * s.r, a * s.g, a * s.b);
    }

    public static RGB operator *(Float a, RGB s) => s * a;
    public static RGB operator /(RGB a, RGB s) => new(a.r / s.r, a.g / s.g, a.b / s.b);

    public static RGB operator /(RGB s, Float a)
    {
        DCHECK(!IsNaN(a));
        DCHECK_NE(a, 0);
        return new(s.r / a, s.g / a, s.b / a);
    }

    public static RGB operator -(RGB s) => new(-s.r, -s.g, -s.b);

    // SquareMatrix<3> * RGB (pbrt's Mul<RGB>()).
    public static RGB operator *(in SquareMatrix3 m, RGB v)
    {
        RGB result = default;
        for (int i = 0; i < 3; ++i)
        {
            Float r = 0;
            for (int j = 0; j < 3; ++j)
                r += m[i, j] * v[j];
            result[i] = r;
        }
        return result;
    }

    public readonly Float Average() => (r + g + b) / 3;

    public static bool operator ==(RGB a, RGB s) => a.r == s.r && a.g == s.g && a.b == s.b;
    public static bool operator !=(RGB a, RGB s) => a.r != s.r || a.g != s.g || a.b != s.b;
    public readonly bool Equals(RGB other) => this == other;
    public override readonly bool Equals(object? obj) => obj is RGB other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(r, g, b);

    public Float this[int c]
    {
        readonly get
        {
            DCHECK(c >= 0 && c < 3);
            if (c == 0)
                return r;
            else if (c == 1)
                return g;
            return b;
        }
        set
        {
            DCHECK(c >= 0 && c < 3);
            if (c == 0)
                r = value;
            else if (c == 1)
                g = value;
            else
                b = value;
        }
    }

    public override readonly string ToString() => FormattableString.Invariant($"[ {r} {g} {b} ]");
}

// XYZ Definition
public struct XYZ : IEquatable<XYZ>
{
    // XYZ Public Members
    public Float X, Y, Z;

    // XYZ Public Methods
    public XYZ(Float X, Float Y, Float Z)
    {
        this.X = X;
        this.Y = Y;
        this.Z = Z;
    }

    public readonly Float Average() => (X + Y + Z) / 3;

    public readonly Point2f xy() => new(X / (X + Y + Z), Y / (X + Y + Z));

    public static XYZ FromxyY(Point2f xy, Float Y = 1)
    {
        if (xy.y == 0)
            return new XYZ(0, 0, 0);
        return new XYZ(xy.x * Y / xy.y, Y, (1 - xy.x - xy.y) * Y / xy.y);
    }

    public static XYZ operator +(XYZ a, XYZ s) => new(a.X + s.X, a.Y + s.Y, a.Z + s.Z);
    public static XYZ operator -(XYZ a, XYZ s) => new(a.X - s.X, a.Y - s.Y, a.Z - s.Z);
    public static XYZ operator -(Float a, XYZ s) => new(a - s.X, a - s.Y, a - s.Z);
    public static XYZ operator *(XYZ a, XYZ s) => new(a.X * s.X, a.Y * s.Y, a.Z * s.Z);

    public static XYZ operator *(XYZ s, Float a)
    {
        DCHECK(!IsNaN(a));
        return new(a * s.X, a * s.Y, a * s.Z);
    }

    public static XYZ operator *(Float a, XYZ s) => s * a;
    public static XYZ operator /(XYZ a, XYZ s) => new(a.X / s.X, a.Y / s.Y, a.Z / s.Z);

    public static XYZ operator /(XYZ s, Float a)
    {
        DCHECK(!IsNaN(a));
        DCHECK_NE(a, 0);
        return new(s.X / a, s.Y / a, s.Z / a);
    }

    public static XYZ operator -(XYZ s) => new(-s.X, -s.Y, -s.Z);

    // SquareMatrix<3> * XYZ (pbrt's Mul<XYZ>()).
    public static XYZ operator *(in SquareMatrix3 m, XYZ v)
    {
        XYZ result = default;
        for (int i = 0; i < 3; ++i)
        {
            Float r = 0;
            for (int j = 0; j < 3; ++j)
                r += m[i, j] * v[j];
            result[i] = r;
        }
        return result;
    }

    public static bool operator ==(XYZ a, XYZ s) => a.X == s.X && a.Y == s.Y && a.Z == s.Z;
    public static bool operator !=(XYZ a, XYZ s) => a.X != s.X || a.Y != s.Y || a.Z != s.Z;
    public readonly bool Equals(XYZ other) => this == other;
    public override readonly bool Equals(object? obj) => obj is XYZ other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

    public Float this[int c]
    {
        readonly get
        {
            DCHECK(c >= 0 && c < 3);
            if (c == 0)
                return X;
            else if (c == 1)
                return Y;
            return Z;
        }
        set
        {
            DCHECK(c >= 0 && c < 3);
            if (c == 0)
                X = value;
            else if (c == 1)
                Y = value;
            else
                Z = value;
        }
    }

    public override readonly string ToString() => FormattableString.Invariant($"[ {X} {Y} {Z} ]");
}

// RGBSigmoidPolynomial Definition
public readonly struct RGBSigmoidPolynomial
{
    // RGBSigmoidPolynomial Private Members
    private readonly Float c0, c1, c2;

    // RGBSigmoidPolynomial Public Methods
    public RGBSigmoidPolynomial(Float c0, Float c1, Float c2)
    {
        this.c0 = c0;
        this.c1 = c1;
        this.c2 = c2;
    }

    public override string ToString() =>
        FormattableString.Invariant($"[ RGBSigmoidPolynomial c0: {c0} c1: {c1} c2: {c2} ]");

    public Float Evaluate(Float lambda) => s(EvaluatePolynomial(lambda, c2, c1, c0));

    public Float MaxValue()
    {
        Float result = Max(Evaluate(360), Evaluate(830));
        Float lambda = -c1 / (2 * c0);
        if (lambda >= 360 && lambda <= 830)
            result = Max(result, Evaluate(lambda));
        return result;
    }

    // RGBSigmoidPolynomial Private Methods
    private static Float s(Float x)
    {
        if (IsInf(x))
            return x > 0 ? 1 : 0;
        return .5f + x / (2 * FMath.Sqrt(1 + Sqr(x)));
    }
}

// RGBToSpectrumTable Definition
public sealed class RGBToSpectrumTable
{
    // RGBToSpectrumTable Public Constants
    public const int res = 64;

    // RGBToSpectrumTable Private Members
    private readonly float[] zNodes;
    // coeffs[3][res][res][res][3], flattened.
    private readonly float[] coeffs;

    // RGBToSpectrumTable Public Methods
    public RGBToSpectrumTable(float[] zNodes, float[] coeffs)
    {
        CHECK_EQ(res, zNodes.Length);
        CHECK_EQ(3 * res * res * res * 3, coeffs.Length);
        this.zNodes = zNodes;
        this.coeffs = coeffs;
    }

    public RGBSigmoidPolynomial Evaluate(RGB rgb)
    {
        DCHECK(rgb[0] >= 0f && rgb[1] >= 0f && rgb[2] >= 0f && rgb[0] <= 1f &&
               rgb[1] <= 1f && rgb[2] <= 1f);

        // Handle uniform _rgb_ values
        if (rgb[0] == rgb[1] && rgb[1] == rgb[2])
            return new RGBSigmoidPolynomial(0, 0,
                                            (rgb[0] - .5f) / FMath.Sqrt(rgb[0] * (1 - rgb[0])));

        // Find maximum component and compute remapped component values
        int maxc =
            (rgb[0] > rgb[1]) ? ((rgb[0] > rgb[2]) ? 0 : 2) : ((rgb[1] > rgb[2]) ? 1 : 2);
        float z = (float)rgb[maxc];
        float x = (float)(rgb[(maxc + 1) % 3] * (res - 1) / z);
        float y = (float)(rgb[(maxc + 2) % 3] * (res - 1) / z);

        // Compute integer indices and offsets for coefficient interpolation
        int xi = Min((int)x, res - 2), yi = Min((int)y, res - 2),
            zi = FindInterval(res, new ZNodeLess(zNodes, z));
        Float dx = x - xi, dy = y - yi, dz = (z - zNodes[zi]) / (zNodes[zi + 1] - zNodes[zi]);

        // Trilinearly interpolate sigmoid polynomial coefficients _c_
        Array3<Float> c = default;
        for (int i = 0; i < 3; ++i)
        {
            // Define _co_ lambda for looking up sigmoid polynomial coefficients
            Float co(int dx, int dy, int dz) =>
                coeffs[(((maxc * res + zi + dz) * res + yi + dy) * res + xi + dx) * 3 + i];

            c[i] = Lerp(dz,
                        Lerp(dy, Lerp(dx, co(0, 0, 0), co(1, 0, 0)),
                             Lerp(dx, co(0, 1, 0), co(1, 1, 0))),
                        Lerp(dy, Lerp(dx, co(0, 0, 1), co(1, 0, 1)),
                             Lerp(dx, co(0, 1, 1), co(1, 1, 1))));
        }

        return new RGBSigmoidPolynomial(c[0], c[1], c[2]);
    }

    // zNodes[i] < z, as a struct predicate so the lookup doesn't allocate.
    private readonly struct ZNodeLess(float[] zNodes, float z) : IIndexPredicate
    {
        public bool Evaluate(int i) => zNodes[i] < z;
    }

    public static readonly RGBToSpectrumTable sRGB = Load("sRGB");
    public static readonly RGBToSpectrumTable DCI_P3 = Load("DCI_P3");
    public static readonly RGBToSpectrumTable Rec2020 = Load("REC2020");
    public static readonly RGBToSpectrumTable ACES2065_1 = Load("ACES2065_1");

    // Reads a table written by Pbrt.Rgb2SpecOpt: int32 res, float
    // scale[res], float data[3][res][res][res][3], all little-endian.
    private static RGBToSpectrumTable Load(string gamut)
    {
        string name = $"Pbrt.RGBToSpectrumTable.{gamut}.bin";
        using Stream stream = typeof(RGBToSpectrumTable).Assembly.GetManifestResourceStream(name) ??
                              throw new FatalErrorException($"{name}: RGB to spectrum table resource not found");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        int tableRes = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (tableRes != res)
            LOG_FATAL($"{name}: table resolution {tableRes} doesn't match {res}");
        float[] scale = new float[res], data = new float[3 * res * res * res * 3];
        ReadOnlySpan<byte> rest = bytes.AsSpan(sizeof(int));
        CHECK_EQ((scale.Length + data.Length) * sizeof(float), rest.Length);
        MemoryMarshal.Cast<byte, float>(rest)[..res].CopyTo(scale);
        MemoryMarshal.Cast<byte, float>(rest)[res..].CopyTo(data);
        if (!BitConverter.IsLittleEndian)
        {
            ReverseEndianness(scale);
            ReverseEndianness(data);
        }
        return new RGBToSpectrumTable(scale, data);
    }

    private static void ReverseEndianness(Span<float> v)
    {
        Span<int> bits = MemoryMarshal.Cast<float, int>(v);
        BinaryPrimitives.ReverseEndianness(bits, bits);
    }

    public override string ToString()
    {
        string id = "";
        if (this == sRGB)
            id = "(sRGB) ";
        else if (this == DCI_P3)
            id = "(DCI_P3) ";
        else if (this == Rec2020)
            id = "(Rec2020) ";
        else if (this == ACES2065_1)
            id = "(ACES2065_1) ";
        CHECK(id.Length != 0);

        return $"[ RGBToSpectrumTable res: {res} {id}]";
    }
}

// ColorEncoding Definitions
public abstract class ColorEncoding
{
    // ColorEncoding Interface
    public abstract void ToLinear(ReadOnlySpan<byte> vin, Span<Float> vout);
    public abstract void FromLinear(ReadOnlySpan<Float> vin, Span<byte> vout);

    public abstract Float ToFloatLinear(Float v);

    public static readonly ColorEncoding Linear = new LinearColorEncoding();
    public static readonly ColorEncoding sRGB = new sRGBColorEncoding();

    private static readonly Dictionary<float, ColorEncoding> cache = [];
    private static readonly Lock mutex = new();

    public static ColorEncoding Get(string name)
    {
        if (name == "linear")
            return Linear;
        else if (name == "sRGB")
            return sRGB;
        else
        {
            List<string> @params = SplitStringsFromWhitespace(name);
            if (@params.Count != 2 || @params[0] != "gamma")
                ErrorExit($"{name}: expected \"gamma <value>\" for color encoding");
            // (atof() returns 0 if it can't parse the value.)
            if (!Atof(@params[1], out Float gamma))
                gamma = 0;
            if (gamma == 0)
                ErrorExit($"{@params[1]}: unable to parse gamma value");

            lock (mutex)
            {
                if (cache.TryGetValue((float)gamma, out ColorEncoding? enc))
                    return enc;

                enc = new GammaColorEncoding(gamma);
                cache[(float)gamma] = enc;
                LOG_VERBOSE($"Added ColorEncoding {name} for gamma {gamma} -> {enc}");
                return enc;
            }
        }
    }
}

public sealed class LinearColorEncoding : ColorEncoding
{
    public override void ToLinear(ReadOnlySpan<byte> vin, Span<Float> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = vin[i] / 255f;
    }

    public override Float ToFloatLinear(Float v) => v;

    public override void FromLinear(ReadOnlySpan<Float> vin, Span<byte> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = (byte)Clamp(vin[i] * 255f + 0.5f, 0, 255);
    }

    public override string ToString() => "[ LinearColorEncoding ]";
}

public sealed class sRGBColorEncoding : ColorEncoding
{
    // sRGBColorEncoding Public Methods
    public override void ToLinear(ReadOnlySpan<byte> vin, Span<Float> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = SRGB8ToLinear(vin[i]);
    }

    public override Float ToFloatLinear(Float v) => SRGBToLinear((float)v);

    public override void FromLinear(ReadOnlySpan<Float> vin, Span<byte> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = LinearToSRGB8(vin[i]);
    }

    public override string ToString() => "[ sRGBColorEncoding ]";
}

public sealed class GammaColorEncoding : ColorEncoding
{
    private readonly Float gamma;
    private readonly Float[] applyLUT = new Float[256];
    private readonly Float[] inverseLUT = new Float[1024];

    public GammaColorEncoding(Float gamma)
    {
        this.gamma = gamma;
        for (int i = 0; i < 256; ++i)
        {
            Float v = (Float)i / 255f;
            applyLUT[i] = FMath.Pow(v, gamma);
        }
        for (int i = 0; i < inverseLUT.Length; ++i)
        {
            Float v = (Float)i / (Float)(inverseLUT.Length - 1);
            inverseLUT[i] = Clamp(255f * FMath.Pow(v, 1f / gamma) + .5f, 0, 255);
        }
    }

    public override void ToLinear(ReadOnlySpan<byte> vin, Span<Float> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = applyLUT[vin[i]];
    }

    public override Float ToFloatLinear(Float v) => FMath.Pow(v, gamma);

    public override void FromLinear(ReadOnlySpan<Float> vin, Span<byte> vout)
    {
        DCHECK_EQ(vin.Length, vout.Length);
        for (int i = 0; i < vin.Length; ++i)
            vout[i] = (byte)inverseLUT[(int)Clamp(vin[i] * (inverseLUT.Length - 1), 0, inverseLUT.Length - 1)];
    }

    public override string ToString() => FormattableString.Invariant($"[ GammaColorEncoding gamma: {gamma} ]");
}

public static partial class Util
{
    public static RGB Max(RGB a, RGB b) => new(Max(a.r, b.r), Max(a.g, b.g), Max(a.b, b.b));

    public static RGB Lerp(Float t, RGB s1, RGB s2) => (1 - t) * s1 + t * s2;

    // RGB Inline Functions
    public static RGB Clamp(RGB rgb, Float min, Float max) =>
        new(Clamp(rgb.r, min, max), Clamp(rgb.g, min, max), Clamp(rgb.b, min, max));

    public static RGB ClampZero(RGB rgb) =>
        new(Max<Float>(0, rgb.r), Max<Float>(0, rgb.g), Max<Float>(0, rgb.b));

    public static XYZ Clamp(XYZ xyz, Float min, Float max) =>
        new(Clamp(xyz.X, min, max), Clamp(xyz.Y, min, max), Clamp(xyz.Z, min, max));

    public static XYZ ClampZero(XYZ xyz) =>
        new(Max<Float>(0, xyz.X), Max<Float>(0, xyz.Y), Max<Float>(0, xyz.Z));

    public static XYZ Lerp(Float t, XYZ s1, XYZ s2) => (1 - t) * s1 + t * s2;

    public static Float LinearToSRGB(Float value)
    {
        if (value <= 0.0031308f)
            return 12.92f * value;
        // Minimax polynomial approximation from enoki's color.h.
        float sqrtValue = (float)SafeSqrt(value);
        float p = EvaluatePolynomial(sqrtValue, -0.0016829072605308378f, 0.03453868659826638f,
                                     0.7642611304733891f, 2.0041169284241644f,
                                     0.7551545191665577f, -0.016202083165206348f);
        float q = EvaluatePolynomial(sqrtValue, 4.178892964897981e-7f,
                                     -0.00004375359692957097f, 0.03467195408529984f,
                                     0.6085338522168684f, 1.8970238036421054f, 1f);
        return p / q * value;
    }

    public static byte LinearToSRGB8(Float value, Float dither = 0)
    {
        if (value <= 0)
            return 0;
        if (value >= 1)
            return 255;
        return (byte)Clamp(FMath.Round(255f * LinearToSRGB(value) + dither, MidpointRounding.AwayFromZero), 0, 255);
    }

    public static Float SRGBToLinear(float value)
    {
        if (value <= 0.04045f)
            return value * (1 / 12.92f);
        // Minimax polynomial approximation from enoki's color.h.
        float p = EvaluatePolynomial(value, -0.0163933279112946f, -0.7386328024653209f,
                                     -11.199318357635072f, -47.46726633009393f,
                                     -36.04572663838034f);
        float q = EvaluatePolynomial(value, -0.004261480793199332f, -19.140923959601675f,
                                     -59.096406619244426f, -18.225745396846637f, 1f);
        return p / q * value;
    }

    public static Float SRGB8ToLinear(byte value) => (Float)SRGBToLinearLUT[value];

    public static ReadOnlySpan<double> SRGBToLinearLUT =>
    [
        0.0000000000, 0.0003035270, 0.0006070540, 0.0009105810, 0.0012141080, 0.0015176350,
        0.0018211619, 0.0021246888, 0.0024282159, 0.0027317430, 0.0030352699, 0.0033465356,
        0.0036765069, 0.0040247170, 0.0043914421, 0.0047769533, 0.0051815170, 0.0056053917,
        0.0060488326, 0.0065120910, 0.0069954102, 0.0074990317, 0.0080231922, 0.0085681248,
        0.0091340570, 0.0097212177, 0.0103298230, 0.0109600937, 0.0116122449, 0.0122864870,
        0.0129830306, 0.0137020806, 0.0144438436, 0.0152085144, 0.0159962922, 0.0168073755,
        0.0176419523, 0.0185002182, 0.0193823613, 0.0202885624, 0.0212190095, 0.0221738834,
        0.0231533647, 0.0241576303, 0.0251868572, 0.0262412224, 0.0273208916, 0.0284260381,
        0.0295568332, 0.0307134409, 0.0318960287, 0.0331047624, 0.0343398079, 0.0356013142,
        0.0368894450, 0.0382043645, 0.0395462364, 0.0409151986, 0.0423114114, 0.0437350273,
        0.0451862030, 0.0466650836, 0.0481718220, 0.0497065634, 0.0512694679, 0.0528606549,
        0.0544802807, 0.0561284944, 0.0578054339, 0.0595112406, 0.0612460710, 0.0630100295,
        0.0648032799, 0.0666259527, 0.0684781820, 0.0703601092, 0.0722718611, 0.0742135793,
        0.0761853904, 0.0781874284, 0.0802198276, 0.0822827145, 0.0843762159, 0.0865004659,
        0.0886556059, 0.0908417329, 0.0930589810, 0.0953074843, 0.0975873619, 0.0998987406,
        0.1022417471, 0.1046164930, 0.1070231125, 0.1094617173, 0.1119324341, 0.1144353822,
        0.1169706732, 0.1195384338, 0.1221387982, 0.1247718409, 0.1274376959, 0.1301364899,
        0.1328683347, 0.1356333494, 0.1384316236, 0.1412633061, 0.1441284865, 0.1470272839,
        0.1499598026, 0.1529261619, 0.1559264660, 0.1589608639, 0.1620294005, 0.1651322246,
        0.1682693958, 0.1714410931, 0.1746473908, 0.1778884083, 0.1811642349, 0.1844749898,
        0.1878207624, 0.1912016720, 0.1946178079, 0.1980693042, 0.2015562356, 0.2050787061,
        0.2086368501, 0.2122307271, 0.2158605307, 0.2195262313, 0.2232279778, 0.2269658893,
        0.2307400703, 0.2345506549, 0.2383976579, 0.2422811985, 0.2462013960, 0.2501583695,
        0.2541521788, 0.2581829131, 0.2622507215, 0.2663556635, 0.2704978585, 0.2746773660,
        0.2788943350, 0.2831487954, 0.2874408960, 0.2917706966, 0.2961383164, 0.3005438447,
        0.3049873710, 0.3094689548, 0.3139887452, 0.3185468316, 0.3231432438, 0.3277781308,
        0.3324515820, 0.3371636569, 0.3419144452, 0.3467040956, 0.3515326977, 0.3564002514,
        0.3613068759, 0.3662526906, 0.3712377846, 0.3762622178, 0.3813261092, 0.3864295185,
        0.3915725648, 0.3967553079, 0.4019778669, 0.4072403014, 0.4125427008, 0.4178851545,
        0.4232677519, 0.4286905527, 0.4341537058, 0.4396572411, 0.4452012479, 0.4507858455,
        0.4564110637, 0.4620770514, 0.4677838385, 0.4735315442, 0.4793202281, 0.4851499796,
        0.4910208881, 0.4969330430, 0.5028865933, 0.5088814497, 0.5149177909, 0.5209956765,
        0.5271152258, 0.5332764983, 0.5394796133, 0.5457245708, 0.5520114899, 0.5583404899,
        0.5647116303, 0.5711249113, 0.5775805116, 0.5840784907, 0.5906189084, 0.5972018838,
        0.6038274169, 0.6104956269, 0.6172066331, 0.6239604354, 0.6307572126, 0.6375969648,
        0.6444797516, 0.6514056921, 0.6583748460, 0.6653873324, 0.6724432111, 0.6795425415,
        0.6866854429, 0.6938719153, 0.7011020184, 0.7083759308, 0.7156936526, 0.7230552435,
        0.7304608822, 0.7379105687, 0.7454043627, 0.7529423237, 0.7605246305, 0.7681512833,
        0.7758223414, 0.7835379243, 0.7912980318, 0.7991028428, 0.8069523573, 0.8148466945,
        0.8227858543, 0.8307699561, 0.8387991190, 0.8468732834, 0.8549926877, 0.8631572723,
        0.8713672161, 0.8796223402, 0.8879231811, 0.8962693810, 0.9046613574, 0.9130986929,
        0.9215820432, 0.9301108718, 0.9386858940, 0.9473065734, 0.9559735060, 0.9646862745,
        0.9734454751, 0.9822505713, 0.9911022186, 1.0000000000,
    ];

    // White Balance Definitions
    // These are the Bradford transformation matrices.
    public static readonly SquareMatrix3 LMSFromXYZ = new( (Float)0.8951,  (Float)0.2664, -(Float)0.1614,
                                                          -(Float)0.7502,  (Float)1.7135,  (Float)0.0367,
                                                           (Float)0.0389, -(Float)0.0685,  (Float)1.0296);
    public static readonly SquareMatrix3 XYZFromLMS = new( (Float)0.986993,   -(Float)0.147054,  (Float)0.159963,
                                                           (Float)0.432305,    (Float)0.51836,   (Float)0.0492912,
                                                          -(Float)0.00852866,  (Float)0.0400428, (Float)0.968487);

    public static SquareMatrix3 WhiteBalance(Point2f srcWhite, Point2f targetWhite)
    {
        // Find LMS coefficients for source and target white
        XYZ srcXYZ = XYZ.FromxyY(srcWhite), dstXYZ = XYZ.FromxyY(targetWhite);
        XYZ srcLMS = LMSFromXYZ * srcXYZ, dstLMS = LMSFromXYZ * dstXYZ;

        // Return white balancing matrix for source and target white
        SquareMatrix3 LMScorrect = SquareMatrix3.Diag(
            dstLMS[0] / srcLMS[0], dstLMS[1] / srcLMS[1], dstLMS[2] / srcLMS[2]);
        return XYZFromLMS * LMScorrect * LMSFromXYZ;
    }
}

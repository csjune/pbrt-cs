// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/colorspace.h and colorspace.cpp
//
// pbrt's RGBColorSpace::Init() becomes the static constructor. Like pbrt,
// == compares the primaries, whitepoint and RGB to spectrum table.

namespace Pbrt;

// RGBColorSpace Definition
public sealed class RGBColorSpace : IEquatable<RGBColorSpace>
{
    // RGBColorSpace Public Members
    public readonly Point2f r, g, b, w;
    public readonly DenselySampledSpectrum illuminant;
    public readonly SquareMatrix3 XYZFromRGB, RGBFromXYZ;
    public static readonly RGBColorSpace sRGB, DCI_P3, Rec2020, ACES2065_1;

    // RGBColorSpace Private Members
    private readonly RGBToSpectrumTable rgbToSpectrumTable;

    // RGBColorSpace Method Definitions
    public RGBColorSpace(Point2f r, Point2f g, Point2f b, Spectrum illuminant,
                         RGBToSpectrumTable rgbToSpec)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.illuminant = new DenselySampledSpectrum(illuminant);
        rgbToSpectrumTable = rgbToSpec;
        // Compute whitepoint primaries and XYZ coordinates
        XYZ W = SpectrumToXYZ(illuminant);
        w = W.xy();
        XYZ R = XYZ.FromxyY(r), G = XYZ.FromxyY(g), B = XYZ.FromxyY(b);

        // Initialize XYZ color space conversion matrices
        SquareMatrix3 rgb = new(R.X, G.X, B.X, R.Y, G.Y, B.Y, R.Z, G.Z, B.Z);
        XYZ C = InvertOrExit(rgb) * W;
        XYZFromRGB = rgb * SquareMatrix3.Diag(C[0], C[1], C[2]);
        RGBFromXYZ = InvertOrExit(XYZFromRGB);
    }

    public RGBSigmoidPolynomial ToRGBCoeffs(RGB rgb)
    {
        DCHECK(rgb.r >= 0 && rgb.g >= 0 && rgb.b >= 0);
        return rgbToSpectrumTable.Evaluate(ClampZero(rgb));
    }

    public static bool operator ==(RGBColorSpace? a, RGBColorSpace? cs) =>
        a is null ? cs is null : a.Equals(cs);

    public static bool operator !=(RGBColorSpace? a, RGBColorSpace? cs) => !(a == cs);

    public bool Equals(RGBColorSpace? cs) =>
        cs is not null && r == cs.r && g == cs.g && b == cs.b && w == cs.w &&
        rgbToSpectrumTable == cs.rgbToSpectrumTable;

    public override bool Equals(object? obj) => Equals(obj as RGBColorSpace);
    public override int GetHashCode() => HashCode.Combine(r, g, b, w, rgbToSpectrumTable);

    public override string ToString() =>
        $"[ RGBColorSpace r: {r} g: {g} b: {b} w: {w} illuminant: {illuminant} RGBToXYZ: {XYZFromRGB} XYZToRGB: {RGBFromXYZ} ]";

    public RGB LuminanceVector() => new(XYZFromRGB[1, 0], XYZFromRGB[1, 1], XYZFromRGB[1, 2]);

    public RGB ToRGB(XYZ xyz)
    {
        XYZ v = RGBFromXYZ * xyz;
        return new RGB(v.X, v.Y, v.Z);
    }

    public XYZ ToXYZ(RGB rgb)
    {
        RGB v = XYZFromRGB * rgb;
        return new XYZ(v.r, v.g, v.b);
    }

    public static RGBColorSpace? GetNamed(string n)
    {
        string name = n.ToLowerInvariant();
        if (name == "aces2065-1")
            return ACES2065_1;
        else if (name == "rec2020")
            return Rec2020;
        else if (name == "dci-p3")
            return DCI_P3;
        else if (name == "srgb")
            return sRGB;
        else
            return null;
    }

    public static RGBColorSpace? Lookup(Point2f r, Point2f g, Point2f b, Point2f w)
    {
        static bool closeEnough(Point2f a, Point2f b) =>
            ((a.x == b.x || FMath.Abs((a.x - b.x) / b.x) < (Float)1e-3) &&
             (a.y == b.y || FMath.Abs((a.y - b.y) / b.y) < (Float)1e-3));
        foreach (RGBColorSpace cs in (ReadOnlySpan<RGBColorSpace>)[ACES2065_1, DCI_P3, Rec2020, sRGB])
        {
            if (closeEnough(r, cs.r) && closeEnough(g, cs.g) && closeEnough(b, cs.b) &&
                closeEnough(w, cs.w))
                return cs;
        }
        return null;
    }

    // RGBColorSpace::Init()
    static RGBColorSpace()
    {
        // Rec. ITU-R BT.709.3
        sRGB = new RGBColorSpace(
            new Point2f((Float).64, (Float).33), new Point2f((Float).3, (Float).6), new Point2f((Float).15, (Float).06),
            GetNamedSpectrum("stdillum-D65")!, RGBToSpectrumTable.sRGB);
        // P3-D65 (display)
        DCI_P3 = new RGBColorSpace(
            new Point2f((Float).68, (Float).32), new Point2f((Float).265, (Float).690), new Point2f((Float).15, (Float).06),
            GetNamedSpectrum("stdillum-D65")!, RGBToSpectrumTable.DCI_P3);
        // ITU-R Rec BT.2020
        Rec2020 = new RGBColorSpace(
            new Point2f((Float).708, (Float).292), new Point2f((Float).170, (Float).797), new Point2f((Float).131, (Float).046),
            GetNamedSpectrum("stdillum-D65")!, RGBToSpectrumTable.Rec2020);
        ACES2065_1 = new RGBColorSpace(
            new Point2f((Float).7347, (Float).2653), new Point2f(0f, 1f), new Point2f((Float).0001, -(Float).077),
            GetNamedSpectrum("illum-acesD60")!, RGBToSpectrumTable.ACES2065_1);
    }
}

public static partial class Util
{
    public static SquareMatrix3 ConvertRGBColorSpace(RGBColorSpace from, RGBColorSpace to)
    {
        if (from == to)
            return new SquareMatrix3();
        return to.RGBFromXYZ * from.XYZFromRGB;
    }
}

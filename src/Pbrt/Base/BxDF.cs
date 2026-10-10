// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/bxdf.h
//
// pbrt's BxDF TaggedPointer is an abstract class; the BxDFs are in
// Bxdfs.cs. pbrt's `flags & BxDFFlags::X` (an int) is
// `(flags & BxDFFlags.X) != 0` here.

namespace Pbrt;

// BxDFReflTransFlags Definition
[Flags]
public enum BxDFReflTransFlags
{
    Unset = 0,
    Reflection = 1 << 0,
    Transmission = 1 << 1,
    All = Reflection | Transmission
}

// BxDFFlags Definition
[Flags]
public enum BxDFFlags
{
    Unset = 0,
    Reflection = 1 << 0,
    Transmission = 1 << 1,
    Diffuse = 1 << 2,
    Glossy = 1 << 3,
    Specular = 1 << 4,
    // Composite _BxDFFlags_ definitions
    DiffuseReflection = Diffuse | Reflection,
    DiffuseTransmission = Diffuse | Transmission,
    GlossyReflection = Glossy | Reflection,
    GlossyTransmission = Glossy | Transmission,
    SpecularReflection = Specular | Reflection,
    SpecularTransmission = Specular | Transmission,
    All = Diffuse | Glossy | Specular | Reflection | Transmission
}

// TransportMode Definition
public enum TransportMode { Radiance, Importance }

public static partial class Util
{
    // BxDFFlags Inline Functions
    public static bool IsReflective(BxDFFlags f) => (f & BxDFFlags.Reflection) != 0;
    public static bool IsTransmissive(BxDFFlags f) => (f & BxDFFlags.Transmission) != 0;
    public static bool IsDiffuse(BxDFFlags f) => (f & BxDFFlags.Diffuse) != 0;
    public static bool IsGlossy(BxDFFlags f) => (f & BxDFFlags.Glossy) != 0;
    public static bool IsSpecular(BxDFFlags f) => (f & BxDFFlags.Specular) != 0;
    public static bool IsNonSpecular(BxDFFlags f) => (f & (BxDFFlags.Diffuse | BxDFFlags.Glossy)) != 0;

    // pbrt's operator!(TransportMode)
    public static TransportMode Flip(TransportMode mode) =>
        (mode == TransportMode.Radiance) ? TransportMode.Importance : TransportMode.Radiance;

    public static string ToString(BxDFReflTransFlags flags)
    {
        if (flags == BxDFReflTransFlags.Unset)
            return "Unset";
        string s = "";
        if ((flags & BxDFReflTransFlags.Reflection) != 0)
            s += "Reflection,";
        if ((flags & BxDFReflTransFlags.Transmission) != 0)
            s += "Transmission,";
        return s;
    }

    public static string ToString(BxDFFlags flags)
    {
        if (flags == BxDFFlags.Unset)
            return "Unset";
        string s = "";
        if ((flags & BxDFFlags.Reflection) != 0)
            s += "Reflection,";
        if ((flags & BxDFFlags.Transmission) != 0)
            s += "Transmission,";
        if ((flags & BxDFFlags.Diffuse) != 0)
            s += "Diffuse,";
        if ((flags & BxDFFlags.Glossy) != 0)
            s += "Glossy,";
        if ((flags & BxDFFlags.Specular) != 0)
            s += "Specular,";
        return s;
    }

    public static string ToString(TransportMode mode) => mode == TransportMode.Radiance ? "Radiance" : "Importance";
}

// BSDFSample Definition
public struct BSDFSample
{
    public SampledSpectrum f;
    public Vector3f wi;
    public Float pdf = 0;
    public BxDFFlags flags;
    public Float eta = 1;
    public bool pdfIsProportional = false;

    // BSDFSample Public Methods
    public BSDFSample() { }

    public BSDFSample(in SampledSpectrum f, Vector3f wi, Float pdf, BxDFFlags flags, Float eta = 1,
                      bool pdfIsProportional = false)
    {
        this.f = f;
        this.wi = wi;
        this.pdf = pdf;
        this.flags = flags;
        this.eta = eta;
        this.pdfIsProportional = pdfIsProportional;
    }

    public readonly bool IsReflection() => IsReflective(flags);
    public readonly bool IsTransmission() => IsTransmissive(flags);
    public readonly bool IsDiffuse() => Util.IsDiffuse(flags);
    public readonly bool IsGlossy() => Util.IsGlossy(flags);
    public readonly bool IsSpecular() => Util.IsSpecular(flags);

    public override readonly string ToString() =>
        $"[ BSDFSample f: {f} wi: {wi} pdf: {FloatToString(pdf)} flags: {Util.ToString(flags)} " +
        $"eta: {FloatToString(eta)} pdfIsProportional: {(pdfIsProportional ? "true" : "false")} ]";
}

// BxDF Definition
public abstract class BxDF
{
    // BxDF Interface
    public abstract BxDFFlags Flags();

    public abstract SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode);

    public abstract BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All);

    public abstract Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All);

    public abstract void Regularize();

    // BxDF Method Definitions
    public SampledSpectrum rho(Vector3f wo, ReadOnlySpan<Float> uc, ReadOnlySpan<Point2f> u2)
    {
        if (wo.z == 0)
            return default;
        SampledSpectrum r = new(0f);
        DCHECK_EQ(uc.Length, u2.Length);
        for (int i = 0; i < uc.Length; ++i)
        {
            // Compute estimate of $\rho_\roman{hd}$
            BSDFSample? bs = Sample_f(wo, uc[i], u2[i]);
            if (bs is { } b && b.pdf > 0)
                r += b.f * AbsCosTheta(b.wi) / b.pdf;
        }
        return r / uc.Length;
    }

    public SampledSpectrum rho(ReadOnlySpan<Point2f> u1, ReadOnlySpan<Float> uc, ReadOnlySpan<Point2f> u2)
    {
        DCHECK_EQ(uc.Length, u1.Length);
        DCHECK_EQ(u1.Length, u2.Length);
        SampledSpectrum r = new(0f);
        for (int i = 0; i < uc.Length; ++i)
        {
            // Compute estimate of $\rho_\roman{hh}$
            Vector3f wo = SampleUniformHemisphere(u1[i]);
            if (wo.z == 0)
                continue;
            Float pdfo = UniformHemispherePDF();
            BSDFSample? bs = Sample_f(wo, uc[i], u2[i]);
            if (bs is { } b && b.pdf > 0)
                r += b.f * AbsCosTheta(b.wi) * AbsCosTheta(wo) / (pdfo * b.pdf);
        }
        return r / (Pi * uc.Length);
    }
}

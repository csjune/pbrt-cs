// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/bsdf.h and bsdf.cpp
//
// pbrt's templated f<BxDF>(), Sample_f<BxDF>() and PDF<BxDF>() take the
// concrete BxDF type as a generic parameter.

namespace Pbrt;

// BSDF Definition
public struct BSDF
{
    // BSDF Private Members
    private readonly BxDF? bxdf;
    private readonly Frame shadingFrame;

    // BSDF Public Methods
    public BSDF() { }

    public BSDF(Normal3f ns, Vector3f dpdus, BxDF bxdf)
    {
        this.bxdf = bxdf;
        shadingFrame = Frame.FromXZ(Normalize(dpdus), new Vector3f(ns));
    }

    public static bool operator true(in BSDF b) => b.bxdf != null;
    public static bool operator false(in BSDF b) => b.bxdf == null;
    public static bool operator !(in BSDF b) => b.bxdf == null;

    public readonly BxDFFlags Flags() => bxdf!.Flags();

    public readonly Vector3f RenderToLocal(Vector3f v) => shadingFrame.ToLocal(v);
    public readonly Vector3f LocalToRender(Vector3f v) => shadingFrame.FromLocal(v);

    public readonly SampledSpectrum f(Vector3f woRender, Vector3f wiRender,
                                      TransportMode mode = TransportMode.Radiance)
    {
        Vector3f wi = RenderToLocal(wiRender), wo = RenderToLocal(woRender);
        if (wo.z == 0)
            return default;
        return bxdf!.f(wo, wi, mode);
    }

    public readonly SampledSpectrum f<T>(Vector3f woRender, Vector3f wiRender,
                                         TransportMode mode = TransportMode.Radiance) where T : BxDF
    {
        Vector3f wi = RenderToLocal(wiRender), wo = RenderToLocal(woRender);
        if (wo.z == 0)
            return default;
        T specificBxDF = (T)bxdf!;
        return specificBxDF.f(wo, wi, mode);
    }

    public readonly BSDFSample? Sample_f(Vector3f woRender, Float u, Point2f u2,
                                         TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        Vector3f wo = RenderToLocal(woRender);
        if (wo.z == 0 || ((int)bxdf!.Flags() & (int)sampleFlags) == 0)
            return null;
        // Sample _bxdf_ and return _BSDFSample_
        BSDFSample? obs = bxdf.Sample_f(wo, u, u2, mode, sampleFlags);
        if (obs is { } b0)
            DCHECK_GE(b0.pdf, 0);
        if (obs is not { } bs || !bs.f || bs.pdf == 0 || bs.wi.z == 0)
            return null;
        bs.wi = LocalToRender(bs.wi);
        return bs;
    }

    public readonly Float PDF(Vector3f woRender, Vector3f wiRender, TransportMode mode = TransportMode.Radiance,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        Vector3f wo = RenderToLocal(woRender), wi = RenderToLocal(wiRender);
        if (wo.z == 0)
            return 0;
        return bxdf!.PDF(wo, wi, mode, sampleFlags);
    }

    public readonly BSDFSample? Sample_f<T>(Vector3f woRender, Float u, Point2f u2,
                                            TransportMode mode = TransportMode.Radiance,
                                            BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All) where T : BxDF
    {
        Vector3f wo = RenderToLocal(woRender);
        if (wo.z == 0)
            return null;

        T specificBxDF = (T)bxdf!;
        if (((int)specificBxDF.Flags() & (int)sampleFlags) == 0)
            return null;

        BSDFSample? obs = specificBxDF.Sample_f(wo, u, u2, mode, sampleFlags);
        if (obs is not { } bs || !bs.f || bs.pdf == 0 || bs.wi.z == 0)
            return null;
        DCHECK_GT(bs.pdf, 0);

        bs.wi = LocalToRender(bs.wi);

        return bs;
    }

    public readonly Float PDF<T>(Vector3f woRender, Vector3f wiRender, TransportMode mode = TransportMode.Radiance,
                                 BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All) where T : BxDF
    {
        Vector3f wo = RenderToLocal(woRender), wi = RenderToLocal(wiRender);
        if (wo.z == 0)
            return 0;
        T specificBxDF = (T)bxdf!;
        return specificBxDF.PDF(wo, wi, mode, sampleFlags);
    }

    // BSDF Method Definitions
    public override readonly string ToString() =>
        $"[ BSDF bxdf: {bxdf?.ToString() ?? "(nullptr)"} shadingFrame: {shadingFrame} ]";

    public readonly SampledSpectrum rho(ReadOnlySpan<Point2f> u1, ReadOnlySpan<Float> uc, ReadOnlySpan<Point2f> u2) =>
        bxdf!.rho(u1, uc, u2);

    public readonly SampledSpectrum rho(Vector3f woRender, ReadOnlySpan<Float> uc, ReadOnlySpan<Point2f> u)
    {
        Vector3f wo = RenderToLocal(woRender);
        return bxdf!.rho(wo, uc, u);
    }

    public readonly void Regularize() => bxdf!.Regularize();

    // (for code that needs the BxDF itself)
    public readonly BxDF? GetBxDF() => bxdf;
}

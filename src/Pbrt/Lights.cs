// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/lights.h and lights.cpp
//
// PhysLight code contributed by Anders Langlands and Luca Fascione
// Copyright (c) 2020, Weta Digital, Ltd.
// SPDX-License-Identifier: Apache-2.0
//
// The lights derive from LightBase, which derives from Light. pbrt's
// pstd::optional results are nullable structs and the pointer outputs are
// out parameters. Where pbrt points an Interaction at the light's
// MediumInterface, the Interaction gets a copy of it.

namespace Pbrt;

// LightLiSample Definition
public struct LightLiSample
{
    // LightLiSample Public Methods
    public LightLiSample(in SampledSpectrum L, Vector3f wi, Float pdf, in Interaction pLight)
    {
        this.L = L;
        this.wi = wi;
        this.pdf = pdf;
        this.pLight = pLight;
    }

    public override readonly string ToString() =>
        $"[ LightLiSample L: {L} wi: {wi} pdf: {FloatToString(pdf)} pLight: {pLight} ]";

    public SampledSpectrum L;
    public Vector3f wi;
    public Float pdf;
    public Interaction pLight;
}

// LightLeSample Definition
public struct LightLeSample
{
    // LightLeSample Public Methods
    public LightLeSample(in SampledSpectrum L, in Ray ray, Float pdfPos, Float pdfDir)
    {
        this.L = L;
        this.ray = ray;
        this.pdfPos = pdfPos;
        this.pdfDir = pdfDir;
    }

    public LightLeSample(in SampledSpectrum L, in Ray ray, in Interaction intr, Float pdfPos, Float pdfDir)
    {
        this.L = L;
        this.ray = ray;
        this.intr = intr;
        this.pdfPos = pdfPos;
        this.pdfDir = pdfDir;
        CHECK(this.intr.Value.n != new Normal3f(0, 0, 0));
    }

    public override readonly string ToString() =>
        $"[ LightLeSample L: {L} ray: {ray} intr: {(intr?.ToString() ?? "(unset)")} " +
        $"pdfPos: {FloatToString(pdfPos)} pdfDir: {FloatToString(pdfDir)} ]";

    public readonly Float AbsCosTheta(Vector3f w) => intr is Interaction i ? AbsDot(w, i.n) : 1;

    // LightLeSample Public Members
    public SampledSpectrum L;
    public Ray ray;
    public Interaction? intr;
    public Float pdfPos = 0, pdfDir = 0;
}

// LightSampleContext Definition
public struct LightSampleContext
{
    // LightSampleContext Public Methods
    public LightSampleContext() { }

    public LightSampleContext(in SurfaceInteraction si)
    {
        pi = si.pi;
        n = si.n;
        ns = si.shading.n;
    }

    public LightSampleContext(in Interaction intr)
    {
        pi = intr.pi;
    }

    public LightSampleContext(Point3fi pi, Normal3f n, Normal3f ns)
    {
        this.pi = pi;
        this.n = n;
        this.ns = ns;
    }

    public readonly Point3f p() => new(pi);

    // (pbrt's constructors from interactions are implicit.)
    public static implicit operator LightSampleContext(in SurfaceInteraction si) => new(si);
    public static implicit operator LightSampleContext(in Interaction intr) => new(intr);

    // LightSampleContext Public Members
    public Point3fi pi;
    public Normal3f n, ns;
}

// LightBounds Definition
public struct LightBounds
{
    // LightBounds Public Methods
    // (pbrt leaves the angles and twoSided uninitialized here; the bounds
    // are empty, as with pbrt's Bounds3f default constructor.)
    public LightBounds() { }

    public LightBounds(in Bounds3f b, Vector3f w, Float phi, Float cosTheta_o, Float cosTheta_e, bool twoSided)
    {
        bounds = b;
        this.w = Normalize(w);
        this.phi = phi;
        this.cosTheta_o = cosTheta_o;
        this.cosTheta_e = cosTheta_e;
        this.twoSided = twoSided;
    }

    public readonly Point3f Centroid() => (bounds.pMin + bounds.pMax) / 2;

    // LightBounds Method Definitions
    public readonly Float Importance(Point3f p, Normal3f n)
    {
        // Return importance for light bounds at reference point
        // Compute clamped squared distance to reference point
        Point3f pc = (bounds.pMin + bounds.pMax) / 2;
        Float d2 = DistanceSquared(p, pc);
        d2 = FMath.Max(d2, Length(bounds.Diagonal()) / 2);

        // Compute sine and cosine of angle to vector _w_, $\theta_\roman{w}$
        Vector3f wi = Normalize(p - pc);
        Float cosTheta_w = Dot(w, wi);
        if (twoSided)
            cosTheta_w = FMath.Abs(cosTheta_w);
        Float sinTheta_w = SafeSqrt(1 - Sqr(cosTheta_w));

        // Compute $\cos\,\theta_\roman{\+b}$ for reference point
        Float cosTheta_b = BoundSubtendedDirections(bounds, p).cosTheta;
        Float sinTheta_b = SafeSqrt(1 - Sqr(cosTheta_b));

        // Compute $\cos\,\theta'$ and test against $\cos\,\theta_\roman{e}$
        Float sinTheta_o = SafeSqrt(1 - Sqr(cosTheta_o));
        Float cosTheta_x = CosSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
        Float sinTheta_x = SinSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
        Float cosThetap = CosSubClamped(sinTheta_x, cosTheta_x, sinTheta_b, cosTheta_b);
        if (cosThetap <= cosTheta_e)
            return 0;

        // Return final importance at reference point
        Float importance = phi * cosThetap / d2;
        DCHECK_GE(importance, -(Float)1e-3);
        // Account for $\cos\theta_\roman{i}$ in importance at surfaces
        if (n != new Normal3f(0, 0, 0))
        {
            Float cosTheta_i = AbsDot(wi, n);
            Float sinTheta_i = SafeSqrt(1 - Sqr(cosTheta_i));
            Float cosThetap_i = CosSubClamped(sinTheta_i, cosTheta_i, sinTheta_b, cosTheta_b);
            importance *= cosThetap_i;
        }

        importance = FMath.Max(importance, 0);
        return importance;
    }

    // Define cosine and sine clamped subtraction lambdas
    internal static Float CosSubClamped(Float sinTheta_a, Float cosTheta_a, Float sinTheta_b, Float cosTheta_b)
    {
        if (cosTheta_a > cosTheta_b)
            return 1;
        return cosTheta_a * cosTheta_b + sinTheta_a * sinTheta_b;
    }

    internal static Float SinSubClamped(Float sinTheta_a, Float cosTheta_a, Float sinTheta_b, Float cosTheta_b)
    {
        if (cosTheta_a > cosTheta_b)
            return 0;
        return sinTheta_a * cosTheta_b - cosTheta_a * sinTheta_b;
    }

    public override readonly string ToString() =>
        $"[ LightBounds bounds: {bounds} w: {w} phi: {FloatToString(phi)} cosTheta_o: {FloatToString(cosTheta_o)} " +
        $"cosTheta_e: {FloatToString(cosTheta_e)} twoSided: {(twoSided ? "true" : "false")} ]";

    // LightBounds Public Members
    public Bounds3f bounds = new();
    public Float phi = 0;
    public Vector3f w;
    public Float cosTheta_o, cosTheta_e;
    public bool twoSided;
}

public static partial class Util
{
    // Light Inline Functions
    public static bool IsDeltaLight(LightType type) =>
        type == LightType.DeltaPosition || type == LightType.DeltaDirection;

    public static string ToString(LightType lf) => lf switch
    {
        LightType.DeltaPosition => "DeltaPosition",
        LightType.DeltaDirection => "DeltaDirection,",
        LightType.Area => "Area",
        LightType.Infinite => "Infinite",
        _ => throw new FatalErrorException("Unhandled type"),
    };

    public static LightBounds Union(in LightBounds a, in LightBounds b)
    {
        // If one _LightBounds_ has zero power, return the other
        if (a.phi == 0)
            return b;
        if (b.phi == 0)
            return a;

        // Find average direction and updated angles for _LightBounds_
        DirectionCone cone = Union(new DirectionCone(a.w, a.cosTheta_o), new DirectionCone(b.w, b.cosTheta_o));
        Float cosTheta_o = cone.cosTheta;
        Float cosTheta_e = FMath.Min(a.cosTheta_e, b.cosTheta_e);

        // Return final _LightBounds_ union
        return new LightBounds(Union(a.bounds, b.bounds), cone.w, a.phi + b.phi, cosTheta_o, cosTheta_e,
                               a.twoSided | b.twoSided);
    }
}

// LightBase Definition
public abstract class LightBase : Light
{
    private static readonly StatCounter numLights = new("Scene/Lights");
    internal static readonly StatMemoryCounter imageBytes = new("Memory/Light image and distributions");

    // LightBase Protected Members
    protected readonly LightType type;
    protected readonly Transform renderFromLight;
    protected readonly MediumInterface mediumInterface;
    private static readonly InternCache<DenselySampledSpectrum> spectrumCache = new();

    // LightBase Public Methods
    protected LightBase(LightType type, Transform renderFromLight, in MediumInterface mediumInterface)
    {
        this.type = type;
        this.mediumInterface = mediumInterface;
        this.renderFromLight = renderFromLight;
        numLights.Inc();
    }

    public override LightType Type() => type;

    protected override SampledSpectrum EmittedL(Point3f p, Normal3f n, Point2f uv, Vector3f w,
                                                in SampledWavelengths lambda) =>
        new(0f);

    public override SampledSpectrum Le(in Ray ray, in SampledWavelengths lambda) => new(0f);

    public override void PDF_Le(in Interaction intr, Vector3f w, out Float pdfPos, out Float pdfDir)
    {
        LOG_FATAL("Shouldn't be called for non-area lights");
        pdfPos = pdfDir = 0;
    }

    // LightBase Protected Methods
    // Returns the unique DenselySampledSpectrum for _s_ from the intern
    // cache.
    protected static DenselySampledSpectrum LookupSpectrum(Spectrum s) =>
        spectrumCache.Lookup(new DenselySampledSpectrum(s), d => d);

    protected string BaseToString() =>
        $"type: {Util.ToString(type)} mediumInterface: {mediumInterface} renderFromLight: {renderFromLight}";
}

// PointLight Definition
public sealed class PointLight : LightBase
{
    // PointLight Private Members
    private readonly DenselySampledSpectrum I;
    private readonly Float scale;

    // PointLight Public Methods
    public PointLight(Transform renderFromLight, in MediumInterface mediumInterface, Spectrum I, Float scale)
        : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
    {
        this.I = LookupSpectrum(I);
        this.scale = scale;
    }

    public static PointLight Create(Transform renderFromLight, Medium? medium, ParameterDictionary parameters,
                                    RGBColorSpace colorSpace, FileLoc? loc)
    {
        Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant)!;
        Float sc = parameters.GetOneFloat("scale", 1);

        sc /= SpectrumToPhotometric(I);

        Float phi_v = parameters.GetOneFloat("power", -1);
        if (phi_v > 0)
        {
            Float k_e = 4 * Pi;
            sc *= phi_v / k_e;
        }

        Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
        Transform tf = Translate(new Vector3f(from.x, from.y, from.z));
        Transform finalRenderFromLight = renderFromLight * tf;

        return new PointLight(finalRenderFromLight, new MediumInterface(medium), I, sc);
    }

    // PointLight Method Definitions
    public override SampledSpectrum Phi(in SampledWavelengths lambda) => 4 * Pi * scale * I.Sample(lambda);

    public override void Preprocess(in Bounds3f sceneBounds) { }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Ray ray = new(p, SampleUniformSphere(u1), time, mediumInterface.outside);
        return new LightLeSample(scale * I.Sample(lambda), ray, 1, UniformSpherePDF());
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        pdfPos = 0;
        pdfDir = UniformSpherePDF();
    }

    public override LightBounds? Bounds()
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Float phi = 4 * Pi * scale * I.MaxValue();
        return new LightBounds(new Bounds3f(p, p), new Vector3f(0, 0, 1), phi, FMath.Cos(Pi), FMath.Cos(Pi / 2),
                               false);
    }

    public override string ToString() => $"[ PointLight {BaseToString()} I: {I} scale: {FloatToString(scale)} ]";

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f wi = Normalize(p - ctx.p());
        SampledSpectrum Li = scale * I.Sample(lambda) / DistanceSquared(p, ctx.p());
        return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) => 0;
}

// DistantLight Definition
public sealed class DistantLight : LightBase
{
    // DistantLight Private Members
    private readonly DenselySampledSpectrum Lemit;
    private readonly Float scale;
    private Point3f sceneCenter;
    private Float sceneRadius;

    // DistantLight Public Methods
    public DistantLight(Transform renderFromLight, Spectrum Lemit, Float scale)
        : base(LightType.DeltaDirection, renderFromLight, new MediumInterface())
    {
        this.Lemit = LookupSpectrum(Lemit);
        this.scale = scale;
    }

    public static DistantLight Create(Transform renderFromLight, ParameterDictionary parameters,
                                      RGBColorSpace colorSpace, FileLoc? loc)
    {
        Spectrum L = parameters.GetOneSpectrum("L", colorSpace.illuminant, SpectrumType.Illuminant)!;
        Float sc = parameters.GetOneFloat("scale", 1);

        Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
        Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));

        Vector3f w = Normalize(from - to);
        CoordinateSystem(w, out Vector3f v1, out Vector3f v2);
        Float[,] m = { { v1.x, v2.x, w.x, 0 }, { v1.y, v2.y, w.y, 0 }, { v1.z, v2.z, w.z, 0 }, { 0, 0, 0, 1 } };
        Transform t = new(m);
        Transform finalRenderFromLight = renderFromLight * t;

        // Scale the light spectrum to be equivalent to 1 nit
        sc /= SpectrumToPhotometric(L);

        // Adjust scale to meet target illuminance value
        // Like for IBLs we measure illuminance as incident on an upward-facing
        // patch.
        Float E_v = parameters.GetOneFloat("illuminance", -1);
        if (E_v > 0)
            sc *= E_v;

        return new DistantLight(finalRenderFromLight, L, sc);
    }

    // DistantLight Method Definitions
    public override SampledSpectrum Phi(in SampledWavelengths lambda) =>
        scale * Lemit.Sample(lambda) * Pi * Sqr(sceneRadius);

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) => 0;

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Choose point on disk oriented toward infinite light direction
        Vector3f w = Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
        Frame wFrame = Frame.FromZ(w);
        Point2f cd = SampleUniformDiskConcentric(u1);
        Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));

        // Compute _DistantLight_ light ray
        Ray ray = new(pDisk + sceneRadius * w, -w, time);

        return new LightLeSample(scale * Lemit.Sample(lambda), ray, 1 / (Pi * Sqr(sceneRadius)), 1);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        pdfPos = 1 / (Pi * sceneRadius * sceneRadius);
        pdfDir = 0;
    }

    public override LightBounds? Bounds() => null;

    public override string ToString() =>
        $"[ DistantLight {BaseToString()} Lemit: {Lemit} scale: {FloatToString(scale)} ]";

    public override void Preprocess(in Bounds3f sceneBounds) =>
        sceneBounds.BoundingSphere(out sceneCenter, out sceneRadius);

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        Vector3f wi = Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
        Point3f pOutside = ctx.p() + wi * (2 * sceneRadius);
        return new LightLiSample(scale * Lemit.Sample(lambda), wi, 1, new Interaction(pOutside, (MediumInterface?)null));
    }
}

// ProjectionLight Definition
public sealed class ProjectionLight : LightBase
{
    // ProjectionLight Private Members
    private readonly Image image;
    private readonly RGBColorSpace imageColorSpace;
    private readonly Float scale;
    private readonly Bounds2f screenBounds;
    private readonly Float hither = 1e-3f;
    private readonly Transform screenFromLight, lightFromScreen;
    private readonly Float A;
    private readonly PiecewiseConstant2D distrib;

    // ProjectionLight Method Definitions
    public ProjectionLight(Transform renderFromLight, in MediumInterface mediumInterface, Image im,
                           RGBColorSpace imageColorSpace, Float scale, Float fov)
        : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
    {
        image = im;
        this.imageColorSpace = imageColorSpace;
        this.scale = scale;
        // _ProjectionLight_ constructor implementation
        // Initialize _ProjectionLight_ projection matrix
        Float aspect = (Float)image.Resolution().x / (Float)image.Resolution().y;
        if (aspect > 1)
            screenBounds = new Bounds2f(new Point2f(-aspect, -1), new Point2f(aspect, 1));
        else
            screenBounds = new Bounds2f(new Point2f(-1, -1 / aspect), new Point2f(1, 1 / aspect));
        screenFromLight = Perspective(fov, hither, 1e30f /* yon */);
        lightFromScreen = Inverse(screenFromLight);

        // Compute projection image area _A_
        Float opposite = FMath.Tan(Radians(fov) / 2);
        A = 4 * Sqr(opposite) * (aspect > 1 ? aspect : (1 / aspect));

        // Compute sampling distribution for _ProjectionLight_
        ImageChannelDesc channelDesc = image.GetChannelDesc(["R", "G", "B"]);
        if (!(bool)channelDesc)
            ErrorExit("Image used for ProjectionLight does not have R, G, B channels.");
        CHECK_EQ(3, channelDesc.Size);
        CHECK(channelDesc.IsIdentity());
        Transform lfs = lightFromScreen;
        Float dwdA(Point2f p)
        {
            Vector3f w = new(lfs.Apply(new Point3f(p.x, p.y, 0)));
            return Pow(CosTheta(Normalize(w)), 3);
        }
        Array2D<Float> d = image.GetSamplingDistribution(dwdA, screenBounds);
        distrib = new PiecewiseConstant2D(d, screenBounds);

        imageBytes.Add(image.BytesUsed() + distrib.BytesUsed());
    }

    public override void Preprocess(in Bounds3f sceneBounds) { }

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        // Return sample for incident radiance from _ProjectionLight_
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f wi = Normalize(p - ctx.p());
        Vector3f wl = renderFromLight.ApplyInverse(-wi);
        SampledSpectrum Li = I(wl, lambda) / DistanceSquared(p, ctx.p());
        if (!Li)
            return null;
        return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) => 0f;

    public override string ToString() =>
        $"[ ProjectionLight {BaseToString()} scale: {FloatToString(scale)} A: {FloatToString(A)} ]";

    public SampledSpectrum I(Vector3f w, in SampledWavelengths lambda)
    {
        // Discard directions behind projection light
        if (w.z < hither)
            return new SampledSpectrum(0f);

        // Project point onto projection plane and compute RGB
        Point3f ps = screenFromLight.Apply(new Point3f(w));
        if (!Inside(new Point2f(ps.x, ps.y), screenBounds))
            return new SampledSpectrum(0f);
        Point2f uv = new(screenBounds.Offset(new Point2f(ps.x, ps.y)));
        RGB rgb = default;
        for (int c = 0; c < 3; ++c)
            rgb[c] = image.LookupNearestChannel(uv, c);

        // Return scaled wavelength samples corresponding to RGB
        RGBIlluminantSpectrum s = new(imageColorSpace, ClampZero(rgb));
        return scale * s.Sample(lambda);
    }

    public override SampledSpectrum Phi(in SampledWavelengths lambda)
    {
        SampledSpectrum sum = new(0f);
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                // Compute change of variables factor _dwdA_ for projection light pixel
                Point2f ps = screenBounds.Lerp(new Point2f((x + 0.5f) / image.Resolution().x,
                                                           (y + 0.5f) / image.Resolution().y));
                Vector3f w = new(lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0)));
                w = Normalize(w);
                Float dwdA = Pow(CosTheta(w), 3);

                // Update _sum_ for projection light pixel
                RGB rgb = default;
                for (int c = 0; c < 3; ++c)
                    rgb[c] = image.GetChannel(new Point2i(x, y), c);
                RGBIlluminantSpectrum s = new(imageColorSpace, ClampZero(rgb));
                sum += s.Sample(lambda) * dwdA;
            }
        // Return final power for projection light
        return scale * A * sum / (image.Resolution().x * image.Resolution().y);
    }

    public override LightBounds? Bounds()
    {
        Float sum = 0;
        for (int v = 0; v < image.Resolution().y; ++v)
            for (int u = 0; u < image.Resolution().x; ++u)
                sum += FMath.Max(image.GetChannel(new Point2i(u, v), 0),
                                 FMath.Max(image.GetChannel(new Point2i(u, v), 1),
                                           image.GetChannel(new Point2i(u, v), 2)));
        Float phi = scale * sum / (image.Resolution().x * image.Resolution().y);

        Point3f pCorner = new(screenBounds.pMax.x, screenBounds.pMax.y, 0);
        Vector3f wCorner = Normalize(new Vector3f(lightFromScreen.Apply(pCorner)));
        Float cosTotalWidth = CosTheta(wCorner);

        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f w = Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
        return new LightBounds(new Bounds3f(p, p), w, phi, MathF.Cos(0f), cosTotalWidth, false);
    }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Sample light space ray direction for projection light
        Point2f ps = distrib.Sample(u1, out Float pdf, out _);
        if (pdf == 0)
            return null;
        Vector3f w = new(lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0)));

        // Compute PDF for sampled projection light direction
        Float cosTheta = CosTheta(Normalize(w));
        CHECK_GT(cosTheta, 0);
        Float pdfDir = pdf * screenBounds.Area() / (A * Pow(cosTheta, 3));

        // Compute radiance and return projection light sample
        Point2f p = new(screenBounds.Offset(ps));
        RGB rgb = default;
        for (int c = 0; c < 3; ++c)
            rgb[c] = image.LookupNearestChannel(p, c);
        SampledSpectrum L = scale * new RGBIlluminantSpectrum(imageColorSpace, rgb).Sample(lambda);
        Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), Normalize(w), time, mediumInterface.outside));
        return new LightLeSample(L, ray, 1, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        pdfPos = 0;
        // Transform ray direction to light space and reject invalid ones
        Vector3f w = Normalize(renderFromLight.ApplyInverse(ray.d));
        if (w.z < hither)
        {
            pdfDir = 0;
            return;
        }

        // Compute screen space coordinates for direction and test against bounds
        Point3f ps = screenFromLight.Apply(new Point3f(w));
        if (!Inside(new Point2f(ps.x, ps.y), screenBounds))
        {
            pdfDir = 0;
            return;
        }

        pdfDir = distrib.PDF(new Point2f(ps.x, ps.y)) * screenBounds.Area() / (A * Pow(CosTheta(w), 3));
    }

    public static ProjectionLight Create(Transform renderFromLight, Medium? medium, ParameterDictionary parameters,
                                         FileLoc? loc)
    {
        Float scale = parameters.GetOneFloat("scale", 1);
        Float power = parameters.GetOneFloat("power", -1);
        Float fov = parameters.GetOneFloat("fov", 90f);

        string texname = ResolveFilename(parameters.GetOneString("filename", ""));
        if (texname.Length == 0)
            ErrorExit(loc, "Must provide \"filename\" to \"projection\" light source");

        ImageAndMetadata imageAndMetadata = Image.Read(texname);
        if (imageAndMetadata.image.HasAnyInfinitePixels())
            ErrorExit(loc, $"{texname}: image has infinite pixel values and so is not suitable as a light.");
        if (imageAndMetadata.image.HasAnyNaNPixels())
            ErrorExit(loc, $"{texname}: image has not-a-number pixel values and so is not suitable as a light.");

        RGBColorSpace colorSpace = imageAndMetadata.metadata.GetColorSpace();

        ImageChannelDesc channelDesc = imageAndMetadata.image.GetChannelDesc(["R", "G", "B"]);
        if (!(bool)channelDesc)
            ErrorExit(loc, "Image provided to \"projection\" light must have R, G, and B channels.");
        Image image = imageAndMetadata.image.SelectChannels(channelDesc);

        scale /= SpectrumToPhotometric(colorSpace.illuminant);
        if (power > 0)
        {
            Bounds2f screenBounds;
            Float A;
            Transform lightFromScreen, screenFromLight;
            Float hither = 1e-3f;
            // Initialize _ProjectionLight_ projection matrix
            Float aspect = (Float)image.Resolution().x / (Float)image.Resolution().y;
            if (aspect > 1)
                screenBounds = new Bounds2f(new Point2f(-aspect, -1), new Point2f(aspect, 1));
            else
                screenBounds = new Bounds2f(new Point2f(-1, -1 / aspect), new Point2f(1, 1 / aspect));
            screenFromLight = Perspective(fov, hither, 1e30f /* yon */);
            lightFromScreen = Inverse(screenFromLight);

            // Compute projection image area _A_
            Float opposite = FMath.Tan(Radians(fov) / 2);
            A = 4 * Sqr(opposite) * (aspect > 1 ? aspect : (1 / aspect));

            Float sum = 0;
            RGB luminance = colorSpace.LuminanceVector();
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Point2f ps = screenBounds.Lerp(new Point2f((x + .5f) / image.Resolution().x,
                                                               (y + .5f) / image.Resolution().y));
                    Vector3f w = new(lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0)));
                    w = Normalize(w);
                    Float dwdA = Pow(w.z, 3);

                    for (int c = 0; c < 3; ++c)
                        sum += image.GetChannel(new Point2i(x, y), c) * luminance[c] * dwdA;
                }
            scale *= power / (A * sum / (image.Resolution().x * image.Resolution().y));
        }

        Transform flip = Scale(1, -1, 1);
        Transform renderFromLightFlipY = renderFromLight * flip;

        return new ProjectionLight(renderFromLightFlipY, new MediumInterface(medium), image, colorSpace, scale, fov);
    }
}

// GoniometricLight Definition
public sealed class GoniometricLight : LightBase
{
    // GoniometricLight Private Members
    private readonly DenselySampledSpectrum Iemit;
    private readonly Float scale;
    private readonly Image image;
    private readonly PiecewiseConstant2D distrib;

    // GoniometricLight Method Definitions
    public GoniometricLight(Transform renderFromLight, in MediumInterface mediumInterface, Spectrum Iemit,
                            Float scale, Image im)
        : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
    {
        this.Iemit = LookupSpectrum(Iemit);
        this.scale = scale;
        image = im;
        CHECK_EQ(1, image.NChannels());
        CHECK_EQ(image.Resolution().x, image.Resolution().y);
        // Compute sampling distribution for _GoniometricLight_
        Array2D<Float> d = image.GetSamplingDistribution();
        distrib = new PiecewiseConstant2D(d);

        imageBytes.Add(image.BytesUsed() + distrib.BytesUsed());
    }

    public override void Preprocess(in Bounds3f sceneBounds) { }

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f wi = Normalize(p - ctx.p());
        SampledSpectrum L = I(renderFromLight.ApplyInverse(-wi), lambda) / DistanceSquared(p, ctx.p());
        return new LightLiSample(L, wi, 1, new Interaction(p, mediumInterface));
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) => 0f;

    private Float SumY()
    {
        Float sumY = 0;
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                sumY += image.GetChannel(new Point2i(x, y), 0);
        return sumY;
    }

    public override SampledSpectrum Phi(in SampledWavelengths lambda)
    {
        Float sumY = SumY();
        return scale * Iemit.Sample(lambda) * 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);
    }

    public override LightBounds? Bounds()
    {
        Float sumY = SumY();
        Float phi = scale * Iemit.MaxValue() * 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);

        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        // Bound it as an isotropic point light.
        return new LightBounds(new Bounds3f(p, p), new Vector3f(0, 0, 1), phi, FMath.Cos(Pi), FMath.Cos(Pi / 2),
                               false);
    }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Sample direction and PDF for ray leaving goniometric light
        Point2f uv = distrib.Sample(u1, out Float pdf, out _);
        Vector3f wLight = EqualAreaSquareToSphere(uv);
        Float pdfDir = pdf / (4 * Pi);

        Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), wLight, time, mediumInterface.outside));
        return new LightLeSample(I(wLight, lambda), ray, 1, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        pdfPos = 0f;
        Vector3f wLight = Normalize(renderFromLight.ApplyInverse(ray.d));
        Point2f uv = EqualAreaSphereToSquare(wLight);
        pdfDir = distrib.PDF(uv) / (4 * Pi);
    }

    public override string ToString() =>
        $"[ GoniometricLight {BaseToString()} Iemit: {Iemit} scale: {FloatToString(scale)} ]";

    public SampledSpectrum I(Vector3f w, in SampledWavelengths lambda)
    {
        Point2f uv = EqualAreaSphereToSquare(w);
        return scale * Iemit.Sample(lambda) * image.LookupNearestChannel(uv, 0);
    }

    public static GoniometricLight Create(Transform renderFromLight, Medium? medium, ParameterDictionary parameters,
                                          RGBColorSpace colorSpace, FileLoc? loc)
    {
        Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant)!;
        Float sc = parameters.GetOneFloat("scale", 1);

        Image image = new();

        string texname = ResolveFilename(parameters.GetOneString("filename", ""));
        if (texname.Length == 0)
            Warning(loc, "No \"filename\" parameter provided for goniometric light.");
        else
        {
            ImageAndMetadata imageAndMetadata = Image.Read(texname);

            if (imageAndMetadata.image.HasAnyInfinitePixels())
                ErrorExit(loc, $"{texname}: image has infinite pixel values and so is not suitable as a light.");
            if (imageAndMetadata.image.HasAnyNaNPixels())
                ErrorExit(loc, $"{texname}: image has not-a-number pixel values and so is not suitable as a light.");

            if (imageAndMetadata.image.Resolution().x != imageAndMetadata.image.Resolution().y)
                ErrorExit($"{texname}: image resolution ({imageAndMetadata.image.Resolution().x}, " +
                          $"{imageAndMetadata.image.Resolution().y}) is non-square. It's unlikely this is an " +
                          "equal-area environment map.");

            ImageChannelDesc rgbDesc = imageAndMetadata.image.GetChannelDesc(["R", "G", "B"]);
            ImageChannelDesc yDesc = imageAndMetadata.image.GetChannelDesc(["Y"]);

            if ((bool)rgbDesc)
            {
                if ((bool)yDesc)
                    ErrorExit($"{texname}: has both \"R\", \"G\", and \"B\" or \"Y\" channels.");
                image = new Image(imageAndMetadata.image.Format(), imageAndMetadata.image.Resolution(), ["Y"],
                                  imageAndMetadata.image.Encoding());
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                        image.SetChannel(new Point2i(x, y), 0,
                                         imageAndMetadata.image.GetChannels(new Point2i(x, y), rgbDesc).Average());
            }
            else if ((bool)yDesc)
                image = imageAndMetadata.image;
            else
                ErrorExit(loc, $"{texname}: has neither \"R\", \"G\", and \"B\" or \"Y\" channels.");
        }

        sc /= SpectrumToPhotometric(I);

        Float phi_v = parameters.GetOneFloat("power", -1);
        if (phi_v > 0)
        {
            Float sumY = 0;
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    sumY += image.GetChannel(new Point2i(x, y), 0);
            Float k_e = 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);
            sc *= phi_v / k_e;
        }

        Float[,] swapYZ = { { 1, 0, 0, 0 }, { 0, 0, 1, 0 }, { 0, 1, 0, 0 }, { 0, 0, 0, 1 } };
        Transform t = new(swapYZ);
        Transform finalRenderFromLight = renderFromLight * t;

        return new GoniometricLight(finalRenderFromLight, new MediumInterface(medium), I, sc, image);
    }
}

// DiffuseAreaLight Definition
public sealed class DiffuseAreaLight : LightBase
{
    private static readonly StatCounter numAreaLights = new("Scene/AreaLights");

    // DiffuseAreaLight Private Members
    private readonly Shape shape;
    private readonly FloatTexture? alpha;
    private readonly Float area;
    private readonly bool twoSided;
    private readonly DenselySampledSpectrum? Lemit;
    private readonly Float scale;
    private readonly Image image;
    private readonly RGBColorSpace? imageColorSpace;

    // Special case handling for area lights with constant zero-valued alpha
    // textures to allow invisible area lights: we will null out the alpha
    // texture below so that as far as the DiffuseAreaLight is concerned, there
    // is no alpha texture and the light is fully emissive. However, such lights
    // will never be intersected by rays (because their associated primitives
    // still have the alpha texture), so we mark them as DeltaPosition lights
    // here so that MIS isn't used for direct illumination. Thus, light sampling
    // is the only strategy used and we get an unbiased (if potentially high
    // variance) estimate.
    private static LightType AlphaLightType(FloatTexture? alpha) =>
        alpha is FloatConstantTexture fc && fc.Evaluate(new TextureEvalContext()) == 0
            ? LightType.DeltaPosition
            : LightType.Area;

    // DiffuseAreaLight Method Definitions
    public DiffuseAreaLight(Transform renderFromLight, in MediumInterface mediumInterface, Spectrum? Le,
                            Float scale, Shape shape, FloatTexture? alpha, Image im,
                            RGBColorSpace? imageColorSpace, bool twoSided)
        : base(AlphaLightType(alpha), renderFromLight, mediumInterface)
    {
        this.shape = shape;
        this.alpha = type == LightType.Area ? alpha : null;
        area = shape.Area();
        this.twoSided = twoSided;
        Lemit = Le != null ? LookupSpectrum(Le) : null;
        this.scale = scale;
        image = im;
        this.imageColorSpace = imageColorSpace;
        numAreaLights.Inc();

        if ((bool)image)
        {
            ImageChannelDesc desc = image.GetChannelDesc(["R", "G", "B"]);
            if (!(bool)desc)
                ErrorExit("Image used for DiffuseAreaLight doesn't have R, G, B channels.");
            CHECK_EQ(3, desc.Size);
            CHECK(desc.IsIdentity());
            CHECK(imageColorSpace != null);
        }
        else
            CHECK(Le != null);

        // Warn if light has transformation with non-uniform scale, though not
        // for Triangles or bilinear patches, since this doesn't matter for them.
        if (renderFromLight.HasScale() && shape is not Triangle && shape is not BilinearPatch)
            Warning("Scaling detected in rendering to light space transformation! " +
                    "The system has numerous assumptions, implicit and explicit, " +
                    "that this transform will have no scale factors in it. " +
                    "Proceed at your own risk; your image may have errors.");
    }

    public static DiffuseAreaLight Create(Transform renderFromLight, Medium? medium, ParameterDictionary parameters,
                                          RGBColorSpace colorSpace, FileLoc? loc, Shape shape, FloatTexture? alphaTex)
    {
        Spectrum? L = parameters.GetOneSpectrum("L", null, SpectrumType.Illuminant);
        Float scale = parameters.GetOneFloat("scale", 1);
        bool twoSided = parameters.GetOneBool("twosided", false);

        string filename = ResolveFilename(parameters.GetOneString("filename", ""));
        Image image = new();
        RGBColorSpace? imageColorSpace = null;
        if (filename.Length > 0)
        {
            if (L != null)
                ErrorExit(loc, "Both \"L\" and \"filename\" specified for DiffuseAreaLight.");
            ImageAndMetadata im = Image.Read(filename);

            if (im.image.HasAnyInfinitePixels())
                ErrorExit(loc, $"{filename}: image has infinite pixel values and so is not suitable as a light.");
            if (im.image.HasAnyNaNPixels())
                ErrorExit(loc, $"{filename}: image has not-a-number pixel values and so is not suitable as a light.");

            ImageChannelDesc channelDesc = im.image.GetChannelDesc(["R", "G", "B"]);
            if (!(bool)channelDesc)
                ErrorExit(loc, $"{filename}: Image provided to \"diffuse\" area light must have R, G, and B channels.");
            image = im.image.SelectChannels(channelDesc);

            imageColorSpace = im.metadata.GetColorSpace();
        }
        else if (L == null)
            L = colorSpace.illuminant;

        // scale so that radiance is equivalent to 1 nit
        scale /= SpectrumToPhotometric(L ?? colorSpace.illuminant);

        Float phi_v = parameters.GetOneFloat("power", -1.0f);
        if (phi_v > 0)
        {
            // k_e is the emissive power of the light as defined by the spectral
            // distribution and texture and is used to normalize the emitted
            // radiance such that the user-defined power will be the actual power
            // emitted by the light.
            Float k_e = 1;
            if ((bool)image)
            {
                // Get the appropriate luminance vector from the image colour space
                RGB lum = imageColorSpace!.LuminanceVector();
                k_e = 0;
                // Assume no distortion in the mapping, FWIW...
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                        for (int c = 0; c < 3; ++c)
                            k_e += image.GetChannel(new Point2i(x, y), c) * lum[c];
                k_e /= image.Resolution().x * image.Resolution().y;
            }

            k_e *= (twoSided ? 2 : 1) * shape.Area() * Pi;

            // now multiply up scale to hit the target power
            scale *= phi_v / k_e;
        }

        return new DiffuseAreaLight(renderFromLight, new MediumInterface(medium), L, scale, shape, alphaTex, image,
                                    imageColorSpace, twoSided);
    }

    public override void Preprocess(in Bounds3f sceneBounds) { }

    public override SampledSpectrum Phi(in SampledWavelengths lambda)
    {
        SampledSpectrum L = new(0f);
        if ((bool)image)
        {
            // Compute average light image emission
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    RGB rgb = default;
                    for (int c = 0; c < 3; ++c)
                        rgb[c] = image.GetChannel(new Point2i(x, y), c);
                    L += new RGBIlluminantSpectrum(imageColorSpace!, ClampZero(rgb)).Sample(lambda);
                }
            L *= scale / (image.Resolution().x * image.Resolution().y);
        }
        else
            L = Lemit!.Sample(lambda) * scale;
        return Pi * (twoSided ? 2 : 1) * area * L;
    }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Sample a point on the area light's _Shape_
        ShapeSample? sso = shape.Sample(u1);
        if (sso is not ShapeSample ss)
            return null;
        ss.intr.time = time;
        ss.intr.mediumInterface = mediumInterface;

        // Check sampled point on shape against alpha texture, if present
        if (AlphaMasked(ss.intr))
            return null;

        // Sample a cosine-weighted outgoing direction _w_ for area light
        Vector3f w;
        Float pdfDir;
        if (twoSided)
        {
            // Choose side of surface and sample cosine-weighted outgoing direction
            if (u2[0] < 0.5f)
            {
                u2.x = FMath.Min(u2[0] * 2, OneMinusEpsilon);
                w = SampleCosineHemisphere(u2);
            }
            else
            {
                u2.x = FMath.Min((u2[0] - 0.5f) * 2, OneMinusEpsilon);
                w = SampleCosineHemisphere(u2);
                w.z *= -1;
            }
            pdfDir = CosineHemispherePDF(FMath.Abs(w.z)) / 2;
        }
        else
        {
            w = SampleCosineHemisphere(u2);
            pdfDir = CosineHemispherePDF(w.z);
        }
        if (pdfDir == 0)
            return null;

        // Return _LightLeSample_ for ray leaving area light
        Interaction intr = ss.intr;
        Frame nFrame = Frame.FromZ(intr.n);
        w = nFrame.FromLocal(w);
        SampledSpectrum Le = EmittedL(intr.p(), intr.n, intr.uv, w, lambda);
        return new LightLeSample(Le, intr.SpawnRay(w), intr, ss.pdf, pdfDir);
    }

    public override void PDF_Le(in Interaction intr, Vector3f w, out Float pdfPos, out Float pdfDir)
    {
        CHECK(intr.n != new Normal3f(0, 0, 0));
        pdfPos = shape.PDF(intr);
        pdfDir = twoSided ? (CosineHemispherePDF(AbsDot(intr.n, w)) / 2) : CosineHemispherePDF(Dot(intr.n, w));
    }

    public override LightBounds? Bounds()
    {
        // Compute _phi_ for diffuse area light bounds
        Float phi = 0;
        if ((bool)image)
        {
            // Compute average _DiffuseAreaLight_ image channel value
            // Assume no distortion in the mapping, FWIW...
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    for (int c = 0; c < 3; ++c)
                        phi += image.GetChannel(new Point2i(x, y), c);
            phi /= 3 * image.Resolution().x * image.Resolution().y;
        }
        else
            phi = Lemit!.MaxValue();
        phi *= scale * area * Pi;

        DirectionCone nb = shape.NormalBounds();
        return new LightBounds(shape.Bounds(), nb.w, phi, nb.cosTheta, FMath.Cos(Pi / 2), twoSided);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        LOG_FATAL("Shouldn't be called for area lights");
        pdfPos = pdfDir = 0;
    }

    public override string ToString() =>
        $"[ DiffuseAreaLight {BaseToString()} Lemit: {Lemit?.ToString() ?? "(nullptr)"} " +
        $"scale: {FloatToString(scale)} shape: {shape} alpha: {alpha?.ToString() ?? "(nullptr)"} " +
        $"twoSided: {(twoSided ? "true" : "false")} area: {FloatToString(area)} image: {image} ]";

    protected override SampledSpectrum EmittedL(Point3f p, Normal3f n, Point2f uv, Vector3f w,
                                                in SampledWavelengths lambda)
    {
        // Check for zero emitted radiance from point on area light
        if (!twoSided && Dot(n, w) < 0)
            return new SampledSpectrum(0f);
        if (AlphaMasked(new Interaction(p, uv)))
            return new SampledSpectrum(0f);

        if ((bool)image)
        {
            // Return _DiffuseAreaLight_ emission using image
            RGB rgb = default;
            uv[1] = 1 - uv[1];
            for (int c = 0; c < 3; ++c)
                rgb[c] = image.BilerpChannel(uv, c);
            RGBIlluminantSpectrum spec = new(imageColorSpace!, ClampZero(rgb));
            return scale * spec.Sample(lambda);
        }
        else
            return scale * Lemit!.Sample(lambda);
    }

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        // Sample point on shape for _DiffuseAreaLight_
        ShapeSampleContext shapeCtx = new(ctx.pi, ctx.n, ctx.ns, 0 /* time */);
        ShapeSample? sso = shape.Sample(shapeCtx, u);
        if (sso is not ShapeSample ss || ss.pdf == 0 || LengthSquared(ss.intr.p() - ctx.p()) == 0)
            return null;
        DCHECK(!IsNaN(ss.pdf));
        ss.intr.mediumInterface = mediumInterface;

        // Check sampled point on shape against alpha texture, if present
        if (AlphaMasked(ss.intr))
            return null;

        // Return _LightLiSample_ for sampled point on shape
        Vector3f wi = Normalize(ss.intr.p() - ctx.p());
        SampledSpectrum Le = EmittedL(ss.intr.p(), ss.intr.n, ss.intr.uv, -wi, lambda);
        if (!Le)
            return null;
        return new LightLiSample(Le, wi, ss.pdf, ss.intr);
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false)
    {
        ShapeSampleContext shapeCtx = new(ctx.pi, ctx.n, ctx.ns, 0 /* time */);
        return shape.PDF(shapeCtx, wi);
    }

    // DiffuseAreaLight Private Methods
    private bool AlphaMasked(in Interaction intr)
    {
        if (alpha == null)
            return false;
        Float a = new UniversalTextureEvaluator().Evaluate(alpha, new TextureEvalContext(intr));
        if (a >= 1)
            return false;
        if (a <= 0)
            return true;
        return HashFloat(intr.p()) > a;
    }
}

// UniformInfiniteLight Definition
public sealed class UniformInfiniteLight : LightBase
{
    // UniformInfiniteLight Private Members
    private readonly DenselySampledSpectrum Lemit;
    private readonly Float scale;
    private Point3f sceneCenter;
    private Float sceneRadius;

    // UniformInfiniteLight Method Definitions
    public UniformInfiniteLight(Transform renderFromLight, Spectrum Lemit, Float scale)
        : base(LightType.Infinite, renderFromLight, new MediumInterface())
    {
        this.Lemit = LookupSpectrum(Lemit);
        this.scale = scale;
    }

    public override void Preprocess(in Bounds3f sceneBounds) =>
        sceneBounds.BoundingSphere(out sceneCenter, out sceneRadius);

    public override SampledSpectrum Le(in Ray ray, in SampledWavelengths lambda) => scale * Lemit.Sample(lambda);

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        if (allowIncompletePDF)
            return null;
        // Return uniform spherical sample for uniform infinite light
        Vector3f wi = SampleUniformSphere(u);
        Float pdf = UniformSpherePDF();
        return new LightLiSample(scale * Lemit.Sample(lambda), wi, pdf,
                                 new Interaction(ctx.p() + wi * (2 * sceneRadius), mediumInterface));
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
    {
        if (allowIncompletePDF)
            return 0;
        return UniformSpherePDF();
    }

    public override SampledSpectrum Phi(in SampledWavelengths lambda) =>
        4 * Pi * Pi * Sqr(sceneRadius) * scale * Lemit.Sample(lambda);

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Sample direction for uniform infinite light ray
        Vector3f w = SampleUniformSphere(u1);

        // Compute infinite light sample ray
        Frame wFrame = Frame.FromZ(-w);
        Point2f cd = SampleUniformDiskConcentric(u2);
        Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
        Ray ray = new(pDisk + sceneRadius * -w, w, time);

        // Compute probabilities for uniform infinite light
        Float pdfPos = 1 / (Pi * Sqr(sceneRadius));
        Float pdfDir = UniformSpherePDF();

        return new LightLeSample(scale * Lemit.Sample(lambda), ray, pdfPos, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        pdfDir = UniformSpherePDF();
        pdfPos = 1 / (Pi * Sqr(sceneRadius));
    }

    public override LightBounds? Bounds() => null;

    public override string ToString() => $"[ UniformInfiniteLight {BaseToString()} Lemit: {Lemit} ]";
}

// ImageInfiniteLight Definition
public sealed class ImageInfiniteLight : LightBase
{
    // ImageInfiniteLight Private Members
    private readonly Image image;
    private readonly RGBColorSpace imageColorSpace;
    private readonly Float scale;
    private Point3f sceneCenter;
    private Float sceneRadius;
    private readonly PiecewiseConstant2D distribution;
    private readonly PiecewiseConstant2D compensatedDistribution;

    // ImageInfiniteLight Method Definitions
    public ImageInfiniteLight(Transform renderFromLight, Image im, RGBColorSpace imageColorSpace, Float scale,
                              string filename)
        : base(LightType.Infinite, renderFromLight, new MediumInterface())
    {
        image = im;
        this.imageColorSpace = imageColorSpace;
        this.scale = scale;
        // ImageInfiniteLight constructor implementation
        // Initialize sampling PDFs for image infinite area light
        ImageChannelDesc channelDesc = image.GetChannelDesc(["R", "G", "B"]);
        if (!(bool)channelDesc)
            ErrorExit($"{filename}: image used for ImageInfiniteLight doesn't have R, G, B channels.");
        CHECK_EQ(3, channelDesc.Size);
        CHECK(channelDesc.IsIdentity());
        if (image.Resolution().x != image.Resolution().y)
            ErrorExit($"{filename}: image resolution ({image.Resolution().x}, {image.Resolution().y}) is " +
                      "non-square. It's unlikely this is an equal area environment map.");
        Array2D<Float> d = image.GetSamplingDistribution();
        Bounds2f domain = new(new Point2f(0, 0), new Point2f(1, 1));
        distribution = new PiecewiseConstant2D(d, domain);

        // Initialize compensated PDF for image infinite area light
        // (pbrt accumulates the sum in a double.)
        Span<Float> dv = d.AsSpan();
        double sum = 0;
        foreach (Float v in dv)
            sum += v;
        Float average = (Float)(sum / dv.Length);
        for (int i = 0; i < dv.Length; ++i)
            dv[i] = FMath.Max(dv[i] - average, 0);
        bool allZero = true;
        foreach (Float v in dv)
            if (v != 0)
                allZero = false;
        if (allZero)
            dv.Fill(1);
        compensatedDistribution = new PiecewiseConstant2D(d, domain);
    }

    public override void Preprocess(in Bounds3f sceneBounds) =>
        sceneBounds.BoundingSphere(out sceneCenter, out sceneRadius);

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
    {
        Vector3f wLight = renderFromLight.ApplyInverse(w);
        Point2f uv = EqualAreaSphereToSquare(wLight);
        Float pdf;
        if (allowIncompletePDF)
            pdf = compensatedDistribution.PDF(uv);
        else
            pdf = distribution.PDF(uv);
        return pdf / (4 * Pi);
    }

    public override SampledSpectrum Phi(in SampledWavelengths lambda)
    {
        // We're computing fluence, then converting to power...
        SampledSpectrum sumL = new(0f);

        int width = image.Resolution().x, height = image.Resolution().y;
        for (int v = 0; v < height; ++v)
        {
            for (int u = 0; u < width; ++u)
            {
                RGB rgb = default;
                for (int c = 0; c < 3; ++c)
                    rgb[c] = image.GetChannel(new Point2i(u, v), c, WrapMode.OctahedralSphere);
                sumL += new RGBIlluminantSpectrum(imageColorSpace, ClampZero(rgb)).Sample(lambda);
            }
        }
        // Integrating over the sphere, so 4pi for that.  Then one more for Pi
        // r^2 for the area of the disk receiving illumination...
        return 4 * Pi * Pi * Sqr(sceneRadius) * scale * sumL / (width * height);
    }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Sample infinite light image and compute ray direction _w_
        Point2f uv = distribution.Sample(u1, out Float mapPDF, out _);
        Vector3f wLight = EqualAreaSquareToSphere(uv);
        Vector3f w = -renderFromLight.Apply(wLight);

        // Compute infinite light sample ray
        Frame wFrame = Frame.FromZ(-w);
        Point2f cd = SampleUniformDiskConcentric(u2);
        Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
        Ray ray = new(pDisk + sceneRadius * -w, w, time);

        // Compute _ImageInfiniteLight_ ray PDFs
        Float pdfDir = mapPDF / (4 * Pi);
        Float pdfPos = 1 / (Pi * Sqr(sceneRadius));

        return new LightLeSample(ImageLe(uv, lambda), ray, pdfPos, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        Vector3f wl = -renderFromLight.ApplyInverse(ray.d);
        Float mapPDF = distribution.PDF(EqualAreaSphereToSquare(wl));
        pdfDir = mapPDF / (4 * Pi);
        pdfPos = 1 / (Pi * Sqr(sceneRadius));
    }

    public override string ToString() => $"[ ImageInfiniteLight {BaseToString()} scale: {FloatToString(scale)} ]";

    public override SampledSpectrum Le(in Ray ray, in SampledWavelengths lambda)
    {
        Vector3f wLight = Normalize(renderFromLight.ApplyInverse(ray.d));
        Point2f uv = EqualAreaSphereToSquare(wLight);
        return ImageLe(uv, lambda);
    }

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        // Find $(u,v)$ sample coordinates in infinite light texture
        Float mapPDF;
        Point2f uv;
        if (allowIncompletePDF)
            uv = compensatedDistribution.Sample(u, out mapPDF, out _);
        else
            uv = distribution.Sample(u, out mapPDF, out _);
        if (mapPDF == 0)
            return null;

        // Convert infinite light sample point to direction
        Vector3f wLight = EqualAreaSquareToSphere(uv);
        Vector3f wi = renderFromLight.Apply(wLight);

        // Compute PDF for sampled infinite light direction
        Float pdf = mapPDF / (4 * Pi);

        // Return radiance value for infinite light direction
        return new LightLiSample(ImageLe(uv, lambda), wi, pdf,
                                 new Interaction(ctx.p() + wi * (2 * sceneRadius), mediumInterface));
    }

    public override LightBounds? Bounds() => null;

    // ImageInfiniteLight Private Methods
    private SampledSpectrum ImageLe(Point2f uv, in SampledWavelengths lambda)
    {
        RGB rgb = default;
        for (int c = 0; c < 3; ++c)
            rgb[c] = image.LookupNearestChannel(uv, c, WrapMode.OctahedralSphere);
        RGBIlluminantSpectrum spec = new(imageColorSpace, ClampZero(rgb));
        return scale * spec.Sample(lambda);
    }
}

// PortalImageInfiniteLight Definition
public sealed class PortalImageInfiniteLight : LightBase
{
    // PortalImageInfiniteLight Private Members
    private readonly Point3f[] portal = new Point3f[4];
    private readonly Frame portalFrame;
    private readonly Image image;
    private readonly WindowedPiecewiseConstant2D distribution;
    private readonly RGBColorSpace imageColorSpace;
    private readonly Float scale;
    private Float sceneRadius;
    private readonly string filename;
    private Point3f sceneCenter;

    // PortalImageInfiniteLight Method Definitions
    public PortalImageInfiniteLight(Transform renderFromLight, Image equalAreaImage, RGBColorSpace imageColorSpace,
                                    Float scale, string filename, List<Point3f> p)
        : base(LightType.Infinite, renderFromLight, new MediumInterface())
    {
        this.imageColorSpace = imageColorSpace;
        this.scale = scale;
        this.filename = filename;
        ImageChannelDesc channelDesc = equalAreaImage.GetChannelDesc(["R", "G", "B"]);
        if (!(bool)channelDesc)
            ErrorExit($"{filename}: image used for PortalImageInfiniteLight doesn't have R, G, B channels.");
        CHECK_EQ(3, channelDesc.Size);
        CHECK(channelDesc.IsIdentity());

        if (equalAreaImage.Resolution().x != equalAreaImage.Resolution().y)
            ErrorExit($"{filename}: image resolution ({equalAreaImage.Resolution().x}, " +
                      $"{equalAreaImage.Resolution().y}) is non-square. It's unlikely this is an equal area " +
                      "environment map.");

        if (p.Count != 4)
            ErrorExit($"Expected 4 vertices for infinite light portal but given {p.Count}");
        for (int i = 0; i < 4; ++i)
            portal[i] = p[i];

        // PortalImageInfiniteLight constructor conclusion
        // Compute frame for portal coordinate system
        Vector3f p01 = Normalize(portal[1] - portal[0]);
        Vector3f p12 = Normalize(portal[2] - portal[1]);
        Vector3f p32 = Normalize(portal[2] - portal[3]);
        Vector3f p03 = Normalize(portal[3] - portal[0]);
        // Do opposite edges have the same direction?
        if (FMath.Abs(Dot(p01, p32) - 1) > .001 || FMath.Abs(Dot(p12, p03) - 1) > .001)
            Error("Infinite light portal isn't a planar quadrilateral");
        // Sides perpendicular?
        if (FMath.Abs(Dot(p01, p12)) > .001 || FMath.Abs(Dot(p12, p32)) > .001 ||
            FMath.Abs(Dot(p32, p03)) > .001 || FMath.Abs(Dot(p03, p01)) > .001)
            Error("Infinite light portal isn't a planar quadrilateral");
        portalFrame = Frame.FromXY(p03, p01);

        // Resample environment map into rectified image
        image = new Image(PixelFormat.Float, equalAreaImage.Resolution(), ["R", "G", "B"],
                          equalAreaImage.Encoding());
        Image img = image;
        ParallelFor(0, image.Resolution().y, (long y) =>
        {
            for (int x = 0; x < img.Resolution().x; ++x)
            {
                // Resample _equalAreaImage_ to compute rectified image pixel $(x,y)$
                // Find $(u,v)$ coordinates in equal-area image for pixel
                Point2f uv = new((x + 0.5f) / img.Resolution().x, (y + 0.5f) / img.Resolution().y);
                Vector3f w = RenderFromImage(uv, out _);
                w = Normalize(renderFromLight.ApplyInverse(w));
                Point2f uvEqui = EqualAreaSphereToSquare(w);

                for (int c = 0; c < 3; ++c)
                {
                    Float v = equalAreaImage.BilerpChannel(uvEqui, c, WrapMode.OctahedralSphere);
                    img.SetChannel(new Point2i(x, (int)y), c, v);
                }
            }
        });

        // Initialize sampling distribution for portal image infinite light
        Array2D<Float> d = image.GetSamplingDistribution(p =>
        {
            RenderFromImage(p, out Float duv_dw);
            return duv_dw;
        });
        distribution = new WindowedPiecewiseConstant2D(d);
    }

    public override void Preprocess(in Bounds3f sceneBounds) =>
        sceneBounds.BoundingSphere(out sceneCenter, out sceneRadius);

    public override SampledSpectrum Phi(in SampledWavelengths lambda)
    {
        // We're really computing fluence, then converting to power, for what
        // that's worth..
        SampledSpectrum sumL = new(0f);

        for (int y = 0; y < image.Resolution().y; ++y)
        {
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                RGB rgb = default;
                for (int c = 0; c < 3; ++c)
                    rgb[c] = image.GetChannel(new Point2i(x, y), c);

                Point2f st = new((x + 0.5f) / image.Resolution().x, (y + 0.5f) / image.Resolution().y);
                RenderFromImage(st, out Float duv_dw);

                sumL += new RGBIlluminantSpectrum(imageColorSpace, ClampZero(rgb)).Sample(lambda) / duv_dw;
            }
        }

        return scale * Area() * sumL / (image.Resolution().x * image.Resolution().y);
    }

    public override SampledSpectrum Le(in Ray ray, in SampledWavelengths lambda)
    {
        Point2f? uv = ImageFromRender(Normalize(ray.d), out _);
        Bounds2f? b = ImageBounds(ray.o);
        if (uv is not Point2f uvv || b is not Bounds2f bb || !Inside(uvv, bb))
            return new SampledSpectrum(0f);
        return ImageLookup(uvv, lambda);
    }

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        // Sample $(u,v)$ in potentially visible region of light image
        if (ImageBounds(ctx.p()) is not Bounds2f b)
            return null;
        if (distribution.Sample(u, b, out Float mapPDF) is not Point2f uv)
            return null;

        // Convert portal image sample point to direction and compute PDF
        Vector3f wi = RenderFromImage(uv, out Float duv_dw);
        if (duv_dw == 0)
            return null;
        Float pdf = mapPDF / duv_dw;
        CHECK(!IsInf(pdf));

        // Compute radiance for portal light sample and return _LightLiSample_
        SampledSpectrum L = ImageLookup(uv, lambda);
        Point3f pl = ctx.p() + 2 * sceneRadius * wi;
        return new LightLiSample(L, wi, pdf, new Interaction(pl, mediumInterface));
    }

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
    {
        // Find image $(u,v)$ coordinates corresponding to direction _w_
        Point2f? uv = ImageFromRender(w, out Float duv_dw);
        if (uv is not Point2f uvv || duv_dw == 0)
            return 0;

        // Return PDF for sampling $(u,v)$ from reference point
        if (ImageBounds(ctx.p()) is not Bounds2f b)
            return 0;
        Float pdf = distribution.PDF(uvv, b);
        return pdf / duv_dw;
    }

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        Bounds2f b = new(new Point2f(0, 0), new Point2f(1, 1));
        if (distribution.Sample(u1, b, out Float mapPDF) is not Point2f uv)
            return null;

        // Convert infinite light sample point to direction
        // Note: ignore WorldToLight since we already folded it in when we
        // resampled...
        Vector3f w = -RenderFromImage(uv, out Float duv_dw);
        if (duv_dw == 0)
            return null;

        // Compute PDF for sampled infinite light direction
        Float pdfDir = mapPDF / duv_dw;

        // Compute infinite light sample ray
        Frame wFrame = Frame.FromZ(-w);
        Point2f cd = SampleUniformDiskConcentric(u2);
        Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
        Ray ray = new(pDisk + sceneRadius * -w, w, time);

        Float pdfPos = 1 / (Pi * Sqr(sceneRadius));

        SampledSpectrum L = ImageLookup(uv, lambda);

        return new LightLeSample(L, ray, pdfPos, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        // TODO: negate here or???
        Vector3f w = -Normalize(ray.d);
        Point2f? uv = ImageFromRender(w, out Float duv_dw);

        if (uv is not Point2f uvv || duv_dw == 0)
        {
            pdfPos = pdfDir = 0;
            return;
        }

        Bounds2f b = new(new Point2f(0, 0), new Point2f(1, 1));
        Float pdf = distribution.PDF(uvv, b);

        pdfPos = 1 / (Pi * Sqr(sceneRadius));

        pdfDir = pdf / duv_dw;
    }

    public override LightBounds? Bounds() => null;

    public override string ToString() =>
        $"[ PortalImageInfiniteLight {BaseToString()} filename:{filename} scale: {FloatToString(scale)} " +
        $"portal: [ {string.Join(", ", portal)} ]  portalFrame: {portalFrame} ]";

    // PortalImageInfiniteLight Private Methods
    private SampledSpectrum ImageLookup(Point2f uv, in SampledWavelengths lambda)
    {
        RGB rgb = default;
        for (int c = 0; c < 3; ++c)
            rgb[c] = image.LookupNearestChannel(uv, c);
        RGBIlluminantSpectrum spec = new(imageColorSpace, ClampZero(rgb));
        return scale * spec.Sample(lambda);
    }

    private Point2f? ImageFromRender(Vector3f wRender, out Float duv_dw)
    {
        duv_dw = 0;
        Vector3f w = portalFrame.ToLocal(wRender);
        if (w.z <= 0)
            return null;
        // Compute Jacobian determinant of mapping $\roman{d}(u,v)/\roman{d}\omega$ if
        // needed
        duv_dw = Sqr(Pi) * (1 - Sqr(w.x)) * (1 - Sqr(w.y)) / w.z;

        Float alpha = FMath.Atan2(w.x, w.z), beta = FMath.Atan2(w.y, w.z);
        DCHECK(!IsNaN(alpha + beta));
        return new Point2f(Clamp((alpha + Pi / 2) / Pi, 0, 1), Clamp((beta + Pi / 2) / Pi, 0, 1));
    }

    private Vector3f RenderFromImage(Point2f uv, out Float duv_dw)
    {
        Float alpha = -Pi / 2 + uv[0] * Pi, beta = -Pi / 2 + uv[1] * Pi;
        Float x = FMath.Tan(alpha), y = FMath.Tan(beta);
        DCHECK(!IsInf(x) && !IsInf(y));
        Vector3f w = Normalize(new Vector3f(x, y, 1));
        // Compute Jacobian determinant of mapping $\roman{d}(u,v)/\roman{d}\omega$ if
        // needed
        duv_dw = Sqr(Pi) * (1 - Sqr(w.x)) * (1 - Sqr(w.y)) / w.z;

        return portalFrame.FromLocal(w);
    }

    private Bounds2f? ImageBounds(Point3f p)
    {
        Point2f? p0 = ImageFromRender(Normalize(portal[0] - p), out _);
        Point2f? p1 = ImageFromRender(Normalize(portal[2] - p), out _);
        if (p0 is not Point2f a || p1 is not Point2f b)
            return null;
        return new Bounds2f(a, b);
    }

    private Float Area() => Length(portal[1] - portal[0]) * Length(portal[3] - portal[0]);
}

// SpotLight Definition
public sealed class SpotLight : LightBase
{
    // SpotLight Private Members
    private readonly DenselySampledSpectrum Iemit;
    private readonly Float scale, cosFalloffStart, cosFalloffEnd;

    // SpotLight Method Definitions
    public SpotLight(Transform renderFromLight, in MediumInterface mediumInterface, Spectrum Iemit, Float scale,
                     Float totalWidth, Float falloffStart)
        : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
    {
        this.Iemit = LookupSpectrum(Iemit);
        this.scale = scale;
        cosFalloffEnd = FMath.Cos(Radians(totalWidth));
        cosFalloffStart = FMath.Cos(Radians(falloffStart));
        CHECK_LE(falloffStart, totalWidth);
    }

    public static SpotLight Create(Transform renderFromLight, Medium? medium, ParameterDictionary parameters,
                                   RGBColorSpace colorSpace, FileLoc? loc)
    {
        Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant)!;
        Float sc = parameters.GetOneFloat("scale", 1);

        Float coneangle = parameters.GetOneFloat("coneangle", 30f);
        Float conedelta = parameters.GetOneFloat("conedeltaangle", 5f);
        // Compute spotlight rendering to light transformation
        Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
        Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));

        Transform dirToZ = new(Frame.FromZ(Normalize(to - from)));
        Transform t = Translate(new Vector3f(from.x, from.y, from.z)) * Inverse(dirToZ);
        Transform finalRenderFromLight = renderFromLight * t;

        sc /= SpectrumToPhotometric(I);

        Float phi_v = parameters.GetOneFloat("power", -1);
        if (phi_v > 0)
        {
            Float cosFalloffEnd = FMath.Cos(Radians(coneangle));
            Float cosFalloffStart = FMath.Cos(Radians(coneangle - conedelta));
            Float k_e = 2 * Pi * ((1 - cosFalloffStart) + (cosFalloffStart - cosFalloffEnd) / 2);
            sc *= phi_v / k_e;
        }

        return new SpotLight(finalRenderFromLight, new MediumInterface(medium), I, sc, coneangle,
                             coneangle - conedelta);
    }

    public override void Preprocess(in Bounds3f sceneBounds) { }

    public SampledSpectrum I(Vector3f w, in SampledWavelengths lambda) =>
        SmoothStep(CosTheta(w), cosFalloffEnd, cosFalloffStart) * scale * Iemit.Sample(lambda);

    public override SampledSpectrum Phi(in SampledWavelengths lambda) =>
        scale * Iemit.Sample(lambda) * 2 * Pi * ((1 - cosFalloffStart) + (cosFalloffStart - cosFalloffEnd) / 2);

    public override Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) => 0f;

    public override LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
    {
        // Choose whether to sample spotlight center cone or falloff region
        ReadOnlySpan<Float> p = [1 - cosFalloffStart, (cosFalloffStart - cosFalloffEnd) / 2];
        int section = SampleDiscrete(p, u2[0], out Float sectionPDF, out _);

        // Sample chosen region of spotlight cone
        Vector3f wLight;
        Float pdfDir;
        if (section == 0)
        {
            // Sample spotlight center cone
            wLight = SampleUniformCone(u1, cosFalloffStart);
            pdfDir = UniformConePDF(cosFalloffStart) * sectionPDF;
        }
        else
        {
            // Sample spotlight falloff region
            Float cosTheta = SampleSmoothStep(u1[0], cosFalloffEnd, cosFalloffStart);
            DCHECK(cosTheta >= cosFalloffEnd && cosTheta <= cosFalloffStart);
            Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
            Float phi = u1[1] * 2 * Pi;
            wLight = SphericalDirection(sinTheta, cosTheta, phi);
            pdfDir = SmoothStepPDF(cosTheta, cosFalloffEnd, cosFalloffStart) * sectionPDF / (2 * Pi);
        }

        // Return sampled spotlight ray
        Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), wLight, time, mediumInterface.outside));
        return new LightLeSample(I(wLight, lambda), ray, 1, pdfDir);
    }

    public override void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        Float p0 = 1 - cosFalloffStart, p1 = (cosFalloffStart - cosFalloffEnd) / 2;
        pdfPos = 0;
        // Find spotlight directional PDF based on $\cos \theta$
        Float cosTheta = CosTheta(renderFromLight.ApplyInverse(ray.d));
        if (cosTheta >= cosFalloffStart)
            pdfDir = UniformConePDF(cosFalloffStart) * p0 / (p0 + p1);
        else
            pdfDir = SmoothStepPDF(cosTheta, cosFalloffEnd, cosFalloffStart) * p1 / ((p0 + p1) * (2 * Pi));
    }

    public override LightBounds? Bounds()
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f w = Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
        Float phi = scale * Iemit.MaxValue() * 4 * Pi;
        Float cosTheta_e = FMath.Cos(FMath.Acos(cosFalloffEnd) - FMath.Acos(cosFalloffStart));
        // Allow a little slop here to deal with fp round-off error in the computation of
        // cosTheta_p in the importance function.
        if (cosTheta_e == 1 && cosFalloffEnd != cosFalloffStart)
            cosTheta_e = 0.999f;
        return new LightBounds(new Bounds3f(p, p), w, phi, cosFalloffStart, cosTheta_e, false);
    }

    public override string ToString() =>
        $"[ SpotLight {BaseToString()} Iemit: {Iemit} cosFalloffStart: {FloatToString(cosFalloffStart)} " +
        $"cosFalloffEnd: {FloatToString(cosFalloffEnd)} ]";

    public override LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false)
    {
        Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
        Vector3f wi = Normalize(p - ctx.p());
        // Compute incident radiance _Li_ for _SpotLight_
        Vector3f wLight = Normalize(renderFromLight.ApplyInverse(-wi));
        SampledSpectrum Li = I(wLight, lambda) / DistanceSquared(p, ctx.p());

        if (!Li)
            return null;
        return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
    }
}

public abstract partial class Light
{
    public static Light Create(string name, ParameterDictionary parameters, Transform renderFromLight,
                               CameraTransform cameraTransform, Medium? outsideMedium, FileLoc? loc)
    {
        Light? light = null;
        if (name == "point")
            light = PointLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace()!, loc);
        else if (name == "spot")
            light = SpotLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace()!, loc);
        else if (name == "goniometric")
            light = GoniometricLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace()!,
                                            loc);
        else if (name == "projection")
            light = ProjectionLight.Create(renderFromLight, outsideMedium, parameters, loc);
        else if (name == "distant")
            light = DistantLight.Create(renderFromLight, parameters, parameters.ColorSpace()!, loc);
        else if (name == "infinite")
        {
            RGBColorSpace colorSpace = parameters.ColorSpace()!;
            List<Spectrum> L = parameters.GetSpectrumArray("L", SpectrumType.Illuminant);
            Float scale = parameters.GetOneFloat("scale", 1);
            List<Point3f> portal = parameters.GetPoint3fArray("portal");
            string filename = ResolveFilename(parameters.GetOneString("filename", ""));
            Float E_v = parameters.GetOneFloat("illuminance", -1);

            if (L.Count == 0 && filename.Length == 0 && portal.Count == 0)
            {
                // Scale the light spectrum to be equivalent to 1 nit
                scale /= SpectrumToPhotometric(colorSpace.illuminant);
                if (E_v > 0)
                {
                    // If the scene specifies desired illuminance, first calculate
                    // the illuminance from a uniform hemispherical emission
                    // of L_v then use this to scale the emission spectrum.
                    Float k_e = Pi;
                    scale *= E_v / k_e;
                }

                // Default: color space's std illuminant
                light = new UniformInfiniteLight(renderFromLight, colorSpace.illuminant, scale);
            }
            else if (L.Count > 0 && portal.Count == 0)
            {
                if (filename.Length > 0)
                    ErrorExit(loc, "Can't specify both emission \"L\" and \"filename\" with ImageInfiniteLight");

                // Scale the light spectrum to be equivalent to 1 nit
                scale /= SpectrumToPhotometric(L[0]);

                if (E_v > 0)
                {
                    // If the scene specifies desired illuminance, first calculate
                    // the illuminance from a uniform hemispherical emission
                    // of L_v then use this to scale the emission spectrum.
                    Float k_e = Pi;
                    scale *= E_v / k_e;
                }

                light = new UniformInfiniteLight(renderFromLight, L[0], scale);
            }
            else
            {
                // Either an image was provided or it's "L" with a portal.
                ImageAndMetadata imageAndMetadata;
                if (filename.Length == 0)
                {
                    // Create a uniform image with the L spectrum converted to
                    // RGB so it can be stored in an Image. This usually should
                    // be ok, but it is the best we can do under the circumstances.
                    // (More generally, for uniform L, it's just as well to create
                    // an emissive bilinear patch at the portal location, though
                    // that doesn't allow things like putting an emissive sphere out
                    // there for the sun, so here we go...
                    if (L[0] is not RGBIlluminantSpectrum)
                        Warning(loc, "Converting non-RGB \"L\" parameter to RGB so that a portal light can be used.");
                    XYZ xyz = SpectrumToXYZ(L[0]);
                    RGB rgb = RGBColorSpace.sRGB.ToRGB(xyz);

                    int res = 1; // happily, this all just works.
                    imageAndMetadata = new ImageAndMetadata(
                        new Image(PixelFormat.Float, new Point2i(res, res), ["R", "G", "B"]), new ImageMetadata());
                    imageAndMetadata.metadata.colorSpace = RGBColorSpace.sRGB;
                    for (int y = 0; y < res; ++y)
                        for (int x = 0; x < res; ++x)
                            for (int c = 0; c < 3; ++c)
                                imageAndMetadata.image.SetChannel(new Point2i(x, y), c, rgb[c]);
                }
                else
                {
                    imageAndMetadata = Image.Read(filename);

                    if (imageAndMetadata.image.HasAnyInfinitePixels())
                        ErrorExit(loc,
                                  $"{filename}: image has infinite pixel values and so is not suitable as a light.");
                    if (imageAndMetadata.image.HasAnyNaNPixels())
                        ErrorExit(loc,
                                  $"{filename}: image has not-a-number pixel values and so is not suitable as a light.");
                }

                RGBColorSpace imageColorSpace = imageAndMetadata.metadata.GetColorSpace();

                ImageChannelDesc channelDesc = imageAndMetadata.image.GetChannelDesc(["R", "G", "B"]);
                if (!(bool)channelDesc)
                    ErrorExit(loc,
                              $"{filename}: image provided to \"infinite\" light must have R, G, and B channels.");

                // Scale the light spectrum to be equivalent to 1 nit
                scale /= SpectrumToPhotometric(imageColorSpace.illuminant);

                if (E_v > 0)
                {
                    // Upper hemisphere illuminance calculation for converting map to physical
                    // units
                    float illuminance = 0;
                    Image img = imageAndMetadata.image;
                    RGB lum = imageAndMetadata.metadata.GetColorSpace().LuminanceVector();
                    for (int y = 0; y < img.Resolution().y; ++y)
                    {
                        float v = ((float)y + 0.5f) / (float)img.Resolution().y;
                        for (int x = 0; x < img.Resolution().x; ++x)
                        {
                            Float u = (x + 0.5f) / img.Resolution().x;
                            Vector3f w = EqualAreaSquareToSphere(new Point2f(u, v));
                            // We could be more clever and see if we're in the inner rotated
                            // square, but not a big deal...
                            if (w.z <= 0)
                                continue;

                            ImageChannelValues values = img.GetChannels(new Point2i(x, y));
                            for (int c = 0; c < 3; ++c)
                                illuminance = (float)(illuminance + values[c] * lum[c] * CosTheta(w));
                        }
                    }
                    illuminance = (float)(illuminance * (2 * Pi / (img.Resolution().x * img.Resolution().y)));

                    // scaling factor is just the ratio of the target
                    // illuminance and the illuminance of the map multiplied by
                    // the illuminant spectrum
                    Float k_e = illuminance;
                    scale *= E_v / k_e;
                }

                Image image = imageAndMetadata.image.SelectChannels(channelDesc);

                if (portal.Count > 0)
                {
                    for (int i = 0; i < portal.Count; ++i)
                        portal[i] = cameraTransform.RenderFromWorld(portal[i]);

                    light = new PortalImageInfiniteLight(renderFromLight, image, imageColorSpace, scale, filename,
                                                         portal);
                }
                else
                    light = new ImageInfiniteLight(renderFromLight, image, imageColorSpace, scale, filename);
            }
        }
        else
            ErrorExit(loc, $"{name}: light type unknown.");

        if (light == null)
            ErrorExit(loc, $"{name}: unable to create light.");

        parameters.ReportUnused();
        return light!;
    }

    public static Light CreateArea(string name, ParameterDictionary parameters, Transform renderFromLight,
                                   in MediumInterface mediumInterface, Shape shape, FloatTexture? alpha, FileLoc? loc)
    {
        Light? area = null;
        if (name == "diffuse")
            area = DiffuseAreaLight.Create(renderFromLight, mediumInterface.outside, parameters,
                                           parameters.ColorSpace()!, loc, shape, alpha);
        else
            ErrorExit(loc, $"{name}: area light type unknown.");

        if (area == null)
            ErrorExit(loc, $"{name}: unable to create area light.");

        parameters.ReportUnused();
        return area!;
    }
}

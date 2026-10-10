// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/bxdfs.h and bxdfs.cpp
//
// The BxDFs are classes deriving from BxDF. pbrt's LayeredBxDF template is
// a generic class over the top and bottom BxDF types, and its
// TopOrBottomBxDF helper is a plain BxDF reference. pbrt's LayeredBxDF
// draws several random numbers within single expressions such as
// `Sample_f(wo, r(), Point2f(r(), r()))`, whose order of evaluation C++
// leaves unspecified; the code here draws them into locals in the order the
// reference GCC build evaluates them (right to left for function and
// constructor arguments, left to right within braces).

namespace Pbrt;

// DiffuseBxDF Definition
public sealed class DiffuseBxDF : BxDF
{
    private readonly SampledSpectrum R;

    // DiffuseBxDF Public Methods
    public DiffuseBxDF() { }

    public DiffuseBxDF(in SampledSpectrum R) => this.R = R;

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        if (!SameHemisphere(wo, wi))
            return new SampledSpectrum(0f);
        return R * InvPi;
    }

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return null;
        // Sample cosine-weighted hemisphere to compute _wi_ and _pdf_
        Vector3f wi = SampleCosineHemisphere(u);
        if (wo.z < 0)
            wi.z *= -1;
        Float pdf = CosineHemispherePDF(AbsCosTheta(wi));

        return new BSDFSample(R * InvPi, wi, pdf, BxDFFlags.DiffuseReflection);
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0 || !SameHemisphere(wo, wi))
            return 0;
        return CosineHemispherePDF(AbsCosTheta(wi));
    }

    public static string Name() => "DiffuseBxDF";

    public override string ToString() => $"[ DiffuseBxDF R: {R} ]";

    public override void Regularize() { }

    public override BxDFFlags Flags() => R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset;
}

// DiffuseTransmissionBxDF Definition
public sealed class DiffuseTransmissionBxDF : BxDF
{
    // DiffuseTransmissionBxDF Private Members
    private readonly SampledSpectrum R, T;

    // DiffuseTransmissionBxDF Public Methods
    public DiffuseTransmissionBxDF() { }

    public DiffuseTransmissionBxDF(in SampledSpectrum R, in SampledSpectrum T)
    {
        this.R = R;
        this.T = T;
    }

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode) =>
        SameHemisphere(wo, wi) ? (R * InvPi) : (T * InvPi);

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        // Compute reflection and transmission probabilities for diffuse BSDF
        Float pr = R.MaxComponentValue(), pt = T.MaxComponentValue();
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            pr = 0;
        if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            pt = 0;
        if (pr == 0 && pt == 0)
            return null;

        // Randomly sample diffuse BSDF reflection or transmission
        if (uc < pr / (pr + pt))
        {
            // Sample diffuse BSDF reflection
            Vector3f wi = SampleCosineHemisphere(u);
            if (wo.z < 0)
                wi.z *= -1;
            Float pdf = CosineHemispherePDF(AbsCosTheta(wi)) * pr / (pr + pt);
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseReflection);
        }
        else
        {
            // Sample diffuse BSDF transmission
            Vector3f wi = SampleCosineHemisphere(u);
            if (wo.z > 0)
                wi.z *= -1;
            Float pdf = CosineHemispherePDF(AbsCosTheta(wi)) * pt / (pr + pt);
            return new BSDFSample(f(wo, wi, mode), wi, pdf, BxDFFlags.DiffuseTransmission);
        }
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        // Compute reflection and transmission probabilities for diffuse BSDF
        Float pr = R.MaxComponentValue(), pt = T.MaxComponentValue();
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            pr = 0;
        if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            pt = 0;
        if (pr == 0 && pt == 0)
            return 0;

        if (SameHemisphere(wo, wi))
            return pr / (pr + pt) * CosineHemispherePDF(AbsCosTheta(wi));
        else
            return pt / (pr + pt) * CosineHemispherePDF(AbsCosTheta(wi));
    }

    public static string Name() => "DiffuseTransmissionBxDF";

    public override string ToString() => $"[ DiffuseTransmissionBxDF R: {R} T: {T} ]";

    public override void Regularize() { }

    public override BxDFFlags Flags() =>
        (R ? BxDFFlags.DiffuseReflection : BxDFFlags.Unset) | (T ? BxDFFlags.DiffuseTransmission : BxDFFlags.Unset);
}

// DielectricBxDF Definition
public sealed class DielectricBxDF : BxDF
{
    // DielectricBxDF Private Members
    private readonly Float eta;
    private TrowbridgeReitzDistribution mfDistrib;

    // DielectricBxDF Public Methods
    public DielectricBxDF(Float eta, TrowbridgeReitzDistribution mfDistrib)
    {
        this.eta = eta;
        this.mfDistrib = mfDistrib;
    }

    public override BxDFFlags Flags()
    {
        BxDFFlags flags = (eta == 1) ? BxDFFlags.Transmission : (BxDFFlags.Reflection | BxDFFlags.Transmission);
        return flags | (mfDistrib.EffectivelySmooth() ? BxDFFlags.Specular : BxDFFlags.Glossy);
    }

    // DielectricBxDF Method Definitions
    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if (eta == 1 || mfDistrib.EffectivelySmooth())
        {
            // Sample perfect specular dielectric BSDF
            Float R = FrDielectric(CosTheta(wo), eta), T = 1 - R;
            // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
            Float pr = R, pt = T;
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return null;

            if (uc < pr / (pr + pt))
            {
                // Sample perfect specular dielectric BRDF
                Vector3f wi = new(-wo.x, -wo.y, wo.z);
                SampledSpectrum fr = new(R / AbsCosTheta(wi));
                return new BSDFSample(fr, wi, pr / (pr + pt), BxDFFlags.SpecularReflection);
            }
            else
            {
                // Sample perfect specular dielectric BTDF
                // Compute ray direction for specular transmission
                bool valid = Refract(wo, new Normal3f(0, 0, 1), eta, out Float etap, out Vector3f wi);
                if (!valid)
                    return null;

                SampledSpectrum ft = new(T / AbsCosTheta(wi));
                // Account for non-symmetry with transmission to different medium
                if (mode == TransportMode.Radiance)
                    ft /= Sqr(etap);

                return new BSDFSample(ft, wi, pt / (pr + pt), BxDFFlags.SpecularTransmission, etap);
            }
        }
        else
        {
            // Sample rough dielectric BSDF
            Vector3f wm = mfDistrib.Sample_wm(wo, u);
            Float R = FrDielectric(Dot(wo, wm), eta);
            Float T = 1 - R;
            // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
            Float pr = R, pt = T;
            if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
                pr = 0;
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
                pt = 0;
            if (pr == 0 && pt == 0)
                return null;

            Float pdf;
            if (uc < pr / (pr + pt))
            {
                // Sample reflection at rough dielectric interface
                Vector3f wi = Reflect(wo, wm);
                if (!SameHemisphere(wo, wi))
                    return null;
                // Compute PDF of rough dielectric reflection
                pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);

                DCHECK(!IsNaN(pdf));
                SampledSpectrum f = new(mfDistrib.D(wm) * mfDistrib.G(wo, wi) * R / (4 * CosTheta(wi) * CosTheta(wo)));
                return new BSDFSample(f, wi, pdf, BxDFFlags.GlossyReflection);
            }
            else
            {
                // Sample transmission at rough dielectric interface
                bool tir = !Refract(wo, new Normal3f(wm), eta, out Float etap, out Vector3f wi);
                if (SameHemisphere(wo, wi) || wi.z == 0 || tir)
                    return null;
                // Compute PDF of rough dielectric transmission
                Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
                Float dwm_dwi = AbsDot(wi, wm) / denom;
                pdf = mfDistrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);

                CHECK(!IsNaN(pdf));
                // Evaluate BRDF and return _BSDFSample_ for rough transmission
                SampledSpectrum ft = new(T * mfDistrib.D(wm) * mfDistrib.G(wo, wi) *
                                         FMath.Abs(Dot(wi, wm) * Dot(wo, wm) / (CosTheta(wi) * CosTheta(wo) * denom)));
                // Account for non-symmetry with transmission to different medium
                if (mode == TransportMode.Radiance)
                    ft /= Sqr(etap);

                return new BSDFSample(ft, wi, pdf, BxDFFlags.GlossyTransmission, etap);
            }
        }
    }

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        if (eta == 1 || mfDistrib.EffectivelySmooth())
            return new SampledSpectrum(0f);
        // Evaluate rough dielectric BSDF
        // Compute generalized half vector _wm_
        Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
        bool reflect = cosTheta_i * cosTheta_o > 0;
        float etap = 1;
        if (!reflect)
            etap = (float)(cosTheta_o > 0 ? eta : (1 / eta));
        Vector3f wm = wi * etap + wo;
        if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
            return default;
        wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));

        // Discard backfacing microfacets
        if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
            return default;

        Float F = FrDielectric(Dot(wo, wm), eta);
        if (reflect)
        {
            // Compute reflection at rough dielectric interface
            return new SampledSpectrum(mfDistrib.D(wm) * mfDistrib.G(wo, wi) * F /
                                       FMath.Abs(4 * cosTheta_i * cosTheta_o));
        }
        else
        {
            // Compute transmission at rough dielectric interface
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap) * cosTheta_i * cosTheta_o;
            Float ft = mfDistrib.D(wm) * (1 - F) * mfDistrib.G(wo, wi) * FMath.Abs(Dot(wi, wm) * Dot(wo, wm) / denom);
            // Account for non-symmetry with transmission to different medium
            if (mode == TransportMode.Radiance)
                ft /= Sqr(etap);

            return new SampledSpectrum(ft);
        }
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if (eta == 1 || mfDistrib.EffectivelySmooth())
            return 0;
        // Evaluate sampling PDF of rough dielectric BSDF
        // Compute generalized half vector _wm_
        Float cosTheta_o = CosTheta(wo), cosTheta_i = CosTheta(wi);
        bool reflect = cosTheta_i * cosTheta_o > 0;
        float etap = 1;
        if (!reflect)
            etap = (float)(cosTheta_o > 0 ? eta : (1 / eta));
        Vector3f wm = wi * etap + wo;
        if (cosTheta_i == 0 || cosTheta_o == 0 || LengthSquared(wm) == 0)
            return 0;
        wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));

        // Discard backfacing microfacets
        if (Dot(wm, wi) * cosTheta_i < 0 || Dot(wm, wo) * cosTheta_o < 0)
            return 0;

        // Determine Fresnel reflectance of rough dielectric boundary
        Float R = FrDielectric(Dot(wo, wm), eta);
        Float T = 1 - R;

        // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
        Float pr = R, pt = T;
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            pr = 0;
        if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            pt = 0;
        if (pr == 0 && pt == 0)
            return 0;

        // Return PDF for rough dielectric
        Float pdf;
        if (reflect)
        {
            // Compute PDF of rough dielectric reflection
            pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm)) * pr / (pr + pt);
        }
        else
        {
            // Compute PDF of rough dielectric transmission
            Float denom = Sqr(Dot(wi, wm) + Dot(wo, wm) / etap);
            Float dwm_dwi = AbsDot(wi, wm) / denom;
            pdf = mfDistrib.PDF(wo, wm) * dwm_dwi * pt / (pr + pt);
        }
        return pdf;
    }

    public static string Name() => "DielectricBxDF";

    public override string ToString() => $"[ DielectricBxDF eta: {FloatToString(eta)} mfDistrib: {mfDistrib} ]";

    public override void Regularize() => mfDistrib.Regularize();
}

// ThinDielectricBxDF Definition
public sealed class ThinDielectricBxDF : BxDF
{
    private readonly Float eta;

    // ThinDielectricBxDF Public Methods
    public ThinDielectricBxDF(Float eta) => this.eta = eta;

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode) => new(0f);

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        Float R = FrDielectric(AbsCosTheta(wo), eta), T = 1 - R;
        // Compute _R_ and _T_ accounting for scattering between interfaces
        if (R < 1)
        {
            R += Sqr(T) * R / (1 - Sqr(R));
            T = 1 - R;
        }

        // Compute probabilities _pr_ and _pt_ for sampling reflection and transmission
        Float pr = R, pt = T;
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            pr = 0;
        if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0)
            pt = 0;
        if (pr == 0 && pt == 0)
            return null;

        if (uc < pr / (pr + pt))
        {
            // Sample perfect specular dielectric BRDF
            Vector3f wi = new(-wo.x, -wo.y, wo.z);
            SampledSpectrum fr = new(R / AbsCosTheta(wi));
            return new BSDFSample(fr, wi, pr / (pr + pt), BxDFFlags.SpecularReflection);
        }
        else
        {
            // Sample perfect specular transmission at thin dielectric interface
            Vector3f wi = -wo;
            SampledSpectrum ft = new(T / AbsCosTheta(wi));
            return new BSDFSample(ft, wi, pt / (pr + pt), BxDFFlags.SpecularTransmission);
        }
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All) => 0;

    public static string Name() => "ThinDielectricBxDF";

    public override string ToString() => $"[ ThinDielectricBxDF eta: {FloatToString(eta)} ]";

    public override void Regularize() { /* TODO */ }

    public override BxDFFlags Flags() => BxDFFlags.Reflection | BxDFFlags.Transmission | BxDFFlags.Specular;
}

// ConductorBxDF Definition
public sealed class ConductorBxDF : BxDF
{
    // ConductorBxDF Private Members
    private TrowbridgeReitzDistribution mfDistrib;
    private readonly SampledSpectrum eta, k;

    // ConductorBxDF Public Methods
    public ConductorBxDF(in TrowbridgeReitzDistribution mfDistrib, in SampledSpectrum eta, in SampledSpectrum k)
    {
        this.mfDistrib = mfDistrib;
        this.eta = eta;
        this.k = k;
    }

    public override BxDFFlags Flags() =>
        mfDistrib.EffectivelySmooth() ? BxDFFlags.SpecularReflection : BxDFFlags.GlossyReflection;

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return null;
        if (mfDistrib.EffectivelySmooth())
        {
            // Sample perfect specular conductor BRDF
            Vector3f wis = new(-wo.x, -wo.y, wo.z);
            SampledSpectrum fs = FrComplex(AbsCosTheta(wis), eta, k) / AbsCosTheta(wis);
            return new BSDFSample(fs, wis, 1, BxDFFlags.SpecularReflection);
        }
        // Sample rough conductor BRDF
        // Sample microfacet normal $\wm$ and reflected direction $\wi$
        if (wo.z == 0)
            return null;
        Vector3f wm = mfDistrib.Sample_wm(wo, u);
        Vector3f wi = Reflect(wo, wm);
        if (!SameHemisphere(wo, wi))
            return null;

        // Compute PDF of _wi_ for microfacet reflection
        Float pdf = mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));

        Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
        if (cosTheta_i == 0 || cosTheta_o == 0)
            return null;
        // Evaluate Fresnel factor _F_ for conductor BRDF
        SampledSpectrum F = FrComplex(AbsDot(wo, wm), eta, k);

        SampledSpectrum f = mfDistrib.D(wm) * F * mfDistrib.G(wo, wi) / (4 * cosTheta_i * cosTheta_o);
        return new BSDFSample(f, wi, pdf, BxDFFlags.GlossyReflection);
    }

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        if (!SameHemisphere(wo, wi))
            return default;
        if (mfDistrib.EffectivelySmooth())
            return default;
        // Evaluate rough conductor BRDF
        // Compute cosines and $\wm$ for conductor BRDF
        Float cosTheta_o = AbsCosTheta(wo), cosTheta_i = AbsCosTheta(wi);
        if (cosTheta_i == 0 || cosTheta_o == 0)
            return default;
        Vector3f wm = wi + wo;
        if (LengthSquared(wm) == 0)
            return default;
        wm = Normalize(wm);

        // Evaluate Fresnel factor _F_ for conductor BRDF
        SampledSpectrum F = FrComplex(AbsDot(wo, wm), eta, k);

        return mfDistrib.D(wm) * F * mfDistrib.G(wo, wi) / (4 * cosTheta_i * cosTheta_o);
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return 0;
        if (!SameHemisphere(wo, wi))
            return 0;
        if (mfDistrib.EffectivelySmooth())
            return 0;
        // Evaluate sampling PDF of rough conductor BRDF
        Vector3f wm = wo + wi;
        if (LengthSquared(wm) == 0)
            return 0;
        wm = FaceForward(Normalize(wm), new Normal3f(0, 0, 1));
        return mfDistrib.PDF(wo, wm) / (4 * AbsDot(wo, wm));
    }

    public static string Name() => "ConductorBxDF";

    public override string ToString() => $"[ ConductorBxDF mfDistrib: {mfDistrib} eta: {eta} k: {k} ]";

    public override void Regularize() => mfDistrib.Regularize();
}

// LayeredBxDF Definition
public abstract class LayeredBxDF<TopBxDF, BottomBxDF> : BxDF where TopBxDF : BxDF where BottomBxDF : BxDF
{
    // (pbrt's twoSided template parameter)
    private readonly bool twoSided;

    // LayeredBxDF Private Members
    private readonly TopBxDF top;
    private readonly BottomBxDF bottom;
    private readonly Float thickness, g;
    private readonly SampledSpectrum albedo;
    private readonly int maxDepth, nSamples;

    // LayeredBxDF Public Methods
    protected LayeredBxDF(TopBxDF top, BottomBxDF bottom, Float thickness, in SampledSpectrum albedo, Float g,
                          int maxDepth, int nSamples, bool twoSided)
    {
        this.top = top;
        this.bottom = bottom;
        this.thickness = FMath.Max(thickness, FloatMin);
        this.g = g;
        this.albedo = albedo;
        this.maxDepth = maxDepth;
        this.nSamples = nSamples;
        this.twoSided = twoSided;
    }

    // std::numeric_limits<Float>::min()
    private const Float FloatMin = 1.17549435e-38f;

    public override string ToString() =>
        $"[ LayeredBxDF top: {top} bottom: {bottom} thickness: {FloatToString(thickness)} albedo: {albedo} " +
        $"g: {FloatToString(g)} ]";

    public override void Regularize()
    {
        top.Regularize();
        bottom.Regularize();
    }

    public override BxDFFlags Flags()
    {
        BxDFFlags topFlags = top.Flags(), bottomFlags = bottom.Flags();
        CHECK(IsTransmissive(topFlags) || IsTransmissive(bottomFlags));  // otherwise, why bother?

        BxDFFlags flags = BxDFFlags.Reflection;
        if (IsSpecular(topFlags))
            flags = flags | BxDFFlags.Specular;

        if (IsDiffuse(topFlags) || IsDiffuse(bottomFlags) || albedo.IsNonZero())
            flags = flags | BxDFFlags.Diffuse;
        else if (IsGlossy(topFlags) || IsGlossy(bottomFlags))
            flags = flags | BxDFFlags.Glossy;

        if (IsTransmissive(topFlags) && IsTransmissive(bottomFlags))
            flags = flags | BxDFFlags.Transmission;

        return flags;
    }

    private static Float R(ref RNG rng) => FMath.Min(rng.Uniform<Float>(), OneMinusEpsilon);

    // Point2f(r(), r()): GCC evaluates the constructor arguments right to left.
    private static Point2f R2Paren(ref RNG rng)
    {
        Float y = R(ref rng);
        Float x = R(ref rng);
        return new Point2f(x, y);
    }

    // Point2f{r(), r()}: left to right.
    private static Point2f R2Brace(ref RNG rng)
    {
        Float x = R(ref rng);
        Float y = R(ref rng);
        return new Point2f(x, y);
    }

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        SampledSpectrum f = new(0f);
        // Estimate _LayeredBxDF_ value _f_ using random sampling
        // Set _wo_ and _wi_ for layered BSDF evaluation
        if (twoSided && wo.z < 0)
        {
            wo = -wo;
            wi = -wi;
        }

        // Determine entrance interface for layered BSDF
        bool enteredTop = twoSided || wo.z > 0;
        BxDF enterInterface = enteredTop ? top : bottom;

        // Determine exit interface and exit $z$ for layered BSDF
        BxDF exitInterface, nonExitInterface;
        if (SameHemisphere(wo, wi) ^ enteredTop)
        {
            exitInterface = bottom;
            nonExitInterface = top;
        }
        else
        {
            exitInterface = top;
            nonExitInterface = bottom;
        }
        Float exitZ = (SameHemisphere(wo, wi) ^ enteredTop) ? 0 : thickness;

        // Account for reflection at the entrance interface
        if (SameHemisphere(wo, wi))
            f = nSamples * enterInterface.f(wo, wi, mode);

        // Declare _RNG_ for layered BSDF evaluation
        RNG rng = new(Hash(GetOptions().seed, wo), Hash(wi));

        for (int s = 0; s < nSamples; ++s)
        {
            // Sample random walk through layers to estimate BSDF value
            // Sample transmission direction through entrance interface
            Float uc = R(ref rng);
            BSDFSample? owos = enterInterface.Sample_f(wo, uc, R2Paren(ref rng), mode, BxDFReflTransFlags.Transmission);
            if (owos is not { } wos || !wos.f || wos.pdf == 0 || wos.wi.z == 0)
                continue;

            // Sample BSDF for virtual light from _wi_
            uc = R(ref rng);
            BSDFSample? owis = exitInterface.Sample_f(wi, uc, R2Paren(ref rng), Flip(mode), BxDFReflTransFlags.Transmission);
            if (owis is not { } wis || !wis.f || wis.pdf == 0 || wis.wi.z == 0)
                continue;

            // Declare state for random walk through BSDF layers
            SampledSpectrum beta = wos.f * AbsCosTheta(wos.wi) / wos.pdf;
            Float z = enteredTop ? thickness : 0;
            Vector3f w = wos.wi;
            HGPhaseFunction phase = new(g);

            for (int depth = 0; depth < maxDepth; ++depth)
            {
                // Sample next event for layered BSDF evaluation random walk
                // Possibly terminate layered BSDF random walk with Russian roulette
                if (depth > 3 && beta.MaxComponentValue() < 0.25f)
                {
                    Float q = FMath.Max(0, 1 - beta.MaxComponentValue());
                    if (R(ref rng) < q)
                        break;
                    beta /= 1 - q;
                }

                // Account for media between layers and possibly scatter
                if (!albedo)
                {
                    // Advance to next layer boundary and update _beta_ for transmittance
                    z = (z == thickness) ? 0 : thickness;
                    beta *= Tr(thickness, w);
                }
                else
                {
                    // Sample medium scattering for layered BSDF evaluation
                    Float sigma_t = 1;
                    Float dz = SampleExponential(R(ref rng), sigma_t / FMath.Abs(w.z));
                    Float zp = w.z > 0 ? (z + dz) : (z - dz);
                    if (z == zp)
                        continue;
                    if (0 < zp && zp < thickness)
                    {
                        // Handle scattering event in layered BSDF medium
                        // Account for scattering through _exitInterface_ using _wis_
                        Float wt = 1;
                        if (!IsSpecular(exitInterface.Flags()))
                            wt = PowerHeuristic(1, wis.pdf, 1, phase.PDF(-w, -wis.wi));
                        f += beta * albedo * phase.p(-w, -wis.wi) * wt * Tr(zp - exitZ, wis.wi) * wis.f / wis.pdf;

                        // Sample phase function and update layered path state
                        Point2f u = R2Brace(ref rng);
                        PhaseFunctionSample? ops = phase.Sample_p(-w, u);
                        if (ops is not { } ps || ps.pdf == 0 || ps.wi.z == 0)
                            continue;
                        beta *= albedo * ps.p / ps.pdf;
                        w = ps.wi;
                        z = zp;

                        // Possibly account for scattering through _exitInterface_
                        if (((z < exitZ && w.z > 0) || (z > exitZ && w.z < 0)) && !IsSpecular(exitInterface.Flags()))
                        {
                            // Account for scattering through _exitInterface_
                            SampledSpectrum fExit = exitInterface.f(-w, wi, mode);
                            if (fExit)
                            {
                                Float exitPDF = exitInterface.PDF(-w, wi, mode, BxDFReflTransFlags.Transmission);
                                Float wt2 = PowerHeuristic(1, ps.pdf, 1, exitPDF);
                                f += beta * Tr(zp - exitZ, ps.wi) * fExit * wt2;
                            }
                        }

                        continue;
                    }
                    z = Clamp(zp, 0, thickness);
                }

                // Account for scattering at appropriate interface
                if (z == exitZ)
                {
                    // Account for reflection at _exitInterface_
                    Float uc2 = R(ref rng);
                    BSDFSample? obs = exitInterface.Sample_f(-w, uc2, R2Paren(ref rng), mode,
                                                             BxDFReflTransFlags.Reflection);
                    if (obs is not { } bs || !bs.f || bs.pdf == 0 || bs.wi.z == 0)
                        break;
                    beta *= bs.f * AbsCosTheta(bs.wi) / bs.pdf;
                    w = bs.wi;
                }
                else
                {
                    // Account for scattering at _nonExitInterface_
                    if (!IsSpecular(nonExitInterface.Flags()))
                    {
                        // Add NEE contribution along presampled _wis_ direction
                        Float wt = 1;
                        if (!IsSpecular(exitInterface.Flags()))
                            wt = PowerHeuristic(1, wis.pdf, 1, nonExitInterface.PDF(-w, -wis.wi, mode));
                        f += beta * nonExitInterface.f(-w, -wis.wi, mode) * AbsCosTheta(wis.wi) * wt *
                             Tr(thickness, wis.wi) * wis.f / wis.pdf;
                    }
                    // Sample new direction using BSDF at _nonExitInterface_
                    Float uc2 = R(ref rng);
                    Point2f u = R2Paren(ref rng);
                    BSDFSample? obs = nonExitInterface.Sample_f(-w, uc2, u, mode, BxDFReflTransFlags.Reflection);
                    if (obs is not { } bs || !bs.f || bs.pdf == 0 || bs.wi.z == 0)
                        break;
                    beta *= bs.f * AbsCosTheta(bs.wi) / bs.pdf;
                    w = bs.wi;

                    if (!IsSpecular(exitInterface.Flags()))
                    {
                        // Add NEE contribution along direction from BSDF sample
                        SampledSpectrum fExit = exitInterface.f(-w, wi, mode);
                        if (fExit)
                        {
                            Float wt = 1;
                            if (!IsSpecular(nonExitInterface.Flags()))
                            {
                                Float exitPDF = exitInterface.PDF(-w, wi, mode, BxDFReflTransFlags.Transmission);
                                wt = PowerHeuristic(1, bs.pdf, 1, exitPDF);
                            }
                            f += beta * Tr(thickness, bs.wi) * fExit * wt;
                        }
                    }
                }
            }
        }

        return f / nSamples;
    }

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        CHECK(sampleFlags == BxDFReflTransFlags.All);  // for now
        // Set _wo_ for layered BSDF sampling
        bool flipWi = false;
        if (twoSided && wo.z < 0)
        {
            wo = -wo;
            flipWi = true;
        }

        // Sample BSDF at entrance interface to get initial direction _w_
        bool enteredTop = twoSided || wo.z > 0;
        BSDFSample? obs0 = enteredTop ? top.Sample_f(wo, uc, u, mode) : bottom.Sample_f(wo, uc, u, mode);
        if (obs0 is not { } bs0 || !bs0.f || bs0.pdf == 0 || bs0.wi.z == 0)
            return null;
        if (bs0.IsReflection())
        {
            if (flipWi)
                bs0.wi = -bs0.wi;
            bs0.pdfIsProportional = true;
            return bs0;
        }
        Vector3f w = bs0.wi;
        bool specularPath = bs0.IsSpecular();

        // Declare _RNG_ for layered BSDF sampling
        RNG rng = new(Hash(GetOptions().seed, wo), Hash(uc, u));

        // Declare common variables for layered BSDF sampling
        SampledSpectrum f = bs0.f * AbsCosTheta(bs0.wi);
        Float pdf = bs0.pdf;
        Float z = enteredTop ? thickness : 0;
        HGPhaseFunction phase = new(g);

        for (int depth = 0; depth < maxDepth; ++depth)
        {
            // Follow random walk through layers to sample layered BSDF
            // Possibly terminate layered BSDF sampling with Russian Roulette
            Float rrBeta = f.MaxComponentValue() / pdf;
            if (depth > 3 && rrBeta < 0.25f)
            {
                Float q = FMath.Max(0, 1 - rrBeta);
                if (R(ref rng) < q)
                    return null;
                pdf *= 1 - q;
            }
            if (w.z == 0)
                return null;

            if (albedo)
            {
                // Sample potential scattering event in layered medium
                Float sigma_t = 1;
                Float dz = SampleExponential(R(ref rng), sigma_t / AbsCosTheta(w));
                Float zp = w.z > 0 ? (z + dz) : (z - dz);
                if (zp == z)
                    return null;
                if (0 < zp && zp < thickness)
                {
                    // Update path state for valid scattering event between interfaces
                    PhaseFunctionSample? ops = phase.Sample_p(-w, R2Paren(ref rng));
                    if (ops is not { } ps || ps.pdf == 0 || ps.wi.z == 0)
                        return null;
                    f *= albedo * ps.p;
                    pdf *= ps.pdf;
                    specularPath = false;
                    w = ps.wi;
                    z = zp;

                    continue;
                }
                z = Clamp(zp, 0, thickness);
            }
            else
            {
                // Advance to the other layer interface
                z = (z == thickness) ? 0 : thickness;
                f *= Tr(thickness, w);
            }
            // Initialize _interface_ for current interface surface
            BxDF @interface = (z == 0) ? bottom : top;

            // Sample interface BSDF to determine new path direction
            Float uc2 = R(ref rng);
            Point2f u2 = R2Paren(ref rng);
            BSDFSample? obs = @interface.Sample_f(-w, uc2, u2, mode);
            if (obs is not { } bs || !bs.f || bs.pdf == 0 || bs.wi.z == 0)
                return null;
            f *= bs.f;
            pdf *= bs.pdf;
            specularPath &= bs.IsSpecular();
            w = bs.wi;

            // Return _BSDFSample_ if path has left the layers
            if (bs.IsTransmission())
            {
                BxDFFlags flags = SameHemisphere(wo, w) ? BxDFFlags.Reflection : BxDFFlags.Transmission;
                flags |= specularPath ? BxDFFlags.Specular : BxDFFlags.Glossy;
                if (flipWi)
                    w = -w;
                return new BSDFSample(f, w, pdf, flags, 1f, true);
            }

            // Scale _f_ by cosine term after scattering at the interface
            f *= AbsCosTheta(bs.wi);
        }
        return null;
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        CHECK(sampleFlags == BxDFReflTransFlags.All);  // for now
        // Set _wo_ and _wi_ for layered BSDF evaluation
        if (twoSided && wo.z < 0)
        {
            wo = -wo;
            wi = -wi;
        }

        // Declare _RNG_ for layered PDF evaluation
        RNG rng = new(Hash(GetOptions().seed, wi), Hash(wo));

        // Update _pdfSum_ for reflection at the entrance layer
        bool enteredTop = twoSided || wo.z > 0;
        Float pdfSum = 0;
        if (SameHemisphere(wo, wi))
        {
            BxDFReflTransFlags reflFlag = BxDFReflTransFlags.Reflection;
            pdfSum += enteredTop ? nSamples * top.PDF(wo, wi, mode, reflFlag)
                                 : nSamples * bottom.PDF(wo, wi, mode, reflFlag);
        }

        for (int s = 0; s < nSamples; ++s)
        {
            // Evaluate layered BSDF PDF sample
            if (SameHemisphere(wo, wi))
            {
                // Evaluate TRT term for PDF estimate
                BxDF rInterface, tInterface;
                if (enteredTop)
                {
                    rInterface = bottom;
                    tInterface = top;
                }
                else
                {
                    rInterface = top;
                    tInterface = bottom;
                }
                // Sample _tInterface_ to get direction into the layers
                BxDFReflTransFlags trans = BxDFReflTransFlags.Transmission;
                // (Sample_f(wo, r(), {r(), r()}, ...): GCC evaluates the
                // braced Point2f before _uc_.)
                Point2f uo = R2Brace(ref rng);
                Float uco = R(ref rng);
                BSDFSample? wos = tInterface.Sample_f(wo, uco, uo, mode, trans);
                Point2f ui = R2Brace(ref rng);
                Float uci = R(ref rng);
                BSDFSample? wis = tInterface.Sample_f(wi, uci, ui, Flip(mode), trans);

                // Update _pdfSum_ accounting for TRT scattering events
                if (wos is { } wosv && wosv.f.IsNonZero() && wosv.pdf > 0 && wis is { } wisv && wisv.f.IsNonZero() && wisv.pdf > 0)
                {
                    if (!IsNonSpecular(tInterface.Flags()))
                        pdfSum += rInterface.PDF(-wosv.wi, -wisv.wi, mode);
                    else
                    {
                        // Use multiple importance sampling to estimate PDF product
                        Point2f ur = R2Brace(ref rng);
                        Float ucr = R(ref rng);
                        BSDFSample? ors = rInterface.Sample_f(-wosv.wi, ucr, ur, mode);
                        if (ors is { } rs && rs.f.IsNonZero() && rs.pdf > 0)
                        {
                            if (!IsNonSpecular(rInterface.Flags()))
                                pdfSum += tInterface.PDF(-rs.wi, wi, mode);
                            else
                            {
                                // Compute MIS-weighted estimate of Equation
                                // (\ref{eq:pdf-triple-canceled-one})
                                Float rPDF = rInterface.PDF(-wosv.wi, -wisv.wi, mode);
                                Float wt = PowerHeuristic(1, wisv.pdf, 1, rPDF);
                                pdfSum += wt * rPDF;

                                Float tPDF = tInterface.PDF(-rs.wi, wi, mode);
                                wt = PowerHeuristic(1, rs.pdf, 1, tPDF);
                                pdfSum += wt * tPDF;
                            }
                        }
                    }
                }
            }
            else
            {
                // Evaluate TT term for PDF estimate
                BxDF toInterface, tiInterface;
                if (enteredTop)
                {
                    toInterface = top;
                    tiInterface = bottom;
                }
                else
                {
                    toInterface = bottom;
                    tiInterface = top;
                }

                Float uc = R(ref rng);
                Point2f u = R2Paren(ref rng);
                BSDFSample? owos = toInterface.Sample_f(wo, uc, u, mode);
                if (owos is not { } wos || !wos.f || wos.pdf == 0 || wos.wi.z == 0 || wos.IsReflection())
                    continue;

                uc = R(ref rng);
                u = R2Paren(ref rng);
                BSDFSample? owis = tiInterface.Sample_f(wi, uc, u, Flip(mode));
                if (owis is not { } wis || !wis.f || wis.pdf == 0 || wis.wi.z == 0 || wis.IsReflection())
                    continue;

                if (IsSpecular(toInterface.Flags()))
                    pdfSum += tiInterface.PDF(-wos.wi, wi, mode);
                else if (IsSpecular(tiInterface.Flags()))
                    pdfSum += toInterface.PDF(wo, -wis.wi, mode);
                else
                    pdfSum += (toInterface.PDF(wo, -wis.wi, mode) + tiInterface.PDF(-wos.wi, wi, mode)) / 2;
            }
        }
        // Return mixture of PDF estimate and constant PDF
        return Lerp(0.9f, 1 / (4 * Pi), pdfSum / nSamples);
    }

    // LayeredBxDF Private Methods
    private static Float Tr(Float dz, Vector3f w)
    {
        if (FMath.Abs(dz) <= FloatMin)
            return 1;
        return FastExp((float)-FMath.Abs(dz / w.z));
    }
}

// CoatedDiffuseBxDF Definition
public sealed class CoatedDiffuseBxDF : LayeredBxDF<DielectricBxDF, DiffuseBxDF>
{
    // CoatedDiffuseBxDF Public Methods
    public CoatedDiffuseBxDF(DielectricBxDF top, DiffuseBxDF bottom, Float thickness, in SampledSpectrum albedo,
                             Float g, int maxDepth, int nSamples)
        : base(top, bottom, thickness, albedo, g, maxDepth, nSamples, true) { }

    public static string Name() => "CoatedDiffuseBxDF";
}

// CoatedConductorBxDF Definition
public sealed class CoatedConductorBxDF : LayeredBxDF<DielectricBxDF, ConductorBxDF>
{
    // CoatedConductorBxDF Public Methods
    public static string Name() => "CoatedConductorBxDF";

    public CoatedConductorBxDF(DielectricBxDF top, ConductorBxDF bottom, Float thickness, in SampledSpectrum albedo,
                               Float g, int maxDepth, int nSamples)
        : base(top, bottom, thickness, albedo, g, maxDepth, nSamples, true) { }
}

// HairBxDF Definition
public sealed class HairBxDF : BxDF
{
    // HairBxDF Constants
    private const int pMax = 3;

    // HairBxDF Private Members
    private readonly Float h, eta;
    private readonly SampledSpectrum sigma_a;
    private readonly Float beta_m, beta_n;
    private readonly Float[] v = new Float[pMax + 1];
    private readonly Float s;
    private readonly Float[] sin2kAlpha = new Float[pMax], cos2kAlpha = new Float[pMax];

    // HairBxDF Method Definitions
    public HairBxDF(Float h, Float eta, in SampledSpectrum sigma_a, Float beta_m, Float beta_n, Float alpha)
    {
        this.h = h;
        this.eta = eta;
        this.sigma_a = sigma_a;
        this.beta_m = beta_m;
        this.beta_n = beta_n;
        CHECK(h >= -1 && h <= 1);
        CHECK(beta_m >= 0 && beta_m <= 1);
        CHECK(beta_n >= 0 && beta_n <= 1);
        // _HairBxDF_ constructor implementation
        v[0] = Sqr(0.726f * beta_m + 0.812f * Sqr(beta_m) + 3.7f * Pow(beta_m, 20));
        v[1] = (Float)(.25 * v[0]);
        v[2] = 4 * v[0];
        for (int p = 3; p <= pMax; ++p)
            // TODO: is there anything better here?
            v[p] = v[2];

        const Float SqrtPiOver8 = 0.626657069f;
        s = SqrtPiOver8 * (0.265f * beta_n + 1.194f * Sqr(beta_n) + 5.372f * Pow(beta_n, 22));
        DCHECK(!IsNaN(s));

        sin2kAlpha[0] = FMath.Sin(Radians(alpha));
        cos2kAlpha[0] = SafeSqrt(1 - Sqr(sin2kAlpha[0]));
        for (int i = 1; i < pMax; ++i)
        {
            sin2kAlpha[i] = 2 * cos2kAlpha[i - 1] * sin2kAlpha[i - 1];
            cos2kAlpha[i] = Sqr(cos2kAlpha[i - 1]) - Sqr(sin2kAlpha[i - 1]);
        }
    }

    // HairBxDF Private Methods
    private static Float Mp(Float cosTheta_i, Float cosTheta_o, Float sinTheta_i, Float sinTheta_o, Float v)
    {
        Float a = cosTheta_i * cosTheta_o / v, b = sinTheta_i * sinTheta_o / v;
        Float mp = (v <= .1) ? (FastExp((float)(LogI0(a) - b - 1 / v + 0.6931f + FMath.Log(1 / (2 * v)))))
                             : (FastExp((float)-b) * I0(a)) / (FMath.Sinh(1 / v) * 2 * v);
        DCHECK(!IsInf(mp) && !IsNaN(mp));
        return mp;
    }

    private static SampledSpectrum[] Ap(Float cosTheta_o, Float eta, Float h, in SampledSpectrum T)
    {
        SampledSpectrum[] ap = new SampledSpectrum[pMax + 1];
        // Compute $p=0$ attenuation at initial cylinder intersection
        Float cosGamma_o = SafeSqrt(1 - Sqr(h));
        Float cosTheta = cosTheta_o * cosGamma_o;
        Float f = FrDielectric(cosTheta, eta);
        ap[0] = new SampledSpectrum(f);

        // Compute $p=1$ attenuation term
        ap[1] = Sqr(1 - f) * T;

        // Compute attenuation terms up to $p=_pMax_$
        for (int p = 2; p < pMax; ++p)
            ap[p] = ap[p - 1] * T * f;

        // Compute attenuation term accounting for remaining orders of scattering
        if (1 - T * f)
            ap[pMax] = ap[pMax - 1] * f * T / (1 - T * f);

        return ap;
    }

    private static Float Phi(int p, Float gamma_o, Float gamma_t) => 2 * p * gamma_t - 2 * gamma_o + p * Pi;

    private static Float Np(Float phi, int p, Float s, Float gamma_o, Float gamma_t)
    {
        Float dphi = phi - Phi(p, gamma_o, gamma_t);
        // Remap _dphi_ to $[-\pi,\pi]$
        while (dphi > Pi)
            dphi -= 2 * Pi;
        while (dphi < -Pi)
            dphi += 2 * Pi;

        return TrimmedLogistic(dphi, s, -Pi, Pi);
    }

    // Compute $\sin\,\thetao$ and $\cos\,\thetao$ terms accounting for scales
    private void ScaleTilt(int p, Float sinTheta_o, Float cosTheta_o, out Float sinThetap_o, out Float cosThetap_o)
    {
        if (p == 0)
        {
            sinThetap_o = sinTheta_o * cos2kAlpha[1] - cosTheta_o * sin2kAlpha[1];
            cosThetap_o = cosTheta_o * cos2kAlpha[1] + sinTheta_o * sin2kAlpha[1];
        }
        // Handle remainder of $p$ values for hair scale tilt
        else if (p == 1)
        {
            sinThetap_o = sinTheta_o * cos2kAlpha[0] + cosTheta_o * sin2kAlpha[0];
            cosThetap_o = cosTheta_o * cos2kAlpha[0] - sinTheta_o * sin2kAlpha[0];
        }
        else if (p == 2)
        {
            sinThetap_o = sinTheta_o * cos2kAlpha[2] + cosTheta_o * sin2kAlpha[2];
            cosThetap_o = cosTheta_o * cos2kAlpha[2] - sinTheta_o * sin2kAlpha[2];
        }
        else
        {
            sinThetap_o = sinTheta_o;
            cosThetap_o = cosTheta_o;
        }

        // Handle out-of-range $\cos\,\thetao$ from scale adjustment
        cosThetap_o = FMath.Abs(cosThetap_o);
    }

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        // Compute hair coordinate system terms related to _wo_
        Float sinTheta_o = wo.x;
        Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
        Float phi_o = FMath.Atan2(wo.z, wo.y);
        Float gamma_o = SafeASin(h);

        // Compute hair coordinate system terms related to _wi_
        Float sinTheta_i = wi.x;
        Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));
        Float phi_i = FMath.Atan2(wi.z, wi.y);

        // Compute $\cos\,\thetat$ for refracted ray
        Float sinTheta_t = sinTheta_o / eta;
        Float cosTheta_t = SafeSqrt(1 - Sqr(sinTheta_t));

        // Compute $\gammat$ for refracted ray
        Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
        Float sinGamma_t = h / etap;
        Float cosGamma_t = SafeSqrt(1 - Sqr(sinGamma_t));
        Float gamma_t = SafeASin(sinGamma_t);

        // Compute the transmittance _T_ of a single path through the cylinder
        SampledSpectrum T = Exp(-sigma_a * (2 * cosGamma_t / cosTheta_t));

        // Evaluate hair BSDF
        Float phi = phi_i - phi_o;
        SampledSpectrum[] ap = Ap(cosTheta_o, eta, h, T);
        SampledSpectrum fsum = new(0f);

        for (int p = 0; p < pMax; ++p)
        {
            ScaleTilt(p, sinTheta_o, cosTheta_o, out Float sinThetap_o, out Float cosThetap_o);
            fsum += Mp(cosTheta_i, cosThetap_o, sinTheta_i, sinThetap_o, v[p]) * ap[p] *
                    Np(phi, p, s, gamma_o, gamma_t);
        }
        // Compute contribution of remaining terms after _pMax_
        fsum += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * ap[pMax] / (2 * Pi);

        if (AbsCosTheta(wi) > 0)
            fsum /= AbsCosTheta(wi);
        DCHECK(!IsInf(fsum.Average()) && !IsNaN(fsum.Average()));
        return fsum;
    }

    private Float[] ApPDF(Float cosTheta_o)
    {
        // Initialize array of $A_p$ values for _cosTheta_o_
        Float sinTheta_o = SafeSqrt(1 - Sqr(cosTheta_o));
        // Compute $\cos\,\thetat$ for refracted ray
        Float sinTheta_t = sinTheta_o / eta;
        Float cosTheta_t = SafeSqrt(1 - Sqr(sinTheta_t));

        // Compute $\gammat$ for refracted ray
        Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
        Float sinGamma_t = h / etap;
        Float cosGamma_t = SafeSqrt(1 - Sqr(sinGamma_t));

        // Compute the transmittance _T_ of a single path through the cylinder
        SampledSpectrum T = Exp(-sigma_a * (2 * cosGamma_t / cosTheta_t));

        SampledSpectrum[] ap = Ap(cosTheta_o, eta, h, T);

        // Compute $A_p$ PDF from individual $A_p$ terms
        Float[] apPDF = new Float[pMax + 1];
        Float sumY = 0;
        foreach (SampledSpectrum @as in ap)
            sumY += @as.Average();
        for (int i = 0; i <= pMax; ++i)
            apPDF[i] = ap[i].Average() / sumY;

        return apPDF;
    }

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        // Compute hair coordinate system terms related to _wo_
        Float sinTheta_o = wo.x;
        Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
        Float phi_o = FMath.Atan2(wo.z, wo.y);
        Float gamma_o = SafeASin(h);

        // Determine which term $p$ to sample for hair scattering
        Float[] apPDF = ApPDF(cosTheta_o);
        int p = SampleDiscrete(apPDF, uc, out _, out uc);

        ScaleTilt(p, sinTheta_o, cosTheta_o, out Float sinThetap_o, out Float cosThetap_o);

        // Sample $M_p$ to compute $\thetai$
        Float cosTheta = 1 + v[p] * FMath.Log(FMath.Max(u[0], (Float)1e-5) + (1 - u[0]) * FastExp((float)(-2 / v[p])));
        Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
        Float cosPhi = FMath.Cos(2 * Pi * u[1]);
        Float sinTheta_i = -cosTheta * sinThetap_o + sinTheta * cosPhi * cosThetap_o;
        Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));

        // Sample $N_p$ to compute $\Delta\phi$
        // Compute $\gammat$ for refracted ray
        Float etap = SafeSqrt(Sqr(eta) - Sqr(sinTheta_o)) / cosTheta_o;
        Float sinGamma_t = h / etap;
        Float gamma_t = SafeASin(sinGamma_t);

        Float dphi;
        if (p < pMax)
            dphi = Phi(p, gamma_o, gamma_t) + SampleTrimmedLogistic(uc, s, -Pi, Pi);
        else
            dphi = 2 * Pi * uc;

        // Compute _wi_ from sampled hair scattering angles
        Float phi_i = phi_o + dphi;
        Vector3f wi = new(sinTheta_i, cosTheta_i * FMath.Cos(phi_i), cosTheta_i * FMath.Sin(phi_i));

        // Compute PDF for sampled hair scattering direction _wi_
        Float pdf = 0;
        for (int pp = 0; pp < pMax; ++pp)
        {
            ScaleTilt(pp, sinTheta_o, cosTheta_o, out Float sinThetapp_o, out Float cosThetapp_o);
            pdf += Mp(cosTheta_i, cosThetapp_o, sinTheta_i, sinThetapp_o, v[pp]) * apPDF[pp] *
                   Np(dphi, pp, s, gamma_o, gamma_t);
        }
        pdf += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * apPDF[pMax] * (1 / (2 * Pi));

        return new BSDFSample(f(wo, wi, mode), wi, pdf, Flags());
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        // TODO? flags...

        // Compute hair coordinate system terms related to _wo_
        Float sinTheta_o = wo.x;
        Float cosTheta_o = SafeSqrt(1 - Sqr(sinTheta_o));
        Float phi_o = FMath.Atan2(wo.z, wo.y);
        Float gamma_o = SafeASin(h);

        // Compute hair coordinate system terms related to _wi_
        Float sinTheta_i = wi.x;
        Float cosTheta_i = SafeSqrt(1 - Sqr(sinTheta_i));
        Float phi_i = FMath.Atan2(wi.z, wi.y);

        // Compute $\gammat$ for refracted ray
        Float etap = SafeSqrt(eta * eta - Sqr(sinTheta_o)) / cosTheta_o;
        Float sinGamma_t = h / etap;
        Float gamma_t = SafeASin(sinGamma_t);

        // Compute PDF for $A_p$ terms
        Float[] apPDF = ApPDF(cosTheta_o);

        // Compute PDF sum for hair scattering events
        Float phi = phi_i - phi_o;
        Float pdf = 0;
        for (int p = 0; p < pMax; ++p)
        {
            ScaleTilt(p, sinTheta_o, cosTheta_o, out Float sinThetap_o, out Float cosThetap_o);
            pdf += Mp(cosTheta_i, cosThetap_o, sinTheta_i, sinThetap_o, v[p]) * apPDF[p] *
                   Np(phi, p, s, gamma_o, gamma_t);
        }
        pdf += Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, v[pMax]) * apPDF[pMax] * (1 / (2 * Pi));
        return pdf;
    }

    public override void Regularize() { }

    public static string Name() => "HairBxDF";

    public override string ToString() =>
        $"[ HairBxDF h: {FloatToString(h)} eta: {FloatToString(eta)} beta_m: {FloatToString(beta_m)} " +
        $"beta_n: {FloatToString(beta_n)} v[0]: {FloatToString(v[0])} s: {FloatToString(s)} sigma_a: {sigma_a} ]";

    public override BxDFFlags Flags() => BxDFFlags.GlossyReflection;

    public static RGBUnboundedSpectrum SigmaAFromConcentration(Float ce, Float cp)
    {
        RGB eumelaninSigma_a = new(0.419f, 0.697f, 1.37f);
        RGB pheomelaninSigma_a = new(0.187f, 0.4f, 1.05f);
        RGB sigma_a = ce * eumelaninSigma_a + cp * pheomelaninSigma_a;
        return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, sigma_a);
    }

    public static SampledSpectrum SigmaAFromReflectance(in SampledSpectrum c, Float beta_n, in SampledWavelengths lambda)
    {
        SampledSpectrum sigma_a = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            sigma_a[i] = Sqr(FMath.Log(c[i]) / (5.969f - 0.215f * beta_n + 2.532f * Sqr(beta_n) -
                                                10.73f * Pow(beta_n, 3) + 5.574f * Pow(beta_n, 4) +
                                                0.245f * Pow(beta_n, 5)));
        return sigma_a;
    }
}

// Tensor file I/O
internal sealed class Tensor
{
    // Data type of the tensor's fields
    public enum Type
    {
        /* Invalid/unspecified */
        Invalid = 0,

        /* Signed and unsigned integer values */
        UInt8,
        Int8,
        UInt16,
        Int16,
        UInt32,
        Int32,
        UInt64,
        Int64,

        /* Floating point values */
        Float16,
        Float32,
        Float64,
    }

    public sealed class Field
    {
        // Data type of the tensor's fields
        public Type dtype;

        // Offset in the file
        public ulong offset;

        /// Specifies both rank and size along each dimension
        public ulong[] shape = [];

        /// The tensor's data
        public byte[] data = [];

        public float[] AsFloats()
        {
            float[] f = new float[data.Length / 4];
            Buffer.BlockCopy(data, 0, f, 0, f.Length * 4);
            return f;
        }
    }

    private readonly Dictionary<string, Field> m_fields = [];
    private readonly string m_filename;
    private readonly ulong m_size;

    private static string TypeName(Type value) => value switch
    {
        Type.Invalid => "invalid",
        Type.UInt8 => "uint8_t",
        Type.Int8 => "int8_t",
        Type.UInt16 => "uint16_t",
        Type.Int16 => "int16_t",
        Type.UInt32 => "uint32_t",
        Type.Int32 => "int8_t",
        Type.UInt64 => "uint64_t",
        Type.Int64 => "int64_t",
        Type.Float16 => "float16_t",
        Type.Float32 => "float32_t",
        Type.Float64 => "float64_t",
        _ => "unknown",
    };

    private static ulong TypeSize(Type value) => value switch
    {
        Type.UInt8 or Type.Int8 => 1,
        Type.UInt16 or Type.Int16 or Type.Float16 => 2,
        Type.UInt32 or Type.Int32 or Type.Float32 => 4,
        Type.UInt64 or Type.Int64 or Type.Float64 => 8,
        _ => 0,
    };

    /// Load a tensor file into memory
    public Tensor(string filename)
    {
        m_filename = filename;
        byte[] file;
        try
        {
            file = File.ReadAllBytes(filename);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorExit($"{filename}: unable to open file");
            throw;
        }

        void Assert(bool cond, string msg)
        {
            if (!cond)
                ErrorExit($"{filename}: Tensor: {msg}");
        }

        m_size = (ulong)file.Length;
        Assert(m_size >= 12 + 2 + 4, "Invalid tensor file: too small, truncated?");

        int pos = 0;
        ReadOnlySpan<byte> Read(int n, string what)
        {
            Assert(pos + n <= file.Length, $"Unable to read {what}.");
            ReadOnlySpan<byte> s = file.AsSpan(pos, n);
            pos += n;
            return s;
        }

        ReadOnlySpan<byte> header = Read(12, "header");
        ReadOnlySpan<byte> version = Read(2, "version");
        uint n_fields = BitConverter.ToUInt32(Read(4, "&n_fields"));

        Assert(header.SequenceEqual("tensor_file\0"u8), "Invalid tensor file: invalid header.");
        Assert(version[0] == 1 && version[1] == 0, "Invalid tensor file: unknown file version.");

        for (uint i = 0; i < n_fields; ++i)
        {
            ushort name_length = BitConverter.ToUInt16(Read(2, "&name_length"));
            string name = System.Text.Encoding.Latin1.GetString(Read(name_length, "(char *)name.data()"));
            ushort ndim = BitConverter.ToUInt16(Read(2, "&ndim"));
            byte dtype = Read(1, "&dtype")[0];
            ulong offset = BitConverter.ToUInt64(Read(8, "&offset"));
            Assert(dtype != (byte)Type.Invalid && dtype <= (byte)Type.Float64, "Invalid tensor file: unknown type.");

            ulong[] shape = new ulong[ndim];
            ulong total_size = TypeSize((Type)dtype);  // no need to check here, line 43
                                                       // already removes invalid types
            for (int j = 0; j < ndim; ++j)
            {
                ulong size_value = BitConverter.ToUInt64(Read(8, "&size_value"));
                shape[j] = size_value;
                total_size *= shape[j];
            }

            Assert(offset + total_size <= (ulong)file.Length, "Unable to read data.get().");
            byte[] data = file.AsSpan((int)offset, (int)total_size).ToArray();

            m_fields[name] = new Field { dtype = (Type)dtype, offset = offset, shape = shape, data = data };
        }
    }

    /// Does the file contain a field of the specified name?
    public bool HasField(string name) => m_fields.ContainsKey(name);

    /// Return a data structure with information about the specified field
    public Field GetField(string name)
    {
        CHECK(m_fields.ContainsKey(name));
        return m_fields[name];
    }

    /// Return the total size of the tensor's data
    public ulong Size() => m_size;

    public string Filename() => m_filename;

    /// Return a human-readable summary
    public override string ToString()
    {
        System.Text.StringBuilder oss = new();
        oss.Append("Tensor[\n")
           .Append($"  filename = \"{m_filename}\",\n")
           .Append($"  size = {Size()},\n")
           .Append("  fields = {\n");

        int ctr = 0;
        foreach (var (key, field) in m_fields)
        {
            oss.Append($"    \"{key}\" => [\n")
               .Append($"      dtype = {TypeName(field.dtype)},\n")
               .Append($"      offset = {field.offset},\n")
               .Append("      shape = [")
               .Append(string.Join(", ", field.shape))
               .Append("]\n")
               .Append("    ]");
            if (++ctr < m_fields.Count)
                oss.Append(',');
            oss.Append('\n');
        }

        oss.Append("  }\n]");
        return oss.ToString();
    }
}

// MeasuredBxDFData Definition
public sealed class MeasuredBxDFData
{
    private static readonly StatMemoryCounter measuredBRDFBytes = new("Memory/Measured BRDF data");

    // MeasuredBxDFData Public Members
    public float[] wavelengths = [];
    public PiecewiseLinear2D spectra = null!;
    public PiecewiseLinear2D ndf = null!;
    public PiecewiseLinear2D vndf = null!;
    public PiecewiseLinear2D sigma = null!;
    public bool isotropic;
    public PiecewiseLinear2D luminance = null!;
    public string filename = "";

    public override string ToString() => $"[ MeasuredBxDFData filename: {filename} ]";

    public static MeasuredBxDFData? Create(string filename)
    {
        Tensor tf = new(filename);
        Tensor.Field theta_i = tf.GetField("theta_i");
        Tensor.Field phi_i = tf.GetField("phi_i");
        Tensor.Field ndf = tf.GetField("ndf");
        Tensor.Field sigma = tf.GetField("sigma");
        Tensor.Field vndf = tf.GetField("vndf");
        Tensor.Field spectra = tf.GetField("spectra");
        Tensor.Field luminance = tf.GetField("luminance");
        Tensor.Field wavelengths = tf.GetField("wavelengths");
        Tensor.Field description = tf.GetField("description");
        Tensor.Field jacobian = tf.GetField("jacobian");

        if (!(description.shape.Length == 1 && description.dtype == Tensor.Type.UInt8 &&

              theta_i.shape.Length == 1 && theta_i.dtype == Tensor.Type.Float32 &&

              phi_i.shape.Length == 1 && phi_i.dtype == Tensor.Type.Float32 &&

              wavelengths.shape.Length == 1 && wavelengths.dtype == Tensor.Type.Float32 &&

              ndf.shape.Length == 2 && ndf.dtype == Tensor.Type.Float32 &&

              sigma.shape.Length == 2 && sigma.dtype == Tensor.Type.Float32 &&

              vndf.shape.Length == 4 && vndf.dtype == Tensor.Type.Float32 &&
              vndf.shape[0] == phi_i.shape[0] && vndf.shape[1] == theta_i.shape[0] &&

              luminance.shape.Length == 4 && luminance.dtype == Tensor.Type.Float32 &&
              luminance.shape[0] == phi_i.shape[0] && luminance.shape[1] == theta_i.shape[0] &&
              luminance.shape[2] == luminance.shape[3] &&

              spectra.dtype == Tensor.Type.Float32 && spectra.shape.Length == 5 &&
              spectra.shape[0] == phi_i.shape[0] && spectra.shape[1] == theta_i.shape[0] &&
              spectra.shape[2] == wavelengths.shape[0] && spectra.shape[3] == spectra.shape[4] &&

              luminance.shape[2] == spectra.shape[3] && luminance.shape[3] == spectra.shape[4] &&

              jacobian.shape.Length == 1 && jacobian.shape[0] == 1 && jacobian.dtype == Tensor.Type.UInt8))
        {
            Error($"{filename}: invalid BRDF file structure: {tf}");
            return null;
        }

        MeasuredBxDFData brdf = new() { filename = filename, isotropic = phi_i.shape[0] <= 2 };
        float[] phi_i_data = phi_i.AsFloats(), theta_i_data = theta_i.AsFloats();

        if (!brdf.isotropic)
        {
            int reduction = (int)FMath.Round((2 * Pi) / (phi_i_data[phi_i.shape[0] - 1] - phi_i_data[0]),
                                             MidpointRounding.ToEven);
            if (reduction != 1)
                ErrorExit($"{filename}: reduction {reduction} (!= 1) not supported");
        }

        /* Construct NDF interpolant data structure */
        brdf.ndf = new PiecewiseLinear2D(ndf.AsFloats(), (int)ndf.shape[1], (int)ndf.shape[0], default, null, false, false);

        /* Construct projected surface area interpolant data structure */
        brdf.sigma = new PiecewiseLinear2D(sigma.AsFloats(), (int)sigma.shape[1], (int)sigma.shape[0], default, null,
                                           false, false);

        /* Construct VNDF warp data structure */
        brdf.vndf = new PiecewiseLinear2D(vndf.AsFloats(), (int)vndf.shape[3], (int)vndf.shape[2],
                                          [(int)phi_i.shape[0], (int)theta_i.shape[0]], [phi_i_data, theta_i_data]);

        /* Construct Luminance warp data structure */
        brdf.luminance = new PiecewiseLinear2D(luminance.AsFloats(), (int)luminance.shape[3], (int)luminance.shape[2],
                                               [(int)phi_i.shape[0], (int)theta_i.shape[0]], [phi_i_data, theta_i_data]);

        /* Copy wavelength information */
        brdf.wavelengths = wavelengths.AsFloats();

        /* Construct spectral interpolant */
        brdf.spectra = new PiecewiseLinear2D(spectra.AsFloats(), (int)spectra.shape[4], (int)spectra.shape[3],
                                             [(int)phi_i.shape[0], (int)theta_i.shape[0], (int)wavelengths.shape[0]],
                                             [phi_i_data, theta_i_data, brdf.wavelengths], false, false);

        // (sizeof(MeasuredBxDFData) is 888 in pbrt)
        measuredBRDFBytes.Add(888 + 4L * brdf.wavelengths.Length + brdf.ndf.BytesUsed() +
                              brdf.sigma.BytesUsed() + brdf.vndf.BytesUsed() + brdf.luminance.BytesUsed() +
                              brdf.spectra.BytesUsed());

        return brdf;
    }
}

// MeasuredBxDF Definition
public sealed class MeasuredBxDF : BxDF
{
    // MeasuredBxDF Private Members
    private readonly MeasuredBxDFData brdf;
    private readonly SampledWavelengths lambda;

    private static readonly Dictionary<string, MeasuredBxDFData?> loadedData = [];

    // MeasuredBxDF Public Methods
    public MeasuredBxDF(MeasuredBxDFData brdf, in SampledWavelengths lambda)
    {
        this.brdf = brdf;
        this.lambda = lambda;
    }

    public static MeasuredBxDFData? BRDFDataFromFile(string filename)
    {
        lock (loadedData)
        {
            if (!loadedData.ContainsKey(filename))
                loadedData[filename] = MeasuredBxDFData.Create(filename);
            return loadedData[filename];
        }
    }

    // MeasuredBxDF Private Methods
    private static Float theta2u(Float theta) => FMath.Sqrt(theta * (2 / Pi));
    private static Float phi2u(Float phi) => phi * (1 / (2 * Pi)) + .5f;
    private static Float u2theta(Float u) => Sqr(u) * (Pi / 2f);
    private static Float u2phi(Float u) => (2f * u - 1f) * Pi;

    // MeasuredBxDF Method Definitions
    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        // Check for valid reflection configurations
        if (!SameHemisphere(wo, wi))
            return new SampledSpectrum(0f);
        if (wo.z < 0)
        {
            wo = -wo;
            wi = -wi;
        }

        // Determine half-direction vector $\wm$
        Vector3f wm = wi + wo;
        if (LengthSquared(wm) == 0)
            return new SampledSpectrum(0f);
        wm = Normalize(wm);

        // Map $\wo$ and $\wm$ to the unit square $[0,\,1]^2$
        Float theta_o = SphericalTheta(wo), phi_o = FMath.Atan2(wo.y, wo.x);
        Float theta_m = SphericalTheta(wm), phi_m = FMath.Atan2(wm.y, wm.x);
        Point2f u_wo = new(theta2u(theta_o), phi2u(phi_o));
        Point2f u_wm = new(theta2u(theta_m), phi2u(brdf.isotropic ? (phi_m - phi_o) : phi_m));
        u_wm[1] = u_wm[1] - FMath.Floor(u_wm[1]);

        // Evaluate inverse parameterization $R^{-1}$
        PLSample ui = brdf.vndf.Invert(u_wm, phi_o, theta_o);

        // Evaluate spectral 5D interpolant
        SampledSpectrum fr = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            fr[i] = Max<Float>(0, brdf.spectra.Evaluate(ui.p, phi_o, theta_o, lambda[i]));

        // Return measured BRDF value
        return fr * brdf.ndf.Evaluate(u_wm) / (4 * brdf.sigma.Evaluate(u_wo) * CosTheta(wi));
    }

    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        // Check flags and detect interactions in lower hemisphere
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return null;
        bool flipWi = false;
        if (wo.z <= 0)
        {
            wo = -wo;
            flipWi = true;
        }

        // Initialize parameters of conditional distribution
        Float theta_o = SphericalTheta(wo), phi_o = FMath.Atan2(wo.y, wo.x);

        // Warp sample using luminance distribution
        PLSample s = brdf.luminance.Sample(u, phi_o, theta_o);
        u = s.p;
        Float lum_pdf = s.pdf;

        // Sample visible normal distribution of measured BRDF
        s = brdf.vndf.Sample(u, phi_o, theta_o);
        Point2f u_wm = s.p;
        Float pdf = s.pdf;

        // Map from microfacet normal to incident direction
        Float phi_m = u2phi(u_wm.y), theta_m = u2theta(u_wm.x);
        if (brdf.isotropic)
            phi_m += phi_o;
        Float sinTheta_m = FMath.Sin(theta_m), cosTheta_m = FMath.Cos(theta_m);
        Vector3f wm = SphericalDirection(sinTheta_m, cosTheta_m, phi_m);
        Vector3f wi = Reflect(wo, wm);
        if (wi.z <= 0)
            return null;

        // Interpolate spectral BRDF
        SampledSpectrum fr = new(0f);
        for (int i = 0; i < NSpectrumSamples; ++i)
            fr[i] = Max<Float>(0, brdf.spectra.Evaluate(u, phi_o, theta_o, lambda[i]));

        Point2f u_wo = new(theta2u(theta_o), phi2u(phi_o));
        fr *= brdf.ndf.Evaluate(u_wm) / (4 * brdf.sigma.Evaluate(u_wo) * AbsCosTheta(wi));
        pdf /= 4 * Dot(wo, wm) * FMath.Max(2 * Sqr(Pi) * u_wm.x * sinTheta_m, 1e-6f);

        // Handle interactions in lower hemisphere
        if (flipWi)
            wi = -wi;

        return new BSDFSample(fr, wi, pdf * lum_pdf, BxDFFlags.GlossyReflection);
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return 0;
        if (!SameHemisphere(wo, wi))
            return 0;
        if (wo.z < 0)
        {
            wo = -wo;
            wi = -wi;
        }

        Vector3f wm = wi + wo;
        if (LengthSquared(wm) == 0)
            return 0;
        wm = Normalize(wm);

        /* Cartesian -> spherical coordinates */
        Float theta_o = SphericalTheta(wo), phi_o = FMath.Atan2(wo.y, wo.x);
        Float theta_m = SphericalTheta(wm), phi_m = FMath.Atan2(wm.y, wm.x);

        /* Spherical coordinates -> unit coordinate system */
        Point2f u_wm = new(theta2u(theta_m), phi2u(brdf.isotropic ? (phi_m - phi_o) : phi_m));
        u_wm.y = u_wm.y - FMath.Floor(u_wm.y);

        PLSample ui = brdf.vndf.Invert(u_wm, phi_o, theta_o);
        Point2f sample = ui.p;
        Float vndfPDF = ui.pdf;

        Float pdf = brdf.luminance.Evaluate(sample, phi_o, theta_o);
        Float sinTheta_m = FMath.Sqrt(Sqr(wm.x) + Sqr(wm.y));
        Float jacobian = 4f * Dot(wo, wm) * FMath.Max(2 * Sqr(Pi) * u_wm.x * sinTheta_m, 1e-6f);
        return vndfPDF * pdf / jacobian;
    }

    public override void Regularize() { }

    public static string Name() => "MeasuredBxDF";

    public override string ToString() => $"[ MeasuredBxDF brdf: {brdf} ]";

    public override BxDFFlags Flags() => BxDFFlags.Reflection | BxDFFlags.Glossy;
}

// NormalizedFresnelBxDF Definition
public sealed class NormalizedFresnelBxDF : BxDF
{
    private readonly Float eta;

    // NormalizedFresnelBxDF Public Methods
    public NormalizedFresnelBxDF(Float eta) => this.eta = eta;

    // (pbrt returns a BSDFSample here, not an optional one; an empty one when
    // reflection isn't sampled.)
    public override BSDFSample? Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode = TransportMode.Radiance,
                                         BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return new BSDFSample();

        // Cosine-sample the hemisphere, flipping the direction if necessary
        Vector3f wi = SampleCosineHemisphere(u);
        if (wo.z < 0)
            wi.z *= -1;
        return new BSDFSample(f(wo, wi, mode), wi, PDF(wo, wi, mode, sampleFlags), BxDFFlags.DiffuseReflection);
    }

    public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode,
                              BxDFReflTransFlags sampleFlags = BxDFReflTransFlags.All)
    {
        if ((sampleFlags & BxDFReflTransFlags.Reflection) == 0)
            return 0;
        return SameHemisphere(wo, wi) ? AbsCosTheta(wi) * InvPi : 0;
    }

    public override void Regularize() { }

    public static string Name() => "NormalizedFresnelBxDF";

    public override string ToString() => $"[ NormalizedFresnelBxDF eta: {FloatToString(eta)} ]";

    public override BxDFFlags Flags() => BxDFFlags.Reflection | BxDFFlags.Diffuse;

    public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
    {
        if (!SameHemisphere(wo, wi))
            return new SampledSpectrum(0f);
        // Compute $\Sw$ factor for BSSRDF value
        Float c = 1 - 2 * FresnelMoment1(1 / eta);
        SampledSpectrum f = new((1 - FrDielectric(CosTheta(wi), eta)) / (c * Pi));

        // Update BSSRDF transmission term to account for adjoint light transport
        if (mode == TransportMode.Radiance)
            f *= Sqr(eta);

        return f;
    }
}

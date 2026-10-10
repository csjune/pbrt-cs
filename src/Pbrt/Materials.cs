// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/materials.h and materials.cpp
//
// MaterialEvalContext can't derive from TextureEvalContext (both are
// structs), so it holds one and converts to it implicitly. MixMaterial
// hashes its materials' ids where pbrt hashes their pointers, so which
// material it picks differs from pbrt's (pbrt's choice depends on where the
// materials happen to be allocated).

namespace Pbrt;

// MaterialEvalContext Definition
public struct MaterialEvalContext
{
    // MaterialEvalContext Public Methods
    public MaterialEvalContext() { }

    public MaterialEvalContext(in SurfaceInteraction si)
    {
        tex = new TextureEvalContext(si);
        wo = si.wo;
        ns = si.shading.n;
        dpdus = si.shading.dpdu;
    }

    public static implicit operator MaterialEvalContext(in SurfaceInteraction si) => new(si);

    public static implicit operator TextureEvalContext(in MaterialEvalContext ctx) => ctx.tex;

    public override readonly string ToString() =>
        $"[ MaterialEvalContext {tex} wo: {wo} ns: {ns} dpdus: {dpdus} ]";

    // (TextureEvalContext's members)
    public TextureEvalContext tex = new();
    public readonly Point3f p => tex.p;
    public readonly Point2f uv => tex.uv;

    public Vector3f wo;
    public Normal3f ns;
    public Vector3f dpdus;
}

// NormalBumpEvalContext Definition
public struct NormalBumpEvalContext
{
    // NormalBumpEvalContext Public Methods
    public NormalBumpEvalContext() { }

    public NormalBumpEvalContext(in SurfaceInteraction si)
    {
        p = si.p();
        uv = si.uv;
        n = si.n;
        dudx = si.dudx;
        dudy = si.dudy;
        dvdx = si.dvdx;
        dvdy = si.dvdy;
        dpdx = si.dpdx;
        dpdy = si.dpdy;
        faceIndex = si.faceIndex;
        shading.n = si.shading.n;
        shading.dpdu = si.shading.dpdu;
        shading.dpdv = si.shading.dpdv;
        shading.dndu = si.shading.dndu;
        shading.dndv = si.shading.dndv;
    }

    public static implicit operator NormalBumpEvalContext(in SurfaceInteraction si) => new(si);

    public override readonly string ToString() =>
        $"[ NormalBumpEvalContext p: {p} uv: {uv} shading.n: {shading.n} shading.dpdu: {shading.dpdu} " +
        $"shading.dpdv: {shading.dpdv} shading.dndu: {shading.dndu} shading.dndv: {shading.dndv} " +
        $"dudx: {FloatToString(dudx)} dudy: {FloatToString(dudy)} dvdx: {FloatToString(dvdx)} " +
        $"dvdy: {FloatToString(dvdy)} dpdx: {dpdx} dpdy: {dpdy} faceIndex: {faceIndex} ]";

    public static implicit operator TextureEvalContext(in NormalBumpEvalContext ctx) =>
        new(ctx.p, ctx.dpdx, ctx.dpdy, ctx.n, ctx.uv, ctx.dudx, ctx.dudy, ctx.dvdx, ctx.dvdy, ctx.faceIndex);

    // NormalBumpEvalContext Public Members
    public Point3f p;
    public Point2f uv;
    public Normal3f n;

    public struct Shading
    {
        public Normal3f n;
        public Vector3f dpdu, dpdv;
        public Normal3f dndu, dndv;
    }

    public Shading shading;
    public Float dudx = 0, dudy = 0, dvdx = 0, dvdy = 0;
    public Vector3f dpdx, dpdy;
    public int faceIndex = 0;
}

public static partial class Util
{
    // Normal Mapping Function Definitions
    public static void NormalMap(Image normalMap, in NormalBumpEvalContext ctx, out Vector3f dpdu,
                                 out Vector3f dpdv)
    {
        // Get normalized normal vector from normal map
        WrapMode2D wrap = new(WrapMode.Repeat);
        Point2f uv = new(ctx.uv[0], 1 - ctx.uv[1]);
        Vector3f ns = new(2 * normalMap.BilerpChannel(uv, 0, wrap) - 1,
                          2 * normalMap.BilerpChannel(uv, 1, wrap) - 1,
                          2 * normalMap.BilerpChannel(uv, 2, wrap) - 1);
        ns = Normalize(ns);

        // Transform tangent-space normal to rendering space
        Frame frame = Frame.FromXZ(Normalize(ctx.shading.dpdu), new Vector3f(ctx.shading.n));
        ns = frame.FromLocal(ns);

        // Find $\dpdu$ and $\dpdv$ that give shading normal
        Float ulen = Length(ctx.shading.dpdu), vlen = Length(ctx.shading.dpdv);
        dpdu = Normalize(GramSchmidt(ctx.shading.dpdu, ns)) * ulen;
        dpdv = Normalize(Cross(ns, dpdu)) * vlen;
    }

    // Bump Mapping Function Definitions
    public static void BumpMap<TextureEvaluator>(TextureEvaluator texEval, FloatTexture displacement,
                                                 in NormalBumpEvalContext ctx, out Vector3f dpdu,
                                                 out Vector3f dpdv)
        where TextureEvaluator : struct, ITextureEvaluator
    {
        DCHECK(texEval.CanEvaluate([displacement], []));
        // Compute offset positions and evaluate displacement texture
        TextureEvalContext shiftedCtx = ctx;
        // Shift _shiftedCtx_ _du_ in the $u$ direction
        Float du = .5f * (FMath.Abs(ctx.dudx) + FMath.Abs(ctx.dudy));
        if (du == 0)
            du = .0005f;
        shiftedCtx.p = ctx.p + du * ctx.shading.dpdu;
        shiftedCtx.uv = ctx.uv + new Vector2f(du, 0f);

        Float uDisplace = texEval.Evaluate(displacement, shiftedCtx);
        // Shift _shiftedCtx_ _dv_ in the $v$ direction
        Float dv = .5f * (FMath.Abs(ctx.dvdx) + FMath.Abs(ctx.dvdy));
        if (dv == 0)
            dv = .0005f;
        shiftedCtx.p = ctx.p + dv * ctx.shading.dpdv;
        shiftedCtx.uv = ctx.uv + new Vector2f(0f, dv);

        Float vDisplace = texEval.Evaluate(displacement, shiftedCtx);
        Float displace = texEval.Evaluate(displacement, ctx);

        // Compute bump-mapped differential geometry
        dpdu = ctx.shading.dpdu + (uDisplace - displace) / du * new Vector3f(ctx.shading.n) +
               displace * new Vector3f(ctx.shading.dndu);
        dpdv = ctx.shading.dpdv + (vDisplace - displace) / dv * new Vector3f(ctx.shading.n) +
               displace * new Vector3f(ctx.shading.dndv);
    }

    // The index of refraction that DielectricMaterial and the coated
    // materials read from "eta" (or "interface.eta").
    internal static Spectrum MaterialGetEta(TextureParameterDictionary parameters, string name)
    {
        Spectrum? eta;
        if (parameters.GetFloatArray(name).Count > 0)
            eta = new ConstantSpectrum(parameters.GetFloatArray(name)[0]);
        else
            eta = parameters.GetOneSpectrum(name, null, SpectrumType.Unbounded);
        eta ??= new ConstantSpectrum(1.5f);
        return eta;
    }

    // Returns the eta(lambda[0]) that the dielectric materials use, terminating
    // the secondary wavelengths if eta varies with wavelength.
    internal static Float MaterialSampleEta(Spectrum eta, ref SampledWavelengths lambda)
    {
        Float sampledEta = eta.Evaluate(lambda[0]);
        if (eta is not ConstantSpectrum)
            lambda.TerminateSecondary();
        // Handle edge case in case lambda[0] is beyond the wavelengths stored by the
        // Spectrum.
        if (sampledEta == 0)
            sampledEta = 1;
        return sampledEta;
    }

    internal static string NormalMapToString(Image? normalMap) => normalMap?.ToString() ?? "(nullptr)";

    internal static string BoolToString(bool b) => b ? "true" : "false";

    internal static string TexToString(object? tex) => tex?.ToString() ?? "(nullptr)";

    // Returns the "uroughness"/"vroughness" textures, falling back to
    // "roughness".
    internal static (FloatTexture u, FloatTexture v) GetRoughness(TextureParameterDictionary parameters,
                                                                   string prefix = "")
    {
        FloatTexture? uRoughness = parameters.GetFloatTextureOrNull(prefix + "uroughness");
        FloatTexture? vRoughness = parameters.GetFloatTextureOrNull(prefix + "vroughness");
        uRoughness ??= parameters.GetFloatTexture(prefix + "roughness", 0f);
        vRoughness ??= parameters.GetFloatTexture(prefix + "roughness", 0f);
        return (uRoughness, vRoughness);
    }

    internal static TrowbridgeReitzDistribution RoughnessDistribution(Float urough, Float vrough,
                                                                     bool remapRoughness)
    {
        if (remapRoughness)
        {
            urough = TrowbridgeReitzDistribution.RoughnessToAlpha(urough);
            vrough = TrowbridgeReitzDistribution.RoughnessToAlpha(vrough);
        }
        return new TrowbridgeReitzDistribution(urough, vrough);
    }
}

// DielectricMaterial Definition
public sealed class DielectricMaterial(FloatTexture uRoughness, FloatTexture vRoughness, Spectrum eta,
                                       FloatTexture? displacement, Image? normalMap, bool remapRoughness)
    : Material
{
    // DielectricMaterial Public Methods
    public static string Name() => "DielectricMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([uRoughness, vRoughness], []);

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static DielectricMaterial Create(TextureParameterDictionary parameters, Image? normalMap, FileLoc? loc)
    {
        Spectrum eta = MaterialGetEta(parameters, "eta");

        var (uRoughness, vRoughness) = GetRoughness(parameters);

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        bool remapRoughness = parameters.GetOneBool("remaproughness", true);

        return new DielectricMaterial(uRoughness, vRoughness, eta, displacement, normalMap, remapRoughness);
    }

    public override string ToString() =>
        $"[ DielectricMaterial displacement: {TexToString(displacement)} normalMap: {NormalMapToString(normalMap)} " +
        $"uRoughness: {uRoughness} vRoughness: {vRoughness} eta: {eta} remapRoughness: {BoolToString(remapRoughness)} ]";

    public override DielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                             ref SampledWavelengths lambda)
    {
        // Compute index of refraction for dielectric material
        Float sampledEta = MaterialSampleEta(eta, ref lambda);

        // Create microfacet distribution for dielectric material
        Float urough = texEval.Evaluate(uRoughness, ctx), vrough = texEval.Evaluate(vRoughness, ctx);
        TrowbridgeReitzDistribution distrib = RoughnessDistribution(urough, vrough, remapRoughness);

        // Return BSDF for dielectric material
        return new DielectricBxDF(sampledEta, distrib);
    }
}

// ThinDielectricMaterial Definition
public sealed class ThinDielectricMaterial(Spectrum eta, FloatTexture? displacement, Image? normalMap) : Material
{
    // ThinDielectricMaterial Public Methods
    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) => true;

    public override ThinDielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval,
                                                                 in MaterialEvalContext ctx,
                                                                 ref SampledWavelengths lambda)
    {
        // Compute index of refraction for dielectric material
        Float sampledEta = MaterialSampleEta(eta, ref lambda);

        // Return BxDF for _ThinDielectricMaterial_
        return new ThinDielectricBxDF(sampledEta);
    }

    public static string Name() => "ThinDielectricMaterial";

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static ThinDielectricMaterial Create(TextureParameterDictionary parameters, Image? normalMap,
                                                FileLoc? loc)
    {
        Spectrum eta = MaterialGetEta(parameters, "eta");

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");

        return new ThinDielectricMaterial(eta, displacement, normalMap);
    }

    public override string ToString() =>
        $"[ ThinDielectricMaterial displacement: {TexToString(displacement)} " +
        $"normalMap: {NormalMapToString(normalMap)} eta: {eta} ]";
}

// MixMaterial Definition
public sealed class MixMaterial : Material
{
    // MixMaterial Private Members
    private readonly FloatTexture amount;
    private readonly Material[] materials = new Material[2];

    // MixMaterial Public Methods
    public MixMaterial(ReadOnlySpan<Material> m, FloatTexture amount)
    {
        this.amount = amount;
        materials[0] = m[0];
        materials[1] = m[1];
    }

    public Material ChooseMaterial<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx)
        where TextureEvaluator : struct, ITextureEvaluator
    {
        Float amt = texEval.Evaluate(amount, ctx);
        if (amt <= 0)
            return materials[0];
        if (amt >= 1)
            return materials[1];
        Float u = HashFloat(ctx.p, ctx.wo, materials[0].id, materials[1].id);
        return (amt < u) ? materials[0] : materials[1];
    }

    public Material GetMaterial(int i) => materials[i];

    public static string Name() => "MixMaterial";

    public override FloatTexture? GetDisplacement()
    {
        LOG_FATAL("Shouldn't be called");
        return null;
    }

    public override Image? GetNormalMap()
    {
        LOG_FATAL("Shouldn't be called");
        return null;
    }

    public static MixMaterial Create(ReadOnlySpan<Material> materials, TextureParameterDictionary parameters,
                                     FileLoc? loc)
    {
        FloatTexture amount = parameters.GetFloatTexture("amount", 0.5f);

        // Check for this stuff here, where we can include the FileLoc in
        // the error message. Note that both of these limitations could be
        // relaxed if they were problematic; the issue is that we currently
        // resolve MixMaterials in the closest hit shader...
        if (Options.wavefront && !new BasicTextureEvaluator().CanEvaluate([amount], []))
            ErrorExit(loc, "The wavefront renderer currently only supports basic textures " +
                           "for its \"amount\" parameter.");

        return new MixMaterial(materials, amount);
    }

    public override BSSRDF? GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                        ref SampledWavelengths lambda)
    {
        LOG_FATAL("Shouldn't be called");
        return null;
    }

    // (pbrt's MixMaterial has no BxDF type, so Material::GetBSDF() returns
    // an unset BSDF for it.)
    public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                   ref SampledWavelengths lambda) => new();

    public override string ToString() =>
        $"[ MixMaterial materials: [ {materials[0]} {materials[1]} ] amount: {amount} ]";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([amount], []);

    public override BxDF? GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                     ref SampledWavelengths lambda)
    {
        LOG_FATAL("MixMaterial::GetBxDF() shouldn't be called");
        return null;
    }
}

// HairMaterial Definition
public sealed class HairMaterial(SpectrumTexture? sigma_a, SpectrumTexture? color, FloatTexture? eumelanin,
                                 FloatTexture? pheomelanin, FloatTexture eta, FloatTexture beta_m,
                                 FloatTexture beta_n, FloatTexture alpha) : Material
{
    // HairMaterial Public Methods
    public static string Name() => "HairMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([eumelanin, pheomelanin, eta, beta_m, beta_n, alpha], [sigma_a, color]);

    public override HairBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda)
    {
        Float bm = FMath.Max((Float)1e-2, FMath.Min(1.0f, texEval.Evaluate(beta_m, ctx)));
        Float bn = FMath.Max((Float)1e-2, FMath.Min(1.0f, texEval.Evaluate(beta_n, ctx)));
        Float a = texEval.Evaluate(alpha, ctx);
        Float e = texEval.Evaluate(eta, ctx);

        SampledSpectrum sig_a;
        if (sigma_a != null)
            sig_a = ClampZero(texEval.Evaluate(sigma_a, ctx, lambda));
        else if (color != null)
        {
            SampledSpectrum c = Clamp(texEval.Evaluate(color, ctx, lambda), 0, 1);
            sig_a = HairBxDF.SigmaAFromReflectance(c, bn, lambda);
        }
        else
        {
            CHECK(eumelanin != null || pheomelanin != null);
            // (GCC evaluates the arguments from right to left.)
            Float cp = FMath.Max(0, pheomelanin != null ? texEval.Evaluate(pheomelanin, ctx) : 0);
            Float ce = FMath.Max(0, eumelanin != null ? texEval.Evaluate(eumelanin, ctx) : 0);
            sig_a = HairBxDF.SigmaAFromConcentration(ce, cp).Sample(lambda);
        }

        // Offset along width
        Float h = -1 + 2 * ctx.uv[1];
        return new HairBxDF(h, e, sig_a, bm, bn, a);
    }

    public static HairMaterial Create(TextureParameterDictionary parameters, FileLoc? loc)
    {
        SpectrumTexture? sigma_a = parameters.GetSpectrumTextureOrNull("sigma_a", SpectrumType.Unbounded);
        SpectrumTexture? reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo);
        reflectance ??= parameters.GetSpectrumTextureOrNull("color", SpectrumType.Albedo);
        FloatTexture? eumelanin = parameters.GetFloatTextureOrNull("eumelanin");
        FloatTexture? pheomelanin = parameters.GetFloatTextureOrNull("pheomelanin");
        if (sigma_a != null)
        {
            if (reflectance != null)
                Warning(loc, "Ignoring \"reflectance\" parameter since \"sigma_a\" was provided.");
            if (eumelanin != null)
                Warning(loc, "Ignoring \"eumelanin\" parameter since \"sigma_a\" was provided.");
            if (pheomelanin != null)
                Warning(loc, "Ignoring \"pheomelanin\" parameter since \"sigma_a\" was provided.");
        }
        else if (reflectance != null)
        {
            // (pbrt's warning for sigma_a here can't be reached.)
            if (eumelanin != null)
                Warning(loc, "Ignoring \"eumelanin\" parameter since \"reflectance\" was provided.");
            if (pheomelanin != null)
                Warning(loc, "Ignoring \"pheomelanin\" parameter since \"reflectance\" was provided.");
        }
        else if (eumelanin != null || pheomelanin != null)
        {
            // (pbrt's warnings here can't be reached: sigma_a and reflectance
            // are both null.)
        }
        else
        {
            // Default: brown-ish hair.
            sigma_a = new SpectrumConstantTexture(HairBxDF.SigmaAFromConcentration((Float)1.3, 0f));
        }

        FloatTexture eta = parameters.GetFloatTexture("eta", 1.55f);
        FloatTexture beta_m = parameters.GetFloatTexture("beta_m", 0.3f);
        FloatTexture beta_n = parameters.GetFloatTexture("beta_n", 0.3f);
        FloatTexture alpha = parameters.GetFloatTexture("alpha", 2f);

        return new HairMaterial(sigma_a, reflectance, eumelanin, pheomelanin, eta, beta_m, beta_n, alpha);
    }

    public override FloatTexture? GetDisplacement() => null;
    public override Image? GetNormalMap() => null;

    public override string ToString() =>
        $"[ HairMaterial sigma_a: {TexToString(sigma_a)} color: {TexToString(color)} " +
        $"eumelanin: {TexToString(eumelanin)} pheomelanin: {TexToString(pheomelanin)} eta: {eta} " +
        $"beta_m: {beta_m} beta_n: {beta_n} alpha: {alpha} ]";
}

// DiffuseMaterial Definition
public sealed class DiffuseMaterial(SpectrumTexture reflectance, FloatTexture? displacement, Image? normalMap)
    : Material
{
    // DiffuseMaterial Public Methods
    public static string Name() => "DiffuseMaterial";

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static DiffuseMaterial Create(TextureParameterDictionary parameters, Image? normalMap, FileLoc? loc)
    {
        SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo) ??
                                      new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");

        return new DiffuseMaterial(reflectance, displacement, normalMap);
    }

    public override string ToString() =>
        $"[ DiffuseMaterial displacement: {TexToString(displacement)} normapMap: {NormalMapToString(normalMap)} " +
        $"reflectance: {reflectance} ]";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([], [reflectance]);

    public override DiffuseBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                          ref SampledWavelengths lambda)
    {
        SampledSpectrum r = Clamp(texEval.Evaluate(reflectance, ctx, lambda), 0, 1);
        return new DiffuseBxDF(r);
    }
}

// ConductorMaterial Definition
public sealed class ConductorMaterial(SpectrumTexture? eta, SpectrumTexture? k, SpectrumTexture? reflectance,
                                      FloatTexture uRoughness, FloatTexture vRoughness, FloatTexture? displacement,
                                      Image? normalMap, bool remapRoughness) : Material
{
    // ConductorMaterial Public Methods
    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([uRoughness, vRoughness], [eta, k, reflectance]);

    public override ConductorBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                            ref SampledWavelengths lambda)
    {
        // Return BSDF for _ConductorMaterial_
        Float uRough = texEval.Evaluate(uRoughness, ctx), vRough = texEval.Evaluate(vRoughness, ctx);
        if (remapRoughness)
        {
            uRough = TrowbridgeReitzDistribution.RoughnessToAlpha(uRough);
            vRough = TrowbridgeReitzDistribution.RoughnessToAlpha(vRough);
        }
        SampledSpectrum etas, ks;
        if (eta != null)
        {
            etas = texEval.Evaluate(eta, ctx, lambda);
            ks = texEval.Evaluate(k!, ctx, lambda);
        }
        else
        {
            // Avoid r==0 NaN case...
            SampledSpectrum r = Clamp(texEval.Evaluate(reflectance!, ctx, lambda), 0, (Float).9999);
            etas = new SampledSpectrum(1f);
            ks = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1) - r));
        }
        TrowbridgeReitzDistribution distrib = new(uRough, vRough);
        return new ConductorBxDF(distrib, etas, ks);
    }

    public static string Name() => "ConductorMaterial";

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static ConductorMaterial Create(TextureParameterDictionary parameters, Image? normalMap, FileLoc? loc)
    {
        SpectrumTexture? eta = parameters.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded);
        SpectrumTexture? k = parameters.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded);
        SpectrumTexture? reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo);

        if (reflectance != null && (eta != null || k != null))
            ErrorExit(loc, "For the conductor material, both \"reflectance\" " +
                           "and \"eta\" and \"k\" can't be provided.");
        if (reflectance == null)
        {
            eta ??= new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-eta")!);
            k ??= new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-k")!);
        }

        var (uRoughness, vRoughness) = GetRoughness(parameters);

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        bool remapRoughness = parameters.GetOneBool("remaproughness", true);

        return new ConductorMaterial(eta, k, reflectance, uRoughness, vRoughness, displacement, normalMap,
                                     remapRoughness);
    }

    public override string ToString() =>
        $"[ ConductorMaterial displacement: {TexToString(displacement)} normalMap: {NormalMapToString(normalMap)} " +
        $"eta: {TexToString(eta)} k: {TexToString(k)} reflectance: {TexToString(reflectance)} " +
        $"uRoughness: {uRoughness} vRoughness: {vRoughness} remapRoughness: {BoolToString(remapRoughness)} ]";
}

// CoatedDiffuseMaterial Definition
public sealed class CoatedDiffuseMaterial(SpectrumTexture reflectance, FloatTexture uRoughness,
                                          FloatTexture vRoughness, FloatTexture thickness, SpectrumTexture albedo,
                                          FloatTexture g, Spectrum eta, FloatTexture? displacement,
                                          Image? normalMap, bool remapRoughness, int maxDepth, int nSamples)
    : Material
{
    // CoatedDiffuseMaterial Public Methods
    public static string Name() => "CoatedDiffuseMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([uRoughness, vRoughness, thickness, g], [reflectance, albedo]);

    public override CoatedDiffuseBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval,
                                                                in MaterialEvalContext ctx,
                                                                ref SampledWavelengths lambda)
    {
        // Initialize diffuse component of plastic material
        SampledSpectrum r = Clamp(texEval.Evaluate(reflectance, ctx, lambda), 0, 1);

        // Create microfacet distribution _distrib_ for coated diffuse material
        Float urough = texEval.Evaluate(uRoughness, ctx);
        Float vrough = texEval.Evaluate(vRoughness, ctx);
        TrowbridgeReitzDistribution distrib = RoughnessDistribution(urough, vrough, remapRoughness);

        Float thick = texEval.Evaluate(thickness, ctx);

        Float sampledEta = MaterialSampleEta(eta, ref lambda);

        SampledSpectrum a = Clamp(texEval.Evaluate(albedo, ctx, lambda), 0, 1);
        Float gg = Clamp(texEval.Evaluate(g, ctx), -1, 1);

        return new CoatedDiffuseBxDF(new DielectricBxDF(sampledEta, distrib), new DiffuseBxDF(r), thick, a, gg,
                                     maxDepth, nSamples);
    }

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static CoatedDiffuseMaterial Create(TextureParameterDictionary parameters, Image? normalMap,
                                               FileLoc? loc)
    {
        SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo) ??
                                      new SpectrumConstantTexture(new ConstantSpectrum(0.5f));

        var (uRoughness, vRoughness) = GetRoughness(parameters);

        FloatTexture thickness = parameters.GetFloatTexture("thickness", (Float).01);

        Spectrum eta = MaterialGetEta(parameters, "eta");

        int maxDepth = parameters.GetOneInt("maxdepth", 10);
        int nSamples = parameters.GetOneInt("nsamples", 1);

        FloatTexture g = parameters.GetFloatTexture("g", 0f);
        SpectrumTexture albedo = parameters.GetSpectrumTexture("albedo", null, SpectrumType.Albedo) ??
                                 new SpectrumConstantTexture(new ConstantSpectrum(0f));

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        bool remapRoughness = parameters.GetOneBool("remaproughness", true);

        return new CoatedDiffuseMaterial(reflectance, uRoughness, vRoughness, thickness, albedo, g, eta,
                                         displacement, normalMap, remapRoughness, maxDepth, nSamples);
    }

    public override string ToString() =>
        $"[ CoatedDiffuseMaterial displacement: {TexToString(displacement)} " +
        $"normalMap: {NormalMapToString(normalMap)} reflectance: {reflectance} uRoughness: {uRoughness} " +
        $"vRoughness: {vRoughness} thickness: {thickness} eta: {eta} remapRoughness: {BoolToString(remapRoughness)} ]";
}

// CoatedConductorMaterial Definition
public sealed class CoatedConductorMaterial(FloatTexture interfaceURoughness, FloatTexture interfaceVRoughness,
                                            FloatTexture thickness, Spectrum interfaceEta, FloatTexture g,
                                            SpectrumTexture albedo, FloatTexture conductorURoughness,
                                            FloatTexture conductorVRoughness, SpectrumTexture? conductorEta,
                                            SpectrumTexture? k, SpectrumTexture? reflectance,
                                            FloatTexture? displacement, Image? normalMap, bool remapRoughness,
                                            int maxDepth, int nSamples) : Material
{
    // CoatedConductorMaterial Public Methods
    public static string Name() => "CoatedConductorMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([interfaceURoughness, interfaceVRoughness, thickness, g, conductorURoughness,
                             conductorVRoughness],
                            [conductorEta, k, reflectance, albedo]);

    public override CoatedConductorBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval,
                                                                  in MaterialEvalContext ctx,
                                                                  ref SampledWavelengths lambda)
    {
        Float iurough = texEval.Evaluate(interfaceURoughness, ctx);
        Float ivrough = texEval.Evaluate(interfaceVRoughness, ctx);
        TrowbridgeReitzDistribution interfaceDistrib = RoughnessDistribution(iurough, ivrough, remapRoughness);

        Float thick = texEval.Evaluate(thickness, ctx);

        Float ieta = MaterialSampleEta(interfaceEta, ref lambda);

        SampledSpectrum ce, ck;
        if (conductorEta != null)
        {
            ce = texEval.Evaluate(conductorEta, ctx, lambda);
            ck = texEval.Evaluate(k!, ctx, lambda);
        }
        else
        {
            // Avoid r==1 NaN case...
            SampledSpectrum r = Clamp(texEval.Evaluate(reflectance!, ctx, lambda), 0, (Float).9999);
            ce = new SampledSpectrum(1f);
            ck = 2 * Sqrt(r) / Sqrt(ClampZero(new SampledSpectrum(1) - r));
        }
        ce /= ieta;
        ck /= ieta;

        Float curough = texEval.Evaluate(conductorURoughness, ctx);
        Float cvrough = texEval.Evaluate(conductorVRoughness, ctx);
        TrowbridgeReitzDistribution conductorDistrib = RoughnessDistribution(curough, cvrough, remapRoughness);

        SampledSpectrum a = Clamp(texEval.Evaluate(albedo, ctx, lambda), 0, 1);
        Float gg = Clamp(texEval.Evaluate(g, ctx), -1, 1);

        return new CoatedConductorBxDF(new DielectricBxDF(ieta, interfaceDistrib),
                                       new ConductorBxDF(conductorDistrib, ce, ck), thick, a, gg, maxDepth,
                                       nSamples);
    }

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static CoatedConductorMaterial Create(TextureParameterDictionary parameters, Image? normalMap,
                                                 FileLoc? loc)
    {
        // interface
        var (interfaceURoughness, interfaceVRoughness) = GetRoughness(parameters, "interface.");

        FloatTexture thickness = parameters.GetFloatTexture("thickness", (Float).01);

        Spectrum interfaceEta = MaterialGetEta(parameters, "interface.eta");

        // conductor
        var (conductorURoughness, conductorVRoughness) = GetRoughness(parameters, "conductor.");
        SpectrumTexture? conductorEta = parameters.GetSpectrumTextureOrNull("conductor.eta", SpectrumType.Unbounded);
        SpectrumTexture? k = parameters.GetSpectrumTextureOrNull("conductor.k", SpectrumType.Unbounded);
        SpectrumTexture? reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo);

        if (reflectance != null && (conductorEta != null || k != null))
            ErrorExit(loc, "For the coated conductor material, both \"reflectance\" " +
                           "and \"eta\" and \"k\" can't be provided.");
        if (reflectance == null)
        {
            conductorEta ??= new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-eta")!);
            k ??= new SpectrumConstantTexture(GetNamedSpectrum("metal-Cu-k")!);
        }

        int maxDepth = parameters.GetOneInt("maxdepth", 10);
        int nSamples = parameters.GetOneInt("nsamples", 1);

        FloatTexture g = parameters.GetFloatTexture("g", 0f);
        SpectrumTexture albedo = parameters.GetSpectrumTexture("albedo", null, SpectrumType.Albedo) ??
                                 new SpectrumConstantTexture(new ConstantSpectrum(0f));

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        bool remapRoughness = parameters.GetOneBool("remaproughness", true);

        return new CoatedConductorMaterial(interfaceURoughness, interfaceVRoughness, thickness, interfaceEta, g,
                                           albedo, conductorURoughness, conductorVRoughness, conductorEta, k,
                                           reflectance, displacement, normalMap, remapRoughness, maxDepth,
                                           nSamples);
    }

    public override string ToString() =>
        $"[ CoatedConductorMaterial displacement: {TexToString(displacement)} " +
        $"normalMap: {NormalMapToString(normalMap)} interfaceURoughness: {interfaceURoughness} " +
        $"interfaceVRoughness: {interfaceVRoughness} thickness: {thickness} interfaceEta: {interfaceEta} " +
        $"g: {g} albedo: {albedo} conductorURoughness: {conductorURoughness} " +
        $"conductorVRoughness: {conductorVRoughness} conductorEta: {TexToString(conductorEta)} " +
        $"k: {TexToString(k)} conductorReflectance: {TexToString(reflectance)} " +
        $"remapRoughness: {BoolToString(remapRoughness)} maxDepth: {maxDepth} nSamples: {nSamples} ]";
}

// SubsurfaceMaterial Definition
public sealed class SubsurfaceMaterial : Material
{
    // SubsurfaceMaterial Private Members
    private readonly FloatTexture? displacement;
    private readonly Image? normalMap;
    private readonly SpectrumTexture? sigma_a, sigma_s, reflectance, mfp;
    private readonly Float scale, eta;
    private readonly FloatTexture uRoughness, vRoughness;
    private readonly bool remapRoughness;
    private readonly BSSRDFTable table;

    // SubsurfaceMaterial Public Methods
    public SubsurfaceMaterial(Float scale, SpectrumTexture? sigma_a, SpectrumTexture? sigma_s,
                              SpectrumTexture? reflectance, SpectrumTexture? mfp, Float g, Float eta,
                              FloatTexture uRoughness, FloatTexture vRoughness, FloatTexture? displacement,
                              Image? normalMap, bool remapRoughness)
    {
        this.displacement = displacement;
        this.normalMap = normalMap;
        this.scale = scale;
        this.sigma_a = sigma_a;
        this.sigma_s = sigma_s;
        this.reflectance = reflectance;
        this.mfp = mfp;
        this.uRoughness = uRoughness;
        this.vRoughness = vRoughness;
        this.eta = eta;
        this.remapRoughness = remapRoughness;
        table = new BSSRDFTable(100, 64);
        ComputeBeamDiffusionBSSRDF(g, eta, table);
    }

    public static string Name() => "SubsurfaceMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([uRoughness, vRoughness], [sigma_a, sigma_s]);

    public override DielectricBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                             ref SampledWavelengths lambda)
    {
        // Initialize BSDF for _SubsurfaceMaterial_

        Float urough = texEval.Evaluate(uRoughness, ctx), vrough = texEval.Evaluate(vRoughness, ctx);
        TrowbridgeReitzDistribution distrib = RoughnessDistribution(urough, vrough, remapRoughness);

        // Initialize _bsdf_ for smooth or rough dielectric
        return new DielectricBxDF(eta, distrib);
    }

    public override TabulatedBSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval,
                                                                in MaterialEvalContext ctx,
                                                                ref SampledWavelengths lambda)
    {
        SampledSpectrum sig_a, sig_s;
        if (sigma_a != null && sigma_s != null)
        {
            // Evaluate textures for $\sigma_\roman{a}$ and $\sigma_\roman{s}$
            sig_a = ClampZero(scale * texEval.Evaluate(sigma_a, ctx, lambda));
            sig_s = ClampZero(scale * texEval.Evaluate(sigma_s, ctx, lambda));
        }
        else
        {
            // Compute _sig_a_ and _sig_s_ from reflectance and mfp
            DCHECK(reflectance != null && mfp != null);
            SampledSpectrum mfree = ClampZero(scale * texEval.Evaluate(mfp!, ctx, lambda));
            SampledSpectrum r = Clamp(texEval.Evaluate(reflectance!, ctx, lambda), 0, 1);
            SubsurfaceFromDiffuse(table, r, mfree, out sig_a, out sig_s);
        }
        return new TabulatedBSSRDF(ctx.p, ctx.ns, ctx.wo, eta, sig_a, sig_s, table);
    }

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public override bool HasSubsurfaceScattering() => true;

    public static SubsurfaceMaterial Create(TextureParameterDictionary parameters, Image? normalMap, FileLoc? loc)
    {
        SpectrumTexture? sigma_a = null, sigma_s = null, reflectance = null, mfp = null;

        Float g = parameters.GetOneFloat("g", 0.0f);

        // 4, mutually-exclusive, ways to specify the subsurface properties...
        string name = parameters.GetOneString("name", "");
        if (name.Length > 0)
        {
            // 1. By name
            if (!GetMediumScatteringProperties(name, out Spectrum? sig_a, out Spectrum? sig_s))
                ErrorExit(loc, $"{name}: named medium not found.");
            if (g != 0)
                Warning(loc, "Non-zero \"g\" ignored with named scattering coefficients.");
            g = 0; /* Enforce g=0 (the database specifies reduced scattering
                      coefficients) */
            sigma_a = new SpectrumConstantTexture(sig_a!);
            sigma_s = new SpectrumConstantTexture(sig_s!);
        }
        else
        {
            // 2. sigma_a and sigma_s directly specified
            sigma_a = parameters.GetSpectrumTextureOrNull("sigma_a", SpectrumType.Unbounded);
            sigma_s = parameters.GetSpectrumTextureOrNull("sigma_s", SpectrumType.Unbounded);
            if (sigma_a != null && sigma_s == null)
                ErrorExit(loc, "Provided \"sigma_a\" parameter without \"sigma_s\".");
            if (sigma_s != null && sigma_a == null)
                ErrorExit(loc, "Provided \"sigma_s\" parameter without \"sigma_a\".");

            if (sigma_a == null && sigma_s == null)
            {
                // 3. RGB/Spectrum, reflectance
                reflectance = parameters.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo);
                if (reflectance != null)
                {
                    Spectrum one = new ConstantSpectrum(1);
                    mfp = parameters.GetSpectrumTexture("mfp", one, SpectrumType.Unbounded);
                }
                else
                {
                    // 4. nothing specified -- use defaults
                    RGBUnboundedSpectrum defaultSigma_a =
                        new(RGBColorSpace.sRGB, new RGB(.0011f, .0024f, .014f));
                    RGBUnboundedSpectrum defaultSigma_s =
                        new(RGBColorSpace.sRGB, new RGB(2.55f, 3.21f, 3.77f));
                    sigma_a = new SpectrumConstantTexture(defaultSigma_a);
                    sigma_s = new SpectrumConstantTexture(defaultSigma_s);
                }
            }
        }

        Float scale = parameters.GetOneFloat("scale", 1f);
        Float eta = parameters.GetOneFloat("eta", 1.33f);

        var (uRoughness, vRoughness) = GetRoughness(parameters);

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        bool remapRoughness = parameters.GetOneBool("remaproughness", true);

        return new SubsurfaceMaterial(scale, sigma_a, sigma_s, reflectance, mfp, g, eta, uRoughness, vRoughness,
                                      displacement, normalMap, remapRoughness);
    }

    public override string ToString() =>
        $"[ SubsurfaceMaterial displacement: {TexToString(displacement)} normalMap: {NormalMapToString(normalMap)} " +
        $"scale: {FloatToString(scale)} sigma_a: {TexToString(sigma_a)} sigma_s: {TexToString(sigma_s)} " +
        $"reflectance: {TexToString(reflectance)} mfp: {TexToString(mfp)} uRoughness: {uRoughness} " +
        $"vRoughness: {vRoughness} scale: {FloatToString(scale)} eta: {FloatToString(eta)} " +
        $"remapRoughness: {BoolToString(remapRoughness)} ]";
}

// DiffuseTransmissionMaterial Definition
public sealed class DiffuseTransmissionMaterial(SpectrumTexture reflectance, SpectrumTexture transmittance,
                                                FloatTexture? displacement, Image? normalMap, Float scale)
    : Material
{
    // DiffuseTransmissionMaterial Public Methods
    public static string Name() => "DiffuseTransmissionMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) =>
        texEval.CanEvaluate([], [reflectance, transmittance]);

    public override DiffuseTransmissionBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval,
                                                                      in MaterialEvalContext ctx,
                                                                      ref SampledWavelengths lambda)
    {
        SampledSpectrum r = Clamp(scale * texEval.Evaluate(reflectance, ctx, lambda), 0, 1);
        SampledSpectrum t = Clamp(scale * texEval.Evaluate(transmittance, ctx, lambda), 0, 1);
        return new DiffuseTransmissionBxDF(r, t);
    }

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static DiffuseTransmissionMaterial Create(TextureParameterDictionary parameters, Image? normalMap,
                                                     FileLoc? loc)
    {
        SpectrumTexture reflectance = parameters.GetSpectrumTexture("reflectance", null, SpectrumType.Albedo) ??
                                      new SpectrumConstantTexture(new ConstantSpectrum(0.25f));

        SpectrumTexture transmittance =
            parameters.GetSpectrumTexture("transmittance", null, SpectrumType.Albedo) ??
            new SpectrumConstantTexture(new ConstantSpectrum(0.25f));

        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");
        Float scale = parameters.GetOneFloat("scale", 1f);

        return new DiffuseTransmissionMaterial(reflectance, transmittance, displacement, normalMap, scale);
    }

    public override string ToString() =>
        $"[ DiffuseTransmissionMaterial displacement: {TexToString(displacement)} reflectance: {reflectance} " +
        $"transmittance: {transmittance} scale: {FloatToString(scale)} ]";
}

// MeasuredMaterial Definition
public sealed class MeasuredMaterial : Material
{
    // MeasuredMaterial Private Members
    private readonly FloatTexture? displacement;
    private readonly Image? normalMap;
    private readonly MeasuredBxDFData brdf;

    // MeasuredMaterial Public Methods
    public override MeasuredBxDF GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                           ref SampledWavelengths lambda) =>
        new(brdf, lambda);

    public MeasuredMaterial(string filename, FloatTexture? displacement, Image? normalMap)
    {
        this.displacement = displacement;
        this.normalMap = normalMap;
        brdf = MeasuredBxDF.BRDFDataFromFile(filename)!;
    }

    public static string Name() => "MeasuredMaterial";

    public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval) => true;

    public override FloatTexture? GetDisplacement() => displacement;
    public override Image? GetNormalMap() => normalMap;

    public static MeasuredMaterial? Create(TextureParameterDictionary parameters, Image? normalMap, FileLoc? loc)
    {
        string filename = ResolveFilename(parameters.GetOneString("filename", ""));
        if (filename.Length == 0)
        {
            Error("Filename must be provided for MeasuredMaterial");
            return null;
        }
        FloatTexture? displacement = parameters.GetFloatTextureOrNull("displacement");

        return new MeasuredMaterial(filename, displacement, normalMap);
    }

    public override string ToString() =>
        $"[ MeasuredMaterial displacement: {TexToString(displacement)} normalMap: {NormalMapToString(normalMap)} ]";
}

public abstract partial class Material
{
    private static readonly StatCounter nMaterialsCreated = new("Scene/Materials");
    private static long nextId = 0;

    // A unique id that MixMaterial hashes in place of pbrt's pointer.
    internal readonly long id = Interlocked.Increment(ref nextId);

    public static Material? Create(string name, TextureParameterDictionary parameters, Image? normalMap,
                                   Dictionary<string, Material?> namedMaterials, FileLoc? loc)
    {
        Material? material = null;
        if (name.Length == 0 || name == "none")
        {
            Warning(loc, $"Material \"{name}\" is deprecated; use \"interface\" instead.");
            return null;
        }
        else if (name == "interface")
            return null;
        else if (name == "diffuse")
            material = DiffuseMaterial.Create(parameters, normalMap, loc);
        else if (name == "coateddiffuse")
            material = CoatedDiffuseMaterial.Create(parameters, normalMap, loc);
        else if (name == "coatedconductor")
            material = CoatedConductorMaterial.Create(parameters, normalMap, loc);
        else if (name == "diffusetransmission")
            material = DiffuseTransmissionMaterial.Create(parameters, normalMap, loc);
        else if (name == "dielectric")
            material = DielectricMaterial.Create(parameters, normalMap, loc);
        else if (name == "thindielectric")
            material = ThinDielectricMaterial.Create(parameters, normalMap, loc);
        else if (name == "hair")
            material = HairMaterial.Create(parameters, loc);
        else if (name == "conductor")
            material = ConductorMaterial.Create(parameters, normalMap, loc);
        else if (name == "measured")
            material = MeasuredMaterial.Create(parameters, normalMap, loc);
        else if (name == "subsurface")
            material = SubsurfaceMaterial.Create(parameters, normalMap, loc);
        else if (name == "mix")
        {
            List<string> materialNames = parameters.GetStringArray("materials");
            if (materialNames.Count != 2)
                ErrorExit("Must provide two values for \"string materials\" for mix material.");

            Material[] materials = new Material[2];
            for (int i = 0; i < 2; ++i)
            {
                if (!namedMaterials.TryGetValue(materialNames[i], out Material? m))
                    ErrorExit($"{materialNames[i]}: named material not found.");
                if (m == null)
                    ErrorExit($"{materialNames[i]}: an \"interface\" material cannot be used as an element of " +
                              "the \"mix\" material.");
                materials[i] = m!;
            }
            material = MixMaterial.Create(materials, parameters, loc);
        }
        else
            ErrorExit(loc, $"{name}: material type unknown.");

        if (material == null)
            ErrorExit(loc, $"{name}: unable to create material.");

        parameters.ReportUnused();
        nMaterialsCreated.Inc();
        return material;
    }
}

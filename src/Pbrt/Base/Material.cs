// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/material.h (and the Material inline methods from
// materials.h)
//
// Material is an abstract class. pbrt's GetBxDF() templates return each
// material's concrete BxDF type; here GetBxDF<TextureEvaluator>() is an
// abstract generic method that the materials override with a covariant
// return type. GetBSDF() and GetBSSRDF() allocate with new instead of a
// ScratchBuffer.

namespace Pbrt;

// Material Definition
public abstract partial class Material
{
    // Material Interface
    public abstract BxDF? GetBxDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                     ref SampledWavelengths lambda)
        where TextureEvaluator : struct, ITextureEvaluator;

    public virtual BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                  ref SampledWavelengths lambda)
        where TextureEvaluator : struct, ITextureEvaluator
    {
        // Allocate memory for _ConcreteBxDF_ and return _BSDF_ for material
        BxDF bxdf = GetBxDF(texEval, ctx, ref lambda)!;
        return new BSDF(ctx.ns, ctx.dpdus, bxdf);
    }

    public virtual BSSRDF? GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, in MaterialEvalContext ctx,
                                                       ref SampledWavelengths lambda)
        where TextureEvaluator : struct, ITextureEvaluator => null;

    public abstract bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        where TextureEvaluator : struct, ITextureEvaluator;

    public abstract Image? GetNormalMap();

    public abstract FloatTexture? GetDisplacement();

    public virtual bool HasSubsurfaceScattering() => false;
}

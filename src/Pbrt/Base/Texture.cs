// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/texture.h
//
// The TaggedPointers are abstract classes; FloatTexture.Create() and
// SpectrumTexture.Create() are in Textures.cs. The GPU textures aren't
// ported.

namespace Pbrt;

// FloatTexture Definition
public abstract partial class FloatTexture
{
    // FloatTexture Interface
    public abstract Float Evaluate(in TextureEvalContext ctx);
}

// SpectrumTexture Definition
public abstract partial class SpectrumTexture
{
    // SpectrumTexture Interface
    public abstract SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda);
}

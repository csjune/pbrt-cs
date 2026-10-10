// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/lightsampler.h
//
// LightSampler is an abstract class; LightSampler.Create() is in
// LightSamplers.cs.

namespace Pbrt;

// SampledLight Definition
public struct SampledLight
{
    public Light light;
    public Float p = 0;

    public SampledLight(Light light, Float p)
    {
        this.light = light;
        this.p = p;
    }

    public override readonly string ToString() =>
        $"[ SampledLight light: {light?.ToString() ?? "(nullptr)"} p: {FloatToString(p)} ]";
}

// LightSampler Definition
public abstract partial class LightSampler
{
    // LightSampler Interface
    public abstract SampledLight? Sample(in LightSampleContext ctx, Float u);

    public abstract Float PMF(in LightSampleContext ctx, Light light);

    public abstract SampledLight? Sample(Float u);

    public abstract Float PMF(Light light);
}

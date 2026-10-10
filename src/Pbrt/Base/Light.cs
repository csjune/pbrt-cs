// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/light.h (and the Light inline methods from
// lights.h)
//
// Light is an abstract class; Light.Create() and Light.CreateArea() are in
// Lights.cs. The pointer outputs of PDF_Le() are out parameters. pbrt's
// Light::L() checks that the light is an area light before calling the
// light's L(); here that check is in L() and the lights implement
// EmittedL().

namespace Pbrt;

// LightType Definition
public enum LightType { DeltaPosition, DeltaDirection, Area, Infinite }

// Light Definition
public abstract partial class Light
{
    // Light Interface
    public abstract SampledSpectrum Phi(in SampledWavelengths lambda);

    public abstract LightType Type();

    public abstract LightLiSample? SampleLi(in LightSampleContext ctx, Point2f u, in SampledWavelengths lambda,
                                            bool allowIncompletePDF = false);

    public abstract Float PDF_Li(in LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false);

    // AreaLights only
    public SampledSpectrum L(Point3f p, Normal3f n, Point2f uv, Vector3f w, in SampledWavelengths lambda)
    {
        CHECK(Type() == LightType.Area);
        return EmittedL(p, n, uv, w, lambda);
    }

    protected abstract SampledSpectrum EmittedL(Point3f p, Normal3f n, Point2f uv, Vector3f w,
                                                in SampledWavelengths lambda);

    // InfiniteLights only
    public abstract SampledSpectrum Le(in Ray ray, in SampledWavelengths lambda);

    public abstract void Preprocess(in Bounds3f sceneBounds);

    public abstract LightBounds? Bounds();

    public abstract LightLeSample? SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time);

    public abstract void PDF_Le(in Ray ray, out Float pdfPos, out Float pdfDir);

    // AreaLights only
    public abstract void PDF_Le(in Interaction intr, Vector3f w, out Float pdfPos, out Float pdfDir);
}

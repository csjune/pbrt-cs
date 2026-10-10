// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/medium.h
//
// pbrt's TaggedPointer-based interfaces become abstract classes with the
// same name; concrete types derive from them. A null reference plays the
// role of pbrt's null TaggedPointer.
//
// RayMajorantIterator is an abstract class too; SampleRay() allocates the
// medium's iterator where pbrt uses a ScratchBuffer.

namespace Pbrt;

// PhaseFunctionSample Definition
public struct PhaseFunctionSample
{
    public Float p;
    public Vector3f wi;
    public Float pdf;

    public PhaseFunctionSample(Float p, Vector3f wi, Float pdf)
    {
        this.p = p;
        this.wi = wi;
        this.pdf = pdf;
    }
}

// PhaseFunction Definition
public abstract class PhaseFunction
{
    // PhaseFunction Interface
    public abstract Float p(Vector3f wo, Vector3f wi);

    public abstract PhaseFunctionSample? Sample_p(Vector3f wo, Point2f u);

    public abstract Float PDF(Vector3f wo, Vector3f wi);
}

// RayMajorantSegment Definition
public struct RayMajorantSegment(Float tMin, Float tMax, SampledSpectrum sigma_maj)
{
    public Float tMin = tMin, tMax = tMax;
    public SampledSpectrum sigma_maj = sigma_maj;

    public override readonly string ToString() =>
        $"[ RayMajorantSegment tMin: {FloatToString(tMin)} tMax: {FloatToString(tMax)} sigma_maj: {sigma_maj} ]";
}

// RayMajorantIterator Definition
public abstract class RayMajorantIterator
{
    public abstract RayMajorantSegment? Next();
}

// Medium Definition
public abstract partial class Medium
{
    // Medium Interface
    public abstract bool IsEmissive();

    public abstract MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda);

    // Medium Public Methods
    public abstract RayMajorantIterator SampleRay(Ray ray, Float tMax, in SampledWavelengths lambda);
}

// MediumInterface Definition
public struct MediumInterface
{
    // MediumInterface Public Members
    public Medium? inside, outside;

    // MediumInterface Public Methods
    public MediumInterface(Medium? medium)
    {
        inside = outside = medium;
    }

    public MediumInterface(Medium? inside, Medium? outside)
    {
        this.inside = inside;
        this.outside = outside;
    }

    public readonly bool IsMediumTransition() => inside != outside;

    public override readonly string ToString() =>
        $"[ MediumInterface inside: {inside?.ToString() ?? "(nullptr)"} outside: {outside?.ToString() ?? "(nullptr)"} ]";
}

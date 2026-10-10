// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/sampler.h
//
// pbrt's Sampler TaggedPointer is an abstract class; the samplers are in
// Samplers.cs. pbrt's Clone() copies the sampler object; here it is a
// MemberwiseClone(), which shares the samplers' immutable tables as pbrt's
// copies share their pointers.

namespace Pbrt;

// CameraSample Definition
public struct CameraSample
{
    public Point2f pFilm;
    public Point2f pLens;
    public Float time = 0;
    public Float filterWeight = 1;

    public CameraSample() { }

    public override readonly string ToString() =>
        $"[ CameraSample pFilm: {pFilm} pLens: {pLens} time: {FloatToString(time)} " +
        $"filterWeight: {FloatToString(filterWeight)} ]";
}

// Sampler Definition
public abstract partial class Sampler
{
    // Sampler Interface
    public abstract int SamplesPerPixel();

    public abstract void StartPixelSample(Point2i p, int sampleIndex, int dimension = 0);

    public abstract Float Get1D();
    public abstract Point2f Get2D();

    public abstract Point2f GetPixel2D();

    public virtual Sampler Clone() => (Sampler)MemberwiseClone();
}

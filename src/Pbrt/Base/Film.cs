// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/film.h
//
// pbrt's Film TaggedPointer is an abstract class; the films are in Film.cs.
// AddSample()'s const VisibleSurface pointer is an in parameter: a default
// (unset) VisibleSurface plays the role of the null pointer.

namespace Pbrt;

// Film Definition
public abstract partial class Film
{
    // Film Interface
    public abstract void AddSample(Point2i pFilm, SampledSpectrum L, in SampledWavelengths lambda,
                                   in VisibleSurface visibleSurface, Float weight);

    public abstract Bounds2f SampleBounds();

    public abstract bool UsesVisibleSurface();

    public abstract void AddSplat(Point2f p, SampledSpectrum v, in SampledWavelengths lambda);

    public abstract SampledWavelengths SampleWavelengths(Float u);

    public abstract Point2i FullResolution();
    public abstract Bounds2i PixelBounds();
    public abstract Float Diagonal();

    public abstract void WriteImage(ImageMetadata metadata, Float splatScale = 1);

    public abstract RGB ToOutputRGB(SampledSpectrum L, in SampledWavelengths lambda);

    public abstract Image GetImage(ImageMetadata metadata, Float splatScale = 1);

    public abstract RGB GetPixelRGB(Point2i p, Float splatScale = 1);

    public abstract Filter GetFilter();
    public abstract PixelSensor GetPixelSensor();
    public abstract string GetFilename();

    public abstract void ResetPixel(Point2i p);
}

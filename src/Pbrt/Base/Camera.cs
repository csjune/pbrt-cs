// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/camera.h
//
// pbrt's Camera TaggedPointer is an abstract class. All of pbrt's cameras
// derive from CameraBase (Cameras.cs), so the methods that pbrt forwards to
// CameraBase (GetFilm, SampleTime, GetCameraTransform, Approximate_dp_dxy)
// are implemented there.

namespace Pbrt;

// Camera Definition
public abstract partial class Camera
{
    // Camera Interface
    public abstract CameraRay? GenerateRay(CameraSample sample, ref SampledWavelengths lambda);

    public abstract CameraRayDifferential? GenerateRayDifferential(CameraSample sample,
                                                                   ref SampledWavelengths lambda);

    public abstract Film GetFilm();

    public abstract Float SampleTime(Float u);

    public abstract void InitMetadata(ImageMetadata metadata);

    public abstract CameraTransform GetCameraTransform();

    public abstract void Approximate_dp_dxy(Point3f p, Normal3f n, Float time, int samplesPerPixel,
                                            out Vector3f dpdx, out Vector3f dpdy);

    public abstract SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda);

    public abstract SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda, out Point2f pRasterOut);

    public abstract void PDF_We(in Ray ray, out Float pdfPos, out Float pdfDir);

    public abstract CameraWiSample? SampleWi(in Interaction @ref, Point2f u, ref SampledWavelengths lambda);
}

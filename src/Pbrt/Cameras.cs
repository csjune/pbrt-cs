// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cameras.h and cameras.cpp
//
// pbrt's optional pRaster/pdf/rOut pointer outputs are out parameters (We()
// has an overload without pRaster). RealisticCamera's debugging helpers
// (DrawLensSystem, DrawRayPathFromFilm/Scene) print to stdout as pbrt's do,
// or to the given writer.

namespace Pbrt;

// CameraTransform Definition
public sealed class CameraTransform
{
    // CameraTransform Private Members
    private readonly AnimatedTransform renderFromCamera = new();
    private readonly Transform worldFromRender = new();

    // CameraTransform Public Methods
    public CameraTransform() { }

    public CameraTransform(AnimatedTransform worldFromCamera)
    {
        switch (Options.renderingSpace)
        {
            case RenderingCoordinateSystem.Camera:
            {
                // Compute _worldFromRender_ for camera-space rendering
                Float tMid = (worldFromCamera.startTime + worldFromCamera.endTime) / 2;
                worldFromRender = worldFromCamera.Interpolate(tMid);
                break;
            }
            case RenderingCoordinateSystem.CameraWorld:
            {
                // Compute _worldFromRender_ for camera-world space rendering
                Float tMid = (worldFromCamera.startTime + worldFromCamera.endTime) / 2;
                Point3f pCamera = worldFromCamera.Apply(new Point3f(0, 0, 0), tMid);
                worldFromRender = Translate(new Vector3f(pCamera));
                break;
            }
            case RenderingCoordinateSystem.World:
            {
                // Compute _worldFromRender_ for world-space rendering
                worldFromRender = new Transform();
                break;
            }
            default:
                LOG_FATAL("Unhandled rendering coordinate space");
                break;
        }
        LOG_VERBOSE($"World-space position: {worldFromRender.Apply(new Point3f(0, 0, 0))}");
        // Compute _renderFromCamera_ transformation
        Transform renderFromWorld = Inverse(worldFromRender);
        Transform rfc0 = renderFromWorld * worldFromCamera.startTransform;
        Transform rfc1 = renderFromWorld * worldFromCamera.endTransform;
        renderFromCamera = new AnimatedTransform(rfc0, worldFromCamera.startTime, rfc1, worldFromCamera.endTime);
    }

    public Point3f RenderFromCamera(Point3f p, Float time) => renderFromCamera.Apply(p, time);
    public Point3f CameraFromRender(Point3f p, Float time) => renderFromCamera.ApplyInverse(p, time);
    public Point3f RenderFromWorld(Point3f p) => worldFromRender.ApplyInverse(p);

    public Transform RenderFromWorld() => Inverse(worldFromRender);
    public Transform CameraFromRender(Float time) => Inverse(renderFromCamera.Interpolate(time));
    public Transform CameraFromWorld(Float time) => Inverse(worldFromRender * renderFromCamera.Interpolate(time));

    public bool CameraFromRenderHasScale() => renderFromCamera.HasScale();

    public Vector3f RenderFromCamera(Vector3f v, Float time) => renderFromCamera.Apply(v, time);
    public Normal3f RenderFromCamera(Normal3f n, Float time) => renderFromCamera.Apply(n, time);
    public Ray RenderFromCamera(in Ray r) => renderFromCamera.Apply(r);
    public RayDifferential RenderFromCamera(in RayDifferential r) => renderFromCamera.Apply(r);
    public Vector3f CameraFromRender(Vector3f v, Float time) => renderFromCamera.ApplyInverse(v, time);
    public Normal3f CameraFromRender(Normal3f v, Float time) => renderFromCamera.ApplyInverse(v, time);

    public AnimatedTransform RenderFromCamera() => renderFromCamera;
    public Transform WorldFromRender() => worldFromRender;

    public override string ToString() =>
        $"[ CameraTransform renderFromCamera: {renderFromCamera} worldFromRender: {worldFromRender} ]";
}

// CameraWiSample Definition
public struct CameraWiSample
{
    public SampledSpectrum Wi;
    public Vector3f wi;
    public Float pdf;
    public Point2f pRaster;
    public Interaction pRef, pLens;

    // CameraWiSample Public Methods
    public CameraWiSample(in SampledSpectrum Wi, Vector3f wi, Float pdf, Point2f pRaster, in Interaction pRef,
                          in Interaction pLens)
    {
        this.Wi = Wi;
        this.wi = wi;
        this.pdf = pdf;
        this.pRaster = pRaster;
        this.pRef = pRef;
        this.pLens = pLens;
    }
}

// CameraRay Definition
public struct CameraRay
{
    public Ray ray;
    public SampledSpectrum weight = new(1);

    public CameraRay(in Ray ray) => this.ray = ray;

    public CameraRay(in Ray ray, in SampledSpectrum weight)
    {
        this.ray = ray;
        this.weight = weight;
    }
}

// CameraRayDifferential Definition
public struct CameraRayDifferential
{
    public RayDifferential ray;
    public SampledSpectrum weight = new(1);

    public CameraRayDifferential(in RayDifferential ray) => this.ray = ray;

    public CameraRayDifferential(in RayDifferential ray, in SampledSpectrum weight)
    {
        this.ray = ray;
        this.weight = weight;
    }
}

// CameraBaseParameters Definition
public struct CameraBaseParameters
{
    public CameraTransform cameraTransform = new();
    public Float shutterOpen = 0, shutterClose = 1;
    public Film film = null!;
    public Medium? medium = null;

    public CameraBaseParameters() { }

    // CameraBaseParameters Method Definitions
    public CameraBaseParameters(CameraTransform cameraTransform, Film film, Medium? medium,
                                ParameterDictionary parameters, FileLoc? loc)
    {
        this.cameraTransform = cameraTransform;
        this.film = film;
        this.medium = medium;
        shutterOpen = parameters.GetOneFloat("shutteropen", 0f);
        shutterClose = parameters.GetOneFloat("shutterclose", 1f);
        if (shutterClose < shutterOpen)
        {
            Warning(loc, $"Shutter close time {FloatToString(shutterClose)} < shutter open {FloatToString(shutterOpen)}.  Swapping them.");
            (shutterClose, shutterOpen) = (shutterOpen, shutterClose);
        }
    }
}

public abstract partial class Camera
{
    public static Camera Create(string name, ParameterDictionary parameters, Medium? medium,
                                CameraTransform cameraTransform, Film film, FileLoc? loc)
    {
        Camera? camera = null;
        if (name == "perspective")
            camera = PerspectiveCamera.Create(parameters, cameraTransform, film, medium, loc);
        else if (name == "orthographic")
            camera = OrthographicCamera.Create(parameters, cameraTransform, film, medium, loc);
        else if (name == "realistic")
            camera = RealisticCamera.Create(parameters, cameraTransform, film, medium, loc);
        else if (name == "spherical")
            camera = SphericalCamera.Create(parameters, cameraTransform, film, medium, loc);
        else
            ErrorExit(loc, $"{name}: camera type unknown.");

        if (camera == null)
            ErrorExit(loc, $"{name}: unable to create camera.");

        parameters.ReportUnused();
        return camera!;
    }

    // The screen window that the projective cameras' Create() methods share.
    private protected static Bounds2f ScreenWindow(ParameterDictionary parameters, Film film, FileLoc? loc,
                                                   bool errorHasLoc = true)
    {
        Float frame = parameters.GetOneFloat("frameaspectratio",
                                             (Float)film.FullResolution().x / (Float)film.FullResolution().y);
        Bounds2f screen = new();
        if (frame > 1f)
        {
            screen.pMin.x = -frame;
            screen.pMax.x = frame;
            screen.pMin.y = -1f;
            screen.pMax.y = 1f;
        }
        else
        {
            screen.pMin.x = -1f;
            screen.pMax.x = 1f;
            screen.pMin.y = -1f / frame;
            screen.pMax.y = 1f / frame;
        }
        List<Float> sw = parameters.GetFloatArray("screenwindow");
        if (sw.Count != 0)
        {
            if (Options.fullscreen)
                Warning("\"screenwindow\" is ignored in fullscreen mode");
            else
            {
                if (sw.Count == 4)
                {
                    screen.pMin.x = sw[0];
                    screen.pMax.x = sw[1];
                    screen.pMin.y = sw[2];
                    screen.pMax.y = sw[3];
                }
                else
                    Error(errorHasLoc ? loc : null, "\"screenwindow\" should have four values");
            }
        }
        return screen;
    }
}

// CameraBase Definition
public abstract class CameraBase : Camera
{
    // CameraBase Protected Members
    protected CameraTransform cameraTransform;
    protected Float shutterOpen, shutterClose;
    protected Film film;
    protected Medium? medium;
    protected Vector3f minPosDifferentialX, minPosDifferentialY;
    protected Vector3f minDirDifferentialX, minDirDifferentialY;

    // CameraBase Public Methods
    public override Film GetFilm() => film;
    public override CameraTransform GetCameraTransform() => cameraTransform;

    public override Float SampleTime(Float u) => Lerp(u, shutterOpen, shutterClose);

    public override void InitMetadata(ImageMetadata metadata)
    {
        metadata.cameraFromWorld = cameraTransform.CameraFromWorld(shutterOpen).GetMatrix();
    }

    public override string ToString() =>
        $"cameraTransform: {cameraTransform} shutterOpen: {FloatToString(shutterOpen)} " +
        $"shutterClose: {FloatToString(shutterClose)} film: {film} " +
        $"medium: {medium?.ToString() ?? "(nullptr)"} minPosDifferentialX: {minPosDifferentialX} " +
        $"minPosDifferentialY: {minPosDifferentialY} minDirDifferentialX: {minDirDifferentialX} " +
        $"minDirDifferentialY: {minDirDifferentialY} ";

    public override void Approximate_dp_dxy(Point3f p, Normal3f n, Float time, int samplesPerPixel,
                                            out Vector3f dpdx, out Vector3f dpdy)
    {
        // Compute tangent plane equation for ray differential intersections
        Point3f pCamera = CameraFromRender(p, time);
        Transform DownZFromCamera = RotateFromTo(Normalize(new Vector3f(pCamera)), new Vector3f(0, 0, 1));
        Point3f pDownZ = DownZFromCamera.Apply(pCamera);
        Normal3f nDownZ = DownZFromCamera.Apply(CameraFromRender(n, time));
        Float d = nDownZ.z * pDownZ.z;

        // Find intersection points for approximated camera differential rays
        Ray xRay = new(new Point3f(0, 0, 0) + minPosDifferentialX, new Vector3f(0, 0, 1) + minDirDifferentialX);
        Float tx = -(Dot(nDownZ, new Vector3f(xRay.o)) - d) / Dot(nDownZ, xRay.d);
        Ray yRay = new(new Point3f(0, 0, 0) + minPosDifferentialY, new Vector3f(0, 0, 1) + minDirDifferentialY);
        Float ty = -(Dot(nDownZ, new Vector3f(yRay.o)) - d) / Dot(nDownZ, yRay.d);
        Point3f px = xRay.At(tx), py = yRay.At(ty);

        // Estimate $\dpdx$ and $\dpdy$ in tangent plane at intersection point
        Float sppScale = GetOptions().disablePixelJitter
                             ? 1
                             : FMath.Max(.125f, 1 / FMath.Sqrt((Float)samplesPerPixel));
        dpdx = sppScale * RenderFromCamera(DownZFromCamera.ApplyInverse(px - pDownZ), time);
        dpdy = sppScale * RenderFromCamera(DownZFromCamera.ApplyInverse(py - pDownZ), time);
    }

    public override SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda) => We(ray, ref lambda, out _);

    // CameraBase Protected Methods
    // CameraBase Method Definitions
    protected CameraBase(CameraBaseParameters p)
    {
        cameraTransform = p.cameraTransform;
        shutterOpen = p.shutterOpen;
        shutterClose = p.shutterClose;
        film = p.film;
        medium = p.medium;
        if (cameraTransform.CameraFromRenderHasScale())
            Warning("Scaling detected in rendering space to camera space transformation!\n" +
                    "The system has numerous assumptions, implicit and explicit,\n" +
                    "that this transform will have no scale factors in it.\n" +
                    "Proceed at your own risk; your image may have errors or\n" +
                    "the system may crash as a result of this.");
    }

    protected static CameraRayDifferential? GenerateRayDifferential(Camera camera, CameraSample sample,
                                                                    ref SampledWavelengths lambda)
    {
        // Generate regular camera ray _cr_ for ray differential
        CameraRay? ocr = camera.GenerateRay(sample, ref lambda);
        if (ocr is not { } cr)
            return null;
        RayDifferential rd = new(cr.ray);

        // Find camera ray after shifting one pixel in the $x$ direction
        CameraRay? rx = null;
        foreach (Float eps in (ReadOnlySpan<Float>)[.05f, -.05f])
        {
            CameraSample sshift = sample;
            sshift.pFilm.x += eps;
            // Try to generate ray with _sshift_ and compute $x$ differential
            if ((rx = camera.GenerateRay(sshift, ref lambda)) is { } r)
            {
                rd.rxOrigin = rd.o + (r.ray.o - rd.o) / eps;
                rd.rxDirection = rd.d + (r.ray.d - rd.d) / eps;
                break;
            }
        }

        // Find camera ray after shifting one pixel in the $y$ direction
        CameraRay? ry = null;
        foreach (Float eps in (ReadOnlySpan<Float>)[.05f, -.05f])
        {
            CameraSample sshift = sample;
            sshift.pFilm.y += eps;
            if ((ry = camera.GenerateRay(sshift, ref lambda)) is { } r)
            {
                rd.ryOrigin = rd.o + (r.ray.o - rd.o) / eps;
                rd.ryDirection = rd.d + (r.ray.d - rd.d) / eps;
                break;
            }
        }

        // Return approximate ray differential and weight
        rd.hasDifferentials = rx.HasValue && ry.HasValue;
        return new CameraRayDifferential(rd, cr.weight);
    }

    protected Ray RenderFromCamera(in Ray r) => cameraTransform.RenderFromCamera(r);
    protected RayDifferential RenderFromCamera(in RayDifferential r) => cameraTransform.RenderFromCamera(r);
    protected Vector3f RenderFromCamera(Vector3f v, Float time) => cameraTransform.RenderFromCamera(v, time);
    protected Normal3f RenderFromCamera(Normal3f v, Float time) => cameraTransform.RenderFromCamera(v, time);
    protected Point3f RenderFromCamera(Point3f p, Float time) => cameraTransform.RenderFromCamera(p, time);
    protected Vector3f CameraFromRender(Vector3f v, Float time) => cameraTransform.CameraFromRender(v, time);
    protected Normal3f CameraFromRender(Normal3f v, Float time) => cameraTransform.CameraFromRender(v, time);
    protected Point3f CameraFromRender(Point3f p, Float time) => cameraTransform.CameraFromRender(p, time);

    protected void FindMinimumDifferentials(Camera camera)
    {
        minPosDifferentialX = minPosDifferentialY = minDirDifferentialX = minDirDifferentialY =
            new Vector3f(Infinity, Infinity, Infinity);

        CameraSample sample = new() { pLens = new Point2f(0.5f, 0.5f), time = 0.5f };
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

        int n = 512;
        for (int i = 0; i < n; ++i)
        {
            sample.pFilm.x = (Float)i / (n - 1) * film.FullResolution().x;
            sample.pFilm.y = (Float)i / (n - 1) * film.FullResolution().y;

            CameraRayDifferential? crd = camera.GenerateRayDifferential(sample, ref lambda);
            if (crd is not { } c)
                continue;

            RayDifferential ray = c.ray;
            Vector3f dox = CameraFromRender(ray.rxOrigin - ray.o, ray.time);
            if (Length(dox) < Length(minPosDifferentialX))
                minPosDifferentialX = dox;
            Vector3f doy = CameraFromRender(ray.ryOrigin - ray.o, ray.time);
            if (Length(doy) < Length(minPosDifferentialY))
                minPosDifferentialY = doy;

            ray.d = Normalize(ray.d);
            ray.rxDirection = Normalize(ray.rxDirection);
            ray.ryDirection = Normalize(ray.ryDirection);

            Frame f = Frame.FromZ(ray.d);
            Vector3f df = f.ToLocal(ray.d);  // should be (0, 0, 1);
            Vector3f dxf = Normalize(f.ToLocal(ray.rxDirection));
            Vector3f dyf = Normalize(f.ToLocal(ray.ryDirection));

            if (Length(dxf - df) < Length(minDirDifferentialX))
                minDirDifferentialX = dxf - df;
            if (Length(dyf - df) < Length(minDirDifferentialY))
                minDirDifferentialY = dyf - df;
        }

        LOG_VERBOSE($"Camera min pos differentials: {minPosDifferentialX}, {minPosDifferentialY}");
        LOG_VERBOSE($"Camera min dir differentials: {minDirDifferentialX}, {minDirDifferentialY}");
    }
}

// ProjectiveCamera Definition
public abstract class ProjectiveCamera : CameraBase
{
    // ProjectiveCamera Protected Members
    protected Transform screenFromCamera, cameraFromRaster;
    protected Transform rasterFromScreen, screenFromRaster;
    protected Float lensRadius, focalDistance;

    // ProjectiveCamera Public Methods
    // ProjectiveCamera Method Definitions
    public override void InitMetadata(ImageMetadata metadata)
    {
        metadata.cameraFromWorld = cameraTransform.CameraFromWorld(shutterOpen).GetMatrix();

        // TODO: double check this
        Transform NDCFromWorld = Translate(new Vector3f(0.5f, 0.5f, 0.5f)) * Scale(0.5f, 0.5f, 0.5f) *
                                 screenFromCamera * new Transform(metadata.cameraFromWorld.Value);
        metadata.NDCFromWorld = NDCFromWorld.GetMatrix();

        base.InitMetadata(metadata);
    }

    public string BaseToString() =>
        base.ToString() +
        $"screenFromCamera: {screenFromCamera} cameraFromRaster: {cameraFromRaster} " +
        $"rasterFromScreen: {rasterFromScreen} screenFromRaster: {screenFromRaster} " +
        $"lensRadius: {FloatToString(lensRadius)} focalDistance: {FloatToString(focalDistance)}";

    protected ProjectiveCamera(CameraBaseParameters baseParameters, Transform screenFromCamera,
                               Bounds2f screenWindow, Float lensRadius, Float focalDistance)
        : base(baseParameters)
    {
        this.screenFromCamera = screenFromCamera;
        this.lensRadius = lensRadius;
        this.focalDistance = focalDistance;
        // Compute projective camera transformations
        // Compute projective camera screen transformations
        Transform NDCFromScreen =
            Scale(1 / (screenWindow.pMax.x - screenWindow.pMin.x), 1 / (screenWindow.pMax.y - screenWindow.pMin.y), 1) *
            Translate(new Vector3f(-screenWindow.pMin.x, -screenWindow.pMax.y, 0));
        Transform rasterFromNDC = Scale(film.FullResolution().x, -film.FullResolution().y, 1);
        rasterFromScreen = rasterFromNDC * NDCFromScreen;
        screenFromRaster = Inverse(rasterFromScreen);

        cameraFromRaster = Inverse(screenFromCamera) * screenFromRaster;
    }
}

// OrthographicCamera Definition
public sealed class OrthographicCamera : ProjectiveCamera
{
    // OrthographicCamera Private Members
    private readonly Vector3f dxCamera, dyCamera;

    // OrthographicCamera Public Methods
    public OrthographicCamera(CameraBaseParameters baseParameters, Bounds2f screenWindow, Float lensRadius,
                              Float focalDist)
        : base(baseParameters, Orthographic(0, 1), screenWindow, lensRadius, focalDist)
    {
        // Compute differential changes in origin for orthographic camera rays
        dxCamera = cameraFromRaster.Apply(new Vector3f(1, 0, 0));
        dyCamera = cameraFromRaster.Apply(new Vector3f(0, 1, 0));

        // Compute minimum differentials for orthographic camera
        minDirDifferentialX = minDirDifferentialY = new Vector3f(0, 0, 0);
        minPosDifferentialX = dxCamera;
        minPosDifferentialY = dyCamera;
    }

    // OrthographicCamera Method Definitions
    public override CameraRay? GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Compute raster and camera sample positions
        Point3f pFilm = new(sample.pFilm.x, sample.pFilm.y, 0);
        Point3f pCamera = cameraFromRaster.Apply(pFilm);

        Ray ray = new(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time), medium);
        // Modify ray for depth of field
        if (lensRadius > 0)
        {
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            // Compute point on plane of focus
            Float ft = focalDistance / ray.d.z;
            Point3f pFocus = ray.At(ft);

            // Update ray for effect of lens
            ray.o = new Point3f(pLens.x, pLens.y, 0);
            ray.d = Normalize(pFocus - ray.o);
        }

        return new CameraRay(RenderFromCamera(ray));
    }

    public override CameraRayDifferential? GenerateRayDifferential(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Compute main orthographic viewing ray
        // Compute raster and camera sample positions
        Point3f pFilm = new(sample.pFilm.x, sample.pFilm.y, 0);
        Point3f pCamera = cameraFromRaster.Apply(pFilm);

        RayDifferential ray = new(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time), medium);
        // Modify ray for depth of field
        if (lensRadius > 0)
        {
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            // Compute point on plane of focus
            Float ft = focalDistance / ray.d.z;
            Point3f pFocus = ray.At(ft);

            // Update ray for effect of lens
            ray.o = new Point3f(pLens.x, pLens.y, 0);
            ray.d = Normalize(pFocus - ray.o);
        }

        // Compute ray differentials for _OrthographicCamera_
        if (lensRadius > 0)
        {
            // Compute _OrthographicCamera_ ray differentials accounting for lens
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            Float ft = focalDistance / ray.d.z;
            Point3f pFocus = pCamera + dxCamera + (ft * new Vector3f(0, 0, 1));
            ray.rxOrigin = new Point3f(pLens.x, pLens.y, 0);
            ray.rxDirection = Normalize(pFocus - ray.rxOrigin);

            pFocus = pCamera + dyCamera + (ft * new Vector3f(0, 0, 1));
            ray.ryOrigin = new Point3f(pLens.x, pLens.y, 0);
            ray.ryDirection = Normalize(pFocus - ray.ryOrigin);
        }
        else
        {
            ray.rxOrigin = ray.o + dxCamera;
            ray.ryOrigin = ray.o + dyCamera;
            ray.rxDirection = ray.ryDirection = ray.d;
        }

        ray.hasDifferentials = true;
        return new CameraRayDifferential(RenderFromCamera(ray));
    }

    public static OrthographicCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform,
                                            Film film, Medium? medium, FileLoc? loc)
    {
        CameraBaseParameters cameraBaseParameters = new(cameraTransform, film, medium, parameters, loc);

        Float lensradius = parameters.GetOneFloat("lensradius", 0f);
        Float focaldistance = parameters.GetOneFloat("focaldistance", 1e6f);
        // (pbrt reports a bad "screenwindow" without the file location here)
        Bounds2f screen = ScreenWindow(parameters, film, loc, errorHasLoc: false);
        return new OrthographicCamera(cameraBaseParameters, screen, lensradius, focaldistance);
    }

    public override SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda, out Point2f pRaster2)
    {
        LOG_FATAL("We() unimplemented for OrthographicCamera");
        pRaster2 = default;
        return default;
    }

    public override void PDF_We(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        LOG_FATAL("PDF_We() unimplemented for OrthographicCamera");
        pdfPos = pdfDir = 0;
    }

    public override CameraWiSample? SampleWi(in Interaction @ref, Point2f u, ref SampledWavelengths lambda)
    {
        LOG_FATAL("SampleWi() unimplemented for OrthographicCamera");
        return null;
    }

    public override string ToString() =>
        $"[ OrthographicCamera {BaseToString()} dxCamera: {dxCamera} dyCamera: {dyCamera} ]";
}

// PerspectiveCamera Definition
public sealed class PerspectiveCamera : ProjectiveCamera
{
    // PerspectiveCamera Private Members
    private readonly Vector3f dxCamera, dyCamera;
    private readonly Float cosTotalWidth;
    private readonly Float A;

    // PerspectiveCamera Public Methods
    public PerspectiveCamera(CameraBaseParameters baseParameters, Float fov, Bounds2f screenWindow,
                             Float lensRadius, Float focalDist)
        : base(baseParameters, Perspective(fov, 1e-2f, 1000f), screenWindow, lensRadius, focalDist)
    {
        // Compute differential changes in origin for perspective camera rays
        dxCamera = cameraFromRaster.Apply(new Point3f(1, 0, 0)) - cameraFromRaster.Apply(new Point3f(0, 0, 0));
        dyCamera = cameraFromRaster.Apply(new Point3f(0, 1, 0)) - cameraFromRaster.Apply(new Point3f(0, 0, 0));

        // Compute _cosTotalWidth_ for perspective camera
        Point2f radius = new(film.GetFilter().Radius());
        Point3f pCorner = new(-radius.x, -radius.y, 0f);
        Vector3f wCornerCamera = Normalize(new Vector3f(cameraFromRaster.Apply(pCorner)));
        cosTotalWidth = wCornerCamera.z;
        DCHECK_LT(.9999 * cosTotalWidth, (double)FMath.Cos(Radians(fov / 2)));

        // Compute image plane area at $z=1$ for _PerspectiveCamera_
        Point2i res = film.FullResolution();
        Point3f pMin = cameraFromRaster.Apply(new Point3f(0, 0, 0));
        Point3f pMax = cameraFromRaster.Apply(new Point3f(res.x, res.y, 0));
        pMin /= pMin.z;
        pMax /= pMax.z;
        A = FMath.Abs((pMax.x - pMin.x) * (pMax.y - pMin.y));

        // Compute minimum differentials for _PerspectiveCamera_
        FindMinimumDifferentials(this);
    }

    // PerspectiveCamera Method Definitions
    public override CameraRay? GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Compute raster and camera sample positions
        Point3f pFilm = new(sample.pFilm.x, sample.pFilm.y, 0);
        Point3f pCamera = cameraFromRaster.Apply(pFilm);

        Ray ray = new(new Point3f(0, 0, 0), Normalize(new Vector3f(pCamera)), SampleTime(sample.time), medium);
        // Modify ray for depth of field
        if (lensRadius > 0)
        {
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            // Compute point on plane of focus
            Float ft = focalDistance / ray.d.z;
            Point3f pFocus = ray.At(ft);

            // Update ray for effect of lens
            ray.o = new Point3f(pLens.x, pLens.y, 0);
            ray.d = Normalize(pFocus - ray.o);
        }

        return new CameraRay(RenderFromCamera(ray));
    }

    public override CameraRayDifferential? GenerateRayDifferential(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Compute raster and camera sample positions
        Point3f pFilm = new(sample.pFilm.x, sample.pFilm.y, 0);
        Point3f pCamera = cameraFromRaster.Apply(pFilm);
        Vector3f dir = Normalize(new Vector3f(pCamera.x, pCamera.y, pCamera.z));
        RayDifferential ray = new(new Point3f(0, 0, 0), dir, SampleTime(sample.time), medium);
        // Modify ray for depth of field
        if (lensRadius > 0)
        {
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            // Compute point on plane of focus
            Float ft = focalDistance / ray.d.z;
            Point3f pFocus = ray.At(ft);

            // Update ray for effect of lens
            ray.o = new Point3f(pLens.x, pLens.y, 0);
            ray.d = Normalize(pFocus - ray.o);
        }

        // Compute offset rays for _PerspectiveCamera_ ray differentials
        if (lensRadius > 0)
        {
            // Compute _PerspectiveCamera_ ray differentials accounting for lens
            // Sample point on lens
            Point2f pLens = lensRadius * SampleUniformDiskConcentric(sample.pLens);

            // Compute $x$ ray differential for _PerspectiveCamera_ with lens
            Vector3f dx = Normalize(new Vector3f(pCamera + dxCamera));
            Float ft = focalDistance / dx.z;
            Point3f pFocus = new Point3f(0, 0, 0) + (ft * dx);
            ray.rxOrigin = new Point3f(pLens.x, pLens.y, 0);
            ray.rxDirection = Normalize(pFocus - ray.rxOrigin);

            // Compute $y$ ray differential for _PerspectiveCamera_ with lens
            Vector3f dy = Normalize(new Vector3f(pCamera + dyCamera));
            ft = focalDistance / dy.z;
            pFocus = new Point3f(0, 0, 0) + (ft * dy);
            ray.ryOrigin = new Point3f(pLens.x, pLens.y, 0);
            ray.ryDirection = Normalize(pFocus - ray.ryOrigin);
        }
        else
        {
            ray.rxOrigin = ray.ryOrigin = ray.o;
            ray.rxDirection = Normalize(new Vector3f(pCamera) + dxCamera);
            ray.ryDirection = Normalize(new Vector3f(pCamera) + dyCamera);
        }

        ray.hasDifferentials = true;
        return new CameraRayDifferential(RenderFromCamera(ray));
    }

    public override string ToString() =>
        $"[ PerspectiveCamera {BaseToString()} dxCamera: {dxCamera} dyCamera: {dyCamera} " +
        $"A: {FloatToString(A)} cosTotalWidth: {FloatToString(cosTotalWidth)} ]";

    public static PerspectiveCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform,
                                           Film film, Medium? medium, FileLoc? loc)
    {
        CameraBaseParameters cameraBaseParameters = new(cameraTransform, film, medium, parameters, loc);

        Float lensradius = parameters.GetOneFloat("lensradius", 0f);
        Float focaldistance = parameters.GetOneFloat("focaldistance", 1e6f);
        Bounds2f screen = ScreenWindow(parameters, film, loc);
        Float fov = parameters.GetOneFloat("fov", 90f);
        return new PerspectiveCamera(cameraBaseParameters, fov, screen, lensradius, focaldistance);
    }

    public override SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda, out Point2f pRasterOut)
    {
        pRasterOut = default;
        // Check if ray is forward-facing with respect to the camera
        Float cosTheta = Dot(ray.d, RenderFromCamera(new Vector3f(0, 0, 1), ray.time));
        if (cosTheta <= cosTotalWidth)
            return new SampledSpectrum(0f);

        // Map ray $(\p{}, \w{})$ onto the raster grid
        Point3f pFocus = ray.At((lensRadius > 0 ? focalDistance : 1) / cosTheta);
        Point3f pCamera = CameraFromRender(pFocus, ray.time);
        Point3f pRaster = cameraFromRaster.ApplyInverse(pCamera);

        // Return raster position if requested
        pRasterOut = new Point2f(pRaster.x, pRaster.y);

        // Return zero importance for out of bounds points
        Bounds2f sampleBounds = film.SampleBounds();
        if (!Inside(new Point2f(pRaster.x, pRaster.y), sampleBounds))
            return new SampledSpectrum(0f);

        // Compute lens area of perspective camera
        Float lensArea = lensRadius != 0 ? (Pi * Sqr(lensRadius)) : 1;

        // Return importance for point on image plane
        return new SampledSpectrum(1 / (A * lensArea * Pow(cosTheta, 4)));
    }

    public override void PDF_We(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        // Return zero PDF values if ray direction is not front-facing
        Float cosTheta = Dot(ray.d, RenderFromCamera(new Vector3f(0, 0, 1), ray.time));
        if (cosTheta <= cosTotalWidth)
        {
            pdfPos = pdfDir = 0;
            return;
        }

        // Map ray $(\p{}, \w{})$ onto the raster grid
        Point3f pFocus = ray.At((lensRadius > 0 ? focalDistance : 1) / cosTheta);
        Point3f pCamera = CameraFromRender(pFocus, ray.time);
        Point3f pRaster = cameraFromRaster.ApplyInverse(pCamera);

        // Return zero probability for out of bounds points
        Bounds2f sampleBounds = film.SampleBounds();
        if (!Inside(new Point2f(pRaster.x, pRaster.y), sampleBounds))
        {
            pdfPos = pdfDir = 0;
            return;
        }

        // Compute lens area  and return perspective camera probabilities
        Float lensArea = lensRadius != 0 ? (Pi * Sqr(lensRadius)) : 1;
        pdfPos = 1 / lensArea;
        pdfDir = 1 / (A * Pow(cosTheta, 3));
    }

    public override CameraWiSample? SampleWi(in Interaction @ref, Point2f u, ref SampledWavelengths lambda)
    {
        // Uniformly sample a lens interaction _lensIntr_
        Point2f pLens = lensRadius * SampleUniformDiskConcentric(u);
        Point3f pLensRender = RenderFromCamera(new Point3f(pLens.x, pLens.y, 0), @ref.time);
        Normal3f n = new(RenderFromCamera(new Vector3f(0, 0, 1), @ref.time));
        Interaction lensIntr = new(pLensRender, n, @ref.time, medium);

        // Find incident direction to camera _wi_ at _ref_
        Vector3f wi = lensIntr.p() - @ref.p();
        Float dist = Length(wi);
        wi /= dist;

        // Compute PDF for importance arriving at _ref_
        Float lensArea = lensRadius != 0 ? (Pi * Sqr(lensRadius)) : 1;
        Float pdf = Sqr(dist) / (AbsDot(lensIntr.n, wi) * lensArea);

        // Compute importance and return _CameraWiSample_
        SampledSpectrum Wi = We(lensIntr.SpawnRay(-wi), ref lambda, out Point2f pRaster);
        if (!Wi)
            return null;
        return new CameraWiSample(Wi, wi, pdf, pRaster, @ref, lensIntr);
    }
}

// SphericalCamera Definition
public sealed class SphericalCamera : CameraBase
{
    // SphericalCamera::Mapping Definition
    public enum Mapping { EquiRectangular, EqualArea }

    // SphericalCamera Private Members
    private readonly Mapping mapping;

    // SphericalCamera Public Methods
    public SphericalCamera(CameraBaseParameters baseParameters, Mapping mapping) : base(baseParameters)
    {
        this.mapping = mapping;
        // Compute minimum differentials for _SphericalCamera_
        FindMinimumDifferentials(this);
    }

    // SphericalCamera Method Definitions
    public override CameraRay? GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Compute spherical camera ray direction
        Point2f uv = new(sample.pFilm.x / film.FullResolution().x, sample.pFilm.y / film.FullResolution().y);
        Vector3f dir;
        if (mapping == Mapping.EquiRectangular)
        {
            // Compute ray direction using equirectangular mapping
            Float theta = Pi * uv[1], phi = 2 * Pi * uv[0];
            dir = SphericalDirection(FMath.Sin(theta), FMath.Cos(theta), phi);
        }
        else
        {
            // Compute ray direction using equal area mapping
            uv = WrapEqualAreaSquare(uv);
            dir = EqualAreaSquareToSphere(uv);
        }
        (dir.y, dir.z) = (dir.z, dir.y);

        Ray ray = new(new Point3f(0, 0, 0), dir, SampleTime(sample.time), medium);
        return new CameraRay(RenderFromCamera(ray));
    }

    public override CameraRayDifferential? GenerateRayDifferential(CameraSample sample, ref SampledWavelengths lambda) =>
        GenerateRayDifferential(this, sample, ref lambda);

    public static SphericalCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform,
                                         Film film, Medium? medium, FileLoc? loc)
    {
        CameraBaseParameters cameraBaseParameters = new(cameraTransform, film, medium, parameters, loc);

        Float lensradius = parameters.GetOneFloat("lensradius", 0f);
        Float focaldistance = parameters.GetOneFloat("focaldistance", 1e30f);
        Bounds2f screen = ScreenWindow(parameters, film, loc);
        _ = lensradius;     // don't need this
        _ = focaldistance;  // don't need this
        _ = screen;

        string m = parameters.GetOneString("mapping", "equalarea");
        Mapping mapping = Mapping.EqualArea;
        if (m == "equalarea")
            mapping = Mapping.EqualArea;
        else if (m == "equirectangular")
            mapping = Mapping.EquiRectangular;
        else
            ErrorExit(loc, $"{m}: unknown mapping for spherical camera. (Must be \"equalarea\" or \"equirectangular\".)");

        return new SphericalCamera(cameraBaseParameters, mapping);
    }

    public override SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda, out Point2f pRaster2)
    {
        LOG_FATAL("We() unimplemented for SphericalCamera");
        pRaster2 = default;
        return default;
    }

    public override void PDF_We(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        LOG_FATAL("PDF_We() unimplemented for SphericalCamera");
        pdfPos = pdfDir = 0;
    }

    public override CameraWiSample? SampleWi(in Interaction @ref, Point2f u, ref SampledWavelengths lambda)
    {
        LOG_FATAL("SampleWi() unimplemented for SphericalCamera");
        return null;
    }

    public override string ToString() =>
        $"[ SphericalCamera {base.ToString()} mapping: {(mapping == Mapping.EquiRectangular ? "EquiRectangular" : "EqualArea")} ]";
}

// ExitPupilSample Definition
public struct ExitPupilSample
{
    public Point3f pPupil;
    public Float pdf;
}

// RealisticCamera Definition
public sealed class RealisticCamera : CameraBase
{
    private static readonly StatPercent vignettedRays = new("Camera/Rays vignetted by lens system");

    // RealisticCamera Private Declarations
    private struct LensElementInterface
    {
        public Float curvatureRadius;
        public Float thickness;
        public Float eta;
        public Float apertureRadius;

        public override readonly string ToString() =>
            $"[ LensElementInterface curvatureRadius: {FloatToString(curvatureRadius)} " +
            $"thickness: {FloatToString(thickness)} eta: {FloatToString(eta)} " +
            $"apertureRadius: {FloatToString(apertureRadius)} ]";
    }

    // RealisticCamera Private Members
    private readonly Bounds2f physicalExtent;
    private readonly LensElementInterface[] elementInterfaces;
    private readonly Image apertureImage;
    private readonly Bounds2f[] exitPupilBounds;

    // RealisticCamera Method Definitions
    public RealisticCamera(CameraBaseParameters baseParameters, IReadOnlyList<Float> lensParameters,
                           Float focusDistance, Float setApertureDiameter, Image apertureImage)
        : base(baseParameters)
    {
        this.apertureImage = apertureImage;
        // Compute film's physical extent
        Float aspect = (Float)film.FullResolution().y / (Float)film.FullResolution().x;
        Float diagonal = film.Diagonal();
        Float x = FMath.Sqrt(Sqr(diagonal) / (1 + Sqr(aspect)));
        Float y = aspect * x;
        physicalExtent = new Bounds2f(new Point2f(-x / 2, -y / 2), new Point2f(x / 2, y / 2));

        // Initialize _elementInterfaces_ for camera
        List<LensElementInterface> interfaces = [];
        for (int i = 0; i < lensParameters.Count; i += 4)
        {
            // Extract lens element configuration from _lensParameters_
            Float curvatureRadius = lensParameters[i] / 1000;
            Float thickness = lensParameters[i + 1] / 1000;
            Float eta = lensParameters[i + 2];
            Float apertureDiameter = lensParameters[i + 3] / 1000;

            if (curvatureRadius == 0)
            {
                // Set aperture stop diameter
                setApertureDiameter /= 1000;
                if (setApertureDiameter > apertureDiameter)
                    Warning($"Aperture diameter {FloatToString(setApertureDiameter)} is greater than maximum possible {FloatToString(apertureDiameter)}. Clamping it.");
                else
                    apertureDiameter = setApertureDiameter;
            }
            // Add element interface to end of _elementInterfaces_
            interfaces.Add(new LensElementInterface
            {
                curvatureRadius = curvatureRadius, thickness = thickness, eta = eta,
                apertureRadius = apertureDiameter / 2
            });
        }
        elementInterfaces = [.. interfaces];

        // Compute lens--film distance for given focus distance
        elementInterfaces[^1].thickness = FocusThickLens(focusDistance);

        // Compute exit pupil bounds at sampled points on the film
        int nSamples = 64;
        exitPupilBounds = new Bounds2f[nSamples];
        ParallelFor(0, nSamples, (long i) =>
        {
            Float r0 = (Float)i / nSamples * film.Diagonal() / 2;
            Float r1 = (Float)(i + 1) / nSamples * film.Diagonal() / 2;
            exitPupilBounds[i] = BoundExitPupil(r0, r1);
        });

        // Compute minimum differentials for _RealisticCamera_
        FindMinimumDifferentials(this);
    }

    // RealisticCamera Private Methods
    private Float LensRearZ() => elementInterfaces[^1].thickness;

    private Float LensFrontZ()
    {
        Float zSum = 0;
        foreach (LensElementInterface element in elementInterfaces)
            zSum += element.thickness;
        return zSum;
    }

    private Float RearElementRadius() => elementInterfaces[^1].apertureRadius;

    private static bool IntersectSphericalElement(Float radius, Float zCenter, in Ray ray, out Float t,
                                                  out Normal3f n)
    {
        t = 0;
        n = default;
        // Compute _t0_ and _t1_ for ray--element intersection
        Point3f o = ray.o - new Vector3f(0, 0, zCenter);
        Float A = ray.d.x * ray.d.x + ray.d.y * ray.d.y + ray.d.z * ray.d.z;
        Float B = 2 * (ray.d.x * o.x + ray.d.y * o.y + ray.d.z * o.z);
        Float C = o.x * o.x + o.y * o.y + o.z * o.z - radius * radius;
        if (!Quadratic(A, B, C, out Float t0, out Float t1))
            return false;

        // Select intersection $t$ based on ray direction and element curvature
        bool useCloserT = (ray.d.z > 0) ^ (radius < 0);
        t = useCloserT ? FMath.Min(t0, t1) : FMath.Max(t0, t1);
        if (t < 0)
            return false;

        // Compute surface normal of element at ray intersection point
        n = new Normal3f(new Vector3f(o + t * ray.d));
        n = FaceForward(Normalize(n), -ray.d);

        return true;
    }

    private Float TraceLensesFromFilm(in Ray rCamera) => TraceLensesFromFilm(rCamera, out _, false);

    private Float TraceLensesFromFilm(in Ray rCamera, out Ray rOut, bool wantOut = true)
    {
        rOut = default;
        Float elementZ = 0, weight = 1;
        // Transform _rCamera_ from camera to lens system space
        Ray rLens = new(new Point3f(rCamera.o.x, rCamera.o.y, -rCamera.o.z),
                        new Vector3f(rCamera.d.x, rCamera.d.y, -rCamera.d.z), rCamera.time);
        for (int i = elementInterfaces.Length - 1; i >= 0; --i)
        {
            ref readonly LensElementInterface element = ref elementInterfaces[i];
            // Update ray from film accounting for interaction with _element_
            elementZ -= element.thickness;
            // Compute intersection of ray with lens element
            Float t;
            Normal3f n = default;
            bool isStop = (element.curvatureRadius == 0);
            if (isStop)
            {
                // Compute _t_ at plane of aperture stop
                t = (elementZ - rLens.o.z) / rLens.d.z;
                if (t < 0)
                    return 0;
            }
            else
            {
                // Intersect ray with element to compute _t_ and _n_
                Float radius = element.curvatureRadius;
                Float zCenter = elementZ + element.curvatureRadius;
                if (!IntersectSphericalElement(radius, zCenter, rLens, out t, out n))
                    return 0;
            }
            DCHECK_GE(t, 0f);

            // Test intersection point against element aperture
            Point3f pHit = rLens.At(t);
            if (isStop && (bool)apertureImage)
            {
                // Check intersection point against _apertureImage_
                Point2f uv = new((pHit.x / element.apertureRadius + 1) / 2, (pHit.y / element.apertureRadius + 1) / 2);
                weight = apertureImage.BilerpChannel(uv, 0, WrapMode.Black);
                if (weight == 0)
                    return 0;
            }
            else
            {
                // Check intersection point against spherical aperture
                if (Sqr(pHit.x) + Sqr(pHit.y) > Sqr(element.apertureRadius))
                    return 0;
            }
            rLens.o = pHit;

            // Update ray path for element interface interaction
            if (!isStop)
            {
                Float eta_i = element.eta;
                Float eta_t = (i > 0 && elementInterfaces[i - 1].eta != 0) ? elementInterfaces[i - 1].eta : 1;
                if (!Refract(Normalize(-rLens.d), n, eta_t / eta_i, out Vector3f w))
                    return 0;
                rLens.d = w;
            }
        }
        // Transform lens system space ray back to camera space
        if (wantOut)
            rOut = new Ray(new Point3f(rLens.o.x, rLens.o.y, -rLens.o.z),
                           new Vector3f(rLens.d.x, rLens.d.y, -rLens.d.z), rLens.time);

        return weight;
    }

    private static void ComputeCardinalPoints(Ray rIn, Ray rOut, out Float pz, out Float fz)
    {
        Float tf = -rOut.o.x / rOut.d.x;
        fz = -rOut.At(tf).z;
        Float tp = (rIn.o.x - rOut.o.x) / rOut.d.x;
        pz = -rOut.At(tp).z;
    }

    private void ComputeThickLensApproximation(Span<Float> pz, Span<Float> fz)
    {
        // Find height $x$ from optical axis for parallel rays
        Float x = .001f * film.Diagonal();

        // Compute cardinal points for film side of lens system
        Ray rScene = new(new Point3f(x, 0, LensFrontZ() + 1), new Vector3f(0, 0, -1));
        if (TraceLensesFromScene(rScene, out Ray rFilm) == 0)
            ErrorExit("Unable to trace ray from scene to film for thick lens " +
                      "approximation. Is aperture stop extremely small?");
        ComputeCardinalPoints(rScene, rFilm, out pz[0], out fz[0]);

        // Compute cardinal points for scene side of lens system
        rFilm = new Ray(new Point3f(x, 0, LensRearZ() - 1), new Vector3f(0, 0, 1));
        if (TraceLensesFromFilm(rFilm, out rScene) == 0)
            ErrorExit("Unable to trace ray from film to scene for thick lens " +
                      "approximation. Is aperture stop extremely small?");
        ComputeCardinalPoints(rFilm, rScene, out pz[1], out fz[1]);
    }

    private Float FocusThickLens(Float focusDistance)
    {
        Span<Float> pz = stackalloc Float[2], fz = stackalloc Float[2];
        ComputeThickLensApproximation(pz, fz);
        LOG_VERBOSE($"Cardinal points: p' = {FloatToString(pz[0])} f' = {FloatToString(fz[0])}, p = {FloatToString(pz[1])} f = {FloatToString(fz[1])}.\n");
        LOG_VERBOSE($"Effective focal length {FloatToString(fz[0] - pz[0])}\n");
        // Compute translation of lens, _delta_, to focus at _focusDistance_
        Float f = fz[0] - pz[0];
        Float z = -focusDistance;
        Float c = (pz[1] - z - pz[0]) * (pz[1] - z - 4 * f - pz[0]);
        if (c <= 0)
            ErrorExit($"Coefficient must be positive. It looks focusDistance {FloatToString(focusDistance)}  is too short for a given lenses configuration");
        Float delta = (pz[1] - z + pz[0] - FMath.Sqrt(c)) / 2;

        return elementInterfaces[^1].thickness + delta;
    }

    private Bounds2f BoundExitPupil(Float filmX0, Float filmX1)
    {
        Bounds2f pupilBounds = new();
        // Sample a collection of points on the rear lens to find exit pupil
        const int nSamples = 1024 * 1024;
        // Compute bounding box of projection of rear element on sampling plane
        Float rearRadius = RearElementRadius();
        Bounds2f projRearBounds = new(new Point2f(-1.5f * rearRadius, -1.5f * rearRadius),
                                      new Point2f(1.5f * rearRadius, 1.5f * rearRadius));

        for (int i = 0; i < nSamples; ++i)
        {
            // Find location of sample points on $x$ segment and rear lens element
            Point3f pFilm = new(Lerp((i + 0.5f) / nSamples, filmX0, filmX1), 0, 0);
            Float u0 = RadicalInverse(0, (ulong)i), u1 = RadicalInverse(1, (ulong)i);
            Point3f pRear = new(Lerp(u0, projRearBounds.pMin.x, projRearBounds.pMax.x),
                                Lerp(u1, projRearBounds.pMin.y, projRearBounds.pMax.y), LensRearZ());

            // Expand pupil bounds if ray makes it through the lens system
            if (!Inside(new Point2f(pRear.x, pRear.y), pupilBounds) &&
                TraceLensesFromFilm(new Ray(pFilm, pRear - pFilm)) != 0)
                pupilBounds = Union(pupilBounds, new Point2f(pRear.x, pRear.y));
        }

        // Return degenerate bounds if no rays made it through the lens system
        if (pupilBounds.IsDegenerate())
        {
            LOG_VERBOSE($"Unable to find exit pupil in x = [{FloatToString(filmX0)},{FloatToString(filmX1)}] on film.");
            return pupilBounds;
        }

        // Expand bounds to account for sample spacing
        pupilBounds = Expand(pupilBounds, (Float)(2 * Length(projRearBounds.Diagonal()) / Math.Sqrt(nSamples)));

        return pupilBounds;
    }

    private ExitPupilSample? SampleExitPupil(Point2f pFilm, Point2f uLens)
    {
        // Find exit pupil bound for sample distance from film center
        Float rFilm = FMath.Sqrt(Sqr(pFilm.x) + Sqr(pFilm.y));
        int rIndex = (int)(rFilm / (film.Diagonal() / 2) * exitPupilBounds.Length);
        rIndex = Math.Min(exitPupilBounds.Length - 1, rIndex);
        Bounds2f pupilBounds = exitPupilBounds[rIndex];
        if (pupilBounds.IsDegenerate())
            return null;

        // Generate sample point inside exit pupil bound
        Point2f pLens = pupilBounds.Lerp(uLens);
        Float pdf = 1 / pupilBounds.Area();

        // Return sample point rotated by angle of _pFilm_ with $+x$ axis
        Float sinTheta = (rFilm != 0) ? pFilm.y / rFilm : 0;
        Float cosTheta = (rFilm != 0) ? pFilm.x / rFilm : 1;
        Point3f pPupil = new(cosTheta * pLens.x - sinTheta * pLens.y, sinTheta * pLens.x + cosTheta * pLens.y,
                             LensRearZ());
        return new ExitPupilSample { pPupil = pPupil, pdf = pdf };
    }

    public override CameraRay? GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
    {
        // Find point on film, _pFilm_, corresponding to _sample.pFilm_
        Point2f s = new(sample.pFilm.x / film.FullResolution().x, sample.pFilm.y / film.FullResolution().y);
        Point2f pFilm2 = physicalExtent.Lerp(s);
        Point3f pFilm = new(-pFilm2.x, pFilm2.y, 0);

        // Trace ray from _pFilm_ through lens system
        ExitPupilSample? oeps = SampleExitPupil(new Point2f(pFilm.x, pFilm.y), sample.pLens);
        if (oeps is not { } eps)
            return null;
        Ray rFilm = new(pFilm, eps.pPupil - pFilm);
        Float weight = TraceLensesFromFilm(rFilm, out Ray ray);
        if (weight == 0)
            return null;

        // Finish initialization of _RealisticCamera_ ray
        ray.time = SampleTime(sample.time);
        ray.medium = medium;
        ray = RenderFromCamera(ray);
        ray.d = Normalize(ray.d);

        // Compute weighting for _RealisticCamera_ ray
        Float cosTheta = Normalize(rFilm.d).z;
        weight *= Pow(cosTheta, 4) / (eps.pdf * Sqr(LensRearZ()));

        return new CameraRay(ray, new SampledSpectrum(weight));
    }

    public override CameraRayDifferential? GenerateRayDifferential(CameraSample sample, ref SampledWavelengths lambda) =>
        GenerateRayDifferential(this, sample, ref lambda);

    private Float TraceLensesFromScene(in Ray rCamera, out Ray rOut)
    {
        rOut = default;
        Float elementZ = -LensFrontZ();
        // Transform _rCamera_ from camera to lens system space
        Transform LensFromCamera = Scale(1, 1, -1);
        Ray rLens = LensFromCamera.Apply(rCamera);
        for (int i = 0; i < elementInterfaces.Length; ++i)
        {
            ref readonly LensElementInterface element = ref elementInterfaces[i];
            // Compute intersection of ray with lens element
            Float t;
            Normal3f n = default;
            bool isStop = (element.curvatureRadius == 0);
            if (isStop)
            {
                t = (elementZ - rLens.o.z) / rLens.d.z;
                if (t < 0)
                    return 0;
            }
            else
            {
                Float radius = element.curvatureRadius;
                Float zCenter = elementZ + element.curvatureRadius;
                if (!IntersectSphericalElement(radius, zCenter, rLens, out t, out n))
                    return 0;
            }

            // Test intersection point against element aperture
            // Don't worry about the aperture image here.
            Point3f pHit = rLens.At(t);
            Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
            if (r2 > element.apertureRadius * element.apertureRadius)
                return 0;
            rLens.o = pHit;

            // Update ray path for from-scene element interface interaction
            if (!isStop)
            {
                Float eta_i = (i == 0 || elementInterfaces[i - 1].eta == 0) ? 1 : elementInterfaces[i - 1].eta;
                Float eta_t = (elementInterfaces[i].eta != 0) ? elementInterfaces[i].eta : 1;
                if (!Refract(Normalize(-rLens.d), n, eta_t / eta_i, out Vector3f wt))
                    return 0;
                rLens.d = wt;
            }
            elementZ += element.thickness;
        }
        // Transform _rLens_ from lens system space back to camera space
        rOut = new Ray(new Point3f(rLens.o.x, rLens.o.y, -rLens.o.z),
                       new Vector3f(rLens.d.x, rLens.d.y, -rLens.d.z), rLens.time);
        return 1;
    }

    public override SampledSpectrum We(in Ray ray, ref SampledWavelengths lambda, out Point2f pRaster2)
    {
        LOG_FATAL("We() unimplemented for RealisticCamera");
        pRaster2 = default;
        return default;
    }

    public override void PDF_We(in Ray ray, out Float pdfPos, out Float pdfDir)
    {
        LOG_FATAL("PDF_We() unimplemented for RealisticCamera");
        pdfPos = pdfDir = 0;
    }

    public override CameraWiSample? SampleWi(in Interaction @ref, Point2f u, ref SampledWavelengths lambda)
    {
        LOG_FATAL("SampleWi() unimplemented for RealisticCamera");
        return null;
    }

    // RealisticCamera debugging methods. (printf()'s %f is six decimals.)
    private static string F(Float v) => ((double)v).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

    public void DrawLensSystem(TextWriter? output = null)
    {
        TextWriter o = output ?? Console.Out;
        Float sumz = -LensFrontZ();
        Float z = sumz;
        for (int i = 0; i < elementInterfaces.Length; ++i)
        {
            LensElementInterface element = elementInterfaces[i];
            Float r = element.curvatureRadius;
            if (r == 0)
            {
                // stop
                o.Write($"{{Thick, Line[{{{{{F(z)}, {F(element.apertureRadius)}}}, {{{F(z)}, {F(2 * element.apertureRadius)}}}}}], ");
                o.Write($"Line[{{{{{F(z)}, {F(-element.apertureRadius)}}}, {{{F(z)}, {F(-2 * element.apertureRadius)}}}}}]}}, ");
            }
            else
            {
                Float theta = FMath.Abs(SafeASin(element.apertureRadius / r));
                if (r > 0)
                {
                    // convex as seen from front of lens
                    Float t0 = Pi - theta;
                    Float t1 = Pi + theta;
                    o.Write($"Circle[{{{F(z + r)}, 0}}, {F(r)}, {{{F(t0)}, {F(t1)}}}], ");
                }
                else
                {
                    // concave as seen from front of lens
                    Float t0 = -theta;
                    Float t1 = theta;
                    o.Write($"Circle[{{{F(z + r)}, 0}}, {F(-r)}, {{{F(t0)}, {F(t1)}}}], ");
                }
                if (element.eta != 0 && element.eta != 1)
                {
                    // connect top/bottom to next element
                    CHECK_LT(i + 1, elementInterfaces.Length);
                    Float nextApertureRadius = elementInterfaces[i + 1].apertureRadius;
                    Float h = Max(element.apertureRadius, nextApertureRadius);
                    Float hlow = Min(element.apertureRadius, nextApertureRadius);

                    Float zp0, zp1;
                    if (r > 0)
                        zp0 = z + element.curvatureRadius - element.apertureRadius / FMath.Tan(theta);
                    else
                        zp0 = z + element.curvatureRadius + element.apertureRadius / FMath.Tan(theta);

                    Float nextCurvatureRadius = elementInterfaces[i + 1].curvatureRadius;
                    Float nextTheta = FMath.Abs(SafeASin(nextApertureRadius / nextCurvatureRadius));
                    if (nextCurvatureRadius > 0)
                        zp1 = z + element.thickness + nextCurvatureRadius - nextApertureRadius / FMath.Tan(nextTheta);
                    else
                        zp1 = z + element.thickness + nextCurvatureRadius + nextApertureRadius / FMath.Tan(nextTheta);

                    // Connect tops
                    o.Write($"Line[{{{{{F(zp0)}, {F(h)}}}, {{{F(zp1)}, {F(h)}}}}}], ");
                    o.Write($"Line[{{{{{F(zp0)}, {F(-h)}}}, {{{F(zp1)}, {F(-h)}}}}}], ");

                    // vertical lines when needed to close up the element profile
                    if (element.apertureRadius < nextApertureRadius)
                    {
                        o.Write($"Line[{{{{{F(zp0)}, {F(h)}}}, {{{F(zp0)}, {F(hlow)}}}}}], ");
                        o.Write($"Line[{{{{{F(zp0)}, {F(-h)}}}, {{{F(zp0)}, {F(-hlow)}}}}}], ");
                    }
                    else if (element.apertureRadius > nextApertureRadius)
                    {
                        o.Write($"Line[{{{{{F(zp1)}, {F(h)}}}, {{{F(zp1)}, {F(hlow)}}}}}], ");
                        o.Write($"Line[{{{{{F(zp1)}, {F(-h)}}}, {{{F(zp1)}, {F(-hlow)}}}}}], ");
                    }
                }
            }
            z += element.thickness;
        }

        // 24mm height for 35mm film
        o.Write("Line[{{0, -.012}, {0, .012}}], ");
        // optical axis
        o.Write($"Line[{{{{0, 0}}, {{{F(1.2f * sumz)}, 0}}}}] ");
    }

    private static readonly Transform LensFromCamera = Scale(1, 1, -1);

    public void DrawRayPathFromFilm(in Ray r, bool arrow, bool toOpticalIntercept, TextWriter? output = null)
    {
        TextWriter o = output ?? Console.Out;
        Float elementZ = 0;
        // Transform _ray_ from camera to lens system space
        Ray ray = LensFromCamera.Apply(r);
        o.Write("{ ");
        if (TraceLensesFromFilm(r) == 0)
            o.Write("Dashed, RGBColor[.8, .5, .5]");
        else
            o.Write("RGBColor[.5, .5, .8]");

        for (int i = elementInterfaces.Length - 1; i >= 0; --i)
        {
            LensElementInterface element = elementInterfaces[i];
            elementZ -= element.thickness;
            bool isStop = element.curvatureRadius == 0;
            // Compute intersection of ray with lens element
            Float t;
            Normal3f n = new();
            if (isStop)
                t = -(ray.o.z - elementZ) / ray.d.z;
            else
            {
                Float radius = element.curvatureRadius;
                Float zCenter = elementZ + element.curvatureRadius;
                if (!IntersectSphericalElement(radius, zCenter, ray, out t, out n))
                {
                    o.Write("}");
                    return;
                }
            }
            CHECK_GE(t, 0);

            o.Write($", Line[{{{{{F(ray.o.z)}, {F(ray.o.x)}}}, {{{F(ray.At(t).z)}, {F(ray.At(t).x)}}}}}]");

            // Test intersection point against element aperture
            Point3f pHit = ray.At(t);
            Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
            Float apertureRadius2 = element.apertureRadius * element.apertureRadius;
            if (r2 > apertureRadius2)
            {
                o.Write("}");
                return;
            }
            ray.o = pHit;

            // Update ray path for element interface interaction
            if (!isStop)
            {
                Float eta_i = element.eta;
                Float eta_t = (i > 0 && elementInterfaces[i - 1].eta != 0) ? elementInterfaces[i - 1].eta : 1;
                if (!Refract(Normalize(-ray.d), n, eta_t / eta_i, out Vector3f wt))
                {
                    o.Write("}");
                    return;
                }
                ray.d = wt;
            }
        }

        ray.d = Normalize(ray.d);
        {
            Float ta = FMath.Abs(elementZ / 4);
            if (toOpticalIntercept)
            {
                ta = -ray.o.x / ray.d.x;
                o.Write($", Point[{{{F(ray.At(ta).z)}, {F(ray.At(ta).x)}}}]");
            }
            o.Write($", {(arrow ? "Arrow" : "Line")}[{{{{{F(ray.o.z)}, {F(ray.o.x)}}}, {{{F(ray.At(ta).z)}, {F(ray.At(ta).x)}}}}}]");

            // overdraw the optical axis if needed...
            if (toOpticalIntercept)
                o.Write($", Line[{{{{{F(ray.o.z)}, 0}}, {{{F(ray.At(ta).z * 1.05f)}, 0}}}}]");
        }

        o.Write("}");
    }

    public void DrawRayPathFromScene(in Ray r, bool arrow, bool toOpticalIntercept, TextWriter? output = null)
    {
        TextWriter o = output ?? Console.Out;
        Float elementZ = LensFrontZ() * -1;

        // Transform _ray_ from camera to lens system space
        Ray ray = LensFromCamera.Apply(r);
        for (int i = 0; i < elementInterfaces.Length; ++i)
        {
            LensElementInterface element = elementInterfaces[i];
            bool isStop = element.curvatureRadius == 0;
            // Compute intersection of ray with lens element
            Float t;
            Normal3f n = new();
            if (isStop)
                t = -(ray.o.z - elementZ) / ray.d.z;
            else
            {
                Float radius = element.curvatureRadius;
                Float zCenter = elementZ + element.curvatureRadius;
                if (!IntersectSphericalElement(radius, zCenter, ray, out t, out n))
                    return;
            }
            CHECK_GE(t, 0f);

            o.Write($"Line[{{{{{F(ray.o.z)}, {F(ray.o.x)}}}, {{{F(ray.At(t).z)}, {F(ray.At(t).x)}}}}}],");

            // Test intersection point against element aperture
            Point3f pHit = ray.At(t);
            Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
            Float apertureRadius2 = element.apertureRadius * element.apertureRadius;
            if (r2 > apertureRadius2)
                return;
            ray.o = pHit;

            // Update ray path for from-scene element interface interaction
            if (!isStop)
            {
                Float eta_i = (i == 0 || elementInterfaces[i - 1].eta == 0f) ? 1f : elementInterfaces[i - 1].eta;
                Float eta_t = (elementInterfaces[i].eta != 0f) ? elementInterfaces[i].eta : 1f;
                if (!Refract(Normalize(-ray.d), n, eta_t / eta_i, out Vector3f wt))
                    return;
                ray.d = wt;
            }
            elementZ += element.thickness;
        }

        // go to the film plane by default
        {
            Float ta = -ray.o.z / ray.d.z;
            if (toOpticalIntercept)
            {
                ta = -ray.o.x / ray.d.x;
                o.Write($"Point[{{{F(ray.At(ta).z)}, {F(ray.At(ta).x)}}}], ");
            }
            o.Write($"{(arrow ? "Arrow" : "Line")}[{{{{{F(ray.o.z)}, {F(ray.o.x)}}}, {{{F(ray.At(ta).z)}, {F(ray.At(ta).x)}}}}}]");
        }
    }

    public void RenderExitPupil(Float sx, Float sy, string filename)
    {
        Point3f pFilm = new(sx, sy, 0);

        const int nSamples = 2048;
        Image image = new(PixelFormat.Float, new Point2i(nSamples, nSamples), ["Y"]);

        for (int y = 0; y < nSamples; ++y)
        {
            Float fy = (Float)y / (Float)(nSamples - 1);
            Float ly = Lerp(fy, -RearElementRadius(), RearElementRadius());
            for (int x = 0; x < nSamples; ++x)
            {
                Float fx = (Float)x / (Float)(nSamples - 1);
                Float lx = Lerp(fx, -RearElementRadius(), RearElementRadius());

                Point3f pRear = new(lx, ly, LensRearZ());

                if (lx * lx + ly * ly > RearElementRadius() * RearElementRadius())
                    image.SetChannel(new Point2i(x, y), 0, 1f);
                else if (TraceLensesFromFilm(new Ray(pFilm, pRear - pFilm)) != 0)
                    image.SetChannel(new Point2i(x, y), 0, 0.5f);
                else
                    image.SetChannel(new Point2i(x, y), 0, 0f);
            }
        }

        image.Write(filename);
    }

    // (pbrt's function-static RNG)
    private static RNG testExitPupilRng = new();

    public void TestExitPupilBounds()
    {
        Float filmDiagonal = film.Diagonal();

        Float u = testExitPupilRng.Uniform<Float>();
        Point3f pFilm = new(u * filmDiagonal / 2, 0, 0);

        Float r = pFilm.x / (filmDiagonal / 2);
        int pupilIndex = Math.Min(exitPupilBounds.Length - 1, (int)FMath.Floor(r * (exitPupilBounds.Length - 1)));
        Bounds2f pupilBounds = exitPupilBounds[pupilIndex];
        if (pupilIndex + 1 < exitPupilBounds.Length)
            pupilBounds = Union(pupilBounds, exitPupilBounds[pupilIndex + 1]);

        // Now, randomly pick points on the aperture and see if any are outside
        // of pupil bounds...
        for (int i = 0; i < 1000; ++i)
        {
            Float u0 = testExitPupilRng.Uniform<Float>();
            Point2f u2 = new(u0, testExitPupilRng.Uniform<Float>());
            Point2f pd = SampleUniformDiskConcentric(u2);
            pd *= RearElementRadius();

            Ray testRay = new(pFilm, new Point3f(pd.x, pd.y, 0f) - pFilm);
            if (TraceLensesFromFilm(testRay, out _) == 0)
                continue;

            if (!Inside(pd, pupilBounds))
            {
                Console.Error.Write($"Aha! ({F(pd.x)},{F(pd.y)}) went through, but outside bounds " +
                                    $"({F(pupilBounds.pMin[0])},{F(pupilBounds.pMin[1])}) - " +
                                    $"({F(pupilBounds.pMax[0])},{F(pupilBounds.pMax[1])})\n");
                RenderExitPupil((Float)pupilIndex / exitPupilBounds.Length * filmDiagonal / 2f, 0f, "low.exr");
                RenderExitPupil((Float)(pupilIndex + 1) / exitPupilBounds.Length * filmDiagonal / 2f, 0f,
                                "high.exr");
                RenderExitPupil(pFilm.x, 0f, "mid.exr");
                Environment.Exit(0);
            }
        }
        Console.Error.Write(".");
    }

    public override string ToString() =>
        $"[ RealisticCamera {base.ToString()} elementInterfaces: [ {string.Join(", ", elementInterfaces)} ] " +
        $"exitPupilBounds: [ {string.Join(", ", exitPupilBounds)} ] ]";

    public static RealisticCamera? Create(ParameterDictionary parameters, CameraTransform cameraTransform,
                                          Film film, Medium? medium, FileLoc? loc)
    {
        CameraBaseParameters cameraBaseParameters = new(cameraTransform, film, medium, parameters, loc);

        // Realistic camera-specific parameters
        string lensFile = ResolveFilename(parameters.GetOneString("lensfile", ""));
        Float apertureDiameter = parameters.GetOneFloat("aperturediameter", 1.0f);
        Float focusDistance = parameters.GetOneFloat("focusdistance", 10.0f);

        if (lensFile.Length == 0)
        {
            Error(loc, "No lens description file supplied!");
            return null;
        }
        // Load element data from lens description file
        List<Float> lensParameters = ReadFloatFile(lensFile);
        if (lensParameters.Count == 0)
        {
            Error(loc, $"Error reading lens specification file \"{lensFile}\".");
            return null;
        }
        if (lensParameters.Count % 4 != 0)
        {
            Error(loc, $"{lensFile}: excess values in lens specification file; must be multiple-of-four values, read {lensParameters.Count}.");
            return null;
        }

        int builtinRes = 256;
        Image Rasterize(ReadOnlySpan<Point2f> vert)
        {
            Image image = new(PixelFormat.Float, new Point2i(builtinRes, builtinRes), ["Y"]);

            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Point2f p = new(-1 + 2 * (x + 0.5f) / image.Resolution().x,
                                    -1 + 2 * (y + 0.5f) / image.Resolution().y);
                    int windingNumber = 0;
                    // Test against edges
                    for (int i = 0; i < vert.Length; ++i)
                    {
                        int i1 = (i + 1) % vert.Length;
                        Float e = (p[0] - vert[i][0]) * (vert[i1][1] - vert[i][1]) -
                                  (p[1] - vert[i][1]) * (vert[i1][0] - vert[i][0]);
                        if (vert[i].y <= p.y)
                        {
                            if (vert[i1].y > p.y && e > 0)
                                ++windingNumber;
                        }
                        else if (vert[i1].y <= p.y && e < 0)
                            --windingNumber;
                    }

                    image.SetChannel(new Point2i(x, y), 0, windingNumber == 0 ? 0f : 1f);
                }

            return image;
        }

        string apertureName = parameters.GetOneString("aperture", "");
        Image apertureImage = new();
        if (apertureName.Length != 0)
        {
            // built-in diaphragm shapes
            if (apertureName == "gaussian")
            {
                apertureImage = new Image(PixelFormat.Float, new Point2i(builtinRes, builtinRes), ["Y"]);
                for (int y = 0; y < apertureImage.Resolution().y; ++y)
                    for (int x = 0; x < apertureImage.Resolution().x; ++x)
                    {
                        Point2f uv = new(-1 + 2 * (x + 0.5f) / apertureImage.Resolution().x,
                                         -1 + 2 * (y + 0.5f) / apertureImage.Resolution().y);
                        Float r2 = Sqr(uv.x) + Sqr(uv.y);
                        Float sigma2 = 1;
                        Float v = FMath.Max(0, FMath.Exp(-r2 / sigma2) - FMath.Exp(-1 / sigma2));
                        apertureImage.SetChannel(new Point2i(x, y), 0, v);
                    }
            }
            else if (apertureName == "square")
            {
                apertureImage = new Image(PixelFormat.Float, new Point2i(builtinRes, builtinRes), ["Y"]);
                for (int y = (int)(.25 * builtinRes); y < .75 * builtinRes; ++y)
                    for (int x = (int)(.25 * builtinRes); x < .75 * builtinRes; ++x)
                        apertureImage.SetChannel(new Point2i(x, y), 0, 4f);
            }
            else if (apertureName == "pentagon")
            {
                // https://mathworld.wolfram.com/RegularPentagon.html
                Float c1 = (MathF.Sqrt(5f) - 1) / 4;
                Float c2 = (MathF.Sqrt(5f) + 1) / 4;
                Float s1 = MathF.Sqrt(10f + 2f * MathF.Sqrt(5f)) / 4;
                Float s2 = MathF.Sqrt(10f - 2f * MathF.Sqrt(5f)) / 4;
                // Vertices in CW order.
                Point2f[] vert = [new(0, 1), new(s1, c1), new(s2, -c2), new(-s2, -c2), new(-s1, c1)];
                // Scale down slightly
                for (int i = 0; i < 5; ++i)
                    vert[i] *= .8f;
                apertureImage = Rasterize(vert);
            }
            else if (apertureName == "star")
            {
                // 5-sided. Vertices are two pentagons--inner and outer radius
                Point2f[] vert = new Point2f[10];
                for (int i = 0; i < 10; ++i)
                {
                    // inner radius: https://math.stackexchange.com/a/2136996
                    Float r = (i & 1) != 0 ? 1f : (FMath.Cos(Radians(72f)) / FMath.Cos(Radians(36f)));
                    vert[i] = new Point2f(r * FMath.Cos(Pi * i / 5f), r * FMath.Sin(Pi * i / 5f));
                }
                Array.Reverse(vert);
                apertureImage = Rasterize(vert);
            }
            else
            {
                ImageAndMetadata im = Image.Read(ResolveFilename(apertureName));
                apertureImage = im.image;
                if (apertureImage.NChannels() > 1)
                {
                    ImageChannelDesc rgbDesc = apertureImage.GetChannelDesc(["R", "G", "B"]);
                    if (!rgbDesc)
                        ErrorExit($"{apertureName}: didn't find R, G, B channels to average for aperture image.");

                    Image mono = new(PixelFormat.Float, apertureImage.Resolution(), ["Y"]);
                    for (int y = 0; y < mono.Resolution().y; ++y)
                        for (int x = 0; x < mono.Resolution().x; ++x)
                        {
                            Float avg = apertureImage.GetChannels(new Point2i(x, y), rgbDesc).Average();
                            mono.SetChannel(new Point2i(x, y), 0, avg);
                        }

                    apertureImage = mono;
                }
            }

            if (apertureImage)
            {
                apertureImage.FlipY();

                // Normalize it so that brightness matches a circular aperture
                Float sum = 0;
                for (int y = 0; y < apertureImage.Resolution().y; ++y)
                    for (int x = 0; x < apertureImage.Resolution().x; ++x)
                        sum += apertureImage.GetChannel(new Point2i(x, y), 0);
                Float avg = sum / (apertureImage.Resolution().x * apertureImage.Resolution().y);

                Float scale = (Pi / 4) / avg;
                for (int y = 0; y < apertureImage.Resolution().y; ++y)
                    for (int x = 0; x < apertureImage.Resolution().x; ++x)
                        apertureImage.SetChannel(new Point2i(x, y), 0,
                                                 apertureImage.GetChannel(new Point2i(x, y), 0) * scale);
            }
        }

        return new RealisticCamera(cameraBaseParameters, lensParameters, focusDistance, apertureDiameter,
                                   apertureImage);
    }
}

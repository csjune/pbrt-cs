// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// pbrt has no tests for the cameras, so these check basic properties of
// the generated rays. (Bit-for-bit agreement with pbrt was checked
// separately against a pbrt build.)

namespace Pbrt.Tests;

public class CamerasTests
{
    private static Film TestFilm(Point2i res) =>
        new RGBFilm(new FilmBaseParameters(res, new Bounds2i(new Point2i(0, 0), res), new BoxFilter(), 35,
                                           PixelSensor.CreateDefault(), "test.exr"),
                    RGBColorSpace.sRGB);

    private static ParameterDictionary Params(params (string type, string name, object value)[] ps)
    {
        List<ParsedParameter> list = [];
        foreach (var (type, name, value) in ps)
        {
            ParsedParameter p = new(new FileLoc()) { type = type, name = name };
            if (value is float f)
                p.floats.Add(f);
            else if (value is double d)
                p.floats.Add((Float)d);
            else
                p.strings.Add((string)value);
            list.Add(p);
        }
        return new ParameterDictionary(list, RGBColorSpace.sRGB);
    }

    private static Camera MakeCamera(string name, ParameterDictionary parameters, Point2i res,
                                     Transform? worldFromCamera = null) =>
        Camera.Create(name, parameters, null, new CameraTransform(new AnimatedTransform(worldFromCamera ?? new Transform())),
                      TestFilm(res), new FileLoc());

    private static CameraSample Sample(Float x, Float y) =>
        new() { pFilm = new Point2f(x, y), pLens = new Point2f(0.5f, 0.5f), time = 0.5f };

    [Fact]
    public void PerspectiveCenterAndCorners()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Point2i res = new(200, 100);
        Camera camera = MakeCamera("perspective", Params(("float", "fov", 60f)), res);

        // The center of the film looks down +z.
        CameraRay cr = camera.GenerateRay(Sample(100, 50), ref lambda)!.Value;
        Assert.True(Distance(new Point3f(cr.ray.d), new Point3f(0, 0, 1)) < 1e-6f);
        Assert.Equal(1, cr.weight[0]);

        // The fov applies to the shorter image axis: the top edge is 30
        // degrees from the axis; +y in raster space is down in the image.
        CameraRay top = camera.GenerateRay(Sample(100, 0), ref lambda)!.Value;
        Assert.Equal(30, Degrees(FMath.Acos(top.ray.d.z)), 1e-3f);
        Assert.True(top.ray.d.y > 0);
        CameraRay left = camera.GenerateRay(Sample(0, 50), ref lambda)!.Value;
        Assert.True(left.ray.d.x < 0);
        Assert.Equal(FMath.Tan(Radians(30)) * 2, -left.ray.d.x / left.ray.d.z, 1e-4f);
    }

    [Fact]
    public void WorldFromCamera()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Transform worldFromCamera = Inverse(LookAt(new Point3f(1, 2, 3), new Point3f(1, 2, 10), new Vector3f(0, 1, 0)));
        Camera camera = MakeCamera("perspective", Params(), new Point2i(64, 64), worldFromCamera);
        CameraTransform ct = camera.GetCameraTransform();
        CameraRay cr = camera.GenerateRay(Sample(32, 32), ref lambda)!.Value;
        // The ray starts at the camera position (in world space) and looks at +z.
        Point3f oWorld = ct.WorldFromRender().Apply(cr.ray.o);
        Assert.True(Distance(oWorld, new Point3f(1, 2, 3)) < 1e-5f);
        Assert.True(Distance(new Point3f(ct.WorldFromRender().Apply(cr.ray.d)), new Point3f(0, 0, 1)) < 1e-5f);
    }

    [Fact]
    public void PerspectiveImportance()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Point2i res = new(64, 48);
        foreach (Float lensRadius in (Float[])[0, 0.1f])
        {
            Camera camera = MakeCamera("perspective",
                                       Params(("float", "fov", 45f), ("float", "lensradius", lensRadius),
                                              ("float", "focaldistance", 5f)), res);
            RNG rng = new(1);
            int nFound = 0;
            for (int i = 0; i < 200; ++i)
            {
                // A point in front of the camera
                Point3f p = new(Lerp(rng.Uniform<Float>(), -2, 2), Lerp(rng.Uniform<Float>(), -2, 2),
                                Lerp(rng.Uniform<Float>(), 2, 10));
                Interaction intr = new(p, 0.5f, (Medium?)null);
                CameraWiSample? ows = camera.SampleWi(intr, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                                                      ref lambda);
                if (ows is not { } ws)
                    continue;
                ++nFound;
                Assert.True(ws.pdf > 0);
                Assert.True(Inside(ws.pRaster, TestFilm(res).SampleBounds()));
                // The ray from the lens toward the point gets the same importance.
                Ray r = ws.pLens.SpawnRay(-ws.wi);
                SampledSpectrum We = camera.We(r, ref lambda, out Point2f pRaster);
                Assert.Equal(ws.Wi[0], We[0]);
                Assert.Equal(ws.pRaster, pRaster);
                camera.PDF_We(r, out Float pdfPos, out Float pdfDir);
                Assert.True(pdfPos > 0 && pdfDir > 0);
            }
            Assert.True(nFound > 20);
        }
    }

    [Fact]
    public void Orthographic()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Camera camera = MakeCamera("orthographic", Params(), new Point2i(100, 100));
        CameraRay a = camera.GenerateRay(Sample(10, 20), ref lambda)!.Value;
        CameraRay b = camera.GenerateRay(Sample(80, 70), ref lambda)!.Value;
        Assert.Equal(a.ray.d, b.ray.d);
        Assert.True(Distance(new Point3f(a.ray.d), new Point3f(0, 0, 1)) < 1e-6f);
        // The screen window is [-1,1]^2, so the image is 2 units wide; +y in
        // raster space is down.
        Assert.Equal(2 * 70f / 100, b.ray.o.x - a.ray.o.x, 1e-4f);
        Assert.Equal(-2 * 50f / 100, b.ray.o.y - a.ray.o.y, 1e-4f);

        CameraRayDifferential rd = camera.GenerateRayDifferential(Sample(10, 20), ref lambda)!.Value;
        Assert.True(rd.ray.hasDifferentials);
        Assert.Equal(2f / 100, Distance(rd.ray.rxOrigin, rd.ray.o), 1e-5f);
        Assert.Equal(rd.ray.d, rd.ray.rxDirection);
    }

    [Theory]
    [InlineData("equalarea")]
    [InlineData("equirectangular")]
    public void Spherical(string mapping)
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Point2i res = new(64, 32);
        Camera camera = MakeCamera("spherical", Params(("string", "mapping", mapping)), res);
        RNG rng = new(3);
        Vector3f sum = new(0, 0, 0);
        int n = 2000;
        for (int i = 0; i < n; ++i)
        {
            CameraSample s = Sample(rng.Uniform<Float>() * res.x, rng.Uniform<Float>() * res.y);
            CameraRay cr = camera.GenerateRay(s, ref lambda)!.Value;
            Assert.Equal(1, Length(cr.ray.d), 1e-5f);
            sum += cr.ray.d;

            CameraRayDifferential rd = camera.GenerateRayDifferential(s, ref lambda)!.Value;
            Assert.Equal(cr.ray.d, rd.ray.d);
            Assert.True(rd.ray.hasDifferentials);
        }
        if (mapping == "equalarea")
            // Uniform over the sphere
            Assert.True(Length(sum / n) < 0.1f);
        else
        {
            // The top row looks up (+y after pbrt's swap of y and z).
            CameraRay top = camera.GenerateRay(Sample(10, 0), ref lambda)!.Value;
            Assert.True(top.ray.d.y > 0.99f);
        }
    }

    [Fact]
    public void Realistic()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pbrt-cs-cameras-test");
        Directory.CreateDirectory(dir);
        string lens = Path.Combine(dir, "dgauss.50mm.dat");
        File.WriteAllText(lens, """
            # D-GAUSS F/2 22deg HFOV
            # radius axpos N aperture
            29.475  3.76   1.67   25.2
            84.83   0.12   1      25.2
            19.275  4.025  1.67   23
            40.77   3.275  1.699  23
            12.75   5.705  1      18
            0       4.5    0      17.1
            -14.495 1.18   1.603  17
            40.77   6.065  1.658  20
            -20.385 0.19   1      20
            437.065 3.22   1.717  20
            -39.73  0      1      20

            """);

        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        Point2i res = new(48, 32);
        // (Each RealisticCamera traces 64M rays to find its exit pupil, so
        // only one aperture shape is tested here.)
        foreach (string aperture in (string[])["pentagon"])
        {
            var ps = new List<(string, string, object)>
            {
                ("string", "lensfile", lens), ("float", "aperturediameter", 10f), ("float", "focusdistance", 3f)
            };
            if (aperture.Length > 0)
                ps.Add(("string", "aperture", aperture));
            Camera camera = MakeCamera("realistic", Params([.. ps]), res);

            RNG rng = new(7);
            int nRays = 0;
            for (int i = 0; i < 500; ++i)
            {
                CameraSample s = new()
                {
                    pFilm = new Point2f(rng.Uniform<Float>() * res.x, rng.Uniform<Float>() * res.y),
                    pLens = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()),
                    time = 0.5f
                };
                if (camera.GenerateRay(s, ref lambda) is not { } cr)
                    continue;
                ++nRays;
                Assert.Equal(1, Length(cr.ray.d), 1e-5f);
                Assert.True(cr.weight[0] > 0);
                // The lens flips the image; it looks down +z.
                Assert.True(cr.ray.d.z > 0.5f);
            }
            Assert.True(nRays > 50);

            // The debugging output is Mathematica graphics: one Circle per
            // spherical element (10) and the stop, and ray paths.
            RealisticCamera rc = (RealisticCamera)camera;
            StringWriter w = new();
            rc.DrawLensSystem(w);
            string lensSystem = w.ToString();
            Assert.Equal(10, lensSystem.Split("Circle[").Length - 1);
            Assert.StartsWith("Circle[{", lensSystem);
            Assert.EndsWith("Line[{{0, -.012}, {0, .012}}], Line[{{0, 0}, {" +
                            lensSystem.Split("Line[{{0, 0}, {")[1], lensSystem);
            w = new StringWriter();
            rc.DrawRayPathFromFilm(new Ray(new Point3f(0, 0, 0), new Vector3f(0.01f, 0, 1)), true, false, w);
            Assert.StartsWith("{ RGBColor[.5, .5, .8], Line[{{", w.ToString());
            Assert.Contains("Arrow[{{", w.ToString());
            Assert.EndsWith("}", w.ToString());
        }

        Assert.Throws<FatalErrorException>(() => MakeCamera("realistic", Params(), res));
    }

    [Fact]
    public void ApproximateDifferentials()
    {
        // A surface facing a perspective camera at distance z: one pixel
        // covers about (2 tan(fov/2) z / res). (pbrt uses the smallest
        // differentials over the film, which are a little smaller toward the
        // corners; with a narrow fov they are nearly the same.)
        Point2i res = new(100, 100);
        Camera camera = MakeCamera("perspective", Params(("float", "fov", 10f)), res);
        Float z = 5;
        camera.Approximate_dp_dxy(new Point3f(0, 0, z), new Normal3f(0, 0, -1), 0.5f, 1, out Vector3f dpdx,
                                  out Vector3f dpdy);
        Float pixel = 2 * FMath.Tan(Radians(5)) * z / res.x;
        Assert.Equal(pixel, Length(dpdx), pixel * 3e-2f);
        Assert.Equal(pixel, Length(dpdy), pixel * 3e-2f);

        // More samples per pixel shrink the footprint (down to 1/8).
        camera.Approximate_dp_dxy(new Point3f(0, 0, z), new Normal3f(0, 0, -1), 0.5f, 16, out dpdx, out _);
        Assert.Equal(pixel / 4, Length(dpdx), pixel * 3e-2f);

        // ComputeDifferentials() falls back to the camera for rays without
        // differentials.
        SurfaceInteraction si = new(new Point3fi(new Point3f(0, 0, z)), new Point2f(0.5f, 0.5f), new Vector3f(0, 0, -1),
                                    new Vector3f(1, 0, 0), new Vector3f(0, 1, 0), new Normal3f(0, 0, 0),
                                    new Normal3f(0, 0, 0), 0.5f, false);
        si.ComputeDifferentials(new RayDifferential(new Point3f(0, 0, 0), new Vector3f(0, 0, 1)), camera, 1);
        Assert.Equal(pixel, Length(si.dpdx), pixel * 3e-2f);
        Assert.Equal(pixel, FMath.Abs(si.dudx), pixel * 3e-2f);
    }

    [Fact]
    public void Create()
    {
        Assert.Throws<FatalErrorException>(() => MakeCamera("pinhole", Params(), new Point2i(10, 10)));
        Assert.Throws<FatalErrorException>(() =>
            MakeCamera("spherical", Params(("string", "mapping", "cube")), new Point2i(10, 10)));
        Assert.IsType<PerspectiveCamera>(MakeCamera("perspective", Params(), new Point2i(10, 10)));
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the integrators and RenderCPU() (pbrt has no integrator tests;
// rendered images were compared with pbrt's separately). The furnace tests
// render a diffuse sphere lit by a uniform infinite light: since the sphere
// is convex, the expected radiance from it is its reflectance times the
// light's.

namespace Pbrt.Tests;

[Collection("Options")]
public class IntegratorsTests
{
    private static Image Render(string integrator, string material, int spp = 64, bool wavefront = false)
    {
        string fn = ImageTests.InTestDir($"render-{Guid.NewGuid():N}.pfm");
        BasicScene scene = new();
        ParseString(new BasicSceneBuilder(scene), $$"""
            LookAt 0 0 -3  0 0 0  0 1 0
            Camera "perspective" "float fov" 90
            Sampler "independent" "integer pixelsamples" {{spp}}
            Integrator {{integrator}}
            Film "rgb" "integer xresolution" 16 "integer yresolution" 16 "string filename" "{{fn}}"
              "string sensor" "cie1931"
            PixelFilter "box"
            WorldBegin
            LightSource "infinite" "spectrum L" [300 1 800 1]
            {{material}}
            Shape "sphere" "float radius" 1
            """);
        if (wavefront)
            RenderWavefront(scene);
        else
            RenderCPU(scene);
        Image image = Image.Read(fn).image;
        Assert.True(RemoveFile(fn));
        return image;
    }

    // Average of the green channel over the center 2r x 2r pixels of the
    // image, which are covered by the sphere (it spans about 2.8 pixels from
    // the center).
    private static Float CenterAverage(Image image, int r = 2)
    {
        double sum = 0;
        int n = 0;
        for (int y = 8 - r; y < 8 + r; ++y)
            for (int x = 8 - r; x < 8 + r; ++x)
            {
                sum += image.GetChannel(new Point2i(x, y), 1);
                ++n;
            }
        return (Float)(sum / n);
    }

    [Theory]
    [InlineData("\"path\" \"integer maxdepth\" 5")]
    [InlineData("\"path\" \"string lightsampler\" \"uniform\"")]
    [InlineData("\"simplepath\"")]
    [InlineData("\"simplepath\" \"bool samplelights\" false")]
    [InlineData("\"simplepath\" \"bool samplebsdf\" false")]
    [InlineData("\"randomwalk\"")]
    [InlineData("\"volpath\"")]
    [InlineData("\"volpath\" \"bool regularize\" true")]
    [InlineData("\"bdpt\"")]
    [InlineData("\"bdpt\" \"integer maxdepth\" 2")]
    public void Furnace(string integrator)
    {
        Image image = Render(integrator, "Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]");
        // The background is the light itself.
        Float background = image.GetChannel(new Point2i(0, 0), 1);
        Assert.True(background > 0);
        Assert.Equal(0.5f, CenterAverage(image) / background, 0.04f);
    }

    // A medium that only scatters inside a uniformly lit sphere leaves the
    // radiance unchanged, so the image matches the one without the medium.
    // (It's compared with that image rather than the background since
    // simplevolpath's single-wavelength estimates are noisy; they're the same
    // wavelengths in both images.)
    [Theory]
    [InlineData("\"volpath\" \"integer maxdepth\" 100", "homogeneous")]
    [InlineData("\"simplevolpath\" \"integer maxdepth\" 100", "homogeneous")]
    [InlineData("\"volpath\" \"integer maxdepth\" 100", "cloud")]
    [InlineData("\"simplevolpath\" \"integer maxdepth\" 100", "uniformgrid")]
    public void VolumeFurnace(string integrator, string medium)
    {
        string mediumParams = medium switch
        {
            "homogeneous" => "",
            "cloud" => "\"point3 p0\" [-1 -1 -1] \"point3 p1\" [1 1 1] \"float g\" 0.5",
            _ => "\"float density\" [0.2 1 0.5 0.8] \"integer nx\" 2 \"integer ny\" 2 \"point3 p0\" [-1 -1 -1] " +
                 "\"point3 p1\" [1 1 1] \"float g\" -0.3"
        };
        Image image = Render(integrator,
            $"MakeNamedMedium \"m\" \"string type\" \"{medium}\" \"spectrum sigma_a\" [300 0 800 0] " +
            $"\"spectrum sigma_s\" [300 2 800 2] {mediumParams}\n" +
            "MediumInterface \"m\" \"\"\nMaterial \"interface\"");
        Image reference = Render(integrator, "Material \"interface\"");
        Assert.True(CenterAverage(reference) > 0);
        Assert.Equal(1f, CenterAverage(image) / CenterAverage(reference), 0.04f);
    }

    // The wavefront integrator always uses volpath's algorithm; its images
    // were compared with pbrt --wavefront's separately.
    [Theory]
    [InlineData("Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]", 0.5f)]
    [InlineData("Material \"coateddiffuse\" \"spectrum reflectance\" [300 0.5 800 0.5] \"float roughness\" 0.3", 0f)]
    [InlineData("MakeNamedMedium \"m\" \"string type\" \"homogeneous\" \"spectrum sigma_a\" [300 0 800 0] " +
                "\"spectrum sigma_s\" [300 2 800 2] \"float g\" 0.5\nMediumInterface \"m\" \"\"\nMaterial \"interface\"",
                1f)]
    [InlineData("Material \"subsurface\" \"spectrum reflectance\" [300 0.5 800 0.5] \"float eta\" 1", 0f)]
    public void WavefrontFurnace(string material, Float expected)
    {
        Image image = Render("\"volpath\" \"integer maxdepth\" 100", material, 64, true);
        Float background = image.GetChannel(new Point2i(0, 0), 1);
        Assert.True(background > 0);
        Float ratio = CenterAverage(image) / background;
        if (expected > 0)
            Assert.Equal(expected, ratio, 0.04f);
        else
            Assert.True(ratio > 0.1f && ratio < 1.04f, $"ratio {ratio}");

        // Volpath estimates the same radiance.
        if (!material.Contains("subsurface"))
        {
            Image volpath = Render("\"volpath\" \"integer maxdepth\" 100", material, 64);
            Assert.Equal(CenterAverage(volpath), CenterAverage(image), 0.02f);
        }
    }

    [Fact]
    public void VolumeAbsorption()
    {
        // With an absorbing medium, the radiance is the light's attenuated by
        // the transmittance along the ray's chord through the sphere.
        Image image = Render("\"volpath\"",
            "MakeNamedMedium \"m\" \"string type\" \"homogeneous\" \"spectrum sigma_a\" [300 0.5 800 0.5] " +
            "\"spectrum sigma_s\" [300 0 800 0]\nMediumInterface \"m\" \"\"\nMaterial \"interface\"", 64);
        Image reference = Render("\"volpath\"", "Material \"interface\"", 64);

        // Average the transmittance over the center pixels (the screen window
        // is [-1,1] for the 90 degree fov, and the camera is 3 from the
        // sphere's center).
        double expected = 0;
        const int n = 200;
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j)
            {
                double sx = -0.125 + 0.25 * (i + 0.5) / n, sy = -0.125 + 0.25 * (j + 0.5) / n;
                double d = 3 * Math.Sqrt(sx * sx + sy * sy) / Math.Sqrt(1 + sx * sx + sy * sy);
                expected += Math.Exp(-0.5 * 2 * Math.Sqrt(1 - d * d));
            }
        expected /= n * n;
        Assert.Equal((Float)expected, CenterAverage(image, 1) / CenterAverage(reference, 1), 0.02f);
    }

    [Fact]
    public void AmbientOcclusion()
    {
        // Nothing occludes a convex sphere, so AO is one everywhere it's hit
        // and zero on the background.
        Image image = Render("\"ambientocclusion\"", "Material \"diffuse\"", 16);
        // (The illuminant is normalized to 1 nit; the remaining variation is
        // from the wavelength samples.)
        Assert.Equal(1f, CenterAverage(image), 0.05f);
        Assert.Equal(0f, image.GetChannel(new Point2i(0, 0), 1));
    }

    [Fact]
    public void LightPath()
    {
        // Light paths only reach the camera via the diffuse sphere.
        Image image = Render("\"lightpath\"", "Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]");
        Image reference = Render("\"path\"", "Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]");
        Assert.Equal(1f, CenterAverage(image) / CenterAverage(reference), 0.1f);
        // Light paths never reach the camera directly from the light.
        Assert.Equal(0f, image.GetChannel(new Point2i(0, 0), 1));
    }

    [Fact]
    public void SPPM()
    {
        // SPPM is biased at this sample count, so it's compared loosely with
        // the path tracer.
        const string material = "Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]";
        Image image = Render("\"sppm\" \"integer photonsperiteration\" 20000 \"float radius\" 0.1", material, 16);
        Image reference = Render("\"path\"", material);
        Assert.Equal(1f, CenterAverage(image) / CenterAverage(reference), 0.15f);
        Assert.Equal(1f, image.GetChannel(new Point2i(0, 0), 1) / reference.GetChannel(new Point2i(0, 0), 1),
                     0.15f);
    }

    private static Float ImageAverage(Image image)
    {
        double sum = 0;
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                sum += image.GetChannel(new Point2i(x, y), 1);
        return (Float)(sum / (image.Resolution().x * image.Resolution().y));
    }

    [Fact]
    public void MLT()
    {
        // MLT's normalization makes the image's average match the path
        // tracer's. (Its distribution among the pixels converges slowly in
        // this scene; the rendered images are the same as pbrt's.)
        const string material = "Material \"diffuse\" \"spectrum reflectance\" [300 0.5 800 0.5]";
        Image image = Render("\"mlt\" \"integer bootstrapsamples\" 10000 \"integer chains\" 64 " +
                             "\"integer mutationsperpixel\" 64", material, 16);
        Image reference = Render("\"path\"", material);
        Assert.Equal(1f, ImageAverage(image) / ImageAverage(reference), 0.05f);
    }

    [Theory]
    [InlineData("step", "\"stratified\" \"integer xsamples\" 4 \"integer ysamples\" 4")]
    [InlineData("disk", "\"halton\" \"integer pixelsamples\" 16")]
    [InlineData("gaussian", "\"paddedsobol\" \"integer pixelsamples\" 16")]
    [InlineData("checkerboard", "\"independent\" \"integer pixelsamples\" 16")]
    public void Function(string function, string sampler)
    {
        // The function integrator writes the MSE of each pixel's estimate of
        // the function's integral (one) after each sample.
        string fn = ImageTests.InTestDir($"function-{Guid.NewGuid():N}.txt");
        BasicScene scene = new();
        ParseString(new BasicSceneBuilder(scene), $$"""
            Camera "perspective"
            Sampler {{sampler}}
            Integrator "function" "string function" "{{function}}" "string filename" "{{fn}}" "bool skipbad" false
            Film "rgb" "integer xresolution" 16 "integer yresolution" 16
            WorldBegin
            """);
        RenderCPU(scene);
        string[] lines = File.ReadAllLines(fn);
        Assert.True(RemoveFile(fn));
        Assert.Equal(16, lines.Length);
        Float[] mse = lines.Select(l => Float.Parse(l.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
                           .ToArray();
        Assert.Equal("16", lines[^1].Split(' ')[0]);
        Assert.True(mse.All(v => v >= 0));
        Assert.True(mse[^1] < mse[0]);
    }

    [Fact]
    public void Create()
    {
        Assert.Throws<FatalErrorException>(() => Render("\"nope\"", ""));
    }
}

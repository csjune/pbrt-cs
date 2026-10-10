// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the materials and SurfaceInteraction.GetBSDF() (pbrt has no
// material tests; the materials were compared with pbrt's in a separate
// harness).

namespace Pbrt.Tests;

[Collection("Options")]
public class MaterialsTests
{
    private static ParsedParameter P(string type, string name, params Float[] v)
    {
        ParsedParameter p = new(new FileLoc()) { type = type, name = name };
        p.floats.AddRange(v);
        return p;
    }

    private static ParsedParameter S(string type, string name, params string[] v)
    {
        ParsedParameter p = new(new FileLoc()) { type = type, name = name };
        p.strings.AddRange(v);
        return p;
    }

    private static Material? Create(string name, Dictionary<string, Material?>? named = null,
                                    params ParsedParameter[] ps) =>
        Material.Create(name, new TextureParameterDictionary(new ParameterDictionary(ps, RGBColorSpace.sRGB),
                                                             new NamedTextures()),
                        null, named ?? [], null);

    // A surface interaction on the z = 0 plane, seen from above.
    private static SurfaceInteraction Si(Material? m)
    {
        SurfaceInteraction si = new(new Point3fi(new Point3f(0.2f, 0.3f, 0)), new Point2f(0.2f, 0.3f),
                                    new Vector3f(0.3f, 0.1f, 1), new Vector3f(1, 0, 0), new Vector3f(0, 1, 0),
                                    new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0, false);
        si.material = m;
        return si;
    }

    private static RayDifferential Ray()
    {
        RayDifferential r = new(new Point3f(0.2f, 0.3f, 1), new Vector3f(0, 0, -1))
        {
            hasDifferentials = true,
            rxOrigin = new Point3f(0.21f, 0.3f, 1),
            ryOrigin = new Point3f(0.2f, 0.31f, 1),
            rxDirection = new Vector3f(0, 0, -1),
            ryDirection = new Vector3f(0, 0, -1)
        };
        return r;
    }

    private static BSDF GetBSDF(Material? m, ref SampledWavelengths lambda)
    {
        SurfaceInteraction si = Si(m);
        return si.GetBSDF(Ray(), ref lambda, null, new IndependentSampler(1));
    }

    [Fact]
    public void Diffuse()
    {
        Material m = Create("diffuse", null, P("rgb", "reflectance", 0.5f, 0.5f, 0.5f))!;
        Assert.IsType<DiffuseMaterial>(m);
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
        BSDF bsdf = GetBSDF(m, ref lambda);
        Assert.True(!!bsdf);
        Assert.Equal(BxDFFlags.DiffuseReflection, bsdf.Flags());
        SampledSpectrum f = bsdf.f(new Vector3f(0, 0, 1), new Vector3f(0.5f, 0, 0.8f));
        for (int i = 0; i < Util.NSpectrumSamples; ++i)
            Assert.Equal(0.5f * Util.InvPi, f[i], 2e-3f);
    }

    [Fact]
    public void DispersiveDielectricTerminatesSecondary()
    {
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
        BSDF bsdf = GetBSDF(Create("dielectric", null, S("spectrum", "eta", "glass-BK7")), ref lambda);
        Assert.True(!!bsdf);
        Assert.True(lambda.SecondaryTerminated());

        lambda = SampledWavelengths.SampleVisible(0.3f);
        GetBSDF(Create("dielectric", null, P("float", "eta", 1.5f)), ref lambda);
        Assert.False(lambda.SecondaryTerminated());
    }

    [Fact]
    public void InterfaceAndMix()
    {
        Assert.Null(Create("interface"));
        Dictionary<string, Material?> named = new()
        {
            ["a"] = Create("diffuse"),
            ["b"] = Create("conductor"),
            ["none"] = null,
        };
        Material mix0 = Create("mix", named, S("string", "materials", "a", "b"), P("float", "amount", 0))!;
        Material mix1 = Create("mix", named, S("string", "materials", "a", "b"), P("float", "amount", 1))!;
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
        Assert.Equal(BxDFFlags.DiffuseReflection, GetBSDF(mix0, ref lambda).Flags());
        Assert.Equal(BxDFFlags.SpecularReflection, GetBSDF(mix1, ref lambda).Flags());
        // An interface material can't be mixed.
        Assert.Throws<FatalErrorException>(() =>
            Create("mix", named, S("string", "materials", "a", "none")));
        // No BSDF for a surface without a material.
        Assert.False(!!GetBSDF(null, ref lambda));
    }

    [Fact]
    public void Subsurface()
    {
        Material m = Create("subsurface", null, S("string", "name", "Ketchup"))!;
        Assert.True(m.HasSubsurfaceScattering());
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
        SurfaceInteraction si = Si(m);
        BSDF bsdf = si.GetBSDF(Ray(), ref lambda, null, new IndependentSampler(1));
        Assert.True(!!bsdf);
        BSSRDF? bssrdf = si.GetBSSRDF(Ray(), ref lambda, null);
        Assert.IsType<TabulatedBSSRDF>(bssrdf);
        Assert.Throws<FatalErrorException>(() => Create("subsurface", null, S("string", "name", "Mud")));
    }

    [Fact]
    public void BumpMapping()
    {
        // A displacement that's linear in u tilts the shading normal.
        NamedTextures textures = new();
        textures.floatTextures["d"] = new FloatBilerpTexture(new UVMapping(), 0, 0, 0.5f, 0.5f);
        ParsedParameter disp = S("texture", "displacement", "d");
        Material m = Material.Create("diffuse",
                                     new TextureParameterDictionary(new ParameterDictionary([disp], RGBColorSpace.sRGB),
                                                                    textures),
                                     null, [], null)!;
        SurfaceInteraction si = Si(m);
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
        si.GetBSDF(Ray(), ref lambda, null, new IndependentSampler(1));
        Vector3f expected = Util.Normalize(new Vector3f(-0.5f, 0, 1));
        Assert.Equal(expected.x, si.shading.n.x, 1e-3f);
        Assert.Equal(expected.z, si.shading.n.z, 1e-3f);
    }

    [Fact]
    public void UnknownMaterial()
    {
        Assert.Throws<FatalErrorException>(() => Create("plastic"));
    }
}

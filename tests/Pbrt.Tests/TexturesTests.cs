// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the textures (pbrt has no texture tests; the textures were
// compared with pbrt's in a separate harness).

namespace Pbrt.Tests;

[Collection("Options")]
public class TexturesTests
{
    private static TextureParameterDictionary Dict(NamedTextures textures, params ParsedParameter[] ps) =>
        new(new ParameterDictionary(ps, RGBColorSpace.sRGB), textures);

    private static ParsedParameter P(string type, string name, params Float[] v)
    {
        ParsedParameter p = new(new FileLoc()) { type = type, name = name };
        p.floats.AddRange(v);
        return p;
    }

    private static ParsedParameter S(string type, string name, string v)
    {
        ParsedParameter p = new(new FileLoc()) { type = type, name = name };
        p.strings.Add(v);
        return p;
    }

    private static TextureEvalContext Ctx(Float u, Float v) =>
        new() { uv = new Point2f(u, v), p = new Point3f(u, v, 0), n = new Normal3f(0, 0, 1) };

    [Fact]
    public void UVMapping()
    {
        UVMapping m = new(2, 3, 0.5f, -1);
        TextureEvalContext ctx = Ctx(0.25f, 0.5f);
        ctx.dudx = 1;
        ctx.dvdy = 2;
        TexCoord2D c = m.Map(ctx);
        Assert.Equal(new Point2f(1, 0.5f), c.st);
        Assert.Equal(2, c.dsdx);
        Assert.Equal(6, c.dtdy);
        Assert.Equal(0, c.dsdy);
    }

    [Fact]
    public void SphericalMapping()
    {
        SphericalMapping m = new(new Transform());
        // s is theta / pi (from +z) and t is phi / 2pi (from +x).
        TexCoord2D c = m.Map(new TextureEvalContext { p = new Point3f(1, 0, 0) });
        Assert.Equal(0.5f, c.st.x, 1e-6f);
        Assert.Equal(0, c.st.y, 1e-6f);
        c = m.Map(new TextureEvalContext { p = new Point3f(0, 2, 2) });
        Assert.Equal(0.25f, c.st.x, 1e-6f);
        Assert.Equal(0.25f, c.st.y, 1e-6f);
    }

    [Fact]
    public void Checkerboard()
    {
        NamedTextures named = new();
        FloatTexture tex = FloatTexture.Create("checkerboard", new Transform(),
                                               Dict(named, P("float", "tex1", 2), P("float", "tex2", 5)), null);
        // Without filtering, the checks alternate.
        Assert.Equal(2, tex.Evaluate(Ctx(0.5f, 0.5f)));
        Assert.Equal(5, tex.Evaluate(Ctx(1.5f, 0.5f)));
        Assert.Equal(2, tex.Evaluate(Ctx(1.5f, 1.5f)));
        // With a wide filter, it's close to the average.
        TextureEvalContext ctx = Ctx(0.3f, 0.7f);
        ctx.dudx = ctx.dvdy = 10;
        Assert.Equal(3.5f, tex.Evaluate(ctx), 0.1f);
    }

    [Fact]
    public void ScaledTextureSimplifies()
    {
        NamedTextures named = new();
        named.floatTextures["c"] = new FloatConstantTexture(3);
        // A constant scale of one returns the texture itself.
        FloatTexture t = FloatScaledTexture.Create(new Transform(),
                                                   Dict(named, S("texture", "tex", "c"), P("float", "scale", 1)), null);
        Assert.Same(named.floatTextures["c"], t);
        // ...and the operands are swapped if "tex" is the constant one.
        t = FloatScaledTexture.Create(new Transform(), Dict(named, P("float", "tex", 1), S("texture", "scale", "c")),
                                      null);
        Assert.Same(named.floatTextures["c"], t);
        t = FloatScaledTexture.Create(new Transform(), Dict(named, P("float", "tex", 2), S("texture", "scale", "c")),
                                      null);
        Assert.IsType<FloatScaledTexture>(t);
        Assert.Equal(6, t.Evaluate(new TextureEvalContext()));
    }

    [Fact]
    public void MixAndDirectionMix()
    {
        NamedTextures named = new();
        named.floatTextures["a"] = new FloatConstantTexture(1);
        named.floatTextures["b"] = new FloatConstantTexture(3);
        FloatTexture mix = FloatTexture.Create("mix", new Transform(),
                                               Dict(named, S("texture", "tex1", "a"), S("texture", "tex2", "b"),
                                                    P("float", "amount", 0.25f)), null);
        Assert.Equal(1.5f, mix.Evaluate(new TextureEvalContext()));

        FloatTexture dmix = FloatTexture.Create("directionmix", new Transform(),
                                                Dict(named, S("texture", "tex1", "a"), S("texture", "tex2", "b"),
                                                     P("vector3", "dir", 0, 0, 1)), null);
        Assert.Equal(1, dmix.Evaluate(new TextureEvalContext { n = new Normal3f(0, 0, 1) }));
        Assert.Equal(3, dmix.Evaluate(new TextureEvalContext { n = new Normal3f(1, 0, 0) }));
    }

    [Fact]
    public void SpectrumTextureLookups()
    {
        NamedTextures named = new();
        named.albedoSpectrumTextures["s"] = new SpectrumConstantTexture(new ConstantSpectrum(0.5f));
        TextureParameterDictionary d = Dict(named, S("texture", "reflectance", "s"), P("rgb", "k", 0.2f, 0.4f, 0.6f),
                                            P("float", "rough", 0.1f));
        Assert.Same(named.albedoSpectrumTextures["s"], d.GetSpectrumTextureOrNull("reflectance", SpectrumType.Albedo));
        Assert.IsType<SpectrumConstantTexture>(d.GetSpectrumTextureOrNull("k", SpectrumType.Unbounded));
        Assert.Null(d.GetSpectrumTextureOrNull("eta", SpectrumType.Unbounded));
        Assert.IsType<FloatConstantTexture>(d.GetFloatTextureOrNull("rough"));
        Assert.Null(d.GetFloatTextureOrNull("other"));
        Assert.Equal(7, d.GetFloatTexture("other", 7).Evaluate(new TextureEvalContext()));
        // A texture that isn't of the requested spectrum type isn't found.
        Assert.Throws<FatalErrorException>(() => d.GetSpectrumTextureOrNull("reflectance", SpectrumType.Unbounded));
    }

    [Fact]
    public void NoiseTexturesAreBounded()
    {
        NamedTextures named = new();
        FloatTexture fbm = FloatTexture.Create("fbm", new Transform(), Dict(named), null);
        FloatTexture wrinkled = FloatTexture.Create("wrinkled", new Transform(), Dict(named), null);
        SpectrumTexture marble = SpectrumTexture.Create("marble", new Transform(), Dict(named), SpectrumType.Albedo,
                                                        null);
        RNG rng = new();
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        for (int i = 0; i < 100; ++i)
        {
            TextureEvalContext ctx = new()
            {
                p = new Point3f(10 * rng.Uniform<Float>(), 10 * rng.Uniform<Float>(), 10 * rng.Uniform<Float>())
            };
            Assert.InRange(fbm.Evaluate(ctx), -2, 2);
            Assert.InRange(wrinkled.Evaluate(ctx), 0, 2);
            SampledSpectrum s = marble.Evaluate(ctx, lambda);
            for (int c = 0; c < Util.NSpectrumSamples; ++c)
                Assert.InRange(s[c], 0, 1);
        }
    }

    [Fact]
    public void UnknownTexture()
    {
        Assert.Throws<FatalErrorException>(() =>
            FloatTexture.Create("marble", new Transform(), Dict(new NamedTextures()), null));
    }
}

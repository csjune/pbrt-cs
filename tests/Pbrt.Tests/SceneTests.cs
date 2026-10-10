// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the ports of src/pbrt/paramdict.h, scene.h and options.h (pbrt
// has no tests for them). What BasicSceneBuilder gives BasicScene was also
// checked against pbrt's, built with -ffp-contract=off, for the scenes of
// pbrt-v4-scenes: the entities, their parameters and their transformations.

namespace Pbrt.Tests;

[CollectionDefinition("Options", DisableParallelization = true)]
public class OptionsCollection;

[Collection("Options")]
public class ParamDictTests
{
    private static List<ParsedParameter> Params(string s)
    {
        // Parse the parameters of a Shape directive.
        List<ParsedParameter> result = [];
        ParseString(new CaptureTarget(p => result = p), "Shape \"x\" " + s);
        return result;
    }

    // Captures the parameters of the first Shape.
    private sealed class CaptureTarget(Action<List<ParsedParameter>> capture) : ParserTarget
    {
        public override void Shape(string name, List<ParsedParameter> parameters, FileLoc loc) => capture(parameters);
        public override void Scale(Float sx, Float sy, Float sz, FileLoc loc) { }
        public override void Option(string name, string value, FileLoc loc) { }
        public override void Identity(FileLoc loc) { }
        public override void Translate(Float dx, Float dy, Float dz, FileLoc loc) { }
        public override void Rotate(Float angle, Float ax, Float ay, Float az, FileLoc loc) { }
        public override void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz, Float ux, Float uy, Float uz, FileLoc loc) { }
        public override void ConcatTransform(Float[] transform, FileLoc loc) { }
        public override void Transform(Float[] transform, FileLoc loc) { }
        public override void CoordinateSystem(string name, FileLoc loc) { }
        public override void CoordSysTransform(string name, FileLoc loc) { }
        public override void ActiveTransformAll(FileLoc loc) { }
        public override void ActiveTransformEndTime(FileLoc loc) { }
        public override void ActiveTransformStartTime(FileLoc loc) { }
        public override void TransformTimes(Float start, Float end, FileLoc loc) { }
        public override void ColorSpace(string n, FileLoc loc) { }
        public override void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Film(string type, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Camera(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void MakeNamedMedium(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void MediumInterface(string insideName, string outsideName, FileLoc loc) { }
        public override void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void WorldBegin(FileLoc loc) { }
        public override void AttributeBegin(FileLoc loc) { }
        public override void AttributeEnd(FileLoc loc) { }
        public override void Attribute(string target, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Texture(string name, string type, string texname, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void Material(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void MakeNamedMaterial(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void NamedMaterial(string name, FileLoc loc) { }
        public override void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc) { }
        public override void ReverseOrientation(FileLoc loc) { }
        public override void ObjectBegin(string name, FileLoc loc) { }
        public override void ObjectEnd(FileLoc loc) { }
        public override void ObjectInstance(string name, FileLoc loc) { }
        public override void EndOfFiles() { }
    }

    internal static ParameterDictionary Dict(string s) => new(Params(s), RGBColorSpace.sRGB);

    [Fact]
    public void Basics()
    {
        ParameterDictionary dict = Dict("\"float f\" 1.5 \"integer i\" [ 3 ] \"bool b\" true \"string s\" \"str\" " +
                                        "\"point2 p2\" [1 2] \"vector2 v2\" [3 4] \"point3 p3\" [1 2 3] " +
                                        "\"vector3 v3\" [4 5 6] \"normal n\" [0 0 1] \"float fa\" [1 2 3] " +
                                        "\"integer ia\" [4 5] \"point3 pa\" [1 2 3 4 5 6] \"string sa\" [\"a\" \"b\"] " +
                                        "\"bool ba\" [false true] \"texture t\" \"tex\"");
        Assert.Equal(1.5f, dict.GetOneFloat("f", 0));
        Assert.Equal(3, dict.GetOneInt("i", 0));
        Assert.True(dict.GetOneBool("b", false));
        Assert.Equal("str", dict.GetOneString("s", ""));
        Assert.Equal(new Point2f(1, 2), dict.GetOnePoint2f("p2", default));
        Assert.Equal(new Vector2f(3, 4), dict.GetOneVector2f("v2", default));
        Assert.Equal(new Point3f(1, 2, 3), dict.GetOnePoint3f("p3", default));
        Assert.Equal(new Vector3f(4, 5, 6), dict.GetOneVector3f("v3", default));
        Assert.Equal(new Normal3f(0, 0, 1), dict.GetOneNormal3f("n", default));
        Assert.Equal([1f, 2, 3], dict.GetFloatArray("fa"));
        Assert.Equal([4, 5], dict.GetIntArray("ia"));
        Assert.Equal([new Point3f(1, 2, 3), new Point3f(4, 5, 6)], dict.GetPoint3fArray("pa"));
        Assert.Equal(["a", "b"], dict.GetStringArray("sa"));
        Assert.Equal([false, true], dict.GetBoolArray("ba"));
        Assert.Equal("tex", dict.GetTexture("t"));

        // Defaults when missing or of a different type
        Assert.Equal(7, dict.GetOneFloat("missing", 7));
        Assert.Equal(7, dict.GetOneFloat("i", 7));
        Assert.Empty(dict.GetFloatArray("i"));
        Assert.Equal("", dict.GetTexture("s"));

        // Everything was looked up except "missing".
        dict.ReportUnused();
        Assert.Equal("\"float f\" [ 1.5 ]", dict.ToParameterDefinition("f"));
        Assert.NotNull(dict.loc("f"));
        Assert.Null(dict.loc("missing"));
    }

    [Fact]
    public void Errors()
    {
        Assert.Throws<FatalErrorException>(() => Dict("\"bool b\" 1"));
        Assert.Throws<FatalErrorException>(() => Dict("\"float f\" \"a\""));
        Assert.Throws<FatalErrorException>(() => Dict("\"string s\" 1"));
        Assert.Throws<FatalErrorException>(() => Dict("\"spectrum s\" true"));
        Assert.Throws<FatalErrorException>(() => Dict("\"foo s\" 1"));
        Assert.Throws<FatalErrorException>(() => Dict("\"float f\" [1 2]").GetOneFloat("f", 0));
        Assert.Throws<FatalErrorException>(() => Dict("\"point3 p\" [1 2]").GetPoint3fArray("p"));
        Assert.Throws<FatalErrorException>(() => Dict("\"float f\" 1 \"float g\" 2").ReportUnused());
    }

    [Fact]
    public void Ordering()
    {
        // Later parameters shadow earlier ones, and parameters shadow
        // attributes.
        List<ParsedParameter> attributes = Params("\"float a\" 1 \"float b\" 10");
        foreach (ParsedParameter p in attributes)
            p.mayBeUnused = true;
        ParameterDictionary dict = new(Params("\"float a\" 2 \"float a\" 3"), attributes, RGBColorSpace.sRGB);
        Assert.Equal(3, dict.GetOneFloat("a", 0));
        Assert.Equal(10, dict.GetOneFloat("b", 0));
        // The shadowed "a" is fine, and unused attributes are fine.
        dict.ReportUnused();
        // (The attributes are added in reverse order too.)
        Assert.Equal("    \"float a\" [ 3 ]\n    \"float a\" [ 2 ]\n    \"float b\" [ 10 ]\n    \"float a\" [ 1 ]\n",
                     dict.ToParameterList());
    }

    [Fact]
    public void Spectra()
    {
        ParameterDictionary dict = Dict("\"rgb c\" [0.25 0.5 0.75] \"blackbody bb\" 3000 \"spectrum named\" \"metal-Cu-eta\" " +
                                        "\"spectrum pl\" [400 1 500 2 600 3] \"rgb big\" [2 2 2] \"rgb neg\" [-1 0 0] " +
                                        "\"spectrum bad\" [500 1 400 2] \"spectrum odd\" [400 1 500]");
        Assert.IsType<RGBAlbedoSpectrum>(dict.GetOneSpectrum("c", null, SpectrumType.Albedo));
        Assert.IsType<RGBUnboundedSpectrum>(dict.GetOneSpectrum("c", null, SpectrumType.Unbounded));
        Assert.IsType<RGBIlluminantSpectrum>(dict.GetOneSpectrum("c", null, SpectrumType.Illuminant));
        Assert.IsType<BlackbodySpectrum>(dict.GetOneSpectrum("bb", null, SpectrumType.Illuminant));
        Assert.Same(GetNamedSpectrum("metal-Cu-eta"), dict.GetOneSpectrum("named", null, SpectrumType.Unbounded));
        Spectrum pl = dict.GetOneSpectrum("pl", null, SpectrumType.Unbounded)!;
        Assert.Equal(1.5f, pl.Evaluate(450), 1e-6f);
        Assert.Null(dict.GetOneSpectrum("missing", null, SpectrumType.Albedo));
        Assert.Equal([new RGB(.25f, .5f, .75f)], dict.GetRGBArray("c"));
        Assert.Equal(new RGB(.25f, .5f, .75f), dict.GetOneRGB("c"));

        Assert.Throws<FatalErrorException>(() => dict.GetOneSpectrum("big", null, SpectrumType.Albedo));
        Assert.NotNull(dict.GetOneSpectrum("big", null, SpectrumType.Unbounded));
        Assert.Throws<FatalErrorException>(() => dict.GetOneSpectrum("neg", null, SpectrumType.Unbounded));
        Assert.Throws<FatalErrorException>(() => dict.GetOneSpectrum("bad", null, SpectrumType.Unbounded));
        Assert.Throws<FatalErrorException>(() => dict.GetOneSpectrum("odd", null, SpectrumType.Unbounded));
        Assert.Throws<FatalErrorException>(() =>
            Dict("\"spectrum s\" \"no-such-spectrum\"").GetOneSpectrum("s", null, SpectrumType.Unbounded));
        Assert.Throws<FatalErrorException>(() =>
            Dict("\"rgb c\" [1 1 1 1 1 1]").GetOneSpectrum("c", null, SpectrumType.Unbounded));
        Assert.Equal(2, Dict("\"rgb c\" [1 1 1 1 1 1]").GetSpectrumArray("c", SpectrumType.Unbounded).Count);

        // Spectrum files are read relative to the search directory.
        string fn = ImageTests.InTestDir("spd.txt");
        File.WriteAllText(fn, "400 1\n700 2\n");
        Spectrum fromFile = Dict($"\"spectrum s\" \"{fn}\"").GetOneSpectrum("s", null, SpectrumType.Unbounded)!;
        Assert.Equal(1.5f, fromFile.Evaluate(550), 1e-6f);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void Upgrading()
    {
        ParameterDictionary dict = Dict("\"blackbody L\" [5500 3] \"float f\" 1 \"rgb Kd\" [1 1 1] \"texture t\" \"a\" " +
                                        "\"texture u\" \"b\"");
        Assert.Equal(3, dict.UpgradeBlackbody("L"));
        Assert.Equal("\"blackbody L\" [ 5500 ]", dict.ToParameterDefinition("L"));
        dict.RenameParameter("Kd", "reflectance");
        Assert.NotNull(dict.GetOneRGB("reflectance"));
        dict.RenameUsedTextures(new Dictionary<string, string> { ["a"] = "a2" });
        Assert.Equal("a2", dict.GetTexture("t"));
        Assert.Equal("b", dict.GetTexture("u"));
        dict.RemoveFloat("f");
        dict.RemoveSpectrum("L");
        dict.RemoveTexture("t");
        Assert.Equal(-1, dict.GetOneFloat("f", -1));
        Assert.Empty(dict.GetSpectrumArray("L", SpectrumType.Illuminant));
        Assert.Equal("", dict.GetTexture("t"));
        Assert.Equal(2, dict.GetParameterVector().Count);

        // Clones share parameters but not the list.
        ParameterDictionary clone = dict.Clone();
        clone.RemoveTexture("u");
        Assert.Equal("b", dict.GetTexture("u"));
    }
}

[Collection("Options")]
public class SceneTests
{
    private static BasicScene Parse(string scene, out BasicSceneBuilder builder)
    {
        BasicScene s = new();
        builder = new BasicSceneBuilder(s);
        ParseString(builder, scene);
        return s;
    }

    private static BasicScene Parse(string scene) => Parse(scene, out _);

    private static void AssertNear(Transform expected, Transform actual)
    {
        for (int i = 0; i < 4; ++i)
            for (int j = 0; j < 4; ++j)
                Assert.Equal(expected.GetMatrix()[i, j], actual.GetMatrix()[i, j], 1e-5f);
    }

    [Fact]
    public void Defaults()
    {
        BasicScene scene = Parse("WorldBegin");
        Assert.Equal("volpath", scene.integrator.name);
        Assert.Equal("bvh", scene.accelerator.name);
        Assert.Equal("perspective", scene.cameraEntity.name);
        Assert.Equal("zsobol", scene.samplerEntity.name);
        Assert.Equal("gaussian", scene.filterEntity.name);
        Assert.Equal("rgb", scene.filmEntity.name);
        Assert.Same(RGBColorSpace.sRGB, scene.filmColorSpace);
        // The default material
        Assert.Single(scene.Materials);
        Assert.Equal("diffuse", scene.Materials[0].name);
        Assert.Equal(1, scene.Materials[0].loc.line);
        Assert.Empty(scene.shapes);
        // Interned names
        Assert.Same(scene.integrator.name, SceneEntity.internedStrings.Lookup(new string("volpath")));
    }

    [Fact]
    public void ShapesAndTransforms()
    {
        BasicScene scene = Parse("""
            LookAt 0 0 5  0 0 0  0 1 0
            Camera "perspective" "float fov" 45
            WorldBegin
            AttributeBegin
              Translate 1 2 3
              Material "conductor"
              ReverseOrientation
              MediumInterface "in" "out"
              Shape "sphere" "float radius" 2
              AttributeBegin
                Scale 2 2 2
                NamedMaterial "foo"
                Shape "disk"
              AttributeEnd
            AttributeEnd
            Shape "sphere"
            """);
        Assert.Equal(3, scene.shapes.Count);

        // Camera-world rendering space: render space is world space
        // translated to the camera position.
        Transform renderFromWorld = Translate(new Vector3f(0, 0, -5));
        ShapeSceneEntity s0 = scene.shapes[0];
        Assert.Equal("sphere", s0.name);
        Assert.Equal(2, s0.parameters.GetOneFloat("radius", 0));
        AssertNear(renderFromWorld * Translate(new Vector3f(1, 2, 3)), s0.renderFromObject);
        AssertNear(Inverse(s0.renderFromObject), s0.objectFromRender);
        Assert.True(s0.reverseOrientation);
        Assert.Equal(1, s0.materialIndex);
        Assert.Equal("", s0.materialName);
        Assert.Equal(-1, s0.lightIndex);
        Assert.Equal(("in", "out"), (s0.insideMedium, s0.outsideMedium));

        ShapeSceneEntity s1 = scene.shapes[1];
        AssertNear(renderFromWorld * Translate(new Vector3f(1, 2, 3)) * Scale(2, 2, 2), s1.renderFromObject);
        Assert.Equal(-1, s1.materialIndex);
        Assert.Equal("foo", s1.materialName);
        Assert.True(s1.reverseOrientation);

        // The graphics state is restored.
        ShapeSceneEntity s2 = scene.shapes[2];
        AssertNear(renderFromWorld, s2.renderFromObject);
        Assert.False(s2.reverseOrientation);
        Assert.Equal(0, s2.materialIndex);
        Assert.Equal("", s2.insideMedium);

        // Equal transforms are shared.
        Assert.Same(s0.renderFromObject, scene.shapes[0].renderFromObject);
        BasicScene twice = Parse("WorldBegin Translate 1 0 0 Shape \"a\" Shape \"b\"");
        Assert.Same(twice.shapes[0].renderFromObject, twice.shapes[1].renderFromObject);

        // The camera
        CameraTransform ct = scene.cameraEntity.cameraTransform;
        AssertNear(Inverse(renderFromWorld), ct.WorldFromRender());
        Assert.Equal(new Point3f(0, 0, 0), ct.RenderFromCamera(new Point3f(0, 0, 0), 0));
        Assert.Equal(45, scene.cameraEntity.parameters.GetOneFloat("fov", 0));
    }

    [Fact]
    public void TransformDirectives()
    {
        BasicScene scene = Parse("""
            Option "rendercoordsys" "world"
            WorldBegin
            Rotate 90 0 0 1
            Shape "a"
            Identity
            Transform [1 0 0 0  0 1 0 0  0 0 1 0  1 2 3 1]
            Shape "b"
            ConcatTransform [2 0 0 0  0 2 0 0  0 0 2 0  0 0 0 1]
            Shape "c"
            CoordinateSystem "mine"
            Identity
            CoordSysTransform "mine"
            Shape "d"
            CoordSysTransform "world"
            Shape "e"
            """);
        try
        {
            AssertNear(Rotate(90, new Vector3f(0, 0, 1)), scene.shapes[0].renderFromObject);
            AssertNear(Translate(new Vector3f(1, 2, 3)), scene.shapes[1].renderFromObject);
            AssertNear(Translate(new Vector3f(1, 2, 3)) * Scale(2, 2, 2), scene.shapes[2].renderFromObject);
            AssertNear(scene.shapes[2].renderFromObject, scene.shapes[3].renderFromObject);
            AssertNear(new Transform(), scene.shapes[4].renderFromObject);
        }
        finally
        {
            Options.renderingSpace = RenderingCoordinateSystem.CameraWorld;
        }
    }

    [Fact]
    public void AnimatedTransforms()
    {
        BasicScene scene = Parse("""
            TransformTimes 0 2
            WorldBegin
            ActiveTransform EndTime
            Translate 1 0 0
            ActiveTransform All
            Shape "moving"
            ObjectBegin "obj"
              Shape "inside"
            ObjectEnd
            ObjectInstance "obj"
            ActiveTransform StartTime
            Translate 1 0 0
            ObjectInstance "obj"
            """);
        AnimatedShapeSceneEntity a = Assert.Single(scene.animatedShapes);
        Assert.Equal("moving", a.name);
        Assert.True(a.renderFromObject.IsAnimated());
        Assert.Equal((0f, 2f), (a.renderFromObject.startTime, a.renderFromObject.endTime));
        Assert.Equal(new Point3f(0.5f, 0, 0), a.renderFromObject.Apply(new Point3f(0, 0, 0), 1));
        Assert.True(a.identity.IsIdentity());

        // Shapes inside instance definitions get the instance's CTM.
        InstanceDefinitionSceneEntity def = scene.instanceDefinitions["obj"];
        Assert.Equal("inside", Assert.Single(def.animatedShapes).name);
        Assert.Empty(def.shapes);

        Assert.Equal(2, scene.instances.Count);
        Assert.NotNull(scene.instances[0].renderFromInstanceAnim);
        Assert.Null(scene.instances[0].renderFromInstance);
        // Both ends are translated by 1 now, so it isn't animated.
        Assert.Null(scene.instances[1].renderFromInstanceAnim);
        AssertNear(Translate(new Vector3f(1, 0, 0)), scene.instances[1].renderFromInstance!);
    }

    [Fact]
    public void MaterialsLightsAndTextures()
    {
        string fn = ImageTests.InTestDir("scene-tex.png");
        Image img = new(PixelFormat.U256, new Point2i(2, 2), ["R", "G", "B"], ColorEncoding.Linear);
        Assert.True(img.Write(fn));
        BasicScene scene = Parse($$"""
            WorldBegin
            Attribute "material" "float roughness" 0.5
            Attribute "light" "float scale" 2
            Material "conductor" "rgb k" [1 1 1]
            MakeNamedMaterial "named" "string type" "diffuse"
            Texture "checks" "float" "checkerboard"
            Texture "img" "spectrum" "imagemap" "string filename" "{{fn}}"
            Texture "img2" "spectrum" "imagemap" "string filename" "{{fn}}"
            Texture "imgf" "float" "imagemap" "string filename" "{{fn}}"
            Texture "nofile" "float" "imagemap" "string filename" "no-such-file.png"
            Texture "nofilename" "spectrum" "imagemap"
            LightSource "point" "rgb I" [1 1 1]
            MakeNamedMedium "fog" "string type" "homogeneous"
            AreaLightSource "diffuse" "rgb L" [1 2 3]
            Shape "sphere"
            Shape "disk"
            """);
        Assert.True(RemoveFile(fn));

        Assert.Equal(2, scene.Materials.Count);
        SceneEntity m = scene.Materials[1];
        Assert.Equal("conductor", m.name);
        // Attributes are included (as the last parameters).
        Assert.Equal(0.5f, m.parameters.GetOneFloat("roughness", 0));
        (string name, SceneEntity material) nm = Assert.Single(scene.NamedMaterials);
        Assert.Equal("named", nm.name);
        Assert.Equal("", nm.material.name);
        Assert.Equal("diffuse", nm.material.parameters.GetOneString("type", ""));

        // Textures from a file that's already being loaded are created
        // serially (float and spectrum textures share the file names).
        Assert.Equal(["checks", "imgf"], scene.SerialFloatTextures.Select(t => t.name));
        Assert.Empty(scene.AsyncFloatTextureNames);
        Assert.Equal("img", Assert.Single(scene.AsyncSpectrumTextures).name);
        Assert.Equal("img2", Assert.Single(scene.SerialSpectrumTextures).name);
        Assert.Equal(2, scene.NMissingTextures);

        LightSceneEntity light = Assert.Single(scene.lights);
        Assert.Equal(2, light.parameters.GetOneFloat("scale", 0));
        Assert.Equal("homogeneous", scene.media["fog"].parameters.GetOneString("type", ""));

        // Both shapes get their own area light.
        Assert.Equal(2, scene.AreaLights.Count);
        Assert.Equal(new RGB(1, 2, 3), scene.AreaLights[0].parameters.GetOneRGB("L"));
        Assert.Equal([0, 1], scene.shapes.Select(s => s.lightIndex));
    }

    [Fact]
    public void CreateTexturesAndMaterials()
    {
        string fn = ImageTests.InTestDir("scene-tex2.pfm");
        Image img = new(PixelFormat.Float, new Point2i(4, 4), ["R", "G", "B"]);
        for (int y = 0; y < 4; ++y)
            for (int x = 0; x < 4; ++x)
                img.SetChannels(new Point2i(x, y), [0.25f, 0.5f, 0.75f]);
        Assert.True(img.Write(fn));
        BasicScene scene = Parse($$"""
            WorldBegin
            Texture "checks" "float" "checkerboard" "float tex1" 0.25 "float tex2" 0.75
            Texture "img" "spectrum" "imagemap" "string filename" "{{fn}}"
            Texture "imgf" "float" "imagemap" "string filename" "{{fn}}"
            Texture "mixed" "spectrum" "mix" "texture tex1" "img" "rgb tex2" [1 0 0] "texture amount" "checks"
            MakeNamedMaterial "red" "string type" "diffuse" "rgb reflectance" [1 0 0]
            MakeNamedMaterial "glass" "string type" "dielectric"
            Material "conductor" "texture roughness" "imgf"
            Material "diffuse" "texture reflectance" "mixed"
            Material "mix" "string materials" ["red" "glass"] "texture amount" "checks"
            Material "interface"
            """);
        NamedTextures textures = scene.CreateTextures();
        Assert.IsType<FloatCheckerboardTexture>(textures.floatTextures["checks"]);
        Assert.IsType<FloatImageTexture>(textures.floatTextures["imgf"]);
        Assert.IsType<SpectrumImageTexture>(textures.albedoSpectrumTextures["img"]);
        Assert.IsType<SpectrumImageTexture>(textures.unboundedSpectrumTextures["img"]);
        Assert.IsType<SpectrumImageTexture>(textures.illuminantSpectrumTextures["img"]);
        Assert.IsType<SpectrumMixTexture>(textures.albedoSpectrumTextures["mixed"]);

        Dictionary<string, Material?> named = [];
        List<Material?> materials = [];
        scene.CreateMaterials(textures, named, materials);
        Assert.IsType<DiffuseMaterial>(named["red"]);
        Assert.IsType<DielectricMaterial>(named["glass"]);
        // (The first one is the builder's default material.)
        Assert.Equal(5, materials.Count);
        Assert.IsType<DiffuseMaterial>(materials[0]);
        Assert.IsType<ConductorMaterial>(materials[1]);
        Assert.IsType<DiffuseMaterial>(materials[2]);
        Assert.IsType<MixMaterial>(materials[3]);
        Assert.Null(materials[4]);

        // The image is constant, so the float image texture is the average
        // over the channels.
        Float v = textures.floatTextures["imgf"].Evaluate(new TextureEvalContext());
        Assert.Equal(0.5f, v, 1e-5f);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void CreateLights()
    {
        BasicScene scene = Parse("""
            WorldBegin
            LightSource "point" "rgb I" [1 1 1]
            LightSource "infinite"
            AttributeBegin
              AreaLightSource "diffuse" "rgb L" [1 2 3]
              Shape "sphere"
              Material "interface"
              Shape "disk"
            AttributeEnd
            AttributeBegin
              AreaLightSource "diffuse" "blackbody L" 5000
              Shape "trianglemesh" "point3 P" [0 0 0 1 0 0 1 1 0 0 1 0] "integer indices" [0 1 2 0 2 3]
                "float alpha" 0
            AttributeEnd
            """);
        NamedTextures textures = scene.CreateTextures();
        Dictionary<int, List<Light>> shapeLights = [];
        List<Light> lights = scene.CreateLights(textures, shapeLights);
        // The area lights come first: one for the sphere and two for the
        // triangles; the disk has an interface material.
        Assert.Equal(5, lights.Count);
        Assert.IsType<DiffuseAreaLight>(lights[0]);
        Assert.Equal(LightType.Area, lights[0].Type());
        Assert.Equal(LightType.DeltaPosition, lights[1].Type());
        Assert.IsType<PointLight>(lights[3]);
        Assert.IsType<UniformInfiniteLight>(lights[4]);
        Assert.Equal([0, 2], shapeLights.Keys.Order());
        Assert.Equal(2, shapeLights[2].Count);
    }

    [Fact]
    public void NormalMaps()
    {
        string fn = ImageTests.InTestDir("normalmap.png");
        Image img = new(PixelFormat.U256, new Point2i(2, 2), ["R", "G", "B"], ColorEncoding.Linear);
        img.SetChannels(new Point2i(1, 1), [1, 0.5f, 0]);
        Assert.True(img.Write(fn));
        BasicScene scene = Parse($"WorldBegin Material \"diffuse\" \"string normalmap\" \"{fn}\" " +
                                 $"MakeNamedMaterial \"a\" \"string type\" \"diffuse\" \"string normalmap\" \"{fn}\"");
        IReadOnlyDictionary<string, Image> maps = scene.GetNormalMaps();
        Image map = Assert.Single(maps).Value;
        Assert.Equal(3, map.NChannels());
        // Read with a linear encoding
        Assert.Equal(1, map.GetChannel(new Point2i(1, 1), 0));
        Assert.Equal(128 / 255f, map.GetChannel(new Point2i(1, 1), 1), 1e-6f);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void OptionDirectives()
    {
        PBRTOptions saved = Util.Options;
        Util.Options = new PBRTOptions();
        try
        {
            Parse("Option \"seed\" 7 Option \"disablepixeljitter\" true Option \"disable-wavelength-jitter\" true " +
                  "Option \"displacementedgescale\" 0.5 Option \"msereferenceimage\" \"ref.exr\" " +
                  "Option \"forcediffuse\" true Option \"pixelstats\" true Option \"wavefront\" false " +
                  "Option \"disabletexturefiltering\" true WorldBegin");
            Assert.Equal(7, Util.Options.seed);
            Assert.True(Util.Options.disablePixelJitter);
            Assert.True(Util.Options.disableWavelengthJitter);
            Assert.Equal(0.5f, Util.Options.displacementEdgeScale);
            Assert.Equal("ref.exr", Util.Options.mseReferenceImage);
            Assert.True(Util.Options.forceDiffuse);
            Assert.True(Util.Options.recordPixelStatistics);
            Assert.True(Util.Options.disableTextureFiltering);

            Assert.Throws<FatalErrorException>(() => Parse("Option \"seed2\" 7 WorldBegin"));
            Assert.Throws<FatalErrorException>(() => Parse("Option \"forcediffuse\" 1 WorldBegin"));
            Assert.Throws<FatalErrorException>(() => Parse("Option \"rendercoordsys\" \"foo\" WorldBegin"));
            Assert.Contains("seed: 7", Util.Options.ToString());
            Assert.Equal("RenderingCoordinateSystem::CameraWorld", Util.ToString(Util.Options.renderingSpace));
        }
        finally
        {
            Util.Options = saved;
        }
    }

    [Fact]
    public void Errors()
    {
        // Options inside the world block and scene description outside it
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Camera \"perspective\""));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Film \"rgb\""));
        Assert.Throws<FatalErrorException>(() => Parse("Shape \"sphere\" WorldBegin"));
        Assert.Throws<FatalErrorException>(() => Parse("AttributeBegin WorldBegin"));
        // Deferred errors are reported at the end.
        Assert.Throws<FatalErrorException>(() => Parse(""));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin AttributeBegin"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin ObjectBegin \"a\" AttributeEnd"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin AttributeBegin ObjectBegin \"a\" AttributeEnd ObjectEnd"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin ObjectEnd"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin ObjectBegin \"a\" ObjectEnd ObjectBegin \"a\" ObjectEnd"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin ObjectBegin \"a\" ObjectInstance \"b\" ObjectEnd"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin MakeNamedMaterial \"a\" MakeNamedMaterial \"a\""));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Texture \"a\" \"float\" \"x\" Texture \"a\" \"float\" \"y\""));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Texture \"a\" \"color\" \"x\""));
        Assert.Throws<FatalErrorException>(() => Parse("MakeNamedMedium \"a\" MakeNamedMedium \"a\" WorldBegin"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Attribute \"camera\" \"float a\" 1"));
        Assert.Throws<FatalErrorException>(() => Parse("Camera \"perspective\" \"float shutteropen\" 1 " +
                                                       "\"float shutterclose\" 1 WorldBegin"));
        Assert.Throws<FatalErrorException>(() => Parse("WorldBegin Import \"x.pbrt\"", out _));
        Assert.Throws<FatalErrorException>(() => Parse("Import \"x.pbrt\" WorldBegin"));

        // These are only warnings or errors that don't stop parsing.
        BasicScene scene = Parse("WorldBegin AttributeEnd ColorSpace \"no-such-space\" CoordSysTransform \"none\"");
        Assert.Empty(scene.shapes);
        // The same texture name may be used for a float and a spectrum
        // texture.
        Parse("WorldBegin Texture \"a\" \"float\" \"x\" Texture \"a\" \"spectrum\" \"y\"");
    }

    [Fact]
    public void ColorSpacesAndAttributes()
    {
        BasicScene scene = Parse("""
            WorldBegin
            ColorSpace "rec2020"
            Attribute "shape" "rgb Kd" [0.5 0.5 0.5]
            ColorSpace "srgb"
            Shape "sphere" "rgb c" [0.5 0.5 0.5]
            """);
        ShapeSceneEntity s = Assert.Single(scene.shapes);
        Assert.Same(RGBColorSpace.sRGB, s.parameters.ColorSpace());
        // The attribute keeps the color space that was current for it.
        ParsedParameter kd = s.parameters.GetParameterVector().Single(p => p.name == "Kd");
        Assert.Same(RGBColorSpace.Rec2020, kd.colorSpace);
        Assert.True(kd.mayBeUnused);
        Assert.Null(s.parameters.GetParameterVector().Single(p => p.name == "c").colorSpace);
    }

    [Fact]
    public void IncludeAndImport()
    {
        string dir = Directory.CreateTempSubdirectory("pbrt-scene").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "inc.pbrt"), "Shape \"included\"\n");
            File.WriteAllText(Path.Combine(dir, "imp.pbrt"),
                              "Shape \"imported\"\nMakeNamedMaterial \"m2\" \"string type\" \"diffuse\"\n" +
                              "AttributeBegin\n");
            File.WriteAllText(Path.Combine(dir, "objimp.pbrt"), "Shape \"in-object\"\nShape \"in-object2\"\n");
            File.WriteAllText(Path.Combine(dir, "main.pbrt"), """
                WorldBegin
                Translate 1 0 0
                Include "inc.pbrt"
                Import "imp.pbrt"
                ObjectBegin "obj"
                  Import "objimp.pbrt"
                ObjectEnd
                Shape "main"
                """);
            BasicScene scene = new();
            BasicSceneBuilder builder = new(scene);
            // The import is missing an AttributeEnd.
            Assert.Throws<FatalErrorException>(() => ParseFiles(builder, [Path.Combine(dir, "main.pbrt")]));
            Assert.Contains(scene.shapes, s => s.name == "imported");

            File.WriteAllText(Path.Combine(dir, "imp.pbrt"),
                              "Shape \"imported\"\nMakeNamedMaterial \"m2\" \"string type\" \"diffuse\"\n");
            scene = new BasicScene();
            builder = new BasicSceneBuilder(scene);
            ParseFiles(builder, [Path.Combine(dir, "main.pbrt")]);
            // (Imported shapes are added when the import is merged, before
            // the main file's shapes.)
            Assert.Equal(["imported", "included", "main"], scene.shapes.Select(s => s.name).Order());
            foreach (ShapeSceneEntity s in scene.shapes)
                AssertNear(Translate(new Vector3f(1, 0, 0)), s.renderFromObject);
            Assert.Equal("inc.pbrt", Path.GetFileName(scene.shapes.Single(s => s.name == "included").loc.filename));
            InstanceDefinitionSceneEntity def = scene.instanceDefinitions["obj"];
            Assert.Equal(["in-object", "in-object2"], def.shapes.Select(s => s.name));
            Assert.Equal("m2", Assert.Single(scene.NamedMaterials).name);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CameraTransforms()
    {
        AnimatedTransform worldFromCamera = new(Translate(new Vector3f(1, 2, 3)), 0,
                                                Translate(new Vector3f(3, 2, 3)), 1);
        RenderingCoordinateSystem saved = Util.Options.renderingSpace;
        try
        {
            Util.Options.renderingSpace = RenderingCoordinateSystem.World;
            CameraTransform world = new(worldFromCamera);
            Assert.True(world.WorldFromRender().IsIdentity());
            Assert.Equal(new Point3f(1, 2, 3), world.RenderFromCamera(new Point3f(0, 0, 0), 0));

            Util.Options.renderingSpace = RenderingCoordinateSystem.CameraWorld;
            CameraTransform cameraWorld = new(worldFromCamera);
            // The camera's position at the middle of the shutter interval
            AssertNear(Translate(new Vector3f(2, 2, 3)), cameraWorld.WorldFromRender());
            Assert.Equal(new Point3f(-1, 0, 0), cameraWorld.RenderFromCamera(new Point3f(0, 0, 0), 0));

            Util.Options.renderingSpace = RenderingCoordinateSystem.Camera;
            AnimatedTransform rotating = new(Rotate(90, new Vector3f(0, 1, 0)), 0, Rotate(90, new Vector3f(0, 1, 0)), 1);
            CameraTransform camera = new(rotating);
            AssertNear(new Transform(), camera.RenderFromCamera().startTransform);
            Assert.False(camera.CameraFromRenderHasScale());
            AssertNear(Inverse(camera.WorldFromRender()), camera.CameraFromWorld(0.5f));
        }
        finally
        {
            Util.Options.renderingSpace = saved;
        }
    }

    [Fact]
    public void TriangleCreateMesh()
    {
        ParameterDictionary dict(string s)
        {
            BasicScene scene = Parse("WorldBegin Shape \"trianglemesh\" " + s);
            return scene.shapes[0].parameters;
        }
        TriangleMesh mesh = Triangle.CreateMesh(new Transform(), false,
                                                dict("\"point3 P\" [0 0 0 1 0 0 0 1 0] \"point2 uv\" [0 0 1 0 0 1]"),
                                                null)!;
        Assert.Equal([0, 1, 2], mesh.vertexIndices);
        Assert.NotNull(mesh.uv);

        // Bad data is dropped with an error.
        mesh = Triangle.CreateMesh(new Transform(), false,
                                   dict("\"point3 P\" [0 0 0 1 0 0 0 1 0] \"integer indices\" [0 1 2 1] " +
                                        "\"point2 uv\" [0 0] \"normal N\" [0 0 1] \"vector3 S\" [1 0 0] " +
                                        "\"integer faceIndices\" [1 2]"), null)!;
        Assert.Equal(1, mesh.nTriangles);
        Assert.Null(mesh.uv);
        Assert.Null(mesh.n);
        Assert.Null(mesh.s);
        Assert.Null(mesh.faceIndices);
        Assert.Null(Triangle.CreateMesh(new Transform(), false, dict("\"point3 P\" [0 0 0 1 0 0]"), null));
        Assert.Null(Triangle.CreateMesh(new Transform(), false, dict("\"integer indices\" [0 1 2]"), null));
        Assert.Null(Triangle.CreateMesh(new Transform(), false,
                                        dict("\"point3 P\" [0 0 0 1 0 0 0 1 0] \"integer indices\" [0 1 3]"), null));
        Assert.Null(Triangle.CreateMesh(new Transform(), false,
                                        dict("\"point3 P\" [0 0 0 1 0 0 0 1 0] \"integer indices\" [0 -1 2]"), null));
    }
}

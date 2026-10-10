// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/scene.h and scene.cpp
//
// The scene entities are classes (pbrt's structs are copied, but never
// modified after they're created). InternedString is a string from
// SceneEntity.internedStrings. The builder's GraphicsState is copied with
// Clone() where pbrt copies it.
//
// BasicScene also keeps some of the entities it's given that pbrt only
// uses to start jobs (the filter, film, sampler, camera, media and lights),
// for tests. The image texture jobs get an empty NamedTextures where pbrt
// passes a null pointer.

global using MediumSceneEntity = Pbrt.TransformedSceneEntity;
global using TextureSceneEntity = Pbrt.TransformedSceneEntity;

namespace Pbrt;

// SceneEntity Definition
public class SceneEntity
{
    // SceneEntity Public Members
    public string name = "";
    // (pbrt's default FileLoc has line 1; default(FileLoc) has line 0.)
    public FileLoc loc = new();
    public ParameterDictionary parameters = new();
    public static readonly InternCache<string> internedStrings = new();

    // SceneEntity Public Methods
    public SceneEntity() { }

    public SceneEntity(string name, ParameterDictionary parameters, FileLoc loc)
    {
        this.name = internedStrings.Lookup(name);
        this.parameters = parameters;
        this.loc = loc;
    }

    public override string ToString() => $"[ SceneEntity name: {name} parameters: {parameters} loc: {loc} ]";
}

public class TransformedSceneEntity : SceneEntity
{
    public AnimatedTransform renderFromObject = new();

    public TransformedSceneEntity() { }

    public TransformedSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                  AnimatedTransform renderFromObject)
        : base(name, parameters, loc)
    {
        this.renderFromObject = renderFromObject;
    }

    public override string ToString() =>
        $"[ TransformedSeneEntity name: {name} parameters: {parameters} loc: {loc} " +
        $"renderFromObject: {renderFromObject} ]";
}

// CameraSceneEntity Definition
public sealed class CameraSceneEntity : SceneEntity
{
    public CameraTransform cameraTransform = new();
    public string medium = "";

    // CameraSceneEntity Public Methods
    public CameraSceneEntity() { }

    public CameraSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                             CameraTransform cameraTransform, string medium)
        : base(name, parameters, loc)
    {
        this.cameraTransform = cameraTransform;
        this.medium = medium;
    }

    public override string ToString() =>
        $"[ CameraSeneEntity name: {name} parameters: {parameters} loc: {loc} " +
        $"cameraTransform: {cameraTransform} medium: {medium} ]";
}

public sealed class ShapeSceneEntity : SceneEntity
{
    public Transform renderFromObject = null!, objectFromRender = null!;
    public bool reverseOrientation = false;
    public int materialIndex; // one of these two...  std::variant?
    public string materialName = "";
    public int lightIndex = -1;
    public string insideMedium = "", outsideMedium = "";

    public ShapeSceneEntity() { }

    public ShapeSceneEntity(string name, ParameterDictionary parameters, FileLoc loc, Transform renderFromObject,
                            Transform objectFromRender, bool reverseOrientation, int materialIndex,
                            string materialName, int lightIndex, string insideMedium, string outsideMedium)
        : base(name, parameters, loc)
    {
        this.renderFromObject = renderFromObject;
        this.objectFromRender = objectFromRender;
        this.reverseOrientation = reverseOrientation;
        this.materialIndex = materialIndex;
        this.materialName = materialName;
        this.lightIndex = lightIndex;
        this.insideMedium = insideMedium;
        this.outsideMedium = outsideMedium;
    }

    public override string ToString() =>
        $"[ ShapeSeneEntity name: {name} parameters: {parameters} loc: {loc} " +
        $"renderFromObject: {renderFromObject} objectFromRender: {objectFromRender} " +
        $"reverseOrientation: {(reverseOrientation ? "true" : "false")} materialIndex: {materialIndex} " +
        $"materialName: {materialName} lightIndex: {lightIndex} insideMedium: {insideMedium} " +
        $"outsideMedium: {outsideMedium}]";
}

public sealed class AnimatedShapeSceneEntity : TransformedSceneEntity
{
    public Transform identity = null!;
    public bool reverseOrientation = false;
    public int materialIndex; // one of these two...  std::variant?
    public string materialName = "";
    public int lightIndex = -1;
    public string insideMedium = "", outsideMedium = "";

    public AnimatedShapeSceneEntity() { }

    public AnimatedShapeSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                                    AnimatedTransform renderFromObject, Transform identity, bool reverseOrientation,
                                    int materialIndex, string materialName, int lightIndex, string insideMedium,
                                    string outsideMedium)
        : base(name, parameters, loc, renderFromObject)
    {
        this.identity = identity;
        this.reverseOrientation = reverseOrientation;
        this.materialIndex = materialIndex;
        this.materialName = materialName;
        this.lightIndex = lightIndex;
        this.insideMedium = insideMedium;
        this.outsideMedium = outsideMedium;
    }

    public override string ToString() =>
        $"[ AnimatedShapeSeneEntity name: {name} parameters: {parameters} loc: {loc} " +
        $"renderFromObject: {renderFromObject} reverseOrientation: {(reverseOrientation ? "true" : "false")} " +
        $"materialIndex: {materialIndex} materialName: {materialName} insideMedium: {insideMedium} " +
        $"outsideMedium: {outsideMedium}]";
}

public sealed class InstanceDefinitionSceneEntity
{
    public string name = "";
    public FileLoc loc = new();
    public List<ShapeSceneEntity> shapes = [];
    public List<AnimatedShapeSceneEntity> animatedShapes = [];

    public InstanceDefinitionSceneEntity() { }

    public InstanceDefinitionSceneEntity(string name, FileLoc loc)
    {
        this.name = SceneEntity.internedStrings.Lookup(name);
        this.loc = loc;
    }

    public override string ToString() =>
        $"[ InstanceDefinitionSceneEntity name: {name} loc: {loc}  shapes: [ {string.Join(", ", shapes)} ] " +
        $"animatedShapes: [ {string.Join(", ", animatedShapes)} ] ]";
}

public sealed class LightSceneEntity : TransformedSceneEntity
{
    public string medium = "";

    public LightSceneEntity() { }

    public LightSceneEntity(string name, ParameterDictionary parameters, FileLoc loc,
                            AnimatedTransform renderFromLight, string medium)
        : base(name, parameters, loc, renderFromLight)
    {
        this.medium = medium;
    }

    public override string ToString() =>
        $"[ LightSeneEntity name: {name} parameters: {parameters} loc: {loc} " +
        $"renderFromObject: {renderFromObject} medium: {medium} ]";
}

public sealed class InstanceSceneEntity
{
    public string name = "";
    public FileLoc loc = new();
    public AnimatedTransform? renderFromInstanceAnim = null;
    public Transform? renderFromInstance = null;

    public InstanceSceneEntity() { }

    public InstanceSceneEntity(string n, FileLoc loc, AnimatedTransform renderFromInstanceAnim)
    {
        name = SceneEntity.internedStrings.Lookup(n);
        this.loc = loc;
        this.renderFromInstanceAnim = renderFromInstanceAnim;
        CHECK(this.renderFromInstanceAnim.IsAnimated());
    }

    public InstanceSceneEntity(string n, FileLoc loc, Transform renderFromInstance)
    {
        name = SceneEntity.internedStrings.Lookup(n);
        this.loc = loc;
        this.renderFromInstance = renderFromInstance;
    }

    public override string ToString() =>
        $"[ InstanceSeneEntity name: {name} loc: {loc} " +
        $"renderFromInstanceAnim: {renderFromInstanceAnim?.ToString() ?? "nullptr"} " +
        $"renderFromInstance: {renderFromInstance?.ToString() ?? "nullptr"} ]";
}

// TransformSet Definition
public struct TransformSet
{
    // MaxTransforms Definition
    public const int MaxTransforms = 2;

    private static readonly Transform identity = new();

    // (Null means the identity, so default(TransformSet) is valid.)
    private Transform? t0, t1;

    // TransformSet Public Methods
    public Transform this[int i]
    {
        readonly get
        {
            CHECK_GE(i, 0);
            CHECK_LT(i, MaxTransforms);
            return (i == 0 ? t0 : t1) ?? identity;
        }
        set
        {
            CHECK_GE(i, 0);
            CHECK_LT(i, MaxTransforms);
            if (i == 0)
                t0 = value;
            else
                t1 = value;
        }
    }

    public static TransformSet Inverse(TransformSet ts)
    {
        TransformSet tInv = new();
        for (int i = 0; i < MaxTransforms; ++i)
            tInv[i] = Util.Inverse(ts[i]);
        return tInv;
    }

    public readonly bool IsAnimated()
    {
        for (int i = 0; i < MaxTransforms - 1; ++i)
            if (this[i] != this[i + 1])
                return true;
        return false;
    }
}

// BasicScene Definition
public sealed class BasicScene
{
    // BasicScene Public Members
    public SceneEntity integrator = new(), accelerator = new();
    public RGBColorSpace? filmColorSpace;
    public List<ShapeSceneEntity> shapes = [];
    public List<AnimatedShapeSceneEntity> animatedShapes = [];
    public List<InstanceSceneEntity> instances = [];
    public Dictionary<string, InstanceDefinitionSceneEntity> instanceDefinitions = [];

    // The entities of the filter, film, sampler and camera, for tests.
    public SceneEntity filterEntity = new(), filmEntity = new(), samplerEntity = new();
    public CameraSceneEntity cameraEntity = new();
    // The media's entities, for tests (pbrt only keeps the jobs that
    // create them).
    public Dictionary<string, MediumSceneEntity> media = [];
    // The lights' entities, for tests (pbrt only keeps the jobs that
    // create them).
    public List<LightSceneEntity> lights = [];

    // BasicScene Private Members
    private Camera? camera;
    private Film? film;
    private readonly object cameraJobMutex = new();
    private AsyncJob<Camera>? cameraJob;
    private Sampler? sampler;
    private readonly object samplerJobMutex = new();
    private AsyncJob<Sampler>? samplerJob;
    private readonly object materialMutex = new();
    private readonly Dictionary<string, AsyncJob<Image>> normalMapJobs = [];
    private readonly Dictionary<string, Image> normalMaps = [];

    private readonly List<(string name, SceneEntity material)> namedMaterials = [];
    private readonly List<SceneEntity> materials = [];

    private readonly object lightMutex = new();
    private readonly List<AsyncJob<Light>> lightJobs = [];
    private readonly object areaLightMutex = new();
    private readonly List<SceneEntity> areaLights = [];

    private readonly object textureMutex = new();
    private readonly List<(string name, TextureSceneEntity texture)> serialFloatTextures = [];
    private readonly List<(string name, TextureSceneEntity texture)> serialSpectrumTextures = [];
    private readonly List<(string name, TextureSceneEntity texture)> asyncSpectrumTextures = [];
    private readonly Dictionary<string, AsyncJob<FloatTexture>> floatTextureJobs = [];
    private readonly Dictionary<string, AsyncJob<SpectrumTexture>> spectrumTextureJobs = [];
    private readonly HashSet<string> loadingTextureFilenames = [];
    private int nMissingTextures = 0;

    private readonly object mediaMutex = new();
    private readonly Dictionary<string, AsyncJob<Medium>> mediumJobs = [];
    private readonly Dictionary<string, Medium> mediaMap = [];
    private readonly object shapeMutex = new(), animatedShapeMutex = new();
    private readonly object instanceDefinitionMutex = new(), instanceUseMutex = new();

    // Read-only views of what's been added, for tests.
    public IReadOnlyList<(string name, SceneEntity material)> NamedMaterials => namedMaterials;
    public IReadOnlyList<SceneEntity> Materials => materials;
    public IReadOnlyList<SceneEntity> AreaLights => areaLights;
    public IReadOnlyList<(string name, TextureSceneEntity texture)> SerialFloatTextures => serialFloatTextures;
    public IReadOnlyList<(string name, TextureSceneEntity texture)> SerialSpectrumTextures => serialSpectrumTextures;
    public IReadOnlyList<(string name, TextureSceneEntity texture)> AsyncSpectrumTextures => asyncSpectrumTextures;
    public IEnumerable<string> AsyncFloatTextureNames => floatTextureJobs.Keys;
    public int NMissingTextures => nMissingTextures;

    // BasicScene Public Methods
    public BasicScene() { }

    public void SetOptions(SceneEntity filter, SceneEntity film, CameraSceneEntity camera, SceneEntity sampler,
                           SceneEntity integ, SceneEntity accel)
    {
        // Store information for specified integrator and accelerator
        filmColorSpace = film.parameters.ColorSpace();
        integrator = integ;
        accelerator = accel;

        // It's a little ugly to poke into the camera's parameters here, but we
        // have this circular dependency that Camera::Create() expects a
        // Film, yet now the film needs to know the exposure time from
        // the camera....
        Float exposureTime = camera.parameters.GetOneFloat("shutterclose", 1f) -
                             camera.parameters.GetOneFloat("shutteropen", 0f);
        if (exposureTime <= 0)
            ErrorExit(camera.loc, "The specified camera shutter times imply that the shutter " +
                                  "does not open.  A black image will result.");

        filterEntity = filter;
        filmEntity = film;
        cameraEntity = camera;
        samplerEntity = sampler;

        // Immediately create filter and film
        LOG_VERBOSE($"Starting to create filter and film");
        Filter filt = Filter.Create(filter.name, filter.parameters, filter.loc);

        this.film = Film.Create(film.name, film.parameters, exposureTime, camera.cameraTransform, filt, film.loc);
        LOG_VERBOSE($"Finished creating filter and film");

        // Enqueue asynchronous job to create sampler
        samplerJob = RunAsync(() =>
        {
            LOG_VERBOSE($"Starting to create sampler");
            Point2i res = this.film.FullResolution();
            return Sampler.Create(sampler.name, sampler.parameters, res, sampler.loc);
        });

        // Enqueue asynchronous job to create camera
        cameraJob = RunAsync(() =>
        {
            LOG_VERBOSE($"Starting to create camera");
            Medium? cameraMedium = GetMedium(camera.medium, camera.loc);

            Camera c = Camera.Create(camera.name, camera.parameters, cameraMedium, camera.cameraTransform, this.film,
                                     camera.loc);
            LOG_VERBOSE($"Finished creating camera");
            return c;
        });
    }

    public Film GetFilm() => film!;

    public Camera GetCamera()
    {
        lock (cameraJobMutex)
        {
            while (camera == null)
            {
                if (cameraJob!.TryGetResult(cameraJobMutex, out Camera? c))
                    camera = c;
            }
        }
        LOG_VERBOSE($"Retrieved Camera from future");
        return camera;
    }

    public Sampler GetSampler()
    {
        lock (samplerJobMutex)
        {
            while (sampler == null)
            {
                if (samplerJob!.TryGetResult(samplerJobMutex, out Sampler? s))
                    sampler = s;
            }
        }
        LOG_VERBOSE($"Retrieved Sampler from future");
        return sampler;
    }

    // BasicScene Private Methods
    private Medium? GetMedium(string name, FileLoc? loc)
    {
        if (name.Length == 0)
            return null;

        lock (mediaMutex)
        {
            while (true)
            {
                if (mediaMap.TryGetValue(name, out Medium? m))
                    return m;
                if (!mediumJobs.TryGetValue(name, out AsyncJob<Medium>? job))
                    ErrorExit(loc, $"{name}: medium is not defined.");
                if (job!.TryGetResult(mediaMutex, out m))
                {
                    mediaMap[name] = m;
                    mediumJobs.Remove(name);
                    return m;
                }
            }
        }
    }

    public void AddMedium(MediumSceneEntity medium)
    {
        // Define _create_ lambda function for _Medium_ creation
        Medium create()
        {
            string type = medium.parameters.GetOneString("type", "");
            // Check for missing medium ``type'' or animated medium transform
            if (type.Length == 0)
                ErrorExit(medium.loc, "No parameter \"string type\" found for medium.");
            if (medium.renderFromObject.IsAnimated())
                Warning(medium.loc, "Animated transformation provided for medium. Only the " +
                                    "start transform will be used.");

            return Medium.Create(type, medium.parameters, medium.renderFromObject.startTransform, medium.loc);
        }

        lock (mediaMutex)
        {
            media[medium.name] = medium;
            mediumJobs[medium.name] = RunAsync(create);
        }
    }

    public void AddNamedMaterial(string name, SceneEntity material)
    {
        lock (materialMutex)
        {
            startLoadingNormalMaps(material.parameters);
            namedMaterials.Add((name, material));
        }
    }

    public int AddMaterial(SceneEntity material)
    {
        lock (materialMutex)
        {
            startLoadingNormalMaps(material.parameters);
            materials.Add(material);
            return materials.Count - 1;
        }
    }

    private void startLoadingNormalMaps(ParameterDictionary parameters)
    {
        string filename = ResolveFilename(parameters.GetOneString("normalmap", ""));
        if (filename.Length == 0)
            return;

        // Overload materialMutex, which we already hold, for the futures...
        if (normalMapJobs.ContainsKey(filename))
            // It's already in flight.
            return;

        normalMapJobs[filename] = RunAsync(() =>
        {
            ImageAndMetadata immeta = Image.Read(filename, ColorEncoding.Linear);
            Image image = immeta.image;
            ImageChannelDesc rgbDesc = image.GetChannelDesc(["R", "G", "B"]);
            if (!rgbDesc)
                ErrorExit($"{filename}: normal map image must contain R, G, and B channels");
            return image.SelectChannels(rgbDesc);
        });
    }

    // Waits for the normal maps to be loaded and returns them (the start of
    // pbrt's CreateMaterials()).
    public IReadOnlyDictionary<string, Image> GetNormalMaps()
    {
        LOG_VERBOSE($"Starting to consume {normalMapJobs.Count} normal map futures");
        lock (materialMutex)
        {
            foreach ((string fn, AsyncJob<Image> job) in normalMapJobs)
            {
                CHECK(!normalMaps.ContainsKey(fn));
                normalMaps[fn] = job.GetResult();
            }
            normalMapJobs.Clear();
        }
        LOG_VERBOSE($"Finished consuming normal map futures");
        return normalMaps;
    }

    public void CreateMaterials(NamedTextures textures, Dictionary<string, Material?> namedMaterialsOut,
                                List<Material?> materialsOut)
    {
        GetNormalMaps();

        lock (materialMutex)
        {
            // Named materials
            foreach ((string name, SceneEntity mtl) in namedMaterials)
            {
                if (namedMaterialsOut.ContainsKey(name))
                {
                    ErrorExit(mtl.loc, $"{name}: trying to redefine named material.");
                    continue;
                }

                string type = mtl.parameters.GetOneString("type", "");
                if (type.Length == 0)
                {
                    ErrorExit(mtl.loc, $"{name}: \"string type\" not provided in named material's parameters.");
                    continue;
                }

                string fn = ResolveFilename(mtl.parameters.GetOneString("normalmap", ""));
                Image? normalMap = null;
                if (fn.Length > 0)
                {
                    CHECK(normalMaps.ContainsKey(fn));
                    normalMap = normalMaps[fn];
                }

                TextureParameterDictionary texDict = new(mtl.parameters, textures);
                Material? m = Material.Create(type, texDict, normalMap, namedMaterialsOut, mtl.loc);
                namedMaterialsOut[name] = m;
            }

            // Regular materials
            materialsOut.Capacity = Math.Max(materialsOut.Capacity, materials.Count);
            foreach (SceneEntity mtl in materials)
            {
                string fn = ResolveFilename(mtl.parameters.GetOneString("normalmap", ""));
                Image? normalMap = null;
                if (fn.Length > 0)
                {
                    CHECK(normalMaps.ContainsKey(fn));
                    normalMap = normalMaps[fn];
                }

                TextureParameterDictionary texDict = new(mtl.parameters, textures);
                Material? m = Material.Create(mtl.name, texDict, normalMap, namedMaterialsOut, mtl.loc);
                materialsOut.Add(m);
            }
        }
    }

    public List<Light> CreateLights(NamedTextures textures, Dictionary<int, List<Light>> shapeIndexToAreaLights)
    {
        Medium? findMedium(string s, FileLoc? loc)
        {
            if (s.Length == 0)
                return null;

            if (!mediaMap.TryGetValue(s, out Medium? m))
                ErrorExit(loc, $"{s}: medium not defined");
            return m;
        }

        FloatTexture? getAlphaTexture(ParameterDictionary parameters, FileLoc? loc)
        {
            string alphaTexName = parameters.GetTexture("alpha");
            if (alphaTexName.Length > 0)
            {
                if (textures.floatTextures.TryGetValue(alphaTexName, out FloatTexture? tex))
                    return tex;
                else
                    ErrorExit(loc, $"{alphaTexName}: couldn't find float texture for \"alpha\" parameter.");
                return null;
            }
            else if (parameters.GetOneFloat("alpha", 1f) is Float alpha && alpha < 1f)
                return new FloatConstantTexture(alpha);
            else
                return null;
        }

        LOG_VERBOSE($"Starting area lights");
        List<Light> lights = [];
        // Area Lights
        for (int i = 0; i < shapes.Count; ++i)
        {
            ShapeSceneEntity sh = shapes[i];

            if (sh.lightIndex == -1)
                continue;

            string materialName;
            if (sh.materialName.Length > 0)
            {
                int index = namedMaterials.FindIndex(nm => nm.name == sh.materialName);
                if (index == -1)
                    ErrorExit(sh.loc, $"{sh.materialName}: no named material defined.");
                SceneEntity mtl = namedMaterials[index].material;
                CHECK(mtl.parameters.GetStringArray("type").Count > 0);
                materialName = mtl.parameters.GetOneString("type", "");
            }
            else
            {
                CHECK_LT(sh.materialIndex, materials.Count);
                materialName = materials[sh.materialIndex].name;
            }
            if (materialName == "interface" || materialName == "none" || materialName == "")
            {
                Warning(sh.loc, "Ignoring area light specification for shape with \"interface\" material.");
                continue;
            }

            List<Shape> shapeObjects = Shape.Create(sh.name, sh.renderFromObject, sh.objectFromRender,
                                                    sh.reverseOrientation, sh.parameters, textures.floatTextures,
                                                    sh.loc);

            FloatTexture? alphaTex = getAlphaTexture(sh.parameters, sh.loc);

            MediumInterface mi = new(findMedium(sh.insideMedium, sh.loc), findMedium(sh.outsideMedium, sh.loc));

            List<Light> shapeLights = [];
            SceneEntity areaLightEntity = areaLights[sh.lightIndex];
            foreach (Shape ps in shapeObjects)
            {
                Light area = Light.CreateArea(areaLightEntity.name, areaLightEntity.parameters, sh.renderFromObject,
                                              mi, ps, alphaTex, areaLightEntity.loc);
                lights.Add(area);
                shapeLights.Add(area);
            }

            shapeIndexToAreaLights[i] = shapeLights;
        }

        LOG_VERBOSE($"Finished area lights");

        LOG_VERBOSE($"Starting to consume non-area light futures");
        lock (lightMutex)
        {
            foreach (AsyncJob<Light> job in lightJobs)
                lights.Add(job.GetResult());
        }
        LOG_VERBOSE($"Finished consuming non-area light futures");

        return lights;
    }

    public Dictionary<string, Medium> CreateMedia()
    {
        lock (mediaMutex)
        {
            if (mediumJobs.Count > 0)
            {
                // Consume results for asynchronously-created _Medium_ objects
                LOG_VERBOSE($"Consume media futures start");
                foreach (var (name, job) in mediumJobs)
                {
                    while (!mediaMap.ContainsKey(name))
                    {
                        if (job.TryGetResult(mediaMutex, out Medium? med))
                            mediaMap[name] = med;
                    }
                }
                LOG_VERBOSE($"Consume media futures finished");
                mediumJobs.Clear();
            }
            return new Dictionary<string, Medium>(mediaMap);
        }
    }

    public Primitive? CreateAggregate(NamedTextures textures, Dictionary<int, List<Light>> shapeIndexToAreaLights,
                                      Dictionary<string, Medium> media, Dictionary<string, Material?> namedMaterials,
                                      List<Material?> materials)
    {
        Medium? findMedium(string s, FileLoc? loc)
        {
            if (s.Length == 0)
                return null;

            if (!media.TryGetValue(s, out Medium? m))
                ErrorExit(loc, $"{s}: medium not defined");
            return m;
        }

        // Primitives
        FloatTexture? getAlphaTexture(ParameterDictionary parameters, FileLoc? loc)
        {
            string alphaTexName = parameters.GetTexture("alpha");
            if (alphaTexName.Length > 0)
            {
                if (textures.floatTextures.TryGetValue(alphaTexName, out FloatTexture? tex))
                    return tex;
                else
                    ErrorExit(loc, $"{alphaTexName}: couldn't find float texture for \"alpha\" parameter.");
                return null;
            }
            else if (parameters.GetOneFloat("alpha", 1f) is Float alpha && alpha < 1f)
                return new FloatConstantTexture(alpha);
            else
                return null;
        }

        Material? findMaterial(string materialName, int materialIndex, FileLoc? loc)
        {
            if (materialName.Length > 0)
            {
                if (!namedMaterials.TryGetValue(materialName, out Material? m))
                    ErrorExit(loc, $"{materialName}: no named material defined.");
                return m;
            }
            CHECK_LT(materialIndex, materials.Count);
            return materials[materialIndex];
        }

        // Non-animated shapes
        List<Primitive> CreatePrimitivesForShapes(List<ShapeSceneEntity> shapes)
        {
            // Parallelize Shape::Create calls, which will in turn
            // parallelize PLY file loading, etc...
            List<Shape>[] shapeVectors = new List<Shape>[shapes.Count];
            ParallelFor(0, shapes.Count, (long i) =>
            {
                ShapeSceneEntity sh = shapes[(int)i];
                shapeVectors[i] = Shape.Create(sh.name, sh.renderFromObject, sh.objectFromRender,
                                               sh.reverseOrientation, sh.parameters, textures.floatTextures, sh.loc);
            });

            List<Primitive> primitives = [];
            for (int i = 0; i < shapes.Count; ++i)
            {
                ShapeSceneEntity sh = shapes[i];
                List<Shape> shapeObjs = shapeVectors[i];
                if (shapeObjs.Count == 0)
                    continue;

                FloatTexture? alphaTex = getAlphaTexture(sh.parameters, sh.loc);
                sh.parameters.ReportUnused(); // do now so can grab alpha...

                Material? mtl = findMaterial(sh.materialName, sh.materialIndex, sh.loc);

                MediumInterface mi = new(findMedium(sh.insideMedium, sh.loc), findMedium(sh.outsideMedium, sh.loc));

                shapeIndexToAreaLights.TryGetValue(i, out List<Light>? areaLights);
                for (int j = 0; j < shapeObjs.Count; ++j)
                {
                    // Possibly create area light for shape
                    Light? area = null;
                    // Will not be present in the map if it has an "interface"
                    // material...
                    if (sh.lightIndex != -1 && areaLights != null)
                        area = areaLights[j];

                    if (area == null && !mi.IsMediumTransition() && alphaTex == null)
                        primitives.Add(new SimplePrimitive(shapeObjs[j], mtl));
                    else
                        primitives.Add(new GeometricPrimitive(shapeObjs[j], mtl, area, mi, alphaTex));
                }
            }
            return primitives;
        }

        LOG_VERBOSE($"Starting shapes");
        List<Primitive> primitives = CreatePrimitivesForShapes(shapes);

        shapes.Clear();

        // Animated shapes
        List<Primitive> CreatePrimitivesForAnimatedShapes(List<AnimatedShapeSceneEntity> shapes)
        {
            List<Primitive> primitives = new(shapes.Count);

            foreach (AnimatedShapeSceneEntity sh in shapes)
            {
                List<Shape> shapeObjs = Shape.Create(sh.name, sh.identity, sh.identity, sh.reverseOrientation,
                                                     sh.parameters, textures.floatTextures, sh.loc);
                if (shapeObjs.Count == 0)
                    continue;

                FloatTexture? alphaTex = getAlphaTexture(sh.parameters, sh.loc);
                sh.parameters.ReportUnused(); // do now so can grab alpha...

                // Create initial shape or shapes for animated shape

                Material? mtl = findMaterial(sh.materialName, sh.materialIndex, sh.loc);

                MediumInterface mi = new(findMedium(sh.insideMedium, sh.loc), findMedium(sh.outsideMedium, sh.loc));

                List<Primitive> prims = [];
                foreach (Shape s in shapeObjs)
                {
                    if (sh.lightIndex != -1)
                    {
                        CHECK(sh.renderFromObject.IsAnimated());
                        ErrorExit(sh.loc, "Animated area lights are not supported.");
                    }

                    if (!mi.IsMediumTransition() && alphaTex == null)
                        prims.Add(new SimplePrimitive(s, mtl));
                    else
                        prims.Add(new GeometricPrimitive(s, mtl, null /* area light */, mi, alphaTex));
                }

                // TODO: could try to be greedy or even segment them according
                // to same sh.renderFromObject...

                // Create single _Primitive_ for _prims_
                if (prims.Count > 1)
                {
                    Primitive bvh = new BVHAggregate(prims);
                    prims.Clear();
                    prims.Add(bvh);
                }
                primitives.Add(new AnimatedPrimitive(prims[0], sh.renderFromObject));
            }
            return primitives;
        }
        List<Primitive> animatedPrimitives = CreatePrimitivesForAnimatedShapes(animatedShapes);
        primitives.AddRange(animatedPrimitives);

        animatedShapes.Clear();
        LOG_VERBOSE($"Finished shapes");

        // Instance definitions
        LOG_VERBOSE($"Starting instances");
        Dictionary<string, Primitive?> instanceDefinitionPrims = [];
        Lock instanceDefinitionsMutex = new();
        List<KeyValuePair<string, InstanceDefinitionSceneEntity>> instanceDefinitionList =
            [.. instanceDefinitions.OrderBy(kv => kv.Key, StringComparer.Ordinal)];
        ParallelFor(0, instanceDefinitionList.Count, (long i) =>
        {
            KeyValuePair<string, InstanceDefinitionSceneEntity> inst = instanceDefinitionList[(int)i];

            List<Primitive> instancePrimitives = CreatePrimitivesForShapes(inst.Value.shapes);
            List<Primitive> movingInstancePrimitives = CreatePrimitivesForAnimatedShapes(inst.Value.animatedShapes);
            instancePrimitives.AddRange(movingInstancePrimitives);

            if (instancePrimitives.Count > 1)
            {
                Primitive bvh = new BVHAggregate(instancePrimitives);
                instancePrimitives = [bvh];
            }

            lock (instanceDefinitionsMutex)
            {
                if (instancePrimitives.Count == 0)
                    instanceDefinitionPrims[inst.Key] = null;
                else
                    instanceDefinitionPrims[inst.Key] = instancePrimitives[0];
            }
        });

        instanceDefinitions.Clear();

        // Instances
        foreach (InstanceSceneEntity inst in instances)
        {
            if (!instanceDefinitionPrims.TryGetValue(inst.name, out Primitive? prim))
                ErrorExit(inst.loc, $"{inst.name}: object instance not defined");

            if (prim == null)
                // empty instance
                continue;

            if (inst.renderFromInstance != null)
                primitives.Add(new TransformedPrimitive(prim, inst.renderFromInstance));
            else
                primitives.Add(new AnimatedPrimitive(prim, inst.renderFromInstanceAnim!));
        }

        instances.Clear();
        LOG_VERBOSE($"Finished instances");

        // Accelerator
        Primitive? aggregate = null;
        LOG_VERBOSE($"Starting top-level accelerator");
        if (primitives.Count > 0)
            aggregate = CreateAccelerator(accelerator.name, primitives, accelerator.parameters);
        LOG_VERBOSE($"Finished top-level accelerator");
        return aggregate;
    }

    public Integrator CreateIntegrator(Camera camera, Sampler sampler, Primitive? accel, List<Light> lights) =>
        Integrator.Create(integrator.name, integrator.parameters, camera, sampler, accel, lights, filmColorSpace!,
                          integrator.loc);

    public NamedTextures CreateTextures()
    {
        NamedTextures textures = new();

        if (nMissingTextures > 0)
            ErrorExit($"{nMissingTextures} missing textures");

        // Consume futures
        LOG_VERBOSE($"Starting to consume texture futures");
        lock (textureMutex)
        {
            foreach ((string name, AsyncJob<FloatTexture> job) in floatTextureJobs)
                textures.floatTextures[name] = job.GetResult();
            floatTextureJobs.Clear();
            foreach ((string name, AsyncJob<SpectrumTexture> job) in spectrumTextureJobs)
                textures.albedoSpectrumTextures[name] = job.GetResult();
            spectrumTextureJobs.Clear();
        }
        LOG_VERBOSE($"Finished consuming texture futures");

        LOG_VERBOSE($"Starting to create remaining textures");
        // Create the other SpectrumTypes for the spectrum textures.
        foreach ((string name, TextureSceneEntity tex) in asyncSpectrumTextures)
        {
            Transform renderFromTexture = tex.renderFromObject.startTransform;
            // These are all image textures, so no textures are needed, as
            // earlier.
            TextureParameterDictionary texDict = new(tex.parameters, new NamedTextures());

            // These should be fast since they should hit the texture cache
            SpectrumTexture unboundedTex =
                SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Unbounded, tex.loc);
            SpectrumTexture illumTex =
                SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Illuminant, tex.loc);

            textures.unboundedSpectrumTextures[name] = unboundedTex;
            textures.illuminantSpectrumTextures[name] = illumTex;
        }

        // And do the rest serially
        foreach ((string name, TextureSceneEntity tex) in serialFloatTextures)
        {
            Transform renderFromTexture = tex.renderFromObject.startTransform;
            TextureParameterDictionary texDict = new(tex.parameters, textures);
            FloatTexture t = FloatTexture.Create(tex.name, renderFromTexture, texDict, tex.loc);
            textures.floatTextures[name] = t;
        }

        foreach ((string name, TextureSceneEntity tex) in serialSpectrumTextures)
        {
            if (tex.renderFromObject.IsAnimated())
                Warning(tex.loc, "Animated world to texture transform not supported. Using start transform.");

            Transform renderFromTexture = tex.renderFromObject.startTransform;
            TextureParameterDictionary texDict = new(tex.parameters, textures);
            SpectrumTexture albedoTex =
                SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Albedo, tex.loc);
            SpectrumTexture unboundedTex =
                SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Unbounded, tex.loc);
            SpectrumTexture illumTex =
                SpectrumTexture.Create(tex.name, renderFromTexture, texDict, SpectrumType.Illuminant, tex.loc);

            textures.albedoSpectrumTextures[name] = albedoTex;
            textures.unboundedSpectrumTextures[name] = unboundedTex;
            textures.illuminantSpectrumTextures[name] = illumTex;
        }

        LOG_VERBOSE($"Done creating textures");
        return textures;
    }

    public void AddFloatTexture(string name, TextureSceneEntity texture)
    {
        if (texture.renderFromObject.IsAnimated())
            Warning(texture.loc, "Animated world to texture transforms are not supported. " +
                                 "Using start transform.");

        lock (textureMutex)
        {
            if (texture.name != "imagemap" && texture.name != "ptex")
            {
                serialFloatTextures.Add((name, texture));
                return;
            }

            string filename = ResolveFilename(texture.parameters.GetOneString("filename", ""));
            if (filename.Length == 0)
            {
                Error(texture.loc, "\"string filename\" not provided for image texture.");
                ++nMissingTextures;
                return;
            }
            if (!FileExists(filename))
            {
                Error(texture.loc, $"{filename}: file not found.");
                ++nMissingTextures;
                return;
            }

            if (loadingTextureFilenames.Contains(filename))
            {
                serialFloatTextures.Add((name, texture));
                return;
            }
            loadingTextureFilenames.Add(filename);

            floatTextureJobs[name] = RunAsync(() =>
            {
                Transform renderFromTexture = texture.renderFromObject.startTransform;
                // Pass no textures, since they shouldn't be accessed anyway.
                TextureParameterDictionary texDict = new(texture.parameters, new NamedTextures());
                return FloatTexture.Create(texture.name, renderFromTexture, texDict, texture.loc);
            });
        }
    }

    public void AddSpectrumTexture(string name, TextureSceneEntity texture)
    {
        lock (textureMutex)
        {
            if (texture.name != "imagemap" && texture.name != "ptex")
            {
                serialSpectrumTextures.Add((name, texture));
                return;
            }

            string filename = ResolveFilename(texture.parameters.GetOneString("filename", ""));
            if (filename.Length == 0)
            {
                Error(texture.loc, "\"string filename\" not provided for image texture.");
                ++nMissingTextures;
                return;
            }
            if (!FileExists(filename))
            {
                Error(texture.loc, $"{filename}: file not found.");
                ++nMissingTextures;
                return;
            }

            if (loadingTextureFilenames.Contains(filename))
            {
                serialSpectrumTextures.Add((name, texture));
                return;
            }
            loadingTextureFilenames.Add(filename);

            asyncSpectrumTextures.Add((name, texture));

            spectrumTextureJobs[name] = RunAsync(() =>
            {
                Transform renderFromTexture = texture.renderFromObject.startTransform;
                // No textures, as with float textures.
                TextureParameterDictionary texDict = new(texture.parameters, new NamedTextures());
                // Only create SpectrumType::Albedo for now; will get the other two
                // types in CreateTextures().
                return SpectrumTexture.Create(texture.name, renderFromTexture, texDict, SpectrumType.Albedo,
                                              texture.loc);
            });
        }
    }

    public void AddLight(LightSceneEntity light)
    {
        Medium? lightMedium = GetMedium(light.medium, light.loc);
        lock (lightMutex)
        {
            if (light.renderFromObject.IsAnimated())
                Warning(light.loc, "Animated lights aren't supported. Using the start transform.");
            lights.Add(light);

            lightJobs.Add(RunAsync(() =>
                Light.Create(light.name, light.parameters, light.renderFromObject.startTransform,
                             GetCamera().GetCameraTransform(), lightMedium, light.loc)));
        }
    }

    public int AddAreaLight(SceneEntity light)
    {
        lock (areaLightMutex)
        {
            areaLights.Add(light);
            return areaLights.Count - 1;
        }
    }

    public void AddShapes(IEnumerable<ShapeSceneEntity> s)
    {
        lock (shapeMutex)
            shapes.AddRange(s);
    }

    public void AddAnimatedShape(AnimatedShapeSceneEntity shape)
    {
        lock (animatedShapeMutex)
            animatedShapes.Add(shape);
    }

    public void AddInstanceDefinition(InstanceDefinitionSceneEntity instance)
    {
        lock (instanceDefinitionMutex)
            instanceDefinitions[instance.name] = instance;
    }

    public void AddInstanceUses(IEnumerable<InstanceSceneEntity> instanceUses)
    {
        lock (instanceUseMutex)
            instances.AddRange(instanceUses);
    }

    // (pbrt's Done() reports unused textures in code that's disabled.)
    public void Done() { }
}

// BasicSceneBuilder Definition
public sealed class BasicSceneBuilder : ParserTarget
{
    private static readonly StatCounter nObjectInstancesCreated = new("Scene/Object instances created");

    // BasicSceneBuilder::GraphicsState Definition
    private sealed class GraphicsState
    {
        // GraphicsState Public Members
        public string currentInsideMedium = "", currentOutsideMedium = "";

        public int currentMaterialIndex = 0;
        public string currentMaterialName = "";

        public string areaLightName = "";
        public ParameterDictionary areaLightParams = new();
        public FileLoc areaLightLoc = new();

        public List<ParsedParameter> shapeAttributes = [];
        public List<ParsedParameter> lightAttributes = [];
        public List<ParsedParameter> materialAttributes = [];
        public List<ParsedParameter> mediumAttributes = [];
        public List<ParsedParameter> textureAttributes = [];
        public bool reverseOrientation = false;
        public RGBColorSpace colorSpace = RGBColorSpace.sRGB;
        public TransformSet ctm;
        public uint activeTransformBits = AllTransformsBits;
        public Float transformStartTime = 0, transformEndTime = 1;

        // GraphicsState Public Methods
        public void ForActiveTransforms(Func<Transform, Transform> func)
        {
            for (int i = 0; i < TransformSet.MaxTransforms; ++i)
                if ((activeTransformBits & (1 << i)) != 0)
                    ctm[i] = func(ctm[i]);
        }

        public GraphicsState Clone()
        {
            GraphicsState gs = (GraphicsState)MemberwiseClone();
            gs.shapeAttributes = [.. shapeAttributes];
            gs.lightAttributes = [.. lightAttributes];
            gs.materialAttributes = [.. materialAttributes];
            gs.mediumAttributes = [.. mediumAttributes];
            gs.textureAttributes = [.. textureAttributes];
            return gs;
        }
    }

    // BasicSceneBuilder Private Members
    private readonly BasicScene scene;
    public enum BlockState { OptionsBlock, WorldBlock }
    internal BlockState currentBlock = BlockState.OptionsBlock;
    private GraphicsState graphicsState = new();
    private const int StartTransformBits = 1 << 0;
    private const int EndTransformBits = 1 << 1;
    private const int AllTransformsBits = (1 << TransformSet.MaxTransforms) - 1;
    private readonly Dictionary<string, TransformSet> namedCoordinateSystems = [];
    private Transform renderFromWorld = new();
    private readonly InternCache<Transform> transformCache = new();
    private readonly List<GraphicsState> pushedGraphicsStates = [];
    private readonly List<(char, FileLoc)> pushStack = []; // 'a': attribute, 'o': object

    private sealed class ActiveInstanceDefinition(string name, FileLoc loc)
    {
        public readonly object mutex = new();
        public int activeImports = 1;
        public InstanceDefinitionSceneEntity entity = new(name, loc);
        public ActiveInstanceDefinition? parent = null;
    }

    private ActiveInstanceDefinition? activeInstanceDefinition = null;

    // Buffer these both to avoid mutex contention and so that they are
    // consistently ordered across runs.
    private List<ShapeSceneEntity> shapes = [];
    private List<InstanceSceneEntity> instanceUses = [];

    private readonly HashSet<string> namedMaterialNames = [], mediumNames = [];
    private readonly HashSet<string> floatTextureNames = [], spectrumTextureNames = [], instanceNames = [];
#pragma warning disable CS0414 // (unused in pbrt too)
    private int currentMaterialIndex = 0, currentLightIndex = -1;
#pragma warning restore CS0414
    private SceneEntity sampler = new();
    private SceneEntity film = new(), integrator = new(), filter = new(), accelerator = new();
    private CameraSceneEntity camera = new();

    // BasicSceneBuilder Private Methods
    private Transform RenderFromObject(int index) =>
        new((renderFromWorld * graphicsState.ctm[index]).GetMatrix());

    private AnimatedTransform RenderFromObject() =>
        new(RenderFromObject(0), graphicsState.transformStartTime, RenderFromObject(1),
            graphicsState.transformEndTime);

    private bool CTMIsAnimated() => graphicsState.ctm.IsAnimated();

    // API State Macros
    private bool VERIFY_OPTIONS(string func, FileLoc loc)
    {
        if (currentBlock == BlockState.WorldBlock)
        {
            ErrorExit(loc, "Options cannot be set inside world block; " + $"\"{func}\" is not allowed.");
            return false;
        }
        return true;
    }

    private bool VERIFY_WORLD(string func, FileLoc loc)
    {
        if (currentBlock == BlockState.OptionsBlock)
        {
            ErrorExit(loc, "Scene description must be inside world block; " + $"\"{func}\" is not allowed.");
            return false;
        }
        return true;
    }

    // BasicSceneBuilder Method Definitions
    public BasicSceneBuilder(BasicScene scene)
    {
        this.scene = scene;
        // Set scene defaults
        camera.name = SceneEntity.internedStrings.Lookup("perspective");
        sampler.name = SceneEntity.internedStrings.Lookup("zsobol");
        filter.name = SceneEntity.internedStrings.Lookup("gaussian");
        integrator.name = SceneEntity.internedStrings.Lookup("volpath");
        accelerator.name = SceneEntity.internedStrings.Lookup("bvh");

        film.name = SceneEntity.internedStrings.Lookup("rgb");
        film.parameters = new ParameterDictionary([], RGBColorSpace.sRGB);

        ParameterDictionary dict = new([], RGBColorSpace.sRGB);
        currentMaterialIndex = scene.AddMaterial(new SceneEntity("diffuse", dict, new FileLoc()));
    }

    public override string ToString() =>
        $"[ BasicSceneBuilder camera: {camera} film: {film} sampler: {sampler} integrator: {integrator} " +
        $"filter: {filter} accelerator: {accelerator} ]";

    public override void ReverseOrientation(FileLoc loc)
    {
        if (!VERIFY_WORLD("ReverseOrientation", loc))
            return;
        graphicsState.reverseOrientation = !graphicsState.reverseOrientation;
    }

    public override void ColorSpace(string name, FileLoc loc)
    {
        if (RGBColorSpace.GetNamed(name) is RGBColorSpace cs)
            graphicsState.colorSpace = cs;
        else
            Error(loc, $"{name}: color space unknown");
    }

    public override void Identity(FileLoc loc) => graphicsState.ForActiveTransforms(t => new Transform());

    public override void Translate(Float dx, Float dy, Float dz, FileLoc loc) =>
        graphicsState.ForActiveTransforms(t => t * Util.Translate(new Vector3f(dx, dy, dz)));

    public override void CoordinateSystem(string origName, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        namedCoordinateSystems[name] = graphicsState.ctm;
    }

    public override void CoordSysTransform(string origName, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        if (namedCoordinateSystems.TryGetValue(name, out TransformSet ts))
            graphicsState.ctm = ts;
        else
            Warning(loc, $"Couldn't find named coordinate system \"{name}\"");
    }

    public override void Camera(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);

        if (!VERIFY_OPTIONS("Camera", loc))
            return;

        TransformSet cameraFromWorld = graphicsState.ctm;
        TransformSet worldFromCamera = TransformSet.Inverse(graphicsState.ctm);
        namedCoordinateSystems["camera"] = TransformSet.Inverse(cameraFromWorld);

        CameraTransform cameraTransform = new(new AnimatedTransform(
            worldFromCamera[0], graphicsState.transformStartTime, worldFromCamera[1],
            graphicsState.transformEndTime));
        renderFromWorld = cameraTransform.RenderFromWorld();

        camera = new CameraSceneEntity(name, dict, loc, cameraTransform, graphicsState.currentOutsideMedium);
    }

    public override void AttributeBegin(FileLoc loc)
    {
        if (!VERIFY_WORLD("AttributeBegin", loc))
            return;
        pushedGraphicsStates.Add(graphicsState.Clone());
        pushStack.Add(('a', loc));
    }

    public override void AttributeEnd(FileLoc loc)
    {
        if (!VERIFY_WORLD("AttributeEnd", loc))
            return;
        // Issue error on unmatched _AttributeEnd_
        if (pushedGraphicsStates.Count == 0)
        {
            Error(loc, "Unmatched AttributeEnd encountered. Ignoring it.");
            return;
        }

        // NOTE: must keep the following consistent with code in ObjectEnd
        graphicsState = pushedGraphicsStates[^1];
        pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);

        if (pushStack[^1].Item1 == 'o')
            ErrorExitDeferred(loc, $"Mismatched nesting: open ObjectBegin from {pushStack[^1].Item2} at AttributeEnd");
        else
            CHECK_EQ(pushStack[^1].Item1, 'a');
        pushStack.RemoveAt(pushStack.Count - 1);
    }

    public override void Attribute(string target, List<ParsedParameter> attrib, FileLoc loc)
    {
        List<ParsedParameter> currentAttributes;
        if (target == "shape")
            currentAttributes = graphicsState.shapeAttributes;
        else if (target == "light")
            currentAttributes = graphicsState.lightAttributes;
        else if (target == "material")
            currentAttributes = graphicsState.materialAttributes;
        else if (target == "medium")
            currentAttributes = graphicsState.mediumAttributes;
        else if (target == "texture")
            currentAttributes = graphicsState.textureAttributes;
        else
        {
            ErrorExitDeferred(loc, $"Unknown attribute target \"{target}\". Must be \"shape\", \"light\", " +
                                   "\"material\", \"medium\", or \"texture\".");
            return;
        }

        // Note that we hold on to the current color space and associate it
        // with the parameters...
        foreach (ParsedParameter p in attrib)
        {
            p.mayBeUnused = true;
            p.colorSpace = graphicsState.colorSpace;
            currentAttributes.Add(p);
        }
    }

    public override void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);
        if (!VERIFY_OPTIONS("Sampler", loc))
            return;
        sampler = new SceneEntity(name, dict, loc);
    }

    public override void WorldBegin(FileLoc loc)
    {
        if (!VERIFY_OPTIONS("WorldBegin", loc))
            return;
        // Reset graphics state for _WorldBegin_
        currentBlock = BlockState.WorldBlock;
        for (int i = 0; i < TransformSet.MaxTransforms; ++i)
            graphicsState.ctm[i] = new Transform();
        graphicsState.activeTransformBits = AllTransformsBits;
        namedCoordinateSystems["world"] = graphicsState.ctm;

        // Pass pre-_WorldBegin_ entities to _scene_
        scene.SetOptions(filter, film, camera, sampler, integrator, accelerator);
    }

    public override void MakeNamedMedium(string origName, List<ParsedParameter> parameters, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        // Issue error if medium _name_ is multiply defined
        if (mediumNames.Contains(name))
        {
            ErrorExitDeferred(loc, $"Named medium \"{name}\" redefined.");
            return;
        }
        mediumNames.Add(name);

        // Create _ParameterDictionary_ for medium and call _AddMedium()_
        ParameterDictionary dict = new(parameters, graphicsState.mediumAttributes, graphicsState.colorSpace);
        scene.AddMedium(new MediumSceneEntity(name, dict, loc, RenderFromObject()));
    }

    public override void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        if (!VERIFY_WORLD("LightSource", loc))
            return;
        ParameterDictionary dict = new(parameters, graphicsState.lightAttributes, graphicsState.colorSpace);
        scene.AddLight(new LightSceneEntity(name, dict, loc, RenderFromObject(),
                                            graphicsState.currentOutsideMedium));
    }

    public override void Shape(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        if (!VERIFY_WORLD("Shape", loc))
            return;

        ParameterDictionary dict = new(parameters, graphicsState.shapeAttributes, graphicsState.colorSpace);

        int areaLightIndex = -1;
        if (graphicsState.areaLightName.Length > 0)
        {
            areaLightIndex = scene.AddAreaLight(new SceneEntity(graphicsState.areaLightName,
                                                                graphicsState.areaLightParams,
                                                                graphicsState.areaLightLoc));
            if (activeInstanceDefinition != null)
                Warning(loc, "Area lights not supported with object instancing");
        }

        if (CTMIsAnimated())
        {
            AnimatedTransform renderFromShape = RenderFromObject();
            Transform identity = transformCache.Lookup(new Transform());

            AnimatedShapeSceneEntity entity = new(
                name, dict, loc, renderFromShape, identity, graphicsState.reverseOrientation,
                graphicsState.currentMaterialIndex, graphicsState.currentMaterialName, areaLightIndex,
                graphicsState.currentInsideMedium, graphicsState.currentOutsideMedium);

            if (activeInstanceDefinition != null)
                activeInstanceDefinition.entity.animatedShapes.Add(entity);
            else
                scene.AddAnimatedShape(entity);
        }
        else
        {
            Transform renderFromObject = transformCache.Lookup(RenderFromObject(0));
            Transform objectFromRender = transformCache.Lookup(Inverse(renderFromObject));

            ShapeSceneEntity entity = new(
                name, dict, loc, renderFromObject, objectFromRender, graphicsState.reverseOrientation,
                graphicsState.currentMaterialIndex, graphicsState.currentMaterialName, areaLightIndex,
                graphicsState.currentInsideMedium, graphicsState.currentOutsideMedium);
            if (activeInstanceDefinition != null)
                activeInstanceDefinition.entity.shapes.Add(entity);
            else
                shapes.Add(entity);
        }
    }

    public override void ObjectBegin(string origName, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);

        if (!VERIFY_WORLD("ObjectBegin", loc))
            return;
        pushedGraphicsStates.Add(graphicsState.Clone());

        pushStack.Add(('o', loc));

        if (activeInstanceDefinition != null)
        {
            ErrorExitDeferred(loc, "ObjectBegin called inside of instance definition");
            return;
        }

        if (instanceNames.Contains(name))
        {
            ErrorExitDeferred(loc, $"{name}: trying to redefine an object instance");
            return;
        }
        instanceNames.Add(name);

        activeInstanceDefinition = new ActiveInstanceDefinition(name, loc);
    }

    public override void ObjectEnd(FileLoc loc)
    {
        if (!VERIFY_WORLD("ObjectEnd", loc))
            return;
        if (activeInstanceDefinition == null)
        {
            ErrorExitDeferred(loc, "ObjectEnd called outside of instance definition");
            return;
        }
        if (activeInstanceDefinition.parent != null)
        {
            ErrorExitDeferred(loc, "ObjectEnd called inside Import for instance definition");
            return;
        }

        // NOTE: Must keep the following consistent with AttributeEnd
        graphicsState = pushedGraphicsStates[^1];
        pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);

        nObjectInstancesCreated.Inc();

        if (pushStack[^1].Item1 == 'a')
            ErrorExitDeferred(loc, $"Mismatched nesting: open AttributeBegin from {pushStack[^1].Item2} at ObjectEnd");
        else
            CHECK_EQ(pushStack[^1].Item1, 'o');
        pushStack.RemoveAt(pushStack.Count - 1);

        // Otherwise it will be taken care of in MergeImported()
        if (Interlocked.Decrement(ref activeInstanceDefinition.activeImports) == 0)
            scene.AddInstanceDefinition(activeInstanceDefinition.entity);

        activeInstanceDefinition = null;
    }

    public override void ObjectInstance(string origName, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        if (!VERIFY_WORLD("ObjectInstance", loc))
            return;

        if (activeInstanceDefinition != null)
        {
            ErrorExitDeferred(loc, "ObjectInstance can't be called inside instance definition");
            return;
        }

        Transform worldFromRender = Inverse(renderFromWorld);

        if (CTMIsAnimated())
        {
            AnimatedTransform animatedRenderFromInstance = new(
                RenderFromObject(0) * worldFromRender, graphicsState.transformStartTime,
                RenderFromObject(1) * worldFromRender, graphicsState.transformEndTime);

            // For very small changes, animatedRenderFromInstance may have both
            // xforms equal even if CTMIsAnimated() has returned true. Fall
            // through to create a regular non-animated instance in that case.
            if (animatedRenderFromInstance.IsAnimated())
            {
                instanceUses.Add(new InstanceSceneEntity(name, loc, animatedRenderFromInstance));
                return;
            }
        }

        Transform renderFromInstance = transformCache.Lookup(RenderFromObject(0) * worldFromRender);
        instanceUses.Add(new InstanceSceneEntity(name, loc, renderFromInstance));
    }

    public override void EndOfFiles()
    {
        if (currentBlock != BlockState.WorldBlock)
            ErrorExitDeferred("End of files before \"WorldBegin\".");

        // Ensure there are no pushed graphics states
        while (pushedGraphicsStates.Count > 0)
        {
            ErrorExitDeferred("Missing end to AttributeBegin");
            pushedGraphicsStates.RemoveAt(pushedGraphicsStates.Count - 1);
        }

        if (errorExit)
            ErrorExit("Fatal errors during scene construction");

        if (shapes.Count > 0)
            scene.AddShapes(shapes);
        if (instanceUses.Count > 0)
            scene.AddInstanceUses(instanceUses);

        scene.Done();
    }

    public BasicSceneBuilder CopyForImport()
    {
        BasicSceneBuilder importBuilder = new(scene)
        {
            renderFromWorld = renderFromWorld,
            graphicsState = graphicsState.Clone(),
            currentBlock = currentBlock
        };
        if (activeInstanceDefinition != null)
        {
            importBuilder.activeInstanceDefinition = new ActiveInstanceDefinition(
                activeInstanceDefinition.entity.name, activeInstanceDefinition.entity.loc);

            // In case of nested imports, go up to the true root parent since
            // that's where we need to merge our shapes and that's where the
            // refcount is.
            ActiveInstanceDefinition parent = activeInstanceDefinition;
            while (parent.parent != null)
                parent = parent.parent;
            importBuilder.activeInstanceDefinition.parent = parent;
            Interlocked.Increment(ref parent.activeImports);
        }
        return importBuilder;
    }

    public void MergeImported(BasicSceneBuilder imported)
    {
        while (imported.pushedGraphicsStates.Count > 0)
        {
            ErrorExitDeferred("Missing end to AttributeBegin");
            imported.pushedGraphicsStates.RemoveAt(imported.pushedGraphicsStates.Count - 1);
        }

        errorExit |= imported.errorExit;

        if (imported.shapes.Count > 0)
            scene.AddShapes(imported.shapes);
        if (imported.instanceUses.Count > 0)
            scene.AddInstanceUses(imported.instanceUses);

        static void mergeVector<T>(List<T> @base, List<T> imported)
        {
            @base.AddRange(imported);
            imported.Clear();
        }
        if (imported.activeInstanceDefinition != null)
        {
            ActiveInstanceDefinition current = imported.activeInstanceDefinition;
            ActiveInstanceDefinition? parent = current.parent;
            CHECK(parent != null);

            lock (parent!.mutex)
            {
                mergeVector(parent.entity.shapes, current.entity.shapes);
                mergeVector(parent.entity.animatedShapes, current.entity.animatedShapes);

                if (Interlocked.Decrement(ref parent.activeImports) == 0)
                    scene.AddInstanceDefinition(parent.entity);
            }
        }

        void mergeSet(HashSet<string> @base, HashSet<string> imported, string name)
        {
            foreach (string item in imported)
            {
                if (@base.Contains(item))
                    ErrorExitDeferred($"{item}: multiply defined {name}.");
                @base.Add(item);
            }
            imported.Clear();
        }
        mergeSet(namedMaterialNames, imported.namedMaterialNames, "named material");
        mergeSet(floatTextureNames, imported.floatTextureNames, "texture");
        mergeSet(spectrumTextureNames, imported.spectrumTextureNames, "texture");
    }

    public override void Option(string name, string value, FileLoc loc)
    {
        string nName = normalizeArg(name);

        bool? parseBool()
        {
            if (value == "true")
                return true;
            else if (value == "false")
                return false;
            ErrorExitDeferred(loc, $"{value}: expected \"true\" or \"false\" for option value");
            return null;
        }
        string quoted()
        {
            if (value.Length < 3 || value[0] != '"' || value[^1] != '"')
                ErrorExitDeferred(loc, $"{value}: expected quoted string for option value");
            // (pbrt's substr() of a short value is empty or throws.)
            return value.Length >= 2 ? value[1..^1] : "";
        }

        if (nName == "disablepixeljitter")
            Options.disablePixelJitter = parseBool() ?? Options.disablePixelJitter;
        else if (nName == "disabletexturefiltering")
            Options.disableTextureFiltering = parseBool() ?? Options.disableTextureFiltering;
        else if (nName == "disablewavelengthjitter")
            Options.disableWavelengthJitter = parseBool() ?? Options.disableWavelengthJitter;
        else if (nName == "displacementedgescale")
        {
            if (!Atof(value, out Options.displacementEdgeScale))
                ErrorExitDeferred(loc, $"{value}: expected floating-point option value");
        }
        else if (nName == "msereferenceimage")
            Options.mseReferenceImage = quoted();
        else if (nName == "msereferenceout")
            Options.mseReferenceOutput = quoted();
        else if (nName == "rendercoordsys")
        {
            string renderCoordSys = quoted();
            if (renderCoordSys == "camera")
                Options.renderingSpace = RenderingCoordinateSystem.Camera;
            else if (renderCoordSys == "cameraworld")
                Options.renderingSpace = RenderingCoordinateSystem.CameraWorld;
            else if (renderCoordSys == "world")
                Options.renderingSpace = RenderingCoordinateSystem.World;
            else
                ErrorExit($"{renderCoordSys}: unknown rendering coordinate system.");
        }
        else if (nName == "seed")
            // std::atoi()
            Options.seed = Atoi(value, out int seed) ? seed : 0;
        else if (nName == "forcediffuse")
            Options.forceDiffuse = parseBool() ?? Options.forceDiffuse;
        else if (nName == "pixelstats")
            Options.recordPixelStatistics = parseBool() ?? Options.recordPixelStatistics;
        else if (nName == "wavefront")
            Options.wavefront = parseBool() ?? Options.wavefront;
        else
            ErrorExitDeferred(loc, $"{name}: unknown option");
    }

    public override void Transform(Float[] tr, FileLoc loc) =>
        graphicsState.ForActiveTransforms(t => Transpose(new Transform(new SquareMatrix4(tr))));

    public override void ConcatTransform(Float[] tr, FileLoc loc) =>
        graphicsState.ForActiveTransforms(t => t * Transpose(new Transform(new SquareMatrix4(tr))));

    public override void Rotate(Float angle, Float dx, Float dy, Float dz, FileLoc loc) =>
        graphicsState.ForActiveTransforms(t => t * Util.Rotate(angle, new Vector3f(dx, dy, dz)));

    public override void Scale(Float sx, Float sy, Float sz, FileLoc loc) =>
        graphicsState.ForActiveTransforms(t => t * Util.Scale(sx, sy, sz));

    public override void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz, Float ux, Float uy,
                                Float uz, FileLoc loc)
    {
        Transform lookAt = Util.LookAt(new Point3f(ex, ey, ez), new Point3f(lx, ly, lz), new Vector3f(ux, uy, uz));
        graphicsState.ForActiveTransforms(t => t * lookAt);
    }

    public override void ActiveTransformAll(FileLoc loc) => graphicsState.activeTransformBits = AllTransformsBits;

    public override void ActiveTransformEndTime(FileLoc loc) =>
        graphicsState.activeTransformBits = EndTransformBits;

    public override void ActiveTransformStartTime(FileLoc loc) =>
        graphicsState.activeTransformBits = StartTransformBits;

    public override void TransformTimes(Float start, Float end, FileLoc loc)
    {
        if (!VERIFY_OPTIONS("TransformTimes", loc))
            return;
        graphicsState.transformStartTime = start;
        graphicsState.transformEndTime = end;
    }

    public override void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);
        if (!VERIFY_OPTIONS("PixelFilter", loc))
            return;
        filter = new SceneEntity(name, dict, loc);
    }

    public override void Film(string type, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);
        if (!VERIFY_OPTIONS("Film", loc))
            return;
        film = new SceneEntity(type, dict, loc);
    }

    public override void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);
        if (!VERIFY_OPTIONS("Accelerator", loc))
            return;
        accelerator = new SceneEntity(name, dict, loc);
    }

    public override void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, graphicsState.colorSpace);

        if (!VERIFY_OPTIONS("Integrator", loc))
            return;
        integrator = new SceneEntity(name, dict, loc);
    }

    public override void MediumInterface(string origInsideName, string origOutsideName, FileLoc loc)
    {
        string insideName = NormalizeUTF8(origInsideName);
        string outsideName = NormalizeUTF8(origOutsideName);

        graphicsState.currentInsideMedium = insideName;
        graphicsState.currentOutsideMedium = outsideName;
    }

    public override void Texture(string origName, string type, string texname, List<ParsedParameter> parameters,
                                 FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        if (!VERIFY_WORLD("Texture", loc))
            return;

        ParameterDictionary dict = new(parameters, graphicsState.textureAttributes, graphicsState.colorSpace);

        if (type != "float" && type != "spectrum")
        {
            ErrorExitDeferred(loc, $"{type}: texture type unknown. Must be \"float\" or \"spectrum\".");
            return;
        }

        HashSet<string> names = type == "float" ? floatTextureNames : spectrumTextureNames;
        if (names.Contains(name))
        {
            ErrorExitDeferred(loc, $"Redefining texture \"{name}\".");
            return;
        }
        names.Add(name);

        if (type == "float")
            scene.AddFloatTexture(name, new TextureSceneEntity(texname, dict, loc, RenderFromObject()));
        else
            scene.AddSpectrumTexture(name, new TextureSceneEntity(texname, dict, loc, RenderFromObject()));
    }

    public override void Material(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        if (!VERIFY_WORLD("Material", loc))
            return;

        ParameterDictionary dict = new(parameters, graphicsState.materialAttributes, graphicsState.colorSpace);

        graphicsState.currentMaterialIndex = scene.AddMaterial(new SceneEntity(name, dict, loc));
        graphicsState.currentMaterialName = "";
    }

    public override void MakeNamedMaterial(string origName, List<ParsedParameter> parameters, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        if (!VERIFY_WORLD("MakeNamedMaterial", loc))
            return;

        ParameterDictionary dict = new(parameters, graphicsState.materialAttributes, graphicsState.colorSpace);

        if (namedMaterialNames.Contains(name))
        {
            ErrorExitDeferred(loc, $"{name}: named material redefined.");
            return;
        }
        namedMaterialNames.Add(name);

        scene.AddNamedMaterial(name, new SceneEntity("", dict, loc));
    }

    public override void NamedMaterial(string origName, FileLoc loc)
    {
        string name = NormalizeUTF8(origName);
        if (!VERIFY_WORLD("NamedMaterial", loc))
            return;
        graphicsState.currentMaterialName = name;
        graphicsState.currentMaterialIndex = -1;
    }

    public override void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        if (!VERIFY_WORLD("AreaLightSource", loc))
            return;
        graphicsState.areaLightName = name;
        graphicsState.areaLightParams = new ParameterDictionary(parameters, graphicsState.lightAttributes,
                                                                graphicsState.colorSpace);
        graphicsState.areaLightLoc = loc;
    }
}

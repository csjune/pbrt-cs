// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cpu/render.h and render.cpp

namespace Pbrt;

public static partial class Util
{
    public static void RenderCPU(BasicScene parsedScene)
    {
        // Create media first (so have them for the camera...)
        Dictionary<string, Medium> media = parsedScene.CreateMedia();

        // Textures
        LOG_VERBOSE($"Starting textures");
        NamedTextures textures = parsedScene.CreateTextures();
        LOG_VERBOSE($"Finished textures");

        // Lights
        Dictionary<int, List<Light>> shapeIndexToAreaLights = [];
        List<Light> lights = parsedScene.CreateLights(textures, shapeIndexToAreaLights);

        LOG_VERBOSE($"Starting materials");
        Dictionary<string, Material?> namedMaterials = [];
        List<Material?> materials = [];
        parsedScene.CreateMaterials(textures, namedMaterials, materials);
        LOG_VERBOSE($"Finished materials");

        // (pbrt checks these after creating the aggregate, which empties the
        // scene's shape lists.)
        bool haveScatteringMedia = false;
        foreach (ShapeSceneEntity sh in parsedScene.shapes)
            if (sh.insideMedium.Length > 0 || sh.outsideMedium.Length > 0)
                haveScatteringMedia = true;
        foreach (AnimatedShapeSceneEntity sh in parsedScene.animatedShapes)
            if (sh.insideMedium.Length > 0 || sh.outsideMedium.Length > 0)
                haveScatteringMedia = true;

        Primitive? accel = parsedScene.CreateAggregate(textures, shapeIndexToAreaLights, media, namedMaterials,
                                                       materials);

        Camera camera = parsedScene.GetCamera();
        Film film = camera.GetFilm();
        Sampler sampler = parsedScene.GetSampler();

        // Integrator
        LOG_VERBOSE($"Starting to create integrator");
        Integrator integrator = parsedScene.CreateIntegrator(camera, sampler, accel, lights);
        LOG_VERBOSE($"Finished creating integrator");

        // Helpful warnings
        string integratorName = parsedScene.integrator.name;
        if (haveScatteringMedia && integratorName != "volpath" && integratorName != "simplevolpath" &&
            integratorName != "bdpt" && integratorName != "mlt")
            Warning($"Scene has scattering media but \"{integratorName}\" integrator doesn't support " +
                    "volume scattering. Consider using \"volpath\", \"simplevolpath\", \"bdpt\", or \"mlt\".");

        bool haveLights = lights.Count > 0;
        foreach (Medium m in media.Values)
            haveLights |= m.IsEmissive();

        if (!haveLights && integratorName != "ambientocclusion" && integratorName != "aov")
            Warning("No light sources defined in scene; rendering a black image.");

        if (film is GBufferFilm && !(integratorName == "path" || integratorName == "volpath"))
            Warning($"GBufferFilm is not supported by the \"{integratorName}\" integrator. The channels " +
                    "other than R, G, B will be zero.");

        bool haveSubsurface = false;
        foreach (Material? mtl in materials)
            haveSubsurface |= mtl != null && mtl.HasSubsurfaceScattering();
        foreach (Material? mtl in namedMaterials.Values)
            haveSubsurface |= mtl != null && mtl.HasSubsurfaceScattering();

        if (haveSubsurface && integratorName != "volpath")
            Warning("Some objects in the scene have subsurface scattering, which is not supported by the " +
                    $"{integratorName} integrator. Use the \"volpath\" integrator to render them correctly.");

        if (Options.pixelMaterial is Point2i pixelMaterial)
        {
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);

            CameraSample cs = new()
            {
                pFilm = new Point2f(pixelMaterial) + new Vector2f(0.5f, 0.5f),
                time = 0.5f,
                pLens = new Point2f(0.5f, 0.5f),
                filterWeight = 1
            };
            CameraRay? cr = camera.GenerateRay(cs, ref lambda);
            if (cr is not CameraRay cameraRay)
            {
                ErrorExit("Unable to generate camera ray for specified pixel.");
                return;
            }

            int depth = 1;
            Ray ray = cameraRay.ray;
            while (true)
            {
                ShapeIntersection? isect = accel?.Intersect(ray, Infinity);
                if (isect is not ShapeIntersection si)
                {
                    if (depth == 1)
                        ErrorExit("No geometry visible at specified pixel.");
                    else
                        break;
                    return;
                }

                SurfaceInteraction intr = si.intr;
                if (intr.material == null)
                    Warning("Ignoring \"interface\" material at intersection.");
                else
                {
                    Transform worldFromRender = camera.GetCameraTransform().WorldFromRender();
                    Console.Write($"Intersection depth {depth}\n");
                    Console.Write($"World-space p: {worldFromRender.Apply(intr.p())}\n");
                    Console.Write($"World-space n: {worldFromRender.Apply(intr.n)}\n");
                    Console.Write($"World-space ns: {worldFromRender.Apply(intr.shading.n)}\n");
                    Console.Write($"Distance from camera: {FloatToString(Distance(intr.p(), cameraRay.ray.o))}\n");

                    bool isNamed = false;
                    foreach ((string name, Material? mtl) in namedMaterials)
                        if (ReferenceEquals(mtl, intr.material))
                        {
                            Console.Write($"Named material: {name}\n\n");
                            isNamed = true;
                            break;
                        }
                    if (!isNamed)
                        // If we didn't find a named material, dump out the whole thing.
                        Console.Write($"{intr.material}\n\n");

                    ++depth;
                    ray = intr.SpawnRay(ray.d);
                }
            }

            return;
        }

        // Render!
        integrator.Render();

        LOG_VERBOSE($"Memory used after rendering: {GetCurrentRSS()}");

        PtexTextureBase.ReportStats();
        ImageTextureBase.ClearCache();
    }
}

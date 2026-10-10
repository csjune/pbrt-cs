// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/wavefront/integrator.h, integrator.cpp, camera.cpp,
// film.cpp, samples.cpp and wavefront.cpp
//
// Only the CPU path is ported (--wavefront, and --interactive with it): the
// GPU kernels and the OptiX aggregate aren't. pbrt's ParallelFor() and Do()
// methods, which launch GPU kernels when --gpu is given, are the CPU
// ParallelFor() and plain code here. pbrt instantiates the camera-ray and
// ray-sample kernels for each concrete Sampler, copying the sampler for
// each work item; here each thread has a clone of the sampler (as the CPU
// integrators do), since StartPixelSample() sets all of its state. The
// material evaluation kernels are generic over the texture evaluator but
// not over the concrete material.

namespace Pbrt;

// WavefrontPathIntegrator Definition
public sealed partial class WavefrontPathIntegrator
{
    // WavefrontPathIntegrator Member Variables
    private readonly bool initializeVisibleSurface;
    private readonly bool haveSubsurface;
    private readonly bool haveMedia;
    private readonly bool haveBasicEvalMaterial, haveUniversalEvalMaterial;

    public sealed class Stats(int maxDepth)
    {
        // Note: not atomics: tid 0 always updates them for everyone...
        public long cameraRays = 0;
        public readonly long[] indirectRays = new long[maxDepth + 1], shadowRays = new long[maxDepth];

        public string Print()
        {
            string s = "";
            s += $"    {"Camera rays",-42}               {cameraRays,12}\n";
            for (int i = 1; i < indirectRays.Length; ++i)
                s += $"    {$"Indirect rays, depth {i,-3}",-42}               {indirectRays[i],12}\n";
            for (int i = 0; i < shadowRays.Length; ++i)
                s += $"    {$"Shadow rays, depth {i,-3}",-42}               {shadowRays[i],12}\n";
            return s;
        }
    }

    public readonly Stats stats;

    public readonly Filter filter;
    public readonly Film film;
    public readonly Sampler sampler;
    public readonly Camera camera;
    private readonly List<Light> infiniteLights = [];
    private readonly LightSampler lightSampler;

    private readonly int maxDepth, samplesPerPixel;
    private readonly bool regularize;

    private readonly int scanlinesPerPass, maxQueueSize;

    private readonly PixelSampleState pixelSampleState;

    private readonly RayQueue[] rayQueues = new RayQueue[2];

    private readonly WavefrontAggregate aggregate;

    private readonly MediumSampleQueue? mediumSampleQueue;
    private readonly MediumScatterQueue? mediumScatterQueue;

    private readonly EscapedRayQueue? escapedRayQueue;

    private readonly HitAreaLightQueue hitAreaLightQueue;

    private readonly MaterialEvalQueue basicEvalMaterialQueue;
    private readonly MaterialEvalQueue universalEvalMaterialQueue;

    private readonly ShadowRayQueue shadowRayQueue;

    private readonly GetBSSRDFAndProbeRayQueue? bssrdfEvalQueue;
    private readonly SubsurfaceScatterQueue? subsurfaceScatterQueue;

    private readonly ThreadLocal<Sampler> samplers;

    // (pbrt's per-material-type flags are a flag per texture evaluator here.)
    private static void updateMaterialNeeds(Material? m, ref bool haveBasicEvalMaterial,
                                            ref bool haveUniversalEvalMaterial, ref bool haveSubsurface,
                                            ref bool haveMedia)
    {
        haveMedia |= m == null;  // interface material
        if (m == null)
            return;

        if (m is MixMaterial mix)
        {
            // This is a somewhat odd place for this check, but it's convenient...
            if (!m.CanEvaluateTextures(new BasicTextureEvaluator()))
                ErrorExit("\"mix\" material has a texture that can't be evaluated with the " +
                          "BasicTextureEvaluator, which is all that is currently supported " +
                          $"int the wavefront renderer--sorry! {mix}");

            updateMaterialNeeds(mix.GetMaterial(0), ref haveBasicEvalMaterial, ref haveUniversalEvalMaterial,
                                ref haveSubsurface, ref haveMedia);
            updateMaterialNeeds(mix.GetMaterial(1), ref haveBasicEvalMaterial, ref haveUniversalEvalMaterial,
                                ref haveSubsurface, ref haveMedia);
            return;
        }

        haveSubsurface |= m.HasSubsurfaceScattering();

        if (UsesBasicTextureEvaluator(m))
            haveBasicEvalMaterial = true;
        else
            haveUniversalEvalMaterial = true;
    }

    public WavefrontPathIntegrator(BasicScene scene)
    {
        // Allocate all of the data structures that represent the scene...
        Dictionary<string, Medium> media = scene.CreateMedia();

        // "haveMedia" is a bit of a misnomer in that determines both whether
        // queues are allocated for the medium sampling kernels and they are
        // launched as well as whether the ray marching shadow ray kernel is
        // launched... Thus, it will be true if there actually are no media,
        // but some "interface" materials are present in the scene.
        haveMedia = false;
        // Check the shapes and instance definitions...
        foreach (ShapeSceneEntity shape in scene.shapes)
            if (shape.insideMedium.Length > 0 || shape.outsideMedium.Length > 0)
                haveMedia = true;
        foreach (AnimatedShapeSceneEntity shape in scene.animatedShapes)
            if (shape.insideMedium.Length > 0 || shape.outsideMedium.Length > 0)
                haveMedia = true;
        foreach (InstanceDefinitionSceneEntity instanceDefinition in scene.instanceDefinitions.Values)
        {
            foreach (ShapeSceneEntity shape in instanceDefinition.shapes)
                if (shape.insideMedium.Length > 0 || shape.outsideMedium.Length > 0)
                    haveMedia = true;
            foreach (AnimatedShapeSceneEntity shape in instanceDefinition.animatedShapes)
                if (shape.insideMedium.Length > 0 || shape.outsideMedium.Length > 0)
                    haveMedia = true;
        }

        // Textures
        LOG_VERBOSE($"Starting to create textures");
        NamedTextures textures = scene.CreateTextures();
        LOG_VERBOSE($"Done creating textures");

        LOG_VERBOSE($"Starting to create lights");
        Dictionary<int, List<Light>> shapeIndexToAreaLights = [];
        List<Light> allLights = [];
        foreach (Light l in scene.CreateLights(textures, shapeIndexToAreaLights))
        {
            if (l is UniformInfiniteLight or ImageInfiniteLight or PortalImageInfiniteLight)
                infiniteLights.Add(l);

            allLights.Add(l);
        }
        LOG_VERBOSE($"Done creating lights");

        LOG_VERBOSE($"Starting to create materials");
        Dictionary<string, Material?> namedMaterials = [];
        List<Material?> materials = [];
        scene.CreateMaterials(textures, namedMaterials, materials);

        haveBasicEvalMaterial = haveUniversalEvalMaterial = false;
        haveSubsurface = false;
        foreach (Material? m in materials)
            updateMaterialNeeds(m, ref haveBasicEvalMaterial, ref haveUniversalEvalMaterial, ref haveSubsurface,
                                ref haveMedia);
        foreach (Material? m in namedMaterials.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value))
            updateMaterialNeeds(m, ref haveBasicEvalMaterial, ref haveUniversalEvalMaterial, ref haveSubsurface,
                                ref haveMedia);
        LOG_VERBOSE($"Finished creating materials");

        camera = scene.GetCamera();
        film = camera.GetFilm();
        filter = film.GetFilter();
        sampler = scene.GetSampler();

        aggregate = new CPUAggregate(scene, textures, shapeIndexToAreaLights, media, namedMaterials, materials);

        // Preprocess the light sources
        foreach (Light light in allLights)
            light.Preprocess(aggregate.Bounds());

        bool haveLights = allLights.Count > 0;
        foreach (Medium m in media.Values)
            haveLights |= m.IsEmissive();
        if (!haveLights)
            ErrorExit("No light sources specified");

        LOG_VERBOSE($"Starting to create light sampler");
        string lightSamplerName = scene.integrator.parameters.GetOneString("lightsampler", "bvh");
        if (allLights.Count == 1)
            lightSamplerName = "uniform";
        lightSampler = LightSampler.Create(lightSamplerName, allLights.ToArray());
        LOG_VERBOSE($"Finished creating light sampler");

        if (scene.integrator.name != "path" && scene.integrator.name != "volpath")
            Warning(scene.integrator.loc,
                    $"Ignoring specified integrator \"{scene.integrator.name}\": the wavefront integrator " +
                    "always uses a \"volpath\" integrator.");

        // Integrator parameters
        regularize = scene.integrator.parameters.GetOneBool("regularize", false);
        maxDepth = scene.integrator.parameters.GetOneInt("maxdepth", 5);

        initializeVisibleSurface = film.UsesVisibleSurface();
        samplesPerPixel = sampler.SamplesPerPixel();

        // Warn about unsupported stuff...
        if (Options.forceDiffuse)
            ErrorExit("The wavefront integrator does not support --force-diffuse.");
        if (Options.writePartialImages)
            Warning("The wavefront integrator does not support --write-partial-images.");
        if (Options.recordPixelStatistics)
            ErrorExit("The wavefront integrator does not support --pixelstats.");
        if (Options.mseReferenceImage.Length > 0)
            ErrorExit("The wavefront integrator does not support --mse-reference-image.");
        if (Options.mseReferenceOutput.Length > 0)
            ErrorExit("The wavefront integrator does not support --mse-reference-out.");

        ///////////////////////////////////////////////////////////////////////////
        // Allocate storage for all of the queues/buffers...

        // Compute number of scanlines to render per pass
        Vector2i resolution = film.PixelBounds().Diagonal();
        // TODO: make this configurable. Base it on the amount of GPU memory?
        int maxSamples = 1024 * 1024;
        scanlinesPerPass = Math.Max(1, maxSamples / resolution.x);
        int nPasses = (resolution.y + scanlinesPerPass - 1) / scanlinesPerPass;
        scanlinesPerPass = (resolution.y + nPasses - 1) / nPasses;
        maxQueueSize = resolution.x * scanlinesPerPass;
        LOG_VERBOSE($"Will render in {nPasses} passes {scanlinesPerPass} scanlines per pass\n");

        pixelSampleState = new PixelSampleState(maxQueueSize);

        rayQueues[0] = new RayQueue(maxQueueSize);
        rayQueues[1] = new RayQueue(maxQueueSize);

        shadowRayQueue = new ShadowRayQueue(maxQueueSize);

        if (haveSubsurface)
        {
            bssrdfEvalQueue = new GetBSSRDFAndProbeRayQueue(maxQueueSize);
            subsurfaceScatterQueue = new SubsurfaceScatterQueue(maxQueueSize);
        }

        if (infiniteLights.Count > 0)
            escapedRayQueue = new EscapedRayQueue(maxQueueSize);
        hitAreaLightQueue = new HitAreaLightQueue(maxQueueSize);

        basicEvalMaterialQueue = new MaterialEvalQueue(haveBasicEvalMaterial ? maxQueueSize : 1);
        universalEvalMaterialQueue = new MaterialEvalQueue(haveUniversalEvalMaterial ? maxQueueSize : 1);

        if (haveMedia)
        {
            mediumSampleQueue = new MediumSampleQueue(maxQueueSize);
            mediumScatterQueue = new MediumScatterQueue(maxQueueSize);
        }

        stats = new Stats(maxDepth);

        samplers = new ThreadLocal<Sampler>(() => sampler.Clone());
    }

    private RayQueue CurrentRayQueue(int wavefrontDepth) => rayQueues[wavefrontDepth & 1];

    private RayQueue NextRayQueue(int wavefrontDepth) => rayQueues[(wavefrontDepth + 1) & 1];

    // WavefrontPathIntegrator Method Definitions
    public Float Render()
    {
        Bounds2i pixelBounds = film.PixelBounds();
        Vector2i resolution = pixelBounds.Diagonal();

        GUI? gui = null;
        // FIXME: camera animation; whatever...
        Transform renderFromCamera = camera.GetCameraTransform().RenderFromCamera().startTransform;
        Transform cameraFromRender = Inverse(renderFromCamera);
        Transform cameraFromWorld = camera.GetCameraTransform().CameraFromWorld(camera.SampleTime(0f));
        if (Options.interactive)
        {
            if (Options.displayServer.Length > 0)
                ErrorExit("--interactive and --display-server cannot be used at the same time.");
            gui = new GUI(film.GetFilename(), resolution, aggregate.Bounds());
        }

        Timer timer = new();

        // Launch thread to copy image for display server, if enabled
        if (Options.displayServer.Length > 0)
            StartDisplayThread();

        // Loop over sample indices and evaluate pixel samples
        int firstSampleIndex = 0, lastSampleIndex = samplesPerPixel;
        // Update sample index range based on debug start, if provided
        if (Options.debugStart.Length > 0)
        {
            List<int> values = SplitStringToInts(Options.debugStart, ',');
            if (values.Count != 1 && values.Count != 2)
                ErrorExit("Expected either one or two integer values for --debugstart.");

            firstSampleIndex = values[0];
            if (values.Count == 2)
                lastSampleIndex = firstSampleIndex + values[1];
            else
                lastSampleIndex = firstSampleIndex + 1;
        }

        ProgressReporter progress = new(lastSampleIndex - firstSampleIndex, "Rendering",
                                        Options.quiet || Options.interactive);
        for (int sampleIndex = firstSampleIndex; sampleIndex < lastSampleIndex || gui != null; ++sampleIndex)
        {
            int si = sampleIndex;
            using CheckCallbackScope _ = new(() =>
                $"Wavefront rendering failed at sample {si}. Debug with \"--debugstart {si}\"\n");

            // Keep running the outer for loop but don't take more samples if
            // the GUI is being used so that the user can move the camera, etc.
            if (sampleIndex < lastSampleIndex)
            {
            // Render image for sample _sampleIndex_
            LOG_VERBOSE($"Starting to submit work for sample {sampleIndex}");
            for (int y0 = pixelBounds.pMin.y; y0 < pixelBounds.pMax.y; y0 += scanlinesPerPass)
            {
                // Generate camera rays for current scanline range
                RayQueue cameraRayQueue = CurrentRayQueue(0);
                cameraRayQueue.Reset();

                Transform cameraMotion = new();
                if (gui != null)
                    cameraMotion = renderFromCamera * gui.GetCameraTransform() * cameraFromRender;
                GenerateCameraRays(y0, cameraMotion, sampleIndex);
                stats.cameraRays += cameraRayQueue.Size();

                // Trace rays and estimate radiance up to maximum ray depth
                for (int wavefrontDepth = 0; true; ++wavefrontDepth)
                {
                    // Reset queues before tracing rays
                    RayQueue nextQueue = NextRayQueue(wavefrontDepth);
                    nextQueue.Reset();
                    // Reset queues before tracing next batch of rays
                    mediumSampleQueue?.Reset();
                    mediumScatterQueue?.Reset();

                    escapedRayQueue?.Reset();
                    hitAreaLightQueue.Reset();

                    basicEvalMaterialQueue.Reset();
                    universalEvalMaterialQueue.Reset();

                    bssrdfEvalQueue?.Reset();
                    subsurfaceScatterQueue?.Reset();

                    // Follow active ray paths and accumulate radiance estimates
                    GenerateRaySamples(wavefrontDepth, sampleIndex);

                    // Find closest intersections along active rays
                    aggregate.IntersectClosest(CurrentRayQueue(wavefrontDepth), escapedRayQueue, hitAreaLightQueue,
                                               basicEvalMaterialQueue, universalEvalMaterialQueue,
                                               mediumSampleQueue, NextRayQueue(wavefrontDepth));

                    if (wavefrontDepth > 0)
                    {
                        // As above, with the indexing...
                        RayQueue statsQueue = CurrentRayQueue(wavefrontDepth);
                        stats.indirectRays[wavefrontDepth] += statsQueue.Size();
                    }

                    SampleMediumInteraction(wavefrontDepth);

                    HandleEscapedRays();

                    HandleEmissiveIntersection();

                    if (wavefrontDepth == maxDepth)
                        break;

                    EvaluateMaterialsAndBSDFs(wavefrontDepth, cameraMotion);

                    // Do immediately so that we have space for shadow rays for subsurface..
                    TraceShadowRays(wavefrontDepth);

                    SampleSubsurface(wavefrontDepth);
                }

                UpdateFilm();
            }

            progress.Update();
            }

            if (gui != null)
            {
                RGB[] rgb = gui.MapFramebuffer();
                UpdateFramebufferFromFilm(pixelBounds, gui.exposure, rgb);
                gui.UnmapFramebuffer();

                if (gui.printCameraTransform)
                {
                    SquareMatrix4 cfw = (Inverse(gui.GetCameraTransform()) * cameraFromWorld).GetMatrix();
                    Console.Write("Current camera transform:\nTransform [ ");
                    for (int i = 0; i < 16; ++i)
                        Console.Write($"{FloatToString(cfw[i % 4, i / 4])} ");
                    Console.Write("]\n");
                    Console.Out.Flush();
                    gui.printCameraTransform = false;
                }

                DisplayState state = gui.RefreshDisplay();
                if (state == DisplayState.EXIT)
                    break;
                else if (state == DisplayState.RESET)
                {
                    sampleIndex = firstSampleIndex - 1;
                    ParallelFor(0, resolution.x * resolution.y, (long i) =>
                    {
                        int x = (int)(i % resolution.x), y = (int)(i / resolution.x);
                        film.ResetPixel(pixelBounds.pMin + new Vector2i(x, y));
                    });
                }
            }
        }

        gui?.Dispose();

        progress.Done();

        Float seconds = (Float)timer.ElapsedSeconds();

        return seconds;
    }

    private void HandleEscapedRays()
    {
        if (escapedRayQueue == null)
            return;
        ForAllQueued(escapedRayQueue, (in EscapedRayWorkItem w) =>
        {
            // Compute weighted radiance for escaped ray
            SampledSpectrum L = new(0f);
            foreach (Light light in infiniteLights)
            {
                SampledSpectrum Le = light.Le(new Ray(w.rayo, w.rayd), w.lambda);
                if (Le)
                {
                    // Compute path radiance contribution from infinite light
                    if (w.depth == 0 || w.specularBounce)
                        L += w.beta * Le / w.r_u.Average();
                    else
                    {
                        // Compute MIS-weighted radiance contribution from infinite light
                        LightSampleContext ctx = w.prevIntrCtx;
                        Float lightChoicePDF = lightSampler.PMF(ctx, light);
                        SampledSpectrum r_l = w.r_l * lightChoicePDF * light.PDF_Li(ctx, w.rayd, true);
                        L += w.beta * Le / (w.r_u + r_l).Average();
                    }
                }
            }

            // Update pixel radiance if ray's radiance is nonzero
            if (L)
            {
                L += pixelSampleState.L[w.pixelIndex];
                pixelSampleState.L[w.pixelIndex] = L;
            }
        });
    }

    private void HandleEmissiveIntersection()
    {
        ForAllQueued(hitAreaLightQueue, (in HitAreaLightWorkItem w) =>
        {
            // Find emitted radiance from surface that ray hit
            SampledSpectrum Le = w.areaLight.L(w.p, w.n, w.uv, w.wo, w.lambda);
            if (!Le)
                return;

            // Compute area light's weighted radiance contribution to the path
            SampledSpectrum L;
            if (w.depth == 0 || w.specularBounce)
                L = w.beta * Le / w.r_u.Average();
            else
            {
                // Compute MIS-weighted radiance contribution from area light
                Vector3f wi = -w.wo;
                LightSampleContext ctx = w.prevIntrCtx;
                Float lightChoicePDF = lightSampler.PMF(ctx, w.areaLight);
                Float lightPDF = lightChoicePDF * w.areaLight.PDF_Li(ctx, wi, true);

                SampledSpectrum r_u = w.r_u;
                SampledSpectrum r_l = w.r_l * lightPDF;
                L = w.beta * Le / (r_u + r_l).Average();
            }

            // Update _L_ in _PixelSampleState_ for area light's radiance
            L += pixelSampleState.L[w.pixelIndex];
            pixelSampleState.L[w.pixelIndex] = L;
        });
    }

    private void TraceShadowRays(int wavefrontDepth)
    {
        if (haveMedia)
            aggregate.IntersectShadowTr(shadowRayQueue, pixelSampleState);
        else
            aggregate.IntersectShadow(shadowRayQueue, pixelSampleState);
        // Reset shadow ray queue
        stats.shadowRays[wavefrontDepth] += shadowRayQueue.Size();
        shadowRayQueue.Reset();
    }

    // --interactive support
    private void UpdateFramebufferFromFilm(Bounds2i pixelBounds, Float exposure, RGB[] rgb)
    {
        Vector2i resolution = pixelBounds.Diagonal();
        ParallelFor(0, resolution.x * resolution.y, (long i) =>
        {
            int index = (int)i;
            Point2i p = new(index % resolution.x, index / resolution.x);
            rgb[index] = exposure * film.GetPixelRGB(p + film.PixelBounds().pMin);
        });
    }

    private void StartDisplayThread()
    {
        Bounds2i pixelBounds = film.PixelBounds();
        DisplayDynamic(film.GetFilename(), new Point2i(pixelBounds.Diagonal()), ["R", "G", "B"],
                       (Bounds2i b, float[][] displayValue) =>
                       {
                           int index = 0;
                           foreach (Point2i p in b)
                           {
                               RGB rgb = film.GetPixelRGB(pixelBounds.pMin + p, 1f /* splat scale */);
                               for (int c = 0; c < 3; ++c)
                                   displayValue[c][index] = (float)rgb[c];
                               ++index;
                           }
                       });
    }

    // WavefrontPathIntegrator Camera Ray Methods
    private void GenerateCameraRays(int y0, Transform movingFromCamera, int sampleIndex)
    {
        RayQueue rayQueue = CurrentRayQueue(0);
        ParallelFor(0, maxQueueSize, (long pi) =>
        {
            int pixelIndex = (int)pi;
            // Enqueue camera ray and set pixel state for sample
            // Compute pixel coordinates for _pixelIndex_
            Bounds2i pixelBounds = film.PixelBounds();
            int xResolution = pixelBounds.pMax.x - pixelBounds.pMin.x;
            Point2i pPixel = new(pixelBounds.pMin.x + pixelIndex % xResolution, y0 + pixelIndex / xResolution);
            pixelSampleState.pPixel[pixelIndex] = pPixel;

            // Test pixel coordinates against pixel bounds
            if (!InsideExclusive(pPixel, pixelBounds))
                return;

            // Initialize _Sampler_ for current pixel and sample
            Sampler pixelSampler = samplers.Value!;
            pixelSampler.StartPixelSample(pPixel, sampleIndex, 0);

            // Sample wavelengths for ray path
            Float lu = pixelSampler.Get1D();
            if (Options.disableWavelengthJitter)
                lu = 0.5f;
            SampledWavelengths lambda = film.SampleWavelengths(lu);

            // Generate _CameraSample_ and corresponding ray
            CameraSample cameraSample = GetCameraSample(pixelSampler, pPixel, filter);
            CameraRay? cameraRay = camera.GenerateRay(cameraSample, ref lambda);

            // Initialize remainder of _PixelSampleState_ for ray
            pixelSampleState.L[pixelIndex] = new SampledSpectrum(0f);
            pixelSampleState.lambda[pixelIndex] = lambda;
            pixelSampleState.filterWeight[pixelIndex] = cameraSample.filterWeight;
            if (initializeVisibleSurface)
                pixelSampleState.visibleSurface[pixelIndex] = new VisibleSurface();

            // Enqueue camera ray for intersection tests
            if (cameraRay is CameraRay cr)
            {
                Ray ray = movingFromCamera.Apply(cr.ray);
                rayQueue.PushCameraRay(ray, lambda, pixelIndex);
                pixelSampleState.cameraRayWeight[pixelIndex] = cr.weight;
            }
            else
                pixelSampleState.cameraRayWeight[pixelIndex] = new SampledSpectrum(0);
        });
    }

    // WavefrontPathIntegrator Film Methods
    private void UpdateFilm()
    {
        ParallelFor(0, maxQueueSize, (long pi) =>
        {
            int pixelIndex = (int)pi;
            // Check pixel against film bounds
            Point2i pPixel = pixelSampleState.pPixel[pixelIndex];
            if (!InsideExclusive(pPixel, film.PixelBounds()))
                return;

            // Compute final weighted radiance value
            SampledSpectrum Lw = pixelSampleState.L[pixelIndex] * pixelSampleState.cameraRayWeight[pixelIndex];

            // Provide sample radiance value to film
            SampledWavelengths lambda = pixelSampleState.lambda[pixelIndex];
            Float filterWeight = pixelSampleState.filterWeight[pixelIndex];
            // (Film::AddSample() ignores _visibleSurface_ when the film
            // doesn't use it, as when pbrt passes nullptr.)
            if (initializeVisibleSurface)
                film.AddSample(pPixel, Lw, lambda, pixelSampleState.visibleSurface[pixelIndex], filterWeight);
            else
                film.AddSample(pPixel, Lw, lambda, new VisibleSurface(), filterWeight);
        });
    }

    // WavefrontPathIntegrator Sampler Methods
    private void GenerateRaySamples(int wavefrontDepth, int sampleIndex)
    {
        RayQueue rayQueue = CurrentRayQueue(wavefrontDepth);
        ForAllQueued(rayQueue, (in RayWorkItem w) =>
        {
            // Generate samples for ray segment at current sample index
            // Find first sample dimension
            int dimension = 6 + 7 * w.depth;
            if (haveSubsurface)
                dimension += 3 * w.depth;

            // Initialize _Sampler_ for pixel, sample index, and dimension
            Sampler pixelSampler = samplers.Value!;
            Point2i pPixel = pixelSampleState.pPixel[w.pixelIndex];
            pixelSampler.StartPixelSample(pPixel, sampleIndex, dimension);

            // Initialize _RaySamples_ structure with sample values
            RaySamples rs = new();
            rs.direct.uc = pixelSampler.Get1D();
            rs.direct.u = pixelSampler.Get2D();
            // Initialize remaining samples in _rs_
            rs.indirect.uc = pixelSampler.Get1D();
            rs.indirect.u = pixelSampler.Get2D();
            rs.indirect.rr = pixelSampler.Get1D();
            // Possibly initialize subsurface subsurface samples in _rs_
            rs.haveSubsurface = haveSubsurface;
            if (haveSubsurface)
            {
                rs.subsurface.uc = pixelSampler.Get1D();
                rs.subsurface.u = pixelSampler.Get2D();
            }

            // Store _RaySamples_ in pixel sample state
            pixelSampleState.samples[w.pixelIndex] = rs;
        });
    }

    public override string ToString() =>
        $"[ WavefrontPathIntegrator camera: {camera} film: {film} sampler: {sampler} maxDepth: {maxDepth} " +
        $"regularize: {regularize} aggregate: {aggregate} ]";
}

public static partial class Util
{
    public static void RenderWavefront(BasicScene scene)
    {
        WavefrontPathIntegrator integrator = new(scene);

        ///////////////////////////////////////////////////////////////////////////
        // Render!
        Float seconds = integrator.Render();

        LOG_VERBOSE($"Total rendering time: {seconds:F3} s");

        if (Options.printStatistics)
        {
            Console.Write("Wavefront integrator statistics:\n");
            Console.Write($"{integrator.stats.Print()}\n");
        }

        ImageMetadata metadata = new();
        integrator.camera.InitMetadata(metadata);
        metadata.renderTimeSeconds = (float)seconds;
        metadata.samplesPerPixel = integrator.sampler.SamplesPerPixel();
        integrator.film.WriteImage(metadata);
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cpu/integrators.h and integrators.cpp
//
// pbrt's ScratchBuffer allocations are ordinary allocations here, so the
// integrators don't take one. Li()'s VisibleSurface pointer is a ref
// parameter plus a flag that says whether pbrt's pointer would be non-null.
// The display server isn't ported.
// The SampleT_maj() callbacks are C# lambdas; they capture copies of ref
// parameters (lambda) where pbrt captures them by reference, which is the
// same since the callbacks don't modify them.

namespace Pbrt;

// Integrator Definition
public abstract partial class Integrator
{
    private static readonly StatCounter nIntersectionTests = new("Intersections/Regular ray intersection tests");
    private static readonly StatCounter nShadowTests = new("Intersections/Shadow ray intersection tests");

    // Integrator Public Members
    public Primitive? aggregate;
    public List<Light> lights;
    public List<Light> infiniteLights = [];

    // Integrator Protected Methods
    protected Integrator(Primitive? aggregate, List<Light> lights)
    {
        this.aggregate = aggregate;
        this.lights = lights;
        // Integrator constructor implementation
        Bounds3f sceneBounds = aggregate != null ? aggregate.Bounds() : new Bounds3f();
        LOG_VERBOSE($"Scene bounds {sceneBounds}");
        foreach (Light light in lights)
        {
            light.Preprocess(sceneBounds);
            if (light.Type() == LightType.Infinite)
                infiniteLights.Add(light);
        }
    }

    // Integrator Public Methods
    public abstract override string ToString();

    public abstract void Render();

    public ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        nIntersectionTests.Inc();
        DCHECK(ray.d != new Vector3f(0, 0, 0));
        if (aggregate != null)
            return aggregate.Intersect(ray, tMax);
        else
            return null;
    }

    public bool IntersectP(in Ray ray, Float tMax = Infinity)
    {
        nShadowTests.Inc();
        DCHECK(ray.d != new Vector3f(0, 0, 0));
        if (aggregate != null)
            return aggregate.IntersectP(ray, tMax);
        else
            return false;
    }

    public bool Unoccluded(in Interaction p0, in Interaction p1) => !IntersectP(p0.SpawnRayTo(p1), 1 - ShadowEpsilon);

    public SampledSpectrum Tr(in Interaction p0, in Interaction p1, in SampledWavelengths lambda)
    {
        RNG rng = new(Hash(p0.p()), Hash(p1.p()));

        // (pbrt calls SurfaceInteraction::SpawnRayTo() for a surface
        // interaction, which is the same as Interaction's.)
        Ray ray = p0.SpawnRayTo(p1);
        SampledSpectrum Tr = new(1f), inv_w = new(1f);
        if (LengthSquared(ray.d) == 0)
            return Tr;

        while (true)
        {
            ShapeIntersection? si = Intersect(ray, 1 - ShadowEpsilon);
            // Handle opaque surface along ray's path
            if (si.HasValue && si.Value.intr.material != null)
                return new SampledSpectrum(0.0f);

            // Update transmittance for current ray segment
            if (ray.medium != null)
            {
                Point3f pExit = ray.At(si.HasValue ? si.Value.tHit : (1 - ShadowEpsilon));
                ray.d = pExit - ray.o;

                SampledSpectrum T_maj = SampleT_maj(ray, 1f, rng.Uniform<Float>(), ref rng, lambda,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);

                        // ratio-tracking: only evaluate null scattering
                        Float pr = T_maj[0] * sigma_maj[0];
                        Tr *= T_maj * sigma_n / pr;
                        inv_w *= T_maj * sigma_maj / pr;

                        if (!Tr || !inv_w)
                            return false;

                        return true;
                    });
                Tr *= T_maj / T_maj[0];
                inv_w *= T_maj / T_maj[0];
            }

            // Generate next ray segment or return final transmittance
            if (si is not ShapeIntersection s)
                break;
            ray = s.intr.SpawnRayTo(p1);
        }
        return Tr / inv_w.Average();
    }

    protected string BaseToString()
    {
        string s = $"[ Integrator aggregate: {aggregate} lights[{lights.Count}]: [ ";
        foreach (Light l in lights)
            s += $"{l}, ";
        s += $"] infiniteLights[{infiniteLights.Count}]: [ ";
        foreach (Light l in infiniteLights)
            s += $"{l}, ";
        return s + " ]";
    }
}

// ImageTileIntegrator Definition
public abstract class ImageTileIntegrator(Camera camera, Sampler sampler, Primitive? aggregate, List<Light> lights)
    : Integrator(aggregate, lights)
{
    // ImageTileIntegrator Protected Members
    protected readonly Camera camera = camera;
    protected readonly Sampler samplerPrototype = sampler;

    // (pbrt's thread_local variables in Render())
    [ThreadStatic] private static Point2i threadPixel;
    [ThreadStatic] private static int threadSampleIndex;

    // ImageTileIntegrator Public Methods
    public abstract void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler);

    // ImageTileIntegrator Method Definitions
    public override void Render()
    {
        // Handle debugStart, if set
        if (Options.debugStart.Length > 0)
        {
            List<int> c = SplitStringToInts(Options.debugStart, ',');
            if (c.Count == 0)
                ErrorExit($"Didn't find integer values after --debugstart: {Options.debugStart}");
            if (c.Count != 3)
                ErrorExit($"Didn't find three integer values after --debugstart: {Options.debugStart}");

            Point2i pPixel0 = new(c[0], c[1]);
            int sampleIndex0 = c[2];

            Sampler tileSampler = samplerPrototype.Clone();
            tileSampler.StartPixelSample(pPixel0, sampleIndex0);

            EvaluatePixelSample(pPixel0, sampleIndex0, tileSampler);

            return;
        }

        using CheckCallbackScope checkCallback = new(() =>
            $"Rendering failed at pixel ({threadPixel.x}, {threadPixel.y}) sample {threadSampleIndex}. Debug with " +
            $"\"--debugstart {threadPixel.x},{threadPixel.y},{threadSampleIndex}\"\n");

        // Declare common variables for rendering image in tiles
        ThreadLocal<Sampler> samplers = new(() => samplerPrototype.Clone());

        Bounds2i pixelBounds = camera.GetFilm().PixelBounds();
        int spp = samplerPrototype.SamplesPerPixel();
        ProgressReporter progress = new((long)spp * pixelBounds.Area(), "Rendering", Options.quiet);

        int waveStart = 0, waveEnd = 1, nextWaveSize = 1;

        if (Options.recordPixelStatistics)
            Stats.StatsEnablePixelStats(pixelBounds, RemoveExtension(camera.GetFilm().GetFilename()));
        // Handle MSE reference image, if provided
        Image? referenceImage = null;
        StreamWriter? mseOutFile = null;
        if (Options.mseReferenceImage.Length > 0)
        {
            ImageAndMetadata mse = Image.Read(Options.mseReferenceImage);
            referenceImage = mse.image;

            Bounds2i msePixelBounds = mse.metadata.pixelBounds ??
                                      new Bounds2i(new Point2i(0, 0), referenceImage.Resolution());
            if (!Inside(pixelBounds, msePixelBounds))
                ErrorExit($"Output image pixel bounds {pixelBounds} aren't inside the MSE image's pixel bounds " +
                          $"{msePixelBounds}.");

            // Transform the pixelBounds of the image we're rendering to the
            // coordinate system with msePixelBounds.pMin at the origin, which
            // in turn gives us the section of the MSE image to crop. (This is
            // complicated by the fact that Image doesn't support pixel
            // bounds...)
            Bounds2i cropBounds = new(new Point2i(pixelBounds.pMin - msePixelBounds.pMin),
                                      new Point2i(pixelBounds.pMax - msePixelBounds.pMin));
            referenceImage = referenceImage.Crop(cropBounds);
            CHECK(referenceImage.Resolution() == new Point2i(pixelBounds.Diagonal()));

            try
            {
                mseOutFile = new StreamWriter(Options.mseReferenceOutput);
            }
            catch (Exception e)
            {
                ErrorExit($"{Options.mseReferenceOutput}: {e.Message}");
            }
        }

        // Connect to display server if needed
        if (Options.displayServer.Length > 0)
        {
            Film film = camera.GetFilm();
            DisplayDynamic(film.GetFilename(), new Point2i(pixelBounds.Diagonal()), ["R", "G", "B"],
                           (Bounds2i b, float[][] displayValue) =>
                           {
                               int index = 0;
                               foreach (Point2i p in b)
                               {
                                   RGB rgb = film.GetPixelRGB(pixelBounds.pMin + p, 2f / (waveStart + waveEnd));
                                   for (int c = 0; c < 3; ++c)
                                       displayValue[c][index] = (float)rgb[c];
                                   ++index;
                               }
                           });
        }

        // Render image in waves
        while (waveStart < spp)
        {
            // Render current wave's image tiles in parallel
            int ws = waveStart, we = waveEnd;
            ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
            {
                // Render image tile given by _tileBounds_
                Sampler sampler = samplers.Value!;
                foreach (Point2i pPixel in tileBounds)
                {
                    Stats.StatsReportPixelStart(pPixel);
                    threadPixel = pPixel;
                    // Render samples in pixel _pPixel_
                    for (int sampleIndex = ws; sampleIndex < we; ++sampleIndex)
                    {
                        threadSampleIndex = sampleIndex;
                        sampler.StartPixelSample(pPixel, sampleIndex);
                        EvaluatePixelSample(pPixel, sampleIndex, sampler);
                    }

                    Stats.StatsReportPixelEnd(pPixel);
                }
                progress.Update((we - ws) * tileBounds.Area());
            });

            // Update start and end wave
            waveStart = waveEnd;
            waveEnd = Math.Min(spp, waveEnd + nextWaveSize);
            if (referenceImage == null)
                nextWaveSize = Math.Min(2 * nextWaveSize, 64);
            if (waveStart == spp)
                progress.Done();

            // Optionally write current image to disk
            if (waveStart == spp || Options.writePartialImages || referenceImage != null)
            {
                LOG_VERBOSE($"Writing image with spp = {waveStart}");
                ImageMetadata metadata = new()
                {
                    renderTimeSeconds = (float)progress.ElapsedSeconds(),
                    samplesPerPixel = waveStart
                };
                if (referenceImage != null)
                {
                    ImageMetadata filmMetadata = new();
                    Image filmImage = camera.GetFilm().GetImage(filmMetadata, 1f / waveStart);
                    ImageChannelValues mse = filmImage.MSE(filmImage.AllChannelsDesc(), referenceImage);
                    mseOutFile!.WriteLine($"{waveStart}, {DoubleToString(mse.Average())}");
                    metadata.MSE = (float)mse.Average();
                    mseOutFile.Flush();
                }
                if (waveStart == spp || Options.writePartialImages)
                {
                    camera.InitMetadata(metadata);
                    camera.GetFilm().WriteImage(metadata, 1.0f / waveStart);
                }
            }
        }

        mseOutFile?.Close();
        DisconnectFromDisplayServer();
        LOG_VERBOSE($"Rendering finished");
    }
}

// RayIntegrator Definition
public abstract class RayIntegrator(Camera camera, Sampler sampler, Primitive? aggregate, List<Light> lights)
    : ImageTileIntegrator(camera, sampler, aggregate, lights)
{
    private static readonly StatCounter nCameraRays = new("Integrator/Camera rays traced");

    // RayIntegrator Public Methods
    // (_initializeVisibleSurface_ is true where pbrt passes a non-null
    // VisibleSurface pointer.)
    public abstract SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface);

    // RayIntegrator Method Definitions
    public sealed override void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler)
    {
        // Sample wavelengths for the ray
        Float lu = sampler.Get1D();
        if (Options.disableWavelengthJitter)
            lu = 0.5f;
        SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(lu);

        // Initialize _CameraSample_ for current sample
        Filter filter = camera.GetFilm().GetFilter();
        CameraSample cameraSample = GetCameraSample(sampler, pPixel, filter);

        // Generate camera ray for current sample
        CameraRayDifferential? cameraRay = camera.GenerateRayDifferential(cameraSample, ref lambda);

        // Trace _cameraRay_ if valid
        SampledSpectrum L = new(0f);
        VisibleSurface visibleSurface = new();
        if (cameraRay is CameraRayDifferential cr)
        {
            // Double check that the ray's direction is normalized.
            DCHECK_GT(Length(cr.ray.d), .999f);
            DCHECK_LT(Length(cr.ray.d), 1.001f);
            // Scale camera ray differentials based on image sampling rate
            Float rayDiffScale = FMath.Max(.125f, 1 / FMath.Sqrt((Float)sampler.SamplesPerPixel()));
            if (!Options.disablePixelJitter)
                cr.ray.ScaleDifferentials(rayDiffScale);

            nCameraRays.Inc();
            // Evaluate radiance along camera ray
            bool initializeVisibleSurface = camera.GetFilm().UsesVisibleSurface();
            L = cr.weight * Li(cr.ray, ref lambda, sampler, initializeVisibleSurface, ref visibleSurface);

            // Issue warning if unexpected radiance value is returned
            if (L.HasNaNs())
            {
                LOG_ERROR($"Not-a-number radiance value returned for pixel ({pPixel.x}, {pPixel.y}), " +
                          $"sample {sampleIndex}. Setting to black.");
                L = new SampledSpectrum(0f);
            }
            else if (IsInf(L.y(lambda)))
            {
                LOG_ERROR($"Infinite radiance value returned for pixel ({pPixel.x}, {pPixel.y}), " +
                          $"sample {sampleIndex}. Setting to black.");
                L = new SampledSpectrum(0f);
            }
        }
        // Add camera ray's contribution to image
        camera.GetFilm().AddSample(pPixel, L, lambda, visibleSurface, cameraSample.filterWeight);
    }
}

// RandomWalkIntegrator Definition
public sealed class RandomWalkIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive? aggregate,
                                         List<Light> lights) : RayIntegrator(camera, sampler, aggregate, lights)
{
    // RandomWalkIntegrator Public Methods
    public static RandomWalkIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                              Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        return new RandomWalkIntegrator(maxDepth, camera, sampler, aggregate, lights);
    }

    public override string ToString() => $"[ RandomWalkIntegrator maxDepth: {maxDepth} ]";

    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface) =>
        LiRandomWalk(ray, ref lambda, sampler, 0);

    // RandomWalkIntegrator Private Methods
    private SampledSpectrum LiRandomWalk(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                         int depth)
    {
        // Intersect ray with scene and return if no intersection
        ShapeIntersection? si = Intersect(ray);
        if (si is not ShapeIntersection s)
        {
            // Return emitted light from infinite light sources
            SampledSpectrum Le0 = new(0f);
            foreach (Light light in infiniteLights)
                Le0 += light.Le(ray, lambda);
            return Le0;
        }
        SurfaceInteraction isect = s.intr;

        // Get emitted radiance at surface intersection
        Vector3f wo = -ray.d;
        SampledSpectrum Le = isect.Le(wo, lambda);

        // Terminate random walk if maximum depth has been reached
        if (depth == maxDepth)
            return Le;

        // Compute BSDF at random walk intersection point
        BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
        if (!bsdf)
            return Le;

        // Randomly sample direction leaving surface for random walk
        Point2f u = sampler.Get2D();
        Vector3f wp = SampleUniformSphere(u);

        // Evaluate BSDF at surface for sampled direction
        SampledSpectrum fcos = bsdf.f(wo, wp) * AbsDot(wp, isect.shading.n);
        if (!fcos)
            return Le;

        // Recursively trace ray to estimate incident radiance at surface
        ray = isect.SpawnRay(wp);
        return Le + fcos * LiRandomWalk(ray, ref lambda, sampler, depth + 1) / (1 / (4 * Pi));
    }
}

// SimplePathIntegrator Definition
public sealed class SimplePathIntegrator : RayIntegrator
{
    // SimplePathIntegrator Private Members
    private readonly int maxDepth;
    private readonly bool sampleLights, sampleBSDF;
    private readonly UniformLightSampler lightSampler;

    // SimplePathIntegrator Method Definitions
    public SimplePathIntegrator(int maxDepth, bool sampleLights, bool sampleBSDF, Camera camera, Sampler sampler,
                                Primitive? aggregate, List<Light> lights)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        this.sampleLights = sampleLights;
        this.sampleBSDF = sampleBSDF;
        lightSampler = new UniformLightSampler(lights.ToArray());
    }

    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface)
    {
        // Estimate radiance along ray using simple path tracing
        SampledSpectrum L = new(0f), beta = new(1f);
        bool specularBounce = true;
        int depth = 0;
        while (beta.IsNonZero())
        {
            // Find next _SimplePathIntegrator_ vertex and accumulate contribution
            // Intersect _ray_ with scene
            ShapeIntersection? si = Intersect(ray);

            // Account for infinite lights if ray has no intersection
            if (si is not ShapeIntersection s)
            {
                if (!sampleLights || specularBounce)
                    foreach (Light light in infiniteLights)
                        L += beta * light.Le(ray, lambda);
                break;
            }

            // Account for emissive surface if light was not sampled
            SurfaceInteraction isect = s.intr;
            if (!sampleLights || specularBounce)
                L += beta * isect.Le(-ray.d, lambda);

            // End path if maximum depth reached
            if (depth++ == maxDepth)
                break;

            // Get BSDF and skip over medium boundaries
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                specularBounce = true;
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // Sample direct illumination if _sampleLights_ is true
            Vector3f wo = -ray.d;
            if (sampleLights)
            {
                SampledLight? sampledLight = lightSampler.Sample(sampler.Get1D());
                if (sampledLight is SampledLight sl)
                {
                    // Sample point on _sampledLight_ to estimate direct illumination
                    Point2f uLight = sampler.Get2D();
                    LightLiSample? lso = sl.light.SampleLi(isect, uLight, lambda);
                    if (lso is LightLiSample ls && ls.L.IsNonZero() && ls.pdf > 0)
                    {
                        // Evaluate BSDF for light and possibly add scattered radiance
                        Vector3f wi = ls.wi;
                        SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, isect.shading.n);
                        if (f.IsNonZero() && Unoccluded(isect, ls.pLight))
                            L += beta * f * ls.L / (sl.p * ls.pdf);
                    }
                }
            }

            // Sample outgoing direction at intersection to continue path
            if (sampleBSDF)
            {
                // Sample BSDF for new path direction
                Float u = sampler.Get1D();
                BSDFSample? bso = bsdf.Sample_f(wo, u, sampler.Get2D());
                if (bso is not BSDFSample bs)
                    break;
                beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
                specularBounce = bs.IsSpecular();
                ray = isect.SpawnRay(bs.wi);
            }
            else
            {
                // Uniformly sample sphere or hemisphere to get new path direction
                Float pdf;
                Vector3f wi;
                BxDFFlags flags = bsdf.Flags();
                if (IsReflective(flags) && IsTransmissive(flags))
                {
                    wi = SampleUniformSphere(sampler.Get2D());
                    pdf = UniformSpherePDF();
                }
                else
                {
                    wi = SampleUniformHemisphere(sampler.Get2D());
                    pdf = UniformHemispherePDF();
                    if (IsReflective(flags) && Dot(wo, isect.n) * Dot(wi, isect.n) < 0)
                        wi = -wi;
                    else if (IsTransmissive(flags) && Dot(wo, isect.n) * Dot(wi, isect.n) > 0)
                        wi = -wi;
                }
                beta *= bsdf.f(wo, wi) * AbsDot(wi, isect.shading.n) / pdf;
                specularBounce = false;
                ray = isect.SpawnRay(wi);
            }

            CHECK_GE(beta.y(lambda), 0f);
            DCHECK(!IsInf(beta.y(lambda)));
        }
        return L;
    }

    public override string ToString() =>
        $"[ SimplePathIntegrator maxDepth: {maxDepth} sampleLights: {(sampleLights ? "true" : "false")} " +
        $"sampleBSDF: {(sampleBSDF ? "true" : "false")} ]";

    public static SimplePathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                              Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        bool sampleLights = parameters.GetOneBool("samplelights", true);
        bool sampleBSDF = parameters.GetOneBool("samplebsdf", true);
        return new SimplePathIntegrator(maxDepth, sampleLights, sampleBSDF, camera, sampler, aggregate, lights);
    }
}

// LightPathIntegrator Definition
public sealed class LightPathIntegrator : ImageTileIntegrator
{
    // LightPathIntegrator Private Members
    private readonly int maxDepth;
    private readonly PowerLightSampler lightSampler;

    // LightPathIntegrator Method Definitions
    public LightPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive? aggregate,
                               List<Light> lights)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        lightSampler = new PowerLightSampler(lights.ToArray());
    }

    public override void EvaluatePixelSample(Point2i pPixel, int sampleIndex, Sampler sampler)
    {
        // Sample wavelengths for the ray
        Float lu = sampler.Get1D();
        if (Options.disableWavelengthJitter)
            lu = 0.5f;
        SampledWavelengths lambda = camera.GetFilm().SampleWavelengths(lu);

        // Sample light to start light path
        Float ul = sampler.Get1D();
        SampledLight? sampledLight = lightSampler.Sample(ul);
        if (sampledLight is not SampledLight sl)
            return;
        Light light = sl.light;
        Float p_l = sl.p;

        // Sample point on light source for light path
        Float time = camera.SampleTime(sampler.Get1D());
        Point2f ul0 = sampler.Get2D();
        Point2f ul1 = sampler.Get2D();
        LightLeSample? leso = light.SampleLe(ul0, ul1, ref lambda, time);
        if (leso is not LightLeSample les || les.pdfPos == 0 || les.pdfDir == 0 || !les.L)
            return;

        // Add contribution of directly visible light source
        if (les.intr is Interaction lintr)
        {
            CameraWiSample? cso = camera.SampleWi(lintr, sampler.Get2D(), ref lambda);
            if (cso is CameraWiSample cs && cs.pdf != 0)
            {
                if (light.PDF_Li(cs.pLens, -cs.wi) is Float pdf && pdf > 0)
                {
                    // Add light's emitted radiance if nonzero and light is visible
                    SampledSpectrum Le = light.L(lintr.p(), lintr.n, lintr.uv, cs.wi, lambda);
                    if (Le.IsNonZero() && Unoccluded(cs.pRef, cs.pLens))
                    {
                        // Compute visible light's path contribution and add to film
                        SampledSpectrum L = Le * DistanceSquared(cs.pRef.p(), cs.pLens.p()) * cs.Wi /
                                            (p_l * pdf * cs.pdf);
                        camera.GetFilm().AddSplat(cs.pRaster, L, lambda);
                    }
                }
            }
        }

        // Follow light path and accumulate contributions to image
        int depth = 0;
        // Initialize light path ray and weighted path throughput _beta_
        RayDifferential ray = new(les.ray);
        SampledSpectrum beta = les.L * les.AbsCosTheta(ray.d) / (p_l * les.pdfPos * les.pdfDir);

        while (true)
        {
            // Intersect light path ray with scene
            ShapeIntersection? si = Intersect(ray);
            if (si is not ShapeIntersection s)
                break;
            SurfaceInteraction isect = s.intr;

            // Get BSDF and skip over medium boundaries
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // End path if maximum depth reached
            if (depth++ == maxDepth)
                break;

            // Splat contribution into film if intersection point is visible to camera
            Point2f u = sampler.Get2D();
            CameraWiSample? cso = camera.SampleWi(isect, u, ref lambda);
            if (cso is CameraWiSample cs && cs.pdf != 0)
            {
                SampledSpectrum L = beta * bsdf.f(isect.wo, cs.wi, TransportMode.Importance) *
                                    AbsDot(cs.wi, isect.shading.n) * cs.Wi / cs.pdf;
                if (L.IsNonZero() && Unoccluded(cs.pRef, cs.pLens))
                    camera.GetFilm().AddSplat(cs.pRaster, L, lambda);
            }

            // Sample BSDF and update light path state
            Float uc = sampler.Get1D();
            BSDFSample? bso = bsdf.Sample_f(isect.wo, uc, sampler.Get2D(), TransportMode.Importance);
            if (bso is not BSDFSample bs)
                break;
            beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
            ray = isect.SpawnRay(ray, bsdf, bs.wi, bs.flags, bs.eta);
        }
    }

    public override string ToString() =>
        $"[ LightPathIntegrator maxDepth: {maxDepth} lightSampler: {lightSampler} ]";

    public static LightPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                             Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        if (camera is not PerspectiveCamera)
            ErrorExit("Only the \"perspective\" camera is currently supported with the \"lightpath\" integrator.");
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        return new LightPathIntegrator(maxDepth, camera, sampler, aggregate, lights);
    }
}

// PathIntegrator Definition
public sealed class PathIntegrator : RayIntegrator
{
    internal static readonly StatPercent zeroRadiancePaths = new("Integrator/Zero-radiance paths");
    internal static readonly StatPercent regularizedBSDFs = new("Integrator/Regularized BSDFs");
    internal static readonly StatIntDistribution pathLength = new("Integrator/Path length");

    // PathIntegrator Private Members
    private readonly int maxDepth;
    private readonly LightSampler lightSampler;
    private readonly bool regularize;

    // PathIntegrator Method Definitions
    public PathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive? aggregate, List<Light> lights,
                          string lightSampleStrategy = "bvh", bool regularize = false)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray());
        this.regularize = regularize;
    }

    // Sample arrays _ucRho_ and _uRho_ for the reflectance estimate (pbrt's
    // double literals converted to Float)
    internal static readonly Float[] ucRho =
    [
        (Float)0.75741637, (Float)0.37870818, (Float)0.7083487, (Float)0.18935409, (Float)0.9149363,
        (Float)0.35417435, (Float)0.5990858, (Float)0.09467703, (Float)0.8578725, (Float)0.45746812,
        (Float)0.686759, (Float)0.17708716, (Float)0.9674518, (Float)0.2995429, (Float)0.5083201,
        (Float)0.047338516
    ];

    internal static readonly Point2f[] uRho =
    [
        P(0.855985, 0.570367), P(0.381823, 0.851844), P(0.285328, 0.764262), P(0.733380, 0.114073),
        P(0.542663, 0.344465), P(0.127274, 0.414848), P(0.964700, 0.947162), P(0.594089, 0.643463),
        P(0.095109, 0.170369), P(0.825444, 0.263359), P(0.429467, 0.454469), P(0.244460, 0.816459),
        P(0.756135, 0.731258), P(0.516165, 0.152852), P(0.180888, 0.214174), P(0.898579, 0.503897)
    ];

    private static Point2f P(double x, double y) => new((Float)x, (Float)y);

    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurf)
    {
        // Declare local variables for _PathIntegrator::Li()_
        SampledSpectrum L = new(0f), beta = new(1f);
        int depth = 0;

        Float p_b = 0, etaScale = 1;
        bool specularBounce = false, anyNonSpecularBounces = false;
        LightSampleContext prevIntrCtx = new();

        // Sample path from camera and accumulate radiance estimate
        while (true)
        {
            // Trace ray and find closest path vertex and its BSDF
            ShapeIntersection? si = Intersect(ray);
            // Add emitted light at intersection point or from the environment
            if (si is not ShapeIntersection s)
            {
                // Incorporate emission from infinite lights for escaped ray
                foreach (Light light in infiniteLights)
                {
                    SampledSpectrum Le0 = light.Le(ray, lambda);
                    if (depth == 0 || specularBounce)
                        L += beta * Le0;
                    else
                    {
                        // Compute MIS weight for infinite light
                        Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, ray.d, true);
                        Float w_b = PowerHeuristic(1, p_b, 1, p_l);

                        L += beta * w_b * Le0;
                    }
                }

                break;
            }
            // Incorporate emission from surface hit by ray
            SampledSpectrum Le = s.intr.Le(-ray.d, lambda);
            if (Le.IsNonZero())
            {
                if (depth == 0 || specularBounce)
                    L += beta * Le;
                else
                {
                    // Compute MIS weight for area light
                    Light areaLight = s.intr.areaLight!;
                    Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                    Float w_l = PowerHeuristic(1, p_b, 1, p_l);

                    L += beta * w_l * Le;
                }
            }

            SurfaceInteraction isect = s.intr;
            // Get BSDF and skip over medium boundaries
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                specularBounce = true; // disable MIS if the indirect ray hits a light
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // Initialize _visibleSurf_ at first intersection
            if (depth == 0 && initializeVisibleSurface)
            {
                // Estimate BSDF's albedo
                SampledSpectrum albedo = bsdf.rho(isect.wo, ucRho, uRho);

                visibleSurf = new VisibleSurface(isect, albedo, lambda);
            }

            // Possibly regularize the BSDF
            if (regularize && anyNonSpecularBounces)
            {
                regularizedBSDFs.Num();
                bsdf.Regularize();
            }

            regularizedBSDFs.Denom();

            // End path if maximum depth reached
            if (depth++ == maxDepth)
                break;

            // Sample direct illumination from the light sources
            if (IsNonSpecular(bsdf.Flags()))
            {
                zeroRadiancePaths.Denom();
                SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler);
                if (!Ld)
                    zeroRadiancePaths.Num();
                L += beta * Ld;
            }

            // Sample BSDF to get new path direction
            Vector3f wo = -ray.d;
            Float u = sampler.Get1D();
            BSDFSample? bso = bsdf.Sample_f(wo, u, sampler.Get2D());
            if (bso is not BSDFSample bs)
                break;
            // Update path state variables after surface scattering
            beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
            p_b = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi) : bs.pdf;
            DCHECK(!IsInf(beta.y(lambda)));
            specularBounce = bs.IsSpecular();
            anyNonSpecularBounces |= !bs.IsSpecular();
            if (bs.IsTransmission())
                etaScale *= Sqr(bs.eta);
            prevIntrCtx = s.intr;

            ray = isect.SpawnRay(ray, bsdf, bs.wi, bs.flags, bs.eta);

            // Possibly terminate the path with Russian roulette
            SampledSpectrum rrBeta = beta * etaScale;
            if (rrBeta.MaxComponentValue() < 1 && depth > 1)
            {
                Float q = FMath.Max(0, 1 - rrBeta.MaxComponentValue());
                if (sampler.Get1D() < q)
                    break;
                beta /= 1 - q;
                DCHECK(!IsInf(beta.y(lambda)));
            }
        }
        pathLength.Add(depth);
        return L;
    }

    private SampledSpectrum SampleLd(in SurfaceInteraction intr, in BSDF bsdf, ref SampledWavelengths lambda,
                                     Sampler sampler)
    {
        // Initialize _LightSampleContext_ for light sampling
        LightSampleContext ctx = new(intr);
        // Try to nudge the light sampling position to correct side of the surface
        BxDFFlags flags = bsdf.Flags();
        if (IsReflective(flags) && !IsTransmissive(flags))
            ctx.pi = new Point3fi(intr.OffsetRayOrigin(intr.wo));
        else if (IsTransmissive(flags) && !IsReflective(flags))
            ctx.pi = new Point3fi(intr.OffsetRayOrigin(-intr.wo));

        // Choose a light source for the direct lighting calculation
        Float u = sampler.Get1D();
        SampledLight? sampledLight = lightSampler.Sample(ctx, u);
        Point2f uLight = sampler.Get2D();
        if (sampledLight is not SampledLight sl)
            return new SampledSpectrum();

        // Sample a point on the light source for direct lighting
        Light light = sl.light;
        DCHECK(light != null && sl.p > 0);
        LightLiSample? lso = light!.SampleLi(ctx, uLight, lambda, true);
        if (lso is not LightLiSample ls || !ls.L || ls.pdf == 0)
            return new SampledSpectrum();

        // Evaluate BSDF for light sample and check light visibility
        Vector3f wo = intr.wo, wi = ls.wi;
        SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, intr.shading.n);
        if (!f || !Unoccluded(intr, ls.pLight))
            return new SampledSpectrum();

        // Return light's contribution to reflected radiance
        Float p_l = sl.p * ls.pdf;
        if (IsDeltaLight(light.Type()))
            return ls.L * f / p_l;
        else
        {
            Float p_b = bsdf.PDF(wo, wi);
            Float w_l = PowerHeuristic(1, p_l, 1, p_b);
            return w_l * ls.L * f / p_l;
        }
    }

    public override string ToString() =>
        $"[ PathIntegrator maxDepth: {maxDepth} lightSampler: {lightSampler} " +
        $"regularize: {(regularize ? "true" : "false")} ]";

    public static PathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                        Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
        bool regularize = parameters.GetOneBool("regularize", false);
        return new PathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, regularize);
    }
}

// SimpleVolPathIntegrator Definition
public sealed class SimpleVolPathIntegrator : RayIntegrator
{
    // SimpleVolPathIntegrator Private Members
    private readonly int maxDepth;

    // SimpleVolPathIntegrator Method Definitions
    public SimpleVolPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive? aggregate,
                                   List<Light> lights)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        foreach (Light light in lights)
        {
            if (IsDeltaLight(light.Type()))
                ErrorExit("SimpleVolPathIntegrator only supports area and infinite light sources");
        }
    }

    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface)
    {
        // Declare local variables for delta tracking integration
        SampledSpectrum L = new(0f);
        Float beta = 1f;
        int depth = 0;

        // Terminate secondary wavelengths before starting random walk
        lambda.TerminateSecondary();
        SampledWavelengths lam = lambda;

        while (true)
        {
            // Estimate radiance for ray path using delta tracking
            ShapeIntersection? si = Intersect(ray);
            bool scattered = false, terminated = false;
            if (ray.medium != null)
            {
                // Initialize _RNG_ for sampling the majorant transmittance
                ulong hash0 = Hash(sampler.Get1D());
                ulong hash1 = Hash(sampler.Get1D());
                RNG rng = new(hash0, hash1);

                // Sample medium using delta tracking
                Float tMax = si.HasValue ? si.Value.tHit : Infinity;
                Float u = sampler.Get1D();
                Float uMode = sampler.Get1D();
                SampleT_maj(ray, tMax, u, ref rng, lam,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        // Compute medium event probabilities for interaction
                        Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                        Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                        Float pNull = FMath.Max(0, 1 - pAbsorb - pScatter);

                        // Randomly sample medium scattering event for delta tracking
                        int mode = SampleDiscrete([pAbsorb, pScatter, pNull], uMode);
                        if (mode == 0)
                        {
                            // Handle absorption event for medium sample
                            L += beta * mp.Le;
                            terminated = true;
                            return false;
                        }
                        else if (mode == 1)
                        {
                            // Handle regular scattering event for medium sample
                            // Stop path sampling if maximum depth has been reached
                            if (depth++ >= maxDepth)
                            {
                                terminated = true;
                                return false;
                            }

                            // Sample phase function for medium scattering event
                            // (braced initializer: left to right)
                            Float u0 = rng.Uniform<Float>();
                            Point2f u = new(u0, rng.Uniform<Float>());
                            PhaseFunctionSample? pso = mp.phase.Sample_p(-ray.d, u);
                            if (pso is not PhaseFunctionSample ps)
                            {
                                terminated = true;
                                return false;
                            }

                            // Update state for recursive evaluation of $L_\roman{i}$
                            beta *= ps.p / ps.pdf;
                            ray.o = p;
                            ray.d = ps.wi;
                            scattered = true;
                            return false;
                        }
                        else
                        {
                            // Handle null-scattering event for medium sample
                            uMode = rng.Uniform<Float>();
                            return true;
                        }
                    });
            }
            // Handle terminated and unscattered rays after medium sampling
            if (terminated)
                return L;
            if (scattered)
                continue;
            // Add emission to surviving ray
            if (si is ShapeIntersection s)
                L += beta * s.intr.Le(-ray.d, lambda);
            else
            {
                foreach (Light light in infiniteLights)
                    L += beta * light.Le(ray, lambda);
                return L;
            }

            // Handle surface intersection along ray path
            SurfaceInteraction isect = s.intr;
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
                isect.SkipIntersection(ref ray, s.tHit);
            else
            {
                // Report error if BSDF returns a valid sample
                Float uc = sampler.Get1D();
                Point2f u = sampler.Get2D();
                if (bsdf.Sample_f(-ray.d, uc, u).HasValue)
                    ErrorExit("SimpleVolPathIntegrator doesn't support surface scattering.");
                else
                    break;
            }
        }
        return L;
    }

    public override string ToString() => $"[ SimpleVolPathIntegrator maxDepth: {maxDepth} ] ";

    public static SimpleVolPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                                 Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        return new SimpleVolPathIntegrator(maxDepth, camera, sampler, aggregate, lights);
    }
}

// VolPathIntegrator Definition
public sealed class VolPathIntegrator : RayIntegrator
{
    private static readonly StatCounter volumeInteractions = new("Integrator/Volume interactions");
    private static readonly StatCounter surfaceInteractions = new("Integrator/Surface interactions");

    // VolPathIntegrator Private Members
    private readonly int maxDepth;
    private readonly LightSampler lightSampler;
    private readonly bool regularize;

    // VolPathIntegrator Public Methods
    public VolPathIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive? aggregate,
                             List<Light> lights, string lightSampleStrategy = "bvh", bool regularize = false)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray());
        this.regularize = regularize;
    }

    // VolPathIntegrator Method Definitions
    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurf)
    {
        // Declare state variables for volumetric path sampling
        SampledSpectrum L = new(0f), beta = new(1f), r_u = new(1f), r_l = new(1f);
        bool specularBounce = false, anyNonSpecularBounces = false;
        int depth = 0;
        Float etaScale = 1;

        LightSampleContext prevIntrContext = new();

        while (true)
        {
            // Sample segment of volumetric scattering path
            ShapeIntersection? si = Intersect(ray);
            if (ray.medium != null)
            {
                // Sample the participating medium
                bool scattered = false, terminated = false;
                Float tMax = si.HasValue ? si.Value.tHit : Infinity;
                // Initialize _RNG_ for sampling the majorant transmittance
                ulong hash0 = Hash(sampler.Get1D());
                ulong hash1 = Hash(sampler.Get1D());
                RNG rng = new(hash0, hash1);

                SampledWavelengths lam = lambda;
                SampledSpectrum T_maj = SampleT_maj(ray, tMax, sampler.Get1D(), ref rng, lam,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        // Handle medium scattering event for ray
                        if (!beta)
                        {
                            terminated = true;
                            return false;
                        }
                        volumeInteractions.Inc();
                        // Add emission from medium scattering event
                        if (depth < maxDepth && mp.Le.IsNonZero())
                        {
                            // Compute $\beta'$ at new path vertex
                            Float pdf = sigma_maj[0] * T_maj[0];
                            SampledSpectrum betap = beta * T_maj / pdf;

                            // Compute rescaled path probability for absorption at path vertex
                            SampledSpectrum r_e = r_u * sigma_maj * T_maj / pdf;

                            // Update _L_ for medium emission
                            if (r_e)
                                L += betap * mp.sigma_a * mp.Le / r_e.Average();
                        }

                        // Compute medium event probabilities for interaction
                        Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                        Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                        Float pNull = FMath.Max(0, 1 - pAbsorb - pScatter);

                        CHECK_GE(1 - pAbsorb - pScatter, -(Float)1e-6);
                        // Sample medium scattering event type and update path
                        Float um = rng.Uniform<Float>();
                        int mode = SampleDiscrete([pAbsorb, pScatter, pNull], um);
                        if (mode == 0)
                        {
                            // Handle absorption along ray path
                            terminated = true;
                            return false;
                        }
                        else if (mode == 1)
                        {
                            // Handle scattering along ray path
                            // Stop path sampling if maximum depth has been reached
                            if (depth++ >= maxDepth)
                            {
                                terminated = true;
                                return false;
                            }

                            // Update _beta_ and _r_u_ for real-scattering event
                            Float pdf = T_maj[0] * mp.sigma_s[0];
                            beta *= T_maj * mp.sigma_s / pdf;
                            r_u *= T_maj * mp.sigma_s / pdf;

                            if (beta.IsNonZero() && r_u.IsNonZero())
                            {
                                // Sample direct lighting at volume-scattering event
                                MediumInteraction intr = new(p, -ray.d, ray.time, ray.medium, mp.phase);
                                L += SampleLd(intr, ref lam, sampler, beta, r_u);

                                // Sample new direction at real-scattering event
                                Point2f u = sampler.Get2D();
                                PhaseFunctionSample? pso = intr.phase!.Sample_p(-ray.d, u);
                                if (pso is not PhaseFunctionSample ps || ps.pdf == 0)
                                    terminated = true;
                                else
                                {
                                    // Update ray path state for indirect volume scattering
                                    beta *= ps.p / ps.pdf;
                                    r_l = r_u / ps.pdf;
                                    prevIntrContext = new LightSampleContext((Interaction)intr);
                                    scattered = true;
                                    ray.o = p;
                                    ray.d = ps.wi;
                                    specularBounce = false;
                                    anyNonSpecularBounces = true;
                                }
                            }
                            return false;
                        }
                        else
                        {
                            // Handle null scattering along ray path
                            SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                            Float pdf = T_maj[0] * sigma_n[0];
                            beta *= T_maj * sigma_n / pdf;
                            if (pdf == 0)
                                beta = new SampledSpectrum(0f);
                            r_u *= T_maj * sigma_n / pdf;
                            r_l *= T_maj * sigma_maj / pdf;
                            return beta.IsNonZero() && r_u.IsNonZero();
                        }
                    });
                // Handle terminated, scattered, and unscattered medium rays
                if (terminated || !beta || !r_u)
                    return L;
                if (scattered)
                    continue;

                beta *= T_maj / T_maj[0];
                r_u *= T_maj / T_maj[0];
                r_l *= T_maj / T_maj[0];
            }
            // Handle surviving unscattered rays
            // Add emitted light at volume path vertex or from the environment
            if (si is not ShapeIntersection s)
            {
                // Accumulate contributions from infinite light sources
                foreach (Light light in infiniteLights)
                {
                    SampledSpectrum Le0 = light.Le(ray, lambda);
                    if (Le0)
                    {
                        if (depth == 0 || specularBounce)
                            L += beta * Le0 / r_u.Average();
                        else
                        {
                            // Add infinite light contribution using both PDFs with MIS
                            Float p_l = lightSampler.PMF(prevIntrContext, light) *
                                        light.PDF_Li(prevIntrContext, ray.d, true);
                            r_l *= p_l;
                            L += beta * Le0 / (r_u + r_l).Average();
                        }
                    }
                }

                break;
            }
            SurfaceInteraction isect = s.intr;
            SampledSpectrum Le = isect.Le(-ray.d, lambda);
            if (Le)
            {
                // Add contribution of emission from intersected surface
                if (depth == 0 || specularBounce)
                    L += beta * Le / r_u.Average();
                else
                {
                    // Add surface light contribution using both PDFs with MIS
                    Light areaLight = isect.areaLight!;
                    Float p_l = lightSampler.PMF(prevIntrContext, areaLight) *
                                areaLight.PDF_Li(prevIntrContext, ray.d, true);
                    r_l *= p_l;
                    L += beta * Le / (r_u + r_l).Average();
                }
            }

            // Get BSDF and skip over medium boundaries
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // Initialize _visibleSurf_ at first intersection
            if (depth == 0 && initializeVisibleSurface)
            {
                // Estimate BSDF's albedo
                SampledSpectrum albedo = bsdf.rho(isect.wo, PathIntegrator.ucRho, PathIntegrator.uRho);

                visibleSurf = new VisibleSurface(isect, albedo, lambda);
            }

            // Terminate path if maximum depth reached
            if (depth++ >= maxDepth)
                return L;

            surfaceInteractions.Inc();
            // Possibly regularize the BSDF
            if (regularize && anyNonSpecularBounces)
            {
                PathIntegrator.regularizedBSDFs.Num();
                bsdf.Regularize();
            }

            // Sample illumination from lights to find attenuated path contribution
            if (IsNonSpecular(bsdf.Flags()))
            {
                L += SampleLd(isect, bsdf, ref lambda, sampler, beta, r_u);
                DCHECK(IsInf(L.y(lambda)) == false);
            }
            prevIntrContext = new LightSampleContext(isect);

            // Sample BSDF to get new volumetric path direction
            Vector3f wo = isect.wo;
            Float u = sampler.Get1D();
            BSDFSample? bso = bsdf.Sample_f(wo, u, sampler.Get2D());
            if (bso is not BSDFSample bs)
                break;
            // Update _beta_ and rescaled path probabilities for BSDF scattering
            beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
            if (bs.pdfIsProportional)
                r_l = r_u / bsdf.PDF(wo, bs.wi);
            else
                r_l = r_u / bs.pdf;

            DCHECK(IsInf(beta.y(lambda)) == false);
            // Update volumetric integrator path state after surface scattering
            specularBounce = bs.IsSpecular();
            anyNonSpecularBounces |= !bs.IsSpecular();
            if (bs.IsTransmission())
                etaScale *= Sqr(bs.eta);
            ray = isect.SpawnRay(ray, bsdf, bs.wi, bs.flags, bs.eta);

            // Account for attenuated subsurface scattering, if applicable
            BSSRDF? bssrdf = isect.GetBSSRDF(ray, ref lambda, camera);
            if (bssrdf != null && bs.IsTransmission())
            {
                // Sample BSSRDF probe segment to find exit point
                Float uc = sampler.Get1D();
                Point2f up = sampler.Get2D();
                BSSRDFProbeSegment? probeSegO = bssrdf.SampleSp(uc, up);
                if (probeSegO is not BSSRDFProbeSegment probeSeg)
                    break;

                // Sample random intersection along BSSRDF probe segment
                ulong seed = MixBits(FloatToBits(sampler.Get1D()));
                WeightedReservoirSampler<SubsurfaceInteraction> interactionSampler = new(seed);
                // Intersect BSSRDF sampling ray against the scene geometry
                Interaction @base = new(probeSeg.p0, ray.time, (Medium?)null);
                while (true)
                {
                    Ray r = @base.SpawnRayTo(probeSeg.p1);
                    if (r.d == new Vector3f(0, 0, 0))
                        break;
                    ShapeIntersection? psi = Intersect(r, 1);
                    if (psi is not ShapeIntersection ps)
                        break;
                    @base = ps.intr;
                    if (ps.intr.material == isect.material)
                        interactionSampler.Add(new SubsurfaceInteraction(ps.intr), 1f);
                }

                if (!interactionSampler.HasSample())
                    break;

                // Convert probe intersection to _BSSRDFSample_
                SubsurfaceInteraction ssi = interactionSampler.GetSample();
                BSSRDFSample bssrdfSample = bssrdf.ProbeIntersectionToSample(ssi);
                if (!bssrdfSample.Sp || !bssrdfSample.pdf)
                    break;

                // Update path state for subsurface scattering
                Float pdf = interactionSampler.SampleProbability() * bssrdfSample.pdf[0];
                beta *= bssrdfSample.Sp / pdf;
                r_u *= bssrdfSample.pdf / bssrdfSample.pdf[0];
                SurfaceInteraction pi = (SurfaceInteraction)ssi;
                pi.wo = bssrdfSample.wo;
                prevIntrContext = new LightSampleContext(pi);
                // Possibly regularize subsurface BSDF
                BSDF Sw = bssrdfSample.Sw;
                anyNonSpecularBounces = true;
                if (regularize)
                {
                    PathIntegrator.regularizedBSDFs.Num();
                    Sw.Regularize();
                }
                else
                    PathIntegrator.regularizedBSDFs.Denom();

                // Account for attenuated direct illumination subsurface scattering
                L += SampleLd(pi, Sw, ref lambda, sampler, beta, r_u);

                // Sample ray for indirect subsurface scattering
                Float u2 = sampler.Get1D();
                BSDFSample? bso2 = Sw.Sample_f(pi.wo, u2, sampler.Get2D());
                if (bso2 is not BSDFSample bs2)
                    break;
                beta *= bs2.f * AbsDot(bs2.wi, pi.shading.n) / bs2.pdf;
                r_l = r_u / bs2.pdf;
                // Don't increment depth this time...
                DCHECK(!IsInf(beta.y(lambda)));
                specularBounce = bs2.IsSpecular();
                ray = pi.SpawnRay(bs2.wi);
            }

            // Possibly terminate volumetric path with Russian roulette
            if (!beta)
                break;
            SampledSpectrum rrBeta = beta * etaScale / r_u.Average();
            Float uRR = sampler.Get1D();
            if (rrBeta.MaxComponentValue() < 1 && depth > 1)
            {
                Float q = FMath.Max(0, 1 - rrBeta.MaxComponentValue());
                if (uRR < q)
                    break;
                beta /= 1 - q;
            }
        }
        return L;
    }

    // (pbrt's Interaction argument is a SurfaceInteraction when bsdf is
    // non-null and a MediumInteraction otherwise; the two overloads take
    // them and call the third with the light sample context and the BSDF or
    // the phase function.)
    private SampledSpectrum SampleLd(in SurfaceInteraction intr, in BSDF bsdf, ref SampledWavelengths lambda,
                                     Sampler sampler, SampledSpectrum beta, SampledSpectrum r_p)
    {
        // Estimate light-sampled direct illumination at _intr_
        // Initialize _LightSampleContext_ for volumetric light sampling
        LightSampleContext ctx = new(intr);
        // Try to nudge the light sampling position to correct side of the surface
        BxDFFlags flags = bsdf.Flags();
        if (IsReflective(flags) && !IsTransmissive(flags))
            ctx.pi = new Point3fi(intr.OffsetRayOrigin(intr.wo));
        else if (IsTransmissive(flags) && !IsReflective(flags))
            ctx.pi = new Point3fi(intr.OffsetRayOrigin(-intr.wo));

        return SampleLd(ctx, intr, intr.shading.n, bsdf, null, ref lambda, sampler, beta, r_p);
    }

    private SampledSpectrum SampleLd(in MediumInteraction intr, ref SampledWavelengths lambda, Sampler sampler,
                                     SampledSpectrum beta, SampledSpectrum r_p)
    {
        LightSampleContext ctx = new((Interaction)intr);
        return SampleLd(ctx, intr, new Normal3f(), null, intr.phase, ref lambda, sampler, beta, r_p);
    }

    private SampledSpectrum SampleLd(in LightSampleContext ctx, in Interaction intr, Normal3f shadingN, BSDF? bsdf,
                                     PhaseFunction? phase, ref SampledWavelengths lambda, Sampler sampler,
                                     SampledSpectrum beta, SampledSpectrum r_p)
    {
        // Sample a light source using _lightSampler_
        Float u = sampler.Get1D();
        SampledLight? sampledLight = lightSampler.Sample(ctx, u);
        Point2f uLight = sampler.Get2D();
        if (sampledLight is not SampledLight sl)
            return new SampledSpectrum(0f);
        Light light = sl.light;
        DCHECK(light != null && sl.p != 0);

        // Sample a point on the light source
        LightLiSample? lso = light!.SampleLi(ctx, uLight, lambda, true);
        if (lso is not LightLiSample ls || !ls.L || ls.pdf == 0)
            return new SampledSpectrum(0f);
        Float p_l = sl.p * ls.pdf;

        // Evaluate BSDF or phase function for light sample direction
        Float scatterPDF;
        SampledSpectrum f_hat;
        Vector3f wo = intr.wo, wi = ls.wi;
        if (bsdf is BSDF b)
        {
            // Update _f_hat_ and _scatterPDF_ accounting for the BSDF
            f_hat = b.f(wo, wi) * AbsDot(wi, shadingN);
            scatterPDF = b.PDF(wo, wi);
        }
        else
        {
            // Update _f_hat_ and _scatterPDF_ accounting for the phase function
            CHECK(intr.IsMediumInteraction());
            f_hat = new SampledSpectrum(phase!.p(wo, wi));
            scatterPDF = phase.PDF(wo, wi);
        }
        if (!f_hat)
            return new SampledSpectrum(0f);

        // Declare path state variables for ray to light source
        Ray lightRay = intr.SpawnRayTo(ls.pLight);
        SampledSpectrum T_ray = new(1f), r_l = new(1f), r_u = new(1f);
        RNG rng = new(Hash(lightRay.o), Hash(lightRay.d));

        while (lightRay.d != new Vector3f(0, 0, 0))
        {
            // Trace ray through media to estimate transmittance
            ShapeIntersection? si = Intersect(lightRay, 1 - ShadowEpsilon);
            // Handle opaque surface along ray's path
            if (si.HasValue && si.Value.intr.material != null)
                return new SampledSpectrum(0f);

            // Update transmittance for current ray segment
            if (lightRay.medium != null)
            {
                Float tMax = si.HasValue ? si.Value.tHit : (1 - ShadowEpsilon);
                Float u1 = rng.Uniform<Float>();
                SampledSpectrum T_maj = SampleT_maj(lightRay, tMax, u1, ref rng, lambda,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        // Update ray transmittance estimate at sampled point
                        // Update _T_ray_ and PDFs using ratio-tracking estimator
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                        Float pdf = T_maj[0] * sigma_maj[0];
                        T_ray *= T_maj * sigma_n / pdf;
                        r_l *= T_maj * sigma_maj / pdf;
                        r_u *= T_maj * sigma_n / pdf;

                        // Possibly terminate transmittance computation using
                        // Russian roulette
                        SampledSpectrum Tr = T_ray / (r_l + r_u).Average();
                        if (Tr.MaxComponentValue() < 0.05f)
                        {
                            Float q = 0.75f;
                            if (rng.Uniform<Float>() < q)
                                T_ray = new SampledSpectrum(0f);
                            else
                                T_ray /= 1 - q;
                        }

                        if (!T_ray)
                            return false;
                        return true;
                    });
                // Update transmittance estimate for final segment
                T_ray *= T_maj / T_maj[0];
                r_l *= T_maj / T_maj[0];
                r_u *= T_maj / T_maj[0];
            }

            // Generate next ray segment or return final transmittance
            if (!T_ray)
                return new SampledSpectrum(0f);
            if (si is not ShapeIntersection s)
                break;
            lightRay = s.intr.SpawnRayTo(ls.pLight);
        }
        // Return path contribution function estimate for direct lighting
        r_l *= r_p * p_l;
        r_u *= r_p * scatterPDF;
        if (IsDeltaLight(light.Type()))
            return beta * f_hat * T_ray * ls.L / r_l.Average();
        else
            return beta * f_hat * T_ray * ls.L / (r_l + r_u).Average();
    }

    public override string ToString() =>
        $"[ VolPathIntegrator maxDepth: {maxDepth} lightSampler: {lightSampler} " +
        $"regularize: {(regularize ? "true" : "false")} ]";

    public static VolPathIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                           Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
        bool regularize = parameters.GetOneBool("regularize", false);
        return new VolPathIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, regularize);
    }
}

// AOIntegrator Definition
public sealed class AOIntegrator(bool cosSample, Float maxDist, Camera camera, Sampler sampler,
                                 Primitive? aggregate, List<Light> lights, Spectrum illuminant)
    : RayIntegrator(camera, sampler, aggregate, lights)
{
    private readonly Float illumScale = 1f / SpectrumToPhotometric(illuminant);

    // AOIntegrator Method Definitions
    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface)
    {
        // Intersect _ray_ with scene and store intersection in _isect_
        while (true)
        {
            ShapeIntersection? si = Intersect(ray);
            if (si is not ShapeIntersection s)
                break;
            SurfaceInteraction isect = s.intr;
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // Compute coordinate frame based on true geometry, not shading
            // geometry.
            Normal3f n = FaceForward(isect.n, -ray.d);

            Vector3f wi;
            Float pdf;
            Point2f u = sampler.Get2D();
            if (cosSample)
            {
                wi = SampleCosineHemisphere(u);
                pdf = CosineHemispherePDF(FMath.Abs(wi.z));
            }
            else
            {
                wi = SampleUniformHemisphere(u);
                pdf = UniformHemispherePDF();
            }
            if (pdf == 0)
                return new SampledSpectrum(0f);

            Frame f = Frame.FromZ(n);
            wi = f.FromLocal(wi);

            // Divide by pi so that fully visible is one.
            Ray r = isect.SpawnRay(wi);
            if (!IntersectP(r, maxDist))
                return illumScale * illuminant.Sample(lambda) * Dot(wi, n) / (Pi * pdf);
            break;
        }
        return new SampledSpectrum(0f);
    }

    public override string ToString() =>
        $"[ AOIntegrator cosSample: {(cosSample ? "true" : "false")} maxDist: {FloatToString(maxDist)} " +
        $"illuminant: {illuminant} ]";

    public static AOIntegrator Create(ParameterDictionary parameters, Spectrum illuminant, Camera camera,
                                      Sampler sampler, Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        bool cosSample = parameters.GetOneBool("cossample", true);
        Float maxDist = parameters.GetOneFloat("maxdistance", Infinity);
        return new AOIntegrator(cosSample, maxDist, camera, sampler, aggregate, lights, illuminant);
    }
}

public abstract partial class Integrator
{
    public static Integrator Create(string name, ParameterDictionary parameters, Camera camera, Sampler sampler,
                                    Primitive? aggregate, List<Light> lights, RGBColorSpace colorSpace, FileLoc? loc)
    {
        Integrator? integrator = null;
        if (name == "path")
            integrator = PathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "simplepath")
            integrator = SimplePathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "lightpath")
            integrator = LightPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "ambientocclusion")
            integrator = AOIntegrator.Create(parameters, colorSpace.illuminant, camera, sampler, aggregate, lights,
                                             loc);
        else if (name == "randomwalk")
            integrator = RandomWalkIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "simplevolpath")
            integrator = SimpleVolPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "volpath")
            integrator = VolPathIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "bdpt")
            integrator = BDPTIntegrator.Create(parameters, camera, sampler, aggregate, lights, loc);
        else if (name == "mlt")
            integrator = MLTIntegrator.Create(parameters, camera, aggregate, lights, loc);
        else if (name == "sppm")
            integrator = SPPMIntegrator.Create(parameters, colorSpace, camera, sampler, aggregate, lights, loc);
        else if (name == "function")
            integrator = FunctionIntegrator.Create(parameters, camera, sampler, loc);
        else
            ErrorExit(loc, $"{name}: integrator type unknown.");

        if (integrator == null)
            ErrorExit(loc, $"{name}: unable to create integrator.");

        parameters.ReportUnused();
        return integrator!;
    }
}

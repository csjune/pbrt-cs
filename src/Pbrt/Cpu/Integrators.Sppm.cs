// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the SPPM parts of src/pbrt/cpu/integrators.h and
// integrators.cpp
//
// SPPMPixel is a class so the grid's list nodes can refer to it, and the
// grid's atomic list heads are an array updated with
// Interlocked.CompareExchange(). The display server isn't ported.

namespace Pbrt;

// SPPMPixel Definition
internal sealed class SPPMPixel
{
    // SPPMPixel Public Members
    public Float radius = 0;
    public RGB Ld;

    public struct VisiblePoint
    {
        // VisiblePoint Public Methods
        public VisiblePoint() { }

        public VisiblePoint(Point3f p, Vector3f wo, in BSDF bsdf, in SampledSpectrum beta,
                            bool secondaryLambdaTerminated)
        {
            this.p = p;
            this.wo = wo;
            this.bsdf = bsdf;
            this.beta = beta;
            this.secondaryLambdaTerminated = secondaryLambdaTerminated;
        }

        // VisiblePoint Public Members
        public Point3f p;
        public Vector3f wo;
        public BSDF bsdf;
        public SampledSpectrum beta;
        public bool secondaryLambdaTerminated;
    }

    public VisiblePoint vp;
    public readonly AtomicFloat[] Phi_i = new AtomicFloat[3];
    public int m = 0;
    public RGB tau;
    public Float n = 0;
}

// SPPMPixelListNode Definition
internal sealed class SPPMPixelListNode
{
    public SPPMPixel pixel = null!;
    public SPPMPixelListNode? next;
}

// SPPMIntegrator Definition
public sealed class SPPMIntegrator : Integrator
{
    private static readonly StatRatio visiblePointsChecked =
        new("Stochastic Progressive Photon Mapping/Visible points checked per photon intersection");
    private static readonly StatCounter photonPaths =
        new("Stochastic Progressive Photon Mapping/Photon paths followed");
    private static readonly StatIntDistribution gridCellsPerVisiblePoint =
        new("Stochastic Progressive Photon Mapping/Grid cells per visible point");
    private static readonly StatMemoryCounter pixelMemoryBytes = new("Memory/SPPM Pixels");

    // SPPMIntegrator Private Members
    private readonly Camera camera;
    private readonly Float initialSearchRadius;
    private readonly Sampler samplerPrototype;
    private readonly int digitPermutationsSeed;
    private readonly int maxDepth;
    private readonly int photonsPerIteration;
    private readonly RGBColorSpace colorSpace;

    // SPPMIntegrator Public Methods
    public SPPMIntegrator(Camera camera, Sampler sampler, Primitive? aggregate, List<Light> lights,
                          int photonsPerIteration, int maxDepth, Float initialSearchRadius, int seed,
                          RGBColorSpace colorSpace)
        : base(aggregate, lights)
    {
        this.camera = camera;
        samplerPrototype = sampler;
        this.initialSearchRadius = initialSearchRadius;
        this.maxDepth = maxDepth;
        this.photonsPerIteration = photonsPerIteration > 0 ? photonsPerIteration
                                                           : camera.GetFilm().PixelBounds().Area();
        this.colorSpace = colorSpace;
        digitPermutationsSeed = seed;
    }

    // SPPM Utility Functions
    private static bool ToGrid(Point3f p, in Bounds3f bounds, int[] gridRes, out Point3i pi)
    {
        bool inBounds = true;
        Vector3f pg = bounds.Offset(p);
        pi = new Point3i();
        for (int i = 0; i < 3; ++i)
        {
            pi[i] = (int)(gridRes[i] * pg[i]);
            inBounds &= pi[i] >= 0 && pi[i] < gridRes[i];
            pi[i] = Clamp(pi[i], 0, gridRes[i] - 1);
        }
        return inBounds;
    }

    // SPPM Method Definitions
    public override void Render()
    {
        // Initialize local variables for _SPPMIntegrator::Render()_
        if (Options.recordPixelStatistics)
            Stats.StatsEnablePixelStats(camera.GetFilm().PixelBounds(),
                                        RemoveExtension(camera.GetFilm().GetFilename()));
        // Define variables for commonly used values in SPPM rendering
        int nIterations = samplerPrototype.SamplesPerPixel();
        ProgressReporter progress = new(2 * nIterations, "Rendering", Options.quiet);
        // (std::sqrt() of an int is a double.)
        Float invSqrtSPP = (Float)(1.0 / Math.Sqrt(nIterations));
        Film film = camera.GetFilm();
        Bounds2i pixelBounds = film.PixelBounds();
        int nPixels = pixelBounds.Area();

        // Initialize _pixels_ array for SPPM
        CHECK(!pixelBounds.IsEmpty());
        Array2D<SPPMPixel> pixels = new(pixelBounds);
        foreach (Point2i pp in pixelBounds)
            pixels[pp] = new SPPMPixel { radius = initialSearchRadius };
        // (sizeof(SPPMPixel) is 144 in pbrt.)
        pixelMemoryBytes.Add((long)nPixels * 144);

        // Create light samplers for SPPM rendering
        BVHLightSampler lightSampler = new(lights.ToArray());
        PowerLightSampler shootLightSampler = new(lights.ToArray());

        // Allocate samplers for SPPM rendering
        ThreadLocal<Sampler> threadSamplers = new(() => samplerPrototype.Clone());
        DigitPermutation[] digitPermutations = ComputeRadicalInversePermutations((uint)digitPermutationsSeed);

        for (int iter = 0; iter < nIterations; ++iter)
        {
            // Connect to display server for SPPM if requested
            if (iter == 0 && Options.displayServer.Length > 0)
            {
                DisplayDynamic(film.GetFilename(), new Point2i(pixelBounds.Diagonal()), ["R", "G", "B"],
                               (Bounds2i b, float[][] displayValue) =>
                               {
                                   int index = 0;
                                   // (_iter_ is _nIterations_ for the final update.)
                                   int i = Math.Min(iter, nIterations - 1);
                                   ulong np = (ulong)(i + 1) * (ulong)photonsPerIteration;
                                   foreach (Point2i pPixel in b)
                                   {
                                       // (pbrt indexes _pixels_ with _pPixel_, which is
                                       // off with a crop window.)
                                       SPPMPixel pixel = pixels[pixelBounds.pMin + pPixel];
                                       RGB rgb = pixel.Ld / (i + 1) +
                                                 pixel.tau / ((Float)np * Pi * Sqr(pixel.radius));
                                       for (int c = 0; c < 3; ++c)
                                           displayValue[c][index] = (float)rgb[c];
                                       ++index;
                                   }
                               });
            }

            // Generate SPPM visible points
            // Sample wavelengths for SPPM pass
            Float uLambda = Options.disableWavelengthJitter ? (Float)0.5 : RadicalInverse(1, (ulong)iter);
            SampledWavelengths passLambda = film.SampleWavelengths(uLambda);

            Float timeSample = RadicalInverse(2, (ulong)iter);

            int it = iter;
            ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
            {
                // Follow camera paths for _tileBounds_ in image for SPPM
                Sampler sampler = threadSamplers.Value!;
                foreach (Point2i pPixel in tileBounds)
                {
                    sampler.StartPixelSample(pPixel, it);
                    // Generate camera ray for pixel for SPPM
                    SampledWavelengths lambda = passLambda;
                    CameraSample cs = GetCameraSample(sampler, pPixel, film.GetFilter());
                    cs.time = timeSample;
                    CameraRayDifferential? crdo = camera.GenerateRayDifferential(cs, ref lambda);
                    if (crdo is not CameraRayDifferential crd || !crd.weight)
                        continue;
                    SampledSpectrum beta = crd.weight;
                    RayDifferential ray = crd.ray;
                    if (!Options.disablePixelJitter)
                        ray.ScaleDifferentials(invSqrtSPP);

                    // Follow camera ray path until a visible point is created
                    SPPMPixel pixel = pixels[pPixel];
                    Float etaScale = 1, p_b = 0;
                    bool specularBounce = true, haveSetVisiblePoint = false;
                    LightSampleContext prevIntrCtx = new();
                    int depth = 0;
                    while (true)
                    {
                        visiblePointsChecked.Denom();
                        ShapeIntersection? si = Intersect(ray);
                        // Accumulate light contributions for ray with no intersection
                        if (si is not ShapeIntersection s)
                        {
                            SampledSpectrum L0 = new(0f);
                            // Incorporate emission from infinite lights for escaped ray
                            foreach (Light light in infiniteLights)
                            {
                                SampledSpectrum Le0 = light.Le(ray, lambda);
                                if (depth == 0 || specularBounce)
                                    L0 += beta * Le0;
                                else
                                {
                                    // Compute MIS weight for infinite light
                                    Float p_l = lightSampler.PMF(prevIntrCtx, light) *
                                                light.PDF_Li(prevIntrCtx, ray.d, true);
                                    Float w_b = PowerHeuristic(1, p_b, 1, p_l);

                                    L0 += beta * w_b * Le0;
                                }
                            }

                            pixel.Ld += film.ToOutputRGB(L0, lambda);
                            break;
                        }

                        // Process SPPM camera ray intersection
                        // Compute BSDF at SPPM camera ray intersection
                        SurfaceInteraction isect = s.intr;
                        BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
                        if (!bsdf)
                        {
                            isect.SkipIntersection(ref ray, s.tHit);
                            continue;
                        }

                        PathIntegrator.regularizedBSDFs.Denom();
                        // Add emission from directly visible emissive surfaces to _pixel.Ld_
                        Vector3f wo = -ray.d;
                        SampledSpectrum L = new(0f);
                        // Incorporate emission from surface hit by ray
                        SampledSpectrum Le = s.intr.Le(-ray.d, lambda);
                        if (Le)
                        {
                            if (depth == 0 || specularBounce)
                                L += beta * Le;
                            else
                            {
                                // Compute MIS weight for area light
                                Light areaLight = s.intr.areaLight!;
                                Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) *
                                            areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                                Float w_l = PowerHeuristic(1, p_b, 1, p_l);

                                L += beta * w_l * Le;
                            }
                        }

                        pixel.Ld += film.ToOutputRGB(L, lambda);

                        // Terminate path at maximum depth or if visible point has been set
                        if (depth++ == maxDepth || haveSetVisiblePoint)
                            break;

                        // Accumulate direct illumination at SPPM camera ray intersection
                        SampledSpectrum Ld = SampleLd(isect, bsdf, ref lambda, sampler, lightSampler);
                        if (Ld)
                            pixel.Ld += film.ToOutputRGB(beta * Ld, lambda);

                        // Possibly create visible point and end camera path
                        BxDFFlags flags = bsdf.Flags();
                        if (IsDiffuse(flags) || (IsGlossy(flags) && depth == maxDepth))
                        {
                            pixel.vp = new SPPMPixel.VisiblePoint(isect.p(), wo, bsdf, beta,
                                                                  lambda.SecondaryTerminated());
                            haveSetVisiblePoint = true;
                        }

                        // Spawn ray from SPPM camera path vertex
                        Float u = sampler.Get1D();
                        BSDFSample? bso = bsdf.Sample_f(wo, u, sampler.Get2D());
                        if (bso is not BSDFSample bs)
                            break;
                        specularBounce = bs.IsSpecular();
                        if (bs.IsTransmission())
                            etaScale *= Sqr(bs.eta);

                        beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
                        p_b = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi) : bs.pdf;

                        SampledSpectrum rrBeta = beta * etaScale;
                        if (rrBeta.MaxComponentValue() < 1)
                        {
                            Float q = FMath.Max(.05f, 1 - rrBeta.MaxComponentValue());
                            if (sampler.Get1D() < q)
                                break;
                            beta /= 1 - q;
                        }
                        ray = isect.SpawnRay(ray, bsdf, bs.wi, bs.flags, bs.eta);
                        prevIntrCtx = new LightSampleContext(isect);
                    }
                }
            });
            progress.Update();
            // Create grid of all SPPM visible points
            // Allocate grid for SPPM visible points
            int hashSize = NextPrime(nPixels);
            SPPMPixelListNode?[] grid = new SPPMPixelListNode?[hashSize];

            // Compute grid bounds for SPPM visible points
            Bounds3f gridBounds = new();
            Float maxRadius = 0;
            foreach (Point2i pp in pixelBounds)
            {
                SPPMPixel pixel = pixels[pp];
                if (!pixel.vp.beta)
                    continue;
                Bounds3f vpBound = Expand(new Bounds3f(pixel.vp.p), pixel.radius);
                gridBounds = Union(gridBounds, vpBound);
                maxRadius = FMath.Max(maxRadius, pixel.radius);
            }

            // Compute resolution of SPPM grid in each dimension
            int[] gridRes = new int[3];
            Vector3f diag = gridBounds.Diagonal();
            Float maxDiag = MaxComponentValue(diag);
            int baseGridRes = (int)(maxDiag / maxRadius);
            for (int i = 0; i < 3; ++i)
                gridRes[i] = Math.Max((int)(baseGridRes * diag[i] / maxDiag), 1);

            // Add visible points to SPPM grid
            ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
            {
                foreach (Point2i pPixel in tileBounds)
                {
                    SPPMPixel pixel = pixels[pPixel];
                    if (pixel.vp.beta)
                    {
                        // Add pixel's visible point to applicable grid cells
                        // Find grid cell bounds for pixel's visible point, _pMin_ and _pMax_
                        Float r = pixel.radius;
                        ToGrid(pixel.vp.p - new Vector3f(r, r, r), gridBounds, gridRes, out Point3i pMin);
                        ToGrid(pixel.vp.p + new Vector3f(r, r, r), gridBounds, gridRes, out Point3i pMax);

                        for (int z = pMin.z; z <= pMax.z; ++z)
                            for (int y = pMin.y; y <= pMax.y; ++y)
                                for (int x = pMin.x; x <= pMax.x; ++x)
                                {
                                    // Add visible point to grid cell $(x, y, z)$
                                    int h = (int)(Hash(new Point3i(x, y, z)) % (ulong)hashSize);
                                    CHECK_GE(h, 0);
                                    SPPMPixelListNode node = new() { pixel = pixel };

                                    // Atomically add _node_ to the start of _grid[h]_'s
                                    // linked list
                                    node.next = Volatile.Read(ref grid[h]);
                                    while (true)
                                    {
                                        SPPMPixelListNode? seen = Interlocked.CompareExchange(ref grid[h], node,
                                                                                              node.next);
                                        if (seen == node.next)
                                            break;
                                        node.next = seen;
                                    }
                                }
                        gridCellsPerVisiblePoint.Add((1 + pMax.x - pMin.x) * (1 + pMax.y - pMin.y) *
                                                     (1 + pMax.z - pMin.z));
                    }
                }
            });

            // Trace photons and accumulate contributions
            ParallelFor(0, photonsPerIteration, (long start, long end) =>
            {
                // Follow photon paths for photon index range _start_ - _end_
                Sampler sampler = threadSamplers.Value!;
                for (long photonIndex = start; photonIndex < end; ++photonIndex)
                {
                    // Follow photon path for _photonIndex_
                    // Define sampling lambda functions for photon shooting
                    ulong haltonIndex = (ulong)it * (ulong)photonsPerIteration + (ulong)photonIndex;
                    int haltonDim = 0;
                    Float Sample1D()
                    {
                        Float u = ScrambledRadicalInverse(haltonDim, haltonIndex, digitPermutations[haltonDim]);
                        ++haltonDim;
                        return u;
                    }

                    Point2f Sample2D()
                    {
                        Point2f u = new(
                            ScrambledRadicalInverse(haltonDim, haltonIndex, digitPermutations[haltonDim]),
                            ScrambledRadicalInverse(haltonDim + 1, haltonIndex, digitPermutations[haltonDim + 1]));
                        haltonDim += 2;
                        return u;
                    }

                    // Choose light to shoot photon from
                    Float ul = Sample1D();
                    SampledLight? sampledLight = shootLightSampler.Sample(ul);
                    if (sampledLight is not SampledLight sl)
                        continue;
                    Light light = sl.light;
                    Float p_l = sl.p;

                    // Compute sample values for photon ray leaving light source
                    Point2f uLight0 = Sample2D();
                    Point2f uLight1 = Sample2D();
                    Float uLightTime = camera.SampleTime(timeSample);

                    // Generate _photonRay_ from light source and initialize _beta_
                    SampledWavelengths lambda = passLambda;
                    LightLeSample? leso = light.SampleLe(uLight0, uLight1, ref lambda, uLightTime);
                    if (leso is not LightLeSample les || les.pdfPos == 0 || les.pdfDir == 0 || !les.L)
                        continue;
                    RayDifferential photonRay = new(les.ray);
                    SampledSpectrum beta = (les.AbsCosTheta(photonRay.d) * les.L) / (p_l * les.pdfPos * les.pdfDir);
                    if (!beta)
                        continue;

                    // Follow photon path through scene and record intersections
                    for (int depth = 0; depth < maxDepth; ++depth)
                    {
                        // Intersect photon ray with scene and return if ray escapes
                        ShapeIntersection? si = Intersect(photonRay);
                        if (si is not ShapeIntersection s)
                            break;
                        SurfaceInteraction isect = s.intr;

                        visiblePointsChecked.Denom();
                        if (depth > 0)
                        {
                            // Add photon contribution to nearby visible points
                            if (ToGrid(isect.p(), gridBounds, gridRes, out Point3i photonGridIndex))
                            {
                                int h = (int)(Hash(photonGridIndex) % (ulong)hashSize);
                                CHECK_GE(h, 0);
                                // Add photon contribution to visible points in _grid[h]_
                                for (SPPMPixelListNode? node = Volatile.Read(ref grid[h]); node != null;
                                     node = node.next)
                                {
                                    visiblePointsChecked.Num();
                                    SPPMPixel pixel = node.pixel;
                                    if (DistanceSquared(pixel.vp.p, isect.p()) > Sqr(pixel.radius))
                                        continue;
                                    // Update _pixel_ $\Phi$ and $m$ for nearby photon
                                    Vector3f wi = -photonRay.d;
                                    SampledSpectrum Phi = beta * pixel.vp.bsdf.f(pixel.vp.wo, wi);
                                    // Update _Phi_i_ for photon contribution
                                    SampledWavelengths photonLambda = lambda;
                                    if (pixel.vp.secondaryLambdaTerminated)
                                        photonLambda.TerminateSecondary();
                                    RGB Phi_i = film.ToOutputRGB(pixel.vp.beta * Phi, photonLambda);
                                    for (int i = 0; i < 3; ++i)
                                        pixel.Phi_i[i].Add((float)Phi_i[i]);

                                    Interlocked.Increment(ref pixel.m);
                                }
                            }
                        }
                        // Sample new photon ray direction
                        // Compute BSDF at photon intersection point
                        BSDF photonBSDF = isect.GetBSDF(photonRay, ref lambda, camera, sampler);
                        if (!photonBSDF)
                        {
                            isect.SkipIntersection(ref photonRay, s.tHit);
                            --depth;
                            continue;
                        }

                        // Sample BSDF _fr_ and direction _wi_ for reflected photon
                        Vector3f wo = -photonRay.d;
                        // (GCC evaluates the arguments right to left.)
                        Point2f u2 = Sample2D();
                        Float u1 = Sample1D();
                        BSDFSample? bso = photonBSDF.Sample_f(wo, u1, u2, TransportMode.Importance);
                        if (bso is not BSDFSample bs)
                            break;
                        SampledSpectrum bnew = beta * bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;

                        // Possibly terminate photon path with Russian roulette
                        Float betaRatio = bnew.MaxComponentValue() / beta.MaxComponentValue();
                        Float q = FMath.Max(0, 1 - betaRatio);
                        if (Sample1D() < q)
                            break;
                        beta = bnew / (1 - q);

                        photonRay = new RayDifferential(isect.SpawnRay(bs.wi));
                    }
                }
            });

            progress.Update();
            photonPaths.Add(photonsPerIteration);

            // Update pixel values from this pass's photons
            ParallelFor2D(pixelBounds, (Point2i pPixel) =>
            {
                SPPMPixel p = pixels[pPixel];
                int m = Volatile.Read(ref p.m);
                if (m > 0)
                {
                    // Compute new photon count and search radius given photons
                    Float gamma = (Float)2 / (Float)3;
                    Float nNew = p.n + gamma * m;
                    Float rNew = p.radius * FMath.Sqrt(nNew / (p.n + m));

                    // Update $\tau$ for pixel
                    RGB Phi_i = new(p.Phi_i[0], p.Phi_i[1], p.Phi_i[2]);
                    p.tau = (p.tau + Phi_i) * Sqr(rNew) / Sqr(p.radius);

                    // Set remaining pixel values for next photon pass
                    p.n = nNew;
                    p.radius = rNew;
                    p.m = 0;
                    for (int i = 0; i < 3; ++i)
                        p.Phi_i[i].Set(0);
                }
                // Reset _VisiblePoint_ in pixel
                p.vp.beta = new SampledSpectrum(0f);
                p.vp.bsdf = new BSDF();
            });

            // Periodically write SPPM image to disk
            if (iter + 1 == nIterations || (iter + 1 <= 64 && IsPowerOf2(iter + 1)) || ((iter + 1) % 64 == 0))
            {
                ulong np = (ulong)(iter + 1) * (ulong)photonsPerIteration;
                Image rgbImage = new(PixelFormat.Float, new Point2i(pixelBounds.Diagonal()), ["R", "G", "B"]);

                ParallelFor2D(pixelBounds, (Point2i pPixel) =>
                {
                    // Compute radiance _L_ for SPPM pixel _pPixel_
                    SPPMPixel pixel = pixels[pPixel];
                    RGB L = pixel.Ld / (it + 1) + pixel.tau / ((Float)np * Pi * Sqr(pixel.radius));

                    Point2i pImage = new(pPixel - pixelBounds.pMin);
                    rgbImage.SetChannels(pImage, [L.r, L.g, L.b]);
                });

                ImageMetadata metadata = new()
                {
                    renderTimeSeconds = (float)progress.ElapsedSeconds(),
                    samplesPerPixel = iter + 1,
                    pixelBounds = pixelBounds,
                    fullResolution = camera.GetFilm().FullResolution(),
                    colorSpace = colorSpace
                };
                camera.InitMetadata(metadata);
                rgbImage.Write(camera.GetFilm().GetFilename(), metadata);

                // Write SPPM radius image, if requested
                if (Environment.GetEnvironmentVariable("SPPM_RADIUS") != null)
                {
                    Image rimg = new(PixelFormat.Float, new Point2i(pixelBounds.Diagonal()), ["Radius"]);
                    Float minrad = 1e30f, maxrad = 0;
                    foreach (Point2i pp in pixelBounds)
                    {
                        minrad = FMath.Min(minrad, pixels[pp].radius);
                        maxrad = FMath.Max(maxrad, pixels[pp].radius);
                    }
                    Console.Error.Write(FormattableString.Invariant(
                        $"iterations: {iter + 1} ({progress.ElapsedSeconds():F2} s) radius range: {minrad:F6} - {maxrad:F6}\n"));
                    foreach (Point2i pPixel in pixelBounds)
                    {
                        SPPMPixel p = pixels[pPixel];
                        Float v = 1f - (p.radius - minrad) / (maxrad - minrad);
                        Point2i pImage = new(pPixel - pixelBounds.pMin);
                        rimg.SetChannel(pImage, 0, v);
                    }
                    ImageMetadata rmetadata = new()
                    {
                        pixelBounds = pixelBounds,
                        fullResolution = camera.GetFilm().FullResolution()
                    };
                    rimg.Write("sppm_radius.png", rmetadata);
                }
            }
        }
        progress.Done();
        DisconnectFromDisplayServer();
    }

    private SampledSpectrum SampleLd(in SurfaceInteraction intr, in BSDF bsdf, ref SampledWavelengths lambda,
                                     Sampler sampler, LightSampler lightSampler)
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
        $"[ SPPMIntegrator camera: {camera} initialSearchRadius: {FloatToString(initialSearchRadius)} " +
        $"maxDepth: {maxDepth} photonsPerIteration: {photonsPerIteration} colorSpace: {colorSpace} " +
        "digitPermutations: (elided) ]";

    public static SPPMIntegrator Create(ParameterDictionary parameters, RGBColorSpace colorSpace, Camera camera,
                                        Sampler sampler, Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        int photonsPerIter = parameters.GetOneInt("photonsperiteration", -1);
        Float radius = parameters.GetOneFloat("radius", 1f);
        int seed = parameters.GetOneInt("seed", Options.seed);
        return new SPPMIntegrator(camera, sampler, aggregate, lights, photonsPerIter, maxDepth, radius, seed,
                                  colorSpace);
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the BDPT and MLT parts of src/pbrt/cpu/integrators.h and
// integrators.cpp
//
// pbrt's Vertex keeps its EndpointInteraction, MediumInteraction and
// SurfaceInteraction in a union; here Vertex has all three, and the code
// that reads the EndpointInteraction of a surface vertex (whose Interaction
// part is the SurfaceInteraction's in pbrt) uses GetInteraction(). The
// vertex pointers are indices into Vertex arrays, and MISWeight()'s
// ScopedAssignments are a backup of the vertices it changes that is
// restored at the end. The BDPT utility functions are in the Bdpt class.

namespace Pbrt;

// VertexType Definition
public enum VertexType { Camera, Light, Surface, Medium }

// EndpointInteraction Definition
public struct EndpointInteraction
{
    // (pbrt's Interaction base)
    public Interaction intr;
    // (pbrt's union of the two)
    public Camera? camera;
    public Light? light;

    // EndpointInteraction Public Methods
    public EndpointInteraction() { }

    public EndpointInteraction(in Interaction it, Camera camera)
    {
        intr = it;
        this.camera = camera;
    }

    public EndpointInteraction(Camera camera, in Ray ray)
    {
        intr = new Interaction(ray.o, ray.time, ray.medium);
        this.camera = camera;
    }

    public EndpointInteraction(Light light, in Interaction it)
    {
        intr = it;
        this.light = light;
    }

    public EndpointInteraction(Light light, in Ray r)
    {
        intr = new Interaction(r.o, r.time, r.medium);
        this.light = light;
    }

    public EndpointInteraction(in Ray ray)
    {
        intr = new Interaction(ray.At(1), new Normal3f(-ray.d), ray.time, ray.medium);
        light = null;
    }
}

// BDPT Vertex Definition
public struct Vertex
{
    // Vertex Public Members
    public VertexType type;
    public SampledSpectrum beta;
    public EndpointInteraction ei;
    public MediumInteraction mi;
    public SurfaceInteraction si;
    public BSDF bsdf;
    public bool delta = false;
    public Float pdfFwd = 0, pdfRev = 0;

    // Vertex Public Methods
    public Vertex() => ei = new EndpointInteraction();

    public Vertex(VertexType type, in EndpointInteraction ei, in SampledSpectrum beta)
    {
        this.type = type;
        this.beta = beta;
        this.ei = ei;
    }

    public Vertex(in SurfaceInteraction si, in BSDF bsdf, in SampledSpectrum beta)
    {
        type = VertexType.Surface;
        this.beta = beta;
        this.si = si;
        this.bsdf = bsdf;
    }

    public Vertex(in MediumInteraction mi, in SampledSpectrum beta)
    {
        type = VertexType.Medium;
        this.beta = beta;
        this.mi = mi;
    }

    public static Vertex CreateCamera(Camera camera, in Ray ray, in SampledSpectrum beta) =>
        new(VertexType.Camera, new EndpointInteraction(camera, ray), beta);

    public static Vertex CreateCamera(Camera camera, in Interaction it, in SampledSpectrum beta) =>
        new(VertexType.Camera, new EndpointInteraction(it, camera), beta);

    public static Vertex CreateLight(Light light, in Ray ray, in SampledSpectrum Le, Float pdf)
    {
        Vertex v = new(VertexType.Light, new EndpointInteraction(light, ray), Le);
        v.pdfFwd = pdf;
        return v;
    }

    public static Vertex CreateLight(Light light, in Interaction intr, in SampledSpectrum Le, Float pdf)
    {
        Vertex v = new(VertexType.Light, new EndpointInteraction(light, intr), Le);
        v.pdfFwd = pdf;
        return v;
    }

    public static Vertex CreateLight(in EndpointInteraction ei, in SampledSpectrum beta, Float pdf)
    {
        Vertex v = new(VertexType.Light, ei, beta);
        v.pdfFwd = pdf;
        return v;
    }

    public static Vertex CreateSurface(in SurfaceInteraction si, in BSDF bsdf, in SampledSpectrum beta, Float pdf,
                                       in Vertex prev)
    {
        Vertex v = new(si, bsdf, beta);
        v.pdfFwd = prev.ConvertDensity(pdf, v);
        return v;
    }

    public static Vertex CreateMedium(in MediumInteraction mi, in SampledSpectrum beta, Float pdf, in Vertex prev)
    {
        Vertex v = new(mi, beta);
        v.pdfFwd = prev.ConvertDensity(pdf, v);
        return v;
    }

    public readonly Interaction GetInteraction() => type switch
    {
        VertexType.Medium => mi,
        VertexType.Surface => si,
        _ => ei.intr
    };

    public readonly Point3f p() => GetInteraction().p();

    public readonly Float time() => GetInteraction().time;

    public readonly Normal3f ng() => GetInteraction().n;

    public readonly Normal3f ns() => type == VertexType.Surface ? si.shading.n : GetInteraction().n;

    public readonly bool IsOnSurface() => ng() != new Normal3f();

    public readonly SampledSpectrum f(in Vertex next, TransportMode mode)
    {
        Vector3f wi = next.p() - p();
        if (LengthSquared(wi) == 0)
            return new SampledSpectrum();
        wi = Normalize(wi);
        switch (type)
        {
            case VertexType.Surface:
                return bsdf.f(si.wo, wi, mode);
            case VertexType.Medium:
                return new SampledSpectrum(mi.phase!.p(mi.wo, wi));
            default:
                LOG_FATAL("Vertex::f(): Unimplemented");
                return new SampledSpectrum(0f);
        }
    }

    public readonly bool IsConnectible()
    {
        switch (type)
        {
            case VertexType.Medium:
                return true;
            case VertexType.Light:
                return ei.light!.Type() != LightType.DeltaDirection;
            case VertexType.Camera:
                return true;
            case VertexType.Surface:
                return IsNonSpecular(bsdf.Flags());
        }
        LOG_FATAL("Unhandled vertex type in IsConnectible()");
        return false;
    }

    public readonly bool IsLight() =>
        type == VertexType.Light || (type == VertexType.Surface && si.areaLight != null);

    public readonly bool IsDeltaLight() =>
        type == VertexType.Light && ei.light != null && Util.IsDeltaLight(ei.light.Type());

    public readonly bool IsInfiniteLight() =>
        type == VertexType.Light &&
        (ei.light == null || ei.light.Type() == LightType.Infinite || ei.light.Type() == LightType.DeltaDirection);

    public readonly SampledSpectrum Le(List<Light> infiniteLights, in Vertex v, in SampledWavelengths lambda)
    {
        if (!IsLight())
            return new SampledSpectrum(0f);
        Vector3f w = v.p() - p();
        if (LengthSquared(w) == 0)
            return new SampledSpectrum(0f);
        w = Normalize(w);
        if (IsInfiniteLight())
        {
            // Return emitted radiance for infinite light sources
            SampledSpectrum Le = new(0f);
            foreach (Light light in infiniteLights)
                Le += light.Le(new Ray(p(), -w), lambda);
            return Le;
        }
        // (pbrt reads si.areaLight through the union here; a non-infinite
        // light vertex doesn't get here.)
        else if (type == VertexType.Surface && si.areaLight != null)
            return si.areaLight.L(si.p(), si.n, si.uv, w, lambda);
        else
            return new SampledSpectrum(0f);
    }

    public override readonly string ToString()
    {
        string s = "[ Vertex type: " + type switch
        {
            VertexType.Camera => "camera",
            VertexType.Light => "light",
            VertexType.Surface => "surface",
            _ => "medium"
        };
        s += $" connectible: {(IsConnectible() ? "true" : "false")} p: {p()} ng: {ng()} " +
             $"pdfFwd: {FloatToString(pdfFwd)} pdfRev: {FloatToString(pdfRev)} beta: {beta}";
        if (type == VertexType.Surface)
            s += "\n  bsdf: " + bsdf;
        else if (type == VertexType.Medium)
            s += "\n  phase: " + mi.phase;
        return s + " ]";
    }

    public readonly Float ConvertDensity(Float pdf, in Vertex next)
    {
        // Return solid angle density if _next_ is an infinite area light
        if (next.IsInfiniteLight())
            return pdf;

        Vector3f w = next.p() - p();
        if (LengthSquared(w) == 0)
            return 0;
        Float invDist2 = 1 / LengthSquared(w);
        if (next.IsOnSurface())
            pdf *= AbsDot(next.ng(), w * FMath.Sqrt(invDist2));
        return pdf * invDist2;
    }

    // (prev is pbrt's pointer, which may be null.)
    public readonly Float PDF(Integrator integrator, in Vertex prev, bool hasPrev, in Vertex next)
    {
        if (type == VertexType.Light)
            return PDFLight(integrator, next);
        // Compute directions to preceding and next vertex
        Vector3f wn = next.p() - p();
        if (LengthSquared(wn) == 0)
            return 0;
        wn = Normalize(wn);
        Vector3f wp = new();
        if (hasPrev)
        {
            wp = prev.p() - p();
            if (LengthSquared(wp) == 0)
                return 0;
            wp = Normalize(wp);
        }
        else
            CHECK(type == VertexType.Camera);

        // Compute directional density depending on the vertex type
        Float pdf = 0;
        if (type == VertexType.Camera)
            ei.camera!.PDF_We(ei.intr.SpawnRay(wn), out _, out pdf);
        else if (type == VertexType.Surface)
            pdf = bsdf.PDF(wp, wn);
        else if (type == VertexType.Medium)
            pdf = mi.phase!.p(wp, wn);
        else
            LOG_FATAL("Vertex::PDF(): Unimplemented");

        // Return probability per unit area at vertex _next_
        return ConvertDensity(pdf, next);
    }

    public readonly Float PDFLight(Integrator integrator, in Vertex v)
    {
        Vector3f w = v.p() - p();
        Float invDist2 = 1 / LengthSquared(w);
        w *= FMath.Sqrt(invDist2);
        // Compute sampling density _pdf_ for light type
        Float pdf;
        if (IsInfiniteLight())
        {
            // Compute planar sampling density for infinite light sources
            Bounds3f sceneBounds = integrator.aggregate!.Bounds();
            sceneBounds.BoundingSphere(out _, out Float sceneRadius);
            pdf = 1 / (Pi * Sqr(sceneRadius));
        }
        else if (IsOnSurface())
        {
            // Compute sampling density at emissive surface
            if (type == VertexType.Light)
                CHECK(ei.light is DiffuseAreaLight); // since that's all we've
                                                     // got currently...
            Light light = (type == VertexType.Light) ? ei.light! : si.areaLight!;
            light.PDF_Le(GetInteraction(), w, out _, out Float pdfDir);
            pdf = pdfDir * invDist2;
        }
        else
        {
            // Compute sampling density for noninfinite light sources
            CHECK(type == VertexType.Light);
            CHECK(ei.light != null);
            ei.light!.PDF_Le(new Ray(p(), w, time()), out _, out Float pdfDir);
            pdf = pdfDir * invDist2;
        }

        if (v.IsOnSurface())
            pdf *= AbsDot(v.ng(), w);
        return pdf;
    }

    public readonly Float PDFLightOrigin(List<Light> infiniteLights, in Vertex v, LightSampler lightSampler)
    {
        Vector3f w = v.p() - p();
        if (LengthSquared(w) == 0)
            return 0f;
        w = Normalize(w);
        if (IsInfiniteLight())
            // Return sampling density for infinite light sources
            return Bdpt.InfiniteLightDensity(infiniteLights, lightSampler, w);
        else
        {
            // Return sampling density for noninfinite light source
            Light light = (type == VertexType.Light) ? ei.light! : si.areaLight!;
            Float pdfPos, pdfChoice = lightSampler.PMF(light);
            if (IsOnSurface())
                light.PDF_Le(GetInteraction(), w, out pdfPos, out _);
            else
                light.PDF_Le(new Ray(p(), w, time()), out pdfPos, out _);
            return pdfPos * pdfChoice;
        }
    }
}

// BDPT Utility Functions
public static class Bdpt
{
    public static int BufferIndex(int s, int t)
    {
        int above = s + t - 2;
        return s + above * (5 + above) / 2;
    }

    public static int GenerateCameraSubpath(Integrator integrator, in RayDifferential ray,
                                            ref SampledWavelengths lambda, Sampler sampler, int maxDepth,
                                            Camera camera, Vertex[] path, bool regularize)
    {
        if (maxDepth == 0)
            return 0;
        SampledSpectrum beta = new(1f);
        // Generate first vertex on camera subpath and start random walk
        path[0] = Vertex.CreateCamera(camera, ray, beta);
        camera.PDF_We(ray, out _, out Float pdfDir);
        return RandomWalk(integrator, ref lambda, ray, sampler, camera, beta, pdfDir, maxDepth - 1,
                          TransportMode.Radiance, path, 1, regularize) + 1;
    }

    public static int GenerateLightSubpath(Integrator integrator, ref SampledWavelengths lambda, Sampler sampler,
                                           Camera camera, int maxDepth, Float time, LightSampler lightSampler,
                                           Vertex[] path, bool regularize)
    {
        // Generate light subpath and initialize _path_ vertices
        if (maxDepth == 0)
            return 0;
        // Sample initial ray for light subpath
        // Sample light for BDPT light subpath
        SampledLight? sampledLight = lightSampler.Sample(sampler.Get1D());
        if (sampledLight is not SampledLight sl)
            return 0;
        Light light = sl.light;
        Float lightSamplePDF = sl.p;

        Point2f ul0 = sampler.Get2D();
        Point2f ul1 = sampler.Get2D();
        LightLeSample? leso = light.SampleLe(ul0, ul1, ref lambda, time);
        if (leso is not LightLeSample les || les.pdfPos == 0 || les.pdfDir == 0 || !les.L)
            return 0;
        RayDifferential ray = new(les.ray);

        // Generate first vertex of light subpath
        Float p_l = lightSamplePDF * les.pdfPos;
        path[0] = les.intr is Interaction li ? Vertex.CreateLight(light, li, les.L, p_l)
                                             : Vertex.CreateLight(light, ray, les.L, p_l);

        // Follow light subpath random walk
        SampledSpectrum beta = les.L * les.AbsCosTheta(ray.d) / (p_l * les.pdfDir);
        int nVertices = RandomWalk(integrator, ref lambda, ray, sampler, camera, beta, les.pdfDir, maxDepth - 1,
                                   TransportMode.Importance, path, 1, regularize);
        // Correct subpath sampling densities for infinite area lights
        if (path[0].IsInfiniteLight())
        {
            // Set spatial density of _path[1]_ for infinite area light
            if (nVertices > 0)
            {
                path[1].pdfFwd = les.pdfPos;
                if (path[1].IsOnSurface())
                    path[1].pdfFwd *= AbsDot(ray.d, path[1].ng());
            }

            // Set spatial density of _path[0]_ for infinite area light
            path[0].pdfFwd = InfiniteLightDensity(integrator.infiniteLights, lightSampler, ray.d);
        }

        return nVertices + 1;
    }

    // (pbrt's path pointer is path + start; path[start - 1] is the
    // preceding vertex.)
    public static int RandomWalk(Integrator integrator, ref SampledWavelengths lambda, RayDifferential ray,
                                 Sampler sampler, Camera camera, SampledSpectrum beta, Float pdf, int maxDepth,
                                 TransportMode mode, Vertex[] path, int start, bool regularize)
    {
        if (maxDepth == 0)
            return 0;
        // Follow random walk to initialize BDPT path vertices
        int bounces = 0;
        bool anyNonSpecularBounces = false;
        Float pdfFwd = pdf;
        while (true)
        {
            // Attempt to create the next subpath vertex in _path_
            if (!beta)
                break;
            bool scattered = false, terminated = false;
            // Trace a ray and sample the medium, if any
            int vertex = start + bounces, prev = start + bounces - 1;
            ShapeIntersection? si = integrator.Intersect(ray);
            if (ray.medium != null)
            {
                // Sample participating medium for _RandomWalk()_ ray
                Float tMax = si.HasValue ? si.Value.tHit : Infinity;
                RNG rng = new(Hash(ray.o, tMax), Hash(ray.d));
                Float u = sampler.Get1D();
                SampledSpectrum T_maj = SampleT_maj(ray, tMax, u, ref rng, lambda,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        // Compute medium event probabilities for interaction
                        Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                        Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                        Float pNull = FMath.Max(0, 1 - pAbsorb - pScatter);

                        // Randomly sample medium event for _RandomRalk()_ ray
                        Float um = sampler.Get1D();
                        int evt = SampleDiscrete([pAbsorb, pScatter, pNull], um);
                        if (evt == 0)
                        {
                            // Handle absorption for _RandomWalk()_ ray
                            terminated = true;
                            return false;
                        }
                        else if (evt == 1)
                        {
                            // Handle scattering for _RandomWalk()_ ray
                            beta *= T_maj * mp.sigma_s / (T_maj[0] * mp.sigma_s[0]);
                            // Record medium interaction in _path_ and compute forward density
                            MediumInteraction intr = new(p, -ray.d, ray.time, ray.medium, mp.phase);
                            path[vertex] = Vertex.CreateMedium(intr, beta, pdfFwd, path[prev]);
                            if (++bounces >= maxDepth)
                            {
                                terminated = true;
                                return false;
                            }

                            // Sample direction and compute reverse density at preceding
                            // vertex
                            PhaseFunctionSample? pso = intr.phase!.Sample_p(-ray.d, sampler.Get2D());
                            if (pso is not PhaseFunctionSample ps || ps.pdf == 0)
                            {
                                terminated = true;
                                return false;
                            }
                            // Update path state and previous path vertex after medium
                            // scattering
                            pdfFwd = ps.pdf;
                            beta *= ps.p / ps.pdf;
                            ray = ((Interaction)intr).SpawnRay(ps.wi);
                            anyNonSpecularBounces = true;
                            path[prev].pdfRev = path[vertex].ConvertDensity(ps.pdf, path[prev]);

                            scattered = true;
                            return false;
                        }
                        else
                        {
                            // Handle null scattering for _RandomWalk()_ ray
                            SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);
                            Float pdf = T_maj[0] * sigma_n[0];
                            if (pdf == 0)
                                beta = new SampledSpectrum(0f);
                            else
                                beta *= T_maj * sigma_n / pdf;
                            return beta.IsNonZero();
                        }
                    });
                // Update _beta_ for medium transmittance
                if (!scattered)
                    beta *= T_maj / T_maj[0];
            }

            if (terminated)
                return bounces;
            if (scattered)
                continue;
            // Handle escaped rays after no medium scattering event
            if (si is not ShapeIntersection s)
            {
                // Capture escaped rays when tracing from the camera
                if (mode == TransportMode.Radiance)
                {
                    path[vertex] = Vertex.CreateLight(new EndpointInteraction(ray), beta, pdfFwd);
                    ++bounces;
                }

                break;
            }

            // Handle surface interaction for path generation
            SurfaceInteraction isect = s.intr;
            // Get BSDF and skip over medium boundaries
            BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, sampler);
            if (!bsdf)
            {
                isect.SkipIntersection(ref ray, s.tHit);
                continue;
            }

            // Possibly regularize the BSDF
            if (regularize && anyNonSpecularBounces)
            {
                PathIntegrator.regularizedBSDFs.Num();
                bsdf.Regularize();
            }

            PathIntegrator.regularizedBSDFs.Denom();
            // Initialize _vertex_ with surface intersection information
            path[vertex] = Vertex.CreateSurface(isect, bsdf, beta, pdfFwd, path[prev]);

            if (++bounces >= maxDepth)
                break;
            // Sample BSDF at current vertex
            Vector3f wo = isect.wo;
            Float u2 = sampler.Get1D();
            BSDFSample? bso = bsdf.Sample_f(wo, u2, sampler.Get2D(), mode);
            if (bso is not BSDFSample bs)
                break;
            pdfFwd = bs.pdfIsProportional ? bsdf.PDF(wo, bs.wi, mode) : bs.pdf;
            anyNonSpecularBounces |= !bs.IsSpecular();
            beta *= bs.f * AbsDot(bs.wi, isect.shading.n) / bs.pdf;
            ray = isect.SpawnRay(ray, bsdf, bs.wi, bs.flags, bs.eta);

            // Compute path probabilities at surface vertex
            // TODO: confirm. I believe that !mode is right. Interestingly,
            // it makes no difference in the test suite either way.
            Float pdfRev = bsdf.PDF(bs.wi, wo, Flip(mode));
            if (bs.IsSpecular())
            {
                path[vertex].delta = true;
                pdfRev = pdfFwd = 0;
            }
            path[prev].pdfRev = path[vertex].ConvertDensity(pdfRev, path[prev]);
        }
        return bounces;
    }

    public static SampledSpectrum G(Integrator integrator, Sampler sampler, in Vertex v0, in Vertex v1,
                                    in SampledWavelengths lambda)
    {
        Vector3f d = v0.p() - v1.p();
        Float g = 1 / LengthSquared(d);
        d *= FMath.Sqrt(g);
        if (v0.IsOnSurface())
            g *= AbsDot(v0.ns(), d);
        if (v1.IsOnSurface())
            g *= AbsDot(v1.ns(), d);
        return g * integrator.Tr(v0.GetInteraction(), v1.GetInteraction(), lambda);
    }

    public static Float MISWeight(Integrator integrator, Camera camera, Vertex[] lightVertices,
                                  Vertex[] cameraVertices, in Vertex sampled, int s, int t,
                                  LightSampler lightSampler)
    {
        if (s + t == 2)
            return 1;
        Float sumRi = 0;
        // Define helper function _remap0_ that deals with Dirac delta functions
        static Float remap0(float f) => f != 0 ? f : 1;

        // Temporarily update vertex properties for current strategy
        // Look up connection vertices and their predecessors
        int qs = s > 0 ? s - 1 : -1, pt = t > 0 ? t - 1 : -1, qsMinus = s > 1 ? s - 2 : -1,
            ptMinus = t > 1 ? t - 2 : -1;
        // (Back up the vertices that are changed; they're restored at the
        // end, as pbrt's ScopedAssignments do.)
        Vertex qsBackup = qs >= 0 ? lightVertices[qs] : default, ptBackup = pt >= 0 ? cameraVertices[pt] : default;
        Vertex qsMinusBackup = qsMinus >= 0 ? lightVertices[qsMinus] : default;
        Vertex ptMinusBackup = ptMinus >= 0 ? cameraVertices[ptMinus] : default;

        // Update sampled vertex for $s=1$ or $t=1$ strategy
        if (s == 1)
            lightVertices[qs] = sampled;
        else if (t == 1)
            cameraVertices[pt] = sampled;

        // Mark connection vertices as non-degenerate
        if (pt >= 0)
            cameraVertices[pt].delta = false;
        if (qs >= 0)
            lightVertices[qs].delta = false;

        // Update reverse density of vertex $\pt{}_{t-1}$
        if (pt >= 0)
            cameraVertices[pt].pdfRev =
                s > 0 ? lightVertices[qs].PDF(integrator, qsMinus >= 0 ? lightVertices[qsMinus] : default,
                                              qsMinus >= 0, cameraVertices[pt])
                      : cameraVertices[pt].PDFLightOrigin(integrator.infiniteLights, cameraVertices[ptMinus],
                                                          lightSampler);

        // Update reverse density of vertex $\pt{}_{t-2}$
        if (ptMinus >= 0)
            cameraVertices[ptMinus].pdfRev =
                s > 0 ? cameraVertices[pt].PDF(integrator, qs >= 0 ? lightVertices[qs] : default, qs >= 0,
                                               cameraVertices[ptMinus])
                      : cameraVertices[pt].PDFLight(integrator, cameraVertices[ptMinus]);

        // Update reverse density of vertices $\pq{}_{s-1}$ and $\pq{}_{s-2}$
        if (qs >= 0)
            lightVertices[qs].pdfRev = cameraVertices[pt].PDF(integrator,
                                                              ptMinus >= 0 ? cameraVertices[ptMinus] : default,
                                                              ptMinus >= 0, lightVertices[qs]);
        if (qsMinus >= 0)
            lightVertices[qsMinus].pdfRev = lightVertices[qs].PDF(integrator, pt >= 0 ? cameraVertices[pt] : default,
                                                                  pt >= 0, lightVertices[qsMinus]);

        Film film = camera.GetFilm();
        Float splatScale = (Float)film.FullResolution().x * (Float)film.FullResolution().y /
                           (Float)film.PixelBounds().Area();

        // Consider hypothetical connection strategies along the camera subpath
        Float ri = 1;
        for (int i = t - 1; i > 0; --i)
        {
            ri *= remap0((float)cameraVertices[i].pdfRev) / remap0((float)cameraVertices[i].pdfFwd);
            // See https://github.com/mmp/pbrt-v4/issues/347
            if (i == 1)
                ri /= splatScale;
            if (!cameraVertices[i].delta && !cameraVertices[i - 1].delta)
                sumRi += ri;
        }

        // Consider hypothetical connection strategies along the light subpath
        ri = 1;
        for (int i = s - 1; i >= 0; --i)
        {
            ri *= remap0((float)lightVertices[i].pdfRev) / remap0((float)lightVertices[i].pdfFwd);
            bool deltaLightvertex = i > 0 ? lightVertices[i - 1].delta : lightVertices[0].IsDeltaLight();
            if (!lightVertices[i].delta && !deltaLightvertex)
                sumRi += ri;
        }

        // See https://github.com/mmp/pbrt-v4/issues/347
        if (t == 1)
            sumRi /= splatScale;

        // (Undo the temporary updates.)
        if (qs >= 0)
            lightVertices[qs] = qsBackup;
        if (pt >= 0)
            cameraVertices[pt] = ptBackup;
        if (qsMinus >= 0)
            lightVertices[qsMinus] = qsMinusBackup;
        if (ptMinus >= 0)
            cameraVertices[ptMinus] = ptMinusBackup;

        return 1 / (1 + sumRi);
    }

    public static Float InfiniteLightDensity(List<Light> infiniteLights, LightSampler lightSampler, Vector3f w)
    {
        Float pdf = 0;
        foreach (Light light in infiniteLights)
            pdf += light.PDF_Li(new LightSampleContext(new Interaction()), -w) * lightSampler.PMF(light);
        return pdf;
    }

    public static SampledSpectrum ConnectBDPT(Integrator integrator, ref SampledWavelengths lambda,
                                              Vertex[] lightVertices, Vertex[] cameraVertices, int s, int t,
                                              LightSampler lightSampler, Camera camera, Sampler sampler,
                                              out Point2f? pRaster, out Float misWeightOut)
    {
        pRaster = null;
        SampledSpectrum L = new(0f);
        // Ignore invalid connections related to infinite area lights
        if (t > 1 && s != 0 && cameraVertices[t - 1].type == VertexType.Light)
        {
            misWeightOut = 0;
            return new SampledSpectrum(0f);
        }

        // Perform connection and write contribution to _L_
        Vertex sampled = new();
        if (s == 0)
        {
            // Interpret the camera subpath as a complete path
            ref readonly Vertex pt = ref cameraVertices[t - 1];
            if (pt.IsLight())
                L = pt.Le(integrator.infiniteLights, cameraVertices[t - 2], lambda) * pt.beta;
            DCHECK(!L.HasNaNs());
        }
        else if (t == 1)
        {
            // Sample a point on the camera and connect it to the light subpath
            ref readonly Vertex qs = ref lightVertices[s - 1];
            if (qs.IsConnectible())
            {
                CameraWiSample? cso = camera.SampleWi(qs.GetInteraction(), sampler.Get2D(), ref lambda);
                if (cso is CameraWiSample cs)
                {
                    pRaster = cs.pRaster;
                    // Initialize dynamically sampled vertex and _L_ for $t=1$ case
                    sampled = Vertex.CreateCamera(camera, cs.pLens, cs.Wi / cs.pdf);
                    L = qs.beta * qs.f(sampled, TransportMode.Importance) * sampled.beta;
                    if (qs.IsOnSurface())
                        L *= AbsDot(cs.wi, qs.ns());
                    DCHECK(!L.HasNaNs());
                    if (L)
                    {
                        L *= integrator.Tr(cs.pRef, cs.pLens, lambda);

                        // See https://github.com/mmp/pbrt-v4/issues/347
                        Film film = camera.GetFilm();
                        L *= (Float)film.FullResolution().x * (Float)film.FullResolution().y /
                             (Float)film.PixelBounds().Area();
                    }
                }
            }
        }
        else if (s == 1)
        {
            // Sample a point on a light and connect it to the camera subpath
            ref readonly Vertex pt = ref cameraVertices[t - 1];
            if (pt.IsConnectible())
            {
                SampledLight? sampledLight = lightSampler.Sample(sampler.Get1D());

                if (sampledLight is SampledLight sl)
                {
                    Light light = sl.light;
                    Float p_l = sl.p;

                    LightSampleContext ctx;
                    if (pt.IsOnSurface())
                    {
                        // (pbrt's AsSurface() of the interaction; a vertex on a
                        // surface is a surface vertex here.)
                        ref readonly SurfaceInteraction si = ref pt.si;
                        ctx = new LightSampleContext(si);
                        // Try to nudge the light sampling position to correct side of the
                        // surface
                        BxDFFlags flags = pt.bsdf.Flags();
                        if (IsReflective(flags) && !IsTransmissive(flags))
                            ctx.pi = new Point3fi(si.OffsetRayOrigin(si.wo));
                        else if (IsTransmissive(flags) && !IsReflective(flags))
                            ctx.pi = new Point3fi(si.OffsetRayOrigin(-si.wo));
                    }
                    else
                        ctx = new LightSampleContext(pt.GetInteraction());
                    LightLiSample? lightWeight = light.SampleLi(ctx, sampler.Get2D(), lambda);
                    if (lightWeight is LightLiSample lw && lw.L.IsNonZero() && lw.pdf > 0)
                    {
                        EndpointInteraction ei = new(light, lw.pLight);
                        sampled = Vertex.CreateLight(ei, lw.L / (lw.pdf * p_l), 0);
                        sampled.pdfFwd = sampled.PDFLightOrigin(integrator.infiniteLights, pt, lightSampler);
                        L = pt.beta * pt.f(sampled, TransportMode.Radiance) * sampled.beta;
                        if (pt.IsOnSurface())
                            L *= AbsDot(lw.wi, pt.ns());
                        // Only check visibility if the path would carry radiance.
                        if (L)
                            L *= integrator.Tr(pt.GetInteraction(), lw.pLight, lambda);
                    }
                }
            }
        }
        else
        {
            // Handle all other bidirectional connection cases
            ref readonly Vertex qs = ref lightVertices[s - 1];
            ref readonly Vertex pt = ref cameraVertices[t - 1];
            if (qs.IsConnectible() && pt.IsConnectible())
            {
                L = qs.beta * qs.f(pt, TransportMode.Importance) * pt.f(qs, TransportMode.Radiance) * pt.beta;
                if (L)
                    L *= G(integrator, sampler, qs, pt, lambda);
            }
        }

        PathIntegrator.zeroRadiancePaths.Denom();
        if (!L)
            PathIntegrator.zeroRadiancePaths.Num();
        PathIntegrator.pathLength.Add(s + t - 2);
        // Compute MIS weight for connection strategy
        Float misWeight = L ? MISWeight(integrator, camera, lightVertices, cameraVertices, sampled, s, t,
                                        lightSampler)
                            : 0f;
        DCHECK(!IsNaN(misWeight));
        L *= misWeight;
        misWeightOut = misWeight;

        return L;
    }
}

// BDPTIntegrator Definition
public sealed class BDPTIntegrator : RayIntegrator
{
    // BDPTIntegrator Private Members
    private readonly int maxDepth;
    private readonly bool regularize;
    private readonly LightSampler lightSampler;
    private readonly bool visualizeStrategies, visualizeWeights;
    private Film?[] weightFilms = [];

    // BDPTIntegrator Public Methods
    // (The light sampler is created in the constructor's body, after the
    // base constructor preprocesses the lights, as in pbrt; a field
    // initializer would run before it.)
    public BDPTIntegrator(Camera camera, Sampler sampler, Primitive? aggregate, List<Light> lights, int maxDepth,
                          bool visualizeStrategies, bool visualizeWeights, bool regularize = false)
        : base(camera, sampler, aggregate, lights)
    {
        this.maxDepth = maxDepth;
        this.regularize = regularize;
        lightSampler = new PowerLightSampler(lights.ToArray());
        this.visualizeStrategies = visualizeStrategies;
        this.visualizeWeights = visualizeWeights;
    }

    // BDPT Method Definitions
    public override void Render()
    {
        // Allocate buffers for debug visualization
        if (visualizeStrategies || visualizeWeights)
        {
            int bufferCount = (1 + maxDepth) * (6 + maxDepth) / 2;
            weightFilms = new Film?[bufferCount];
            for (int depth = 0; depth <= maxDepth; ++depth)
            {
                for (int s = 0; s <= depth + 2; ++s)
                {
                    int t = depth + 2 - s;
                    if (t == 0 || (s == 1 && t == 1))
                        continue;

                    string filename = $"bdpt_d{depth:D2}_s{s:D2}_t{t:D2}.exr";

                    FilmBaseParameters p = new(camera.GetFilm().FullResolution(),
                                               new Bounds2i(new Point2i(0, 0), camera.GetFilm().FullResolution()),
                                               new BoxFilter(), camera.GetFilm().Diagonal() * 1000,
                                               PixelSensor.CreateDefault(), filename);
                    weightFilms[Bdpt.BufferIndex(s, t)] = new RGBFilm(p, RGBColorSpace.sRGB);
                }
            }
        }

        base.Render();

        // Write buffers for debug visualization
        if (visualizeStrategies || visualizeWeights)
        {
            Float invSampleCount = 1.0f / samplerPrototype.SamplesPerPixel();
            for (int i = 0; i < weightFilms.Length; ++i)
            {
                ImageMetadata metadata = new();
                weightFilms[i]?.WriteImage(metadata, invSampleCount);
            }
            weightFilms = [];
        }
    }

    public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                       bool initializeVisibleSurface, ref VisibleSurface visibleSurface)
    {
        // Trace the camera and light subpaths
        Vertex[] cameraVertices = new Vertex[maxDepth + 2];
        int nCamera = Bdpt.GenerateCameraSubpath(this, ray, ref lambda, sampler, maxDepth + 2, camera,
                                                 cameraVertices, regularize);
        Vertex[] lightVertices = new Vertex[maxDepth + 1];
        int nLight = Bdpt.GenerateLightSubpath(this, ref lambda, sampler, camera, maxDepth + 1,
                                               cameraVertices[0].time(), lightSampler, lightVertices, regularize);

        SampledSpectrum L = new(0f);
        // Execute all BDPT connection strategies
        for (int t = 1; t <= nCamera; ++t)
        {
            for (int s = 0; s <= nLight; ++s)
            {
                int depth = t + s - 2;
                if ((s == 1 && t == 1) || depth < 0 || depth > maxDepth)
                    continue;
                // Execute the $(s, t)$ connection strategy and update _L_
                SampledSpectrum Lpath = Bdpt.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t,
                                                         lightSampler, camera, sampler, out Point2f? pFilmNew,
                                                         out Float misWeight);
                if (Lpath.IsNonZero() && (visualizeStrategies || visualizeWeights))
                {
                    SampledSpectrum value = new();
                    if (visualizeStrategies)
                        value = misWeight == 0 ? new SampledSpectrum(0f) : Lpath / misWeight;
                    if (visualizeWeights)
                        value = Lpath;
                    if (pFilmNew is Point2f pf)
                        weightFilms[Bdpt.BufferIndex(s, t)]!.AddSplat(pf, value, lambda);
                    else
                    {
                        // Unfortunately we no longer have the pixel
                        // coordinates of the sample easily available, so we
                        // need to go back to the camera and ask for them; here
                        // we take a point a little bit along the camera ray
                        // and ask the camera to reproject that for us.
                        //
                        // Double unfortunately, this doesn't quite work for
                        // scenes where the camera has a finite aperture, since
                        // we don't have the CameraSample either so just have
                        // to pass (0.5,0.5) in for the lens sample...
                        CameraWiSample? cs = camera.SampleWi(new Interaction(ray.At(100f), (MediumInterface?)null),
                                                             new Point2f(0.5f, 0.5f), ref lambda);
                        if (cs.HasValue)
                            weightFilms[Bdpt.BufferIndex(s, t)]!.AddSplat(cs.Value.pRaster, value, lambda);
                    }
                }
                if (t != 1)
                    L += Lpath;
                else if (Lpath)
                {
                    CHECK(pFilmNew.HasValue);
                    camera.GetFilm().AddSplat(pFilmNew!.Value, Lpath, lambda);
                }
            }
        }

        return L;
    }

    public override string ToString() =>
        $"[ BDPTIntegrator maxDepth: {maxDepth} visualizeStrategies: {(visualizeStrategies ? "true" : "false")} " +
        $"visualizeWeights: {(visualizeWeights ? "true" : "false")} regularize: {(regularize ? "true" : "false")} " +
        $"lightSampler: {lightSampler} ]";

    public static BDPTIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                        Primitive? aggregate, List<Light> lights, FileLoc? loc)
    {
        if (camera is not PerspectiveCamera)
            ErrorExit("Only the \"perspective\" camera is currently supported with the \"bdpt\" integrator.");
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        bool visualizeStrategies = parameters.GetOneBool("visualizestrategies", false);
        bool visualizeWeights = parameters.GetOneBool("visualizeweights", false);

        if ((visualizeStrategies || visualizeWeights) && maxDepth > 5)
        {
            Warning(loc, "visualizestrategies/visualizeweights was enabled, limiting maxdepth to 5");
            maxDepth = 5;
        }

        bool regularize = parameters.GetOneBool("regularize", false);
        return new BDPTIntegrator(camera, sampler, aggregate, lights, maxDepth, visualizeStrategies,
                                  visualizeWeights, regularize);
    }
}

// MLTIntegrator Definition
public sealed class MLTIntegrator : Integrator
{
    private static readonly StatPercent acceptedMutations = new("Integrator/Acceptance rate");

    // MLTIntegrator Constants
    private const int cameraStreamIndex = 0;
    private const int lightStreamIndex = 1;
    private const int connectionStreamIndex = 2;
    private const int nSampleStreams = 3;

    // MLTIntegrator Private Members
    private readonly Camera camera;
    private readonly bool regularize;
    private readonly LightSampler lightSampler;
    private readonly int maxDepth, nBootstrap;
    private readonly int mutationsPerPixel;
    private readonly Float sigma, largeStepProbability;
    private readonly int nChains;

    // MLTIntegrator Public Methods
    public MLTIntegrator(Camera camera, Primitive? aggregate, List<Light> lights, int maxDepth, int nBootstrap,
                         int nChains, int mutationsPerPixel, Float sigma, Float largeStepProbability,
                         bool regularize)
        : base(aggregate, lights)
    {
        lightSampler = new PowerLightSampler(lights.ToArray());
        this.camera = camera;
        this.maxDepth = maxDepth;
        this.nBootstrap = nBootstrap;
        this.nChains = nChains;
        this.mutationsPerPixel = mutationsPerPixel;
        this.sigma = sigma;
        this.largeStepProbability = largeStepProbability;
        this.regularize = regularize;
    }

    // (pbrt's thread_local variables in Render())
    [ThreadStatic] private static MLTSampler? threadSampler;
    [ThreadStatic] private static int threadDepth;

    // MLTIntegrator Private Methods
    private static Float c(in SampledSpectrum L, in SampledWavelengths lambda) => L.y(lambda);

    // MLTIntegrator Method Definitions
    private SampledSpectrum L(MLTSampler sampler, int depth, out Point2f pRaster, out SampledWavelengths lambda)
    {
        pRaster = new Point2f();
        lambda = new SampledWavelengths();
        if (lights.Count == 0)
            return new SampledSpectrum(0f);
        sampler.StartStream(cameraStreamIndex);
        // Determine the number of available strategies and pick a specific one
        int s, t, nStrategies;
        if (depth == 0)
        {
            nStrategies = 1;
            s = 0;
            t = 2;
        }
        else
        {
            nStrategies = depth + 2;
            s = Math.Min((int)(sampler.Get1D() * nStrategies), nStrategies - 1);
            t = nStrategies - s;
        }

        // Sample wavelengths for MLT path
        if (Options.disableWavelengthJitter)
            lambda = camera.GetFilm().SampleWavelengths(0.5f);
        else
            lambda = camera.GetFilm().SampleWavelengths(sampler.Get1D());

        // Generate a camera subpath with exactly _t_ vertices
        Vertex[] cameraVertices = new Vertex[t];
        // Compute camera sample for MLT camera path
        Bounds2f sampleBounds = camera.GetFilm().SampleBounds();
        pRaster = sampleBounds.Lerp(sampler.GetPixel2D());
        CameraSample cameraSample = new() { pFilm = pRaster };
        cameraSample.time = sampler.Get1D();
        cameraSample.pLens = sampler.Get2D();

        // Generate camera ray for MLT camera path
        CameraRayDifferential? crdo = camera.GenerateRayDifferential(cameraSample, ref lambda);
        if (crdo is not CameraRayDifferential crd || !crd.weight)
            return new SampledSpectrum(0f);
        Float rayDiffScale = FMath.Max(.125f, 1 / FMath.Sqrt((Float)sampler.SamplesPerPixel()));
        crd.ray.ScaleDifferentials(rayDiffScale);

        if (Bdpt.GenerateCameraSubpath(this, crd.ray, ref lambda, sampler, t, camera, cameraVertices,
                                       regularize) != t)
            return new SampledSpectrum(0f);

        // Generate a light subpath with exactly _s_ vertices
        sampler.StartStream(lightStreamIndex);
        Vertex[] lightVertices = new Vertex[s];
        if (Bdpt.GenerateLightSubpath(this, ref lambda, sampler, camera, s, cameraVertices[0].time(), lightSampler,
                                      lightVertices, regularize) != s)
            return new SampledSpectrum(0f);

        // Execute connection strategy and return the radiance estimate
        sampler.StartStream(connectionStreamIndex);
        SampledSpectrum L = Bdpt.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t, lightSampler,
                                             camera, sampler, out Point2f? pRasterNew, out _) * nStrategies;
        if (pRasterNew is Point2f pr)
            pRaster = pr;
        return L;
    }

    public override void Render()
    {
        // Handle statistics and debugstart for MLTIntegrator
        if (Options.recordPixelStatistics)
            Stats.StatsEnablePixelStats(camera.GetFilm().PixelBounds(),
                                        RemoveExtension(camera.GetFilm().GetFilename()));

        if (Options.debugStart.Length > 0)
        {
            List<string> c = SplitString(Options.debugStart, ',');
            if (c.Count == 0)
                ErrorExit($"Didn't find comma-separated values after --debugstart: {Options.debugStart}");

            if (!Atoi(c[0], out int depth))
                ErrorExit($"Unable to decode first --debugstart value: {c[0]}");

            DebugMLTSampler debugSampler = DebugMLTSampler.Create(c.GetRange(1, c.Count - 1), nSampleStreams);

            _ = L(debugSampler, depth, out _, out _);
            return;
        }

        using CheckCallbackScope checkCallback = new(() =>
            $"Rendering failed. Debug with --debugstart {threadDepth},{threadSampler?.DumpState()}\"\n");

        // Generate bootstrap samples and compute normalization constant $b$
        Timer timer = new();
        int nBootstrapSamples = nBootstrap * (maxDepth + 1);
        Float[] bootstrapWeights = new Float[nBootstrapSamples];

        // Generate bootstrap samples in parallel
        ProgressReporter progress = new(nBootstrap, "Generating bootstrap paths", Options.quiet);
        ParallelFor(0, nBootstrap, (long start, long end) =>
        {
            for (long i = start; i < end; ++i)
            {
                // Generate _i_th bootstrap sample
                for (int depth = 0; depth <= maxDepth; ++depth)
                {
                    int rngIndex = (int)(i * (maxDepth + 1) + depth);
                    MLTSampler sampler = new(mutationsPerPixel, rngIndex, sigma, largeStepProbability,
                                             nSampleStreams);
                    threadSampler = sampler;
                    threadDepth = depth;
                    // Evaluate path radiance using _sampler_ and update _bootstrapWeights_
                    SampledSpectrum L_i = L(sampler, depth, out _, out SampledWavelengths lambda);
                    bootstrapWeights[rngIndex] = c(L_i, lambda);
                }
            }
            progress.Update(end - start);
        });
        progress.Done();

        // (pbrt's std::accumulate() with a double initial value.)
        double sumWeights = 0;
        foreach (Float w in bootstrapWeights)
            sumWeights += w;
        if (sumWeights == 0.0)
            ErrorExit("No light carrying paths found during bootstrap sampling! " +
                      "Are you trying to render a black image?");
        AliasTable bootstrapTable = new(bootstrapWeights);
        Float b = (Float)((Float)(maxDepth + 1) / (Float)bootstrapWeights.Length * sumWeights);

        // Set up connection to display server, if enabled
        int finishedChains = 0;
        if (Options.displayServer.Length > 0)
        {
            DisplayDynamic(camera.GetFilm().GetFilename(),
                           new Point2i(camera.GetFilm().PixelBounds().Diagonal()), ["R", "G", "B"],
                           (Bounds2i bounds, float[][] displayValue) =>
                           {
                               Film film = camera.GetFilm();
                               Bounds2i pixelBounds = film.PixelBounds();
                               int index = 0;
                               foreach (Point2i p in bounds)
                               {
                                   Float finishedPixelMutations =
                                       (Float)Volatile.Read(ref finishedChains) / (Float)nChains * mutationsPerPixel;
                                   Float scale = b / FMath.Max(1, finishedPixelMutations);
                                   RGB rgb = film.GetPixelRGB(pixelBounds.pMin + p, scale);
                                   for (int c = 0; c < 3; ++c)
                                       displayValue[c][index] = (float)rgb[c];
                                   ++index;
                               }
                           });
        }

        // Follow _nChains_ Markov chains to render image
        Film film = camera.GetFilm();
        long nTotalMutations = (long)film.SampleBounds().Area() * mutationsPerPixel;
        ProgressReporter progressRender = new(nChains, "Rendering", Options.quiet);
        // Run _nChains_ Markov chains in parallel
        ParallelFor(0, nChains, (long ii) =>
        {
            int i = (int)ii;
            // Compute number of mutations to apply in current Markov chain
            long nChainMutations = Math.Min((i + 1) * nTotalMutations / nChains, nTotalMutations) -
                                   i * nTotalMutations / nChains;

            // Select initial state from the set of bootstrap samples
            RNG rng = new((ulong)i);
            int bootstrapIndex = bootstrapTable.Sample(rng.Uniform<Float>());
            int depth = bootstrapIndex % (maxDepth + 1);
            threadDepth = depth;

            // Initialize local variables for selected state
            MLTSampler sampler = new(mutationsPerPixel, bootstrapIndex, sigma, largeStepProbability,
                                     nSampleStreams);
            threadSampler = sampler;
            SampledSpectrum LCurrent = L(sampler, depth, out Point2f pCurrent, out SampledWavelengths lambdaCurrent);

            // Run the Markov chain for _nChainMutations_ steps
            for (long j = 0; j < nChainMutations; ++j)
            {
                Stats.StatsReportPixelStart(new Point2i(pCurrent));
                sampler.StartIteration();
                // Generate proposed sample and compute its radiance
                SampledSpectrum LProposed = L(sampler, depth, out Point2f pProposed,
                                              out SampledWavelengths lambdaProposed);

                // Compute acceptance probability for proposed sample
                Float cProposed = c(LProposed, lambdaProposed);
                Float cCurrent = c(LCurrent, lambdaCurrent);
                Float accept = FMath.Min(1, cProposed / cCurrent);

                // Splat both current and proposed samples to _film_
                if (accept > 0)
                    film.AddSplat(pProposed, LProposed * accept / cProposed, lambdaProposed);
                film.AddSplat(pCurrent, LCurrent * (1 - accept) / cCurrent, lambdaCurrent);

                // Accept or reject the proposal
                if (rng.Uniform<Float>() < accept)
                {
                    Stats.StatsReportPixelEnd(new Point2i(pCurrent));
                    Stats.StatsReportPixelStart(new Point2i(pProposed));
                    pCurrent = pProposed;
                    LCurrent = LProposed;
                    lambdaCurrent = lambdaProposed;
                    sampler.Accept();
                    acceptedMutations.Num();
                }
                else
                    sampler.Reject();

                acceptedMutations.Denom();
                Stats.StatsReportPixelEnd(new Point2i(pCurrent));
            }

            Interlocked.Increment(ref finishedChains);
            progressRender.Update(1);
        });

        progressRender.Done();

        // Store final image computed with MLT
        ImageMetadata metadata = new() { renderTimeSeconds = (float)timer.ElapsedSeconds() };
        camera.InitMetadata(metadata);
        camera.GetFilm().WriteImage(metadata, b / mutationsPerPixel);
        DisconnectFromDisplayServer();
    }

    public override string ToString() =>
        $"[ MLTIntegrator camera: {camera} maxDepth: {maxDepth} nBootstrap: {nBootstrap} nChains: {nChains} " +
        $"mutationsPerPixel: {mutationsPerPixel} sigma: {FloatToString(sigma)} " +
        $"largeStepProbability: {FloatToString(largeStepProbability)} lightSampler: {lightSampler} " +
        $"regularize: {(regularize ? "true" : "false")} ]";

    public static MLTIntegrator Create(ParameterDictionary parameters, Camera camera, Primitive? aggregate,
                                       List<Light> lights, FileLoc? loc)
    {
        if (camera is not PerspectiveCamera)
            ErrorExit("Only the \"perspective\" camera is currently supported with the \"mlt\" integrator.");
        int maxDepth = parameters.GetOneInt("maxdepth", 5);
        int nBootstrap = parameters.GetOneInt("bootstrapsamples", 100000);
        long nChains = parameters.GetOneInt("chains", 1000);
        int mutationsPerPixel = parameters.GetOneInt("mutationsperpixel", 100);
        if (Options.pixelSamples is int ps)
            mutationsPerPixel = ps;
        Float largeStepProbability = parameters.GetOneFloat("largestepprobability", 0.3f);
        Float sigma = parameters.GetOneFloat("sigma", .01f);
        if (Options.quickRender)
        {
            mutationsPerPixel = Math.Max(1, mutationsPerPixel / 16);
            nBootstrap = Math.Max(1, nBootstrap / 16);
        }
        bool regularize = parameters.GetOneBool("regularize", false);
        return new MLTIntegrator(camera, aggregate, lights, maxDepth, nBootstrap, (int)nChains, mutationsPerPixel,
                                 sigma, largeStepProbability, regularize);
    }
}

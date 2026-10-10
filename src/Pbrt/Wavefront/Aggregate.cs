// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/wavefront/aggregate.h, aggregate.cpp and intersect.h
//
// The OptiX aggregate isn't ported; CPUAggregate uses the CPU renderer's
// aggregate. The maxRays arguments (which size pbrt's GPU kernel launches)
// are dropped.

namespace Pbrt;

// WavefrontAggregate Definition
public abstract class WavefrontAggregate
{
    // WavefrontAggregate Interface
    public abstract Bounds3f Bounds();

    public abstract void IntersectClosest(RayQueue rayQ, EscapedRayQueue? escapedRayQ, HitAreaLightQueue hitAreaLightQ,
                                          MaterialEvalQueue basicMtlQ, MaterialEvalQueue universalMtlQ,
                                          MediumSampleQueue? mediumSampleQ, RayQueue nextRayQ);

    public abstract void IntersectShadow(ShadowRayQueue shadowRayQueue, PixelSampleState pixelSampleState);

    public abstract void IntersectShadowTr(ShadowRayQueue shadowRayQueue, PixelSampleState pixelSampleState);

    public abstract void IntersectOneRandom(SubsurfaceScatterQueue subsurfaceScatterQueue);
}

public sealed class CPUAggregate : WavefrontAggregate
{
    private readonly Primitive? aggregate;

    public CPUAggregate(BasicScene scene, NamedTextures textures,
                        Dictionary<int, List<Light>> shapeIndexToAreaLights, Dictionary<string, Medium> media,
                        Dictionary<string, Material?> namedMaterials, List<Material?> materials)
    {
        aggregate = scene.CreateAggregate(textures, shapeIndexToAreaLights, media, namedMaterials, materials);
    }

    public override Bounds3f Bounds() => aggregate != null ? aggregate.Bounds() : new Bounds3f();

    // CPUAggregate Method Definitions
    public override void IntersectClosest(RayQueue rayQueue, EscapedRayQueue? escapedRayQueue,
                                          HitAreaLightQueue hitAreaLightQueue,
                                          MaterialEvalQueue basicEvalMaterialQueue,
                                          MaterialEvalQueue universalEvalMaterialQueue,
                                          MediumSampleQueue? mediumSampleQueue, RayQueue nextRayQueue)
    {
        // _CPUAggregate::IntersectClosest()_ method implementation
        ParallelFor(0, rayQueue.Size(), (long index) =>
        {
            ref readonly RayWorkItem r = ref rayQueue[(int)index];
            // Intersect _r_'s ray with the scene and enqueue resulting work
            if (aggregate == null)
            {
                EnqueueWorkAfterMiss(r, mediumSampleQueue, escapedRayQueue);
                return;
            }
            ShapeIntersection? si = aggregate.Intersect(r.ray);
            if (si is not ShapeIntersection s)
                EnqueueWorkAfterMiss(r, mediumSampleQueue, escapedRayQueue);
            else
                // FIXME? Second arg r.ray.medium doesn't match OptiX path
                EnqueueWorkAfterIntersection(r, r.ray.medium, (float)s.tHit, s.intr, mediumSampleQueue, nextRayQueue,
                                             hitAreaLightQueue, basicEvalMaterialQueue, universalEvalMaterialQueue);
        });
    }

    public override void IntersectShadow(ShadowRayQueue shadowRayQueue, PixelSampleState pixelSampleState)
    {
        // Intersect shadow rays from _shadowRayQueue_ in parallel
        ParallelFor(0, shadowRayQueue.Size(), (long index) =>
        {
            ref readonly ShadowRayWorkItem w = ref shadowRayQueue[(int)index];
            bool hit = aggregate != null && aggregate.IntersectP(w.ray, w.tMax);
            RecordShadowRayResult(w, pixelSampleState, hit);
        });
    }

    public override void IntersectShadowTr(ShadowRayQueue shadowRayQueue, PixelSampleState pixelSampleState)
    {
        ParallelFor(0, shadowRayQueue.Size(), (long index) =>
        {
            ShapeIntersection? si = null;
            TraceTransmittance(shadowRayQueue[(int)index], pixelSampleState,
                               (Ray ray, Float tMax) =>
                               {
                                   si = aggregate?.Intersect(ray, tMax);

                                   if (si is not ShapeIntersection s)
                                       return new TransmittanceTraceResult(false, new Point3f(), null);
                                   else
                                       return new TransmittanceTraceResult(true, s.intr.p(), s.intr.material);
                               },
                               (Point3f p) => si!.Value.intr.SpawnRayTo(p));
        });
    }

    public override void IntersectOneRandom(SubsurfaceScatterQueue subsurfaceScatterQueue)
    {
        ParallelFor(0, subsurfaceScatterQueue.Size(), (long index) =>
        {
            ref SubsurfaceScatterWorkItem w = ref subsurfaceScatterQueue[(int)index];
            ulong seed = Hash(w.p0, w.p1);

            WeightedReservoirSampler<SubsurfaceInteraction> wrs = new(seed);
            Interaction @base = new(w.p0, 0f /* FIXME time */, (Medium?)null);
            while (true)
            {
                Ray r = @base.SpawnRayTo(w.p1);
                if (r.d == new Vector3f(0, 0, 0))
                    break;
                ShapeIntersection? si = aggregate?.Intersect(r, 1);
                if (si is not ShapeIntersection s)
                    break;
                @base = s.intr;
                if (s.intr.material == w.material)
                    wrs.Add(new SubsurfaceInteraction(s.intr), 1f);
            }

            if (wrs.HasSample())
            {
                w.reservoirPDF = wrs.SampleProbability();
                w.ssi = wrs.GetSample();
            }
            else
                w.reservoirPDF = 0;
        });
    }

    public override string ToString() => $"[ CPUAggregate aggregate: {aggregate} ]";
}

// (pbrt's TransmittanceTraceResult; material is null if the hit surface
// is a medium interface.)
public readonly record struct TransmittanceTraceResult(bool hit, Point3f pHit, Material? material);

public static partial class Util
{
    // Wavefront Ray Intersection Enqueuing Functions
    public static void EnqueueWorkAfterMiss(in RayWorkItem r, MediumSampleQueue? mediumSampleQueue,
                                            EscapedRayQueue? escapedRayQueue)
    {
        if (r.ray.medium != null)
            mediumSampleQueue!.Push(r, Infinity);
        else
            escapedRayQueue?.Push(r);
    }

    public static void RecordShadowRayResult(in ShadowRayWorkItem w, PixelSampleState pixelSampleState,
                                             bool foundIntersection)
    {
        if (foundIntersection)
            return;
        SampledSpectrum Ld = w.Ld / (w.r_u + w.r_l).Average();

        SampledSpectrum Lpixel = pixelSampleState.L[w.pixelIndex];
        pixelSampleState.L[w.pixelIndex] = Lpixel + Ld;
    }

    // (tMax is a float, not a Float, in pbrt too: with Float = double, the
    // medium sample gets the hit distance rounded to float.)
    public static void EnqueueWorkAfterIntersection(in RayWorkItem r, Medium? rayMedium, float tMax,
                                                    in SurfaceInteraction intr, MediumSampleQueue? mediumSampleQueue,
                                                    RayQueue nextRayQueue, HitAreaLightQueue hitAreaLightQueue,
                                                    MaterialEvalQueue basicEvalMaterialQueue,
                                                    MaterialEvalQueue universalEvalMaterialQueue)
    {
        MediumInterface mediumInterface = intr.mediumInterface ?? new MediumInterface(rayMedium);

        if (rayMedium != null)
        {
            CHECK(mediumSampleQueue != null);
            mediumSampleQueue!.Push(new MediumSampleWorkItem
            {
                ray = r.ray, depth = r.depth, tMax = tMax, lambda = r.lambda, beta = r.beta, r_u = r.r_u,
                r_l = r.r_l, pixelIndex = r.pixelIndex, prevIntrCtx = r.prevIntrCtx,
                specularBounce = r.specularBounce, anyNonSpecularBounces = r.anyNonSpecularBounces,
                etaScale = r.etaScale, areaLight = intr.areaLight, pi = intr.pi, n = intr.n, dpdu = intr.dpdu,
                dpdv = intr.dpdv, wo = -r.ray.d, uv = intr.uv, material = intr.material, ns = intr.shading.n,
                dpdus = intr.shading.dpdu, dpdvs = intr.shading.dpdv, dndus = intr.shading.dndu,
                dndvs = intr.shading.dndv, faceIndex = intr.faceIndex, mediumInterface = mediumInterface
            });
            return;
        }

        // FIXME: this is all basically duplicate code w/medium.cpp
        Material? material = intr.material;

        MixMaterial? mix = material as MixMaterial;
        while (mix != null)
        {
            MaterialEvalContext ctx = new(intr);
            material = mix.ChooseMaterial(new BasicTextureEvaluator(), ctx);
            mix = material as MixMaterial;
        }

        if (material == null)
        {
            Ray newRay = intr.SpawnRay(r.ray.d);
            nextRayQueue.PushIndirectRay(newRay, r.depth, r.prevIntrCtx, r.beta, r.r_u, r.r_l, r.lambda, r.etaScale,
                                         r.specularBounce, r.anyNonSpecularBounces, r.pixelIndex);
            return;
        }

        if (intr.areaLight != null)
        {
            // TODO: intr.wo == -ray.d?
            hitAreaLightQueue.Push(new HitAreaLightWorkItem
            {
                areaLight = intr.areaLight, p = intr.p(), n = intr.n, uv = intr.uv, wo = intr.wo,
                lambda = r.lambda, depth = r.depth, beta = r.beta, r_u = r.r_u, r_l = r.r_l,
                prevIntrCtx = r.prevIntrCtx, specularBounce = r.specularBounce, pixelIndex = r.pixelIndex
            });
        }

        MaterialEvalQueue q = UsesBasicTextureEvaluator(material) ? basicEvalMaterialQueue
                                                                  : universalEvalMaterialQueue;

        q.Push(new MaterialEvalWorkItem
        {
            material = material, pi = intr.pi, n = intr.n, dpdu = intr.dpdu, dpdv = intr.dpdv, time = intr.time,
            depth = r.depth, ns = intr.shading.n, dpdus = intr.shading.dpdu, dpdvs = intr.shading.dpdv,
            dndus = intr.shading.dndu, dndvs = intr.shading.dndv, uv = intr.uv, faceIndex = intr.faceIndex,
            lambda = r.lambda, pixelIndex = r.pixelIndex, anyNonSpecularBounces = r.anyNonSpecularBounces,
            wo = intr.wo, beta = r.beta, r_u = r.r_u, etaScale = r.etaScale, mediumInterface = mediumInterface
        });
    }

    // Whether _material_'s textures can all be evaluated with the
    // BasicTextureEvaluator (pbrt repeats this expression where it's used)
    internal static bool UsesBasicTextureEvaluator(Material material)
    {
        FloatTexture? displacement = material.GetDisplacement();
        return material.CanEvaluateTextures(new BasicTextureEvaluator()) &&
               (displacement == null || new BasicTextureEvaluator().CanEvaluate([displacement], []));
    }

    public static void TraceTransmittance(in ShadowRayWorkItem sr, PixelSampleState pixelSampleState,
                                          Func<Ray, Float, TransmittanceTraceResult> trace,
                                          Func<Point3f, Ray> spawnTo)
    {
        SampledWavelengths lambda = sr.lambda;

        SampledSpectrum Ld = sr.Ld;

        Ray ray = sr.ray;
        Float tMax = sr.tMax;
        Point3f pLight = ray.At(tMax);
        RNG rng = new(Hash(ray.o), Hash(ray.d));

        SampledSpectrum T_ray = new(1f);
        SampledSpectrum r_u = new(1f), r_l = new(1f);

        while (ray.d != new Vector3f(0, 0, 0))
        {
            TransmittanceTraceResult result = trace(ray, tMax);

            if (result.hit && result.material != null)
            {
                // Hit opaque surface
                T_ray = new SampledSpectrum(0f);
                break;
            }

            if (ray.medium != null)
            {
                Float tEnd = !result.hit ? tMax : (Distance(ray.o, result.pHit) / Length(ray.d));
                Float u = rng.Uniform<Float>();
                SampledSpectrum T_maj = SampleT_maj(ray, tEnd, u, ref rng, lambda,
                    (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj,
                     ref RNG rng) =>
                    {
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);

                        // ratio-tracking: only evaluate null scattering
                        Float pr = T_maj[0] * sigma_maj[0];
                        T_ray *= T_maj * sigma_n / pr;
                        r_l *= T_maj * sigma_maj / pr;
                        r_u *= T_maj * sigma_n / pr;

                        // Possibly terminate transmittance computation using Russian roulette
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
                T_ray *= T_maj / T_maj[0];
                r_l *= T_maj / T_maj[0];
                r_u *= T_maj / T_maj[0];
            }

            if (!result.hit || !T_ray)
                // done
                break;

            ray = spawnTo(pLight);
        }

        if (T_ray)
        {
            // FIXME/reconcile: this takes r_l as input while
            // e.g. VolPathIntegrator::SampleLd() does not...
            Ld *= T_ray / (sr.r_u * r_u + sr.r_l * r_l).Average();

            SampledSpectrum Lpixel = pixelSampleState.L[sr.pixelIndex];
            pixelSampleState.L[sr.pixelIndex] = Lpixel + Ld;
        }
    }
}

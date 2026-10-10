// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/wavefront/media.cpp
//
// pbrt runs the medium scattering kernel for each concrete phase function
// type; here there's one kernel and PhaseFunction's virtual methods.

namespace Pbrt;

public sealed partial class WavefrontPathIntegrator
{
    // WavefrontPathIntegrator Participating Media Methods
    private void SampleMediumInteraction(int wavefrontDepth)
    {
        if (!haveMedia)
            return;

        RayQueue nextRayQueue = NextRayQueue(wavefrontDepth);
        ForAllQueued(mediumSampleQueue!, (in MediumSampleWorkItem wi) =>
        {
            // (A copy, since the callback below can't capture an in parameter.)
            MediumSampleWorkItem w = wi;
            Ray ray = w.ray;
            Float tMax = w.tMax;

            SampledWavelengths lambda = w.lambda;
            SampledSpectrum beta = w.beta;
            SampledSpectrum r_u = w.r_u;
            SampledSpectrum r_l = w.r_l;
            SampledSpectrum L = new(0f);
            RNG rng = new(Hash(ray.o, tMax), Hash(ray.d));

            // Sample the medium according to T_maj, the homogeneous
            // transmission function based on the majorant.
            bool scattered = false;

            Float uDist = rng.Uniform<Float>();
            Float uMode = rng.Uniform<Float>();

            SampledSpectrum T_maj = SampleT_maj(ray, tMax, uDist, ref rng, lambda,
                (Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj, ref RNG rng) =>
                {
                    // Add emission, if present.  Always do this and scale
                    // by sigma_a/sigma_maj rather than only doing it
                    // (without scaling) at absorption events.
                    if (w.depth < maxDepth && mp.Le.IsNonZero())
                    {
                        Float pr = sigma_maj[0] * T_maj[0];
                        SampledSpectrum r_e = r_u * sigma_maj * T_maj / pr;

                        // Update _L_ for medium emission
                        if (r_e)
                            L += beta * mp.sigma_a * T_maj * mp.Le / (pr * r_e.Average());
                    }

                    // Compute probabilities for each type of scattering.
                    Float pAbsorb = mp.sigma_a[0] / sigma_maj[0];
                    Float pScatter = mp.sigma_s[0] / sigma_maj[0];
                    Float pNull = FMath.Max(0, 1 - pAbsorb - pScatter);

                    // And randomly choose one.
                    int mode = SampleDiscrete([pAbsorb, pScatter, pNull], uMode);

                    if (mode == 0)
                    {
                        // Absorption--done.
                        beta = new SampledSpectrum(0f);
                        // Tell the medium to stop traversal.
                        return false;
                    }
                    else if (mode == 1)
                    {
                        // Scattering.
                        Float pr = T_maj[0] * mp.sigma_s[0];
                        beta *= T_maj * mp.sigma_s / pr;
                        r_u *= T_maj * mp.sigma_s / pr;

                        // Enqueue medium scattering work.
                        if (beta.IsNonZero() && r_u.IsNonZero())
                            mediumScatterQueue!.Push(new MediumScatterWorkItem
                            {
                                p = p, depth = w.depth, lambda = lambda, beta = beta, r_u = r_u, phase = mp.phase!,
                                wo = -ray.d, time = ray.time, etaScale = w.etaScale, medium = ray.medium!,
                                pixelIndex = w.pixelIndex
                            });

                        scattered = true;

                        return false;
                    }
                    else
                    {
                        // Null scattering.
                        SampledSpectrum sigma_n = ClampZero(sigma_maj - mp.sigma_a - mp.sigma_s);

                        Float pr = T_maj[0] * sigma_n[0];
                        beta *= T_maj * sigma_n / pr;
                        if (pr == 0)
                            beta = new SampledSpectrum(0f);
                        r_u *= T_maj * sigma_n / pr;
                        r_l *= T_maj * sigma_maj / pr;

                        uMode = rng.Uniform<Float>();

                        return beta.IsNonZero() && r_u.IsNonZero();
                    }
                });
            if (!scattered && beta.IsNonZero())
            {
                beta *= T_maj / T_maj[0];
                r_u *= T_maj / T_maj[0];
                r_l *= T_maj / T_maj[0];
            }

            // Add any emission found to its pixel sample's L value.
            if (L)
            {
                SampledSpectrum Lp = pixelSampleState.L[w.pixelIndex];
                pixelSampleState.L[w.pixelIndex] = Lp + L;
            }

            // There's no more work to do if there was a scattering event in
            // the medium.
            if (scattered || !beta || !r_u || w.depth == maxDepth)
                return;

            // Otherwise, enqueue bump and medium stuff...
            // FIXME: this is all basically duplicate code w/optix.cu
            if (w.tMax == Infinity)
            {
                // no intersection
                escapedRayQueue?.Push(new EscapedRayWorkItem
                {
                    rayo = ray.o, rayd = ray.d, depth = w.depth, lambda = lambda, pixelIndex = w.pixelIndex,
                    beta = beta, specularBounce = w.specularBounce, r_u = r_u, r_l = r_l,
                    prevIntrCtx = w.prevIntrCtx
                });
                return;
            }

            Material? material = w.material;

            MixMaterial? mix = material as MixMaterial;
            while (mix != null)
            {
                SurfaceInteraction intr = new(w.pi, w.uv, w.wo, w.dpdus, w.dpdvs, w.dndus, w.dndvs, ray.time,
                                              false /* flip normal */)
                {
                    faceIndex = w.faceIndex
                };
                MaterialEvalContext ctx = new(intr);
                material = mix.ChooseMaterial(new BasicTextureEvaluator(), ctx);
                mix = material as MixMaterial;
            }

            if (material == null)
            {
                Interaction intr = new(w.pi, w.n) { mediumInterface = w.mediumInterface };
                Ray newRay = intr.SpawnRay(ray.d);
                nextRayQueue.PushIndirectRay(newRay, w.depth, w.prevIntrCtx, beta, r_u, r_l, lambda, w.etaScale,
                                             w.specularBounce, w.anyNonSpecularBounces, w.pixelIndex);
                return;
            }

            if (w.areaLight != null)
            {
                hitAreaLightQueue.Push(new HitAreaLightWorkItem
                {
                    areaLight = w.areaLight, p = new Point3f(w.pi), n = w.n, uv = w.uv, wo = -ray.d,
                    lambda = lambda, depth = w.depth, beta = beta, r_u = r_u, r_l = r_l,
                    prevIntrCtx = w.prevIntrCtx, specularBounce = w.specularBounce, pixelIndex = w.pixelIndex
                });
            }

            MaterialEvalQueue q = UsesBasicTextureEvaluator(material) ? basicEvalMaterialQueue
                                                                      : universalEvalMaterialQueue;

            q.Push(new MaterialEvalWorkItem
            {
                material = material, pi = w.pi, n = w.n, dpdu = w.dpdu, dpdv = w.dpdv, time = ray.time,
                depth = w.depth, ns = w.ns, dpdus = w.dpdus, dpdvs = w.dpdvs, dndus = w.dndus, dndvs = w.dndvs,
                uv = w.uv, faceIndex = w.faceIndex, lambda = lambda, pixelIndex = w.pixelIndex,
                anyNonSpecularBounces = w.anyNonSpecularBounces, wo = -ray.d, beta = beta, r_u = r_u,
                etaScale = w.etaScale, mediumInterface = w.mediumInterface
            });
        });

        if (wavefrontDepth == maxDepth)
            return;

        SampleMediumScattering(wavefrontDepth);
    }

    private void SampleMediumScattering(int wavefrontDepth)
    {
        RayQueue nextRayQueue = NextRayQueue(wavefrontDepth);

        ForAllQueued(mediumScatterQueue!, (in MediumScatterWorkItem w) =>
        {
            RaySamples raySamples = pixelSampleState.samples[w.pixelIndex];
            Vector3f wo = w.wo;

            // Sample direct lighting at medium scattering event.  First,
            // choose a light source.
            LightSampleContext ctx = new(new Point3fi(w.p), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0));
            SampledLight? sampledLightO = lightSampler.Sample(ctx, raySamples.direct.uc);

            if (sampledLightO is SampledLight sampledLight)
            {
                Light light = sampledLight.light;
                // And now sample a point on the light.
                LightLiSample? lso = light.SampleLi(ctx, raySamples.direct.u, w.lambda, true);
                if (lso is LightLiSample ls && ls.L.IsNonZero() && ls.pdf > 0)
                {
                    Vector3f wi = ls.wi;
                    SampledSpectrum beta = w.beta * w.phase.p(wo, wi);

                    // Compute PDFs for direct lighting MIS calculation.
                    Float lightPDF = ls.pdf * sampledLight.p;
                    Float phasePDF = IsDeltaLight(light.Type()) ? 0f : w.phase.PDF(wo, wi);
                    SampledSpectrum r_u = w.r_u * phasePDF;
                    SampledSpectrum r_l = w.r_u * lightPDF;

                    SampledSpectrum Ld = beta * ls.L;
                    Ray ray = new(w.p, ls.pLight.p() - w.p, w.time, w.medium);

                    // Enqueue shadow ray
                    shadowRayQueue.Push(new ShadowRayWorkItem
                    {
                        ray = ray, tMax = 1 - ShadowEpsilon, lambda = w.lambda, Ld = Ld, r_u = r_u, r_l = r_l,
                        pixelIndex = w.pixelIndex
                    });
                }
            }

            // Sample indirect lighting.
            PhaseFunctionSample? phaseSampleO = w.phase.Sample_p(wo, raySamples.indirect.u);
            if (phaseSampleO is not PhaseFunctionSample phaseSample || phaseSample.pdf == 0)
                return;

            SampledSpectrum betaI = w.beta * phaseSample.p / phaseSample.pdf;
            SampledSpectrum r_uI = w.r_u;
            SampledSpectrum r_lI = w.r_u / phaseSample.pdf;

            // Russian roulette
            // TODO: should we even bother? Generally beta is one here,
            // due to the way scattering events are scattered and because we're
            // sampling exactly from the phase function's distribution...
            SampledSpectrum rrBeta = betaI * w.etaScale / r_uI.Average();
            if (rrBeta.MaxComponentValue() < 1 && w.depth >= 1)
            {
                Float q = FMath.Max(0, 1 - rrBeta.MaxComponentValue());
                if (raySamples.indirect.rr < q)
                    return;
                betaI /= 1 - q;
            }

            Ray rayI = new(w.p, phaseSample.wi, w.time, w.medium);
            bool specularBounce = false;
            bool anyNonSpecularBounces = true;

            // Spawn indirect ray.
            nextRayQueue.PushIndirectRay(rayI, w.depth + 1, ctx, betaI, r_uI, r_lI, w.lambda, w.etaScale,
                                         specularBounce, anyNonSpecularBounces, w.pixelIndex);
        });
    }
}

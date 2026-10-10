// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/wavefront/subsurface.cpp

namespace Pbrt;

public sealed partial class WavefrontPathIntegrator
{
    // WavefrontPathIntegrator Subsurface Scattering Methods
    private void SampleSubsurface(int wavefrontDepth)
    {
        if (!haveSubsurface)
            return;

        RayQueue nextRayQueue = NextRayQueue(wavefrontDepth);

        ForAllQueued(bssrdfEvalQueue!, (in GetBSSRDFAndProbeRayWorkItem w) =>
        {
            SubsurfaceMaterial material = (SubsurfaceMaterial)w.material;
            MaterialEvalContext ctx = w.GetMaterialEvalContext();
            SampledWavelengths lambda = w.lambda;
            TabulatedBSSRDF bssrdf = material.GetBSSRDF(new BasicTextureEvaluator(), ctx, ref lambda);

            RaySamples raySamples = pixelSampleState.samples[w.pixelIndex];
            Float uc = raySamples.subsurface.uc;
            Point2f u = raySamples.subsurface.u;

            BSSRDFProbeSegment? probeSegO = bssrdf.SampleSp(uc, u);
            if (probeSegO is BSSRDFProbeSegment probeSeg)
                subsurfaceScatterQueue!.Push(new SubsurfaceScatterWorkItem
                {
                    p0 = probeSeg.p0, p1 = probeSeg.p1, depth = w.depth, material = material, bssrdf = bssrdf,
                    lambda = lambda, beta = w.beta, r_u = w.r_u, mediumInterface = w.mediumInterface,
                    etaScale = w.etaScale, pixelIndex = w.pixelIndex
                });
        });

        aggregate.IntersectOneRandom(subsurfaceScatterQueue!);

        ForAllQueued(subsurfaceScatterQueue!, (in SubsurfaceScatterWorkItem w) =>
        {
            if (w.reservoirPDF == 0)
                return;

            TabulatedBSSRDF bssrdf = w.bssrdf;

            SubsurfaceInteraction intr = w.ssi;
            BSSRDFSample bssrdfSample = bssrdf.ProbeIntersectionToSample(intr);

            if (!bssrdfSample.Sp || !bssrdfSample.pdf)
                return;

            Float pr = w.reservoirPDF * bssrdfSample.pdf[0];
            SampledSpectrum betap = w.beta * bssrdfSample.Sp / pr;
            SampledSpectrum r_u = w.r_u * bssrdfSample.pdf / bssrdfSample.pdf[0];
            SampledWavelengths lambda = w.lambda;
            RaySamples raySamples = pixelSampleState.samples[w.pixelIndex];
            Vector3f wo = bssrdfSample.wo;
            BSDF bsdf = bssrdfSample.Sw;
            Float time = 0;  // TODO: pipe through

            // NOTE: the remainder is copied from the Material/BSDF eval method.
            // Will unify into shared fragments in the book...

            // Indirect...
            {
                Point2f u = raySamples.indirect.u;
                Float uc = raySamples.indirect.uc;

                BSDFSample? bsdfSampleO = bsdf.Sample_f(wo, uc, u);
                if (bsdfSampleO is BSDFSample bsdfSample)
                {
                    Vector3f wi = bsdfSample.wi;
                    SampledSpectrum beta = betap * bsdfSample.f * AbsDot(wi, intr.ns) / bsdfSample.pdf;
                    SampledSpectrum indir_r_u = r_u;

                    SampledSpectrum r_l;
                    if (bsdfSample.pdfIsProportional)
                        r_l = r_u / bsdf.PDF(wo, bsdfSample.wi);
                    else
                        r_l = r_u / bsdfSample.pdf;

                    Float etaScale = w.etaScale;
                    if (bsdfSample.IsTransmission())
                        etaScale *= Sqr(bsdfSample.eta);

                    // Russian roulette
                    SampledSpectrum rrBeta = beta * etaScale / indir_r_u.Average();
                    if (rrBeta.MaxComponentValue() < 1 && w.depth > 1)
                    {
                        Float q = FMath.Max(0, 1 - rrBeta.MaxComponentValue());
                        if (raySamples.indirect.rr < q)
                            beta = new SampledSpectrum(0f);
                        else
                            beta /= 1 - q;
                    }

                    if (beta)
                    {
                        Ray ray = SpawnRay(intr.pi, intr.n, time, wi);
                        if (haveMedia)
                            // TODO: should always just take outside in this case?
                            ray.medium = Dot(ray.d, intr.n) > 0 ? w.mediumInterface.outside
                                                                : w.mediumInterface.inside;

                        // || rather than | is intentional, to avoid the read if
                        // possible...
                        bool anyNonSpecularBounces = true;

                        LightSampleContext ctx = new(intr.pi, intr.n, intr.ns);
                        nextRayQueue.PushIndirectRay(ray, w.depth + 1, ctx, beta, indir_r_u, r_l, lambda, etaScale,
                                                     bsdfSample.IsSpecular(), anyNonSpecularBounces, w.pixelIndex);
                    }
                }
            }

            // Direct lighting...
            if (IsNonSpecular(bsdf.Flags()))
            {
                LightSampleContext ctx = new(intr.pi, intr.n, intr.ns);
                SampledLight? sampledLightO = lightSampler.Sample(ctx, raySamples.direct.uc);
                if (sampledLightO is not SampledLight sampledLight)
                    return;
                Light light = sampledLight.light;

                LightLiSample? lso = light.SampleLi(ctx, raySamples.direct.u, lambda, true);
                if (lso is not LightLiSample ls || !ls.L || ls.pdf == 0)
                    return;

                Vector3f wi = ls.wi;
                SampledSpectrum f = bsdf.f(wo, wi);
                if (!f)
                    return;

                SampledSpectrum beta = betap * f * AbsDot(wi, intr.ns);

                Float lightPDF = ls.pdf * sampledLight.p;
                // This causes r_u to be zero for the shadow ray, so that
                // part of MIS just becomes a no-op.
                Float bsdfPDF = IsDeltaLight(light.Type()) ? 0f : bsdf.PDF(wo, wi);
                SampledSpectrum r_l = r_u * lightPDF;
                r_u *= bsdfPDF;

                SampledSpectrum Ld = beta * ls.L;

                Ray ray = SpawnRayTo(intr.pi, intr.n, time, ls.pLight.pi, ls.pLight.n);
                if (haveMedia)
                    // TODO: as above, always take outside here?
                    ray.medium = Dot(ray.d, intr.n) > 0 ? w.mediumInterface.outside : w.mediumInterface.inside;

                shadowRayQueue.Push(new ShadowRayWorkItem
                {
                    ray = ray, tMax = 1 - ShadowEpsilon, lambda = lambda, Ld = Ld, r_u = r_u, r_l = r_l,
                    pixelIndex = w.pixelIndex
                });
            }
        });

        TraceShadowRays(wavefrontDepth);
    }
}

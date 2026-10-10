// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/wavefront/surfscatter.cpp
//
// pbrt runs a kernel for each concrete material type and texture
// evaluator; here there's a kernel for each texture evaluator, and the
// materials' virtual methods are called. (MixMaterials are resolved before
// the items are enqueued, as in pbrt.)

namespace Pbrt;

public sealed partial class WavefrontPathIntegrator
{
    // WavefrontPathIntegrator Surface Scattering Methods
    private void EvaluateMaterialsAndBSDFs(int wavefrontDepth, Transform movingFromCamera)
    {
        if (haveBasicEvalMaterial)
            EvaluateMaterialAndBSDF<BasicTextureEvaluator>(basicEvalMaterialQueue, movingFromCamera, wavefrontDepth);
        if (haveUniversalEvalMaterial)
            EvaluateMaterialAndBSDF<UniversalTextureEvaluator>(universalEvalMaterialQueue, movingFromCamera,
                                                               wavefrontDepth);
    }

    private void EvaluateMaterialAndBSDF<TextureEvaluator>(MaterialEvalQueue evalQueue, Transform movingFromCamera,
                                                           int wavefrontDepth)
        where TextureEvaluator : struct, ITextureEvaluator
    {
        // Get BSDF for items in _evalQueue_ and sample illumination
        RayQueue nextRayQueue = NextRayQueue(wavefrontDepth);
        ForAllQueued(evalQueue, (in MaterialEvalWorkItem w) =>
        {
            // Evaluate material and BSDF for ray intersection
            TextureEvaluator texEval = new();
            // Compute differentials for position and $(u,v)$ at intersection point
            Vector3f dpdx = new(), dpdy = new();
            Float dudx = 0, dudy = 0, dvdx = 0, dvdy = 0;
            if (!Options.disableTextureFiltering)
            {
                Point3f pc = movingFromCamera.ApplyInverse(new Point3f(w.pi));
                Normal3f nc = movingFromCamera.ApplyInverse(w.n);
                camera.Approximate_dp_dxy(pc, nc, w.time, samplesPerPixel, out dpdx, out dpdy);
                Vector3f dpdu = w.dpdu, dpdv = w.dpdv;
                // Estimate screen-space change in $(u,v)$
                // Compute $\transpose{\XFORM{A}} \XFORM{A}$ and its determinant
                Float ata00 = Dot(dpdu, dpdu), ata01 = Dot(dpdu, dpdv);
                Float ata11 = Dot(dpdv, dpdv);
                Float invDet = 1 / DifferenceOfProducts(ata00, ata11, ata01, ata01);
                invDet = IsFinite(invDet) ? invDet : 0f;

                // Compute $\transpose{\XFORM{A}} \VEC{b}$ for $x$ and $y$
                Float atb0x = Dot(dpdu, dpdx), atb1x = Dot(dpdv, dpdx);
                Float atb0y = Dot(dpdu, dpdy), atb1y = Dot(dpdv, dpdy);

                // Compute $u$ and $v$ derivatives with respect to $x$ and $y$
                dudx = DifferenceOfProducts(ata11, atb0x, ata01, atb1x) * invDet;
                dvdx = DifferenceOfProducts(ata00, atb1x, ata01, atb0x) * invDet;
                dudy = DifferenceOfProducts(ata11, atb0y, ata01, atb1y) * invDet;
                dvdy = DifferenceOfProducts(ata00, atb1y, ata01, atb0y) * invDet;

                // Clamp derivatives of $u$ and $v$ to reasonable values
                dudx = IsFinite(dudx) ? Clamp(dudx, -1e8f, 1e8f) : 0f;
                dvdx = IsFinite(dvdx) ? Clamp(dvdx, -1e8f, 1e8f) : 0f;
                dudy = IsFinite(dudy) ? Clamp(dudy, -1e8f, 1e8f) : 0f;
                dvdy = IsFinite(dvdy) ? Clamp(dvdy, -1e8f, 1e8f) : 0f;
            }

            // Compute shading normal if bump or normal mapping is being used
            Normal3f ns = w.ns;
            Vector3f dpdus = w.dpdus;
            FloatTexture? displacement = w.material.GetDisplacement();
            Image? normalMap = w.material.GetNormalMap();
            if (normalMap != null)
            {
                // Call _NormalMap()_ to find shading geometry
                NormalBumpEvalContext bctx = w.GetNormalBumpEvalContext(dudx, dudy, dvdx, dvdy);
                NormalMap(normalMap, bctx, out dpdus, out Vector3f dpdvs);
                ns = new Normal3f(Normalize(Cross(dpdus, dpdvs)));
                ns = FaceForward(ns, w.n);
            }
            else if (displacement != null)
            {
                // Call _BumpMap()_ to find shading geometry
                DCHECK(texEval.CanEvaluate([displacement], []));
                NormalBumpEvalContext bctx = w.GetNormalBumpEvalContext(dudx, dudy, dvdx, dvdy);
                BumpMap(texEval, displacement, bctx, out dpdus, out Vector3f dpdvs);
                ns = new Normal3f(Normalize(Cross(dpdus, dpdvs)));
                ns = FaceForward(ns, w.n);
            }

            // Get BSDF at intersection point
            SampledWavelengths lambda = w.lambda;
            MaterialEvalContext ctx = w.GetMaterialEvalContext(dudx, dudy, dvdx, dvdy, ns, dpdus);
            BxDF bxdf = w.material.GetBxDF(texEval, ctx, ref lambda)!;
            BSDF bsdf = new(ctx.ns, ctx.dpdus, bxdf);
            // Handle terminated secondary wavelengths after BSDF creation
            if (lambda.SecondaryTerminated())
                pixelSampleState.lambda[w.pixelIndex] = lambda;

            // Regularize BSDF, if appropriate
            if (regularize && w.anyNonSpecularBounces)
                bsdf.Regularize();

            // Initialize _VisibleSurface_ at first intersection if necessary
            if (w.depth == 0 && initializeVisibleSurface)
            {
                SurfaceInteraction isect = new()
                {
                    pi = w.pi,
                    n = w.n,
                    uv = w.uv,
                    wo = w.wo,
                    time = w.time,
                    dpdx = dpdx,
                    dpdy = dpdy
                };
                isect.shading.n = ns;

                // Estimate BSDF's albedo
                SampledSpectrum albedo = bsdf.rho(isect.wo, PathIntegrator.ucRho, PathIntegrator.uRho);

                pixelSampleState.visibleSurface[w.pixelIndex] = new VisibleSurface(isect, albedo, lambda);
            }

            // Sample BSDF and enqueue indirect ray at intersection point
            Vector3f wo = w.wo;
            RaySamples raySamples = pixelSampleState.samples[w.pixelIndex];
            BSDFSample? bsdfSampleO = bsdf.Sample_f(wo, raySamples.indirect.uc, raySamples.indirect.u);
            if (bsdfSampleO is BSDFSample bsdfSample)
            {
                // Compute updated path throughput and PDFs and enqueue indirect ray
                Vector3f wi = bsdfSample.wi;
                SampledSpectrum beta = w.beta * bsdfSample.f * AbsDot(wi, ns) / bsdfSample.pdf;
                SampledSpectrum r_u = w.r_u, r_l;

                // Update _r_u_ based on BSDF sample PDF
                if (bsdfSample.pdfIsProportional)
                    r_l = r_u / bsdf.PDF(wo, bsdfSample.wi);
                else
                    r_l = r_u / bsdfSample.pdf;

                // Update _etaScale_ accounting for BSDF scattering
                Float etaScale = w.etaScale;
                if (bsdfSample.IsTransmission())
                    etaScale *= Sqr(bsdfSample.eta);

                // Apply Russian roulette to indirect ray based on weighted path
                // throughput
                SampledSpectrum rrBeta = beta * etaScale / r_u.Average();
                // Note: depth >= 1 here to match VolPathIntegrator (which increments
                // depth earlier).
                if (rrBeta.MaxComponentValue() < 1 && w.depth >= 1)
                {
                    Float q = FMath.Max(0, 1 - rrBeta.MaxComponentValue());
                    if (raySamples.indirect.rr < q)
                        beta = new SampledSpectrum(0f);
                    else
                        beta /= 1 - q;
                }

                if (beta)
                {
                    // Initialize spawned ray and enqueue for next ray depth
                    if (bsdfSample.IsTransmission() && w.material.HasSubsurfaceScattering())
                    {
                        bssrdfEvalQueue!.Push(new GetBSSRDFAndProbeRayWorkItem
                        {
                            material = w.material, lambda = lambda, beta = beta, r_u = r_u, p = new Point3f(w.pi),
                            wo = wo, n = w.n, ns = ns, dpdus = dpdus, uv = w.uv, depth = w.depth,
                            mediumInterface = w.mediumInterface, etaScale = etaScale, pixelIndex = w.pixelIndex
                        });
                    }
                    else
                    {
                        Ray ray = SpawnRay(w.pi, w.n, w.time, wi);
                        // Initialize _ray_ medium if media are present
                        if (haveMedia)
                            ray.medium = Dot(ray.d, w.n) > 0 ? w.mediumInterface.outside
                                                             : w.mediumInterface.inside;

                        bool anyNonSpecularBounces = !bsdfSample.IsSpecular() || w.anyNonSpecularBounces;
                        // NOTE: slightly different than context below. Problem?
                        LightSampleContext ctx2 = new(w.pi, w.n, ns);
                        nextRayQueue.PushIndirectRay(ray, w.depth + 1, ctx2, beta, r_u, r_l, lambda, etaScale,
                                                     bsdfSample.IsSpecular(), anyNonSpecularBounces, w.pixelIndex);
                    }
                }
            }

            // Sample light and enqueue shadow ray at intersection point
            BxDFFlags flags = bsdf.Flags();
            if (IsNonSpecular(flags))
            {
                // Choose a light source using the _LightSampler_
                LightSampleContext ctx2 = new(w.pi, w.n, ns);
                if (IsReflective(flags) && !IsTransmissive(flags))
                    ctx2.pi = new Point3fi(OffsetRayOrigin(ctx2.pi, w.n, wo));
                else if (IsTransmissive(flags) && IsReflective(flags))
                    ctx2.pi = new Point3fi(OffsetRayOrigin(ctx2.pi, w.n, -wo));
                SampledLight? sampledLightO = lightSampler.Sample(ctx2, raySamples.direct.uc);
                if (sampledLightO is not SampledLight sampledLight)
                    return;
                Light light = sampledLight.light;

                // Sample light source and evaluate BSDF for direct lighting
                LightLiSample? lso = light.SampleLi(ctx2, raySamples.direct.u, lambda, true);
                if (lso is not LightLiSample ls || !ls.L || ls.pdf == 0)
                    return;
                Vector3f wi = ls.wi;
                SampledSpectrum f = bsdf.f(wo, wi);
                if (!f)
                    return;

                // Compute path throughput and path PDFs for light sample
                SampledSpectrum beta = w.beta * f * AbsDot(wi, ns);

                Float lightPDF = ls.pdf * sampledLight.p;
                // This causes r_u to be zero for the shadow ray, so that
                // part of MIS just becomes a no-op.
                Float bsdfPDF = IsDeltaLight(light.Type()) ? 0f : bsdf.PDF(wo, wi);
                SampledSpectrum r_u = w.r_u * bsdfPDF;
                SampledSpectrum r_l = w.r_u * lightPDF;

                // Enqueue shadow ray with tentative radiance contribution
                SampledSpectrum Ld = beta * ls.L;
                Ray ray = SpawnRayTo(w.pi, w.n, w.time, ls.pLight.pi, ls.pLight.n);
                // Initialize _ray_ medium if media are present
                if (haveMedia)
                    ray.medium = Dot(ray.d, w.n) > 0 ? w.mediumInterface.outside : w.mediumInterface.inside;

                shadowRayQueue.Push(new ShadowRayWorkItem
                {
                    ray = ray, tMax = 1 - ShadowEpsilon, lambda = lambda, Ld = Ld, r_u = r_u, r_l = r_l,
                    pixelIndex = w.pixelIndex
                });
            }
        });
    }
}

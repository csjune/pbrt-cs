// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/bssrdf.h and bssrdf.cpp

namespace Pbrt;

// BSSRDFSample Definition
public struct BSSRDFSample
{
    public SampledSpectrum Sp, pdf;
    public BSDF Sw;
    public Vector3f wo;

    public BSSRDFSample(in SampledSpectrum Sp, in SampledSpectrum pdf, in BSDF Sw, Vector3f wo)
    {
        this.Sp = Sp;
        this.pdf = pdf;
        this.Sw = Sw;
        this.wo = wo;
    }
}

// SubsurfaceInteraction Definition
public struct SubsurfaceInteraction
{
    // SubsurfaceInteraction Public Members
    public Point3fi pi;
    public Normal3f n, ns;
    public Vector3f dpdu, dpdv, dpdus, dpdvs;

    // SubsurfaceInteraction Public Methods
    public SubsurfaceInteraction(in SurfaceInteraction si)
    {
        pi = si.pi;
        n = si.n;
        dpdu = si.dpdu;
        dpdv = si.dpdv;
        ns = si.shading.n;
        dpdus = si.shading.dpdu;
        dpdvs = si.shading.dpdv;
    }

    public static explicit operator SurfaceInteraction(in SubsurfaceInteraction ssi)
    {
        SurfaceInteraction si = new()
        {
            pi = ssi.pi,
            n = ssi.n,
            dpdu = ssi.dpdu,
            dpdv = ssi.dpdv,
        };
        si.shading.n = ssi.ns;
        si.shading.dpdu = ssi.dpdus;
        si.shading.dpdv = ssi.dpdvs;
        return si;
    }

    public readonly Point3f p() => new(pi);
}

// BSSRDFTable Definition
public sealed class BSSRDFTable
{
    // BSSRDFTable Public Members
    public readonly Float[] rhoSamples, radiusSamples;
    public readonly Float[] profile;
    public readonly Float[] rhoEff;
    public readonly Float[] profileCDF;

    // BSSRDFTable Method Definitions
    public BSSRDFTable(int nRhoSamples, int nRadiusSamples)
    {
        rhoSamples = new Float[nRhoSamples];
        radiusSamples = new Float[nRadiusSamples];
        profile = new Float[nRadiusSamples * nRhoSamples];
        rhoEff = new Float[nRhoSamples];
        profileCDF = new Float[nRadiusSamples * nRhoSamples];
    }

    private static string Vec(Float[] v) => v.Length == 0 ? "[  ]" : $"[ {string.Join(", ", v.Select(FloatToString))} ]";

    public override string ToString() =>
        $"[ BSSRDFTable rhoSamples: {Vec(rhoSamples)} radiusSamples: {Vec(radiusSamples)} profile: {Vec(profile)} " +
        $"rhoEff: {Vec(rhoEff)} profileCDF: {Vec(profileCDF)} ]";

    // BSSRDFTable Public Methods
    public Float EvalProfile(int rhoIndex, int radiusIndex)
    {
        CHECK(rhoIndex >= 0 && rhoIndex < rhoSamples.Length);
        CHECK(radiusIndex >= 0 && radiusIndex < radiusSamples.Length);
        return profile[rhoIndex * radiusSamples.Length + radiusIndex];
    }
}

// BSSRDFProbeSegment Definition
public struct BSSRDFProbeSegment
{
    public Point3f p0, p1;

    // BSSRDFProbeSegment Public Methods
    public BSSRDFProbeSegment(Point3f p0, Point3f p1)
    {
        this.p0 = p0;
        this.p1 = p1;
    }
}

// TabulatedBSSRDF Definition
public sealed class TabulatedBSSRDF : BSSRDF
{
    // TabulatedBSSRDF Private Members
    private readonly Point3f po;
    private readonly Vector3f wo;
    private readonly Normal3f ns;
    private readonly Float eta;
    private readonly SampledSpectrum sigma_t, rho;
    private readonly BSSRDFTable table;

    // TabulatedBSSRDF Public Methods
    public TabulatedBSSRDF(Point3f po, Normal3f ns, Vector3f wo, Float eta, in SampledSpectrum sigma_a,
                           in SampledSpectrum sigma_s, BSSRDFTable table)
    {
        this.po = po;
        this.wo = wo;
        this.eta = eta;
        this.ns = ns;
        this.table = table;
        sigma_t = sigma_a + sigma_s;
        rho = SafeDiv(sigma_s, sigma_t);
    }

    public SampledSpectrum Sp(Point3f pi) => Sr(Distance(po, pi));

    public SampledSpectrum Sr(Float r)
    {
        SampledSpectrum Sr = new(0f);
        Span<Float> rhoWeights = stackalloc Float[4], radiusWeights = stackalloc Float[4];
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            // Convert $r$ into unitless optical radius $r_{\roman{optical}}$
            Float rOptical = r * sigma_t[i];

            // Compute spline weights to interpolate BSSRDF at _i_th wavelength
            if (!CatmullRomWeights(table.rhoSamples, rho[i], out int rhoOffset, rhoWeights) ||
                !CatmullRomWeights(table.radiusSamples, rOptical, out int radiusOffset, radiusWeights))
                continue;

            // Set BSSRDF value _Sr[i]_ using tensor spline interpolation
            Float sr = 0;
            for (int j = 0; j < 4; ++j)
                for (int k = 0; k < 4; ++k)
                {
                    // Accumulate contribution of $(j,k)$ table sample
                    Float weight = rhoWeights[j] * radiusWeights[k];
                    if (weight != 0)
                        sr += weight * table.EvalProfile(rhoOffset + j, radiusOffset + k);
                }
            // Cancel marginal PDF factor from tabulated BSSRDF profile
            if (rOptical != 0)
                sr /= 2 * Pi * rOptical;

            Sr[i] = sr;
        }
        // Transform BSSRDF value into rendering space units
        Sr *= sigma_t * sigma_t;

        return ClampZero(Sr);
    }

    public Float? SampleSr(Float u)
    {
        if (sigma_t[0] == 0)
            return null;
        return SampleCatmullRom2D(table.rhoSamples, table.radiusSamples, table.profile, table.profileCDF, rho[0], u) /
               sigma_t[0];
    }

    public SampledSpectrum PDF_Sr(Float r)
    {
        SampledSpectrum pdf = new(0f);
        Span<Float> rhoWeights = stackalloc Float[4], radiusWeights = stackalloc Float[4];
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            // Convert $r$ into unitless optical radius $r_{\roman{optical}}$
            Float rOptical = r * sigma_t[i];

            // Compute spline weights to interpolate BSSRDF at _i_th wavelength
            if (!CatmullRomWeights(table.rhoSamples, rho[i], out int rhoOffset, rhoWeights) ||
                !CatmullRomWeights(table.radiusSamples, rOptical, out int radiusOffset, radiusWeights))
                continue;

            // Set BSSRDF profile probability density for wavelength
            Float sr = 0, rhoEff = 0;
            for (int j = 0; j < 4; ++j)
                if (rhoWeights[j] != 0)
                {
                    // Update _rhoEff_ and _sr_ for wavelength
                    rhoEff += table.rhoEff[rhoOffset + j] * rhoWeights[j];
                    for (int k = 0; k < 4; ++k)
                        if (radiusWeights[k] != 0)
                            sr += table.EvalProfile(rhoOffset + j, radiusOffset + k) * rhoWeights[j] * radiusWeights[k];
                }
            // Cancel marginal PDF factor from tabulated BSSRDF profile
            if (rOptical != 0)
                sr /= 2 * Pi * rOptical;

            pdf[i] = sr * Sqr(sigma_t[i]) / rhoEff;
        }
        return ClampZero(pdf);
    }

    public override BSSRDFProbeSegment? SampleSp(Float u1, Point2f u2)
    {
        // Choose projection axis for BSSRDF sampling
        Frame f;
        if (u1 < 0.25f)
            f = Frame.FromX(ns);
        else if (u1 < 0.5f)
            f = Frame.FromY(ns);
        else
            f = Frame.FromZ(ns);

        // Sample BSSRDF profile in polar coordinates
        Float? or = SampleSr(u2[0]);
        if (or is not { } r)
            return null;
        Float phi = 2 * Pi * u2[1];

        // Compute BSSRDF profile bounds and intersection height
        Float? or_max = SampleSr(0.999f);
        if (or_max is not { } r_max || r >= r_max)
            return null;
        Float l = 2 * FMath.Sqrt(Sqr(r_max) - Sqr(r));

        // Return BSSRDF sampling ray segment
        Point3f pStart = po + r * (f.x * FMath.Cos(phi) + f.y * FMath.Sin(phi)) - l * f.z / 2;
        Point3f pTarget = pStart + l * f.z;
        return new BSSRDFProbeSegment(pStart, pTarget);
    }

    public SampledSpectrum PDF_Sp(Point3f pi, Normal3f ni)
    {
        // Express $\pti-\pto$ and $\N{}_\roman{i}$ with respect to local coordinates at
        // $\pto$
        Vector3f d = pi - po;
        Frame f = Frame.FromZ(ns);
        Vector3f dLocal = f.ToLocal(d);
        Normal3f nLocal = f.ToLocal(ni);

        // Compute BSSRDF profile radius under projection along each axis
        Span<Float> rProj = [FMath.Sqrt(Sqr(dLocal.y) + Sqr(dLocal.z)), FMath.Sqrt(Sqr(dLocal.z) + Sqr(dLocal.x)),
                             FMath.Sqrt(Sqr(dLocal.x) + Sqr(dLocal.y))];

        // Return combined probability from all BSSRDF sampling strategies
        SampledSpectrum pdf = new(0f);
        Span<Float> axisProb = [.25f, .25f, .5f];
        for (int axis = 0; axis < 3; ++axis)
            pdf += PDF_Sr(rProj[axis]) * FMath.Abs(nLocal[axis]) * axisProb[axis];
        return pdf;
    }

    public override BSSRDFSample ProbeIntersectionToSample(in SubsurfaceInteraction si)
    {
        NormalizedFresnelBxDF bxdf = new(eta);
        Vector3f wo = new(si.ns);
        BSDF bsdf = new(si.ns, si.dpdus, bxdf);
        return new BSSRDFSample(Sp(si.p()), PDF_Sp(si.p(), si.n), bsdf, wo);
    }

    public override string ToString() =>
        $"[ TabulatedBSSRDF po: {po} eta: {FloatToString(eta)} ns: {ns} sigma_t: {sigma_t} rho: {rho} table: {table} ]";
}

public static partial class Util
{
    // BSSRDF Function Definitions
    public static Float BeamDiffusionMS(Float sigma_s, Float sigma_a, Float g, Float eta, Float r)
    {
        const int nSamples = 100;
        Float Ed = 0;
        // Precompute information for dipole integrand
        // Compute reduced scattering coefficients $\sigmaps, \sigmapt$ and albedo $\rhop$
        Float sigmap_s = sigma_s * (1 - g);
        Float sigmap_t = sigma_a + sigmap_s;
        Float rhop = sigmap_s / sigmap_t;

        // Compute non-classical diffusion coefficient $D_\roman{G}$ using Equation
        // $(\ref{eq:diffusion-coefficient-grosjean})$
        Float D_g = (2 * sigma_a + sigmap_s) / (3 * sigmap_t * sigmap_t);

        // Compute effective transport coefficient $\sigmatr$ based on $D_\roman{G}$
        Float sigma_tr = SafeSqrt(sigma_a / D_g);

        // Determine linear extrapolation distance $\depthextrapolation$ using Equation
        // $(\ref{eq:dipole-boundary-condition})$
        Float fm1 = FresnelMoment1(eta), fm2 = FresnelMoment2(eta);
        Float ze = -2 * D_g * (1 + 3 * fm2) / (1 - 2 * fm1);

        // Determine exitance scale factors using Equations $(\ref{eq:kp-exitance-phi})$ and
        // $(\ref{eq:kp-exitance-e})$
        Float cPhi = 0.25f * (1 - 2 * fm1), cE = 0.5f * (1 - 3 * fm2);

        for (int i = 0; i < nSamples; ++i)
        {
            // Sample real point source depth $\depthreal$
            Float zr = SampleExponential((i + 0.5f) / nSamples, sigmap_t);

            // Evaluate dipole integrand $E_{\roman{d}}$ at $\depthreal$ and add to _Ed_
            Float zv = -zr + 2 * ze;
            Float dr = FMath.Sqrt(Sqr(r) + Sqr(zr)), dv = FMath.Sqrt(Sqr(r) + Sqr(zv));
            // Compute dipole fluence rate $\dipole(r)$ using Equation
            // $(\ref{eq:diffusion-dipole})$
            Float phiD = Inv4Pi / D_g * (FastExp((float)(-sigma_tr * dr)) / dr - FastExp((float)(-sigma_tr * dv)) / dv);

            // Compute dipole vector irradiance $-\N{}\cdot\dipoleE(r)$ using Equation
            // $(\ref{eq:diffusion-dipole-vector-irradiance-normal})$
            Float EDn = Inv4Pi * (zr * (1 + sigma_tr * dr) * FastExp((float)(-sigma_tr * dr)) / (Pow(dr, 3)) -
                                  zv * (1 + sigma_tr * dv) * FastExp((float)(-sigma_tr * dv)) / (Pow(dv, 3)));

            // Add contribution from dipole for depth $\depthreal$ to _Ed_
            Float E = phiD * cPhi + EDn * cE;
            Float kappa = 1 - FastExp((float)(-2 * sigmap_t * (dr + zr)));
            Ed += kappa * rhop * rhop * E;
        }
        return Ed / nSamples;
    }

    public static Float BeamDiffusionSS(Float sigma_s, Float sigma_a, Float g, Float eta, Float r)
    {
        // Compute material parameters and minimum $t$ below the critical angle
        Float sigma_t = sigma_a + sigma_s, rho = sigma_s / sigma_t;
        Float tCrit = r * SafeSqrt(Sqr(eta) - 1);

        Float Ess = 0;
        const int nSamples = 100;
        for (int i = 0; i < nSamples; ++i)
        {
            // Evaluate single-scattering integrand and add to _Ess_
            Float ti = tCrit + SampleExponential((i + 0.5f) / nSamples, sigma_t);
            // Determine length $d$ of connecting segment and $\cos\theta_\roman{o}$
            Float d = FMath.Sqrt(Sqr(r) + Sqr(ti));
            Float cosTheta_o = ti / d;

            // Add contribution of single scattering at depth $t$
            Ess += rho * FastExp((float)(-sigma_t * (d + tCrit))) / Sqr(d) * HenyeyGreenstein(cosTheta_o, g) *
                   (1 - FrDielectric(-cosTheta_o, eta)) * FMath.Abs(cosTheta_o);
        }
        return Ess / nSamples;
    }

    public static void ComputeBeamDiffusionBSSRDF(Float g, Float eta, BSSRDFTable t)
    {
        // Choose radius values of the diffusion profile discretization
        t.radiusSamples[0] = 0;
        t.radiusSamples[1] = 2.5e-3f;
        for (int i = 2; i < t.radiusSamples.Length; ++i)
            t.radiusSamples[i] = t.radiusSamples[i - 1] * 1.2f;

        // Choose albedo values of the diffusion profile discretization
        for (int i = 0; i < t.rhoSamples.Length; ++i)
            t.rhoSamples[i] = (1 - FastExp((float)(-8 * i / (Float)(t.rhoSamples.Length - 1)))) / (1 - FastExp(-8));

        ParallelFor(0, t.rhoSamples.Length, (long li) =>
        {
            int i = (int)li;
            // Compute the diffusion profile for the _i_th albedo sample
            // Compute scattering profile for chosen albedo $\rho$
            int nSamples = t.radiusSamples.Length;
            for (int j = 0; j < nSamples; ++j)
            {
                Float rho = t.rhoSamples[i], r = t.radiusSamples[j];
                t.profile[i * nSamples + j] = 2 * Pi * r * (BeamDiffusionSS(rho, 1 - rho, g, eta, r) +
                                                            BeamDiffusionMS(rho, 1 - rho, g, eta, r));
            }

            // Compute effective albedo $\rho_{\roman{eff}}$ and CDF for importance sampling
            t.rhoEff[i] = IntegrateCatmullRom(t.radiusSamples, t.profile.AsSpan(i * nSamples, nSamples),
                                              t.profileCDF.AsSpan(i * nSamples, nSamples));
        });
    }

    // BSSRDF Inline Functions
    public static void SubsurfaceFromDiffuse(BSSRDFTable t, in SampledSpectrum rhoEff, in SampledSpectrum mfp,
                                             out SampledSpectrum sigma_a, out SampledSpectrum sigma_s)
    {
        sigma_a = default;
        sigma_s = default;
        for (int c = 0; c < NSpectrumSamples; ++c)
        {
            Float rho = InvertCatmullRom(t.rhoSamples, t.rhoEff, rhoEff[c]);
            sigma_s[c] = rho / mfp[c];
            sigma_a[c] = (1 - rho) / mfp[c];
        }
    }
}

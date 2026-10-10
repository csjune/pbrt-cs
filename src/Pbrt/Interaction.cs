// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/interaction.h and interaction.cpp
//
// The interactions are structs, as in pbrt, so they can't inherit from
// Interaction: MediumInteraction and SurfaceInteraction repeat its members
// and convert implicitly to it (as pbrt slices them), like RayDifferential
// and Ray. pbrt's AsSurface()/AsMedium() casts aren't possible; the code
// that uses them keeps the derived type. The const MediumInterface pointer
// is a nullable MediumInterface.

namespace Pbrt;

// Interaction Definition
public struct Interaction
{
    // Interaction Public Members
    public Point3fi pi;
    public Float time = 0;
    public Vector3f wo;
    public Normal3f n;
    public Point2f uv;
    public MediumInterface? mediumInterface = null;
    public Medium? medium = null;

    // Interaction Public Methods
    public Interaction() { }

    public Interaction(Point3fi pi, Normal3f n, Point2f uv, Vector3f wo, Float time)
    {
        this.pi = pi;
        this.n = n;
        this.uv = uv;
        this.wo = Normalize(wo);
        this.time = time;
    }

    // used by medium ctor
    public Interaction(Point3f p, Vector3f wo, Float time, Medium? medium)
    {
        pi = p;
        this.time = time;
        this.wo = wo;
        this.medium = medium;
    }

    public Interaction(Point3f p, Normal3f n, Float time, Medium? medium)
    {
        pi = p;
        this.n = n;
        this.time = time;
        this.medium = medium;
    }

    public Interaction(Point3f p, Point2f uv)
    {
        pi = p;
        this.uv = uv;
    }

    public Interaction(Point3fi pi, Normal3f n, Float time = 0, Point2f uv = default)
    {
        this.pi = pi;
        this.n = n;
        this.uv = uv;
        this.time = time;
    }

    public Interaction(Point3fi pi, Normal3f n, Point2f uv)
    {
        this.pi = pi;
        this.n = n;
        this.uv = uv;
    }

    public Interaction(Point3f p, Float time, Medium? medium)
    {
        pi = p;
        this.time = time;
        this.medium = medium;
    }

    public Interaction(Point3f p, MediumInterface? mediumInterface)
    {
        pi = p;
        this.mediumInterface = mediumInterface;
    }

    public Interaction(Point3f p, Float time, MediumInterface? mediumInterface)
    {
        pi = p;
        this.time = time;
        this.mediumInterface = mediumInterface;
    }

    public readonly Point3f p() => new(pi);

    public readonly bool IsSurfaceInteraction() => n != new Normal3f(0, 0, 0);
    public readonly bool IsMediumInteraction() => !IsSurfaceInteraction();

    public readonly Point3f OffsetRayOrigin(Vector3f w) => Util.OffsetRayOrigin(pi, n, w);

    public readonly Point3f OffsetRayOrigin(Point3f pt) => OffsetRayOrigin(pt - p());

    public readonly RayDifferential SpawnRay(Vector3f d) => new(OffsetRayOrigin(d), d, time, GetMedium(d));

    public readonly Ray SpawnRayTo(Point3f p2)
    {
        Ray r = Util.SpawnRayTo(pi, n, time, p2);
        r.medium = GetMedium(r.d);
        return r;
    }

    public readonly Ray SpawnRayTo(in Interaction it)
    {
        Ray r = Util.SpawnRayTo(pi, n, time, it.pi, it.n);
        r.medium = GetMedium(r.d);
        return r;
    }

    public readonly Medium? GetMedium(Vector3f w)
    {
        if (mediumInterface.HasValue)
            return Dot(w, n) > 0 ? mediumInterface.Value.outside : mediumInterface.Value.inside;
        return medium;
    }

    public readonly Medium? GetMedium()
    {
        if (mediumInterface.HasValue)
            DCHECK(mediumInterface.Value.inside == mediumInterface.Value.outside);
        return mediumInterface.HasValue ? mediumInterface.Value.inside : medium;
    }

    public override readonly string ToString() =>
        $"[ Interaction pi: {pi} n: {n} uv: {uv} wo: {wo} time: {FloatToString(time)} " +
        $"medium: {medium?.ToString() ?? "(nullptr)"} mediumInterface: {mediumInterface?.ToString() ?? "(nullptr)"} ]";
}

// MediumInteraction Definition
public struct MediumInteraction
{
    // (Interaction's members)
    public Point3fi pi;
    public Float time = 0;
    public Vector3f wo;
    public Normal3f n;
    public Point2f uv;
    public MediumInterface? mediumInterface = null;
    public Medium? medium = null;

    // MediumInteraction Public Members
    public PhaseFunction? phase = null;

    // MediumInteraction Public Methods
    public MediumInteraction() { }

    public MediumInteraction(Point3f p, Vector3f wo, Float time, Medium? medium, PhaseFunction? phase)
    {
        pi = p;
        this.time = time;
        this.wo = wo;
        this.medium = medium;
        this.phase = phase;
    }

    public static implicit operator Interaction(in MediumInteraction mi) => new()
    {
        pi = mi.pi, time = mi.time, wo = mi.wo, n = mi.n, uv = mi.uv, mediumInterface = mi.mediumInterface,
        medium = mi.medium
    };

    public readonly Point3f p() => new(pi);

    public override readonly string ToString() =>
        $"[ MediumInteraction pi: {pi} n: {n} uv: {uv} wo: {wo} time: {FloatToString(time)} " +
        $"medium: {medium?.ToString() ?? "(nullptr)"} mediumInterface: {mediumInterface?.ToString() ?? "(nullptr)"} " +
        $"phase: {phase?.ToString() ?? "(nullptr)"} ]";
}

// SurfaceInteraction Definition
public struct SurfaceInteraction
{
    // (Interaction's members)
    public Point3fi pi;
    public Float time = 0;
    public Vector3f wo;
    public Normal3f n;
    public Point2f uv;
    public MediumInterface? mediumInterface = null;
    public Medium? medium = null;

    // SurfaceInteraction Public Members
    public Vector3f dpdu, dpdv;
    public Normal3f dndu, dndv;

    public struct ShadingGeometry
    {
        public Normal3f n;
        public Vector3f dpdu, dpdv;
        public Normal3f dndu, dndv;
    }

    public ShadingGeometry shading;
    public int faceIndex = 0;
    public Material? material = null;
    public Light? areaLight = null;
    public Vector3f dpdx, dpdy;
    public Float dudx = 0, dvdx = 0, dudy = 0, dvdy = 0;

    // SurfaceInteraction Public Methods
    public SurfaceInteraction() { }

    public SurfaceInteraction(Point3fi pi, Point2f uv, Vector3f wo, Vector3f dpdu, Vector3f dpdv, Normal3f dndu,
                              Normal3f dndv, Float time, bool flipNormal)
    {
        this.pi = pi;
        n = new Normal3f(Normalize(Cross(dpdu, dpdv)));
        this.uv = uv;
        this.wo = Normalize(wo);
        this.time = time;
        this.dpdu = dpdu;
        this.dpdv = dpdv;
        this.dndu = dndu;
        this.dndv = dndv;
        // Initialize shading geometry from true geometry
        shading.n = n;
        shading.dpdu = dpdu;
        shading.dpdv = dpdv;
        shading.dndu = dndu;
        shading.dndv = dndv;

        // Adjust normal based on orientation and handedness
        if (flipNormal)
        {
            n *= -1;
            shading.n *= -1;
        }
    }

    public SurfaceInteraction(Point3fi pi, Point2f uv, Vector3f wo, Vector3f dpdu, Vector3f dpdv, Normal3f dndu,
                              Normal3f dndv, Float time, bool flipNormal, int faceIndex)
        : this(pi, uv, wo, dpdu, dpdv, dndu, dndv, time, flipNormal)
    {
        this.faceIndex = faceIndex;
    }

    public static implicit operator Interaction(in SurfaceInteraction si) => new()
    {
        pi = si.pi, time = si.time, wo = si.wo, n = si.n, uv = si.uv, mediumInterface = si.mediumInterface,
        medium = si.medium
    };

    private readonly Interaction AsInteraction() => this;

    public readonly Point3f p() => new(pi);
    public readonly bool IsSurfaceInteraction() => n != new Normal3f(0, 0, 0);
    public readonly Point3f OffsetRayOrigin(Vector3f w) => Util.OffsetRayOrigin(pi, n, w);
    public readonly Point3f OffsetRayOrigin(Point3f pt) => OffsetRayOrigin(pt - p());
    public readonly RayDifferential SpawnRay(Vector3f d) => AsInteraction().SpawnRay(d);
    public readonly Ray SpawnRayTo(Point3f p2) => AsInteraction().SpawnRayTo(p2);
    public readonly Ray SpawnRayTo(in Interaction it) => AsInteraction().SpawnRayTo(it);
    public readonly Medium? GetMedium(Vector3f w) => AsInteraction().GetMedium(w);
    public readonly Medium? GetMedium() => AsInteraction().GetMedium();

    public void SetShadingGeometry(Normal3f ns, Vector3f dpdus, Vector3f dpdvs, Normal3f dndus, Normal3f dndvs,
                                   bool orientationIsAuthoritative)
    {
        // Compute _shading.n_ for _SurfaceInteraction_
        shading.n = ns;
        DCHECK(shading.n != new Normal3f(0, 0, 0));
        if (orientationIsAuthoritative)
            n = FaceForward(n, shading.n);
        else
            shading.n = FaceForward(shading.n, n);

        // Initialize _shading_ partial derivative values
        shading.dpdu = dpdus;
        shading.dpdv = dpdvs;
        shading.dndu = dndus;
        shading.dndv = dndvs;
        while (LengthSquared(shading.dpdu) > 1e16f || LengthSquared(shading.dpdv) > 1e16f)
        {
            shading.dpdu /= 1e8f;
            shading.dpdv /= 1e8f;
        }
    }

    public void ComputeDifferentials(in RayDifferential ray, Camera camera, int samplesPerPixel)
    {
        if (GetOptions().disableTextureFiltering)
        {
            dudx = dudy = dvdx = dvdy = 0;
            dpdx = dpdy = new Vector3f(0, 0, 0);
            return;
        }
        if (ray.hasDifferentials && Dot(n, ray.rxDirection) != 0 && Dot(n, ray.ryDirection) != 0)
        {
            // Estimate screen-space change in $\pt{}$ using ray differentials
            // Compute auxiliary intersection points with plane, _px_ and _py_
            Float d = -Dot(n, new Vector3f(p()));
            Float tx = (-Dot(n, new Vector3f(ray.rxOrigin)) - d) / Dot(n, ray.rxDirection);
            DCHECK(!IsInf(tx) && !IsNaN(tx));
            Point3f px = ray.rxOrigin + tx * ray.rxDirection;
            Float ty = (-Dot(n, new Vector3f(ray.ryOrigin)) - d) / Dot(n, ray.ryDirection);
            DCHECK(!IsInf(ty) && !IsNaN(ty));
            Point3f py = ray.ryOrigin + ty * ray.ryDirection;
            dpdx = px - p();
            dpdy = py - p();
        }
        else
        {
            // Approximate screen-space change in $\pt{}$ based on camera projection
            camera.Approximate_dp_dxy(p(), n, time, samplesPerPixel, out dpdx, out dpdy);
        }

        ComputeUVDifferentials();
    }

    private void ComputeUVDifferentials()
    {
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

    public void SetIntersectionProperties(Material? mtl, Light? area, in MediumInterface? primMediumInterface,
                                          Medium? rayMedium)
    {
        material = mtl;
        areaLight = area;
        CHECK_GE(Dot(n, shading.n), 0f);
        // Set medium properties at surface intersection
        if (primMediumInterface is { } mi && mi.IsMediumTransition())
            mediumInterface = mi;
        else
            medium = rayMedium;
    }

    public readonly void SkipIntersection(ref RayDifferential ray, Float t)
    {
        Ray r = SpawnRay(ray.d);
        ray.o = r.o;
        ray.d = r.d;
        ray.time = r.time;
        ray.medium = r.medium;
        if (ray.hasDifferentials)
        {
            ray.rxOrigin = ray.rxOrigin + t * ray.rxDirection;
            ray.ryOrigin = ray.ryOrigin + t * ray.ryDirection;
        }
    }

    public readonly RayDifferential SpawnRay(in RayDifferential rayi, in BSDF bsdf, Vector3f wi, BxDFFlags flags,
                                             Float eta)
    {
        RayDifferential rd = SpawnRay(wi);
        if (rayi.hasDifferentials)
        {
            // Compute ray differentials for specular reflection or transmission
            // Compute common factors for specular ray differentials
            Normal3f n = shading.n;
            Normal3f dndx = shading.dndu * dudx + shading.dndv * dvdx;
            Normal3f dndy = shading.dndu * dudy + shading.dndv * dvdy;
            Vector3f dwodx = -rayi.rxDirection - wo, dwody = -rayi.ryDirection - wo;

            if (flags == BxDFFlags.SpecularReflection)
            {
                // Initialize origins of specular differential rays
                rd.hasDifferentials = true;
                rd.rxOrigin = p() + dpdx;
                rd.ryOrigin = p() + dpdy;

                // Compute differential reflected directions
                Float dwoDotn_dx = Dot(dwodx, n) + Dot(wo, dndx);
                Float dwoDotn_dy = Dot(dwody, n) + Dot(wo, dndy);
                rd.rxDirection = wi - dwodx + 2 * new Vector3f(Dot(wo, n) * dndx + dwoDotn_dx * n);
                rd.ryDirection = wi - dwody + 2 * new Vector3f(Dot(wo, n) * dndy + dwoDotn_dy * n);
            }
            else if (flags == BxDFFlags.SpecularTransmission)
            {
                // Initialize origins of specular differential rays
                rd.hasDifferentials = true;
                rd.rxOrigin = p() + dpdx;
                rd.ryOrigin = p() + dpdy;

                // Compute differential transmitted directions
                // Find oriented surface normal for transmission
                if (Dot(wo, n) < 0)
                {
                    n = -n;
                    dndx = -dndx;
                    dndy = -dndy;
                }

                // Compute partial derivatives of $\mu$
                Float dwoDotn_dx = Dot(dwodx, n) + Dot(wo, dndx);
                Float dwoDotn_dy = Dot(dwody, n) + Dot(wo, dndy);
                Float mu = Dot(wo, n) / eta - AbsDot(wi, n);
                Float dmudx = dwoDotn_dx * (1 / eta + 1 / Sqr(eta) * Dot(wo, n) / Dot(wi, n));
                Float dmudy = dwoDotn_dy * (1 / eta + 1 / Sqr(eta) * Dot(wo, n) / Dot(wi, n));

                rd.rxDirection = wi - eta * dwodx + new Vector3f(mu * dndx + dmudx * n);
                rd.ryDirection = wi - eta * dwody + new Vector3f(mu * dndy + dmudy * n);
            }
        }
        // Squash potentially troublesome differentials
        if (LengthSquared(rd.rxDirection) > 1e16f || LengthSquared(rd.ryDirection) > 1e16f ||
            LengthSquared(new Vector3f(rd.rxOrigin)) > 1e16f || LengthSquared(new Vector3f(rd.ryOrigin)) > 1e16f)
            rd.hasDifferentials = false;

        return rd;
    }

    public readonly SampledSpectrum Le(Vector3f w, in SampledWavelengths lambda) =>
        areaLight != null ? areaLight.L(p(), n, uv, w, lambda) : new SampledSpectrum(0f);

    // (pbrt's ScratchBuffer allocations are ordinary allocations here.)
    public BSDF GetBSDF(in RayDifferential ray, ref SampledWavelengths lambda, Camera? camera, Sampler sampler)
    {
        // Estimate $(u,v)$ and position differentials at intersection point
        ComputeDifferentials(ray, camera!, sampler.SamplesPerPixel());

        // Resolve _MixMaterial_ if necessary
        while (material is MixMaterial mix)
            material = mix.ChooseMaterial(new UniversalTextureEvaluator(), this);

        // Return unset _BSDF_ if surface has a null material
        if (material == null)
            return new BSDF();

        // Evaluate normal or bump map, if present
        FloatTexture? displacement = material.GetDisplacement();
        Image? normalMap = material.GetNormalMap();
        if (displacement != null || normalMap != null)
        {
            // Get shading $\dpdu$ and $\dpdv$ using normal or bump map
            Vector3f dpdu, dpdv;
            if (normalMap != null)
                NormalMap(normalMap, this, out dpdu, out dpdv);
            else
                BumpMap(new UniversalTextureEvaluator(), displacement!, this, out dpdu, out dpdv);

            Normal3f ns = new(Normalize(Cross(dpdu, dpdv)));
            SetShadingGeometry(ns, dpdu, dpdv, shading.dndu, shading.dndv, false);
        }

        // Return BSDF for surface interaction
        BSDF bsdf = material.GetBSDF(new UniversalTextureEvaluator(), this, ref lambda);
        if (!!bsdf && GetOptions().forceDiffuse)
        {
            // Override _bsdf_ with diffuse equivalent
            // (GCC evaluates the arguments from right to left.)
            Point2f u2 = sampler.Get2D();
            Float uc = sampler.Get1D();
            SampledSpectrum r = bsdf.rho(wo, [uc], [u2]);
            bsdf = new BSDF(shading.n, shading.dpdu, new DiffuseBxDF(r));
        }
        return bsdf;
    }

    public BSSRDF? GetBSSRDF(in RayDifferential ray, ref SampledWavelengths lambda, Camera? camera)
    {
        // Resolve _MixMaterial_ if necessary
        while (material is MixMaterial mix)
            material = mix.ChooseMaterial(new UniversalTextureEvaluator(), this);

        return material!.GetBSSRDF(new UniversalTextureEvaluator(), this, ref lambda);
    }

    public override readonly string ToString() =>
        $"[ SurfaceInteraction pi: {pi} n: {n} uv: {uv} wo: {wo} time: {FloatToString(time)} " +
        $"medium: {medium?.ToString() ?? "(nullptr)"} mediumInterface: {mediumInterface?.ToString() ?? "(nullptr)"} " +
        $"dpdu: {dpdu} dpdv: {dpdv} dndu: {dndu} dndv: {dndv} shading.n: {shading.n} shading.dpdu: {shading.dpdu} " +
        $"shading.dpdv: {shading.dpdv} shading.dndu: {shading.dndu} shading.dndv: {shading.dndv} " +
        $"material: {material?.ToString() ?? "(nullptr)"} areaLight: {areaLight?.ToString() ?? "(nullptr)"} dpdx: {dpdx} dpdy: {dpdy} dudx: {FloatToString(dudx)} " +
        $"dvdx: {FloatToString(dvdx)} dudy: {FloatToString(dudy)} dvdy: {FloatToString(dvdy)} faceIndex: {faceIndex} ]";
}

public sealed partial class Transform
{
    public Interaction Apply(in Interaction @in)
    {
        Interaction ret = new() { pi = Apply(@in.pi), n = Apply(@in.n) };
        if (LengthSquared(ret.n) > 0)
            ret.n = Normalize(ret.n);
        ret.uv = @in.uv;
        ret.wo = Apply(@in.wo);
        if (LengthSquared(ret.wo) > 0)
            ret.wo = Normalize(ret.wo);
        ret.time = @in.time;
        ret.mediumInterface = @in.mediumInterface;
        return ret;
    }

    public Interaction ApplyInverse(in Interaction @in)
    {
        Transform t = Inverse(this);
        Interaction ret = new() { pi = t.Apply(@in.pi), n = t.Apply(@in.n) };
        if (LengthSquared(ret.n) > 0)
            ret.n = Normalize(ret.n);
        ret.uv = @in.uv;
        ret.wo = t.Apply(@in.wo);
        if (LengthSquared(ret.wo) > 0)
            ret.wo = Normalize(ret.wo);
        ret.time = @in.time;
        ret.mediumInterface = @in.mediumInterface;
        return ret;
    }

    public SurfaceInteraction Apply(in SurfaceInteraction si) => Apply(this, this, si);

    // (pbrt's ApplyInverse(SurfaceInteraction) transforms _pi_ with this
    // transformation, not its inverse.)
    public SurfaceInteraction ApplyInverse(in SurfaceInteraction si) => Apply(this, Inverse(this), si);

    private static SurfaceInteraction Apply(Transform tp, Transform t, in SurfaceInteraction si)
    {
        SurfaceInteraction ret = new()
        {
            pi = tp.Apply(si.pi),
            // Transform remaining members of _SurfaceInteraction_
            n = Normalize(t.Apply(si.n)),
            wo = Normalize(t.Apply(si.wo)),
            time = si.time,
            mediumInterface = si.mediumInterface,
            uv = si.uv,
            dpdu = t.Apply(si.dpdu),
            dpdv = t.Apply(si.dpdv),
            dndu = t.Apply(si.dndu),
            dndv = t.Apply(si.dndv),
        };
        ret.shading.n = Normalize(t.Apply(si.shading.n));
        ret.shading.dpdu = t.Apply(si.shading.dpdu);
        ret.shading.dpdv = t.Apply(si.shading.dpdv);
        ret.shading.dndu = t.Apply(si.shading.dndu);
        ret.shading.dndv = t.Apply(si.shading.dndv);
        ret.dudx = si.dudx;
        ret.dvdx = si.dvdx;
        ret.dudy = si.dudy;
        ret.dvdy = si.dvdy;
        ret.dpdx = t.Apply(si.dpdx);
        ret.dpdy = t.Apply(si.dpdy);
        //    ret.n = FaceForward(ret.n, ret.shading.n);
        ret.shading.n = FaceForward(ret.shading.n, ret.n);
        ret.material = si.material;
        ret.areaLight = si.areaLight;
        ret.faceIndex = si.faceIndex;

        return ret;
    }
}

public sealed partial class AnimatedTransform
{
    public Interaction Apply(in Interaction it) =>
        !IsAnimated() ? startTransform.Apply(it) : Interpolate(it.time).Apply(it);

    public Interaction ApplyInverse(in Interaction it) =>
        !IsAnimated() ? startTransform.ApplyInverse(it) : Interpolate(it.time).ApplyInverse(it);

    public SurfaceInteraction Apply(in SurfaceInteraction it) =>
        !IsAnimated() ? startTransform.Apply(it) : Interpolate(it.time).Apply(it);

    public SurfaceInteraction ApplyInverse(in SurfaceInteraction it) =>
        !IsAnimated() ? startTransform.ApplyInverse(it) : Interpolate(it.time).ApplyInverse(it);
}

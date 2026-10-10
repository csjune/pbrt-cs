// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/shapes.h, shapes.cpp and base/shape.h
//
// Shape is an abstract class (pbrt's TaggedPointer). pstd::optional results
// are nullable structs. Triangles and bilinear patches keep a reference to
// their mesh as well as pbrt's index into the list of all meshes.

using System.Diagnostics;

namespace Pbrt;

// ShapeSample Definition
public struct ShapeSample
{
    public Interaction intr;
    public Float pdf;

    public ShapeSample(in Interaction intr, Float pdf)
    {
        this.intr = intr;
        this.pdf = pdf;
    }

    public override readonly string ToString() => $"[ ShapeSample intr: {intr} pdf: {FloatToString(pdf)} ]";
}

// ShapeSampleContext Definition
public struct ShapeSampleContext
{
    public Point3fi pi;
    public Normal3f n, ns;
    public Float time;

    // ShapeSampleContext Public Methods
    public ShapeSampleContext(Point3fi pi, Normal3f n, Normal3f ns, Float time)
    {
        this.pi = pi;
        this.n = n;
        this.ns = ns;
        this.time = time;
    }

    public ShapeSampleContext(in SurfaceInteraction si)
    {
        pi = si.pi;
        n = si.n;
        ns = si.shading.n;
        time = si.time;
    }

    public ShapeSampleContext(in MediumInteraction mi)
    {
        pi = mi.pi;
        time = mi.time;
    }

    public readonly Point3f p() => new(pi);

    // ShapeSampleContext Inline Methods
    public readonly Point3f OffsetRayOrigin(Vector3f w)
    {
        // Find vector _offset_ to corner of error bounds and compute initial _po_
        Float d = Dot(Abs(n), pi.Error());
        Vector3f offset = d * new Vector3f(n);
        if (Dot(w, n) < 0)
            offset = -offset;
        Point3f po = new Point3f(pi) + offset;

        // Round offset point _po_ away from _p_
        for (int i = 0; i < 3; ++i)
        {
            if (offset[i] > 0)
                po[i] = NextFloatUp(po[i]);
            else if (offset[i] < 0)
                po[i] = NextFloatDown(po[i]);
        }

        return po;
    }

    public readonly Point3f OffsetRayOrigin(Point3f pt) => OffsetRayOrigin(pt - p());

    // Note: doesn't set medium, but that's fine, since this is only
    // used by shapes to see if ray would have intersected them
    public readonly Ray SpawnRay(Vector3f w) => new(OffsetRayOrigin(w), w, time);
}

// ShapeIntersection Definition
public struct ShapeIntersection
{
    public SurfaceInteraction intr;
    public Float tHit;

    public ShapeIntersection(in SurfaceInteraction intr, Float tHit)
    {
        this.intr = intr;
        this.tHit = tHit;
    }

    public override readonly string ToString() => $"[ ShapeIntersection intr: {intr} tHit: {FloatToString(tHit)} ]";
}

// QuadricIntersection Definition
public struct QuadricIntersection
{
    public Float tHit;
    public Point3f pObj;
    public Float phi;

    public QuadricIntersection(Float tHit, Point3f pObj, Float phi)
    {
        this.tHit = tHit;
        this.pObj = pObj;
        this.phi = phi;
    }
}

// Shape Definition
public abstract class Shape
{
    private static readonly StatCounter nSpheres = new("Geometry/Spheres");
    private static readonly StatCounter nCylinders = new("Geometry/Cylinders");
    private static readonly StatCounter nDisks = new("Geometry/Disks");
    private static readonly StatCounter displacedTrisDelta = new("Geometry/Triangles added from displacement mapping");

    // Shape Interface
    public abstract Bounds3f Bounds();
    public abstract DirectionCone NormalBounds();
    public abstract ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity);
    public abstract bool IntersectP(in Ray ray, Float tMax = Infinity);
    public abstract Float Area();
    public abstract ShapeSample? Sample(Point2f u);
    public abstract Float PDF(in Interaction intr);
    public abstract ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u);
    public abstract Float PDF(in ShapeSampleContext ctx, Vector3f wi);

    public static List<Shape> Create(string name, Transform renderFromObject, Transform objectFromRender,
                                     bool reverseOrientation, ParameterDictionary parameters,
                                     IReadOnlyDictionary<string, FloatTexture> floatTextures, FileLoc? loc)
    {
        List<Shape> shapes = [];
        if (name == "sphere")
        {
            shapes = [Sphere.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc)];
            nSpheres.Inc();
        }
        // Create remaining single _Shape_ types
        else if (name == "cylinder")
        {
            shapes = [Cylinder.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc)];
            nCylinders.Inc();
        }
        else if (name == "disk")
        {
            shapes = [Disk.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc)];
            nDisks.Inc();
        }
        else if (name == "bilinearmesh")
        {
            BilinearPatchMesh? mesh = BilinearPatch.CreateMesh(renderFromObject, reverseOrientation, parameters, loc);
            // (pbrt passes a null mesh on; CreatePatches() would then crash.)
            if (mesh != null)
                shapes = BilinearPatch.CreatePatches(mesh);
        }
        // Create multiple-_Shape_ types
        else if (name == "curve")
            shapes = Curve.Create(renderFromObject, objectFromRender, reverseOrientation, parameters, loc);
        else if (name == "trianglemesh")
        {
            TriangleMesh? mesh = Triangle.CreateMesh(renderFromObject, reverseOrientation, parameters, loc);
            if (mesh != null)
                shapes = Triangle.CreateTriangles(mesh);
        }
        else if (name == "plymesh")
        {
            string filename = ResolveFilename(parameters.GetOneString("filename", ""));
            TriQuadMesh plyMesh = TriQuadMesh.ReadPLY(filename);

            Float edgeLength = parameters.GetOneFloat("edgelength", 1f);
            edgeLength *= Options.displacementEdgeScale;

            string displacementTexName = parameters.GetTexture("displacement");
            if (displacementTexName.Length > 0)
            {
                if (!floatTextures.TryGetValue(displacementTexName, out FloatTexture? displacement))
                    ErrorExit(loc, $"{displacementTexName}: no such texture defined.");

                LOG_VERBOSE($"Starting to displace mesh \"{filename}\" with \"{displacementTexName}\"");

                int origTriCount = plyMesh.triIndices.Count / 3;
                plyMesh = plyMesh.Displace(
                    (v0, v1) =>
                    {
                        v0 = renderFromObject.Apply(v0);
                        v1 = renderFromObject.Apply(v1);
                        return Distance(v0, v1);
                    },
                    edgeLength,
                    (p, n, uv, nVertices) =>
                    {
                        ParallelFor(0, nVertices, (long i) =>
                        {
                            TextureEvalContext ctx = new() { p = p[(int)i], uv = uv[(int)i] };
                            Float d = new UniversalTextureEvaluator().Evaluate(displacement!, ctx);
                            p[(int)i] += new Vector3f(d * n[(int)i]);
                        });
                    },
                    loc);

                LOG_VERBOSE($"Finished displacing mesh \"{filename}\" with \"{displacementTexName}\" -> " +
                            $"{plyMesh.triIndices.Count / 3} tris");

                displacedTrisDelta.Add(plyMesh.triIndices.Count / 3 - origTriCount);
            }

            if (plyMesh.triIndices.Count > 0)
            {
                TriangleMesh mesh = new(renderFromObject, reverseOrientation, plyMesh.triIndices, plyMesh.p, null,
                                        plyMesh.n, plyMesh.uv, plyMesh.faceIndices);
                shapes = Triangle.CreateTriangles(mesh);
            }

            if (plyMesh.quadIndices.Count > 0)
            {
                BilinearPatchMesh mesh = new(renderFromObject, reverseOrientation, plyMesh.quadIndices, plyMesh.p,
                                             plyMesh.n, plyMesh.uv, plyMesh.faceIndices, null /* image dist */);
                shapes.AddRange(BilinearPatch.CreatePatches(mesh));
            }
        }
        else if (name == "loopsubdiv")
        {
            int nLevels = parameters.GetOneInt("levels", 3);
            List<int> vertexIndices = parameters.GetIntArray("indices");
            if (vertexIndices.Count == 0)
                ErrorExit(loc, "Vertex indices \"indices\" not provided for " + "LoopSubdiv shape.");

            List<Point3f> P = parameters.GetPoint3fArray("P");
            if (P.Count == 0)
                ErrorExit(loc, "Vertex positions \"P\" not provided for LoopSubdiv shape.");

            // don't actually use this for now...
            string scheme = parameters.GetOneString("scheme", "loop");

            TriangleMesh mesh = LoopSubdivide(renderFromObject, reverseOrientation, nLevels,
                                              vertexIndices.ToArray(), P.ToArray());

            shapes = Triangle.CreateTriangles(mesh);
        }
        else
            ErrorExit(loc, $"{name}: shape type unknown.");

        if (shapes.Count == 0)
            ErrorExit(loc, $"{name}: unable to create shape.");

        return shapes;
    }

    // Shared by the shapes that sample by area.
    protected ShapeSample? SampleByArea(in ShapeSampleContext ctx, Point2f u)
    {
        // Sample shape by area and compute incident direction _wi_
        ShapeSample? sso = Sample(u);
        DCHECK(sso.HasValue);
        ShapeSample ss = sso!.Value;
        ss.intr.time = ctx.time;
        Vector3f wi = ss.intr.p() - ctx.p();
        if (LengthSquared(wi) == 0)
            return null;
        wi = Normalize(wi);

        // Convert area sampling PDF in _ss_ to solid angle measure
        ss.pdf /= AbsDot(ss.intr.n, -wi) / DistanceSquared(ctx.p(), ss.intr.p());
        if (IsInf(ss.pdf))
            return null;

        return ss;
    }

    protected Float PDFByArea(in ShapeSampleContext ctx, Vector3f wi)
    {
        // Intersect sample ray with shape geometry
        Ray ray = ctx.SpawnRay(wi);
        ShapeIntersection? isect = Intersect(ray);
        if (!isect.HasValue)
            return 0;

        // Compute PDF in solid angle measure from shape intersection point
        Float pdf = (1 / Area()) / (AbsDot(isect.Value.intr.n, -wi) /
                                    DistanceSquared(ctx.p(), isect.Value.intr.p()));
        if (IsInf(pdf))
            pdf = 0;

        return pdf;
    }
}

// Sphere Definition
public sealed class Sphere : Shape
{
    // Sphere Private Members
    private readonly Float radius;
    private readonly Float zMin, zMax;
    private readonly Float thetaZMin, thetaZMax, phiMax;
    private readonly Transform renderFromObject, objectFromRender;
    private readonly bool reverseOrientation, transformSwapsHandedness;

    // Sphere Public Methods
    public static Sphere Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                ParameterDictionary parameters, FileLoc? loc)
    {
        Float radius = parameters.GetOneFloat("radius", 1f);
        Float zmin = parameters.GetOneFloat("zmin", -radius);
        Float zmax = parameters.GetOneFloat("zmax", radius);
        Float phimax = parameters.GetOneFloat("phimax", 360f);
        return new Sphere(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
    }

    public Sphere(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float radius,
                  Float zMin, Float zMax, Float phiMax)
    {
        this.renderFromObject = renderFromObject;
        this.objectFromRender = objectFromRender;
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();
        this.radius = radius;
        this.zMin = Clamp(FMath.Min(zMin, zMax), -radius, radius);
        this.zMax = Clamp(FMath.Max(zMin, zMax), -radius, radius);
        thetaZMin = FMath.Acos(Clamp(FMath.Min(zMin, zMax) / radius, -1, 1));
        thetaZMax = FMath.Acos(Clamp(FMath.Max(zMin, zMax) / radius, -1, 1));
        this.phiMax = Radians(Clamp(phiMax, 0, 360));
    }

    public override string ToString() =>
        $"[ Sphere renderFromObject: {renderFromObject} objectFromRender: {objectFromRender} " +
        $"reverseOrientation: {B(reverseOrientation)} transformSwapsHandedness: {B(transformSwapsHandedness)} " +
        $"radius: {FloatToString(radius)} zMin: {FloatToString(zMin)} zMax: {FloatToString(zMax)} " +
        $"thetaMin: {FloatToString(thetaZMin)} thetaMax: {FloatToString(thetaZMax)} phiMax: {FloatToString(phiMax)} ]";

    internal static string B(bool b) => b ? "true" : "false";

    // Sphere Method Definitions
    public override Bounds3f Bounds() =>
        renderFromObject.Apply(new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));

    public override DirectionCone NormalBounds() => DirectionCone.EntireSphere();

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        QuadricIntersection? isect = BasicIntersect(ray, tMax);
        if (!isect.HasValue)
            return null;
        SurfaceInteraction intr = InteractionFromIntersection(isect.Value, -ray.d, ray.time);
        return new ShapeIntersection(intr, isect.Value.tHit);
    }

    public QuadricIntersection? BasicIntersect(in Ray r, Float tMax)
    {
        Float phi;
        Point3f pHit;
        // Transform _Ray_ origin and direction to object space
        Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
        Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

        // Solve quadratic equation to compute sphere _t0_ and _t1_
        Interval t0, t1;
        // Compute sphere quadratic coefficients
        Interval a = Sqr(di.x) + Sqr(di.y) + Sqr(di.z);
        Interval b = 2 * (di.x * oi.x + di.y * oi.y + di.z * oi.z);
        Interval c = Sqr(oi.x) + Sqr(oi.y) + Sqr(oi.z) - Sqr(new Interval(radius));

        // Compute sphere quadratic discriminant _discrim_
        Vector3fi v = new(oi - b / (2 * a) * di);
        Interval length = Length(v);
        Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
        if (discrim.LowerBound < 0)
            return null;

        // Compute quadratic $t$ values
        Interval rootDiscrim = Sqrt(discrim);
        Interval q;
        if ((Float)b < 0)
            q = -.5f * (b - rootDiscrim);
        else
            q = -.5f * (b + rootDiscrim);
        t0 = q / a;
        t1 = c / q;
        // Swap quadratic $t$ values so that _t0_ is the lesser
        if (t0.LowerBound > t1.LowerBound)
            (t0, t1) = (t1, t0);

        // Check quadric shape _t0_ and _t1_ for nearest intersection
        if (t0.UpperBound > tMax || t1.LowerBound <= 0)
            return null;
        Interval tShapeHit = t0;
        if (tShapeHit.LowerBound <= 0)
        {
            tShapeHit = t1;
            if (tShapeHit.UpperBound > tMax)
                return null;
        }

        // Compute sphere hit position and $\phi$
        pHit = new Point3f(oi) + (Float)tShapeHit * new Vector3f(di);
        // Refine sphere intersection point
        pHit *= radius / Distance(pHit, new Point3f(0, 0, 0));

        if (pHit.x == 0 && pHit.y == 0)
            pHit.x = 1e-5f * radius;
        phi = FMath.Atan2(pHit.y, pHit.x);
        if (phi < 0)
            phi += 2 * Pi;

        // Test sphere intersection against clipping parameters
        if ((zMin > -radius && pHit.z < zMin) || (zMax < radius && pHit.z > zMax) || phi > phiMax)
        {
            if (tShapeHit == t1)
                return null;
            if (t1.UpperBound > tMax)
                return null;
            tShapeHit = t1;
            // Compute sphere hit position and $\phi$
            pHit = new Point3f(oi) + (Float)tShapeHit * new Vector3f(di);
            // Refine sphere intersection point
            pHit *= radius / Distance(pHit, new Point3f(0, 0, 0));

            if (pHit.x == 0 && pHit.y == 0)
                pHit.x = 1e-5f * radius;
            phi = FMath.Atan2(pHit.y, pHit.x);
            if (phi < 0)
                phi += 2 * Pi;

            if ((zMin > -radius && pHit.z < zMin) || (zMax < radius && pHit.z > zMax) || phi > phiMax)
                return null;
        }

        // Return _QuadricIntersection_ for sphere intersection
        return new QuadricIntersection((Float)tShapeHit, pHit, phi);
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity) => BasicIntersect(r, tMax).HasValue;

    public SurfaceInteraction InteractionFromIntersection(in QuadricIntersection isect, Vector3f wo, Float time)
    {
        Point3f pHit = isect.pObj;
        Float phi = isect.phi;
        // Find parametric representation of sphere hit
        Float u = phi / phiMax;
        Float cosTheta = pHit.z / radius;
        Float theta = SafeACos(cosTheta);
        Float v = (theta - thetaZMin) / (thetaZMax - thetaZMin);
        // Compute sphere $\dpdu$ and $\dpdv$
        Float zRadius = FMath.Sqrt(Sqr(pHit.x) + Sqr(pHit.y));
        Float cosPhi = pHit.x / zRadius, sinPhi = pHit.y / zRadius;
        Vector3f dpdu = new(-phiMax * pHit.y, phiMax * pHit.x, 0);
        Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
        Vector3f dpdv = (thetaZMax - thetaZMin) * new Vector3f(pHit.z * cosPhi, pHit.z * sinPhi, -radius * sinTheta);

        // Compute sphere $\dndu$ and $\dndv$
        Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
        Vector3f d2Pduv = (thetaZMax - thetaZMin) * pHit.z * phiMax * new Vector3f(-sinPhi, cosPhi, 0);
        Vector3f d2Pdvv = -Sqr(thetaZMax - thetaZMin) * new Vector3f(pHit.x, pHit.y, pHit.z);
        // Compute coefficients for fundamental forms
        Float E = Dot(dpdu, dpdu), F = Dot(dpdu, dpdv), G = Dot(dpdv, dpdv);
        Vector3f n = Normalize(Cross(dpdu, dpdv));
        Float e = Dot(n, d2Pduu), f = Dot(n, d2Pduv), g = Dot(n, d2Pdvv);

        // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
        Float EGF2 = DifferenceOfProducts(E, G, F, F);
        Float invEGF2 = (EGF2 == 0) ? 0 : 1 / EGF2;
        Normal3f dndu = new((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
        Normal3f dndv = new((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

        // Compute error bounds for sphere intersection
        Vector3f pError = Gamma(5) * Abs(new Vector3f(pHit));

        // Return _SurfaceInteraction_ for quadric intersection
        bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
        Vector3f woObject = objectFromRender.Apply(wo);
        return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v), woObject,
                                                             dpdu, dpdv, dndu, dndv, time, flipNormal));
    }

    public override Float Area() => phiMax * radius * (zMax - zMin);

    public override ShapeSample? Sample(Point2f u)
    {
        Point3f pObj = new Point3f(0, 0, 0) + radius * SampleUniformSphere(u);
        // Reproject _pObj_ to sphere surface and compute _pObjError_
        pObj *= radius / Distance(pObj, new Point3f(0, 0, 0));
        Vector3f pObjError = Gamma(5) * Abs(new Vector3f(pObj));

        // Compute surface normal for sphere sample and return _ShapeSample_
        Normal3f nObj = new(pObj.x, pObj.y, pObj.z);
        Normal3f n = Normalize(renderFromObject.Apply(nObj));
        if (reverseOrientation)
            n *= -1;
        // Compute $(u, v)$ coordinates for sphere sample
        Float theta = SafeACos(pObj.z / radius);
        Float phi = FMath.Atan2(pObj.y, pObj.x);
        if (phi < 0)
            phi += 2 * Pi;
        Point2f uv = new(phi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

        Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
        return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
    }

    public override Float PDF(in Interaction intr) => 1 / Area();

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u)
    {
        // Sample uniformly on sphere if $\pt{}$ is inside it
        Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
        Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
        if (DistanceSquared(pOrigin, pCenter) <= Sqr(radius))
        {
            // Sample shape by area and compute incident direction _wi_
            ShapeSample? sso = Sample(u);
            DCHECK(sso.HasValue);
            ShapeSample ss = sso!.Value;
            ss.intr.time = ctx.time;
            Vector3f wi = ss.intr.p() - ctx.p();
            if (LengthSquared(wi) == 0)
                return null;
            wi = Normalize(wi);

            // Convert area sampling PDF in _ss_ to solid angle measure
            ss.pdf /= AbsDot(ss.intr.n, -wi) / DistanceSquared(ctx.p(), ss.intr.p());
            if (IsInf(ss.pdf))
                return null;

            return ss;
        }

        // Sample sphere uniformly inside subtended cone
        // Compute quantities related to the $\theta_\roman{max}$ for cone
        Float sinThetaMax = radius / Distance(ctx.p(), pCenter);
        Float sin2ThetaMax = Sqr(sinThetaMax);
        Float cosThetaMax = SafeSqrt(1 - sin2ThetaMax);
        Float oneMinusCosThetaMax = 1 - cosThetaMax;

        // Compute $\theta$ and $\phi$ values for sample in cone
        Float cosTheta = (cosThetaMax - 1) * u[0] + 1;
        Float sin2Theta = 1 - Sqr(cosTheta);
        if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
        {
            // Compute cone sample via Taylor series expansion for small angles
            sin2Theta = sin2ThetaMax * u[0];
            cosTheta = FMath.Sqrt(1 - sin2Theta);
            oneMinusCosThetaMax = sin2ThetaMax / 2;
        }

        // Compute angle $\alpha$ from center of sphere to sampled point on surface
        Float cosAlpha = sin2Theta / sinThetaMax + cosTheta * SafeSqrt(1 - sin2Theta / Sqr(sinThetaMax));
        Float sinAlpha = SafeSqrt(1 - Sqr(cosAlpha));

        // Compute surface normal and sampled point on sphere
        Float phi = u[1] * 2 * Pi;
        Vector3f w = SphericalDirection(sinAlpha, cosAlpha, phi);
        Frame samplingFrame = Frame.FromZ(Normalize(pCenter - ctx.p()));
        Normal3f n = new(samplingFrame.FromLocal(-w));
        Point3f p = pCenter + radius * new Point3f(n.x, n.y, n.z);
        if (reverseOrientation)
            n *= -1;

        // Return _ShapeSample_ for sampled point on sphere
        // Compute _pError_ for sampled point on sphere
        Vector3f pError = Gamma(5) * Abs(new Vector3f(p));

        // Compute $(u,v)$ coordinates for sampled point on sphere
        Point3f pObj = objectFromRender.Apply(p);
        Float theta = SafeACos(pObj.z / radius);
        Float spherePhi = FMath.Atan2(pObj.y, pObj.x);
        if (spherePhi < 0)
            spherePhi += 2 * Pi;
        Point2f uv = new(spherePhi / phiMax, (theta - thetaZMin) / (thetaZMax - thetaZMin));

        DCHECK_NE(oneMinusCosThetaMax, 0); // very small far away sphere
        return new ShapeSample(new Interaction(new Point3fi(p, pError), n, ctx.time, uv),
                               1 / (2 * Pi * oneMinusCosThetaMax));
    }

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi)
    {
        Point3f pCenter = renderFromObject.Apply(new Point3f(0, 0, 0));
        Point3f pOrigin = ctx.OffsetRayOrigin(pCenter);
        if (DistanceSquared(pOrigin, pCenter) <= Sqr(radius))
        {
            // Return solid angle PDF for point inside sphere
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            ShapeIntersection? isect = Intersect(ray);
            if (!isect.HasValue)
                return 0;

            // Compute PDF in solid angle measure from shape intersection point
            Float pdf = (1 / Area()) / (AbsDot(isect.Value.intr.n, -wi) /
                                        DistanceSquared(ctx.p(), isect.Value.intr.p()));
            if (IsInf(pdf))
                pdf = 0;

            return pdf;
        }
        // Compute general solid angle sphere PDF
        Float sin2ThetaMax = radius * radius / DistanceSquared(ctx.p(), pCenter);
        Float cosThetaMax = SafeSqrt(1 - sin2ThetaMax);
        Float oneMinusCosThetaMax = 1 - cosThetaMax;
        // Compute more accurate _oneMinusCosThetaMax_ for small solid angle
        if (sin2ThetaMax < 0.00068523f /* sin^2(1.5 deg) */)
            oneMinusCosThetaMax = sin2ThetaMax / 2;

        return 1 / (2 * Pi * oneMinusCosThetaMax);
    }
}

// Disk Definition
public sealed class Disk : Shape
{
    // Disk Private Members
    private readonly Transform renderFromObject, objectFromRender;
    private readonly bool reverseOrientation, transformSwapsHandedness;
    private readonly Float height, radius, innerRadius, phiMax;

    // Disk Public Methods
    public Disk(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float height,
                Float radius, Float innerRadius, Float phiMax)
    {
        this.renderFromObject = renderFromObject;
        this.objectFromRender = objectFromRender;
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();
        this.height = height;
        this.radius = radius;
        this.innerRadius = innerRadius;
        this.phiMax = Radians(Clamp(phiMax, 0, 360));
    }

    public static Disk Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                              ParameterDictionary parameters, FileLoc? loc)
    {
        Float height = parameters.GetOneFloat("height", 0);
        Float radius = parameters.GetOneFloat("radius", 1);
        Float innerRadius = parameters.GetOneFloat("innerradius", 0);
        Float phimax = parameters.GetOneFloat("phimax", 360);
        return new Disk(renderFromObject, objectFromRender, reverseOrientation, height, radius, innerRadius, phimax);
    }

    public override string ToString() =>
        $"[ Disk renderFromObject: {renderFromObject} objectFromRender: {objectFromRender} " +
        $"reverseOrientation: {Sphere.B(reverseOrientation)} transformSwapsHandedness: {Sphere.B(transformSwapsHandedness)} " +
        $"height: {FloatToString(height)} radius: {FloatToString(radius)} innerRadius: {FloatToString(innerRadius)} " +
        $"phiMax: {FloatToString(phiMax)} ]";

    public override Float Area() => phiMax * 0.5f * (Sqr(radius) - Sqr(innerRadius));

    // Disk Method Definitions
    public override Bounds3f Bounds() =>
        renderFromObject.Apply(new Bounds3f(new Point3f(-radius, -radius, height), new Point3f(radius, radius, height)));

    public override DirectionCone NormalBounds()
    {
        Normal3f n = renderFromObject.Apply(new Normal3f(0, 0, 1));
        if (reverseOrientation)
            n = -n;
        return new DirectionCone(new Vector3f(n));
    }

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        QuadricIntersection? isect = BasicIntersect(ray, tMax);
        if (!isect.HasValue)
            return null;
        SurfaceInteraction intr = InteractionFromIntersection(isect.Value, -ray.d, ray.time);
        return new ShapeIntersection(intr, isect.Value.tHit);
    }

    public QuadricIntersection? BasicIntersect(in Ray r, Float tMax)
    {
        // Transform _Ray_ origin and direction to object space
        Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
        Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

        // Compute plane intersection for disk
        // Reject disk intersections for rays parallel to the disk's plane
        if ((Float)di.z == 0)
            return null;

        Float tShapeHit = (height - (Float)oi.z) / (Float)di.z;
        if (tShapeHit <= 0 || tShapeHit >= tMax)
            return null;

        // See if hit point is inside disk radii and $\phimax$
        Point3f pHit = new Point3f(oi) + tShapeHit * new Vector3f(di);
        Float dist2 = Sqr(pHit.x) + Sqr(pHit.y);
        if (dist2 > Sqr(radius) || dist2 < Sqr(innerRadius))
            return null;
        // Test disk $\phi$ value against $\phimax$
        Float phi = FMath.Atan2(pHit.y, pHit.x);
        if (phi < 0)
            phi += 2 * Pi;
        if (phi > phiMax)
            return null;

        // Return _QuadricIntersection_ for disk intersection
        return new QuadricIntersection(tShapeHit, pHit, phi);
    }

    public SurfaceInteraction InteractionFromIntersection(in QuadricIntersection isect, Vector3f wo, Float time)
    {
        Point3f pHit = isect.pObj;
        Float phi = isect.phi;
        // Find parametric representation of disk hit
        Float u = phi / phiMax;
        Float rHit = FMath.Sqrt(Sqr(pHit.x) + Sqr(pHit.y));
        Float v = (radius - rHit) / (radius - innerRadius);
        Vector3f dpdu = new(-phiMax * pHit.y, phiMax * pHit.x, 0);
        Vector3f dpdv = new Vector3f(pHit.x, pHit.y, 0) * (innerRadius - radius) / rHit;
        Normal3f dndu = new(0, 0, 0), dndv = new(0, 0, 0);

        // Refine disk intersection point
        pHit.z = height;

        // Compute error bounds for disk intersection
        Vector3f pError = new(0, 0, 0);

        // Return _SurfaceInteraction_ for quadric intersection
        bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
        Vector3f woObject = objectFromRender.Apply(wo);
        return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v), woObject,
                                                             dpdu, dpdv, dndu, dndv, time, flipNormal));
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity) => BasicIntersect(r, tMax).HasValue;

    public override ShapeSample? Sample(Point2f u)
    {
        Point2f pd = SampleUniformDiskConcentric(u);
        Point3f pObj = new(pd.x * radius, pd.y * radius, height);
        Point3fi pi = renderFromObject.Apply(new Point3fi(pObj));
        Normal3f n = Normalize(renderFromObject.Apply(new Normal3f(0, 0, 1)));

        if (reverseOrientation)
            n *= -1;
        // Compute $(u,v)$ for sampled point on disk
        Float phi = FMath.Atan2(pd.y, pd.x);
        if (phi < 0)
            phi += 2 * Pi;
        Float radiusSample = FMath.Sqrt(Sqr(pObj.x) + Sqr(pObj.y));
        Point2f uv = new(phi / phiMax, (radius - radiusSample) / (radius - innerRadius));

        return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
    }

    public override Float PDF(in Interaction intr) => 1 / Area();

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u) => SampleByArea(ctx, u);

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi) => PDFByArea(ctx, wi);
}

// Cylinder Definition
public sealed class Cylinder : Shape
{
    // Cylinder Private Members
    private readonly Transform renderFromObject, objectFromRender;
    private readonly bool reverseOrientation, transformSwapsHandedness;
    private readonly Float radius, zMin, zMax, phiMax;

    // Cylinder Inline Methods
    public Cylinder(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float radius,
                    Float zMin, Float zMax, Float phiMax)
    {
        this.renderFromObject = renderFromObject;
        this.objectFromRender = objectFromRender;
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();
        this.radius = radius;
        this.zMin = FMath.Min(zMin, zMax);
        this.zMax = FMath.Max(zMin, zMax);
        this.phiMax = Radians(Clamp(phiMax, 0, 360));
    }

    public static Cylinder Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                  ParameterDictionary parameters, FileLoc? loc)
    {
        Float radius = parameters.GetOneFloat("radius", 1);
        Float zmin = parameters.GetOneFloat("zmin", -1);
        Float zmax = parameters.GetOneFloat("zmax", 1);
        Float phimax = parameters.GetOneFloat("phimax", 360);
        return new Cylinder(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
    }

    // Cylinder Method Definitions
    public override Bounds3f Bounds() =>
        renderFromObject.Apply(new Bounds3f(new Point3f(-radius, -radius, zMin), new Point3f(radius, radius, zMax)));

    public override string ToString() =>
        $"[ Cylinder renderFromObject: {renderFromObject} objectFromRender: {objectFromRender} " +
        $"reverseOrientation: {Sphere.B(reverseOrientation)} transformSwapsHandedness: {Sphere.B(transformSwapsHandedness)} " +
        $"radius: {FloatToString(radius)} zMin: {FloatToString(zMin)} zMax: {FloatToString(zMax)} " +
        $"phiMax: {FloatToString(phiMax)} ]";

    public override Float Area() => (zMax - zMin) * radius * phiMax;

    public override DirectionCone NormalBounds() => DirectionCone.EntireSphere();

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        QuadricIntersection? isect = BasicIntersect(ray, tMax);
        if (!isect.HasValue)
            return null;
        SurfaceInteraction intr = InteractionFromIntersection(isect.Value, -ray.d, ray.time);
        return new ShapeIntersection(intr, isect.Value.tHit);
    }

    public QuadricIntersection? BasicIntersect(in Ray r, Float tMax)
    {
        Float phi;
        Point3f pHit;
        // Transform _Ray_ origin and direction to object space
        Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
        Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

        // Solve quadratic equation to find cylinder _t0_ and _t1_ values
        Interval t0, t1;
        // Compute cylinder quadratic coefficients
        Interval a = Sqr(di.x) + Sqr(di.y);
        Interval b = 2 * (di.x * oi.x + di.y * oi.y);
        Interval c = Sqr(oi.x) + Sqr(oi.y) - Sqr(new Interval(radius));

        // Compute cylinder quadratic discriminant _discrim_
        Interval f = b / (2 * a);
        Interval vx = oi.x - f * di.x, vy = oi.y - f * di.y;
        Interval length = Sqrt(Sqr(vx) + Sqr(vy));
        Interval discrim = 4 * a * (new Interval(radius) + length) * (new Interval(radius) - length);
        if (discrim.LowerBound < 0)
            return null;

        // Compute quadratic $t$ values
        Interval rootDiscrim = Sqrt(discrim);
        Interval q;
        if ((Float)b < 0)
            q = -.5f * (b - rootDiscrim);
        else
            q = -.5f * (b + rootDiscrim);
        t0 = q / a;
        t1 = c / q;
        // Swap quadratic $t$ values so that _t0_ is the lesser
        if (t0.LowerBound > t1.LowerBound)
            (t0, t1) = (t1, t0);

        // Check quadric shape _t0_ and _t1_ for nearest intersection
        if (t0.UpperBound > tMax || t1.LowerBound <= 0)
            return null;
        Interval tShapeHit = t0;
        if (tShapeHit.LowerBound <= 0)
        {
            tShapeHit = t1;
            if (tShapeHit.UpperBound > tMax)
                return null;
        }

        // Compute cylinder hit point and $\phi$
        pHit = new Point3f(oi) + (Float)tShapeHit * new Vector3f(di);
        // Refine cylinder intersection point
        Float hitRad = FMath.Sqrt(Sqr(pHit.x) + Sqr(pHit.y));
        pHit.x *= radius / hitRad;
        pHit.y *= radius / hitRad;

        phi = FMath.Atan2(pHit.y, pHit.x);
        if (phi < 0)
            phi += 2 * Pi;

        // Test cylinder intersection against clipping parameters
        if (pHit.z < zMin || pHit.z > zMax || phi > phiMax)
        {
            if (tShapeHit == t1)
                return null;
            tShapeHit = t1;
            if (t1.UpperBound > tMax)
                return null;
            // Compute cylinder hit point and $\phi$
            pHit = new Point3f(oi) + (Float)tShapeHit * new Vector3f(di);
            // Refine cylinder intersection point
            hitRad = FMath.Sqrt(Sqr(pHit.x) + Sqr(pHit.y));
            pHit.x *= radius / hitRad;
            pHit.y *= radius / hitRad;

            phi = FMath.Atan2(pHit.y, pHit.x);
            if (phi < 0)
                phi += 2 * Pi;

            if (pHit.z < zMin || pHit.z > zMax || phi > phiMax)
                return null;
        }

        // Return _QuadricIntersection_ for cylinder intersection
        return new QuadricIntersection((Float)tShapeHit, pHit, phi);
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity) => BasicIntersect(r, tMax).HasValue;

    public SurfaceInteraction InteractionFromIntersection(in QuadricIntersection isect, Vector3f wo, Float time)
    {
        Point3f pHit = isect.pObj;
        Float phi = isect.phi;
        // Find parametric representation of cylinder hit
        Float u = phi / phiMax;
        Float v = (pHit.z - zMin) / (zMax - zMin);
        // Compute cylinder $\dpdu$ and $\dpdv$
        Vector3f dpdu = new(-phiMax * pHit.y, phiMax * pHit.x, 0);
        Vector3f dpdv = new(0, 0, zMax - zMin);

        // Compute cylinder $\dndu$ and $\dndv$
        Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
        Vector3f d2Pduv = new(0, 0, 0), d2Pdvv = new(0, 0, 0);
        // Compute coefficients for fundamental forms
        Float E = Dot(dpdu, dpdu), F = Dot(dpdu, dpdv), G = Dot(dpdv, dpdv);
        Vector3f n = Normalize(Cross(dpdu, dpdv));
        Float e = Dot(n, d2Pduu), f = Dot(n, d2Pduv), g = Dot(n, d2Pdvv);

        // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
        Float EGF2 = DifferenceOfProducts(E, G, F, F);
        Float invEGF2 = (EGF2 == 0) ? 0 : 1 / EGF2;
        Normal3f dndu = new((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
        Normal3f dndv = new((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

        // Compute error bounds for cylinder intersection
        Vector3f pError = Gamma(3) * Abs(new Vector3f(pHit.x, pHit.y, 0));

        // Return _SurfaceInteraction_ for quadric intersection
        bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
        Vector3f woObject = objectFromRender.Apply(wo);
        return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), new Point2f(u, v), woObject,
                                                             dpdu, dpdv, dndu, dndv, time, flipNormal));
    }

    public override ShapeSample? Sample(Point2f u)
    {
        Float z = Lerp(u[0], zMin, zMax);
        Float phi = u[1] * phiMax;
        // Compute cylinder sample position _pi_ and normal _n_ from $z$ and $\phi$
        Point3f pObj = new(radius * FMath.Cos(phi), radius * FMath.Sin(phi), z);
        // Reproject _pObj_ to cylinder surface and compute _pObjError_
        Float hitRad = FMath.Sqrt(Sqr(pObj.x) + Sqr(pObj.y));
        pObj.x *= radius / hitRad;
        pObj.y *= radius / hitRad;
        Vector3f pObjError = Gamma(3) * Abs(new Vector3f(pObj.x, pObj.y, 0));

        Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, pObjError));
        Normal3f n = Normalize(renderFromObject.Apply(new Normal3f(pObj.x, pObj.y, 0)));
        if (reverseOrientation)
            n *= -1;

        Point2f uv = new(phi / phiMax, (pObj.z - zMin) / (zMax - zMin));
        return new ShapeSample(new Interaction(pi, n, uv), 1 / Area());
    }

    public override Float PDF(in Interaction intr) => 1 / Area();

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u) => SampleByArea(ctx, u);

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi) => PDFByArea(ctx, wi);
}

// TriangleIntersection Definition
public struct TriangleIntersection(Float b0, Float b1, Float b2, Float t)
{
    public Float b0 = b0, b1 = b1, b2 = b2;
    public Float t = t;

    public override readonly string ToString() =>
        $"[ TriangleIntersection b0: {FloatToString(b0)} b1: {FloatToString(b1)} b2: {FloatToString(b2)} " +
        $"t: {FloatToString(t)} ]";
}

// Triangle Definition
public sealed partial class Triangle : Shape
{
    private static readonly StatPixelRatio nTriHits = new("Intersections/Ray-Triangle intersection tests");
    private static readonly StatMemoryCounter triangleBytes = new("Memory/Triangles");

    // Triangle Private Members
    private readonly int meshIndex = -1, triIndex = -1;
    private readonly TriangleMesh mesh;
    private static readonly List<TriangleMesh> allMeshes = [];
    private const Float MinSphericalSampleArea = (Float)3e-4;
    private const Float MaxSphericalSampleArea = (Float)6.22;

    // Triangle Public Methods
    public Triangle(TriangleMesh mesh, int meshIndex, int triIndex)
    {
        this.mesh = mesh;
        this.meshIndex = meshIndex;
        this.triIndex = triIndex;
    }

    // Triangle Method Definitions
    public static List<Shape> CreateTriangles(TriangleMesh mesh)
    {
        int meshIndex;
        lock (allMeshes)
        {
            CHECK_LT(allMeshes.Count, int.MaxValue);
            meshIndex = allMeshes.Count;
            allMeshes.Add(mesh);
        }

        List<Shape> tris = new(mesh.nTriangles);
        for (int i = 0; i < mesh.nTriangles; ++i)
            tris.Add(new Triangle(mesh, meshIndex, i));
        // (sizeof(Triangle) in pbrt)
        triangleBytes.Add(mesh.nTriangles * 8L);
        return tris;
    }

    // Get triangle vertices in _p0_, _p1_, and _p2_
    private void GetVertices(out Point3f p0, out Point3f p1, out Point3f p2)
    {
        int v = 3 * triIndex;
        p0 = mesh.p[mesh.vertexIndices[v]];
        p1 = mesh.p[mesh.vertexIndices[v + 1]];
        p2 = mesh.p[mesh.vertexIndices[v + 2]];
    }

    public override Float Area()
    {
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);
        return 0.5f * Length(Cross(p1 - p0, p2 - p0));
    }

    public Float SolidAngle(Point3f p)
    {
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);
        return SphericalTriangleArea(Normalize(p0 - p), Normalize(p1 - p), Normalize(p2 - p));
    }

    public override Bounds3f Bounds()
    {
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);
        return Union(new Bounds3f(p0, p1), p2);
    }

    public override DirectionCone NormalBounds()
    {
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);
        int v = 3 * triIndex;

        Normal3f n = Normalize(new Normal3f(Cross(p1 - p0, p2 - p0)));
        // Ensure correct orientation of geometric normal for normal bounds
        if (mesh.n != null)
        {
            Normal3f ns = mesh.n[mesh.vertexIndices[v]] + mesh.n[mesh.vertexIndices[v + 1]] +
                          mesh.n[mesh.vertexIndices[v + 2]];
            n = FaceForward(n, ns);
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n *= -1;

        return new DirectionCone(new Vector3f(n));
    }

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        nTriHits.Denom();
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);

        TriangleIntersection? triIsect = IntersectTriangle(ray, tMax, p0, p1, p2);
        if (!triIsect.HasValue)
            return null;
        SurfaceInteraction intr = InteractionFromIntersection(mesh, triIndex, triIsect.Value, ray.time, -ray.d);
        nTriHits.Num();
        return new ShapeIntersection(intr, triIsect.Value.t);
    }

    public override bool IntersectP(in Ray ray, Float tMax = Infinity)
    {
        nTriHits.Denom();
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);

        TriangleIntersection? isect = IntersectTriangle(ray, tMax, p0, p1, p2);
        if (isect.HasValue)
        {
            nTriHits.Num();
            return true;
        }
        else
            return false;
    }

    public override string ToString()
    {
        GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);
        return $"[ Triangle meshIndex: {meshIndex} triIndex: {triIndex} -> p [ {p0} {p1} {p2} ] ]";
    }

    // Triangle Functions
    public static TriangleIntersection? IntersectTriangle(in Ray ray, Float tMax, Point3f p0, Point3f p1, Point3f p2)
    {
        // Return no intersection if triangle is degenerate
        if (LengthSquared(Cross(p2 - p0, p1 - p0)) == 0)
            return null;

        // Transform triangle vertices to ray coordinate space
        // Translate vertices based on ray origin
        Point3f p0t = p0 - new Vector3f(ray.o);
        Point3f p1t = p1 - new Vector3f(ray.o);
        Point3f p2t = p2 - new Vector3f(ray.o);

        // Permute components of triangle vertices and ray direction
        int kz = MaxComponentIndex(Abs(ray.d));
        int kx = kz + 1;
        if (kx == 3)
            kx = 0;
        int ky = kx + 1;
        if (ky == 3)
            ky = 0;
        ReadOnlySpan<int> perm = [kx, ky, kz];
        Vector3f d = Permute(ray.d, perm);
        p0t = Permute(p0t, perm);
        p1t = Permute(p1t, perm);
        p2t = Permute(p2t, perm);

        // Apply shear transformation to translated vertex positions
        Float Sx = -d.x / d.z;
        Float Sy = -d.y / d.z;
        Float Sz = 1 / d.z;
        p0t.x += Sx * p0t.z;
        p0t.y += Sy * p0t.z;
        p1t.x += Sx * p1t.z;
        p1t.y += Sy * p1t.z;
        p2t.x += Sx * p2t.z;
        p2t.y += Sy * p2t.z;

        // Compute edge function coefficients _e0_, _e1_, and _e2_
        Float e0 = DifferenceOfProducts(p1t.x, p2t.y, p1t.y, p2t.x);
        Float e1 = DifferenceOfProducts(p2t.x, p0t.y, p2t.y, p0t.x);
        Float e2 = DifferenceOfProducts(p0t.x, p1t.y, p0t.y, p1t.x);

        // Fall back to double-precision test at triangle edges
        if (sizeof(Float) == sizeof(float) && (e0 == 0.0f || e1 == 0.0f || e2 == 0.0f))
        {
            double p2txp1ty = (double)p2t.x * (double)p1t.y;
            double p2typ1tx = (double)p2t.y * (double)p1t.x;
            e0 = (float)(p2typ1tx - p2txp1ty);
            double p0txp2ty = (double)p0t.x * (double)p2t.y;
            double p0typ2tx = (double)p0t.y * (double)p2t.x;
            e1 = (float)(p0typ2tx - p0txp2ty);
            double p1txp0ty = (double)p1t.x * (double)p0t.y;
            double p1typ0tx = (double)p1t.y * (double)p0t.x;
            e2 = (float)(p1typ0tx - p1txp0ty);
        }

        // Perform triangle edge and determinant tests
        if ((e0 < 0 || e1 < 0 || e2 < 0) && (e0 > 0 || e1 > 0 || e2 > 0))
            return null;
        Float det = e0 + e1 + e2;
        if (det == 0)
            return null;

        // Compute scaled hit distance to triangle and test against ray $t$ range
        p0t.z *= Sz;
        p1t.z *= Sz;
        p2t.z *= Sz;
        Float tScaled = e0 * p0t.z + e1 * p1t.z + e2 * p2t.z;
        if (det < 0 && (tScaled >= 0 || tScaled < tMax * det))
            return null;
        else if (det > 0 && (tScaled <= 0 || tScaled > tMax * det))
            return null;

        // Compute barycentric coordinates and $t$ value for triangle intersection
        Float invDet = 1 / det;
        Float b0 = e0 * invDet, b1 = e1 * invDet, b2 = e2 * invDet;
        Float t = tScaled * invDet;
        DCHECK(!IsNaN(t));

        // Ensure that computed triangle $t$ is conservatively greater than zero
        // Compute $\delta_z$ term for triangle $t$ error bounds
        Float maxZt = MaxComponentValue(Abs(new Vector3f(p0t.z, p1t.z, p2t.z)));
        Float deltaZ = Gamma(3) * maxZt;

        // Compute $\delta_x$ and $\delta_y$ terms for triangle $t$ error bounds
        Float maxXt = MaxComponentValue(Abs(new Vector3f(p0t.x, p1t.x, p2t.x)));
        Float maxYt = MaxComponentValue(Abs(new Vector3f(p0t.y, p1t.y, p2t.y)));
        Float deltaX = Gamma(5) * (maxXt + maxZt);
        Float deltaY = Gamma(5) * (maxYt + maxZt);

        // Compute $\delta_e$ term for triangle $t$ error bounds
        Float deltaE = 2 * (Gamma(2) * maxXt * maxYt + deltaY * maxXt + deltaX * maxYt);

        // Compute $\delta_t$ term for triangle $t$ error bounds and check _t_
        Float maxE = MaxComponentValue(Abs(new Vector3f(e0, e1, e2)));
        Float deltaT = 3 * (Gamma(3) * maxE * maxZt + deltaE * maxZt + deltaZ * maxE) * FMath.Abs(invDet);
        if (t <= deltaT)
            return null;

        // Return _TriangleIntersection_ for intersection
        return new TriangleIntersection(b0, b1, b2, t);
    }

    // Get triangle texture coordinates in _uv_ array
    private static void GetUVs(TriangleMesh mesh, int v0, int v1, int v2, out Point2f uv0, out Point2f uv1,
                               out Point2f uv2)
    {
        if (mesh.uv != null)
        {
            uv0 = mesh.uv[v0];
            uv1 = mesh.uv[v1];
            uv2 = mesh.uv[v2];
        }
        else
        {
            uv0 = new Point2f(0, 0);
            uv1 = new Point2f(1, 0);
            uv2 = new Point2f(1, 1);
        }
    }

    public static SurfaceInteraction InteractionFromIntersection(TriangleMesh mesh, int triIndex,
                                                                 TriangleIntersection ti, Float time, Vector3f wo)
    {
        int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
            v2 = mesh.vertexIndices[3 * triIndex + 2];
        Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];
        // Compute triangle partial derivatives
        // Compute deltas and matrix determinant for triangle partial derivatives
        GetUVs(mesh, v0, v1, v2, out Point2f uv0, out Point2f uv1, out Point2f uv2);

        Vector2f duv02 = uv0 - uv2, duv12 = uv1 - uv2;
        Vector3f dp02 = p0 - p2, dp12 = p1 - p2;
        Float determinant = DifferenceOfProducts(duv02[0], duv12[1], duv02[1], duv12[0]);

        Vector3f dpdu = default, dpdv = default;
        bool degenerateUV = FMath.Abs(determinant) < 1e-9f;
        if (!degenerateUV)
        {
            // Compute triangle $\dpdu$ and $\dpdv$ via matrix inversion
            Float invdet = 1 / determinant;
            dpdu = DifferenceOfProducts(duv12[1], dp02, duv02[1], dp12) * invdet;
            dpdv = DifferenceOfProducts(duv02[0], dp12, duv12[0], dp02) * invdet;
        }
        // Handle degenerate triangle $(u,v)$ parameterization or partial derivatives
        if (degenerateUV || LengthSquared(Cross(dpdu, dpdv)) == 0)
        {
            Vector3f ng = Cross(p2 - p0, p1 - p0);
            if (LengthSquared(ng) == 0)
            {
                ng = CrossDouble(p2 - p0, p1 - p0);
                CHECK_NE(LengthSquared(ng), 0);
            }
            CoordinateSystem(Normalize(ng), out dpdu, out dpdv);
        }

        // Interpolate $(u,v)$ parametric coordinates and hit point
        Point3f pHit = ti.b0 * p0 + ti.b1 * p1 + ti.b2 * p2;
        Point2f uvHit = ti.b0 * uv0 + ti.b1 * uv1 + ti.b2 * uv2;

        // Return _SurfaceInteraction_ for triangle hit
        bool flipNormal = mesh.reverseOrientation ^ mesh.transformSwapsHandedness;
        // Compute error bounds _pError_ for triangle intersection
        Point3f pAbsSum = Abs(ti.b0 * p0) + Abs(ti.b1 * p1) + Abs(ti.b2 * p2);
        Vector3f pError = Gamma(7) * new Vector3f(pAbsSum);

        SurfaceInteraction isect = new(new Point3fi(pHit, pError), uvHit, wo, dpdu, dpdv, new Normal3f(),
                                       new Normal3f(), time, flipNormal);
        isect.faceIndex = mesh.faceIndices != null ? mesh.faceIndices[triIndex] : 0;
        // Set final surface normal and shading geometry for triangle
        // Override surface normal in _isect_ for triangle
        isect.n = isect.shading.n = new Normal3f(Normalize(Cross(dp02, dp12)));
        if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            isect.n = isect.shading.n = -isect.n;

        if (mesh.n != null || mesh.s != null)
        {
            // Initialize _Triangle_ shading geometry
            // Compute shading normal _ns_ for triangle
            Normal3f ns;
            if (mesh.n != null)
            {
                ns = ti.b0 * mesh.n[v0] + ti.b1 * mesh.n[v1] + ti.b2 * mesh.n[v2];
                ns = LengthSquared(ns) > 0 ? Normalize(ns) : isect.n;
            }
            else
                ns = isect.n;

            // Compute shading tangent _ss_ for triangle
            Vector3f ss;
            if (mesh.s != null)
            {
                ss = ti.b0 * mesh.s[v0] + ti.b1 * mesh.s[v1] + ti.b2 * mesh.s[v2];
                if (LengthSquared(ss) == 0)
                    ss = isect.dpdu;
            }
            else
                ss = isect.dpdu;

            // Compute shading bitangent _ts_ for triangle and adjust _ss_
            Vector3f ts = Cross(ns, ss);
            if (LengthSquared(ts) > 0)
                ss = Cross(ts, ns);
            else
                CoordinateSystem(ns, out ss, out ts);

            // Compute $\dndu$ and $\dndv$ for triangle shading geometry
            Normal3f dndu, dndv;
            if (mesh.n != null)
            {
                // Compute deltas for triangle partial derivatives of normal
                Vector2f duv02n = uv0 - uv2;
                Vector2f duv12n = uv1 - uv2;
                Normal3f dn1 = mesh.n[v0] - mesh.n[v2];
                Normal3f dn2 = mesh.n[v1] - mesh.n[v2];

                Float determinantn = DifferenceOfProducts(duv02n[0], duv12n[1], duv02n[1], duv12n[0]);
                // (pbrt compares with the double 1e-9 here.)
                bool degenerateUVn = Math.Abs(determinantn) < 1e-9;
                if (degenerateUVn)
                {
                    // We can still compute dndu and dndv, with respect to the
                    // same arbitrary coordinate system we use to compute dpdu
                    // and dpdv when this happens. It's important to do this
                    // (rather than giving up) so that ray differentials for
                    // rays reflected from triangles with degenerate
                    // parameterizations are still reasonable.
                    Vector3f dn = Cross(new Vector3f(mesh.n[v2] - mesh.n[v0]), new Vector3f(mesh.n[v1] - mesh.n[v0]));

                    if (LengthSquared(dn) == 0)
                        dndu = dndv = new Normal3f(0, 0, 0);
                    else
                    {
                        CoordinateSystem(dn, out Vector3f dnu, out Vector3f dnv);
                        dndu = new Normal3f(dnu);
                        dndv = new Normal3f(dnv);
                    }
                }
                else
                {
                    Float invDet = 1 / determinantn;
                    dndu = DifferenceOfProducts(duv12n[1], dn1, duv02n[1], dn2) * invDet;
                    dndv = DifferenceOfProducts(duv02n[0], dn2, duv12n[0], dn1) * invDet;
                }
            }
            else
                dndu = dndv = new Normal3f(0, 0, 0);

            isect.SetShadingGeometry(ns, ss, ts, dndu, dndv, true);
        }

        return isect;
    }

    // Vector3f(Cross(Vector3<double>(a), Vector3<double>(b)))
    private static Vector3f CrossDouble(Vector3f a, Vector3f b)
    {
        double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
        return new Vector3f((Float)DifferenceOfProducts(ay, bz, az, by), (Float)DifferenceOfProducts(az, bx, ax, bz),
                            (Float)DifferenceOfProducts(ax, by, ay, bx));
    }

    public override ShapeSample? Sample(Point2f u)
    {
        int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
            v2 = mesh.vertexIndices[3 * triIndex + 2];
        Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

        // Sample point on triangle uniformly by area
        Array3<Float> b = SampleUniformTriangle(u);
        Point3f p = b[0] * p0 + b[1] * p1 + b[2] * p2;

        // Compute surface normal for sampled point on triangle
        Normal3f n = Normalize(new Normal3f(Cross(p1 - p0, p2 - p0)));
        if (mesh.n != null)
        {
            Normal3f ns = b[0] * mesh.n[v0] + b[1] * mesh.n[v1] + (1 - b[0] - b[1]) * mesh.n[v2];
            n = FaceForward(n, ns);
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n *= -1;

        // Compute $(u,v)$ for sampled point on triangle
        GetUVs(mesh, v0, v1, v2, out Point2f uv0, out Point2f uv1, out Point2f uv2);
        Point2f uvSample = b[0] * uv0 + b[1] * uv1 + b[2] * uv2;

        // Compute error bounds _pError_ for sampled point on triangle
        Point3f pAbsSum = Abs(b[0] * p0) + Abs(b[1] * p1) + Abs((1 - b[0] - b[1]) * p2);
        Vector3f pError = new(Gamma(6) * pAbsSum);

        return new ShapeSample(new Interaction(new Point3fi(p, pError), n, uvSample), 1 / Area());
    }

    public override Float PDF(in Interaction intr) => 1 / Area();

    private static Array4<Float> CosineWeights(Normal3f ns, Point3f rp, Point3f p0, Point3f p1, Point3f p2)
    {
        // Compute $\cos\theta$-based weights _w_ at sample domain corners
        Vector3f wi0 = Normalize(p0 - rp), wi1 = Normalize(p1 - rp), wi2 = Normalize(p2 - rp);
        Array4<Float> w = default;
        w[0] = FMath.Max((Float)0.01, AbsDot(ns, wi1));
        w[1] = FMath.Max((Float)0.01, AbsDot(ns, wi1));
        w[2] = FMath.Max((Float)0.01, AbsDot(ns, wi0));
        w[3] = FMath.Max((Float)0.01, AbsDot(ns, wi2));
        return w;
    }

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u)
    {
        int v0 = mesh.vertexIndices[3 * triIndex], v1 = mesh.vertexIndices[3 * triIndex + 1],
            v2 = mesh.vertexIndices[3 * triIndex + 2];
        Point3f p0 = mesh.p[v0], p1 = mesh.p[v1], p2 = mesh.p[v2];

        // Use uniform area sampling for numerically unstable cases
        Float solidAngle = SolidAngle(ctx.p());
        if (solidAngle < MinSphericalSampleArea || solidAngle > MaxSphericalSampleArea)
            return SampleByArea(ctx, u);

        // Sample spherical triangle from reference point
        // Apply warp product sampling for cosine factor at reference point
        Float pdf = 1;
        if (ctx.ns != new Normal3f(0, 0, 0))
        {
            Array4<Float> w = CosineWeights(ctx.ns, ctx.p(), p0, p1, p2);
            u = SampleBilinear(u, w);
            DCHECK(u[0] >= 0 && u[0] < 1 && u[1] >= 0 && u[1] < 1);
            pdf = BilinearPDF(u, w);
        }

        Array3<Float> b = SampleSphericalTriangle([p0, p1, p2], ctx.p(), u, out Float triPDF);
        if (triPDF == 0)
            return null;
        pdf *= triPDF;

        // Compute error bounds _pError_ for sampled point on triangle
        Point3f pAbsSum = Abs(b[0] * p0) + Abs(b[1] * p1) + Abs((1 - b[0] - b[1]) * p2);
        Vector3f pError = new(Gamma(6) * pAbsSum);

        // Return _ShapeSample_ for solid angle sampled point on triangle
        Point3f p = b[0] * p0 + b[1] * p1 + b[2] * p2;
        // Compute surface normal for sampled point on triangle
        Normal3f n = Normalize(new Normal3f(Cross(p1 - p0, p2 - p0)));
        if (mesh.n != null)
        {
            Normal3f ns = b[0] * mesh.n[v0] + b[1] * mesh.n[v1] + (1 - b[0] - b[1]) * mesh.n[v2];
            n = FaceForward(n, ns);
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n *= -1;

        // Compute $(u,v)$ for sampled point on triangle
        GetUVs(mesh, v0, v1, v2, out Point2f uv0, out Point2f uv1, out Point2f uv2);
        Point2f uvSample = b[0] * uv0 + b[1] * uv1 + b[2] * uv2;

        return new ShapeSample(new Interaction(new Point3fi(p, pError), n, ctx.time, uvSample), pdf);
    }

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi)
    {
        Float solidAngle = SolidAngle(ctx.p());
        // Return PDF based on uniform area sampling for challenging triangles
        if (solidAngle < MinSphericalSampleArea || solidAngle > MaxSphericalSampleArea)
            return PDFByArea(ctx, wi);

        Float pdf = 1 / solidAngle;
        // Adjust PDF for warp product sampling of triangle $\cos\theta$ factor
        if (ctx.ns != new Normal3f(0, 0, 0))
        {
            GetVertices(out Point3f p0, out Point3f p1, out Point3f p2);

            Point2f u = InvertSphericalTriangleSample([p0, p1, p2], ctx.p(), wi);
            Array4<Float> w = CosineWeights(ctx.ns, ctx.p(), p0, p1, p2);
            pdf *= BilinearPDF(u, w);
        }

        return pdf;
    }
}

// CurveType Definition
public enum CurveType { Flat, Cylinder, Ribbon }

// CurveCommon Definition
public sealed class CurveCommon
{
    private static readonly StatCounter nCurves = new("Geometry/Curves");

    // CurveCommon Public Members
    public readonly CurveType type;
    public readonly Point3f[] cpObj = new Point3f[4];
    public readonly Float[] width = new Float[2];
    public readonly Normal3f[] n = new Normal3f[2];
    public readonly Float normalAngle, invSinNormalAngle;
    public readonly Transform renderFromObject, objectFromRender;
    public readonly bool reverseOrientation, transformSwapsHandedness;

    // CurveCommon Method Definitions
    public CurveCommon(ReadOnlySpan<Point3f> c, Float width0, Float width1, CurveType type,
                       ReadOnlySpan<Normal3f> norm, Transform renderFromObject, Transform objectFromRender,
                       bool reverseOrientation)
    {
        this.type = type;
        this.renderFromObject = renderFromObject;
        this.objectFromRender = objectFromRender;
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();
        width[0] = width0;
        width[1] = width1;
        CHECK_EQ(c.Length, 4);
        for (int i = 0; i < 4; ++i)
            cpObj[i] = c[i];
        if (norm.Length == 2)
        {
            n[0] = Normalize(norm[0]);
            n[1] = Normalize(norm[1]);
            normalAngle = AngleBetween(n[0], n[1]);
            invSinNormalAngle = 1 / FMath.Sin(normalAngle);
        }
        nCurves.Inc();
    }

    public override string ToString() =>
        $"[ CurveCommon type: {Util.ToString(type)} cpObj: [ {string.Join(", ", cpObj)} ] " +
        $"width: [ {string.Join(", ", width.Select(FloatToString))} ] n: [ {string.Join(", ", n)} ] " +
        $"normalAngle: {FloatToString(normalAngle)} invSinNormalAngle: {FloatToString(invSinNormalAngle)} " +
        $"renderFromObject: {renderFromObject} objectFromRender: {objectFromRender} " +
        $"reverseOrientation: {Sphere.B(reverseOrientation)} transformSwapsHandedness: {Sphere.B(transformSwapsHandedness)} ]";
}

// Curve Definition
public sealed class Curve : Shape
{
    private static readonly StatMemoryCounter curveBytes = new("Memory/Curves");
    private static readonly StatPercent nCurveHits = new("Intersections/Ray-curve intersection tests");
    private static readonly StatCounter nSplitCurves = new("Geometry/Split curves");

    // Curve Private Members
    private readonly CurveCommon common;
    private readonly Float uMin, uMax;

    // Curve Public Methods
    public Curve(CurveCommon common, Float uMin, Float uMax)
    {
        this.common = common;
        this.uMin = uMin;
        this.uMax = uMax;
    }

    public override DirectionCone NormalBounds() => DirectionCone.EntireSphere();

    public static List<Shape> CreateCurve(Transform renderFromObject, Transform objectFromRender,
                                          bool reverseOrientation, ReadOnlySpan<Point3f> c, Float w0, Float w1,
                                          CurveType type, ReadOnlySpan<Normal3f> norm, int splitDepth)
    {
        CurveCommon common = new(c, w0, w1, type, norm, renderFromObject, objectFromRender, reverseOrientation);

        int nSegments = 1 << splitDepth;
        List<Shape> segments = new(nSegments);
        for (int i = 0; i < nSegments; ++i)
        {
            Float uMin = i / (Float)nSegments;
            Float uMax = (i + 1) / (Float)nSegments;
            segments.Add(new Curve(common, uMin, uMax));
            nSplitCurves.Inc();
        }

        // (sizeof(CurveCommon) + nSegments * sizeof(Curve) in pbrt)
        curveBytes.Add(120 + nSegments * 16L);
        return segments;
    }

    // Curve Method Definitions
    public override Bounds3f Bounds()
    {
        Bounds3f objBounds = BoundCubicBezier(common.cpObj, uMin, uMax);
        // Expand _objBounds_ by maximum curve width over $u$ range
        Float width0 = Lerp(uMin, common.width[0], common.width[1]);
        Float width1 = Lerp(uMax, common.width[0], common.width[1]);
        objBounds = Expand(objBounds, FMath.Max(width0, width1) * 0.5f);

        return common.renderFromObject.Apply(objBounds);
    }

    public override Float Area()
    {
        Array4<Point3f> cpObj = CubicBezierControlPoints(common.cpObj, uMin, uMax);
        Float width0 = Lerp(uMin, common.width[0], common.width[1]);
        Float width1 = Lerp(uMax, common.width[0], common.width[1]);
        Float avgWidth = (width0 + width1) * 0.5f;
        Float approxLength = 0f;
        for (int i = 0; i < 3; ++i)
            approxLength += Distance(cpObj[i], cpObj[i + 1]);
        return approxLength * avgWidth;
    }

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        ShapeIntersection? si = null;
        IntersectRay(ray, tMax, ref si, true);
        return si;
    }

    public override bool IntersectP(in Ray ray, Float tMax = Infinity)
    {
        ShapeIntersection? si = null;
        return IntersectRay(ray, tMax, ref si, false);
    }

    // (pbrt passes a null ShapeIntersection pointer for IntersectP();
    // _wantSi_ is false then.)
    private bool IntersectRay(in Ray r, Float tMax, ref ShapeIntersection? si, bool wantSi)
    {
        nCurveHits.Denom();
        // Transform _Ray_ to curve's object space
        Ray ray = common.objectFromRender.Apply(r);

        // Get object-space control points for curve segment, _cpObj_
        Array4<Point3f> cpObj = CubicBezierControlPoints(common.cpObj, uMin, uMax);

        // Project curve control points to plane perpendicular to ray
        Vector3f dx = Cross(ray.d, cpObj[3] - cpObj[0]);
        if (LengthSquared(dx) == 0)
            CoordinateSystem(ray.d, out dx, out _);
        Transform rayFromObject = LookAt(ray.o, ray.o + ray.d, dx);
        Span<Point3f> cp = [rayFromObject.Apply(cpObj[0]), rayFromObject.Apply(cpObj[1]),
                            rayFromObject.Apply(cpObj[2]), rayFromObject.Apply(cpObj[3])];

        // Test ray against bound of projected control points
        Float maxWidth = FMath.Max(Lerp(uMin, common.width[0], common.width[1]),
                                   Lerp(uMax, common.width[0], common.width[1]));
        Bounds3f curveBounds = Union(new Bounds3f(cp[0], cp[1]), new Bounds3f(cp[2], cp[3]));
        curveBounds = Expand(curveBounds, 0.5f * maxWidth);
        Bounds3f rayBounds = new(new Point3f(0, 0, 0), new Point3f(0, 0, Length(ray.d) * tMax));
        if (!Overlaps(rayBounds, curveBounds))
            return false;

        // Compute refinement depth for curve, _maxDepth_
        Float L0 = 0;
        for (int i = 0; i < 2; ++i)
            L0 = FMath.Max(L0, FMath.Max(FMath.Max(FMath.Abs(cp[i].x - 2 * cp[i + 1].x + cp[i + 2].x),
                                                   FMath.Abs(cp[i].y - 2 * cp[i + 1].y + cp[i + 2].y)),
                                         FMath.Abs(cp[i].z - 2 * cp[i + 1].z + cp[i + 2].z)));
        int maxDepth = 0;
        if (L0 > 0)
        {
            Float eps = FMath.Max(common.width[0], common.width[1]) * .05f; // width / 20
            // Compute log base 4 by dividing log2 in half.
            int r0 = Log2Int(1.41421356237f * 6f * L0 / (8f * eps)) / 2;
            maxDepth = Clamp(r0, 0, 10);
        }

        // Recursively test for ray--curve intersection
        return RecursiveIntersect(ray, tMax, cp, Inverse(rayFromObject), uMin, uMax, maxDepth, ref si, wantSi);
    }

    private bool RecursiveIntersect(in Ray ray, Float tMax, ReadOnlySpan<Point3f> cp, Transform objectFromRay,
                                    Float u0, Float u1, int depth, ref ShapeIntersection? si, bool wantSi)
    {
        Float rayLength = Length(ray.d);
        if (depth > 0)
        {
            // Split curve segment into subsegments and test for intersection
            Point3f[] cpSplit = SubdivideCubicBezier(cp);
            Span<Float> u = [u0, (u0 + u1) / 2, u1];
            for (int seg = 0; seg < 2; ++seg)
            {
                // Check ray against curve segment's bounding box
                Float maxWidth = FMath.Max(Lerp(u[seg], common.width[0], common.width[1]),
                                           Lerp(u[seg + 1], common.width[0], common.width[1]));
                ReadOnlySpan<Point3f> cps = cpSplit.AsSpan(3 * seg, 4);
                Bounds3f curveBounds = Union(new Bounds3f(cps[0], cps[1]), new Bounds3f(cps[2], cps[3]));
                curveBounds = Expand(curveBounds, 0.5f * maxWidth);
                Bounds3f rayBounds = new(new Point3f(0, 0, 0), new Point3f(0, 0, Length(ray.d) * tMax));
                if (!Overlaps(rayBounds, curveBounds))
                    continue;

                // Recursively test ray-segment intersection
                bool hit = RecursiveIntersect(ray, tMax, cps, objectFromRay, u[seg], u[seg + 1], depth - 1, ref si,
                                              wantSi);
                if (hit && !wantSi)
                    return true;
            }
            return wantSi && si.HasValue;
        }
        else
        {
            // Intersect ray with curve segment
            // Test ray against segment endpoint boundaries
            // Test sample point against tangent perpendicular at curve start
            Float edge = (cp[1].y - cp[0].y) * -cp[0].y + cp[0].x * (cp[0].x - cp[1].x);
            if (edge < 0)
                return false;

            // Test sample point against tangent perpendicular at curve end
            edge = (cp[2].y - cp[3].y) * -cp[3].y + cp[3].x * (cp[3].x - cp[2].x);
            if (edge < 0)
                return false;

            // Find line $w$ that gives minimum distance to sample point
            Vector2f segmentDir = new Point2f(cp[3].x, cp[3].y) - new Point2f(cp[0].x, cp[0].y);
            Float denom = LengthSquared(segmentDir);
            if (denom == 0)
                return false;
            Float w = Dot(-new Vector2f(cp[0].x, cp[0].y), segmentDir) / denom;

            // Compute $u$ coordinate of curve intersection point and _hitWidth_
            Float u = Clamp(Lerp(w, u0, u1), u0, u1);
            Float hitWidth = Lerp(u, common.width[0], common.width[1]);
            Normal3f nHit = default;
            if (common.type == CurveType.Ribbon)
            {
                // Scale _hitWidth_ based on ribbon orientation
                if (common.normalAngle == 0)
                    nHit = common.n[0];
                else
                {
                    Float sin0 = FMath.Sin((1 - u) * common.normalAngle) * common.invSinNormalAngle;
                    Float sin1 = FMath.Sin(u * common.normalAngle) * common.invSinNormalAngle;
                    nHit = sin0 * common.n[0] + sin1 * common.n[1];
                }
                hitWidth *= AbsDot(nHit, ray.d) / rayLength;
            }

            // Test intersection point against curve width
            Point3f pc = EvaluateCubicBezier(cp, Clamp(w, 0, 1), out Vector3f dpcdw);
            Float ptCurveDist2 = Sqr(pc.x) + Sqr(pc.y);

            if (ptCurveDist2 > Sqr(hitWidth) * 0.25f)
                return false;
            if (pc.z < 0 || pc.z > rayLength * tMax)
                return false;

            if (wantSi)
            {
                // Initialize _ShapeIntersection_ for curve intersection
                // Compute _tHit_ for curve intersection
                // FIXME: this tHit isn't quite right for ribbons...
                Float tHit = pc.z / rayLength;
                if (si.HasValue && tHit > si.Value.tHit)
                    return false;

                // Initialize _SurfaceInteraction_ _intr_ for curve intersection
                // Compute $v$ coordinate of curve intersection point
                Float ptCurveDist = FMath.Sqrt(ptCurveDist2);
                Float edgeFunc = dpcdw.x * -pc.y + pc.x * dpcdw.y;
                Float v = (edgeFunc > 0) ? 0.5f + ptCurveDist / hitWidth : 0.5f - ptCurveDist / hitWidth;

                // Compute $\dpdu$ and $\dpdv$ for curve intersection
                Vector3f dpdv;
                EvaluateCubicBezier(common.cpObj, u, out Vector3f dpdu);
                CHECK(new Vector3f(0, 0, 0) != dpdu);
                if (common.type == CurveType.Ribbon)
                    dpdv = Normalize(Cross(nHit, dpdu)) * hitWidth;
                else
                {
                    // Compute curve $\dpdv$ for flat and cylinder curves
                    Vector3f dpduPlane = objectFromRay.ApplyInverse(dpdu);
                    Vector3f dpdvPlane = Normalize(new Vector3f(-dpduPlane.y, dpduPlane.x, 0)) * hitWidth;
                    if (common.type == CurveType.Cylinder)
                    {
                        // Rotate _dpdvPlane_ to give cylindrical appearance
                        Float theta = Lerp(v, -90, 90);
                        Transform rot = Rotate(-theta, dpduPlane);
                        dpdvPlane = rot.Apply(dpdvPlane);
                    }
                    dpdv = objectFromRay.Apply(dpdvPlane);
                }

                // Compute error bounds for curve intersection
                Vector3f pError = new(hitWidth, hitWidth, hitWidth);

                bool flipNormal = common.reverseOrientation ^ common.transformSwapsHandedness;
                Point3fi pi = new(ray.At(tHit), pError);
                SurfaceInteraction intr = new(pi, new Point2f(u, v), -ray.d, dpdu, dpdv, new Normal3f(),
                                              new Normal3f(), ray.time, flipNormal);
                intr = common.renderFromObject.Apply(intr);

                si = new ShapeIntersection(intr, tHit);
            }
            nCurveHits.Num();
            return true;
        }
    }

    public override ShapeSample? Sample(Point2f u)
    {
        LOG_FATAL("Curve::Sample not implemented.");
        return null;
    }

    public override Float PDF(in Interaction intr)
    {
        LOG_FATAL("Curve::PDF not implemented.");
        return 0;
    }

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u)
    {
        LOG_FATAL("Curve::Sample not implemented.");
        return null;
    }

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi)
    {
        LOG_FATAL("Curve::PDF not implemented.");
        return 0;
    }

    public override string ToString() =>
        $"[ Curve common: {common} uMin: {FloatToString(uMin)} uMax: {FloatToString(uMax)} ]";

    public static List<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                     ParameterDictionary parameters, FileLoc? loc)
    {
        Float width = parameters.GetOneFloat("width", 1f);
        Float width0 = parameters.GetOneFloat("width0", width);
        Float width1 = parameters.GetOneFloat("width1", width);

        int degree = parameters.GetOneInt("degree", 3);
        if (degree != 2 && degree != 3)
        {
            Error(loc, $"Invalid degree {degree}: only degree 2 and 3 curves are supported.");
            return [];
        }

        string basis = parameters.GetOneString("basis", "bezier");
        if (basis != "bezier" && basis != "bspline")
        {
            Error(loc, $"Invalid basis \"{basis}\": only \"bezier\" and \"bspline\" are " + "supported.");
            return [];
        }

        int nSegments;
        List<Point3f> cp = parameters.GetPoint3fArray("P");
        if (basis == "bezier")
        {
            // After the first segment, which uses degree+1 control points,
            // subsequent segments reuse the last control point of the previous
            // one and then use degree more control points.
            // (pbrt computes this with size_t, so fewer than degree + 1
            // control points wrap around.)
            if ((((ulong)cp.Count - 1 - (ulong)degree) % (ulong)degree) != 0)
            {
                Error(loc, $"Invalid number of control points {cp.Count}: for the degree {degree} " +
                           $"Bezier basis {degree + 1} + n * {degree} are required, for n >= 0.");
                return [];
            }
            nSegments = (int)(((ulong)cp.Count - 1) / (ulong)degree);
        }
        else
        {
            if (cp.Count < degree + 1)
            {
                Error(loc, $"Invalid number of control points {cp.Count}: for the degree {degree} " +
                           $"b-spline basis, must have >= {degree + 1}.");
                return [];
            }
            nSegments = cp.Count - degree;
        }

        CurveType type;
        string curveType = parameters.GetOneString("type", "flat");
        if (curveType == "flat")
            type = CurveType.Flat;
        else if (curveType == "ribbon")
            type = CurveType.Ribbon;
        else if (curveType == "cylinder")
            type = CurveType.Cylinder;
        else
        {
            Error(loc, $"Unknown curve type \"{curveType}\".  Using \"cylinder\".");
            type = CurveType.Cylinder;
        }

        List<Normal3f> n = parameters.GetNormal3fArray("N");
        if (n.Count > 0)
        {
            if (type != CurveType.Ribbon)
            {
                Warning("Curve normals are only used with \"ribbon\" type curves.");
                n = [];
            }
            else if (n.Count != nSegments + 1)
            {
                Error(loc, $"Invalid number of normals {n.Count}: must provide {nSegments + 1} normals for " +
                           "ribbon " + $"curves with {nSegments} segments.");
                return [];
            }
        }
        else if (type == CurveType.Ribbon)
        {
            Error(loc, "Must provide normals \"N\" at curve endpoints with ribbon " + "curves.");
            return [];
        }

        // This is kind of a hack, but since we dice curves on the GPU we
        // really don't want to have them split here.
        int sd = Options.useGPU ? 0 : parameters.GetOneInt("splitdepth", 3);

        if (type == CurveType.Ribbon && n.Count == 0)
        {
            Error(loc, "Must provide normals \"N\" at curve endpoints with ribbon " + "curves.");
            return [];
        }

        List<Shape> curves = [];
        Point3f[] cpa = cp.ToArray();
        Normal3f[] na = n.ToArray();
        // Pointer to the first control point for the current segment. This is
        // updated after each loop iteration depending on the current basis.
        int cpOffset = 0;
        for (int seg = 0; seg < nSegments; ++seg)
        {
            Array4<Point3f> segCpBezier;

            // First, compute the cubic Bezier control points for the current
            // segment and store them in segCpBezier. (It is admittedly
            // wasteful storage-wise to turn b-splines into Bezier segments and
            // wasteful computationally to turn quadratic curves into cubics,
            // but yolo.)
            if (basis == "bezier")
            {
                if (degree == 2)
                    // Elevate to degree 3.
                    segCpBezier = ElevateQuadraticBezierToCubic(cpa.AsSpan(cpOffset, 3));
                else
                {
                    // All set.
                    segCpBezier = default;
                    for (int i = 0; i < 4; ++i)
                        segCpBezier[i] = cpa[cpOffset + i];
                }
                cpOffset += degree;
            }
            else
            {
                // Uniform b-spline.
                if (degree == 2)
                {
                    Array3<Point3f> bezCp = QuadraticBSplineToBezier(cpa.AsSpan(cpOffset, 3));
                    segCpBezier = ElevateQuadraticBezierToCubic(bezCp);
                }
                else
                    segCpBezier = CubicBSplineToBezier(cpa.AsSpan(cpOffset, 4));
                ++cpOffset;
            }

            ReadOnlySpan<Normal3f> nspan = na.Length > 0 ? na.AsSpan(seg, 2) : [];
            List<Shape> c = CreateCurve(renderFromObject, objectFromRender, reverseOrientation, segCpBezier,
                                        Lerp(seg / (Float)nSegments, width0, width1),
                                        Lerp((seg + 1) / (Float)nSegments, width0, width1), type, nspan, sd);
            curves.AddRange(c);
        }
        return curves;
    }
}

// BilinearIntersection Definition
public struct BilinearIntersection(Point2f uv, Float t)
{
    public Point2f uv = uv;
    public Float t = t;

    // (pbrt's string is missing the closing bracket.)
    public override readonly string ToString() => $"[ BilinearIntersection uv: {uv} t: {FloatToString(t)}";
}

// BilinearPatch Definition
public sealed class BilinearPatch : Shape
{
    private static readonly StatPixelRatio nBLPHits = new("Intersections/Ray-bilinear patch intersection tests");
    private static readonly StatMemoryCounter blpBytes = new("Memory/Bilinear patches");

    // BilinearPatch Private Members
    private readonly int meshIndex, blpIndex;
    private readonly BilinearPatchMesh mesh;
    private static readonly List<BilinearPatchMesh> allMeshes = [];
    private readonly Float area;
    private const Float MinSphericalSampleArea = (Float)1e-4;

    // BilinearPatch Method Definitions
    public BilinearPatch(BilinearPatchMesh mesh, int meshIndex, int blpIndex)
    {
        this.mesh = mesh;
        this.meshIndex = meshIndex;
        this.blpIndex = blpIndex;
        // (sizeof(BilinearPatch) in pbrt)
        blpBytes.Add(12);
        // Store area of bilinear patch in _area_
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        if (IsRectangle())
            area = Distance(p00, p01) * Distance(p00, p10);
        else
        {
            // Compute approximate area of bilinear patch
            // FIXME: it would be good to skip this for flat patches, or to
            // be adaptive based on curvature in some manner
            const int na = 3;
            Point3f[,] p = new Point3f[na + 1, na + 1];
            for (int i = 0; i <= na; ++i)
            {
                Float u = i / (Float)na;
                for (int j = 0; j <= na; ++j)
                {
                    Float v = j / (Float)na;
                    p[i, j] = Lerp(u, Lerp(v, p00, p01), Lerp(v, p10, p11));
                }
            }
            area = 0;
            for (int i = 0; i < na; ++i)
                for (int j = 0; j < na; ++j)
                    area += 0.5f * Length(Cross(p[i + 1, j + 1] - p[i, j], p[i + 1, j] - p[i, j + 1]));
        }
    }

    public static BilinearPatchMesh? CreateMesh(Transform renderFromObject, bool reverseOrientation,
                                                ParameterDictionary parameters, FileLoc? loc)
    {
        List<int> vertexIndices = parameters.GetIntArray("indices");
        List<Point3f> P = parameters.GetPoint3fArray("P");
        List<Point2f> uv = parameters.GetPoint2fArray("uv");

        if (vertexIndices.Count == 0)
        {
            if (P.Count == 4)
                // single patch
                vertexIndices = [0, 1, 2, 3];
            else
            {
                Error(loc, "Vertex indices \"indices\" must be provided with " + "bilinear patch mesh shape.");
                return null;
            }
        }
        else if ((vertexIndices.Count % 4) != 0)
        {
            Error(loc, $"Number of vertex indices {vertexIndices.Count} not a multiple of 4. Discarding " +
                       $"{vertexIndices.Count % 4} excess.");
            while ((vertexIndices.Count % 4) != 0)
                vertexIndices.RemoveAt(vertexIndices.Count - 1);
        }

        if (P.Count == 0)
        {
            Error(loc, "Vertex positions \"P\" must be provided with bilinear " + "patch mesh shape.");
            return null;
        }

        if (uv.Count > 0 && uv.Count != P.Count)
        {
            Error(loc, "Number of \"uv\"s for bilinear patch mesh must match \"P\"s. " + "Discarding uvs.");
            uv = [];
        }

        List<Normal3f> N = parameters.GetNormal3fArray("N");
        if (N.Count > 0 && N.Count != P.Count)
        {
            Error(loc, "Number of \"N\"s for bilinear patch mesh must match \"P\"s. " + "Discarding \"N\"s.");
            N = [];
        }

        // (Negative indices are out of bounds too, as with pbrt's size_t
        // comparison.)
        for (int i = 0; i < vertexIndices.Count; ++i)
            if ((uint)vertexIndices[i] >= (uint)P.Count)
            {
                Error(loc, $"Bilinear patch mesh has out of-bounds vertex index {vertexIndices[i]} ({P.Count} " +
                           "\"P\" " + "values were given. Discarding this mesh.");
                return null;
            }

        List<int> faceIndices = parameters.GetIntArray("faceIndices");
        if (faceIndices.Count > 0 && faceIndices.Count != vertexIndices.Count / 4)
        {
            Error(loc, $"Number of face indices {faceIndices.Count} does not match number of bilinear " +
                       $"patches {vertexIndices.Count / 4}. " + "Discarding face indices.");
            faceIndices = [];
        }

        string filename = ResolveFilename(parameters.GetOneString("emissionfilename", ""));
        PiecewiseConstant2D? imageDist = null;
        if (filename.Length > 0)
        {
            if (uv.Count > 0)
                Error(loc, "\"emissionfilename\" is currently ignored for bilinear patches " +
                           "if \"uv\" coordinates have been provided--sorry!");
            else
            {
                ImageAndMetadata im = Image.Read(filename);
                // Account for v inversion in DiffuseAreaLight lookup, which in turn is there
                // to match ImageTexture...
                im.image.FlipY();
                Bounds2f domain = new(new Point2f(0, 0), new Point2f(1, 1));
                Array2D<Float> d = im.image.GetSamplingDistribution();
                imageDist = new PiecewiseConstant2D(d, domain);
            }
        }

        return new BilinearPatchMesh(renderFromObject, reverseOrientation, vertexIndices, P, N, uv, faceIndices,
                                     imageDist);
    }

    public static List<Shape> CreatePatches(BilinearPatchMesh mesh)
    {
        int meshIndex;
        lock (allMeshes)
        {
            CHECK_LT(allMeshes.Count, int.MaxValue);
            meshIndex = allMeshes.Count;
            allMeshes.Add(mesh);
        }

        List<Shape> blps = new(mesh.nPatches);
        for (int i = 0; i < mesh.nPatches; ++i)
            blps.Add(new BilinearPatch(mesh, meshIndex, i));

        return blps;
    }

    // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
    private void GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11)
    {
        int v = 4 * blpIndex;
        p00 = mesh.p[mesh.vertexIndices[v]];
        p10 = mesh.p[mesh.vertexIndices[v + 1]];
        p01 = mesh.p[mesh.vertexIndices[v + 2]];
        p11 = mesh.p[mesh.vertexIndices[v + 3]];
    }

    private Normal3f N(int i) => mesh.n![mesh.vertexIndices[4 * blpIndex + i]];
    private Point2f UV(int i) => mesh.uv![mesh.vertexIndices[4 * blpIndex + i]];

    // BilinearPatch Private Methods
    private bool IsRectangle()
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        if (p00 == p01 || p01 == p11 || p11 == p10 || p10 == p00)
            return false;
        // Check if bilinear patch vertices are coplanar
        Normal3f n = new(Normalize(Cross(p10 - p00, p01 - p00)));
        if (AbsDot(Normalize(p11 - p00), n) > 1e-5f)
            return false;

        // Check if planar vertices form a rectangle
        Point3f pCenter = (p00 + p01 + p10 + p11) / 4;
        Span<Float> d2 = [DistanceSquared(p00, pCenter), DistanceSquared(p01, pCenter),
                          DistanceSquared(p10, pCenter), DistanceSquared(p11, pCenter)];
        for (int i = 1; i < 4; ++i)
            if (FMath.Abs(d2[i] - d2[0]) / d2[0] > 1e-4f)
                return false;
        return true;
    }

    public override Float Area() => area;

    public override Bounds3f Bounds()
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);
        return Union(new Bounds3f(p00, p01), new Bounds3f(p10, p11));
    }

    public override DirectionCone NormalBounds()
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        // If patch is a triangle, return bounds for single surface normal
        if (p00 == p10 || p10 == p11 || p11 == p01 || p01 == p00)
        {
            Vector3f dpdu = Lerp(0.5f, p10, p11) - Lerp(0.5f, p00, p01);
            Vector3f dpdv = Lerp(0.5f, p01, p11) - Lerp(0.5f, p00, p10);
            Vector3f n = Normalize(Cross(dpdu, dpdv));
            if (mesh.n != null)
            {
                Normal3f ns = (N(0) + N(1) + N(2) + N(3)) / 4;
                n = FaceForward(n, ns);
            }
            else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
                n = -n;
            return new DirectionCone(n);
        }

        // Compute bilinear patch normal _n00_ at $(0,0)$
        Vector3f n00 = Normalize(Cross(p10 - p00, p01 - p00));
        if (mesh.n != null)
            n00 = FaceForward(n00, N(0));
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n00 = -n00;

        // Compute bilinear patch normals _n10_, _n01_, and _n11_
        Vector3f n10 = Normalize(Cross(p11 - p10, p00 - p10));
        Vector3f n01 = Normalize(Cross(p00 - p01, p11 - p01));
        Vector3f n11 = Normalize(Cross(p01 - p11, p10 - p11));
        if (mesh.n != null)
        {
            n10 = FaceForward(n10, N(1));
            n01 = FaceForward(n01, N(2));
            n11 = FaceForward(n11, N(3));
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
        {
            n10 = -n10;
            n01 = -n01;
            n11 = -n11;
        }

        // Compute average normal and return normal bounds for patch
        Vector3f nAvg = Normalize(n00 + n10 + n01 + n11);
        Float cosTheta = FMath.Min(FMath.Min(Dot(nAvg, n00), Dot(nAvg, n01)), FMath.Min(Dot(nAvg, n10), Dot(nAvg, n11)));
        return new DirectionCone(nAvg, Clamp(cosTheta, -1, 1));
    }

    // Bilinear Patch Inline Functions
    public static BilinearIntersection? IntersectBilinearPatch(in Ray ray, Float tMax, Point3f p00, Point3f p10,
                                                               Point3f p01, Point3f p11)
    {
        // Find quadratic coefficients for distance from ray to $u$ iso-lines
        Float a = Dot(Cross(p10 - p00, p01 - p11), ray.d);
        Float c = Dot(Cross(p00 - ray.o, ray.d), p01 - p00);
        Float b = Dot(Cross(p10 - ray.o, ray.d), p11 - p10) - (a + c);

        // Solve quadratic for bilinear patch $u$ intersection
        if (!Quadratic(a, b, c, out Float u1, out Float u2))
            return null;

        // Find epsilon _eps_ to ensure that candidate $t$ is greater than zero
        Float eps = Gamma(30) * (MaxComponentValue(Abs(ray.o)) + MaxComponentValue(Abs(ray.d)) +
                                 MaxComponentValue(Abs(p00)) + MaxComponentValue(Abs(p10)) +
                                 MaxComponentValue(Abs(p01)) + MaxComponentValue(Abs(p11)));

        // Compute $v$ and $t$ for the first $u$ intersection
        // (pbrt leaves _u_ and _v_ uninitialized; they're only used if _t_
        // is set.)
        Float t = tMax, u = 0, v = 0;
        if (0 <= u1 && u1 <= 1)
        {
            // Precompute common terms for $v$ and $t$ computation
            Point3f uo = Lerp(u1, p00, p10);
            Vector3f ud = Lerp(u1, p01, p11) - uo;
            Vector3f deltao = uo - ray.o;
            Vector3f perp = Cross(ray.d, ud);
            Float p2 = LengthSquared(perp);

            // Compute matrix determinants for $v$ and $t$ numerators
            Float v1 = Determinant(new SquareMatrix3(deltao.x, ray.d.x, perp.x, deltao.y, ray.d.y, perp.y, deltao.z,
                                                     ray.d.z, perp.z));
            Float t1 = Determinant(new SquareMatrix3(deltao.x, ud.x, perp.x, deltao.y, ud.y, perp.y, deltao.z, ud.z,
                                                     perp.z));

            // Set _u_, _v_, and _t_ if intersection is valid
            if (t1 > p2 * eps && 0 <= v1 && v1 <= p2)
            {
                u = u1;
                v = v1 / p2;
                t = t1 / p2;
            }
        }

        // Compute $v$ and $t$ for the second $u$ intersection
        if (0 <= u2 && u2 <= 1 && u2 != u1)
        {
            Point3f uo = Lerp(u2, p00, p10);
            Vector3f ud = Lerp(u2, p01, p11) - uo;
            Vector3f deltao = uo - ray.o;
            Vector3f perp = Cross(ray.d, ud);
            Float p2 = LengthSquared(perp);
            Float v2 = Determinant(new SquareMatrix3(deltao.x, ray.d.x, perp.x, deltao.y, ray.d.y, perp.y, deltao.z,
                                                     ray.d.z, perp.z));
            Float t2 = Determinant(new SquareMatrix3(deltao.x, ud.x, perp.x, deltao.y, ud.y, perp.y, deltao.z, ud.z,
                                                     perp.z));
            t2 /= p2;
            if (0 <= v2 && v2 <= p2 && t > t2 && t2 > eps)
            {
                t = t2;
                u = u2;
                v = v2 / p2;
            }
        }

        // TODO: reject hits with sufficiently small t that we're not sure.
        // Check intersection $t$ against _tMax_ and possibly return intersection
        if (t >= tMax)
            return null;
        return new BilinearIntersection(new Point2f(u, v), t);
    }

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        nBLPHits.Denom();
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        BilinearIntersection? blpIsect = IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11);
        if (!blpIsect.HasValue)
            return null;
        nBLPHits.Num();
        SurfaceInteraction intr = InteractionFromIntersection(mesh, blpIndex, blpIsect.Value.uv, ray.time, -ray.d);
        return new ShapeIntersection(intr, blpIsect.Value.t);
    }

    public override bool IntersectP(in Ray ray, Float tMax = Infinity)
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);
        return IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11).HasValue;
    }

    public static SurfaceInteraction InteractionFromIntersection(BilinearPatchMesh mesh, int blpIndex, Point2f uv,
                                                                 Float time, Vector3f wo)
    {
        // Compute bilinear patch point $\pt{}$, $\dpdu$, and $\dpdv$ for $(u,v)$
        // Get bilinear patch vertices in _p00_, _p01_, _p10_, and _p11_
        int v0 = mesh.vertexIndices[4 * blpIndex], v1 = mesh.vertexIndices[4 * blpIndex + 1],
            v2 = mesh.vertexIndices[4 * blpIndex + 2], v3 = mesh.vertexIndices[4 * blpIndex + 3];
        Point3f p00 = mesh.p[v0], p10 = mesh.p[v1];
        Point3f p01 = mesh.p[v2], p11 = mesh.p[v3];

        Point3f p = Lerp(uv[0], Lerp(uv[1], p00, p01), Lerp(uv[1], p10, p11));
        Vector3f dpdu = Lerp(uv[1], p10, p11) - Lerp(uv[1], p00, p01);
        Vector3f dpdv = Lerp(uv[0], p01, p11) - Lerp(uv[0], p00, p10);

        // Compute $(s,t)$ texture coordinates at bilinear patch $(u,v)$
        Point2f st = uv;
        Float duds = 1, dudt = 0, dvds = 0, dvdt = 1;
        if (mesh.uv != null)
        {
            // Compute texture coordinates for bilinear patch intersection point
            Point2f uv00 = mesh.uv[v0], uv10 = mesh.uv[v1];
            Point2f uv01 = mesh.uv[v2], uv11 = mesh.uv[v3];
            st = Lerp(uv[0], Lerp(uv[1], uv00, uv01), Lerp(uv[1], uv10, uv11));

            // Update bilinear patch $\dpdu$ and $\dpdv$ accounting for $(s,t)$
            // Compute partial derivatives of $(u,v)$ with respect to $(s,t)$
            Vector2f dstdu = Lerp(uv[1], uv10, uv11) - Lerp(uv[1], uv00, uv01);
            Vector2f dstdv = Lerp(uv[0], uv01, uv11) - Lerp(uv[0], uv00, uv10);
            duds = FMath.Abs(dstdu[0]) < 1e-8f ? 0 : 1 / dstdu[0];
            dvds = FMath.Abs(dstdv[0]) < 1e-8f ? 0 : 1 / dstdv[0];
            dudt = FMath.Abs(dstdu[1]) < 1e-8f ? 0 : 1 / dstdu[1];
            dvdt = FMath.Abs(dstdv[1]) < 1e-8f ? 0 : 1 / dstdv[1];

            // Compute partial derivatives of $\pt{}$ with respect to $(s,t)$
            Vector3f dpds = dpdu * duds + dpdv * dvds;
            Vector3f dpdt = dpdu * dudt + dpdv * dvdt;

            // Set _dpdu_ and _dpdv_ to updated partial derivatives
            if (Cross(dpds, dpdt) != new Vector3f(0, 0, 0))
            {
                if (Dot(Cross(dpdu, dpdv), Cross(dpds, dpdt)) < 0)
                    dpdt = -dpdt;
                DCHECK(Dot(Normalize(Cross(dpdu, dpdv)), Normalize(Cross(dpds, dpdt))) >= -1e-3);
                dpdu = dpds;
                dpdv = dpdt;
            }
        }

        // Find partial derivatives $\dndu$ and $\dndv$ for bilinear patch
        Vector3f d2Pduu = new(0, 0, 0), d2Pdvv = new(0, 0, 0);
        Vector3f d2Pduv = (p00 - p01) + (p11 - p10);
        // Compute coefficients for fundamental forms
        Float E = Dot(dpdu, dpdu), F = Dot(dpdu, dpdv), G = Dot(dpdv, dpdv);
        Vector3f n = Normalize(Cross(dpdu, dpdv));
        Float e = Dot(n, d2Pduu), f = Dot(n, d2Pduv), g = Dot(n, d2Pdvv);

        // Compute $\dndu$ and $\dndv$ from fundamental form coefficients
        Float EGF2 = DifferenceOfProducts(E, G, F, F);
        Float invEGF2 = (EGF2 == 0) ? 0 : 1 / EGF2;
        Normal3f dndu = new((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
        Normal3f dndv = new((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);

        // Update $\dndu$ and $\dndv$ to account for $(s,t)$ parameterization
        Normal3f dnds = dndu * duds + dndv * dvds;
        Normal3f dndt = dndu * dudt + dndv * dvdt;
        dndu = dnds;
        dndv = dndt;

        // Initialize bilinear patch intersection point error _pError_
        Point3f pAbsSum = Abs(p00) + Abs(p01) + Abs(p10) + Abs(p11);
        Vector3f pError = Gamma(6) * new Vector3f(pAbsSum);

        // Initialize _SurfaceInteraction_ for bilinear patch intersection
        int faceIndex = mesh.faceIndices != null ? mesh.faceIndices[blpIndex] : 0;
        bool flipNormal = mesh.reverseOrientation ^ mesh.transformSwapsHandedness;
        SurfaceInteraction isect = new(new Point3fi(p, pError), st, wo, dpdu, dpdv, dndu, dndv, time, flipNormal,
                                       faceIndex);

        // Compute bilinear patch shading normal if necessary
        if (mesh.n != null)
        {
            // Compute shading normals for bilinear patch intersection point
            Normal3f n00 = mesh.n[v0], n10 = mesh.n[v1];
            Normal3f n01 = mesh.n[v2], n11 = mesh.n[v3];
            Normal3f ns = Lerp(uv[0], Lerp(uv[1], n00, n01), Lerp(uv[1], n10, n11));
            if (LengthSquared(ns) > 0)
            {
                ns = Normalize(ns);
                // Set shading geometry for bilinear patch intersection
                Normal3f dndus = Lerp(uv[1], n10, n11) - Lerp(uv[1], n00, n01);
                Normal3f dndvs = Lerp(uv[0], n01, n11) - Lerp(uv[0], n00, n10);
                // Update $\dndu$ and $\dndv$ to account for $(s,t)$ parameterization
                Normal3f dndss = dndus * duds + dndvs * dvds;
                Normal3f dndts = dndus * dudt + dndvs * dvdt;
                dndus = dndss;
                dndvs = dndts;

                Transform r = RotateFromTo(new Vector3f(Normalize(isect.n)), new Vector3f(ns));
                isect.SetShadingGeometry(ns, r.Apply(dpdu), r.Apply(dpdv), dndus, dndvs, true);
            }
        }

        return isect;
    }

    // Initialize _w_ array with differential area at bilinear patch corners
    private static Array4<Float> AreaWeights(Point3f p00, Point3f p10, Point3f p01, Point3f p11)
    {
        Array4<Float> w = default;
        w[0] = Length(Cross(p10 - p00, p01 - p00));
        w[1] = Length(Cross(p10 - p00, p11 - p10));
        w[2] = Length(Cross(p01 - p00, p11 - p01));
        w[3] = Length(Cross(p11 - p10, p11 - p01));
        return w;
    }

    public override ShapeSample? Sample(Point2f u)
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        // Sample bilinear patch parametric $(u,v)$ coordinates
        Float pdf = 1;
        Point2f uv;
        if (mesh.imageDistribution != null)
            uv = mesh.imageDistribution.Sample(u, out pdf, out _);
        else if (!IsRectangle())
        {
            // Sample patch $(u,v)$ with approximate uniform area sampling
            Array4<Float> w = AreaWeights(p00, p10, p01, p11);
            uv = SampleBilinear(u, w);
            pdf = BilinearPDF(uv, w);
        }
        else
            uv = u;

        // Compute bilinear patch geometric quantities at sampled $(u,v)$
        // Compute $\pt{}$, $\dpdu$, and $\dpdv$ for sampled $(u,v)$
        Point3f pu0 = Lerp(uv[1], p00, p01), pu1 = Lerp(uv[1], p10, p11);
        Point3f p = Lerp(uv[0], pu0, pu1);
        Vector3f dpdu = pu1 - pu0;
        Vector3f dpdv = Lerp(uv[0], p01, p11) - Lerp(uv[0], p00, p10);
        if (LengthSquared(dpdu) == 0 || LengthSquared(dpdv) == 0)
            return null;

        Point2f st = uv;
        if (mesh.uv != null)
            // Compute texture coordinates for bilinear patch intersection point
            st = Lerp(uv[0], Lerp(uv[1], UV(0), UV(2)), Lerp(uv[1], UV(1), UV(3)));
        // Compute surface normal for sampled bilinear patch $(u,v)$
        Normal3f n = new(Normalize(Cross(dpdu, dpdv)));
        // Flip normal at sampled $(u,v)$ if necessary
        if (mesh.n != null)
        {
            Normal3f ns = Lerp(uv[0], Lerp(uv[1], N(0), N(2)), Lerp(uv[1], N(1), N(3)));
            n = FaceForward(n, ns);
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n = -n;

        // Compute _pError_ for sampled bilinear patch $(u,v)$
        Point3f pAbsSum = Abs(p00) + Abs(p01) + Abs(p10) + Abs(p11);
        Vector3f pError = Gamma(6) * new Vector3f(pAbsSum);

        // Return _ShapeSample_ for sampled bilinear patch point
        return new ShapeSample(new Interaction(new Point3fi(p, pError), n, st), pdf / Length(Cross(dpdu, dpdv)));
    }

    public override Float PDF(in Interaction intr)
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        // Compute parametric $(u,v)$ of point on bilinear patch
        Point2f uv = intr.uv;
        if (mesh.uv != null)
            uv = InvertBilinear(uv, [UV(0), UV(1), UV(2), UV(3)]);

        // Compute PDF for sampling the $(u,v)$ coordinates given by _intr.uv_
        Float pdf;
        if (mesh.imageDistribution != null)
            pdf = mesh.imageDistribution.PDF(uv);
        else if (!IsRectangle())
        {
            Array4<Float> w = AreaWeights(p00, p10, p01, p11);
            pdf = BilinearPDF(uv, w);
        }
        else
            pdf = 1;

        // Find $\dpdu$ and $\dpdv$ at bilinear patch $(u,v)$
        Point3f pu0 = Lerp(uv[1], p00, p01), pu1 = Lerp(uv[1], p10, p11);
        Vector3f dpdu = pu1 - pu0;
        Vector3f dpdv = Lerp(uv[0], p01, p11) - Lerp(uv[0], p00, p10);

        // Return final bilinear patch area sampling PDF
        return pdf / Length(Cross(dpdu, dpdv));
    }

    private static Array4<Float> CosineWeights(Vector3f v00, Vector3f v10, Vector3f v01, Vector3f v11, Normal3f ns)
    {
        // Compute $\cos\theta$ weights for rectangle seen from reference point
        Array4<Float> w = default;
        w[0] = FMath.Max((Float)0.01, AbsDot(v00, ns));
        w[1] = FMath.Max((Float)0.01, AbsDot(v10, ns));
        w[2] = FMath.Max((Float)0.01, AbsDot(v01, ns));
        w[3] = FMath.Max((Float)0.01, AbsDot(v11, ns));
        return w;
    }

    public override ShapeSample? Sample(in ShapeSampleContext ctx, Point2f u)
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        // Sample bilinear patch with respect to solid angle from reference point
        Vector3f v00 = Normalize(p00 - ctx.p()), v10 = Normalize(p10 - ctx.p());
        Vector3f v01 = Normalize(p01 - ctx.p()), v11 = Normalize(p11 - ctx.p());
        if (!IsRectangle() || mesh.imageDistribution != null ||
            SphericalQuadArea(v00, v10, v11, v01) <= MinSphericalSampleArea)
            return SampleByArea(ctx, u);

        // Sample direction to rectangular bilinear patch
        Float pdf = 1;
        // Warp uniform sample _u_ to account for incident $\cos\theta$ factor
        if (ctx.ns != new Normal3f(0, 0, 0))
        {
            Array4<Float> w = CosineWeights(v00, v10, v01, v11, ctx.ns);
            u = SampleBilinear(u, w);
            pdf *= BilinearPDF(u, w);
        }

        // Sample spherical rectangle at reference point
        Vector3f eu = p10 - p00, ev = p01 - p00;
        Point3f p = SampleSphericalRectangle(ctx.p(), p00, eu, ev, u, out Float quadPDF);
        pdf *= quadPDF;

        // Compute $(u,v)$ and surface normal for sampled point on rectangle
        Point2f uv = new(Dot(p - p00, eu) / DistanceSquared(p10, p00), Dot(p - p00, ev) / DistanceSquared(p01, p00));
        Normal3f n = new(Normalize(Cross(eu, ev)));
        // Flip normal at sampled $(u,v)$ if necessary
        if (mesh.n != null)
        {
            Normal3f ns = Lerp(uv[0], Lerp(uv[1], N(0), N(2)), Lerp(uv[1], N(1), N(3)));
            n = FaceForward(n, ns);
        }
        else if (mesh.reverseOrientation ^ mesh.transformSwapsHandedness)
            n = -n;

        // Compute $(s,t)$ texture coordinates for sampled $(u,v)$
        Point2f st = uv;
        if (mesh.uv != null)
            // Compute texture coordinates for bilinear patch intersection point
            st = Lerp(uv[0], Lerp(uv[1], UV(0), UV(2)), Lerp(uv[1], UV(1), UV(3)));

        return new ShapeSample(new Interaction(p, n, ctx.time, st), pdf);
    }

    public override Float PDF(in ShapeSampleContext ctx, Vector3f wi)
    {
        GetVertices(out Point3f p00, out Point3f p10, out Point3f p01, out Point3f p11);

        // Compute solid angle PDF for sampling bilinear patch from _ctx_
        // Intersect sample ray with shape geometry
        Ray ray = ctx.SpawnRay(wi);
        ShapeIntersection? isect = Intersect(ray);
        if (!isect.HasValue)
            return 0;

        Vector3f v00 = Normalize(p00 - ctx.p()), v10 = Normalize(p10 - ctx.p());
        Vector3f v01 = Normalize(p01 - ctx.p()), v11 = Normalize(p11 - ctx.p());
        if (!IsRectangle() || mesh.imageDistribution != null ||
            SphericalQuadArea(v00, v10, v11, v01) <= MinSphericalSampleArea)
        {
            // Return solid angle PDF for area-sampled bilinear patch
            Float pdf = PDF(isect.Value.intr) * (DistanceSquared(ctx.p(), isect.Value.intr.p()) /
                                                AbsDot(isect.Value.intr.n, -wi));
            return IsInf(pdf) ? 0 : pdf;
        }
        else
        {
            // Return PDF for sample in spherical rectangle
            Float pdf = 1 / SphericalQuadArea(v00, v10, v11, v01);
            if (ctx.ns != new Normal3f(0, 0, 0))
            {
                Array4<Float> w = CosineWeights(v00, v10, v01, v11, ctx.ns);
                Point2f u = InvertSphericalRectangleSample(ctx.p(), p00, p10 - p00, p01 - p00, isect.Value.intr.p());
                return BilinearPDF(u, w) * pdf;
            }
            else
                return pdf;
        }
    }

    public override string ToString() =>
        $"[ BilinearPatch meshIndex: {meshIndex} blpIndex: {blpIndex} area: {FloatToString(area)} ]";
}

// The Triangle mesh creation from the parameter dictionary.
public sealed partial class Triangle
{
    public static TriangleMesh? CreateMesh(Transform renderFromObject, bool reverseOrientation,
                                           ParameterDictionary parameters, FileLoc? loc)
    {
        List<int> vi = parameters.GetIntArray("indices");
        List<Point3f> P = parameters.GetPoint3fArray("P");
        List<Point2f> uvs = parameters.GetPoint2fArray("uv");

        if (vi.Count == 0)
        {
            if (P.Count == 3)
                vi = [0, 1, 2];
            else
            {
                Error(loc, "Vertex indices \"indices\" must be provided with " + "triangle mesh.");
                return null;
            }
        }
        else if ((vi.Count % 3) != 0)
        {
            Error(loc, $"Number of vertex indices {vi.Count} not a multiple of 3. Discarding {vi.Count % 3} " +
                       "excess.");
            while ((vi.Count % 3) != 0)
                vi.RemoveAt(vi.Count - 1);
        }

        if (P.Count == 0)
        {
            Error(loc, "Vertex positions \"P\" must be provided with triangle mesh.");
            return null;
        }

        if (uvs.Count > 0 && uvs.Count != P.Count)
        {
            Error(loc, "Number of \"uv\"s for triangle mesh must match \"P\"s. " + "Discarding uvs.");
            uvs = [];
        }

        List<Vector3f> S = parameters.GetVector3fArray("S");
        if (S.Count > 0 && S.Count != P.Count)
        {
            Error(loc, "Number of \"S\"s for triangle mesh must match \"P\"s. " + "Discarding \"S\"s.");
            S = [];
        }
        List<Normal3f> N = parameters.GetNormal3fArray("N");
        if (N.Count > 0 && N.Count != P.Count)
        {
            Error(loc, "Number of \"N\"s for triangle mesh must match \"P\"s. " + "Discarding \"N\"s.");
            N = [];
        }

        // (pbrt compares the signed index with the unsigned size, so negative
        // indices are out of bounds too.)
        for (int i = 0; i < vi.Count; ++i)
            if ((uint)vi[i] >= (uint)P.Count)
            {
                Error(loc, $"trianglemesh has out of-bounds vertex index {vi[i]} ({P.Count} \"P\" " +
                           "values were given. Discarding this mesh.");
                return null;
            }

        List<int> faceIndices = parameters.GetIntArray("faceIndices");
        if (faceIndices.Count > 0 && faceIndices.Count != vi.Count / 3)
        {
            Error(loc, $"Number of face indices {faceIndices.Count} does not match number of triangles " +
                       $"{vi.Count / 3}. Discarding face indices.");
            faceIndices = [];
        }

        return new TriangleMesh(renderFromObject, reverseOrientation, vi, P, S, N, uvs, faceIndices);
    }
}

public static partial class Util
{
    // Triangle and bilinear patch intersection functions
    public static TriangleIntersection? IntersectTriangle(in Ray ray, Float tMax, Point3f p0, Point3f p1, Point3f p2) =>
        Triangle.IntersectTriangle(ray, tMax, p0, p1, p2);

    public static BilinearIntersection? IntersectBilinearPatch(in Ray ray, Float tMax, Point3f p00, Point3f p10,
                                                               Point3f p01, Point3f p11) =>
        BilinearPatch.IntersectBilinearPatch(ray, tMax, p00, p10, p01, p11);

    public static string ToString(CurveType type) => type switch
    {
        CurveType.Flat => "Flat",
        CurveType.Cylinder => "Cylinder",
        CurveType.Ribbon => "Ribbon",
        _ => throw new UnreachableException()
    };

    // pbrt's DifferenceOfProducts() template with a Float and a vector or
    // normal, using the componentwise FMA.
    public static Vector3f DifferenceOfProducts(Float a, Vector3f b, Float c, Vector3f d)
    {
        Vector3f cd = c * d;
        Vector3f differenceOfProducts = new(FMA(a, b.x, -cd.x), FMA(a, b.y, -cd.y), FMA(a, b.z, -cd.z));
        Vector3f error = new(FMA(-c, d.x, cd.x), FMA(-c, d.y, cd.y), FMA(-c, d.z, cd.z));
        return differenceOfProducts + error;
    }

    public static Normal3f DifferenceOfProducts(Float a, Normal3f b, Float c, Normal3f d)
    {
        Normal3f cd = c * d;
        Normal3f differenceOfProducts = new(FMA(a, b.x, -cd.x), FMA(a, b.y, -cd.y), FMA(a, b.z, -cd.z));
        Normal3f error = new(FMA(-c, d.x, cd.x), FMA(-c, d.y, cd.y), FMA(-c, d.z, cd.z));
        return differenceOfProducts + error;
    }
}

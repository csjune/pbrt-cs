// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/ray.h and ray.cpp
//
// pbrt's ray(t) is ray.At(t). C# structs cannot inherit, so RayDifferential
// repeats Ray's members and converts implicitly to Ray (like C++ slicing).

namespace Pbrt;

// Ray Definition
public struct Ray
{
    // Ray Public Members
    public Point3f o;
    public Vector3f d;
    public Float time;
    public Medium? medium;

    // Ray Public Methods
    public Ray(Point3f o, Vector3f d, Float time = 0f, Medium? medium = null)
    {
        this.o = o;
        this.d = d;
        this.time = time;
        this.medium = medium;
    }

    public readonly bool HasNaN() => o.HasNaN() || d.HasNaN();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Point3f At(Float t) => o + d * t;

    public override readonly string ToString() =>
        FormattableString.Invariant($"[ o: {o} d: {d} time: {time}, medium: {medium?.ToString() ?? "(nullptr)"} ]");
}

// RayDifferential Definition
public struct RayDifferential
{
    // Ray Public Members
    public Point3f o;
    public Vector3f d;
    public Float time;
    public Medium? medium;

    // RayDifferential Public Members
    public bool hasDifferentials;
    public Point3f rxOrigin, ryOrigin;
    public Vector3f rxDirection, ryDirection;

    // RayDifferential Public Methods
    public RayDifferential(Point3f o, Vector3f d, Float time = 0f, Medium? medium = null)
    {
        this.o = o;
        this.d = d;
        this.time = time;
        this.medium = medium;
    }

    public RayDifferential(in Ray ray) : this(ray.o, ray.d, ray.time, ray.medium) { }

    public static implicit operator Ray(in RayDifferential r) => new(r.o, r.d, r.time, r.medium);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Point3f At(Float t) => o + d * t;

    public void ScaleDifferentials(Float s)
    {
        rxOrigin = o + (rxOrigin - o) * s;
        ryOrigin = o + (ryOrigin - o) * s;
        rxDirection = d + (rxDirection - d) * s;
        ryDirection = d + (ryDirection - d) * s;
    }

    public readonly bool HasNaN() =>
        o.HasNaN() || d.HasNaN() ||
        (hasDifferentials && (rxOrigin.HasNaN() || ryOrigin.HasNaN() ||
                              rxDirection.HasNaN() || ryDirection.HasNaN()));

    public override readonly string ToString() =>
        FormattableString.Invariant(
            $"[ ray: {(Ray)this} differentials: {hasDifferentials} xo: {rxOrigin} xd: {rxDirection} yo: {ryOrigin} yd: {ryDirection} ]");
}

public static partial class Util
{
    // Ray Inline Functions
    public static Point3f OffsetRayOrigin(Point3fi pi, Normal3f n, Vector3f w)
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

    public static Ray SpawnRay(Point3fi pi, Normal3f n, Float time, Vector3f d) =>
        new(OffsetRayOrigin(pi, n, d), d, time);

    public static Ray SpawnRayTo(Point3fi pFrom, Normal3f n, Float time, Point3f pTo)
    {
        Vector3f d = pTo - new Point3f(pFrom);
        return SpawnRay(pFrom, n, time, d);
    }

    public static Ray SpawnRayTo(Point3fi pFrom, Normal3f nFrom, Float time, Point3fi pTo, Normal3f nTo)
    {
        Point3f pf = OffsetRayOrigin(pFrom, nFrom, new Point3f(pTo) - new Point3f(pFrom));
        Point3f pt = OffsetRayOrigin(pTo, nTo, pf - new Point3f(pTo));
        return new Ray(pf, pt - pf, time);
    }
}

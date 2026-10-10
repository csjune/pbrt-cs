// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/vecmath.h and vecmath.cpp: the type-specific
// parts. The repetitive Tuple2/Tuple3/Bounds code is generated into
// VecMath.Tuples.g.cs by tools/gen-vecmath.ps1.
//
// Also holds the vector-dependent functions of math.h/math.cpp
// (EqualAreaSquareToSphere() and friends, SquareMatrix * vector).

namespace Pbrt;

// Vector3fi Definition
public partial struct Vector3fi
{
    // Vector3fi Public Methods
    public Vector3fi(Float x, Float y, Float z) : this(new Interval(x), new Interval(y), new Interval(z)) { }

    public Vector3fi(Vector3f p) : this(new Interval(p.x), new Interval(p.y), new Interval(p.z)) { }

    public Vector3fi(Point3f p) : this(new Interval(p.x), new Interval(p.y), new Interval(p.z)) { }

    public Vector3fi(Point3fi p) : this(p.x, p.y, p.z) { }

    public Vector3fi(Vector3f v, Vector3f e)
        : this(Interval.FromValueAndError(v.x, e.x), Interval.FromValueAndError(v.y, e.y),
               Interval.FromValueAndError(v.z, e.z)) { }

    public static implicit operator Vector3fi(Vector3f v) => new(v);

    public readonly Vector3f Error() => new(x.Width / 2, y.Width / 2, z.Width / 2);

    public readonly bool IsExact() => x.Width == 0 && y.Width == 0 && z.Width == 0;
}

// Point3fi Definition
public partial struct Point3fi
{
    public Point3fi(Float x, Float y, Float z) : this(new Interval(x), new Interval(y), new Interval(z)) { }

    public Point3fi(Point3f p) : this(new Interval(p.x), new Interval(p.y), new Interval(p.z)) { }

    public Point3fi(Vector3fi v) : this(v.x, v.y, v.z) { }

    public Point3fi(Point3f p, Vector3f e)
        : this(Interval.FromValueAndError(p.x, e.x), Interval.FromValueAndError(p.y, e.y),
               Interval.FromValueAndError(p.z, e.z)) { }

    // Point3fi +/- Vector3f and Point3f work through these implicit conversions.
    public static implicit operator Point3fi(Point3f p) => new(p);

    public readonly Vector3f Error() => new(x.Width / 2, y.Width / 2, z.Width / 2);

    public readonly bool IsExact() => x.Width == 0 && y.Width == 0 && z.Width == 0;
}

public partial struct Vector3f
{
    // Interval components are converted via their midpoints.
    public Vector3f(Vector3fi v) : this((Float)v.x, (Float)v.y, (Float)v.z) { }

    // SquareMatrix<3> * vector (pbrt's Mul<Vector3f>()).
    public static Vector3f operator *(in SquareMatrix3 m, Vector3f v)
    {
        Vector3f result = default;
        for (int i = 0; i < 3; ++i)
        {
            Float r = 0;
            for (int j = 0; j < 3; ++j)
                r += m[i, j] * v[j];
            result[i] = r;
        }
        return result;
    }
}

public partial struct Point3f
{
    // Interval components are converted via their midpoints.
    public Point3f(Point3fi p) : this((Float)p.x, (Float)p.y, (Float)p.z) { }
}

public partial struct Normal3f
{
    public Normal3f(Point3f v) : this(v.x, v.y, v.z) { }
}

// Quaternion Definition
public struct Quaternion : IEquatable<Quaternion>
{
    // Quaternion Public Members
    public Vector3f v;
    public Float w;

    // Quaternion Public Methods
    // NOTE: new Quaternion() is the identity (w = 1); default(Quaternion) has w = 0.
    public Quaternion()
    {
        v = default;
        w = 1;
    }

    public Quaternion(Vector3f v, Float w)
    {
        this.v = v;
        this.w = w;
    }

    public static Quaternion operator +(Quaternion a, Quaternion b) => new(a.v + b.v, a.w + b.w);
    public static Quaternion operator -(Quaternion q) => new(-q.v, -q.w);
    public static Quaternion operator -(Quaternion a, Quaternion b) => new(a.v - b.v, a.w - b.w);
    public static Quaternion operator *(Quaternion q, Float f) => new(q.v * f, q.w * f);
    public static Quaternion operator *(Float f, Quaternion q) => q * f;

    public static Quaternion operator /(Quaternion q, Float f)
    {
        DCHECK_NE(0, f);
        return new(q.v / f, q.w / f);
    }

    public static bool operator ==(Quaternion a, Quaternion b) => a.v == b.v && a.w == b.w;
    public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
    public readonly bool Equals(Quaternion other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Quaternion other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(v, w);

    public override readonly string ToString() =>
        FormattableString.Invariant($"[ {v.x}, {v.y}, {v.z}, {w} ]");
}

public partial struct Bounds2f
{
    public readonly Point2f Lerp(Point2f t) =>
        new(Util.Lerp(t.x, pMin.x, pMax.x), Util.Lerp(t.y, pMin.y, pMax.y));

    public readonly Vector2f Offset(Point2f p)
    {
        Vector2f o = p - pMin;
        if (pMax.x > pMin.x)
            o.x /= pMax.x - pMin.x;
        if (pMax.y > pMin.y)
            o.y /= pMax.y - pMin.y;
        return o;
    }

    public readonly void BoundingSphere(out Point2f c, out Float rad)
    {
        c = (pMin + pMax) / 2;
        rad = Inside(c, this) ? Distance(c, pMax) : 0;
    }
}

public partial struct Bounds2i
{
    // Iterates over the integer points p with pMin <= p < pMax, x fastest
    // (pbrt's begin()/end() for Bounds2i).
    public readonly Bounds2iIterator GetEnumerator() => new(this);
}

public struct Bounds2iIterator
{
    private readonly Bounds2i bounds;
    private Point2i p;
    private bool started;

    public Bounds2iIterator(Bounds2i b)
    {
        bounds = b;
        p = b.pMin;
        started = false;
    }

    public readonly Point2i Current => p;

    public bool MoveNext()
    {
        if (!started)
        {
            started = true;
            // If the bounds are degenerate, exit out immediately.
            return !(bounds.pMin.x >= bounds.pMax.x || bounds.pMin.y >= bounds.pMax.y);
        }
        ++p.x;
        if (p.x == bounds.pMax.x)
        {
            p.x = bounds.pMin.x;
            ++p.y;
        }
        return p.y < bounds.pMax.y;
    }
}

public partial struct Bounds3f
{
    public readonly Point3f Lerp(Point3f t) =>
        new(Util.Lerp(t.x, pMin.x, pMax.x), Util.Lerp(t.y, pMin.y, pMax.y),
            Util.Lerp(t.z, pMin.z, pMax.z));

    public readonly Vector3f Offset(Point3f p)
    {
        Vector3f o = p - pMin;
        if (pMax.x > pMin.x)
            o.x /= pMax.x - pMin.x;
        if (pMax.y > pMin.y)
            o.y /= pMax.y - pMin.y;
        if (pMax.z > pMin.z)
            o.z /= pMax.z - pMin.z;
        return o;
    }

    public readonly void BoundingSphere(out Point3f center, out Float radius)
    {
        center = (pMin + pMax) / 2;
        radius = Inside(center, this) ? Distance(center, pMax) : 0;
    }

    public readonly bool IntersectP(Point3f o, Vector3f d, Float tMax = Infinity) =>
        IntersectP(o, d, tMax, out _, out _);

    public readonly bool IntersectP(Point3f o, Vector3f d, Float tMax, out Float hitt0, out Float hitt1)
    {
        hitt0 = hitt1 = 0;
        Float t0 = 0, t1 = tMax;
        for (int i = 0; i < 3; ++i)
        {
            // Update interval for _i_th bounding box slab
            Float invRayDir = 1 / d[i];
            Float tNear = (pMin[i] - o[i]) * invRayDir;
            Float tFar = (pMax[i] - o[i]) * invRayDir;
            // Update parametric interval from slab intersection $t$ values
            if (tNear > tFar)
                (tNear, tFar) = (tFar, tNear);
            // Update _tFar_ to ensure robust ray--bounds intersection
            tFar *= 1 + 2 * Gamma(3);

            t0 = tNear > t0 ? tNear : t0;
            t1 = tFar < t1 ? tFar : t1;
            if (t0 > t1)
                return false;
        }
        hitt0 = t0;
        hitt1 = t1;
        return true;
    }

    public readonly bool IntersectP(Point3f o, Vector3f d, Float raytMax, Vector3f invDir,
                                    ReadOnlySpan<int> dirIsNeg)
    {
        // Check for ray intersection against $x$ and $y$ slabs
        Float tMin = (this[dirIsNeg[0]].x - o.x) * invDir.x;
        Float tMax = (this[1 - dirIsNeg[0]].x - o.x) * invDir.x;
        Float tyMin = (this[dirIsNeg[1]].y - o.y) * invDir.y;
        Float tyMax = (this[1 - dirIsNeg[1]].y - o.y) * invDir.y;
        // Update _tMax_ and _tyMax_ to ensure robust bounds intersection
        tMax *= 1 + 2 * Gamma(3);
        tyMax *= 1 + 2 * Gamma(3);

        if (tMin > tyMax || tyMin > tMax)
            return false;
        if (tyMin > tMin)
            tMin = tyMin;
        if (tyMax < tMax)
            tMax = tyMax;

        // Check for ray intersection against $z$ slab
        Float tzMin = (this[dirIsNeg[2]].z - o.z) * invDir.z;
        Float tzMax = (this[1 - dirIsNeg[2]].z - o.z) * invDir.z;
        // Update _tzMax_ to ensure robust bounds intersection
        tzMax *= 1 + 2 * Gamma(3);

        if (tMin > tzMax || tzMin > tMax)
            return false;
        if (tzMin > tMin)
            tMin = tzMin;
        if (tzMax < tMax)
            tMax = tzMax;

        return (tMin < raytMax) && (tMax > 0);
    }
}

// OctahedralVector Definition
public struct OctahedralVector
{
    // OctahedralVector Private Members
    private ushort x, y;

    // OctahedralVector Public Methods
    public OctahedralVector(Vector3f v)
    {
        v /= FMath.Abs(v.x) + FMath.Abs(v.y) + FMath.Abs(v.z);
        if (v.z >= 0)
        {
            x = Encode(v.x);
            y = Encode(v.y);
        }
        else
        {
            // Encode octahedral vector with $z < 0$
            x = Encode((1 - FMath.Abs(v.y)) * Sign(v.x));
            y = Encode((1 - FMath.Abs(v.x)) * Sign(v.y));
        }
    }

    public static explicit operator Vector3f(OctahedralVector ov)
    {
        Vector3f v;
        v.x = -1 + 2 * (ov.x / 65535f);
        v.y = -1 + 2 * (ov.y / 65535f);
        v.z = 1 - (FMath.Abs(v.x) + FMath.Abs(v.y));
        // Reparameterize directions in the $z<0$ portion of the octahedron
        if (v.z < 0)
        {
            Float xo = v.x;
            v.x = (1 - FMath.Abs(v.y)) * Sign(xo);
            v.y = (1 - FMath.Abs(xo)) * Sign(v.y);
        }

        return Normalize(v);
    }

    public override readonly string ToString() => $"[ OctahedralVector x: {x} y: {y} ]";

    // OctahedralVector Private Methods
    private static Float Sign(Float v) => FMath.CopySign(1, v);

    // std::round rounds halfway cases away from zero.
    private static ushort Encode(Float f) =>
        (ushort)FMath.Round(Clamp((f + 1) / 2, 0, 1) * 65535f, MidpointRounding.AwayFromZero);
}

// DirectionCone Definition
public struct DirectionCone
{
    // DirectionCone Public Members
    public Vector3f w;
    public Float cosTheta;

    // DirectionCone Public Methods
    // NOTE: new DirectionCone() is empty (cosTheta = Infinity), but
    // default(DirectionCone) has cosTheta = 0 and is not.
    public DirectionCone()
    {
        w = default;
        cosTheta = Infinity;
    }

    public DirectionCone(Vector3f w, Float cosTheta)
    {
        this.w = Normalize(w);
        this.cosTheta = cosTheta;
    }

    public DirectionCone(Vector3f w) : this(w, 1) { }

    public readonly bool IsEmpty() => cosTheta == Infinity;

    public static DirectionCone EntireSphere() => new(new Vector3f(0, 0, 1), -1);

    public override readonly string ToString() =>
        FormattableString.Invariant($"[ DirectionCone w: {w} cosTheta: {cosTheta} ]");

    public readonly Vector3f ClosestVectorInCone(Vector3f wp)
    {
        DCHECK(!IsEmpty());
        wp = Normalize(wp);
        // Return provided vector if it is inside the cone
        if (Dot(wp, w) > cosTheta)
            return wp;

        // Find closest vector by rotating _wp_ until it touches the cone
        Float sinTheta = -SafeSqrt(1 - cosTheta * cosTheta);
        Vector3f a = Cross(wp, w);
        return cosTheta * w +
               (sinTheta / Length(a)) *
                   new Vector3f(w.x * (wp.y * w.y + wp.z * w.z) - wp.x * (Sqr(w.y) + Sqr(w.z)),
                                w.y * (wp.x * w.x + wp.z * w.z) - wp.y * (Sqr(w.x) + Sqr(w.z)),
                                w.z * (wp.x * w.x + wp.y * w.y) - wp.z * (Sqr(w.x) + Sqr(w.y)));
    }
}

// Frame Definition
public struct Frame
{
    // Frame Public Members
    public Vector3f x, y, z;

    // Frame Public Methods
    // NOTE: new Frame() is the identity frame; default(Frame) is all zeros.
    public Frame()
    {
        x = new Vector3f(1, 0, 0);
        y = new Vector3f(0, 1, 0);
        z = new Vector3f(0, 0, 1);
    }

    public Frame(Vector3f x, Vector3f y, Vector3f z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        DCHECK_LT(FMath.Abs(LengthSquared(x) - 1), (Float)1e-4);
        DCHECK_LT(FMath.Abs(LengthSquared(y) - 1), (Float)1e-4);
        DCHECK_LT(FMath.Abs(LengthSquared(z) - 1), (Float)1e-4);
        DCHECK_LT(FMath.Abs(Dot(x, y)), (Float)1e-4);
        DCHECK_LT(FMath.Abs(Dot(y, z)), (Float)1e-4);
        DCHECK_LT(FMath.Abs(Dot(z, x)), (Float)1e-4);
    }

    public static Frame FromXZ(Vector3f x, Vector3f z) => new(x, Cross(z, x), z);
    public static Frame FromXY(Vector3f x, Vector3f y) => new(x, y, Cross(x, y));

    public static Frame FromZ(Vector3f z)
    {
        CoordinateSystem(z, out Vector3f x, out Vector3f y);
        return new Frame(x, y, z);
    }

    public static Frame FromX(Vector3f x)
    {
        CoordinateSystem(x, out Vector3f y, out Vector3f z);
        return new Frame(x, y, z);
    }

    public static Frame FromY(Vector3f y)
    {
        CoordinateSystem(y, out Vector3f z, out Vector3f x);
        return new Frame(x, y, z);
    }

    public static Frame FromX(Normal3f x)
    {
        CoordinateSystem(x, out Vector3f y, out Vector3f z);
        return new Frame(new Vector3f(x), y, z);
    }

    public static Frame FromY(Normal3f y)
    {
        CoordinateSystem(y, out Vector3f z, out Vector3f x);
        return new Frame(x, new Vector3f(y), z);
    }

    public static Frame FromZ(Normal3f z) => FromZ(new Vector3f(z));

    public readonly Vector3f ToLocal(Vector3f v) => new(Dot(v, x), Dot(v, y), Dot(v, z));

    public readonly Normal3f ToLocal(Normal3f n) => new(Dot(n, x), Dot(n, y), Dot(n, z));

    public readonly Vector3f FromLocal(Vector3f v) => v.x * x + v.y * y + v.z * z;

    public readonly Normal3f FromLocal(Normal3f n) => new(n.x * x + n.y * y + n.z * z);

    public override readonly string ToString() => $"[ Frame x: {x} y: {y} z: {z} ]";
}

public static partial class Util
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(Interval fi) => IsNaN((Float)fi);

    // Point2 Inline Functions
    // https://www.iquilezles.org/www/articles/ibilinear/ibilinear.htm,
    // with a fix for perfect quads
    public static Point2f InvertBilinear(Point2f p, ReadOnlySpan<Point2f> vert)
    {
        // The below assumes a quad (vs uv parametric layout) in v....
        Point2f a = vert[0], b = vert[1], c = vert[3], d = vert[2];
        Vector2f e = b - a, f = d - a, g = (a - b) + (c - d), h = p - a;

        static Float cross2d(Vector2f a, Vector2f b) => DifferenceOfProducts(a.x, b.y, a.y, b.x);

        Float k2 = cross2d(g, f);
        Float k1 = cross2d(e, f) + cross2d(h, g);
        Float k0 = cross2d(h, e);

        // if edges are parallel, this is a linear equation
        if (FMath.Abs(k2) < 0.001f)
        {
            if (FMath.Abs(e.x * k1 - g.x * k0) < 1e-5f)
                return new Point2f((h.y * k1 + f.y * k0) / (e.y * k1 - g.y * k0), -k0 / k1);
            else
                return new Point2f((h.x * k1 + f.x * k0) / (e.x * k1 - g.x * k0), -k0 / k1);
        }

        if (!Quadratic(k2, k1, k0, out Float v0, out Float v1))
            return new Point2f(0, 0);

        Float u = (h.x - f.x * v0) / (e.x + g.x * v0);
        if (u < 0 || u > 1 || v0 < 0 || v0 > 1)
            return new Point2f((h.x - f.x * v1) / (e.x + g.x * v1), v1);
        return new Point2f(u, v0);
    }

    // Vector2 Inline Functions
    public static Float Dot(Vector2f v1, Vector2f v2)
    {
        DCHECK(!v1.HasNaN() && !v2.HasNaN());
        return SumOfProducts(v1.x, v2.x, v1.y, v2.y);
    }

    public static Float AbsDot(Vector2f v1, Vector2f v2)
    {
        DCHECK(!v1.HasNaN() && !v2.HasNaN());
        return FMath.Abs(Dot(v1, v2));
    }

    public static Float LengthSquared(Vector2f v) => Sqr(v.x) + Sqr(v.y);

    public static Float Length(Vector2f v) => FMath.Sqrt(LengthSquared(v));

    public static Vector2f Normalize(Vector2f v) => v / Length(v);

    public static Float Distance(Point2f p1, Point2f p2) => Length(p1 - p2);

    public static Float DistanceSquared(Point2f p1, Point2f p2) => LengthSquared(p1 - p2);

    // Vector3 Inline Functions
    public static Vector3f Cross(Vector3f v1, Normal3f v2)
    {
        DCHECK(!v1.HasNaN() && !v2.HasNaN());
        return new(DifferenceOfProducts(v1.y, v2.z, v1.z, v2.y),
                   DifferenceOfProducts(v1.z, v2.x, v1.x, v2.z),
                   DifferenceOfProducts(v1.x, v2.y, v1.y, v2.x));
    }

    public static Vector3f Cross(Normal3f v1, Vector3f v2)
    {
        DCHECK(!v1.HasNaN() && !v2.HasNaN());
        return new(DifferenceOfProducts(v1.y, v2.z, v1.z, v2.y),
                   DifferenceOfProducts(v1.z, v2.x, v1.x, v2.z),
                   DifferenceOfProducts(v1.x, v2.y, v1.y, v2.x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float LengthSquared(Vector3f v) => Sqr(v.x) + Sqr(v.y) + Sqr(v.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Length(Vector3f v) => FMath.Sqrt(LengthSquared(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3f Normalize(Vector3f v) => v / Length(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Dot(Vector3f v, Vector3f w)
    {
        DCHECK(!v.HasNaN() && !w.HasNaN());
        return v.x * w.x + v.y * w.y + v.z * w.z;
    }

    // Equivalent to std::acos(Dot(a, b)), but more numerically stable.
    // via http://www.plunk.org/~hatch/rightway.html
    public static Float AngleBetween(Vector3f v1, Vector3f v2)
    {
        if (Dot(v1, v2) < 0)
            return Pi - 2 * SafeASin(Length(v1 + v2) / 2);
        else
            return 2 * SafeASin(Length(v2 - v1) / 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float AbsDot(Vector3f v1, Vector3f v2)
    {
        DCHECK(!v1.HasNaN() && !v2.HasNaN());
        return FMath.Abs(Dot(v1, v2));
    }

    public static Float AngleBetween(Normal3f a, Normal3f b)
    {
        if (Dot(a, b) < 0)
            return Pi - 2 * SafeASin(Length(a + b) / 2);
        else
            return 2 * SafeASin(Length(b - a) / 2);
    }

    public static Vector3f GramSchmidt(Vector3f v, Vector3f w) => v - Dot(v, w) * w;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3f Cross(Vector3f v, Vector3f w)
    {
        DCHECK(!v.HasNaN() && !w.HasNaN());
        return new(DifferenceOfProducts(v.y, w.z, v.z, w.y),
                   DifferenceOfProducts(v.z, w.x, v.x, w.z),
                   DifferenceOfProducts(v.x, w.y, v.y, w.x));
    }

    public static void CoordinateSystem(Vector3f v1, out Vector3f v2, out Vector3f v3)
    {
        Float sign = FMath.CopySign(1, v1.z);
        Float a = -1 / (sign + v1.z);
        Float b = v1.x * v1.y * a;
        v2 = new Vector3f(1 + sign * Sqr(v1.x) * a, sign * b, -sign * v1.x);
        v3 = new Vector3f(b, sign + Sqr(v1.y) * a, -v1.y);
    }

    public static void CoordinateSystem(Normal3f v1, out Vector3f v2, out Vector3f v3)
    {
        Float sign = FMath.CopySign(1, v1.z);
        Float a = -1 / (sign + v1.z);
        Float b = v1.x * v1.y * a;
        v2 = new Vector3f(1 + sign * Sqr(v1.x) * a, sign * b, -sign * v1.x);
        v3 = new Vector3f(b, sign + Sqr(v1.y) * a, -v1.y);
    }

    // Vector3<Interval> instantiations of the generic Vector3 functions
    public static Interval Dot(Vector3fi v, Vector3fi w) => v.x * w.x + v.y * w.y + v.z * w.z;

    public static Interval LengthSquared(Vector3fi v) => Sqr(v.x) + Sqr(v.y) + Sqr(v.z);

    public static Interval Length(Vector3fi v) => Sqrt(LengthSquared(v));

    public static Vector3fi Normalize(Vector3fi v) => v / Length(v);

    public static Vector3fi Cross(Vector3fi v, Vector3fi w) =>
        new(DifferenceOfProducts(v.y, w.z, v.z, w.y),
            DifferenceOfProducts(v.z, w.x, v.x, w.z),
            DifferenceOfProducts(v.x, w.y, v.y, w.x));

    // As in pbrt's Point3<T> template, the difference here is a vector
    // (unlike Point3fi's own operator-, which returns a Point3fi).
    public static Interval DistanceSquared(Point3fi p1, Point3fi p2) =>
        LengthSquared(new Vector3fi(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z));

    public static Interval Distance(Point3fi p1, Point3fi p2) => Sqrt(DistanceSquared(p1, p2));

    // Point3 Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Distance(Point3f p1, Point3f p2) => Length(p1 - p2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float DistanceSquared(Point3f p1, Point3f p2) => LengthSquared(p1 - p2);

    // Normal3 Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float LengthSquared(Normal3f n) => Sqr(n.x) + Sqr(n.y) + Sqr(n.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Length(Normal3f n) => FMath.Sqrt(LengthSquared(n));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Normal3f Normalize(Normal3f n) => n / Length(n);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Dot(Normal3f n, Vector3f v)
    {
        DCHECK(!n.HasNaN() && !v.HasNaN());
        return FMA(n.x, v.x, SumOfProducts(n.y, v.y, n.z, v.z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Dot(Vector3f v, Normal3f n)
    {
        DCHECK(!v.HasNaN() && !n.HasNaN());
        return FMA(n.x, v.x, SumOfProducts(n.y, v.y, n.z, v.z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Dot(Normal3f n1, Normal3f n2)
    {
        DCHECK(!n1.HasNaN() && !n2.HasNaN());
        return FMA(n1.x, n2.x, SumOfProducts(n1.y, n2.y, n1.z, n2.z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float AbsDot(Normal3f n, Vector3f v) => FMath.Abs(Dot(n, v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float AbsDot(Vector3f v, Normal3f n) => FMath.Abs(Dot(v, n));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float AbsDot(Normal3f n1, Normal3f n2) => FMath.Abs(Dot(n1, n2));

    public static Normal3f FaceForward(Normal3f n, Vector3f v) => (Dot(n, v) < 0f) ? -n : n;
    public static Normal3f FaceForward(Normal3f n, Normal3f n2) => (Dot(n, n2) < 0f) ? -n : n;
    public static Vector3f FaceForward(Vector3f v, Vector3f v2) => (Dot(v, v2) < 0f) ? -v : v;
    public static Vector3f FaceForward(Vector3f v, Normal3f n2) => (Dot(v, n2) < 0f) ? -v : v;

    // Quaternion Inline Functions
    public static Float Dot(Quaternion q1, Quaternion q2) => Dot(q1.v, q2.v) + q1.w * q2.w;

    public static Float Length(Quaternion q) => FMath.Sqrt(Dot(q, q));

    public static Quaternion Normalize(Quaternion q)
    {
        DCHECK_GT(Length(q), 0);
        return q / Length(q);
    }

    public static Float AngleBetween(Quaternion q1, Quaternion q2)
    {
        if (Dot(q1, q2) < 0)
            return Pi - 2 * SafeASin(Length(q1 + q2) / 2);
        else
            return 2 * SafeASin(Length(q2 - q1) / 2);
    }

    // http://www.plunk.org/~hatch/rightway.html
    public static Quaternion Slerp(Float t, Quaternion q1, Quaternion q2)
    {
        Float theta = AngleBetween(q1, q2);
        Float sinThetaOverTheta = SinXOverX(theta);
        return q1 * (1 - t) * SinXOverX((1 - t) * theta) / sinThetaOverTheta +
               q2 * t * SinXOverX(t * theta) / sinThetaOverTheta;
    }

    // Bounds3 Inline Functions
    public static Float DistanceSquared(Point3f p, in Bounds3f b)
    {
        Float dx = Max(Max(0, b.pMin.x - p.x), p.x - b.pMax.x);
        Float dy = Max(Max(0, b.pMin.y - p.y), p.y - b.pMax.y);
        Float dz = Max(Max(0, b.pMin.z - p.z), p.z - b.pMax.z);
        return Sqr(dx) + Sqr(dy) + Sqr(dz);
    }

    public static Float Distance(Point3f p, in Bounds3f b) => FMath.Sqrt(DistanceSquared(p, b));

    // Spherical Geometry Inline Functions
    public static Float SphericalTriangleArea(Vector3f a, Vector3f b, Vector3f c) =>
        FMath.Abs(2 * FMath.Atan2(Dot(a, Cross(b, c)), 1 + Dot(a, b) + Dot(a, c) + Dot(b, c)));

    public static Float SphericalQuadArea(Vector3f a, Vector3f b, Vector3f c, Vector3f d)
    {
        Vector3f axb = Cross(a, b), bxc = Cross(b, c);
        Vector3f cxd = Cross(c, d), dxa = Cross(d, a);
        if (LengthSquared(axb) == 0 || LengthSquared(bxc) == 0 || LengthSquared(cxd) == 0 ||
            LengthSquared(dxa) == 0)
            return 0;
        axb = Normalize(axb);
        bxc = Normalize(bxc);
        cxd = Normalize(cxd);
        dxa = Normalize(dxa);

        Float alpha = AngleBetween(dxa, -axb);
        Float beta = AngleBetween(axb, -bxc);
        Float gamma = AngleBetween(bxc, -cxd);
        Float delta = AngleBetween(cxd, -dxa);

        return FMath.Abs(alpha + beta + gamma + delta - 2 * Pi);
    }

    public static Vector3f SphericalDirection(Float sinTheta, Float cosTheta, Float phi)
    {
        DCHECK(sinTheta >= -1.0001 && sinTheta <= 1.0001);
        DCHECK(cosTheta >= -1.0001 && cosTheta <= 1.0001);
        return new Vector3f(Clamp(sinTheta, -1, 1) * FMath.Cos(phi),
                            Clamp(sinTheta, -1, 1) * FMath.Sin(phi), Clamp(cosTheta, -1, 1));
    }

    public static Float SphericalTheta(Vector3f v) => SafeACos(v.z);

    public static Float SphericalPhi(Vector3f v)
    {
        Float p = FMath.Atan2(v.y, v.x);
        return (p < 0) ? (p + 2 * Pi) : p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float CosTheta(Vector3f w) => w.z;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Cos2Theta(Vector3f w) => Sqr(w.z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float AbsCosTheta(Vector3f w) => FMath.Abs(w.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Sin2Theta(Vector3f w) => Max(0, 1 - Cos2Theta(w));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float SinTheta(Vector3f w) => FMath.Sqrt(Sin2Theta(w));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float TanTheta(Vector3f w) => SinTheta(w) / CosTheta(w);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Tan2Theta(Vector3f w) => Sin2Theta(w) / Cos2Theta(w);

    public static Float CosPhi(Vector3f w)
    {
        Float sinTheta = SinTheta(w);
        return (sinTheta == 0) ? 1 : Clamp(w.x / sinTheta, -1, 1);
    }

    public static Float SinPhi(Vector3f w)
    {
        Float sinTheta = SinTheta(w);
        return (sinTheta == 0) ? 0 : Clamp(w.y / sinTheta, -1, 1);
    }

    public static Float CosDPhi(Vector3f wa, Vector3f wb)
    {
        Float waxy = Sqr(wa.x) + Sqr(wa.y), wbxy = Sqr(wb.x) + Sqr(wb.y);
        if (waxy == 0 || wbxy == 0)
            return 1;
        return Clamp((wa.x * wb.x + wa.y * wb.y) / FMath.Sqrt(waxy * wbxy), -1, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SameHemisphere(Vector3f w, Vector3f wp) => w.z * wp.z > 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SameHemisphere(Vector3f w, Normal3f wp) => w.z * wp.z > 0;

    // DirectionCone Inline Functions
    public static bool Inside(in DirectionCone d, Vector3f w) =>
        !d.IsEmpty() && Dot(d.w, Normalize(w)) >= d.cosTheta;

    public static DirectionCone BoundSubtendedDirections(in Bounds3f b, Point3f p)
    {
        // Compute bounding sphere for _b_ and check if _p_ is inside
        b.BoundingSphere(out Point3f pCenter, out Float radius);
        if (DistanceSquared(p, pCenter) < Sqr(radius))
            return DirectionCone.EntireSphere();

        // Compute and return _DirectionCone_ for bounding sphere
        Vector3f w = Normalize(pCenter - p);
        Float sin2ThetaMax = Sqr(radius) / DistanceSquared(pCenter, p);
        Float cosThetaMax = SafeSqrt(1 - sin2ThetaMax);
        return new DirectionCone(w, cosThetaMax);
    }

    // DirectionCone Function Definitions
    public static DirectionCone Union(in DirectionCone a, in DirectionCone b)
    {
        // Handle the cases where one or both cones are empty
        if (a.IsEmpty())
            return b;
        if (b.IsEmpty())
            return a;

        // Handle the cases where one cone is inside the other
        Float theta_a = SafeACos(a.cosTheta), theta_b = SafeACos(b.cosTheta);
        Float theta_d = AngleBetween(a.w, b.w);
        if (Min(theta_d + theta_b, Pi) <= theta_a)
            return a;
        if (Min(theta_d + theta_a, Pi) <= theta_b)
            return b;

        // Compute the spread angle of the merged cone, $\theta_o$
        Float theta_o = (theta_a + theta_d + theta_b) / 2;
        if (theta_o >= Pi)
            return DirectionCone.EntireSphere();

        // Find the merged cone's axis and return cone union
        Float theta_r = theta_o - theta_a;
        Vector3f wr = Cross(a.w, b.w);
        if (LengthSquared(wr) == 0)
            return DirectionCone.EntireSphere();
        Vector3f w = Rotate(Degrees(theta_r), wr).Apply(a.w);
        return new DirectionCone(w, FMath.Cos(theta_o));
    }

    // Square--Sphere Mapping Function Definitions (math.cpp)
    // Via source code from Clarberg: Fast Equal-Area Mapping of the (Hemi)Sphere using SIMD
    public static Vector3f EqualAreaSquareToSphere(Point2f p)
    {
        CHECK(p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1);
        // Transform _p_ to $[-1,1]^2$ and compute absolute values
        Float u = 2 * p.x - 1, v = 2 * p.y - 1;
        Float up = FMath.Abs(u), vp = FMath.Abs(v);

        // Compute radius _r_ as signed distance from diagonal
        Float signedDistance = 1 - (up + vp);
        Float d = FMath.Abs(signedDistance);
        Float r = 1 - d;

        // Compute angle $\phi$ for square to sphere mapping
        Float phi = (r == 0 ? 1 : (vp - up) / r + 1) * Pi / 4;

        // Find $z$ coordinate for spherical direction
        Float z = FMath.CopySign(1 - Sqr(r), signedDistance);

        // Compute $\cos\phi$ and $\sin\phi$ for original quadrant and return vector
        Float cosPhi = FMath.CopySign(FMath.Cos(phi), u);
        Float sinPhi = FMath.CopySign(FMath.Sin(phi), v);
        return new Vector3f(cosPhi * r * SafeSqrt(2 - Sqr(r)), sinPhi * r * SafeSqrt(2 - Sqr(r)), z);
    }

    // Via source code from Clarberg: Fast Equal-Area Mapping of the (Hemi)Sphere using SIMD
    public static Point2f EqualAreaSphereToSquare(Vector3f d)
    {
        DCHECK(LengthSquared(d) > .999 && LengthSquared(d) < 1.001);
        Float x = FMath.Abs(d.x), y = FMath.Abs(d.y), z = FMath.Abs(d.z);

        // Compute the radius r
        Float r = SafeSqrt(1 - z); // r = sqrt(1-|z|)

        // Compute the argument to atan (detect a=0 to avoid div-by-zero)
        Float a = Max(x, y), b = Min(x, y);
        b = a == 0 ? 0 : b / a;

        // Polynomial approximation of atan(x)*2/pi, x=b
        // Coefficients for 6th degree minimax approximation of atan(x)*2/pi,
        // x=[0,1].
        const Float t1 = (Float)0.406758566246788489601959989e-5;
        const Float t2 = (Float)0.636226545274016134946890922156;
        const Float t3 = (Float)0.61572017898280213493197203466e-2;
        const Float t4 = -(Float)0.247333733281268944196501420480;
        const Float t5 = (Float)0.881770664775316294736387951347e-1;
        const Float t6 = (Float)0.419038818029165735901852432784e-1;
        const Float t7 = -(Float)0.251390972343483509333252996350e-1;
        Float phi = EvaluatePolynomial(b, t1, t2, t3, t4, t5, t6, t7);

        // Extend phi if the input is in the range 45-90 degrees (u<v)
        if (x < y)
            phi = 1 - phi;

        // Find (u,v) based on (r,phi)
        Float v = phi * r;
        Float u = r - v;

        if (d.z < 0)
        {
            // southern hemisphere -> mirror u,v
            (u, v) = (v, u);
            u = 1 - u;
            v = 1 - v;
        }

        // Move (u,v) to the correct quadrant based on the signs of (x,y)
        u = FMath.CopySign(u, d.x);
        v = FMath.CopySign(v, d.y);

        // Transform (u,v) from [-1,1] to [0,1]
        return new Point2f(0.5f * (u + 1), 0.5f * (v + 1));
    }

    public static Point2f WrapEqualAreaSquare(Point2f uv)
    {
        if (uv[0] < 0)
        {
            uv[0] = -uv[0];    // mirror across u = 0
            uv[1] = 1 - uv[1]; // mirror across v = 0.5
        }
        else if (uv[0] > 1)
        {
            uv[0] = 2 - uv[0]; // mirror across u = 1
            uv[1] = 1 - uv[1]; // mirror across v = 0.5
        }
        if (uv[1] < 0)
        {
            uv[0] = 1 - uv[0]; // mirror across u = 0.5
            uv[1] = -uv[1];    // mirror across v = 0;
        }
        else if (uv[1] > 1)
        {
            uv[0] = 1 - uv[0]; // mirror across u = 0.5
            uv[1] = 2 - uv[1]; // mirror across v = 1
        }
        return uv;
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/splines.h
//
// The template BlossomCubicBezier<P>() and EvaluateCubicBezier<P>() are
// written for the point types pbrt uses them with (Point3f, Float and RGB).

namespace Pbrt;

public static partial class Util
{
    // Bezier Inline Functions
    public static Point3f BlossomCubicBezier(ReadOnlySpan<Point3f> p, Float u0, Float u1, Float u2)
    {
        Point3f a0 = Lerp(u0, p[0], p[1]), a1 = Lerp(u0, p[1], p[2]), a2 = Lerp(u0, p[2], p[3]);
        Point3f b0 = Lerp(u1, a0, a1), b1 = Lerp(u1, a1, a2);
        return Lerp(u2, b0, b1);
    }

    public static Float BlossomCubicBezier(ReadOnlySpan<Float> p, Float u0, Float u1, Float u2)
    {
        Float a0 = Lerp(u0, p[0], p[1]), a1 = Lerp(u0, p[1], p[2]), a2 = Lerp(u0, p[2], p[3]);
        Float b0 = Lerp(u1, a0, a1), b1 = Lerp(u1, a1, a2);
        return Lerp(u2, b0, b1);
    }

    public static RGB BlossomCubicBezier(ReadOnlySpan<RGB> p, Float u0, Float u1, Float u2)
    {
        RGB a0 = Lerp(u0, p[0], p[1]), a1 = Lerp(u0, p[1], p[2]), a2 = Lerp(u0, p[2], p[3]);
        RGB b0 = Lerp(u1, a0, a1), b1 = Lerp(u1, a1, a2);
        return Lerp(u2, b0, b1);
    }

    public static RGB EvaluateCubicBezier(ReadOnlySpan<RGB> cp, Float u) => BlossomCubicBezier(cp, u, u, u);

    public static Point3f EvaluateCubicBezier(ReadOnlySpan<Point3f> cp, Float u) =>
        BlossomCubicBezier(cp, u, u, u);

    public static Float EvaluateCubicBezier(ReadOnlySpan<Float> cp, Float u) =>
        BlossomCubicBezier(cp, u, u, u);

    public static Point3f EvaluateCubicBezier(ReadOnlySpan<Point3f> cp, Float u, out Vector3f deriv)
    {
        Point3f cp10 = Lerp(u, cp[0], cp[1]), cp11 = Lerp(u, cp[1], cp[2]), cp12 = Lerp(u, cp[2], cp[3]);
        Point3f cp20 = Lerp(u, cp10, cp11), cp21 = Lerp(u, cp11, cp12);
        // Compute B\'ezier curve derivative at $u$
        if (LengthSquared(cp21 - cp20) > 0)
            deriv = 3 * (cp21 - cp20);
        else
            deriv = cp[3] - cp[0];
        return Lerp(u, cp20, cp21);
    }

    public static Point3f[] SubdivideCubicBezier(ReadOnlySpan<Point3f> cp) =>
    [
        cp[0],
        (cp[0] + cp[1]) / 2,
        (cp[0] + 2 * cp[1] + cp[2]) / 4,
        (cp[0] + 3 * cp[1] + 3 * cp[2] + cp[3]) / 8,
        (cp[1] + 2 * cp[2] + cp[3]) / 4,
        (cp[2] + cp[3]) / 2,
        cp[3],
    ];

    public static Array4<Point3f> CubicBezierControlPoints(ReadOnlySpan<Point3f> cp, Float uMin, Float uMax) =>
        new(BlossomCubicBezier(cp, uMin, uMin, uMin),
            BlossomCubicBezier(cp, uMin, uMin, uMax),
            BlossomCubicBezier(cp, uMin, uMax, uMax),
            BlossomCubicBezier(cp, uMax, uMax, uMax));

    public static Bounds3f BoundCubicBezier(ReadOnlySpan<Point3f> cp) =>
        Union(new Bounds3f(cp[0], cp[1]), new Bounds3f(cp[2], cp[3]));

    public static Bounds3f BoundCubicBezier(ReadOnlySpan<Point3f> cp, Float uMin, Float uMax)
    {
        if (uMin == 0 && uMax == 1)
            return BoundCubicBezier(cp);
        Array4<Point3f> cpSeg = CubicBezierControlPoints(cp, uMin, uMax);
        return BoundCubicBezier(cpSeg);
    }

    public static Array4<Point3f> ElevateQuadraticBezierToCubic(ReadOnlySpan<Point3f> cp) =>
        new(cp[0], Lerp(2f / 3f, cp[0], cp[1]), Lerp(1f / 3f, cp[1], cp[2]), cp[2]);

    public static Array3<Point3f> QuadraticBSplineToBezier(ReadOnlySpan<Point3f> cp)
    {
        // We can compute equivalent Bezier control points via some blossoming.
        // We have three control points and a uniform knot vector; we will label
        // the points p01, p12, and p23.  We want the Bezier control points of
        // the equivalent curve, which are p11, p12, and p22.  We already have
        // p12.
        Point3f p11 = Lerp(0.5f, cp[0], cp[1]);
        Point3f p22 = Lerp(0.5f, cp[1], cp[2]);
        return new(p11, cp[1], p22);
    }

    public static Array4<Point3f> CubicBSplineToBezier(ReadOnlySpan<Point3f> cp)
    {
        // Blossom from p012, p123, p234, and p345 to the Bezier control points
        // p222, p223, p233, and p333.
        // https://people.eecs.berkeley.edu/~sequin/CS284/IMGS/cubicbsplinepoints.gif
        Point3f p012 = cp[0], p123 = cp[1], p234 = cp[2], p345 = cp[3];

        Point3f p122 = Lerp(2f / 3f, p012, p123);
        Point3f p223 = Lerp(1f / 3f, p123, p234);
        Point3f p233 = Lerp(2f / 3f, p123, p234);
        Point3f p334 = Lerp(1f / 3f, p234, p345);

        Point3f p222 = Lerp(0.5f, p122, p223);
        Point3f p333 = Lerp(0.5f, p233, p334);

        return new(p222, p223, p233, p333);
    }
}

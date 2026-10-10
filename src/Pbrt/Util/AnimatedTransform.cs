// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of AnimatedTransform from src/pbrt/util/transform.h and
// transform.cpp. The motion derivative coefficients are computed in
// AnimatedTransform.Derivatives.cs. Apply/ApplyInverse for Interaction and
// SurfaceInteraction are in Interaction.cs.

namespace Pbrt;

// AnimatedTransform Definition
public sealed partial class AnimatedTransform
{
    // AnimatedTransform Public Members
    public readonly Transform startTransform, endTransform;
    public readonly Float startTime = 0, endTime = 1;

    // AnimatedTransform Private Members
    private readonly bool actuallyAnimated = false;
    private readonly Vector3f[] T = new Vector3f[2];
    private readonly Quaternion[] R = [new Quaternion(), new Quaternion()];
    private readonly SquareMatrix4[] S = new SquareMatrix4[2];
    private readonly bool hasRotation;

    private readonly struct DerivativeTerm(Float c, Float x, Float y, Float z)
    {
        public readonly Float kc = c, kx = x, ky = y, kz = z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float Eval(Point3f p) => kc + kx * p.x + ky * p.y + kz * p.z;
    }

    private readonly DerivativeTerm[] c1 = new DerivativeTerm[3], c2 = new DerivativeTerm[3],
                                      c3 = new DerivativeTerm[3], c4 = new DerivativeTerm[3],
                                      c5 = new DerivativeTerm[3];

    // AnimatedTransform Public Methods
    public AnimatedTransform()
    {
        startTransform = endTransform = new Transform();
    }

    public AnimatedTransform(Transform t) : this(t, 0, t, 1) { }

    // AnimatedTransform Method Definitions
    public AnimatedTransform(Transform startTransform, Float startTime, Transform endTransform,
                             Float endTime)
    {
        this.startTransform = startTransform;
        this.endTransform = endTransform;
        this.startTime = startTime;
        this.endTime = endTime;
        actuallyAnimated = startTransform != endTransform;
        if (!actuallyAnimated)
            return;
        // Decompose start and end transformations
        startTransform.Decompose(out T[0], out SquareMatrix4 Rm, out S[0]);
        R[0] = (Quaternion)new Transform(Rm);
        endTransform.Decompose(out T[1], out Rm, out S[1]);
        R[1] = (Quaternion)new Transform(Rm);
        // Flip _R[1]_ if needed to select shortest path
        if (Dot(R[0], R[1]) < 0)
            R[1] = -R[1];

        hasRotation = Dot(R[0], R[1]) < 0.9995f;
        // Compute terms of motion derivative function
        if (hasRotation)
            ComputeDerivativeTerms();
    }

    public bool IsAnimated() => actuallyAnimated;

    public bool HasScale() => startTransform.HasScale() || endTransform.HasScale();

    public Ray Apply(in Ray r)
    {
        Float tMax = 0;
        return Apply(r, ref tMax);
    }

    public Ray Apply(in Ray r, ref Float tMax)
    {
        if (!actuallyAnimated || r.time <= startTime)
            return startTransform.Apply(r, ref tMax);
        else if (r.time >= endTime)
            return endTransform.Apply(r, ref tMax);
        else
        {
            Transform t = Interpolate(r.time);
            return t.Apply(r, ref tMax);
        }
    }

    public Ray ApplyInverse(in Ray r)
    {
        Float tMax = 0;
        return ApplyInverse(r, ref tMax);
    }

    public Ray ApplyInverse(in Ray r, ref Float tMax)
    {
        if (!actuallyAnimated || r.time <= startTime)
            return startTransform.ApplyInverse(r, ref tMax);
        else if (r.time >= endTime)
            return endTransform.ApplyInverse(r, ref tMax);
        else
        {
            Transform t = Interpolate(r.time);
            return t.ApplyInverse(r, ref tMax);
        }
    }

    public RayDifferential Apply(in RayDifferential r)
    {
        Float tMax = 0;
        return Apply(r, ref tMax);
    }

    public RayDifferential Apply(in RayDifferential r, ref Float tMax)
    {
        if (!actuallyAnimated || r.time <= startTime)
            return startTransform.Apply(r, ref tMax);
        else if (r.time >= endTime)
            return endTransform.Apply(r, ref tMax);
        else
        {
            Transform t = Interpolate(r.time);
            return t.Apply(r, ref tMax);
        }
    }

    public Point3f Apply(Point3f p, Float time)
    {
        if (!actuallyAnimated || time <= startTime)
            return startTransform.Apply(p);
        else if (time >= endTime)
            return endTransform.Apply(p);
        Transform t = Interpolate(time);
        return t.Apply(p);
    }

    public Vector3f Apply(Vector3f v, Float time)
    {
        if (!actuallyAnimated || time <= startTime)
            return startTransform.Apply(v);
        else if (time >= endTime)
            return endTransform.Apply(v);
        Transform t = Interpolate(time);
        return t.Apply(v);
    }

    public Normal3f Apply(Normal3f n, Float time)
    {
        if (!actuallyAnimated || time <= startTime)
            return startTransform.Apply(n);
        else if (time >= endTime)
            return endTransform.Apply(n);
        Transform t = Interpolate(time);
        return t.Apply(n);
    }

    public Point3f ApplyInverse(Point3f p, Float time)
    {
        if (!actuallyAnimated)
            return startTransform.ApplyInverse(p);
        return Interpolate(time).ApplyInverse(p);
    }

    public Vector3f ApplyInverse(Vector3f v, Float time)
    {
        if (!actuallyAnimated)
            return startTransform.ApplyInverse(v);
        return Interpolate(time).ApplyInverse(v);
    }

    public Normal3f ApplyInverse(Normal3f n, Float time)
    {
        if (!actuallyAnimated)
            return startTransform.ApplyInverse(n);
        return Interpolate(time).ApplyInverse(n);
    }

    public Transform Interpolate(Float time)
    {
        // Handle boundary conditions for matrix interpolation
        if (!actuallyAnimated || time <= startTime)
            return startTransform;
        if (time >= endTime)
            return endTransform;

        Float dt = (time - startTime) / (endTime - startTime);
        // Interpolate translation at _dt_
        Vector3f trans = (1 - dt) * T[0] + dt * T[1];

        // Interpolate rotation at _dt_
        Quaternion rotate = Slerp(dt, R[0], R[1]);

        // Interpolate scale at _dt_
        SquareMatrix4 scale = (1 - dt) * S[0] + dt * S[1];

        // Return interpolated matrix as product of interpolated components
        return Translate(trans) * new Transform(rotate) * new Transform(scale);
    }

    public Bounds3f MotionBounds(in Bounds3f b)
    {
        // Handle easy cases for _Bounds3f_ motion bounds
        if (!actuallyAnimated)
            return startTransform.Apply(b);
        if (!hasRotation)
            return Union(startTransform.Apply(b), endTransform.Apply(b));

        // Return motion bounds accounting for animated rotation
        Bounds3f bounds = new();
        for (int corner = 0; corner < 8; ++corner)
            bounds = Union(bounds, BoundPointMotion(b.Corner(corner)));
        return bounds;
    }

    public Bounds3f BoundPointMotion(Point3f p)
    {
        if (!actuallyAnimated)
            return new Bounds3f(startTransform.Apply(p));
        Bounds3f bounds = new(startTransform.Apply(p), endTransform.Apply(p));
        Float cosTheta = Dot(R[0], R[1]);
        Float theta = SafeACos(cosTheta);
        Span<Float> zeros = stackalloc Float[8];
        for (int c = 0; c < 3; ++c)
        {
            // Find any motion derivative zeros for the component _c_
            int nZeros = 0;
            FindZeros(c1[c].Eval(p), c2[c].Eval(p), c3[c].Eval(p), c4[c].Eval(p),
                      c5[c].Eval(p), theta, new Interval(0f, 1f), zeros, ref nZeros);
            CHECK_LE(nZeros, zeros.Length);

            // Expand bounding box for any motion derivative zeros found
            for (int i = 0; i < nZeros; ++i)
            {
                Point3f pz = Apply(p, Lerp(zeros[i], startTime, endTime));
                bounds = Union(bounds, pz);
            }
        }
        return bounds;
    }

    // AnimatedTransform Private Methods
    private static void FindZeros(Float c1, Float c2, Float c3, Float c4, Float c5, Float theta,
                                  Interval tInterval, Span<Float> zeros, ref int nZeros,
                                  int depth = 8)
    {
        // Evaluate motion derivative in interval form, return if no zeros
        Interval dadt =
            new Interval(c1) +
            (new Interval(c2) + new Interval(c3) * tInterval) * Cos(new Interval(2 * theta) * tInterval) +
            (new Interval(c4) + new Interval(c5) * tInterval) * Sin(new Interval(2 * theta) * tInterval);
        if (dadt.LowerBound > 0 || dadt.UpperBound < 0 || dadt.LowerBound == dadt.UpperBound)
            return;

        // Either split range and recurse or report a zero
        if (depth > 0 && dadt.Width > 1e-3)
        {
            // Split _tInterval_ and check both resulting intervals
            Float mid = tInterval.Midpoint;
            FindZeros(c1, c2, c3, c4, c5, theta, new Interval(tInterval.LowerBound, mid), zeros,
                      ref nZeros, depth - 1);
            FindZeros(c1, c2, c3, c4, c5, theta, new Interval(mid, tInterval.UpperBound), zeros,
                      ref nZeros, depth - 1);
        }
        else
        {
            // Use Newton's method to refine zero
            Float tNewton = tInterval.Midpoint;
            for (int i = 0; i < 4; ++i)
            {
                // Evaluate motion function derivative and its derivative at _tNewton_
                Float fNewton = c1 + (c2 + c3 * tNewton) * FMath.Cos(2 * theta * tNewton) +
                                (c4 + c5 * tNewton) * FMath.Sin(2 * theta * tNewton);
                Float fPrimeNewton =
                    (c3 + 2 * (c4 + c5 * tNewton) * theta) * FMath.Cos(2 * tNewton * theta) +
                    (c5 - 2 * (c2 + c3 * tNewton) * theta) * FMath.Sin(2 * tNewton * theta);

                if (fNewton == 0 || fPrimeNewton == 0)
                    break;
                tNewton = tNewton - fNewton / fPrimeNewton;
            }

            // Record zero if refined zero is in interval
            if (tNewton >= tInterval.LowerBound - 1e-3f && tNewton < tInterval.UpperBound + 1e-3f)
            {
                zeros[nZeros] = tNewton;
                nZeros++;
            }
        }
    }

    public override string ToString() =>
        FormattableString.Invariant(
            $"[ AnimatedTransform startTransform: {startTransform} endTransform: {endTransform} startTime: {startTime} endTime: {endTime} actuallyAnimated: {actuallyAnimated} T: [ {T[0]} {T[1]} ] R: [ {R[0]} {R[1]} ] S: [ {S[0]} {S[1]} ] hasRotation: {hasRotation} ]");
}

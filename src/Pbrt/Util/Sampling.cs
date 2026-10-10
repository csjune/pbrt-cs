// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/sampling.h and sampling.cpp: the sampling
// functions. The sampling distribution classes are in
// Sampling.Distributions.cs, the sample generators (Uniform2D() etc.) in
// Sampling.Generators.cs and PiecewiseLinear2D in PiecewiseLinear2D.cs.
//
// pbrt's optional output pointers (Float *pdf = nullptr) become two
// overloads: one without the outputs and one with all of them as out
// parameters (pass `out _` to ignore one).

namespace Pbrt;

public static partial class Util
{
    // Sampling Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float BalanceHeuristic(int nf, Float fPdf, int ng, Float gPdf) =>
        (nf * fPdf) / (nf * fPdf + ng * gPdf);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float PowerHeuristic(int nf, Float fPdf, int ng, Float gPdf)
    {
        Float f = nf * fPdf, g = ng * gPdf;
        if (IsInf(Sqr(f)))
            return 1;
        return Sqr(f) / (Sqr(f) + Sqr(g));
    }

    public static int SampleDiscrete(ReadOnlySpan<Float> weights, Float u) =>
        SampleDiscrete(weights, u, out _, out _);

    public static int SampleDiscrete(ReadOnlySpan<Float> weights, Float u, out Float pmf,
                                     out Float uRemapped)
    {
        uRemapped = 0;
        // Handle empty _weights_ for discrete sampling
        if (weights.IsEmpty)
        {
            pmf = 0;
            return -1;
        }

        // Compute sum of _weights_
        Float sumWeights = 0;
        foreach (Float w in weights)
            sumWeights += w;

        // Compute rescaled $u'$ sample
        Float up = u * sumWeights;
        if (up == sumWeights)
            up = NextFloatDown(up);

        // Find offset in _weights_ corresponding to $u'$
        int offset = 0;
        Float sum = 0;
        while (sum + weights[offset] <= up)
        {
            sum += weights[offset++];
            DCHECK_LT(offset, weights.Length);
        }

        // Compute PMF and remapped _u_ value, if necessary
        pmf = weights[offset] / sumWeights;
        uRemapped = Min((up - sum) / weights[offset], OneMinusEpsilon);

        return offset;
    }

    public static Float LinearPDF(Float x, Float a, Float b)
    {
        DCHECK(a >= 0 && b >= 0);
        if (x < 0 || x > 1)
            return 0;
        return 2 * Lerp(x, a, b) / (a + b);
    }

    public static Float SampleLinear(Float u, Float a, Float b)
    {
        DCHECK(a >= 0 && b >= 0);
        if (u == 0 && a == 0)
            return 0;
        Float x = u * (a + b) / (a + FMath.Sqrt(Lerp(u, Sqr(a), Sqr(b))));
        return Min(x, OneMinusEpsilon);
    }

    public static Float InvertLinearSample(Float x, Float a, Float b) =>
        x * (a * (2 - x) + b * x) / (a + b);

    public static Float BilinearPDF(Point2f p, ReadOnlySpan<Float> w)
    {
        DCHECK_EQ(4, w.Length);
        if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
            return 0;
        if (w[0] + w[1] + w[2] + w[3] == 0)
            return 1;
        return 4 *
               ((1 - p[0]) * (1 - p[1]) * w[0] + p[0] * (1 - p[1]) * w[1] +
                (1 - p[0]) * p[1] * w[2] + p[0] * p[1] * w[3]) /
               (w[0] + w[1] + w[2] + w[3]);
    }

    public static Point2f SampleBilinear(Point2f u, ReadOnlySpan<Float> w)
    {
        DCHECK_EQ(4, w.Length);
        Point2f p;
        // Sample $y$ for bilinear marginal distribution
        p.y = SampleLinear(u[1], w[0] + w[1], w[2] + w[3]);

        // Sample $x$ for bilinear conditional distribution
        p.x = SampleLinear(u[0], Lerp(p.y, w[0], w[2]), Lerp(p.y, w[1], w[3]));

        return p;
    }

    public static Point2f InvertBilinearSample(Point2f p, ReadOnlySpan<Float> w) =>
        new(InvertLinearSample(p.x, Lerp(p.y, w[0], w[2]), Lerp(p.y, w[1], w[3])),
            InvertLinearSample(p.y, w[0] + w[1], w[2] + w[3]));

    public static Float VisibleWavelengthsPDF(Float lambda)
    {
        if (lambda < 360 || lambda > 830)
            return 0;
        return 0.0039398042f / Sqr(FMath.Cosh(0.0072f * (lambda - 538)));
    }

    public static Float SampleVisibleWavelengths(Float u) =>
        538 - 138.888889f * FMath.Atanh(0.85691062f - 1.82750197f * u);

    public static Array3<Float> SampleUniformTriangle(Point2f u)
    {
        Float b0, b1;
        if (u[0] < u[1])
        {
            b0 = u[0] / 2;
            b1 = u[1] - b0;
        }
        else
        {
            b1 = u[1] / 2;
            b0 = u[0] - b1;
        }
        return new Array3<Float>(b0, b1, 1 - b0 - b1);
    }

    public static Point2f InvertUniformTriangleSample(in Array3<Float> b)
    {
        if (b[0] > b[1])
        {
            // b0 = u[0] - u[1] / 2, b1 = u[1] / 2
            return new Point2f(b[0] + b[1], 2 * b[1]);
        }
        else
        {
            // b1 = u[1] - u[0] / 2, b0 = u[0] / 2
            return new Point2f(2 * b[0], b[1] + b[0]);
        }
    }

    public static Float SampleTent(Float u, Float r)
    {
        if (SampleDiscrete([0.5f, 0.5f], u, out _, out u) == 0)
            return -r + r * SampleLinear(u, 0, 1);
        else
            return r * SampleLinear(u, 1, 0);
    }

    public static Float TentPDF(Float x, Float r)
    {
        if (FMath.Abs(x) >= r)
            return 0;
        return 1 / r - FMath.Abs(x) / Sqr(r);
    }

    public static Float InvertTentSample(Float x, Float r)
    {
        if (x <= 0)
            return (1 - InvertLinearSample(-x / r, 1, 0)) / 2;
        else
            return 0.5f + InvertLinearSample(x / r, 1, 0) / 2;
    }

    public static Float ExponentialPDF(Float x, Float a)
    {
        DCHECK_GT(a, 0);
        return a * FMath.Exp(-a * x);
    }

    public static Float SampleExponential(Float u, Float a)
    {
        DCHECK_GT(a, 0);
        return -FMath.Log(1 - u) / a;
    }

    public static Float InvertExponentialSample(Float x, Float a)
    {
        DCHECK_GT(a, 0);
        return 1 - FMath.Exp(-a * x);
    }

    public static Float NormalPDF(Float x, Float mu = 0, Float sigma = 1) => Gaussian(x, mu, sigma);

    public static Float SampleNormal(Float u, Float mu = 0, Float sigma = 1) =>
        mu + Sqrt2 * sigma * ErfInv(2 * u - 1);

    public static Float InvertNormalSample(Float x, Float mu = 0, Float sigma = 1) =>
        0.5f * (1 + Erf((x - mu) / (sigma * Sqrt2)));

    public static Point2f SampleTwoNormal(Point2f u, Float mu = 0, Float sigma = 1)
    {
        Float r2 = -2 * Log1p(-u[0]); // log(1-u[0]), robustly.
        return new Point2f(mu + sigma * FMath.Sqrt(r2) * FMath.Cos(2 * Pi * u[1]),
                           mu + sigma * FMath.Sqrt(r2) * FMath.Sin(2 * Pi * u[1]));
    }

    public static Float LogisticPDF(Float x, Float s)
    {
        x = FMath.Abs(x);
        return FMath.Exp(-x / s) / (s * Sqr(1 + FMath.Exp(-x / s)));
    }

    public static Float SampleLogistic(Float u, Float s) => -s * FMath.Log(1 / u - 1);

    public static Float InvertLogisticSample(Float x, Float s) => 1 / (1 + FMath.Exp(-x / s));

    public static Float TrimmedLogisticPDF(Float x, Float s, Float a, Float b)
    {
        if (x < a || x > b)
            return 0;
        return Logistic(x, s) / (InvertLogisticSample(b, s) - InvertLogisticSample(a, s));
    }

    public static Float SampleTrimmedLogistic(Float u, Float s, Float a, Float b)
    {
        DCHECK_LT(a, b);
        u = Lerp(u, InvertLogisticSample(a, s), InvertLogisticSample(b, s));
        Float x = SampleLogistic(u, s);
        DCHECK(!IsNaN(x));
        return Clamp(x, a, b);
    }

    public static Float InvertTrimmedLogisticSample(Float x, Float s, Float a, Float b)
    {
        DCHECK(a <= x && x <= b);
        return (InvertLogisticSample(x, s) - InvertLogisticSample(a, s)) /
               (InvertLogisticSample(b, s) - InvertLogisticSample(a, s));
    }

    public static Float SmoothStepPDF(Float x, Float a, Float b)
    {
        if (x < a || x > b)
            return 0;
        DCHECK_LT(a, b);
        return (2 / (b - a)) * SmoothStep(x, a, b);
    }

    public static Float SampleSmoothStep(Float u, Float a, Float b)
    {
        DCHECK_LT(a, b);
        (Float, Float) cdfMinusU(Float x)
        {
            Float t = (x - a) / (b - a);
            Float P = 2 * Pow(t, 3) - Pow(t, 4);
            Float PDeriv = SmoothStepPDF(x, a, b);
            return (P - u, PDeriv);
        }
        return NewtonBisection(a, b, cdfMinusU);
    }

    // NOTE: as in pbrt, P() ignores its argument (it always uses the _t_
    // computed from _x_).
    public static Float InvertSmoothStepSample(Float x, Float a, Float b)
    {
        Float t = (x - a) / (b - a);
        Float P(Float x) => 2 * Pow(t, 3) - Pow(t, 4);
        return (P(x) - P(a)) / (P(b) - P(a));
    }

    public static Point2f SampleUniformDiskPolar(Point2f u)
    {
        Float r = FMath.Sqrt(u[0]);
        Float theta = 2 * Pi * u[1];
        return new Point2f(r * FMath.Cos(theta), r * FMath.Sin(theta));
    }

    public static Point2f InvertUniformDiskPolarSample(Point2f p)
    {
        Float phi = FMath.Atan2(p.y, p.x);
        if (phi < 0)
            phi += 2 * Pi;
        return new Point2f(Sqr(p.x) + Sqr(p.y), phi / (2 * Pi));
    }

    public static Point2f SampleUniformDiskConcentric(Point2f u)
    {
        // Map _u_ to $[-1,1]^2$ and handle degeneracy at the origin
        Point2f uOffset = 2 * u - new Vector2f(1, 1);
        if (uOffset.x == 0 && uOffset.y == 0)
            return new Point2f(0, 0);

        // Apply concentric mapping to point
        Float theta, r;
        if (FMath.Abs(uOffset.x) > FMath.Abs(uOffset.y))
        {
            r = uOffset.x;
            theta = PiOver4 * (uOffset.y / uOffset.x);
        }
        else
        {
            r = uOffset.y;
            theta = PiOver2 - PiOver4 * (uOffset.x / uOffset.y);
        }
        return r * new Point2f(FMath.Cos(theta), FMath.Sin(theta));
    }

    public static Point2f InvertUniformDiskConcentricSample(Point2f p)
    {
        Float theta = FMath.Atan2(p.y, p.x); // -pi -> pi
        Float r = FMath.Sqrt(Sqr(p.x) + Sqr(p.y));

        Point2f uo;
        // TODO: can we make this less branchy?
        if (FMath.Abs(theta) < PiOver4 || FMath.Abs(theta) > 3 * PiOver4)
        {
            uo.x = r = FMath.CopySign(r, p.x);
            if (p.x < 0)
            {
                if (p.y < 0)
                    uo.y = (Pi + theta) * r / PiOver4;
                else
                    uo.y = (theta - Pi) * r / PiOver4;
            }
            else
                uo.y = (theta * r) / PiOver4;
        }
        else
        {
            uo.y = r = FMath.CopySign(r, p.y);
            if (p.y < 0)
                uo.x = -(PiOver2 + theta) * r / PiOver4;
            else
                uo.x = (PiOver2 - theta) * r / PiOver4;
        }

        return new Point2f((uo.x + 1) / 2, (uo.y + 1) / 2);
    }

    public static Vector3f SampleUniformHemisphere(Point2f u)
    {
        Float z = u[0];
        Float r = SafeSqrt(1 - Sqr(z));
        Float phi = 2 * Pi * u[1];
        return new Vector3f(r * FMath.Cos(phi), r * FMath.Sin(phi), z);
    }

    public static Float UniformHemispherePDF() => Inv2Pi;

    public static Point2f InvertUniformHemisphereSample(Vector3f w)
    {
        Float phi = FMath.Atan2(w.y, w.x);
        if (phi < 0)
            phi += 2 * Pi;
        return new Point2f(w.z, phi / (2 * Pi));
    }

    public static Vector3f SampleUniformSphere(Point2f u)
    {
        Float z = 1 - 2 * u[0];
        Float r = SafeSqrt(1 - Sqr(z));
        Float phi = 2 * Pi * u[1];
        return new Vector3f(r * FMath.Cos(phi), r * FMath.Sin(phi), z);
    }

    public static Float UniformSpherePDF() => Inv4Pi;

    public static Point2f InvertUniformSphereSample(Vector3f w)
    {
        Float phi = FMath.Atan2(w.y, w.x);
        if (phi < 0)
            phi += 2 * Pi;
        return new Point2f((1 - w.z) / 2, phi / (2 * Pi));
    }

    public static Vector3f SampleCosineHemisphere(Point2f u)
    {
        Point2f d = SampleUniformDiskConcentric(u);
        Float z = SafeSqrt(1 - Sqr(d.x) - Sqr(d.y));
        return new Vector3f(d.x, d.y, z);
    }

    public static Float CosineHemispherePDF(Float cosTheta) => cosTheta * InvPi;

    public static Point2f InvertCosineHemisphereSample(Vector3f w) =>
        InvertUniformDiskConcentricSample(new Point2f(w.x, w.y));

    public static Float UniformConePDF(Float cosThetaMax) => 1 / (2 * Pi * (1 - cosThetaMax));

    public static Vector3f SampleUniformCone(Point2f u, Float cosThetaMax)
    {
        Float cosTheta = (1 - u[0]) + u[0] * cosThetaMax;
        Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
        Float phi = u[1] * 2 * Pi;
        return SphericalDirection(sinTheta, cosTheta, phi);
    }

    public static Point2f InvertUniformConeSample(Vector3f w, Float cosThetaMax)
    {
        Float cosTheta = w.z;
        Float phi = SphericalPhi(w);
        return new Point2f((cosTheta - 1) / (cosThetaMax - 1), phi / (2 * Pi));
    }

    // Sample from e^(-c x), x from 0 to xMax
    public static Float SampleTrimmedExponential(Float u, Float c, Float xMax) =>
        FMath.Log(1 - u * (1 - FMath.Exp(-c * xMax))) / -c;

    public static Float TrimmedExponentialPDF(Float x, Float c, Float xMax)
    {
        if (x < 0 || x > xMax)
            return 0;
        return c / (1 - FMath.Exp(-c * xMax)) * FMath.Exp(-c * x);
    }

    public static Float InvertTrimmedExponentialSample(Float x, Float c, Float xMax)
    {
        DCHECK(x >= 0 && x <= xMax);
        return (1 - FMath.Exp(-c * x)) / (1 - FMath.Exp(-c * xMax));
    }

    public static Vector3f SampleUniformHemisphereConcentric(Point2f u)
    {
        // Map uniform random numbers to $[-1,1]^2$
        Point2f uOffset = 2f * u - new Vector2f(1, 1);

        // Handle degeneracy at the origin
        if (uOffset.x == 0 && uOffset.y == 0)
            return new Vector3f(0, 0, 1);

        // Apply concentric mapping to point
        Float theta, r;
        if (FMath.Abs(uOffset.x) > FMath.Abs(uOffset.y))
        {
            r = uOffset.x;
            theta = PiOver4 * (uOffset.y / uOffset.x);
        }
        else
        {
            r = uOffset.y;
            theta = PiOver2 - PiOver4 * (uOffset.x / uOffset.y);
        }

        return new Vector3f(FMath.Cos(theta) * r * FMath.Sqrt(2 - r * r),
                            FMath.Sin(theta) * r * FMath.Sqrt(2 - r * r), 1 - r * r);
    }

    // Sampling Function Definitions
    public static Array3<Float> SampleSphericalTriangle(ReadOnlySpan<Point3f> v, Point3f p, Point2f u) =>
        SampleSphericalTriangle(v, p, u, out _);

    public static Array3<Float> SampleSphericalTriangle(ReadOnlySpan<Point3f> v, Point3f p, Point2f u,
                                                        out Float pdf)
    {
        pdf = 0;
        // Compute vectors _a_, _b_, and _c_ to spherical triangle vertices
        Vector3f a = v[0] - p, b = v[1] - p, c = v[2] - p;
        CHECK_GT(LengthSquared(a), 0);
        CHECK_GT(LengthSquared(b), 0);
        CHECK_GT(LengthSquared(c), 0);
        a = Normalize(a);
        b = Normalize(b);
        c = Normalize(c);

        // Compute normalized cross products of all direction pairs
        Vector3f n_ab = Cross(a, b), n_bc = Cross(b, c), n_ca = Cross(c, a);
        if (LengthSquared(n_ab) == 0 || LengthSquared(n_bc) == 0 || LengthSquared(n_ca) == 0)
            return default;
        n_ab = Normalize(n_ab);
        n_bc = Normalize(n_bc);
        n_ca = Normalize(n_ca);

        // Find angles $\alpha$, $\beta$, and $\gamma$ at spherical triangle vertices
        Float alpha = AngleBetween(n_ab, -n_ca);
        Float beta = AngleBetween(n_bc, -n_ab);
        Float gamma = AngleBetween(n_ca, -n_bc);

        // Uniformly sample triangle area $A$ to compute $A'$
        Float A_pi = alpha + beta + gamma;
        Float Ap_pi = Lerp(u[0], Pi, A_pi);
        {
            Float A = A_pi - Pi;
            pdf = (A <= 0) ? 0 : 1 / A;
        }

        // Find $\cos\beta'$ for point along _b_ for sampled area
        Float cosAlpha = FMath.Cos(alpha), sinAlpha = FMath.Sin(alpha);
        Float sinPhi = FMath.Sin(Ap_pi) * cosAlpha - FMath.Cos(Ap_pi) * sinAlpha;
        Float cosPhi = FMath.Cos(Ap_pi) * cosAlpha + FMath.Sin(Ap_pi) * sinAlpha;
        Float k1 = cosPhi + cosAlpha;
        Float k2 = sinPhi - sinAlpha * Dot(a, b) /* cos c */;
        Float cosBp = (k2 + (DifferenceOfProducts(k2, cosPhi, k1, sinPhi)) * cosAlpha) /
                      ((SumOfProducts(k2, sinPhi, k1, cosPhi)) * sinAlpha);
        // Happens if the triangle basically covers the entire hemisphere.
        // We currently depend on calling code to detect this case, which
        // is sort of ugly/unfortunate.
        CHECK(!IsNaN(cosBp));
        cosBp = Clamp(cosBp, -1, 1);

        // Sample $c'$ along the arc between $a$ and $c$
        Float sinBp = SafeSqrt(1 - Sqr(cosBp));
        Vector3f cp = cosBp * a + sinBp * Normalize(GramSchmidt(c, a));

        // Compute sampled spherical triangle direction and return barycentrics
        Float cosTheta = 1 - u[1] * (1 - Dot(cp, b));
        Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
        Vector3f w = cosTheta * b + sinTheta * Normalize(GramSchmidt(cp, b));
        // Find barycentric coordinates for sampled direction _w_
        Vector3f e1 = v[1] - v[0], e2 = v[2] - v[0];
        Vector3f s1 = Cross(w, e2);
        Float divisor = Dot(s1, e1);
        if (divisor == 0)
        {
            // This happens with triangles that cover (nearly) the whole
            // hemisphere.
            return new Array3<Float>(1f / 3f, 1f / 3f, 1f / 3f);
        }
        Float invDivisor = 1 / divisor;
        Vector3f s = p - v[0];
        Float b1 = Dot(s, s1) * invDivisor;
        Float b2 = Dot(w, Cross(s, e1)) * invDivisor;

        // Return clamped barycentrics for sampled direction
        b1 = Clamp(b1, 0, 1);
        b2 = Clamp(b2, 0, 1);
        if (b1 + b2 > 1)
        {
            b1 /= b1 + b2;
            b2 /= b1 + b2;
        }
        return new Array3<Float>(1 - b1 - b2, b1, b2);
    }

    // Via Jim Arvo's SphTri.C
    public static Point2f InvertSphericalTriangleSample(ReadOnlySpan<Point3f> v, Point3f p, Vector3f w)
    {
        // Compute vectors _a_, _b_, and _c_ to spherical triangle vertices
        Vector3f a = v[0] - p, b = v[1] - p, c = v[2] - p;
        CHECK_GT(LengthSquared(a), 0);
        CHECK_GT(LengthSquared(b), 0);
        CHECK_GT(LengthSquared(c), 0);
        a = Normalize(a);
        b = Normalize(b);
        c = Normalize(c);

        // Compute normalized cross products of all direction pairs
        Vector3f n_ab = Cross(a, b), n_bc = Cross(b, c), n_ca = Cross(c, a);
        if (LengthSquared(n_ab) == 0 || LengthSquared(n_bc) == 0 || LengthSquared(n_ca) == 0)
            return default;
        n_ab = Normalize(n_ab);
        n_bc = Normalize(n_bc);
        n_ca = Normalize(n_ca);

        // Find angles $\alpha$, $\beta$, and $\gamma$ at spherical triangle vertices
        Float alpha = AngleBetween(n_ab, -n_ca);
        Float beta = AngleBetween(n_bc, -n_ab);
        Float gamma = AngleBetween(n_ca, -n_bc);

        // Find vertex $\VEC{c'}$ along $\VEC{a}\VEC{c}$ arc for $\w{}$
        Vector3f cp = Normalize(Cross(Cross(b, w), Cross(c, a)));
        if (Dot(cp, a + c) < 0)
            cp = -cp;

        // Invert uniform area sampling to find _u0_
        Float u0;
        if (Dot(a, cp) > 0.99999847691f /* 0.1 degrees */)
            u0 = 0;
        else
        {
            // Compute area $A'$ of subtriangle
            Vector3f n_cpb = Cross(cp, b), n_acp = Cross(a, cp);
            if (LengthSquared(n_cpb) == 0 || LengthSquared(n_acp) == 0)
                return new Point2f(0.5f, 0.5f);
            n_cpb = Normalize(n_cpb);
            n_acp = Normalize(n_acp);
            Float Ap = alpha + AngleBetween(n_ab, n_cpb) + AngleBetween(n_acp, -n_cpb) - Pi;

            // Compute sample _u0_ that gives the area $A'$
            Float A = alpha + beta + gamma - Pi;
            u0 = Ap / A;
        }

        // Invert arc sampling to find _u1_ and return result
        Float u1 = (1 - Dot(w, b)) / (1 - Dot(cp, b));
        return new Point2f(Clamp(u0, 0, 1), Clamp(u1, 0, 1));
    }

    public static Point3f SampleSphericalRectangle(Point3f pRef, Point3f s, Vector3f ex, Vector3f ey,
                                                   Point2f u) =>
        SampleSphericalRectangle(pRef, s, ex, ey, u, out _);

    public static Point3f SampleSphericalRectangle(Point3f pRef, Point3f s, Vector3f ex, Vector3f ey,
                                                   Point2f u, out Float pdf)
    {
        // Compute local reference frame and transform rectangle coordinates
        Float exl = Length(ex), eyl = Length(ey);
        Frame R = Frame.FromXY(ex / exl, ey / eyl);
        Vector3f dLocal = R.ToLocal(s - pRef);
        Float z0 = dLocal.z;

        // flip 'z' to make it point against 'Q'
        if (z0 > 0)
        {
            R.z = -R.z;
            z0 *= -1;
        }
        Float x0 = dLocal.x, y0 = dLocal.y;
        Float x1 = x0 + exl, y1 = y0 + eyl;

        // Find plane normals to rectangle edges and compute internal angles
        Vector3f v00 = new(x0, y0, z0), v01 = new(x0, y1, z0);
        Vector3f v10 = new(x1, y0, z0), v11 = new(x1, y1, z0);
        Vector3f n0 = Normalize(Cross(v00, v10)), n1 = Normalize(Cross(v10, v11));
        Vector3f n2 = Normalize(Cross(v11, v01)), n3 = Normalize(Cross(v01, v00));
        Float g0 = AngleBetween(-n0, n1), g1 = AngleBetween(-n1, n2);
        Float g2 = AngleBetween(-n2, n3), g3 = AngleBetween(-n3, n0);

        // Compute spherical rectangle solid angle and PDF
        Float solidAngle = g0 + g1 + g2 + g3 - 2 * Pi;
        if (solidAngle <= 0)
        {
            pdf = 0;
            return s + u[0] * ex + u[1] * ey;
        }
        pdf = Max(0, 1 / solidAngle);
        if (solidAngle < 1e-3)
            return s + u[0] * ex + u[1] * ey;

        // Sample _cu_ for spherical rectangle sample
        Float b0 = n0.z, b1 = n2.z;
        Float au = u[0] * (g0 + g1 - 2 * Pi) + (u[0] - 1) * (g2 + g3);
        Float fu = (FMath.Cos(au) * b0 - b1) / FMath.Sin(au);
        Float cu = FMath.CopySign(1 / FMath.Sqrt(Sqr(fu) + Sqr(b0)), fu);
        cu = Clamp(cu, -OneMinusEpsilon, OneMinusEpsilon); // avoid NaNs

        // Find _xu_ along $x$ edge for spherical rectangle sample
        Float xu = -(cu * z0) / SafeSqrt(1 - Sqr(cu));
        xu = Clamp(xu, x0, x1);

        // Find _xv_ along $y$ edge for spherical rectangle sample
        Float dd = FMath.Sqrt(Sqr(xu) + Sqr(z0));
        Float h0 = y0 / FMath.Sqrt(Sqr(dd) + Sqr(y0));
        Float h1 = y1 / FMath.Sqrt(Sqr(dd) + Sqr(y1));
        Float hv = h0 + u[1] * (h1 - h0), hvsq = Sqr(hv);
        Float yv = (hvsq < 1 - 1e-6f) ? (hv * dd) / FMath.Sqrt(1 - hvsq) : y1;

        // Return spherical triangle sample in original coordinate system
        return pRef + R.FromLocal(new Vector3f(xu, yv, z0));
    }

    public static Point2f InvertSphericalRectangleSample(Point3f pRef, Point3f s, Vector3f ex,
                                                         Vector3f ey, Point3f pRect)
    {
        // TODO: Delete anything unused in the below...

        // SphQuadInit()
        // local reference system 'R'
        Float exl = Length(ex), eyl = Length(ey);
        Frame R = Frame.FromXY(ex / exl, ey / eyl);

        // compute rectangle coords in local reference system
        Vector3f d = s - pRef;
        Vector3f dLocal = R.ToLocal(d);
        Float z0 = dLocal.z;

        // flip 'z' to make it point against 'Q'
        if (z0 > 0)
        {
            R.z = -R.z;
            z0 *= -1;
        }
        Float z0sq = Sqr(z0);
        Float x0 = dLocal.x;
        Float y0 = dLocal.y;
        Float x1 = x0 + exl;
        Float y1 = y0 + eyl;
        Float y0sq = Sqr(y0), y1sq = Sqr(y1);

        // create vectors to four vertices
        Vector3f v00 = new(x0, y0, z0), v01 = new(x0, y1, z0);
        Vector3f v10 = new(x1, y0, z0), v11 = new(x1, y1, z0);

        // compute normals to edges
        Vector3f n0 = Normalize(Cross(v00, v10));
        Vector3f n1 = Normalize(Cross(v10, v11));
        Vector3f n2 = Normalize(Cross(v11, v01));
        Vector3f n3 = Normalize(Cross(v01, v00));

        // compute internal angles (gamma_i)
        Float g0 = AngleBetween(-n0, n1);
        Float g1 = AngleBetween(-n1, n2);
        Float g2 = AngleBetween(-n2, n3);
        Float g3 = AngleBetween(-n3, n0);

        // compute predefined constants
        Float b0 = n0.z, b1 = n2.z, b0sq = Sqr(b0), b1sq = Sqr(b1);

        // compute solid angle from internal angles
        Float solidAngle = (Float)((double)g0 + (double)g1 + (double)g2 + (double)g3 - 2.0 * Pi);

        // TODO: this (rarely) goes differently than sample. figure out why...
        if (solidAngle < 1e-3)
        {
            Vector3f pq = pRect - s;
            return new Point2f(Dot(pq, ex) / LengthSquared(ex), Dot(pq, ey) / LengthSquared(ey));
        }

        Vector3f v = R.ToLocal(pRect - pRef);
        Float xu = v.x, yv = v.y;

        xu = Clamp(xu, x0, x1); // avoid Infs
        if (xu == 0)
            xu = (Float)1e-10;

        // Doing all this in double actually makes things slightly worse???!?
        // Float fusq = (1 - b0sq * Sqr(cu)) / Sqr(cu);
        // Float fusq = 1 / Sqr(cu) - b0sq;  // more stable
        Float invcusq = 1 + z0sq / Sqr(xu);
        Float fusq = invcusq - b0sq; // the winner so far
        Float fu = FMath.CopySign(FMath.Sqrt(fusq), xu);
        // Note, though have 1 + z^2/x^2 - b0^2, which isn't great if b0 \approx 1
        // double fusq = 1. - Sqr(double(b0)) + Sqr(double(z0) / double(xu));  //
        // this is worse?? double fu = pstd::copysign(std::sqrt(fusq), cu);

        // State of the floating point world: in the bad cases, about half the
        // error seems to come from inaccuracy in fu and half comes from
        // inaccuracy in sqrt/au.
        //
        // For fu, the main issue comes adding a small value to 1+ in invcusq
        // and then having b0sq be close to one, so having catastrophic
        // cancellation affect fusq. Approximating it as z0sq / Sqr(xu) when
        // b0sq is close to one doesn't help, however..
        //
        // For au, DifferenceOfProducts doesn't seem to help with the two
        // factors. Furthermore, while it would be nice to think about this
        // like atan(y/x) and then rewrite/simplify y/x, we need to do so in a
        // way that doesn't flip the sign of x and y, which would be fine if we
        // were computing y/x, but messes up atan2's quadrant-determinations...

        Float sqrt = SafeSqrt(DifferenceOfProducts(b0, b0, b1, b1) + fusq);
        // No benefit to difference of products here...
        Float au = FMath.Atan2(-(b1 * fu) - FMath.CopySign(b0 * sqrt, fu * b0),
                               b0 * b1 - sqrt * FMath.Abs(fu));
        if (au > 0)
            au -= 2 * Pi;

        if (fu == 0)
            au = Pi;
        Float u0 = (au + g2 + g3) / solidAngle;

        Float ddsq = Sqr(xu) + z0sq;
        Float dd = FMath.Sqrt(ddsq);
        Float h0 = y0 / FMath.Sqrt(ddsq + y0sq);
        Float h1 = y1 / FMath.Sqrt(ddsq + y1sq);
        Float yvsq = Sqr(yv);

        Float u10 = (DifferenceOfProducts(h0, h0, h0, h1) -
                     FMath.Abs(h0 - h1) * FMath.Sqrt(yvsq * (ddsq + yvsq)) / (ddsq + yvsq)) /
                    Sqr(h0 - h1);
        Float u11 = (DifferenceOfProducts(h0, h0, h0, h1) +
                     FMath.Abs(h0 - h1) * FMath.Sqrt(yvsq * (ddsq + yvsq)) / (ddsq + yvsq)) /
                    Sqr(h0 - h1);

        // TODO: yuck is there a better way to figure out which is the right
        // solution?
        Float hv0 = Lerp(u10, h0, h1), hv1 = Lerp(u11, h0, h1);
        Float hvsq0 = Sqr(hv0), hvsq1 = Sqr(hv1);
        Float yz0 = (hv0 * dd) / FMath.Sqrt(1 - hvsq0);
        Float yz1 = (hv1 * dd) / FMath.Sqrt(1 - hvsq1);

        Point2f u = (FMath.Abs(yz0 - yv) < FMath.Abs(yz1 - yv))
                        ? new Point2f(Clamp(u0, 0, 1), u10)
                        : new Point2f(Clamp(u0, 0, 1), u11);

        return u;
    }

    public static Vector3f SampleHenyeyGreenstein(Vector3f wo, Float g, Point2f u) =>
        SampleHenyeyGreenstein(wo, g, u, out _);

    public static Vector3f SampleHenyeyGreenstein(Vector3f wo, Float g, Point2f u, out Float pdf)
    {
        // When g \approx -1 and u[0] \approx 0 or with g \approx 1 and u[0]
        // \approx 1, the computation of cosTheta below is unstable and can
        // give, leading to NaNs. For now we limit g to the range where it is
        // still ok; it would be nice to come up with a numerically robust
        // rewrite of the computation instead, but with |g| \approx 1, the
        // sampling distribution has a very sharp turn to deal with.
        g = Clamp(g, -(Float).99, (Float).99);

        // Compute $\cos\theta$ for Henyey--Greenstein sample
        Float cosTheta;
        if (FMath.Abs(g) < 1e-3f)
            cosTheta = 1 - 2 * u[0];
        else
            cosTheta = -1 / (2 * g) * (1 + Sqr(g) - Sqr((1 - Sqr(g)) / (1 + g - 2 * g * u[0])));

        // Compute direction _wi_ for Henyey--Greenstein sample
        Float sinTheta = SafeSqrt(1 - Sqr(cosTheta));
        Float phi = 2 * Pi * u[1];
        Frame wFrame = Frame.FromZ(wo);
        Vector3f wi = wFrame.FromLocal(SphericalDirection(sinTheta, cosTheta, phi));

        pdf = HenyeyGreenstein(cosTheta, g);
        return wi;
    }

    public static Point2f RejectionSampleDisk(ref RNG rng)
    {
        Point2f p;
        do
        {
            p.x = 1 - 2 * rng.Uniform<Float>();
            p.y = 1 - 2 * rng.Uniform<Float>();
        } while (Sqr(p.x) + Sqr(p.y) > 1);
        return p;
    }

    public static Float SampleCatmullRom(ReadOnlySpan<Float> nodes, ReadOnlySpan<Float> f,
                                         ReadOnlySpan<Float> F, Float u) =>
        SampleCatmullRom(nodes, f, F, u, out _, out _);

    public static Float SampleCatmullRom(ReadOnlySpan<Float> nodes, ReadOnlySpan<Float> f,
                                         ReadOnlySpan<Float> F, Float u, out Float fval, out Float pdf)
    {
        CHECK_EQ(nodes.Length, f.Length);
        CHECK_EQ(f.Length, F.Length);
        // Map _u_ to a spline interval by inverting _F_
        u *= F[^1];
        int i = FindInterval(F, u);

        // Look up $x_i$ and function values of spline segment _i_
        Float x0 = nodes[i], x1 = nodes[i + 1];
        Float f0 = f[i], f1 = f[i + 1];
        Float width = x1 - x0;

        // Approximate derivatives using finite differences
        Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
        Float d1 = (i + 2 < nodes.Length) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0)
                                          : (f1 - f0);

        // Rescale _u_ for continuous spline sampling step
        u = (u - F[i]) / width;

        // Invert definite integral over spline segment
        Float t = InvertSplineSegment(f0, f1, d0, d1, u, out Float fhat);

        fval = fhat;
        pdf = fhat / F[^1];
        return x0 + width * t;
    }

    // The NewtonBisection() inversion shared by SampleCatmullRom() and
    // SampleCatmullRom2D() (a lambda in each in pbrt); returns t and the
    // spline value fhat there.
    private static Float InvertSplineSegment(Float f0, Float f1, Float d0, Float d1, Float u,
                                             out Float fhatOut)
    {
        Float Fhat = 0, fhat = 0;
        (Float, Float) eval(Float t)
        {
            Fhat = EvaluatePolynomial(t, 0, f0, 0.5f * d0,
                                      (1f / 3f) * (-2 * d0 - d1) + f1 - f0,
                                      0.25f * (d0 + d1) + 0.5f * (f0 - f1));
            fhat = EvaluatePolynomial(t, f0, d0, -2 * d0 - d1 + 3 * (f1 - f0),
                                      d0 + d1 + 2 * (f0 - f1));
            return (Fhat - u, fhat);
        }
        Float tResult = NewtonBisection(0, 1, eval);
        fhatOut = fhat;
        return tResult;
    }

    private static Float InterpolateCatmullRom2D(ReadOnlySpan<Float> array, int idx,
                                                 ReadOnlySpan<Float> weights, int offset, int n2)
    {
        Float v = 0;
        for (int i = 0; i < 4; ++i)
            if (weights[i] != 0)
                v += array[(offset + i) * n2 + idx] * weights[i];
        return v;
    }

    private readonly ref struct CatmullRom2DCdfPredicate(ReadOnlySpan<Float> cdf, ReadOnlySpan<Float> weights,
                                                         int offset, int n2, Float u) : IIndexPredicate
    {
        private readonly ReadOnlySpan<Float> cdf = cdf, weights = weights;

        public bool Evaluate(int i) => InterpolateCatmullRom2D(cdf, i, weights, offset, n2) <= u;
    }

    public static Float SampleCatmullRom2D(ReadOnlySpan<Float> nodes1, ReadOnlySpan<Float> nodes2,
                                           ReadOnlySpan<Float> values, ReadOnlySpan<Float> cdf,
                                           Float alpha, Float u) =>
        SampleCatmullRom2D(nodes1, nodes2, values, cdf, alpha, u, out _, out _);

    public static Float SampleCatmullRom2D(ReadOnlySpan<Float> nodes1, ReadOnlySpan<Float> nodes2,
                                           ReadOnlySpan<Float> values, ReadOnlySpan<Float> cdf,
                                           Float alpha, Float u, out Float fval, out Float pdf)
    {
        fval = pdf = 0;
        // Determine offset and coefficients for the _alpha_ parameter
        Span<Float> weights = stackalloc Float[4];
        if (!CatmullRomWeights(nodes1, alpha, out int offset, weights))
            return 0;
        int n2 = nodes2.Length;

        // Map _u_ to a spline interval by inverting the interpolated _cdf_
        Float maximum = InterpolateCatmullRom2D(cdf, n2 - 1, weights, offset, n2);
        u *= maximum;
        int idx = FindInterval(n2, new CatmullRom2DCdfPredicate(cdf, weights, offset, n2, u));

        // Look up node positions and interpolated function values
        Float f0 = InterpolateCatmullRom2D(values, idx, weights, offset, n2),
              f1 = InterpolateCatmullRom2D(values, idx + 1, weights, offset, n2);
        Float x0 = nodes2[idx], x1 = nodes2[idx + 1];
        Float width = x1 - x0;
        Float d0, d1;

        // Rescale _u_ using the interpolated _cdf_
        u = (u - InterpolateCatmullRom2D(cdf, idx, weights, offset, n2)) / width;

        // Approximate derivatives using finite differences of the interpolant
        if (idx > 0)
            d0 = width * (f1 - InterpolateCatmullRom2D(values, idx - 1, weights, offset, n2)) /
                 (x1 - nodes2[idx - 1]);
        else
            d0 = f1 - f0;
        if (idx + 2 < n2)
            d1 = width * (InterpolateCatmullRom2D(values, idx + 2, weights, offset, n2) - f0) /
                 (nodes2[idx + 2] - x0);
        else
            d1 = f1 - f0;

        // Invert definite integral over spline segment
        Float t = InvertSplineSegment(f0, f1, d0, d1, u, out Float fhat);

        fval = fhat;
        pdf = fhat / maximum;
        return x0 + width * t;
    }

    public static Float[] Sample1DFunction(Func<Float, Float> f, int nSteps, int nSamples,
                                           Float min = 0, Float max = 1)
    {
        var values = new Float[nSteps];
        for (int i = 0; i < nSteps; ++i)
        {
            double accum = 0;
            // One extra so that we sample at the very start and the very end.
            for (int j = 0; j < nSamples + 1; ++j)
            {
                Float delta = (Float)j / nSamples;
                Float v = Lerp((i + delta) / (Float)nSteps, min, max);
                Float fv = FMath.Abs(f(v));
                accum = Max(accum, fv);
            }
            // There's actually no need for the divide by nSamples, since
            // these are normalzed into a PDF anyway.
            values[i] = (Float)accum;
        }
        return values;
    }

    public static Array2D<Float> Sample2DFunction(Func<Float, Float, Float> f, int nu, int nv,
                                                  int nSamples) =>
        Sample2DFunction(f, nu, nv, nSamples, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));

    public static Array2D<Float> Sample2DFunction(Func<Float, Float, Float> f, int nu, int nv,
                                                  int nSamples, Bounds2f domain)
    {
        var samples = new List<Point2f>(nSamples + 3);
        for (int i = 0; i < nSamples; ++i)
            samples.Add(new Point2f(RadicalInverse(0, (ulong)i), RadicalInverse(1, (ulong)i)));
        // Check the corners, too.
        samples.Add(new Point2f(0, 1));
        samples.Add(new Point2f(1, 0));
        samples.Add(new Point2f(1, 1));

        var values = new Array2D<Float>(nu, nv);
        for (int v = 0; v < nv; ++v)
        {
            for (int u = 0; u < nu; ++u)
            {
                double accum = 0;
                for (int i = 0; i < samples.Count; ++i)
                {
                    Point2f p = domain.Lerp(
                        new Point2f((u + samples[i][0]) / nu, (v + samples[i][1]) / nv));
                    Float fuv = FMath.Abs(f(p.x, p.y));
                    accum = Max(accum, fuv);
                }
                // There's actually no need for the divide by nSamples, since
                // these are normalzed into a PDF anyway.
                values[u, v] = (Float)accum;
            }
        }

        return values;
    }
}

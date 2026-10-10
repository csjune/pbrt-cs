// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the Transform class and functions in src/pbrt/util/transform.h
// and transform.cpp. AnimatedTransform is in AnimatedTransform.cs.
//
// Transform is an immutable class: pbrt mostly refers to transforms through
// pointers (shapes, primitives, cameras), and a class avoids both copying
// 128 bytes around and a zero-matrix default(Transform). pbrt's t(x) is
// t.Apply(x); the optional Float *tMax argument is a ref overload.
// Apply/ApplyInverse for Interaction and SurfaceInteraction are in
// Interaction.cs.

namespace Pbrt;

// Transform Definition
public sealed partial class Transform : IEquatable<Transform>
{
    // Transform Private Members
    private readonly SquareMatrix4 m, mInv;

    // Transform Public Methods
    public Transform()
    {
        m = new SquareMatrix4();
        mInv = new SquareMatrix4();
    }

    public Transform(in SquareMatrix4 m)
    {
        this.m = m;
        SquareMatrix4? inv = Inverse(m);
        if (inv.HasValue)
            mInv = inv.Value;
        else
        {
            // Initialize _mInv_ with not-a-number values
            SquareMatrix4 nan = default;
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    nan[i, j] = Float.NaN;
            mInv = nan;
        }
    }

    public Transform(Float[,] mat) : this(new SquareMatrix4(mat)) { }

    public Transform(in SquareMatrix4 m, in SquareMatrix4 mInv)
    {
        this.m = m;
        this.mInv = mInv;
    }

    public Transform(in Frame frame)
        : this(new SquareMatrix4(frame.x.x, frame.x.y, frame.x.z, 0, frame.y.x, frame.y.y,
                                 frame.y.z, 0, frame.z.x, frame.z.y, frame.z.z, 0, 0, 0, 0, 1)) { }

    public Transform(Quaternion q)
    {
        Float xx = q.v.x * q.v.x, yy = q.v.y * q.v.y, zz = q.v.z * q.v.z;
        Float xy = q.v.x * q.v.y, xz = q.v.x * q.v.z, yz = q.v.y * q.v.z;
        Float wx = q.v.x * q.w, wy = q.v.y * q.w, wz = q.v.z * q.w;

        SquareMatrix4 mi = new();
        mi[0, 0] = 1 - 2 * (yy + zz);
        mi[0, 1] = 2 * (xy + wz);
        mi[0, 2] = 2 * (xz - wy);
        mi[1, 0] = 2 * (xy - wz);
        mi[1, 1] = 1 - 2 * (xx + zz);
        mi[1, 2] = 2 * (yz + wx);
        mi[2, 0] = 2 * (xz + wy);
        mi[2, 1] = 2 * (yz - wx);
        mi[2, 2] = 1 - 2 * (xx + yy);
        mInv = mi;

        // Transpose since we are left-handed.  Ugh.
        m = Transpose(mInv);
    }

    public ref readonly SquareMatrix4 GetMatrix() => ref m;
    public ref readonly SquareMatrix4 GetInverseMatrix() => ref mInv;

    public static bool operator ==(Transform? a, Transform? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.m == b.m);
    public static bool operator !=(Transform? a, Transform? b) => !(a == b);
    public bool Equals(Transform? other) => this == other;
    public override bool Equals(object? obj) => obj is Transform other && this == other;
    // std::hash<Transform>: hashes the matrix bytes.
    public override int GetHashCode() => (int)Hash(m);

    public bool IsIdentity() => m.IsIdentity();

    public bool HasScale(Float tolerance = 1e-3f)
    {
        Float la2 = LengthSquared(Apply(new Vector3f(1, 0, 0)));
        Float lb2 = LengthSquared(Apply(new Vector3f(0, 1, 0)));
        Float lc2 = LengthSquared(Apply(new Vector3f(0, 0, 1)));
        return FMath.Abs(la2 - 1) > tolerance || FMath.Abs(lb2 - 1) > tolerance ||
               FMath.Abs(lc2 - 1) > tolerance;
    }

    // Transform Inline Methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Point3f Apply(Point3f p)
    {
        Float xp = m[0, 0] * p.x + m[0, 1] * p.y + m[0, 2] * p.z + m[0, 3];
        Float yp = m[1, 0] * p.x + m[1, 1] * p.y + m[1, 2] * p.z + m[1, 3];
        Float zp = m[2, 0] * p.x + m[2, 1] * p.y + m[2, 2] * p.z + m[2, 3];
        Float wp = m[3, 0] * p.x + m[3, 1] * p.y + m[3, 2] * p.z + m[3, 3];
        if (wp == 1)
            return new Point3f(xp, yp, zp);
        else
            return new Point3f(xp, yp, zp) / wp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3f Apply(Vector3f v) =>
        new(m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z,
            m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z,
            m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Normal3f Apply(Normal3f n)
    {
        Float x = n.x, y = n.y, z = n.z;
        return new Normal3f(mInv[0, 0] * x + mInv[1, 0] * y + mInv[2, 0] * z,
                            mInv[0, 1] * x + mInv[1, 1] * y + mInv[2, 1] * z,
                            mInv[0, 2] * x + mInv[1, 2] * y + mInv[2, 2] * z);
    }

    public Point3fi Apply(Point3fi p)
    {
        Float x = (Float)p.x, y = (Float)p.y, z = (Float)p.z;
        // Compute transformed coordinates from point _(x, y, z)_
        Float xp = (m[0, 0] * x + m[0, 1] * y) + (m[0, 2] * z + m[0, 3]);
        Float yp = (m[1, 0] * x + m[1, 1] * y) + (m[1, 2] * z + m[1, 3]);
        Float zp = (m[2, 0] * x + m[2, 1] * y) + (m[2, 2] * z + m[2, 3]);
        Float wp = (m[3, 0] * x + m[3, 1] * y) + (m[3, 2] * z + m[3, 3]);

        // Compute absolute error for transformed point, _pError_
        Vector3f pError;
        if (p.IsExact())
        {
            // Compute error for transformed exact _p_
            pError.x = Gamma(3) * (FMath.Abs(m[0, 0] * x) + FMath.Abs(m[0, 1] * y) +
                                   FMath.Abs(m[0, 2] * z) + FMath.Abs(m[0, 3]));
            pError.y = Gamma(3) * (FMath.Abs(m[1, 0] * x) + FMath.Abs(m[1, 1] * y) +
                                   FMath.Abs(m[1, 2] * z) + FMath.Abs(m[1, 3]));
            pError.z = Gamma(3) * (FMath.Abs(m[2, 0] * x) + FMath.Abs(m[2, 1] * y) +
                                   FMath.Abs(m[2, 2] * z) + FMath.Abs(m[2, 3]));
        }
        else
        {
            // Compute error for transformed approximate _p_
            Vector3f pInError = p.Error();
            pError.x = (Gamma(3) + 1) * (FMath.Abs(m[0, 0]) * pInError.x +
                                         FMath.Abs(m[0, 1]) * pInError.y +
                                         FMath.Abs(m[0, 2]) * pInError.z) +
                       Gamma(3) * (FMath.Abs(m[0, 0] * x) + FMath.Abs(m[0, 1] * y) +
                                   FMath.Abs(m[0, 2] * z) + FMath.Abs(m[0, 3]));
            pError.y = (Gamma(3) + 1) * (FMath.Abs(m[1, 0]) * pInError.x +
                                         FMath.Abs(m[1, 1]) * pInError.y +
                                         FMath.Abs(m[1, 2]) * pInError.z) +
                       Gamma(3) * (FMath.Abs(m[1, 0] * x) + FMath.Abs(m[1, 1] * y) +
                                   FMath.Abs(m[1, 2] * z) + FMath.Abs(m[1, 3]));
            pError.z = (Gamma(3) + 1) * (FMath.Abs(m[2, 0]) * pInError.x +
                                         FMath.Abs(m[2, 1]) * pInError.y +
                                         FMath.Abs(m[2, 2]) * pInError.z) +
                       Gamma(3) * (FMath.Abs(m[2, 0] * x) + FMath.Abs(m[2, 1] * y) +
                                   FMath.Abs(m[2, 2] * z) + FMath.Abs(m[2, 3]));
        }

        if (wp == 1)
            return new Point3fi(new Point3f(xp, yp, zp), pError);
        else
            return new Point3fi(new Point3f(xp, yp, zp), pError) / wp;
    }

    public Vector3fi Apply(Vector3fi v)
    {
        Float x = (Float)v.x, y = (Float)v.y, z = (Float)v.z;
        Vector3f vOutError;
        if (v.IsExact())
        {
            vOutError.x = Gamma(3) * (FMath.Abs(m[0, 0] * x) + FMath.Abs(m[0, 1] * y) +
                                      FMath.Abs(m[0, 2] * z));
            vOutError.y = Gamma(3) * (FMath.Abs(m[1, 0] * x) + FMath.Abs(m[1, 1] * y) +
                                      FMath.Abs(m[1, 2] * z));
            vOutError.z = Gamma(3) * (FMath.Abs(m[2, 0] * x) + FMath.Abs(m[2, 1] * y) +
                                      FMath.Abs(m[2, 2] * z));
        }
        else
        {
            Vector3f vInError = v.Error();
            vOutError.x = (Gamma(3) + 1) * (FMath.Abs(m[0, 0]) * vInError.x +
                                            FMath.Abs(m[0, 1]) * vInError.y +
                                            FMath.Abs(m[0, 2]) * vInError.z) +
                          Gamma(3) * (FMath.Abs(m[0, 0] * x) + FMath.Abs(m[0, 1] * y) +
                                      FMath.Abs(m[0, 2] * z));
            vOutError.y = (Gamma(3) + 1) * (FMath.Abs(m[1, 0]) * vInError.x +
                                            FMath.Abs(m[1, 1]) * vInError.y +
                                            FMath.Abs(m[1, 2]) * vInError.z) +
                          Gamma(3) * (FMath.Abs(m[1, 0] * x) + FMath.Abs(m[1, 1] * y) +
                                      FMath.Abs(m[1, 2] * z));
            vOutError.z = (Gamma(3) + 1) * (FMath.Abs(m[2, 0]) * vInError.x +
                                            FMath.Abs(m[2, 1]) * vInError.y +
                                            FMath.Abs(m[2, 2]) * vInError.z) +
                          Gamma(3) * (FMath.Abs(m[2, 0] * x) + FMath.Abs(m[2, 1] * y) +
                                      FMath.Abs(m[2, 2] * z));
        }

        Float xp = m[0, 0] * x + m[0, 1] * y + m[0, 2] * z;
        Float yp = m[1, 0] * x + m[1, 1] * y + m[1, 2] * z;
        Float zp = m[2, 0] * x + m[2, 1] * y + m[2, 2] * z;

        return new Vector3fi(new Vector3f(xp, yp, zp), vOutError);
    }

    public Ray Apply(in Ray r)
    {
        Float tMax = 0;
        return Apply(r, ref tMax);
    }

    public Ray Apply(in Ray r, ref Float tMax)
    {
        Point3fi o = Apply(new Point3fi(r.o));
        Vector3f d = Apply(r.d);
        // Offset ray origin to edge of error bounds and compute _tMax_
        Float lengthSquared = LengthSquared(d);
        if (lengthSquared > 0)
        {
            Float dt = Dot(Abs(d), o.Error()) / lengthSquared;
            o += d * dt;
            tMax -= dt;
        }

        return new Ray(new Point3f(o), d, r.time, r.medium);
    }

    public RayDifferential Apply(in RayDifferential r)
    {
        Float tMax = 0;
        return Apply(r, ref tMax);
    }

    public RayDifferential Apply(in RayDifferential r, ref Float tMax)
    {
        Ray tr = Apply((Ray)r, ref tMax);
        RayDifferential ret = new(tr.o, tr.d, tr.time, tr.medium);
        ret.hasDifferentials = r.hasDifferentials;
        ret.rxOrigin = Apply(r.rxOrigin);
        ret.ryOrigin = Apply(r.ryOrigin);
        ret.rxDirection = Apply(r.rxDirection);
        ret.ryDirection = Apply(r.ryDirection);
        return ret;
    }

    public Bounds3f Apply(in Bounds3f b)
    {
        Bounds3f bt = new();
        for (int i = 0; i < 8; ++i)
            bt = Union(bt, Apply(b.Corner(i)));
        return bt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Point3f ApplyInverse(Point3f p)
    {
        Float x = p.x, y = p.y, z = p.z;
        Float xp = (mInv[0, 0] * x + mInv[0, 1] * y) + (mInv[0, 2] * z + mInv[0, 3]);
        Float yp = (mInv[1, 0] * x + mInv[1, 1] * y) + (mInv[1, 2] * z + mInv[1, 3]);
        Float zp = (mInv[2, 0] * x + mInv[2, 1] * y) + (mInv[2, 2] * z + mInv[2, 3]);
        Float wp = (mInv[3, 0] * x + mInv[3, 1] * y) + (mInv[3, 2] * z + mInv[3, 3]);
        CHECK_NE(wp, 0);
        if (wp == 1)
            return new Point3f(xp, yp, zp);
        else
            return new Point3f(xp, yp, zp) / wp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3f ApplyInverse(Vector3f v)
    {
        Float x = v.x, y = v.y, z = v.z;
        return new Vector3f(mInv[0, 0] * x + mInv[0, 1] * y + mInv[0, 2] * z,
                            mInv[1, 0] * x + mInv[1, 1] * y + mInv[1, 2] * z,
                            mInv[2, 0] * x + mInv[2, 1] * y + mInv[2, 2] * z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Normal3f ApplyInverse(Normal3f n)
    {
        Float x = n.x, y = n.y, z = n.z;
        return new Normal3f(m[0, 0] * x + m[1, 0] * y + m[2, 0] * z,
                            m[0, 1] * x + m[1, 1] * y + m[2, 1] * z,
                            m[0, 2] * x + m[1, 2] * y + m[2, 2] * z);
    }

    public Point3fi ApplyInverse(Point3fi p)
    {
        Float x = (Float)p.x, y = (Float)p.y, z = (Float)p.z;
        // Compute transformed coordinates from point _pt_
        Float xp = (mInv[0, 0] * x + mInv[0, 1] * y) + (mInv[0, 2] * z + mInv[0, 3]);
        Float yp = (mInv[1, 0] * x + mInv[1, 1] * y) + (mInv[1, 2] * z + mInv[1, 3]);
        Float zp = (mInv[2, 0] * x + mInv[2, 1] * y) + (mInv[2, 2] * z + mInv[2, 3]);
        Float wp = (mInv[3, 0] * x + mInv[3, 1] * y) + (mInv[3, 2] * z + mInv[3, 3]);

        // Compute absolute error for transformed point
        Vector3f pOutError;
        if (p.IsExact())
        {
            pOutError.x = Gamma(3) * (FMath.Abs(mInv[0, 0] * x) + FMath.Abs(mInv[0, 1] * y) +
                                      FMath.Abs(mInv[0, 2] * z));
            pOutError.y = Gamma(3) * (FMath.Abs(mInv[1, 0] * x) + FMath.Abs(mInv[1, 1] * y) +
                                      FMath.Abs(mInv[1, 2] * z));
            pOutError.z = Gamma(3) * (FMath.Abs(mInv[2, 0] * x) + FMath.Abs(mInv[2, 1] * y) +
                                      FMath.Abs(mInv[2, 2] * z));
        }
        else
        {
            Vector3f pInError = p.Error();
            pOutError.x = (Gamma(3) + 1) * (FMath.Abs(mInv[0, 0]) * pInError.x +
                                            FMath.Abs(mInv[0, 1]) * pInError.y +
                                            FMath.Abs(mInv[0, 2]) * pInError.z) +
                          Gamma(3) * (FMath.Abs(mInv[0, 0] * x) + FMath.Abs(mInv[0, 1] * y) +
                                      FMath.Abs(mInv[0, 2] * z) + FMath.Abs(mInv[0, 3]));
            pOutError.y = (Gamma(3) + 1) * (FMath.Abs(mInv[1, 0]) * pInError.x +
                                            FMath.Abs(mInv[1, 1]) * pInError.y +
                                            FMath.Abs(mInv[1, 2]) * pInError.z) +
                          Gamma(3) * (FMath.Abs(mInv[1, 0] * x) + FMath.Abs(mInv[1, 1] * y) +
                                      FMath.Abs(mInv[1, 2] * z) + FMath.Abs(mInv[1, 3]));
            pOutError.z = (Gamma(3) + 1) * (FMath.Abs(mInv[2, 0]) * pInError.x +
                                            FMath.Abs(mInv[2, 1]) * pInError.y +
                                            FMath.Abs(mInv[2, 2]) * pInError.z) +
                          Gamma(3) * (FMath.Abs(mInv[2, 0] * x) + FMath.Abs(mInv[2, 1] * y) +
                                      FMath.Abs(mInv[2, 2] * z) + FMath.Abs(mInv[2, 3]));
        }

        if (wp == 1)
            return new Point3fi(new Point3f(xp, yp, zp), pOutError);
        else
            return new Point3fi(new Point3f(xp, yp, zp), pOutError) / wp;
    }

    public Ray ApplyInverse(in Ray r)
    {
        Float tMax = 0;
        return ApplyInverse(r, ref tMax);
    }

    public Ray ApplyInverse(in Ray r, ref Float tMax)
    {
        Point3fi o = ApplyInverse(new Point3fi(r.o));
        Vector3f d = ApplyInverse(r.d);
        // Offset ray origin to edge of error bounds and compute _tMax_
        Float lengthSquared = LengthSquared(d);
        if (lengthSquared > 0)
        {
            Vector3f oError = new(o.x.Width / 2, o.y.Width / 2, o.z.Width / 2);
            Float dt = Dot(Abs(d), oError) / lengthSquared;
            o += d * dt;
            tMax -= dt;
        }
        return new Ray(new Point3f(o), d, r.time, r.medium);
    }

    public RayDifferential ApplyInverse(in RayDifferential r)
    {
        Float tMax = 0;
        return ApplyInverse(r, ref tMax);
    }

    public RayDifferential ApplyInverse(in RayDifferential r, ref Float tMax)
    {
        Ray tr = ApplyInverse((Ray)r, ref tMax);
        RayDifferential ret = new(tr.o, tr.d, tr.time, tr.medium);
        ret.hasDifferentials = r.hasDifferentials;
        ret.rxOrigin = ApplyInverse(r.rxOrigin);
        ret.ryOrigin = ApplyInverse(r.ryOrigin);
        ret.rxDirection = ApplyInverse(r.rxDirection);
        ret.ryDirection = ApplyInverse(r.ryDirection);
        return ret;
    }

    // Transform Method Definitions
    public static Transform operator *(Transform t1, Transform t2) =>
        new(t1.m * t2.m, t2.mInv * t1.mInv);

    public bool SwapsHandedness()
    {
        SquareMatrix3 s = new(m[0, 0], m[0, 1], m[0, 2],
                              m[1, 0], m[1, 1], m[1, 2],
                              m[2, 0], m[2, 1], m[2, 2]);
        return Determinant(s) < 0;
    }

    public static explicit operator Quaternion(Transform t)
    {
        ref readonly SquareMatrix4 m = ref t.m;
        Float trace = m[0, 0] + m[1, 1] + m[2, 2];
        Quaternion quat = new();
        if (trace > 0f)
        {
            // Compute w from matrix trace, then xyz
            // 4w^2 = m[0][0] + m[1][1] + m[2][2] + m[3][3] (but m[3][3] == 1)
            Float s = FMath.Sqrt(trace + 1.0f);
            quat.w = s / 2.0f;
            s = 0.5f / s;
            quat.v.x = (m[2, 1] - m[1, 2]) * s;
            quat.v.y = (m[0, 2] - m[2, 0]) * s;
            quat.v.z = (m[1, 0] - m[0, 1]) * s;
        }
        else
        {
            // Compute largest of $x$, $y$, or $z$, then remaining components
            ReadOnlySpan<int> nxt = [1, 2, 0];
            Span<Float> q = stackalloc Float[3];
            int i = 0;
            if (m[1, 1] > m[0, 0])
                i = 1;
            if (m[2, 2] > m[i, i])
                i = 2;
            int j = nxt[i];
            int k = nxt[j];
            Float s = SafeSqrt((m[i, i] - (m[j, j] + m[k, k])) + 1.0f);
            q[i] = s * 0.5f;
            if (s != 0f)
                s = 0.5f / s;
            quat.w = (m[k, j] - m[j, k]) * s;
            q[j] = (m[j, i] + m[i, j]) * s;
            q[k] = (m[k, i] + m[i, k]) * s;
            quat.v.x = q[0];
            quat.v.y = q[1];
            quat.v.z = q[2];
        }
        return quat;
    }

    public void Decompose(out Vector3f T, out SquareMatrix4 R, out SquareMatrix4 S)
    {
        // Extract translation _T_ from transformation matrix
        T = new Vector3f(m[0, 3], m[1, 3], m[2, 3]);

        // Compute new transformation matrix _M_ without translation
        SquareMatrix4 M = m;
        for (int i = 0; i < 3; ++i)
            M[i, 3] = M[3, i] = 0f;
        M[3, 3] = 1f;

        // Extract rotation _R_ from transformation matrix
        Float norm;
        int count = 0;
        R = M;
        do
        {
            // Compute next matrix _Rnext_ in series
            SquareMatrix4 Rit = InvertOrExit(Transpose(R));
            SquareMatrix4 Rnext = (R + Rit) / 2;

            // Compute norm of difference between _R_ and _Rnext_
            norm = 0;
            for (int i = 0; i < 3; ++i)
            {
                Float n = FMath.Abs(R[i, 0] - Rnext[i, 0]) +
                          FMath.Abs(R[i, 1] - Rnext[i, 1]) +
                          FMath.Abs(R[i, 2] - Rnext[i, 2]);
                norm = Max(norm, n);
            }

            R = Rnext;
        } while (++count < 100 && norm > .0001);
        // XXX TODO FIXME deal with flip...

        // Compute scale _S_ using rotation and original matrix
        S = InvertOrExit(R) * M;
    }

    public override string ToString() => $"[ m: {m} mInv: {mInv} ]";
}

public static partial class Util
{
    // Transform Function Definitions
    public static Transform Translate(Vector3f delta)
    {
        SquareMatrix4 m = new(1, 0, 0, delta.x,
                              0, 1, 0, delta.y,
                              0, 0, 1, delta.z,
                              0, 0, 0, 1);
        SquareMatrix4 minv = new(1, 0, 0, -delta.x,
                                 0, 1, 0, -delta.y,
                                 0, 0, 1, -delta.z,
                                 0, 0, 0, 1);
        return new Transform(m, minv);
    }

    public static Transform Scale(Float x, Float y, Float z)
    {
        SquareMatrix4 m = new(x, 0, 0, 0,
                              0, y, 0, 0,
                              0, 0, z, 0,
                              0, 0, 0, 1);
        SquareMatrix4 minv = new(1 / x, 0, 0, 0,
                                 0, 1 / y, 0, 0,
                                 0, 0, 1 / z, 0,
                                 0, 0, 0, 1);
        return new Transform(m, minv);
    }

    public static Transform RotateX(Float theta)
    {
        Float sinTheta = FMath.Sin(Radians(theta));
        Float cosTheta = FMath.Cos(Radians(theta));
        SquareMatrix4 m = new(1, 0, 0, 0,
                              0, cosTheta, -sinTheta, 0,
                              0, sinTheta, cosTheta, 0,
                              0, 0, 0, 1);
        return new Transform(m, Transpose(m));
    }

    public static Transform RotateY(Float theta)
    {
        Float sinTheta = FMath.Sin(Radians(theta));
        Float cosTheta = FMath.Cos(Radians(theta));
        SquareMatrix4 m = new(cosTheta, 0, sinTheta, 0,
                              0, 1, 0, 0,
                              -sinTheta, 0, cosTheta, 0,
                              0, 0, 0, 1);
        return new Transform(m, Transpose(m));
    }

    public static Transform RotateZ(Float theta)
    {
        Float sinTheta = FMath.Sin(Radians(theta));
        Float cosTheta = FMath.Cos(Radians(theta));
        SquareMatrix4 m = new(cosTheta, -sinTheta, 0, 0,
                              sinTheta, cosTheta, 0, 0,
                              0, 0, 1, 0,
                              0, 0, 0, 1);
        return new Transform(m, Transpose(m));
    }

    public static Transform LookAt(Point3f pos, Point3f look, Vector3f up)
    {
        SquareMatrix4 worldFromCamera = new();
        // Initialize fourth column of viewing matrix
        worldFromCamera[0, 3] = pos.x;
        worldFromCamera[1, 3] = pos.y;
        worldFromCamera[2, 3] = pos.z;
        worldFromCamera[3, 3] = 1;

        // Initialize first three columns of viewing matrix
        Vector3f dir = Normalize(look - pos);
        if (Length(Cross(Normalize(up), dir)) == 0)
            LOG_FATAL(FormattableString.Invariant(
                $"LookAt: \"up\" vector ({up.x}, {up.y}, {up.z}) and viewing direction ({dir.x}, {dir.y}, {dir.z}) passed to LookAt are pointing in the same direction."));
        Vector3f right = Normalize(Cross(Normalize(up), dir));
        Vector3f newUp = Cross(dir, right);
        worldFromCamera[0, 0] = right.x;
        worldFromCamera[1, 0] = right.y;
        worldFromCamera[2, 0] = right.z;
        worldFromCamera[3, 0] = 0f;
        worldFromCamera[0, 1] = newUp.x;
        worldFromCamera[1, 1] = newUp.y;
        worldFromCamera[2, 1] = newUp.z;
        worldFromCamera[3, 1] = 0f;
        worldFromCamera[0, 2] = dir.x;
        worldFromCamera[1, 2] = dir.y;
        worldFromCamera[2, 2] = dir.z;
        worldFromCamera[3, 2] = 0f;

        SquareMatrix4 cameraFromWorld = InvertOrExit(worldFromCamera);
        return new Transform(cameraFromWorld, worldFromCamera);
    }

    public static Transform Orthographic(Float zNear, Float zFar) =>
        Scale(1, 1, 1 / (zFar - zNear)) * Translate(new Vector3f(0, 0, -zNear));

    public static Transform Perspective(Float fov, Float n, Float f)
    {
        // Perform projective divide for perspective projection
        SquareMatrix4 persp = new(1, 0, 0, 0,
                                  0, 1, 0, 0,
                                  0, 0, f / (f - n), -f * n / (f - n),
                                  0, 0, 1, 0);

        // Scale canonical perspective view to specified field of view
        Float invTanAng = 1 / FMath.Tan(Radians(fov) / 2);
        return Scale(invTanAng, invTanAng, 1) * new Transform(persp);
    }

    // Transform Inline Functions
    public static Transform Inverse(Transform t) =>
        new(t.GetInverseMatrix(), t.GetMatrix());

    public static Transform Transpose(Transform t) =>
        new(Transpose(t.GetMatrix()), Transpose(t.GetInverseMatrix()));

    public static Transform Rotate(Float sinTheta, Float cosTheta, Vector3f axis)
    {
        Vector3f a = Normalize(axis);
        SquareMatrix4 m = new();
        // Compute rotation of first basis vector
        m[0, 0] = a.x * a.x + (1 - a.x * a.x) * cosTheta;
        m[0, 1] = a.x * a.y * (1 - cosTheta) - a.z * sinTheta;
        m[0, 2] = a.x * a.z * (1 - cosTheta) + a.y * sinTheta;
        m[0, 3] = 0;

        // Compute rotations of second and third basis vectors
        m[1, 0] = a.x * a.y * (1 - cosTheta) + a.z * sinTheta;
        m[1, 1] = a.y * a.y + (1 - a.y * a.y) * cosTheta;
        m[1, 2] = a.y * a.z * (1 - cosTheta) - a.x * sinTheta;
        m[1, 3] = 0;

        m[2, 0] = a.x * a.z * (1 - cosTheta) - a.y * sinTheta;
        m[2, 1] = a.y * a.z * (1 - cosTheta) + a.x * sinTheta;
        m[2, 2] = a.z * a.z + (1 - a.z * a.z) * cosTheta;
        m[2, 3] = 0;

        return new Transform(m, Transpose(m));
    }

    public static Transform Rotate(Float theta, Vector3f axis)
    {
        Float sinTheta = FMath.Sin(Radians(theta));
        Float cosTheta = FMath.Cos(Radians(theta));
        return Rotate(sinTheta, cosTheta, axis);
    }

    public static Transform RotateFromTo(Vector3f from, Vector3f to)
    {
        // Compute intermediate vector for vector reflection
        Vector3f refl;
        if (FMath.Abs(from.x) < 0.72f && FMath.Abs(to.x) < 0.72f)
            refl = new Vector3f(1, 0, 0);
        else if (FMath.Abs(from.y) < 0.72f && FMath.Abs(to.y) < 0.72f)
            refl = new Vector3f(0, 1, 0);
        else
            refl = new Vector3f(0, 0, 1);

        // Initialize matrix _r_ for rotation
        Vector3f u = refl - from, v = refl - to;
        SquareMatrix4 r = new();
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j)
                // Initialize matrix element _r[i][j]_
                r[i, j] = ((i == j) ? 1 : 0) - 2 / Dot(u, u) * u[i] * u[j] -
                          2 / Dot(v, v) * v[i] * v[j] +
                          4 * Dot(u, v) / (Dot(u, u) * Dot(v, v)) * v[i] * u[j];

        return new Transform(r, Transpose(r));
    }
}

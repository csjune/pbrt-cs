// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/math.h and math.cpp (scalar functions).
// Interval lives in Interval.cs and SquareMatrix in SquareMatrix.cs.
// EqualAreaSquareToSphere() and friends live with the vector types.

using System.Numerics;

namespace Pbrt;

// CompensatedSum Definition
public struct CompensatedSum<T> where T : struct, IFloatingPoint<T>
{
    // CompensatedSum Private Members
    private T sum, c;

    // CompensatedSum Public Methods
    public CompensatedSum(T v)
    {
        sum = v;
        c = T.Zero;
    }

    public static CompensatedSum<T> operator +(CompensatedSum<T> s, T v)
    {
        T delta = v - s.c;
        T newSum = s.sum + delta;
        s.c = (newSum - s.sum) - delta;
        s.sum = newSum;
        return s;
    }

    public static explicit operator T(CompensatedSum<T> s) => s.sum;

    public readonly T Value => sum;

    public override readonly string ToString() => $"[ CompensatedSum sum: {sum} c: {c} ]";
}

// CompensatedFloat Definition
public readonly struct CompensatedFloat(Float v, Float err = 0)
{
    // CompensatedFloat Public Members
    public readonly Float v = v, err = err;

    // CompensatedFloat Public Methods
    public static explicit operator float(CompensatedFloat cf) => (float)(cf.v + cf.err);
    public static explicit operator double(CompensatedFloat cf) => (double)cf.v + (double)cf.err;

    public override string ToString() => $"[ CompensatedFloat v: {v} err: {err} ]";
}

// Predicate for the generic FindInterval().
public interface IIndexPredicate
{
    bool Evaluate(int index);
}

public static partial class Util
{
    // Mathematical Constants
    public const Float ShadowEpsilon = 0.0001f;

    public const Float Pi = (Float)3.14159265358979323846;
    public const Float InvPi = (Float)0.31830988618379067154;
    public const Float Inv2Pi = (Float)0.15915494309189533577;
    public const Float Inv4Pi = (Float)0.07957747154594766788;
    public const Float PiOver2 = (Float)1.57079632679489661923;
    public const Float PiOver4 = (Float)0.78539816339744830961;
    public const Float Sqrt2 = (Float)1.41421356237309504880;

    // Bit Operation Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReverseBits32(uint n)
    {
        n = (n << 16) | (n >> 16);
        n = ((n & 0x00ff00ff) << 8) | ((n & 0xff00ff00) >> 8);
        n = ((n & 0x0f0f0f0f) << 4) | ((n & 0xf0f0f0f0) >> 4);
        n = ((n & 0x33333333) << 2) | ((n & 0xcccccccc) >> 2);
        n = ((n & 0x55555555) << 1) | ((n & 0xaaaaaaaa) >> 1);
        return n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReverseBits64(ulong n)
    {
        ulong n0 = ReverseBits32((uint)n);
        ulong n1 = ReverseBits32((uint)(n >> 32));
        return (n0 << 32) | n1;
    }

    // https://fgiesen.wordpress.com/2009/12/13/decoding-morton-codes/
    // updated to 64 bits.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong LeftShift2(ulong x)
    {
        x &= 0xffffffff;
        x = (x ^ (x << 16)) & 0x0000ffff0000ffff;
        x = (x ^ (x << 8)) & 0x00ff00ff00ff00ff;
        x = (x ^ (x << 4)) & 0x0f0f0f0f0f0f0f0f;
        x = (x ^ (x << 2)) & 0x3333333333333333;
        x = (x ^ (x << 1)) & 0x5555555555555555;
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong EncodeMorton2(uint x, uint y) => (LeftShift2(y) << 1) | LeftShift2(x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LeftShift3(uint x)
    {
        DCHECK_LE(x, 1u << 10);
        if (x == (1 << 10))
            --x;
        x = (x | (x << 16)) & 0b00000011000000000000000011111111;
        // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x | (x << 8)) & 0b00000011000000001111000000001111;
        // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x | (x << 4)) & 0b00000011000011000011000011000011;
        // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x | (x << 2)) & 0b00001001001001001001001001001001;
        // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeMorton3(float x, float y, float z)
    {
        DCHECK_GE(x, 0);
        DCHECK_GE(y, 0);
        DCHECK_GE(z, 0);
        return (LeftShift3((uint)z) << 2) | (LeftShift3((uint)y) << 1) | LeftShift3((uint)x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Compact1By1(ulong x)
    {
        // x = -f-e -d-c -b-a -9-8 -7-6 -5-4 -3-2 -1-0
        x &= 0x5555555555555555;
        // x = --fe --dc --ba --98 --76 --54 --32 --10
        x = (x ^ (x >> 1)) & 0x3333333333333333;
        // x = ---- fedc ---- ba98 ---- 7654 ---- 3210
        x = (x ^ (x >> 2)) & 0x0f0f0f0f0f0f0f0f;
        // x = ---- ---- fedc ba98 ---- ---- 7654 3210
        x = (x ^ (x >> 4)) & 0x00ff00ff00ff00ff;
        // x = ---- ---- ---- ---- fedc ba98 7654 3210
        x = (x ^ (x >> 8)) & 0x0000ffff0000ffff;
        // ...
        x = (x ^ (x >> 16)) & 0xffffffff;
        return (uint)x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DecodeMorton2(ulong v, out uint x, out uint y)
    {
        x = Compact1By1(v);
        y = Compact1By1(v >> 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Compact1By2(uint x)
    {
        x &= 0x09249249;                  // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
        x = (x ^ (x >> 2)) & 0x030c30c3;  // x = ---- --98 ---- 76-- --54 ---- 32-- --10
        x = (x ^ (x >> 4)) & 0x0300f00f;  // x = ---- --98 ---- ---- 7654 ---- ---- 3210
        x = (x ^ (x >> 8)) & 0xff0000ff;  // x = ---- --98 ---- ---- ---- ---- 7654 3210
        x = (x ^ (x >> 16)) & 0x000003ff; // x = ---- ---- ---- ---- ---- --98 7654 3210
        return x;
    }

    // std::min / std::max semantics: unlike FMath.Min/Max, these return the
    // first argument when the comparison is false (NaN, or -0 vs +0).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Min<T>(T a, T b) where T : INumber<T> => (b < a) ? b : a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Max<T>(T a, T b) where T : INumber<T> => (a < b) ? b : a;

    // Math Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Lerp(Float x, Float a, Float b) => (1 - x) * a + x * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FMA(int a, int b, int c) => a * b + c;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FMA(long a, long b, long c) => a * b + c;

    public static Float WindowedSinc(Float x, Float radius, Float tau)
    {
        if (FMath.Abs(x) > radius)
            return 0;
        return Sinc(x) * Sinc(x / tau);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Sinc(Float x) => SinXOverX(Pi * x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Clamp<T>(T val, T low, T high) where T : INumber<T>
    {
        if (val < low)
            return low;
        else if (val > high)
            return high;
        else
            return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Mod<T>(T a, T b) where T : IBinaryInteger<T>
    {
        T result = a - (a / b) * b;
        return (result < T.Zero) ? result + b : result;
    }

    // C#'s floating-point % has std::fmod semantics.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Mod(Float a, Float b) => a % b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Radians(Float deg) => (Pi / 180) * deg;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Degrees(Float rad) => (180 / Pi) * rad;

    public static Float SmoothStep(Float x, Float a, Float b)
    {
        if (a == b)
            return (x < a) ? 0 : 1;
        DCHECK_LT(a, b);
        Float t = Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SafeSqrt(float x)
    {
        DCHECK_GE(x, -1e-3f); // not too negative
        return MathF.Sqrt(Max(0f, x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SafeSqrt(double x)
    {
        DCHECK_GE(x, -1e-3); // not too negative
        return Math.Sqrt(Max(0.0, x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Sqr<T>(T v) where T : INumber<T> => v * v;

    // pbrt's Pow<n>(v): same evaluation order as the C++ template recursion.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Pow(float v, int n)
    {
        if (n < 0)
            return 1 / Pow(v, -n);
        if (n == 0)
            return 1;
        if (n == 1)
            return v;
        float n2 = Pow(v, n / 2);
        return n2 * n2 * Pow(v, n & 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Pow(double v, int n)
    {
        if (n < 0)
            return 1 / Pow(v, -n);
        if (n == 0)
            return 1;
        if (n == 1)
            return v;
        double n2 = Pow(v, n / 2);
        return n2 * n2 * Pow(v, n & 1);
    }

    // EvaluatePolynomial(t, c0, c1, ...) = c0 + t * (c1 + t * (...))
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EvaluatePolynomial(float t, params ReadOnlySpan<float> c)
    {
        int n = c.Length;
        float r = c[n - 1];
        for (int i = n - 2; i >= 0; --i)
            r = FMA(t, r, c[i]);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double EvaluatePolynomial(double t, params ReadOnlySpan<double> c)
    {
        int n = c.Length;
        double r = c[n - 1];
        for (int i = n - 2; i >= 0; --i)
            r = FMA(t, r, c[i]);
        return r;
    }

    // http://www.plunk.org/~hatch/rightway.html
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float SinXOverX(Float x)
    {
        if (1 - x * x == 1)
            return 1;
        return FMath.Sin(x) / x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SafeASin(float x)
    {
        DCHECK(x >= -1.0001 && x <= 1.0001);
        return MathF.Asin(Clamp(x, -1, 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SafeACos(float x)
    {
        DCHECK(x >= -1.0001 && x <= 1.0001);
        return MathF.Acos(Clamp(x, -1, 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SafeASin(double x)
    {
        DCHECK(x >= -1.0001 && x <= 1.0001);
        return Math.Asin(Clamp(x, -1, 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SafeACos(double x)
    {
        DCHECK(x >= -1.0001 && x <= 1.0001);
        return Math.Acos(Clamp(x, -1, 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Log2(Float x)
    {
        const Float invLog2 = (Float)1.442695040888963387004650940071;
        return FMath.Log(x) * invLog2;
    }

    public static int Log2Int(float v)
    {
        DCHECK_GT(v, 0);
        if (v < 1)
            return -Log2Int(1 / v);
        // https://graphics.stanford.edu/~seander/bithacks.html#IntegerLog
        // (With an additional check of the significant to get round-to-nearest
        // rather than round down.)
        // midsignif = Significand(std::pow(2., 1.5))
        // i.e. grab the significand of a value halfway between two exponents,
        // in log space.
        const uint midsignif = 0b00000000001101010000010011110011;
        return Exponent(v) + ((Significand(v) >= midsignif) ? 1 : 0);
    }

    public static int Log2Int(double v)
    {
        DCHECK_GT(v, 0);
        if (v < 1)
            return -Log2Int(1 / v);
        const ulong midsignif = 0b110101000001001111001100110011111110011101111001101;
        return Exponent(v) + ((Significand(v) >= midsignif) ? 1 : 0);
    }

    // Returns 0 for v == 0, like the _BitScanReverse path in pbrt.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2Int(uint v) => BitOperations.Log2(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2Int(int v) => Log2Int((uint)v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2Int(ulong v) => BitOperations.Log2(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2Int(long v) => Log2Int((ulong)v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log4Int(int v) => Log2Int(v) / 2;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log4Int(uint v) => Log2Int(v) / 2;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log4Int(long v) => Log2Int(v) / 2;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log4Int(ulong v) => Log2Int(v) / 2;

    // https://stackoverflow.com/a/10792321
    public static float FastExp(float x)
    {
        // Compute $x'$ such that $\roman{e}^x = 2^{x'}$
        float xp = x * 1.442695041f;

        // Find integer and fractional components of $x'$
        float fxp = MathF.Floor(xp), f = xp - fxp;
        int i = (int)fxp;

        // Evaluate polynomial approximation of $2^f$
        float twoToF = EvaluatePolynomial(f, 1f, 0.695556856f, 0.226173572f, 0.0781455737f);

        // Scale $2^f$ by $2^i$ and return final result
        int exponent = Exponent(twoToF) + i;
        if (exponent < -126)
            return 0;
        if (exponent > 127)
            return float.PositiveInfinity;
        uint bits = FloatToBits(twoToF);
        bits &= 0b10000000011111111111111111111111u;
        bits |= (uint)(exponent + 127) << 23;
        return BitsToFloat(bits);
    }

    public static Float Gaussian(Float x, Float mu = 0, Float sigma = 1) =>
        1 / FMath.Sqrt(2 * Pi * sigma * sigma) * FastExp((float)(-Sqr(x - mu) / (2 * sigma * sigma)));

    public static Float GaussianIntegral(Float x0, Float x1, Float mu = 0, Float sigma = 1)
    {
        DCHECK_GT(sigma, 0);
        Float sigmaRoot2 = sigma * (Float)1.414213562373095;
        return 0.5f * (Erf((mu - x0) / sigmaRoot2) - Erf((mu - x1) / sigmaRoot2));
    }

    public static Float Logistic(Float x, Float s)
    {
        x = FMath.Abs(x);
        return FMath.Exp(-x / s) / (s * Sqr(1 + FMath.Exp(-x / s)));
    }

    public static Float LogisticCDF(Float x, Float s) => 1 / (1 + FMath.Exp(-x / s));

    public static Float TrimmedLogistic(Float x, Float s, Float a, Float b)
    {
        DCHECK_LT(a, b);
        return Logistic(x, s) / (LogisticCDF(b, s) - LogisticCDF(a, s));
    }

    // std::log1p(float). .NET's float.LogP1 is computed as Log(x + 1), which
    // loses precision for small x; evaluating in double is accurate for
    // floats. The double version is in Libm.cs.
    public static float Log1p(float x) => (float)Math.Log(1.0 + x);

    // std::erf(float): .NET has no erf, so this is glibc's erff (the
    // fdlibm-derived sysdeps/ieee754/flt-32/s_erff.c of glibc 2.39, which
    // the reference pbrt build uses), evaluated in float as there. It calls
    // MathF.Exp(), which is the C library's expf on Linux.
    //
    // ====================================================
    // Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
    //
    // Developed at SunPro, a Sun Microsystems, Inc. business.
    // Permission to use, copy, modify, and distribute this
    // software is freely granted, provided that this notice
    // is preserved.
    // ====================================================
    public static float Erf(float x)
    {
        const float tiny = 1e-30f, one = 1.0f;
        const float erx = 8.4506291151e-01f;
        // Coefficients for approximation to  erf on [0,0.84375]
        const float efx = 1.2837916613e-01f;
        const float pp0 = 1.2837916613e-01f, pp1 = -3.2504209876e-01f, pp2 = -2.8481749818e-02f,
                    pp3 = -5.7702702470e-03f, pp4 = -2.3763017452e-05f;
        const float qq1 = 3.9791721106e-01f, qq2 = 6.5022252500e-02f, qq3 = 5.0813062117e-03f,
                    qq4 = 1.3249473704e-04f, qq5 = -3.9602282413e-06f;
        // Coefficients for approximation to  erf  in [0.84375,1.25]
        const float pa0 = -2.3621185683e-03f, pa1 = 4.1485610604e-01f, pa2 = -3.7220788002e-01f,
                    pa3 = 3.1834661961e-01f, pa4 = -1.1089469492e-01f, pa5 = 3.5478305072e-02f,
                    pa6 = -2.1663755178e-03f;
        const float qa1 = 1.0642088205e-01f, qa2 = 5.4039794207e-01f, qa3 = 7.1828655899e-02f,
                    qa4 = 1.2617121637e-01f, qa5 = 1.3637083583e-02f, qa6 = 1.1984500103e-02f;
        // Coefficients for approximation to  erfc in [1.25,1/0.35]
        const float ra0 = -9.8649440333e-03f, ra1 = -6.9385856390e-01f, ra2 = -1.0558626175e+01f,
                    ra3 = -6.2375331879e+01f, ra4 = -1.6239666748e+02f, ra5 = -1.8460508728e+02f,
                    ra6 = -8.1287437439e+01f, ra7 = -9.8143291473e+00f;
        const float sa1 = 1.9651271820e+01f, sa2 = 1.3765776062e+02f, sa3 = 4.3456588745e+02f,
                    sa4 = 6.4538726807e+02f, sa5 = 4.2900814819e+02f, sa6 = 1.0863500214e+02f,
                    sa7 = 6.5702495575e+00f, sa8 = -6.0424413532e-02f;
        // Coefficients for approximation to  erfc in [1/.35,28]
        const float rb0 = -9.8649431020e-03f, rb1 = -7.9928326607e-01f, rb2 = -1.7757955551e+01f,
                    rb3 = -1.6063638306e+02f, rb4 = -6.3756646729e+02f, rb5 = -1.0250950928e+03f,
                    rb6 = -4.8351919556e+02f;
        const float sb1 = 3.0338060379e+01f, sb2 = 3.2579251099e+02f, sb3 = 1.5367296143e+03f,
                    sb4 = 3.1998581543e+03f, sb5 = 2.5530502930e+03f, sb6 = 4.7452853394e+02f,
                    sb7 = -2.2440952301e+01f;

        int hx = BitConverter.SingleToInt32Bits(x);
        int ix = hx & 0x7fffffff;
        if (ix >= 0x7f800000)
        {
            // erf(nan)=nan, erf(+-inf)=+-1
            int i = (int)(((uint)hx >> 31) << 1);
            return (float)(1 - i) + one / x;
        }

        float R, S, P, Q, s, y, z, r;
        if (ix < 0x3f580000)
        {
            // |x|<0.84375
            if (ix < 0x31800000)
            {
                // |x|<2**-28
                if (ix < 0x04000000)
                    // Avoid spurious underflow.
                    return 0.0625f * (16.0f * x + (16.0f * efx) * x);
                return x + efx * x;
            }
            z = x * x;
            r = pp0 + z * (pp1 + z * (pp2 + z * (pp3 + z * pp4)));
            s = one + z * (qq1 + z * (qq2 + z * (qq3 + z * (qq4 + z * qq5))));
            y = r / s;
            return x + x * y;
        }
        if (ix < 0x3fa00000)
        {
            // 0.84375 <= |x| < 1.25
            s = MathF.Abs(x) - one;
            P = pa0 + s * (pa1 + s * (pa2 + s * (pa3 + s * (pa4 + s * (pa5 + s * pa6)))));
            Q = one + s * (qa1 + s * (qa2 + s * (qa3 + s * (qa4 + s * (qa5 + s * qa6)))));
            if (hx >= 0)
                return erx + P / Q;
            else
                return -erx - P / Q;
        }
        if (ix >= 0x40c00000)
        {
            // inf>|x|>=6
            if (hx >= 0)
                return one - tiny;
            else
                return tiny - one;
        }
        x = MathF.Abs(x);
        s = one / (x * x);
        if (ix < 0x4036DB6E)
        {
            // |x| < 1/0.35
            R = ra0 + s * (ra1 + s * (ra2 + s * (ra3 + s * (ra4 + s * (ra5 + s * (ra6 + s * ra7))))));
            S = one + s * (sa1 + s * (sa2 + s * (sa3 + s * (sa4 + s * (sa5 + s * (sa6 + s * (sa7 + s * sa8)))))));
        }
        else
        {
            // |x| >= 1/0.35
            R = rb0 + s * (rb1 + s * (rb2 + s * (rb3 + s * (rb4 + s * (rb5 + s * rb6)))));
            S = one + s * (sb1 + s * (sb2 + s * (sb3 + s * (sb4 + s * (sb5 + s * (sb6 + s * sb7))))));
        }
        z = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x) & unchecked((int)0xfffff000));
        r = MathF.Exp(-z * z - 0.5625f) * MathF.Exp((z - x) * (z + x) + R / S);
        if (hx >= 0)
            return one - r / x;
        else
            return r / x - one;
    }

    // Returns the largest index i in [0, sz-2] such that pred(i) is true,
    // assuming pred is true then false over [0, sz).
    public static int FindInterval(int sz, Func<int, bool> pred)
    {
        int size = sz - 2, first = 1;
        while (size > 0)
        {
            // Evaluate predicate at midpoint and update _first_ and _size_
            int half = size >> 1, middle = first + half;
            bool predResult = pred(middle);
            first = predResult ? middle + 1 : first;
            size = predResult ? size - (half + 1) : half;
        }
        return sz < 2 ? 0 : Clamp(first - 1, 0, sz - 2);
    }

    // FindInterval() with a struct predicate: no delegate allocation, and
    // the predicate may be a ref struct holding spans (which lambdas cannot
    // capture).
    public static int FindInterval<P>(int sz, P pred) where P : IIndexPredicate, allows ref struct
    {
        int size = sz - 2, first = 1;
        while (size > 0)
        {
            int half = size >> 1, middle = first + half;
            bool predResult = pred.Evaluate(middle);
            first = predResult ? middle + 1 : first;
            size = predResult ? size - (half + 1) : half;
        }
        return sz < 2 ? 0 : Clamp(first - 1, 0, sz - 2);
    }

    // FindInterval() specialized for the common pred(i) = (a[i] <= x) case;
    // usable with spans, which lambdas cannot capture.
    public static int FindInterval(ReadOnlySpan<Float> a, Float x)
    {
        int sz = a.Length;
        int size = sz - 2, first = 1;
        while (size > 0)
        {
            int half = size >> 1, middle = first + half;
            bool predResult = a[middle] <= x;
            first = predResult ? middle + 1 : first;
            size = predResult ? size - (half + 1) : half;
        }
        return sz < 2 ? 0 : Clamp(first - 1, 0, sz - 2);
    }

#if PBRT_FLOAT_AS_DOUBLE
    // FindInterval() over float values with a Float (double) x.
    public static int FindInterval(ReadOnlySpan<float> a, Float x)
    {
        int sz = a.Length;
        int size = sz - 2, first = 1;
        while (size > 0)
        {
            int half = size >> 1, middle = first + half;
            bool predResult = a[middle] <= x;
            first = predResult ? middle + 1 : first;
            size = predResult ? size - (half + 1) : half;
        }
        return sz < 2 ? 0 : Clamp(first - 1, 0, sz - 2);
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOf2<T>(T v) where T : IBinaryInteger<T> => T.IsPow2(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOf4(int v) => v == 1 << (2 * Log4Int(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOf4(long v) => v == 1 << (2 * Log4Int(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUpPow2(int v) => (int)BitOperations.RoundUpToPowerOf2((uint)v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long RoundUpPow2(long v) => (long)BitOperations.RoundUpToPowerOf2((ulong)v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUpPow4(int v) => IsPowerOf4(v) ? v : (1 << (2 * (1 + Log4Int(v))));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long RoundUpPow4(long v) => IsPowerOf4(v) ? v : (1 << (2 * (1 + Log4Int(v))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CompensatedFloat TwoProd(Float a, Float b)
    {
        Float ab = a * b;
        return new CompensatedFloat(ab, FMA(a, b, -ab));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CompensatedFloat TwoSum(Float a, Float b)
    {
        Float s = a + b, delta = s - a;
        return new CompensatedFloat(s, (a - (s - delta)) + (b - delta));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DifferenceOfProducts(float a, float b, float c, float d)
    {
        float cd = c * d;
        float differenceOfProducts = FMA(a, b, -cd);
        float error = FMA(-c, d, cd);
        return differenceOfProducts + error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DifferenceOfProducts(double a, double b, double c, double d)
    {
        double cd = c * d;
        double differenceOfProducts = FMA(a, b, -cd);
        double error = FMA(-c, d, cd);
        return differenceOfProducts + error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SumOfProducts(float a, float b, float c, float d)
    {
        float cd = c * d;
        float sumOfProducts = FMA(a, b, cd);
        float error = FMA(c, d, -cd);
        return sumOfProducts + error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SumOfProducts(double a, double b, double c, double d)
    {
        double cd = c * d;
        double sumOfProducts = FMA(a, b, cd);
        double error = FMA(c, d, -cd);
        return sumOfProducts + error;
    }

    // Accurate dot products with FMA: Graillat et al.,
    // https://www-pequan.lip6.fr/~graillat/papers/posterRNC7.pdf
    //
    // InnerProduct(a0, b0, a1, b1, ...): terms are (a, b) pairs. Evaluated
    // right-to-left, as in pbrt's recursive template.
    public static Float InnerProduct(params ReadOnlySpan<Float> terms)
    {
        DCHECK((terms.Length & 1) == 0 && terms.Length >= 2);
        int n = terms.Length;
        CompensatedFloat tp = TwoProd(terms[n - 2], terms[n - 1]);
        for (int i = n - 4; i >= 0; i -= 2)
        {
            CompensatedFloat ab = TwoProd(terms[i], terms[i + 1]);
            CompensatedFloat sum = TwoSum(ab.v, tp.v);
            tp = new CompensatedFloat(sum.v, ab.err + (tp.err + sum.err));
        }
        return (Float)tp;
    }

    public static bool Quadratic(float a, float b, float c, out float t0, out float t1)
    {
        // Handle case of $a=0$ for quadratic solution
        if (a == 0)
        {
            if (b == 0)
            {
                t0 = t1 = 0;
                return false;
            }
            t0 = t1 = -c / b;
            return true;
        }

        // Find quadratic discriminant
        float discrim = DifferenceOfProducts(b, b, 4 * a, c);
        if (discrim < 0)
        {
            t0 = t1 = 0;
            return false;
        }
        float rootDiscrim = MathF.Sqrt(discrim);

        // Compute quadratic _t_ values
        float q = -0.5f * (b + MathF.CopySign(rootDiscrim, b));
        t0 = q / a;
        t1 = c / q;
        if (t0 > t1)
            (t0, t1) = (t1, t0);

        return true;
    }

    public static bool Quadratic(double a, double b, double c, out double t0, out double t1)
    {
        // Find quadratic discriminant
        double discrim = DifferenceOfProducts(b, b, 4 * a, c);
        if (discrim < 0)
        {
            t0 = t1 = 0;
            return false;
        }
        double rootDiscrim = Math.Sqrt(discrim);

        if (a == 0)
        {
            t0 = t1 = -c / b;
            return true;
        }

        // Compute quadratic _t_ values
        double q = -0.5 * (b + Math.CopySign(rootDiscrim, b));
        t0 = q / a;
        t1 = c / q;
        if (t0 > t1)
            (t0, t1) = (t1, t0);
        return true;
    }

    // f returns (value, derivative).
    public static Float NewtonBisection(Float x0, Float x1, Func<Float, (Float, Float)> f,
                                        Float xEps = 1e-6f, Float fEps = 1e-6f)
    {
        // Check function endpoints for roots
        DCHECK_LT(x0, x1);
        Float fx0 = f(x0).Item1, fx1 = f(x1).Item1;
        if (FMath.Abs(fx0) < fEps)
            return x0;
        if (FMath.Abs(fx1) < fEps)
            return x1;
        bool startIsNegative = fx0 < 0;

        // Set initial midpoint using linear approximation of _f_
        Float xMid = x0 + (x1 - x0) * -fx0 / (fx1 - fx0);

        while (true)
        {
            // Fall back to bisection if _xMid_ is out of bounds
            if (!(x0 < xMid && xMid < x1))
                xMid = (x0 + x1) / 2;

            // Evaluate function and narrow bracket range _[x0, x1]_
            (Float fxMid, Float dfxMid) = f(xMid);
            DCHECK(!IsNaN(fxMid));
            if (startIsNegative == (fxMid < 0))
                x0 = xMid;
            else
                x1 = xMid;

            // Stop the iteration if converged
            if ((x1 - x0) < xEps || FMath.Abs(fxMid) < fEps)
                return xMid;

            // Perform a Newton step
            xMid -= fxMid / dfxMid;
        }
    }

    // Math Function Definitions
    public static int NextPrime(int x)
    {
        if (x == 2)
            return 3;
        if ((x & 1) == 0)
            ++x; // make it odd

        List<int> smallPrimes = [2];
        // NOTE: isPrime w.r.t. smallPrims...
        bool IsPrime(int n)
        {
            foreach (int p in smallPrimes)
                if (n != p && (n % p) == 0)
                    return false;
            return true;
        }

        // Initialize smallPrimes
        // Up to about 2B, the biggest gap between primes:
        // https://en.wikipedia.org/wiki/Prime_gap
        const int maxPrimeGap = 320;
        for (int n = 3; n < (int)Math.Sqrt(x + maxPrimeGap) + 1; n += 2)
            if (IsPrime(n))
                smallPrimes.Add(n);

        while (!IsPrime(x))
            x += 2;

        return x;
    }

    // Permutation Inline Functions
    public static int PermutationElement(uint i, uint l, uint p)
    {
        uint w = l - 1;
        w |= w >> 1;
        w |= w >> 2;
        w |= w >> 4;
        w |= w >> 8;
        w |= w >> 16;
        do
        {
            i ^= p;
            i *= 0xe170893d;
            i ^= p >> 16;
            i ^= (i & w) >> 4;
            i ^= p >> 8;
            i *= 0x0929eb3f;
            i ^= p >> 23;
            i ^= (i & w) >> 1;
            i *= 1 | p >> 27;
            i *= 0x6935fa69;
            i ^= (i & w) >> 11;
            i *= 0x74dcb303;
            i ^= (i & w) >> 2;
            i *= 0x9e501cc3;
            i ^= (i & w) >> 2;
            i *= 0xc860a3df;
            i &= w;
            i ^= i >> 5;
        } while (i >= l);
        return (int)((i + p) % l);
    }

    public static Float ErfInv(Float a)
    {
        // https://stackoverflow.com/a/49743348
        float p;
        float t = (float)FMath.Log(Max(FMA(a, -a, 1), FloatMin));
        CHECK(!IsNaN(t) && !IsInf(t));
        if (MathF.Abs(t) > 6.125f)
        {                                     // maximum ulp error = 2.35793
            p = 3.03697567e-10f;              //  0x1.4deb44p-32
            p = FMA(p, t, 2.93243101e-8f);    //  0x1.f7c9aep-26
            p = FMA(p, t, 1.22150334e-6f);    //  0x1.47e512p-20
            p = FMA(p, t, 2.84108955e-5f);    //  0x1.dca7dep-16
            p = FMA(p, t, 3.93552968e-4f);    //  0x1.9cab92p-12
            p = FMA(p, t, 3.02698812e-3f);    //  0x1.8cc0dep-9
            p = FMA(p, t, 4.83185798e-3f);    //  0x1.3ca920p-8
            p = FMA(p, t, -2.64646143e-1f);   // -0x1.0eff66p-2
            p = FMA(p, t, 8.40016484e-1f);    //  0x1.ae16a4p-1
        }
        else
        {                                     // maximum ulp error = 2.35456
            p = 5.43877832e-9f;               //  0x1.75c000p-28
            p = FMA(p, t, 1.43286059e-7f);    //  0x1.33b458p-23
            p = FMA(p, t, 1.22775396e-6f);    //  0x1.49929cp-20
            p = FMA(p, t, 1.12962631e-7f);    //  0x1.e52bbap-24
            p = FMA(p, t, -5.61531961e-5f);   // -0x1.d70c12p-15
            p = FMA(p, t, -1.47697705e-4f);   // -0x1.35be9ap-13
            p = FMA(p, t, 2.31468701e-3f);    //  0x1.2f6402p-9
            p = FMA(p, t, 1.15392562e-2f);    //  0x1.7a1e4cp-7
            p = FMA(p, t, -2.32015476e-1f);   // -0x1.db2aeep-3
            p = FMA(p, t, 8.86226892e-1f);    //  0x1.c5bf88p-1
        }
        return a * p;
    }

    public static Float I0(Float x)
    {
        Float val = 0;
        Float x2i = 1;
        long ifact = 1;
        int i4 = 1;
        // I0(x) \approx Sum_i x^(2i) / (4^i (i!)^2)
        for (int i = 0; i < 10; ++i)
        {
            if (i > 1)
                ifact *= i;
            val += x2i / (i4 * Sqr(ifact));
            x2i *= x * x;
            i4 *= 4;
        }
        return val;
    }

    public static Float LogI0(Float x)
    {
        if (x > 12)
            return x + 0.5f * (-FMath.Log(2 * Pi) + FMath.Log(1 / x) + 1 / (8 * x));
        else
            return FMath.Log(I0(x));
    }

    // Spline Interpolation Function Definitions
    public static bool CatmullRomWeights(ReadOnlySpan<Float> nodes, Float x, out int offset,
                                         Span<Float> weights)
    {
        CHECK_GE(weights.Length, 4);
        offset = 0;
        // Return _false_ if _x_ is out of bounds
        if (!(x >= nodes[0] && x <= nodes[^1]))
            return false;

        // Search for the interval _idx_ containing _x_
        int idx = FindInterval(nodes, x);
        offset = idx - 1;
        Float x0 = nodes[idx], x1 = nodes[idx + 1];

        // Compute the $t$ parameter and powers
        Float t = (x - x0) / (x1 - x0), t2 = t * t, t3 = t2 * t;

        // Compute initial node weights $w_1$ and $w_2$
        weights[1] = 2 * t3 - 3 * t2 + 1;
        weights[2] = -2 * t3 + 3 * t2;

        // Compute first node weight $w_0$
        if (idx > 0)
        {
            Float w0 = (t3 - 2 * t2 + t) * (x1 - x0) / (x1 - nodes[idx - 1]);
            weights[0] = -w0;
            weights[2] += w0;
        }
        else
        {
            Float w0 = t3 - 2 * t2 + t;
            weights[0] = 0;
            weights[1] -= w0;
            weights[2] += w0;
        }

        // Compute last node weight $w_3$
        if (idx + 2 < nodes.Length)
        {
            Float w3 = (t3 - t2) * (x1 - x0) / (nodes[idx + 2] - x0);
            weights[1] -= w3;
            weights[3] = w3;
        }
        else
        {
            Float w3 = t3 - t2;
            weights[1] -= w3;
            weights[2] += w3;
            weights[3] = 0;
        }

        return true;
    }

    public static Float CatmullRom(ReadOnlySpan<Float> nodes, ReadOnlySpan<Float> f, Float x)
    {
        CHECK_EQ(nodes.Length, f.Length);
        if (!(x >= nodes[0] && x <= nodes[^1]))
            return 0;
        int idx = FindInterval(nodes, x);
        Float x0 = nodes[idx], x1 = nodes[idx + 1];
        Float f0 = f[idx], f1 = f[idx + 1];
        Float width = x1 - x0;
        Float d0, d1;
        if (idx > 0)
            d0 = width * (f1 - f[idx - 1]) / (x1 - nodes[idx - 1]);
        else
            d0 = f1 - f0;

        if (idx + 2 < nodes.Length)
            d1 = width * (f[idx + 2] - f0) / (nodes[idx + 2] - x0);
        else
            d1 = f1 - f0;

        Float t = (x - x0) / (x1 - x0), t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * f0 + (-2 * t3 + 3 * t2) * f1 + (t3 - 2 * t2 + t) * d0 +
               (t3 - t2) * d1;
    }

    public static Float InvertCatmullRom(ReadOnlySpan<Float> nodes, ReadOnlySpan<Float> f, Float u)
    {
        // Stop when _u_ is out of bounds
        if (!(u > f[0]))
            return nodes[0];
        else if (!(u < f[^1]))
            return nodes[^1];

        // Map _u_ to a spline interval by inverting _f_
        int i = FindInterval(f, u);

        // Look up $x_i$ and function values of spline segment _i_
        Float x0 = nodes[i], x1 = nodes[i + 1];
        Float f0 = f[i], f1 = f[i + 1];
        Float width = x1 - x0;

        // Approximate derivatives using finite differences
        Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
        Float d1 = (i + 2 < nodes.Length) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0)
                                          : (f1 - f0);

        // Invert the spline interpolant using Newton-Bisection
        (Float, Float) Eval(Float t)
        {
            // Compute powers of _t_
            Float t2 = t * t, t3 = t2 * t;

            // Set _Fhat_ using Equation (\ref{eq:cubicspline-as-basisfunctions})
            Float Fhat = (2 * t3 - 3 * t2 + 1) * f0 + (-2 * t3 + 3 * t2) * f1 +
                         (t3 - 2 * t2 + t) * d0 + (t3 - t2) * d1;

            // Set _fhat_ using Equation (\ref{eq:cubicspline-derivative})
            Float fhat = (6 * t2 - 6 * t) * f0 + (-6 * t2 + 6 * t) * f1 +
                         (3 * t2 - 4 * t + 1) * d0 + (3 * t2 - 2 * t) * d1;

            return (Fhat - u, fhat);
        }
        Float tRoot = NewtonBisection(0, 1, Eval);
        return x0 + tRoot * width;
    }

    public static Float IntegrateCatmullRom(ReadOnlySpan<Float> nodes, ReadOnlySpan<Float> f,
                                            Span<Float> cdf)
    {
        CHECK_EQ(nodes.Length, f.Length);
        Float sum = 0;
        cdf[0] = 0;
        for (int i = 0; i < nodes.Length - 1; ++i)
        {
            // Look up $x_i$ and function values of spline segment _i_
            Float x0 = nodes[i], x1 = nodes[i + 1];
            Float f0 = f[i], f1 = f[i + 1];
            Float width = x1 - x0;

            // Approximate derivatives using finite differences
            Float d0 = (i > 0) ? width * (f1 - f[i - 1]) / (x1 - nodes[i - 1]) : (f1 - f0);
            Float d1 = (i + 2 < nodes.Length) ? width * (f[i + 2] - f0) / (nodes[i + 2] - x0)
                                              : (f1 - f0);

            // Keep a running sum and build a cumulative distribution function
            sum += width * ((f0 + f1) / 2 + (d0 - d1) / 12);
            cdf[i + 1] = sum;
        }
        return sum;
    }
}

// pbrt-cs: double precision C library functions that .NET lacks or computes
// differently, for builds with Float = double (PBRT_FLOAT_AS_DOUBLE).
//
// These are glibc 2.39's sysdeps/ieee754/dbl-64/s_erf.c and s_log1p.c (the
// fdlibm-derived versions the reference pbrt build calls), evaluated in
// the same order so the results match bit for bit. On x86-64 CPUs with FMA,
// glibc's log1p() is __log1p_fma, the same source compiled with -mfma, where
// GCC fuses some multiply-adds; Log1p() fuses the same ones (read off its
// machine code) when the CPU has FMA.
//
// ====================================================
// Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
//
// Developed at SunPro, a Sun Microsystems, Inc. business.
// Permission to use, copy, modify, and distribute this
// software is freely granted, provided that this notice
// is preserved.
// ====================================================

namespace Pbrt;

public static partial class Util
{
    private static int HighWord(double x) => (int)(BitConverter.DoubleToUInt64Bits(x) >> 32);

    private static double WithHighWord(double x, int hi) =>
        BitConverter.UInt64BitsToDouble(((ulong)(uint)hi << 32) |
                                        (BitConverter.DoubleToUInt64Bits(x) & 0xffffffffUL));

    // std::erf(double)
    public static double Erf(double x)
    {
        const double tiny = 1e-300, one = 1.0;
        // c = (float)0.84506291151
        const double erx = 8.45062911510467529297e-01;
        // Coefficients for approximation to  erf on [0,0.84375]
        const double efx = 1.28379167095512586316e-01;
        const double pp0 = 1.28379167095512558561e-01, pp1 = -3.25042107247001499370e-01,
                     pp2 = -2.84817495755985104766e-02, pp3 = -5.77027029648944159157e-03,
                     pp4 = -2.37630166566501626084e-05;
        const double qq1 = 3.97917223959155352819e-01, qq2 = 6.50222499887672944485e-02,
                     qq3 = 5.08130628187576562776e-03, qq4 = 1.32494738004321644526e-04,
                     qq5 = -3.96022827877536812320e-06;
        // Coefficients for approximation to  erf  in [0.84375,1.25]
        const double pa0 = -2.36211856075265944077e-03, pa1 = 4.14856118683748331666e-01,
                     pa2 = -3.72207876035701323847e-01, pa3 = 3.18346619901161753674e-01,
                     pa4 = -1.10894694282396677476e-01, pa5 = 3.54783043256182359371e-02,
                     pa6 = -2.16637559486879084300e-03;
        const double qa1 = 1.06420880400844228286e-01, qa2 = 5.40397917702171048937e-01,
                     qa3 = 7.18286544141962662868e-02, qa4 = 1.26171219808761642112e-01,
                     qa5 = 1.36370839120290507362e-02, qa6 = 1.19844998467991074170e-02;
        // Coefficients for approximation to  erfc in [1.25,1/0.35]
        const double ra0 = -9.86494403484714822705e-03, ra1 = -6.93858572707181764372e-01,
                     ra2 = -1.05586262253232909814e+01, ra3 = -6.23753324503260060396e+01,
                     ra4 = -1.62396669462573470355e+02, ra5 = -1.84605092906711035994e+02,
                     ra6 = -8.12874355063065934246e+01, ra7 = -9.81432934416914548592e+00;
        const double sa1 = 1.96512716674392571292e+01, sa2 = 1.37657754143519042600e+02,
                     sa3 = 4.34565877475229228821e+02, sa4 = 6.45387271733267880336e+02,
                     sa5 = 4.29008140027567833386e+02, sa6 = 1.08635005541779435134e+02,
                     sa7 = 6.57024977031928170135e+00, sa8 = -6.04244152148580987438e-02;
        // Coefficients for approximation to  erfc in [1/.35,28]
        const double rb0 = -9.86494292470009928597e-03, rb1 = -7.99283237680523006574e-01,
                     rb2 = -1.77579549177547519889e+01, rb3 = -1.60636384855821916062e+02,
                     rb4 = -6.37566443368389627722e+02, rb5 = -1.02509513161107724954e+03,
                     rb6 = -4.83519191608651397019e+02;
        const double sb1 = 3.03380607434824582924e+01, sb2 = 3.25792512996573918826e+02,
                     sb3 = 1.53672958608443695994e+03, sb4 = 3.19985821950859553908e+03,
                     sb5 = 2.55305040643316442583e+03, sb6 = 4.74528541206955367215e+02,
                     sb7 = -2.24409524465858183362e+01;

        double R, S, P, Q, s, z, r;
        int hx = HighWord(x);
        int ix = hx & 0x7fffffff;
        if (ix >= 0x7ff00000)
        {
            // erf(nan)=nan, erf(+-inf)=+-1
            int i = (int)(((uint)hx >> 31) << 1);
            return (double)(1 - i) + one / x;
        }

        if (ix < 0x3feb0000)
        {
            // |x|<0.84375
            if (ix < 0x3e300000)
            {
                // |x|<2**-28
                if (ix < 0x00800000)
                    // Avoid spurious underflow.
                    return 0.0625 * (16.0 * x + (16.0 * efx) * x);
                return x + efx * x;
            }
            z = x * x;
            double r1 = pp0 + z * pp1, z2 = z * z;
            double r2 = pp2 + z * pp3, z4 = z2 * z2;
            double s1 = one + z * qq1;
            double s2 = qq2 + z * qq3;
            double s3 = qq4 + z * qq5;
            r = r1 + z2 * r2 + z4 * pp4;
            s = s1 + z2 * s2 + z4 * s3;
            double y = r / s;
            return x + x * y;
        }
        if (ix < 0x3ff40000)
        {
            // 0.84375 <= |x| < 1.25
            s = Math.Abs(x) - one;
            double P1 = pa0 + s * pa1, s2 = s * s;
            double Q1 = one + s * qa1, s4 = s2 * s2;
            double P2 = pa2 + s * pa3, s6 = s4 * s2;
            double Q2 = qa2 + s * qa3;
            double P3 = pa4 + s * pa5;
            double Q3 = qa4 + s * qa5;
            double P4 = pa6;
            double Q4 = qa6;
            P = P1 + s2 * P2 + s4 * P3 + s6 * P4;
            Q = Q1 + s2 * Q2 + s4 * Q3 + s6 * Q4;
            if (hx >= 0)
                return erx + P / Q;
            else
                return -erx - P / Q;
        }
        if (ix >= 0x40180000)
        {
            // inf>|x|>=6
            if (hx >= 0)
                return one - tiny;
            else
                return tiny - one;
        }
        x = Math.Abs(x);
        s = one / (x * x);
        if (ix < 0x4006DB6E)
        {
            // |x| < 1/0.35
            double R1 = ra0 + s * ra1, s2 = s * s;
            double S1 = one + s * sa1, s4 = s2 * s2;
            double R2 = ra2 + s * ra3, s6 = s4 * s2;
            double S2 = sa2 + s * sa3, s8 = s4 * s4;
            double R3 = ra4 + s * ra5;
            double S3 = sa4 + s * sa5;
            double R4 = ra6 + s * ra7;
            double S4 = sa6 + s * sa7;
            R = R1 + s2 * R2 + s4 * R3 + s6 * R4;
            S = S1 + s2 * S2 + s4 * S3 + s6 * S4 + s8 * sa8;
        }
        else
        {
            // |x| >= 1/0.35
            double R1 = rb0 + s * rb1, s2 = s * s;
            double S1 = one + s * sb1, s4 = s2 * s2;
            double R2 = rb2 + s * rb3, s6 = s4 * s2;
            double S2 = sb2 + s * sb3;
            double R3 = rb4 + s * rb5;
            double S3 = sb4 + s * sb5;
            double S4 = sb6 + s * sb7;
            R = R1 + s2 * R2 + s4 * R3 + s6 * rb6;
            S = S1 + s2 * S2 + s4 * S3 + s6 * S4;
        }
        // SET_LOW_WORD(z, 0)
        z = BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(x) & 0xffffffff00000000UL);
        r = Math.Exp(-z * z - 0.5625) * Math.Exp((z - x) * (z + x) + R / S);
        if (hx >= 0)
            return one - r / x;
        else
            return r / x - one;
    }

    // a * b + c, fused where __log1p_fma fuses it.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Log1pMulAdd(double a, double b, double c) =>
        System.Runtime.Intrinsics.X86.Fma.IsSupported ? Math.FusedMultiplyAdd(a, b, c) : a * b + c;

    // std::log1p(double)
    public static double Log1p(double x)
    {
        const double ln2_hi = 6.93147180369123816490e-01, ln2_lo = 1.90821492927058770002e-10,
                     two54 = 1.80143985094819840000e+16;
        const double Lp1 = 6.666666666666735130e-01, Lp2 = 3.999999999940941908e-01,
                     Lp3 = 2.857142874366239149e-01, Lp4 = 2.222219843214978396e-01,
                     Lp5 = 1.818357216161805012e-01, Lp6 = 1.531383769920937332e-01,
                     Lp7 = 1.479819860511658591e-01;
        const double zero = 0.0;

        double hfsq, f = 0, c = 0, s, z, R, u;
        int k, hx, hu = 0, ax;

        hx = HighWord(x);
        ax = hx & 0x7fffffff;

        k = 1;
        if (hx < 0x3FDA827A)
        {
            // x < 0.41422
            if (ax >= 0x3ff00000)
            {
                // x <= -1.0
                if (x == -1.0)
                    return -two54 / zero; // log1p(-1)=-inf
                else
                    return (x - x) / (x - x); // log1p(x<-1)=NaN
            }
            if (ax < 0x3e200000)
            {
                // |x| < 2**-29
                if (ax < 0x3c900000) // |x| < 2**-54
                    return x;
                else
                    return Log1pMulAdd(-(x * x), 0.5, x);
            }
            if (hx > 0 || hx <= unchecked((int)0xbfd2bec3))
            {
                // -0.2929<x<0.41422
                k = 0;
                f = x;
                hu = 1;
            }
        }
        else if (hx >= 0x7ff00000)
            return x + x;
        if (k != 0)
        {
            if (hx < 0x43400000)
            {
                u = 1.0 + x;
                hu = HighWord(u);
                k = (hu >> 20) - 1023;
                c = (k > 0) ? 1.0 - (u - x) : x - (u - 1.0); // correction term
                c /= u;
            }
            else
            {
                u = x;
                hu = HighWord(u);
                k = (hu >> 20) - 1023;
                c = 0;
            }
            hu &= 0x000fffff;
            if (hu < 0x6a09e)
                u = WithHighWord(u, hu | 0x3ff00000); // normalize u
            else
            {
                k += 1;
                u = WithHighWord(u, hu | 0x3fe00000); // normalize u/2
                hu = (0x00100000 - hu) >> 2;
            }
            f = u - 1.0;
        }
        hfsq = 0.5 * f * f;
        if (hu == 0)
        {
            // |f| < 2**-20
            if (f == zero)
            {
                if (k == 0)
                    return zero;
                else
                {
                    c = Log1pMulAdd(k, ln2_lo, c);
                    return Log1pMulAdd(k, ln2_hi, c);
                }
            }
            R = hfsq * Log1pMulAdd(-0.66666666666666666, f, 1.0);
            if (k == 0)
                return f - R;
            else
                return Log1pMulAdd(k, ln2_hi, -((R - Log1pMulAdd(k, ln2_lo, c)) - f));
        }
        s = f / (2.0 + f);
        z = s * s;
        double z2 = z * z;
        double R2 = Log1pMulAdd(z, Lp3, Lp2), z4 = z2 * z2;
        double R3 = Log1pMulAdd(z, Lp5, Lp4), z6 = z4 * z2;
        double R4 = Log1pMulAdd(z, Lp7, Lp6);
        // R = R1 + z2 * R2 + z4 * R3 + z6 * R4 with R1 = z * Lp1
        R = Log1pMulAdd(z6, R4, Log1pMulAdd(z4, R3, Log1pMulAdd(z, Lp1, z2 * R2)));
        if (k == 0)
            return f - (hfsq - s * (hfsq + R));
        else
            return Log1pMulAdd(k, ln2_hi, -((hfsq - (s * (hfsq + R) + Log1pMulAdd(k, ln2_lo, c))) - f));
    }
}

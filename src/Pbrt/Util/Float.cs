// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/float.h

namespace Pbrt;

public static partial class Util
{
    // Floating-point Constants
    public const Float Infinity = Float.PositiveInfinity;

    // std::numeric_limits<Float>::epsilon() * 0.5: 2^-24 for float, 2^-53 for
    // double. (Note: C#'s float.Epsilon is the smallest denormal, not the C++
    // epsilon.)
#if PBRT_FLOAT_AS_DOUBLE
    public const Float MachineEpsilon = 1.1102230246251565e-16;
#else
    public const Float MachineEpsilon = 5.96046448e-08f;
#endif

    // 0x1.fffffffffffffp-1 and 0x1.fffffep-1
    public const double DoubleOneMinusEpsilon = 0.99999999999999989;
    public const float FloatOneMinusEpsilon = 0.99999994f;
#if PBRT_FLOAT_AS_DOUBLE
    public const Float OneMinusEpsilon = DoubleOneMinusEpsilon;
#else
    public const Float OneMinusEpsilon = FloatOneMinusEpsilon;
#endif

    // C# has no hex float literals: 0x1p-32f and 0x1p-64 (exact powers of two).
    public const float OneOver2Pow32 = 1f / 4294967296f;
    public const double OneOver2Pow64 = 1.0 / 18446744073709551616.0;

    // std::numeric_limits<float>::min(): the smallest positive normal value.
    // (Note: C#'s float.MinValue is the most negative value.)
    public const float FloatMinNormal = 1.17549435e-38f;
    // std::numeric_limits<Float>::min()
#if PBRT_FLOAT_AS_DOUBLE
    public const Float FloatMin = 2.2250738585072014e-308;
#else
    public const Float FloatMin = FloatMinNormal;
#endif

    // Floating-point Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(float v) => float.IsNaN(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(double v) => double.IsNaN(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNaN(int v) => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInf(float v) => float.IsInfinity(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInf(double v) => double.IsInfinity(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInf(int v) => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(float v) => float.IsFinite(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(double v) => double.IsFinite(v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(int v) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FMA(float a, float b, float c) => MathF.FusedMultiplyAdd(a, b, c);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FMA(double a, double b, double c) => Math.FusedMultiplyAdd(a, b, c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FloatToBits(float f) => BitConverter.SingleToUInt32Bits(f);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BitsToFloat(uint ui) => BitConverter.UInt32BitsToSingle(ui);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Exponent(float v) => (int)(FloatToBits(v) >> 23) - 127;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Significand(float v) => (int)(FloatToBits(v) & ((1u << 23) - 1));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SignBit(float v) => FloatToBits(v) & 0x80000000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong FloatToBits(double f) => BitConverter.DoubleToUInt64Bits(f);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double BitsToFloat(ulong ui) => BitConverter.UInt64BitsToDouble(ui);

    // (.NET's BitIncrement()/BitDecrement() do what pbrt's do, including
    // for infinities and zeros of either sign.)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NextFloatUp(float v) => MathF.BitIncrement(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NextFloatDown(float v) => MathF.BitDecrement(v);

    // pbrt's gamma(n): bound on relative error of n floating-point operations.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float Gamma(int n) => (n * MachineEpsilon) / (1 - n * MachineEpsilon);

    public static Float AddRoundUp(Float a, Float b) => NextFloatUp(a + b);
    public static Float AddRoundDown(Float a, Float b) => NextFloatDown(a + b);

    public static Float SubRoundUp(Float a, Float b) => AddRoundUp(a, -b);
    public static Float SubRoundDown(Float a, Float b) => AddRoundDown(a, -b);

    public static Float MulRoundUp(Float a, Float b) => NextFloatUp(a * b);
    public static Float MulRoundDown(Float a, Float b) => NextFloatDown(a * b);

    public static Float DivRoundUp(Float a, Float b) => NextFloatUp(a / b);
    public static Float DivRoundDown(Float a, Float b) => NextFloatDown(a / b);

    public static Float SqrtRoundUp(Float a) => NextFloatUp(FMath.Sqrt(a));
    public static Float SqrtRoundDown(Float a) => Max(0, NextFloatDown(FMath.Sqrt(a)));

    public static Float FMARoundUp(Float a, Float b, Float c) => NextFloatUp(FMA(a, b, c));
    public static Float FMARoundDown(Float a, Float b, Float c) => NextFloatDown(FMA(a, b, c));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NextFloatUp(double v) => Math.BitIncrement(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NextFloatDown(double v) => Math.BitDecrement(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Exponent(double d) => (int)(FloatToBits(d) >> 52) - 1023;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Significand(double d) => FloatToBits(d) & ((1ul << 52) - 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SignBit(double v) => FloatToBits(v) & 0x8000000000000000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FlipSign(double a, double b) => BitsToFloat(FloatToBits(a) ^ SignBit(b));
}

// pbrt's Half is System.Half: its conversions from float (rounding to nearest
// even) and to float give the same results as pbrt's for all values except
// NaNs, whose payload bits can differ.

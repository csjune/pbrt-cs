// pbrt-cs: math functions on Float.
//
// pbrt calls std::sqrt, std::sin, ... on Float and C++ overloading picks the
// float or double version. C# has separate classes (MathF for float, Math for
// double), so code on Float calls FMath, which forwards to the one matching
// Float. Code that works on float whatever Float is (image, Ptex and file
// format code) keeps calling MathF.

namespace Pbrt;

public static class FMath
{
#if PBRT_FLOAT_AS_DOUBLE
    // float arguments would be widened to double silently, unlike C++, which
    // calls the float overload. Catch them at compile time: the caller
    // decides between MathF (pbrt passes a float) and a cast to Float.
    private const string FloatArg = "float argument to FMath; use MathF or cast to Float";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Abs(double x) => Math.Abs(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sqrt(double x) => Math.Sqrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Cos(double x) => Math.Cos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sin(double x) => Math.Sin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Floor(double x) => Math.Floor(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Ceiling(double x) => Math.Ceiling(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Exp(double x) => Math.Exp(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Atan(double x) => Math.Atan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Acos(double x) => Math.Acos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Asin(double x) => Math.Asin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Tan(double x) => Math.Tan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Log(double x) => Math.Log(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Log2(double x) => Math.Log2(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Round(double x) => Math.Round(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sinh(double x) => Math.Sinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Cosh(double x) => Math.Cosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Atanh(double x) => Math.Atanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double BitIncrement(double x) => Math.BitIncrement(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double BitDecrement(double x) => Math.BitDecrement(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Truncate(double x) => Math.Truncate(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Cbrt(double x) => Math.Cbrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Tanh(double x) => Math.Tanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Max(double x, double y) => Math.Max(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Min(double x, double y) => Math.Min(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Atan2(double x, double y) => Math.Atan2(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Pow(double x, double y) => Math.Pow(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CopySign(double x, double y) => Math.CopySign(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FusedMultiplyAdd(double x, double y, double z) => Math.FusedMultiplyAdd(x, y, z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Round(double x, MidpointRounding mode) => Math.Round(x, mode);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ScaleB(double x, int n) => Math.ScaleB(x, n);

    [Obsolete(FloatArg, true)]
    public static float Abs(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Sqrt(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Cos(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Sin(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Floor(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Ceiling(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Exp(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Atan(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Acos(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Asin(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Tan(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Log(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Log2(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Round(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Sinh(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Cosh(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Atanh(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float BitIncrement(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float BitDecrement(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Truncate(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Cbrt(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Tanh(float x) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Max(float x, float y) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Min(float x, float y) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Atan2(float x, float y) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Pow(float x, float y) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float CopySign(float x, float y) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float FusedMultiplyAdd(float x, float y, float z) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float Round(float x, MidpointRounding mode) => throw new NotSupportedException();
    [Obsolete(FloatArg, true)]
    public static float ScaleB(float x, int n) => throw new NotSupportedException();
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Abs(float x) => MathF.Abs(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqrt(float x) => MathF.Sqrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cos(float x) => MathF.Cos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sin(float x) => MathF.Sin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Floor(float x) => MathF.Floor(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Ceiling(float x) => MathF.Ceiling(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Exp(float x) => MathF.Exp(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan(float x) => MathF.Atan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Acos(float x) => MathF.Acos(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Asin(float x) => MathF.Asin(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tan(float x) => MathF.Tan(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Log(float x) => MathF.Log(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Log2(float x) => MathF.Log2(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Round(float x) => MathF.Round(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sinh(float x) => MathF.Sinh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cosh(float x) => MathF.Cosh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atanh(float x) => MathF.Atanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BitIncrement(float x) => MathF.BitIncrement(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BitDecrement(float x) => MathF.BitDecrement(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Truncate(float x) => MathF.Truncate(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cbrt(float x) => MathF.Cbrt(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tanh(float x) => MathF.Tanh(x);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float x, float y) => MathF.Max(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float x, float y) => MathF.Min(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan2(float x, float y) => MathF.Atan2(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CopySign(float x, float y) => MathF.CopySign(x, y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FusedMultiplyAdd(float x, float y, float z) => MathF.FusedMultiplyAdd(x, y, z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Round(float x, MidpointRounding mode) => MathF.Round(x, mode);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ScaleB(float x, int n) => MathF.ScaleB(x, n);
#endif
}

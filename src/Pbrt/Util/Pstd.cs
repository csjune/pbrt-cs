// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# equivalents for the parts of src/pbrt/util/pstd.h that need them.
// Most of pstd maps directly onto .NET (optional -> T?, span -> Span<T>,
// vector -> List<T>/T[]); fixed-size pstd::array<T, N> values become these
// inline-array structs, indexed like arrays and copied by value.

namespace Pbrt;

// The constructors assign elements through the indexer, which the nullable
// analysis doesn't see.
#pragma warning disable CS8618

// pstd::array<T, 2>
[InlineArray(2)]
public struct Array2<T>
{
    private T e0;

    public Array2(T a, T b)
    {
        this[0] = a;
        this[1] = b;
    }

    public readonly int Length => 2;
}

// pstd::array<T, 3>
[InlineArray(3)]
public struct Array3<T>
{
    private T e0;

    public Array3(T a, T b, T c)
    {
        this[0] = a;
        this[1] = b;
        this[2] = c;
    }

    public readonly int Length => 3;
}

// pstd::array<T, 4>
[InlineArray(4)]
public struct Array4<T>
{
    private T e0;

    public Array4(T a, T b, T c, T d)
    {
        this[0] = a;
        this[1] = b;
        this[2] = c;
        this[3] = d;
    }

    public readonly int Length => 4;
}

// pstd::complex<Float>
public readonly struct Complex : IEquatable<Complex>
{
    public readonly Float re, im;

    public Complex(Float re)
    {
        this.re = re;
        im = 0;
    }

    public Complex(Float re, Float im)
    {
        this.re = re;
        this.im = im;
    }

    public static implicit operator Complex(Float re) => new(re);

    public static Complex operator -(Complex z) => new(-z.re, -z.im);
    public static Complex operator +(Complex a, Complex z) => new(a.re + z.re, a.im + z.im);
    public static Complex operator -(Complex a, Complex z) => new(a.re - z.re, a.im - z.im);
    public static Complex operator *(Complex a, Complex z) => new(a.re * z.re - a.im * z.im, a.re * z.im + a.im * z.re);

    public static Complex operator /(Complex a, Complex z)
    {
        Float scale = 1 / (z.re * z.re + z.im * z.im);
        return new(scale * (a.re * z.re + a.im * z.im), scale * (a.im * z.re - a.re * z.im));
    }

    public static bool operator ==(Complex a, Complex b) => a.re == b.re && a.im == b.im;
    public static bool operator !=(Complex a, Complex b) => !(a == b);
    public bool Equals(Complex other) => this == other;
    public override bool Equals(object? obj) => obj is Complex c && this == c;
    public override int GetHashCode() => HashCode.Combine(re, im);

    public static Float Real(Complex z) => z.re;
    public static Float Imag(Complex z) => z.im;
    public static Float Norm(Complex z) => z.re * z.re + z.im * z.im;
    public static Float Abs(Complex z) => FMath.Sqrt(Norm(z));

    // pbrt's Sqr() of a complex value (the generic Sqr(v) = v * v).
    public static Complex Sqr(Complex z) => z * z;

    public static Complex Sqrt(Complex z)
    {
        Float n = Abs(z), t1 = FMath.Sqrt(.5f * (n + FMath.Abs(z.re))), t2 = .5f * z.im / t1;

        if (n == 0)
            return 0;

        if (z.re >= 0)
            return new(t1, t2);
        else
            return new(FMath.Abs(t2), FMath.CopySign(t1, z.im));
    }

    public override string ToString() => FormattableString.Invariant($"({re}, {im})");
}

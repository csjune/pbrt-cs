// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the Interval class and functions from src/pbrt/util/math.h.

namespace Pbrt;

// Interval Definition
public readonly struct Interval : IEquatable<Interval>
{
    // Interval Private Members
    private readonly Float low, high;

    // Interval Public Methods
    public Interval(Float v)
    {
        low = high = v;
    }

    public Interval(Float low, Float high)
    {
        this.low = Min(low, high);
        this.high = Max(low, high);
    }

    // Assigns bounds directly; callers guarantee low <= high.
    private Interval(Float low, Float high, bool _)
    {
        this.low = low;
        this.high = high;
    }

    public static Interval FromValueAndError(Float v, Float err)
    {
        if (err == 0)
            return new Interval(v);
        return new Interval(SubRoundDown(v, err), AddRoundUp(v, err), false);
    }

#if PBRT_FLOAT_AS_DOUBLE
    public static readonly Interval Pi = new(3.1415926535897931, 3.1415926535897936);
#else
    public static readonly Interval Pi = new(3.1415925f, 3.14159274f);
#endif

    public Float UpperBound => high;
    public Float LowerBound => low;
    public Float Midpoint => (low + high) / 2;
    public Float Width => high - low;

    public Float this[int i]
    {
        get
        {
            DCHECK(i == 0 || i == 1);
            return (i == 0) ? low : high;
        }
    }

    public static explicit operator Float(Interval i) => i.Midpoint;

    public bool Exactly(Float v) => low == v && high == v;

    // Note the asymmetry inherited from pbrt: == tests for an exact value,
    // while != tests whether v lies outside of the interval.
    public static bool operator ==(Interval i, Float v) => i.Exactly(v);
    public static bool operator !=(Interval i, Float f) => f < i.low || f > i.high;

    public static bool operator ==(Interval a, Interval b) => a.low == b.low && a.high == b.high;
    public static bool operator !=(Interval a, Interval b) => !(a == b);

    public bool Equals(Interval other) => this == other;
    public override bool Equals(object? obj) => obj is Interval other && this == other;
    public override int GetHashCode() => HashCode.Combine(low, high);

    public static Interval operator -(Interval i) => new(-i.high, -i.low, false);

    public static Interval operator +(Interval a, Interval b) =>
        new(AddRoundDown(a.low, b.low), AddRoundUp(a.high, b.high));

    public static Interval operator -(Interval a, Interval b) =>
        new(SubRoundDown(a.low, b.high), SubRoundUp(a.high, b.low));

    public static Interval operator *(Interval a, Interval b)
    {
        Float lp0 = MulRoundDown(a.low, b.low), lp1 = MulRoundDown(a.high, b.low),
              lp2 = MulRoundDown(a.low, b.high), lp3 = MulRoundDown(a.high, b.high);
        Float hp0 = MulRoundUp(a.low, b.low), hp1 = MulRoundUp(a.high, b.low),
              hp2 = MulRoundUp(a.low, b.high), hp3 = MulRoundUp(a.high, b.high);
        return new(Min(Min(lp0, lp1), Min(lp2, lp3)),
                   Max(Max(hp0, hp1), Max(hp2, hp3)));
    }

    public static Interval operator /(Interval a, Interval b)
    {
        if (InRange(0, b))
            // The interval we're dividing by straddles zero, so just
            // return an interval of everything.
            return new Interval(-Infinity, Infinity);

        Float lq0 = DivRoundDown(a.low, b.low), lq1 = DivRoundDown(a.high, b.low),
              lq2 = DivRoundDown(a.low, b.high), lq3 = DivRoundDown(a.high, b.high);
        Float hq0 = DivRoundUp(a.low, b.low), hq1 = DivRoundUp(a.high, b.low),
              hq2 = DivRoundUp(a.low, b.high), hq3 = DivRoundUp(a.high, b.high);
        return new(Min(Min(lq0, lq1), Min(lq2, lq3)),
                   Max(Max(hq0, hq1), Max(hq2, hq3)));
    }

    public static Interval operator +(Float f, Interval i) => new Interval(f) + i;
    public static Interval operator -(Float f, Interval i) => new Interval(f) - i;

    public static Interval operator *(Float f, Interval i)
    {
        if (f > 0)
            return new Interval(MulRoundDown(f, i.low), MulRoundUp(f, i.high));
        else
            return new Interval(MulRoundDown(f, i.high), MulRoundUp(f, i.low));
    }

    public static Interval operator /(Float f, Interval i)
    {
        if (InRange(0, i))
            // The interval we're dividing by straddles zero, so just
            // return an interval of everything.
            return new Interval(-Infinity, Infinity);

        if (f > 0)
            return new Interval(DivRoundDown(f, i.high), DivRoundUp(f, i.low));
        else
            return new Interval(DivRoundDown(f, i.low), DivRoundUp(f, i.high));
    }

    public static Interval operator +(Interval i, Float f) => i + new Interval(f);
    public static Interval operator -(Interval i, Float f) => i - new Interval(f);

    // Also used for *=; matches pbrt's Interval::operator*=(Float).
    public static Interval operator *(Interval i, Float f)
    {
        if (f > 0)
            return new Interval(MulRoundDown(f, i.low), MulRoundUp(f, i.high));
        else
            return new Interval(MulRoundDown(f, i.high), MulRoundUp(f, i.low));
    }

    public static Interval operator /(Interval i, Float f)
    {
        if (f == 0)
            return new Interval(-Infinity, Infinity);

        if (f > 0)
            return new Interval(DivRoundDown(i.low, f), DivRoundUp(i.high, f));
        else
            return new Interval(DivRoundDown(i.high, f), DivRoundUp(i.low, f));
    }

    public override string ToString() => $"[ Interval [{low}, {high}] ]";
}

public static partial class Util
{
    // Interval Inline Functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InRange(Float v, Interval i) => v >= i.LowerBound && v <= i.UpperBound;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InRange(Interval a, Interval b) =>
        a.LowerBound <= b.UpperBound && a.UpperBound >= b.LowerBound;

    public static Interval Sqr(Interval i)
    {
        Float alow = FMath.Abs(i.LowerBound), ahigh = FMath.Abs(i.UpperBound);
        if (alow > ahigh)
            (alow, ahigh) = (ahigh, alow);
        if (InRange(0, i))
            return new Interval(0, MulRoundUp(ahigh, ahigh));
        return new Interval(MulRoundDown(alow, alow), MulRoundUp(ahigh, ahigh));
    }

    public static Float Floor(Interval i) => FMath.Floor(i.LowerBound);
    public static Float Ceil(Interval i) => FMath.Ceiling(i.UpperBound);

    public static Float Min(Interval a, Interval b) => Min(a.LowerBound, b.LowerBound);
    public static Float Max(Interval a, Interval b) => Max(a.UpperBound, b.UpperBound);

    public static Interval Sqrt(Interval i) =>
        new(SqrtRoundDown(i.LowerBound), SqrtRoundUp(i.UpperBound));

    public static Interval FMA(Interval a, Interval b, Interval c)
    {
        Float low = Min(
            Min(FMARoundDown(a.LowerBound, b.LowerBound, c.LowerBound),
                      FMARoundDown(a.UpperBound, b.LowerBound, c.LowerBound)),
            Min(FMARoundDown(a.LowerBound, b.UpperBound, c.LowerBound),
                      FMARoundDown(a.UpperBound, b.UpperBound, c.LowerBound)));
        Float high = Max(
            Max(FMARoundUp(a.LowerBound, b.LowerBound, c.UpperBound),
                      FMARoundUp(a.UpperBound, b.LowerBound, c.UpperBound)),
            Max(FMARoundUp(a.LowerBound, b.UpperBound, c.UpperBound),
                      FMARoundUp(a.UpperBound, b.UpperBound, c.UpperBound)));
        return new Interval(low, high);
    }

    public static Interval DifferenceOfProducts(Interval a, Interval b, Interval c, Interval d)
    {
        Span<Float> ab = [a.LowerBound * b.LowerBound, a.UpperBound * b.LowerBound,
                          a.LowerBound * b.UpperBound, a.UpperBound * b.UpperBound];
        Float abLow = Min(Min(ab[0], ab[1]), Min(ab[2], ab[3]));
        Float abHigh = Max(Max(ab[0], ab[1]), Max(ab[2], ab[3]));
        int abLowIndex = abLow == ab[0] ? 0 : (abLow == ab[1] ? 1 : (abLow == ab[2] ? 2 : 3));
        int abHighIndex =
            abHigh == ab[0] ? 0 : (abHigh == ab[1] ? 1 : (abHigh == ab[2] ? 2 : 3));

        Span<Float> cd = [c.LowerBound * d.LowerBound, c.UpperBound * d.LowerBound,
                          c.LowerBound * d.UpperBound, c.UpperBound * d.UpperBound];
        Float cdLow = Min(Min(cd[0], cd[1]), Min(cd[2], cd[3]));
        Float cdHigh = Max(Max(cd[0], cd[1]), Max(cd[2], cd[3]));
        int cdLowIndex = cdLow == cd[0] ? 0 : (cdLow == cd[1] ? 1 : (cdLow == cd[2] ? 2 : 3));
        int cdHighIndex =
            cdHigh == cd[0] ? 0 : (cdHigh == cd[1] ? 1 : (cdHigh == cd[2] ? 2 : 3));

        // Invert cd Indices since it's subtracted...
        Float low = DifferenceOfProducts(a[abLowIndex & 1], b[abLowIndex >> 1],
                                         c[cdHighIndex & 1], d[cdHighIndex >> 1]);
        Float high = DifferenceOfProducts(a[abHighIndex & 1], b[abHighIndex >> 1],
                                          c[cdLowIndex & 1], d[cdLowIndex >> 1]);
        DCHECK_LE(low, high);

        return new Interval(NextFloatDown(NextFloatDown(low)), NextFloatUp(NextFloatUp(high)));
    }

    public static Interval SumOfProducts(Interval a, Interval b, Interval c, Interval d) =>
        DifferenceOfProducts(a, b, -c, d);

    public static Interval MulPow2(Float s, Interval i) => MulPow2(i, s);

    public static Interval MulPow2(Interval i, Float s)
    {
        Float a = FMath.Abs(s);
        if (a < 1)
            DCHECK_EQ(1 / a, (Float)(1ul << Log2Int(1 / a)));
        else
            DCHECK_EQ(a, (Float)(1ul << Log2Int(a)));

        // Multiplication by powers of 2 is exaact
        return new Interval(Min(i.LowerBound * s, i.UpperBound * s),
                            Max(i.LowerBound * s, i.UpperBound * s));
    }

    public static Interval Abs(Interval i)
    {
        if (i.LowerBound >= 0)
            // The entire interval is greater than zero, so we're all set.
            return i;
        else if (i.UpperBound <= 0)
            // The entire interval is less than zero.
            return new Interval(-i.UpperBound, -i.LowerBound);
        else
            // The interval straddles zero.
            return new Interval(0, Max(-i.LowerBound, i.UpperBound));
    }

    public static Interval ACos(Interval i)
    {
        Float low = FMath.Acos(Min(1, i.UpperBound));
        Float high = FMath.Acos(Max(-1, i.LowerBound));

        return new Interval(Max(0, NextFloatDown(low)), NextFloatUp(high));
    }

    public static Interval Sin(Interval i)
    {
        CHECK_GE(i.LowerBound, -(Float)1e-16);
        CHECK_LE(i.UpperBound, (Float)2.0001 * Pi);
        Float low = FMath.Sin(Max(0, i.LowerBound));
        Float high = FMath.Sin(i.UpperBound);
        if (low > high)
            (low, high) = (high, low);
        low = Max(-1, NextFloatDown(low));
        high = Min(1, NextFloatUp(high));
        if (InRange(Pi / 2, i))
            high = 1;
        if (InRange((3f / 2f) * Pi, i))
            low = -1;

        return new Interval(low, high);
    }

    public static Interval Cos(Interval i)
    {
        CHECK_GE(i.LowerBound, -(Float)1e-16);
        CHECK_LE(i.UpperBound, (Float)2.0001 * Pi);
        Float low = FMath.Cos(Max(0, i.LowerBound));
        Float high = FMath.Cos(i.UpperBound);
        if (low > high)
            (low, high) = (high, low);
        low = Max(-1, NextFloatDown(low));
        high = Min(1, NextFloatUp(high));
        if (InRange(Pi, i))
            low = -1;

        return new Interval(low, high);
    }

    public static bool Quadratic(Interval a, Interval b, Interval c, out Interval t0, out Interval t1)
    {
        // Find quadratic discriminant
        Interval discrim = DifferenceOfProducts(b, b, MulPow2(4, a), c);
        if (discrim.LowerBound < 0)
        {
            t0 = t1 = default;
            return false;
        }
        Interval floatRootDiscrim = Sqrt(discrim);

        // Compute quadratic _t_ values
        Interval q;
        if ((Float)b < 0)
            q = MulPow2(-.5f, b - floatRootDiscrim);
        else
            q = MulPow2(-.5f, b + floatRootDiscrim);
        t0 = q / a;
        t1 = c / q;
        if (t0.LowerBound > t1.LowerBound)
            (t0, t1) = (t1, t0);
        return true;
    }

    // SumSquares(i0, i1, ...) = i0^2 + i1^2 + ..., evaluated right-to-left
    // with FMA as in pbrt's recursive template.
    public static Interval SumSquares(params ReadOnlySpan<Interval> args)
    {
        int n = args.Length;
        Interval ss = Sqr(args[n - 1]);
        for (int k = n - 2; k >= 0; --k)
        {
            ss = FMA(args[k], args[k], ss);
            ss = new Interval(Max(0, ss.LowerBound), ss.UpperBound);
        }
        return ss;
    }
}

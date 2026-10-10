// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

// PhysLight code contributed by Anders Langlands and Luca Fascione
// Copyright (c) 2020, Weta Digital, Ltd.
// SPDX-License-Identifier: Apache-2.0
//
// C# port of src/pbrt/util/spectrum.h and spectrum.cpp
//
// Spectrum is an abstract class (pbrt's TaggedPointer) and its function call
// operator s(lambda) is s.Evaluate(lambda). The spectral data tables are
// generated into SpectrumData.g.cs by tools/gen-tables.ps1. pbrt's
// Spectra::Init() becomes the Spectra static constructor.

using System.Diagnostics;
using System.Text;

namespace Pbrt;

public static partial class Util
{
    // Spectrum Constants
    public const Float Lambda_min = 360, Lambda_max = 830;

    public const int NSpectrumSamples = 4;

    public const Float CIE_Y_integral = (Float)106.856895;

    // Spectrum Function Declarations
    public static Float Blackbody(Float lambda, Float T)
    {
        if (T <= 0)
            return 0;
        const Float c = 299792458f;
        const Float h = 6.62606957e-34f;
        const Float kb = 1.3806488e-23f;
        // Return emitted radiance for blackbody at wavelength _lambda_
        Float l = lambda * 1e-9f;
        Float Le = (2 * h * c * c) / (Pow(l, 5) * (FastExp((float)((h * c) / (l * kb * T))) - 1));
        CHECK(!IsNaN(Le));
        return Le;
    }

    // Spectrum Function Definitions
    public static Float SpectrumToPhotometric(Spectrum s)
    {
        // We have to handle RGBIlluminantSpectrum separately here as it's composed of an
        // illuminant spectrum and an RGB multiplier. We only want to consider the
        // illuminant for the sake of this calculation, and we should consider the
        // RGB separately for the purposes of target power/illuminance computation
        // in the lights themselves (but we currently don't)
        if (s is RGBIlluminantSpectrum rgbIllum)
            s = rgbIllum.Illuminant()!;

        return InnerProduct(Spectra.Y(), s);
    }

    public static XYZ SpectrumToXYZ(Spectrum s) =>
        new XYZ(InnerProduct(Spectra.X(), s), InnerProduct(Spectra.Y(), s),
                InnerProduct(Spectra.Z(), s)) /
        CIE_Y_integral;

    // SampledSpectrum Inline Functions
    public static SampledSpectrum SafeDiv(SampledSpectrum a, SampledSpectrum b)
    {
        SampledSpectrum r = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            r[i] = (b[i] != 0) ? a[i] / b[i] : 0;
        return r;
    }

    public static SampledSpectrum Clamp(SampledSpectrum s, Float low, Float high)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = Clamp(s[i], low, high);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum ClampZero(SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = Max<Float>(0, s[i]);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum Sqrt(SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = FMath.Sqrt(s[i]);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum SafeSqrt(SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = SafeSqrt(s[i]);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum Pow(SampledSpectrum s, Float e)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = FMath.Pow(s[i], e);
        return ret;
    }

    public static SampledSpectrum Exp(SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = FMath.Exp(s[i]);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum FastExp(SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret[i] = FastExp((float)s[i]);
        DCHECK(!ret.HasNaNs());
        return ret;
    }

    public static SampledSpectrum Bilerp(Array2<Float> p, ReadOnlySpan<SampledSpectrum> v) =>
        ((1 - p[0]) * (1 - p[1]) * v[0] + p[0] * (1 - p[1]) * v[1] +
         (1 - p[0]) * p[1] * v[2] + p[0] * p[1] * v[3]);

    public static SampledSpectrum Lerp(Float t, SampledSpectrum s1, SampledSpectrum s2) =>
        (1 - t) * s1 + t * s2;

    // Spectrum Inline Functions
    public static Float InnerProduct(Spectrum f, Spectrum g)
    {
        Float integral = 0;
        for (Float lambda = Lambda_min; lambda <= Lambda_max; ++lambda)
            integral += f.Evaluate(lambda) * g.Evaluate(lambda);
        return integral;
    }

    // Spectral Function Definitions
    public static Spectrum? GetNamedSpectrum(string name) =>
        Spectra.namedSpectra.TryGetValue(name, out Spectrum? s) ? s : null;

    public static string FindMatchingNamedSpectrum(Spectrum s)
    {
        static bool sampledLambdasMatch(Spectrum a, Spectrum b)
        {
            ReadOnlySpan<Float> wls = [306, (Float)360.932007, 380, 402, 455, 503, 579,
                                       610, 660, 692, 702, 760, 800, 860];
            foreach (Float lambda in wls)
                if (a.Evaluate(lambda) != b.Evaluate(lambda))
                    return false;
            return true;
        }
        foreach (var spd in Spectra.namedSpectra)
        {
            if (sampledLambdasMatch(s, spd.Value))
                return spd.Key;
        }
        return "";
    }
}

// Spectrum Definition
public abstract class Spectrum
{
    // Spectrum Interface
    public abstract Float Evaluate(Float lambda);

    public abstract Float MaxValue();

    public abstract SampledSpectrum Sample(in SampledWavelengths lambda);
}

// SampledSpectrum Definition
//
// NOTE: like pbrt, default(SampledSpectrum) is zero everywhere.
public struct SampledSpectrum : IEquatable<SampledSpectrum>
{
    // SampledSpectrum Private Members
    // (Array4 because NSpectrumSamples is 4.)
    private Array4<Float> values;

    // SampledSpectrum Public Methods
    public static SampledSpectrum operator +(in SampledSpectrum a, in SampledSpectrum s)
    {
        SampledSpectrum ret = a;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] += s.values[i];
        return ret;
    }

    public static SampledSpectrum operator -(in SampledSpectrum a, in SampledSpectrum s)
    {
        SampledSpectrum ret = a;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] -= s.values[i];
        return ret;
    }

    public static SampledSpectrum operator -(Float a, in SampledSpectrum s)
    {
        DCHECK(!IsNaN(a));
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] = a - s.values[i];
        return ret;
    }

    public static SampledSpectrum operator *(in SampledSpectrum a, in SampledSpectrum s)
    {
        SampledSpectrum ret = a;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] *= s.values[i];
        return ret;
    }

    public static SampledSpectrum operator *(in SampledSpectrum s, Float a)
    {
        DCHECK(!IsNaN(a));
        SampledSpectrum ret = s;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] *= a;
        return ret;
    }

    public static SampledSpectrum operator *(Float a, in SampledSpectrum s) => s * a;

    public static SampledSpectrum operator /(in SampledSpectrum a, in SampledSpectrum s)
    {
        SampledSpectrum ret = a;
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            DCHECK_NE(0, s.values[i]);
            ret.values[i] /= s.values[i];
        }
        return ret;
    }

    public static SampledSpectrum operator /(in SampledSpectrum s, Float a)
    {
        DCHECK_NE(a, 0);
        DCHECK(!IsNaN(a));
        SampledSpectrum ret = s;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] /= a;
        return ret;
    }

    public static SampledSpectrum operator -(in SampledSpectrum s)
    {
        SampledSpectrum ret = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            ret.values[i] = -s.values[i];
        return ret;
    }

    public static bool operator ==(in SampledSpectrum a, in SampledSpectrum s)
    {
        for (int i = 0; i < NSpectrumSamples; ++i)
            if (a.values[i] != s.values[i])
                return false;
        return true;
    }

    public static bool operator !=(in SampledSpectrum a, in SampledSpectrum s) => !(a == s);

    public readonly bool Equals(SampledSpectrum other) => this == other;
    public override readonly bool Equals(object? obj) => obj is SampledSpectrum other && this == other;
    public override readonly int GetHashCode() => HashCode.Combine(values[0], values[1], values[2], values[3]);

    public override readonly string ToString()
    {
        var str = new StringBuilder("[ ");
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            str.Append(FormattableString.Invariant($"{values[i]}"));
            if (i + 1 < NSpectrumSamples)
                str.Append(", ");
        }
        str.Append(" ]");
        return str.ToString();
    }

    public readonly bool HasNaNs()
    {
        for (int i = 0; i < NSpectrumSamples; ++i)
            if (IsNaN(values[i]))
                return true;
        return false;
    }

    public readonly XYZ ToXYZ(in SampledWavelengths lambda)
    {
        // Sample the $X$, $Y$, and $Z$ matching curves at _lambda_
        SampledSpectrum X = Spectra.X().Sample(lambda);
        SampledSpectrum Y = Spectra.Y().Sample(lambda);
        SampledSpectrum Z = Spectra.Z().Sample(lambda);

        // Evaluate estimator to compute $(x,y,z)$ coefficients
        SampledSpectrum pdf = lambda.PDF();
        return new XYZ(SafeDiv(X * this, pdf).Average(), SafeDiv(Y * this, pdf).Average(),
                       SafeDiv(Z * this, pdf).Average()) /
               CIE_Y_integral;
    }

    public readonly RGB ToRGB(in SampledWavelengths lambda, RGBColorSpace cs)
    {
        XYZ xyz = ToXYZ(lambda);
        return cs.ToRGB(xyz);
    }

    public readonly Float y(in SampledWavelengths lambda)
    {
        SampledSpectrum Ys = Spectra.Y().Sample(lambda);
        SampledSpectrum pdf = lambda.PDF();
        return SafeDiv(Ys * this, pdf).Average() / CIE_Y_integral;
    }

    public SampledSpectrum(Float c)
    {
        for (int i = 0; i < NSpectrumSamples; ++i)
            values[i] = c;
    }

    public SampledSpectrum(ReadOnlySpan<Float> v)
    {
        DCHECK_EQ(NSpectrumSamples, v.Length);
        for (int i = 0; i < NSpectrumSamples; ++i)
            values[i] = v[i];
    }

    public Float this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            DCHECK(i >= 0 && i < NSpectrumSamples);
            return values[i];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            DCHECK(i >= 0 && i < NSpectrumSamples);
            values[i] = value;
        }
    }

    // pbrt's explicit operator bool: true if any value is nonzero. With
    // operator true/false, `if (s)` works as in C++.
    public readonly bool IsNonZero()
    {
        for (int i = 0; i < NSpectrumSamples; ++i)
            if (values[i] != 0)
                return true;
        return false;
    }

    public static explicit operator bool(in SampledSpectrum s) => s.IsNonZero();
    public static bool operator true(in SampledSpectrum s) => s.IsNonZero();
    public static bool operator false(in SampledSpectrum s) => !s.IsNonZero();
    public static bool operator !(in SampledSpectrum s) => !s.IsNonZero();

    public readonly Float MinComponentValue()
    {
        Float m = values[0];
        for (int i = 1; i < NSpectrumSamples; ++i)
            m = Min(m, values[i]);
        return m;
    }

    public readonly Float MaxComponentValue()
    {
        Float m = values[0];
        for (int i = 1; i < NSpectrumSamples; ++i)
            m = Max(m, values[i]);
        return m;
    }

    public readonly Float Average()
    {
        Float sum = values[0];
        for (int i = 1; i < NSpectrumSamples; ++i)
            sum += values[i];
        return sum / NSpectrumSamples;
    }
}

// SampledWavelengths Definitions
public struct SampledWavelengths : IEquatable<SampledWavelengths>
{
    // SampledWavelengths Private Members
    private Array4<Float> lambda, pdf;

    // SampledWavelengths Public Methods
    public static bool operator ==(in SampledWavelengths a, in SampledWavelengths swl)
    {
        for (int i = 0; i < NSpectrumSamples; ++i)
            if (a.lambda[i] != swl.lambda[i] || a.pdf[i] != swl.pdf[i])
                return false;
        return true;
    }

    public static bool operator !=(in SampledWavelengths a, in SampledWavelengths swl) => !(a == swl);

    public readonly bool Equals(SampledWavelengths other) => this == other;
    public override readonly bool Equals(object? obj) => obj is SampledWavelengths other && this == other;
    public override readonly int GetHashCode() =>
        HashCode.Combine(lambda[0], lambda[1], lambda[2], lambda[3], pdf[0], pdf[1], pdf[2], pdf[3]);

    public override readonly string ToString()
    {
        var r = new StringBuilder("[ SampledWavelengths lambda: [");
        for (int i = 0; i < NSpectrumSamples; ++i)
            r.Append(FormattableString.Invariant($" {lambda[i]}{(i != NSpectrumSamples - 1 ? ',' : ' ')}"));
        r.Append("] pdf: [");
        for (int i = 0; i < NSpectrumSamples; ++i)
            r.Append(FormattableString.Invariant($" {pdf[i]}{(i != NSpectrumSamples - 1 ? ',' : ' ')}"));
        r.Append("] ]");
        return r.ToString();
    }

    public static SampledWavelengths SampleUniform(Float u, Float lambda_min = Lambda_min,
                                                   Float lambda_max = Lambda_max)
    {
        SampledWavelengths swl = default;
        // Sample first wavelength using _u_
        swl.lambda[0] = Lerp(u, lambda_min, lambda_max);

        // Initialize _lambda_ for remaining wavelengths
        Float delta = (lambda_max - lambda_min) / NSpectrumSamples;
        for (int i = 1; i < NSpectrumSamples; ++i)
        {
            swl.lambda[i] = swl.lambda[i - 1] + delta;
            if (swl.lambda[i] > lambda_max)
                swl.lambda[i] = lambda_min + (swl.lambda[i] - lambda_max);
        }

        // Compute PDF for sampled wavelengths
        for (int i = 0; i < NSpectrumSamples; ++i)
            swl.pdf[i] = 1 / (lambda_max - lambda_min);

        return swl;
    }

    public Float this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => lambda[i];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => lambda[i] = value;
    }

    public readonly SampledSpectrum PDF() => new SampledSpectrum(pdf);

    public void TerminateSecondary()
    {
        if (SecondaryTerminated())
            return;
        // Update wavelength probabilities for termination
        for (int i = 1; i < NSpectrumSamples; ++i)
            pdf[i] = 0;
        pdf[0] /= NSpectrumSamples;
    }

    public readonly bool SecondaryTerminated()
    {
        for (int i = 1; i < NSpectrumSamples; ++i)
            if (pdf[i] != 0)
                return false;
        return true;
    }

    public static SampledWavelengths SampleVisible(Float u)
    {
        SampledWavelengths swl = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            // Compute _up_ for $i$th wavelength sample
            Float up = u + (Float)i / NSpectrumSamples;
            if (up > 1)
                up -= 1;

            swl.lambda[i] = SampleVisibleWavelengths(up);
            swl.pdf[i] = VisibleWavelengthsPDF(swl.lambda[i]);
        }
        return swl;
    }
}

// Spectrum Definitions
public sealed class ConstantSpectrum : Spectrum
{
    private readonly Float c;

    public ConstantSpectrum(Float c)
    {
        this.c = c;
    }

    public override Float Evaluate(Float lambda) => c;

    // ConstantSpectrum Public Methods
    public override SampledSpectrum Sample(in SampledWavelengths lambda) => new SampledSpectrum(c);

    public override Float MaxValue() => c;

    public override string ToString() => FormattableString.Invariant($"[ ConstantSpectrum c: {c} ]");
}

public sealed class DenselySampledSpectrum : Spectrum, IEquatable<DenselySampledSpectrum>
{
    // DenselySampledSpectrum Private Members
    private readonly int lambda_min, lambda_max;
    private readonly Float[] values;

    // DenselySampledSpectrum Public Methods
    public DenselySampledSpectrum(int lambda_min = (int)Lambda_min, int lambda_max = (int)Lambda_max)
    {
        this.lambda_min = lambda_min;
        this.lambda_max = lambda_max;
        values = new Float[lambda_max - lambda_min + 1];
    }

    public DenselySampledSpectrum(DenselySampledSpectrum s)
    {
        lambda_min = s.lambda_min;
        lambda_max = s.lambda_max;
        values = (Float[])s.values.Clone();
    }

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
        {
            int offset = LRound(lambda[i]) - lambda_min;
            if (offset < 0 || offset >= values.Length)
                s[i] = 0;
            else
                s[i] = values[offset];
        }
        return s;
    }

    public void Scale(Float s)
    {
        for (int i = 0; i < values.Length; ++i)
            values[i] *= s;
    }

    public override Float MaxValue() => values.Max();

    public override string ToString()
    {
        var s = new StringBuilder(
            $"[ DenselySampledSpectrum lambda_min: {lambda_min} lambda_max: {lambda_max} values: [ ");
        for (int i = 0; i < values.Length; ++i)
            s.Append(FormattableString.Invariant($"{values[i]} "));
        s.Append("] ]");
        return s.ToString();
    }

    public DenselySampledSpectrum(Spectrum? spec, int lambda_min = (int)Lambda_min,
                                  int lambda_max = (int)Lambda_max)
    {
        this.lambda_min = lambda_min;
        this.lambda_max = lambda_max;
        CHECK_GE(lambda_max, lambda_min);
        values = new Float[lambda_max - lambda_min + 1];
        if (spec != null)
            for (int lambda = lambda_min; lambda <= lambda_max; ++lambda)
                values[lambda - lambda_min] = spec.Evaluate(lambda);
    }

    public static DenselySampledSpectrum SampleFunction(Func<Float, Float> func,
                                                        int lambda_min = (int)Lambda_min,
                                                        int lambda_max = (int)Lambda_max)
    {
        var s = new DenselySampledSpectrum(lambda_min, lambda_max);
        for (int lambda = lambda_min; lambda <= lambda_max; ++lambda)
            s.values[lambda - lambda_min] = func(lambda);
        return s;
    }

    public override Float Evaluate(Float lambda)
    {
        DCHECK_GT(lambda, 0);
        int offset = LRound(lambda) - lambda_min;
        if (offset < 0 || offset >= values.Length)
            return 0;
        return values[offset];
    }

    // std::lround(): rounds halfway cases away from zero.
    private static int LRound(Float v) => (int)FMath.Round(v, MidpointRounding.AwayFromZero);

    public bool Equals(DenselySampledSpectrum? d)
    {
        if (d is null)
            return false;
        if (lambda_min != d.lambda_min || lambda_max != d.lambda_max ||
            values.Length != d.values.Length)
            return false;
        for (int i = 0; i < values.Length; ++i)
            if (values[i] != d.values[i])
                return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DenselySampledSpectrum);

    public static bool operator ==(DenselySampledSpectrum? a, DenselySampledSpectrum? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(DenselySampledSpectrum? a, DenselySampledSpectrum? b) => !(a == b);

    // std::hash<DenselySampledSpectrum>
    public override int GetHashCode() => (int)HashBuffer<Float>(values);
}

public sealed class PiecewiseLinearSpectrum : Spectrum
{
    // PiecewiseLinearSpectrum Private Members
    private readonly Float[] lambdas, values;

    // PiecewiseLinearSpectrum Public Methods
    public PiecewiseLinearSpectrum()
    {
        lambdas = [];
        values = [];
    }

    public void Scale(Float s)
    {
        for (int i = 0; i < values.Length; ++i)
            values[i] *= s;
    }

    public override Float MaxValue()
    {
        if (values.Length == 0)
            return 0;
        return values.Max();
    }

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            s[i] = Evaluate(lambda[i]);
        return s;
    }

    public override Float Evaluate(Float lambda)
    {
        // Handle _PiecewiseLinearSpectrum_ corner cases
        if (lambdas.Length == 0 || lambda < lambdas[0] || lambda > lambdas[^1])
            return 0;

        // Find offset to largest _lambdas_ below _lambda_ and interpolate
        int o = FindInterval(lambdas, lambda);
        DCHECK(lambda >= lambdas[o] && lambda <= lambdas[o + 1]);
        Float t = (lambda - lambdas[o]) / (lambdas[o + 1] - lambdas[o]);
        return Lerp(t, values[o], values[o + 1]);
    }

    public override string ToString()
    {
        string name = FindMatchingNamedSpectrum(this);
        if (name.Length != 0)
            return $"\"{name}\"";

        var ret = new StringBuilder("[ PiecewiseLinearSpectrum ");
        for (int i = 0; i < lambdas.Length; ++i)
            ret.Append(FormattableString.Invariant($"{lambdas[i]} {values[i]} "));
        return ret.Append(" ]").ToString();
    }

    public PiecewiseLinearSpectrum(ReadOnlySpan<Float> l, ReadOnlySpan<Float> v)
    {
        lambdas = l.ToArray();
        values = v.ToArray();
        CHECK_EQ(lambdas.Length, values.Length);
        for (int i = 0; i + 1 < lambdas.Length; ++i)
            CHECK_LT(lambdas[i], lambdas[i + 1]);
    }

    public static Spectrum? Read(string fn)
    {
        List<Float> vals = ReadFloatFile(fn);
        if (vals.Count == 0)
        {
            Warning($"{fn}: unable to read spectrum file.");
            return null;
        }
        else
        {
            if (vals.Count % 2 != 0)
            {
                Warning($"{fn}: extra value found in spectrum file.");
                return null;
            }
            var lambda = new List<Float>();
            var v = new List<Float>();
            for (int i = 0; i < vals.Count / 2; ++i)
            {
                if (i > 0 && vals[2 * i] <= lambda[^1])
                {
                    Warning(FormattableString.Invariant(
                        $"{fn}: spectrum file invalid: at {i}'th entry, wavelengths aren't increasing: {lambda[^1]} >= {vals[2 * i]}."));
                    return null;
                }
                lambda.Add(vals[2 * i]);
                v.Add(vals[2 * i + 1]);
            }
            return new PiecewiseLinearSpectrum(lambda.ToArray(), v.ToArray());
        }
    }

    public static PiecewiseLinearSpectrum FromInterleaved(ReadOnlySpan<Float> samples, bool normalize)
    {
        CHECK_EQ(0, samples.Length % 2);
        int n = samples.Length / 2;
        var lambda = new List<Float>();
        var v = new List<Float>();

        // Extend samples to cover range of visible wavelengths if needed.
        if (samples[0] > Lambda_min)
        {
            lambda.Add(Lambda_min - 1);
            v.Add(samples[1]);
        }
        for (int i = 0; i < n; ++i)
        {
            lambda.Add(samples[2 * i]);
            v.Add(samples[2 * i + 1]);
            if (i > 0)
                CHECK_GT(lambda[^1], lambda[^2]);
        }
        if (lambda[^1] < Lambda_max)
        {
            lambda.Add(Lambda_max + 1);
            v.Add(v[^1]);
        }

        var spec = new PiecewiseLinearSpectrum(lambda.ToArray(), v.ToArray());

        if (normalize)
            // Normalize to have luminance of 1.
            spec.Scale(CIE_Y_integral / InnerProduct(spec, Spectra.Y()));

        return spec;
    }
}

public sealed class BlackbodySpectrum : Spectrum
{
    // BlackbodySpectrum Private Members
    private readonly Float T;
    private readonly Float normalizationFactor;

    // BlackbodySpectrum Public Methods
    public BlackbodySpectrum(Float T)
    {
        this.T = T;
        // Compute blackbody normalization constant for given temperature
        Float lambdaMax = 2.8977721e-3f / T;
        normalizationFactor = 1 / Blackbody(lambdaMax * 1e9f, T);
    }

    public override Float Evaluate(Float lambda) => Blackbody(lambda, T) * normalizationFactor;

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            s[i] = Blackbody(lambda[i], T) * normalizationFactor;
        return s;
    }

    public override Float MaxValue() => 1f;

    public override string ToString() => FormattableString.Invariant($"[ BlackbodySpectrum T: {T} ]");
}

public sealed class RGBAlbedoSpectrum : Spectrum
{
    // RGBAlbedoSpectrum Private Members
    private readonly RGBSigmoidPolynomial rsp;

    // RGBAlbedoSpectrum Public Methods
    public override Float Evaluate(Float lambda) => rsp.Evaluate(lambda);
    public override Float MaxValue() => rsp.MaxValue();

    public RGBAlbedoSpectrum(RGBColorSpace cs, RGB rgb)
    {
        DCHECK_LE(Max(rgb.r, Max(rgb.g, rgb.b)), 1);
        DCHECK_GE(Min(rgb.r, Min(rgb.g, rgb.b)), 0);
        rsp = cs.ToRGBCoeffs(rgb);
    }

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            s[i] = rsp.Evaluate(lambda[i]);
        return s;
    }

    public override string ToString() => $"[ RGBAlbedoSpectrum rsp: {rsp} ]";
}

public sealed class RGBUnboundedSpectrum : Spectrum
{
    // RGBUnboundedSpectrum Private Members
    private readonly Float scale = 1;
    private readonly RGBSigmoidPolynomial rsp;

    // RGBUnboundedSpectrum Public Methods
    public override Float Evaluate(Float lambda) => scale * rsp.Evaluate(lambda);
    public override Float MaxValue() => scale * rsp.MaxValue();

    public RGBUnboundedSpectrum(RGBColorSpace cs, RGB rgb)
    {
        Float m = Max(rgb.r, Max(rgb.g, rgb.b));
        scale = 2 * m;
        rsp = cs.ToRGBCoeffs(scale != 0 ? rgb / scale : new RGB(0, 0, 0));
    }

    public RGBUnboundedSpectrum()
    {
        rsp = new RGBSigmoidPolynomial(0, 0, 0);
        scale = 0;
    }

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            s[i] = scale * rsp.Evaluate(lambda[i]);
        return s;
    }

    public override string ToString() => $"[ RGBUnboundedSpectrum rsp: {rsp} ]";
}

public sealed class RGBIlluminantSpectrum : Spectrum
{
    // RGBIlluminantSpectrum Private Members
    private readonly Float scale;
    private readonly RGBSigmoidPolynomial rsp;
    private readonly DenselySampledSpectrum? illuminant;

    // RGBIlluminantSpectrum Public Methods
    public RGBIlluminantSpectrum() { }

    public RGBIlluminantSpectrum(RGBColorSpace cs, RGB rgb)
    {
        illuminant = cs.illuminant;
        Float m = Max(rgb.r, Max(rgb.g, rgb.b));
        scale = 2 * m;
        rsp = cs.ToRGBCoeffs(scale != 0 ? rgb / scale : new RGB(0, 0, 0));
    }

    public override Float Evaluate(Float lambda)
    {
        if (illuminant is null)
            return 0;
        return scale * rsp.Evaluate(lambda) * illuminant.Evaluate(lambda);
    }

    public override Float MaxValue()
    {
        if (illuminant is null)
            return 0;
        return scale * rsp.MaxValue() * illuminant.MaxValue();
    }

    public DenselySampledSpectrum? Illuminant() => illuminant;

    public override SampledSpectrum Sample(in SampledWavelengths lambda)
    {
        if (illuminant is null)
            return new SampledSpectrum(0);
        SampledSpectrum s = default;
        for (int i = 0; i < NSpectrumSamples; ++i)
            s[i] = scale * rsp.Evaluate(lambda[i]);
        return s * illuminant.Sample(lambda);
    }

    public override string ToString() =>
        FormattableString.Invariant(
            $"[ RGBIlluminantSpectrum: rsp: {rsp} scale: {scale} illuminant: {illuminant?.ToString() ?? "(nullptr)"} ]");
}

// Spectral Data Definitions
public static partial class Spectra
{
    public static DenselySampledSpectrum D(Float temperature)
    {
        // Convert temperature to CCT
        Float cct = temperature * 1.4388f / 1.4380f;
        if (cct < 4000)
        {
            // CIE D ill-defined, use blackbody
            BlackbodySpectrum bb = new BlackbodySpectrum(cct);
            DenselySampledSpectrum blackbody = DenselySampledSpectrum.SampleFunction(
                lambda => bb.Evaluate(lambda));

            return blackbody;
        }

        // Convert CCT to xy
        Float x;
        if (cct <= 7000)
            x = -4.607f * 1e9f / Pow(cct, 3) + 2.9678f * 1e6f / Sqr(cct) +
                0.09911f * 1e3f / cct + 0.244063f;
        else
            x = -2.0064f * 1e9f / Pow(cct, 3) + 1.9018f * 1e6f / Sqr(cct) +
                0.24748f * 1e3f / cct + 0.23704f;
        Float y = -3 * x * x + 2.870f * x - 0.275f;

        // Interpolate D spectrum
        Float M = 0.0241f + 0.2562f * x - 0.7341f * y;
        Float M1 = (-1.3515f - 1.7703f * x + 5.9114f * y) / M;
        Float M2 = (0.0300f - 31.4424f * x + 30.0717f * y) / M;

        Float[] values = new Float[nCIES];
        for (int i = 0; i < nCIES; ++i)
            values[i] = (Float)((CIE_S0[i] + CIE_S1[i] * M1 + CIE_S2[i] * M2) * 0.01);

        PiecewiseLinearSpectrum dpls = new PiecewiseLinearSpectrum(CIE_S_lambda, values);
        return new DenselySampledSpectrum(dpls);
    }

    private static readonly DenselySampledSpectrum x, y, z;

    // std::map in pbrt: FindMatchingNamedSpectrum() returns the first match in
    // key order.
    internal static readonly SortedDictionary<string, Spectrum> namedSpectra = new(StringComparer.Ordinal);

    public static DenselySampledSpectrum X() => x;
    public static DenselySampledSpectrum Y() => y;
    public static DenselySampledSpectrum Z() => z;

    // Spectra::Init()
    static Spectra()
    {
        PiecewiseLinearSpectrum xpls = new(CIE_lambda, CIE_X);
        x = new DenselySampledSpectrum(xpls);

        PiecewiseLinearSpectrum ypls = new(CIE_lambda, CIE_Y);
        y = new DenselySampledSpectrum(ypls);

        PiecewiseLinearSpectrum zpls = new(CIE_lambda, CIE_Z);
        z = new DenselySampledSpectrum(zpls);

        Spectrum illuma = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_A, true);
        Spectrum illumd50 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_D5000, true);
        Spectrum illumacesd60 = PiecewiseLinearSpectrum.FromInterleaved(ACES_Illum_D60, true);
        Spectrum illumd65 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_D6500, true);
        Spectrum illumf1 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F1, true);
        Spectrum illumf2 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F2, true);
        Spectrum illumf3 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F3, true);
        Spectrum illumf4 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F4, true);
        Spectrum illumf5 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F5, true);
        Spectrum illumf6 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F6, true);
        Spectrum illumf7 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F7, true);
        Spectrum illumf8 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F8, true);
        Spectrum illumf9 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F9, true);
        Spectrum illumf10 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F10, true);
        Spectrum illumf11 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F11, true);
        Spectrum illumf12 = PiecewiseLinearSpectrum.FromInterleaved(CIE_Illum_F12, true);

        Spectrum ageta = PiecewiseLinearSpectrum.FromInterleaved(Ag_eta, false);
        Spectrum agk = PiecewiseLinearSpectrum.FromInterleaved(Ag_k, false);
        Spectrum aleta = PiecewiseLinearSpectrum.FromInterleaved(Al_eta, false);
        Spectrum alk = PiecewiseLinearSpectrum.FromInterleaved(Al_k, false);
        Spectrum aueta = PiecewiseLinearSpectrum.FromInterleaved(Au_eta, false);
        Spectrum auk = PiecewiseLinearSpectrum.FromInterleaved(Au_k, false);
        Spectrum cueta = PiecewiseLinearSpectrum.FromInterleaved(Cu_eta, false);
        Spectrum cuk = PiecewiseLinearSpectrum.FromInterleaved(Cu_k, false);
        Spectrum cuzneta = PiecewiseLinearSpectrum.FromInterleaved(CuZn_eta, false);
        Spectrum cuznk = PiecewiseLinearSpectrum.FromInterleaved(CuZn_k, false);
        Spectrum mgoeta = PiecewiseLinearSpectrum.FromInterleaved(MgO_eta, false);
        Spectrum mgok = PiecewiseLinearSpectrum.FromInterleaved(MgO_k, false);
        Spectrum tio2eta = PiecewiseLinearSpectrum.FromInterleaved(TiO2_eta, false);
        Spectrum tio2k = PiecewiseLinearSpectrum.FromInterleaved(TiO2_k, false);
        Spectrum glassbk7eta = PiecewiseLinearSpectrum.FromInterleaved(GlassBK7_eta, false);
        Spectrum glassbaf10eta = PiecewiseLinearSpectrum.FromInterleaved(GlassBAF10_eta, false);
        Spectrum glassfk51aeta = PiecewiseLinearSpectrum.FromInterleaved(GlassFK51A_eta, false);
        Spectrum glasslasf9eta = PiecewiseLinearSpectrum.FromInterleaved(GlassLASF9_eta, false);
        Spectrum glasssf5eta = PiecewiseLinearSpectrum.FromInterleaved(GlassSF5_eta, false);
        Spectrum glasssf10eta = PiecewiseLinearSpectrum.FromInterleaved(GlassSF10_eta, false);
        Spectrum glasssf11eta = PiecewiseLinearSpectrum.FromInterleaved(GlassSF11_eta, false);

        namedSpectra.Add("glass-BK7", glassbk7eta);
        namedSpectra.Add("glass-BAF10", glassbaf10eta);
        namedSpectra.Add("glass-FK51A", glassfk51aeta);
        namedSpectra.Add("glass-LASF9", glasslasf9eta);
        namedSpectra.Add("glass-F5", glasssf5eta);
        namedSpectra.Add("glass-F10", glasssf10eta);
        namedSpectra.Add("glass-F11", glasssf11eta);

        namedSpectra.Add("metal-Ag-eta", ageta);
        namedSpectra.Add("metal-Ag-k", agk);
        namedSpectra.Add("metal-Al-eta", aleta);
        namedSpectra.Add("metal-Al-k", alk);
        namedSpectra.Add("metal-Au-eta", aueta);
        namedSpectra.Add("metal-Au-k", auk);
        namedSpectra.Add("metal-Cu-eta", cueta);
        namedSpectra.Add("metal-Cu-k", cuk);
        namedSpectra.Add("metal-CuZn-eta", cuzneta);
        namedSpectra.Add("metal-CuZn-k", cuznk);
        namedSpectra.Add("metal-MgO-eta", mgoeta);
        namedSpectra.Add("metal-MgO-k", mgok);
        namedSpectra.Add("metal-TiO2-eta", tio2eta);
        namedSpectra.Add("metal-TiO2-k", tio2k);

        namedSpectra.Add("stdillum-A", illuma);
        namedSpectra.Add("stdillum-D50", illumd50);
        namedSpectra.Add("stdillum-D65", illumd65);
        namedSpectra.Add("stdillum-F1", illumf1);
        namedSpectra.Add("stdillum-F2", illumf2);
        namedSpectra.Add("stdillum-F3", illumf3);
        namedSpectra.Add("stdillum-F4", illumf4);
        namedSpectra.Add("stdillum-F5", illumf5);
        namedSpectra.Add("stdillum-F6", illumf6);
        namedSpectra.Add("stdillum-F7", illumf7);
        namedSpectra.Add("stdillum-F8", illumf8);
        namedSpectra.Add("stdillum-F9", illumf9);
        namedSpectra.Add("stdillum-F10", illumf10);
        namedSpectra.Add("stdillum-F11", illumf11);
        namedSpectra.Add("stdillum-F12", illumf12);

        namedSpectra.Add("illum-acesD60", illumacesd60);

        // Camera sensor response curves: (name, table) pairs.
        void addCamera(string name, ReadOnlySpan<Float> r, ReadOnlySpan<Float> g, ReadOnlySpan<Float> b)
        {
            namedSpectra.Add(name + "_r", PiecewiseLinearSpectrum.FromInterleaved(r, false));
            namedSpectra.Add(name + "_g", PiecewiseLinearSpectrum.FromInterleaved(g, false));
            namedSpectra.Add(name + "_b", PiecewiseLinearSpectrum.FromInterleaved(b, false));
        }
        addCamera("canon_eos_100d", canon_eos_100d_r, canon_eos_100d_g, canon_eos_100d_b);
        addCamera("canon_eos_1dx_mkii", canon_eos_1dx_mkii_r, canon_eos_1dx_mkii_g, canon_eos_1dx_mkii_b);
        addCamera("canon_eos_200d", canon_eos_200d_r, canon_eos_200d_g, canon_eos_200d_b);
        addCamera("canon_eos_200d_mkii", canon_eos_200d_mkii_r, canon_eos_200d_mkii_g, canon_eos_200d_mkii_b);
        addCamera("canon_eos_5d", canon_eos_5d_r, canon_eos_5d_g, canon_eos_5d_b);
        addCamera("canon_eos_5d_mkii", canon_eos_5d_mkii_r, canon_eos_5d_mkii_g, canon_eos_5d_mkii_b);
        addCamera("canon_eos_5d_mkiii", canon_eos_5d_mkiii_r, canon_eos_5d_mkiii_g, canon_eos_5d_mkiii_b);
        addCamera("canon_eos_5d_mkiv", canon_eos_5d_mkiv_r, canon_eos_5d_mkiv_g, canon_eos_5d_mkiv_b);
        addCamera("canon_eos_5ds", canon_eos_5ds_r, canon_eos_5ds_g, canon_eos_5ds_b);
        addCamera("canon_eos_m", canon_eos_m_r, canon_eos_m_g, canon_eos_m_b);
        addCamera("hasselblad_l1d_20c", hasselblad_l1d_20c_r, hasselblad_l1d_20c_g, hasselblad_l1d_20c_b);
        addCamera("nikon_d810", nikon_d810_r, nikon_d810_g, nikon_d810_b);
        addCamera("nikon_d850", nikon_d850_r, nikon_d850_g, nikon_d850_b);
        addCamera("sony_ilce_6400", sony_ilce_6400_r, sony_ilce_6400_g, sony_ilce_6400_b);
        addCamera("sony_ilce_7m3", sony_ilce_7m3_r, sony_ilce_7m3_g, sony_ilce_7m3_b);
        addCamera("sony_ilce_7rm3", sony_ilce_7rm3_r, sony_ilce_7rm3_g, sony_ilce_7rm3_b);
        addCamera("sony_ilce_9", sony_ilce_9_r, sony_ilce_9_g, sony_ilce_9_b);
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the sampling distribution classes in src/pbrt/util/sampling.h
// and sampling.cpp: VarianceEstimator, WeightedReservoirSampler,
// PiecewiseConstant1D/2D, AliasTable, SummedAreaTable and
// WindowedPiecewiseConstant2D.

using System.Numerics;

namespace Pbrt;

// VarianceEstimator Definition
public struct VarianceEstimator<T> where T : struct, IFloatingPoint<T>
{
    // VarianceEstimator Private Members
    private T mean, S;
    private long n;

    // VarianceEstimator Public Methods
    public void Add(T x)
    {
        ++n;
        T delta = x - mean;
        mean += delta / T.CreateTruncating(n);
        T delta2 = x - mean;
        S += delta * delta2;
    }

    public readonly T Mean() => mean;
    public readonly T Variance() => (n > 1) ? S / T.CreateTruncating(n - 1) : T.Zero;
    public readonly long Count() => n;
    public readonly T RelativeVariance() => (n < 1 || mean == T.Zero) ? T.Zero : Variance() / Mean();

    public void Merge(in VarianceEstimator<T> ve)
    {
        if (ve.n == 0)
            return;
        S = S + ve.S + Util.Sqr(ve.mean - mean) * T.CreateTruncating(n) * T.CreateTruncating(ve.n) /
                           T.CreateTruncating(n + ve.n);
        mean = (T.CreateTruncating(n) * mean + T.CreateTruncating(ve.n) * ve.mean) / T.CreateTruncating(n + ve.n);
        n += ve.n;
    }
}

// WeightedReservoirSampler Definition
public struct WeightedReservoirSampler<T>
{
    // WeightedReservoirSampler Private Members
    private RNG rng;
    private Float weightSum;
    private Float reservoirWeight;
    private T reservoir;

    // WeightedReservoirSampler Public Methods
    public WeightedReservoirSampler()
    {
        rng = new RNG();
        weightSum = reservoirWeight = 0;
        reservoir = default!;
    }

    public WeightedReservoirSampler(ulong rngSeed)
    {
        rng = new RNG(rngSeed);
        weightSum = reservoirWeight = 0;
        reservoir = default!;
    }

    public void Seed(ulong seed) => rng.SetSequence(seed);

    public bool Add(T sample, Float weight)
    {
        weightSum += weight;
        // Randomly add _sample_ to reservoir
        Float p = weight / weightSum;
        if (rng.Uniform<Float>() < p)
        {
            reservoir = sample;
            reservoirWeight = weight;
            return true;
        }
        DCHECK_LT((double)weightSum, 1e80);
        return false;
    }

    public bool Add(Func<T> func, Float weight)
    {
        // Process weighted reservoir sample via callback
        weightSum += weight;
        Float p = weight / weightSum;
        if (rng.Uniform<Float>() < p)
        {
            reservoir = func();
            reservoirWeight = weight;
            return true;
        }
        DCHECK_LT((double)weightSum, 1e80);
        return false;
    }

    public void Copy(in WeightedReservoirSampler<T> wrs)
    {
        weightSum = wrs.weightSum;
        reservoir = wrs.reservoir;
        reservoirWeight = wrs.reservoirWeight;
    }

    public readonly bool HasSample() => weightSum > 0;
    public readonly T GetSample() => reservoir;
    public readonly Float SampleProbability() => reservoirWeight / weightSum;
    public readonly Float WeightSum() => weightSum;

    public void Reset() => reservoirWeight = weightSum = 0;

    public void Merge(in WeightedReservoirSampler<T> wrs)
    {
        DCHECK_LE((double)(weightSum + wrs.WeightSum()), 1e80);
        if (wrs.HasSample() && Add(wrs.reservoir, wrs.weightSum))
            reservoirWeight = wrs.reservoirWeight;
    }

    public override readonly string ToString() =>
        FormattableString.Invariant(
            $"[ WeightedReservoirSampler rng: {rng} weightSum: {weightSum} reservoir: {reservoir} reservoirWeight: {reservoirWeight} ]");
}

// PiecewiseConstant1D Definition
public sealed class PiecewiseConstant1D
{
    // PiecewiseConstant1D Public Members
    public readonly Float[] func, cdf;
    public readonly Float min, max;
    public readonly Float funcInt = 0;

    // PiecewiseConstant1D Public Methods
    public PiecewiseConstant1D(ReadOnlySpan<Float> f) : this(f, 0, 1) { }

    public PiecewiseConstant1D(ReadOnlySpan<Float> f, Float min, Float max)
    {
        func = f.ToArray();
        cdf = new Float[f.Length + 1];
        this.min = min;
        this.max = max;
        CHECK_GT(max, min);
        // Take absolute value of _func_
        for (int i = 0; i < func.Length; ++i)
            func[i] = FMath.Abs(func[i]);

        // Compute integral of step function at $x_i$
        cdf[0] = 0;
        int n = f.Length;
        for (int i = 1; i < n + 1; ++i)
        {
            CHECK_GE(func[i - 1], 0);
            cdf[i] = cdf[i - 1] + func[i - 1] * (max - min) / n;
        }

        // Transform step function integral into CDF
        funcInt = cdf[n];
        if (funcInt == 0)
            for (int i = 1; i < n + 1; ++i)
                cdf[i] = (Float)i / (Float)n;
        else
            for (int i = 1; i < n + 1; ++i)
                cdf[i] /= funcInt;
    }

    public long BytesUsed() => (func.Length + cdf.Length) * sizeof(Float);

    public Float Integral() => funcInt;
    public int Size() => func.Length;

    public Float Sample(Float u) => Sample(u, out _, out _);

    public Float Sample(Float u, out Float pdf, out int offset)
    {
        // Find surrounding CDF segments and _offset_
        int o = FindInterval(cdf, u);
        offset = o;

        // Compute offset along CDF segment
        Float du = u - cdf[o];
        if (cdf[o + 1] - cdf[o] > 0)
            du /= cdf[o + 1] - cdf[o];
        DCHECK(!IsNaN(du));

        // Compute PDF for sampled offset
        pdf = (funcInt > 0) ? func[o] / funcInt : 0;

        // Return $x$ corresponding to sample
        return Lerp((o + du) / Size(), min, max);
    }

    public Float? Invert(Float x)
    {
        // Compute offset to CDF values that bracket $x$
        if (x < min || x > max)
            return null;
        Float c = (x - min) / (max - min) * func.Length;
        int offset = Clamp((int)c, 0, func.Length - 1);
        DCHECK(offset >= 0 && offset + 1 < cdf.Length);

        // Linearly interpolate between adjacent CDF values to find sample value
        Float delta = c - offset;
        return Lerp(delta, cdf[offset], cdf[offset + 1]);
    }

    public override string ToString() =>
        FormattableString.Invariant(
            $"[ PiecewiseConstant1D func: [ {string.Join(", ", func)} ] cdf: [ {string.Join(", ", cdf)} ] min: {min} max: {max} funcInt: {funcInt} ]");
}

// PiecewiseConstant2D Definition
public sealed class PiecewiseConstant2D
{
    // PiecewiseConstant2D Private Members
    private readonly Bounds2f domain;
    private readonly PiecewiseConstant1D[] pConditionalV;
    private readonly PiecewiseConstant1D pMarginal;

    // PiecewiseConstant2D Public Methods
    public PiecewiseConstant2D(ReadOnlySpan<Float> data, int nx, int ny)
        : this(data, nx, ny, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1))) { }

    public PiecewiseConstant2D(Array2D<Float> data)
        : this(data.AsSpan(), data.XSize(), data.YSize()) { }

    public PiecewiseConstant2D(Array2D<Float> data, Bounds2f domain)
        : this(data.AsSpan(), data.XSize(), data.YSize(), domain) { }

    public PiecewiseConstant2D(ReadOnlySpan<Float> func, int nu, int nv, Bounds2f domain)
    {
        this.domain = domain;
        CHECK_EQ(func.Length, nu * nv);
        pConditionalV = new PiecewiseConstant1D[nv];
        for (int v = 0; v < nv; ++v)
            // Compute conditional sampling distribution for $\tilde{v}$
            pConditionalV[v] = new PiecewiseConstant1D(func.Slice(v * nu, nu), domain.pMin[0],
                                                       domain.pMax[0]);

        // Compute marginal sampling distribution $p[\tilde{v}]$
        var marginalFunc = new Float[nv];
        for (int v = 0; v < nv; ++v)
            marginalFunc[v] = pConditionalV[v].Integral();
        pMarginal = new PiecewiseConstant1D(marginalFunc, domain.pMin[1], domain.pMax[1]);
    }

    public long BytesUsed() =>
        pConditionalV.Length * pConditionalV[0].BytesUsed() + pMarginal.BytesUsed();

    public Bounds2f Domain() => domain;

    public Point2i Resolution() => new(pConditionalV[0].Size(), pMarginal.Size());

    public Float Integral() => pMarginal.Integral();

    public Point2f Sample(Point2f u) => Sample(u, out _, out _);

    public Point2f Sample(Point2f u, out Float pdf, out Point2i offset)
    {
        Point2i uv = default;
        Float d1 = pMarginal.Sample(u[1], out Float pdf1, out uv.y);
        Float d0 = pConditionalV[uv[1]].Sample(u[0], out Float pdf0, out uv.x);
        pdf = pdf0 * pdf1;
        offset = uv;
        return new Point2f(d0, d1);
    }

    public Float PDF(Point2f pr)
    {
        Point2f p = new(domain.Offset(pr));
        int iu = Clamp((int)(p[0] * pConditionalV[0].Size()), 0, pConditionalV[0].Size() - 1);
        int iv = Clamp((int)(p[1] * pMarginal.Size()), 0, pMarginal.Size() - 1);
        return pConditionalV[iv].func[iu] / pMarginal.Integral();
    }

    public Point2f? Invert(Point2f p)
    {
        Float? mInv = pMarginal.Invert(p[1]);
        if (!mInv.HasValue)
            return null;
        Float p1o = (p[1] - domain.pMin[1]) / (domain.pMax[1] - domain.pMin[1]);
        if (p1o < 0 || p1o > 1)
            return null;
        int offset = (int)Clamp(p1o * pConditionalV.Length, 0, pConditionalV.Length - 1);
        Float? cInv = pConditionalV[offset].Invert(p[0]);
        if (!cInv.HasValue)
            return null;
        return new Point2f(cInv.Value, mInv.Value);
    }

    // For tests (pbrt's TestCompareDistributions() accesses these directly).
    internal PiecewiseConstant1D Marginal => pMarginal;
    internal IReadOnlyList<PiecewiseConstant1D> Conditionals => pConditionalV;

    public override string ToString() =>
        $"[ PiecewiseConstant2D domain: {domain} pConditionalV: [ {string.Join(", ", (object[])pConditionalV)} ] pMarginal: {pMarginal} ]";
}

// AliasTable Definition
public sealed class AliasTable
{
    // AliasTable Private Members
    private struct Bin
    {
        public Float q, p;
        public int alias;
    }

    private readonly Bin[] bins;

    // AliasTable Method Definitions
    public AliasTable(ReadOnlySpan<Float> weights)
    {
        bins = new Bin[weights.Length];
        // Normalize _weights_ to compute alias table PDF
        double sumD = 0;
        foreach (Float w in weights)
            sumD += w;
        Float sum = (Float)sumD;
        CHECK_GT(sum, 0);
        for (int i = 0; i < weights.Length; ++i)
            bins[i].p = weights[i] / sum;

        // Create alias table work lists
        var under = new List<(Float pHat, int index)>();
        var over = new List<(Float pHat, int index)>();
        for (int i = 0; i < bins.Length; ++i)
        {
            // Add outcome _i_ to an alias table work list
            Float pHat = bins[i].p * bins.Length;
            if (pHat < 1)
                under.Add((pHat, i));
            else
                over.Add((pHat, i));
        }

        // Process under and over work item together
        while (under.Count > 0 && over.Count > 0)
        {
            // Remove items _un_ and _ov_ from the alias table work lists
            var un = under[^1];
            var ov = over[^1];
            under.RemoveAt(under.Count - 1);
            over.RemoveAt(over.Count - 1);

            // Initialize probability and alias for _un_
            bins[un.index].q = un.pHat;
            bins[un.index].alias = ov.index;

            // Push excess probability on to work list
            Float pExcess = un.pHat + ov.pHat - 1;
            if (pExcess < 1)
                under.Add((pExcess, ov.index));
            else
                over.Add((pExcess, ov.index));
        }

        // Handle remaining alias table work items
        while (over.Count > 0)
        {
            var ov = over[^1];
            over.RemoveAt(over.Count - 1);
            bins[ov.index].q = 1;
            bins[ov.index].alias = -1;
        }
        while (under.Count > 0)
        {
            var un = under[^1];
            under.RemoveAt(under.Count - 1);
            bins[un.index].q = 1;
            bins[un.index].alias = -1;
        }
    }

    public int Sample(Float u) => Sample(u, out _, out _);

    public int Sample(Float u, out Float pmf, out Float uRemapped)
    {
        // Compute alias table _offset_ and remapped random sample _up_
        int offset = Min((int)(u * bins.Length), bins.Length - 1);
        Float up = Min(u * bins.Length - offset, OneMinusEpsilon);

        if (up < bins[offset].q)
        {
            // Return sample for alias table at _offset_
            DCHECK_GT(bins[offset].p, 0);
            pmf = bins[offset].p;
            uRemapped = Min(up / bins[offset].q, OneMinusEpsilon);
            return offset;
        }
        else
        {
            // Return sample for alias table at _alias[offset]_
            int alias = bins[offset].alias;
            DCHECK_GE(alias, 0);
            DCHECK_GT(bins[alias].p, 0);
            pmf = bins[alias].p;
            uRemapped = Min((up - bins[offset].q) / (1 - bins[offset].q), OneMinusEpsilon);
            return alias;
        }
    }

    public int Size() => bins.Length;
    public Float PMF(int index) => bins[index].p;

    public override string ToString()
    {
        var s = new System.Text.StringBuilder("[ AliasTable bins: [ ");
        foreach (Bin b in bins)
            s.Append(FormattableString.Invariant($"[ Bin q: {b.q} p: {b.p} alias: {b.alias} ] "));
        return s.Append("] ]").ToString();
    }
}

// SummedAreaTable Definition
public sealed class SummedAreaTable
{
    // SummedAreaTable Private Members
    private readonly Array2D<double> sum;

    // SummedAreaTable Public Methods
    public SummedAreaTable(Array2D<Float> values)
    {
        sum = new Array2D<double>(values.XSize(), values.YSize());
        sum[0, 0] = values[0, 0];
        // Compute sums along first row and column
        for (int x = 1; x < sum.XSize(); ++x)
            sum[x, 0] = values[x, 0] + sum[x - 1, 0];
        for (int y = 1; y < sum.YSize(); ++y)
            sum[0, y] = values[0, y] + sum[0, y - 1];

        // Compute sums for the remainder of the entries
        for (int y = 1; y < sum.YSize(); ++y)
            for (int x = 1; x < sum.XSize(); ++x)
                sum[x, y] = (values[x, y] + sum[x - 1, y] + sum[x, y - 1] - sum[x - 1, y - 1]);
    }

    public Float Integral(Bounds2f extent)
    {
        double s = (((double)Lookup(extent.pMax.x, extent.pMax.y) -
                     (double)Lookup(extent.pMin.x, extent.pMax.y)) +
                    ((double)Lookup(extent.pMin.x, extent.pMin.y) -
                     (double)Lookup(extent.pMax.x, extent.pMin.y)));
        return Max((Float)(s / (sum.XSize() * sum.YSize())), 0);
    }

    // SummedAreaTable Private Methods
    private Float Lookup(Float x, Float y)
    {
        // Rescale $(x,y)$ to table resolution and compute integer coordinates
        x *= sum.XSize();
        y *= sum.YSize();
        int x0 = (int)x, y0 = (int)y;

        // Bilinearly interpolate between surrounding table values
        Float v00 = LookupInt(x0, y0), v10 = LookupInt(x0 + 1, y0);
        Float v01 = LookupInt(x0, y0 + 1), v11 = LookupInt(x0 + 1, y0 + 1);
        Float dx = x - (int)x, dy = y - (int)y;
        return (1 - dx) * (1 - dy) * v00 + (1 - dx) * dy * v01 + dx * (1 - dy) * v10 +
               dx * dy * v11;
    }

    private Float LookupInt(int x, int y)
    {
        // Return zero at lower boundaries
        if (x == 0 || y == 0)
            return 0;

        // Reindex $(x,y)$ and return actual stored value
        x = Min(x - 1, sum.XSize() - 1);
        y = Min(y - 1, sum.YSize() - 1);
        return (Float)sum[x, y];
    }

    public override string ToString() => $"[ SummedAreaTable sum: {sum} ]";
}

// WindowedPiecewiseConstant2D Definition
public sealed class WindowedPiecewiseConstant2D
{
    // WindowedPiecewiseConstant2D Private Members
    private readonly SummedAreaTable sat;
    private readonly Array2D<Float> func;

    // WindowedPiecewiseConstant2D Public Methods
    public WindowedPiecewiseConstant2D(Array2D<Float> f)
    {
        sat = new SummedAreaTable(f);
        func = new Array2D<Float>(f);
    }

    public Point2f? Sample(Point2f u, Bounds2f b, out Float pdf)
    {
        pdf = 0;
        // Handle zero-valued function for windowed sampling
        if (sat.Integral(b) == 0)
            return null;

        // Define lambda function _Px_ for marginal cumulative distribution
        Float bInt = sat.Integral(b);
        Float Px(Float x)
        {
            Bounds2f bx = b;
            bx.pMax.x = x;
            return sat.Integral(bx) / bInt;
        }

        // Sample marginal windowed function in $x$
        Point2f p;
        p.y = 0;
        p.x = SampleBisection(Px, u[0], b.pMin.x, b.pMax.x, func.XSize());

        // Sample conditional windowed function in $y$
        // Compute 2D bounds _bCond_ for conditional sampling
        int nx = func.XSize();
        Bounds2f bCond = new(new Point2f(FMath.Floor(p.x * nx) / nx, b.pMin.y),
                             new Point2f(FMath.Ceiling(p.x * nx) / nx, b.pMax.y));
        if (bCond.pMin.x == bCond.pMax.x)
            bCond.pMax.x += 1f / nx;
        if (sat.Integral(bCond) == 0)
            return null;

        // Define lambda function for conditional distribution and sample $y$
        Float condIntegral = sat.Integral(bCond);
        Float Py(Float y)
        {
            Bounds2f by = bCond;
            by.pMax.y = y;
            return sat.Integral(by) / condIntegral;
        }
        p.y = SampleBisection(Py, u[1], b.pMin.y, b.pMax.y, func.YSize());

        // Compute PDF and return point sampled from windowed function
        pdf = Eval(p) / bInt;
        return p;
    }

    public Float PDF(Point2f p, Bounds2f b)
    {
        Float funcInt = sat.Integral(b);
        if (funcInt == 0)
            return 0;
        return Eval(p) / funcInt;
    }

    // WindowedPiecewiseConstant2D Private Methods
    private static Float SampleBisection(Func<Float, Float> P, Float u, Float min, Float max, int n)
    {
        // Apply bisection to bracket _u_
        while (FMath.Ceiling(n * max) - FMath.Floor(n * min) > 1)
        {
            DCHECK_LE(P(min), u);
            DCHECK_GE(P(max), u);
            Float mid = (min + max) / 2;
            if (P(mid) > u)
                max = mid;
            else
                min = mid;
        }

        // Find sample by interpolating between _min_ and _max_
        Float t = (u - P(min)) / (P(max) - P(min));
        return Clamp(Lerp(t, min, max), min, max);
    }

    private Float Eval(Point2f p)
    {
        Point2i pi = new(Min((int)(p[0] * func.XSize()), func.XSize() - 1),
                         Min((int)(p[1] * func.YSize()), func.YSize() - 1));
        return func[pi];
    }
}

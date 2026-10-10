// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/stats.h and stats.cpp
//
// pbrt's STAT_* macros declare thread_local variables and register a
// callback that reports and clears them. Here each statistic is an object,
// declared once as a static field:
//
//   STAT_COUNTER("Geometry/Triangles", nTris)    static readonly StatCounter nTris = new("Geometry/Triangles");
//   ++nTris;                                     nTris.Inc();
//   STAT_PERCENT("A/B", nHits, nTests)           static readonly StatPercent hits = new("A/B");
//   ++nHits; ++nTests;                           hits.Num(); hits.Denom();
//
// whose values live in per-thread slots, so updating them is as cheap as in
// pbrt; ReportThreadStats() adds the calling thread's values to the global
// accumulator and clears them, like pbrt's callbacks.

using System.Runtime.InteropServices;
using System.Text;

namespace Pbrt;

// Statistics base class: one registered statistic with _nLong_ integer and
// _nDouble_ floating-point per-thread slots.
public abstract class Stat
{
    protected readonly string title;
    protected readonly int longBase, doubleBase;

    protected Stat(string title, int nLong, int nDouble, bool pixelStat = false)
    {
        this.title = title;
        Stats.Register(this, nLong, nDouble, pixelStat, out longBase, out doubleBase);
        // Slots start out at their reset values in every thread.
    }

    protected static ref long L(int i) => ref Stats.LongSlot(i);
    protected static ref double D(int i) => ref Stats.DoubleSlot(i);

    // Sets this statistic's slots to their initial values.
    internal virtual void Reset(Span<long> l, Span<double> d) { }

    // Reports the thread's values (from _l_ and _d_, indexed from the
    // statistic's bases) and resets them.
    internal abstract void Report(StatsAccumulator accum, Span<long> l, Span<double> d);

    // For pixel statistics: reports the values accumulated since the last
    // call for pixel _p_.
    internal virtual void ReportPixel(Point2i p, int counterIndex, PixelStatsAccumulator accum, Span<long> l) { }

    internal int LongBase => longBase;
    internal int DoubleBase => doubleBase;
}

// STAT_COUNTER
public sealed class StatCounter(string title) : Stat(title, 1, 0)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Inc() => ++L(longBase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long v) => L(longBase) += v;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportCounter(title, l[longBase]);
        l[longBase] = 0;
    }
}

// STAT_PIXEL_COUNTER
public sealed class StatPixelCounter(string title) : Stat(title, 2, 0, pixelStat: true)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Inc() => ++L(longBase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long v) => L(longBase) += v;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        // report sum, since if disabled, it all just goes into var...
        accum.ReportCounter(title, l[longBase] + l[longBase + 1]);
        l[longBase + 1] = 0;
        l[longBase] = 0;
    }

    internal override void ReportPixel(Point2i p, int counterIndex, PixelStatsAccumulator accum, Span<long> l)
    {
        accum.ReportCounter(p, counterIndex, title, l[longBase]);
        l[longBase + 1] += l[longBase];
        l[longBase] = 0;
    }
}

// STAT_MEMORY_COUNTER
public sealed class StatMemoryCounter(string title) : Stat(title, 1, 0)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long bytes) => L(longBase) += bytes;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportMemoryCounter(title, l[longBase]);
        l[longBase] = 0;
    }
}

// STAT_INT_DISTRIBUTION (pbrt's `var << value` is Add(value))
public sealed class StatIntDistribution(string title) : Stat(title, 4, 0)
{
    public void Add(long value)
    {
        ref long sum = ref L(longBase);
        sum += value;
        ref long count = ref L(longBase + 1);
        count += 1;
        ref long min = ref L(longBase + 2);
        min = (value < min) ? value : min;
        ref long max = ref L(longBase + 3);
        max = (value > max) ? value : max;
    }

    internal override void Reset(Span<long> l, Span<double> d)
    {
        l[longBase] = 0;
        l[longBase + 1] = 0;
        l[longBase + 2] = long.MaxValue;
        l[longBase + 3] = long.MinValue;
    }

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportIntDistribution(title, l[longBase], l[longBase + 1], l[longBase + 2], l[longBase + 3]);
        Reset(l, d);
    }
}

// STAT_FLOAT_DISTRIBUTION
public sealed class StatFloatDistribution(string title) : Stat(title, 1, 3)
{
    public void Add(double value)
    {
        D(doubleBase) += value;
        L(longBase) += 1;
        ref double min = ref D(doubleBase + 1);
        min = Math.Min(min, value);
        ref double max = ref D(doubleBase + 2);
        max = Math.Max(max, value);
    }

    internal override void Reset(Span<long> l, Span<double> d)
    {
        d[doubleBase] = 0;
        l[longBase] = 0;
        d[doubleBase + 1] = double.MaxValue;
        d[doubleBase + 2] = double.MinValue;
    }

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportFloatDistribution(title, d[doubleBase], l[longBase], d[doubleBase + 1], d[doubleBase + 2]);
        Reset(l, d);
    }
}

// STAT_PERCENT (++numVar is Num(), ++denomVar is Denom())
public sealed class StatPercent(string title) : Stat(title, 2, 0)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Num(long n = 1) => L(longBase) += n;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Denom(long n = 1) => L(longBase + 1) += n;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportPercentage(title, l[longBase], l[longBase + 1]);
        l[longBase] = 0;
        l[longBase + 1] = 0;
    }
}

// STAT_RATIO
public sealed class StatRatio(string title) : Stat(title, 2, 0)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Num(long n = 1) => L(longBase) += n;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Denom(long n = 1) => L(longBase + 1) += n;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        accum.ReportRatio(title, l[longBase], l[longBase + 1]);
        l[longBase] = 0;
        l[longBase + 1] = 0;
    }
}

// STAT_PIXEL_RATIO
public sealed class StatPixelRatio(string title) : Stat(title, 4, 0, pixelStat: true)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Num(long n = 1) => L(longBase) += n;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Denom(long n = 1) => L(longBase + 2) += n;

    internal override void Report(StatsAccumulator accum, Span<long> l, Span<double> d)
    {
        // report sum, since if disabled, it all just goes into var...
        accum.ReportRatio(title, l[longBase] + l[longBase + 1], l[longBase + 2] + l[longBase + 3]);
        l[longBase] = 0;
        l[longBase + 1] = 0;
        l[longBase + 2] = 0;
        l[longBase + 3] = 0;
    }

    internal override void ReportPixel(Point2i p, int counterIndex, PixelStatsAccumulator accum, Span<long> l)
    {
        accum.ReportRatio(p, counterIndex, title, l[longBase], l[longBase + 2]);
        l[longBase + 1] += l[longBase];
        l[longBase + 3] += l[longBase + 2];
        l[longBase] = 0;
        l[longBase + 2] = 0;
    }
}

// PixelStatsAccumulator Definition
public sealed class PixelStatsAccumulator
{
    // PixelStatsAccumulator::PixelStats Definition
    internal Image time = new();
    internal readonly List<string> counterNames = [];
    internal readonly List<Image> counterImages = [];
    internal readonly List<string> ratioNames = [];
    internal readonly List<Image> ratioImages = [];

    // PixelStatsAccumulator Method Definitions
    public void ReportPixelMS(Point2i p, float ms)
    {
        Point2i res = new(Stats.imageBounds.Diagonal());
        if (time.Resolution() != res)
            time = new Image(PixelFormat.Float, res, ["ms"]);

        Point2i pp = new(p - Stats.imageBounds.pMin);
        time.SetChannel(pp, 0, time.GetChannel(pp, 0) + ms);
    }

    public void ReportCounter(Point2i p, int statIndex, string name, long val)
    {
        while (statIndex >= counterImages.Count)
        {
            counterImages.Add(new Image());
            counterNames.Add("");
        }
        if (counterNames[statIndex].Length == 0)
            counterNames[statIndex] = name;

        Point2i res = new(Stats.imageBounds.Diagonal());
        if (counterImages[statIndex].Resolution() != res)
            counterImages[statIndex] = new Image(PixelFormat.Float, res, ["count"]);
        Image im = counterImages[statIndex];

        Point2i pp = new(p - Stats.imageBounds.pMin);
        im.SetChannel(pp, 0, im.GetChannel(pp, 0) + val);
    }

    public void ReportRatio(Point2i p, int statIndex, string name, long num, long denom)
    {
        while (statIndex >= ratioImages.Count)
        {
            ratioImages.Add(new Image());
            ratioNames.Add("");
        }
        if (ratioNames[statIndex].Length == 0)
            ratioNames[statIndex] = name;

        Point2i res = new(Stats.imageBounds.Diagonal());
        if (ratioImages[statIndex].Resolution() != res)
            ratioImages[statIndex] = new Image(PixelFormat.Float, res, ["numerator", "denominator", "ratio"]);
        Image im = ratioImages[statIndex];

        Point2i pp = new(p - Stats.imageBounds.pMin);
        im.SetChannel(pp, 0, im.GetChannel(pp, 0) + num);
        im.SetChannel(pp, 1, im.GetChannel(pp, 1) + denom);
        if (im.GetChannel(pp, 0) == 0)
            im.SetChannel(pp, 2, 0);
        else
            im.SetChannel(pp, 2, im.GetChannel(pp, 0) / im.GetChannel(pp, 1));
    }
}

// StatsAccumulator Definition
public sealed class StatsAccumulator
{
    // StatsAccumulator::Stats Definition
    private sealed class Distribution<T>(T min, T max)
    {
        public long count = 0;
        public T min = min;
        public T max = max;
        public T sum = default!;
    }

    private sealed class RareCheck(double maxFrequency)
    {
        public readonly double maxFrequency = maxFrequency;
        public long numTrue = 0, total = 0;
    }

    private readonly SortedDictionary<string, long> counters = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> memoryCounters = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Distribution<long>> intDistributions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Distribution<double>> floatDistributions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, (long, long)> percentages = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, (long, long)> ratios = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, RareCheck> rareChecks = new(StringComparer.Ordinal);

    private Image pixelTime = new();
    private readonly List<string> pixelCounterNames = [];
    private readonly List<Image> pixelCounterImages = [];
    private readonly List<string> pixelRatioNames = [];
    private readonly List<Image> pixelRatioImages = [];

    // StatsAccumulator Method Definitions
    public void ReportCounter(string name, long val) =>
        counters[name] = counters.GetValueOrDefault(name) + val;

    public void ReportMemoryCounter(string name, long val) =>
        memoryCounters[name] = memoryCounters.GetValueOrDefault(name) + val;

    public void ReportPercentage(string name, long num, long denom)
    {
        var (n, d) = percentages.GetValueOrDefault(name);
        percentages[name] = (n + num, d + denom);
    }

    public void ReportRatio(string name, long num, long denom)
    {
        var (n, d) = ratios.GetValueOrDefault(name);
        ratios[name] = (n + num, d + denom);
    }

    public void ReportRareCheck(string condition, double maxFrequency, long numTrue, long total)
    {
        if (!rareChecks.TryGetValue(condition, out RareCheck? rc))
            rareChecks[condition] = rc = new RareCheck(maxFrequency);
        rc.numTrue += numTrue;
        rc.total += total;
    }

    public void ReportIntDistribution(string name, long sum, long count, long min, long max)
    {
        if (!intDistributions.TryGetValue(name, out Distribution<long>? distrib))
            intDistributions[name] = distrib = new Distribution<long>(long.MaxValue, long.MinValue);
        distrib.sum += sum;
        distrib.count += count;
        distrib.min = Math.Min(distrib.min, min);
        distrib.max = Math.Max(distrib.max, max);
    }

    public void ReportFloatDistribution(string name, double sum, long count, double min, double max)
    {
        if (!floatDistributions.TryGetValue(name, out Distribution<double>? distrib))
            floatDistributions[name] = distrib = new Distribution<double>(double.MaxValue, double.MinValue);
        distrib.sum += sum;
        distrib.count += count;
        distrib.min = Math.Min(distrib.min, min);
        distrib.max = Math.Max(distrib.max, max);
    }

    public void AccumulatePixelStats(PixelStatsAccumulator accum)
    {
        Point2i res = new(Stats.imageBounds.Diagonal());
        if (pixelTime.Resolution() == new Point2i(0, 0))
            pixelTime = new Image(PixelFormat.Float, res, ["ms"]);
        else
            CHECK(pixelTime.Resolution() == res);
        CHECK(pixelTime.Resolution() == accum.time.Resolution());

        for (int y = 0; y < pixelTime.Resolution().y; ++y)
            for (int x = 0; x < pixelTime.Resolution().x; ++x)
                pixelTime.SetChannel(new Point2i(x, y), 0,
                                     pixelTime.GetChannel(new Point2i(x, y), 0) +
                                     accum.time.GetChannel(new Point2i(x, y), 0));

        while (pixelCounterImages.Count < accum.counterImages.Count)
        {
            pixelCounterImages.Add(new Image());
            pixelCounterNames.Add("");
        }
        while (pixelRatioImages.Count < accum.ratioImages.Count)
        {
            pixelRatioImages.Add(new Image());
            pixelRatioNames.Add("");
        }

        for (int i = 0; i < accum.counterImages.Count; ++i)
        {
            if (pixelCounterNames[i].Length == 0)
                pixelCounterNames[i] = accum.counterNames[i];

            Image threadImage = accum.counterImages[i];
            if (threadImage.Resolution() == new Point2i(0, 0))
                continue;
            if (pixelCounterImages[i].Resolution() == new Point2i(0, 0))
                pixelCounterImages[i] = new Image(PixelFormat.Float, threadImage.Resolution(), ["count"]);
            Image accumImage = pixelCounterImages[i];
            for (int y = 0; y < threadImage.Resolution().y; ++y)
                for (int x = 0; x < threadImage.Resolution().x; ++x)
                    accumImage.SetChannel(new Point2i(x, y), 0,
                                          accumImage.GetChannel(new Point2i(x, y), 0) +
                                          threadImage.GetChannel(new Point2i(x, y), 0));
        }
        for (int i = 0; i < accum.ratioImages.Count; ++i)
        {
            if (pixelRatioNames[i].Length == 0)
                pixelRatioNames[i] = accum.ratioNames[i];

            Image threadImage = accum.ratioImages[i];
            if (threadImage.Resolution() == new Point2i(0, 0))
                continue;
            if (pixelRatioImages[i].Resolution() == new Point2i(0, 0))
                pixelRatioImages[i] = new Image(PixelFormat.Float, threadImage.Resolution(),
                                                ["numerator", "denominator", "ratio"]);
            Image accumImage = pixelRatioImages[i];
            for (int y = 0; y < threadImage.Resolution().y; ++y)
                for (int x = 0; x < threadImage.Resolution().x; ++x)
                {
                    Point2i p = new(x, y);
                    accumImage.SetChannel(p, 0, accumImage.GetChannel(p, 0) + threadImage.GetChannel(p, 0));
                    accumImage.SetChannel(p, 1, accumImage.GetChannel(p, 1) + threadImage.GetChannel(p, 1));
                    if (accumImage.GetChannel(p, 0) == 0)
                        accumImage.SetChannel(p, 2, 0f);
                    else
                        accumImage.SetChannel(p, 2, accumImage.GetChannel(p, 0) / accumImage.GetChannel(p, 1));
                }
        }
    }

    private static void getCategoryAndTitle(string str, out string category, out string title)
    {
        List<string> comps = SplitString(str, '/');
        if (comps.Count == 1)
        {
            category = "";
            title = comps[0];
        }
        else
        {
            CHECK_EQ(comps.Count, 2);
            category = comps[0];
            title = comps[1];
        }
    }

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);

    public void Print(TextWriter dest)
    {
        dest.Write("Statistics:\n");
        SortedDictionary<string, List<string>> toPrint = new(StringComparer.Ordinal);
        List<string> category(string c)
        {
            if (!toPrint.TryGetValue(c, out List<string>? l))
                toPrint[c] = l = [];
            return l;
        }

        foreach (var (name, value) in counters)
        {
            if (value == 0)
                continue;
            getCategoryAndTitle(name, out string cat, out string title);
            category(cat).Add(Inv($"{title,-42}               {value,12}"));
        }

        long totalMemoryReported = 0;
        static string printBytes(long bytes)
        {
            float kb = (float)((double)bytes / 1024.0);
            if (MathF.Abs(kb) < 1024f)
                return Inv($"{kb,9:F2} kB");

            float mib = kb / 1024f;
            if (MathF.Abs(mib) < 1024f)
                return Inv($"{mib,9:F2} MiB");

            float gib = mib / 1024f;
            return Inv($"{gib,9:F2} GiB");
        }

        foreach (var (name, value) in memoryCounters)
        {
            if (value == 0)
                continue;
            totalMemoryReported += value;

            getCategoryAndTitle(name, out string cat, out string title);
            category(cat).Add(Inv($"{title,-42}                  {printBytes(value)}"));
        }
        long unreportedBytes = GetCurrentRSS() - totalMemoryReported;
        if (unreportedBytes > 0)
            category("Memory").Add(Inv($"{"Unreported / unused",-42}                  {printBytes(unreportedBytes)}"));

        foreach (var (name, distrib) in intDistributions)
        {
            if (distrib.count == 0)
                continue;
            getCategoryAndTitle(name, out string cat, out string title);
            double avg = (double)distrib.sum / (double)distrib.count;
            category(cat).Add(Inv($"{title,-42}                      {avg:F3} avg [range {distrib.min} - {distrib.max}]"));
        }
        foreach (var (name, distrib) in floatDistributions)
        {
            if (distrib.count == 0)
                continue;
            getCategoryAndTitle(name, out string cat, out string title);
            double avg = distrib.sum / (double)distrib.count;
            category(cat).Add(Inv($"{title,-42}                      {avg:F3} avg [range {distrib.min:F6} - {distrib.max:F6}]"));
        }
        foreach (var (name, (num, denom)) in percentages)
        {
            if (denom == 0)
                continue;
            getCategoryAndTitle(name, out string cat, out string title);
            category(cat).Add(Inv($"{title,-42}{num,12} / {denom,12} ({(100f * num) / denom:F2}%)"));
        }
        foreach (var (name, (num, denom)) in ratios)
        {
            if (denom == 0)
                continue;
            getCategoryAndTitle(name, out string cat, out string title);
            category(cat).Add(Inv($"{title,-42}{num,12} / {denom,12} ({(double)num / (double)denom:F2}x)"));
        }

        foreach (var (cat, items) in toPrint)
        {
            dest.Write($"  {cat}\n");
            foreach (string item in items)
                dest.Write($"    {item}\n");
        }
    }

    public void WritePixelImages()
    {
        // FIXME: do this where?
        CHECK(pixelTime.Write(Stats.pixelStatsBaseName + "-time.exr"));

        static string rewriteSlashes(string s) => s.Replace('/', '_');

        for (int i = 0; i < pixelCounterImages.Count; ++i)
        {
            string n = Stats.pixelStatsBaseName + "-" + rewriteSlashes(pixelCounterNames[i]) + ".exr";

            static bool AllZero(Image im)
            {
                for (int y = 0; y < im.Resolution().y; ++y)
                    for (int x = 0; x < im.Resolution().x; ++x)
                        if (im.GetChannel(new Point2i(x, y), 0) != 0)
                            return false;
                return true;
            }
            if (!AllZero(pixelCounterImages[i]))
                CHECK(pixelCounterImages[i].Write(n));
        }

        for (int i = 0; i < pixelRatioImages.Count; ++i)
        {
            string n = Stats.pixelStatsBaseName + "-" + rewriteSlashes(pixelRatioNames[i]) + ".exr";

            static bool AllZero(Image im)
            {
                for (int y = 0; y < im.Resolution().y; ++y)
                    for (int x = 0; x < im.Resolution().x; ++x)
                        if (im.GetChannels(new Point2i(x, y)).MaxValue() != 0)
                            return false;
                return true;
            }
            if (!AllZero(pixelRatioImages[i]))
                CHECK(pixelRatioImages[i].Write(n));
        }
    }

    public bool PrintCheckRare(TextWriter dest)
    {
        bool anyFailed = false;
        foreach (var (condition, rc) in rareChecks)
        {
            Float trueFreq = (Float)((double)rc.numTrue / (double)rc.total);
            Float varianceEstimate = (Float)(1 / (double)(rc.total - 1) * trueFreq * (1 - trueFreq));
            if (trueFreq - 2 * varianceEstimate >= rc.maxFrequency)
            {
                dest.Write(Inv($"{condition} @ {trueFreq:G9} failures was {trueFreq / rc.maxFrequency:F6}x over limit {rc.maxFrequency:G9} ({rc.total} samples, sigma est {varianceEstimate:G9})\n"));
                anyFailed = true;
            }
        }
        return anyFailed;
    }

    public void Clear()
    {
        counters.Clear();
        memoryCounters.Clear();
        intDistributions.Clear();
        floatDistributions.Clear();
        percentages.Clear();
        ratios.Clear();
    }
}

// Statistics Function Definitions
public static class Stats
{
    // Statistics Local Variables
    private static readonly object mutex = new();
    private static readonly List<Stat> stats = [];
    private static readonly List<Stat> pixelStats = [];
    private static int nLongSlots, nDoubleSlots;

    // The calling thread's statistic values (pbrt's thread_local variables).
    [ThreadStatic] private static long[]? threadLongs;
    [ThreadStatic] private static double[]? threadDoubles;

    // ThreadStatsState Definition
    private sealed class ThreadStatsState
    {
        public Point2i p;
        public bool active;
        public long start;
        public PixelStatsAccumulator accum = new();
    }

    [ThreadStatic] private static ThreadStatsState? threadStatsState;

    private static ThreadStatsState TSS => threadStatsState ??= new ThreadStatsState();

    internal static bool pixelStatsEnabled = false;
    internal static Bounds2i imageBounds;
    internal static string pixelStatsBaseName = "";

    private static readonly StatsAccumulator statsAccumulator = new();

    // StatRegisterer
    internal static void Register(Stat stat, int nLong, int nDouble, bool pixelStat,
                                  out int longBase, out int doubleBase)
    {
        lock (mutex)
        {
            longBase = nLongSlots;
            doubleBase = nDoubleSlots;
            nLongSlots += nLong;
            nDoubleSlots += nDouble;
            stats.Add(stat);
            if (pixelStat)
                pixelStats.Add(stat);
        }
    }

    // Grows the thread's slot arrays to cover all registered statistics and
    // initializes the new slots.
    private static void GrowSlots()
    {
        lock (mutex)
        {
            long[] l = new long[nLongSlots];
            double[] d = new double[nDoubleSlots];
            threadLongs?.CopyTo(l, 0);
            threadDoubles?.CopyTo(d, 0);
            // Every statistic has at least one long slot, registered along
            // with its double slots, so these are the new ones.
            int oldL = threadLongs?.Length ?? 0;
            foreach (Stat s in stats)
                if (s.LongBase >= oldL)
                    s.Reset(l, d);
            threadLongs = l;
            threadDoubles = d;
        }
    }

    internal static ref long LongSlot(int i)
    {
        long[]? l = threadLongs;
        if (l == null || i >= l.Length)
        {
            GrowSlots();
            l = threadLongs!;
        }
        return ref l[i];
    }

    internal static ref double DoubleSlot(int i)
    {
        double[]? d = threadDoubles;
        if (d == null || i >= d.Length)
        {
            GrowSlots();
            d = threadDoubles!;
        }
        return ref d[i];
    }

    // CHECK_RARE: per-thread counts for each call site, keyed by the
    // (interned) file path string and line.
    private sealed class RareCheckCounts(string condition, double freq)
    {
        public readonly string condition = condition;
        public readonly double freq = freq;
        public long numTrue, total;
    }

    private readonly struct CallSite(string file, int line) : IEquatable<CallSite>
    {
        public readonly string file = file;
        public readonly int line = line;
        public bool Equals(CallSite o) => ReferenceEquals(file, o.file) && line == o.line;
        public override bool Equals(object? obj) => obj is CallSite o && Equals(o);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(file) * 31 + line;
    }

    [ThreadStatic] private static Dictionary<CallSite, RareCheckCounts>? threadRareChecks;

    internal static void RecordRareCheck(double freq, bool condition, string? expr, string file, int line)
    {
        Dictionary<CallSite, RareCheckCounts> checks = threadRareChecks ??= [];
        ref RareCheckCounts? rc = ref CollectionsMarshal.GetValueRefOrAddDefault(checks, new CallSite(file, line), out _);
        rc ??= new RareCheckCounts($"{Path.GetFileName(file)} {line}: CHECK_RARE failed: {expr}", freq);
        ++rc.total;
        if (condition)
            ++rc.numTrue;
    }

    public static void ReportThreadStats()
    {
        lock (mutex)
        {
            if (threadLongs == null || threadLongs.Length < nLongSlots ||
                threadDoubles == null || threadDoubles.Length < nDoubleSlots)
                GrowSlots();
            // StatRegisterer::CallCallbacks()
            foreach (Stat s in stats)
                s.Report(statsAccumulator, threadLongs!, threadDoubles!);
            if (threadRareChecks != null)
                foreach (RareCheckCounts rc in threadRareChecks.Values)
                {
                    statsAccumulator.ReportRareCheck(rc.condition, rc.freq, rc.numTrue, rc.total);
                    rc.numTrue = rc.total = 0;
                }
            if (pixelStatsEnabled)
            {
                statsAccumulator.AccumulatePixelStats(TSS.accum);
                TSS.accum = new PixelStatsAccumulator();
            }
        }
    }

    public static void StatsReportPixelStart(Point2i p)
    {
        if (!pixelStatsEnabled)
            return;
        ThreadStatsState tss = TSS;
        CHECK(tss.active == false);
        tss.active = true;
        tss.p = p;
        tss.start = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public static void StatsReportPixelEnd(Point2i p)
    {
        if (!pixelStatsEnabled)
            return;

        ThreadStatsState tss = TSS;
        CHECK(tss.active == true && tss.p == p);
        tss.active = false;

        float deltaMS = (float)System.Diagnostics.Stopwatch.GetElapsedTime(tss.start).TotalMilliseconds;
        tss.accum.ReportPixelMS(p, deltaMS);

        // StatRegisterer::CallPixelCallbacks()
        if (threadLongs == null || threadLongs.Length < nLongSlots)
            GrowSlots();
        for (int i = 0; i < pixelStats.Count; ++i)
            pixelStats[i].ReportPixel(p, i, tss.accum, threadLongs!);
    }

    public static void StatsEnablePixelStats(Bounds2i b, string baseName)
    {
        pixelStatsEnabled = true;
        imageBounds = b;
        pixelStatsBaseName = baseName;
    }

    public static void PrintStats(TextWriter dest)
    {
        lock (mutex)
            statsAccumulator.Print(dest);
    }

    public static bool PrintCheckRare(TextWriter dest)
    {
        lock (mutex)
            return statsAccumulator.PrintCheckRare(dest);
    }

    public static void ClearStats()
    {
        lock (mutex)
            statsAccumulator.Clear();
    }

    public static void StatsWritePixelImages() => statsAccumulator.WritePixelImages();
}

public static partial class Util
{
    // From util/memory.h: the process's resident set size.
    public static long GetCurrentRSS() => Environment.WorkingSet;
}

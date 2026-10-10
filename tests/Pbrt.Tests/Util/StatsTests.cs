// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the port of src/pbrt/util/stats.h (pbrt has no stats tests).

namespace Pbrt.Tests;

public class StatsTests
{
    private static readonly StatCounter counter = new("Test/Things counted");
    private static readonly StatMemoryCounter memory = new("Test/Bytes used");
    private static readonly StatPercent percent = new("Test/Hits");
    private static readonly StatRatio ratio = new("Test/Per thing");
    private static readonly StatIntDistribution intDistrib = new("Test/Int distribution");
    private static readonly StatFloatDistribution floatDistrib = new("Test/Float distribution");

    [Fact]
    public void ReportAndPrint()
    {
        // Four threads that each report their own values when done, as
        // pbrt's worker threads do via ForEachThread(ReportThreadStats).
        // (Statistics are process-wide; this test is the only user of these.)
        Thread[] threads = new Thread[4];
        for (int t = 0; t < threads.Length; ++t)
        {
            int t0 = t;
            threads[t] = new Thread(() =>
            {
                for (int i = 250 * t0; i < 250 * (t0 + 1); ++i)
                {
                    counter.Inc();
                    percent.Denom();
                    if (i % 4 == 0)
                        percent.Num();
                    ratio.Num(3);
                    ratio.Denom();
                    intDistrib.Add(i);
                    floatDistrib.Add(i * .5);
                }
                Stats.ReportThreadStats();
            });
            threads[t].Start();
        }
        foreach (Thread t in threads)
            t.Join();
        memory.Add(3 * 1024 * 1024);
        // (With one sample, pbrt's variance estimate is 0 / 0 and nothing is
        // reported.)
        for (int i = 0; i < 10; ++i)
            CHECK_RARE(1e-6, true);
        for (int i = 0; i < 1000; ++i)
            CHECK_RARE(.5, false);
        Stats.ReportThreadStats();

        StringWriter w = new();
        Stats.PrintStats(w);
        string s = w.ToString();
        string line(string title, string rest) => $"    {title,-42}{rest}\n";
        Assert.Contains("  Test\n", s);
        Assert.Contains(line("Things counted", $"{"",15}{1000,12}"), s);
        Assert.Contains(line("Bytes used", $"{"",18}{"3.00",9} MiB"), s);
        Assert.Contains(line("Hits", $"{250,12} / {1000,12} (25.00%)"), s);
        Assert.Contains(line("Per thing", $"{3000,12} / {1000,12} (3.00x)"), s);
        Assert.Contains(line("Int distribution", $"{"",22}499.500 avg [range 0 - 999]"), s);
        Assert.Contains(line("Float distribution", $"{"",22}249.750 avg [range 0.000000 - 499.500000]"), s);

        StringWriter rare = new();
        Assert.True(Stats.PrintCheckRare(rare));
        Assert.Contains("CHECK_RARE failed: true @ 1 failures", rare.ToString());
        Assert.DoesNotContain("false", rare.ToString());

        // Reported values are cleared from the threads.
        Stats.ClearStats();
        Stats.ReportThreadStats();
        w = new();
        Stats.PrintStats(w);
        Assert.DoesNotContain("Things counted", w.ToString());
    }
}

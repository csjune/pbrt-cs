// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/parallel_test.cpp

namespace Pbrt.Tests;

public class ParallelTests
{
    [Fact]
    public void Basics()
    {
        int counter = 0;
        ParallelFor(0, 1000, (long _) => Interlocked.Increment(ref counter));
        Assert.Equal(1000, counter);

        counter = 0;
        ParallelFor(10, 1010, (long start, long end) =>
        {
            Assert.True(end > start);
            Assert.True(start >= 10 && start < 1010);
            Assert.True(end > 10 && end <= 1010);
            for (long i = start; i < end; ++i)
                Interlocked.Increment(ref counter);
        });
        Assert.Equal(1000, counter);

        counter = 0;
        ParallelFor2D(new Bounds2i(new Point2i(0, 0), new Point2i(15, 14)),
                      (Point2i p) => Interlocked.Increment(ref counter));
        Assert.Equal(15 * 14, counter);
    }

    [Fact]
    public void DoNothing()
    {
        int counter = 0;
        ParallelFor(0, 0, (long _) => Interlocked.Increment(ref counter));
        Assert.Equal(0, counter);

        counter = 0;
        ParallelFor2D(new Bounds2i(new Point2i(0, 0), new Point2i(0, 0)),
                      (Bounds2i b) => Interlocked.Increment(ref counter));
        Assert.Equal(0, counter);
    }

    [Fact]
    public void ForEachThread()
    {
        int count = RunningThreads();
        Util.ForEachThread(() => Interlocked.Decrement(ref count));
        Assert.Equal(0, count);
    }

    // Not in parallel_test.cpp: checks for the port.

    [Fact]
    public void AtomicAdds()
    {
        AtomicFloat[] af = [new(0)];
        AtomicDouble[] ad = [new(0)];
        ParallelFor(0, 10000, (long _) =>
        {
            af[0].Add(1);
            ad[0].Add(0.5);
        });
        Assert.Equal(10000f, af[0]);
        Assert.Equal(5000.0, ad[0]);
    }

    [Fact]
    public void NestedAndAsync()
    {
        // Parallel loops started from inside parallel loops and async jobs
        // must not deadlock.
        long sum = 0;
        ParallelFor(0, 16, (long i) =>
        {
            ParallelFor(0, 100, (long j) => Interlocked.Add(ref sum, j));
        });
        Assert.Equal(16 * 4950, sum);

        AsyncJob<int> job = RunAsync(() =>
        {
            int inner = 0;
            ParallelFor(0, 50, (long _) => Interlocked.Increment(ref inner));
            return inner;
        });
        Assert.Equal(50, job.GetResult());
        Assert.True(job.IsReady());

        object extMutex = new();
        AsyncJob<string> sjob = RunAsync(() => "done");
        string? r;
        lock (extMutex)
        {
            while (!sjob.TryGetResult(extMutex, out r))
                ;
        }
        Assert.Equal("done", r);
    }
}

public class ThreadLocalTests
{
    [Fact]
    public void Consistency()
    {
        using ThreadLocal<int> tids = new(() => Environment.CurrentManagedThreadId, trackAllValues: true);

        long dummy = 0;
        void busywork(long index)
        {
            // Do some busy work to burn some time
            // Use the index to do a varying amount of computation
            long result = 0;
            for (int i = 0; i <= index; ++i)
                for (int j = 0; j <= index; ++j)
                    result += i * j;
            // Store result in atomic to prevent optimization
            Interlocked.Add(ref dummy, result);
        }

        ParallelFor(0, 1000, (long index) =>
        {
            Assert.Equal(Environment.CurrentManagedThreadId, tids.Value);
            busywork(index);
        });

        var jobs = new List<AsyncJob<int>>();
        for (int i = 0; i < 100; ++i)
        {
            int ii = i;
            jobs.Add(RunAsync(() =>
            {
                Assert.Equal(Environment.CurrentManagedThreadId, tids.Value);
                busywork(ii);
                return ii;
            }));
        }
        for (int i = 0; i < 100; ++i)
            _ = jobs[i].GetResult();

        ParallelFor(0, 1000, (long index) =>
        {
            Assert.Equal(Environment.CurrentManagedThreadId, tids.Value);
            busywork(index);
        });

        // Not in parallel_test.cpp: there's one value for each thread.
        var seen = new List<int>(tids.Values);
        // (There can be more than RunningThreads() of them: threads waiting
        // in other ParallelFor() calls also help with this loop.)
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Contains(Environment.CurrentManagedThreadId, seen);
    }
}

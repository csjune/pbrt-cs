// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/parallel.h and parallel.cpp
//
// std::mutex + std::condition_variable become a Monitor on an object
// (Monitor.Wait/PulseAll). std::unique_lock, which pbrt passes to
// ParallelJob::RunStep() so the job can release the lock, is UniqueLock.
//
// pbrt's ThreadLocal and Barrier are System.Threading's. ThreadPool has the
// same name as System.Threading's; inside namespace Pbrt the pbrt one wins.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Pbrt;

// std::unique_lock<std::mutex> over a Monitor.
public sealed class UniqueLock
{
    private readonly object mutex;
    private bool owns;

    public UniqueLock(object mutex)
    {
        this.mutex = mutex;
        Lock();
    }

    public object Mutex => mutex;
    public bool OwnsLock() => owns;

    public void Lock()
    {
        Monitor.Enter(mutex);
        owns = true;
    }

    public void Unlock()
    {
        owns = false;
        Monitor.Exit(mutex);
    }

    // condition_variable::wait(lock)
    public void Wait()
    {
        DCHECK(owns);
        Monitor.Wait(mutex);
    }
}

// AtomicFloat Definition
//
// A struct like pbrt's: keep it in an array element or a (non-readonly)
// field and call Add()/Set() on that storage, not on a copy. pbrt's
// operator= is Set().
//
// pbrt keeps the value in a std::atomic<FloatBits>, which is 64 bits when
// Float is double. Its float methods then mix float and double bits: the
// constructor and operator= store a float's 32 bits there (so a nonzero
// value reads back as a tiny double), Add() reads the bits as a double,
// adds in double precision and stores the double's bits, and reading
// converts that double to float. Since pbrt only stores zeros (SPPM's
// Phi_i), the effect is that sums accumulate in double; this does the same.
public struct AtomicFloat
{
#if PBRT_FLOAT_AS_DOUBLE
    // AtomicFloat Private Members
    private ulong bits;

    // AtomicFloat Public Methods
    public AtomicFloat(float v = 0)
    {
        bits = FloatToBits(v);
    }

    public static implicit operator float(in AtomicFloat a) =>
        (float)BitsToFloat(Volatile.Read(ref Unsafe.AsRef(in a.bits)));

    public Float Set(float v)
    {
        Volatile.Write(ref bits, FloatToBits(v));
        return v;
    }

    public void Add(float v)
    {
        ulong oldBits = Volatile.Read(ref bits), newBits;
        while (true)
        {
            newBits = FloatToBits(BitsToFloat(oldBits) + v);
            ulong seen = Interlocked.CompareExchange(ref bits, newBits, oldBits);
            if (seen == oldBits)
                break;
            oldBits = seen;
        }
    }

    public override readonly string ToString() => FormattableString.Invariant($"{(float)BitsToFloat(bits)}");
#else
    // AtomicFloat Private Members
    private uint bits;

    // AtomicFloat Public Methods
    public AtomicFloat(float v = 0)
    {
        bits = FloatToBits(v);
    }

    public static implicit operator float(in AtomicFloat a) => BitsToFloat(Volatile.Read(ref Unsafe.AsRef(in a.bits)));

    public float Set(float v)
    {
        Volatile.Write(ref bits, FloatToBits(v));
        return v;
    }

    public void Add(float v)
    {
        uint oldBits = Volatile.Read(ref bits), newBits;
        while (true)
        {
            newBits = FloatToBits(BitsToFloat(oldBits) + v);
            uint seen = Interlocked.CompareExchange(ref bits, newBits, oldBits);
            if (seen == oldBits)
                break;
            oldBits = seen;
        }
    }

    public override readonly string ToString() => FormattableString.Invariant($"{BitsToFloat(bits)}");
#endif
}

public struct AtomicDouble
{
    // AtomicDouble Private Data
    private ulong bits;

    // AtomicDouble Public Methods
    public AtomicDouble(double v = 0)
    {
        bits = FloatToBits(v);
    }

    public static implicit operator double(in AtomicDouble a) => BitsToFloat(Volatile.Read(ref Unsafe.AsRef(in a.bits)));

    public double Set(double v)
    {
        Volatile.Write(ref bits, FloatToBits(v));
        return v;
    }

    public void Add(double v)
    {
        ulong oldBits = Volatile.Read(ref bits), newBits;
        while (true)
        {
            newBits = FloatToBits(BitsToFloat(oldBits) + v);
            ulong seen = Interlocked.CompareExchange(ref bits, newBits, oldBits);
            if (seen == oldBits)
                break;
            oldBits = seen;
        }
    }

    public override readonly string ToString() => FormattableString.Invariant($"{BitsToFloat(bits)}");
}

// ParallelJob Definition
public abstract class ParallelJob
{
    // ParallelJob Public Methods
    public abstract bool HaveWork();
    public abstract void RunStep(UniqueLock @lock);

    public bool Finished() => !HaveWork() && activeWorkers == 0;

    public abstract override string ToString();

    // ParallelJob Public Members
    public static ThreadPool? threadPool;

    protected string BaseToString() => $"activeWorkers: {activeWorkers} removed: {removed}";

    // ParallelJob Private Members
    internal int activeWorkers = 0;
    internal ParallelJob? prev = null, next = null;
    internal bool removed = false;
}

// ThreadPool Definition
public sealed class ThreadPool : IDisposable
{
    // ThreadPool Private Members
    private readonly List<Thread> threads = [];
    // Also serves as the job list condition variable.
    private readonly object mutex = new();
    private bool shutdownThreads = false;
    private bool disabled = false;
    private ParallelJob? jobList = null;

    // ThreadPool Method Definitions
    public ThreadPool(int nThreads)
    {
        for (int i = 0; i < nThreads - 1; ++i)
        {
            Thread thread = new(Worker) { IsBackground = true, Name = $"pbrt worker {i + 1}" };
            threads.Add(thread);
            thread.Start();
        }
    }

    public int Size => threads.Count;

    private void Worker()
    {
        LOG_VERBOSE($"Started execution in worker thread");

        UniqueLock @lock = new(mutex);
        while (!shutdownThreads)
            WorkOrWait(@lock, false);
        @lock.Unlock();

        LOG_VERBOSE($"Exiting worker thread");
    }

    public UniqueLock AddToJobList(ParallelJob job)
    {
        UniqueLock @lock = new(mutex);
        // Add _job_ to head of _jobList_
        if (jobList != null)
            jobList.prev = job;
        job.next = jobList;
        jobList = job;

        Monitor.PulseAll(mutex);
        return @lock;
    }

    public void WorkOrWait(UniqueLock @lock, bool isEnqueuingThread)
    {
        DCHECK(@lock.OwnsLock());
        // Return if this is a worker thread and the thread pool is disabled
        if (!isEnqueuingThread && disabled)
        {
            @lock.Wait();
            return;
        }

        ParallelJob? job = jobList;
        while (job != null && !job.HaveWork())
            job = job.next;
        if (job != null)
        {
            // Execute work for _job_
            job.activeWorkers++;
            job.RunStep(@lock);
            // Handle post-job-execution details
            DCHECK(!@lock.OwnsLock());
            @lock.Lock();
            job.activeWorkers--;
            if (job.Finished())
                Monitor.PulseAll(mutex);
        }
        else
            // Wait for new work to arrive or the job to finish
            @lock.Wait();
    }

    public void RemoveFromJobList(ParallelJob job)
    {
        DCHECK(!job.removed);

        if (job.prev != null)
            job.prev.next = job.next;
        else
        {
            DCHECK(jobList == job);
            jobList = job.next;
        }
        if (job.next != null)
            job.next.prev = job.prev;

        job.removed = true;
    }

    public bool WorkOrReturn()
    {
        UniqueLock @lock = new(mutex);

        ParallelJob? job = jobList;
        while (job != null && !job.HaveWork())
            job = job.next;
        if (job == null)
        {
            @lock.Unlock();
            return false;
        }

        // Execute work for _job_
        job.activeWorkers++;
        job.RunStep(@lock);
        DCHECK(!@lock.OwnsLock());
        @lock.Lock();
        job.activeWorkers--;
        if (job.Finished())
            Monitor.PulseAll(mutex);
        @lock.Unlock();

        return true;
    }

    public void ForEachThread(Action func)
    {
        using Barrier barrier = new(threads.Count + 1);

        ParallelFor(0, threads.Count + 1, (long _) =>
        {
            func();
            barrier.SignalAndWait();
        });
    }

    public void Disable()
    {
        CHECK(!disabled);
        disabled = true;
        CHECK(jobList == null);  // Nothing should be running when Disable() is called.
    }

    public void Reenable()
    {
        CHECK(disabled);
        disabled = false;
    }

    // ~ThreadPool()
    public void Dispose()
    {
        if (threads.Count == 0)
            return;

        lock (mutex)
        {
            shutdownThreads = true;
            Monitor.PulseAll(mutex);
        }

        foreach (Thread thread in threads)
            thread.Join();
        threads.Clear();
    }

    public override string ToString()
    {
        var s = new StringBuilder($"[ ThreadPool threads.size(): {threads.Count} shutdownThreads: {shutdownThreads} ");
        if (Monitor.TryEnter(mutex))
        {
            s.Append("jobList: [ ");
            ParallelJob? job = jobList;
            while (job != null)
            {
                s.Append(job.ToString()).Append(' ');
                job = job.next;
            }
            s.Append("] ");
            Monitor.Exit(mutex);
        }
        else
            s.Append("(job list mutex locked) ");
        return s.Append(']').ToString();
    }
}

// AsyncJob Definition
public sealed class AsyncJob<T> : ParallelJob
{
    // AsyncJob Private Members
    private readonly Func<T> func;
    private bool started = false;
    private bool hasResult = false;
    private T result = default!;
    private ExceptionDispatchInfo? exception;
    // Also serves as the condition variable.
    private readonly object mutex = new();

    // AsyncJob Public Methods
    public AsyncJob(Func<T> w)
    {
        func = w;
    }

    public override bool HaveWork() => !started;

    public override void RunStep(UniqueLock @lock)
    {
        threadPool!.RemoveFromJobList(this);
        started = true;
        @lock.Unlock();
        // Execute asynchronous work and notify waiting threads of its completion
        Run();
    }

    // (An exception from _func_, such as ErrorExit()'s, is rethrown to the
    // threads that wait for the result; in pbrt it ends the program.)
    private void Run()
    {
        T r = default!;
        ExceptionDispatchInfo? e = null;
        try
        {
            r = func();
        }
        catch (Exception ex)
        {
            e = ExceptionDispatchInfo.Capture(ex);
        }
        lock (mutex)
        {
            CHECK(!hasResult);
            result = r;
            exception = e;
            hasResult = true;
            Monitor.PulseAll(mutex);
        }
    }

    public bool IsReady()
    {
        lock (mutex)
            return hasResult;
    }

    public T GetResult()
    {
        Wait();
        lock (mutex)
            return result;
    }

    private void ThrowIfFailed() => exception?.Throw();

    // Returns the result if it's ready; otherwise releases _extMutex_ (a
    // Monitor the caller holds) while helping with parallel work.
    public bool TryGetResult(object extMutex, [MaybeNullWhen(false)] out T r)
    {
        lock (mutex)
        {
            if (hasResult)
            {
                ThrowIfFailed();
                r = result;
                return true;
            }
        }

        Monitor.Exit(extMutex);
        DoParallelWork();
        Monitor.Enter(extMutex);
        r = default;
        return false;
    }

    public void Wait()
    {
        while (!IsReady() && DoParallelWork())
            ;
        lock (mutex)
        {
            while (!hasResult)
                Monitor.Wait(mutex);
            ThrowIfFailed();
        }
    }

    public void DoWork() => Run();

    public override string ToString() => $"[ AsyncJob started: {started} ]";
}

// ParallelForLoop1D Definition
internal sealed class ParallelForLoop1D : ParallelJob
{
    // ParallelForLoop1D Private Members
    private readonly Action<long, long> func;
    private long nextIndex;
    private readonly long endIndex;
    private readonly int chunkSize;

    // ParallelForLoop1D Public Methods
    public ParallelForLoop1D(long startIndex, long endIndex, int chunkSize, Action<long, long> func)
    {
        this.func = func;
        nextIndex = startIndex;
        this.endIndex = endIndex;
        this.chunkSize = chunkSize;
    }

    public override bool HaveWork() => nextIndex < endIndex;

    // ParallelForLoop1D Method Definitions
    public override void RunStep(UniqueLock @lock)
    {
        // Determine the range of loop iterations to run in this step
        long indexStart = nextIndex;
        long indexEnd = Math.Min(indexStart + chunkSize, endIndex);
        nextIndex = indexEnd;

        // Remove job from list if all work has been started
        if (!HaveWork())
            threadPool!.RemoveFromJobList(this);

        // Release lock and execute loop iterations in _[indexStart, indexEnd)_
        @lock.Unlock();
        func(indexStart, indexEnd);
    }

    public override string ToString() =>
        $"[ ParallelForLoop1D nextIndex: {nextIndex} endIndex: {endIndex} chunkSize: {chunkSize} ]";
}

internal sealed class ParallelForLoop2D : ParallelJob
{
    private readonly Action<Bounds2i> func;
    private readonly Bounds2i extent;
    private Point2i nextStart;
    private readonly int chunkSize;

    public ParallelForLoop2D(Bounds2i extent, int chunkSize, Action<Bounds2i> func)
    {
        this.func = func;
        this.extent = extent;
        nextStart = extent.pMin;
        this.chunkSize = chunkSize;
    }

    public override bool HaveWork() => nextStart.y < extent.pMax.y;

    public override void RunStep(UniqueLock @lock)
    {
        // Compute extent for this step
        Point2i end = nextStart + new Vector2i(chunkSize, chunkSize);
        Bounds2i b = Intersect(new Bounds2i(nextStart, end), extent);
        CHECK(!b.IsEmpty());

        // Advance to be ready for the next extent.
        nextStart.x += chunkSize;
        if (nextStart.x >= extent.pMax.x)
        {
            nextStart.x = extent.pMin.x;
            nextStart.y += chunkSize;
        }

        if (!HaveWork())
            threadPool!.RemoveFromJobList(this);

        @lock.Unlock();

        // Run the loop iteration
        func(b);
    }

    public override string ToString() =>
        $"[ ParallelForLoop2D extent: {extent} nextStart: {nextStart} chunkSize: {chunkSize} ]";
}

public static partial class Util
{
    // Parallel Function Definitions
    public static void ParallelFor(long start, long end, Action<long, long> func)
    {
        CHECK(ParallelJob.threadPool != null);
        if (start == end)
            return;
        // Compute chunk size for parallel loop
        long chunkSize = Math.Max(1, (end - start) / (8 * RunningThreads()));

        // Create and enqueue _ParallelForLoop1D_ for this loop
        ParallelForLoop1D loop = new(start, end, (int)chunkSize, func);
        UniqueLock @lock = ParallelJob.threadPool.AddToJobList(loop);

        // Help out with parallel loop iterations in the current thread
        while (!loop.Finished())
            ParallelJob.threadPool.WorkOrWait(@lock, true);
        @lock.Unlock();
    }

    // Parallel Inline Functions
    public static void ParallelFor(long start, long end, Action<long> func) =>
        ParallelFor(start, end, (long start, long end) =>
        {
            for (long i = start; i < end; ++i)
                func(i);
        });

    // Callers pass a lambda with a typed parameter, (Bounds2i b) => ... or
    // (Point2i p) => ..., to pick the overload.
    public static void ParallelFor2D(Bounds2i extent, Action<Bounds2i> func)
    {
        CHECK(ParallelJob.threadPool != null);

        if (extent.IsEmpty())
            return;
        if (extent.Area() == 1)
        {
            func(extent);
            return;
        }

        // Want at least 8 tiles per thread, subject to not too big and not too
        // small.
        // TODO: should we do non-square?
        int tileSize = Clamp((int)Math.Sqrt(extent.Diagonal().x * extent.Diagonal().y /
                                             (8 * RunningThreads())),
                             1, 32);

        ParallelForLoop2D loop = new(extent, tileSize, func);
        UniqueLock @lock = ParallelJob.threadPool.AddToJobList(loop);

        // Help out with parallel loop iterations in the current thread
        while (!loop.Finished())
            ParallelJob.threadPool.WorkOrWait(@lock, true);
        @lock.Unlock();
    }

    public static void ParallelFor2D(Bounds2i extent, Action<Point2i> func) =>
        ParallelFor2D(extent, (Bounds2i b) =>
        {
            foreach (Point2i p in b)
                func(p);
        });

    public static int AvailableCores() => Math.Max(1, Environment.ProcessorCount);

    public static int RunningThreads() =>
        ParallelJob.threadPool != null ? (1 + ParallelJob.threadPool.Size) : 1;

    public static void ParallelInit(int nThreads = -1)
    {
        CHECK(ParallelJob.threadPool == null);
        if (nThreads <= 0)
            nThreads = AvailableCores();
        ParallelJob.threadPool = new ThreadPool(nThreads);
    }

    public static void ParallelCleanup()
    {
        ParallelJob.threadPool?.Dispose();
        ParallelJob.threadPool = null;
    }

    public static bool DoParallelWork()
    {
        CHECK(ParallelJob.threadPool != null);
        // lock should be held when this is called...
        return ParallelJob.threadPool.WorkOrReturn();
    }

    public static void ForEachThread(Action func) => ParallelJob.threadPool?.ForEachThread(func);

    public static void DisableThreadPool()
    {
        CHECK(ParallelJob.threadPool != null);
        ParallelJob.threadPool.Disable();
    }

    public static void ReenableThreadPool()
    {
        CHECK(ParallelJob.threadPool != null);
        ParallelJob.threadPool.Reenable();
    }

    // Asynchronous Task Launch Function Definitions
    //
    // pbrt binds extra arguments with std::bind; capture them in the lambda
    // instead.
    public static AsyncJob<R> RunAsync<R>(Func<R> func)
    {
        // Create _AsyncJob_ for _func_
        AsyncJob<R> job = new(func);

        // Enqueue _job_ or run it immediately
        if (RunningThreads() == 1)
            job.DoWork();
        else
            ParallelJob.threadPool!.AddToJobList(job).Unlock();

        return job;
    }
}

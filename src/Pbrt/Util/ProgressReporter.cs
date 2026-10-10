// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/progressreporter.h and progressreporter.cpp
// (without the GPU event tracking). pbrt's destructor calls Done(); here
// ProgressReporter is IDisposable.

using System.Diagnostics;
using System.Text;

namespace Pbrt;

// Timer Definition
public sealed class Timer
{
    private readonly long start = Stopwatch.GetTimestamp();

    public double ElapsedSeconds() => Stopwatch.GetElapsedTime(start).TotalSeconds;

    public override string ToString() =>
        $"[ Timer start(ns): {(long)(start * (1e9 / Stopwatch.Frequency))} ]";
}

// ProgressReporter Definition
public sealed class ProgressReporter : IDisposable
{
    // ProgressReporter Private Members
    private readonly long totalWork;
    private readonly string title = "";
    private readonly bool quiet;
    private readonly Timer timer = new();
    private long workDone;
    private int exitThread;
    private readonly Thread? updateThread;
    private float? finishTime;
    private readonly TextWriter output;

    // ProgressReporter Public Methods
    public ProgressReporter()
    {
        quiet = true;
        output = Console.Out;
    }

    // _output_ is where the bar is printed (stdout by default).
    public ProgressReporter(long totalWork, string title, bool quiet, TextWriter? output = null)
    {
        this.totalWork = Math.Max(1, totalWork);
        this.title = title;
        this.quiet = quiet;
        this.output = output ?? Console.Out;
        workDone = 0;
        exitThread = 0;

        // Launch thread to periodically update progress bar
        if (!quiet)
        {
            updateThread = new Thread(printBar) { IsBackground = true, Name = "pbrt progress bar" };
            updateThread.Start();
        }
    }

    public void Dispose() => Done();

    // ProgressReporter Inline Method Definitions
    public double ElapsedSeconds() => finishTime ?? timer.ElapsedSeconds();

    public void Update(long num = 1)
    {
        if (num == 0 || quiet)
            return;
        Interlocked.Add(ref workDone, num);
    }

    // ProgressReporter Method Definitions
    private void printBar()
    {
        int barLength = TerminalWidth() - 28;
        int totalPlusses = Math.Max(2, barLength - title.Length);
        int plussesPrinted = 0;

        // Initialize progress string
        StringBuilder buf = new();
        buf.Append($"\r{title}: [");
        int curSpace = buf.Length;
        buf.Append(' ', totalPlusses);
        buf.Append("] ");
        output.Write(buf.ToString());
        output.Flush();

        int sleepDuration = 250;

        int iterCount = 0;
        bool reallyExit = false;  // make sure we do one more go-round to get the final report
        while (!reallyExit)
        {
            if (Volatile.Read(ref exitThread) != 0)
                reallyExit = true;
            else
                Thread.Sleep(sleepDuration);

            // Periodically increase sleepDuration to reduce overhead of
            // updates.
            ++iterCount;
            if (iterCount == 10)
                // Up to 0.5s after ~2.5s elapsed
                sleepDuration *= 2;
            else if (iterCount == 70)
                // Up to 1s after an additional ~30s have elapsed.
                sleepDuration *= 2;
            else if (iterCount == 520)
                // After 15m, jump up to 5s intervals
                sleepDuration *= 5;

            Float percentDone = (Float)Interlocked.Read(ref workDone) / (Float)totalWork;
            int plussesNeeded = (int)FMath.Round(totalPlusses * percentDone, MidpointRounding.AwayFromZero);
            while (plussesPrinted < plussesNeeded)
            {
                buf[curSpace++] = '+';
                ++plussesPrinted;
            }
            output.Write(buf.ToString());

            // Update elapsed time and estimated time to completion
            Float elapsed = (Float)ElapsedSeconds();
            Float estRemaining = elapsed / percentDone - elapsed;
            if (Volatile.Read(ref exitThread) != 0)
                output.Write(FormattableString.Invariant($" ({finishTime:F1}s)       "));
            else if (percentDone == 1f)
                output.Write(FormattableString.Invariant($" ({elapsed:F1}s)       "));
            else if (!IsInf(estRemaining))
                output.Write(FormattableString.Invariant($" ({elapsed:F1}s|{Max<Float>(0, estRemaining):F1}s)  "));
            else
                output.Write(FormattableString.Invariant($" ({elapsed:F1}s|?s)  "));
            output.Flush();
        }
    }

    public void Done()
    {
        if (!quiet)
        {
            // Only let one thread shut things down.
            finishTime = (float)ElapsedSeconds();
            if (Interlocked.CompareExchange(ref exitThread, 1, 0) == 0)
            {
                Interlocked.Exchange(ref workDone, totalWork);
                updateThread?.Join();
                output.Write("\n");
            }
        }
    }

    public override string ToString() =>
        $"[ ProgressReporter totalWork: {totalWork} title: {title} timer: {timer} workDone: {Interlocked.Read(ref workDone)} exitThread: {exitThread != 0}";

    private static int TerminalWidth()
    {
        try
        {
            if (Console.IsOutputRedirected)
                return 80;
            int w = Console.WindowWidth;
            return w > 0 ? w : 80;
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            return 80;
        }
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/log.h and log.cpp
//
// LOG_VERBOSE($"...") and LOG_ERROR($"...") take an interpolated string;
// like pbrt's macros, the message isn't formatted when its level is
// disabled (the interpolated string handlers below check first). LOG_FATAL
// is in Check.cs. Thread ids in log lines are managed thread ids (pbrt
// prints the OS thread id). The GPU logging isn't ported.

using System.Diagnostics;
using System.Text;

namespace Pbrt;

// LogLevel Definition
public enum LogLevel { Verbose, Error, Fatal, Invalid }

// LogLevel Global Variable Declaration
public static class Logging
{
    public static LogLevel logLevel = LogLevel.Error;
    public static TextWriter? logFile;

    internal static readonly object mutex = new();
    private static readonly Stopwatch start = Stopwatch.StartNew();

    internal static float ElapsedSeconds() => (float)(start.ElapsedTicks / (double)Stopwatch.Frequency);
}

// Builds a log message only if its level is enabled. (One handler type per
// level, since a static method's handler can't take the level as an
// argument.)
public interface ILogLevel
{
    static abstract LogLevel Level { get; }
}

public readonly struct VerboseLevel : ILogLevel
{
    public static LogLevel Level => LogLevel.Verbose;
}

public readonly struct ErrorLevel : ILogLevel
{
    public static LogLevel Level => LogLevel.Error;
}

[InterpolatedStringHandler]
public ref struct LogInterpolatedStringHandler<L> where L : ILogLevel
{
    private DefaultInterpolatedStringHandler inner;
    private readonly bool enabled;

    public LogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
    {
        enabled = isEnabled = L.Level >= Logging.logLevel;
        inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount,
                                                               System.Globalization.CultureInfo.InvariantCulture)
                        : default;
    }

    public void AppendLiteral(string s) => inner.AppendLiteral(s);
    public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string? format) => inner.AppendFormatted(value, format);
    public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);
    public void AppendFormatted<T>(T value, int alignment, string? format) => inner.AppendFormatted(value, alignment, format);
    public void AppendFormatted(ReadOnlySpan<char> value) => inner.AppendFormatted(value);
    public void AppendFormatted(string? value) => inner.AppendFormatted(value);

    internal string ToStringAndClear() => enabled ? inner.ToStringAndClear() : "";
}

public static partial class Util
{
    public static LogLevel LogLevelFromString(string s)
    {
        if (s == "verbose")
            return LogLevel.Verbose;
        else if (s == "error")
            return LogLevel.Error;
        else if (s == "fatal")
            return LogLevel.Fatal;
        return LogLevel.Invalid;
    }

    // Inside types with their own ToString(), call this as Util.ToString().
    public static string ToString(LogLevel level) => level switch
    {
        LogLevel.Verbose => "VERBOSE",
        LogLevel.Error => "ERROR",
        LogLevel.Fatal => "FATAL",
        _ => "UNKNOWN",
    };

    // Logging Macros
    public static void LOG_VERBOSE(ref LogInterpolatedStringHandler<VerboseLevel> message,
                                   [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (LogLevel.Verbose >= Logging.logLevel)
            Log(LogLevel.Verbose, file, line, message.ToStringAndClear());
    }

    public static void LOG_ERROR(ref LogInterpolatedStringHandler<ErrorLevel> message,
                                 [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (LogLevel.Error >= Logging.logLevel)
            Log(LogLevel.Error, file, line, message.ToStringAndClear());
    }

    // Logging Function Definitions
    public static void Log(LogLevel level, string file, int line, string s)
    {
        int len = s.Length;
        if (len == 0)
            return;
        string levelString = (level == LogLevel.Verbose) ? "" : (ToString(level) + " ");

        // cut off everything up to pbrt/ (here, src/)
        file = file.Replace('\\', '/');
        int fileStart = file.LastIndexOf("/src/", StringComparison.Ordinal);
        string shortfile = fileStart >= 0 ? file[(fileStart + 5)..] : file;

        string msg = FormattableString.Invariant(
            $"[ tid {Environment.CurrentManagedThreadId:D3} @ {Logging.ElapsedSeconds(),9:F3}s {shortfile}:{line} ] {levelString}{s}");
        lock (Logging.mutex)
        {
            if (Logging.logFile != null)
            {
                Logging.logFile.WriteLine(msg);
                Logging.logFile.Flush();
            }
            else
                Console.Error.WriteLine(msg);
        }
    }

    private static volatile bool shutdownLogUtilization;
    private static Thread? logUtilizationThread;

    public static void InitLogging(LogLevel level, string logFile, bool logUtilization, bool useGPU = false)
    {
        Logging.logLevel = level;
        if (logFile.Length != 0)
        {
            try
            {
                Logging.logFile = new StreamWriter(logFile, false, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ErrorExit($"{logFile}: {e.Message}");
            }
            Logging.logLevel = LogLevel.Verbose;
        }

        if (level == LogLevel.Invalid)
            ErrorExit("Invalid --log-level specified.");

        if (logUtilization)
        {
            shutdownLogUtilization = false;
            logUtilizationThread = new Thread(LogUtilization) { IsBackground = true, Name = "pbrt log utilization" };
            logUtilizationThread.Start();
        }
    }

    public static void ShutdownLogging()
    {
        if (logUtilizationThread != null)
        {
            shutdownLogUtilization = true;
            logUtilizationThread.Join();
            logUtilizationThread = null;
        }
        lock (Logging.mutex)
        {
            Logging.logFile?.Dispose();
            Logging.logFile = null;
        }
    }

    private struct Usage
    {
        // CPU usage in ticks
        public long user, nice, system, idle;
        // I/O in bytes: requested by process
        public long readRequest, writeRequest;
        // I/O in bytes: how much actually came from/went to storage
        public long readActual, writeActual;
    }

    // Return overall CPU usage in ticks. (pbrt reads /proc on Linux and uses
    // GetSystemTimes() on Windows; elsewhere, this is all zeros.)
    private static Usage GetCPUUsage()
    {
        Usage usage = default;
        if (!OperatingSystem.IsLinux())
            return usage;
        try
        {
            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (!line.StartsWith("cpu ", StringComparison.Ordinal))
                    continue;

                List<long> values = SplitStringToInt64s(line[5..], ' ');

                CHECK_GE(values.Count, 4);
                usage.user = values[0];
                usage.nice = values[1];
                usage.system = values[2];
                usage.idle = values[3];

                break;
            }

            foreach (string line in File.ReadLines("/proc/self/io"))
            {
                if (line.StartsWith("rchar: ", StringComparison.Ordinal))
                    CHECK(Atoi(line[7..], out usage.readRequest));
                if (line.StartsWith("wchar: ", StringComparison.Ordinal))
                    CHECK(Atoi(line[7..], out usage.writeRequest));
                if (line.StartsWith("read_bytes: ", StringComparison.Ordinal))
                    CHECK(Atoi(line[12..], out usage.readActual));
                if (line.StartsWith("write_bytes: ", StringComparison.Ordinal))
                    CHECK(Atoi(line[13..], out usage.writeActual));
            }
        }
        catch (IOException)
        {
        }
        return usage;
    }

    private static void LogUtilization()
    {
        Usage prevUsage = GetCPUUsage();

        while (!shutdownLogUtilization)
        {
            Thread.Sleep(100);

            Usage currentUsage = GetCPUUsage();

            // Report activity since last logging event. A value of 1
            // represents all cores running at 100% utilization.
            long delta = (currentUsage.user + currentUsage.nice + currentUsage.system + currentUsage.idle) -
                         (prevUsage.user + prevUsage.nice + prevUsage.system + prevUsage.idle);
            LOG_VERBOSE($"CPU: Memory used {GetCurrentRSS() / (1024 * 1024)} MB. Core activity: {(double)(currentUsage.user - prevUsage.user) / delta:F4} user {(double)(currentUsage.nice - prevUsage.nice) / delta:F4} nice {(double)(currentUsage.system - prevUsage.system) / delta:F4} system {(double)(currentUsage.idle - prevUsage.idle) / delta:F4} idle");
            LOG_VERBOSE($"IO: read request {currentUsage.readRequest - prevUsage.readRequest} read actual {currentUsage.readActual - prevUsage.readActual} write request {currentUsage.writeRequest - prevUsage.writeRequest} write actual {currentUsage.writeActual - prevUsage.writeActual}");

            prevUsage = currentUsage;
        }
    }
}

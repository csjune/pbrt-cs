// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/check.h
//
// CHECK* are always evaluated; DCHECK* are compiled out (arguments included)
// unless DEBUG is defined, matching pbrt's NDEBUG behavior.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Pbrt;

public sealed class CheckFailedException(string message) : Exception(message);

public sealed class FatalErrorException(string message) : Exception(message);

public static partial class Util
{
    // LOG_FATAL from util/log.h: as pbrt's LogFatal(), this prints the
    // message and the CheckCallbackScope messages; then it throws where
    // pbrt aborts.
    [DoesNotReturn]
    public static void LOG_FATAL(string message, [CallerFilePath] string file = "",
                                 [CallerLineNumber] int line = 0)
    {
        LogFatal(file, line, message);
        throw new FatalErrorException(message);
    }

    // pbrt's LogFatal(), but for the abort()
    private static void LogFatal(string file, int line, string message)
    {
        lock (logFatalMutex)
        {
            // cut off everything up to pbrt/ (here, src/)
            file = file.Replace('\\', '/');
            int fileStart = file.LastIndexOf("/src/", StringComparison.Ordinal);
            string shortfile = fileStart >= 0 ? file[(fileStart + 5)..] : file;
            Console.Error.WriteLine(FormattableString.Invariant(
                $"[ tid {Environment.CurrentManagedThreadId:D3} @ {Logging.ElapsedSeconds(),9:F3}s {shortfile}:{line} ] FATAL {message}"));
            // (CheckCallbackScope::Fail() without the stack trace; an
            // unhandled exception prints its own.)
            string callbackMessage = CheckCallbackScope.Message();
            if (callbackMessage.Length > 0)
                Console.Error.Write(callbackMessage + "\n\n");
        }
    }

    private static readonly object logFatalMutex = new();

    // CHECK_RARE(freq, condition): records how often _condition_ is true;
    // the statistics report flags it if that's more often than _freq_.
    public static void CHECK_RARE(double freq, bool condition,
                                  [CallerArgumentExpression(nameof(condition))] string? expr = null,
                                  [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Stats.RecordRareCheck(freq, condition, expr, file, line);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fail(string message, string file, int line)
    {
        LogFatal(file, line, message);
        throw new CheckFailedException(message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailOp<T>(string? ea, string? eb, string op, T a, T b, string file, int line) =>
        Fail($"Check failed: {ea} {op} {eb} with {ea} = {a}, {eb} = {b}", file, line);

    public static void CHECK([DoesNotReturnIf(false)] bool cond,
                             [CallerArgumentExpression(nameof(cond))] string? expr = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (!cond) Fail($"Check failed: {expr}", file, line);
    }

    public static void CHECK_EQ<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IEqualityOperators<T, T, bool>
    {
        if (!(a == b)) FailOp(ea, eb, "==", a, b, file, line);
    }

    public static void CHECK_NE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IEqualityOperators<T, T, bool>
    {
        if (!(a != b)) FailOp(ea, eb, "!=", a, b, file, line);
    }

    public static void CHECK_GT<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool>
    {
        if (!(a > b)) FailOp(ea, eb, ">", a, b, file, line);
    }

    public static void CHECK_GE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool>
    {
        if (!(a >= b)) FailOp(ea, eb, ">=", a, b, file, line);
    }

    public static void CHECK_LT<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool>
    {
        if (!(a < b)) FailOp(ea, eb, "<", a, b, file, line);
    }

    public static void CHECK_LE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                   [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool>
    {
        if (!(a <= b)) FailOp(ea, eb, "<=", a, b, file, line);
    }

    [Conditional("DEBUG")]
    public static void DCHECK(bool cond, [CallerArgumentExpression(nameof(cond))] string? expr = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        CHECK(cond, expr, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_EQ<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IEqualityOperators<T, T, bool> => CHECK_EQ(a, b, ea, eb, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_NE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IEqualityOperators<T, T, bool> => CHECK_NE(a, b, ea, eb, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_GT<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool> => CHECK_GT(a, b, ea, eb, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_GE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool> => CHECK_GE(a, b, ea, eb, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_LT<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool> => CHECK_LT(a, b, ea, eb, file, line);

    [Conditional("DEBUG")]
    public static void DCHECK_LE<T>(T a, T b, [CallerArgumentExpression(nameof(a))] string? ea = null,
                                    [CallerArgumentExpression(nameof(b))] string? eb = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : IComparisonOperators<T, T, bool> => CHECK_LE(a, b, ea, eb, file, line);
}

// CheckCallbackScope Definition
//
// pbrt's RAII class is IDisposable here (using CheckCallbackScope _ = new(...)).
public sealed class CheckCallbackScope : IDisposable
{
    // CheckCallbackScope Private Members
    private static readonly List<Func<string>> callbacks = [];

    // CheckCallbackScope Public Methods
    public CheckCallbackScope(Func<string> callback)
    {
        lock (callbacks)
            callbacks.Add(callback);
    }

    public void Dispose()
    {
        lock (callbacks)
        {
            CHECK_GT(callbacks.Count, 0);
            callbacks.RemoveAt(callbacks.Count - 1);
        }
    }

    // The message that pbrt's Fail() prints
    public static string Message()
    {
        string message = "";
        lock (callbacks)
            for (int i = callbacks.Count - 1; i >= 0; --i)
                message += callbacks[i]();
        return message;
    }
}

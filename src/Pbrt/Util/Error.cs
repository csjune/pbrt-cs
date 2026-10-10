// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/error.h and error.cpp
//
// pbrt's ErrorExit() prints the message and exits the process; here it
// prints it the same way and then throws FatalErrorException (like
// LOG_FATAL), so that library users and tests can catch it; the pbrt
// command exits when it gets one. Messages are formatted by the caller with
// string interpolation instead of printf-style arguments. LastError() isn't
// needed: .NET reports errors with exceptions; ErrorString() formats an I/O
// exception the way pbrt formats errno ("No such file or directory (2)").

using System.Diagnostics.CodeAnalysis;

namespace Pbrt;

// FileLoc Definition
public struct FileLoc
{
    public string filename = "";
    public int line = 1, column = 0;

    public FileLoc() { }

    public FileLoc(string filename)
    {
        this.filename = filename;
    }

    public override readonly string ToString() => $"{filename}:{line}:{column}";
}

public static partial class Util
{
    private static bool quiet = false;

    public static void SuppressErrorMessages() => quiet = true;

    private static readonly object errorMutex = new();
    private static string lastError = "";

    private static string processError(string errorType, FileLoc? loc, string message)
    {
        // Build up an entire formatted error string and print it all at once;
        // this way, if multiple threads are printing messages at once, they
        // don't get jumbled up...
        string errorString = Red(errorType);

        if (loc.HasValue)
            errorString += ": " + loc.Value.ToString();

        errorString += ": ";
        errorString += message;

        // Print the error message (but not more than one time).
        lock (errorMutex)
        {
            if (errorString != lastError)
            {
                Console.Error.WriteLine(errorString);
                LOG_VERBOSE($"{errorString}");
                lastError = errorString;
            }
        }
        return errorString;
    }

    // Error Reporting Function Definitions
    public static void Warning(string message) => Warning(null, message);

    public static void Warning(FileLoc? loc, string message)
    {
        if (quiet)
            return;
        processError("Warning", loc, message);
    }

    public static void Error(string message) => Error(null, message);

    public static void Error(FileLoc? loc, string message)
    {
        if (quiet)
            return;
        processError("Error", loc, message);
    }

    [DoesNotReturn]
    public static void ErrorExit(string message) => ErrorExit(null, message);

    [DoesNotReturn]
    public static void ErrorExit(FileLoc? loc, string message)
    {
        processError("Error", loc, message);
        DisconnectFromDisplayServer();
        // (The exception message is the error without the color codes.)
        throw new FatalErrorException(loc.HasValue ? $"Error: {loc.Value}: {message}" : $"Error: {message}");
    }

    public static string ErrorString(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "No such file or directory (2)",
        UnauthorizedAccessException => "Permission denied (13)",
        _ => e.Message
    };
}

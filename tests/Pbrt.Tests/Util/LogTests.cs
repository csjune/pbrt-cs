// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the ports of src/pbrt/util/log.h, error.h and progressreporter.h
// (pbrt has no tests for these).

namespace Pbrt.Tests;

// Logging and error settings are process-wide, so these tests don't run in
// parallel with each other.
[Collection("Logging")]
public class LogTests
{
    [Fact]
    public void LevelsAndFormatting()
    {
        Assert.Equal(LogLevel.Verbose, LogLevelFromString("verbose"));
        Assert.Equal(LogLevel.Fatal, LogLevelFromString("fatal"));
        Assert.Equal(LogLevel.Invalid, LogLevelFromString("chatty"));
        Assert.Equal("ERROR", Util.ToString(LogLevel.Error));

        StringWriter w = new();
        LogLevel old = Logging.logLevel;
        try
        {
            Logging.logFile = w;
            Logging.logLevel = LogLevel.Error;
            int evaluated = 0;
            int f() => ++evaluated;
            // Disabled levels don't format the message.
            LOG_VERBOSE($"not shown {f()}");
            Assert.Equal(0, evaluated);
            LOG_ERROR($"shown {f()}");
            Assert.Equal(1, evaluated);

            Logging.logLevel = LogLevel.Verbose;
            LOG_VERBOSE($"value {1.5}");
        }
        finally
        {
            Logging.logLevel = old;
            Logging.logFile = null;
        }

        // (Other tests running at the same time may log, too.)
        string[] lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.TrimEnd('\r'))
                                     .Where(l => l.Contains("LogTests.cs")).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^\[ tid \d{3,} @ +\d+\.\d{3}s .*LogTests.cs:\d+ \] ERROR shown 1$", lines[0]);
        Assert.Matches(@"^\[ tid \d{3,} @ +\d+\.\d{3}s .*LogTests.cs:\d+ \] value 1.5$", lines[1]);
    }

    [Fact]
    public void Errors()
    {
        FileLoc loc = new("scene.pbrt") { line = 12, column = 4 };
        Assert.Equal("scene.pbrt:12:4", loc.ToString());

        StringWriter err = new();
        TextWriter oldErr = Console.Error;
        Console.SetError(err);
        try
        {
            Warning(loc, "something odd");
            Warning(loc, "something odd");  // printed once
            Error("bad thing");
            FatalErrorException e = Assert.Throws<FatalErrorException>(() => ErrorExit(loc, "fatal"));
            Assert.Equal("Error: scene.pbrt:12:4: fatal", e.Message);
        }
        finally
        {
            Console.SetError(oldErr);
        }
        // (Other tests running at the same time may print, too.)
        string[] lines = err.ToString().Replace("\r\n", "\n").Split('\n');
        Assert.Single(lines, Red("Warning") + ": scene.pbrt:12:4: something odd");
        Assert.Single(lines, Red("Error") + ": bad thing");
        Assert.Single(lines, Red("Error") + ": scene.pbrt:12:4: fatal");
    }

    [Fact]
    public void ProgressBar()
    {
        StringWriter w = new();
        using (ProgressReporter progress = new(10, "Rendering", false, w))
        {
            for (int i = 0; i < 10; ++i)
                progress.Update();
        }
        string s = w.ToString();
        Assert.StartsWith("\rRendering: [", s);
        Assert.EndsWith("\n", s);
        // The final report has a full bar and the total time.
        string last = s[s.LastIndexOf('\r')..];
        Assert.Matches(@"^\rRendering: \[\++\]  \(\d+\.\ds\)", last);

        // Quiet reporters print nothing.
        ProgressReporter quiet = new(10, "Quiet", true, w);
        quiet.Update(5);
        quiet.Done();
        Assert.Equal(s, w.ToString());
    }
}

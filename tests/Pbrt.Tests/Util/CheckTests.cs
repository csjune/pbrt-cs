// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the port of src/pbrt/util/check.h (pbrt has no check tests).

namespace Pbrt.Tests;

// (In the Options collection since the tests redirect stderr.)
[Collection("Options")]
public class CheckTests
{
    private static string CaptureStderr(Action a)
    {
        TextWriter old = Console.Error;
        StringWriter sw = new();
        Console.SetError(sw);
        try
        {
            a();
        }
        finally
        {
            Console.SetError(old);
        }
        return sw.ToString();
    }

    [Fact]
    public void Checks()
    {
        CHECK(true);
        CHECK_EQ(1, 1);
        CHECK_LT(1, 2);
        int x = 3;
        string err = CaptureStderr(() => Assert.Equal("Check failed: x < 2 with x = 3, 2 = 2",
                                                      Assert.Throws<CheckFailedException>(() => CHECK_LT(x, 2)).Message));
        Assert.Contains("Util/CheckTests.cs:", err);
        Assert.Contains("] FATAL Check failed: x < 2 with x = 3, 2 = 2", err);
    }

    [Fact]
    public void CallbackScope()
    {
        string err;
        using (CheckCallbackScope outer = new(() => "outer\n"))
        {
            using (CheckCallbackScope inner = new(() => "inner\n"))
            {
                Assert.Equal("inner\nouter\n", CheckCallbackScope.Message());
                err = CaptureStderr(() => Assert.Throws<FatalErrorException>(() => LOG_FATAL("oops")));
            }
            Assert.Equal("outer\n", CheckCallbackScope.Message());
        }
        Assert.Equal("", CheckCallbackScope.Message());

        // LOG_FATAL prints the message and then the callbacks' messages,
        // most recent first.
        Assert.Contains("] FATAL oops", err);
        Assert.Contains("] FATAL oops" + Environment.NewLine + "inner\nouter\n\n\n", err);
    }
}

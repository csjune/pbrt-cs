// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/args_test.cpp

namespace Pbrt.Tests;

public class ArgsTests
{
    private static void expectNoError(string s) => Assert.Fail(s);

    [Fact]
    public void Simple()
    {
        {
            int nthreads = 0;
            List<string> args = SplitStringsFromWhitespace("--nthreads 4");
            int iter = 0;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(++iter == args.Count);
        }
        {
            int nthreads = 0;
            List<string> args = SplitStringsFromWhitespace("--nthreads=4");
            int iter = 0;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(++iter == args.Count);
        }
    }

    [Fact]
    public void Multiple()
    {
        {
            int nthreads = 0;
            bool log = false;
            List<string> args = SplitStringsFromWhitespace("--log --nthreads 4 yolo");
            int iter = 0;
            Assert.True(ParseArg(args, ref iter, "log", ref log, expectNoError));
            ++iter;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(log);
            Assert.False(++iter == args.Count);
            Assert.Equal("yolo", args[iter]);
        }
        {
            int nthreads = 0;
            List<string> args = SplitStringsFromWhitespace("yolo --nthreads=4");
            int iter = 0;
            Assert.False(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal("yolo", args[iter]);
            ++iter;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(++iter == args.Count);
        }
    }

    [Fact]
    public void Bool()
    {
        bool log = true;
        bool benchmark = false;
        bool debug = false;
        List<string> args = SplitStringsFromWhitespace("--log=false --benchmark --debug=true");
        int iter = 0;
        Assert.True(ParseArg(args, ref iter, "log", ref log, expectNoError));
        Assert.False(log);
        ++iter;
        Assert.True(ParseArg(args, ref iter, "benchmark", ref benchmark, expectNoError));
        Assert.True(benchmark);
        ++iter;
        Assert.True(ParseArg(args, ref iter, "debug", ref debug, expectNoError));
        Assert.True(debug);
        Assert.True(++iter == args.Count);
    }

    [Fact]
    public void ErrorMissingValue()
    {
        bool errCalled = false;
        void callback(string s) => errCalled = true;

        int nthreads = 2;
        List<string> args = SplitStringsFromWhitespace("--nthreads");
        int iter = 0;
        Assert.False(ParseArg(args, ref iter, "nthreads", ref nthreads, callback));
        Assert.Equal(2, nthreads);
        Assert.True(errCalled);
    }

    [Fact]
    public void ErrorMissingValueEqual()
    {
        bool errCalled = false;
        void callback(string s) => errCalled = true;

        int nthreads = 2;
        List<string> args = SplitStringsFromWhitespace("--nthreads=");
        int iter = 0;
        Assert.False(ParseArg(args, ref iter, "nthreads", ref nthreads, callback));
        Assert.Equal(2, nthreads);
        Assert.True(errCalled);
    }

    [Fact]
    public void ErrorBogusBool()
    {
        bool errCalled = false;
        void callback(string s) => errCalled = true;

        bool log = false;
        List<string> args = SplitStringsFromWhitespace("--log=tru3");
        int iter = 0;
        Assert.False(ParseArg(args, ref iter, "log", ref log, callback));
        Assert.False(log);
        Assert.True(errCalled);
    }

    [Fact]
    public void Normalization()
    {
        {
            int nthreads = 0;
            List<string> args = SplitStringsFromWhitespace("--n_threads 4");
            int iter = 0;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(++iter == args.Count);
        }
        {
            int nthreads = 0;
            List<string> args = SplitStringsFromWhitespace("--nThreads=4");
            int iter = 0;
            Assert.True(ParseArg(args, ref iter, "nthreads", ref nthreads, expectNoError));
            Assert.Equal(4, nthreads);
            Assert.True(++iter == args.Count);
        }
    }

    // Not in args_test.cpp: checks for the port.
    [Fact]
    public void OtherTypes()
    {
        List<string> args = SplitStringsFromWhitespace("--seed 7 --cropwindow 0,.5,0.25,1 --outfile a.exr --spp=x --pixel 3,4");
        int iter = 0;
        int? seed = null;
        Assert.True(ParseArg(args, ref iter, "seed", ref seed, expectNoError));
        Assert.Equal(7, seed);
        ++iter;
        float[] crop = new float[4];
        Assert.True(ParseArg(args, ref iter, "cropwindow", crop.AsSpan(), expectNoError));
        Assert.Equal([0f, .5f, .25f, 1f], crop);
        ++iter;
        string outfile = "";
        Assert.True(ParseArg(args, ref iter, "outfile", ref outfile, expectNoError));
        Assert.Equal("a.exr", outfile);
        ++iter;
        string? err = null;
        float spp = 1;
        Assert.False(ParseArg(args, ref iter, "spp", ref spp, s => err = s));
        Assert.Equal("invalid value \"x\" for --spp argument", err);
        ++iter;
        int[] pixel = new int[3];
        Assert.False(ParseArg(args, ref iter, "pixel", pixel.AsSpan(), s => err = s));
        Assert.Equal([0, 0, 0], pixel);
    }
}

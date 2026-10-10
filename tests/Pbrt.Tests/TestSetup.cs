// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Equivalent of the InitPBRT() call in src/pbrt/cmd/pbrt_test.cpp: runs once
// when the test assembly is loaded.

namespace Pbrt.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        ParallelInit();
    }
}

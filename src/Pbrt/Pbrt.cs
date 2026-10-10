// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/pbrt.cpp
//
// The color encodings, spectra, RGB-to-spectrum tables, color spaces and
// shape tables that pbrt initializes here are static fields that .NET
// initializes on first use. The GPU isn't ported.

namespace Pbrt;

public static partial class Util
{
    // API Function Definitions
    public static void InitPBRT(PBRTOptions opt)
    {
        Options = opt;
        // API Initialization

        if (Options.quiet)
            SuppressErrorMessages();

        InitLogging(opt.logLevel, opt.logFile, opt.logUtilization, Options.useGPU);

        // General \pbrt Initialization
        int nThreads = Options.nThreads != 0 ? Options.nThreads : AvailableCores();
        ParallelInit(nThreads);

        if (Options.interactive)
            GUI.Initialize();

        if (Options.displayServer.Length > 0)
            ConnectToDisplayServer(Options.displayServer);
    }

    public static void CleanupPBRT()
    {
        ForEachThread(Stats.ReportThreadStats);

        if (Options.recordPixelStatistics)
            Stats.StatsWritePixelImages();

        if (Options.printStatistics)
        {
            Stats.PrintStats(Console.Out);
            Stats.ClearStats();
        }

        if (Options.displayServer.Length > 0)
            DisconnectFromDisplayServer();

        // API Cleanup
        ParallelCleanup();

        ShutdownLogging();

        Options = new PBRTOptions();
    }
}

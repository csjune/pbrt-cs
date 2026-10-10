// pbrt is Copyright(c) 1998-2023 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cmd/nanovdb2pbrt.cpp

using System.Globalization;
using System.Text;

namespace Pbrt.NanoVDB2Pbrt;

public static class Program
{
    private static NanoVDBGridHandle? readGrid(string filename, string gridName)
    {
        NanoVDBGridHandle? grid = null;
        try
        {
            grid = NanoVDBIO.ReadGrid(filename, gridName);
        }
        catch (NanoVDBException e)
        {
            ErrorExit($"nanovdb: {filename}: {e.Message}");
        }

        if (grid != null)
        {
            if (!grid.IsFogVolume() && !grid.IsUnknown())
                ErrorExit($"{filename}: \"{gridName}\" isn't a FogVolume grid?");

            LOG_VERBOSE($"{filename}: found {grid.ActiveVoxelCount()} \"{gridName}\" voxels");
        }

        return grid;
    }

    private static void usage(string? msg = null)
    {
        if (!string.IsNullOrEmpty(msg))
            Console.Error.Write($"nanovdb2pbrt: {msg}\n\n");

        Console.Error.Write("""
            usage: nanovdb2pbrt [<options>] <filename.nvdb>

            Options:
              --grid <name>        Name of grid to extract. Default: "density"

            """);
        Environment.Exit(string.IsNullOrEmpty(msg) ? 0 : 1);
    }

    public static int Main(string[] argv)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is FatalErrorException)
                Environment.Exit(1);
        };
        try
        {
            return Run(argv);
        }
        catch (FatalErrorException)
        {
            // (The error has been reported.)
            return 1;
        }
    }

    private static int Run(string[] argv)
    {
        List<string> args = GetCommandLineArguments(argv);

        static void onError(string err)
        {
            usage(err);
            Environment.Exit(1);
        }

        string filename = "";
        string grid = "density";
        int downsample = 0;
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (args[iter].Length == 0 || args[iter][0] != '-')
            {
                if (filename.Length == 0)
                    filename = args[iter];
                else
                {
                    usage();
                    Environment.Exit(1);
                }
            }
            else if (ParseArg(args, ref iter, "downsample", ref downsample, onError) ||
                     ParseArg(args, ref iter, "grid", ref grid, onError))
            {
                // success
            }
            else
            {
                usage();
                Environment.Exit(1);
            }
        }

        if (filename.Length == 0)
            usage("must specify a nanovdb filename");

        NanoVDBGridHandle? nanoGrid = readGrid(filename, grid);
        if (nanoGrid == null)
            ErrorExit($"{filename}: didn't find \"{grid}\" grid.");
        NanoVDBFloatGrid floatGrid = nanoGrid!.FloatGrid()!;

        var bmin = floatGrid.WorldBBoxMin();
        var bmax = floatGrid.WorldBBoxMax();
        Bounds3f bounds = new(new Point3f((Float)bmin.x, (Float)bmin.y, (Float)bmin.z),
                              new Point3f((Float)bmax.x, (Float)bmax.y, (Float)bmax.z));

        List<Float> values = [];

        var (imin, imax) = floatGrid.IndexBBox();
        int x0 = imin.x, x1 = imax.x + 1;
        int y0 = imin.y, y1 = imax.y + 1;
        int z0 = imin.z, z1 = imax.z + 1;

        NanoVDBFloatGrid.Accessor accessor = new(floatGrid);
        for (int z = z0; z <= z1; ++z)
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x)
                    values.Add(accessor.GetValue(x, y, z));

        using Stream stdout = Console.OpenStandardOutput();
        using StreamWriter w = new(stdout, new UTF8Encoding(false), 1 << 20);
        CultureInfo ci = CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
        w.Write(string.Create(ci, $"\"integer nx\" {1 + x1 - x0} \"integer ny\" {1 + y1 - y0}  \"integer nz\" {1 + z1 - z0}\n"));
        w.Write($"\t\"point3 p0\" [ {F(bounds.pMin.x)} {F(bounds.pMin.y)} {F(bounds.pMin.z)} ] " +
                $"\"point3 p1\" [ {F(bounds.pMax.x)} {F(bounds.pMax.y)} {F(bounds.pMax.z)} ]\n");
        w.Write($"\t\"float {grid}\" [\n");
        for (int i = 0; i < values.Count; ++i)
        {
            Float d = values[i];
            if (d == 0) w.Write("0 ");
            else { w.Write(F(d)); w.Write(' '); }
            if ((i % 20) == 19) w.Write('\n');
        }
        w.Write("]\n");
        return 0;
    }
}

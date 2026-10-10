// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the ports of pbrt's auxiliary command-line tools (plytool,
// nanovdb2pbrt, cyhair2pbrt, pspec and imgtool). Each test writes a small
// input, runs a tool as a separate process and compares what it prints and
// writes with what pbrt-v4's C++ tool produces for the same input. (Larger
// outputs are compared by their SHA-256 hashes; images by the hash of their
// pixel values, since EXR files' zip-compressed bytes depend on the zlib
// implementation.)
//
// The expected values were recorded from the C++ tools (built with
// -ffp-contract=off) by running these tests with PBRT_TOOLS_CPP_DIR set to
// the directory with the C++ executables and PBRT_TOOLS_RECORD set to a file
// that the actual values are appended to (instead of being checked).

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Pbrt.Tests;

public class ToolsTests
{
    private static readonly string TestDir = Directory.CreateTempSubdirectory("pbrt-tools-test").FullName;

    private static string NewDir(string name)
    {
        string dir = Path.Combine(TestDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed record Result(int ExitCode, string Stdout, string Stderr);

    private static Result Run(string tool, string cwd, params string[] args) => Run(tool, cwd, null, args);

    private static Result Run(string tool, string cwd, byte[]? stdin, params string[] args)
    {
        string? cppDir = Environment.GetEnvironmentVariable("PBRT_TOOLS_CPP_DIR");
        ProcessStartInfo psi;
        if (!string.IsNullOrEmpty(cppDir))
            psi = new ProcessStartInfo(Path.Combine(cppDir, tool));
        else
        {
            psi = new ProcessStartInfo("dotnet");
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, tool + ".dll"));
        }
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        psi.WorkingDirectory = cwd;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        using Process p = Process.Start(psi)!;
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (stdin != null)
            p.StandardInput.BaseStream.Write(stdin);
        p.StandardInput.Close();
        p.WaitForExit();
        return new Result(p.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16];
    private static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));
    private static string FileHash(string path) => Hash(File.ReadAllBytes(path));

    // A hash of an image's resolution, channel names and pixel values.
    private static string ImageHash(string path)
    {
        Image image = Image.Read(path).image;
        StringBuilder sb = new($"{image.Resolution()} {string.Join(",", image.ChannelNames())}");
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                for (int c = 0; c < image.NChannels(); ++c)
                    sb.Append(' ').Append(BitConverter.SingleToInt32Bits((float)image.GetChannel(new Point2i(x, y), c)));
        return Hash(sb.ToString());
    }

    // Expected values, recorded from the C++ tools.
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["cyhair2pbrt file"] = "9776ABCD45432220",
        ["cyhair2pbrt stderr"] = "Converted 6 strands.\n",
        ["cyhair2pbrt stdout"] = "690E1282AE90EFE1",
        ["imgtool bloom"] = "FAF275F5D4A0D98A",
        ["imgtool cat"] = "694C1D0C6E3ECDD0",
        ["imgtool cat --list"] = "7B79A8CA592CCA92",
        ["imgtool cat --sort"] = "B9A618A82E4C11CD", // (pbrt-cs's order of ties, see below)
        ["imgtool convert --acesfilmic --bw"] = "AEA583EA4F9CC64D",
        ["imgtool convert --colorspace ACES2065-1 --preservecolors"] = "4507F417A6DCAF74",
        ["imgtool convert --crop 3,15,2,-6 --repeatpix 2 --flipy"] = "B911BAFED4D6893D",
        ["imgtool convert --despike 1"] = "11468A3454EBFB6B",
        ["imgtool convert --despike ties"] = "F3E86E203D490CB4",
        ["imgtool convert --despike ties stderr"] = "ties.exr: despiked 107 pixels\n",
        ["imgtool convert --scale 2 --gamma 2.2"] = "3BC32C4E9AC6A80F",
        ["imgtool convert --tonemap --maxluminance 0.7 --clamp 3"] = "66327455E412EC7F",
        ["imgtool diff FLIP"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.48736182 / 0.48928678 (\u001b[1m\u001b[31m-0.393422% delta\u001b[0m), FLIP = 0.20164235\n",
        ["imgtool diff FLIP image"] = "E1DF814D5797C39E",
        ["imgtool diff MAE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.48736182 / 0.48928678 (\u001b[1m\u001b[31m-0.393422% delta\u001b[0m), MAE = 0.16091564\n",
        ["imgtool diff MAE image"] = "B6571FDCECD8ADAF",
        ["imgtool diff MRSE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.48736182 / 0.48928678 (\u001b[1m\u001b[31m-0.393422% delta\u001b[0m), MRSE = 4.185161\n",
        ["imgtool diff MRSE image"] = "86338D9CA7921106",
        ["imgtool diff MSE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.48736182 / 0.48928678 (\u001b[1m\u001b[31m-0.393422% delta\u001b[0m), MSE = 0.5619749\n",
        ["imgtool diff MSE image"] = "CD1CE711D7AD89BB",
        ["imgtool falsecolor"] = "B184443EF81671DD",
        ["imgtool falsecolor --plusminus"] = "D17495D221A29488",
        ["imgtool info"] = "a.pfm:\n\tresolution (9, 7)\n\tpixel format: Float\n\tcolor space : sRGB\n\tChannels:\n\t                       R: min          0.5 max     0.998747 avg     0.838086 (0 infinite, 0 not-a-number)\n\t                       G: min            0 max     0.857143 avg     0.428571 (0 infinite, 0 not-a-number)\n\t                       B: min         0.25 max            4 avg     0.488095 (0 infinite, 0 not-a-number)\nties.exr:\n\tresolution (13, 11)\n\tpixel format: Float\n\tcolor space :  (unspecified)\n\tfull resolution (13, 11)\n\tpixel bounds (0, 0) - (13, 11)\n\tChannels:\n\t                       Y: min            0 max          1.5 avg     0.751748 (0 infinite, 0 not-a-number)\n",
        ["imgtool makeemitters"] = "369355B1A168B71F",
        ["imgtool makesky"] = "76B2B8CAF0CFDB20",
        ["imgtool scalenormalmap"] = "0DA7638729161C24",
        ["imgtool splitn"] = "1721B9A1D744B30A",
        ["imgtool splitn crops"] = "3F40B61555CA8637",
        ["imgtool whitebalance --illuminant"] = "FF24491BD57EAA41",
        ["imgtool whitebalance --temperature"] = "7A691B9D877D0185",
        ["nanovdb2pbrt density"] = "749F9EAB1BBB50AD",
        ["nanovdb2pbrt density header"] = "\"integer nx\" 9 \"integer ny\" 9  \"integer nz\" 9\n\t\"point3 p0\" [ 1.000000 0.000000 0.000000 ] \"point3 p1\" [ 5.000000 4.000000 4.000000 ]\n",
        ["nanovdb2pbrt temperature"] = "8B79742B402F8154",
        ["plytool cat"] = "1500E552FA009258",
        ["plytool displace"] = "BF21E35BB1150021",
        ["plytool info"] = "mesh.ply:\n\tTriangles: 24\n\tQuads: 0\n\tVertex positions: 20\n\tVertex normals: 20\n\tVertex uvs: 20\n\tFace indices: 0\n\tBounding box: [ [ 0, 0, 0 ] - [ 2, 1.2, 0.75 ] ]\n",
        ["plytool split"] = "98F62BBFDF28AFF8 EAC2E06D199E67A1 4BCBC94A3D8BA19D",
        ["plytool split stderr"] = "mesh.ply: mesh has 24 faces and will be split into 3 meshes.\n",
        ["pspec halton.owen exr"] = "B8197918AA477E7F",
        ["pspec halton.owen txt"] = "B6B05465D7C8AE9D",
        ["pspec lhs exr"] = "DBD237F3DF4A2E9C",
        ["pspec lhs txt"] = "46103A1D5003E57A",
        ["pspec pmj02bn exr"] = "18E6A1565CB82D79",
        ["pspec pmj02bn txt"] = "4F1E4FA2A24C9A17",
        ["pspec sobol.z exr"] = "1BEB5EAF3159E828",
        ["pspec sobol.z txt"] = "154A4E3B4E122B99",
        ["pspec stdin.dat txt"] = "337D1C118B1B3DF8",
        ["pspec stratified exr"] = "798201DC3FEE46DD",
        ["pspec stratified txt"] = "1240D98099D1866D",
    };

#if PBRT_FLOAT_AS_DOUBLE
    // The values that differ when Float is double, recorded from the C++
    // tools built with PBRT_FLOAT_AS_DOUBLE.
    private static readonly Dictionary<string, string> ExpectedDouble = new()
    {
        ["imgtool bloom"] = "96067899FFD58645",
        ["imgtool cat"] = "C395190F974C32B1",
        ["imgtool convert --acesfilmic --bw"] = "90AE299D2857D54E",
        ["imgtool convert --colorspace ACES2065-1 --preservecolors"] = "B2232F0FC8C27FE4",
        ["imgtool convert --tonemap --maxluminance 0.7 --clamp 3"] = "AED3A9AEB46829A7",
        ["imgtool diff FLIP"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.4873618471125762 / 0.4892867849104934 (\u001b[1m\u001b[31m-0.3934171% delta\u001b[0m), FLIP = 0.20164233395189513\n",
        ["imgtool diff MAE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.4873618471125762 / 0.4892867849104934 (\u001b[1m\u001b[31m-0.3934171% delta\u001b[0m), MAE = 0.16091563883754942\n",
        ["imgtool diff MRSE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.4873618471125762 / 0.4892867849104934 (\u001b[1m\u001b[31m-0.3934171% delta\u001b[0m), MRSE = 4.185161242020383\n",
        ["imgtool diff MSE"] = "Images differ:\n\ta.pfm b.pfm\n\tavg = 0.4873618471125762 / 0.4892867849104934 (\u001b[1m\u001b[31m-0.3934171% delta\u001b[0m), MSE = 0.5619748786587305\n",
        ["imgtool makesky"] = "99FDE2485BBDBC22",
        ["imgtool scalenormalmap"] = "969B7DD725B13A1F",
        ["imgtool whitebalance --illuminant"] = "42D7661E501A3391",
        ["imgtool whitebalance --temperature"] = "AA220B85A9A86414",
        ["plytool cat"] = "CEDAC723C0C97ED6",
        ["plytool displace"] = "9617C6B945FE0804",
        ["plytool info"] = "mesh.ply:\n\tTriangles: 24\n\tQuads: 0\n\tVertex positions: 20\n\tVertex normals: 20\n\tVertex uvs: 20\n\tFace indices: 0\n\tBounding box: [ [ 0, 0, 0 ] - [ 2, 1.2000000476837158, 0.75 ] ]\n",
        ["plytool split"] = "48F630DB17C03CC9 A671ECBACDE6A810 4BE4C24DC8A1AE3A",
        ["pspec halton.owen exr"] = "3891E86481985A1B",
        ["pspec halton.owen txt"] = "6330CBE2E40AC652",
        ["pspec lhs exr"] = "1A3AE7F6363F1162",
        ["pspec lhs txt"] = "F0899930CB7C4B45",
        ["pspec pmj02bn exr"] = "D0A6E58528689781",
        ["pspec sobol.z exr"] = "A238771C573513DB",
        ["pspec sobol.z txt"] = "F69025F39AACFF40",
        ["pspec stdin.dat txt"] = "8298137773D96203",
        ["pspec stratified exr"] = "21830595ED5A6FFB",
        ["pspec stratified txt"] = "7D70986235ADB63A",
    };
#endif

    // Results that depend on the last bits of transcendental functions
    // (FMath.Cos(), Math.Pow() and so forth), which .NET takes from the C
    // runtime: the expected values were recorded on Linux (glibc), and they
    // aren't the same with Windows's UCRT.
    private static readonly string[] libmDependent = ["pspec ", "imgtool diff FLIP", "imgtool makesky"];

    private static void Check(string key, string actual)
    {
        if (!OperatingSystem.IsLinux() && libmDependent.Any(key.StartsWith))
        {
            Assert.NotEmpty(actual);
            return;
        }
        string? record = Environment.GetEnvironmentVariable("PBRT_TOOLS_RECORD");
        if (!string.IsNullOrEmpty(record))
        {
            lock (typeof(ToolsTests))
                File.AppendAllText(record, $"{key}: {actual.Replace("\n", "\\n")}\n");
            return;
        }
#if PBRT_FLOAT_AS_DOUBLE
        if (ExpectedDouble.TryGetValue(key, out string? expectedDouble))
        {
            Assert.Equal(expectedDouble, actual);
            return;
        }
#endif
        Assert.Equal(Expected[key], actual);
    }

    ///////////////////////////////////////////////////////////////////////////
    // Inputs

    // An RGB float image with smooth gradients and a few bright pixels;
    // variant changes it slightly.
    private static void WriteRGBImage(string path, int xres, int yres, int variant)
    {
        Image image = new(PixelFormat.Float, new Point2i(xres, yres), ["R", "G", "B"]);
        for (int y = 0; y < yres; ++y)
            for (int x = 0; x < xres; ++x)
            {
                Float r = 0.5f + 0.5f * MathF.Sin(0.3f * x + 0.1f * variant);
                Float g = (Float)y / yres;
                Float b = ((x * 7 + y * 13 + variant) % 17 == 0) ? 4 : 0.25f;
                image.SetChannels(new Point2i(x, y), [r, g, b]);
            }
        Assert.True(image.Write(path));
    }

    // A one-channel image with many equal values (and so many ties when
    // sorting by value). (It's an EXR, since PFM files store 3 channels.)
    private static void WriteTiesImage(string path)
    {
        Image image = new(PixelFormat.Float, new Point2i(13, 11), ["Y"]);
        for (int y = 0; y < 11; ++y)
            for (int x = 0; x < 13; ++x)
                image.SetChannel(new Point2i(x, y), 0, ((x * 5 + y * 3) % 4) * 0.5f);
        Assert.True(image.Write(path));
    }

    // A triangle mesh with normals and uvs: a 4x3 grid of quads, each split
    // into two triangles.
    private static void WritePlyMesh(string path)
    {
        List<Point3f> p = [];
        List<Normal3f> n = [];
        List<Point2f> uv = [];
        for (int j = 0; j <= 3; ++j)
            for (int i = 0; i <= 4; ++i)
            {
                p.Add(new Point3f(i * 0.5f, 0.1f * i * j, j * 0.25f));
                n.Add(Normalize(new Normal3f(0.1f * i, 1, -0.05f * j)));
                uv.Add(new Point2f(i / 4f, j / 3f));
            }
        List<int> indices = [];
        for (int j = 0; j < 3; ++j)
            for (int i = 0; i < 4; ++i)
            {
                int v00 = j * 5 + i, v10 = v00 + 1, v01 = v00 + 5, v11 = v01 + 1;
                indices.AddRange([v00, v10, v11, v00, v11, v01]);
            }
        Assert.True(WritePLY(path, indices.ToArray(), [], p.ToArray(), n.ToArray(), uv.ToArray(), []));
    }

    // A CyHair file with per-strand segment counts.
    private static void WriteHair(string path)
    {
        ushort[] segments = [3, 1, 5, 2, 4];
        int totalPoints = segments.Sum(s => s + 1);
        using BinaryWriter w = new(File.Create(path));
        w.Write("HAIR"u8);
        w.Write((uint)segments.Length);
        w.Write((uint)totalPoints);
        w.Write(0x1u | 0x2u | 0x4u);  // segments, points, thickness
        w.Write(0u);  // default segments
        w.Write(0.02f);  // default thickness
        w.Write(1f);  // default transparency
        w.Write(0.5f);
        w.Write(0.5f);
        w.Write(0.5f);
        w.Write(new byte[88]);
        foreach (ushort s in segments)
            w.Write(s);
        for (int i = 0; i < totalPoints; ++i)
        {
            w.Write(MathF.Sin(i * 0.7f));
            w.Write(0.3f * i);
            w.Write(MathF.Cos(i * 1.3f) - 0.2f);
        }
        for (int i = 0; i < totalPoints; ++i)
            w.Write(0.01f * (i + 1));
    }

    ///////////////////////////////////////////////////////////////////////////
    // plytool

    [Fact]
    public void PlyTool()
    {
        string dir = NewDir("plytool");
        WritePlyMesh(Path.Combine(dir, "mesh.ply"));

        Result info = Run("plytool", dir, "info", "mesh.ply");
        Assert.Equal(0, info.ExitCode);
        Check("plytool info", info.Stdout);

        Result cat = Run("plytool", dir, "cat", "mesh.ply");
        Assert.Equal(0, cat.ExitCode);
        Check("plytool cat", Hash(cat.Stdout));

        Result split = Run("plytool", dir, "split", "--maxfaces", "10", "mesh.ply");
        Assert.Equal(0, split.ExitCode);
        Check("plytool split stderr", split.Stderr);
        Check("plytool split",
              string.Join(" ", Enumerable.Range(0, 3).Select(i => FileHash(Path.Combine(dir, $"mesh-{i:D3}.ply")))));

        WriteRGBImage(Path.Combine(dir, "disp.pfm"), 16, 8, 0);
        Result displace = Run("plytool", dir, "displace", "--scale", "0.1", "--uvscale", "1.5", "--edge-length",
                              "0.2", "--image", "disp.pfm", "--outfile", "displaced.ply", "mesh.ply");
        Assert.Equal(0, displace.ExitCode);
        Check("plytool displace", FileHash(Path.Combine(dir, "displaced.ply")));

        Result bad = Run("plytool", dir, "frobnicate");
        Assert.Equal(1, bad.ExitCode);
        Assert.StartsWith("plytool: frobnicate: command unknown\n\n", bad.Stderr);
    }

    ///////////////////////////////////////////////////////////////////////////
    // nanovdb2pbrt

    [Fact]
    public void NanoVDB2Pbrt()
    {
        string dir = NewDir("nanovdb2pbrt");
        string nvdb = NanoVDBTests.WriteFile(0, ("density", NanoVDBTests.MakeGrid("density")),
                                             ("temperature", NanoVDBTests.MakeGrid("temperature")));
        File.Copy(nvdb, Path.Combine(dir, "grid.nvdb"));

        Result density = Run("nanovdb2pbrt", dir, "grid.nvdb");
        Assert.Equal(0, density.ExitCode);
        Check("nanovdb2pbrt density", Hash(density.Stdout));
        Check("nanovdb2pbrt density header", density.Stdout[..density.Stdout.IndexOf("\t\"float", StringComparison.Ordinal)]);

        Result temperature = Run("nanovdb2pbrt", dir, "--grid", "temperature", "grid.nvdb");
        Assert.Equal(0, temperature.ExitCode);
        Check("nanovdb2pbrt temperature", Hash(temperature.Stdout));

        Result missing = Run("nanovdb2pbrt", dir, "--grid", "foo", "grid.nvdb");
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("grid.nvdb: didn't find \"foo\" grid.", missing.Stderr);
    }

    ///////////////////////////////////////////////////////////////////////////
    // cyhair2pbrt

    [Fact]
    public void CyHair2Pbrt()
    {
        string dir = NewDir("cyhair2pbrt");
        WriteHair(Path.Combine(dir, "test.hair"));

        Result toStdout = Run("cyhair2pbrt", dir, "test.hair", "-");
        Assert.Equal(0, toStdout.ExitCode);
        Check("cyhair2pbrt stdout", Hash(toStdout.Stdout));
        Check("cyhair2pbrt stderr", toStdout.Stderr);

        Result toFile = Run("cyhair2pbrt", dir, "test.hair", "hair.pbrt", "3", "-1");
        Assert.Equal(0, toFile.ExitCode);
        Check("cyhair2pbrt file", FileHash(Path.Combine(dir, "hair.pbrt")));

        Result usage = Run("cyhair2pbrt", dir, "test.hair");
        Assert.Equal(1, usage.ExitCode);
    }

    ///////////////////////////////////////////////////////////////////////////
    // pspec

    [Fact]
    public void PSpec()
    {
        string dir = NewDir("pspec");
        foreach (string sampler in new[] { "halton.owen", "lhs", "pmj02bn", "sobol.z", "stratified" })
        {
            Result r = Run("pspec", dir, sampler, "--npoints", "64", "--nsets", "2", "--resolution", "20", "--outbase",
                           sampler);
            Assert.Equal(0, r.ExitCode);
            Check($"pspec {sampler} txt", FileHash(Path.Combine(dir, sampler + ".txt")));
            Check($"pspec {sampler} exr", ImageHash(Path.Combine(dir, sampler + ".exr")));
        }

        // Two sets of points, separated by '#', read from standard input.
        StringBuilder points = new();
        RNG rng = new(7);
        for (int set = 0; set < 2; ++set)
        {
            for (int i = 0; i < 16; ++i)
                points.Append($"{FloatToString(rng.Uniform<Float>())} {FloatToString(rng.Uniform<Float>())}\n");
            points.Append("#\n");
        }
        Result stdin = Run("pspec", dir, Encoding.ASCII.GetBytes(points.ToString()), "stdin.dat", "--npoints", "16",
                           "--resolution", "15");
        Assert.Equal(0, stdin.ExitCode);
        Check("pspec stdin.dat txt", FileHash(Path.Combine(dir, "stdin.dat-16pts-2sets.txt")));
    }

    ///////////////////////////////////////////////////////////////////////////
    // imgtool

    [Fact]
    public void ImgToolInfoAndCat()
    {
        string dir = NewDir("imgtool-info");
        WriteRGBImage(Path.Combine(dir, "a.pfm"), 9, 7, 0);
        WriteTiesImage(Path.Combine(dir, "ties.exr"));

        Result info = Run("imgtool", dir, "info", "a.pfm", "ties.exr");
        Assert.Equal(0, info.ExitCode);
        Check("imgtool info", info.Stdout);

        Result cat = Run("imgtool", dir, "cat", "a.pfm");
        Assert.Equal(0, cat.ExitCode);
        Check("imgtool cat", Hash(cat.Stdout));

        // The order of the tied values is std::sort()'s, which C++ leaves
        // unspecified, so this one is pbrt-cs's own output rather than the
        // C++ tool's.
        Result sort = Run("imgtool", dir, "cat", "--sort", "ties.exr");
        Assert.Equal(0, sort.ExitCode);
        Check("imgtool cat --sort", Hash(sort.Stdout));

        Result list = Run("imgtool", dir, "cat", "--list", "ties.exr");
        Assert.Equal(0, list.ExitCode);
        Check("imgtool cat --list", Hash(list.Stdout));
    }

    [Fact]
    public void ImgToolDiff()
    {
        string dir = NewDir("imgtool-diff");
        WriteRGBImage(Path.Combine(dir, "a.pfm"), 24, 16, 0);
        WriteRGBImage(Path.Combine(dir, "b.pfm"), 24, 16, 1);

        foreach (string metric in new[] { "MSE", "MAE", "MRSE", "FLIP" })
        {
            Result r = Run("imgtool", dir, "diff", "--metric", metric, "--reference", "b.pfm", "--outfile",
                           $"diff-{metric}.exr", "a.pfm");
            Assert.Equal(1, r.ExitCode);
            Check($"imgtool diff {metric}", r.Stdout);
            Check($"imgtool diff {metric} image", ImageHash(Path.Combine(dir, $"diff-{metric}.exr")));
        }

        Result same = Run("imgtool", dir, "diff", "--reference", "a.pfm", "a.pfm");
        Assert.Equal(0, same.ExitCode);
        Assert.Equal("", same.Stdout);
    }

    [Fact]
    public void ImgToolConvert()
    {
        string dir = NewDir("imgtool-convert");
        WriteRGBImage(Path.Combine(dir, "a.pfm"), 20, 12, 2);
        WriteTiesImage(Path.Combine(dir, "ties.exr"));

        string[][] options =
        [
            ["--scale", "2", "--gamma", "2.2"],
            ["--tonemap", "--maxluminance", "0.7", "--clamp", "3"],
            ["--colorspace", "ACES2065-1", "--preservecolors"],
            ["--acesfilmic", "--bw"],
            ["--crop", "3,15,2,-6", "--repeatpix", "2", "--flipy"],
            ["--despike", "1"],
        ];
        for (int i = 0; i < options.Length; ++i)
        {
            Result r = Run("imgtool", dir, ["convert", .. options[i], "--outfile", $"out-{i}.pfm", "a.pfm"]);
            Assert.Equal(0, r.ExitCode);
            Check($"imgtool convert {string.Join(" ", options[i])}", FileHash(Path.Combine(dir, $"out-{i}.pfm")));
        }

        // std::nth_element() with ties (the median's value doesn't depend
        // on which of the tied elements it picks here).
        Result despike = Run("imgtool", dir, "convert", "--despike", "0.5", "--outfile", "despiked.exr", "ties.exr");
        Assert.Equal(0, despike.ExitCode);
        Check("imgtool convert --despike ties", ImageHash(Path.Combine(dir, "despiked.exr")));
        Check("imgtool convert --despike ties stderr", despike.Stderr);
    }

    [Fact]
    public void ImgToolMisc()
    {
        string dir = NewDir("imgtool-misc");
        WriteRGBImage(Path.Combine(dir, "a.pfm"), 20, 16, 3);
        WriteRGBImage(Path.Combine(dir, "b.pfm"), 20, 16, 4);

        (string name, string[] args, string output)[] commands =
        [
            ("bloom", ["bloom", "--level", "2", "--width", "5", "--iterations", "2", "--outfile", "bloom.pfm", "a.pfm"],
             "bloom.pfm"),
            ("falsecolor", ["falsecolor", "--outfile", "false.pfm", "a.pfm"], "false.pfm"),
            ("falsecolor --plusminus", ["falsecolor", "--plusminus", "--outfile", "pm.pfm", "a.pfm"], "pm.pfm"),
            ("scalenormalmap", ["scalenormalmap", "--scale", "0.5", "--outfile", "normal.pfm", "a.pfm"],
             "normal.pfm"),
            ("whitebalance --illuminant",
             ["whitebalance", "--illuminant", "A", "--outfile", "wb-a.pfm", "a.pfm"], "wb-a.pfm"),
            ("whitebalance --temperature",
             ["whitebalance", "--temperature", "4000", "--outfile", "wb-t.pfm", "a.pfm"], "wb-t.pfm"),
            ("splitn", ["splitn", "--cropsize", "4", "--crop", "8,6", "--outfile", "split.pfm", "b.pfm", "a.pfm",
                        "b.pfm"], "split.pfm"),
            ("makesky", ["makesky", "--resolution", "24", "--elevation", "25", "--turbidity", "4.5", "--albedo",
                         "0.3", "--outfile", "sky.pfm"], "sky.pfm"),
        ];
        foreach (var (name, args, output) in commands)
        {
            Result r = Run("imgtool", dir, args);
            Assert.Equal(0, r.ExitCode);
            Check($"imgtool {name}", FileHash(Path.Combine(dir, output)));
        }
        Check("imgtool splitn crops", FileHash(Path.Combine(dir, "crops-split.pfm")));

        Result emitters = Run("imgtool", dir, "makeemitters", "--downsample", "3", "a.pfm");
        Assert.Equal(0, emitters.ExitCode);
        Check("imgtool makeemitters", Hash(emitters.Stdout));

        Result help = Run("imgtool", dir, "help", "makesky");
        Assert.Equal(0, help.ExitCode);
        Assert.StartsWith("usage: imgtool makesky [options] <filename>\n\noptions:\n    --albedo <a>", help.Stderr);
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/file_test.cpp

namespace Pbrt.Tests;

public class FileTests
{
    private static string inTestDir(string path) => ImageTests.InTestDir(path);

    [Fact]
    public void HasExtension()
    {
        Assert.True(Util.HasExtension("foo.exr", "exr"));
        Assert.True(Util.HasExtension("foo.Exr", "exr"));
        Assert.True(Util.HasExtension("foo.Exr", "exR"));
        Assert.True(Util.HasExtension("foo.EXR", "exr"));
        Assert.False(Util.HasExtension("foo.xr", "exr"));
        Assert.False(Util.HasExtension("/foo/png", "ppm"));
    }

    [Fact]
    public void RemoveExtension()
    {
        Assert.Equal("foo", Util.RemoveExtension("foo.exr"));
        Assert.Equal("fooexr", Util.RemoveExtension("fooexr"));
        Assert.Equal("foo.exr", Util.RemoveExtension("foo.exr.png"));
    }

    [Fact]
    public void ReadWriteFile()
    {
        string fn = inTestDir("readwrite.txt");
        string str = "this is a test.";
        Assert.True(WriteFileContents(fn, str));
        byte[] contents = ReadFileContents(fn);
        Assert.NotEmpty(contents);
        Assert.Equal(str, System.Text.Encoding.UTF8.GetString(contents));
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void Success()
    {
        string fn = inTestDir("floatfile_good.txt");
        Assert.True(WriteFileContents(fn, """
            1
            # comment 6632
            -2.5
            #6502
            3e2       -4.75E-1       5.25




            6

            """));

        List<Float> floats = ReadFloatFile(fn);
        Assert.Equal(6, floats.Count);
        Float[] expected = [1, -2.5f, 300, -(Float).475, 5.25f, 6];
        for (int i = 0; i < expected.Length; ++i)
            Assert.Equal(expected[i], floats[i]);

        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void Failures()
    {
        List<Float> floats = ReadFloatFile("NO_SUCH_FILE_64622");
        Assert.Empty(floats);

        string fn = inTestDir("malformed.txt");
        Assert.True(WriteFileContents(fn, """
            1
            2 3 4
            l5l
            6



            """));
        floats = ReadFloatFile(fn);
        Assert.Empty(floats);

        RemoveFile(fn);
    }

    // Not in file_test.cpp: checks for the port.

    [Fact]
    public void SearchDirectoryAndMatching()
    {
        string dir = Path.Combine(ImageTests.TestDir, "search");
        Directory.CreateDirectory(dir);
        string scene = Path.Combine(dir, "scene.pbrt");
        Assert.True(WriteFileContents(scene, "# scene"));
        Assert.True(WriteFileContents(Path.Combine(dir, "tex-a.png"), "a"));
        Assert.True(WriteFileContents(Path.Combine(dir, "tex-b.png"), "b"));
        try
        {
            SetSearchDirectory(scene);
            // (Compared as full paths: on Windows, the directory separators
            // in the results are backslashes.)
            Assert.Equal(Path.GetFullPath(Path.Combine(dir, "tex-a.png")), ResolveFilename("tex-a.png"));
            Assert.Equal("missing.png", ResolveFilename("missing.png"));
            Assert.Equal("", ResolveFilename(""));
            Assert.Equal(scene, ResolveFilename(scene));

            List<string> matches = [.. MatchingFilenames(Path.Combine(dir, "tex-")).Select(Path.GetFullPath)];
            matches.Sort(StringComparer.Ordinal);
            Assert.Equal([Path.GetFullPath(Path.Combine(dir, "tex-a.png")), Path.GetFullPath(Path.Combine(dir, "tex-b.png"))],
                         matches);
            Assert.True(FileExists(scene));
            Assert.False(FileExists(Path.Combine(dir, "nope")));
        }
        finally
        {
            SetSearchDirectory("");
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Decompress()
    {
        string fn = inTestDir("data.gz");
        byte[] data = new byte[100000];
        new Random(1).NextBytes(data.AsSpan(0, 50000));
        using (FileStream f = File.Create(fn))
        using (System.IO.Compression.GZipStream gz = new(f, System.IO.Compression.CompressionLevel.Optimal))
            gz.Write(data);
        Assert.Equal(data, ReadDecompressedFileContents(fn));
        Assert.True(RemoveFile(fn));
    }
}

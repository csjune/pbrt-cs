// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the NanoVDB port (pbrt has no NanoVDB tests; the media were
// compared with pbrt's using pbrt-v4-scenes' .nvdb files separately). The
// tests write small .nvdb files with a minimal writer: a grid with one leaf
// at the origin and tiles at each level of the tree.

using System.IO.Compression;
using System.Text;

namespace Pbrt.Tests;

[Collection("Options")]
public class NanoVDBTests
{
    private const float Background = 0.25f, RootTileValue = 3, UpperTileValue = 5, LowerTileValue = 7;

    private static float LeafValue(int i, int j, int k) => 0.01f * (i + 8 * j + 64 * k) + 0.5f;

    // A float grid's buffer: index space is world space scaled by 2 and
    // translated by (1, 0, 0) (so worldToIndex() is (p - (1,0,0)) * 2).
    internal static byte[] MakeGrid(string name, NanoVDBGridClass gridClass = NanoVDBGridClass.FogVolume,
                                   NanoVDBGridType gridType = NanoVDBGridType.Float)
    {
        const int grid = 0, tree = 672, root = tree + 64, tiles = root + 64, upper = tiles + 3 * 32;
        const int lower = upper + 270400, leaf = lower + 33856, size = leaf + 2144;
        byte[] b = new byte[size];
        void U32(int o, uint v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
        void U64(int o, ulong v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
        void I32(int o, int v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
        void F32(int o, float v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);
        void F64(int o, double v) => BitConverter.TryWriteBytes(b.AsSpan(o), v);

        // GridData
        U64(grid, 0x304244566f6e614eUL);
        U32(grid + 28, 1);  // grid count (C++ NanoVDB's GridHandle needs it)
        U64(grid + 32, (ulong)size);
        Encoding.ASCII.GetBytes(name).CopyTo(b, grid + 40);
        const int map = grid + 296;
        float[] mat = [0.5f, 0, 0, 0, 0.5f, 0, 0, 0, 0.5f], invMat = [2, 0, 0, 0, 2, 0, 0, 0, 2];
        for (int i = 0; i < 9; ++i)
        {
            F32(map + 4 * i, mat[i]);
            F32(map + 36 + 4 * i, invMat[i]);
        }
        F32(map + 72, 1);
        F64(grid + 560, 1);
        F64(grid + 584, 5);
        F64(grid + 592, 4);
        F64(grid + 600, 4);
        U32(grid + 632, (uint)gridClass);
        U32(grid + 636, (uint)gridType);

        // TreeData: the root's offset and the voxel count
        U64(tree + 24, (ulong)(root - tree));
        U64(tree + 56, 512);

        // RootData: bbox, table size, background, min, max
        I32(root + 12, 7);
        I32(root + 16, 7);
        I32(root + 20, 7);
        U32(root + 24, 3);
        F32(root + 28, Background);
        F32(root + 32, 0);
        F32(root + 36, 10);
        // Tiles: (0,0,0) with the upper node, (4096,0,0) with a value and a
        // second tile with the first key (which is ignored: NanoVDB uses the
        // first match).
        U64(tiles, 0);
        U64(tiles + 8, (ulong)(upper - root));
        U64(tiles + 32, 1ul << 42);
        U32(tiles + 32 + 16, 1);
        F32(tiles + 32 + 20, RootTileValue);
        U64(tiles + 64, 0);
        F32(tiles + 64 + 20, 99);

        // Upper node: child 0 is the lower node, and (128,0,0) is a tile.
        U64(upper + 4128, 1);
        U64(upper + 8256, (ulong)(lower - upper));
        F32(upper + 8256 + 8 * 1024, UpperTileValue);
        // Lower node: child 0 is the leaf, and (8,0,0) is a tile.
        U64(lower + 544, 1);
        U64(lower + 1088, (ulong)(leaf - lower));
        F32(lower + 1088 + 8 * 256, LowerTileValue);
        // Leaf values
        for (int i = 0; i < 8; ++i)
            for (int j = 0; j < 8; ++j)
                for (int k = 0; k < 8; ++k)
                    F32(leaf + 96 + 4 * ((i << 6) | (j << 3) | k), LeafValue(i, j, k));
        return b;
    }

    // Writes a segment with the grids (codec 0: none, 1: ZIP).
    internal static string WriteFile(ushort codec, params (string name, byte[] grid)[] grids)
    {
        string fn = ImageTests.InTestDir($"grid-{Guid.NewGuid():N}.nvdb");
        using FileStream s = new(fn, FileMode.Create);
        using BinaryWriter w = new(s);
        w.Write(0x304244566f6e614eUL);
        w.Write((32u << 21) | (3u << 10) | 3u);
        w.Write((ushort)grids.Length);
        w.Write(codec);
        List<byte[]> payloads = [];
        foreach (var (name, grid) in grids)
        {
            byte[] payload = grid;
            if (codec == 1)
            {
                MemoryStream ms = new();
                using (ZLibStream z = new(ms, CompressionLevel.Optimal, true))
                    z.Write(grid);
                payload = [.. BitConverter.GetBytes((ulong)ms.Length), .. ms.ToArray()];
            }
            payloads.Add(payload);
            byte[] md = new byte[176];
            BitConverter.TryWriteBytes(md.AsSpan(0), (ulong)grid.Length);
            BitConverter.TryWriteBytes(md.AsSpan(8), (ulong)payload.Length);
            BitConverter.TryWriteBytes(md.AsSpan(16), NanoVDBIO.StringHash(name));
            BitConverter.TryWriteBytes(md.AsSpan(136), (uint)(name.Length + 1));
            w.Write(md);
            w.Write(Encoding.ASCII.GetBytes(name + "\0"));
        }
        foreach (byte[] p in payloads)
            w.Write(p);
        return fn;
    }

    [Fact]
    public void StringHash()
    {
        // The hash of "density" computed by NanoVDB's stringHash().
        ulong expected = 0;
        foreach (char c in "density")
        {
            ulong overflow = expected >> 56;
            expected = expected * 67 + c + overflow;
        }
        Assert.Equal(expected, NanoVDBIO.StringHash("density"));
        Assert.Equal(0ul, NanoVDBIO.StringHash(""));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReadGrid(ushort codec)
    {
        string fn = WriteFile(codec, ("temperature", MakeGrid("temperature")), ("density", MakeGrid("density")));
        NanoVDBGridHandle? handle = NanoVDBIO.ReadGrid(fn, "density");
        Assert.Null(NanoVDBIO.ReadGrid(fn, "velocity"));
        Assert.NotNull(NanoVDBIO.ReadGrid(fn, "temperature"));
        Assert.True(RemoveFile(fn));

        Assert.NotNull(handle);
        Assert.Equal(NanoVDBGridType.Float, handle!.GridType());
        Assert.True(handle.IsFogVolume());
        Assert.Equal(512ul, handle.ActiveVoxelCount());
        NanoVDBFloatGrid grid = handle.FloatGrid()!;

        // Voxel values at each level of the tree
        Assert.Equal(LeafValue(1, 2, 3), grid.GetValue(1, 2, 3));
        Assert.Equal(LeafValue(7, 7, 7), grid.GetValue(7, 7, 7));
        Assert.Equal(LowerTileValue, grid.GetValue(8, 0, 0));
        Assert.Equal(0, grid.GetValue(16, 0, 0));
        Assert.Equal(UpperTileValue, grid.GetValue(130, 3, 4));
        Assert.Equal(RootTileValue, grid.GetValue(4096 + 17, 5, 9));
        Assert.Equal(Background, grid.GetValue(-1, 0, 0));
        Assert.Equal(Background, grid.GetValue(0, 4096, 0));

        // An accessor gives the same values in any order.
        NanoVDBFloatGrid.Accessor acc = new(grid);
        RNG rng = new(3);
        for (int n = 0; n < 1000; ++n)
        {
            int i = (int)rng.Uniform<uint>() % 300 - 20, j = (int)(rng.Uniform<uint>() % 20) - 4;
            int k = (int)(rng.Uniform<uint>() % 20) - 4;
            Assert.Equal(grid.GetValue(i, j, k), acc.GetValue(i, j, k));
        }

        // Bounds and transformation
        var (min, max) = grid.IndexBBox();
        Assert.Equal(new Point3i(0, 0, 0), min);
        Assert.Equal(new Point3i(7, 7, 7), max);
        Assert.Equal((1.0, 0.0, 0.0), grid.WorldBBoxMin());
        Assert.Equal((5.0, 4.0, 4.0), grid.WorldBBoxMax());
        Assert.Equal((1f, 1f, 3f), grid.WorldToIndexF((1.5f, 0.5f, 1.5f)));
        Assert.Equal((1.0, 1.0, 3.0), grid.WorldToIndexF(1.5, 0.5, 1.5));
        grid.Extrema(out float vmin, out float vmax);
        Assert.Equal((0f, 10f), (vmin, vmax));

        // Trilinear interpolation, as NanoVDB computes it
        static float lerp(float a, float b, float w) => a + w * (b - a);
        (float, float, float) p = (2.25f, 3.5f, 4.75f);
        float vy = lerp(LeafValue(2, 3, 4), LeafValue(2, 3, 5), 0.75f);
        float vy1 = lerp(LeafValue(2, 4, 4), LeafValue(2, 4, 5), 0.75f);
        float vx = lerp(vy, vy1, 0.5f);
        vy1 = lerp(LeafValue(3, 4, 4), LeafValue(3, 4, 5), 0.75f);
        vy = lerp(LeafValue(3, 3, 4), LeafValue(3, 3, 5), 0.75f);
        float vx1 = lerp(vy, vy1, 0.5f);
        Assert.Equal(lerp(vx, vx1, 0.25f), grid.SampleTrilinear(p));
        // At a voxel center it's the voxel's value, and across the leaf's
        // edge it blends with the lower node's tile.
        Assert.Equal(LeafValue(5, 6, 1), grid.SampleTrilinear((5, 6, 1)));
        Assert.Equal(lerp(LeafValue(7, 0, 0), LowerTileValue, 0.5f), grid.SampleTrilinear((7.5f, 0, 0)));
    }

    [Fact]
    public void Errors()
    {
        Assert.Equal("Unable to open file named \"nope.nvdb\" for input",
                     Assert.Throws<NanoVDBException>(() => NanoVDBIO.ReadGrid("nope.nvdb", "density")).Message);

        string fn = ImageTests.InTestDir($"bad-{Guid.NewGuid():N}.nvdb");
        File.WriteAllBytes(fn, new byte[32]);
        Assert.Equal("Magic number error: This is not a valid nvdb file",
                     Assert.Throws<NanoVDBException>(() => NanoVDBIO.ReadGrid(fn, "density")).Message);
        Assert.True(RemoveFile(fn));

        fn = WriteFile(2, ("density", MakeGrid("density")));
        Assert.Equal("BLOSC compression codec was disabled during build",
                     Assert.Throws<NanoVDBException>(() => NanoVDBIO.ReadGrid(fn, "density")).Message);
        Assert.True(RemoveFile(fn));

        // An empty file has no grids.
        fn = ImageTests.InTestDir($"empty-{Guid.NewGuid():N}.nvdb");
        File.WriteAllBytes(fn, []);
        Assert.Null(NanoVDBIO.ReadGrid(fn, "density"));
        Assert.True(RemoveFile(fn));

        Assert.Null(new NanoVDBGridHandle_Test(MakeGrid("d", gridType: NanoVDBGridType.Double)).FloatGrid());
    }

    // (Reads a grid buffer through a file to get a handle.)
    private sealed class NanoVDBGridHandle_Test(byte[] grid)
    {
        public NanoVDBFloatGrid? FloatGrid()
        {
            string fn = WriteFile(0, ("g", grid));
            NanoVDBGridHandle h = NanoVDBIO.ReadGrid(fn, "g")!;
            Assert.True(RemoveFile(fn));
            return h.FloatGrid();
        }
    }

    [Fact]
    public void Medium()
    {
        string fn = WriteFile(1, ("density", MakeGrid("density")), ("temperature", MakeGrid("temperature")));
        string nonFog = WriteFile(0, ("density", MakeGrid("density", NanoVDBGridClass.LevelSet)));
        try
        {
            ParameterDictionary Dict(string s) => ParamDictTests.Dict(s);
            Medium m = Pbrt.Medium.Create("nanovdb", Dict($"\"string filename\" \"{fn}\" \"float scale\" 2 " +
                                                          "\"rgb sigma_a\" [1 1 1] \"rgb sigma_s\" [3 3 3]"),
                                          new Transform(), null);
            Assert.IsType<NanoVDBMedium>(m);
            Assert.True(m.IsEmissive());
            Assert.False(Pbrt.Medium.Create("nanovdb", Dict($"\"string filename\" \"{fn}\" \"float Lescale\" 0"),
                                            new Transform(), null).IsEmissive());

            // The density is sampled in index space.
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            NanoVDBFloatGrid grid = NanoVDBIO.ReadGrid(fn, "density")!.FloatGrid()!;
            Point3f p = new(2.1f, 1.7f, 2.3f);
            MediumProperties mp = m.SamplePoint(p, lambda);
            Float d = grid.SampleTrilinear(grid.WorldToIndexF(((float)p.x, (float)p.y, (float)p.z)));
            Assert.Equal(2 * d, mp.sigma_a[0], 1e-5f);
            Assert.Equal(6 * d, mp.sigma_s[0], 1e-5f);

            // The majorants bound the medium's sigma_t along a ray. (pbrt's
            // majorants only account for the voxels in the index bounding
            // box, so this skips the points that interpolate the tiles past
            // the leaf.)
            Ray ray = new(new Point3f(0, 0.3f, 0.2f), Normalize(new Vector3f(1, 0.6f, 0.7f)));
            RayMajorantIterator iter = m.SampleRay(ray, 20, lambda);
            int nSegments = 0;
            while (iter.Next() is RayMajorantSegment seg)
            {
                ++nSegments;
                for (int i = 0; i <= 10; ++i)
                {
                    Point3f pt = ray.At(Lerp(i / 10f, seg.tMin, seg.tMax));
                    var pi = grid.WorldToIndexF(((float)pt.x, (float)pt.y, (float)pt.z));
                    if (pi.x >= 7 || pi.y >= 7 || pi.z >= 7)
                        continue;
                    MediumProperties mpt = m.SamplePoint(pt, lambda);
                    Assert.True(mpt.sigma_a[0] + mpt.sigma_s[0] <= seg.sigma_maj[0] * (1 + 1e-5f));
                }
            }
            Assert.True(nSegments > 1);

            Assert.Throws<FatalErrorException>(() =>
                Pbrt.Medium.Create("nanovdb", Dict($"\"string filename\" \"{nonFog}\""), new Transform(), null));
            Assert.Throws<FatalErrorException>(() =>
                Pbrt.Medium.Create("nanovdb", Dict($"\"string filename\" \"{fn}\" \"string gridname\" \"nope\""),
                                   new Transform(), null));
            Assert.Throws<FatalErrorException>(() => Pbrt.Medium.Create("nanovdb", Dict(""), new Transform(), null));
        }
        finally
        {
            Assert.True(RemoveFile(fn));
            Assert.True(RemoveFile(nonFog));
        }
    }
}

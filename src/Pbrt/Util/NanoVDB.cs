// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: MPL-2.0
//
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Unlike the rest of pbrt-cs (Apache-2.0), this file is a translation of
// NanoVDB's MPL-2.0 source and so is under the MPL-2.0.
//
// C# port of the parts of NanoVDB (version 32.3, in pbrt's src/ext/openvdb)
// that pbrt's NanoVDBMedium uses: nanovdb::io::readGrid() by grid name,
// GridHandle's metadata, and the float grid's worldToIndexF(), worldBBox(),
// indexBBox(), voxel lookups (with a ReadAccessor-style node cache, which
// gives the same values as the tree's getValue()) and the trilinear
// SampleFromVoxels<..., 1, false> sampler.
//
// A grid is kept as the raw NanoVDB buffer, as in NanoVDB, and its structs
// are read at their byte offsets (the offsets are those of the x86-64 build
// that pbrt uses). As in pbrt (whose media.cpp defines NANOVDB_USE_ZIP),
// uncompressed and ZIP-compressed (zlib) files can be read, but not BLOSC
// ones.

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pbrt;

// NanoVDB's std::runtime_error
public sealed class NanoVDBException(string message) : Exception(message);

public enum NanoVDBGridType : uint
{
    Unknown = 0, Float = 1, Double = 2, Int16 = 3, Int32 = 4, Int64 = 5, Vec3f = 6, Vec3d = 7, Mask = 8,
    Half = 9, UInt32 = 10, Boolean = 11, RGBA8 = 12, Fp4 = 13, Fp8 = 14, Fp16 = 15, FpN = 16, Vec4f = 17,
    Vec4d = 18, End = 19
}

public enum NanoVDBGridClass : uint
{
    Unknown = 0, LevelSet = 1, FogVolume = 2, Staggered = 3, PointIndex = 4, PointData = 5, Topology = 6,
    VoxelVolume = 7, End = 8
}

// GridHandle (with a single grid)
public sealed class NanoVDBGridHandle
{
    // GridData offsets
    private const int GridTypeOffset = 636, GridClassOffset = 632;
    internal const int GridDataSize = 672;
    // TreeData offsets (the tree follows the GridData)
    private const int VoxelCountOffset = 56;

    internal readonly byte[] data;

    internal NanoVDBGridHandle(byte[] data) => this.data = data;

    // (GridMetaData's accessors)
    public NanoVDBGridType GridType() => (NanoVDBGridType)Unsafe.ReadUnaligned<uint>(ref data[GridTypeOffset]);
    public NanoVDBGridClass GridClass() => (NanoVDBGridClass)Unsafe.ReadUnaligned<uint>(ref data[GridClassOffset]);
    public bool IsFogVolume() => GridClass() == NanoVDBGridClass.FogVolume;
    public bool IsUnknown() => GridClass() == NanoVDBGridClass.Unknown;

    public ulong ActiveVoxelCount() =>
        Unsafe.ReadUnaligned<ulong>(ref data[GridDataSize + VoxelCountOffset]);

    // GridHandle::grid<float>()
    public NanoVDBFloatGrid? FloatGrid() =>
        GridType() == NanoVDBGridType.Float ? new NanoVDBFloatGrid(data) : null;
}

public static class NanoVDBIO
{
    private const ulong MagicNumber = 0x304244566f6e614eUL; // "NanoVDB0"
    private const uint MajorVersionNumber = 32;

    // nanovdb::io::stringHash()
    public static ulong StringHash(string str)
    {
        ulong hash = 0;
        foreach (byte c in Encoding.UTF8.GetBytes(str))
        {
            if (c == 0)
                break;
            ulong overflow = hash >> (64 - 8);
            hash *= 67; // Next-ish prime after 26 + 26 + 10
            hash += c + overflow;
        }
        return hash;
    }

    private static ulong ReverseEndianness(ulong val) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(val);

    private static string VersionString(uint v) => $"{(v >> 21) & ((1u << 11) - 1)}.{(v >> 10) & ((1u << 11) - 1)}.{v & ((1u << 10) - 1)}";

    // Segment::read(): returns the metadata of the segment's grids
    // (gridSize, fileSize, nameKey, name) and its codec, or null at the end
    // of the file.
    private static (List<(ulong gridSize, ulong fileSize, ulong nameKey, string name)> meta, ushort codec)?
        ReadSegment(Stream s)
    {
        byte[] header = new byte[16];
        int n = s.ReadAtLeast(header, 16, throwOnEndOfStream: false);
        if (n == 0)
            return null;
        ulong magic = BitConverter.ToUInt64(header, 0);
        uint version = BitConverter.ToUInt32(header, 8);
        ushort gridCount = BitConverter.ToUInt16(header, 12);
        ushort codec = BitConverter.ToUInt16(header, 14);
        if (n < 16 || magic != MagicNumber)
        {
            if (magic == ReverseEndianness(MagicNumber))
                throw new NanoVDBException("This nvdb file has reversed endianness");
            throw new NanoVDBException("Magic number error: This is not a valid nvdb file");
        }
        uint major = (version >> 21) & ((1u << 11) - 1);
        if (major != MajorVersionNumber)
        {
            string ss = major < MajorVersionNumber
                ? $"The file contains an older version of NanoVDB: {VersionString(version)}!\n\t" +
                  $"Recommendation: Re-generate this NanoVDB file with this version: {MajorVersionNumber}.X of NanoVDB"
                : $"This tool was compiled against an older version of NanoVDB: {MajorVersionNumber}.X!\n\t" +
                  $"Recommendation: Re-compile this tool against the newer version: {major}.X of NanoVDB";
            throw new NanoVDBException("An unrecoverable error in nanovdb::Segment::read:\n\tIncompatible file format: " +
                                       ss);
        }
        List<(ulong, ulong, ulong, string)> meta = [];
        byte[] md = new byte[176];
        for (int i = 0; i < gridCount; ++i)
        {
            // MetaData followed by the name
            if (s.ReadAtLeast(md, 176, throwOnEndOfStream: false) < 176)
                throw new NanoVDBException("Failed reading GridMetaData");
            ulong gridSize = BitConverter.ToUInt64(md, 0), fileSize = BitConverter.ToUInt64(md, 8);
            ulong nameKey = BitConverter.ToUInt64(md, 16);
            uint nameSize = BitConverter.ToUInt32(md, 136);
            byte[] name = new byte[nameSize];
            if (s.ReadAtLeast(name, (int)nameSize, throwOnEndOfStream: false) < nameSize)
                throw new NanoVDBException("Failed reading GridMetaData");
            int len = Array.IndexOf(name, (byte)0);
            meta.Add((gridSize, fileSize, nameKey, Encoding.UTF8.GetString(name, 0, len < 0 ? name.Length : len)));
        }
        return (meta, codec);
    }

    // Internal::read()
    private static byte[] Read(Stream s, ulong gridSize, ushort codec)
    {
        byte[] data = new byte[gridSize];
        switch (codec)
        {
            case 1: // ZIP
            {
                byte[] sizeBytes = new byte[8];
                bool ok = s.ReadAtLeast(sizeBytes, 8, throwOnEndOfStream: false) == 8;
                ulong size = BitConverter.ToUInt64(sizeBytes);
                byte[] tmp = new byte[ok ? size : 0];
                ok &= s.ReadAtLeast(tmp, tmp.Length, throwOnEndOfStream: false) == tmp.Length;
                // (zlib's uncompress(); its errors show up as the wrong
                // number of bytes.)
                int numBytes = 0;
                try
                {
                    using ZLibStream z = new(new MemoryStream(tmp), CompressionMode.Decompress);
                    numBytes = z.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
                }
                catch (InvalidDataException)
                {
                }
                if ((ulong)numBytes != gridSize)
                    throw new NanoVDBException("UNZIP failed on byte size");
                if (!ok)
                    throw new NanoVDBException("Failed to read Tree from file");
                break;
            }
            case 2: // BLOSC
                throw new NanoVDBException("BLOSC compression codec was disabled during build");
            default:
                if (s.ReadAtLeast(data, data.Length, throwOnEndOfStream: false) < data.Length)
                    throw new NanoVDBException("Failed to read Tree from file");
                break;
        }
        return data;
    }

    // nanovdb::io::readGrid(fileName, gridName); null is the empty handle.
    public static NanoVDBGridHandle? ReadGrid(string fileName, string gridName)
    {
        FileStream s;
        try
        {
            s = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        }
        catch (Exception)
        {
            throw new NanoVDBException($"Unable to open file named \"{fileName}\" for input");
        }
        using (s)
        {
            ulong key = StringHash(gridName);
            while (ReadSegment(s) is var (meta, codec))
            {
                long seek = 0;
                foreach (var m in meta)
                {
                    if (m.nameKey == key && m.name == gridName)
                    {
                        s.Seek(seek, SeekOrigin.Current);
                        return new NanoVDBGridHandle(Read(s, m.gridSize, codec));
                    }
                    seek += (long)m.fileSize;
                }
                s.Seek(seek, SeekOrigin.Current);
            }
            return null;
        }
    }
}

// NanoGrid<float>
public sealed class NanoVDBFloatGrid
{
    // GridData offsets
    private const int MapOffset = 296, WorldBBoxOffset = 560;
    private const int InvMatFOffset = MapOffset + 36, VecFOffset = MapOffset + 72;
    // TreeData, RootData and node offsets and sizes
    private const int TreeOffset = NanoVDBGridHandle.GridDataSize;
    private const int RootDataSize = 64, RootTableSizeOffset = 24, RootBackgroundOffset = 28;
    private const int RootMinimumOffset = 32, RootMaximumOffset = 36;
    private const int TileSize = 32, TileChildOffset = 8, TileValueOffset = 20;
    private const int UpperChildMaskOffset = 4128, UpperTableOffset = 8256;
    private const int LowerChildMaskOffset = 544, LowerTableOffset = 1088;
    private const int LeafValuesOffset = 96;

    private readonly byte[] data;
    private readonly int rootOffset;
    private readonly float[] invMatF = new float[9], vecF = new float[3];
    private readonly float background;
    // The root's tiles by key (NanoVDB searches them linearly for the first
    // match).
    private readonly Dictionary<ulong, int> rootTiles = [];

    internal NanoVDBFloatGrid(byte[] data)
    {
        this.data = data;
        rootOffset = TreeOffset + (int)Unsafe.ReadUnaligned<ulong>(ref data[TreeOffset + 3 * 8]);
        for (int i = 0; i < 9; ++i)
            invMatF[i] = F(InvMatFOffset + 4 * i);
        for (int i = 0; i < 3; ++i)
            vecF[i] = F(VecFOffset + 4 * i);
        background = F(rootOffset + RootBackgroundOffset);
        uint tableSize = Unsafe.ReadUnaligned<uint>(ref data[rootOffset + RootTableSizeOffset]);
        for (int i = 0; i < tableSize; ++i)
            rootTiles.TryAdd(Unsafe.ReadUnaligned<ulong>(ref data[rootOffset + RootDataSize + i * TileSize]), i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float F(long offset) => Unsafe.ReadUnaligned<float>(ref data[offset]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long L(long offset) => Unsafe.ReadUnaligned<long>(ref data[offset]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MaskIsOn(long maskOffset, uint n) =>
        (Unsafe.ReadUnaligned<ulong>(ref data[maskOffset + 8 * (n >> 6)]) & (1ul << (int)(n & 63))) != 0;

    // worldBBox(): min and max
    public (double x, double y, double z) WorldBBoxMin() =>
        (D(WorldBBoxOffset), D(WorldBBoxOffset + 8), D(WorldBBoxOffset + 16));

    public (double x, double y, double z) WorldBBoxMax() =>
        (D(WorldBBoxOffset + 24), D(WorldBBoxOffset + 32), D(WorldBBoxOffset + 40));

    private double D(long offset) => Unsafe.ReadUnaligned<double>(ref data[offset]);

    // indexBBox(), the root's bounding box
    public (Point3i min, Point3i max) IndexBBox()
    {
        int I(int o) => Unsafe.ReadUnaligned<int>(ref data[rootOffset + o]);
        return (new Point3i(I(0), I(4), I(8)), new Point3i(I(12), I(16), I(20)));
    }

    // tree().extrema()
    public void Extrema(out float min, out float max)
    {
        min = F(rootOffset + RootMinimumOffset);
        max = F(rootOffset + RootMaximumOffset);
    }

    // worldToIndexF() of a Vec3<float> (Map::applyInverseMapF() and
    // matMult() with fmaf())
    public (float x, float y, float z) WorldToIndexF((float x, float y, float z) p)
    {
        float x = p.x - vecF[0], y = p.y - vecF[1], z = p.z - vecF[2];
        float[] m = invMatF;
        return (MathF.FusedMultiplyAdd(x, m[0], MathF.FusedMultiplyAdd(y, m[1], z * m[2])),
                MathF.FusedMultiplyAdd(x, m[3], MathF.FusedMultiplyAdd(y, m[4], z * m[5])),
                MathF.FusedMultiplyAdd(x, m[6], MathF.FusedMultiplyAdd(y, m[7], z * m[8])));
    }

    // worldToIndexF() of a Vec3R: the subtraction and the products of z are
    // doubles that fmaf() takes as floats.
    public (double x, double y, double z) WorldToIndexF(double px, double py, double pz)
    {
        double x = px - vecF[0], y = py - vecF[1], z = pz - vecF[2];
        float[] m = invMatF;
        return (MathF.FusedMultiplyAdd((float)x, m[0], MathF.FusedMultiplyAdd((float)y, m[1], (float)(z * m[2]))),
                MathF.FusedMultiplyAdd((float)x, m[3], MathF.FusedMultiplyAdd((float)y, m[4], (float)(z * m[5]))),
                MathF.FusedMultiplyAdd((float)x, m[6], MathF.FusedMultiplyAdd((float)y, m[7], (float)(z * m[8]))));
    }

    // ReadAccessor<float, 0, 1, 2>: caches the last leaf, lower and upper
    // nodes visited (their byte offsets in the buffer).
    public sealed class Accessor(NanoVDBFloatGrid grid)
    {
        private int leafKeyX = int.MaxValue, leafKeyY, leafKeyZ;
        private int lowerKeyX = int.MaxValue, lowerKeyY, lowerKeyZ;
        private int upperKeyX = int.MaxValue, upperKeyY, upperKeyZ;
        private long leaf, lower, upper;

        public float GetValue(int i, int j, int k)
        {
            if ((i & ~7) == leafKeyX && (j & ~7) == leafKeyY && (k & ~7) == leafKeyZ)
                return grid.LeafValue(leaf, i, j, k);
            if ((i & ~127) == lowerKeyX && (j & ~127) == lowerKeyY && (k & ~127) == lowerKeyZ)
                return LowerValue(lower, i, j, k);
            if ((i & ~4095) == upperKeyX && (j & ~4095) == upperKeyY && (k & ~4095) == upperKeyZ)
                return UpperValue(upper, i, j, k);
            // RootNode::getValueAndCache()
            long child = grid.FindRootChild(i, j, k, out float value);
            if (child < 0)
                return value;
            upper = child;
            (upperKeyX, upperKeyY, upperKeyZ) = (i & ~4095, j & ~4095, k & ~4095);
            return UpperValue(upper, i, j, k);
        }

        private float UpperValue(long node, int i, int j, int k)
        {
            uint n = UpperOffset(i, j, k);
            if (!grid.MaskIsOn(node + UpperChildMaskOffset, n))
                return grid.F(node + UpperTableOffset + 8 * n);
            lower = node + grid.L(node + UpperTableOffset + 8 * n);
            (lowerKeyX, lowerKeyY, lowerKeyZ) = (i & ~127, j & ~127, k & ~127);
            return LowerValue(lower, i, j, k);
        }

        private float LowerValue(long node, int i, int j, int k)
        {
            uint n = LowerOffset(i, j, k);
            if (!grid.MaskIsOn(node + LowerChildMaskOffset, n))
                return grid.F(node + LowerTableOffset + 8 * n);
            leaf = node + grid.L(node + LowerTableOffset + 8 * n);
            (leafKeyX, leafKeyY, leafKeyZ) = (i & ~7, j & ~7, k & ~7);
            return grid.LeafValue(leaf, i, j, k);
        }
    }

    // CoordToOffset() of the upper and lower internal nodes
    private static uint UpperOffset(int i, int j, int k) =>
        (uint)((((i & 4095) >> 7) << 10) | (((j & 4095) >> 7) << 5) | ((k & 4095) >> 7));

    private static uint LowerOffset(int i, int j, int k) =>
        (uint)((((i & 127) >> 3) << 8) | (((j & 127) >> 3) << 4) | ((k & 127) >> 3));

    private float LeafValue(long leaf, int i, int j, int k) =>
        F(leaf + LeafValuesOffset + 4 * (((i & 7) << 6) | ((j & 7) << 3) | (k & 7)));

    // RootNode::findTile(): returns the offset of the tile's child node, or
    // -1 with the tile's value or the background.
    private long FindRootChild(int i, int j, int k, out float value)
    {
        ulong key = ((ulong)((uint)k >> 12)) | ((ulong)((uint)j >> 12) << 21) | ((ulong)((uint)i >> 12) << 42);
        if (!rootTiles.TryGetValue(key, out int t))
        {
            value = background;
            return -1;
        }
        long tile = rootOffset + RootDataSize + (long)t * TileSize;
        long child = L(tile + TileChildOffset);
        if (child == 0)
        {
            value = F(tile + TileValueOffset);
            return -1;
        }
        value = 0;
        return rootOffset + child;
    }

    // tree().getValue()
    public float GetValue(int i, int j, int k) => new Accessor(this).GetValue(i, j, k);

    [ThreadStatic]
    private static (NanoVDBFloatGrid? grid, Accessor? acc) threadAccessor;

    // SampleFromVoxels<FloatTree, 1, false>()(xyz). (NanoVDB looks up the
    // values through the tree; a cached accessor gives the same values.)
    public float SampleTrilinear((float x, float y, float z) xyz)
    {
        Accessor acc;
        if (threadAccessor.grid == this)
            acc = threadAccessor.acc!;
        else
        {
            acc = new Accessor(this);
            threadAccessor = (this, acc);
        }

        static float lerp(float a, float b, float w) => a + w * (b - a);

        // Floor()
        float fx = MathF.Floor(xyz.x), fy = MathF.Floor(xyz.y), fz = MathF.Floor(xyz.z);
        float ux = xyz.x - fx, uy = xyz.y - fy, uz = xyz.z - fz;
        int ci = (int)fx, cj = (int)fy, ck = (int)fz;

        float vx, vx1, vy, vy1, vz, vz1;

        vz = acc.GetValue(ci, cj, ck);
        vz1 = acc.GetValue(ci, cj, ck + 1);
        vy = lerp(vz, vz1, uz);

        vz1 = acc.GetValue(ci, cj + 1, ck + 1);
        vz = acc.GetValue(ci, cj + 1, ck);
        vy1 = lerp(vz, vz1, uz);

        vx = lerp(vy, vy1, uy);

        vz = acc.GetValue(ci + 1, cj + 1, ck);
        vz1 = acc.GetValue(ci + 1, cj + 1, ck + 1);
        vy1 = lerp(vz, vz1, uz);

        vz1 = acc.GetValue(ci + 1, cj, ck + 1);
        vz = acc.GetValue(ci + 1, cj, ck);
        vy = lerp(vz, vz1, uz);

        vx1 = lerp(vy, vy1, uy);

        return lerp(vx, vx1, ux);
    }
}

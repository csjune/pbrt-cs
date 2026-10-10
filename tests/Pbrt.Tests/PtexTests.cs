// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Checks of the Ptex port (Ptex's own tests need its writer; the reader and
// filters were compared with the C++ library separately, using files from
// its PtexWriter). The tests write small .ptx files with a minimal writer:
// one level of zipped faces.

using System.IO.Compression;
using Pbrt.Ptex;

namespace Pbrt.Tests;

[Collection("Options")]
public class PtexTests
{
    private static byte[] Zip(byte[] data)
    {
        MemoryStream ms = new();
        using (ZLibStream z = new(ms, CompressionLevel.Optimal, true))
            z.Write(data);
        return ms.ToArray();
    }

    private sealed record Face(int ulog2, int vlog2, float[] texels, int[] adjfaces, int[] adjedges, byte flags = 0);

    // Writes a float-texel file; texels are interleaved, v-major.
    private static string Write(MeshType mt, int nchannels, Face[] faces, BorderMode border = BorderMode.Clamp)
    {
        int nfaces = faces.Length, pixelsize = 4 * nchannels;
        MemoryStream faceinfo = new(), constdata = new();
        List<byte[]> blocks = [];
        foreach (Face f in faces)
        {
            BinaryWriter w = new(faceinfo);
            w.Write((sbyte)f.ulog2);
            w.Write((sbyte)f.vlog2);
            w.Write((byte)(f.adjedges[0] | (f.adjedges[1] << 2) | (f.adjedges[2] << 4) | (f.adjedges[3] << 6)));
            w.Write(f.flags);
            foreach (int a in f.adjfaces)
                w.Write(a);
            // constant data: the face average
            int n = (1 << f.ulog2) * (1 << f.vlog2);
            BinaryWriter cw = new(constdata);
            for (int c = 0; c < nchannels; c++)
            {
                float sum = 0;
                for (int i = 0; i < n; i++)
                    sum += f.texels[i * nchannels + c];
                cw.Write(sum / n);
            }
            // face data: zipped channel planes
            MemoryStream planes = new();
            BinaryWriter pw = new(planes);
            for (int c = 0; c < nchannels; c++)
                for (int i = 0; i < n; i++)
                    pw.Write(f.texels[i * nchannels + c]);
            blocks.Add(Zip(planes.ToArray()));
        }
        MemoryStream fdh = new();
        foreach (byte[] b in blocks)
            new BinaryWriter(fdh).Write((uint)b.Length | (1u << 30)); // enc_zipped
        byte[] fdhZip = Zip(fdh.ToArray()), faceinfoZip = Zip(faceinfo.ToArray()), constZip = Zip(constdata.ToArray());
        long leveldatasize = fdhZip.Length + blocks.Sum(b => (long)b.Length);

        string fn = ImageTests.InTestDir($"ptex-{Guid.NewGuid():N}.ptx");
        using (BinaryWriter w = new(File.Create(fn)))
        {
            // Header
            w.Write((uint)('P' | ('t' << 8) | ('e' << 16) | ('x' << 24)));
            w.Write(1u);                                  // version
            w.Write((uint)mt);
            w.Write((uint)DataType.Float);
            w.Write(-1);                                  // alphachan
            w.Write((ushort)nchannels);
            w.Write((ushort)1);                           // nlevels
            w.Write((uint)nfaces);
            w.Write(40u);                                 // extheadersize
            w.Write((uint)faceinfoZip.Length);
            w.Write((uint)constZip.Length);
            w.Write(16u);                                 // levelinfosize
            w.Write(0u);                                  // minorversion
            w.Write((ulong)leveldatasize);
            w.Write(0u);                                  // metadatazipsize
            w.Write(0u);                                  // metadatamemsize
            // ExtHeader
            w.Write((ushort)border);
            w.Write((ushort)0);
            w.Write((ushort)border);
            w.Write((ushort)0);
            w.Write(new byte[32]);
            w.Write(faceinfoZip);
            w.Write(constZip);
            // LevelInfo
            w.Write((ulong)leveldatasize);
            w.Write((uint)fdhZip.Length);
            w.Write((uint)nfaces);
            w.Write(fdhZip);
            foreach (byte[] b in blocks)
                w.Write(b);
            _ = pixelsize;
        }
        return fn;
    }

    private static readonly int[] NoAdj = [-1, -1, -1, -1], Zeros = [0, 0, 0, 0];

    private static float[] Ramp(int ulog2, int vlog2, int nc, float offset)
    {
        int u = 1 << ulog2, v = 1 << vlog2;
        float[] t = new float[u * v * nc];
        for (int y = 0; y < v; y++)
            for (int x = 0; x < u; x++)
                for (int c = 0; c < nc; c++)
                    t[(y * u + x) * nc + c] = offset + x + 10 * y + 100 * c;
        return t;
    }

    [Fact]
    public void ReadAndPointFilter()
    {
        string fn = Write(MeshType.Quad, 3, [new Face(2, 1, Ramp(2, 1, 3, 0), NoAdj, Zeros),
                                             new Face(0, 0, [7, 8, 9], NoAdj, Zeros)]);
        try
        {
            PtexReader tx = PtexReader.Open(fn, out string error)!;
            Assert.Equal("", error);
            Assert.Equal(2, tx.numFaces());
            Assert.Equal(3, tx.numChannels());
            Assert.Equal(DataType.Float, tx.dataType());
            Assert.Equal(new Res(2, 1), tx.getFaceInfo(0).res);

            Span<float> r = stackalloc float[3];
            tx.getPixel(0, 3, 1, r, 0, 3);
            Assert.Equal([13, 113, 213], r.ToArray());
            tx.getPixel(1, 0, 0, r, 1, 2);
            Assert.Equal([8, 9], r[..2].ToArray());

            // The point filter picks the texel containing (u, v).
            PtexFilter point = PtexFilter.getFilter(tx, new FilterOptions(FilterType.Point))!;
            point.eval(r, 0, 3, 0, 0.6f, 0.9f, 0, 0, 0, 0);
            Assert.Equal([12, 112, 212], r.ToArray());

            // Reduced data is the average of each 2x2 block (for floats).
            PtexFaceData d = tx.getData(0, new Res(1, 0));
            byte[] px = new byte[12];
            d.GetPixel(1, 0, px);
            Assert.Equal((2 + 3 + 12 + 13) / 4f, BitConverter.ToSingle(px, 0));
            // Res 0 is the constant (average) value.
            Assert.True(tx.getData(0, new Res(0, 0)).IsConstant());
        }
        finally
        {
            Assert.True(RemoveFile(fn));
        }
    }

    [Theory]
    [InlineData(FilterType.BSpline)]
    [InlineData(FilterType.Box)]
    [InlineData(FilterType.Bilinear)]
    [InlineData(FilterType.Gaussian)]
    [InlineData(FilterType.CatmullRom)]
    public void ConstantAcrossFaces(FilterType filter)
    {
        // A 2x2 grid of faces with the same constant value: filtering gives
        // that value anywhere, including across the edges and corner.
        int[][] adj = [[-1, 1, 2, -1], [-1, -1, 3, 0], [0, 3, -1, -1], [1, -1, -1, 2]];
        int[][] edges = [[0, 3, 0, 0], [0, 0, 0, 1], [2, 3, 0, 0], [2, 0, 0, 1]];
        Face[] faces = [.. Enumerable.Range(0, 4).Select(i =>
            new Face(3, 2, Enumerable.Repeat(0.25f, 32).ToArray(), adj[i], edges[i]))];
        string fn = Write(MeshType.Quad, 1, faces);
        try
        {
            PtexReader tx = PtexReader.Open(fn, out _)!;
            PtexFilter f = PtexFilter.getFilter(tx, new FilterOptions(filter))!;
            Span<float> r = stackalloc float[1];
            RNG rng = new(3);
            for (int i = 0; i < 100; ++i)
            {
                int face = (int)(rng.Uniform<uint>() % 4);
                float u = rng.Uniform<float>(), v = rng.Uniform<float>();
                float w = 0.3f * rng.Uniform<float>() * rng.Uniform<float>();
                f.eval(r, 0, 1, face, u, v, w, 0, 0, w);
                Assert.Equal(0.25f, r[0], 1e-5f);
            }
        }
        finally
        {
            Assert.True(RemoveFile(fn));
        }
    }

    [Fact]
    public void BSplineRamp()
    {
        // On a linear ramp far from the edges, the (normalized) b-spline
        // filter reproduces the linear function.
        string fn = Write(MeshType.Quad, 1, [new Face(5, 5, Ramp(5, 5, 1, 0), NoAdj, Zeros)]);
        try
        {
            PtexReader tx = PtexReader.Open(fn, out _)!;
            PtexFilter f = PtexFilter.getFilter(tx, new FilterOptions(FilterType.BSpline))!;
            Span<float> r = stackalloc float[1];
            f.eval(r, 0, 1, 0, 0.5f, 0.5f, 1 / 32f, 0, 0, 1 / 32f);
            // texel centers are at (i + 0.5) / 32, so u = 0.5 is between
            // texels 15 and 16.
            Assert.Equal(15.5f + 10 * 15.5f, r[0], 1e-3f);
        }
        finally
        {
            Assert.True(RemoveFile(fn));
        }
    }

    [Fact]
    public void Triangles()
    {
        // One triangle face with a constant value
        string fn = Write(MeshType.Triangle, 1, [new Face(2, 2, Enumerable.Repeat(0.75f, 16).ToArray(), NoAdj, Zeros)]);
        try
        {
            PtexReader tx = PtexReader.Open(fn, out _)!;
            Span<float> r = stackalloc float[1];
            foreach (FilterType filter in (FilterType[])[FilterType.Point, FilterType.BSpline])
            {
                PtexFilter f = PtexFilter.getFilter(tx, new FilterOptions(filter))!;
                f.eval(r, 0, 1, 0, 0.3f, 0.2f, 0.05f, 0, 0, 0.05f);
                Assert.Equal(0.75f, r[0], 1e-5f);
            }
        }
        finally
        {
            Assert.True(RemoveFile(fn));
        }
    }

    [Fact]
    public void Errors()
    {
        Assert.Null(PtexReader.Open("nope.ptx", out string error));
        Assert.Equal("Can't open ptex file: nope.ptx\nNo such file or directory", error);

        string fn = ImageTests.InTestDir($"bad-{Guid.NewGuid():N}.ptx");
        File.WriteAllBytes(fn, new byte[64]);
        Assert.Null(PtexReader.Open(fn, out error));
        Assert.Equal($"Not a ptex file: {fn}", error);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void Half()
    {
        // Half conversions are exact for representable values,
        // and Ptex rounds half up.
        foreach (float v in (float[])[0, 1, -2, 0.5f, 65504, 6.1035156e-05f, 5.9604645e-08f])
            Assert.Equal(v, PtexHalf.ToFloat(PtexHalf.FromFloat(v)));
        Assert.Equal((ushort)0x3c00, PtexHalf.FromFloat(1));
        Assert.Equal((ushort)0x7c00, PtexHalf.FromFloat(1e6f));
        // 1 + 2^-11 is halfway between 1 and the next half; Ptex rounds up.
        Assert.Equal((ushort)0x3c01, PtexHalf.FromFloat(1 + MathF.Pow(2, -11)));
    }

    [Fact]
    public void Texture()
    {
        string fn = Write(MeshType.Quad, 1, [new Face(0, 0, [0.4f], NoAdj, Zeros), new Face(0, 0, [0.8f], NoAdj, Zeros)]);
        try
        {
            FloatPtexTexture tex = new(fn, ColorEncoding.Linear, 2);
            TextureEvalContext ctx = new() { uv = new Point2f(0.5f, 0.5f), faceIndex = 1 };
            Assert.Equal(1.6f, tex.Evaluate(ctx), 1e-6f);
            ctx.faceIndex = 0;
            Assert.Equal(0.8f, tex.Evaluate(ctx), 1e-6f);

            // A missing file gives zero.
            FloatPtexTexture missing = new("nope.ptx", ColorEncoding.Linear, 1);
            Assert.Equal(0f, missing.Evaluate(ctx));
        }
        finally
        {
            Assert.True(RemoveFile(fn));
        }
    }
}

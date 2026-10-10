// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the ports of src/pbrt/util/mesh.h, loopsubdiv.h and RPly (pbrt
// has no tests for them). The PLY reading and writing and LoopSubdivide()
// were also checked against pbrt's code with RPly built from src/ext: the
// same meshes from 32 generated files (ASCII, both binary byte orders, gzip,
// CRLF headers, all property types, other uv names, extra properties and
// elements, faces that aren't triangles or quads, errors), byte-identical
// written files, and bit-identical subdivisions of 8 meshes at levels 0-3.

using System.IO.Compression;
using System.Text;

namespace Pbrt.Tests;

public class MeshTests
{
    private static string InTestDir(string path) => ImageTests.InTestDir(path);

    private static TriQuadMesh ReadPLYText(string name, string text)
    {
        string fn = InTestDir(name);
        File.WriteAllText(fn, text, Encoding.Latin1);
        try
        {
            return TriQuadMesh.ReadPLY(fn);
        }
        finally
        {
            File.Delete(fn);
        }
    }

    [Fact]
    public void WriteReadPLY()
    {
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(.5f, .5f, 1)];
        Normal3f[] n = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0)];
        Point2f[] uv = [new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(.5f, .5f)];
        int[] tri = [0, 1, 4, 1, 2, 4];
        int[] quad = [0, 1, 3, 2];
        int[] faceIndices = [7, 8, 9];

        string fn = InTestDir("mesh.ply");
        Assert.True(WritePLY(fn, tri, quad, p, n, uv, faceIndices));

        // The header is RPly's, for a little-endian machine.
        byte[] bytes = File.ReadAllBytes(fn);
        string header = "ply\nformat binary_little_endian 1.0\nelement vertex 5\n" +
                        "property float x\nproperty float y\nproperty float z\n" +
                        "property float nx\nproperty float ny\nproperty float nz\n" +
                        "property float u\nproperty float v\n" +
                        "element face 3\nproperty list uint8 int vertex_indices\nproperty int face_indices\n" +
                        "end_header\n";
        Assert.Equal(header, Encoding.ASCII.GetString(bytes, 0, header.Length));
        Assert.Equal(header.Length + 5 * 8 * 4 + 2 * (1 + 3 * 4 + 4) + (1 + 4 * 4 + 4), bytes.Length);

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(fn);
        Assert.Equal(p, mesh.p);
        Assert.Equal(n, mesh.n);
        Assert.Equal(uv, mesh.uv);
        Assert.Equal(tri, mesh.triIndices);
        // Quads are reordered for bilinear patches when they're read.
        Assert.Equal([0, 1, 2, 3], mesh.quadIndices);
        // (pbrt writes the first face indices again for the quads.)
        Assert.Equal([7, 8, 7], mesh.faceIndices);
        Assert.True(RemoveFile(fn));

        // Without the optional data
        Assert.True(WritePLY(fn, tri, [], p, [], [], []));
        mesh = TriQuadMesh.ReadPLY(fn);
        Assert.Equal(p, mesh.p);
        Assert.Empty(mesh.n);
        Assert.Empty(mesh.uv);
        Assert.Empty(mesh.faceIndices);
        Assert.Empty(mesh.quadIndices);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ReadASCII()
    {
        TriQuadMesh mesh = ReadPLYText("ascii.ply",
            "ply\r\nformat ascii 1.0\r\ncomment a comment\r\nobj_info info\r\n" +
            "element vertex 4\r\nproperty double x\r\nproperty float y\r\nproperty uchar red\r\n" +
            "property float z\r\nproperty float s\r\nproperty float t\r\n" +
            "element face 3\r\nproperty list uchar uint vertex_indices\r\nend_header\r\n" +
            "0 0 255 0 0 0\r\n1.5 0 0 0 1 0\r\n1 1e1 7 -2.5 1 1\r\n0 1 0 0 0 1\r\n" +
            "3 0 1 2\r\n4 0 1 2 3\r\n  3 3 2 1\r\n");
        Assert.Equal([new(0, 0, 0), new(1.5f, 0, 0), new(1, 10, -2.5f), new(0, 1, 0)], mesh.p);
        Assert.Equal([new(0, 0), new(1, 0), new(1, 1), new(0, 1)], mesh.uv);
        Assert.Empty(mesh.n);
        Assert.Equal([0, 1, 2, 3, 2, 1], mesh.triIndices);
        Assert.Equal([0, 1, 3, 2], mesh.quadIndices);
    }

    [Fact]
    public void ReadBigEndianAndGzip()
    {
        // Big-endian doubles and int16 list lengths, with an element before
        // the vertices that is skipped.
        MemoryStream ms = new();
        ms.Write(Encoding.ASCII.GetBytes(
            "ply\nformat binary_big_endian 1.0\nelement material 1\nproperty int id\n" +
            "element vertex 3\nproperty double x\nproperty double y\nproperty double z\n" +
            "property float texture_u\nproperty float texture_v\n" +
            "element face 1\nproperty list short ushort vertex_indices\nproperty int face_indices\nend_header\n"));
        void be(byte[] b) { Array.Reverse(b); ms.Write(b); }
        be(BitConverter.GetBytes(42));
        double[][] verts = [[0, 0, 0], [2, 0, 0], [0, 3, -1]];
        foreach (double[] v in verts)
        {
            foreach (double c in v)
                be(BitConverter.GetBytes(c));
            be(BitConverter.GetBytes((float)v[0] / 2));
            be(BitConverter.GetBytes((float)v[1] / 3));
        }
        be(BitConverter.GetBytes((short)3));
        foreach (ushort i in (ushort[])[2, 1, 0])
            be(BitConverter.GetBytes(i));
        be(BitConverter.GetBytes(5));

        string fn = InTestDir("be.ply.gz");
        using (FileStream fs = File.Create(fn))
        using (GZipStream gz = new(fs, CompressionLevel.Fastest))
            gz.Write(ms.ToArray());

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(fn);
        Assert.Equal([new(0, 0, 0), new(2, 0, 0), new(0, 3, -1)], mesh.p);
        Assert.Equal([new(0, 0), new(1, 0), new(0, 1)], mesh.uv);
        Assert.Equal([2, 1, 0], mesh.triIndices);
        Assert.Equal([5], mesh.faceIndices);
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ReadErrors()
    {
        const string header = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\n" +
                              "property float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n";
        const string verts = "0 0 0\n1 0 0\n0 1 0\n";

        // Faces that aren't triangles or quads are ignored.
        TriQuadMesh mesh = ReadPLYText("pentagon.ply", header.Replace("face 1", "face 2") + verts + "5 0 1 2 0 1\n3 0 1 2\n");
        Assert.Equal([0, 1, 2], mesh.triIndices);

        Assert.Throws<FatalErrorException>(() => ReadPLYText("oob.ply", header + verts + "3 0 1 3\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("negative.ply", header + verts + "3 0 -1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("noz.ply", header.Replace("float z", "float w") + verts + "3 0 1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("noface.ply", header.Replace("face 1", "face 0") + verts));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("magic.ply", "plx" + header[3..] + verts + "3 0 1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("token.ply", header.Replace("end_header", "foo\nend_header") + verts + "3 0 1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("version.ply", header.Replace("1.0", "2.0") + verts + "3 0 1 2\n"));
        // Bad and out of range values
        Assert.Throws<FatalErrorException>(() => ReadPLYText("value.ply", header + "0 0 0x\n1 0 0\n0 1 0\n3 0 1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("range.ply", header + verts + "256 0 1 2\n"));
        Assert.Throws<FatalErrorException>(() => ReadPLYText("short.ply", header + verts + "3 0 1\n"));
        Assert.Throws<FatalErrorException>(() => TriQuadMesh.ReadPLY(InTestDir("missing.ply")));
    }

    [Fact]
    public void PlyASCIIWriting()
    {
        string fn = InTestDir("ascii-out.ply");
        PlyFile ply = PlyFile.Create(fn, PlyStorageMode.Ascii, null)!;
        ply.AddComment("written by a test");
        ply.AddElement("vertex", 2);
        ply.AddScalarProperty("x", PlyType.Float);
        ply.AddScalarProperty("y", PlyType.Double);
        ply.AddElement("empty", 0);
        ply.AddScalarProperty("e", PlyType.Int);
        ply.AddElement("face", 1);
        ply.AddListProperty("vertex_indices", PlyType.UChar, PlyType.UInt);
        Assert.True(ply.WriteHeader());
        foreach (double v in (double[])[0.1, 123456789, 1e-5, -0.000123456])
            Assert.True(ply.Write(v));
        foreach (double v in (double[])[3, 0, 1, 4000000000])
            Assert.True(ply.Write(v));
        // Out of range for the type
        Assert.False(ply.Write(1));
        Assert.True(ply.Close());

        // printf("%g") formatting, and RPly prints uint32 with %d.
        Assert.Equal("ply\nformat ascii 1.0\ncomment written by a test\nelement vertex 2\nproperty float x\n" +
                     "property double y\nelement empty 0\nproperty int e\nelement face 1\n" +
                     "property list uchar uint vertex_indices\nend_header\n" +
                     "0.1 1.23457e+08\n1e-05 -0.000123456\n3 0 1 -294967296\n", File.ReadAllText(fn));
        Assert.True(RemoveFile(fn));

        PlyFile out2 = PlyFile.Create(fn, PlyStorageMode.LittleEndian, null)!;
        out2.AddElement("v", 1);
        out2.AddScalarProperty("a", PlyType.UInt8);
        out2.WriteHeader();
        Assert.False(out2.Write(256));
        Assert.False(out2.Write(-1));
        Assert.True(out2.Write(255));
        out2.Close();
        Assert.True(RemoveFile(fn));
    }

    [Fact]
    public void ConvertToTrianglesAndNormals()
    {
        TriQuadMesh mesh = new()
        {
            p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            // (As read from a PLY file: 0, 1, 3, 2 in the file.)
            quadIndices = [0, 1, 2, 3]
        };
        mesh.ConvertToOnlyTriangles();
        Assert.Empty(mesh.quadIndices);
        Assert.Equal([0, 1, 3, 0, 3, 2], mesh.triIndices);
        mesh.ComputeNormals();
        Assert.Equal(4, mesh.n.Count);
        foreach (Normal3f n in mesh.n)
            Assert.Equal(new Normal3f(0, 0, 1), n);

        // Degenerate triangles don't contribute; unused vertices get zero.
        mesh.p.Add(new Point3f(5, 5, 5));
        mesh.triIndices.AddRange([0, 0, 1]);
        mesh.ComputeNormals();
        Assert.Equal(new Normal3f(0, 0, 1), mesh.n[0]);
        Assert.Equal(new Normal3f(0, 0, 0), mesh.n[4]);
        Assert.Contains("TriQuadMesh", mesh.ToString());
    }

    [Fact]
    public void Displace()
    {
        TriQuadMesh mesh = new()
        {
            p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            uv = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)],
            quadIndices = [0, 1, 2, 3]
        };
        Float maxDist = .3f;
        TriQuadMesh d = mesh.Displace((p0, p1) => Distance(p0, p1), maxDist, (p, n, uv, nVertices) =>
        {
            Assert.Equal(p.Count, nVertices);
            for (int i = 0; i < nVertices; ++i)
                p[i] += new Vector3f(.1f * uv[i].x * n[i]);
        });

        // The input mesh isn't changed.
        Assert.Equal(4, mesh.p.Count);
        Assert.Equal(4, mesh.quadIndices.Count);

        Assert.Empty(d.quadIndices);
        Assert.Equal(d.p.Count, d.uv.Count);
        Assert.Equal(d.p.Count, d.n.Count);
        Assert.True(d.triIndices.Count / 3 > 2);
        for (int i = 0; i < d.triIndices.Count; i += 3)
        {
            for (int j = 0; j < 3; ++j)
            {
                // Edges are short enough (before displacement, in uv).
                Point2f a = d.uv[d.triIndices[i + j]], b = d.uv[d.triIndices[i + (j + 1) % 3]];
                Assert.True(Distance(a, b) < maxDist);
            }
        }
        // Split edges are shared, so vertices aren't duplicated.
        HashSet<Point2f> uvs = [.. d.uv];
        Assert.Equal(d.uv.Count, uvs.Count);
        // Displaced along the normal by .1 * u
        for (int i = 0; i < d.p.Count; ++i)
            Assert.Equal(.1f * d.uv[i].x, d.p[i].z, 1e-6f);

        Assert.Throws<FatalErrorException>(() =>
            new TriQuadMesh { p = mesh.p, triIndices = [0, 1, 2] }.Displace((a, b) => 0, 1, (p, n, uv, nv) => { }));
    }

    [Fact]
    public void TriangleMeshBasics()
    {
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        Normal3f[] n = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)];
        Transform t = Translate(new Vector3f(1, 2, 3)) * Scale(-1, 1, 1);
        TriangleMesh mesh = new(t, true, (int[])[0, 1, 2], p, null, n, null, (int[])[4]);
        Assert.Equal(1, mesh.nTriangles);
        Assert.Equal(3, mesh.nVertices);
        Assert.Equal([new(1, 2, 3), new(0, 2, 3), new(1, 3, 3)], mesh.p);
        // Normals are transformed and flipped for reverseOrientation.
        Assert.Equal(new Normal3f(0, 0, -1), mesh.n![0]);
        Assert.True(mesh.reverseOrientation);
        Assert.True(mesh.transformSwapsHandedness);
        Assert.Null(mesh.s);
        Assert.Null(mesh.uv);
        Assert.Equal([4], mesh.faceIndices!);
        // The input isn't modified.
        Assert.Equal(new Point3f(1, 0, 0), p[1]);

        // Identical data is shared through the buffer caches.
        TriangleMesh mesh2 = new(t, true, (int[])[0, 1, 2], p, null, n, null, null);
        Assert.Same(mesh.vertexIndices, mesh2.vertexIndices);
        Assert.Same(mesh.p, mesh2.p);
        Assert.Same(mesh.n, mesh2.n);
        Assert.Null(mesh2.faceIndices);
        Assert.Contains("nTriangles: 1", mesh.ToString());

        string fn = InTestDir("trimesh.ply");
        Assert.True(mesh.WritePLY(fn));
        TriQuadMesh read = TriQuadMesh.ReadPLY(fn);
        Assert.Equal(mesh.p, read.p);
        Assert.Equal(mesh.n, read.n);
        Assert.Equal([4], read.faceIndices);
        Assert.True(RemoveFile(fn));

        Assert.Throws<CheckFailedException>(() => new TriangleMesh(t, false, (int[])[0, 1], p, null, null, null, null));
    }

    [Fact]
    public void BilinearPatchMeshBasics()
    {
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)];
        Point2f[] uv = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
        BilinearPatchMesh mesh = new(Scale(2, 2, 2), false, (int[])[0, 1, 2, 3], p, null, uv, null, null);
        Assert.Equal(1, mesh.nPatches);
        Assert.Equal(4, mesh.nVertices);
        Assert.Equal(new Point3f(2, 2, 0), mesh.p[3]);
        Assert.Equal(uv, mesh.uv!);
        Assert.Null(mesh.n);
        Assert.False(mesh.transformSwapsHandedness);
        Assert.Contains("nPatches: 1", mesh.ToString());
    }

    [Fact]
    public void LoopSubdivideTetrahedron()
    {
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        int[] idx = [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3];
        for (int level = 0; level < 4; ++level)
        {
            TriangleMesh mesh = LoopSubdivide(new Transform(), false, level, idx, p);
            int nFaces = 4 << (2 * level);
            Assert.Equal(nFaces, mesh.nTriangles);
            // V - E + F = 2 for a closed genus-0 mesh
            Assert.Equal(2 + nFaces * 3 / 2 - nFaces, mesh.nVertices);
            Assert.NotNull(mesh.n);

            // Each edge is shared by two faces with opposite orientations.
            HashSet<(int, int)> edges = [];
            for (int i = 0; i < 3 * nFaces; i += 3)
                for (int j = 0; j < 3; ++j)
                    Assert.True(edges.Add((mesh.vertexIndices[i + j], mesh.vertexIndices[i + (j + 1) % 3])));
            foreach ((int a, int b) in edges)
                Assert.Contains((b, a), edges);

            // The limit surface is inside the tetrahedron, and the normals
            // (from the tangents) point inward with this winding.
            Point3f c = new(.25f, .25f, .25f);
            for (int i = 0; i < mesh.nVertices; ++i)
            {
                Point3f q = mesh.p[i];
                Assert.True(q.x > 0 && q.y > 0 && q.z > 0 && q.x + q.y + q.z < 1, $"{q}");
                Assert.True(Dot(mesh.n![i], q - c) < 0, $"{i}: {q} {mesh.n[i]}");
            }
        }

        // Values from pbrt's LoopSubdivide().
        TriangleMesh m1 = LoopSubdivide(new Transform(), false, 1, idx, p);
        Assert.Equal([0, 4, 6, 4, 2, 5, 6, 5, 1, 4, 5, 6], m1.vertexIndices[..12]);
#if PBRT_FLOAT_AS_DOUBLE
        Assert.Equal(new Point3f(0.19999999999999998, 0.19999999999999998, 0.19999999999999998), m1.p[0]);
        Assert.Equal(new Point3f(0.39999999999999997, 0.19999999999999998, 0.19999999999999998), m1.p[1]);
        Assert.Equal(new Point3f(0.17708333333333334, 0.32291666666666669, 0.17708333333333334), m1.p[4]);
#else
        Assert.Equal(new Point3f(0.200000003f, 0.199999988f, 0.200000018f), m1.p[0]);
        Assert.Equal(new Point3f(0.399999976f, 0.200000018f, 0.199999988f), m1.p[1]);
        Assert.Equal(new Point3f(0.177083328f, 0.322916657f, 0.177083328f), m1.p[4]);
#endif
    }

    [Fact]
    public void LoopSubdividePlanar()
    {
        // A flat open mesh stays flat, with boundary vertices of valence 2,
        // 3 and 4 and interior ones of different valences.
        Point3f[] p = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(0, 1, 0), new(1, 1, 0), new(2, 1, 0),
                       new(0, 2, 0), new(1, 2, 0), new(2, 2, 0)];
        int[] idx = [0, 1, 4, 0, 4, 3, 1, 2, 4, 2, 5, 4, 3, 4, 6, 4, 7, 6, 4, 5, 8, 4, 8, 7];
        TriangleMesh mesh = LoopSubdivide(new Transform(), false, 3, idx, p);
        Assert.Equal(8 * 64, mesh.nTriangles);
        for (int i = 0; i < mesh.nVertices; ++i)
        {
            Assert.Equal(0, mesh.p[i].z);
            Assert.True(mesh.p[i].x >= 0 && mesh.p[i].x <= 2 && mesh.p[i].y >= 0 && mesh.p[i].y <= 2);
            Normal3f n = mesh.n![i];
            Assert.True(n.x == 0 && n.y == 0 && n.z != 0, $"{i}: {n}");
        }
        // Corners aren't kept: the boundary rule moves them toward their
        // neighbors along the boundary. (The first vertices are the
        // children of the original ones.)
        Assert.True(mesh.p[0].x > 0 && mesh.p[0].y > 0);
        Assert.Equal(new Point3f(1, 1, 0), mesh.p[4]);
    }
}

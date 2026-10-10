// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/mesh.h and mesh.cpp
//
// The meshes' vertex data are shared arrays from the buffer caches (pbrt's
// pointers); data a mesh doesn't have is null. PLY files are read and
// written with Ply.cs (pbrt uses RPly).

namespace Pbrt;

// TriangleMesh Definition
public sealed class TriangleMesh
{
    private static readonly StatRatio nTris = new("Geometry/Triangles per mesh");
    private static readonly StatMemoryCounter triangleBytes = new("Memory/Triangles");

    // TriangleMesh Public Members
    public readonly int nTriangles, nVertices;
    public readonly int[] vertexIndices;
    public readonly Point3f[] p;
    public readonly Normal3f[]? n;
    public readonly Vector3f[]? s;
    public readonly Point2f[]? uv;
    public readonly int[]? faceIndices;
    public readonly bool reverseOrientation, transformSwapsHandedness;

    // TriangleMesh Public Methods
    public TriangleMesh(Transform renderFromObject, bool reverseOrientation, IReadOnlyList<int> indices,
                        IReadOnlyList<Point3f> p, IReadOnlyList<Vector3f>? s, IReadOnlyList<Normal3f>? n,
                        IReadOnlyList<Point2f>? uv, IReadOnlyList<int>? faceIndices)
    {
        nTriangles = indices.Count / 3;
        nVertices = p.Count;
        CHECK_EQ(indices.Count % 3, 0);
        nTris.Denom();
        nTris.Num(nTriangles);
        // (sizeof(TriangleMesh) in pbrt)
        triangleBytes.Add(80);
        // Initialize mesh _vertexIndices_
        vertexIndices = BufferCaches.intBufferCache.LookupOrAdd(ToArray(indices));

        // Transform mesh vertices to rendering space and initialize mesh _p_
        Point3f[] pt = ToArray(p);
        for (int i = 0; i < pt.Length; ++i)
            pt[i] = renderFromObject.Apply(pt[i]);
        this.p = BufferCaches.point3BufferCache.LookupOrAdd(pt);

        // Remainder of _TriangleMesh_ constructor
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();

        if (uv != null && uv.Count > 0)
        {
            CHECK_EQ(nVertices, uv.Count);
            this.uv = BufferCaches.point2BufferCache.LookupOrAdd(ToArray(uv));
        }
        if (n != null && n.Count > 0)
        {
            CHECK_EQ(nVertices, n.Count);
            Normal3f[] nn = ToArray(n);
            for (int i = 0; i < nn.Length; ++i)
            {
                nn[i] = renderFromObject.Apply(nn[i]);
                if (reverseOrientation)
                    nn[i] = -nn[i];
            }
            this.n = BufferCaches.normal3BufferCache.LookupOrAdd(nn);
        }
        if (s != null && s.Count > 0)
        {
            CHECK_EQ(nVertices, s.Count);
            Vector3f[] ss = ToArray(s);
            for (int i = 0; i < ss.Length; ++i)
                ss[i] = renderFromObject.Apply(ss[i]);
            this.s = BufferCaches.vector3BufferCache.LookupOrAdd(ss);
        }

        if (faceIndices != null && faceIndices.Count > 0)
        {
            CHECK_EQ(nTriangles, faceIndices.Count);
            this.faceIndices = BufferCaches.intBufferCache.LookupOrAdd(ToArray(faceIndices));
        }
    }

    internal static T[] ToArray<T>(IReadOnlyList<T> list) where T : unmanaged
    {
        T[] a = new T[list.Count];
        for (int i = 0; i < a.Length; ++i)
            a[i] = list[i];
        return a;
    }

    internal static string Format<T>(T[]? a) => a == null ? "(nullptr)" : $"[ {string.Join(", ", a)} ]";

    public override string ToString() =>
        $"[ TriangleMesh reverseOrientation: {reverseOrientation} transformSwapsHandedness: {transformSwapsHandedness} " +
        $"nTriangles: {nTriangles} nVertices: {nVertices} vertexIndices: {Format(vertexIndices)} p: {Format(p)} " +
        $"n: {Format(n)} s: {Format(s)} uv: {Format(uv)} faceIndices: {Format(faceIndices)} ]";

    public bool WritePLY(string filename)
    {
        if (s != null)
            Warning($"{filename}: PLY mesh will be missing tangent vectors \"S\".");

        return Util.WritePLY(filename, vertexIndices, [], p, n ?? [], uv ?? [], faceIndices ?? []);
    }
}

// BilinearPatchMesh Definition
public sealed class BilinearPatchMesh
{
    private static readonly StatRatio nBlps = new("Geometry/Bilinear patches per mesh");
    private static readonly StatMemoryCounter blpBytes = new("Memory/Bilinear patches");

    // BilinearPatchMesh Public Members
    public readonly bool reverseOrientation, transformSwapsHandedness;
    public readonly int nPatches, nVertices;
    public readonly int[] vertexIndices;
    public readonly Point3f[] p;
    public readonly Normal3f[]? n;
    public readonly Point2f[]? uv;
    public readonly int[]? faceIndices;
    public readonly PiecewiseConstant2D? imageDistribution;

    // BilinearPatchMesh Public Methods
    public BilinearPatchMesh(Transform renderFromObject, bool reverseOrientation, IReadOnlyList<int> indices,
                             IReadOnlyList<Point3f> P, IReadOnlyList<Normal3f>? N, IReadOnlyList<Point2f>? UV,
                             IReadOnlyList<int>? fIndices, PiecewiseConstant2D? imageDist)
    {
        this.reverseOrientation = reverseOrientation;
        transformSwapsHandedness = renderFromObject.SwapsHandedness();
        nPatches = indices.Count / 4;
        nVertices = P.Count;
        imageDistribution = imageDist;
        CHECK_EQ(indices.Count % 4, 0);
        nBlps.Denom();
        nBlps.Num(nPatches);

        vertexIndices = BufferCaches.intBufferCache.LookupOrAdd(TriangleMesh.ToArray(indices));

        // (sizeof(BilinearPatchMesh) in pbrt)
        blpBytes.Add(64);

        // Transform mesh vertices to rendering space
        Point3f[] pt = TriangleMesh.ToArray(P);
        for (int i = 0; i < pt.Length; ++i)
            pt[i] = renderFromObject.Apply(pt[i]);
        p = BufferCaches.point3BufferCache.LookupOrAdd(pt);

        // Copy _UV_ and _N_ vertex data, if present
        if (UV != null && UV.Count > 0)
        {
            CHECK_EQ(nVertices, UV.Count);
            uv = BufferCaches.point2BufferCache.LookupOrAdd(TriangleMesh.ToArray(UV));
        }
        if (N != null && N.Count > 0)
        {
            CHECK_EQ(nVertices, N.Count);
            Normal3f[] nn = TriangleMesh.ToArray(N);
            for (int i = 0; i < nn.Length; ++i)
            {
                nn[i] = renderFromObject.Apply(nn[i]);
                if (reverseOrientation)
                    nn[i] = -nn[i];
            }
            n = BufferCaches.normal3BufferCache.LookupOrAdd(nn);
        }

        if (fIndices != null && fIndices.Count > 0)
        {
            CHECK_EQ(nPatches, fIndices.Count);
            faceIndices = BufferCaches.intBufferCache.LookupOrAdd(TriangleMesh.ToArray(fIndices));
        }
    }

    // (pbrt's string says "BilinearMatchMesh".)
    public override string ToString() =>
        $"[ BilinearMatchMesh reverseOrientation: {reverseOrientation} transformSwapsHandedness: {transformSwapsHandedness} " +
        $"nPatches: {nPatches} nVertices: {nVertices} vertexIndices: {TriangleMesh.Format(vertexIndices)} " +
        $"p: {TriangleMesh.Format(p)} n: {TriangleMesh.Format(n)} uv: {TriangleMesh.Format(uv)} " +
        $"faceIndices: {TriangleMesh.Format(faceIndices)} ]";
}

// HashIntPair Definition
public sealed class HashIntPair : IEqualityComparer<(int, int)>
{
    public static readonly HashIntPair Instance = new();

    public bool Equals((int, int) a, (int, int) b) => a == b;
    public int GetHashCode((int, int) p) => (int)MixBits((ulong)p.Item1 << 32 | (uint)p.Item2);
}

public sealed class TriQuadMesh
{
    public List<Point3f> p = [];
    public List<Normal3f> n = [];
    public List<Point2f> uv = [];
    public List<int> faceIndices = [];
    public List<int> triIndices = [], quadIndices = [];

    // TriQuadMesh Public Methods
    public TriQuadMesh Clone() => new()
    {
        p = [.. p], n = [.. n], uv = [.. uv], faceIndices = [.. faceIndices],
        triIndices = [.. triIndices], quadIndices = [.. quadIndices]
    };

    private struct FaceCallbackContext
    {
        public Array4<int> face;
        public List<int> triIndices, quadIndices;
    }

    public static TriQuadMesh ReadPLY(string filename)
    {
        TriQuadMesh mesh = new();

        PlyFile? ply = PlyFile.Open(filename, message => Warning($"rply: {message}"));
        if (ply == null)
            ErrorExit($"Couldn't open PLY file \"{filename}\"");

        if (!ply!.ReadHeader())
            ErrorExit($"Unable to read the header of PLY file \"{filename}\"");

        long vertexCount = 0, faceCount = 0;

        // Inspect the structure of the PLY file
        foreach (PlyElement element in ply.Elements)
        {
            if (element.name == "vertex")
                vertexCount = element.nInstances;
            else if (element.name == "face")
                faceCount = element.nInstances;
        }

        if (vertexCount == 0 || faceCount == 0)
            ErrorExit($"{filename}: PLY file is invalid! No face/vertex elements found!");

        // Vertex data are read into Float buffers, as pbrt's
        // rply_vertex_callback() does with its stride and offset flags.
        Func<PlyArgument, bool> VertexCallback(Float[] buffer, int stride, int offset) => argument =>
        {
            buffer[argument.instanceIndex * stride + offset] = (float)argument.value;
            return true;
        };

        Float[] pBuf = new Float[3 * vertexCount];
        if (ply.SetReadCb("vertex", "x", VertexCallback(pBuf, 3, 0)) == 0 ||
            ply.SetReadCb("vertex", "y", VertexCallback(pBuf, 3, 1)) == 0 ||
            ply.SetReadCb("vertex", "z", VertexCallback(pBuf, 3, 2)) == 0)
            ErrorExit($"{filename}: Vertex coordinate property not found!");

        Float[]? nBuf = new Float[3 * vertexCount];
        if (ply.SetReadCb("vertex", "nx", VertexCallback(nBuf, 3, 0)) == 0 ||
            ply.SetReadCb("vertex", "ny", VertexCallback(nBuf, 3, 1)) == 0 ||
            ply.SetReadCb("vertex", "nz", VertexCallback(nBuf, 3, 2)) == 0)
            nBuf = null;

        // There seem to be lots of different conventions regarding UV
        // coordinate names
        Float[]? uvBuf = new Float[2 * vertexCount];
        if ((ply.SetReadCb("vertex", "u", VertexCallback(uvBuf, 2, 0)) != 0 &&
             ply.SetReadCb("vertex", "v", VertexCallback(uvBuf, 2, 1)) != 0) ||
            (ply.SetReadCb("vertex", "s", VertexCallback(uvBuf, 2, 0)) != 0 &&
             ply.SetReadCb("vertex", "t", VertexCallback(uvBuf, 2, 1)) != 0) ||
            (ply.SetReadCb("vertex", "texture_u", VertexCallback(uvBuf, 2, 0)) != 0 &&
             ply.SetReadCb("vertex", "texture_v", VertexCallback(uvBuf, 2, 1)) != 0) ||
            (ply.SetReadCb("vertex", "texture_s", VertexCallback(uvBuf, 2, 0)) != 0 &&
             ply.SetReadCb("vertex", "texture_t", VertexCallback(uvBuf, 2, 1)) != 0))
        {
        }
        else
            uvBuf = null;

        FaceCallbackContext context = new()
        {
            triIndices = new List<int>((int)Math.Min(faceCount * 3, int.MaxValue)),
            quadIndices = []
        };
        if (ply.SetReadCb("face", "vertex_indices", argument =>
            {
                long length = argument.length, valueIndex = argument.valueIndex;

                if (length != 3 && length != 4)
                {
                    Warning($"plymesh: Ignoring face with {length} vertices (only triangles and quads " +
                            "are supported!)");
                    return true;
                }
                else if (valueIndex < 0)
                    return true;

                context.face[(int)valueIndex] = (int)argument.value;

                if (valueIndex == length - 1)
                {
                    if (length == 3)
                        for (int i = 0; i < 3; ++i)
                            context.triIndices.Add(context.face[i]);
                    else
                    {
                        CHECK_EQ(length, 4);

                        // Note: modify order since we're specifying it as a blp...
                        context.quadIndices.Add(context.face[0]);
                        context.quadIndices.Add(context.face[1]);
                        context.quadIndices.Add(context.face[3]);
                        context.quadIndices.Add(context.face[2]);
                    }
                }

                return true;
            }) == 0)
            ErrorExit($"{filename}: vertex indices not found in PLY file");

        if (ply.SetReadCb("face", "face_indices", argument =>
            {
                mesh.faceIndices.Add((int)argument.value);
                return true;
            }) != 0)
            mesh.faceIndices.Capacity = (int)faceCount;

        if (!ply.Read())
            ErrorExit($"{filename}: unable to read the contents of PLY file");

        mesh.triIndices = context.triIndices;
        mesh.quadIndices = context.quadIndices;

        ply.Close();

        mesh.p = new List<Point3f>((int)vertexCount);
        for (int i = 0; i < vertexCount; ++i)
            mesh.p.Add(new Point3f(pBuf[3 * i], pBuf[3 * i + 1], pBuf[3 * i + 2]));
        if (nBuf != null)
        {
            mesh.n = new List<Normal3f>((int)vertexCount);
            for (int i = 0; i < vertexCount; ++i)
                mesh.n.Add(new Normal3f(nBuf[3 * i], nBuf[3 * i + 1], nBuf[3 * i + 2]));
        }
        if (uvBuf != null)
        {
            mesh.uv = new List<Point2f>((int)vertexCount);
            for (int i = 0; i < vertexCount; ++i)
                mesh.uv.Add(new Point2f(uvBuf[2 * i], uvBuf[2 * i + 1]));
        }

        foreach (int idx in mesh.triIndices)
            if (idx < 0 || idx >= mesh.p.Count)
                ErrorExit($"plymesh: Vertex index {idx} is out of bounds! " +
                          $"Valid range is [0..{mesh.p.Count})");
        foreach (int idx in mesh.quadIndices)
            if (idx < 0 || idx >= mesh.p.Count)
                ErrorExit($"plymesh: Vertex index {idx} is out of bounds! " +
                          $"Valid range is [0..{mesh.p.Count})");

        return mesh;
    }

    public void ConvertToOnlyTriangles()
    {
        if (quadIndices.Count == 0)
            return;

        triIndices.Capacity = triIndices.Count + 3 * quadIndices.Count / 2;

        for (int i = 0; i < quadIndices.Count; i += 4)
        {
            triIndices.Add(quadIndices[i]); // 0, 1, 2 of original
            triIndices.Add(quadIndices[i + 1]);
            triIndices.Add(quadIndices[i + 3]);

            triIndices.Add(quadIndices[i]); // 0, 2, 3 of original
            triIndices.Add(quadIndices[i + 3]);
            triIndices.Add(quadIndices[i + 2]);
        }

        quadIndices.Clear();
    }

    public void ComputeNormals()
    {
        n = new List<Normal3f>(p.Count);
        for (int i = 0; i < p.Count; ++i)
            n.Add(new Normal3f(0, 0, 0));

        for (int i = 0; i < triIndices.Count; i += 3)
        {
            int v0 = triIndices[i], v1 = triIndices[i + 1], v2 = triIndices[i + 2];
            Vector3f v10 = p[v1] - p[v0];
            Vector3f v21 = p[v2] - p[v1];

            Normal3f vn = new(Cross(v10, v21));
            if (LengthSquared(vn) > 0)
            {
                vn = Normalize(vn);
                n[v0] += vn;
                n[v1] += vn;
                n[v2] += vn;
            }
        }
        CHECK_EQ(0, quadIndices.Count); // TODO: handle this...

        for (int i = 0; i < n.Count; ++i)
            if (LengthSquared(n[i]) > 0)
                n[i] = Normalize(n[i]);
    }

    public override string ToString() =>
        $"[ TriQuadMesh p: [ {string.Join(", ", p)} ] n: [ {string.Join(", ", n)} ] uv: [ {string.Join(", ", uv)} ] " +
        $"faceIndices: [ {string.Join(", ", faceIndices)} ] triIndices: [ {string.Join(", ", triIndices)} ] " +
        $"quadIndices: [ {string.Join(", ", quadIndices)} ] ]";

    // _displace_ is given the output mesh's positions, normals and uvs and
    // the number of vertices; it updates the positions.
    public TriQuadMesh Displace(Func<Point3f, Point3f, Float> dist, Float maxDist,
                                Action<List<Point3f>, List<Normal3f>, List<Point2f>, int> displace,
                                FileLoc? loc = null)
    {
        if (uv.Count == 0)
            ErrorExit(loc, "Vertex uvs are currently required by Displace(). Sorry.\n");

        // Prepare the output mesh
        TriQuadMesh outputMesh = Clone();
        outputMesh.ConvertToOnlyTriangles();
        if (outputMesh.n.Count == 0)
            outputMesh.ComputeNormals();
        List<int> oldTriIndices = new(outputMesh.triIndices);
        outputMesh.triIndices.Clear();

        // Refine
        Dictionary<(int, int), int> edgeSplit = new(HashIntPair.Instance);
        for (int i = 0; i < oldTriIndices.Count / 3; ++i)
            outputMesh.Refine(dist, maxDist, oldTriIndices[3 * i], oldTriIndices[3 * i + 1],
                              oldTriIndices[3 * i + 2], edgeSplit);

        // Displace
        displace(outputMesh.p, outputMesh.n, outputMesh.uv, outputMesh.p.Count);

        outputMesh.ComputeNormals();

        return outputMesh;
    }

    // TriQuadMesh Private Methods
    private void Refine(Func<Point3f, Point3f, Float> distance, Float maxDist, int v0, int v1, int v2,
                        Dictionary<(int, int), int> edgeSplit)
    {
        Point3f p0 = p[v0], p1 = p[v1], p2 = p[v2];
        Float d01 = distance(p0, p1), d12 = distance(p1, p2), d20 = distance(p2, p0);

        if (d01 < maxDist && d12 < maxDist && d20 < maxDist)
        {
            triIndices.Add(v0);
            triIndices.Add(v1);
            triIndices.Add(v2);
            return;
        }

        // order so that the first two vertices have the longest edge
        (int, int, int) v;
        if (d01 > d12)
        {
            if (d01 > d20)
                v = (v0, v1, v2);
            else
                v = (v2, v0, v1);
        }
        else
        {
            if (d12 > d20)
                v = (v1, v2, v0);
            else
                v = (v2, v0, v1);
        }

        // has the edge been spilt before?
        (int, int) edge = (v.Item1, v.Item2);
        if (v.Item1 > v.Item2)
            edge = (edge.Item2, edge.Item1);

        int vmid;
        if (edgeSplit.TryGetValue(edge, out int split))
            vmid = split;
        else
        {
            vmid = p.Count;
            edgeSplit[edge] = vmid;
            p.Add((p[v.Item1] + p[v.Item2]) / 2);
            if (n.Count > 0)
            {
                Normal3f nn = n[v.Item1] + n[v.Item2];
                if (LengthSquared(nn) > 0)
                    nn = Normalize(nn);
                n.Add(nn);
            }
            if (uv.Count > 0)
                uv.Add((uv[v.Item1] + uv[v.Item2]) / 2);
        }

        Refine(distance, maxDist, v.Item1, vmid, v.Item3, edgeSplit);
        Refine(distance, maxDist, vmid, v.Item2, v.Item3, edgeSplit);
    }
}

public static partial class Util
{
    public static bool WritePLY(string filename, ReadOnlySpan<int> triIndices, ReadOnlySpan<int> quadIndices,
                                ReadOnlySpan<Point3f> p, ReadOnlySpan<Normal3f> n, ReadOnlySpan<Point2f> uv,
                                ReadOnlySpan<int> faceIndices)
    {
        PlyFile? plyFile = PlyFile.Create(filename, PlyStorageMode.Default,
                                          message => Error($"PLY writing error: {message}"));
        if (plyFile == null)
            return false;

        int nVertices = p.Length;
        plyFile.AddElement("vertex", nVertices);
        plyFile.AddScalarProperty("x", PlyType.Float);
        plyFile.AddScalarProperty("y", PlyType.Float);
        plyFile.AddScalarProperty("z", PlyType.Float);
        if (!n.IsEmpty)
        {
            plyFile.AddScalarProperty("nx", PlyType.Float);
            plyFile.AddScalarProperty("ny", PlyType.Float);
            plyFile.AddScalarProperty("nz", PlyType.Float);
        }
        if (!uv.IsEmpty)
        {
            plyFile.AddScalarProperty("u", PlyType.Float);
            plyFile.AddScalarProperty("v", PlyType.Float);
        }

        int nTriangles = triIndices.Length / 3, nQuads = quadIndices.Length / 4;
        plyFile.AddElement("face", nTriangles + nQuads);
        plyFile.AddListProperty("vertex_indices", PlyType.UInt8, PlyType.Int);
        if (!faceIndices.IsEmpty)
            plyFile.AddScalarProperty("face_indices", PlyType.Int);

        plyFile.WriteHeader();

        for (int i = 0; i < nVertices; ++i)
        {
            plyFile.Write(p[i].x);
            plyFile.Write(p[i].y);
            plyFile.Write(p[i].z);
            if (!n.IsEmpty)
            {
                plyFile.Write(n[i].x);
                plyFile.Write(n[i].y);
                plyFile.Write(n[i].z);
            }
            if (!uv.IsEmpty)
            {
                plyFile.Write(uv[i].x);
                plyFile.Write(uv[i].y);
            }
        }

        for (int i = 0; i < nTriangles; ++i)
        {
            plyFile.Write(3);
            plyFile.Write(triIndices[3 * i]);
            plyFile.Write(triIndices[3 * i + 1]);
            plyFile.Write(triIndices[3 * i + 2]);
            if (!faceIndices.IsEmpty)
                plyFile.Write(faceIndices[i]);
        }
        for (int i = 0; i < nQuads; ++i)
        {
            plyFile.Write(4);
            plyFile.Write(quadIndices[4 * i]);
            plyFile.Write(quadIndices[4 * i + 1]);
            plyFile.Write(quadIndices[4 * i + 2]);
            plyFile.Write(quadIndices[4 * i + 3]);
            // (pbrt writes faceIndices[i] here, not faceIndices[nTriangles + i].)
            if (!faceIndices.IsEmpty)
                plyFile.Write(faceIndices[i]);
        }

        plyFile.Close();
        return true;
    }
}

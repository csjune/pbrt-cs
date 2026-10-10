// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/loopsubdiv.h and loopsubdiv.cpp
//
// pbrt orders an edge's vertices by their addresses, which determines the
// order of the floating-point sums for new edge vertices. Vertices here have
// ids in allocation order instead, which matches the addresses when pbrt's
// allocations are contiguous (the input vertices are one array).

namespace Pbrt;

public static partial class Util
{
    // LoopSubdiv Local Structures
    private sealed class SDVertex(Point3f p, int id)
    {
        public Point3f p = p;
        public readonly int id = id;
        public SDFace? startFace;
        public SDVertex? child;
        public bool regular = false, boundary = false;

        // SDVertex Methods
        public int Valence()
        {
            SDFace? f = startFace;
            if (!boundary)
            {
                // Compute valence of interior vertex
                int nf = 1;
                while ((f = f!.NextFace(this)) != startFace)
                    ++nf;
                return nf;
            }
            else
            {
                // Compute valence of boundary vertex
                int nf = 1;
                while ((f = f!.NextFace(this)) != null)
                    ++nf;
                f = startFace;
                while ((f = f!.PrevFace(this)) != null)
                    ++nf;
                return nf + 1;
            }
        }

        public void OneRing(Span<Point3f> p)
        {
            int i = 0;
            if (!boundary)
            {
                // Get one-ring vertices for interior vertex
                SDFace face = startFace!;
                do
                {
                    p[i++] = face.NextVert(this).p;
                    face = face.NextFace(this)!;
                } while (face != startFace);
            }
            else
            {
                // Get one-ring vertices for boundary vertex
                SDFace face = startFace!;
                SDFace? f2;
                while ((f2 = face.NextFace(this)) != null)
                    face = f2;
                p[i++] = face.NextVert(this).p;
                SDFace? fc = face;
                do
                {
                    p[i++] = fc.PrevVert(this).p;
                    fc = fc.PrevFace(this);
                } while (fc != null);
            }
        }
    }

    // LoopSubdiv Macros
    private static int NEXT(int i) => (i + 1) % 3;
    private static int PREV(int i) => (i + 2) % 3;

    private sealed class SDFace
    {
        public readonly SDVertex?[] v = new SDVertex?[3];
        public readonly SDFace?[] f = new SDFace?[3];
        public readonly SDFace?[] children = new SDFace?[4];

        // SDFace Methods
        public int Vnum(SDVertex vert)
        {
            for (int i = 0; i < 3; ++i)
                if (v[i] == vert)
                    return i;
            LOG_FATAL("Basic logic error in SDFace::vnum()");
            return -1;
        }

        public SDFace? NextFace(SDVertex vert) => f[Vnum(vert)];
        public SDFace? PrevFace(SDVertex vert) => f[PREV(Vnum(vert))];
        public SDVertex NextVert(SDVertex vert) => v[NEXT(Vnum(vert))]!;
        public SDVertex PrevVert(SDVertex vert) => v[PREV(Vnum(vert))]!;

        public SDVertex OtherVert(SDVertex v0, SDVertex v1)
        {
            for (int i = 0; i < 3; ++i)
                if (v[i] != v0 && v[i] != v1)
                    return v[i]!;
            LOG_FATAL("Basic logic error in SDFace::otherVert()");
            return null!;
        }
    }

    private readonly struct SDEdge : IEquatable<SDEdge>
    {
        public readonly SDVertex v0, v1;

        // SDEdge Constructor
        public SDEdge(SDVertex a, SDVertex b)
        {
            (v0, v1) = a.id < b.id ? (a, b) : (b, a);
        }

        public bool Equals(SDEdge e) => v0 == e.v0 && v1 == e.v1;
        public override bool Equals(object? obj) => obj is SDEdge e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(v0.id, v1.id);
    }

    private static Float beta(int valence)
    {
        if (valence == 3)
            return 3f / 16f;
        else
            return 3f / (8f * valence);
    }

    private static Float loopGamma(int valence) => 1f / (valence + 3f / (8f * beta(valence)));

    // LoopSubdiv Function Definitions
    public static TriangleMesh LoopSubdivide(Transform renderFromObject, bool reverseOrientation, int nLevels,
                                             ReadOnlySpan<int> vertexIndices, ReadOnlySpan<Point3f> p)
    {
        int nextId = 0;
        List<SDVertex> vertices = new(p.Length);
        List<SDFace> faces = [];
        // Allocate _LoopSubdiv_ vertices and faces
        for (int i = 0; i < p.Length; ++i)
            vertices.Add(new SDVertex(p[i], nextId++));
        int nFaces = vertexIndices.Length / 3;
        for (int i = 0; i < nFaces; ++i)
            faces.Add(new SDFace());

        // Set face to vertex pointers
        for (int i = 0; i < nFaces; ++i)
        {
            SDFace f = faces[i];
            for (int j = 0; j < 3; ++j)
            {
                SDVertex v = vertices[vertexIndices[3 * i + j]];
                f.v[j] = v;
                v.startFace = f;
            }
        }

        // Set neighbor pointers in _faces_
        Dictionary<SDEdge, (SDFace f0, int f0edgeNum)> edges = [];
        for (int i = 0; i < nFaces; ++i)
        {
            SDFace f = faces[i];
            for (int edgeNum = 0; edgeNum < 3; ++edgeNum)
            {
                // Update neighbor pointer for _edgeNum_
                int v0 = edgeNum, v1 = NEXT(edgeNum);
                SDEdge e = new(f.v[v0]!, f.v[v1]!);
                if (!edges.TryGetValue(e, out (SDFace f0, int f0edgeNum) found))
                    // Handle new edge
                    edges.Add(e, (f, edgeNum));
                else
                {
                    // Handle previously seen edge
                    found.f0.f[found.f0edgeNum] = f;
                    f.f[edgeNum] = found.f0;
                    edges.Remove(e);
                }
            }
        }

        // Finish vertex initialization
        for (int i = 0; i < p.Length; ++i)
        {
            SDVertex v = vertices[i];
            SDFace? f = v.startFace;
            do
            {
                f = f!.NextFace(v);
            } while (f != null && f != v.startFace);
            v.boundary = f == null;
            if (!v.boundary && v.Valence() == 6)
                v.regular = true;
            else if (v.boundary && v.Valence() == 4)
                v.regular = true;
            else
                v.regular = false;
        }

        // Refine _LoopSubdiv_ into triangles
        List<SDFace> fs = faces;
        List<SDVertex> vs = vertices;

        for (int i = 0; i < nLevels; ++i)
        {
            // Update _f_ and _v_ for next level of subdivision
            List<SDFace> newFaces = new(4 * fs.Count);
            List<SDVertex> newVertices = [];

            // Allocate next level of children in mesh tree
            foreach (SDVertex vertex in vs)
            {
                vertex.child = new SDVertex(new Point3f(0, 0, 0), nextId++)
                {
                    regular = vertex.regular,
                    boundary = vertex.boundary
                };
                newVertices.Add(vertex.child);
            }
            foreach (SDFace face in fs)
            {
                for (int k = 0; k < 4; ++k)
                {
                    face.children[k] = new SDFace();
                    newFaces.Add(face.children[k]!);
                }
            }

            // Update vertex positions and create new edge vertices

            // Update vertex positions for even vertices
            foreach (SDVertex vertex in vs)
            {
                if (!vertex.boundary)
                {
                    // Apply one-ring rule for even vertex
                    if (vertex.regular)
                        vertex.child!.p = weightOneRing(vertex, 1f / 16f);
                    else
                        vertex.child!.p = weightOneRing(vertex, beta(vertex.Valence()));
                }
                else
                {
                    // Apply boundary rule for even vertex
                    vertex.child!.p = weightBoundary(vertex, 1f / 8f);
                }
            }

            // Compute new odd edge vertices
            Dictionary<SDEdge, SDVertex> edgeVerts = [];
            foreach (SDFace face in fs)
            {
                for (int k = 0; k < 3; ++k)
                {
                    // Compute odd vertex on _k_th edge
                    SDEdge edge = new(face.v[k]!, face.v[NEXT(k)]!);
                    if (!edgeVerts.ContainsKey(edge))
                    {
                        // Create and initialize new odd vertex
                        SDVertex vert = new(new Point3f(0, 0, 0), nextId++);
                        newVertices.Add(vert);
                        vert.regular = true;
                        vert.boundary = face.f[k] == null;
                        vert.startFace = face.children[3];

                        // Apply edge rules to compute new vertex position
                        if (vert.boundary)
                        {
                            vert.p = 0.5f * edge.v0.p;
                            vert.p += (0.5f * edge.v1.p);
                        }
                        else
                        {
                            vert.p = 3f / 8f * edge.v0.p;
                            vert.p += (3f / 8f * edge.v1.p);
                            vert.p += (1f / 8f * face.OtherVert(edge.v0, edge.v1).p);
                            vert.p += (1f / 8f * face.f[k]!.OtherVert(edge.v0, edge.v1).p);
                        }
                        edgeVerts[edge] = vert;
                    }
                }
            }

            // Update new mesh topology

            // Update even vertex face pointers
            foreach (SDVertex vertex in vs)
            {
                int vertNum = vertex.startFace!.Vnum(vertex);
                vertex.child!.startFace = vertex.startFace.children[vertNum];
            }

            // Update face neighbor pointers
            foreach (SDFace face in fs)
            {
                for (int j = 0; j < 3; ++j)
                {
                    // Update children _f_ pointers for siblings
                    face.children[3]!.f[j] = face.children[NEXT(j)];
                    face.children[j]!.f[NEXT(j)] = face.children[3];

                    // Update children _f_ pointers for neighbor children
                    SDFace? f2 = face.f[j];
                    face.children[j]!.f[j] = f2 != null ? f2.children[f2.Vnum(face.v[j]!)] : null;
                    f2 = face.f[PREV(j)];
                    face.children[j]!.f[PREV(j)] = f2 != null ? f2.children[f2.Vnum(face.v[j]!)] : null;
                }
            }

            // Update face vertex pointers
            foreach (SDFace face in fs)
            {
                for (int j = 0; j < 3; ++j)
                {
                    // Update child vertex pointer to new even vertex
                    face.children[j]!.v[j] = face.v[j]!.child;

                    // Update child vertex pointer to new odd vertex
                    SDVertex vert = edgeVerts[new SDEdge(face.v[j]!, face.v[NEXT(j)]!)];
                    face.children[j]!.v[NEXT(j)] = vert;
                    face.children[NEXT(j)]!.v[j] = vert;
                    face.children[3]!.v[j] = vert;
                }
            }

            // Prepare for next level of subdivision
            fs = newFaces;
            vs = newVertices;
        }

        // Push vertices to limit surface
        Point3f[] pLimit = new Point3f[vs.Count];
        for (int i = 0; i < vs.Count; ++i)
        {
            if (vs[i].boundary)
                pLimit[i] = weightBoundary(vs[i], 1f / 5f);
            else
                pLimit[i] = weightOneRing(vs[i], loopGamma(vs[i].Valence()));
        }
        for (int i = 0; i < vs.Count; ++i)
            vs[i].p = pLimit[i];

        // Compute vertex tangents on limit surface
        List<Normal3f> Ns = new(vs.Count);
        Point3f[] pRing = new Point3f[16];
        foreach (SDVertex vertex in vs)
        {
            Vector3f S = new(0, 0, 0), T = new(0, 0, 0);
            int valence = vertex.Valence();
            if (valence > pRing.Length)
                Array.Resize(ref pRing, valence);
            vertex.OneRing(pRing);
            if (!vertex.boundary)
            {
                // Compute tangents of interior face
                for (int j = 0; j < valence; ++j)
                {
                    S += FMath.Cos(2 * Pi * j / valence) * new Vector3f(pRing[j]);
                    T += FMath.Sin(2 * Pi * j / valence) * new Vector3f(pRing[j]);
                }
            }
            else
            {
                // Compute tangents of boundary face
                S = pRing[valence - 1] - pRing[0];
                if (valence == 2)
                    T = new Vector3f(pRing[0] + pRing[1] - (2 * vertex.p));
                else if (valence == 3)
                    T = pRing[1] - vertex.p;
                else if (valence == 4) // regular
                    T = new Vector3f(-1 * pRing[0] + (2 * pRing[1]) + (2 * pRing[2]) +
                                     (-1 * pRing[3]) + (-2 * vertex.p));
                else
                {
                    Float theta = Pi / (float)(valence - 1);
                    T = new Vector3f(FMath.Sin(theta) * (pRing[0] + pRing[valence - 1]));
                    for (int k = 1; k < valence - 1; ++k)
                    {
                        Float wt = (2 * FMath.Cos(theta) - 2) * FMath.Sin(k * theta);
                        T += new Vector3f(wt * pRing[k]);
                    }
                    T = -T;
                }
            }
            Ns.Add(new Normal3f(Cross(S, T)));
        }

        // Create triangle mesh from subdivision mesh
        {
            int ntris = fs.Count;
            int[] verts = new int[3 * ntris];
            Dictionary<SDVertex, int> usedVerts = new(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < vs.Count; ++i)
                usedVerts[vs[i]] = i;
            for (int i = 0; i < ntris; ++i)
                for (int j = 0; j < 3; ++j)
                    verts[3 * i + j] = usedVerts[fs[i].v[j]!];
            return new TriangleMesh(renderFromObject, reverseOrientation, verts, pLimit, null, Ns, null, null);
        }
    }

    private static Point3f weightOneRing(SDVertex vert, Float beta)
    {
        // Put _vert_ one-ring in _pRing_
        int valence = vert.Valence();
        Span<Point3f> pRing = valence <= 16 ? stackalloc Point3f[valence] : new Point3f[valence];

        vert.OneRing(pRing);
        Point3f p = (1 - valence * beta) * vert.p;
        for (int i = 0; i < valence; ++i)
            p += (beta * pRing[i]);
        return p;
    }

    private static Point3f weightBoundary(SDVertex vert, Float beta)
    {
        // Put _vert_ one-ring in _pRing_
        int valence = vert.Valence();
        Span<Point3f> pRing = valence <= 16 ? stackalloc Point3f[valence] : new Point3f[valence];

        vert.OneRing(pRing);
        Point3f p = (1 - 2 * beta) * vert.p;
        p += (beta * pRing[0]);
        p += (beta * pRing[valence - 1]);
        return p;
    }
}

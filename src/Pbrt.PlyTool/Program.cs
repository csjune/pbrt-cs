// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cmd/plytool.cpp

namespace Pbrt.PlyTool;

public static class Program
{
    private static void help()
    {
        Console.Write("""
            plytool provides a number of operations on PLY meshes.

            usage: plytool <command> [options]

            where <command> is:

            cat: Print a text representation of the mesh.

            displace: Apply displacement mapping to a mesh.

            info: Print general information about the mesh.

            split: Split the mesh into multiple PLY files.

            "plytool help <command>" provides detailed information about <command>.

            """);
    }

    private static void usage(string? msg = null)
    {
        if (msg != null)
            Console.Error.Write($"plytool: {msg}\n\n");

        help();

        Environment.Exit(1);
    }

    private static int help(List<string> args)
    {
        if (args.Count == 0)
        {
            help();
            return 0;
        }
        foreach (string cmd in args)
        {
            if (cmd == "cat")
                Console.Write("usage: plytool cat <filename>\n");
            else if (cmd == "info")
                Console.Write("usage: plytool info <filename...>\n");
            else if (cmd == "displace")
                Console.Write("""
                    usage: plytool displace [options] <filename>

                    options:
                      --scale <s>       Scale to apply to displacement value in image.
                                        (Default: 1)
                      --uvscale <s>     Scale to apply to uv texture coordinates in image.
                                        (Default: 1)
                      --edge-length <s> Maximum length of an edge in the undisplaced mesh.
                                        (Default: 1)
                      --image <name>    Filename for image used to define displacements.
                      --outfile <name>  Filename name for emitted PLY file.

                    """);
            else if (cmd == "split")
                Console.Write("""
                    usage: plytool split [options] <filename>

                    options:
                      --maxfaces <n>    Maximum number of faces in an output PLY file.
                                        (Default: 1000000)
                      --outbase <name>  Base name for emitted PLY files.  Consecutive numbers
                                        and a ".ply" suffix will be appended to <name>.
                                        (Default: based on <source.ply>.)

                    """);
            else
            {
                usage($"{cmd}: command unknown");
                return 1;
            }
        }

        return 0;
    }

    private static int cat(List<string> args)
    {
        string filename = "";
        foreach (string arg in args)
        {
            if (filename.Length == 0)
                filename = arg;
            else
                usage($"{arg}: unexpected argument");
        }

        if (filename.Length == 0)
            usage("must specify PLY filename.");

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);

        for (int i = 0; i < mesh.triIndices.Count; i += 3)
            Console.Write($"Triangle: {mesh.triIndices[i]} {mesh.triIndices[i + 1]} {mesh.triIndices[i + 2]}\n");
        for (int i = 0; i < mesh.quadIndices.Count; i += 4)
            Console.Write($"Quad: {mesh.quadIndices[i]} {mesh.quadIndices[i + 1]} {mesh.quadIndices[i + 2]} " +
                          $"{mesh.quadIndices[i + 3]}\n");
        for (int i = 0; i < mesh.p.Count; ++i)
            Console.Write($"Vertex position {i}: {mesh.p[i]}\n");
        for (int i = 0; i < mesh.n.Count; ++i)
            Console.Write($"Vertex normal {i}: {mesh.n[i]}\n");
        for (int i = 0; i < mesh.uv.Count; ++i)
            Console.Write($"Vertex uv {i}: {mesh.uv[i]}\n");

        return 0;
    }

    private static int info(List<string> args)
    {
        foreach (string filename in args)
        {
            TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);

            Console.Write($"{filename}:\n");
            Console.Write($"\tTriangles: {mesh.triIndices.Count / 3}\n");
            Console.Write($"\tQuads: {mesh.quadIndices.Count / 4}\n");
            Console.Write($"\tVertex positions: {mesh.p.Count}\n");
            Console.Write($"\tVertex normals: {mesh.n.Count}\n");
            Console.Write($"\tVertex uvs: {mesh.uv.Count}\n");
            Console.Write($"\tFace indices: {mesh.faceIndices.Count}\n");

            bool[] vertexUsed = new bool[mesh.p.Count];
            foreach (int v in mesh.triIndices)
            {
                CHECK(v >= 0 && v < mesh.p.Count);
                vertexUsed[v] = true;
            }
            foreach (int v in mesh.quadIndices)
            {
                CHECK(v >= 0 && v < mesh.p.Count);
                vertexUsed[v] = true;
            }
            for (int i = 0; i < vertexUsed.Length; ++i)
                if (!vertexUsed[i])
                    Console.Write($"Notice: vertex {i} is not used.\n");

            Bounds3f bounds = new();
            foreach (Point3f p in mesh.p)
                bounds = Union(bounds, p);
            Console.Write($"\tBounding box: {bounds}\n");
        }

        return 0;
    }

    private static int displace(List<string> args)
    {
        Float scale = 1, uvScale = 1, edgeLength = 1;
        string filename = "", imageFilename = "", outFilename = "";
        static void onError(string err)
        {
            usage(err);
            Environment.Exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, "uvscale", ref uvScale, onError) ||
                ParseArg(args, ref iter, "image", ref imageFilename, onError) ||
                ParseArg(args, ref iter, "outfile", ref outFilename, onError) ||
                ParseArg(args, ref iter, "edge-length", ref edgeLength, onError))
                { } // yaay
            else if (filename.Length == 0)
                filename = args[iter];
            else
                usage($"unexpected argument \"{args[iter]}\"");
        }

        if (filename.Length == 0)
            usage("must specify source PLY filename.");
        if (outFilename.Length == 0)
            usage("must specify output PLY filename.");
        if (imageFilename.Length == 0)
            usage("must specify image displacement map.");

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);
        ImageAndMetadata immeta = Image.Read(imageFilename);

        TriQuadMesh outputMesh = mesh.Displace((v0, v1) => Distance(v0, v1), edgeLength, (p, n, uv, nVertices) =>
        {
            ParallelFor(0, nVertices, (long i) =>
            {
                Point2f uvp = new(uvScale * uv[(int)i][0], 1 - uvScale * uv[(int)i][1]);
                Float d = immeta.image.Bilerp(uvp, WrapMode.Repeat).Average();
                p[(int)i] += new Vector3f(d * scale * n[(int)i]);
            });
        });

        if (!WritePLY(outFilename, outputMesh.triIndices.ToArray(), [], outputMesh.p.ToArray(),
                      outputMesh.n.ToArray(), outputMesh.uv.ToArray(), []))
            return 1;

        return 0;
    }

    private static int split(List<string> args)
    {
        string inPLY = "", outPLYBase = "";
        int maxFaces = 1000000;
        static void onError(string err)
        {
            usage(err);
            Environment.Exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "maxfaces", ref maxFaces, onError) ||
                ParseArg(args, ref iter, "outbase", ref outPLYBase, onError))
                { } // yaay
            else if (inPLY.Length == 0)
                inPLY = args[iter];
            else
                usage($"unexpected argument \"{args[iter]}\"");
        }

        if (inPLY.Length == 0)
            usage("must specify source PLY filename.");

        if (outPLYBase.Length == 0)
            outPLYBase = RemoveExtension(inPLY);

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(inPLY);

        if (mesh.quadIndices.Count > 0)
        {
            Console.Error.Write($"{inPLY}: sorry, mesh has quad faces. plytool currently only " +
                                "supports triangle meshes.\n");
            return 1;
        }
        if (mesh.faceIndices.Count > 0)
        {
            Console.Error.Write($"{inPLY}: sorry, mesh has faceIndices, which are not currently " +
                                "supported by plytool.\n");
            return 1;
        }

        int nFaces = mesh.triIndices.Count / 3;
        if (nFaces <= maxFaces)
        {
            Console.Error.Write($"{inPLY}: mesh has {nFaces} faces and so has not been split up.\n");
            return 0;
        }

        int nMeshes = (nFaces + maxFaces - 1) / maxFaces;
        Console.Error.Write($"{inPLY}: mesh has {nFaces} faces and will be split into {nMeshes} meshes.\n");

        int nFacesPerMesh = nFaces / nMeshes; // more or less...
        for (int i = 0; i < nMeshes; ++i)
        {
            int firstFaceIndex = i * nFacesPerMesh;
            int lastFaceIndex = (i + 1) * nFacesPerMesh;
            if (i == nMeshes - 1)
                lastFaceIndex = nFaces;

            Dictionary<int, int> vertexIndexRemap = [];
            List<int> indices = [];
            List<Point3f> p = [];
            List<Normal3f> n = [];
            List<Point2f> uv = [];
            for (int vertexIndex = 3 * firstFaceIndex; vertexIndex < 3 * lastFaceIndex; ++vertexIndex)
            {
                int index = mesh.triIndices[vertexIndex];
                if (vertexIndexRemap.TryGetValue(index, out int remapped))
                    indices.Add(remapped);
                else
                {
                    int newIndex = vertexIndexRemap.Count;
                    vertexIndexRemap[index] = newIndex;
                    indices.Add(newIndex);

                    p.Add(mesh.p[index]);
                    if (mesh.n.Count > 0)
                        n.Add(mesh.n[index]);
                    // TODO: there's no "s" in TriQuadMesh???
                    if (mesh.uv.Count > 0)
                        uv.Add(mesh.uv[index]);
                }
            }

            TriangleMesh triMesh = new(new Transform(), false /* reverse orientation */, indices, p, [], n, uv, []);
            if (!triMesh.WritePLY($"{outPLYBase}-{i:D3}.ply"))
                return 1;
        }

        return 0;
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
        InitPBRT(new PBRTOptions());

        if (argv.Length < 1)
        {
            help();
            return 0;
        }

        List<string> args = GetCommandLineArguments(argv);

        string cmd = args[0];
        args.RemoveAt(0);

        int ret = 0;
        if (cmd == "help")
            ret = help(args);
        else if (cmd == "cat")
            ret = cat(args);
        else if (cmd == "displace")
            ret = displace(args);
        else if (cmd == "info")
            ret = info(args);
        else if (cmd == "split")
            ret = split(args);
        else
            usage($"{cmd}: command unknown");

        CleanupPBRT();

        return ret;
    }
}

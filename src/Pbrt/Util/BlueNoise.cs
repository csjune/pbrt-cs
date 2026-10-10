// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/bluenoise.h and bluenoise.cpp
//
// The table is converted from bluenoise.cpp by tools/gen-tables.ps1 into
// src/Pbrt/Data/BlueNoise.bin (uint16 little-endian), which is embedded in
// the assembly. BlueNoiseTextures[t][x][y] is
// BlueNoiseTextures[(t * BlueNoiseResolution + x) * BlueNoiseResolution + y].

using System.Buffers.Binary;

namespace Pbrt;

public static partial class Util
{
    public const int BlueNoiseResolution = 128;
    public const int NumBlueNoiseTextures = 48;

    public static ReadOnlySpan<ushort> BlueNoiseTextures => BlueNoiseData.textures;

    // Blue noise lookup functions
    public static float BlueNoise(int textureIndex, Point2i p)
    {
        CHECK(textureIndex >= 0 && p.x >= 0 && p.y >= 0);
        textureIndex %= NumBlueNoiseTextures;
        int x = p.x % BlueNoiseResolution, y = p.y % BlueNoiseResolution;
        return BlueNoiseData.textures[(textureIndex * BlueNoiseResolution + x) * BlueNoiseResolution + y] / 65535f;
    }

    // Separate class so the table is loaded on first use.
    private static class BlueNoiseData
    {
        public static readonly ushort[] textures = Load();

        private static ushort[] Load()
        {
            const string name = "Pbrt.Data.BlueNoise.bin";
            using Stream stream = typeof(BlueNoiseData).Assembly.GetManifestResourceStream(name) ??
                                  throw new FatalErrorException($"{name}: resource not found");
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            ushort[] t = new ushort[NumBlueNoiseTextures * BlueNoiseResolution * BlueNoiseResolution];
            CHECK_EQ(2 * t.Length, bytes.Length);
            for (int i = 0; i < t.Length; ++i)
                t[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i));
            return t;
        }
    }
}

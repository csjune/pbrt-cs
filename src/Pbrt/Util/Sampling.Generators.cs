// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the sample generators in src/pbrt/util/sampling.h
// (Uniform1D/2D/3D, Stratified1D/2D/3D, Hammersley2D/3D).
//
// pbrt's generator classes with begin()/end() iterators become C# iterator
// methods: `for (Point2f u : Uniform2D(n))` is `foreach (Point2f u in
// Uniform2D(n))`. As in pbrt, each iteration starts from
// RNG(sequenceIndex); the seed argument is accepted but unused.

namespace Pbrt;

public static partial class Util
{
    public static IEnumerable<Float> Uniform1D(int n, ulong sequenceIndex = 0,
                                               ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        for (int i = 0; i < n; ++i)
            yield return rng.Uniform<Float>();
    }

    public static IEnumerable<Point2f> Uniform2D(int n, ulong sequenceIndex = 0,
                                                 ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        for (int i = 0; i < n; ++i)
        {
            Float x = rng.Uniform<Float>();
            Float y = rng.Uniform<Float>();
            yield return new Point2f(x, y);
        }
    }

    public static IEnumerable<Point3f> Uniform3D(int n, ulong sequenceIndex = 0,
                                                 ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        for (int i = 0; i < n; ++i)
        {
            Float x = rng.Uniform<Float>();
            Float y = rng.Uniform<Float>();
            Float z = rng.Uniform<Float>();
            yield return new Point3f(x, y, z);
        }
    }

    public static IEnumerable<Float> Stratified1D(int n, ulong sequenceIndex = 0,
                                                  ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        for (int i = 0; i < n; ++i)
            yield return (i + rng.Uniform<Float>()) / n;
    }

    public static IEnumerable<Point2f> Stratified2D(int nx, int ny, ulong sequenceIndex = 0,
                                                    ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        int n = nx * ny;
        for (int i = 0; i < n; ++i)
        {
            int ix = i % nx, iy = i / nx;
            Float x = (ix + rng.Uniform<Float>()) / nx;
            Float y = (iy + rng.Uniform<Float>()) / ny;
            yield return new Point2f(x, y);
        }
    }

    public static IEnumerable<Point3f> Stratified3D(int nx, int ny, int nz, ulong sequenceIndex = 0,
                                                    ulong seed = RNG.PCG32_DEFAULT_STATE)
    {
        RNG rng = new(sequenceIndex);
        int n = nx * ny * nz;
        for (int i = 0; i < n; ++i)
        {
            int ix = i % nx;
            int iy = (i / nx) % ny;
            int iz = i / (nx * ny);
            Float x = (ix + rng.Uniform<Float>()) / nx;
            Float y = (iy + rng.Uniform<Float>()) / ny;
            Float z = (iz + rng.Uniform<Float>()) / nz;
            yield return new Point3f(x, y, z);
        }
    }

    public static IEnumerable<Point2f> Hammersley2D(int n)
    {
        for (int i = 0; i < n; ++i)
            yield return new Point2f((Float)i / (Float)n, RadicalInverse(0, (ulong)i));
    }

    public static IEnumerable<Point3f> Hammersley3D(int n)
    {
        for (int i = 0; i < n; ++i)
            yield return new Point3f((Float)i / (Float)n, RadicalInverse(0, (ulong)i),
                                     RadicalInverse(1, (ulong)i));
    }
}

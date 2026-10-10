// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Tests for the port of src/pbrt/util/noise.h (pbrt has no noise tests).

namespace Pbrt.Tests;

public class NoiseTests
{
    [Fact]
    public void Basics()
    {
        RNG rng = new();
        // Perlin noise is zero at the lattice points.
        for (int i = 0; i < 100; ++i)
            Assert.Equal(0, Noise(rng.Uniform<uint>() % 1000, rng.Uniform<uint>() % 1000, rng.Uniform<uint>() % 1000));

        Float min = 0, max = 0;
        for (int i = 0; i < 10000; ++i)
        {
            Point3f p = new(100 * rng.Uniform<Float>() - 50, 100 * rng.Uniform<Float>() - 50, 100 * rng.Uniform<Float>() - 50);
            Float n = Noise(p);
            min = Min(min, n);
            max = Max(max, n);
            Assert.True(n >= -1.5f && n <= 1.5f, $"{p}: {n}");
            // Continuity
            Assert.True(FMath.Abs(Noise(p + new Vector3f(1e-4f, 0, 0)) - n) < 1e-2f);

            // DNoise() is a forward difference of Noise().
            Vector3f d = DNoise(p);
            Assert.Equal((Noise(p + new Vector3f(.01f, 0, 0)) - n) / .01f, d.x, 1e-3f);
        }
        Assert.True(min < -.4f && max > .4f, $"{min} {max}");
    }

    [Fact]
    public void FBmAndTurbulence()
    {
        Point3f p = new(1.3f, 2.7f, -.4f);
        // Large footprints filter out all octaves.
        Vector3f big = new(10, 0, 0);
        Assert.Equal(0, FBm(p, big, big, .5f, 8));
        // Turbulence then is the average contribution 0.2 of every octave
        // (pbrt adds the first one's twice: as the partial octave and in
        // the loop over the clamped ones).
        Float expected = .2f, o = 1;
        for (int i = 0; i < 8; ++i)
        {
            expected += o * .2f;
            o *= .5f;
        }
        Assert.Equal(expected, Turbulence(p, big, big, .5f, 8), 1e-6f);

        // Tiny footprints use all octaves.
        Vector3f tiny = new(1e-6f, 0, 0);
        Float fbm = FBm(p, tiny, tiny, .5f, 3);
        Float sum = 0, lambda = 1;
        o = 1;
        for (int i = 0; i < 3; ++i)
        {
            sum += o * Noise(lambda * p);
            lambda *= 1.99f;
            o *= .5f;
        }
        Assert.Equal(sum, fbm, 1e-6f);
        Assert.True(Turbulence(p, tiny, tiny, .5f, 3) >= 0);
    }
}

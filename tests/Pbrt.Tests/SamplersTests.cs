// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/samplers_test.cpp, plus checks of Clone(),
// Sampler.Create(), the MLTSampler and GetCameraSample() (not in pbrt's
// tests).

namespace Pbrt.Tests;

public class SamplersTests
{
    // Make sure all samplers give the same sample values if we go back to the
    // same pixel / sample index.
    [Fact]
    public void Sampler_ConsistentValues()
    {
        const int rootSpp = 4;
        const int spp = rootSpp * rootSpp;
        Point2i resolution = new(100, 101);

        List<Sampler> samplers =
        [
            new HaltonSampler(spp, resolution),
            new IndependentSampler(spp),
            new PaddedSobolSampler(spp, RandomizeStrategy.None),
            new PaddedSobolSampler(spp, RandomizeStrategy.PermuteDigits),
            new PaddedSobolSampler(spp, RandomizeStrategy.FastOwen),
            new PaddedSobolSampler(spp, RandomizeStrategy.Owen),
            new ZSobolSampler(spp, resolution, RandomizeStrategy.None),
            new ZSobolSampler(spp, resolution, RandomizeStrategy.PermuteDigits),
            new ZSobolSampler(spp, resolution, RandomizeStrategy.FastOwen),
            new ZSobolSampler(spp, resolution, RandomizeStrategy.Owen),
            new PMJ02BNSampler(spp),
            new StratifiedSampler(rootSpp, rootSpp, true),
            new SobolSampler(spp, resolution, RandomizeStrategy.None),
            new SobolSampler(spp, resolution, RandomizeStrategy.PermuteDigits),
            new SobolSampler(spp, resolution, RandomizeStrategy.Owen),
            new SobolSampler(spp, resolution, RandomizeStrategy.FastOwen),
        ];

        foreach (Sampler sampler in samplers)
        {
            List<Float>[] s1d = new List<Float>[spp];
            List<Point2f>[] s2d = new List<Point2f>[spp];

            for (int s = 0; s < spp; ++s)
            {
                s1d[s] = [];
                s2d[s] = [];
                sampler.StartPixelSample(new Point2i(1, 5), s);
                for (int i = 0; i < 10; ++i)
                {
                    s2d[s].Add(sampler.Get2D());
                    s1d[s].Add(sampler.Get1D());
                }
            }

            // Go somewhere else and generate some samples, just to make sure
            // things are shaken up.
            sampler.StartPixelSample(new Point2i(0, 6), 10);
            sampler.Get2D();
            sampler.Get2D();
            sampler.Get1D();

            // Now go back and generate samples again, but enumerate them in a
            // different order to make sure the sampler is doing the right
            // thing.
            for (int s = spp - 1; s >= 0; --s)
            {
                sampler.StartPixelSample(new Point2i(1, 5), s);
                for (int i = 0; i < s2d[s].Count; ++i)
                {
                    Assert.Equal(s2d[s][i], sampler.Get2D());
                    Assert.Equal(s1d[s][i], sampler.Get1D());
                }
            }
        }
    }

    private static void CheckElementary(string name, List<Point2f> samples, int logSamples)
    {
        for (int i = 0; i <= logSamples; ++i)
        {
            // Check one set of elementary intervals: number of intervals
            // in each dimension.
            int nx = 1 << i, ny = 1 << (logSamples - i);

            int[] count = new int[1 << logSamples];
            foreach (Point2f s in samples)
            {
                // Map the sample to an interval
                Float x = nx * s.x, y = ny * s.y;
                Assert.True(x >= 0);
                Assert.True(x < nx);
                Assert.True(y >= 0);
                Assert.True(y < ny);
                int index = (int)FMath.Floor(y) * nx + (int)FMath.Floor(x);
                Assert.True(index >= 0);
                Assert.True(index < count.Length);

                // This should be the first time a sample has landed in its
                // interval.
                Assert.True(count[index] == 0, $"Sampler {name} with interval {nx} x {ny}");
                ++count[index];
            }
        }
    }

    private static void CheckElementarySampler(string name, Sampler sampler, int logSamples, int res = 1)
    {
        // Get all of the samples for a pixel.
        int spp = sampler.SamplesPerPixel();
        List<Point2f> samples = [];
        foreach (Point2i p in new Bounds2i(new Point2i(0, 0), new Point2i(res, res)))
        {
            samples.Clear();
            for (int i = 0; i < spp; ++i)
            {
                sampler.StartPixelSample(p, i);
                samples.Add(sampler.GetPixel2D());
            }

            CheckElementary(name, samples, logSamples);
        }
    }

    // TODO: check Halton (where the elementary intervals are (2^i, 3^j)).

    [Fact]
    public void PaddedSobolSampler_ElementaryIntervals()
    {
        foreach (RandomizeStrategy rand in (RandomizeStrategy[])[RandomizeStrategy.None, RandomizeStrategy.PermuteDigits])
            for (int logSamples = 2; logSamples <= 10; ++logSamples)
                CheckElementarySampler("PaddedSobolSampler", new PaddedSobolSampler(1 << logSamples, rand), logSamples);
    }

    [Fact]
    public void ZSobolSampler_ElementaryIntervals()
    {
        foreach (int seed in (int[])[0, 1, 5, 6, 10, 15])
            foreach (RandomizeStrategy rand in (RandomizeStrategy[])[RandomizeStrategy.None, RandomizeStrategy.PermuteDigits])
                for (int logSamples = 2; logSamples <= 8; ++logSamples)
                    CheckElementarySampler($"ZSobolSampler - {Util.ToString(rand)} - {seed}",
                                           new ZSobolSampler(1 << logSamples, new Point2i(10, 10), rand, seed),
                                           logSamples, 10);
    }

    [Fact]
    public void SobolUnscrambledSampler_ElementaryIntervals()
    {
        for (int logSamples = 2; logSamples <= 10; ++logSamples)
            CheckElementarySampler("Sobol Unscrambled",
                                   new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.None),
                                   logSamples);
    }

    [Fact]
    public void SobolXORScrambledSampler_ElementaryIntervals()
    {
        for (int logSamples = 2; logSamples <= 10; ++logSamples)
            CheckElementarySampler("Sobol XOR Scrambled",
                                   new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.PermuteDigits),
                                   logSamples);
    }

    [Fact]
    public void SobolOwenScrambledSampler_ElementaryIntervals()
    {
        for (int logSamples = 2; logSamples <= 10; ++logSamples)
            CheckElementarySampler("Sobol Owen Scrambled",
                                   new SobolSampler(1 << logSamples, new Point2i(1, 1), RandomizeStrategy.Owen),
                                   logSamples);
    }

    [Fact]
    public void PMJ02BNSampler_ElementaryIntervals()
    {
        for (int logSamples = 2; logSamples <= 10; logSamples += 2)
            CheckElementarySampler("PMJ02BNSampler", new PMJ02BNSampler(1 << logSamples), logSamples);
    }

    [Fact]
    public void ZSobolSampler_ValidIndices()
    {
        Point2i res = new(16, 9);
        for (int logSamples = 0; logSamples <= 10; ++logSamples)
        {
            int spp = 1 << logSamples;
            ZSobolSampler sampler = new(spp, res, RandomizeStrategy.PermuteDigits);

            for (int dim = 0; dim < 7; dim += 3)
            {
                HashSet<ulong> returnedIndices = [];
                foreach (Point2i p in new Bounds2i(new Point2i(0, 0), res))
                {
                    ulong pow2Base = 0;
                    for (int i = 0; i < spp; ++i)
                    {
                        sampler.StartPixelSample(p, i, dim);
                        ulong index = sampler.GetSampleIndex();

                        // Make sure no index is repeated across multiple pixels
                        Assert.DoesNotContain(index, returnedIndices);
                        returnedIndices.Add(index);

                        // Make sure that all samples for this pixel are within the
                        // same pow2 aligned and sized range of the sample indices.
                        if (i == 0)
                            pow2Base = index / (ulong)spp;
                        else
                            Assert.Equal(pow2Base, index / (ulong)spp);
                    }
                }
            }
        }
    }

    [Fact]
    public void Sampler_Clone()
    {
        Sampler sampler = new StratifiedSampler(2, 2, true);
        sampler.StartPixelSample(new Point2i(3, 4), 1);
        sampler.Get2D();
        Sampler clone = sampler.Clone();
        // The clone continues from the same state, independently.
        Point2f a = sampler.Get2D();
        Point2f b = clone.Get2D();
        Assert.Equal(a, b);
        Assert.Equal(sampler.Get1D(), clone.Get1D());
    }

    [Fact]
    public void Sampler_Create()
    {
        ParameterDictionary empty = new();
        Point2i res = new(64, 32);
        Assert.IsType<ZSobolSampler>(Sampler.Create("zsobol", empty, res, null));
        Assert.Equal(16, Sampler.Create("halton", empty, res, null).SamplesPerPixel());
        Assert.Equal(4, Sampler.Create("independent", empty, res, null).SamplesPerPixel());
        Assert.Equal(16, Sampler.Create("stratified", empty, res, null).SamplesPerPixel());
        Assert.Throws<FatalErrorException>(() => Sampler.Create("random", empty, res, null));
    }

    [Fact]
    public void MLTSampler_RejectRestores()
    {
        MLTSampler sampler = new(16, 3, 0.01f, 0.3f, 2);
        int nRejected = 0;
        for (int iter = 0; iter < 100; ++iter)
        {
            string before = sampler.DumpState();
            sampler.StartIteration();
            sampler.StartStream(0);
            for (int i = 0; i < 5; ++i)
            {
                Float v = sampler.Get1D();
                Assert.True(v >= 0 && v < 1);
            }
            Point2f u = sampler.Get2D();
            Assert.True(u.x >= 0 && u.x < 1 && u.y >= 0 && u.y < 1);

            if (iter > 0 && iter % 3 == 0)
            {
                // Rejecting restores the state from before the iteration. (Every
                // iteration uses the same samples, so none were reset lazily
                // after a large step.)
                sampler.Reject();
                Assert.Equal(before, sampler.DumpState());
                ++nRejected;
            }
            else
            {
                sampler.Accept();
                if (iter > 0)
                    Assert.NotEqual(before, sampler.DumpState());
            }
        }
        Assert.True(nRejected > 10);
    }

    [Fact]
    public void GetCameraSample()
    {
        Sampler sampler = new IndependentSampler(4);
        Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
        for (int i = 0; i < 100; ++i)
        {
            sampler.StartPixelSample(new Point2i(7, 9), i);
            CameraSample cs = Util.GetCameraSample(sampler, new Point2i(7, 9), filter);
            Assert.True(cs.pFilm.x >= 7 && cs.pFilm.x <= 8 && cs.pFilm.y >= 9 && cs.pFilm.y <= 10);
            Assert.Equal(1, cs.filterWeight);
            Assert.True(cs.time >= 0 && cs.time < 1);
        }
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/containers_test.cpp (TypePack isn't ported, and
// HashMap is .NET's Dictionary), with SampledGrid tests added.

namespace Pbrt.Tests;

public class ContainersTests
{
    [Fact]
    public void Array2DBasics()
    {
        const int nx = 5, ny = 9;
        Array2D<Float> a = new(nx, ny);

        Assert.Equal(nx, a.XSize());
        Assert.Equal(ny, a.YSize());
        Assert.Equal(nx * ny, a.Size());

        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x)
                a[x, y] = 1000 * x + y;

        for (int y = 0; y < ny; ++y)
            for (int x = 0; x < nx; ++x)
                Assert.Equal(1000 * x + y, a[x, y]);
    }

    [Fact]
    public void Array2DBounds()
    {
        Bounds2i b = new(new Point2i(-4, 3), new Point2i(10, 7));
        Array2D<Point2f> a = new(b);

        Assert.Equal(b.pMax.x - b.pMin.x, a.XSize());
        Assert.Equal(b.pMax.y - b.pMin.y, a.YSize());

        foreach (Point2i p in b)
            a[p] = new Point2f(p.y, p.x);

        foreach (Point2i p in b)
            Assert.Equal(new Point2f(p.y, p.x), a[p]);
    }

    [Fact]
    public void InternCacheBasicString()
    {
        InternCache<string> cache = new();

        string one = cache.Lookup(new string("one"));
        Assert.Equal(1, cache.Size());
        Assert.Equal("one", one);

        string two = cache.Lookup(new string("two"));
        Assert.NotSame(one, two);
        Assert.Equal("two", two);

        string anotherOne = cache.Lookup(new string("one"));
        Assert.Equal(2, cache.Size());
        Assert.Equal("one", anotherOne);
        Assert.Same(one, anotherOne);
    }

    private sealed class BadIntHash : IEqualityComparer<int>
    {
        public bool Equals(int a, int b) => a == b;
        public int GetHashCode(int i) => i % 7;
    }

    private sealed class BadStringHash : IEqualityComparer<string>
    {
        public bool Equals(string? a, string? b) => a == b;
        public int GetHashCode(string s) => s.Length % 7;
    }

    [Fact]
    public void InternCacheBadHash()
    {
        InternCache<int> cache = new(new BadIntHash());

        int n = 10000;
        for (int i = 0; i < n; ++i)
        {
            int ii = PermutationElement((uint)i, (uint)n, 0xfeed00f5);
            Assert.Equal(ii, cache.Lookup(ii));
            Assert.Equal(i + 1, cache.Size());
        }
        for (int i = 0; i < n; ++i)
        {
            int ii = PermutationElement((uint)i, (uint)n, 0xfeed00f5);
            Assert.Equal(ii, cache.Lookup(ii));
            Assert.Equal(n, cache.Size());
        }

        // (Reference identity, which pbrt checks with pointers.)
        InternCache<string> sc = new(new BadStringHash());
        string[] firstPass = new string[1000];
        for (int i = 0; i < 1000; ++i)
            firstPass[i] = sc.Lookup(i.ToString());
        for (int i = 0; i < 1000; ++i)
            Assert.Same(firstPass[i], sc.Lookup(i.ToString()));
    }

    [Fact]
    public void InternCacheThreads()
    {
        InternCache<string> cache = new();
        string[][] results = new string[8][];
        ParallelFor(0, 8, t =>
        {
            results[t] = new string[2000];
            for (int i = 0; i < 2000; ++i)
                results[t][i] = cache.Lookup(((i * 7 + t) % 2000).ToString());
        });
        Assert.Equal(2000, cache.Size());
        // Every thread got the same instance for each string.
        for (int t = 0; t < 8; ++t)
            for (int i = 0; i < 2000; ++i)
                Assert.Same(cache.Lookup(new string(results[t][i])), results[t][i]);
    }

    [Fact]
    public void SampledGridLookup()
    {
        // f(x, y, z) = x + 2y + 4z at voxel centers.
        const int nx = 4, ny = 3, nz = 5;
        Float[] v = new Float[nx * ny * nz];
        Float f(Float x, Float y, Float z) => x + 2 * y + 4 * z;
        for (int z = 0; z < nz; ++z)
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x)
                    v[(z * ny + y) * nx + x] = f((x + .5f) / nx, (y + .5f) / ny, (z + .5f) / nz);
        SampledGrid<Float> grid = new(v, nx, ny, nz);
        Assert.Equal(nx, grid.XSize());
        Assert.Equal(ny, grid.YSize());
        Assert.Equal(nz, grid.ZSize());

        // Trilinear interpolation is exact for linear functions inside the
        // voxel centers.
        RNG rng = new();
        for (int i = 0; i < 100; ++i)
        {
            Point3f p = new(Lerp(rng.Uniform<Float>(), .5f / nx, 1 - .5f / nx),
                            Lerp(rng.Uniform<Float>(), .5f / ny, 1 - .5f / ny),
                            Lerp(rng.Uniform<Float>(), .5f / nz, 1 - .5f / nz));
            Assert.Equal(f(p.x, p.y, p.z), grid.Lookup(p), 1e-5f);
        }

        // Out of bounds samples are zero.
        Assert.Equal(0, grid.Lookup(new Point3i(-1, 0, 0)));
        Assert.Equal(0, grid.Lookup(new Point3i(0, 0, nz)));
        Assert.Equal(v[(1 * ny + 2) * nx + 3], grid.Lookup(new Point3i(3, 2, 1)));
        // ...so lookups near the edge go toward zero.
        Assert.Equal(v[0] / 2, grid.Lookup(new Point3f(0, .5f / ny, .5f / nz)), 1e-6f);

        // MaxValue over the whole grid and a corner.
        Bounds3f all = new(new Point3f(0, 0, 0), new Point3f(1, 1, 1));
        Assert.Equal(v.Max(), grid.MaxValue(all));
        Bounds3f corner = new(new Point3f(0, 0, 0), new Point3f(.1f, .1f, .1f));
        // (The voxel range is floor(p * n - .5) to that + 1 at the upper
        // end: here x and y stay at 0 and z goes to 1.)
        Assert.Equal(v[(1 * ny + 0) * nx + 0], grid.MaxValue(corner));

        SampledSpectrum s = grid.Lookup(new Point3f(.5f, .5f, .5f), (Float x) => new SampledSpectrum(x));
        Assert.Equal(grid.Lookup(new Point3f(.5f, .5f, .5f)), s[0], 1e-6f);
    }

    [Fact]
    public void BufferCacheBasics()
    {
        BufferCache<int> intBufferCache = BufferCaches.intBufferCache;

        int[] v = [1, 2, 3, 4, 5];

        long baseMem = intBufferCache.BytesUsed();

        int[] ptr = intBufferCache.LookupOrAdd(v);
        Assert.Equal(v, ptr);
        Assert.NotSame(v, ptr);

        Assert.Equal(5 * sizeof(int), intBufferCache.BytesUsed() - baseMem);

        Assert.Same(ptr, intBufferCache.LookupOrAdd(v));

        Assert.Equal(5 * sizeof(int), intBufferCache.BytesUsed() - baseMem);

        int[] v2 = [1, 2, 3, 4];
        int[] ptr2 = intBufferCache.LookupOrAdd(v2);
        Assert.NotSame(ptr, ptr2);
        Assert.Equal(v2, ptr2);

        Assert.Equal(9 * sizeof(int), intBufferCache.BytesUsed() - baseMem);
    }

    [Fact]
    public void BufferCachePoints()
    {
        BufferCache<Point3f> cache = new();
        Point3f[] a = [new(1, 2, 3), new(4, 5, 6)];
        Point3f[] b = [new(1, 2, 3), new(4, 5, 7)];
        Point3f[] ca = cache.LookupOrAdd(a), cb = cache.LookupOrAdd(b);
        Assert.NotSame(ca, cb);
        Assert.Same(ca, cache.LookupOrAdd((Point3f[])[new(1, 2, 3), new(4, 5, 6)]));
        Assert.Equal(2 * 2 * 3 * sizeof(Float), cache.BytesUsed());
    }

    [Fact]
    public void PMJ02BN()
    {
        // First and last entries from pmj02tables.cpp.
        Assert.Equal(4142185728u, PMJ02bnSamples[0]);
        Assert.Equal(3847968256u, PMJ02bnSamples[1]);
        Assert.Equal(1828938024u, PMJ02bnSamples[^2]);
        Assert.Equal(4281666539u, PMJ02bnSamples[^1]);

        Point2f p = GetPMJ02BNSample(0, 0);
        Assert.Equal((Float)(4142185728 / 4294967296.0), p.x);
        Assert.Equal((Float)(3847968256 / 4294967296.0), p.y);
        Assert.Equal(GetPMJ02BNSample(1, 7), GetPMJ02BNSample(1 + nPMJ02bnSets, 7));

        // Each set is (0,2)-sequence: the first 16 points of each set have
        // one point in each of the 4x4 strata.
        for (int s = 0; s < nPMJ02bnSets; ++s)
        {
            bool[] seen = new bool[16];
            for (int i = 0; i < 16; ++i)
            {
                Point2f q = GetPMJ02BNSample(s, i);
                Assert.True(q.x >= 0 && q.x < 1 && q.y >= 0 && q.y < 1);
                int cell = (int)(q.x * 4) + 4 * (int)(q.y * 4);
                Assert.False(seen[cell]);
                seen[cell] = true;
            }
        }
    }
}

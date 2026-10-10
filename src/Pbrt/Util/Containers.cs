// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/containers.h
//
// TypePack and friends (template metaprogramming for TaggedPointer) aren't
// needed. InlinedVector is only used where it matters for allocation
// (ImageChannelValues; see Image.cs). HashMap's and InternCache's hash
// functions are IEqualityComparers, like std::hash.

namespace Pbrt;

// Array2D Definition
public sealed class Array2D<T>
{
    // Array2D Private Members
    private readonly Bounds2i extent;
    private readonly T[] values;

    // Array2D Public Methods
    public Array2D() : this(new Bounds2i(new Point2i(0, 0), new Point2i(0, 0))) { }

    public Array2D(Bounds2i extent)
    {
        this.extent = extent;
        values = new T[extent.Area()];
    }

    public Array2D(Bounds2i extent, T def) : this(extent)
    {
        Array.Fill(values, def);
    }

    public Array2D(ReadOnlySpan<T> data, int nx, int ny)
        : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny)))
    {
        data.CopyTo(values);
    }

    public Array2D(int nx, int ny) : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny))) { }

    public Array2D(int nx, int ny, T def)
        : this(new Bounds2i(new Point2i(0, 0), new Point2i(nx, ny)), def) { }

    public Array2D(Array2D<T> a) : this(a.AsSpan(), a.XSize(), a.YSize()) { }

    public ref T this[Point2i p]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            DCHECK(InsideExclusive(p, extent));
            p.x -= extent.pMin.x;
            p.y -= extent.pMin.y;
            return ref values[p.x + (extent.pMax.x - extent.pMin.x) * p.y];
        }
    }

    // pbrt's operator()(x, y)
    public ref T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref this[new Point2i(x, y)];
    }

    public int Size() => extent.Area();
    public int XSize() => extent.pMax.x - extent.pMin.x;
    public int YSize() => extent.pMax.y - extent.pMin.y;

    public Bounds2i Extent() => extent;

    // pbrt's begin()/end() and pstd::span<const T>(array2d)
    public Span<T> AsSpan() => values;

    public override string ToString()
    {
        var s = new System.Text.StringBuilder($"[ Array2D extent: {extent} values: [");
        for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
        {
            s.Append(" [ ");
            for (int x = extent.pMin.x; x < extent.pMax.x; ++x)
            {
                s.Append(this[x, y]);
                if (x != extent.pMax.x - 1)
                    s.Append(", ");
            }
            s.Append(" ]");
        }
        s.Append(" ] ]");
        return s.ToString();
    }
}

// SampledGrid Definition
//
// pbrt's templated Lookup(p, convert) is written for the result types it's
// used with (Float and SampledSpectrum); lookups of Float grids without a
// conversion are extension methods in Util. Out-of-bounds samples are
// convert(new T()), as pbrt's convert(T{}).
public sealed class SampledGrid<T> where T : new()
{
    // SampledGrid Private Members
    private readonly T[] values;
    private readonly int nx, ny, nz;

    // SampledGrid Public Methods
    public SampledGrid()
    {
        values = [];
    }

    public SampledGrid(ReadOnlySpan<T> v, int nx, int ny, int nz)
    {
        values = v.ToArray();
        this.nx = nx;
        this.ny = ny;
        this.nz = nz;
        CHECK_EQ(nx * ny * nz, values.Length);
    }

    public int XSize() => nx;
    public int YSize() => ny;
    public int ZSize() => nz;
    public ReadOnlySpan<T> Values => values;

    public Float Lookup(Point3f p, Func<T, Float> convert)
    {
        // Compute voxel coordinates and offsets for _p_
        Point3f pSamples = new(p.x * nx - .5f, p.y * ny - .5f, p.z * nz - .5f);
        Point3i pi = new(Floor(pSamples));
        Vector3f d = pSamples - new Point3f(pi);

        // Return trilinearly interpolated voxel values
        Float d00 = Lerp(d.x, Lookup(pi, convert), Lookup(pi + new Vector3i(1, 0, 0), convert));
        Float d10 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 0), convert), Lookup(pi + new Vector3i(1, 1, 0), convert));
        Float d01 = Lerp(d.x, Lookup(pi + new Vector3i(0, 0, 1), convert), Lookup(pi + new Vector3i(1, 0, 1), convert));
        Float d11 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 1), convert), Lookup(pi + new Vector3i(1, 1, 1), convert));
        return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
    }

    public SampledSpectrum Lookup(Point3f p, Func<T, SampledSpectrum> convert)
    {
        // Compute voxel coordinates and offsets for _p_
        Point3f pSamples = new(p.x * nx - .5f, p.y * ny - .5f, p.z * nz - .5f);
        Point3i pi = new(Floor(pSamples));
        Vector3f d = pSamples - new Point3f(pi);

        // Return trilinearly interpolated voxel values
        SampledSpectrum d00 = Lerp(d.x, Lookup(pi, convert), Lookup(pi + new Vector3i(1, 0, 0), convert));
        SampledSpectrum d10 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 0), convert), Lookup(pi + new Vector3i(1, 1, 0), convert));
        SampledSpectrum d01 = Lerp(d.x, Lookup(pi + new Vector3i(0, 0, 1), convert), Lookup(pi + new Vector3i(1, 0, 1), convert));
        SampledSpectrum d11 = Lerp(d.x, Lookup(pi + new Vector3i(0, 1, 1), convert), Lookup(pi + new Vector3i(1, 1, 1), convert));
        return Lerp(d.z, Lerp(d.y, d00, d10), Lerp(d.y, d01, d11));
    }

    public U Lookup<U>(Point3i p, Func<T, U> convert)
    {
        Bounds3i sampleBounds = new(new Point3i(0, 0, 0), new Point3i(nx, ny, nz));
        if (!InsideExclusive(p, sampleBounds))
            return convert(new T());
        return convert(values[(p.z * ny + p.y) * nx + p.x]);
    }

    public T Lookup(Point3i p)
    {
        Bounds3i sampleBounds = new(new Point3i(0, 0, 0), new Point3i(nx, ny, nz));
        if (!InsideExclusive(p, sampleBounds))
            return new T();
        return values[(p.z * ny + p.y) * nx + p.x];
    }

    public Float MaxValue(Bounds3f bounds, Func<T, Float> convert)
    {
        Point3f ps0 = new(bounds.pMin.x * nx - .5f, bounds.pMin.y * ny - .5f, bounds.pMin.z * nz - .5f);
        Point3f ps1 = new(bounds.pMax.x * nx - .5f, bounds.pMax.y * ny - .5f, bounds.pMax.z * nz - .5f);
        Point3i pi0 = Max(new Point3i(Floor(ps0)), new Point3i(0, 0, 0));
        Point3i pi1 = Min(new Point3i(Floor(ps1)) + new Vector3i(1, 1, 1), new Point3i(nx - 1, ny - 1, nz - 1));

        Float maxValue = Lookup(pi0, convert);
        for (int z = pi0.z; z <= pi1.z; ++z)
            for (int y = pi0.y; y <= pi1.y; ++y)
                for (int x = pi0.x; x <= pi1.x; ++x)
                    maxValue = Max(maxValue, Lookup(new Point3i(x, y, z), convert));

        return maxValue;
    }

    public override string ToString() =>
        $"[ SampledGrid nx: {nx} ny: {ny} nz: {nz} values: [ {string.Join(", ", values)} ] ]";
}

// InternCache Definition
//
// Returns a single shared instance for each distinct item (pbrt returns a
// pointer to its copy). Items are compared with _hash_ (std::hash and
// operator== in pbrt).
public sealed class InternCache<T> where T : notnull
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<T, T> items;

    public InternCache(IEqualityComparer<T>? hash = null)
    {
        items = new(hash ?? EqualityComparer<T>.Default);
    }

    public T Lookup(T item, Func<T, T> create) => items.GetOrAdd(item, create);

    // (pbrt copies _item_; here the first instance looked up is kept.)
    public T Lookup(T item) => items.GetOrAdd(item, item);

    public int Size() => items.Count;
}

public static partial class Util
{
    // SampledGrid<Float> lookups without a conversion.
    public static Float Lookup(this SampledGrid<Float> grid, Point3f p) => grid.Lookup(p, (Float v) => v);

    public static Float MaxValue(this SampledGrid<Float> grid, Bounds3f bounds) =>
        grid.MaxValue(bounds, (Float v) => v);
}


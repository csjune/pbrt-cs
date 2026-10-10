// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/media.h and media.cpp
//
// The majorant iterators are classes that SampleRay() allocates. pbrt's
// templated SampleT_maj() calls the medium through the Medium interface;
// its callback gets the RNG as a ref parameter since C# lambdas can't
// capture the caller's ref RNG.
// NanoVDBMedium uses the NanoVDB port in Util/NanoVDB.cs.

namespace Pbrt;

// HGPhaseFunction Definition
public sealed class HGPhaseFunction : PhaseFunction
{
    // HGPhaseFunction Private Members
    private readonly Float g;

    // HGPhaseFunction Public Methods
    public HGPhaseFunction(Float g) => this.g = g;

    public override Float p(Vector3f wo, Vector3f wi) => HenyeyGreenstein(Dot(wo, wi), g);

    public override PhaseFunctionSample? Sample_p(Vector3f wo, Point2f u)
    {
        Vector3f wi = SampleHenyeyGreenstein(wo, g, u, out Float pdf);
        return new PhaseFunctionSample(pdf, wi, pdf);
    }

    public override Float PDF(Vector3f wo, Vector3f wi) => p(wo, wi);

    public static string Name() => "Henyey-Greenstein";

    public override string ToString() => $"[ HGPhaseFunction g: {FloatToString(g)} ]";
}

// MediumProperties Definition
public struct MediumProperties(SampledSpectrum sigma_a, SampledSpectrum sigma_s, PhaseFunction phase,
                               SampledSpectrum Le)
{
    public SampledSpectrum sigma_a = sigma_a, sigma_s = sigma_s;
    public PhaseFunction phase = phase;
    public SampledSpectrum Le = Le;
}

// HomogeneousMajorantIterator Definition
public sealed class HomogeneousMajorantIterator : RayMajorantIterator
{
    private readonly RayMajorantSegment seg;
    private bool called;

    // HomogeneousMajorantIterator Public Methods
    public HomogeneousMajorantIterator() => called = true;

    public HomogeneousMajorantIterator(Float tMin, Float tMax, SampledSpectrum sigma_maj)
    {
        seg = new RayMajorantSegment(tMin, tMax, sigma_maj);
        called = false;
    }

    public override RayMajorantSegment? Next()
    {
        if (called)
            return null;
        called = true;
        return seg;
    }

    public override string ToString() =>
        $"[ HomogeneousMajorantIterator seg: {seg} called: {(called ? "true" : "false")} ]";
}

// MajorantGrid Definition
public sealed class MajorantGrid
{
    // MajorantGrid Public Members
    public Bounds3f bounds;
    public readonly Float[] voxels;
    public readonly Point3i res;

    // MajorantGrid Public Methods
    public MajorantGrid(Bounds3f bounds, Point3i res)
    {
        this.bounds = bounds;
        voxels = new Float[res.x * res.y * res.z];
        this.res = res;
    }

    public Float Lookup(int x, int y, int z)
    {
        DCHECK(x >= 0 && x < res.x && y >= 0 && y < res.y && z >= 0 && z < res.z);
        return voxels[x + res.x * (y + res.y * z)];
    }

    public void Set(int x, int y, int z, Float v)
    {
        DCHECK(x >= 0 && x < res.x && y >= 0 && y < res.y && z >= 0 && z < res.z);
        voxels[x + res.x * (y + res.y * z)] = v;
    }

    public Bounds3f VoxelBounds(int x, int y, int z)
    {
        Point3f p0 = new((Float)x / res.x, (Float)y / res.y, (Float)z / res.z);
        Point3f p1 = new((Float)(x + 1) / res.x, (Float)(y + 1) / res.y, (Float)(z + 1) / res.z);
        return new Bounds3f(p0, p1);
    }
}

// DDAMajorantIterator Definition
public sealed class DDAMajorantIterator : RayMajorantIterator
{
    // DDAMajorantIterator Private Members
    private readonly SampledSpectrum sigma_t;
    private Float tMin = Infinity, tMax = -Infinity;
    private readonly MajorantGrid? grid;
    private readonly Float[] nextCrossingT = new Float[3], deltaT = new Float[3];
    private readonly int[] step = new int[3], voxelLimit = new int[3], voxel = new int[3];
    private static readonly int[] cmpToAxis = [2, 1, 2, 1, 2, 2, 0, 0];

    // DDAMajorantIterator Public Methods
    public DDAMajorantIterator() { }

    public DDAMajorantIterator(Ray ray, Float tMin, Float tMax, MajorantGrid grid, SampledSpectrum sigma_t)
    {
        this.tMin = tMin;
        this.tMax = tMax;
        this.grid = grid;
        this.sigma_t = sigma_t;
        // Set up 3D DDA for ray through the majorant grid
        Vector3f diag = grid.bounds.Diagonal();
        Ray rayGrid = new(new Point3f(grid.bounds.Offset(ray.o)),
                          new Vector3f(ray.d.x / diag.x, ray.d.y / diag.y, ray.d.z / diag.z));
        Point3f gridIntersect = rayGrid.At(tMin);
        for (int axis = 0; axis < 3; ++axis)
        {
            // Initialize ray stepping parameters for _axis_
            // Compute current voxel for axis and handle negative zero direction
            voxel[axis] = (int)Clamp(gridIntersect[axis] * grid.res[axis], 0, grid.res[axis] - 1);
            deltaT[axis] = 1 / (FMath.Abs(rayGrid.d[axis]) * grid.res[axis]);
            if (rayGrid.d[axis] == -0f)
                rayGrid.d[axis] = 0f;

            if (rayGrid.d[axis] >= 0)
            {
                // Handle ray with positive direction for voxel stepping
                Float nextVoxelPos = (Float)(voxel[axis] + 1) / grid.res[axis];
                nextCrossingT[axis] = tMin + (nextVoxelPos - gridIntersect[axis]) / rayGrid.d[axis];
                step[axis] = 1;
                voxelLimit[axis] = grid.res[axis];
            }
            else
            {
                // Handle ray with negative direction for voxel stepping
                Float nextVoxelPos = (Float)voxel[axis] / grid.res[axis];
                nextCrossingT[axis] = tMin + (nextVoxelPos - gridIntersect[axis]) / rayGrid.d[axis];
                step[axis] = -1;
                voxelLimit[axis] = -1;
            }
        }
    }

    public override RayMajorantSegment? Next()
    {
        if (tMin >= tMax)
            return null;
        // Find _stepAxis_ for stepping to next voxel and exit point _tVoxelExit_
        int bits = ((nextCrossingT[0] < nextCrossingT[1] ? 1 : 0) << 2) +
                   ((nextCrossingT[0] < nextCrossingT[2] ? 1 : 0) << 1) +
                   (nextCrossingT[1] < nextCrossingT[2] ? 1 : 0);
        int stepAxis = cmpToAxis[bits];
        Float tVoxelExit = FMath.Min(tMax, nextCrossingT[stepAxis]);

        // Get _maxDensity_ for current voxel and initialize _RayMajorantSegment_, _seg_
        SampledSpectrum sigma_maj = sigma_t * grid!.Lookup(voxel[0], voxel[1], voxel[2]);
        RayMajorantSegment seg = new(tMin, tVoxelExit, sigma_maj);

        // Advance to next voxel in maximum density grid
        tMin = tVoxelExit;
        if (nextCrossingT[stepAxis] > tMax)
            tMin = tMax;
        voxel[stepAxis] += step[stepAxis];
        if (voxel[stepAxis] == voxelLimit[stepAxis])
            tMin = tMax;
        nextCrossingT[stepAxis] += deltaT[stepAxis];

        return seg;
    }

    public override string ToString() =>
        $"[ DDAMajorantIterator tMin: {FloatToString(tMin)} tMax: {FloatToString(tMax)} sigma_t: {sigma_t} " +
        $"nextCrossingT: [ {string.Join(" ", nextCrossingT.Select(FloatToString))} ] " +
        $"deltaT: [ {string.Join(" ", deltaT.Select(FloatToString))} ] step: [ {string.Join(" ", step)} ] " +
        $"voxelLimit: [ {string.Join(" ", voxelLimit)} ] voxel: [ {string.Join(" ", voxel)} ] ]";
}

// HomogeneousMedium Definition
public sealed class HomogeneousMedium : Medium
{
    // HomogeneousMedium Private Data
    private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec, Le_spec;
    private readonly HGPhaseFunction phase;

    // HomogeneousMedium Public Methods
    public HomogeneousMedium(Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale, Spectrum Le, Float LeScale,
                             Float g)
    {
        sigma_a_spec = new DenselySampledSpectrum(sigma_a);
        sigma_s_spec = new DenselySampledSpectrum(sigma_s);
        Le_spec = new DenselySampledSpectrum(Le);
        phase = new HGPhaseFunction(g);
        sigma_a_spec.Scale(sigmaScale);
        sigma_s_spec.Scale(sigmaScale);
        Le_spec.Scale(LeScale);
    }

    // HomogeneousMedium Method Definitions
    public static HomogeneousMedium Create(ParameterDictionary parameters, FileLoc? loc)
    {
        Spectrum? sig_a = null, sig_s = null;
        string preset = parameters.GetOneString("preset", "");
        if (preset.Length > 0)
        {
            if (!GetMediumScatteringProperties(preset, out sig_a, out sig_s))
                Warning(loc, $"Material preset \"{preset}\" not found.");
        }
        if (sig_a == null)
            sig_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        if (sig_s == null)
            sig_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);

        Spectrum? Le = parameters.GetOneSpectrum("Le", null, SpectrumType.Illuminant);
        Float LeScale = parameters.GetOneFloat("Lescale", 1f);
        if (Le == null || Le.MaxValue() == 0)
            Le = new ConstantSpectrum(0f);
        else
            LeScale /= SpectrumToPhotometric(Le);

        Float sigmaScale = parameters.GetOneFloat("scale", 1f);
        Float g = parameters.GetOneFloat("g", 0.0f);

        return new HomogeneousMedium(sig_a, sig_s, sigmaScale, Le, LeScale, g);
    }

    public override bool IsEmissive() => Le_spec.MaxValue() > 0;

    public override MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda)
    {
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
        SampledSpectrum Le = Le_spec.Sample(lambda);
        return new MediumProperties(sigma_a, sigma_s, phase, Le);
    }

    public override RayMajorantIterator SampleRay(Ray ray, Float tMax, in SampledWavelengths lambda)
    {
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
        return new HomogeneousMajorantIterator(0, tMax, sigma_a + sigma_s);
    }

    public override string ToString() =>
        $"[ Homogeneous medium sigma_a_spec: {sigma_a_spec} sigma_s_spec: {sigma_s_spec} Le_spec: {Le_spec} " +
        $"phase: {phase} ]";
}

// GridMedium Definition
public sealed class GridMedium : Medium
{
    internal static readonly StatMemoryCounter volumeGridBytes = new("Memory/Volume grids");

    // GridMedium Private Members
    private readonly Bounds3f bounds;
    private readonly Transform renderFromMedium;
    private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
    private readonly SampledGrid<Float> densityGrid;
    private readonly HGPhaseFunction phase;
    private readonly SampledGrid<Float>? temperatureGrid;
    private readonly DenselySampledSpectrum Le_spec;
    private readonly SampledGrid<Float> LeScale;
    private readonly bool isEmissive;
    private readonly Float temperatureScale, temperatureOffset;
    private readonly MajorantGrid majorantGrid;

    // GridMedium Method Definitions
    public GridMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s,
                      Float sigmaScale, Float g, SampledGrid<Float> d, SampledGrid<Float>? temperature,
                      Float temperatureScale, Float temperatureOffset, Spectrum Le, SampledGrid<Float> LeGrid)
    {
        this.bounds = bounds;
        this.renderFromMedium = renderFromMedium;
        sigma_a_spec = new DenselySampledSpectrum(sigma_a);
        sigma_s_spec = new DenselySampledSpectrum(sigma_s);
        densityGrid = d;
        phase = new HGPhaseFunction(g);
        temperatureGrid = temperature;
        this.temperatureScale = temperatureScale;
        this.temperatureOffset = temperatureOffset;
        Le_spec = new DenselySampledSpectrum(Le);
        LeScale = LeGrid;
        majorantGrid = new MajorantGrid(bounds, new Point3i(16, 16, 16));
        sigma_a_spec.Scale(sigmaScale);
        sigma_s_spec.Scale(sigmaScale);

        // (sizeof(Float) values)
        volumeGridBytes.Add(LeScale.Values.Length * 4);
        volumeGridBytes.Add(densityGrid.Values.Length * 4);
        if (temperatureGrid != null)
            volumeGridBytes.Add(temperatureGrid.Values.Length * 4);

        isEmissive = temperatureGrid != null || Le_spec.MaxValue() > 0;

        // Initialize _majorantGrid_ for _GridMedium_
        for (int z = 0; z < majorantGrid.res.z; ++z)
            for (int y = 0; y < majorantGrid.res.y; ++y)
                for (int x = 0; x < majorantGrid.res.x; ++x)
                {
                    Bounds3f vb = majorantGrid.VoxelBounds(x, y, z);
                    majorantGrid.Set(x, y, z, densityGrid.MaxValue(vb, v => v));
                }
    }

    public static GridMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc? loc)
    {
        List<Float> density = parameters.GetFloatArray("density");
        List<Float> temperature = parameters.GetFloatArray("temperature");

        if (density.Count == 0)
            ErrorExit(loc, "No \"density\" value provided for grid medium.");
        int nDensity = density.Count;

        if (temperature.Count > 0)
            if (nDensity != temperature.Count)
                ErrorExit(loc, $"Different number of samples ({nDensity} vs {temperature.Count}) provided for " +
                               "\"density\" and \"temperature\".");

        int nx = parameters.GetOneInt("nx", 1);
        int ny = parameters.GetOneInt("ny", 1);
        int nz = parameters.GetOneInt("nz", 1);
        if (nDensity != nx * ny * nz)
            ErrorExit(loc, $"Grid medium has {nDensity} density values; expected nx*ny*nz = {nx * ny * nz}");

        // Create Density Grid
        SampledGrid<Float> densityGrid = new(density.ToArray(), nx, ny, nz);

        SampledGrid<Float>? temperatureGrid = null;
        if (temperature.Count > 0)
            temperatureGrid = new SampledGrid<Float>(temperature.ToArray(), nx, ny, nz);

        Spectrum? Le = parameters.GetOneSpectrum("Le", null, SpectrumType.Illuminant);

        if (Le != null && temperature.Count > 0)
            ErrorExit(loc, "Both \"Le\" and \"temperature\" values were provided.");

        Float LeNorm = 1;
        if (Le == null || Le.MaxValue() == 0)
            Le = new ConstantSpectrum(0f);
        else
            LeNorm = 1 / SpectrumToPhotometric(Le);

        SampledGrid<Float> LeGrid;
        List<Float> LeScale = parameters.GetFloatArray("Lescale");

        if (LeScale.Count == 0)
            LeGrid = new SampledGrid<Float>([LeNorm], 1, 1, 1);
        else
        {
            if (LeScale.Count != nx * ny * nz)
                ErrorExit($"Expected {nx} x {ny} {nz} = {nx * ny * nz} values for \"Lescale\" but were " +
                          $"given {LeScale.Count}.");
            for (int i = 0; i < nx * ny * nz; ++i)
                LeScale[i] *= LeNorm;
            LeGrid = new SampledGrid<Float>(LeScale.ToArray(), nx, ny, nz);
        }

        Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
        Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));

        Float g = parameters.GetOneFloat("g", 0f);
        Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        Float sigmaScale = parameters.GetOneFloat("scale", 1f);

        Float temperatureOffset = parameters.GetOneFloat("temperatureoffset",
                                                         parameters.GetOneFloat("temperaturecutoff", 0f));
        Float temperatureScale = parameters.GetOneFloat("temperaturescale", 1f);

        return new GridMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, sigmaScale, g, densityGrid,
                              temperatureGrid, temperatureScale, temperatureOffset, Le, LeGrid);
    }

    public override string ToString() =>
        $"[ GridMedium bounds: {bounds} renderFromMedium: {renderFromMedium} phase: {phase} " +
        $"LeScale: {LeScale} (grids elided) ]";

    public override bool IsEmissive() => isEmissive;

    public override MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda)
    {
        // Sample spectra for grid medium $\sigmaa$ and $\sigmas$
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

        // Scale scattering coefficients by medium density at _p_
        p = renderFromMedium.ApplyInverse(p);
        p = new Point3f(bounds.Offset(p));
        Float d = densityGrid.Lookup(p);
        sigma_a *= d;
        sigma_s *= d;

        // Compute grid emission _Le_ at _p_
        SampledSpectrum Le = new(0f);
        if (isEmissive)
        {
            Float scale = LeScale.Lookup(p);
            if (scale > 0)
            {
                // Compute emitted radiance using _temperatureGrid_ or _Le_spec_
                if (temperatureGrid != null)
                {
                    Float temp = temperatureGrid.Lookup(p);
                    // Added after book publication: optionally offset and scale
                    // temperature based on user-supplied parameters. (Match
                    // NanoVDBMedium functionality.)
                    temp = (temp - temperatureOffset) * temperatureScale;
                    if (temp > 100f)
                        Le = scale * new BlackbodySpectrum(temp).Sample(lambda);
                }
                else
                    Le = scale * Le_spec.Sample(lambda);
            }
        }

        return new MediumProperties(sigma_a, sigma_s, phase, Le);
    }

    public override RayMajorantIterator SampleRay(Ray ray, Float raytMax, in SampledWavelengths lambda)
    {
        // Transform ray to medium's space and compute bounds overlap
        ray = renderFromMedium.ApplyInverse(ray, ref raytMax);
        if (!bounds.IntersectP(ray.o, ray.d, raytMax, out Float tMin, out Float tMax))
            return new DDAMajorantIterator();
        DCHECK_LE(tMax, raytMax);

        // Sample spectra for grid medium $\sigmaa$ and $\sigmas$
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

        SampledSpectrum sigma_t = sigma_a + sigma_s;
        return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
    }
}

// RGBGridMedium Definition
public sealed class RGBGridMedium : Medium
{
    // RGBGridMedium Private Members
    private readonly Bounds3f bounds;
    private readonly Transform renderFromMedium;
    private readonly SampledGrid<RGBIlluminantSpectrum>? LeGrid;
    private readonly Float LeScale;
    private readonly HGPhaseFunction phase;
    private readonly SampledGrid<RGBUnboundedSpectrum>? sigma_aGrid, sigma_sGrid;
    private readonly Float sigmaScale;
    private readonly MajorantGrid majorantGrid;

    // RGBGridMedium Method Definitions
    public RGBGridMedium(Bounds3f bounds, Transform renderFromMedium, Float g,
                         SampledGrid<RGBUnboundedSpectrum>? rgbA, SampledGrid<RGBUnboundedSpectrum>? rgbS,
                         Float sigmaScale, SampledGrid<RGBIlluminantSpectrum>? rgbLe, Float LeScale)
    {
        this.bounds = bounds;
        this.renderFromMedium = renderFromMedium;
        phase = new HGPhaseFunction(g);
        sigma_aGrid = rgbA;
        sigma_sGrid = rgbS;
        this.sigmaScale = sigmaScale;
        majorantGrid = new MajorantGrid(bounds, new Point3i(16, 16, 16));
        LeGrid = rgbLe;
        this.LeScale = LeScale;
        if (LeGrid != null)
            CHECK(sigma_aGrid != null);
        // (sizeof(RGBUnboundedSpectrum) and sizeof(RGBIlluminantSpectrum) are
        // 16 and 24 in pbrt.)
        if (sigma_aGrid != null)
            GridMedium.volumeGridBytes.Add(sigma_aGrid.Values.Length * 16);
        if (sigma_sGrid != null)
            GridMedium.volumeGridBytes.Add(sigma_sGrid.Values.Length * 16);
        if (LeGrid != null)
            GridMedium.volumeGridBytes.Add(LeGrid.Values.Length * 24);

        // Initialize _majorantGrid_ for _RGBGridMedium_
        for (int z = 0; z < majorantGrid.res.z; ++z)
            for (int y = 0; y < majorantGrid.res.y; ++y)
                for (int x = 0; x < majorantGrid.res.x; ++x)
                {
                    Bounds3f vb = majorantGrid.VoxelBounds(x, y, z);
                    // Initialize _majorantGrid_ voxel for RGB $\sigmaa$ and $\sigmas$
                    Float maxSigma_t = (sigma_aGrid != null ? sigma_aGrid.MaxValue(vb, s => s.MaxValue()) : 1) +
                                       (sigma_sGrid != null ? sigma_sGrid.MaxValue(vb, s => s.MaxValue()) : 1);
                    majorantGrid.Set(x, y, z, sigmaScale * maxSigma_t);
                }
    }

    public static RGBGridMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc? loc)
    {
        List<RGB> sigma_a = parameters.GetRGBArray("sigma_a");
        List<RGB> sigma_s = parameters.GetRGBArray("sigma_s");
        List<RGB> Le = parameters.GetRGBArray("Le");

        if (sigma_a.Count == 0 && sigma_s.Count == 0)
            ErrorExit(loc, "RGB grid requires \"sigma_a\" and/or \"sigma_s\" parameter values.");

        int nDensity;
        if (sigma_a.Count > 0)
        {
            nDensity = sigma_a.Count;
            if (sigma_s.Count > 0 && nDensity != sigma_s.Count)
                ErrorExit(loc, $"Different number of samples ({nDensity} vs {sigma_s.Count}) provided for " +
                               "\"sigma_a\" and \"sigma_s\".");
        }
        else
            nDensity = sigma_s.Count;

        if (Le.Count > 0 && sigma_a.Count == 0)
            ErrorExit(loc, "RGB grid requires \"sigma_a\" if \"Le\" value provided.");

        if (Le.Count > 0 && nDensity != Le.Count)
            ErrorExit($"Expected {nDensity} values for \"Le\" parameter but were given {Le.Count}.");

        int nx = parameters.GetOneInt("nx", 1);
        int ny = parameters.GetOneInt("ny", 1);
        int nz = parameters.GetOneInt("nz", 1);
        if (nDensity != nx * ny * nz)
            ErrorExit(loc, $"RGB grid medium has {nDensity} density values; expected nx*ny*nz = {nx * ny * nz}");

        SampledGrid<RGBUnboundedSpectrum>? sigma_aGrid = null, sigma_sGrid = null;
        SampledGrid<RGBIlluminantSpectrum>? LeGrid = null;

        RGBColorSpace colorSpace = parameters.ColorSpace()!;
        if (sigma_a.Count > 0)
            sigma_aGrid = new SampledGrid<RGBUnboundedSpectrum>(
                sigma_a.Select(rgb => new RGBUnboundedSpectrum(colorSpace, rgb)).ToArray(), nx, ny, nz);
        if (sigma_s.Count > 0)
            sigma_sGrid = new SampledGrid<RGBUnboundedSpectrum>(
                sigma_s.Select(rgb => new RGBUnboundedSpectrum(colorSpace, rgb)).ToArray(), nx, ny, nz);
        if (Le.Count > 0)
            LeGrid = new SampledGrid<RGBIlluminantSpectrum>(
                Le.Select(rgb => new RGBIlluminantSpectrum(colorSpace, rgb)).ToArray(), nx, ny, nz);

        Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
        Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));
        Float LeScale = parameters.GetOneFloat("Lescale", 1f);
        Float g = parameters.GetOneFloat("g", 0f);
        Float sigmaScale = parameters.GetOneFloat("scale", 1f);

        return new RGBGridMedium(new Bounds3f(p0, p1), renderFromMedium, g, sigma_aGrid, sigma_sGrid, sigmaScale,
                                 LeGrid, LeScale);
    }

    public override string ToString() =>
        $"[ RGBGridMedium bounds: {bounds} renderFromMedium: {renderFromMedium} phase: {phase} " +
        $"sigmaScale: {FloatToString(sigmaScale)} LeScale: {FloatToString(LeScale)} (grids elided) ]";

    public override bool IsEmissive() => LeGrid != null && LeScale > 0;

    public override MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda)
    {
        p = renderFromMedium.ApplyInverse(p);
        p = new Point3f(bounds.Offset(p));
        // Compute $\sigmaa$ and $\sigmas$ for _RGBGridMedium_
        SampledWavelengths lam = lambda;
        SampledSpectrum sigma_a = sigmaScale *
                                  (sigma_aGrid != null ? sigma_aGrid.Lookup(p, s => s.Sample(lam)) : new SampledSpectrum(1f));
        SampledSpectrum sigma_s = sigmaScale *
                                  (sigma_sGrid != null ? sigma_sGrid.Lookup(p, s => s.Sample(lam)) : new SampledSpectrum(1f));

        // Find emitted radiance _Le_ for _RGBGridMedium_
        SampledSpectrum Le = new(0f);
        if (LeGrid != null && LeScale > 0)
            Le = LeScale * LeGrid.Lookup(p, s => s.Sample(lam));

        return new MediumProperties(sigma_a, sigma_s, phase, Le);
    }

    public override RayMajorantIterator SampleRay(Ray ray, Float raytMax, in SampledWavelengths lambda)
    {
        // Transform ray to medium's space and compute bounds overlap
        ray = renderFromMedium.ApplyInverse(ray, ref raytMax);
        if (!bounds.IntersectP(ray.o, ray.d, raytMax, out Float tMin, out Float tMax))
            return new DDAMajorantIterator();
        DCHECK_LE(tMax, raytMax);

        SampledSpectrum sigma_t = new(1);
        return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
    }
}

// CloudMedium Definition
public sealed class CloudMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s,
                                Float g, Float density, Float wispiness, Float frequency) : Medium
{
    // CloudMedium Private Members
    private readonly HGPhaseFunction phase = new(g);
    private readonly DenselySampledSpectrum sigma_a_spec = new(sigma_a), sigma_s_spec = new(sigma_s);

    // CloudMedium Public Methods
    public static CloudMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc? loc)
    {
        Float density = parameters.GetOneFloat("density", 1);
        Float g = parameters.GetOneFloat("g", 0f);
        Float wispiness = parameters.GetOneFloat("wispiness", 1);
        Float frequency = parameters.GetOneFloat("frequency", 5);
        Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);

        Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
        Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));

        return new CloudMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, g, density, wispiness,
                               frequency);
    }

    public override string ToString() =>
        $"[ CloudMedium bounds: {bounds} renderFromMedium: {renderFromMedium} phase: {phase} " +
        $"sigma_a_spec: {sigma_a_spec} sigma_s_spec: {sigma_s_spec} density: {FloatToString(density)} " +
        $"wispiness: {FloatToString(wispiness)} frequency: {FloatToString(frequency)} ]";

    public override bool IsEmissive() => false;

    public override MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda)
    {
        // Compute sampled spectra for cloud $\sigmaa$ and $\sigmas$ at _p_
        Float d = Density(renderFromMedium.ApplyInverse(p));
        SampledSpectrum sigma_a = d * sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = d * sigma_s_spec.Sample(lambda);

        return new MediumProperties(sigma_a, sigma_s, phase, new SampledSpectrum(0f));
    }

    public override RayMajorantIterator SampleRay(Ray ray, Float raytMax, in SampledWavelengths lambda)
    {
        // Transform ray to medium's space and compute bounds overlap
        ray = renderFromMedium.ApplyInverse(ray, ref raytMax);
        if (!bounds.IntersectP(ray.o, ray.d, raytMax, out Float tMin, out Float tMax))
            return new HomogeneousMajorantIterator();
        DCHECK_LE(tMax, raytMax);

        // Compute $\sigmat$ bound for cloud medium and initialize majorant iterator
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
        SampledSpectrum sigma_t = sigma_a + sigma_s;
        return new HomogeneousMajorantIterator(tMin, tMax, sigma_t);
    }

    // CloudMedium Private Methods
    private Float Density(Point3f p)
    {
        Point3f pp = frequency * p;
        if (wispiness > 0)
        {
            // Perturb cloud lookup point _pp_ using noise
            Float vomega = 0.05f * wispiness, vlambda = 10f;
            for (int i = 0; i < 2; ++i)
            {
                pp += vomega * DNoise(vlambda * pp);
                vomega *= 0.5f;
                vlambda *= 1.99f;
            }
        }
        // Sum scales of noise to approximate cloud density
        Float d = 0;
        Float omega = 0.5f, lambda = 1f;
        for (int i = 0; i < 5; ++i)
        {
            d += omega * Noise(lambda * pp);
            omega *= 0.5f;
            lambda *= 1.99f;
        }

        // Model decrease in density with altitude and return final cloud density
        d = Clamp((1 - p.y) * 4.5f * density * d, 0, 1);
        d += 2 * FMath.Max(0, 0.5f - p.y);
        return Clamp(d, 0, 1);
    }
}

// NanoVDBMedium Definition
public sealed class NanoVDBMedium : Medium
{
    // NanoVDBMedium Private Members
    private readonly Bounds3f bounds;
    private readonly Transform renderFromMedium;
    private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
    private readonly HGPhaseFunction phase;
    private readonly MajorantGrid majorantGrid;
    private readonly NanoVDBFloatGrid densityFloatGrid;
    private readonly NanoVDBFloatGrid? temperatureFloatGrid;
    private readonly Float LeScale, temperatureOffset, temperatureScale;

    // NanoVDBMedium Method Definitions
    private static NanoVDBGridHandle? ReadGrid(string filename, string gridName, FileLoc? loc)
    {
        NanoVDBGridHandle? grid = null;
        try
        {
            grid = NanoVDBIO.ReadGrid(filename, gridName);
        }
        catch (NanoVDBException e)
        {
            ErrorExit($"nanovdb: {filename}: {e.Message}");
        }

        if (grid != null)
        {
            if (!grid.IsFogVolume() && !grid.IsUnknown())
                ErrorExit(loc, $"{filename}: \"{gridName}\" isn't a FogVolume grid?");

            LOG_VERBOSE($"{filename}: found {grid.ActiveVoxelCount()} \"{gridName}\" voxels");
        }

        return grid;
    }

    // (pbrt's grid<float>() is null for a grid of another type, which it
    // then dereferences; here that's an error.)
    private static NanoVDBFloatGrid FloatGrid(NanoVDBGridHandle grid, string filename)
    {
        NanoVDBFloatGrid? fg = grid.FloatGrid();
        if (fg == null)
            ErrorExit($"nanovdb: {filename}: grid isn't a float grid.");
        return fg!;
    }

    public NanoVDBMedium(Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale, Float g,
                         NanoVDBGridHandle dg, NanoVDBGridHandle? tg, Float LeScale, Float temperatureOffset,
                         Float temperatureScale, string filename = "")
    {
        this.renderFromMedium = renderFromMedium;
        sigma_a_spec = new DenselySampledSpectrum(sigma_a);
        sigma_s_spec = new DenselySampledSpectrum(sigma_s);
        phase = new HGPhaseFunction(g);
        majorantGrid = new MajorantGrid(new Bounds3f(), new Point3i(64, 64, 64));
        this.LeScale = LeScale;
        this.temperatureOffset = temperatureOffset;
        this.temperatureScale = temperatureScale;
        densityFloatGrid = FloatGrid(dg, filename);

        sigma_a_spec.Scale(sigmaScale);
        sigma_s_spec.Scale(sigmaScale);

        var (bmin, bmax) = (densityFloatGrid.WorldBBoxMin(), densityFloatGrid.WorldBBoxMax());
        bounds = new Bounds3f(new Point3f((Float)bmin.x, (Float)bmin.y, (Float)bmin.z),
                              new Point3f((Float)bmax.x, (Float)bmax.y, (Float)bmax.z));

        if (tg != null)
        {
            temperatureFloatGrid = FloatGrid(tg, filename);
            temperatureFloatGrid.Extrema(out _, out float maxTemperature);
            LOG_VERBOSE($"Max temperature: {FloatToString(maxTemperature)}");

            var (tmin, tmax) = (temperatureFloatGrid.WorldBBoxMin(), temperatureFloatGrid.WorldBBoxMax());
            bounds = Union(bounds, new Bounds3f(new Point3f((Float)tmin.x, (Float)tmin.y, (Float)tmin.z),
                                                new Point3f((Float)tmax.x, (Float)tmax.y, (Float)tmax.z)));
        }

        majorantGrid.bounds = bounds;

        // Initialize majorantGrid
        LOG_VERBOSE($"Starting nanovdb grid GetMaxDensityGrid()");

        int gridSize = majorantGrid.res.x * majorantGrid.res.y * majorantGrid.res.z;
        Bounds3f b = bounds;
        ParallelFor(0, gridSize, (long index) =>
        {
            // Indices into majorantGrid
            int x = (int)(index % majorantGrid.res.x);
            int y = (int)((index / majorantGrid.res.x) % majorantGrid.res.y);
            int z = (int)(index / (majorantGrid.res.x * majorantGrid.res.y));
            CHECK_EQ(index, x + majorantGrid.res.x * (y + majorantGrid.res.y * z));

            // World (aka medium) space bounds of this max grid cell
            Bounds3f wb = new(b.Lerp(new Point3f((Float)x / majorantGrid.res.x, (Float)y / majorantGrid.res.y,
                                                 (Float)z / majorantGrid.res.z)),
                              b.Lerp(new Point3f((Float)(x + 1) / majorantGrid.res.x,
                                                 (Float)(y + 1) / majorantGrid.res.y,
                                                 (Float)(z + 1) / majorantGrid.res.z)));

            // Compute corresponding NanoVDB index-space bounds in floating-point.
            var i0 = densityFloatGrid.WorldToIndexF(wb.pMin.x, wb.pMin.y, wb.pMin.z);
            var i1 = densityFloatGrid.WorldToIndexF(wb.pMax.x, wb.pMax.y, wb.pMax.z);

            // Now find integer index-space bounds, accounting for both
            // filtering and the overall index bounding box.
            var (bboxMin, bboxMax) = densityFloatGrid.IndexBBox();
            Float delta = 1f; // Filter slop
            int nx0 = Math.Max((int)(i0.x - delta), bboxMin.x);
            int nx1 = Math.Min((int)(i1.x + delta), bboxMax.x);
            int ny0 = Math.Max((int)(i0.y - delta), bboxMin.y);
            int ny1 = Math.Min((int)(i1.y + delta), bboxMax.y);
            int nz0 = Math.Max((int)(i0.z - delta), bboxMin.z);
            int nz1 = Math.Min((int)(i1.z + delta), bboxMax.z);

            // FIXME: While the following is properly conservative, it can lead
            // to voxels with majorants that are much higher than any actual
            // volume density value in their extent. The issue comes up when
            // a) the density at a sample outside the is much higher than in the
            // voxel's interior and b) that sample only has a minimal
            // contribution in practice due to trilinear interpolation.  We
            // compute a majorant as if it might fully contribute, even though
            // it can't.  Fixing this would require careful handling of the
            // boundary samples.  The impact of these majorants is not
            // insignificant; they cause a roughly 10% slowdown in practice
            // due to excess null scattering in such voxels.
            float maxValue = 0;
            NanoVDBFloatGrid.Accessor accessor = new(densityFloatGrid);
            // Apparently nanovdb integer bounding boxes are inclusive on
            // the upper end...
            for (int nz = nz0; nz <= nz1; ++nz)
                for (int ny = ny0; ny <= ny1; ++ny)
                    for (int nx = nx0; nx <= nx1; ++nx)
                        maxValue = MathF.Max(maxValue, accessor.GetValue(nx, ny, nz));

            // Only write into maxGrid once when we're done to minimize
            // cache thrashing..
            majorantGrid.Set(x, y, z, maxValue);
        });

        LOG_VERBOSE($"Finished nanovdb grid GetMaxDensityGrid()");
    }

    public override string ToString() =>
        $"[ NanoVDBMedium bounds: {bounds} LeScale: {FloatToString(LeScale)} " +
        $"temperatureOffset: {FloatToString(temperatureOffset)} " +
        $"temperatureScale: {FloatToString(temperatureScale)} (grids elided) ]";

    public static NanoVDBMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc? loc)
    {
        string filename = ResolveFilename(parameters.GetOneString("filename", ""));
        if (filename.Length == 0)
            ErrorExit(loc, "Must supply \"filename\" to \"nanovdb\" medium.");

        string gridname = parameters.GetOneString("gridname", "density");
        NanoVDBGridHandle? densityGrid = ReadGrid(filename, gridname, loc);
        if (densityGrid == null)
            ErrorExit(loc, $"{filename}: didn't find \"density\" grid.");

        string temperaturename = parameters.GetOneString("temperaturename", "temperature");
        NanoVDBGridHandle? temperatureGrid = ReadGrid(filename, temperaturename, loc);

        Float LeScale = parameters.GetOneFloat("Lescale", 1f);
        Float temperatureOffset = parameters.GetOneFloat("temperatureoffset",
                                                         parameters.GetOneFloat("temperaturecutoff", 0f));
        Float temperatureScale = parameters.GetOneFloat("temperaturescale", 1f);

        Float g = parameters.GetOneFloat("g", 0f);
        Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded) ?? new ConstantSpectrum(1f);
        Float sigmaScale = parameters.GetOneFloat("scale", 1f);

        return new NanoVDBMedium(renderFromMedium, sigma_a, sigma_s, sigmaScale, g, densityGrid!, temperatureGrid,
                                 LeScale, temperatureOffset, temperatureScale, filename);
    }

    public override bool IsEmissive() => temperatureFloatGrid != null && LeScale > 0;

    public override MediumProperties SamplePoint(Point3f p, in SampledWavelengths lambda)
    {
        // Sample spectra for grid $\sigmaa$ and $\sigmas$
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

        // Scale scattering coefficients by medium density at _p_
        p = renderFromMedium.ApplyInverse(p);

        var pIndex = densityFloatGrid.WorldToIndexF(((float)p.x, (float)p.y, (float)p.z));
        Float d = densityFloatGrid.SampleTrilinear(pIndex);

        return new MediumProperties(sigma_a * d, sigma_s * d, phase, Le(p, lambda));
    }

    public override RayMajorantIterator SampleRay(Ray ray, Float raytMax, in SampledWavelengths lambda)
    {
        // Transform ray to medium's space and compute bounds overlap
        ray = renderFromMedium.ApplyInverse(ray, ref raytMax);
        if (!bounds.IntersectP(ray.o, ray.d, raytMax, out Float tMin, out Float tMax))
            return new DDAMajorantIterator();
        DCHECK_LE(tMax, raytMax);

        // Sample spectra for grid $\sigmaa$ and $\sigmas$
        SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
        SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

        SampledSpectrum sigma_t = sigma_a + sigma_s;
        return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
    }

    // NanoVDBMedium Private Methods
    private SampledSpectrum Le(Point3f p, in SampledWavelengths lambda)
    {
        if (temperatureFloatGrid == null)
            return new SampledSpectrum(0f);
        var pIndex = temperatureFloatGrid.WorldToIndexF(((float)p.x, (float)p.y, (float)p.z));
        Float temp = temperatureFloatGrid.SampleTrilinear(pIndex);
        temp = (temp - temperatureOffset) * temperatureScale;
        if (temp <= 100f)
            return new SampledSpectrum(0f);
        return LeScale * new BlackbodySpectrum(temp).Sample(lambda);
    }
}

public abstract partial class Medium
{
    public static Medium Create(string name, ParameterDictionary parameters, Transform renderFromMedium,
                                FileLoc? loc)
    {
        Medium? m = null;
        if (name == "homogeneous")
            m = HomogeneousMedium.Create(parameters, loc);
        else if (name == "uniformgrid")
            m = GridMedium.Create(parameters, renderFromMedium, loc);
        else if (name == "rgbgrid")
            m = RGBGridMedium.Create(parameters, renderFromMedium, loc);
        else if (name == "cloud")
            m = CloudMedium.Create(parameters, renderFromMedium, loc);
        else if (name == "nanovdb")
            m = NanoVDBMedium.Create(parameters, renderFromMedium, loc);
        else
            ErrorExit(loc, $"{name}: medium unknown.");

        if (m == null)
            ErrorExit(loc, $"{name}: unable to create medium.");

        parameters.ReportUnused();
        return m!;
    }
}

// Callback for SampleT_maj(): pbrt's lambdas capture the RNG by reference;
// here it's passed to the callback.
public delegate bool MajorantCallback(Point3f p, in MediumProperties mp, SampledSpectrum sigma_maj,
                                      SampledSpectrum T_maj, ref RNG rng);

public static partial class Util
{
    public static SampledSpectrum SampleT_maj(Ray ray, Float tMax, Float u, ref RNG rng,
                                              in SampledWavelengths lambda, MajorantCallback callback)
    {
        // Normalize ray direction and update _tMax_ accordingly
        tMax *= Length(ray.d);
        ray.d = Normalize(ray.d);

        // Initialize _MajorantIterator_ for ray majorant sampling
        Medium medium = ray.medium!;
        RayMajorantIterator iter = medium.SampleRay(ray, tMax, lambda);

        // Generate ray majorant samples until termination
        SampledSpectrum T_maj = new(1f);
        bool done = false;
        while (!done)
        {
            // Get next majorant segment from iterator and sample it
            RayMajorantSegment? sego = iter.Next();
            if (sego is not RayMajorantSegment seg)
                return T_maj;
            // Handle zero-valued majorant for current segment
            if (seg.sigma_maj[0] == 0)
            {
                Float dt = seg.tMax - seg.tMin;
                // Handle infinite _dt_ for ray majorant segment
                if (IsInf(dt))
                    dt = Float.MaxValue;

                T_maj *= FastExp(-dt * seg.sigma_maj);
                continue;
            }

            // Generate samples along current majorant segment
            Float tMin = seg.tMin;
            while (true)
            {
                // Try to generate sample along current majorant segment
                Float t = tMin + SampleExponential(u, seg.sigma_maj[0]);
                u = rng.Uniform<Float>();
                if (t < seg.tMax)
                {
                    // Call callback function for sample within segment
                    T_maj *= FastExp(-(t - tMin) * seg.sigma_maj);
                    MediumProperties mp = medium.SamplePoint(ray.At(t), lambda);
                    if (!callback(ray.At(t), mp, seg.sigma_maj, T_maj, ref rng))
                    {
                        // Returning out of doubly-nested while loop is not as good perf. wise
                        // on the GPU vs using "done" here.
                        done = true;
                        break;
                    }
                    T_maj = new SampledSpectrum(1f);
                    tMin = t;
                }
                else
                {
                    // Handle sample past end of majorant segment
                    Float dt = seg.tMax - tMin;
                    // Handle infinite _dt_ for ray majorant segment
                    if (IsInf(dt))
                        dt = Float.MaxValue;

                    T_maj *= FastExp(-dt * seg.sigma_maj);
                    break;
                }
            }
        }
        return new SampledSpectrum(1f);
    }

    // (The values are doubles converted to Float, as in pbrt.)
    private static RGB R(double r, double g, double b) => new((Float)r, (Float)g, (Float)b);

    private readonly record struct MeasuredSS(string name, RGB sigma_prime_s, RGB sigma_a); // mm^-1

    private static readonly MeasuredSS[] SubsurfaceParameterTable =
    [
        // From "A Practical Model for Subsurface Light Transport"
        // Jensen, Marschner, Levoy, Hanrahan
        // Proc SIGGRAPH 2001
        new("Apple", R(2.29, 2.39, 1.97), R(0.0030, 0.0034, 0.046)),
        new("Chicken1", R(0.15, 0.21, 0.38), R(0.015, 0.077, 0.19)),
        new("Chicken2", R(0.19, 0.25, 0.32), R(0.018, 0.088, 0.20)),
        new("Cream", R(7.38, 5.47, 3.15), R(0.0002, 0.0028, 0.0163)),
        new("Ketchup", R(0.18, 0.07, 0.03), R(0.061, 0.97, 1.45)),
        new("Marble", R(2.19, 2.62, 3.00), R(0.0021, 0.0041, 0.0071)),
        new("Potato", R(0.68, 0.70, 0.55), R(0.0024, 0.0090, 0.12)),
        new("Skimmilk", R(0.70, 1.22, 1.90), R(0.0014, 0.0025, 0.0142)),
        new("Skin1", R(0.74, 0.88, 1.01), R(0.032, 0.17, 0.48)),
        new("Skin2", R(1.09, 1.59, 1.79), R(0.013, 0.070, 0.145)),
        new("Spectralon", R(11.6, 20.4, 14.9), R(0.00, 0.00, 0.00)),
        new("Wholemilk", R(2.55, 3.21, 3.77), R(0.0011, 0.0024, 0.014)),

        // From "Acquiring Scattering Properties of Participating Media by
        // Dilution",
        // Narasimhan, Gupta, Donner, Ramamoorthi, Nayar, Jensen
        // Proc SIGGRAPH 2006
        new("Lowfat Milk", R(0.89187, 1.5136, 2.532), R(0.002875, 0.00575, 0.0115)),
        new("Reduced Milk", R(2.4858, 3.1669, 4.5214), R(0.0025556, 0.0051111, 0.012778)),
        new("Regular Milk", R(4.5513, 5.8294, 7.136), R(0.0015333, 0.0046, 0.019933)),
        new("Espresso", R(0.72378, 0.84557, 1.0247), R(4.7984, 6.5751, 8.8493)),
        new("Mint Mocha Coffee", R(0.31602, 0.38538, 0.48131), R(3.772, 5.8228, 7.82)),
        new("Lowfat Soy Milk", R(0.30576, 0.34233, 0.61664), R(0.0014375, 0.0071875, 0.035937)),
        new("Regular Soy Milk", R(0.59223, 0.73866, 1.4693), R(0.0019167, 0.0095833, 0.065167)),
        new("Lowfat Chocolate Milk", R(0.64925, 0.83916, 1.1057), R(0.0115, 0.0368, 0.1564)),
        new("Regular Chocolate Milk", R(1.4585, 2.1289, 2.9527), R(0.010063, 0.043125, 0.14375)),
        new("Coke", R(8.9053e-05, 8.372e-05, 0), R(0.10014, 0.16503, 0.2468)),
        new("Pepsi", R(6.1697e-05, 4.2564e-05, 0), R(0.091641, 0.14158, 0.20729)),
        new("Sprite", R(6.0306e-06, 6.4139e-06, 6.5504e-06), R(0.001886, 0.0018308, 0.0020025)),
        new("Gatorade", R(0.0024574, 0.003007, 0.0037325), R(0.024794, 0.019289, 0.008878)),
        new("Chardonnay", R(1.7982e-05, 1.3758e-05, 1.2023e-05), R(0.010782, 0.011855, 0.023997)),
        new("White Zinfandel", R(1.7501e-05, 1.9069e-05, 1.288e-05), R(0.012072, 0.016184, 0.019843)),
        new("Merlot", R(2.1129e-05, 0, 0), R(0.11632, 0.25191, 0.29434)),
        new("Budweiser Beer", R(2.4356e-05, 2.4079e-05, 1.0564e-05), R(0.011492, 0.024911, 0.057786)),
        new("Coors Light Beer", R(5.0922e-05, 4.301e-05, 0), R(0.006164, 0.013984, 0.034983)),
        new("Clorox", R(0.0024035, 0.0031373, 0.003991), R(0.0033542, 0.014892, 0.026297)),
        new("Apple Juice", R(0.00013612, 0.00015836, 0.000227), R(0.012957, 0.023741, 0.052184)),
        new("Cranberry Juice", R(0.00010402, 0.00011646, 7.8139e-05), R(0.039437, 0.094223, 0.12426)),
        new("Grape Juice", R(5.382e-05, 0, 0), R(0.10404, 0.23958, 0.29325)),
        new("Ruby Grapefruit Juice", R(0.011002, 0.010927, 0.011036), R(0.085867, 0.18314, 0.25262)),
        new("White Grapefruit Juice", R(0.22826, 0.23998, 0.32748), R(0.0138, 0.018831, 0.056781)),
        new("Shampoo", R(0.0007176, 0.0008303, 0.0009016), R(0.014107, 0.045693, 0.061717)),
        new("Strawberry Shampoo", R(0.00015671, 0.00015947, 1.518e-05), R(0.01449, 0.05796, 0.075823)),
        new("Head & Shoulders Shampoo", R(0.023805, 0.028804, 0.034306), R(0.084621, 0.15688, 0.20365)),
        new("Lemon Tea Powder", R(0.040224, 0.045264, 0.051081), R(2.4288, 4.5757, 7.2127)),
        new("Orange Powder", R(0.00015617, 0.00017482, 0.0001762), R(0.001449, 0.003441, 0.007863)),
        new("Pink Lemonade Powder", R(0.00012103, 0.00013073, 0.00012528), R(0.001165, 0.002366, 0.003195)),
        new("Cappuccino Powder", R(1.8436, 2.5851, 2.1662), R(35.844, 49.547, 61.084)),
        new("Salt Powder", R(0.027333, 0.032451, 0.031979), R(0.28415, 0.3257, 0.34148)),
        new("Sugar Powder", R(0.00022272, 0.00025513, 0.000271), R(0.012638, 0.031051, 0.050124)),
        new("Suisse Mocha Powder", R(2.7979, 3.5452, 4.3365), R(17.502, 27.004, 35.433)),
        new("Pacific Ocean Surface Water", R(0.0001764, 0.00032095, 0.00019617), R(0.031845, 0.031324, 0.030147)),
    ];

    public static bool GetMediumScatteringProperties(string name, out Spectrum? sigma_a, out Spectrum? sigma_s)
    {
        foreach (MeasuredSS mss in SubsurfaceParameterTable)
        {
            if (name == mss.name)
            {
                sigma_a = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, mss.sigma_a);
                sigma_s = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, mss.sigma_prime_s);
                return true;
            }
        }
        sigma_a = sigma_s = null;
        return false;
    }
}

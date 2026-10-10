// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/filters.h and filters.cpp

namespace Pbrt;

// FilterSample Definition
public struct FilterSample
{
    public Point2f p;
    public Float weight;

    public FilterSample(Point2f p, Float weight)
    {
        this.p = p;
        this.weight = weight;
    }
}

// FilterSampler Definition
public sealed class FilterSampler
{
    // FilterSampler Private Members
    private readonly Bounds2f domain;
    private readonly Array2D<Float> f;
    private readonly PiecewiseConstant2D distrib;

    // FilterSampler Method Definitions
    public FilterSampler(Filter filter)
    {
        domain = new Bounds2f(new Point2f(-filter.Radius()), new Point2f(filter.Radius()));
        f = new Array2D<Float>((int)(32 * filter.Radius().x), (int)(32 * filter.Radius().y));
        // Tabularize unnormalized filter function in _f_
        for (int y = 0; y < f.YSize(); ++y)
            for (int x = 0; x < f.XSize(); ++x)
            {
                Point2f p = domain.Lerp(new Point2f((x + 0.5f) / f.XSize(), (y + 0.5f) / f.YSize()));
                f[x, y] = filter.Evaluate(p);
            }

        // Compute sampling distribution for filter
        distrib = new PiecewiseConstant2D(f, domain);
    }

    // FilterSampler Public Methods
    public FilterSample Sample(Point2f u)
    {
        Point2f p = distrib.Sample(u, out Float pdf, out Point2i pi);
        return new FilterSample(p, f[pi] / pdf);
    }

    public override string ToString() => $"[ FilterSampler domain: {domain} f: {f} distrib: {distrib} ]";
}

// BoxFilter Definition
public sealed class BoxFilter : Filter
{
    private readonly Vector2f radius;

    // BoxFilter Public Methods
    public BoxFilter() : this(new Vector2f(0.5f, 0.5f)) { }

    public BoxFilter(Vector2f radius) => this.radius = radius;

    public override Vector2f Radius() => radius;

    public override Float Evaluate(Point2f p) =>
        (FMath.Abs(p.x) <= radius.x && FMath.Abs(p.y) <= radius.y) ? 1 : 0;

    public override FilterSample Sample(Point2f u)
    {
        Point2f p = new(Lerp(u[0], -radius.x, radius.x), Lerp(u[1], -radius.y, radius.y));
        return new FilterSample(p, 1);
    }

    public override Float Integral() => 2 * radius.x * 2 * radius.y;

    // Box Filter Method Definitions
    public override string ToString() => $"[ BoxFilter radius: {radius} ]";

    public static BoxFilter Create(ParameterDictionary parameters, FileLoc? loc)
    {
        Float xw = parameters.GetOneFloat("xradius", 0.5f);
        Float yw = parameters.GetOneFloat("yradius", 0.5f);
        return new BoxFilter(new Vector2f(xw, yw));
    }
}

// GaussianFilter Definition
public sealed class GaussianFilter : Filter
{
    // GaussianFilter Private Members
    private readonly Vector2f radius;
    private readonly Float sigma, expX, expY;
    private readonly FilterSampler sampler;

    // GaussianFilter Public Methods
    public GaussianFilter(Vector2f radius, Float sigma = 0.5f)
    {
        this.radius = radius;
        this.sigma = sigma;
        expX = Gaussian(radius.x, 0, sigma);
        expY = Gaussian(radius.y, 0, sigma);
        sampler = new FilterSampler(this);
    }

    public override Vector2f Radius() => radius;

    public override Float Evaluate(Point2f p) =>
        FMath.Max(0, Gaussian(p.x, 0, sigma) - expX) * FMath.Max(0, Gaussian(p.y, 0, sigma) - expY);

    public override Float Integral() =>
        (GaussianIntegral(-radius.x, radius.x, 0, sigma) - 2 * radius.x * expX) *
        (GaussianIntegral(-radius.y, radius.y, 0, sigma) - 2 * radius.y * expY);

    public override FilterSample Sample(Point2f u) => sampler.Sample(u);

    // Gaussian Filter Method Definitions
    public override string ToString() =>
        $"[ GaussianFilter radius: {radius} sigma: {FloatToString(sigma)} expX: {FloatToString(expX)} " +
        $"expY: {FloatToString(expY)} sampler: {sampler} ]";

    public static GaussianFilter Create(ParameterDictionary parameters, FileLoc? loc)
    {
        // Find common filter parameters
        Float xw = parameters.GetOneFloat("xradius", 1.5f);
        Float yw = parameters.GetOneFloat("yradius", 1.5f);
        Float sigma = parameters.GetOneFloat("sigma", 0.5f);  // equivalent to old alpha = 2
        return new GaussianFilter(new Vector2f(xw, yw), sigma);
    }
}

// MitchellFilter Definition
public sealed class MitchellFilter : Filter
{
    // MitchellFilter Private Members
    private readonly Vector2f radius;
    private readonly Float b, c;
    private readonly FilterSampler sampler;

    // MitchellFilter Public Methods
    public MitchellFilter(Vector2f radius, Float b = 1f / 3f, Float c = 1f / 3f)
    {
        this.radius = radius;
        this.b = b;
        this.c = c;
        sampler = new FilterSampler(this);
    }

    public override Vector2f Radius() => radius;

    public override Float Evaluate(Point2f p) => Mitchell1D(2 * p.x / radius.x) * Mitchell1D(2 * p.y / radius.y);

    public override FilterSample Sample(Point2f u) => sampler.Sample(u);

    public override Float Integral() => radius.x * radius.y / 4;

    // MitchellFilter Private Methods
    private Float Mitchell1D(Float x)
    {
        x = FMath.Abs(x);
        if (x <= 1)
            return ((12 - 9 * b - 6 * c) * x * x * x + (-18 + 12 * b + 6 * c) * x * x + (6 - 2 * b)) * (1f / 6f);
        else if (x <= 2)
            return ((-b - 6 * c) * x * x * x + (6 * b + 30 * c) * x * x + (-12 * b - 48 * c) * x +
                    (8 * b + 24 * c)) *
                   (1f / 6f);
        else
            return 0;
    }

    // Mitchell Filter Method Definitions
    public override string ToString() =>
        $"[ MitchellFilter radius: {radius} b: {FloatToString(b)} c: {FloatToString(c)} sampler: {sampler} ]";

    public static MitchellFilter Create(ParameterDictionary parameters, FileLoc? loc)
    {
        // Find common filter parameters
        Float xw = parameters.GetOneFloat("xradius", 2f);
        Float yw = parameters.GetOneFloat("yradius", 2f);
        Float B = parameters.GetOneFloat("B", 1f / 3f);
        Float C = parameters.GetOneFloat("C", 1f / 3f);
        return new MitchellFilter(new Vector2f(xw, yw), B, C);
    }
}

// LanczosSincFilter Definition
public sealed class LanczosSincFilter : Filter
{
    // LanczosSincFilter Private Members
    private readonly Vector2f radius;
    private readonly Float tau;
    private readonly FilterSampler sampler;

    // LanczosSincFilter Public Methods
    public LanczosSincFilter(Vector2f radius, Float tau = 3f)
    {
        this.radius = radius;
        this.tau = tau;
        sampler = new FilterSampler(this);
    }

    public override Vector2f Radius() => radius;

    public override Float Evaluate(Point2f p) => WindowedSinc(p.x, radius.x, tau) * WindowedSinc(p.y, radius.y, tau);

    public override FilterSample Sample(Point2f u) => sampler.Sample(u);

    // Sinc Filter Method Definitions
    public override Float Integral()
    {
        Float sum = 0;
        int sqrtSamples = 64;
        int nSamples = sqrtSamples * sqrtSamples;
        Float area = 2 * radius.x * 2 * radius.y;
        RNG rng = new();
        for (int y = 0; y < sqrtSamples; ++y)
        {
            for (int x = 0; x < sqrtSamples; ++x)
            {
                // (The order of evaluation of pbrt's Point2f constructor
                // arguments is unspecified; GCC draws the y sample first.)
                Float uy = (y + rng.Uniform<Float>()) / sqrtSamples;
                Float ux = (x + rng.Uniform<Float>()) / sqrtSamples;
                Point2f p = new(Lerp(ux, -radius.x, radius.x), Lerp(uy, -radius.y, radius.y));
                sum += Evaluate(p);
            }
        }
        return sum / nSamples * area;
    }

    public override string ToString() =>
        $"[ LanczosSincFilter radius: {radius} tau: {FloatToString(tau)} sampler: {sampler} ]";

    public static LanczosSincFilter Create(ParameterDictionary parameters, FileLoc? loc)
    {
        Float xw = parameters.GetOneFloat("xradius", 4f);
        Float yw = parameters.GetOneFloat("yradius", 4f);
        Float tau = parameters.GetOneFloat("tau", 3f);
        return new LanczosSincFilter(new Vector2f(xw, yw), tau);
    }
}

// TriangleFilter Definition
public sealed class TriangleFilter : Filter
{
    private readonly Vector2f radius;

    // TriangleFilter Public Methods
    public TriangleFilter(Vector2f radius) => this.radius = radius;

    public override Vector2f Radius() => radius;

    public override Float Evaluate(Point2f p) =>
        FMath.Max(0, radius.x - FMath.Abs(p.x)) * FMath.Max(0, radius.y - FMath.Abs(p.y));

    public override FilterSample Sample(Point2f u) =>
        new(new Point2f(SampleTent(u[0], radius.x), SampleTent(u[1], radius.y)), 1);

    public override Float Integral() => Sqr(radius.x) * Sqr(radius.y);

    // Triangle Filter Method Definitions
    public override string ToString() => $"[ TriangleFilter radius: {radius} ]";

    public static TriangleFilter Create(ParameterDictionary parameters, FileLoc? loc)
    {
        // Find common filter parameters
        Float xw = parameters.GetOneFloat("xradius", 2f);
        Float yw = parameters.GetOneFloat("yradius", 2f);
        return new TriangleFilter(new Vector2f(xw, yw));
    }
}

public abstract partial class Filter
{
    public static Filter Create(string name, ParameterDictionary parameters, FileLoc? loc)
    {
        Filter? filter = null;
        if (name == "box")
            filter = BoxFilter.Create(parameters, loc);
        else if (name == "gaussian")
            filter = GaussianFilter.Create(parameters, loc);
        else if (name == "mitchell")
            filter = MitchellFilter.Create(parameters, loc);
        else if (name == "sinc")
            filter = LanczosSincFilter.Create(parameters, loc);
        else if (name == "triangle")
            filter = TriangleFilter.Create(parameters, loc);
        else
            ErrorExit(loc, $"{name}: filter type unknown.");

        if (filter == null)
            ErrorExit(loc, $"{name}: unable to create filter.");

        parameters.ReportUnused();
        return filter!;
    }
}

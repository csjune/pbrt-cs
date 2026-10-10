// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/bsdfs_test.cpp (the tests disabled with #if 0 there
// are left out). .NET has no std::lgamma, so LGamma() below uses a Lanczos
// approximation; it only feeds the chi^2 test's p-value. The tabulated
// chi^2 frequencies are computed with ParallelFor over the theta rows to
// keep the tests' running time reasonable; the result doesn't depend on
// the order.

namespace Pbrt.Tests;

public class BsdfsTests
{
    /* The null hypothesis will be rejected when the associated
       p-value is below the significance level specified here. */
    private const double CHI2_SLEVEL = 0.01;

    /* Resolution of the frequency table discretization. The azimuthal
       resolution is twice this value. */
    private const int CHI2_THETA_RES = 80;
    private const int CHI2_PHI_RES = 2 * CHI2_THETA_RES;

    /* Number of MC samples to compute the observed frequency table */
    private const int CHI2_SAMPLECOUNT = 1000000;

    /* Minimum expected bin frequency. The chi^2 test does not
       work reliably when the expected frequency in a cell is
       low (e.g. less than 5), because normality assumptions
       break down in this case. Therefore, the implementation
       will merge such low-frequency cells when they fall below
       the threshold specified here. */
    private const int CHI2_MINFREQ = 5;

    /* Each provided BSDF will be tested for a few different
       incident directions. The value specified here determines
       how many tests will be executed per BSDF */
    private const int CHI2_RUNS = 5;

    // log(Gamma(x)) (Lanczos approximation, g = 7, n = 9)
    private static double LGamma(double x)
    {
        if (x < 0.5)
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LGamma(1 - x);
        double[] c =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
            1.5056327351493116e-7
        ];
        x -= 1;
        double a = c[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; ++i)
            a += c[i] / (x + i);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// Regularized lower incomplete gamma function (based on code from Cephes)
    private static double RLGamma(double a, double x)
    {
        const double epsilon = 0.000000000000001;
        const double big = 4503599627370496.0;
        const double bigInv = 2.22044604925031308085e-16;
        if (a < 0 || x < 0)
            throw new ArgumentException("LLGamma: invalid arguments range!");

        if (x == 0)
            return 0.0f;

        double ax = (a * Math.Log(x)) - x - LGamma(a);
        if (ax < -709.78271289338399)
            return a < x ? 1.0 : 0.0;

        if (x <= 1 || x <= a)
        {
            double r2 = a;
            double c2 = 1;
            double ans2 = 1;

            do
            {
                r2 = r2 + 1;
                c2 = c2 * x / r2;
                ans2 += c2;
            } while ((c2 / ans2) > epsilon);

            return Math.Exp(ax) * ans2 / a;
        }

        int c = 0;
        double y = 1 - a;
        double z = x + y + 1;
        double p3 = 1;
        double q3 = x;
        double p2 = x + 1;
        double q2 = z * x;
        double ans = p2 / q2;
        double error;

        do
        {
            c++;
            y += 1;
            z += 2;
            double yc = y * c;
            double p = (p2 * z) - (p3 * yc);
            double q = (q2 * z) - (q3 * yc);

            if (q != 0)
            {
                double nextans = p / q;
                error = Math.Abs((ans - nextans) / nextans);
                ans = nextans;
            }
            else
            {
                // zero div, skip
                error = 1;
            }

            // shift
            p3 = p2;
            p2 = p;
            q3 = q2;
            q2 = q;

            // normalize fraction when the numerator becomes large
            if (Math.Abs(p) > big)
            {
                p3 *= bigInv;
                p2 *= bigInv;
                q3 *= bigInv;
                q2 *= bigInv;
            }
        } while (error > epsilon);

        return 1.0 - (Math.Exp(ax) * ans);
    }

    /// Chi^2 distribution cumulative distribution function
    private static double Chi2CDF(double x, int dof)
    {
        if (dof < 1 || x < 0)
            return 0.0;
        else if (dof == 2)
            return 1.0 - Math.Exp(-0.5 * x);
        else
            return (Float)RLGamma(0.5 * dof, 0.5 * x);
    }

    /// Adaptive Simpson integration over an 1D interval
    private static Float AdaptiveSimpson(Func<Float, Float> f, Float x0, Float x1, Float eps = 1e-6f, int depth = 6)
    {
        /* Define an recursive function for integration over subintervals */
        Float Integrate(Float a, Float b, Float c, Float fa, Float fb, Float fc, Float I, Float eps, int depth)
        {
            /* Evaluate the function at two intermediate points */
            Float d = 0.5f * (a + b), e = 0.5f * (b + c), fd = f(d), fe = f(e);

            /* Simpson integration over each subinterval */
            Float h = c - a, I0 = (Float)(1.0 / 12.0) * h * (fa + 4 * fd + fb),
                  I1 = (Float)(1.0 / 12.0) * h * (fb + 4 * fe + fc), Ip = I0 + I1;

            /* Stopping criterion from J.N. Lyness (1969)
              "Notes on the adaptive Simpson quadrature routine" */
            if (depth <= 0 || FMath.Abs(Ip - I) < 15 * eps)
            {
                // Richardson extrapolation
                return Ip + (Float)(1.0 / 15.0) * (Ip - I);
            }

            return Integrate(a, d, b, fa, fd, fb, I0, .5f * eps, depth - 1) +
                   Integrate(b, e, c, fb, fe, fc, I1, .5f * eps, depth - 1);
        }

        Float a = x0, b = 0.5f * (x0 + x1), c = x1;
        Float fa = f(a), fb = f(b), fc = f(c);
        Float I = (c - a) * (Float)(1.0 / 6.0) * (fa + 4 * fb + fc);
        return Integrate(a, b, c, fa, fb, fc, I, eps, depth);
    }

    /// Nested adaptive Simpson integration over a 2D rectangle
    private static Float AdaptiveSimpson2D(Func<Float, Float, Float> f, Float x0, Float y0, Float x1, Float y1,
                                           Float eps = 1e-6f, int depth = 6)
    {
        /* Lambda function that integrates over the X axis */
        Float Integrate(Float y) => AdaptiveSimpson(x => f(x, y), x0, x1, eps, depth);
        Float value = AdaptiveSimpson(Integrate, y0, y1, eps, depth);
        return value;
    }

    /// Generate a histogram of the BSDF density function via MC sampling
    private static void FrequencyTable(in BSDF bsdf, Vector3f wo, ref RNG rng, int sampleCount, int thetaRes,
                                       int phiRes, Float[] target)
    {
        Array.Clear(target);

        Float factorTheta = thetaRes / Pi, factorPhi = phiRes / (2 * Pi);

        for (int i = 0; i < sampleCount; ++i)
        {
            Float u = rng.Uniform<Float>();
            Float s0 = rng.Uniform<Float>();
            Float s1 = rng.Uniform<Float>();
            BSDFSample? obs = bsdf.Sample_f(wo, u, new Point2f(s0, s1));

            if (obs is not { } bs || bs.IsSpecular())
                continue;

            Vector3f wiL = bsdf.RenderToLocal(bs.wi);

            Point2f coords = new(SafeACos(wiL.z) * factorTheta, FMath.Atan2(wiL.y, wiL.x) * factorPhi);

            if (coords.y < 0)
                coords.y += 2 * Pi * factorPhi;

            int thetaBin = Math.Min(Math.Max(0, (int)FMath.Floor(coords.x)), thetaRes - 1);
            int phiBin = Math.Min(Math.Max(0, (int)FMath.Floor(coords.y)), phiRes - 1);

            target[thetaBin * phiRes + phiBin] += 1;
        }
    }

    // Numerically integrate the probability density function over rectangles in
    // spherical coordinates.
    private static void IntegrateFrequencyTable(BSDF bsdf, Vector3f wo, int sampleCount, int thetaRes, int phiRes,
                                                Float[] target)
    {
        Array.Clear(target);

        Float factorTheta = Pi / thetaRes, factorPhi = (2 * Pi) / phiRes;

        ParallelFor(0, thetaRes, (long il) =>
        {
            int i = (int)il;
            for (int j = 0; j < phiRes; ++j)
            {
                target[i * phiRes + j] =
                    sampleCount *
                    AdaptiveSimpson2D((theta, phi) =>
                    {
                        Float cosTheta = FMath.Cos(theta), sinTheta = FMath.Sin(theta);
                        Float cosPhi = FMath.Cos(phi), sinPhi = FMath.Sin(phi);
                        Vector3f wiL = new(sinTheta * cosPhi, sinTheta * sinPhi, cosTheta);
                        return bsdf.PDF(wo, bsdf.LocalToRender(wiL)) * sinTheta;
                    }, i * factorTheta, j * factorPhi, (i + 1) * factorTheta, (j + 1) * factorPhi);
            }
        });
    }

    /// Run A Chi^2 test based on the given frequency tables
    private static (bool, string) Chi2Test(Float[] frequencies, Float[] expFrequencies, int thetaRes, int phiRes,
                                           int sampleCount, Float minExpFrequency, Float significanceLevel,
                                           int numTests)
    {
        /* Sort all cells by their expected frequencies */
        (Float expFrequency, int index)[] cells = new (Float, int)[thetaRes * phiRes];
        for (int i = 0; i < cells.Length; ++i)
            cells[i] = (expFrequencies[i], i);
        Array.Sort(cells, (a, b) => a.expFrequency.CompareTo(b.expFrequency));

        /* Compute the Chi^2 statistic and pool cells as necessary */
        Float pooledFrequencies = 0, pooledExpFrequencies = 0, chsq = 0;
        int pooledCells = 0, dof = 0;

        foreach (var c in cells)
        {
            if (expFrequencies[c.index] == 0)
            {
                if (frequencies[c.index] > sampleCount * 1e-5f)
                {
                    /* Uh oh: samples in a c that should be completely empty
                       according to the probability density function. Ordinarily,
                       even a single sample requires immediate rejection of the null
                       hypothesis. But due to finite-precision computations and
                       rounding
                       errors, this can occasionally happen without there being an
                       actual bug. Therefore, the criterion here is a bit more
                       lenient. */
                    return (false, $"Encountered {frequencies[c.index]} samples in a c with expected " +
                                   "frequency 0. Rejecting the null hypothesis!");
                }
            }
            else if (expFrequencies[c.index] < minExpFrequency)
            {
                /* Pool cells with low expected frequencies */
                pooledFrequencies += frequencies[c.index];
                pooledExpFrequencies += expFrequencies[c.index];
                pooledCells++;
            }
            else if (pooledExpFrequencies > 0 && pooledExpFrequencies < minExpFrequency)
            {
                /* Keep on pooling cells until a sufficiently high
                   expected frequency is achieved. */
                pooledFrequencies += frequencies[c.index];
                pooledExpFrequencies += expFrequencies[c.index];
                pooledCells++;
            }
            else
            {
                Float diff = frequencies[c.index] - expFrequencies[c.index];
                chsq += (diff * diff) / expFrequencies[c.index];
                ++dof;
            }
        }

        if (pooledExpFrequencies > 0 || pooledFrequencies > 0)
        {
            Float diff = pooledFrequencies - pooledExpFrequencies;
            chsq += (diff * diff) / pooledExpFrequencies;
            ++dof;
        }

        /* All parameters are assumed to be known, so there is no
           additional DF reduction due to model parameters */
        dof -= 1;

        if (dof <= 0)
            return (false, $"The number of degrees of freedom {dof} is too low!");

        /* Probability of obtaining a test statistic at least
           as extreme as the one observed under the assumption
           that the distributions match */
        Float pval = 1 - (Float)Chi2CDF(chsq, dof);

        /* Apply the Sidak correction term, since we'll be conducting multiple
           independent
           hypothesis tests. This accounts for the fact that the probability of a
           failure
           increases quickly when several hypothesis tests are run in sequence. */
        Float alpha = 1.0f - FMath.Pow(1.0f - significanceLevel, 1.0f / numTests);

        if (pval < alpha || !Float.IsFinite(pval))
            return (false, $"Rejected the null hypothesis (p-value = {pval}, significance level = {alpha}");
        else
            return (true, "");
    }

    // A SurfaceInteraction on a disk, as pbrt's tests make one.
    private static SurfaceInteraction DiskInteraction()
    {
        Transform t = RotateX(-90);
        Transform tInv = Inverse(t);
        bool reverseOrientation = false;
        Disk disk = new(t, tInv, reverseOrientation, 0f, 1f, 0, 360f);
        Point3f origin = new(0.1f, 1, 0);  // offset slightly so we don't hit center of disk
        Vector3f direction = new(0, -1, 0);
        Ray r = new(origin, direction);
        ShapeIntersection? si = disk.Intersect(r);
        Assert.True(si.HasValue);
        return si!.Value.intr;
    }

    private static void TestBSDF(Func<SurfaceInteraction, BSDF> createBSDF, string description)
    {
        const int thetaRes = CHI2_THETA_RES;
        const int phiRes = CHI2_PHI_RES;
        const int sampleCount = CHI2_SAMPLECOUNT;
        Float[] frequencies = new Float[thetaRes * phiRes];
        Float[] expFrequencies = new Float[thetaRes * phiRes];
        RNG rng = new();

        // Create BSDF, which requires creating a Shape, casting a Ray that
        // hits the shape to get a SurfaceInteraction object.
        BSDF bsdf = createBSDF(DiskInteraction());

        for (int k = 0; k < CHI2_RUNS; ++k)
        {
            /* Randomly pick an outgoing direction on the hemisphere */
            Float s0 = rng.Uniform<Float>();
            Float s1 = rng.Uniform<Float>();
            Vector3f woL = SampleCosineHemisphere(new Point2f(s0, s1));
            Vector3f wo = bsdf.LocalToRender(woL);

            FrequencyTable(bsdf, wo, ref rng, sampleCount, thetaRes, phiRes, frequencies);

            IntegrateFrequencyTable(bsdf, wo, sampleCount, thetaRes, phiRes, expFrequencies);

            var (ok, message) = Chi2Test(frequencies, expFrequencies, thetaRes, phiRes, sampleCount, CHI2_MINFREQ,
                                         (Float)CHI2_SLEVEL, CHI2_RUNS);
            Assert.True(ok, $"{description}: {message}, iteration {k}");
        }
    }

    private static BSDF CreateLambertian(SurfaceInteraction si)
    {
        SampledSpectrum Kd = new(1f);
        return new BSDF(si.shading.n, si.shading.dpdu, new DiffuseBxDF(Kd));
    }

    [Fact]
    public void BSDFSampling_Lambertian() => TestBSDF(CreateLambertian, "Lambertian");

    private static BSDF CreateConductorMicrofacet(SurfaceInteraction si, float eta, float k, float roughx, float roughy)
    {
        Float alphax = TrowbridgeReitzDistribution.RoughnessToAlpha(roughx);
        Float alphay = TrowbridgeReitzDistribution.RoughnessToAlpha(roughy);
        TrowbridgeReitzDistribution distrib = new(alphax, alphay);
        return new BSDF(si.shading.n, si.shading.dpdu,
                        new ConductorBxDF(distrib, new SampledSpectrum(eta), new SampledSpectrum(k)));
    }

    private static BSDF CreateDielectricMicrofacet(SurfaceInteraction si, float ior, float roughx, float roughy)
    {
        Float alphax = TrowbridgeReitzDistribution.RoughnessToAlpha(roughx);
        Float alphay = TrowbridgeReitzDistribution.RoughnessToAlpha(roughy);
        TrowbridgeReitzDistribution distrib = new(alphax, alphay);
        return new BSDF(si.shading.n, si.shading.dpdu, new DielectricBxDF(ior, distrib));
    }

    [Fact]
    public void BSDFSampling_TRCondIso() =>
        TestBSDF(si => CreateConductorMicrofacet(si, 2f, 4f, 0.5f, 0.5f), "TRCondIso");

    [Fact]
    public void BSDFSampling_TRCondAniso() =>
        TestBSDF(si => CreateConductorMicrofacet(si, 2f, 4f, 0.3f, 0.15f), "TRCondAniso");

    [Fact]
    public void BSDFSampling_TRDielIso() => TestBSDF(si => CreateDielectricMicrofacet(si, 1.5f, 0.5f, 0.5f), "TRDielIso");

    [Fact]
    public void BSDFSampling_TRDielAniso() =>
        TestBSDF(si => CreateDielectricMicrofacet(si, 1.5f, 0.3f, 0.15f), "TRDielAniso");

    [Fact]
    public void BSDFSampling_TRDielIsoInv() =>
        TestBSDF(si => CreateDielectricMicrofacet(si, 1 / 1.5f, 0.5f, 0.5f), "TRDielIsoInv");

    [Fact]
    public void BSDFSampling_TRDielAnisoInv() =>
        TestBSDF(si => CreateDielectricMicrofacet(si, 1 / 1.5f, 0.3f, 0.15f), "TRDielAnisoInv");

    ///////////////////////////////////////////////////////////////////////////
    // Energy Conservation Tests

    private static void TestEnergyConservation(Func<SurfaceInteraction, BSDF> createBSDF, string description)
    {
        RNG rng = new();

        // Create BSDF, which requires creating a Shape, casting a Ray that
        // hits the shape to get a SurfaceInteraction object.
        SurfaceInteraction si = DiskInteraction();
        BSDF bsdf = createBSDF(si);

        for (int i = 0; i < 10; ++i)
        {
            Float uo0 = rng.Uniform<Float>();
            Float uo1 = rng.Uniform<Float>();
            Vector3f woL = SampleUniformHemisphere(new Point2f(uo0, uo1));
            Vector3f wo = bsdf.LocalToRender(woL);

            const int nSamples = 16384;
            SampledSpectrum Lo = new(0f);
            for (int j = 0; j < nSamples; ++j)
            {
                Float u = rng.Uniform<Float>();
                Float ui0 = rng.Uniform<Float>();
                Float ui1 = rng.Uniform<Float>();
                BSDFSample? obs = bsdf.Sample_f(wo, u, new Point2f(ui0, ui1));
                if (obs is { } bs)
                    Lo += bs.f * AbsDot(bs.wi, si.n) / bs.pdf;
            }
            Lo /= nSamples;

            Assert.True(Lo.MaxComponentValue() < 1.01, $"{description}: Lo = {Lo}, wo = {wo}");
        }
    }

    [Fact]
    public void BSDFEnergyConservation_LambertianReflection() =>
        TestEnergyConservation(si => new BSDF(si.shading.n, si.shading.dpdu, new DiffuseBxDF(new SampledSpectrum(1f))),
                               "LambertianReflection");

    // Hair Tests

    [Fact]
    public void Hair_WhiteFurnace()
    {
        RNG rng = new();
        Float s0 = rng.Uniform<Float>();
        Float s1 = rng.Uniform<Float>();
        Vector3f wo = SampleUniformSphere(new Point2f(s0, s1));
        for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
        {
            for (Float beta_n = .1f; beta_n < 1; beta_n += .2f)
            {
                // Estimate reflected uniform incident radiance from hair
                Float ySum = 0;

                // More samples for the smooth case, since we're sampling blindly.
                int count = (beta_m < .5 || beta_n < .5) ? 100000 : 20000;

                for (int i = 0; i < count; ++i)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(RadicalInverse(0, (ulong)i));

                    Float h = (Float)Math.Clamp(-1 + 2.0 * RadicalInverse(1, (ulong)i), -.999999, .999999);
                    SampledSpectrum sigma_a = new(0f);
                    HairBxDF hair = new(h, 1.55f, sigma_a, beta_m, beta_n, 0f);
                    Vector3f wi = SampleUniformSphere(new Point2f(RadicalInverse(2, (ulong)i), RadicalInverse(3, (ulong)i)));

                    SampledSpectrum f = hair.f(wo, wi, TransportMode.Radiance) * AbsCosTheta(wi);
                    ySum += f.y(lambda);
                }

                Float avg = ySum / (count * UniformSpherePDF());
                Assert.True(avg >= .95 && avg <= 1.05, $"{avg}");
            }
        }
    }

    [Fact]
    public void Hair_HOnTheEdge()
    {
        Vector3f wo = new(0.54986966f, 0.03359017f, 0.83457476f), wi = new(-0.37383357f, -0.91920084f, 0.12376696f);
        Float h = -1, beta_m = .1f, beta_n = .1f;
        SampledSpectrum sigma_a = new(0f);
        HairBxDF hair = new(h, 1.55f, sigma_a, beta_m, beta_n, 0f);

        SampledSpectrum f = hair.f(wo, wi, TransportMode.Radiance);
        Assert.False(f.HasNaNs());
    }

    [Fact]
    public void Hair_WhiteFurnaceSampled()
    {
        RNG rng = new();
        Float s0 = rng.Uniform<Float>();
        Float s1 = rng.Uniform<Float>();
        Vector3f wo = SampleUniformSphere(new Point2f(s0, s1));
        for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
        {
            for (Float beta_n = .1f; beta_n < 1; beta_n += .2f)
            {
                Float ySum = 0;

                int count = 10000;
                for (int i = 0; i < count; ++i)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(RadicalInverse(0, (ulong)i));
                    Float h = (Float)Math.Clamp(-1 + 2.0 * RadicalInverse(1, (ulong)i), -.999999, .999999);

                    SampledSpectrum sigma_a = new(0f);
                    HairBxDF hair = new(h, 1.55f, sigma_a, beta_m, beta_n, 0f);

                    Float uc = RadicalInverse(2, (ulong)i);
                    Point2f u = new(RadicalInverse(3, (ulong)i), RadicalInverse(4, (ulong)i));

                    BSDFSample? obs = hair.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (obs is { } bs)
                    {
                        SampledSpectrum f = bs.f * AbsCosTheta(bs.wi) / bs.pdf;
                        ySum += f.y(lambda);
                    }
                }

                Float avg = ySum / count;
                Assert.True(avg >= .99 && avg <= 1.01, $"{avg}");
            }
        }
    }

    [Fact]
    public void Hair_SamplingWeights()
    {
        for (Float beta_m = .1f; beta_m < 1; beta_m += .2f)
            for (Float beta_n = .4f; beta_n < 1; beta_n += .2f)
            {
                int count = 10000;
                for (int i = 0; i < count; ++i)
                {
                    Float h = (Float)Math.Clamp(-1 + 2.0 * RadicalInverse(0, (ulong)i), -.999999, .999999);

                    // Check _HairBxDF::Sample\_f()_ sample weight
                    SampledSpectrum sigma_a = new(0f);
                    HairBxDF hair = new(h, 1.55f, sigma_a, beta_m, beta_n, 0f);

                    Vector3f wo = SampleUniformSphere(new Point2f(RadicalInverse(1, (ulong)i), RadicalInverse(2, (ulong)i)));
                    Float uc = RadicalInverse(3, (ulong)i);
                    Point2f u = new(RadicalInverse(4, (ulong)i), RadicalInverse(5, (ulong)i));
                    BSDFSample? obs = hair.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (obs is { } bs)
                    {
                        Float sum = 0;
                        int ny = 20;
                        foreach (Float ul in Stratified1D(ny))
                        {
                            SampledWavelengths lambda = SampledWavelengths.SampleVisible(ul);
                            sum += bs.f.y(lambda) * AbsCosTheta(bs.wi) / bs.pdf;
                        }

                        // Verify that hair BSDF sample weight is close to 1 for
                        // _wi_
                        Float avg = sum / ny;
                        Assert.True(avg > 0.99);
                        Assert.True(avg < 1.01);
                    }
                }
            }
    }

    [Fact]
    public void Hair_SamplingConsistency()
    {
        RNG rng = new();
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        for (Float beta_m = .2f; beta_m < 1; beta_m += .2f)
            for (Float beta_n = .4f; beta_n < 1; beta_n += .2f)
            {
                // Declare variables for hair sampling test
                const int count = 64 * 1024;
                SampledSpectrum sigma_a = new(.25f);
                Float s0 = rng.Uniform<Float>();
                Float s1 = rng.Uniform<Float>();
                Vector3f wo = SampleUniformSphere(new Point2f(s0, s1));
                static SampledSpectrum Li(Vector3f w) => new(w.z * w.z);
                SampledSpectrum fImportance = new(0f), fUniform = new(0f);
                for (int i = 0; i < count; ++i)
                {
                    // Compute estimates of scattered radiance for hair sampling
                    // test
                    Float h = -1 + 2 * rng.Uniform<Float>();
                    HairBxDF hair = new(h, 1.55f, sigma_a, beta_m, beta_n, 0f);
                    Float uc = rng.Uniform<Float>();
                    Float u0 = rng.Uniform<Float>();
                    Float u1 = rng.Uniform<Float>();
                    Point2f u = new(u0, u1);
                    BSDFSample? obs = hair.Sample_f(wo, uc, u, TransportMode.Radiance, BxDFReflTransFlags.All);
                    if (obs is { } bs)
                        fImportance += bs.f * Li(bs.wi) * AbsCosTheta(bs.wi) / (count * bs.pdf);
                    Vector3f wi = SampleUniformSphere(u);
                    fUniform += hair.f(wo, wi, TransportMode.Radiance) * Li(wi) * AbsCosTheta(wi) /
                                (count * UniformSpherePDF());
                }
                // Verify consistency of estimated hair reflected radiance values
                Float err = FMath.Abs(fImportance.y(lambda) - fUniform.y(lambda)) / fUniform.y(lambda);
                Assert.True(err < 0.05);
            }
    }
}

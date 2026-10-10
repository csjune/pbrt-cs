// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the FunctionIntegrator parts of src/pbrt/cpu/integrators.h and
// integrators.cpp
//
// As in pbrt, Create() stores the functions as returning Float, so their
// double values are rounded to Float.

namespace Pbrt;

// FunctionIntegrator Definition
public sealed class FunctionIntegrator(Func<Point2f, double> func, string outputFilename, Camera camera,
                                       Sampler sampler, bool skipBad, string imageFilename)
    : Integrator(null, [])
{
    private readonly Sampler baseSampler = sampler;

    private static class Funcs
    {
        public static double Step(Point2f p) => (p.x < 0.5) ? 2 : 0;

        public static double Diagonal(Point2f p) => (p.x + p.y < 1) ? 2 : 0;

        public static double Disk(Point2f p) =>
            Distance(p, new Point2f(0.5f, 0.5f)) < 0.5 ? (1 / ((double)Pi * Sqr(0.5))) : 0;

        public static double Checkerboard(Point2f p)
        {
            int freq = 10;
            Point2i pi = new(p * freq);
            return ((pi.x & 1) ^ (pi.y & 1)) != 0 ? 2 : 0;
        }

        private static readonly double angle = Radians(45);
        private static readonly double sa = Math.Sin(angle), ca = Math.Cos(angle);

        public static double RotatedCheckerboard(Point2f p)
        {
            double nrm = 1.00006866455078125;
            return Checkerboard(new Point2f((Float)(10 + p.x * ca - p.y * sa), (Float)(10 + p.x * sa + p.y * ca))) /
                   nrm;
        }

        private static double Gaussian(double x, double mu = 0, double sigma = 1) =>
            1 / Math.Sqrt(2 * Pi * sigma * sigma) * Math.Exp(-Sqr(x - mu) / (2 * sigma * sigma));

        // pbrt's Sqr(GaussianIntegral(0, 1, mu, sigma)), which uses
        // std::erf(); this is the value it computes.
        private static readonly double gaussianNrm = BitConverter.Int64BitsToDouble(0x3FED277BBD8CBDD3);

        public static double GaussianFunc(Point2f p)
        {
            double mu = 0.5, sigma = 0.25;
            return Gaussian(p.x, mu, sigma) * Gaussian(p.y, mu, sigma) / gaussianNrm;
        }
    }

    public static FunctionIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler,
                                            FileLoc? loc)
    {
        string funcName = parameters.GetOneString("function", "step");
        string defaultOut = Options.imageFile.Length == 0 ? $"{funcName}-mse.txt" : Options.imageFile;
        string outputFilename = parameters.GetOneString("filename", defaultOut);

        Func<Point2f, double>? f = funcName switch
        {
            "step" => Funcs.Step,
            "diagonal" => Funcs.Diagonal,
            "disk" => Funcs.Disk,
            "checkerboard" => Funcs.Checkerboard,
            "rotatedcheckerboard" => Funcs.RotatedCheckerboard,
            "gaussian" => Funcs.GaussianFunc,
            _ => null
        };

        if (f == null)
            ErrorExit(loc, $"{funcName}: function for FunctionIntegrator unknown");

        if (sampler is SobolSampler)
            ErrorExit(loc, "\"sobol\" sampler should be replaced with \"paddedsobol\" for the \"function\" integrator.");

        bool skipBad = parameters.GetOneBool("skipbad", true);
        string imageFilename = parameters.GetOneString("imagefilename", "");

        // (pbrt's std::function<Float(Point2f)>.)
        return new FunctionIntegrator(p => (Float)f!(p), outputFilename, camera, sampler, skipBad, imageFilename);
    }

    public override void Render()
    {
        string result = "";
        Bounds2i pixelBounds = camera.GetFilm().PixelBounds();
        int nPixels = pixelBounds.Area();
        Array2D<double> sumv = new(pixelBounds);
        int nSamples = baseSampler.SamplesPerPixel();

        if (imageFilename.Length > 0)
        {
            RNG irng = new();
            Vector2i res = pixelBounds.pMax - pixelBounds.pMin;
            Image image = new(PixelFormat.Float, new Point2i(res.x, res.y), ["Y"]);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    int nImageSamples = 256;
                    Float sum = 0;
                    for (int i = 0; i < nImageSamples; ++i)
                    {
                        // (GCC evaluates the constructor's arguments right to
                        // left.)
                        Float py = (y + irng.Uniform<Float>()) / res.y;
                        Float px = (x + irng.Uniform<Float>()) / res.x;
                        sum = (Float)(sum + func(new Point2f(px, py)));
                    }
                    image.SetChannel(new Point2i(x, y), 0, sum / nImageSamples);
                }
            image.Write(imageFilename);
        }

        ProgressReporter prog = new(nSamples, "Sampling", Options.quiet);

        bool isHalton = baseSampler is HaltonSampler;
        bool isStratified = baseSampler is StratifiedSampler;
        bool isSobol = baseSampler is PaddedSobolSampler;
        DigitPermutation[][] digitPermutations = [[], []];
        ulong[][] owenHash = [[], []];
        RNG rng = new();
        if (isHalton)
        {
            for (int d = 0; d < 2; ++d)
            {
                digitPermutations[d] = new DigitPermutation[nPixels];
                for (int i = 0; i < nPixels; ++i)
                    digitPermutations[d][i] = new DigitPermutation(d == 0 ? 2 : 3, rng.Uniform<uint>());
            }
        }
        if (isHalton || isSobol)
        {
            for (int d = 0; d < 2; ++d)
            {
                owenHash[d] = new ulong[nPixels];
                for (int i = 0; i < nPixels; ++i)
                    owenHash[d][i] = rng.Uniform<ulong>();
            }
        }

        int nTakenSamples = 0;
        ThreadLocal<Sampler> threadSamplers = new(() => baseSampler.Clone());
        for (int sampleIndex = 0; sampleIndex < nSamples; ++sampleIndex)
        {
            bool reportResult = true;
            if (skipBad)
            {
                int ns = sampleIndex + 1;
                if (isStratified && Sqr((int)Math.Sqrt(ns)) != ns)
                {
                    prog.Update();
                    continue;
                }
                else if (isSobol && !IsPowerOf2(ns))
                    reportResult = false;
                else if (isHalton)
                {
                    int n2 = 0, n3 = 0;
                    while (true)
                    {
                        if ((ns % 2) == 0)
                        {
                            ns /= 2;
                            ++n2;
                        }
                        else if ((ns % 3) == 0)
                        {
                            ns /= 3;
                            ++n3;
                        }
                        else
                            break;
                    }
                    if (ns != 1 || n2 != n3)
                        reportResult = false;
                }
            }

            ++nTakenSamples;

            int si = sampleIndex, nTaken = nTakenSamples;
            if (isStratified)
            {
                int spp = sampleIndex + 1;
                int factor = (int)Math.Sqrt(spp);

                while ((spp % factor) != 0)
                    --factor;

                ParallelFor2D(pixelBounds, (Point2i pPixel) =>
                {
                    int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                     (pPixel.y - pixelBounds.pMin.y) * (pixelBounds.pMax.x - pixelBounds.pMin.x);
                    RNG prng = new((ulong)pixelIndex, MixBits((ulong)(si + 1)));

                    Float v = 0;
                    int nx = factor, ny = spp / factor;
                    for (int x = 0; x < nx; ++x)
                    {
                        for (int y = 0; y < ny; ++y)
                        {
                            Float u0 = (x + prng.Uniform<Float>()) / nx;
                            Point2f u = new(u0, (y + prng.Uniform<Float>()) / ny);
                            v = (Float)(v + func(u));
                        }
                    }
                    // Need nTakenSamples factor to cancel out division in sumSE computation.
                    sumv[pPixel] = v * nTaken / (nx * ny);
                });
            }
            else
            {
                ParallelFor2D(pixelBounds, (Bounds2i bounds) =>
                {
                    foreach (Point2i pPixel in bounds)
                    {
                        Point2f u;
                        if (isHalton)
                        {
                            int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                             (pPixel.y - pixelBounds.pMin.y) *
                                             (pixelBounds.pMax.x - pixelBounds.pMin.x);
                            DCHECK_GE(pixelIndex, 0);
                            DCHECK_LT(pixelIndex, nPixels);

                            switch (((HaltonSampler)baseSampler).GetRandomizeStrategy())
                            {
                                case RandomizeStrategy.None:
                                    u = new Point2f(RadicalInverse(0, (ulong)si), RadicalInverse(1, (ulong)si));
                                    break;
                                case RandomizeStrategy.PermuteDigits:
                                    u = new Point2f(
                                        ScrambledRadicalInverse(0, (ulong)si, digitPermutations[0][pixelIndex]),
                                        ScrambledRadicalInverse(1, (ulong)si, digitPermutations[1][pixelIndex]));
                                    break;
                                case RandomizeStrategy.Owen:
                                    u = new Point2f(
                                        OwenScrambledRadicalInverse(0, (ulong)si, (uint)owenHash[0][pixelIndex]),
                                        OwenScrambledRadicalInverse(1, (ulong)si, (uint)owenHash[1][pixelIndex]));
                                    break;
                                default:
                                    LOG_FATAL("Unhandled randomization strategy");
                                    u = new Point2f();
                                    break;
                            }
                        }
                        else if (isSobol)
                        {
                            int pixelIndex = (pPixel.x - pixelBounds.pMin.x) +
                                             (pPixel.y - pixelBounds.pMin.y) *
                                             (pixelBounds.pMax.x - pixelBounds.pMin.x);
                            DCHECK_GE(pixelIndex, 0);
                            DCHECK_LT(pixelIndex, nPixels);

                            switch (((PaddedSobolSampler)baseSampler).GetRandomizeStrategy())
                            {
                                case RandomizeStrategy.None:
                                    u = new Point2f(SobolSample(si, 0, new NoRandomizer()),
                                                    SobolSample(si, 1, new NoRandomizer()));
                                    break;
                                case RandomizeStrategy.PermuteDigits:
                                    u = new Point2f(
                                        SobolSample(si, 0, new BinaryPermuteScrambler((uint)owenHash[0][pixelIndex])),
                                        SobolSample(si, 1, new BinaryPermuteScrambler((uint)owenHash[1][pixelIndex])));
                                    break;
                                case RandomizeStrategy.FastOwen:
                                    u = new Point2f(
                                        SobolSample(si, 0, new FastOwenScrambler((uint)owenHash[0][pixelIndex])),
                                        SobolSample(si, 1, new FastOwenScrambler((uint)owenHash[1][pixelIndex])));
                                    break;
                                case RandomizeStrategy.Owen:
                                    u = new Point2f(
                                        SobolSample(si, 0, new OwenScrambler((uint)owenHash[0][pixelIndex])),
                                        SobolSample(si, 1, new OwenScrambler((uint)owenHash[1][pixelIndex])));
                                    break;
                                default:
                                    LOG_FATAL("Unhandled randomization strategy");
                                    u = new Point2f();
                                    break;
                            }
                        }
                        else
                        {
                            Sampler sampler = threadSamplers.Value!;
                            sampler.StartPixelSample(pPixel, si, 0);
                            u = sampler.GetPixel2D();
                        }
                        sumv[pPixel] += func(u);
                    }
                });
            }

            // Compute average MSE/variance
            if (reportResult)
            {
                double sumSE = 0;
                foreach (Point2i p in pixelBounds)
                    sumSE += Sqr(sumv[p] / nTakenSamples - 1);
                Float mse = (Float)(sumSE / nPixels);

                // (pbrt's StringPrintf() prints %f with the shortest
                // representation.)
                result += $"{sampleIndex + 1} {FloatToString(mse)}\n";
            }
            prog.Update();
        }

        // Make sure that it's basically one...
        double total = 0;
        foreach (Point2i p in pixelBounds)
            total += sumv[p];
        double avg = total / ((double)nPixels * (double)nTakenSamples);
        if (avg < 0.999 || avg > 1.001)
            Warning($"Average estimate is {DoubleToString(avg)}, which is suspiciously far from 1.");

        prog.Done();

        WriteFileContents(outputFilename, result);
    }

    public override string ToString() =>
        $"[ FunctionIntegrator outputFilename: {outputFilename} camera: {camera} baseSampler: {baseSampler} ]";
}

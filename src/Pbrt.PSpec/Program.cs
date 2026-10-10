// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cmd/pspec.cpp (the CPU path; the GPU power spectrum
// update isn't ported).

// Computes power spectra of a variety point sets used by pbrt's samplers.

using System.Globalization;
using System.Text;

namespace Pbrt.PSpec;

public static class Program
{
    private static void usage(string? msg = null)
    {
        Console.Error.Write("\n");
        if (!string.IsNullOrEmpty(msg))
            Console.Error.Write($"pspec: {msg}\n\n");

        Console.Error.Write("""
            usage: pspec <sampler> [<options...>]

            Where <sampler> is one of:
                cwd.pts:         Each file named "pts-*" in the current directory is read to
                                 find the sample values for a single point set. Files are
                                 plain text and should just be whitespace-separated sample
                                 values between 0 and 1.
                grid:            A regular grid of sample points, with floor(sqrt(npoints))
                                 samples.
                halton:          The first two dimensions of the Halton sequence.
                halton.owen:     The first two dimensions of the Halton sequence, randomized
                                 with Owen scrambling.
                halton.permutedigits:
                                 The first two dimensions of the Halton sequence, randomized
                                 using random digit permutations.
                independent:     Independent uniform random samples.
                lhs:             Latin hypercube sampling.
                pmj02bn:         Progressive multi-jittered (0,2) blue noise points. (Note:
                                 pbrt uses precomputed tables for these and only has five,
                                 so nsets > 5 does not make sense in this case.)
                sobol:           The first two dimensions of the Sobol' sequence.
                sobol.fastowen:  The first two dimensions of the Sobol' sequence, randomized
                                 using a fast hashing approach that operates on all bits in
                                 parallel.
                sobol.owen:      The first two dimensions of the Sobol' sequence, randomized
                                 using Owen scrambling.
                sobol.permutedigits:
                                 The first two dimensions of the Sobol' sequence, randomized
                                 with bitwise permutations.
                sobol.z:         Randomized Morton z-curve Sobol' corresponding to the
                                 ZSobolSampler.
                stdin.binary:    Sample values are read from standard input as binary 32-bit
                                 floats. Multiple point sets may be provided by providing
                                 successive point sets one after the previous.
                stdin.dat:       Sample values are read from standard input as plain text
                                 numbers.  Multiple point sets may be provided by separating
                                 them with a '#' character.
                stratified:      A grid of stratified sample points, with floor(sqrt(npoints))
                                 in each dimension.

            Options:
              --npoints <n>        Number of sample points to generate in each set.
                                   (Default: 1024).
              --nsets <n>          Number of independent sets of sample points.
                                   (Default: 4).
              --outbase <name>     Base filename to use for saving output.  The power
                                   spectrum is saved in a filewith the given name and an
                                   ".exr" suffix, and the power spectrum is saved with an
                                   ".txt" suffix.
                                   (Default: <sampler>-<npoints>pts-<nsets>sets.)
              --resolution <res>   Resolution of image for power spectrum. (Default: 1500).


            """);
    }

    // A character stream with one character of pushback, enough for
    // scanf("%f %f") and getchar().
    private sealed class CharReader(Stream s)
    {
        private readonly BufferedStream stream = new(s, 1 << 16);
        private int pushback = -1;

        public int Get()
        {
            if (pushback >= 0)
            {
                int c = pushback;
                pushback = -1;
                return c;
            }
            return stream.ReadByte();
        }

        public void Unget(int c) => pushback = c;

        public void Close() => stream.Dispose();

        public int Peek()
        {
            int c = Get();
            Unget(c);
            return c;
        }

        private static bool IsSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

        // One "%f" conversion: skips leading whitespace and reads the longest
        // prefix that is a decimal floating-point number (or inf/nan). Returns
        // -1 at EOF before any characters, 0 on a matching failure.
        private int ScanFloat(out float v)
        {
            v = 0;
            int c;
            do
                c = Get();
            while (IsSpace(c));
            if (c < 0)
                return -1;

            StringBuilder sb = new();
            if (c == '+' || c == '-')
            {
                sb.Append((char)c);
                c = Get();
            }
            if (c == 'i' || c == 'I' || c == 'n' || c == 'N')
            {
                string word = c == 'i' || c == 'I' ? "inf" : "nan";
                foreach (char w in word)
                {
                    if (char.ToLowerInvariant((char)c) != w)
                    {
                        Unget(c);
                        return 0;
                    }
                    c = Get();
                }
                if (word == "inf")
                {
                    // Optional "inity"
                    foreach (char w in "inity")
                    {
                        if (char.ToLowerInvariant((char)c) != w)
                            break;
                        c = Get();
                    }
                }
                Unget(c);
                v = word == "inf" ? (sb.ToString() == "-" ? float.NegativeInfinity : float.PositiveInfinity)
                                  : float.NaN;
                return 1;
            }
            int nDigits = 0;
            while (c >= '0' && c <= '9')
            {
                sb.Append((char)c);
                ++nDigits;
                c = Get();
            }
            if (c == '.')
            {
                sb.Append('.');
                c = Get();
                while (c >= '0' && c <= '9')
                {
                    sb.Append((char)c);
                    ++nDigits;
                    c = Get();
                }
            }
            if (nDigits == 0)
            {
                Unget(c);
                return 0;
            }
            if (c == 'e' || c == 'E')
            {
                StringBuilder exp = new("e");
                c = Get();
                if (c == '+' || c == '-')
                {
                    exp.Append((char)c);
                    c = Get();
                }
                if (c >= '0' && c <= '9')
                {
                    while (c >= '0' && c <= '9')
                    {
                        exp.Append((char)c);
                        c = Get();
                    }
                    sb.Append(exp);
                }
                else
                {
                    // An incomplete exponent can't be put back.
                    Unget(c);
                    return 0;
                }
            }
            Unget(c);
            v = float.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return 1;
        }

        // scanf("%f %f", &s[0], &s[1])
        public int ScanTwoFloats(Span<float> s)
        {
            int r = ScanFloat(out s[0]);
            if (r != 1)
                return r;
            r = ScanFloat(out s[1]);
            return r == 1 ? 2 : 1;
        }
    }

    private static CharReader? stdinReader;
    private static Stream? stdinStream;
    private static List<string>? cwdFiles;
    private static int cwdOffset = 0;

    private static List<Point2f>? GenerateSamples(string samplerName, int nPoints, int iter)
    {
        List<Point2f> points = new(nPoints);

        if (samplerName == "stdin.binary")
        {
            stdinStream ??= new BufferedStream(Console.OpenStandardInput(), 1 << 16);
            Span<byte> buf = stackalloc byte[8];
            for (int i = 0; i < nPoints; ++i)
            {
                int n = 0;
                while (n < 8)
                {
                    int got = stdinStream.Read(buf[n..]);
                    if (got == 0)
                        break;
                    n += got;
                }
                if (n / 4 != 2)
                {
                    if (i > 1)
                        ErrorExit($"Partial point set provided in standard input: have {i} points at EOF.");
                    return null;
                }
                points.Add(new Point2f(BitConverter.ToSingle(buf), BitConverter.ToSingle(buf[4..])));
            }
        }
        else if (samplerName == "stdin.dat")
        {
            stdinReader ??= new CharReader(Console.OpenStandardInput());
            Span<float> s = stackalloc float[2];
            for (int i = 0; i < nPoints; ++i)
            {
                int n = stdinReader.ScanTwoFloats(s);
                if (n != 2)
                {
                    if (i > 1)
                        ErrorExit($"Partial point set provided in standard input: have {i} points at EOF.");
                    return null;
                }
                points.Add(new Point2f(s[0], s[1]));
            }
            int ch;
            do
            {
                ch = stdinReader.Get();
            } while (ch != '#' && ch != -1);
        }
        else if (samplerName == "cwd.pts")
        {
            cwdFiles ??= MatchingFilenames("pts-");
            if (cwdFiles.Count == 0)
                ErrorExit("No *.dat files found in current directory.");

            while (true)
            {
                if (cwdOffset == cwdFiles.Count)
                    return null;

                FileStream f;
                try
                {
                    f = new FileStream(cwdFiles[cwdOffset], FileMode.Open, FileAccess.Read);
                }
                catch (Exception)
                {
                    ErrorExit($"{cwdFiles[cwdOffset]}: unable to open file");
                    return null;
                }
                CharReader reader = new(f);

                bool ok = true;
                Span<float> s = stackalloc float[2];
                for (int i = 0; i < nPoints; ++i)
                {
                    int n = reader.ScanTwoFloats(s);
                    if (n < 2)
                    {
                        // (pbrt reports files[iter] here.)
                        Warning($"{(iter < cwdFiles.Count ? cwdFiles[iter] : "")}: premature EOF. " +
                                $"Read {i} / {nPoints} points. Ignoring file.");
                        ++cwdOffset;
                        reader.Close();
                        points.Clear();
                        ok = false;
                        break;
                    }
                    points.Add(new Point2f(s[0], s[1]));
                }
                if (!ok)
                    continue;
                ++cwdOffset;
                reader.Close();
                break;
            }
        }
        else if (samplerName == "grid")
        {
            int sqrtSamples = (int)Math.Sqrt(nPoints);
            nPoints = Sqr(sqrtSamples);

            for (int i = 0; i < sqrtSamples; ++i)
                for (int j = 0; j < sqrtSamples; ++j)
                    points.Add(new Point2f((Float)i / sqrtSamples, (Float)j / sqrtSamples));
        }
        else if (samplerName == "lhs")
        {
            RNG rng = new((ulong)Options.seed, (ulong)iter);
            // Sample points along the diagonal
            for (int i = 0; i < nPoints; ++i)
            {
                // (GCC evaluates the Point2f constructor arguments right to
                // left.)
                Float y = (i + rng.Uniform<Float>()) / nPoints;
                Float x = (i + rng.Uniform<Float>()) / nPoints;
                points.Add(new Point2f(x, y));
            }
            // Suffle x
            for (int i = 0; i < nPoints; ++i)
            {
                int other = i + (int)rng.Uniform((uint)(nPoints - i));
                Point2f pi = points[i], po = points[other];
                (pi.x, po.x) = (po.x, pi.x);
                points[i] = pi;
                if (other != i)
                    points[other] = po;
            }
        }
        else if (samplerName == "halton")
        {
            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(RadicalInverse(0, (ulong)i), RadicalInverse(1, (ulong)i)));
        }
        else if (samplerName == "halton.permutedigits")
        {
            RNG rng = new((ulong)Options.seed, (ulong)iter);
            DigitPermutation perm2 = new(2, rng.Uniform<uint>());
            DigitPermutation perm3 = new(3, rng.Uniform<uint>());

            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(ScrambledRadicalInverse(0, (ulong)i, perm2),
                                       ScrambledRadicalInverse(1, (ulong)i, perm3)));
        }
        else if (samplerName == "halton.owen")
        {
            RNG rng = new((ulong)Options.seed, (ulong)iter);
            uint r0 = rng.Uniform<uint>(), r1 = rng.Uniform<uint>();

            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(SobolSample(i, 0, new OwenScrambler(r0)),
                                       OwenScrambledRadicalInverse(1, (ulong)i, r1)));
        }
        else if (samplerName == "sobol.z")
        {
            if (!IsPowerOf2(nPoints))
                ErrorExit("Must use power of 2 points for \"sobol.z\".");

            int n = 1 << (Log2Int(nPoints) / 2);
            int spp = nPoints / Sqr(n);
            ZSobolSampler sampler = new(spp, new Point2i(n, n), RandomizeStrategy.Owen, Options.seed);

            for (int y = 0; y < n; ++y)
                for (int x = 0; x < n; ++x)
                {
                    sampler.StartPixelSample(new Point2i(x, y), 0, 0);
                    for (int s = 0; s < spp; ++s)
                    {
                        Point2f u = sampler.Get2D();
                        points.Add(new Point2f((x + u[0]) / n, (y + u[1]) / n));
                    }
                }
        }
        else
        {
            Sampler sampler;
            if (samplerName == "independent")
                sampler = new IndependentSampler(nPoints, Options.seed);
            else if (samplerName == "stratified")
            {
                int sqrtSamples = (int)Math.Sqrt(nPoints);
                nPoints = Sqr(sqrtSamples);
                sampler = new StratifiedSampler(sqrtSamples, sqrtSamples, true, Options.seed);
            }
            else if (samplerName == "pmj02bn")
                sampler = new PMJ02BNSampler(nPoints, Options.seed);
            else if (samplerName == "sobol")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.None, Options.seed);
            else if (samplerName == "sobol.permutedigits")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.PermuteDigits, Options.seed);
            else if (samplerName == "sobol.fastowen")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.FastOwen, Options.seed);
            else if (samplerName == "sobol.owen")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.Owen, Options.seed);
            else
            {
                usage($"{samplerName}: sampler unknown");
                Environment.Exit(1);
                return null;
            }

            for (int i = 0; i < nPoints; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i, 0);
                Point2f u = sampler.Get2D();
                points.Add(u);
            }
        }

        return points;
    }

    public static int Main(string[] argv)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is FatalErrorException)
                Environment.Exit(1);
        };
        try
        {
            return Run(argv);
        }
        catch (FatalErrorException)
        {
            // (The error has been reported.)
            return 1;
        }
    }

    private static int Run(string[] argv)
    {
        if (argv.Length < 1)
        {
            usage();
            return 1;
        }

        List<string> args = GetCommandLineArguments(argv);

        string samplerName = "";
        int nPoints = 1024;
        int nSets = 0;
        int res = 1500;
        string baseOutFilename = "";

        static void onError(string err)
        {
            usage(err);
            Environment.Exit(1);
        }
        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (ParseArg(args, ref iter, "npoints", ref nPoints, onError) ||
                ParseArg(args, ref iter, "resolution", ref res, onError) ||
                ParseArg(args, ref iter, "outbase", ref baseOutFilename, onError) ||
                ParseArg(args, ref iter, "nsets", ref nSets, onError))
                { }
            else if (samplerName.Length == 0)
            {
                if (args[iter].Length > 0 && args[iter][0] == '-')
                {
                    usage();
                    return 0;
                }
                samplerName = args[iter];
            }
            else
            {
                usage($"unknown argument \"{args[iter]}\"");
                return 1;
            }
        }

        if (samplerName.Length == 0)
        {
            usage("Must specify name of sampler.\n");
            return 1;
        }

        if ((res & 1) == 0)
            ++res;

        PBRTOptions options = new() { quiet = true };
        InitPBRT(options);

        Image pspec = new(PixelFormat.Float, new Point2i(res, res), ["power"]);
        ProgressReporter progress = new(nSets, "Analyzing", nSets == 1);

        int actualNSets = 0;
        while (true)
        {
            Options.seed = (int)MixBits((ulong)actualNSets);

            // Generate points
            List<Point2f>? points = GenerateSamples(samplerName, nPoints, actualNSets);
            if (points == null)
                break;
            ++actualNSets;

            // Fourier transform
            Point2f[] pts = points.ToArray();
            ParallelFor(0, res, (long yl) =>
            {
                int y = (int)yl;
                for (int x = 0; x < res; ++x)
                {
                    Point2f uv = new(0, 0);
                    Float wx = x - res / 2, wy = y - res / 2;
                    foreach (Point2f u in pts)
                    {
                        Float exp = -2 * Pi * (wx * u[0] + wy * u[1]);
                        uv[0] += FMath.Cos(exp);
                        uv[1] += FMath.Sin(exp);
                    }

                    // Update power spectrum
                    pspec.SetChannel(new Point2i(x, y), 0,
                                     pspec.GetChannel(new Point2i(x, y), 0) + (Sqr(uv[0]) + Sqr(uv[1])));
                }
            });

            progress.Update();

            if (nSets != 0 && actualNSets == nSets)
                break;
        }

        ParallelFor(0, res, (long yl) =>
        {
            int y = (int)yl;
            for (int x = 0; x < res; ++x)
            {
                // Early float cast so no integer overflow...
                pspec.SetChannel(new Point2i(x, y), 0,
                                 pspec.GetChannel(new Point2i(x, y), 0) / ((Float)nPoints * (Float)actualNSets));
            }
        });

        progress.Done();

        if (baseOutFilename.Length == 0)
            baseOutFilename = $"{samplerName}-{nPoints}pts-{actualNSets}sets";

        pspec.Write(baseOutFilename + ".exr");

        // Compute radial average
        Float[] sumPower = new Float[res / 2];
        int[] nPower = new int[res / 2];
        for (int y = 0; y < res; ++y)
            for (int x = 0; x < res; ++x)
            {
                int dx = Math.Abs(x - res / 2), dy = Math.Abs(y - res / 2);
                if (dx == 0 && dy == 0)
                    // skip the central spike
                    continue;
                int bucket = (int)Math.Sqrt(dx * dx + dy * dy);
                if (bucket >= sumPower.Length)
                    continue;
                sumPower[bucket] += pspec.GetChannel(new Point2i(x, y), 0);
                ++nPower[bucket];
            }

        StreamWriter f;
        try
        {
            f = new StreamWriter(baseOutFilename + ".txt", false, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            ErrorExit($"{baseOutFilename + ".txt"}: could not open output file");
            return 1;
        }
        using (f)
        {
            for (int i = 1; i < res / 2; ++i)
                f.Write(string.Create(CultureInfo.InvariantCulture,
                                      $"{i} {((double)(sumPower[i] / nPower[i])).ToString("F6", CultureInfo.InvariantCulture)}\n"));
        }

        CleanupPBRT();

        return 0;
    }
}

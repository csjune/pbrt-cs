// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/samplers.h and samplers.cpp
//
// pbrt prints the HaltonSampler's and PMJ02BNSampler's table pointers with
// %p in ToString(); here they are printed as "(table)". GetCameraSample()
// is a template in pbrt and takes a Sampler here.

namespace Pbrt;

// HaltonSampler Definition
public sealed class HaltonSampler : Sampler
{
    // HaltonSampler Private Members
    private readonly int samplesPerPixel;
    private readonly RandomizeStrategy randomize;
    private readonly DigitPermutation[]? digitPermutations;
    private const int MaxHaltonResolution = 128;
    private Point2i baseScales = default, baseExponents = default;
    private readonly int multInverse0, multInverse1;
    private long haltonIndex = 0;
    private int dimension = 0;

    // HaltonSampler Method Definitions
    public HaltonSampler(int samplesPerPixel, Point2i fullRes,
                         RandomizeStrategy randomize = RandomizeStrategy.PermuteDigits, int seed = 0)
    {
        this.samplesPerPixel = samplesPerPixel;
        this.randomize = randomize;
        if (randomize == RandomizeStrategy.PermuteDigits)
            digitPermutations = ComputeRadicalInversePermutations((uint)seed);
        // Find radical inverse base scales and exponents that cover sampling area
        for (int i = 0; i < 2; ++i)
        {
            int @base = (i == 0) ? 2 : 3;
            int scale = 1, exp = 0;
            while (scale < Math.Min(fullRes[i], MaxHaltonResolution))
            {
                scale *= @base;
                ++exp;
            }
            baseScales[i] = scale;
            baseExponents[i] = exp;
        }

        // Compute multiplicative inverses for _baseScales_
        multInverse0 = (int)MultiplicativeInverse(baseScales[1], baseScales[0]);
        multInverse1 = (int)MultiplicativeInverse(baseScales[0], baseScales[1]);
    }

    // HaltonSampler Public Methods
    public override int SamplesPerPixel() => samplesPerPixel;

    public RandomizeStrategy GetRandomizeStrategy() => randomize;

    public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
    {
        haltonIndex = 0;
        int sampleStride = baseScales[0] * baseScales[1];
        // Compute Halton sample index for first sample in pixel _p_
        if (sampleStride > 1)
        {
            Point2i pm = new(Mod(p[0], MaxHaltonResolution), Mod(p[1], MaxHaltonResolution));
            for (int i = 0; i < 2; ++i)
            {
                ulong dimOffset = (i == 0) ? InverseRadicalInverse((ulong)pm[i], 2, baseExponents[i])
                                           : InverseRadicalInverse((ulong)pm[i], 3, baseExponents[i]);
                haltonIndex = (long)((ulong)haltonIndex + dimOffset * (ulong)(long)(sampleStride / baseScales[i]) *
                                     (ulong)(long)(i == 0 ? multInverse0 : multInverse1));
            }
            haltonIndex %= sampleStride;
        }

        haltonIndex += sampleIndex * sampleStride;
        dimension = Math.Max(2, dim);
    }

    public override Float Get1D()
    {
        if (dimension >= PrimeTableSize)
            dimension = 2;
        return SampleDimension(dimension++);
    }

    public override Point2f Get2D()
    {
        if (dimension + 1 >= PrimeTableSize)
            dimension = 2;
        int dim = dimension;
        dimension += 2;
        return new Point2f(SampleDimension(dim), SampleDimension(dim + 1));
    }

    public override Point2f GetPixel2D() =>
        new(RadicalInverse(0, (ulong)(haltonIndex >> baseExponents[0])),
            RadicalInverse(1, (ulong)(haltonIndex / baseScales[1])));

    // HaltonSampler Private Methods
    private static ulong MultiplicativeInverse(long a, long n)
    {
        ExtendedGCD((ulong)a, (ulong)n, out long x, out _);
        return (ulong)Mod(x, n);
    }

    private static void ExtendedGCD(ulong a, ulong b, out long x, out long y)
    {
        if (b == 0)
        {
            x = 1;
            y = 0;
            return;
        }
        long d = (long)(a / b);
        ExtendedGCD(b, a % b, out long xp, out long yp);
        x = yp;
        y = xp - (d * yp);
    }

    private Float SampleDimension(int dimension)
    {
        if (randomize == RandomizeStrategy.None)
            return RadicalInverse(dimension, (ulong)haltonIndex);
        else if (randomize == RandomizeStrategy.PermuteDigits)
            return ScrambledRadicalInverse(dimension, (ulong)haltonIndex, digitPermutations![dimension]);
        else
        {
            DCHECK(randomize == RandomizeStrategy.Owen);
            return OwenScrambledRadicalInverse(dimension, (ulong)haltonIndex, (uint)MixBits((ulong)(1 + (dimension << 4))));
        }
    }

    public override string ToString() =>
        $"[ HaltonSampler randomize: {Util.ToString(randomize)} digitPermutations: " +
        $"{(digitPermutations != null ? "(table)" : "(nil)")} haltonIndex: {haltonIndex} dimension: {dimension} " +
        $"samplesPerPixel: {samplesPerPixel} baseScales: {baseScales} baseExponents: {baseExponents} " +
        $"multInverse: [ {multInverse0} {multInverse1} ] ]";

    public static HaltonSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc? loc)
    {
        int nsamp = parameters.GetOneInt("pixelsamples", 16);
        if (Options.pixelSamples is { } ps)
            nsamp = ps;
        int seed = parameters.GetOneInt("seed", Options.seed);
        if (Options.quickRender)
            nsamp = 1;

        RandomizeStrategy randomizer = RandomizeStrategy.None;
        string s = parameters.GetOneString("randomization", "permutedigits");
        if (s == "none")
            randomizer = RandomizeStrategy.None;
        else if (s == "permutedigits")
            randomizer = RandomizeStrategy.PermuteDigits;
        else if (s == "fastowen")
            ErrorExit("\"fastowen\" randomization not supported by Halton sampler.");
        else if (s == "owen")
            randomizer = RandomizeStrategy.Owen;
        else
            ErrorExit(loc, $"{s}: unknown randomization strategy given to HaltonSampler");

        return new HaltonSampler(nsamp, fullResolution, randomizer, seed);
    }
}

// PaddedSobolSampler Definition
public sealed class PaddedSobolSampler : Sampler
{
    // PaddedSobolSampler Private Members
    private readonly int samplesPerPixel, seed;
    private readonly RandomizeStrategy randomize;
    private Point2i pixel;
    private int sampleIndex, dimension;

    // PaddedSobolSampler Public Methods
    public PaddedSobolSampler(int samplesPerPixel, RandomizeStrategy randomizer, int seed = 0)
    {
        this.samplesPerPixel = samplesPerPixel;
        randomize = randomizer;
        this.seed = seed;
        if (!IsPowerOf2(samplesPerPixel))
            Warning($"Sobol samplers with non power-of-two sample counts ({samplesPerPixel}) are suboptimal.");
    }

    public override int SamplesPerPixel() => samplesPerPixel;

    public override void StartPixelSample(Point2i p, int index, int dim = 0)
    {
        pixel = p;
        sampleIndex = index;
        dimension = dim;
    }

    public override Float Get1D()
    {
        // Get permuted index for current pixel sample
        ulong hash = Hash(pixel, dimension, seed);
        int index = PermutationElement((uint)sampleIndex, (uint)samplesPerPixel, (uint)hash);

        int dim = dimension++;
        // Return randomized 1D van der Corput sample for dimension _dim_
        return SampleDimension(0, (uint)index, (uint)(hash >> 32));
    }

    public override Point2f Get2D()
    {
        // Get permuted index for current pixel sample
        ulong hash = Hash(pixel, dimension, seed);
        int index = PermutationElement((uint)sampleIndex, (uint)samplesPerPixel, (uint)hash);

        int dim = dimension;
        dimension += 2;
        // Return randomized 2D Sobol\+$'$ sample
        return new Point2f(SampleDimension(0, (uint)index, (uint)hash), SampleDimension(1, (uint)index, (uint)(hash >> 32)));
    }

    public override Point2f GetPixel2D() => Get2D();

    public RandomizeStrategy GetRandomizeStrategy() => randomize;

    // PaddedSobolSampler Private Methods
    private Float SampleDimension(int dimension, uint a, uint hash)
    {
        if (randomize == RandomizeStrategy.None)
            return SobolSample(a, dimension, new NoRandomizer());
        else if (randomize == RandomizeStrategy.PermuteDigits)
            return SobolSample(a, dimension, new BinaryPermuteScrambler(hash));
        else if (randomize == RandomizeStrategy.FastOwen)
            return SobolSample(a, dimension, new FastOwenScrambler(hash));
        else
            return SobolSample(a, dimension, new OwenScrambler(hash));
    }

    public override string ToString() =>
        $"[ PaddedSobolSampler pixel: {pixel} sampleIndex: {sampleIndex} dimension: {dimension} " +
        $"samplesPerPixel: {samplesPerPixel} seed: {seed} randomize: {Util.ToString(randomize)} ]";

    public static PaddedSobolSampler Create(ParameterDictionary parameters, FileLoc? loc)
    {
        int nsamp = parameters.GetOneInt("pixelsamples", 16);
        if (Options.pixelSamples is { } ps)
            nsamp = ps;
        if (Options.quickRender)
            nsamp = 1;
        int seed = parameters.GetOneInt("seed", Options.seed);

        RandomizeStrategy randomizer = ParseRandomization(parameters, loc, "PaddedSobolSampler");
        return new PaddedSobolSampler(nsamp, randomizer, seed);
    }

    // The "randomization" parameter of the Sobol samplers.
    internal static RandomizeStrategy ParseRandomization(ParameterDictionary parameters, FileLoc? loc, string sampler)
    {
        string s = parameters.GetOneString("randomization", "fastowen");
        if (s == "none")
            return RandomizeStrategy.None;
        else if (s == "permutedigits")
            return RandomizeStrategy.PermuteDigits;
        else if (s == "fastowen")
            return RandomizeStrategy.FastOwen;
        else if (s == "owen")
            return RandomizeStrategy.Owen;
        else
            ErrorExit(loc, $"{s}: unknown randomization strategy given to {sampler}");
        return RandomizeStrategy.None;
    }
}

// ZSobolSampler Definition
public sealed class ZSobolSampler : Sampler
{
    // ZSobolSampler Private Members
    private readonly RandomizeStrategy randomize;
    private readonly int seed, log2SamplesPerPixel, nBase4Digits;
    private ulong mortonIndex;
    private int dimension;

    // ZSobolSampler Public Methods
    public ZSobolSampler(int samplesPerPixel, Point2i fullResolution, RandomizeStrategy randomize, int seed = 0)
    {
        this.randomize = randomize;
        this.seed = seed;
        if (!IsPowerOf2(samplesPerPixel))
            Warning($"Sobol samplers with non power-of-two sample counts ({samplesPerPixel}) are suboptimal.");
        log2SamplesPerPixel = Log2Int(samplesPerPixel);
        int res = RoundUpPow2(Math.Max(fullResolution.x, fullResolution.y));
        int log4SamplesPerPixel = (log2SamplesPerPixel + 1) / 2;
        nBase4Digits = Log2Int(res) + log4SamplesPerPixel;
    }

    public override int SamplesPerPixel() => 1 << log2SamplesPerPixel;

    public override void StartPixelSample(Point2i p, int index, int dim = 0)
    {
        dimension = dim;
        mortonIndex = (EncodeMorton2((uint)p.x, (uint)p.y) << log2SamplesPerPixel) | (ulong)(long)index;
    }

    public override Float Get1D()
    {
        ulong sampleIndex = GetSampleIndex();
        ++dimension;
        // Generate 1D Sobol\+$'$ sample at _sampleIndex_
        uint sampleHash = (uint)Hash(dimension, seed);
        if (randomize == RandomizeStrategy.None)
            return SobolSample((long)sampleIndex, 0, new NoRandomizer());
        else if (randomize == RandomizeStrategy.PermuteDigits)
            return SobolSample((long)sampleIndex, 0, new BinaryPermuteScrambler(sampleHash));
        else if (randomize == RandomizeStrategy.FastOwen)
            return SobolSample((long)sampleIndex, 0, new FastOwenScrambler(sampleHash));
        else
            return SobolSample((long)sampleIndex, 0, new OwenScrambler(sampleHash));
    }

    public override Point2f Get2D()
    {
        long sampleIndex = (long)GetSampleIndex();
        dimension += 2;
        // Generate 2D Sobol\+$'$ sample at _sampleIndex_
        ulong bits = Hash(dimension, seed);
        uint sampleHash0 = (uint)bits, sampleHash1 = (uint)(bits >> 32);
        if (randomize == RandomizeStrategy.None)
            return new Point2f(SobolSample(sampleIndex, 0, new NoRandomizer()),
                               SobolSample(sampleIndex, 1, new NoRandomizer()));
        else if (randomize == RandomizeStrategy.PermuteDigits)
            return new Point2f(SobolSample(sampleIndex, 0, new BinaryPermuteScrambler(sampleHash0)),
                               SobolSample(sampleIndex, 1, new BinaryPermuteScrambler(sampleHash1)));
        else if (randomize == RandomizeStrategy.FastOwen)
            return new Point2f(SobolSample(sampleIndex, 0, new FastOwenScrambler(sampleHash0)),
                               SobolSample(sampleIndex, 1, new FastOwenScrambler(sampleHash1)));
        else
            return new Point2f(SobolSample(sampleIndex, 0, new OwenScrambler(sampleHash0)),
                               SobolSample(sampleIndex, 1, new OwenScrambler(sampleHash1)));
    }

    public override Point2f GetPixel2D() => Get2D();

    // Define the full set of 4-way permutations in _permutations_
    private static ReadOnlySpan<byte> permutations =>
    [
        0, 1, 2, 3, 0, 1, 3, 2, 0, 2, 1, 3, 0, 2, 3, 1,
        // Define remaining 20 4-way permutations
        0, 3, 2, 1, 0, 3, 1, 2, 1, 0, 2, 3, 1, 0, 3, 2, 1, 2, 0, 3, 1, 2, 3, 0, 1, 3, 2, 0, 1, 3, 0, 2,
        2, 1, 0, 3, 2, 1, 3, 0, 2, 0, 1, 3, 2, 0, 3, 1, 2, 3, 0, 1, 2, 3, 1, 0, 3, 1, 2, 0, 3, 1, 0, 2,
        3, 2, 1, 0, 3, 2, 0, 1, 3, 0, 2, 1, 3, 0, 1, 2,
    ];

    public ulong GetSampleIndex()
    {
        ulong sampleIndex = 0;
        // Apply random permutations to full base-4 digits
        bool pow2Samples = (log2SamplesPerPixel & 1) != 0;
        int lastDigit = pow2Samples ? 1 : 0;
        for (int i = nBase4Digits - 1; i >= lastDigit; --i)
        {
            // Randomly permute $i$th base-4 digit in _mortonIndex_
            int digitShift = 2 * i - (pow2Samples ? 1 : 0);
            int digit = (int)((mortonIndex >> digitShift) & 3);
            // Choose permutation _p_ to use for _digit_
            ulong higherDigits = mortonIndex >> (digitShift + 2);
            int p = (int)((MixBits(higherDigits ^ (0x55555555u * (uint)dimension)) >> 24) % 24);

            digit = permutations[p * 4 + digit];
            sampleIndex |= (ulong)digit << digitShift;
        }

        // Handle power-of-2 (but not 4) sample count
        if (pow2Samples)
        {
            int digit = (int)(mortonIndex & 1);
            sampleIndex |= (ulong)digit ^ (MixBits((mortonIndex >> 1) ^ (0x55555555u * (uint)dimension)) & 1);
        }

        return sampleIndex;
    }

    // ZSobolSampler Method Definitions
    public override string ToString() =>
        $"[ ZSobolSampler randomize: {Util.ToString(randomize)} log2SamplesPerPixel: {log2SamplesPerPixel} " +
        $" seed: {seed} nBase4Digits: {nBase4Digits} mortonIndex: {(long)mortonIndex} dimension: {dimension} ]";

    public static ZSobolSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc? loc)
    {
        int nsamp = parameters.GetOneInt("pixelsamples", 16);
        if (Options.pixelSamples is { } ps)
            nsamp = ps;
        if (Options.quickRender)
            nsamp = 1;
        int seed = parameters.GetOneInt("seed", Options.seed);

        RandomizeStrategy randomizer = PaddedSobolSampler.ParseRandomization(parameters, loc, "ZSobolSampler");
        return new ZSobolSampler(nsamp, fullResolution, randomizer, seed);
    }
}

// PMJ02BNSampler Definition
public sealed class PMJ02BNSampler : Sampler
{
    // PMJ02BNSampler Private Members
    private readonly int samplesPerPixel, seed;
    private readonly int pixelTileSize;
    private readonly Point2f[] pixelSamples;
    private Point2i pixel;
    private int sampleIndex, dimension;

    // PMJ02BNSampler Method Definitions
    public PMJ02BNSampler(int samplesPerPixel, int seed = 0)
    {
        this.samplesPerPixel = samplesPerPixel;
        this.seed = seed;
        if (!IsPowerOf4(samplesPerPixel))
            Warning("PMJ02BNSampler results are best with power-of-4 samples per pixel (1, 4, 16, 64, ...)");
        // Get sorted pmj02bn samples for pixel samples
        if (samplesPerPixel > nPMJ02bnSamples)
            Error($"PMJ02BNSampler only supports up to {nPMJ02bnSamples} samples per pixel");
        // Compute _pixelTileSize_ for pmj02bn pixel samples and allocate _pixelSamples_
        pixelTileSize = 1 << (Log4Int(nPMJ02bnSamples) - Log4Int(RoundUpPow4(samplesPerPixel)));
        int nPixelSamples = pixelTileSize * pixelTileSize * samplesPerPixel;
        pixelSamples = new Point2f[nPixelSamples];

        // Loop over pmj02bn samples and associate them with their pixels
        int[] nStored = new int[pixelTileSize * pixelTileSize];
        for (int i = 0; i < nPMJ02bnSamples; ++i)
        {
            Point2f p = GetPMJ02BNSample(0, i);
            p *= pixelTileSize;
            int pixelOffset = (int)p.x + (int)p.y * pixelTileSize;
            if (nStored[pixelOffset] == samplesPerPixel)
            {
                CHECK(!IsPowerOf4(samplesPerPixel));
                continue;
            }
            int sampleOffset = pixelOffset * samplesPerPixel + nStored[pixelOffset];
            CHECK(pixelSamples[sampleOffset] == new Point2f(0, 0));
            pixelSamples[sampleOffset] = new Point2f(p - Floor(p));
            ++nStored[pixelOffset];
        }

        for (int i = 0; i < nStored.Length; ++i)
            CHECK_EQ(nStored[i], samplesPerPixel);
        foreach (int c in nStored)
            DCHECK_EQ(c, samplesPerPixel);
    }

    // PMJ02BNSampler Public Methods
    public override int SamplesPerPixel() => samplesPerPixel;

    public override void StartPixelSample(Point2i p, int index, int dim = 0)
    {
        pixel = p;
        sampleIndex = index;
        dimension = Math.Max(2, dim);
    }

    public override Float Get1D()
    {
        // Find permuted sample index for 1D PMJ02BNSampler sample
        ulong hash = Hash(pixel, dimension, seed);
        int index = PermutationElement((uint)sampleIndex, (uint)samplesPerPixel, (uint)hash);

        Float delta = BlueNoise(dimension, pixel);
        ++dimension;
        return FMath.Min((index + delta) / samplesPerPixel, OneMinusEpsilon);
    }

    public override Point2f GetPixel2D()
    {
        int px = pixel.x % pixelTileSize, py = pixel.y % pixelTileSize;
        int offset = (px + py * pixelTileSize) * samplesPerPixel;
        return pixelSamples[offset + sampleIndex];
    }

    public override Point2f Get2D()
    {
        // Compute index for 2D pmj02bn sample
        int index = sampleIndex;
        int pmjInstance = dimension / 2;
        if (pmjInstance >= nPMJ02bnSets)
        {
            // Permute index to be used for pmj02bn sample array
            ulong hash = Hash(pixel, dimension, seed);
            index = PermutationElement((uint)sampleIndex, (uint)samplesPerPixel, (uint)hash);
        }

        // Return randomized pmj02bn sample for current dimension
        Point2f u = GetPMJ02BNSample(pmjInstance, index);
        // Apply Cranley-Patterson rotation to pmj02bn sample _u_
        u += new Vector2f(BlueNoise(dimension, pixel), BlueNoise(dimension + 1, pixel));
        if (u.x >= 1)
            u.x -= 1;
        if (u.y >= 1)
            u.y -= 1;

        dimension += 2;
        return new Point2f(FMath.Min(u.x, OneMinusEpsilon), FMath.Min(u.y, OneMinusEpsilon));
    }

    public override string ToString() =>
        $"[ PMJ02BNSampler pixel: {pixel} sampleIndex: {sampleIndex} dimension: {dimension} " +
        $"samplesPerPixel: {samplesPerPixel} pixelTileSize: {pixelTileSize} pixelSamples: (table) ]";

    public static PMJ02BNSampler Create(ParameterDictionary parameters, FileLoc? loc)
    {
        int nsamp = parameters.GetOneInt("pixelsamples", 16);
        if (Options.pixelSamples is { } ps)
            nsamp = ps;
        if (Options.quickRender)
            nsamp = 1;
        int seed = parameters.GetOneInt("seed", Options.seed);
        return new PMJ02BNSampler(nsamp, seed);
    }
}

// IndependentSampler Definition
public sealed class IndependentSampler : Sampler
{
    // IndependentSampler Private Members
    private readonly int samplesPerPixel, seed;
    private RNG rng = new();

    // IndependentSampler Public Methods
    public IndependentSampler(int samplesPerPixel, int seed = 0)
    {
        this.samplesPerPixel = samplesPerPixel;
        this.seed = seed;
    }

    public override int SamplesPerPixel() => samplesPerPixel;

    public override void StartPixelSample(Point2i p, int sampleIndex, int dimension = 0)
    {
        rng.SetSequence(Hash(p, seed));
        rng.Advance(unchecked((long)((ulong)(long)sampleIndex * 65536ul + (ulong)(long)dimension)));
    }

    public override Float Get1D() => rng.Uniform<Float>();

    public override Point2f Get2D()
    {
        Float x = rng.Uniform<Float>();
        Float y = rng.Uniform<Float>();
        return new Point2f(x, y);
    }

    public override Point2f GetPixel2D() => Get2D();

    public override string ToString() =>
        $"[ IndependentSampler samplesPerPixel: {samplesPerPixel} seed: {seed} rng: {rng} ]";

    public static IndependentSampler Create(ParameterDictionary parameters, FileLoc? loc)
    {
        int ns = parameters.GetOneInt("pixelsamples", 4);
        if (Options.pixelSamples is { } ps)
            ns = ps;
        int seed = parameters.GetOneInt("seed", Options.seed);
        return new IndependentSampler(ns, seed);
    }
}

// SobolSampler Definition
public sealed class SobolSampler : Sampler
{
    // SobolSampler Private Members
    private readonly int samplesPerPixel, scale, seed;
    private readonly RandomizeStrategy randomize;
    private Point2i pixel;
    private int dimension;
    private long sobolIndex;

    // SobolSampler Public Methods
    public SobolSampler(int samplesPerPixel, Point2i fullResolution, RandomizeStrategy randomize, int seed = 0)
    {
        this.samplesPerPixel = samplesPerPixel;
        this.seed = seed;
        this.randomize = randomize;
        if (!IsPowerOf2(samplesPerPixel))
            Warning($"Non power-of-two sample count {samplesPerPixel} will perform suboptimally with the SobolSampler.");
        scale = RoundUpPow2(Math.Max(fullResolution.x, fullResolution.y));
    }

    public override int SamplesPerPixel() => samplesPerPixel;

    public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
    {
        pixel = p;
        dimension = Math.Max(2, dim);
        sobolIndex = (long)SobolIntervalToIndex((uint)Log2Int(scale), (ulong)(long)sampleIndex, pixel);
    }

    public override Float Get1D()
    {
        if (dimension >= NSobolDimensions)
            dimension = 2;
        return SampleDimension(dimension++);
    }

    public override Point2f Get2D()
    {
        if (dimension + 1 >= NSobolDimensions)
            dimension = 2;
        Point2f u = new(SampleDimension(dimension), SampleDimension(dimension + 1));
        dimension += 2;
        return u;
    }

    public override Point2f GetPixel2D()
    {
        Point2f u = new(SobolSample(sobolIndex, 0, new NoRandomizer()), SobolSample(sobolIndex, 1, new NoRandomizer()));
        // Remap Sobol\+$'$ dimensions used for pixel samples
        for (int dim = 0; dim < 2; ++dim)
            u[dim] = Clamp(u[dim] * scale - pixel[dim], 0, OneMinusEpsilon);

        return u;
    }

    // SobolSampler Private Methods
    private Float SampleDimension(int dimension)
    {
        // Return un-randomized Sobol\+$'$ sample if appropriate
        if (randomize == RandomizeStrategy.None)
            return SobolSample(sobolIndex, dimension, new NoRandomizer());

        // Return randomized Sobol\+$'$ sample using _randomize_
        uint hash = (uint)Hash(dimension, seed);
        if (randomize == RandomizeStrategy.PermuteDigits)
            return SobolSample(sobolIndex, dimension, new BinaryPermuteScrambler(hash));
        else if (randomize == RandomizeStrategy.FastOwen)
            return SobolSample(sobolIndex, dimension, new FastOwenScrambler(hash));
        else
            return SobolSample(sobolIndex, dimension, new OwenScrambler(hash));
    }

    // SobolSampler Method Definitions
    public override string ToString() =>
        $"[ SobolSampler pixel: {pixel} dimension: {dimension} samplesPerPixel: {samplesPerPixel} " +
        $"scale: {scale} sobolIndex: {sobolIndex} seed: {seed} randomize: {Util.ToString(randomize)} ]";

    public static SobolSampler Create(ParameterDictionary parameters, Point2i fullResolution, FileLoc? loc)
    {
        int nsamp = parameters.GetOneInt("pixelsamples", 16);
        if (Options.pixelSamples is { } ps)
            nsamp = ps;
        if (Options.quickRender)
            nsamp = 1;

        RandomizeStrategy randomizer = PaddedSobolSampler.ParseRandomization(parameters, loc, "SobolSampler");

        int seed = parameters.GetOneInt("seed", Options.seed);

        return new SobolSampler(nsamp, fullResolution, randomizer, seed);
    }
}

// StratifiedSampler Definition
public sealed class StratifiedSampler : Sampler
{
    // StratifiedSampler Private Members
    private readonly int xPixelSamples, yPixelSamples, seed;
    private readonly bool jitter;
    private RNG rng = new();
    private Point2i pixel;
    private int sampleIndex = 0, dimension = 0;

    // StratifiedSampler Public Methods
    public StratifiedSampler(int xPixelSamples, int yPixelSamples, bool jitter, int seed = 0)
    {
        this.xPixelSamples = xPixelSamples;
        this.yPixelSamples = yPixelSamples;
        this.seed = seed;
        this.jitter = jitter;
    }

    public override int SamplesPerPixel() => xPixelSamples * yPixelSamples;

    public override void StartPixelSample(Point2i p, int index, int dim = 0)
    {
        pixel = p;
        sampleIndex = index;
        dimension = dim;
        rng.SetSequence(Hash(p, seed));
        rng.Advance(unchecked((long)((ulong)(long)sampleIndex * 65536ul + (ulong)(long)dimension)));
    }

    public override Float Get1D()
    {
        // Compute _stratum_ index for current pixel and dimension
        ulong hash = Hash(pixel, dimension, seed);
        int stratum = PermutationElement((uint)sampleIndex, (uint)SamplesPerPixel(), (uint)hash);

        ++dimension;
        Float delta = jitter ? rng.Uniform<Float>() : 0.5f;
        return (stratum + delta) / SamplesPerPixel();
    }

    public override Point2f Get2D()
    {
        // Compute _stratum_ index for current pixel and dimension
        ulong hash = Hash(pixel, dimension, seed);
        int stratum = PermutationElement((uint)sampleIndex, (uint)SamplesPerPixel(), (uint)hash);

        dimension += 2;
        int x = stratum % xPixelSamples, y = stratum / xPixelSamples;
        Float dx = jitter ? rng.Uniform<Float>() : 0.5f;
        Float dy = jitter ? rng.Uniform<Float>() : 0.5f;
        return new Point2f((x + dx) / xPixelSamples, (y + dy) / yPixelSamples);
    }

    public override Point2f GetPixel2D() => Get2D();

    // StratifiedSampler Method Definitions
    public override string ToString() =>
        $"[ StratifiedSampler pixel: {pixel} sampleIndex: {sampleIndex} dimension: {dimension} " +
        $"xPixelSamples: {xPixelSamples} yPixelSamples: {yPixelSamples} jitter: {(jitter ? "true" : "false")} " +
        $"seed: {seed} rng: {rng} ]";

    public static StratifiedSampler Create(ParameterDictionary parameters, FileLoc? loc)
    {
        bool jitter = parameters.GetOneBool("jitter", true);
        int xSamples = parameters.GetOneInt("xsamples", 4);
        int ySamples = parameters.GetOneInt("ysamples", 4);
        if (Options.pixelSamples is { } nSamples)
        {
            int div = (int)Math.Sqrt(nSamples);
            while (nSamples % div != 0)
            {
                CHECK_GT(div, 0);
                --div;
            }
            xSamples = nSamples / div;
            ySamples = nSamples / xSamples;
            CHECK_EQ(nSamples, xSamples * ySamples);
            LOG_VERBOSE($"xSamples {xSamples} ySamples {ySamples}");
        }
        if (Options.quickRender)
            xSamples = ySamples = 1;
        int seed = parameters.GetOneInt("seed", Options.seed);

        return new StratifiedSampler(xSamples, ySamples, jitter, seed);
    }
}

// MLTSampler Definition
public class MLTSampler : Sampler
{
    // MLTSampler Private Declarations
    protected struct PrimarySample
    {
        public Float value = 0;

        public PrimarySample() { }

        // PrimarySample Public Methods
        public void Backup()
        {
            valueBackup = value;
            modifyBackup = lastModificationIteration;
        }

        public void Restore()
        {
            value = valueBackup;
            lastModificationIteration = modifyBackup;
        }

        public override readonly string ToString() =>
            $"[ PrimarySample lastModificationIteration: {lastModificationIteration} " +
            $"valueBackup: {FloatToString(valueBackup)} modifyBackup: {modifyBackup} ]";

        // PrimarySample Public Members
        public long lastModificationIteration = 0;
        public Float valueBackup = 0;
        public long modifyBackup = 0;
    }

    // MLTSampler Private Members
    protected int mutationsPerPixel;
    protected RNG rng;
    protected Float sigma, largeStepProbability;
    protected int streamCount;
    protected List<PrimarySample> X = [];
    protected long currentIteration = 0;
    protected bool largeStep = true;
    protected long lastLargeStepIteration = 0;
    protected int streamIndex, sampleIndex;

    // MLTSampler Public Methods
    public MLTSampler(int mutationsPerPixel, int rngSequenceIndex, Float sigma, Float largeStepProbability,
                      int streamCount)
    {
        this.mutationsPerPixel = mutationsPerPixel;
        rng = new RNG(MixBits((ulong)(long)rngSequenceIndex) ^ MixBits((ulong)(long)Options.seed));
        this.sigma = sigma;
        this.largeStepProbability = largeStepProbability;
        this.streamCount = streamCount;
    }

    public int GetNextIndex() => streamIndex + streamCount * sampleIndex++;

    public override int SamplesPerPixel() => mutationsPerPixel;

    public override void StartPixelSample(Point2i p, int sampleIndex, int dim = 0)
    {
        rng.SetSequence(Hash(p));
        rng.Advance(sampleIndex * 65536 + dim * 8192);
    }

    // MLTSampler Method Definitions
    public override Float Get1D()
    {
        int index = GetNextIndex();
        EnsureReady(index);
        return X[index].value;
    }

    public override Point2f Get2D()
    {
        Float x = Get1D();
        Float y = Get1D();
        return new Point2f(x, y);
    }

    public override Point2f GetPixel2D() => Get2D();

    public override Sampler Clone()
    {
        LOG_FATAL("MLTSampler::Clone() is not implemented");
        return null!;
    }

    public void StartIteration()
    {
        currentIteration++;
        largeStep = rng.Uniform<Float>() < largeStepProbability;
    }

    public void Accept()
    {
        if (largeStep)
            lastLargeStepIteration = currentIteration;
    }

    private void EnsureReady(int index)
    {
        // Enlarge _MLTSampler::X_ if necessary and get current $\VEC{X}_i$
        while (index >= X.Count)
            X.Add(new PrimarySample());
        Span<PrimarySample> xs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(X);
        ref PrimarySample X_i = ref xs[index];

        // Reset $\VEC{X}_i$ if a large step took place in the meantime
        if (X_i.lastModificationIteration < lastLargeStepIteration)
        {
            X_i.value = rng.Uniform<Float>();
            X_i.lastModificationIteration = lastLargeStepIteration;
        }

        // Apply remaining sequence of mutations to _sample_
        X_i.Backup();
        if (largeStep)
            X_i.value = rng.Uniform<Float>();
        else
        {
            long nSmall = currentIteration - X_i.lastModificationIteration;
            // Apply _nSmall_ small step mutations to $\VEC{X}_i$
            Float effSigma = sigma * FMath.Sqrt((Float)nSmall);
            Float delta = SampleNormal(rng.Uniform<Float>(), 0, effSigma);
            X_i.value += delta;
            X_i.value -= FMath.Floor(X_i.value);
        }
        X_i.lastModificationIteration = currentIteration;
    }

    public void Reject()
    {
        Span<PrimarySample> xs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(X);
        for (int i = 0; i < xs.Length; ++i)
            if (xs[i].lastModificationIteration == currentIteration)
                xs[i].Restore();
        --currentIteration;
    }

    public void StartStream(int index)
    {
        DCHECK_LT(index, streamCount);
        streamIndex = index;
        sampleIndex = 0;
    }

    public string DumpState()
    {
        System.Text.StringBuilder state = new();
        foreach (PrimarySample Xi in X)
            state.Append(FloatToString(Xi.value)).Append(',');
        state.Append('0');
        return state.ToString();
    }

    public override string ToString() =>
        $"[ MLTSampler rng: {rng} sigma: {FloatToString(sigma)} largeStepProbability: " +
        $"{FloatToString(largeStepProbability)} streamCount: {streamCount} X: [ {string.Join(", ", X)} ] " +
        $"currentIteration: {currentIteration} largeStep: {(largeStep ? "true" : "false")} " +
        $"lastLargeStepIteration: {lastLargeStepIteration} streamIndex: {streamIndex} sampleIndex: {sampleIndex} ] ";
}

public sealed class DebugMLTSampler : MLTSampler
{
    private readonly List<Float> u = [];

    private DebugMLTSampler(int nSampleStreams) : base(1, 0, 0.5f, 0.5f, nSampleStreams) { }

    public static DebugMLTSampler Create(IReadOnlyList<string> state, int nSampleStreams)
    {
        DebugMLTSampler ds = new(nSampleStreams);
        for (int i = 0; i < state.Count; ++i)
        {
            if (!Atof(state[i], out Float v))
                ErrorExit($"Invalid value in --debugstate: {state[i]}");
            ds.u.Add(v);
        }
        return ds;
    }

    public override Float Get1D()
    {
        int index = GetNextIndex();
        DCHECK_LT(index, u.Count);
        return u[index];
    }

    public override Point2f Get2D()
    {
        Float x = Get1D();
        Float y = Get1D();
        return new Point2f(x, y);
    }

    public override Point2f GetPixel2D() => Get2D();

    public override string ToString() =>
        $"[ DebugMLTSampler {base.ToString()} u: [ {string.Join(", ", u.Select(FloatToString))} ] ]";
}

public abstract partial class Sampler
{
    // Sampler Method Definitions
    public static Sampler Create(string name, ParameterDictionary parameters, Point2i fullRes, FileLoc? loc)
    {
        Sampler? sampler = null;
        if (name == "zsobol")
            sampler = ZSobolSampler.Create(parameters, fullRes, loc);
        // Create remainder of _Sampler_ types
        else if (name == "paddedsobol")
            sampler = PaddedSobolSampler.Create(parameters, loc);
        else if (name == "halton")
            sampler = HaltonSampler.Create(parameters, fullRes, loc);
        else if (name == "sobol")
            sampler = SobolSampler.Create(parameters, fullRes, loc);
        else if (name == "pmj02bn")
            sampler = PMJ02BNSampler.Create(parameters, loc);
        else if (name == "independent")
            sampler = IndependentSampler.Create(parameters, loc);
        else if (name == "stratified")
            sampler = StratifiedSampler.Create(parameters, loc);
        else
            ErrorExit(loc, $"{name}: sampler type unknown.");
        if (sampler == null)
            ErrorExit(loc, $"{name}: unable to create sampler.");
        parameters.ReportUnused();

        return sampler!;
    }
}

public static partial class Util
{
    // Sampler Inline Functions
    public static CameraSample GetCameraSample(Sampler sampler, Point2i pPixel, Filter filter)
    {
        FilterSample fs = filter.Sample(sampler.GetPixel2D());
        CameraSample cs = new();
        // Initialize _CameraSample_ member variables
        cs.pFilm = new Point2f(pPixel) + fs.p + new Vector2f(0.5f, 0.5f);
        cs.time = sampler.Get1D();
        cs.pLens = sampler.Get2D();
        cs.filterWeight = fs.weight;

        if (GetOptions().disablePixelJitter)
        {
            cs.pFilm = new Point2f(pPixel) + new Vector2f(0.5f, 0.5f);
            cs.time = 0.5f;
            cs.pLens = new Point2f(0.5f, 0.5f);
            cs.filterWeight = 1;
        }
        return cs;
    }
}

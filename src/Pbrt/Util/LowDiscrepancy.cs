// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/lowdiscrepancy.h and lowdiscrepancy.cpp.
//
// pbrt's randomizer functors (NoRandomizer, FastOwenScrambler, ...) are
// structs implementing ISobolRandomizer; SobolSample() is generic over them
// so the JIT specializes it per randomizer, like the C++ template.

namespace Pbrt;

// DigitPermutation Definition
public readonly struct DigitPermutation
{
    // DigitPermutation Private Members
    private readonly int @base, nDigits;
    private readonly ushort[] permutations;

    // DigitPermutation Public Methods
    public DigitPermutation(int @base, uint seed)
    {
        this.@base = @base;
        CHECK_LT(@base, 65536); // uint16_t
        // Compute number of digits needed for _base_
        nDigits = 0;
        Float invBase = (Float)1 / (Float)@base, invBaseM = 1;
        while (1 - (@base - 1) * invBaseM < 1)
        {
            ++nDigits;
            invBaseM *= invBase;
        }

        permutations = new ushort[nDigits * @base];
        // Compute random permutations for all digits
        for (int digitIndex = 0; digitIndex < nDigits; ++digitIndex)
        {
            ulong dseed = Hash(@base, digitIndex, seed);
            for (int digitValue = 0; digitValue < @base; ++digitValue)
            {
                int index = digitIndex * @base + digitValue;
                permutations[index] = (ushort)PermutationElement((uint)digitValue, (uint)@base, (uint)dseed);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Permute(int digitIndex, int digitValue)
    {
        DCHECK_LT(digitIndex, nDigits);
        DCHECK_LT(digitValue, @base);
        return permutations[digitIndex * @base + digitValue];
    }

    public override string ToString()
    {
        var s = new System.Text.StringBuilder(
            $"[ DigitPermitation base: {@base} nDigits: {nDigits} permutations: ");
        for (int digitIndex = 0; digitIndex < nDigits; ++digitIndex)
        {
            s.Append($"[{digitIndex}] ( ");
            for (int digitValue = 0; digitValue < @base; ++digitValue)
            {
                s.Append(permutations[digitIndex * @base + digitValue]);
                if (digitValue != @base - 1)
                    s.Append(", ");
            }
            s.Append(") ");
        }
        return s.Append(" ]").ToString();
    }
}

// pbrt's randomizer functor concept: uint32_t operator()(uint32_t v) const
public interface ISobolRandomizer
{
    uint Randomize(uint v);
}

// NoRandomizer Definition
public readonly struct NoRandomizer : ISobolRandomizer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Randomize(uint v) => v;
}

// BinaryPermuteScrambler Definition
public readonly struct BinaryPermuteScrambler(uint perm) : ISobolRandomizer
{
    public readonly uint permutation = perm;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Randomize(uint v) => permutation ^ v;
}

// FastOwenScrambler Definition
public readonly struct FastOwenScrambler(uint seed) : ISobolRandomizer
{
    public readonly uint seed = seed;

    // FastOwenScrambler Public Methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Randomize(uint v)
    {
        v = ReverseBits32(v);
        v ^= v * 0x3d20adea;
        v += seed;
        v *= (seed >> 16) | 1;
        v ^= v * 0x05526c56;
        v ^= v * 0x53a22864;
        return ReverseBits32(v);
    }
}

// OwenScrambler Definition
public readonly struct OwenScrambler(uint seed) : ISobolRandomizer
{
    public readonly uint seed = seed;

    // OwenScrambler Public Methods
    public uint Randomize(uint v)
    {
        if ((seed & 1) != 0)
            v ^= 1u << 31;
        for (int b = 1; b < 32; ++b)
        {
            // Apply Owen scrambling to binary digit _b_ in _v_
            uint mask = (~0u) << (32 - b);
            if (((uint)MixBits((v & mask) ^ seed) & (1u << b)) != 0)
                v ^= 1u << (31 - b);
        }
        return v;
    }
}

// RandomizeStrategy Definition
public enum RandomizeStrategy { None, PermuteDigits, FastOwen, Owen }

public static partial class Util
{
    public static string ToString(RandomizeStrategy r) => r switch
    {
        RandomizeStrategy.None => "None",
        RandomizeStrategy.PermuteDigits => "PermuteDigits",
        RandomizeStrategy.FastOwen => "FastOwen",
        RandomizeStrategy.Owen => "Owen",
        _ => throw new FatalErrorException("Unhandled RandomizeStrategy"),
    };

    // Low Discrepancy Inline Functions
    public static Float RadicalInverse(int baseIndex, ulong a)
    {
        uint @base = (uint)Primes[baseIndex];
        // We have to stop once reversedDigits is >= limit since otherwise the
        // next digit of |a| may cause reversedDigits to overflow.
        ulong limit = ~0ul / @base - @base;
        Float invBase = (Float)1 / (Float)@base, invBaseM = 1;
        ulong reversedDigits = 0;
        while (a != 0 && reversedDigits < limit)
        {
            // Extract least significant digit from _a_ and update _reversedDigits_
            ulong next = a / @base;
            ulong digit = a - next * @base;
            reversedDigits = reversedDigits * @base + digit;
            invBaseM *= invBase;
            a = next;
        }
        return Min(reversedDigits * invBaseM, OneMinusEpsilon);
    }

    public static ulong InverseRadicalInverse(ulong inverse, int @base, int nDigits)
    {
        ulong index = 0;
        for (int i = 0; i < nDigits; ++i)
        {
            ulong digit = inverse % (ulong)@base;
            inverse /= (ulong)@base;
            index = index * (ulong)@base + digit;
        }
        return index;
    }

    public static Float ScrambledRadicalInverse(int baseIndex, ulong a, in DigitPermutation perm)
    {
        uint @base = (uint)Primes[baseIndex];
        // We have to stop once reversedDigits is >= limit since otherwise the
        // next digit of |a| may cause reversedDigits to overflow.
        ulong limit = ~0ul / @base - @base;
        Float invBase = (Float)1 / (Float)@base, invBaseM = 1;
        ulong reversedDigits = 0;
        int digitIndex = 0;
        while (1 - (@base - 1) * invBaseM < 1 && reversedDigits < limit)
        {
            // Permute least significant digit from _a_ and update _reversedDigits_
            ulong next = a / @base;
            int digitValue = (int)(a - next * @base);
            reversedDigits = reversedDigits * @base + (ulong)perm.Permute(digitIndex, digitValue);
            invBaseM *= invBase;
            ++digitIndex;
            a = next;
        }
        return Min(invBaseM * reversedDigits, OneMinusEpsilon);
    }

    public static Float OwenScrambledRadicalInverse(int baseIndex, ulong a, uint hash)
    {
        uint @base = (uint)Primes[baseIndex];
        // We have to stop once reversedDigits is >= limit since otherwise the
        // next digit of |a| may cause reversedDigits to overflow.
        ulong limit = ~0ul / @base - @base;
        Float invBase = (Float)1 / (Float)@base, invBaseM = 1;
        ulong reversedDigits = 0;
        int digitIndex = 0;
        while (1 - invBaseM < 1 && reversedDigits < limit)
        {
            // Compute Owen-scrambled digit for _digitIndex_
            ulong next = a / @base;
            int digitValue = (int)(a - next * @base);
            uint digitHash = (uint)MixBits(hash ^ reversedDigits);
            digitValue = PermutationElement((uint)digitValue, @base, digitHash);
            reversedDigits = reversedDigits * @base + (ulong)digitValue;
            invBaseM *= invBase;
            ++digitIndex;
            a = next;
        }
        return Min(invBaseM * reversedDigits, OneMinusEpsilon);
    }

    public static uint MultiplyGenerator(ReadOnlySpan<uint> C, uint a)
    {
        uint v = 0;
        for (int i = 0; a != 0; ++i, a >>= 1)
            if ((a & 1) != 0)
                v ^= C[i];
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Float SobolSample<R>(long a, int dimension, R randomizer) where R : struct, ISobolRandomizer
    {
        DCHECK_LT(dimension, NSobolDimensions);
        DCHECK(a >= 0 && (ulong)a < (1ul << SobolMatrixSize));
        // Compute initial Sobol\+$'$ sample _v_ using generator matrices
        ReadOnlySpan<uint> sobolMatrices = SobolMatrices32;
        uint v = 0;
        for (int i = dimension * SobolMatrixSize; a != 0; a >>= 1, i++)
            if ((a & 1) != 0)
                v ^= sobolMatrices[i];

        // Randomize Sobol\+$'$ sample and return floating-point value
        v = randomizer.Randomize(v);
        return Min(v * OneOver2Pow32, FloatOneMinusEpsilon);
    }

    private static ReadOnlySpan<byte> BlueNoisePermutations =>
    [
        0, 1, 2, 3, 0, 1, 3, 2, 0, 2, 1, 3, 0, 2, 3, 1, 0, 3, 2, 1,
        0, 3, 1, 2, 1, 0, 2, 3, 1, 0, 3, 2, 1, 2, 0, 3, 1, 2, 3, 0,
        1, 3, 2, 0, 1, 3, 0, 2, 2, 1, 0, 3, 2, 1, 3, 0, 2, 0, 1, 3,
        2, 0, 3, 1, 2, 3, 0, 1, 2, 3, 1, 0, 3, 1, 2, 0, 3, 1, 0, 2,
        3, 2, 1, 0, 3, 2, 0, 1, 3, 0, 2, 1, 3, 0, 1, 2,
    ];

    public static Float BlueNoiseSample(Point2i p, int instance)
    {
        int HashPerm(ulong index) =>
            (int)((uint)(MixBits(index ^ (ulong)(long)(0x55555555 * instance)) >> 24) % 24);

        int nBase4Digits = 8; // Log2Int(256)
        p.x &= 255;
        p.y &= 255;
        ulong mortonIndex = EncodeMorton2((uint)p.x, (uint)p.y);

        // permutations[24][4]
        ReadOnlySpan<byte> permutations = BlueNoisePermutations;

        uint sampleIndex = 0;
        for (int i = nBase4Digits - 1; i >= 0; --i)
        {
            int digitShift = 2 * i;
            int digit = (int)((mortonIndex >> digitShift) & 3);
            int pi = HashPerm(mortonIndex >> (digitShift + 2));
            digit = permutations[pi * 4 + digit];
            sampleIndex |= (uint)(digit << digitShift);
        }

        return ReverseBits32(sampleIndex) * OneOver2Pow32;
    }

    public static ulong SobolIntervalToIndex(uint m, ulong frame, Point2i p)
    {
        if (m == 0)
            return frame;

        uint m2 = m << 1;
        ulong index = frame << (int)m2;

        // VdCSobolMatrices[m - 1][c]
        ReadOnlySpan<ulong> vdc = VdCSobolMatrices.Slice((int)(m - 1) * SobolMatrixSize, SobolMatrixSize);
        ReadOnlySpan<ulong> vdcInv = VdCSobolMatricesInv.Slice((int)(m - 1) * SobolMatrixSize, SobolMatrixSize);

        ulong delta = 0;
        for (int c = 0; frame != 0; frame >>= 1, ++c)
            if ((frame & 1) != 0) // Add flipped column m + c + 1.
                delta ^= vdc[c];

        // flipped b
        ulong b = (((ulong)(uint)p.x << (int)m) | (uint)p.y) ^ delta;

        for (int c = 0; b != 0; b >>= 1, ++c)
            if ((b & 1) != 0) // Add column 2 * m - c.
                index ^= vdcInv[c];

        return index;
    }

    // Low Discrepancy Function Definitions
    public static DigitPermutation[] ComputeRadicalInversePermutations(uint seed)
    {
        var perms = new DigitPermutation[PrimeTableSize];
        for (int i = 0; i < PrimeTableSize; ++i)
            perms[i] = new DigitPermutation(Primes[i], seed);
        return perms;
    }
}

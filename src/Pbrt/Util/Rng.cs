// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/rng.h
//
// NOTE: `new RNG()` matches pbrt's default constructor, but `default(RNG)`
// (and zero-initialized fields/arrays) has zero state and increment.

namespace Pbrt;

// RNG Definition
public struct RNG
{
    // Random Number Declarations
    public const ulong PCG32_DEFAULT_STATE = 0x853c49e6748fea9bUL;
    public const ulong PCG32_DEFAULT_STREAM = 0xda3e39cb94b95bdbUL;
    public const ulong PCG32_MULT = 0x5851f42d4c957f2dUL;

    // RNG Private Members
    private ulong state, inc;

    // RNG Public Methods
    public RNG()
    {
        state = PCG32_DEFAULT_STATE;
        inc = PCG32_DEFAULT_STREAM;
    }

    public RNG(ulong seqIndex, ulong offset) => SetSequence(seqIndex, offset);

    public RNG(ulong seqIndex) => SetSequence(seqIndex);

    public void SetSequence(ulong sequenceIndex, ulong seed)
    {
        state = 0u;
        inc = (sequenceIndex << 1) | 1u;
        UniformUInt32();
        state += seed;
        UniformUInt32();
    }

    public void SetSequence(ulong sequenceIndex) => SetSequence(sequenceIndex, MixBits(sequenceIndex));

    // pbrt's Uniform<T>(); the typeof checks are resolved by the JIT.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Uniform<T>()
    {
        if (typeof(T) == typeof(uint)) return (T)(object)UniformUInt32();
        if (typeof(T) == typeof(ulong)) return (T)(object)UniformUInt64();
        if (typeof(T) == typeof(int)) return (T)(object)UniformInt32();
        if (typeof(T) == typeof(long)) return (T)(object)UniformInt64();
        if (typeof(T) == typeof(float)) return (T)(object)UniformFloat();
        if (typeof(T) == typeof(double)) return (T)(object)UniformDouble();
        throw new NotSupportedException($"RNG.Uniform<{typeof(T)}>");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint UniformUInt32()
    {
        ulong oldstate = state;
        state = oldstate * PCG32_MULT + inc;
        uint xorshifted = (uint)(((oldstate >> 18) ^ oldstate) >> 27);
        uint rot = (uint)(oldstate >> 59);
        return (xorshifted >> (int)rot) | (xorshifted << (int)((~rot + 1u) & 31));
    }

    public ulong UniformUInt64()
    {
        ulong v0 = UniformUInt32(), v1 = UniformUInt32();
        return (v0 << 32) | v1;
    }

    public int UniformInt32()
    {
        // https://stackoverflow.com/a/13208789
        uint v = UniformUInt32();
        if (v <= int.MaxValue)
            return (int)v;
        DCHECK_GE(v, unchecked((uint)int.MinValue));
        return (int)(v - unchecked((uint)int.MinValue)) + int.MinValue;
    }

    public long UniformInt64()
    {
        // https://stackoverflow.com/a/13208789
        ulong v = UniformUInt64();
        if (v <= long.MaxValue)
            // Safe to type convert directly.
            return (long)v;
        DCHECK_GE(v, unchecked((ulong)long.MinValue));
        return (long)(v - unchecked((ulong)long.MinValue)) + long.MinValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float UniformFloat() => Min(FloatOneMinusEpsilon, UniformUInt32() * OneOver2Pow32);

    public double UniformDouble() => Min(DoubleOneMinusEpsilon, UniformUInt64() * OneOver2Pow64);

    // Uniform<T>(b): uniformly distributed in [0, b).
    public uint Uniform(uint b)
    {
        uint threshold = (~b + 1u) % b;
        while (true)
        {
            uint r = UniformUInt32();
            if (r >= threshold)
                return r % b;
        }
    }

    public ulong Uniform(ulong b)
    {
        ulong threshold = (~b + 1u) % b;
        while (true)
        {
            ulong r = UniformUInt64();
            if (r >= threshold)
                return r % b;
        }
    }

    // Same (unsigned) threshold computation as pbrt's Uniform<int>(b).
    public int Uniform(int b)
    {
        int threshold = (int)((~(uint)b + 1u) % (uint)b);
        while (true)
        {
            int r = UniformInt32();
            if (r >= threshold)
                return r % b;
        }
    }

    public void Advance(long idelta)
    {
        ulong curMult = PCG32_MULT, curPlus = inc, accMult = 1u;
        ulong accPlus = 0u, delta = (ulong)idelta;
        while (delta > 0)
        {
            if ((delta & 1) != 0)
            {
                accMult *= curMult;
                accPlus = accPlus * curMult + curPlus;
            }
            curPlus = (curMult + 1) * curPlus;
            curMult *= curMult;
            delta /= 2;
        }
        state = accMult * state + accPlus;
    }

    public static long operator -(RNG a, RNG other)
    {
        CHECK_EQ(a.inc, other.inc);
        ulong curMult = PCG32_MULT, curPlus = a.inc, curState = other.state;
        ulong theBit = 1u, distance = 0u;
        while (a.state != curState)
        {
            if ((a.state & theBit) != (curState & theBit))
            {
                curState = curState * curMult + curPlus;
                distance |= theBit;
            }
            CHECK_EQ(a.state & theBit, curState & theBit);
            theBit <<= 1;
            curPlus = (curMult + 1UL) * curPlus;
            curMult *= curMult;
        }
        return (long)distance;
    }

    public override readonly string ToString() => $"[ RNG state: {state} inc: {inc} ]";
}

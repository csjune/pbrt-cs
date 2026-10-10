// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/buffercache.h and buffercache.cpp
//
// LookupOrAdd() returns a shared array with the buffer's contents (pbrt
// returns a pointer to its copy). The global caches are created on first
// use instead of by InitBufferCaches().

using System.Runtime.InteropServices;

namespace Pbrt;

// BufferCache Definition
public sealed class BufferCache<T> where T : unmanaged
{
    private static readonly StatMemoryCounter redundantBufferBytes = new("Memory/Redundant vertex and index buffers");
    private static readonly StatPercent nBufferCacheHits = new("Geometry/Buffer cache hits");

    // BufferCache::Buffer Definition
    private readonly struct Buffer(T[] data, ulong hash) : IEquatable<Buffer>
    {
        public readonly T[] data = data;
        public readonly ulong hash = hash;

        public bool Equals(Buffer b) =>
            data.Length == b.data.Length && hash == b.hash &&
            MemoryMarshal.AsBytes(data.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.data.AsSpan()));

        public override bool Equals(object? obj) => obj is Buffer b && Equals(b);
        public override int GetHashCode() => (int)hash;
    }

    // BufferCache Private Members
    private const int logShards = 6;
    private const int nShards = 1 << logShards;
    private readonly ReaderWriterLockSlim[] mutex = new ReaderWriterLockSlim[nShards];
    private readonly HashSet<Buffer>[] cache = new HashSet<Buffer>[nShards];
    private long bytesUsed;

    public BufferCache()
    {
        for (int i = 0; i < nShards; ++i)
        {
            mutex[i] = new ReaderWriterLockSlim();
            cache[i] = [];
        }
    }

    // BufferCache Public Methods
    public unsafe T[] LookupOrAdd(ReadOnlySpan<T> buf)
    {
        nBufferCacheHits.Denom();
        // Return the cached array if _buf_ contents are already in the cache
        ulong hash = HashBuffer(buf);
        // (The lookup key needs an array; it's only kept if it's added.)
        T[] copy = buf.ToArray();
        Buffer lookupBuffer = new(copy, hash);
        int shardIndex = (int)((uint)hash >> (32 - logShards));
        DCHECK(shardIndex >= 0 && shardIndex < nShards);
        mutex[shardIndex].EnterReadLock();
        try
        {
            if (cache[shardIndex].TryGetValue(lookupBuffer, out Buffer found))
            {
                nBufferCacheHits.Num();
                redundantBufferBytes.Add(buf.Length * sizeof(T));
                return found.data;
            }
        }
        finally
        {
            mutex[shardIndex].ExitReadLock();
        }

        // Add _buf_ contents to cache and return the cached copy
        mutex[shardIndex].EnterWriteLock();
        try
        {
            // Handle the case of another thread adding the buffer first
            if (cache[shardIndex].TryGetValue(lookupBuffer, out Buffer found))
                return found.data;

            Interlocked.Add(ref bytesUsed, buf.Length * sizeof(T));
            cache[shardIndex].Add(lookupBuffer);
            return copy;
        }
        finally
        {
            mutex[shardIndex].ExitWriteLock();
        }
    }

    public long BytesUsed() => Interlocked.Read(ref bytesUsed);
}

// BufferCache Global Definitions
public static class BufferCaches
{
    public static readonly BufferCache<int> intBufferCache = new();
    public static readonly BufferCache<Point2f> point2BufferCache = new();
    public static readonly BufferCache<Point3f> point3BufferCache = new();
    public static readonly BufferCache<Vector3f> vector3BufferCache = new();
    public static readonly BufferCache<Normal3f> normal3BufferCache = new();
}

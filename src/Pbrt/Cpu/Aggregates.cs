// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cpu/aggregates.h and aggregates.cpp
//
// The build nodes are GC-allocated objects instead of coming from pbrt's
// per-thread allocators, and pointers into the node and primitive arrays
// become indices. std::partition, std::nth_element and std::sort are
// Util.StdPartition/StdNthElement/StdSort; where primitives tie (equal
// centroids or split positions), their order, and so the tree, can differ
// from pbrt's built with a given C++ standard library. As in pbrt, the order
// of the primitives in the BVH leaves depends on thread timing when
// subtrees are built in parallel (HLBVH, or SAH/Middle/EqualCounts with
// more than 128k primitives); it is deterministic with one thread.

using System.Runtime.CompilerServices;

namespace Pbrt;

// MortonPrimitive Definition
internal struct MortonPrimitive
{
    public int primitiveIndex;
    public uint mortonCode;
}

// LBVHTreelet Definition
internal struct LBVHTreelet
{
    public int startIndex, nPrimitives;
    public BVHBuildNode? buildNodes;
}

// BVHSplitBucket Definition
internal struct BVHSplitBucket
{
    public int count;
    public Bounds3f bounds;
}

// BVHPrimitive Definition
internal struct BVHPrimitive
{
    public int primitiveIndex;
    public Bounds3f bounds;

    public BVHPrimitive(int primitiveIndex, in Bounds3f bounds)
    {
        this.primitiveIndex = primitiveIndex;
        this.bounds = bounds;
    }

    // BVHPrimitive Public Methods
    public readonly Point3f Centroid() => .5f * bounds.pMin + .5f * bounds.pMax;
}

// BVHBuildNode Definition
internal sealed class BVHBuildNode
{
    public Bounds3f bounds;
    public BVHBuildNode? child0, child1;
    public int splitAxis, firstPrimOffset, nPrimitives;

    // BVHBuildNode Public Methods
    public void InitLeaf(int first, int n, in Bounds3f b)
    {
        firstPrimOffset = first;
        nPrimitives = n;
        bounds = b;
        child0 = child1 = null;
        BVHAggregate.leafNodes.Inc();
        BVHAggregate.totalLeafNodes.Denom();
        BVHAggregate.totalLeafNodes.Num(n);
    }

    public void InitInterior(int axis, BVHBuildNode c0, BVHBuildNode c1)
    {
        child0 = c0;
        child1 = c1;
        bounds = Union(c0.bounds, c1.bounds);
        splitAxis = axis;
        nPrimitives = 0;
        BVHAggregate.interiorNodes.Inc();
    }
}

// LinearBVHNode Definition
internal struct LinearBVHNode
{
    public Bounds3f bounds;
    // primitivesOffset (leaf) or secondChildOffset (interior)
    public int offset;
    public ushort nPrimitives;  // 0 -> interior node
    public byte axis;           // interior node: xyz
}

// BVHAggregate Definition
public sealed class BVHAggregate : Primitive
{
    private static readonly StatMemoryCounter treeBytes = new("Memory/BVH");
    internal static readonly StatRatio totalLeafNodes = new("BVH/Primitives per leaf node");
    internal static readonly StatCounter interiorNodes = new("BVH/Interior nodes");
    internal static readonly StatCounter leafNodes = new("BVH/Leaf nodes");
    private static readonly StatPixelCounter bvhNodesVisited = new("BVH/Nodes visited");

    public enum SplitMethod { SAH, HLBVH, Middle, EqualCounts }

    // BVHAggregate Private Members
    private readonly int maxPrimsInNode;
    private Primitive[] primitives;
    private readonly SplitMethod splitMethod;
    private readonly LinearBVHNode[]? nodes;

    // BVHAggregate Method Definitions
    public BVHAggregate(IReadOnlyList<Primitive> prims, int maxPrimsInNode = 1,
                        SplitMethod splitMethod = SplitMethod.SAH)
    {
        this.maxPrimsInNode = Math.Min(255, maxPrimsInNode);
        primitives = [.. prims];
        this.splitMethod = splitMethod;
        CHECK(primitives.Length != 0);
        // Build BVH from _primitives_
        // Initialize _bvhPrimitives_ array for primitives
        BVHPrimitive[] bvhPrimitives = new BVHPrimitive[primitives.Length];
        for (int i = 0; i < primitives.Length; ++i)
            bvhPrimitives[i] = new BVHPrimitive(i, primitives[i].Bounds());

        // Build BVH for primitives using _bvhPrimitives_
        Primitive[] orderedPrims = new Primitive[primitives.Length];
        BVHBuildNode root;
        // Build BVH according to selected _splitMethod_
        StrongBox<int> totalNodes = new(0);
        if (splitMethod == SplitMethod.HLBVH)
            root = BuildHLBVH(bvhPrimitives, totalNodes, orderedPrims);
        else
        {
            StrongBox<int> orderedPrimsOffset = new(0);
            root = BuildRecursive(bvhPrimitives, totalNodes, orderedPrimsOffset, orderedPrims);
            CHECK_EQ(orderedPrimsOffset.Value, orderedPrims.Length);
        }
        primitives = orderedPrims;

        // Convert BVH into compact representation in _nodes_ array
        LOG_VERBOSE($"BVH created with {totalNodes.Value} nodes for {primitives.Length} primitives ({(float)(totalNodes.Value * 32) / (1024f * 1024f):F2} MB)");
        // (sizeof(LinearBVHNode), sizeof(BVHAggregate) and sizeof(Primitive)
        // in pbrt)
        treeBytes.Add(totalNodes.Value * 32L + 48 + primitives.Length * 8L);
        nodes = new LinearBVHNode[totalNodes.Value];
        int offset = 0;
        FlattenBVH(root, ref offset);
        CHECK_EQ(totalNodes.Value, offset);
    }

    private struct MiddlePredicate(int dim, Float pmid) : IStdPredicate<BVHPrimitive>
    {
        public readonly bool Evaluate(in BVHPrimitive pi) => pi.Centroid()[dim] < pmid;
    }

    private struct CentroidLess(int dim) : IStdLess<BVHPrimitive>
    {
        public readonly bool Less(in BVHPrimitive a, in BVHPrimitive b) => a.Centroid()[dim] < b.Centroid()[dim];
    }

    private struct SAHPredicate(int nBuckets, Bounds3f centroidBounds, int dim, int minCostSplitBucket)
        : IStdPredicate<BVHPrimitive>
    {
        public readonly bool Evaluate(in BVHPrimitive bp)
        {
            int b = (int)(nBuckets * centroidBounds.Offset(bp.Centroid())[dim]);
            if (b == nBuckets)
                b = nBuckets - 1;
            return b <= minCostSplitBucket;
        }
    }

    private BVHBuildNode MakeLeaf(Span<BVHPrimitive> bvhPrimitives, StrongBox<int> orderedPrimsOffset,
                                  Primitive[] orderedPrims, BVHBuildNode node, in Bounds3f bounds)
    {
        // Create leaf _BVHBuildNode_
        int firstPrimOffset = Interlocked.Add(ref orderedPrimsOffset.Value, bvhPrimitives.Length) -
                              bvhPrimitives.Length;
        for (int i = 0; i < bvhPrimitives.Length; ++i)
        {
            int index = bvhPrimitives[i].primitiveIndex;
            orderedPrims[firstPrimOffset + i] = primitives[index];
        }
        node.InitLeaf(firstPrimOffset, bvhPrimitives.Length, bounds);
        return node;
    }

    private BVHBuildNode BuildRecursive(Memory<BVHPrimitive> bvhPrimitivesMemory, StrongBox<int> totalNodes,
                                        StrongBox<int> orderedPrimsOffset, Primitive[] orderedPrims)
    {
        Span<BVHPrimitive> bvhPrimitives = bvhPrimitivesMemory.Span;
        DCHECK_NE(bvhPrimitives.Length, 0);
        BVHBuildNode node = new();
        // Initialize _BVHBuildNode_ for primitive range
        Interlocked.Increment(ref totalNodes.Value);
        // Compute bounds of all primitives in BVH node
        Bounds3f bounds = new();
        foreach (ref readonly BVHPrimitive prim in bvhPrimitives)
            bounds = Union(bounds, prim.bounds);

        if (bounds.SurfaceArea() == 0 || bvhPrimitives.Length == 1)
            return MakeLeaf(bvhPrimitives, orderedPrimsOffset, orderedPrims, node, bounds);

        // Compute bound of primitive centroids and choose split dimension _dim_
        Bounds3f centroidBounds = new();
        foreach (ref readonly BVHPrimitive prim in bvhPrimitives)
            centroidBounds = Union(centroidBounds, prim.Centroid());
        int dim = centroidBounds.MaxDimension();

        // Partition primitives into two sets and build children
        if (centroidBounds.pMax[dim] == centroidBounds.pMin[dim])
            return MakeLeaf(bvhPrimitives, orderedPrimsOffset, orderedPrims, node, bounds);

        int mid = bvhPrimitives.Length / 2;
        // Partition primitives based on _splitMethod_
        switch (splitMethod)
        {
            case SplitMethod.Middle:
            case SplitMethod.EqualCounts:
            {
                if (splitMethod == SplitMethod.Middle)
                {
                    // Partition primitives through node's midpoint
                    Float pmid = (centroidBounds.pMin[dim] + centroidBounds.pMax[dim]) / 2;
                    mid = StdPartition(bvhPrimitives, new MiddlePredicate(dim, pmid));
                    // For lots of prims with large overlapping bounding boxes, this
                    // may fail to partition; in that case do not break and fall through
                    // to EqualCounts.
                    if (mid != 0 && mid != bvhPrimitives.Length)
                        break;
                }
                // Partition primitives into equally sized subsets
                mid = bvhPrimitives.Length / 2;
                StdNthElement(bvhPrimitives, mid, new CentroidLess(dim));
                break;
            }
            case SplitMethod.SAH:
            default:
            {
                // Partition primitives using approximate SAH
                if (bvhPrimitives.Length <= 2)
                {
                    // Partition primitives into equally sized subsets
                    mid = bvhPrimitives.Length / 2;
                    StdNthElement(bvhPrimitives, mid, new CentroidLess(dim));
                }
                else
                {
                    // Allocate _BVHSplitBucket_ for SAH partition buckets
                    const int nBuckets = 12;
                    Span<BVHSplitBucket> buckets = stackalloc BVHSplitBucket[nBuckets];
                    for (int i = 0; i < nBuckets; ++i)
                        buckets[i].bounds = new Bounds3f();

                    // Initialize _BVHSplitBucket_ for SAH partition buckets
                    foreach (ref readonly BVHPrimitive prim in bvhPrimitives)
                    {
                        int b = (int)(nBuckets * centroidBounds.Offset(prim.Centroid())[dim]);
                        if (b == nBuckets)
                            b = nBuckets - 1;
                        DCHECK_GE(b, 0);
                        DCHECK_LT(b, nBuckets);
                        buckets[b].count++;
                        buckets[b].bounds = Union(buckets[b].bounds, prim.bounds);
                    }

                    // Compute costs for splitting after each bucket
                    const int nSplits = nBuckets - 1;
                    Span<Float> costs = stackalloc Float[nSplits];
                    costs.Clear();
                    // Partially initialize _costs_ using a forward scan over splits
                    int countBelow = 0;
                    Bounds3f boundBelow = new();
                    for (int i = 0; i < nSplits; ++i)
                    {
                        boundBelow = Union(boundBelow, buckets[i].bounds);
                        countBelow += buckets[i].count;
                        costs[i] += countBelow * boundBelow.SurfaceArea();
                    }

                    // Finish initializing _costs_ using a backward scan over splits
                    int countAbove = 0;
                    Bounds3f boundAbove = new();
                    for (int i = nSplits; i >= 1; --i)
                    {
                        boundAbove = Union(boundAbove, buckets[i].bounds);
                        countAbove += buckets[i].count;
                        costs[i - 1] += countAbove * boundAbove.SurfaceArea();
                    }

                    // Find bucket to split at that minimizes SAH metric
                    int minCostSplitBucket = -1;
                    Float minCost = Infinity;
                    for (int i = 0; i < nSplits; ++i)
                    {
                        // Compute cost for candidate split and update minimum if
                        // necessary
                        if (costs[i] < minCost)
                        {
                            minCost = costs[i];
                            minCostSplitBucket = i;
                        }
                    }
                    // Compute leaf cost and SAH split cost for chosen split
                    Float leafCost = bvhPrimitives.Length;
                    minCost = 1f / 2f + minCost / bounds.SurfaceArea();

                    // Either create leaf or split primitives at selected SAH bucket
                    if (bvhPrimitives.Length > maxPrimsInNode || minCost < leafCost)
                        mid = StdPartition(bvhPrimitives,
                                           new SAHPredicate(nBuckets, centroidBounds, dim, minCostSplitBucket));
                    else
                        return MakeLeaf(bvhPrimitives, orderedPrimsOffset, orderedPrims, node, bounds);
                }
                break;
            }
        }

        BVHBuildNode child0, child1;
        // Recursively build BVHs for _children_
        if (bvhPrimitives.Length > 128 * 1024)
        {
            // Recursively build child BVHs in parallel
            BVHBuildNode? c0 = null, c1 = null;
            ParallelFor(0, 2, (long i) =>
            {
                if (i == 0)
                    c0 = BuildRecursive(bvhPrimitivesMemory[..mid], totalNodes, orderedPrimsOffset,
                                        orderedPrims);
                else
                    c1 = BuildRecursive(bvhPrimitivesMemory[mid..], totalNodes, orderedPrimsOffset,
                                        orderedPrims);
            });
            child0 = c0!;
            child1 = c1!;
        }
        else
        {
            // Recursively build child BVHs sequentially
            child0 = BuildRecursive(bvhPrimitivesMemory[..mid], totalNodes, orderedPrimsOffset, orderedPrims);
            child1 = BuildRecursive(bvhPrimitivesMemory[mid..], totalNodes, orderedPrimsOffset, orderedPrims);
        }

        node.InitInterior(dim, child0, child1);
        return node;
    }

    // BVHAggregate Utility Functions
    private static void RadixSort(ref MortonPrimitive[] v)
    {
        MortonPrimitive[] tempVector = new MortonPrimitive[v.Length];
        const int bitsPerPass = 6;
        const int nBits = 30;
        const int nPasses = nBits / bitsPerPass;
        const int nBuckets = 1 << bitsPerPass;
        Span<int> bucketCount = stackalloc int[nBuckets];
        Span<int> outIndex = stackalloc int[nBuckets];
        for (int pass = 0; pass < nPasses; ++pass)
        {
            // Perform one pass of radix sort, sorting _bitsPerPass_ bits
            int lowBit = pass * bitsPerPass;
            // Set in and out vector references for radix sort pass
            MortonPrimitive[] @in = (pass & 1) != 0 ? tempVector : v;
            MortonPrimitive[] @out = (pass & 1) != 0 ? v : tempVector;

            // Count number of zero bits in array for current radix sort bit
            bucketCount.Clear();
            const int bitMask = (1 << bitsPerPass) - 1;
            foreach (ref readonly MortonPrimitive mp in @in.AsSpan())
            {
                int bucket = (int)(mp.mortonCode >> lowBit) & bitMask;
                CHECK_GE(bucket, 0);
                CHECK_LT(bucket, nBuckets);
                ++bucketCount[bucket];
            }

            // Compute starting index in output array for each bucket
            outIndex[0] = 0;
            for (int i = 1; i < nBuckets; ++i)
                outIndex[i] = outIndex[i - 1] + bucketCount[i - 1];

            // Store sorted values in output array
            foreach (ref readonly MortonPrimitive mp in @in.AsSpan())
            {
                int bucket = (int)(mp.mortonCode >> lowBit) & bitMask;
                @out[outIndex[bucket]++] = mp;
            }
        }
        // Copy final result from _tempVector_, if needed
        if ((nPasses & 1) != 0)
            (v, tempVector) = (tempVector, v);
    }

    private BVHBuildNode BuildHLBVH(BVHPrimitive[] bvhPrimitives, StrongBox<int> totalNodes,
                                    Primitive[] orderedPrims)
    {
        // Compute bounding box of all primitive centroids
        Bounds3f bounds = new();
        foreach (ref readonly BVHPrimitive prim in bvhPrimitives.AsSpan())
            bounds = Union(bounds, prim.Centroid());

        // Compute Morton indices of primitives
        MortonPrimitive[] mortonPrims = new MortonPrimitive[bvhPrimitives.Length];
        ParallelFor(0, bvhPrimitives.Length, (long i) =>
        {
            // Initialize _mortonPrims[i]_ for _i_th primitive
            const int mortonBits = 10;
            const int mortonScale = 1 << mortonBits;
            mortonPrims[i].primitiveIndex = bvhPrimitives[i].primitiveIndex;
            Vector3f centroidOffset = bounds.Offset(bvhPrimitives[i].Centroid());
            Vector3f offset = centroidOffset * mortonScale;
            mortonPrims[i].mortonCode = EncodeMorton3((float)offset.x, (float)offset.y, (float)offset.z);
        });

        // Radix sort primitive Morton indices
        RadixSort(ref mortonPrims);

        // Create LBVH treelets at bottom of BVH
        // Find intervals of primitives for each treelet
        List<LBVHTreelet> treeletsToBuild = [];
        for (int start = 0, end = 1; end <= mortonPrims.Length; ++end)
        {
            uint mask = 0b00111111111111000000000000000000;
            if (end == mortonPrims.Length ||
                ((mortonPrims[start].mortonCode & mask) != (mortonPrims[end].mortonCode & mask)))
            {
                // Add entry to _treeletsToBuild_ for this treelet
                int nPrimitives = end - start;
                treeletsToBuild.Add(new LBVHTreelet { startIndex = start, nPrimitives = nPrimitives });

                start = end;
            }
        }

        // Create LBVHs for treelets in parallel
        StrongBox<int> orderedPrimsOffset = new(0);
        LBVHTreelet[] treelets = [.. treeletsToBuild];
        ParallelFor(0, treelets.Length, (long i) =>
        {
            // Generate _i_th LBVH treelet
            int nodesCreated = 0;
            const int firstBitIndex = 29 - 12;
            ref LBVHTreelet tr = ref treelets[i];
            tr.buildNodes = EmitLBVH(bvhPrimitives, mortonPrims, tr.startIndex, tr.nPrimitives, ref nodesCreated,
                                     orderedPrims, orderedPrimsOffset, firstBitIndex);
            Interlocked.Add(ref totalNodes.Value, nodesCreated);
        });

        // Create and return SAH BVH from LBVH treelets
        BVHBuildNode[] finishedTreelets = new BVHBuildNode[treelets.Length];
        for (int i = 0; i < treelets.Length; ++i)
            finishedTreelets[i] = treelets[i].buildNodes!;
        return BuildUpperSAH(finishedTreelets, 0, finishedTreelets.Length, totalNodes);
    }

    private BVHBuildNode EmitLBVH(BVHPrimitive[] bvhPrimitives, MortonPrimitive[] mortonPrimsArray,
                                  int mortonStart, int nPrimitives, ref int totalNodes, Primitive[] orderedPrims,
                                  StrongBox<int> orderedPrimsOffset, int bitIndex)
    {
        ReadOnlySpan<MortonPrimitive> mortonPrims = mortonPrimsArray.AsSpan(mortonStart);
        CHECK_GT(nPrimitives, 0);
        if (bitIndex == -1 || nPrimitives < maxPrimsInNode)
        {
            // Create and return leaf node of LBVH treelet
            ++totalNodes;
            BVHBuildNode node = new();
            Bounds3f bounds = new();
            int firstPrimOffset = Interlocked.Add(ref orderedPrimsOffset.Value, nPrimitives) - nPrimitives;
            for (int i = 0; i < nPrimitives; ++i)
            {
                int primitiveIndex = mortonPrims[i].primitiveIndex;
                orderedPrims[firstPrimOffset + i] = primitives[primitiveIndex];
                bounds = Union(bounds, bvhPrimitives[primitiveIndex].bounds);
            }
            node.InitLeaf(firstPrimOffset, nPrimitives, bounds);
            return node;
        }
        else
        {
            uint mask = 1u << bitIndex;
            // Advance to next subtree level if there is no LBVH split for this bit
            if ((mortonPrims[0].mortonCode & mask) == (mortonPrims[nPrimitives - 1].mortonCode & mask))
                return EmitLBVH(bvhPrimitives, mortonPrimsArray, mortonStart, nPrimitives, ref totalNodes,
                                orderedPrims, orderedPrimsOffset, bitIndex - 1);

            // Find LBVH split point for this dimension
            int splitOffset = FindInterval(nPrimitives,
                index => (mortonPrimsArray[mortonStart].mortonCode & mask) ==
                         (mortonPrimsArray[mortonStart + index].mortonCode & mask));
            ++splitOffset;
            CHECK_LE(splitOffset, nPrimitives - 1);
            CHECK_NE(mortonPrims[splitOffset - 1].mortonCode & mask, mortonPrims[splitOffset].mortonCode & mask);

            // Create and return interior LBVH node
            totalNodes++;
            BVHBuildNode node = new();
            BVHBuildNode lbvh0 = EmitLBVH(bvhPrimitives, mortonPrimsArray, mortonStart, splitOffset,
                                          ref totalNodes, orderedPrims, orderedPrimsOffset, bitIndex - 1);
            BVHBuildNode lbvh1 = EmitLBVH(bvhPrimitives, mortonPrimsArray, mortonStart + splitOffset,
                                          nPrimitives - splitOffset, ref totalNodes, orderedPrims,
                                          orderedPrimsOffset, bitIndex - 1);
            int axis = bitIndex % 3;
            node.InitInterior(axis, lbvh0, lbvh1);
            return node;
        }
    }

    private int FlattenBVH(BVHBuildNode node, ref int offset)
    {
        ref LinearBVHNode linearNode = ref nodes![offset];
        linearNode.bounds = node.bounds;
        int nodeOffset = offset++;
        if (node.nPrimitives > 0)
        {
            CHECK(node.child0 == null && node.child1 == null);
            CHECK_LT(node.nPrimitives, 65536);
            linearNode.offset = node.firstPrimOffset;
            linearNode.nPrimitives = (ushort)node.nPrimitives;
        }
        else
        {
            // Create interior flattened BVH node
            linearNode.axis = (byte)node.splitAxis;
            linearNode.nPrimitives = 0;
            FlattenBVH(node.child0!, ref offset);
            linearNode.offset = FlattenBVH(node.child1!, ref offset);
        }
        return nodeOffset;
    }

    public override Bounds3f Bounds()
    {
        CHECK(nodes != null);
        return nodes![0].bounds;
    }

    public override ShapeIntersection? Intersect(in Ray ray, Float tMax = Infinity)
    {
        if (nodes == null)
            return null;
        ShapeIntersection? si = null;
        Vector3f invDir = new(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
        Span<int> dirIsNeg = [invDir.x < 0 ? 1 : 0, invDir.y < 0 ? 1 : 0, invDir.z < 0 ? 1 : 0];
        // Follow ray through BVH nodes to find primitive intersections
        int toVisitOffset = 0, currentNodeIndex = 0;
        Span<int> nodesToVisit = stackalloc int[64];
        int nodesVisited = 0;
        while (true)
        {
            ++nodesVisited;
            ref readonly LinearBVHNode node = ref nodes[currentNodeIndex];
            // Check ray against BVH node
            if (node.bounds.IntersectP(ray.o, ray.d, tMax, invDir, dirIsNeg))
            {
                if (node.nPrimitives > 0)
                {
                    // Intersect ray with primitives in leaf BVH node
                    for (int i = 0; i < node.nPrimitives; ++i)
                    {
                        // Check for intersection with primitive in BVH node
                        ShapeIntersection? primSi = primitives[node.offset + i].Intersect(ray, tMax);
                        if (primSi.HasValue)
                        {
                            si = primSi;
                            tMax = primSi.Value.tHit;
                        }
                    }
                    if (toVisitOffset == 0)
                        break;
                    currentNodeIndex = nodesToVisit[--toVisitOffset];
                }
                else
                {
                    // Put far BVH node on _nodesToVisit_ stack, advance to near node
                    if (dirIsNeg[node.axis] != 0)
                    {
                        nodesToVisit[toVisitOffset++] = currentNodeIndex + 1;
                        currentNodeIndex = node.offset;
                    }
                    else
                    {
                        nodesToVisit[toVisitOffset++] = node.offset;
                        currentNodeIndex = currentNodeIndex + 1;
                    }
                }
            }
            else
            {
                if (toVisitOffset == 0)
                    break;
                currentNodeIndex = nodesToVisit[--toVisitOffset];
            }
        }

        bvhNodesVisited.Add(nodesVisited);
        return si;
    }

    public override bool IntersectP(in Ray ray, Float tMax = Infinity)
    {
        if (nodes == null)
            return false;
        Vector3f invDir = new(1f / ray.d.x, 1f / ray.d.y, 1f / ray.d.z);
        Span<int> dirIsNeg = [invDir.x < 0 ? 1 : 0, invDir.y < 0 ? 1 : 0, invDir.z < 0 ? 1 : 0];
        Span<int> nodesToVisit = stackalloc int[64];
        int toVisitOffset = 0, currentNodeIndex = 0;
        int nodesVisited = 0;

        while (true)
        {
            ++nodesVisited;
            ref readonly LinearBVHNode node = ref nodes[currentNodeIndex];
            if (node.bounds.IntersectP(ray.o, ray.d, tMax, invDir, dirIsNeg))
            {
                // Process BVH node _node_ for traversal
                if (node.nPrimitives > 0)
                {
                    for (int i = 0; i < node.nPrimitives; ++i)
                    {
                        if (primitives[node.offset + i].IntersectP(ray, tMax))
                        {
                            bvhNodesVisited.Add(nodesVisited);
                            return true;
                        }
                    }
                    if (toVisitOffset == 0)
                        break;
                    currentNodeIndex = nodesToVisit[--toVisitOffset];
                }
                else
                {
                    if (dirIsNeg[node.axis] != 0)
                    {
                        /// second child first
                        nodesToVisit[toVisitOffset++] = currentNodeIndex + 1;
                        currentNodeIndex = node.offset;
                    }
                    else
                    {
                        nodesToVisit[toVisitOffset++] = node.offset;
                        currentNodeIndex = currentNodeIndex + 1;
                    }
                }
            }
            else
            {
                if (toVisitOffset == 0)
                    break;
                currentNodeIndex = nodesToVisit[--toVisitOffset];
            }
        }
        bvhNodesVisited.Add(nodesVisited);
        return false;
    }

    private struct UpperSAHPredicate(int nBuckets, Bounds3f centroidBounds, int dim, int minCostSplitBucket)
        : IStdPredicate<BVHBuildNode>
    {
        public readonly bool Evaluate(in BVHBuildNode node)
        {
            Float centroid = (node.bounds.pMin[dim] + node.bounds.pMax[dim]) * 0.5f;
            int b = (int)(nBuckets * ((centroid - centroidBounds.pMin[dim]) /
                                      (centroidBounds.pMax[dim] - centroidBounds.pMin[dim])));
            if (b == nBuckets)
                b = nBuckets - 1;
            CHECK_GE(b, 0);
            CHECK_LT(b, nBuckets);
            return b <= minCostSplitBucket;
        }
    }

    private BVHBuildNode BuildUpperSAH(BVHBuildNode[] treeletRoots, int start, int end, StrongBox<int> totalNodes)
    {
        CHECK_LT(start, end);
        int nNodes = end - start;
        if (nNodes == 1)
            return treeletRoots[start];
        totalNodes.Value++;
        BVHBuildNode node = new();

        // Compute bounds of all nodes under this HLBVH node
        Bounds3f bounds = new();
        for (int i = start; i < end; ++i)
            bounds = Union(bounds, treeletRoots[i].bounds);

        // Compute bound of HLBVH node centroids, choose split dimension _dim_
        Bounds3f centroidBounds = new();
        for (int i = start; i < end; ++i)
        {
            Point3f centroid = (treeletRoots[i].bounds.pMin + treeletRoots[i].bounds.pMax) * 0.5f;
            centroidBounds = Union(centroidBounds, centroid);
        }
        int dim = centroidBounds.MaxDimension();
        // FIXME: if this hits, what do we need to do?
        // Make sure the SAH split below does something... ?
        CHECK_NE(centroidBounds.pMax[dim], centroidBounds.pMin[dim]);

        // Allocate _BVHSplitBucket_ for SAH partition buckets
        const int nBuckets = 12;
        Span<BVHSplitBucket> buckets = stackalloc BVHSplitBucket[nBuckets];
        for (int i = 0; i < nBuckets; ++i)
            buckets[i].bounds = new Bounds3f();

        // Initialize _BVHSplitBucket_ for HLBVH SAH partition buckets
        for (int i = start; i < end; ++i)
        {
            Float centroid = (treeletRoots[i].bounds.pMin[dim] + treeletRoots[i].bounds.pMax[dim]) * 0.5f;
            int b = (int)(nBuckets * ((centroid - centroidBounds.pMin[dim]) /
                                      (centroidBounds.pMax[dim] - centroidBounds.pMin[dim])));
            if (b == nBuckets)
                b = nBuckets - 1;
            CHECK_GE(b, 0);
            CHECK_LT(b, nBuckets);
            buckets[b].count++;
            buckets[b].bounds = Union(buckets[b].bounds, treeletRoots[i].bounds);
        }

        // Compute costs for splitting after each bucket
        Span<Float> cost = stackalloc Float[nBuckets - 1];
        for (int i = 0; i < nBuckets - 1; ++i)
        {
            Bounds3f b0 = new(), b1 = new();
            int count0 = 0, count1 = 0;
            for (int j = 0; j <= i; ++j)
            {
                b0 = Union(b0, buckets[j].bounds);
                count0 += buckets[j].count;
            }
            for (int j = i + 1; j < nBuckets; ++j)
            {
                b1 = Union(b1, buckets[j].bounds);
                count1 += buckets[j].count;
            }
            cost[i] = .125f + (count0 * b0.SurfaceArea() + count1 * b1.SurfaceArea()) / bounds.SurfaceArea();
        }

        // Find bucket to split at that minimizes SAH metric
        Float minCost = cost[0];
        int minCostSplitBucket = 0;
        for (int i = 1; i < nBuckets - 1; ++i)
        {
            if (cost[i] < minCost)
            {
                minCost = cost[i];
                minCostSplitBucket = i;
            }
        }

        // Split nodes and create interior HLBVH SAH node
        int mid = start + StdPartition(treeletRoots.AsSpan(start, end - start),
                                       new UpperSAHPredicate(nBuckets, centroidBounds, dim, minCostSplitBucket));
        CHECK_GT(mid, start);
        CHECK_LT(mid, end);
        node.InitInterior(dim, BuildUpperSAH(treeletRoots, start, mid, totalNodes),
                          BuildUpperSAH(treeletRoots, mid, end, totalNodes));
        return node;
    }

    public static BVHAggregate Create(IReadOnlyList<Primitive> prims, ParameterDictionary parameters)
    {
        string splitMethodName = parameters.GetOneString("splitmethod", "sah");
        SplitMethod splitMethod;
        if (splitMethodName == "sah")
            splitMethod = SplitMethod.SAH;
        else if (splitMethodName == "hlbvh")
            splitMethod = SplitMethod.HLBVH;
        else if (splitMethodName == "middle")
            splitMethod = SplitMethod.Middle;
        else if (splitMethodName == "equal")
            splitMethod = SplitMethod.EqualCounts;
        else
        {
            Warning($"BVH split method \"{splitMethodName}\" unknown.  Using \"sah\".");
            splitMethod = SplitMethod.SAH;
        }

        int maxPrimsInNode = parameters.GetOneInt("maxnodeprims", 4);
        return new BVHAggregate(prims, maxPrimsInNode, splitMethod);
    }

    // (for tests and debugging: the flattened tree and the primitive order)
    internal int NodeCount => nodes?.Length ?? 0;
    internal (Bounds3f bounds, int offset, int nPrimitives, int axis) GetNode(int i) =>
        (nodes![i].bounds, nodes[i].offset, nodes[i].nPrimitives, nodes[i].axis);
    internal Primitive GetPrimitive(int i) => primitives[i];
}

// KdNodeToVisit Definition
internal struct KdNodeToVisit
{
    public int node;
    public Float tMin, tMax;
}

// KdTreeNode Definition
internal struct KdTreeNode
{
    // split (interior)
    public Float split;
    // onePrimitiveIndex or primitiveIndicesOffset (leaf)
    public int primitiveIndex;
    private uint flags;

    // KdTreeNode Methods
    public void InitLeaf(ReadOnlySpan<int> primNums, List<int> primitiveIndices)
    {
        flags = 3 | ((uint)primNums.Length << 2);
        // Store primitive ids for leaf node
        if (primNums.Length == 0)
            primitiveIndex = 0;
        else if (primNums.Length == 1)
            primitiveIndex = primNums[0];
        else
        {
            primitiveIndex = primitiveIndices.Count;
            foreach (int pn in primNums)
                primitiveIndices.Add(pn);
        }
    }

    public void InitInterior(int axis, int aboveChild, Float s)
    {
        split = s;
        flags = (uint)axis | ((uint)aboveChild << 2);
    }

    public readonly Float SplitPos() => split;
    public readonly int nPrimitives() => (int)(flags >> 2);
    public readonly int SplitAxis() => (int)(flags & 3);
    public readonly bool IsLeaf() => (flags & 3) == 3;
    public readonly int AboveChild() => (int)(flags >> 2);
}

// EdgeType Definition
internal enum EdgeType { Start, End }

// BoundEdge Definition
internal struct BoundEdge
{
    public Float t;
    public int primNum;
    public EdgeType type;

    // BoundEdge Public Methods
    public BoundEdge(Float t, int primNum, bool starting)
    {
        this.t = t;
        this.primNum = primNum;
        type = starting ? EdgeType.Start : EdgeType.End;
    }
}

// KdTreeAggregate Definition
public sealed class KdTreeAggregate : Primitive
{
    private static readonly StatPixelCounter kdNodesVisited = new("Kd-Tree/Nodes visited");

    // KdTreeAggregate Private Members
    private readonly int isectCost, traversalCost, maxPrims;
    private readonly Float emptyBonus;
    private readonly Primitive[] primitives;
    private readonly List<int> primitiveIndices = [];
    private KdTreeNode[] nodes = [];
    private int nAllocedNodes, nextFreeNode;
    private readonly Bounds3f bounds = new();

    // KdTreeAggregate Method Definitions
    public KdTreeAggregate(IReadOnlyList<Primitive> p, int isectCost = 5, int traversalCost = 1,
                           Float emptyBonus = 0.5f, int maxPrims = 1, int maxDepth = -1)
    {
        this.isectCost = isectCost;
        this.traversalCost = traversalCost;
        this.maxPrims = maxPrims;
        this.emptyBonus = emptyBonus;
        primitives = [.. p];
        // Build kd-tree aggregate
        nextFreeNode = nAllocedNodes = 0;
        if (maxDepth <= 0)
            maxDepth = (int)MathF.Round(8 + 1.3f * Log2Int((long)primitives.Length), MidpointRounding.AwayFromZero);
        // Compute bounds for kd-tree construction
        Bounds3f[] primBounds = new Bounds3f[primitives.Length];
        for (int i = 0; i < primitives.Length; ++i)
        {
            Bounds3f b = primitives[i].Bounds();
            bounds = Union(bounds, b);
            primBounds[i] = b;
        }

        // Allocate working memory for kd-tree construction
        BoundEdge[][] edges = new BoundEdge[3][];
        for (int i = 0; i < 3; ++i)
            edges[i] = new BoundEdge[2 * primitives.Length];

        int[] prims0 = new int[primitives.Length];
        int[] prims1 = new int[(maxDepth + 1) * primitives.Length];

        // Initialize _primNums_ for kd-tree construction
        int[] primNums = new int[primitives.Length];
        for (int i = 0; i < primitives.Length; ++i)
            primNums[i] = i;

        // Start recursive construction of kd-tree
        BuildTree(0, bounds, primBounds, primNums, maxDepth, edges, prims0, prims1, 0);
    }

    private struct EdgeLess : IStdLess<BoundEdge>
    {
        // std::tie(e0.t, e0.type) < std::tie(e1.t, e1.type)
        public readonly bool Less(in BoundEdge e0, in BoundEdge e1) =>
            e0.t < e1.t || (!(e1.t < e0.t) && e0.type < e1.type);
    }

    private void BuildTree(int nodeNum, in Bounds3f nodeBounds, Bounds3f[] allPrimBounds,
                           ReadOnlySpan<int> primNums, int depth, BoundEdge[][] edges, Span<int> prims0,
                           Span<int> prims1, int badRefines)
    {
        CHECK_EQ(nodeNum, nextFreeNode);
        // Get next free node from _nodes_ array
        if (nextFreeNode == nAllocedNodes)
        {
            int nNewAllocNodes = Math.Max(2 * nAllocedNodes, 512);
            Array.Resize(ref nodes, nNewAllocNodes);
            nAllocedNodes = nNewAllocNodes;
        }
        ++nextFreeNode;

        // Initialize leaf node if termination criteria met
        if (primNums.Length <= maxPrims || depth == 0)
        {
            nodes[nodeNum].InitLeaf(primNums, primitiveIndices);
            return;
        }

        // Initialize interior node and continue recursion
        // Choose split axis position for interior node
        int bestAxis = -1, bestOffset = -1;
        Float bestCost = Infinity, leafCost = isectCost * (long)primNums.Length;
        Float invTotalSA = 1 / nodeBounds.SurfaceArea();
        // Choose which axis to split along
        int axis = nodeBounds.MaxDimension();

        // Choose split along axis and attempt to partition primitives
        int retries = 0;
        int nPrimitives = primNums.Length;
        while (true)
        {
            // Initialize edges for _axis_
            for (int i = 0; i < nPrimitives; ++i)
            {
                int pn = primNums[i];
                ref readonly Bounds3f b = ref allPrimBounds[pn];
                edges[axis][2 * i] = new BoundEdge(b.pMin[axis], pn, true);
                edges[axis][2 * i + 1] = new BoundEdge(b.pMax[axis], pn, false);
            }
            // Sort _edges_ for _axis_
            StdSort(edges[axis].AsSpan(0, 2 * nPrimitives), new EdgeLess());

            // Compute cost of all splits for _axis_ to find best
            int nBelow = 0, nAbove = primNums.Length;
            for (int i = 0; i < 2 * primNums.Length; ++i)
            {
                if (edges[axis][i].type == EdgeType.End)
                    --nAbove;
                Float edgeT = edges[axis][i].t;
                if (edgeT > nodeBounds.pMin[axis] && edgeT < nodeBounds.pMax[axis])
                {
                    // Compute child surface areas for split at _edgeT_
                    Vector3f d = nodeBounds.pMax - nodeBounds.pMin;
                    int otherAxis0 = (axis + 1) % 3, otherAxis1 = (axis + 2) % 3;
                    Float belowSA = 2 * (d[otherAxis0] * d[otherAxis1] +
                                         (edgeT - nodeBounds.pMin[axis]) * (d[otherAxis0] + d[otherAxis1]));
                    Float aboveSA = 2 * (d[otherAxis0] * d[otherAxis1] +
                                         (nodeBounds.pMax[axis] - edgeT) * (d[otherAxis0] + d[otherAxis1]));

                    // Compute cost for split at _i_th edge
                    Float pBelow = belowSA * invTotalSA, pAbove = aboveSA * invTotalSA;
                    Float eb = (nAbove == 0 || nBelow == 0) ? emptyBonus : 0;
                    Float cost = traversalCost + isectCost * (1 - eb) * (pBelow * nBelow + pAbove * nAbove);
                    // Update best split if this is lowest cost so far
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestOffset = i;
                    }
                }
                if (edges[axis][i].type == EdgeType.Start)
                    ++nBelow;
            }
            CHECK(nBelow == nPrimitives && nAbove == 0);

            // Try to split along another axis if no good splits were found
            if (bestAxis == -1 && retries < 2)
            {
                ++retries;
                axis = (axis + 1) % 3;
                continue;
            }
            break;
        }

        // Create leaf if no good splits were found
        if (bestCost > leafCost)
            ++badRefines;
        if ((bestCost > 4 * leafCost && nPrimitives < 16) || bestAxis == -1 || badRefines == 3)
        {
            nodes[nodeNum].InitLeaf(primNums, primitiveIndices);
            return;
        }

        // Classify primitives with respect to split
        int n0 = 0, n1 = 0;
        for (int i = 0; i < bestOffset; ++i)
            if (edges[bestAxis][i].type == EdgeType.Start)
                prims0[n0++] = edges[bestAxis][i].primNum;
        for (int i = bestOffset + 1; i < 2 * nPrimitives; ++i)
            if (edges[bestAxis][i].type == EdgeType.End)
                prims1[n1++] = edges[bestAxis][i].primNum;

        // Recursively initialize kd-tree node's children
        Float tSplit = edges[bestAxis][bestOffset].t;
        Bounds3f bounds0 = nodeBounds, bounds1 = nodeBounds;
        bounds0.pMax[bestAxis] = bounds1.pMin[bestAxis] = tSplit;
        BuildTree(nodeNum + 1, bounds0, allPrimBounds, prims0[..n0], depth - 1, edges, prims0, prims1[n1..],
                  badRefines);
        int aboveChild = nextFreeNode;
        nodes[nodeNum].InitInterior(bestAxis, aboveChild, tSplit);
        BuildTree(aboveChild, bounds1, allPrimBounds, prims1[..n1], depth - 1, edges, prims0, prims1[n1..],
                  badRefines);
    }

    public override Bounds3f Bounds() => bounds;

    public override ShapeIntersection? Intersect(in Ray ray, Float rayTMax = Infinity)
    {
        // Compute initial parametric range of ray inside kd-tree extent
        if (!bounds.IntersectP(ray.o, ray.d, rayTMax, out Float tMin, out Float tMax))
            return null;

        // Prepare to traverse kd-tree for ray
        Vector3f invDir = new(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
        const int maxToVisit = 64;
        Span<KdNodeToVisit> toVisit = stackalloc KdNodeToVisit[maxToVisit];
        int toVisitIndex = 0;
        int nodesVisited = 0;

        // Traverse kd-tree nodes in order for ray
        ShapeIntersection? si = null;
        int nodeIndex = 0;
        while (true)
        {
            // Bail out if we found a hit closer than the current node
            if (rayTMax < tMin)
                break;

            ++nodesVisited;
            ref readonly KdTreeNode node = ref nodes[nodeIndex];
            if (!node.IsLeaf())
            {
                // Visit kd-tree interior node
                // Compute parametric distance along ray to split plane
                int axis = node.SplitAxis();
                Float tSplit = (node.SplitPos() - ray.o[axis]) * invDir[axis];

                // Get node child pointers for ray
                int firstChild, secondChild;
                bool belowFirst = (ray.o[axis] < node.SplitPos()) ||
                                  (ray.o[axis] == node.SplitPos() && ray.d[axis] <= 0);
                if (belowFirst)
                {
                    firstChild = nodeIndex + 1;
                    secondChild = node.AboveChild();
                }
                else
                {
                    firstChild = node.AboveChild();
                    secondChild = nodeIndex + 1;
                }

                // Advance to next child node, possibly enqueue other child
                if (tSplit > tMax || tSplit <= 0)
                    nodeIndex = firstChild;
                else if (tSplit < tMin)
                    nodeIndex = secondChild;
                else
                {
                    // Enqueue _secondChild_ in todo list
                    toVisit[toVisitIndex].node = secondChild;
                    toVisit[toVisitIndex].tMin = tSplit;
                    toVisit[toVisitIndex].tMax = tMax;
                    ++toVisitIndex;

                    nodeIndex = firstChild;
                    tMax = tSplit;
                }
            }
            else
            {
                // Check for intersections inside leaf node
                int nPrimitives = node.nPrimitives();
                if (nPrimitives == 1)
                {
                    Primitive p = primitives[node.primitiveIndex];
                    // Check one primitive inside leaf node
                    ShapeIntersection? primSi = p.Intersect(ray, rayTMax);
                    if (primSi.HasValue)
                    {
                        si = primSi;
                        rayTMax = primSi.Value.tHit;
                    }
                }
                else
                {
                    for (int i = 0; i < nPrimitives; ++i)
                    {
                        int index = primitiveIndices[node.primitiveIndex + i];
                        Primitive p = primitives[index];
                        // Check one primitive inside leaf node
                        ShapeIntersection? primSi = p.Intersect(ray, rayTMax);
                        if (primSi.HasValue)
                        {
                            si = primSi;
                            rayTMax = primSi.Value.tHit;
                        }
                    }
                }

                // Grab next node to visit from todo list
                if (toVisitIndex > 0)
                {
                    --toVisitIndex;
                    nodeIndex = toVisit[toVisitIndex].node;
                    tMin = toVisit[toVisitIndex].tMin;
                    tMax = toVisit[toVisitIndex].tMax;
                }
                else
                    break;
            }
        }
        kdNodesVisited.Add(nodesVisited);
        return si;
    }

    public override bool IntersectP(in Ray ray, Float raytMax = Infinity)
    {
        // Compute initial parametric range of ray inside kd-tree extent
        if (!bounds.IntersectP(ray.o, ray.d, raytMax, out Float tMin, out Float tMax))
            return false;

        // Prepare to traverse kd-tree for ray
        Vector3f invDir = new(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
        const int maxTodo = 64;
        Span<KdNodeToVisit> toVisit = stackalloc KdNodeToVisit[maxTodo];
        int toVisitIndex = 0;
        int nodesVisited = 0;
        int nodeIndex = 0;
        while (true)
        {
            ++nodesVisited;
            ref readonly KdTreeNode node = ref nodes[nodeIndex];
            if (node.IsLeaf())
            {
                // Check for shadow ray intersections inside leaf node
                int nPrimitives = node.nPrimitives();
                if (nPrimitives == 1)
                {
                    Primitive p = primitives[node.primitiveIndex];
                    if (p.IntersectP(ray, raytMax))
                    {
                        kdNodesVisited.Add(nodesVisited);
                        return true;
                    }
                }
                else
                {
                    for (int i = 0; i < nPrimitives; ++i)
                    {
                        int primitiveIndex = primitiveIndices[node.primitiveIndex + i];
                        Primitive prim = primitives[primitiveIndex];
                        if (prim.IntersectP(ray, raytMax))
                        {
                            kdNodesVisited.Add(nodesVisited);
                            return true;
                        }
                    }
                }

                // Grab next node to process from todo list
                if (toVisitIndex > 0)
                {
                    --toVisitIndex;
                    nodeIndex = toVisit[toVisitIndex].node;
                    tMin = toVisit[toVisitIndex].tMin;
                    tMax = toVisit[toVisitIndex].tMax;
                }
                else
                    break;
            }
            else
            {
                // Process kd-tree interior node

                // Compute parametric distance along ray to split plane
                int axis = node.SplitAxis();
                Float tSplit = (node.SplitPos() - ray.o[axis]) * invDir[axis];

                // Get node children pointers for ray
                int firstChild, secondChild;
                bool belowFirst = (ray.o[axis] < node.SplitPos()) ||
                                  (ray.o[axis] == node.SplitPos() && ray.d[axis] <= 0);
                if (belowFirst)
                {
                    firstChild = nodeIndex + 1;
                    secondChild = node.AboveChild();
                }
                else
                {
                    firstChild = node.AboveChild();
                    secondChild = nodeIndex + 1;
                }

                // Advance to next child node, possibly enqueue other child
                if (tSplit > tMax || tSplit <= 0)
                    nodeIndex = firstChild;
                else if (tSplit < tMin)
                    nodeIndex = secondChild;
                else
                {
                    // Enqueue _secondChild_ in todo list
                    toVisit[toVisitIndex].node = secondChild;
                    toVisit[toVisitIndex].tMin = tSplit;
                    toVisit[toVisitIndex].tMax = tMax;
                    ++toVisitIndex;
                    nodeIndex = firstChild;
                    tMax = tSplit;
                }
            }
        }
        kdNodesVisited.Add(nodesVisited);
        return false;
    }

    public static KdTreeAggregate Create(IReadOnlyList<Primitive> prims, ParameterDictionary parameters)
    {
        int isectCost = parameters.GetOneInt("intersectcost", 5);
        int travCost = parameters.GetOneInt("traversalcost", 1);
        Float emptyBonus = parameters.GetOneFloat("emptybonus", 0.5f);
        int maxPrims = parameters.GetOneInt("maxprims", 1);
        int maxDepth = parameters.GetOneInt("maxdepth", -1);
        return new KdTreeAggregate(prims, isectCost, travCost, emptyBonus, maxPrims, maxDepth);
    }

    // (for tests and debugging: the tree's nodes)
    internal int NodeCount => nextFreeNode;
    internal (bool isLeaf, int axis, Float split, int nPrimitives, int aboveChild, int primitiveIndex) GetNode(int i) =>
        (nodes[i].IsLeaf(), nodes[i].SplitAxis(), nodes[i].split, nodes[i].nPrimitives(), nodes[i].AboveChild(),
         nodes[i].primitiveIndex);
    internal IReadOnlyList<int> PrimitiveIndices => primitiveIndices;
}

public static partial class Util
{
    public static Primitive CreateAccelerator(string name, IReadOnlyList<Primitive> prims,
                                              ParameterDictionary parameters)
    {
        Primitive? accel = null;
        if (name == "bvh")
            accel = BVHAggregate.Create(prims, parameters);
        else if (name == "kdtree")
            accel = KdTreeAggregate.Create(prims, parameters);
        else
            ErrorExit($"{name}: accelerator type unknown.");

        if (accel == null)
            ErrorExit($"{name}: unable to create accelerator.");

        parameters.ReportUnused();
        return accel!;
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/lightsamplers.h and lightsamplers.cpp
//
// pbrt's HashMap<Light, ...> hashes the light pointers; here the lights are
// compared by reference. CompactLightBounds keeps its bit fields in
// separate fields.

using System.Runtime.InteropServices;

namespace Pbrt;

// UniformLightSampler Definition
public sealed class UniformLightSampler : LightSampler
{
    // UniformLightSampler Private Members
    private readonly Light[] lights;

    // UniformLightSampler Public Methods
    public UniformLightSampler(ReadOnlySpan<Light> lights) => this.lights = lights.ToArray();

    public override SampledLight? Sample(Float u)
    {
        if (lights.Length == 0)
            return null;
        int lightIndex = Math.Min((int)(u * lights.Length), lights.Length - 1);
        return new SampledLight(lights[lightIndex], 1f / lights.Length);
    }

    public override Float PMF(Light light)
    {
        if (lights.Length == 0)
            return 0;
        return 1f / lights.Length;
    }

    public override SampledLight? Sample(in LightSampleContext ctx, Float u) => Sample(u);

    public override Float PMF(in LightSampleContext ctx, Light light) => PMF(light);

    public override string ToString() => "UniformLightSampler";
}

// PowerLightSampler Definition
public sealed class PowerLightSampler : LightSampler
{
    // PowerLightSampler Private Members
    private readonly Light[] lights;
    private readonly Dictionary<Light, int> lightToIndex = new(ReferenceEqualityComparer.Instance);
    private readonly AliasTable? aliasTable;

    // PowerLightSampler Method Definitions
    public PowerLightSampler(ReadOnlySpan<Light> lights)
    {
        this.lights = lights.ToArray();
        if (lights.Length == 0)
            return;
        // Initialize _lightToIndex_ hash table
        for (int i = 0; i < lights.Length; ++i)
            lightToIndex[lights[i]] = i;

        // Compute lights' power and initialize alias table
        Float[] lightPower = new Float[lights.Length];
        SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
        for (int i = 0; i < lights.Length; ++i)
        {
            SampledSpectrum phi = SafeDiv(lights[i].Phi(lambda), lambda.PDF());
            lightPower[i] = phi.Average();
        }
        Float sum = 0f;
        foreach (Float p in lightPower)
            sum += p;
        if (sum == 0f)
            Array.Fill(lightPower, 1f);
        aliasTable = new AliasTable(lightPower);
    }

    public override SampledLight? Sample(Float u)
    {
        if (aliasTable == null || aliasTable.Size() == 0)
            return null;
        int lightIndex = aliasTable.Sample(u, out Float pmf, out _);
        return new SampledLight(lights[lightIndex], pmf);
    }

    public override Float PMF(Light light)
    {
        if (aliasTable == null || aliasTable.Size() == 0)
            return 0;
        return aliasTable.PMF(lightToIndex[light]);
    }

    public override SampledLight? Sample(in LightSampleContext ctx, Float u) => Sample(u);

    public override Float PMF(in LightSampleContext ctx, Light light) => PMF(light);

    public override string ToString() => $"[ PowerLightSampler aliasTable: {aliasTable} ]";
}

// CompactLightBounds Definition
public struct CompactLightBounds
{
    // CompactLightBounds Private Members
    private OctahedralVector w;
    private Float phi = 0;
    private uint qCosTheta_o, qCosTheta_e;
    private bool twoSided;
    private ushort qb00, qb01, qb02, qb10, qb11, qb12;

    // CompactLightBounds Public Methods
    public CompactLightBounds() { }

    public CompactLightBounds(in LightBounds lb, in Bounds3f allb)
    {
        w = new OctahedralVector(Normalize(lb.w));
        phi = lb.phi;
        qCosTheta_o = QuantizeCos(lb.cosTheta_o);
        qCosTheta_e = QuantizeCos(lb.cosTheta_e);
        twoSided = lb.twoSided;
        // Quantize bounding box into _qb_
        qb00 = (ushort)FMath.Floor(QuantizeBounds(lb.bounds.pMin.x, allb.pMin.x, allb.pMax.x));
        qb01 = (ushort)FMath.Floor(QuantizeBounds(lb.bounds.pMin.y, allb.pMin.y, allb.pMax.y));
        qb02 = (ushort)FMath.Floor(QuantizeBounds(lb.bounds.pMin.z, allb.pMin.z, allb.pMax.z));
        qb10 = (ushort)FMath.Ceiling(QuantizeBounds(lb.bounds.pMax.x, allb.pMin.x, allb.pMax.x));
        qb11 = (ushort)FMath.Ceiling(QuantizeBounds(lb.bounds.pMax.y, allb.pMin.y, allb.pMax.y));
        qb12 = (ushort)FMath.Ceiling(QuantizeBounds(lb.bounds.pMax.z, allb.pMin.z, allb.pMax.z));
    }

    public override readonly string ToString() =>
        $"[ CompactLightBounds qb: [ [ {qb00} {qb01} {qb02} ] [ {qb10} {qb11} {qb12} ] ] w: {w} " +
        $"({(Vector3f)w}) phi: {FloatToString(phi)} qCosTheta_o: {qCosTheta_o} ({FloatToString(CosTheta_o())}) " +
        $"qCosTheta_e: {qCosTheta_e} ({FloatToString(CosTheta_e())}) twoSided: {(twoSided ? 1 : 0)} ]";

    public readonly string ToString(in Bounds3f allBounds) =>
        $"[ CompactLightBounds b: {Bounds(allBounds)} qb: [ [ {qb00} {qb01} {qb02} ] [ {qb10} {qb11} {qb12} ] ] " +
        $"w: {w} ({(Vector3f)w}) phi: {FloatToString(phi)} qCosTheta_o: {qCosTheta_o} " +
        $"({FloatToString(CosTheta_o())}) qCosTheta_e: {qCosTheta_e} ({FloatToString(CosTheta_e())}) " +
        $"twoSided: {(twoSided ? 1 : 0)} ]";

    public readonly bool TwoSided() => twoSided;
    public readonly Float CosTheta_o() => 2 * (qCosTheta_o / 32767f) - 1;
    public readonly Float CosTheta_e() => 2 * (qCosTheta_e / 32767f) - 1;

    public readonly Bounds3f Bounds(in Bounds3f allb) =>
        new(new Point3f(Lerp(qb00 / 65535f, allb.pMin.x, allb.pMax.x),
                        Lerp(qb01 / 65535f, allb.pMin.y, allb.pMax.y),
                        Lerp(qb02 / 65535f, allb.pMin.z, allb.pMax.z)),
            new Point3f(Lerp(qb10 / 65535f, allb.pMin.x, allb.pMax.x),
                        Lerp(qb11 / 65535f, allb.pMin.y, allb.pMax.y),
                        Lerp(qb12 / 65535f, allb.pMin.z, allb.pMax.z)));

    public readonly Float Importance(Point3f p, Normal3f n, in Bounds3f allb)
    {
        Bounds3f bounds = Bounds(allb);
        Float cosTheta_o = CosTheta_o(), cosTheta_e = CosTheta_e();
        // Return importance for light bounds at reference point
        // Compute clamped squared distance to reference point
        Point3f pc = (bounds.pMin + bounds.pMax) / 2;
        Float d2 = DistanceSquared(p, pc);
        d2 = FMath.Max(d2, Length(bounds.Diagonal()) / 2);

        // Compute sine and cosine of angle to vector _w_, $\theta_\roman{w}$
        Vector3f wi = Normalize(p - pc);
        Float cosTheta_w = Dot((Vector3f)w, wi);
        if (twoSided)
            cosTheta_w = FMath.Abs(cosTheta_w);
        Float sinTheta_w = SafeSqrt(1 - Sqr(cosTheta_w));

        // Compute $\cos\,\theta_\roman{\+b}$ for reference point
        Float cosTheta_b = BoundSubtendedDirections(bounds, p).cosTheta;
        Float sinTheta_b = SafeSqrt(1 - Sqr(cosTheta_b));

        // Compute $\cos\,\theta'$ and test against $\cos\,\theta_\roman{e}$
        Float sinTheta_o = SafeSqrt(1 - Sqr(cosTheta_o));
        Float cosTheta_x = LightBounds.CosSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
        Float sinTheta_x = LightBounds.SinSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
        Float cosThetap = LightBounds.CosSubClamped(sinTheta_x, cosTheta_x, sinTheta_b, cosTheta_b);
        if (cosThetap <= cosTheta_e)
            return 0;

        // Return final importance at reference point
        Float importance = phi * cosThetap / d2;
        DCHECK_GE(importance, -(Float)1e-3);
        // Account for $\cos\theta_\roman{i}$ in importance at surfaces
        if (n != new Normal3f(0, 0, 0))
        {
            Float cosTheta_i = AbsDot(wi, n);
            Float sinTheta_i = SafeSqrt(1 - Sqr(cosTheta_i));
            Float cosThetap_i = LightBounds.CosSubClamped(sinTheta_i, cosTheta_i, sinTheta_b, cosTheta_b);
            importance *= cosThetap_i;
        }

        importance = FMath.Max(importance, 0);
        return importance;
    }

    // CompactLightBounds Private Methods
    // (The result is stored in a 15-bit field in pbrt.)
    private static uint QuantizeCos(Float c)
    {
        CHECK(c >= -1 && c <= 1);
        return (uint)FMath.Floor(32767f * ((c + 1) / 2)) & 0x7fff;
    }

    private static Float QuantizeBounds(Float c, Float min, Float max)
    {
        CHECK(c >= min && c <= max);
        if (min == max)
            return 0;
        return 65535f * Clamp((c - min) / (max - min), 0, 1);
    }
}

// LightBVHNode Definition
public struct LightBVHNode
{
    // LightBVHNode Public Methods
    public static LightBVHNode MakeLeaf(uint lightIndex, in CompactLightBounds cb) =>
        new() { lightBounds = cb, childOrLightIndex = lightIndex, isLeaf = true };

    public static LightBVHNode MakeInterior(uint child1Index, in CompactLightBounds cb) =>
        new() { lightBounds = cb, childOrLightIndex = child1Index, isLeaf = false };

    public override readonly string ToString() =>
        $"[ LightBVHNode lightBounds: {lightBounds} childOrLightIndex: {childOrLightIndex} isLeaf: {(isLeaf ? 1 : 0)} ]";

    // LightBVHNode Public Members
    public CompactLightBounds lightBounds;
    public uint childOrLightIndex;
    public bool isLeaf;
}

// BVHLightSampler Definition
public sealed class BVHLightSampler : LightSampler
{
    private static readonly StatMemoryCounter lightBVHBytes = new("Memory/Light BVH");

    // BVHLightSampler Private Members
    private readonly Light[] lights;
    private readonly List<Light> infiniteLights = [];
    private Bounds3f allLightBounds = new();
    private readonly List<LightBVHNode> nodes = [];
    private readonly Dictionary<Light, uint> lightToBitTrail = new(ReferenceEqualityComparer.Instance);

    // BVHLightSampler Method Definitions
    public BVHLightSampler(ReadOnlySpan<Light> lights)
    {
        this.lights = lights.ToArray();
        // Initialize _infiniteLights_ array and light BVH
        List<(int, LightBounds)> bvhLights = [];
        for (int i = 0; i < lights.Length; ++i)
        {
            // Store $i$th light in either _infiniteLights_ or _bvhLights_
            Light light = lights[i];
            LightBounds? lightBounds = light.Bounds();
            if (lightBounds is not LightBounds lb)
                infiniteLights.Add(light);
            else if (lb.phi > 0)
            {
                bvhLights.Add((i, lb));
                allLightBounds = Union(allLightBounds, lb.bounds);
            }
        }
        if (bvhLights.Count > 0)
            buildBVH(CollectionsMarshal.AsSpan(bvhLights), 0, bvhLights.Count, 0, 0);
        // (sizeof(LightBVHNode) is 32, sizeof(uint32_t) 4 and sizeof(Light) 8
        // in pbrt.)
        lightBVHBytes.Add(nodes.Count * 32 + PbrtHashMapCapacity(lightToBitTrail.Count) * 4 + lights.Length * 8 +
                          infiniteLights.Count * 8);
    }

    // The capacity of pbrt's HashMap after n insertions, for the memory
    // statistic: it starts with 8 entries and grows to max(64, 2 * capacity)
    // when more than a third full.
    private static int PbrtHashMapCapacity(int n)
    {
        int capacity = 8;
        for (int k = 1; k <= n; ++k)
            if (3 * k > capacity)
                capacity = Math.Max(64, 2 * capacity);
        return capacity;
    }

    public override SampledLight? Sample(in LightSampleContext ctx, Float u)
    {
        // Compute infinite light sampling probability _pInfinite_
        Float pInfinite = (Float)infiniteLights.Count / (Float)(infiniteLights.Count + (nodes.Count == 0 ? 0 : 1));

        if (u < pInfinite)
        {
            // Sample infinite lights with uniform probability
            u /= pInfinite;
            int index = Math.Min((int)(u * infiniteLights.Count), infiniteLights.Count - 1);
            Float pmf = pInfinite / infiniteLights.Count;
            return new SampledLight(infiniteLights[index], pmf);
        }
        else
        {
            // Traverse light BVH to sample light
            if (nodes.Count == 0)
                return null;
            // Declare common variables for light BVH traversal
            Point3f p = ctx.p();
            Normal3f n = ctx.ns;
            u = FMath.Min((u - pInfinite) / (1 - pInfinite), OneMinusEpsilon);
            int nodeIndex = 0;
            Float pmf = 1 - pInfinite;

            while (true)
            {
                // Process light BVH node for light sampling
                LightBVHNode node = nodes[nodeIndex];
                if (!node.isLeaf)
                {
                    // Compute light BVH child node importances
                    LightBVHNode child0 = nodes[nodeIndex + 1], child1 = nodes[(int)node.childOrLightIndex];
                    ReadOnlySpan<Float> ci = [child0.lightBounds.Importance(p, n, allLightBounds),
                                              child1.lightBounds.Importance(p, n, allLightBounds)];
                    if (ci[0] == 0 && ci[1] == 0)
                        return null;

                    // Randomly sample light BVH child node
                    int child = SampleDiscrete(ci, u, out Float nodePMF, out u);
                    pmf *= nodePMF;
                    nodeIndex = (child == 0) ? (nodeIndex + 1) : (int)node.childOrLightIndex;
                }
                else
                {
                    // Confirm light has nonzero importance before returning light sample
                    if (nodeIndex > 0)
                        DCHECK_GT(node.lightBounds.Importance(p, n, allLightBounds), 0);
                    if (nodeIndex > 0 || node.lightBounds.Importance(p, n, allLightBounds) > 0)
                        return new SampledLight(lights[node.childOrLightIndex], pmf);
                    return null;
                }
            }
        }
    }

    public override Float PMF(in LightSampleContext ctx, Light light)
    {
        // Handle infinite _light_ PMF computation
        if (!lightToBitTrail.ContainsKey(light))
            return 1f / (infiniteLights.Count + (nodes.Count == 0 ? 0 : 1));

        // Initialize local variables for BVH traversal for PMF computation
        uint bitTrail = lightToBitTrail[light];
        Point3f p = ctx.p();
        Normal3f n = ctx.ns;
        // Compute infinite light sampling probability _pInfinite_
        Float pInfinite = (Float)infiniteLights.Count / (Float)(infiniteLights.Count + (nodes.Count == 0 ? 0 : 1));

        Float pmf = 1 - pInfinite;
        int nodeIndex = 0;

        // Compute light's PMF by walking down tree nodes to the light
        while (true)
        {
            LightBVHNode node = nodes[nodeIndex];
            if (node.isLeaf)
            {
                DCHECK(ReferenceEquals(light, lights[node.childOrLightIndex]));
                return pmf;
            }
            // Compute child importances and update PMF for current node
            LightBVHNode child0 = nodes[nodeIndex + 1];
            LightBVHNode child1 = nodes[(int)node.childOrLightIndex];
            Float ci0 = child0.lightBounds.Importance(p, n, allLightBounds);
            Float ci1 = child1.lightBounds.Importance(p, n, allLightBounds);
            Float cib = (bitTrail & 1) != 0 ? ci1 : ci0;
            DCHECK_GT(cib, 0);
            pmf *= cib / (ci0 + ci1);

            // Use _bitTrail_ to find next node index and update its value
            nodeIndex = (bitTrail & 1) != 0 ? (int)node.childOrLightIndex : (nodeIndex + 1);
            bitTrail >>= 1;
        }
    }

    public override SampledLight? Sample(Float u)
    {
        if (lights.Length == 0)
            return null;
        int lightIndex = Math.Min((int)(u * lights.Length), lights.Length - 1);
        return new SampledLight(lights[lightIndex], 1f / lights.Length);
    }

    public override Float PMF(Light light)
    {
        if (lights.Length == 0)
            return 0;
        return 1f / lights.Length;
    }

    public override string ToString() => $"[ BVHLightSampler nodes: [ {string.Join(", ", nodes)} ] ]";

    // BVHLightSampler Private Methods
    private const int nBuckets = 12;

    private readonly struct BucketPredicate(Bounds3f centroidBounds, int dim, int splitBucket)
        : IStdPredicate<(int, LightBounds)>
    {
        public bool Evaluate(in (int, LightBounds) l)
        {
            int b = (int)(nBuckets * centroidBounds.Offset(l.Item2.Centroid())[dim]);
            if (b == nBuckets)
                b = nBuckets - 1;
            DCHECK_GE(b, 0);
            DCHECK_LT(b, nBuckets);
            return b <= splitBucket;
        }
    }

    private (int, LightBounds) buildBVH(Span<(int, LightBounds)> bvhLights, int start, int end, uint bitTrail,
                                        int depth)
    {
        DCHECK_LT(start, end);
        // Initialize leaf node if only a single light remains
        if (end - start == 1)
        {
            int leafIndex = nodes.Count;
            CompactLightBounds leafBounds = new(bvhLights[start].Item2, allLightBounds);
            int lightIndex = bvhLights[start].Item1;
            nodes.Add(LightBVHNode.MakeLeaf((uint)lightIndex, leafBounds));
            lightToBitTrail[lights[lightIndex]] = bitTrail;
            return (leafIndex, bvhLights[start].Item2);
        }

        // Choose split dimension and position using modified SAH
        // Compute bounds and centroid bounds for lights
        Bounds3f bounds = new(), centroidBounds = new();
        for (int i = start; i < end; ++i)
        {
            ref readonly LightBounds lb = ref bvhLights[i].Item2;
            bounds = Union(bounds, lb.bounds);
            centroidBounds = Union(centroidBounds, lb.Centroid());
        }

        Float minCost = Infinity;
        int minCostSplitBucket = -1, minCostSplitDim = -1;
        Span<LightBounds> bucketLightBounds = new LightBounds[nBuckets];
        Span<Float> cost = stackalloc Float[nBuckets - 1];
        for (int dim = 0; dim < 3; ++dim)
        {
            // Compute minimum cost bucket for splitting along dimension _dim_
            if (centroidBounds.pMax[dim] == centroidBounds.pMin[dim])
                continue;
            // Compute _LightBounds_ for each bucket
            bucketLightBounds.Fill(new LightBounds());
            for (int i = start; i < end; ++i)
            {
                Point3f pc = bvhLights[i].Item2.Centroid();
                int b = (int)(nBuckets * centroidBounds.Offset(pc)[dim]);
                if (b == nBuckets)
                    b = nBuckets - 1;
                DCHECK_GE(b, 0);
                DCHECK_LT(b, nBuckets);
                bucketLightBounds[b] = Union(bucketLightBounds[b], bvhLights[i].Item2);
            }

            // Compute costs for splitting lights after each bucket
            for (int i = 0; i < nBuckets - 1; ++i)
            {
                // Find _LightBounds_ for lights below and above bucket split
                LightBounds b0 = new(), b1 = new();
                for (int j = 0; j <= i; ++j)
                    b0 = Union(b0, bucketLightBounds[j]);
                for (int j = i + 1; j < nBuckets; ++j)
                    b1 = Union(b1, bucketLightBounds[j]);

                // Compute final light split cost for bucket
                cost[i] = EvaluateCost(b0, bounds, dim) + EvaluateCost(b1, bounds, dim);
            }

            // Find light split that minimizes SAH metric
            for (int i = 1; i < nBuckets - 1; ++i)
            {
                if (cost[i] > 0 && cost[i] < minCost)
                {
                    minCost = cost[i];
                    minCostSplitBucket = i;
                    minCostSplitDim = dim;
                }
            }
        }

        // Partition lights according to chosen split
        int mid;
        if (minCostSplitDim == -1)
            mid = (start + end) / 2;
        else
        {
            mid = start + StdPartition(bvhLights[start..end],
                                       new BucketPredicate(centroidBounds, minCostSplitDim, minCostSplitBucket));
            if (mid == start || mid == end)
                mid = (start + end) / 2;
            DCHECK(mid > start && mid < end);
        }

        // Allocate interior _LightBVHNode_ and recursively initialize children
        int nodeIndex = nodes.Count;
        nodes.Add(new LightBVHNode());
        CHECK_LT(depth, 64);
        (int, LightBounds) child0 = buildBVH(bvhLights, start, mid, bitTrail, depth + 1);
        DCHECK_EQ(nodeIndex + 1, child0.Item1);
        (int, LightBounds) child1 = buildBVH(bvhLights, mid, end, bitTrail | (1u << depth), depth + 1);

        // Initialize interior node and return node index and bounds
        LightBounds lbu = Union(child0.Item2, child1.Item2);
        CompactLightBounds cb = new(lbu, allLightBounds);
        nodes[nodeIndex] = LightBVHNode.MakeInterior((uint)child1.Item1, cb);
        return (nodeIndex, lbu);
    }

    private static Float EvaluateCost(in LightBounds b, in Bounds3f bounds, int dim)
    {
        // Evaluate direction bounds measure for _LightBounds_
        Float theta_o = FMath.Acos(b.cosTheta_o), theta_e = FMath.Acos(b.cosTheta_e);
        Float theta_w = FMath.Min(theta_o + theta_e, Pi);
        Float sinTheta_o = SafeSqrt(1 - Sqr(b.cosTheta_o));
        Float M_omega = 2 * Pi * (1 - b.cosTheta_o) +
                        Pi / 2 * (2 * theta_w * sinTheta_o - FMath.Cos(theta_o - 2 * theta_w) -
                                  2 * theta_o * sinTheta_o + b.cosTheta_o);

        // Return complete cost estimate for _LightBounds_
        Float Kr = MaxComponentValue(bounds.Diagonal()) / bounds.Diagonal()[dim];
        return b.phi * M_omega * Kr * b.bounds.SurfaceArea();
    }
}

// ExhaustiveLightSampler Definition
public sealed class ExhaustiveLightSampler : LightSampler
{
    private readonly Light[] lights;
    private readonly List<Light> boundedLights = [], infiniteLights = [];
    private readonly List<LightBounds> lightBounds = [];
    private readonly Dictionary<Light, int> lightToBoundedIndex = new(ReferenceEqualityComparer.Instance);

    // ExhaustiveLightSampler Method Definitions
    public ExhaustiveLightSampler(ReadOnlySpan<Light> lights)
    {
        this.lights = lights.ToArray();
        foreach (Light light in lights)
        {
            if (light.Bounds() is LightBounds lb)
            {
                lightToBoundedIndex[light] = boundedLights.Count;
                lightBounds.Add(lb);
                boundedLights.Add(light);
            }
            else
                infiniteLights.Add(light);
        }
    }

    public override SampledLight? Sample(in LightSampleContext ctx, Float u)
    {
        Float pInfinite = (Float)infiniteLights.Count / (Float)(infiniteLights.Count + (lightBounds.Count > 0 ? 1 : 0));

        // Note: shared with BVH light sampler...
        if (u < pInfinite)
        {
            u /= pInfinite;
            int index = Math.Min((int)(u * infiniteLights.Count), infiniteLights.Count - 1);
            Float pdf = pInfinite * 1f / infiniteLights.Count;
            return new SampledLight(infiniteLights[index], pdf);
        }
        else
        {
            u = FMath.Min((u - pInfinite) / (1 - pInfinite), OneMinusEpsilon);

            ulong seed = MixBits(FloatToBits(u));
            WeightedReservoirSampler<Light> wrs = new(seed);

            for (int i = 0; i < boundedLights.Count; ++i)
                wrs.Add(boundedLights[i], lightBounds[i].Importance(ctx.p(), ctx.n));

            if (!wrs.HasSample())
                return null;

            Float pdf = (1f - pInfinite) * wrs.SampleProbability();
            return new SampledLight(wrs.GetSample(), pdf);
        }
    }

    public override Float PMF(in LightSampleContext ctx, Light light)
    {
        if (!lightToBoundedIndex.ContainsKey(light))
            return 1f / (infiniteLights.Count + (lightBounds.Count > 0 ? 1 : 0));

        Float importanceSum = 0;
        Float lightImportance = 0;
        for (int i = 0; i < boundedLights.Count; ++i)
        {
            Float importance = lightBounds[i].Importance(ctx.p(), ctx.n);
            importanceSum += importance;
            if (ReferenceEquals(light, boundedLights[i]))
                lightImportance = importance;
        }
        Float pInfinite = (Float)infiniteLights.Count / (Float)(infiniteLights.Count + (lightBounds.Count > 0 ? 1 : 0));
        // (pbrt computes this in double.)
        Float pdf = (Float)(lightImportance / importanceSum * (1.0 - pInfinite));
        return pdf;
    }

    public override SampledLight? Sample(Float u)
    {
        if (lights.Length == 0)
            return null;

        int lightIndex = Math.Min((int)(u * lights.Length), lights.Length - 1);
        return new SampledLight(lights[lightIndex], 1f / lights.Length);
    }

    public override Float PMF(Light light)
    {
        if (lights.Length == 0)
            return 0;
        return 1f / lights.Length;
    }

    public override string ToString() =>
        $"[ ExhaustiveLightSampler lightBounds: [ {string.Join(", ", lightBounds)} ]]";
}

public abstract partial class LightSampler
{
    public static LightSampler Create(string name, ReadOnlySpan<Light> lights)
    {
        if (name == "uniform")
            return new UniformLightSampler(lights);
        else if (name == "power")
            return new PowerLightSampler(lights);
        else if (name == "bvh")
            return new BVHLightSampler(lights);
        else if (name == "exhaustive")
            return new ExhaustiveLightSampler(lights);
        else
        {
            Error($"Light sample distribution type \"{name}\" unknown. Using \"bvh\".");
            return new BVHLightSampler(lights);
        }
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/cpu/primitive.h and primitive.cpp
//
// pbrt's Primitive TaggedPointer is an abstract class that the primitives
// and the aggregates derive from.

namespace Pbrt;

// Primitive Definition
public abstract class Primitive
{
    internal static readonly StatMemoryCounter primitiveMemory = new("Memory/Primitives");

    // Primitive Interface
    public abstract Bounds3f Bounds();
    public abstract ShapeIntersection? Intersect(in Ray r, Float tMax = Infinity);
    public abstract bool IntersectP(in Ray r, Float tMax = Infinity);
}

// GeometricPrimitive Definition
public sealed class GeometricPrimitive : Primitive
{
    // GeometricPrimitive Private Members
    private readonly Shape shape;
    private readonly Material? material;
    private readonly Light? areaLight;
    private readonly MediumInterface mediumInterface;
    private readonly FloatTexture? alpha;

    // GeometricPrimitive Method Definitions
    public GeometricPrimitive(Shape shape, Material? material, Light? areaLight,
                              in MediumInterface mediumInterface, FloatTexture? alpha)
    {
        this.shape = shape;
        this.material = material;
        this.areaLight = areaLight;
        this.mediumInterface = mediumInterface;
        this.alpha = alpha;
        // (sizeof(GeometricPrimitive) in pbrt)
        primitiveMemory.Add(48);
    }

    public override Bounds3f Bounds() => shape.Bounds();

    public override ShapeIntersection? Intersect(in Ray r, Float tMax = Infinity)
    {
        ShapeIntersection? osi = shape.Intersect(r, tMax);
        if (osi is not { } si)
            return null;
        CHECK_LT((double)si.tHit, 1.001 * tMax);
        // Test intersection against alpha texture, if present
        if (alpha != null)
        {
            Float a = alpha.Evaluate(si.intr);
            if (a < 1)
            {
                // Possibly ignore intersection based on stochastic alpha test
                Float u = (a <= 0) ? 1f : HashFloat(r.o, r.d);
                if (u > a)
                {
                    // Ignore this intersection and trace a new ray
                    Ray rNext = si.intr.SpawnRay(r.d);
                    ShapeIntersection? siNext = Intersect(rNext, tMax - si.tHit);
                    if (siNext is { } sn)
                    {
                        sn.tHit += si.tHit;
                        return sn;
                    }
                    return null;
                }
            }
        }

        // Initialize _SurfaceInteraction_ after _Shape_ intersection
        si.intr.SetIntersectionProperties(material, areaLight, mediumInterface, r.medium);

        return si;
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity)
    {
        if (alpha != null)
            return Intersect(r, tMax).HasValue;
        else
            return shape.IntersectP(r, tMax);
    }
}

// SimplePrimitive Definition
public sealed class SimplePrimitive : Primitive
{
    // SimplePrimitive Private Members
    private readonly Shape shape;
    private readonly Material? material;

    // SimplePrimitive Method Definitions
    public SimplePrimitive(Shape shape, Material? material)
    {
        this.shape = shape;
        this.material = material;
        // (sizeof(SimplePrimitive) in pbrt)
        primitiveMemory.Add(16);
    }

    public override Bounds3f Bounds() => shape.Bounds();

    public override bool IntersectP(in Ray r, Float tMax = Infinity) => shape.IntersectP(r, tMax);

    public override ShapeIntersection? Intersect(in Ray r, Float tMax = Infinity)
    {
        ShapeIntersection? osi = shape.Intersect(r, tMax);
        if (osi is not { } si)
            return null;

        si.intr.SetIntersectionProperties(material, null, null, r.medium);

        return si;
    }
}

// TransformedPrimitive Definition
public sealed class TransformedPrimitive : Primitive
{
    // TransformedPrimitive Private Members
    private readonly Primitive primitive;
    private readonly Transform renderFromPrimitive;

    // TransformedPrimitive Public Methods
    public TransformedPrimitive(Primitive primitive, Transform renderFromPrimitive)
    {
        this.primitive = primitive;
        this.renderFromPrimitive = renderFromPrimitive;
        // (sizeof(TransformedPrimitive) in pbrt)
        primitiveMemory.Add(16);
    }

    public override Bounds3f Bounds() => renderFromPrimitive.Apply(primitive.Bounds());

    // TransformedPrimitive Method Definitions
    public override ShapeIntersection? Intersect(in Ray r, Float tMax = Infinity)
    {
        // Transform ray to primitive-space and intersect with primitive
        Ray ray = renderFromPrimitive.ApplyInverse(r, ref tMax);
        ShapeIntersection? osi = primitive.Intersect(ray, tMax);
        if (osi is not { } si)
            return null;
        CHECK_LT((double)si.tHit, 1.001 * tMax);

        // Return transformed instance's intersection information
        si.intr = renderFromPrimitive.Apply(si.intr);
        CHECK_GE(Dot(si.intr.n, si.intr.shading.n), 0f);
        return si;
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity)
    {
        Ray ray = renderFromPrimitive.ApplyInverse(r, ref tMax);
        return primitive.IntersectP(ray, tMax);
    }
}

// AnimatedPrimitive Definition
public sealed class AnimatedPrimitive : Primitive
{
    // AnimatedPrimitive Private Members
    private readonly Primitive primitive;
    private readonly AnimatedTransform renderFromPrimitive;

    // AnimatedPrimitive Public Methods
    public AnimatedPrimitive(Primitive p, AnimatedTransform renderFromPrimitive)
    {
        primitive = p;
        this.renderFromPrimitive = renderFromPrimitive;
        // (sizeof(AnimatedPrimitive) in pbrt)
        primitiveMemory.Add(704);
        CHECK(renderFromPrimitive.IsAnimated());
    }

    public override Bounds3f Bounds() => renderFromPrimitive.MotionBounds(primitive.Bounds());

    // AnimatedPrimitive Method Definitions
    public override ShapeIntersection? Intersect(in Ray r, Float tMax = Infinity)
    {
        // Compute _ray_ after transformation by _renderFromPrimitive_
        Transform interpRenderFromPrimitive = renderFromPrimitive.Interpolate(r.time);
        Ray ray = interpRenderFromPrimitive.ApplyInverse(r, ref tMax);
        ShapeIntersection? osi = primitive.Intersect(ray, tMax);
        if (osi is not { } si)
            return null;

        // Transform instance's intersection data to rendering space
        si.intr = interpRenderFromPrimitive.Apply(si.intr);
        CHECK_GE(Dot(si.intr.n, si.intr.shading.n), 0f);
        return si;
    }

    public override bool IntersectP(in Ray r, Float tMax = Infinity)
    {
        Ray ray = renderFromPrimitive.ApplyInverse(r, ref tMax);
        return primitive.IntersectP(ray, tMax);
    }
}

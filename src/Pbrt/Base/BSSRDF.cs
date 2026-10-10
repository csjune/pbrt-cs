// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/base/bssrdf.h
//
// pbrt's BSSRDF TaggedPointer is an abstract class; TabulatedBSSRDF is in
// Bssrdf.cs. ProbeIntersectionToSample() allocates the BSSRDF's BxDF
// instead of taking it from a ScratchBuffer.

namespace Pbrt;

// BSSRDF Definition
public abstract class BSSRDF
{
    // BSSRDF Interface
    public abstract BSSRDFProbeSegment? SampleSp(Float u1, Point2f u2);

    public abstract BSSRDFSample ProbeIntersectionToSample(in SubsurfaceInteraction si);
}

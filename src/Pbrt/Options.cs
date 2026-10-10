// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/options.h and options.cpp
//
// pbrt's global PBRTOptions *Options is set by InitPBRT(); here Util.Options
// starts out with the default options, so code that reads it works before
// that. The GPU copy of the options isn't needed.

namespace Pbrt;

// RenderingCoordinateSystem Definition
public enum RenderingCoordinateSystem { Camera, CameraWorld, World }

// BasicPBRTOptions Definition
public class BasicPBRTOptions
{
    public int seed = 0;
    public bool quiet = false;
    public bool disablePixelJitter = false, disableWavelengthJitter = false;
    public bool disableTextureFiltering = false;
    public bool disableImageTextures = false;
    public bool forceDiffuse = false;
    public bool useGPU = false;
    public bool wavefront = false;
    public bool interactive = false;
    public bool fullscreen = false;
    public RenderingCoordinateSystem renderingSpace = RenderingCoordinateSystem.CameraWorld;
}

// PBRTOptions Definition
public sealed class PBRTOptions : BasicPBRTOptions
{
    public int nThreads = 0;
    public LogLevel logLevel = LogLevel.Error;
    public string logFile = "";
    public bool logUtilization = false;
    public bool writePartialImages = false;
    public bool recordPixelStatistics = false;
    public bool printStatistics = false;
    public int? pixelSamples;
    public int? gpuDevice;
    public bool quickRender = false;
    public bool upgrade = false;
    public string imageFile = "";
    public string mseReferenceImage = "", mseReferenceOutput = "";
    public string debugStart = "";
    public string displayServer = "";
    public Bounds2f? cropWindow;
    public Bounds2i? pixelBounds;
    public Point2i? pixelMaterial;
    public Float displacementEdgeScale = 1;

    public override string ToString()
    {
        static string B(bool b) => b ? "true" : "false";
        static string O<T>(T? v) where T : struct => v.HasValue ? v.Value.ToString()! : "[ unset ]";
        return $"[ PBRTOptions seed: {seed} quiet: {B(quiet)} disablePixelJitter: {B(disablePixelJitter)} " +
               $"disableWavelengthJitter: {B(disableWavelengthJitter)} disableTextureFiltering: {B(disableTextureFiltering)} " +
               $"disableImageTextures: {B(disableImageTextures)} forceDiffuse: {B(forceDiffuse)} useGPU: {B(useGPU)} " +
               $"wavefront: {B(wavefront)} interactive: {B(interactive)} fullscreen {B(fullscreen)} " +
               $"renderingSpace: {Util.ToString(renderingSpace)} nThreads: {nThreads} logLevel: {Util.ToString(logLevel)} " +
               $"logFile: {logFile} logUtilization: {B(logUtilization)} writePartialImages: {B(writePartialImages)} " +
               $"recordPixelStatistics: {B(recordPixelStatistics)} printStatistics: {B(printStatistics)} " +
               $"pixelSamples: {O(pixelSamples)} gpuDevice: {O(gpuDevice)} quickRender: {B(quickRender)} " +
               $"upgrade: {B(upgrade)} imageFile: {imageFile} mseReferenceImage: {mseReferenceImage} " +
               $"mseReferenceOutput: {mseReferenceOutput} debugStart: {debugStart} displayServer: {displayServer} " +
               $"cropWindow: {O(cropWindow)} pixelBounds: {O(pixelBounds)} pixelMaterial: {O(pixelMaterial)} " +
               $"displacementEdgeScale: {Util.FloatToString(displacementEdgeScale)} ]";
    }
}

public static partial class Util
{
    // Options Global Variable Declaration
    public static PBRTOptions Options = new();

    // Options Inline Functions
    public static BasicPBRTOptions GetOptions() => Options;

    public static string ToString(RenderingCoordinateSystem r)
    {
        if (r == RenderingCoordinateSystem.Camera)
            return "RenderingCoordinateSystem::Camera";
        else if (r == RenderingCoordinateSystem.CameraWorld)
            return "RenderingCoordinateSystem::CameraWorld";
        else
        {
            CHECK(r == RenderingCoordinateSystem.World);
            return "RenderingCoordinateSystem::World";
        }
    }
}

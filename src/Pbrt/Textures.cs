// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/textures.h and textures.cpp
//
// TextureMapping2D/TextureMapping3D are abstract classes. ImageTextureBase
// holds the shared texture cache; since FloatImageTexture and
// SpectrumImageTexture already derive from FloatTexture/SpectrumTexture,
// they have their own copies of its members and get their MIPMap from
// ImageTextureBase.GetMIPMap().
//
// The Ptex textures use the port of the Ptex library in Ptex/. The GPU
// textures aren't ported.

namespace Pbrt;

// TextureEvalContext Definition
public struct TextureEvalContext
{
    public Point3f p;
    public Vector3f dpdx, dpdy;
    public Normal3f n;
    public Point2f uv;
    public Float dudx = 0, dudy = 0, dvdx = 0, dvdy = 0;
    public int faceIndex = 0;

    // TextureEvalContext Public Methods
    public TextureEvalContext() { }

    public TextureEvalContext(in Interaction intr)
    {
        p = intr.p();
        uv = intr.uv;
    }

    public TextureEvalContext(in SurfaceInteraction si)
    {
        p = si.p();
        dpdx = si.dpdx;
        dpdy = si.dpdy;
        n = si.n;
        uv = si.uv;
        dudx = si.dudx;
        dudy = si.dudy;
        dvdx = si.dvdx;
        dvdy = si.dvdy;
        faceIndex = si.faceIndex;
    }

    public TextureEvalContext(Point3f p, Vector3f dpdx, Vector3f dpdy, Normal3f n, Point2f uv,
                              Float dudx, Float dudy, Float dvdx, Float dvdy, int faceIndex)
    {
        this.p = p;
        this.dpdx = dpdx;
        this.dpdy = dpdy;
        this.n = n;
        this.uv = uv;
        this.dudx = dudx;
        this.dudy = dudy;
        this.dvdx = dvdx;
        this.dvdy = dvdy;
        this.faceIndex = faceIndex;
    }

    public static implicit operator TextureEvalContext(in SurfaceInteraction si) => new(si);

    public override readonly string ToString() =>
        $"[ TextureEvalContext p: {p} dpdx: {dpdx} dpdy: {dpdy} n: {n} uv: {uv} " +
        $"dudx: {FloatToString(dudx)} dudy: {FloatToString(dudy)} dvdx: {FloatToString(dvdx)} " +
        $"dvdy: {FloatToString(dvdy)} faceIndex: {faceIndex} ]";
}

// TexCoord2D Definition
public struct TexCoord2D(Point2f st, Float dsdx, Float dsdy, Float dtdx, Float dtdy)
{
    public Point2f st = st;
    public Float dsdx = dsdx, dsdy = dsdy, dtdx = dtdx, dtdy = dtdy;

    public override readonly string ToString() =>
        $"[ TexCoord2D st: {st} dsdx: {FloatToString(dsdx)} dsdy: {FloatToString(dsdy)} " +
        $"dtdx: {FloatToString(dtdx)} dtdy: {FloatToString(dtdy)} ]";
}

// TexCoord3D Definition
public struct TexCoord3D(Point3f p, Vector3f dpdx, Vector3f dpdy)
{
    public Point3f p = p;
    public Vector3f dpdx = dpdx, dpdy = dpdy;

    public override readonly string ToString() => $"[ TexCoord3D p: {p} dpdx: {dpdx} dpdy: {dpdy} ]";
}

// TextureMapping2D Definition
public abstract class TextureMapping2D
{
    // TextureMapping2D Interface
    public abstract TexCoord2D Map(in TextureEvalContext ctx);

    public static TextureMapping2D Create(ParameterDictionary parameters, Transform renderFromTexture,
                                          FileLoc? loc)
    {
        string type = parameters.GetOneString("mapping", "uv");
        if (type == "uv")
        {
            Float su = parameters.GetOneFloat("uscale", 1f);
            Float sv = parameters.GetOneFloat("vscale", 1f);
            Float du = parameters.GetOneFloat("udelta", 0f);
            Float dv = parameters.GetOneFloat("vdelta", 0f);
            return new UVMapping(su, sv, du, dv);
        }
        else if (type == "spherical")
            return new SphericalMapping(Inverse(renderFromTexture));
        else if (type == "cylindrical")
            return new CylindricalMapping(Inverse(renderFromTexture));
        else if (type == "planar")
        {
            // (GCC evaluates the arguments from right to left.)
            Float dt = parameters.GetOneFloat("vdelta", 0f);
            Float ds = parameters.GetOneFloat("udelta", 0f);
            Vector3f vt = parameters.GetOneVector3f("v2", new Vector3f(0, 1, 0));
            Vector3f vs = parameters.GetOneVector3f("v1", new Vector3f(1, 0, 0));
            return new PlanarMapping(Inverse(renderFromTexture), vs, vt, ds, dt);
        }
        else
        {
            Error(loc, $"2D texture mapping \"{type}\" unknown");
            return new UVMapping();
        }
    }
}

// UVMapping Definition
public sealed class UVMapping(Float su = 1, Float sv = 1, Float du = 0, Float dv = 0) : TextureMapping2D
{
    // UVMapping Public Methods
    public override string ToString() =>
        $"[ UVMapping su: {FloatToString(su)} sv: {FloatToString(sv)} du: {FloatToString(du)} dv: {FloatToString(dv)} ]";

    public override TexCoord2D Map(in TextureEvalContext ctx)
    {
        // Compute texture differentials for 2D $(u,v)$ mapping
        Float dsdx = su * ctx.dudx, dsdy = su * ctx.dudy;
        Float dtdx = sv * ctx.dvdx, dtdy = sv * ctx.dvdy;

        Point2f st = new(su * ctx.uv[0] + du, sv * ctx.uv[1] + dv);
        return new TexCoord2D(st, dsdx, dsdy, dtdx, dtdy);
    }
}

// SphericalMapping Definition
public sealed class SphericalMapping(Transform textureFromRender) : TextureMapping2D
{
    // SphericalMapping Public Methods
    public override string ToString() => $"[ SphericalMapping textureFromRender: {textureFromRender} ]";

    public override TexCoord2D Map(in TextureEvalContext ctx)
    {
        Point3f pt = textureFromRender.Apply(ctx.p);
        // Compute $\partial\,s/\partial\,\pt{}$ and $\partial\,t/\partial\,\pt{}$ for
        // spherical mapping
        Float x2y2 = Sqr(pt.x) + Sqr(pt.y);
        Float sqrtx2y2 = FMath.Sqrt(x2y2);
        Vector3f dsdp = new Vector3f(-pt.y, pt.x, 0) / (2 * Pi * x2y2);
        Vector3f dtdp = 1 / (Pi * (x2y2 + Sqr(pt.z))) *
                        new Vector3f(pt.x * pt.z / sqrtx2y2, pt.y * pt.z / sqrtx2y2, -sqrtx2y2);

        // Compute texture coordinate differentials for spherical mapping
        Vector3f dpdx = textureFromRender.Apply(ctx.dpdx);
        Vector3f dpdy = textureFromRender.Apply(ctx.dpdy);
        Float dsdx = Dot(dsdp, dpdx), dsdy = Dot(dsdp, dpdy);
        Float dtdx = Dot(dtdp, dpdx), dtdy = Dot(dtdp, dpdy);

        // Return $(s,t)$ texture coordinates and differentials based on spherical mapping
        Vector3f vec = Normalize(pt - new Point3f(0, 0, 0));
        Point2f st = new(SphericalTheta(vec) * InvPi, SphericalPhi(vec) * Inv2Pi);
        return new TexCoord2D(st, dsdx, dsdy, dtdx, dtdy);
    }
}

// CylindricalMapping Definition
public sealed class CylindricalMapping(Transform textureFromRender) : TextureMapping2D
{
    // CylindricalMapping Public Methods
    public override string ToString() => $"[ CylindricalMapping textureFromRender: {textureFromRender} ]";

    public override TexCoord2D Map(in TextureEvalContext ctx)
    {
        Point3f pt = textureFromRender.Apply(ctx.p);
        // Compute texture coordinate differentials for cylinder $(u,v)$ mapping
        Float x2y2 = Sqr(pt.x) + Sqr(pt.y);
        Vector3f dsdp = new Vector3f(-pt.y, pt.x, 0) / (2 * Pi * x2y2), dtdp = new(0, 0, 1);
        Vector3f dpdx = textureFromRender.Apply(ctx.dpdx), dpdy = textureFromRender.Apply(ctx.dpdy);
        Float dsdx = Dot(dsdp, dpdx), dsdy = Dot(dsdp, dpdy);
        Float dtdx = Dot(dtdp, dpdx), dtdy = Dot(dtdp, dpdy);

        Point2f st = new((Pi + FMath.Atan2(pt.y, pt.x)) * Inv2Pi, pt.z);
        return new TexCoord2D(st, dsdx, dsdy, dtdx, dtdy);
    }
}

// PlanarMapping Definition
public sealed class PlanarMapping(Transform textureFromRender, Vector3f vs, Vector3f vt, Float ds, Float dt)
    : TextureMapping2D
{
    // PlanarMapping Public Methods
    public override TexCoord2D Map(in TextureEvalContext ctx)
    {
        Vector3f vec = new(textureFromRender.Apply(ctx.p));
        // Initialize partial derivatives of planar mapping $(s,t)$ coordinates
        Vector3f dpdx = textureFromRender.Apply(ctx.dpdx);
        Vector3f dpdy = textureFromRender.Apply(ctx.dpdy);
        Float dsdx = Dot(vs, dpdx), dsdy = Dot(vs, dpdy);
        Float dtdx = Dot(vt, dpdx), dtdy = Dot(vt, dpdy);

        Point2f st = new(ds + Dot(vec, vs), dt + Dot(vec, vt));
        return new TexCoord2D(st, dsdx, dsdy, dtdx, dtdy);
    }

    public override string ToString() =>
        $"[ PlanarMapping vs: {vs} vt: {vt} ds: {FloatToString(ds)} dt: {FloatToString(dt)}]";
}

// TextureMapping3D Definition
public abstract class TextureMapping3D
{
    // TextureMapping3D Interface
    public abstract TexCoord3D Map(in TextureEvalContext ctx);

    public static TextureMapping3D Create(ParameterDictionary parameters, Transform renderFromTexture,
                                          FileLoc? loc) =>
        new PointTransformMapping(Inverse(renderFromTexture));
}

// PointTransformMapping Definition
public sealed class PointTransformMapping(Transform textureFromRender) : TextureMapping3D
{
    // PointTransformMapping Public Methods
    public override string ToString() => $"[ PointTransformMapping textureFromRender: {textureFromRender} ]";

    public override TexCoord3D Map(in TextureEvalContext ctx) =>
        new(textureFromRender.Apply(ctx.p), textureFromRender.Apply(ctx.dpdx), textureFromRender.Apply(ctx.dpdy));
}

// FloatConstantTexture Definition
public sealed class FloatConstantTexture(Float value) : FloatTexture
{
    public override Float Evaluate(in TextureEvalContext ctx) => value;

    // FloatConstantTexture Public Methods
    public static FloatConstantTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                              FileLoc? loc) =>
        new(parameters.GetOneFloat("value", 1f));

    public override string ToString() => $"[ FloatConstantTexture value: {FloatToString(value)} ]";
}

// SpectrumConstantTexture Definition
public sealed class SpectrumConstantTexture(Spectrum value) : SpectrumTexture
{
    // SpectrumConstantTexture Public Methods
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda) =>
        value.Sample(lambda);

    public static SpectrumConstantTexture Create(Transform renderFromTexture,
                                                 TextureParameterDictionary parameters,
                                                 SpectrumType spectrumType, FileLoc? loc)
    {
        Spectrum one = new ConstantSpectrum(1);
        Spectrum c = parameters.GetOneSpectrum("value", one, spectrumType)!;
        return new SpectrumConstantTexture(c);
    }

    public override string ToString() => $"[ SpectrumConstantTexture value: {value} ]";
}

// FloatBilerpTexture Definition
public sealed class FloatBilerpTexture(TextureMapping2D mapping, Float v00, Float v01, Float v10, Float v11)
    : FloatTexture
{
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord2D c = mapping.Map(ctx);
        return (1 - c.st[0]) * (1 - c.st[1]) * v00 + c.st[0] * (1 - c.st[1]) * v10 +
               (1 - c.st[0]) * c.st[1] * v01 + c.st[0] * c.st[1] * v11;
    }

    public static FloatBilerpTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                            FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

        return new FloatBilerpTexture(map, parameters.GetOneFloat("v00", 0f), parameters.GetOneFloat("v01", 1f),
                                      parameters.GetOneFloat("v10", 0f), parameters.GetOneFloat("v11", 1f));
    }

    public override string ToString() =>
        $"[ FloatBilerpTexture mapping: {mapping} v00: {FloatToString(v00)} v01: {FloatToString(v01)} " +
        $"v10: {FloatToString(v10)} v11: {FloatToString(v11)} ]";
}

// SpectrumBilerpTexture Definition
public sealed class SpectrumBilerpTexture(TextureMapping2D mapping, Spectrum v00, Spectrum v01, Spectrum v10,
                                          Spectrum v11) : SpectrumTexture
{
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        TexCoord2D c = mapping.Map(ctx);
        return Bilerp(new Array2<Float>(c.st[0], c.st[1]),
                      [v00.Sample(lambda), v10.Sample(lambda), v01.Sample(lambda), v11.Sample(lambda)]);
    }

    public static SpectrumBilerpTexture Create(Transform renderFromTexture,
                                               TextureParameterDictionary parameters,
                                               SpectrumType spectrumType, FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

        Spectrum zero = new ConstantSpectrum(0);
        Spectrum one = new ConstantSpectrum(1);

        return new SpectrumBilerpTexture(map, parameters.GetOneSpectrum("v00", zero, spectrumType)!,
                                         parameters.GetOneSpectrum("v01", one, spectrumType)!,
                                         parameters.GetOneSpectrum("v10", zero, spectrumType)!,
                                         parameters.GetOneSpectrum("v11", one, spectrumType)!);
    }

    public override string ToString() =>
        $"[ SpectrumBilerpTexture mapping: {mapping} v00: {v00} v01: {v01} v10: {v10} v11: {v11} ]";
}

// FloatCheckerboardTexture Definition
public sealed class FloatCheckerboardTexture(TextureMapping2D? map2D, TextureMapping3D? map3D,
                                             FloatTexture tex1, FloatTexture tex2) : FloatTexture
{
    private readonly FloatTexture[] tex = [tex1, tex2];

    public override Float Evaluate(in TextureEvalContext ctx)
    {
        Float w = Checkerboard(ctx, map2D, map3D);
        Float t0 = 0, t1 = 0;
        if (w != 1)
            t0 = tex[0].Evaluate(ctx);
        if (w != 0)
            t1 = tex[1].Evaluate(ctx);
        return (1 - w) * t0 + w * t1;
    }

    public static FloatCheckerboardTexture? Create(Transform renderFromTexture,
                                                   TextureParameterDictionary parameters, FileLoc? loc)
    {
        int dim = parameters.GetOneInt("dimension", 2);
        if (dim != 2 && dim != 3)
        {
            Error(loc, $"{dim} dimensional checkerboard texture not supported");
            return null;
        }
        FloatTexture tex1 = parameters.GetFloatTexture("tex1", 1f);
        FloatTexture tex2 = parameters.GetFloatTexture("tex2", 0f);
        if (dim == 2)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

            return new FloatCheckerboardTexture(map, null, tex1, tex2);
        }
        else
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
            return new FloatCheckerboardTexture(null, map, tex1, tex2);
        }
    }

    public override string ToString() =>
        $"[ FloatCheckerboardTexture map2D: {map2D?.ToString() ?? "(nullptr)"} " +
        $"map3D: {map3D?.ToString() ?? "(nullptr)"} tex[0]: {tex[0]} tex[1]: {tex[1]} ";
}

// SpectrumCheckerboardTexture Definition
public sealed class SpectrumCheckerboardTexture(TextureMapping2D? map2D, TextureMapping3D? map3D,
                                                SpectrumTexture tex1, SpectrumTexture tex2) : SpectrumTexture
{
    // SpectrumCheckerboardTexture Private Members
    private readonly SpectrumTexture[] tex = [tex1, tex2];

    // SpectrumCheckerboardTexture Public Methods
    public static SpectrumCheckerboardTexture? Create(Transform renderFromTexture,
                                                      TextureParameterDictionary parameters,
                                                      SpectrumType spectrumType, FileLoc? loc)
    {
        int dim = parameters.GetOneInt("dimension", 2);
        if (dim != 2 && dim != 3)
        {
            Error(loc, $"{dim} dimensional checkerboard texture not supported");
            return null;
        }

        Spectrum zero = new ConstantSpectrum(0);
        Spectrum one = new ConstantSpectrum(1);

        SpectrumTexture tex1 = parameters.GetSpectrumTexture("tex1", one, spectrumType)!;
        SpectrumTexture tex2 = parameters.GetSpectrumTexture("tex2", zero, spectrumType)!;
        if (dim == 2)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

            return new SpectrumCheckerboardTexture(map, null, tex1, tex2);
        }
        else
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
            return new SpectrumCheckerboardTexture(null, map, tex1, tex2);
        }
    }

    public override string ToString() =>
        $"[ SpectrumCheckerboardTexture map2D: {map2D?.ToString() ?? "(nullptr)"} " +
        $"map3D: {map3D?.ToString() ?? "(nullptr)"} tex[0]: {tex[0]} tex[1]: {tex[1]} ";

    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        Float w = Checkerboard(ctx, map2D, map3D);
        SampledSpectrum t0 = default, t1 = default;
        if (w != 1)
            t0 = tex[0].Evaluate(ctx, lambda);
        if (w != 0)
            t1 = tex[1].Evaluate(ctx, lambda);
        return (1 - w) * t0 + w * t1;
    }
}

// FloatDotsTexture Definition
public sealed class FloatDotsTexture(TextureMapping2D mapping, FloatTexture outsideDot, FloatTexture insideDot)
    : FloatTexture
{
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord2D c = mapping.Map(ctx);
        return InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx) : outsideDot.Evaluate(ctx);
    }

    public static FloatDotsTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                          FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

        // (As in pbrt, "inside" is passed as outsideDot and "outside" as
        // insideDot.)
        return new FloatDotsTexture(map, parameters.GetFloatTexture("inside", 1f),
                                    parameters.GetFloatTexture("outside", 0f));
    }

    public override string ToString() =>
        $"[ FloatDotsTexture mapping: {mapping} insideDot: {insideDot} outsideDot: {outsideDot} ]";
}

// SpectrumDotsTexture Definition
public sealed class SpectrumDotsTexture(TextureMapping2D mapping, SpectrumTexture outsideDot,
                                        SpectrumTexture insideDot) : SpectrumTexture
{
    // SpectrumDotsTexture Public Methods
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        TexCoord2D c = mapping.Map(ctx);
        return InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx, lambda) : outsideDot.Evaluate(ctx, lambda);
    }

    public static SpectrumDotsTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                             SpectrumType spectrumType, FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);
        Spectrum zero = new ConstantSpectrum(0);
        Spectrum one = new ConstantSpectrum(1);

        return new SpectrumDotsTexture(map, parameters.GetSpectrumTexture("inside", one, spectrumType)!,
                                       parameters.GetSpectrumTexture("outside", zero, spectrumType)!);
    }

    public override string ToString() =>
        $"[ SpectrumDotsTexture mapping: {mapping} insideDot: {insideDot} outsideDot: {outsideDot} ]";
}

// FBmTexture Definition
public sealed class FBmTexture(TextureMapping3D mapping, int octaves, Float omega) : FloatTexture
{
    // FBmTexture Public Methods
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord3D c = mapping.Map(ctx);
        return FBm(c.p, c.dpdx, c.dpdy, omega, octaves);
    }

    public static FBmTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                    FileLoc? loc)
    {
        // Initialize 3D texture mapping _map_ from _tp_
        TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
        return new FBmTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f));
    }

    public override string ToString() =>
        $"[ FBmTexture mapping: {mapping} omega: {FloatToString(omega)} octaves: {octaves} ]";
}

// TexInfo Definition
public readonly record struct TexInfo(string filename, MIPMapFilterOptions filterOptions, WrapMode wrapMode,
                                      ColorEncoding encoding)
{
    public override string ToString() =>
        $"[ TexInfo filename: {filename} filterOptions: {filterOptions} wrapMode: {Util.ToString(wrapMode)} " +
        $"encoding: {encoding} ]";
}

// ImageTextureBase Definition
public static class ImageTextureBase
{
    // ImageTextureBase Private Members
    private static readonly Lock textureCacheMutex = new();
    private static readonly Dictionary<TexInfo, MIPMap> textureCache = [];

    // Returns the MIPMap that pbrt's ImageTextureBase constructor finds or
    // creates.
    public static MIPMap GetMIPMap(string filename, MIPMapFilterOptions filterOptions, WrapMode wrapMode,
                                   ColorEncoding encoding)
    {
        // Get _MIPMap_ from texture cache if present
        TexInfo texInfo = new(filename, filterOptions, wrapMode, encoding);
        lock (textureCacheMutex)
        {
            if (textureCache.TryGetValue(texInfo, out MIPMap? cached))
                return cached;
        }

        // Create _MIPMap_ for _filename_ and add to texture cache
        MIPMap mipmap = MIPMap.CreateFromFile(filename, filterOptions, wrapMode, encoding);
        lock (textureCacheMutex)
        {
            // This is actually ok, but if it hits, it means we've wastefully
            // loaded this texture. (Note that in that case, should just return
            // the one that's already in there and not replace it.)
            CHECK(!textureCache.ContainsKey(texInfo));
            textureCache[texInfo] = mipmap;
        }
        return mipmap;
    }

    public static void ClearCache()
    {
        lock (textureCacheMutex)
            textureCache.Clear();
    }

    // The parameters that FloatImageTexture::Create() and
    // SpectrumImageTexture::Create() both read.
    internal static (MIPMapFilterOptions filterOptions, WrapMode wrapMode, Float scale, bool invert, string filename,
        ColorEncoding encoding) GetParameters(TextureParameterDictionary parameters, FileLoc? loc)
    {
        // Initialize _ImageTexture_ parameters
        Float maxAniso = parameters.GetOneFloat("maxanisotropy", 8f);
        string filter = parameters.GetOneString("filter", "bilinear");
        MIPMapFilterOptions filterOptions = new() { maxAnisotropy = maxAniso };
        FilterFunction? ff = ParseFilter(filter);
        if (ff is FilterFunction f)
            filterOptions.filter = f;
        else
            Error(loc, $"{filter}: filter function unknown");

        string wrapString = parameters.GetOneString("wrap", "repeat");
        WrapMode? wrapMode = ParseWrapMode(wrapString);
        if (wrapMode == null)
            ErrorExit($"{wrapString}: wrap mode unknown");
        Float scale = parameters.GetOneFloat("scale", 1f);
        bool invert = parameters.GetOneBool("invert", false);
        string filename = ResolveFilename(parameters.GetOneString("filename", ""));

        string defaultEncoding = HasExtension(filename, "png") ? "sRGB" : "linear";
        string encodingString = parameters.GetOneString("encoding", defaultEncoding);
        ColorEncoding encoding = ColorEncoding.Get(encodingString);

        return (filterOptions, wrapMode!.Value, scale, invert, filename, encoding);
    }
}

// FloatImageTexture Definition
public sealed class FloatImageTexture : FloatTexture
{
    // ImageTextureBase Protected Members
    private readonly TextureMapping2D mapping;
    private readonly string filename;
    private Float scale;
    private readonly bool invert;
    private readonly MIPMap mipmap;

    public FloatImageTexture(TextureMapping2D m, string filename, MIPMapFilterOptions filterOptions, WrapMode wm,
                             Float scale, bool invert, ColorEncoding encoding)
    {
        mapping = m;
        this.filename = filename;
        this.scale = scale;
        this.invert = invert;
        mipmap = ImageTextureBase.GetMIPMap(filename, filterOptions, wm, encoding);
    }

    // A copy, like pbrt's copy constructor.
    public FloatImageTexture Clone() => (FloatImageTexture)MemberwiseClone();

    public void MultiplyScale(Float s) => scale *= s;

    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord2D c = mapping.Map(ctx);
        // Texture coordinates are (0,0) in the lower left corner, but
        // image coordinates are (0,0) in the upper left.
        c.st[1] = 1 - c.st[1];
        Float v = scale * mipmap.Filter<Float>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
        return invert ? FMath.Max(0, 1 - v) : v;
    }

    public static FloatImageTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                           FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

        var (filterOptions, wrapMode, scale, invert, filename, encoding) =
            ImageTextureBase.GetParameters(parameters, loc);
        return new FloatImageTexture(map, filename, filterOptions, wrapMode, scale, invert, encoding);
    }

    public override string ToString() =>
        $"[ FloatImageTexture filename: {filename} mapping: {mapping} scale: {FloatToString(scale)} " +
        $"invert: {(invert ? "true" : "false")} mipmap: {mipmap} ]";
}

// SpectrumImageTexture Definition
public sealed class SpectrumImageTexture : SpectrumTexture
{
    // ImageTextureBase Protected Members
    private readonly TextureMapping2D mapping;
    private readonly string filename;
    private Float scale;
    private readonly bool invert;
    private readonly MIPMap mipmap;

    // SpectrumImageTexture Private Members
    private readonly SpectrumType spectrumType;

    // SpectrumImageTexture Public Methods
    public SpectrumImageTexture(TextureMapping2D mapping, string filename, MIPMapFilterOptions filterOptions,
                                WrapMode wrapMode, Float scale, bool invert, ColorEncoding encoding,
                                SpectrumType spectrumType)
    {
        this.mapping = mapping;
        this.filename = filename;
        this.scale = scale;
        this.invert = invert;
        mipmap = ImageTextureBase.GetMIPMap(filename, filterOptions, wrapMode, encoding);
        this.spectrumType = spectrumType;
    }

    // A copy, like pbrt's copy constructor.
    public SpectrumImageTexture Clone() => (SpectrumImageTexture)MemberwiseClone();

    public void MultiplyScale(Float s) => scale *= s;

    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        // Apply texture mapping and flip $t$ coordinate for image texture lookup
        TexCoord2D c = mapping.Map(ctx);
        c.st[1] = 1 - c.st[1];

        // Lookup filtered RGB value in _MIPMap_
        RGB rgb = scale * mipmap.Filter<RGB>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
        rgb = ClampZero(invert ? (new RGB(1, 1, 1) - rgb) : rgb);

        // Return _SampledSpectrum_ for RGB image texture value
        if (mipmap.GetRGBColorSpace() is RGBColorSpace cs)
        {
            if (spectrumType == SpectrumType.Unbounded)
                return new RGBUnboundedSpectrum(cs, rgb).Sample(lambda);
            else if (spectrumType == SpectrumType.Albedo)
                return new RGBAlbedoSpectrum(cs, Clamp(rgb, 0, 1)).Sample(lambda);
            else
                return new RGBIlluminantSpectrum(cs, rgb).Sample(lambda);
        }
        // otherwise it better be a one-channel texture
        DCHECK(rgb[0] == rgb[1] && rgb[1] == rgb[2]);
        return new SampledSpectrum(rgb[0]);
    }

    public static SpectrumImageTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                              SpectrumType spectrumType, FileLoc? loc)
    {
        // Initialize 2D texture mapping _map_ from _tp_
        TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc);

        var (filterOptions, wrapMode, scale, invert, filename, encoding) =
            ImageTextureBase.GetParameters(parameters, loc);
        return new SpectrumImageTexture(map, filename, filterOptions, wrapMode, scale, invert, encoding,
                                        spectrumType);
    }

    public override string ToString() =>
        $"[ SpectrumImageTexture filename: {filename} mapping: {mapping} scale: {FloatToString(scale)} " +
        $"invert: {(invert ? "true" : "false")} mipmap: {mipmap} ]";
}

// MarbleTexture Definition
public sealed class MarbleTexture(TextureMapping3D mapping, int octaves, Float omega, Float scale, Float variation)
    : SpectrumTexture
{
    private static readonly RGB[] colors =
    [
        new(.58f, .58f, .6f), new(.58f, .58f, .6f), new(.58f, .58f, .6f),
        new(.5f, .5f, .5f), new(.6f, .59f, .58f), new(.58f, .58f, .6f),
        new(.58f, .58f, .6f), new(.2f, .2f, .33f), new(.58f, .58f, .6f),
    ];

    // MarbleTexture Public Methods
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        TexCoord3D c = mapping.Map(ctx);
        c.p *= scale;
        Float marble = c.p.y + variation * FBm(c.p, scale * c.dpdx, scale * c.dpdy, omega, octaves);
        Float t = .5f + .5f * FMath.Sin(marble);
        // Evaluate marble spline at $t$ to compute color _rgb_
        int nSeg = colors.Length - 3;
        int first = Math.Min((int)FMath.Floor(t * nSeg), nSeg - 1);
        t = t * nSeg - first;
        RGB rgb = 1.5f * EvaluateCubicBezier(colors.AsSpan(first, 4), t);

        return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
    }

    public static MarbleTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                       FileLoc? loc)
    {
        // Initialize 3D texture mapping _map_ from _tp_
        TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
        return new MarbleTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f),
                                 parameters.GetOneFloat("scale", 1f), parameters.GetOneFloat("variation", .2f));
    }

    public override string ToString() =>
        $"[ MarbleTexture mapping: {mapping} octaves: {octaves} omega: {FloatToString(omega)} " +
        $"scale: {FloatToString(scale)} variation: {FloatToString(variation)} ]";
}

// FloatMixTexture Definition
public sealed class FloatMixTexture(FloatTexture tex1, FloatTexture tex2, FloatTexture amount) : FloatTexture
{
    // FloatMixTexture Public Methods
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        Float amt = amount.Evaluate(ctx);
        Float t1 = 0, t2 = 0;
        if (amt != 1)
            t1 = tex1.Evaluate(ctx);
        if (amt != 0)
            t2 = tex2.Evaluate(ctx);
        return (1 - amt) * t1 + amt * t2;
    }

    public static FloatMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                         FileLoc? loc) =>
        new(parameters.GetFloatTexture("tex1", 0f), parameters.GetFloatTexture("tex2", 1f),
            parameters.GetFloatTexture("amount", 0.5f));

    public override string ToString() => $"[ FloatMixTexture tex1: {tex1} tex2: {tex2} amount: {amount} ]";
}

// FloatDirectionMixTexture Definition
public sealed class FloatDirectionMixTexture(FloatTexture tex1, FloatTexture tex2, Vector3f dir) : FloatTexture
{
    // FloatDirectionMixTexture Public Methods
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        Float amt = AbsDot(ctx.n, dir);
        Float t1 = 0, t2 = 0;
        if (amt != 0)
            t1 = tex1.Evaluate(ctx);
        if (amt != 1)
            t2 = tex2.Evaluate(ctx);
        return amt * t1 + (1 - amt) * t2;
    }

    public static FloatDirectionMixTexture Create(Transform renderFromTexture,
                                                  TextureParameterDictionary parameters, FileLoc? loc)
    {
        Vector3f dir = parameters.GetOneVector3f("dir", new Vector3f(0, 1, 0));
        dir = Normalize(renderFromTexture.Apply(dir));
        return new FloatDirectionMixTexture(parameters.GetFloatTexture("tex1", 0f),
                                            parameters.GetFloatTexture("tex2", 1f), dir);
    }

    public override string ToString() => $"[ FloatDirectionMixTexture tex1: {tex1} tex2: {tex2} dir: {dir} ]";
}

// SpectrumMixTexture Definition
public sealed class SpectrumMixTexture(SpectrumTexture tex1, SpectrumTexture tex2, FloatTexture amount)
    : SpectrumTexture
{
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        Float amt = amount.Evaluate(ctx);
        SampledSpectrum t1 = default, t2 = default;
        if (amt != 1)
            t1 = tex1.Evaluate(ctx, lambda);
        if (amt != 0)
            t2 = tex2.Evaluate(ctx, lambda);
        return (1 - amt) * t1 + amt * t2;
    }

    public static SpectrumMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                            SpectrumType spectrumType, FileLoc? loc)
    {
        Spectrum zero = new ConstantSpectrum(0);
        Spectrum one = new ConstantSpectrum(1);
        return new SpectrumMixTexture(parameters.GetSpectrumTexture("tex1", zero, spectrumType)!,
                                      parameters.GetSpectrumTexture("tex2", one, spectrumType)!,
                                      parameters.GetFloatTexture("amount", 0.5f));
    }

    public override string ToString() => $"[ SpectrumMixTexture tex1: {tex1} tex2: {tex2} amount: {amount} ]";
}

// SpectrumDirectionMixTexture Definition
public sealed class SpectrumDirectionMixTexture(SpectrumTexture tex1, SpectrumTexture tex2, Vector3f dir)
    : SpectrumTexture
{
    // SpectrumDirectionMixTexture Public Methods
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        Float amt = AbsDot(ctx.n, dir);
        SampledSpectrum t1 = default, t2 = default;
        if (amt != 0)
            t1 = tex1.Evaluate(ctx, lambda);
        if (amt != 1)
            t2 = tex2.Evaluate(ctx, lambda);
        return amt * t1 + (1 - amt) * t2;
    }

    public static SpectrumDirectionMixTexture Create(Transform renderFromTexture,
                                                     TextureParameterDictionary parameters,
                                                     SpectrumType spectrumType, FileLoc? loc)
    {
        Spectrum zero = new ConstantSpectrum(0);
        Spectrum one = new ConstantSpectrum(1);
        Vector3f dir = parameters.GetOneVector3f("dir", new Vector3f(0, 1, 0));
        dir = Normalize(renderFromTexture.Apply(dir));
        return new SpectrumDirectionMixTexture(parameters.GetSpectrumTexture("tex1", zero, spectrumType)!,
                                               parameters.GetSpectrumTexture("tex2", one, spectrumType)!, dir);
    }

    public override string ToString() =>
        $"[ SpectrumDirectionMixTexture tex1: {tex1} tex2: {tex2} dir: {dir} ]";
}

// PtexTexture Declarations
public sealed class PtexTextureBase
{
    private static readonly StatCounter nLookups = new("Texture/Ptex lookups");

    private static readonly object ptexMutex = new();
    private static Ptex.PtexCache? cache;

    private readonly bool valid;
    private readonly string filename;
    private readonly ColorEncoding encoding;
    private readonly Float scale;

    public PtexTextureBase(string filename, ColorEncoding encoding, Float scale)
    {
        this.filename = filename;
        this.encoding = encoding;
        this.scale = scale;

        lock (ptexMutex)
        {
            if (cache == null)
            {
                // (pbrt's cache holds up to 100 files and 4GB; this one keeps
                // every file.)
                bool premultiply = true;
                cache = new Ptex.PtexCache(premultiply, error => Error(error));
            }
        }

        // Issue an error if the texture doesn't exist or has an unsupported
        // number of channels.
        valid = false;
        Ptex.PtexReader? texture = cache.Get(filename, out string err);
        if (texture == null)
            Error(err);
        else
        {
            if (texture.numChannels() != 1 && texture.numChannels() != 3)
                Error($"{filename}: only one and three channel ptex textures are supported");
            else
                valid = true;
        }
    }

    // (pbrt reports the Ptex cache's file and block read statistics here;
    // the port's cache doesn't track them.)
    public static void ReportStats() { }

    public int SampleTexture(in TextureEvalContext ctx, Span<float> result)
    {
        if (!valid)
        {
            result[0] = 0;
            return 1;
        }

        nLookups.Inc();
        Ptex.PtexReader? texture = cache!.Get(filename, out _);
        CHECK(texture != null);
        // TODO: make the filter an option?
        Ptex.FilterOptions opts = new(Ptex.FilterType.BSpline);
        Ptex.PtexFilter filter = Ptex.PtexFilter.getFilter(texture!, opts)!;
        int nc = texture!.numChannels();

        int firstChan = 0;
        filter.eval(result, firstChan, nc, ctx.faceIndex, (float)ctx.uv[0], (float)ctx.uv[1], (float)ctx.dudx,
                    (float)ctx.dvdx, (float)ctx.dudy, (float)ctx.dvdy);

        if (encoding != ColorEncoding.Linear)
        {
            // It feels a little dirty to convert to 8-bits to run through the
            // encoding, but it's probably fine.
            Span<byte> result8 = stackalloc byte[3];
            for (int i = 0; i < nc; ++i)
                result8[i] = (byte)Clamp(result[i] * 255f + 0.5f, 0, 255);

            Span<Float> fResult = stackalloc Float[3];
            encoding.ToLinear(result8[..nc], fResult[..nc]);
            for (int c = 0; c < nc; ++c)
                result[c] = (float)fResult[c];
        }

        for (int c = 0; c < nc; ++c)
            result[c] = (float)(result[c] * scale);

        return nc;
    }

    public string BaseToString() =>
        $"valid: {(valid ? "true" : "false")} filename: {filename} encoding: {encoding} scale: {FloatToString(scale)}";
}

public sealed class FloatPtexTexture(string filename, ColorEncoding encoding, Float scale) : FloatTexture
{
    private readonly PtexTextureBase ptexBase = new(filename, encoding, scale);

    public override Float Evaluate(in TextureEvalContext ctx)
    {
        Span<float> result = stackalloc float[3];
        int nc = ptexBase.SampleTexture(ctx, result);
        if (nc == 1)
            return result[0];
        DCHECK_EQ(3, nc);
        return (result[0] + result[1] + result[2]) / 3;
    }

    public static FloatPtexTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                          FileLoc? loc)
    {
        string filename = ResolveFilename(parameters.GetOneString("filename", ""));
        string encodingString = parameters.GetOneString("encoding", "gamma 2.2");
        ColorEncoding encoding = ColorEncoding.Get(encodingString);
        Float scale = parameters.GetOneFloat("scale", 1f);
        return new FloatPtexTexture(filename, encoding, scale);
    }

    public override string ToString() => $"[ FloatPtexTexture {ptexBase.BaseToString()} ]";
}

public sealed class SpectrumPtexTexture(string filename, ColorEncoding encoding, Float scale,
                                        SpectrumType spectrumType) : SpectrumTexture
{
    private readonly PtexTextureBase ptexBase = new(filename, encoding, scale);

    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        Span<float> result = stackalloc float[3];
        int nc = ptexBase.SampleTexture(ctx, result);
        if (nc == 1)
            return new SampledSpectrum(result[0]);
        DCHECK_EQ(3, nc);
        RGB rgb = new(result[0], result[1], result[2]);
        if (spectrumType == SpectrumType.Unbounded)
            return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
        else if (spectrumType == SpectrumType.Albedo)
            return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, Clamp(rgb, 0, 1)).Sample(lambda);
        else
            return new RGBIlluminantSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
    }

    public static SpectrumPtexTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                             SpectrumType spectrumType, FileLoc? loc)
    {
        string filename = ResolveFilename(parameters.GetOneString("filename", ""));
        string encodingString = parameters.GetOneString("encoding", "gamma 2.2");
        ColorEncoding encoding = ColorEncoding.Get(encodingString);
        Float scale = parameters.GetOneFloat("scale", 1f);
        return new SpectrumPtexTexture(filename, encoding, scale, spectrumType);
    }

    public override string ToString() => $"[ SpectrumPtexTexture {ptexBase.BaseToString()} ]";
}

// FloatScaledTexture Definition
public sealed class FloatScaledTexture(FloatTexture tex, FloatTexture scale) : FloatTexture
{
    // FloatScaledTexture Public Methods
    public static FloatTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                      FileLoc? loc)
    {
        FloatTexture tex = parameters.GetFloatTexture("tex", 1f);
        FloatTexture scale = parameters.GetFloatTexture("scale", 1f);

        for (int i = 0; i < 2; ++i)
        {
            if (scale is FloatConstantTexture cscale)
            {
                Float cs = cscale.Evaluate(new TextureEvalContext());
                if (cs == 1)
                    return tex;
                else if (tex is FloatImageTexture image)
                {
                    FloatImageTexture imageCopy = image.Clone();
                    imageCopy.MultiplyScale(cs);
                    return imageCopy;
                }
            }
            (tex, scale) = (scale, tex);
        }

        (tex, scale) = (scale, tex);
        return new FloatScaledTexture(tex, scale);
    }

    public override Float Evaluate(in TextureEvalContext ctx)
    {
        Float sc = scale.Evaluate(ctx);
        if (sc == 0)
            return 0;
        return tex.Evaluate(ctx) * sc;
    }

    public override string ToString() => $"[ FloatScaledTexture tex: {tex} scale: {scale} ]";
}

// SpectrumScaledTexture Definition
public sealed class SpectrumScaledTexture(SpectrumTexture tex, FloatTexture scale) : SpectrumTexture
{
    public override SampledSpectrum Evaluate(in TextureEvalContext ctx, in SampledWavelengths lambda)
    {
        Float sc = scale.Evaluate(ctx);
        if (sc == 0)
            return new SampledSpectrum(0f);
        return tex.Evaluate(ctx, lambda) * sc;
    }

    public static SpectrumTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                         SpectrumType spectrumType, FileLoc? loc)
    {
        Spectrum one = new ConstantSpectrum(1);
        SpectrumTexture tex = parameters.GetSpectrumTexture("tex", one, spectrumType)!;
        FloatTexture scale = parameters.GetFloatTexture("scale", 1f);

        if (scale is FloatConstantTexture cscale)
        {
            Float cs = cscale.Evaluate(new TextureEvalContext());
            if (cs == 1)
                return tex;
            else if (tex is SpectrumImageTexture image)
            {
                SpectrumImageTexture imageCopy = image.Clone();
                imageCopy.MultiplyScale(cs);
                return imageCopy;
            }
        }

        return new SpectrumScaledTexture(tex, scale);
    }

    public override string ToString() => $"[ SpectrumScaledTexture tex: {tex} scale: {scale} ]";
}

// WindyTexture Definition
public sealed class WindyTexture(TextureMapping3D mapping) : FloatTexture
{
    // WindyTexture Public Methods
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord3D c = mapping.Map(ctx);
        Float windStrength = FBm(.1f * c.p, .1f * c.dpdx, .1f * c.dpdy, .5f, 3);
        Float waveHeight = FBm(c.p, c.dpdx, c.dpdy, .5f, 6);
        return FMath.Abs(windStrength) * waveHeight;
    }

    public static WindyTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                      FileLoc? loc)
    {
        // Initialize 3D texture mapping _map_ from _tp_
        TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
        return new WindyTexture(map);
    }

    public override string ToString() => $"[ WindyTexture mapping: {mapping} ]";
}

// WrinkledTexture Definition
public sealed class WrinkledTexture(TextureMapping3D mapping, int octaves, Float omega) : FloatTexture
{
    // WrinkledTexture Public Methods
    public override Float Evaluate(in TextureEvalContext ctx)
    {
        TexCoord3D c = mapping.Map(ctx);
        return Turbulence(c.p, c.dpdx, c.dpdy, omega, octaves);
    }

    public static WrinkledTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                         FileLoc? loc)
    {
        // Initialize 3D texture mapping _map_ from _tp_
        TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc);
        return new WrinkledTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f));
    }

    public override string ToString() =>
        $"[ WrinkledTexture mapping: {mapping} octaves: {octaves} omega: {FloatToString(omega)} ]";
}

public abstract partial class FloatTexture
{
    private static readonly StatCounter nTextures = new("Scene/Textures");

    internal static void CountTexture() => nTextures.Inc();

    public static FloatTexture Create(string name, Transform renderFromTexture, TextureParameterDictionary parameters,
                                      FileLoc? loc)
    {
        FloatTexture? tex = null;
        if (name == "constant")
            tex = FloatConstantTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "scale")
            tex = FloatScaledTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "mix")
            tex = FloatMixTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "directionmix")
            tex = FloatDirectionMixTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "bilerp")
            tex = FloatBilerpTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "imagemap")
            tex = FloatImageTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "checkerboard")
            tex = FloatCheckerboardTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "dots")
            tex = FloatDotsTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "fbm")
            tex = FBmTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "wrinkled")
            tex = WrinkledTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "windy")
            tex = WindyTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "ptex")
            tex = FloatPtexTexture.Create(renderFromTexture, parameters, loc);
        else
            ErrorExit(loc, $"{name}: float texture type unknown.");

        if (tex == null)
            ErrorExit(loc, $"{name}: unable to create texture.");

        nTextures.Inc();

        parameters.ReportUnused();
        return tex!;
    }
}

public abstract partial class SpectrumTexture
{
    public static SpectrumTexture Create(string name, Transform renderFromTexture,
                                         TextureParameterDictionary parameters, SpectrumType spectrumType,
                                         FileLoc? loc)
    {
        SpectrumTexture? tex = null;
        if (name == "constant")
            tex = SpectrumConstantTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "scale")
            tex = SpectrumScaledTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "mix")
            tex = SpectrumMixTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "directionmix")
            tex = SpectrumDirectionMixTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "bilerp")
            tex = SpectrumBilerpTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "imagemap")
            tex = SpectrumImageTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "checkerboard")
            tex = SpectrumCheckerboardTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "dots")
            tex = SpectrumDotsTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else if (name == "marble")
            tex = MarbleTexture.Create(renderFromTexture, parameters, loc);
        else if (name == "ptex")
            tex = SpectrumPtexTexture.Create(renderFromTexture, parameters, spectrumType, loc);
        else
            ErrorExit(loc, $"{name}: spectrum texture type unknown.");

        if (tex == null)
            ErrorExit(loc, $"{name}: unable to create texture.");

        FloatTexture.CountTexture();

        parameters.ReportUnused();

        return tex!;
    }
}

// TextureEvaluator Definition
//
// pbrt's materials are templates over the texture evaluator; here the
// evaluators are structs implementing ITextureEvaluator and the materials'
// methods are generic over them.
public interface ITextureEvaluator
{
    bool CanEvaluate(ReadOnlySpan<FloatTexture?> ftex, ReadOnlySpan<SpectrumTexture?> stex);
    Float Evaluate(FloatTexture tex, in TextureEvalContext ctx);
    SampledSpectrum Evaluate(SpectrumTexture tex, in TextureEvalContext ctx, in SampledWavelengths lambda);
}

// UniversalTextureEvaluator Definition
public struct UniversalTextureEvaluator : ITextureEvaluator
{
    // UniversalTextureEvaluator Public Methods
    public readonly bool CanEvaluate(ReadOnlySpan<FloatTexture?> ftex, ReadOnlySpan<SpectrumTexture?> stex) => true;

    public readonly Float Evaluate(FloatTexture tex, in TextureEvalContext ctx) => tex.Evaluate(ctx);

    public readonly SampledSpectrum Evaluate(SpectrumTexture tex, in TextureEvalContext ctx,
                                             in SampledWavelengths lambda) =>
        tex.Evaluate(ctx, lambda);
}

// BasicTextureEvaluator Definition
public struct BasicTextureEvaluator : ITextureEvaluator
{
    // BasicTextureEvaluator Public Methods
    public readonly bool CanEvaluate(ReadOnlySpan<FloatTexture?> ftex, ReadOnlySpan<SpectrumTexture?> stex)
    {
        // Return _false_ if any _FloatTexture_s cannot be evaluated
        foreach (FloatTexture? f in ftex)
            if (f != null && f is not FloatConstantTexture && f is not FloatImageTexture)
                return false;

        // Return _false_ if any _SpectrumTexture_s cannot be evaluated
        foreach (SpectrumTexture? s in stex)
            if (s != null && s is not SpectrumConstantTexture && s is not SpectrumImageTexture)
                return false;

        return true;
    }

    public readonly Float Evaluate(FloatTexture tex, in TextureEvalContext ctx)
    {
        if (tex is FloatConstantTexture c)
            return c.Evaluate(ctx);
        else if (tex is FloatImageTexture i)
            return i.Evaluate(ctx);
        else
        {
            if (tex != null)
                LOG_FATAL($"BasicTextureEvaluator::operator() called with {tex}");
            return 0f;
        }
    }

    public readonly SampledSpectrum Evaluate(SpectrumTexture tex, in TextureEvalContext ctx,
                                             in SampledWavelengths lambda)
    {
        if (tex is SpectrumConstantTexture c)
            return c.Evaluate(ctx, lambda);
        else if (tex is SpectrumImageTexture i)
            return i.Evaluate(ctx, lambda);
        else
        {
            if (tex != null)
                LOG_FATAL($"BasicTextureEvaluator::operator() called with {tex}");
            return new SampledSpectrum(0f);
        }
    }
}

public static partial class Util
{
    // CheckerboardTexture Function Definitions
    public static Float Checkerboard(in TextureEvalContext ctx, TextureMapping2D? map2D, TextureMapping3D? map3D)
    {
        // Define 1D checkerboard filtered integral functions
        static Float d(Float x)
        {
            Float y = x / 2 - FMath.Floor(x / 2) - 0.5f;
            return x / 2 + y * (1 - 2 * FMath.Abs(y));
        }

        static Float bf(Float x, Float r)
        {
            if (FMath.Floor(x - r) == FMath.Floor(x + r))
                return 1 - 2 * ((int)FMath.Floor(x) & 1);
            return (d(x + r) - 2 * d(x) + d(x - r)) / Sqr(r);
        }

        if (map2D != null)
        {
            // Return weights for 2D checkerboard texture
            CHECK(map3D == null);
            TexCoord2D c = map2D.Map(ctx);
            Float ds = FMath.Max(FMath.Abs(c.dsdx), FMath.Abs(c.dsdy));
            Float dt = FMath.Max(FMath.Abs(c.dtdx), FMath.Abs(c.dtdy));
            // Integrate product of 2D checkerboard function and triangle filter
            ds *= 1.5f;
            dt *= 1.5f;
            return 0.5f - bf(c.st[0], ds) * bf(c.st[1], dt) / 2;
        }
        else
        {
            // Return weights for 3D checkerboard texture
            CHECK(map3D != null);
            TexCoord3D c = map3D!.Map(ctx);
            Float dx = 1.5f * FMath.Max(FMath.Abs(c.dpdx.x), FMath.Abs(c.dpdy.x));
            Float dy = 1.5f * FMath.Max(FMath.Abs(c.dpdx.y), FMath.Abs(c.dpdy.y));
            Float dz = 1.5f * FMath.Max(FMath.Abs(c.dpdx.z), FMath.Abs(c.dpdy.z));
            return 0.5f - 0.5f * bf(c.p.x, dx) * bf(c.p.y, dy) * bf(c.p.z, dz);
        }
    }

    // InsidePolkaDot Function Definition
    public static bool InsidePolkaDot(Point2f st)
    {
        // Compute cell indices (_sCell_,_tCell_ for dots
        int sCell = (int)FMath.Floor(st[0] + .5f), tCell = (int)FMath.Floor(st[1] + .5f);

        if (Noise(sCell + .5f, tCell + .5f) > 0)
        {
            // Determine dot position and test if _st_ is inside it
            Float radius = .35f;
            Float maxShift = 0.5f - radius;
            Float sCenter = sCell + maxShift * Noise(sCell + 1.5f, tCell + 2.8f);
            Float tCenter = tCell + maxShift * Noise(sCell + 4.5f, tCell + 9.8f);
            Vector2f dst = st - new Point2f(sCenter, tCenter);
            if (LengthSquared(dst) < Sqr(radius))
                return true;
        }
        return false;
    }
}

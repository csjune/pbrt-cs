// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/paramdict.h and paramdict.cpp (and the ParsedParameter
// methods from parser.cpp)
//
// ParsedParameterVector is List<ParsedParameter>. ParameterDictionary is a
// class: pbrt copies it by value, but the copies share their
// ParsedParameters, and the copies that are later modified (by the
// --upgrade code) are separate dictionaries here too. pbrt's ParameterTypeTraits
// become the type name, values per item and conversion passed to
// LookupSingle()/LookupArray().
//
// TextureParameterDictionary converts implicitly to ParameterDictionary
// like pbrt's operator const ParameterDictionary &().

using System.Text;

namespace Pbrt;

// ParsedParameter Definition
public sealed class ParsedParameter(FileLoc loc)
{
    // ParsedParameter Public Members
    public string type = "", name = "";
    public FileLoc loc = loc;
    public List<Float> floats = [];
    public List<int> ints = [];
    public List<string> strings = [];
    public List<bool> bools = [];
    public bool lookedUp = false;
    public RGBColorSpace? colorSpace = null;
    public bool mayBeUnused = false;

    // ParsedParameter Public Methods
    public void AddFloat(Float v)
    {
        CHECK(ints.Count == 0 && strings.Count == 0 && bools.Count == 0);
        floats.Add(v);
    }

    public void AddInt(int i)
    {
        CHECK(floats.Count == 0 && strings.Count == 0 && bools.Count == 0);
        ints.Add(i);
    }

    public void AddString(string str)
    {
        CHECK(floats.Count == 0 && ints.Count == 0 && bools.Count == 0);
        strings.Add(str);
    }

    public void AddBool(bool v)
    {
        CHECK(floats.Count == 0 && ints.Count == 0 && strings.Count == 0);
        bools.Add(v);
    }

    public override string ToString()
    {
        StringBuilder str = new();
        str.Append('"').Append(type).Append(' ').Append(name).Append("\" [ ");
        if (floats.Count > 0)
            foreach (Float d in floats)
                str.Append(FloatToString(d)).Append(' ');
        else if (ints.Count > 0)
            foreach (int i in ints)
                str.Append(i).Append(' ');
        else if (strings.Count > 0)
            foreach (string s in strings)
                str.Append('"').Append(s).Append("\" ");
        else if (bools.Count > 0)
            foreach (bool b in bools)
                str.Append(b ? "true " : "false ");
        str.Append("] ");

        return str.ToString();
    }
}

// ParameterType Definition
public enum ParameterType
{
    Boolean,
    Float,
    Integer,
    Point2f,
    Vector2f,
    Point3f,
    Vector3f,
    Normal3f,
    Spectrum,
    String,
    Texture
}

// SpectrumType Definition
public enum SpectrumType { Illuminant, Albedo, Unbounded }

// ParameterDictionary Definition
public sealed class ParameterDictionary
{
    // ParameterDictionary Private Members
    private List<ParsedParameter> parameters = [];
    private readonly RGBColorSpace? colorSpace = null;

    // ParameterDictionary Public Methods
    public ParameterDictionary() { }

    public ParameterDictionary(IEnumerable<ParsedParameter> p, RGBColorSpace colorSpace)
    {
        parameters = [.. p];
        this.colorSpace = colorSpace;
        parameters.Reverse();
        CHECK(colorSpace != null);
        checkParameterTypes();
    }

    public ParameterDictionary(IEnumerable<ParsedParameter> p0, IReadOnlyList<ParsedParameter> params1,
                               RGBColorSpace colorSpace)
    {
        parameters = [.. p0];
        this.colorSpace = colorSpace;
        parameters.Reverse();
        CHECK(colorSpace != null);
        for (int i = params1.Count - 1; i >= 0; --i)
            parameters.Add(params1[i]);
        checkParameterTypes();
    }

    // A copy that shares the ParsedParameters, like copying pbrt's
    // ParameterDictionary.
    public ParameterDictionary Clone() => new(this);

    private ParameterDictionary(ParameterDictionary d)
    {
        parameters = [.. d.parameters];
        colorSpace = d.colorSpace;
    }

    public RGBColorSpace? ColorSpace() => colorSpace;

    public IReadOnlyList<ParsedParameter> GetParameterVector() => parameters;

    public void FreeParameters() => parameters.Clear();

    private const string BoolTypeName = "bool", FloatTypeName = "float", IntegerTypeName = "integer",
                         Point2fTypeName = "point2", Vector2fTypeName = "vector2", Point3fTypeName = "point3",
                         Vector3fTypeName = "vector3", Normal3fTypeName = "normal", StringTypeName = "string";

    private void checkParameterTypes()
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.type == BoolTypeName)
            {
                if (p.bools.Count == 0)
                    ErrorExit(p.loc, $"\"{p.name}\": non-Boolean values provided for Boolean-valued parameter");
            }
            else if (p.type == FloatTypeName || p.type == IntegerTypeName || p.type == Point2fTypeName ||
                     p.type == Vector2fTypeName || p.type == Point3fTypeName || p.type == Vector3fTypeName ||
                     p.type == Normal3fTypeName || p.type == "rgb" || p.type == "blackbody")
            {
                if (p.ints.Count == 0 && p.floats.Count == 0)
                    ErrorExit(p.loc, $"\"{p.name}\": non-numeric values provided for numeric-valued parameter");
            }
            else if (p.type == StringTypeName || p.type == "texture")
            {
                if (p.strings.Count == 0)
                    ErrorExit(p.loc, $"\"{p.name}\": non-string values provided for string-valued parameter");
            }
            else if (p.type == "spectrum")
            {
                if (p.strings.Count == 0 && p.ints.Count == 0 && p.floats.Count == 0)
                    ErrorExit(p.loc, $"\"{p.name}\": expecting string or numeric-valued parameter for " +
                                     "spectrum parameter");
            }
            else
                ErrorExit(p.loc, $"\"{p.type}\": unknown parameter type");
        }
    }

    private delegate R Convert<V, R>(List<V> values, int offset, FileLoc loc);

    private R lookupSingle<V, R>(string name, R defaultValue, string typeName, int nPerItem,
                                 Func<ParsedParameter, List<V>> getValues, Convert<V, R> convert)
    {
        // Search _params_ for parameter _name_
        foreach (ParsedParameter p in parameters)
        {
            if (p.name != name || p.type != typeName)
                continue;
            // Extract parameter values from _p_
            List<V> values = getValues(p);

            // Issue error if an incorrect number of parameter values were provided
            if (values.Count == 0)
                ErrorExit(p.loc, $"No values provided for parameter \"{name}\".");
            if (values.Count != nPerItem)
                ErrorExit(p.loc, $"Expected {nPerItem} values for parameter \"{name}\".");

            // Return parameter values as _ReturnType_
            p.lookedUp = true;
            return convert(values, 0, p.loc);
        }

        return defaultValue;
    }

    private static List<R> returnArray<V, R>(List<V> values, ParsedParameter param, int nPerItem,
                                             Convert<V, R> convert)
    {
        if (values.Count == 0)
            ErrorExit(param.loc, $"No values provided for \"{param.name}\".");
        if (values.Count % nPerItem != 0)
            ErrorExit(param.loc, $"Number of values provided for \"{param.name}\" not a multiple of {nPerItem}");

        param.lookedUp = true;
        int n = values.Count / nPerItem;
        List<R> v = new(n);
        for (int i = 0; i < n; ++i)
            v.Add(convert(values, nPerItem * i, param.loc));
        return v;
    }

    private List<R> lookupArray<V, R>(string name, string typeName, int nPerItem,
                                      Func<ParsedParameter, List<V>> getValues, Convert<V, R> convert)
    {
        foreach (ParsedParameter p in parameters)
            if (p.name == name && p.type == typeName)
                return returnArray(getValues(p), p, nPerItem, convert);

        return [];
    }

    // ParameterTypeTraits
    private static List<bool> Bools(ParsedParameter p) => p.bools;
    private static List<Float> Floats(ParsedParameter p) => p.floats;
    private static List<int> Ints(ParsedParameter p) => p.ints;
    private static List<string> Strings(ParsedParameter p) => p.strings;
    private static bool ToBool(List<bool> v, int i, FileLoc loc) => v[i];
    private static Float ToFloat(List<Float> v, int i, FileLoc loc) => v[i];
    private static int ToInt(List<int> v, int i, FileLoc loc) => v[i];
    private static string ToStr(List<string> v, int i, FileLoc loc) => v[i];
    private static Point2f ToPoint2f(List<Float> v, int i, FileLoc loc) => new(v[i], v[i + 1]);
    private static Vector2f ToVector2f(List<Float> v, int i, FileLoc loc) => new(v[i], v[i + 1]);
    private static Point3f ToPoint3f(List<Float> v, int i, FileLoc loc) => new(v[i], v[i + 1], v[i + 2]);
    private static Vector3f ToVector3f(List<Float> v, int i, FileLoc loc) => new(v[i], v[i + 1], v[i + 2]);
    private static Normal3f ToNormal3f(List<Float> v, int i, FileLoc loc) => new(v[i], v[i + 1], v[i + 2]);

    // ParameterDictionary Method Definitions
    public Float GetOneFloat(string name, Float def) =>
        lookupSingle<Float, Float>(name, def, FloatTypeName, 1, Floats, ToFloat);

    public int GetOneInt(string name, int def) =>
        lookupSingle<int, int>(name, def, IntegerTypeName, 1, Ints, ToInt);

    public bool GetOneBool(string name, bool def) =>
        lookupSingle<bool, bool>(name, def, BoolTypeName, 1, Bools, ToBool);

    public string GetOneString(string name, string def) =>
        lookupSingle<string, string>(name, def, StringTypeName, 1, Strings, ToStr);

    public Point2f GetOnePoint2f(string name, Point2f def) =>
        lookupSingle<Float, Point2f>(name, def, Point2fTypeName, 2, Floats, ToPoint2f);

    public Vector2f GetOneVector2f(string name, Vector2f def) =>
        lookupSingle<Float, Vector2f>(name, def, Vector2fTypeName, 2, Floats, ToVector2f);

    public Point3f GetOnePoint3f(string name, Point3f def) =>
        lookupSingle<Float, Point3f>(name, def, Point3fTypeName, 3, Floats, ToPoint3f);

    public Vector3f GetOneVector3f(string name, Vector3f def) =>
        lookupSingle<Float, Vector3f>(name, def, Vector3fTypeName, 3, Floats, ToVector3f);

    public Normal3f GetOneNormal3f(string name, Normal3f def) =>
        lookupSingle<Float, Normal3f>(name, def, Normal3fTypeName, 3, Floats, ToNormal3f);

    public Spectrum? GetOneSpectrum(string name, Spectrum? defaultValue, SpectrumType spectrumType)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.name != name)
                continue;

            List<Spectrum> s = extractSpectrumArray(p, spectrumType);
            if (s.Count > 0)
            {
                if (s.Count > 1)
                    ErrorExit(p.loc, $"More than one value provided for parameter \"{name}\".");
                return s[0];
            }
        }

        return defaultValue;
    }

    public List<Float> GetFloatArray(string name) => lookupArray<Float, Float>(name, FloatTypeName, 1, Floats, ToFloat);
    public List<int> GetIntArray(string name) => lookupArray<int, int>(name, IntegerTypeName, 1, Ints, ToInt);
    public List<bool> GetBoolArray(string name) => lookupArray<bool, bool>(name, BoolTypeName, 1, Bools, ToBool);

    public List<Point2f> GetPoint2fArray(string name) =>
        lookupArray<Float, Point2f>(name, Point2fTypeName, 2, Floats, ToPoint2f);

    public List<Vector2f> GetVector2fArray(string name) =>
        lookupArray<Float, Vector2f>(name, Vector2fTypeName, 2, Floats, ToVector2f);

    public List<Point3f> GetPoint3fArray(string name) =>
        lookupArray<Float, Point3f>(name, Point3fTypeName, 3, Floats, ToPoint3f);

    public List<Vector3f> GetVector3fArray(string name) =>
        lookupArray<Float, Vector3f>(name, Vector3fTypeName, 3, Floats, ToVector3f);

    public List<Normal3f> GetNormal3fArray(string name) =>
        lookupArray<Float, Normal3f>(name, Normal3fTypeName, 3, Floats, ToNormal3f);

    public List<string> GetStringArray(string name) =>
        lookupArray<string, string>(name, StringTypeName, 1, Strings, ToStr);

    private static readonly Dictionary<string, Spectrum> cachedSpectra = [];

    // TODO: move this functionality (but not the caching?) to a Spectrum method.
    private static Spectrum? readSpectrumFromFile(string filename)
    {
        string fn = ResolveFilename(filename);
        lock (cachedSpectra)
        {
            if (cachedSpectra.TryGetValue(fn, out Spectrum? s))
                return s;

            Spectrum? pls = PiecewiseLinearSpectrum.Read(fn);
            if (pls == null)
                return null;

            cachedSpectra[fn] = pls;
            return pls;
        }
    }

    private List<Spectrum> extractSpectrumArray(ParsedParameter param, SpectrumType spectrumType)
    {
        if (param.type == "rgb" || (Options.upgrade && param.type == "color"))
            return returnArray(param.floats, param, 3, (List<Float> v, int i, FileLoc loc) =>
            {
                RGB rgb = new(v[i], v[i + 1], v[i + 2]);
                RGBColorSpace cs = param.colorSpace ?? colorSpace!;
                if (rgb.r < 0 || rgb.g < 0 || rgb.b < 0)
                    ErrorExit(loc, $"RGB parameter \"{param.name}\" has negative component.");
                if (spectrumType == SpectrumType.Albedo)
                {
                    if (rgb.r > 1 || rgb.g > 1 || rgb.b > 1)
                        ErrorExit(loc, $"RGB parameter \"{param.name}\" has > 1 component.");
                    return (Spectrum)new RGBAlbedoSpectrum(cs, rgb);
                }
                else if (spectrumType == SpectrumType.Unbounded)
                    return new RGBUnboundedSpectrum(cs, rgb);
                else
                {
                    CHECK(spectrumType == SpectrumType.Illuminant);
                    return new RGBIlluminantSpectrum(cs, rgb);
                }
            });
        else if (param.type == "blackbody")
            return returnArray(param.floats, param, 1,
                               (List<Float> v, int i, FileLoc loc) => (Spectrum)new BlackbodySpectrum(v[i]));
        else if (param.type == "spectrum" && param.floats.Count > 0)
        {
            if (param.floats.Count % 2 != 0)
                ErrorExit(param.loc, $"Found odd number of values for \"{param.name}\"");

            int nSamples = param.floats.Count / 2;
            if (nSamples == 1)
                Warning(param.loc, "Specified spectrum is only non-zero at a single wavelength. " +
                                   "This is probably unintended.");
            return returnArray(param.floats, param, param.floats.Count, (List<Float> v, int offset, FileLoc loc) =>
            {
                Float[] lambda = new Float[nSamples], value = new Float[nSamples];
                for (int i = 0; i < nSamples; ++i)
                {
                    if (i > 0 && v[offset + 2 * i] <= lambda[i - 1])
                        ErrorExit(param.loc, $"Spectrum description invalid: at {i - 1}'th entry, " +
                                             $"wavelengths aren't increasing: {FloatToString(lambda[i - 1])} >= " +
                                             $"{FloatToString(v[offset + 2 * i])}.");
                    lambda[i] = v[offset + 2 * i];
                    value[i] = v[offset + 2 * i + 1];
                }
                return (Spectrum)new PiecewiseLinearSpectrum(lambda, value);
            });
        }
        else if (param.type == "spectrum" && param.strings.Count > 0)
            return returnArray(param.strings, param, 1, (List<string> v, int i, FileLoc loc) =>
            {
                Spectrum? spd = GetNamedSpectrum(v[i]);
                if (spd != null)
                    return spd;

                spd = readSpectrumFromFile(v[i]);
                if (spd == null)
                    ErrorExit(param.loc, $"{v[i]}: unable to read valid spectrum file");
                return spd!;
            });

        return [];
    }

    public List<Spectrum> GetSpectrumArray(string name, SpectrumType spectrumType)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.name != name)
                continue;

            List<Spectrum> s = extractSpectrumArray(p, spectrumType);
            if (s.Count > 0)
                return s;
        }
        return [];
    }

    public string GetTexture(string name)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.name != name || p.type != "texture")
                continue;

            if (p.strings.Count == 0)
                ErrorExit(p.loc, $"No string values provided for parameter \"{name}\".");
            if (p.strings.Count > 1)
                ErrorExit(p.loc, $"More than one value provided for parameter \"{name}\".");
            p.lookedUp = true;
            return p.strings[0];
        }

        return "";
    }

    public List<RGB> GetRGBArray(string name)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.name == name && p.type == "rgb")
            {
                // (pbrt's message has a stray %d and no name.)
                if (p.floats.Count % 3 != 0)
                    ErrorExit(p.loc, "Number of values given for \"rgb\" parameter %d " +
                                     "\"name\" isn't a multiple of 3.");

                List<RGB> rgb = new(p.floats.Count / 3);
                for (int i = 0; i < p.floats.Count / 3; ++i)
                    rgb.Add(new RGB(p.floats[3 * i], p.floats[3 * i + 1], p.floats[3 * i + 2]));

                p.lookedUp = true;
                return rgb;
            }
        }
        return [];
    }

    // For --upgrade only
    public RGB? GetOneRGB(string name)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.name == name && p.type == "rgb")
            {
                if (p.floats.Count < 3)
                    ErrorExit(p.loc, $"Insufficient values for \"rgb\" parameter \"{p.name}\".");
                return new RGB(p.floats[0], p.floats[1], p.floats[2]);
            }
        }
        return null;
    }

    // Unfortunately, this is most easily done here...
    public Float UpgradeBlackbody(string name)
    {
        Float scale = 1;
        foreach (ParsedParameter p in parameters)
        {
            if (p.name == name && p.type == "blackbody")
            {
                if (p.floats.Count != 2)
                    ErrorExit(p.loc, "Expected two values for legacy \"blackbody\" parameter.");
                scale *= p.floats[1];
                p.floats.RemoveAt(p.floats.Count - 1);
            }
        }
        return scale;
    }

    private void remove(string name, string typeName)
    {
        for (int i = 0; i < parameters.Count; ++i)
            if (parameters[i].name == name && parameters[i].type == typeName)
            {
                parameters.RemoveAt(i);
                return;
            }
    }

    public void RemoveFloat(string name) => remove(name, FloatTypeName);
    public void RemoveInt(string name) => remove(name, IntegerTypeName);
    public void RemoveBool(string name) => remove(name, BoolTypeName);
    public void RemovePoint2f(string name) => remove(name, Point2fTypeName);
    public void RemoveVector2f(string name) => remove(name, Vector2fTypeName);
    public void RemovePoint3f(string name) => remove(name, Point3fTypeName);
    public void RemoveVector3f(string name) => remove(name, Vector3fTypeName);
    public void RemoveNormal3f(string name) => remove(name, Normal3fTypeName);
    public void RemoveString(string name) => remove(name, StringTypeName);
    public void RemoveTexture(string name) => remove(name, "texture");

    public void RemoveSpectrum(string name)
    {
        remove(name, "spectrum");
        remove(name, "rgb");
        remove(name, "blackbody");
    }

    public void RenameParameter(string before, string after)
    {
        foreach (ParsedParameter p in parameters)
            if (p.name == before)
                p.name = after;
    }

    public void RenameUsedTextures(IReadOnlyDictionary<string, string> m)
    {
        foreach (ParsedParameter p in parameters)
        {
            if (p.type != "texture")
                continue;

            CHECK_EQ(1, p.strings.Count);
            if (m.TryGetValue(p.strings[0], out string? renamed))
                p.strings[0] = renamed;
        }
    }

    public void ReportUnused()
    {
        // type / name
        List<(string type, string name)> seen = [];

        foreach (ParsedParameter p in parameters)
        {
            if (p.mayBeUnused)
                continue;

            bool haveSeen = seen.Exists(p2 => p2.type == p.type && p2.name == p.name);
            if (p.lookedUp)
            {
                // A parameter may be used when creating an initial Material, say,
                // but then an override from a Shape may shadow it such that its
                // name is already in the seen array.
                if (!haveSeen)
                    seen.Add((p.type, p.name));
            }
            else if (haveSeen)
            {
                // It's shadowed by another parameter; that's fine.
            }
            else
                ErrorExit(p.loc, $"\"{p.name}\": unused parameter.");
        }
    }

    private static string ToParameterDefinition(ParsedParameter p, int indentCount)
    {
        StringBuilder s = new($"\"{p.type} {p.name}\" [ ");
        int continuationIndent = indentCount + 10 + p.type.Length + p.name.Length;
        int column = indentCount + 4 + s.Length;
        void printOne(string val)
        {
            if (column > 80)
            { //   && i % 3 == 0) {
                s.Append('\n');
                s.Append(' ', continuationIndent);
                column = continuationIndent;
            }
            column += val.Length;
            s.Append(val);
        }

        foreach (Float v in p.floats)
            printOne(FloatToString(v) + " ");
        foreach (int i in p.ints)
            printOne($"{i} ");
        foreach (string str in p.strings)
            printOne('"' + str + "\" ");
        foreach (bool b in p.bools)
            printOne(b ? "true " : "false ");
        s.Append(']');

        return s.ToString();
    }

    public string ToParameterList(int indentCount = 0)
    {
        StringBuilder s = new();
        foreach (ParsedParameter p in parameters)
        {
            s.Append(' ', indentCount + 4);
            s.Append(ToParameterDefinition(p, indentCount));
            s.Append('\n');
        }

        return s.ToString();
    }

    public string ToParameterDefinition(string name)
    {
        foreach (ParsedParameter p in parameters)
            if (p.name == name)
                return ToParameterDefinition(p, 0);
        return "";
    }

    public override string ToString()
    {
        StringBuilder s = new("[ ParameterDictionary params: ");
        foreach (ParsedParameter p in parameters)
            s.Append("[ " + p.ToString() + "] ");
        s.Append($"colorSpace: {(colorSpace != null ? colorSpace.ToString() : "<nullptr>")} ");
        s.Append(']');
        return s.ToString();
    }

    public FileLoc? loc(string name)
    {
        foreach (ParsedParameter p in parameters)
            if (p.name == name)
                return p.loc;
        return null;
    }
}

// NamedTextures Definition
public sealed class NamedTextures
{
    public Dictionary<string, FloatTexture> floatTextures = [];
    public Dictionary<string, SpectrumTexture> albedoSpectrumTextures = [];
    public Dictionary<string, SpectrumTexture> unboundedSpectrumTextures = [];
    public Dictionary<string, SpectrumTexture> illuminantSpectrumTextures = [];
}

// TextureParameterDictionary Definition
public sealed class TextureParameterDictionary(ParameterDictionary dict, NamedTextures textures)
{
    // TextureParameterDictionary Private Members
    private readonly ParameterDictionary dict = dict;
    private readonly NamedTextures textures = textures;

    // TextureParameterDictionary Public Methods
    public static implicit operator ParameterDictionary(TextureParameterDictionary d) => d.dict;

    public Float GetOneFloat(string name, Float def) => dict.GetOneFloat(name, def);
    public int GetOneInt(string name, int def) => dict.GetOneInt(name, def);
    public bool GetOneBool(string name, bool def) => dict.GetOneBool(name, def);
    public Point2f GetOnePoint2f(string name, Point2f def) => dict.GetOnePoint2f(name, def);
    public Vector2f GetOneVector2f(string name, Vector2f def) => dict.GetOneVector2f(name, def);
    public Point3f GetOnePoint3f(string name, Point3f def) => dict.GetOnePoint3f(name, def);
    public Vector3f GetOneVector3f(string name, Vector3f def) => dict.GetOneVector3f(name, def);
    public Normal3f GetOneNormal3f(string name, Normal3f def) => dict.GetOneNormal3f(name, def);
    public Spectrum? GetOneSpectrum(string name, Spectrum? def, SpectrumType spectrumType) =>
        dict.GetOneSpectrum(name, def, spectrumType);
    public string GetOneString(string name, string def) => dict.GetOneString(name, def);

    public List<Float> GetFloatArray(string name) => dict.GetFloatArray(name);
    public List<int> GetIntArray(string name) => dict.GetIntArray(name);
    public List<bool> GetBoolArray(string name) => dict.GetBoolArray(name);
    public List<Point2f> GetPoint2fArray(string name) => dict.GetPoint2fArray(name);
    public List<Vector2f> GetVector2fArray(string name) => dict.GetVector2fArray(name);
    public List<Point3f> GetPoint3fArray(string name) => dict.GetPoint3fArray(name);
    public List<Vector3f> GetVector3fArray(string name) => dict.GetVector3fArray(name);
    public List<Normal3f> GetNormal3fArray(string name) => dict.GetNormal3fArray(name);
    public List<Spectrum> GetSpectrumArray(string name, SpectrumType spectrumType) =>
        dict.GetSpectrumArray(name, spectrumType);
    public List<string> GetStringArray(string name) => dict.GetStringArray(name);

    public FloatTexture GetFloatTexture(string name, Float defaultValue) =>
        GetFloatTextureOrNull(name) ?? new FloatConstantTexture(defaultValue);

    public FloatTexture? GetFloatTextureOrNull(string name)
    {
        foreach (ParsedParameter p in dict.GetParameterVector())
        {
            if (p.name != name)
                continue;

            if (p.type == "texture")
            {
                if (p.strings.Count == 0)
                    ErrorExit(p.loc, $"No texture name provided for parameter \"{name}\".");
                if (p.strings.Count != 1)
                    ErrorExit(p.loc, $"More than one texture name provided for parameter \"{name}\".");

                p.lookedUp = true;
                if (textures.floatTextures.TryGetValue(p.strings[0], out FloatTexture? tex))
                    return tex;

                ErrorExit(p.loc, $"Couldn't find float texture named \"{p.strings[0]}\" for parameter \"{p.name}\"");
            }
            else if (p.type == "float")
            {
                Float v = GetOneFloat(name, 0f); // we know this will be found
                return new FloatConstantTexture(v);
            }
        }

        return null;
    }

    public void ReportUnused() => dict.ReportUnused();

    public SpectrumTexture? GetSpectrumTexture(string name, Spectrum? defaultValue, SpectrumType spectrumType)
    {
        SpectrumTexture? tex = GetSpectrumTextureOrNull(name, spectrumType);
        if (tex != null)
            return tex;
        else if (defaultValue != null)
            return new SpectrumConstantTexture(defaultValue);
        else
            return null;
    }

    public SpectrumTexture? GetSpectrumTextureOrNull(string name, SpectrumType spectrumType)
    {
        Dictionary<string, SpectrumTexture> spectrumTextures =
            (spectrumType == SpectrumType.Unbounded) ? textures.unboundedSpectrumTextures
            : ((spectrumType == SpectrumType.Albedo) ? textures.albedoSpectrumTextures
               : textures.illuminantSpectrumTextures);

        foreach (ParsedParameter p in dict.GetParameterVector())
        {
            if (p.name != name)
                continue;

            if (p.type == "texture")
            {
                if (p.strings.Count == 0)
                    ErrorExit(p.loc, $"No texture name provided for parameter \"{name}\".");
                if (p.strings.Count != 1)
                    ErrorExit(p.loc, $"More than one texture name provided for parameter \"{name}\".");

                p.lookedUp = true;
                if (spectrumTextures.TryGetValue(p.strings[0], out SpectrumTexture? tex))
                    return tex;

                ErrorExit(p.loc,
                          $"Couldn't find spectrum texture named \"{p.strings[0]}\" for parameter \"{p.name}\"");
            }
            else if (p.type == "rgb")
            {
                if (p.floats.Count != 3)
                    ErrorExit(p.loc, $"Didn't find three values for \"rgb\" parameter \"{p.name}\".");
                p.lookedUp = true;

                RGB rgb = new(p.floats[0], p.floats[1], p.floats[2]);
                if (rgb.r < 0 || rgb.g < 0 || rgb.b < 0)
                    ErrorExit(p.loc, $"Negative value provided for RGB parameter \"{p.name}\".");
                Spectrum s;
                if (spectrumType == SpectrumType.Illuminant)
                    s = new RGBIlluminantSpectrum(dict.ColorSpace()!, rgb);
                else if (spectrumType == SpectrumType.Unbounded)
                    s = new RGBUnboundedSpectrum(dict.ColorSpace()!, rgb);
                else
                {
                    CHECK(spectrumType == SpectrumType.Albedo);
                    if (rgb.r > 1 || rgb.g > 1 || rgb.b > 1)
                        ErrorExit(p.loc, $"RGB parameter \"{p.name}\" used as an albedo has > 1 component.");
                    s = new RGBAlbedoSpectrum(dict.ColorSpace()!, rgb);
                }
                return new SpectrumConstantTexture(s);
            }
            else if (p.type == "spectrum" || p.type == "blackbody")
            {
                Spectrum? s = GetOneSpectrum(name, null, spectrumType);
                CHECK(s != null);
                return new SpectrumConstantTexture(s!);
            }
        }

        return null;
    }
}

public static partial class Util
{
    public static string ToString(SpectrumType t) => t switch
    {
        SpectrumType.Albedo => "Albedo",
        SpectrumType.Unbounded => "Unbounded",
        SpectrumType.Illuminant => "Illuminant",
        _ => throw new FatalErrorException("Unhandled SpectrumType")
    };
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// PLY reading and writing for Mesh.cs: a C# port of the parts of RPly
// (src/ext/rply/rply.cpp, by Diego Nehab, MIT license) that pbrt uses,
// including pbrt's addition of reading gzipped files (names ending in .gz).
//
// As in RPly, values are passed to read callbacks as doubles; for list
// properties the callback is first called with the list length
// (valueIndex == -1) and then once per value. Writes take values in file
// order, one ply_write() per value. strtod()'s hex floats aren't supported
// in ASCII files.

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Pbrt;

internal enum PlyStorageMode { BigEndian, LittleEndian, Ascii, Default }

// (Order matches RPly's e_ply_type and its type names.)
internal enum PlyType
{
    Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64,
    Char, UChar, Short, UShort, Int, UInt, Float, Double, List
}

internal sealed class PlyArgument
{
    public PlyElement element = null!;
    public PlyProperty property = null!;
    public long instanceIndex;
    public long length;
    public long valueIndex;
    public double value;
}

internal sealed class PlyProperty(string name)
{
    public readonly string name = name;
    public PlyType type, lengthType, valueType;
    public Func<PlyArgument, bool>? readCb;
}

internal sealed class PlyElement(string name, long nInstances)
{
    public readonly string name = name;
    public readonly long nInstances = nInstances;
    public readonly List<PlyProperty> properties = [];
}

internal sealed class PlyFile
{
    private static readonly string[] storageModeNames = ["binary_big_endian", "binary_little_endian", "ascii"];
    private static readonly string[] typeNames =
    [
        "int8", "uint8", "int16", "uint16", "int32", "uint32", "float32", "float64",
        "char", "uchar", "short", "ushort", "int", "uint", "float", "double", "list"
    ];

    private readonly Action<string> errorCb;
    private readonly List<PlyElement> elements = [];
    private readonly List<string> comments = [], objInfos = [];
    private PlyStorageMode storageMode;

    // Reading
    private readonly byte[] data = [];
    private int pos;

    // Writing
    private readonly Stream? output;
    private int wElement, wProperty, wInstanceIndex;
    private long wValueIndex, wLength;

    private PlyFile(Action<string> errorCb, byte[] data)
    {
        this.errorCb = errorCb;
        this.data = data;
    }

    private PlyFile(Action<string> errorCb, Stream output, PlyStorageMode storageMode)
    {
        this.errorCb = errorCb;
        this.output = output;
        this.storageMode = storageMode;
    }

    public IReadOnlyList<PlyElement> Elements => elements;
    public IReadOnlyList<string> Comments => comments;
    public IReadOnlyList<string> ObjInfos => objInfos;

    private static void DefaultErrorCb(string message) => Console.Error.WriteLine($"RPly: {message}");

    // ---- Reading ----

    public static PlyFile? Open(string name, Action<string>? errorCb)
    {
        errorCb ??= DefaultErrorCb;
        byte[] data;
        try
        {
            // Is it gzipped?
            if (name.Length > 3 && name[^3] == '.' && char.ToLower(name[^2]) == 'g' &&
                char.ToLower(name[^1]) == 'z')
            {
                using FileStream fs = File.OpenRead(name);
                using GZipStream gz = new(fs, CompressionMode.Decompress);
                using MemoryStream ms = new();
                gz.CopyTo(ms);
                data = ms.ToArray();
            }
            else
                data = File.ReadAllBytes(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            errorCb("Unable to open file");
            return null;
        }
        return new PlyFile(errorCb, data);
    }

    // Reads the next whitespace-separated word, or null at the end of the
    // data. Like RPly, the character that ends the word is consumed.
    private string? ReadWord()
    {
        while (pos < data.Length && IsBlank(data[pos]))
            ++pos;
        if (pos >= data.Length)
        {
            errorCb("Unexpected end of file");
            return null;
        }
        int start = pos;
        while (pos < data.Length && !IsBlank(data[pos]))
            ++pos;
        string word = Encoding.Latin1.GetString(data, start, pos - start);
        if (pos < data.Length)
            ++pos;
        return word;
    }

    private static bool IsBlank(byte c) => c == ' ' || c == '\n' || c == '\r' || c == '\t';

    // Reads the rest of the current line.
    private string? ReadLine()
    {
        int end = Array.IndexOf(data, (byte)'\n', pos);
        if (end < 0)
        {
            errorCb("Unexpected end of file");
            return null;
        }
        string line = Encoding.Latin1.GetString(data, pos, end - pos);
        pos = end + 1;
        return line;
    }

    public bool ReadHeader()
    {
        // Check if it is ply
        if (data.Length < 4 || data[0] != 'p' || data[1] != 'l' || data[2] != 'y' ||
            !char.IsWhiteSpace((char)data[3]))
        {
            errorCb(data.Length == 0 ? "Unable to read magic number from file" : "Wrong magic number. Expected 'ply'");
            return false;
        }
        // Figure out if we have to skip the extra character after the header
        // when we reach the binary part of file
        bool rn = data[3] == '\r' && data.Length > 4 && data[4] == '\n';
        pos = 3;

        string? word = ReadWord();
        if (word == null)
            return false;
        // Parse file format
        if (!ReadHeaderFormat(ref word))
        {
            errorCb("Invalid file format");
            return false;
        }
        // Parse elements, comments or obj_infos until the end of header
        while (word != "end_header")
        {
            int r = ReadHeaderComment(ref word);
            if (r == 0)
                r = ReadHeaderElement(ref word);
            if (r == 0)
                r = ReadHeaderObjInfo(ref word);
            if (r < 0)
                return false;
            if (r == 0)
            {
                errorCb($"Unexpected token '{word}'");
                return false;
            }
        }
        // Skip extra character?
        if (rn)
        {
            if (pos >= data.Length)
            {
                errorCb("Unexpected end of file");
                return false;
            }
            ++pos;
        }
        return true;
    }

    private bool ReadHeaderFormat(ref string? word)
    {
        if (word != "format")
            return false;
        if ((word = ReadWord()) == null)
            return false;
        int mode = Array.IndexOf(storageModeNames, word);
        if (mode < 0)
            return false;
        storageMode = (PlyStorageMode)mode;
        if ((word = ReadWord()) == null || word != "1.0")
            return false;
        return (word = ReadWord()) != null;
    }

    // These return 1 if the token was handled, 0 if it isn't theirs and -1
    // on errors.
    private int ReadHeaderComment(ref string? word)
    {
        if (word != "comment")
            return 0;
        string? line = ReadLine();
        if (line == null)
            return -1;
        comments.Add(line);
        return (word = ReadWord()) != null ? 1 : -1;
    }

    private int ReadHeaderObjInfo(ref string? word)
    {
        if (word != "obj_info")
            return 0;
        string? line = ReadLine();
        if (line == null)
            return -1;
        objInfos.Add(line);
        return (word = ReadWord()) != null ? 1 : -1;
    }

    private int ReadHeaderProperty(ref string? word)
    {
        // Make sure it is a property
        if (word != "property")
            return 0;
        // Get property type
        if ((word = ReadWord()) == null)
            return -1;
        int type = Array.IndexOf(typeNames, word);
        if (type < 0)
            return 0;
        int lengthType = 0, valueType = 0;
        if (type == (int)PlyType.List)
        {
            // If it's a list, we need the base types
            if ((word = ReadWord()) == null)
                return -1;
            if ((lengthType = Array.IndexOf(typeNames, word)) < 0)
                return 0;
            if ((word = ReadWord()) == null)
                return -1;
            if ((valueType = Array.IndexOf(typeNames, word)) < 0)
                return 0;
        }
        // Get property name
        if ((word = ReadWord()) == null)
            return -1;
        elements[^1].properties.Add(new PlyProperty(word)
        {
            type = (PlyType)type, lengthType = (PlyType)lengthType, valueType = (PlyType)valueType
        });
        return (word = ReadWord()) != null ? 1 : -1;
    }

    private int ReadHeaderElement(ref string? word)
    {
        if (word != "element")
            return 0;
        // Get element name
        string? name = ReadWord();
        if (name == null)
            return -1;
        // Get number of elements of this type (sscanf("%ld"))
        if ((word = ReadWord()) == null)
            return -1;
        int n = 0;
        if (n < word.Length && (word[n] == '-' || word[n] == '+'))
            ++n;
        while (n < word.Length && char.IsAsciiDigit(word[n]))
            ++n;
        if (!long.TryParse(word.AsSpan(0, n), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                           out long nInstances))
        {
            errorCb($"Expected number got '{word}'");
            return -1;
        }
        elements.Add(new PlyElement(name, nInstances));
        // Get all properties for this element
        if ((word = ReadWord()) == null)
            return -1;
        while (true)
        {
            int r = ReadHeaderProperty(ref word);
            if (r == 0)
                r = ReadHeaderComment(ref word);
            if (r == 0)
                r = ReadHeaderObjInfo(ref word);
            if (r < 0)
                return -1;
            if (r == 0)
                break;
        }
        return 1;
    }

    // Returns the element's number of instances, or 0 if the element or
    // property isn't there.
    public long SetReadCb(string elementName, string propertyName, Func<PlyArgument, bool> readCb)
    {
        PlyElement? element = elements.Find(e => e.name == elementName);
        PlyProperty? property = element?.properties.Find(p => p.name == propertyName);
        if (property == null)
            return 0;
        property.readCb = readCb;
        return element!.nInstances;
    }

    public bool Read()
    {
        PlyArgument argument = new();
        // For each element type
        foreach (PlyElement element in elements)
        {
            argument.element = element;
            // For each element of this type
            for (long j = 0; j < element.nInstances; j++)
            {
                argument.instanceIndex = j;
                // For each property
                foreach (PlyProperty property in element.properties)
                {
                    argument.property = property;
                    if (!ReadProperty(element, property, argument))
                        return false;
                }
            }
        }
        return true;
    }

    private bool ReadProperty(PlyElement element, PlyProperty property, PlyArgument argument)
    {
        Func<PlyArgument, bool>? readCb = property.readCb;
        if (property.type != PlyType.List)
        {
            argument.length = 1;
            argument.valueIndex = 0;
            if (!ReadValue(property.type, out argument.value))
            {
                errorCb($"Error reading '{property.name}' of '{element.name}' number {argument.instanceIndex}");
                return false;
            }
            if (readCb != null && !readCb(argument))
            {
                errorCb("Aborted by user");
                return false;
            }
            return true;
        }

        // Get list length
        if (!ReadValue(property.lengthType, out double length))
        {
            errorCb($"Error reading '{property.name}' of '{element.name}' number {argument.instanceIndex}");
            return false;
        }
        // Invoke callback to pass length in value field
        argument.length = (long)length;
        argument.valueIndex = -1;
        argument.value = length;
        if (readCb != null && !readCb(argument))
        {
            errorCb("Aborted by user");
            return false;
        }
        // For each value in list
        for (long l = 0; l < (long)length; l++)
        {
            // Read value from file
            argument.valueIndex = l;
            if (!ReadValue(property.valueType, out argument.value))
            {
                errorCb($"Error reading value number {l + 1} of '{property.name}' of " +
                        $"'{element.name}' number {argument.instanceIndex}");
                return false;
            }
            // Invoke callback to pass value
            if (readCb != null && !readCb(argument))
            {
                errorCb("Aborted by user");
                return false;
            }
        }
        return true;
    }

    // Like RPly's types: char/uchar/short/... are aliases of int8/uint8/...
    private static PlyType BaseType(PlyType t) => t >= PlyType.Char && t < PlyType.List ? (PlyType)(t - PlyType.Char) : t;

    private static int Size(PlyType t) => BaseType(t) switch
    {
        PlyType.Int8 or PlyType.UInt8 => 1,
        PlyType.Int16 or PlyType.UInt16 => 2,
        PlyType.Int32 or PlyType.UInt32 or PlyType.Float32 => 4,
        _ => 8
    };

    private bool ReadValue(PlyType type, out double value)
    {
        type = BaseType(type);
        value = 0;
        if (storageMode == PlyStorageMode.Ascii)
        {
            string? word = ReadWord();
            if (word == null)
                return false;
            if (type == PlyType.Float32 || type == PlyType.Float64)
            {
                // strtod()
                if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return false;
                double max = type == PlyType.Float32 ? float.MaxValue : double.MaxValue;
                return !(value < -max || value > max);
            }
            // strtol()
            if (!long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                return false;
            value = l;
            return type switch
            {
                PlyType.Int8 => l >= sbyte.MinValue && l <= sbyte.MaxValue,
                PlyType.UInt8 => l >= 0 && l <= byte.MaxValue,
                PlyType.Int16 => l >= short.MinValue && l <= short.MaxValue,
                PlyType.UInt16 => l >= 0 && l <= ushort.MaxValue,
                PlyType.Int32 => l >= int.MinValue && l <= int.MaxValue,
                _ => l >= 0 && l <= uint.MaxValue
            };
        }

        int size = Size(type);
        if (pos + size > data.Length)
            return false;
        ReadOnlySpan<byte> b = data.AsSpan(pos, size);
        pos += size;
        bool big = storageMode == PlyStorageMode.BigEndian;
        value = type switch
        {
            PlyType.Int8 => (sbyte)b[0],
            PlyType.UInt8 => b[0],
            PlyType.Int16 => big ? BinaryPrimitives.ReadInt16BigEndian(b) : BinaryPrimitives.ReadInt16LittleEndian(b),
            PlyType.UInt16 => big ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b),
            PlyType.Int32 => big ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt32LittleEndian(b),
            PlyType.UInt32 => big ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b),
            PlyType.Float32 => big ? BinaryPrimitives.ReadSingleBigEndian(b) : BinaryPrimitives.ReadSingleLittleEndian(b),
            _ => big ? BinaryPrimitives.ReadDoubleBigEndian(b) : BinaryPrimitives.ReadDoubleLittleEndian(b)
        };
        return true;
    }

    // ---- Writing ----

    public static PlyFile? Create(string name, PlyStorageMode storageMode, Action<string>? errorCb)
    {
        errorCb ??= DefaultErrorCb;
        FileStream fs;
        try
        {
            fs = File.Create(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errorCb("Unable to create file");
            return null;
        }
        if (storageMode == PlyStorageMode.Default)
            storageMode = BitConverter.IsLittleEndian ? PlyStorageMode.LittleEndian : PlyStorageMode.BigEndian;
        return new PlyFile(errorCb, new BufferedStream(fs, 1 << 16), storageMode);
    }

    public void AddElement(string name, long nInstances) => elements.Add(new PlyElement(name, nInstances));

    public void AddScalarProperty(string name, PlyType type)
    {
        CHECK(type < PlyType.List);
        elements[^1].properties.Add(new PlyProperty(name) { type = type });
    }

    public void AddListProperty(string name, PlyType lengthType, PlyType valueType)
    {
        CHECK(lengthType < PlyType.List && valueType < PlyType.List);
        elements[^1].properties.Add(new PlyProperty(name)
        {
            type = PlyType.List, lengthType = lengthType, valueType = valueType
        });
    }

    public void AddComment(string comment) => comments.Add(comment);
    public void AddObjInfo(string objInfo) => objInfos.Add(objInfo);

    private void WriteString(string s) => output!.Write(Encoding.Latin1.GetBytes(s));

    public bool WriteHeader()
    {
        StringBuilder sb = new();
        sb.Append($"ply\nformat {storageModeNames[(int)storageMode]} 1.0\n");
        foreach (string c in comments)
            sb.Append($"comment {c}\n");
        foreach (string o in objInfos)
            sb.Append($"obj_info {o}\n");
        foreach (PlyElement element in elements)
        {
            sb.Append(CultureInfo.InvariantCulture, $"element {element.name} {element.nInstances}\n");
            foreach (PlyProperty property in element.properties)
            {
                if (property.type == PlyType.List)
                    sb.Append($"property list {typeNames[(int)property.lengthType]} " +
                              $"{typeNames[(int)property.valueType]} {property.name}\n");
                else
                    sb.Append($"property {typeNames[(int)property.type]} {property.name}\n");
            }
        }
        sb.Append("end_header\n");
        WriteString(sb.ToString());
        // (Skip leading elements without instances, as RPly's writes do
        // after each element.)
        while (wElement < elements.Count && elements[wElement].nInstances == 0)
            ++wElement;
        return true;
    }

    public bool Write(double value)
    {
        if (wElement >= elements.Count)
            return false;
        PlyElement element = elements[wElement];
        if (wProperty >= element.properties.Count)
            return false;
        PlyProperty property = element.properties[wProperty];
        PlyType type;
        if (property.type == PlyType.List)
        {
            if (wValueIndex == 0)
            {
                type = property.lengthType;
                wLength = (long)value;
            }
            else
                type = property.valueType;
        }
        else
        {
            type = property.type;
            wLength = 0;
        }
        if (!WriteValue(type, value))
        {
            errorCb($"Failed writing {property.name} of {element.name} {wInstanceIndex} " +
                    $"({(storageMode == PlyStorageMode.Ascii ? "ascii" : "binary")}: {typeNames[(int)type]})");
            return false;
        }
        bool breakAfter = false, spaceAfter = true;
        wValueIndex++;
        if (wValueIndex > wLength)
        {
            wValueIndex = 0;
            wProperty++;
        }
        if (wProperty >= element.properties.Count)
        {
            wProperty = 0;
            wInstanceIndex++;
            breakAfter = true;
            spaceAfter = false;
        }
        if (wInstanceIndex >= element.nInstances)
        {
            wInstanceIndex = 0;
            do
                wElement++;
            while (wElement < elements.Count && elements[wElement].nInstances == 0);
        }
        if (storageMode == PlyStorageMode.Ascii)
        {
            if (spaceAfter)
                output!.WriteByte((byte)' ');
            if (breakAfter)
                output!.WriteByte((byte)'\n');
        }
        return true;
    }

    private bool WriteValue(PlyType type, double value)
    {
        type = BaseType(type);
        (double min, double max) = type switch
        {
            PlyType.Int8 => ((double)sbyte.MinValue, (double)sbyte.MaxValue),
            PlyType.UInt8 => (0, byte.MaxValue),
            PlyType.Int16 => (short.MinValue, short.MaxValue),
            PlyType.UInt16 => (0, ushort.MaxValue),
            PlyType.Int32 => (int.MinValue, int.MaxValue),
            PlyType.UInt32 => (0, uint.MaxValue),
            PlyType.Float32 => (-float.MaxValue, float.MaxValue),
            _ => (-double.MaxValue, double.MaxValue)
        };
        if (value > max || value < min)
            return false;

        if (storageMode == PlyStorageMode.Ascii)
        {
            WriteString(type switch
            {
                PlyType.Float32 => FormatG((float)value),
                PlyType.Float64 => FormatG(value),
                // (RPly prints uint32 with %d.)
                PlyType.UInt32 => ((int)(uint)value).ToString(CultureInfo.InvariantCulture),
                _ => ((long)value).ToString(CultureInfo.InvariantCulture)
            });
            return true;
        }

        Span<byte> b = stackalloc byte[Size(type)];
        bool big = storageMode == PlyStorageMode.BigEndian;
        switch (type)
        {
            case PlyType.Int8: b[0] = (byte)(sbyte)value; break;
            case PlyType.UInt8: b[0] = (byte)value; break;
            case PlyType.Int16:
                if (big) BinaryPrimitives.WriteInt16BigEndian(b, (short)value); else BinaryPrimitives.WriteInt16LittleEndian(b, (short)value);
                break;
            case PlyType.UInt16:
                if (big) BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value); else BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)value);
                break;
            case PlyType.Int32:
                if (big) BinaryPrimitives.WriteInt32BigEndian(b, (int)value); else BinaryPrimitives.WriteInt32LittleEndian(b, (int)value);
                break;
            case PlyType.UInt32:
                if (big) BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value); else BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)value);
                break;
            case PlyType.Float32:
                if (big) BinaryPrimitives.WriteSingleBigEndian(b, (float)value); else BinaryPrimitives.WriteSingleLittleEndian(b, (float)value);
                break;
            default:
                if (big) BinaryPrimitives.WriteDoubleBigEndian(b, value); else BinaryPrimitives.WriteDoubleLittleEndian(b, value);
                break;
        }
        output!.Write(b);
        return true;
    }

    // printf("%g")
    private static string FormatG(double v)
    {
        if (v == 0)
            return double.IsNegative(v) ? "-0" : "0";
        if (double.IsNaN(v))
            return "nan";
        if (double.IsInfinity(v))
            return v < 0 ? "-inf" : "inf";
        // %g uses 6 significant digits and %e style for exponents < -4 or
        // >= 6, with trailing zeros removed.
        string e = v.ToString("E5", CultureInfo.InvariantCulture);
        int exp = int.Parse(e.AsSpan(e.IndexOf('E') + 1), CultureInfo.InvariantCulture);
        if (exp < -4 || exp >= 6)
        {
            string mant = e[..e.IndexOf('E')];
            if (mant.Contains('.'))
                mant = mant.TrimEnd('0').TrimEnd('.');
            return $"{mant}e{(exp < 0 ? '-' : '+')}{Math.Abs(exp):00}";
        }
        string f = v.ToString("F" + Math.Max(0, 5 - exp), CultureInfo.InvariantCulture);
        if (f.Contains('.'))
            f = f.TrimEnd('0').TrimEnd('.');
        return f;
    }

    public bool Close()
    {
        if (output != null)
        {
            try
            {
                output.Dispose();
            }
            catch (IOException)
            {
                errorCb("Error closing up");
                return false;
            }
        }
        return true;
    }
}

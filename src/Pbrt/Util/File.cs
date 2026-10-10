// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/file.h and file.cpp
//
// File contents are byte arrays (pbrt uses std::string). FOpenRead() and
// FOpenWrite() aren't needed; .NET's file APIs handle UTF-8/UTF-16 names.

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Pbrt;

public static partial class Util
{
    private static string searchDirectory = "";

    // File and Filename Function Definitions
    public static void SetSearchDirectory(string filename)
    {
        string path = filename;
        if (!Directory.Exists(path))
            path = Path.GetDirectoryName(path) ?? "";
        searchDirectory = path;
    }

    private static bool IsAbsolutePath(string filename)
    {
        if (filename.Length == 0)
            return false;
        return Path.IsPathRooted(filename);
    }

    public static string ResolveFilename(string filename)
    {
        if (searchDirectory.Length == 0 || filename.Length == 0 || IsAbsolutePath(filename))
            return filename;

        string filepath = Path.Combine(searchDirectory, filename);
        if (File.Exists(filepath) || Directory.Exists(filepath))
            return Path.GetFullPath(filepath);
        return filename;
    }

    public static List<string> MatchingFilenames(string filenameBase)
    {
        List<string> filenames = [];

        string dirStr = Path.GetDirectoryName(filenameBase) ?? "";
        string baseName = Path.GetFileName(filenameBase);
        if (dirStr.Length == 0)
            dirStr = ".";
        if (!Directory.Exists(dirStr))
            ErrorExit($"{Path.GetDirectoryName(filenameBase)}: unable to open directory\n");

        foreach (string path in Directory.EnumerateFiles(dirStr))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith(baseName, StringComparison.Ordinal))
                filenames.Add(Path.Combine(Path.GetDirectoryName(filenameBase) ?? "", name));
        }

        return filenames;
    }

    public static bool FileExists(string filename) => File.Exists(filename);

    public static bool HasExtension(string filename, string e)
    {
        string ext = e;
        if (ext.Length != 0 && ext[0] == '.')
            ext = ext[1..];

        string filenameExtension = Path.GetExtension(filename);
        if (filenameExtension.StartsWith('.'))
            filenameExtension = filenameExtension[1..];
        if (ext.Length > filenameExtension.Length)
            return false;
        return filenameExtension.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }

    public static string RemoveExtension(string filename)
    {
        string ext = Path.GetExtension(filename);
        if (ext.Length <= 1)
            return filename;
        return filename[..^ext.Length];
    }

    // pbrt returns the contents as a std::string; here they are bytes.
    public static byte[] ReadFileContents(string filename)
    {
        try
        {
            return File.ReadAllBytes(filename);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorExit($"{filename}: {ErrorString(e)}");
            return [];
        }
    }

    public static byte[] ReadDecompressedFileContents(string filename)
    {
        byte[] compressed = ReadFileContents(filename);

        // (pbrt gets the size of the uncompressed data from the last 4 bytes
        // of the gzip file to size its buffer; GZipStream doesn't need that.)
        CHECK_GT(compressed.Length, 4);

        try
        {
            using MemoryStream input = new(compressed);
            using GZipStream gz = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            gz.CopyTo(output);
            byte[] decompressed = output.ToArray();
            LOG_VERBOSE($"Decompressed {filename} from {compressed.Length} to {decompressed.Length} bytes");
            return decompressed;
        }
        catch (InvalidDataException)
        {
            ErrorExit($"{filename}: invalid or corrupt compressed data");
            return [];
        }
    }

    public static bool WriteFileContents(string filename, string contents) =>
        WriteFileContents(filename, Encoding.UTF8.GetBytes(contents));

    public static bool WriteFileContents(string filename, ReadOnlySpan<byte> contents)
    {
        try
        {
            using FileStream f = File.Create(filename);
            f.Write(contents);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error($"{filename}: {e.Message}");
            return false;
        }
    }

    public static bool RemoveFile(string filename)
    {
        try
        {
            if (!File.Exists(filename))
                return false;
            File.Delete(filename);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static List<Float> ReadFloatFile(string filename)
    {
        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(filename);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error($"{filename}: unable to open file");
            return [];
        }

        bool inNumber = false;
        var curNumber = new StringBuilder();
        int lineNumber = 1;
        var values = new List<Float>();
        for (int pos = 0; pos < contents.Length; ++pos)
        {
            char c = (char)contents[pos];
            if (c == '\n')
                ++lineNumber;
            if (inNumber)
            {
                if (curNumber.Length >= 32)
                    LOG_FATAL($"Overflowed buffer for parsing number in file: {filename} at line {lineNumber}");
                // Note: this is not very robust, and would accept something
                // like 0.0.0.0eeee-+--2 as a valid number.
                if (char.IsAsciiDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')
                {
                    curNumber.Append(c);
                }
                else
                {
                    if (!Float.TryParse(curNumber.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                        out Float v))
                        ErrorExit($"{filename}: unable to parse float value \"{curNumber}\"");
                    values.Add(v);
                    inNumber = false;
                    curNumber.Clear();
                }
            }
            else
            {
                if (char.IsAsciiDigit(c) || c == '.' || c == '-' || c == '+')
                {
                    inNumber = true;
                    curNumber.Append(c);
                }
                else if (c == '#')
                {
                    while (++pos < contents.Length && contents[pos] != '\n')
                        ;
                    ++lineNumber;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    Error($"{filename}: unexpected character \"{c}\" found at line {lineNumber}.");
                    return [];
                }
            }
        }
        return values;
    }
}

// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/string.h and string.cpp
//
// C# strings are UTF-16, so pbrt's UTF-8/UTF-16 conversions aren't needed;
// NormalizeUTF8() is NFC normalization (pbrt uses utf8proc's COMPOSE). The
// InternedString class becomes a plain string from InternCache.

using System.Globalization;
using System.Text;

namespace Pbrt;

public static partial class Util
{
    // Atoi()/Atof() follow std::stoi()/std::stof(): leading whitespace is
    // skipped and the longest numeric prefix is converted; they fail if
    // there is none or it's out of range.
    public static bool Atoi(string str, out int v)
    {
        v = 0;
        if (!Atoi(str, out long l) || l < int.MinValue || l > int.MaxValue)
            return false;
        v = (int)l;
        return true;
    }

    public static bool Atoi(string str, out long v)
    {
        int i = 0;
        while (i < str.Length && char.IsWhiteSpace(str[i]))
            ++i;
        int start = i;
        if (i < str.Length && (str[i] == '+' || str[i] == '-'))
            ++i;
        int digits = i;
        while (i < str.Length && char.IsAsciiDigit(str[i]))
            ++i;
        if (i == digits)
        {
            v = 0;
            return false;
        }
        return long.TryParse(str.AsSpan(start, i - start), NumberStyles.AllowLeadingSign,
                             CultureInfo.InvariantCulture, out v);
    }

    public static bool Atof(string str, out float v)
    {
        v = 0;
        if (!AtofPrefix(str, out double d))
            return false;
        // std::stof() fails if the value is out of float's range.
        if (!double.IsInfinity(d) && !double.IsNaN(d) && Math.Abs(d) > float.MaxValue)
            return false;
        v = (float)d;
        return true;
    }

    public static bool Atof(string str, out double v) => AtofPrefix(str, out v);

    private static bool AtofPrefix(string str, out double v)
    {
        v = 0;
        int i = 0;
        while (i < str.Length && char.IsWhiteSpace(str[i]))
            ++i;
        int start = i;
        if (i < str.Length && (str[i] == '+' || str[i] == '-'))
            ++i;
        // inf, infinity, nan
        foreach (string word in (ReadOnlySpan<string>)["infinity", "inf", "nan"])
            if (string.Compare(str, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                bool neg = i > start && str[start] == '-';
                v = word == "nan" ? double.NaN : neg ? double.NegativeInfinity : double.PositiveInfinity;
                return true;
            }
        int mantissa = i;
        while (i < str.Length && char.IsAsciiDigit(str[i]))
            ++i;
        if (i < str.Length && str[i] == '.')
        {
            ++i;
            while (i < str.Length && char.IsAsciiDigit(str[i]))
                ++i;
        }
        if (i == mantissa || (i == mantissa + 1 && str[mantissa] == '.'))
            return false;
        if (i < str.Length && (str[i] == 'e' || str[i] == 'E'))
        {
            int e = i + 1;
            if (e < str.Length && (str[e] == '+' || str[e] == '-'))
                ++e;
            int expDigits = e;
            while (e < str.Length && char.IsAsciiDigit(str[e]))
                ++e;
            if (e > expDigits)
                i = e;
        }
        if (!double.TryParse(str.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return false;
        // std::stod() reports overflow as out of range.
        return !double.IsInfinity(v);
    }

    public static List<string> SplitStringsFromWhitespace(string str)
    {
        List<string> ret = [];

        int start = 0;
        do
        {
            // skip leading ws
            while (start != str.Length && char.IsWhiteSpace(str[start]))
                ++start;

            // |start| is at the start of the current word
            int end = start;
            while (end != str.Length && !char.IsWhiteSpace(str[end]))
                ++end;

            ret.Add(str[start..end]);
            start = end;
        } while (start != str.Length);

        return ret;
    }

    public static List<string> SplitString(string str, char ch)
    {
        List<string> strings = [];

        if (str.Length == 0)
            return strings;

        int begin = 0;
        while (true)
        {
            int end = begin;
            while (end != str.Length && str[end] != ch)
                ++end;

            strings.Add(str[begin..end]);

            if (end == str.Length)
                break;

            begin = end + 1;
        }

        return strings;
    }

    public static List<int> SplitStringToInts(string str, char ch)
    {
        List<string> strs = SplitString(str, ch);
        List<int> ints = new(strs.Count);

        foreach (string s in strs)
        {
            if (!Atoi(s, out int v))
                return [];
            ints.Add(v);
        }
        return ints;
    }

    public static List<long> SplitStringToInt64s(string str, char ch)
    {
        List<string> strs = SplitString(str, ch);
        List<long> ints = new(strs.Count);

        foreach (string s in strs)
        {
            if (!Atoi(s, out long v))
                return [];
            ints.Add(v);
        }
        return ints;
    }

    public static List<Float> SplitStringToFloats(string str, char ch)
    {
        List<string> strs = SplitString(str, ch);
        List<Float> floats = new(strs.Count);

        foreach (string s in strs)
        {
            if (!Atof(s, out Float v))
                return [];
            floats.Add(v);
        }
        return floats;
    }

    public static List<double> SplitStringToDoubles(string str, char ch)
    {
        List<string> strs = SplitString(str, ch);
        List<double> doubles = new(strs.Count);

        foreach (string s in strs)
        {
            if (!Atof(s, out double v))
                return [];
            doubles.Add(v);
        }
        return doubles;
    }

    public static string NormalizeUTF8(string str)
    {
        try
        {
            return str.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException e)
        {
            ErrorExit($"Unicode normalization error: {e.Message}: \"{str}\"");
            return str;
        }
    }
}

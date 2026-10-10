// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Pieces of the C and C++ standard libraries that imgtool's output depends
// on: printf()'s "%g" and "%f" conversions, and std::sort() and
// std::nth_element() for "imgtool cat --sort" and "imgtool convert
// --despike".

using System.Globalization;

namespace Pbrt.ImgTool;

internal static class CStd
{
    // printf("%.<precision>f", v)
    public static string F(double v, int precision = 6)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return NonFinite(v);
        return v.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // printf("%<width>.<precision>g", v)
    public static string G(double v, int precision = 6, int width = 0)
    {
        string s;
        if (double.IsNaN(v) || double.IsInfinity(v))
            s = NonFinite(v);
        else
        {
            int P = precision == 0 ? 1 : precision;
            // .NET's "E" formatting is exact and rounds ties to even, like
            // glibc's.
            string e = v.ToString("E" + (P - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int ei = e.IndexOf('E');
            int X = int.Parse(e.AsSpan(ei + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (P > X && X >= -4)
                s = StripZeros(v.ToString("F" + (P - 1 - X).ToString(CultureInfo.InvariantCulture),
                                          CultureInfo.InvariantCulture));
            else
                s = StripZeros(e[..ei]) + "e" + (X < 0 ? "-" : "+") +
                    Math.Abs(X).ToString("00", CultureInfo.InvariantCulture);
        }
        return s.PadLeft(width);
    }

    private static string NonFinite(double v)
    {
        bool negative = BitConverter.DoubleToInt64Bits(v) < 0;
        return (negative ? "-" : "") + (double.IsNaN(v) ? "nan" : "inf");
    }

    private static string StripZeros(string s)
    {
        if (!s.Contains('.'))
            return s;
        s = s.TrimEnd('0');
        return s.EndsWith('.') ? s[..^1] : s;
    }

    ///////////////////////////////////////////////////////////////////////////
    // std::sort() and std::nth_element() (the order of equivalent elements is
    // unspecified, as in C++)

    public static void Sort<T>(Span<T> a, Func<T, T, bool> comp) =>
        a.Sort((x, y) => comp(x, y) ? -1 : comp(y, x) ? 1 : 0);

    // Sorting the whole span puts the nth element in place.
    public static void NthElement<T>(Span<T> a, int nth, Func<T, T, bool> comp) => Sort(a, comp);
}

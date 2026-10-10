// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of the parts of src/pbrt/util/print.h and print.cpp that C# needs:
// the ANSI color helpers and FloatToString()/DoubleToString(), which is how
// pbrt's StringPrintf() formats floats for "%f" and "%s". pbrt's
// StringPrintf()/Printf() are otherwise C# string interpolation.

using System.Globalization;
using System.Text;

namespace Pbrt;

public static partial class Util
{
    // https://en.wikipedia.org/wiki/ANSI_escape_code#Colors
    public static string Red(string s)
    {
        const string red = "\u001b[1m\u001b[31m";  // bold red
        const string reset = "\u001b[0m";
        return red + s + reset;
    }

    public static string Yellow(string s)
    {
        // https://en.wikipedia.org/wiki/ANSI_escape_code#8-bit
        const string yellow = "\u001b[1m\u001b[38;5;100m";
        const string reset = "\u001b[0m";
        return yellow + s + reset;
    }

    public static string Green(string s)
    {
        // https://en.wikipedia.org/wiki/ANSI_escape_code#8-bit
        const string green = "\u001b[1m\u001b[38;5;22m";
        const string reset = "\u001b[0m";
        return green + s + reset;
    }

    // pbrt formats floats and doubles with double-conversion's ToShortest(),
    // with decimal notation for exponents in [-6, 9) and otherwise
    // exponential notation without a '+' ("1e9", "1.5e-7").
    public static string FloatToString(float v) =>
        ShortestToString(v.ToString("R", CultureInfo.InvariantCulture), float.IsNegative(v), float.IsNaN(v),
                         float.IsInfinity(v));

    // pbrt picks the formatter by the argument's type, so Float values in a
    // double build print as doubles.
    public static string FloatToString(double v) => DoubleToString(v);

    public static string DoubleToString(double v) =>
        ShortestToString(v.ToString("R", CultureInfo.InvariantCulture), double.IsNegative(v), double.IsNaN(v),
                         double.IsInfinity(v));

    private static string ShortestToString(string r, bool negative, bool isNaN, bool isInf)
    {
        if (isNaN)
            return "NaN";
        if (isInf)
            return negative ? "-Inf" : "Inf";

        // Find the shortest digits and the position of the decimal point
        // from .NET's shortest round-trip string ("1.5E-07", "123.25").
        if (r[0] == '-')
            r = r[1..];
        int exp = 0;
        int e = r.IndexOf('E');
        if (e >= 0)
        {
            exp = int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }
        int dot = r.IndexOf('.');
        string digits = dot >= 0 ? r.Remove(dot, 1) : r;
        int decimalPoint = (dot >= 0 ? dot : r.Length) + exp;
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0')
            ++lead;
        digits = digits[lead..];
        decimalPoint -= lead;
        digits = digits.TrimEnd('0');

        StringBuilder sb = new();
        if (negative)
            sb.Append('-');
        if (digits.Length == 0)
            return sb.Append('0').ToString();

        int exponent = decimalPoint - 1;
        if (exponent >= -6 && exponent < 9)
        {
            if (decimalPoint <= 0)
                sb.Append("0.").Append('0', -decimalPoint).Append(digits);
            else if (decimalPoint >= digits.Length)
                sb.Append(digits).Append('0', decimalPoint - digits.Length);
            else
                sb.Append(digits, 0, decimalPoint).Append('.').Append(digits, decimalPoint,
                                                                       digits.Length - decimalPoint);
        }
        else
        {
            sb.Append(digits[0]);
            if (digits.Length > 1)
                sb.Append('.').Append(digits, 1, digits.Length - 1);
            sb.Append('e').Append(exponent.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}

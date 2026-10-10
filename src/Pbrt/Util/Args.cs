// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/args.h and args.cpp
//
// pbrt's ParseArg(&iter, end, name, T *out, onError) takes an iterator into
// the argument list; here it's the list and a `ref int` index. There is an
// overload for each kind of value pbrt parses; pstd::optional<T> outputs
// are `ref T?`, spans are Span<T>.

using System.Text;

namespace Pbrt;

public static partial class Util
{
    // Downcase the string and remove any '-' or '_' characters; thus we can be
    // a little flexible in what we match for argument names.
    internal static string normalizeArg(string str)
    {
        StringBuilder ret = new();
        foreach (char c in str)
        {
            if (c != '_' && c != '-')
                ret.Append(char.ToLowerInvariant(c));
        }
        return ret.ToString();
    }

    private delegate bool InitArg<T>(string str, out T value);

    private static bool initArg(string str, out int v)
    {
        v = 0;
        if (str.Length == 0 || (!char.IsAsciiDigit(str[0]) && str[0] != '-'))
            return false;
        return Atoi(str, out v);
    }

    private static bool initArg(string str, out float v)
    {
        v = 0;
        if (str.Length == 0)
            return false;
        return Atof(str, out v);
    }

    private static bool initArg(string str, out double v)
    {
        v = 0;
        if (str.Length == 0)
            return false;
        return Atof(str, out v);
    }

    private static bool initArg(string str, out string v)
    {
        v = str;
        return str.Length != 0;
    }

    private static bool initArg(string str, out bool v)
    {
        v = false;
        if (normalizeArg(str) == "false")
        {
            v = false;
            return true;
        }
        else if (normalizeArg(str) == "true")
        {
            v = true;
            return true;
        }
        return false;
    }

    private static bool initArgs<T>(List<T> v, Span<T> @out)
    {
        if (v.Count != @out.Length)
            return false;
        v.CopyTo(@out);
        return true;
    }

    private static bool matchPrefix(string str, string prefix)
    {
        if (prefix.Length > str.Length)
            return false;
        for (int i = 0; i < prefix.Length; ++i)
            if (prefix[i] != str[i])
                return false;
        return true;
    }

    // The shared ParseArg() logic: _init_ parses a value into the output;
    // _enable_ (for bool arguments) sets it for a bare "--name".
    private static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name,
                                 Func<string, bool> init, Func<bool>? enable, Action<string> onError)
    {
        string arg = args[iter];

        // Strip either one or two leading dashes.
        if (arg.Length > 1 && arg[1] == '-')
            arg = arg[2..];
        else
            arg = arg[1..];

        if (matchPrefix(normalizeArg(arg), normalizeArg(name + '=')))
        {
            // --arg=value
            string value = arg[(name.Length + 1)..];
            if (!init(value))
            {
                onError($"invalid value \"{value}\" for --{name} argument");
                return false;
            }
            return true;
        }
        else if (normalizeArg(arg) == normalizeArg(name))
        {
            // --arg <value>, except for bool arguments, which are set to true
            // without expecting another argument.
            if (enable != null && enable())
                return true;

            ++iter;
            if (iter == args.Count)
            {
                onError($"missing value after --{arg} argument");
                return false;
            }
            if (!init(args[iter]))
            {
                onError($"invalid value \"{args[iter]}\" for --{name} argument");
                return false;
            }
            return true;
        }
        else
            return false;
    }

    // Sets _out_ only if parsing succeeds (pbrt writes through its pointer
    // only then, too).
    private static bool ParseArg<T>(IReadOnlyList<string> args, ref int iter, string name, ref T @out,
                                    InitArg<T> init, Action<string> onError)
    {
        T value = @out;
        bool result = ParseArg(args, ref iter, name, s =>
        {
            if (!init(s, out T v))
                return false;
            value = v;
            return true;
        }, null, onError);
        @out = value;
        return result;
    }

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref int @out,
                                Action<string> onError) =>
        ParseArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref float @out,
                                Action<string> onError) =>
        ParseArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref double @out,
                                Action<string> onError) =>
        ParseArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref string @out,
                                Action<string> onError) =>
        ParseArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref bool @out,
                                Action<string> onError)
    {
        bool value = @out;
        bool result = ParseArg(args, ref iter, name, s =>
        {
            if (!initArg(s, out bool v))
                return false;
            value = v;
            return true;
        }, () =>
        {
            // enable(bool *ptr)
            value = true;
            return true;
        }, onError);
        @out = value;
        return result;
    }

    // pstd::optional<T> outputs
    private static bool ParseOptionalArg<T>(IReadOnlyList<string> args, ref int iter, string name, ref T? @out,
                                            InitArg<T> init, Action<string> onError) where T : struct
    {
        T? value = @out;
        bool result = ParseArg(args, ref iter, name, s =>
        {
            if (!init(s, out T v))
                return false;
            value = v;
            return true;
        }, null, onError);
        @out = value;
        return result;
    }

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref int? @out,
                                Action<string> onError) =>
        ParseOptionalArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref float? @out,
                                Action<string> onError) =>
        ParseOptionalArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref double? @out,
                                Action<string> onError) =>
        ParseOptionalArg(args, ref iter, name, ref @out, initArg, onError);

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, ref bool? @out,
                                Action<string> onError) =>
        ParseOptionalArg(args, ref iter, name, ref @out, initArg, onError);

    // Span outputs: comma-separated values, which must fill the span.
    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, Span<float> @out,
                                Action<string> onError)
    {
        float[] v = new float[@out.Length];
        bool ok = ParseArg(args, ref iter, name, s => initArgs(SplitStringToFloats(s, ',').ConvertAll(f => (float)f), v), null, onError);
        if (ok)
            v.CopyTo(@out);
        return ok;
    }

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, Span<double> @out,
                                Action<string> onError)
    {
        double[] v = new double[@out.Length];
        bool ok = ParseArg(args, ref iter, name, s => initArgs(SplitStringToDoubles(s, ','), v), null, onError);
        if (ok)
            v.CopyTo(@out);
        return ok;
    }

    public static bool ParseArg(IReadOnlyList<string> args, ref int iter, string name, Span<int> @out,
                                Action<string> onError)
    {
        int[] v = new int[@out.Length];
        bool ok = ParseArg(args, ref iter, name, s => initArgs(SplitStringToInts(s, ','), v), null, onError);
        if (ok)
            v.CopyTo(@out);
        return ok;
    }

    // Command-line Argument Utility Functions
    // (C#'s Main() arguments already exclude the executable name.)
    public static List<string> GetCommandLineArguments(string[] argv) => [.. argv];
}

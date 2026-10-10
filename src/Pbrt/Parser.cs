// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/parser.h and parser.cpp
//
// Files are read and decoded as UTF-8 and tokenized as C# strings (pbrt
// tokenizes the bytes of a memory-mapped file), so token columns count
// UTF-16 characters. Tokens are strings rather than string_views.
// FormattingParserTarget writes to a TextWriter (stdout by default), and its
// destructor's error check happens in EndOfFiles(). Numbers are parsed as
// pbrt does with double-conversion: decimal or hexadecimal integers, and
// decimal floats rounded directly to Float.

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Pbrt;

// ParserTarget Definition
public abstract class ParserTarget
{
    // ParserTarget Interface
    public abstract void Scale(Float sx, Float sy, Float sz, FileLoc loc);
    public abstract void Shape(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Option(string name, string value, FileLoc loc);
    public abstract void Identity(FileLoc loc);
    public abstract void Translate(Float dx, Float dy, Float dz, FileLoc loc);
    public abstract void Rotate(Float angle, Float ax, Float ay, Float az, FileLoc loc);
    public abstract void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz, Float ux, Float uy,
                                Float uz, FileLoc loc);
    public abstract void ConcatTransform(Float[] transform, FileLoc loc);
    public abstract void Transform(Float[] transform, FileLoc loc);
    public abstract void CoordinateSystem(string name, FileLoc loc);
    public abstract void CoordSysTransform(string name, FileLoc loc);
    public abstract void ActiveTransformAll(FileLoc loc);
    public abstract void ActiveTransformEndTime(FileLoc loc);
    public abstract void ActiveTransformStartTime(FileLoc loc);
    public abstract void TransformTimes(Float start, Float end, FileLoc loc);

    public abstract void ColorSpace(string n, FileLoc loc);
    public abstract void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Film(string type, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Camera(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void MakeNamedMedium(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void MediumInterface(string insideName, string outsideName, FileLoc loc);
    public abstract void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc);

    public abstract void WorldBegin(FileLoc loc);
    public abstract void AttributeBegin(FileLoc loc);
    public abstract void AttributeEnd(FileLoc loc);
    public abstract void Attribute(string target, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void Texture(string name, string type, string texname, List<ParsedParameter> parameters,
                                 FileLoc loc);
    public abstract void Material(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void MakeNamedMaterial(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void NamedMaterial(string name, FileLoc loc);
    public abstract void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc);
    public abstract void ReverseOrientation(FileLoc loc);
    public abstract void ObjectBegin(string name, FileLoc loc);
    public abstract void ObjectEnd(FileLoc loc);
    public abstract void ObjectInstance(string name, FileLoc loc);

    public abstract void EndOfFiles();

    // ParserTarget Protected Methods
    protected void ErrorExitDeferred(string message)
    {
        errorExit = true;
        Error(message);
    }

    protected void ErrorExitDeferred(FileLoc? loc, string message)
    {
        errorExit = true;
        Error(loc, message);
    }

    protected bool errorExit = false;
}

// Token Definition
public readonly struct Token(string token, FileLoc loc)
{
    public readonly string token = token;
    public readonly FileLoc loc = loc;

    public override string ToString() => $"[ Token token: {token} loc: {loc} ]";
}

// Tokenizer Definition
public sealed class Tokenizer
{
    private static readonly StatMemoryCounter tokenizerMemory = new("Memory/Tokenizer buffers");

    // Tokenizer Public Members
    // Just for parse().
    // TODO? Have a method to set this?
    public FileLoc loc;

    // Tokenizer Private Members
    // This function is called if there is an error during lexing.
    private readonly Action<string, FileLoc?> errorCallback;
    private readonly string contents;
    private int pos;
    private readonly int end;

    // Tokenizer Public Methods
    public Tokenizer(string str, string filename, Action<string, FileLoc?> errorCallback)
    {
        this.errorCallback = errorCallback;
        contents = str;
        loc = new FileLoc(filename);
        pos = 0;
        end = contents.Length;
        tokenizerMemory.Add(contents.Length);
    }

    private Tokenizer(byte[] bytes, string filename, Action<string, FileLoc?> errorCallback)
        : this(Encoding.UTF8.GetString(bytes), filename, errorCallback)
    {
        CheckUTF(bytes);
    }

    public static Tokenizer? CreateFromFile(string filename, Action<string, FileLoc?> errorCallback)
    {
        if (filename == "-")
        {
            // Handle stdin by slurping everything into a string.
            using Stream stdin = Console.OpenStandardInput();
            using MemoryStream ms = new();
            stdin.CopyTo(ms);
            return new Tokenizer(ms.ToArray(), "<stdin>", errorCallback);
        }

        if (HasExtension(filename, ".gz"))
            return new Tokenizer(ReadDecompressedFileContents(filename), filename, errorCallback);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filename);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errorCallback($"{filename}: {ErrorString(e)}", null);
            return null;
        }
        return new Tokenizer(bytes, filename, errorCallback);
    }

    public static Tokenizer CreateFromString(string str, Action<string, FileLoc?> errorCallback) =>
        new(str, "<stdin>", errorCallback);

    // Tokenizer Private Methods
    private void CheckUTF(byte[] c)
    {
        // https://en.wikipedia.org/wiki/Byte_order_mark
        if (c.Length >= 2 && ((c[0] == 0xfe && c[1] == 0xff) || (c[0] == 0xff && c[1] == 0xfe)))
            errorCallback("File is encoded with UTF-16, which is not currently " +
                          "supported by pbrt (https://github.com/mmp/pbrt-v4/issues/136).", loc);
    }

    private const int EOF = -1;

    private int getChar()
    {
        if (pos == end)
            return EOF;
        int ch = contents[pos++];
        if (ch == '\n')
        {
            ++loc.line;
            loc.column = 0;
        }
        else
            ++loc.column;
        return ch;
    }

    private void ungetChar()
    {
        --pos;
        if (contents[pos] == '\n')
            // Don't worry about the column; we'll be going to the start of
            // the next line again shortly...
            --loc.line;
    }

    private static char decodeEscaped(int ch, FileLoc loc)
    {
        switch (ch)
        {
            case EOF:
                ErrorExit(loc, "premature EOF after character escape '\\'");
                break;
            case 'b':
                return '\b';
            case 'f':
                return '\f';
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            case '\\':
                return '\\';
            case '\'':
                return '\'';
            case '\"':
                return '\"';
            default:
                ErrorExit(loc, $"unexpected escaped character \"{(char)ch}\"");
                break;
        }
        return '\0'; // NOTREACHED
    }

    public Token? Next()
    {
        while (true)
        {
            int tokenStart = pos;
            FileLoc startLoc = loc;

            int ch = getChar();
            if (ch == EOF)
                return null;
            else if (ch == ' ' || ch == '\n' || ch == '\t' || ch == '\r')
            {
                // nothing
            }
            else if (ch == '"')
            {
                // scan to closing quote
                bool haveEscaped = false;
                while ((ch = getChar()) != '"')
                {
                    if (ch == EOF)
                    {
                        errorCallback("premature EOF", startLoc);
                        return null;
                    }
                    else if (ch == '\n')
                    {
                        errorCallback("unterminated string", startLoc);
                        return null;
                    }
                    else if (ch == '\\')
                    {
                        haveEscaped = true;
                        // Grab the next character
                        if ((ch = getChar()) == EOF)
                        {
                            errorCallback("premature EOF", startLoc);
                            return null;
                        }
                    }
                }

                if (!haveEscaped)
                    return new Token(contents[tokenStart..pos], startLoc);
                else
                {
                    StringBuilder sEscaped = new();
                    for (int p = tokenStart; p < pos; ++p)
                    {
                        if (contents[p] != '\\')
                            sEscaped.Append(contents[p]);
                        else
                        {
                            ++p;
                            CHECK_LT(p, pos);
                            sEscaped.Append(decodeEscaped(contents[p], startLoc));
                        }
                    }
                    return new Token(sEscaped.ToString(), startLoc);
                }
            }
            else if (ch == '[' || ch == ']')
                return new Token(contents.Substring(tokenStart, 1), startLoc);
            else if (ch == '#')
            {
                // comment: scan to EOL (or EOF)
                while ((ch = getChar()) != EOF)
                {
                    if (ch == '\n' || ch == '\r')
                    {
                        ungetChar();
                        break;
                    }
                }

                return new Token(contents[tokenStart..pos], startLoc);
            }
            else
            {
                // Regular statement or numeric token; scan until we hit a
                // space, opening quote, or bracket.
                while ((ch = getChar()) != EOF)
                {
                    if (ch == ' ' || ch == '\n' || ch == '\t' || ch == '\r' || ch == '"' || ch == '[' || ch == ']')
                    {
                        ungetChar();
                        break;
                    }
                }
                return new Token(contents[tokenStart..pos], startLoc);
            }
        }
    }
}

public static partial class Util
{
    private static int parseInt(Token t)
    {
        bool negate = t.token[0] == '-';

        int index = 0;
        if (t.token[0] == '+' || t.token[0] == '-')
            ++index;

        long value = 0;
        while (index < t.token.Length)
        {
            if (!(t.token[index] >= '0' && t.token[index] <= '9'))
                ErrorExit(t.loc, $"\"{t.token[index]}\": expected a number");
            value = 10 * value + (t.token[index] - '0');
            ++index;

            if (value > int.MaxValue)
                ErrorExit(t.loc, "Numeric value too large to represent as a 32-bit integer.");
            else if (value < int.MinValue)
                Warning(t.loc, "Numeric value %d too low to represent as a 32-bit integer.");
        }

        return (int)(negate ? -value : value);
    }

    // double-conversion's StringToFloat() with ALLOW_HEX and no junk,
    // spaces, infinity or NaN; returns false if _s_ isn't all a number.
    internal static bool StringToFloat(string s, out Float v)
    {
        v = 0;
        int i = 0;
        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            ++i;
        }
        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            // Hexadecimal integer
            int start = i + 2;
            if (start >= s.Length)
                return false;
            for (int j = start; j < s.Length; ++j)
                if (!char.IsAsciiHexDigit(s[j]))
                    return false;
            BigInteger b = BigInteger.Parse("0" + s[start..], NumberStyles.AllowHexSpecifier,
                                            CultureInfo.InvariantCulture);
            v = (Float)b;
            if (negative)
                v = -v;
            return true;
        }

        int mantissaStart = i;
        int nDigits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            ++i;
            ++nDigits;
        }
        if (i < s.Length && s[i] == '.')
        {
            ++i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                ++i;
                ++nDigits;
            }
        }
        if (nDigits == 0)
            return false;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            ++i;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                ++i;
            int expDigits = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                ++i;
                ++expDigits;
            }
            if (expDigits == 0)
                return false;
        }
        if (i != s.Length)
            return false;
        v = Float.Parse(s.AsSpan(mantissaStart), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (negative)
            v = -v;
        return true;
    }

    private static double parseFloat(Token t)
    {
        // Fast path for a single digit
        if (t.token.Length == 1)
        {
            if (!(t.token[0] >= '0' && t.token[0] <= '9'))
                ErrorExit(t.loc, $"\"{t.token[0]}\": expected a number");
            return t.token[0] - '0';
        }

        // Can we just use strtol?
        static bool isInteger(string str)
        {
            foreach (char ch in str)
                if (!(ch >= '0' && ch <= '9'))
                    return false;
            return true;
        }

        double val = 0;
        bool ok;
        if (isInteger(t.token))
        {
            // (strtol() saturates.)
            val = long.TryParse(t.token, NumberStyles.None, CultureInfo.InvariantCulture, out long l)
                      ? l
                      : long.MaxValue;
            ok = true;
        }
        else
        {
            ok = StringToFloat(t.token, out Float f);
            val = f;
        }

        if (!ok)
            ErrorExit(t.loc, $"{t.token}: expected a number");

        return val;
    }

    private static bool isQuotedString(string str) => str.Length >= 2 && str[0] == '"' && str[^1] == '"';

    private static string dequoteString(Token t)
    {
        if (!isQuotedString(t.token))
            ErrorExit(t.loc, $"\"{t.token}\": expected quoted string");

        return t.token[1..^1];
    }

    private const int TokenOptional = 0;
    private const int TokenRequired = 1;

    private static List<ParsedParameter> parseParameters(Func<int, Token?> nextToken, Action<Token> ungetToken,
                                                         bool formatting, Action<Token, string> errorCallback)
    {
        List<ParsedParameter> parameterVector = [];

        while (true)
        {
            Token? t = nextToken(TokenOptional);
            if (!t.HasValue)
                return parameterVector;

            if (!isQuotedString(t.Value.token))
            {
                ungetToken(t.Value);
                return parameterVector;
            }

            ParsedParameter param = new(t.Value.loc);

            string decl = dequoteString(t.Value);

            int skipSpace(int iter)
            {
                while (iter != decl.Length && (decl[iter] == ' ' || decl[iter] == '\t'))
                    ++iter;
                return iter;
            }
            // Skip to the next whitespace character (or the end of the string).
            int skipToSpace(int iter)
            {
                while (iter != decl.Length && decl[iter] != ' ' && decl[iter] != '\t')
                    ++iter;
                return iter;
            }

            int typeBegin = skipSpace(0);
            if (typeBegin == decl.Length)
                ErrorExit(t.Value.loc, $"Parameter \"{decl}\" doesn't have a type declaration?!");

            // Find end of type declaration
            int typeEnd = skipToSpace(typeBegin);
            param.type = decl[typeBegin..typeEnd];

            if (formatting)
            { // close enough: upgrade...
                if (param.type == "point")
                    param.type = "point3";
                if (param.type == "vector")
                    param.type = "vector3";
                if (param.type == "color")
                    param.type = "rgb";
            }

            int nameBegin = skipSpace(typeEnd);
            if (nameBegin == decl.Length)
                ErrorExit(t.Value.loc, $"Unable to find parameter name from \"{decl}\"");

            int nameEnd = skipToSpace(nameBegin);
            param.name = decl[nameBegin..nameEnd];

            ValType valType = ValType.Unknown;

            if (param.type == "integer")
                valType = ValType.Int;

            // (pbrt's switch statements fall through after calling the error
            // callback, which exits, so only the first error matters.)
            void addVal(Token t)
            {
                if (isQuotedString(t.token))
                {
                    switch (valType)
                    {
                        case ValType.Unknown:
                            valType = ValType.String;
                            break;
                        case ValType.String:
                            break;
                        case ValType.Float:
                            errorCallback(t, "expected floating-point value");
                            break;
                        case ValType.Int:
                            errorCallback(t, "expected integer value");
                            break;
                        case ValType.Bool:
                            errorCallback(t, "expected Boolean value");
                            break;
                    }

                    param.AddString(dequoteString(t));
                }
                else if (t.token[0] == 't' && t.token == "true")
                {
                    switch (valType)
                    {
                        case ValType.Unknown:
                            valType = ValType.Bool;
                            break;
                        case ValType.String:
                            errorCallback(t, "expected string value");
                            break;
                        case ValType.Float:
                            errorCallback(t, "expected floating-point value");
                            break;
                        case ValType.Int:
                            errorCallback(t, "expected integer value");
                            break;
                        case ValType.Bool:
                            break;
                    }

                    param.AddBool(true);
                }
                else if (t.token[0] == 'f' && t.token == "false")
                {
                    switch (valType)
                    {
                        case ValType.Unknown:
                            valType = ValType.Bool;
                            break;
                        case ValType.String:
                            errorCallback(t, "expected string value");
                            break;
                        case ValType.Float:
                            errorCallback(t, "expected floating-point value");
                            break;
                        case ValType.Int:
                            errorCallback(t, "expected integer value");
                            break;
                        case ValType.Bool:
                            break;
                    }

                    param.AddBool(false);
                }
                else
                {
                    switch (valType)
                    {
                        case ValType.Unknown:
                            valType = ValType.Float;
                            break;
                        case ValType.String:
                            errorCallback(t, "expected string value");
                            break;
                        case ValType.Float:
                            break;
                        case ValType.Int:
                            break;
                        case ValType.Bool:
                            errorCallback(t, "expected Boolean value");
                            break;
                    }

                    if (valType == ValType.Int)
                        param.AddInt(parseInt(t));
                    else
                        param.AddFloat((Float)parseFloat(t));
                }
            }

            Token val = nextToken(TokenRequired)!.Value;

            if (val.token == "[")
            {
                while (true)
                {
                    val = nextToken(TokenRequired)!.Value;
                    if (val.token == "]")
                        break;
                    addVal(val);
                }
            }
            else
                addVal(val);

            if (formatting && param.type == "bool")
            {
                foreach (string b in param.strings)
                {
                    if (b == "true")
                        param.bools.Add(true);
                    else if (b == "false")
                        param.bools.Add(false);
                    else
                        Error(param.loc, $"{b}: neither \"true\" nor \"false\" in bool " + "parameter list.");
                }
                param.strings.Clear();
            }

            parameterVector.Add(param);
        }
    }

    private enum ValType { Unknown, String, Bool, Float, Int }

    private static int warnedTransformBeginEndDeprecated = 0;

    private static void parse(ParserTarget target, Tokenizer t)
    {
        FormattingParserTarget? formattingTarget = target as FormattingParserTarget;
        bool formatting = formattingTarget != null;

        List<(AsyncJob<int> job, BasicSceneBuilder builder)> imports = [];

        LOG_VERBOSE($"Started parsing {t.loc.filename}");
        List<Tokenizer> fileStack = [t];

        Token? ungetToken = null;

        void parseError(string msg, FileLoc? loc) => ErrorExit(loc, msg);

        // nextToken is a little helper function that handles the file stack,
        // returning the next token from the current file until reaching EOF,
        // at which point it switches to the next file (if any).
        Token? nextToken(int flags)
        {
            while (true)
            {
                if (ungetToken.HasValue)
                {
                    Token u = ungetToken.Value;
                    ungetToken = null;
                    return u;
                }

                if (fileStack.Count == 0)
                {
                    if ((flags & TokenRequired) != 0)
                        ErrorExit("premature end of file");
                    return null;
                }

                Token? tok = fileStack[^1].Next();

                if (!tok.HasValue)
                {
                    // We've reached EOF in the current file. Anything more to parse?
                    LOG_VERBOSE($"Finished parsing {fileStack[^1].loc.filename}");
                    fileStack.RemoveAt(fileStack.Count - 1);
                }
                else if (tok.Value.token[0] == '#')
                {
                    // Swallow comments, unless --format or --toply was given, in
                    // which case they're printed to stdout.
                    if (formatting)
                        formattingTarget!.output.Write($"{formattingTarget.indent()}{tok.Value.token}\n");
                }
                else
                    // Regular token; success.
                    return tok;
            }
        }

        void unget(Token t)
        {
            CHECK(!ungetToken.HasValue);
            ungetToken = t;
        }

        void paramError(Token t, string msg) => parseError($"{t.token}: {msg}", t.loc);

        // Helper function for pbrt API entrypoints that take a single string
        // parameter and a ParameterVector (e.g. pbrtShape()).
        void basicParamListEntrypoint(Action<string, List<ParsedParameter>, FileLoc> apiFunc, FileLoc loc)
        {
            Token t = nextToken(TokenRequired)!.Value;
            string n = dequoteString(t);
            List<ParsedParameter> parameterVector = parseParameters(nextToken, unget, formatting, paramError);
            apiFunc(n, parameterVector, loc);
        }

        void syntaxError(Token t) => ErrorExit(t.loc, $"Unknown directive: {t.token}");

        Float[] readFloats(int n)
        {
            Float[] v = new Float[n];
            for (int i = 0; i < n; ++i)
                v[i] = (Float)parseFloat(nextToken(TokenRequired)!.Value);
            return v;
        }

        while (true)
        {
            Token? optTok = nextToken(TokenOptional);
            if (!optTok.HasValue)
                break;
            Token tok = optTok.Value;

            switch (tok.token[0])
            {
                case 'A':
                    if (tok.token == "AttributeBegin")
                        target.AttributeBegin(tok.loc);
                    else if (tok.token == "AttributeEnd")
                        target.AttributeEnd(tok.loc);
                    else if (tok.token == "Attribute")
                        basicParamListEntrypoint(target.Attribute, tok.loc);
                    else if (tok.token == "ActiveTransform")
                    {
                        Token a = nextToken(TokenRequired)!.Value;
                        if (a.token == "All")
                            target.ActiveTransformAll(tok.loc);
                        else if (a.token == "EndTime")
                            target.ActiveTransformEndTime(tok.loc);
                        else if (a.token == "StartTime")
                            target.ActiveTransformStartTime(tok.loc);
                        else
                            syntaxError(tok);
                    }
                    else if (tok.token == "AreaLightSource")
                        basicParamListEntrypoint(target.AreaLightSource, tok.loc);
                    else if (tok.token == "Accelerator")
                        basicParamListEntrypoint(target.Accelerator, tok.loc);
                    else
                        syntaxError(tok);
                    break;

                case 'C':
                    if (tok.token == "ConcatTransform")
                    {
                        if (nextToken(TokenRequired)!.Value.token != "[")
                            syntaxError(tok);
                        Float[] m = readFloats(16);
                        if (nextToken(TokenRequired)!.Value.token != "]")
                            syntaxError(tok);
                        target.ConcatTransform(m, tok.loc);
                    }
                    else if (tok.token == "CoordinateSystem")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.CoordinateSystem(n, tok.loc);
                    }
                    else if (tok.token == "CoordSysTransform")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.CoordSysTransform(n, tok.loc);
                    }
                    else if (tok.token == "ColorSpace")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.ColorSpace(n, tok.loc);
                    }
                    else if (tok.token == "Camera")
                        basicParamListEntrypoint(target.Camera, tok.loc);
                    else
                        syntaxError(tok);
                    break;

                case 'F':
                    if (tok.token == "Film")
                        basicParamListEntrypoint(target.Film, tok.loc);
                    else
                        syntaxError(tok);
                    break;

                case 'I':
                    if (tok.token == "Integrator")
                        basicParamListEntrypoint(target.Integrator, tok.loc);
                    else if (tok.token == "Include")
                    {
                        Token filenameToken = nextToken(TokenRequired)!.Value;
                        string filename = dequoteString(filenameToken);
                        if (formatting)
                            formattingTarget!.output.Write($"{formattingTarget.indent()}Include \"{filename}\"\n");
                        else
                        {
                            filename = ResolveFilename(filename);
                            Tokenizer? tinc = Tokenizer.CreateFromFile(filename, parseError);
                            if (tinc != null)
                            {
                                LOG_VERBOSE($"Started parsing {tinc.loc.filename}");
                                fileStack.Add(tinc);
                            }
                        }
                    }
                    else if (tok.token == "Import")
                    {
                        Token filenameToken = nextToken(TokenRequired)!.Value;
                        string filename = dequoteString(filenameToken);
                        if (formatting)
                            formattingTarget!.output.Write($"{formattingTarget.indent()}Import \"{filename}\"\n");
                        else
                        {
                            BasicSceneBuilder? builder = target as BasicSceneBuilder;
                            CHECK(builder != null);

                            if (builder!.currentBlock != BasicSceneBuilder.BlockState.WorldBlock)
                                ErrorExit(tok.loc, "Import statement only allowed inside world " +
                                                   "definition block.");

                            filename = ResolveFilename(filename);
                            BasicSceneBuilder importBuilder = builder.CopyForImport();

                            if (RunningThreads() == 1)
                            {
                                Tokenizer? timport = Tokenizer.CreateFromFile(filename, parseError);
                                if (timport != null)
                                    parse(importBuilder, timport);
                                builder.MergeImported(importBuilder);
                            }
                            else
                            {
                                string fn = filename;
                                AsyncJob<int> jobFinished = RunAsync(() =>
                                {
                                    Timer timer = new();
                                    Tokenizer? timport = Tokenizer.CreateFromFile(fn, parseError);
                                    if (timport != null)
                                        parse(importBuilder, timport);
                                    LOG_VERBOSE($"Elapsed time to parse \"{fn}\": {timer.ElapsedSeconds():F2}s");
                                    return 0;
                                });
                                imports.Add((jobFinished, importBuilder));
                            }
                        }
                    }
                    else if (tok.token == "Identity")
                        target.Identity(tok.loc);
                    else
                        syntaxError(tok);
                    break;

                case 'L':
                    if (tok.token == "LightSource")
                        basicParamListEntrypoint(target.LightSource, tok.loc);
                    else if (tok.token == "LookAt")
                    {
                        Float[] v = readFloats(9);
                        target.LookAt(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'M':
                    if (tok.token == "MakeNamedMaterial")
                        basicParamListEntrypoint(target.MakeNamedMaterial, tok.loc);
                    else if (tok.token == "MakeNamedMedium")
                        basicParamListEntrypoint(target.MakeNamedMedium, tok.loc);
                    else if (tok.token == "Material")
                        basicParamListEntrypoint(target.Material, tok.loc);
                    else if (tok.token == "MediumInterface")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        string name0 = n, name1;

                        // Check for optional second parameter
                        Token? second = nextToken(TokenOptional);
                        if (second.HasValue)
                        {
                            if (isQuotedString(second.Value.token))
                                name1 = dequoteString(second.Value);
                            else
                            {
                                unget(second.Value);
                                name1 = name0;
                            }
                        }
                        else
                            name1 = name0;

                        target.MediumInterface(name0, name1, tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'N':
                    if (tok.token == "NamedMaterial")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.NamedMaterial(n, tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'O':
                    if (tok.token == "ObjectBegin")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.ObjectBegin(n, tok.loc);
                    }
                    else if (tok.token == "ObjectEnd")
                        target.ObjectEnd(tok.loc);
                    else if (tok.token == "ObjectInstance")
                    {
                        string n = dequoteString(nextToken(TokenRequired)!.Value);
                        target.ObjectInstance(n, tok.loc);
                    }
                    else if (tok.token == "Option")
                    {
                        string name = dequoteString(nextToken(TokenRequired)!.Value);
                        string value = nextToken(TokenRequired)!.Value.token;
                        target.Option(name, value, tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'P':
                    if (tok.token == "PixelFilter")
                        basicParamListEntrypoint(target.PixelFilter, tok.loc);
                    else
                        syntaxError(tok);
                    break;

                case 'R':
                    if (tok.token == "ReverseOrientation")
                        target.ReverseOrientation(tok.loc);
                    else if (tok.token == "Rotate")
                    {
                        Float[] v = readFloats(4);
                        target.Rotate(v[0], v[1], v[2], v[3], tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'S':
                    if (tok.token == "Shape")
                        basicParamListEntrypoint(target.Shape, tok.loc);
                    else if (tok.token == "Sampler")
                        basicParamListEntrypoint(target.Sampler, tok.loc);
                    else if (tok.token == "Scale")
                    {
                        Float[] v = readFloats(3);
                        target.Scale(v[0], v[1], v[2], tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'T':
                    if (tok.token == "TransformBegin")
                    {
                        if (formattingTarget != null)
                            formattingTarget.TransformBegin(tok.loc);
                        else
                        {
                            if (Interlocked.Exchange(ref warnedTransformBeginEndDeprecated, 1) == 0)
                                Warning(tok.loc, "TransformBegin/End are deprecated and should " +
                                                 "be replaced with AttributeBegin/End");
                            target.AttributeBegin(tok.loc);
                        }
                    }
                    else if (tok.token == "TransformEnd")
                    {
                        if (formattingTarget != null)
                            formattingTarget.TransformEnd(tok.loc);
                        else
                            target.AttributeEnd(tok.loc);
                    }
                    else if (tok.token == "Transform")
                    {
                        if (nextToken(TokenRequired)!.Value.token != "[")
                            syntaxError(tok);
                        Float[] m = readFloats(16);
                        if (nextToken(TokenRequired)!.Value.token != "]")
                            syntaxError(tok);
                        target.Transform(m, tok.loc);
                    }
                    else if (tok.token == "Translate")
                    {
                        Float[] v = readFloats(3);
                        target.Translate(v[0], v[1], v[2], tok.loc);
                    }
                    else if (tok.token == "TransformTimes")
                    {
                        Float[] v = readFloats(2);
                        target.TransformTimes(v[0], v[1], tok.loc);
                    }
                    else if (tok.token == "Texture")
                    {
                        string name = dequoteString(nextToken(TokenRequired)!.Value);
                        string type = dequoteString(nextToken(TokenRequired)!.Value);

                        Token t2 = nextToken(TokenRequired)!.Value;
                        string texName = dequoteString(t2);
                        List<ParsedParameter> parameters = parseParameters(nextToken, unget, formatting, paramError);

                        target.Texture(name, type, texName, parameters, tok.loc);
                    }
                    else
                        syntaxError(tok);
                    break;

                case 'W':
                    if (tok.token == "WorldBegin")
                        target.WorldBegin(tok.loc);
                    else if (tok.token == "WorldEnd" && formatting)
                    {
                        // just swallow it
                    }
                    else
                        syntaxError(tok);
                    break;

                default:
                    syntaxError(tok);
                    break;
            }
        }

        foreach ((AsyncJob<int> job, BasicSceneBuilder importBuilder) in imports)
        {
            job.Wait();

            BasicSceneBuilder? builder = target as BasicSceneBuilder;
            CHECK(builder != null);
            builder!.MergeImported(importBuilder);
        }
    }

    // Scene Parsing Declarations
    public static void ParseFiles(ParserTarget target, IReadOnlyList<string> filenames)
    {
        static void tokError(string msg, FileLoc? loc) => ErrorExit(loc, msg);

        // Process scene description
        if (filenames.Count == 0)
        {
            // Parse scene from standard input
            Tokenizer? t = Tokenizer.CreateFromFile("-", tokError);
            if (t != null)
                parse(target, t);
        }
        else
        {
            // Parse scene from input files
            foreach (string fn in filenames)
            {
                if (fn != "-")
                    SetSearchDirectory(fn);

                Tokenizer? t = Tokenizer.CreateFromFile(fn, tokError);
                if (t != null)
                    parse(target, t);
            }
        }

        target.EndOfFiles();
    }

    public static void ParseString(ParserTarget target, string str)
    {
        static void tokError(string msg, FileLoc? loc) => ErrorExit(loc, msg);
        Tokenizer t = Tokenizer.CreateFromString(str, tokError);
        parse(target, t);

        target.EndOfFiles();
    }
}

// FormattingParserTarget Definition
public sealed class FormattingParserTarget(bool toPly, bool upgrade, TextWriter? output = null) : ParserTarget
{
    internal readonly TextWriter output = output ?? Console.Out;
    private int catIndentCount = 0;
    private readonly bool toPly = toPly, upgrade = upgrade;
    private readonly SortedDictionary<string, string> definedTextures = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> definedNamedMaterials = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, ParameterDictionary> namedMaterialDictionaries =
        new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> definedObjectInstances = new(StringComparer.Ordinal);

    // (Function-level statics in pbrt.)
    private static int textureRenameCount = 0, namedMaterialRenameCount = 0, objectRenameCount = 0;
    private static int plyCount = 1;

    public string indent(int extra = 0) => new(' ', catIndentCount + 4 * extra);

    private static string F(Float v) => FloatToString(v);

    private void Printf(string s) => output.Write(s);

    // FormattingParserTarget Method Definitions
    public override void Option(string name, string value, FileLoc loc)
    {
        string nName = normalizeArg(name);
        Printf($"{indent()}Option \"{name}\" {value}\n");
    }

    public override void Identity(FileLoc loc) => Printf($"{indent()}Identity\n");

    public override void Translate(Float dx, Float dy, Float dz, FileLoc loc) =>
        Printf($"{indent()}Translate {F(dx)} {F(dy)} {F(dz)}\n");

    public override void Rotate(Float angle, Float ax, Float ay, Float az, FileLoc loc) =>
        Printf($"{indent()}Rotate {F(angle)} {F(ax)} {F(ay)} {F(az)}\n");

    public override void Scale(Float sx, Float sy, Float sz, FileLoc loc) =>
        Printf($"{indent()}Scale {F(sx)} {F(sy)} {F(sz)}\n");

    public override void LookAt(Float ex, Float ey, Float ez, Float lx, Float ly, Float lz, Float ux, Float uy,
                                Float uz, FileLoc loc) =>
        Printf($"{indent()}LookAt {F(ex)} {F(ey)} {F(ez)}\n{indent()}    {F(lx)} {F(ly)} {F(lz)}\n" +
               $"{indent()}    {F(ux)} {F(uy)} {F(uz)}\n");

    public override void ConcatTransform(Float[] transform, FileLoc loc)
    {
        Printf($"{indent()}ConcatTransform [ ");
        for (int i = 0; i < 16; ++i)
            Printf($"{F(transform[i])} ");
        Printf(" ]\n");
    }

    public override void Transform(Float[] transform, FileLoc loc)
    {
        Printf($"{indent()}Transform [ ");
        for (int i = 0; i < 16; ++i)
            Printf($"{F(transform[i])} ");
        Printf(" ]\n");
    }

    public override void CoordinateSystem(string name, FileLoc loc) =>
        Printf($"{indent()}CoordinateSystem \"{name}\"\n");

    public override void CoordSysTransform(string name, FileLoc loc) =>
        Printf($"{indent()}CoordSysTransform \"{name}\"\n");

    public override void ActiveTransformAll(FileLoc loc) => Printf($"{indent()}ActiveTransform All\n");

    public override void ActiveTransformEndTime(FileLoc loc) => Printf($"{indent()}ActiveTransform EndTime\n");

    public override void ActiveTransformStartTime(FileLoc loc) =>
        Printf($"{indent()}ActiveTransform StartTime\n");

    public override void TransformTimes(Float start, Float end, FileLoc loc) =>
        Printf($"{indent()}TransformTimes {F(start)} {F(end)}\n");

    public override void ColorSpace(string n, FileLoc loc) => Printf($"{indent()}ColorSpace \"{n}\"\n");

    public override void PixelFilter(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        string extra = "";
        if (upgrade)
        {
            List<Float> xr = dict.GetFloatArray("xwidth");
            if (xr.Count == 1)
            {
                dict.RemoveFloat("xwidth");
                extra += $"{indent(1)}\"float xradius\" [ {F(xr[0])} ]\n";
            }
            List<Float> yr = dict.GetFloatArray("ywidth");
            if (yr.Count == 1)
            {
                dict.RemoveFloat("ywidth");
                extra += $"{indent(1)}\"float yradius\" [ {F(yr[0])} ]\n";
            }

            if (name == "gaussian")
            {
                List<Float> alpha = dict.GetFloatArray("alpha");
                if (alpha.Count == 1)
                {
                    dict.RemoveFloat("alpha");
                    extra += $"{indent(1)}\"float sigma\" [ {F(1 / FMath.Sqrt(2 * alpha[0]))} ]\n";
                }
            }
        }

        Printf($"{indent()}PixelFilter \"{name}\"\n");
        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    public override void Film(string type, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        string extra = "";
        if (upgrade)
        {
            List<Float> m = dict.GetFloatArray("maxsampleluminance");
            if (m.Count > 0)
            {
                dict.RemoveFloat("maxsampleluminance");
                extra += $"{indent(1)}\"float maxcomponentvalue\" [ {F(m[0])} ]\n";
            }
            List<Float> s = dict.GetFloatArray("scale");
            if (s.Count > 0)
            {
                dict.RemoveFloat("scale");
                extra += $"{indent(1)}\"float iso\" [ {F(100 * s[0])} ]\n";
            }
        }

        if (upgrade && type == "image")
            Printf($"{indent()}Film \"rgb\"\n");
        else
            Printf($"{indent()}Film \"{type}\"\n");
        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    public override void Sampler(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        if (upgrade)
        {
            if (name == "lowdiscrepancy" || name == "02sequence")
                Printf($"{indent()}Sampler \"paddedsobol\"\n");
            else if (name == "maxmindist")
                Printf($"{indent()}Sampler \"pmj02bn\"\n");
            else if (name == "random")
                Printf($"{indent()}Sampler \"independent\"\n");
            else
                Printf($"{indent()}Sampler \"{name}\"\n");
        }
        else
            Printf($"{indent()}Sampler \"{name}\"\n");
        Printf(dict.ToParameterList(catIndentCount));
    }

    public override void Accelerator(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        Printf($"{indent()}Accelerator \"{name}\"\n{dict.ToParameterList(catIndentCount)}");
    }

    public override void Integrator(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        string extra = "";
        if (upgrade)
        {
            dict.RemoveFloat("rrthreshold");

            if (name == "sppm")
            {
                dict.RemoveInt("imagewritefrequency");

                List<int> iterations = dict.GetIntArray("numiterations");
                if (iterations.Count > 0)
                {
                    dict.RemoveInt("numiterations");
                    Warning(loc, "The SPPM integrator no longer takes a \"numiterations\" parameter. " +
                                 "This value is now set via the Sampler's number of pixel samples.");
                }
            }
            string lss = dict.GetOneString("lightsamplestrategy", "");
            if (lss == "spatial")
            {
                dict.RemoveString("lightsamplestrategy");
                extra += indent(1) + "\"string lightsamplestrategy\" \"bvh\"\n";
            }
        }

        if (upgrade && name == "directlighting")
        {
            Printf($"{indent()}Integrator \"path\"\n");
            extra += indent(1) + "\"integer maxdepth\" [ 1 ]\n";
        }
        else
            Printf($"{indent()}Integrator \"{name}\"\n");
        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    public override void Camera(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        if (upgrade && name == "environment")
            Printf($"{indent()}Camera \"spherical\" \"string mapping\" \"equirectangular\"\n");
        else
            Printf($"{indent()}Camera \"{name}\"\n");
        if (upgrade && name == "realistic")
            dict.RemoveBool("simpleweighting");

        Printf(dict.ToParameterList(catIndentCount));
    }

    public override void MakeNamedMedium(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);
        if (upgrade && name == "heterogeneous")
            Printf($"{indent()}MakeNamedMedium \"uniformgrid\"\n{dict.ToParameterList(catIndentCount)}\n");
        else
            Printf($"{indent()}MakeNamedMedium \"{name}\"\n{dict.ToParameterList(catIndentCount)}\n");
    }

    public override void MediumInterface(string insideName, string outsideName, FileLoc loc) =>
        Printf($"{indent()}MediumInterface \"{insideName}\" \"{outsideName}\"\n");

    public override void WorldBegin(FileLoc loc) => Printf("\n\nWorldBegin\n\n");

    public override void AttributeBegin(FileLoc loc)
    {
        Printf($"\n{indent()}AttributeBegin\n");
        catIndentCount += 4;
    }

    public override void AttributeEnd(FileLoc loc)
    {
        catIndentCount -= 4;
        Printf($"{indent()}AttributeEnd\n");
    }

    public override void Attribute(string target, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);
        Printf($"{indent()}Attribute \"{target}\" ");
        if (parameters.Count == 1)
            // just one; put it on the same line
            Printf(dict.ToParameterList(0) + '\n');
        else
            Printf('\n' + dict.ToParameterList(catIndentCount));
    }

    public void TransformBegin(FileLoc loc)
    {
        Warning(loc, "Rewriting \"TransformBegin\" to \"AttributeBegin\".");
        Printf($"{indent()}AttributeBegin\n");
        catIndentCount += 4;
    }

    public void TransformEnd(FileLoc loc)
    {
        catIndentCount -= 4;
        Warning(loc, "Rewriting \"TransformEnd\" to \"AttributeEnd\".");
        Printf($"{indent()}AttributeEnd\n");
    }

    public override void Texture(string name, string type, string texname, List<ParsedParameter> parameters,
                                 FileLoc loc)
    {
        if (upgrade)
        {
            if (definedTextures.ContainsKey(name))
            {
                Warning(loc, $"{name}: renaming multiply-defined texture");
                definedTextures[name] = $"{name}-renamed-{textureRenameCount++}";
            }
            else
                definedTextures[name] = name;
        }

        if (upgrade && texname == "scale")
        {
            // This is easier to do in the raw ParsedParameterVector...
            if (type == "float")
            {
                foreach (ParsedParameter p in parameters)
                {
                    if (p.name == "tex1")
                        p.name = "tex";
                    if (p.name == "tex2")
                        p.name = "scale";
                }
            }
            else
            {
                // more subtle: rename one of them as float, but need one of them
                // to be an RGB and spectrally constant...
                bool foundRGB = false, foundTexture = false;
                foreach (ParsedParameter p in parameters)
                {
                    if (p.name != "tex1" && p.name != "tex2")
                        continue;

                    if (p.type == "rgb")
                    {
                        if (foundRGB)
                        {
                            ErrorExitDeferred(p.loc, "Two \"rgb\" textures found for \"scale\" " +
                                                     $"texture \"{name}\". Please manually edit the file to " +
                                                     "upgrade.");
                            return;
                        }
                        if (p.floats.Count != 3)
                        {
                            ErrorExitDeferred(p.loc, $"Didn't find 3 values for \"rgb\" \"{p.name}\".");
                            return;
                        }
                        if (p.floats[0] != p.floats[1] || p.floats[1] != p.floats[2])
                        {
                            ErrorExitDeferred(p.loc, "Non-constant \"rgb\" value found for " +
                                                     $"\"scale\" texture parameter \"{p.name}\". Please " +
                                                     "manually " + "edit the file to upgrade.");
                            return;
                        }

                        foundRGB = true;
                        p.type = "float";
                        p.name = "scale";
                        p.floats.RemoveRange(1, p.floats.Count - 1);
                    }
                    else
                    {
                        if (foundTexture)
                        {
                            ErrorExitDeferred(p.loc, "Two textures found for \"scale\" " +
                                                     $"texture \"{name}\". Please manually edit the file to " +
                                                     "upgrade.");
                            return;
                        }
                        p.name = "tex";
                        foundTexture = true;
                    }
                }
            }
        }

        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);
        if (upgrade)
            dict.RenameUsedTextures(definedTextures);

        string extra = "";
        if (upgrade)
        {
            if (texname == "imagemap")
            {
                List<bool> tri = dict.GetBoolArray("trilinear");
                if (tri.Count == 1)
                {
                    dict.RemoveBool("trilinear");
                    extra += indent(1) + "\"string filter\" ";
                    extra += tri[0] ? "\"trilinear\"\n" : "\"bilinear\"\n";
                }
            }

            if (texname == "imagemap" || texname == "ptex")
            {
                Float gamma = dict.GetOneFloat("gamma", 0);
                if (gamma != 0)
                {
                    dict.RemoveFloat("gamma");
                    extra += indent(1) + $"\"string encoding \" \"gamma {F(gamma)}\" ";
                }
                else
                {
                    List<bool> gammaBool = dict.GetBoolArray("gamma");
                    if (gammaBool.Count == 1)
                    {
                        dict.RemoveBool("gamma");
                        extra += indent(1) + "\"string encoding\" ";
                        extra += gammaBool[0] ? "\"sRGB\"\n" : "\"linear\"\n";
                    }
                }
            }
        }

        if (upgrade)
        {
            if (type == "color")
                Printf($"{indent()}Texture \"{definedTextures[name]}\" \"spectrum\" \"{texname}\"\n");
            else
                Printf($"{indent()}Texture \"{definedTextures[name]}\" \"{type}\" \"{texname}\"\n");
        }
        else
            Printf($"{indent()}Texture \"{name}\" \"{type}\" \"{texname}\"\n");

        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    private string upgradeMaterialIndex(string name, ParameterDictionary dict, FileLoc loc)
    {
        if (name != "glass" && name != "uber")
            return "";

        string tex = dict.GetTexture("index");
        if (tex.Length > 0)
        {
            if (dict.GetTexture("eta").Length > 0)
            {
                ErrorExitDeferred(loc, $"Material \"{name}\" has both \"index\" and \"eta\" parameters.");
                return "";
            }

            dict.RemoveTexture("index");
            return indent(1) + $"\"texture eta\" \"{tex}\"\n";
        }
        else
        {
            List<Float> index = dict.GetFloatArray("index");
            if (index.Count == 0)
                return "";
            if (index.Count != 1)
            {
                ErrorExitDeferred(loc, "Multiple values provided for \"index\" parameter.");
                return "";
            }
            if (dict.GetFloatArray("eta").Count > 0)
            {
                ErrorExitDeferred(loc, $"Material \"{name}\" has both \"index\" and \"eta\" parameters.");
                return "";
            }

            Float value = index[0];
            dict.RemoveFloat("index");
            return indent(1) + $"\"float eta\" [ {F(value)} ]\n";
        }
    }

    private string upgradeMaterial(ref string name, ParameterDictionary dict, FileLoc loc)
    {
        string extra = upgradeMaterialIndex(name, dict, loc);

        dict.RenameParameter("bumpmap", "displacement");

        string matName = name;
        bool removeParamSilentIfConstant(string paramName, Float value)
        {
            RGB? rgb = dict.GetOneRGB(paramName);
            bool matches = rgb.HasValue && rgb.Value.r == value && rgb.Value.g == value && rgb.Value.b == value;

            if (!matches && dict.GetSpectrumArray(paramName, SpectrumType.Unbounded).Count > 0)
                Warning(loc, "Parameter is being removed when converting " +
                             $"to \"{matName}\" material: {dict.ToParameterDefinition(paramName)}");
            dict.RemoveSpectrum(paramName);
            dict.RemoveTexture(paramName);
            return matches;
        }

        if (name == "uber")
        {
            name = matName = "coateddiffuse";
            if (removeParamSilentIfConstant("Ks", 0))
            {
                name = matName = "diffuse";
                dict.RemoveFloat("eta");
                dict.RemoveFloat("roughness");
            }
            removeParamSilentIfConstant("Kr", 0);
            removeParamSilentIfConstant("Kt", 0);
            dict.RenameParameter("Kd", "reflectance");

            if (dict.GetTexture("opacity").Length > 0)
            {
                ErrorExitDeferred(loc, "Non-opaque \"opacity\" texture in \"uber\" " +
                                       "material not supported " + "in pbrt-v4. Please edit the file manually.");
                return "";
            }

            if (dict.GetSpectrumArray("opacity", SpectrumType.Unbounded).Count == 0)
                return "";

            RGB? opacity = dict.GetOneRGB("opacity");
            if (opacity.HasValue && opacity.Value.r == 1 && opacity.Value.g == 1 && opacity.Value.b == 1)
            {
                dict.RemoveSpectrum("opacity");
                return "";
            }

            ErrorExitDeferred(loc, "A non-opaque \"opacity\" in the \"uber\" " + "material is not supported " +
                                   "in pbrt-v4. Please edit the file manually.");
        }
        else if (name == "mix")
        {
            // Convert the amount to a scalar
            RGB? rgb = dict.GetOneRGB("amount");
            if (rgb.HasValue)
            {
                RGB c = rgb.Value;
                if (c.r == c.g && c.g == c.b)
                    extra += indent(1) + $"\"float amount\" [ {F(c.r)} ]\n";
                else
                {
                    Float avg = (c.r + c.g + c.b) / 3;
                    Warning(loc, $"Changing RGB \"amount\" ({F(c.r)}, {F(c.g)}, {F(c.b)}) to scalar average {F(avg)}");
                    extra += indent(1) + $"\"float amount\" [ {F(avg)} ]\n";
                }
            }
            else if (dict.GetSpectrumArray("amount", SpectrumType.Unbounded).Count > 0)
                ErrorExitDeferred(loc, "Unable to update non-RGB spectrum \"amount\" to a scalar: " +
                                       dict.ToParameterDefinition("amount"));

            dict.RemoveSpectrum("amount");

            // And rename...
            string m1 = dict.GetOneString("namedmaterial1", "");
            if (m1.Length == 0)
                ErrorExitDeferred(loc, "Didn't find \"namedmaterial1\" parameter for \"mix\" material.");
            dict.RemoveString("namedmaterial1");

            string m2 = dict.GetOneString("namedmaterial2", "");
            if (m2.Length == 0)
                ErrorExitDeferred(loc, "Didn't find \"namedmaterial2\" parameter for \"mix\" material.");
            dict.RemoveString("namedmaterial2");

            // Note: swapped order vs pbrt-v3!
            extra += indent(1) + $"\"string materials\" [ \"{m2}\" \"{m1}\" ]\n";
        }
        else if (name == "substrate")
        {
            name = matName = "coateddiffuse";
            removeParamSilentIfConstant("Ks", 1);
            dict.RenameParameter("Kd", "reflectance");
        }
        else if (name == "glass")
        {
            name = matName = "dielectric";
            removeParamSilentIfConstant("Kr", 1);
            removeParamSilentIfConstant("Kt", 1);
        }
        else if (name == "plastic")
        {
            name = matName = "coateddiffuse";
            if (removeParamSilentIfConstant("Ks", 0))
            {
                name = matName = "diffuse";
                dict.RemoveFloat("roughness");
                dict.RemoveFloat("eta");
            }
            dict.RenameParameter("Kd", "reflectance");
        }
        else if (name == "fourier")
            Warning(loc, "\"fourier\" material is no longer supported. (But there " + "is \"measured\"!)");
        else if (name == "kdsubsurface")
        {
            name = "subsurface";
            dict.RenameParameter("Kd", "reflectance");
        }
        else if (name == "matte")
        {
            name = "diffuse";
            dict.RenameParameter("Kd", "reflectance");
            dict.RemoveFloat("sigma");
            dict.RemoveTexture("sigma");
        }
        else if (name == "metal")
        {
            name = matName = "conductor";
            removeParamSilentIfConstant("Kr", 1);
        }
        else if (name == "translucent")
        {
            name = matName = "diffusetransmission";

            dict.RenameParameter("Kd", "transmittance");

            removeParamSilentIfConstant("reflect", 0);
            removeParamSilentIfConstant("transmit", 1);

            removeParamSilentIfConstant("Ks", 0);
            dict.RemoveFloat("roughness");
        }
        else if (name == "mirror")
        {
            name = matName = "conductor";
            extra += indent(1) + "\"float roughness\" [ 0 ]\n";
            extra += indent(1) + "\"spectrum eta\" [ \"metal-Ag-eta\" ]\n";
            extra += indent(1) + "\"spectrum k\" [ \"metal-Ag-k\" ]\n";

            removeParamSilentIfConstant("Kr", 0);
        }
        else if (name == "disney")
        {
            name = "diffuse";
            dict.RenameParameter("color", "reflectance");
        }
        else if (name == "hair")
            dict.RenameParameter("color", "reflectance");
        else if (name.Length == 0 || name == "none")
            name = "interface";

        return extra;
    }

    public override void Material(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);
        if (upgrade)
            dict.RenameUsedTextures(definedTextures);

        string extra = "";
        string newName = name;
        if (upgrade)
            extra = upgradeMaterial(ref newName, dict, loc);

        Printf($"{indent()}Material \"{newName}\"\n");
        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    public override void MakeNamedMaterial(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        if (upgrade)
        {
            dict.RenameUsedTextures(definedTextures);

            if (definedNamedMaterials.ContainsKey(name))
            {
                Warning(loc, $"{name}: renaming multiply-defined named material");
                definedNamedMaterials[name] = $"{name}-renamed-{namedMaterialRenameCount++}";
            }
            else
                definedNamedMaterials[name] = name;
            Printf($"{indent()}MakeNamedMaterial \"{definedNamedMaterials[name]}\"\n");
        }
        else
            Printf($"{indent()}MakeNamedMaterial \"{name}\"\n");

        string extra = "";
        if (upgrade)
        {
            string matName = dict.GetOneString("type", "");
            extra = upgradeMaterial(ref matName, dict, loc);
            dict.RemoveString("type");
            extra = indent(1) + $"\"string type\" [ \"{matName}\" ]\n" + extra;
        }
        Printf(extra + dict.ToParameterList(catIndentCount));

        if (upgrade)
            namedMaterialDictionaries[definedNamedMaterials[name]] = dict;
    }

    public override void NamedMaterial(string name, FileLoc loc) => Printf($"{indent()}NamedMaterial \"{name}\"\n");

    private static bool upgradeRGBToScale(ParameterDictionary dict, string name, ref Float totalScale)
    {
        List<Spectrum> s = dict.GetSpectrumArray(name, SpectrumType.Unbounded);
        if (s.Count == 0)
            return true;

        RGB? rgb = dict.GetOneRGB(name);
        if (!rgb.HasValue || rgb.Value.r != rgb.Value.g || rgb.Value.g != rgb.Value.b)
            return false;

        totalScale *= rgb.Value.r;
        dict.RemoveSpectrum(name);
        return true;
    }

    private string upgradeMapname(ParameterDictionary dict)
    {
        string n = dict.GetOneString("mapname", "");
        if (n.Length == 0)
            return "";

        dict.RemoveString("mapname");
        return indent(1) + $"\"string filename\" \"{n}\"\n";
    }

    public override void LightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        Printf($"{indent()}LightSource \"{name}\"\n");

        string extra = "";
        if (upgrade)
        {
            Float totalScale = 1;
            if (!upgradeRGBToScale(dict, "scale", ref totalScale))
            {
                ErrorExitDeferred(dict.loc("scale"), "In pbrt-v4, \"scale\" is a \"float\" parameter " +
                                                     "to light sources. " + "Please modify your scene file manually.");
                return;
            }
            dict.RemoveInt("samples");

            if (dict.GetOneString("mapname", "").Length > 0)
            {
                if (name == "infinite" && !upgradeRGBToScale(dict, "L", ref totalScale))
                {
                    ErrorExitDeferred(dict.loc("L"), "Non-constant \"L\" is no longer supported with " +
                                                     "\"mapname\" for " +
                                                     "the \"infinite\" light source. Please upgrade your scene " +
                                                     "file manually.");
                    return;
                }
                else if (name == "projection" && !upgradeRGBToScale(dict, "I", ref totalScale))
                {
                    ErrorExitDeferred(dict.loc("I"), "\"I\" is no longer supported with \"mapname\" for " +
                                                     "the \"projection\" light source. Please upgrade your scene " +
                                                     "file manually.");
                    return;
                }
            }

            totalScale *= dict.UpgradeBlackbody("I");
            totalScale *= dict.UpgradeBlackbody("L");

            // Do this after we've handled infinite "L" with a map, since
            // it removes the "mapname" parameter from the dictionary.
            extra += upgradeMapname(dict);

            if (totalScale != 1)
            {
                totalScale *= dict.GetOneFloat("scale", 1f);
                dict.RemoveFloat("scale");
                Printf($"{indent(1)}\"float scale\" [{F(totalScale)}]\n");
            }
        }

        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    public override void AreaLightSource(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);
        string extra = "";
        Float totalScale = 1;
        if (upgrade)
        {
            if (!upgradeRGBToScale(dict, "scale", ref totalScale))
            {
                ErrorExitDeferred(dict.loc("scale"), "In pbrt-v4, \"scale\" is a \"float\" parameter " +
                                                     "to light sources. " + "Please modify your scene file manually.");
                return;
            }

            totalScale *= dict.UpgradeBlackbody("L");

            if (name == "area")
                Printf($"{indent()}AreaLightSource \"diffuse\"\n");
            else
                Printf($"{indent()}AreaLightSource \"{name}\"\n");
            dict.RemoveInt("nsamples");
        }
        else
            Printf($"{indent()}AreaLightSource \"{name}\"\n");

        if (totalScale != 1)
            Printf($"{indent(1)}\"float scale\" [{F(totalScale)}]\n");
        Printf(extra + dict.ToParameterList(catIndentCount));
    }

    private string upgradeTriMeshUVs(ParameterDictionary dict)
    {
        List<Point2f> uv = dict.GetPoint2fArray("st");
        if (uv.Count > 0)
            dict.RemovePoint2f("st");
        else
        {
            void upgradeFloatArray(string name)
            {
                List<Float> fuv = dict.GetFloatArray(name);
                if (fuv.Count == 0)
                    return;

                List<Point2f> tempUVs = new(fuv.Count / 2);
                for (int i = 0; i < fuv.Count / 2; ++i)
                    tempUVs.Add(new Point2f(fuv[2 * i], fuv[2 * i + 1]));
                dict.RemoveFloat(name);
                uv = tempUVs;
            }
            upgradeFloatArray("uv");
            upgradeFloatArray("st");
        }

        if (uv.Count == 0)
            return "";

        StringBuilder s = new(indent(1) + "\"point2 uv\" [ ");
        for (int i = 0; i < uv.Count; ++i)
        {
            s.Append($"{F(uv[i].x)} {F(uv[i].y)} ");
            if ((i + 1) % 4 == 0)
            {
                s.Append('\n');
                s.Append(indent(2));
            }
        }
        s.Append("]\n");
        return s.ToString();
    }

    public override void Shape(string name, List<ParsedParameter> parameters, FileLoc loc)
    {
        ParameterDictionary dict = new(parameters, RGBColorSpace.sRGB);

        if (toPly && name == "trianglemesh")
        {
            List<int> vi = dict.GetIntArray("indices");

            if (vi.Count < 500)
            {
                // It's a small mesh; don't bother with a PLY file after all.
                Printf($"{indent()}Shape \"{name}\"\n");
                Printf(dict.ToParameterList(catIndentCount));
            }
            else
            {
                string plyPrefix = Environment.GetEnvironmentVariable("PLY_PREFIX") ?? "mesh";
                string fn = $"{plyPrefix}_{plyCount++:00000}.ply";

                Transform identity = new();
                TriangleMesh? mesh = Triangle.CreateMesh(identity, false, dict, loc);
                if (!mesh!.WritePLY(fn))
                    ErrorExit(loc, $"{fn}: unable to write PLY file.");

                dict.RemoveInt("indices");
                dict.RemovePoint3f("P");
                dict.RemovePoint2f("uv");
                dict.RemoveNormal3f("N");
                dict.RemoveVector3f("S");
                dict.RemoveInt("faceIndices");

                Printf($"{indent()}Shape \"plymesh\" \"string filename\" \"{fn}\"\n");
                Printf(dict.ToParameterList(catIndentCount));
            }
            return;
        }

        Printf($"{indent()}Shape \"{name}\"\n");

        if (upgrade)
        {
            if (name == "trianglemesh")
            {
                // Remove indices if they're [0 1 2] and we have a single triangle
                List<int> indices = dict.GetIntArray("indices");
                if (indices.Count == 3 && dict.GetPoint3fArray("P").Count == 3 && indices[0] == 0 &&
                    indices[1] == 1 && indices[2] == 2)
                    dict.RemoveInt("indices");
            }

            if (name == "bilinearmesh")
            {
                // Remove indices if they're [0 1 2 3] and we have a single blp
                List<int> indices = dict.GetIntArray("indices");
                if (indices.Count == 4 && dict.GetPoint3fArray("P").Count == 4 && indices[0] == 0 &&
                    indices[1] == 1 && indices[2] == 2 && indices[3] == 3)
                    dict.RemoveInt("indices");
            }

            if (name == "loopsubdiv")
            {
                List<int> levels = dict.GetIntArray("nlevels");
                if (levels.Count > 0)
                {
                    Printf($"{indent(1)}\"integer levels\" [ {levels[0]} ]\n");
                    dict.RemoveInt("nlevels");
                }
            }
            if (name == "trianglemesh" || name == "plymesh")
            {
                dict.RemoveBool("discarddegenerateUVs");
                dict.RemoveTexture("shadowalpha");
            }

            if (name == "trianglemesh")
            {
                string extra = upgradeTriMeshUVs(dict);
                Printf(extra);
            }

            dict.RenameParameter("Kd", "reflectance");
        }

        Printf(dict.ToParameterList(catIndentCount));
    }

    public override void ReverseOrientation(FileLoc loc) => Printf($"{indent()}ReverseOrientation\n");

    public override void ObjectBegin(string name, FileLoc loc)
    {
        if (upgrade)
        {
            if (definedObjectInstances.ContainsKey(name))
            {
                Warning(loc, $"{name}: renaming multiply-defined object instance");
                definedObjectInstances[name] = $"{name}-renamed-{objectRenameCount++}";
            }
            else
                definedObjectInstances[name] = name;
            Printf($"{indent()}ObjectBegin \"{definedObjectInstances[name]}\"\n");
        }
        else
            Printf($"{indent()}ObjectBegin \"{name}\"\n");
    }

    public override void ObjectEnd(FileLoc loc) => Printf($"{indent()}ObjectEnd\n");

    public override void ObjectInstance(string name, FileLoc loc)
    {
        if (upgrade)
        {
            if (!definedObjectInstances.TryGetValue(name, out string? renamed))
                // this is legit if we're upgrading multiple files separately...
                Printf($"{indent()}ObjectInstance \"{name}\"\n");
            else
                // use the most recent renaming of it
                Printf($"{indent()}ObjectInstance \"{renamed}\"\n");
        }
        else
            Printf($"{indent()}ObjectInstance \"{name}\"\n");
    }

    // (pbrt does this check in FormattingParserTarget's destructor.)
    public override void EndOfFiles()
    {
        output.Flush();
        if (errorExit)
            ErrorExit("Fatal errors during scene updating.");
    }
}

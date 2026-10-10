// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/parser_test.cpp, with tests added for number parsing
// and formatting and for FormattingParserTarget. The expected --format and
// --upgrade output is from pbrt; the port's output was also checked against
// pbrt's for all 166 files of pbrt-v4-scenes and for files exercising each
// of the --upgrade rewrites and --toply.

using System.Text;

namespace Pbrt.Tests;

[Collection("Options")]
public class ParserTests
{
    private static List<string> extract(Tokenizer t)
    {
        List<string> tokens = [];
        while (true)
        {
            Token? tok = t.Next();
            if (!tok.HasValue)
                return tokens;
            tokens.Add(tok.Value.token);
        }
    }

    private static void checkTokens(Tokenizer t, params string[] expected) => Assert.Equal(expected, extract(t));

    [Fact]
    public void TokenizerBasics()
    {
        List<string> errors = [];
        void err(string e, FileLoc? loc) => errors.Add(e);

        checkTokens(Tokenizer.CreateFromString("Shape \"sphere\" \"float radius\" [1]", err),
                    "Shape", "\"sphere\"", "\"float radius\"", "[", "1", "]");

        checkTokens(Tokenizer.CreateFromString("Shape \"sphere\"\n\"float radius\" [1]", err),
                    "Shape", "\"sphere\"", "\"float radius\"", "[", "1", "]");

        checkTokens(Tokenizer.CreateFromString("\nShape\"sphere\" # foo bar [\n\"float radius\\\"\" 1", err),
                    "Shape", "\"sphere\"", "# foo bar [", "\"float radius\"\"", "1");
        Assert.Empty(errors);
    }

    [Fact]
    public void TokenizerErrors()
    {
        void expectError(string str, string expected)
        {
            bool gotError = false;
            Tokenizer t = Tokenizer.CreateFromString(str, (e, loc) => gotError = e == expected);
            extract(t);
            Assert.True(gotError, str);
        }

        expectError("Shape\"sphere\"\t\t # foo bar\n\"float radius", "premature EOF");
        expectError("Shape\"sphere\"\t\t # foo bar\n\"float radius\\", "premature EOF");
        expectError("Shape\"sphere\"\t\t # foo bar\n\"float radius\n\" 5", "unterminated string");
    }

    [Fact]
    public void TokenizeFile()
    {
        string filename = ImageTests.InTestDir("test.tok");
        File.WriteAllText(filename, "\nWorldBegin # hello\nIntegrator \"deep\" \"float density\" [ 2 2.66612 -5e-51]\n");

        Tokenizer t = Tokenizer.CreateFromFile(filename, (e, loc) => Assert.Fail($"Unexpected error: {e}"))!;
        checkTokens(t, "WorldBegin", "# hello", "Integrator", "\"deep\"", "\"float density\"", "[", "2", "2.66612",
                    "-5e-51", "]");

        Assert.True(RemoveFile(filename));
    }

    [Fact]
    public void TokenLocations()
    {
        Tokenizer t = Tokenizer.CreateFromString("A \"b\\n\"\n  [ 1 ]\n# c", (e, loc) => Assert.Fail(e));
        Token a = t.Next()!.Value;
        Assert.Equal((1, 0), (a.loc.line, a.loc.column));
        Token b = t.Next()!.Value;
        // Escapes are decoded.
        Assert.Equal("\"b\n\"", b.token);
        // (ungetChar() doesn't back up the column, so tokens after a
        // statement or number start one column later.)
        Assert.Equal((1, 3), (b.loc.line, b.loc.column));
        Token open = t.Next()!.Value;
        Assert.Equal((2, 2), (open.loc.line, open.loc.column));
        Assert.Equal("1", t.Next()!.Value.token);
        Assert.Equal("]", t.Next()!.Value.token);
        Token c = t.Next()!.Value;
        Assert.Equal("# c", c.token);
        Assert.Equal(3, c.loc.line);
        Assert.Null(t.Next());

        // UTF-16 files are reported.
        string fn = ImageTests.InTestDir("utf16.pbrt");
        File.WriteAllText(fn, "WorldBegin", Encoding.Unicode);
        string? error = null;
        Tokenizer.CreateFromFile(fn, (e, loc) => error = e);
        Assert.StartsWith("File is encoded with UTF-16", error);
        Assert.True(RemoveFile(fn));

        // Bad escapes are fatal.
        Assert.Throws<FatalErrorException>(() =>
            extract(Tokenizer.CreateFromString("\"a\\q\"", (e, loc) => { })));
    }

    [Fact]
    public void FloatFormatting()
    {
        // pbrt's StringPrintf("%f") (double-conversion's shortest form).
        (float, string)[] cases =
        [
            (1, "1"), (0.5f, "0.5"), (1e9f, "1e9"), (123456789, "123456790"), (1e-6f, "0.000001"),
            (1e-7f, "1e-7"), (-0f, "-0"), (2.66612f, "2.66612"), (1.5e10f, "1.5e10"), (1e-45f, "1e-45"),
            (3.4028235e38f, "3.4028235e38"), (100000000, "100000000"), (0.1f, "0.1"),
            (float.PositiveInfinity, "Inf"), (float.NegativeInfinity, "-Inf"), (float.NaN, "NaN"),
            (16777216, "16777216"), (-1.25e-5f, "-0.0000125"), (999999900, "999999900"), (0, "0")
        ];
        foreach ((float v, string s) in cases)
            Assert.Equal(s, FloatToString(v));
        Assert.Equal("0.1", DoubleToString(0.1));
        Assert.Equal("1e100", DoubleToString(1e100));
        Assert.Equal("123456789", DoubleToString(123456789));
    }

    [Fact]
    public void FloatParsing()
    {
#if PBRT_FLOAT_AS_DOUBLE
        (string, Float)[] good =
        [
            ("1", 1), ("-1", -1), ("+2.5", 2.5), (".5", .5), ("1.", 1), ("1e3", 1000), ("1E-2", .01),
            ("0x1F", 31), ("-0x10", -16), ("16777217", 16777217), ("1e40", 1e40),
            ("2.66612", 2.66612), ("-5e-51", -5e-51), ("1e400", double.PositiveInfinity)
        ];
#else
        (string, Float)[] good =
        [
            ("1", 1), ("-1", -1), ("+2.5", 2.5f), (".5", .5f), ("1.", 1), ("1e3", 1000), ("1E-2", .01f),
            ("0x1F", 31), ("-0x10", -16), ("16777217", 16777216), ("1e40", float.PositiveInfinity),
            ("2.66612", 2.66612f), ("-5e-51", -0f)
        ];
#endif
        foreach ((string s, Float v) in good)
        {
            Assert.True(StringToFloat(s, out Float f), s);
            Assert.Equal(v, f);
        }
        foreach (string s in (string[])["", ".", "1e", "1e+", "abc", "1.2.3", "0x", "inf", "nan", " 1", "1 ", "--1", "0x1.8p1"])
            Assert.False(StringToFloat(s, out _), s);
    }

    private static string Format(string scene, bool upgrade)
    {
        StringWriter sw = new();
        bool oldUpgrade = Options.upgrade;
        Options.upgrade = upgrade;
        try
        {
            ParseString(new FormattingParserTarget(false, upgrade, sw), scene);
        }
        finally
        {
            Options.upgrade = oldUpgrade;
        }
        return sw.ToString();
    }

    [Fact]
    public void FormatScene()
    {
        string scene = """
                       LookAt 0 0 5 0 0 0 0 1 0
                       Camera "perspective" "float fov" 45
                       Film "rgb" "string filename" "out.exr"
                       WorldBegin
                       # a comment
                       AttributeBegin
                         Material "diffuse" "rgb reflectance" [ .5 .5 .5 ]
                         Shape "sphere" "float radius" 2 "bool flag" true "string s" ["a" "b"]
                       AttributeEnd
                       TransformBegin
                       Translate 1 2 3
                       TransformEnd
                       Attribute "shape" "integer n" [1 2 3]
                       Shape "trianglemesh" "point3 P" [0 0 0 1 0 0 0 1 0 0.123456 0.234567 0.345678 0.456789 0.567891 0.678912 0.789123 0.891234 0.912345 1.5 2.5 3.5 4.5 5.5 6.5] "integer indices" [0 1 2]
                       """;
        string expected = """
                          LookAt 0 0 5
                              0 0 0
                              0 1 0
                          Camera "perspective"
                              "float fov" [ 45 ]
                          Film "rgb"
                              "string filename" [ "out.exr" ]


                          WorldBegin

                          # a comment

                          AttributeBegin
                              Material "diffuse"
                                  "rgb reflectance" [ 0.5 0.5 0.5 ]
                              Shape "sphere"
                                  "string s" [ "a" "b" ]
                                  "bool flag" [ true ]
                                  "float radius" [ 2 ]
                          AttributeEnd
                          AttributeBegin
                              Translate 1 2 3
                          AttributeEnd
                          Attribute "shape"     "integer n" [ 1 2 3 ]

                          Shape "trianglemesh"
                              "integer indices" [ 0 1 2 ]
                              "point3 P" [ 0 0 0 1 0 0 0 1 0 0.123456 0.234567 0.345678 0.456789 0.567891 0.678912
                                           0.789123 0.891234 0.912345 1.5 2.5 3.5 4.5 5.5 6.5 ]

                          """;
        // (pbrt leaves a space at the end of the wrapped line.)
        expected = expected.ReplaceLineEndings("\n").Replace("0.678912\n", "0.678912 \n");
        Assert.Equal(expected, Format(scene, false));
    }

    [Fact]
    public void UpgradeScene()
    {
        string scene = """
                       Sampler "lowdiscrepancy"
                       PixelFilter "gaussian" "float xwidth" 2 "float alpha" 2
                       Film "image" "float scale" 2
                       WorldBegin
                       Texture "t" "color" "imagemap" "string filename" "a.png" "bool trilinear" true
                       Material "plastic" "color Kd" [0.1 0.2 0.3] "color Ks" [0 0 0] "float roughness" 0.1
                       Material "uber" "texture Kd" "t" "float index" 1.33
                       LightSource "point" "blackbody I" [5500 2] "integer samples" 4
                       AreaLightSource "area" "color L" [1 1 1]
                       Shape "loopsubdiv" "integer nlevels" 2 "point P" [0 0 0 1 0 0 0 1 0] "integer indices" [0 1 2]
                       """;
        // (pbrt drops the "uber" material's "index": upgradeMaterial()
        // returns early without the eta it made from it.)
        string expected = """
                          Sampler "paddedsobol"
                          PixelFilter "gaussian"
                              "float xradius" [ 2 ]
                              "float sigma" [ 0.5 ]
                          Film "rgb"
                              "float iso" [ 200 ]


                          WorldBegin

                          Texture "t" "spectrum" "imagemap"
                              "string filter" "trilinear"
                              "string filename" [ "a.png" ]
                          Material "diffuse"
                              "rgb reflectance" [ 0.1 0.2 0.3 ]
                          Material "coateddiffuse"
                              "texture reflectance" [ "t" ]
                          LightSource "point"
                              "float scale" [2]
                              "blackbody I" [ 5500 ]
                          AreaLightSource "diffuse"
                              "rgb L" [ 1 1 1 ]
                          Shape "loopsubdiv"
                              "integer levels" [ 2 ]
                              "integer indices" [ 0 1 2 ]
                              "point3 P" [ 0 0 0 1 0 0 0 1 0 ]

                          """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Format(scene, true));

        // Errors are deferred to the end.
        Assert.Throws<FatalErrorException>(() =>
            Format("WorldBegin LightSource \"point\" \"color scale\" [1 2 3] Shape \"sphere\"", true));
    }

    [Fact]
    public void ToPly()
    {
        string dir = Directory.CreateTempSubdirectory("pbrt-toply").FullName;
        string oldDir = Directory.GetCurrentDirectory();
        StringWriter sw = new();
        StringBuilder indices = new(), P = new();
        for (int i = 0; i < 200; ++i)
            indices.Append($"{i % 7} {(i + 1) % 7} {(i + 2) % 7} ");
        for (int i = 0; i < 7; ++i)
            P.Append($"{i} {i * i} 1 ");
        try
        {
            Directory.SetCurrentDirectory(dir);
            Environment.SetEnvironmentVariable("PLY_PREFIX", "test");
            ParseString(new FormattingParserTarget(true, false, sw),
                        $"WorldBegin Shape \"trianglemesh\" \"integer indices\" [{indices}] \"point3 P\" [{P}] " +
                        "\"float alpha\" 1\nShape \"trianglemesh\" \"point3 P\" [0 0 0 1 0 0 0 1 0]");
            string[] plys = Directory.GetFiles(dir, "test_*.ply");
            Assert.Single(plys);
            TriQuadMesh mesh = TriQuadMesh.ReadPLY(plys[0]);
            Assert.Equal(600, mesh.triIndices.Count);
            Assert.Equal(new Point3f(6, 36, 1), mesh.p[6]);
            string name = Path.GetFileName(plys[0]);
            Assert.Equal("\n\nWorldBegin\n\n" +
                         $"Shape \"plymesh\" \"string filename\" \"{name}\"\n    \"float alpha\" [ 1 ]\n" +
                         "Shape \"trianglemesh\"\n    \"point3 P\" [ 0 0 0 1 0 0 0 1 0 ]\n", sw.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLY_PREFIX", null);
            Directory.SetCurrentDirectory(oldDir);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParseErrors()
    {
        void fails(string scene) =>
            Assert.Throws<FatalErrorException>(() => ParseString(new FormattingParserTarget(false, false, new StringWriter()), scene));

        fails("Foo");
        fails("Shape sphere");
        fails("Shape \"sphere\" \"float\" 1");
        fails("Shape \"sphere\" \"\" 1");
        fails("Shape \"sphere\" \"float radius\" [ 1 \"a\" ]");
        fails("Shape \"sphere\" \"string s\" [ \"a\" 1 ]");
        fails("Shape \"sphere\" \"bool b\" [ true 1 ]");
        fails("Shape \"sphere\" \"integer n\" [ 1.5 ]");
        fails("Shape \"sphere\" \"integer n\" [ 3000000000 ]");
        fails("Shape \"sphere\" \"float x\" [ 1x ]");
        fails("Shape \"sphere\" \"float x\" [ 1");
        fails("Translate 1 2");
        fails("ActiveTransform Foo");
        fails("ConcatTransform 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1");
    }
}

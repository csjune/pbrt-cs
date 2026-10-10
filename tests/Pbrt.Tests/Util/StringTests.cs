// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of src/pbrt/util/string_test.cpp

namespace Pbrt.Tests;

public class UnicodeTests
{
    [Fact]
    public void BasicNormalization()
    {
        // "Amélie" two ways, via https://en.wikipedia.org/wiki/Unicode_equivalence
        string nfc = "Amélie";
        string nfd = "Amélie";
        Assert.NotEqual(nfc, nfd);

        Assert.Equal(nfc, NormalizeUTF8(nfc));  // nfc is already normalized
        Assert.Equal(nfc, NormalizeUTF8(nfd));  // normalizing nfd should make it equal nfc
    }
}

// Not in string_test.cpp: checks for the port.
public class StringTests
{
    [Fact]
    public void Split()
    {
        Assert.Equal(["a", "b", ""], SplitStringsFromWhitespace("  a\tb \n"));
        Assert.Equal([""], SplitStringsFromWhitespace(""));
        Assert.Equal(["gamma", "2.2"], SplitStringsFromWhitespace("gamma 2.2"));

        Assert.Empty(SplitString("", ','));
        Assert.Equal(["a", "", "b", ""], SplitString("a,,b,", ','));
        Assert.Equal([1, -2, 3], SplitStringToInts("1,-2,3", ','));
        Assert.Empty(SplitStringToInts("1,x,3", ','));
        Assert.Equal([1.5f, -2f, 3e2f], SplitStringToFloats("1.5,-2,3e2", ','));
        Assert.Equal([10000000000L], SplitStringToInt64s("10000000000", ','));
        Assert.Equal([0.1], SplitStringToDoubles("0.1", ','));
    }

    [Fact]
    public void AtoiAtof()
    {
        // Like std::stoi()/std::stof(): the longest numeric prefix.
        Assert.True(Atoi(" 42abc", out int i));
        Assert.Equal(42, i);
        Assert.False(Atoi("abc", out i));
        Assert.False(Atoi("99999999999", out i));
        Assert.True(Atoi("99999999999", out long l));
        Assert.Equal(99999999999L, l);

        Assert.True(Atof("1.5e3x", out float f));
        Assert.Equal(1500f, f);
        Assert.True(Atof(".5", out f));
        Assert.Equal(.5f, f);
        Assert.True(Atof("2e", out f));
        Assert.Equal(2f, f);
        Assert.True(Atof("-inf", out f));
        Assert.Equal(float.NegativeInfinity, f);
        Assert.False(Atof("e5", out f));
        Assert.False(Atof(".", out f));
        Assert.False(Atof("1e100", out f));
        Assert.True(Atof("1e100", out double d));
        Assert.Equal(1e100, d);
    }
}

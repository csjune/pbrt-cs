using System.Runtime.InteropServices;
using Xunit;

namespace Pbrt.Tests;

// Util.Erf() and Util.Log1p() port glibc's erff, erf and log1p, which .NET
// lacks; check them bit for bit against the C library they come from.
// (Linux only: other C libraries compute these differently.)
public partial class LibmTests
{
    [LibraryImport("libm.so.6", EntryPoint = "erf")]
    private static partial double erf(double x);

    [LibraryImport("libm.so.6", EntryPoint = "erff")]
    private static partial float erff(float x);

    [LibraryImport("libm.so.6", EntryPoint = "log1p")]
    private static partial double log1p(double x);

    // Values over many magnitudes, both signs, and the special cases.
    private static IEnumerable<double> Inputs()
    {
        double[] special = [0, -0.0, 1, -1, 1e-300, 5e-324, 0.84375, 1.25, 1 / 0.35, 6, 28,
                            double.PositiveInfinity, double.NegativeInfinity, double.NaN];
        foreach (double v in special)
            yield return v;
        RNG rng = new(1234);
        for (int i = 0; i < 200000; ++i)
        {
            double v = Math.Pow(10, -12 + 14 * rng.Uniform<double>());
            yield return (i & 1) != 0 ? -v : v;
        }
    }

    private static bool SameBits(double a, double b) =>
        BitConverter.DoubleToUInt64Bits(a) == BitConverter.DoubleToUInt64Bits(b) || (double.IsNaN(a) && double.IsNaN(b));

    [Fact]
    public void ErfDouble()
    {
        if (!OperatingSystem.IsLinux())
            return;
        foreach (double x in Inputs())
            Assert.True(SameBits(erf(x), Util.Erf(x)), $"erf({x:R}): {erf(x):R} vs {Util.Erf(x):R}");
    }

    [Fact]
    public void ErfFloat()
    {
        if (!OperatingSystem.IsLinux())
            return;
        foreach (double d in Inputs())
        {
            float x = (float)d;
            float e = erff(x), r = Util.Erf(x);
            Assert.True(BitConverter.SingleToUInt32Bits(e) == BitConverter.SingleToUInt32Bits(r) ||
                            (float.IsNaN(e) && float.IsNaN(r)),
                        $"erff({x:R}): {e:R} vs {r:R}");
        }
    }

    [Fact]
    public void Log1pDouble()
    {
        if (!OperatingSystem.IsLinux())
            return;
        foreach (double x in Inputs())
        {
            // log1p is defined for x >= -1; also cover x close to -1.
            double y = x < -1 ? -1 + 1 / -x : x;
            Assert.True(SameBits(log1p(y), Util.Log1p(y)), $"log1p({y:R}): {log1p(y):R} vs {Util.Log1p(y):R}");
        }
    }
}

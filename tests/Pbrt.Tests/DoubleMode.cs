// Tests that don't apply when Float is double (built with
// -p:PbrtFloatAsDouble=true). They're skipped with the reason in that build
// and run as usual otherwise.

namespace Pbrt.Tests;

internal static class DoubleMode
{
#if PBRT_FLOAT_AS_DOUBLE
    // pbrt's version of the test also fails when Float is double: it
    // assumes float precision or tolerances that only hold for the random
    // numbers float gives.
    public const string? PbrtFails = "pbrt's version of this test fails too when Float is double";

    // pbrt leaves the test out when Float is double since there's no higher
    // precision to check the results with.
    public const string? NeedsHigherPrecision = "needs a type more precise than Float (pbrt leaves it out too)";
#else
    public const string? PbrtFails = null;
    public const string? NeedsHigherPrecision = null;
#endif
}

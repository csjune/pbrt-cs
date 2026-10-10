// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of SquareMatrix<N> from src/pbrt/util/math.h and math.cpp.
//
// C# has no integer template parameters, so the sizes pbrt actually uses
// are separate structs: SquareMatrix3 and SquareMatrix4. m[i][j] in pbrt is
// m[i, j] here.
//
// NOTE: like pbrt's default constructor, `new SquareMatrix4()` is the
// identity, but `default(SquareMatrix4)` (and zero-initialized fields and
// arrays) is the zero matrix.

namespace Pbrt;

[InlineArray(9)]
internal struct FloatBuffer9
{
    private Float e0;
}

[InlineArray(16)]
internal struct FloatBuffer16
{
    private Float e0;
}

// SquareMatrix<3> Definition
public struct SquareMatrix3 : IEquatable<SquareMatrix3>
{
    public const int N = 3;
    private FloatBuffer9 m;

    // SquareMatrix Public Methods
    public static SquareMatrix3 Zero() => default;

    public SquareMatrix3()
    {
        m[0] = m[4] = m[8] = 1;
    }

    public SquareMatrix3(Float m00, Float m01, Float m02,
                         Float m10, Float m11, Float m12,
                         Float m20, Float m21, Float m22)
    {
        m[0] = m00; m[1] = m01; m[2] = m02;
        m[3] = m10; m[4] = m11; m[5] = m12;
        m[6] = m20; m[7] = m21; m[8] = m22;
    }

    public SquareMatrix3(ReadOnlySpan<Float> t)
    {
        CHECK_EQ(N * N, t.Length);
        t.CopyTo(m);
    }

    public SquareMatrix3(Float[,] mat)
    {
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
                this[i, j] = mat[i, j];
    }

    public static SquareMatrix3 Diag(Float d0, Float d1, Float d2)
    {
        SquareMatrix3 r = Zero();
        r[0, 0] = d0;
        r[1, 1] = d1;
        r[2, 2] = d2;
        return r;
    }

    public Float this[int i, int j]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => m[i * N + j];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => m[i * N + j] = value;
    }

    public static SquareMatrix3 operator +(SquareMatrix3 a, SquareMatrix3 b)
    {
        for (int i = 0; i < N * N; ++i)
            a.m[i] += b.m[i];
        return a;
    }

    public static SquareMatrix3 operator *(SquareMatrix3 a, Float s)
    {
        for (int i = 0; i < N * N; ++i)
            a.m[i] *= s;
        return a;
    }

    public static SquareMatrix3 operator *(Float s, SquareMatrix3 a) => a * s;

    public static SquareMatrix3 operator /(SquareMatrix3 a, Float s)
    {
        DCHECK_NE(s, 0);
        for (int i = 0; i < N * N; ++i)
            a.m[i] /= s;
        return a;
    }

    // pbrt's 3x3 and 4x4 specializations of operator* that use InnerProduct()
    // actually specialize the matrix-vector operator*(SquareMatrix<N>, T)
    // template, so matrix products use the generic FMA loop.
    public static SquareMatrix3 operator *(SquareMatrix3 m1, SquareMatrix3 m2)
    {
        SquareMatrix3 r = default;
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
            {
                r[i, j] = 0;
                for (int k = 0; k < N; ++k)
                    r[i, j] = FMA(m1[i, k], m2[k, j], r[i, j]);
            }
        return r;
    }

    public static bool operator ==(SquareMatrix3 a, SquareMatrix3 b)
    {
        for (int i = 0; i < N * N; ++i)
            if (a.m[i] != b.m[i])
                return false;
        return true;
    }

    public static bool operator !=(SquareMatrix3 a, SquareMatrix3 b) => !(a == b);

    // Lexicographic ordering, as in pbrt.
    public static bool operator <(SquareMatrix3 a, SquareMatrix3 b)
    {
        for (int i = 0; i < N * N; ++i)
        {
            if (a.m[i] < b.m[i])
                return true;
            if (a.m[i] > b.m[i])
                return false;
        }
        return false;
    }

    public static bool operator >(SquareMatrix3 a, SquareMatrix3 b) => b < a;

    public readonly bool Equals(SquareMatrix3 other) => this == other;
    public override readonly bool Equals(object? obj) => obj is SquareMatrix3 other && this == other;
    public override readonly int GetHashCode()
    {
        HashCode h = default;
        for (int i = 0; i < N * N; ++i)
            h.Add(m[i]);
        return h.ToHashCode();
    }

    public readonly bool IsIdentity()
    {
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
            {
                if (i == j)
                {
                    if (this[i, j] != 1)
                        return false;
                }
                else if (this[i, j] != 0)
                    return false;
            }
        return true;
    }

    public override readonly string ToString() => Util.SquareMatrixToString(m, N);
}

// SquareMatrix<4> Definition
public struct SquareMatrix4 : IEquatable<SquareMatrix4>
{
    public const int N = 4;
    private FloatBuffer16 m;

    // SquareMatrix Public Methods
    public static SquareMatrix4 Zero() => default;

    public SquareMatrix4()
    {
        m[0] = m[5] = m[10] = m[15] = 1;
    }

    public SquareMatrix4(Float m00, Float m01, Float m02, Float m03,
                         Float m10, Float m11, Float m12, Float m13,
                         Float m20, Float m21, Float m22, Float m23,
                         Float m30, Float m31, Float m32, Float m33)
    {
        m[0] = m00; m[1] = m01; m[2] = m02; m[3] = m03;
        m[4] = m10; m[5] = m11; m[6] = m12; m[7] = m13;
        m[8] = m20; m[9] = m21; m[10] = m22; m[11] = m23;
        m[12] = m30; m[13] = m31; m[14] = m32; m[15] = m33;
    }

    public SquareMatrix4(ReadOnlySpan<Float> t)
    {
        CHECK_EQ(N * N, t.Length);
        t.CopyTo(m);
    }

    public SquareMatrix4(Float[,] mat)
    {
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
                this[i, j] = mat[i, j];
    }

    public static SquareMatrix4 Diag(Float d0, Float d1, Float d2, Float d3)
    {
        SquareMatrix4 r = Zero();
        r[0, 0] = d0;
        r[1, 1] = d1;
        r[2, 2] = d2;
        r[3, 3] = d3;
        return r;
    }

    public Float this[int i, int j]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => m[i * N + j];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => m[i * N + j] = value;
    }

    public static SquareMatrix4 operator +(SquareMatrix4 a, SquareMatrix4 b)
    {
        for (int i = 0; i < N * N; ++i)
            a.m[i] += b.m[i];
        return a;
    }

    public static SquareMatrix4 operator *(SquareMatrix4 a, Float s)
    {
        for (int i = 0; i < N * N; ++i)
            a.m[i] *= s;
        return a;
    }

    public static SquareMatrix4 operator *(Float s, SquareMatrix4 a) => a * s;

    public static SquareMatrix4 operator /(SquareMatrix4 a, Float s)
    {
        DCHECK_NE(s, 0);
        for (int i = 0; i < N * N; ++i)
            a.m[i] /= s;
        return a;
    }

    // (See SquareMatrix3's operator*: pbrt multiplies with the generic FMA
    // loop.)
    public static SquareMatrix4 operator *(in SquareMatrix4 m1, in SquareMatrix4 m2)
    {
        SquareMatrix4 r = default;
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
            {
                Float v = 0;
                for (int k = 0; k < N; ++k)
                    v = FMA(m1[i, k], m2[k, j], v);
                r[i, j] = v;
            }
        return r;
    }

    public static bool operator ==(in SquareMatrix4 a, in SquareMatrix4 b)
    {
        for (int i = 0; i < N * N; ++i)
            if (a.m[i] != b.m[i])
                return false;
        return true;
    }

    public static bool operator !=(in SquareMatrix4 a, in SquareMatrix4 b) => !(a == b);

    // Lexicographic ordering, as in pbrt.
    public static bool operator <(in SquareMatrix4 a, in SquareMatrix4 b)
    {
        for (int i = 0; i < N * N; ++i)
        {
            if (a.m[i] < b.m[i])
                return true;
            if (a.m[i] > b.m[i])
                return false;
        }
        return false;
    }

    public static bool operator >(in SquareMatrix4 a, in SquareMatrix4 b) => b < a;

    public readonly bool Equals(SquareMatrix4 other) => this == other;
    public override readonly bool Equals(object? obj) => obj is SquareMatrix4 other && this == other;
    public override readonly int GetHashCode()
    {
        HashCode h = default;
        for (int i = 0; i < N * N; ++i)
            h.Add(m[i]);
        return h.ToHashCode();
    }

    public readonly bool IsIdentity()
    {
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
            {
                if (i == j)
                {
                    if (this[i, j] != 1)
                        return false;
                }
                else if (this[i, j] != 0)
                    return false;
            }
        return true;
    }

    public override readonly string ToString() => Util.SquareMatrixToString(m, N);

}

public static partial class Util
{
    internal static string SquareMatrixToString(ReadOnlySpan<Float> m, int n)
    {
        var s = new System.Text.StringBuilder("[ [");
        for (int i = 0; i < n; ++i)
        {
            for (int j = 0; j < n; ++j)
            {
                s.Append(' ').Append(FloatToString(m[i * n + j]));
                if (j < n - 1)
                    s.Append(',');
                else
                    s.Append(" ]");
            }
            if (i < n - 1)
                s.Append(", [");
        }
        s.Append(" ]");
        return s.ToString();
    }

    // SquareMatrix Inline Functions
    public static SquareMatrix3 Transpose(in SquareMatrix3 m)
    {
        SquareMatrix3 r = default;
        for (int i = 0; i < SquareMatrix3.N; ++i)
            for (int j = 0; j < SquareMatrix3.N; ++j)
                r[i, j] = m[j, i];
        return r;
    }

    public static SquareMatrix4 Transpose(in SquareMatrix4 m)
    {
        SquareMatrix4 r = default;
        for (int i = 0; i < SquareMatrix4.N; ++i)
            for (int j = 0; j < SquareMatrix4.N; ++j)
                r[i, j] = m[j, i];
        return r;
    }

    public static Float Determinant(in SquareMatrix3 m)
    {
        Float minor12 = DifferenceOfProducts(m[1, 1], m[2, 2], m[1, 2], m[2, 1]);
        Float minor02 = DifferenceOfProducts(m[1, 0], m[2, 2], m[1, 2], m[2, 0]);
        Float minor01 = DifferenceOfProducts(m[1, 0], m[2, 1], m[1, 1], m[2, 0]);
        return FMA(m[0, 2], minor01, DifferenceOfProducts(m[0, 0], minor12, m[0, 1], minor02));
    }

    public static Float Determinant(in SquareMatrix4 m)
    {
        Float s0 = DifferenceOfProducts(m[0, 0], m[1, 1], m[1, 0], m[0, 1]);
        Float s1 = DifferenceOfProducts(m[0, 0], m[1, 2], m[1, 0], m[0, 2]);
        Float s2 = DifferenceOfProducts(m[0, 0], m[1, 3], m[1, 0], m[0, 3]);

        Float s3 = DifferenceOfProducts(m[0, 1], m[1, 2], m[1, 1], m[0, 2]);
        Float s4 = DifferenceOfProducts(m[0, 1], m[1, 3], m[1, 1], m[0, 3]);
        Float s5 = DifferenceOfProducts(m[0, 2], m[1, 3], m[1, 2], m[0, 3]);

        Float c0 = DifferenceOfProducts(m[2, 0], m[3, 1], m[3, 0], m[2, 1]);
        Float c1 = DifferenceOfProducts(m[2, 0], m[3, 2], m[3, 0], m[2, 2]);
        Float c2 = DifferenceOfProducts(m[2, 0], m[3, 3], m[3, 0], m[2, 3]);

        Float c3 = DifferenceOfProducts(m[2, 1], m[3, 2], m[3, 1], m[2, 2]);
        Float c4 = DifferenceOfProducts(m[2, 1], m[3, 3], m[3, 1], m[2, 3]);
        Float c5 = DifferenceOfProducts(m[2, 2], m[3, 3], m[3, 2], m[2, 3]);

        return (DifferenceOfProducts(s0, c5, s1, c4) + DifferenceOfProducts(s2, c3, -s3, c2) +
                DifferenceOfProducts(s5, c0, s4, c1));
    }

    public static SquareMatrix3? Inverse(in SquareMatrix3 m)
    {
        Float det = Determinant(m);
        if (det == 0)
            return null;
        Float invDet = 1 / det;

        SquareMatrix3 r = default;

        r[0, 0] = invDet * DifferenceOfProducts(m[1, 1], m[2, 2], m[1, 2], m[2, 1]);
        r[1, 0] = invDet * DifferenceOfProducts(m[1, 2], m[2, 0], m[1, 0], m[2, 2]);
        r[2, 0] = invDet * DifferenceOfProducts(m[1, 0], m[2, 1], m[1, 1], m[2, 0]);
        r[0, 1] = invDet * DifferenceOfProducts(m[0, 2], m[2, 1], m[0, 1], m[2, 2]);
        r[1, 1] = invDet * DifferenceOfProducts(m[0, 0], m[2, 2], m[0, 2], m[2, 0]);
        r[2, 1] = invDet * DifferenceOfProducts(m[0, 1], m[2, 0], m[0, 0], m[2, 1]);
        r[0, 2] = invDet * DifferenceOfProducts(m[0, 1], m[1, 2], m[0, 2], m[1, 1]);
        r[1, 2] = invDet * DifferenceOfProducts(m[0, 2], m[1, 0], m[0, 0], m[1, 2]);
        r[2, 2] = invDet * DifferenceOfProducts(m[0, 0], m[1, 1], m[0, 1], m[1, 0]);

        return r;
    }

    public static SquareMatrix4? Inverse(in SquareMatrix4 m)
    {
        // Via: https://github.com/google/ion/blob/master/ion/math/matrixutils.cc,
        // (c) Google, Apache license.

        // For 4x4 do not compute the adjugate as the transpose of the cofactor
        // matrix, because this results in extra work. Several calculations can be
        // shared across the sub-determinants.
        //
        // This approach is explained in David Eberly's Geometric Tools book,
        // excerpted here:
        //   http://www.geometrictools.com/Documentation/LaplaceExpansionTheorem.pdf
        Float s0 = DifferenceOfProducts(m[0, 0], m[1, 1], m[1, 0], m[0, 1]);
        Float s1 = DifferenceOfProducts(m[0, 0], m[1, 2], m[1, 0], m[0, 2]);
        Float s2 = DifferenceOfProducts(m[0, 0], m[1, 3], m[1, 0], m[0, 3]);

        Float s3 = DifferenceOfProducts(m[0, 1], m[1, 2], m[1, 1], m[0, 2]);
        Float s4 = DifferenceOfProducts(m[0, 1], m[1, 3], m[1, 1], m[0, 3]);
        Float s5 = DifferenceOfProducts(m[0, 2], m[1, 3], m[1, 2], m[0, 3]);

        Float c0 = DifferenceOfProducts(m[2, 0], m[3, 1], m[3, 0], m[2, 1]);
        Float c1 = DifferenceOfProducts(m[2, 0], m[3, 2], m[3, 0], m[2, 2]);
        Float c2 = DifferenceOfProducts(m[2, 0], m[3, 3], m[3, 0], m[2, 3]);

        Float c3 = DifferenceOfProducts(m[2, 1], m[3, 2], m[3, 1], m[2, 2]);
        Float c4 = DifferenceOfProducts(m[2, 1], m[3, 3], m[3, 1], m[2, 3]);
        Float c5 = DifferenceOfProducts(m[2, 2], m[3, 3], m[3, 2], m[2, 3]);

        Float determinant = InnerProduct(s0, c5, -s1, c4, s2, c3, s3, c2, s5, c0, -s4, c1);
        if (determinant == 0)
            return null;
        Float s = 1 / determinant;

        return new SquareMatrix4(
            s * InnerProduct(m[1, 1], c5, m[1, 3], c3, -m[1, 2], c4),
            s * InnerProduct(-m[0, 1], c5, m[0, 2], c4, -m[0, 3], c3),
            s * InnerProduct(m[3, 1], s5, m[3, 3], s3, -m[3, 2], s4),
            s * InnerProduct(-m[2, 1], s5, m[2, 2], s4, -m[2, 3], s3),

            s * InnerProduct(-m[1, 0], c5, m[1, 2], c2, -m[1, 3], c1),
            s * InnerProduct(m[0, 0], c5, m[0, 3], c1, -m[0, 2], c2),
            s * InnerProduct(-m[3, 0], s5, m[3, 2], s2, -m[3, 3], s1),
            s * InnerProduct(m[2, 0], s5, m[2, 3], s1, -m[2, 2], s2),

            s * InnerProduct(m[1, 0], c4, m[1, 3], c0, -m[1, 1], c2),
            s * InnerProduct(-m[0, 0], c4, m[0, 1], c2, -m[0, 3], c0),
            s * InnerProduct(m[3, 0], s4, m[3, 3], s0, -m[3, 1], s2),
            s * InnerProduct(-m[2, 0], s4, m[2, 1], s2, -m[2, 3], s0),

            s * InnerProduct(-m[1, 0], c3, m[1, 1], c1, -m[1, 2], c0),
            s * InnerProduct(m[0, 0], c3, m[0, 2], c0, -m[0, 1], c1),
            s * InnerProduct(-m[3, 0], s3, m[3, 1], s1, -m[3, 2], s0),
            s * InnerProduct(m[2, 0], s3, m[2, 2], s0, -m[2, 1], s1));
    }

    public static SquareMatrix3 InvertOrExit(in SquareMatrix3 m)
    {
        SquareMatrix3? inv = Inverse(m);
        CHECK(inv.HasValue);
        return inv.Value;
    }

    public static SquareMatrix4 InvertOrExit(in SquareMatrix4 m)
    {
        SquareMatrix4? inv = Inverse(m);
        CHECK(inv.HasValue);
        return inv.Value;
    }

    // A is rows x 3, B is rows x 3.
    public static SquareMatrix3? LinearLeastSquares(Float[,] A, Float[,] B, int rows)
    {
        const int N = SquareMatrix3.N;
        SquareMatrix3 AtA = SquareMatrix3.Zero();
        SquareMatrix3 AtB = SquareMatrix3.Zero();

        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j)
                for (int r = 0; r < rows; ++r)
                {
                    AtA[i, j] += A[r, i] * A[r, j];
                    AtB[i, j] += A[r, i] * B[r, j];
                }

        SquareMatrix3? AtAi = Inverse(AtA);
        if (!AtAi.HasValue)
            return null;
        return Transpose(AtAi.Value * AtB);
    }
}

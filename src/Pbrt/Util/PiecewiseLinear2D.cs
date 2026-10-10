// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of PLSample and PiecewiseLinear2D<Dimension> from
// src/pbrt/util/sampling.h. C# has no integer template parameters, so the
// number of additional parameters (Dimension) is a runtime value given by
// the length of paramRes, and the parameters are passed as a span.

namespace Pbrt;

public struct PLSample
{
    public Point2f p;
    public Float pdf;

    public PLSample(Point2f p, Float pdf)
    {
        this.p = p;
        this.pdf = pdf;
    }
}

// PiecewiseLinear2D Implementation
// *****************************************************************************
// Marginal-conditional warp
// *****************************************************************************

/// <summary>
/// Implements a marginal sample warping scheme for 2D distributions
/// with linear interpolation and an optional dependence on additional parameters
///
/// This class takes a rectangular floating point array as input and constructs
/// internal data structures to efficiently map uniform variates from the unit
/// square [0, 1]^2 to a function on [0, 1]^2 that linearly
/// interpolates the input array.
///
/// The mapping is constructed via the inversion method, which is applied to
/// a marginal distribution over rows, followed by a conditional distribution
/// over columns.
///
/// The implementation also supports conditional distributions, i.e. 2D
/// distributions that depend on an arbitrary number of parameters (indicated
/// via the Dimension template parameter in pbrt).
///
/// In this case, the input array should have dimensions N0 x N1 x ... x Nn
/// x res[1] x res[0] (where the last dimension is contiguous in memory),
/// and the param_res should be set to { N0, N1, ..., Nn },
/// and param_values should contain the parameter values where the
/// distribution is discretized. Linear interpolation is used when sampling or
/// evaluating the distribution for in-between parameter values.
/// </summary>
public sealed class PiecewiseLinear2D
{
    /// Number of additional parameters
    private readonly int Dimension;

    /// Resolution of the discretized density function
    private readonly Vector2i m_size;

    /// Size of a bilinear patch in the unit square
    private readonly Vector2f m_patch_size, m_inv_patch_size;

    /// Resolution of each parameter (optional)
    private readonly int[] m_param_size;

    /// Stride per parameter in units of sizeof(float)
    private readonly int[] m_param_strides;

    /// Discretization of each parameter domain
    private readonly float[][] m_param_values;

    /// Density values
    private readonly float[] m_data;

    /// Marginal and conditional PDFs
    private readonly float[] m_marginal_cdf;
    private readonly float[] m_conditional_cdf;

    /// <summary>
    /// Construct a marginal sample warping scheme for floating point
    /// data of resolution xSize x ySize.
    ///
    /// paramRes and paramValues are only needed for conditional
    /// distributions (see the text describing the PiecewiseLinear2D class).
    ///
    /// If normalize is set to false, the implementation will not
    /// re-scale the distribution so that it integrates to 1. It can
    /// still be sampled (proportionally), but returned density values
    /// will reflect the unnormalized values.
    ///
    /// If buildCdf is set to false, the implementation will not
    /// construct the cdf needed for sample warping, which saves memory in case
    /// this functionality is not needed (e.g. if only the interpolation in
    /// Evaluate() is used).
    /// </summary>
    public PiecewiseLinear2D(ReadOnlySpan<float> data, int xSize, int ySize,
                             ReadOnlySpan<int> paramRes = default, float[][]? paramValues = null,
                             bool normalize = true, bool buildCdf = true)
    {
        Dimension = paramRes.Length;
        m_size = new Vector2i(xSize, ySize);
        m_patch_size = new Vector2f(1f / (xSize - 1), 1f / (ySize - 1));
        m_inv_patch_size = new Vector2f(m_size - new Vector2i(1, 1));

        if (buildCdf && !normalize)
            LOG_FATAL("PiecewiseLinear2D: build_cdf implies normalize=true");

        /* Keep track of the dependence on additional parameters (optional) */
        int arraySize = Max(Dimension, 1);
        m_param_size = new int[arraySize];
        m_param_strides = new int[arraySize];
        m_param_values = new float[arraySize][];
        for (int i = 0; i < arraySize; ++i)
            m_param_values[i] = [];
        int slices = 1;
        for (int i = Dimension - 1; i >= 0; --i)
        {
            if (paramRes[i] < 1)
                LOG_FATAL("PiecewiseLinear2D(): parameter resolution must be >= 1!");

            m_param_size[i] = paramRes[i];
            m_param_values[i] = paramValues![i].AsSpan(0, paramRes[i]).ToArray();
            m_param_strides[i] = paramRes[i] > 1 ? slices : 0;
            slices *= m_param_size[i];
        }

        int n_values = xSize * ySize;

        m_data = new float[slices * n_values];
        m_marginal_cdf = [];
        m_conditional_cdf = [];

        int dataIn = 0;
        if (buildCdf)
        {
            m_marginal_cdf = new float[slices * m_size.y];
            m_conditional_cdf = new float[slices * n_values];

            Span<float> marginal_cdf = m_marginal_cdf, conditional_cdf = m_conditional_cdf,
                        data_out = m_data;

            for (int slice = 0; slice < slices; ++slice)
            {
                /* Construct conditional CDF */
                for (int y = 0; y < m_size.y; ++y)
                {
                    double sum = 0.0;
                    int i = y * xSize;
                    conditional_cdf[i] = 0f;
                    for (int x = 0; x < m_size.x - 1; ++x, ++i)
                    {
                        sum += .5 * ((double)data[dataIn + i] + (double)data[dataIn + i + 1]);
                        conditional_cdf[i + 1] = (float)sum;
                    }
                }

                /* Construct marginal CDF */
                marginal_cdf[0] = 0f;
                {
                    double sum = 0.0;
                    for (int y = 0; y < m_size.y - 1; ++y)
                    {
                        sum += .5 * ((double)conditional_cdf[(y + 1) * xSize - 1] +
                                     (double)conditional_cdf[(y + 2) * xSize - 1]);
                        marginal_cdf[y + 1] = (float)sum;
                    }
                }

                /* Normalize CDFs and PDF (if requested) */
                float normalization = 1f / marginal_cdf[m_size.y - 1];
                for (int i = 0; i < n_values; ++i)
                    conditional_cdf[i] *= normalization;
                for (int i = 0; i < m_size.y; ++i)
                    marginal_cdf[i] *= normalization;
                for (int i = 0; i < n_values; ++i)
                    data_out[i] = data[dataIn + i] * normalization;

                marginal_cdf = marginal_cdf.Slice(m_size.y);
                conditional_cdf = conditional_cdf.Slice(n_values);
                data_out = data_out.Slice(n_values);
                dataIn += n_values;
            }
        }
        else
        {
            Span<float> data_out = m_data;

            for (int slice = 0; slice < slices; ++slice)
            {
                float normalization = (float)(1f / HProd(m_inv_patch_size));
                if (normalize)
                {
                    double sum = 0.0;
                    for (int y = 0; y < m_size.y - 1; ++y)
                    {
                        int i = y * xSize;
                        for (int x = 0; x < m_size.x - 1; ++x, ++i)
                        {
                            float v00 = data[dataIn + i], v10 = data[dataIn + i + 1],
                                  v01 = data[dataIn + i + xSize], v11 = data[dataIn + i + 1 + xSize],
                                  avg = .25f * (v00 + v10 + v01 + v11);
                            sum += (double)avg;
                        }
                    }
                    normalization = (float)(1.0 / sum);
                }

                for (int k = 0; k < n_values; ++k)
                    data_out[k] = data[dataIn + k] * normalization;

                dataIn += n_values;
                data_out = data_out.Slice(n_values);
            }
        }
    }

    // Computes the parameter interpolation weights and slice offset shared by
    // Sample(), Invert() and Evaluate().
    private int ParamWeights(ReadOnlySpan<Float> param, Span<float> param_weight)
    {
        CHECK_EQ(param.Length, Dimension); // "Incorrect number of additional parameters passed"
        int slice_offset = 0;
        for (int dim = 0; dim < Dimension; ++dim)
        {
            if (m_param_size[dim] == 1)
            {
                param_weight[2 * dim] = 1f;
                param_weight[2 * dim + 1] = 0f;
                continue;
            }

            int param_index = FindInterval(m_param_values[dim], param[dim]);

            float p0 = m_param_values[dim][param_index], p1 = m_param_values[dim][param_index + 1];

            param_weight[2 * dim + 1] = (float)Clamp((param[dim] - p0) / (p1 - p0), 0f, 1f);
            param_weight[2 * dim] = 1f - param_weight[2 * dim + 1];
            slice_offset += m_param_strides[dim] * param_index;
        }
        return slice_offset;
    }

    private readonly ref struct MarginalPredicate(PiecewiseLinear2D d, int offset, ReadOnlySpan<float> paramWeight,
                                                  Float y) : IIndexPredicate
    {
        private readonly ReadOnlySpan<float> paramWeight = paramWeight;

        public bool Evaluate(int idx) =>
            (float)d.Lookup(d.Dimension, d.m_marginal_cdf, 0, offset + idx, d.m_size.y, paramWeight) < y;
    }

    private readonly ref struct ConditionalPredicate(PiecewiseLinear2D d, int offset, int sliceSize,
                                                     ReadOnlySpan<float> paramWeight, Float sampleY,
                                                     Float sampleX) : IIndexPredicate
    {
        private readonly ReadOnlySpan<float> paramWeight = paramWeight;

        public bool Evaluate(int idx) => d.FetchConditional(offset, sliceSize, paramWeight, sampleY, idx) < sampleX;
    }

    private float FetchConditional(int offset, int sliceSize, ReadOnlySpan<float> paramWeight, Float sampleY, int idx)
    {
        float v0 = (float)Lookup(Dimension, m_conditional_cdf, 0, offset + idx, sliceSize, paramWeight),
              v1 = (float)Lookup(Dimension, m_conditional_cdf, m_size.x, offset + idx, sliceSize, paramWeight);

        return (float)((1f - sampleY) * v0 + sampleY * v1);
    }

    /// <summary>
    /// Given a uniformly distributed 2D sample, draw a sample from the
    /// distribution (parameterized by param if applicable)
    ///
    /// Returns the warped sample and associated probability density.
    /// </summary>
    public PLSample Sample(Point2f sample, params ReadOnlySpan<Float> param)
    {
        /* Avoid degeneracies at the extrema */
        sample[0] = Clamp(sample[0], 1 - OneMinusEpsilon, OneMinusEpsilon);
        sample[1] = Clamp(sample[1], 1 - OneMinusEpsilon, OneMinusEpsilon);

        /* Look up parameter-related indices and weights (if Dimension != 0) */
        Span<float> param_weight = stackalloc float[2 * Max(Dimension, 1)];
        int slice_offset = ParamWeights(param, param_weight);

        /* Sample the row first */
        int offset = 0;
        if (Dimension != 0)
            offset = slice_offset * m_size.y;

        int row = FindInterval(m_size.y, new MarginalPredicate(this, offset, param_weight, sample.y));

        sample.y -= (float)Lookup(Dimension, m_marginal_cdf, 0, offset + row, m_size.y, param_weight);

        int slice_size = HProd(m_size);
        offset = row * m_size.x;
        if (Dimension != 0)
            offset += slice_offset * slice_size;

        Float r0 = Lookup(Dimension, m_conditional_cdf, 0, offset + m_size.x - 1, slice_size, param_weight),
              r1 = Lookup(Dimension, m_conditional_cdf, 0, offset + (m_size.x * 2 - 1), slice_size,
                          param_weight);

        bool is_const = FMath.Abs(r0 - r1) < 1e-4f * (r0 + r1);
        sample.y = is_const ? (2f * sample.y) : (r0 - SafeSqrt(r0 * r0 - 2f * sample.y * (r0 - r1)));
        sample.y /= is_const ? (r0 + r1) : (r0 - r1);

        /* Sample the column next */
        sample.x *= (1f - sample.y) * r0 + sample.y * r1;

        int col = FindInterval(m_size.x,
                               new ConditionalPredicate(this, offset, slice_size, param_weight, sample.y, sample.x));

        sample.x -= FetchConditional(offset, slice_size, param_weight, sample.y, col);

        offset += col;

        Float v00 = Lookup(Dimension, m_data, 0, offset, slice_size, param_weight),
              v10 = Lookup(Dimension, m_data, 1, offset, slice_size, param_weight),
              v01 = Lookup(Dimension, m_data, m_size.x, offset, slice_size, param_weight),
              v11 = Lookup(Dimension, m_data, m_size.x + 1, offset, slice_size, param_weight),
              c0 = FMA((1f - sample.y), v00, sample.y * v01),
              c1 = FMA((1f - sample.y), v10, sample.y * v11);

        is_const = FMath.Abs(c0 - c1) < 1e-4f * (c0 + c1);
        sample.x = is_const ? (2f * sample.x) : (c0 - SafeSqrt(c0 * c0 - 2f * sample.x * (c0 - c1)));
        sample.x /= is_const ? (c0 + c1) : (c0 - c1);

        return new PLSample(
            new Point2f((col + sample.x) * m_patch_size.x, (row + sample.y) * m_patch_size.y),
            ((1f - sample.x) * c0 + sample.x * c1) * HProd(m_inv_patch_size));
    }

    /// Inverse of the mapping implemented in Sample()
    public PLSample Invert(Point2f sample, params ReadOnlySpan<Float> param)
    {
        /* Look up parameter-related indices and weights (if Dimension != 0) */
        Span<float> param_weight = stackalloc float[2 * Max(Dimension, 1)];
        int slice_offset = ParamWeights(param, param_weight);

        /* Fetch values at corners of bilinear patch */
        sample.x *= m_inv_patch_size.x;
        sample.y *= m_inv_patch_size.y;
        Vector2i pos = Min(new Vector2i(sample), m_size - new Vector2i(2, 2));
        sample -= new Vector2f(pos);

        int offset = pos.x + pos.y * m_size.x;
        int slice_size = HProd(m_size);
        if (Dimension != 0)
            offset += slice_offset * slice_size;

        /* Invert the X component */
        Float v00 = Lookup(Dimension, m_data, 0, offset, slice_size, param_weight),
              v10 = Lookup(Dimension, m_data, 1, offset, slice_size, param_weight),
              v01 = Lookup(Dimension, m_data, m_size.x, offset, slice_size, param_weight),
              v11 = Lookup(Dimension, m_data, m_size.x + 1, offset, slice_size, param_weight);

        Vector2f w1 = new(sample), w0 = new Vector2f(1, 1) - w1;

        Float c0 = FMA(w0.y, v00, w1.y * v01), c1 = FMA(w0.y, v10, w1.y * v11),
              pdf = FMA(w0.x, c0, w1.x * c1);

        sample.x *= c0 + .5f * sample.x * (c1 - c0);

        Float v0 = Lookup(Dimension, m_conditional_cdf, 0, offset, slice_size, param_weight),
              v1 = Lookup(Dimension, m_conditional_cdf, m_size.x, offset, slice_size, param_weight);

        sample.x += (1f - sample.y) * v0 + sample.y * v1;

        offset = pos.y * m_size.x;
        if (Dimension != 0)
            offset += slice_offset * slice_size;

        Float r0 = Lookup(Dimension, m_conditional_cdf, 0, offset + m_size.x - 1, slice_size, param_weight),
              r1 = Lookup(Dimension, m_conditional_cdf, 0, offset + (m_size.x * 2 - 1), slice_size,
                          param_weight);

        sample.x /= (1f - sample.y) * r0 + sample.y * r1;

        /* Invert the Y component */
        sample.y *= r0 + .5f * sample.y * (r1 - r0);

        offset = pos.y;
        if (Dimension != 0)
            offset += slice_offset * m_size.y;

        sample.y += Lookup(Dimension, m_marginal_cdf, 0, offset, m_size.y, param_weight);

        return new PLSample(sample, pdf * HProd(m_inv_patch_size));
    }

    /// Evaluate the density at position pos. The distribution is
    /// parameterized by param if applicable.
    public float Evaluate(Point2f pos, params ReadOnlySpan<Float> param)
    {
        /* Look up parameter-related indices and weights (if Dimension != 0) */
        Span<float> param_weight = stackalloc float[2 * Max(Dimension, 1)];
        int slice_offset = ParamWeights(param, param_weight);

        /* Compute linear interpolation weights */
        pos.x *= m_inv_patch_size.x;
        pos.y *= m_inv_patch_size.y;
        Vector2i offset = Min(new Vector2i(pos), m_size - new Vector2i(2, 2));

        Vector2f w1 = new Vector2f(pos) - new Vector2f(offset), w0 = new Vector2f(1, 1) - w1;

        int index = offset.x + offset.y * m_size.x;

        int size = HProd(m_size);
        if (Dimension != 0)
            index += slice_offset * size;

        Float v00 = Lookup(Dimension, m_data, 0, index, size, param_weight),
              v10 = Lookup(Dimension, m_data, 1, index, size, param_weight),
              v01 = Lookup(Dimension, m_data, m_size.x, index, size, param_weight),
              v11 = Lookup(Dimension, m_data, m_size.x + 1, index, size, param_weight);

        return (float)(FMA(w0.y, FMA(w0.x, v00, w1.x * v10), w1.y * FMA(w0.x, v01, w1.x * v11)) *
                       HProd(m_inv_patch_size));
    }

    public long BytesUsed()
    {
        long sum = 4L * (m_data.Length + m_marginal_cdf.Length + m_conditional_cdf.Length);
        foreach (float[] v in m_param_values)
            sum += v.Length;
        return sum;
    }

    // pbrt's lookup<Dim>(data + dataOffset, i0, size, param_weight).
    private Float Lookup(int dim, float[] data, int dataOffset, int i0, int size, ReadOnlySpan<float> param_weight)
    {
        if (dim == 0)
            return data[dataOffset + i0];

        int i1 = i0 + m_param_strides[dim - 1] * size;

        Float w0 = param_weight[2 * dim - 2], w1 = param_weight[2 * dim - 1],
              v0 = Lookup(dim - 1, data, dataOffset, i0, size, param_weight),
              v1 = Lookup(dim - 1, data, dataOffset, i1, size, param_weight);

        return FMA(v0, w0, v1 * w1);
    }
}

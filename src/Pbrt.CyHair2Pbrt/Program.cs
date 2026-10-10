//
// cyhair2pbrt
//
// Convert CyHair files to PBRT.
// Hair vertices are interpreted as Catmull-Rom spline points.
// The tool simply converts Catmull-Rom spline points to cubic Bezier points.
//
// MIT license
//
// C# port of src/pbrt/cmd/cyhair2pbrt.cpp

///////////// start of cyhair_loader

/*
The MIT License (MIT)

Copyright (c) 2016 Light Transport Entertainment, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

// Simple Cyhair loader.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Pbrt.CyHair2Pbrt;

public readonly struct real3(float xx, float yy, float zz)
{
    public readonly float x = xx, y = yy, z = zz;

    public real3(float v) : this(v, v, v) { }

    public static real3 operator +(real3 f1, real3 f2) => new(f1.x + f2.x, f1.y + f2.y, f1.z + f2.z);
    public static real3 operator *(real3 f1, real3 f2) => new(f1.x * f2.x, f1.y * f2.y, f1.z * f2.z);
    public static real3 operator /(real3 f1, real3 f2) => new(f1.x / f2.x, f1.y / f2.y, f1.z / f2.z);
    public static real3 operator /(real3 v, float f) => new(v.x / f, v.y / f, v.z / f);
    public static real3 operator *(float f, real3 v) => new(v.x * f, v.y * f, v.z * f);
    // C++'s cps[i] * 2.0f converts 2.0f to real3 through the real3(float)
    // constructor and uses the component-wise product.
    public static real3 operator *(real3 v, float f) => v * new real3(f);
}

public sealed class CyHair
{
    private static readonly float[,] toC2B = {
        {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
        {-1.0f / 6.0f, 6.0f / 6.0f, 1.0f / 6.0f, 0.0f},
        {0.0f, 1.0f / 6.0f, 6.0f / 6.0f, -1.0f / 6.0f},
        {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

    private static readonly float[,] toC2B0 = {
        {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
        {0.0f, 3.0f / 6.0f, 4.0f / 6.0f, -1.0f / 6.0f},
        {0.0f, 1.0f / 6.0f, 6.0f / 6.0f, -1.0f / 6.0f},
        {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

    private static readonly float[,] toC2B1 = {
        {0.0f, 6.0f / 6.0f, 0.0f, 0.0f},
        {-1.0f / 6.0f, 6.0f / 6.0f, 1.0f / 6.0f, 0.0f},
        {-1.0f / 6.0f, 4.0f / 6.0f, 3.0f / 6.0f, 0.0f},
        {0.0f, 0.0f, 6.0f / 6.0f, 0.0f}};

    private static void mul_matrix(Span<real3> output, float[,] mat, ReadOnlySpan<real3> pt)
    {
        for (int i = 0; i < 4; i++)
            output[i] = mat[i, 0] * pt[0] + mat[i, 1] * pt[1] + mat[i, 2] * pt[2] + mat[i, 3] * pt[3];
    }

    private static void CamullRomToCubicBezier(Span<real3> Q, List<real3> cps, int cps_size, int seg_idx)
    {
        if (cps_size == 2)
        {
            Q[0] = cps[seg_idx];
            Q[1] = cps[seg_idx] * 2.0f / 3.0f + cps[seg_idx + 1] * 1.0f / 3.0f;
            Q[2] = cps[seg_idx] * 1.0f / 3.0f + cps[seg_idx + 1] * 2.0f / 3.0f;
            Q[3] = cps[seg_idx + 1];
        }
        else
        {
            Span<real3> P = stackalloc real3[4];
            if (seg_idx == 0)
            {
                P[0] = new real3(0.0f);
                P[1] = cps[seg_idx + 0];
                P[2] = cps[seg_idx + 1];
                P[3] = cps[seg_idx + 2];
                mul_matrix(Q, toC2B0, P);
            }
            else if (seg_idx == cps_size - 2)
            {
                P[0] = cps[seg_idx - 1];
                P[1] = cps[seg_idx + 0];
                P[2] = cps[seg_idx + 1];
                P[3] = new real3(0.0f);
                mul_matrix(Q, toC2B1, P);
            }
            else
            {
                P[0] = cps[seg_idx - 1];
                P[1] = cps[seg_idx + 0];
                P[2] = cps[seg_idx + 1];
                P[3] = cps[seg_idx + 2];
                mul_matrix(Q, toC2B, P);
            }
        }
    }

    // Raw CyHair values
    public ushort[] segments_ = [];
    public float[] points_ = [];  // xyz
    public float[] thicknesses_ = [];
    public float[] transparencies_ = [];
    public float[] colors_ = [];  // rgb
    public uint flags_ = 0;
    public uint num_strands_ = 0;
    public uint total_points_ = 0;
    public int default_segments_ = -1;
    public float default_thickness_ = 0.01f;
    public float default_transparency_ = 1.0f;
    public float[] default_color_ = [0.5f, 0.5f, 0.5f];

    // Processed CyHair values
    public uint[] strand_offsets_ = [];

    // fread(ptr, n, 1, fp) == 1: all n bytes or nothing.
    private static byte[]? ReadBytes(Stream fp, long n)
    {
        byte[] buf = new byte[n];
        int total = 0;
        while (total < n)
        {
            int got = fp.Read(buf, total, (int)(n - total));
            if (got == 0)
                return null;
            total += got;
        }
        return buf;
    }

    private static float[] ToFloats(byte[] b)
    {
        float[] f = new float[b.Length / 4];
        for (int i = 0; i < f.Length; ++i)
            f[i] = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(4 * i));
        return f;
    }

    /// Load CyHair data from a file.
    public bool Load(string filename, TextWriter cout)
    {
        FileStream fp;
        try
        {
            fp = new FileStream(filename, FileMode.Open, FileAccess.Read);
        }
        catch (Exception)
        {
            return false;
        }
        using (fp)
        {
            byte[]? header = ReadBytes(fp, 128);
            if (header == null)
                return false;
            if (Encoding.ASCII.GetString(header, 0, 4) != "HAIR")
                return false;

            uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset));
            float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(offset));

            flags_ = U(12);
            default_thickness_ = F(20);
            default_transparency_ = F(24);
            default_segments_ = (int)U(16);
            default_color_[0] = F(28);
            default_color_[1] = F(32);
            default_color_[2] = F(36);

            bool has_segments = (flags_ & 0x1) != 0;
            bool has_points = (flags_ & 0x2) != 0;
            bool has_thickness = (flags_ & 0x4) != 0;
            bool has_transparency = (flags_ & 0x8) != 0;
            bool has_color = (flags_ & 0x10) != 0;

            num_strands_ = U(4);
            total_points_ = U(8);

            if (!has_points)
            {
                cout.Write("No point data in CyHair.\n");
                return false;
            }

            if ((default_segments_ < 1) && (!has_segments))
            {
                cout.Write("No valid segment information in CyHair.\n");
                return false;
            }

            // First read all strand data from a file.
            if (has_segments)
            {
                byte[]? b = ReadBytes(fp, 2L * num_strands_);
                if (b == null)
                {
                    cout.Write("Failed to read CyHair segments data.\n");
                    return false;
                }
                segments_ = new ushort[num_strands_];
                for (int i = 0; i < segments_.Length; ++i)
                    segments_[i] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2 * i));
            }

            if (has_points)
            {
                cout.Write("[CyHair] Has points.\n");
                byte[]? b = ReadBytes(fp, 12L * total_points_);
                if (b == null)
                {
                    cout.Write("Failed to read CyHair points data.\n");
                    return false;
                }
                points_ = ToFloats(b);
            }
            if (has_thickness)
            {
                cout.Write("[CyHair] Has thickness.\n");
                byte[]? b = ReadBytes(fp, 4L * total_points_);
                if (b == null)
                {
                    cout.Write("Failed to read CyHair thickness data.\n");
                    return false;
                }
                thicknesses_ = ToFloats(b);
            }

            if (has_transparency)
            {
                cout.Write("[CyHair] Has transparency.\n");
                byte[]? b = ReadBytes(fp, 4L * total_points_);
                if (b == null)
                {
                    cout.Write("Failed to read CyHair transparencies data.\n");
                    return false;
                }
                transparencies_ = ToFloats(b);
            }

            if (has_color)
            {
                cout.Write("[CyHair] Has color.\n");
                byte[]? b = ReadBytes(fp, 12L * total_points_);
                if (b == null)
                {
                    cout.Write("Failed to read CyHair colors data.\n");
                    return false;
                }
                colors_ = ToFloats(b);
            }

            // Build strand offset table.
            strand_offsets_ = new uint[num_strands_];
            if (num_strands_ > 0)
                strand_offsets_[0] = 0;
            for (int i = 1; i < num_strands_; i++)
            {
                int num_segments = segments_.Length == 0 ? default_segments_ : segments_[i - 1];
                strand_offsets_[i] = strand_offsets_[i - 1] + (uint)(num_segments + 1);
            }

            return true;
        }
    }

    /// Convert to cubic bezier curves.
    /// 4(cubic) * 3(xyz) * num_curves = vertices.size()
    /// 4(cubic) * num_curves = radiuss.size()
    /// `max_strands` limits the number of strands to convert. -1 = convert all
    /// strands.
    /// `thickness` overwrites strand thickness if it have positive value.
    /// Apply `vertex_translate` after `vertex_scale`.
    /// TODO(syoyo) return strand/segment information
    public bool ToCubicBezierCurves(List<float> vertices, List<float> radiuss, float[] vertex_scale,
                                    float[] vertex_translate, TextWriter cout, int max_strands = -1,
                                    float user_thickness = -1.0f)
    {
        if (points_.Length == 0 || strand_offsets_.Length == 0)
            return false;

        vertices.Clear();
        radiuss.Clear();

        int num_strands = (int)num_strands_;

        if ((max_strands > 0) && (max_strands < num_strands))
            num_strands = max_strands;

        cout.Write($"[Hair] Convert first {num_strands} strands from {max_strands} strands in the original " +
                   "hair data.\n");

        Span<real3> q = stackalloc real3[4];
        // Assume input points are CatmullRom spline.
        for (int i = 0; i < num_strands; i++)
        {
            if ((i % 1000) == 0)
                cout.Write($"{i} / {num_strands_}\n");

            int num_segments = segments_.Length == 0 ? default_segments_ : segments_[i];
            if (num_segments < 2)
                continue;

            List<real3> segment_points = [];
            for (int k = 0; k < num_segments; k++)
            {
                // Zup -> Yup
                long o = 3L * (strand_offsets_[i] + (uint)k);
                real3 p = new(points_[o + 0], points_[o + 2], points_[o + 1]);
                segment_points.Add(p);
            }

            // Skip both endpoints
            for (int s = 1; s < num_segments - 1; s++)
            {
                int seg_idx = s - 1;
                CamullRomToCubicBezier(q, segment_points, num_segments, seg_idx);

                for (int j = 0; j < 4; ++j)
                {
                    vertices.Add(vertex_scale[0] * q[j].x + vertex_translate[0]);
                    vertices.Add(vertex_scale[1] * q[j].y + vertex_translate[1]);
                    vertices.Add(vertex_scale[2] * q[j].z + vertex_translate[2]);
                }

                // Use user supplied thickness if given.
                // TODO(syoyo) Support per point/segment thickness
                float r = user_thickness > 0 ? user_thickness : default_thickness_;
                for (int j = 0; j < 4; ++j)
                    radiuss.Add(r);
            }
        }

        return true;
    }
}

///////////////////////////////////////////////////////////////////////////
// The above is cyhair_loader.{h,cc} basically directly; pbrt specific
// code follows...

public static class Program
{
    // C's atoi(): optional whitespace and sign, then as many digits as
    // there are; 0 if there are none.
    private static int atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            ++i;
        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            ++i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            ++i;
        return long.TryParse(s.AsSpan(start, i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                             out long v) ? (int)v : 0;
    }

    // C's atof(): the longest prefix that is a decimal floating-point
    // number; 0 if there is none.
    private static double atof(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            ++i;
        int start = i;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            ++i;
        int mantissa = i;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
            ++i;
        if (i < s.Length && s[i] == '.')
        {
            ++i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
                ++i;
        }
        if (i == mantissa || (i == mantissa + 1 && s[mantissa] == '.'))
            return 0;
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-'))
                ++j;
            if (j < s.Length && char.IsAsciiDigit(s[j]))
            {
                while (j < s.Length && char.IsAsciiDigit(s[j]))
                    ++j;
                i = j;
            }
        }
        return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    public static int Main(string[] argv)
    {
        if (argv.Length <= 1 || argv[0] == "--help" || argv[0] == "-h")
        {
            Console.Error.Write("usage: cyhair2pbrt [CyHair filename] [pbrt output filename] " +
                                "(max strands) (thickness)\n");
            return 1;
        }

        UTF8Encoding utf8 = new(false);
        using StreamWriter stdout = new(Console.OpenStandardOutput(), utf8, 1 << 16);
        StreamWriter f;
        if (argv[1] == "-")
            f = stdout;
        else
        {
            try
            {
                f = new StreamWriter(argv[1], false, utf8, 1 << 16);
            }
            catch (Exception e)
            {
                // perror()
                string err = e switch
                {
                    FileNotFoundException or DirectoryNotFoundException => "No such file or directory",
                    UnauthorizedAccessException => "Permission denied",
                    _ => e.Message
                };
                Console.Error.Write($"{argv[1]}: {err}\n");
                return 1;
            }
        }

        int max_strands = -1;         // -1 = Convert all strands
        float user_thickness = 1.0f;  // -1 = Use thickness in CyHair file.
        if (argv.Length > 2)
            max_strands = atoi(argv[2]);

        if (argv.Length > 3)
            user_thickness = (float)atof(argv[3]);

        CyHair hair = new();
        bool ret = hair.Load(argv[0], stdout);
        if (!ret)
        {
            stdout.Flush();
            Console.Error.Write($"Failed to load CyHair file [ {argv[0]} ]\n");
            return 1;
        }

        List<float> points = [];
        List<float> radiuss = [];
        float[] vertex_scale = [1.0f, 1.0f, 1.0f];
        float[] vertex_translate = [0.0f, 0.0f, 0.0f];
        ret = hair.ToCubicBezierCurves(points, radiuss, vertex_scale, vertex_translate, stdout, max_strands,
                                       user_thickness);
        if (!ret)
        {
            stdout.Flush();
            Console.Error.Write("Failed to convert CyHair data\n");
            return 1;
        }

        double[,] bounds = { { 1e30, 1e30, 1e30 }, { -1e30, -1e30, -1e30 } };
        for (int i = 0; i < points.Count / 3; ++i)
        {
            double thickness = radiuss[i];
            for (int c = 0; c < 3; ++c)
            {
                bounds[0, c] = Math.Min(bounds[0, c], (double)points[3 * i + c] - thickness);
                bounds[1, c] = Math.Max(bounds[1, c], (double)points[3 * i + c] + thickness);
            }
        }
        f.Write($"# Converted from \"{argv[0]}\" by cyhair2pbrt\n");
        f.Write($"# The number of strands = {radiuss.Count / 4}. user_thickness = {F(user_thickness)}\n");
        f.Write($"# Scene bounds: ({F(bounds[0, 0])}, {F(bounds[0, 1])}, {F(bounds[0, 2])}) - " +
                $"({F(bounds[1, 0])}, {F(bounds[1, 1])}, {F(bounds[1, 2])})\n\n\n");

        int num_curves = radiuss.Count / 4;
        for (int i = 0; i < num_curves; i++)
        {
            f.Write("Shape \"curve\" \"string type\" [ \"cylinder\" ] \"point3 P\" [ ");
            for (int j = 0; j < 12; j++)
            {
                f.Write(F(points[12 * i + j]));
                f.Write(' ');
            }
            f.Write($" ] \"float width0\" [ {F(radiuss[4 * i + 0])} ] \"float width1\" [ {F(radiuss[4 * i + 3])} ]\n");
        }

        if (f != stdout)
            f.Dispose();
        stdout.Flush();

        Console.Error.Write($"Converted {radiuss.Count / 4} strands.\n");

        return 0;
    }
}

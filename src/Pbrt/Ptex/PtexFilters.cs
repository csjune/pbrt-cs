// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of Ptex's PtexFilters.cpp, PtexSeparableFilter.h/.cpp,
// PtexSeparableKernel.h/.cpp, PtexTriangleFilter.h/.cpp and
// PtexTriangleKernel.h/.cpp (src/ext/ptex; Copyright 2014 Disney
// Enterprises, Inc.).
//
// The kernels' weight pointers are (array, offset) pairs. Ptex's apply
// functions, specialized by data type and channel count, do the same
// operations in the same order as the single loops here.

namespace Pbrt.Ptex;

public enum FilterType { Point, Bilinear, Box, Gaussian, Bicubic, BSpline, CatmullRom, Mitchell }

public struct FilterOptions(FilterType filter = FilterType.Box, bool lerp = false, float sharpness = 0,
                            bool noedgeblend = false)
{
    public FilterType filter = filter;
    public bool lerp = lerp;
    public float sharpness = sharpness;
    public bool noedgeblend = noedgeblend;
}

// PtexFilter Definition
public abstract class PtexFilter
{
    public abstract void eval(Span<float> result, int firstchan, int nchannels, int faceid, float u, float v,
                              float uw1, float vw1, float uw2, float vw2, float width = 1, float blur = 0);

    public static PtexFilter? getFilter(PtexReader tex, FilterOptions opts)
    {
        switch (tex.meshType())
        {
            case MeshType.Quad:
                return opts.filter switch
                {
                    FilterType.Point => new PtexPointFilter(tex),
                    FilterType.Bilinear => new PtexBilinearFilter(tex, opts),
                    FilterType.Gaussian => new PtexGaussianFilter(tex, opts),
                    FilterType.Bicubic => new PtexBicubicFilter(tex, opts, opts.sharpness),
                    FilterType.BSpline => new PtexBicubicFilter(tex, opts, 0f),
                    FilterType.CatmullRom => new PtexBicubicFilter(tex, opts, 1f),
                    FilterType.Mitchell => new PtexBicubicFilter(tex, opts, 2f / 3f),
                    _ => new PtexBoxFilter(tex, opts)
                };
            case MeshType.Triangle:
                return opts.filter switch
                {
                    FilterType.Point => new PtexPointFilterTri(tex),
                    _ => new PtexTriangleFilter(tex, opts)
                };
        }
        return null;
    }
}

// Point-sampling filter for rectangular textures
internal sealed class PtexPointFilter(PtexReader tx) : PtexFilter
{
    public override void eval(Span<float> result, int firstchan, int nchannels, int faceid, float u, float v,
                              float uw1, float vw1, float uw2, float vw2, float width, float blur)
    {
        if (nchannels <= 0)
            return;
        if (faceid < 0 || faceid >= tx.numFaces())
            return;
        FaceInfo f = tx.getFaceInfo(faceid);
        int resu = f.res.u(), resv = f.res.v();
        int ui = PtexUtils.clamp((int)(u * (float)resu), 0, resu - 1);
        int vi = PtexUtils.clamp((int)(v * (float)resv), 0, resv - 1);
        tx.getPixel(faceid, ui, vi, result, firstchan, nchannels);
    }
}

// Point-sampling filter for triangular textures
internal sealed class PtexPointFilterTri(PtexReader tx) : PtexFilter
{
    public override void eval(Span<float> result, int firstchan, int nchannels, int faceid, float u, float v,
                              float uw1, float vw1, float uw2, float vw2, float width, float blur)
    {
        if (nchannels <= 0)
            return;
        if (faceid < 0 || faceid >= tx.numFaces())
            return;
        FaceInfo f = tx.getFaceInfo(faceid);
        int res = f.res.u();
        int resm1 = res - 1;
        float ut = u * (float)res, vt = v * (float)res;
        int ui = PtexUtils.clamp((int)ut, 0, resm1);
        int vi = PtexUtils.clamp((int)vt, 0, resm1);
        float uf = ut - (float)ui, vf = vt - (float)vi;

        if (uf + vf <= 1.0f)
            // "even" triangles are stored in lower-left half-texture
            tx.getPixel(faceid, ui, vi, result, firstchan, nchannels);
        else
            // "odd" triangles are stored in upper-right half-texture
            tx.getPixel(faceid, resm1 - vi, resm1 - ui, result, firstchan, nchannels);
    }
}

// Separable convolution kernel
internal sealed class PtexSeparableKernel
{
    public Res res;                    // resolution that kernel was built for
    public int u, v;                   // uv offset within face data
    public int uw, vw;                 // kernel width
    public float[] ku, kv;             // kernel weights in u (from kuo) and v (from kvo)
    public int kuo, kvo;
    public const int kmax = 10;        // max kernel width
    public readonly float[] kubuff = new float[kmax];
    public readonly float[] kvbuff = new float[kmax];
    public int rot;

    public PtexSeparableKernel()
    {
        ku = kubuff;
        kv = kvbuff;
    }

    public PtexSeparableKernel(PtexSeparableKernel k) : this() => Assign(k);

    // operator=
    public void Assign(PtexSeparableKernel k) => set(k.res, k.u, k.v, k.uw, k.vw, k.ku, k.kuo, k.kv, k.kvo, k.rot);

    public void set(Res resVal, int uVal, int vVal, int uwVal, int vwVal, float[] kuVal, int kuOff, float[] kvVal,
                    int kvOff, int rotVal = 0)
    {
        res = resVal;
        u = uVal;
        v = vVal;
        uw = uwVal;
        vw = vwVal;
        // (copy through temporaries in case the source is this kernel)
        float[] tu = kuVal.AsSpan(kuOff, uw).ToArray(), tv = kvVal.AsSpan(kvOff, vw).ToArray();
        tu.CopyTo(kubuff, 0);
        tv.CopyTo(kvbuff, 0);
        ku = kubuff;
        kuo = 0;
        kv = kvbuff;
        kvo = 0;
        rot = rotVal;
    }

    public void stripZeros()
    {
        while (ku[kuo] == 0) { kuo++; u++; uw--; }
        while (ku[kuo + uw - 1] == 0) { uw--; }
        while (kv[kvo] == 0) { kvo++; v++; vw--; }
        while (kv[kvo + vw - 1] == 0) { vw--; }
    }

    public float weight() => accumulate(ku, kuo, uw) * accumulate(kv, kvo, vw);

    public void mergeL(BorderMode mode)
    {
        int w = -u;
        if (mode != BorderMode.Black)
            ku[kuo + w] += accumulate(ku, kuo, w);
        kuo += w;
        uw -= w;
        u = 0;
    }

    public void mergeR(BorderMode mode)
    {
        int w = uw + u - res.u();
        int kp = kuo + uw - w;
        if (mode != BorderMode.Black)
            ku[kp - 1] += accumulate(ku, kp, w);
        uw -= w;
    }

    public void mergeB(BorderMode mode)
    {
        int w = -v;
        if (mode != BorderMode.Black)
            kv[kvo + w] += accumulate(kv, kvo, w);
        kvo += w;
        vw -= w;
        v = 0;
    }

    public void mergeT(BorderMode mode)
    {
        int w = vw + v - res.v();
        int kp = kvo + vw - w;
        if (mode != BorderMode.Black)
            kv[kp - 1] += accumulate(kv, kp, w);
        vw -= w;
    }

    public void splitL(PtexSeparableKernel k)
    {
        // split off left piece of width w into k
        int w = -u;

        if (w < uw)
        {
            // normal case - split off a portion
            //    res  u          v  uw vw  ku  kv
            k.set(res, res.u() - w, v, w, vw, ku, kuo, kv, kvo);

            // update local
            u = 0;
            uw -= w;
            kuo += w;
        }
        else
        {
            // entire kernel is split off
            k.Assign(this);
            k.u += res.u();
            u = 0; uw = 0;
        }
    }

    public void splitR(PtexSeparableKernel k)
    {
        // split off right piece of width w into k
        int w = u + uw - res.u();

        if (w < uw)
        {
            // normal case - split off a portion
            //    res  u  v  uw vw  ku           kv
            k.set(res, 0, v, w, vw, ku, kuo + uw - w, kv, kvo);

            // update local
            uw -= w;
        }
        else
        {
            // entire kernel is split off
            k.Assign(this);
            k.u -= res.u();
            u = 0; uw = 0;
        }
    }

    public void splitB(PtexSeparableKernel k)
    {
        // split off bottom piece of width w into k
        int w = -v;
        if (w < vw)
        {
            // normal case - split off a portion
            //    res  u  v          uw vw  ku  kv
            k.set(res, u, res.v() - w, uw, w, ku, kuo, kv, kvo);

            // update local
            v = 0;
            vw -= w;
            kvo += w;
        }
        else
        {
            // entire kernel is split off
            k.Assign(this);
            k.v += res.v();
            v = 0; vw = 0;
        }
    }

    public void splitT(PtexSeparableKernel k)
    {
        // split off top piece of width w into k
        int w = v + vw - res.v();
        if (w < vw)
        {
            // normal case - split off a portion
            //    res  u  v  uw vw  ku  kv
            k.set(res, u, 0, uw, w, ku, kuo, kv, kvo + vw - w);

            // update local
            vw -= w;
        }
        else
        {
            // entire kernel is split off
            k.Assign(this);
            k.v -= res.v();
            v = 0; vw = 0;
        }
    }

    public void flipU()
    {
        u = res.u() - u - uw;
        Array.Reverse(ku, kuo, uw);
    }

    public void flipV()
    {
        v = res.v() - v - vw;
        Array.Reverse(kv, kvo, vw);
    }

    public void swapUV()
    {
        res.swapuv();
        (u, v) = (v, u);
        (uw, vw) = (vw, uw);
        (ku, kv) = (kv, ku);
        (kuo, kvo) = (kvo, kuo);
    }

    public void rotate(int rotVal)
    {
        // rotate kernel 'rot' steps ccw
        switch (rotVal & 3)
        {
            default: return;
            case 1: flipU(); swapUV(); break;
            case 2: flipU(); flipV(); break;
            case 3: flipV(); swapUV(); break;
        }
        rot = (rot + rotVal) & 3;
    }

    public bool adjustMainToSubface(int eid)
    {
        // to adjust the kernel for the subface, we must adjust the res down and offset the uv coords
        // however, if the res is already zero, we must upres the kernel first
        if (res.ulog2 == 0) upresU();
        if (res.vlog2 == 0) upresV();

        if (res.ulog2 > 0) res.ulog2--;
        if (res.vlog2 > 0) res.vlog2--;

        // offset uv coords and determine whether target subface is the primary one
        bool primary = false;
        int resu = res.u(), resv = res.v();
        switch (eid & 3)
        {
            case (int)EdgeId.Bottom:
                primary = u < resu;
                v -= resv;
                if (!primary) u -= resu;
                break;
            case (int)EdgeId.Right:
                primary = v < resv;
                if (!primary) v -= resv;
                break;
            case (int)EdgeId.Top:
                primary = u >= resu;
                if (primary) u -= resu;
                break;
            case (int)EdgeId.Left:
                primary = v >= resv;
                u -= resu;
                if (primary) v -= resv;
                break;
        }
        return primary;
    }

    public void adjustSubfaceToMain(int eid)
    {
        switch (eid & 3)
        {
            case (int)EdgeId.Bottom: v += res.v(); break;
            case (int)EdgeId.Right: break;
            case (int)EdgeId.Top: u += res.u(); break;
            case (int)EdgeId.Left: u += res.u(); v += res.v(); break;
        }
        res.ulog2++; res.vlog2++;
    }

    public void downresU()
    {
        int src = kuo, dst = kuo;

        // skip odd leading sample (if any)
        if ((u & 1) != 0)
        {
            dst++;
            src++;
            uw--;
        }

        // combine even pairs
        for (int i = uw / 2; i > 0; i--)
        {
            ku[dst++] = ku[src] + ku[src + 1];
            src += 2;
        }

        // copy odd trailing sample (if any)
        if ((uw & 1) != 0)
            ku[dst++] = ku[src++];

        // update state
        u /= 2;
        uw = dst - kuo;
        res.ulog2--;
    }

    public void downresV()
    {
        int src = kvo, dst = kvo;

        // skip odd leading sample (if any)
        if ((v & 1) != 0)
        {
            dst++;
            src++;
            vw--;
        }

        // combine even pairs
        for (int i = vw / 2; i > 0; i--)
        {
            kv[dst++] = kv[src] + kv[src + 1];
            src += 2;
        }

        // copy odd trailing sample (if any)
        if ((vw & 1) != 0)
            kv[dst++] = kv[src++];

        // update state
        v /= 2;
        vw = dst - kvo;
        res.vlog2--;
    }

    public void upresU()
    {
        int src = kuo + uw - 1;
        int dst = kuo + uw * 2 - 2;
        for (int i = uw; i > 0; i--)
        {
            ku[dst] = ku[dst + 1] = ku[src--] / 2;
            dst -= 2;
        }
        uw *= 2;
        u *= 2;
        res.ulog2++;
    }

    public void upresV()
    {
        int src = kvo + vw - 1;
        int dst = kvo + vw * 2 - 2;
        for (int i = vw; i > 0; i--)
        {
            kv[dst] = kv[dst + 1] = kv[src--] / 2;
            dst -= 2;
        }
        vw *= 2;
        v *= 2;
        res.vlog2++;
    }

    public float makeSymmetric(float initialWeight)
    {
        // downres higher-res dimension until equal
        if (res.ulog2 > res.vlog2)
        {
            do { downresU(); } while (res.ulog2 > res.vlog2);
        }
        else if (res.vlog2 > res.ulog2)
        {
            do { downresV(); } while (res.vlog2 > res.ulog2);
        }

        // truncate excess samples in longer dimension
        uw = vw = PtexUtils.min(uw, vw);

        // combine corresponding u and v samples and compute new kernel weight
        float newWeight = 0;
        for (int i = 0; i < uw; i++)
        {
            float sum = ku[kuo + i] + kv[kvo + i];
            ku[kuo + i] = kv[kvo + i] = sum;
            newWeight += sum;
        }
        newWeight *= newWeight; // equivalent to k.weight() ( = sum(ku)*sum(kv) )

        // compute scale factor to compensate for weight change
        float scale = newWeight == 0 ? 1f : initialWeight / newWeight;

        // clamp scale factor to -1..1 range
        if (scale >= 1)
        {
            // scale by 1 (i.e. do nothing)
        }
        else
        {
            if (scale < -1)
            {
                // a negative scale means the original kernel had an overall negative weight
                // after making symmetric, the kernel will always be positive
                // scale ku by -1
                // note: choice of u is arbitrary; we could have scaled u or v (but not both)
                for (int i = 0; i < uw; i++) ku[kuo + i] *= -1;
                newWeight = -newWeight;
            }
            else
            {
                // scale ku to restore initialWeight (again, choice of u instead of v is arbitrary)
                for (int i = 0; i < uw; i++) ku[kuo + i] *= scale;
                newWeight = initialWeight;
            }
        }
        return newWeight;
    }

    // Apply() / ApplyS() / ApplyN(): the data starts at the byte offset.
    public void apply(Span<float> result, byte[] data, int offset, DataType dt, int nChan, int nTxChan)
    {
        Span<float> rowResult = stackalloc float[nChan];
        int rowlen = res.u() * nTxChan;
        int datalen = uw * nTxChan;
        int rowskip = rowlen - datalen;
        int kvp = kvo;
        int p = (v * res.u() + u) * nTxChan;
        int pEnd = p + vw * rowlen;
        while (p != pEnd)
        {
            int kup = kuo;
            int pRowEnd = p + datalen;
            // just mult and copy first element
            for (int i = 0; i < nChan; i++)
                rowResult[i] = Texel.Get(data, offset, p + i, dt) * ku[kup];
            kup++;
            p += nTxChan;
            // accumulate remaining elements
            while (p != pRowEnd)
            {
                // rowResult[i] = p[i] * ku[u] for i in {0..n-1}
                for (int i = 0; i < nChan; i++)
                    rowResult[i] += Texel.Get(data, offset, p + i, dt) * ku[kup];
                kup++;
                p += nTxChan;
            }
            // result[i] += rowResult[i] * kv[v] for i in {0..n-1}
            for (int i = 0; i < nChan; i++)
                result[i] += rowResult[i] * kv[kvp];
            kvp++;
            p += rowskip;
        }
    }

    public void applyConst(Span<float> dst, byte[] data, int offset, DataType dt, int nChan) =>
        PtexUtils.applyConst(weight(), dst, data, offset, dt, nChan);

    private static float accumulate(float[] p, int start, int n)
    {
        float result = 0;
        for (int i = start; i < start + n; i++)
            result += p[i];
        return result;
    }
}

// PtexSeparableFilter Definition
internal abstract class PtexSeparableFilter(PtexReader tx, FilterOptions opts) : PtexFilter
{
    protected readonly PtexReader _tx = tx;
    protected FilterOptions _options = opts;
    private float[] _result = [];
    private float _weight;
    private int _firstChanOffset;
    private int _nchan;
    private readonly int _ntxchan = tx.numChannels();
    private readonly DataType _dt = tx.dataType();
    private readonly BorderMode _uMode = tx.uBorderMode(), _vMode = tx.vBorderMode();
    private readonly EdgeFilterMode _efm = tx.edgeFilterMode();

    // (Ptex's filters are per-thread objects; pbrt gets one per lookup. The
    // state is per call here, so a filter can be shared: eval() works on a
    // copy.)
    protected abstract void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw, Res faceRes);

    public override void eval(Span<float> result, int firstChan, int nChannels, int faceid, float u, float v,
                              float uw1, float vw1, float uw2, float vw2, float width = 1, float blur = 0)
    {
        PtexSeparableFilter f = (PtexSeparableFilter)MemberwiseClone();
        f.Eval(result, firstChan, nChannels, faceid, u, v, uw1, vw1, uw2, vw2, width, blur);
    }

    private void Eval(Span<float> result, int firstChan, int nChannels, int faceid, float u, float v,
                      float uw1, float vw1, float uw2, float vw2, float width, float blur)
    {
        // init
        if (nChannels <= 0)
            return;
        if (faceid < 0 || faceid >= _tx.numFaces())
            return;
        _firstChanOffset = firstChan * Texel.DataSize(_dt);
        _nchan = PtexUtils.min(nChannels, _ntxchan - firstChan);

        // get face info
        FaceInfo f = _tx.getFaceInfo(faceid);

        // if neighborhood is constant, just return constant value of face
        if (f.isNeighborhoodConstant())
        {
            PtexFaceData data = _tx.getData(faceid, new Res(0, 0));
            PtexUtils.ConvertToFloat(result, data.Data()!, data.Offset() + _firstChanOffset, _dt, _nchan);
            return;
        }

        // find filter width as bounding box of vectors w1 and w2
        float uw = PtexUtils.abs(uw1) + PtexUtils.abs(uw2), vw = PtexUtils.abs(vw1) + PtexUtils.abs(vw2);

        // handle border modes
        bool return_black = false;

        switch (_uMode)
        {
            case BorderMode.Clamp: u = PtexUtils.clamp(u, 0.0f, 1.0f); break;
            case BorderMode.Periodic: u = u - PtexUtils.floor(u); break;
            case BorderMode.Black: if (u <= -1.0f || u >= 2.0f) return_black = true; break;
        }

        switch (_vMode)
        {
            case BorderMode.Clamp: v = PtexUtils.clamp(v, 0.0f, 1.0f); break;
            case BorderMode.Periodic: v = v - PtexUtils.floor(v); break;
            case BorderMode.Black: if (v <= -1.0f || v >= 2.0f) return_black = true; break;
        }

        if (return_black)
        {
            result[.._nchan].Clear();
            return;
        }

        // build kernel
        PtexSeparableKernel k = new();
        if (f.isSubface())
        {
            // for a subface, build the kernel as if it were on a main face and then downres
            uw = uw * width + blur * 2.0f;
            vw = vw * width + blur * 2.0f;
            buildKernel(k, u * .5f, v * .5f, uw * .5f, vw * .5f, new Res(f.res.ulog2 + 1, f.res.vlog2 + 1));
            if (k.res.ulog2 == 0) k.upresU();
            if (k.res.vlog2 == 0) k.upresV();
            k.res.ulog2--; k.res.vlog2--;
        }
        else
        {
            uw = uw * width + blur;
            vw = vw * width + blur;
            buildKernel(k, u, v, uw, vw, f.res);
        }
        k.stripZeros();

        _weight = k.weight();

        // allocate temporary result
        _result = new float[_nchan];

        // apply to faces
        splitAndApply(k, faceid, f);

        // normalize (both for data type and cumulative kernel weight applied)
        // and output result
        float scale = 1.0f / (_weight * Texel.OneValue(_dt));
        for (int i = 0; i < _nchan; i++)
            result[i] = _result[i] * scale;
    }

    private void splitAndApply(PtexSeparableKernel k, int faceid, FaceInfo f)
    {
        // do we need to split? (i.e. does kernel span an edge?)
        bool splitR = k.u + k.uw > k.res.u(), splitL = k.u < 0;
        bool splitT = k.v + k.vw > k.res.v(), splitB = k.v < 0;

        if (_options.noedgeblend)
        {
            if (splitR) k.mergeR(_uMode);
            if (splitL) k.mergeL(_uMode);
            if (splitT) k.mergeT(_vMode);
            if (splitB) k.mergeB(_vMode);
            apply(k, faceid, f);
            return;
        }

        if (splitR || splitL || splitT || splitB)
        {
            PtexSeparableKernel ka = new(), kc = new();
            if (splitR)
            {
                if (f.adjface((int)EdgeId.Right) >= 0)
                {
                    k.splitR(ka);
                    if (splitT)
                    {
                        if (f.adjface((int)EdgeId.Top) >= 0)
                        {
                            ka.splitT(kc);
                            applyToCorner(kc, faceid, f, (int)EdgeId.Top);
                        }
                        else ka.mergeT(_vMode);
                    }
                    if (splitB)
                    {
                        if (f.adjface((int)EdgeId.Bottom) >= 0)
                        {
                            ka.splitB(kc);
                            applyToCorner(kc, faceid, f, (int)EdgeId.Right);
                        }
                        else ka.mergeB(_vMode);
                    }
                    applyAcrossEdge(ka, faceid, f, (int)EdgeId.Right);
                }
                else k.mergeR(_uMode);
            }
            if (splitL)
            {
                if (f.adjface((int)EdgeId.Left) >= 0)
                {
                    k.splitL(ka);
                    if (splitT)
                    {
                        if (f.adjface((int)EdgeId.Top) >= 0)
                        {
                            ka.splitT(kc);
                            applyToCorner(kc, faceid, f, (int)EdgeId.Left);
                        }
                        else ka.mergeT(_vMode);
                    }
                    if (splitB)
                    {
                        if (f.adjface((int)EdgeId.Bottom) >= 0)
                        {
                            ka.splitB(kc);
                            applyToCorner(kc, faceid, f, (int)EdgeId.Bottom);
                        }
                        else ka.mergeB(_vMode);
                    }
                    applyAcrossEdge(ka, faceid, f, (int)EdgeId.Left);
                }
                else k.mergeL(_uMode);
            }
            if (splitT)
            {
                if (f.adjface((int)EdgeId.Top) >= 0)
                {
                    k.splitT(ka);
                    applyAcrossEdge(ka, faceid, f, (int)EdgeId.Top);
                }
                else k.mergeT(_vMode);
            }
            if (splitB)
            {
                if (f.adjface((int)EdgeId.Bottom) >= 0)
                {
                    k.splitB(ka);
                    applyAcrossEdge(ka, faceid, f, (int)EdgeId.Bottom);
                }
                else k.mergeB(_vMode);
            }
        }

        // do local face
        apply(k, faceid, f);
    }

    private void applyAcrossEdge(PtexSeparableKernel k, int faceid, FaceInfo f, int eid)
    {
        int afid = f.adjface(eid), aeid = f.adjedge(eid);
        FaceInfo af = _tx.getFaceInfo(afid);
        int rot = eid - aeid + 2;

        // adjust uv coord and res for face/subface boundary
        bool fIsSubface = f.isSubface(), afIsSubface = af.isSubface();
        if (fIsSubface != afIsSubface)
        {
            if (afIsSubface)
            {
                // main face to subface transition
                // adjust res and offset uv coord for primary subface
                bool primary = k.adjustMainToSubface(eid);
                if (!primary)
                {
                    // advance ajacent face and edge id to secondary subface
                    int neid = (aeid + 3) % 4;
                    afid = af.adjface(neid);
                    aeid = af.adjedge(neid);
                    af = _tx.getFaceInfo(afid);
                    rot += neid - aeid + 2;
                }
            }
            else
            {
                // subface to main face transition
                // Note: the transform depends on which subface the kernel is
                // coming from.  The "primary" subface is the one the main
                // face is pointing at.  The secondary subface adjustment
                // happens to be the same as for the primary subface for the
                // next edge, so the cases can be combined.
                bool primary = af.adjface(aeid) == faceid;
                k.adjustSubfaceToMain(eid - (primary ? 1 : 0));
            }
        }

        // rotate and apply (resplit if going to a subface)
        k.rotate(rot);
        if (afIsSubface) splitAndApply(k, afid, af);
        else apply(k, afid, af);
    }

    private void applyToCorner(PtexSeparableKernel k, int faceid, FaceInfo f, int eid)
    {
        // traverse clockwise around corner vertex and gather corner faces
        int afid = faceid, aeid = eid;
        FaceInfo af = f;
        bool prevIsSubface = af.isSubface();

        const int MaxValence = 10;
        int[] cfaceId = new int[MaxValence];
        int[] cedgeId = new int[MaxValence];
        FaceInfo[] cface = new FaceInfo[MaxValence];

        int numCorners = 0;
        for (int i = 0; i < MaxValence; i++)
        {
            // advance to next face
            int prevFace = afid;
            afid = af.adjface(aeid);
            aeid = (af.adjedge(aeid) + 1) % 4;

            // we hit a boundary or reached starting face
            // note: we need to check edge id too because we might have
            // a periodic texture (w/ toroidal topology) where all 4 corners
            // are from the same face
            if (afid < 0 || (afid == faceid && aeid == eid))
            {
                numCorners = i - 2;
                break;
            }

            // record face info
            af = _tx.getFaceInfo(afid);
            cfaceId[i] = afid;
            cedgeId[i] = aeid;
            cface[i] = af;

            // check to see if corner is a subface "tee"
            bool isSubface = af.isSubface();
            if (prevIsSubface && !isSubface && af.adjface((aeid + 3) % 4) == prevFace)
            {
                // adjust the eid depending on whether we started from
                // the primary or secondary subface.
                int primary = i == 1 ? 1 : 0;
                k.adjustSubfaceToMain(eid + primary * 2);
                k.rotate(eid - aeid + 3 - primary);
                splitAndApply(k, afid, af);
                return;
            }
            prevIsSubface = isSubface;
        }

        if (numCorners == 1)
            // regular case (valence 4)
            applyToCornerFace(k, f, eid, cfaceId[1], cface[1], cedgeId[1]);
        else if (numCorners > 1)
        {
            // valence 5+, make kernel symmetric and apply equally to each face
            // first, rotate to standard orientation, u=v=0
            k.rotate(eid + 2);
            float initialWeight = k.weight();
            float newWeight = k.makeSymmetric(initialWeight);
            for (int i = 1; i <= numCorners; i++)
            {
                PtexSeparableKernel kc = new(k);
                applyToCornerFace(kc, f, 2, cfaceId[i], cface[i], cedgeId[i]);
            }
            // adjust weight for symmetrification and for additional corners
            _weight += newWeight * (float)numCorners - initialWeight;
        }
        else
            // valence 2 or 3, ignore corner face (just adjust weight)
            _weight -= k.weight();
    }

    private void applyToCornerFace(PtexSeparableKernel k, FaceInfo f, int eid, int cfid, FaceInfo cf, int ceid)
    {
        // adjust uv coord and res for face/subface boundary
        bool fIsSubface = f.isSubface(), cfIsSubface = cf.isSubface();
        if (fIsSubface != cfIsSubface)
        {
            if (cfIsSubface) k.adjustMainToSubface(eid + 3);
            else k.adjustSubfaceToMain(eid + 3);
        }

        // rotate and apply (resplit if going to a subface)
        k.rotate(eid - ceid + 2);
        if (cfIsSubface) splitAndApply(k, cfid, cf);
        else apply(k, cfid, cf);
    }

    private void apply(PtexSeparableKernel k, int faceid, FaceInfo f)
    {
        if (k.uw <= 0 || k.vw <= 0)
            return;

        // downres kernel if needed
        while (k.res.u() > f.res.u()) k.downresU();
        while (k.res.v() > f.res.v()) k.downresV();

        // get face data, and apply
        PtexFaceData dh = _tx.getData(faceid, k.res);

        if (dh.IsConstant())
        {
            k.applyConst(_result, dh.Data()!, dh.Offset() + _firstChanOffset, _dt, _nchan);
            return;
        }

        // allocate temporary result for tanvec mode (if needed)
        bool tanvecMode = _efm == EdgeFilterMode.TanVec && _nchan >= 2 && k.rot > 0;
        float[] result = tanvecMode ? new float[_nchan] : _result;

        if (dh.IsTiled())
        {
            Res tileres = dh.TileRes();
            PtexSeparableKernel kt = new() { res = tileres };
            int tileresu = tileres.u();
            int tileresv = tileres.v();
            int ntilesu = k.res.u() / tileresu;
            for (int v = k.v, vw = k.vw; vw > 0; vw -= kt.vw, v += kt.vw)
            {
                int tilev = v / tileresv;
                kt.v = v % tileresv;
                kt.vw = PtexUtils.min(vw, tileresv - kt.v);
                kt.kv = k.kv;
                kt.kvo = k.kvo + v - k.v;
                for (int u = k.u, uw = k.uw; uw > 0; uw -= kt.uw, u += kt.uw)
                {
                    int tileu = u / tileresu;
                    kt.u = u % tileresu;
                    kt.uw = PtexUtils.min(uw, tileresu - kt.u);
                    kt.ku = k.ku;
                    kt.kuo = k.kuo + u - k.u;
                    PtexFaceData? th = dh.GetTile(tilev * ntilesu + tileu);
                    if (th != null)
                    {
                        if (th.IsConstant())
                            kt.applyConst(result, th.Data()!, th.Offset() + _firstChanOffset, _dt, _nchan);
                        else
                            kt.apply(result, th.Data()!, th.Offset() + _firstChanOffset, _dt, _nchan, _ntxchan);
                    }
                }
            }
        }
        else
            k.apply(result, dh.Data()!, dh.Offset() + _firstChanOffset, _dt, _nchan, _ntxchan);

        if (tanvecMode)
        {
            // rotate tangent-space vector data and update main result
            switch (k.rot)
            {
                case 0: // rot==0 included for completeness, but tanvecMode should be false in this case
                    _result[0] += result[0];
                    _result[1] += result[1];
                    break;
                case 1:
                    _result[0] -= result[1];
                    _result[1] += result[0];
                    break;
                case 2:
                    _result[0] -= result[0];
                    _result[1] -= result[1];
                    break;
                case 3:
                    _result[0] += result[1];
                    _result[1] -= result[0];
                    break;
            }
            for (int i = 2; i < _nchan; i++) _result[i] += result[i];
        }
    }
}

// Separable filter with width=4 support.
//
// The kernel width is calculated as a multiple of 4 times the filter
// width and the texture resolution is chosen such that each kernel
// axis has between 4 and 8.
//
// For kernel widths too large to handle (because the kernel would
// extend significantly beyond both sides of the face), a special
// Hermite smoothstep is used to interpolate the two nearest 2 samples
// along the affected axis (or axes).
internal abstract class PtexWidth4Filter(PtexReader tx, FilterOptions opts) : PtexSeparableFilter(tx, opts)
{
    protected abstract float kernel(float x);

    protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw, Res faceRes)
    {
        buildKernelAxis(ref k.res.ulog2, out k.u, out k.uw, k.ku, u, uw, faceRes.ulog2);
        buildKernelAxis(ref k.res.vlog2, out k.v, out k.vw, k.kv, v, vw, faceRes.vlog2);
    }

    private static float blur(float x)
    {
        // 2-unit (x in -1..1) cubic hermite kernel
        // this produces a blur roughly 1.5 times that of the 4-unit b-spline kernel
        x = PtexUtils.abs(x);
        return x < 1.0f ? (2.0f * x - 3.0f) * x * x + 1.0f : 0.0f;
    }

    private void buildKernelAxis(ref sbyte k_ureslog2, out int k_u, out int k_uw, float[] ku, float u, float uw,
                                 int f_ureslog2)
    {
        // build 1 axis (note: "u" labels may repesent either u or v axis)

        // clamp filter width to no smaller than a texel
        uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(f_ureslog2));

        // compute desired texture res based on filter width
        k_ureslog2 = (sbyte)PtexUtils.calcResFromWidth(uw);
        int resu = 1 << k_ureslog2;
        float uwlo = 1.0f / (float)resu; // smallest filter width for this res

        // compute lerp weights (amount to blend towards next-lower res)
        float lerp2 = _options.lerp ? (uw - uwlo) / uwlo : 0;
        float lerp1 = 1.0f - lerp2;

        // adjust for large filter widths
        if (uw >= .25f)
        {
            if (uw < .5f)
            {
                k_ureslog2 = 2;
                float upix = u * 4.0f - 0.5f;
                int u1 = (int)PtexUtils.ceil(upix - 2), u2 = (int)PtexUtils.ceil(upix + 2);
                u1 = u1 & ~1;       // round down to even pair
                u2 = (u2 + 1) & ~1; // round up to even pair
                k_u = u1;
                k_uw = u2 - u1;
                float x1 = (float)u1 - upix;
                for (int i = 0; i < k_uw; i += 2)
                {
                    float xa = x1 + (float)i, xb = xa + 1.0f, xc = (xa + xb) * 0.25f;
                    // spread the filter gradually to approach the next-lower-res width
                    // at uw = .5, s = 1.0; at uw = 1, s = 0.8
                    float s = 1.0f / (uw + .75f);
                    float ka = kernel(xa), kb = kernel(xb), kc = blur(xc * s);
                    ku[i] = ka * lerp1 + kc * lerp2;
                    ku[i + 1] = kb * lerp1 + kc * lerp2;
                }
                return;
            }
            else if (uw < 1)
            {
                k_ureslog2 = 1;
                float upix = u * 2.0f - 0.5f;
                k_u = (int)PtexUtils.floor(u - .5f) * 2;
                k_uw = 4;
                float x1 = (float)k_u - upix;
                for (int i = 0; i < k_uw; i += 2)
                {
                    float xa = x1 + (float)i, xb = xa + 1.0f, xc = (xa + xb) * 0.5f;
                    // spread the filter gradually to approach the next-lower-res width
                    // at uw = .5, s = .8; at uw = 1, s = 0.5
                    float s = 1.0f / (uw * 1.5f + .5f);
                    float ka = blur(xa * s), kb = blur(xb * s), kc = blur(xc * s);
                    ku[i] = ka * lerp1 + kc * lerp2;
                    ku[i + 1] = kb * lerp1 + kc * lerp2;
                }
                return;
            }
            else
            {
                // use res 0 (1 texel per face) w/ no lerping
                // (future: use face-blended values for filter > 2)
                k_ureslog2 = 0;
                float upix = u - .5f;
                k_uw = 2;
                float ui = PtexUtils.floor(upix);
                k_u = (int)ui;
                ku[0] = blur(upix - ui);
                ku[1] = 1 - ku[0];
                return;
            }
        }

        {
            // convert from normalized coords to pixel coords
            float upix = u * (float)resu - 0.5f;
            float uwpix = uw * (float)resu;

            // find integer pixel extent: [u,v] +/- [2*uw,2*vw]
            // (kernel width is 4 times filter width)
            float dupix = 2.0f * uwpix;
            int u1 = (int)PtexUtils.ceil(upix - dupix), u2 = (int)PtexUtils.ceil(upix + dupix);

            if (lerp2 != 0)
            {
                // lerp kernel weights towards next-lower res
                // extend kernel width to cover even pairs
                u1 = u1 & ~1;
                u2 = (u2 + 1) & ~1;
                k_u = u1;
                k_uw = u2 - u1;

                // compute kernel weights
                float step = 1.0f / uwpix, x1 = ((float)u1 - upix) * (float)step;
                for (int i = 0; i < k_uw; i += 2)
                {
                    float xa = x1 + (float)i * step, xb = xa + step, xc = (xa + xb) * 0.5f;
                    float ka = kernel(xa), kb = kernel(xb), kc = kernel(xc);
                    ku[i] = ka * lerp1 + kc * lerp2;
                    ku[i + 1] = kb * lerp1 + kc * lerp2;
                }
            }
            else
            {
                k_u = u1;
                k_uw = u2 - u1;
                // compute kernel weights
                float x1 = ((float)u1 - upix) / uwpix, step = 1.0f / uwpix;
                for (int i = 0; i < k_uw; i++) ku[i] = kernel(x1 + (float)i * step);
            }
        }
    }
}

// Separable bicubic filter
internal sealed class PtexBicubicFilter : PtexWidth4Filter
{
    private readonly float[] _coeffs = new float[7]; // filter coefficients for current sharpness

    public PtexBicubicFilter(PtexReader tx, FilterOptions opts, float sharpness) : base(tx, opts)
    {
        // compute Cubic filter coefficients:
        // abs(x) < 1:
        //   1/6 * ((12 - 9*B - 6*C)*x^3 + (-18 + 12*B + 6*C)*x^2 + (6 - 2*B))
        //   == c[0]*x^3 + c[1]*x^2 + c[2]
        // abs(x) < 2:
        //   1/6 * ((-B - 6*C)*x^3 + (6*B + 30*C)*x^2 + (-12*B - 48*C)*x + (8*B + 24*C))
        //   == c[3]*x^3 + c[4]*x^2 + c[5]*x + c[6]
        // else: 0

        float B = 1.0f - sharpness; // choose C = (1-B)/2
        _coeffs[0] = 1.5f - B;
        _coeffs[1] = 1.5f * B - 2.5f;
        _coeffs[2] = 1.0f - (float)(1.0 / 3.0) * B;
        _coeffs[3] = (float)(1.0 / 3.0) * B - 0.5f;
        _coeffs[4] = 2.5f - 1.5f * B;
        _coeffs[5] = 2.0f * B - 4.0f;
        _coeffs[6] = 2.0f - (float)(2.0 / 3.0) * B;
    }

    protected override float kernel(float x)
    {
        float[] c = _coeffs;
        x = PtexUtils.abs(x);
        if (x < 1.0f) return (c[0] * x + c[1]) * x * x + c[2];
        else if (x < 2.0f) return ((c[3] * x + c[4]) * x + c[5]) * x + c[6];
        else return 0.0f;
    }
}

// Separable gaussian filter
internal sealed class PtexGaussianFilter(PtexReader tx, FilterOptions opts) : PtexWidth4Filter(tx, opts)
{
    protected override float kernel(float x) => MathF.Exp(-2.0f * x * x);
}

// Rectangular box filter.
// The box is convolved with the texels as area samples and thus the kernel function is
// actually trapezoidally shaped.
internal sealed class PtexBoxFilter(PtexReader tx, FilterOptions opts) : PtexSeparableFilter(tx, opts)
{
    protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw, Res faceRes)
    {
        // clamp filter width to no larger than 1.0
        uw = PtexUtils.min(uw, 1.0f);
        vw = PtexUtils.min(vw, 1.0f);

        // clamp filter width to no smaller than a texel
        uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(faceRes.ulog2));
        vw = PtexUtils.max(vw, PtexUtils.reciprocalPow2(faceRes.vlog2));

        // compute desired texture res based on filter width
        byte ureslog2 = (byte)PtexUtils.calcResFromWidth(uw);
        byte vreslog2 = (byte)PtexUtils.calcResFromWidth(vw);
        k.res = new Res((sbyte)ureslog2, (sbyte)vreslog2);

        // convert from normalized coords to pixel coords
        u = u * (float)k.res.u();
        v = v * (float)k.res.v();
        uw *= (float)k.res.u();
        vw *= (float)k.res.v();

        // find integer pixel extent: [u,v] +/- [uw/2,vw/2]
        // (box is 1 unit wide for a 1 unit filter period)
        float u1 = u - 0.5f * uw, u2 = u + 0.5f * uw;
        float v1 = v - 0.5f * vw, v2 = v + 0.5f * vw;
        float u1floor = PtexUtils.floor(u1), u2ceil = PtexUtils.ceil(u2);
        float v1floor = PtexUtils.floor(v1), v2ceil = PtexUtils.ceil(v2);
        k.u = (int)u1floor;
        k.v = (int)v1floor;
        k.uw = (int)u2ceil - k.u;
        k.vw = (int)v2ceil - k.v;

        // compute kernel weights along u and v directions
        computeWeights(k.ku, k.uw, 1.0f - (u1 - u1floor), 1.0f - (u2ceil - u2));
        computeWeights(k.kv, k.vw, 1.0f - (v1 - v1floor), 1.0f - (v2ceil - v2));
    }

    private static void computeWeights(float[] kernel, int size, float f1, float f2)
    {
        if (size == 1)
            kernel[0] = f1 + f2 - 1.0f;
        else
        {
            kernel[0] = f1;
            for (int i = 1; i < size - 1; i++) kernel[i] = 1.0f;
            kernel[size - 1] = f2;
        }
    }
}

// Bilinear filter (for rectangular textures)
internal sealed class PtexBilinearFilter(PtexReader tx, FilterOptions opts) : PtexSeparableFilter(tx, opts)
{
    protected override void buildKernel(PtexSeparableKernel k, float u, float v, float uw, float vw, Res faceRes)
    {
        // clamp filter width to no larger than 1.0
        uw = PtexUtils.min(uw, 1.0f);
        vw = PtexUtils.min(vw, 1.0f);

        // clamp filter width to no smaller than a texel
        uw = PtexUtils.max(uw, PtexUtils.reciprocalPow2(faceRes.ulog2));
        vw = PtexUtils.max(vw, PtexUtils.reciprocalPow2(faceRes.vlog2));

        byte ureslog2 = (byte)PtexUtils.calcResFromWidth(uw);
        byte vreslog2 = (byte)PtexUtils.calcResFromWidth(vw);
        k.res = new Res((sbyte)ureslog2, (sbyte)vreslog2);

        // convert from normalized coords to pixel coords
        float upix = u * (float)k.res.u() - 0.5f;
        float vpix = v * (float)k.res.v() - 0.5f;

        float ufloor = PtexUtils.floor(upix);
        float vfloor = PtexUtils.floor(vpix);
        k.u = (int)ufloor;
        k.v = (int)vfloor;
        k.uw = 2;
        k.vw = 2;

        // compute kernel weights
        float ufrac = upix - ufloor, vfrac = vpix - vfloor;
        k.ku[0] = 1.0f - ufrac;
        k.ku[1] = ufrac;
        k.kv[0] = 1.0f - vfrac;
        k.kv[1] = vfrac;
    }
}

// Triangle filter kernel iterator (in texel coords)
internal struct PtexTriangleKernelIter
{
    public int rowlen;              // row length (in u)
    public float u, v;              // uv center in texels
    public int u1, v1, w1;          // uvw lower bounds
    public int u2, v2, w2;          // uvw upper bounds
    public float A, B, C;           // ellipse coefficients (F = 1)
    public bool valid;              // footprint is valid (non-empty)
    public float wscale;            // amount to scale weights by (proportional to texel area)
    public float weight;            // accumulated weight

    private const float scaleG = -0.5f * (PtexTriangleKernel.Width * PtexTriangleKernel.Width);

    private static float gaussian(float x_squared) => MathF.Exp(scaleG * x_squared);

    // the ellipse equation, Q, is calculated via finite differences (Heckbert '89 pg 57)
    public void apply(Span<float> result, byte[] data, int offset, DataType dt, int nChan, int nTxChan)
    {
        float DDQ = 2.0f * A;
        for (int vi = v1; vi != v2; vi++)
        {
            int xw = rowlen - vi;
            int x1 = PtexUtils.max(u1, xw - w2);
            int x2 = PtexUtils.min(u2, xw - w1);
            float U = (float)x1 - u;
            float V = (float)vi - v;
            float DQ = A * (2.0f * U + 1.0f) + B * V;
            float Q = A * U * U + (B * U + C * V) * V;
            int p = (vi * rowlen + x1) * nTxChan;
            int pEnd = p + (x2 - x1) * nTxChan;
            for (; p < pEnd; p += nTxChan)
            {
                if (Q < 1.0f)
                {
                    float w = gaussian(Q) * wscale;
                    weight += w;
                    for (int i = 0; i < nChan; i++)
                        result[i] += Texel.Get(data, offset, p + i, dt) * w;
                }
                Q += DQ;
                DQ += DDQ;
            }
        }
    }

    public void applyConst(Span<float> dst, byte[] data, int offset, DataType dt, int nChan)
    {
        // iterate over texel locations and calculate weight as if texture weren't const
        float DDQ = 2.0f * A;
        for (int vi = v1; vi != v2; vi++)
        {
            int xw = rowlen - vi;
            int x1 = PtexUtils.max(u1, xw - w2);
            int x2 = PtexUtils.min(u2, xw - w1);
            float U = (float)x1 - u;
            float V = (float)vi - v;
            float DQ = A * (2.0f * U + 1.0f) + B * V;
            float Q = A * U * U + (B * U + C * V) * V;
            for (int x = x1; x < x2; x++)
            {
                if (Q < 1.0f)
                    weight += gaussian(Q) * wscale;
                Q += DQ;
                DQ += DDQ;
            }
        }

        // apply weight to single texel value
        PtexUtils.applyConst(weight, dst, data, offset, dt, nChan);
    }
}

// Triangle filter kernel (in normalized triangle coords)
internal struct PtexTriangleKernel
{
    // kernel width as a multiple of filter width (should be between 3 and 4)
    // for values below 3, the gaussian is not close to zero and a contour will be formed
    // larger values are more expensive (proportional to width-squared)
    public const float Width = 3.5f;

    public Res res;                 // desired resolution
    public float u, v;              // uv filter center
    public float u1, v1, w1;        // uvw lower bounds
    public float u2, v2, w2;        // uvw upper bounds
    public float A, B, C;           // ellipse coefficients (F = A*C-B*B/4)

    public void set(Res resVal, float uVal, float vVal, float u1Val, float v1Val, float w1Val, float u2Val,
                    float v2Val, float w2Val, float AVal, float BVal, float CVal)
    {
        res = resVal;
        u = uVal; v = vVal;
        u1 = u1Val; v1 = v1Val; w1 = w1Val;
        u2 = u2Val; v2 = v2Val; w2 = w2Val;
        A = AVal; B = BVal; C = CVal;
    }

    public void set(float uVal, float vVal, float u1Val, float v1Val, float w1Val, float u2Val, float v2Val,
                    float w2Val)
    {
        u = uVal; v = vVal;
        u1 = u1Val; v1 = v1Val; w1 = w1Val;
        u2 = u2Val; v2 = v2Val; w2 = w2Val;
    }

    public void setABC(float AVal, float BVal, float CVal)
    {
        A = AVal; B = BVal; C = CVal;
    }

    public void splitU(out PtexTriangleKernel ka)
    {
        ka = this;
        u1 = 0;
        ka.u2 = 0;
    }

    public void splitV(out PtexTriangleKernel ka)
    {
        ka = this;
        v1 = 0;
        ka.v2 = 0;
    }

    public void splitW(out PtexTriangleKernel ka)
    {
        ka = this;
        w1 = 0;
        ka.w2 = 0;
    }

    // rotate ellipse where u'=w, v'=u, w'=v
    // (derived by converting to Barycentric form, rotating, and converting back)
    public void rotate1() => setABC(C, 2.0f * C - B, A + C - B);

    // rotate ellipse where u'=v, v'=w, w'=u
    // (derived by converting to Barycentric form, rotating, and converting back)
    public void rotate2() => setABC(A + C - B, 2.0f * A - B, A);

    public void reorient(int eid, int aeid)
    {
        float w = 1.0f - u - v;
        switch (eid * 3 + aeid)
        {
            case 0: set(1.0f - u, -v, 1.0f - u2, -v2, 1.0f - w2, 1.0f - u1, -v1, 1.0f - w1); break;
            case 1: set(1.0f - w, 1.0f - u, 1.0f - w2, 1.0f - u2, -v2, 1.0f - w1, 1.0f - u1, -v1); rotate1(); break;
            case 2: set(-v, 1.0f - w, -v2, 1.0f - w2, 1.0f - u2, -v1, 1.0f - w1, 1.0f - u1); rotate2(); break;
            case 3: set(1.0f - v, -w, 1.0f - v2, -w2, 1.0f - u2, 1.0f - v1, -w1, 1.0f - u1); rotate2(); break;
            case 4: set(1.0f - u, 1.0f - v, 1.0f - u2, 1.0f - v2, -w2, 1.0f - u1, 1.0f - v1, -w1); break;
            case 5: set(-w, 1.0f - u, -w2, 1.0f - u2, 1.0f - v2, -w1, 1.0f - u1, 1.0f - v1); rotate1(); break;
            case 6: set(1.0f - w, -u, 1.0f - w2, -u2, 1.0f - v2, 1.0f - w1, -u1, 1.0f - v1); rotate1(); break;
            case 7: set(1.0f - v, 1.0f - w, 1.0f - v2, 1.0f - w2, -u2, 1.0f - v1, 1.0f - w1, -u1); rotate2(); break;
            case 8: set(-u, 1.0f - v, -u2, 1.0f - v2, 1.0f - w2, -u1, 1.0f - v1, 1.0f - w1); break;
        }
    }

    public void clampRes(Res fres)
    {
        res.ulog2 = (sbyte)PtexUtils.min(res.ulog2, fres.ulog2);
        res.vlog2 = res.ulog2;
    }

    public void clampExtent()
    {
        u1 = PtexUtils.max(u1, 0.0f);
        v1 = PtexUtils.max(v1, 0.0f);
        w1 = PtexUtils.max(w1, 0.0f);
        u2 = PtexUtils.min(u2, 1.0f - (v1 + w1));
        v2 = PtexUtils.min(v2, 1.0f - (w1 + u1));
        w2 = PtexUtils.min(w2, 1.0f - (u1 + v1));
    }

    public readonly void getIterators(out PtexTriangleKernelIter ke, out PtexTriangleKernelIter ko)
    {
        int resu = res.u();

        // normalize coefficients for texel units
        float Finv = 1.0f / ((float)resu * (float)resu * (A * C - 0.25f * B * B));
        float Ak = A * Finv, Bk = B * Finv, Ck = C * Finv;
        const float third = (float)(1 / 3.0);

        // build even iterator
        ke = new PtexTriangleKernelIter();
        ke.rowlen = resu;
        ke.wscale = 1.0f / ((float)resu * (float)resu);
        float scale = (float)ke.rowlen;
        ke.u = u * scale - third;
        ke.v = v * scale - third;
        ke.u1 = (int)PtexUtils.ceil(u1 * scale - third);
        ke.v1 = (int)PtexUtils.ceil(v1 * scale - third);
        ke.w1 = (int)PtexUtils.ceil(w1 * scale - third);
        ke.u2 = (int)PtexUtils.ceil(u2 * scale - third);
        ke.v2 = (int)PtexUtils.ceil(v2 * scale - third);
        ke.w2 = (int)PtexUtils.ceil(w2 * scale - third);
        ke.A = Ak; ke.B = Bk; ke.C = Ck;
        ke.valid = ke.u2 > ke.u1 && ke.v2 > ke.v1 && ke.w2 > ke.w1;
        ke.weight = 0;

        // build odd iterator: flip kernel across diagonal (u = 1-v, v = 1-u, w = -w)
        ko = new PtexTriangleKernelIter();
        ko.rowlen = ke.rowlen;
        ko.wscale = ke.wscale;
        ko.u = (1.0f - v) * scale - third;
        ko.v = (1.0f - u) * scale - third;
        ko.u1 = (int)PtexUtils.ceil((1.0f - v2) * scale - third);
        ko.v1 = (int)PtexUtils.ceil((1.0f - u2) * scale - third);
        ko.w1 = (int)PtexUtils.ceil((-w2) * scale - third);
        ko.u2 = (int)PtexUtils.ceil((1.0f - v1) * scale - third);
        ko.v2 = (int)PtexUtils.ceil((1.0f - u1) * scale - third);
        ko.w2 = (int)PtexUtils.ceil((-w1) * scale - third);
        ko.A = Ck; ko.B = Bk; ko.C = Ak;
        ko.valid = ko.u2 > ko.u1 && ko.v2 > ko.v1 && ko.w2 > ko.w1;
        ko.weight = 0;
    }
}

// PtexTriangleFilter Definition
internal sealed class PtexTriangleFilter(PtexReader tx, FilterOptions opts) : PtexFilter
{
    private readonly FilterOptions _options = opts;

    // (per-call state, as in PtexSeparableFilter)
    private sealed class State
    {
        public float[] result = [];
        public float weight;
        public int firstChanOffset, nchan, ntxchan;
        public DataType dt;
    }

    public override void eval(Span<float> result, int firstChan, int nChannels, int faceid, float u, float v,
                              float uw1, float vw1, float uw2, float vw2, float width = 1, float blur = 0)
    {
        // init
        if (nChannels <= 0)
            return;
        if (faceid < 0 || faceid >= tx.numFaces())
            return;
        State s = new() { ntxchan = tx.numChannels(), dt = tx.dataType() };
        s.firstChanOffset = firstChan * Texel.DataSize(s.dt);
        s.nchan = PtexUtils.min(nChannels, s.ntxchan - firstChan);

        // get face info
        FaceInfo f = tx.getFaceInfo(faceid);

        // if neighborhood is constant, just return constant value of face
        if (f.isNeighborhoodConstant())
        {
            PtexFaceData data = tx.getData(faceid, new Res(0, 0));
            PtexUtils.ConvertToFloat(result, data.Data()!, data.Offset() + s.firstChanOffset, s.dt, s.nchan);
            return;
        }

        // clamp u and v
        u = PtexUtils.clamp(u, 0.0f, 1.0f);
        v = PtexUtils.clamp(v, 0.0f, 1.0f);

        // build kernel
        PtexTriangleKernel k = new();
        buildKernel(ref k, u, v, uw1, vw1, uw2, vw2, width, blur, f.res);

        // accumulate the weight as we apply
        s.weight = 0;

        // allocate temporary result
        s.result = new float[s.nchan];

        // apply to faces
        splitAndApply(s, k, faceid, f);

        // normalize (both for data type and cumulative kernel weight applied)
        // and output result
        float scale = 1.0f / (s.weight * Texel.OneValue(s.dt));
        for (int i = 0; i < s.nchan; i++)
            result[i] = s.result[i] * scale;
    }

    private static float squared(float x) => x * x;

    private static void buildKernel(ref PtexTriangleKernel k, float u, float v, float uw1, float vw1, float uw2,
                                    float vw2, float width, float blur, Res faceRes)
    {
        const float sqrt3 = 1.7320508075688772f;

        // compute ellipse coefficients, A*u^2 + B*u*v + C*v^2 == AC - B^2/4
        float scaleAC = 0.25f * width * width;
        float scaleB = -2.0f * scaleAC;
        float A = (vw1 * vw1 + vw2 * vw2) * scaleAC;
        float B = (uw1 * vw1 + uw2 * vw2) * scaleB;
        float C = (uw1 * uw1 + uw2 * uw2) * scaleAC;

        // convert to cartesian domain
        float Ac = 0.75f * A;
        float Bc = (float)(sqrt3 / 2) * (B - A);
        float Cc = 0.25f * A - 0.5f * B + C;

        // compute min blur for eccentricity clamping
        const float maxEcc = 15.0f; // max eccentricity
        const float eccRatio = (maxEcc * maxEcc + 1.0f) / (maxEcc * maxEcc - 1.0f);
        float X = MathF.Sqrt(squared(Ac - Cc) + squared(Bc));
        float b_e = 0.5f * (eccRatio * X - (Ac + Cc));

        // compute min blur for texel clamping
        // (ensure that ellipse is no smaller than a texel)
        float b_t = squared(0.5f / (float)faceRes.u());

        // add blur
        float b_b = 0.25f * blur * blur;
        float b = PtexUtils.max(b_b, PtexUtils.max(b_e, b_t));
        Ac += b;
        Cc += b;

        // compute minor radius
        float m = MathF.Sqrt(2.0f * (Ac * Cc - 0.25f * Bc * Bc) / (Ac + Cc + X));

        // choose desired resolution
        int reslog2 = PtexUtils.max(0, PtexUtils.calcResFromWidth(2.0f * m));

        // convert back to triangular domain
        A = (float)(4 / 3.0) * Ac;
        B = (float)(2 / sqrt3) * Bc + A;
        C = -0.25f * A + 0.5f * B + Cc;

        // scale by kernel width
        float scale = PtexTriangleKernel.Width * PtexTriangleKernel.Width;
        A *= scale;
        B *= scale;
        C *= scale;

        // find u,v,w extents
        float uw = PtexUtils.min(MathF.Sqrt(C), 1.0f);
        float vw = PtexUtils.min(MathF.Sqrt(A), 1.0f);
        float ww = PtexUtils.min(MathF.Sqrt(A - B + C), 1.0f);

        // init kernel
        float w = 1.0f - u - v;
        k.set(new Res(reslog2, reslog2), u, v, u - uw, v - vw, w - ww, u + uw, v + vw, w + ww, A, B, C);
    }

    private void splitAndApply(State s, PtexTriangleKernel k, int faceid, FaceInfo f)
    {
        // do we need to split? if so, split kernel and apply across edge(s)
        if (k.u1 < 0 && f.adjface(2) >= 0)
        {
            k.splitU(out PtexTriangleKernel ka);
            applyAcrossEdge(s, ka, f, 2);
        }
        if (k.v1 < 0 && f.adjface(0) >= 0)
        {
            k.splitV(out PtexTriangleKernel ka);
            applyAcrossEdge(s, ka, f, 0);
        }
        if (k.w1 < 0 && f.adjface(1) >= 0)
        {
            k.splitW(out PtexTriangleKernel ka);
            applyAcrossEdge(s, ka, f, 1);
        }
        // apply to local face
        apply(s, k, faceid, f);
    }

    private void applyAcrossEdge(State s, PtexTriangleKernel k, FaceInfo f, int eid)
    {
        int afid = f.adjface(eid), aeid = f.adjedge(eid);
        FaceInfo af = tx.getFaceInfo(afid);
        k.reorient(eid, aeid);
        splitAndApply(s, k, afid, af);
    }

    private void apply(State s, PtexTriangleKernel k, int faceid, FaceInfo f)
    {
        // clamp kernel face (resolution and extent)
        k.clampRes(f.res);
        k.clampExtent();

        // build kernel iterators
        k.getIterators(out PtexTriangleKernelIter keven, out PtexTriangleKernelIter kodd);
        if (!keven.valid && !kodd.valid)
            return;

        // get face data, and apply
        PtexFaceData dh = tx.getData(faceid, k.res);

        if (keven.valid) applyIter(s, keven, dh);
        if (kodd.valid) applyIter(s, kodd, dh);
    }

    private static void applyIter(State s, PtexTriangleKernelIter k, PtexFaceData dh)
    {
        if (dh.IsConstant())
        {
            k.applyConst(s.result, dh.Data()!, dh.Offset() + s.firstChanOffset, s.dt, s.nchan);
            s.weight += k.weight;
        }
        else if (dh.IsTiled())
        {
            Res tileres = dh.TileRes();
            PtexTriangleKernelIter kt = k;
            int tileresu = tileres.u();
            int tileresv = tileres.v();
            kt.rowlen = tileresu;
            int ntilesu = k.rowlen / kt.rowlen;
            int wOffsetBase = k.rowlen - tileresu;
            for (int tilev = k.v1 / tileresv, tilevEnd = (k.v2 - 1) / tileresv; tilev <= tilevEnd; tilev++)
            {
                int vOffset = tilev * tileresv;
                kt.v = k.v - (float)vOffset;
                kt.v1 = PtexUtils.max(0, k.v1 - vOffset);
                kt.v2 = PtexUtils.min(k.v2 - vOffset, tileresv);
                for (int tileu = k.u1 / tileresu, tileuEnd = (k.u2 - 1) / tileresu; tileu <= tileuEnd; tileu++)
                {
                    int uOffset = tileu * tileresu;
                    int wOffset = wOffsetBase - uOffset - vOffset;
                    kt.u = k.u - (float)uOffset;
                    kt.u1 = PtexUtils.max(0, k.u1 - uOffset);
                    kt.u2 = PtexUtils.min(k.u2 - uOffset, tileresu);
                    kt.w1 = k.w1 - wOffset;
                    kt.w2 = k.w2 - wOffset;
                    PtexFaceData? th = dh.GetTile(tilev * ntilesu + tileu);
                    if (th != null)
                    {
                        kt.weight = 0;
                        if (th.IsConstant())
                            kt.applyConst(s.result, th.Data()!, th.Offset() + s.firstChanOffset, s.dt, s.nchan);
                        else
                            kt.apply(s.result, th.Data()!, th.Offset() + s.firstChanOffset, s.dt, s.nchan,
                                     s.ntxchan);
                        s.weight += kt.weight;
                    }
                }
            }
        }
        else
        {
            k.apply(s.result, dh.Data()!, dh.Offset() + s.firstChanOffset, s.dt, s.nchan, s.ntxchan);
            s.weight += k.weight;
        }
    }
}

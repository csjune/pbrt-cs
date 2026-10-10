// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// C# port of Ptex's PtexReader.h/.cpp and the parts of PtexCache.cpp that
// pbrt uses (src/ext/ptex; Copyright 2014 Disney Enterprises, Inc.).
//
// The reader loads levels, faces and tiles lazily and keeps them (pbrt's
// 4GB cache limit is never reached here, so nothing is purged). The meta
// data isn't read (pbrt doesn't use it). PtexFaceData's release() is
// garbage collection.

using System.IO.Compression;
using System.Text;

namespace Pbrt.Ptex;

// PtexIO.h file structures
internal struct Header
{
    public uint magic, version, meshtype, datatype;
    public int alphachan;
    public ushort nchannels, nlevels;
    public uint nfaces, extheadersize, faceinfosize, constdatasize, levelinfosize, minorversion;
    public ulong leveldatasize;
    public uint metadatazipsize, metadatamemsize;

    public const int Size = 64;

    public static Header Read(byte[] b) => new()
    {
        magic = BitConverter.ToUInt32(b, 0),
        version = BitConverter.ToUInt32(b, 4),
        meshtype = BitConverter.ToUInt32(b, 8),
        datatype = BitConverter.ToUInt32(b, 12),
        alphachan = BitConverter.ToInt32(b, 16),
        nchannels = BitConverter.ToUInt16(b, 20),
        nlevels = BitConverter.ToUInt16(b, 22),
        nfaces = BitConverter.ToUInt32(b, 24),
        extheadersize = BitConverter.ToUInt32(b, 28),
        faceinfosize = BitConverter.ToUInt32(b, 32),
        constdatasize = BitConverter.ToUInt32(b, 36),
        levelinfosize = BitConverter.ToUInt32(b, 40),
        minorversion = BitConverter.ToUInt32(b, 44),
        leveldatasize = BitConverter.ToUInt64(b, 48),
        metadatazipsize = BitConverter.ToUInt32(b, 56),
        metadatamemsize = BitConverter.ToUInt32(b, 60)
    };

    public readonly int pixelSize() => Texel.DataSize((DataType)datatype) * nchannels;
    public readonly bool hasAlpha() => alphachan >= 0 && alphachan < nchannels;
}

internal struct ExtHeader
{
    public ushort ubordermode, pad, vbordermode, edgefiltermode;
    public uint lmdheaderzipsize, lmdheadermemsize;
    public ulong lmddatasize, editdatasize, editdatapos;

    public const int Size = 40;

    public static ExtHeader Read(byte[] b) => new()
    {
        ubordermode = BitConverter.ToUInt16(b, 0),
        pad = BitConverter.ToUInt16(b, 2),
        vbordermode = BitConverter.ToUInt16(b, 4),
        edgefiltermode = BitConverter.ToUInt16(b, 6),
        lmdheaderzipsize = BitConverter.ToUInt32(b, 8),
        lmdheadermemsize = BitConverter.ToUInt32(b, 12),
        lmddatasize = BitConverter.ToUInt64(b, 16),
        editdatasize = BitConverter.ToUInt64(b, 24),
        editdatapos = BitConverter.ToUInt64(b, 32)
    };
}

internal struct LevelInfo
{
    public ulong leveldatasize;
    public uint levelheadersize, nfaces;

    public const int Size = 16;
}

internal enum Encoding { Constant, Zipped, DiffZipped, Tiled }

internal struct FaceDataHeader(uint data)
{
    public uint data = data; // bits 0..29 = blocksize, bits 30..31 = encoding

    public readonly uint blocksize() => data & 0x3fffffff;
    public readonly Encoding encoding() => (Encoding)((data >> 30) & 0x3);

    public const int Size = 4;
}

// PtexFaceData: per-face texture data; the data is at (Data, Offset).
public abstract class PtexFaceData
{
    public abstract bool IsConstant();
    public abstract Res ResVal();
    public abstract void GetPixel(int u, int v, byte[] result);
    public abstract byte[]? Data();
    public virtual int Offset() => 0;
    public abstract bool IsTiled();
    public abstract Res TileRes();
    public abstract PtexFaceData? GetTile(int tile);
}

public sealed class PtexReader
{
    private const uint Magic = 'P' | ('t' << 8) | ('e' << 16) | ('x' << 24);
    private const int BlockSize = 16384; // target block size for file I/O

    // PtexErrorHandler: where read errors go (pbrt's handler calls Error())
    private readonly Action<string>? err;
    private readonly bool premultiply;
    private bool ok = true;
    private FileStream? fp;
    private string path = "";
    private Header header;
    private ExtHeader extheader;
    private long faceinfopos, constdatapos, levelinfopos, leveldatapos, editdatapos;
    private int pixelsize;
    private byte[] constdata = [];
    private bool hasEdits;
    private FaceInfo[] faceinfo = [];
    private uint[] rfaceids = [];
    private LevelInfo[] levelinfo = [];
    private long[] levelpos = [];
    private Level?[] levels = [];
    private readonly List<(long pos, int faceid, FaceDataHeader fdh)> faceedits = [];
    private readonly Dictionary<(int faceid, ushort res), FaceData> reductions = [];
    private byte[] errorPixel = [];
    private readonly object readlock = new();

    public PtexReader(bool premultiply, Action<string>? errorHandler)
    {
        this.premultiply = premultiply;
        err = errorHandler;
    }

    // PtexTexture::open()
    public static PtexReader? Open(string path, out string error, bool premultiply = false)
    {
        PtexReader reader = new(premultiply, null);
        return reader.OpenFile(path, out error) ? reader : null;
    }

    public bool Ok => ok;

    public MeshType meshType() => (MeshType)header.meshtype;
    public DataType dataType() => (DataType)header.datatype;
    public BorderMode uBorderMode() => (BorderMode)extheader.ubordermode;
    public BorderMode vBorderMode() => (BorderMode)extheader.vbordermode;
    public EdgeFilterMode edgeFilterMode() => (EdgeFilterMode)extheader.edgefiltermode;
    public int alphaChannel() => header.alphachan;
    public int numChannels() => header.nchannels;
    public int numFaces() => (int)header.nfaces;
    public bool HasEdits() => hasEdits;
    public bool hasMipMaps() => header.nlevels > 1;
    internal DataType datatype() => (DataType)header.datatype;
    internal int nchannels() => header.nchannels;

    private static readonly FaceInfo dummy = new();

    public FaceInfo getFaceInfo(int faceid)
    {
        if (faceid >= 0 && faceid < faceinfo.Length)
            return faceinfo[faceid];
        return dummy;
    }

    // Errors while reading are reported by setError(); OpenFile() collects
    // them in the error string (as the TempErrorHandler does).
    private StringBuilder? tempError;

    private void setError(string error)
    {
        string msg = error + " PtexFile: " + path + "\n";
        if (tempError != null)
            tempError.Append(msg);
        else if (err != null)
            err(msg);
        else
            Console.Error.Write(msg);
        ok = false;
    }

    internal bool OpenFile(string pathArg, out string error)
    {
        lock (readlock)
        {
            error = "";
            path = pathArg;
            try
            {
                // (FileShare.Delete so that, as on POSIX systems, the file can
                // be deleted while the cache holds it open.)
                fp = new FileStream(pathArg, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                                    8192);
            }
            catch (Exception e)
            {
                error = "Can't open ptex file: " + pathArg + "\n" + StrError(e);
                ok = false;
                return false;
            }
            byte[] hb = new byte[Header.Size];
            readBlock(hb, 0, Header.Size);
            header = Header.Read(hb);
            if (header.magic != Magic)
            {
                error = "Not a ptex file: " + pathArg;
                ok = false;
                closeFP();
                return false;
            }
            if (header.version != 1)
            {
                error = $"Unsupported ptex file version ({header.version}): {pathArg}";
                ok = false;
                closeFP();
                return false;
            }
            if (!(header.meshtype == (uint)MeshType.Triangle || header.meshtype == (uint)MeshType.Quad))
            {
                error = $"Invalid mesh type ({header.meshtype}): {pathArg}";
                ok = false;
                closeFP();
                return false;
            }
            pixelsize = header.pixelSize();
            errorPixel = new byte[pixelsize];

            // install temp error handler to capture error (to return in error param)
            tempError = new StringBuilder();

            // read extended header
            byte[] eb = new byte[ExtHeader.Size];
            readBlock(eb, 0, (int)Math.Min((uint)ExtHeader.Size, header.extheadersize));
            extheader = ExtHeader.Read(eb);

            // compute offsets of various blocks
            long pos = Header.Size + header.extheadersize;
            faceinfopos = pos; pos += header.faceinfosize;
            constdatapos = pos; pos += header.constdatasize;
            levelinfopos = pos; pos += header.levelinfosize;
            leveldatapos = pos; pos += (long)header.leveldatasize;
            pos += header.metadatazipsize;
            pos += sizeof(ulong); // compatibility barrier
            pos += extheader.lmdheaderzipsize;
            pos += (long)extheader.lmddatasize;

            // edit data may not start immediately if additional sections have been added
            // use value from extheader if present (and > pos)
            editdatapos = Math.Max((long)extheader.editdatapos, pos);

            // read basic file info
            readFaceInfo();
            readConstData();
            readLevelInfo();
            readEditData();

            // restore error handler
            string errors = tempError.ToString();
            tempError = null;

            // an error occurred while reading the file
            if (!ok)
            {
                error = errors;
                closeFP();
                return false;
            }
            return true;
        }
    }

    private static string StrError(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "No such file or directory",
        UnauthorizedAccessException => "Permission denied",
        _ => e.Message
    };

    private void closeFP()
    {
        fp?.Dispose();
        fp = null;
    }

    private long pos;

    private void seek(long p)
    {
        if (fp == null)
            return;
        if (p != pos)
        {
            fp.Seek(p, SeekOrigin.Begin);
            pos = p;
        }
    }

    private long tell() => pos;

    private bool readBlock(byte[] data, int offset, int size, bool reportError = true)
    {
        if (fp == null || size < 0)
            return false;
        int n = fp.ReadAtLeast(data.AsSpan(offset, size), size, throwOnEndOfStream: false);
        if (n == size)
        {
            pos += size;
            return true;
        }
        // (fread() leaves the position at the end of what it read)
        pos += n;
        if (reportError)
            setError("PtexReader error: read failed (EOF)");
        return false;
    }

    private bool readZipBlock(byte[] data, int offset, int zipsize, int unzipsize)
    {
        if (zipsize < 0 || unzipsize < 0)
            return false;
        // (zlib inflates the blocks as they're read; this reads them all
        // and inflates them.)
        byte[] zipped = new byte[zipsize];
        int read = 0;
        while (zipsize - read > 0)
        {
            int size = Math.Min(zipsize - read, BlockSize);
            if (!readBlock(zipped, read, size))
                break;
            read += size;
        }
        int total = 0;
        try
        {
            using ZLibStream z = new(new MemoryStream(zipped, 0, read), CompressionMode.Decompress);
            total = z.ReadAtLeast(data.AsSpan(offset, unzipsize), unzipsize, throwOnEndOfStream: false);
        }
        catch (InvalidDataException)
        {
            setError("PtexReader error: unzip failed, file corrupt");
            return false;
        }
        return total == unzipsize;
    }

    private void readFaceInfo()
    {
        // read compressed face info block
        seek(faceinfopos);
        int nfaces = (int)header.nfaces;
        byte[] b = new byte[FaceInfo.Size * nfaces];
        readZipBlock(b, 0, (int)header.faceinfosize, FaceInfo.Size * nfaces);
        faceinfo = new FaceInfo[nfaces];
        for (int i = 0; i < nfaces; i++)
            faceinfo[i] = FaceInfo.Read(b.AsSpan(FaceInfo.Size * i, FaceInfo.Size));

        // generate rfaceids
        rfaceids = new uint[nfaces];
        PtexUtils.genRfaceids(faceinfo, rfaceids, new uint[nfaces]);
    }

    private void readLevelInfo()
    {
        // read level info block
        seek(levelinfopos);
        byte[] b = new byte[LevelInfo.Size * header.nlevels];
        readBlock(b, 0, b.Length);
        levelinfo = new LevelInfo[header.nlevels];
        for (int i = 0; i < header.nlevels; i++)
            levelinfo[i] = new LevelInfo
            {
                leveldatasize = BitConverter.ToUInt64(b, 16 * i),
                levelheadersize = BitConverter.ToUInt32(b, 16 * i + 8),
                nfaces = BitConverter.ToUInt32(b, 16 * i + 12)
            };

        // initialize related data
        levels = new Level?[header.nlevels];
        levelpos = new long[header.nlevels];
        long p = leveldatapos;
        for (int i = 0; i < header.nlevels; i++)
        {
            levelpos[i] = p;
            p += (long)levelinfo[i].leveldatasize;
        }
    }

    private void readConstData()
    {
        // read compressed constant data block
        seek(constdatapos);
        int size = pixelsize * (int)header.nfaces;
        constdata = new byte[size];
        readZipBlock(constdata, 0, (int)header.constdatasize, size);
        if (premultiply && header.hasAlpha())
            PtexUtils.multalpha(constdata, 0, (int)header.nfaces, datatype(), header.nchannels, header.alphachan);
    }

    private void readEditData()
    {
        // determine file range to scan for edits
        long p = editdatapos, endpos;
        if (extheader.editdatapos > 0)
            // newer files record the edit data position and size in the extheader
            // note: position will be non-zero even if size is zero
            endpos = p + (long)extheader.editdatasize;
        else
            // must have an older file, just read until EOF
            endpos = long.MaxValue;

        byte[] b = new byte[5];
        while (p < endpos)
        {
            seek(p);
            // read the edit data header
            if (!readBlock(b, 0, 1, false))
                break;
            byte edittype = b[0];
            if (!readBlock(b, 1, 4, false))
                break;
            uint editsize = BitConverter.ToUInt32(b, 1);
            if (editsize == 0)
                break;
            hasEdits = true;
            p = tell() + editsize;
            if (edittype == 0)
                readEditFaceData();
            // (et_editmetadata: pbrt doesn't read the meta data)
        }
    }

    private void readEditFaceData()
    {
        // read header
        byte[] efdh = new byte[28];
        if (!readBlock(efdh, 0, efdh.Length))
            return;

        // update face info
        int faceid = (int)BitConverter.ToUInt32(efdh, 0);
        if (faceid < 0 || faceid >= header.nfaces)
            return;
        FaceInfo f = FaceInfo.Read(efdh.AsSpan(4, FaceInfo.Size));
        f.flags |= FaceInfo.flag_hasedits;
        faceinfo[faceid] = f;

        // read const value now
        if (!readBlock(constdata, pixelsize * faceid, pixelsize))
            return;
        if (premultiply && header.hasAlpha())
            PtexUtils.multalpha(constdata, pixelsize * faceid, 1, datatype(), header.nchannels, header.alphachan);

        // update header info for remaining data
        if (!f.isConstant())
            faceedits.Add((tell(), faceid, new FaceDataHeader(BitConverter.ToUInt32(efdh, 24))));
    }

    // Level
    private sealed class Level(int nfaces)
    {
        public readonly FaceDataHeader[] fdh = new FaceDataHeader[nfaces];
        public readonly long[] offsets = new long[nfaces];
        public readonly FaceData?[] faces = new FaceData?[nfaces];
    }

    private Level getLevel(int levelid)
    {
        Level? level = Volatile.Read(ref levels[levelid]);
        if (level == null)
            level = readLevel(levelid);
        return level;
    }

    private Level readLevel(int levelid)
    {
        // get read lock and make sure we still need to read
        lock (readlock)
        {
            if (levels[levelid] is Level l0)
                return l0;

            // go ahead and read the level
            LevelInfo l = levelinfo[levelid];

            // keep new level local until finished
            Level newlevel = new((int)l.nfaces);
            seek(levelpos[levelid]);
            byte[] b = new byte[FaceDataHeader.Size * l.nfaces];
            readZipBlock(b, 0, (int)l.levelheadersize, b.Length);
            for (int i = 0; i < l.nfaces; i++)
                newlevel.fdh[i] = new FaceDataHeader(BitConverter.ToUInt32(b, 4 * i));
            computeOffsets(tell(), newlevel.fdh, newlevel.offsets);

            // apply edits (if any) to level 0
            if (levelid == 0)
            {
                foreach (var e in faceedits)
                {
                    newlevel.fdh[e.faceid] = e.fdh;
                    newlevel.offsets[e.faceid] = e.pos;
                }
            }

            Volatile.Write(ref levels[levelid], newlevel);
            return newlevel;
        }
    }

    private static void computeOffsets(long p, FaceDataHeader[] fdh, long[] offsets)
    {
        for (int i = 0; i < fdh.Length; i++)
        {
            offsets[i] = p;
            p += fdh[i].blocksize();
        }
    }

    private FaceData getFace(int levelid, Level level, int faceid, Res res)
    {
        FaceData? face = Volatile.Read(ref level.faces[faceid]);
        if (face == null)
            face = readFaceData(level.offsets[faceid], level.fdh[faceid], res, levelid, ref level.faces[faceid]);
        return face;
    }

    private FaceData readFaceData(long p, FaceDataHeader fdh, Res res, int levelid, ref FaceData? face)
    {
        lock (readlock)
        {
            if (face != null)
                return face;

            // keep new face local until fully initialized
            FaceData? newface = null;

            seek(p);
            switch (fdh.encoding())
            {
                case Encoding.Constant:
                {
                    ConstantFace cf = new(pixelsize);
                    newface = cf;
                    readBlock(cf.data, 0, pixelsize);
                    if (levelid == 0 && premultiply && header.hasAlpha())
                        PtexUtils.multalpha(cf.data, 0, 1, datatype(), header.nchannels, header.alphachan);
                    break;
                }
                case Encoding.Tiled:
                {
                    byte[] b = new byte[6];
                    readBlock(b, 0, 2);
                    Res tileres = Res.FromValue(BitConverter.ToUInt16(b, 0));
                    readBlock(b, 2, 4);
                    uint tileheadersize = BitConverter.ToUInt32(b, 2);
                    TiledFace tf = new(this, res, tileres, levelid);
                    newface = tf;
                    byte[] hb = new byte[FaceDataHeader.Size * tf.ntiles];
                    readZipBlock(hb, 0, (int)tileheadersize, hb.Length);
                    for (int i = 0; i < tf.ntiles; i++)
                        tf.fdh[i] = new FaceDataHeader(BitConverter.ToUInt32(hb, 4 * i));
                    computeOffsets(tell(), tf.fdh, tf.offsets);
                    break;
                }
                case Encoding.Zipped:
                case Encoding.DiffZipped:
                {
                    int uw = res.u(), vw = res.v();
                    int npixels = uw * vw;
                    int unpackedSize = pixelsize * npixels;
                    PackedFace pf = new(res, pixelsize, unpackedSize);
                    newface = pf;
                    byte[] tmp = new byte[unpackedSize];
                    readZipBlock(tmp, 0, (int)fdh.blocksize(), unpackedSize);
                    if (fdh.encoding() == Encoding.DiffZipped)
                        PtexUtils.decodeDifference(tmp, unpackedSize, datatype());
                    PtexUtils.interleave(tmp, 0, uw * Texel.DataSize(datatype()), uw, vw, pf.data, 0, uw * pixelsize,
                                         datatype(), header.nchannels);
                    if (levelid == 0 && premultiply && header.hasAlpha())
                        PtexUtils.multalpha(pf.data, 0, npixels, datatype(), header.nchannels, header.alphachan);
                    break;
                }
            }

            newface ??= errorData();

            Volatile.Write(ref face, newface);
            return newface;
        }
    }

    private FaceData errorData() => new ErrorFace(errorPixel, pixelsize);

    public PtexFaceData getData(int faceid)
    {
        if (!ok || faceid < 0 || faceid >= header.nfaces)
            return errorData();

        FaceInfo fi = faceinfo[faceid];
        if (fi.isConstant() || fi.res == new Res(0, 0))
            return new ConstDataPtr(constdata, faceid * pixelsize, pixelsize);

        // get level zero (full) res face
        Level level = getLevel(0);
        return getFace(0, level, faceid, fi.res);
    }

    public PtexFaceData getData(int faceid, Res res)
    {
        if (!ok || faceid < 0 || faceid >= header.nfaces)
            return errorData();

        FaceInfo fi = faceinfo[faceid];
        if (fi.isConstant() || res == new Res(0, 0))
            return new ConstDataPtr(constdata, faceid * pixelsize, pixelsize);

        // determine how many reduction levels are needed
        int redu = fi.res.ulog2 - res.ulog2, redv = fi.res.vlog2 - res.vlog2;

        if (redu == 0 && redv == 0)
        {
            // no reduction - get level zero (full) res face
            Level level = getLevel(0);
            return getFace(0, level, faceid, res);
        }

        if (redu == redv && !fi.hasEdits())
        {
            // reduction is symmetric and non-negative
            // and face has no edits => access data from reduction level (if present)
            int levelid = redu;
            if (levelid < levels.Length)
            {
                Level level = getLevel(levelid);

                // get reduction face id
                int rfaceid = (int)rfaceids[faceid];

                // get the face data (if present)
                if (rfaceid < level.faces.Length)
                    return getFace(levelid, level, rfaceid, res);
            }
        }

        // dynamic reduction required - look in dynamic reduction cache
        var key = (faceid, res.val());
        lock (reductions)
            if (reductions.TryGetValue(key, out FaceData? face))
                return face;

        // not found,  generate new reduction
        FaceData newface;

        if (res.ulog2 < 0 || res.vlog2 < 0)
        {
            Console.Error.WriteLine("PtexReader::getData - reductions below 1 pixel not supported");
            newface = errorData();
        }
        else if (redu < 0 || redv < 0)
        {
            Console.Error.WriteLine("PtexReader::getData - enlargements not supported");
            newface = errorData();
        }
        else if (header.meshtype == (uint)MeshType.Triangle)
        {
            if (redu != redv)
            {
                Console.Error.WriteLine("PtexReader::getData - anisotropic reductions not supported for triangle mesh");
                newface = errorData();
            }
            else
            {
                FaceData src = (FaceData)getData(faceid, new Res(res.ulog2 + 1, res.vlog2 + 1));
                newface = src.reduce(this, res, PtexUtils.reduceTri);
            }
        }
        else
        {
            // determine which direction to blend
            bool blendu;
            if (redu == redv)
                // for symmetric face blends, alternate u and v blending
                blendu = (res.ulog2 & 1) != 0;
            else
                blendu = redu > redv;

            if (blendu)
            {
                // get next-higher u-res and reduce in u
                FaceData src = (FaceData)getData(faceid, new Res(res.ulog2 + 1, res.vlog2));
                newface = src.reduce(this, res, PtexUtils.reduceu);
            }
            else
            {
                // get next-higher v-res and reduce in v
                FaceData src = (FaceData)getData(faceid, new Res(res.ulog2, res.vlog2 + 1));
                newface = src.reduce(this, res, PtexUtils.reducev);
            }
        }

        lock (reductions)
        {
            if (reductions.TryGetValue(key, out FaceData? existing))
                return existing;
            reductions[key] = newface;
            return newface;
        }
    }

    public void getPixel(int faceid, int u, int v, Span<float> result, int firstchan, int nchannelsArg)
    {
        result[..nchannelsArg].Clear();

        // clip nchannels against actual number available
        nchannelsArg = Math.Min(nchannelsArg, header.nchannels - firstchan);
        if (nchannelsArg <= 0)
            return;

        // get raw pixel data
        PtexFaceData data = getData(faceid);
        byte[] pixel = new byte[pixelsize];
        data.GetPixel(u, v, pixel);

        // adjust for firstchan offset
        int datasize = Texel.DataSize(datatype());

        // convert/copy to result as needed
        PtexUtils.ConvertToFloat(result, pixel, datasize * firstchan, datatype(), nchannelsArg);
    }

    // FaceData
    internal abstract class FaceData(Res res) : PtexFaceData
    {
        protected Res _res = res;

        public override Res ResVal() => _res;

        public abstract FaceData reduce(PtexReader r, Res newres, PtexUtils.ReduceFn reducefn);
    }

    internal class PackedFace(Res res, int pixelsize, int size) : FaceData(res)
    {
        protected readonly int _pixelsize = pixelsize;
        public readonly byte[] data = new byte[size];

        public override bool IsConstant() => false;

        public override void GetPixel(int u, int v, byte[] result) =>
            Buffer.BlockCopy(data, (v * _res.u() + u) * _pixelsize, result, 0, _pixelsize);

        public override byte[]? Data() => data;
        public override bool IsTiled() => false;
        public override Res TileRes() => _res;
        public override PtexFaceData? GetTile(int tile) => null;

        public override FaceData reduce(PtexReader r, Res newres, PtexUtils.ReduceFn reducefn)
        {
            // allocate a new face and reduce image
            DataType dt = r.datatype();
            int nchan = r.nchannels();
            int memsize = _pixelsize * newres.size();
            PackedFace pf = new(newres, _pixelsize, memsize);
            // reduce and copy into new face
            reducefn(data, 0, _pixelsize * _res.u(), _res.u(), _res.v(), pf.data, 0, _pixelsize * newres.u(), dt,
                     nchan);
            return pf;
        }
    }

    internal class ConstantFace(int pixelsize) : PackedFace(new Res(0, 0), pixelsize, pixelsize)
    {
        public override bool IsConstant() => true;

        public override void GetPixel(int u, int v, byte[] result) => Buffer.BlockCopy(data, 0, result, 0, _pixelsize);

        public override FaceData reduce(PtexReader r, Res newres, PtexUtils.ReduceFn reducefn)
        {
            // must make a new constant face (even though it's identical to this one)
            // because it will be owned by a different reduction level
            // and will therefore have a different parent
            ConstantFace pf = new(_pixelsize);
            Buffer.BlockCopy(data, 0, pf.data, 0, _pixelsize);
            return pf;
        }
    }

    internal sealed class ErrorFace : ConstantFace
    {
        public ErrorFace(byte[] errorPixel, int pixelsize) : base(pixelsize) =>
            Buffer.BlockCopy(errorPixel, 0, data, 0, pixelsize);
    }

    internal sealed class ConstDataPtr(byte[] data, int offset, int pixelsize) : FaceData(new Res(0, 0))
    {
        public override Res ResVal() => new(0, 0);
        public override bool IsConstant() => true;
        public override void GetPixel(int u, int v, byte[] result) => Buffer.BlockCopy(data, offset, result, 0, pixelsize);
        public override byte[]? Data() => data;
        public override int Offset() => offset;
        public override bool IsTiled() => false;
        public override Res TileRes() => new(0, 0);
        public override PtexFaceData? GetTile(int tile) => null;

        // (pbrt never reduces a ConstDataPtr; it's not a FaceData in Ptex.)
        public override FaceData reduce(PtexReader r, Res newres, PtexUtils.ReduceFn reducefn) =>
            throw new InvalidOperationException();
    }

    internal abstract class TiledFaceBase : FaceData
    {
        protected readonly PtexReader _reader;
        protected readonly Res _tileres;
        protected readonly DataType _dt;
        protected readonly int _nchan;
        public readonly int ntilesu, ntilesv, ntiles;
        protected readonly int _pixelsize;
        protected readonly FaceData?[] _tiles;

        protected TiledFaceBase(PtexReader reader, Res res, Res tileres) : base(res)
        {
            _reader = reader;
            _tileres = tileres;
            _dt = reader.datatype();
            _nchan = reader.nchannels();
            _pixelsize = Texel.DataSize(_dt) * _nchan;
            ntilesu = _res.ntilesu(tileres);
            ntilesv = _res.ntilesv(tileres);
            ntiles = ntilesu * ntilesv;
            _tiles = new FaceData?[ntiles];
        }

        public Res tileres() => _tileres;
        public override bool IsConstant() => false;
        public override byte[]? Data() => null;
        public override bool IsTiled() => true;
        public override Res TileRes() => _tileres;

        public override void GetPixel(int ui, int vi, byte[] result)
        {
            int tileu = ui >> _tileres.ulog2;
            int tilev = vi >> _tileres.vlog2;
            PtexFaceData tile = GetTile(tilev * ntilesu + tileu)!;
            tile.GetPixel(ui - (tileu << _tileres.ulog2), vi - (tilev << _tileres.vlog2), result);
        }

        private static bool SameConst(PtexFaceData a, PtexFaceData b, int pixelsize) =>
            a.Data()!.AsSpan(a.Offset(), pixelsize).SequenceEqual(b.Data()!.AsSpan(b.Offset(), pixelsize));

        public override FaceData reduce(PtexReader r, Res newres, PtexUtils.ReduceFn reducefn)
        {
            // keep new face local until fully initialized
            FaceData newface;

            // don't tile triangle reductions (too complicated)
            Res newtileres;
            bool isTriangle = r.header.meshtype == (uint)MeshType.Triangle;
            if (isTriangle)
                newtileres = newres;
            else
            {
                // propagate the tile res to the reduction
                newtileres = _tileres;
                // but make sure tile isn't larger than the new face!
                if (newtileres.ulog2 > newres.ulog2) newtileres.ulog2 = newres.ulog2;
                if (newtileres.vlog2 > newres.vlog2) newtileres.vlog2 = newres.vlog2;
            }

            // determine how many tiles we will have on the reduction
            int newntiles = newres.ntiles(newtileres);

            if (newntiles == 1)
            {
                // no need to keep tiling, reduce tiles into a single face
                // first, get all tiles and check if they are constant (with the same value)
                PtexFaceData[] tiles = new PtexFaceData[ntiles];
                bool allConstant = true;
                for (int i = 0; i < ntiles; i++)
                {
                    PtexFaceData tile = tiles[i] = GetTile(i)!;
                    allConstant = allConstant && tile.IsConstant() && (i == 0 || SameConst(tiles[0], tile, _pixelsize));
                }
                if (allConstant)
                {
                    // allocate a new constant face
                    ConstantFace cf = new(_pixelsize);
                    Buffer.BlockCopy(tiles[0].Data()!, tiles[0].Offset(), cf.data, 0, _pixelsize);
                    newface = cf;
                }
                else if (isTriangle)
                {
                    // reassemble all tiles into temporary contiguous image
                    // (triangle reduction doesn't work on tiles)
                    int tileures = _tileres.u();
                    int tilevres = _tileres.v();
                    int sstride = _pixelsize * tileures;
                    int dstride = sstride * ntilesu;
                    int dstepv = dstride * tilevres - sstride * (ntilesu - 1);

                    byte[] tmp = new byte[ntiles * _tileres.size() * _pixelsize];
                    int tmpptr = 0;
                    for (int i = 0; i < ntiles;)
                    {
                        PtexFaceData tile = tiles[i];
                        if (tile.IsConstant())
                            PtexUtils.fill(tile.Data()!, tile.Offset(), tmp, tmpptr, dstride, tileures, tilevres,
                                           _pixelsize);
                        else
                            PtexUtils.copy(tile.Data()!, tile.Offset(), sstride, tmp, tmpptr, dstride, tilevres,
                                           sstride);
                        i++;
                        tmpptr += (i % ntilesu) != 0 ? sstride : dstepv;
                    }

                    // allocate a new packed face
                    int memsize = _pixelsize * newres.size();
                    PackedFace pf = new(newres, _pixelsize, memsize);
                    // reduce and copy into new face
                    reducefn(tmp, 0, _pixelsize * _res.u(), _res.u(), _res.v(), pf.data, 0, _pixelsize * newres.u(),
                             _dt, _nchan);
                    newface = pf;
                }
                else
                {
                    // allocate a new packed face
                    int memsize = _pixelsize * newres.size();
                    PackedFace pf = new(newres, _pixelsize, memsize);

                    int tileures = _tileres.u();
                    int tilevres = _tileres.v();
                    int sstride = _pixelsize * tileures;
                    int dstride = _pixelsize * newres.u();
                    int dstepu = dstride / ntilesu;
                    int dstepv = dstride * newres.v() / ntilesv - dstepu * (ntilesu - 1);

                    int dst = 0;
                    for (int i = 0; i < ntiles;)
                    {
                        PtexFaceData tile = tiles[i];
                        if (tile.IsConstant())
                            PtexUtils.fill(tile.Data()!, tile.Offset(), pf.data, dst, dstride, newres.u() / ntilesu,
                                           newres.v() / ntilesv, _pixelsize);
                        else
                            reducefn(tile.Data()!, tile.Offset(), sstride, tileures, tilevres, pf.data, dst, dstride,
                                     _dt, _nchan);
                        i++;
                        dst += (i % ntilesu) != 0 ? dstepu : dstepv;
                    }
                    newface = pf;
                }
            }
            else
                // otherwise, tile the reduced face
                newface = new TiledReducedFace(_reader, newres, newtileres, this, reducefn);
            return newface;
        }

        internal static bool SameConstant(PtexFaceData a, PtexFaceData b, int pixelsize) => SameConst(a, b, pixelsize);
    }

    internal sealed class TiledFace : TiledFaceBase
    {
        private readonly int _levelid;
        public readonly FaceDataHeader[] fdh;
        public readonly long[] offsets;

        public TiledFace(PtexReader reader, Res res, Res tileres, int levelid) : base(reader, res, tileres)
        {
            _levelid = levelid;
            fdh = new FaceDataHeader[ntiles];
            offsets = new long[ntiles];
        }

        public override PtexFaceData? GetTile(int tile)
        {
            FaceData? f = Volatile.Read(ref _tiles[tile]);
            if (f == null)
                f = _reader.readFaceData(offsets[tile], fdh[tile], _tileres, _levelid, ref _tiles[tile]);
            return f;
        }
    }

    internal sealed class TiledReducedFace(PtexReader reader, Res res, Res tileres, TiledFaceBase parentface,
                                           PtexUtils.ReduceFn reducefn) : TiledFaceBase(reader, res, tileres)
    {
        public override PtexFaceData? GetTile(int tile)
        {
            FaceData? face = Volatile.Read(ref _tiles[tile]);
            if (face != null)
                return face;

            // first, get all parent tiles for this tile
            // and check if they are constant (with the same value)
            int pntilesu = parentface.ntilesu;
            int pntilesv = parentface.ntilesv;
            int nu = pntilesu / ntilesu; // num parent tiles for this tile in u dir
            int nv = pntilesv / ntilesv; // num parent tiles for this tile in v dir

            int ntilesval = nu * nv; // num parent tiles for this tile
            PtexFaceData[] tiles = new PtexFaceData[ntilesval];
            bool allConstant = true;
            int ptile = (tile / ntilesu) * nv * pntilesu + (tile % ntilesu) * nu;
            for (int i = 0; i < ntilesval;)
            {
                PtexFaceData tileval = tiles[i] = parentface.GetTile(ptile)!;
                allConstant = allConstant && tileval.IsConstant() &&
                              (i == 0 || SameConstant(tiles[0], tileval, _pixelsize));
                i++;
                ptile += (i % nu) != 0 ? 1 : pntilesu - nu + 1;
            }

            FaceData newface;
            if (allConstant)
            {
                // allocate a new constant face
                ConstantFace cf = new(_pixelsize);
                Buffer.BlockCopy(tiles[0].Data()!, tiles[0].Offset(), cf.data, 0, _pixelsize);
                newface = cf;
            }
            else
            {
                // allocate a new packed face for the tile
                int memsize = _pixelsize * _tileres.size();
                PackedFace pf = new(_tileres, _pixelsize, memsize);

                // generate reduction from parent tiles
                int ptileures = parentface.tileres().u();
                int ptilevres = parentface.tileres().v();
                int sstride = ptileures * _pixelsize;
                int dstride = _tileres.u() * _pixelsize;
                int dstepu = dstride / nu;
                int dstepv = dstride * _tileres.v() / nv - dstepu * (nu - 1);

                int dst = 0;
                for (int i = 0; i < ntilesval;)
                {
                    PtexFaceData tileval = tiles[i];
                    if (tileval.IsConstant())
                        PtexUtils.fill(tileval.Data()!, tileval.Offset(), pf.data, dst, dstride, _tileres.u() / nu,
                                       _tileres.v() / nv, _pixelsize);
                    else
                        reducefn(tileval.Data()!, tileval.Offset(), sstride, ptileures, ptilevres, pf.data, dst,
                                 dstride, _dt, _nchan);
                    i++;
                    dst += (i % nu) != 0 ? dstepu : dstepv;
                }
                newface = pf;
            }

            FaceData? prev = Interlocked.CompareExchange(ref _tiles[tile], newface, null);
            return prev ?? newface;
        }
    }
}

// PtexCache: the readers by filename (pbrt's cache never purges, with its
// 4GB memory limit and 100 files here)
public sealed class PtexCache(bool premultiply, Action<string>? errorHandler)
{
    private readonly Dictionary<string, PtexReader> files = [];

    public PtexReader? Get(string filename, out string error)
    {
        error = "";
        PtexReader? reader;
        lock (files)
        {
            if (files.TryGetValue(filename, out reader))
                return reader.Ok ? reader : null;
            reader = new PtexReader(premultiply, errorHandler);
            reader.OpenFile(filename, out error);
            files[filename] = reader;
        }
        return reader.Ok ? reader : null;
    }
}

#!/usr/bin/env python3
# Writes the DWAA/DWAB and multi-part OpenEXR files that ExrDwaTests reads,
# using the OpenEXR Python bindings (pip install OpenEXR numpy; written with
# OpenEXR 3.5.2). The expected pixel values in the tests were checked against
# pbrt-v4's C++ imgtool (OpenEXR 3.3.1) reading these files.
#
# usage: python3 gen-exr-dwa.py <output directory>
import os, sys
import numpy as np
import OpenEXR

out = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
r = np.random.default_rng(12345)


def window(x0, y0, w, h):
    return (np.array([x0, y0], np.int32), np.array([x0 + w - 1, y0 + h - 1], np.int32))


def smooth(h, w, s):
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    return (0.5 + 0.45 * np.sin(x * 0.21 + s) * np.cos(y * 0.13 - s)).astype(np.float32)


# RGB (color space converted together), A (RLE) and N.X (zlib), half
h, w = 29, 37
OpenEXR.File({'compression': OpenEXR.DWAA_COMPRESSION, 'type': OpenEXR.scanlineimage},
             {'R': smooth(h, w, 0).astype(np.float16), 'G': smooth(h, w, 1).astype(np.float16),
              'B': (r.random((h, w)) * 3).astype(np.float16),
              'A': np.where(r.random((h, w)) < 0.5, 1.0, 0.25).astype(np.float16),
              'N.X': r.normal(0, 1, (h, w)).astype(np.float16)}).write(os.path.join(out, 'dwaa-rgba.exr'))

# Float Y with high dynamic range, constant and zero blocks
h, w = 40, 70
y = np.exp(r.normal(0, 2, (h, w))).astype(np.float32)
y[:16, :24] = 0.75
y[16:24, 40:] = 0
y[30:, :] = -0.0
OpenEXR.File({'compression': OpenEXR.DWAB_COMPRESSION, 'type': OpenEXR.scanlineimage,
              'dwaCompressionLevel': 0.0},
             {'Y': y}).write(os.path.join(out, 'dwab-y.exr'))

# Tiled, with an offset data window, two layers of float RGB
h, w = 33, 45
td = OpenEXR.TileDescription()
td.xSize, td.ySize = 16, 24
OpenEXR.File({'compression': OpenEXR.DWAA_COMPRESSION, 'type': OpenEXR.tiledimage, 'tiles': td,
              'dataWindow': window(-5, 7, w, h), 'displayWindow': window(-5, 7, w, h),
              'dwaCompressionLevel': 200.0},
             {f'{layer}.{c}': (smooth(h, w, i + 3 * k) * (1 + 4 * k)).astype(np.float32)
              for k, layer in enumerate(['diffuse', 'specular'])
              for i, c in enumerate('RGB')}).write(os.path.join(out, 'dwaa-tiled-layers.exr'))

# Multi-part: a DWAB part (which is read) and a ZIP part
h, w = 20, 300
parts = [OpenEXR.Part({'compression': OpenEXR.DWAB_COMPRESSION, 'type': OpenEXR.scanlineimage,
                       'dataWindow': window(0, 0, w, h), 'displayWindow': window(0, 0, w, h)},
                      {'R': smooth(h, w, 5).astype(np.float16), 'G': smooth(h, w, 6).astype(np.float16),
                       'B': smooth(h, w, 7).astype(np.float16)}, name='beauty'),
         OpenEXR.Part({'compression': OpenEXR.ZIP_COMPRESSION, 'type': OpenEXR.scanlineimage,
                       'dataWindow': window(0, 0, 8, 8), 'displayWindow': window(0, 0, w, h)},
                      {'Z': r.random((8, 8)).astype(np.float32)}, name='depth')]
OpenEXR.File(parts).write(os.path.join(out, 'multipart.exr'))

# B44 with perceptually linear (pLinear) R and B channels and a linear G
# channel; the decoder maps pLinear values back through OpenEXR's logTable.
r2 = np.random.default_rng(54321)
h, w = 9, 17
chans = {}
for i, name in enumerate(['R', 'G', 'B']):
    c = OpenEXR.Channel((np.exp(r2.random((h, w)) * 6 - 2) * (1 + 0.5 * i)).astype(np.float16))
    c.pLinear = name != 'G'
    chans[name] = c
OpenEXR.File({'compression': OpenEXR.B44_COMPRESSION, 'type': OpenEXR.scanlineimage},
             chans).write(os.path.join(out, 'b44-plinear.exr'))

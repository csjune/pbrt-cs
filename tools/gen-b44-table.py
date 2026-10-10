#!/usr/bin/env python3
# Extracts the logTable that OpenEXR's B44 decoder uses for perceptually
# linear (pLinear) channels (65536 half-float bit patterns, x -> 8 log(x))
# from OpenEXR's src/lib/OpenEXRCore/internal_b44_table.c in pbrt-v4's
# src/ext/openexr and writes it as little-endian uint16 values to
# src/Pbrt/Data/B44LogTable.bin, which Util/Exr.cs embeds as a resource.
#
# usage: python3 tools/gen-b44-table.py [path/to/pbrt-v4]
import os, re, struct, sys

here = os.path.dirname(os.path.abspath(__file__))
pbrt = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', '..', 'pbrt-v4')
src = os.path.join(pbrt, 'src', 'ext', 'openexr', 'src', 'lib', 'OpenEXRCore', 'internal_b44_table.c')
dst = os.path.join(here, '..', 'src', 'Pbrt', 'Data', 'B44LogTable.bin')

text = open(src).read()
m = re.search(r'exrcore_logTable_data\[\]\s*=\s*\{(.*?)\}', text, re.S)
values = [int(v, 16) for v in re.findall(r'0x[0-9a-fA-F]+', m.group(1))]
assert len(values) == 65536, len(values)
with open(dst, 'wb') as f:
    f.write(struct.pack('<65536H', *values))
print(dst)

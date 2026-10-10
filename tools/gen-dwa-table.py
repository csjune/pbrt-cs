#!/usr/bin/env python3
# Extracts the dwaCompressorToLinear table (65536 half-float bit patterns) from
# OpenEXR's src/lib/OpenEXRCore/dwaLookups.h in pbrt-v4's src/ext/openexr and
# writes it as little-endian uint16 values to src/Pbrt/Data/DwaToLinear.bin,
# which Util/ExrDwa.cs (the DWAA/DWAB decoder) embeds as a resource.
#
# usage: python3 tools/gen-dwa-table.py [path/to/pbrt-v4]
import os, re, struct, sys

here = os.path.dirname(os.path.abspath(__file__))
pbrt = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', '..', 'pbrt-v4')
src = os.path.join(pbrt, 'src', 'ext', 'openexr', 'src', 'lib', 'OpenEXRCore', 'dwaLookups.h')
dst = os.path.join(here, '..', 'src', 'Pbrt', 'Data', 'DwaToLinear.bin')

text = open(src).read()
m = re.search(r'dwaCompressorToLinear\[\]\s*=\s*\{(.*?)\}', text, re.S)
values = [int(v, 16) for v in re.findall(r'0x[0-9a-fA-F]+', m.group(1))]
assert len(values) == 65536, len(values)
with open(dst, 'wb') as f:
    f.write(struct.pack('<65536H', *values))
print(dst)

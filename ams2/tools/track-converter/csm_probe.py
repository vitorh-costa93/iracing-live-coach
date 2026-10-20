"""Correlate collision meshes in .csm with render meshes (.meb) to learn the per-mesh surface id."""
import struct, os, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from meb import read_meb

D = sys.argv[1]
vmap = {}
for f in os.listdir(D):
    if f.endswith('.meb'):
        m = read_meb(open(os.path.join(D, f), 'rb').read())
        pos = m.streams[0][3]
        for s in m.subs:
            for i in s['indices'][:3]:
                vmap.setdefault((round(pos[3 * i], 2), round(pos[3 * i + 1], 2), round(pos[3 * i + 2], 2)), set()).add(
                    s['material'].split(chr(92))[-1])
d = open(os.path.join(D, 'physics', os.path.basename(D.rstrip('/')) + '.csm'), 'rb').read()
o = 4; prev = struct.unpack_from('<I', d, 0)[0]
res = collections.defaultdict(collections.Counter)
while o + 8 <= len(d):
    sz = struct.unpack_from('<I', d, o)[0]; p = o + 4
    v = struct.unpack_from('<3f', d, p + 28)
    after = struct.unpack_from('<I', d, p + sz)[0] if p + sz + 4 <= len(d) else None
    mats = vmap.get(tuple(round(x, 2) for x in v), {'?'})
    for mt in mats:
        res[mt][(prev, after)] += 1
    prev = after; o = p + sz + 4
for mt, c in sorted(res.items(), key=lambda x: -sum(x[1].values()))[:30]:
    print('%-40s (before, after): %s' % (mt, dict(c.most_common(3))))

"""Which render meshes/materials correspond to each CSM surface id (Sochi).  Matches collision vertices to .meb vertices."""
import sys, os, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csm import read_csm, parse_mesh
from meb import read_meb

D = sys.argv[1]
vmap = {}
for f in os.listdir(D):
    if f.lower().endswith('.meb'):
        m = read_meb(open(os.path.join(D, f), 'rb').read())
        P = np.array(m.streams[0][3]).reshape(-1, 3)
        mat = {}
        for s in m.subs:
            for i in s['indices']:
                mat[i] = s['material'].split(chr(92))[-1][:-4]
        for i, p in enumerate(np.round(P, 2).tolist()):
            vmap.setdefault(tuple(p), (f[:-4], mat.get(i, '?')))
head, ms = read_csm(open(os.path.join(D, 'physics', os.path.basename(D.rstrip('/\\')) + '.csm'), 'rb').read())
by = collections.defaultdict(collections.Counter)
for b, sid in ms:
    m = parse_mesh(b)
    hits = collections.Counter(vmap.get(tuple(p)) for p in np.round(m.verts.astype(np.float64), 2).tolist())
    (best, n), = hits.most_common(1)
    by[sid][best[1] if best else None] += len(m.tris)
for sid, c in sorted(by.items()):
    print(sid, sum(c.values()), c.most_common(12))

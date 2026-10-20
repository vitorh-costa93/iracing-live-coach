import sys, os, collections, struct
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csm import read_csm, parse_mesh

head, ms = read_csm(open(sys.argv[1], 'rb').read())
f32 = np.float32
leafsz = collections.Counter(); order_ok = 0; bfs_ok = 0; eps_hits = collections.Counter(); bb = collections.Counter()
lvl_ok = 0; sidc = collections.Counter(); aabb_ok = 0; remap_perm = 0
for i, (b, sid) in enumerate(ms):
    m = parse_mesh(b); sidc[sid] += 1
    V = m.verts
    mn, mx = V.min(0), V.max(0)
    bb[(tuple(np.array(m.bmin[:3], f32) == mn), tuple(np.array(m.bmax[:3], f32) == mx))] += 1
    aabb_ok += np.allclose(m.aabb, list(mn) + list(mx), atol=0)
    # eps candidates
    mag = f32(np.abs(V).max())
    for name, val in (('maxabs*2^-22', f32(mag) * f32(2 ** -22)), ('maxabs*FLT_EPS*2', f32(mag) * f32(2 * 1.1920929e-07)),
                      ('ext', f32(np.max(np.maximum(np.abs(mn), np.abs(mx)))) * f32(2.384185791015625e-07))):
        if abs(val - f32(m.geom_eps)) <= abs(f32(m.geom_eps)) * 1e-6:
            eps_hits[name] += 1
    # dscaler
    ds = (np.array(m.bmax[:3], f32) - np.array(m.bmin[:3], f32)) / f32(65535)
    bb[('dscaler', bool(np.allclose(ds, m.dscaler[:3], rtol=1e-6)))] += 1
    # leaves in order, BFS layout, levels
    starts = []
    q = list(range(m.nroot)); visited = []; depth = {r: 1 for r in q}; maxd = 1
    while q:
        p = q.pop(0); visited.append(p)
        f, ptrs = m.pages[p]
        for j in range(4):
            if f[0, j] > f[3, j]:
                continue
            if ptrs[j] & 1:
                starts.append((ptrs[j] >> 5, ((ptrs[j] >> 1) & 15) + 1))
            else:
                c = ptrs[j] // 112; q.append(c); depth[c] = depth[p] + 1; maxd = max(maxd, depth[c])
    leafsz.update(n for _, n in starts)
    bfs_ok += visited == list(range(len(m.pages)))
    lvl_ok += maxd == m.nlevels
    s = sorted(starts); order_ok += all(a[0] + a[1] == b[0] for a, b in zip(s, s[1:]))
    remap_perm += m.remap is not None and sorted(m.remap.tolist()) == list(range(len(m.tris)))
print('meshes', len(ms), 'surface ids', sidc)
print('leaf sizes', sorted(leafsz.items()))
print('pages in BFS order', bfs_ok, 'levels==depth', lvl_ok, 'leaves contiguous', order_ok, 'remap is permutation', remap_perm)
print('rtree bounds == vertex min/max', bb.most_common(6))
print('aabb == min/max', aabb_ok, 'eps formula hits', eps_hits)

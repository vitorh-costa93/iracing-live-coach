"""Edge flag (extraTrigData) rule for PhysX cooked meshes: compare candidate rule vs Sochi data."""
import sys, os, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csm import read_csm, parse_mesh


def edge_flags(V, T, cos_thr=None):
    """bit 3/4/5 = edge (0,1)/(1,2)/(2,0) is 'active' (convex or boundary).
    Rule: boundary or non-manifold edge -> active; shared by exactly 2 -> active if convex and the angle between
    face normals exceeds the threshold (cos(n0,n1) < cos_thr)."""
    V = V.astype(np.float64)
    n = np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]])
    ln = np.linalg.norm(n, axis=1); n = n / np.maximum(ln, 1e-30)[:, None]
    edges = collections.defaultdict(list)
    for t, (a, b, c) in enumerate(T.tolist()):
        for k, (u, v) in enumerate(((a, b), (b, c), (c, a))):
            edges[(min(u, v), max(u, v))].append((t, k))
    flags = np.zeros(len(T), np.uint8)
    kind = {}
    for e, lst in edges.items():
        if len(lst) != 2:
            for t, k in lst:
                flags[t] |= 8 << k; kind[(t, k)] = 'boundary' if len(lst) == 1 else 'nonmanifold'
            continue
        (t0, k0), (t1, k1) = lst
        opp = [x for x in T[t1] if x not in e]
        if not opp:
            continue
        # convex if the opposite vertex of t1 lies below the plane of t0
        dist = n[t0] @ (V[opp[0]] - V[T[t0, 0]])
        cosang = float(n[t0] @ n[t1])
        convex = dist < 0
        act = convex and (cos_thr is None or cosang < cos_thr)
        for t, k in lst:
            kind[(t, k)] = ('convex' if convex else 'concave', cosang)
            if act:
                flags[t] |= 8 << k
    return flags, kind


if __name__ == '__main__':
    head, ms = read_csm(open(sys.argv[1], 'rb').read())
    stats = collections.Counter(); cos_on = []; cos_off = []
    for b, sid in ms[:400]:
        m = parse_mesh(b)
        if m.extra is None:
            continue
        fl, kind = edge_flags(m.verts, m.tris)
        for (t, k), kd in kind.items():
            got = bool(m.extra[t] & (8 << k))
            if isinstance(kd, tuple):
                stats[(kd[0], got)] += 1
                if kd[0] == 'convex':
                    (cos_on if got else cos_off).append(kd[1])
            else:
                stats[(kd, got)] += 1
        stats['low bits set'] += int((m.extra & 7).any())
    print(stats)
    if cos_on and cos_off:
        print('convex active cos max %.6f   convex inactive cos min %.6f' % (max(cos_on), min(cos_off)))
        print('inactive cos quantiles', np.quantile(cos_off, [0, .01, .5]), 'active', np.quantile(cos_on, [.5, .99, 1]))

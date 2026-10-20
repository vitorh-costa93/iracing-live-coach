"""Write PhysX 3.2-style cooked triangle meshes ('NXS\\1MESH' v12 + big-endian RTRE v1) and AMS2 .csm files.
Layout reproduced from the 1493 Sochi meshes (see csm.py / SPEC.md section 7).  Not PhysX's own cooker: the R-tree
is built by median splits (leaves <= 4 triangles, pages of 4), which is structurally valid but not byte-identical.
usage: python -I csm_write.py selftest <ref.csm>   re-cook every mesh of a reference .csm and verify trees + ray casts
"""
import math, struct, sys, os
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from edgeflags import edge_flags

F32 = np.float32
FLT_MAX = float(np.finfo(np.float32).max)
INFLATE = F32(0.0005)
PAGE_BYTES = 112
EMPTY_PTR = 29          # value seen in empty Sochi slots (ignored by traversal: min > max)
LEAF_MAX = 4
CONVEX_COS = math.cos(0.1)


def _write_indices(vals, out):
    mx = int(vals.max()) if len(vals) else 0
    out += struct.pack('<I', mx)
    if mx <= 0xff:
        out += vals.astype('u1').tobytes()
    elif mx <= 0xffff:
        out += vals.astype('<u2').tobytes()
    else:
        out += vals.astype('<u4').tobytes()


def cook_mesh(verts, tris):
    """verts (N,3) float, tris (M,3) int -> cooked bytes.  Degenerate triangles are dropped (as the cooker does)."""
    V = np.asarray(verts, F32)
    T = np.asarray(tris, np.int64)
    a, b, c = V[T[:, 0]].astype(np.float64), V[T[:, 1]].astype(np.float64), V[T[:, 2]].astype(np.float64)
    area = np.linalg.norm(np.cross(b - a, c - a), axis=1)
    keep = (T[:, 0] != T[:, 1]) & (T[:, 1] != T[:, 2]) & (T[:, 0] != T[:, 2]) & (area > 1e-12)
    orig = np.nonzero(keep)[0]
    T = T[keep]
    if len(T) == 0:
        return None
    lo = np.minimum(np.minimum(V[T[:, 0]], V[T[:, 1]]), V[T[:, 2]])
    hi = np.maximum(np.maximum(V[T[:, 0]], V[T[:, 1]]), V[T[:, 2]])
    cen = (lo.astype(np.float64) + hi) * 0.5

    # --- leaves by recursive median split; leaf count rounded so the bottom pages are full where possible
    leaves = []

    def split(ids):
        if len(ids) <= LEAF_MAX:
            leaves.append(ids); return
        ext = cen[ids].max(0) - cen[ids].min(0)
        ax = int(np.argmax(ext))
        o = ids[np.argsort(cen[ids, ax], kind='stable')]
        # split so that the left part holds a multiple of LEAF_MAX triangles (keeps leaves full)
        h = (len(o) // 2 + LEAF_MAX - 1) // LEAF_MAX * LEAF_MAX
        h = min(max(h, LEAF_MAX), len(o) - 1)
        split(o[:h]); split(o[h:])
    split(np.arange(len(T)))
    order = np.concatenate(leaves)
    T = T[order]; remap = orig[order]; lo = lo[order]; hi = hi[order]
    # --- nodes level 0 = leaves
    nodes = []
    s = 0
    for lf in leaves:
        n = len(lf)
        nodes.append((lo[s:s + n].min(0) - INFLATE, hi[s:s + n].max(0) + INFLATE, ('leaf', (s << 5) | ((n - 1) << 1) | 1)))
        s += n
    levels = []                       # list of pages per level, bottom first; page = list of child nodes
    cur = nodes
    while True:
        pages = [cur[i:i + 4] for i in range(0, len(cur), 4)]
        levels.append(pages)
        if len(pages) == 1:
            break
        cur = [(np.min([n[0] for n in p], axis=0), np.max([n[1] for n in p], axis=0), ('page', (len(levels) - 1, k)))
               for k, p in enumerate(pages)]
    # --- assign page indices top-down (root page = 0, then each level in order)
    index = {}
    k = 0
    for L in range(len(levels) - 1, -1, -1):
        for j in range(len(levels[L])):
            index[(L, j)] = k; k += 1
    total_pages = k
    page_blobs = [None] * total_pages
    for L, pages in enumerate(levels):
        for j, p in enumerate(pages):
            mins = [[FLT_MAX] * 4 for _ in range(3)]; maxs = [[-FLT_MAX] * 4 for _ in range(3)]; ptrs = [EMPTY_PTR] * 4
            for slot, (mn, mx, (kind, val)) in enumerate(p):
                for ax in range(3):
                    mins[ax][slot] = float(F32(mn[ax])); maxs[ax][slot] = float(F32(mx[ax]))
                ptrs[slot] = val if kind == 'leaf' else index[val] * PAGE_BYTES
            page_blobs[index[(L, j)]] = struct.pack('>24f4I', *(mins[0] + mins[1] + mins[2] + maxs[0] + maxs[1] + maxs[2]), *ptrs)
    root = levels[-1][0]
    rmin = np.min([n[0] for n in root], axis=0).astype(F32); rmax = np.max([n[1] for n in root], axis=0).astype(F32)
    dscale = ((rmax - rmin) / F32(65535)).astype(F32)

    nv = len(V)
    flags = 0x0a if nv <= 0xffff else 0x02           # FACE_REMAP | 16-bit indices (Sochi always 0x0a)
    out = bytearray(b'NXS\x01MESH')
    out += struct.pack('<IIf', 12, flags, 0.001)
    out += struct.pack('<II', nv, len(T))
    out += V.tobytes()
    out += T.astype('<u2' if flags & 8 else '<u4').tobytes()
    _write_indices(remap, out)
    out += b'RTRE' + struct.pack('>I', 1)
    out += struct.pack('>4f', *rmin, 0.0) + struct.pack('>4f', *rmax, 0.0)
    out += struct.pack('>4f', 1.0, 1.0, 1.0, 1.0) + struct.pack('>4f', *dscale, 0.0)
    out += struct.pack('>6I', 4, 1, len(levels), 4 * total_pages, total_pages, 0)
    out += b''.join(page_blobs)
    mag = F32(np.abs(V).max())
    out += struct.pack('<f', float(mag * F32(2.0 ** -22)))
    out += struct.pack('<6f', *V.min(0), *V.max(0))
    fl, _ = edge_flags(V, T, CONVEX_COS)
    out += struct.pack('<I', len(T)) + fl.astype('u1').tobytes()
    return bytes(out)


def write_csm(meshes):
    """meshes: list of (verts, tris, surface_id)."""
    out = bytearray(struct.pack('<I', 330))
    n = 0
    for V, T, sid in meshes:
        blob = cook_mesh(V, T)
        if blob is None:
            continue
        out += struct.pack('<I', len(blob)) + blob + struct.pack('<I', sid); n += 1
    return bytes(out), n


# ------------------------------------------------------------------ verification helpers
def ray_down_tree(m, x, z):
    """highest hit of a vertical ray using the R-tree (exercises the same data the engine traverses)."""
    best = None
    stack = list(range(m.nroot))
    P = m.verts.astype(np.float64)
    while stack:
        f, ptrs = m.pages[stack.pop()]
        for j in range(4):
            if not (f[0, j] <= x <= f[3, j] and f[2, j] <= z <= f[5, j]):
                continue
            if ptrs[j] & 1:
                st = ptrs[j] >> 5; nb = ((ptrs[j] >> 1) & 15) + 1
                for t in range(st, st + nb):
                    y = _hit(P[m.tris[t]], x, z)
                    if y is not None and (best is None or y > best):
                        best = y
            else:
                stack.append(ptrs[j] // PAGE_BYTES)
    return best


def ray_down_brute(P, T, x, z):
    best = None
    for t in T:
        y = _hit(P[t], x, z)
        if y is not None and (best is None or y > best):
            best = y
    return best


def _hit(tri, x, z):
    (x0, y0, z0), (x1, y1, z1), (x2, y2, z2) = tri
    d = (z1 - z2) * (x0 - x2) + (x2 - x1) * (z0 - z2)
    if abs(d) < 1e-12:
        return None
    l0 = ((z1 - z2) * (x - x2) + (x2 - x1) * (z - z2)) / d
    l1 = ((z2 - z0) * (x - x2) + (x0 - x2) * (z - z2)) / d
    l2 = 1 - l0 - l1
    if min(l0, l1, l2) < -1e-9:
        return None
    return l0 * y0 + l1 * y1 + l2 * y2


def selftest(ref):
    from csm import read_csm, parse_mesh, check_tree
    head, ms = read_csm(open(ref, 'rb').read())
    rng = np.random.default_rng(1)
    bad = 0; rays = 0; miss = 0
    for i, (b, sid) in enumerate(ms):
        m0 = parse_mesh(b)
        blob = cook_mesh(m0.verts, m0.tris)
        m = parse_mesh(blob)
        assert m.end == len(blob)
        ok, nmiss, dup, pr = check_tree(m)
        if not ok or pr:
            bad += 1
        if m.extra is not None and m0.extra is not None and len(m.tris) == len(m0.tris):
            pass
        if i % 25 == 0 and len(m.tris) < 6000:
            P = m.verts.astype(np.float64)
            for _ in range(20):
                t = m.tris[rng.integers(len(m.tris))]
                w = rng.dirichlet([1, 1, 1]); p = w @ P[t]
                a = ray_down_tree(m, p[0], p[2]); bb = ray_down_brute(P, m.tris, p[0], p[2])
                rays += 1
                if (a is None) != (bb is None) or (a is not None and abs(a - bb) > 1e-6):
                    miss += 1
    print('re-cooked', len(ms), 'meshes; tree errors', bad, '; ray casts', rays, 'mismatches', miss)


if __name__ == '__main__':
    if sys.argv[1] == 'selftest':
        selftest(sys.argv[2])

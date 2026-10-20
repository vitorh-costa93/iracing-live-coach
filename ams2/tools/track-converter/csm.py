"""AMS2 .csm = u32 330 + N x { u32 size, PhysX 3.x cooked triangle mesh ('NXS\\1' 'MESH' v12), u32 surface id }.
Reader + structural checker.  See SPEC.md section 7.
"""
import struct, sys
import numpy as np

FLT_MAX = 3.4028234663852886e38


class Mesh:
    pass


def parse_mesh(d):
    m = Mesh()
    assert d[:8] == b'NXS\x01MESH', d[:8]
    m.version, m.flags = struct.unpack_from('<II', d, 8)
    m.cet, = struct.unpack_from('<f', d, 16)
    nv, nt = struct.unpack_from('<II', d, 20)
    o = 28
    m.verts = np.frombuffer(d, '<f4', nv * 3, o).reshape(nv, 3); o += 12 * nv
    if m.flags & 4:
        m.tris = np.frombuffer(d, 'u1', nt * 3, o).reshape(nt, 3).astype(np.int64); o += 3 * nt
    elif m.flags & 8:
        m.tris = np.frombuffer(d, '<u2', nt * 3, o).reshape(nt, 3).astype(np.int64); o += 6 * nt
    else:
        m.tris = np.frombuffer(d, '<u4', nt * 3, o).reshape(nt, 3).astype(np.int64); o += 12 * nt
    if m.flags & 1:
        m.mats = np.frombuffer(d, '<u2', nt, o); o += 2 * nt
    m.remap = None
    if m.flags & 2:
        mx, = struct.unpack_from('<I', d, o); o += 4
        m.remap_max = mx
        if mx <= 0xff:
            m.remap = np.frombuffer(d, 'u1', nt, o); o += nt
        elif mx <= 0xffff:
            m.remap = np.frombuffer(d, '<u2', nt, o); o += 2 * nt
        else:
            m.remap = np.frombuffer(d, '<u4', nt, o); o += 4 * nt
    if m.flags & 16:
        o += 12 * nt
    assert d[o:o + 4] == b'RTRE', (hex(o), d[o:o + 4])
    o += 4
    m.rt_version, = struct.unpack_from('>I', d, o); o += 4
    m.bmin = struct.unpack_from('>4f', d, o); o += 16
    m.bmax = struct.unpack_from('>4f', d, o); o += 16
    m.invdiag = struct.unpack_from('>4f', d, o); o += 16
    m.dscaler = struct.unpack_from('>4f', d, o); o += 16
    (m.page_size, m.nroot, m.nlevels, m.total_nodes, m.total_pages, m.unused) = struct.unpack_from('>6I', d, o); o += 24
    pages = []
    for _ in range(m.total_pages):
        f = np.array(struct.unpack_from('>24f', d, o), dtype=np.float64).reshape(6, 4); o += 96
        p = struct.unpack_from('>4I', d, o); o += 16
        pages.append((f, p))
    m.pages = pages
    m.geom_eps, = struct.unpack_from('<f', d, o); o += 4
    m.aabb = struct.unpack_from('<6f', d, o); o += 24
    nb, = struct.unpack_from('<I', d, o); o += 4
    m.extra = np.frombuffer(d, 'u1', nb, o) if nb else None; o += nb
    m.end = o
    return m


def check_tree(m, verbose=False):
    """walk the R-tree: every triangle must be reached exactly once and lie inside its node box."""
    seen = np.zeros(len(m.tris), int)
    P = m.verts.astype(np.float64)
    problems = []

    def walk(pi, depth, box):
        f, ptrs = m.pages[pi]
        for j in range(4):
            mn = f[0:3, j]; mx = f[3:6, j]; ptr = ptrs[j]
            if mn[0] > mx[0]:
                continue                       # empty slot
            if ptr & 1:
                start = ptr >> 5; nb = ((ptr >> 1) & 15) + 1
                for t in range(start, start + nb):
                    seen[t] += 1
                    v = P[m.tris[t]]
                    if (v.min(0) < mn - 1e-3).any() or (v.max(0) > mx + 1e-3).any():
                        problems.append(('tri outside leaf box', t))
            else:
                assert ptr % 112 == 0, ptr
                walk(ptr // 112, depth + 1, (mn, mx))
    for r in range(m.nroot):
        walk(r, 0, None)
    return (seen == 1).all(), int((seen == 0).sum()), int((seen > 1).sum()), problems[:5]


def read_csm(d):
    head, = struct.unpack_from('<I', d, 0)
    o = 4; out = []
    while o < len(d):
        sz, = struct.unpack_from('<I', d, o)
        blob = d[o + 4:o + 4 + sz]
        sid, = struct.unpack_from('<I', d, o + 4 + sz)
        out.append((blob, sid)); o += 8 + sz
    return head, out


if __name__ == '__main__':
    head, ms = read_csm(open(sys.argv[1], 'rb').read())
    print('head', head, 'meshes', len(ms))
    import collections
    st = collections.Counter(); bad = 0
    for i, (b, sid) in enumerate(ms):
        m = parse_mesh(b)
        assert m.end == len(b), (i, m.end, len(b))
        ok, miss, dup, pr = check_tree(m)
        st[(m.version, m.flags, m.rt_version, m.page_size, m.unused)] += 1
        if not ok or pr:
            bad += 1
            if bad < 4:
                print('tree problem', i, miss, dup, pr)
        if i < 3 or len(m.tris) > 20000 and i < 400:
            print(i, 'sid', sid, 'nv', len(m.verts), 'nt', len(m.tris), 'roots', m.nroot, 'levels', m.nlevels,
                  'nodes', m.total_nodes, 'pages', m.total_pages, 'invdiag', m.invdiag, 'dscaler', m.dscaler,
                  'eps', m.geom_eps, 'remapmax', m.remap_max, 'extra', None if m.extra is None else collections.Counter(m.extra.tolist()).most_common(4))
    print(st, 'tree problems', bad)

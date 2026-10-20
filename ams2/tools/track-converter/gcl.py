"""AMS2 track_cut .gcl (track-limits / cut detection triangles + 2-level xz grid).  See SPEC.md section 9.
layout (LE):  u32 0x10000001, u32 ntri, f32 cell, f32 ox, f32 oy, f32 oz, u32 ncx, u32 ncz,
              ntri x { f32 v[3][3], u32 flags },
              ncx*ncz cells (x outer, z inner) x node,
  node = u32 n, u32 nx, u32 nz, f32 x, f32 y, f32 z, f32 child_size, u32 tri[n], then nx*nz child nodes.
usage: python -I gcl.py check <file.gcl>
"""
import struct, sys, collections
import numpy as np

TRI = np.dtype([('v', '<f4', (3, 3)), ('f', '<u4')])


def read_gcl(d):
    magic, n, cell, ox, oy, oz, ncx, ncz = struct.unpack_from('<2I4f2I', d, 0)
    assert magic == 0x10000001
    o = 32
    tris = np.frombuffer(d, TRI, n, o); o += 40 * n

    def node(o):
        cnt, nx, nz, x, y, z, cs = struct.unpack_from('<3I4f', d, o); o += 28
        idx = np.frombuffer(d, '<u4', cnt, o); o += 4 * cnt
        kids = []
        for _ in range(nx * nz):
            k, o = node(o); kids.append(k)
        return dict(n=cnt, nx=nx, nz=nz, pos=(x, y, z), cs=cs, idx=idx, kids=kids), o
    cells = []
    for _ in range(ncx * ncz):
        c, o = node(o); cells.append(c)
    return dict(n=n, cell=cell, origin=(ox, oy, oz), ncx=ncx, ncz=ncz, tris=tris, cells=cells, end=o)


def tri_hits_box(v, x0, z0, s):
    """conservative xz overlap test (AABB vs AABB)."""
    return v[:, 0].min() <= x0 + s and v[:, 0].max() >= x0 and v[:, 2].min() <= z0 + s and v[:, 2].max() >= z0


def sat_xz(P, ids, x0, z0, s):
    """ids of triangles (P: (N,3,2) xz, float64) overlapping the square [x0,x0+s]x[z0,z0+s] (2D separating axes)."""
    if len(ids) == 0:
        return ids
    T = P[ids]
    keep = (T[:, :, 0].max(1) >= x0) & (T[:, :, 0].min(1) <= x0 + s) & (T[:, :, 1].max(1) >= z0) & (T[:, :, 1].min(1) <= z0 + s)
    C = np.array([[x0, z0], [x0 + s, z0], [x0 + s, z0 + s], [x0, z0 + s]])
    for k in range(3):
        e = T[:, (k + 1) % 3] - T[:, k]
        n = np.stack([-e[:, 1], e[:, 0]], 1)
        pt = np.einsum('nvk,nk->nv', T, n); pc = C @ n.T
        keep &= ~((pt.max(1) < pc.min(0)) | (pc.max(0) < pt.min(1)))
    return ids[keep]


def write_gcl(tris, flags, cell=100.0, sub=10, leaf_max=10, origin=None, dims=None):
    """tris (N,3,3) float, flags (N,) u32 -> bytes.  Rules reproduced from Sochi [V byte-exact re-encode]:
    100 m cells; a cell with no triangle has no children (cs 10); otherwise 10x10 nodes of 10 m; a 10 m node with
    <= 10 overlapping triangles is a leaf, else 10x10 leaves of 1 m.  Overlap = exact 2D (xz) triangle/square test."""
    tris = np.asarray(tris, np.float32); flags = np.asarray(flags, np.uint32)
    lo = tris.reshape(-1, 3).min(0); hi = tris.reshape(-1, 3).max(0)
    if origin is None:
        ox = float(np.floor(lo[0] / cell) * cell); oz = float(np.floor(lo[2] / cell) * cell); oy = -100.0
    else:
        ox, oy, oz = origin
    if dims is None:
        ncx = int((hi[0] - ox) // cell) + 1; ncz = int((hi[2] - oz) // cell) + 1
    else:
        ncx, ncz = dims
    out = bytearray(struct.pack('<2I4f2I', 0x10000001, len(tris), cell, ox, oy, oz, ncx, ncz))
    rec = np.zeros(len(tris), TRI); rec['v'] = tris; rec['f'] = flags
    out += rec.tobytes()
    P = tris[:, :, [0, 2]].astype(np.float64)
    allids = np.arange(len(tris))
    cs = cell / sub
    for i in range(ncx):
        for j in range(ncz):
            x0 = ox + i * cell; z0 = oz + j * cell
            inc = sat_xz(P, allids, x0, z0, cell)
            if len(inc) == 0:
                out += struct.pack('<3I4f', 0, 0, 0, x0, 0.0, z0, cs); continue
            out += struct.pack('<3I4f', 0, sub, sub, x0, 0.0, z0, cs)
            for a in range(sub):
                for b in range(sub):
                    sx = x0 + a * cs; sz = z0 + b * cs
                    m = sat_xz(P, inc, sx, sz, cs)
                    if len(m) <= leaf_max:
                        out += struct.pack('<3I4f', len(m), 0, 0, sx, 0.0, sz, 1.0) + m.astype('<u4').tobytes(); continue
                    out += struct.pack('<3I4f', 0, sub, sub, sx, 0.0, sz, 1.0)
                    s3 = cs / sub
                    for c in range(sub):
                        for e in range(sub):
                            m3 = sat_xz(P, m, sx + c * s3, sz + e * s3, s3)
                            out += struct.pack('<3I4f', len(m3), 0, 0, sx + c * s3, 0.0, sz + e * s3, s3 / sub) + m3.astype('<u4').tobytes()
    return bytes(out)


if __name__ == '__main__':
    d = open(sys.argv[2], 'rb').read()
    g = read_gcl(d)
    print('ntri', g['n'], 'grid', g['ncx'], 'x', g['ncz'], 'cell', g['cell'], 'origin', g['origin'], 'end', g['end'], 'len', len(d))
    print('tri flags', collections.Counter(g['tris']['f'].tolist()))
    shape = collections.Counter(); miss = 0; tot = 0; extra = 0; order = 0
    V = g['tris']['v'].astype(np.float64)
    tmin = V.min(1); tmax = V.max(1)
    for ci, c in enumerate(g['cells']):
        i, j = divmod(ci, g['ncz'])
        order += c['pos'] == (g['origin'][0] + i * g['cell'], 0.0, g['origin'][2] + j * g['cell'])
        shape[(c['n'], c['nx'], c['nz'], c['cs'])] += 1
        for k in c['kids']:
            shape[('kid', k['nx'], k['nz'], k['cs'])] += 1
            x0, _, z0 = k['pos']; s = c['cs']
            want = set(np.nonzero((tmin[:, 0] <= x0 + s) & (tmax[:, 0] >= x0) & (tmin[:, 2] <= z0 + s) & (tmax[:, 2] >= z0))[0].tolist())
            got = set(k['idx'].tolist())
            tot += len(got); miss += len(got - want); extra += len(want - got)
    print('node shapes', shape.most_common(6), 'cells in x-outer order', order)
    print('listed', tot, 'listed but AABB-disjoint', miss, 'AABB-overlapping but not listed', extra)

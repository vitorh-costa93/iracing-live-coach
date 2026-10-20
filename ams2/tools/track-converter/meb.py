"""MEB mesh (as written by 'Open Madness Track Tools' for the Sochi mod) reader/writer.  See SPEC.md.
usage: python -I meb.py check <dir>      parse every .meb, require exact length consumption
       python -I meb.py dump <file.meb>
"""
import os, struct, sys

FMT = {0: ('f', 1), 1: ('f', 2), 2: ('f', 3), 3: ('f', 4), 4: ('I', 1)}   # D3DDECLTYPE FLOAT1..4, D3DCOLOR
SEM = {0: 'position', 2: 'normal', 3: 'uv', 4: 'tangent', 5: 'binormal', 6: 'color'}


def _cstr(d, o):
    e = d.index(b'\0', o)
    s = d[o:e].decode('latin1')
    o = e + 1
    return s, o + (-o) % 4


class Meb:
    pass


def read_meb(d):
    m = Meb()
    m.magic, m.flags = struct.unpack_from('<II', d, 0)
    m.name, o = _cstr(d, 8)
    m.nverts, nstreams, nsub = struct.unpack_from('<III', d, o); o += 12
    m.bounds = struct.unpack_from('<10f', d, o); o += 40      # sphere cx,cy,cz,r, aabb min xyz, max xyz
    m.streams = []
    for _ in range(nstreams):
        fmt, sem, idx = struct.unpack_from('<III', d, o); o += 12
        c, k = FMT[fmt]
        n = k * m.nverts
        vals = struct.unpack_from('<%d%s' % (n, c), d, o); o += 4 * n
        m.streams.append((fmt, sem, idx, vals))
    m.subs = []
    for _ in range(nsub):
        mat, o = _cstr(d, o)
        unk, ntri = struct.unpack_from('<II', d, o); o += 8
        wide = m.nverts > 0xffff
        ic = 'I' if wide else 'H'; isz = 4 if wide else 2
        idx = struct.unpack_from('<%d%s' % (3 * ntri, ic), d, o); o += isz * 3 * ntri
        o += (-o) % 4
        vmin, vmax = struct.unpack_from('<HH', d, o); o += 4
        b = struct.unpack_from('<10f', d, o); o += 40
        m.subs.append(dict(material=mat, unk=unk, indices=idx, vmin=vmin, vmax=vmax, bounds=b))
    m.end = o
    return m


def _f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def _bounds(pts):
    """sphere centre = (min+max)*0.5, radius = |max-centre| (float32); matches OMTT output for ~94%
    of Sochi meshes bit-exactly, the rest differ in the last float bit."""
    if not pts:
        return (0.0,) * 10
    mn = [_f32(min(p[i] for p in pts)) for i in range(3)]
    mx = [_f32(max(p[i] for p in pts)) for i in range(3)]
    c = [_f32(_f32(a + b) * 0.5) for a, b in zip(mn, mx)]
    r = _f32(sum(_f32(b - a) ** 2 for a, b in zip(c, mx)) ** 0.5)
    return tuple(c) + (r,) + tuple(mn) + tuple(mx)


def _put_cstr(out, s):
    out += s.encode('latin1') + b'\0'
    out += b'\0' * ((-len(out)) % 4)


def write_meb(name, positions, normals, uvs, submeshes, colors=None, tangents=None, binormals=None, bounds=None):
    """positions/normals/tangents/binormals: list of (x,y,z); uvs: list of (u,v);
    submeshes: list of (material_path, triangle index list).  Stream order follows Sochi:
    position, color, normal, tangent, binormal, uv.  Bounds sphere = AABB centre + max distance
    (OMTT's exact sphere algorithm not verified)."""
    n = len(positions)
    if colors is None:
        colors = [0xffffffff] * n
    if tangents is None or binormals is None:
        tangents, binormals = _tangent_frame(normals)
    out = bytearray(struct.pack('<II', 0x01000000, 0x100))
    _put_cstr(out, name.upper())
    streams = [(2, 0, positions), (4, 6, colors), (2, 2, normals), (2, 4, tangents), (2, 5, binormals), (1, 3, uvs)]
    out += struct.pack('<III', n, len(streams), len(submeshes))
    out += struct.pack('<10f', *(bounds[0] if bounds else _bounds(positions)))
    for fmt, sem, data in streams:
        out += struct.pack('<III', fmt, sem, 0)
        c, k = FMT[fmt]
        flat = [v for item in data for v in (item if k > 1 else (item,))]
        out += struct.pack('<%d%s' % (len(flat), c), *flat)
    wide = n > 0xffff
    for si, (mat, idx) in enumerate(submeshes):
        _put_cstr(out, mat.upper())
        out += struct.pack('<II', 0, len(idx) // 3)
        out += struct.pack('<%d%s' % (len(idx), 'I' if wide else 'H'), *idx)
        out += b'\0' * ((-len(out)) % 4)
        used = sorted(set(idx)) or [0]
        out += struct.pack('<HH', used[0] & 0xffff, used[-1] & 0xffff)
        out += struct.pack('<10f', *(bounds[1 + si] if bounds else _bounds([positions[i] for i in used])))
    return bytes(out)


def _tangent_frame(normals):
    t, b = [], []
    for nx, ny, nz in normals:
        ax = (0.0, 1.0, 0.0) if abs(ny) < 0.9 else (1.0, 0.0, 0.0)
        tx, ty, tz = ay_cross(ax, (nx, ny, nz))
        ln = (tx * tx + ty * ty + tz * tz) ** 0.5 or 1.0
        tv = (tx / ln, ty / ln, tz / ln)
        t.append(tv); b.append(ay_cross((nx, ny, nz), tv))
    return t, b


def ay_cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def main():
    cmd, p = sys.argv[1], sys.argv[2]
    if cmd == 'dump':
        m = read_meb(open(p, 'rb').read())
        print(m.name, hex(m.magic), hex(m.flags), 'verts', m.nverts, 'bounds', ['%.2f' % x for x in m.bounds])
        for fmt, sem, idx, v in m.streams:
            print(' stream fmt', fmt, 'sem', sem, SEM.get(sem), 'idx', idx, v[:6])
        for s in m.subs:
            print(' sub', s['material'], 'unk', s['unk'], 'tris', len(s['indices']) // 3, 'vrange', s['vmin'], s['vmax'])
        print('end', m.end)
    elif cmd == 'check':
        import collections
        c = collections.Counter(); bad = []
        for f in sorted(os.listdir(p)):
            if not f.lower().endswith('.meb'):
                continue
            d = open(os.path.join(p, f), 'rb').read()
            try:
                m = read_meb(d)
                ok = m.end == len(d)
                c['ok' if ok else 'len_mismatch'] += 1
                c['streams=%s' % ','.join('%d/%d' % (s[0], s[1]) for s in m.streams)] += 1
                c['subs=%d' % min(len(m.subs), 5)] += 1
                c['magic=%x/%x' % (m.magic, m.flags)] += 1
                c['wide' if m.nverts > 0xffff else 'narrow'] += 1
                for s in m.subs:
                    c['unk=%d' % s['unk']] += 1
                    used = set(s['indices'])
                    c['vrange_ok' if (min(used), max(used)) == (s['vmin'], s['vmax']) else 'vrange_x'] += 1
                if not ok:
                    bad.append((f, m.end, len(d)))
                # rewrite test: rebuild with our writer from parsed data
                st = {s[1]: s[3] for s in m.streams}
                g = lambda v, k: [tuple(v[i:i + k]) for i in range(0, len(v), k)]
                w = write_meb(m.name, g(st[0], 3), g(st[2], 3), g(st[3], 2),
                              [(s['material'], list(s['indices'])) for s in m.subs], colors=list(st[6]),
                              tangents=g(st[4], 3), binormals=g(st[5], 3), bounds=None)
                if len(m.streams) == 6:
                    w2 = write_meb(m.name, g(st[0], 3), g(st[2], 3), g(st[3], 2), [(s['material'], list(s['indices'])) for s in m.subs], colors=list(st[6]), tangents=g(st[4], 3), binormals=g(st[5], 3), bounds=[m.bounds] + [s['bounds'] for s in m.subs])
                    c['rewrite_with_orig_bounds_identical' if w2 == d else 'rewrite_with_orig_bounds_differs'] += 1
                c['rewrite_identical' if w == d else 'rewrite_differs'] += 1
                if w != d and c['rewrite_differs'] <= 3:
                    k = next(i for i in range(min(len(w), len(d))) if w[i] != d[i]) if len(w) == len(d) else -1
                    print('rewrite diff', f, len(w), len(d), hex(k))
            except Exception as ex:
                c['err:' + type(ex).__name__] += 1; bad.append((f, repr(ex)))
        print(dict(sorted(c.items())))
        print(bad[:5])


if __name__ == '__main__':
    main()

"""Minimal Assetto Corsa .kn5 reader (static track models).
Layout (community-documented, checked against bahrain.kn5 by parsing to EOF):
  'sc6969' i32 version [i32 extra if version>5]
  i32 nTex   { i32 type, str name, i32 size, bytes }
  i32 nMat   { str name, str shader, u8 blend, u8 alphaTested, [i32 depthMode if version>4],
               i32 nProps {str name, f32 a, f32[2] b, f32[3] c, f32[4] d},
               i32 nSamplers {str sampler, i32 slot, str texture} }
  node tree (recursive): i32 class, str name, i32 nChildren, u8 active
     class 1 (dummy): f32[16] matrix (row-major, row vectors, translation in [12..14])
     class 2 (mesh):  u8 castShadows, u8 visible, u8 transparent, i32 nv {pos3 nrm3 uv2 tan3},
                      i32 ni {u16}, i32 matId, i32 layer, f32 lodIn, f32 lodOut, f32[3] sphereC, f32 sphereR,
                      u8 renderable
     class 3 (skinned) -> not supported (tracks normally have none)
  str = i32 length + bytes
"""
import struct
import numpy as np


class R:
    def __init__(self, d):
        self.d = d; self.o = 0

    def i(self):
        v = struct.unpack_from('<i', self.d, self.o)[0]; self.o += 4; return v

    def f(self, n=1):
        v = struct.unpack_from('<%df' % n, self.d, self.o); self.o += 4 * n; return v

    def b(self):
        v = self.d[self.o]; self.o += 1; return v

    def s(self):
        n = self.i(); v = self.d[self.o:self.o + n].decode('utf-8', 'replace'); self.o += n; return v

    def raw(self, n):
        v = self.d[self.o:self.o + n]; self.o += n; return v


def read_kn5(path, load_textures=True):
    r = R(open(path, 'rb').read())
    assert r.raw(6) == b'sc6969'
    ver = r.i()
    if ver > 5:
        r.i()
    textures = {}
    for _ in range(r.i()):
        r.i(); name = r.s(); size = r.i()
        if load_textures:
            textures[name] = r.raw(size)
        else:
            r.o += size; textures[name] = None
    mats = []
    for _ in range(r.i()):
        m = {'name': r.s(), 'shader': r.s(), 'blend': r.b(), 'alphaTested': r.b()}
        if ver > 4:
            m['depth'] = r.i()
        m['props'] = {}
        for _ in range(r.i()):
            pn = r.s(); vals = r.f(10); m['props'][pn] = vals
        m['tex'] = {}
        for _ in range(r.i()):
            sm = r.s(); r.i(); m['tex'][sm] = r.s()
        mats.append(m)
    meshes, dummies = [], []

    def node(parent_m):
        cls = r.i(); name = r.s(); nch = r.i(); r.b()
        M = parent_m
        if cls == 1:
            local = np.array(r.f(16), dtype=np.float64).reshape(4, 4)
            M = local @ parent_m
            dummies.append((name, M))
        elif cls == 2:
            cast, vis, transp = r.b(), r.b(), r.b()
            nv = r.i()
            v = np.frombuffer(r.raw(44 * nv), dtype='<f4').reshape(nv, 11).astype(np.float64)
            ni = r.i()
            idx = np.frombuffer(r.raw(2 * ni), dtype='<u2').astype(np.int64)
            mat = r.i(); layer = r.i(); lod = r.f(2); r.f(4); rend = r.b()
            pos = np.c_[v[:, 0:3], np.ones(nv)] @ parent_m
            nrm = v[:, 3:6] @ parent_m[:3, :3]
            tan = v[:, 8:11] @ parent_m[:3, :3]
            meshes.append({'name': name, 'pos': pos[:, :3], 'nrm': nrm, 'uv': v[:, 6:8], 'tan': tan, 'idx': idx,
                           'mat': mat, 'visible': vis, 'transparent': transp, 'cast': cast, 'renderable': rend,
                           'lod': lod})
        else:
            raise ValueError('unsupported kn5 node class %d (%s) at 0x%x' % (cls, name, r.o))
        for _ in range(nch):
            node(M)
    node(np.eye(4))
    assert r.o == len(r.d), 'trailing bytes %d' % (len(r.d) - r.o)
    return {'version': ver, 'textures': textures, 'materials': mats, 'meshes': meshes, 'dummies': dummies}


if __name__ == '__main__':
    import sys, collections
    k = read_kn5(sys.argv[1], load_textures=False)
    print('version', k['version'], 'textures', len(k['textures']), 'materials', len(k['materials']),
          'meshes', len(k['meshes']), 'dummies', len(k['dummies']))
    print('tris', sum(len(m['idx']) // 3 for m in k['meshes']), 'verts', sum(len(m['pos']) for m in k['meshes']))
    print('shaders', collections.Counter(m['shader'] for m in k['materials']).most_common())
    print('AC_ dummies', sorted(n for n, _ in k['dummies'] if n.startswith('AC_'))[:80])
    import re
    pre = collections.Counter(re.match(r'\d*[A-Za-z]*', m['name']).group() for m in k['meshes'])
    print('mesh name prefixes', pre.most_common(40))
    allp = np.vstack([m['pos'] for m in k['meshes']])
    print('bounds', allp.min(0), allp.max(0))

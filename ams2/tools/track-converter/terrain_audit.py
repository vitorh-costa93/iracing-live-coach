"""Terrain material audit of a converted track (read back from the paks, independent of convert.py).

python -I terrain_audit.py "<output>/<id>/Automobilista 2" <id> [--json report.json] [--strict]

Per visual mesh: terrain class from the mesh/material/texture names (grass, gravel, road, kerb), material -> texture
referenced in the .bmt, texture present in the pak, mean RGB/alpha of the DDS actually shipped, 3D area.  Flags:
  missing/WHITE fallback texture on terrain, grass that is dark/white/not green, gravel that is dark/white/green,
  asphalt that is bright, opaque terrain whose diffuse alpha is a mask (rz_basic reads it: black/white patches).
Reference colours: Sochi GRASS01 109/131/59, SOC_GRASS_A_01_D 60/73/39, ROAD_BASE 74/74/74.
"""
import argparse, io, json, os, re, sys, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bff import Bff
from meb import read_meb
from bmt import decode, KNOWN

CLASSES = (('kerb', r'kerb|curb|rumble|cordolo|zebra'),
           ('gravel', r'gravel|grav|grvl|sand|kies|brita|ghiaia|trap'),
           ('grass', r'grass|gras|turf|lawn|meadow|prato'),
           ('road', r'(^|[\\/ ])m?\d*_?(road|asph|tarmac|pitlane|pitr|pitfloor|roff|runoff)'))
NOT_TERRAIN = r'wall|tire|tyre|line|fence|barrier|sign|light|logo|garage|build|tree|flag'


def terrain_class(*names):
    text = ' '.join(n.lower() for n in names if n)
    if re.search(NOT_TERRAIN, text): return None
    for cls, rx in CLASSES:
        if re.search(rx, text): return cls
    return None


BLOCK = {b'DXT1': 8, b'DXT3': 16, b'DXT5': 16}


def dds_level(data, level):
    """DDS holding only mip `level` of a DXT texture (None when absent): the game samples the whole chain."""
    import struct
    fmt = data[84:88]
    if data[:4] != b'DDS ' or fmt not in BLOCK: return None
    h, w = struct.unpack_from('<II', data, 12); mips = struct.unpack_from('<I', data, 28)[0] or 1
    if level >= mips: return None
    off = 128
    for lv in range(level):
        off += max(1, (max(w >> lv, 1) + 3) // 4) * max(1, (max(h >> lv, 1) + 3) // 4) * BLOCK[fmt]
    lw, lh = max(w >> level, 1), max(h >> level, 1)
    head = bytearray(data[:128]); struct.pack_into('<II', head, 12, lh, lw); struct.pack_into('<I', head, 28, 1)
    return bytes(head) + data[off:]


def dds_stats(data, level=0):
    """Mean RGBA of one mip level, straight (no premultiplied resampling, which zeroes RGB under alpha 0)."""
    from PIL import Image
    try:
        if level: data = dds_level(data, level)
        if data is None: return None
        im = Image.open(io.BytesIO(data)).convert('RGBA')
    except Exception:
        return None
    a = np.asarray(im)
    step = max(1, max(a.shape[:2]) // 256)
    a = a[::step, ::step].astype(np.float64).reshape(-1, 4)
    return [round(float(x), 1) for x in a.mean(0)]


def lum(rgb): return 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]


def anomalies(cls, tex, stats, blended, mip=None, spec=None):
    out = []
    if tex is None: return ['no texture']
    if stats is None: return ['texture missing/undecodable']
    if mip is not None and lum(stats) > 20 and not 0.6 < lum(mip) / lum(stats) < 1.6:
        out.append('mip chain lum %.0f vs mip0 %.0f' % (lum(mip), lum(stats)))
    if os.path.basename(tex.replace('\\', '/')).upper() == 'WHITE.DDS': out.append('WHITE fallback')
    r, g, b, a = stats; L = lum(stats)
    if not blended and a < 200 and cls != 'kerb': out.append('alpha mask %.0f on opaque' % a)
    if cls in ('grass', 'gravel') and spec is not None and spec > 0.2 + 1e-6: out.append('specular %.2f (Sochi grass 0.2)' % spec)
    if cls == 'grass':
        if L < 35: out.append('grass black (lum %.0f)' % L)
        elif L > 170: out.append('grass white (lum %.0f)' % L)
        elif not (g >= r * 0.9 and g >= b * 1.05): out.append('grass not green %s' % [int(r), int(g), int(b)])
    elif cls == 'gravel':
        if L < 40: out.append('gravel black (lum %.0f)' % L)
        elif L > 175: out.append('gravel white (lum %.0f)' % L)
        elif g > r * 1.12 and g > b * 1.3: out.append('gravel green %s' % [int(r), int(g), int(b)])
    elif cls == 'road':
        if L > 130: out.append('road bright (lum %.0f)' % L)
        if L < 20: out.append('road black (lum %.0f)' % L)
    return out


def audit(root, tid):
    files = {}
    pak = os.path.join(root, 'Pakfiles', 'Tracks')
    for f in sorted(os.listdir(pak)):
        if f.lower() != (tid + '.bff').lower(): continue
        b = Bff(os.path.join(pak, f))
        for e in b.entries: files[e.name.lower()] = b.read(e)
    TD = 'tracks\\%s\\' % tid.lower()
    mats = {}
    stats_cache = {}
    rows = collections.defaultdict(lambda: dict(area=0.0, meshes=0, names=[]))
    for n in sorted(x for x in files if x.endswith('.meb') and x.startswith(TD)):
        m = read_meb(files[n])
        pos = next(np.array(v, dtype=np.float64).reshape(-1, 3) for fmt, sem, idx, v in m.streams if sem == 0)
        for s in m.subs:
            mt = s['material'].lower()
            if mt not in mats:
                bm = files.get(mt[:-4] + '.bmt')
                x = decode(bm, KNOWN + ['define', 'sourceblend', 'destblend', 'blendop', 'sb', 'db', 'bo']) if bm else ''
                tex = re.findall(r'v="(tracks\\textures\\[^"]+)"', x)
                blended = 'USE_ALPHATEST' in x or bool(re.search(r'enabled e="true"', x.split('alphablendparams')[1] if 'alphablendparams' in x else ''))
                sp = re.search(r'globalSpecularFactor.*?v="([^"]+)"', x, re.S)
                mats[mt] = (tex[0].lower() if tex else None, blended, float(sp.group(1)) if sp else None)
            tex, blended, spec = mats[mt]
            if tex and tex not in stats_cache:
                stats_cache[tex] = (dds_stats(files[tex]), dds_stats(files[tex], 3)) if tex in files else (None, None)
            t = np.array(s['indices']).reshape(-1, 3)
            area = float(np.linalg.norm(np.cross(pos[t[:, 1]] - pos[t[:, 0]], pos[t[:, 2]] - pos[t[:, 0]]), axis=1).sum() / 2) if len(t) else 0.0
            cls = terrain_class(m.name.replace('_no_uv_comp', ''), os.path.basename(mt)) or terrain_class(os.path.basename(tex or ''))
            key = (os.path.basename(mt), tex, cls)
            r = rows[key]; r['area'] += area; r['meshes'] += 1
            if len(r['names']) < 3: r['names'].append(m.name.replace('_no_uv_comp', ''))
            r['blended'] = blended; r['spec'] = spec
    report = []
    for (mt, tex, cls), r in sorted(rows.items(), key=lambda kv: -kv[1]['area']):
        st, mip = stats_cache.get(tex, (None, None))
        if cls:
            bad = anomalies(cls, tex, st, r['blended'], mip, r['spec'])
        elif tex is not None and st is None:
            bad = ['texture missing']
        elif st and mip and not r['blended'] and r['area'] > 200 and lum(st) > 20 and not 0.6 < lum(mip) / lum(st) < 1.6:
            bad = ['mip chain lum %.0f vs mip0 %.0f' % (lum(mip), lum(st))]    # premultiplied mips darken any opaque surface
        else:
            bad = []
        report.append(dict(material=mt, texture=tex and os.path.basename(tex), cls=cls, area=round(r['area'], 1), meshes=r['meshes'], sample=r['names'],
                           mean=st, mip3=mip, blended=r['blended'], spec=r['spec'], anomalies=bad))
    summary = collections.defaultdict(lambda: [0.0, np.zeros(4)])
    for row in report:
        if row['cls'] and row['mean']:
            s = summary[row['cls']]; s[0] += row['area']; s[1] += row['area'] * np.array(row['mean'])
    summary = {k: dict(area=round(v[0]), mean=[round(float(x)) for x in v[1] / max(v[0], 1e-9)]) for k, v in summary.items()}
    return dict(track=tid, summary=summary, materials=report,
                anomalies=[r for r in report if r['anomalies'] and (r['area'] > 20 or r['cls'] in ('grass', 'gravel'))])


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('root'); ap.add_argument('tid'); ap.add_argument('--json'); ap.add_argument('--strict', action='store_true')
    a = ap.parse_args()
    rep = audit(a.root, a.tid)
    print('track', a.tid)
    for k, v in sorted(rep['summary'].items()): print('  %-7s area %9d m2  mean RGBA %s' % (k, v['area'], v['mean']))
    for r in rep['anomalies']:
        print('  ANOMALY %-28s tex=%-30s cls=%-6s area=%8.0f mean=%s %s' % (r['material'], r['texture'], r['cls'], r['area'], r['mean'], '; '.join(r['anomalies'])))
    print('anomalies', len(rep['anomalies']))
    if a.json: open(a.json, 'w', encoding='utf-8').write(json.dumps(rep, indent=1))
    return int(a.strict and bool(rep['anomalies']))


if __name__ == '__main__': raise SystemExit(main())

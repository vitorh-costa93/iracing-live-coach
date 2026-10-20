"""Extract AMS2 official track paks into a visible folder tree, naming entries by hash lookup when possible.
usage: python -I extract_tracks.py <outdir> physics|all [track ...]
  physics: only the *_Physics.bff of every track (aiw, csm, gcl, xml ...), ~5 GB for all tracks
  all:     physics + main pak (meshes, materials, textures); ~1.5x pak size on disk, so pass track names
"""
import sys, os, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bff import Bff
from uidhash import uid

PAK = r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2\Pakfiles\TRACKS'
BS = chr(92)


def candidates(t):
    c = []
    for rel in ['physics' + BS + t + '.csm', 'physics' + BS + 'triggers.xml', 'physics' + BS + 'object_properties.xml',
                'track_cut' + BS + t + '.gcl', t + '.trd', t + '.sgx', t + '.enx', t + '.bin', 'cameras' + BS + t + '.xml',
                'cameras' + BS + 'cameraconfig.xml', 'physics' + BS + 'surfaces.xml', 'physics' + BS + t + '.rrz',
                'physics' + BS + 'rrz.xml', 'physics' + BS + t + '.trk', 'physics' + BS + t + '.tgr']:
        c.append('tracks' + BS + t + BS + rel)
    for d in ('aiw', 'omtt', 'trd', 'gcl', 'ai'):
        for ext in ('aiw', 'bmt', 'meb', 'xml'):
            c.append('tracks' + BS + '_data' + BS + d + BS + t + '.' + ext)
    return c


def extract(pak, out, track):
    b = Bff(pak)
    names = {uid(x): x for x in candidates(track.lower())}
    os.makedirs(out, exist_ok=True)
    n = 0
    for e in b.entries:
        data = b.read(e) if hasattr(b, 'read') else None
        if data is None:
            raise SystemExit('Bff has no read(); adapt to bff.py API')
        nm = names.get(e.uid)
        rel = nm.replace(BS, os.sep) if nm else '%016x.%s' % (e.uid, e.ext)
        dst = os.path.join(out, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, 'wb').write(data); n += 1
    return n


if __name__ == '__main__':
    out, mode = sys.argv[1], sys.argv[2]
    only = {x.lower() for x in sys.argv[3:]}
    for f in sorted(glob.glob(os.path.join(PAK, '*_Physics.bff'))):
        t = os.path.basename(f)[:-len('_Physics.bff')]
        if only and t.lower() not in only:
            continue
        print(t, extract(f, os.path.join(out, t.lower()), t), flush=True)
        if mode == 'all':
            main = os.path.join(PAK, t + '.bff')
            if os.path.exists(main):
                print(t, 'main', extract(main, os.path.join(out, t.lower()), t), flush=True)

"""Resolve official (EXT-encrypted) entry names by hashing candidate paths with uidhash.uid.
usage: python -I guess.py <pak.bff> <trackdir> [extra names file]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bff import Bff
from uidhash import uid

BS = chr(92)
pak, td = sys.argv[1], sys.argv[2]
b = Bff(pak)
cands = []
for t in (td, td.lower()):
    for rel in ['physics' + BS + t + '.csm', 'physics' + BS + 'triggers.xml', 'physics' + BS + 'object_properties.xml',
                'track_cut' + BS + t + '.gcl', t + '.trd', t + '.sgx', t + '.enx', t + '.bin', 'cameras' + BS + t + '.xml',
                'cameras' + BS + 'cameraconfig.xml', 'physics' + BS + 'surfaces.xml', 'physics' + BS + t + '.rrz',
                'physics' + BS + 'rrz.xml', 'physics' + BS + t + '.trk', 'physics' + BS + t + '.tgr']:
        cands.append('tracks' + BS + t + BS + rel)
    for d in ('aiw', 'omtt', 'trd', 'gcl', 'ai'):
        for ext in ('aiw', 'bmt', 'meb', 'xml'):
            cands.append('tracks' + BS + '_data' + BS + d + BS + t + '.' + ext)
if len(sys.argv) > 3:
    cands += [l.strip() for l in open(sys.argv[3]) if l.strip()]
m = {uid(c): c for c in cands}
hit = 0
for e in b.entries:
    n = m.get(e.uid)
    hit += n is not None
    print('%3d %016x %04x %10d %s %s' % (e.index, e.uid, e.comp, e.size, e.ext, n))
print('resolved %d/%d' % (hit, b.count))

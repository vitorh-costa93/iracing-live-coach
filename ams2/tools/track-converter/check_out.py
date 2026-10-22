"""Self-consistency checks of a converted track folder (read back with the independent readers)."""
import os, re, sys, zlib, struct, math, json
import xml.etree.ElementTree as ET
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bff import Bff
from meb import read_meb
from bmt import decode, KNOWN
from uidhash import uid
from validation_contracts import pit_endpoint_errors, active_camera_coverage

root, tid = sys.argv[1], sys.argv[2]
pak = os.path.join(root, 'Pakfiles', 'Tracks')
failures = []
metrics = {}
def require(ok, message):
    if not bool(ok): failures.append(message)
files = {}
for f in sorted(os.listdir(pak)):
    b = Bff(os.path.join(pak, f))
    bad = 0
    for e in b.entries:
        raw = b.read_raw(e)
        if (zlib.crc32(raw) ^ 0xffffffff) != e.crc or e.uid != uid(e.name):
            bad += 1
        d = b.read(e)
        assert len(d) == e.size
        files[e.name.lower()] = d
    require(bad == 0, f'{f}: CRC/UID errors={bad}')
    print('%-28s entries %5d  crc/uid errors %d  name=%s' % (f, b.count, bad, b.name))
TD = 'tracks\\%s\\' % tid
mebs = [n for n in files if n.endswith('.meb') and n.startswith(TD)]
mats_needed = set(); tex_needed = set(); nt = 0
for n in mebs:
    m = read_meb(files[n]); assert m.end == len(files[n]), n
    for s in m.subs:
        mats_needed.add(s['material'].lower()); nt += len(s['indices']) // 3
        assert max(s['indices']) < m.nverts
print('meb parsed', len(mebs), 'tris', nt)
for mt in mats_needed:
    bm = files.get(mt[:-4] + '.bmt'); assert bm, mt
    x = decode(bm, KNOWN + ['define', 'sourceblend', 'destblend', 'blendop', 'sb', 'db', 'bo'])
    tex_needed.update(t.lower() for t in re.findall(r'v="(tracks\\textures\\[^"]+)"', x))
miss = [t for t in tex_needed if t not in files]
require(not miss, f'Missing textures: {miss[:10]}')
print('materials', len(mats_needed), 'textures referenced', len(tex_needed), 'missing', len(miss), miss[:5])
sgx = ET.fromstring(files[TD + tid + '.sgx'])
res = [r.get('Filename').replace('/', '\\').lower() for r in sgx.iter('RESOURCE')]
require(all(r in files for r in res), 'Scene references absent meshes')
print('sgx nodes', len(res), 'missing meb', sum(r not in files for r in res))
ET.fromstring(files[TD + 'physics\\triggers.xml']); ET.fromstring(files[TD + tid + '.trd'])
aiw = files['tracks\\_data\\aiw\\%s.aiw' % tid].decode('latin1')
from aiw_pit import parse_aiw, tup
WPS = [w for w in parse_aiw(aiw.split('[Waypoint]')[1]) if 'wp_pos' in w]
ALLW = np.array([tup(w['wp_pos']) for w in WPS])
BR = np.array([int(tup(w['wp_branchID'])[0]) for w in WPS])
PTR = np.array([[int(x) for x in tup(w['WP_PTRS'])] for w in WPS])
W = ALLW[BR == 0]
PERP = np.array([tup(w['wp_perp']) for w in WPS])[BR == 0]
PATH = np.array([tup(w['wp_path'])[0] for w in WPS])[BR == 0]
WIDTH = np.array([tup(w['wp_width']) for w in WPS])[BR == 0]
RACE = W + PATH[:,None] * PERP
require(np.isfinite(ALLW).all() and np.isfinite(RACE).all() and np.isfinite(WIDTH).all(), 'Nonfinite AI geometry')
require(np.all(WIDTH > 0), 'Nonpositive AI corridor width')
margin = np.minimum(WIDTH[:,0] + PATH, WIDTH[:,1] - PATH)
require(np.min(margin) >= 0.60, f'AI asphalt clearance too small: {np.min(margin):.3f}m')
require(np.all((PTR[:,0] >= 0) & (PTR[:,0] < len(WPS)) & (PTR[:,1] >= 0) & (PTR[:,1] < len(WPS))), 'AI previous/next pointer outside waypoint array')
main_steps = np.linalg.norm(np.roll(W,-1,axis=0)-W,axis=1)
require(np.all(main_steps > .01) and np.max(main_steps) < 12, 'Main AI discontinuity or duplicate waypoint')
require(np.all([int(w.get('wpd_CornerState','0').strip('()')) in (0,1,2,3) for w in WPS]), 'Invalid corner state')
require(any('1' == w.get('wpd_CornerState','').strip('()') for w in WPS), 'Missing corner entry/braking markers')
main_sectors = np.array([int(tup(w['wp_score'])[0]) for w in WPS if int(tup(w['wp_branchID'])[0]) == 0])
require(set(main_sectors) == {0,1,2} and np.all(np.diff(main_sectors) >= 0), 'Timing sectors missing or out of driving order')
metrics.update(main_waypoints=len(W), clearance_min=float(margin.min()), race_finite=bool(np.isfinite(RACE).all()))
a = 0.5 * np.sum(W[:, 0] * np.roll(W[:, 2], -1) - np.roll(W[:, 0], -1) * W[:, 2])
print('aiw waypoints', len(W), 'signed area %.0f (Sochi is negative)' % a)
for blk in ('GRID', 'PITS'):
    sec = aiw.split('[%s]' % blk)[1].split('\n[')[0]
    P = re.findall(r'(?:Pos|PitPos)=\(([-\d.]+),([-\d.]+),([-\d.]+)\)', sec)
    O = re.findall(r'(?:Ori|PitOri)=\(0\.0+,([-\d.]+),0\.0+\)', sec)
    errs = []
    for p, o in zip(P, O):
        p = np.array(list(map(float, p))); y = float(o)
        i = int(np.argmin(np.linalg.norm(W - p, axis=1)))
        d = W[(i + 1) % len(W)] - W[i - 1]; d = d / np.linalg.norm(d)
        f = np.array([-math.sin(y), 0, -math.cos(y)])
        errs.append((round(float(np.linalg.norm(W[i] - p)), 1), round(math.degrees(math.acos(max(-1, min(1, f @ d)))), 1), i))
    require(len(P) >= 2, f'{blk}: fewer than two positions')
    if blk == 'GRID': require(all(e[1] < 45 for e in errs), 'Grid points against the driving direction')
    print(blk, len(P), '(dist to line m, heading err deg, wp) first:', errs[:4])
# does the AI line lie on the road meshes? sample vertical gap to nearest mesh vertex
allv = np.vstack([np.array(read_meb(files[n]).streams[0][3]).reshape(-1, 3) for n in mebs])
from collections import defaultdict
grid = defaultdict(list)
for v in allv[::3]:
    grid[(int(v[0] // 4), int(v[2] // 4))].append(v[1])
gaps = []
for w in W[::20]:
    ys = grid.get((int(w[0] // 4), int(w[2] // 4)))
    if ys:
        gaps.append(min(abs(y - w[1]) for y in ys))
print('AI line vs nearest mesh vertex height: median %.2f m, max %.2f m over %d samples' % (np.median(gaps), max(gaps), len(gaps)))

# ---- AIW pit branch topology (rules from Sochi, SPEC 8)
nmain = len(W); n_all = len(WPS)
assert int(re.search(r'number_waypoints=(\d+)', aiw).group(1)) == n_all
pitlanes = int(re.search(r'pitlanes=(\d+)', aiw).group(1))
require(pitlanes > 0 or '--require-pits' not in sys.argv, 'Missing pit lane branch')
if pitlanes:
    pit = np.nonzero(BR == 1)[0]; spurs = np.nonzero(BR >= 2)[0]
    first, last = pit[0], pit[-1]
    fork = PTR[first][0]; join = PTR[last][1]
    errs = pit_endpoint_errors(BR,PTR)
    if PTR[fork][2] != first: errs.append('fork 3rd ptr')
    if not (fork < nmain and join < nmain): errs.append('fork/join not on main')
    if PTR[first][1] != first+1 or PTR[first][3] != 1: errs.append('pit first next/branch')
    if PTR[last][0] != last-1 or PTR[last][2] != join or PTR[last][3] != 1: errs.append('pit last prev/link/branch')
    for i in pit[1:-1]:
        if PTR[i][0] != i - 1 or PTR[i][1] != i + 1 or PTR[i][3] != 1: errs.append(('pit ptr', i)); break
    for i in spurs:
        if PTR[i][1] != i or not (BR[PTR[i][0]] == 1 and BR[PTR[i][2]] == 1) or PTR[i][3] != BR[i]: errs.append(('spur', i)); break
    for i in range(nmain):
        if PTR[i][0] != (i - 1) % nmain or PTR[i][1] != (i + 1) % nmain: errs.append(('main ptr', i)); break
    sc = [tup(WPS[i]['wp_score'])[1] for i in pit]
    pl = np.linalg.norm(np.diff(ALLW[pit], axis=0), axis=1)
    print('pit branch: %d wps (%.0f m, spacing %.1f), fork %d -> join %d, spurs %d, gap fork-first %.1f m, last-join %.1f m, score monotone %s, errors %s'
          % (len(pit), pl.sum(), np.median(pl), fork, join, len(spurs), np.linalg.norm(ALLW[fork] - ALLW[first]),
             np.linalg.norm(ALLW[last] - ALLW[join]), all(b > a_ for a_, b in zip(sc, sc[1:])), errs))
    require(not errs, f'Pit topology errors: {errs}')
    require(all(b > a_ for a_, b in zip(sc, sc[1:])), 'Pit scores are not monotone')
    require(len(spurs) >= 2, 'Missing pit stall spurs')
    require(np.linalg.norm(ALLW[fork]-ALLW[first]) < 25 and np.linalg.norm(ALLW[last]-ALLW[join]) < 25, 'Pit fork/join has a discontinuity >25m')
    pp = np.array([list(map(float, g)) for g in re.findall(r'PitPos=\(([-\d.]+),([-\d.]+),([-\d.]+)\)', aiw)])
    print('  PitPos -> nearest pit-lane wp: median %.1f m, max %.1f m; spur wp == PitPos: %s' % (
        np.median([np.linalg.norm(ALLW[pit] - p, axis=1).min() for p in pp]),
        max(np.linalg.norm(ALLW[pit] - p, axis=1).min() for p in pp),
        all(np.linalg.norm(ALLW[spurs] - p, axis=1).min() < 1e-3 for p in pp)))

    require(all(np.linalg.norm(ALLW[spurs] - p, axis=1).min() < 1e-3 for p in pp), 'Pit stall without matching AI spur')

# ---- collision (.csm): parse every cooked mesh, walk the R-tree, ray-cast down from the AI line
from csm import read_csm, parse_mesh, check_tree
from csm_write import ray_down_tree
head, ms = read_csm(files[TD + 'physics\\%s.csm' % tid])
import collections
bad = 0; sids = collections.Counter(); meshes = []
for blob, sid in ms:
    m = parse_mesh(blob); assert m.end == len(blob)
    ok, miss_, dup, pr = check_tree(m)
    bad += (not ok) or bool(pr); sids[sid] += len(m.tris); meshes.append((m, sid))
print('csm head %d meshes %d tree errors %d tris by surface id %s' % (head, len(ms), bad, dict(sids)))
require(bad == 0, f'CSM tree errors: {bad}')
hits = []; nohit = 0; hit_sid = collections.Counter(); bad_samples = []
samples = np.vstack([RACE, ALLW[BR != 0]])
for sample_index, w in enumerate(samples):
    best = None
    for m, sid in meshes:
        if not (m.aabb[0] <= w[0] <= m.aabb[3] and m.aabb[2] <= w[2] <= m.aabb[5]):
            continue
        y = ray_down_tree(m, w[0], w[2], max_y=w[1]+2.0)
        if y is not None and y < w[1] + 2.0 and (best is None or y > best[0]):
            best = (y, sid)
    if best is None:
        nohit += 1
    else:
        hits.append(w[1] - best[0]); hit_sid[best[1]] += 1
        if abs(w[1] - best[0]) >= 2 or best[1] not in (0,10,46):
            bad_samples.append(dict(index=sample_index, position=w.tolist(), ground_y=float(best[0]), surface=int(best[1])))
print('ray down from actual racing line (all main + pit/stall waypoints): %d hits, %d misses, |dy| median %.2f max %.2f m, surface ids %s'
      % (len(hits), nohit, np.median(np.abs(hits)) if hits else float('nan'), np.max(np.abs(hits)) if hits else float('nan'), dict(hit_sid)))
require(nohit == 0, f'AI racing/pit line collision misses={nohit}')
# 46 = paved run-off (asphalt-like grip): tolerated where the line grazes the road edge, but only a sliver of the line
require(not (set(hit_sid) - {0,10,46}), f'AI racing/pit line crosses non-road surfaces: {dict(hit_sid)}')
require(hit_sid.get(46, 0) <= 0.01 * max(1, len(hits)), f'AI racing/pit line runs on paved run-off (46): {hit_sid.get(46, 0)}/{len(hits)}')
require(hits and np.max(np.abs(hits)) < 2, 'AI racing line >2m away from collision surface')
metrics.update(collision_misses=nohit, collision_surface_samples=dict(hit_sid), collision_bad_samples=bad_samples)
if bad_samples: print('collision bad samples:', json.dumps(bad_samples[:12]))

# ---- track limits (.gcl)
from gcl import read_gcl, write_gcl, sat_xz
g = read_gcl(files[TD + 'track_cut\\%s.gcl' % tid])
assert g['end'] == len(files[TD + 'track_cut\\%s.gcl' % tid])
G = g['tris']['v'].astype(np.float64); GF = g['tris']['f']
same = write_gcl(G, GF) == files[TD + 'track_cut\\%s.gcl' % tid]
nY = np.cross(G[:, 1] - G[:, 0], G[:, 2] - G[:, 0])[:, 1]
PX = G[:, :, [0, 2]]
def inside(p, flag):
    ids = np.nonzero(GF == flag)[0]
    return len(sat_xz(PX, ids, p[0] - 0.01, p[2] - 0.01, 0.02)) > 0
cov_main = np.mean([inside(w, 1) for w in RACE])
cov_pit = np.mean([inside(w, 2) for w in ALLW[BR == 1]]) if pitlanes else 0
print('gcl tris %d flags %s, +y normals %s, grid %dx%d, re-encode identical %s, main wps inside flag-1 %.3f, pit wps inside flag-2 %.2f'
      % (g['n'], dict(collections.Counter(GF.tolist())), bool((nY > 0).all()), g['ncx'], g['ncz'], same, cov_main, cov_pit))

# ---- cameras
cx = ET.fromstring(files['cameras\\%s.xml' % tid])
cams = cx.findall('.//data[@class="CTrackingCamData"]'); areas = cx.findall('.//data[@class="CSphereArea"]')
refs = []
for c in cams:
    fp = c.find('prop[@name="ActiveAreas"]/funcpropdata')
    refs += [int(v) for k, v in fp.attrib.items() if k.startswith('areaIndex')]
cent = np.array([[float(x) for x in a_.find('prop[@name="Centre"]').get('data').split(';')] for a_ in areas])
rad = np.array([float(a_.find('prop[@name="Radius"]').get('data')) for a_ in areas])
valid_refs = sorted(set(r for r in refs if 0 <= r < len(areas)))
covered = active_camera_coverage(cent,rad,refs,np.vstack([W,RACE]))
require(bool(valid_refs), 'No camera has an active track area')
print('cameras %d areas %d refs %d (unique %d, out of range %d), main wps inside some area %.3f'
      % (len(cams), len(areas), len(refs), len(set(refs)), sum(r >= len(areas) for r in refs), covered))

require(same, 'GCL read/write roundtrip differs')
require((nY > 0).all(), 'GCL has downward or degenerate triangles')
require(cov_main == 1, f'Racing line outside GCL drivable corridor: coverage={cov_main:.6f}')
require(not pitlanes or cov_pit > 0, 'Pit lane has no limiter zone')
require(len(cams) > 0 and len(areas) > 0, 'Missing TV cameras')
require(all(0 <= r < len(areas) for r in refs), 'Camera reference out of range')
require(len(refs) == len(set(refs)), 'Duplicate camera-area ownership')
require(covered == 1, f'Incomplete camera coverage: {covered:.6f}')
metrics.update(track_id=tid, passed=not failures, failures=failures, gcl_coverage=float(cov_main), camera_coverage=float(covered), pit_lane=bool(pitlanes))
if '--json' in sys.argv:
    report = sys.argv[sys.argv.index('--json')+1]
    with open(report, 'w', encoding='utf-8') as output: json.dump(metrics, output, indent=2)
for error in failures: print('FAIL:', error)
print('VALIDATION:', 'PASS' if not failures else 'FAIL')
raise SystemExit(1 if failures else 0)

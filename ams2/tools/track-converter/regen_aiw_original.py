"""Regenerate the main AI line of an ORIGINAL AMS2 track (official, RC4-encrypted Physics.bff) into a staging folder.

python -I regen_aiw_original.py plan [--game <AMS2 folder>]
python -I regen_aiw_original.py stage <Track> <out_dir> [--game <AMS2 folder>] [--margin 1.2] [--max-shift M]

The game folder is only read.  stage writes
  <out_dir>\\<Track>\\Pakfiles\\Tracks\\<Track>_Physics.bff   (only the AIW entry differs, see bff_write.patch_official)
  <out_dir>\\<Track>\\<Track>.aiw, <out_dir>\\<Track>\\report.json, <out_dir>\\<Track>\\backup\\<Track>_Physics.bff (+ .sha256)
and refuses (exit 1, nothing but report.json written) when an acceptance check fails.

Same line logic as the converted Bahrain (convert.build_aiw with --ai-brake-physics --ai-dry2 --ai-width-rule f1), with the
original wp_path as the reference line instead of the AC/F1 line:
  corridor  = csm asphalt (surface id 0) n GCL flag 1 n wp_width, `margin` m inside; walls (csm ids 12/13/14) WALL_CLEAR off;
  line      = convert.racing_line(mode 'fastest', physics=True): minimum curvature anchored to the original line, brake-zone
              weights, corridor_fit (no clip kinks); candidates = corridor-fitted original, 'fastest', lap_time_polish and
              pinned_polish (polish re-optimised with waypoints pinned wherever it would turn tighter than the original
              nearby or kink more than the original's worst kink); every candidate passes curvature_guard and the fastest
              one within criterion 4 wins.  Tracks whose original runs on the kerbs/near walls may need --margin 1.0
              (Long_Beach: 1.2 m -> 83.24 s > 82.96 s original, refused; 1.0 m -> 82.84 s, accepted);
  pit ramp  = original line pinned +-PIN wps around the pit fork/join, raised-cosine blend to the new line over RAMP wps;
  dry2      = convert.secondary_line(physics=True) where the original has wp_dry2 (never added, see DECISIONS);
  dwidth    = F1 rule (asphalt/kerb/paved run-off + <= 2 m grass, floor width + 1 m, capped by any wall - WALL_CLEAR and
              width + 12 m), never below the original nor wp_width, unchanged in the pit ramp.
DECISIONS (documented in report.json too):
  - wpd_CornerType/State, wp_event, wp_galpha stay ORIGINAL: hand-made by Reiza (galpha fades 0.018..0.432, corner states with
    state 1 runs, event 2 at 1.05) and the corners do not move; corner_marks() of the new line is only reported (agreement).
  - wp_groove_lat = -0.27 * path only on waypoints whose path moved > 5 cm (keeps the drawn groove 73 % of the way to the line);
    other waypoints keep the original value.
  - no wp_dry2 is added to AIWs without it (55/229 originals have it; adding it changes every block's line count).
  - times / timesDry2 are scaled by est_laptime(new) / est_laptime(original), keeping Reiza's calibration.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import sys

import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from bff import Bff
from bff_write import patch_official
from csm import read_csm, parse_mesh, check_tree
from csm_write import ray_down_tree
from gcl import read_gcl, sat_xz
from uidhash import uid
from repair_aiw import LateralSurface, safe_corridor, dense_clearance
import convert as cv

GAME = r'C:\Program Files (x86)\Steam\steamapps\common\Automobilista 2'
WALL_IDS = (12, 13, 14)
DRIVE_IDS = (0, 10, 46)          # asphalt, kerb, paved run-off
GRASS_IDS = (7, 32)              # grass, astroturf (AI_GRASS metres allowed in dwidth)
CHECK_OK_IDS = {0, 10, 46, 7, 8, 32}
PIN, RAMP = 3, 15                # waypoints pinned to the original line / blended around the pit fork and join
GROOVE_MOVE = 0.05
POLISH = (100.0, 10.0, 3.0)  # lap_time_polish continuation (stops before the kink-prone mu 0.1)
DENSE_MIN, DENSE_TARGET = 1.0, 1.08  # criterion 2 limit / bound-tightening target between waypoints
GUARD_WIN, GUARD_PIN, GUARD_RAMP = 5, 2, 12   # curvature guard: local original max over +-5 wps; held/blend widths
PIN_HALF, POLISH_REFINE = 3, (10.0, 3.0)   # pinned polish: half window pinned per violation, re-optimisation schedule
CURV_TOL = 0.05                  # criterion 4: new max |k| and max kink <= original * (1 + CURV_TOL) + 1e-4 1/m
ALLOWED_MAIN = {'wp_path', 'wp_dry2', 'wp_dwidth', 'wp_groove_lat'}
ALLOWED_HEADER = {'times', 'timesDry2'}


# ---------------------------------------------------------------- AIW text (latin1, CRLF kept)
VAL = r'\(([^)\r\n]*)\)'


def split_aiw(text):
    prefix, wt = text.split('[Waypoint]', 1)
    parts = re.split(r'(?m)^(\\\\[^\r\n]*\r?\n)', wt)
    blocks = [parts[i] + parts[i + 1] for i in range(1, len(parts), 2)]
    return prefix, parts[0], blocks


def field(block, key):
    m = re.findall(r'(?m)^' + re.escape(key) + '=' + VAL, block)
    if len(m) != 1:
        raise ValueError('Missing or duplicated field %s' % key)
    return [float(x) for x in m[0].split(',')]


def has(block, key):
    return re.search(r'(?m)^' + re.escape(key) + '=', block) is not None


def tokens(block, key):
    return re.findall(r'(?m)^' + re.escape(key) + '=' + VAL, block)[0].split(',')


def fmt_like(tok, v):
    """Format v with the decimals of the original token (-0.0000 avoided)."""
    dec = len(tok.split('.')[1]) if '.' in tok else 0
    s = '%.*f' % (dec, v)
    return s[1:] if re.fullmatch(r'-0\.?0*', s) else s


def set_field(block, key, toks):
    new, n = re.subn(r'(?m)^(' + re.escape(key) + r'=)\([^)\r\n]*\)', lambda m: m.group(1) + '(' + ','.join(toks) + ')', block)
    if n != 1:
        raise ValueError('Missing or duplicated field %s' % key)
    return new


def features(prefix):
    sec = prefix.split('[Features]', 1)[1].split('[', 1)[0] if '[Features]' in prefix else ''
    return {k.strip(): v.strip() for k, v in (l.split('=', 1) for l in sec.splitlines() if '=' in l)}


def topology(blocks):
    br = np.array([int(field(b, 'wp_branchID')[0]) for b in blocks])
    ptr = np.array([[int(x) for x in field(b, 'WP_PTRS')] for b in blocks])
    """Main loop in driving order (walk of the next pointers over branch 0), branch ids, pointers and the loop positions of
    the pit fork/join waypoints.  Raises for open (point-to-point) or broken main branches."""
    main = np.flatnonzero(br == 0)
    n = len(main)
    if n < 50:
        raise ValueError('main branch too short (%d wps)' % n)
    order = [int(main[0])]
    for _ in range(n - 1):
        nxt = int(ptr[order[-1], 1])
        if not (0 <= nxt < len(blocks)) or br[nxt] != 0 or int(ptr[nxt, 0]) != order[-1]:
            raise ValueError('main branch is not a closed loop (point-to-point / open layout)')
        order.append(nxt)
    if len(set(order)) != n or int(ptr[order[-1], 1]) != order[0] or int(ptr[order[0], 0]) != order[-1]:
        raise ValueError('main branch is not a closed loop (point-to-point / open layout)')
    order = np.array(order)
    P = np.array([field(blocks[i], 'wp_pos') for i in order])
    step = np.linalg.norm(np.roll(P, -1, 0) - P, axis=1)
    long_ = step > max(12.0, 2.5 * np.median(step))
    # one longer closing segment (lap wrap, 14-18 m on 30 originals) is tolerated
    if step.min() < .01 or step.max() > 20.0 or long_.sum() > 1:
        raise ValueError('main loop discontinuity %.1f m at %d segments (point-to-point layout?)' % (step.max(), long_.sum()))
    pos = {int(i): k for k, i in enumerate(order)}
    pit = np.flatnonzero(br == 1)
    j = {int(ptr[i, 0]) for i in pit} | {int(ptr[i, 1]) for i in pit} | {int(i) for i in order if ptr[i, 2] >= 0 and br[ptr[i, 2]] == 1}
    forks = sorted(pos[i] for i in j if i in pos)
    return order, br, ptr, forks


def refusal(name, text, pak_uids, tid):
    """None if the track is eligible, else the reason."""
    if re.search(r'kart', name, re.I):
        return 'kart track'
    prefix, _, blocks = split_aiw(text)
    f = features(prefix)
    for k in ('Oval', 'RallyCross', 'IceTrack', 'IceTrackSolo'):
        if f.get(k, '0') not in ('0', '0.000000'):
            return '%s=%s' % (k, f[k])
    for what, path in (('csm', 'tracks/%s/physics/%s.csm' % (tid, tid)), ('gcl', 'tracks/%s/track_cut/%s.gcl' % (tid, tid))):
        if uid(path) not in pak_uids:
            return 'no %s entry under tracks\\%s' % (what, tid)
    try:
        topology(blocks)
    except ValueError as e:
        return str(e)
    return None


# ---------------------------------------------------------------- pak access
def open_track(game, track):
    pak = Path(game) / 'Pakfiles' / 'TRACKS' / (track + '_Physics.bff')
    if not pak.exists():
        raise ValueError('No official pak %s' % pak)
    b = Bff(str(pak))
    if not b.key_index:
        raise ValueError('%s is not an official (encrypted) pak' % pak.name)
    E = {e.uid: e for e in b.entries}
    tid = track.lower()
    a = uid('tracks/_data/aiw/%s.aiw' % tid)
    if a not in E:
        raise ValueError('No tracks\\_data\\aiw\\%s.aiw entry' % tid)
    return pak, b, E, tid, a


def surfaces(b, E, tid):
    csm = b.read(E[uid('tracks/%s/physics/%s.csm' % (tid, tid))])
    meshes = [(parse_mesh(blob), sid) for blob, sid in read_csm(csm)[1]]
    tri = lambda ids: np.vstack([m.verts[m.tris] for m, s in meshes if s in ids] or [np.zeros((0, 3, 3))]).astype(float)
    road = LateralSurface(tri((0,)))
    drive = LateralSurface(tri(DRIVE_IDS))
    grass = LateralSurface(tri(GRASS_IDS)) if len(tri(GRASS_IDS)) else None
    allt = np.vstack([m.verts[m.tris] for m, _ in meshes]).astype(float)
    sids = np.concatenate([np.full(len(m.tris), s) for m, s in meshes])
    nrm = np.cross(allt[:, 1] - allt[:, 0], allt[:, 2] - allt[:, 0])
    ny = np.abs(nrm[:, 1]) / (np.linalg.norm(nrm, axis=1) + 1e-12)
    walls = Walls(allt[np.isin(sids, WALL_IDS)])
    # dwidth widening only: any steep collision face that is not a drivable/terrain surface also counts as a wall
    obstacles = Walls(allt[np.isin(sids, WALL_IDS) | ((ny < 0.5) & ~np.isin(sids, DRIVE_IDS + GRASS_IDS + (8,)))])
    g = read_gcl(b.read(E[uid('tracks/%s/track_cut/%s.gcl' % (tid, tid))]))
    limits = LateralSurface(g['tris']['v'][g['tris']['f'] == 1])
    return dict(meshes=meshes, road=road, drive=drive, grass=grass, walls=walls, obstacles=obstacles, limits=limits, gcl=g)


class Walls:
    """Horizontal rays P + t*perp (t in [-reach, reach]) at car heights against collision triangles (Moller-Trumbore)."""
    def __init__(self, tri):
        self.t = np.asarray(tri, float).reshape(-1, 3, 3)
        self.mn, self.mx = self.t.min(1), self.t.max(1)

    def free(self, p, perp, reach=30.0, heights=(0.3, 1.0)):
        dirv = np.array([perp[0], 0.0, perp[2]]); dirv /= np.linalg.norm(dirv)
        out = [reach, reach]                         # [-perp side, +perp side]
        if not len(self.t):
            return out
        sel = ((self.mn[:, 0] <= p[0] + reach) & (self.mx[:, 0] >= p[0] - reach) & (self.mn[:, 2] <= p[2] + reach)
               & (self.mx[:, 2] >= p[2] - reach) & (self.mn[:, 1] <= p[1] + max(heights)) & (self.mx[:, 1] >= p[1] + min(heights)))
        T = self.t[sel]
        if not len(T):
            return out
        e1, e2 = T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]
        h = np.cross(np.broadcast_to(dirv, e2.shape), e2); a = np.einsum('ij,ij->i', e1, h)
        ok = np.abs(a) > 1e-10
        for hgt in heights:
            o = p + np.array([0.0, hgt, 0.0])
            s = o - T[:, 0]
            with np.errstate(divide='ignore', invalid='ignore'):
                u = np.einsum('ij,ij->i', s, h) / a
                q = np.cross(s, e1); v = (q @ dirv) / a
                t = np.einsum('ij,ij->i', e2, q) / a
            hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (np.abs(t) <= reach)
            if hit.any():
                tn, tp = -t[hit & (t < 0)], t[hit & (t > 0)]
                if len(tn): out[0] = min(out[0], float(tn.min()))
                if len(tp): out[1] = min(out[1], float(tp.min()))
        return out


def reach_sides(S, p, perp, width):
    """Free drivable distance per side (-perp, +perp) from p: contiguous asphalt/kerb/paved run-off, then <= AI_GRASS grass."""
    out = []
    iv = S['drive'].intervals(p, perp)
    cur = next(((lo, hi) for lo, hi in iv if lo <= 0 <= hi), (-width[0], width[1]))
    for side, edge in ((0, -cur[0]), (1, cur[1])):
        r = edge
        if S['grass'] is not None:
            for lo, hi in S['grass'].intervals(p, perp):
                a, b = (-hi, -lo) if side == 0 else (lo, hi)     # distances along this side
                if a <= r + 0.1 < b:
                    r += min(b - r, cv.AI_GRASS)
                    break
        out.append(max(r, width[side]))
    return out


# ---------------------------------------------------------------- metrics
def curvature_stats(L):
    k, _ = cv.line_curvature(L)
    d = np.roll(L, -1, 0) - L; d[:, 1] = 0; ds = np.linalg.norm(d, axis=1) + 1e-9
    h = np.unwrap(np.arctan2(d[:, 0], d[:, 2])); kr = np.angle(np.exp(1j * (np.roll(h, -1) - h))) / ds
    kink = np.abs(kr - 0.5 * (np.roll(kr, 1) + np.roll(kr, -1)))
    return dict(k_max=float(np.abs(k).max()), kink_max=float(kink.max()), kink_p99=float(np.percentile(kink, 99)))


def physics_checks(text, S):
    """check_out.py subset that applies to an unnamed official physics pak (AIW + csm + gcl); failures as stable keys."""
    _, _, blocks = split_aiw(text)
    order, br, ptr, _ = topology(blocks)
    mb = [blocks[i] for i in order]
    P = np.array([field(b, 'wp_pos') for b in blocks]); perp = np.array([field(b, 'wp_perp') for b in mb])
    path = np.array([field(b, 'wp_path')[0] for b in mb]); width = np.array([field(b, 'wp_width') for b in mb])
    race = P[order] + path[:, None] * perp
    other = P[br != 0]
    fail = []
    margin = np.minimum(width[:, 0] + path, width[:, 1] - path)
    if not (np.isfinite(P).all() and np.isfinite(race).all()): fail.append('nonfinite')
    if np.min(margin) < 0.6: fail.append('width_clearance<0.6')
    if not np.all((ptr[:, :2] >= 0) & (ptr[:, :2] < len(blocks))): fail.append('ptr_range')
    cs = [int(field(b, 'wpd_CornerState')[0]) for b in blocks]
    if not set(cs) <= {0, 1, 2, 3}: fail.append('corner_state')
    if 1 not in cs: fail.append('no_corner_entry')
    sec = np.array([int(field(b, 'wp_score')[0]) for b in mb])
    if set(sec) != {0, 1, 2}: fail.append('sectors')
    hits = []; nohit = 0; sid_count = {}
    for w in np.vstack([race, other]):
        best = None
        for m, sid in S['meshes']:
            if not (m.aabb[0] <= w[0] <= m.aabb[3] and m.aabb[2] <= w[2] <= m.aabb[5]):
                continue
            y = ray_down_tree(m, w[0], w[2], max_y=w[1] + 2.0)
            if y is not None and (best is None or y > best[0]):
                best = (y, sid)
        if best is None:
            nohit += 1
        else:
            hits.append(abs(w[1] - best[0])); sid_count[best[1]] = sid_count.get(best[1], 0) + 1
    n_ai = max(1, len(hits))
    off_ids = sum(v for k, v in sid_count.items() if k not in CHECK_OK_IDS)
    if nohit: fail.append('collision_miss')
    if off_ids: fail.append('non_road_surface')
    if sid_count.get(46, 0) > 0.02 * n_ai: fail.append('paved_runoff>2%')
    if sum(sid_count.get(i, 0) for i in (7, 8, 32)) > 0.002 * n_ai: fail.append('off_paved>0.2%')
    if not hits or max(hits) >= 2: fail.append('line>2m_from_collision')
    G = S['gcl']['tris']['v'].astype(np.float64); GF = S['gcl']['tris']['f']; PX = G[:, :, [0, 2]]; ids = np.flatnonzero(GF == 1)
    cov = float(np.mean([len(sat_xz(PX, ids, w[0] - .01, w[2] - .01, .02)) > 0 for w in race]))
    if cov < 1: fail.append('gcl_coverage<1')
    return dict(failures=fail, width_clearance_min=float(margin.min()), collision_misses=nohit, off_road_samples=off_ids,
                surface_samples={str(k): v for k, v in sorted(sid_count.items())}, gcl_coverage=cov,
                collision_dy_max=float(max(hits)) if hits else None)


def text_diff(old, new, main_ids, has_dry2):
    """Criterion 1: only the allowed keys change, only on main waypoints (and times/timesDry2 in the header)."""
    po, ho, bo = split_aiw(old); pn, hn, bn = split_aiw(new)
    errs = []
    if po != pn: errs.append('text before [Waypoint] changed')
    lo_, ln_ = ho.splitlines(True), hn.splitlines(True)
    if len(lo_) != len(ln_): errs.append('header line count changed')
    for a, b in zip(lo_, ln_):
        if a != b and a.split('=', 1)[0] not in ALLOWED_HEADER: errs.append('header line changed: ' + a.strip())
    if len(bo) != len(bn): errs.append('waypoint count changed')
    changed = {}
    for i, (a, b) in enumerate(zip(bo, bn)):
        if a == b:
            continue
        if i not in main_ids:
            errs.append('pit/branch waypoint %d changed' % i); continue
        la, lb = a.splitlines(True), b.splitlines(True)
        if len(la) != len(lb):
            errs.append('waypoint %d line count changed' % i); continue
        for x, y in zip(la, lb):
            if x == y:
                continue
            k = x.split('=', 1)[0]
            if k not in ALLOWED_MAIN or (k == 'wp_dry2' and not has_dry2):
                errs.append('waypoint %d key %s changed' % (i, k)); continue
            # only the value changes: same key, same trailing comment / line ending
            if re.sub(VAL, '()', x, 1) != re.sub(VAL, '()', y, 1):
                errs.append('waypoint %d %s comment/format changed' % (i, k))
            if k == 'wp_path' and tokens(a, k)[1:] != tokens(b, k)[1:]:
                errs.append('waypoint %d wet line changed' % i)
            changed[k] = changed.get(k, 0) + 1
    return errs, changed


# ---------------------------------------------------------------- regeneration
def segment_shortfall(line, S, lo, hi, path, perp):
    """Sample every chord of the serialized closed line at < 1 m (as dense_clearance) and, where the asphalt clearance is
    below DENSE_TARGET or the point leaves GCL flag 1, move the bound on that side at both chord ends past the current
    offset (in place).  Returns the number of tightened bounds."""
    n, count = len(line), 0
    for i in range(n):
        a, b = line[i], line[(i + 1) % n]
        t = b - a; t[1] = 0; ln = np.linalg.norm(t)
        sp = np.array([t[2], 0, -t[0]]) / ln
        need = [0.0, 0.0]                       # push toward +perp (raise lo) / toward -perp (lower hi)
        for f in np.linspace(0, 1, int(np.ceil(np.linalg.norm(b - a))) + 1, endpoint=False):
            q = a * (1 - f) + b * f
            iv = [(l, h) for l, h in S['road'].intervals(q, sp) if l <= 0 <= h]
            gl = S['limits'].intervals(q, sp)
            if iv:
                l, h = iv[0]
                if -l < DENSE_TARGET: need[0] = max(need[0], DENSE_TARGET + l)
                if h < DENSE_TARGET: need[1] = max(need[1], DENSE_TARGET - h)
            if not any(l <= 0 <= h for l, h in gl) and gl:
                l, h = min(gl, key=lambda x: min(abs(x[0]), abs(x[1])))
                if l > 0: need[0] = max(need[0], l + 0.1)
                else: need[1] = max(need[1], -h + 0.1)
        sgn = 1.0 if np.dot(sp, perp[i]) >= 0 else -1.0      # chord perp vs wp_perp orientation
        if sgn < 0: need = need[::-1]
        for k in (i, (i + 1) % n):
            if need[0] > 1e-6:
                lo[k] = max(lo[k], path[k] + need[0] + 0.02); count += 1
            if need[1] > 1e-6:
                hi[k] = min(hi[k], path[k] - need[1] - 0.02); count += 1
    return count


def raw_kink(L):
    d = np.roll(L, -1, 0) - L; d[:, 1] = 0; ds = np.linalg.norm(d, axis=1) + 1e-9
    h = np.unwrap(np.arctan2(d[:, 0], d[:, 2])); kr = np.angle(np.exp(1j * (np.roll(h, -1) - h))) / ds
    return np.abs(kr - 0.5 * (np.roll(kr, 1) + np.roll(kr, -1)))


def pinned_polish(P, perp, lo, hi, base, wr, kcap, kink_cap, rounds=12):
    """Lap-time polish of the corridor-fitted original (convert.lap_time_polish) that may not tighten corners: wherever
    the polished line turns tighter than the original nearby (kcap) or kinks more than the original's worst kink, the
    waypoints +-PIN_HALF around it are pinned to `base` and the rest is re-optimised, until no unpinned waypoint
    violates.  Returns the fastest clean round (or `base` if none)."""
    n = len(P); lo2, hi2 = lo.copy(), hi.copy(); pinned = np.zeros(n, bool)
    d = cv.lap_time_polish(P, perp, lo, hi, base, schedule=POLISH)
    best = (cv.est_laptime(P + base[:, None] * perp), base)
    for _ in range(rounds):
        dd = np.round(np.clip(base + wr * (d - base), lo, hi), 4); L = P + dd[:, None] * perp
        k = np.abs(cv.line_curvature(L)[0])
        v = np.flatnonzero(((k > kcap) | (raw_kink(L) > kink_cap)) & ~pinned)
        if not len(v):
            t = cv.est_laptime(L)
            if t < best[0]:
                best = (t, dd)
            break
        for j in v:
            idx = np.arange(j - PIN_HALF, j + PIN_HALF + 1) % n; lo2[idx] = hi2[idx] = base[idx]; pinned[idx] = True
        d = cv.lap_time_polish(P, perp, lo2, hi2, np.clip(d, lo2, hi2), schedule=POLISH_REFINE)
    return best[1]


def curvature_guard(P, perp, base, cand, wr, kcap, kink_cap, lo, hi, rounds=40):
    """Criterion 4 by construction: blend the candidate back to `base` (corridor-fitted original) around every waypoint
    where it turns tighter than the original nearby (kcap) or kinks more than the original's worst kink (raised-cosine
    windows, PIN/RAMP-like).  Returns (offsets rounded as serialized, number of waypoints fully held on base)."""
    n = len(base); alpha = np.ones(n)
    for _ in range(rounds):
        d = np.round(np.clip(base + alpha * wr * (cand - base), lo, hi), 4)
        L = P + d[:, None] * perp
        k = np.abs(cv.line_curvature(L)[0]); kk = raw_kink(L)
        viol = np.flatnonzero((k > kcap) | (kk > kink_cap))
        if not len(viol):
            break
        alpha = np.minimum(alpha, ramp_weights(n, viol, pin=GUARD_PIN, ramp=GUARD_RAMP))
    return d, int((alpha == 0).sum())


def ramp_weights(n, junctions, pin=PIN, ramp=RAMP):
    w = np.ones(n)
    for j in junctions:
        for i in range(n):
            g = min((i - j) % n, (j - i) % n)
            v = 0.0 if g <= pin else 1.0 if g >= pin + ramp else 0.5 - 0.5 * math.cos(math.pi * (g - pin) / ramp)
            w[i] = min(w[i], v)
    return w


def regen(text, S, margin=1.2, max_shift=None, log=print):
    if not np.isfinite(margin) or margin < 1.0:
        raise ValueError('Asphalt margin must be finite and at least 1 m')
    prefix, header, blocks = split_aiw(text)
    order, br, ptr, forks = topology(blocks)
    n = len(order)
    mb = [blocks[i] for i in order]
    P = np.array([field(b, 'wp_pos') for b in mb]); perp = np.array([field(b, 'wp_perp') for b in mb])
    width = np.array([field(b, 'wp_width') for b in mb]); dwidth = np.array([field(b, 'wp_dwidth') for b in mb])
    old = np.array([field(b, 'wp_path')[0] for b in mb])
    has_dry2 = all(has(b, 'wp_dry2') for b in mb) and has(header, 'timesDry2')
    old2 = np.array([field(b, 'wp_dry2')[0] for b in mb]) if has_dry2 else None
    if not np.allclose(np.linalg.norm(perp, axis=1), 1, atol=.01):
        raise ValueError('wp_perp is not unit length')
    # corridor (offsets from wp_pos along wp_perp)
    edges = np.array([safe_corridor(S['road'], S['limits'], p, pp, w, d, margin) for p, pp, w, d in zip(P, perp, width, old)])
    wall = np.array([S['walls'].free(p, pp) for p, pp in zip(P, perp)])
    lo = np.maximum(edges[:, 0] + margin, -(wall[:, 0] - cv.WALL_CLEAR))
    hi = np.minimum(edges[:, 1] - margin, wall[:, 1] - cv.WALL_CLEAR)
    if max_shift is not None:
        c = np.clip(old, lo, hi); lo, hi = np.maximum(lo, c - max_shift), np.minimum(hi, c + max_shift)
    if np.any(lo > hi):
        raise ValueError('Infeasible AI corridor at %d waypoints (asphalt/GCL/wall)' % int((lo > hi).sum()))
    old_out = np.maximum(lo - old, old - hi)
    log('  corridor: original line outside the %.1f m corridor at %d wps (max %.2f m); wall-limited wps %d' %
        (margin, int((old_out > 1e-4).sum()), max(old_out.max(), 0), int(((wall - cv.WALL_CLEAR) < np.c_[-edges[:, 0], edges[:, 1]] - margin).any(1).sum())))
    wr = ramp_weights(n, forks)
    L_old = P + old[:, None] * perp
    curv_old = curvature_stats(L_old)
    k_old = np.abs(cv.line_curvature(L_old)[0])
    kcap = np.array([k_old[np.arange(i - GUARD_WIN, i + GUARD_WIN + 1) % n].max() for i in range(n)]) * (1 + CURV_TOL) + 1e-4
    kink_cap = curv_old['kink_max'] * (1 + CURV_TOL) + 1e-4
    path = path2 = None
    picked = None
    lo2, hi2 = (lo.copy(), hi.copy()) if has_dry2 else (None, None)
    for it in range(6):
        # candidates: corridor-fitted original, convert 'fastest' (Bahrain logic), lap-time polish of the fitted original;
        # the fastest one that keeps max curvature/kinks within the criterion-4 tolerance wins
        base = cv.corridor_fit(lo, hi, old)
        cands = {'original_fit': base}
        try:
            cands['fastest'] = cv.racing_line(P, perp, lo, hi, old.copy(), mode='fastest', physics=True)
        except ValueError as e:
            log('  fastest line skipped: %s' % e)
        cands['polish'] = cv.lap_time_polish(P, perp, lo, hi, base, schedule=POLISH)
        cands['pinned_polish'] = pinned_polish(P, perp, lo, hi, base, wr, kcap, kink_cap)
        scored = []
        for name, d in cands.items():
            d, guarded = curvature_guard(P, perp, base, d, wr, kcap, kink_cap, lo, hi)
            Ld = P + d[:, None] * perp
            if guarded: log('    %s: %d wps held on the original line (curvature guard)' % (name, guarded))
            c = curvature_stats(Ld)
            ok = all(c[k] <= curv_old[k] * (1 + CURV_TOL) + 1e-4 for k in ('k_max', 'kink_max'))
            scored.append((not ok, cv.est_laptime(Ld), name, d))
            log('    %s: est %.3f k_max %.5f (orig %.5f) kink_max %.5f (orig %.5f)' % (name, scored[-1][1], c['k_max'], curv_old['k_max'], c['kink_max'], curv_old['kink_max']))
        scored.sort(key=lambda x: (x[0], x[1]))
        log('  candidates: ' + ', '.join('%s %.3f s%s' % (nm, t, '' if not bad else ' (curvature)') for bad, t, nm, _ in scored))
        _, _, picked, path = scored[0]
        if has_dry2:
            d2 = cv.secondary_line(P + path[:, None] * perp, perp, path, lo2, hi2, physics=True)
            b2 = np.clip(old2, lo2, hi2)
            path2 = np.round(np.clip(b2 + wr * (d2 - b2), lo2, hi2), 4)
        # serialized line between waypoints: tighten the corridor where a chord gets closer than 1 m to the asphalt edge
        bad = segment_shortfall(P + path[:, None] * perp, S, lo, hi, path, perp)
        bad2 = segment_shortfall(P + path2[:, None] * perp, S, lo2, hi2, path2, perp) if has_dry2 else 0
        if not bad and not bad2:
            break
        log('  dense clearance: tightened %d (line) + %d (dry2) waypoint bounds, round %d' % (bad, bad2, it + 1))
        if np.any(lo > hi) or (has_dry2 and np.any(lo2 > hi2)):
            raise ValueError('Infeasible AI corridor after dense-clearance tightening')
    if np.any(path < lo - 1e-4) or np.any(path > hi + 1e-4):
        raise ValueError('Serialized racing line violates the corridor')
    L = P + path[:, None] * perp
    # dwidth (F1 rule), never below original / width, untouched in the pit ramp
    obst = np.array([S['obstacles'].free(p, pp) for p, pp in zip(P, perp)])
    reach = np.array([reach_sides(S, p, pp, w) for p, pp, w in zip(P, perp, width)])
    lim = np.maximum(reach, width + cv.AI_DW_FLOOR)
    lim = np.minimum(lim, obst - cv.WALL_CLEAR)
    lim = np.minimum(lim, width + cv.AI_DW_MAX)
    sm = lambda a: cv._smooth_closed(a, 5)
    lim = np.minimum(np.c_[sm(lim[:, 0]), sm(lim[:, 1])], lim)
    newdw = np.maximum(dwidth, np.maximum(lim, width))
    newdw[wr < 1] = dwidth[wr < 1]
    newdw = np.round(newdw, 3)
    newdw = np.where(newdw < dwidth, dwidth, newdw)
    # write
    t_old, t_new = cv.est_laptime(L_old), cv.est_laptime(L)
    out = list(blocks)
    for i in range(n):
        b = out[order[i]]
        tk = tokens(b, 'wp_path')
        if abs(path[i] - old[i]) > 1e-9:
            b = set_field(b, 'wp_path', [fmt_like(tk[0], path[i])] + tk[1:])
            if abs(path[i] - old[i]) > GROOVE_MOVE:
                gt = tokens(b, 'wp_groove_lat')
                b = set_field(b, 'wp_groove_lat', [fmt_like(gt[0], -0.27 * path[i])])
        if has_dry2 and abs(path2[i] - old2[i]) > 1e-9:
            b = set_field(b, 'wp_dry2', [fmt_like(tokens(b, 'wp_dry2')[0], path2[i])])
        if np.any(newdw[i] > dwidth[i] + 1e-9):
            dt = tokens(b, 'wp_dwidth')
            b = set_field(b, 'wp_dwidth', [dt[s] if newdw[i, s] <= dwidth[i, s] + 1e-9 else fmt_like(dt[s], newdw[i, s]) for s in (0, 1)])
        out[order[i]] = b
    ratio = t_new / t_old
    def scale_header(h, key, r):
        m = re.search(r'(?m)^' + key + '=' + VAL, h)
        if not m:
            return h
        toks = m.group(1).split(',')
        return h[:m.start(1)] + ','.join(fmt_like(t, float(t) * r) for t in toks) + h[m.end(1):]
    header2 = scale_header(header, 'times', ratio)
    t2_old = t2_new = None
    if has_dry2:
        t2_old, t2_new = cv.est_laptime(P + old2[:, None] * perp), cv.est_laptime(P + path2[:, None] * perp)
        header2 = scale_header(header2, 'timesDry2', t2_new / t2_old)
    new_text = prefix + '[Waypoint]' + header2 + ''.join(out)
    # corner markers: report only
    cty, cst = cv.corner_marks(L)
    oty = np.array([int(field(b, 'wpd_CornerType')[0]) for b in mb]); ost = np.array([int(field(b, 'wpd_CornerState')[0]) for b in mb])
    shift = np.abs(path - old)
    rep = dict(main_waypoints=n, main_ids=[int(i) for i in order], total_waypoints=len(blocks),
               pit_junctions=[int(order[k]) for k in forks], margin=margin, wall_clear=cv.WALL_CLEAR,
               max_shift_limit=max_shift, lap_est_before=t_old, lap_est_after=t_new,
               times_scale=ratio, has_dry2=has_dry2, dry2_lap_est_before=t2_old, dry2_lap_est_after=t2_new,
               changed_path_wps=int((shift > 1e-4).sum()), max_shift=float(shift.max()), mean_shift=float(shift.mean()),
               pit_ramp_max_shift=float(shift[wr < 1].max()) if (wr < 1).any() else 0.0,
               pinned_wps_moved=int((shift[wr == 0] > 1e-4).sum()),
               original_outside_corridor_wps=int((old_out > 1e-4).sum()), original_outside_corridor_max=float(max(old_out.max(), 0)),
               dwidth_widened_sides=int((newdw > dwidth + 1e-9).sum()), dwidth_add_max=float((newdw - dwidth).max()),
               dwidth_minus_width_min=float((newdw - width).min()),
               wall_limited_dwidth_sides=int(((obst - cv.WALL_CLEAR) < np.maximum(reach, width + cv.AI_DW_FLOOR)).sum()),
               corner_marks_agreement=float(np.mean((cty == oty) & (cst == ost))),
               curvature_before=curvature_stats(L_old), curvature_after=curvature_stats(L))
    if has_dry2:
        rep['dry2_changed_wps'] = int((np.abs(path2 - old2) > 1e-4).sum())
    return new_text, rep, dict(L=L, L2=(P + path2[:, None] * perp) if has_dry2 else None)


def accept(old_text, new_text, rep, lines, S):
    """Acceptance criteria 1-5; returns list of failures (criterion 6 is checked on the written pak)."""
    fail = []
    errs, changed = text_diff(old_text, new_text, set(rep['main_ids']), rep['has_dry2'])
    rep['text_diff'] = dict(errors=errs[:20], changed_lines=changed)
    if errs: fail.append('1 text diff: ' + '; '.join(errs[:5]))
    for key, L in (('line', lines['L']), ('dry2', lines['L2'])):
        if L is None:
            continue
        try:
            rep['dense_' + key] = dense_clearance(L, S['road'], S['limits'], minimum=1.0)
        except ValueError as e:
            fail.append('2 dense clearance (%s): %s' % (key, e))
    if not rep['lap_est_after'] <= rep['lap_est_before'] + 1e-9:
        fail.append('3 est lap %.3f > original %.3f' % (rep['lap_est_after'], rep['lap_est_before']))
    cb, ca = rep['curvature_before'], rep['curvature_after']
    for k in ('k_max', 'kink_max'):
        if ca[k] > cb[k] * (1 + CURV_TOL) + 1e-4:
            fail.append('4 %s %.5f > original %.5f (+%d%%)' % (k, ca[k], cb[k], CURV_TOL * 100))
    rep['curvature_tolerance'] = '%s new <= original*(1+%.2f)+1e-4 1/m' % ('k_max, kink_max', CURV_TOL)
    po, pn = physics_checks(old_text, S), physics_checks(new_text, S)
    rep['check_out_before'], rep['check_out_after'] = po, pn
    worse = sorted(set(pn['failures']) - set(po['failures']))
    if worse: fail.append('5 check_out subset: new failures %s' % worse)
    if pn['collision_misses'] > po['collision_misses'] or pn['off_road_samples'] > po['off_road_samples'] or pn['gcl_coverage'] < po['gcl_coverage']:
        fail.append('5 check_out subset: collision/GCL worse than original')
    return fail


def sha256(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for c in iter(lambda: f.read(1 << 20), b''):
            h.update(c)
    return h.hexdigest()


def stage(game, track, out_dir, margin=1.2, max_shift=None):
    pak, b, E, tid, a_uid = open_track(game, track)
    text = b.read(E[a_uid]).decode('latin1')
    why = refusal(track, text, set(E), tid)
    dst = Path(out_dir) / track
    dst.mkdir(parents=True, exist_ok=True)
    rep = dict(track=track, source_pak=str(pak), source_sha256=sha256(pak), aiw_uid='%016x' % a_uid,
               decisions=__doc__.split('DECISIONS (documented in report.json too):')[1].strip().splitlines())
    def done(ok, fail):
        rep['accepted'], rep['failures'] = ok, fail
        rep.pop('main_ids', None)
        (dst / 'report.json').write_text(json.dumps(rep, indent=2, default=float), encoding='utf8')
        return rep
    if why:
        return done(False, ['refused: ' + why])
    S = surfaces(b, E, tid)
    try:
        new_text, r, lines = regen(text, S, margin, max_shift)
    except ValueError as e:
        return done(False, ['regen: %s' % e])
    rep.update(r)
    fail = accept(text, new_text, rep, lines, S)
    if fail:
        return done(False, fail)
    blob = patch_official(b, {a_uid: new_text.encode('latin1')})
    # criterion 6 on the bytes about to be written
    tmp = dst / ('.' + track + '_Physics.bff.tmp')
    tmp.write_bytes(blob)
    c = Bff(str(tmp))
    same = c.count == b.count and all(x.uid == y.uid and (x.uid == a_uid or (c.read_raw(x) == b.read_raw(y) and x.size == y.size
                                      and x.comp == y.comp and x.timestamp == y.timestamp)) for x, y in zip(c.entries, b.entries))
    back = c.read(next(x for x in c.entries if x.uid == a_uid)).decode('latin1') == new_text
    rep['pak_check'] = dict(entries=c.count, uids_and_other_bytes_identical=same, aiw_reads_back=back, name=c.name)
    if not (same and back):
        tmp.unlink()
        return done(False, ['6 repacked pak differs outside the AIW entry or does not read back'])
    (dst / 'Pakfiles' / 'Tracks').mkdir(parents=True, exist_ok=True)
    out_pak = dst / 'Pakfiles' / 'Tracks' / pak.name
    os.replace(tmp, out_pak)
    (dst / (track + '.aiw')).write_bytes(new_text.encode('latin1'))
    (dst / (track + '.original.aiw')).write_bytes(text.encode('latin1'))
    (dst / 'backup').mkdir(exist_ok=True)
    shutil.copy2(pak, dst / 'backup' / pak.name)
    if sha256(dst / 'backup' / pak.name) != rep['source_sha256']:
        raise RuntimeError('backup copy sha256 mismatch')
    (dst / 'backup' / (pak.name + '.sha256')).write_text('%s  %s\n' % (rep['source_sha256'], pak.name), encoding='ascii')
    rep['staged_pak'] = str(out_pak); rep['staged_sha256'] = sha256(out_pak)
    return done(True, [])


def plan(game):
    rows = []
    for pak in sorted((Path(game) / 'Pakfiles' / 'TRACKS').glob('*_Physics.bff')):
        track = pak.name[:-len('_Physics.bff')]
        try:
            _, b, E, tid, a = open_track(game, track)
            why = refusal(track, b.read(E[a]).decode('latin1'), set(E), tid)
        except (ValueError, KeyError) as e:
            why = str(e)
        rows.append((track, why))
        print('%-28s %s' % (track, 'ELIGIBLE' if why is None else 'refused: ' + why))
    print('eligible %d / %d' % (sum(w is None for _, w in rows), len(rows)))
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('cmd', choices=('plan', 'stage')); ap.add_argument('track', nargs='?'); ap.add_argument('out', nargs='?')
    ap.add_argument('--game', default=GAME); ap.add_argument('--margin', type=float, default=1.2)
    ap.add_argument('--max-shift', type=float, default=None)
    a = ap.parse_args()
    game = Path(a.game).resolve()
    if a.cmd == 'plan':
        plan(game); return
    if not a.track or not a.out:
        ap.error('stage needs <Track> <out_dir>')
    out = Path(a.out).resolve()
    if out == game or game in out.parents:
        raise SystemExit('Output must be outside the game folder')
    rep = stage(game, a.track, out, a.margin, a.max_shift)
    keys = ('accepted', 'failures', 'lap_est_before', 'lap_est_after', 'max_shift', 'mean_shift', 'changed_path_wps',
            'dwidth_widened_sides', 'curvature_before', 'curvature_after', 'pak_check')
    print(json.dumps({k: rep.get(k) for k in keys}, indent=2, default=float))
    raise SystemExit(0 if rep['accepted'] else 1)


if __name__ == '__main__':
    main()

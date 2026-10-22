"""Sketch converter: Assetto Corsa track (kn5 + ai/fast_lane.ai) -> AMS2 (Madness) mod folder for AMS2CM.
usage: python -I convert.py <ac_track_dir> <track_id> <out_dir> <sochi_ref_dir> <sochi_extract_dir> [--max-meshes N]
  ac_track_dir      ...\\bahrain   (bahrain.kn5, ai\\fast_lane.ai, data\\, ui\\)
  sochi_ref_dir     ...\\sochi\\Sochi Autodrom\\Automobilista 2   (templates: trd, mtx, seasonal paks)
  sochi_extract_dir ...\\x_sochi   (extracted Sochi paks: enx, cameras, trackproperties.bin, physics templates)
Writes <out_dir>\\<track_id>\\Automobilista 2\\{Pakfiles\\Tracks, Tracks\\<id>, GUI\\...}.  Nothing is installed.
See SPEC.md for what is verified and what is a hypothesis (coordinate mirror, AIW fields, triggers, CSM).
"""
import argparse, collections, json, math, os, re, struct, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kn5 import read_kn5
from meb import write_meb
from bmt import encode as bmt_encode
from bff import Bff
from bff_write import write_bff
import xml.etree.ElementTree as ET

BS = chr(92)
# AC -> Madness: mirror Z (evidence: both real tracks run clockwise but the XZ signed area of the AI loop has
# opposite sign in AC Bahrain and AMS2 Sochi).  Winding is then reversed to keep cross(b-a,c-a).n > 0 (Sochi).
MIRROR = np.array([1.0, 1.0, -1.0])
LINE_MARGIN = 1.0         # min distance of the AI racing line to the asphalt edge (AMS2 originals: 5th percentile 1.4 m)
OPT_REG = 0.02
WALL_CLEAR = 1.8        # min distance of the AI racing line to a wall (Azure original p1 1.4 m, p5 1.6 m)            # weight pulling the optimised line toward the AC line (keeps the problem well posed)
KERB_USE = 0.3          # the AI line may sit this much closer to the asphalt edge where a kerb follows it
EDGE_CAP = 14.0          # AI side distances include run-off; cap per side



def san(s):
    s = re.sub(r'[^A-Za-z0-9_]', '_', s).strip('_') or 'x'
    return s[:60]


def f6(x):
    return '%.6f' % x


# ---------------------------------------------------------------- materials
RZ_BASIC = '''<?xml version='1.0' encoding='utf-8'?>
<material VERSION="v1.0.0.1" name="{name}" shader="Render\\Shaders\\rz_basic.fx" technique="rz_basic" supportsSpecialisedLighting="false" fog="true" antialias="1" numparams="9" cull="{cull}">
  <shaderparam name="detailTilingX" type="EPT_F32">
    <value v="8.0" />
  </shaderparam>
  <shaderparam name="detailTilingY" type="EPT_F32">
    <value v="8.0" />
  </shaderparam>
  <shaderparam name="diffuseMultiplier" type="EPT_F32">
    <value v="1.0" />
  </shaderparam>
  <shaderparam name="emissiveScale" type="EPT_F32">
    <value v="1.0" />
  </shaderparam>
  <shaderparam name="globalSpecularFactor" type="EPT_F32">
    <value v="{spec}" />
  </shaderparam>
  <shaderparam name="maxSpecPower" type="EPT_F32">
    <value v="125.0" />
  </shaderparam>
  <shaderparam name="minSpecPower" type="EPT_F32">
    <value v="8.0" />
  </shaderparam>
  <shaderparam name="reflectionBlurBias" type="EPT_F32">
    <value v="0.0" />
  </shaderparam>
  <shaderparam name="diffuse1Texture" type="EPT_TEXTURE">
    <type t="ET_STANDARD" />
    <value v="{tex}" />
  </shaderparam>
  <depthparams>
    <enabled e="true" />
    <writeenabled w="{zwrite}" />
  </depthparams>
  <alphablendparams>
{blend}  </alphablendparams>
{defines}</material>
'''
BLEND_ON = ('    <enabled e="true" />\n    <sourceblend sb="EBF_SOURCE_ALPHA" />\n'
            '    <destblend db="EBF_INV_SOURCE_ALPHA" />\n    <blendop bo="EBO_ADD" />\n')
BLEND_OFF = '    <enabled e="false" />\n'


def make_mtx(m, tid, texname):
    alpha_blend = m['blend'] == 1
    alpha_test = bool(m['alphaTested']) or m['shader'] in ('ksPerPixelAT', 'ksTree', 'ksGrass')
    tex = 'tracks' + BS + 'textures' + BS + tid + BS + texname.upper()
    spec = 0.2
    if 'ksSpecular' in m['props']:
        spec = max(0.0, min(1.0, m['props']['ksSpecular'][0]))
    if m['shader'].startswith('ksMultilayer') and spec > 0.5:
        spec = 0.05    # AC multilayer ground relies on its own gloss shader; spec 1.0 here mirrors the sky (blue/white streaks)
    return RZ_BASIC.format(name=m['mname'], cull='EBFCT_NONE' if (alpha_test or alpha_blend) else 'EBFCT_ANTICLOCKWISE',
                           spec=repr(float(np.float32(spec))), tex=tex, zwrite='false' if alpha_blend else 'true',
                           blend=BLEND_ON if alpha_blend else BLEND_OFF,
                           defines='  <define name="USE_ALPHATEST" />\n' if alpha_test else '')


# AC overlay layers that render as a wide dark band on the road in AMS2 (rubber groove / skid decals, black alpha textures);
# the game has its own rubbering.  ksMultilayer road (stretched 1024x4096 lane texture) gets the plain tarmac tile instead.
DROP_MATS = {'groove', 'skidmark_01', 'skidmark_02', 'bah_skid_decals_a'}
TARMAC_TILE = 'tarmac_diff_a_07_d.dds'
UV_MULTILAYER = (1.5, 3.5)


AVAIL = None    # textures present in the kn5 (set in main); ksMultilayer falls back when the tile is missing (Sepang)


DETAIL_TILE_M = {'road': 4.0, 'grass': 6.0}    # metres covered by one repeat of the detail texture


def detail_kind(mt):
    # ksMultilayer_fresnel ground: the diffuse is a macro colour map (satellite photo, colour-layer mask) stretched over
    # hundreds of metres; the real surface lives in txDetailR/G, so tile that instead (Magny-Cours)
    d = mt['tex'].get('txDetailR')
    n = mt['name'].upper()
    if not (mt['shader'].startswith('ksMultilayer_fresnel') and d and (AVAIL is None or d in AVAIL)):
        return None
    if (n.startswith(('ROAD', 'PITR', 'DROAD')) or (n.startswith('TR_TARMAC') and 'GREEN' not in n) or n == 'ASPHALT_ROAD') and not any(x in mt['tex'].get('txDiffuse', '') for x in ('cheq', 'stgrid')):
        return 'road'
    if n in ('EXTERIOR', 'GRASS_MAIN'):
        return 'grass'
    return None


def metric_uv_scale(P, uv, idx, tile):
    # Area-weighted metres per UV unit: tiny densely tessellated patches must not
    # dictate the texture scale over an otherwise large road mesh (Shanghai).
    t = idx.reshape(-1, 3)
    e1, e2 = P[t[:, 1]] - P[t[:, 0]], P[t[:, 2]] - P[t[:, 0]]
    d1, d2 = uv[t[:, 1]] - uv[t[:, 0]], uv[t[:, 2]] - uv[t[:, 0]]
    det = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]
    area = np.linalg.norm(np.cross(e1, e2), axis=1)
    ok = (np.abs(det) > 1e-12) & (area > 1e-12)
    if not ok.any():
        return np.array([1.0, 1.0])
    e1, e2, d1, d2, det = e1[ok], e2[ok], d1[ok], d2[ok], det[ok]
    Pu = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / det[:, None]
    Pv = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / det[:, None]
    def median(values):
        order = np.argsort(values)
        weights = area[ok][order]
        return values[order[np.searchsorted(np.cumsum(weights), weights.sum() * .5)]]
    mu, mv = median(np.linalg.norm(Pu, axis=1)), median(np.linalg.norm(Pv, axis=1))
    return np.array([max(mu / tile, 0.05), max(mv / tile, 0.05)])


def tex_of(mt):
    if detail_kind(mt):
        return mt['tex']['txDetailR']
    if mt['shader'] == 'ksMultilayer' and (AVAIL is None or TARMAC_TILE in AVAIL):
        return TARMAC_TILE
    ok = [t for t in mt['tex'].values() if AVAIL is None or t in AVAIL]
    return next((t for n, t in mt['tex'].items() if n in ('txDiffuse', 'txBase') and t in ok), None) or next(iter(ok), None)


# ---------------------------------------------------------------- AI line
def read_ai(path):
    with open(path, 'rb') as stream:
        d = stream.read()
    if len(d) < 20:
        raise ValueError('Truncated AI file: ' + str(path))
    ver, n, _, _ = struct.unpack_from('<4i', d, 0)
    if ver != 7 or n < 3 or len(d) < 20 + 20 * n:
        raise ValueError('Unsupported/truncated AI file (need v7, >=3 points): ' + str(path))
    pts = np.array([struct.unpack_from('<4f', d, 16 + 20 * i) for i in range(n)])
    o = 16 + 20 * n
    ec = struct.unpack_from('<i', d, o)[0]
    extra = np.frombuffer(d, '<f4', ec * 18, o + 4).reshape(ec, 18) if ec == n and len(d) >= o + 4 + ec * 72 else None
    if not np.isfinite(pts).all() or (extra is not None and not np.isfinite(extra).all()):
        raise ValueError('Non-finite AI coordinates/extras: ' + str(path))
    return pts[:, :3], extra


def validate_ai_inputs(fast, extra, dummies, pitlane=None, require_complete=False):
    """Fail before writing a package when source AI cannot support its advertised features."""
    missing = ['AC_TIME_%d_%s' % (i, side) for i in range(3) for side in ('L', 'R')
               if 'AC_TIME_%d_%s' % (i, side) not in dummies]
    if missing:
        raise ValueError('Missing timing dummies: ' + ', '.join(missing))
    for prefix in ('AC_START_', 'AC_PIT_'):
        slots = sorted(int(n[len(prefix):]) for n in dummies if re.fullmatch(prefix + r'\d+', n))
        if not slots or slots != list(range(len(slots))):
            raise ValueError('Missing/non-contiguous %s dummies (need indices from 0)' % prefix)
    if len(fast) < 12 or np.asarray(fast).shape != (len(fast), 3) or not np.isfinite(fast).all():
        raise ValueError('Fast lane needs >=12 finite 3D points')
    if np.any(np.linalg.norm(np.diff(fast, axis=0), axis=1) < 1e-5):
        raise ValueError('Fast lane contains duplicate adjacent points')
    for name, matrix in dummies.items():
        if name.startswith(('AC_TIME_', 'AC_START_', 'AC_PIT_')) and (np.asarray(matrix).shape != (4, 4) or not np.isfinite(matrix).all()):
            raise ValueError('Invalid dummy transform: ' + name)
    if extra is not None and (extra.shape != (len(fast), 18) or not np.isfinite(extra).all() or np.any(extra[:, 5:7] <= 0)):
        raise ValueError('Fast lane extras need finite positive left/right widths for every point')
    if pitlane is not None and (len(pitlane) < 3 or not np.isfinite(pitlane).all()):
        raise ValueError('Pit lane needs >=3 finite points')
    if require_complete and (extra is None or pitlane is None):
        raise ValueError('Complete AI requires fast_lane.ai extras and a layout-specific pit_lane.ai')


def yaw_of(fwd):
    """Madness Ori yaw: car faces (-sin y, 0, -cos y)  (derived from Sochi grid vs AI direction)."""
    return math.atan2(-fwd[0], -fwd[2])


def order_timing_sectors(fast, dummies):
    """Keep source timing gates, assigning sector numbers in actual driving order."""
    centres = [(dummies['AC_TIME_%d_L' % n][3,:3] + dummies['AC_TIME_%d_R' % n][3,:3]) * .5 for n in range(3)]
    indices = [int(np.argmin(np.linalg.norm(fast-point,axis=1))) for point in centres]
    order = [(index-indices[0]) % len(fast) for index in indices[1:]]
    if 0 in order or order[0] == order[1]:
        raise ValueError('Timing gates do not define three distinct sectors')
    if order[0] < order[1]: return dummies
    result = dict(dummies)
    for side in ('L','R'):
        result['AC_TIME_1_'+side],result['AC_TIME_2_'+side] = dummies['AC_TIME_2_'+side],dummies['AC_TIME_1_'+side]
    print('  source timing: relabelled TIME_2 then TIME_1 into sector driving order')
    return result


def line_curvature(L):
    """Signed curvature (1/m, <0 = left in AMS2 coords) of a closed polyline plus per-point spacing."""
    d = np.roll(L, -1, 0) - L; d[:, 1] = 0; ds = np.linalg.norm(d, axis=1) + 1e-9
    h = np.unwrap(np.arctan2(d[:, 0], d[:, 2]))
    dh = np.angle(np.exp(1j * (np.roll(h, -1) - h)))
    k = dh / ds
    n = 3
    k = np.convolve(np.r_[k[-n:], k, k[:n]], np.ones(n) / n, 'same')[n:-n]
    return k, ds


def speed_profile(L, alat=12.0, vmax=80.0, acc=5.0, brk=14.0):
    k, ds = line_curvature(L)
    v = np.minimum(vmax, np.sqrt(alat / np.maximum(np.abs(k), 1e-4)))
    n = len(L)
    for _ in range(2):
        for i in list(range(n)) * 2:
            j = (i + 1) % n; v[j] = min(v[j], math.sqrt(v[i] ** 2 + 2 * acc * ds[i]))
        for i in list(range(n - 1, -1, -1)) * 2:
            j = (i + 1) % n; v[i] = min(v[i], math.sqrt(v[j] ** 2 + 2 * brk * ds[i]))
    return v, ds


def est_laptime(L, alat=12.0, vmax=80.0, acc=5.0, brk=14.0):
    """Point-mass speed profile lap time; calibrated (median ratio ~1) against `times` of the original AMS2 AIWs."""
    v, ds = speed_profile(L, alat, vmax, acc, brk)
    return float((ds / v).sum())


def corner_marks(L, thr_r=150.0):
    """AMS2 corner markers (wpd_CornerType / wpd_CornerState) from the racing line.
    Originals: type 3 = left (dh<0), 4 = right; state 1 = one wp at the start, state 2 (~76 m median, apex near its
    end), state 3 = exit (~52 m).  Returns (type[], state[])."""
    n = len(L)
    k, ds = line_curvature(L)
    step = float(ds.mean())
    ak = np.abs(k)
    ty = np.zeros(n, int); st = np.zeros(n, int)
    hot = ak > 1.0 / 260.0
    # merge gaps shorter than ~30 m of low curvature
    gap = max(1, int(30 / step))
    idx = np.where(hot)[0]
    if len(idx) == 0:
        return ty, st
    groups = []; cur = [idx[0]]
    for q in idx[1:]:
        if q - cur[-1] <= gap: cur.append(q)
        else: groups.append(cur); cur = [q]
    groups.append(cur)
    if len(groups) > 1 and groups[0][0] + n - groups[-1][-1] <= gap:   # wrap-around
        groups[0] = groups.pop() + groups[0]
    taken = np.zeros(n, bool)
    for g in groups:
        g = np.array(g); seg = g % n if g[0] <= g[-1] else g
        if len(g) < 2: continue
        apex = g[int(np.argmax(ak[g % n]))] % n
        if 1.0 / max(ak[apex], 1e-4) > thr_r: continue
        sgn = np.sign(k[g % n].sum()) or 1.0
        n2 = int(np.clip((len(g) * step) * 0.8, 20, 130) / step)
        n3 = int(np.clip((len(g) * step) * 0.6, 25, 110) / step)
        a2 = [(apex - m) % n for m in range(n2, 0, -1)] + [apex]
        a3 = [(apex + m) % n for m in range(1, n3 + 1)]
        a1 = (apex - n2 - 1) % n
        if any(taken[q] for q in a2 + a3): continue
        t = 3 if sgn < 0 else 4
        for q in a2: st[q] = 2; ty[q] = t; taken[q] = True
        for q in a3: st[q] = 3; ty[q] = t; taken[q] = True
        if not taken[a1]:
            st[a1] = 1; ty[a1] = t; taken[a1] = True
    return ty, st


# --street: AI header values of the AMS2 street circuit Azure (Monaco-like): anticipation 40/60/80, groove 6.0/3.5, setup 0.48/0.52/0.50
STREET = '--street' in sys.argv


def min_curvature_offsets(W, pw, lo, hi, d0, reg=OPT_REG, rounds=30, w=None, fallback_maxiter=20000):
    """Lateral offsets d (within [lo, hi]) of the closed polyline W + d*pw that minimise the sum of squared second differences
    (minimum-curvature line), with a sparse active-set solve and bounded L-BFGS-B fallback.
    Both methods must pass the same projected-gradient KKT and feasibility checks."""
    import scipy.sparse as sp, scipy.sparse.linalg as sla
    import warnings
    if rounds < 1 or fallback_maxiter < 1:
        raise ValueError('Racing-line solver requires at least one round')
    if not all(np.isfinite(a).all() for a in (W, pw, lo, hi, d0)) or np.any(lo > hi):
        raise ValueError('Invalid/non-finite racing-line solver inputs or infeasible corridor')
    n = len(W)
    I = sp.identity(n, format='csr'); D2 = sp.diags([1., -2., 1.], [-1, 0, 1], shape=(n, n), format='lil'); D2[0, n - 1] = 1; D2[n - 1, 0] = 1
    D2 = D2.tocsr()
    if w is not None:
        D2 = sp.diags(np.sqrt(w)) @ D2                      # weight the curvature penalty per point
    Ax = D2 @ sp.diags(pw[:, 0]); Az = D2 @ sp.diags(pw[:, 2])
    H = (Ax.T @ Ax + Az.T @ Az + reg ** 2 * I).tocsc(); g = Ax.T @ (-D2 @ W[:, 0]) + Az.T @ (-D2 @ W[:, 2]) + reg ** 2 * d0
    def residual(offsets):
        gradient = H @ offsets - g
        # Projected-gradient residual is zero exactly at the box-constrained quadratic's KKT solution.
        return float(np.max(np.abs(offsets - np.clip(offsets - gradient, lo, hi))))

    kkt_tolerance = 1e-5
    fixed = np.zeros(n, bool); d = d0.copy()
    converged = False
    for iteration in range(rounds):
        free = ~fixed
        if free.any():
            with warnings.catch_warnings():
                warnings.simplefilter('error', sla.MatrixRankWarning)
                d[free] = sla.spsolve(H[free][:, free].tocsc(), g[free] - H[free][:, fixed] @ d[fixed])
        if not np.isfinite(d).all():
            raise ValueError('Racing-line solver returned non-finite offsets')
        over = (d < lo - 1e-6) | (d > hi + 1e-6)
        d = np.clip(d, lo, hi)
        if over.any():
            fixed |= over; continue
        grad = H @ d - g
        rel = fixed & (((d >= hi - 1e-9) & (grad > 0)) | ((d <= lo + 1e-9) & (grad < 0)))
        if not rel.any():
            converged = True
            break
        fixed &= ~rel
    method, fallback_iterations, solver_message = 'active-set', 0, ''
    kkt = residual(d)
    if not converged or kkt > kkt_tolerance:
        from scipy.optimize import minimize

        def objective(offsets):
            hd = H @ offsets
            return float(0.5 * offsets @ hd - g @ offsets), hd - g

        result = minimize(objective, np.clip(d, lo, hi), method='L-BFGS-B', jac=True,
                          bounds=list(zip(lo, hi)),
                          options=dict(maxiter=fallback_maxiter, maxfun=3 * fallback_maxiter,
                                       maxcor=30, maxls=50, ftol=1e-15, gtol=1e-8))
        d = np.asarray(result.x)
        method, fallback_iterations, solver_message = 'L-BFGS-B', int(result.nit), str(result.message)
        if not np.isfinite(d).all():
            raise ValueError('Racing-line solver returned non-finite offsets')
        kkt = residual(d)
        converged = bool(result.success) and kkt <= kkt_tolerance
    bound_violation = max(float(np.max(lo - d)), float(np.max(d - hi)), 0.0)
    print('  AI solver: method=%s iterations=%d fallback_iterations=%d converged=%s finite=%s bound_violation=%.6g kkt_residual=%.6g' %
          (method, iteration + 1, fallback_iterations, converged, np.isfinite(d).all(), bound_violation, kkt))
    if not converged or kkt > kkt_tolerance or bound_violation > 1e-7:
        raise ValueError('Racing-line solver did not converge: KKT %.6g (limit %.6g); %s' %
                         (kkt, kkt_tolerance, solver_message))
    return d


def build_aiw(fast, extra, dummies, tid, pit=None, wide=None, wallf=None):
    """pit: pit-lane centreline (AC coords, resampled) from build_pit(), or None for no pit branch."""
    if pit is not None:
        pit = pit * MIRROR
    P = fast * MIRROR
    left = extra[:, 5] if extra is not None else np.full(len(P), 6.0)
    right = extra[:, 6] if extra is not None else np.full(len(P), 6.0)
    sf = (dummies['AC_TIME_0_L'][3, :3] + dummies['AC_TIME_0_R'][3, :3]) * 0.5 * MIRROR
    k0 = int(np.argmin(np.linalg.norm(P - sf, axis=1)))
    P = np.roll(P, -k0, axis=0); left = np.roll(left, -k0); right = np.roll(right, -k0)
    seg = np.linalg.norm(np.roll(P, -1, axis=0) - P, axis=1)
    cum = np.r_[0.0, np.cumsum(seg)]
    lap = cum[-1]
    span = 5.0
    nw = int(lap // span)
    s = np.linspace(0, lap, nw, endpoint=False)
    Pc = np.vstack([P, P[:1]])
    W = np.c_[[np.interp(s, cum, Pc[:, i]) for i in range(3)]].T
    half = np.minimum(np.interp(s, cum, np.r_[left, left[:1]]), np.interp(s, cum, np.r_[right, right[:1]]))
    half = np.clip(half, 3.0, 30.0)
    # Sochi layout [V]: wp_pos = track centre, wp_width = distances to the edges, wp_path = lateral offset of the racing
    # line (line = pos + path*perp; negative path is bounded by the first width, 0 violations in 819 wps).  So the AC
    # fast_lane (racing line) goes into path and wp_pos moves to the middle of the usable strip.
    def smooth(a, n=5):
        k = np.ones(n) / n; return np.convolve(np.r_[a[-n:], a, a[:n]], k, 'same')[n:-n]
    # Smoothing must never widen a local bottleneck; it can move the racing line outside the source/GCL corridor.
    raw_l = np.clip(np.interp(s, cum, np.r_[left, left[:1]]), 0.5, EDGE_CAP)
    raw_r = np.clip(np.interp(s, cum, np.r_[right, right[:1]]), 0.5, EDGE_CAP)
    ex_l = np.minimum(smooth(raw_l), raw_l)    # AC +lat side = AMS -perp side
    ex_r = np.minimum(smooth(raw_r), raw_r)    # AC -lat side = AMS +perp side

    # keep the racing line off the white line / kerb: AMS2 cars run over kerbs the AI treats as road (Bahrain)
    wide_l = wide_r = None
    if wide is not None:
        wl_ = np.roll(wide[:, 5], -k0); wr_ = np.roll(wide[:, 6], -k0)
        wide_l = smooth(np.clip(np.interp(s, cum, np.r_[wl_, wl_[:1]]), 1.5, EDGE_CAP))
        wide_r = smooth(np.clip(np.interp(s, cum, np.r_[wr_, wr_[:1]]), 1.5, EDGE_CAP))
    wall_l = wall_r = None
    if wallf is not None:
        wl_ = np.roll(wallf[:, 0], -k0); wr_ = np.roll(wallf[:, 1], -k0)
        wall_l = np.interp(s, cum, np.r_[wl_, wl_[:1]]); wall_r = np.interp(s, cum, np.r_[wr_, wr_[:1]])
    # low (smoothed, <= 6.5 cm) kerbs beside the asphalt: the line may use their inner part like the AMS2 AI does at apexes
    mg_l = np.full(nw, LINE_MARGIN); mg_r = np.full(nw, LINE_MARGIN)
    if wide_l is not None:
        mg_l -= KERB_USE * ((wide_l - ex_l) > 0.6); mg_r -= KERB_USE * ((wide_r - ex_r) > 0.6)
    sh = smooth(np.maximum(0.0, mg_l - ex_l) - np.maximum(0.0, mg_r - ex_r), 9)
    # racing line: the AC fast_lane is often timid (Bahrain/Sepang stay >3 m off the edge, AMS2 originals p5 1.1 m).  Relax the line
    # toward the minimum-curvature path inside the corridor [-(ex_l - mg_l), ex_r - mg_r] (apexes reach the margin), keep it only if the
    # estimated lap time improves.
    tt = np.roll(W, -1, 0) - np.roll(W, 1, 0); tt[:, 1] = 0; tt /= np.linalg.norm(tt, axis=1)[:, None] + 1e-9
    pw = np.c_[tt[:, 2], np.zeros(nw), -tt[:, 0]]
    lo = -(ex_l - mg_l); hi = ex_r - mg_r
    if wall_l is not None:                       # walls: keep the line WALL_CLEAR off them (AMS2 street tracks p1 1.4 m)
        lo = np.maximum(lo, -(wall_l - WALL_CLEAR)); hi = np.minimum(hi, wall_r - WALL_CLEAR)
    if np.any(lo > hi):
        raise ValueError('Infeasible AI clearance corridor at %d waypoints' % np.count_nonzero(lo > hi))
    d = np.clip(smooth(min_curvature_offsets(W, pw, lo, hi, np.clip(sh, lo, hi)), 5), lo, hi)
    # later apex / fuller exit: penalise curvature where the car brakes and turns in (weight on the braking zones), keep the best lap
    v0, _ = speed_profile(W + d[:, None] * pw)
    bf = smooth((np.roll(v0, -1) < v0 - 1e-3).astype(float), 9)
    best = est_laptime(W + d[:, None] * pw)
    for beta in (2.0, 4.0, 8.0):
        d2 = np.clip(smooth(min_curvature_offsets(W, pw, lo, hi, d, w=1.0 + beta * bf), 5), lo, hi)
        t2 = est_laptime(W + d2[:, None] * pw)
        if t2 < best - 0.02: best, d = t2, d2
    t_ac, t_opt = est_laptime(W + sh[:, None] * pw), est_laptime(W + d[:, None] * pw)
    print('  racing line: est lap %.2f s (AC line) -> %.2f s (optimised), mean |shift| %.2f m, max %.2f m' % (t_ac, t_opt, np.abs(d - sh).mean(), np.abs(d - sh).max()))
    if t_opt < t_ac:
        sh = d
    if not np.isfinite([t_ac, t_opt]).all() or not np.isfinite(sh).all():
        raise ValueError('Non-finite racing line/lap estimate')
    margin_l, margin_r = ex_l + sh, ex_r - sh
    print('  AI margins: left min=%.3f right min=%.3f finite=True' % (margin_l.min(), margin_r.min()))
    if np.any(sh < lo - 1e-6) or np.any(sh > hi + 1e-6):
        raise ValueError('Selected racing line violates asphalt/wall safety margins')

    def lapdist(name):
        c = (dummies[name + '_L'][3, :3] + dummies[name + '_R'][3, :3]) * 0.5 * MIRROR
        return float(s[int(np.argmin(np.linalg.norm(W - c, axis=1)))])
    s1, s2 = lapdist('AC_TIME_1'), lapdist('AC_TIME_2')
    if not 0 < s1 < s2 < lap:
        raise ValueError('Timing sectors must follow 0 < S1 < S2 < lap')

    starts = sorted((int(n.split('_')[2]), M) for n, M in dummies.items() if re.fullmatch(r'AC_START_\d+', n))
    pits = sorted((int(n.split('_')[2]), M) for n, M in dummies.items() if re.fullmatch(r'AC_PIT_\d+', n))

    def pos_ori(M):
        p = M[3, :3] * MIRROR; f = M[2, :3] * MIRROR
        return p, yaw_of(f)
    _t = np.roll(W, -1, 0) - np.roll(W, 1, 0); _t[:, 1] = 0; _t /= np.linalg.norm(_t, axis=1)[:, None]
    line = W + sh[:, None] * np.c_[_t[:, 2], np.zeros(nw), -_t[:, 0]]       # racing line = pos + path*perp
    tdry = est_laptime(line)
    cty, cst = corner_marks(line)
    # [V from 224 original AIWs] wp_galpha = 0.45 in corners (R < ~200 m), 0 on straights; wp_groove_lat ~ -0.27 * wp_path
    _k, _ = line_curvature(line)
    galpha = 0.45 * (np.convolve(np.r_[(np.abs(_k) > 1 / 200.0)[-6:], np.abs(_k) > 1 / 200.0, (np.abs(_k) > 1 / 200.0)[:6]].astype(float), np.ones(13), 'valid') > 0)
    out = ['[Features]', 'waypointspan=%.2f' % (lap / nw), 'pitlanes=%d' % (pit is not None), 'startinggrid=%d' % len(starts),
           'pitspots=%d' % len(pits), 'garagespots=2', 'clippingpoints=0', 'driftversion=0', 'cornermarkerversion=1',
           'AI Late Braking Fraction=1.000000', 'Track Difficulty=1.000000', 'Oval=0', 'RallyCross=0', 'IceTrack=0',
           'IceTrackSolo=0', 'IsNarrowTrack=0', 'AnticipationDistMin=40.000000', 'AnticipationDistOffRoad=%.6f' % (60 if STREET else 80),
           'AnticipationDistWall=%.6f' % (80 if STREET else 160), 'Race Start Disabled=0.000000',
           'AI Setup Gearing=%.6f' % (0.48 if STREET else 0.5), 'AI Setup Downforce=%.6f' % (0.52 if STREET else 0.5),
           'AI Setup Balance=0.500000', '', '[GRID]']
    grid = []
    for i, M in starts:
        p, y = pos_ori(M)
        grid += ['GridIndex=%d' % i, 'Pos=(%.3f,%.3f,%.3f)' % tuple(p), 'Ori=(0.000,%.3f,0.000)' % y]
    out += grid + ['', '[ROLLING START]', 'RaceType="Race"', 'DistanceBehindGrid=460.000', 'DistanceBetweenRows=16.000',
                   'CarsInRow=2', 'StartSpeed=37.000', 'MaxSpeed=60.000', 'RaceType="TimeAttack"',
                   'DistanceBehindGrid=460.000', 'DistanceBetweenRows=15.000', 'CarsInRow=1', 'StartSpeed=30.000',
                   'MaxSpeed=37.000', '', '[TELEPORT]'] + grid + ['', '[CLIPPING POINTS]', '', '[PITS]']
    for i, M in pits:
        p, y = pos_ori(M)
        out += ['TeamIndex=%d' % i, 'PitLeftHanded=1', 'PitPos=(%.4f,%.4f,%.4f)' % tuple(p), 'PitOri=(0.0000,%.6f,0.0000)' % y]
        for g in (0, 1):
            out += ['GarLeftHanded=(%d,0)' % g, 'GarPos=(%d,%.6f,%.6f,%.6f)' % ((g,) + tuple(p)),
                    'GarOri=(%d,0.000000,%.6f,0.000000)' % (g, y)]
    out += ['', '', '', '[Waypoint]', 'trackstate=467', 'times=(%.4f,%.4f)' % (tdry, tdry * 1.045), 'number_waypoints=%d' % nw,
            'lap_length=%.6f' % lap, 'sector_1_length=%.6f' % s1, 'sector_2_length=%.6f' % s2, 'FuelUse=0.845000',
            'GrooveWidth=%.6f' % (6.0 if STREET else 7.5), 'GrooveWidthWet=%.6f' % (3.5 if STREET else 3.0), 'IntermediateFogLevel=(0.0000)',
            'IntermediateFogPlanes=(0.0,0.0)', 'RainyFogPlanes=(0.0,0.0)', 'IntermediateFogColor=(0.0,0.0,0.0)',
            'RainyFogColor=(0.0,0.0,0.0)', 'FogDensity=(0.00000,0.00000)', 'RainyDarkness=(0.00000,0.00000)',
            'GarageDepth=(0.0000)', 'PitStopSpaceFront=(0.5000)', 'PitStopSpaceBack=(4.0000)',
            'PitStopJoinIn=(4.0000)', 'PitStopJoinOut=(7.0000)', 'UseLineBlendSpeed=1']
    def sector(x):                                  # [V] Sochi: wp_score=(sector index, lap distance)
        return 0 if x < s1 else 1 if x < s2 else 2

    def perp_of(a, b):                              # [V] Sochi main + pit: perp = (dz, 0, -dx)
        d = b - a; d = np.array([d[0], 0.0, d[2]]); d /= (np.linalg.norm(d) or 1)
        return np.array([d[2], 0.0, -d[0]])

    # ---- pit branch [V structure from Sochi, H that the game accepts ours]
    wps = []              # dict(pos, perp, width, dwidth, path, galpha, score, branch, bits, ptrs)
    third = {}
    pitw = []
    if pit is not None:
        Q = pit
        nq = len(Q)
        fork = int(np.argmin(np.linalg.norm(W - Q[0], axis=1))); fork = (fork - 1) % nw
        join = int(np.argmin(np.linalg.norm(W - Q[-1], axis=1))); join = (join + 1) % nw
        third[fork] = nw                            # main fork wp -> first pit wp (Sochi 1003 -> 1101)
        cum = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(Q, axis=0), axis=1))] + np.linalg.norm(Q[0] - W[fork])
        sec = sector(s[fork])
        for j in range(nq):
            prev = fork if j == 0 else nw + j - 1
            nxt = join if j == nq - 1 else nw + j + 1
            pa = W[fork] if j == 0 else Q[j - 1]; pb = W[join] if j == nq - 1 else Q[j + 1]
            near = int(np.argmin(np.linalg.norm(W - Q[j], axis=1)))
            th = join if j == nq - 1 else (near if np.linalg.norm(W[near] - Q[j]) < 6.0 and j > 0 else -1)
            pitw.append(dict(pos=Q[j], perp=perp_of(pa, pb), width=(6.0, 6.0), dwidth=(10.0, 10.0), path=0.0,
                             galpha=0.0, score=(sec, s[fork] + cum[j]), branch=1, bits=0, ptrs=(prev, nxt, th, 1)))
        # stall spurs (Sochi: one wp at each PitPos, prev/third = pit wps 2 before/after, next = itself)
        for kk, (_, M) in enumerate(pits):
            p = M[3, :3] * MIRROR
            j = int(np.argmin(np.linalg.norm(Q - p, axis=1)))
            a = max(j - 2, 0); b = min(j + 2, nq - 1)
            lat = float(np.dot(p - Q[a], pitw[a]['perp']))
            idx = nw + nq + kk
            pitw.append(dict(pos=p, perp=np.zeros(3), width=(5.0, 5.0), dwidth=(5.0, 5.0), path=-lat, galpha=0.288,
                             score=(sec, pitw[j]['score'][1]), branch=106 + kk, bits=1, ptrs=(nw + a, idx, nw + b, 106 + kk)))
    for i in range(nw):
        pp = perp_of(W[i - 1], W[(i + 1) % nw])
        shift = (ex_r[i] - ex_l[i]) / 2.0            # racing line -> centre along +perp
        hw = (ex_l[i] + ex_r[i]) / 2.0
        dw = max(hw + 1.0, (wide_l[i] + wide_r[i]) / 2.0 + 1.0) if wide_l is not None else hw + 1.0
        wps.append(dict(pos=W[i] + shift * pp, perp=pp, width=(hw, hw),
                        dwidth=(dw, dw), path=sh[i] - shift, galpha=float(galpha[i]), score=(sector(s[i]), s[i]), branch=0, bits=0,
                        ptrs=((i - 1) % nw, (i + 1) % nw, third.get(i, -1), 0), cty=int(cty[i]), cst=int(cst[i])))
    wps += pitw
    out = [x.replace('number_waypoints=%d' % nw, 'number_waypoints=%d' % len(wps)) for x in out]
    for i, w in enumerate(wps):
        out += [BS + BS + str(i),
                'wp_pos=(%.4f,%.4f,%.4f)' % tuple(w['pos']), 'wp_perp=(%.4f,0.0000,%.4f)' % (w['perp'][0], w['perp'][2]),
                'wp_width=(%.3f,%.3f)' % w['width'], 'wp_dwidth=(%.3f,%.3f)' % w['dwidth'],
                'wp_path=(%.4f,0.0000)' % w['path'], 'wp_galpha=(%.4f)' % w['galpha'], 'wp_score=(%d,%.3f)' % w['score'],
                'wp_groove_lat=(%.6f)' % (-0.27 * w['path']), ('wp_event=(1.000,2,1.100000)' if w.get('cst', 0) in (2, 3) and w['branch'] == 0 else 'wp_event=(1.000,0,0.000000)'), 'wp_branchID=(%d)' % w['branch'],
                'wp_bitfields=(%d)' % w['bits'], 'wpd_CornerType=(%d)' % w.get('cty', 0), 'wpd_CornerState=(%d)' % w.get('cst', 0), 'WP_PTRS=(%d,%d,%d,%d)' % w['ptrs']]
    info = dict(tdry=tdry, ncorners=int((cst == 1).sum()), lap=lap, s1=s1, s2=s2, nw=nw, W=W, half=half,
                camera_centres=np.array([w['pos'] for w in wps[:nw]]),
                camera_half=np.array([w['width'][0] for w in wps[:nw]]),
                starts=len(starts), pits=len(pits), margin_left=float(margin_l.min()), margin_right=float(margin_r.min()),
                npit=len(pitw) - (len(pits) if pit is not None else 0))
    return '\n'.join(out) + '\n', info


def pit_section(fast, fextra, pitlane, span=5.0, merge=4.0):
    """AC pit_lane.ai is a slow full lap (not closed).  Pit section = contiguous run outside the fast-lane track edges
    (sideLeft/Right + 0.5 m) around the middle of AC_PIT_n, extended until it is within `merge` m of the racing line.
    Returned resampled at `span` m, in AC coordinates."""
    from scipy.spatial import cKDTree
    d, j = cKDTree(fast).query(pitlane)
    tng = np.roll(fast, -1, 0) - np.roll(fast, 1, 0); tng[:, 1] = 0; tng /= np.linalg.norm(tng, axis=1)[:, None]
    rel = pitlane - fast[j]
    lat = rel[:, 0] * tng[j, 2] - rel[:, 2] * tng[j, 0]
    # AC +perp is col 5 (left), -perp is col 6 (right), as in build_gcl/tarmac_edges.
    outside = (lat > fextra[j, 5] + 0.5) | (-lat > fextra[j, 6] + 0.5)
    return d, outside


def build_pit(fast, fextra, pitlane, pit_dummies, span=5.0, merge=4.0):
    from scipy.spatial import cKDTree
    d, outside = pit_section(fast, fextra, pitlane)
    stalls = np.array([M[3, :3] for _, M in pit_dummies])
    stall_distance, stall_indices = cKDTree(pitlane).query(stalls)
    c = int(np.median(stall_indices))
    # Some AC widths include the entire pit apron (Albert Park). An explicit pit lane anchored
    # at AC_PIT still identifies a distinct branch if it is farther than the merge distance
    # from the fast lane, even when the broad AI corridor calls it "inside".
    print('  pit source: anchor=%d distance_to_fast=%.3f outside_width=%s stall_to_lane_max=%.3f' %
          (c, d[c], outside[c], float(stall_distance.max())))
    if not outside[c] and d[c] <= merge:
        return None
    a = c
    while a > 0 and (outside[a - 1] or d[a - 1] > merge):
        a -= 1
    b = c
    while b < len(pitlane) - 1 and (outside[b + 1] or d[b + 1] > merge):
        b += 1
    seg = pitlane[a:b + 1]
    cum = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(seg, axis=0), axis=1))]
    n = max(int(cum[-1] // span), 2)
    t = np.linspace(0, cum[-1], n + 1)
    return np.c_[[np.interp(t, cum, seg[:, i]) for i in range(3)]].T


# ---------------------------------------------------------------- collision (.csm)
# AC surface KEY -> AMS2 csm surface id.  The csm id is the shared-memory terrain enum (Type#11 in the game's
# Support/SharedMemory/.../SharedMemory.h): 0 ROAD, 7 GRASS, 8 GRAVEL, 10 RUMBLE_STRIPS, 12 TYREWALLS, 13 CEMENTWALLS,
# 14 GUARDRAILS, 32 ASTROTURF, 46 RUNOFF_ROAD [V: Sochi render materials + official track geometry, see the format spec].
# AC "SAND" is the gravel trap of circuit tracks (sand.wav), so it maps to GRAVEL.
SURF_ID = {'TARMAC': 0, 'PITLANE': 0, 'ROAD': 0, 'PIT': 0, 'PAVEMENT': 0, 'ROFF': 7, 'KERB': 10, 'CARPET': 7, 'OFFTRACK': 7, 'SAND': 8, 'GRAVEL': 8, 'GRASS': 7, 'WALL': 13}
GRASS_ID, GRAVEL_ID, ASTROTURF_ID, RUNOFF_ROAD_ID = 7, 8, 32, 46


def surface_keys(ini_path):
    keys = set()
    if os.path.exists(ini_path):
        with open(ini_path, encoding='latin1') as stream:
            for line in stream:
                mm = re.match(r'\s*KEY\s*=\s*((\w+)[\w-]*)', line)
                if mm:
                    keys.update((mm.group(1).upper(), mm.group(2).upper()))   # full key (PEN-CNC-A) + legacy word prefix
    return keys | {'WALL', 'ROAD'}         # WALL and ROAD are AC built-in surfaces (Monaco declares only 1ASPHALT but names meshes 1ROAD_phy*)


def invalid_keys(ini_path):
    bad = set(); cur = None
    if os.path.exists(ini_path):
        with open(ini_path, encoding='latin1') as stream:
            for line in stream:
                mm = re.match(r'\s*KEY\s*=\s*((\w+)[\w-]*)', line)
                if mm:
                    cur = (mm.group(1).upper(), mm.group(2).upper())
                mm = re.match(r'\s*IS_VALID_TRACK\s*=\s*(\d+)', line)
                if mm and cur and int(mm.group(1)) == 0:
                    bad.update(cur)
    return bad


def surface_props(ini_path):
    """KEY -> {FRICTION, DIRT_ADDITIVE, WAV} from surfaces.ini (missing numbers -> None)."""
    props = {}; cur = None
    if os.path.exists(ini_path):
        with open(ini_path, encoding='latin1') as stream:
            for line in stream:
                line = line.split(';')[0].strip()
                if line.startswith('['):
                    cur = None
                    continue
                mm = re.match(r'(\w+)\s*=\s*(.*)', line)
                if not mm:
                    continue
                name, value = mm.group(1).upper(), mm.group(2).strip()
                if name == 'KEY':
                    cur = props.setdefault(value.upper(), {'FRICTION': None, 'DIRT_ADDITIVE': None, 'WAV': ''})
                elif cur is not None and name in ('FRICTION', 'DIRT_ADDITIVE'):
                    try:
                        cur[name] = float(value)
                    except ValueError:
                        pass
                elif cur is not None and name == 'WAV':
                    cur['WAV'] = value.lower()
    return props


def surf_id(key, bad, props=None):
    semantic = surface_kind(key)
    if semantic in SURF_ID:
        sid = SURF_ID[semantic]
    elif semantic in ('CURB', 'RUMBLE'):
        return 10
    else:
        sid = GRASS_ID if key in bad else 0
    p = (props or {}).get(key)
    if sid != GRASS_ID or semantic == 'GRASS' or not p:
        return sid
    # off-track surface: the AC physics say what it is (grass.wav / sand.wav / extraturf.wav, or a paved runoff
    # with asphalt-like friction and no dirt) instead of defaulting everything to grass.
    wav = p['WAV']
    if 'grass' in wav:
        return GRASS_ID
    if 'sand' in wav or 'gravel' in wav:
        return GRAVEL_ID
    if 'turf' in wav:
        return ASTROTURF_ID
    if (p['FRICTION'] or 0) >= 0.9 and (p['DIRT_ADDITIVE'] or 0) <= 0.05:
        return RUNOFF_ROAD_ID
    return GRASS_ID


def surface_kind(key):
    """Semantic category only; preserve the original full key for validity lookups/statistics."""
    name = re.sub(r'^\d+', '', key or '').upper()
    return max((kind for kind in (*SURF_ID, 'CURB', 'RUMBLE') if name.startswith(kind)), key=len, default=name)


def match_surface(name, keys):
    """AC collision names start with digits; match full keys or the legacy prefix after those digits."""
    if not re.match(r'^\d', name):
        return None
    full = name.upper()
    unnumbered = re.sub(r'^\d+', '', full)
    return max((key.upper() for key in keys if full.startswith(key.upper()) or unnumbered.startswith(key.upper())),
               key=lambda key: (len(key), key), default=None)


def smooth_kerbs(items, radius=0.8, cap=0.065):
    """AC kerb collision meshes are modelled ridge by ridge (median triangle 0.01-0.03 m2, 2-3% steep faces); AMS2's own kerbs are
    smooth ramps (0.3 m2, no steep faces).  The ridges shake the car, launch it and roll it over at the kerbs the AI uses.
    Smooth the kerb heights over `radius` and keep them at most `cap` above the neighbouring asphalt."""
    from scipy.spatial import cKDTree
    kv = [i for i, it in enumerate(items) if it[2] == 10]
    rv = [it[0] for it in items if it[2] in (0,)]
    if not rv:
        return items
    if not kv:
        return smooth_road(items)
    allk = np.vstack([items[i][0] for i in kv]).astype(np.float64)
    road = np.vstack(rv).astype(np.float64)
    tk = cKDTree(allk); tr = cKDTree(road)
    nb = tk.query_ball_point(allk, radius)
    zs = np.array([allk[j, 1].mean() for j in nb])
    d, ir = tr.query(allk, distance_upper_bound=2.0)
    ok = np.isfinite(d)
    zr = np.where(ok, road[np.minimum(ir, len(road) - 1), 1], zs)
    zn = np.where(ok, np.clip(zs, zr - 0.01, zr + cap), zs)
    print('  kerbs smoothed: %d vertices, mean |dz| %.3f m, max %.3f m' % (len(allk), np.abs(zn - allk[:, 1]).mean(), np.abs(zn - allk[:, 1]).max()))
    out = smooth_road(items, tr)
    o = 0
    for i in kv:
        U, T, sid = items[i]
        U2 = U.copy(); U2[:, 1] = zn[o:o + len(U)].astype(np.float32); o += len(U)
        out[i] = (U2, T, sid)
    return out


def smooth_road(items, tr=None, radius=1.0, maxcorr=0.05):
    """Remove short-wave asphalt noise with a local plane, preserving banked/sloping roads.
    A height mean on irregular sampling itself introduces ripples; corrections stay bounded by `maxcorr`."""
    from road_surface import smooth_heights
    out = list(items)
    ri = [i for i, it in enumerate(items) if it[2] == 0]
    if not ri:
        return out
    allr = np.vstack([items[i][0] for i in ri]).astype(np.float64)
    corr = smooth_heights(allr, radius, maxcorr) - allr[:, 1]
    print('  asphalt smoothed: mean |dz| %.4f m, max %.3f m' % (np.abs(corr).mean(), np.abs(corr).max()))
    o = 0
    for i in ri:
        U, T, sid = items[i]
        U2 = U.copy(); U2[:, 1] = (U[:, 1] + corr[o:o + len(U)]).astype(np.float32); o += len(U)
        out[i] = (U2, T, sid)
    return out


def build_csm(k, ini_path):
    from csm_write import write_csm
    keys = surface_keys(ini_path) | {'SAND', 'GRASS'}
    bad = invalid_keys(ini_path)
    props = surface_props(ini_path)
    items = []; stats = collections.Counter()
    for m in k['meshes']:
        key = match_surface(m['name'], keys)
        if key is None or not len(m['idx']):
            continue
        low = m['name'].lower()
        sid = surf_id(key, bad, props)
        if surface_kind(key) == 'WALL':
            sid = 12 if 'tyre' in low or 'tire' in low else 14 if ('armco' in low or 'rail' in low or 'barrier' in low) else 13
        P = (m['pos'] * MIRROR).astype(np.float32)
        T = m['idx'].reshape(-1, 3)[:, [0, 2, 1]]                  # mirrored -> reverse winding (same as render)
        U, inv = np.unique(P, axis=0, return_inverse=True)          # weld split (uv-seam) vertices
        T = inv.reshape(-1)[T]
        items.append((U, T, sid)); stats[(key, sid)] += len(T)
    items = smooth_kerbs(items)
    data, n = write_csm(items)
    return data, n, stats


# ---------------------------------------------------------------- track limits (.gcl)
def _strip(C, left, right, nrm):
    """quads between consecutive centre points (rows), returns (M,3,3) triangles."""
    L = C + nrm * left[:, None]; R = C - nrm * right[:, None]
    tris = []
    for i in range(len(C) - 1):
        tris += [(L[i], R[i], L[i + 1]), (R[i], R[i + 1], L[i + 1])]
    return np.array(tris)


def pit_light_zone(pitc, meshes, lane_halfwidth=5.0):
    """Explicit pitlightin/out mesh clusters may delimit a limiter zone when PITLANE meshes are absent.
    Both clusters must project onto the pit strip, each at one station, in entry-to-exit order.
    """
    groups = {'in': [], 'out': []}
    for mesh in meshes:
        match = re.fullmatch(r'pitlight(in|out)(?:_sub\d+)?', mesh['name'], re.I)
        if match and len(mesh['pos']):
            groups[match.group(1).lower()].append((mesh['name'], (mesh['pos'].min(0) + mesh['pos'].max(0)) * 0.5))
    if not any(groups.values()):
        return None
    if not all(groups.values()):
        raise ValueError('Pit limiter anchors need both pitlightin and pitlightout clusters')
    points = np.asarray(pitc)[:, [0, 2]]
    delta = np.diff(points, axis=0)
    lengths = np.linalg.norm(delta, axis=1)
    if not np.isfinite(points).all() or np.any(lengths < 1e-6):
        raise ValueError('Invalid pit lane for limiter anchor projection')
    cum = np.r_[0., np.cumsum(lengths)]
    stations = {}
    for role, members in groups.items():
        role_stations, distances = [], []
        for name, position in members:
            position = position[[0, 2]]
            fraction = np.clip(np.sum((position - points[:-1]) * delta, axis=1) / lengths ** 2, 0, 1)
            projections = points[:-1] + fraction[:, None] * delta
            ds = np.linalg.norm(projections - position, axis=1)
            segment = int(np.argmin(ds))
            if not np.isfinite(ds[segment]) or ds[segment] > lane_halfwidth:
                raise ValueError('Pit limiter anchor outside pit strip: %s (%.3f m)' % (name, ds[segment]))
            # A similarly close, distant station indicates a self-overlap/ambiguous projection.
            station = cum[:-1] + fraction * lengths
            ambiguous = (ds <= ds[segment] + 0.25) & (np.abs(station - station[segment]) > 2 * lane_halfwidth)
            if ambiguous.any():
                raise ValueError('Ambiguous pit limiter anchor projection: ' + name)
            role_stations.append(station[segment]); distances.append(ds[segment])
        if np.ptp(role_stations) > 2 * lane_halfwidth:
            raise ValueError('Ambiguous pit limiter %s anchors at multiple stations' % role)
        stations[role] = float(np.median(role_stations))
        print('  pit limiter anchor: role=%s source=%s station=%.3f max_distance=%.3f' %
              (role, ','.join(name for name, _ in members), stations[role], max(distances)))
    if stations['out'] <= stations['in']:
        raise ValueError('Pit limiter anchors reversed: pitlightout must follow pitlightin')
    a, b = (int(np.argmin(np.abs(cum - stations[role]))) for role in ('in', 'out'))
    if b - a < 2:
        raise ValueError('Pit limiter anchor zone is too short for two segments')
    print('  pit limiter zone: source=pitlightin/out indices=%d:%d length=%.3f' % (a, b, cum[b] - cum[a]))
    return a, b


def build_gcl(fast, fextra, sf, pitc, pitlane_tris, span=5.0, limiter_zone=None):
    """Sochi .gcl [V]: flag 1 = drivable strip along the racing line out to the track edges (|lat| <= 30),
    flag 2 = pit-lane strip (+-5 m) over the speed-limited part, flag 10 / 4 = 2-tri quads at the start / end of
    that part.  Here: AC fast_lane sideLeft/sideRight (col 5 = +lat side, col 6 = -lat side [V vs tarmac meshes])
    and the pit zone = pit branch points lying on AC PITLANE physics meshes [H]."""
    from gcl import write_gcl
    from gcl import sat_xz
    P = fast; L = np.clip(fextra[:, 5], 0.5, 30.0); R = np.clip(fextra[:, 6], 0.5, 30.0)
    k0 = int(np.argmin(np.linalg.norm(P - sf, axis=1)))
    P = np.roll(P, -k0, 0); L = np.roll(L, -k0); R = np.roll(R, -k0)
    Pc = np.vstack([P, P[:1]]); Lc = np.r_[L, L[:1]]; Rc = np.r_[R, R[:1]]
    cum = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(Pc, axis=0), axis=1))]
    n = int(cum[-1] // span); s = np.r_[np.linspace(0, cum[-1], n, endpoint=False), cum[-1]]
    C = np.c_[[np.interp(s, cum, Pc[:, i]) for i in range(3)]].T
    l = np.interp(s, cum, Lc); r = np.interp(s, cum, Rc)
    t = np.roll(C[:-1], -1, 0) - np.roll(C[:-1], 1, 0); t = np.vstack([t, t[:1]]); t[:, 1] = 0
    t /= np.linalg.norm(t, axis=1)[:, None]
    nrm = np.c_[t[:, 2], np.zeros(len(t)), -t[:, 0]]
    tris = [_strip(C, l, r, nrm)]; flags = [np.full(len(tris[0]), 1)]
    zone = None
    if pitc is not None and pitlane_tris is not None and len(pitlane_tris):
        PT = pitlane_tris[:, :, [0, 2]].astype(np.float64)
        on = np.array([len(sat_xz(PT, np.arange(len(PT)), q[0] - 0.05, q[2] - 0.05, 0.1)) > 0 for q in pitc])
        if on.any():
            zone = (int(np.argmax(on)), len(on) - 1 - int(np.argmax(on[::-1])))
            print('  pit limiter zone: source=PITLANE collision triangles indices=%d:%d' % zone)
    if zone is None and limiter_zone is not None:
        zone = limiter_zone
    if pitc is not None and zone is not None:
        a, b = zone
        if not 0 <= a < b < len(pitc):
            raise ValueError('Pit limiter zone indices outside pit lane')
        Q = pitc[a:b + 1]
        tq = np.gradient(Q, axis=0); tq[:, 1] = 0; tq /= np.linalg.norm(tq, axis=1)[:, None]
        nq = np.c_[tq[:, 2], np.zeros(len(tq)), -tq[:, 0]]
        five = np.full(len(Q), 5.0)
        ps = _strip(Q, five, five, nq); tris.append(ps); flags.append(np.full(len(ps), 2))
        for idx, fl, sgn in ((0, 10, 1), (len(Q) - 1, 4, -1)):
            c2 = np.array([Q[idx], Q[idx] + tq[idx] * 2.0 * sgn])
            if sgn < 0:
                c2 = c2[::-1]
            qd = _strip(c2, np.full(2, 5.0), np.full(2, 5.0), np.array([nq[idx]] * 2))
            tris.append(qd); flags.append(np.full(2, fl))
    T = np.vstack(tris) * MIRROR
    nY = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])[:, 1]
    T[nY < 0] = T[nY < 0][:, [0, 2, 1]]                     # Sochi: every gcl triangle has +y normal [V]
    F = np.concatenate(flags).astype(np.uint32)
    return write_gcl(T, F), len(T), zone


# ---------------------------------------------------------------- TV cameras
def read_ac_cameras(path):
    cams = []; cur = None
    for line in open(path, encoding='latin1'):
        line = line.strip()
        if re.fullmatch(r'\[CAMERA_\d+\]', line):
            cur = {}; cams.append(cur)
        elif cur is not None and '=' in line:
            kk, v = line.split('=', 1); cur[kk.strip()] = v.strip()
    out = []
    for c in cams:
        try:
            pos = np.array([float(x) for x in c['POSITION'].split(',')])
            out.append(dict(name=c.get('NAME', ''), pos=pos, a=float(c['IN_POINT']), b=float(c['OUT_POINT'])))
        except (KeyError, ValueError):
            pass
    return out


def build_cameras(template, cams, fast, W, half, tid):
    """Sochi cameras\\<track>.xml [V structure]: CCameraDataSet{Trackside Cams = CTrackingCamData (Pos, QuatOri 1;0;0;0,
    ActiveAreas = indices into Areas), Splines = 0, Areas = CSphereArea (Centre on the AI line, Radius 6.5)}.
    AC IN/OUT_POINT are fractions of fast_lane.ai length; each AIW waypoint goes to the camera whose [in,out) holds
    it, uncovered waypoints to the nearest camera.  Radius = max(6.5, half width + 2) so cars at the edge stay
    inside [H: Sochi uses 6.5 everywhere]."""
    from scipy.spatial import cKDTree
    cum = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(fast, axis=0), axis=1))]
    lapF = cum[-1] + np.linalg.norm(fast[0] - fast[-1])
    frac = cum[cKDTree(fast).query(W * MIRROR)[1]] / lapF
    owner = np.full(len(W), -1)
    for ci, c in enumerate(cams):
        a, b = c['a'], c['b']
        m = (frac >= a) & (frac < b) if a <= b else (frac >= a) | (frac < b)
        owner[m & (owner < 0)] = ci
    P = np.array([c['pos'] * MIRROR for c in cams])
    for i in np.nonzero(owner < 0)[0]:
        owner[i] = int(np.argmin(np.linalg.norm(P - W[i], axis=1)))
    head, rest = template.split('<prop name="Trackside Cams" elements=', 1)    # (not the class declaration)
    cam_t = re.search(r'<data class="CTrackingCamData".*?</data>', rest, re.S).group(0)
    area_t = re.search(r'<data class="CSphereArea".*?</data>', rest, re.S).group(0)
    ind = ' ' * 16
    def setp(b, key, val):
        return re.sub(r'(<prop name="%s" data=")[^"]*(")' % re.escape(key), lambda mm: mm.group(1) + val + mm.group(2), b, count=1)
    cam_xml = []; area_xml = []; k = 0
    for ci, c in enumerate(cams):
        ids = []
        # keep waypoint order starting at the camera's IN point
        wp = np.nonzero(owner == ci)[0]
        if len(wp) == 0:
            continue
        gaps = np.nonzero(np.diff(wp) > 1)[0]
        if len(gaps):
            wp = np.r_[wp[gaps[0] + 1:], wp[:gaps[0] + 1]]
        nm = '%s_main_%02d' % (tid, len(cam_xml) + 1)
        for j, i in enumerate(wp):
            b = setp(area_t, 'Name', '%s_visible_%03d' % (nm, j))
            b = setp(b, 'Centre', ';'.join(f6(x) for x in (W[i][0], W[i][1] + 0.8, W[i][2])))
            b = setp(b, 'Radius', f6(max(6.5, half[i] + 2.0)))
            b = re.sub(r'id="0x[0-9A-F]+"', 'id="0x%08X"' % (0xA5B20000 + k), b, count=1)
            area_xml.append(b); ids.append(k); k += 1
        b = setp(cam_t, 'Name', nm)
        b = setp(b, 'Pos', ';'.join(f6(x) for x in c['pos'] * MIRROR))
        b = re.sub(r'id="0x[0-9A-F]+"', 'id="0x%08X"' % (0xA5C10000 + len(cam_xml)), b, count=1)
        b = re.sub(r'<prop name="ActiveAreas" elements="\d+">\s*<funcpropdata[^>]*/>',
                   '<prop name="ActiveAreas" elements="%d">\n%s        <funcpropdata %s />' %
                   (len(ids), ind, ' '.join('areaIndex%d="%d"' % (n + 1, x) for n, x in enumerate(ids))), b, count=1)
        cam_xml.append(b)
    body = ('<prop name="Trackside Cams" elements="%d">\n            <funcpropdata>\n%s%s\n            </funcpropdata>\n'
            '        </prop>\n        <prop name="Splines" elements="0">\n            <funcpropdata />\n        </prop>\n'
            '        <prop name="Areas" elements="%d">\n            <funcpropdata>\n%s%s\n            </funcpropdata>\n'
            '        </prop>\n    </data>\n</Reflection>\n') % (len(cam_xml), ind, ('\n' + ind).join(cam_xml),
                                                         len(area_xml), ind, ('\n' + ind).join(area_xml))
    return head + body, len(cam_xml), len(area_xml)


# ---------------------------------------------------------------- triggers
TRIG_CRC = {'AC_TIME_0': 4110219313, 'AC_TIME_1': 2964580884, 'AC_TIME_2': 2964580887}   # roles inferred on Sochi


def build_triggers(template, dummies):
    head, rest = template.split('<funcpropdata>', 1)
    blocks = re.findall(r'\s*<data class="ShapeDesc".*?</data>', rest, re.S)
    tail = rest.split(blocks[-1], 1)[1]
    tmpl = blocks[0]
    shapes = []
    for k, (name, crc) in enumerate(TRIG_CRC.items()):
        L = dummies[name + '_L'][3, :3] * MIRROR; R = dummies[name + '_R'][3, :3] * MIRROR
        c = (L + R) * 0.5
        across = R - L; across[1] = 0; width = float(np.linalg.norm(across)); across /= width
        d = np.array([-across[2], 0.0, across[0]])          # along-track (perp rule: across = (dz,0,-dx))
        rows = [d, np.array([0.0, -1.0, 0.0]), np.array([d[2], 0.0, -d[0]])]
        b = tmpl
        def setp(b, key, val):
            return re.sub(r'(name="%s" data=")[^"]*(")' % re.escape(key), lambda m: m.group(1) + val + m.group(2), b)
        b = setp(b, 'Name', '%08x' % (0x40c0f61b + k))
        b = setp(b, 'Length', f6(width + 10.0))
        b = setp(b, 'Material CRC', str(crc))
        b = setp(b, 'Relative Position', ';'.join(f6(x) for x in c))
        b = setp(b, 'Relative Orientation', ';'.join(f6(x + 0.0) for r in rows for x in r))
        b = re.sub(r'id="0x[0-9A-F]+"', 'id="0x%08X"' % (0xF3280FA0 + k), b, count=1)
        shapes.append(b)
    head = re.sub(r'(<prop name="Shapes" elements=")\d+', lambda m: m.group(1) + str(len(shapes)), head)
    return head + '<funcpropdata>' + ''.join(shapes) + tail


# ---------------------------------------------------------------- trd
def build_trd(template, tid, lap, meta=None, starts=0, pits=0):
    """UI/CLI metadata; unknown coordinates use neutral zero placeholders, never Bahrain's location."""
    m = dict(name=tid, short=tid, location='', country='', city='', variation='',
             lat='0', lon='0', tz='0', turns='0', year='0', cw='false', altitude='0', climate='')
    m.update(meta or {})
    capacity = min(starts, pits)
    if m.get('pitboxes'):
        capacity = min(capacity, int(m['pitboxes']))
    description = '%s - AC kn5 conversion' % m['short']
    if m.get('version'):
        description += ' (source version %s)' % m['version']
    vals = {'Name': tid, 'TrackName': m['name'], 'ShortTrackName': m['short'],
            'ScenegraphFile': tid + '.sgx', 'Year': m['year'], 'Length': str(int(round(lap))), 'Location': m['location'],
            'Country': m['country'], 'Track Group': '%s (AC conversion)' % m['short'],
            'Track Description': description, 'Is Clockwise': m['cw'],
            'Track_Location': m['city'], 'Track_Variation': m['variation'], 'Track_Longitude': m['lon'],
            'Track_Latitude': m['lat'], 'Track_Altitude': m['altitude'], 'Track_TimeZone': m['tz'], 'Number Of Turns': m['turns'],
            'Max AI participants': str(max(0, capacity - 1)), 'Drs Zone 1 Detection Number': '0',
            'Drs Zone 2 Detection Number': '0', 'Order Override': '1'}
    if m['climate']:
        vals['Track_Climate'] = m['climate']
    from xml.sax.saxutils import escape
    for k, v in vals.items():
        v = escape(str(v), {'"': '&quot;'})
        template, n = re.subn(r'(name="%s" data=")[^"]*(")' % re.escape(k), lambda m: m.group(1) + v + m.group(2), template)
        assert n == 1, k
    if m.get('version'):
        template = re.sub(r'(name="(?:Version|Track Version)" data=")[^"]*(")',
                          lambda match: match.group(1) + escape(str(m['version']), {'"': '&quot;'}) + match.group(2), template)
    return template


# ---------------------------------------------------------------- GUI dds
def gui_dds(png, size):
    from PIL import Image
    import io
    im = Image.open(png).convert('RGBA')
    im.thumbnail(size)
    canvas = Image.new('RGBA', size, (0, 0, 0, 0)); canvas.paste(im, ((size[0] - im.width) // 2, (size[1] - im.height) // 2))
    b = io.BytesIO()
    try:
        canvas.save(b, 'DDS', pixel_format='DXT5')
    except Exception:
        b = io.BytesIO(); canvas.save(b, 'DDS')
    return b.getvalue()


def full_mips(data):
    """AMS2 samples the whole mip chain: DDS files with a truncated chain (Shanghai: 4096x1024 with 1 level) render as white
    block garbage.  Rebuild the missing levels (box-filtered) and re-encode them in the original DXT format."""
    import io, struct, math
    from PIL import Image
    if data[:4] != b'DDS ' or data[84:88] not in (b'DXT1', b'DXT3', b'DXT5'):
        return data
    h, w, _, _, mips = struct.unpack_from('<IIIII', data, 12)
    mips = mips or 1
    need = int(math.log2(max(w, h))) + 1
    if mips >= need or min(w, h) < 4:
        return data
    fmt = data[84:88].decode()
    im = Image.open(io.BytesIO(data)).convert('RGBA')
    out = bytearray(data[:128]); body = bytearray()
    for lv in range(need):
        lw, lh = max(w >> lv, 1), max(h >> lv, 1)
        lim = im if lv == 0 else im.resize((lw, lh), Image.BOX)
        b = io.BytesIO(); lim.save(b, 'DDS', pixel_format=fmt)
        body += b.getvalue()[128:]
    struct.pack_into('<I', out, 8, struct.unpack_from('<I', out, 8)[0] | 0x20000)
    struct.pack_into('<I', out, 28, need)
    struct.pack_into('<I', out, 108, struct.unpack_from('<I', out, 108)[0] | 0x400008)
    return bytes(out + body)


def tone_asphalt(data, target=75.0, keep_sat=0.3):
    """AC asphalt textures are authored bright because AC's shader darkens them; the same texture in AMS2 (rz_basic, lit) reads as
    pale grey.  Original AMS2 asphalt is neutral and dark (Sochi ROAD_BASE mean RGB 75/75/75): scale to that and mute the tint."""
    import io, struct, math
    from PIL import Image
    if data[:4] != b'DDS ' or data[84:88] not in (b'DXT1', b'DXT3', b'DXT5'):
        return data
    im = Image.open(io.BytesIO(data)).convert('RGBA')
    a = np.asarray(im).astype(np.float32)
    lum = float((a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114).mean())
    if lum <= target * 1.05:
        return data
    g = (a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114)[..., None]
    a[..., :3] = (g + (a[..., :3] - g) * keep_sat) * (target / lum)
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGBA')
    h, w = struct.unpack_from('<II', data, 12)
    need = int(math.log2(max(w, h))) + 1
    fmt = data[84:88].decode()
    out = bytearray(data[:128]); body = bytearray()
    for lv in range(need):
        lw, lh = max(w >> lv, 1), max(h >> lv, 1)
        lim = im if lv == 0 else im.resize((lw, lh), Image.BOX)
        b = io.BytesIO(); lim.save(b, 'DDS', pixel_format=fmt)
        body += b.getvalue()[128:]
    struct.pack_into('<I', out, 8, struct.unpack_from('<I', out, 8)[0] | 0x20000)
    struct.pack_into('<I', out, 28, need)
    struct.pack_into('<I', out, 108, struct.unpack_from('<I', out, 108)[0] | 0x400008)
    print('  asphalt texture lum %.0f -> %.0f' % (lum, target))
    return bytes(out + body)


def opaque_alpha(data):
    """AC keeps masks (detail strength, gloss) in the diffuse alpha of opaque ground; AMS2 rz_basic reads it as something else
    (Shanghai asphalt/runoff rendered white with dark smears), so force alpha to 255 on textures no material blends or tests."""
    import io, struct, math
    from PIL import Image
    if data[:4] != b'DDS ' or data[84:88] != b'DXT5':
        return data
    im = Image.open(io.BytesIO(data)).convert('RGBA')
    a = np.asarray(im).copy()
    if a[..., 3].min() >= 250:
        return data
    a[..., 3] = 255
    im = Image.fromarray(a, 'RGBA')
    h, w = struct.unpack_from('<II', data, 12)
    need = int(math.log2(max(w, h))) + 1
    out = bytearray(data[:128]); body = bytearray()
    for lv in range(need):
        lw, lh = max(w >> lv, 1), max(h >> lv, 1)
        lim = im if lv == 0 else im.resize((lw, lh), Image.BOX)
        b = io.BytesIO(); lim.save(b, 'DDS', pixel_format='DXT5')
        body += b.getvalue()[128:]
    struct.pack_into('<I', out, 8, struct.unpack_from('<I', out, 8)[0] | 0x20000)
    struct.pack_into('<I', out, 28, need)
    struct.pack_into('<I', out, 108, struct.unpack_from('<I', out, 108)[0] | 0x400008)
    print('  alpha stripped', w, h)
    return bytes(out + body)


def is_asphalt(mt):
    n = mt['name'].upper()
    return (detail_kind(mt) == 'road' or (mt['shader'] == 'ksMultilayer' and not n.startswith('KERB'))
            or n.startswith(('ROAD', 'TARMAC', 'ASPHALT', 'PITR', 'DROAD')) and 'KERB' not in n and 'LINE' not in n)


def legacy_dds(data):
    """AMS2 only loads classic DXT/BGRA DDS: a DX10-header DDS (BC7 etc., e.g. Shanghai's jumbotron_effect) is re-encoded to DXT5."""
    if data[84:88] != b'DX10':
        return data
    import io
    from PIL import Image
    im = Image.open(io.BytesIO(data)).convert('RGBA')
    b = io.BytesIO(); im.save(b, 'DDS', pixel_format='DXT5')
    print('  re-encoded DX10 texture ->', im.size, 'DXT5')
    return b.getvalue()


def source_path(src, relative):
    """Resolve source-controlled paths, rejecting absolute paths and traversal/symlink escape."""
    from pathlib import Path, PureWindowsPath
    root = Path(src).resolve()
    if Path(relative).is_absolute() or PureWindowsPath(relative).is_absolute() or PureWindowsPath(relative).drive:
        raise ValueError('Absolute source path is forbidden: ' + str(relative))
    candidate = (root / str(relative).replace('\\', '/')).resolve()
    if not candidate.is_relative_to(root):
        raise ValueError('Source path escapes track root: ' + str(relative))
    return str(candidate)


def source_locations(src, layout=None):
    """Layouts share geometry only; AI/cameras/UI belong to the selected layout."""
    if layout and (layout in ('.', '..') or not re.fullmatch(r'[^/\\:]+', layout)):
        raise ValueError('--layout must be one directory name')
    local = source_path(src, layout) if layout else os.path.abspath(src)
    if not os.path.isdir(local):
        raise ValueError('Layout/source directory does not exist: ' + local)
    ui = source_path(src, os.path.join('ui', layout)) if layout else source_path(src, 'ui')
    # surfaces describe the shared root geometry and may legitimately be shared by layouts.
    surfaces = os.path.join(local, 'data', 'surfaces.ini')
    if layout and not os.path.isfile(surfaces):
        surfaces = source_path(src, 'data/surfaces.ini')
    return dict(ai=os.path.join(local, 'ai'), cameras=os.path.join(local, 'data', 'cameras.ini'),
                surfaces=surfaces, ui=ui)


def source_metadata(ui, tid, layout=None):
    path = os.path.join(ui, 'ui_track.json')
    meta = dict(name=tid, short=tid, variation=layout or '')
    if not os.path.isfile(path):
        return meta
    with open(path, encoding='utf-8-sig') as stream:
        # AC UI files occasionally contain literal newlines inside descriptions (not executable data).
        data = json.load(stream, strict=False)
    for key in ('name', 'city', 'country', 'version'):
        if data.get(key) is not None:
            meta[key] = str(data[key])
    meta['short'] = meta['name']
    meta['location'] = meta.get('city', '')
    if data.get('pitboxes') not in (None, ''):
        count = int(data['pitboxes'])
        if count <= 0:
            raise ValueError('ui_track.json pitboxes must be positive')
        meta['pitboxes'] = count
    return meta


def model_files(src, layout=None, with_transforms=False):
    import configparser
    ini = source_path(src, 'models_%s.ini' % layout) if layout else source_path(src, 'models.ini')
    if layout and not os.path.isfile(ini):
        ini = source_path(src, 'models.ini')
    if os.path.isfile(ini):
        with open(ini, encoding='utf-8-sig', errors='replace') as stream:
            parser = configparser.ConfigParser(interpolation=None, inline_comment_prefixes=(';',))
            try:
                parser.read_file(stream)
            except configparser.Error as error:
                raise ValueError('Invalid models INI: %s: %s' % (ini, error)) from error
        instances = []
        for section in parser.sections():
            if not re.fullmatch(r'MODEL_\d+', section, re.I):
                continue
            values = parser[section]
            filename = values.get('FILE', '').strip().strip('"')
            vectors = []
            for field in ('POSITION', 'ROTATION'):
                try:
                    vector = np.array([float(part) for part in values.get(field, '0,0,0').split(',')])
                except ValueError as error:
                    raise ValueError('Invalid %s in %s/%s' % (field, ini, section)) from error
                if vector.shape != (3,) or not np.isfinite(vector).all():
                    raise ValueError('Invalid %s in %s/%s' % (field, ini, section))
                vectors.append(vector)
            position, rotation = vectors
            # AC models use heading first (Content Manager TrackMapRenderer.LoadModels).
            # Received nonzero rotations are yaw in degrees; reject unverified mixed Euler conventions.
            if np.any(np.abs(rotation[1:]) > 1e-9):
                raise ValueError('Unsupported model pitch/roll in %s/%s; only yaw degrees verified' % (ini, section))
            yaw = math.radians(float(rotation[0])); c, s = math.cos(yaw), math.sin(yaw)
            transform = np.array([[c, 0., -s, 0.], [0., 1., 0., 0.], [s, 0., c, 0.], [*position, 1.]])
            instances.append(dict(file=filename, transform=transform, section=section))
        files = [instance['file'] for instance in instances]
        if not files or any(not f.lower().endswith('.kn5') for f in files):
            raise ValueError('Missing/invalid FILE entries in ' + ini)
        # Validate every declared file, including ignored groove layers: a partial model is never silently exported.
        for name in files:
            path = source_path(src, name)
            if not os.path.isfile(path):
                raise ValueError('Missing KN5 declared in %s: %s' % (ini, name))
    else:
        files = sorted(f for f in os.listdir(src) if f.lower().endswith('.kn5'))
        instances = [dict(file=f, transform=np.eye(4), section='discovered') for f in files]
    instances = [instance for instance in instances if 'groove' not in instance['file'].lower()]
    if not instances:
        raise ValueError('No renderable KN5 files in ' + str(src))
    return instances if with_transforms else [instance['file'] for instance in instances]


def load_all_kn5(src, layout=None):
    """Merge every kn5 of the track (models.ini order, or all *.kn5 when there is none) into one kn5 dict.
    Single-kn5 tracks behave exactly as before.  Meshes/materials are re-indexed; textures with the same name but
    different bytes get a unique name.  Groove-only kn5s (rubber decals) are skipped (see DROP_MATS)."""
    import copy, hashlib
    instances = model_files(src, layout, with_transforms=True)
    out = {'version': 0, 'textures': {}, 'materials': [], 'meshes': [], 'dummies': []}
    cache = {}
    occurrences = collections.Counter(instance['file'] for instance in instances)
    for instance in instances:
        f, transform = instance['file'], instance['transform']
        source = source_path(src, f)
        if source in cache:
            k = cache[source]
        else:
            k = read_kn5(source)
            if occurrences[f] > 1:
                cache[source] = k
        print('  kn5', f, instance['section'], len(k['meshes']), 'meshes', len(k['materials']), 'materials')
        ren = {}
        for n, d in k['textures'].items():
            have = out['textures'].get(n)
            if have is None:
                out['textures'][n] = d
            elif have != d:
                # Content-derived prefix stays unique after main's filename sanitisation/truncation.
                ren[n] = 'tex_' + hashlib.sha256(d).hexdigest()[:16] + '_' + os.path.basename(n)
                if ren[n] in out['textures'] and out['textures'][ren[n]] != d:
                    raise ValueError('Texture content alias collision: ' + n)
                out['textures'][ren[n]] = d
        base = len(out['materials'])
        for source_material in k['materials']:
            mt = copy.deepcopy(source_material)
            mt['tex'] = {a: ren.get(b, b) for a, b in mt['tex'].items()}
            out['materials'].append(mt)
        for source_mesh in k['meshes']:
            m = dict(source_mesh)
            m['pos'] = m['pos'] @ transform[:3, :3] + transform[3, :3]
            m['nrm'] = m['nrm'] @ transform[:3, :3]
            m['tan'] = m['tan'] @ transform[:3, :3]
            m['mat'] += base
            out['meshes'].append(m)
        out['dummies'] += [(n, M @ transform) for n, M in k['dummies'] if n.upper().startswith('AC_')]
    return out


def ground_pit_dummies(k, dummies, ini_path, clearance=0.3, max_correction=3.5):
    """Explicitly ground every AC_PIT on source asphalt collision; refuse absent/stacked floors.
    Only Y is corrected. Source XZ and every orientation component remain unchanged.
    """
    keys, bad = surface_keys(ini_path), invalid_keys(ini_path)
    triangles = []
    for mesh in k['meshes']:
        key = match_surface(mesh['name'], keys)
        if key is not None and surf_id(key, bad) == 0 and len(mesh['idx']):
            triangles.append(mesh['pos'][mesh['idx'].reshape(-1, 3)])
    if not triangles:
        raise ValueError('--ground-pits: source has no asphalt collision triangles')
    triangles = np.vstack(triangles).astype(float)
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    v0, v1 = b - a, c - a
    determinant = v0[:, 0] * v1[:, 2] - v1[:, 0] * v0[:, 2]
    flat_valid = np.abs(determinant) > 1e-10
    result = dict(dummies)
    for name, matrix in dummies.items():
        if not re.fullmatch(r'AC_PIT_\d+', name):
            continue
        position = matrix[3, :3]
        q = position - a
        u = np.divide(q[:, 0] * v1[:, 2] - v1[:, 0] * q[:, 2], determinant,
                      out=np.zeros(len(a)), where=flat_valid)
        v = np.divide(v0[:, 0] * q[:, 2] - q[:, 0] * v0[:, 2], determinant,
                      out=np.zeros(len(a)), where=flat_valid)
        inside = flat_valid & (u >= -1e-7) & (v >= -1e-7) & (u + v <= 1 + 1e-7)
        heights = (a[:, 1] + u * v0[:, 1] + v * v1[:, 1])[inside]
        if not len(heights) or not np.isfinite(heights).all():
            raise ValueError('--ground-pits: no finite asphalt floor below XZ of ' + name)
        if np.ptp(heights) > 0.1:
            raise ValueError('--ground-pits: ambiguous stacked asphalt floors for %s (%.3f m spread)' % (name, np.ptp(heights)))
        target = float(np.median(heights)) + clearance
        correction = target - float(position[1])
        if abs(correction) > max_correction:
            raise ValueError('--ground-pits: excessive correction for %s: %.3f m (limit %.3f)' % (name, correction, max_correction))
        adjusted = matrix.copy(); adjusted[3, 1] = target
        result[name] = adjusted
        print('  ground pit: %s source_Y=%.3f floor_Y=%.3f target_Y=%.3f correction=%.3f' %
              (name, position[1], target - clearance, target, correction))
    return result


def tarmac_edges(k, fast, res=0.5, reach=30.0, kerbs=True, keys=None):
    """Distance from each fast_lane point to the real asphalt edge on the col-5 (+perp) and col-6 (-perp) sides, from a top-view
    raster of the tarmac* and kerb triangles.  AC side distances include kerbs and paved run-off; the AI must stay inside the white lines."""
    from PIL import Image, ImageDraw
    x0 = fast[:, 0].min() - reach - 10; z0 = fast[:, 2].min() - reach - 10
    w = int((fast[:, 0].max() + reach + 10 - x0) / res); h = int((fast[:, 2].max() + reach + 10 - z0) / res)
    img = Image.new('L', (w, h), 0); dr = ImageDraw.Draw(img)
    keys = set(SURF_ID) | {'CURB', 'RUMBLE'} if keys is None else keys
    for m in k['meshes']:
        road = k['materials'][m['mat']]['name'].lower().startswith('tarmac') and m['visible'] and m['renderable']
        if not kerbs and re.search(r'kerb|curb|rumble|sausage', m['name'] + ' ' + k['materials'][m['mat']]['name'], re.I):
            continue
        kind = surface_kind(match_surface(m['name'], keys))
        if road or kind in (('TARMAC', 'ROAD', 'PIT', 'PITLANE', 'KERB', 'CURB', 'RUMBLE') if kerbs else ('TARMAC', 'ROAD', 'PIT', 'PITLANE')):
            P = m['pos']
            T = m['idx'].reshape(-1, 3)
            if not road:     # collision 'road' meshes that are really ground planes (Phoenix p1g...) would hide the true edge
                A2 = 0.5 * np.abs((P[T[:, 1], 0] - P[T[:, 0], 0]) * (P[T[:, 2], 2] - P[T[:, 0], 2]) - (P[T[:, 2], 0] - P[T[:, 0], 0]) * (P[T[:, 1], 2] - P[T[:, 0], 2]))
                if A2.sum() > 100000:
                    continue
            for a, b, c in T:
                dr.polygon([((P[j, 0] - x0) / res, (P[j, 2] - z0) / res) for j in (a, b, c)], fill=255)
    A = np.asarray(img) > 0
    n = len(fast)
    out = np.zeros((n, 2))
    steps = np.arange(res, reach, res)
    for i in range(n):
        t = fast[(i + 1) % n] - fast[i - 1]; t[1] = 0; t /= (np.linalg.norm(t) or 1)
        perp = np.array([t[2], 0.0, -t[0]])
        for c, sg in enumerate((1, -1)):
            q = fast[i][None, :] + sg * steps[:, None] * perp[None, :]
            xi = ((q[:, 0] - x0) / res).astype(int); zi = ((q[:, 2] - z0) / res).astype(int)
            ok = A[np.clip(zi, 0, h - 1), np.clip(xi, 0, w - 1)]
            out[i, c] = 0.0 if not ok[0] else (reach if ok.all() else steps[np.argmin(ok)])
    return out


def wall_free(k, fast, reach=30.0, keys=None):
    from scipy.spatial import cKDTree
    """Free lateral distance from each fast_lane point to the nearest collision wall on the col-5 (+perp) and col-6 (-perp) sides
    (AC coords).  Walls = meshes whose surface key is WALL; vertices, edge midpoints and centroids are sampled."""
    pts = []; height_ranges = []
    keys = {'WALL'} if keys is None else keys
    for m in k['meshes']:
        if not len(m['idx']) or surface_kind(match_surface(m['name'], keys)) != 'WALL':
            continue
        P = m['pos']; T = m['idx'].reshape(-1, 3); triangles = P[T]
        # Associate every sample with its triangle's vertical span. A wall under a
        # bridge must not constrain the upper road; tall walls still count even
        # when none of their sampled vertices is at the car's height.
        samples = [triangles[:, j] for j in range(3)] + [triangles.mean(1)]
        samples += [0.5 * (triangles[:, j] + triangles[:, (j+1)%3]) for j in range(3)]
        heights = np.c_[triangles[:,:,1].min(1), triangles[:,:,1].max(1)]
        pts.extend(samples); height_ranges.extend([heights] * len(samples))
    n = len(fast); out = np.full((n, 2), reach)
    if not pts:
        return out
    Wp = np.vstack(pts); vertical = np.vstack(height_ranges); tr = cKDTree(Wp[:, [0, 2]])
    for i in range(n):
        t = fast[(i + 1) % n] - fast[i - 1]; t[1] = 0; t /= (np.linalg.norm(t) or 1)
        perp = np.array([t[2], 0.0, -t[0]])
        idx = tr.query_ball_point(fast[i][[0, 2]], reach)
        if not idx:
            continue
        d = Wp[idx][:, [0, 2]] - fast[i][[0, 2]]
        lat = d @ perp[[0, 2]]; lon = d @ t[[0, 2]]
        near = (np.abs(lon) < 4.0) & (vertical[idx,0] <= fast[i,1] + 1.8) & (vertical[idx,1] >= fast[i,1] - 0.5)
        pos = lat[near & (lat > 0)]; neg = -lat[near & (lat < 0)]
        if len(pos): out[i, 0] = min(reach, pos.min())
        if len(neg): out[i, 1] = min(reach, neg.min())
    return out


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('src', 'tid', 'out', 'sref', 'sx'):
        parser.add_argument(name)
    parser.add_argument('--layout', help='AC layout folder; geometry uses root models_<layout>.ini (fallback models.ini)')
    parser.add_argument('--require-complete-ai', action='store_true', help='Require fast-lane extras and a usable pit branch')
    parser.add_argument('--ground-pits', action='store_true', help='Explicitly align AC_PIT Y to source asphalt floor +0.3m; reject ambiguous/missing floors')
    parser.add_argument('--street', action='store_true')
    parser.add_argument('--max-meshes', type=int)
    parser.add_argument('--skip-mesh')
    parser.add_argument('--dump-aiw')
    for name in ('name', 'short', 'location', 'country', 'city', 'variation', 'lat', 'lon', 'tz', 'turns', 'year', 'cw'):
        parser.add_argument('--' + name)
    return parser.parse_args(argv)


def main():
    args = parse_args()
    src, tid, out, sref, sx = args.src, args.tid, args.out, args.sref, args.sx
    locations = source_locations(src, args.layout)
    keys = surface_keys(locations['surfaces']) | {'SAND', 'GRASS'}
    meta = source_metadata(locations['ui'], tid, args.layout)
    meta.update({key: getattr(args, key) for key in ('name', 'short', 'location', 'country', 'city', 'variation', 'lat', 'lon', 'tz', 'turns', 'year', 'cw')
                 if getattr(args, key) is not None})
    maxm = args.max_meshes
    root = os.path.join(out, tid, 'Automobilista 2')
    k = load_all_kn5(src, args.layout)
    dummies = {n: M for n, M in k['dummies']}
    if args.ground_pits:
        dummies = ground_pit_dummies(k, dummies, locations['surfaces'])
    print('kn5', len(k['meshes']), 'meshes', len(k['materials']), 'materials')
    fast, extra = read_ai(os.path.join(locations['ai'], 'fast_lane.ai'))
    pl = os.path.join(locations['ai'], 'pit_lane.ai')
    pitlane = read_ai(pl)[0] if os.path.isfile(pl) else None
    validate_ai_inputs(fast, extra, dummies, pitlane, args.require_complete_ai)
    dummies = order_timing_sectors(fast, dummies)
    pitc = None
    if pitlane is not None and extra is not None:
        pitc = build_pit(fast, extra, pitlane,
                         sorted((n, M) for n, M in dummies.items() if re.fullmatch(r'AC_PIT_\d+', n)))
    if args.require_complete_ai and pitc is None:
        raise ValueError('Complete AI requires a usable pit branch outside the fast lane')
    print('source metadata', json.dumps(meta, ensure_ascii=True, sort_keys=True))
    print('AI source: finite=True extras=%s pit_branch=%s' % (extra is not None, pitc is not None))

    main_items, loose = [], {}
    TD = 'tracks' + BS + tid + BS
    # materials (only those used by exported meshes)
    meshes = [m for m in k['meshes'] if m['visible'] and m['renderable'] and not m['name'].lower().startswith('ac_')
              and len(m['idx']) and k['materials'][m['mat']]['name'].lower() not in DROP_MATS]
    if args.skip_mesh:    # debug: leave out meshes whose name matches this regex
        rx = re.compile(args.skip_mesh, re.I); meshes = [m for m in meshes if not rx.search(m['name'])]
    if maxm:
        meshes.sort(key=lambda m: -len(m['idx'])); meshes = meshes[:maxm]
    used = sorted({m['mat'] for m in meshes})
    global AVAIL
    AVAIL = set(k['textures'])
    names = set()
    for i in used:
        mt = k['materials'][i]
        base = san(mt['name']).upper()
        if base[:1].isdigit():
            base = 'M' + base
        nm = base; j = 1
        while nm in names:
            j += 1; nm = '%s_%d' % (base, j)
        names.add(nm); mt['mname'] = nm
        tex = tex_of(mt)
        texname = (san(os.path.splitext(tex)[0]) + '.DDS') if tex else 'WHITE.DDS'
        mt['texname'] = texname
        mtx = make_mtx(mt, tid, texname)
        loose[nm + '.mtx'] = mtx.encode('utf-8')
        bm = bmt_encode(ET.fromstring(mtx))
        loose[nm + '.bmt'] = bm
        main_items.append(dict(name=TD + nm + '.bmt', data=bm))
    texs = {}; road_tex = set(); alpha_tex = set(); multi_tex = set()
    for i in used:
        mt = k['materials'][i]
        tex = tex_of(mt)
        if tex and tex in k['textures']:
            texs[mt['texname']] = k['textures'][tex]
            if mt['shader'].startswith('ksMultilayer'):
                multi_tex.add(mt['texname'])
            if mt['blend'] == 1 or mt['alphaTested'] or mt['shader'] in ('ksPerPixelAT', 'ksPerPixelAT_NM', 'ksTree', 'ksGrass'):
                alpha_tex.add(mt['texname'])
            if is_asphalt(mt):
                road_tex.add(mt['texname'])
    for tn, data in sorted(texs.items()):
        data = full_mips(legacy_dds(data))
        if tn not in alpha_tex and (tn in road_tex or tn in multi_tex or 'TRACKSURFACE' in tn or 'RUNOFF' in tn):
            data = opaque_alpha(data)
        if tn in road_tex:
            data = tone_asphalt(data)
        main_items.append(dict(name='tracks' + BS + 'textures' + BS + tid + BS + tn, data=data))
    print('materials', len(used), 'textures', len(texs))

    # meshes
    sgx_nodes = []; mnames = set(); allmin = np.full(3, 1e30); allmax = np.full(3, -1e30)
    for mi, m in enumerate(meshes):
        base = san(m['name'])
        if base[:1].isdigit():
            base = 'm' + base                                          # AMS2 drops meshes whose name starts with a digit
        nm = base; j = 1
        while nm.lower() in mnames:
            j += 1; nm = '%s_%d' % (base, j)
        mnames.add(nm.lower())
        P = (m['pos'] * MIRROR).astype(np.float32)
        N = (m['nrm'] * MIRROR); N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
        T = (m['tan'] * MIRROR); T /= np.maximum(np.linalg.norm(T, axis=1, keepdims=True), 1e-9)
        B = np.cross(N, T)
        idx = m['idx'].reshape(-1, 3)[:, [0, 2, 1]].ravel()        # mirrored -> reverse winding
        mn, mx = P.min(0), P.max(0); c = (mn + mx) * np.float32(0.5); r = float(np.linalg.norm(mx - c))
        bounds = tuple(map(float, c)) + (r,) + tuple(map(float, mn)) + tuple(map(float, mx))
        mat = 'TRACKS' + BS + tid.upper() + BS + k['materials'][m['mat']]['mname'] + '.MTX'
        mt_ = k['materials'][m['mat']]
        dk = detail_kind(mt_)
        uv = (m['uv'] * metric_uv_scale(m['pos'], m['uv'], m['idx'], DETAIL_TILE_M[dk]) if dk
              else m['uv'] * np.array(UV_MULTILAYER) if tex_of(mt_) == TARMAC_TILE else m['uv'])
        data = write_meb(nm, P.tolist(), N.astype(np.float32).tolist(), uv.astype(np.float32).tolist(),
                         [(mat, idx.tolist())], tangents=T.astype(np.float32).tolist(),
                         binormals=B.astype(np.float32).tolist(), bounds=[bounds, bounds])
        main_items.append(dict(name=TD + nm + '.meb', data=data))
        allmin = np.minimum(allmin, mn); allmax = np.maximum(allmax, mx)
        sgx_nodes.append((nm, c, r))
        if mi % 200 == 0:
            print(' mesh', mi, '/', len(meshes))
    sgx = ["<?xml version='1.0' encoding='utf-8'?>",
           '<SCENE FileVersion="0.1.0.0" ExporterVersion="Open Madness Track Tools 0.1.0" NumObjects="%d" NumPartitions="1">' % len(sgx_nodes)]
    for i, (nm, c, r) in enumerate(sgx_nodes, 1):
        sgx += ['    <OBJ_ID no="%d">' % i,
                '        <NODE type="OBJECT" Name="%s" MatrixNumber="-1" instances="1" userflags="1048693">' % nm.lower(),
                '            <RESOURCE Filename="tracks/%s/%s.meb" />' % (tid, nm.lower()),
                '            <SPHERE Centre="%s %s %s 1.000000" Radius="%s" />' % (f6(c[0]), f6(c[1]), f6(c[2]), f6(r)),
                '            <MATRIX Offset="0.000000 0.000000 0.000000" Orientation="0.000000 0.000000 -0.000000 -1.000000" Scale="1.000000" />',
                '        </NODE>', '    </OBJ_ID>']
    sgx += ['    <PARTITION_ID no="0">',
            '        <AABBOX min="%s %s %s" max="%s %s %s" />' % tuple(f6(x) for x in list(allmin) + list(allmax)),
            '        <CHILD_PARTITIONS IDs="NONE" />',
            '        <CHILD_OBJS IDs="%s " />' % ' '.join(str(i) for i in range(1, len(sgx_nodes) + 1)),
            '    </PARTITION_ID>', '</SCENE>']
    main_items.append(dict(name=TD + tid + '.sgx', data=('\n'.join(sgx) + '\n').encode()))

    SX = os.path.join(sx, 'tracks', 'sochi_ams')
    main_items.append(dict(name=TD + tid + '.enx', data=open(os.path.join(SX, 'sochi_ams.enx'), 'rb').read()))
    main_items.append(dict(name=TD + 'trackproperties.bin', data=open(os.path.join(SX, 'trackproperties.bin'), 'rb').read()))
    main_items.append(dict(name='cameras' + BS + 'cameraconfig.xml', data=open(os.path.join(sx, 'cameras', 'cameraconfig.xml'), 'rb').read()))

    aextra = extra
    if extra is not None:                           # AI width = asphalt + kerbs (outer white line), not run-off
        te = tarmac_edges(k, fast, keys=keys)                    # asphalt + kerbs (collision strip)
        ta = tarmac_edges(k, fast, kerbs=False, keys=keys)       # asphalt only: the white lines
        aextra = extra.copy(); wide = extra.copy()
        for c in (0, 1):
            ok = te[:, c] > 1.0
            wide[ok, 5 + c] = np.minimum(extra[ok, 5 + c], te[ok, c] + 0.3)
            ok = ta[:, c] > 1.0
            aextra[ok, 5 + c] = np.minimum(extra[ok, 5 + c], ta[ok, c] + 0.2)
    else:
        wide = None
    wallf = wall_free(k, fast, keys=keys) if extra is not None else None
    aiw, info = build_aiw(fast, aextra, dummies, tid, pitc, wide, wallf)
    if args.dump_aiw: open(args.dump_aiw, 'w', encoding='latin1', newline='').write(aiw)
    print('pit branch waypoints', info['npit'])
    camtpl = open(os.path.join(sx, 'cameras', 'sochi_ams.xml'), encoding='utf-8').read()
    cini = locations['cameras']
    if os.path.exists(cini) and read_ac_cameras(cini):
        camx, ncam, narea = build_cameras(camtpl, read_ac_cameras(cini), fast, info['camera_centres'], info['camera_half'], tid)
        print('cameras', ncam, 'areas', narea)
        main_items.append(dict(name='cameras' + BS + tid + '.xml', data=camx.encode('utf-8')))
    else:
        main_items.append(dict(name='cameras' + BS + tid + '.xml', data=camtpl.encode('utf-8')))
    print('aiw lap %.1f m, sectors %.1f / %.1f, %d waypoints, est lap %.1f s, %d corners' % (info['lap'], info['s1'], info['s2'], info['nw'], info['tdry'], info['ncorners']))
    trd = build_trd(open(os.path.join(SX, 'sochi_ams.trd'), encoding='utf-8').read(), tid, info['lap'],
                    meta, info['starts'], info['pits']).encode('utf-8')
    main_items.append(dict(name=TD + tid + '.trd', data=trd))
    loose[tid + '.trd'] = trd

    pak = os.path.join(root, 'Pakfiles', 'Tracks'); os.makedirs(pak, exist_ok=True)
    print('main pak bytes', write_bff(os.path.join(pak, tid + '.bff'), tid, main_items))

    csm, ncsm, cst = build_csm(k, locations['surfaces'])
    print('csm', ncsm, 'meshes', len(csm), 'bytes', dict(cst))
    plt = [m['pos'][m['idx'].reshape(-1, 3)] for m in k['meshes']
           if surface_kind(match_surface(m['name'], keys)) in ('PIT', 'PITLANE') and len(m['idx'])]
    sf_ac = (dummies['AC_TIME_0_L'][3, :3] + dummies['AC_TIME_0_R'][3, :3]) * 0.5
    limiter_zone = pit_light_zone(pitc, k['meshes']) if pitc is not None and not plt else None
    gcl, ngcl, zone = build_gcl(fast, extra, sf_ac, pitc, np.vstack(plt) if plt else None, limiter_zone=limiter_zone)
    if args.require_complete_ai and zone is None:
        raise ValueError('Complete AI requires a limiter zone from PITLANE geometry or explicit pitlightin/out anchors')
    print('gcl', ngcl, 'tris', len(gcl), 'bytes, pit limiter zone (pit pts)', zone)
    trig = build_triggers(open(os.path.join(SX, 'physics', 'triggers.xml'), encoding='utf-8').read(), dummies)
    phys = [dict(name='tracks' + BS + '_data' + BS + 'aiw' + BS + tid + '.aiw', data=aiw.encode('latin1')),
            dict(name=TD + 'physics' + BS + 'object_properties.xml', data=open(os.path.join(SX, 'physics', 'object_properties.xml'), 'rb').read()),
            dict(name=TD + 'physics' + BS + tid + '.csm', data=csm),
            dict(name=TD + 'physics' + BS + 'triggers.xml', data=trig.encode('utf-8')),
            dict(name=TD + 'track_cut' + BS + tid + '.gcl', data=gcl)]
    write_bff(os.path.join(pak, tid + '_Physics.bff'), tid + '_Physics', phys)
    for season in ('AUT', 'SNO', 'SPR', 'SUM', 'WIN'):
        b = Bff(os.path.join(sref, 'Pakfiles', 'Tracks', '%s_sochi_ams.bff' % season))
        write_bff(os.path.join(pak, '%s_%s.bff' % (season, tid)), '%s_%s' % (season, tid),
                  [dict(name=e.name, data=b.read(e)) for e in b.entries])

    tdir = os.path.join(root, 'Tracks', tid); os.makedirs(tdir, exist_ok=True)
    for n, d in loose.items():
        open(os.path.join(tdir, n), 'wb').write(d)
    ui = locations['ui']
    for sub, png, size in (('tracklogos', 'outline.png', (512, 512)), ('trackmaps', 'outline.png', (1000, 1000)),
                           ('trackmaps3d', 'outline.png', (1000, 1000)), ('trackphotos', 'preview.png', (1920, 1080))):
        g = os.path.join(root, 'GUI', sub); os.makedirs(g, exist_ok=True)
        open(os.path.join(g, tid + '.dds'), 'wb').write(gui_dds(os.path.join(ui, png), size))
    open(os.path.join(out, tid, 'debug_aiw.txt'), 'w').write(aiw[:4000])
    print('done ->', root)


if __name__ == '__main__':
    main()

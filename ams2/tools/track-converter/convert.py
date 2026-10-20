"""Sketch converter: Assetto Corsa track (kn5 + ai/fast_lane.ai) -> AMS2 (Madness) mod folder for AMS2CM.
usage: python -I convert.py <ac_track_dir> <track_id> <out_dir> <sochi_ref_dir> <sochi_extract_dir> [--max-meshes N]
  ac_track_dir      ...\\bahrain   (bahrain.kn5, ai\\fast_lane.ai, data\\, ui\\)
  sochi_ref_dir     ...\\sochi\\Sochi Autodrom\\Automobilista 2   (templates: trd, mtx, seasonal paks)
  sochi_extract_dir ...\\x_sochi   (extracted Sochi paks: enx, cameras, trackproperties.bin, physics templates)
Writes <out_dir>\\<track_id>\\Automobilista 2\\{Pakfiles\\Tracks, Tracks\\<id>, GUI\\...}.  Nothing is installed.
See SPEC.md for what is verified and what is a hypothesis (coordinate mirror, AIW fields, triggers, CSM).
"""
import collections, math, os, re, struct, sys
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
    return RZ_BASIC.format(name=m['mname'], cull='EBFCT_NONE' if (alpha_test or alpha_blend) else 'EBFCT_ANTICLOCKWISE',
                           spec=repr(float(np.float32(spec))), tex=tex, zwrite='false' if alpha_blend else 'true',
                           blend=BLEND_ON if alpha_blend else BLEND_OFF,
                           defines='  <define name="USE_ALPHATEST" />\n' if alpha_test else '')


# AC overlay layers that render as a wide dark band on the road in AMS2 (rubber groove / skid decals, black alpha textures);
# the game has its own rubbering.  ksMultilayer road (stretched 1024x4096 lane texture) gets the plain tarmac tile instead.
DROP_MATS = {'groove', 'skidmark_01', 'skidmark_02', 'bah_skid_decals_a'}
TARMAC_TILE = 'tarmac_diff_a_07_d.dds'
UV_MULTILAYER = (1.5, 3.5)


def tex_of(mt):
    if mt['shader'] == 'ksMultilayer':
        return TARMAC_TILE
    return mt['tex'].get('txDiffuse') or next(iter(mt['tex'].values()), None)


# ---------------------------------------------------------------- AI line
def read_ai(path):
    d = open(path, 'rb').read()
    ver, n, _, _ = struct.unpack_from('<4i', d, 0)
    pts = np.array([struct.unpack_from('<4f', d, 16 + 20 * i) for i in range(n)])
    o = 16 + 20 * n
    ec = struct.unpack_from('<i', d, o)[0]
    extra = np.frombuffer(d, '<f4', ec * 18, o + 4).reshape(ec, 18) if ec == n else None
    return pts[:, :3], extra


def yaw_of(fwd):
    """Madness Ori yaw: car faces (-sin y, 0, -cos y)  (derived from Sochi grid vs AI direction)."""
    return math.atan2(-fwd[0], -fwd[2])


def build_aiw(fast, extra, dummies, tid, pit=None):
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
    ex_l = smooth(np.clip(np.interp(s, cum, np.r_[left, left[:1]]), 3.0, EDGE_CAP))    # AC +lat side = AMS -perp side
    ex_r = smooth(np.clip(np.interp(s, cum, np.r_[right, right[:1]]), 3.0, EDGE_CAP))  # AC -lat side = AMS +perp side

    def lapdist(name):
        c = (dummies[name + '_L'][3, :3] + dummies[name + '_R'][3, :3]) * 0.5 * MIRROR
        return float(s[int(np.argmin(np.linalg.norm(W - c, axis=1)))])
    s1, s2 = lapdist('AC_TIME_1'), lapdist('AC_TIME_2')

    starts = sorted((int(n.split('_')[2]), M) for n, M in dummies.items() if re.fullmatch(r'AC_START_\d+', n))
    pits = sorted((int(n.split('_')[2]), M) for n, M in dummies.items() if re.fullmatch(r'AC_PIT_\d+', n))

    def pos_ori(M):
        p = M[3, :3] * MIRROR; f = M[2, :3] * MIRROR
        return p, yaw_of(f)
    out = ['[Features]', 'waypointspan=%.2f' % (lap / nw), 'pitlanes=%d' % (pit is not None), 'startinggrid=%d' % len(starts),
           'pitspots=%d' % len(pits), 'garagespots=2', 'clippingpoints=0', 'driftversion=0', 'cornermarkerversion=1',
           'AI Late Braking Fraction=1.000000', 'Track Difficulty=1.000000', 'Oval=0', 'RallyCross=0', 'IceTrack=0',
           'IceTrackSolo=0', 'IsNarrowTrack=0', 'AnticipationDistMin=40.000000', 'AnticipationDistOffRoad=80.000000',
           'AnticipationDistWall=160.000000', 'Race Start Disabled=0.000000', 'AI Setup Gearing=0.500000',
           'AI Setup Downforce=0.500000', 'AI Setup Balance=0.500000', '', '[GRID]']
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
    out += ['', '', '', '[Waypoint]', 'trackstate=467', 'times=(110.0000,115.0000)', 'number_waypoints=%d' % nw,
            'lap_length=%.6f' % lap, 'sector_1_length=%.6f' % s1, 'sector_2_length=%.6f' % s2, 'FuelUse=0.845000',
            'GrooveWidth=7.500000', 'GrooveWidthWet=3.000000', 'IntermediateFogLevel=(0.0000)',
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
        wps.append(dict(pos=W[i] + shift * pp, perp=pp, width=(hw, hw),
                        dwidth=(hw, hw), path=-shift, galpha=0.0, score=(sector(s[i]), s[i]), branch=0, bits=0,
                        ptrs=((i - 1) % nw, (i + 1) % nw, third.get(i, -1), 0)))
    wps += pitw
    out = [x.replace('number_waypoints=%d' % nw, 'number_waypoints=%d' % len(wps)) for x in out]
    for i, w in enumerate(wps):
        out += [BS + BS + str(i),
                'wp_pos=(%.4f,%.4f,%.4f)' % tuple(w['pos']), 'wp_perp=(%.4f,0.0000,%.4f)' % (w['perp'][0], w['perp'][2]),
                'wp_width=(%.3f,%.3f)' % w['width'], 'wp_dwidth=(%.3f,%.3f)' % w['dwidth'],
                'wp_path=(%.4f,0.0000)' % w['path'], 'wp_galpha=(%.4f)' % w['galpha'], 'wp_score=(%d,%.3f)' % w['score'],
                'wp_groove_lat=(0.000000)', 'wp_event=(1.000,0,0.000000)', 'wp_branchID=(%d)' % w['branch'],
                'wp_bitfields=(%d)' % w['bits'], 'wpd_CornerType=(0)', 'wpd_CornerState=(0)', 'WP_PTRS=(%d,%d,%d,%d)' % w['ptrs']]
    info = dict(lap=lap, s1=s1, s2=s2, nw=nw, W=W, half=half, npit=len(pitw) - (len(pits) if pit is not None else 0))
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
    outside = (lat > fextra[j, 6] + 0.5) | (-lat > fextra[j, 5] + 0.5)
    return d, outside


def build_pit(fast, fextra, pitlane, pit_dummies, span=5.0, merge=4.0):
    from scipy.spatial import cKDTree
    d, outside = pit_section(fast, fextra, pitlane)
    stalls = np.array([M[3, :3] for _, M in pit_dummies])
    c = int(np.median(cKDTree(pitlane).query(stalls)[1]))
    if not outside[c]:
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
# AC surface KEY -> AMS2 csm surface id.  Ids taken from Sochi (OMTT) correlated with its render meshes [H]:
# 0 asphalt/pit, 7 grass, 10 rumble/kerb, 12 tyre wall, 13 concrete wall, 14 armco.  No gravel id known -> sand = 7.
SURF_ID = {'TARMAC': 0, 'PITLANE': 0, 'KERB': 10, 'CARPET': 7, 'OFFTRACK': 7, 'SAND': 7, 'GRASS': 7, 'WALL': 13}


def surface_keys(ini_path):
    keys = set()
    if os.path.exists(ini_path):
        for line in open(ini_path, encoding='latin1'):
            mm = re.match(r'\s*KEY\s*=\s*(\w+)', line)
            if mm:
                keys.add(mm.group(1).upper())
    return keys | {'WALL'}                 # WALL is an AC built-in surface


def build_csm(k, ini_path):
    from csm_write import write_csm
    keys = surface_keys(ini_path)
    items = []; stats = collections.Counter()
    for m in k['meshes']:
        g = re.match(r'^\d+([A-Za-z]+)', m['name'])
        if not g or g.group(1).upper() not in keys or not len(m['idx']):
            continue
        key = g.group(1).upper(); low = m['name'].lower()
        sid = SURF_ID.get(key, 0)
        if key == 'WALL':
            sid = 12 if 'tyre' in low or 'tire' in low else 14 if ('armco' in low or 'rail' in low or 'barrier' in low) else 13
        P = (m['pos'] * MIRROR).astype(np.float32)
        T = m['idx'].reshape(-1, 3)[:, [0, 2, 1]]                  # mirrored -> reverse winding (same as render)
        U, inv = np.unique(P, axis=0, return_inverse=True)          # weld split (uv-seam) vertices
        T = inv.reshape(-1)[T]
        items.append((U, T, sid)); stats[(key, sid)] += len(T)
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


def build_gcl(fast, fextra, sf, pitc, pitlane_tris, span=5.0):
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
            a = int(np.argmax(on)); b = len(on) - 1 - int(np.argmax(on[::-1]))
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
            zone = (a, b)
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
def build_trd(template, tid, lap):
    vals = {'Name': tid, 'TrackName': 'Bahrain International Circuit (AC conv)', 'ShortTrackName': 'Bahrain',
            'ScenegraphFile': tid + '.sgx', 'Year': '2004', 'Length': str(int(round(lap))), 'Location': 'Bahrain',
            'Country': 'BH', 'Track Group': 'Bahrain (AC conversion test)',
            'Track Description': 'Bahrain - AC kn5 conversion test', 'Is Clockwise': 'true',
            'Track_Location': 'Sakhir', 'Track_Variation': 'Grand Prix', 'Track_Longitude': '50.512',
            'Track_Latitude': '26.032', 'Track_Altitude': '7', 'Track_TimeZone': '3', 'Number Of Turns': '15',
            'Track_Climate': 'desert', 'Max AI participants': '23', 'Drs Zone 1 Detection Number': '0',
            'Drs Zone 2 Detection Number': '0', 'Order Override': '1'}
    for k, v in vals.items():
        template, n = re.subn(r'(name="%s" data=")[^"]*(")' % re.escape(k), lambda m: m.group(1) + v + m.group(2), template)
        assert n == 1, k
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


def main():
    src, tid, out, sref, sx = sys.argv[1:6]
    maxm = int(sys.argv[sys.argv.index('--max-meshes') + 1]) if '--max-meshes' in sys.argv else None
    root = os.path.join(out, tid, 'Automobilista 2')
    kn5_path = next(os.path.join(src, f) for f in os.listdir(src) if f.lower().endswith('.kn5'))
    k = read_kn5(kn5_path)
    dummies = {n: M for n, M in k['dummies']}
    print('kn5', len(k['meshes']), 'meshes', len(k['materials']), 'materials')

    main_items, loose = [], {}
    TD = 'tracks' + BS + tid + BS
    # materials (only those used by exported meshes)
    meshes = [m for m in k['meshes'] if m['visible'] and m['renderable'] and not m['name'].lower().startswith('ac_')
              and len(m['idx']) and k['materials'][m['mat']]['name'].lower() not in DROP_MATS]
    if maxm:
        meshes.sort(key=lambda m: -len(m['idx'])); meshes = meshes[:maxm]
    used = sorted({m['mat'] for m in meshes})
    names = set()
    for i in used:
        mt = k['materials'][i]
        base = san(mt['name']).upper(); nm = base; j = 1
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
    texs = {}
    for i in used:
        mt = k['materials'][i]
        tex = tex_of(mt)
        if tex and tex in k['textures']:
            texs[mt['texname']] = k['textures'][tex]
    for tn, data in sorted(texs.items()):
        main_items.append(dict(name='tracks' + BS + 'textures' + BS + tid + BS + tn, data=data))
    print('materials', len(used), 'textures', len(texs))

    # meshes
    sgx_nodes = []; mnames = set(); allmin = np.full(3, 1e30); allmax = np.full(3, -1e30)
    for mi, m in enumerate(meshes):
        base = san(m['name']); nm = base; j = 1
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
        uv = m['uv'] * np.array(UV_MULTILAYER) if k['materials'][m['mat']]['shader'] == 'ksMultilayer' else m['uv']
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

    fast, extra = read_ai(os.path.join(src, 'ai', 'fast_lane.ai'))
    pl = os.path.join(src, 'ai', 'pit_lane.ai')
    pitc = None
    if os.path.exists(pl) and extra is not None:
        pitc = build_pit(fast, extra, read_ai(pl)[0],
                         sorted((n, M) for n, M in dummies.items() if re.fullmatch(r'AC_PIT_\d+', n)))
    aiw, info = build_aiw(fast, extra, dummies, tid, pitc)
    print('pit branch waypoints', info['npit'])
    camtpl = open(os.path.join(sx, 'cameras', 'sochi_ams.xml'), encoding='utf-8').read()
    cini = os.path.join(src, 'data', 'cameras.ini')
    if os.path.exists(cini) and read_ac_cameras(cini):
        camx, ncam, narea = build_cameras(camtpl, read_ac_cameras(cini), fast, info['W'], info['half'], tid)
        print('cameras', ncam, 'areas', narea)
        main_items.append(dict(name='cameras' + BS + tid + '.xml', data=camx.encode('utf-8')))
    else:
        main_items.append(dict(name='cameras' + BS + tid + '.xml', data=camtpl.encode('utf-8')))
    print('aiw lap %.1f m, sectors %.1f / %.1f, %d waypoints' % (info['lap'], info['s1'], info['s2'], info['nw']))
    trd = build_trd(open(os.path.join(SX, 'sochi_ams.trd'), encoding='utf-8').read(), tid, info['lap']).encode('utf-8')
    main_items.append(dict(name=TD + tid + '.trd', data=trd))
    loose[tid + '.trd'] = trd

    pak = os.path.join(root, 'Pakfiles', 'Tracks'); os.makedirs(pak, exist_ok=True)
    print('main pak bytes', write_bff(os.path.join(pak, tid + '.bff'), tid, main_items))

    csm, ncsm, cst = build_csm(k, os.path.join(src, 'data', 'surfaces.ini'))
    print('csm', ncsm, 'meshes', len(csm), 'bytes', dict(cst))
    plt = [m['pos'][m['idx'].reshape(-1, 3)] for m in k['meshes'] if re.match(r'^\d+pitlane', m['name'], re.I) and len(m['idx'])]
    sf_ac = (dummies['AC_TIME_0_L'][3, :3] + dummies['AC_TIME_0_R'][3, :3]) * 0.5
    gcl, ngcl, zone = build_gcl(fast, extra, sf_ac, pitc, np.vstack(plt) if plt else None)
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
    ui = os.path.join(src, 'ui')
    for sub, png, size in (('tracklogos', 'outline.png', (512, 512)), ('trackmaps', 'outline.png', (1000, 1000)),
                           ('trackmaps3d', 'outline.png', (1000, 1000)), ('trackphotos', 'preview.png', (1920, 1080))):
        g = os.path.join(root, 'GUI', sub); os.makedirs(g, exist_ok=True)
        open(os.path.join(g, tid + '.dds'), 'wb').write(gui_dds(os.path.join(ui, png), size))
    open(os.path.join(out, tid, 'debug_aiw.txt'), 'w').write(aiw[:4000])
    print('done ->', root)


if __name__ == '__main__':
    main()

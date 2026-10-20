"""Per-waypoint kerb / bump analysis of the AC collision meshes, used to tune AMS2 AI widths (low kerbs usable, tall kerbs and rough ground avoided)."""
import re
import numpy as np
from scipy.spatial import cKDTree


def _pts(k, pred):
    out = []
    for m in k['meshes']:
        if not pred(m, k['materials'][m['mat']]['name']):
            continue
        P = m['pos']; T = m['idx'].reshape(-1, 3)
        out.append(P[T].mean(1)); out.append(P)
        for a, b in ((0, 1), (1, 2), (2, 0)):
            out.append((P[T[:, a]] + P[T[:, b]]) * 0.5)
    return np.vstack(out) if out else np.zeros((0, 3))


def kerb_profile(k, fast, reach=14.0, step=0.25):
    """fast: AC-coordinate racing line (n,3).  Returns dict per side (0 = AC +perp (col 5), 1 = -perp): asphalt edge distance,
    kerb outer distance and kerb step height (m) above the adjacent asphalt, all arrays of len(n)."""
    kerb_re = re.compile(r'^\d+kerb|vis-kerb|rumble|sausage', re.I)
    road_re = re.compile(r'^\d+(tarmac|road|pit)', re.I)
    kp = _pts(k, lambda m, mt: bool(kerb_re.search(m['name'])) and not m['name'].lower().startswith('vis-whiteline') and not m['name'].lower().startswith('vis-'))
    kv = _pts(k, lambda m, mt: m['name'].lower().startswith('vis-kerb') or 'rumble' in mt.lower())
    rp = _pts(k, lambda m, mt: bool(road_re.match(m['name'])))
    if len(kp) == 0:
        kp = kv
    n = len(fast)
    res = {c: dict(edge=np.zeros(n), kout=np.zeros(n), kh=np.zeros(n)) for c in (0, 1)}
    if len(rp) == 0:
        return res
    tr = cKDTree(rp[:, [0, 2]]); tk = cKDTree(kp[:, [0, 2]]) if len(kp) else None
    offs = np.arange(step, reach, step)
    for i in range(n):
        t = fast[(i + 1) % n] - fast[i - 1]; t[1] = 0; t /= (np.linalg.norm(t) or 1)
        perp = np.array([t[2], 0.0, -t[0]])
        for c, sg in enumerate((1, -1)):
            q = fast[i][None, [0, 2]] + sg * offs[:, None] * perp[None, [0, 2]]
            dr, ir = tr.query(q)
            onr = dr < 0.45
            if not onr[0]:
                continue
            edge = offs[np.argmin(onr)] if not onr.all() else reach
            res[c]['edge'][i] = edge
            if tk is None:
                continue
            dk, ik = tk.query(q)
            onk = (dk < 0.45) & ~onr & (offs >= edge - 0.5)
            if not onk.any():
                continue
            j = np.where(onk)[0]
            # contiguous kerb run starting near the edge
            run = [j[0]]
            for a in j[1:]:
                if offs[a] - offs[run[-1]] <= 0.6: run.append(a)
                else: break
            if offs[run[0]] - edge > 0.8:
                continue
            zr = rp[ir[max(np.argmin(onr) - 1, 0)], 1] if not onr.all() else rp[ir[-1], 1]
            res[c]['kout'][i] = offs[run[-1]]
            res[c]['kh'][i] = float(np.max(kp[ik[run], 1]) - zr)
    return res


def bumpiness(fast, win=10.0):
    """Vertical roughness of the racing line: std of the 2nd difference of height, per ~5 m point (m)."""
    z = fast[:, 1]
    d = np.diff(np.r_[z, z[:1]]); dd = np.diff(np.r_[d, d[:1]])
    return np.abs(dd)

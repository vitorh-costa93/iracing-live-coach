"""Ajusta contornos desenhados (retas + cúbicas) à borda real do original, preservando a estrutura."""
import numpy as np, cv2
from scipy import ndimage as ndi
from scipy.ndimage import map_coordinates, gaussian_filter1d


class Field:
    def __init__(self, rl, UP, x0, y0):
        img = cv2.GaussianBlur(rl.astype(np.float32), (0, 0), UP * 0.2)
        m = img > 0.5
        self.sdf = (ndi.distance_transform_edt(m) - ndi.distance_transform_edt(~m)) / UP
        gy, gx = np.gradient(self.sdf)
        self.gx, self.gy = gx, gy
        self.UP, self.x0, self.y0 = UP, x0, y0

    def at(self, pts):
        pts = np.asarray(pts, float)
        r = (self.y0 + pts[:, 1]) * self.UP
        c = (self.x0 + pts[:, 0]) * self.UP
        co = np.vstack([r, c])
        s = map_coordinates(self.sdf, co, order=1, mode='nearest')
        gx = map_coordinates(self.gx, co, order=1, mode='nearest')
        gy = map_coordinates(self.gy, co, order=1, mode='nearest')
        g = np.stack([gx, gy], 1)
        n = np.hypot(g[:, 0], g[:, 1])
        g = g / np.maximum(n, 1e-9)[:, None]
        return s, g                     # s>0 dentro; g aponta para dentro (x,y)


def bez(P, t):
    t = np.asarray(t)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


def bez_d(P, t):
    t = np.asarray(t)[:, None]
    mt = 1 - t
    return 3 * mt**2 * (P[1] - P[0]) + 6 * mt * t * (P[2] - P[1]) + 3 * t**2 * (P[3] - P[2])


def unit(v):
    n = np.hypot(v[..., 0], v[..., 1])
    return v / np.maximum(n, 1e-9)[..., None]


def line_inter(p, d, q, e):
    A = np.array([d, -e]).T
    if abs(np.linalg.det(A)) < 0.17:
        return None
    t = np.linalg.solve(A, q - p)
    return p + t[0] * d


def fit_contour(segs, F, clip=1.0, trim=0.45):
    m = len(segs)
    new_start = [None] * m          # ponto inicial deslocado de cada segmento
    new_end = [None] * m
    lines = [None] * m              # (ponto, direção) da reta deslocada, para L
    cinfo = [None] * m
    for i, s in enumerate(segs):
        if s[0] == 'L':
            p0, p1 = np.array(s[1], float), np.array(s[2], float)
            L = np.hypot(*(p1 - p0))
            d = (p1 - p0) / max(L, 1e-9)
            if L < 2 * trim + 0.4:
                new_start[i], new_end[i] = p0.copy(), p1.copy()
                continue
            t = np.linspace(trim / L, 1 - trim / L, max(6, int(L / 0.2)))
            pts = p0 + np.outer(t, p1 - p0)
            sd, g = F.at(pts)
            nO = np.array([d[1], -d[0]])
            if (g @ nO).mean() > 0:
                nO = -nO                   # nO aponta para FORA
            ok = np.abs(sd) < 1.5
            if ok.sum() < 3:
                new_start[i], new_end[i] = p0.copy(), p1.copy()
                continue
            A = np.stack([np.ones(ok.sum()), t[ok] - 0.5], 1)
            coef, *_ = np.linalg.lstsq(A, sd[ok], rcond=None)
            a, b = np.clip(coef, -clip, clip)
            new_start[i] = p0 + nO * (a - 0.5 * b)
            new_end[i] = p1 + nO * (a + 0.5 * b)
            lines[i] = (new_start[i], unit(new_end[i] - new_start[i]))
        else:
            P = np.array(s[1:], float)
            t = np.linspace(0, 1, 41)
            pts = bez(P, t)
            sd, g = F.at(pts)
            tg = unit(bez_d(P, t))
            nO = np.stack([tg[:, 1], -tg[:, 0]], 1)
            flip = (nO * g).sum(1) > 0
            nO[flip] *= -1
            raw = np.clip(sd, -clip, clip)
            deg = 2 if np.hypot(*(P[3] - P[0])) > 4.0 else 1
            w = (np.abs(sd) < 1.5).astype(float) + 1e-3
            dsp = np.polyval(np.polyfit(t, raw, deg, w=w), t)
            cinfo[i] = (P, t, nO, dsp)
            new_start[i] = P[0] + nO[0] * dsp[0]
            new_end[i] = P[3] + nO[-1] * dsp[-1]
    # vértices
    verts = [None] * m
    for k in range(m):
        a, b = (k - 1) % m, k                      # segmento anterior e seguinte
        la, lb = lines[a], lines[b]
        if la is not None and lb is not None:
            x = line_inter(la[0], la[1], lb[0], lb[1])
            base = segs[b][1] if segs[b][0] == 'L' else segs[b][1]
            if x is not None and np.hypot(*(x - np.array(base))) < 1.5:
                verts[k] = x
                continue
        if la is not None:
            verts[k] = new_end[a]
        elif lb is not None:
            verts[k] = new_start[b]
        else:
            verts[k] = (new_end[a] + new_start[b]) / 2
    out = []
    for i, s in enumerate(segs):
        v0, v1 = verts[i], verts[(i + 1) % m]
        if s[0] == 'L':
            out.append(('L', v0, v1))
        else:
            P, t, nO, dsp = cinfo[i]
            T = bez(P, t) + nO * dsp[:, None]
            base = (1 - t)[:, None]**3 * v0 + t[:, None]**3 * v1
            R = T - base
            A1 = 3 * (1 - t)**2 * t
            A2 = 3 * (1 - t) * t**2
            M = np.array([[A1 @ A1, A1 @ A2], [A1 @ A2, A2 @ A2]])
            c = np.linalg.solve(M + 1e-9 * np.eye(2), np.array([[A1 @ R[:, 0], A1 @ R[:, 1]], [A2 @ R[:, 0], A2 @ R[:, 1]]]))
            out.append(('C', v0, c[0], c[1], v1))
    return out


def fit_glyph(contours, F, **kw):
    return [fit_contour(c, F, **kw) for c in contours]

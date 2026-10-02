"""Otimização por síntese: ajusta os pontos de um contorno desenhado para que a renderização (cobertura + desfoque)
case com os pixels reais da captura."""
import numpy as np
import cv2

SS = 12


def flat_pts(seg, n=24):
    if seg[0] == 'L':
        return np.array([seg[1]])
    P = np.array(seg[1:])
    t = np.linspace(0, 1, n, endpoint=False)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


class Model:
    """Contornos como vértices + pontos de controle; renderiza em uma grade th x tw com offset global."""

    def __init__(self, contours):
        self.kinds = []          # por contorno: lista de 'L'/'C'
        params = []
        self.layout = []         # por contorno: (índice dos vértices, índice dos controles por segmento)
        for c in contours:
            kinds = [s[0] for s in c]
            vi = []
            ci = []
            for s in c:
                vi.append(len(params))
                params.append(np.array(s[1], float))
                if s[0] == 'C':
                    ci.append((len(params), len(params) + 1))
                    params.append(np.array(s[2], float))
                    params.append(np.array(s[3], float))
                else:
                    ci.append(None)
            self.kinds.append(kinds)
            self.layout.append((vi, ci))
        self.theta0 = np.array(params, float)          # (n,2)
        self.n = len(params)

    def contours(self, theta):
        out = []
        for kinds, (vi, ci) in zip(self.kinds, self.layout):
            m = len(kinds)
            segs = []
            for i in range(m):
                p0 = theta[vi[i]]
                p1 = theta[vi[(i + 1) % m]]
                if kinds[i] == 'L':
                    segs.append(('L', p0.copy(), p1.copy()))
                else:
                    a, b = ci[i]
                    segs.append(('C', p0.copy(), theta[a].copy(), theta[b].copy(), p1.copy()))
            out.append(segs)
        return out

    def render(self, theta, origin, shape):
        """origin = (ox, oy) deslocamento do glifo na grade (px). Retorna cobertura (shape) em [0,1]."""
        h, w = shape
        mask = np.zeros((h * SS, w * SS), np.uint8)
        for c in self.contours(theta):
            pts = np.vstack([flat_pts(s) for s in c])
            q = np.round(np.stack([(origin[0] + pts[:, 0]) * SS, (origin[1] + pts[:, 1]) * SS], 1)).astype(np.int32)
            layer = np.zeros_like(mask)
            cv2.fillPoly(layer, [q.reshape(-1, 1, 2)], 1)
            mask ^= layer
        return mask.reshape(h, SS, w, SS).mean((1, 3)).astype(np.float32)


def predict(cov, sigma):
    return cv2.GaussianBlur(cov, (0, 0), sigma) if sigma > 0 else cov


def optimize(model, obs, win, origin0, sigma=0.4, lam=0.6, steps=(0.5, 0.25, 0.12, 0.06), sweeps=3, seed=0):
    """obs: tinta observada (h,w) em [0,1]; win=(y0,y1,x0,x1) região da perda; origin0: deslocamento inicial (x,y)."""
    rng = np.random.default_rng(seed)
    y0, y1, x0, x1 = win
    theta = model.theta0.copy()
    origin = np.array(origin0, float)

    def energy(th, org):
        pred = predict(model.render(th, org, obs.shape), sigma)
        data = np.abs(pred - obs)[y0:y1, x0:x1].sum()
        reg = lam * ((th - model.theta0) ** 2).sum()
        return data + reg, data

    best, data0 = energy(theta, origin)
    for st in steps:
        for _ in range(sweeps):
            order = rng.permutation(model.n * 2 + 2)
            for k in order:
                for sgn in (+1, -1):
                    th = theta
                    org = origin
                    if k < model.n * 2:
                        th = theta.copy()
                        th[k // 2, k % 2] += sgn * st
                    else:
                        org = origin.copy()
                        org[k - model.n * 2] += sgn * st * 0.5
                    e, _ = energy(th, org)
                    if e < best - 1e-6:
                        best, theta, origin = e, th, org
                        break
    return theta, origin, data0, energy(theta, origin)[1]


def optimize_multi(model, obs_list, origin0_list, sigma=0.42, lam=2.0, steps=(0.5, 0.25, 0.12, 0.06), sweeps=3, seed=0, margin=2):
    """Várias amostras do mesmo glifo, cada uma com a sua origem; os pontos são compartilhados."""
    rng = np.random.default_rng(seed)
    theta = model.theta0.copy()
    origins = [np.array(o, float) for o in origin0_list]
    K = len(obs_list)

    def data_term(th, orgs, only=None):
        tot = 0.0
        for i in (range(K) if only is None else [only]):
            o = obs_list[i]
            pred = predict(model.render(th, orgs[i], o.shape), sigma)
            tot += np.abs(pred - o)[margin:-margin, margin:-margin].sum()
        return tot

    def energy(th, orgs):
        return data_term(th, orgs) / K + lam * ((th - model.theta0) ** 2).sum()

    best = energy(theta, origins)
    d0 = data_term(theta, origins) / K
    # 1) alinhamento de cada amostra (só a origem)
    for i in range(K):
        for st in (0.5, 0.25, 0.12):
            for _ in range(3):
                for ax in (0, 1):
                    for sgn in (+1, -1):
                        org = origins[i].copy()
                        org[ax] += sgn * st
                        old = data_term(theta, origins, i)
                        origins[i], keep = org, origins[i]
                        if data_term(theta, origins, i) < old - 1e-6:
                            break
                        origins[i] = keep
    best = energy(theta, origins)
    for st in steps:
        for _ in range(sweeps):
            for k in rng.permutation(model.n * 2):
                for sgn in (+1, -1):
                    th = theta.copy()
                    th[k // 2, k % 2] += sgn * st
                    e = energy(th, origins)
                    if e < best - 1e-6:
                        best, theta = e, th
                        break
    return theta, origins, d0, data_term(theta, origins) / K

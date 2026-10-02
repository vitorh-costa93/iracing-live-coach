"""Dígitos das caixas (Futura Condensed Extra Bold, 1998-2001) desenhados à mão.
Coordenadas em px da captura, y para BAIXO, origem no canto superior esquerdo da caixa do glifo, altura 25.
Medidas extraídas de measure.py sobre a reconstrução em super-resolução."""
import numpy as np


class Path:
    def __init__(self, x, y):
        self.segs = []
        self.start = np.array([x, y], float)
        self.cur = self.start.copy()

    def L(self, x, y):
        p = np.array([x, y], float)
        if np.hypot(*(p - self.cur)) > 1e-6:
            self.segs.append(('L', self.cur.copy(), p))
            self.cur = p
        return self

    def C(self, c1, c2, p):
        p = np.array(p, float)
        self.segs.append(('C', self.cur.copy(), np.array(c1, float), np.array(c2, float), p))
        self.cur = p
        return self

    def arc(self, cx, cy, rx, ry, a0, a1):
        pt = lambda a: np.array([cx + rx * np.cos(np.radians(a)), cy + ry * np.sin(np.radians(a))])
        d = lambda a: np.array([-rx * np.sin(np.radians(a)), ry * np.cos(np.radians(a))])
        n = max(1, int(np.ceil(abs(a1 - a0) / 90.0 - 1e-9)))
        edges = np.linspace(a0, a1, n + 1)
        self.L(*pt(a0))
        for i in range(n):
            a, b = edges[i], edges[i + 1]
            t = np.radians(b - a)
            k = 4.0 / 3.0 * np.tan(t / 4.0)
            self.C(pt(a) + k * d(a), pt(b) - k * d(b), pt(b))
        return self

    def close(self):
        self.L(*self.start)
        return self.segs


def poly(pts):
    p = Path(*pts[0])
    for q in pts[1:]:
        p.L(*q)
    return p.close()


def g1():
    return [poly([(0.8, 0), (6.4, 0), (7.0, 0.6), (7.0, 24.5), (6.5, 25), (3.2, 25), (2.6, 24.5), (2.6, 4.7),
                  (2.0, 3.6), (0.7, 3.5), (0.1, 2.7), (0, 0.9)])]


def g7():
    return [poly([(0, 0.5), (0.6, 0), (13.2, 0), (13.6, 0.7), (4.8, 24.5), (4.4, 25), (0.5, 25), (0, 24.3),
                  (7.7, 3.8), (7.4, 3.5), (0.4, 3.5), (0, 3.0)])]


def g4():
    outer = poly([(7.3, 0), (10.9, 0), (11.5, 0.6), (11.5, 14.9), (12.9, 14.9), (13.5, 15.5), (13.5, 18.0),
                  (12.9, 18.3), (11.5, 18.3), (11.5, 24.4), (11.1, 25), (8.1, 25), (7.6, 24.4), (7.6, 18.3),
                  (0.5, 18.3), (0, 17.6), (0.2, 16.9)])
    counter = poly([(7.8, 5.3), (7.8, 14.6), (7.3, 15.1), (3.6, 15.1), (3.2, 14.5)])
    return [outer, counter]


def g2():
    p = Path(0.8, 25.2)
    p.L(0.2, 24.7).L(0.3, 23.8)                      # canto inferior esquerdo
    p.L(7.1, 11.0)                                   # borda esquerda da diagonal
    p.C((7.9, 9.4), (8.3, 8.0), (8.3, 6.6))          # entra no vão interno
    p.arc(6.0, 6.6, 2.3, 3.2, 0, -180)               # vão interno (sentido anti-horário na tela)
    p.L(3.7, 8.4).L(0.4, 8.4).L(0, 8.0)              # terminal esquerdo
    p.L(0, 6.1)
    p.arc(6.05, 6.1, 6.05, 6.1, 180, 360)            # arco externo sobre o topo
    p.C((12.1, 8.5), (11.6, 10.4), (10.9, 12.1))     # desce pela direita
    p.L(6.6, 20.4)                                   # borda direita da diagonal
    p.C((6.7, 21.1), (6.9, 21.4), (7.5, 21.45))
    p.L(11.4, 21.45).L(11.8, 22.0).L(11.8, 24.6).L(11.2, 25.2)
    return [p.close()]


def g3_proper():
    p = Path(0.8, 6.6)
    p.L(0.8, 6.2)
    p.arc(6.6, 6.2, 5.8, 6.2, 180, 415)
    p.L(9.9, 11.6)
    p.arc(6.8, 18.6, 6.7, 6.1, -62, 180)
    p.L(0.1, 17.9).L(3.2, 17.9)
    p.arc(6.1, 17.3, 2.5, 3.9, 168, 0)               # contorna o vão inferior por baixo
    p.arc(6.1, 17.3, 2.5, 3.9, 0, -116)              # sobe até a ponta da barra do meio
    p.L(4.9, 10.2)
    p.arc(6.3, 6.6, 2.7, 3.0, 100, -180 + 0)         # vão superior
    p.L(4.2, 6.6).L(0.8, 6.6)
    return [p.close()]


def g5():
    p = Path(3.3, 0)
    p.L(11.1, 0).L(12.1, 1.0).L(12.1, 3.2).L(11.0, 4.0).L(6.0, 4.0).L(5.4, 4.8).L(4.9, 8.0).L(5.3, 8.8)
    p.L(6.2, 8.8)
    p.arc(6.2, 16.9, 6.1, 8.1, -90, 90)
    p.C((3.9, 25.0), (0.0, 24.8), (0.0, 23.2))
    p.L(0.3, 21.5).L(0.8, 20.7).L(6.2, 20.7)
    p.arc(4.5, 16.6, 3.9, 4.2, 60, -95)
    p.L(1.3, 12.3).L(0.9, 11.9).L(2.2, 1.3).L(2.7, 0.3)
    return [p.close()]


def g6():
    outer = Path(6.3, 0)
    outer.L(9.9, 0).L(10.3, 0.5).L(5.7, 9.6).L(7.96, 9.4)
    outer.arc(6.8, 17.0, 6.7, 7.7, -80, 200)
    outer.C((0.5, 12.5), (1.0, 10.6), (1.8, 9.5))
    outer.L(6.0, 0.2)
    counter = Path(9.1, 16.9)
    counter.arc(6.8, 16.9, 2.3, 4.2, 0, -360)
    return [outer.close(), counter.close()]


def g8():
    p = Path(6.55, 0)
    p.arc(6.55, 5.2, 5.75, 5.2, -90, 56)
    p.L(9.85, 11.0)
    p.arc(6.65, 18.3, 6.65, 6.7, -62, 242)
    p.L(3.4, 11.0)
    p.arc(6.55, 5.2, 5.75, 5.2, 124, 270)
    top = Path(9.0, 6.2)
    top.arc(6.7, 6.2, 2.1, 3.1, 0, -360)
    bot = Path(9.1, 17.1)
    bot.arc(6.75, 17.1, 2.35, 3.9, 0, -360)
    return [p.close(), top.close(), bot.close()]


def g0():
    o = Path(13.4, 12.5)
    o.arc(6.7, 12.5, 6.7, 12.5, 0, 360)
    i = Path(9.0, 12.5)
    i.arc(6.7, 12.5, 2.4, 9.0, 0, -360)
    return [o.close(), i.close()]


GLYPHS = {'1': g1, '2': g2, '3': g3_proper, '4': g4, '5': g5, '6': g6, '7': g7, '8': g8, '0': g0}

"""Fonte dos valores (F1Broadcast98-Values) a partir dos contornos desenhados em glyphs_vals.py."""
import numpy as np

src = open("make_fonts_v4.py", encoding="utf-8").read().split("CAP = 700")[0]
exec(src)
import glyphs_vals as GV

CAP = 700
SB = 30
FLAT = 23.1                       # altura de cap (px) dos dígitos retos
scale = CAP / FLAT
gv = {}
raw = {}
for ch, fn in GV.GLYPHS.items():
    b = GV.BASELINE[ch]
    raw[ch] = [xform(c, lambda p, b=b: np.array([p[0], b - p[1]])) for c in fn()]   # y para cima, linha de base = 0
for ch in "012345678.":
    gv[ch] = glyph_from(raw[ch], scale, SB, base=None if False else None) if False else None
# glyph_from usa o mínimo y como base; aqui a base é a linha de base real, então calcula manualmente
for ch in "012345678.":
    allp = np.vstack([contour_poly(c) for c in raw[ch]])
    minx = allp[:, 0].min()
    fx = lambda p, minx=minx: np.array([(p[0] - minx) * scale + SB, p[1] * scale])
    cs = [xform(c, fx) for c in raw[ch]]
    gv[ch] = (cs, (allp[:, 0].max() - minx) * scale)
# 9 = 6 girado 180 graus em torno do centro vertical do glifo (linha de base 0)
c6, w6 = gv["6"]
allp6 = np.vstack([contour_poly(c) for c in c6])
cyc = (allp6[:, 1].min() + allp6[:, 1].max()) / 2
cxc = SB + w6 / 2
c9 = [xform(c, lambda p: np.array([2 * cxc - p[0], 2 * cyc - p[1]])) for c in c6]
# alinha a base do 9 à linha de base (pequeno overshoot permitido)
low = np.vstack([contour_poly(c) for c in c9])[:, 1].min()
c9 = [xform(c, lambda p, low=low: np.array([p[0], p[1] - low - 0.3 * scale])) for c in c9]
gv["9"] = (c9, w6)
dot, dot_w = gv["."]
stem = dot_w * 0.85
dot2 = [xform(c, lambda p: p + np.array([0, CAP * 0.58])) for c in dot]
gv[":"] = (dot + dot2, dot_w)
bar_w = CAP * 0.5
y0b = CAP * 0.30
a = SB + bar_w / 2 - stem / 2
b2 = a + stem
hh = bar_w / 2 - stem / 2
gv["-"] = ([lines_contour([(SB, y0b), (SB, y0b + stem), (SB + bar_w, y0b + stem), (SB + bar_w, y0b)])], bar_w)
gv["+"] = ([lines_contour([(SB, y0b), (SB, y0b + stem), (a, y0b + stem), (a, y0b + stem + hh), (b2, y0b + stem + hh),
                           (b2, y0b + stem), (SB + bar_w, y0b + stem), (SB + bar_w, y0b), (b2, y0b), (b2, y0b - hh),
                           (a, y0b - hh), (a, y0b)])], bar_w)
order = "0123456789.:+-"
build_font("F1 Broadcast 98 Values", {c: gv[c] for c in order}, CAP, SB, "../F1Broadcast98-Values.ttf")
print("ok v6")

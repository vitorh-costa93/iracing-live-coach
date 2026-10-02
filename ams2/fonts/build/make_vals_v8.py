"""F1Broadcast98-Values.ttf a partir dos contornos otimizados contra os pixels (opt_vals.pkl)."""
import pickle
import numpy as np

head = open("make_fonts_v4.py", encoding="utf-8").read().split("CAP = 700")[0]
exec(head)
import glyphs_vals as GV

CAP = 700
SBV = 30
FLAT = 23.1
scale_v = CAP / FLAT
opt = pickle.load(open("opt_vals.pkl", "rb"))
raw = {}
for ch in "012345678.":
    b = GV.BASELINE[ch]
    raw[ch] = [xform(c, lambda p, b=b: np.array([p[0], b - p[1]])) for c in opt[ch]]
gv = {}
for ch in raw:
    allp = np.vstack([contour_poly(c) for c in raw[ch]])
    minx = allp[:, 0].min()
    fx = lambda p, minx=minx: np.array([(p[0] - minx) * scale_v + SBV, p[1] * scale_v])
    gv[ch] = ([xform(c, fx) for c in raw[ch]], (allp[:, 0].max() - minx) * scale_v)
c6, w6 = gv["6"]
allp6 = np.vstack([contour_poly(c) for c in c6])
cyc = (allp6[:, 1].min() + allp6[:, 1].max()) / 2
cxc = SBV + w6 / 2
c9 = [xform(c, lambda p: np.array([2 * cxc - p[0], 2 * cyc - p[1]])) for c in c6]
low = np.vstack([contour_poly(c) for c in c9])[:, 1].min()
c9 = [xform(c, lambda p, low=low: np.array([p[0], p[1] - low - 0.3 * scale_v])) for c in c9]
gv["9"] = (c9, w6)
dot, dot_w = gv["."]
stem = dot_w * 0.85
dot2 = [xform(c, lambda p: p + np.array([0, CAP * 0.58])) for c in dot]
gv[":"] = (dot + dot2, dot_w)
bar_w = CAP * 0.5
y0b = CAP * 0.30
a = SBV + bar_w / 2 - stem / 2
b2 = a + stem
hh = bar_w / 2 - stem / 2
gv["-"] = ([lines_contour([(SBV, y0b), (SBV, y0b + stem), (SBV + bar_w, y0b + stem), (SBV + bar_w, y0b)])], bar_w)
gv["+"] = ([lines_contour([(SBV, y0b), (SBV, y0b + stem), (a, y0b + stem), (a, y0b + stem + hh), (b2, y0b + stem + hh),
                           (b2, y0b + stem), (SBV + bar_w, y0b + stem), (SBV + bar_w, y0b), (b2, y0b), (b2, y0b - hh),
                           (a, y0b - hh), (a, y0b)])], bar_w)
build_font("F1 Broadcast 98 Values", {c: gv[c] for c in "0123456789.:+-"}, CAP, SBV, "../F1Broadcast98-Values.ttf")
print("values v8 ok")

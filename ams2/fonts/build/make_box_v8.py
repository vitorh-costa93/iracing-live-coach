"""F1Broadcast98-Box.ttf a partir dos contornos otimizados contra os pixels (opt_box.pkl)."""
import pickle
import numpy as np

head = open("make_fonts_v4.py", encoding="utf-8").read().split("CAP = 700")[0]
exec(head)
import glyphs_box as GB

CAP = 700
SB = 22
opt = pickle.load(open("opt_box.pkl", "rb"))
HB = 25.0
FLAT = set("1247")
OVER = 0.3


def to_up(contours):
    return [xform(c, lambda p: np.array([p[0], -p[1]])) for c in contours]


raw = {}
for d in "12345678":
    raw[d] = to_up(opt[d][0])
raw["0"] = to_up(GB.GLYPHS["0"]())
scale = CAP / HB
gb = {}
for d in "012345678":
    allp = np.vstack([contour_poly(c) for c in raw[d]])
    minx = allp[:, 0].min()
    miny = allp[:, 1].min()
    base = miny + (0.0 if d in FLAT else OVER)
    fx = lambda p, minx=minx, base=base: np.array([(p[0] - minx) * scale + SB, (p[1] - base) * scale])
    gb[d] = ([xform(c, fx) for c in raw[d]], (allp[:, 0].max() - minx) * scale)
c6, w6 = gb["6"]
cx = SB + w6 / 2
cy = CAP / 2
gb["9"] = ([xform(c, lambda p: np.array([2 * cx - p[0], 2 * cy - p[1]])) for c in c6], w6)
build_font("F1 Broadcast 98 Box", {d: gb[d] for d in "0123456789"}, CAP, SB, "../F1Broadcast98-Box.ttf")
print("box v8 ok")

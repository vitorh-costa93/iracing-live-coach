"""Gera F1Broadcast98-Box.ttf e F1Broadcast98-Values.ttf: contornos desenhados (glyphs_*.py) ajustados à borda real do original (fitdata.py)."""
import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure

head = open("make_fonts_v4.py", encoding="utf-8").read().split("sv = {ch")[0]
exec(head)                       # utilitários, amostras dos valores (samples), CAP=700, SB=22
import glyphs_box as GB
import glyphs_vals as GV
import fitdata as FD

CAP = 700
FIT_SET = set('147')      # só as peças de retas se beneficiam do ajuste; as curvas ficam no desenho limpo


def field_for(rl):
    img = cv2.GaussianBlur(rl.astype(np.float32), (0, 0), UP * 0.25)
    cs = measure.find_contours(np.pad(img, 3), 0.5)
    allp = np.vstack([c - 3 for c in cs])
    x0 = allp[:, 1].min() / UP
    y0 = allp[:, 0].min() / UP
    return FD.Field(rl, UP, x0, y0)


# ------------------------------------------------ CAIXAS
ink = np.clip((200 - Rr) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]
SBB = 22
HB = 25.0
fitted_box = {}
for d, (xs, yy) in zip("12345678", tiles):
    sub = ink[yy[0]:yy[1], xs[0]:xs[1]]
    lab, n = ndi.label(sub > 0.5)
    sizes = ndi.sum(sub > 0.5, lab, range(1, n + 1))
    kk = int(np.argmax(sizes)) + 1
    mask = ndi.binary_dilation(lab == kk, iterations=2)
    sl = ndi.find_objects((lab == kk).astype(int))[0]
    crop = (sub * mask)[max(0, sl[0].start - 4):sl[0].stop + 4, max(0, sl[1].start - 4):sl[1].stop + 4]
    rl = SR.sr([crop], iters=12)[0]
    F = field_for(rl)
    fitted_box[d] = FD.fit_glyph(GB.GLYPHS[d](), F) if d in FIT_SET else GB.GLYPHS[d]()
fitted_box["0"] = GB.GLYPHS["0"]()
flip = lambda cs: [xform(c, lambda p: np.array([p[0], HB - p[1]])) for c in cs]
scale_b = CAP / HB
gb = {}
for d in "012345678":
    cs = flip(fitted_box[d])
    allp = np.vstack([contour_poly(c) for c in cs])
    minx = allp[:, 0].min()
    fx = lambda p, minx=minx: np.array([(p[0] - minx) * scale_b + SBB, p[1] * scale_b])
    gb[d] = ([xform(c, fx) for c in cs], (allp[:, 0].max() - minx) * scale_b)
c6, w6 = gb["6"]
cx = SBB + w6 / 2
cy = CAP / 2
gb["9"] = ([xform(c, lambda p: np.array([2 * cx - p[0], 2 * cy - p[1]])) for c in c6], w6)
build_font("F1 Broadcast 98 Box", {d: gb[d] for d in "0123456789"}, CAP, SBB, "../F1Broadcast98-Box.ttf")
print("caixas ok")

# ------------------------------------------------ VALORES
SBV = 30
FLAT = 23.1
scale_v = CAP / FLAT
raw = {}
for ch in "012345678.":
    rl = SR.sr(samples[ch], iters=12)[0]
    F = field_for(rl)
    cs = FD.fit_glyph(GV.GLYPHS[ch](), F) if ch in FIT_SET else GV.GLYPHS[ch]()
    b = GV.BASELINE[ch]
    raw[ch] = [xform(c, lambda p, b=b: np.array([p[0], b - p[1]])) for c in cs]
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
print("valores ok")

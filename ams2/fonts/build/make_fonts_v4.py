import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.cu2quPen import Cu2QuPen
import sr as SR, refine2 as R2

UP = SR.UP


def comps(ink, box, minarea=6):
    x0, y0, x1, y1 = box
    lab, n = ndi.label(ink[y0:y1, x0:x1] > 0.5)
    out = []
    for sl in ndi.find_objects(lab):
        h = sl[0].stop - sl[0].start
        w = sl[1].stop - sl[1].start
        if h * w >= minarea:
            out.append((sl[1].start + x0, sl[0].start + y0, w, h))
    return sorted(out)


def get_contours(img, err=0.05):
    img = cv2.GaussianBlur(img.astype(np.float32), (0, 0), UP * 0.18)
    pad = np.pad(img, 3)
    res = []
    for c in measure.find_contours(pad, 0.5):
        c = c - 3
        x = c[:, 1]
        y = -c[:, 0]
        area = 0.5 * abs(np.sum(x[:-1] * y[1:] - x[1:] * y[:-1]))
        if area < 1.5 * UP * UP:
            continue
        res.append(R2.closed_segments(c, UP, err=err))
    return res


def seg_pts(seg, n=10):
    if seg[0] == 'L':
        return np.array([seg[1], seg[2]])
    P = np.array(seg[1:])
    t = np.linspace(0, 1, n)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


def contour_poly(c):
    return np.vstack([seg_pts(s)[:-1] for s in c])


def xform(c, fx):
    return [(s[0],) + tuple(fx(np.array(p)) for p in s[1:]) for s in c]


def reverse(c):
    out = []
    for s in reversed(c):
        if s[0] == 'L':
            out.append(('L', s[2], s[1]))
        else:
            out.append(('C', s[4], s[3], s[2], s[1]))
    return out


def area(poly):
    x = poly[:, 0]
    y = poly[:, 1]
    return 0.5 * np.sum(x * np.roll(y, -1) - np.roll(x, -1) * y)


def orient(contours):
    polys = [contour_poly(c) for c in contours]
    out = []
    for i, c in enumerate(contours):
        depth = sum(1 for j, q in enumerate(polys) if j != i and measure.points_in_poly(polys[i][:1], q)[0])
        want_cw = (depth % 2 == 0)
        out.append(c if (area(polys[i]) < 0) == want_cw else reverse(c))
    return out


def glyph_from(contours, scale, sb, base=None):
    allp = np.vstack([contour_poly(c) for c in contours])
    minx = allp[:, 0].min()
    miny = allp[:, 1].min() if base is None else base
    fx = lambda p: np.array([(p[0] - minx) * scale + sb, (p[1] - miny) * scale])
    cs = [xform(c, fx) for c in contours]
    w = (allp[:, 0].max() - minx) * scale
    return cs, w


def height(contours):
    a = np.vstack([contour_poly(c) for c in contours])
    return a[:, 1].max() - a[:, 1].min()


def draw(contours):
    inner = TTGlyphPen(None)
    pen = Cu2QuPen(inner, max_err=1.2, reverse_direction=False)
    for c in orient(contours):
        pen.moveTo(tuple(c[0][1]))
        for s in c:
            if s[0] == 'L':
                pen.lineTo(tuple(s[2]))
            else:
                pen.curveTo(tuple(s[2]), tuple(s[3]), tuple(s[4]))
        pen.closePath()
    return inner.glyph()


def lines_contour(pts):
    pts = [np.array(p, float) for p in pts]
    return [('L', pts[i], pts[(i + 1) % len(pts)]) for i in range(len(pts))]


def circle_like(x0, y0, x1, y1, e=2.0):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    a, b = (x1 - x0) / 2, (y1 - y0) / 2
    k = 0.5523 * (1 + (e - 2) * 0.17)
    pts = [(cx + a, cy), (cx, cy + b), (cx - a, cy), (cx, cy - b)]
    out_t = [(0, k * b), (-k * a, 0), (0, -k * b), (k * a, 0)]
    in_t = [(0, -k * b), (k * a, 0), (0, k * b), (-k * a, 0)]
    segs = []
    for i in range(4):
        p0 = np.array(pts[i], float)
        p3 = np.array(pts[(i + 1) % 4], float)
        segs.append(('C', p0, p0 + np.array(out_t[i]), p3 + np.array(in_t[i]), p3))
    return segs


def build_font(name, glyphs, cap, sb, outfile):
    chars = list(glyphs)
    nm = lambda c: {".": "period", ":": "colon", "+": "plus", "-": "hyphen"}.get(c, "uni%04X" % ord(c))
    order = [".notdef", "space"] + [nm(c) for c in chars]
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(order)
    cmap = {32: "space"}
    gl = {".notdef": draw([lines_contour([(60, 0), (60, 700), (440, 700), (440, 0)]),
                           lines_contour([(120, 60), (380, 60), (380, 640), (120, 640)])]),
          "space": TTGlyphPen(None).glyph()}
    hm = {".notdef": (500, 60), "space": (int(cap * 0.35), 0)}
    for c in chars:
        cs, w = glyphs[c]
        cmap[ord(c)] = nm(c)
        gl[nm(c)] = draw(cs)
        hm[nm(c)] = (int(round(w + 2 * sb)), int(sb))
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(gl)
    fb.setupHorizontalMetrics(hm)
    fb.setupHorizontalHeader(ascent=int(cap * 1.15), descent=-int(cap * 0.3))
    fb.setupNameTable({"familyName": name, "styleName": "Regular", "uniqueFontIdentifier": name + " Regular",
                       "fullName": name, "psName": name.replace(" ", "") + "-Regular", "version": "Version 2.000"})
    fb.setupOS2(sTypoAscender=int(cap * 1.15), sTypoDescender=-int(cap * 0.3), usWinAscent=int(cap * 1.2),
                usWinDescent=int(cap * 0.35), sCapHeight=cap, sxHeight=cap)
    fb.setupPost()
    fb.save(outfile)


CAP = 700
SB = 22
im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
Rr, Gg, Bb = im[..., 0], im[..., 1], im[..., 2]

# ---------- VALORES ----------
s = np.clip(((Rr + Gg) / 2 - Bb) / 195.0, 0, 1)
rows = [(15, 55), (62, 98), (108, 141), (151, 186)]
blocks = [(372, 460), (795, 892)]
order_ = [("L", "A", "P", "2"), ("0", ".", "9", "4", "7"), ("2", ".", "6", "4", "3"), ("3", ".", "3", "1", "9"),
          ("4", ".", "3", "6", "5"), ("5", ".", "5", "5", "3"), ("8", ".", "6", "2", "9"), ("9", ".", "1", "4", "6")]
samples = {}
k = 0
for bx in blocks:
    for ry in rows:
        for ch, c in zip(order_[k], comps(s, (bx[0], ry[0], bx[1], ry[1]))):
            x, y, w, h = c
            m = 4
            p = s[max(0, y - m):y + h + m, max(0, x - m):x + w + m].copy()
            lab, n = ndi.label(p > 0.35)
            cy, cx = y - max(0, y - m) + h // 2, x - max(0, x - m) + w // 2
            keep = lab[min(cy, p.shape[0] - 1), min(cx, p.shape[1] - 1)]
            if keep == 0:
                keep = np.argmax(ndi.sum(p > 0.35, lab, range(1, n + 1))) + 1
            samples.setdefault(ch, []).append(p * ndi.binary_dilation(lab == keep, iterations=2))
        k += 1
sv = {ch: get_contours(SR.sr(ps, iters=12)[0]) for ch, ps in samples.items()}
H = np.median([height(sv[c]) for c in "0123456789"])
scale_v = CAP / H
gv = {ch: glyph_from(sv[ch], scale_v, SB) for ch in "0123456789."}
dot, dot_w = gv["."]
stem = dot_w * 0.95
dot2 = [xform(c, lambda p: p + np.array([0, CAP * 0.58])) for c in dot]
gv[":"] = (dot + dot2, dot_w)
bar_w = CAP * 0.52
y0b = CAP * 0.30
a = SB + bar_w / 2 - stem / 2
b = a + stem
hh = bar_w / 2 - stem / 2
gv["-"] = ([lines_contour([(SB, y0b), (SB, y0b + stem), (SB + bar_w, y0b + stem), (SB + bar_w, y0b)])], bar_w)
gv["+"] = ([lines_contour([(SB, y0b), (SB, y0b + stem), (a, y0b + stem), (a, y0b + stem + hh), (b, y0b + stem + hh),
                           (b, y0b + stem), (SB + bar_w, y0b + stem), (SB + bar_w, y0b), (b, y0b), (b, y0b - hh),
                           (a, y0b - hh), (a, y0b)])], bar_w)
build_font("F1 Broadcast 98 Values", gv, CAP, SB, "../F1Broadcast98-Values.ttf")

# ---------- CAIXAS ----------
ink = np.clip((200 - Rr) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]
bp = {}
for d, (xs, yy) in zip("12345678", tiles):
    sub = ink[yy[0]:yy[1], xs[0]:xs[1]]
    lab, n = ndi.label(sub > 0.5)
    sizes = ndi.sum(sub > 0.5, lab, range(1, n + 1))
    kk = int(np.argmax(sizes)) + 1
    mask = ndi.binary_dilation(lab == kk, iterations=2)
    sl = ndi.find_objects((lab == kk).astype(int))[0]
    crop = (sub * mask)[max(0, sl[0].start - 4):sl[0].stop + 4, max(0, sl[1].start - 4):sl[1].stop + 4]
    bp[d] = get_contours(SR.sr([crop], iters=12)[0])
hb = np.median([height(c) for c in bp.values()])
scale_b = CAP / hb
gb = {d: glyph_from(bp[d], scale_b, SB) for d in "12345678"}
c6, w6 = gb["6"]
cx = SB + w6 / 2
cy = CAP / 2
gb["9"] = ([xform(c, lambda p: np.array([2 * cx - p[0], 2 * cy - p[1]])) for c in c6], w6)
w0 = (gb["8"][1] + gb["6"][1]) / 2
t_side = gb["1"][1] * 0.72
t_tb = t_side * 0.7
gb["0"] = ([circle_like(SB, 0, SB + w0, CAP, e=3.0), circle_like(SB + t_side, t_tb, SB + w0 - t_side, CAP - t_tb, e=2.6)], w0)
build_font("F1 Broadcast 98 Box", gb, CAP, SB, "../F1Broadcast98-Box.ttf")
print("ok v4")

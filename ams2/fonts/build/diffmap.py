"""Mapa de diferenças: contorno desenhado x reconstrução do original. Vermelho = sobra no desenho, azul = falta no desenho."""
import sys
import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
import sr as SR
import glyphs_box as GB

UP = SR.UP
S = 24
im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
ink = np.clip((200 - im[..., 0]) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]


def flatten(seg, n=30):
    if seg[0] == 'L':
        return np.array([seg[1]])
    P = np.array(seg[1:])
    t = np.linspace(0, 1, n, endpoint=False)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


def raster(contours, x0, y0, shape):
    m = np.zeros(shape, np.uint8)
    for c in contours:
        pts = np.vstack([flatten(s) for s in c])
        q = np.round(np.stack([(x0 + pts[:, 0]) * S, (y0 + pts[:, 1]) * S], 1)).astype(np.int32)
        layer = np.zeros(shape, np.uint8)
        cv2.fillPoly(layer, [q.reshape(-1, 1, 2)], 1)
        m ^= layer                                    # regra par-ímpar
    return m


sheets = []
report = []
for d, (xs, yy) in zip("12345678", tiles):
    sub = ink[yy[0]:yy[1], xs[0]:xs[1]]
    lab, n = ndi.label(sub > 0.5)
    sizes = ndi.sum(sub > 0.5, lab, range(1, n + 1))
    kk = int(np.argmax(sizes)) + 1
    mask = ndi.binary_dilation(lab == kk, iterations=2)
    sl = ndi.find_objects((lab == kk).astype(int))[0]
    crop = (sub * mask)[max(0, sl[0].start - 4):sl[0].stop + 4, max(0, sl[1].start - 4):sl[1].stop + 4]
    rl = SR.sr([crop], iters=12)[0]
    img = cv2.GaussianBlur(rl.astype(np.float32), (0, 0), UP * 0.25)
    cs = measure.find_contours(np.pad(img, 3), 0.5)
    allp = np.vstack([c - 3 for c in cs])
    x0 = allp[:, 1].min() / UP
    y0 = allp[:, 0].min() / UP
    H, W = rl.shape
    orig = cv2.resize(img, (W * S // UP, H * S // UP), interpolation=cv2.INTER_AREA) > 0.5
    mine = raster(GB.GLYPHS[d](), x0, y0, orig.shape).astype(bool)
    canvas = np.full(orig.shape + (3,), 255, np.uint8)
    canvas[orig & mine] = (60, 60, 60)
    canvas[mine & ~orig] = (0, 0, 255)           # BGR vermelho: sobra
    canvas[orig & ~mine] = (255, 120, 0)         # BGR azul: falta
    ex = (mine & ~orig).sum() / S**2
    mi = (orig & ~mine).sum() / S**2
    report.append((d, round(ex, 1), round(mi, 1), round(orig.sum() / S**2, 0)))
    sheets.append(canvas)
Hh = max(s.shape[0] for s in sheets)
Ww = max(s.shape[1] for s in sheets)
out = np.full((Hh * 2, Ww * 4, 3), 255, np.uint8)
for i, s in enumerate(sheets):
    r, c = divmod(i, 4)
    out[r * Hh:r * Hh + s.shape[0], c * Ww:c * Ww + s.shape[1]] = s
cv2.imwrite("diffmap_box.png", out)
print("glifo, sobra(px2), falta(px2), area_original(px2)")
for r in report:
    print(r)

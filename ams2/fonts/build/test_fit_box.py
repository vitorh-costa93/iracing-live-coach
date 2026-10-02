import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
import sr as SR
import glyphs_box as GB
import fitdata as FD

UP = SR.UP
S = 12
im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
ink = np.clip((200 - im[..., 0]) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]


def flat(seg, n=30):
    if seg[0] == 'L':
        return np.array([seg[1]])
    P = np.array(seg[1:])
    t = np.linspace(0, 1, n, endpoint=False)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


def raster(contours, x0, y0, shape):
    m = np.zeros(shape, np.uint8)
    for c in contours:
        pts = np.vstack([flat(s) for s in c])
        q = np.round(np.stack([(x0 + pts[:, 0]) * S, (y0 + pts[:, 1]) * S], 1)).astype(np.int32)
        layer = np.zeros(shape, np.uint8)
        cv2.fillPoly(layer, [q.reshape(-1, 1, 2)], 1)
        m ^= layer
    return m.astype(bool)


tot_b = tot_a = 0
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
    F = FD.Field(rl, UP, x0, y0)
    g0 = GB.GLYPHS[d]()
    g1 = FD.fit_glyph(g0, F)
    b = (raster(g0, x0, y0, orig.shape) ^ orig).sum() / S**2
    a = (raster(g1, x0, y0, orig.shape) ^ orig).sum() / S**2
    tot_b += b
    tot_a += a
    print(d, "xor antes %.1f depois %.1f px2" % (b, a))
print("total antes %.1f depois %.1f" % (tot_b, tot_a))

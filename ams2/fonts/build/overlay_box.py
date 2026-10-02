import sys
import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
import sr as SR
import glyphs_box as GB

UP = SR.UP
S = 26
im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
ink = np.clip((200 - im[..., 0]) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]
want = sys.argv[1] if len(sys.argv) > 1 else "12345678"
out_name = sys.argv[2] if len(sys.argv) > 2 else "overlay_box.png"


def seg_pts(seg, n=24):
    if seg[0] == 'L':
        return np.array([seg[1], seg[2]])
    P = np.array(seg[1:])
    t = np.linspace(0, 1, n)[:, None]
    mt = 1 - t
    return mt**3 * P[0] + 3 * mt**2 * t * P[1] + 3 * mt * t**2 * P[2] + t**3 * P[3]


sheets = []
for d, (xs, yy) in zip("12345678", tiles):
    if d not in want:
        continue
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
    canvas = cv2.resize((255 * (1 - np.clip(rl, 0, 1))).astype(np.uint8), (W * S // UP, H * S // UP), interpolation=cv2.INTER_AREA)
    canvas = cv2.cvtColor(canvas, cv2.COLOR_GRAY2BGR)
    for c in GB.GLYPHS[d]():
        for s in c:
            p = seg_pts(s)
            q = np.stack([(x0 + p[:, 0]) * S, (y0 + p[:, 1]) * S], 1).astype(np.int32)
            cv2.polylines(canvas, [q.reshape(-1, 1, 2)], False, (0, 0, 255), 1, cv2.LINE_AA)
    sheets.append(canvas)
if "0" in want:
    pass
cols = 2
rows = (len(sheets) + cols - 1) // cols
Hh = max(s.shape[0] for s in sheets)
Ww = max(s.shape[1] for s in sheets)
out = np.full((Hh * rows, Ww * cols, 3), 255, np.uint8)
for i, s in enumerate(sheets):
    r, c = divmod(i, cols)
    out[r * Hh:r * Hh + s.shape[0], c * Ww:c * Ww + s.shape[1]] = s
cv2.imwrite(out_name, out)
print(out.shape)

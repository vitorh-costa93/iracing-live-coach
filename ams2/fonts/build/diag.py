import sys
import numpy as np, cv2
from scipy import ndimage as ndi

sys.argv = ["x"]
src = open("make_fonts_v4.py", encoding="utf-8").read().split("CAP = 700")[0]
exec(src)

im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
Rr, Gg, Bb = im[..., 0], im[..., 1], im[..., 2]
ink = np.clip((200 - Rr) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]
S = 14
sheets = []
for d, (xs, yy) in zip("12345678", tiles):
    sub = ink[yy[0]:yy[1], xs[0]:xs[1]]
    lab, n = ndi.label(sub > 0.5)
    sizes = ndi.sum(sub > 0.5, lab, range(1, n + 1))
    kk = int(np.argmax(sizes)) + 1
    mask = ndi.binary_dilation(lab == kk, iterations=2)
    sl = ndi.find_objects((lab == kk).astype(int))[0]
    crop = (sub * mask)[max(0, sl[0].start - 4):sl[0].stop + 4, max(0, sl[1].start - 4):sl[1].stop + 4]
    rl = SR.sr([crop], iters=12)[0]
    cons = get_contours(rl)
    H, W = rl.shape
    canvas = cv2.resize((255 * (1 - np.clip(rl, 0, 1))).astype(np.uint8), (W * S // UP, H * S // UP), interpolation=cv2.INTER_AREA)
    canvas = cv2.cvtColor(canvas, cv2.COLOR_GRAY2BGR)
    for c in cons:
        for s in c:
            pts = seg_pts(s, 24)
            # coords (x,y) px orig; y para cima -> imagem
            p = np.stack([pts[:, 0] * S, (-pts[:, 1]) * S], 1).astype(np.int32)
            col = (0, 0, 255) if s[0] == 'C' else (255, 0, 0)
            cv2.polylines(canvas, [p.reshape(-1, 1, 2)], False, col, 1)
            e = (int(s[1][0] * S), int(-s[1][1] * S))
            cv2.circle(canvas, e, 3, (0, 160, 0), -1)
    sheets.append(canvas)
Hh = max(s.shape[0] for s in sheets)
Ww = sum(s.shape[1] for s in sheets[:4])
out = np.full((Hh * 2, max(sum(s.shape[1] for s in sheets[:4]), sum(s.shape[1] for s in sheets[4:])), 3), 255, np.uint8)
x = 0
for s in sheets[:4]:
    out[:s.shape[0], x:x + s.shape[1]] = s
    x += s.shape[1]
x = 0
for s in sheets[4:]:
    out[Hh:Hh + s.shape[0], x:x + s.shape[1]] = s
    x += s.shape[1]
cv2.imwrite("diag_box.png", out)
print(out.shape)

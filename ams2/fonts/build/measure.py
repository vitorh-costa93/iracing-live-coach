import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
import sr as SR

UP = SR.UP
im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
ink = np.clip((200 - im[..., 0]) / 200.0, 0, 1)
Lx = (5, 43)
Rx = (471, 511)
ys = [(12, 50), (57, 96), (104, 142), (148, 188)]
tiles = [(Lx, ys[i]) for i in range(4)] + [(Rx, ys[i]) for i in range(4)]
np.set_printoptions(precision=1, suppress=True)
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
    print("== glifo", d, "largura %.1f altura %.1f" % ((allp[:, 1].max() - allp[:, 1].min()) / UP, (allp[:, 0].max() - allp[:, 0].min()) / UP))
    for ci, c in enumerate(cs):
        c = (c - 3) / UP
        if len(c) < 20:
            continue
        poly = measure.approximate_polygon(c, 0.22)
        pts = np.stack([poly[:, 1] - x0, poly[:, 0] - y0], 1)   # (x,y) com y para baixo, origem no canto sup-esq do glifo
        print(" contorno", ci, "n=%d" % len(pts), "área~%.0f" % abs(0.5 * np.sum(pts[:-1, 0] * pts[1:, 1] - pts[1:, 0] * pts[:-1, 1])))
        print("  ", " ".join("(%.1f,%.1f)" % (x, y) for x, y in pts[:-1]))

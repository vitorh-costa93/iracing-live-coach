import numpy as np, cv2
from scipy import ndimage as ndi
from skimage import measure
import sr as SR

src = open("make_fonts_v4.py", encoding="utf-8").read()
head = src.split("sv = {ch")[0]
exec(head)

np.set_printoptions(precision=1, suppress=True)
UPX = SR.UP
for ch in "0123456789.":
    ps = samples[ch]
    rl = SR.sr(ps, iters=12)[0]
    img = cv2.GaussianBlur(rl.astype(np.float32), (0, 0), UPX * 0.25)
    cs = measure.find_contours(np.pad(img, 3), 0.5)
    allp = np.vstack([c - 3 for c in cs])
    x0 = allp[:, 1].min() / UPX
    y0 = allp[:, 0].min() / UPX
    print("== glifo", ch, "n_amostras", len(ps), "largura %.1f altura %.1f" % ((allp[:, 1].max() - allp[:, 1].min()) / UPX, (allp[:, 0].max() - allp[:, 0].min()) / UPX))
    for ci, c in enumerate(cs):
        c = (c - 3) / UPX
        if len(c) < 20:
            continue
        poly = measure.approximate_polygon(c, 0.2)
        pts = np.stack([poly[:, 1] - x0, poly[:, 0] - y0], 1)
        print(" contorno", ci, "n=%d" % len(pts))
        print("  ", " ".join("(%.1f,%.1f)" % (x, y) for x, y in pts[:-1]))

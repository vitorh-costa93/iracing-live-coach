import sys, pickle
import numpy as np
from PIL import Image
import glyphs_box as GB
import optpix as OP

src = np.asarray(Image.open("src.png").convert("RGB"), float)
tiles = {"1": (5, 12), "2": (5, 57), "3": (5, 104), "4": (5, 148), "5": (471, 12), "6": (471, 57), "7": (471, 104), "8": (471, 148)}
TW = 40
want = sys.argv[1] if len(sys.argv) > 1 else "12345678"
lam = float(sys.argv[2]) if len(sys.argv) > 2 else 0.6
sigma = float(sys.argv[3]) if len(sys.argv) > 3 else 0.4
res = {}
for d in want:
    x0, y0 = tiles[d]
    obs = np.clip(1 - src[y0:y0 + TW, x0:x0 + TW, 0] / 255.0, 0, 1).astype(np.float32)
    contours = GB.GLYPHS[d]()
    m = OP.Model(contours)
    # origem inicial: centro aproximado (o otimizador corrige)
    org0 = (13.0, 7.5)
    theta, org, e0, e1 = OP.optimize(m, obs, (4, 36, 4, 36), org0, sigma=sigma, lam=lam)
    # segunda passada partindo do resultado (origem já refinada)
    m2 = OP.Model(m.contours(theta))
    theta2, org2, e1b, e2 = OP.optimize(m2, obs, (4, 36, 4, 36), org, sigma=sigma, lam=lam, steps=(0.25, 0.12, 0.06))
    res[d] = (m2.contours(theta2), tuple(org2))
    print(d, "erro de pixel %.1f -> %.1f -> %.1f" % (e0, e1, e2))
pickle.dump(res, open("opt_box.pkl", "wb"))

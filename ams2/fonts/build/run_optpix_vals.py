import sys, pickle
import numpy as np

head = open("make_fonts_v4.py", encoding="utf-8").read().split("sv = {ch")[0]
exec(head)
import glyphs_vals as GV
import optpix as OP

want = sys.argv[1] if len(sys.argv) > 1 else "012345678."
lam = float(sys.argv[2]) if len(sys.argv) > 2 else 2.0
res = {}
for ch in want:
    m = OP.Model(GV.GLYPHS[ch]())
    obs = [np.clip(p, 0, 1).astype(np.float32) for p in samples[ch]]
    org0 = [(4.0, 4.0)] * len(obs)
    theta, orgs, e0, e1 = OP.optimize_multi(m, obs, org0, sigma=0.42, lam=lam)
    res[ch] = m.contours(theta)
    print(ch, "amostras", len(obs), "erro %.1f -> %.1f" % (e0, e1))
pickle.dump(res, open("opt_vals.pkl", "wb"))

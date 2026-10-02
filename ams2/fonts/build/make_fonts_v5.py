"""Fonte das caixas (F1Broadcast98-Box) a partir dos contornos desenhados em glyphs_box.py."""
import numpy as np

src = open("make_fonts_v4.py", encoding="utf-8").read().split("CAP = 700")[0]
exec(src)                       # imports + utilitários (xform, glyph_from, draw, build_font...)
import glyphs_box as GB

CAP = 700
SB = 22
H = 25.0


def flip(contours):
    f = lambda p: np.array([p[0], H - p[1]])
    return [xform(c, f) for c in contours]


raw = {d: flip(GB.GLYPHS[d]()) for d in "012345678"}
scale = CAP / H
gb = {}
for d in "12345678":
    gb[d] = glyph_from(raw[d], scale, SB, base=0.0)
# 0: contorno desenhado (anel)
gb["0"] = glyph_from(raw["0"], scale, SB, base=0.0)
# 9 = 6 girado 180 graus em torno do centro da caixa do glifo
c6, w6 = gb["6"]
cx = SB + w6 / 2
cy = CAP / 2
gb["9"] = ([xform(c, lambda p: np.array([2 * cx - p[0], 2 * cy - p[1]])) for c in c6], w6)
order = "0123456789"
build_font("F1 Broadcast 98 Box", {d: gb[d] for d in order}, CAP, SB, "../F1Broadcast98-Box.ttf")
print("ok v5")

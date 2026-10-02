"""Compara o dígito real (pixels da captura) com a fonte renderizada na mesma grade de pixels."""
import sys
import numpy as np
import cv2
from PIL import Image, ImageDraw, ImageFont

src = Image.open("src.png").convert("RGB")
want = sys.argv[1] if len(sys.argv) > 1 else "348"
font_path = sys.argv[2] if len(sys.argv) > 2 else "../F1Broadcast98-Box.ttf"
tiles = {"1": (5, 12), "2": (5, 57), "3": (5, 104), "4": (5, 148), "5": (471, 12), "6": (471, 57), "7": (471, 104), "8": (471, 148)}
SS = 16
TW = 40
f = ImageFont.truetype(font_path, int(round(25 / 0.7 * SS)))
Z = 14                       # ampliação (nearest) para exibir

panels = []
for d in want:
    x0, y0 = tiles[d]
    o = np.asarray(src.crop((x0, y0, x0 + TW, y0 + TW)).convert("RGB"), float)
    ink_o = np.clip(1 - o[..., 0] / 255.0, 0, 1)            # canal R: preto = 1, amarelo = 0
    big = Image.new("L", (TW * SS, TW * SS), 255)
    dr = ImageDraw.Draw(big)
    bb = f.getbbox(d, anchor="ls")
    gx = int((TW * SS - (bb[2] - bb[0])) / 2 - bb[0])
    gy = int(TW * SS * 0.52 + 25 * SS / 2)
    dr.text((gx, gy), d, font=f, fill=0, anchor="ls")
    arr = 1 - np.asarray(big, float) / 255.0
    best = None
    for dy in range(-3 * SS, 3 * SS + 1, 4):
        for dx in range(-3 * SS, 3 * SS + 1, 4):
            sh = np.roll(np.roll(arr, dy, 0), dx, 1)
            low = sh.reshape(TW, SS, TW, SS).mean((1, 3))
            err = np.abs(low - ink_o)[4:36, 4:36].sum()
            if best is None or err < best[0]:
                best = (err, dx, dy, low)
    _, dx, dy, low0 = best
    sh0 = np.roll(np.roll(arr, dy, 0), dx, 1)
    bs = None
    for sg in [0.0, 0.3, 0.45, 0.6, 0.75, 0.9, 1.1]:
        bl = cv2.GaussianBlur(sh0.astype(np.float32), (0, 0), max(sg * SS, 0.01)) if sg > 0 else sh0
        lw = bl.reshape(TW, SS, TW, SS).mean((1, 3))
        er = np.abs(lw - ink_o)[4:36, 4:36].sum()
        if bs is None or er < bs[0]:
            bs = (er, sg, lw)
    print(d, "erro sem desfoque %.1f; melhor sigma %.2f -> erro %.1f" % (best[0], bs[1], bs[0]))
    low = bs[2]
    diff = low - ink_o                                        # + = meu desenho tem tinta a mais
    h, w = ink_o.shape
    img = np.full((h, w * 3 + 4, 3), 40, np.uint8)
    img[:, :w] = (255 * (1 - ink_o))[..., None].astype(np.uint8)
    img[:, w + 2:2 * w + 2] = (255 * (1 - low))[..., None].astype(np.uint8)
    dc = np.full((h, w, 3), 255, np.uint8)
    dc[diff > 0.15] = (255, 60, 60)                           # sobra no meu
    dc[diff < -0.15] = (60, 90, 255)                          # falta no meu
    dc[np.abs(diff) <= 0.15] = (235, 235, 235)
    img[:, 2 * w + 4:] = dc
    big_img = Image.fromarray(img).resize((img.shape[1] * Z, img.shape[0] * Z), Image.NEAREST)
    panels.append(big_img)
W = max(p.width for p in panels)
Ht = sum(p.height for p in panels) + 10 * len(panels)
out = Image.new("RGB", (W, Ht), (20, 20, 20))
y = 0
for p in panels:
    out.paste(p, (0, y))
    y += p.height + 10
out.save("pixel_diff.png")
print(out.size)

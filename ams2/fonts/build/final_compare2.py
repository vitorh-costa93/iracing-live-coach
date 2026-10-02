"""Comparação: referência (captura, baixa resolução) em cima; fonte renderizada como vetor na resolução final embaixo."""
from PIL import Image, ImageDraw, ImageFont

src = Image.open("src.png").convert("RGB")
OUT = "../../tools/fonttest/"
SS = 3          # supersampling da renderização vetorial
D = 5           # fator de exibição (px de tela por px da captura)

# ---------- caixas ----------
tiles = [(5, 43, 12, 50), (5, 43, 57, 96), (5, 43, 104, 142), (5, 43, 148, 188),
         (471, 511, 12, 50), (471, 511, 57, 96), (471, 511, 104, 142), (471, 511, 148, 188)]
TW, TH = 40, 40
cell_w, cell_h = TW * D, TH * D
sheet = Image.new("RGB", (cell_w * 8 + 9 * 8, cell_h * 2 + 3 * 8), (30, 30, 30))
size_px = 25.0 / 0.7 * D * SS            # altura do dígito = 25 px da captura
fb = ImageFont.truetype("../F1Broadcast98-Box.ttf", int(round(size_px)))
for i, (x0, x1, y0, y1) in enumerate(tiles):
    x = 8 + i * (cell_w + 8)
    ref = src.crop((x0, y0, x0 + TW, y0 + TH)).resize((cell_w, cell_h), Image.LANCZOS)
    sheet.paste(ref, (x, 8))
    big = Image.new("RGB", (cell_w * SS, cell_h * SS), (255, 255, 60))
    d = ImageDraw.Draw(big)
    ch = "12345678"[i]
    # centraliza pela caixa real do glifo, linha de base fixa
    bb = fb.getbbox(ch, anchor="ls")
    gw = bb[2] - bb[0]
    base = int(cell_h * SS * 0.5 + 25 * D * SS / 2)
    d.text((int((cell_w * SS - gw) / 2 - bb[0]), base), ch, font=fb, fill=(0, 0, 0), anchor="ls")
    sheet.paste(big.resize((cell_w, cell_h), Image.LANCZOS), (x, 8 + cell_h + 8))
sheet.save(OUT + "compare_box.png")

# ---------- valores ----------
rows = [("0.947", (372, 56)), ("2.643", (372, 104)), ("4.365", (795, 11)), ("9.146", (795, 147))]
CW, CH = 97, 44
D2 = 5
fv = ImageFont.truetype("../F1Broadcast98-Values.ttf", int(round(23.1 / 0.7 * D2 * SS)))
out = Image.new("RGB", (2 * CW * D2 + 30, len(rows) * CH * D2 + 10), (30, 30, 30))
for i, (txt, (x0, y0)) in enumerate(rows):
    out.paste(src.crop((x0, y0, x0 + CW, y0 + CH)).resize((CW * D2, CH * D2), Image.LANCZOS), (5, 5 + i * CH * D2))
    W2, H2 = CW * D2 * SS, CH * D2 * SS
    big = Image.new("RGBA", (W2, H2), (52, 52, 52, 255))
    w = ImageDraw.Draw(big).textlength(txt, font=fv)
    pos = (W2 - 6 * D2 * SS - w, 34 * D2 * SS)
    sh = Image.new("RGBA", (W2, H2), (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((pos[0] + 2 * D2 * SS, pos[1] + 2 * D2 * SS), txt, font=fv, fill=(0, 0, 0, 166), anchor="ls")
    big = Image.alpha_composite(big, sh)
    tx = Image.new("RGBA", (W2, H2), (0, 0, 0, 0))
    ImageDraw.Draw(tx).text(pos, txt, font=fv, fill=(255, 255, 60, 255), anchor="ls")
    big = Image.alpha_composite(big, tx).convert("RGB")
    out.paste(big.resize((CW * D2, CH * D2), Image.LANCZOS), (CW * D2 + 25, 5 + i * CH * D2))
out.save(OUT + "compare_values.png")
print("ok")

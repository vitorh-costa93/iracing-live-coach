from PIL import Image, ImageDraw, ImageFont
src = Image.open("src.png").convert("RGB")
fv = ImageFont.truetype("../F1Broadcast98-Values.ttf", 34)
fb = ImageFont.truetype("../F1Broadcast98-Box.ttf", 34)
S = 4
rows = [("0.947", (372, 56)), ("2.643", (372, 104)), ("4.365", (795, 11)), ("9.146", (795, 147))]
out = Image.new("RGB", (2 * 97 * S + 30, len(rows) * 44 * S + 10), (40, 40, 40))
for i, (txt, (x0, y0)) in enumerate(rows):
    out.paste(src.crop((x0, y0, x0 + 97, y0 + 44)).resize((97 * S, 44 * S), Image.LANCZOS), (5, 5 + i * 44 * S))
    t = Image.new("RGB", (97, 44), (52, 52, 52))
    d = ImageDraw.Draw(t)
    w = d.textlength(txt, font=fv)
    d.text((97 - 6 - w, 34), txt, font=fv, fill=(255, 255, 60), anchor="ls")
    out.paste(t.resize((97 * S, 44 * S), Image.LANCZOS), (97 * S + 25, 5 + i * 44 * S))
out.save("../../tools/fonttest/compare_values.png")
cmp = Image.new("RGB", (1000, 330), (40, 40, 40))
for i, (x0, x1, y0, y1) in enumerate([(5, 43, 12, 50), (5, 43, 57, 96), (5, 43, 104, 142), (5, 43, 148, 188), (471, 511, 12, 50), (471, 511, 57, 96), (471, 511, 104, 142), (471, 511, 148, 188)]):
    cmp.paste(src.crop((x0, y0, x1, y1)).resize((38 * 3, 40 * 3), Image.LANCZOS), (8 + i * 122, 8))
    t = Image.new("RGB", (38, 40), (255, 255, 60))
    dd = ImageDraw.Draw(t)
    ch = "12345678"[i]
    dd.text((int(19 - dd.textlength(ch, font=fb) / 2), 5), ch, font=fb, fill=(0, 0, 0))
    cmp.paste(t.resize((38 * 3, 40 * 3), Image.LANCZOS), (8 + i * 122, 150))
cmp.save("../../tools/fonttest/compare_box.png")
big = Image.new("RGB", (1500, 760), (45, 45, 45))
d = ImageDraw.Draw(big)
def row(y, label, path, txt, col, size):
    f = ImageFont.truetype(path, size)
    d.text((10, y + 2), label, fill=(150, 200, 255))
    d.text((10, y + 20), txt, font=f, fill=col)
row(0, "Caixas · antes (traçado direto)", "v1/F1Broadcast98-Box.ttf", "1234567890", (255, 255, 255), 170)
row(250, "Caixas · agora (contornos desenhados sobre a medição)", "../F1Broadcast98-Box.ttf", "1234567890", (255, 255, 255), 170)
row(500, "Valores · agora (contornos desenhados)", "../F1Broadcast98-Values.ttf", "0123456789.:+-", (255, 255, 60), 140)
big.save("../../tools/fonttest/compare_sr.png")

from PIL import Image, ImageDraw, ImageFont
src = Image.open("src.png").convert("RGB")
SS, D = 3, 6
tiles = [(5, 43, 12, 50), (5, 43, 57, 96), (5, 43, 104, 142), (5, 43, 148, 188),
         (471, 511, 12, 50), (471, 511, 57, 96), (471, 511, 104, 142), (471, 511, 148, 188)]
TW, TH = 40, 40
cw, ch = TW * D, TH * D
sheet = Image.new("RGB", (cw * 8 + 72, ch * 3 + 32), (30, 30, 30))
fonts = [ImageFont.truetype(p, int(round(25 / 0.7 * D * SS))) for p in ("plain_box.ttf", "fit_box.ttf")]
for i, (x0, x1, y0, y1) in enumerate(tiles):
    x = 8 + i * (cw + 8)
    sheet.paste(src.crop((x0, y0, x0 + TW, y0 + TH)).resize((cw, ch), Image.LANCZOS), (x, 8))
    for r, f in enumerate(fonts):
        big = Image.new("RGB", (cw * SS, ch * SS), (255, 255, 60))
        d = ImageDraw.Draw(big)
        c = "12345678"[i]
        bb = f.getbbox(c, anchor="ls")
        d.text((int((cw * SS - (bb[2] - bb[0])) / 2 - bb[0]), int(ch * SS * 0.5 + 25 * D * SS / 2)), c, font=f, fill=(0, 0, 0), anchor="ls")
        sheet.paste(big.resize((cw, ch), Image.LANCZOS), (x, 8 + (r + 1) * (ch + 8)))
sheet.save("three_way_box.png")
print(sheet.size)

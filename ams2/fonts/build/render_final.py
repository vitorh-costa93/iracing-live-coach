from PIL import Image, ImageDraw, ImageFont
img = Image.new("RGB", (1500, 760), (45, 45, 45))
d = ImageDraw.Draw(img)
def row(y, label, path, txt, col, size):
    f = ImageFont.truetype(path, size)
    d.text((10, y + 2), label, fill=(150, 200, 255))
    d.text((10, y + 20), txt, font=f, fill=col)
row(0, "Caixas · antes (traçado direto)", "v1/F1Broadcast98-Box.ttf", "1234567890", (255, 255, 255), 170)
row(250, "Caixas · agora (contornos desenhados sobre a medição)", "../F1Broadcast98-Box.ttf", "1234567890", (255, 255, 255), 170)
row(500, "Valores · atual", "../F1Broadcast98-Values.ttf", "0123456789.:+-", (255, 255, 60), 140)
img.save("compare_final.png")

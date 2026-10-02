"""Gera o icone/logo do app AMS2 Live Coach (marca propria, sem marcas de terceiros).

Uso: python ams2\assets\make_icon.py     (requer Pillow)
Saida: ams2\assets\ams2-live-coach.ico (16..256 px) e ams2-live-coach.png (512 px).
Desenho: quadrado arredondado grafite, anel de velocimetro aberto em ciano-esverdeado
(mesma paleta do Control Center) e tres barras de classificacao no centro.
"""
import math
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
BG, BG2, ACCENT, DIM = (26, 29, 35), (18, 20, 24), (79, 184, 176), (45, 50, 60)
S = 1024  # supersample


def render(size=S):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = size / 1024
    # fundo: quadrado arredondado com leve degrade vertical
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / (size - 1)
        gd.line([(0, y), (size, y)], fill=tuple(int(BG[i] * (1 - t) + BG2[i] * t) for i in range(3)) + (255,))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(210 * k), fill=255)
    img.paste(grad, (0, 0), mask)
    d.rounded_rectangle([int(10 * k), int(10 * k), size - 1 - int(10 * k), size - 1 - int(10 * k)],
                        radius=int(200 * k), outline=DIM + (255,), width=max(1, int(10 * k)))
    # anel de velocimetro: arco de 270 graus aberto embaixo
    cx = cy = size / 2
    r = 340 * k
    w = int(70 * k)
    box = [cx - r, cy - r, cx + r, cy + r]
    d.arc(box, 135, 405, fill=DIM + (255,), width=w)          # trilho
    d.arc(box, 135, 135 + 270 * 0.78, fill=ACCENT + (255,), width=w)  # progresso
    # pontas arredondadas do progresso
    for ang in (135, 135 + 270 * 0.78):
        a = math.radians(ang)
        rc = r - w / 2  # PIL desenha o arco para dentro do box
        px, py = cx + rc * math.cos(a), cy + rc * math.sin(a)
        d.ellipse([px - w / 2, py - w / 2, px + w / 2, py + w / 2], fill=ACCENT + (255,))
    # tres barras de classificacao (larguras decrescentes)
    bx, bh, gap = cx - 150 * k, 46 * k, 30 * k
    top = cy - (3 * bh + 2 * gap) / 2 + 10 * k
    for i, wd in enumerate((300, 230, 160)):
        y0 = top + i * (bh + gap)
        d.rounded_rectangle([bx, y0, bx + wd * k, y0 + bh], radius=int(14 * k),
                            fill=(ACCENT if i == 0 else (232, 234, 237)) + (255,))
    return img


def main():
    big = render()
    out512 = big.resize((512, 512), Image.LANCZOS)
    out512.save(os.path.join(HERE, "ams2-live-coach.png"))
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [big.resize((s, s), Image.LANCZOS) for s in sizes]
    imgs[-1].save(os.path.join(HERE, "ams2-live-coach.ico"), format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    print("ok:", os.listdir(HERE))


if __name__ == "__main__":
    main()

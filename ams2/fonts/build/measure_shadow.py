import numpy as np, cv2

im = cv2.imread("src.png")[:, :, ::-1].astype(np.float32)
R, G, B = im[..., 0], im[..., 1], im[..., 2]
lum = 0.3 * R + 0.59 * G + 0.11 * B
yellow = np.clip(((R + G) / 2 - B) / 195.0, 0, 1)
regions = [(372, 15, 460, 55), (372, 62, 460, 98), (372, 108, 460, 141), (372, 151, 460, 186),
           (795, 15, 892, 55), (795, 62, 892, 98), (795, 108, 892, 141), (795, 151, 892, 186)]
K = 6        # superamostragem
best = []
for (x0, y0, x1, y1) in regions:
    y = cv2.resize(yellow[y0:y1, x0:x1], None, fx=K, fy=K, interpolation=cv2.INTER_CUBIC)
    l = cv2.resize(lum[y0:y1, x0:x1], None, fx=K, fy=K, interpolation=cv2.INTER_CUBIC)
    M = y > 0.5
    res = []
    for dy in np.arange(0, 4.01, 0.5):
        for dx in np.arange(0, 4.01, 0.5):
            sh = np.roll(np.roll(M, int(dy * K), 0), int(dx * K), 1)
            ring = sh & ~M
            if ring.sum() < 50:
                continue
            dark = (l < 22)[ring].mean()
            res.append((dark * np.sqrt(ring.sum()), dx, dy, dark))
    res.sort(reverse=True)
    best.append(res[0])
    print("região", (x0, y0), "melhor deslocamento (dx=%.1f, dy=%.1f) escuro=%.2f" % (res[0][1], res[0][2], res[0][3]))
print("mediana dx=%.2f dy=%.2f" % (np.median([b[1] for b in best]), np.median([b[2] for b in best])))
# dureza: perfil de luminância saindo da borda amarela para a direita
prof = []
for (x0, y0, x1, y1) in regions[:4]:
    y = yellow[y0:y1, x0:x1]
    l = lum[y0:y1, x0:x1]
    for r in range(3, y.shape[0] - 3):
        row = y[r]
        edges = np.where((row[:-1] > 0.5) & (row[1:] <= 0.5))[0]
        for e in edges:
            seg = l[r, e + 1:e + 8]
            if len(seg) == 7:
                prof.append(seg)
prof = np.array(prof)
print("luminância média nos 7 px à direita da borda amarela:", np.round(prof.mean(0), 0))
print("fundo típico:", np.round(np.median(lum[y0:y1, x0:x1]), 0))

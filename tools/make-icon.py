# Uso: python tools/make-icon.py deploy/windows/installer/DoveLoFanno.ico  (serve Pillow)
# Icona di DoveLoFanno dal tracciato di web/public/favicon.svg (viewBox 64x64), disegnata a 1024 px e ridotta.
import math, sys
from PIL import Image, ImageDraw
S = 16; N = 64 * S
img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, N - 1, N - 1], radius=14 * S, fill="#1B2440")
# arco: da (20,21) a (38,31.4), raggio 12, large-arc=1, sweep=1 (SVG, asse y in giù)
x1, y1, x2, y2, r = 20, 21, 38, 31.4, 12
mx, my = (x1 + x2) / 2, (y1 + y2) / 2
dx, dy = x2 - x1, y2 - y1; q = math.hypot(dx, dy)
h = math.sqrt(r * r - (q / 2) ** 2)
ux, uy = -dy / q, dx / q
cands = [(mx + ux * h, my + uy * h), (mx - ux * h, my - uy * h)]
pts = []
for cx, cy in cands:
    a1 = math.atan2(y1 - cy, x1 - cx); a2 = math.atan2(y2 - cy, x2 - cx)
    sweep = (a2 - a1) % (2 * math.pi)          # senso orario sullo schermo = angolo crescente con y in giù
    if sweep > math.pi:                         # large arc + sweep positivo
        pts = [(cx + r * math.cos(a1 + sweep * t / 2000), cy + r * math.sin(a1 + sweep * t / 2000)) for t in range(2001)]
        break
p0, c1, c2, p3 = (38, 31.4), (35, 33.4), (32, 35.5), (32, 39)
for i in range(1, 1001):
    t = i / 1000
    pts.append(tuple((1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * c + t ** 3 * e for a, b, c, e in zip(p0, c1, c2, p3)))
w = 9 * S
P = [(x * S, y * S) for x, y in pts]
for x, y in P:   # pennello: un cerchio pieno per punto, così le curve restano piene
    d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill="#F2F4F7")
tri = [(25 * S, 45 * S), (25 * S, 60 * S), (39 * S, 52.5 * S)]
d.polygon(tri, fill="#34C77B")
d.line(tri + [tri[0]], fill="#34C77B", width=3 * S, joint="curve")
for x, y in tri:
    d.ellipse([x - 1.5 * S, y - 1.5 * S, x + 1.5 * S, y + 1.5 * S], fill="#34C77B")
out = sys.argv[1]
img.resize((256, 256), Image.LANCZOS).save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])


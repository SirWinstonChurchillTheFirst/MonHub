"""
MonHub's own menu and marker icons – 32x32 pixel art drawn here (no game graphics), so they are part of MonHub
(GPL-3.0) and live in the repository. The app shows them at 32 px (the Dex mark at 16).

Each icon is a stack of layers (shapes on the pixel grid). A layer is lit from the top left – a light rim there, a
two-step shadow at the bottom right – and separated from the layers below it by a line in its own darkest colour;
the whole silhouette gets a dark outline tinted by the colour next to it. Glints are set by hand.

    python tools/build_icons.py            -> Assets/Icons/<name>.png   (keys "icon/<name>" in the app)
    python tools/build_icons.py --preview  -> tools/icons-preview.png (big and at real size, light and dark)
"""
import math, os, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def ramp(base, light=None, shade=None, dark=None):
    """light / base / shade / dark from one colour (or given explicitly)."""
    b = hexc(base)
    return {"light": hexc(light) if light else mix(b, (255, 255, 255), 0.38),
            "base": b,
            "shade": hexc(shade) if shade else mix(b, (20, 16, 40), 0.28),
            "dark": hexc(dark) if dark else mix(b, (20, 16, 40), 0.58)}


# ---- shapes: functions of the pixel centre (x, y) -> inside?
def rrect(x0, y0, x1, y1, r=0):
    def f(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return False
        cx, cy = min(max(x, x0 + r), x1 - r), min(max(y, y0 + r), y1 - r)
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r + 0.01
    return f


def ellipse(cx, cy, rx, ry=None):
    ry = ry or rx
    return lambda x, y: ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1


def poly(*pts):
    pts = list(pts)

    def f(x, y):
        inside = False
        for (x1, y1), (x2, y2) in zip(pts, pts[1:] + pts[:1]):
            if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
                inside = not inside
        return inside
    return f


def seg(x1, y1, x2, y2, w):
    def f(x, y):
        dx, dy = x2 - x1, y2 - y1
        t = max(0, min(1, ((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy)))
        return math.hypot(x - x1 - t * dx, y - y1 - t * dy) <= w / 2
    return f


def union(*fs):
    return lambda x, y: any(f(x, y) for f in fs)


def minus(a, *bs):
    return lambda x, y: a(x, y) and not any(b(x, y) for b in bs)


def both(a, b):
    return lambda x, y: a(x, y) and b(x, y)


class Icon:
    def __init__(self, size=32):
        self.n = size
        self.layers = []  # (mask, ramp, lit, line)
        self.dots = []

    def add(self, shape, colors, lit=True, line=True):
        n = self.n
        self.layers.append(([[shape(x + 0.5, y + 0.5) for x in range(n)] for y in range(n)], colors, lit, line))
        return self

    def dot(self, color, *pts):
        self.dots += [(p, hexc(color)) for p in pts]
        return self

    def render(self):
        n = self.n
        top = [[-1] * n for _ in range(n)]
        for i, (mask, *_) in enumerate(self.layers):
            for y in range(n):
                for x in range(n):
                    if mask[y][x]:
                        top[y][x] = i
        filled = lambda x, y: 0 <= x < n and 0 <= y < n and top[y][x] >= 0
        img = Image.new("RGBA", (n, n))
        for y in range(n):
            for x in range(n):
                i = top[y][x]
                if i < 0:
                    continue
                mask, c, lit, line = self.layers[i]
                inm = lambda x, y: 0 <= x < n and 0 <= y < n and mask[y][x]
                # a line where this layer lies on a lower one (not at the outside – that gets the outline)
                below = [p for p in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)) if not inm(*p) and filled(*p)]
                if line and any(top[py][px] < i for px, py in below):
                    col = c["dark"]
                elif lit and (not inm(x, y - 1) or not inm(x - 1, y)):
                    col = c["light"]
                elif lit and (not inm(x, y + 1) or not inm(x + 1, y) or not inm(x + 1, y + 1)):
                    col = c["shade"]
                elif lit and (not inm(x, y + 2) or not inm(x + 2, y)):
                    col = mix(c["base"], c["shade"], 0.5)
                else:
                    col = c["base"]
                img.putpixel((x, y), col + (255,))
        # outline around the whole silhouette, tinted by its neighbour
        for y in range(n):
            for x in range(n):
                if filled(x, y):
                    continue
                near = [top[y + dy][x + dx] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if filled(x + dx, y + dy)]
                if near:
                    img.putpixel((x, y), mix(self.layers[max(near)][1]["dark"], (16, 12, 28), 0.55) + (255,))
        for (x, y), col in self.dots:
            img.putpixel((x, y), col + (255,))
        return img


ICONS = {}

# Weiter – a play button
ICONS["play"] = (Icon()
                 .add(rrect(2.5, 2.5, 29.5, 29.5, 6), ramp("#3fae4a", "#86e08c"))
                 .add(rrect(5.5, 5.5, 26.5, 15.5, 4), ramp("#52c45c", "#9be8a0"), line=False)
                 .add(poly((11.5, 8.5), (11.5, 23.5), (24, 16)), ramp("#ffffff", "#ffffff", "#dfe8e0", "#b7c7b9"))
                 .dot("#ffffff", (6, 6), (7, 6), (6, 7)))

# Spiele – a game cartridge
cart = poly((5.5, 2.5), (22.5, 2.5), (26.5, 6.5), (26.5, 26.5), (5.5, 26.5))
ICONS["cartridge"] = (Icon()
                      .add(union(cart, *[rrect(7.6 + 3 * i, 26, 9.4 + 3 * i, 29.5) for i in range(6)]), ramp("#f0c341", "#ffe48a"), lit=False)
                      .add(cart, ramp("#a9acc0", "#d9dbe8"))
                      .add(union(*[rrect(8.5, 4.5 + 2 * i, 20.5, 4.6 + 2 * i) for i in range(2)]), ramp("#7c7f96"), lit=False)
                      .add(rrect(8.5, 9.5, 23.5, 22.5, 1.5), ramp("#f7f5ee", "#ffffff"))
                      .add(rrect(10.5, 11.5, 21.5, 18.5), ramp("#6fb7ff"), lit=False)  # the label's picture: sun over a hill
                      .add(ellipse(18.5, 13.8, 1.7), ramp("#ffd84f"), lit=False, line=False)
                      .add(both(ellipse(13, 21, 7, 5), rrect(10.5, 11.5, 21.5, 18.5)), ramp("#5cbf4a"), lit=False, line=False)
                      .add(rrect(10.5, 20.5, 18.5, 20.6), ramp("#c4c4d0"), lit=False, line=False)
                      .dot("#ffffff", (7, 4), (6, 5)))

# Neuer Run – a die, seen from above at an angle
top_f = poly((16, 2.5), (28.5, 8.8), (16, 15), (3.5, 8.8))
left_f = poly((3.5, 8.8), (16, 15), (16, 29.5), (3.5, 23.2))
right_f = poly((16, 15), (28.5, 8.8), (28.5, 23.2), (16, 29.5))
red = ramp("#e23a3a", "#ff7a6e", "#b52630", "#7c1420")
die = (Icon().add(top_f, ramp("#ffffff", "#ffffff", "#f1f1f6", "#9a9ab2"), lit=False)
       .add(left_f, ramp("#e9e9f1", dark="#8d8da6"), lit=False)
       .add(right_f, ramp("#c9c9da", dark="#80809a"), lit=False))
die.add(ellipse(16, 8.8, 2.9, 1.8), red, lit=False, line=False)
for u in (0.3, 0.7):  # two pips on each side face, along its diagonal
    die.add(ellipse(3.5 + 12.5 * u, 8.8 + 6.2 * u + 14.4 * u, 1.7, 2.2), red, lit=False, line=False)
    die.add(ellipse(16 + 12.5 * u, 15 - 6.2 * u + 14.4 * u, 1.7, 2.2), red, lit=False, line=False)
ICONS["dice"] = die

# Nuzlocke – a journal with a skull and a bookmark
journal = (Icon()
           .add(rrect(8.5, 5.5, 28.5, 29.5, 1), ramp("#f3ecd8", "#fffaf0", "#d9cfb3"))
           .add(rrect(5.5, 2.5, 25.5, 27.5, 1.5), ramp("#9a5b39", "#c98a5f"))
           .add(rrect(5.5, 2.5, 8.5, 27.5, 1), ramp("#6e3d24", "#8d5233"), line=False)
           .add(poly((19.5, 24), (24.5, 24), (24.5, 31.5), (22, 29), (19.5, 31.5)), ramp("#e23a3a", "#ff7a6e")))
skull = ["..WWWWW..", ".WWWWWWW.", "WWWWWWWWW", "WkkWWWkkW", "WkkWWWkkW", "WWWWkWWWW", ".WWWWWWW.", "..WkWkW.."]
for y, row in enumerate(skull):
    for x, ch in enumerate(row):
        if ch != ".":
            journal.dot("#f5ecd2" if ch == "W" else "#4a2a18", (12 + x, 8 + y))
ICONS["journal"] = journal.dot("#e6a87c", (7, 4), (6, 5))

# Dex – a magnifying glass
ICONS["magnifier"] = (Icon()
                      .add(seg(20.5, 20.5, 28.5, 28.5, 5.6), ramp("#8d5a34", "#c08454"))
                      .add(seg(19, 19, 21.5, 21.5, 6.4), ramp("#9ea7b8", "#dfe4ee"))
                      .add(ellipse(12.5, 12.5, 10.5), ramp("#aeb6c6", "#e8ecf4"))
                      .add(ellipse(12.5, 12.5, 7.6), ramp("#8fd0f2", "#c6ecff", "#5fa8d8"))
                      .dot("#ffffff", (8, 8), (9, 8), (8, 9), (8, 10), (10, 7), (11, 7), (8, 11)))

# Fangames – a star
star_pts = [(16 + (14.5 if k % 2 == 0 else 6.2) * math.sin(k * math.pi / 5), 17 - (14.5 if k % 2 == 0 else 6.2) * math.cos(k * math.pi / 5)) for k in range(10)]
ICONS["star"] = (Icon()
                 .add(poly(*star_pts), ramp("#f8c42e", "#ffe98a", "#e0961a", "#9a5a0c"))
                 .add(poly(*[(16 + (x - 16) * 0.5, 16.5 + (y - 17) * 0.5) for x, y in star_pts]), ramp("#ffd84f", "#fff2b0"), line=False)
                 .dot("#ffffff", (15, 6), (16, 6), (15, 7), (13, 10)))

# Emulatoren – a handheld console
ICONS["console"] = (Icon()
                    .add(union(rrect(5.5, 1.5, 26.5, 26.5, 2), rrect(5.5, 20, 22.5, 30.5, 2), poly((22, 26), (26.5, 26), (26.5, 26.5), (22, 30.5))), ramp("#c9cad8", "#eceef6"))
                    .add(rrect(8.5, 4.5, 23.5, 16.5, 1), ramp("#585a72"), lit=False)
                    .add(rrect(10.5, 6.5, 21.5, 14.5), ramp("#9bc53d", "#c8e86a", "#7aa62a"), line=False)
                    .add(union(rrect(8.5, 20.5, 15.5, 22.5), rrect(11, 18, 13, 25)), ramp("#383a4a", "#55576a"))
                    .add(ellipse(22.5, 20, 1.8), ramp("#d81b60", "#ff5c93"))
                    .add(ellipse(18, 23.5, 1.8), ramp("#d81b60", "#ff5c93"))
                    .add(union(seg(13, 28, 15, 28, 1), seg(17, 28, 19, 28, 1)), ramp("#7c7f96"), lit=False, line=False)
                    .dot("#eef8c8", (11, 7), (12, 7), (11, 8)))

# Optionen – a gear
teeth = [seg(16 + 9 * math.cos(a), 16 + 9 * math.sin(a), 16 + 13.6 * math.cos(a), 16 + 13.6 * math.sin(a), 5.2)
         for a in (k * math.pi / 4 + math.pi / 8 for k in range(8))]
ICONS["gear"] = (Icon()
                 .add(minus(union(ellipse(16, 16, 10.5), *teeth), ellipse(16, 16, 3.6)), ramp("#9aa3b6", "#d9dfea"))
                 .add(minus(ellipse(16, 16, 6.8), ellipse(16, 16, 3.6)), ramp("#7d8699", "#b9c0cf"))
                 .dot("#ffffff", (9, 8), (8, 9)))

# Import – a folder, a page sliding in
ICONS["import"] = (Icon()
                   .add(union(rrect(2.5, 5.5, 13.5, 10.5, 1.5), rrect(2.5, 8.5, 29.5, 27.5, 1.5)), ramp("#d7952a", "#f1b84c"))
                   .add(rrect(6.5, 3.5, 24.5, 18.5, 1), ramp("#ffffff", "#ffffff", "#e3e3ec", "#9a9ab2"))
                   .add(union(*[rrect(9.5, 6.5 + 3 * i, 20.5, 6.6 + 3 * i) for i in range(3)]), ramp("#b8b8c8"), lit=False, line=False)
                   .add(poly((2.5, 13.5), (29.5, 13.5), (28.5, 28.5), (3.5, 28.5)), ramp("#f6c048", "#ffdf85"))
                   .add(union(rrect(14.5, 9.5, 17.5, 19.5), poly((10.5, 18.5), (21.5, 18.5), (16, 24.5))), ramp("#3f6fd8", "#7fa4ff")))

# BIOS – a chip
pins = [rrect(9.5 + 4 * i, 2.5, 11.5 + 4 * i, 29.5) for i in range(4)] + [rrect(2.5, 9.5 + 4 * i, 29.5, 11.5 + 4 * i) for i in range(4)]
ICONS["chip"] = (Icon()
                 .add(union(*pins), ramp("#b8bcc8", "#eef0f6"))
                 .add(rrect(6.5, 6.5, 25.5, 25.5, 2), ramp("#3a3a52", "#5c5c7c"))
                 .add(rrect(11.5, 11.5, 20.5, 20.5, 1), ramp("#e0b440", "#fff0a0"))
                 .add(ellipse(9.8, 9.8, 1.3), ramp("#22223a"), lit=False, line=False))

# Trainerpass – an ID card
card = rrect(1.5, 6.5, 30.5, 25.5, 2.5)
photo = rrect(4.5, 13.5, 12.5, 22.5, 1)
ICONS["card"] = (Icon()
                 .add(card, ramp("#f6f4ea", "#ffffff", "#dcd9c8"))
                 .add(both(card, lambda x, y: y < 11.5), ramp("#e23a3a", "#ff7a6e"), line=False)
                 .add(photo, ramp("#8fb3e8", "#c4d8ff"))
                 .add(both(union(ellipse(8.5, 16.5, 2.1), ellipse(8.5, 23, 3.6, 3.2)), photo), ramp("#4e6fa8"), lit=False, line=False)
                 .add(union(rrect(15.5, 14.5, 27.5, 15.5), rrect(15.5, 17.5, 25.5, 18.5), rrect(15.5, 20.5, 22.5, 21.5)), ramp("#a3a3b6"), lit=False, line=False)
                 .dot("#ffd5d0", (4, 8), (5, 8)))

# Rosa theme – a heart
ICONS["heart"] = (Icon()
                  .add(union(ellipse(10, 11, 7.3), ellipse(22, 11, 7.3), poly((3, 13), (29, 13), (16, 28.5))), ramp("#e8475e", "#ff8fa0", "#bf2c45", "#7c1428"))
                  .dot("#ffffff", (7, 7), (8, 7), (6, 8), (6, 9), (7, 8)))

# Savanne theme – a paw print
ICONS["paw"] = (Icon()
                .add(union(ellipse(16, 21.5, 7.4, 6.2), ellipse(5.8, 13.5, 3, 3.6), ellipse(11.8, 7, 3, 3.8), ellipse(20.2, 7, 3, 3.8), ellipse(26.2, 13.5, 3, 3.6)),
                     ramp("#8d5a3b", "#c08a62")))

# Dex – the "caught" mark, small
ICONS["check"] = (Icon(16)
                  .add(ellipse(8, 8, 7), ramp("#3fae4a", "#86e08c"))
                  .add(union(seg(4.3, 8.3, 6.8, 10.8, 1.6), seg(6.8, 10.8, 11.8, 5.6, 1.6)), ramp("#ffffff", "#ffffff", "#ffffff"), lit=False, line=False))

out = os.path.join(ROOT, "Assets", "Icons")
os.makedirs(out, exist_ok=True)
images = {name: icon.render() for name, icon in ICONS.items()}
for name, img in images.items():
    img.save(os.path.join(out, f"{name}.png"))
print(f"{len(images)} icons -> Assets/Icons")

if "--preview" in sys.argv:
    s, cell = 6, 32 * 6 + 10
    sheet = Image.new("RGBA", (len(images) * cell + 10, 2 * (cell + 50) + 10), (255, 255, 255, 255))
    for row, bg in enumerate([(236, 236, 240, 255), (28, 30, 40, 255)]):
        y0 = 10 + row * (cell + 50)
        for i, img in enumerate(images.values()):
            tile = Image.new("RGBA", (32 * s, 32 * s), bg)
            tile.alpha_composite(img.resize((img.width * s, img.height * s), Image.NEAREST))
            sheet.paste(tile, (10 + i * cell, y0))
            small = Image.new("RGBA", (40, 40), bg)
            small.alpha_composite(img, ((40 - img.width) // 2, (40 - img.height) // 2))
            sheet.paste(small, (10 + i * cell, y0 + 32 * s + 4))
    sheet.save(os.path.join(ROOT, "tools", "icons-preview.png"))

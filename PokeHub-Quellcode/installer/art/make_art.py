"""Installer artwork (run once, results are checked in next to this script):
- wizard-<n>.png  the banner on the welcome and finish page, red like MonHub's default theme, at every DPI size
- small-<n>.png   the Poké Ball at the top right of the other pages
Sprites (PMD Sprite Collab, CC BY-NC 4.0) and fonts (OFL) are MonHub's own assets – nothing drawn by hand
except simple shapes (lens, lights, platforms)."""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Assets")
FONT = os.path.join(ASSETS, "Fonts", "PixelifySans.ttf")
FONT_SMALL = os.path.join(ASSETS, "Fonts", "Silkscreen-Regular.ttf")

# idle-animation frame counts (SpeciesData.g.cs) of the Pokémon on the banner: Bisasam, Glumanda, Schiggy, Pikachu
FRAMES = {1: 3, 4: 4, 7: 8, 25: 6}

RED, RED_DARK, RED_DEEP = (200, 32, 42), (160, 20, 30), (122, 14, 21)


def sprite(dex, scale):
    sheet = Image.open(os.path.join(ASSETS, "Species", f"{dex:04}.png")).convert("RGBA")
    w = sheet.width // FRAMES[dex]
    frame = sheet.crop((0, 0, w, sheet.height))
    frame = frame.crop(frame.getbbox())
    return frame.resize((frame.width * scale, frame.height * scale), Image.NEAREST)


def banner(W=328, H=628):
    """Drawn at 200 % (328×628); the other sizes are scaled from it."""
    im = Image.new("RGBA", (W, H), RED + (255,))
    d = ImageDraw.Draw(im)
    # a darker hinge at the right, like the Pokédex's fold
    d.rectangle((W - 22, 0, W, H), fill=RED_DARK)
    d.line((W - 23, 0, W - 23, H), fill=RED_DEEP, width=3)
    # the big blue lens with its white ring, and the three lights
    cx, cy, r = 74, 82, 44
    d.ellipse((cx - r - 9, cy - r - 9, cx + r + 9, cy + r + 9), fill=(250, 250, 250), outline=RED_DEEP, width=4)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(40, 120, 200), outline=(20, 60, 110), width=4)
    d.ellipse((cx - r + 10, cy - r + 10, cx + r - 10, cy + r - 10), fill=(70, 160, 230))
    d.ellipse((cx - 22, cy - 26, cx - 4, cy - 8), fill=(200, 235, 255))
    for i, color in enumerate([(230, 50, 50), (250, 200, 40), (60, 180, 80)]):
        x = 150 + i * 30
        d.ellipse((x, 40, x + 18, 58), fill=color, outline=RED_DEEP, width=3)
    d.line((0, 160, W - 23, 160), fill=RED_DEEP, width=4)
    d.line((W - 140, 160, W - 100, 190), fill=RED_DEEP, width=4)
    d.line((W - 100, 190, W - 23, 190), fill=RED_DEEP, width=4)

    # name and subtitle
    title = ImageFont.truetype(FONT, 58)
    sub = ImageFont.truetype(FONT_SMALL, 17)
    d.text((26, 222), "MonHub", font=title, fill=(122, 14, 21))
    d.text((24, 218), "MonHub", font=title, fill=(255, 255, 255))
    d.text((27, 296), "DEIN SPIELZENTRUM", font=sub, fill=(255, 225, 225))

    # the screen at the bottom: four Pokémon on a light display
    sx0, sy0, sx1, sy1 = 20, 360, W - 44, H - 40
    d.rounded_rectangle((sx0 - 10, sy0 - 10, sx1 + 10, sy1 + 10), radius=14, fill=(228, 226, 223), outline=RED_DEEP, width=4)
    d.rectangle((sx0, sy0, sx1, sy1), fill=(18, 24, 34), outline=(10, 14, 19), width=3)
    for y in range(sy0 + 4, sy1, 4):  # scanlines
        d.line((sx0 + 3, y, sx1 - 3, y), fill=(24, 31, 43))
    spots = [(1, 66, 520), (4, 140, 470), (7, 214, 520), (25, 140, 568)]
    for dex, x, foot in spots:
        mon = sprite(dex, 3)
        shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
        ImageDraw.Draw(shadow).ellipse((x - 26, foot - 6, x + 26, foot + 6), fill=(0, 0, 0, 120))
        im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(2)))
        im.alpha_composite(mon, (x - mon.width // 2, foot - mon.height + 2))
    return im


def save_sizes(img, name, sizes):
    for w, h in sizes:
        out = img.resize((w, h), Image.LANCZOS) if (w, h) != img.size else img
        out.convert("RGB").save(os.path.join(HERE, f"{name}-{w}x{h}.bmp")) if name == "wizard" else out.save(os.path.join(HERE, f"{name}-{w}x{h}.png"))


big = banner()
# 100 / 125 / 150 / 175 / 200 % of the modern wizard's 164×314
save_sizes(big, "wizard", [(164, 314), (205, 393), (246, 471), (287, 550), (328, 628)])

ball = Image.open(os.path.join(ASSETS, "Items", "poke-ball.png")).convert("RGBA")
ball = ball.crop(ball.getbbox())
for n in (55, 69, 83, 96, 110):
    k = max(1, (n - 6) // ball.width)
    b = ball.resize((ball.width * k, ball.height * k), Image.NEAREST)
    canvas = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    canvas.alpha_composite(b, ((n - b.width) // 2, (n - b.height) // 2))
    canvas.save(os.path.join(HERE, f"small-{n}x{n}.png"))
print("ok")

"""
Small assets for MonHub's design (the menu icons are MonHub's own, tools/build_icons.py) – the pictures from PMD Sprite Collab (https://sprites.pmdcollab.org/, CC BY-NC 4.0,
credits in CREDITS.md via tools/build_credits.py), the fonts from Google Fonts (SIL Open Font License):
 - Porygon's faces for the tour guide                     -> Assets/Portraits/0137-<Emotion>.png
 - the app icon (Porygon)                                 -> monhub.ico
 - pixel fonts                                            -> Assets/Fonts

Run:  python tools/build_assets.py
"""
import io, os, urllib.request
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORTRAITS = "https://raw.githubusercontent.com/PMDCollab/SpriteCollab/master/portrait"
FONTS = "https://raw.githubusercontent.com/google/fonts/main/ofl"

GUIDE = ("0137", ["Normal", "Happy", "Joyous", "Inspired", "Surprised", "Determined"])  # Porygon
WANTED_FONTS = [("silkscreen", ["Silkscreen-Regular.ttf", "Silkscreen-Bold.ttf", "OFL.txt"]),
                ("pixelifysans", ["PixelifySans[wght].ttf", "OFL.txt"]),
                ("pressstart2p", ["PressStart2P-Regular.ttf", "OFL.txt"])]


def get(url):
    with urllib.request.urlopen(url.replace("[", "%5B").replace("]", "%5D"), timeout=60) as r:
        return r.read()


def portrait(dex, emotion):
    return Image.open(io.BytesIO(get(f"{PORTRAITS}/{dex}/{emotion}.png"))).convert("RGBA")


faces = os.path.join(ROOT, "Assets", "Portraits")
os.makedirs(faces, exist_ok=True)
for emotion in GUIDE[1]:
    portrait(GUIDE[0], emotion).save(os.path.join(faces, f"{GUIDE[0]}-{emotion}.png"))
    print(f"Guide {GUIDE[0]}/{emotion}")

# app icon: Porygon's face, sharp pixels at every size Windows asks for
face = portrait(GUIDE[0], "Normal")
big = face.resize((256, 256), Image.NEAREST)
big.save(os.path.join(ROOT, "monhub.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("App-Symbol monhub.ico (Porygon)")

fonts = os.path.join(ROOT, "Assets", "Fonts")
os.makedirs(fonts, exist_ok=True)
for family, files in WANTED_FONTS:
    for name in files:
        target = os.path.join(fonts, f"{family}-OFL.txt" if name == "OFL.txt" else name.replace("[wght]", ""))
        with open(target, "wb") as f:
            f.write(get(f"{FONTS}/{family}/{name}"))
        print(f"Font  {family}/{name}")

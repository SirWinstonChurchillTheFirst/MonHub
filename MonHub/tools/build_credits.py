"""
CREDITS.md: who made every picture MonHub shows. All of them come from PMD Sprite Collab (CC BY-NC 4.0, credit required);
the artists of each Pokémon's sprite and portrait are listed in the collab's credits.txt files, their names in
credit_names.txt. Run after the asset scripts (they decide which pictures are used):

    python tools/build_credits.py
"""
import os, re, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = "https://raw.githubusercontent.com/PMDCollab/SpriteCollab/master"


def get(url):
    try:
        with urllib.request.urlopen(url, timeout=60) as r:
            return r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError:
        return ""


def species_name(dex):
    data = open(os.path.join(ROOT, "SpeciesData.g.cs"), encoding="utf-8").read()
    names = re.search(r"EnglishNames\s*=\s*\[(.*?)\];", data, re.S)
    if not names:
        return f"#{dex}"
    items = re.findall(r'"([^"]*)"', names.group(1))
    return items[dex - 1] if 0 < dex <= len(items) else f"#{dex}"


# which pictures are used
sprites = {int(f[:4]) for f in os.listdir(os.path.join(ROOT, "Assets", "Species")) if f[:4].isdigit()}
sprites |= {int(f[:4]) for f in os.listdir(os.path.join(ROOT, "Assets", "Sprites")) if f[:4].isdigit() and "portrait" not in f}
portraits = {int(f[:4]) for f in os.listdir(os.path.join(ROOT, "Assets", "Sprites")) if "portrait" in f}
portraits |= {int(f[:4]) for f in os.listdir(os.path.join(ROOT, "Assets", "Portraits")) if f[:4].isdigit()}
src =open(os.path.join(ROOT, "tools", "build_assets.py"), encoding="utf-8").read()
portraits |= {int(d) for d in re.findall(r'\("(\d{4})", "[A-Za-z]+"\)', src)}

# artist names: credit id -> shown name (+ contact)
names = {}
for line in get(f"{BASE}/credit_names.txt").splitlines()[1:]:
    parts = line.split("\t")
    if len(parts) >= 2:
        names[parts[1].strip()] = (parts[0].strip(), parts[2].strip() if len(parts) > 2 else "")


def credits(kind, dex):
    seen = []
    for line in get(f"{BASE}/{kind}/{dex:04}/credits.txt").splitlines():
        parts = line.split("\t")
        if len(parts) >= 2:
            who = names.get(parts[1].strip(), (parts[1].strip(), ""))[0].replace("|", "/")
            if who and who not in seen:
                seen.append(who)
    return seen


with ThreadPoolExecutor(16) as pool:
    sprite_credits = dict(zip(sorted(sprites), pool.map(lambda d: credits("sprite", d), sorted(sprites))))
    portrait_credits = dict(zip(sorted(portraits), pool.map(lambda d: credits("portrait", d), sorted(portraits))))

everyone = sorted({n for v in list(sprite_credits.values()) + list(portrait_credits.values()) for n in v}, key=str.casefold)
lines = [
    "# Credits",
    "",
    "Every Pokémon picture in MonHub – sprites, portraits and Porygon in the tour – comes from",
    "**PMD Sprite Collab** (https://sprites.pmdcollab.org, https://github.com/PMDCollab/SpriteCollab).",
    "Its artists released them under **CC BY-NC 4.0**",
    "(https://creativecommons.org/licenses/by-nc/4.0/): sharing and adapting allowed, with attribution,",
    "non-commercial. MonHub cuts the pictures down to one facing direction and recolours them for the Retro look.",
    "",
    "Pictures marked **CHUNSOFT** are original graphics from *Pokémon Mystery Dungeon*",
    "© Nintendo / Creatures / GAME FREAK / Spike Chunsoft – they are not CC licensed.",
    "",
    f"## All {len(everyone)} contributors",
    "",
    ", ".join(everyone),
    "",
    "## Per Pokémon",
    "",
    "| # | Pokémon | Sprite | Portrait |",
    "|---|---|---|---|",
]
for dex in sorted(sprites | portraits):
    s = ", ".join(sprite_credits.get(dex, [])) if dex in sprites else ""
    p = ", ".join(portrait_credits.get(dex, [])) if dex in portraits else ""
    lines.append(f"| {dex:03} | {species_name(dex)} | {s} | {p} |")
lines += [
    "",
    "## Fonts",
    "",
    "Silkscreen (Jason Kottke), Pixelify Sans (Stefie Justprince), Press Start 2P (CodeMan38) – SIL Open Font License 1.1,",
    "via Google Fonts (https://fonts.google.com). The license texts come with the fonts.",
    "",
    "## Icons",
    "",
    "The menu icons (die, cartridge, gear …) are drawn by MonHub itself (tools/build_icons.py), GPL-3.0.",
    "",
    "## Data",
    "",
    "Pokémon names (English and German) and the route lists of the Nuzlocke tracker: PokeAPI (https://pokeapi.co).",
    "",
]
open(os.path.join(ROOT, "CREDITS.md"), "w", encoding="utf-8").write("\n".join(lines))
print(f"CREDITS.md: {len(sprites)} Sprites, {len(portraits)} Porträts, {len(everyone)} Mitwirkende")

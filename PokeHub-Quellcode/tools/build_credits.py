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
    names = re.search(r"GermanNames\s*=\s*\[(.*?)\];", data, re.S)
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
    "Alle Pokémon-Bilder in MonHub – Sprites, Porträts und Porygon in der Tour – stammen von",
    "**PMD Sprite Collab** (https://sprites.pmdcollab.org, https://github.com/PMDCollab/SpriteCollab).",
    "Die Künstlerinnen und Künstler haben sie unter **CC BY-NC 4.0** freigegeben",
    "(https://creativecommons.org/licenses/by-nc/4.0/): Weitergabe und Bearbeitung erlaubt, mit Namensnennung,",
    "nicht kommerziell. MonHub verkleinert die Bilder auf eine Blickrichtung und färbt sie für den Retro-Look um.",
    "",
    "Mit **CHUNSOFT** gekennzeichnete Bilder sind Originalgrafiken aus *Pokémon Mystery Dungeon*",
    "© Nintendo / Creatures / GAME FREAK / Spike Chunsoft – sie sind nicht CC-lizenziert.",
    "",
    f"## Alle {len(everyone)} Mitwirkenden",
    "",
    ", ".join(everyone),
    "",
    "## Pro Pokémon",
    "",
    "| # | Pokémon | Sprite | Porträt |",
    "|---|---|---|---|",
]
for dex in sorted(sprites | portraits):
    s = ", ".join(sprite_credits.get(dex, [])) if dex in sprites else ""
    p = ", ".join(portrait_credits.get(dex, [])) if dex in portraits else ""
    lines.append(f"| {dex:03} | {species_name(dex)} | {s} | {p} |")
lines += [
    "",
    "## Schriften",
    "",
    "Silkscreen (Jason Kottke), Pixelify Sans (Stefie Justprince), Press Start 2P (CodeMan38) – SIL Open Font License 1.1,",
    "über Google Fonts (https://fonts.google.com). Die Lizenztexte liegen bei den Schriften.",
    "",
    "## Symbole",
    "",
    "Die Menü-Symbole (Würfel, Modul, Zahnrad …) hat MonHub selbst gezeichnet (tools/build_icons.py), GPL-3.0.",
    "",
    "## Daten",
    "",
    "Deutsche Pokémon-Namen: PokeAPI (https://pokeapi.co). Routenlisten für den Nuzlocke-Tracker: PokeAPI.",
    "",
]
open(os.path.join(ROOT, "CREDITS.md"), "w", encoding="utf-8").write("\n".join(lines))
print(f"CREDITS.md: {len(sprites)} Sprites, {len(portraits)} Porträts, {len(everyone)} Mitwirkende")

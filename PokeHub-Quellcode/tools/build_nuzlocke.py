"""
Route lists for the Nuzlocke tracker: every place with wild Pokémon per game (PokeAPI data, German names), in story order.
The order is hand-made per region below; places the order doesn't know are appended (and printed) so nothing is lost.
Output: Assets/Nuzlocke/routes.json   {"firered": [{"id": "kanto-route-1", "name": "Route 1"}, ...], ...}

Run:  python tools/build_nuzlocke.py   (downloads the PokeAPI CSVs once into tools/.pokeapi)
"""
import csv, json, os, urllib.request, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "tools", ".pokeapi")
BASE = "https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv/"
GERMAN, ENGLISH = "6", "9"

KANTO_1 = """pallet-town kanto-route-1 viridian-city kanto-route-22 kanto-route-2 viridian-forest pewter-city kanto-route-3 mt-moon
kanto-route-4 cerulean-city kanto-route-24 kanto-route-25 kanto-route-5 kanto-underground-path kanto-route-6 vermilion-city ss-anne
kanto-route-11 digletts-cave kanto-route-9 kanto-route-10 rock-tunnel kanto-power-plant lavender-town kanto-route-8 kanto-route-7
celadon-city pokemon-tower saffron-city kanto-route-12 kanto-route-13 kanto-route-14 kanto-route-15 kanto-route-16 kanto-route-17
kanto-route-18 fuchsia-city kanto-safari-zone kanto-sea-route-19 kanto-sea-route-20 seafoam-islands cinnabar-island pokemon-mansion"""
KANTO_1_END = "kanto-sea-route-21 kanto-route-23 kanto-victory-road-2 cerulean-cave"
SEVII = """one-island treasure-beach kindle-road mt-ember cape-brink three-isle-port bond-bridge berry-forest"""
SEVII_POST = """four-island icefall-cave five-island five-isle-meadow memorial-pillar water-labyrinth resort-gorgeous lost-cave
water-path ruin-valley green-path outcast-island pattern-bush kanto-altering-cave canyon-entrance sevault-canyon tanoby-ruins
monean-chamber liptoo-chamber weepth-chamber dilford-chamber scufib-chamber rixy-chamber viapos-chamber trainer-tower"""

JOHTO = """new-bark-town johto-route-29 cherrygrove-city johto-route-30 johto-route-31 violet-city sprout-tower dark-cave johto-route-32
ruins-of-alph union-cave johto-route-33 azalea-town slowpoke-well ilex-forest johto-route-34 goldenrod-city johto-route-35
national-park johto-route-36 johto-route-37 ecruteak-city burned-tower johto-route-38 johto-route-39 olivine-city
johto-sea-route-40 johto-sea-route-41 cianwood-city johto-route-47 johto-route-48 johto-safari-zone whirl-islands johto-route-42
mt-mortar johto-route-43 lake-of-rage team-rocket-hq bell-tower johto-route-44 ice-path blackthorn-city dragons-den johto-route-45
johto-route-46 kanto-route-27 tohjo-falls kanto-route-26 kanto-victory-road-1
vermilion-city kanto-route-6 kanto-route-11 digletts-cave saffron-city kanto-route-5 cerulean-city kanto-route-24 kanto-route-25
kanto-route-9 kanto-route-10 rock-tunnel kanto-power-plant kanto-route-8 kanto-route-7 celadon-city kanto-route-16 kanto-route-17
kanto-route-18 fuchsia-city kanto-route-15 kanto-route-14 kanto-route-13 kanto-route-12 kanto-sea-route-19 kanto-sea-route-20
seafoam-islands cinnabar-island kanto-sea-route-21 pallet-town kanto-route-1 viridian-city kanto-route-22 kanto-route-2
viridian-forest pewter-city kanto-route-3 mt-moon kanto-route-4 kanto-route-28 mt-silver cerulean-cave embedded-tower sinjoh-ruins"""

HOENN = """littleroot-town hoenn-route-101 hoenn-route-103 hoenn-route-102 petalburg-city hoenn-route-104 petalburg-woods rustboro-city
hoenn-route-116 rusturf-tunnel dewford-town granite-cave hoenn-route-106 hoenn-route-109 slateport-city hoenn-route-110
hoenn-route-117 hoenn-route-111 hoenn-route-112 fiery-path hoenn-route-113 hoenn-route-114 meteor-falls hoenn-route-115
jagged-pass lavaridge-town mirage-tower hoenn-route-105 hoenn-route-107 hoenn-route-108 abandoned-ship new-mauville
hoenn-route-118 hoenn-route-119 fortree-city hoenn-route-120 hoenn-route-121 hoenn-safari-zone lilycove-city hoenn-route-122
mt-pyre hoenn-route-123 magma-hideout team-magma-hideout team-aqua-hideout hoenn-route-124 mossdeep-city hoenn-route-125
shoal-cave hoenn-route-127 hoenn-route-128 seafloor-cavern hoenn-route-126 sootopolis-city cave-of-origin hoenn-route-129
hoenn-route-130 hoenn-route-131 pacifidlog-town hoenn-route-132 hoenn-route-133 hoenn-route-134 sky-pillar ever-grande-city
hoenn-victory-road desert-underpass hoenn-altering-cave artisan-cave marine-cave terra-cave island-cave ancient-tomb desert-ruins
southern-island mirage-island hoenn-battle-frontier faraway-island birth-island navel-rock"""

SINNOH = """twinleaf-town sinnoh-route-201 lake-verity sinnoh-route-202 sinnoh-route-203 oreburgh-gate oreburgh-city oreburgh-mine
sinnoh-route-204 ravaged-path floaroma-meadow valley-windworks sinnoh-route-205 eterna-forest old-chateau eterna-city
sinnoh-route-211 mt-coronet sinnoh-route-206 wayward-cave sinnoh-route-207 sinnoh-route-208 hearthome-city sinnoh-route-209
lost-tower solaceon-ruins sinnoh-route-210 sinnoh-route-215 veilstone-city sinnoh-route-214 maniac-tunnel ruin-maniac-cave
valor-lakefront sinnoh-route-213 pastoria-city great-marsh sinnoh-route-212 trophy-garden celestic-town fuego-ironworks
sinnoh-route-218 canalave-city iron-island lake-valor acuity-lakefront sinnoh-route-216 sinnoh-route-217 lake-acuity
snowpoint-city snowpoint-temple spear-pillar distortion-world sinnoh-route-219 sinnoh-sea-route-220 sinnoh-route-221
sinnoh-route-222 sunyshore-city sinnoh-sea-route-223 sinnoh-pokemon-league sinnoh-victory-road sinnoh-route-224 sinnoh-route-225
sinnoh-sea-route-226 sinnoh-route-227 stark-mountain sinnoh-route-228 sinnoh-route-229 sinnoh-sea-route-230 resort-area
turnback-cave sendoff-spring flower-paradise newmoon-island sinnoh-hall-of-origin-1"""

UNOVA = """nuvema-town unova-route-1 accumula-town unova-route-2 striaton-city dreamyard unova-route-3 wellspring-cave nacrene-city
pinwheel-forest castelia-city unova-route-4 desert-resort relic-castle unova-route-5 unova-route-16 lostlorn-forest
driftveil-drawbridge driftveil-city cold-storage unova-route-6 mistralton-cave chargestone-cave unova-route-7 celestial-tower
twist-mountain icirrus-city dragonspiral-tower unova-route-8 moor-of-icirrus unova-route-9 unova-route-10 unova-victory-road
ns-castle unova-route-11 village-bridge unova-route-12 unova-route-13 giant-chasm undella-town undella-bay unova-route-14
abundant-shrine unova-route-15 marvelous-bridge unova-route-17 unova-route-18 p2-laboratory challengers-cave guidance-chamber
trial-chamber liberty-garden"""

UNOVA_2 = """aspertia-city unova-route-19 floccesy-town unova-route-20 floccesy-ranch virbank-city virbank-complex castelia-city
castelia-sewers unova-route-4 desert-resort relic-castle unova-route-16 lostlorn-forest unova-route-5 driftveil-drawbridge
driftveil-city relic-passage unova-route-6 chargestone-cave mistralton-cave unova-route-7 celestial-tower reversal-mountain
strange-house undella-town undella-bay unova-route-13 unova-route-12 village-bridge unova-route-11 unova-route-9 seaside-cave
humilau-city unova-route-21 unova-route-22 giant-chasm unova-route-23 unova-victory-road unova-victory-road-2 twist-mountain
icirrus-city dragonspiral-tower unova-route-8 moor-of-icirrus unova-route-14 abundant-shrine unova-route-15 marvelous-bridge
unova-route-17 unova-route-18 p2-laboratory nature-sanctuary clay-tunnel underground-ruins rocky-mountain-room glacier-room
iron-room wellspring-cave striaton-city dreamyard unova-route-3 unova-route-2 unova-route-1 accumula-town nacrene-city
pinwheel-forest"""

KALOS = """aquacorde-town kalos-route-2 santalune-forest kalos-route-3 kalos-route-22 kalos-route-4 lumiose-city kalos-route-5
kalos-route-6 parfum-palace kalos-route-7 connecting-cave kalos-route-8 ambrette-town glittering-cave kalos-route-9 cyllage-city
kalos-route-10 kalos-route-11 reflection-cave shalour-city tower-of-mastery kalos-route-12 azure-bay kalos-route-13
kalos-power-plant kalos-route-14 laverre-city kalos-route-15 lost-hotel kalos-route-16 kalos-route-17 frost-cavern
kalos-route-18 couriway-town kalos-route-19 kalos-route-20 pokemon-village kalos-route-21 kalos-victory-road terminus-cave
sea-spirits-den unknown-dungeon friend-safari kalos-berry-fields"""

HOENN_ORAS = """littleroot-town hoenn-route-101 oldale-town hoenn-route-103 hoenn-route-102 petalburg-city hoenn-route-104 petalburg-woods
rustboro-city hoenn-route-116 rusturf-tunnel dewford-town granite-cave hoenn-route-106 hoenn-route-107 hoenn-route-109
slateport-city hoenn-route-110 hoenn-route-117 verdanturf-town hoenn-route-111 hoenn-route-112 fiery-path hoenn-route-113
fallarbor-town hoenn-route-114 meteor-falls hoenn-route-115 jagged-pass lavaridge-town hoenn-route-105 hoenn-route-108
abandoned-ship new-mauville hoenn-route-118 hoenn-route-119 fortree-city hoenn-route-120 hoenn-route-121 hoenn-safari-zone
lilycove-city hoenn-route-122 mt-pyre hoenn-route-123 team-magma-hideout team-aqua-hideout hoenn-route-124 mossdeep-city
hoenn-route-125 shoal-cave mossdeep-space-center hoenn-route-127 hoenn-route-128 seafloor-cavern hoenn-route-126
sootopolis-city cave-of-origin sky-pillar hoenn-route-129 hoenn-route-130 hoenn-route-131 pacifidlog-town hoenn-route-132
hoenn-route-133 hoenn-route-134 ever-grande-city hoenn-victory-road sea-mauville soaring-in-the-sky southern-island
scorched-slab battle-resort desert-ruins island-cave ancient-tomb crescent-isle mirage-spot-forest mirage-spot-cave
mirage-spot-island mirage-spot-mountain fabled-cave gnarled-den nameless-cavern pathless-plain trackless-forest"""
# PokeAPI lacks some ORAS places (e.g. the sea routes) – they are in the game, so they're listed anyway
ORAS_EXTRA = """hoenn-route-105 hoenn-route-106 hoenn-route-107 hoenn-route-108 hoenn-route-109 hoenn-route-122 hoenn-route-124
hoenn-route-125 hoenn-route-126 hoenn-route-127 hoenn-route-128 hoenn-route-129 hoenn-route-130 hoenn-route-131 hoenn-route-132
hoenn-route-133 hoenn-route-134 petalburg-city dewford-town sootopolis-city pacifidlog-town ever-grande-city abandoned-ship"""

ALOLA = """iki-town alola-route-1 trainers-school mahalo-trail hauoli-city alola-route-2 hauoli-cemetery verdant-cavern alola-route-3
melemele-meadow seaward-cave kalae-bay ten-carat-hill melemele-sea heahea-city heahea-beach alola-route-4 paniola-town
paniola-ranch alola-route-5 brooklet-hill alola-route-6 royal-avenue alola-route-7 wela-volcano-park alola-route-8
lush-jungle digletts-tunnel alola-route-9 konikoni-city memorial-hill akala-outskirts hano-beach aether-paradise
malie-city malie-garden alola-route-10 mount-hokulani alola-route-11 alola-route-12 blush-mountain dividing-peak-tunnel
alola-route-13 ulaula-beach haina-desert tapu-village alola-route-14 alola-route-15 alola-route-16 ulaula-meadow alola-route-17
thrifty-megamart seafolk-village poni-wilds ancient-poni-path poni-breaker-coast exeggutor-island vast-poni-canyon
altar-of-the-sunne altar-of-the-moone lake-of-the-sunne lake-of-the-moone ultra-megalopolis mount-lanakila
poni-grove poni-plains poni-meadow poni-coast sandy-cave poni-gauntlet resolution-cave ruins-of-conflict ruins-of-life
ruins-of-abundance ruins-of-hope secluded-shore team-rockets-castle ultra-space-wilds ultra-space poke-pelago alola-berry-fields"""

STORY = {
    "red": KANTO_1 + " " + KANTO_1_END, "blue": KANTO_1 + " " + KANTO_1_END, "yellow": KANTO_1 + " " + KANTO_1_END,
    "firered": KANTO_1 + " " + SEVII + " " + KANTO_1_END + " " + SEVII_POST + " navel-rock birth-island",
    "leafgreen": KANTO_1 + " " + SEVII + " " + KANTO_1_END + " " + SEVII_POST + " navel-rock birth-island",
    "gold": JOHTO, "silver": JOHTO, "crystal": JOHTO, "heartgold": JOHTO, "soulsilver": JOHTO,
    "ruby": HOENN, "sapphire": HOENN, "emerald": HOENN,
    "diamond": SINNOH, "pearl": SINNOH, "platinum": SINNOH,
    "black": UNOVA, "white": UNOVA, "black-2": UNOVA_2, "white-2": UNOVA_2,
    "x": KALOS, "y": KALOS, "omega-ruby": HOENN_ORAS, "alpha-sapphire": HOENN_ORAS,
    "sun": ALOLA, "moon": ALOLA, "ultra-sun": ALOLA, "ultra-moon": ALOLA,
}
EXTRA = {"omega-ruby": ORAS_EXTRA, "alpha-sapphire": ORAS_EXTRA}
# not places you'd catch your route Pokémon (shop/centre helpers, roaming legends, data oddities)
SKIP = ("roaming-", "pokecenter", "pokemart", "unknown-all-", "safari-zone-gate", "team-flare-secret-hq")


def table(name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name + ".csv")
    if not os.path.exists(path):
        urllib.request.urlretrieve(BASE + name + ".csv", path)
    return list(csv.DictReader(open(path, encoding="utf-8")))


versions = {r["id"]: r["identifier"] for r in table("versions")}
locations = {r["id"]: r["identifier"] for r in table("locations")}
area_location = {r["id"]: r["location_id"] for r in table("location_areas")}
names = collections.defaultdict(dict)
for r in table("location_names"):
    names[r["location_id"]][r["local_language_id"]] = r["name"]
location_id = {ident: lid for lid, ident in locations.items()}

places = collections.defaultdict(set)
for r in table("encounters"):
    places[versions[r["version_id"]]].add(locations[area_location[r["location_area_id"]]])

result = {}
for version, story in STORY.items():
    have = {p for p in places[version] | set(EXTRA.get(version, "").split()) if not any(s in p for s in SKIP) and p in location_id}
    order = [p for p in story.split() if p in have]
    missing = sorted(have - set(order))
    if missing:
        print(f"{version}: nicht einsortiert: {' '.join(missing)}")
    entries = [{"id": "starter", "name": "Starter"}]
    for p in order + missing:
        n = names[location_id[p]]
        entries.append({"id": p, "name": n.get(GERMAN) or n.get(ENGLISH) or p.replace("-", " ").title()})
    result[version] = entries

out = os.path.join(ROOT, "Assets", "Nuzlocke", "routes.json")
os.makedirs(os.path.dirname(out), exist_ok=True)
json.dump(result, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
print("geschrieben:", out, {v: len(e) for v, e in result.items()})

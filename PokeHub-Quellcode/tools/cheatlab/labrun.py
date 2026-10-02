import json, os, struct, subprocess, sys, glob
sys.path.insert(0, ".")
from lab import boot, eval_code, rd, SAVE_FOR, MARK

# Known candidate codes per family (DE ones are verified; others are guesses to be checked)
FAMILY = {
 "ADAE": ("DP", 0xDAC), "APAE": ("DP", 0xDAC), "CPUD": ("PT", 0xB60), "CPUE": ("PT", 0xB60),
 "IPKD": ("HGSS", 0xB74), "IPKE": ("HGSS", 0xB74), "IPGD": ("HGSS", 0xB74), "IPGE": ("HGSS", 0xB74),
 "IRBD": ("BW", 0x19494), "IRBO": ("BW", 0x19494), "IRAD": ("BW", 0x19494), "IRAO": ("BW", 0x19494),
 "IRED": ("B2W2", 0x194F8), "IREO": ("B2W2", 0x194F8), "IRDD": ("B2W2", 0x194F8), "IRDO": ("B2W2", 0x194F8),
}
CANDIDATES = {
 "DP": ["B21C4D28 00000000 B0000004 00000000 00000DAC 03E70032"],
 "PT": ["B2101EE0 00000000 00000B60 03E70032", "B2101D40 00000000 00000B60 03E70032"],
 "HGSS": ["62111860 00000000 B2111860 00000000 00000B74 03E70032", "62111880 00000000 B2111880 00000000 00000B74 03E70032"],
 "BW": ["B2000024 00000000 00019494 03E70032"],
 "B2W2": ["B2000024 00000000 000194F8 03E70032"],
}

def static_single(ram, m, off):
    want = m - off
    return [0x02000000 + i for i in range(0, 0x200000, 4) if struct.unpack_from("<I", ram, i)[0] == want]

def static_double(ram, m, off):
    want = m - off  # value at [V1 + 4]
    firsts = [0x02000000 + i - 4 for i in range(4, len(ram), 4) if struct.unpack_from("<I", ram, i)[0] == want]
    out = []
    for v1 in firsts:
        out += [(0x02000000 + i, v1) for i in range(0, 0x200000, 4) if struct.unpack_from("<I", ram, i)[0] == v1]
    return out

def analyse(rom):
    gc = os.path.basename(rom)[:4]; fam, off = FAMILY[gc]
    ingame = fam in ("BW", "B2W2") or os.environ.get("INGAME") == "1"
    rams = boot(rom, f"saves/marker_{SAVE_FOR[gc]}.sav", [4800, 6600] if ingame else [1800, 3000], press=ingame)
    row = {"rom": os.path.basename(rom), "family": fam}
    marks = [[0x02000000 + i for i in range(0, len(r) - 12, 4) if r[i:i+12] == MARK] for r in rams]
    row["marks"] = [[hex(x) for x in m] for m in marks]
    row["works"] = [c for c in CANDIDATES[fam] if all(eval_code(r, c) in m for r, m in zip(rams, marks))]
    # pointer search on the later dump, then confirm on the earlier one
    found = set()
    for m in marks[1]:
        if fam == "DP":
            for a, v1 in static_double(rams[1], m, off):
                c = f"B{a & 0x0FFFFFFF:07X} 00000000 B0000004 00000000 {off:08X} 03E70032"
                if eval_code(rams[0], c) in marks[0]: found.add(c)
        else:
            for a in static_single(rams[1], m, off):
                c = f"B{a & 0x0FFFFFFF:07X} 00000000 {off:08X} 03E70032"
                if eval_code(rams[0], c) in marks[0]: found.add(c)
    row["found"] = sorted(found)
    return row

if len(sys.argv) > 1 and "*" not in sys.argv[1]:  # worker: one ROM per process (the emulator library can't be restarted in-process)
    json.dump(analyse(sys.argv[1]), open(f"results/{os.path.basename(sys.argv[1])}.json", "w"), indent=1)
else:
    rows = []
    for rom in sorted(glob.glob(sys.argv[1] if len(sys.argv) > 1 and "*" in sys.argv[1] else "roms/*_v*.nds")):
        subprocess.run([sys.executable, __file__, rom], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            row = json.load(open(f"results/{os.path.basename(rom)}.json"))
        except Exception as e:
            print(f"{os.path.basename(rom):14} FEHLER {e}", flush=True); continue
        rows.append(row)
        print(f'{row["rom"]:14} {row["family"]:5} marks={len(row["marks"][1])} works={row["works"]} found={row["found"][:3]}', flush=True)
    json.dump(rows, open("results/all.json", "w"), indent=1)

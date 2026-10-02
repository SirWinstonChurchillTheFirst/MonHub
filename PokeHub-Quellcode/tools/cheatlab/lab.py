import sys, struct, os, json
from desmume.emulator import DeSmuME
MARK = struct.pack("<HHHHHH", 17, 123, 26, 45, 25, 89)
SAVE_FOR = {"ADAE":"D","APAE":"P","CPUD":"Pt","CPUE":"Pt","IPKD":"HG","IPKE":"HG","IPGD":"SS","IPGE":"SS",
            "IRBD":"B","IRBO":"B","IRAD":"W","IRAO":"W","IRED":"B2","IREO":"B2","IRDD":"W2","IRDO":"W2"}

def boot(rom, save, frames, press=False):
    from desmume.controls import keymask, Keys
    emu = DeSmuME(); emu.open(rom); emu.backup.import_file(save); emu.reset(); emu.volume_set(0)
    dumps = []
    for f in range(1, max(frames) + 1):
        if press and f >= 1500 and f % 60 == 0:  # title screen -> "Continue" -> into the game
            emu.input.keypad_add_key(keymask(Keys.KEY_START if (f // 60) % 2 else Keys.KEY_A))
        if press and f >= 1500 and f % 60 == 6:
            emu.input.keypad_rm_key(keymask(Keys.KEY_START)); emu.input.keypad_rm_key(keymask(Keys.KEY_A))
        emu.cycle(with_joystick=False)
        if f in frames: dumps.append(bytes(emu.memory.unsigned[0x02000000:0x02400000]))
    emu.destroy()
    return dumps

def rd(ram, a): return struct.unpack_from("<I", ram, a - 0x02000000)[0] if 0x02000000 <= a < 0x02400000 - 4 else None

def eval_code(ram, code):
    """Follows the pointer part of an AR code; returns the address of the 0-type (32-bit) write."""
    offset = 0
    for line in code.split():
        pass
    words = code.split()
    for i in range(0, len(words), 2):
        a, v = int(words[i], 16), int(words[i + 1], 16)
        t = a >> 28
        if t == 0x9 or t == 0xD: continue
        if t == 0x6:  # if [a+offset] != v
            if rd(ram, (a & 0x0FFFFFFF) + offset) == v: return None
        elif t == 0xB:
            offset = rd(ram, (a & 0x0FFFFFFF) + offset) or 0
        elif t == 0x0:
            return (a & 0x0FFFFFFF) + offset
    return None

if __name__ == "__main__":
    rom = sys.argv[1]
    codes = json.loads(sys.argv[2]) if len(sys.argv) > 2 else []
    gc = os.path.basename(rom)[:4]
    dumps = boot(rom, f"saves/marker_{SAVE_FOR[gc]}.sav", [1800, 3000])
    result = {"rom": os.path.basename(rom)}
    for idx, ram in enumerate(dumps):
        marks = [0x02000000 + i for i in range(0, len(ram) - 12, 4) if ram[i:i+12] == MARK]
        result[f"marks_{idx}"] = [hex(m) for m in marks]
        result[f"codes_{idx}"] = {c: (hex(t) if (t := eval_code(ram, c)) else None) for c in codes}
        # static pointer candidates (address below 0x02200000) pointing shortly before a marker
        cands = []
        for m in marks:
            for i in range(0, 0x200000, 4):
                v = struct.unpack_from("<I", ram, i)[0]
                if 0 < m - v <= 0x30000 and v >= 0x02000000:
                    cands.append((hex(0x02000000 + i), hex(m - v)))
        result[f"ptrs_{idx}"] = cands[:40]
    os.makedirs("results", exist_ok=True)
    open(f"results/{os.path.basename(rom)}.json", "w").write(json.dumps(result, indent=1))

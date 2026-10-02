import sys, struct
from desmume.emulator import DeSmuME
from desmume.controls import keymask, Keys
rom, tag, total = sys.argv[1], sys.argv[2], int(sys.argv[3])
NAME = "AAAAAAA".encode("utf-16-le")
emu = DeSmuME(); emu.open(rom); emu.volume_set(0)
log = open(f"results/newgame_{tag}.txt", "w")
held = None
for f in range(1, total + 1):
    if held and f % 20 == 5:
        emu.input.keypad_rm_key(keymask(held)); held = None
    if f >= 900 and f % 20 == 0:
        held = Keys.KEY_START if f % 400 == 0 else Keys.KEY_A
        emu.input.keypad_add_key(keymask(held))
    emu.cycle(with_joystick=False)
    if f % 2000 == 0:
        ram = bytes(emu.memory.unsigned[0x02000000:0x02400000])
        v = struct.unpack_from("<I", ram, 0x24)[0]
        names = [0x02000000 + i for i in range(0, len(ram) - 14, 2) if ram[i:i+14] == NAME]
        log.write(f"frame {f}: ptr={v:#x} names={[hex(n) for n in names]} rel={[hex(n - v) for n in names]}\n"); log.flush()
        emu.screenshot().save(f"results/newgame_{tag}_{f}.png")

def utf16_at(ram, addr, n=8):
    raw = ram[addr - 0x02000000: addr - 0x02000000 + 2 * n]
    chars = []
    for i in range(0, len(raw), 2):
        c = struct.unpack_from("<H", raw, i)[0]
        if c in (0xFFFF, 0): break
        chars.append(chr(c))
    return "".join(chars), raw.hex()
ram = bytes(emu.memory.unsigned[0x02000000:0x02400000])
v = struct.unpack_from("<I", ram, 0x24)[0]
for label, delta in (("BW-Code (0x8BC)", 0x8BC), ("B2W2-Struktur (0x920)", 0x920)):
    log.write(f"{label}: name@{v + delta + 0x19404:#x} = {utf16_at(ram, v + delta + 0x19404)}\n")
log.close()

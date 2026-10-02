import sys, struct
from desmume.emulator import DeSmuME
from desmume.controls import keymask, Keys
MARK = struct.pack("<HHHHHH", 17, 123, 26, 45, 25, 89)
rom, save = sys.argv[1], sys.argv[2]
emu = DeSmuME(); emu.open(rom); emu.backup.import_file(save); emu.reset(); emu.volume_set(0)
log = open(sys.argv[3], "w")
for f in range(1, 9001):
    # from frame 1500 on: tap START / A alternately every 60 frames (title screen -> "Continue" -> game)
    if f >= 1500 and f % 60 == 0:
        emu.input.keypad_add_key(keymask(Keys.KEY_START if (f // 60) % 2 else Keys.KEY_A))
    if f >= 1500 and f % 60 == 6:
        emu.input.keypad_rm_key(keymask(Keys.KEY_START)); emu.input.keypad_rm_key(keymask(Keys.KEY_A))
    emu.cycle(with_joystick=False)
    if f % 600 == 0:
        ram = bytes(emu.memory.unsigned[0x02000000:0x02400000])
        marks = [hex(0x02000000 + i) for i in range(0, len(ram) - 12, 4) if ram[i:i+12] == MARK]
        p = struct.unpack_from("<I", ram, 0x24)[0]
        log.write(f"frame {f}: marks={marks} [0x02000024]={p:#x}\n"); log.flush()
emu.screenshot().save(sys.argv[3] + ".png")

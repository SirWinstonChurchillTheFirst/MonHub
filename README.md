# MonHub

**Your game hub for randomizers, emulators and fangames – all in one window.**

MonHub is a Windows app for anyone who wants to replay their own monster-catching games freshly randomized, without
fiddling with emulators, folders or settings: install once, import your ROMs, press “Mix & play”.
MonHub speaks **English** and **German** (switch under *Options*).

> **Unofficial fan project.** MonHub is not affiliated with Nintendo, Creatures, GAME FREAK or The Pokémon Company.
> Pokémon and all related names are trademarks of their owners. **MonHub includes no games (ROMs) and no BIOS** – only
> use copies of games you own.

![Continue where you left off](docs/screenshots/continue.png)

## What MonHub does

- **New run:** pick an original, randomize, play – powered by the Universal Pokemon Randomizer ZX, with presets and a
  built-in editor for every option. Optional Rare Candy cheat. Four presets come with it: *Nuzlock with Items*
  (default), *Easy Wild and Starters*, *Hard Trainers* and *Chaos Everything*.
- **Emulators, ready to go:** melonDS / DeSmuME (DS), mGBA (Game Boy, Game Boy Color, GBA), Azahar (3DS) – shared key
  and controller mapping, a 60 FPS switch for 3DS games.
- **Continue:** your latest save with trainer, play time, badges and team – read straight from the save files of every
  generation (Gen 1–7).
- **Dex:** the Dex of every single save.
- **Nuzlocke tracker**, **fangames**, **import** of ROMs and saves from other emulators.
- Seven looks and a short tour with Porygon to get started.

| New run | Your games |
|---|---|
| ![New run](docs/screenshots/new-run.png) | ![Games](docs/screenshots/games.png) |
| **Dex** | **Tour with Porygon** |
| ![Dex](docs/screenshots/dex.png) | ![Tour](docs/screenshots/tour.png) |

**Seven looks** – Red, Retro, Pink, Neon, Savanna, Spooky and Plain:

![The looks](docs/screenshots/looks.png)

## Install

Download `MonHub-Setup.exe` from the [releases](../../releases) and run it – no admin rights needed. Everything goes
into one folder (`%USERPROFILE%\MonHub`); ROMs and saves are kept on updates and when uninstalling.

The installer isn't signed, so Windows may show “Windows protected your PC” on the first start: click *More info* →
*Run anyway*.

## Build it yourself

Requirements: Windows, .NET 10 SDK, JDK 17 (`javac`, `jar`, `jlink`), Python 3 with Pillow, Inno Setup 6, 7-Zip.

1. Download [Universal Pokemon Randomizer ZX 4.6.1](https://github.com/Ajarmar/universal-pokemon-randomizer-zx/releases)
   and put its `.jar` into the repository's root folder (it is found by its checksum, the file name doesn't matter).
2. Run `MonHub/installer/build.ps1`. The first time it downloads the pictures from PMD Sprite Collab and the fonts,
   then melonDS, DeSmuME, mGBA and Azahar from their official releases (each checked against a fixed SHA-256 sum),
   and builds `Installer/MonHub-Setup.exe`.

Tests: `dotnet test MonHub.Tests` (among others the save reader for every generation, checked against PKHeX, and both
languages).

The Pokémon pictures are deliberately **not** in the repository; `MonHub/tools/` downloads them from their
source. The menu icons are MonHub's own (`tools/build_icons.py`).

## License

MonHub is free software under the **GNU General Public License v3.0** – see [LICENSE](LICENSE).

Bundled programs (in the installer, each with its license):

| Program | License | Source code |
|---|---|---|
| Universal Pokemon Randomizer ZX | GPL-3.0 | https://github.com/Ajarmar/universal-pokemon-randomizer-zx |
| melonDS | GPL-3.0 | https://github.com/melonDS-emu/melonDS |
| DeSmuME | GPL-2.0 | https://github.com/TASEmulators/desmume |
| mGBA | MPL-2.0 | https://github.com/mgba-emu/mgba |
| Azahar | GPL-2.0 | https://github.com/azahar-emu/azahar |
| OpenJDK 17 (Microsoft Build) | GPL-2.0 with Classpath Exception | https://github.com/microsoft/openjdk |
| .NET Runtime | MIT | https://github.com/dotnet/runtime |

**Pictures:** the menu icons are drawn by MonHub itself (`tools/build_icons.py`); every Pokémon picture comes from
[PMD Sprite Collab](https://sprites.pmdcollab.org), released by its artists under
[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) – attribution, non-commercial. Who made which picture is
listed in [CREDITS.md](MonHub/CREDITS.md). Pictures marked “CHUNSOFT” are original graphics from
*Pokémon Mystery Dungeon* © Nintendo / Creatures / GAME FREAK / Spike Chunsoft. The screenshots above show such
pictures too.

**Fonts:** Silkscreen, Pixelify Sans, Press Start 2P – SIL Open Font License 1.1.

**Thanks** to PKHeX (save formats, for checking), PokeAPI (Pokémon and place names in both languages) and the cheat
collection CTRPF-AR-CHEAT-CODES (3DS addresses).

## Support

MonHub is and stays free, with no paid features. If you like it, you can support its development with a coffee –
voluntary, nothing in return: **[ko-fi.com/krogger](https://ko-fi.com/krogger)** ☕

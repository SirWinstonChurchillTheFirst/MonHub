# MonHub

**Dein Spielzentrum für Randomizer, Emulatoren und Fangames – alles in einem Fenster.**

MonHub ist eine Windows-App für Freunde, die ihre eigenen Monster-Spiele neu gemischt spielen wollen, ohne sich mit
Emulatoren, Ordnern oder Einstellungen herumzuschlagen: einmal installieren, ROMs importieren, „Mischen & spielen“.

> **Inoffizielles Fan-Projekt.** MonHub hat keine Verbindung zu Nintendo, Creatures, GAME FREAK oder The Pokémon
> Company. Pokémon und alle zugehörigen Namen sind Marken ihrer Inhaber. **MonHub enthält keine Spiele (ROMs) und kein
> BIOS** – nutze nur Kopien von Spielen, die dir gehören.

## Was MonHub kann

- **Neuer Run:** ein Original wählen, mischen, spielen – mit dem Universal Pokemon Randomizer ZX, Voreinstellungen und
  einem eingebauten Editor für alle Optionen. Auf Wunsch mit Rare-Candy-Cheat. Vier Voreinstellungen sind dabei:
  *Nuzlock with Items* (Standard), *Easy Wild and Starters*, *Hard Trainers* und *Chaos Everything*
  (`PokeHub-Quellcode/presets`, erzeugt von `tools/build_presets.ps1`).
- **Emulatoren fertig eingerichtet:** melonDS / DeSmuME (DS), mGBA (Game Boy, Game Boy Color, GBA), Azahar (3DS),
  gemeinsame Tasten- und Controller-Belegung, 60-FPS-Schalter für 3DS-Spiele.
- **Weiterspielen:** der letzte Spielstand mit Trainer, Spielzeit, Orden und Team – gelesen aus den Spielständen aller
  Generationen (Gen 1–7).
- **Dex:** der Pokédex jedes einzelnen Spielstands.
- **Nuzlocke-Tracker**, **Fangames**, **Import** von ROMs und Spielständen aus anderen Emulatoren.
- Sieben Looks, eine Tour mit Porygon für den Einstieg.

## Installieren

Den Installer `MonHub-Setup.exe` aus den [Releases](../../releases) herunterladen und starten. Alles landet in einem
Ordner (`%USERPROFILE%\MonHub`); ROMs und Spielstände bleiben bei Updates und beim Deinstallieren erhalten.

## Selbst bauen

Voraussetzungen: Windows, .NET 10 SDK, JDK 17 (`javac`, `jar`, `jlink`), Python 3 mit Pillow, Inno Setup 6.

1. [Universal Pokemon Randomizer ZX 4.6.1](https://github.com/Ajarmar/universal-pokemon-randomizer-zx/releases)
   herunterladen und `PokeRandoZX.jar` in den Hauptordner legen.
2. melonDS und DeSmuME 0.9.13 bereitlegen (Pfade oben in `PokeHub-Quellcode/installer/build-pokehub.ps1`).
3. `PokeHub-Quellcode/installer/build-pokehub.ps1` ausführen. Beim ersten Mal lädt es die Bilder von PMD Sprite
   Collab und die Schriften herunter, dann mGBA und Azahar, und baut `Installer/MonHub-Setup.exe`.

Tests: `dotnet test PokeHub-Tests` (u. a. der Spielstand-Leser für alle Generationen, geprüft gegen PKHeX).

Die Pokémon-Bilder liegen bewusst **nicht** im Repository; `PokeHub-Quellcode/tools/` lädt sie aus ihren Quellen.

## Lizenz

MonHub ist freie Software unter der **GNU General Public License v3.0** – siehe [LICENSE](LICENSE).

Mitgelieferte Programme (im Installer, jeweils mit ihrer Lizenz):

| Programm | Lizenz | Quellcode |
|---|---|---|
| Universal Pokemon Randomizer ZX | GPL-3.0 | https://github.com/Ajarmar/universal-pokemon-randomizer-zx |
| melonDS | GPL-3.0 | https://github.com/melonDS-emu/melonDS |
| DeSmuME | GPL-2.0 | https://github.com/TASEmulators/desmume |
| mGBA | MPL-2.0 | https://github.com/mgba-emu/mgba |
| Azahar | GPL-2.0 | https://github.com/azahar-emu/azahar |
| OpenJDK 17 (Microsoft Build) | GPL-2.0 mit Classpath Exception | https://github.com/microsoft/openjdk |
| .NET Runtime | MIT | https://github.com/dotnet/runtime |

**Bilder:** die Menü-Symbole sind selbst gezeichnet (`tools/build_icons.py`), alle Pokémon-Bilder sind von [PMD Sprite Collab](https://sprites.pmdcollab.org), von den Künstlerinnen und Künstlern dort unter
[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) freigegeben – Namensnennung, nicht kommerziell. Wer
welches Bild gemacht hat, steht in [CREDITS.md](PokeHub-Quellcode/CREDITS.md). Mit „CHUNSOFT“ gekennzeichnete Bilder
sind Originalgrafiken aus *Pokémon Mystery Dungeon* © Nintendo / Creatures / GAME FREAK / Spike Chunsoft.

**Schriften:** Silkscreen, Pixelify Sans, Press Start 2P – SIL Open Font License 1.1.

**Danke** an PKHeX (Spielstand-Formate, zum Prüfen), PokeAPI (Namen, Routen) und die Cheat-Sammlung
CTRPF-AR-CHEAT-CODES (3DS-Adressen).

## Unterstützen

MonHub ist und bleibt kostenlos, ohne Bezahl-Funktionen. Wer mag, kann die Entwicklung mit einem Kaffee unterstützen –
freiwillig, ohne Gegenleistung: **[ko-fi.com/krogger](https://ko-fi.com/krogger)** ☕

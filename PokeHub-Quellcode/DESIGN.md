# MonHub design system

MonHub should feel like a Pokémon fan project, not a generic dashboard. Each theme is its own "device" or
"place" with its own shapes, type, decoration and small motions, not just a new colour palette.

## Structure

- `Themes/Base.xaml`: all styles (panel, key, menu entry, card, chip …), built only from **tokens** (`Hub.*`),
  plus neutral default values for every token.
- `Themes/<id>.xaml`: one theme. It overrides tokens and, where its identity needs a different shape, whole
  styles or templates (Game Boy: menu entries with a blinking pixel ▶, list cards with a cursor; Dex: the
  screen with bezel, LED and speaker).
- `HubThemes.cs`: the list of themes (name, tagline, colours for the preview tiles in "Optionen") and `Apply`,
  which swaps **one** merged dictionary. Every use is a `DynamicResource`, so WPF updates everything in one pass.
- `MainWindow.xaml`: the shell. Decoration behind everything (`Hub.Backdrop`), the menu (`Hub.RailIdentity` on
  top, `Hub.RailDeco` at the bottom, left or right via `Hub.RailColumn`), the pages on the "screen" (`Hub.ScreenFrame`).

## Themes

| Id | Idea | Special parts |
|---|---|---|
| `pokedex` (default) | the device itself | red body, lens + status LEDs, display with bezel, scanlines, d-pad; Pixelify Sans / Silkscreen |
| `gameboy` | the window is the green screen | only the 4 LCD greens (sprites recoloured too), START menu on the right, blinking pixel ▶, double-line text boxes; Press Start 2P |
| `center` | Pokémon Center | floor tiles with the Poké Ball emblem, healing machine (balls light up in turn), pill buttons, crooked trainer card, Chansey at the counter at the menu's foot |
| `cgear` | C-Gear from Black/White | night blue, rings of light (kept in the corner, away from the lists), cut corners, the three round C-Gear buttons at the menu's foot; Bahnschrift |
| `safari` | Safari Zone | sand, wooden board with a pinned note, parchment signs with hard shadows, swaying grass, ball-counter note and a Tauros at the menu's foot; Georgia |
| `lavandia` | night at the Pokémon Tower | violet sky with stars, faint moon and tower, fog; gravestone shapes (round top, square bottom); gravestone name plate with Gengar, Gastly floating between two flickering candles; Constantia |
| `schlicht` | calm | the base defaults, no decoration, nothing moves on its own |

## Tokens (selection)

- **Colours**: `Hub.Bg`, `Rail*`, `Surface*`, `Ink*`, `Line*`, `Accent*`, `KeyFace/KeyEdge/KeyInk`,
  `DangerFace/DangerEdge/DangerInk`, `BadgeOn/Off`, `ChipOn`, `Dialog`. Brushes may be patterns
  (`DrawingBrush` with `CachingHint="Cache"`).
- **Shape**: `Radius*` (a theme may use uneven corners like `18,3,18,3`), `PanelBorder`, `InnerBorder`/`InnerGap`
  (double lines), `ShadowOffset`/`ShadowVisibility` (hard shadows only), `KeyDepth`, `CardTilt`.
- **Type**: `Hub.Font*` (always full `FontFamily` values ending in `Segoe UI Symbol` for ♂/♀/▶; **never**
  `<StaticResource>` aliases, which WPF mixes up in merged dictionaries), `Hub.Size*`, `Hub.Weight*`.
- **Menu**: `RailColumn`, `RailWidth`, `RailBorder`, `NavIconVisibility`, `NavCaps` (capitals in retro themes),
  `CursorVisibility`, `IndicatorVisibility`.
- A token must never share its key with a style: the theme's value would hide the style from Base.xaml.

## Motion

- Sprites: `SpriteView` (PMD sprite collab), paused when invisible, the window is inactive or minimized, or a
  dialog is open. List/team sprites only move under the mouse or when selected.
- Theme details: `Ambient.Storyboard` runs a storyboard under the same rules and only at motion level "Alles".
  Storyboards set a low `Timeline.DesiredFrameRate` (4–15 fps).
- Levels (Optionen → Bewegung): **Wie Windows** (Animation effects on/off), **Alles**, **Ruhig** (only the big
  game sprite), **Aus** (nothing, no page transitions either).
- Page change: fade + 10 px slide, 170 ms, `FillBehavior.Stop`, skipped when motion is off. Pages are created
  once and kept.
- No `DropShadowEffect`/`BlurEffect` (see `PERFORMANCE.md`).

## Adding a theme

1. Copy `Themes/schlicht.xaml` to `Themes/<id>.xaml` and override tokens.
   Add `Hub.Backdrop`, `Hub.RailIdentity`, `Hub.RailDeco` or style overrides where the idea needs them.
2. Add an entry to `HubThemes.All` (name, tagline, light/dark, sprite tint, preview colours).
3. Look at every page and dialog in the new theme (Weiter, Spiele, Fangames, Emulatoren, Optionen, Import, Hilfe,
   Spielstände ohne Spiel), also with an empty MonHub and a narrow window.

## Tour (Porygon)

- `Tour.cs`: the stations (page, element name – several as `A|B`, first visible wins –, title, text, `Since` version).
  `TourOverlay.xaml`: dims everything except the element, a pulsing frame of light around it (`Hub.Accent`), the
  professor (Pokémon Showdown trainer sprite, `Assets/Tour`) in the corner away from it, pointing with his stick, and
  a speech bubble in the theme's colours. Keys: → / Enter, ←, Esc.
- When: the setup writes a new stamp into `System\App\install.ini` on every install. A new stamp → the professor asks
  (Ja / Vielleicht später / Nein): the whole tour after a new install or the same version again, only the stations with a
  newer `Since` after an update. "Vielleicht später" asks again on the next start; a normal restart asks nothing.
  Optionen → "Tour mit Porygon" starts it any time.
- New feature → add a station with `Since` = the new version (and give the element an `x:Name`).

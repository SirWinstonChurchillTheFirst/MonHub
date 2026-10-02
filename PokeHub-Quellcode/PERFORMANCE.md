# der Randomizer: why the settings lagged, and what fixed it

## Symptom

With an animated theme background (e.g. Gengar, Kanto starters), the settings dialog and the main window
reacted sluggishly. On PCs without a real graphics card (or in a VM) it was worst.

## How it was measured

An instrumented copy of the app (probe) measured, per scenario:

- **CPU**: process CPU time as a % of one core
- **input latency**: time from posting input-priority work until it runs (median / 95 % / max)
- **frame changes/s**: counted on the sprite image's `Source`

Scenarios were: main window active, settings open (modal), editor open, minimized. Each ran on the host (GPU)
and in a Hyper-V VM that renders in software (WARP) like a laptop without a GPU driver.

## Cause (in order of impact)

1. **Soft shadow around animated content.** The header had a `DropShadowEffect`, and the animated sprites sat
   inside it. Every sprite frame invalidates the element under the effect. WPF then re-renders the whole header
   and blurs it again, on every frame. In software rendering this dominated everything.
2. **A global resource swap per animation frame.** The background sprite was animated by replacing a resource
   in `Application.Resources` (`ThemeMascot`). Every assignment walks the element tree of *all* windows,
   including the open settings dialog. It ran at render priority, above input, so typing and clicking waited.
   The cost grew with window size: 0.19 → 0.81 ms per frame on the host (up to 1 497 visuals with the editor open).
3. **12 separate resource writes per theme change**, and a theme preview on **every keystroke** in the
   "own background image" field.

## Fix (the animation stays)

- **No soft shadows** anywhere near animated content. Headers and portraits use a hard, offset shape as shadow
  (a second `Border`), which costs nothing extra per frame.
- **Sprites animate their own `Image`** (`SpriteAnimator`, `Sprites.cs`): one `DispatcherTimer` per visible
  sprite at `Background` priority (below input), frames cut once and cached. No resource writes.
- **Animation only when someone can see it.** A sprite runs only while motion is on, its image is loaded and
  visible, and its window is active, enabled (no modal dialog open) and not minimized. Otherwise the timer stops.
- **Theme = one dictionary.** `Themes.Apply` builds one `ResourceDictionary` and replaces it in a single step:
  0.8 ms instead of 2.9 ms.
- The background-image preview in the settings waits 350 ms after the last keystroke.
- Windows' "Animation effects" setting off → nothing moves (`Motion.Enabled`, the desktop equivalent of
  `prefers-reduced-motion`).

## Results (VM, software rendering, one core = 100 %)

| Scenario | before | after |
|---|---|---|
| Gengar, main window active | 33.9 % | 0.8–2.5 % (animation running, 14 frame changes/s) |
| Kanto starters, main window active | 52.0 % | ≤ 5.6 % (animation running, 18 frame changes/s) |
| Settings open (modal) | noticeably laggy (user report) | 0.0 %, input waits 0.1 ms (median), max 0.4 ms |
| Editor open | noticeably laggy (user report) | 0.4 % or less |
| Minimized | – | 0.0–0.6 % |

Before, without the blur shadow but still with the resource swap, the same scenes needed 8.2 % / 6.3 %.
So the shadow was the main cause and the resource swap the second one.

## Rules for new UI

- No `DropShadowEffect` / `BlurEffect` around anything that changes. Use hard shadows (offset shapes).
- Never animate by writing to `Application.Resources`.
- Looping animations must stop when invisible, when the window is inactive or minimized, or when a dialog is
  open. Set a low `Timeline.DesiredFrameRate` on storyboards (a blink needs 4 fps, not 60).
- Debounce previews that react to typing.

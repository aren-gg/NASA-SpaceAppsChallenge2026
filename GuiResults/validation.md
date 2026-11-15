# GUI validation

2026-11-15 — Unity 2022.3.24f1. Verification used disposable local project copies so the open editor was not interrupted.

- **EditMode: 161 passed, 0 failed** ([results](editmode.xml)); includes the simulation suite, UI layout at 1280×720 and 1920×1080 with standard/large text, research purchases, oxygen-first ordering, supply consumption on EVA, full/paused action states and required icon renderers. Balance-category policy runs were excluded; simulation balance was unchanged.
- **PlayMode: 7 passed, 0 failed** ([results](playmode.xml)); keyboard/mouse menu actions, tab focus, research scrolling, kit movement blocking, terrain traversal and zero-allocation walking.
- **macOS build succeeded**, then a graphics-enabled native smoke run returned `P12_READY bundledData=True`, `OPEN_WORLD_READY width=203891; depth=204800; scale=1` and `P12_SMOKE_PASS`.
- **WebGL build succeeded** (`GuiBuilds/WebGL/`). A live browser runtime check was not performed; gameplay/input checks ran in Unity PlayMode.
- Final procedural icons, HUD, pack, research and pause renders were inspected. Layout tests caught the compass height issue; rendering inspection caught missing CanvasRenderer on custom symbols. Both were fixed and regression-checked. Earlier `*-red.xml` files preserve expected pre-fix test failures; `editmode.xml` and `playmode.xml` are the final results.

## Visual evidence
[Exploration HUD](open-world-hud.png) · [Expedition pack](open-world-kit.png) · [Terrain walk](open-world-terrain.png) · [Research at 720p](research-1280.png) · [Pause at 720p](pause-1280.png) · [Mission report](report-1280.png).

## Files
`Assets/Scripts/UI/HudView.cs`, `HudView.Layout.cs`, `StatBar.cs`, `MissionControls.cs`, `FacetedImage.cs`, new `ResourceGlyph.cs`/`ResourceSymbol.cs`; visual constants in `Assets/Scripts/Sim/GameConstants.cs`; `Assets/Tests/EditMode/UI/ResourceHudTests.cs`; regenerated HUD/menu prefabs and Main/P12Main scenes; `PLAY_GAME.md`.

The official Cinemachine 2.9.7 package cache was incomplete on this machine. Its official archive checksum was verified before restoring disposable verification cache content (and the five missing local cache assembly definitions). No dependency version or tracked package source changed; Library remains ignored. No commit was made.

Caveman engine remains unavailable; the attempted shrink wrapper reported passthrough and zero savings. No global configuration was installed. See [research and design choices](design.md).

## Counter Striken reference font update
Counter-Strike Regular by SoJa now supplies all HUD/pack/menu text. The original TTF, source attribution and static TMP SDF asset are included. `UiTypeface.cs` prepares the asset; `StatBar.cs` selects it; HUD/menu prefabs were regenerated. Hyphens deliberately use Liberation Sans because the source font maps that character to a soldier silhouette. Updated screenshots were inspected.

Final font checks: **161 EditMode passed** ([results](font-editmode.xml)), including font selection, fallback hyphens and all four standard/large text layouts; **7 PlayMode passed** ([results](font-playmode.xml)).

Both font-updated macOS and WebGL builds succeeded. The macOS native smoke check passed with bundled terrain data.

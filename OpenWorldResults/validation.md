# Open-world and minimal UI validation — 2026-11-15

Main now uses the original streamed Gale Crater terrain at real scale, not the earlier compressed 400 m arena. The available tile is **203,891 × 204,800 metres**. Its source relief is unchanged. The outpost is placed near Curiosity's landing coordinates; the existing survey scene remains available.

| Check | Result |
| --- | --- |
| EditMode, excluding the existing Balance category | 159 passed, 0 failed, 0 skipped |
| All PlayMode | 7 passed, 0 failed, 0 skipped |
| macOS build | Succeeded, zero build errors |
| WebGL build | Succeeded, zero build errors |
| Graphics-enabled macOS startup smoke | Exit 0, P12_SMOKE_PASS |
| Bundled data at startup | 668 schedule rows, 3 crops, full terrain ready at scale 1 |

Traversal evidence: `OPEN_WORLD_WALK distance=458.211; allocations=0; terrainTriangles=28032`. The test starts 180 m from base, walks over 458 m farther and finishes beyond 500 m from origin, checks feet against the source surface within 3 cm, verifies dataset-edge clamping, and measures zero managed allocations in walking calls. It also checks orbit heading, initial far clip, kit input blocking and closing. This is not a standalone 60 fps benchmark; it does not assert that streaming new meshes allocates no memory.

The startup visibility regression was reproduced at a 500 m far clip, then fixed by configuring both the real camera and Cinemachine lens from terrain viewing distance. The default HUD regression was reproduced with detailed actions visible, then fixed by moving them into the initially closed kit. Hidden saved-button binding is explicitly initialized and passes the existing late-HUD tests.

## Visual result

- [Default HUD](open-world-hud.png): compact vitals, power/radiation state, location and three navigation buttons; no large floating equipment labels.
- [Walking on the original terrain](open-world-terrain.png): actual source-scale landscape and a relative direction back to base.
- [Field kit](open-world-kit.png): energy/radiation details, tutorial and actions on demand.

UI layout checks pass with normal and large text at 1280×720 and 1920×1080. Keyboard/mouse tests open/close the kit with E, Tab, Escape and the close button, and retain research, pause, settings and end-report interactions. Existing recall, farm, exercise and replay integration checks pass.

Native CUA initially read the existing Unity window, but final reconnection returned `cgWindowNotFound` for editor and player windows. Final visual QA therefore used the rendered PlayMode captures above, plus the graphics-enabled built-player startup smoke. Stop any old Play session and reopen Main before trying the updated scene.

## Design tools and choices

Located UI/UX Pro Max and Caveman under `~/.agents/skill-collections/installed-skills-and-tools` (linked CLI packages). Used UI/UX Pro Max's local design search/template workflow for minimal geometric styling, readable labels, contrast and focus. Its marketing-page structure and web-only libraries were not applicable to the Unity HUD. Retained the bundled font and project palette, added texture-free faceted UI meshes, and removed unnecessary persistent dashboard panels. Caveman's installed package is coding-context compression tooling, not a low-poly art skill; its agent-wrapper configuration was not changed.

## Runtime changes

`OpenWorldTerrain` adapts the existing `MarsTerrain` streamer to the mission, preserving authored terrain scale and sampling the same triangles used for rendering. A walking-surface interface removes the old arena clamp from this mode while keeping the compact fallback. The existing terrain/weather bridge follows the player camera; preview debug controls are disabled in the mission.

The exploration mode uses an eight-hour accelerated year and thirty-minute EVA air capacity at 1×; those are new GameConstants, not changes to the default headless scenario. The mission arrives in late afternoon. MissionSession and ExpeditionSystem accept optional timing/capacity settings while preserving their prior defaults; dedicated EditMode tests verify both custom and default behavior.

The HUD uses compact bars and labels; E opens a field kit and blocks movement, F/G consume carried food/water, RMB/right stick orbits, and the wheel zooms. Detailed screens remain available. Warning panels are invisible when no warning/flare forecast exists. Read [controls and launch instructions](../PLAY_GAME.md).

## File scope

- Terrain assembly boundaries: new runtime/editor asmdefs under `Assets/Scripts/World` and `Assets/Scripts/Integration`; updated references in gameplay, shipping and tests.
- Walking/camera: `Gameplay/World/OpenWorldTerrain.cs`, `TerrainGenerator.cs`, `OutpostView.cs`; `Gameplay/Player/IWalkingSurface.cs`, `PlayerController.cs`, `OrbitCamera.cs`, `TerrainCameraClearance.cs`.
- Pacing: `Core/MissionSession.cs`, `Sim/World/ExpeditionSystem.cs`, `Sim/GameConstants.cs`.
- UI: `UI/HudView.cs`, new `HudView.Layout.cs`, new `FacetedImage.cs`, plus `StatBar.cs`, `MissionControls.cs`, `AlertBanner.cs`, `SunZapMeterView.cs`, `TutorialPanel.cs`, `MissionBriefing.cs`.
- Composition/assets: `Assets/Editor/P12/P12Build.cs`, Main/P12Main scenes, regenerated HUD/UI prefabs, Unity meta files.
- Tests: new `OpenWorldPacingTests.cs`, `MinimalHudTests.cs`, `OpenWorldTraversalTests.cs`; extended `HudInputTests.cs`; test assembly references.
- Reproduction/docs: `PLAY_GAME.md`, `P12_SHIP.md`, `tasks/`, and output exclusions in `.gitignore`.

## Reproduction and limits

Unity 2022.3.24f1. Test/build work used disposable project copies so the open source editor was not locked by batch jobs. Sources, scene, prefabs and new metadata were copied back. No commit was made; Library was not added.

Build outputs are in `OpenWorldBuilds/`. Rebuild commands are in `PLAY_GAME.md`; WebGL must be served over HTTP. WebGL compiled successfully; its browser runtime smoke was not repeated because browser/native window access was unavailable. The existing full 3,000-run balance sweep was not rerun: default sim equations, balance values and bot policy routines are unchanged. Earlier balance results remain in `P12Results/validation.md`.

The map ends at the prepared dataset edges. Existing nearby shelters/samples remain; this change adds no new quest/content system or persistence. Missing terrain data warns and uses the compact fallback. The existing completion horizon remains 668 scheduled sols rather than 668.6 astronomical sols.

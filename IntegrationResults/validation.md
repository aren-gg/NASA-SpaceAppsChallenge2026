# Playable integration validation — 2026-11-15

The playable entry point is `My project/Assets/Scenes/Main.unity`. Unity's **Mars Trainer > Open Playable Game** menu opens it; editor Play uses this entry point. `P12Main` is regenerated as a compatible copy. Preview scenes remain available for development.

| Verification | Result |
| --- | --- |
| EditMode, excluding the existing Balance category | 156 passed, 0 failed, 0 skipped |
| All PlayMode tests | 6 passed, 0 failed, 0 skipped |
| macOS build, Unity 2022.3.24f1 | Succeeded, 0 build errors |
| WebGL build, Unity 2022.3.24f1 | Succeeded, 0 build errors |
| macOS graphics-enabled smoke | Exit 0, P12_SMOKE_PASS; validated bundled JSON: 668 rows, 3 crops |
| Unity editor visual check | Briefing starts by Enter; world, equipment labels, mission HUD and separated warning/date panels render |

The integration test loads Main and verifies the shared simulation, bundled elevation, borrowed farm, briefing pause/focus, planting, exercise, powered recall flight, pause during recall, death report and scene replay. EditMode also checks elevation orientation/interpolation/invalid input, shared inventory, portable EVA supplies, base-only work and readable standard/large UI text at 1280×720 and 1920×1080. The existing mouse/keyboard and HUD lifecycle checks pass.

Walking-route evidence: `P9 route passed; frames=3299; max movement/sim allocation=0 B; max editor frame=0.07129838 s; profile=/var/folders/fp/prk030vs7tl0jdgfhdk50_400000gn/T/p9-world-profile.raw`. This is an allocation check, not a standalone 60 fps benchmark. `world-route.png` captures the generated world with corrected labels.

## Connected content

- One MissionSession owns weather, radiation, energy, survival, health, inventory, farming and research. The visible greenhouse borrows its existing FarmSystem.
- The bundled survey DEM now supplies the mission terrain, compressed to the existing 400 m walkable arena with softened relief and flattened base/cave areas. The original survey scene retains its separate 1:1 presentation.
- Existing weather presentation, follow camera, cave shelter zones, research pickups and authored upgrades use the shared mission.
- Automatic recall is composed with the actual battery and shelter list; movement is suspended during flight and restored on arrival.
- Solar equipment and crop growth are visible. Eat/drink work from carried provisions during EVA; farming, rest, exercise and medbay require base. Walking wakes a resting astronaut.
- A paused mission briefing gives controls and objective; the end report can start a fresh mission. HUD panels leave the astronaut visible.

## Files

New runtime files: `Data/MissionElevation.cs`, `Gameplay/World/OutpostView.cs`, `UI/MissionBriefing.cs`, plus Unity meta files.
Updated runtime files under `Assets/Scripts`: `Core/MissionSession.cs`, `Sim/GameConstants.cs`, `Gameplay/World/TerrainGenerator.cs`, `Gameplay/Player/PlayerController.cs`, `Gameplay/Radiation/RecallDrone.cs`, `Gameplay/Radiation/ShelterZone.cs`, `Gameplay/Farm/FarmInteract.cs`, and `UI/HudView.cs`, `MissionControls.cs`, `EndScreen.cs`, `AlertBanner.cs`, `TimeControls.cs`.
Updated composition/assets: `Assets/Editor/P12/P12Build.cs`, Main/P12Main scenes, regenerated HUD/UI prefabs, and `ProjectSettings/EditorBuildSettings.asset`.
Tests: new `PlayableMissionTests.cs` and `PlayableGameTests.cs`, updated `ShipUiTests.cs`, route initialization/provisions in `WorldRouteTests.cs`, and UI PlayMode assembly references. Documentation: `PLAY_GAME.md`, `P12_SHIP.md`, `tasks/`; output exclusions in `.gitignore`.

## Reproduction and limits

Final tests ran in disposable project copies. The first verification copy imported Assets/Packages/ProjectSettings without Library. No commit was created and Library was not added.
Run Unity with `-runTests -testPlatform EditMode -testCategory '!Balance'` for these EditMode checks, or `-runTests -testPlatform PlayMode` for the complete PlayMode suite. Category exclusion is supported by the [Unity Test Framework CLI](https://docs.unity.cn/Packages/com.unity.test-framework%401.1/manual/reference-command-line.html).
Build commands remain in `P12_SHIP.md`. Set `MARS_BUILD_OUTPUT` to choose a build directory; this delivery used `IntegratedBuilds/`. WebGL must be served over HTTP.

The 3,000-run balance sweep was not rerun; no balance equations or existing balance values were tuned. Earlier balance evidence remains in `P12Results/validation.md`. WebGL compiled successfully, but its browser runtime smoke was not repeated for this integration. The existing mission horizon remains 668 scheduled sols, with 668.6 shown as the astronomical year. The terrain survey's diagnostic overlay and preview-only controls are intentionally excluded from the playable mission.

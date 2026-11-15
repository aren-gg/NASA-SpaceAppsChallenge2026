# Implementation status

The complete scope is in `MARS_SURVIVAL_IMPLEMENTATION_PLAN.md`. Work is in progress; checks below are completed only with recorded evidence.

| Requirement | Status | Implementation / evidence |
| --- | --- | --- |
| Existing terrain, astronaut, base, rover and weather | Reused; runtime verification pending | Existing `Assets/Scripts/` and `Assets/MarsWeather/` |
| Month / half-year / full-year mission lifecycle | Implemented; integration verification in progress | `Gameplay/MarsMissionController.cs`; six scripted campaign runs passed |
| Three-knob power allocation and conservation | Implemented; pure checks passed | `Simulation/MissionSimulation.Power.cs`; proportional delivery and local batteries |
| Health, hunger, water, fatigue, exercise and medicine | Implemented; pure checks passed | `Simulation/MissionSimulation.Needs.cs` and `.Tasks.cs` |
| Oxygen, shielding, thermal response and overlapping weather | Implemented; integrated balance verification in progress | `Environment/MissionEnvironment.cs`; source and game rules identified separately |
| Farming, solar installation, storage and efficiency | Implemented; pure checks passed | Crop cycles, equipment state and installed research effects |
| All 16 research nodes | Implemented; pure checks passed | `Research/ResearchCatalog.cs`; unlock/install transactions |
| Reachable science sites, soil, markers and photographs | Implemented; Play-mode verification pending | `Gameplay/MissionWorld.cs`; terrain route sampling and camera evidence capture |
| Rover/base repair and panel dusting | Implemented; pure checks passed | Task completion and condition/deposit state |
| Radio coverage/windows and Houston reports | Implemented; pure checks passed | Modeled signal, energy cost and persistent once-only acknowledgment |
| Save/load, pause, safe advancement and debrief | Implemented; scene/save verification pending | Versioned JSON, atomic file replacement, saved task/equipment/report state |
| HUD, learning cards, keyboard access, source cards | Implemented; visual verification pending | `UI/MarsMissionHud.cs`; selector, dashboard, tasks, research, map, radio, learning, settings, debrief |
| Guided tutorial | Implemented; complete manual walkthrough pending | Ten persisted objectives with skip/replay, completion stops and six source-linked learning activities |
| Physical dashboard, calibration and takeover | Software adapter checked; real pairing deferred by user | Arduino Uno USB serial; existing sketch retained; `docs/hardware.md` |
| NASA data preparation and challenge alignment | Measured data imported; exact challenge resource list unverified | REMS sol224: 10,323 accepted rows, quality flags, 24 hourly means, hashes/provenance; 120,340 environment assertions passed |
| Automated campaign and conservation checks | Passed pure simulation checks | `tools/tests/Simulation/run.ps1`: 744 assertions including six synthetic-environment campaign runs |
| Runtime and editor assembly compilation | Passed against actual installed Unity assemblies | `tools/tests/compile-game.ps1`; this does not substitute for Play-mode verification |
| Actual Unity compilation and offline desktop build | Passed | Unity 2022.3.62f3 Windows build, zero errors, 103,806,473 reported build bytes; `Artifacts/release-build.log` |
| Built-player integration smoke | Passed | `Artifacts/final-smoke.log`: startup, mission selection, 16 reachable-endpoint/route-sampled sites, fixed-step advancement, save/load and HUD composition |
| Visual check | Core UI checked; extended playthrough remains | Title, clean final selector and live three-channel power inspected; overlapping legacy HUDs fixed and clean selector rechecked in final build; software controls worked |
| Full measured-environment campaign balance | Not fully verified | Six full preset/strategy runs use the test harness's variable synthetic environment; the actual measured sampler passed independent numerical checks. Extended joined playthrough/Profiler evidence is still outstanding |

Pre-existing user changes: rendering/package/project settings, `placeholder.py` deletion, `.vsconfig`, MarsBase directories/metadata and planning documents. Do not reset or revert them.

Launch `Artifacts/MarsOutpost/MarsOutpost.exe`. The build is generated and ignored by Git. Source entry scene: `My project/Assets/Scenes/Mars.unity`, with weather serialized and release build settings corrected. See `docs/play-survival.md` for controls.

Physical Arduino hookup is intentionally deferred by Cary's instruction. Its existing sketch was not replaced or flashed. The exact challenge resources tab is still inaccessible; no full resource-list compliance claim is made. Additional verification needed: a complete manual fieldwork/photo/weather walkthrough, long measured-environment balance, and profiling on the target computer. These limits do not prevent running the implemented game.

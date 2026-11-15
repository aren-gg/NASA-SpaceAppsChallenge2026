# P12 balance and builds

The unified playable game and controls are described in [Play Mars Outpost](PLAY_GAME.md). Latest open-world checks are recorded in [open-world validation](OpenWorldResults/validation.md); the [earlier integration validation](IntegrationResults/validation.md) is retained as history.

Use Unity **2022.3.24f1**, macOS Build Support and WebGL Build Support. Close the project in Unity before using the batch commands. No Python data fetch is needed; both JSON inputs ship in `Assets/StreamingAssets`.

For editor play, open `Assets/Scenes/Main.unity` (or use **Mars Trainer > Open Playable Game**). Final results and limitations are in [P12 validation](P12Results/validation.md).

```sh
bash tools/p12_ship.sh tests
bash tools/p12_ship.sh mac
bash tools/p12_ship.sh webgl
P12Builds/MarsTrainer.app/Contents/MacOS/'My project' -p12Smoke -logFile /tmp/mars-smoke.log
python3 -m http.server 8080 --directory P12Builds/WebGL
```

Open `http://localhost:8080`. The runtime must log `P12_READY bundledData=True` with 668 schedule rows and 3 crops. This marker checks validated JSON; defaults do not satisfy it. The macOS smoke launch must log `P12_SMOKE_PASS` and exit 0. WebGL uses UnityWebRequest for same-origin StreamingAssets, as required by the [Unity manual](https://docs.unity3d.com/2022.3/Documentation/Manual/StreamingAssets.html). Serving the build over HTTP is required; opening index.html as a local file is insufficient.

`UNITY_EDITOR`, `MARS_BUILD_OUTPUT` and `MARS_BALANCE_OUTPUT` override paths. Outputs live outside Assets. Build preparation regenerates the HUD prefab, creates `Main.unity` plus a compatible `P12Main.unity` copy, and selects `Main.unity` as the single ship scene. Rebuilds require no Library, cached IDE files, or editor-only scene setup. Keep all referenced Assets, their meta files, Packages and ProjectSettings in your commit; the current workspace also contains earlier uncommitted work required by this game.

The MarsTrainer WebGL template keeps the game at 16:9 and fits the browser viewport. The uncompressed build can be served by a plain static HTTP server without special compression headers.

The EditMode harness defaults to seeds 0–999 for each of idle, naive and good. `runs.csv` retains every seed; `balance.csv` prints survival percentage, terminal death-cause counts, mean dose at death/completion, and the mean full-sol generation-minus-requested-load margin. The energy margin excludes discretionary medbay spending and lamp spending; battery spending still uses the real energy system. Failed runs contribute their dose and margin at death, so these means are not survivor-only metrics. `MARS_BALANCE_RUNS` permits small calibration samples; only the default 1000-run result is release evidence.

All policies use `MissionSession`, the same systems and shipped crop/weather JSON as the playable world. Idle takes no actions. Naive tends crops, eats, drinks, rests, visits medbay once per sol when critically hurt, and shelters at night. Good also checks flare forecasts, visits medbay preventively, and buys available authored research. These are base-management policies; navigation and research sample collection are outside this harness. Independent simulations run on worker threads; no Unity APIs run there.

Mission seeds rotate the bundled seasonal calendar and sample a constant-controlled background-dose envelope. This is fictional scenario uncertainty, not a NASA forecast. Flare rates, thresholds and equipment are gameplay constants. Balancing changes belong in GameConstants or authored data; the harness must never change policy routines to meet targets.

The tutorial shows move, farm, energy, shelter and research guidance during the first five Martian days and can be skipped. Base actions consume the shared inventory/battery. Menu settings cycle volume and switch standard/large body text; headings keep their existing larger size. Settings and mission saves are not persisted (JSON save/load was optional).

The playable goal is now 99 Martian days, controlled by GameConstants.MissionDurationDays. The bundled 668-day schedule is retained for data integrity; only the first 99 days are needed. Daily clock speed is unchanged.

No commit is created by this workflow. A literal fresh-clone test requires the finished source and earlier required untracked assets to be committed by the owner. Until then, clean Assets/Packages/ProjectSettings exports verify independence from Library but are not proof of Git completeness.

## P12 file scope

| Area | Files |
| --- | --- |
| Shared mission | `Assets/Scripts/Core/MissionSession.cs`, Core assembly definition, `Gameplay/World/TerrainGenerator.cs`, World assembly definition |
| Balance | `Assets/Scripts/Sim/GameConstants.cs`, `Assets/Data/Research/Protection_1_cozy_shield.asset` |
| Data verification | `Assets/Scripts/Data/DataRepository.cs` |
| Tutorial, settings, actions | `Assets/Scripts/UI/TutorialPanel.cs`, `MissionSettings.cs`, `MissionControls.cs`, `HudView.cs`, `PauseMenu.cs` |
| HUD layout | `Assets/Scripts/UI/AlertBanner.cs`, `SunZapMeterView.cs`, regenerated HUD and pause prefabs |
| Build | `Assets/Editor/P12/`, `Assets/WebGLTemplates/MarsTrainer/`, `Assets/Scenes/P12Main.unity`, build-scene and WebGL project settings |
| Tests | `Assets/Tests/EditMode/MissionBalanceTests.cs`, `BalanceBot.cs`, `BalancePolicy.cs`, updated radiation expectations, UI layout/settings/prefab tests, `Tests/PlayMode/UI/MissionHudBindingTests.cs`, test assembly references |
| Reproduction | `tools/p12_ship.sh`, this document, P12 output exclusions in `.gitignore` |

Paths in the table are relative to `My project/` unless otherwise shown. New Unity assets include their meta files. Existing simulation-system logic is unchanged; the mission composition now connects it to the playable world.

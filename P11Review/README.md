# P11 look and VFX review

Review frames from the macOS development player at 1600 × 900:

- [Day](day.png): orange–pink–purple procedural sky, matte props, warm low sun and purple shadows.
- [Storm](storm.png): bounded dust particles, denser exponential fog and rose tint.
- [Flare](flare.png): procedural aurora ribbons, mild bloom and a pulsing visor warning. The initial sky flash decays before this settled frame.
- [Cold night](cold-night.png): dark violet grade, procedural stars and frost crystals confined to visor edges.

The development-build watermark is retained in these unedited player screenshots.

## Validation

- [EditMode results](tests.xml): 142 passed, zero failed or skipped.
- [Build result](build.txt): macOS development player succeeded, zero errors.
- [Player measurements](player-performance.txt): 99–101 draw calls; worst sampled p95 frame time 1.51 ms, under the 16.7 ms frame budget. Each state has a two-second warmup and three-second sample, uncapped with VSync disabled.
- Measurements cover the isolated look-review scene on this Mac, not a full-world or WebGL performance guarantee. `performance.txt` contains the earlier editor-only samples.
- No image textures were added. The visor binds Unity's existing white UI texture; its frost pattern and sky effects are calculated in shaders.

## Main files

- `Assets/MarsWeather/Runtime/WeatherController.cs`: atmosphere, post-processing, bounded particles, sky dome and visor overlay. No simulation advancement.
- `Assets/MarsWeather/Runtime/MarsLookProfile.cs` and `Assets/Resources/MarsLook.asset`: palette, atmosphere/VFX tuning and shared materials.
- `Assets/MarsWeather/Shaders/SunsetSky.shadergraph`, `MarsSky.hlsl`, `VisorEdges.shader`: procedural visuals.
- `Assets/MarsWeather/Materials/P11/`: eight shared matte surface materials and procedural effect materials.
- `Assets/Scripts/Gameplay/Weather/WeatherPresenter.cs`: independent overlapping hazard weights, warnings/ends, sol hour, suit state, and clean subscription/rebinding.
- `Assets/Scripts/Gameplay/World/TerrainGenerator.cs`, `PropScatter.cs`, `Assets/Scripts/Sim/GameConstants.cs`: P9 presentation hookup, suit shell, palette tokens and Play-mode static batching for stationary props.
- `Assets/Tests/EditMode/WeatherPresenterTests.cs`, `LookPresentationTests.cs`: lifecycle, overlap, late binding, night gating, transitions, environment restoration, shared suit materials, and procedural UI regression checks.
- `Assets/MarsWeather/Editor/P11*.cs`: look authoring, repeatable captures, tests and a development-player build command.
- `Packages/manifest.json`: URP aligned to the installed Unity 2022.3.24f1 editor's bundled 14.0.10.

## Review and reuse

Open `Assets/MarsWeather/Scenes/P11LookReview.unity`, enter Play mode, and use keys 1–5. `WeatherPreview.unity` keeps its streamed terrain experiment; the review copy disables that terrain and its fly-camera behaviour so the lab is visible.

The P9 world creates a WeatherPresenter using its existing Simulation and follows the player with the VFX. Existing WeatherSystem and RadiationSystem instances remain the source of hazards and suit state; this presentation change does not add or advance simulation systems.

`Mars/P11/Apply Look` updates the profile/prefab and preview palette. `Create Review Scene` regenerates the isolated copy. `Capture Review` writes editor captures. `Run EditMode Tests` writes the full XML result. `Build Review Player` creates `/tmp/P11ReviewPlayer.app`.

The opt-in development-player verifier runs only when launched with `-p11ReviewOutput <absolute-output-directory>`, writes four screenshots and measurements, then exits. It is excluded from release builds.

Implementation references: Unity's [Custom Function Node](https://docs.unity3d.com/Packages/com.unity.shadergraph@14.0/manual/Custom-Function-Node.html) and [TestRunnerApi](https://docs.unity.cn/Packages/com.unity.test-framework%401.1/api/UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.html) documentation.

Pending: your visual style review; full-world/target-platform profiling.

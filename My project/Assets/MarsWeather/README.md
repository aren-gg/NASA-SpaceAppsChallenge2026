# Mars Weather

Open `Scenes/WeatherPreview.unity` and press Play. Buttons or keys 1–5 activate Clear, Solar Flare, Dust Storm, Heat Storm, and Cold Night. Each transition takes three seconds. One scene keeps the lighting comparison consistent and avoids duplicated scene files.

## Merge into the team's game

1. Copy the entire `Assets/MarsWeather` folder, including every `.meta` file, into the target Unity project.
2. The target needs Unity 2022.3, URP 14, and Unity UI. Keep the target project's own render pipeline assets and project settings.
3. Drag `Prefabs/MarsWeather.prefab` into the team's scene. Assign its **Sun** field to that scene's directional light. Position the prefab where weather particles should appear, or parent it to a moving region anchor.
4. Enable post-processing on the target camera for bloom and grading. The sky, fog, and particles work independently of those camera effects.
5. Call the public activation API from the team's game logic. Do not copy the preview scene's stage, camera, or `WeatherPreview` component into the gameplay scene.

```csharp
using MarsWeather;

// Keep a serialized WeatherController reference in your gameplay script.
weather.Activate(WeatherEvent.DustStorm, strength: 0.8f, durationSeconds: 45f);
weather.Activate(WeatherEvent.SolarFlare); // remains active until changed
weather.Activate(WeatherEvent.HeatStorm);
weather.Activate(WeatherEvent.ColdNight);
weather.StopWeather(); // smoothly returns to the module's clear-weather preset
```

Only one event is active at a time. Strength is clamped to 0–1; zero duration stays active indefinitely. `WeatherChanged` fires when the target event changes. Read `DustAmount`, `SolarAmount`, `HeatAmount`, and `ColdAmount` for the current transition weights. These are visual amounts, not physical measurements.

## Event visuals

- **Clear:** desaturated mineral sky, neutral daylight and a softer solar disk.
- **Solar flare:** brighter solar disk, warm sky pulse, restrained bloom. This is an artistic solar-particle warning, not visible radiation or a dose calculation.
- **Dust storm:** two layers of wind-driven particles, rolling dust banks, dense colored fog, reduced sunlight.
- **Heat storm:** warm haze, up to 900 rising flecks and 64 tall thermal wisps, subtle animated sky shimmer and warmer horizon. This is a stylized extreme-heat scenario, not a measured Martian storm model.
- **Cold night:** darker violet sky, blue ambient light, faint stars and reduced sunlight. No snow is implied.

The preview uses smooth reference objects and instrument rings to show visibility and lighting detail. The floor is a flat test surface; this module does not generate terrain.

## Isolation and ownership

All module assets are under `Assets/MarsWeather`, with separate runtime/editor assemblies and the `MarsWeather` namespace. There is no global startup hook, terrain dependency, survival system, or hardware adapter. The editor scene builder writes only inside this folder and is optional.

The controller temporarily owns global sky, fog, and ambient settings, plus the assigned sun and its own post-process volume. Use one active atmospheric controller per scene. Disable **Control Atmosphere** before enabling the component if another teammate owns global lighting; this leaves particle effects active. Disabling the component restores the captured original scene atmosphere and removes its effects. `StopWeather()` instead transitions to this module's clear preset.

Assign sky and particle materials through the prefab so Unity includes their shaders in builds. Do not modify shared materials at runtime; the controller clones its sky material and uses particle property blocks.

Teammates should edit their own folders and merge this whole folder with its GUIDs intact. Scene integration consists of one prefab instance and a sun reference. No changes to shared gameplay scripts are required.

Solar events and heat waves are kept separate: solar events represent radiation exposure, while heat waves represent thermal stress. The visual glow is an artistic alert, not a claim that a solar flare directly produces a local heat wave. See [NASA’s Mars solar storm observations](https://www.jpl.nasa.gov/news/nasa-watches-mars-light-up-during-epic-solar-storm/).

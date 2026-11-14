# Weather module agent guide

Scope: standalone Solar Flare, Dust Storm, Heat Storm, and Cold Night visuals. Terrain is owned by another teammate. The previous survival MVP is retired.

- Own only `My project/Assets/MarsWeather/` for future weather changes.
- Keep the `MarsWeather` namespace and separate runtime/editor assemblies.
- Use one preview scene with event selection; keep preview controls and props separate from the reusable prefab.
- Keep `WeatherController.Activate`, `StopWeather`, and transition weights stable for integration.
- Preserve `.meta` GUIDs. Do not edit the team's terrain, gameplay scene, render pipeline, or project settings during module development.
- Assign scene-specific lights through serialized references. Never search for and disable teammates' lights.
- Only one controller owns global atmosphere at a time. Restore captured settings on disable.
- Prefer detailed particle layering, smooth geometry, fog and lighting over high-cost assets or image generation.
- Document artistic assumptions; these events do not calculate radiation dose or reproduce measured Mars weather.
- Use subagents only for independent tasks with explicit file ownership. Keep small edits with one agent; do not spend credits on duplicate browsing or overlapping reviews.
- Verify compilation, event switching, timed expiration, cleanup, and preview appearance after meaningful changes.

See `My project/Assets/MarsWeather/README.md` for the integration contract.

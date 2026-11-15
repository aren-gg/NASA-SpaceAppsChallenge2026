# Mars Survival Game: Agent Implementation Guide

## Objective and scope

Build a Unity Mars survival game with real terrain elevation, connected survival systems, environmental events, and a physical dashboard controller. The audience is ages 7–12: use plain labels, actionable warnings, and visible consequences.

Required features: health, hunger, water, tiredness, energy reserves, radiation exposure, oxygen, solar events, dust storms, temperature, and Mars terrain with altitude.

Start with one astronaut, one habitat, and one small terrain region. A seven-sol mission with nearby science tasks is a proposed default, not an additional mandatory feature. Complete the required systems before expanding scope.

### Current state

| Area | Status | Where |
| --- | --- | --- |
| Terrain and altitude | Done: Gale Crater at 1:1 from real elevation data, streamed in chunks | `Assets/Scripts/World/`, `Assets/Data/Terrain/`, `docs/data/terrain-data.md` |
| Weather visuals | Done: Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night as a standalone module | `Assets/MarsWeather/` and its `README.md` |
| Terrain + weather together | Linked in one scene; weather owns the atmosphere | `Assets/Scripts/Integration/`, `Assets/Scenes/Mars.unity` |
| Survival systems | Not started. The earlier survival MVP prototype was retired when the weather module was added; the required features above still need building | — |
| Hardware dashboard | Not started; board and controls not yet confirmed | — |

## Stack

- Unity project: `My project/`, Unity 2022.3.24f1 with URP 14. Preserve the installed editor and package versions unless a demonstrated need requires a change. Opening in a newer 2022.3 patch rewrites `ProjectSettings/ProjectVersion.txt`; do not commit that change.
- C#: simulation, movement, terrain integration, UI, and hardware adapter.
- Python: offline NASA data preparation only (`tools/data/`).
- C/C++: Arduino or ESP32 firmware once the actual board and controls are known.
- JSON/CSV: prepared observations and metadata. Heightmap: terrain elevation.
- Target a desktop build first. Confirm the operating system and serial compatibility before implementing USB integration.
- Use the [Patch asset toolbox](https://patch.gg/create/toolbox/assets) as a reference when generating game assets. Art direction is in `docs/game-design-references.md`.

## Delegation and credit budget

Use one lead agent and up to three worker agents at once. Subteams are ownership groups; they do not require extra management agents. The lead coordinates them directly.

1. Inspect existing source and instructions once. Exclude Unity-generated Library, Temp, Logs, obj, and package caches from broad searches.
2. Define shared state, units, commands, and file ownership before delegation.
3. Give workers narrow task briefs with only relevant context, files, interfaces, and acceptance criteria.
4. Parallelize independent work. Keep tightly coupled resource calculations with one owner.
5. Reuse an agent for follow-up work in its area. Do not spawn one agent per feature or recursively delegate routine tasks. Use subagents only for independent tasks with explicit file ownership; keep small edits with one agent.
6. Use a lower-cost available model for bounded data conversion, UI wiring, and documentation. Reserve stronger reasoning for architecture, scientific interpretation, and difficult integration failures. Follow any environment restrictions on model selection.
7. Research each dataset once and share a concise source manifest. Do not have multiple agents repeat the same browsing or repository inspection.
8. When budget controls exist, allocate bounded task budgets and checkpoint before expansion. Never claim exact credit savings without usage evidence.
9. Report blockers early. Stop speculative work that depends on an unanswered hardware or data question; continue independent work.
10. Validate meaningful behavior, then stop. Avoid repeated whole-project reviews, cosmetic rewrites, and unrelated refactors. Do not spend credits on duplicate browsing or overlapping reviews.

## Subteams and ownership

| Owner | Responsibilities | Owned files or areas |
|---|---|---|
| Lead / integration | Shared contracts, task allocation, main scene, integration, final build | Core contracts, `Assets/Scenes/Mars.unity`, `Assets/Scripts/Integration/`, shared configuration, project settings, render pipeline assets |
| A: Survival simulation | Health, hunger, water, fatigue, battery, oxygen, exposure accumulation | `Assets/Scripts/Simulation/` and focused simulation checks |
| B: World and environment | NASA preparation, terrain, temperature, environmental event *effects* (solar availability, radiation rate, event schedule) | `tools/data/`, `Assets/Scripts/World/`, `Assets/Scripts/Environment/`, `Assets/Data/`, `docs/data/` |
| W: Weather visuals | Sky, fog, lighting, particles and post-processing for each weather event | `Assets/MarsWeather/` only |
| C: Interaction and hardware | HUD, player commands, movement, dashboard adapter, firmware | `Assets/Scripts/Interaction/`, `Assets/Scripts/UI/`, `Assets/Scripts/Hardware/`, `firmware/` |

These are proposed paths. Adapt to existing project structure rather than creating duplicate systems.

Only one owner edits a shared scene, prefab, settings asset, or contract at a time. Workers create separate components/prefabs and send integration notes. Preserve Unity `.meta` files and GUIDs. Never edit generated Library files or revert another agent's changes. Module owners do not edit the team's terrain, gameplay scene, render pipeline, or project settings during module development; integration changes go through the lead.

### Worker brief

- Outcome: the behavior to deliver.
- Ownership: exact files or directories permitted for edits.
- Inputs: contracts, source data, and dependencies already available.
- Implementation: the smallest complete approach.
- Acceptance: observable behavior and relevant checks.
- Return: changed files, evidence, assumptions, blockers, and integration steps.

## Shared simulation contract

The lead defines these before workers start:

- One authoritative game state; UI and hardware issue commands rather than changing resource values directly.
- A fixed simulation time step independent of rendering. Pause and time acceleration use the same clock.
- Units: seconds, meters, degrees Celsius, kW for power, kWh for energy, liters for water, and one documented oxygen unit such as grams. World space is 1 Unity unit = 1 real metre.
- Preserve radiation measurement units and detector metadata. Do not silently convert absorbed dose to effective dose.
- Separate persistent configuration from mutable session state. Use ScriptableObjects for configuration and fresh state for each mission.
- Tick order: environment → requested loads → available power allocation → production/consumption → astronaut effects → warnings/UI snapshot.
- Apply resource transfers once and limit production by actual inputs and delivered power. Clamp storage to valid capacities.
- Share environment output as temperature, solar availability, event status, and radiation rate. Simulation owns resulting resource and health changes.
- Environment drives weather visuals, not the other way round: when an event starts, call `WeatherController.Activate(event, strength, durationSeconds)`; `StopWeather()` returns to clear. The weather's `DustAmount`, `SolarAmount`, `HeatAmount` and `ColdAmount` are visual transition weights, not physical measurements, and must not feed the simulation.

## Module contracts

### Terrain (`Assets/Scripts/World/`)

- Gale Crater, 204 km × 205 km, from the USGS MOLA+HRSC 200 m elevation model. 2.1 MB downloaded and bundled; no network use at run time. Data usage must stay explicit: source, bytes downloaded, bytes bundled and live memory are recorded in the metadata and shown on the F3 panel.
- Scale 1:1, horizontal and vertical. Config distances are in real metres.
- Chunks stream within the render distance (40 km) and unload beyond it; detail is highest near the viewer. Rocks are procedural decoration, not data.
- Queries for other systems: `GetGroundY`, `GetGroundElevationMeters` (areoid reference), `GetSlopeDegrees`, `GetLatLon`, `LatLonToWorld`. They read the heightmap, so they work whether or not a chunk is loaded.
- Full details, checks and limits: `docs/data/terrain-data.md`.

### Weather (`Assets/MarsWeather/`)

- Keep the `MarsWeather` namespace and separate runtime/editor assemblies.
- Keep `WeatherController.Activate`, `StopWeather`, and the transition weights stable for integration.
- Preview weather in the Mars scene (test keys F5–F9); there is no separate preview scene. Keep the reusable prefab free of scene-specific props.
- Assign scene-specific lights through serialized references. Never search for and disable teammates' lights.
- Prefer detailed particle layering, smooth geometry, fog and lighting over high-cost assets or image generation. (Smooth geometry differs from the low-poly art direction; see Open questions.)
- Document artistic assumptions: these events do not calculate radiation dose or reproduce measured Mars weather. Solar events stand for radiation exposure and heat events for thermal stress; the glow is an artistic alert.
- Integration contract and API examples: `Assets/MarsWeather/README.md`.

### Atmosphere ownership

- Only one controller owns global atmosphere (sky, fog, ambient light) at a time, and it restores the captured settings on disable.
- In the combined scene the weather controller owns it. `MarsWorldWeatherLink` turns off the terrain's own fog and sky, leaves the terrain in charge of the camera far clip, rescales weather fog density (originally tuned on a 60 m test stage) to the 1:1 map, and keeps weather effects centred on the camera.
- Set up a combined scene with **Mars → Terrain → Create Gale Crater Terrain**, then **Mars → Add Weather To Terrain Scene**.

## Feature implementation

| Feature | Implementation | Completion check |
|---|---|---|
| Health | Store 0–100 HP. Apply configurable damage from sustained oxygen shortage, dehydration, prolonged starvation, dangerous habitat/suit temperature, and explicitly simplified severe radiation scenarios. Recovery requires stable conditions and rest or limited medical supplies. Record the cause of each health change. | No unexplained passive HP loss; warnings precede preventable damage; zero HP ends the run once. |
| Hunger | Track food inventory separately from satiety. Satiety falls with elapsed time and activity. Eating consumes a ration. Low satiety reduces work efficiency before prolonged deprivation damages health. | Eating cannot occur without food; consumption does not depend on frame rate. |
| Water | Track clean water, wastewater, and astronaut hydration. Drinking consumes clean water. A powered recycler recovers a configurable fraction of available wastewater. Account for water consumed by oxygen production. | Recycling cannot create water without input; tanks cannot become negative or exceed capacity. |
| Tiredness | Track fatigue from activity and time awake. Rest lowers fatigue. Fatigue increases task duration and therefore supply use. Keep thresholds and recovery rates configurable. | The same task takes longer when tired; rest restores efficiency without restoring consumed supplies. |
| Energy reserves | Store battery energy in kWh. Integrate solar generation and delivered loads using elapsed hours. Apply charge capacity, discharge limits, and player-selected system priorities. | Loads cannot spend the same energy twice; power shortages reduce actual system output. |
| Oxygen | Track habitat and suit reserves separately. Breathing consumes oxygen; powered production consumes its modeled inputs. Refill suits from habitat reserves only when connected. Bundle air processing into life-support demand. | Outside trips consume suit reserves; a full habitat tank cannot remotely refill the suit. |
| Radiation | Track current absorbed dose rate and accumulated absorbed dose. Integrate rate over time using matching units. Shelter changes future exposure through documented model assumptions. Severe-event HP penalties are gameplay rules, not medical thresholds. | Shelter does not erase accumulated exposure; source units are visible; ordinary background exposure does not cause instant damage. |
| Solar event | Schedule warning, onset, peak, and recovery with a reproducible seed. Use an archived Mars radiation event when available; otherwise label the profile simulated. Treat the radiation hazard as a solar particle event rather than converting every flare into a Mars dose. Visuals: `WeatherEvent.SolarFlare`. | Player can recall the astronaut and shelter; event effects expire cleanly. |
| Dust storm | Reduce solar availability and outdoor visibility; increase travel/task difficulty. Separate airborne dust from optional persistent panel deposits. Avoid unsupported hurricane-like forces. Visuals: `WeatherEvent.DustStorm`. | Charging falls during the event; recovery restores only the effects that should end. |
| Temperature | Load a representative NASA temperature profile and interpolate across the sol. Model habitat thermal response with simplified heat loss and delivered heating power. Treat outdoor air temperature separately from astronaut temperature inside a suit. Visuals for extremes: `WeatherEvent.HeatStorm` and `WeatherEvent.ColdNight` (stylized; not a measured Martian storm model). | A cold period raises heating demand; turning heating off changes internal temperature gradually. |
| Mars terrain and altitude | Done. Real elevation cropped with projection, extent and vertical reference preserved; missing samples handled; heightmap plus metadata exported; mesh built at matching horizontal and vertical scale; altitude shown against the dataset's reference. | Known sample elevations and distances agree with the prepared source within declared resampling tolerance (Curiosity landing site: −4503 m in the tile vs −4501 m published). |
| Movement and exploration | Start with keyboard movement or destination selection. Use terrain slope (`GetSlopeDegrees`) for route restrictions and exertion. Outdoor tasks consume time, suit supplies, and stamina. | Routes stay on traversable terrain; returning to the habitat changes supply context correctly. |
| Hardware dashboard | Put USB communication behind an input adapter. Debounce buttons, smooth knobs, validate messages, and send commands through the same interface as UI. Update Unity objects on the main thread. Send status back for LEDs. | Disconnecting hardware leaves on-screen controls usable; invalid messages cannot corrupt state. |
| HUD and learning feedback | Show health and urgent needs prominently; group habitat reserves separately. Explain warnings with an action and cause. Include source/date labels and a short event/resource history. | A player can identify the current problem and a response without reading technical documentation. |

## NASA data plan

Bundle a small curated dataset with the game. Network access should not be required during judging. Record source URL, product identifier, location, dates, units, resolution, missing values, and processing steps.

| Source | Use | Boundary |
| --- | --- | --- |
| [Mars Trek](https://trek.nasa.gov/mars/) | Elevation and imagery for one region | Image tiles are not automatically numerical elevation data. Choose map size after checking actual DEM resolution. The terrain uses the USGS GeoTIFF behind Trek's MOLA+HRSC layer for numeric heights. |
| [Curiosity RAD](https://science.nasa.gov/resource/radiation-measurements-on-mars/) | Mars surface radiation reference | Measurements apply to a particular detector, place, and period; do not claim a global live radiation map. |
| [NASA PDS Mars atmosphere archive](https://pds-atmospheres.nmsu.edu/data_and_services/atmospheres_data/Mars/Mars.html) | REMS weather observations | Inspect calibrated product documentation and quality flags; label reused profiles as representative. |
| [NASA ECLSS](https://www.nasa.gov/reference/environmental-control-and-life-support-systems-eclss/) | Water, oxygen, and air-processing relationships | Prototype capacities and efficiencies remain explicit game assumptions unless sourced. |
| [NASA Mars surface power study](https://www.nasa.gov/wp-content/uploads/2024/01/mars-surface-power-generation-challenges-and-considerations.pdf) | Dust and solar-power behavior | Do not claim an invented attenuation multiplier is a measurement. |
| [DONKI](https://ccmc.gsfc.nasa.gov/tools/DONKI/) | Optional historical solar-event context | A flare or CME record alone does not determine a Mars surface dose. |
| [OSDR RadLab](https://visualization.osdr.nasa.gov/radlab/gui/data-api/) | Optional radiation comparisons | Do not substitute ISS measurements for Mars surface measurements. |
| [NASA human performance standards](https://www.nasa.gov/reference/4-0-human-performance/) | Radiation terminology and exposure limits | Check the applicable standard and units before quoting limits. Health limits are not HP conversion rules. |
| [NASA's Mars solar storm observations](https://www.jpl.nasa.gov/news/nasa-watches-mars-light-up-during-epic-solar-storm/) | Context for the solar-flare visual | Visual alert only; not a dose source. |

Gale Crater is the chosen region, since Curiosity radiation and weather context can support the same general region. Do not imply observations from different dates form one simultaneously measured scenario.

## Execution waves

### Wave 1: Contracts and first playable system

Lead defines state, units, commands, ownership, and scene setup. A implements battery, heating, oxygen, and health. B obtains and prepares the terrain and environmental samples (terrain done). C builds a basic HUD and on-screen commands against the agreed contracts.

Exit: power allocation changes habitat conditions and produces understandable warnings; data assets have source metadata.

### Wave 2: Full feature integration

A adds hunger, water, fatigue, and exposure accumulation. B adds temperature, dust, and solar-event outputs and triggers the matching weather visuals through `WeatherController.Activate` (terrain import done). C adds movement, tasks, and a mock hardware adapter. Lead integrates the main scene and resolves contract changes.

Exit: all required features work together in one playable scene.

### Wave 3: Hardware and quality

C implements the adapter for the confirmed board and desktop platform. A checks resource balances and health transitions. B checks terrain scaling and source claims. W checks weather readability at gameplay distance. Lead performs an integrated playthrough and prepares a repeatable demonstration.

Use one bounded independent reviewer only after integration, focusing on resource conservation, unit errors, data claims, and hardware failures. Do not launch overlapping general reviews. Resolve concrete findings and recheck the affected behavior.

Exit: a desktop build completes the demonstration, works offline, and remains controllable without hardware.

## Quality and completion rules

- Prioritize functioning mechanics over extra assets, animations, procedural worlds, or additional NASA integrations.
- Use focused checks for resource conservation, time-step consistency, event reproducibility, and state reset.
- Verify terrain scale and radiation units against source metadata.
- For weather changes, verify compilation, event switching, timed expiration, cleanup (settings restored on disable), and appearance in the Mars scene.
- Check the actual Unity scene as well as isolated C# logic. Report unavailable editor, build, or hardware verification honestly.
- Keep a concise handoff: completed work, changed files, evidence, unresolved issues, next owner.
- Finish when required features meet their acceptance checks and integration is demonstrated. Avoid adding new systems without user direction.

## Open questions for the team

- **Art direction:** `docs/game-design-references.md` asks for flat-shaded low-poly geometry, an eight-colour palette and purple ambient fill. The weather module prefers smooth geometry and sets its own flat, greyer ambient light and sky colours. Agree on one direction before more assets are made.
- **Unity patch version:** the guide pins 2022.3.24f1; at least one machine has 2022.3.62f3 installed.

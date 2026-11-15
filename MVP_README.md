# Mars terrain and weather prototype

The survival game is now integrated with this project. See [play and build instructions](docs/play-survival.md) and the [implementation and verification ledger](docs/implementation-status.md). Run `Artifacts/MarsOutpost/MarsOutpost.exe` for the packaged Windows build, or open `Assets/Scenes/Mars.unity` in Unity. The prototype notes below describe the earlier foundations.

Two modules in one Unity project (`My project/`, Unity 2022.3, URP 14):

- **Terrain:** Gale Crater at life size (1:1) from real NASA/USGS elevation data, streamed in chunks around the camera. See [terrain data and setup](docs/data/terrain-data.md).
- **Time and events:** real Mars time with a 20-minute day-night cycle, and solar events replayed from NASA's DONKI catalogue. The sol, local time and current event show in the top-right info tab. See [Mars clock and weather events](docs/data/weather-events.md).
- **Outpost:** an enterable habitat 40 m from the start, with a room for each survival system (airlock, EVA prep and lab, command hub and galley, medical bay, crew quarters, storm shelter, energy room, life support, greenhouse) and the tool models placed in the rooms that use them. See [Mars outpost](docs/outpost.md).
- **Rover:** a drivable battery rover parked on the outpost pad: 50 km/h top speed, 120 km on a full battery, recharges on the pad. See [Rover](docs/rover.md).
- **Weather:** Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night visuals. See the [MarsWeather integration guide](My%20project/Assets/MarsWeather/README.md).

## Run both together

Open `My project/Assets/Scenes/Mars.unity`. If the scene has no weather yet, choose **Mars → Add Weather To Terrain Scene**. Press Play.

Play opens on the home screen: the camera slowly circles the starting area (or the outpost, once it is in the scene) while the menu offers **Start mission** (or press Enter), **How to play** and **Quit**. Starting fades into the astronaut's view. The screen is built in code (`Scripts/UI/MarsHomeScreen.cs`); to change its text or skip it, add a **MarsHomeScreen** component to a scene object and edit it or untick *Show On Start*.

| Key | Action |
| --- | --- |
| W A S D | Walk (3.4 km/h) |
| Mouse | Look (Esc frees the cursor, click captures it) |
| Left Shift + W | Sprint (29 km/h) |
| Space | Jump (Mars gravity) |
| E | Board the rover when standing next to it; step out when nearly stopped |
| In the rover | W accelerate, S brake then reverse, A / D steer, Space handbrake, mouse orbits the camera |
| 1 – 9, 0 | Pick a tool (the mouse wheel also works) |
| Left mouse | Use the tool. One click starts a job; it finishes by itself while you keep aiming at the same thing |
| R | The tool's extra action (camera zoom, ask the radio for the nearest survey site) |
| F2 | Switch between the astronaut and the free inspection camera (W A S D, Q / E, right mouse to look, Shift faster, scroll wheel changes speed) |
| F5–F9 | Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night, for testing (the schedule takes over again after 45 s) |
| ] / [ | Step the Mars clock one hour forward / back |
| F3 | Terrain data panel |

## Tools

Ten tools from `Task_assets` sit on a numbered hotbar (keys 1–9 and 0); the selected one is held in the astronaut's right glove (the arm bends to hold it, so tool and hand move together when walking, running and jumping). Aim with the crosshair (reach 3.5 m). The prompt under it says what clicking will do (or why not), a card beside it explains what you are looking at and which tool it needs, and a ring around the crosshair fills while a job runs. The **What to do next** card (top left) always suggests the most useful next step, with the key to press.

A small **base camp** appears to the right of the start: three solar panels, a battery, two crop beds and a supply crate with an oxygen tank. Seven **survey sites** are scattered 40–170 m around the start; each is a kind of rock or sand Curiosity studied in Gale Crater. A marker on screen guides you to the nearest one.

| Key | Tool | What it does |
| --- | --- | --- |
| 1 | Rock Hammer | Break a rock sample off a survey site |
| 2 | Core Drill | Drill a core at a survey site (worth the most) |
| 3 | Survey Camera | Photograph survey sites, Mount Sharp and the camp; R zooms |
| 4 | Radio | Weather and status report; R points you to the nearest unfinished site. Weather alerts arrive by radio |
| 5 | Oxygen Canister | Top up suit oxygen away from camp; refill it at the supply crate |
| 6 | Work Lamp | A light for night and dust storms, with soft shadows. In your hand it shines from the lamp; switch tools and it clips to your helmet |
| 7 | Panel Duster | Sweep dust off the solar panels (dust storms cover them and cut their power) |
| 8 | Field Wrench | Fix panels and the battery after heat storms and cold nights |
| 9 | Shovel | Dig soil and fill the crop beds (2 loads each) |
| 0 | Sprayer | Water the crop beds so the plants grow, then harvest them |

Samples go in a 4-slot bag and are handed in automatically when you walk up to the supply crate. Suit oxygen lasts 7 minutes away from camp and refills inside it; the screen edges glow when it runs low.

**Stand-ins:** suit oxygen, the battery and solar power, crop growth and the radiation level are simple placeholders in `Scripts/Tools/` (`MarsExpedition`, `MarsBaseCamp`, `MarsCropBed`, `MarsSolarPanel`, `MarsBatteryCabinet`), to be replaced by the real survival systems. Everything is added automatically at Play. The tool models are read from `Assets/MarsTools/Resources/MarsTools/Models/*.obj.txt` (copies of the Task_assets OBJ files, read by the game so each tool is held the right way up); the hotbar icons in `Icons/` are rendered from the same geometry.

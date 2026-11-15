# Play the survival game

The packaged Windows game is `Artifacts/MarsOutpost/MarsOutpost.exe`. Keep the entire adjacent build folder together.

Open `My project/Assets/Scenes/Mars.unity` in Unity 2022.3.62f3 and press Play. Choose Start mission on the title screen, then select one Martian month, half a year or a full year. A month means one-twelfth of a Martian year in this game.

## The HUD

The HUD uses the MarsTrainer UI kit and Counter-Strike UI font from the backend branch. Bottom left are the suit vitals, oxygen first: Air, Health, Water, Food and Rest. These needs are tracked by the MarsTrainer survival model (`SurvivalSystem` and `HealthSystem`). Bottom right are the battery, the Sun-Zap radiation meter (dose against the NASA-STD-3001 budget) and the quick Eat [F] and Drink [G] buttons. Warnings queue in the banner at the top. Escape opens the pause menu with controls, sound and text size. When a mission ends, a mission report appears.

## Daily routine and power

The mission starts in the habitat. Press Tab to open the outpost console. The Overview has the three power controls: Life control, Temperature and Radiation shielding. Compare each request with delivered power and estimated need. Excess draw consumes reserves; tasks and charging use the remaining supply. Solar panels generate electricity.

The daily routine handles stocked meals, water, crops and rest during safe advancement. Use the Overview to advance an hour, a sol or several sols; warnings and completed tasks stop advancement for review.

## Tools

Ten hand tools stand on racks in the outpost rooms. Look at one and press E to put it into its hotbar slot. Select a slot with 1–9 or 0 (or the mouse wheel), then left-click to use the tool. R is the tool's second action. Each click starts the matching outpost task, which runs while you stand still. Inside the base, Q puts the selected tool back and B returns them all.

| Key | Tool | Where it is kept | What it does |
| --- | --- | --- | --- |
| 1 | Rock Hammer | Lab | At a science site: survey the rocks, then hammer in the survey marker |
| 2 | Core Drill | Lab | At a site: drill a core sample. At the lab bench: analyze it for research points |
| 3 | Survey Camera | Lab | Photograph a placed marker (R zooms) |
| 4 | Radio | Hub, shelter, lab | Queue a Houston status report, then transmit it. R guides you to the nearest site with work left |
| 5 | Oxygen Canister | Life support, medical bay, lab | Inside: refill the canister and your suit. On EVA: top up your suit |
| 6 | Work Lamp | Quarters, shelter | Light for nights and dust storms |
| 7 | Panel Duster | Energy room | At the solar array: dust the panels |
| 8 | Field Wrench | Energy room | Repair the base (inside) or the rover (beside it). Install researched upgrades in the energy room |
| 9 | Shovel | Greenhouse | Plant an empty bed (R switches between leafy and staple crops) |
| 0 | Sprayer | Greenhouse | Harvest a ready bed, or water a stressed bed |

The console's Base tasks, Field log and Radio tabs still offer every task as a button. Those buttons need the same tool in your hotbar.

## Field science and Houston

The Field log lists science sites and their coordinates. Select a site, or use the Radio's R action, and follow its marker. Survey and mark the site, drill a core and photograph the marker. Then return to the lab to analyze the core, and spend research points on designs. Install researched equipment in the energy room before its benefit applies.

Radio reports go to simulated Houston mission control. Both listening and transmitting consume power. No network connection is required.

## Controls

| Control | Action |
| --- | --- |
| WASD / mouse | Move / look |
| Shift / Space | Sprint / jump |
| 1–9, 0 / mouse wheel | Choose a tool |
| Left click / R | Use the tool / its second action |
| E | Pick up the tool you are looking at; board or leave the rover |
| Q / B | Put the selected tool / all tools back (inside) |
| F / G | Eat / drink |
| Tab | Outpost console |
| H | Daily task help |
| P | Pause/resume |
| Escape | Pause menu, or close the console |
| F5 / F9 | Save/load |
| F3 | Terrain data and source information |

## Data sources

The console's Learning tab lists every source with links:

- The bundled MarsTrainer schedule (`StreamingAssets/mars_year_schedule.json`, 668 sols) drives the environment each sol. Curiosity RAD sets the daily GCR dose. Curiosity REMS and InSight set the daily temperature range, which rescales the REMS sol 224 daily curve. The schedule also sets dust opacity, and NASA DONKI sets the flare level. Rows built offline are marked synthetic.
- KSC Veggie / CEA crop estimates (`StreamingAssets/crop_parameters.json`) set the leafy bed's water use.
- The NASA-STD-3001 Vol. 1 career limit is the dose budget behind radiation health damage.
- NASA Mars24 provides sun position, and USGS MOLA–HRSC provides the terrain.

Rebuild the JSON with `tools/build_schedule.py` and `tools/build_crops.py` (see `tools/README.md`).

## Hardware, build and checks

The Settings tab includes interface scaling, sound, text size, reduced weather motion and a dashboard emulator. The Arduino control panel is described in `docs/hardware.md`.

Build with **Mars → Build Survival Game**. It includes the gameplay scene and weather prefab. To run the in-game play check, launch the built player with `--qa --qa-out=<folder>`. Other checks:

- Pure simulation checks: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/tests/Simulation/run.ps1`
- Environment checks: `tools/data/check_mission_environment.ps1`
- EditMode tests for the MarsTrainer sim and data: Unity Test Runner

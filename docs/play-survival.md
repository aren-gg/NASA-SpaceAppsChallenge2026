# Play the survival game

The packaged Windows game is `Artifacts/MarsOutpost/MarsOutpost.exe`. Keep the entire adjacent build folder together. The final build is open at the mission selector after verification.

Open `My project/Assets/Scenes/Mars.unity` in Unity 2022.3.62f3 and press Play. Choose Start mission on the title screen, then select one Martian month, half a year or a full year. A month means one-twelfth of a Martian year in this game.

The mission starts in the habitat. Press Tab to open the console. The Overview has the three power controls: Life control, Temperature and Radiation shielding. Compare each request with delivered power and estimated need. Excess draw consumes reserves; tasks and charging use the remaining supply. Solar panels generate electricity.

Use Base tasks for care, greenhouse work, repairs and cleaning. Walk to the named room or equipment before starting a manual station task. The daily routine handles stocked meals, water, crops and rest during safe advancement. Use the Overview to advance an hour, a sol or several sols; warnings and completed tasks stop advancement for review.

The Field log lists science sites and their coordinates. Select a nearby site, close the console and walk there. Sample soil, place a marker, then face it before capturing a photo. Return to the lab for analysis and spend research points on designs. Install researched equipment in the energy room before its benefit applies.

Radio reports go to simulated Houston mission control. Queue an update, select Transmit and wait for a usable signal window. Both listening and transmitting consume power. No network connection is required.

| Control | Action |
| --- | --- |
| WASD / mouse | Move / look |
| Shift / Space | Sprint / jump |
| E | Board or leave the rover |
| Tab / Escape | Toggle the mission console |
| P | Pause/resume |
| F5 / F9 | Save/load |
| F3 | Terrain data and source information |

The Settings tab includes interface scaling, reduced weather motion and a dashboard emulator. Arduino Uno pairing is deferred until the existing sketch is connected; the emulator is explicitly software input.

Build with **Mars → Build Survival Game**. It includes the gameplay scene and weather prefab. Pure simulation checks run with `powershell -NoProfile -ExecutionPolicy Bypass -File tools/tests/Simulation/run.ps1`; assembly checks run with `tools/tests/compile-game.ps1`. Current verification limitations are recorded in `docs/implementation-status.md`.

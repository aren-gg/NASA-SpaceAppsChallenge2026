# Explore Mars
Stop any old Play session, then open `My project` in Unity 2022.3.24f1 and reload `Assets/Scenes/Main.unity`. Choose **Mars Trainer > Open Playable Game**, press Play, then **Begin mission**.

- **WASD / left stick:** walk relative to the camera. **Shift / left-stick press:** sprint.
- **Hold RMB / right stick:** orbit the camera. **Mouse wheel:** zoom.
- **E:** open/close the expedition pack. Walking stops while it is open; time continues until you pause.
- **F / G:** eat / drink carried supplies without opening a panel.
- **R:** research. **Space:** pause time. **1 / 2 / 3:** 1× / 2× / 4× time. **Escape:** close the kit or open the pause/settings menu.
- The HUD puts oxygen first, with resource symbols and labeled meters, power, radiation status and a direction back to base. Food/water buttons show Full when replenishment is unnecessary. Detailed energy/radiation readouts, research and base actions are in the pack. Warning panels appear only when needed.
- At base, tend crops, rest, heal and exercise. Caves contain research samples and provide radiation shelter. Return to base to refill EVA air. The existing automatic recall costs battery and has a cooldown.

`Assets/Scenes/Main.unity` now streams the original bundled Gale Crater elevation tile at **1 Unity unit = 1 metre**, without compressing or adding noise to its relief. Its approximately 204 × 205 km extent is the available world; walking is bounded by the dataset edges. The outpost begins near Curiosity's landing site. Missing terrain data warns and falls back to the compact world.

The goal is to survive **99 Martian days**. The HUD shows elapsed days out of 99. Daily time speed is unchanged (about 43 seconds per Martian day at 1×, or about 71 minutes from arrival to day 99), with a thirty-minute EVA air reserve at 1×, and a late-afternoon arrival. The original daily simulation pace and equipment remain unchanged; mission completion now occurs at day 99. No save/load feature was added.

The preview scenes remain development tools. There is one shared mission, one player camera and one weather presentation in Main. No runtime external downloads are required.

Current builds: `GuiBuilds/MarsTrainer.app` and `GuiBuilds/WebGL/`. For WebGL, run `python3 -m http.server 8080 --directory GuiBuilds/WebGL` and open `http://localhost:8080`.
Rebuild after closing this Unity project: `MARS_BUILD_OUTPUT="$PWD/GuiBuilds" bash tools/p12_ship.sh mac` (or `webgl`).
The briefing and pause menu list each control on its own indented row. UI text uses title capitalization, larger body text and a 6% condensed Counter-Strike font.

Checks and screenshots: [readability validation](GuiResults/readability-validation.md), [99-day validation](GuiResults/days-validation.md), [GUI validation](GuiResults/validation.md), [design and competitor research](GuiResults/design.md), [open-world validation](OpenWorldResults/validation.md).

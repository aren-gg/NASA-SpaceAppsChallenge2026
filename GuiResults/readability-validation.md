# UI readability pass

Labels/headings/buttons use title capitalization; acronyms and scientific units retain their meaningful casing. Body text increased from 27 to 30 reference pixels, with additional word/line spacing. The original Counter-Strike TTF remains intact; CounterStrikeUI.ttf is a local derivative whose horizontal outlines and advances are 94% of the original width, with vertical outlines unchanged. This supplies the new static CounterStrike UI SDF atlas. Unsupported symbols and the decorative hyphen still use Liberation Sans fallback.

A shared ControlGuide builds separate, indented key/action rows, with two columns in the mission briefing and pause menu. The pause menu includes exploration and menu-navigation keys. HUD walking hints and pack time hints use the same aligned rows; tutorial controls are split across indented lines. Walking hints hide while the pack is open.

Updated files: UI StatBar, ControlGuide, UiTypeface, HUD/layout, briefing, pause/menu/settings, tutorial, research, report and radiation label code; UI visual constants; the world navigation caption; font source/atlas and generated HUD/menu prefabs/scenes. The traversal capture also saves the briefing screen. Simulation balance and the 99-day goal are unchanged.

Validation uses disposable Unity 2022.3.24f1 copies. Layout checks cover 1280×720 and 1920×1080, standard and large text, missing geometry and text overflow. PlayMode exercises keyboard/mouse, menu focus, research scrolling, pack blocking and terrain traversal. The extensive Balance-category policy runs remain excluded from the UI check.

[Briefing controls](open-world-briefing.png) · [Exploration HUD](open-world-hud.png) · [Pack](open-world-kit.png) · [Pause controls at 720p](pause-1280.png).

Final assertions: **162 EditMode passed** ([results](readability-editmode.xml)); **7 PlayMode passed** ([results](readability-playmode.xml)). The final graphics-enabled PlayMode process exited successfully. Briefing, HUD, pack and pause captures were inspected. One earlier headless EditMode process crashed during editor shutdown after saving passing results; the final graphics-enabled EditMode rerun passed all 162 tests and exited 0.

macOS build succeeded and the native launch returned P12_SMOKE_PASS with bundled data loaded.
WebGL build also succeeded. Current macOS/WebGL builds are in GuiBuilds/. No commit was made.

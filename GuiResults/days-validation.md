# 99 Martian day mission

The mission target is `GameConstants.MissionDurationDays = 99`. The HUD says MARTIAN DAYS and shows elapsed progress out of 99; the briefing, warning countdowns, energy/radiation units and mission report use day terminology. Completion, walking, supplies, pickups and timed actions share the new mission boundary.

The 668-row weather dataset and astronomical Mars-year conversion remain intact. Daily speed is unchanged: the open-world mission takes about 71 minutes at 1× from its late-afternoon arrival to day 99.

EditMode: **162 passed, 0 failed** ([results](days-editmode.xml)), including completion exactly at 99, no duplicate win/extra time after completion, death taking priority at the finish, no actions beyond the mission, full weather-schedule mapping with dispatch only through day 99, intact data loading, and standard/large UI layouts at 720p/1080p. The extensive Balance-category policy runs were not rerun; no new balancing targets were introduced. The expected pre-fix failure is retained in days-red.xml.

Changed files: `GameConstants.cs`, `SimClock.cs`, `Simulation.cs`, `ExpeditionSystem.cs`; player action/controller/pickup mission guards; HUD/briefing/report/alert/radiation UI; related simulation, action, weather, balance-run horizon and HUD tests; regenerated prefabs/scenes; play guide. No commit.

PlayMode: **7 passed, 0 failed** ([results](days-playmode.xml)). Updated [HUD](open-world-hud.png) and [pack](open-world-kit.png) screenshots were visually inspected; the longer day labels fit.

macOS build succeeded and the native launch returned P12_SMOKE_PASS with bundled data loaded.
WebGL build also succeeded. Both current builds are in GuiBuilds/.

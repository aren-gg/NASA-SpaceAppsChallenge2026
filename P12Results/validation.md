# P12 validation — 2026-11-15

| Policy | Runs | Survival | Death causes | Mean final dose (mSv) | Energy margin (kWh/sol) |
| --- | ---: | ---: | --- | ---: | ---: |
| idle | 1000 | 0.000% | dehydration:1000 | 1.725 | 70.769 |
| naive | 1000 | 25.300% | radiation:747 | 385.416 | 53.143 |
| good | 1000 | 84.600% | radiation:154 | 330.847 | 68.378 |

All targets pass. Raw CSV verified: 3,000 rows, exactly seeds 0–999 once per policy, finite measurements, and summary values consistent with raw rows.

EditMode: 152 passed, 0 failed, 0 skipped. Full suite duration: 45.23 minutes.
UI PlayMode: 4 passed, 0 failed, 0 skipped. Covers keyboard/mouse at 720p/1080p, HUD lifecycle, and saved action/skip buttons when the HUD loads after the mission.

macOS and WebGL builds succeeded with zero build errors using Unity 2022.3.24f1. Projects were exported with Assets, Packages and ProjectSettings only; no Library was copied. The final macOS launch exited 0 with P12_SMOKE_PASS. Chrome confirmed valid bundled JSON, responsive canvas sizing, volume and large-body-text controls. See [runtime smoke evidence](runtime-smoke.txt).

Review found and fixed missing prefab callback wiring and late HUD mission binding. Focused regression tests passed; deliberately inverting the text-size setting failed its test, then the disposable copy was restored byte-for-byte.

Balance changes affect GameConstants and the authored Cozy Shield value; existing simulation-system logic was not changed. Baseline and calibration CSVs are retained beside the final report. Mean dose includes deaths at termination; energy margin is the mean full-sol generation minus requested standing load, excluding discretionary medbay/lamp spending.

Open gates: a literal fresh-clone verification awaits the owner committing the finished source and earlier required untracked assets (no commit was made). The existing clock completes at 668 scheduled sols rather than the displayed 668.6-sol year; correcting that requires simulation logic outside the tuning constraint. WebGL engine diagnostics are recorded in the smoke evidence.

Build commands and changed-file scope: [P12 ship guide](../P12_SHIP.md).

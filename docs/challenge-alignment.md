# Challenge alignment

The [official 2026 Junior Astronaut Mission Trainer summary](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/) asks for a game/app where students operate a lunar or Martian outpost and experience tradeoffs among life support, radiation shielding, power and food production. The selected setting is Mars.

| Challenge goal | Implementation to verify in the integrated game |
| --- | --- |
| Operate an outpost | Existing habitat plus functional survival stations and mission lifecycle |
| Balance competing resources | Three adjustable power channels, solar production, storage, task energy and load shortages |
| Radiation protection decisions | Warnings, shelter, current dose rate and cumulative absorbed dose |
| Food-production decisions | Crops consume water/power and produce food; no electrical energy is created by food |
| Learn from success/failure | Tutorial, actionable warnings, history and mission debrief |
| Explore a real setting | Existing georeferenced Gale terrain, survey/sample tasks and source labels |

The user requires the three-knob physical dashboard. That requirement does not depend on whether the challenge separately mandates hardware.

The exact [2026 resources tab](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/?tab=resources) remains inaccessible through browsing during implementation. Its complete list and any additional submission rules are not verified. Do not claim full compliance or call the supplemental NASA sources a transcription of that tab.

Scientific evidence is split deliberately: the existing terrain is a prepared elevation dataset; Mars24 is an astronomical algorithm; air temperature uses quality-filtered REMS sol224 observations as a representative daily profile; the RAD baseline is derived from a historical summary; seasonal offsets and severe hazards are labeled models. See [mission environment](data/mission-environment.md) for data extraction and limitations, and [implementation ledger](implementation-status.md) for integrated test/build status.

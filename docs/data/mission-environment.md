# Mission environment

`MissionEnvironment.Sample(elapsedSeconds, startUnix, seed, latitude, longitude)` supplies physical input to the fixed-step survival simulation. It does not read shader brightness or weather transition weights. `LoadBundled()` loads the profile in `Assets/Data/Environment/Resources/`; the game needs no network connection.

The simulation owns permanent panel deposits, equipment condition, shielding, thermal response and installed upgrades. The environment returns ambient inputs only, avoiding duplicate attenuation or resource effects.

## Implemented inputs

| Input | Implementation and boundary |
| --- | --- |
| Sun | Existing NASA Mars24 arithmetic supplies elevation and Sun distance. Generation is zero below the geometric horizon. The normalized curve and dust attenuation are a simplified model. |
| Air temperature | Quality-filtered Curiosity REMS sol 224 ambient-air measurements averaged into 24 hourly points and interpolated cyclically. Reuse through the campaign and added season/thermal-event offsets are explicitly modeled. |
| Radiation | Historical RAD reference of approximately 210 microgray/day, represented as 8.75 microgray/hour using a documented 24-hour convention. Extra particle-event dose is simulated, not derived from a flare's display intensity. |
| Dust and particle events | Mission-seed hash determines onset, duration and severity, without mutable random-generator state. Both can overlap with night/thermal effects. Warnings begin 0.3 sol before onset. Every campaign length includes events, and the schedule continues after the archive ends. |
| DONKI | Existing archive records are historical context only. Predicted CME arrivals remain labeled as modeled arrivals. No record is converted into a measured surface dose. |
| Radio | Fictional local relay windows and a coordinate-dependent signal model. Zero signal closes transmission windows; this is not actual NASA communications coverage. |

Primary references: [NASA Mars24](https://www.giss.nasa.gov/tools/mars24/help/algorithm.html), [historical RAD measurements](https://science.nasa.gov/resource/radiation-measurements-on-mars/), and [NASA surface-power study](https://www.nasa.gov/wp-content/uploads/2024/01/mars-surface-power-generation-challenges-and-considerations.pdf). The RAD figure describes August 2012–June 2013; it is not a current or global surface measurement. Dose is absorbed dose, with no effective-dose or medical-threshold conversion.

## REMS observations and preparation

The maintained [PDS Mars archive](https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/data_archive.html) leads to the [Curiosity REMS archive](https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html). Its reduced-data collection is now a PDS4 bundle. The temperature example identifies Curiosity sol 224, but its older PDS3 link returns 404.

The corresponding available PDS4 directory contains:

- [Calibrated MODRDR table, P8](https://atmos.nmsu.edu/PDS/data/PDS4/msl_rems_bundle/data_calibrated/SOL_00180_00269/SOL00224/RME_417331939RMD02240000000_______P8.TAB), approximately 6.2 MB.
- [PDS4 XML label](https://atmos.nmsu.edu/PDS/data/PDS4/msl_rems_bundle/data_calibrated/SOL_00180_00269/SOL00224/RME_417331939RMD02240000000_______P8.xml), approximately 27 KB.
- [REMS RDR SIS](https://atmos.nmsu.edu/PDS/data/PDS4/msl_rems_bundle/document/REMS_RDR_SIS.PDF), issue 9.

The lead downloaded the numeric table and XML label into `Artifacts/REMS/`. `tools/data/prepare_rems_profile.ps1` reads the label's fixed-width field locations, verifies record length/count and ordered LMST, and imports `AMBIENT_TEMP` in Kelvin. The source covers 23 March 2013 17:38:11.697 UTC through 24 March 2013 17:21:21.489 UTC. Ground brightness temperature is not used.

Of 17,565 rows, 10,323 pass the quality rule and 7,242 are excluded. Temperature must be strictly inside 150–300 K; the nine-character confidence code must have `1` in positions 0, 1, 2, 7 and 8 (instrument conditions, operational range and observation reliability). Unknown wind factors remain a stated limitation, and complete confidence-code counts are retained. Each local-hour bin has at least 33 accepted samples. Celsius equals Kelvin minus 273.15.

The script averages temperature and LMST within each hourly bin. The largest gap between resulting points is 1.462945 local hours. The two midnight endpoints are interpolated cyclically; they are not additional measurements. Preparation fails if any hourly bin is empty or a gap exceeds two local hours. `rems_sol224_hourly.csv` preserves Kelvin/Celsius, counts, ranges and accepted confidence codes. The runtime JSON is 4,589 bytes; the prepared CSV is 1,933 bytes. Downloaded inputs total 6,562,338 bytes.

Regenerate with `powershell -NoProfile -File tools/data/prepare_rems_profile.ps1`, using the downloaded default paths or explicit `-LabelPath`/`-TablePath`. The script performs no network requests. `environment-provenance.json` records product ID, source URLs, original observation times, download-completion/preparation times, quality counts and SHA-256 hashes of inputs and prepared files.

The game reuses one historical sol as a daily shape. Seasonal variation, thermal-stress offsets and data reused outside the original place/time are models. The player-facing source label states this. If the runtime asset is missing or invalid, the code falls back to an authored curve and explicitly labels it as unmeasured.

## Checks

`powershell -NoProfile -File tools/data/check_mission_environment.ps1` checks the prepared JSON against its hash and CSV values, then runs 120,340 assertions over the full campaign span using both the fallback and measured profile. It covers repeatability, no night generation, bounded radiation/signal, cyclic temperature continuity, warning coverage and simultaneous dust/night effects. This uses the installed Unity C# compiler and a standalone Mono process; it is a numerical check, not a Unity player/rendering test. Actual Unity compilation and integrated gameplay remain the lead's verification work.

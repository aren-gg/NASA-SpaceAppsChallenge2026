# Mars clock, day-night cycle and weather events

The game runs on real Mars time and replays real solar events from NASA's DONKI catalogue. Events DONKI does not record are simulated and labelled as such.

## Mars time

`Assets/Scripts/Environment/MarsTime.cs` implements the Mars24 algorithm (Allison and McEwen 2000, published by [NASA GISS](https://www.giss.nasa.gov/tools/mars24/help/algorithm.html)). For a UTC instant and a place on Mars it gives:

| Value | Meaning |
| --- | --- |
| Mars Sol Date and local sol | Sols counted continuously; the info tab shows the local sol number |
| Local Mean Solar Time (LMST) | The clock time shown on the info tab |
| Local True Solar Time | Where the Sun really is; noon is the Sun's highest point |
| Solar longitude Ls and Mars Year | The season; Ls 0° is northern spring equinox |
| Sun direction and distance | Drives the directional light and its brightness |

Checked against the published worked example (2000-01-06 00:00 UTC: Mars Sol Date 44795.99976, Coordinated Mars Time 23:59:39, Ls 277.18758°, equation of time −5.18774°, Sun distance 1.39358 au). It also puts Curiosity's landing at 15:03 LMST and its sol 4000 on 2023-11-07.

One sol is 24 h 39 m 35.244 s (88,775.244 s). `MarsDayNightCycle` runs the clock about 74 times faster than real time so a sol takes 20 minutes (`realSecondsPerSol = 1200`). The latitude and longitude come from where the player stands on the terrain, so local time shifts by up to 14 minutes across the 204 km map.

The clock starts at 07:00 LMST on the morning of the first event in the bundled archive (2025-11-24). Set `startUtc` on the component to start elsewhere.

## Solar events: NASA DONKI

Source: [DONKI](https://ccmc.gsfc.nasa.gov/tools/DONKI/), the Space Weather Database Of Notifications, Knowledge, Information, run by NASA's Community Coordinated Modeling Center. `tools/data/fetch_donki_events.py` downloads it once, offline; the game never contacts the network.

| DONKI record | How it becomes an event at Mars | Kept (2025-11-15 to 2026-11-15) |
| --- | --- | --- |
| `FLR` solar flares, GOES class M1 and above | Kept only if the flare is on the half of the Sun facing Mars, worked out from the flare's position on the disk and where Mars and Earth are in their orbits. Times are shifted by the light-travel difference (4 to 5 minutes in this window). In the game a flare shows only while the Sun is up. | 93 of 404 listed (106 below M1, 205 facing away) |
| `WSAEnlilSimulations` | Model runs that list an impact at Mars give a predicted CME arrival time there. Runs within 12 hours of each other are merged. | 92 arrivals from 190 model impacts in 1,204 runs |
| `SEP` solar energetic particles | Kept only if detected by an instrument at Mars. | 0 of 37 listed (all detected elsewhere) |

All three show as the weather module's Solar Flare visuals, with the event's own name on the info tab.

Data use:

| | Bytes |
| --- | --- |
| Downloaded from DONKI (21 requests) | 1,260,340 |
| Bundled in the game (`Assets/Data/Weather/Resources/donki_mars_events.json`) | 75,218 |
| In memory | 185 small event records |

Limits to state plainly:

- CME arrivals are WSA-ENLIL+Cone model predictions, not detections at Mars.
- DONKI gives only an arrival time for a CME, so each passage is given a nominal 12 hours.
- Each event's strength (0 to 1) is a display intensity from flare class or CME speed. It is not a radiation dose.
- Planet positions use mean orbital elements with the equation of centre, good to about 1°.
- Short flares are shown for at least 1.5 Mars hours so they are not over in seconds at 74× speed.
- When the clock passes the end of the archive (2026-11-15), no further solar events occur and the info tab says so. Rerun the tool to extend it.

## Events DONKI does not have

DONKI covers the Sun and interplanetary space. It has no Mars dust or temperature records, so `MarsWeatherSchedule` generates these from a seed (the same sol always has the same weather):

| Event | Rule | Basis |
| --- | --- | --- |
| Dust storm | About one every 30 sols in the dust-storm season (Ls 180–360°), one every 90 sols otherwise; lasts 1 to 4 sols | Simulated. The season follows the well-established Mars dust-storm season. A local storm recorded by Curiosity spread over Gale for about five sols at Ls 263° ([Ordóñez-Etxeberria et al.](https://ui.adsabs.harvard.edu/abs/2019Icar..317..591O)). I found no published count of storms per year at Gale, so the 30-sol interval is the team's assumption. |
| Heat storm | One afternoon about every 30 sols, 11:00 to 17:00 true solar time, strongest at 14:00 | Simulated; the team's 30-sol assumption. Mars has no recorded "heat storm". Curiosity's warmest air temperatures are summer afternoons a little above freezing. |
| Cold night | Every night, following the Sun | Curiosity's REMS station records nights far below freezing all year, with a typical day-to-night swing of more than 55 °C ([JPL](https://www.jpl.nasa.gov/images/seasonal-cycles-in-curiositys-first-two-martian-years)). |

One event shows at a time, in this order: solar, dust, heat, cold night.

## Components

All added automatically when a scene with the terrain starts (`Assets/Scripts/Integration/MarsEnvironmentBootstrap.cs`):

| Path under `My project/Assets/` | Role |
| --- | --- |
| `Scripts/Environment/MarsTime.cs` | Mars time and Sun position arithmetic |
| `Scripts/Environment/MarsDayNightCycle.cs` | Clock, Sun direction, day and night lighting, Sun disk |
| `Scripts/Environment/MarsWeatherArchive.cs` | Reads the bundled DONKI events |
| `Scripts/Environment/MarsWeatherSchedule.cs` | Chooses the current event and activates it on the weather module |
| `Scripts/UI/MarsClockHud.cs` | Info tab, top right |

The weather module is not edited. It writes midday sky, fog and light values each frame; the day-night cycle runs after it and scales them for the time of day. The weather sky draws its Sun at a fixed direction, so that disk is switched off and `MarsDayNightCycle` draws one that follows the real Sun (drawn at 0.6° across; the real Sun from Mars is about 0.35°).

For other systems:

```csharp
MarsTime.State now = cycle.Now;        // sol, LMST, Ls, Sun elevation and direction
float daylight = cycle.Daylight;        // 0 at night, 1 in full sun: solar panel input
float solar = schedule.SolarStrength;   // 0-1 display strength of the DONKI event in progress
var donkiEvent = schedule.ActiveSolarEvent; // label, kind, DONKI link; null when none
float dust = schedule.DustStrength, heat = schedule.HeatStrength, night = schedule.NightAmount;
```

Test keys: `]` and `[` step the clock one Mars hour forward and back. Keys 1–5 still switch weather by hand; the schedule takes over again after 45 seconds.

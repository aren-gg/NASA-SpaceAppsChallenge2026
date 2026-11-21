# P1 DATA — V1
Implement the user-supplied P1 contract: offline Python preparation → bundled JSON → Unity Data repository.

- Build order: fetch/normalize RAD, REMS, DONKI → merge 668 sols; DTOs → repository → EditMode integration tests.
- Schedule is a JSON array, sols 0–667, Ls [0,360), Celsius [-150,50], tau [0,10], GCR [0,2000] uGy/day, flare integer [0,3], boolean alert, nonempty source.
- REMS pressure (Pa) and UV remain in intermediate rows. Tau is an explicitly approximate UV attenuation proxy; categorical UV uses a documented mapping. Missing UV makes the merged row synthetic.
- Fixed seed 668; Ls advances linearly for gameplay. Dust increases at Ls 180–360; synthetic flares occur in multi-sol clusters.
- Source rows align by sol or UTC date against a fixed epoch (2024-01-01), 88775.244 seconds/sol. Never stretch a short feed to pretend it covers a year. Any missing component marks the merged row synthetic.
- Default build is fully offline. Optional --online fetches have bounded timeout and raw cache; no API key stored. Missing/malformed data falls back per sol. No runtime remote requests.
- Unity: DataRepository.LoadScheduleJson with Newtonsoft and range validation; MissionDataSources reads StreamingAssets. Missing/invalid files reset to deterministic defaults and emit warnings.
- Tunables: Python config.json and pure-C# GameConstants; no Sim UnityEngine dependencies. One C# class/file, Data asmdef.
- Tests: Python unittest for deterministic, partial, malformed and unavailable sources; Unity EditMode for bundled files, validation and missing-file warning/defaults.
- Commands: python3 -m unittest discover -s Tools/tests; python3 Tools/build_schedule.py.
- Boundaries: preserve unrelated user changes; no commits, Library tracking, runtime networking, sim integration, UI or scene changes.

Sources and limitations:
- RAD baseline: https://science.nasa.gov/resource/radiation-measurements-on-mars/ (~210 uGy/day). Published daily CSV can be supplied with --input; unsupported archive formats fall back rather than being mislabeled observed.
- REMS: https://mars.nasa.gov/rss/api/?feed=weather&category=msl&feedtype=json ; https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/curiosity/rems.html
- DONKI: https://api.nasa.gov/ ; https://ccmc.gsfc.nasa.gov/tools/DONKI/ . FLR and CME are gameplay drivers, not a forecast of Mars aurora.
- Veggie: https://www.nasa.gov/wp-content/uploads/2019/04/veggie_fact_sheet_508.pdf ; https://taskbook.nasaprs.com/tbp/index.cfm?TASKID=AEB3CD6E42487F8A7EB8C1112E0B449F&action=public_query_taskbook_content . Kcal, water and PAR are provisional per-plant estimates, not NASA-certified yields.

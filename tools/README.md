python3 Tools/fetch_rad.py --input /path/to/daily_rad.csv
python3 Tools/fetch_rems.py --online
NASA_API_KEY=your_key python3 Tools/fetch_donki.py --online
python3 Tools/build_schedule.py
python3 -m unittest discover -s Tools/tests
# Fully offline: skip fetch commands; builders use cached inputs or seeded defaults.
# RAD CSV: sol,gcrDose_uGy (schedule-relative sol); optional RAD_DAILY_URL for --online.
# Units, sources and proxy limits: Tools/SPEC.md.

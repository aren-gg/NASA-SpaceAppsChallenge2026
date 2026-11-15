"""Shared offline generation and bounded, key-free raw cache storage."""
import argparse
from datetime import datetime, timezone, timedelta
import json
import math
from pathlib import Path
import random
import warnings
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parent
CONFIG = json.loads((ROOT / 'config.json').read_text())
OUTPUT = ROOT.parent / 'My project' / 'Assets' / 'StreamingAssets'
CACHE = ROOT / 'cache'


def write(path, rows):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(rows, indent=2, allow_nan=False) + '\n')


def raw(name, url, online=False, cache=CACHE):
    path = Path(cache) / (name + '.raw')
    try:
        if path.exists():
            return path.read_text()
        if online and url:
            with urlopen(url, timeout=CONFIG['timeoutSeconds']) as response:
                payload = response.read(CONFIG['maxBytes'] + 1)
            if len(payload) > CONFIG['maxBytes']:
                raise ValueError('response too large')
            value = payload.decode('utf-8-sig')
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(value)
            return value
    except (OSError, ValueError, TimeoutError):
        # Deliberately never log the URL: DONKI query includes an API key.
        warnings.warn(name + ': unavailable; using synthetic data')
    return None


def in_range(value, kind):
    lo, hi = CONFIG['ranges'][kind]
    return isinstance(value, (float, int)) and not isinstance(value, bool) and math.isfinite(value) and lo <= value <= hi


def sol_from_date(value):
    if not isinstance(value, str):
        raise ValueError('Expected an ISO timestamp')
    epoch = datetime.fromisoformat(CONFIG['epoch']).replace(tzinfo=timezone.utc)
    date = datetime.fromisoformat(value.replace('Z', '+00:00'))
    if date.tzinfo is None:
        date = date.replace(tzinfo=timezone.utc)
    return math.floor((date - epoch).total_seconds() / CONFIG['secondsPerSol'])


def end_date():
    return (datetime.fromisoformat(CONFIG['epoch']) + timedelta(
        seconds=CONFIG['sols'] * CONFIG['secondsPerSol'])).date().isoformat()


def synthetic():
    rng = random.Random(CONFIG['seed'])
    rows = []
    for sol in range(CONFIG['sols']):
        ls = sol * 360 / CONFIG['sols']
        season = math.sin(math.radians(ls))
        low = CONFIG['minTemp'] + CONFIG['tempSeasonAmplitude'] * season + rng.uniform(-CONFIG['tempNoise'], CONFIG['tempNoise'])
        phase = sol % CONFIG['flarePeriod']
        flare = 1 + (sol // CONFIG['flarePeriod']) % 3 if phase < CONFIG['flareClusterLength'] else 0
        rows.append(dict(sol=sol, ls=round(ls, 4), minTemp=round(low, 2),
                         maxTemp=round(low + CONFIG['dailyTempSpan'], 2),
                         pressurePa=round(CONFIG['pressurePa'] + CONFIG['pressureAmplitude'] * season, 2),
                         dustOpacity=round(CONFIG['clearTau'] + (CONFIG['dustTau'] if ls >= 180 else 0) + rng.random() * CONFIG['tauNoise'], 3),
                         gcrDose_uGy=round(CONFIG['gcrBaseline'] + rng.uniform(-CONFIG['gcrVariation'], CONFIG['gcrVariation']), 2),
                         flareLevel=flare, auroraAlert=flare >= 2, source='synthetic'))
    return rows


def cli(description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument('--online', action='store_true', help='Permit bounded source requests; default is offline')
    parser.add_argument('--input', type=Path, help='Local source file')
    parser.add_argument('--output', type=Path)
    return parser

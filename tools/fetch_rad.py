"""Curiosity RAD daily CSV: sol,gcrDose_uGy (uGy/day; not dose equivalent)."""
import csv
import io
import os
from data_common import CACHE, CONFIG, cli, in_range, raw, synthetic, write


def parse(text):
    rows = []
    for row in csv.DictReader(io.StringIO(text)):
        try:
            sol, dose = int(row['sol']), float(row['gcrDose_uGy'])
            if 0 <= sol < CONFIG['sols'] and in_range(dose, 'gcr'):
                rows.append(dict(sol=sol, gcrDose_uGy=dose, source='RAD'))
        except (KeyError, TypeError, ValueError):
            continue
    return rows


def fetch(online=False, cache=CACHE, input_path=None):
    try:
        value = input_path.read_text() if input_path else raw('rad', os.environ.get('RAD_DAILY_URL', CONFIG['radUrl']), online, cache)
        return (parse(value) if value else []) or synthetic()
    except (OSError, ValueError, TypeError):
        return synthetic()


if __name__ == '__main__':
    args = cli(__doc__).parse_args()
    write(args.output or CACHE / 'rad.json', fetch(args.online, input_path=args.input))

"""Merge by actual sol; missing components retain deterministic defaults."""
import json
from data_common import CACHE, CONFIG, OUTPUT, cli, in_range, synthetic, write
import fetch_rad
import fetch_rems
import fetch_donki

FIELDS = ('sol', 'ls', 'minTemp', 'maxTemp', 'dustOpacity', 'gcrDose_uGy', 'flareLevel', 'auroraAlert', 'source')


def valid(row):
    try:
        return (type(row['sol']) is int and 0 <= row['sol'] < CONFIG['sols'] and 0 <= row['ls'] < 360
                and in_range(row['minTemp'], 'temp') and in_range(row['maxTemp'], 'temp')
                and row['minTemp'] <= row['maxTemp'] and in_range(row['dustOpacity'], 'tau')
                and in_range(row['gcrDose_uGy'], 'gcr') and type(row['flareLevel']) is int
                and 0 <= row['flareLevel'] <= 3 and type(row['auroraAlert']) is bool
                and isinstance(row['source'], str) and bool(row['source'].strip()))
    except (KeyError, TypeError):
        return False


def build(rad=None, weather=None, events=None):
    rows = synthetic()
    groups = ((rad or [], ('gcrDose_uGy',)), (weather or [], ('minTemp', 'maxTemp', 'dustOpacity')),
              (events or [], ('flareLevel', 'auroraAlert')))
    origins = [[] for _ in rows]
    for records, fields in groups:
        seen = set()
        for record in records:
            sol = record.get('sol')
            if type(sol) is not int or not 0 <= sol < len(rows) or sol in seen:
                continue
            candidate = dict(rows[sol])
            if not all(field in record for field in fields):
                continue
            candidate.update({field: record[field] for field in fields})
            if valid(candidate):
                rows[sol] = candidate
                source = record.get('source')
                origins[sol].append(source if isinstance(source, str) and source.strip() else 'synthetic')
                seen.add(sol)
    for row, sources in zip(rows, origins):
        row['source'] = '+'.join(sources) if len(sources) == len(groups) and all(s and s != 'synthetic' for s in sources) else 'synthetic'
    return [{key: row[key] for key in FIELDS} for row in rows]


def prepared(name, fetcher, online):
    path = CACHE / (name + '.json')
    if not online and path.exists():
        try:
            rows = json.loads(path.read_text())
            if isinstance(rows, list) and all(isinstance(row, dict) for row in rows):
                return rows
        except (ValueError, OSError):
            pass
    return fetcher(online=online)


if __name__ == '__main__':
    args = cli(__doc__).parse_args()
    write(args.output or OUTPUT / 'mars_year_schedule.json', build(
        prepared('rad', fetch_rad.fetch, args.online), prepared('rems', fetch_rems.fetch, args.online),
        prepared('donki', fetch_donki.fetch, args.online)))

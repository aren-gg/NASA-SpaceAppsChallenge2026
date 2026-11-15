"""DONKI FLR + CME: historical activity drives gameplay, not Mars forecasts."""
import json
import os
from urllib.parse import urlencode
from data_common import CACHE, CONFIG, cli, end_date, raw, sol_from_date, synthetic, write


def parse(flares, cmes):
    if not isinstance(flares, list) or not isinstance(cmes, list):
        raise ValueError('Expected event arrays')
    rows = [dict(sol=sol, flareLevel=0, auroraAlert=False, source='DONKI') for sol in range(CONFIG['sols'])]
    for events, is_cme in ((flares, False), (cmes, True)):
        for event in events:
            try:
                sol = sol_from_date(event['startTime' if is_cme else 'beginTime'])
                if 0 <= sol < len(rows):
                    row = rows[sol]
                    if is_cme:
                        row['auroraAlert'] = True
                    else:
                        level = {'X': 3, 'M': 2, 'C': 1}.get(event['classType'][0], 0)
                        row['flareLevel'] = max(row['flareLevel'], level)
                        row['auroraAlert'] |= level >= 2
            except (KeyError, TypeError, ValueError, IndexError):
                continue
    return rows


def fetch(online=False, cache=CACHE, input_path=None):
    try:
        if input_path:
            data = json.loads(input_path.read_text())
            return parse(data['FLR'], data['CME'])
        query = urlencode(dict(startDate=CONFIG['epoch'], endDate=end_date(),
                               api_key=os.environ.get('NASA_API_KEY') or 'DEMO_KEY'))
        flr = raw('donki_flr', 'https://api.nasa.gov/DONKI/FLR?' + query, online, cache)
        cme = raw('donki_cme', 'https://api.nasa.gov/DONKI/CME?' + query, online, cache)
        if flr is not None and cme is not None:
            return parse(json.loads(flr), json.loads(cme))
    except (OSError, ValueError, TypeError, KeyError):
        pass
    return synthetic()


if __name__ == '__main__':
    args = cli(__doc__).parse_args()
    write(args.output or CACHE / 'donki.json', fetch(args.online, input_path=args.input))

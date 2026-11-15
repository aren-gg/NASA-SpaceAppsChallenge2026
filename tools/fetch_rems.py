"""REMS min/max air Celsius, pressure Pa and UV attenuation tau proxy."""
import json
import math
from data_common import CACHE, CONFIG, cli, in_range, raw, sol_from_date, synthetic, write


def parse(data):
    rows = []
    for row in data.get('soles', []):
        try:
            sol = sol_from_date(row['terrestrial_date']) if row.get('terrestrial_date') else int(row['sol'])
            low, high, pressure = float(row['min_temp']), float(row['max_temp']), float(row['pressure'])
            uv = row.get('uv_irradiance', row.get('local_uv_irradiance_index'))
            # Categories represent approximate tau, not measured irradiance.
            category = str(uv).lower().replace(' ', '_')
            tau = CONFIG['uvCategories'].get(category)
            if tau is None:
                uv = float(uv)
                if not math.isfinite(uv) or not 0 < uv <= CONFIG['clearUv']:
                    continue
                tau = -math.log(uv / CONFIG['clearUv'])
            if (0 <= sol < CONFIG['sols'] and in_range(low, 'temp') and in_range(high, 'temp')
                    and low <= high and in_range(pressure, 'pressure') and in_range(tau, 'tau')):
                rows.append(dict(sol=sol, minTemp=low, maxTemp=high, pressurePa=pressure,
                                 uv=uv, dustOpacity=round(tau, 3), source='REMS'))
        except (KeyError, TypeError, ValueError):
            continue
    return rows


def fetch(online=False, cache=CACHE, input_path=None):
    try:
        value = input_path.read_text() if input_path else raw('rems', CONFIG['remsUrl'], online, cache)
        return (parse(json.loads(value)) if value else []) or synthetic()
    except (OSError, ValueError, TypeError, AttributeError):
        return synthetic()


if __name__ == '__main__':
    args = cli(__doc__).parse_args()
    write(args.output or CACHE / 'rems.json', fetch(args.online, input_path=args.input))

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_schedule
import build_crops
import fetch_rad
import fetch_rems
import fetch_donki


class DataTests(unittest.TestCase):
    def test_offline_year_is_deterministic_and_seasonal(self):
        rows = build_schedule.build()
        self.assertEqual(rows, build_schedule.build())
        self.assertEqual(len(rows), 668)
        for sol, row in enumerate(rows):
            self.assertEqual(row['sol'], sol)
            self.assertTrue(build_schedule.valid(row))
            self.assertEqual(row['source'], 'synthetic')
        clear = [r['dustOpacity'] for r in rows if r['ls'] < 180]
        dusty = [r['dustOpacity'] for r in rows if r['ls'] >= 180]
        self.assertGreater(sum(dusty) / len(dusty), sum(clear) / len(clear))
        self.assertTrue(any(a['flareLevel'] and b['flareLevel'] for a, b in zip(rows, rows[1:])))

    def test_partial_sources_do_not_repeat(self):
        rad = [{'sol': 0, 'gcrDose_uGy': 212, 'source': 'RAD'}]
        weather = [{'sol': 0, 'minTemp': -80, 'maxTemp': -10, 'pressurePa': 800,
                    'dustOpacity': 0.4, 'source': 'REMS'}]
        events = [{'sol': 0, 'flareLevel': 2, 'auroraAlert': True, 'source': 'DONKI'}]
        rows = build_schedule.build(rad, weather, events)
        self.assertEqual(rows[0]['gcrDose_uGy'], 212)
        self.assertNotEqual(rows[0]['source'], 'synthetic')
        self.assertEqual(rows[1]['source'], 'synthetic')
        self.assertEqual(build_schedule.build([{'sol': 0, 'gcrDose_uGy': -1}], weather, events)[0]['source'], 'synthetic')

    def test_rad_csv_and_rems_feed(self):
        self.assertEqual(fetch_rad.parse('sol,gcrDose_uGy\n0,210\n1,-4\n')[0]['gcrDose_uGy'], 210)
        rows = fetch_rems.parse({'soles': [{'sol': '0', 'min_temp': '-80', 'max_temp': '-10',
                                           'pressure': '800', 'uv_irradiance': '2.5'}]})
        self.assertEqual(rows[0]['pressurePa'], 800)
        self.assertGreater(rows[0]['dustOpacity'], 0)
        self.assertEqual(fetch_rems.parse({'soles': [{'sol': 0, 'min_temp': -80, 'max_temp': -10}]}), [])

    def test_donki_dates_and_flare_levels(self):
        rows = fetch_donki.parse([{'beginTime': '2024-01-01T01:00Z', 'classType': 'X1.0'}],
                                [{'startTime': '2024-01-01T02:00Z'}])
        self.assertEqual(rows[0]['sol'], 0)
        self.assertEqual(rows[0]['flareLevel'], 3)
        self.assertTrue(rows[0]['auroraAlert'])
        self.assertEqual(fetch_donki.parse([], [])[0]['flareLevel'], 0)

    def test_unavailable_sources_fall_back(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch('data_common.urlopen', side_effect=TimeoutError):
                for fetch in (fetch_rad.fetch, fetch_rems.fetch, fetch_donki.fetch):
                    rows = fetch(online=True, cache=Path(directory))
                    self.assertEqual(len(rows), 668)
                    self.assertTrue(all(r['source'] == 'synthetic' for r in rows))

    def test_bad_cache_shapes_and_sources_do_not_crash(self):
        self.assertEqual(build_schedule.build([{'sol': 0, 'gcrDose_uGy': 210, 'source': 3}])[0]['source'], 'synthetic')
        with tempfile.TemporaryDirectory() as directory:
            for name in ('rad', 'rems', 'donki_flr', 'donki_cme'):
                (Path(directory) / (name + '.raw')).write_text('{"invalid":true}')
            for fetch in (fetch_rad.fetch, fetch_rems.fetch, fetch_donki.fetch):
                self.assertEqual(len(fetch(cache=Path(directory))), 668)

    def test_rems_aligns_dates_and_uv_proxy_direction(self):
        feed = {'soles': [dict(sol=4000, terrestrial_date='2024-01-01', min_temp=-80,
                               max_temp=-10, pressure=800, local_uv_irradiance_index=uv)
                          for uv in ('Low', 'High')]}
        rows = fetch_rems.parse(feed)
        self.assertEqual(rows[0]['sol'], 0)
        self.assertGreater(rows[0]['dustOpacity'], rows[1]['dustOpacity'])

    def test_raw_cache_is_reused_without_network(self):
        from data_common import raw
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / 'sample.raw').write_text('cached')
            with patch('data_common.urlopen', side_effect=AssertionError('network called')):
                self.assertEqual(raw('sample', 'https://example.invalid', False, Path(directory)), 'cached')

    def test_donki_malformed_events_are_ignored(self):
        rows = fetch_donki.parse([None, {'beginTime': 12, 'classType': 'X1'}], [None])
        self.assertTrue(all(r['flareLevel'] == 0 for r in rows))

    def test_crop_contract(self):
        crops = build_crops.build()
        self.assertGreaterEqual(len(crops), 3)
        self.assertEqual(len({r['id'] for r in crops}), len(crops))
        for crop in crops:
            self.assertTrue(crop['needsVerification'])
            self.assertGreater(crop['daysToHarvest'], 0)
            self.assertTrue(0 < crop['lightHours'] <= 24)
            self.assertGreater(crop['kcalPerHarvest'], 0)


if __name__ == '__main__':
    unittest.main()

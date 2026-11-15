#!/usr/bin/env python3
"""Build the game's solar-event schedule for Mars from NASA's DONKI catalogue.

Offline data preparation only (never runs in the game). Standard library only.

Source
------
DONKI, the Space Weather Database Of Notifications, Knowledge, Information,
run by NASA's Community Coordinated Modeling Center (CCMC):

    https://ccmc.gsfc.nasa.gov/tools/DONKI/
    web service: https://ccmc.gsfc.nasa.gov/DONKI-API/get/<type>?startDate=&endDate=

The service accepts at most 60 days per request, so the window is fetched in
slices. Every byte received is counted and written into the output file.

What is taken, and how it is turned into an event *at Mars*
----------------------------------------------------------
FLR  Solar flares (GOES X-ray class, observed from Earth). A flare lights only
     the half of the Sun it sits on. The flare's position on the solar disk as
     seen from Earth is compared with where Mars is around the Sun, and the
     flare is kept only if it is on the hemisphere facing Mars. Its times are
     shifted by the difference in light-travel time between Earth and Mars.
     Flares below class M1 are skipped (--min-flare-class).
WSAEnlilSimulations
     CCMC's WSA-ENLIL+Cone model runs of coronal mass ejections. Runs that list
     an impact at Mars give a *predicted* arrival time there. Several runs
     often describe the same ejection, so arrivals closer than 12 h are merged
     and the most recently completed run is kept.
SEP  Solar energetic particle events. DONKI records these at the spacecraft
     that detected them (mostly near Earth). They are kept as events only when
     the detecting instrument is at Mars; the others are counted in the notes.

What DONKI does not have: dust storms, temperature extremes, anything about
the Martian lower atmosphere. The game generates those itself and labels them
as simulated (see docs/data/weather-events.md).

Simplifications, stated so nobody mistakes this for a measurement
----------------------------------------------------------------
- Planet positions use mean orbital elements plus the equation of centre
  (about 1 degree accuracy), which is ample for a "which side of the Sun" test.
- A CME passage is given a nominal 12 h duration; DONKI gives only the arrival.
- "strength" (0-1) is a display intensity for the weather visuals, derived
  from flare class or CME speed. It is not a radiation dose.

Usage
-----
    python tools/data/fetch_donki_events.py                  # the last 365 days
    python tools/data/fetch_donki_events.py --start 2024-05-01 --end 2024-06-30

Output
    My project/Assets/Data/Weather/Resources/donki_mars_events.json
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://ccmc.gsfc.nasa.gov/DONKI-API/get"
USER_AGENT = "donki-mars-events/1.0 (offline data preparation)"
MAX_DAYS_PER_REQUEST = 60
AU_LIGHT_SECONDS = 499.004784
CME_PASSAGE_HOURS = 12.0
CME_MERGE_HOURS = 12.0
SEP_HOURS = 24.0
UTC = dt.timezone.utc


class Fetcher:
    def __init__(self) -> None:
        self.bytes = 0
        self.requests = 0

    def get(self, kind: str, start: dt.date, end: dt.date) -> list:
        url = f"{API}/{kind}?startDate={start.isoformat()}&endDate={end.isoformat()}"
        last_error = None
        for attempt in range(4):
            try:
                req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
                with urllib.request.urlopen(req, timeout=120) as resp:
                    body = resp.read()
                self.requests += 1
                self.bytes += len(body)
                text = body.decode("utf-8").strip()
                return json.loads(text) if text else []
            except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
                last_error = exc
                time.sleep(2 + 3 * attempt)
        raise RuntimeError(f"DONKI request failed: {url}: {last_error}")

    def get_window(self, kind: str, start: dt.date, end: dt.date) -> list:
        out = []
        cursor = start
        while cursor <= end:
            stop = min(end, cursor + dt.timedelta(days=MAX_DAYS_PER_REQUEST - 1))
            out.extend(self.get(kind, cursor, stop))
            cursor = stop + dt.timedelta(days=1)
        return out


def parse_time(text: str) -> dt.datetime:
    """DONKI times look like 2026-09-30T02:30Z (sometimes with seconds)."""
    text = text.strip().rstrip("Z")
    for fmt in ("%Y-%m-%dT%H:%M", "%Y-%m-%dT%H:%M:%S"):
        try:
            return dt.datetime.strptime(text, fmt).replace(tzinfo=UTC)
        except ValueError:
            pass
    raise ValueError(f"unrecognised DONKI time: {text!r}")


def iso(t: dt.datetime) -> str:
    return t.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------- where the planets are

def days_since_j2000(t: dt.datetime) -> float:
    return (t - dt.datetime(2000, 1, 1, 12, tzinfo=UTC)).total_seconds() / 86400.0


def earth_position(t: dt.datetime) -> tuple[float, float]:
    """Heliocentric ecliptic longitude (degrees) and distance (au)."""
    d = days_since_j2000(t)
    m = math.radians((357.52911 + 0.98560028 * d) % 360.0)
    sun_longitude = 280.46646 + 0.98564736 * d + 1.914602 * math.sin(m) + 0.019993 * math.sin(2 * m)
    r = 1.000140612 - 0.016708617 * math.cos(m) - 0.000139589 * math.cos(2 * m)
    return (sun_longitude + 180.0) % 360.0, r


def mars_position(t: dt.datetime) -> tuple[float, float]:
    """Heliocentric ecliptic longitude (degrees) and distance (au)."""
    d = days_since_j2000(t)
    m = math.radians((19.3871 + 0.52402073 * d) % 360.0)
    centre = (10.691 * math.sin(m) + 0.623 * math.sin(2 * m) + 0.050 * math.sin(3 * m)
              + 0.005 * math.sin(4 * m) + 0.0005 * math.sin(5 * m))
    longitude_of_perihelion = 336.04084 + 1.21e-5 * d
    longitude = (math.degrees(m) + centre + longitude_of_perihelion) % 360.0
    e = 0.09340
    nu = m + math.radians(centre)
    r = 1.523679 * (1 - e * e) / (1 + e * math.cos(nu))
    return longitude, r


def wrap180(deg: float) -> float:
    return (deg + 180.0) % 360.0 - 180.0


def flare_faces_mars(source_location: str, t: dt.datetime) -> tuple[bool, float] | None:
    """Is a flare at e.g. 'N11W48' (as seen from Earth) on the side of the Sun facing Mars?

    Heliographic west is the direction the planets orbit, so Mars sits at solar
    longitude (Mars - Earth) west of the centre of the disk seen from Earth.
    Returns (faces, angle from the point under Mars in degrees), or None if the
    position cannot be read.
    """
    match = re.fullmatch(r"([NS])(\d{1,2})([EW])(\d{1,3})", (source_location or "").strip().upper())
    if not match:
        return None
    lat = float(match.group(2)) * (1 if match.group(1) == "N" else -1)
    lon_west = float(match.group(4)) * (1 if match.group(3) == "W" else -1)
    mars_west = wrap180(mars_position(t)[0] - earth_position(t)[0])
    cos_sep = math.cos(math.radians(lat)) * math.cos(math.radians(wrap180(lon_west - mars_west)))
    separation = math.degrees(math.acos(max(-1.0, min(1.0, cos_sep))))
    return separation < 90.0, separation


def flare_magnitude(class_type: str) -> float | None:
    """Peak X-ray flux as a log scale: C1 = 0, M1 = 1, X1 = 2, X10 = 3."""
    match = re.fullmatch(r"([ABCMX])(\d+(?:\.\d+)?)", (class_type or "").strip().upper())
    if not match:
        return None
    return "ABCMX".index(match.group(1)) - 2 + math.log10(max(0.1, float(match.group(2))))


# ---------------------------------------------------------------- event builders

def build_flares(raw: list, min_magnitude: float, stats: dict) -> list:
    events = []
    for f in raw:
        stats["flaresListed"] += 1
        magnitude = flare_magnitude(f.get("classType"))
        if magnitude is None or magnitude < min_magnitude or not f.get("beginTime"):
            stats["flaresBelowClass"] += 1
            continue
        begin = parse_time(f["beginTime"])
        facing = flare_faces_mars(f.get("sourceLocation"), begin)
        if facing is None:
            stats["flaresWithoutPosition"] += 1
            continue
        if not facing[0]:
            stats["flaresFacingAway"] += 1
            continue
        end = parse_time(f["endTime"]) if f.get("endTime") else begin + dt.timedelta(minutes=30)
        # Seen at Earth at `begin`; the same light reaches Mars this much later (or earlier).
        delay = (mars_position(begin)[1] - earth_position(begin)[1]) * AU_LIGHT_SECONDS
        begin_mars = begin + dt.timedelta(seconds=delay)
        region = f.get("activeRegionNum")
        events.append({
            "kind": "flare",
            "id": f.get("flrID", ""),
            "startUtc": iso(begin_mars),
            "startUnix": int(begin_mars.timestamp()),
            "durationHours": round(max(0.1, (end - begin).total_seconds() / 3600.0), 3),
            # M1 -> 0.4, X1 -> 0.7, X10 and above -> 1.0
            "strength": round(max(0.25, min(1.0, 0.4 + 0.3 * (magnitude - 1.0))), 2),
            "label": f"{f['classType']} solar flare" + (f" (AR {region})" if region else ""),
            "basis": f"DONKI FLR, GOES class {f['classType']} at {f.get('sourceLocation')}; "
                     f"{facing[1]:.0f} deg from the point under Mars; light-time shift {delay:+.0f} s",
            "link": f.get("link", ""),
        })
        stats["flaresKept"] += 1
    return events


def build_cme_arrivals(raw: list, start: dt.datetime, end: dt.datetime, stats: dict) -> list:
    arrivals = []
    for sim in raw:
        stats["enlilRuns"] += 1
        for impact in sim.get("impactList") or []:
            if impact.get("location") != "Mars" or not impact.get("arrivalTime"):
                continue
            arrival = parse_time(impact["arrivalTime"])
            if not (start <= arrival <= end):
                continue
            speeds = [c.get("speed") for c in (sim.get("cmeInputs") or []) if c.get("speed")]
            arrivals.append({
                "arrival": arrival,
                "glancing": bool(impact.get("isGlancingBlow")),
                "speed": max(speeds) if speeds else None,
                "completed": sim.get("modelCompletionTime") or "",
                "id": sim.get("simulationID", ""),
                "link": sim.get("link", ""),
            })
            stats["enlilMarsImpacts"] += 1

    arrivals.sort(key=lambda a: a["arrival"])
    groups: list[list[dict]] = []
    for a in arrivals:
        if groups and (a["arrival"] - groups[-1][-1]["arrival"]).total_seconds() <= CME_MERGE_HOURS * 3600:
            groups[-1].append(a)
        else:
            groups.append([a])

    events = []
    for group in groups:
        best = max(group, key=lambda a: a["completed"])
        speed = best["speed"]
        # 400 km/s -> 0.45, 1400 km/s and above -> 1.0; a glancing blow is weaker.
        strength = 0.6 if speed is None else 0.45 + 0.55 * (speed - 400.0) / 1000.0
        strength = max(0.35, min(1.0, strength)) * (0.7 if best["glancing"] else 1.0)
        speed_text = f"{speed:.0f} km/s" if speed else "speed not listed"
        events.append({
            "kind": "cme",
            "id": best["id"],
            "startUtc": iso(best["arrival"]),
            "startUnix": int(best["arrival"].timestamp()),
            "durationHours": CME_PASSAGE_HOURS,
            "strength": round(strength, 2),
            "label": "CME arrival at Mars" + (" (glancing)" if best["glancing"] else ""),
            "basis": f"DONKI WSA-ENLIL+Cone predicted arrival at Mars, {speed_text}; "
                     f"{len(group)} model run(s) merged; nominal {CME_PASSAGE_HOURS:.0f} h passage",
            "link": best["link"],
        })
    return events


def build_sep(raw: list, stats: dict) -> list:
    events = []
    for s in raw:
        stats["sepListed"] += 1
        names = [i.get("displayName", "") for i in (s.get("instruments") or [])]
        if not any(("MSL" in n or "MAVEN" in n or "Mars" in n) for n in names) or not s.get("eventTime"):
            continue
        t = parse_time(s["eventTime"])
        events.append({
            "kind": "sep",
            "id": s.get("sepID", ""),
            "startUtc": iso(t),
            "startUnix": int(t.timestamp()),
            "durationHours": SEP_HOURS,
            "strength": 0.8,
            "label": "Solar energetic particles at Mars",
            "basis": "DONKI SEP detected by " + ", ".join(names) + f"; nominal {SEP_HOURS:.0f} h",
            "link": s.get("link", ""),
        })
        stats["sepAtMars"] += 1
    return events


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    today = dt.datetime.now(UTC).date()
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--start", default=(today - dt.timedelta(days=365)).isoformat(), help="first day, YYYY-MM-DD (UTC)")
    ap.add_argument("--end", default=today.isoformat(), help="last day, YYYY-MM-DD (UTC)")
    ap.add_argument("--min-flare-class", default="M1.0", help="smallest flare kept (default M1.0)")
    ap.add_argument("--out", default=str(repo / "My project" / "Assets" / "Data" / "Weather" / "Resources"
                                         / "donki_mars_events.json"))
    args = ap.parse_args()

    start_day = dt.date.fromisoformat(args.start)
    end_day = dt.date.fromisoformat(args.end)
    start = dt.datetime.combine(start_day, dt.time(), UTC)
    end = dt.datetime.combine(end_day, dt.time(23, 59, 59), UTC)
    min_magnitude = flare_magnitude(args.min_flare_class)
    if min_magnitude is None or end_day < start_day:
        ap.error("bad --min-flare-class or date range")

    stats = dict.fromkeys(["flaresListed", "flaresBelowClass", "flaresWithoutPosition", "flaresFacingAway",
                           "flaresKept", "enlilRuns", "enlilMarsImpacts", "sepListed", "sepAtMars"], 0)
    fetcher = Fetcher()
    print(f"DONKI {start_day} to {end_day}")
    events = build_flares(fetcher.get_window("FLR", start_day, end_day), min_magnitude, stats)
    print(f"  flares: {stats['flaresKept']} kept of {stats['flaresListed']} listed")
    # A CME launched before the window can still arrive inside it.
    cme_events = build_cme_arrivals(
        fetcher.get_window("WSAEnlilSimulations", start_day - dt.timedelta(days=10), end_day), start, end, stats)
    print(f"  CME arrivals at Mars: {len(cme_events)} (from {stats['enlilMarsImpacts']} model impacts)")
    sep_events = build_sep(fetcher.get_window("SEP", start_day, end_day), stats)
    print(f"  SEP at Mars: {stats['sepAtMars']} of {stats['sepListed']} listed")

    events = sorted(events + cme_events + sep_events, key=lambda e: e["startUnix"])
    document = {
        "source": "NASA CCMC DONKI (Space Weather Database Of Notifications, Knowledge, Information)",
        "sourceUrl": "https://ccmc.gsfc.nasa.gov/tools/DONKI/",
        "api": API,
        "retrievedUtc": iso(dt.datetime.now(UTC)),
        "windowStartUtc": iso(start),
        "windowStartUnix": int(start.timestamp()),
        "windowEndUtc": iso(end),
        "windowEndUnix": int(end.timestamp()),
        "minFlareClass": args.min_flare_class,
        "downloadedBytes": fetcher.bytes,
        "downloadRequests": fetcher.requests,
        "counts": stats,
        "notes": "Flares are kept only when on the solar hemisphere facing Mars and are shifted by light time. "
                 "CME arrivals are WSA-ENLIL+Cone model predictions, not detections. Strength is a display "
                 "intensity, not a dose. DONKI holds no Mars dust or temperature events.",
        "events": events,
    }
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(document, indent=1)
    out.write_text(text + "\n", encoding="utf-8")
    print(f"  {len(events)} events, {fetcher.bytes:,} bytes downloaded in {fetcher.requests} requests, "
          f"{len(text) + 1:,} bytes written to {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

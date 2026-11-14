#!/usr/bin/env python3
"""Crop a real Mars elevation tile out of the global MOLA+HRSC blended DEM.

Offline data preparation only (never runs in the game). Standard library only.

Why this source and not Mars Trek tiles
---------------------------------------
Mars Trek (https://trek.nasa.gov/mars/) serves this same product as WMTS
*picture* tiles (8-bit shaded relief). Those are images, not elevations.
The numeric values live in the USGS-hosted GeoTIFF that Trek's layer is
rendered from:

    Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2.tif   (11.4 GB, int16 metres)

That file is an uncompressed BigTIFF with one image row per strip, so a crop
needs only HTTP Range requests: the TIFF header, the slice of the strip-offset
table for the wanted rows, the georeferencing tags, and `width * 2` bytes per
row. A 1025 x 1025 crop costs about 2.1 MB of transfer instead of 11.4 GB.
Every byte requested is counted and written to the metadata file.

Usage
-----
    python tools/data/fetch_mars_dem.py                 # Gale Crater default
    python tools/data/fetch_mars_dem.py --lat -4.59 --lon 137.44 --size 513

Outputs
    My project/Assets/Data/Terrain/<name>.bytes      raw int16 little-endian, row 0 = north, col 0 = west
    My project/Assets/Data/Terrain/<name>.metadata.json  georeferencing, units, provenance, transfer accounting
    docs/data/<name>.preview.png                     shaded-relief preview for eyeballing (not used in game)
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import math
import struct
import sys
import threading
import urllib.request
import zlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

SOURCE_URL = (
    "https://planetarymaps.usgs.gov/mosaic/Mars/HRSC_MOLA_Blend/"
    "Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2.tif"
)
PRODUCT_ID = "Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2"
USER_AGENT = "mars-terrain-prep/1.0"

# TIFF tag ids used here.
T_WIDTH, T_HEIGHT, T_BITS, T_COMPRESSION = 256, 257, 258, 259
T_STRIP_OFFSETS, T_ROWS_PER_STRIP, T_STRIP_COUNTS = 273, 278, 279
T_SAMPLE_FORMAT, T_PIXEL_SCALE, T_TIEPOINT = 339, 33550, 33922
T_GEO_DOUBLES, T_GEO_ASCII, T_NODATA = 34736, 34737, 42113


class RangeReader:
    """HTTP Range reader that counts every request and payload byte."""

    def __init__(self, url: str):
        self.requests = 0
        self.payload_bytes = 0
        self._lock = threading.Lock()
        # Resolve the redirect once so each range request is a single hop.
        req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=60) as resp:
            self.url = resp.geturl()
            self.total_size = int(resp.headers["Content-Length"])
            if resp.headers.get("Accept-Ranges", "").lower() != "bytes":
                raise RuntimeError("Server does not advertise byte-range support")

    def read(self, offset: int, length: int) -> bytes:
        headers = {"User-Agent": USER_AGENT, "Range": f"bytes={offset}-{offset + length - 1}"}
        last_error = None
        for _ in range(4):
            try:
                req = urllib.request.Request(self.url, headers=headers)
                with urllib.request.urlopen(req, timeout=60) as resp:
                    if resp.status != 206:
                        raise RuntimeError(f"Expected 206 Partial Content, got {resp.status}")
                    data = resp.read()
                if len(data) != length:
                    raise RuntimeError(f"Short read: wanted {length}, got {len(data)}")
                with self._lock:
                    self.requests += 1
                    self.payload_bytes += len(data)
                return data
            except Exception as exc:  # retry transient network failures
                last_error = exc
        raise RuntimeError(f"Range read failed at {offset}+{length}: {last_error}")


def read_bigtiff_tags(reader: RangeReader) -> dict:
    """Return {tag: (type, count, raw 8-byte value field)} for the first IFD."""
    head = reader.read(0, 4096)
    if head[:4] != b"II+\x00":
        raise RuntimeError("Expected a little-endian BigTIFF; the source layout has changed")
    ifd = struct.unpack_from("<Q", head, 8)[0]
    count = struct.unpack_from("<Q", head, ifd)[0]
    tags = {}
    for i in range(count):
        tag, typ, cnt = struct.unpack_from("<HHQ", head, ifd + 8 + i * 20)
        tags[tag] = (typ, cnt, head[ifd + 20 + i * 20: ifd + 28 + i * 20])
    return tags


def inline_int(entry) -> int:
    typ, _cnt, raw = entry
    return struct.unpack_from({3: "<H", 4: "<I", 16: "<Q"}[typ], raw)[0]


def pointer(entry) -> int:
    return struct.unpack("<Q", entry[2])[0]


def write_png_gray(path: Path, pixels: bytes, width: int, height: int) -> None:
    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    raw = b"".join(b"\x00" + pixels[y * width:(y + 1) * width] for y in range(height))
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def hillshade(elev, width, height, dx, dy) -> bytes:
    """Simple Lambert shaded relief, sun from the north-west, for a preview."""
    lx, ly, lz = -0.5, 0.5, 0.707
    out = bytearray(width * height)
    for y in range(height):
        y0, y1 = max(y - 1, 0), min(y + 1, height - 1)
        for x in range(width):
            x0, x1 = max(x - 1, 0), min(x + 1, width - 1)
            gx = (elev[y * width + x1] - elev[y * width + x0]) / ((x1 - x0) * dx)
            gy = (elev[y0 * width + x] - elev[y1 * width + x]) / ((y1 - y0) * dy)  # north-up
            # 3x vertical exaggeration so crater relief reads in a thumbnail.
            nx, ny, nz = -3 * gx, -3 * gy, 1.0
            shade = (nx * lx + ny * ly + nz * lz) / math.sqrt(nx * nx + ny * ny + nz * nz)
            out[y * width + x] = max(0, min(255, int(40 + 215 * max(shade, 0.0))))
    return bytes(out)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lat", type=float, default=-5.4, help="centre latitude, degrees north (Gale Crater: -5.4)")
    ap.add_argument("--lon", type=float, default=137.8, help="centre longitude, degrees east (Gale Crater: 137.8)")
    ap.add_argument("--size", type=int, default=1025, help="samples per side; use 2^n + 1 (default 1025)")
    ap.add_argument("--name", default="gale_crater_200m", help="output base name")
    repo = Path(__file__).resolve().parents[2]
    ap.add_argument("--out", default=str(repo / "My project" / "Assets" / "Data" / "Terrain"),
                    help="folder for the .bytes and .metadata.json game assets")
    ap.add_argument("--preview-out", default=str(repo / "docs" / "data"),
                    help="folder for the preview PNG (kept out of the Unity project)")
    ap.add_argument("--threads", type=int, default=16)
    args = ap.parse_args()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    reader = RangeReader(SOURCE_URL)
    tags = read_bigtiff_tags(reader)

    src_w, src_h = inline_int(tags[T_WIDTH]), inline_int(tags[T_HEIGHT])
    if (inline_int(tags[T_BITS]), inline_int(tags[T_COMPRESSION]),
            inline_int(tags[T_SAMPLE_FORMAT]), inline_int(tags[T_ROWS_PER_STRIP])) != (16, 1, 2, 1):
        raise RuntimeError("Source is no longer uncompressed int16 with one row per strip")
    nodata = int(tags[T_NODATA][2].split(b"\x00")[0].decode())

    geo = reader.read(pointer(tags[T_PIXEL_SCALE]), 24) + reader.read(pointer(tags[T_TIEPOINT]), 48)
    deg_per_px_x, deg_per_px_y, _ = struct.unpack_from("<3d", geo, 0)
    _, _, _, tie_lon, tie_lat, _ = struct.unpack_from("<6d", geo, 24)
    radius_m = struct.unpack("<d", reader.read(pointer(tags[T_GEO_DOUBLES]), 8))[0]
    geo_ascii = reader.read(pointer(tags[T_GEO_ASCII]), tags[T_GEO_ASCII][1]).rstrip(b"\x00").decode()

    # Source pixels are "pixel is area": pixel (c, r) covers lon [tie + c*s, tie + (c+1)*s).
    # We treat each pixel value as a point sample at the pixel centre.
    n = args.size
    col0 = int(round((args.lon - tie_lon) / deg_per_px_x - n / 2))
    row0 = int(round((tie_lat - args.lat) / deg_per_px_y - n / 2))
    if not (0 <= col0 and col0 + n <= src_w and 0 <= row0 and row0 + n <= src_h):
        raise RuntimeError("Requested crop crosses the edge of the global mosaic")

    offsets = struct.unpack(f"<{n}Q", reader.read(pointer(tags[T_STRIP_OFFSETS]) + row0 * 8, n * 8))

    def fetch_row(i: int):
        return i, reader.read(offsets[i] + col0 * 2, n * 2)

    rows = [None] * n
    with ThreadPoolExecutor(max_workers=args.threads) as pool:
        for i, data in pool.map(fetch_row, range(n)):
            rows[i] = data
    elev = list(struct.unpack(f"<{n * n}h", b"".join(rows)))

    # Missing samples: fill from the mean of valid 4-neighbours, repeated until none remain.
    missing = [i for i, v in enumerate(elev) if v == nodata]
    nodata_count = len(missing)
    while missing:
        still = []
        fills = {}
        for i in missing:
            y, x = divmod(i, n)
            near = [elev[j] for j in ((i - 1) if x > 0 else -1, (i + 1) if x < n - 1 else -1,
                                      (i - n) if y > 0 else -1, (i + n) if y < n - 1 else -1)
                    if j >= 0 and elev[j] != nodata]
            if near:
                fills[i] = int(round(sum(near) / len(near)))
            else:
                still.append(i)
        if not fills:
            raise RuntimeError("Crop is entirely missing data")
        for i, v in fills.items():
            elev[i] = v
        missing = still

    raw = struct.pack(f"<{n * n}h", *elev)
    (out_dir / f"{args.name}.bytes").write_bytes(raw)

    lat_north = tie_lat - (row0 + 0.5) * deg_per_px_y  # centre of first row
    lat_south = tie_lat - (row0 + n - 0.5) * deg_per_px_y
    lon_west = tie_lon + (col0 + 0.5) * deg_per_px_x
    lon_east = tie_lon + (col0 + n - 0.5) * deg_per_px_x
    lat_centre = 0.5 * (lat_north + lat_south)
    m_per_sample_ns = math.radians(deg_per_px_y) * radius_m
    m_per_sample_ew = math.radians(deg_per_px_x) * radius_m * math.cos(math.radians(lat_centre))
    # Largest east-west error from using one cos(lat) for the whole tile.
    ew_err = max(abs(math.cos(math.radians(l)) / math.cos(math.radians(lat_centre)) - 1.0)
                 for l in (lat_north, lat_south))

    def sample(lat, lon):
        r = int(round((lat_north - lat) / deg_per_px_y))
        c = int(round((lon - lon_west) / deg_per_px_x))
        return elev[r * n + c] if 0 <= r < n and 0 <= c < n else None

    checks = []
    for label, lat, lon, published, note in (
        ("Curiosity landing site (Bradbury Landing)", -4.5895, 137.4417, -4501,
         "published MOLA elevation, approximate"),
    ):
        v = sample(lat, lon)
        if v is not None:
            checks.append({"label": label, "latitude": lat, "longitude": lon,
                           "datasetElevationM": v, "publishedElevationM": published, "note": note})

    meta = {
        "name": args.name,
        "width": n,
        "height": n,
        "sampleFormat": "int16 little-endian, metres, row 0 = north, column 0 = west",
        "elevationUnit": "metre",
        "verticalReference": "Mars areoid (MOLA datum); checked against the published MOLA elevation of the Curiosity landing site",
        "minElevationM": min(elev),
        "maxElevationM": max(elev),
        "latNorthDeg": lat_north,
        "latSouthDeg": lat_south,
        "lonWestDeg": lon_west,
        "lonEastDeg": lon_east,
        "metersPerSampleNorthSouth": m_per_sample_ns,
        "metersPerSampleEastWest": m_per_sample_ew,
        "eastWestScaleErrorFraction": ew_err,
        "sphereRadiusM": radius_m,
        "sourceProjection": "Simple cylindrical (geographic degrees), " + geo_ascii.strip("|"),
        "sourceProductId": PRODUCT_ID,
        "sourceUrl": SOURCE_URL,
        "sourceInstruments": "MGS MOLA laser altimeter blended with Mars Express HRSC stereo DTMs",
        "sourceGridSpacingM": m_per_sample_ns,
        "sourceFileBytes": reader.total_size,
        "sourcePixelOrigin": {"column": col0, "row": row0},
        "noDataValue": nodata,
        "noDataSamplesFilled": nodata_count,
        "resampling": "none (source pixels copied 1:1)",
        "downloadedBytes": reader.payload_bytes,
        "downloadRequests": reader.requests,
        "heightmapBytes": len(raw),
        "heightmapSha256": hashlib.sha256(raw).hexdigest(),
        "retrievedUtc": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "verification": checks,
    }
    (out_dir / f"{args.name}.metadata.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")

    preview_dir = Path(args.preview_out)
    preview_dir.mkdir(parents=True, exist_ok=True)
    write_png_gray(preview_dir / f"{args.name}.preview.png",
                   hillshade(elev, n, n, m_per_sample_ew, m_per_sample_ns), n, n)

    print(f"crop           {n} x {n} samples at ({args.lat}, {args.lon})")
    print(f"extent         {n * m_per_sample_ew / 1000:.1f} km E-W x {n * m_per_sample_ns / 1000:.1f} km N-S")
    print(f"elevation      {min(elev)} .. {max(elev)} m")
    print(f"missing filled {nodata_count}")
    print(f"transferred    {reader.payload_bytes:,} bytes in {reader.requests} requests "
          f"({reader.payload_bytes / reader.total_size:.6%} of the {reader.total_size:,}-byte source)")
    for c in checks:
        print(f"check          {c['label']}: dataset {c['datasetElevationM']} m, published {c['publishedElevationM']} m")
    print(f"wrote          {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

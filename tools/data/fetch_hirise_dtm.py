#!/usr/bin/env python3
"""Crop a 1 m HiRISE elevation patch around the outpost out of the USGS Gale mosaic.

Offline data preparation only (never runs in the game). Needs numpy.

Source
------
    MSL_Gale_DEM_Mosaic_1m_v3.tif  (USGS Astrogeology, 3.9 GB, float32 metres)
    https://astrogeology.usgs.gov/search/map/mars_msl_gale_merged_dem_1m
    Built from HiRISE stereo pairs. HiRISE credit: NASA/JPL/University of Arizona.

The file is a classic TIFF, LZW-compressed, one image row per strip, so a crop
needs only HTTP Range requests for the wanted rows (each strip is a full-width
row, about 85 KB compressed). Every byte requested is counted.

Placement
---------
The patch is aligned to the 200 m regional tile (gale_crater_200m): it covers
`--cells` x `--cells` cells of that grid, starting on a multiple of 4 samples, so
its edges lie on grid lines of the regional mesh at its three nearest detail
levels. MarsTerrain sinks the regional mesh inside the patch and blends the
patch's border onto the regional surface, so the two meet without a step.

Each output sample (i east, j north) sits at regional grid coordinate
(ix0 + i * cells / (size - 1), iz0 + j * cells / (size - 1)), converted to
latitude/longitude with the same linear rule as MarsHeightmap.LocalToLatLon.

Outputs
    My project/Assets/Data/Terrain/<name>.bytes          uint16 little-endian, row 0 = SOUTH, col 0 = west
                                                         elevation = minElevationM + v * elevationStepM
    My project/Assets/Data/Terrain/<name>.metadata.json  placement, scale, provenance, transfer accounting
    docs/data/<name>.preview.png                         shaded-relief preview (not used in game)

Usage
    python3 tools/data/fetch_hirise_dtm.py
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import math
import os
import struct
import sys
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor
from pathlib import Path

import numpy as np

# python.org builds on macOS ship without a CA bundle; fall back to the system one.
if "SSL_CERT_FILE" not in os.environ and Path("/etc/ssl/cert.pem").exists():
    os.environ["SSL_CERT_FILE"] = "/etc/ssl/cert.pem"

sys.path.insert(0, str(Path(__file__).resolve().parent))
from fetch_mars_dem import RangeReader, write_png_gray  # noqa: E402

SOURCE_URL = "https://planetarymaps.usgs.gov/mosaic/Mars/MSL/MSL_Gale_DEM_Mosaic_1m_v3.tif"
PRODUCT_ID = "MSL_Gale_DEM_Mosaic_1m_v3"
PRODUCT_PAGE = "https://astrogeology.usgs.gov/search/map/mars_msl_gale_merged_dem_1m"
CURIOSITY_LAT, CURIOSITY_LON = -4.5895, 137.4417  # MarsTime.CuriosityLatitude / LongitudeEast

T_WIDTH, T_HEIGHT, T_BITS, T_COMPRESSION = 256, 257, 258, 259
T_STRIP_OFFSETS, T_ROWS_PER_STRIP, T_STRIP_COUNTS = 273, 278, 279
T_PREDICTOR, T_SAMPLE_FORMAT = 317, 339
T_PIXEL_SCALE, T_TIEPOINT, T_GEOKEYS, T_GEO_DOUBLES, T_NODATA = 33550, 33922, 34735, 34736, 42113
TYPE_SIZE = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 7: 1, 11: 4, 12: 8, 16: 8}
TYPE_FMT = {3: "H", 4: "I", 11: "f", 12: "d", 16: "Q"}


# ---------------------------------------------------------------- TIFF reading

def read_tiff_tags(reader: RangeReader) -> dict:
    """Return {tag: tuple(values)} for the first IFD of a classic little-endian TIFF."""
    head = reader.read(0, 8)
    if head[:4] != b"II*\x00":
        raise RuntimeError("Expected a little-endian classic TIFF; the source layout has changed")
    ifd = struct.unpack_from("<I", head, 4)[0]
    count = struct.unpack("<H", reader.read(ifd, 2))[0]
    block = reader.read(ifd + 2, count * 12)
    tags = {}
    for i in range(count):
        tag, typ, cnt = struct.unpack_from("<HHI", block, i * 12)
        size = TYPE_SIZE.get(typ, 1) * cnt
        raw = block[i * 12 + 8: i * 12 + 12] if size <= 4 else None
        offset = None if size <= 4 else struct.unpack_from("<I", block, i * 12 + 8)[0]
        tags[tag] = (typ, cnt, raw, offset)
    return tags


def tag_values(reader: RangeReader, entry, first: int = 0, count: int | None = None):
    typ, cnt, raw, offset = entry
    count = cnt if count is None else count
    if typ == 2:
        data = raw if offset is None else reader.read(offset, cnt)
        return data.rstrip(b"\x00").decode("ascii", "replace")
    size = TYPE_SIZE[typ]
    if offset is None:
        data = raw[first * size:(first + count) * size]
    else:
        data = reader.read(offset + first * size, count * size)
    return struct.unpack("<%d%s" % (count, TYPE_FMT[typ]), data)


def lzw_decode(data: bytes, limit: int) -> bytes:
    """TIFF LZW (MSB-first codes, early change). Stops once `limit` bytes are out."""
    out = bytearray()
    table = [bytes([i]) for i in range(256)] + [b"", b""]
    bits, pos, nbits = 0, 0, 0
    width = 9
    prev = None
    total_bits = len(data) * 8
    while pos + width <= total_bits and len(out) < limit:
        # read `width` bits MSB-first
        byte_i = pos >> 3
        chunk = int.from_bytes(data[byte_i:byte_i + 3].ljust(3, b"\x00"), "big")
        code = (chunk >> (24 - (pos & 7) - width)) & ((1 << width) - 1)
        pos += width
        if code == 256:
            table = table[:258]
            width = 9
            prev = None
            continue
        if code == 257:
            break
        if prev is None:
            entry = table[code]
            out += entry
            prev = entry
            continue
        if code < len(table):
            entry = table[code]
            table.append(prev + entry[:1])
        else:
            entry = prev + prev[:1]
            table.append(entry)
        out += entry
        prev = entry
        n = len(table)
        if n + 1 >= 4096:
            width = 12
        elif n + 1 >= 2048:
            width = 12
        elif n + 1 >= 1024:
            width = 11
        elif n + 1 >= 512:
            width = 10
    return bytes(out)


def decode_row(args):
    data, col0, col1 = args
    raw = lzw_decode(data, col1 * 4)
    if len(raw) < col1 * 4:
        raise RuntimeError("LZW row ended early: %d of %d bytes" % (len(raw), col1 * 4))
    return np.frombuffer(raw[col0 * 4: col1 * 4], dtype="<f4").copy()


# ---------------------------------------------------------------- main

def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    terrain_dir = repo / "My project" / "Assets" / "Data" / "Terrain"
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--regional", default="gale_crater_200m", help="regional tile name in Assets/Data/Terrain")
    ap.add_argument("--name", default="gale_bradbury_1m")
    ap.add_argument("--lat", type=float, default=CURIOSITY_LAT)
    ap.add_argument("--lon", type=float, default=CURIOSITY_LON)
    ap.add_argument("--cells", type=int, default=20, help="regional 200 m cells along each edge (multiple of 4)")
    ap.add_argument("--size", type=int, default=4097, help="samples along each edge (2^n + 1, at most 4097)")
    ap.add_argument("--preview-out", default=str(repo / "docs" / "data"))
    args = ap.parse_args()
    if args.cells % 4:
        ap.error("--cells must be a multiple of 4")

    # Regional tile and placement on its grid (same conventions as MarsHeightmap).
    meta200 = json.loads((terrain_dir / f"{args.regional}.metadata.json").read_text())
    W, H = meta200["width"], meta200["height"]
    reg = np.frombuffer((terrain_dir / f"{args.regional}.bytes").read_bytes(), dtype="<i2").reshape(H, W)
    reg = reg[::-1].astype(np.float64)  # row index = iz (north up)
    lonW, lonE = meta200["lonWestDeg"], meta200["lonEastDeg"]
    latS, latN = meta200["latSouthDeg"], meta200["latNorthDeg"]
    gx_site = (args.lon - lonW) / (lonE - lonW) * (W - 1)
    gz_site = (args.lat - latS) / (latN - latS) * (H - 1)
    ix0 = int(round((gx_site - args.cells / 2) / 4)) * 4
    iz0 = int(round((gz_site - args.cells / 2) / 4)) * 4
    n = args.size
    step = args.cells / (n - 1)
    gx = ix0 + np.arange(n) * step
    gz = iz0 + np.arange(n) * step
    lon = lonW + (lonE - lonW) * gx / (W - 1)
    lat = latS + (latN - latS) * gz / (H - 1)
    print(f"patch: regional cells x {ix0}..{ix0 + args.cells}, z {iz0}..{iz0 + args.cells}; "
          f"lat {lat[0]:.4f}..{lat[-1]:.4f}, lon {lon[0]:.4f}..{lon[-1]:.4f}")

    # Source georeferencing.
    reader = RangeReader(SOURCE_URL)
    tags = read_tiff_tags(reader)
    sw, sh = tag_values(reader, tags[T_WIDTH])[0], tag_values(reader, tags[T_HEIGHT])[0]
    if tag_values(reader, tags[T_BITS])[0] != 32 or tag_values(reader, tags[T_SAMPLE_FORMAT])[0] != 3:
        raise RuntimeError("Expected float32 samples")
    if tag_values(reader, tags[T_COMPRESSION])[0] != 5 or tag_values(reader, tags[T_ROWS_PER_STRIP])[0] != 1:
        raise RuntimeError("Expected LZW with one row per strip")
    if T_PREDICTOR in tags and tag_values(reader, tags[T_PREDICTOR])[0] != 1:
        raise RuntimeError("Horizontal predictor not supported")
    scale = tag_values(reader, tags[T_PIXEL_SCALE])
    tie = tag_values(reader, tags[T_TIEPOINT])
    geo = tag_values(reader, tags[T_GEO_DOUBLES])
    keys = tag_values(reader, tags[T_GEOKEYS])
    radius = geo[6] if len(geo) > 6 and geo[6] > 3e6 else 3396190.0
    nodata = float(tag_values(reader, tags[T_NODATA]))
    raster_type = 1
    for k in range(1, keys[3] + 1):
        if keys[k * 4] == 1025:  # GTRasterTypeGeoKey: 1 = PixelIsArea, 2 = PixelIsPoint
            raster_type = keys[k * 4 + 3]
    half = 0.5 if raster_type == 1 else 0.0
    # Equirectangular, centre longitude 0, standard parallel 0: x = R*lon, y = R*lat.
    col = (np.radians(lon) * radius - tie[3]) / scale[0] - half
    row = (tie[4] - np.radians(lat) * radius) / scale[1] - half
    c0, c1 = int(math.floor(col.min())) - 1, int(math.ceil(col.max())) + 2
    r0, r1 = int(math.floor(row.min())) - 1, int(math.ceil(row.max())) + 2
    if c0 < 0 or r0 < 0 or c1 > sw or r1 > sh:
        raise RuntimeError("Patch extends beyond the HiRISE mosaic")
    print(f"source: {sw} x {sh} px, {scale[0]} m/px, R = {radius:.0f} m; reading rows {r0}..{r1}, cols {c0}..{c1}")

    offsets = tag_values(reader, tags[T_STRIP_OFFSETS], r0, r1 - r0)
    counts = tag_values(reader, tags[T_STRIP_COUNTS], r0, r1 - r0)

    # Fetch rows in coalesced ranges of about 8 MB.
    groups, start = [], 0
    for i in range(1, len(offsets) + 1):
        if (i == len(offsets) or offsets[i] != offsets[i - 1] + counts[i - 1]
                or offsets[i] + counts[i] - offsets[start] > 8 << 20):
            groups.append((start, i))
            start = i

    def fetch(group):
        a, b = group
        blob = reader.read(offsets[a], offsets[b - 1] + counts[b - 1] - offsets[a])
        return [blob[offsets[k] - offsets[a]: offsets[k] - offsets[a] + counts[k]] for k in range(a, b)]

    strips = []
    with ThreadPoolExecutor(8) as pool:
        for i, part in enumerate(pool.map(fetch, groups)):
            strips.extend(part)
            print(f"\r  downloaded {reader.payload_bytes / 1e6:7.1f} MB", end="", flush=True)
    print()
    with ProcessPoolExecutor() as pool:
        rows = list(pool.map(decode_row, [(s, c0, c1) for s in strips], chunksize=32))
    src = np.stack(rows).astype(np.float64)
    missing = src <= nodata + 0.5
    print(f"decoded {src.shape[0]} x {src.shape[1]} source px, {int(missing.sum())} nodata")

    # Bilinear resample onto the patch grid (row j = south..north, col i = west..east).
    cc = col - c0
    rr = row - r0
    ci = np.clip(np.floor(cc).astype(int), 0, src.shape[1] - 2)
    ri = np.clip(np.floor(rr).astype(int), 0, src.shape[0] - 2)
    fu = (cc - ci)[None, :]
    fv = (rr - ri)[:, None]
    srcv = np.where(missing, np.nan, src)
    a = srcv[np.ix_(ri, ci)]
    b = srcv[np.ix_(ri, ci + 1)]
    c = srcv[np.ix_(ri + 1, ci)]
    d = srcv[np.ix_(ri + 1, ci + 1)]
    elev = (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv

    # Regional bilinear surface on the same grid, for the datum check and hole filling.
    gxi = np.clip(np.floor(gx).astype(int), 0, W - 2)
    gzi = np.clip(np.floor(gz).astype(int), 0, H - 2)
    gu = (gx - gxi)[None, :]
    gv = (gz - gzi)[:, None]
    regional = ((reg[np.ix_(gzi, gxi)] * (1 - gu) + reg[np.ix_(gzi, gxi + 1)] * gu) * (1 - gv)
                + (reg[np.ix_(gzi + 1, gxi)] * (1 - gu) + reg[np.ix_(gzi + 1, gxi + 1)] * gu) * gv)
    holes = np.isnan(elev)
    offset = float(np.nanmedian(elev - regional))
    elev[holes] = regional[holes] + offset
    print(f"median HiRISE minus regional: {offset:+.2f} m; {int(holes.sum())} samples filled from regional")

    lo, hi = float(np.floor(elev.min())), float(np.ceil(elev.max()))
    stepm = (hi - lo) / 65535.0
    q = np.clip(np.round((elev - lo) / stepm), 0, 65535).astype("<u2")
    out_bytes = q.tobytes()  # row 0 = south
    terrain_dir.mkdir(parents=True, exist_ok=True)
    (terrain_dir / f"{args.name}.bytes").write_bytes(out_bytes)

    # Bradbury check.
    jj = int(round((CURIOSITY_LAT - lat[0]) / (lat[-1] - lat[0]) * (n - 1)))
    ii = int(round((CURIOSITY_LON - lon[0]) / (lon[-1] - lon[0]) * (n - 1)))
    site_elev = float(elev[jj, ii]) if 0 <= jj < n and 0 <= ii < n else None

    meta = {
        "name": args.name,
        "width": n,
        "height": n,
        "sampleFormat": "uint16 little-endian, row 0 = south, column 0 = west; elevation = minElevationM + v * elevationStepM",
        "minElevationM": lo,
        "maxElevationM": hi,
        "elevationStepM": stepm,
        "regionalTile": args.regional,
        "regionalOriginX": ix0,
        "regionalOriginZ": iz0,
        "regionalCells": args.cells,
        "latSouthDeg": float(lat[0]),
        "latNorthDeg": float(lat[-1]),
        "lonWestDeg": float(lon[0]),
        "lonEastDeg": float(lon[-1]),
        "medianOffsetFromRegionalM": offset,
        "samplesFilledFromRegional": int(holes.sum()),
        "sourceProductId": PRODUCT_ID,
        "sourceUrl": SOURCE_URL,
        "sourcePage": PRODUCT_PAGE,
        "sourceInstruments": "MRO HiRISE stereo DTMs (USGS Astrogeology mosaic); credit NASA/JPL/University of Arizona, USGS",
        "sourceGridSpacingM": float(scale[0]),
        "sourceRasterType": "PixelIsArea" if raster_type == 1 else "PixelIsPoint",
        "resampling": "bilinear onto the regional-grid-aligned patch",
        "downloadedBytes": reader.payload_bytes,
        "downloadRequests": reader.requests,
        "heightmapBytes": len(out_bytes),
        "heightmapSha256": hashlib.sha256(out_bytes).hexdigest(),
        "retrievedUtc": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "verification": [{
            "label": "Curiosity landing site (Bradbury Landing)",
            "latitude": CURIOSITY_LAT, "longitude": CURIOSITY_LON,
            "datasetElevationM": site_elev, "publishedElevationM": -4501,
            "note": "published MOLA elevation, approximate",
        }],
    }
    (terrain_dir / f"{args.name}.metadata.json").write_text(json.dumps(meta, indent=2) + "\n")

    # Shaded-relief preview at 1/4 resolution, north up.
    p = elev[::4, ::4][::-1]
    gy, gxg = np.gradient(p, 4.0)
    shade = np.clip((-0.5 * -gxg + 0.5 * gy + 0.707) / np.sqrt(gxg ** 2 + gy ** 2 + 1), 0, 1)
    preview = Path(args.preview_out)
    preview.mkdir(parents=True, exist_ok=True)
    write_png_gray(preview / f"{args.name}.preview.png", (shade * 255).astype(np.uint8).tobytes(), p.shape[1], p.shape[0])

    print(f"wrote {args.name}: {n} x {n}, {lo:.0f}..{hi:.0f} m, step {stepm * 1000:.2f} mm; "
          f"site {site_elev:.1f} m (published -4501); {reader.payload_bytes / 1e6:.1f} MB in {reader.requests} requests")
    return 0


if __name__ == "__main__":
    sys.exit(main())

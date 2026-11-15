#!/usr/bin/env python3
"""Crop a local MOLA MEGDR topography .img into a normalized 16-bit PNG.

Example: --source megt90n000cb.img --source-width 1440 --source-height 720
         --crop 600 300 256 256 --seed 9
Crop coordinates are source pixels (x, y, width, height); output is 256 square.
No downloads or third-party packages. Missing/invalid source uses seeded noise.
MOLA format: https://pds-geosciences.wustl.edu/missions/mgs/megdr.html
"""

import argparse
from pathlib import Path
import random
import struct
import sys
import zlib

SIZE = 256
MAX_SAMPLE = 65535
DEFAULT_SEED = 9
NOISE_BANDS = ((3, 1.0), (7, 0.45), (17, 0.18), (37, 0.06))
DEFAULT_OUTPUT = Path(__file__).resolve().parents[1] / "My project/Assets/Data/mars_height.png"


def fallback(seed):
    rng = random.Random(seed)
    values = [0.0] * (SIZE * SIZE)
    for cells, amplitude in NOISE_BANDS:
        grid = [[rng.random() for _ in range(cells + 1)] for _ in range(cells + 1)]
        for y in range(SIZE):
            fy = y * (cells - 1) / (SIZE - 1)
            iy = int(fy)
            ty = fy - iy
            ty = ty * ty * (3 - 2 * ty)
            for x in range(SIZE):
                fx = x * (cells - 1) / (SIZE - 1)
                ix = int(fx)
                tx = fx - ix
                tx = tx * tx * (3 - 2 * tx)
                a = grid[iy][ix] * (1 - tx) + grid[iy][ix + 1] * tx
                b = grid[iy + 1][ix] * (1 - tx) + grid[iy + 1][ix + 1] * tx
                values[y * SIZE + x] += amplitude * (a * (1 - ty) + b * ty)
    return values


def crop_mola(path, width, height, crop):
    if width <= 0 or height <= 0:
        raise ValueError("supply positive --source-width and --source-height from the PDS label")
    x, y, w, h = crop or (0, 0, width, height)
    if min(x, y) < 0 or min(w, h) <= 0 or x + w > width or y + h > height:
        raise ValueError("crop falls outside source dimensions")
    if path.stat().st_size != width * height * 2:
        raise ValueError("source must be a headerless signed 16-bit big-endian MOLA topography image")
    values = []
    with path.open("rb") as source:
        for row in range(SIZE):
            sy = y + round(row * (h - 1) / (SIZE - 1))
            source.seek((sy * width + x) * 2)
            samples = struct.unpack(f">{w}h", source.read(w * 2))
            for col in range(SIZE):
                values.append(samples[round(col * (w - 1) / (SIZE - 1))])
    return values


def write_png(path, values):
    low, high = min(values), max(values)
    scale = MAX_SAMPLE / (high - low) if high > low else 0
    rows = bytearray()
    for y in range(SIZE):
        rows.append(0)  # PNG filter: none; samples are network byte order.
        for x in range(SIZE):
            value = round((values[y * SIZE + x] - low) * scale)
            rows.extend(struct.pack(">H", max(0, min(MAX_SAMPLE, value))))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", SIZE, SIZE, 16, 0, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
    png += chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--source-width", type=int, default=0)
    parser.add_argument("--source-height", type=int, default=0)
    parser.add_argument("--crop", type=int, nargs=4, metavar=("X", "Y", "WIDTH", "HEIGHT"))
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    values = None
    if args.source:
        try:
            values = crop_mola(args.source, args.source_width, args.source_height, args.crop)
        except (OSError, ValueError, struct.error) as error:
            print(f"warning: {error}; using seeded noise", file=sys.stderr)
    if values is None:
        if not args.source:
            print("warning: no MOLA source supplied; using seeded noise", file=sys.stderr)
        values = fallback(args.seed)
    write_png(args.output, values)
    print(f"Wrote {SIZE}x{SIZE} 16-bit heightmap: {args.output}")


if __name__ == "__main__":
    main()

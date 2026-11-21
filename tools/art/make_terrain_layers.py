#!/usr/bin/env python3
"""Generate the four tileable PBR layers used on the 1 m HiRISE terrain patch.

Stylised-realistic: the surface detail (ripples, mudstone plates, pebbles) is real
PBR relief, but the colours stay within the game's palette
(docs/game-design-references.md section 5), so the outpost still fits.

Each layer gets three 1024 x 1024 PNGs:
    <layer>_albedo.png  sRGB colour
    <layer>_normal.png  tangent-space normal map (OpenGL, +Y up), imported as Normal
    <layer>_mask.png    URP terrain mask map: R metallic, G occlusion, B height, A smoothness

Everything is periodic (FFT noise, wrapped Voronoi), so the textures tile seamlessly.
Unity .meta files with fixed GUIDs are written too, so MarsTerrainConfig can reference
the textures and re-running the script does not break those references.

Usage: python3 tools/art/make_terrain_layers.py
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

import numpy as np

N = 1024
OUT = Path(__file__).resolve().parents[2] / "My project" / "Assets" / "Art" / "TerrainLayers"

# Palette (sRGB 0-1).
def hex_rgb(h: int):
    return np.array([(h >> 16) & 255, (h >> 8) & 255, h & 255], dtype=np.float64) / 255.0

TERRACOTTA = hex_rgb(0xB96D55)
DUSTY_ROSE = hex_rgb(0xC78D87)
CLAY_SAND = hex_rgb(0xD8AC8F)
MAUVE = hex_rgb(0x806886)
DEEP_PURPLE = hex_rgb(0x40334F)
WARM_ORANGE = hex_rgb(0xE6A06E)


def fft_noise(rng, falloff: float, low: int = 1, high: int = N // 2) -> np.ndarray:
    """Periodic noise with a 1/f^falloff spectrum between two frequencies, scaled to 0..1."""
    fx = np.fft.fftfreq(N) * N
    f = np.sqrt(fx[None, :] ** 2 + fx[:, None] ** 2)
    amp = np.where((f >= low) & (f <= high), 1.0 / np.maximum(f, 1) ** falloff, 0.0)
    spec = amp * np.exp(2j * np.pi * rng.random((N, N)))
    n = np.real(np.fft.ifft2(spec))
    return (n - n.min()) / (n.max() - n.min())


def voronoi(rng, cells: int):
    """Wrapped Voronoi on an N x N torus: (F1, F2 - F1, cell id), distances in cell units."""
    pts = rng.random((cells, cells, 2))
    ids = rng.random((cells, cells))
    y, x = np.mgrid[0:N, 0:N] / N * cells
    cx, cy = np.floor(x).astype(int), np.floor(y).astype(int)
    f1 = np.full((N, N), 9.0)
    f2 = np.full((N, N), 9.0)
    idv = np.zeros((N, N))
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gx, gy = cx + dx, cy + dy
            p = pts[gy % cells, gx % cells]
            d = np.hypot(gx + p[..., 0] - x, gy + p[..., 1] - y)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            idv = np.where(closer, ids[gy % cells, gx % cells], idv)
            f1 = np.where(closer, d, f1)
    return f1, f2 - f1, idv


def normal_map(h: np.ndarray, strength: float) -> np.ndarray:
    """Tangent-space normals from a 0..1 height field (row 0 = top = +V)."""
    du = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dv = -(np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-du, -dv, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def cavity(h: np.ndarray) -> np.ndarray:
    """Ambient occlusion stand-in: how far each texel sits below its blurred surroundings."""
    k = np.fft.fft2(h)
    fx = np.fft.fftfreq(N)
    blur = np.real(np.fft.ifft2(k * np.exp(-(fx[None, :] ** 2 + fx[:, None] ** 2) * (N / 24.0) ** 2)))
    return np.clip(1.0 - np.maximum(blur - h, 0) * 4.0, 0.45, 1.0)


def colorize(base, h, variation, rng_noise, tint=None, tint_amount=None):
    col = base[None, None, :] * (0.78 + 0.34 * h[..., None]) * (0.94 + 0.12 * rng_noise[..., None])
    if tint is not None:
        col = col * (1 - tint_amount[..., None]) + tint[None, None, :] * tint_amount[..., None]
    col = col + (variation[..., None] - 0.5) * 0.06
    return np.clip(col, 0, 1)


def write_png(path: Path, img: np.ndarray) -> None:
    data = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, c = data.shape
    color_type = {3: 2, 4: 6}[c]
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 8
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: {alpha_usage}
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: {texture_type}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Fixed GUIDs so the config asset's references survive re-runs.
GUIDS = {
    ("dust", "albedo"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d01",
    ("dust", "normal"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d02",
    ("dust", "mask"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d03",
    ("sand", "albedo"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d11",
    ("sand", "normal"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d12",
    ("sand", "mask"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d13",
    ("bedrock", "albedo"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d21",
    ("bedrock", "normal"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d22",
    ("bedrock", "mask"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d23",
    ("gravel", "albedo"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d31",
    ("gravel", "normal"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d32",
    ("gravel", "mask"): "6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d33",
}


def save_layer(name, albedo, height, normal_strength, smooth):
    hn = (height - height.min()) / (height.max() - height.min())
    write_png(OUT / f"{name}_albedo.png", albedo)
    write_png(OUT / f"{name}_normal.png", normal_map(hn, normal_strength))
    ao = cavity(hn)
    mask = np.stack([np.zeros_like(hn), ao, hn, smooth], -1)
    write_png(OUT / f"{name}_mask.png", mask)
    for kind, srgb, ttype, alpha in (("albedo", 1, 0, 0), ("normal", 0, 1, 0), ("mask", 0, 0, 1)):
        (OUT / f"{name}_{kind}.png.meta").write_text(META.format(
            guid=GUIDS[(name, kind)], srgb=srgb, texture_type=ttype, alpha_usage=alpha))
    print(f"  {name}: albedo mean {albedo.reshape(-1, 3).mean(0).round(3)}")


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    folder_meta = OUT.parent / "TerrainLayers.meta"
    if not folder_meta.exists():
        folder_meta.write_text("fileFormatVersion: 2\nguid: 6d1a0c2e7f3b4a5d9e8c1b2a3f4e5d00\nfolderAsset: yes\n"
                               "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n"
                               "  assetBundleVariant: \n")
    rng = np.random.default_rng(2026)
    fine = fft_noise(rng, 1.0, 64, 512)

    # Dust: fine wind-blown regolith, soft undulation, pale and slightly glossier.
    h = 0.6 * fft_noise(rng, 2.0, 2, 64) + 0.4 * fine
    tint = fft_noise(rng, 2.5, 1, 16)
    # Matches the flat-ground colour of the 200 m terrain, so the patch edge fades into it.
    alb = colorize(0.8 * DUSTY_ROSE + 0.2 * CLAY_SAND, h, tint, fine, TERRACOTTA, 0.2 * tint)
    save_layer("dust", alb, h, 3.0, 0.12 + 0.06 * h)

    # Sand: wind ripples (periodic: integer wave count along the tile), warped by noise.
    y, x = np.mgrid[0:N, 0:N] / N
    warp = fft_noise(rng, 2.0, 1, 8)
    phase = 2 * np.pi * (11 * x + 3 * y + 1.6 * warp)
    ripple = (0.5 + 0.5 * np.sin(phase)) ** 1.6
    h = 0.65 * ripple + 0.2 * fft_noise(rng, 2.0, 2, 32) + 0.15 * fine
    alb = colorize(TERRACOTTA, h, fft_noise(rng, 2.5, 1, 16), fine, CLAY_SAND, 0.18 * ripple)
    save_layer("sand", alb, h, 3.5, np.full((N, N), 0.08))

    # Bedrock: Murray-formation style mudstone plates split by cracks, dust in the cracks.
    f1, edge, ids = voronoi(rng, 9)
    plates = np.clip(edge * 6.0, 0, 1)
    tilt = (ids - 0.5) * 0.3 * (f1 / 1.2)
    h = 0.6 * plates + 0.2 * (ids + tilt) + 0.2 * fft_noise(rng, 1.5, 4, 256)
    crack_dust = 1 - plates
    base = 0.55 * MAUVE + 0.45 * TERRACOTTA
    alb = colorize(base, h, ids, fine, 0.6 * CLAY_SAND + 0.4 * TERRACOTTA, 0.7 * crack_dust)
    save_layer("bedrock", alb, h, 9.0, 0.22 * plates + 0.06)

    # Gravel: pebble lag, rounded clasts over dark sand.
    f1, edge, ids = voronoi(rng, 38)
    pebble = np.clip(1 - f1 / 0.55, 0, 1) ** 0.6 * (ids > 0.25)
    h = 0.75 * pebble + 0.25 * fine
    base = 0.6 * TERRACOTTA + 0.4 * DEEP_PURPLE
    peb_col = np.where((ids > 0.7)[..., None], (0.5 * MAUVE + 0.5 * DEEP_PURPLE)[None, None, :],
                       (0.7 * TERRACOTTA + 0.3 * WARM_ORANGE)[None, None, :])
    alb = colorize(base, h, ids, fine)
    alb = alb * (1 - pebble[..., None]) + peb_col * (0.8 + 0.3 * h[..., None]) * pebble[..., None]
    save_layer("gravel", np.clip(alb, 0, 1), h, 8.0, 0.05 + 0.2 * pebble)
    return 0


if __name__ == "__main__":
    sys.exit(main())

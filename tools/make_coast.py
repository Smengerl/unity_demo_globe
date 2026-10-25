"""Coast distance maps for the surf/foam effect.

Land/water is decided from the day map exactly like the Earth shader does (blue/(max(r,g)+0.003) ratio in linear colour),
so the foam follows the coastline that is actually drawn. Output is a grey PNG: 0 on land and at the shore, rising with the
distance from land over the water up to 255 at DMAX pixels and beyond.

Usage: python3 tools/make_coast.py Assets/GlobeView/Resources/Earth
Writes coast.png (from day.jpg and watermask.png, 8192x4096, DMAX 8 px ~ 39 km). The finer coast maps are tiles, see tools/build_tiles.py.
"""
import sys
import numpy as np
from PIL import Image

out = sys.argv[1]


def water_mask(path, deep=None):
    rgb = np.asarray(Image.open(path).convert("RGB"), np.float32) / 255.0
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    ratio = lin[..., 2] / (np.maximum(lin[..., 0], lin[..., 1]) + 0.003)
    w = ratio > 1.265             # smoothstep(1.08, 1.45) midpoint
    return (w | deep) if deep is not None else w   # plus deep ocean from watermask.png, same rule as the shader


def grow(a, diagonal):
    g = a.copy()
    g[1:] |= a[:-1]; g[:-1] |= a[1:]
    g[:, 1:] |= a[:, :-1]; g[:, :-1] |= a[:, 1:]
    if diagonal:
        g[1:, 1:] |= a[:-1, :-1]; g[1:, :-1] |= a[:-1, 1:]
        g[:-1, 1:] |= a[1:, :-1]; g[:-1, :-1] |= a[1:, 1:]
    return g


def coast_distance(water, dmax):
    land = ~water
    reached = land.copy()
    dist = np.full(water.shape, dmax, np.float32)
    dist[land] = 0
    for r in range(1, dmax + 1):
        new = grow(reached, r % 2 == 0) & ~reached     # alternate 4 / 8 neighbours: roughly round
        dist[new] = r
        reached |= new
    dist[land] = 0
    return np.clip(dist / dmax * 255.0 + 0.5, 0, 255).astype(np.uint8)


deep_global = np.asarray(Image.open(f"{out}/watermask.png"), np.uint8) > 127          # 8192x4096, whole Earth
w = water_mask(f"{out}/day.jpg", deep_global)
Image.fromarray(coast_distance(w, 8), "L").save(f"{out}/coast.png")        # 8 px = 39 km, as CoastKm in globe-config.json
print("coast.png", w.shape, "water %.1f %%" % (w.mean() * 100))

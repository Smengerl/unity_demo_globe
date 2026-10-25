#!/usr/bin/env python3
"""
Builds the tile pyramid of the globe (day, night, relief, coast) from the raw data, as the shared configuration asks.

  python3 tools/build_tiles.py                 # everything the configuration lists, into EarthTiles/
  python3 tools/build_tiles.py --estimate      # only count tiles and estimate the size
  python3 tools/build_tiles.py --layers day,night --levels 5-6 --workers 4

The configuration is Assets/GlobeView/Resources/globe-config.json, the same file the view reads (section "Tiles": tile size, layers
with format and level range, coast distance; and the tile levels the zoom stages use). Raw data comes from temp-src (see
tools/get_data.sh and tools/README.md). A tile is only made where a source is fine enough for the level and covers the whole tile,
so the pyramid is as dense as the raw data allows; the view falls back to coarser tiles where one is missing.

Pyramid: plate carree, level L has 2^(L+1) x 2^L tiles, tile (0, 0) at the north-west corner, files layer/level/x_y.format.
Needs Pillow and numpy.
"""
import argparse
import json
import math
import multiprocessing
import os
import sys
import time

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Raw images: file in temp-src, the area they cover (lon0, lat0, lon1, lat1) and their density in pixels per degree.
IMAGE_SOURCES = {
    "day": [
        dict(file="day_21600.jpg", bounds=(-180, -90, 180, 90), ppd=60.0),     # Blue Marble NG, 1.8 km
        dict(file="day_C1.jpg", bounds=(0, 0, 90, 90), ppd=240.0),             # tile C1, 500 m
    ],
    "night": [
        dict(file="night_3km.jpg", bounds=(-180, -90, 180, 90), ppd=37.5),     # Black Marble 2016, 3 km
        dict(file="night_C1.jpg", bounds=(0, 0, 90, 90), ppd=240.0),
    ],
}
K = 8.0                      # vertical exaggeration of slopes (as for the global relief map)
M_PER_DEG = 111320.0
KM_PER_DEG = 111.32

cfg = None                   # configuration
loaded = {}                  # file -> PIL image (shared with the workers by fork)
terrain = {}                 # z -> (heights, tile x0, tile y0)
deep = None                  # deep-ocean mask of the global map


def level_ppd(level, size):
    return size * (2 << level) / 360.0


def tile_bounds(level, x, y):
    w, h = 360.0 / (2 << level), 180.0 / (1 << level)
    return (-180 + x * w, 90 - (y + 1) * h, -180 + (x + 1) * w, 90 - y * h)


def contains(outer, inner, eps=1e-6):
    return outer[0] - eps <= inner[0] and outer[1] - eps <= inner[1] and inner[2] <= outer[2] + eps and inner[3] <= outer[3] + eps


def available(src_dir, layer):
    return [s for s in IMAGE_SOURCES[layer] if os.path.exists(os.path.join(src_dir, s["file"]))]


def pick_image_source(srcs, level, size, bounds):
    """The coarsest source that covers the tile and is fine enough (a little coarser than the level is accepted)."""
    need = level_ppd(level, size) * 0.75
    ok = [s for s in srcs if contains(s["bounds"], bounds) and s["ppd"] >= need]
    return min(ok, key=lambda s: s["ppd"]) if ok else None


def render_image(src, bounds, px, margin=0):
    """Cuts the area (grown by margin output pixels) out of a source and resizes it to px + 2 margin square; edges are repeated."""
    im = loaded[src["file"]]
    sb = src["bounds"]
    dlon, dlat = (bounds[2] - bounds[0]) / px, (bounds[3] - bounds[1]) / px
    lon0, lon1 = bounds[0] - margin * dlon, bounds[2] + margin * dlon
    lat0, lat1 = bounds[1] - margin * dlat, bounds[3] + margin * dlat
    ppx = im.width / (sb[2] - sb[0])
    ppy = im.height / (sb[3] - sb[1])
    box = [(lon0 - sb[0]) * ppx, (sb[3] - lat1) * ppy, (lon1 - sb[0]) * ppx, (sb[3] - lat0) * ppy]
    n = px + 2 * margin
    cl = [max(box[0], 0), max(box[1], 0), min(box[2], im.width), min(box[3], im.height)]
    if cl == box:
        return im.resize((n, n), Image.LANCZOS, box=tuple(box))
    # the margin sticks out of the source: resize what exists, repeat its edge
    left, top = round((cl[0] - box[0]) / (box[2] - box[0]) * n), round((cl[1] - box[1]) / (box[3] - box[1]) * n)
    right, bottom = n - round((box[2] - cl[2]) / (box[2] - box[0]) * n), n - round((box[3] - cl[3]) / (box[3] - box[1]) * n)
    part = im.resize((max(right - left, 1), max(bottom - top, 1)), Image.LANCZOS, box=tuple(cl))
    arr = np.pad(np.asarray(part), ((top, n - bottom), (left, n - right), (0, 0)), mode="edge")
    return Image.fromarray(arr)


# ---- coast ----

def water_mask(rgb_img, deep_mask):
    rgb = np.asarray(rgb_img.convert("RGB"), np.float32) / 255.0
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    ratio = lin[..., 2] / (np.maximum(lin[..., 0], lin[..., 1]) + 0.003)
    return (ratio > 1.265) | deep_mask      # same rule as the shader: colour guess plus deep ocean


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


def grid(bounds, n):
    lon = bounds[0] + (np.arange(n) + 0.5) / n * (bounds[2] - bounds[0])
    lat = bounds[3] - (np.arange(n) + 0.5) / n * (bounds[3] - bounds[1])
    return lon, lat


def make_coast(level, x, y, size, srcs):
    b = tile_bounds(level, x, y)
    src = pick_image_source(srcs, level, size, b)
    if src is None or deep is None:
        return None
    ppd = level_ppd(level, size)
    dmax = max(2, round(cfg["Tiles"]["CoastKm"] / (KM_PER_DEG / ppd)))
    img = render_image(src, b, size, margin=dmax)
    n = size + 2 * dmax
    dlon, dlat = (b[2] - b[0]) / size, (b[3] - b[1]) / size
    lon = b[0] - (dmax - 0.5) * dlon + np.arange(n) * dlon
    lat = b[3] + (dmax - 0.5) * dlat - np.arange(n) * dlat
    dh, dw = deep.shape
    cols = np.clip(((lon + 180) / 360 * dw).astype(int), 0, dw - 1)
    rows = np.clip(((90 - lat) / 180 * dh).astype(int), 0, dh - 1)
    water = water_mask(img, deep[np.ix_(rows, cols)])
    inner = water[dmax:dmax + size, dmax:dmax + size]
    if not inner.any():
        return None                                  # no water: the shader needs no coast distance here
    dist = coast_distance(water, dmax)[dmax:dmax + size, dmax:dmax + size]
    if dist[inner].min() >= 255:
        return None                                  # all water is far from the coast: the coarser map says the same
    out = np.dstack([dist, dist, dist, np.full_like(dist, 255)])
    return Image.fromarray(out, "RGBA")


# ---- relief ----

def merc_y(lat):
    lat = np.clip(lat, -85.0511, 85.0511)
    return (1 - np.arcsinh(np.tan(np.radians(lat))) / np.pi) / 2


def load_terrain(src_dir):
    tiles = os.path.join(src_dir, "tiles")
    if not os.path.isdir(tiles):
        return
    found = {}
    for name in os.listdir(tiles):
        parts = name[:-4].split("_") if name.endswith(".png") else []
        if len(parts) == 3 and all(p.isdigit() for p in parts):
            z, tx, ty = map(int, parts)
            found.setdefault(z, []).append((tx, ty))
    for z, lst in found.items():
        xs, ys = [t[0] for t in lst], [t[1] for t in lst]
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        m = np.zeros(((y1 - y0 + 1) * 256, (x1 - x0 + 1) * 256), np.float32)
        for tx, ty in lst:
            rgb = np.asarray(Image.open(os.path.join(tiles, f"{z}_{tx}_{ty}.png")).convert("RGB"), np.float32)
            m[(ty - y0) * 256:(ty - y0 + 1) * 256, (tx - x0) * 256:(tx - x0 + 1) * 256] = rgb[..., 0] * 256 + rgb[..., 1] + rgb[..., 2] / 256 - 32768
        terrain[z] = (np.maximum(m, 0), x0, y0)          # ocean flat


def resample(m, xs, ys):
    ys = np.clip(ys, 0, m.shape[0] - 1.001)
    xs = np.clip(xs, 0, m.shape[1] - 1.001)
    y0 = ys.astype(int); fy = (ys - y0)[:, None]
    tmp = m[y0] * (1 - fy) + m[y0 + 1] * fy
    x0 = xs.astype(int); fx = (xs - x0)[None, :]
    return tmp[:, x0] * (1 - fx) + tmp[:, x0 + 1] * fx


def make_relief(level, x, y, size):
    # only the finest terrain that is loaded and, to be worth it, covers part of the tile
    b = tile_bounds(level, x, y)
    ppd = level_ppd(level, size)
    for z in sorted(terrain, reverse=True):
        if 256 * (1 << z) / 360.0 * 2.2 < ppd * 0.5:
            continue                                  # far too coarse for this level
        m, tx0, ty0 = terrain[z]
        n = size + 2
        dlon, dlat = (b[2] - b[0]) / size, (b[3] - b[1]) / size
        lon = b[0] - 0.5 * dlon + np.arange(n) * dlon
        lat = b[3] + 0.5 * dlat - np.arange(n) * dlat
        xs = ((lon + 180) / 360 * (1 << z) - tx0) * 256 - 0.5
        ys = (merc_y(lat) * (1 << z) - ty0) * 256 - 0.5
        covered = (xs[None, 1:-1] >= 0) & (xs[None, 1:-1] <= m.shape[1] - 1) & (ys[1:-1, None] >= 0) & (ys[1:-1, None] <= m.shape[0] - 1)
        if not covered.any():
            continue
        h = resample(m, xs, ys)
        cos = max(math.cos(math.radians((b[1] + b[3]) / 2)), 0.05)
        dx = M_PER_DEG * cos * dlon
        dy = M_PER_DEG * dlat
        gx = np.gradient(h, axis=1) / dx
        gy = np.gradient(h, axis=0) / dy
        nrm = np.stack([-K * gx, K * gy, np.ones_like(h)], -1)     # rows run north to south
        nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
        rgb = ((nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)[1:-1, 1:-1]
        alpha = np.where(covered, 255, 0).astype(np.uint8)
        rgb[~covered] = (128, 128, 255)
        return Image.fromarray(np.dstack([rgb, alpha]), "RGBA")
    return None


# ---- jobs ----

def make_tile(job):
    layer, level, x, y, size, fmt, path, quality = job
    try:
        if layer in ("day", "night"):
            b = tile_bounds(level, x, y)
            src = pick_image_source(available(job_src_dir, layer), level, size, b)
            img = render_image(src, b, size).convert("RGB") if src else None
        elif layer == "relief":
            img = make_relief(level, x, y, size)
        else:
            img = make_coast(level, x, y, size, available(job_src_dir, "day"))
        if img is None:
            return layer, level, x, y, 0
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if fmt == "jpg":
            img.save(path, quality=quality, optimize=True)
        else:
            img.save(path, optimize=True, compress_level=6)
        return layer, level, x, y, os.path.getsize(path)
    except Exception as e:                   # one bad tile must not stop the run
        print(f"  tile {layer}/{level}/{x}_{y} failed: {e}", file=sys.stderr)
        return layer, level, x, y, 0


job_src_dir = ""


def wanted_tiles(layer_cfg, size, levels, src_dir, only_estimate_cover=True):
    """Tiles of a layer that the raw data can make: (level, x, y)."""
    name = layer_cfg["Name"]
    out = []
    for level in levels:
        cols, rows = 2 << level, 1 << level
        if name in ("day", "night"):
            srcs = available(src_dir, name)
            for s in srcs:
                pass
            for y in range(rows):
                for x in range(cols):
                    if pick_image_source(srcs, level, size, tile_bounds(level, x, y)):
                        out.append((level, x, y))
        elif name == "coast":
            srcs = available(src_dir, "day")
            for y in range(rows):
                for x in range(cols):
                    if pick_image_source(srcs, level, size, tile_bounds(level, x, y)):
                        out.append((level, x, y))
        elif name == "relief":
            for z in sorted(terrain, reverse=True):
                if 256 * (1 << z) / 360.0 * 2.2 < level_ppd(level, size) * 0.5:
                    continue
                m, tx0, ty0 = terrain[z]
                lon0 = tx0 / (1 << z) * 360 - 180
                lon1 = (tx0 + m.shape[1] / 256) / (1 << z) * 360 - 180
                latf = lambda ty: math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * ty / (1 << z)))))
                lat1, lat0 = latf(ty0), latf(ty0 + m.shape[0] / 256)
                for y in range(rows):
                    for x in range(cols):
                        b = tile_bounds(level, x, y)
                        if b[2] > lon0 and b[0] < lon1 and b[3] > lat0 and b[1] < lat1:
                            out.append((level, x, y))
                break
    return out


AVERAGE_BYTES = {"day": 80_000, "night": 45_000, "relief": 260_000, "coast": 70_000}


def main():
    global cfg, deep, job_src_dir
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=os.path.join(ROOT, "Assets/GlobeView/Resources/globe-config.json"))
    ap.add_argument("--src", default=os.path.join(ROOT, "temp-src"), help="raw data (default temp-src)")
    ap.add_argument("--out", default=os.path.join(ROOT, "EarthTiles"))
    ap.add_argument("--layers", default="", help="comma separated, default all of the configuration")
    ap.add_argument("--levels", default="", help="e.g. 5-6, default the range of each layer")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--estimate", action="store_true", help="count tiles and estimate the size, make nothing")
    args = ap.parse_args()

    cfg = json.load(open(args.config, encoding="utf-8"))
    size = cfg["Tiles"]["TileSize"]
    job_src_dir = args.src
    layers = [l for l in cfg["Tiles"]["Layers"] if not args.layers or l["Name"] in args.layers.split(",")]

    # the stages' tile levels must be inside the layers' ranges
    for tier in cfg["Tiers"]:
        for st in cfg["Stages"]:
            lvl = st["TileLevel"] + tier["LevelOffset"]
            if st["TileLevel"] >= 0 and tier["WindowTiles"] > 0 and lvl > max(l["MaxLevel"] for l in cfg["Tiles"]["Layers"]):
                print(f"note: {st['Name']} on {tier['Name']} asks for level {lvl}, finer than any layer; the view uses the finest tiles")

    for name in ("day", "night"):
        if not available(args.src, name):
            print(f"missing raw data for '{name}' in {args.src}: run tools/get_data.sh")
    load_terrain(args.src)
    if not terrain:
        print(f"no terrain tiles in {args.src}/tiles: no relief tiles (run tools/get_data.sh)")

    plan = []
    for l in layers:
        lo, hi = l["MinLevel"], l["MaxLevel"]
        if args.levels:
            a, _, b = args.levels.partition("-")
            lo, hi = max(lo, int(a)), min(hi, int(b or a))
        tiles = wanted_tiles(l, size, range(lo, hi + 1), args.src)
        plan.append((l, tiles))

    total_bytes = 0
    print(f"tile size {size}, source {args.src}, target {args.out}")
    for l, tiles in plan:
        by_level = {}
        for lvl, _, _ in tiles:
            by_level[lvl] = by_level.get(lvl, 0) + 1
        est = len(tiles) * AVERAGE_BYTES[l["Name"]] * (0.35 if l["Name"] == "coast" else 1)
        total_bytes += est
        print(f"  {l['Name']:7s} {len(tiles):6d} tiles  ~{est / 1e6:7.1f} MB   per level {dict(sorted(by_level.items()))}")
    print(f"  total ~{total_bytes / 1e6:.0f} MB (estimate)")
    if args.estimate:
        return

    for name in {src["file"] for l, _ in plan for s in ([IMAGE_SOURCES.get("day", [])] if l["Name"] == "coast" else [IMAGE_SOURCES.get(l["Name"], [])]) for src in s}:
        path = os.path.join(args.src, name)
        if os.path.exists(path):
            print("loading", name)
            im = Image.open(path)
            im.load()
            loaded[name] = im
    wm = os.path.join(ROOT, "Assets/GlobeView/Resources/Earth/watermask.png")
    deep = np.asarray(Image.open(wm), np.uint8) > 127 if os.path.exists(wm) else None

    jobs = []
    for l, tiles in plan:
        for lvl, x, y in tiles:
            path = os.path.join(args.out, l["Name"], str(lvl), f"{x}_{y}.{l['Format']}")
            jobs.append((l["Name"], lvl, x, y, size, l["Format"], path, 85))
    print(f"making {len(jobs)} tiles with {args.workers} workers")
    started = time.time()
    stats = {}
    ctx = multiprocessing.get_context("fork")
    with ctx.Pool(args.workers) as pool:
        for i, (layer, lvl, x, y, nbytes) in enumerate(pool.imap_unordered(make_tile, jobs, chunksize=4)):
            s = stats.setdefault((layer, lvl), [0, 0, 0])
            s[0] += 1
            if nbytes:
                s[1] += 1
                s[2] += nbytes
            if (i + 1) % 200 == 0:
                print(f"  {i + 1}/{len(jobs)}  {time.time() - started:.0f} s")

    # the manifest describes everything in the folder (also layers of earlier runs)
    manifest = {"TileSize": size, "Layers": {}}
    for layer in sorted(os.listdir(args.out)):
        ldir = os.path.join(args.out, layer)
        if not os.path.isdir(ldir):
            continue
        for lvl in sorted(os.listdir(ldir), key=int):
            files = [f for f in os.listdir(os.path.join(ldir, lvl)) if not f.startswith(".")]
            nbytes = sum(os.path.getsize(os.path.join(ldir, lvl, f)) for f in files)
            manifest["Layers"].setdefault(layer, {})[lvl] = {"tiles": len(files), "bytes": nbytes}
    os.makedirs(args.out, exist_ok=True)
    json.dump(manifest, open(os.path.join(args.out, "manifest.json"), "w"), indent=2)
    real = sum(v["bytes"] for layer in manifest["Layers"].values() for v in layer.values())
    print(f"done in {time.time() - started:.0f} s, {real / 1e6:.0f} MB in {args.out}")


if __name__ == "__main__":
    main()

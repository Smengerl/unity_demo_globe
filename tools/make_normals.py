import sys
import numpy as np
from PIL import Image

tiles, out = sys.argv[1], sys.argv[2]
K = 8.0  # vertical exaggeration of slopes
M_PER_DEG = 111320.0


def mosaic(z, x0, x1, y0, y1):
    w, h = (x1 - x0 + 1) * 256, (y1 - y0 + 1) * 256
    m = np.zeros((h, w), np.float32)
    for tx in range(x0, x1 + 1):
        for ty in range(y0, y1 + 1):
            rgb = np.asarray(Image.open(f"{tiles}/{z}_{tx}_{ty}.png").convert("RGB"), np.float32)
            m[(ty - y0) * 256:(ty - y0 + 1) * 256, (tx - x0) * 256:(tx - x0 + 1) * 256] = (
                rgb[..., 0] * 256 + rgb[..., 1] + rgb[..., 2] / 256 - 32768)
    return np.maximum(m, 0)  # ocean flat


def merc_y(lat):
    lat = np.clip(lat, -85.0511, 85.0511)
    return (1 - np.arcsinh(np.tan(np.radians(lat))) / np.pi) / 2


def resample(m, xs, ys):
    """Separable bilinear sample of m at fractional pixel coords xs (cols), ys (rows)."""
    ys = np.clip(ys, 0, m.shape[0] - 1.001)
    xs = np.clip(xs, 0, m.shape[1] - 1.001)
    y0 = ys.astype(int); fy = (ys - y0)[:, None]
    tmp = m[y0] * (1 - fy) + m[y0 + 1] * fy
    x0 = xs.astype(int); fx = (xs - x0)[None, :]
    return tmp[:, x0] * (1 - fx) + tmp[:, x0 + 1] * fx


def to_normal(h, lat_per_row, lon_per_col, wrap):
    cos = np.maximum(np.cos(np.radians(lat_per_row)), 0.05)[:, None]
    dx = M_PER_DEG * cos * lon_per_col
    dy = M_PER_DEG * abs(lat_per_row[1] - lat_per_row[0])
    if wrap:
        gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / (2 * dx)
    else:
        gx = np.gradient(h, axis=1) / dx
    gy = np.gradient(h, axis=0) / dy
    slope_e, slope_n = gx, -gy  # rows run north -> south
    n = np.stack([-K * slope_e, -K * slope_n, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8))


# global: z=4 mosaic (4096 px across 360 deg) -> equirect 4096x2048
g = mosaic(4, 0, 15, 0, 15)
W, H = 4096, 2048
lat = 90 - (np.arange(H) + 0.5) / H * 180
xs = (np.arange(W) + 0.5) / W * g.shape[1] - 0.5
ys = merc_y(lat) * g.shape[0] - 0.5
hg = resample(g, xs, ys)
to_normal(hg, lat, 360.0 / W, True).save(f"{out}/normal.png")
print("normal.png", hg.max())

# detail: z=7 tiles x 64..74, y 35..50 -> equirect rect lon 0..30, lat 35..62
LON0, LON1, LAT0, LAT1 = 0, 30, 35, 62
d = mosaic(7, 64, 74, 35, 50)
W = 3072
H = round(W * (LAT1 - LAT0) / (LON1 - LON0))
lon = LON0 + (np.arange(W) + 0.5) / W * (LON1 - LON0)
lat = LAT1 - (np.arange(H) + 0.5) / H * (LAT1 - LAT0)
xs = ((lon + 180) / 360 * 128 - 64) * 256 - 0.5
ys = (merc_y(lat) * 128 - 35) * 256 - 0.5
hd = resample(d, xs, ys)
to_normal(hd, lat, (LON1 - LON0) / W, False).save(f"{out}/detail_normal.png")
print("detail_normal.png", hd.shape, hd.max())


# deep-ocean mask for the shader: ocean areas the day-colour water guess misses (mid-ocean ridges and seamounts look pale in
# the Blue Marble bathymetry colours and would be treated as land). Water = terrain height below -200 m.
# 200 m keeps land basins below sea level (Death Valley, Qattara, Dead Sea shore) out. Coastlines still come from the colours.
W, H = 8192, 4096
lat = 90 - (np.arange(H) + 0.5) / H * 180
xs = (np.arange(W) + 0.5) / W * g.shape[1] - 0.5
ys = merc_y(lat) * g.shape[0] - 0.5
raw = np.zeros_like(g)
for tx in range(16):
    for ty in range(16):
        rgb = np.asarray(Image.open(f"{tiles}/4_{tx}_{ty}.png").convert("RGB"), np.float32)
        raw[ty * 256:(ty + 1) * 256, tx * 256:(tx + 1) * 256] = rgb[..., 0] * 256 + rgb[..., 1] + rgb[..., 2] / 256 - 32768
deep = resample(raw, xs, ys) < -200.0
Image.fromarray((deep * 255).astype(np.uint8), "L").save(f"{out}/watermask.png")
print("watermask.png (deep ocean only)", "%.1f %%" % (deep.mean() * 100))

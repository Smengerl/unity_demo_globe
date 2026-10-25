# tools/ – making the textures from raw data

Order (Python 3 with `numpy` and `Pillow`; work folder `temp-src` inside the project, ignored by Git and Unity):

```bash
tools/get_data.sh                                  # downloads NASA images and AWS elevation tiles (about 210 MB)
OUT=Assets/GlobeView/Resources/Earth
python3 tools/prep_textures.py temp-src $OUT        # day.jpg, night.jpg (global base)
python3 tools/make_normals.py  temp-src/tiles $OUT   # normal.png, watermask.png
python3 tools/make_waves.py    $OUT                  # waves.png (tileable wave normal map)
python3 tools/make_coast.py    $OUT                  # coast.png (needs day.jpg and watermask.png)
python3 tools/build_tiles.py                         # the tile pyramid into EarthTiles/ (reads globe-config.json)
```

| Script | Input | Output | Key numbers |
|---|---|---|---|
| `prep_textures.py` | `day_21600.jpg`, `night_3km.jpg` | `day.jpg`, `night.jpg`, 8192×4096 each | global base, in the repository |
| `make_normals.py` | Terrarium tiles z=4 | `normal.png` 4096×2048, `watermask.png` 8192×4096 (deep sea: height < −200 m) | ocean clamped to 0 m, slope exaggerated 8× (`K = 8`), encoding `n*0.5+0.5` with x = east, y = north, z = up |
| `make_coast.py` | `day.jpg`, `watermask.png` | `coast.png` 8192×4096 | grey value = distance from land over water, 0–8 px ≈ 39 km, same water rule as the shader |
| `make_waves.py` | – | `waves.png` 256×256 | FFT with a 1/f^1.4 spectrum, random phases (seed 7), seamlessly tileable, slope `k = 0.35` |
| `build_tiles.py` | `globe-config.json`, `temp-src` (all raw data, including terrain z=7) | `EarthTiles/<layer>/<level>/<x>_<y>.jpg|png` | tile pyramid for day, night, relief, coast; reads the same config as the view, `--estimate` counts beforehand; not in the repository |

The tiles need the raw data from `get_data.sh`; for more areas or finer levels download further sources (the table `IMAGE_SOURCES` in `build_tiles.py` lists the files, their areas and densities). Where a source is too coarse or too small no tiles are made.

Licenses and attribution of the source data: see [../docs/LICENSES.md](../docs/LICENSES.md).

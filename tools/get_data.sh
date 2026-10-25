#!/bin/bash
# Downloads the raw data for the globe textures (about 150 MB) into temp-src inside the project (git-ignored, outside Assets so Unity ignores it).
# Usage: tools/get_data.sh [work-dir]      (default: temp-src in the project)
# Needs: curl. Python's urllib failed with SSL certificate errors on macOS, curl uses the system certificates.
set -euo pipefail

WORK="${1:-$(cd "$(dirname "$0")/.." && pwd)/temp-src}"
mkdir -p "$WORK/tiles"
cd "$WORK"

BMNG=https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july
BM=https://assets.science.nasa.gov/content/dam/science/esd/eo/images/imagerecords/144000/144898

# NASA Blue Marble Next Generation, July 2004, topography + bathymetry
curl -fL -o day_21600.jpg "$BMNG/world.topo.bathy.200407.3x21600x10800.jpg"   # 21600x10800, 27 MB (day, global)
curl -fL -o day_C1.jpg    "$BMNG/world.topo.bathy.200407.3x21600x21600.C1.jpg" # 500 m tile lon 0..90E, lat 0..90N, 88 MB
# NASA Black Marble 2016 colour, 3 km
curl -fL -o night_3km.jpg "$BM/BlackMarble_2016_3km.jpg"                       # 13500x6750, 8 MB
curl -fL -o night_C1.jpg  "$BM/BlackMarble_2016_C1.jpg"                        # 500 m tile lon 0..90E, lat 0..90N, 59 MB

# AWS Open Data "Terrain Tiles" (Terrarium encoding: height = R*256 + G + B/256 - 32768 metres)
#   z=4: whole world (256 tiles), z=7: Central Europe tiles x 64..74, y 35..50 (176 tiles)
: > tiles.cfg
T=https://s3.amazonaws.com/elevation-tiles-prod/terrarium
for x in $(seq 0 15); do for y in $(seq 0 15); do
  printf 'url = "%s/4/%d/%d.png"\noutput = "tiles/4_%d_%d.png"\n' "$T" "$x" "$y" "$x" "$y" >> tiles.cfg
done; done
for x in $(seq 64 74); do for y in $(seq 35 50); do
  printf 'url = "%s/7/%d/%d.png"\noutput = "tiles/7_%d_%d.png"\n' "$T" "$x" "$y" "$x" "$y" >> tiles.cfg
done; done
curl -sS -f --parallel --parallel-max 8 --retry 3 -K tiles.cfg

echo "done: $(ls tiles | wc -l) tiles in $WORK/tiles"

import sys
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
src, out = sys.argv[1], sys.argv[2]

def save(img, name):
    img.convert("RGB").save(f"{out}/{name}", quality=92, optimize=True)
    print(name, img.size)

with Image.open(f"{src}/day_21600.jpg") as im:
    save(im.resize((8192, 4096), Image.LANCZOS), "day.jpg")

with Image.open(f"{src}/night_3km.jpg") as im:
    save(im.resize((8192, 4096), Image.LANCZOS), "night.jpg")

# Tile C1 = lon 0..90 E, lat 0..90 N, 240 px/deg, row 0 = 90 N.
LON0, LON1, LAT0, LAT1 = 0, 30, 35, 62
with Image.open(f"{src}/day_C1.jpg") as im:
    box = (LON0 * 240, (90 - LAT1) * 240, LON1 * 240, (90 - LAT0) * 240)
    crop = im.crop(box)
    w = 4096
    save(crop.resize((w, round(w * (LAT1 - LAT0) / (LON1 - LON0))), Image.LANCZOS), "detail.jpg")

# night lights for the same region: Black Marble 500 m tile C1, same crop and size as detail.jpg
with Image.open(f"{src}/night_C1.jpg") as im:
    box = (LON0 * 240, (90 - LAT1) * 240, LON1 * 240, (90 - LAT0) * 240)
    crop = im.crop(box)
    save(crop.resize((w, round(w * (LAT1 - LAT0) / (LON1 - LON0))), Image.LANCZOS), "detail_night.jpg")

with open(f"{out}/detail_rect.txt", "w") as f:
    f.write(f"{LON0} {LAT0} {LON1} {LAT1}\n")

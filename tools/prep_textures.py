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

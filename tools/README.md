# tools/ – Texturen aus Rohdaten erzeugen

Reihenfolge (Python 3 mit `numpy` und `Pillow`; Arbeitsordner `temp-src` im Projekt, von Git und Unity ignoriert):

```bash
tools/get_data.sh                                  # lädt NASA-Bilder und AWS-Höhenkacheln (ca. 210 MB)
OUT=Assets/GlobeView/Resources/Earth
python3 tools/prep_textures.py temp-src $OUT        # day.jpg, night.jpg, detail.jpg, detail_night.jpg, detail_rect.txt
python3 tools/make_normals.py  temp-src/tiles $OUT   # normal.png, detail_normal.png
python3 tools/make_waves.py    $OUT                               # waves.png (kachelbare Wellen-Normal-Map)
python3 tools/make_coast.py    $OUT                               # coast.png, detail_coast.png (braucht day.jpg, detail.jpg und watermask.png von make_normals.py)
```

| Skript | Eingabe | Ausgabe | Wichtige Zahlen |
|---|---|---|---|
| `prep_textures.py` | `day_21600.jpg`, `day_C1.jpg`, `night_3km.jpg`, `night_C1.jpg` | `day.jpg` 8192×4096, `night.jpg` 8192×4096, `detail.jpg` 4096×3686, `detail_night.jpg` 4096×3686, `detail_rect.txt` (`0 35 30 62`) | Kachel C1 = 0–90°E, 0–90°N bei 240 px/Grad; Zuschnitt 0–30°E, 35–62°N |
| `make_normals.py` | Terrarium-Kacheln z=4 und z=7 | `normal.png` 4096×2048, `detail_normal.png` 3072×2765, `watermask.png` 8192×4096 (Tiefsee: Höhe < −200 m) | Ozean auf 0 m geklemmt (flach), Neigung 8-fach überhöht (`K = 8`), Kodierung `n*0.5+0.5` mit x = Ost, y = Nord, z = oben |
| `make_coast.py` | `day.jpg`, `detail.jpg`, `watermask.png`, `detail_rect.txt` | `coast.png` 8192×4096, `detail_coast.png` 4096×3686 | Grauwert = Abstand vom Land über Wasser (global 0–8 px ≈ 39 km, Detail 0–48 px ≈ 34 km), gleiche Wasserregel wie der Shader; zur Küstenschaum-Berechnung |
| `make_waves.py` | – | `waves.png` 256×256 | FFT mit 1/f^1.4-Spektrum, zufällige Phasen (Seed 7), nahtlos kachelbar, Neigung `k = 0.35` |

Das Zuschneiden des Detailbereichs ist in `prep_textures.py` (`LON0, LON1, LAT0, LAT1`) und in `make_normals.py` (gleiche Konstanten) hart codiert. Beide müssen zusammenpassen, ebenso `detail_rect.txt`.

Lizenz: Die NASA-Bilder tragen auf den Quellseiten keine ausdrückliche Lizenz. NASA-Material ist üblicherweise gemeinfrei, vor einer Veröffentlichung die NASA-Nutzungsrichtlinien prüfen.

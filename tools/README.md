# tools/ – Texturen aus Rohdaten erzeugen

Reihenfolge (Python 3 mit `numpy` und `Pillow`; Arbeitsordner `temp-src` im Projekt, von Git und Unity ignoriert):

```bash
tools/get_data.sh                                  # lädt NASA-Bilder und AWS-Höhenkacheln (ca. 210 MB)
OUT=Assets/GlobeView/Resources/Earth
python3 tools/prep_textures.py temp-src $OUT        # day.jpg, night.jpg (global basis)
python3 tools/make_normals.py  temp-src/tiles $OUT   # normal.png, watermask.png
python3 tools/make_waves.py    $OUT                               # waves.png (kachelbare Wellen-Normal-Map)
python3 tools/make_coast.py    $OUT                               # coast.png (needs day.jpg and watermask.png)
python3 tools/build_tiles.py                                      # the tile pyramid into EarthTiles/ (reads globe-config.json)
```

| Skript | Eingabe | Ausgabe | Wichtige Zahlen |
|---|---|---|---|
| `prep_textures.py` | `day_21600.jpg`, `night_3km.jpg` | `day.jpg`, `night.jpg` je 8192×4096 | globale Basis, im Repo |
| `make_normals.py` | Terrarium-Kacheln z=4 | `normal.png` 4096×2048, `watermask.png` 8192×4096 (Tiefsee: Höhe < −200 m) | Ozean auf 0 m geklemmt, Neigung 8-fach überhöht (`K = 8`), Kodierung `n*0.5+0.5` mit x = Ost, y = Nord, z = oben |
| `make_coast.py` | `day.jpg`, `watermask.png` | `coast.png` 8192×4096 | Grauwert = Abstand vom Land über Wasser, 0–8 px ≈ 39 km, gleiche Wasserregel wie der Shader |
| `make_waves.py` | – | `waves.png` 256×256 | FFT mit 1/f^1.4-Spektrum, zufällige Phasen (Seed 7), nahtlos kachelbar, Neigung `k = 0.35` |
| `build_tiles.py` | `globe-config.json`, `temp-src` (alle Rohdaten, auch Terrain z=7) | `EarthTiles/<Ebene>/<Stufe>/<x>_<y>.jpg|png` | Kachel-Pyramide für Tag, Nacht, Relief, Küste; liest dieselbe Config wie die View, `--estimate` zählt vorab; nicht im Repo |

Die Kacheln brauchen die Rohdaten aus `get_data.sh`; wer mehr Gebiete oder feinere Stufen will, lädt weitere Quellen dazu (die Tabelle `IMAGE_SOURCES` in `build_tiles.py` nennt die Dateien, ihre Gebiete und Dichten). Wo eine Quelle zu grob oder zu klein ist, entstehen keine Kacheln.

Lizenz: Die NASA-Bilder tragen auf den Quellseiten keine ausdrückliche Lizenz. NASA-Material ist üblicherweise gemeinfrei, vor einer Veröffentlichung die NASA-Nutzungsrichtlinien prüfen.

# unity_demo_globe – Globus-View und HUD für den Airport Tycoon

Unity 6 (URP). Der Globus ist eine wiederverwendbare **View** für das Handyspiel (Android und iOS): Ein Controller füttert sie über `IGlobeModel` mit Spielzeit, Flughäfen, Routen und Flügen, die View meldet Taps und Kamera zurück. Die **Demo** zeigt sie mit einem Fake-Controller; die Spiellogik kommt später getrennt (C#, gleiche App).

Stand: lokale Umsetzung am Rechner (Phasen 1–6 des Migrationsplans). Noch offen: Handy-Portierung, Messung auf Geräten, das echte Spiel – siehe `docs/MIGRATIONSPLAN.md`.

## Einrichten und starten

Dieser Ordner ist ein fertiges Unity-6-Projekt (URP-Vorlage, Editor 6000.6.7f1).
Unity Hub → Add → Add project from disk → diesen Ordner wählen, öffnen, `Assets/Demo/Scenes/GlobeDemo` laden, Play. Fehlt die Szene: Menü *Tycoon → Create Demo Scene*.

Maus: ziehen = drehen, Scrollen = Zoomen, Klick auf Flughafen = auswählen. Touch: wischen, pinchen, tippen. Oben Datum, Zeit und Kasse als Klappkarten, darunter die Tempo-Pille (Pause, 10m, 1h, 6h, 1d), unten die Gate-Leiste. Mit F1 erscheinen die Entwickler-Werkzeuge: Qualitätsstufe, Stresstest (200 Flughäfen, 800 Routen), Fokus, Panel-Versatz, Eingabesperre.

Tests (EditMode, 38 Stück): *Window → General → Test Runner*, oder im Batchmodus (der Editor darf das Projekt nicht offen haben):

```bash
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

Screenshot ohne Editor-Fenster (Batchmodus, für Tests):

```bash
Unity -batchmode -projectPath . -executeMethod Tycoon.Demo.EditorTools.Capture.Run -tycoonShot out.png \
  -utc 2026-10-24T12:00:00 -lat 49 -lon 10 -height 0.12 [-rate 86400] [-tier low] [-stress]
```

## Aufbau

| Ordner | Inhalt |
|---|---|
| `Assets/GlobeView/Contracts` | Modelltypen, `IGlobeModel`, `IGlobeView`, `GlobeViewConfig` – die einzige Schnittstelle zum Spiel |
| `Assets/GlobeView/Runtime` | die View: `GlobeViewHost` (Einstieg), Kamera, Ebenen für Flughäfen/Routen/Flugzeuge, Himmel, Detailstufen, Lens Flare |
| `Assets/GlobeView/Resources` | Shader (Erde, Atmosphäre, Sterne, Sonne/Mond, Flare) und Texturen (`Earth/`) |
| `Assets/GlobeView/Editor` | Importeinstellungen der Erdtexturen |
| `Assets/Hud` | das HUD „Gate Chart“ (uGUI in Code): Klappkarten, Pillen, Auswahl-Fenster, Leiste, Tokens |
| `Assets/Demo` | `FakeController`, `DemoMain`, `DemoDevPanel` (F1), Demo-Szene, Capture-Werkzeug |
| `Assets/Tests/EditMode` | Tests der View, des Fake-Controllers und der Demo-Oberfläche |
| `tools/` | Python-Skripte, die die Texturen aus Rohdaten erzeugen |
| `docs/` | Lösungsdokumentation, Migrationsplan, Architekturvorschlag |

Koordinatenkonvention: `Assets/GlobeView/Runtime/GeoMath.cs`; Shader und C# nutzen dieselbe Abbildung.

## Texturen (`Assets/GlobeView/Resources/Earth/`)

Ohne Texturen ist die Erde grau. Dateinamen sind fest verdrahtet (jpg oder png); erzeugt mit `tools/` (siehe `tools/README.md`). Alles, was die View und das Skript einstellen, steht in `Assets/GlobeView/Resources/globe-config.json`:

| Datei | Inhalt |
|---|---|
| `day`, `night` | Tagtextur (NASA Blue Marble) und Stadtlichter (NASA Black Marble), equirektangular, 8192×4096 |
| `normal`, `waves` | Relief-Normal-Map (SRTM/AWS-Höhen) und kachelbare Wellen-Normal-Map |
| `watermask`, `coast` | Tiefsee-Maske und Küstenabstand (Schaum) |
| `EarthTiles/` (nicht im Repo) | die Kachel-Pyramide für Tag, Nacht, Relief und Küste; `python3 tools/build_tiles.py` macht sie aus den Rohdaten, siehe `docs/LOESUNG.md` Abschnitt 7b |

## Dokumentation

- `docs/LOESUNG.md` – vollständige Lösungsdokumentation (Entscheidungen, Architektur, Formeln, Datenpipeline, Stolpersteine, Übernahme ins echte Spiel)
- `docs/MIGRATIONSPLAN.md` – Plan und Status der Umstellung auf die View-Architektur
- `docs/VIEW-ARCHITEKTUR.md` – Architekturvorschlag mit Vertrag, Konfigurations- und Laufzeitwerten
- `docs/hud-design/HUD-DESIGN.md` und `hud-tokens.json` – finales HUD-Konzept „Gate Chart“ mit Design Tokens
- `tools/README.md` – Texturen aus Rohdaten erzeugen
- `docs/archiv/clouds-prototype.hlsl.txt` – verworfene prozedurale Wolken (Vorlage zum Backen einer Wolkentextur)

## Zoom-Stufen

Fünf Stufen nach Kamerahöhe (Z0 Weltraum … Z4 nah), Gerätestufe als Obergrenze (`DetailController`); Details in `docs/LOESUNG.md`, Abschnitt 12b. Im Demo-HUD: „Zoom Zx (Detail Zy)“.

## Qualitätsstufen

Beim Start nach Gerätespeicher/Kernen gewählt (Desktop = High), im Demo-HUD durchschaltbar, über `GlobeViewConfig.Tier` oder `SetQualityTier` festlegbar.

| Stufe | Inhalt |
|---|---|
| Low | flacher Ozean, kein Relief, keine Detailtextur, kein Bloom; höchstens Zoomstufe Z2 |
| Medium | + Relief, animierte Wellen, scharfe Nachtkarte, funkelnde Stadtlichter (ab Mitternacht nach und nach aus), Schaum und Brandung; bis Z3 |
| High | + Kachel-Detail (Tag, Relief, Küste), Bloom; bis Z4 |

Noch nicht umgesetzt (kommt mit der Handy-Portierung): Render-Scale, MSAA und halbe Texturgröße je Stufe, 30 fps auf dem Handy. Die prozeduralen Wolken wurden entfernt (zu teuer).

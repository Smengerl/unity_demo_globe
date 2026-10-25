# Schriften des HUD

Stand: 2026-10-25.

| Schrift | Datei | Verwendung |
|---|---|---|
| Barlow Regular | `Assets/Hud/Resources/Fonts/Barlow-Regular.ttf` | kleine Texte (Bildunterschriften) |
| Barlow Bold | `Assets/Hud/Resources/Fonts/Barlow-Bold.ttf` | Titel, Texte, Buttons |
| Barlow Condensed Bold | `Assets/Hud/Resources/Fonts/BarlowCondensed-Bold.ttf` | Klappkarten, Codes, Kennzahlen, Beschriftungen |

Quelle: Google-Fonts-Repository (`github.com/google/fonts`, Ordner `ofl/barlow` und `ofl/barlowcondensed`), Version 1.408, Download am 2026-10-25. Urheber: The Barlow Project Authors (<https://github.com/jpt/barlow>).

## Lizenz

SIL Open Font License, Version 1.1. Die Lizenztexte liegen neben den Schriften (`OFL-Barlow.txt`, `OFL-BarlowCondensed.txt`). Die Lizenz erlaubt die Nutzung in kommerziellen Apps und das Einbetten in die App; der Verkauf der Schrift allein ist nicht erlaubt.

Pflichten bei der Veröffentlichung:
- Lizenztext und Copyright-Hinweis beim Weitergeben der Schriftdateien mitliefern. Für die App heißt das: eine Seite „Lizenzen“ (oder „Danksagung“) in der App mit Namen, Copyright-Zeile und Lizenzname, die Lizenztexte in der Repository-Kopie lassen.
- Die Schriftdateien nicht verändern und unter dem Originalnamen weitergeben. Ein Fontatlas, der in Unity zur Laufzeit aus der Datei entsteht, zählt nicht als Änderung der Schrift.

Das ist keine Rechtsberatung; der Lizenztext ist maßgeblich.

## Rückfall

Fehlen die Dateien, nimmt `HudFonts` die erste installierte ähnliche Systemschrift (Arial Narrow, Roboto Condensed …), zuletzt Unitys eingebaute Schrift.

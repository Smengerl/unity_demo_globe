# HUD fonts

Status: 2026-10-25.

| Font | File | Used for |
|---|---|---|
| Barlow Regular | `Assets/Hud/Resources/Fonts/Barlow-Regular.ttf` | small text (captions) |
| Barlow Bold | `Assets/Hud/Resources/Fonts/Barlow-Bold.ttf` | titles, body text, buttons |
| Barlow Condensed Bold | `Assets/Hud/Resources/Fonts/BarlowCondensed-Bold.ttf` | flip cards, codes, figures, labels |

Source: Google Fonts repository (`github.com/google/fonts`, folders `ofl/barlow` and `ofl/barlowcondensed`), version 1.408, downloaded 2026-10-25. Author: The Barlow Project Authors (<https://github.com/jpt/barlow>).

## License

SIL Open Font License 1.1. The license texts sit next to the fonts (`OFL-Barlow.txt`, `OFL-BarlowCondensed.txt`). The license allows use in commercial apps and embedding in the app; selling the font on its own is not allowed.

Obligations when publishing:
- Ship the license text and copyright notice with the font files. For the app this means a "Licenses" (or "Credits") page with name, copyright line and license name; keep the license texts in the repository.
- Do not modify the font files, and pass them on under their original name. A font atlas that Unity builds from the file at run time does not count as modifying the font.

This is not legal advice; the license text is authoritative. See also [LICENSES.md](../LICENSES.md).

## Fallback

If the files are missing, `HudFonts` uses the first installed similar system font (Arial Narrow, Roboto Condensed …) and finally Unity's built-in font.

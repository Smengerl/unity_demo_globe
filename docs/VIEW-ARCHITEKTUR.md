# GlobeView als View im Spiel – Architekturvorschlag

> **Hinweis (2026-10-25):** Dieser Vorschlag wurde im [Migrationsplan](MIGRATIONSPLAN.md) überarbeitet. Dort gelten: Variante B (ein Unity-Projekt, Logik als reine C#-Assembly), **Fahrplan statt Flugzeugpositionen**, Verhalten bei hohen Tempostufen, Beschriftungen durch die Spiel-UI, weniger Konfigurationsparameter.

Stand: 2026-10-25. Vorschlag, noch nicht umgesetzt. Ziel: Der Globus-Prototyp wird zur **austauschbaren Render-Komponente (View)** eines Android-/iOS-Spiels. Spiellogik und Datenmodell liegen getrennt; der Controller liefert Zeit, Flughäfen, Routen und Flugzeugpositionen, die View **rendert nur**. Spiel-UI (Buttons, Uhrzeit, Geld, Menüs) liegt später darüber.

---

## 1. Leitprinzipien

1. **Eine Richtung der Abhängigkeit.** Die View kennt das Spiel nicht. Das Spiel kennt die View nur über eine schmale Schnittstelle (Verträge). Kein `FindObjectOfType`, keine Singletons, keine `RuntimeInitializeOnLoadMethod`, keine statischen Spielzustände (`SimClock.I`, `GlobeLod.Stage` usw. werden Instanzen).
2. **IDs statt Objekte.** Alles, was die View zeigt, hat eine stabile ID des Controllers (Flughafen, Route, Flugzeug). Die View hält nur Darstellungsdaten, nie Spielzustand.
3. **Snapshot + Änderungen.** Seltene Daten (Flughäfen, Routen) kommen als Snapshot mit Versionszähler, häufige (Flugzeuge, Zeit) werden pro Frame ohne Speicherzuweisung gelesen.
4. **Die View entscheidet über das *Wie*, der Controller über das *Was*.** Detailstufen, Qualität, Kamera, Shader sind Sache der View. Konfiguration beim Start und Befehle zur Laufzeit lassen den Controller Grenzen setzen, nie Rendering-Details.
5. **UI liegt darüber, nicht darin.** Die View zeichnet keine Menüs. Sie liefert Bildschirmpositionen (Anker) und meldet Taps; die Spiel-UI entscheidet, wer Eingaben bekommt.

---

## 2. Paketstruktur

Ein Unity-Paket (UPM, lokal oder Git) mit Assemblies:

| Assembly | Inhalt | Abhängigkeiten |
|---|---|---|
| `Tycoon.GlobeView.Contracts` | Konfiguration, DTO-Structs, Schnittstellen, Ereignisse. Reines C#, keine Unity-Typen außer `Vector2`, `Color32` | keine |
| `Tycoon.GlobeView.Runtime` | Rendering, Kamera, Ebenen, Qualität/Detailstufen, Streaming | Contracts, URP, Input System |
| `Tycoon.GlobeView.Editor` | Texturimport, Capture-Werkzeug (Screenshots), Datenpipeline-Menü | Runtime |
| `Tycoon.GlobeView.Samples` | Demo-Host mit Fake-Modell (der heutige Prototyp mit Flughäfen/Routen), Debug-Overlay | Runtime |
| *(im Spiel)* `Game.GlobeAdapter` | implementiert die Schnittstellen auf dem echten Datenmodell | Contracts |

Assets (Shader, Texturen, Prefab) liegen im Paket; Shader und Texturen weiter über `Resources` oder, besser für Handy, **Addressables** (Texturen einzeln ladbar, z. B. Regionaldetail als eigenes Bündel).

---

## 3. Einstieg und Lebenszyklus

```
var host = Instantiate(globeViewPrefab);               // Prefab statt GlobeBootstrap
host.Initialize(config, source);                         // config: GlobeViewConfig, source: IGlobeModelSource
host.Ready += ...;                                       // Basistexturen geladen, erster Frame möglich
...
host.SetVisible(false) / Pause() / Resume() / Dispose()  // Menüs, App im Hintergrund, Szenenwechsel
```

- **Initialisierung asynchron** (Basistexturen, Shader-Warmup, optional Shader-Variantensammlung), Fortschritt über `LoadingProgress`.
- **Pause/Resume** für App-Lifecycle und Vollbild-Menüs: Kamera aus oder auf 10 fps, keine Simulations-Updates. Verhindert Akku- und Wärmeverbrauch.
- **Kamerazustand speichern/laden:** `GetCameraState()`, `SetCameraState()` (Spielstand-Persistenz).

---

## 4. Schnittstelle Controller → View

Zwei Wege, beide denkbar. Empfehlung: **Pull für schnelle Daten, Push für seltene**.

```
interface IGlobeModelSource {                     // vom Spiel implementiert (Adapter)
    GameTimeState Time { get; }                   // jede Frame gelesen
    int AirportsVersion { get; }                  // View liest die Liste neu, wenn sich die Zahl ändert
    IReadOnlyList<AirportView> Airports { get; }
    int RoutesVersion { get; }
    IReadOnlyList<RouteView> Routes { get; }
    int FlightCount { get; }
    void FillFlights(Span<FlightView> buffer);    // jede Frame, ohne Speicherzuweisung
    SelectionState Selection { get; }             // Auswahl/Hervorhebung vom Controller
}
```

Alternativ ein push-API (`view.SetAirports(...)`, `view.UpdateFlights(...)`), wenn die Simulation in einem anderen Thread läuft: dann gehört eine doppelt gepufferte Übergabe dazu (`IGlobeModelSource` wird vom Adapter aus dem letzten fertigen Snapshot bedient).

### 4.1 Laufzeitwerte (Controller → View)

**Zeit** (`GameTimeState`), jede Frame
| Feld | Typ | Bedeutung |
|---|---|---|
| `UtcSeconds` | `double` | Spielzeit als Unix-Zeit (Sekunden), bestimmt Sonnenstand, Mond, Sterne, Tag/Nacht, Lichter |
| `Rate` | `double` | Spielsekunden pro Echtsekunde (0 = Pause), die View extrapoliert zwischen Updates |
| `Paused` | `bool` | Pause (Funkeln und Wellen laufen in Echtzeit weiter) |

Die View besitzt **keine eigene Spielzeit** mehr (heute `SimClock`); sie hat nur noch eine interne Echtzeituhr fürs Funkeln und die Wellen.

**Flughäfen** (`AirportView`), nur bei Änderung (`AirportsVersion`)
| Feld | Bedeutung |
|---|---|
| `Id` | stabile ID des Controllers |
| `Lat`, `Lon` | Position in Grad |
| `Label` | Kurztext (z. B. IATA) |
| `Importance` | 0–3: Hub-Stufe, steuert Beschriftung und Sichtbarkeit je Zoomstufe (ersetzt die fest codierte Hub-Liste) |
| `Kind` | eigen / Konkurrent / verfügbar / gesperrt (Markerstil) |
| `Color` | Markerfarbe (z. B. Airline-Farbe) |
| `Size` | Markergröße (Klassen) |
| `Highlight` | pulsierend, ausgewählt |

**Routen** (`RouteView`), nur bei Änderung (`RoutesVersion`)
| Feld | Bedeutung |
|---|---|
| `Id`, `FromId`, `ToId` | Verbindung zwischen zwei Flughäfen |
| `Importance` | Hauptroute ja/nein; bestimmt, ob sie in Z0/Z1 gezeigt wird (ersetzt „beide Enden Hubs“) |
| `Color`, `Width` | Linienstil (Airline, Auslastung) |
| `Status` | aktiv / geplant / pausiert (gestrichelt, gedimmt) |

Der Bogen (Höhenprofil) wird **von der View** berechnet, damit Linien und Flugzeuge garantiert deckungsgleich sind.

**Flugzeuge** (`FlightView`), jede Frame
| Feld | Bedeutung |
|---|---|
| `Id` | stabile ID |
| `RouteId` | Route, auf der es fliegt |
| `T` | Fortschritt 0–1 entlang der Route |
| `Backwards` | Richtung (Rückflug) |
| `TPerSecond` | optional: Fortschritt pro Echtsekunde, die View extrapoliert zwischen Controller-Ticks (z. B. 4 Hz Controller, 60 fps View) |
| `Kind` | Flugzeugklasse (Mesh/Größe) |
| `Color` | Airline-Farbe |

Optional zusätzlich ein freier Modus `FlightPose` (Lat, Lon, Höhe, Kurs) für Warteschleifen oder Sonderflüge. Positionsvorgabe (lat/lon) statt `T` ist möglich, aber die Übereinstimmung mit dem gezeichneten Bogen muss dann der Controller sicherstellen.

**Auswahl und Anzeige** (`SelectionState`), bei Änderung
`SelectedAirportId`, `SelectedRouteId`, `HoveredId`; Filter (`ShowOwnRoutesOnly`, `ShowLabels`, `ShowFlights`, `ShowLights`).

### 4.2 Befehle (Controller → View, einmalig)
| Befehl | Zweck |
|---|---|
| `FocusOn(lat, lon, height, duration)` / `FocusAirport(id)` | Kameraflug (z. B. beim Kauf einer Linie) |
| `SetZoomLimits(min, max)` | Zoomgrenzen ändern (Tutorial, Freischaltung) |
| `SetInputEnabled(rotate, zoom, tap)` | Eingabe sperren (Dialoge) |
| `SetViewportInsets(top, bottom, left, right)` | Globus-Mittelpunkt verschiebt sich, wenn Menüs Platz brauchen (Safe Area, Panels) |
| `SetQuality(tier)` / `SetFrameRateTarget(fps)` | Qualität, Bildrate |
| `SetPaused(bool)` / `SetVisible(bool)` | Menüs, App-Lifecycle |
| `ResetView()` | Standardansicht |

### 4.3 Ereignisse und Abfragen (View → Controller)
| Ereignis / Abfrage | Inhalt |
|---|---|
| `AirportTapped(id)`, `RouteTapped(id)`, `FlightTapped(id)`, `EmptyTapped(lat, lon)` | Auswahl durch den Spieler |
| `CameraChanged(lat, lon, height, zoomStage)` | für UI-Anpassungen (z. B. Zoomleiste) |
| `ZoomStageChanged(from, to)` | optional |
| `Ready`, `LoadingProgress` | Start |
| `TryGetScreenPoint(id, out Vector2, out bool visible)` | **Anker für die Spiel-UI** (Tooltips, Beschriftungen, Pfeile); ohne diese Abfrage kann die UI nichts am Globus verankern |

---

## 5. Startkonfiguration (`GlobeViewConfig`)

Als `ScriptableObject` (Standardwerte im Editor) plus Überschreiben im Code beim `Initialize`.

### Kamera und Eingabe
| Parameter | Standard | Anmerkung |
|---|---|---|
| `InitialLat`, `InitialLon`, `InitialHeight` | 48, 10, 3 | Startansicht |
| `MinHeight`, `MaxHeight` | 0,12 / 5 | Zoomgrenzen (Erdradien über der Oberfläche) |
| `FieldOfView` | 30 | |
| `LatitudeLimit` | 80 | Pol-Grenze |
| `DragSensitivity`, `PinchSensitivity`, `ScrollSensitivity` | 1, 1, 0,01 | |
| `InertiaDamping` | 4 | Trägheit nach dem Wischen |
| `TapSlopMm` | 1,8 | Toleranz fürs Tippen (in Millimetern, nicht Pixeln) |
| `AllowRotate`, `AllowZoom` | an | |
| `ViewportInsets` | 0 | Startversatz für Panels |
| `GlobeScreenFraction` | 1 | Anteil der Bildschirmhöhe, den der Globus bei maximalem Zoom-Out einnimmt (Platz für UI oben und unten) |

### Qualität
| Parameter | Standard | Anmerkung |
|---|---|---|
| `Tier` | Auto | Auto / Low / Medium / High |
| `StageCapOverride` | Auto | Obergrenze der Zoomstufe (Low Z2, Medium Z3, High Z4) |
| `TargetFrameRate` | 30 | Handy |
| `RenderScale`, `MsaaSamples`, `EnableBloom` | je Tier | |
| `Features` | alle an | schaltet Wellen, Relief, Schaum, Lichtpunkte, Lens Flare, Regionaltexturen einzeln ab |
| `TextureMemoryBudgetMb` | 256 | Regionaltexturen werden nur geladen, wenn sie ins Budget passen |
| `ThermalAutoDowngrade` | an | bei Überhitzung (Android ADPF / iOS Thermal State) zur nächsten Qualitätsstufe |

### Detailstufen
`StageEdges` (Standard `{3,2; 1,6; 0,75; 0,32}`), `Hysteresis` (12 %), `UnloadDelaySeconds` (4), `PrefetchStages` (1: Regionaltexturen eine Stufe früher laden).

### Aussehen
`AtmosphereColor`, `AtmosphereIntensity`, `CityLightGain`, `LightDensity`, `Sparkle`, `TwinkleSpeed`, `SunGlint`, `LandGlint`, `Foam`, `Relief`, `NightDay`, `NightMapAlpha`, `FlareEnabled`, `StarDensity`, `ShowMoon`, `GoldAmount`. Einmal als **Look-Presets** (Standard, „mehr Stil“, „leise“ für Low-End) bündelbar.

### Astronomie und Zeit
`SunMode`: `Astronomical` (echte Sonne aus `UtcSeconds`) / `Fixed(lat, lon)` (z. B. für ein Menü-Hintergrundbild) / `Disabled`. `MoonEnabled`, `StarsFollowTime`.

### Kapazitäten (Vorreservierung ohne Speicherzuweisung zur Laufzeit)
`MaxAirports` (512), `MaxRoutes` (2048), `MaxFlights` (2048), `LineSegments` (48).

### Darstellung der Objekte
`LabelMode` (View zeichnet / aus / nur Anker), `MarkerStyles` (Markergrößen und -formen je `Kind`), `RouteAltitudeScale` (Bogenhöhe), `PlaneScale`.

### Daten und Assets
`AssetProvider` (Resources / Addressables), `DetailRegions[]` (Liste: Rechteck lon/lat und Texturnamen je Region; heute ist es genau eine: Mitteleuropa; später mehrere), `Textures` (Namen oder Verweise der Basistexturen).

### Debug
`ShowDebugOverlay` (Stufe, Gerät, fps), `CaptureMode` (für automatisierte Screenshots).

---

## 6. Umbau des bestehenden Codes

| heute | wird | Änderung |
|---|---|---|
| `GlobeBootstrap` (baut zur Laufzeit alles per Code) | `GlobeViewHost` + Prefab | Szene/Prefab statt `RuntimeInitializeOnLoadMethod`; `Initialize(config, source)` |
| `SimClock` | `ViewClock` | keine eigene Spielzeit, nur Anzeige der vom Controller gelieferten; interne Echtzeituhr für Funkeln |
| `SkyDriver`, `Astronomy` | `SkyRenderer`, `Astronomy` (unverändert) | bekommt `UtcSeconds` vom Controller |
| `AirportData` (fest codierte Liste und Hubs) | entfällt | Daten kommen als `AirportView`; Hubs über `Importance` |
| `AirportLayer` | `AirportLayer` | gepoolte Marker aus `AirportView`; meldet `AirportTapped(id)`; `TryGetScreenPoint` |
| `FlightLayer` | `RouteLayer` + `FlightLayer` | Routen aus `RouteView`, Flugzeuge aus `FillFlights` (gepoolt, später GPU-Instancing); Bogenhöhe bleibt in der View |
| `GlobeHud` (IMGUI) | entfällt in der View | Buttons/Uhr/Geld gehören der Spiel-UI; bleibt nur als `DebugOverlay` im Sample |
| `GlobeCameraController` | `GlobeCamera` | Konfiguration, Befehle (`FocusOn`, Limits, Insets), Eingabe über eine Schnittstelle `IGlobeInput`; Sperre durch die UI statt statischer Rechtecke |
| `GlobeQuality`, `GlobeLod` | `QualityManager`, `LodController` (Instanzen) | Konfiguration statt Konstanten, kein statischer Zustand; thermisches Herunterschalten |
| `Capture` (Editor) | bleibt Editor-Werkzeug | startet den `GlobeViewHost` mit einem Fake-Modell |
| statische Reset-Methoden | entfallen | wegen Instanzen nicht mehr nötig |

**Kamera und UI:** Der Globus rendert mit einer eigenen Kamera. Die Spiel-UI liegt in einem **Overlay-Canvas** (UI Toolkit oder uGUI) darüber und kostet fast nichts. Eingabe-Vorrang: Zuerst fragt die View die UI (`EventSystem.IsPointerOverGameObject` bzw. `panel.Pick`), erst dann bearbeitet sie Drehen, Zoomen, Tippen. Der Versatz bei offenen Panels läuft über `SetViewportInsets`.

---

## 7. Mobile-Aspekte (Android und iOS)

- **Texturen:** ASTC auf beiden Plattformen; Importeinstellungen pro Plattform (Android 4096er Obergrenze für Medium).
- **Grafik-APIs:** Metal (iOS), Vulkan/GLES3 (Android). Die Shader nutzen nur `ddx/ddy`, `multi_compile_local` und `SAMPLE_TEXTURE2D_LOD/GRAD`; auf beiden Plattformen früh testen. Shader-Variantensammlung vorwärmen, damit beim ersten Stufenwechsel kein Ruckeln entsteht.
- **Energie:** 30 fps, Pause bei Vollbild-Menüs, thermische Rückstufung.
- **Speicher:** Regionaltexturen mit Budget; Entladen nach Herauszoomen (bereits umgesetzt, 4 s).
- **Instancing:** Flugzeuge und Marker per `Graphics.DrawMeshInstanced`, sobald mehrere Hundert sichtbar sind.

---

## 8. Test und Qualitätssicherung

- **Sample-Szene** mit Fake-Modell (Zufallsflüge, einige Hundert Flughäfen) als Dauertest und Demo.
- **Screenshot-Tests** (Capture-Werkzeug): je Zoomstufe × Gerätestufe ein Referenzbild, Abweichungen vergleichen.
- **Vertragstests** (reines C#): `AirportView`/`RouteView` validieren (IDs eindeutig, Routen verweisen auf vorhandene Flughäfen).
- **Messung** auf je einem schwachen, mittleren und starken Gerät pro Plattform; Schwellen der Gerätestufen anpassen.

---

## 9. Reihenfolge der Umsetzung (Vorschlag)

1. **Verträge** (DTOs, `IGlobeModelSource`, `GlobeViewConfig`) und Paket-/Assemblystruktur anlegen.
2. **Host und Prefab** ersetzen den Bootstrap; Statics entfernen; `SimClock` zu `ViewClock`.
3. **Daten aus dem Modell:** `AirportLayer`, `RouteLayer`, `FlightLayer` auf Pooling und IDs umstellen, Ereignisse, `TryGetScreenPoint`.
4. **Kamera:** Konfiguration, Befehle, Eingabe-Schnittstelle, Insets.
5. **Qualität/Detailstufen** auf Konfiguration umstellen, thermisches Herunterschalten.
6. **Sample-Szene und Fake-Modell**, danach das alte HUD zum Debug-Overlay verkleinern.
7. **Mobile-Tests** (Android und iOS) und Messungen.

---

## 10. Offene Entscheidungen

1. **Push oder Pull** für die schnellen Daten (Flugzeuge): Pull mit `FillFlights` ist am sparsamsten, setzt aber voraus, dass der Adapter threadsicher liest.
2. **Flugzeugposition:** Fortschritt `T` entlang der Route (empfohlen, deckungsgleich mit dem Bogen) oder freie Position (Lat/Lon/Höhe).
3. **Beschriftungen:** zeichnet die View sie (TextMeshPro, gepoolt) oder die Spiel-UI an den Ankern aus `TryGetScreenPoint`?
4. **UI-Technik:** UI Toolkit oder uGUI (beeinflusst die Eingabe-Arbitration).
5. **Zeit-Hoheit:** Der Controller ist der einzige Takt; soll die View bei Rate 0 weiter Sonne/Mond fortschreiben (z. B. Menühintergrund)? Vorschlag: nein, aber `SunMode.Fixed` für Menüs.
6. **Größenordnung:** Wie viele Flughäfen, Routen, Flugzeuge gleichzeitig (bestimmt Pooling und Instancing)?
7. **Verteilung:** UPM-Paket in eigenem Repo oder Unterordner des Spiels; Versionierung der Schnittstelle.

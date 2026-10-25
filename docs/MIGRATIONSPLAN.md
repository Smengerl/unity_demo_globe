# Migrationsplan: vom Globus-Prototyp zur View im Spiel

Stand: 2026-10-25. **Phasen 1–6 sind lokal am Rechner umgesetzt** (Status und Abweichungen unten); offen sind der Geräteteil von Phase 0, Phase 7 und Phase 8. Baut auf `VIEW-ARCHITEKTUR.md` auf und ersetzt dort die Abschnitte zu Flugzeugdaten (jetzt Fahrplan), Zeit/Tempo, Verträgen und Projektaufbau.

**Randbedingungen:** Spiellogik und Datenmodell liegen in **derselben App** und werden in **C#** neu geschrieben. Die heutige Lösung bleibt als **Demo** erhalten, mit einem **Fake-Controller**, der Flughäfen, Routen und Flüge in die View einspeist. Aufwandsangaben sind Schätzungen für eine Person in Arbeitstagen, nicht gemessen.

---

## Umsetzungsstand (2026-10-25)

Lokale Umsetzung am Rechner, ohne Android/iOS und ohne Performancemessung, danach ein Review auf Vereinfachung und toten Code.

| Phase | Stand | Anmerkung |
|---|---|---|
| 0 Absichern und messen | teilweise | Git-Repository seit 2026-10-25 (privat auf GitHub: `Smengerl/unity_demo_globe`); Geräte-Messung offen |
| 1 Struktur und Verträge | erledigt | Assemblies `Tycoon.GlobeView.Contracts`, `.GlobeView`, `.GlobeView.Editor`, `.Demo`, `.Demo.Editor`, `.Tests.EditMode`; Prototyp-Ordner entfernt |
| 2 Host und Instanzen | erledigt | `GlobeViewHost` ohne Singleton und ohne Statics (außer den Shader-Globals), `Dispose`/`SetActive` |
| 3 Datenebenen | erledigt | `GlobeData` mit Versionszählern, Marker/Routen/Flugzeuge gepoolt, Fahrplan, Verkehrsfluss ab 6 h/s |
| 4 Kamera und Eingabe | erledigt | `GlobeCamera`: Tap über Bildschirmabstand, UI-Vorrang, `FocusOn`/`FrameRoute`, Viewport-Versatz, Zoomgrenzen |
| 5 Qualität, Detailstufen, Tempo | erledigt (ohne Mobile-Einstellungen) | `DetailController`, Calm-Faktor im Shader, Gerätestufe als Obergrenze der Zoomstufe |
| 6 Demo-UI und Overlay | erledigt | `DemoHud` in uGUI, Beschriftungen über `TryGetAirportScreenPoint`, Stresstest |
| 7 Geräte-Härtung | offen | Render-Scale/MSAA/HDR je Stufe, halbe Texturgröße, ASTC, Messung |
| 8 Echtes Spiel | offen | `Tycoon.Domain`, `GameController`, echtes HUD |

**Prüfung:** 38 EditMode-Tests grün (22 der View, 16 des HUDs) (Geometrie, Astronomie, Zoomstufen, Verträge, Fake-Controller, `GlobeData`, Schnittstelle der View end-to-end, Himmel/Calm, Verkehrsfluss, Demo-Beschriftungen). Screenshots der Stufen Z0, Z2, Z4, Lens Flare, Calm bei 1 Tag/s, Verkehrsfluss und Low-Stufe entsprechen dem Prototyp. **Nicht geprüft:** Play-Modus im Editor des Nutzers, Geräte.

**Abweichungen vom Plan (bewusst):**
- Keine Look-Presets; die Optik ist eine Einstellung ohne Varianten.
- Kein Prefab und kein Addressables: `GlobeViewHost.Create(config, model, camera)` baut alles per Code; die Texturen bleiben in `Resources`.
- Beschriftungen zeichnet die Spiel-UI (Demo: `DemoHud`), nicht die View; sie bekommt nur die Bildschirmanker.
- Schlankere Verträge: `AirportView` ohne `Highlight` (die Auswahl steckt im `SelectionState`), `SelectionState` nur mit `AirportId`/`RouteId` (ohne `FlightId` und Filter), nur das Ereignis `AirportTapped` (kein `RouteTapped`/`FlightTapped`), `ZoomStageChanged` mit der neuen Stufe als einzigem Wert, `SetActive` statt `Pause`/`Resume`/`SetVisible`. Das Antippen von Routen und Flugzeugen ist nicht umgesetzt, bis das Spiel es braucht.
- URP-Mobileinstellungen (Render-Scale, MSAA, HDR) und `globalTextureMipmapLimit` sind für Phase 7 zurückgestellt; `GlobeViewConfig.TargetFrameRate` ist vorhanden.
- Zusätzlich entfernt: Template-Reste (TutorialInfo, Readme, SampleScene, `BillingMode.json`), neun nicht benötigte Pakete, ungenutzte Shader-Funktionen und Hilfsmethoden.

---

## 1. Zielbild

### 1.1 Projektaufbau (ein Unity-Projekt, Grenzen über Assembly Definitions)

```
Assets/
  GlobeView/                         Paket-ähnlich, später als UPM auslagerbar
    Contracts/   Tycoon.GlobeView.Contracts   reines C#, keine UnityEngine-Abhängigkeit außer Vector2/Color32
    Runtime/     Tycoon.GlobeView             Rendering, Kamera, Ebenen, Qualität/Detailstufen, Streaming
    Shaders/ Textures/ Prefabs/
    Editor/      Tycoon.GlobeView.Editor      Texturimport, Capture, Menüs
  Game/
    Domain/      Tycoon.Domain                Spiellogik und Datenmodell, reines C#, keine Unity-Abhängigkeit (testbar ohne Editor)
    App/         Tycoon.App                   GameController: verbindet Domain und View (implementiert IGlobeModel), Befehle
    UI/          Tycoon.UI                    Overlay-Oberfläche (Buttons, Uhr, Geld, Menüs)
  Demo/          Tycoon.Demo                  Fake-Controller, Demo-Szene, Debug-Overlay (der heutige Prototyp)
  Tests/         EditMode und PlayMode
tools/ docs/
```

### 1.2 Abhängigkeiten (Pfeil = „kennt“)

```
UI ──► App ──► Domain
 │      │
 │      └────► GlobeView.Contracts ◄──── GlobeView (Runtime)
 └──────────────────────────────────────►  GlobeView (Host-API: Befehle, Anker)

Demo ──► GlobeView.Contracts, GlobeView, (Demo-UI)        Demo kennt Domain/App/UI nicht
```

- `GlobeView` kennt weder `Domain` noch `App` noch `UI`. Das erzwingen die Assembly-Grenzen (ein falscher Verweis kompiliert nicht).
- `Domain` kennt keine Unity-Typen: Logik lässt sich ohne Editor testen und wäre bei Bedarf auf einen Server verlagerbar.
- Der **Fake-Controller** der Demo implementiert dieselbe Schnittstelle wie später der `GameController`. Der Austausch ist ein Zeilenwechsel im Host-Aufbau.

### 1.3 Verträge (neu: Fahrplan statt Positionen)

```csharp
namespace Tycoon.GlobeView.Contracts {

public interface IGlobeModel {                     // wird vom Controller implementiert, von der View gelesen
    GameTimeState Time { get; }                    // pro Frame
    int AirportsVersion { get; }  IReadOnlyList<AirportView> Airports { get; }   // bei Änderung neu gelesen
    int RoutesVersion   { get; }  IReadOnlyList<RouteView>   Routes   { get; }
    int FlightsVersion  { get; }  IReadOnlyList<FlightView>  Flights  { get; }   // Fahrplan, nicht Position
    SelectionState Selection { get; }
}

public readonly struct GameTimeState {             // Zeit gehört dem Controller
    public readonly double UtcSeconds;             // Spielzeit (Unix-Sekunden)
    public readonly double Rate;                   // Spielsekunden pro Echtsekunde, 0 = Pause
}

public readonly struct AirportView { int Id; string Label; float Lat, Lon; byte Importance; /*0..3*/ AirportKind Kind; Color32 Color; byte Size; bool Highlight; }
public readonly struct RouteView   { int Id, FromId, ToId; byte Importance; /*0 normal, 1 Hauptroute*/ Color32 Color; float Width; RouteStatus Status; }

public readonly struct FlightView {                // der View genügt der Fahrplan; Position = f(Spielzeit)
    public readonly int Id, RouteId;
    public readonly double DepartUtc, ArriveUtc;   // Spielzeit; bei Verspätung ändert der Controller ArriveUtc und erhöht FlightsVersion
    public readonly bool Backwards;                // Richtung entlang der Route
    public readonly byte Kind;                     // Flugzeugklasse
    public readonly Color32 Color;
}

public struct SelectionState { int AirportId, RouteId, FlightId; /*-1 = keine*/ SelectionFilter Filter; }

public interface IGlobeEvents {                    // von der View aufgerufen, vom Controller/der UI abonniert
    event Action<int> AirportTapped, RouteTapped, FlightTapped;
    event Action<float,float> EmptyTapped;         // lat, lon
    event Action<CameraState> CameraChanged;
    event Action<int,int> ZoomStageChanged;
    event Action Ready;
}
}
```

Die **Host-API** der View (nicht des Modells):
`Initialize(GlobeViewConfig, IGlobeModel)`, `Pause()`, `Resume()`, `SetVisible(bool)`, `Dispose()`,
`FocusOn(lat, lon, height, duration)`, `FocusAirport(id)`, **`FrameRoute(id)`** (beide Enden ins Bild),
`SetZoomLimits(min, max)`, `SetInputEnabled(rotate, zoom, tap)`, `SetViewportInsets(top, bottom, left, right)`,
`SetQuality(tier)`, `GetCameraState()/SetCameraState()`, `TryGetScreenPoint(kind, id, out Vector2, out bool visible)`.

### 1.4 Verhalten bei hohen Tempostufen (neue Spezifikation)

| Tempo | Flugzeuge | Tag/Nacht |
|---|---|---|
| bis 10 min/s | einzelne Maschinen auf den Routen | normal |
| 1 h/s (3600) | einzelne Maschinen, Flugzeit 4–12 s sichtbar | normal |
| ab ca. 6 h/s | **Verkehrsfluss**: pulsierende Lichtpunkte auf den Routen, Dichte nach Anzahl der Flüge, keine Einzelmaschinen | Tag/Nacht-Kontrast wird mit steigender Rate gedämpft (Schwelle 3600 → 21600), damit es nicht blinkt |
| 1 Tag/s (86400) | nur Fluss | Dämmerung als sanftes Pulsieren der Helligkeit, **kein** hartes Wechseln |

Die Schwellen sind Startwerte und gehören mit Testern abgestimmt (lichtempfindliche Spieler!). Zusätzlich eine Einstellung „Bewegung reduzieren“.

---

## 2. Entscheidungen (mit Begründung)

| Thema | Entscheidung | Grund |
|---|---|---|
| Spielarchitektur | **Variante B**: ein Unity-Projekt, Logik als reine C#-Assembly | einfachster Weg bei gleicher App und C#; harte Trennung über Assemblies |
| Flugdaten | **Fahrplan** (`DepartUtc`, `ArriveUtc`), View berechnet Position | funktioniert mit Pause, Tempowechsel, Zeitsprung, Laden; kein Datenstrom pro Frame |
| Datenübergabe | **Versionszähler + Listen** für seltene Daten (Flughäfen, Routen, Flüge); Zeit pro Frame | wenig Aufwand, kein Pro-Frame-Abgleich, threadsicher bei Snapshot im Controller |
| Zeit | gehört dem **Controller**; die View besitzt nur eine Echtzeituhr für Funkeln und Wellen | eine Wahrheit |
| Beschriftungen | die **Spiel-UI** zeichnet sie an Ankern aus `TryGetScreenPoint` | gleiche Schrift und Lokalisierung wie der Rest der UI; View bleibt schlank. Nur im Demo-Overlay gibt es einfache Labels |
| UI-Technik | **uGUI** mit Overlay-Canvas für das Spiel-HUD (Vorschlag) | bewährt, Eingabe-Vorrang über `EventSystem.IsPointerOverGameObject` einfach; UI Toolkit später prüfbar |
| Konfiguration | `GlobeViewConfig` (ScriptableObject) mit **wenigen** Parametern nach außen, Aussehen als Look-Preset | der Controller setzt Grenzen, nicht Rendering-Details |
| Statische Zustände | **keine**; alles in Instanzen am Host | mehrfach instanziierbar, saubere Tests, Domain Reload an |
| Routenform und -höhe | berechnet die View (`RouteAltitudeScale` in der Config) | Linien und Flugzeuge garantiert deckungsgleich |
| Plattformen | Android und iOS gleichberechtigt, Messung auf beiden | siehe Phase 0 |

---

## 3. Phasen

Abhängigkeiten: 0 → 1 → 2 → (3 ‖ 4) → 5 → 6 → 7 → 8. Phase 3 und 4 sind unabhängig und können getauscht oder parallel laufen.

### Phase 0 – Absichern und messen (2–4 Tage, auch ohne Umbau wertvoll)
**Ausgangslage:** Das Projekt ist **nicht** unter Versionskontrolle. Es sind weder Android- noch iOS-Modul im Unity-Editor installiert, es gibt keine Android-SDK und nur die Xcode-Kommandozeilentools (kein volles Xcode).

1. `git init`, `.gitignore` (steht schon), erster Commit, Tag `prototype-1` (Rückfall). Texturen: Git LFS oder die Rohdaten außerhalb halten (Texturen 15–30 MB).
2. Über Unity Hub die Module **Android Build Support** (mit SDK/NDK/OpenJDK) und **iOS Build Support** nachinstallieren; für iOS **volles Xcode** installieren und ein Apple-Entwicklerkonto/Signing einrichten.
3. Entwicklungs-Builds des heutigen Stands auf mindestens je ein Gerät: Android Mittelklasse (ca. 4–6 GB RAM, Adreno 6xx/Mali-G5x/G6x), iPhone älter (z. B. XR/11) und neuer.
4. Messen (Unity Profiler, Frame Debugger, Xcode Instruments / Android Studio Profiler): fps pro Zoomstufe × Gerätestufe, GPU-Zeit, Speicher, Wärme nach 10 Minuten.
5. **Leistungsbudget** festlegen (Vorschlag: höchstens 28 ms Frame bei 30 fps, höchstens 400 MB Speicher für die View, thermische Rückstufung nach 5 Minuten Dauerlast).

*Fertig, wenn:* ein Messbericht vorliegt, die Schwellen der Gerätestufen (`GlobeQuality.Detect`) und die Zoomgrenzen angepasst sind und feststeht, ob Wellen/Lichtfeld/Schaum auf Medium tragbar sind. **Gate:** Ohne dieses Ergebnis nicht mit Phase 2 weitermachen.

### Phase 1 – Struktur und Verträge (2–3 Tage)
1. Ordner und `.asmdef` anlegen (Abschnitt 1.1); Dateien verschieben, **Verhalten unverändert**.
2. `Contracts` schreiben (Abschnitt 1.3) mit Strukturen, Schnittstellen, `GlobeViewConfig`, `CameraState`.
3. Erste Referenz: heutiges `AirportData` und `GlobeBootstrap.Routes` wandern aus der View nach `Demo` (als Datenquelle des Fake-Controllers).
4. EditMode-Tests für die Verträge (IDs eindeutig, Routen verweisen auf vorhandene Flughäfen, Fahrplan gültig: `ArriveUtc > DepartUtc`).

*Fertig, wenn:* das Projekt kompiliert, die Demo wie vorher aussieht (Screenshot-Vergleich mit dem Capture-Werkzeug, Stufen × Gerätestufen) und `GlobeView` keinen Verweis auf `Demo`, `Game` oder `UI` enthält (die Assembly-Grenzen prüfen das).

### Phase 2 – Host und Instanzen (2–3 Tage)
1. `GlobeViewHost` (MonoBehaviour am Prefab) mit `Initialize(config, model)`, `Pause`, `Resume`, `SetVisible`, `Dispose`.
2. `GlobeBootstrap` entfällt; `RuntimeInitializeOnLoadMethod` und alle `ResetStatics` entfernen. `SimClock` → `ViewClock` (nur Echtzeituhr fürs Funkeln, Spielzeit kommt aus `IGlobeModel.Time`). `GlobeLod.Stage` und `GlobeQuality.*` werden Instanzwerte (`LodController`, `QualityManager`).
3. `GlobeViewConfig` (ScriptableObject) mit den Parametern aus Abschnitt 5 von `VIEW-ARCHITEKTUR.md`, aber **reduziert**: Kamera, Qualität, Detailstufen, Kapazitäten, Look-Preset.
4. Demo-Szene: Prefab plus `FakeController` (zunächst nur Zeit und die bestehenden Flughäfen).

*Fertig, wenn:* Host lässt sich mehrfach erzeugen und zerstören (kein Speicherleck, kein Fehler bei ausgeschaltetem Domain Reload), die Demo läuft unverändert.

### Phase 3 – Datenebenen aus dem Modell (3–4 Tage)
1. `AirportLayer`: gepoolte Marker aus `AirportView`; `Importance` ersetzt die Hub-Liste; Tap-Ereignis (`AirportTapped(id)`); `TryGetScreenPoint`.
2. `RouteLayer`: Routen aus `RouteView` (Bogen, Farbe, Breite, Status); Hauptroute über `Importance` (Z0/Z1-Sichtbarkeit).
3. `FlightLayer`: Flüge aus `FlightView` (Fahrplan): Position = f(`Time.UtcSeconds`, `DepartUtc`, `ArriveUtc`, Route); gepoolt; Verspätung durch geänderten `ArriveUtc`; Flüge außerhalb von `[DepartUtc, ArriveUtc]` unsichtbar.
4. **Verkehrsfluss-Modus** für hohe Tempostufen (Abschnitt 1.4).
5. Auswahl und Hervorhebung aus `SelectionState`; gedimmte Darstellung der Nicht-Ausgewählten.
6. **Fake-Controller (Demo)** erzeugt: Flughäfen (heutige 32), Routen (heutige 20 plus Zufallsrouten), einen **Fahrplan** (pro Route tägliche Abflüge mit Zufallsversatz, Flugzeit nach Entfernung bei 850 km/h), ändert gelegentlich Fahrplanzeiten (Verspätung) und reagiert auf `AirportTapped` mit Auswahl. Dazu ein Stresstest-Schalter (z. B. 200 Flughäfen, 1000 Routen, 2000 Flüge).

*Fertig, wenn:* die Demo sieht aus wie vorher, aber alle Daten kommen über `IGlobeModel`; Pooling erzeugt im Betrieb keinen Speichermüll (Profiler); Stresstest hält das Leistungsbudget auf dem Mac (Handy erst in Phase 7).

### Phase 4 – Kamera und Eingabe (2 Tage)
1. `GlobeCamera` mit Konfiguration und Befehlen: `FocusOn`, `FocusAirport`, **`FrameRoute`**, Zoomgrenzen, Eingabe sperren, `SetViewportInsets`, `Get/SetCameraState`.
2. Eingabe über `IGlobeInput` (Input System); **UI zuerst**: bevor die View Drehen/Zoomen/Tippen verarbeitet, fragt sie `EventSystem` (ersetzt die statischen HUD-Rechtecke).
3. **Tippen mit Magnet** auf den nächsten Flughafen (Treffer im Radius von ca. 7 mm), bei Häufungen Auswahlliste; Tap-Toleranz in Millimetern (`Screen.dpi`).
4. Weiche Kamerafahrten (Ease), Trägheit konfigurierbar.

*Fertig, wenn:* Fake-Controller kann die Kamera steuern (z. B. beim Antippen einer Route `FrameRoute`), und ein Test-Button im Overlay blockiert Gesten, ohne dass der Globus sich mitdreht.

### Phase 5 – Qualität, Detailstufen, Tempo (2–3 Tage)
1. `QualityManager`/`LodController` über die Config (Zoomkanten, Hysterese, Gerätedeckel); thermisches Herunterschalten (Android ADPF, iOS `ProcessInfo.thermalState`).
2. Tempo-Regeln aus Abschnitt 1.4: Verkehrsfluss und Tag/Nacht-Dämpfung; Einstellung „Bewegung reduzieren“.
3. Start: erster Frame sofort (Basistexturen niedrig aufgelöst), Details laufen im Hintergrund nach; `Ready` und `LoadingProgress`.
4. Shader-Variantensammlung vorwärmen (kein Ruckeln beim ersten Stufenwechsel).

*Fertig, wenn:* Messung zeigt keine Ruckler beim Stufenwechsel, und bei 1 Tag/s blinkt der Globus nicht.

### Phase 6 – Demo-UI und Overlay (2 Tage)
1. IMGUI-HUD ersetzen durch einen uGUI-Overlay-Canvas im Demo-Projekt: Zeit-Buttons, Auswahlpanel, Qualitätsanzeige. Er spricht nur mit dem Fake-Controller und der Host-API (nie mit den Interna der View).
2. Beschriftungen als UI-Elemente an den Ankern aus `TryGetScreenPoint` (zeigt das Muster für das echte Spiel), mit einfacher Kollisionsvermeidung.
3. Debug-Overlay (Stufe, Gerätestufe, fps, Anzahl der Objekte).

*Fertig, wenn:* die Demo ohne IMGUI auskommt und zeigt, wie das Spiel-HUD später andockt (Safe Area, Panels mit `SetViewportInsets`).

### Phase 7 – Geräte-Härtung (3–5 Tage)
1. Android- und iOS-Builds mit ASTC; Texturimport pro Plattform; Speicherbudget; Prüfen der Shader auf Metal und Vulkan/GLES3.
2. Messungen aus Phase 0 wiederholen und mit dem Budget vergleichen; Schwellen nachziehen.
3. Safe Area, Hoch-/Querformat, Tablet; Barrierefreiheit (Farbpalette, „Bewegung reduzieren“, Textgröße).
4. Screenshot-Tests je Zoomstufe × Gerätestufe als Referenzbilder; Leistungstests im Build.

### Phase 8 – Echtes Spiel einbinden (offen)
1. `Tycoon.Domain` (reine C#): Zeit, Flughäfen, Linien, Flugzeuge, Wirtschaft, Fahrplanerzeugung.
2. `GameController` in `Tycoon.App`: implementiert `IGlobeModel` auf Basis der Domain (Fahrpläne direkt aus dem Domain-Zustand), nimmt Ereignisse der View entgegen und übersetzt sie in Befehle.
3. `Tycoon.UI` mit dem echten HUD; Fake-Controller und Demo bleiben als Entwicklungs- und Regressionswerkzeug im Projekt.
4. Speicherstände: Domain-Zustand plus `CameraState`.

---

## 4. Wie sich der heutige Code zuordnen lässt

| Heute | Neu | Phase |
|---|---|---|
| `GlobeBootstrap` | `GlobeViewHost` (Prefab) | 2 |
| `SimClock` | `ViewClock` (Echtzeituhr) + `GameTimeState` aus dem Modell | 2 |
| `SkyDriver`, `Astronomy`, `GeoMath` | `GlobeView/Runtime`, `SkyRenderer`; Zeit kommt vom Modell | 1–2 |
| `AirportData` (feste Liste) | Demo: `FakeController` liefert `AirportView` | 1/3 |
| `AirportLayer` | `AirportLayer` (Pooling, Tap, Anker) | 3 |
| `FlightLayer` | `RouteLayer` + `FlightLayer` (Fahrplan, Fluss-Modus) | 3 |
| `GlobeCameraController` | `GlobeCamera` + `IGlobeInput` | 4 |
| `GlobeQuality`, `GlobeLod` | `QualityManager`, `LodController` (Instanzen, Konfiguration) | 2/5 |
| `GlobeHud` (IMGUI) | Demo: uGUI-Overlay; Debug-Overlay | 6 |
| `SunFlare`, `MeshFactory`, Shader | unverändert in `GlobeView/Runtime` und `Shaders` | 1 |
| `Capture` (Editor) | bleibt im Editor-Teil, startet Host mit Fake-Controller | 1/2 |
| `tools/` | unverändert | – |

---

## 5. Tests

| Art | Wann | Inhalt |
|---|---|---|
| **EditMode (NUnit)** | ab Phase 1 | Verträge, Fahrplanrechnung (Position bei `t`), Astronomie, Geometrie, Kapazitätsgrenzen |
| **PlayMode** | ab Phase 3 | Fake-Controller füttert die View: Flüge erscheinen/verschwinden korrekt, Verspätung, Pooling ohne Allokation, Taps liefern die richtigen IDs |
| **Screenshot-Referenz** | ab Phase 1 | `Capture` rendert je Zoomstufe × Gerätestufe; Abweichung über Schwelle meldet Fehler |
| **Leistung im Gerät** | Phase 0, 7 | fps, GPU-Zeit, Speicher, Wärme |
| **Fehlersuche** | laufend | Konsole darf bei Zoomen, Stufenwechsel, Tempowechsel und Stresstest keine Fehler zeigen |

---

## 6. Risiken und Gegenmaßnahmen

| Risiko | Wahrscheinlichkeit/Wirkung | Gegenmaßnahme |
|---|---|---|
| Shader zu teuer für Mittelklasse-Handys | hoch, groß | Phase 0 zuerst messen; Qualitätsstufen und Zoomstufen sind schon da; für Low/Medium Wellen und Lichtfeld notfalls durch vorberechnete Texturen ersetzen |
| iOS-Pipeline (Xcode, Signing) kostet Zeit | mittel | früh in Phase 0 anstoßen |
| Fahrplanmodell passt nicht zu allen Spielmechaniken (Warteschleifen, Umleitungen) | mittel | zusätzlich einen freien Posen-Modus (`FlightPose`) vorsehen, wenn der Bedarf entsteht |
| Verträge zu früh eingefroren | mittel | versionieren, Phase 8 darf sie erweitern; Änderungen nur additiv |
| Texturen/Daten ohne Versionskontrolle verloren | hoch ohne Gegenmaßnahme | Phase 0 Schritt 1 (Git, ggf. LFS) |
| Hohe Tempostufen wirken unruhig oder blinken | mittel | Spezifikation 1.4, Tests mit Spielern, Einstellung „Bewegung reduzieren“ |
| Eingabe-Konflikte mit der UI | mittel | UI-zuerst-Regel (Phase 4), Test mit Buttons im Overlay |

---

## 7. Zeitrahmen und Reihenfolge

| Phase | Aufwand (Tage, geschätzt) |
|---|---|
| 0 Absichern und messen | 2–4 |
| 1 Struktur und Verträge | 2–3 |
| 2 Host und Instanzen | 2–3 |
| 3 Datenebenen | 3–4 |
| 4 Kamera und Eingabe | 2 |
| 5 Qualität, Detailstufen, Tempo | 2–3 |
| 6 Demo-UI und Overlay | 2 |
| 7 Geräte-Härtung | 3–5 |
| **Summe bis zur fertigen View mit Demo** | **18–26** |
| 8 Echtes Spiel | offen |

Parallel möglich: Phase 3 und 4; Teile von Phase 6 (Demo-UI) schon während Phase 3.

---

## 8. Noch offene Fragen

1. **Zielgeräte:** Welche Mindest-Android-Version und welches Mindest-iPhone? (bestimmt Qualitätsstufen und Tests)
2. **uGUI oder UI Toolkit** für das Spiel-HUD (Vorschlag uGUI).
3. **Größenordnung:** Wie viele Flughäfen, Routen und Flüge gleichzeitig? (bestimmt, ob GPU-Instancing und Verkehrsfluss früher nötig sind)
4. **Ebene Planungskarte:** Soll es neben dem Globus eine flache Karte für Planung und Übersicht geben? (Eine Entscheidung, die den Zuschnitt von View und Kamera beeinflusst; sinnvoll vor Phase 4 klären.)
5. **Look-Presets:** Reicht ein Standard-Look oder werden mehrere gebraucht (z. B. ruhig für Low-End)?

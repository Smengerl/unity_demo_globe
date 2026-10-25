# Migration plan: from globe prototype to a view inside the game

Status: 2026-10-25. **Phases 1–6 are done** (status and deviations below); the device part of phase 0, phase 7 and phase 8 are open. The architecture itself is described in [SOLUTION.md](SOLUTION.md), section 4; this file keeps the plan, the decisions and what is left.

**Constraints:** game logic and data model live in the **same app** and are written in **C#**. The current solution stays as a **demo** with a **fake controller** that feeds airports, routes and flights into the view. Effort figures are estimates for one person in working days, not measured.

---

## Status (2026-10-25)

Done locally on the Mac, without Android/iOS and without performance measurement.

| Phase | Status | Note |
|---|---|---|
| 0 Secure and measure | partly | git repository since 2026-10-25 (private on GitHub: `Smengerl/unity_demo_globe`); device measurement open |
| 1 Structure and contracts | done | assemblies `Tycoon.GlobeView.Contracts`, `.GlobeView`, `.GlobeView.Editor`, `.Hud`, `.Demo`, `.Demo.Editor`, `.Tests.EditMode`; prototype folders removed |
| 2 Host and instances | done | `GlobeViewHost` without singleton and without statics (except the shader globals), `Dispose`/`SetActive` |
| 3 Data layers | done | `GlobeData` with version counters, markers/routes/planes pooled, timetable, traffic flow from 6 h/s |
| 4 Camera and input | done | `GlobeCamera`: tap by screen distance, UI first, `FocusOn`/`FrameRoute`, viewport offset, zoom limits |
| 5 Quality, detail stages, speed | done (no mobile settings) | `DetailController`, calm factor in the shader, device tier as ceiling of the zoom stage, tile streaming |
| 6 Demo UI and overlay | done | HUD "Gate Chart" in uGUI (`Tycoon.Hud`), labels via `TryGetAirportScreenPoint`, stress test |
| 7 Device hardening | open | render scale/MSAA/HDR per tier, half texture size, ASTC, measurement |
| 8 The real game | open | `Tycoon.Domain`, `GameController`, real screens |

**Checks:** 44 EditMode tests green (geometry, astronomy, zoom stages, configuration, tiles, contracts, fake controller, `GlobeData`, the view interface end to end, sky/calm, traffic flow, HUD). Screenshots of all stages, lens flare, calm mode, traffic flow and the Low tier match the prototype. **Not checked:** devices.

**Deliberate deviations from the plan:**
- No look presets; the look is one setting without variants.
- No prefab and no Addressables: `GlobeViewHost.Create(config, model, camera)` builds everything in code; textures stay in `Resources`.
- Labels are drawn by the game UI (demo: `GateChartHud`), not by the view; the view only provides screen anchors.
- Slimmer contracts: `AirportView` without `Highlight` (the selection is in `SelectionState`), `SelectionState` with only `AirportId`/`RouteId`, only the event `AirportTapped` (no `RouteTapped`/`FlightTapped`), `ZoomStageChanged` carries only the new stage, `SetActive` instead of `Pause`/`Resume`/`SetVisible`. Tapping routes and planes is not implemented until the game needs it.
- The configuration is a JSON file (`globe-config.json`) shared with the tile script instead of a ScriptableObject.
- URP mobile settings (render scale, MSAA, HDR) and `globalTextureMipmapLimit` are postponed to phase 7; `GlobeViewConfig.TargetFrameRate` exists.
- Also removed: template leftovers (tutorial info, readme, sample scene, `BillingMode.json`), nine unneeded packages, unused shader functions and helpers.

---

## 1. Target picture

### 1.1 Project layout (one Unity project, boundaries via assembly definitions)

```
Assets/
  GlobeView/     Contracts (pure C#), Runtime (rendering, camera, layers, detail, tiles), Editor
  Hud/           Tycoon.Hud – the HUD
  Game/          (phase 8) Domain (pure C#), App (GameController), UI
  Demo/          Tycoon.Demo – fake controller, demo scene, developer panel
  Tests/         EditMode
tools/ docs/
```

### 1.2 Dependencies (arrow = "knows")

```
UI ──► App ──► Domain
 │      │
 │      └────► GlobeView.Contracts ◄──── GlobeView (runtime)
 └──────────────────────────────────────►  GlobeView (host API: commands, anchors)

Demo ──► GlobeView.Contracts, GlobeView, Hud        Demo knows neither Domain nor App
```

- `GlobeView` knows neither `Domain` nor `App` nor `UI`; the assembly boundaries enforce this.
- `Domain` knows no Unity types, so logic is testable without the editor.
- The **fake controller** implements the same interface as the later `GameController`. Swapping is a one-line change in the host set-up.

### 1.3 Contracts

`IGlobeModel` (time every frame; airports, routes, flights each with a version counter), the structs `GameTimeState`, `AirportView`, `RouteView`, `FlightView` (a **timetable**: `DepartUtc`, `ArriveUtc`, `Backwards`, class, colour), `SelectionState`, and the view's `IGlobeView` with events and commands. The exact current definitions are in `Assets/GlobeView/Contracts` and summarised in SOLUTION.md section 4.

### 1.4 Behaviour at high speed

| Speed | Planes | Day/night |
|---|---|---|
| up to 1 h/s | single aircraft | normal |
| from 6 h/s | **traffic flow** instead of single aircraft | change stays visible |
| 1 day/s | traffic flow | calm light plus fast-forward look, no hard flicker |

The thresholds are starting values and should be tuned with testers (light-sensitive players!). A "reduce motion" setting exists.

---

## 2. Decisions (with reasons)

| Topic | Decision | Reason |
|---|---|---|
| Game architecture | one Unity project, logic as a pure C# assembly | simplest way with the same app and C#; hard separation via assemblies |
| Flight data | **timetable** (`DepartUtc`, `ArriveUtc`), the view computes the position | works with pause, speed changes, time jumps, loading; no per-frame data stream |
| Data hand-over | **version counters + lists** for rarely changing data; time every frame | little effort, no per-frame comparison |
| Time | owned by the **controller**; the view only has a real-time clock for twinkling and waves | one truth |
| Labels | the **game UI** draws them at anchors from `TryGetAirportScreenPoint` | same font and localisation as the rest of the UI; slim view |
| UI technology | **uGUI** with an overlay canvas | proven; input priority via `EventSystem` is easy |
| Configuration | one JSON file shared by view and tile script | the controller sets limits, not rendering details |
| Static state | **none**; everything in instances on the host | several instances possible, clean tests |
| Route shape and height | computed by the view | lines and planes always coincide |
| Platforms | Android and iOS equal, measure on both | see phase 0 |

---

## 3. Phases

Dependencies: 0 → 1 → 2 → (3 ‖ 4) → 5 → 6 → 7 → 8.

### Phase 0 – Secure and measure (2–4 days)
1. Git repository, `.gitignore`, first commit (done).
2. Install the **Android Build Support** (with SDK/NDK/OpenJDK) and **iOS Build Support** modules via Unity Hub; for iOS full **Xcode** and signing.
3. Development builds on at least one mid-range Android (4–6 GB RAM, Adreno 6xx/Mali-G5x/G6x) and an older and a newer iPhone.
4. Measure (Unity Profiler, Frame Debugger, Xcode Instruments / Android Studio Profiler): fps per zoom stage × tier, GPU time, memory, heat after 10 minutes.
5. Set a **performance budget** (proposal: at most 28 ms per frame at 30 fps, at most 400 MB memory for the view, thermal step-down after 5 minutes of sustained load).

*Done when:* a measurement report exists, the thresholds of the device tiers (`DetailController.Detect`) and the zoom limits are adjusted, and it is clear whether waves / light dots / surf are affordable on Medium.

### Phases 1–6 – done
Structure and contracts; host and instances; data layers from the model; camera and input; quality, detail stages and speed rules; HUD and overlay. Details of the result: SOLUTION.md.

### Phase 7 – Device hardening (3–5 days)
1. Android and iOS builds with ASTC; texture import per platform; memory budget; check the shaders on Metal and Vulkan/GLES3.
2. Repeat the phase 0 measurements against the budget; adjust thresholds.
3. Safe area, portrait/landscape, tablet; accessibility (palette, "reduce motion", text size).
4. Screenshot tests per zoom stage × tier as reference images; performance tests in the build.
5. Ship the tile pyramid with the build (StreamingAssets copy or server, `Tiles.BaseUrl`).

### Phase 8 – Bring in the real game (open)
1. `Tycoon.Domain` (pure C#): time, airports, routes, aircraft, economy, timetable generation.
2. `GameController` in `Tycoon.App`: implements `IGlobeModel` and `IHudModel` on top of the domain, takes view events and turns them into commands.
3. Real HUD screens; fake controller and demo stay as a development and regression tool.
4. Save games: domain state plus `CameraState`.

---

## 4. Tests

| Kind | Content |
|---|---|
| **EditMode (NUnit)** | contracts, timetable maths, astronomy, geometry, capacity limits, configuration, tiles, HUD |
| **Screenshot reference** | `Capture` renders per zoom stage × tier; a deviation over a threshold reports an error |
| **Device performance** | phases 0 and 7: fps, GPU time, memory, heat |
| **Troubleshooting** | the console must stay free of errors when zooming, changing stage, changing speed and in the stress test |

---

## 5. Risks and countermeasures

| Risk | Likelihood/impact | Countermeasure |
|---|---|---|
| Shaders too expensive for mid-range phones | high, large | measure first (phase 0); tiers and zoom stages exist; for Low/Medium replace waves and light dots by precomputed textures if needed |
| iOS pipeline (Xcode, signing) costs time | medium | start early in phase 0 |
| Timetable model does not fit every game mechanic (holding patterns, diversions) | medium | add a free pose mode (`FlightPose`) when the need arises |
| Contracts frozen too early | medium | version them, phase 8 may extend them; additive changes only |
| High speeds look restless or flicker | medium | calm rule, tests with players, "reduce motion" |
| Input conflicts with the UI | medium | UI-first rule, test with buttons in the overlay |

---

## 6. Open questions

1. **Target devices:** minimum Android version and minimum iPhone? (decides tiers and tests)
2. **Scale:** how many airports, routes and flights at once? (decides whether GPU instancing is needed earlier)
3. **Flat planning map** next to the globe? (a decision that shapes view and camera)
4. **Look presets:** is one standard look enough, or are several needed (e.g. calm for low-end)?

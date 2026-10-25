# unity_demo_globe – how it works

Status: 2026-10-25. This document explains the whole solution so that it can be **reproduced or taken over into the real game** without knowing how it came about: goal, decisions, architecture, formulas, data pipeline, pitfalls, constants.

The [README](../README.md) is the short version, [tools/README.md](../tools/README.md) describes the data pipeline, [hud-design/HUD-DESIGN.md](hud-design/HUD-DESIGN.md) the HUD, [MIGRATION-PLAN.md](MIGRATION-PLAN.md) how the prototype became a reusable view.

---

## 1. Goal and result

Game idea: an airport tycoon for phones. The player runs airports, buys airline routes between them and lets planes fly on them; the game time can be made faster and slower. In the centre of the screen there is always a 3D globe (swipe = turn, pinch = zoom) that should look as good as possible.

Requirements the solution meets:

| Requirement | Implementation |
|---|---|
| Globe always centred, swipe turns, pinch/scroll zooms | orbit camera around the origin (`GlobeCamera`) |
| Zoom down to detail, airports selectable there | `MinHeight = 0.12` Earth radii above the surface; detail from the tile pyramid (section 7b); markers picked by screen distance |
| Space, moon and sun on realistic orbits | `Astronomy.cs` (Meeus approximation), `SkyRenderer`, star skybox |
| Realistic day/night, cities glow at night | Earth shader: terminator with twilight, Black Marble texture |
| Realistic water with structure, mirrors the sun | wave normal map, facet shading, sun glint |
| Land shows the position of the sun | relief from SRTM elevation data as a normal map |
| Airports marked, planes move on routes | `AirportLayer`, `RouteLayer`, `FlightLayer` (great-circle arcs with height) |
| Runs on ordinary Android phones later | quality tiers Low/Medium/High (`DetailController`); mobile settings follow with the port |

---

## 2. Technology decision

**Unity 6 (URP)**, not Unreal or Godot. Reason: the game is mostly interface and simulation logic, only the globe needs graphics effort. Unreal is too heavy for that (an empty app is around 100 MB, Lumen/Nanite are not usable on mobile, UMG is tedious for management UI, weak devices drop out). Unity has the most mature mobile ecosystem (IAP, ads, cloud save), a good UI system and small builds. Godot would be the lean alternative but has thinner mobile SDKs.

Everything visual about the globe is **custom shaders on a sphere**, no engine-specific features. That is engine-independent and portable.

---

## 3. Project setup (reproducible)

- Unity Hub, editor **6000.6.7f1**, template **URP Blank**.
- Packages (`Packages/manifest.json`): `com.unity.render-pipelines.universal 17.6.0`, `com.unity.inputsystem`, `com.unity.ugui` (HUD), `com.unity.test-framework`, IDE packages. Template packages that are not needed (navigation, analytics, purchasing, timeline, XR, 2D, multiplayer centre, collab) were removed. **Active Input Handling = Input System (new)**; the old `UnityEngine.Input` cannot be used.
- Layout (details in section 4): `Assets/GlobeView` (the view, with its own assemblies), `Assets/Hud` (the HUD), `Assets/Demo` (fake controller, demo scene), `Assets/Tests/EditMode`, `tools/` (data pipeline), `docs/`.
- **Start the demo:** open the scene `Assets/Demo/Scenes/GlobeDemo` and press Play (if the scene is missing: menu *Tycoon → Create Demo Scene*).
- **Important:** shaders and textures live under `Assets/GlobeView/Resources/` so that player builds contain them (loaded with `Resources.Load`, not `Shader.Find`).
- Tests: *Window → General → Test Runner → EditMode*; or in batch mode `Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults out.xml` (the editor must not have the project open).

---

## 4. Architecture

The globe is a **view**: it shows what the game's controller delivers through `IGlobeModel`, reports input through events and takes commands through `IGlobeView`. Game logic and data model will live separately (C# assembly `Tycoon.Domain`, not created yet).

```
Assembly                      Content                                          Knows
Tycoon.GlobeView.Contracts    model types, IGlobeModel, IGlobeView, config     nothing (Unity types only)
Tycoon.GlobeView              rendering, camera, layers, detail control        Contracts, URP, Input System, uGUI
Tycoon.GlobeView.Editor       texture import                                   GlobeView
Tycoon.Hud                    HUD "Gate Chart" (uGUI in code), IHudModel       Contracts, uGUI, Input System
Tycoon.Demo                   FakeController, DemoMain, DemoDevPanel           Contracts, GlobeView, Hud
Tycoon.Demo.Editor            Capture (screenshots, movies), scene builder     Demo, GlobeView, Hud
Tycoon.Tests.EditMode         44 tests                                         everything
(later) Tycoon.Domain / App   game logic, controller                           see migration plan
```

The view knows neither game nor demo; the assembly boundaries enforce that. The demo's **fake controller** implements `IGlobeModel` exactly as the real `GameController` will.

### Flow

```
GlobeViewHost.Create(config, model, camera)         // one GameObject, no singleton, no statics
  Initialize: sky, Earth, atmosphere, sun/moon, layers, camera, lens flare, detail control
  LateUpdate -> TickCore(dt):
    GlobeData.Refresh(model)        copies the lists only when a version changed, derives directions/arcs/route index
    GlobeCamera.Tick                input, camera flight, pose
    RewindEffect.Tick               fast-forward look at the highest speed (shakes the camera a little)
    DetailController.Tick           zoom stage, keywords, tile window (TileWindow), shader warm-up
    SkyRenderer.Tick                sun/moon/stars from game time, twinkle clock, calm factor
    AirportLayer / RouteLayer / FlightLayer.Tick
    SunFlare.Tick
    events (CameraChanged, ZoomStageChanged, Ready)
```

### Files

| File (`Assets/GlobeView/...`) | Purpose |
|---|---|
| `Contracts/ModelTypes.cs` | `GameTimeState`, `AirportView`, `RouteView`, `FlightView` (timetable), `SelectionState`, `CameraState` |
| `Contracts/IGlobeModel.cs` | what the controller delivers: time, airports, routes, flights (each with a version counter), selection |
| `Contracts/IGlobeView.cs` | events, commands and queries of the view; `QualityTier` |
| `Contracts/GlobeViewConfig.cs` | the configuration classes and the reader of `globe-config.json` |
| `Runtime/GlobeViewHost.cs` | entry point: set-up, tick, commands, tap handling, clean-up |
| `Runtime/GlobeData.cs` | the copy of the model data and what is derived from it |
| `Runtime/GlobeCamera.cs` | orbit camera, input, camera flights, `FrameDirections`, viewport offset |
| `Runtime/AirportLayer.cs`, `RouteLayer.cs`, `FlightLayer.cs` | markers, lines, planes (pooled) |
| `Runtime/SkyRenderer.cs`, `Astronomy.cs` | sun/moon/stars, twinkle clock, calm factor |
| `Runtime/DetailController.cs`, `TileWindow.cs`, `TileSource.cs`, `TileMath.cs`, `ShaderWarmup.cs` | device tier, zoom stages (from the config), keywords, tile streaming, shader variant warm-up |
| `Runtime/SunFlare.cs`, `RewindEffect.cs` | lens flare of the sun, fast-forward look |
| `Runtime/GeoMath.cs`, `RouteMath.cs`, `ZoomStages.cs`, `MeshFactory.cs` | pure helper functions |
| `Resources/Shaders/*.shader`, `TycoonCommon.hlsl` | Earth, Atmosphere, Stars, Body, Flat, Flare, shared functions/globals |
| `Resources/Earth/*`, `Resources/globe-config.json` | global base textures; the configuration shared by view and script |
| `Editor/EarthTextureImporter.cs` | import settings for `Resources/Earth` |
| `Demo/FakeController.cs` | game time, airports, routes, timetable, delays, selection, stress test, cash |
| `Demo/DemoMain.cs`, `DemoDevPanel.cs` | demo entry point and developer tools (F1: quality, stress test, camera info) |
| `Hud/GateChartHud.cs` and others | the HUD (time, cash, speed, selection, labels), see `docs/hud-design/HUD-DESIGN.md` |
| `Demo/Editor/Capture.cs` | screenshots and movie frames without play mode (section 13) |
| `Demo/Editor/DemoSceneBuilder.cs`, `PlayModeGuard.cs` | create the demo scene; stop play mode when scripts are recompiled during play |

### Basic principle of the scene

The Earth never turns. It stands still at the origin. The *camera* circles around it and the *sun* (direction) moves with the game time. All positions (airports, routes) therefore stay static, and day/night is just a shader parameter.

**Shader globals** (Earth-fixed frame): `_TycoonSunDir`, `_TycoonMoonDir`, `_TycoonStarRot`, `_TycoonTime` (twinkle clock, constant real-time speed), `_TycoonCalm` (0–1, see 12b). Set by `SkyRenderer` every frame and pre-set in its constructor so that the first frame does not see a zero vector. They are global: **one active host at a time.**

### The contract in brief

- **Model → view** (`IGlobeModel`): `Time` (`UtcSeconds` as Unix seconds, `Rate` = game seconds per real second), `Airports`, `Routes`, `Flights`, `Selection`. The lists are only read again when their version counter changes.
- **Flights are a timetable** (`DepartUtc`, `ArriveUtc`, direction, class, colour): the view computes the position from the game time. Pause, speed changes, time jumps and loading a save game need no special handling; a delay = changed times, raise `FlightsVersion`.
- **View → game** (`IGlobeView` events): `AirportTapped(id)`, `EmptyTapped(lat, lon)`, `CameraChanged`, `ZoomStageChanged`, `Ready`.
- **Game → view** (commands): `FocusOn`, `FocusAirport`, `FrameRoute`, `SetZoomLimits`, `SetInputEnabled`, `SetViewportInsets` (panels), `SetQualityTier`, `SetActive`, `SetPointerBlocker` (UI first), `Get/SetCameraState`.
- **Query:** `TryGetAirportScreenPoint(id)` returns the screen position and visibility; the game UI draws labels itself at these anchors (demo: `GateChartHud`).
- Not implemented because not needed yet: tapping routes and planes, free flight poses, GPU instancing, labels drawn by the view.

---

## 5. Coordinate and time conventions

**Earth-fixed frame** (`GeoMath.Dir`): unit sphere at the origin, +Y = north pole, longitude 0 / latitude 0 lies at **−Z**, east = **+X**. Seen from outside (camera at −Z looking towards +Z) east is on the right.

```
Dir(lat, lon) = ( cos(lat)·sin(lon),  sin(lat),  −cos(lat)·cos(lon) )
```

Shaders and C# use the same mapping. `MeshFactory.Sphere` creates vertices in exactly this order and sets UVs:

- `u = k/segments + 0.5` (runs past 1, stays continuous, textures wrap), `v = 1 − r/rings`.
- The Earth shader reads longitude/latitude **from the mesh UV** (`frac(u)*360−180`, `(v−0.5)*180`). An earlier version computed `atan2/asin` per pixel; that was more expensive and forced special handling of the date line for mip derivatives.

**Textures** are equirectangular (u = longitude from −180° to 180°, v = latitude from −90° to 90°, north at the top).

**Local compass directions** at a point N (for relief and waves): `E = normalize(−N.z, 0, N.x)` (east), `Nn = cross(E, N)` (north). Near the poles `length` is clamped.

**Time:** belongs to the controller (`GameTimeState`: `UtcSeconds`, `Rate`). The fake controller starts with the real UTC time so that the terminator matches reality and takes its speed steps from the HUD (pause, 10 min/s, 1 h/s, 6 h/s, 1 day/s, see `docs/hud-design`); it runs on `Time.unscaledDeltaTime`, independent of `Time.timeScale`. The view has no game time of its own, only a real-time clock for the twinkling.

---

## 6. Astronomy (`Astronomy.cs`)

Low-precision approximation (sun ~0.3°, moon ~1°, after Meeus), enough for looks, not for navigation.

1. Days since J2000: `d = unixSeconds/86400 − 10957.5` (2000-01-01 12:00 UTC = Unix second 946728000).
2. Obliquity `ε = 23.4393° − 3.563e−7·d`.
3. Greenwich sidereal time `GMST = 280.46061837 + 360.98564736629·d` (mod 360).
4. Sun: mean longitude `L = 280.460 + 0.9856474·d`, anomaly `g = 357.528 + 0.9856003·d`, ecliptic longitude `λ = L + 1.915·sin g + 0.020·sin 2g`, latitude 0 → right ascension/declination via `EclipticToEquatorial`.
5. Moon: `Lm = 218.316 + 13.176396·d`, `M = 134.963 + 13.064993·d`, `F = 93.272 + 13.229350·d`, `λ = Lm + 6.289·sin M`, `β = 5.128·sin F`.
6. Into the Earth-fixed frame: direction = `Dir(dec, ra − GMST)`. The subsolar point is at latitude = declination, longitude = RA − GMST.
7. Stars are fixed in space: the skybox is turned about the Y axis by `GMST` (`Matrix4x4.Rotate(Quaternion.AngleAxis(gmst, up))`, shader global `_TycoonStarRot`).

Plausibility check (by script): elongation on 2026-10-10 16:00 UTC ≈ 4° (new moon), on 2026-10-26 04:00 UTC ≈ 175° (full moon ~04:12 UTC, 5° off). Declination of the sun at the end of October ≈ −12°.

Scene scale (Earth radius = 1): moon at **60.3** with radius **0.2727** (to scale), sun at 150 with radius 0.7 (not to scale but the right size in the sky). Camera far plane 400.

---

## 7. Data pipeline of the textures

Details and commands: `tools/README.md`. In short:

| Texture | Source | Processing | File |
|---|---|---|---|
| Day | NASA Blue Marble Next Generation (July 2004, topo+bathy), 21600×10800 | scaled to 8192×4096 | `Resources/Earth/day.jpg` |
| Night | NASA Black Marble 2016 colour, 13500×6750 | to 8192×4096 | `night.jpg` |
| Relief, global | AWS Terrain Tiles (Terrarium) z=4, 256 tiles | Mercator → equirectangular 4096×2048, slope → normal map | `normal.png` |
| Waves | self-made (FFT, 1/f^1.4, seamless) | 256×256 | `waves.png` |
| Deep-ocean mask | AWS Terrain Tiles z=4 (height below −200 m) | 8192×4096, water = white | `watermask.png` (adds to the colour-based water detection, see 8) |
| Coast distance | from `day.jpg` (water rule as in the shader, plus the deep-ocean mask) | grey value = distance from land over water, 0–8 px (≈39 km) | `coast.png` 8192×4096 |

Terrarium height: `h = R·256 + G + B/256 − 32768` metres. The ocean is clamped to 0 (flat). Normal map encoding: `rgb = n·0.5+0.5`, **x = east, y = north, z = up** (tangent space of the local frame, not the UV space of a mesh). Slopes are exaggerated 8 times (`K = 8`) because the real relief looks too flat.

**Import settings** (`EarthTextureImporter`, only path `/Resources/Earth/`): max 8192, mipmaps, trilinear, aniso 8, `CompressedHQ`, **sRGB off** for `watermask`, `normal`, `waves`, `coast`; `waves` repeat, otherwise U repeat / V clamp. Raise `GetVersion()` to force a re-import.

Memory (rough): day+night 8192×4096 each and three normal-type maps (the base, in the repository) – about 50 MB with ASTC on Android; plus the tile window (section 7b, 0 to about 200 MB depending on the tier).

Water is estimated from the day colour (sharp coasts); the deep-ocean mask adds mid-ocean ridges and seamounts that look pale in the Blue Marble colours and would otherwise count as land. Not present: clouds.

---

## 7b. Configuration and tile pyramid

**One configuration file** (`Assets/GlobeView/Resources/globe-config.json`) replaces the earlier ScriptableObject class. View and script read the same file. `GlobeViewConfig` is only the reader now (`LoadDefault()`, `FromJson()`); a missing field keeps its default, enums are written as names (`"Tier": "High"`, `"Features": "Relief, Waves"`).

| Section | Content |
|---|---|
| camera | start, zoom limits (`MinHeight`/`MaxHeight`), field of view, swiping, tapping |
| `StageEdges`, `Stages[5]` | heights where the stages change; per stage: name, `TileLevel` (−1 = base only), relief, wave layers, surf, light grids, night/day detail, flares |
| `Tiers[3]` | Low, Medium, High: `StageCap`, allowed `Features`, `WindowTiles` (tiles per side of the window), `LevelOffset` |
| `Tiles` | `TileSize`, `BaseUrl`, `TilesPerFrame`, `CoastKm`, `Layers` (day, night, relief, coast with format and level range) |
| sky, motion | sun/moon, speed limits for traffic flow, calm lighting and the fast-forward look |

**Pyramid:** plate carrée, level L has 2^(L+1) × 2^L tiles of `TileSize` pixels, tile (0, 0) at the north-west, files `EarthTiles/<layer>/<level>/<x>_<y>.jpg|png`. Level 4 is about 2.4 km per pixel, level 5 1.2 km, level 6 0.6 km. The tiles are not in the repository (`.gitignore`).

**Script:** `python3 tools/build_tiles.py` reads the config, counts the tiles beforehand (`--estimate`) and only makes the tiles the raw data can provide (source fine enough and the whole tile covered); `--layers`, `--levels`, `--workers` restrict it. With the raw data of `get_data.sh` about 141 MB result: day and night level 4 worldwide, levels 5 and 6 in the quarter of the Earth 0–90°E, 0–90°N; relief only over Central Europe; coast tiles only at coasts. Missing tiles are allowed, the view then takes the next coarser one.

**Runtime** (`TileWindow`): from the stage (`Stages[...].TileLevel` plus the tier's `LevelOffset`) and the camera position a block of `WindowTiles` × `WindowTiles` tiles around the camera results. It is assembled in the background into **one texture per layer**, which the Earth shader lays over the base inside the block's rectangle. If a tile is missing the next coarser one is stretched in; where nothing exists the pixel stays transparent (alpha 0). A new block is built next to the old one and replaces it only when complete (or after 8 s); it is rebuilt when the camera leaves the middle half of the block or the stage changes. Tiles are read by a background thread (folder) or `UnityWebRequest` (address, also `jar:` on Android), decoded on the main thread (`TilesPerFrame`).

**Memory:** per layer `WindowTiles × TileSize` pixels square (6 tiles = 3072 px, about 50 MB with mips, four layers on High about 200 MB, briefly twice that during a swap). Low loads nothing, Medium only the night map with 4 tiles.

**Still open:** the tiles are not yet part of a player build (copy the folder to `StreamingAssets/earth-tiles` or point `Tiles.BaseUrl` at a server); the raw data for more areas (more NASA tiles, finer terrain levels) has to be downloaded; the window always shows only one area.

---

## 8. Earth shader (`Earth.shader`)

One pass (URP, `LightMode = UniversalForward`), unlit, own lighting. Variables: `N` normal (= direction from the centre), `V` to the camera, `L` to the sun, `ndl = dot(N, L)`.

All ramps that depend on the sun use `TySoft(a, b, x)` (`TycoonCommon.hlsl`): a quintic ramp from 0 at `a` to 1 at `b` with zero slope and zero curvature at both ends. Cubic `smoothstep` and linear `saturate` ramps show a faint edge where they start or end (Mach bands); the quintic one does not.

**Steps per pixel**

1. **UV** from the mesh, derivatives `ddx/ddy` for `SAMPLE_TEXTURE2D_GRAD` (needed because sampling happens with manual UV and branches).
2. **Day colour** `_DayTex`. With `TY_DETAIL`: the tile window `_DetailTex` inside the rectangle `_DetailRect` (longitude/latitude in degrees), faded out softly at the edge (`smoothstep(0, 0.04, …)`) and **weighted by alpha** (alpha 0 = no tile here, the base shows through); derivatives are converted with `360/span` and `180/span`.
3. **Relief** (`TY_RELIEF`): tangent normal `tn` from `_NormalTex` (and `_DetailNormalTex` in the tile window) → `Nterrain = normalize(E·tn.x + Nn·tn.y + N·tn.z)`; `ndlTerrain = lerp(ndl, dot(Nterrain, L), _Relief)`.
4. **Water:** `water = max(colour guess, deep-ocean mask)`; guess `smoothstep(1.08, 1.45, b/(max(r,g)+0.003))` (ratio instead of difference so that very dark deep sea counts; ice/white ≈ 1 does not), mask `watermask.png` (height < −200 m; −200 instead of 0 so that basins below sea level such as Death Valley do not count as water). Without the mask ridges appeared as grey "islands" in the ocean.
5. **Day side:** `lit = TySoft(−0.35, 1.1, ndlTerrain)` (soft fall-off instead of a hard Lambert edge), `col = day · (0.012 + lit·1.35) · warm`; `warm` tints orange near the terminator (`TySoft(−0.05, 0.55, ndl)`).
6. **Twilight:** `dusk = TySoft(−0.50, −0.02, ndl) · (1 − TySoft(0.02, 0.42, ndl))` – sun between about +25° and −30° around the horizon, widest where it is weakest. Colour from orange at the terminator to blue further into the night (`TySoft(−0.40, 0.12, ndl)`); `col += (day·0.6 + 0.04) · tint · dusk · 0.5`.
6b. **Night side, the day map stays visible:** in dark night the day map including relief is added faintly in bluish moonlight: `col += day · (0.55 0.72 1) · (0.04 + 0.09·moonLit) · _NightDay · nightMask`, `moonLit = smoothstep(−0.2, 1.1, dot(Nsurf, moon direction))` (shader global `_TycoonMoonDir`, `Nsurf` = normal with relief). Coasts, mountains (Alps with snow) and land/sea stay readable at night; the pixelated night map is only a share. `_NightDay` (0–3, default 0.7) sets the brightness.
7. **City lights:** `nightMask = 1 − TySoft(−0.42, 0.16, ndl)` (from sunset, full at nautical twilight), `col += night · (1, 0.78, 0.5) · _CityGain · nightMask`.
   - The night map is read with **quintic texel interpolation** instead of bilinear (`_NightTex_TexelSize`) so that magnified city lights look rounder. In the tile window (`TY_NIGHTDETAIL`) the sharp night map from the tiles is blended in as well (`_UseDetailNight`, same edge and alpha as the day detail).
   - **Twinkling light dots** (`TY_LIGHTS`, Medium/High): when zooming in the night map turns into a **field of single, independently twinkling lights**, sharp everywhere on Earth (the dots are made in the shader). **Lights per map point:** the function `UvLights` lays a grid of square cells into UV space (rows of equal height, cells per row ≈ `2·cellsV·cos(lat)` so that the cells stay square at high latitudes). Every cell has at most one dot (soft round glow, radius 0.7–1.05 cells, `exp(−r²/size²·3)`); whether it lights up is decided by the night map **at the position of the cell** (`NightLumAt`: global map, in the tile window the sharp map): `p = saturate(smoothstep(0.06, 0.80, sqrt(luminance))·_LightDensity)` (the map is linear, `sqrt` lifts dark suburbs), a random number per cell (`TyHash`) against `p`. Bright map pixels give many lit cells, dark ones almost none; the map is rebuilt as a field of lights, its pixels are no longer visible. **Three grids** with `cellsV` = 4096 (1 map pixel per cell, ≈ 4.9 km), 8192 (≈ 2.4 km) and 16384 (≈ 1.2 km); the finest grid whose cells are at least about 2–3 pixels takes over from the coarser ones (`fade = saturate((px − 1.0)/1.2)`, `w4 = f4`, `w2 = f2·(1 − f4)`, `w1 = f1·(1 − f2)`). **Important:** `px` is computed from `psPix = max(length(ddx(N)), length(ddy(N)))`, not from `fwidth` – `length(fwidth(N))` overestimates the pixel size by about 1.5 times and makes the grids fade out too early. **Twinkling:** two sine parts per dot with a random rate (1.5–6 rad/s) and phase, factor about 0.08–1, plus short bright flashes (`1.6·pow(max(0, sin(…)), 14)`); clock `_TycoonTime` (= the `SkyRenderer`'s real-time clock, constant speed `TwinkleSpeed = 0.45`, independent of game time, pause and camera; `Capture -time` freezes it). Every pixel adds the glow of the **3×3 cells** around it (`CellLight`) so that the lights blur into one another instead of looking like a regular dot grid; considering the cells in all directions prevents cut-off (square) edges. Brightness of the grids 0.22 / 0.3 / 0.4 (fine / medium / coarse), times `_CityGain·0.5·_Sparkle`; more overexposes the overlapping glows into white areas. **The night map only lies below with `_NightMapAlpha` (default 0.12)** where dots are active (`glowK = lerp(1, _NightMapAlpha, saturate(f4 + f2 + f1)·saturate(_Sparkle))`). Density is set by `_LightDensity` (0.5–8, default 3.5). Far away (cells < about 1.6 pixels) the usual glow of the map remains. `_Sparkle` (0–2; 0 = twinkling off).
   - **Limit:** where night tiles exist (the tile window) the night map is sharp; elsewhere the lights sit on the grid of the global map (4.9 km per pixel, 9.8 km on Low/Medium) and are shown as a field of dots by the fine grids. How far sharp tiles reach is determined by the raw data and `Tiles.Layers` in the config (section 7b).
   - **After midnight the lights go out one after another:** local time via the hour angle (`hourAngle` = longitude − longitude of the subsolar point, normalised to [−π, π); positive = afternoon/evening, negative = morning). On the morning side `morning = TySoft(ndlMidnight, 0.2, ndl)` grows from 0 (midnight) to 1 (end of morning twilight, sun about 11° above the horizon); `ndlMidnight = L.y·N.y − √(1−L.y²)·√(1−N.y²)` is the height of the sun at midnight for this latitude. The dots use `lightMask = 1 − morning` with a fixed threshold per dot (see below), the glow of the night map is dimmed with `(1 − morning)`. Measured (Central Europe, zoom 0.3): share of bright pixels evening 3.9 %, 03:00 UTC 2.3 %, shortly before dawn 1.0 %.
   - **Twilight:** the dots need longer than the map to go out (`lightMask = 1 − TySoft(−0.42, 0.26, ndl)`), and **every dot has its own hard threshold** (`on *= step(h2, lightMask)` with a random number `h2` per cell): if `lightMask` is above it the dot is fully on, below it is fully off – no uniform dimming down. The higher the sun climbs (or after midnight) the fewer lights are on; they go out one after another in the morning, come on one after another in the evening.
8. **Waves** (`TY_WAVES`, water only): three layers of the same `waves.png` with **unrelated scales and angles** (tile counts 80/210/520 in longitude, angles 0°/37°/71°, opposing drift), plus large calm/rough patches from a very large pass of the same texture (`calmRough`). The normal is tilted: `Nw = normalize(E·g.x + Nn·g.y + N)`. Facets: `facet = 1 + 5·(dot(Nw,L)−ndl) + 2·(dot(Nw,V)−dot(N,V))`, clamped 0.45–1.7, multiplied onto the water colour. Fade with distance `saturate(1 − length(fwidth(N))·220)`; mip averaging smooths further.
9. **Sun glint on land and surf at the coast:**
   - **Land reflection:** `landSpec = pow(dot(Nsurf, H), 24) + 0.25·pow(dot(Nsurf, H), 6)` with the relief normal, factor `(0.3 + 1.2·smoothstep(0.12, 0.55, brightness of the day colour))` (bright ground such as sand/snow reflects more than forest), day side only. Wide and weak, follows the relief. Control `_LandGlint` (0–3, default 0.8; more bleaches deserts).
   - **Surf and foam** (`TY_FOAM`, Medium/High, water only, needs the coast maps) – **as a constantly changing ripple pattern that gets stronger towards the coast** (no line along the coast, no regular motion away from land): the coast distance `coastKm` comes from the coast maps (global/tile window). `wCoast = pow(1 − smoothstep(0, 26 km, coastKm), 1.4)`. The pattern `ripple` consists of three noise layers drifting in different directions (like the waves on the water): `n1` ≈ 15 km (`N·420`), `n2` ≈ 6 km (`N·1100`), `n3` ≈ 37 km (`N·170`, slow: busy and calm coast sections); `n1`/`n2` fade out when their structure gets smaller than a pixel (`psPix`-based). The threshold falls towards the coast (`thr = lerp(1.05, 0.55, wCoast)`), `foamField = smoothstep(thr, thr + 0.14, ripple)`: almost nothing far from the coast, a lot of white near it. In addition **white pixels** (`FoamDots`, 3D cell grid in three sizes 0.0003 / 0.0008 / 0.0020 rad ≈ 2 / 5 / 13 km, the finest with at least about 2–3 pixels per cell takes over); every cell re-rolls every few seconds (`phase = t·1.4 + random`, cross-fade between two rolls), density `wCoast·(0.25 + 0.9·foamField)`. Time `_TycoonTime` = **real time at constant speed**, independent of game time. Brightness `_Foam` (0–2, default 1), dimmed at night (moonlight). `_CoastKm` and `_DetailCoastKm` (both 39, from `Tiles.CoastKm`) must match the maximum distance the coast maps were made with. (Discarded variant: surf lines running towards the coast with a threshold/shore line – looked like a white outline around the continents.)
10. **Sun glint:** `(pow(dot(Nw,H), 400) + 0.03·pow(dot(N,H), 60)) · 1.2` on water, day side only. Plus Fresnel sky reflection `pow(1 − dot(N,V), 4)` and an atmosphere rim `pow(1 − dot(N,V), 3)` on the sun side.

**Keywords** (`multi_compile_local`, set at run time by the `DetailController`; all variants are contained in the build): `TY_WAVES`, `TY_RELIEF`, `TY_DETAIL` (day detail), `TY_NIGHTDETAIL` (sharp night map), `TY_LIGHTS` (light dots), `TY_FOAM` (surf at the coast). No keywords = Low. Internal: `TY_REGION` is defined when `TY_DETAIL` or `TY_NIGHTDETAIL` is active (the detail rectangle is computed).

**Important parameters** (material properties): `_Relief` 1, `_LandGlint` 0.8, `_Foam` 1, `_NightDay` 0.7, `_NightMapAlpha` 0.12, `_WaveStrength` 2.5, `_Shininess` 400, `_GlintStrength` 1.8, `_CityGain` 3, `_AtmoColor` (0.35 0.6 1), `_DetailRect`, `_UseWaterMask`.

---

## 9. Other shaders

- **Atmosphere** (`Atmosphere.shader`): a sphere of radius 1.07 around the Earth, `Cull Front` (back faces only), `Blend One One`, `ZWrite Off`. Per pixel: distance of the view ray from the Earth's centre `b = |cross(rd, camPos)|`, intensity from `b` and the day factor of the closest point of the ray. The mesh scale and `_RadiusOut` must be equal.
- **Stars** (`Stars.shader`): skybox shader, two layers of hash stars on cells of the rotated direction (`_TycoonStarRot`), random tint.
- **Body** (`Body.shader`): sun (`_Emissive = 1`, brightness 4 for bloom) and moon (Lambert against `_TycoonSunDir`, hence the right phase).
- **Flat** (`Flat.shader`): unlit with `_Color × vertex colour`, `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `Cull Off`. For markers, route lines, planes.
- **Render queues:** outlines 3000 → markers/lines 3001 → planes 3002. The dark (semi-transparent) outlines lie below the objects so that everything stays readable on a bright background.
- Shared `TycoonCommon.hlsl`: includes URP `Core.hlsl`, declares the globals, `TySoft`, `TyHash`, `TyNoise`.

### Extra effects (atmosphere, glare, lens flare)

- **Atmosphere:** halo sphere of radius 1.07, intensity 1.9, **one continuous gradient gold → blue** (`dayMix = smoothstep(−0.35, 1.0, sun side)·0.75`: weak gold (0.85 0.5 0.24) at the terminator, still about 25 % gold at noon so that it does not look like two separate colour transitions; terminator glow weak), thicker fall-off (`pow(t, 2.4) + 0.35·pow(t, 6)`), sunset colours (orange at the terminator, blue by day), additional glow right at the terminator, **forward scattering** (`pow(saturate(dot(view ray, sun)), 5)`: against the light the atmosphere glows up to 4 times brighter). The rim on the Earth's surface is thicker accordingly (wide inner haze + bright rim, same colours, back-light boost).
- **Glare/glow/land:** sun glint on water at three scales (narrow 400, wide 60, very wide 9); land gets three reflection lobes (24 / 6 / 2.5) plus a silvery grazing light at flat view angles to the sun (`graze`). Bloom on High: threshold 0.85, intensity 1.0, scatter 0.72.
- **Lens flare** (`SunFlare.cs`, shader `Flare.shader`): eight additive sprites (golden palette, no coloured ghosts) as children of the camera (depth 0.5, beyond the near plane): warm halo, core glow, horizontal streak and five ghosts along the line through the screen centre (positions `centre + (sun − centre)·k`, k = 0.55 / −0.4 / −0.9 / −1.55 / 0.25); ghosts and streak only from Medium and only up to Z2 (see 12b). The Earth hides the sun (distance of the camera–sun ray from the Earth's centre, soft between 0.98 and 1.07 so that the sun can shine through the atmosphere); fades towards the edge of the screen. A `MaterialPropertyBlock` must not be created in a field initializer (Unity throws an exception).

---

## 10. Camera and input (`GlobeCamera`)

- State: `Lat`, `Lon` (position of the camera over the globe), `Height` (distance to the surface in Earth radii, default 0.12–5, changeable with `SetZoomLimits`). Pose: `pos = Dir(Lat, Lon) · (1 + Height)`, `rotation = LookRotation(−dir, up)`; latitude clamped to `LatitudeLimit` (80°). FOV 30°, near 0.01, far 400.
- **Turning:** the surface under the finger follows 1:1 (times `DragSensitivity`). Degrees per pixel `= 2·Height·tan(fov/2)/Screen.height`, the longitude is also divided by `cos(Lat)` (at least 0.25). Swipe right → camera to the west (`Lon −`), up → `Lat −`.
- **Inertia:** speed after letting go, decay `exp(−InertiaDamping·dt)`.
- **Zoom:** `Height *= factor`. Pinch: `factor = previousDistance / currentDistance`. Mouse: `exp(−scroll·0.01)`.
- **Camera flight** (`FocusOn`, `FocusAirport`, `FrameRoute`): latitude/longitude linear (longitude the shorter way), height logarithmic, `SmoothStep`; any gesture cancels the flight. **`FrameRoute`** picks the smallest height at which both ends are on the front side and within 75 % of the smaller view angle (bisection over `atan2(sin α, D − cos α)`).
- **Viewport offset** (`SetViewportInsets`): the camera is turned slightly so that the globe's centre appears at `((left − right)/width, (bottom − top)/height)` in screen coordinates (room for panels).
- **Tap:** a short touch without multiple fingers and with a path below `max(12 px, TapSlopMm/25.4·dpi)`. The hit is the **nearest visible airport within `PickRadiusMm`** (7 mm) on the screen (front side only, `GeoMath.HorizonDot`); otherwise the view reports the position where the view ray hits the sphere (ray–sphere intersection). No colliders, no physics.
- **UI first:** if a gesture begins where the game UI has something (`SetPointerBlocker`), the view ignores it. The demo asks `EventSystem.RaycastAll` for that.
- Input through the Input System: `Touchscreen.current.touches`, `Mouse.current`.

---

## 11. Airports, routes, planes

- **Markers** (`AirportLayer`): one sphere per `AirportView` (pooled) with radius `clamp(Height·0.006, 0.0007, 0.012) · size class (0.8 / 1 / 1.3)`, selected ×1.6; dark outline sphere (×1.5, queue 3000) behind it. **Colour and kind** from the model (alpha 1 / 0.8 / 0.6 / 0.35 for own / competitor / available / closed). **Thinning by zoom:** `Importance` ≥ 2 at Z0, ≥ 1 at Z1, all from Z2; the selected one always.
- **Routes** (`RouteLayer`): `Position(t) = Slerp(A, B, t) · (1.0005 + peak·sin(πt))`, `peak = clamp(0.02 + 0.10·angle, 0.025, 0.14)` (`RouteMath`); start and end on the surface, up to about 900 km high in the middle. 48 line segments, a wider dark outline. Main routes (`Importance` 1) are drawn from Z0, all from Z2; routes behind the horizon are off (when both ends and the middle are beyond `1/(1 + height) − 0.25`). Colour, width and status (active / planned / paused → alpha 0.55 / 0.3 / 0.18) from the model. **Selection:** when a route is selected the others are dimmed to 35 %; with a selected airport the routes that do not touch it.
- **Planes** (`FlightLayer`, pooled): each `FlightView` is a **timetable entry**. `Progress = (now − DepartUtc)/(ArriveUtc − DepartUtc)`; between 0 and 1 the plane flies (with `Backwards` along the route from B to A), otherwise it is not visible. Orientation by `LookRotation(tangent, surface normal)`, the tangent from `t ± 0.01`. At most 1 (Z0/Z1), 2 (Z2) or 4 (from Z3) planes per route, size by class and zoom.
- **Traffic flow** at high speed: from `Rate ≥ FlowRate` (21600, i.e. 6 h/s) the view shows, instead of single planes, as many aircraft per route as there are flights in the air (at most 4), gliding along the route at a fixed pace (0.28 route lengths per real second), alternating there and back.
- The **demo timetable** (`FakeController`): per route 2–10 departures a day at fixed intervals, flight time by distance at 850 km/h plus 20 min, direction alternating; generated ahead of time (horizon `max(6 h, 8·Rate)`) so that there are always flights even at 1 day/s; every 15 s a flight that has not left yet is delayed by 15 min (new times, `FlightsVersion` +1, a notice in the HUD).

---

## 12. Quality tiers and phones (`DetailController`)

The device tier is the **ceiling** for features and zoom stage (section 12b). What each tier allows is in the `Tiers` of the configuration.

| Tier | Allowed | Stage ceiling |
|---|---|---|
| Low | none of it (flat ocean, no relief, no detail, no bloom) | Z2 |
| Medium | relief, waves, surf, light dots, sharp night map (tile window 4 tiles, one level coarser) | Z3 |
| High | + day detail with relief and coast, bloom (tile window 6 tiles) | Z4 |

- **Selection:** `Tier` = Auto (desktop/editor High; phone: High from ≥ 6 GB RAM and ≥ 8 cores, Medium from ≥ 3.5 GB, otherwise Low – estimates, check on the device), can be set fixed, at run time with `SetQualityTier`. Single features can be switched off with `Features`.
- **Bloom:** from the scene's volume, otherwise the view creates its own (threshold 0.85, intensity 1.0, scatter 0.72); active on High only.
- **Not implemented yet (comes with the phone port):** URP settings per tier (render scale 0.65/0.75/0.85, MSAA 1/2/4, HDR), half texture size via `QualitySettings.globalTextureMipmapLimit`. `TargetFrameRate` (0 = leave alone) exists.

**Why so (measurement logic):** the most expensive part of the pixel shader was the procedural clouds (~38 noise functions + a 4× swirl loop) → removed. Waves as noise (6 noise functions) → normal map (2–3 texture reads). `atan2/asin` per pixel → mesh UV. Bloom is a full-screen post effect on phones → High only.

**Android/iOS checklist (still to do):** install platform modules and SDK/Xcode, ASTC compression, IL2CPP/ARM64, minimum versions, measurement on devices (fps, heat, memory), adjust the thresholds in `DetailController.Detect`.

---

## 12b. Zoom stages (`DetailController`) and behaviour at high speed

Five detail stages by camera height (Earth radii above the surface), implemented in `DetailController` (stage choice: `ZoomStages.Next`, testable). The device tier (section 12) is only the **ceiling**: Low 2, Medium 3, High 4; `Stage = min(RawStage, ceiling)` counts. The edges are `StageEdges` in the configuration; what each stage shows is `Stages`.

| Stage | Height | View |
|---|---|---|
| Z0 Space | > 3.2 | the whole globe, small |
| Z1 Globe | 1.6 – 3.2 | the globe fills the screen |
| Z2 Continent | 0.75 – 1.6 | one continent |
| Z3 Region | 0.32 – 0.75 | a country (tile level 5) |
| Z4 Close | 0.12 – 0.32 | a region filling the screen (tile level 6) |

Changes with **12 % hysteresis** (`StageEdges = {3.2, 1.6, 0.75, 0.32}`, chosen early so that it never looks coarse before the next change; inwards at `height < edge·0.88`, outwards at `height > edge·1.12`) so that nothing flickers at the limits.

| | Z0 | Z1 | Z2 | Z3 | Z4 |
|---|---|---|---|---|---|
| Relief | off (`_Relief` = 0) | fades in | on | on | on, plus tile relief |
| Waves (`_ZWaveLayers`) | – (no `TY_WAVES`) | – | 1 layer | 2 layers | 3 layers + calm/rough patches |
| Surf (`TY_FOAM`, `_ZFoamDots`) | off | off | soft ripple edge, no pixels | + white pixels | all pixel sizes |
| Light dots (`TY_LIGHTS`) | off | off | 1 grid, own cell only (`_ZLightWide` = 0) | 2 grids, 3×3 overlap | 3 grids, 3×3 |
| Flashes of the lights (`_ZFlares`) | – | – | off | on | on |
| Tile window (day, night, relief, coast) | not loaded | not loaded | not loaded | being built (ahead from Z2) | built, finer tiles |
| Sharp night map (`TY_NIGHTDETAIL`) / day detail (`TY_DETAIL`) | off | off | off | on (Medium+ / High) | on |
| Mip bias (`_ZMipBias`) | +1 (coarser mips) | fades out | 0 | 0 | 0 |
| Globe mesh | 96×48 | 192×96 | 192×96 | 192×96 | 192×96 |
| Atmosphere halo | on | on | on | off (rim not in the picture, from height < 0.75) | off |
| Lens flare | everything | everything | everything | only halo and core glow | off |
| Airport labels (HUD, at the anchors) | none | none | only `Importance` 3 | all | all |
| Routes / planes | main routes only (`Importance` 1), 1 plane | as Z0 | all routes, up to 2 planes | all, up to 4 | all |

- **The tile window is built from Z2 on** (one stage early so that it is there at Z3), in the background and with at most `TilesPerFrame` tiles per frame, and **released 4 s after zooming out below Z1**. The keywords `TY_DETAIL`/`TY_NIGHTDETAIL` are only set when the window is there and are switched off before it is released.
- **Continuous parts** (relief strength, mip bias) follow the height between `StageEdges[1]` and `StageEdges[0]` = 1.6 and 3.2 (`smoothstep`); everything structural (additional grids, wave layers, keywords) switches at the stage limits. Lights, surf and waves also fade out depending on the pixel size (`psPix`).
- **Shader variants** are warmed up before a keyword set is switched on (see lessons, 12 and 13).
- **Test:** `Capture -height` chooses the stage (e.g. 3.5 = Z0, 2.0 = Z1, 1.0 = Z2, 0.5 = Z3, 0.2 = Z4); `GlobeViewHost.TickImmediate` builds the tile window synchronously.

### Behaviour at high speed (`Rate`)

| Speed | Planes | Day/night |
|---|---|---|
| up to 1 h/s | single aircraft according to the timetable | normal |
| 6 h/s (`FlowRate` 21600) | **traffic flow** instead of single aircraft (section 11) | the change stays clearly visible |
| 1 day/s (86400) | traffic flow | calm, even light (`CalmRange` 43200 → 86400) and the **fast-forward look** (`RewindRate`) |

`SkyRenderer` computes `_TycoonCalm = smoothstep(CalmRange.x, CalmRange.y, Rate)` (with `ReduceMotion` already at a quarter of these values, always 0 with `SunMode.Fixed`). In the shader `calm` blends the sun-dependent parts into a constant light (`ndl → 0.25`, lights threshold → 0.35), switches off sun glint, land reflection and wave facets, and damps halo and atmosphere rim; otherwise the globe would blink between day and night once per second at 1 day/s. The thresholds are starting values and should be tuned with testers.

**Fast-forward look** (`RewindEffect`, only from `RewindRate`, i.e. at 1 day/s; fades in and out in 0.4 s): the picture trembles and now and then slips away (a small camera wobble in pitch, yaw and roll, plus a short jerk every 0.6 to 1.6 s); on top lie scan lines, crawling noise, two bright tracking bands and a noisy strip at the bottom edge. The overlay is its own canvas below the HUD, so the HUD stays steady. Off with `ReduceMotion` or `RewindRate = 0`.

---

## 13. Test and screenshot tool

`Demo/Editor/Capture.cs` renders without play mode (play mode in batch mode hangs and never got to a screenshot). It opens the demo scene, creates `FakeController` and `GlobeViewHost`, calls `host.TickImmediate` (the components do not run on their own in edit mode) and renders the camera into a `RenderTexture` (4× MSAA) → PNG. With `-movie` it writes a JPEG per frame instead.

```bash
Unity -batchmode -projectPath <project> -executeMethod Tycoon.Demo.EditorTools.Capture.Run \
      -logFile out.log -tycoonShot out.png \
      [-utc 2026-10-25T12:00:00] [-rate 600] [-lat 48 -lon 10 -height 0.5] \
      [-tier low|medium|high] [-time <seconds>] [-stress] [-hud [-select 1]] [-w 1280 -h 720]
      [-movie zoom|hud -frames 90 -fps 15 -outdir frames]
```

Path on macOS: `/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity`. The script quits Unity itself (`EditorApplication.Exit`). About 20–60 s per picture.

- **The editor must not have the project open** ("another Unity instance is running with this project"). Workaround if the editor is to stay open: copy the project as an APFS clone (`cp -cR project copy`, costs no space, the Library is cloned too, no re-import), then `rsync -a --delete Assets/` and render there.
- `-hud` draws the HUD into the picture (the canvas scales to the width / 390). Markers, routes and planes are moved in the render queue so that the screen-space canvases end up on top, as the overlay canvases do in the game.
- `-time` freezes the twinkling of the city lights at one second (compare two pictures with different `-time` to check the twinkling).
- `-utc` sets date and time, `-rate` the game speed (default 0 = time stands still; 86400 shows calm mode, traffic flow and the fast-forward look), `-stress` switches on the fake controller's stress test.
- **Movies:** `-movie zoom` flies from space into the Alps, `-movie hud` runs through the speed steps with the HUD. Assemble the frames with ffmpeg (see the README).
- **Tests** (EditMode, no screen): `Assets/Tests/EditMode/GlobeViewTests.cs` and `HudTests.cs` – geometry, astronomy, zoom stages with hysteresis, the configuration file, tile geometry and tile window, contracts (the fake controller delivers a valid model even at high speed and in the stress test), `GlobeData`, the view interface (anchors, taps, `FocusAirport`/`FrameRoute`, viewport offset, zoom limits, stage and tier), sky and calm, timetable and traffic flow, the fast-forward look, HUD texts, flip cards, speed slider, airport sheet, labels.

---

## 14. Lessons learned / pitfalls

0. **Night map pixelated when zooming:** only 4.9 km per pixel globally. Remedy: sharp 500 m night tiles (A), light dots in the shader (C), quintic interpolation. `smoothstep` with swapped edges (`smoothstep(0.3, 0.0, x)`) happens to work in Metal but is undefined by the HLSL specification – use `1 − smoothstep(0.0, 0.3, x)`.
1. **Editor recompilation in play mode** produced thousands of `NullReferenceException` in the prototype (static state was gone after the domain reload). Today there are no statics left; `PlayModeGuard` still ends play mode on a recompile because the demo objects (`DemoMain`) lose their state.
2. **Avoid statics:** the prototype needed `ResetStatics` because the template had "Reload Domain" switched off. The view now keeps everything in instances (exception: the shader globals, hence one active host). `Destroy` must not be used in edit mode, `GlobeViewHost.Discard` exists for that (a test found it).
3. **Python `urllib` fails on macOS with SSL certificates.** Do not switch the check off, use `curl` (`tools/get_data.sh`).
4. **Date line:** `atan2` jumps; derivatives must come from the jump-free UV. With a mesh UV that continues past 1 the problem disappears.
5. **Water mask from the day colour:** differences (`b − r`) fail on dark deep sea; ratios work.
6. **Sun glint:** a second wide reflection lobe made huge grey blotches. The narrow lobe (exponent 400) plus a very weak wide one (0.03, exponent 60) looks good.
7. **Waves from a small texture** look regular if only two layers with the same angle run. Solution: three scales without a common divisor, rotated, opposing drift, plus calm/rough patches. With far too small a pattern it looked like gravel: tile counts 80/210/520.
8. **Clouds (discarded):** plain noise gave "cotton balls". It only became realistic with twisted cyclones, lumpy tropics, latitude winds (flow map), a new weather pattern per period – but too expensive for phones and not convincing enough. Removed (the prototype code is not part of this repository). A bug there: adding the noise offset before `normalize()` turned the fine clouds into smooth blobs. Recommended way back: a real NASA cloud map as a texture (two drifting layers, ~2–4 MB) or bake a texture offline.
9. **Dark outlines** under markers, lines and planes solve the "white on clouds/ice unreadable" problem.
10. **Known harmless messages:** "Script 'Foo.cs' will not be compiled because it exists outside the Assets folder" comes from Unity itself; "Shader Hidden/ChartRasterizerHardware is not supported" from the light baker hardware; ignore both.
11. **Constructing a `MaterialPropertyBlock` in a MonoBehaviour field initializer** throws; create it in the constructor/`Awake`.
12. **Magenta when zooming in:** when a shader keyword is switched on for the first time the variant has to be compiled first (the editor does it in the background and draws magenta meanwhile). `ShaderWarmup` warms up the variants of the next stage ahead of time (`ShaderVariantCollection.WarmUpProgressively`) and `DetailController` only switches a set on when it is warm; until then the old one stays active (it gives up after 5 s). Without graphics (tests, batch mode) and in the capture tool nothing is held back.
13. **Edges at the border of the day/night transition:** several `smoothstep` ramps that start and end at different places, and linear `saturate(...)` ramps, show an edge at their beginning and end (Mach bands). All transitions with the sun now use `TySoft` (quintic, slope and curvature 0 at both ends) and wider ranges (`TycoonCommon.hlsl`).
14. **Screen-space canvases and 3D objects:** markers and routes draw in the transparent queue (3000–3002) and appear over screen-space-camera canvases at queue 3000. The game uses overlay canvases (always on top); only the screenshot tool, which has to use camera canvases, shifts the queues.

---

## 15. Taking it into the real game

**State of the migration:** phases 1–6 of the migration plan are done (structure and contracts, host without statics, data layers with timetable, camera and input, detail stages and speed rules, demo with fake controller and uGUI HUD). **Open:** phase 0 (device part: platform modules, measurements), phase 7 (hardening on Android and iOS), phase 8 (the real game).

**In the real game:**
1. `Tycoon.Domain` (pure C# game logic), `Tycoon.App` with a `GameController` that implements `IGlobeModel` (timetables straight from the domain state; time as `GameTimeState`) and `IHudModel`, and translates `AirportTapped`/`EmptyTapped` into commands.
2. The wiring is in `Demo/DemoMain.cs` (≈ 30 lines): `GlobeViewHost.Create(config, controller, Camera.main)`, connect the events, `SetPointerBlocker(hud.IsOverUi)`; the fake controller is replaced by the `GameController`, the demo and fake controller stay as development and regression tool.
3. Labels and tooltips are drawn by the game UI at the anchors from `TryGetAirportScreenPoint` (example: `GateChartHud.UpdateLabels`); panels that cover the globe move it with `SetViewportInsets`.
4. Save game: domain state plus `GetCameraState()`.

**Setting up a new project:** Unity 6 with the URP template, Input System active; take over the folder `Assets/GlobeView` (including Resources) and `Assets/Hud`; make the textures and tiles with `tools/`; in the game provide a camera, a configuration (the JSON file) and a controller.

---

## 16. Open points and ideas

- **Devices:** Android and iOS builds, measurement (fps, heat, memory), URP settings per tier and half texture size for Low/Medium (see 12), thresholds of the device tiers.
- Clouds as a baked texture (a static NASA map or a baked one), two drifting layers.
- Stars as a small cubemap instead of calculation; a bloom replacement for the city lights on Low/Medium.
- Tiles for more areas and finer levels: download more raw data (`tools/get_data.sh`, now only Central Europe and a quarter of the Earth at 500 m) and run `build_tiles.py`; tiles in player builds (StreamingAssets copy or server, `Tiles.BaseUrl`).
- Planes and markers with GPU instancing once many are visible; tapping routes and planes; free flight poses for special flights.
- A flat planning map next to the globe (decision pending).

---

## 17. Appendix: parameters and constants at a glance

| Where | Name | Value |
|---|---|---|
| Camera | FOV / near / far | 30° / 0.01 / 400 |
| Camera | `MinHeight` / `MaxHeight` | 0.12 / 5 Earth radii |
| Camera | latitude | ±80° |
| Camera | inertia | `exp(−4·dt)` |
| Sun / moon | distance | 150 / 60.3 |
| Atmosphere | radius | 1.07 |
| Airport marker | radius | `clamp(Height·0.006, 0.0007, 0.012)` |
| Routes | peak height | `clamp(0.02 + 0.10·angle, 0.025, 0.14)` |
| Planes | speed | 850 km/h in game time |
| Game speed steps (HUD) | | pause, 10 min/s (600), 1 h/s (3600), 6 h/s (21600), 1 day/s (86400) |
| Traffic flow from | `FlowRate` | 21600 |
| Calm lighting | `CalmRange` | 43200 → 86400 |
| Fast-forward look from | `RewindRate` | 86400 |
| Relief | exaggeration | `K = 8` (in the tile and normal-map scripts) |
| Earth shader | twilight | `TySoft(−0.50, −0.02, ndl)` … `1 − TySoft(0.02, 0.42, ndl)` |
| Earth shader | city lights | `nightMask = 1 − TySoft(−0.42, 0.16, ndl)` |
| Bloom | threshold / intensity / scatter | 0.85 / 1.0 / 0.72 |
| Device | target fps | `TargetFrameRate` (0 = unchanged; 30 intended for phones, phase 7) |
| Tiles | tile size / levels / coast distance | 512 px / 4–6 / 39 km |

Earth radius 6371 km, unit sphere = 1.

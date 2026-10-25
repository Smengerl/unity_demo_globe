using System;
using System.Collections.Generic;
using System.Globalization;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// The globe as a view: renders what the <see cref="IGlobeModel"/> says, reports taps, takes commands (<see cref="IGlobeView"/>).
    /// Create it with <see cref="Create"/> and call <see cref="Dispose"/> when done. One host at a time (shader globals are shared).
    /// The shaders and textures load from the Resources folder of this package.
    /// </summary>
    public sealed class GlobeViewHost : MonoBehaviour, IGlobeView, IDisposable
    {
        const float AtmosphereRadius = 1.07f;

        public static GlobeViewHost Create(GlobeViewConfig config, IGlobeModel model, Camera camera)
        {
            var host = new GameObject("GlobeView").AddComponent<GlobeViewHost>();
            host.Initialize(config, model, camera);
            return host;
        }

        // ---- IGlobeView: events ----
        public event Action Ready;
        public event Action<int> AirportTapped;
        public event Action<float, float> EmptyTapped;
        public event Action<CameraState> CameraChanged;
        public event Action<int> ZoomStageChanged;

        // ---- extra for tools and debug overlays ----
        public QualityTier Tier => detail.Tier;
        public int RawZoomStage => detail.RawStage;

        public int ZoomStage => detail.Stage;

        GlobeViewConfig config;
        IGlobeModel model;
        Camera cam;
        GlobeData data;
        GlobeCamera globeCam;
        DetailController detail;
        SkyRenderer sky;
        AirportLayer airports;
        RouteLayer routes;
        FlightLayer flights;
        SunFlare flare;
        RewindEffect rewind;

        readonly List<Object> owned = new List<Object>();
        Material skyboxBefore;
        Volume ownVolume;
        GameObject sceneRoot;
        bool initialized, disposed, active = true, readyPending = true;
        int lastStage = -1;
        CameraState lastCameraEvent;
        float cameraEventTimer;

        void Initialize(GlobeViewConfig cfg, IGlobeModel mdl, Camera camera)
        {
            config = cfg;
            model = mdl;
            cam = camera;
            data = new GlobeData();
            if (config.TargetFrameRate > 0) Application.targetFrameRate = config.TargetFrameRate;

            skyboxBefore = RenderSettings.skybox;
            RenderSettings.skybox = Mat("Stars");
            cam.clearFlags = CameraClearFlags.Skybox;
            globeCam = new GlobeCamera(cam, config);
            globeCam.Tapped += OnTap;

            sceneRoot = new GameObject("Globe");
            sceneRoot.transform.SetParent(transform, false);

            // Earth, atmosphere, sun and moon
            var earthMat = Mat("Earth");
            Tex(earthMat, "_DayTex", "day");
            Tex(earthMat, "_NightTex", "night");
            Tex(earthMat, "_NormalTex", "normal");
            Tex(earthMat, "_WavesTex", "waves");
            if (Tex(earthMat, "_WaterMask", "watermask", required: false)) earthMat.SetFloat("_UseWaterMask", 1f);
            if (Tex(earthMat, "_CoastTex", "coast", required: false)) earthMat.SetFloat("_UseCoast", 1f);
            earthMat.SetFloat("_CoastKm", config.Tiles.CoastKm);
            var meshHi = Own(MeshFactory.Sphere(192, 96));
            var meshLo = Own(MeshFactory.Sphere(96, 48));
            var earth = Body("Earth", meshHi, earthMat, 1f);
            var halo = Body("Atmosphere", Own(MeshFactory.Sphere(96, 48)), Mat("Atmosphere", m => m.SetFloat("_RadiusOut", AtmosphereRadius)), AtmosphereRadius);

            var bodyMesh = Own(MeshFactory.Sphere(48, 24));
            var sun = Body("Sun", bodyMesh, Mat("Body", m => { m.color = new Color(1f, 0.96f, 0.85f); m.SetFloat("_Emissive", 1f); }), 0.7f);
            var moon = Body("Moon", bodyMesh, Mat("Body", m => m.color = new Color(0.72f, 0.72f, 0.7f)), 0.2727f);
            moon.SetActive(config.ShowMoon);
            sky = new SkyRenderer(config, sun.transform, moon.transform);
            rewind = new RewindEffect(config);

            // data layers
            var flat = Mat("Flat");
            airports = new AirportLayer(NewRoot("Airports"), Own(MeshFactory.Sphere(12, 8)), flat);
            routes = new RouteLayer(NewRoot("Routes"), flat);
            flights = new FlightLayer(NewRoot("Flights"), Own(MeshFactory.Airplane()), flat, config);
            if (config.SunFlare) flare = new SunFlare(cam, sun.transform, Mat("Flare"));

            detail = new DetailController(config, earthMat, earth.GetComponent<MeshFilter>(), meshHi, meshLo, halo, FindOrCreateBloom());
            initialized = true;
        }

        // ---- per frame ----

        void LateUpdate()
        {
            if (initialized && active) TickCore(Time.unscaledDeltaTime, false);
        }

        /// <summary>For screenshot tools: draws the view's own 2D overlay (the fast-forward look) into a camera instead of the screen.</summary>
        public void RenderOverlaysInto(Camera target) => rewind.RenderInto(target);

        /// <summary>For tools and tests outside play mode: one tick, the tile window builds synchronously.</summary>
        public void TickImmediate(float deltaTime) => TickCore(deltaTime, true);

        void TickCore(float dt, bool syncLoad)
        {
            data.Refresh(model);
            if (data.AirportsChanged) airports.OnDataChanged(data);
            if (data.AirportsChanged || data.RoutesChanged) routes.OnDataChanged(data);

            globeCam.Tick(dt);
            detail.Tick(globeCam.Height, globeCam.State.Lat, globeCam.State.Lon, syncLoad);
            int stage = detail.Stage;
            var time = model.Time;
            var selection = model.Selection;
            Vector3 camDir = cam.transform.position.normalized;

            rewind.Tick(time.Rate, dt, cam.transform);
            sky.Tick(time, dt);
            airports.Tick(data, globeCam.Height, stage, selection.AirportId);
            routes.Tick(data, camDir, globeCam.Height, stage, selection);
            flights.Tick(data, routes.Visible, time, Time.unscaledTime, globeCam.Height, stage);
            flare?.Tick(stage, detail.FlaresAllowed);

            if (stage != lastStage) { lastStage = stage; ZoomStageChanged?.Invoke(stage); }
            cameraEventTimer += dt;
            if (cameraEventTimer > 1f / 15f && CameraMoved())
            {
                cameraEventTimer = 0f;
                lastCameraEvent = globeCam.State;
                CameraChanged?.Invoke(lastCameraEvent);
            }
            if (readyPending) { readyPending = false; Ready?.Invoke(); }
        }

        bool CameraMoved()
        {
            var s = globeCam.State;
            return Mathf.Abs(s.Lat - lastCameraEvent.Lat) > 0.01f || Mathf.Abs(s.Lon - lastCameraEvent.Lon) > 0.01f
                || Mathf.Abs(s.Height - lastCameraEvent.Height) > 0.001f;
        }

        // ---- taps ----

        internal void OnTap(Vector2 screenPx)   // internal: the tests tap without a finger
        {
            float radiusPx = config.PickRadiusMm / 25.4f * GlobeCamera.Dpi;
            if (airports.TryPick(data, cam, screenPx, radiusPx, globeCam.Height, out int id)) { AirportTapped?.Invoke(id); return; }

            // empty tap: where does the ray hit the globe?
            Ray ray = cam.ScreenPointToRay(screenPx);
            float b = Vector3.Dot(ray.origin, ray.direction);
            float disc = b * b - (ray.origin.sqrMagnitude - 1f);
            if (disc < 0f) return;
            Vector3 hit = ray.origin + ray.direction * (-b - Mathf.Sqrt(disc));
            GeoMath.LatLon(hit.normalized, out float lat, out float lon);
            EmptyTapped?.Invoke(lat, lon);
        }

        // ---- IGlobeView: commands and queries ----

        public CameraState GetCameraState() => globeCam.State;
        public void SetCameraState(CameraState s) => globeCam.SetState(s.Lat, s.Lon, s.Height);
        public void FocusOn(float lat, float lon, float height, float seconds) => globeCam.FlyTo(lat, lon, height, seconds);

        public void FocusAirport(int airportId, float height = 0.3f, float seconds = 1.2f)
        {
            if (!data.TryAirportIndex(airportId, out int i)) return;
            globeCam.FlyTo(data.Airports[i].Lat, data.Airports[i].Lon, height, seconds);
        }

        public void FrameRoute(int routeId, float seconds = 1.2f)
        {
            if (!data.TryRouteIndex(routeId, out int i) || data.RouteGeoms[i].FromIndex < 0) return;
            globeCam.FrameDirections(data.RouteGeoms[i].A, data.RouteGeoms[i].B, seconds);
        }

        public void SetZoomLimits(float minHeight, float maxHeight)
        {
            globeCam.MinHeight = minHeight;
            globeCam.MaxHeight = Mathf.Max(maxHeight, minHeight);
            globeCam.Height = Mathf.Clamp(globeCam.Height, globeCam.MinHeight, globeCam.MaxHeight);
        }

        public void SetInputEnabled(bool rotate, bool zoom, bool tap)
        {
            globeCam.RotateEnabled = rotate;
            globeCam.ZoomEnabled = zoom;
            globeCam.TapEnabled = tap;
        }

        public void SetViewportInsets(float top, float bottom, float left, float right) => globeCam.SetInsets(top, bottom, left, right);
        public void SetQualityTier(QualityTier tier) => detail.SetTier(tier);
        public void SetPointerBlocker(Func<Vector2, bool> isOverUi) => globeCam.PointerBlocker = isOverUi;

        public void SetActive(bool value)
        {
            active = value;
            cam.enabled = value;
            sceneRoot.SetActive(value);
        }

        public bool TryGetAirportScreenPoint(int airportId, out Vector2 screenPx, out bool visible) =>
            airports.TryScreenPoint(data, cam, globeCam.Height, airportId, out screenPx, out visible);

        // ---- build helpers ----

        Transform NewRoot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(sceneRoot.transform, false);
            return go.transform;
        }

        GameObject Body(string name, Mesh mesh, Material mat, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(sceneRoot.transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }

        Material Mat(string shaderName, Action<Material> setup = null)
        {
            var shader = Resources.Load<Shader>("Shaders/" + shaderName);
            if (shader == null)
            {
                Debug.LogError($"GlobeView: shader Resources/Shaders/{shaderName} not found");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            var m = Own(new Material(shader));
            setup?.Invoke(m);
            return m;
        }

        static bool Tex(Material m, string property, string resource, bool required = true)
        {
            var t = Resources.Load<Texture2D>("Earth/" + resource);
            if (t == null)
            {
                if (required) Debug.LogWarning($"GlobeView: texture Resources/Earth/{resource} is missing");
                return false;
            }
            m.SetTexture(property, t);
            return true;
        }


        // Bloom makes city lights and the sun glint glow on High. Uses the scene's global volume if there is one, else makes its own.
        Bloom FindOrCreateBloom()
        {
            var volume = FindAnyObjectByType<Volume>();
            if (volume == null)
            {
                ownVolume = new GameObject("GlobeView volume").AddComponent<Volume>();
                ownVolume.isGlobal = true;
                ownVolume.profile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
                volume = ownVolume;
            }
            if (!volume.profile.TryGet(out Bloom bloom)) bloom = volume.profile.Add<Bloom>();
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.0f);
            bloom.scatter.Override(0.72f);
            return bloom;
        }

        // ---- end of life ----

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (initialized)
            {
                globeCam.Tapped -= OnTap;
                flare?.Destroy();
                rewind.Destroy();
                detail.Dispose();
                RenderSettings.skybox = skyboxBefore;
                if (ownVolume != null) Discard(ownVolume.gameObject);
                foreach (var o in owned) if (o != null) Discard(o);
                owned.Clear();
            }
            Discard(gameObject);
        }

        void OnDestroy() => Dispose();

        internal static void Discard(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}

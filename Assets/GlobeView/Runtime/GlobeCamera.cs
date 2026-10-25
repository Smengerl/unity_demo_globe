using System;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Orbit camera around the unit-sphere Earth: drag = rotate, pinch / scroll = zoom, short press = tap.
    /// Mouse and touch through the Input System. Also flies to targets and keeps the globe's centre clear of UI panels.
    /// </summary>
    internal sealed class GlobeCamera
    {
        public float Lat, Lon, Height;
        public float MinHeight, MaxHeight;
        public bool RotateEnabled = true, ZoomEnabled = true, TapEnabled = true;
        public Func<Vector2, bool> PointerBlocker;
        public event Action<Vector2> Tapped;

        /// <summary>Dots per inch, with a fallback where the platform does not report one.</summary>
        public static float Dpi => Screen.dpi > 0f ? Screen.dpi : 110f;

        readonly Camera cam;
        readonly GlobeViewConfig config;

        Vector2 vel;                        // deg/s (lon, lat), for inertia after release
        bool down, multi, blocked;
        float travel;
        Vector2 last;

        // flight to a target
        bool flying;
        float flyTime, flyDuration, flyLat0, flyLon0, flyHeight0, flyLat1, flyLon1, flyHeight1;

        Vector2 centreNdc;                  // where the globe's centre sits on screen (0,0 = middle), set by the viewport insets

        public GlobeCamera(Camera camera, GlobeViewConfig config)
        {
            cam = camera;
            this.config = config;
            Lat = config.InitialLat;
            Lon = config.InitialLon;
            Height = config.InitialHeight;
            MinHeight = config.MinHeight;
            MaxHeight = config.MaxHeight;
            cam.fieldOfView = config.FieldOfView;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 400f;
        }

        public CameraState State => new CameraState(Lat, Lon, Height);

        public void SetState(float lat, float lon, float height)
        {
            flying = false;
            vel = Vector2.zero;
            Lat = lat; Lon = lon; Height = Mathf.Clamp(height, MinHeight, MaxHeight);
            Clamp();
        }

        public void FlyTo(float lat, float lon, float height, float seconds)
        {
            if (seconds <= 0f) { SetState(lat, lon, height); return; }
            flying = true;
            flyTime = 0f; flyDuration = seconds;
            flyLat0 = Lat; flyLon0 = Lon; flyHeight0 = Height;
            flyLat1 = Mathf.Clamp(lat, -config.LatitudeLimit, config.LatitudeLimit);
            flyLon1 = flyLon0 + Mathf.DeltaAngle(flyLon0, lon);     // shortest way round
            flyHeight1 = Mathf.Clamp(height, MinHeight, MaxHeight);
            vel = Vector2.zero;
        }

        /// <summary>Flies so that both directions (unit vectors on the globe) are in view.</summary>
        public void FrameDirections(Vector3 a, Vector3 b, float seconds)
        {
            Vector3 centre = a + b;
            if (centre.sqrMagnitude < 1e-4f) centre = Vector3.Cross(a, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(a, Vector3.up) : Vector3.right;
            centre.Normalize();
            float half = Vector3.Angle(a, centre) * Mathf.Deg2Rad;       // angle from the centre to either end
            float limit = Mathf.Min(cam.fieldOfView * 0.5f, Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg) * 0.75f * Mathf.Deg2Rad;

            // smallest height at which both ends are on the near side of the globe and well inside the picture
            float lo = MinHeight, hi = MaxHeight;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi), dist = 1f + mid;
                bool fits = Mathf.Atan2(Mathf.Sin(half), dist - Mathf.Cos(half)) <= limit && half < Mathf.Acos(1f / dist) * 0.92f;
                if (fits) hi = mid; else lo = mid;
            }
            GeoMath.LatLon(centre, out float lat, out float lon);
            FlyTo(lat, lon, hi, seconds);
        }

        public void SetInsets(float top, float bottom, float left, float right)
        {
            centreNdc = new Vector2((left - right) / Mathf.Max(Screen.width, 1), (bottom - top) / Mathf.Max(Screen.height, 1));
        }

        public void Tick(float dt)
        {
            dt = Mathf.Max(dt, 1e-4f);
            ReadInput(dt);

            if (flying)
            {
                flyTime += dt;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flyTime / flyDuration));
                Lat = Mathf.Lerp(flyLat0, flyLat1, t);
                Lon = Mathf.Lerp(flyLon0, flyLon1, t);
                Height = Mathf.Exp(Mathf.Lerp(Mathf.Log(flyHeight0), Mathf.Log(flyHeight1), t));
                if (flyTime >= flyDuration) flying = false;
            }
            Clamp();
            ApplyPose();
        }

        void Clamp()
        {
            Lat = Mathf.Clamp(Lat, -config.LatitudeLimit, config.LatitudeLimit);
            Lon = Mathf.Repeat(Lon + 180f, 360f) - 180f;
            Height = Mathf.Clamp(Height, MinHeight, MaxHeight);
        }

        void ApplyPose()
        {
            Vector3 dir = GeoMath.Dir(Lat, Lon);
            var look = Quaternion.LookRotation(-dir, Vector3.up);
            // turn the camera slightly so the globe's centre appears at centreNdc instead of the middle of the screen
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var toCentre = new Vector3(centreNdc.x * tanV * cam.aspect, centreNdc.y * tanV, 1f).normalized;
            cam.transform.SetPositionAndRotation(dir * (1f + Height), look * Quaternion.Inverse(Quaternion.FromToRotation(Vector3.forward, toCentre)));
        }

        // ---- input ----

        void ReadInput(float dt)
        {
            int n = 0;
            Vector2 p0 = default, d0 = default, p1 = default, d1 = default;
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (!t.isInProgress) continue;
                    if (n == 0) { p0 = t.position.ReadValue(); d0 = t.delta.ReadValue(); }
                    else if (n == 1) { p1 = t.position.ReadValue(); d1 = t.delta.ReadValue(); }
                    n++;
                }
            }

            var mouse = Mouse.current;
            if (n == 0 && mouse != null && mouse.leftButton.isPressed)
            {
                n = 1;
                p0 = mouse.position.ReadValue();
                d0 = mouse.delta.ReadValue();
            }
            if (mouse != null && ZoomEnabled)
            {
                float s = Mathf.Clamp(mouse.scroll.ReadValue().y, -30f, 30f);
                if (s != 0f && !IsBlocked(mouse.position.ReadValue())) Zoom(Mathf.Exp(-s * 0.01f));
            }

            if (n >= 2)
            {
                float cur = (p0 - p1).magnitude;
                float prev = ((p0 - d0) - (p1 - d1)).magnitude;
                if (ZoomEnabled && cur > 1f && prev > 1f) Zoom(prev / cur);
                down = true; multi = true; vel = Vector2.zero;
            }
            else if (n == 1)
            {
                if (!down)
                {
                    down = true; multi = false; travel = 0f; vel = Vector2.zero;
                    blocked = IsBlocked(p0);
                }
                last = p0;
                if (!blocked && !multi)
                {
                    travel += d0.magnitude;
                    if (RotateEnabled) Drag(d0, dt);
                }
            }
            else if (down)
            {
                float slop = Mathf.Max(12f, config.TapSlopMm / 25.4f * Dpi);
                if (!blocked && !multi && travel < slop && TapEnabled) Tapped?.Invoke(last);
                down = false;
            }
            else if (vel.sqrMagnitude > 1e-4f)
            {
                Lon += vel.x * dt;
                Lat += vel.y * dt;
                vel *= Mathf.Exp(-config.InertiaDamping * dt);
            }
        }

        bool IsBlocked(Vector2 screenPos) => PointerBlocker != null && PointerBlocker(screenPos);

        void Zoom(float factor)
        {
            flying = false;
            Height = Mathf.Clamp(Height * factor, MinHeight, MaxHeight);
        }

        // Surface at screen centre follows the finger 1:1 (times the sensitivity).
        float DegreesPerPixel() =>
            2f * Height * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height * Mathf.Rad2Deg * config.DragSensitivity;

        void Drag(Vector2 px, float dt)
        {
            flying = false;
            float deg = DegreesPerPixel();
            float lonScale = 1f / Mathf.Max(Mathf.Cos(Lat * Mathf.Deg2Rad), 0.25f);
            var move = new Vector2(-px.x * deg * lonScale, -px.y * deg);
            Lon += move.x;
            Lat += move.y;
            vel = Vector2.Lerp(vel, move / dt, 0.4f);
        }
    }
}

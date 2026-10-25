using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tycoon.Demo;
using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using UnityEngine;

namespace Tycoon.Tests
{
    public class GeometryTests
    {
        [Test]
        public void DirAndLatLonRoundTrip()
        {
            foreach (var (lat, lon) in new[] { (0f, 0f), (48.1f, 11.6f), (-33.9f, 151.2f), (60f, -170f) })
            {
                GeoMath.LatLon(GeoMath.Dir(lat, lon), out float la, out float lo);
                Assert.AreEqual(lat, la, 0.01f);
                Assert.AreEqual(lon, lo, 0.01f);
            }
        }

        [Test]
        public void EastIsPlusXAndLonZeroIsMinusZ()
        {
            Vector3 d = GeoMath.Dir(0, 0);
            Assert.AreEqual(-1f, d.z, 1e-5f);
            Assert.Greater(GeoMath.Dir(0, 10).x, 0f);
        }

        [Test]
        public void RouteStartsAndEndsOnTheSurfaceAndRisesInBetween()
        {
            Vector3 a = GeoMath.Dir(50, 8), b = GeoMath.Dir(40, -74);
            float angle = RouteMath.AngleRad(a, b);
            Assert.AreEqual(1.0005f, RouteMath.Position(a, b, angle, 0f).magnitude, 1e-4f);
            Assert.AreEqual(1.0005f, RouteMath.Position(a, b, angle, 1f).magnitude, 1e-4f);
            Assert.Greater(RouteMath.Position(a, b, angle, 0.5f).magnitude, 1.02f);
        }

        [Test]
        public void FlightProgressRunsFromZeroToOne()
        {
            Assert.AreEqual(0f, RouteMath.Progress(100, 100, 300), 1e-6f);
            Assert.AreEqual(0.5f, RouteMath.Progress(200, 100, 300), 1e-6f);
            Assert.AreEqual(1f, RouteMath.Progress(300, 100, 300), 1e-6f);
            Assert.Less(RouteMath.Progress(50, 100, 300), 0f);
        }
    }

    public class AstronomyTests
    {
        static double Unix(int y, int mo, int d, int h) => new DateTimeOffset(y, mo, d, h, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        [Test]
        public void SunSitsOverTheEquatorialBeltAtNoonUtcOnTheGreenwichMeridian()
        {
            var sky = Astronomy.Compute(Unix(2026, 10, 25, 12));
            GeoMath.LatLon(sky.SunDir, out float lat, out float lon);
            Assert.AreEqual(-12.3f, lat, 1f);      // declination in late October
            Assert.AreEqual(-4f, lon, 8f);         // equation of time about +16 minutes
        }

        [Test]
        public void MoonPhasesMatchTheCalendar()
        {
            float Elongation(double t) { var s = Astronomy.Compute(t); return Vector3.Angle(s.SunDir, s.MoonDir); }
            Assert.Less(Elongation(Unix(2026, 10, 10, 16)), 10f);       // new moon 2026-10-10
            Assert.Greater(Elongation(Unix(2026, 10, 26, 4)), 165f);    // full moon 2026-10-26
        }
    }

    public class ZoomStageTests
    {
        static readonly float[] Edges = { 3.2f, 1.6f, 0.75f, 0.32f };

        [Test]
        public void HeightsMapToTheFiveStages()
        {
            Assert.AreEqual(0, ZoomStages.Next(0, 5f, Edges, 0.12f));
            Assert.AreEqual(2, ZoomStages.Next(0, 1f, Edges, 0.12f));
            Assert.AreEqual(4, ZoomStages.Next(0, 0.12f, Edges, 0.12f));
        }

        [Test]
        public void StagesDoNotFlickerAtTheEdges()
        {
            Assert.AreEqual(3, ZoomStages.Next(3, 0.30f, Edges, 0.12f));    // slightly past the edge, not far enough yet
            Assert.AreEqual(4, ZoomStages.Next(4, 0.34f, Edges, 0.12f));    // slightly back out, stays
            Assert.AreEqual(3, ZoomStages.Next(4, 0.40f, Edges, 0.12f));    // clearly back out, changes
        }
    }

    public class ContractTests
    {
        static void AssertValid(IGlobeModel m)
        {
            var airportIds = new HashSet<int>();
            foreach (var a in m.Airports) Assert.IsTrue(airportIds.Add(a.Id), "airport id twice: " + a.Id);
            var routeIds = new HashSet<int>();
            foreach (var r in m.Routes)
            {
                Assert.IsTrue(routeIds.Add(r.Id), "route id twice: " + r.Id);
                Assert.IsTrue(airportIds.Contains(r.FromId) && airportIds.Contains(r.ToId), "route " + r.Id + " has an unknown airport");
            }
            var flightIds = new HashSet<int>();
            foreach (var f in m.Flights)
            {
                Assert.IsTrue(flightIds.Add(f.Id), "flight id twice: " + f.Id);
                Assert.IsTrue(routeIds.Contains(f.RouteId), "flight " + f.Id + " has an unknown route");
                Assert.Greater(f.ArriveUtc, f.DepartUtc);
            }
        }

        [Test]
        public void FakeControllerDeliversAValidModelAlsoAtHighSpeedAndUnderStress()
        {
            var c = new FakeController();
            AssertValid(c);
            c.Rate = 86400;
            for (int i = 0; i < 120; i++) c.Update(0.05f);
            AssertValid(c);
            c.SetStress(true);
            AssertValid(c);
            Assert.Greater(c.Airports.Count, 200);
        }

        [Test]
        public void VersionsChangeWhenTheModelDoes()
        {
            var c = new FakeController();
            int flights = c.FlightsVersion, airports = c.AirportsVersion;
            c.Rate = 86400;
            for (int i = 0; i < 60; i++) c.Update(0.1f);
            Assert.AreNotEqual(flights, c.FlightsVersion);
            Assert.AreEqual(airports, c.AirportsVersion);
        }

        [Test]
        public void DefaultConfigIsSane()
        {
            var cfg = GlobeViewConfig.CreateDefault();
            Assert.AreEqual(4, cfg.StageEdges.Length);
            for (int i = 1; i < 4; i++) Assert.Less(cfg.StageEdges[i], cfg.StageEdges[i - 1]);
            Assert.Less(cfg.MinHeight, cfg.MaxHeight);
            UnityEngine.Object.DestroyImmediate(cfg);
        }
    }

    public class GlobeDataTests
    {
        [Test]
        public void IndexesRoutesAndFlightsAndFollowsVersions()
        {
            var c = new FakeController();
            var d = new GlobeData();
            d.Refresh(c);
            Assert.AreEqual(c.Airports.Count, d.AirportCount);
            Assert.AreEqual(c.Routes.Count, d.RouteCount);
            Assert.AreEqual(c.Flights.Count, d.FlightCount);
            Assert.IsTrue(d.AirportsChanged && d.RoutesChanged && d.FlightsChanged);
            for (int i = 0; i < d.FlightCount; i++) Assert.GreaterOrEqual(d.FlightRoute[i], 0);

            d.Refresh(c);
            Assert.IsFalse(d.AirportsChanged || d.RoutesChanged || d.FlightsChanged);   // nothing changed, nothing rebuilt

            c.Rate = 86400;
            for (int i = 0; i < 60; i++) c.Update(0.1f);
            d.Refresh(c);
            Assert.IsTrue(d.FlightsChanged);
            Assert.IsFalse(d.AirportsChanged);
        }
    }
}

namespace Tycoon.Tests
{
    /// <summary>The view's interface end to end, without a screen: a host on a fake controller in a scene without play mode.</summary>
    public class ViewInterfaceTests
    {
        FakeController controller;
        GlobeViewHost host;
        Camera cam;
        GameObject camGo;
        GlobeViewConfig config;

        [SetUp]
        public void SetUp()
        {
            camGo = new GameObject("Test camera");
            cam = camGo.AddComponent<Camera>();
            controller = new FakeController();
            controller.SetTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), 0);
            config = GlobeViewConfig.CreateDefault();
            host = GlobeViewHost.Create(config, controller, cam);
            host.SetCameraState(new CameraState(48f, 10f, 0.4f));
            Tick();
        }

        [TearDown]
        public void TearDown()
        {
            host.Dispose();
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(config);
        }

        void Tick() { for (int i = 0; i < 3; i++) host.TickImmediate(0.016f); }

        [Test]
        public void ReportsWhereAnAirportIsOnScreen()
        {
            int fra = 1;
            Assert.IsTrue(host.TryGetAirportScreenPoint(fra, out Vector2 px, out bool visible));
            Assert.IsTrue(visible);
            Assert.IsFalse(host.TryGetAirportScreenPoint(999999, out _, out _));
        }

        [Test]
        public void TappingAnAirportReportsItAndTappingTheSeaReportsAPosition()
        {
            int tappedAirport = -1; bool empty = false; float lat = 0f;
            host.AirportTapped += id => tappedAirport = id;
            host.EmptyTapped += (la, lo) => { empty = true; lat = la; };

            host.TryGetAirportScreenPoint(1, out Vector2 fra, out _);
            host.OnTap(fra + new Vector2(4f, 3f));          // near, not exactly on it
            Assert.AreEqual(1, tappedAirport);
            Assert.IsFalse(empty);

            Vector3 sea = cam.WorldToScreenPoint(GeoMath.Dir(44.5, -5.0));    // Bay of Biscay, no airport nearby
            host.OnTap(new Vector2(sea.x, sea.y));
            Assert.IsTrue(empty);
            Assert.AreEqual(44.5f, lat, 0.5f);
        }

        [Test]
        public void FocusAndFrameMoveTheCamera()
        {
            host.FocusAirport(1, 0.5f, 0f);
            Tick();
            var s = host.GetCameraState();
            Assert.AreEqual(50.04f, s.Lat, 0.01f);
            Assert.AreEqual(0.5f, s.Height, 0.001f);

            // Frankfurt to New York: after framing both ends are on the near side and inside the picture
            host.FrameRoute(100, 0f);       // FRA - JFK is the first route of the fake controller
            Tick();
            foreach (var iata in new[] { 1, 32 })
            {
                host.TryGetAirportScreenPoint(iata, out Vector2 px, out bool visible);
                Assert.IsTrue(visible, "airport " + iata + " not in view");
            }
        }

        [Test]
        public void ViewportInsetsMoveTheGlobeCentre()
        {
            host.SetViewportInsets(0, 0, 0, 400f);          // a panel covers the right 400 px
            Tick();
            float x = cam.WorldToViewportPoint(Vector3.zero).x;
            Assert.AreEqual(0.5f - 400f / (2f * Mathf.Max(Screen.width, 1)), x, 0.01f);
        }

        [Test]
        public void ZoomLimitsAndInputSwitchesHold()
        {
            host.SetZoomLimits(1f, 2f);
            Assert.AreEqual(1f, host.GetCameraState().Height, 1e-4f);     // 0.4 is pushed up to the new minimum
            host.SetCameraState(new CameraState(0f, 0f, 9f));
            Assert.AreEqual(2f, host.GetCameraState().Height, 1e-4f);
            host.SetInputEnabled(false, false, false);                     // no exception, nothing to see without a finger
            host.SetActive(false);
            Assert.IsFalse(cam.enabled);
            host.SetActive(true);
            Assert.IsTrue(cam.enabled);
        }

        [Test]
        public void ZoomStageFollowsTheHeightAndTheTier()
        {
            host.SetQualityTier(QualityTier.High);
            host.SetCameraState(new CameraState(48f, 10f, 0.2f));
            Tick();
            Assert.AreEqual(4, host.ZoomStage);
            host.SetQualityTier(QualityTier.Low);                           // Low shows at most Z2 detail
            Tick();
            Assert.AreEqual(2, host.ZoomStage);
            Assert.AreEqual(4, host.RawZoomStage);
            host.SetCameraState(new CameraState(48f, 10f, 4f));
            Tick();
            Assert.AreEqual(0, host.ZoomStage);
        }
    }
}

namespace Tycoon.Tests
{
    public class SkyAndMotionTests
    {
        GameObject camGo;
        GlobeViewConfig config;
        GlobeViewHost host;
        FakeController controller;

        void Create(Action<GlobeViewConfig> tweak = null, double rate = 0)
        {
            camGo = new GameObject("Test camera");
            var cam = camGo.AddComponent<Camera>();
            config = GlobeViewConfig.CreateDefault();
            tweak?.Invoke(config);
            controller = new FakeController();
            controller.SetTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), rate);
            host = GlobeViewHost.Create(config, controller, cam);
            for (int i = 0; i < 3; i++) host.TickImmediate(0.016f);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) host.Dispose();
            if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
            host = null; camGo = null; config = null;
        }

        [Test]
        public void SunFollowsTheGameTimeOrStaysWhereTheConfigPutsIt()
        {
            Create();
            Vector4 sun = Shader.GetGlobalVector("_TycoonSunDir");
            GeoMath.LatLon(new Vector3(sun.x, sun.y, sun.z), out float lat, out _);
            Assert.AreEqual(-12.3f, lat, 1f);
            TearDown();

            Create(c => { c.Sun = SunMode.Fixed; c.FixedSunLat = 10f; c.FixedSunLon = 20f; });
            sun = Shader.GetGlobalVector("_TycoonSunDir");
            Vector3 expected = GeoMath.Dir(10, 20);
            Assert.AreEqual(expected.x, sun.x, 1e-4f);
            Assert.AreEqual(expected.y, sun.y, 1e-4f);
        }

        [Test]
        public void DayAndNightCalmDownAtHighGameSpeedOnly()
        {
            Create(rate: 600);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_TycoonCalm"), 1e-4f);
            TearDown();

            Create(rate: 21600);                                        // 6 h/s: day and night still change visibly
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_TycoonCalm"), 1e-4f);
            TearDown();

            Create(rate: 86400);
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_TycoonCalm"), 1e-4f);
            TearDown();

            Create(c => c.ReduceMotion = true, rate: 21600);            // reduced motion calms down earlier
            Assert.Greater(Shader.GetGlobalFloat("_TycoonCalm"), 0.5f);
        }

        [Test]
        public void FastForwardLookOnlyAtTheHighestSpeed()
        {
            var config = GlobeViewConfig.CreateDefault();
            var rewind = new RewindEffect(config);
            var camGo = new GameObject("rewind camera");
            try
            {
                for (int i = 0; i < 10; i++) rewind.Tick(21600, 0.1f, camGo.transform);       // 6 h/s
                Assert.AreEqual(0f, rewind.Strength);
                Assert.IsFalse(rewind.OverlayVisible);
                Assert.AreEqual(Quaternion.identity, camGo.transform.rotation);

                for (int i = 0; i < 10; i++) rewind.Tick(86400, 0.1f, camGo.transform);       // 1 d/s
                Assert.AreEqual(1f, rewind.Strength, 1e-4f);
                Assert.IsTrue(rewind.OverlayVisible);
                Assert.AreNotEqual(Quaternion.identity, camGo.transform.rotation);            // the picture trembles

                for (int i = 0; i < 10; i++) rewind.Tick(600, 0.1f, camGo.transform);         // back to normal
                Assert.IsFalse(rewind.OverlayVisible);
            }
            finally
            {
                rewind.Destroy();
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ReducedMotionSwitchesTheFastForwardLookOff()
        {
            var config = GlobeViewConfig.CreateDefault();
            config.ReduceMotion = true;
            var rewind = new RewindEffect(config);
            var camGo = new GameObject("rewind camera");
            try
            {
                for (int i = 0; i < 10; i++) rewind.Tick(86400, 0.1f, camGo.transform);
                Assert.AreEqual(0f, rewind.Strength);
            }
            finally
            {
                rewind.Destroy();
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        static int ActiveChildren(Transform t)
        {
            int n = 0;
            foreach (Transform c in t) if (c.gameObject.activeSelf) n++;
            return n;
        }

        [Test]
        public void PlanesFollowTheTimetableAndTurnIntoFlowAtHighSpeed()
        {
            var c = new FakeController();
            c.SetTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), 600);
            var data = new GlobeData();
            data.Refresh(c);
            var flat = new Material(Resources.Load<Shader>("Shaders/Flat"));
            var root = new GameObject("Flights").transform;
            var layer = new FlightLayer(root, MeshFactory.Airplane(), flat, GlobeViewConfig.CreateDefault());
            var visible = new bool[data.RouteCount];
            for (int i = 0; i < visible.Length; i++) visible[i] = true;

            int inAir = 0;
            for (int i = 0; i < data.FlightCount; i++)
                if (c.Time.UtcSeconds >= data.Flights[i].DepartUtc && c.Time.UtcSeconds < data.Flights[i].ArriveUtc) inAir++;
            Assert.Greater(inAir, 0);

            layer.Tick(data, visible, c.Time, 0f, 0.3f, 4);
            int shown = ActiveChildren(root);
            Assert.Greater(shown, 0);
            Assert.LessOrEqual(shown, inAir);                            // never more planes than flights in the air

            layer.Tick(data, visible, new GameTimeState(c.Time.UtcSeconds, 86400), 0f, 0.3f, 4);
            int flow = ActiveChildren(root);
            Assert.Greater(flow, 0);
            Assert.LessOrEqual(flow, data.RouteCount * 4);

            foreach (Transform plane in root)
                if (plane.gameObject.activeSelf) Assert.That(plane.position.magnitude, Is.InRange(1.0f, 1.2f));   // on its arc, above the surface

            UnityEngine.Object.DestroyImmediate(root.gameObject);
            UnityEngine.Object.DestroyImmediate(flat);
        }
    }
}

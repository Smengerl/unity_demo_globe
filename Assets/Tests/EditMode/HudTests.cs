using System.IO;
using NUnit.Framework;
using Tycoon.Demo;
using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using Tycoon.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace Tycoon.Tests
{
    public class HudTests
    {
        // ---- texts ----

        [Test]
        public void DateAndClockAreShownInGameTime()
        {
            double t = new System.DateTimeOffset(2026, 10, 24, 11, 20, 0, System.TimeSpan.Zero).ToUnixTimeSeconds();
            Assert.AreEqual("SA 24.10.", HudFormat.Date(t));
            Assert.AreEqual("11:20", HudFormat.Clock(t));
        }

        [TestCase(12_400_000.0, "12.4", "M €")]
        [TestCase(845_000.0, "845", "K €")]
        [TestCase(1_234_000_000.0, "1.23", "B €")]
        [TestCase(950.0, "950", "€")]
        [TestCase(-3_100_000.0, "−3.10", "M €")]
        public void CashHasThreeDigitsAndAUnit(double value, string number, string unit)
        {
            HudFormat.Cash(value, out string n, out string u);
            Assert.AreEqual(number, n);
            Assert.AreEqual(unit, u);
        }

        [Test]
        public void CashChangeShowsSignAndUnit()
        {
            Assert.AreEqual("+38.2K/day", HudFormat.Delta(38_200));
            Assert.AreEqual("−12.6K/day", HudFormat.Delta(-12_600));
        }

        // ---- tokens, sprites, icons ----

        [Test]
        public void TokensMatchTheJsonFile()
        {
            string path = Path.Combine(Application.dataPath, "..", "docs", "hud-design", "hud-tokens.json");
            if (!File.Exists(path)) Assert.Ignore("hud-tokens.json is not part of this checkout");
            string json = File.ReadAllText(path).ToUpperInvariant();
            foreach (var hex in HudTokens.Hex) Assert.IsTrue(json.Contains(hex), hex + " is missing in hud-tokens.json");
        }

        [Test]
        public void EveryIconDrawsSomething()
        {
            foreach (HudIcon icon in System.Enum.GetValues(typeof(HudIcon)))
            {
                var tex = HudIcons.Get(icon).texture;
                int opaque = 0;
                foreach (var p in tex.GetPixels32()) if (p.a > 128) opaque++;
                Assert.Greater(opaque, 60, icon + " looks empty");
                Assert.Less(opaque, tex.width * tex.height / 2, icon + " looks filled");
            }
        }

        [Test]
        public void RoundedSpriteIsRoundAndSliced()
        {
            var s = HudSprites.Rounded(8);
            Assert.AreEqual(16f, s.border.x);
            var px = s.texture.GetPixels32();
            int n = s.texture.width;
            Assert.AreEqual(0, px[0].a);                        // the corner is cut off
            Assert.AreEqual(255, px[n / 2 * n + n / 2].a);      // the middle is solid
        }

        // ---- flip cards ----

        [Test]
        public void FlipRowShowsTextAndFolds()
        {
            var root = new GameObject("flip test", typeof(RectTransform)).transform;
            try
            {
                var row = new FlipRow(root, "Time", FlipSize.Time, HudFonts.Cond, false, 30);
                row.Root.sizeDelta = new Vector2(130, 42);
                row.SetText("11:20", 0f, false);
                Assert.AreEqual("11:20", row.Text);
                row.SetText("11:21", 5f, true);
                for (int i = 0; i < 40; i++) row.Tick(0.016f);          // folding done after 220 ms (plus stagger)
                Assert.AreEqual("11:21", row.Text);
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void FastChangingDigitsStandStill()
        {
            var root = new GameObject("flip test", typeof(RectTransform)).transform;
            try
            {
                var row = new FlipRow(root, "Time", FlipSize.Time, HudFonts.Cond, false, 30);
                row.Root.sizeDelta = new Vector2(130, 42);
                row.SetText("10:00", 0f, false);
                // the last digit changes ten times a second: after three changes within a second it is dimmed and shown at once
                for (int i = 1; i <= 6; i++) row.SetText("10:0" + i, i * 0.1f, true);
                bool anyDim = false;
                foreach (var t in row.Root.GetComponentsInChildren<Text>()) if (t.color.a < 0.5f) anyDim = true;
                Assert.IsTrue(anyDim);
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        // ---- the HUD ----

        class Rig : System.IDisposable
        {
            public readonly GameObject CamGo = new GameObject("Test camera");
            public readonly GlobeViewConfig Config = GlobeViewConfig.CreateDefault();
            public readonly FakeController Controller = new FakeController();
            public readonly GlobeViewHost Host;
            public readonly GateChartHud Hud;

            public Rig()
            {
                var cam = CamGo.AddComponent<Camera>();
                Host = GlobeViewHost.Create(Config, Controller, cam);
                Hud = new GateChartHud(Host, Controller, Controller, () => Host.ZoomStage);
            }

            public void Tick(int frames = 3)
            {
                for (int i = 0; i < frames; i++) { Host.TickImmediate(0.016f); Hud.Tick(0.016f); }
            }

            public void Dispose()
            {
                Hud.Dispose();
                Host.Dispose();
                Object.DestroyImmediate(GameObject.Find("EventSystem"));
                Object.DestroyImmediate(CamGo);
            }
        }

        [Test]
        public void HudShowsTimeAndCashOfTheGame()
        {
            using (var rig = new Rig())
            {
                double t = new System.DateTimeOffset(2026, 10, 24, 11, 20, 0, System.TimeSpan.Zero).ToUnixTimeSeconds();
                rig.Controller.SetTime(t, 0);
                rig.Tick();
                Assert.AreEqual("11:20", rig.Hud.TimeText);
                Assert.AreEqual("SA 24.10.", rig.Hud.DateText);
                Assert.AreEqual("12.4", rig.Hud.CashText);
            }
        }

        [Test]
        public void SpeedButtonsSetTheGameSpeed()
        {
            using (var rig = new Rig())
            {
                double[] expected = { 0, 600, 3600, 21600, 86400 };
                Assert.AreEqual(expected.Length, rig.Hud.SpeedCount);
                for (int i = 0; i < expected.Length; i++)
                {
                    rig.Hud.SpeedButton(i).onClick.Invoke();
                    Assert.AreEqual(expected[i], rig.Controller.Rate, "button " + i);
                }
            }
        }

        [Test]
        public void SpeedHighlightSlidesToTheChosenSpeed()
        {
            using (var rig = new Rig())
            {
                rig.Controller.SetRate(600);
                rig.Tick();
                Assert.AreEqual(GateChartHud.SliderTarget(1), rig.Hud.SliderX, 0.01f);      // starts in place, no glide
                rig.Hud.SpeedButton(3).onClick.Invoke();                                      // 6 h/s
                rig.Tick(1);
                float target = GateChartHud.SliderTarget(3);
                Assert.Greater(rig.Hud.SliderX, GateChartHud.SliderTarget(1));                // on its way
                Assert.Less(rig.Hud.SliderX, target);
                rig.Tick(80);
                Assert.AreEqual(target, rig.Hud.SliderX, 0.5f);                               // arrived
            }
        }

        [Test]
        public void SelectionSheetFollowsTheSelection()
        {
            using (var rig = new Rig())
            {
                rig.Tick();
                Assert.IsFalse(rig.Hud.SheetVisible);
                rig.Controller.SelectAirport(1);                    // FRA
                rig.Tick();
                Assert.IsTrue(rig.Hud.SheetVisible);
                Assert.AreEqual("FRA", rig.Hud.SheetCode);
                rig.Controller.ClearSelection();
                rig.Tick();
                Assert.IsFalse(rig.Hud.SheetVisible);
            }
        }

        [Test]
        public void LabelsAppearFromZ3OnAndFollowTheAirports()
        {
            using (var rig = new Rig())
            {
                rig.Host.SetCameraState(new CameraState(48f, 10f, 0.4f));      // Z3
                rig.Tick();
                var labels = GameObject.Find("Labels").transform;
                int on = 0;
                foreach (Transform t in labels) if (t.gameObject.activeSelf) on++;
                Assert.Greater(on, 5);

                rig.Host.SetCameraState(new CameraState(48f, 10f, 4f));        // Z0: no labels
                rig.Tick();
                on = 0;
                foreach (Transform t in labels) if (t.gameObject.activeSelf) on++;
                Assert.AreEqual(0, on);
            }
        }

        [Test]
        public void DelayAnnouncesANotice()
        {
            var controller = new FakeController();
            HudToastKind? kind = null;
            string text = null;
            controller.Notice += (k, s) => { kind = k; text = s; };
            controller.SetTime(controller.Time.UtcSeconds, 0);
            for (int i = 0; i < 20 && kind == null; i++) controller.Update(16f);    // one delay every 15 s
            Assert.AreEqual(HudToastKind.Warning, kind);
            StringAssert.Contains("+15 min", text);
        }
    }
}

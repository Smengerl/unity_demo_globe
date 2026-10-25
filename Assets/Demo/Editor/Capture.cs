using System;
using System.Globalization;
using System.IO;
using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using Tycoon.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tycoon.Demo.EditorTools
{
    /// <summary>
    /// Screenshot without play mode (works while the editor is closed, in batch mode):
    /// Unity -batchmode -projectPath . -executeMethod Tycoon.Demo.EditorTools.Capture.Run -tycoonShot out.png
    ///   [-lat 50 -lon 10 -height 3] [-utc 2026-10-24T12:00:00] [-rate 600] [-tier low|medium|high] [-time 0] [-stress] [-hud [-select 1]] [-w 1280 -h 720]
    /// -hud draws the game HUD into the picture (the canvas scales to the picture width / 390), -select picks an airport by id.
    /// -time freezes the twinkle clock at a second; -rate is the game speed (try 86400 for the calm / traffic-flow mode).
    /// </summary>
    public static class Capture
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static float F(string name, float fallback) =>
            float.TryParse(Arg(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static void Run()
        {
            try { Render(); }
            catch (Exception e) { Debug.LogError("Capture failed: " + e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        static void Render()
        {
            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath);
            var cam = Camera.main;

            var controller = new FakeController();
            if (Flag("-stress")) controller.SetStress(true);
            double unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var utc = Arg("-utc");
            if (utc != null)
                unix = new DateTimeOffset(DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)).ToUnixTimeSeconds();
            controller.SetTime(unix, F("-rate", 0f));

            var host = GlobeViewHost.Create(GlobeViewConfig.CreateDefault(), controller, cam);
            var tier = Arg("-tier");
            if (tier != null) host.SetQualityTier((QualityTier)Enum.Parse(typeof(QualityTier), tier, true));
            host.SetCameraState(new CameraState(F("-lat", 50f), F("-lon", 10f), F("-height", 3f)));
            host.RenderOverlaysInto(cam);
            for (int i = 0; i < 3; i++) host.TickImmediate(0.016f);       // zoom stage, keywords, tile window (built synchronously here)
            if (F("-rate", 0f) >= 86400f) for (int i = 0; i < 4; i++) host.TickImmediate(0.2f);     // the fast-forward look fades in
            if (Arg("-time") != null) Shader.SetGlobalFloat("_TycoonTime", F("-time", 0f));

            int w = (int)F("-w", 1280), h = (int)F("-h", 720);
            GateChartHud hud = null;
            if (Flag("-hud"))
            {
                if (Arg("-select") != null) controller.SelectAirport((int)F("-select", 1));
                hud = new GateChartHud(host, controller, controller, () => host.ZoomStage);
                hud.UseCamera(cam, w / 390f);
                hud.ShowToast(HudToastKind.Info, "Strecke FRA – VIE in Betrieb");
                for (int i = 0; i < 4; i++) { host.TickImmediate(0.016f); hud.Tick(0.016f); Canvas.ForceUpdateCanvases(); }
            }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Arg("-tycoonShot"), tex.EncodeToPNG());
            Debug.Log("Capture written: " + Arg("-tycoonShot"));
            if (hud != null) hud.Dispose();
            host.Dispose();
        }
    }
}

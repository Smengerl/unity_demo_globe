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
    ///   [-lat 50 -lon 10 -height 3] [-utc 2026-10-24T12:00:00] [-rate 600] [-tier low|medium|high] [-time 0] [-stress] [-hud [-select 1]] [-movie zoom|hud -frames 90 -fps 15 -outdir dir] [-w 1280 -h 720]
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

            int w = (int)F("-w", 1280), h = (int)F("-h", 720);
            GateChartHud hud = null;
            if (Flag("-hud") || Arg("-movie") == "hud")
            {
                hud = new GateChartHud(host, controller, controller, () => host.ZoomStage);
                hud.UseCamera(cam, w / 390f);
            }
            if (hud != null && Arg("-select") != null) controller.SelectAirport((int)F("-select", 1));
            hud?.ShowToast(HudToastKind.Info, "Route FRA – VIE is open");

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            void Step(float dt, bool sync)
            {
                controller.Update(dt);
                host.TickImmediate(dt);
                if (hud != null) { hud.Tick(dt); Canvas.ForceUpdateCanvases(); }
            }

            byte[] Frame(bool png)
            {
                // Markers, routes and planes draw in the transparent queue (3000-3002), the screen-space canvases at 3000 by distance:
                // move the 3D parts in front of them so the HUD ends up on top, as the overlay canvases do in the game.
                foreach (var r in host.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.renderQueue >= 3000 && m.renderQueue <= 3002) m.renderQueue -= 20;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                return png ? tex.EncodeToPNG() : tex.EncodeToJPG(92);
            }

            string movie = Arg("-movie");
            if (movie == null)
            {
                for (int i = 0; i < 4; i++) Step(0.016f, true);       // zoom stage, keywords, tile window (built synchronously here)
                if (F("-rate", 0f) >= 86400f) for (int i = 0; i < 4; i++) Step(0.2f, true);     // the fast-forward look fades in
                if (Arg("-time") != null) Shader.SetGlobalFloat("_TycoonTime", F("-time", 0f));
                File.WriteAllBytes(Arg("-tycoonShot"), Frame(true));
                Debug.Log("Capture written: " + Arg("-tycoonShot"));
            }
            else
            {
                int frames = (int)F("-frames", 90);
                float fps = F("-fps", 15f), dt = 1f / fps;
                string dir = Arg("-outdir");
                Directory.CreateDirectory(dir);
                for (int i = 0; i < 3; i++) Step(0.016f, true);
                for (int i = 0; i < frames; i++)
                {
                    float t = i * dt, u = frames > 1 ? i / (float)(frames - 1) : 0f, e = Mathf.SmoothStep(0f, 1f, u);
                    if (movie == "zoom")
                    {
                        float height = Mathf.Exp(Mathf.Lerp(Mathf.Log(4.2f), Mathf.Log(0.3f), e));
                        host.SetCameraState(new CameraState(Mathf.Lerp(22f, 47.5f, e), Mathf.Lerp(24f, 10.5f, e), height));
                    }
                    else
                    {
                        host.SetCameraState(new CameraState(46f, Mathf.Lerp(2f, 16f, u), 2.4f));
                        if (i == 0) controller.Rate = 600;
                        if (Mathf.Abs(t - 1.5f) < dt / 2) controller.Rate = 3600;
                        if (Mathf.Abs(t - 3.0f) < dt / 2) controller.Rate = 21600;
                        if (Mathf.Abs(t - 4.5f) < dt / 2) controller.Rate = 86400;
                        if (Mathf.Abs(t - 7.0f) < dt / 2) { controller.Rate = 600; controller.SelectAirport(1); }
                        if (Mathf.Abs(t - 9.0f) < dt / 2) controller.Rate = 0;
                    }
                    Step(dt, true);
                    File.WriteAllBytes(Path.Combine(dir, $"frame_{i + 1:0000}.jpg"), Frame(false));
                }
                Debug.Log("Movie frames written: " + dir);
            }
            if (hud != null) hud.Dispose();
            host.Dispose();
        }
    }
}

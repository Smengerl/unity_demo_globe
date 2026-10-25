using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using Tycoon.Hud;
using UnityEngine;

namespace Tycoon.Demo
{
    /// <summary>
    /// The demo scene's entry point: puts a <see cref="FakeController"/> behind a <see cref="GlobeViewHost"/> and adds the HUD.
    /// This is the pattern the real game follows with its own controller.
    /// </summary>
    public sealed class DemoMain : MonoBehaviour
    {
        FakeController controller;
        GlobeViewHost host;
        GateChartHud hud;
        DemoDevPanel dev;

        void Start()
        {
            var cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();

            controller = new FakeController();
            host = GlobeViewHost.Create(GlobeViewConfig.CreateDefault(), controller, cam);
            hud = new GateChartHud(host, controller, controller, () => host.ZoomStage);
            dev = new DemoDevPanel(host, controller);

            host.AirportTapped += controller.SelectAirport;
            host.EmptyTapped += (lat, lon) => controller.ClearSelection();
            host.SetPointerBlocker(p => hud.IsOverUi(p) || dev.IsOverUi(p));
            hud.ShowToast(HudToastKind.Info, "Strecke FRA – VIE in Betrieb");
        }

        void Update()
        {
            controller.Update(Time.unscaledDeltaTime);
            hud.Tick(Time.unscaledDeltaTime);
            dev.Update();
        }

        void OnDestroy()
        {
            if (hud != null) hud.Dispose();
            if (host != null) host.Dispose();
        }
    }
}

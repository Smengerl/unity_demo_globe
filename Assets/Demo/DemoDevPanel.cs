using System;
using System.Collections.Generic;
using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Tycoon.Demo
{
    /// <summary>
    /// Developer tools of the demo, hidden until F1 is pressed: view quality, stress test, camera info and a few of the view's commands.
    /// The game's own HUD is <see cref="Tycoon.Hud.GateChartHud"/>.
    /// </summary>
    internal sealed class DemoDevPanel
    {
        static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.13f, 0.9f);

        readonly GlobeViewHost host;
        readonly FakeController controller;
        readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        readonly GameObject panel;
        readonly Text infoText, qualityLabel, stressLabel;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        PointerEventData pointer;
        CameraState camera;
        bool insetsOn, inputLocked;

        public DemoDevPanel(GlobeViewHost host, FakeController controller)
        {
            this.host = host;
            this.controller = controller;

            var canvasGo = new GameObject("Demo dev panel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -10);
            rt.sizeDelta = new Vector2(430, 150);
            panel.GetComponent<Image>().color = PanelColor;

            qualityLabel = ButtonLabel(MakeButton("", new Vector2(10, -10), new Vector2(150, 32), CycleQuality));
            stressLabel = ButtonLabel(MakeButton("", new Vector2(170, -10), new Vector2(150, 32), () => controller.SetStress(!controller.Stress)));
            MakeButton("Focus FRA", new Vector2(330, -10), new Vector2(90, 32), () => host.FocusAirport(1));
            infoText = MakeLabel("", new Vector2(10, -52), new Vector2(410, 22), 14);
            MakeButton("Panel offset", new Vector2(10, -82), new Vector2(150, 32), ToggleInsets);
            MakeButton("Lock input", new Vector2(170, -82), new Vector2(150, 32), ToggleInput);
            MakeLabel("F1 hides this panel", new Vector2(10, -122), new Vector2(410, 22), 12);

            host.CameraChanged += s => camera = s;
            panel.SetActive(false);
        }

        public bool Visible => panel.activeSelf;

        public bool IsOverUi(Vector2 screenPos)
        {
            if (!panel.activeSelf || EventSystem.current == null) return false;
            if (pointer == null) pointer = new PointerEventData(EventSystem.current);
            pointer.position = screenPos;
            hits.Clear();
            EventSystem.current.RaycastAll(pointer, hits);
            foreach (var h in hits) if (h.gameObject.transform.IsChildOf(panel.transform)) return true;
            return false;
        }

        public void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) panel.SetActive(!panel.activeSelf);
            if (!panel.activeSelf) return;
            qualityLabel.text = $"Quality: {host.Tier}";
            stressLabel.text = controller.Stress ? "Stress test: on" : "Stress test: off";
            infoText.text = $"Zoom Z{host.RawZoomStage} (Detail Z{host.ZoomStage})   {camera.Lat:0.0}°, {camera.Lon:0.0}°, Height {camera.Height:0.00}";
        }

        void CycleQuality()
        {
            var next = host.Tier == QualityTier.Low ? QualityTier.Medium : host.Tier == QualityTier.Medium ? QualityTier.High : QualityTier.Low;
            host.SetQualityTier(next);
        }

        void ToggleInsets()
        {
            insetsOn = !insetsOn;
            host.SetViewportInsets(0, 0, 0, insetsOn ? Screen.width * 0.35f : 0f);
        }

        void ToggleInput()
        {
            inputLocked = !inputLocked;
            host.SetInputEnabled(!inputLocked, !inputLocked, !inputLocked);
        }

        Text MakeLabel(string text, Vector2 pos, Vector2 size, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(string label, Vector2 pos, Vector2 size, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.25f, 0.3f, 0.4f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => onClick());
            var text = MakeLabel(label, Vector2.zero, size, 14);
            text.transform.SetParent(go.transform, false);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        static Text ButtonLabel(Button b) => b.GetComponentInChildren<Text>();
    }
}

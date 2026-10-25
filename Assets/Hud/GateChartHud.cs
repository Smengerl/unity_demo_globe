using System;
using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tycoon.Hud
{
    /// <summary>
    /// The game's HUD in the "Gate Chart" design (uGUI, built in code): top pill with date, time and cash as flip cards, speed pill,
    /// notices, a selection sheet for the chosen airport, the gate bar at the bottom and airport labels on the globe.
    /// It reads the game through <see cref="IHudModel"/> and <see cref="IGlobeModel"/> and talks to the globe through <see cref="IGlobeView"/>.
    /// Call <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class GateChartHud
    {
        static readonly double[] Rates = { 0, 600, 3600, 21600, 86400 };
        static readonly string[] RateLabels = { "", "10m", "1h", "6h", "1d" };
        static readonly string[] TabNames = { "KARTE", "FLUGHÄFEN", "STRECKEN", "FLOTTE", "FINANZEN" };
        static readonly HudIcon[] TabIcons = { HudIcon.Globe, HudIcon.Tower, HudIcon.Route, HudIcon.Plane, HudIcon.Chart };

        const float SpeedY = HudTokens.SafeTop + HudTokens.TopPillHeight + HudTokens.StackGap;
        const float ToastY = SpeedY + HudTokens.SpeedPillHeight + HudTokens.StackGap;
        const float SheetBottom = HudTokens.SafeBottom + HudTokens.DockHeight + 12f;

        readonly IGlobeView view;
        readonly IGlobeModel model;
        readonly IHudModel game;
        readonly Func<int> zoomStage;
        readonly bool reduceMotion;

        readonly Canvas canvas;
        readonly CanvasScaler scaler;
        readonly RectTransform column, labelRoot;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        PointerEventData pointer;
        bool cameraMode;
        float now;

        // top pill
        FlipRow dateRow, timeRow, cashRow;
        Text deltaText;
        string lastUnit;

        // speed
        readonly Image[] speedBg = new Image[Rates.Length];
        readonly Text[] speedText = new Text[Rates.Length];
        readonly Image[] speedIcon = new Image[Rates.Length];

        // notice
        RectTransform toast;
        Image toastIcon;
        Text toastText;
        float toastTimer;

        // sheet
        RectTransform sheet;
        Text codeText, nameText, kindText, leftCode, rightCode, caption;
        Image kindDot;
        readonly Text[] stat = new Text[3];
        readonly Image[] gauge = new Image[10];
        RectTransform strip;
        int sheetAirport = int.MinValue, stripRoute = -1;

        // dock
        readonly Image[] tabRing = new Image[5];
        readonly Text[] tabLabel = new Text[5];
        readonly Image[] tabIcon = new Image[5];
        int tab;

        // labels
        readonly List<Text> labels = new List<Text>();
        float insetTop = -1f, insetBottom = -1f;

        public Canvas Canvas => canvas;
        internal string TimeText => timeRow.Text;
        internal string DateText => dateRow.Text;
        internal string CashText => cashRow.Text;
        internal bool SheetVisible => sheet.gameObject.activeSelf;
        internal string SheetCode => codeText.text;
        internal Button SpeedButton(int index) => speedBg[index].GetComponent<Button>();
        internal int SpeedCount => Rates.Length;

        public GateChartHud(IGlobeView view, IGlobeModel model, IHudModel game, Func<int> zoomStage, bool reduceMotion = false)
        {
            this.view = view;
            this.model = model;
            this.game = game;
            this.zoomStage = zoomStage;
            this.reduceMotion = reduceMotion;

            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var go = new GameObject("Gate Chart HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0f;

            labelRoot = HudKit.Node("Labels", go.transform).Stretch();
            column = HudKit.Node("Column", go.transform);
            column.anchorMin = new Vector2(0.5f, 0f);
            column.anchorMax = new Vector2(0.5f, 1f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(HudTokens.ColumnWidth, 0f);
            column.anchoredPosition = Vector2.zero;

            BuildTopPill();
            BuildSpeedPill();
            BuildToast();
            BuildSheet();
            BuildDock();

            game.Notice += ShowToast;
            Refresh(false);
            sheet.gameObject.SetActive(false);
        }

        /// <summary>Renders into a camera instead of the screen (screenshot tool, tests); <paramref name="scale"/> is texels per canvas unit.</summary>
        public void UseCamera(Camera cam, float scale)
        {
            cameraMode = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = scale;
            Canvas.ForceUpdateCanvases();
        }

        public void Dispose()
        {
            game.Notice -= ShowToast;
            if (canvas != null) HudKit.Discard(canvas.gameObject);
        }

        public bool IsOverUi(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            if (pointer == null) pointer = new PointerEventData(EventSystem.current);
            pointer.position = screenPos;
            hits.Clear();
            EventSystem.current.RaycastAll(pointer, hits);
            foreach (var h in hits) if (h.gameObject != null && h.gameObject.transform.IsChildOf(canvas.transform)) return true;
            return false;
        }

        public void ShowToast(HudToastKind kind, string text)
        {
            var (icon, color) = kind switch
            {
                HudToastKind.Success => (HudIcon.Check, HudTokens.Positive),
                HudToastKind.Warning => (HudIcon.Warning, HudTokens.Warning),
                HudToastKind.Error => (HudIcon.Error, HudTokens.Negative),
                _ => (HudIcon.Route, HudTokens.Info),
            };
            toastIcon.sprite = HudIcons.Get(icon);
            toastIcon.color = color;
            toastText.text = text;
            toast.sizeDelta = new Vector2(44f + toastText.preferredWidth + 16f, 40f);
            toast.gameObject.SetActive(true);
            toastTimer = HudTokens.ToastSeconds;
        }

        // ---- per frame ----

        public void Tick(float realDeltaTime)
        {
            now += realDeltaTime;
            if (!cameraMode) scaler.matchWidthOrHeight = Screen.width * 844f < Screen.height * 390f ? 0f : 1f;

            Refresh(!reduceMotion);
            dateRow.Tick(realDeltaTime);
            timeRow.Tick(realDeltaTime);
            cashRow.Tick(realDeltaTime);

            if (toast.gameObject.activeSelf)
            {
                toastTimer -= realDeltaTime;
                if (toastTimer <= 0f) toast.gameObject.SetActive(false);
            }

            UpdateSheet();
            UpdateInsets();
            UpdateLabels();
        }

        void Refresh(bool animate)
        {
            var t = game.Time;
            dateRow.SetText(HudFormat.Date(t.UtcSeconds), now, animate);
            timeRow.SetText(HudFormat.Clock(t.UtcSeconds), now, animate);

            HudFormat.Cash(game.Cash, out string number, out string unit);
            bool negative = game.Cash < 0;
            cashRow.SetText(number, now, animate, negative);
            if (unit != lastUnit || negative)
            {
                lastUnit = unit;
                cashRow.SetSuffix(unit, 24, negative ? HudTokens.Negative : HudTokens.TextPrimary);
            }
            deltaText.text = HudFormat.Delta(game.CashPerDay);
            deltaText.color = game.CashPerDay < 0 ? HudTokens.Negative : HudTokens.Positive;

            for (int i = 0; i < Rates.Length; i++)
            {
                bool on = Math.Abs(t.Rate - Rates[i]) < 1e-6;
                speedBg[i].color = on ? HudTokens.Accent : new Color(0f, 0f, 0f, 0f);
                var fg = on ? HudTokens.TextOnAccent : HudTokens.TextPrimary;
                if (speedText[i] != null) speedText[i].color = fg;
                if (speedIcon[i] != null) speedIcon[i].color = fg;
            }
        }

        // ---- building: top pill and speed pill ----

        void BuildTopPill()
        {
            var pill = HudKit.Card(column, "Top pill", 24, HudTokens.SurfacePanel, HudTokens.BorderSubtle).Top(0, HudTokens.SafeTop, HudTokens.ColumnWidth, HudTokens.TopPillHeight);

            dateRow = new FlipRow(pill, "Date", FlipSize.Date, HudFonts.Cond, false, 18);
            dateRow.Root.Top(16, 12, 128, 26);
            timeRow = new FlipRow(pill, "Time", FlipSize.Time, HudFonts.Cond, false, 30);
            timeRow.Root.Top(16, 44, 130, 42);

            HudKit.Tiled(pill, "Divider", HudSprites.DashV(), HudTokens.BorderSubtle).rectTransform.Top(156, 12, 2, 74);

            HudKit.Txt(pill, "Cash label", "KASSE", 13, HudTokens.TextSecondary, HudFonts.Cond, TextAnchor.MiddleLeft).rectTransform.Top(172, 12, 60, 26);
            deltaText = HudKit.Txt(pill, "Cash change", "", 14, HudTokens.Positive, HudFonts.Cond, TextAnchor.MiddleRight);
            deltaText.rectTransform.TopRight(16, 12, 110, 26);
            cashRow = new FlipRow(pill, "Cash", FlipSize.Time, HudFonts.Cond, true, 30);
            cashRow.Root.Top(172, 44, 170, 42);
        }

        void BuildSpeedPill()
        {
            var pill = HudKit.Card(column, "Speed pill", 28, HudTokens.SurfacePanel, HudTokens.BorderSubtle).Top(0, SpeedY, HudTokens.ColumnWidth, HudTokens.SpeedPillHeight);
            float w = (HudTokens.ColumnWidth - 8f - 16f) / Rates.Length;
            for (int i = 0; i < Rates.Length; i++)
            {
                int index = i;
                var rt = HudKit.Node("Speed " + Rates[i], pill).Top(4f + i * (w + 4f), 4, w, 48);
                var bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = HudSprites.Rounded(24);
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0f, 0f, 0f, 0f);
                speedBg[i] = bg;
                HudKit.Tappable(rt, () => game.SetRate(Rates[index]));
                if (i == 0) speedIcon[i] = HudKit.Icon(rt, "Pause", HudIcon.Pause, HudTokens.TextPrimary, 26);
                else speedText[i] = HudKit.Txt(rt, "Label", RateLabels[i], 20, HudTokens.TextPrimary, HudFonts.Cond, TextAnchor.MiddleCenter);
                if (i == 0) speedIcon[i].rectTransform.Center(26, 26);
                else speedText[i].rectTransform.Stretch();
            }
        }

        void BuildToast()
        {
            toast = HudKit.Card(column, "Notice", 16, HudTokens.SurfacePanel, HudTokens.BorderSubtle).Top(0, ToastY, 200, 40);
            toastIcon = HudKit.Icon(toast, "Icon", HudIcon.Route, HudTokens.Info, 22);
            toastIcon.rectTransform.Top(12, 9, 22, 22);
            toastText = HudKit.Txt(toast, "Text", "", 15, HudTokens.TextPrimary, HudFonts.Ui, TextAnchor.MiddleLeft);
            toastText.rectTransform.Top(44, 0, 400, 40);
            toast.gameObject.SetActive(false);
        }

        // ---- building: selection sheet ----

        void BuildSheet()
        {
            sheet = HudKit.Card(column, "Airport sheet", 28, HudTokens.SurfacePanel, HudTokens.BorderSubtle).Bottom(0, SheetBottom, HudTokens.ColumnWidth, HudTokens.SheetHeight);

            HudKit.Img(sheet, "Badge", HudSprites.Outline(28, 2), HudTokens.Accent, sliced: true).rectTransform.Top(16, 25, 56, 56);
            HudKit.Icon(sheet, "Badge icon", HudIcon.AirportSymbol, HudTokens.Accent, 32).rectTransform.Top(28, 37, 32, 32);
            codeText = HudKit.Txt(sheet, "Code", "", 34, HudTokens.Accent, HudFonts.Cond, TextAnchor.MiddleLeft);
            codeText.rectTransform.Top(84, 16, 190, 34);
            nameText = HudKit.Txt(sheet, "Name", "", 18, HudTokens.TextPrimary, HudFonts.Ui, TextAnchor.MiddleLeft);
            nameText.rectTransform.Top(84, 50, 200, 24);
            kindDot = HudKit.Img(sheet, "Kind dot", HudSprites.Rounded(5), HudTokens.Positive, sliced: true);
            kindDot.rectTransform.Top(84, 77, 10, 10);
            kindText = HudKit.Txt(sheet, "Kind", "", 13, HudTokens.TextSecondary, HudFonts.Cond, TextAnchor.MiddleLeft);
            kindText.rectTransform.Top(100, 74, 180, 16);

            var close = HudKit.Img(sheet, "Close", HudSprites.Rounded(24), HudTokens.SurfaceRaised, raycast: true, sliced: true);
            close.rectTransform.Top(294, 29, 48, 48);
            HudKit.Tappable(close.rectTransform, () => game.ClearSelection());
            HudKit.Icon(close.rectTransform, "Icon", HudIcon.Close, HudTokens.TextPrimary, 24).rectTransform.Center(24, 24);

            // route strip: FRA o - - plane - - o MUC
            strip = HudKit.Card(sheet, "Route strip", 16, HudTokens.SurfaceInset, HudTokens.BorderSubtle).Top(16, 106, 326, 75);
            HudKit.Tappable(strip, FrameStripRoute);
            leftCode = HudKit.Txt(strip, "From", "", 24, HudTokens.TextPrimary, HudFonts.Cond, TextAnchor.MiddleLeft);
            leftCode.rectTransform.Top(16, 12, 60, 28);
            rightCode = HudKit.Txt(strip, "To", "", 24, HudTokens.TextPrimary, HudFonts.Cond, TextAnchor.MiddleRight);
            rightCode.rectTransform.TopRight(16, 12, 60, 28);
            var line = new Color(HudTokens.ChartLine.r, HudTokens.ChartLine.g, HudTokens.ChartLine.b, 0.8f);
            HudKit.Img(strip, "Start", HudSprites.Outline(5, 2), HudTokens.ChartLine, sliced: true).rectTransform.Top(62, 21, 10, 10);
            HudKit.Tiled(strip, "Dash 1", HudSprites.DashH(), line).rectTransform.Top(78, 25, 69, 2);
            var plane = HudKit.Icon(strip, "Plane", HudIcon.Plane, HudTokens.ChartLine, 20);
            plane.rectTransform.Top(153, 16, 20, 20);
            plane.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            HudKit.Tiled(strip, "Dash 2", HudSprites.DashH(), line).rectTransform.Top(179, 25, 73, 2);
            HudKit.Img(strip, "End", HudSprites.Outline(5, 2), HudTokens.ChartLine, sliced: true).rectTransform.Top(254, 21, 10, 10);
            caption = HudKit.Txt(strip, "Caption", "", 13, HudTokens.TextSecondary, HudFonts.Ui, TextAnchor.MiddleLeft, FontStyle.Normal);
            caption.rectTransform.Top(16, 44, 294, 16);

            // three figures with dashed dividers
            string[] names = { "STRECKEN", "FLÜGE HEUTE", "AUSLASTUNG" };
            float[] xs = { 16f, 139f, 247f };
            for (int i = 0; i < 3; i++)
            {
                HudKit.Txt(sheet, "Stat label", names[i], 13, HudTokens.TextSecondary, HudFonts.Cond, TextAnchor.MiddleLeft).rectTransform.Top(xs[i], 197, 100, 16);
                stat[i] = HudKit.Txt(sheet, "Stat value", "", 28, HudTokens.TextPrimary, HudFonts.Cond, TextAnchor.MiddleLeft);
                stat[i].rectTransform.Top(xs[i], 215, 100, 32);
            }
            HudKit.Tiled(sheet, "Divider 1", HudSprites.DashV(), HudTokens.BorderSubtle).rectTransform.Top(124, 197, 2, 64);
            HudKit.Tiled(sheet, "Divider 2", HudSprites.DashV(), HudTokens.BorderSubtle).rectTransform.Top(233, 197, 2, 64);
            for (int i = 0; i < gauge.Length; i++)
                gauge[i] = HudKit.Img(sheet, "Gauge", HudSprites.Rounded(3), HudTokens.SurfaceRaised, sliced: true);
            for (int i = 0; i < gauge.Length; i++) gauge[i].rectTransform.Top(247f + i * 9.7f, 253, 7.7f, 6);

            // actions
            var primary = HudKit.Node("New route", sheet).Top(16, 277, 173, 52);
            var pbg = primary.gameObject.AddComponent<Image>();
            pbg.sprite = HudSprites.Rounded(26);
            pbg.type = Image.Type.Sliced;
            pbg.color = HudTokens.Accent;
            HudKit.Tappable(primary, () => ShowToast(HudToastKind.Info, "Neue Strecken kommen mit der Spiellogik"));
            HudKit.Icon(primary, "Plus", HudIcon.Plus, HudTokens.TextOnAccent, 22).rectTransform.Top(14, 15, 22, 22);
            var ptxt = HudKit.Txt(primary, "Label", "NEUE STRECKE", 16, HudTokens.TextOnAccent, HudFonts.Ui, TextAnchor.MiddleCenter);
            ptxt.rectTransform.Stretch();
            ptxt.rectTransform.offsetMin = new Vector2(34f, 0f);

            var secondary = HudKit.Node("Upgrade", sheet).Top(197, 277, 145, 52);
            HudKit.Img(secondary, "Border", HudSprites.Outline(26, 2), HudTokens.BorderStrong, sliced: true).rectTransform.Stretch();
            HudKit.Tappable(secondary, () => ShowToast(HudToastKind.Info, "Ausbauen kommt mit der Spiellogik"));
            HudKit.Txt(secondary, "Label", "AUSBAUEN", 17, HudTokens.TextPrimary, HudFonts.Ui, TextAnchor.MiddleCenter).rectTransform.Stretch();
        }

        void UpdateSheet()
        {
            var selection = model.Selection;
            bool show = selection.AirportId >= 0;
            if (sheet.gameObject.activeSelf != show) sheet.gameObject.SetActive(show);
            if (!show) { sheetAirport = int.MinValue; return; }
            if (selection.AirportId == sheetAirport) return;
            sheetAirport = selection.AirportId;

            AirportView airport = default;
            bool found = false;
            var airports = model.Airports;
            for (int i = 0; i < airports.Count; i++)
                if (airports[i].Id == sheetAirport) { airport = airports[i]; found = true; break; }
            if (!found) return;

            codeText.text = airport.Label;
            nameText.text = game.AirportName(airport.Id);
            var (kind, color) = airport.Kind switch
            {
                AirportKind.Competitor => ("KONKURRENT", (Color)HudTokens.MapCompetitor),
                AirportKind.Available => ("VERFÜGBAR", (Color)HudTokens.MapAvailable),
                AirportKind.Closed => ("GESPERRT", (Color)HudTokens.MapClosed),
                _ => ("EIGENER FLUGHAFEN", (Color)HudTokens.MapOwn),
            };
            kindText.text = kind;
            kindDot.color = color;

            game.AirportStats(airport.Id, out int routes, out int perDay, out float load);
            stat[0].text = routes.ToString();
            stat[1].text = perDay.ToString();
            stat[2].text = Mathf.RoundToInt(load * 100f) + " %";
            int filled = Mathf.RoundToInt(load * gauge.Length);
            for (int i = 0; i < gauge.Length; i++) gauge[i].color = i < filled ? HudTokens.Accent : HudTokens.SurfaceRaised;

            // the most important route of this airport
            stripRoute = -1;
            byte best = 0;
            AirportView other = default;
            var rs = model.Routes;
            for (int i = 0; i < rs.Count; i++)
            {
                var r = rs[i];
                if (r.FromId != airport.Id && r.ToId != airport.Id) continue;
                if (stripRoute >= 0 && r.Importance <= best) continue;
                int otherId = r.FromId == airport.Id ? r.ToId : r.FromId;
                for (int k = 0; k < airports.Count; k++) if (airports[k].Id == otherId) { other = airports[k]; break; }
                stripRoute = r.Id;
                best = r.Importance;
            }
            leftCode.text = airport.Label;
            if (stripRoute >= 0)
            {
                rightCode.text = other.Label;
                float km = DistanceKm(airport.Lat, airport.Lon, other.Lat, other.Lon);
                int minutes = Mathf.RoundToInt(km / 850f * 60f + 20f);
                string duration = minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
                caption.text = $"Wichtigste Strecke · {km:0} km · {duration}";
            }
            else
            {
                rightCode.text = "";
                caption.text = "Noch keine Strecke";
            }
        }

        void FrameStripRoute()
        {
            if (stripRoute < 0) return;
            game.SelectRoute(stripRoute);
            view.FrameRoute(stripRoute);
        }

        static float DistanceKm(float lat1, float lon1, float lat2, float lon2)
        {
            double p1 = lat1 * Mathf.Deg2Rad, p2 = lat2 * Mathf.Deg2Rad, dl = (lon2 - lon1) * Mathf.Deg2Rad;
            double a = Math.Sin((p2 - p1) / 2) * Math.Sin((p2 - p1) / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return (float)(2 * 6371.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(a))));
        }

        // ---- building: gate bar ----

        void BuildDock()
        {
            var dock = HudKit.Card(column, "Gate bar", 28, HudTokens.SurfacePanel, HudTokens.BorderSubtle).Bottom(0, HudTokens.SafeBottom, HudTokens.ColumnWidth, HudTokens.DockHeight);
            float w = (HudTokens.ColumnWidth - 12f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                var item = HudKit.Node("Tab " + TabNames[i], dock).Top(6f + i * w, 6, w, 64);
                HudKit.Tappable(item, () => { tab = index; RefreshTabs(); });
                var ring = HudKit.Img(item, "Ring", HudSprites.Outline(20, 2), HudTokens.Accent, sliced: true);
                ring.rectTransform.Top((w - 40f) / 2f, 3, 40, 40);
                tabRing[i] = ring;
                tabIcon[i] = HudKit.Icon(item, "Icon", TabIcons[i], HudTokens.TextSecondary, 24);
                tabIcon[i].rectTransform.Top((w - 24f) / 2f, 11, 24, 24);
                tabLabel[i] = HudKit.Txt(item, "Label", TabNames[i], 12, HudTokens.TextSecondary, HudFonts.Cond, TextAnchor.MiddleCenter);
                tabLabel[i].rectTransform.Top(0, 45, w, 16);
            }
            RefreshTabs();
        }

        void RefreshTabs()
        {
            for (int i = 0; i < 5; i++)
            {
                bool on = i == tab;
                tabRing[i].enabled = on;
                var c = on ? HudTokens.Accent : HudTokens.TextSecondary;
                tabIcon[i].color = c;
                tabLabel[i].color = c;
            }
        }

        // ---- the globe's surroundings ----

        // keeps the chosen airport in the free middle of the screen: the globe is told how much the HUD covers
        void UpdateInsets()
        {
            if (cameraMode) return;         // the picture is not the screen the view measures the insets against
            float s = canvas.scaleFactor;
            float top = (SpeedY + HudTokens.SpeedPillHeight + 8f) * s;
            float bottom = ((sheet.gameObject.activeSelf ? SheetBottom + HudTokens.SheetHeight : HudTokens.SafeBottom + HudTokens.DockHeight) + 8f) * s;
            if (Mathf.Abs(top - insetTop) < 0.5f && Mathf.Abs(bottom - insetBottom) < 0.5f) return;
            insetTop = top;
            insetBottom = bottom;
            view.SetViewportInsets(top, bottom, 0f, 0f);
        }

        // Labels live in the UI, the view only tells where an airport is on screen.
        void UpdateLabels()
        {
            int stage = zoomStage();
            var airports = model.Airports;
            int used = 0;
            if (stage >= 2)
            {
                for (int i = 0; i < airports.Count; i++)
                {
                    var a = airports[i];
                    if (stage == 2 && a.Importance < 3) continue;       // Z2: hubs only, from Z3 on all
                    if (!view.TryGetAirportScreenPoint(a.Id, out Vector2 px, out bool visible) || !visible) continue;
                    var text = Label(used++);
                    text.text = a.Label;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(labelRoot, px + new Vector2(10f, 4f) * canvas.scaleFactor, canvas.worldCamera, out Vector2 local);
                    text.rectTransform.anchoredPosition = local;
                }
            }
            for (int i = used; i < labels.Count; i++) labels[i].gameObject.SetActive(false);
        }

        Text Label(int index)
        {
            while (labels.Count <= index)
            {
                var t = HudKit.Txt(labelRoot, "Label", "", 15, Color.white, HudFonts.Cond, TextAnchor.MiddleLeft);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.pivot = new Vector2(0f, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(80f, 20f);
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = HudTokens.Alpha(HudTokens.SurfaceBase, 0.9f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
                labels.Add(t);
            }
            var label = labels[index];
            label.gameObject.SetActive(true);
            return label;
        }
    }
}

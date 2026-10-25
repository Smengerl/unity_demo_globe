using UnityEngine;

namespace Tycoon.Hud
{
    /// <summary>
    /// Design tokens of the HUD ("Gate Chart"). The values mirror docs/hud-design/hud-tokens.json; a test keeps both in step.
    /// </summary>
    public static class HudTokens
    {
        // hex values as in the JSON file (alpha as RRGGBBAA)
        public static readonly string[] Hex =
        {
            "#07111F", "#0F2040", "#1A3260", "#0A1830", "#2B4478", "#F4F7FB", "#B9C6DD", "#7385A6", "#101010", "#FFD21F", "#7FB2FF",
            "#3DDC97", "#FFB224", "#FF6B6B", "#071327", "#030A15", "#FF8FA3", "#FFFFFF",
        };

        public static readonly Color SurfaceBase = C("#07111F");
        public static readonly Color SurfacePanel = C("#0F2040");
        public static readonly Color SurfaceRaised = C("#1A3260");
        public static readonly Color SurfaceInset = C("#0A1830");
        public static readonly Color BorderSubtle = C("#2B4478");
        public static readonly Color BorderStrong = C("#F4F7FB");
        public static readonly Color TextPrimary = C("#F4F7FB");
        public static readonly Color TextSecondary = C("#B9C6DD");
        public static readonly Color TextDisabled = C("#7385A6");
        public static readonly Color TextOnAccent = C("#101010");
        public static readonly Color Accent = C("#FFD21F");
        public static readonly Color ChartLine = C("#7FB2FF");
        public static readonly Color Positive = C("#3DDC97");
        public static readonly Color Warning = C("#FFB224");
        public static readonly Color Negative = C("#FF6B6B");
        public static readonly Color Info = C("#7FB2FF");
        public static readonly Color FlipTop = C("#071327");
        public static readonly Color FlipBottom = C("#030A15");

        // map colours: also given to the globe view through AirportView.Color / RouteView.Color
        public static readonly Color32 MapOwn = C("#FFD21F");
        public static readonly Color32 MapCompetitor = C("#FF8FA3");
        public static readonly Color32 MapAvailable = C("#FFFFFF");
        public static readonly Color32 MapClosed = C("#7385A6");
        public static readonly Color32 MapRouteActive = C("#FFFFFF");

        // layout (reference width 390)
        public const float ColumnWidth = 358f;
        public const float SafeTop = 52f;
        public const float SafeBottom = 16f;
        public const float StackGap = 8f;
        public const float TopPillHeight = 98f;
        public const float SpeedPillHeight = 56f;
        public const float DockHeight = 76f;
        public const float SheetHeight = 345f;

        // motion
        public const float FlipHalfSeconds = 0.11f;
        public const float FlipStaggerSeconds = 0.04f;
        public const int FlipMaxChangesPerSecond = 3;
        public const float ToastSeconds = 4f;

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}

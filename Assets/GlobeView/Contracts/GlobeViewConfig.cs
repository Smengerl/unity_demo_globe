using System;
using UnityEngine;

namespace Tycoon.GlobeView.Contracts
{
    [Flags]
    public enum GlobeFeatures
    {
        None = 0,
        Relief = 1,
        Waves = 2,
        Foam = 4,
        Lights = 8,
        RegionalDetail = 16,
        Bloom = 32,
        All = Relief | Waves | Foam | Lights | RegionalDetail | Bloom,
    }

    public enum SunMode { Astronomical, Fixed }

    /// <summary>
    /// Start-up configuration of the view. Create one as an asset (Create > Tycoon > Globe View Config) or in code with
    /// <see cref="CreateDefault"/>. The view only reads it.
    /// </summary>
    [CreateAssetMenu(menuName = "Tycoon/Globe View Config", fileName = "GlobeViewConfig")]
    public class GlobeViewConfig : ScriptableObject
    {
        [Header("Camera")]
        public float InitialLat = 48f;
        public float InitialLon = 10f;
        [Tooltip("Distance above the surface in Earth radii")] public float InitialHeight = 3f;
        public float MinHeight = 0.12f;
        public float MaxHeight = 5f;
        public float FieldOfView = 30f;
        public float LatitudeLimit = 80f;
        [Tooltip("1 = the surface follows the finger exactly")] public float DragSensitivity = 1f;
        [Tooltip("How fast the turn dies out after letting go (per second)")] public float InertiaDamping = 4f;
        [Tooltip("A press that moves less than this counts as a tap")] public float TapSlopMm = 1.8f;
        [Tooltip("Radius around a finger in which an airport is picked")] public float PickRadiusMm = 7f;

        [Header("Zoom stages (camera height; Z0 | Z1 | Z2 | Z3 | Z4)")]
        [Tooltip("Heights where Z0|Z1, Z1|Z2, Z2|Z3 and Z3|Z4 meet")] public float[] StageEdges = { 3.2f, 1.6f, 0.75f, 0.32f };
        [Tooltip("Share of the edge height the camera has to pass before the stage changes (no flicker)")] public float StageHysteresis = 0.12f;
        [Tooltip("Seconds after zooming out before the regional textures are released")] public float RegionalUnloadSeconds = 4f;

        [Header("Quality")]
        public QualityTier Tier = QualityTier.Auto;
        public GlobeFeatures Features = GlobeFeatures.All;
        [Tooltip("0 = leave the application's frame rate alone")] public int TargetFrameRate = 0;

        [Header("Sky")]
        public SunMode Sun = SunMode.Astronomical;
        public float FixedSunLat = 10f;
        public float FixedSunLon = 20f;
        public bool ShowMoon = true;
        public bool SunFlare = true;

        [Header("Motion")]
        [Tooltip("Fewer fast movements: day and night calm down at lower game speeds")] public bool ReduceMotion = false;
        [Tooltip("Game seconds per real second from which single planes turn into traffic flow")] public float FlowRate = 21600f;
        [Tooltip("Game speed range in which day and night are calmed down (blinking at high speed)")] public Vector2 CalmRange = new Vector2(43200f, 86400f);
        [Tooltip("Game speed from which the fast-forward look (shaky picture, noise, tracking bands) is shown; 0 = never")] public float RewindRate = 86400f;

        public static GlobeViewConfig CreateDefault() => CreateInstance<GlobeViewConfig>();
    }
}

using System;
using System.Text.RegularExpressions;
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
        DayDetail = 16,
        Bloom = 32,
        NightDetail = 64,
        All = Relief | Waves | Foam | Lights | DayDetail | Bloom | NightDetail,
    }

    public enum SunMode { Astronomical, Fixed }

    /// <summary>What the Earth shows in one zoom stage (Z0 far .. Z4 close).</summary>
    [Serializable]
    public class StageConfig
    {
        public string Name = "";
        [Tooltip("Level of the tile pyramid shown in this stage; -1 = only the global base textures")] public int TileLevel = -1;
        [Tooltip("Relief (sun on the terrain)")] public bool Relief;
        [Tooltip("Animated waves; 0 = off, else number of wave layers")] public int WaveLayers;
        [Tooltip("Surf and foam on the coasts")] public bool Foam;
        [Tooltip("Fine foam dots (closer than the coarse pattern)")] public bool FoamDots;
        [Tooltip("Twinkling city lights; 0 = off, else number of light grids (finer with more)")] public int LightGrids;
        [Tooltip("Wider glow around single lights")] public bool LightWide;
        [Tooltip("Sharp night map from the tile pyramid")] public bool NightDetail;
        [Tooltip("Sharp day map, relief and coast from the tile pyramid")] public bool DayDetail;
        [Tooltip("Lens flare of the sun")] public bool Flares;
    }

    /// <summary>A device quality tier: the ceiling for zoom stage and features, and how much of the tile pyramid is kept in memory.</summary>
    [Serializable]
    public class TierConfig
    {
        public string Name = "";
        [Tooltip("Highest zoom stage this tier shows")] public int StageCap = 4;
        public GlobeFeatures Features = GlobeFeatures.All;
        [Tooltip("Tiles per side of the detail window; 0 = no tile detail. 512 px tiles: 4 = 2048 px, 8 = 4096 px")] public int WindowTiles;
        [Tooltip("Added to the stage's tile level (negative: coarser tiles, larger area)")] public int LevelOffset;
    }

    /// <summary>One layer of the tile pyramid.</summary>
    [Serializable]
    public class TileLayerConfig
    {
        [Tooltip("day, night, relief or coast")] public string Name = "";
        [Tooltip("jpg or png")] public string Format = "jpg";
        public int MinLevel = 4;
        public int MaxLevel = 6;
    }

    /// <summary>
    /// The tile pyramid: plate carree, level L has 2^(L+1) x 2^L tiles of TileSize px, row 0 at the north.
    /// Files: BaseUrl/layer/level/x_y.format. The script tools/build_tiles.py makes them from this same configuration.
    /// </summary>
    [Serializable]
    public class TilesConfig
    {
        [Tooltip("Folder or http(s) address of the tiles; empty = EarthTiles next to the project (editor) or StreamingAssets/earth-tiles (player)")] public string BaseUrl = "";
        public int TileSize = 512;
        [Tooltip("Tiles decoded per frame at most")] public int TilesPerFrame = 2;
        [Tooltip("Distance in km the coast maps reach (value 255), for the global map and every tile level")] public float CoastKm = 39f;
        public TileLayerConfig[] Layers =
        {
            new TileLayerConfig { Name = "day", Format = "jpg", MinLevel = 4, MaxLevel = 6 },
            new TileLayerConfig { Name = "night", Format = "jpg", MinLevel = 4, MaxLevel = 6 },
            new TileLayerConfig { Name = "relief", Format = "png", MinLevel = 4, MaxLevel = 6 },
            new TileLayerConfig { Name = "coast", Format = "png", MinLevel = 4, MaxLevel = 6 },
        };
    }

    /// <summary>
    /// Configuration of the view: camera and zoom limits, what each zoom stage shows, device tiers, sky, motion and the tile pyramid.
    /// It lives in one JSON file (Resources/globe-config.json) that the view and tools/build_tiles.py both read; the view only reads it.
    /// </summary>
    [Serializable]
    public class GlobeViewConfig
    {
        // ---- Camera ----
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

        // ---- Zoom stages ----
        [Tooltip("Heights where Z0|Z1, Z1|Z2, Z2|Z3 and Z3|Z4 meet")] public float[] StageEdges = { 3.2f, 1.6f, 0.75f, 0.32f };
        [Tooltip("Share of the edge height the camera has to pass before the stage changes (no flicker)")] public float StageHysteresis = 0.12f;
        [Tooltip("Seconds after zooming out before the tile detail is released")] public float RegionalUnloadSeconds = 4f;
        [Tooltip("What each stage shows, Z0 .. Z4")] public StageConfig[] Stages = DefaultStages();

        // ---- Quality ----
        public QualityTier Tier = QualityTier.Auto;
        public GlobeFeatures Features = GlobeFeatures.All;
        [Tooltip("Low, Medium and High")] public TierConfig[] Tiers = DefaultTiers();
        [Tooltip("0 = leave the application's frame rate alone")] public int TargetFrameRate = 0;

        // ---- Sky ----
        public SunMode Sun = SunMode.Astronomical;
        public float FixedSunLat = 10f;
        public float FixedSunLon = 20f;
        public bool ShowMoon = true;
        public bool SunFlare = true;

        // ---- Motion ----
        [Tooltip("Fewer fast movements: day and night calm down at lower game speeds")] public bool ReduceMotion = false;
        [Tooltip("Game seconds per real second from which single planes turn into traffic flow")] public float FlowRate = 21600f;
        [Tooltip("Game speed range in which day and night are calmed down (blinking at high speed)")] public Vector2 CalmRange = new Vector2(43200f, 86400f);
        [Tooltip("Game speed from which the fast-forward look (shaky picture, noise, tracking bands) is shown; 0 = never")] public float RewindRate = 86400f;

        // ---- Tiles ----
        public TilesConfig Tiles = new TilesConfig();

        // ---- loading ----

        const string ResourceName = "globe-config";

        /// <summary>The configuration shipped in Resources/globe-config.json; the built-in defaults if the file is missing.</summary>
        public static GlobeViewConfig LoadDefault()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset != null ? FromJson(asset.text) : new GlobeViewConfig();
        }

        /// <summary>Same as <see cref="LoadDefault"/>; kept as the short name used by tools and tests.</summary>
        public static GlobeViewConfig CreateDefault() => LoadDefault();

        /// <summary>Reads a configuration; fields missing in the text keep their defaults. Enums may be written by name ("High", "Relief, Waves").</summary>
        public static GlobeViewConfig FromJson(string json)
        {
            var config = new GlobeViewConfig();
            JsonUtility.FromJsonOverwrite(EnumsToNumbers(json), config);
            config.Validate();
            return config;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>The tier's settings; the first tier if the name is unknown.</summary>
        public TierConfig TierSettings(QualityTier tier)
        {
            int i = Mathf.Clamp((int)tier - 1, 0, Tiers.Length - 1);   // Auto = 0, Low = 1 .. High = 3
            return Tiers[i];
        }

        void Validate()
        {
            if (StageEdges == null || StageEdges.Length != 4) StageEdges = new[] { 3.2f, 1.6f, 0.75f, 0.32f };
            if (Stages == null || Stages.Length != ZoomStageCount) Stages = DefaultStages();
            if (Tiers == null || Tiers.Length != 3) Tiers = DefaultTiers();
            if (Tiles == null) Tiles = new TilesConfig();
        }

        const int ZoomStageCount = 5;

        static readonly Regex NamedEnum = new Regex("\"(Tier|Sun|Features)\"\\s*:\\s*\"([^\"]*)\"");

        static string EnumsToNumbers(string json) => NamedEnum.Replace(json, m =>
        {
            string key = m.Groups[1].Value, value = m.Groups[2].Value;
            int number = key == "Tier" ? (int)Enum.Parse(typeof(QualityTier), value, true)
                : key == "Sun" ? (int)Enum.Parse(typeof(SunMode), value, true)
                : (int)(GlobeFeatures)Enum.Parse(typeof(GlobeFeatures), value, true);
            return $"\"{key}\": {number}";
        });

        static StageConfig[] DefaultStages() => new[]
        {
            new StageConfig { Name = "Z0 Weltraum", TileLevel = -1, WaveLayers = 1, LightGrids = 1 },
            new StageConfig { Name = "Z1 Globus", TileLevel = -1, Relief = true, WaveLayers = 1, LightGrids = 1 },
            new StageConfig { Name = "Z2 Kontinent", TileLevel = -1, Relief = true, WaveLayers = 1, Foam = true, LightGrids = 1 },
            new StageConfig { Name = "Z3 Region", TileLevel = 5, Relief = true, WaveLayers = 2, Foam = true, FoamDots = true, LightGrids = 2, LightWide = true, NightDetail = true, DayDetail = true, Flares = true },
            new StageConfig { Name = "Z4 nah", TileLevel = 6, Relief = true, WaveLayers = 3, Foam = true, FoamDots = true, LightGrids = 3, LightWide = true, NightDetail = true, DayDetail = true, Flares = true },
        };

        static TierConfig[] DefaultTiers() => new[]
        {
            new TierConfig { Name = "Low", StageCap = 2, Features = GlobeFeatures.None, WindowTiles = 0, LevelOffset = 0 },
            new TierConfig { Name = "Medium", StageCap = 3, Features = GlobeFeatures.Relief | GlobeFeatures.Waves | GlobeFeatures.Foam | GlobeFeatures.Lights | GlobeFeatures.NightDetail, WindowTiles = 4, LevelOffset = -1 },
            new TierConfig { Name = "High", StageCap = 4, Features = GlobeFeatures.All, WindowTiles = 6, LevelOffset = 0 },
        };
    }
}

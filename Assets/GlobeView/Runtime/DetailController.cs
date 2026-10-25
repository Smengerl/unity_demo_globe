using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Decides how much detail the Earth shows. Two inputs:
    ///   the quality tier of the device (Low, Medium, High) is the ceiling,
    ///   the zoom stage (Z0 far .. Z4 close, see <see cref="ZoomStages"/>) decides what is on at the current zoom.
    /// What each stage and tier means is read from the configuration (<see cref="GlobeViewConfig.Stages"/>, <see cref="GlobeViewConfig.Tiers"/>).
    /// From the third stage on the detail from the tile pyramid is built around the camera (see <see cref="TileWindow"/>), one stage
    /// ahead of when it is shown, and released a few seconds after zooming out.
    /// </summary>
    internal sealed class DetailController
    {
        readonly GlobeViewConfig config;
        readonly Material earth;
        readonly MeshFilter earthMesh;
        readonly Mesh meshHi, meshLo;
        readonly GameObject halo;
        readonly Bloom bloom;
        readonly TileWindow window;
        readonly Texture2D clear;           // transparent: "no detail data" for layers that are not loaded

        public QualityTier Tier { get; private set; }
        /// <summary>Effective stage after the tier cap, 0 far .. 4 close.</summary>
        public int Stage { get; private set; }
        /// <summary>Stage from the zoom alone.</summary>
        public int RawStage { get; private set; }
        public bool FlaresAllowed => Tier >= QualityTier.Medium;
        /// <summary>The tile window, for tests.</summary>
        internal TileWindow Window => window;

        bool allowRelief, allowWaves, allowFoam, allowLights, allowNightDetail, allowDayDetail;
        TierConfig tier;
        int stageCap = 4;
        bool dirty = true;
        bool dayReady, nightReady;
        int attachedVersion = -1;
        float farSince = -1f;
        int appliedStage = -1;
        readonly List<string> layerNames = new List<string>();

        public DetailController(GlobeViewConfig config, Material earth, MeshFilter earthMesh, Mesh hi, Mesh lo, GameObject halo, Bloom bloom)
        {
            this.config = config;
            this.earth = earth;
            this.earthMesh = earthMesh;
            meshHi = hi;
            meshLo = lo;
            this.halo = halo;
            this.bloom = bloom;
            window = new TileWindow(config.Tiles);
            clear = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            clear.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f, 0f));
            clear.Apply();
            earth.SetFloat("_DetailCoastKm", config.Tiles.CoastKm);
            SetTier(config.Tier);
        }

        public void SetTier(QualityTier requested)
        {
            Tier = requested == QualityTier.Auto ? Detect() : requested;
            tier = config.TierSettings(Tier);
            var f = tier.Features & config.Features;
            allowRelief = (f & GlobeFeatures.Relief) != 0;
            allowWaves = (f & GlobeFeatures.Waves) != 0;
            allowFoam = (f & GlobeFeatures.Foam) != 0;
            allowLights = (f & GlobeFeatures.Lights) != 0;
            allowNightDetail = (f & GlobeFeatures.NightDetail) != 0;
            allowDayDetail = (f & GlobeFeatures.DayDetail) != 0;
            stageCap = tier.StageCap;
            if (bloom != null) bloom.active = (f & GlobeFeatures.Bloom) != 0;

            layerNames.Clear();
            if (allowDayDetail) { layerNames.Add("day"); layerNames.Add("relief"); layerNames.Add("coast"); }
            if (allowNightDetail) layerNames.Add("night");
            window.SetLayers(layerNames);
            dirty = true;
        }

        static QualityTier Detect()
        {
            if (!Application.isMobilePlatform) return QualityTier.High;
            if (SystemInfo.systemMemorySize >= 6000 && SystemInfo.processorCount >= 8) return QualityTier.High;
            return SystemInfo.systemMemorySize >= 3500 ? QualityTier.Medium : QualityTier.Low;
        }

        /// <param name="lat">Latitude and longitude below the camera, where the tile window is centred.</param>
        /// <param name="syncLoad">Build the tile window right away (screenshot tool, tests); in the game it builds in the background.</param>
        public void Tick(float height, float lat, float lon, bool syncLoad)
        {
            RawStage = ZoomStages.Next(RawStage, height, config.StageEdges, config.StageHysteresis);
            int effective = Mathf.Min(RawStage, stageCap);

            // tile detail: starts one stage early (Z2), released a while after zooming far out
            bool wantTiles = RawStage >= 2 && stageCap >= 3 && tier.WindowTiles > 0 && layerNames.Count > 0 && window.Available;
            if (wantTiles)
            {
                int tileStage = Mathf.Min(Mathf.Max(RawStage, 3), stageCap);
                int level = config.Stages[tileStage].TileLevel + tier.LevelOffset;
                if (level >= 0) window.Want(level, lat, lon, tier.WindowTiles, syncLoad);
            }
            window.Tick(Time.unscaledTime);
            if (window.Version != attachedVersion) Attach();

            if (RawStage <= 1 && attachedVersion >= 0 && (dayReady || nightReady || window.Building))
            {
                if (farSince < 0f) farSince = Time.unscaledTime;
                else if (Time.unscaledTime - farSince > config.RegionalUnloadSeconds) { window.Release(); Attach(); farSince = -1f; }
            }
            else farSince = -1f;

            if (effective != appliedStage || dirty)
            {
                Stage = effective;
                appliedStage = effective;
                dirty = false;
                ApplyDiscrete();
            }
            ApplyContinuous(height);
        }

        void ApplyDiscrete()
        {
            var st = config.Stages[Stage];
            Kw("TY_RELIEF", allowRelief && st.Relief);
            Kw("TY_WAVES", allowWaves && st.WaveLayers > 0);
            Kw("TY_FOAM", allowFoam && st.Foam);
            Kw("TY_LIGHTS", allowLights && st.LightGrids > 0);
            Kw("TY_NIGHTDETAIL", allowNightDetail && st.NightDetail && nightReady);
            Kw("TY_DETAIL", allowDayDetail && st.DayDetail && dayReady);

            earth.SetFloat("_ZWaveLayers", Mathf.Max(1, st.WaveLayers));
            earth.SetFloat("_ZFoamDots", st.FoamDots ? 1f : 0f);
            earth.SetFloat("_ZLightGrids", Mathf.Max(1, st.LightGrids));
            earth.SetFloat("_ZLightWide", st.LightWide ? 1f : 0f);
            earth.SetFloat("_ZFlares", st.Flares ? 1f : 0f);

            earthMesh.sharedMesh = Stage == 0 ? meshLo : meshHi;   // coarser sphere when the globe is small
        }

        void ApplyContinuous(float height)
        {
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(config.StageEdges[1], config.StageEdges[0], height));   // 0 at Z2 and nearer, 1 at Z0
            earth.SetFloat("_Relief", 1f - far);        // relief fades in between Z0 and Z2
            earth.SetFloat("_ZMipBias", far);           // one mip level coarser when far
            bool limbVisible = height > 0.75f;          // closer than that the limb is off screen
            if (halo.activeSelf != limbVisible) halo.SetActive(limbVisible);
        }

        void Kw(string keyword, bool on)
        {
            if (on) earth.EnableKeyword(keyword); else earth.DisableKeyword(keyword);
        }

        // the window changed: point the shader at its textures (transparent ones for layers without data)
        void Attach()
        {
            attachedVersion = window.Version;
            foreach (var name in new[] { "day", "night", "relief", "coast" })
            {
                string property = window.PropertyOf(name) ?? PropertyFor(name);
                earth.SetTexture(property, window.Texture(name) != null ? window.Texture(name) : clear);
            }
            dayReady = window.HasData("day");
            nightReady = window.HasData("night");
            earth.SetVector("_DetailRect", dayReady || nightReady ? window.Rect : Vector4.zero);
            earth.SetFloat("_UseDetailNight", nightReady ? 1f : 0f);
            dirty = true;
        }

        static string PropertyFor(string layer) => layer == "day" ? "_DetailTex" : layer == "night" ? "_DetailNightTex" : layer == "relief" ? "_DetailNormalTex" : "_DetailCoastTex";

        public void Dispose()
        {
            window.Release();
            if (clear != null) GlobeViewHost.Discard(clear);
        }
    }
}

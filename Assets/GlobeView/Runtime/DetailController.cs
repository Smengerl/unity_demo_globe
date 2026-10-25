using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Decides how much detail the Earth shows. Two inputs:
    ///   the quality tier of the device (Low, Medium, High) is the ceiling,
    ///   the zoom stage (Z0 far .. Z4 close, see <see cref="ZoomStages"/>) decides what is on at the current zoom.
    /// The effective stage is the zoom stage capped by the tier (Low Z2, Medium Z3, High Z4).
    /// The regional textures are only loaded from Z2 on (one stage before they are needed) and released a few seconds after zooming out.
    /// </summary>
    internal sealed class DetailController
    {
        static readonly (string property, string file)[] RegionalSet =
        {
            ("_DetailTex", "detail"), ("_DetailNormalTex", "detail_normal"),
            ("_DetailNightTex", "detail_night"), ("_DetailCoastTex", "detail_coast"),
        };

        readonly GlobeViewConfig config;
        readonly Material earth;
        readonly MeshFilter earthMesh;
        readonly Mesh meshHi, meshLo;
        readonly GameObject halo;
        readonly Bloom bloom;

        public QualityTier Tier { get; private set; }
        /// <summary>Effective stage after the tier cap, 0 far .. 4 close.</summary>
        public int Stage { get; private set; }
        /// <summary>Stage from the zoom alone.</summary>
        public int RawStage { get; private set; }
        public bool FlaresAllowed => Tier >= QualityTier.Medium;

        bool allowRelief, allowWaves, allowFoam, allowLights, allowNightDetail, allowDetail;
        int stageCap = 4;
        bool dirty = true;

        readonly Texture2D[] regional = new Texture2D[RegionalSet.Length];
        ResourceRequest[] requests;
        bool regionalReady, nightReady;
        float farSince = -1f;
        int appliedStage = -1;

        public DetailController(GlobeViewConfig config, Material earth, MeshFilter earthMesh, Mesh hi, Mesh lo, GameObject halo, Bloom bloom)
        {
            this.config = config;
            this.earth = earth;
            this.earthMesh = earthMesh;
            meshHi = hi;
            meshLo = lo;
            this.halo = halo;
            this.bloom = bloom;
            SetTier(config.Tier);
        }

        public void SetTier(QualityTier tier)
        {
            Tier = tier == QualityTier.Auto ? Detect() : tier;
            var f = config.Features;
            allowRelief = Tier >= QualityTier.Medium && (f & GlobeFeatures.Relief) != 0;
            allowWaves = Tier >= QualityTier.Medium && (f & GlobeFeatures.Waves) != 0;
            allowFoam = Tier >= QualityTier.Medium && (f & GlobeFeatures.Foam) != 0;
            allowLights = Tier >= QualityTier.Medium && (f & GlobeFeatures.Lights) != 0;
            allowNightDetail = Tier >= QualityTier.Medium && (f & GlobeFeatures.RegionalDetail) != 0;
            allowDetail = Tier >= QualityTier.High && (f & GlobeFeatures.RegionalDetail) != 0;
            stageCap = Tier == QualityTier.High ? 4 : Tier == QualityTier.Medium ? 3 : 2;
            if (bloom != null) bloom.active = Tier == QualityTier.High && (f & GlobeFeatures.Bloom) != 0;
            dirty = true;
        }

        static QualityTier Detect()
        {
            if (!Application.isMobilePlatform) return QualityTier.High;
            if (SystemInfo.systemMemorySize >= 6000 && SystemInfo.processorCount >= 8) return QualityTier.High;
            return SystemInfo.systemMemorySize >= 3500 ? QualityTier.Medium : QualityTier.Low;
        }

        /// <param name="syncLoad">Load the regional textures right away (screenshot tool); in the game they load in the background.</param>
        public void Tick(float height, bool syncLoad)
        {
            RawStage = ZoomStages.Next(RawStage, height, config.StageEdges, config.StageHysteresis);
            int effective = Mathf.Min(RawStage, stageCap);

            // regional textures: start loading one stage early (already at Z2), release a while after zooming far out
            if (RawStage >= 2 && stageCap >= 3 && !regionalReady && requests == null)
            {
                if (syncLoad) { for (int i = 0; i < regional.Length; i++) regional[i] = Resources.Load<Texture2D>("Earth/" + RegionalSet[i].file); AttachRegional(); }
                else
                {
                    requests = new ResourceRequest[RegionalSet.Length];
                    for (int i = 0; i < requests.Length; i++) requests[i] = Resources.LoadAsync<Texture2D>("Earth/" + RegionalSet[i].file);
                }
            }
            if (requests != null && AllDone(requests))
            {
                for (int i = 0; i < regional.Length; i++) regional[i] = requests[i].asset as Texture2D;
                requests = null;
                AttachRegional();
            }
            if (RawStage <= 1 && regionalReady)
            {
                if (farSince < 0f) farSince = Time.unscaledTime;
                else if (Time.unscaledTime - farSince > config.RegionalUnloadSeconds) ReleaseRegional();
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

        static bool AllDone(ResourceRequest[] r)
        {
            for (int i = 0; i < r.Length; i++) if (!r[i].isDone) return false;
            return true;
        }

        void ApplyDiscrete()
        {
            int z = Stage;
            Kw("TY_RELIEF", allowRelief && z >= 1);
            Kw("TY_WAVES", allowWaves && z >= 2);
            Kw("TY_FOAM", allowFoam && z >= 2);
            Kw("TY_LIGHTS", allowLights && z >= 2);
            Kw("TY_NIGHTDETAIL", allowNightDetail && z >= 3 && nightReady);
            Kw("TY_DETAIL", allowDetail && z >= 3 && regionalReady);

            earth.SetFloat("_ZWaveLayers", z >= 4 ? 3f : z == 3 ? 2f : 1f);
            earth.SetFloat("_ZFoamDots", z >= 3 ? 1f : 0f);
            earth.SetFloat("_ZLightGrids", z >= 4 ? 3f : z == 3 ? 2f : 1f);
            earth.SetFloat("_ZLightWide", z >= 3 ? 1f : 0f);
            earth.SetFloat("_ZFlares", z >= 3 ? 1f : 0f);

            earthMesh.sharedMesh = z == 0 ? meshLo : meshHi;   // coarser sphere when the globe is small
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

        void AttachRegional()
        {
            for (int i = 0; i < regional.Length; i++)
                if (regional[i] != null) earth.SetTexture(RegionalSet[i].property, regional[i]);
            regionalReady = regional[0] != null;
            nightReady = regional[2] != null;
            earth.SetFloat("_UseDetailNight", nightReady ? 1f : 0f);
            dirty = true;
        }

        void ReleaseRegional()
        {
            regionalReady = nightReady = false;
            earth.SetFloat("_UseDetailNight", 0f);
            ApplyDiscrete();                            // keywords off first, then the textures can go
            for (int i = 0; i < regional.Length; i++)
            {
                earth.SetTexture(RegionalSet[i].property, null);
                if (regional[i] != null) Resources.UnloadAsset(regional[i]);
                regional[i] = null;
            }
            farSince = -1f;
            dirty = true;
        }
    }
}

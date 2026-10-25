using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Fast-forward look at the highest game speed, like a video recorder winding on: the picture trembles and sometimes slips
    /// (small camera shakes), with scan lines, crawling noise, bright tracking bands and a noisy strip at the bottom over it.
    /// Fades in and out; off with <see cref="GlobeViewConfig.ReduceMotion"/> or <see cref="GlobeViewConfig.RewindRate"/> = 0.
    /// </summary>
    internal sealed class RewindEffect
    {
        const float FadeSeconds = 0.4f;

        readonly GlobeViewConfig config;
        GameObject root;
        Canvas canvas;
        RawImage scan, noise, bandA, bandB, foot;
        float strength, time, glitchTimer, glitchLeft;

        public float Strength => strength;
        public bool OverlayVisible => root != null && root.activeSelf;

        public RewindEffect(GlobeViewConfig config) { this.config = config; }

        public void Tick(double rate, float dt, Transform camera)
        {
            bool on = config.RewindRate > 0f && !config.ReduceMotion && rate >= config.RewindRate * 0.99;
            strength = Mathf.MoveTowards(strength, on ? 1f : 0f, dt / FadeSeconds);
            if (strength <= 0f)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            Ensure();
            root.SetActive(true);
            time += dt;

            // now and then the picture slips for a moment
            glitchTimer -= dt;
            if (glitchTimer <= 0f) { glitchTimer = Random.Range(0.6f, 1.6f); glitchLeft = Random.Range(0.08f, 0.2f); }
            bool glitch = glitchLeft > 0f;
            glitchLeft -= dt;

            float s = strength;
            float pitch = Wobble(1f) * 0.05f * s;
            float yaw = (Wobble(2f) * 0.12f + (glitch ? Random.Range(-0.7f, 0.7f) : 0f)) * s;
            float roll = Wobble(3f) * 0.25f * s;
            camera.rotation *= Quaternion.Euler(pitch, yaw, roll);

            var size = ((RectTransform)root.transform).rect.size;
            scan.color = new Color(1f, 1f, 1f, 0.14f * s);
            scan.uvRect = new Rect(0f, time * 0.8f, 1f, size.y / 4f);
            noise.color = new Color(1f, 1f, 1f, (glitch ? 0.16f : 0.06f) * s);
            noise.uvRect = new Rect(Random.value, Random.value, size.x / 128f, size.y / 128f);

            float flicker = 0.7f + 0.3f * Wobble(4f);
            Band(bandA, Mathf.Repeat(time * 0.22f, 1.2f) - 0.1f, 0.07f, 0.26f * s * flicker, size);
            Band(bandB, Mathf.Repeat(time * 0.13f + 0.5f, 1.2f) - 0.1f, 0.035f, 0.2f * s * flicker, size);
            Band(foot, 0f, 0.07f, (0.45f + 0.15f * Wobble(5f)) * s, size);
        }

        public void RenderInto(Camera cam)
        {
            Ensure();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
        }

        public void Destroy()
        {
            if (root != null) GlobeViewHost.Discard(root);
        }

        float Wobble(float seed) => Mathf.PerlinNoise(time * 17f + seed * 13.7f, seed * 3.1f) * 2f - 1f;

        static void Band(RawImage image, float y, float height, float alpha, Vector2 size)
        {
            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y + height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image.color = new Color(1f, 1f, 1f, alpha);
            image.uvRect = new Rect(Random.value, Random.value, size.x / 128f, 0.5f);
        }

        void Ensure()
        {
            if (root != null) return;
            root = new GameObject("Rewind effect", typeof(Canvas));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -50;              // below the game's HUD

            var noiseTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++) { byte g = (byte)Random.Range(40, 255); px[i] = new Color32(g, g, g, 255); }
            noiseTex.SetPixels32(px);
            noiseTex.Apply(false, false);

            var scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            scanTex.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255) });
            scanTex.Apply(false, false);

            scan = Layer("Scan lines", scanTex);
            noise = Layer("Noise", noiseTex);
            bandA = Layer("Band A", noiseTex);
            bandB = Layer("Band B", noiseTex);
            foot = Layer("Foot", noiseTex);
        }

        RawImage Layer(string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return image;
        }
    }
}

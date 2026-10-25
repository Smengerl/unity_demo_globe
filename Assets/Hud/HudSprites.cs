using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tycoon.Hud
{
    /// <summary>
    /// Sprites made in code: nine-sliced rounded rectangles (fill and outline, optionally with only some corners rounded) and dash patterns.
    /// All are white with alpha, the UI tints them. Sizes are in canvas units, the textures have two texels per unit.
    /// </summary>
    internal static class HudSprites
    {
        const int Texels = 2;

        [Flags] public enum Corners { TL = 1, TR = 2, BL = 4, BR = 8, Top = 3, Bottom = 12, All = 15 }

        static readonly Dictionary<long, Sprite> cache = new Dictionary<long, Sprite>();

        public static Sprite Rounded(float radius, Corners corners = Corners.All) => Make(radius, corners, 0f);
        public static Sprite Outline(float radius, float width) => Make(radius, Corners.All, width);

        static Sprite Make(float radius, Corners corners, float width)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius * Texels));
            int w = Mathf.RoundToInt(width * Texels);
            long key = (long)r | ((long)(int)corners << 16) | ((long)w << 24);
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            int n = 2 * r + 2;
            float half = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = x + 0.5f - half, fy = y + 0.5f - half;
                var quadrant = fy > 0 ? (fx > 0 ? Corners.TR : Corners.TL) : (fx > 0 ? Corners.BR : Corners.BL);
                float rq = (corners & quadrant) != 0 ? r : 0f;
                float qx = Mathf.Abs(fx) - half + rq, qy = Mathf.Abs(fy) - half + rq;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - rq;
                float a = Mathf.Clamp01(0.5f - d);
                if (w > 0) a -= Mathf.Clamp01(0.5f - (d + w));
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            var tex = Texture(n, n, px);
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), Texels * 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>A horizontal dash pattern (3 units on, 3 off, 2 units thick) to be tiled.</summary>
        public static Sprite DashH() => Dash(false);
        /// <summary>A vertical dash pattern (2 units wide) to be tiled.</summary>
        public static Sprite DashV() => Dash(true);

        static Sprite Dash(bool vertical)
        {
            long key = vertical ? -2 : -1;
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            int period = 12, thick = 4;
            int w = vertical ? thick : period, h = vertical ? period : thick;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int along = vertical ? y : x;
                px[y * w + x] = new Color32(255, 255, 255, (byte)(along < period / 2 ? 255 : 0));
            }
            var tex = Texture(w, h, px);
            tex.wrapMode = TextureWrapMode.Repeat;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Texels * 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            cache[key] = sprite;
            return sprite;
        }

        internal static Texture2D Texture(int w, int h, Color32[] pixels)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}

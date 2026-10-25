using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Tycoon.Hud
{
    public enum HudIcon { Globe, Tower, Route, Plane, Chart, Pause, Plus, Close, Check, Warning, Error, AirportSymbol }

    /// <summary>
    /// The HUD's line icons, drawn at run time from the same SVG path data as in the design (24 x 24 grid, stroke 2, round ends):
    /// no image files, and they take any colour. Supported path commands: M L H V C and z, absolute and relative.
    /// </summary>
    internal static class HudIcons
    {
        const int Size = 96;            // texels, 4 per grid unit
        const float Stroke = 2f;

        static readonly Dictionary<HudIcon, string[]> Defs = new Dictionary<HudIcon, string[]>
        {
            { HudIcon.Globe, new[] { "o12 12 9", "M3 12h18", "M12 3c3.2 3 3.2 15 0 18", "M12 3c-3.2 3-3.2 15 0 18" } },
            { HudIcon.Tower, new[] { "M8 21h8", "M12 21V12", "M8.5 12h7l-1.2-4.5H9.7z", "M12 7.5V3.5", "M10 3.5h4" } },
            { HudIcon.Route, new[] { "o5.5 18 2", "o18.5 6 2", "M7.5 18c7 0 2-12 9-12" } },
            { HudIcon.Plane, new[] { "M12 3c.8 0 1.4.7 1.4 1.5V9l7.6 4.8V16l-7.6-2.3v3.6l2 1.5v1.7L12 19.7 8.6 21v-1.7l2-1.5v-3.6L3 16v-2.2L10.6 9V4.5C10.6 3.7 11.2 3 12 3z" } },
            { HudIcon.Chart, new[] { "M4 4v16h16", "M8 15l3.5-4.5 3 3L20 7" } },
            { HudIcon.Pause, new[] { "M9 6v12", "M15 6v12" } },
            { HudIcon.Plus, new[] { "M12 5v14", "M5 12h14" } },
            { HudIcon.Close, new[] { "M6 6l12 12", "M18 6L6 18" } },
            { HudIcon.Check, new[] { "M5 12.5l4.5 4.5L19 7" } },
            { HudIcon.Warning, new[] { "M12 4l9 16H3z", "M12 10v4", "M12 17h.01" } },
            { HudIcon.Error, new[] { "o12 12 9", "M9 9l6 6", "M15 9l-6 6" } },
            { HudIcon.AirportSymbol, new[] { "o12 12 6", "M12 2v4", "M12 18v4", "M2 12h4", "M18 12h4" } },
        };

        static readonly Dictionary<HudIcon, Sprite> cache = new Dictionary<HudIcon, Sprite>();

        public static Sprite Get(HudIcon icon)
        {
            if (cache.TryGetValue(icon, out var s) && s != null) return s;
            var lines = new List<Vector2[]>();
            foreach (var d in Defs[icon]) Flatten(d, lines);
            var px = new Color32[Size * Size];
            float scale = Size / 24f, half = Stroke * 0.5f * scale;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f, Size - (y + 0.5f));         // texel position in texel units, y down like the grid
                float dist = float.MaxValue;
                foreach (var line in lines)
                    for (int i = 0; i + 1 < line.Length; i++)
                        dist = Mathf.Min(dist, DistanceToSegment(p, line[i] * scale, line[i + 1] * scale));
                float a = Mathf.Clamp01(half - dist + 0.5f);
                px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            // texture rows run bottom to top, the loop above filled them with y mirrored already
            s = Sprite.Create(HudSprites.Texture(Size, Size, px), new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.HideAndDontSave;
            cache[icon] = s;
            return s;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        // ---- tiny SVG path reader ----

        internal static void Flatten(string d, List<Vector2[]> output)
        {
            if (d[0] == 'o')            // circle: "o cx cy r"
            {
                var v = d.Substring(1).Split(' ');
                float cx = P(v[0]), cy = P(v[1]), r = P(v[2]);
                var ring = new Vector2[41];
                for (int i = 0; i <= 40; i++)
                {
                    float a = i / 40f * Mathf.PI * 2f;
                    ring[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
                }
                output.Add(ring);
                return;
            }

            var pts = new List<Vector2>();
            Vector2 cur = default, start = default;
            char cmd = ' ';
            int pos = 0;
            while (true)
            {
                SkipSeparators(d, ref pos);
                if (pos >= d.Length) break;
                if (char.IsLetter(d[pos]))
                {
                    cmd = d[pos++];
                    if (cmd == 'z' || cmd == 'Z') { pts.Add(start); cur = start; continue; }
                }
                bool rel = char.IsLower(cmd);
                switch (char.ToUpper(cmd))
                {
                    case 'M':
                    {
                        var p = Read2(d, ref pos); cur = rel ? cur + p : p; start = cur;
                        if (pts.Count > 1) output.Add(pts.ToArray());
                        pts = new List<Vector2> { cur };
                        cmd = rel ? 'l' : 'L';
                        break;
                    }
                    case 'L': { var p = Read2(d, ref pos); cur = rel ? cur + p : p; pts.Add(cur); break; }
                    case 'H': { float x = ReadNum(d, ref pos); cur = new Vector2(rel ? cur.x + x : x, cur.y); pts.Add(cur); break; }
                    case 'V': { float y = ReadNum(d, ref pos); cur = new Vector2(cur.x, rel ? cur.y + y : y); pts.Add(cur); break; }
                    case 'C':
                    {
                        var c1 = Read2(d, ref pos); var c2 = Read2(d, ref pos); var e = Read2(d, ref pos);
                        if (rel) { c1 += cur; c2 += cur; e += cur; }
                        for (int i = 1; i <= 14; i++)
                        {
                            float t = i / 14f, u = 1f - t;
                            pts.Add(u * u * u * cur + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * e);
                        }
                        cur = e;
                        break;
                    }
                    default: throw new System.FormatException("Unsupported path command " + cmd);
                }
            }
            if (pts.Count > 1) output.Add(pts.ToArray());
        }

        static void SkipSeparators(string d, ref int pos)
        {
            while (pos < d.Length && (d[pos] == ' ' || d[pos] == ',')) pos++;
        }

        static Vector2 Read2(string d, ref int pos)
        {
            float x = ReadNum(d, ref pos);
            return new Vector2(x, ReadNum(d, ref pos));
        }

        static float ReadNum(string d, ref int pos)
        {
            SkipSeparators(d, ref pos);
            int s = pos;
            if (pos < d.Length && (d[pos] == '-' || d[pos] == '+')) pos++;
            bool dot = false;
            while (pos < d.Length && (char.IsDigit(d[pos]) || (d[pos] == '.' && !dot)))
            {
                if (d[pos] == '.') dot = true;
                pos++;
            }
            return P(d.Substring(s, pos - s));
        }

        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}

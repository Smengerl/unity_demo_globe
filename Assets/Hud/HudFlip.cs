using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tycoon.Hud
{
    internal readonly struct FlipSize
    {
        public readonly float W, H, Radius, Split, Pin, Gap;
        public readonly int Font;
        public FlipSize(float w, float h, float radius, float split, float pin, float gap, int font)
        { W = w; H = h; Radius = radius; Split = split; Pin = pin; Gap = gap; Font = font; }

        public static readonly FlipSize Time = new FlipSize(26, 42, 8, 2f, 3, 3, 32);     // clock and cash
        public static readonly FlipSize Date = new FlipSize(16, 26, 5, 1.5f, 2, 2, 18);
    }

    /// <summary>
    /// One split-flap card. The new character's top half is shown at once; the old top half folds down (110 ms), then the new bottom half
    /// folds into place (110 ms). A start delay staggers neighbouring cards.
    /// </summary>
    internal sealed class FlipSlot
    {
        public readonly RectTransform Root;
        readonly Text topStatic, bottomStatic, topFlap, bottomFlap;
        readonly RectTransform topFlapRt, bottomFlapRt;
        readonly Text[] all;
        char shown = ' ';
        bool active, begun;
        float t;
        char oldChar;
        Color color, applied;

        public char Char => shown;

        public FlipSlot(Transform parent, FlipSize size, Font font)
        {
            Root = HudKit.Node("Flip", parent);
            Root.sizeDelta = new Vector2(size.W, size.H);
            color = HudTokens.Accent;

            Text Half(string name, bool top, Color bg, bool flap, out RectTransform container)
            {
                container = HudKit.Node(name, Root);
                container.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
                container.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
                container.offsetMin = container.offsetMax = Vector2.zero;
                if (flap) container.pivot = new Vector2(0.5f, top ? 0f : 1f);       // folds around the centre line
                container.gameObject.AddComponent<RectMask2D>();
                HudKit.Img(container, "Bg", HudSprites.Rounded(size.Radius, top ? HudSprites.Corners.Top : HudSprites.Corners.Bottom), bg, sliced: true).rectTransform.Stretch();
                var text = HudKit.Txt(container, "Char", "", size.Font, color, font, TextAnchor.MiddleCenter);
                var rt = text.rectTransform;
                rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
                rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
                rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
                rt.sizeDelta = new Vector2(0f, size.H);
                rt.anchoredPosition = Vector2.zero;
                return text;
            }

            topStatic = Half("Top", true, HudTokens.FlipTop, false, out _);
            bottomStatic = Half("Bottom", false, HudTokens.FlipBottom, false, out _);
            bottomFlap = Half("Flap bottom", false, HudTokens.FlipBottom, true, out bottomFlapRt);
            topFlap = Half("Flap top", true, HudTokens.FlipTop, true, out topFlapRt);
            all = new[] { topStatic, bottomStatic, topFlap, bottomFlap };
            topFlapRt.gameObject.SetActive(false);
            bottomFlapRt.gameObject.SetActive(false);

            var split = HudKit.Img(Root, "Split", null, HudTokens.SurfaceRaised);
            split.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            split.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            split.rectTransform.sizeDelta = new Vector2(0f, size.Split);
            for (int i = 0; i < 2; i++)
            {
                var pin = HudKit.Img(Root, "Pin", HudSprites.Rounded(size.Pin), HudTokens.BorderSubtle, sliced: true);
                var rt = pin.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(i, 0.5f);
                rt.sizeDelta = new Vector2(size.Pin * 2f, size.Pin * 2f);
                rt.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>Digit colour and whether the card stands dimmed (it changes too fast to fold).</summary>
        public void SetStyle(Color c, bool dim)
        {
            var final = dim ? HudTokens.Alpha(c, 0.35f) : c;
            if (final == applied) return;
            applied = final;
            color = c;
            foreach (var t in all) t.color = final;
        }

        void SetAll(char c)
        {
            string s = c == ' ' ? "" : c.ToString();
            foreach (var t in all) t.text = s;
        }

        /// <summary>Show a character, folding if <paramref name="animate"/>. A running fold is finished first.</summary>
        public void Set(char c, bool animate, float delay = 0f)
        {
            if (c == shown) return;
            if (active) Finish();
            oldChar = shown;
            shown = c;
            if (!animate) { SetAll(c); return; }
            active = true;
            begun = false;
            t = -delay;
        }

        void Begin()
        {
            string n = shown == ' ' ? "" : shown.ToString(), o = oldChar == ' ' ? "" : oldChar.ToString();
            topStatic.text = n;
            bottomStatic.text = o;
            topFlap.text = o;
            bottomFlap.text = n;
            topFlapRt.localScale = Vector3.one;
            topFlapRt.gameObject.SetActive(true);
            bottomFlapRt.localScale = new Vector3(1f, 0f, 1f);
            bottomFlapRt.gameObject.SetActive(false);
            begun = true;
        }

        void Finish()
        {
            SetAll(shown);
            topFlapRt.gameObject.SetActive(false);
            bottomFlapRt.gameObject.SetActive(false);
            active = false;
        }

        public void Tick(float dt)
        {
            if (!active) return;
            t += dt;
            if (t < 0f) return;
            if (!begun) Begin();
            float half = HudTokens.FlipHalfSeconds;
            if (t < half)
            {
                topFlapRt.localScale = new Vector3(1f, 1f - t / half, 1f);
            }
            else if (t < 2f * half)
            {
                topFlapRt.gameObject.SetActive(false);
                bottomFlapRt.gameObject.SetActive(true);
                bottomFlapRt.localScale = new Vector3(1f, (t - half) / half, 1f);
            }
            else Finish();
        }
    }

    /// <summary>
    /// A row of flip cards and plain separators (":", ".", ",", " "), laid out by hand. Cards that would have to change more often
    /// than <see cref="HudTokens.FlipMaxChangesPerSecond"/> times a second stop folding and stand dimmed.
    /// </summary>
    internal sealed class FlipRow
    {
        public readonly RectTransform Root;
        readonly FlipSize size;
        readonly Font font;
        readonly bool alignRight;
        readonly Color sepColor;
        readonly int sepFont;
        readonly List<FlipSlot> slots = new List<FlipSlot>();
        readonly List<RectTransform> items = new List<RectTransform>();
        readonly List<List<float>> history = new List<List<float>>();
        string pattern = "";
        string text = "";
        Text suffix;

        public string Text => text;

        public FlipRow(Transform parent, string name, FlipSize size, Font font, bool alignRight, int sepFont)
        {
            Root = HudKit.Node(name, parent);
            this.size = size;
            this.font = font;
            this.alignRight = alignRight;
            this.sepFont = sepFont;
            sepColor = HudTokens.Accent;
        }

        static bool IsSeparator(char c) => c == ':' || c == '.' || c == ',' || c == ' ';
        static float SepWidth(char c) => c == ':' ? 10f : 6f;

        public void SetSuffix(string s, int fontSize, Color color)
        {
            if (suffix == null) suffix = HudKit.Txt(Root, "Suffix", s, fontSize, color, font, TextAnchor.MiddleLeft);
            suffix.text = s;
            suffix.fontSize = fontSize;
            suffix.color = color;
            Layout();
        }

        public void SetText(string s, float now, bool animate, bool negative = false)
        {
            string kinds = "";
            foreach (char c in s) kinds += IsSeparator(c) ? c : '#';
            if (kinds != pattern) Rebuild(kinds, s);
            text = s;

            int slotIndex = 0;
            var color = negative ? HudTokens.Negative : HudTokens.Accent;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsSeparator(s[i]))
                {
                    var sep = items[i].GetComponent<Text>();
                    if (sep != null) sep.color = negative ? HudTokens.Negative : sepColor;
                    continue;
                }
                var slot = slots[slotIndex];
                var hist = history[slotIndex];
                hist.RemoveAll(x => x < now - 1f);
                bool changes = slot.Char != s[i];
                if (changes) hist.Add(now);
                bool fast = hist.Count > HudTokens.FlipMaxChangesPerSecond;
                slot.SetStyle(color, fast);
                slot.Set(s[i], animate && !fast, slotIndex * HudTokens.FlipStaggerSeconds);
                slotIndex++;
            }
        }

        void Rebuild(string kinds, string s)
        {
            foreach (var it in items) HudKit.Discard(it.gameObject);
            items.Clear(); slots.Clear(); history.Clear();
            pattern = kinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == '#')
                {
                    var slot = new FlipSlot(Root, size, font);
                    slots.Add(slot);
                    history.Add(new List<float>());
                    items.Add(slot.Root);
                }
                else
                {
                    var t = HudKit.Txt(Root, "Sep", kinds[i] == ' ' ? "" : kinds[i].ToString(), sepFont, sepColor, font, TextAnchor.MiddleCenter);
                    t.rectTransform.sizeDelta = new Vector2(SepWidth(kinds[i]), size.H);
                    items.Add(t.rectTransform);
                }
            }
            Layout();
        }

        void Layout()
        {
            float total = 0f;
            for (int i = 0; i < items.Count; i++) total += items[i].sizeDelta.x + (i > 0 ? size.Gap : 0f);
            float suffixW = 0f;
            if (suffix != null && suffix.text.Length > 0) suffixW = suffix.preferredWidth + 6f + size.Gap;
            float rowW = Root.sizeDelta.x;
            float x = alignRight ? rowW - total - suffixW : 0f;
            foreach (var it in items)
            {
                it.anchorMin = it.anchorMax = new Vector2(0f, 0.5f);
                it.pivot = new Vector2(0f, 0.5f);
                it.anchoredPosition = new Vector2(x, 0f);
                x += it.sizeDelta.x + size.Gap;
            }
            if (suffix != null)
            {
                var rt = suffix.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(suffix.preferredWidth + 4f, size.H);
                rt.anchoredPosition = new Vector2(x + 3f, 0f);
            }
        }

        public void Tick(float dt)
        {
            foreach (var s in slots) s.Tick(dt);
        }
    }
}

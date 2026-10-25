using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tycoon.Hud
{
    /// <summary>Small helpers to build uGUI in code: nodes, placing, images, texts, rounded cards, buttons.</summary>
    internal static class HudKit
    {
        /// <summary>Destroy that also works in edit mode (tests, screenshot tool).</summary>
        public static void Discard(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchored to the top left of the parent; x to the right, y down.</summary>
        public static RectTransform Top(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Anchored to the top right of the parent; <paramref name="right"/> is the distance of the right edge.</summary>
        public static RectTransform TopRight(this RectTransform rt, float right, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Anchored to the bottom left of the parent; y up from the bottom edge.</summary>
        public static RectTransform Bottom(this RectTransform rt, float x, float bottom, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, bottom);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Center(this RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool raycast = false, bool sliced = false)
        {
            var im = Node(name, parent).gameObject.AddComponent<Image>();
            im.sprite = sprite;
            im.color = color;
            im.raycastTarget = raycast;
            if (sliced) im.type = Image.Type.Sliced;
            return im;
        }

        public static Image Tiled(Transform parent, string name, Sprite sprite, Color color)
        {
            var im = Img(parent, name, sprite, color);
            im.type = Image.Type.Tiled;
            return im;
        }

        public static Image Icon(Transform parent, string name, HudIcon icon, Color color, float size)
        {
            var im = Img(parent, name, HudIcons.Get(icon), color);
            im.rectTransform.sizeDelta = new Vector2(size, size);
            return im;
        }

        public static Text Txt(Transform parent, string name, string text, int size, Color color, Font font, TextAnchor align, FontStyle style = FontStyle.Bold)
        {
            HudFonts.Resolve(ref font, ref style);
            var t = Node(name, parent).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        /// <summary>A rounded card: filled and outlined, both nine-sliced; the fill catches taps so the globe below does not.</summary>
        public static RectTransform Card(Transform parent, string name, float radius, Color fill, Color border, float borderWidth = 1.5f)
        {
            var rt = Node(name, parent);
            Img(rt, "Fill", HudSprites.Rounded(radius), fill, raycast: true, sliced: true).rectTransform.Stretch();
            if (borderWidth > 0f) Img(rt, "Border", HudSprites.Outline(radius, borderWidth), border, sliced: true).rectTransform.Stretch();
            return rt;
        }

        /// <summary>Makes the node tappable. Draws nothing itself; add an image of your own below it if it should look like something.</summary>
        public static Button Tappable(RectTransform rt, Action onClick)
        {
            var hit = rt.GetComponent<Image>();
            if (hit == null)
            {
                hit = rt.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
            }
            hit.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = hit;
            b.onClick.AddListener(() => onClick());
            return b;
        }
    }
}

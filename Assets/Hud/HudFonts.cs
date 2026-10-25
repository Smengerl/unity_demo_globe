using UnityEngine;

namespace Tycoon.Hud
{
    /// <summary>
    /// Fonts of the HUD: Barlow (regular and bold) and Barlow Condensed (bold), SIL Open Font License, files in Resources/Fonts.
    /// If they are missing the HUD takes the first installed system font of a similar look, and Unity's built-in font as the last resort.
    /// </summary>
    public static class HudFonts
    {
        static Font ui, uiBold, cond;
        static bool bundled;

        /// <summary>Barlow regular.</summary>
        public static Font Ui => ui != null ? ui : ui = Load("Fonts/Barlow-Regular", new[] { "Barlow", "Helvetica Neue", "Roboto", "Arial" });
        /// <summary>Barlow bold, used instead of faux-bolding <see cref="Ui"/>.</summary>
        public static Font UiBold => uiBold != null ? uiBold : uiBold = Load("Fonts/Barlow-Bold", new[] { "Barlow", "Helvetica Neue", "Roboto", "Arial" });
        /// <summary>Barlow Condensed bold, for numbers, codes and labels.</summary>
        public static Font Cond => cond != null ? cond : cond = Load("Fonts/BarlowCondensed-Bold", new[] { "Barlow Condensed", "Arial Narrow", "Roboto Condensed", "Helvetica Neue", "Arial" });

        /// <summary>True if the Barlow files were found, so bold needs no faux style.</summary>
        public static bool Bundled { get { var _ = Ui; return bundled; } }

        /// <summary>The font and style to use for a family and a wanted style (bold comes from the bold file where there is one).</summary>
        internal static void Resolve(ref Font font, ref FontStyle style)
        {
            if (font == ui && style == FontStyle.Bold && Bundled) { font = UiBold; style = FontStyle.Normal; }
            else if (font == cond && Bundled) style = FontStyle.Normal;            // the file already is bold
        }

        static Font Load(string resource, string[] systemNames)
        {
            var f = Resources.Load<Font>(resource);
            if (f != null) { bundled = true; return f; }
            try
            {
                f = Font.CreateDynamicFontFromOSFont(systemNames, 32);
                if (f != null) return f;
            }
            catch (System.Exception) { /* no system fonts (headless): fall through */ }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}

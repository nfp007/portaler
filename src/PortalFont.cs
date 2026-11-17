using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Picture font in the style of the PORTALER sign: one embedded PNG per character (art/font/glyph_*.png,
    /// made by tools/make_font.py). Every glyph image has the same height and baseline, so letters line up.
    /// Lowercase uses the capitals; accents are dropped (Č → C). Text with other characters falls back to the normal font.
    /// </summary>
    internal static class PortalFont
    {
        // metrics of the source images (pixels at full size)
        private const float CapPx = 96f;      // height of a capital letter
        private const float LinePx = 133f;    // height of every glyph image
        private const float CapTopPx = 16f;   // from the image top to the top of a capital
        private const float Overlap = 15f;    // transparent padding shared between neighbours
        private const float SpacePx = 30f;

        private static readonly Dictionary<char, string> Names = new Dictionary<char, string>
        {
            { '-', "dash" }, { '_', "underscore" }, { '.', "dot" }, { ',', "comma" }, { ':', "colon" }, { '!', "exclam" },
            { '?', "question" }, { '\'', "apostrophe" }, { '(', "parenleft" }, { ')', "parenright" }, { '+', "plus" },
            { '/', "slash" }, { '#', "hash" }, { '&', "ampersand" },
        };
        private static readonly Dictionary<char, Texture2D> Glyphs = new Dictionary<char, Texture2D>();
        private static readonly HashSet<char> Missing = new HashSet<char>();
        private static readonly Dictionary<string, string> Prepared = new Dictionary<string, string>();

        private static Texture2D Glyph(char c)
        {
            if (Glyphs.TryGetValue(c, out var t)) return t;
            if (Missing.Contains(c)) return null;
            string name = Names.TryGetValue(c, out var n) ? n : c.ToString();
            t = Art.LoadPng("glyph_" + name + ".png", true);
            if (t == null) { Missing.Add(c); return null; }
            Glyphs[c] = t;
            return t;
        }

        /// <summary>Upper case without accents ("Čertova hora" → "CERTOVA HORA"), or null if a character has no picture.</summary>
        public static string Prepare(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (Prepared.TryGetValue(text, out var cached)) return cached;
            var sb = new StringBuilder();
            foreach (char ch in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch == 'ß') { sb.Append("SS"); continue; }
                char c = char.ToUpperInvariant(ch);
                if (c == ' ') { sb.Append(' '); continue; }
                if (Glyph(c) == null) { sb = null; break; }
                sb.Append(c);
            }
            string result = sb?.ToString();
            if (Prepared.Count > 500) Prepared.Clear();
            Prepared[text] = result;
            return result;
        }

        /// <summary>Width on screen of prepared text when capitals are capHeight pixels tall.</summary>
        public static float Width(string prepared, float capHeight)
        {
            float s = capHeight / CapPx, w = 0f;
            for (int i = 0; i < prepared.Length; i++)
            {
                char c = prepared[i];
                if (c == ' ') { w += SpacePx * s; continue; }
                var t = Glyph(c);
                if (t != null) w += (t.width - Overlap) * s;
            }
            return w + Overlap * s;
        }

        /// <summary>Draws prepared text; (x, capTop) is the left edge and the top of the capitals.</summary>
        public static void Draw(string prepared, float x, float capTop, float capHeight, float alpha)
        {
            float s = capHeight / CapPx;
            float y = capTop - CapTopPx * s, h = LinePx * s;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            for (int i = 0; i < prepared.Length; i++)
            {
                char c = prepared[i];
                if (c == ' ') { x += SpacePx * s; continue; }
                var t = Glyph(c);
                if (t == null) continue;
                GUI.DrawTexture(new Rect(x, y, t.width * s, h), t);
                x += (t.width - Overlap) * s;
            }
            GUI.color = old;
        }
    }
}

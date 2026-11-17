using UnityEngine;

namespace Portaler
{
    /// <summary>Portal colours (shared by everybody). 0 = vanilla look. Stored by number: keep the order.</summary>
    public static class PortalColors
    {
        public static readonly string[] Names = { "Vanilla", "Ice", "Green", "Gold", "Red", "Purple", "Teal", "White", "Pink" };
        public static readonly Color[] Values =
        {
            Color.clear,
            new Color32(110, 185, 255, 255),
            new Color32(110, 210, 110, 255),
            new Color32(222, 165, 40, 255),
            new Color32(235, 75, 55, 255),
            new Color32(180, 110, 240, 255),
            new Color32(60, 205, 190, 255),
            new Color32(240, 236, 225, 255),
            new Color32(245, 120, 180, 255),
        };
        public const int Count = 9;

        /// <summary>Custom colours from the picker are stored as 0x01RRGGBB (presets stay 0..8).</summary>
        private const int CustomFlag = 0x01000000;
        public static bool IsCustom(int c) => (c & 0x7F000000) == CustomFlag;
        public static int Clamp(int c) => IsCustom(c) ? c : c < 0 || c >= Count ? 0 : c;
        public static bool Has(int c) => IsCustom(c) || (c > 0 && c < Count);

        /// <summary>The colour for a stored number (preset or custom); clear for vanilla.</summary>
        public static Color Get(int c)
        {
            if (IsCustom(c)) return new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
            return c > 0 && c < Count ? Values[c] : Color.clear;
        }

        public static int FromColor(Color c)
        {
            Color32 k = c;
            return CustomFlag | (k.r << 16) | (k.g << 8) | k.b;
        }

        public static string Name(int c) => IsCustom(c) ? "Custom" : Names[Clamp(c)];
    }
}

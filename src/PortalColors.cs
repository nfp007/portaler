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

        public static int Clamp(int c) => c < 0 || c >= Count ? 0 : c;
        public static bool Has(int c) => c > 0 && c < Count;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Portaler
{
    /// <summary>Biome codes used for automatic portal names, and the order of groups in the list.</summary>
    public static class Biomes
    {
        public struct Entry
        {
            public string Code;
            public string Display;
            public Entry(string code, string display) { Code = code; Display = display; }
        }

        /// <summary>List order: Meadows first, Deep North last.</summary>
        public static readonly Entry[] Order =
        {
            new Entry("ME", "Meadows"),
            new Entry("BF", "Black Forest"),
            new Entry("SW", "Swamp"),
            new Entry("MO", "Mountain"),
            new Entry("PL", "Plains"),
            new Entry("OC", "Ocean"),
            new Entry("MI", "Mistlands"),
            new Entry("AL", "Ashlands"),
            new Entry("DN", "Deep North"),
            new Entry("XX", "Other"),
        };

        private static readonly Dictionary<string, string> ByEnumName = new Dictionary<string, string>
        {
            { "Meadows", "ME" },
            { "BlackForest", "BF" },
            { "Swamp", "SW" },
            { "Mountain", "MO" },
            { "Plains", "PL" },
            { "Ocean", "OC" },
            { "Mistlands", "MI" },
            { "AshLands", "AL" },
            { "DeepNorth", "DN" },
        };

        public static string CodeAt(Vector3 position)
        {
            if (WorldGenerator.instance == null) return "XX";
            var biome = WorldGenerator.instance.GetBiome(position);
            return ByEnumName.TryGetValue(biome.ToString(), out var code) ? code : "XX";
        }

        public static int SortIndex(string code)
        {
            for (int i = 0; i < Order.Length; i++)
                if (Order[i].Code == code) return i;
            return Order.Length;
        }
    }
}

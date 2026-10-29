using System;
using System.IO;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Personal settings of this PC (list sorting). Stored in Valheim's own data folder
    /// (AppData\LocalLow\IronGate\Valheim\Portaler.prefs), so mod updates, reinstalls or
    /// a different Gale profile never reset them.
    /// </summary>
    public static class Prefs
    {
        private static string _mode = "Biome";
        private static bool _reversed;
        private static bool _loaded;
        // folded list groups, per character (player id) -> group keys
        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> Collapsed =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();

        private static string CharKey => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID().ToString() : "0";

        // last portal each character came from ("Back to" row), stored as a position
        private static readonly System.Collections.Generic.Dictionary<string, Vector3> Back =
            new System.Collections.Generic.Dictionary<string, Vector3>();

        public static Vector3? GetBack()
        {
            Load();
            return Back.TryGetValue(CharKey, out var v) ? v : (Vector3?)null;
        }

        public static void SetBack(Vector3 pos)
        {
            Load();
            Back[CharKey] = pos;
            Save();
        }

        public static bool IsCollapsed(string group)
        {
            Load();
            return Collapsed.TryGetValue(CharKey, out var set) && set.Contains(group);
        }

        public static void SetCollapsed(string group, bool collapsed)
        {
            Load();
            if (!Collapsed.TryGetValue(CharKey, out var set)) Collapsed[CharKey] = set = new System.Collections.Generic.HashSet<string>();
            if (collapsed ? set.Add(group) : set.Remove(group)) Save();
        }

        private static string FilePath => Path.Combine(Application.persistentDataPath, "Portaler.prefs");

        public static string SortMode
        {
            get { Load(); return _mode; }
            set { Load(); if (_mode != value) { _mode = value; Save(); } }
        }

        public static bool SortReversed
        {
            get { Load(); return _reversed; }
            set { Load(); if (_reversed != value) { _reversed = value; Save(); } }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "SortMode" && (v == "Biome" || v == "Name" || v == "Distance")) _mode = v;
                    else if (k == "SortReversed") _reversed = v == "true";
                    else if (k.StartsWith("Back."))
                    {
                        var c = v.Split(';');
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, inv, out float x)
                            && float.TryParse(c[1], System.Globalization.NumberStyles.Float, inv, out float y)
                            && float.TryParse(c[2], System.Globalization.NumberStyles.Float, inv, out float z))
                            Back[k.Substring(5)] = new Vector3(x, y, z);
                    }
                    else if (k.StartsWith("Collapsed."))
                        Collapsed[k.Substring(10)] = new System.Collections.Generic.HashSet<string>(
                            v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not read prefs: " + e.Message); }
        }

        private static void Save()
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    "# Portaler personal settings",
                    "SortMode=" + _mode,
                    "SortReversed=" + (_reversed ? "true" : "false"),
                };
                foreach (var kv in Collapsed)
                    if (kv.Value.Count > 0) lines.Add("Collapsed." + kv.Key + "=" + string.Join(",", kv.Value));
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var kv in Back)
                    lines.Add("Back." + kv.Key + "=" + kv.Value.x.ToString("0.###", ic) + ";" + kv.Value.y.ToString("0.###", ic) + ";" + kv.Value.z.ToString("0.###", ic));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not save prefs: " + e.Message); }
        }
    }
}

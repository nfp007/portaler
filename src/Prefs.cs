using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Personal settings of this PC (sorting, list sizes) and of each character (folded and ordered groups, "Back to" history, followed players). Stored in Valheim's own data folder
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

        // portals each character last came from ("Back to" rows), newest first, stored as positions
        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector3>> Back =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector3>>();
        private const int BackKeep = 4; // one more than shown: the portal you stand in is never offered

        public static System.Collections.Generic.List<Vector3> GetBackList()
        {
            Load();
            return Back.TryGetValue(CharKey, out var v) ? v : new System.Collections.Generic.List<Vector3>();
        }

        /// <summary>Remember the portal we leave from (newest first, no duplicates).</summary>
        public static void PushBack(Vector3 pos)
        {
            Load();
            if (!Back.TryGetValue(CharKey, out var list)) Back[CharKey] = list = new System.Collections.Generic.List<Vector3>();
            list.RemoveAll(v => Utils.DistanceXZ(v, pos) < 1.5f && Mathf.Abs(v.y - pos.y) < 3f);
            list.Insert(0, pos);
            if (list.Count > BackKeep) list.RemoveRange(BackKeep, list.Count - BackKeep);
            Save();
        }

        // how many "Back to" and "Last built" rows to show (this PC)
        private static int _backCount = 1, _lastBuiltCount = 3;
        public const int MaxBack = 3, MaxLastBuilt = 5;

        public static int BackCount
        {
            get { Load(); return _backCount; }
            set { Load(); value = Mathf.Clamp(value, 1, MaxBack); if (_backCount != value) { _backCount = value; Save(); } }
        }

        public static int LastBuiltCount
        {
            get { Load(); return _lastBuiltCount; }
            set { Load(); value = Mathf.Clamp(value, 1, MaxLastBuilt); if (_lastBuiltCount != value) { _lastBuiltCount = value; Save(); } }
        }

        // order of the movable groups, per character
        public static readonly string[] DefaultGroupOrder = { "BACK", "TRIPS", "FAV", "LAST", "PORTALS" };
        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> GroupOrders =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();

        public static System.Collections.Generic.List<string> GetGroupOrder()
        {
            Load();
            var list = GroupOrders.TryGetValue(CharKey, out var o) ? new System.Collections.Generic.List<string>(o) : new System.Collections.Generic.List<string>();
            list.RemoveAll(k => Array.IndexOf(DefaultGroupOrder, k) < 0);
            // groups missing from a saved order go to their default place (before the next default group that is there)
            for (int i = 0; i < DefaultGroupOrder.Length; i++)
            {
                string k = DefaultGroupOrder[i];
                if (list.Contains(k)) continue;
                int at = list.Count;
                for (int j = i + 1; j < DefaultGroupOrder.Length; j++)
                {
                    int found = list.IndexOf(DefaultGroupOrder[j]);
                    if (found >= 0) { at = found; break; }
                }
                list.Insert(at, k);
            }
            return list;
        }

        public static void SetGroupOrder(System.Collections.Generic.List<string> order)
        {
            Load();
            GroupOrders[CharKey] = new System.Collections.Generic.List<string>(order);
            Save();
        }

        // players whose trips this character follows (by name); not set = everybody
        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> Tracked =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();

        /// <summary>Followed player names, or null when this character never chose (= follow everybody).</summary>
        public static System.Collections.Generic.HashSet<string> GetTracked()
        {
            Load();
            return Tracked.TryGetValue(CharKey, out var set) ? set : null;
        }

        public static void SetTracked(System.Collections.Generic.IEnumerable<string> names)
        {
            Load();
            Tracked[CharKey] = new System.Collections.Generic.HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
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
                    else if (k == "BackCount" && int.TryParse(v, out int bc)) _backCount = Mathf.Clamp(bc, 1, MaxBack);
                    else if (k == "LastBuiltCount" && int.TryParse(v, out int lc)) _lastBuiltCount = Mathf.Clamp(lc, 1, MaxLastBuilt);
                    else if (k.StartsWith("Back."))
                    {
                        // newest first, "x;y;z|x;y;z|..." (older versions stored a single position)
                        var list = new System.Collections.Generic.List<Vector3>();
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        foreach (var item in v.Split('|'))
                        {
                            var c = item.Split(';');
                            if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, inv, out float x)
                                && float.TryParse(c[1], System.Globalization.NumberStyles.Float, inv, out float y)
                                && float.TryParse(c[2], System.Globalization.NumberStyles.Float, inv, out float z))
                                list.Add(new Vector3(x, y, z));
                        }
                        if (list.Count > 0) Back[k.Substring(5)] = list;
                    }
                    else if (k.StartsWith("Groups."))
                        GroupOrders[k.Substring(7)] = new System.Collections.Generic.List<string>(
                            v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    else if (k.StartsWith("Track."))
                        Tracked[k.Substring(6)] = new System.Collections.Generic.HashSet<string>(
                            v.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(n => n != "-"), StringComparer.OrdinalIgnoreCase);
                    else if (k.StartsWith("Collapsed."))
                        Collapsed[k.Substring(10)] = new System.Collections.Generic.HashSet<string>(
                            v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not read prefs: " + e.Message); }
        }

        /// <summary>Goes up on every change (the list rebuilds what depends on these settings).</summary>
        public static int Version { get; private set; }

        private static void Save()
        {
            Version++;
            try
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    "# Portaler personal settings",
                    "SortMode=" + _mode,
                    "SortReversed=" + (_reversed ? "true" : "false"),
                    "BackCount=" + _backCount,
                    "LastBuiltCount=" + _lastBuiltCount,
                };
                foreach (var kv in Collapsed)
                    if (kv.Value.Count > 0) lines.Add("Collapsed." + kv.Key + "=" + string.Join(",", kv.Value));
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var kv in Back)
                    if (kv.Value.Count > 0)
                        lines.Add("Back." + kv.Key + "=" + string.Join("|", kv.Value.Select(p =>
                            p.x.ToString("0.###", ic) + ";" + p.y.ToString("0.###", ic) + ";" + p.z.ToString("0.###", ic)).ToArray()));
                foreach (var kv in GroupOrders) lines.Add("Groups." + kv.Key + "=" + string.Join(",", kv.Value.ToArray()));
                // an empty choice is kept too ("-"): it means "nobody", not "everybody"
                foreach (var kv in Tracked) lines.Add("Track." + kv.Key + "=" + (kv.Value.Count > 0 ? string.Join("|", kv.Value.ToArray()) : "-"));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not save prefs: " + e.Message); }
        }
    }
}

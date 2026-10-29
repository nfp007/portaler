using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Server half: the single source of truth for the portal list.
    /// Collects every portal in the world, hands out automatic names and
    /// pushes the list to all players whenever it changes.
    /// </summary>
    public static class PortalServer
    {
        public const string RpcRequestList = "Portaler_RequestList";
        public const string RpcNewPortal = "Portaler_NewPortal";
        public const string RpcRequestFavs = "Portaler_RequestFavs";
        public const string RpcToggleFav = "Portaler_ToggleFav";
        public const string RpcRename = "Portaler_Rename";

        private static bool _sentRestrictions;
        private static bool _lastRestrictions;

        private const float ScanInterval = 2f;
        private const float PendingTimeout = 60f;

        private static float _nextScan;
        private static string _lastHash = "";
        private static List<PortalInfo> _current = new List<PortalInfo>();

        // Persistent data (per world): next number per biome code, build time per portal.
        // Portals are identified by their position: ZDOIDs are re-numbered every time the world is loaded,
        // but a portal never moves.
        private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        private static readonly Dictionary<string, long> BuiltAt = new Dictionary<string, long>();
        // Favorites per player (Player.GetPlayerID) -> portal keys.
        private static readonly Dictionary<long, HashSet<string>> Favs = new Dictionary<long, HashSet<string>>();
        // Favorites shared with everybody.
        private static readonly HashSet<string> Shared = new HashSet<string>();
        private static string _dataFile;

        // Runtime mapping between this session's ZDOIDs and the persistent position keys.
        private static readonly Dictionary<ZDOID, string> IdToKey = new Dictionary<ZDOID, string>();
        private static readonly Dictionary<string, ZDOID> KeyToId = new Dictionary<string, ZDOID>();
        private static bool _loaded;

        // Portals reported by a builder that the server has not seen yet (ZDO sync can lag behind the RPC).
        private class Pending { public ZDOID Id; public Vector3 Pos; public float Since; }
        private static readonly List<Pending> PendingNames = new List<Pending>();

        private static readonly Regex AutoName = new Regex(@"^([A-Z]{2})-(\d{3,})$");

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        public static void Register()
        {
            ZRoutedRpc.instance.Register(RpcRequestList, new Action<long>(OnRequestList));
            ZRoutedRpc.instance.Register(RpcNewPortal, new Action<long, ZDOID, Vector3>(OnNewPortal));
            ZRoutedRpc.instance.Register(RpcRequestFavs, new Action<long, long>(OnRequestFavs));
            ZRoutedRpc.instance.Register(RpcToggleFav, new Action<long, long, ZDOID>(OnToggleFav));
            ZRoutedRpc.instance.Register(RpcRename, new Action<long, ZDOID, string, string>(OnRename));
            _loaded = false;
            _sentRestrictions = false;
            _lastHash = "";
            _current = new List<PortalInfo>();
            PendingNames.Clear();
        }

        public static void Tick()
        {
            if (!IsServer || ZDOMan.instance == null || ZRoutedRpc.instance == null) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + ScanInterval;

            EnsureLoaded();
            ProcessPending();
            Rebuild();

            // tell everybody when the restriction setting changes (e.g. edited in the config manager)
            bool r = Plugin.VanillaRestrictions.Value;
            if (!_sentRestrictions || r != _lastRestrictions)
            {
                _sentRestrictions = true;
                _lastRestrictions = r;
                SendSettings(ZRoutedRpc.Everybody);
            }
        }

        private static void SendSettings(long target)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(target, PortalClient.RpcSettings, Plugin.VanillaRestrictions.Value);
        }

        private static void OnRequestList(long sender)
        {
            if (!IsServer) return;
            EnsureLoaded();
            Rebuild();
            Send(sender);
            SendSettings(sender);
        }

        private static void OnRequestFavs(long sender, long playerId)
        {
            if (!IsServer) return;
            EnsureLoaded();
            SendFavs(sender, playerId);
        }

        private static void OnToggleFav(long sender, long playerId, ZDOID id)
        {
            if (!IsServer || playerId == 0) return;
            EnsureLoaded();
            if (!Favs.TryGetValue(playerId, out var set)) Favs[playerId] = set = new HashSet<string>();
            string key = KeyOf(id);
            if (key == null) return;
            // cycle: none -> personal -> shared -> none
            bool sharedChanged = true;
            if (Shared.Contains(key)) { Shared.Remove(key); set.Remove(key); }
            else if (set.Contains(key)) { set.Remove(key); Shared.Add(key); }
            else { set.Add(key); sharedChanged = false; }
            Save();
            SendFavs(sender, playerId);
            if (sharedChanged) BroadcastShared();
        }

        /// <summary>
        /// Rename from the list. The portal may be far away from everybody (not loaded anywhere),
        /// so the server writes the name itself when no player owns the portal.
        /// </summary>
        private static void OnRename(long sender, ZDOID id, string name, string author)
        {
            if (!IsServer) return;
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null) return;
            name = (name ?? "").Trim();
            if (name.Length > PortalClient.MaxNameLength) name = name.Substring(0, PortalClient.MaxNameLength);

            bool ownedByPlayer = zdo.HasOwner() && !zdo.IsOwner() && ZNet.instance.GetPeer(zdo.GetOwner()) != null;
            if (ownedByPlayer)
            {
                // a player has this portal loaded: its owner applies the change (vanilla RPC)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, id, "RPC_SetTag", name, author ?? "");
            }
            else
            {
                // nobody has it loaded: take it for a moment, write the name, give it back
                bool borrowed = !zdo.IsOwner();
                if (borrowed) zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.Set(ZDOVars.s_tag, name);
                zdo.Set(ZDOVars.s_tagauthor, author ?? "");
                if (borrowed) zdo.SetOwner(0L);
            }
            // Valheim saves portals in their own part of the world file, which is only written when
            // flagged as changed (vanilla does this when renaming at the portal). Without it the new name was lost.
            ZDOMan.instance.SetDirtyPortals();
            Plugin.Log.LogInfo($"Portal renamed to '{name}'");
            _nextScan = 0;
        }

        private static void SendFavs(long target, long playerId)
        {
            var pkg = new ZPackage();
            Favs.TryGetValue(playerId, out var set);
            if (KeyToId.Count == 0) Rebuild();
            var ids = new List<ZDOID>();
            if (set != null)
                foreach (var k in set)
                    if (KeyToId.TryGetValue(k, out var id)) ids.Add(id);
            WriteIds(pkg, ids);
            WriteIds(pkg, SharedIds());
            ZRoutedRpc.instance.InvokeRoutedRPC(target, PortalClient.RpcFavs, pkg);
        }

        private static void BroadcastShared()
        {
            var pkg = new ZPackage();
            WriteIds(pkg, SharedIds());
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, PortalClient.RpcShared, pkg);
        }

        private static List<ZDOID> SharedIds()
        {
            var ids = new List<ZDOID>();
            foreach (var k in Shared)
                if (KeyToId.TryGetValue(k, out var id)) ids.Add(id);
            return ids;
        }

        private static void WriteIds(ZPackage pkg, List<ZDOID> ids)
        {
            pkg.Write(ids.Count);
            foreach (var id in ids) pkg.Write(id);
        }

        private static string KeyOf(ZDOID id)
        {
            if (IdToKey.TryGetValue(id, out var key)) return key;
            var zdo = ZDOMan.instance?.GetZDO(id);
            return zdo != null ? Key(zdo.GetPosition()) : null;
        }

        private static void OnNewPortal(long sender, ZDOID id, Vector3 pos)
        {
            if (!IsServer) return;
            EnsureLoaded();
            PendingNames.Add(new Pending { Id = id, Pos = pos, Since = Time.time });
            ProcessPending();
        }

        private static void ProcessPending()
        {
            for (int i = PendingNames.Count - 1; i >= 0; i--)
            {
                var p = PendingNames[i];
                var zdo = ZDOMan.instance.GetZDO(p.Id);
                if (zdo == null)
                {
                    if (Time.time - p.Since > PendingTimeout) PendingNames.RemoveAt(i);
                    continue;
                }
                PendingNames.RemoveAt(i);

                string key = Key(zdo.GetPosition());
                if (!BuiltAt.ContainsKey(key)) BuiltAt[key] = DateTime.UtcNow.Ticks;

                // Only name it if the builder did not type a name already.
                if (string.IsNullOrEmpty(zdo.GetString(ZDOVars.s_tag, "")))
                {
                    string code = Biomes.CodeAt(zdo.GetPosition());
                    Counters.TryGetValue(code, out int last);
                    int next = last + 1;
                    Counters[code] = next;
                    string name = $"{code}-{next:000}";
                    // RPC_SetTag is handled by whoever owns the portal; everybody else ignores it.
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, p.Id, "RPC_SetTag", name, "");
                    Plugin.Log.LogInfo($"Named new portal {key} as {name}");
                }
                Save();
                _nextScan = 0; // rebuild soon so the name shows up quickly
            }
        }

        private static void Rebuild()
        {
            var list = new List<PortalInfo>();
            IdToKey.Clear();
            KeyToId.Clear();
            foreach (var zdo in ZDOMan.instance.GetPortalList())
            {
                if (zdo == null || !zdo.IsValid()) continue;
                string key = Key(zdo.GetPosition());
                IdToKey[zdo.m_uid] = key;
                KeyToId[key] = zdo.m_uid;
                BuiltAt.TryGetValue(key, out long built);
                string name = zdo.GetString(ZDOVars.s_tag, "");
                list.Add(new PortalInfo
                {
                    Id = zdo.m_uid,
                    Name = name,
                    Position = zdo.GetPosition(),
                    Rotation = zdo.GetRotation(),
                    BiomeCode = Biomes.CodeAt(zdo.GetPosition()),
                    BuiltAt = built,
                });
                SeedCounter(name);
            }

            // Forget build times of portals that no longer exist (destroyed).
            if (list.Count > 0)
            {
                var alive = new HashSet<string>(KeyToId.Keys);
                var gone = BuiltAt.Keys.Where(k => !alive.Contains(k)).ToList();
                bool changed = false;
                foreach (var k in gone) { BuiltAt.Remove(k); changed = true; }
                // ...and drop them from everyone's favorites.
                foreach (var set in Favs.Values)
                    if (set.RemoveWhere(k => !alive.Contains(k)) > 0) changed = true;
                if (Shared.RemoveWhere(k => !alive.Contains(k)) > 0) { changed = true; BroadcastShared(); }
                if (changed) Save();
            }

            string hash = Hash(list);
            if (hash == _lastHash) return;
            // any portal name change -> make sure the next world save writes the portals
            if (_lastHash.Length > 0) ZDOMan.instance.SetDirtyPortals();
            _lastHash = hash;
            _current = list;
            Send(ZRoutedRpc.Everybody);
        }

        private static void Send(long target)
        {
            var pkg = new ZPackage();
            PortalInfo.WriteList(pkg, _current);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, PortalClient.RpcList, pkg);
        }

        // Keep counting up: never hand out a number at or below one already in use.
        private static void SeedCounter(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var m = AutoName.Match(name);
            if (!m.Success) return;
            string code = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, out int n)) return;
            Counters.TryGetValue(code, out int last);
            if (n > last)
            {
                Counters[code] = n;
                Save();
            }
        }

        private static string Hash(List<PortalInfo> list)
        {
            var sb = new StringBuilder();
            foreach (var p in list.OrderBy(p => p.Id.UserID).ThenBy(p => p.Id.ID))
            {
                sb.Append(p.Id.UserID).Append(':').Append(p.Id.ID).Append('|').Append(p.Name).Append('|')
                  .Append(Mathf.RoundToInt(p.Position.x)).Append(',').Append(Mathf.RoundToInt(p.Position.z))
                  .Append('|').Append(p.BuiltAt).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Persistent portal identity: position in decimetres, e.g. "1234_305_-5678".</summary>
        private static string Key(Vector3 pos) =>
            Mathf.RoundToInt(pos.x * 10f) + "_" + Mathf.RoundToInt(pos.y * 10f) + "_" + Mathf.RoundToInt(pos.z * 10f);

        // ---------- persistence: <world>.portaler.txt next to the world save on the server ----------
        // Lives with the world (not in the mod or config folder), so it survives mod updates and reinstalls.

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Counters.Clear();
            BuiltAt.Clear();
            Favs.Clear();
            Shared.Clear();
            string world = ZNet.instance.GetWorldName() ?? "world";
            foreach (char c in Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            _dataFile = DataFilePath(world);

            string source = _dataFile;
            if (!File.Exists(source))
            {
                // older versions kept the file in BepInEx/config/Portaler (only the counters are still valid)
                string old = Path.Combine(Path.Combine(Paths.ConfigPath, "Portaler"), world + ".txt");
                if (!File.Exists(old)) { Plugin.Log.LogInfo($"New data file: {_dataFile}"); return; }
                source = old;
            }
            foreach (var line in File.ReadAllLines(source))
            {
                var parts = line.Split(' ');
                if (parts.Length == 2 && parts[0] == "shared") { Shared.Add(parts[1]); continue; }
                if (parts.Length != 3) continue;
                if (parts[0] == "counter" && int.TryParse(parts[2], out int n)) Counters[parts[1]] = n;
                else if (parts[1].Contains(":")) continue; // old ZDOID based entry, no longer valid
                else if (parts[0] == "built" && long.TryParse(parts[2], out long t)) BuiltAt[parts[1]] = t;
                else if (parts[0] == "fav" && !parts[2].Contains(":") && long.TryParse(parts[1], out long pid))
                {
                    if (!Favs.TryGetValue(pid, out var set)) Favs[pid] = set = new HashSet<string>();
                    set.Add(parts[2]);
                }
            }
            Plugin.Log.LogInfo($"Loaded {Counters.Count} counters, {BuiltAt.Count} build times and favorites of {Favs.Count} players from {source}");
            if (source != _dataFile) Save();
        }

        private static string DataFilePath(string world)
        {
            try
            {
                var w = ZNet.instance.GetWorld();
                string db = w?.GetDBPath();
                string dir = string.IsNullOrEmpty(db) ? null : Path.GetDirectoryName(db);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, world + ".portaler.txt");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("World folder not found, using config folder: " + e.Message);
            }
            string fallback = Path.Combine(Paths.ConfigPath, "Portaler");
            Directory.CreateDirectory(fallback);
            return Path.Combine(fallback, world + ".txt");
        }

        private static void Save()
        {
            if (_dataFile == null) return;
            try
            {
                var lines = new List<string>();
                foreach (var kv in Counters) lines.Add($"counter {kv.Key} {kv.Value}");
                foreach (var kv in BuiltAt) lines.Add($"built {kv.Key} {kv.Value}");
                foreach (var kv in Favs)
                    foreach (var k in kv.Value) lines.Add($"fav {kv.Key} {k}");
                foreach (var k in Shared) lines.Add($"shared {k}");
                string tmp = _dataFile + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray());
                if (File.Exists(_dataFile)) File.Delete(_dataFile);
                File.Move(tmp, _dataFile);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not save {_dataFile}: {e.Message}");
            }
        }
    }
}

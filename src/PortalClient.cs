using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Client half: keeps the server's portal list and this player's favorites, shows pins on the map,
    /// handles "choose destination" mode while the player stands in a portal, and teleports.
    /// </summary>
    public static class PortalClient
    {
        public const string RpcList = "Portaler_List";
        public const string RpcFavs = "Portaler_Favs";
        public const string RpcShared = "Portaler_Shared";
        public const string RpcSettings = "Portaler_Settings";
        public const string RpcTrips = "Portaler_Trips";

        /// <summary>Restriction setting received from the server (null = not received yet, use own config).</summary>
        private static bool? _serverRestrictions;
        private static bool _sourceAllowAllItems = true;

        public static bool Restricted => _serverRestrictions ?? Plugin.VanillaRestrictions.Value;

        /// <summary>Latest list from the server.</summary>
        public static List<PortalInfo> Portals = new List<PortalInfo>();

        /// <summary>This player's favorites (synced through the server).</summary>
        public static HashSet<ZDOID> Favorites = new HashSet<ZDOID>();

        /// <summary>Favorites shared with everybody (anyone can set or remove them).</summary>
        public static HashSet<ZDOID> SharedFavorites = new HashSet<ZDOID>();

        public enum FavState { None, Personal, Shared }

        /// <summary>True while the player stands in a portal and is choosing a destination.</summary>
        public static bool Active { get; private set; }
        public static ZDOID SourceId { get; private set; } = ZDOID.None;

        private static Transform _sourceTransform;
        private static Vector3 _sourcePos;

        /// <summary>Position of the portal the player stands in (for distance sorting).</summary>
        public static Vector3 SourcePosition => _sourceTransform != null ? _sourceTransform.position : _sourcePos;
        private static float _exitDistance = 1f;
        private static float _activatedAt;

        private const float LeaveDistance = 4f;
        private const float LeaveDistanceWorld = 9f; // list opened with L / C while looking at a portal
        private static float _leaveDistance = LeaveDistance;

        private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new Dictionary<ZDOID, Minimap.PinData>();
        private static bool _pinsDirty;
        private static Sprite _pinSprite;
        private static readonly HashSet<Sprite> OurSprites = new HashSet<Sprite>();
        private static float _nextForeignScan;
        private const float ForeignPinRadius = 4f;
        private static readonly AccessTools_FieldRef PinsField = new AccessTools_FieldRef();

        public static void Register()
        {
            ZRoutedRpc.instance.Register(RpcList, new Action<long, ZPackage>(OnList));
            ZRoutedRpc.instance.Register(RpcFavs, new Action<long, ZPackage>(OnFavs));
            ZRoutedRpc.instance.Register(RpcShared, new Action<long, ZPackage>(OnShared));
            ZRoutedRpc.instance.Register(RpcSettings, new Action<long, bool>((sender, restricted) => _serverRestrictions = restricted));
            ZRoutedRpc.instance.Register(RpcTrips, new Action<long, ZPackage>(OnTrips));
            Trips = new List<TripInfo>();
            _serverRestrictions = null;
            Portals = new List<PortalInfo>();
            Favorites = new HashSet<ZDOID>();
            SharedFavorites = new HashSet<ZDOID>();
            Pins.Clear();
            Active = false;
        }

        /// <summary>Goes up whenever the portal list, favorites, categories or trips change (the list redraws its rows then).</summary>
        public static int DataVersion { get; private set; }

        private static long LocalPlayerId => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;

        public static void RequestList()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcRequestList);
            if (LocalPlayerId != 0) ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcRequestFavs, LocalPlayerId);
        }

        private static void OnList(long sender, ZPackage pkg)
        {
            Portals = PortalInfo.ReadList(pkg);
            ById.Clear();
            foreach (var p in Portals) ById[p.Id] = p;
            _pinsDirty = true;
            DataVersion++;
        }

        private static readonly Dictionary<ZDOID, PortalInfo> ById = new Dictionary<ZDOID, PortalInfo>();

        /// <summary>The portal with this id from the server's list, or null.</summary>
        public static PortalInfo Get(ZDOID id) => id != ZDOID.None && ById.TryGetValue(id, out var p) ? p : null;

        /// <summary>Where a linked portal leads (null when it is open or its destination is gone).</summary>
        public static PortalInfo LinkOf(PortalInfo p) => p != null && p.IsLinked ? Get(p.LinkTarget) : null;

        /// <summary>Link a portal to one destination (to = null removes the link). Shared with everybody.</summary>
        public static void SetLink(PortalInfo from, PortalInfo to)
        {
            if (from == null || ZRoutedRpc.instance == null) return;
            if (to != null && to.Id == from.Id) return;
            from.LinkTarget = to != null ? to.Id : ZDOID.None; // show it right away
            DataVersion++;
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcSetLink, from.Id, to != null ? to.Id : ZDOID.None);
        }

        /// <summary>Set a portal's colour (0 = vanilla look). Shared with everybody.</summary>
        public static void SetColor(PortalInfo p, int color)
        {
            if (p == null || ZRoutedRpc.instance == null) return;
            p.Color = PortalColors.Clamp(color);
            DataVersion++;
            _pinsDirty = true;
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcSetColor, p.Id, p.Color);
        }

        /// <summary>This player's order of favorites (first = top). Favorites missing here go to the end.</summary>
        public static List<ZDOID> FavOrder = new List<ZDOID>();

        // package: personal ids, shared ids, personal order
        private static void OnFavs(long sender, ZPackage pkg)
        {
            Favorites = ReadIds(pkg);
            SharedFavorites = ReadIds(pkg);
            FavOrder = new List<ZDOID>(ReadIdList(pkg));
            DataVersion++;
        }

        private static List<ZDOID> ReadIdList(ZPackage pkg)
        {
            var list = new List<ZDOID>();
            if (pkg.GetPos() >= pkg.Size()) return list;
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) list.Add(pkg.ReadZDOID());
            return list;
        }

        /// <summary>Favorites in this player's own order; ones not ordered yet (e.g. newly shared by others) at the end.</summary>
        public static List<PortalInfo> OrderedFavorites(IEnumerable<PortalInfo> from)
        {
            var favs = from.Where(IsFavorite).ToList();
            var rank = new Dictionary<ZDOID, int>();
            for (int i = 0; i < FavOrder.Count; i++) if (!rank.ContainsKey(FavOrder[i])) rank[FavOrder[i]] = i;
            return favs.OrderBy(p => rank.TryGetValue(p.Id, out int r) ? r : int.MaxValue)
                       .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Drag and drop result: store the new order locally and on the server.</summary>
        public static void SetFavoriteOrder(List<PortalInfo> ordered)
        {
            FavOrder = ordered.Select(p => p.Id).ToList();
            DataVersion++;
            if (ZRoutedRpc.instance == null || LocalPlayerId == 0) return;
            var pkg = new ZPackage();
            pkg.Write(FavOrder.Count);
            foreach (var id in FavOrder) pkg.Write(id);
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcSetFavOrder, LocalPlayerId, pkg);
        }

        /// <summary>Set a portal's category (shared with everybody).</summary>
        public static void SetCategory(PortalInfo p, int category)
        {
            if (p == null || ZRoutedRpc.instance == null) return;
            category = Categories.Clamp(category);
            foreach (var x in Portals) if (x.Id == p.Id) x.Category = category; // show it right away
            p.Category = category;
            _pinsDirty = true;
            DataVersion++;
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcSetCategory, p.Id, category);
        }

        private static void OnShared(long sender, ZPackage pkg) { SharedFavorites = ReadIds(pkg); DataVersion++; }

        // ---------- other players' last trips (shared through the server) ----------

        public class TripInfo
        {
            public long PlayerId;
            public string Name;
            public ZDOID PortalId;
            public long AgeAtReceive;     // seconds, as told by the server
            public float ReceivedAt;      // Time.realtimeSinceStartup
            public float AgeSeconds => AgeAtReceive + (Time.realtimeSinceStartup - ReceivedAt);
        }

        /// <summary>Last portal each player travelled to (everybody, including this player).</summary>
        public static List<TripInfo> Trips = new List<TripInfo>();

        private static void OnTrips(long sender, ZPackage pkg)
        {
            var list = new List<TripInfo>();
            int n = pkg.ReadInt();
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++)
                list.Add(new TripInfo { PlayerId = pkg.ReadLong(), Name = pkg.ReadString(), PortalId = pkg.ReadZDOID(), AgeAtReceive = pkg.ReadLong(), ReceivedAt = now });
            Trips = list;
            DataVersion++;
        }

        private static string LocalPlayerName => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";

        private static HashSet<string> OnlineNames()
        {
            var online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (ZNet.instance != null)
                    foreach (var pi in ZNet.instance.GetPlayerList())
                        if (!string.IsNullOrEmpty(pi.m_name)) online.Add(pi.m_name);
            }
            catch { }
            online.Remove(LocalPlayerName);
            return online;
        }

        /// <summary>Players that can be followed: the other players online right now (never yourself). Value = online.</summary>
        public static List<KeyValuePair<string, bool>> KnownPlayers()
        {
            return OnlineNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                .Select(x => new KeyValuePair<string, bool>(x, true)).ToList();
        }

        public static bool IsTracked(string name)
        {
            var set = Prefs.GetTracked();
            return set == null || set.Contains(name);
        }

        /// <summary>Last trips of the online players this character follows, newest first.</summary>
        public static List<KeyValuePair<TripInfo, PortalInfo>> TrackedTrips()
        {
            var result = new List<KeyValuePair<TripInfo, PortalInfo>>();
            var online = OnlineNames();
            foreach (var t in Trips.OrderBy(t => t.AgeSeconds))
            {
                if (t.PlayerId == LocalPlayerId || string.Equals(t.Name, LocalPlayerName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!online.Contains(t.Name ?? "")) continue; // only players who are here now
                if (!IsTracked(t.Name)) continue;
                var p = Portals.FirstOrDefault(x => x.Id == t.PortalId);
                if (p != null) result.Add(new KeyValuePair<TripInfo, PortalInfo>(t, p));
            }
            return result;
        }

        private static HashSet<ZDOID> ReadIds(ZPackage pkg)
        {
            var set = new HashSet<ZDOID>();
            if (pkg.GetPos() >= pkg.Size()) return set;
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) set.Add(pkg.ReadZDOID());
            return set;
        }

        public static FavState GetFav(PortalInfo p) =>
            SharedFavorites.Contains(p.Id) ? FavState.Shared :
            Favorites.Contains(p.Id) ? FavState.Personal : FavState.None;

        public static bool IsFavorite(PortalInfo p) => GetFav(p) != FavState.None;

        // ---------- rename from the list ----------

        public const int MaxNameLength = 25;

        /// <summary>Opens Valheim's own text box (it blocks game keys while typing).</summary>
        private static PortalInfo _renamePending;
        private static int _focusFrames;
        private static readonly System.Reflection.FieldInfo InputFieldInfo =
            HarmonyLib.AccessTools.Field(typeof(TextInput), "m_inputField");

        public static void BeginRename(PortalInfo p)
        {
            if (p != null) _renamePending = p;
        }

        private static void UpdateRename()
        {
            if (_renamePending != null)
            {
                var p = _renamePending;
                _renamePending = null;
                if (TextInput.instance != null && !TextInput.IsVisible())
                {
                    TextInput.instance.RequestText(new RenameReceiver(p), "Rename portal", MaxNameLength);
                    _focusFrames = 15;
                }
            }

            // With the big map open the text box does not always get keyboard focus by itself: give it focus.
            if (_focusFrames > 0)
            {
                _focusFrames--;
                if (!TextInput.IsVisible()) { _focusFrames = 0; return; }
                try
                {
                    var field = InputFieldInfo?.GetValue(TextInput.instance) as UnityEngine.UI.Selectable;
                    if (field == null) return;
                    var focused = field.GetType().GetProperty("isFocused");
                    if (focused != null && (bool)focused.GetValue(field, null)) { _focusFrames = 0; return; }
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(field.gameObject);
                    field.Select();
                    field.GetType().GetMethod("ActivateInputField", System.Type.EmptyTypes)?.Invoke(field, null);
                }
                catch (Exception e)
                {
                    _focusFrames = 0;
                    Plugin.Log.LogWarning("Could not focus rename box: " + e.Message);
                }
            }
        }

        private class RenameReceiver : TextReceiver
        {
            private readonly PortalInfo _portal;
            public RenameReceiver(PortalInfo p) { _portal = p; }

            public string GetText() => _portal.Name ?? "";

            public void SetText(string text) => Rename(_portal, text);
        }

        /// <summary>Rename a portal for everybody (through the server).</summary>
        public static void Rename(PortalInfo portal, string text)
        {
            if (portal == null) return;
            text = (text ?? "").Trim();
            if (text.Length > MaxNameLength) text = text.Substring(0, MaxNameLength);
            if (text == (portal.Name ?? "") || ZRoutedRpc.instance == null) return;
            string author = "";
            try { author = Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.ToString(); }
            catch { }
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcRename, portal.Id, text, author);
            // show it right away; the server confirms with the next list update
            foreach (var p in Portals) if (p.Id == portal.Id) p.Name = text;
            portal.Name = text;
            _pinsDirty = true;
            DataVersion++;
        }

        /// <summary>Click cycle: none -> mine -> shared with everyone -> none.</summary>
        public static void ToggleFavorite(PortalInfo p)
        {
            if (ZRoutedRpc.instance == null || LocalPlayerId == 0) return;
            // Optimistic local update; the server answers with the stored lists.
            switch (GetFav(p))
            {
                case FavState.None: Favorites.Add(p.Id); if (!FavOrder.Contains(p.Id)) FavOrder.Add(p.Id); break;
                case FavState.Personal: Favorites.Remove(p.Id); SharedFavorites.Add(p.Id); break;
                default: SharedFavorites.Remove(p.Id); Favorites.Remove(p.Id); break;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcToggleFav, LocalPlayerId, p.Id);
        }

        // ---------- choose-destination mode ----------

        /// <summary>
        /// The player walked into a portal. A linked portal sends him straight to its destination,
        /// any other portal opens the map and the list.
        /// </summary>
        public static void OnPortalEntered(TeleportWorld portal)
        {
            var nview = portal.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            var here = Get(nview.GetZDO().m_uid);
            var dest = LinkOf(here);
            var player = Player.m_localPlayer;
            if (dest != null && player != null && !player.IsTeleporting())
            {
                SetSource(portal, nview);
                player.Message(MessageHud.MessageType.Center, "Travelling to " + dest.DisplayName, 0, null);
                TeleportTo(dest);
                SourceId = ZDOID.None;
                _sourceTransform = null;
                return;
            }
            Activate(portal);
        }

        private static void SetSource(TeleportWorld portal, ZNetView nview)
        {
            SourceId = nview.GetZDO().m_uid;
            _sourceTransform = portal.transform;
            _sourcePos = portal.transform.position;
            _exitDistance = portal.m_exitDistance > 0 ? portal.m_exitDistance : 1f;
            _sourceAllowAllItems = portal.m_allowAllItems;
        }

        public static void Activate(TeleportWorld portal, bool fromWorldKey = false)
        {
            var nview = portal.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            if (Minimap.instance == null) return;
            _leaveDistance = fromWorldKey ? LeaveDistanceWorld : LeaveDistance;

            SourceId = nview.GetZDO().m_uid;
            _sourceTransform = portal.transform;
            _sourcePos = portal.transform.position;
            _exitDistance = portal.m_exitDistance > 0 ? portal.m_exitDistance : 1f;
            _sourceAllowAllItems = portal.m_allowAllItems;
            _activatedAt = Time.time;
            Active = true;
            _pinsDirty = true; // re-add pins in case the map removed any
            RequestList();
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            PortalUI.OnOpened();
            if (!fromWorldKey) PortalUI.CheckGrave(); // first portal after a death: "You died" dialog
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public static string PosText(Vector3 p) => p.x.ToString("0.#", Inv) + ";" + p.y.ToString("0.#", Inv) + ";" + p.z.ToString("0.#", Inv);

        /// <summary>The portal standing at a position written by <see cref="PosText"/> (within 2 m), or null.</summary>
        public static PortalInfo PortalAt(string posText)
        {
            if (string.IsNullOrEmpty(posText)) return null;
            var c = posText.Split(';');
            if (c.Length != 3 || !float.TryParse(c[0], System.Globalization.NumberStyles.Float, Inv, out float x) ||
                !float.TryParse(c[1], System.Globalization.NumberStyles.Float, Inv, out float y) ||
                !float.TryParse(c[2], System.Globalization.NumberStyles.Float, Inv, out float z)) return null;
            var pos = new Vector3(x, y, z);
            PortalInfo best = null; float bestD = 2f;
            foreach (var p in Portals)
            {
                float d = Vector3.Distance(p.Position, pos);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Newest grave marker ("death" pin) on this player's map; day = the in-game day it shows, 0 if unknown.</summary>
        public static bool NewestGrave(out Vector3 pos, out int day)
        {
            pos = Vector3.zero; day = -1;
            var all = Minimap.instance != null ? PinsField.Get(Minimap.instance) : null;
            if (all == null) return false;
            foreach (var pin in all)
            {
                if (pin == null || pin.m_type != Minimap.PinType.Death) continue;
                int d = 0;
                var m = System.Text.RegularExpressions.Regex.Match(pin.m_name ?? "", @"\d+");
                if (m.Success) int.TryParse(m.Value, out d);
                if (d >= day) { day = d; pos = pin.m_pos; } // same day: the later pin wins
            }
            return day >= 0;
        }

        public static void Deactivate(bool closeMap)
        {
            if (!Active) return;
            Active = false;
            // put the map view back where it was before hovering moved it
            if (_saved && Minimap.instance != null) { MapOffset(Minimap.instance) = _savedOffset; Minimap.instance.LargeZoom = _savedZoom; }
            _saved = false; _moving = false; _returning = false;
            StopGlow();
            SourceId = ZDOID.None;
            _sourceTransform = null;
            if (closeMap && Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);
        }

        public static void Tick()
        {
            if (_pinsDirty) { var pt = Perf.Begin(); RefreshPins(); Perf.End(Perf.S.PinRefresh, pt); }
            UpdateRename();
            if (Time.time >= _nextForeignScan)
            {
                _nextForeignScan = Time.time + 0.5f;
                var st = Perf.Begin();
                ReskinForeignPins();
                Perf.End(Perf.S.PinScan, st);
            }
            if (!Active) { _hoverPortal = null; _hoverSince = 0f; StopGlow(); _saved = false; FlowLine.Hide(); WorldKeys(); return; }

            var player = Player.m_localPlayer;
            if (player == null || Minimap.instance == null) { Deactivate(false); return; }
            UpdateMapFocus();

            // The player closed the map (Esc / M) -> leave the mode.
            if (Time.time - _activatedAt > 0.3f && Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                Deactivate(false);
                return;
            }

            // The portal was destroyed, or the player walked away from it.
            if (_sourceTransform == null ||
                Vector3.Distance(player.transform.position, _sourceTransform.position) > _leaveDistance)
            {
                Deactivate(true);
            }
        }

        // ---------- keys while looking at a portal in the world ----------

        private static ZDOID _confirmUnlink = ZDOID.None;
        private static float _confirmUntil;

        /// <summary>The portal the player is looking at (hover), or null.</summary>
        public static TeleportWorld LookedAtPortal()
        {
            var player = Player.m_localPlayer;
            var go = player != null ? player.GetHoverObject() : null;
            return go != null ? go.GetComponentInParent<TeleportWorld>() : null;
        }

        private static bool GameTakesKeys()
        {
            if (Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen()) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            var player = Player.m_localPlayer;
            return player != null && !player.InPlaceMode() && !player.IsTeleporting();
        }

        /// <summary>L = link / remove the link, C = edit (name, type, colour), for the portal the player looks at.</summary>
        private static void WorldKeys()
        {
            bool link = Input.GetKeyDown(Plugin.LinkKey.Value), color = Input.GetKeyDown(Plugin.ColorKey.Value);
            if (!link && !color) return;
            if (!GameTakesKeys()) return;
            var tw = LookedAtPortal();
            var nview = tw != null ? tw.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid()) return;
            var p = Get(nview.GetZDO().m_uid);
            if (p == null) return;
            var player = Player.m_localPlayer;

            if (color)
            {
                PortalUI.OpenRename(p); // the Edit portal window, right here in the world
                return;
            }
            var dest = LinkOf(p);
            if (dest == null)
            {
                Activate(tw, true);
                PortalUI.BeginPick(p);
                return;
            }
            // linked: second press within 3 s removes the link
            if (_confirmUnlink == p.Id && Time.time < _confirmUntil)
            {
                SetLink(p, null);
                _confirmUnlink = ZDOID.None;
                player.Message(MessageHud.MessageType.Center, $"{p.DisplayName} is an open portal again", 0, null);
            }
            else
            {
                _confirmUnlink = p.Id;
                _confirmUntil = Time.time + 3f;
                player.Message(MessageHud.MessageType.Center, $"Press {Plugin.LinkKey.Value} again to remove the link to {dest.DisplayName}", 0, null);
            }
        }

        /// <summary>
        /// A portal's hover text: the game's "[E] Set tag" becomes "[E] Edit portal" (E opens Portaler's window),
        /// plus where it leads and the L key.
        /// </summary>
        public static string HoverText(TeleportWorld tw, string vanilla)
        {
            var nview = tw != null ? tw.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid()) return vanilla;
            var p = Get(nview.GetZDO().m_uid);
            if (p == null) return vanilla;
            string text = vanilla ?? "";
            if (Localization.instance != null)
            {
                string setTag = Localization.instance.Localize("$piece_portal_settag");
                if (!string.IsNullOrEmpty(setTag)) text = text.Replace(setTag, "Edit portal");
            }
            var dest = LinkOf(p);
            var sb = new System.Text.StringBuilder(text);
            if (dest != null) sb.Append("\n<color=#ffbe3c>Leads to: ").Append(dest.DisplayName).Append("</color>");
            sb.Append("\n[<color=yellow><b>").Append(Plugin.LinkKey.Value).Append("</b></color>] ").Append(dest != null ? "Remove link" : "Link");
            return sb.ToString();
        }

        // ---------- hover a name -> show it on the map ----------

        private static readonly HarmonyLib.AccessTools.FieldRef<Minimap, Vector3> MapOffset =
            HarmonyLib.AccessTools.FieldRefAccess<Minimap, Vector3>("m_mapOffset");

        private static PortalInfo _hoverPortal;
        private static float _hoverSince, _leftListAt;
        private static ZDOID _glowId = ZDOID.None;
        private static Canvas _mapCanvas;
        private static readonly Dictionary<Minimap.PinData, float> PinScale = new Dictionary<Minimap.PinData, float>();

        /// <summary>
        /// Called every frame (LateUpdate, after the map updated). Our pins have a steady size:
        /// the previewed portal is drawn big (and stays big), a pin under the mouse grows a little.
        /// The size changes smoothly instead of the vanilla pulsing.
        /// </summary>
        /// <summary>
        /// Draw a UI element above everything else on the map without moving it in the hierarchy
        /// (the game rebuilds pins that are moved): a nested canvas with its own, higher sorting order.
        /// order = 0 switches it back to normal drawing.
        /// </summary>
        public static void SetOnTop(GameObject go, int order)
        {
            if (go == null) return;
            var c = go.GetComponent<Canvas>();
            if (order <= 0)
            {
                if (c != null && c.overrideSorting) c.overrideSorting = false;
                return;
            }
            if (c == null) c = go.AddComponent<Canvas>();
            if (!c.overrideSorting) c.overrideSorting = true;
            int baseOrder = MapSortingOrder();
            if (c.sortingOrder != baseOrder + order) c.sortingOrder = baseOrder + order;
            var root = _mapCanvas != null ? _mapCanvas.rootCanvas : null;
            if (root != null && c.sortingLayerID != root.sortingLayerID) c.sortingLayerID = root.sortingLayerID;
        }

        private static int MapSortingOrder()
        {
            var map = Minimap.instance;
            if (_mapCanvas == null && map != null && map.m_pinRootLarge != null) _mapCanvas = map.m_pinRootLarge.GetComponentInParent<Canvas>();
            var root = _mapCanvas != null ? _mapCanvas.rootCanvas : null;
            return root != null ? root.sortingOrder : 0;
        }

        /// <summary>The portal whose pin is under the mouse on the large map (null when none).</summary>
        public static PortalInfo HoveredPin { get; private set; }

        public static void ScalePins()
        {
            var map = Minimap.instance;
            if (map == null) return;
            bool large = map.m_mode == Minimap.MapMode.Large;
            // while choosing a destination our pins are drawn above every other pin and label
            bool lift = large && Active;
            Vector2 mouse = ZInput.pointerPosition;
            // the map's canvas may use a camera; hit tests need it
            if (_mapCanvas == null && map.m_pinRootLarge != null) _mapCanvas = map.m_pinRootLarge.GetComponentInParent<Canvas>();
            Camera cam = _mapCanvas != null && _mapCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _mapCanvas.worldCamera : null;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f);
            ZDOID hoveredPin = ZDOID.None;
            foreach (var kv in Pins)
            {
                var pin = kv.Value;
                if (pin == null || pin.m_uiElement == null) continue;
                if (large && hoveredPin == ZDOID.None && RectTransformUtility.RectangleContainsScreenPoint(pin.m_uiElement, mouse, cam)) hoveredPin = kv.Key;
                ById.TryGetValue(kv.Key, out var info);
                PortalVisuals.PinRing(pin, info != null ? info.Color : 0);
                float target = 1f;
                if (large && Active)
                {
                    if (kv.Key == _glowId) target = Plugin.PinSizePreview.Value;
                    else if (RectTransformUtility.RectangleContainsScreenPoint(pin.m_uiElement, mouse, cam)) target = Plugin.PinSizeHover.Value;
                }
                else if (large && RectTransformUtility.RectangleContainsScreenPoint(pin.m_uiElement, mouse, cam)) target = Plugin.PinSizeHover.Value;

                PinScale.TryGetValue(pin, out float cur);
                if (cur <= 0f) cur = 1f;
                cur = Mathf.Lerp(cur, target, k);
                if (Mathf.Abs(cur - target) < 0.005f) cur = target;
                PinScale[pin] = cur;
                pin.m_uiElement.localScale = new Vector3(cur, cur, 1f);
                var name = pin.m_NamePinData?.PinNameGameObject;
                if (name != null) name.transform.localScale = new Vector3(cur, cur, 1f);

                // order: fiery line 5, portal pins 10, previewed pin 12, names 11 / 13
                bool big = kv.Key == _glowId;
                SetOnTop(pin.m_uiElement.gameObject, lift ? (big ? 12 : 10) : 0);
                if (name != null) SetOnTop(name, lift ? (big ? 13 : 11) : 0);
                if (lift && pin.m_iconElement != null && pin.m_iconElement.color != Color.white) pin.m_iconElement.color = Color.white;
            }
            HoveredPin = Get(hoveredPin);
        }
        private static bool _saved, _returning;
        private static Vector3 _savedOffset, _targetOffset;
        private static float _savedZoom, _targetZoom;
        private static bool _moving;
        // eased glide: from -> to over a fixed time
        private static Vector3 _fromOffset;
        private static float _fromZoom, _moveStart, _moveTime;

        private static void StartMove(Minimap map, Vector3 offset, float zoom, bool instant)
        {
            _targetOffset = offset;
            _targetZoom = zoom;
            _fromOffset = MapOffset(map);
            _fromZoom = map.LargeZoom;
            _moveStart = Time.unscaledTime;
            _moveTime = instant ? 0f : Mathf.Max(0.05f, Plugin.HoverMoveSeconds.Value);
            _moving = true;
        }

        /// <summary>
        /// View for a preview. Close portals (within HoverMaxFitMeters): frame both, with extra room around the target.
        /// Far portals: show the target at the usual close zoom, without gliding across the world.
        /// </summary>
        private static void PreviewView(Minimap map, Vector3 from, Vector3 to, out Vector3 center, out float zoom, out bool instant)
        {
            float worldSize = Mathf.Max(1f, map.m_textureSize * map.m_pixelSize);
            float regionZoom = Mathf.Clamp(Plugin.HoverZoomMeters.Value / worldSize, map.m_minZoom, map.m_maxZoom);
            float dist = Utils.DistanceXZ(from, to);

            if (dist > Plugin.HoverMaxFitMeters.Value)
            {
                center = to; zoom = regionZoom; instant = true;
                return;
            }

            // box around both portals; more room around the target than around your own portal
            float mSrc = 150f, mDst = 400f;
            float minX = Mathf.Min(from.x - mSrc, to.x - mDst), maxX = Mathf.Max(from.x + mSrc, to.x + mDst);
            float minZ = Mathf.Min(from.z - mSrc, to.z - mDst), maxZ = Mathf.Max(from.z + mSrc, to.z + mDst);

            // the list covers the left part of the screen: fit the box into the part right of it
            var rect = map.m_mapImageLarge != null ? map.m_mapImageLarge.rectTransform.rect : new Rect(0, 0, Screen.width, Screen.height);
            float aspect = rect.height > 0 ? rect.width / rect.height : (float)Screen.width / Screen.height;
            float hidden = Mathf.Clamp01(PortalUI.PanelRight / Mathf.Max(1f, Screen.width));
            float visibleW = Mathf.Max(0.3f, 1f - hidden);

            float zoomH = (maxZ - minZ) / worldSize;
            float zoomW = (maxX - minX) / (worldSize * aspect * visibleW);
            zoom = Mathf.Clamp(Mathf.Max(zoomH, zoomW), map.m_minZoom, map.m_maxZoom);

            // shift the map so the box sits in the middle of the visible part
            float viewWidthMeters = zoom * worldSize * aspect;
            float shift = hidden * 0.5f * viewWidthMeters;
            center = new Vector3((minX + maxX) * 0.5f - shift, to.y, (minZ + maxZ) * 0.5f);
            instant = false;
        }

        private static Vector3 _lastOffset;
        private static float _lastZoom;

        private static void UpdateMapFocus()
        {
            var map = Minimap.instance;
            if (!Plugin.HoverShowsMap.Value || map.m_mode != Minimap.MapMode.Large) { FlowLine.Hide(); return; }
            var player = Player.m_localPlayer;
            var hovered = PortalUI.HoveredPortal;

            // The player moved or zoomed the map himself (mouse is off the list): leave the view where it is.
            if (_saved && hovered == null &&
                ((MapOffset(map) - _lastOffset).sqrMagnitude > 1f || Mathf.Abs(map.LargeZoom - _lastZoom) > 0.0001f))
            {
                _saved = false; _moving = false; _returning = false;
            }

            if (hovered != null)
            {
                _leftListAt = 0f;
                if (_hoverPortal == null || hovered.Id != _hoverPortal.Id) { _hoverPortal = hovered; _hoverSince = Time.unscaledTime; }
                if (Time.unscaledTime - _hoverSince >= Plugin.HoverDelay.Value && _glowId != hovered.Id)
                {
                    if (!_saved) { _saved = true; _savedOffset = MapOffset(map); _savedZoom = map.LargeZoom; }
                    PreviewView(map, SourcePosition, hovered.Position, out Vector3 center, out float zoom, out bool instant);
                    StartMove(map, center - player.transform.position, zoom, instant);
                    _returning = false;
                    SetGlow(hovered.Id);
                }
            }
            else
            {
                _hoverPortal = null;
                if (_saved && !_returning)
                {
                    if (_leftListAt == 0f) _leftListAt = Time.unscaledTime;
                    // stay on the previewed portal for a while, then glide back
                    if (Time.unscaledTime - _leftListAt > Plugin.HoverReturnDelay.Value)
                    {
                        StopGlow();
                        if (Plugin.HoverReturn.Value) { StartMove(map, _savedOffset, _savedZoom, false); _returning = true; }
                        else { _saved = false; _moving = false; }
                    }
                }
            }

            if (_moving)
            {
                // calm glide: soft start, soft stop (smoothstep over HoverMoveSeconds); 0 s = instant
                float t = _moveTime <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - _moveStart) / _moveTime);
                float e = t * t * (3f - 2f * t);
                Vector3 off = Vector3.Lerp(_fromOffset, _targetOffset, e);
                // zoom eases in log space so zooming in and out feel equally calm
                float zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1e-5f, _fromZoom)), Mathf.Log(Mathf.Max(1e-5f, _targetZoom)), e));
                if (t >= 1f)
                {
                    off = _targetOffset; zoom = _targetZoom;
                    _moving = false;
                    if (_returning) { _returning = false; _saved = false; }
                }
                MapOffset(map) = off;
                map.LargeZoom = zoom;
            }
            _lastOffset = MapOffset(map);
            _lastZoom = map.LargeZoom;

            // fiery line from the portal we stand in to the previewed one
            var target = _glowId != ZDOID.None ? Portals.FirstOrDefault(p => p.Id == _glowId) : null;
            if (target != null && target.Id != SourceId && Plugin.HoverLine.Value)
            {
                var lt = Perf.Begin();
                FlowLine.Show(map, SourcePosition, target.Position);
                Perf.End(Perf.S.Line, lt);
            }
            else FlowLine.Hide();
        }

        private static void SetGlow(ZDOID id)
        {
            StopGlow();
            _glowId = id; // the previewed pin stays big as long as the fiery line is shown
        }

        private static void StopGlow()
        {
            _glowId = ZDOID.None;
        }

        /// <summary>The portals this character last travelled from, newest first (only existing ones, never the current one).</summary>
        public static List<PortalInfo> BackPortals
        {
            get
            {
                var result = new List<PortalInfo>();
                foreach (var pos in Prefs.GetBackList())
                {
                    PortalInfo best = null;
                    float bestD = 1.5f;
                    foreach (var p in Portals)
                    {
                        float d = Utils.DistanceXZ(p.Position, pos);
                        if (d < bestD && Mathf.Abs(p.Position.y - pos.y) < 3f) { bestD = d; best = p; }
                    }
                    if (best != null && best.Id != SourceId && !result.Contains(best)) result.Add(best);
                }
                return result.Take(Prefs.BackCount).ToList();
            }
        }

        public static IEnumerable<PortalInfo> Destinations =>
            Portals.Where(p => p.Id != SourceId);

        public static List<PortalInfo> LastBuilt =>
            Portals.Where(p => p.BuiltAt > 0).OrderByDescending(p => p.BuiltAt)
                   .Take(Prefs.LastBuiltCount).ToList();

        public static void TeleportTo(PortalInfo target)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            if (target.Id == SourceId)
            {
                player.Message(MessageHud.MessageType.Center, "You are already at this portal", 0, null);
                return;
            }

            // Destroyed meanwhile? It is no longer in the server's list.
            if (!Portals.Any(p => p.Id == target.Id))
            {
                player.Message(MessageHud.MessageType.Center, "This portal no longer exists", 0, null);
                return;
            }

            if (Restricted && !PassesVanillaRules(player)) return;

            // Like vanilla: land in front of the target portal, facing away from it.
            Vector3 exit = target.Position + target.Rotation * Vector3.forward * _exitDistance + Vector3.up;
            var pt = Perf.Begin();
            Prefs.PushBack(SourcePosition); // the "Back to" rows will offer the portal we leave from
            Prefs.SetMisc("Dest", PosText(target.Position)); // "Last trip" in the grave dialog
            // tell the server (and so everybody) where we went: "Players' last trips"
            if (ZRoutedRpc.instance != null && LocalPlayerId != 0)
                ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcTrip, LocalPlayerId, player.GetPlayerName(), target.Id);
            Perf.TeleportStarted(target, exit, Utils.DistanceXZ(SourcePosition, target.Position));
            Perf.End(Perf.S.Travel, pt);
            pt = Perf.Begin();
            Deactivate(true);
            Perf.End(Perf.S.MapClose, pt);
            pt = Perf.Begin();
            // tames that follow you, the mount you ride and the cart you pull come along
            var companions = PortalCompanions.Collect(player, _sourceAllowAllItems, Restricted);
            if (companions?.Mount != null) player.StopDoodadControl(); // off the saddle before the jump
            QuickArrival.Begin(player);
            bool started = player.TeleportTo(exit, target.Rotation, true);
            if (started && companions != null) PortalCompanions.Send(player, companions, exit, target.Rotation);
            Perf.End(Perf.S.TpStart, pt);
            Plugin.Log.LogInfo($"Teleporting to {target.DisplayName}");
        }

        /// <summary>The game's own portal checks (only used when VanillaRestrictions is on).</summary>
        private static bool PassesVanillaRules(Player player)
        {
            var zs = ZoneSystem.instance;
            if (zs != null && zs.GetGlobalKey(GlobalKeys.NoPortals))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_blocked", 0, null);
                return false;
            }
            if (zs != null && zs.GetGlobalKey(GlobalKeys.NoBossPortals))
            {
                bool bossActive = (RandEventSystem.instance != null && !string.IsNullOrEmpty(RandEventSystem.instance.GetBossEvent()))
                                  || zs.GetGlobalKey(GlobalKeys.activeBosses, out float _);
                if (bossActive)
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss", 0, null);
                    return false;
                }
            }
            if (!player.IsTeleportable(_sourceAllowAllItems))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noteleport", 0, null);
                return false;
            }
            return true;
        }

        /// <summary>Map click while choosing: teleport to the portal pin under the cursor.</summary>
        /// <summary>The portal whose pin is at this map point (within radius), or null.</summary>
        public static PortalInfo PortalAtMapPoint(Vector3 worldPoint, float radius)
        {
            PortalInfo best = null;
            float bestDist = radius;
            foreach (var p in Portals)
            {
                float d = Utils.DistanceXZ(p.Position, worldPoint);
                if (d <= bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        public static bool TryTeleportAtMapPoint(Vector3 worldPoint, float radius)
        {
            if (PortalUI.Picking)
            {
                // choosing an end of a link: any portal, also the one you stand in
                var pick = PortalAtMapPoint(worldPoint, radius);
                if (pick == null) return false;
                PortalUI.PickTarget(pick);
                return true;
            }
            PortalInfo best = null;
            float bestDist = radius;
            foreach (var p in Destinations)
            {
                float d = Utils.DistanceXZ(p.Position, worldPoint);
                if (d <= bestDist) { bestDist = d; best = p; }
            }
            if (best == null) return false;
            if (PortalUI.Picking) PortalUI.PickTarget(best);
            else TeleportTo(best);
            return true;
        }

        // ---------- map pins ----------

        private static void RefreshPins()
        {
            if (Minimap.instance == null) return;
            _pinsDirty = false;

            foreach (var pin in Pins.Values) Minimap.instance.RemovePin(pin);
            Pins.Clear();
            PinScale.Clear();

            if (_pinSprite == null) _pinSprite = Art.PinSprite();
            foreach (var p in Portals)
            {
                var pin = Minimap.instance.AddPin(p.Position, Minimap.PinType.Icon4, p.DisplayName,
                    false, false, 0L, default(Splatform.PlatformUserID));
                pin.m_icon = Art.PinSprite(p.Category);
                Pins[p.Id] = pin;
                OurSprites.Add(pin.m_icon);
            }
        }

        /// <summary>
        /// Other mods (auto map pins etc.) may put their own marker on portals, which gave double icons.
        /// Those pins are kept (their data is untouched) but not drawn while a Portaler pin sits on the same spot.
        /// </summary>
        private static readonly List<Minimap.PinData> HiddenForeign = new List<Minimap.PinData>();

        private static void ReskinForeignPins()
        {
            HiddenForeign.Clear();
            if (Minimap.instance == null || Portals.Count == 0) return;
            var all = PinsField.Get(Minimap.instance);
            if (all == null) return;
            var ours = new HashSet<Minimap.PinData>(Pins.Values);
            foreach (var pin in all)
            {
                if (pin == null || ours.Contains(pin) || OurSprites.Contains(pin.m_icon)) continue;
                if (pin.m_type == Minimap.PinType.Player || pin.m_type == Minimap.PinType.Death ||
                    pin.m_type == Minimap.PinType.Ping || pin.m_type == Minimap.PinType.Shout) continue;
                foreach (var p in Portals)
                    if (Utils.DistanceXZ(p.Position, pin.m_pos) <= ForeignPinRadius) { HiddenForeign.Add(pin); break; }
            }
        }

        /// <summary>Called after the map updated its pins every frame: keep the duplicates invisible.</summary>
        public static void HideForeignPins()
        {
            foreach (var pin in HiddenForeign)
            {
                if (pin == null) continue;
                if (pin.m_uiElement != null && pin.m_uiElement.gameObject.activeSelf)
                    pin.m_uiElement.gameObject.SetActive(false);
                var name = pin.m_NamePinData?.PinNameGameObject;
                if (name != null && name.activeSelf) name.SetActive(false);
            }
        }

        public static bool IsPortalPin(Minimap.PinData pin) => pin != null && Pins.ContainsValue(pin);
    }

    /// <summary>Fast access to Minimap's private pin list.</summary>
    internal class AccessTools_FieldRef
    {
        private readonly HarmonyLib.AccessTools.FieldRef<Minimap, List<Minimap.PinData>> _ref =
            HarmonyLib.AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        public List<Minimap.PinData> Get(Minimap m) => _ref(m);
    }
}

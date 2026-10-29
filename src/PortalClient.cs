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
        public const int LastBuiltCount = 3;

        private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new Dictionary<ZDOID, Minimap.PinData>();
        private static bool _pinsDirty;
        private static Sprite _pinSprite;
        private static float _nextForeignScan;
        private const float ForeignPinRadius = 4f;
        private static readonly AccessTools_FieldRef PinsField = new AccessTools_FieldRef();

        public static void Register()
        {
            ZRoutedRpc.instance.Register(RpcList, new Action<long, ZPackage>(OnList));
            ZRoutedRpc.instance.Register(RpcFavs, new Action<long, ZPackage>(OnFavs));
            ZRoutedRpc.instance.Register(RpcShared, new Action<long, ZPackage>(OnShared));
            ZRoutedRpc.instance.Register(RpcSettings, new Action<long, bool>((sender, restricted) => _serverRestrictions = restricted));
            _serverRestrictions = null;
            Portals = new List<PortalInfo>();
            Favorites = new HashSet<ZDOID>();
            SharedFavorites = new HashSet<ZDOID>();
            Pins.Clear();
            Active = false;
        }

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
            _pinsDirty = true;
        }

        // package: personal ids, then shared ids
        private static void OnFavs(long sender, ZPackage pkg)
        {
            Favorites = ReadIds(pkg);
            SharedFavorites = ReadIds(pkg);
        }

        private static void OnShared(long sender, ZPackage pkg) => SharedFavorites = ReadIds(pkg);

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

            public void SetText(string text)
            {
                text = (text ?? "").Trim();
                if (text.Length > MaxNameLength) text = text.Substring(0, MaxNameLength);
                if (text == (_portal.Name ?? "") || ZRoutedRpc.instance == null) return;
                string author = "";
                try { author = Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.ToString(); }
                catch { }
                ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcRename, _portal.Id, text, author);
                // show it right away; the server confirms with the next list update
                foreach (var p in Portals) if (p.Id == _portal.Id) p.Name = text;
                _pinsDirty = true;
            }
        }

        /// <summary>Click cycle: none -> mine -> shared with everyone -> none.</summary>
        public static void ToggleFavorite(PortalInfo p)
        {
            if (ZRoutedRpc.instance == null || LocalPlayerId == 0) return;
            // Optimistic local update; the server answers with the stored lists.
            switch (GetFav(p))
            {
                case FavState.None: Favorites.Add(p.Id); break;
                case FavState.Personal: Favorites.Remove(p.Id); SharedFavorites.Add(p.Id); break;
                default: SharedFavorites.Remove(p.Id); Favorites.Remove(p.Id); break;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcToggleFav, LocalPlayerId, p.Id);
        }

        // ---------- choose-destination mode ----------

        public static void Activate(TeleportWorld portal)
        {
            var nview = portal.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            if (Minimap.instance == null) return;

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
        }

        public static void Deactivate(bool closeMap)
        {
            if (!Active) return;
            Active = false;
            SourceId = ZDOID.None;
            _sourceTransform = null;
            if (closeMap && Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);
        }

        public static void Tick()
        {
            if (_pinsDirty) RefreshPins();
            UpdateRename();
            if (Time.time >= _nextForeignScan)
            {
                _nextForeignScan = Time.time + 0.5f;
                ReskinForeignPins();
            }
            if (!Active) return;

            var player = Player.m_localPlayer;
            if (player == null || Minimap.instance == null) { Deactivate(false); return; }

            // The player closed the map (Esc / M) -> leave the mode.
            if (Time.time - _activatedAt > 0.3f && Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                Deactivate(false);
                return;
            }

            // The portal was destroyed, or the player walked away from it.
            if (_sourceTransform == null ||
                Vector3.Distance(player.transform.position, _sourceTransform.position) > LeaveDistance)
            {
                Deactivate(true);
            }
        }

        /// <summary>The portal this character last travelled from (if it still exists and is not the current one).</summary>
        public static PortalInfo BackPortal
        {
            get
            {
                var pos = Prefs.GetBack();
                if (pos == null) return null;
                PortalInfo best = null;
                float bestD = 1.5f;
                foreach (var p in Portals)
                {
                    float d = Utils.DistanceXZ(p.Position, pos.Value);
                    if (d < bestD && Mathf.Abs(p.Position.y - pos.Value.y) < 3f) { bestD = d; best = p; }
                }
                return best != null && best.Id != SourceId ? best : null;
            }
        }

        public static IEnumerable<PortalInfo> Destinations =>
            Portals.Where(p => p.Id != SourceId);

        public static List<PortalInfo> LastBuilt =>
            Portals.Where(p => p.BuiltAt > 0).OrderByDescending(p => p.BuiltAt)
                   .Take(LastBuiltCount).ToList();

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
            Prefs.SetBack(SourcePosition); // the "Back to" row will offer the portal we leave from
            Deactivate(true);
            player.TeleportTo(exit, target.Rotation, true);
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
        public static bool TryTeleportAtMapPoint(Vector3 worldPoint, float radius)
        {
            PortalInfo best = null;
            float bestDist = radius;
            foreach (var p in Destinations)
            {
                float d = Utils.DistanceXZ(p.Position, worldPoint);
                if (d <= bestDist) { bestDist = d; best = p; }
            }
            if (best == null) return false;
            TeleportTo(best);
            return true;
        }

        // ---------- map pins ----------

        private static void RefreshPins()
        {
            if (Minimap.instance == null) return;
            _pinsDirty = false;

            foreach (var pin in Pins.Values) Minimap.instance.RemovePin(pin);
            Pins.Clear();

            if (_pinSprite == null) _pinSprite = Art.PinSprite();
            foreach (var p in Portals)
            {
                var pin = Minimap.instance.AddPin(p.Position, Minimap.PinType.Icon4, p.DisplayName,
                    false, false, 0L, default(Splatform.PlatformUserID));
                pin.m_icon = _pinSprite;
                Pins[p.Id] = pin;
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
                if (pin == null || ours.Contains(pin)) continue;
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

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

        /// <summary>This player's order of favorites (first = top). Favorites missing here go to the end.</summary>
        public static List<ZDOID> FavOrder = new List<ZDOID>();

        // package: personal ids, shared ids, personal order
        private static void OnFavs(long sender, ZPackage pkg)
        {
            Favorites = ReadIds(pkg);
            SharedFavorites = ReadIds(pkg);
            FavOrder = new List<ZDOID>(ReadIdList(pkg));
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
            ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcSetCategory, p.Id, category);
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
                case FavState.None: Favorites.Add(p.Id); if (!FavOrder.Contains(p.Id)) FavOrder.Add(p.Id); break;
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
            if (_pinsDirty) RefreshPins();
            UpdateRename();
            if (Time.time >= _nextForeignScan)
            {
                _nextForeignScan = Time.time + 0.5f;
                ReskinForeignPins();
            }
            if (!Active) { _hoverPortal = null; _hoverSince = 0f; StopGlow(); _saved = false; FlowLine.Hide(); return; }

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
                Vector3.Distance(player.transform.position, _sourceTransform.position) > LeaveDistance)
            {
                Deactivate(true);
            }
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
        public static void ScalePins()
        {
            var map = Minimap.instance;
            if (map == null) return;
            bool large = map.m_mode == Minimap.MapMode.Large;
            Vector2 mouse = ZInput.pointerPosition;
            // the map's canvas may use a camera; hit tests need it
            if (_mapCanvas == null && map.m_pinRootLarge != null) _mapCanvas = map.m_pinRootLarge.GetComponentInParent<Canvas>();
            Camera cam = _mapCanvas != null && _mapCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _mapCanvas.worldCamera : null;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f);
            foreach (var kv in Pins)
            {
                var pin = kv.Value;
                if (pin == null || pin.m_uiElement == null) continue;
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
                if (cur > 1.01f) pin.m_uiElement.SetAsLastSibling(); // big pins on top of their neighbours
                var name = pin.m_NamePinData?.PinNameGameObject;
                if (name != null) name.transform.localScale = new Vector3(cur, cur, 1f);
            }
        }
        private static bool _saved, _returning;
        private static Vector3 _savedOffset, _targetOffset;
        private static float _savedZoom, _targetZoom;
        private static bool _moving;

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
                    _targetOffset = hovered.Position - player.transform.position;
                    float worldSize = Mathf.Max(1f, map.m_textureSize * map.m_pixelSize);
                    float regionZoom = Mathf.Clamp(Plugin.HoverZoomMeters.Value / worldSize, map.m_minZoom, map.m_maxZoom);
                    _targetZoom = Mathf.Min(map.LargeZoom, regionZoom); // only zoom in, never out
                    _moving = true; _returning = false;
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
                        if (Plugin.HoverReturn.Value) { _targetOffset = _savedOffset; _targetZoom = _savedZoom; _moving = true; _returning = true; }
                        else { _saved = false; _moving = false; }
                    }
                }
            }

            if (_moving)
            {
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f); // ~0.3 s glide
                Vector3 off = Vector3.Lerp(MapOffset(map), _targetOffset, k);
                float zoom = Mathf.Lerp(map.LargeZoom, _targetZoom, k);
                if ((off - _targetOffset).sqrMagnitude < 0.25f && Mathf.Abs(zoom - _targetZoom) < 0.0005f)
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
            if (target != null && target.Id != SourceId && Plugin.HoverLine.Value) FlowLine.Show(map, SourcePosition, target.Position);
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

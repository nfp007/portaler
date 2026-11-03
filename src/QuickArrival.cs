using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Shorter waits for Portaler trips (config [Travel] QuickArrival, on by default).
    ///
    /// The game's teleport (Player.UpdateTeleport) always waits 2 seconds before moving you and at least
    /// 8 seconds in total, even when the target area is long loaded. For trips started by Portaler:
    ///  - you are moved as soon as the screen is fully dark (instead of after 2 s);
    ///  - you arrive as soon as the game itself says the area is ready and there is ground under you
    ///    (the same checks the game makes at 8 s, only without the fixed minimum).
    /// Everything else stays vanilla: while the area is not ready nothing changes, and the game's own
    /// 15 s fallback still applies. Vanilla portals, dungeons and other mods' teleports are not touched.
    /// Switched off automatically when Fast Teleport is installed (it does the same for every teleport).
    /// </summary>
    public static class QuickArrival
    {
        private static readonly AccessTools.FieldRef<Player, bool> Teleporting = AccessTools.FieldRefAccess<Player, bool>("m_teleporting");
        private static readonly AccessTools.FieldRef<Player, float> Timer = AccessTools.FieldRefAccess<Player, float>("m_teleportTimer");
        private static readonly AccessTools.FieldRef<Player, Vector3> Target = AccessTools.FieldRefAccess<Player, Vector3>("m_teleportTargetPos");
        private static readonly AccessTools.FieldRef<Player, bool> Distant = AccessTools.FieldRefAccess<Player, bool>("m_distantTeleport");
        private static readonly AccessTools.FieldRef<Hud, CanvasGroup> LoadingScreen = AccessTools.FieldRefAccess<Hud, CanvasGroup>("m_loadingScreen");

        private const float MoveAt = 2f;      // vanilla: player is moved once the timer passes this
        private const float ArriveAt = 8f;    // vanilla: earliest arrival for distant teleports
        private const float MaxFade = 1.5f;   // never wait longer than this for the dark screen

        private static Player _player;
        private static bool _otherMod, _checked;

        /// <summary>The option is on and no other teleport-speed mod is installed.</summary>
        public static bool Enabled => Plugin.QuickArrival != null && Plugin.QuickArrival.Value && !OtherModInstalled;

        private static bool OtherModInstalled
        {
            get
            {
                if (_checked) return _otherMod;
                _checked = true;
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    string id = kv.Key ?? "", name = kv.Value?.Metadata?.Name ?? "";
                    if (id.IndexOf("FastTeleport", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Fast Teleport", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("FastTeleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _otherMod = true;
                        Plugin.Log.LogInfo($"{name} is installed: Portaler's QuickArrival stays off.");
                    }
                }
                return _otherMod;
            }
        }

        /// <summary>Portaler is about to teleport this player.</summary>
        public static void Begin(Player player) => _player = Enabled ? player : null;

        [HarmonyPatch(typeof(Player), "UpdateTeleport")]
        private static class Player_UpdateTeleport
        {
            private static void Prefix(Player __instance, float dt)
            {
                if (_player == null || __instance != _player) return;
                if (!Teleporting(__instance)) { _player = null; return; } // trip over (or never started)
                if (!Distant(__instance)) return;
                try
                {
                    ref float timer = ref Timer(__instance);
                    if (timer + dt <= MoveAt)
                    {
                        // move as soon as the loading screen hides the jump
                        var hud = Hud.instance;
                        var screen = hud != null ? LoadingScreen(hud) : null;
                        bool dark = screen == null || screen.alpha >= 0.99f;
                        if (dark || timer + dt >= MaxFade) timer = MoveAt;
                    }
                    else if (timer > MoveAt && timer + dt <= ArriveAt)
                    {
                        // the area is ready: let the game's own arrival check pass now instead of at 8 s
                        Vector3 pos = Target(__instance);
                        if (ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(pos) &&
                            ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(pos, out _))
                            timer = ArriveAt;
                    }
                }
                catch (System.Exception e)
                {
                    _player = null; // never get in the way of the game's teleport
                    Plugin.Log.LogWarning("QuickArrival switched off for this trip: " + e.Message);
                }
            }
        }
    }
}

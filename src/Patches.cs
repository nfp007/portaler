using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>All Harmony patches in one place.</summary>
    internal static class Patches
    {
        /// <summary>Register network messages once the game (and its RPC system) exists.</summary>
        [HarmonyPatch(typeof(Game), "Start")]
        private static class Game_Start
        {
            private static void Postfix()
            {
                PortalServer.Register();
                PortalClient.Register();
            }
        }

        /// <summary>Ask the server for the list as soon as the local player spawns.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) PortalClient.RequestList();
            }
        }

        /// <summary>Stepping into a portal opens the destination chooser instead of the vanilla teleport.</summary>
        [HarmonyPatch(typeof(TeleportWorldTrigger), "OnTriggerEnter")]
        private static class TeleportWorldTrigger_OnTriggerEnter
        {
            private static bool Prefix(TeleportWorldTrigger __instance, Collider colliderIn)
            {
                var player = colliderIn.GetComponent<Player>();
                if (player == null || player != Player.m_localPlayer) return false;
                var portal = __instance.GetComponentInParent<TeleportWorld>();
                if (portal != null) PortalClient.Activate(portal);
                return false;
            }
        }

        /// <summary>Every portal is "connected" (glows), since any portal can go anywhere.</summary>
        [HarmonyPatch(typeof(TeleportWorld), "HaveTarget")]
        private static class TeleportWorld_HaveTarget
        {
            private static void Postfix(ref bool __result) => __result = true;
        }

        [HarmonyPatch(typeof(TeleportWorld), "TargetFound")]
        private static class TeleportWorld_TargetFound
        {
            private static void Postfix(ref bool __result) => __result = true;
        }

        /// <summary>Turn off vanilla pairing of portals with the same name.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.ConnectPortals))]
        private static class Game_ConnectPortals
        {
            private static bool Prefix() => false;
        }

        [HarmonyPatch(typeof(ZDOMan), "ConnectPortals")]
        private static class ZDOMan_ConnectPortals
        {
            private static bool Prefix() => false;
        }

        /// <summary>A freshly built portal: ask the server for an automatic name.</summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class Piece_SetCreator
        {
            private static void Postfix(Piece __instance)
            {
                if (__instance.GetComponent<TeleportWorld>() == null) return;
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || ZRoutedRpc.instance == null) return;
                ZRoutedRpc.instance.InvokeRoutedRPC(PortalServer.RpcNewPortal,
                    nview.GetZDO().m_uid, __instance.transform.position);
            }
        }

        /// <summary>While choosing a destination, a map click on a portal pin teleports there.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
        private static class Minimap_OnMapLeftClick
        {
            private static readonly System.Func<Minimap, Vector3, Vector3> ScreenToWorld =
                AccessTools.MethodDelegate<System.Func<Minimap, Vector3, Vector3>>(
                    AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint"));

            private static readonly System.Func<Minimap, float> PinRadius =
                AccessTools.MethodDelegate<System.Func<Minimap, float>>(
                    AccessTools.PropertyGetter(typeof(Minimap), "PinInteractRadius"));

            private static bool Prefix(Minimap __instance)
            {
                if (!PortalClient.Active) return true;
                if (PortalUI.MouseOverList) return false; // click was on our list
                Vector3 world = ScreenToWorld(__instance, ZInput.pointerPosition);
                PortalClient.TryTeleportAtMapPoint(world, PinRadius(__instance) * 1.5f);
                return false; // no pin checking / other map clicks while choosing
            }
        }
    
        /// <summary>While choosing, other map clicks (double click = new pin, middle click = ping) do nothing.</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
        private static class Minimap_OnMapDblClick
        {
            private static bool Prefix() => !PortalClient.Active;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapMiddleClick))]
        private static class Minimap_OnMapMiddleClick
        {
            private static bool Prefix() => !PortalClient.Active;
        }

        /// <summary>
        /// While the mouse is on the list (or dragging its scrollbar), the map takes no mouse input at all:
        /// no zooming with the wheel and no dragging of the map underneath.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), "UpdateMap")]
        private static class Minimap_UpdateMap
        {
            private static void Prefix(ref bool takeInput)
            {
                if (PortalClient.Active && PortalUI.ListHasMouse) takeInput = false;
            }
        }

        /// <summary>Pressing the mouse on the list must not start dragging the map.</summary>
        [HarmonyPatch(typeof(Minimap), "OnMapLeftDown")]
        private static class Minimap_OnMapLeftDown
        {
            private static bool Prefix() => !(PortalClient.Active && PortalUI.ListHasMouse);
        }
    
        /// <summary>Hide other mods' pins that sit on a portal (we already show ours there).</summary>
        [HarmonyPatch(typeof(Minimap), "UpdatePins")]
        private static class Minimap_UpdatePins
        {
            private static void Postfix() => PortalClient.HideForeignPins();
        }
    
        /// <summary>While typing in the search box the game must not react to keys (walking, map, chat...).</summary>
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
        private static class Minimap_InTextInput
        {
            private static void Postfix(ref bool __result)
            {
                if (PortalClient.Active && PortalUI.BlocksKeys) __result = true;
            }
        }
    }
}

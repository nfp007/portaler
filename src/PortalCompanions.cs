using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Tames, a ridden mount and a pulled cart travel along on Portaler trips (list, map pin, linked portal).
    ///
    /// How: when the trip starts, Portaler takes control of them (ownership) and puts them at the destination,
    /// in front of the target portal. That area is usually not loaded yet; the game keeps them there and shows them
    /// when you arrive. After arrival they are put on the ground next to you, the cart is hitched again and you
    /// sit in the saddle again. Personal settings [Travel] BringTames / BringMounts / BringCarts.
    /// Switched off when TeleportEverything (or a similar mod) is installed, so animals never get moved twice.
    /// </summary>
    internal static class PortalCompanions
    {
        private const float TameRadius = 12f;  // tames following you within this distance come along
        private const int MaxTames = 8;

        private static readonly AccessTools.FieldRef<Character, ZNetView> CharView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Character, Rigidbody> CharBody = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, float> CharMaxAir = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Vagon, ZNetView> CartView = AccessTools.FieldRefAccess<Vagon, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Vagon, ConfigurableJoint> CartJoint = AccessTools.FieldRefAccess<Vagon, ConfigurableJoint>("m_attachJoin");
        private static readonly AccessTools.FieldRef<Vagon, GameObject> CartAttached = AccessTools.FieldRefAccess<Vagon, GameObject>("m_attachedObject");
        private static readonly AccessTools.FieldRef<Vagon, Rigidbody[]> CartBodies = AccessTools.FieldRefAccess<Vagon, Rigidbody[]>("m_bodies");
        private static readonly System.Reflection.FieldInfo CartInstancesField = AccessTools.Field(typeof(Vagon), "m_instances");
        private static List<Vagon> CartInstances() => CartInstancesField?.GetValue(null) as List<Vagon>;
        private static readonly System.Action<Vagon> CartDetach = AccessTools.MethodDelegate<System.Action<Vagon>>(AccessTools.Method(typeof(Vagon), "Detach"));
        private static readonly System.Action<Vagon, GameObject> CartAttachTo = AccessTools.MethodDelegate<System.Action<Vagon, GameObject>>(AccessTools.Method(typeof(Vagon), "AttachTo"));
        private static readonly System.Func<Vagon, GameObject, bool> CartCanAttach = AccessTools.MethodDelegate<System.Func<Vagon, GameObject, bool>>(AccessTools.Method(typeof(Vagon), "CanAttach"));

        /// <summary>What goes along on this trip (collected before the player is moved).</summary>
        internal sealed class Trip
        {
            public Character Mount;
            public Vagon Cart;
            public readonly List<Character> Tames = new List<Character>();
            public bool Any => Mount != null || Cart != null || Tames.Count > 0;
        }

        private static bool? _otherMod;

        /// <summary>Another mod already takes animals and carts along: leave it to that one.</summary>
        private static bool OtherModInstalled
        {
            get
            {
                if (_otherMod.HasValue) return _otherMod.Value;
                _otherMod = false;
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    string id = kv.Key ?? "", name = kv.Value?.Metadata?.Name ?? "";
                    foreach (var part in new[] { "TeleportEverything", "TeleportCreatures", "TeleportCarts", "AdjustablePortals" })
                        if (id.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _otherMod = true;
                            Plugin.Log.LogInfo($"{name} is installed: Portaler leaves tames, mounts and carts to it.");
                        }
                }
                return _otherMod.Value;
            }
        }

        /// <summary>Before the trip: who comes along. Null when nothing does.</summary>
        public static Trip Collect(Player player, bool allowAllItems, bool restricted)
        {
            if (player == null || OtherModInstalled) return null;
            var trip = new Trip();
            try
            {
                if (Plugin.BringMounts.Value) trip.Mount = RiddenMount(player);
                if (Plugin.BringCarts.Value)
                {
                    var cart = PulledCart(player);
                    if (cart != null)
                    {
                        var inv = cart.m_container != null ? cart.m_container.GetInventory() : null;
                        if (restricted && inv != null && !inv.IsTeleportable(allowAllItems))
                            player.Message(MessageHud.MessageType.Center, "The cart stays here: its load cannot go through portals", 0, null);
                        else trip.Cart = cart;
                    }
                }
                if (Plugin.BringTames.Value) CollectTames(player, trip);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Companions: " + e.Message); }
            return trip.Any ? trip : null;
        }

        /// <summary>The trip has started: move everybody to the destination and settle them after arrival.</summary>
        public static void Send(Player player, Trip trip, Vector3 exit, Quaternion rot)
        {
            if (player == null || trip == null) return;
            try
            {
                var ids = new List<ZDOID>();
                ZDOID mountId = ZDOID.None, cartId = ZDOID.None;
                int slot = 0;
                if (trip.Mount != null)
                {
                    var nv = CharView(trip.Mount);
                    if (nv != null && nv.IsValid())
                    {
                        mountId = nv.GetZDO().m_uid;
                        MoveCharacter(trip.Mount, Spot(exit, rot, slot++));
                    }
                }
                foreach (var t in trip.Tames)
                {
                    var nv = CharView(t);
                    if (nv == null || !nv.IsValid()) continue;
                    ids.Add(nv.GetZDO().m_uid);
                    MoveCharacter(t, Spot(exit, rot, slot++));
                }
                if (trip.Cart != null)
                {
                    var nv = CartView(trip.Cart);
                    if (nv != null && nv.IsValid())
                    {
                        cartId = nv.GetZDO().m_uid;
                        CartDetach(trip.Cart);
                        MoveCart(trip.Cart, exit - rot * Vector3.forward * 2.5f); // put right next to you again on arrival
                    }
                }
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Settle(player, exit, rot, ids, mountId, cartId));
                int n = ids.Count + (mountId.IsNone() ? 0 : 1);
                Plugin.Log.LogInfo($"Taking along: {n} animal(s){(cartId.IsNone() ? "" : " and a cart")}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Companions: " + e.Message); }
        }

        // ---------- who comes along ----------

        private static Character RiddenMount(Player player)
        {
            if (!player.IsRiding()) return null;
            var saddle = player.GetDoodadController() as Sadle;
            var c = saddle != null ? saddle.GetCharacter() : null;
            return c != null && !c.IsDead() ? c : null;
        }

        private static Vagon PulledCart(Player player)
        {
            var all = CartInstances();
            if (all == null) return null;
            var go = player.gameObject;
            foreach (var v in all)
            {
                if (v == null) continue;
                if (CartAttached(v) == go) return v;
                var j = CartJoint(v);
                if (j != null && j.connectedBody != null && j.connectedBody.gameObject == go) return v;
            }
            return null;
        }

        private static void CollectTames(Player player, Trip trip)
        {
            Vector3 me = player.transform.position;
            string myName = player.GetPlayerName();
            var found = new List<Character>();
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == trip.Mount || c.IsPlayer() || c.IsDead() || !c.IsTamed()) continue;
                if (Utils.DistanceXZ(c.transform.position, me) > TameRadius) continue;
                var nv = CharView(c);
                if (nv == null || !nv.IsValid()) continue;
                // only animals that follow you (told "follow" by pressing E on them), never penned livestock
                var ai = c.GetComponent<MonsterAI>();
                bool follows = (ai != null && ai.GetFollowTarget() == player.gameObject) ||
                               nv.GetZDO().GetString(ZDOVars.s_follow, "") == myName;
                if (!follows) continue;
                var saddle = c.GetComponentInChildren<Sadle>();
                if (saddle != null && saddle.HaveValidUser()) continue; // somebody else rides it
                found.Add(c);
            }
            found.Sort((a, b) => Utils.DistanceXZ(a.transform.position, me).CompareTo(Utils.DistanceXZ(b.transform.position, me)));
            if (found.Count > MaxTames) found.RemoveRange(MaxTames, found.Count - MaxTames);
            trip.Tames.AddRange(found);
        }

        // ---------- moving ----------

        /// <summary>Places in front of the target portal: rows of three, 2.5 m and more away from the frame.</summary>
        private static Vector3 Spot(Vector3 exit, Quaternion rot, int i)
        {
            float side = (i % 3 - 1) * 2f;
            float ahead = 2.5f + (i / 3) * 2f;
            return exit + rot * new Vector3(side, 0f, ahead);
        }

        private static void MoveCharacter(Character c, Vector3 pos)
        {
            var nv = CharView(c);
            nv.ClaimOwnership();
            c.transform.position = pos;
            nv.GetZDO().SetPosition(pos);
            var body = CharBody(c);
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            CharMaxAir(c) = pos.y; // no fall damage from the jump
        }

        private static void MoveCart(Vagon cart, Vector3 pos)
        {
            var nv = CartView(cart);
            nv.ClaimOwnership();
            cart.transform.position = pos;
            nv.GetZDO().SetPosition(pos);
            var bodies = CartBodies(cart);
            if (bodies == null) return;
            foreach (var b in bodies)
            {
                if (b == null) continue;
                b.position = b.transform.position;
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
            }
        }

        // ---------- after arrival ----------

        private static IEnumerator Settle(Player player, Vector3 exit, Quaternion rot, List<ZDOID> tames, ZDOID mount, ZDOID cart)
        {
            float deadline = Time.time + 30f;
            while (player != null && player.IsTeleporting() && Time.time < deadline) yield return null;
            if (player == null) yield break;
            yield return null; // one frame for the area to show its objects

            float wait = Time.time + 10f;
            int slot = 0;
            if (!mount.IsNone())
            {
                var go = Find(mount);
                while (go == null && Time.time < wait) { yield return null; go = Find(mount); }
                if (go != null)
                {
                    var c = go.GetComponent<Character>();
                    if (c != null) Ground(c, Spot(exit, rot, slot));
                    var saddle = go.GetComponentInChildren<Sadle>();
                    yield return null;
                    if (saddle != null && player != null && !player.IsDead() && !player.IsAttached() && player.GetDoodadController() == null)
                        saddle.Interact(player, false, false); // back in the saddle
                }
                slot++;
            }
            foreach (var id in tames)
            {
                var go = Find(id);
                while (go == null && Time.time < wait) { yield return null; go = Find(id); }
                var c = go != null ? go.GetComponent<Character>() : null;
                if (c != null) Ground(c, Spot(exit, rot, slot));
                slot++;
            }
            if (!cart.IsNone())
            {
                var go = Find(cart);
                while (go == null && Time.time < wait) { yield return null; go = Find(cart); }
                var v = go != null ? go.GetComponent<Vagon>() : null;
                if (v != null && player != null)
                {
                    // behind the player at the hitch, then hitch it again
                    Vector3 hitch = v.m_attachPoint != null ? v.m_attachPoint.position - v.transform.position : Vector3.zero;
                    Vector3 pos = player.transform.position + player.transform.rotation * v.m_attachOffset - hitch;
                    pos.y = Floor(pos) + 0.1f;
                    MoveCart(v, pos);
                    yield return null;
                    if (player != null && !player.IsTeleporting() && CartCanAttach(v, player.gameObject)) CartAttachTo(v, player.gameObject);
                }
            }
        }

        private static GameObject Find(ZDOID id) => ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;

        /// <summary>On solid ground near the planned spot (never inside rock or under the floor).</summary>
        private static void Ground(Character c, Vector3 spot)
        {
            Vector3 pos = c.transform.position;
            if (Utils.DistanceXZ(pos, spot) > 6f) pos = spot; // wandered or landed far off: back to its place
            pos.y = Floor(pos) + 0.2f;
            MoveCharacter(c, pos);
        }

        private static float Floor(Vector3 pos)
        {
            var zs = ZoneSystem.instance;
            if (zs == null) return pos.y;
            if (zs.FindFloor(pos + Vector3.up * 2f, out float h)) return h;
            return zs.GetSolidHeight(pos);
        }
    }
}

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
    /// Switched off when TeleportEverything is installed, so animals never get moved twice.
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

        /// <summary>Cart shape and where it came from, measured while it still stands at the start.</summary>
        private sealed class CartPlan
        {
            public Bounds Local;        // colliders in the cart's own space
            public Vector3 HitchLocal;  // hitch point in the cart's own space
            public Vector3 AttachOffset; // hitch point behind the player, in the player's space (Vagon.m_attachOffset)
            public float Push;          // how far the player steps forward so the cart is clear of the portal
            public Vector3 SrcPos;
            public Quaternion SrcRot;
        }

        private const float CartMargin = 0.8f;      // free space between portal exit and the back of the cart
        private const float MaxCorner = 1.2f;       // floor under each corner within this of the floor under the cart
        private const float MaxFloorJump = 10f;     // a floor further than this from the exit height is not ours
        private static readonly Vector2[] CartTries = // (sideways, further ahead) in metres, tried in this order
        {
            new Vector2(0f, 0f), new Vector2(0f, 1.5f), new Vector2(0f, 3f),
            new Vector2(-2.5f, 0f), new Vector2(2.5f, 0f), new Vector2(-2.5f, 1.5f), new Vector2(2.5f, 1.5f),
        };
        private static int _solidMask = -1;
        private static readonly Collider[] Hits = new Collider[32];

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
                    foreach (var part in new[] { "TeleportEverything", "TeleportCreatures", "TeleportCarts" })
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
                CartPlan plan = trip.Cart != null ? PlanCart(trip.Cart, player) : null;
                Vector3 front = exit + rot * Vector3.forward * (plan != null ? plan.Push : 0f); // where you will stand
                int slot = 0;
                if (trip.Mount != null)
                {
                    var nv = CharView(trip.Mount);
                    if (nv != null && nv.IsValid())
                    {
                        mountId = nv.GetZDO().m_uid;
                        MoveCharacter(trip.Mount, Spot(front, rot, slot++));
                    }
                }
                foreach (var t in trip.Tames)
                {
                    var nv = CharView(t);
                    if (nv == null || !nv.IsValid()) continue;
                    ids.Add(nv.GetZDO().m_uid);
                    MoveCharacter(t, Spot(front, rot, slot++));
                }
                if (trip.Cart != null && plan != null)
                {
                    var nv = CartView(trip.Cart);
                    if (nv != null && nv.IsValid())
                    {
                        cartId = nv.GetZDO().m_uid;
                        CartDetach(trip.Cart);
                        // straight behind the spot where you will stand; the height is fixed after arrival
                        CartPose(plan, front, rot, out var pos, out var crot);
                        pos.y = exit.y - 0.5f;
                        MoveCart(trip.Cart, pos, crot);
                        var zdo = nv.GetZDO();
                        if (zdo == null || Vector3.Distance(zdo.GetPosition(), pos) > 0.1f)
                            Plugin.Log.LogWarning($"Cart record not moved (is {zdo?.GetPosition()}, wanted {pos})");
                    }
                }
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Settle(player, exit, rot, ids, mountId, cartId, plan));
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

        private static void MoveCart(Vagon cart, Vector3 pos, Quaternion rot)
        {
            var nv = CartView(cart);
            nv.ClaimOwnership();
            cart.transform.SetPositionAndRotation(pos, rot);
            nv.GetZDO().SetPosition(pos);
            nv.GetZDO().SetRotation(rot);
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

        private static IEnumerator Settle(Player player, Vector3 exit, Quaternion rot, List<ZDOID> tames, ZDOID mount, ZDOID cart, CartPlan plan)
        {
            float deadline = Time.time + 30f;
            while (player != null && player.IsTeleporting() && Time.time < deadline) yield return null;
            if (player == null) yield break;
            yield return null; // one frame for the area to show its objects

            Vector3 front = exit + rot * Vector3.forward * (plan != null ? plan.Push : 0f);
            float wait = Time.time + 10f;
            int slot = 0;
            if (!mount.IsNone())
            {
                var go = Find(mount);
                while (go == null && Time.time < wait) { yield return null; go = Find(mount); }
                if (go != null)
                {
                    var c = go.GetComponent<Character>();
                    if (c != null) Ground(c, Spot(front, rot, slot));
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
                if (c != null) Ground(c, Spot(front, rot, slot));
                slot++;
            }
            if (!cart.IsNone() && plan != null)
                yield return SettleCart(player, exit, rot, cart, plan);
        }

        /// <summary>
        /// Cart after arrival: find a free, flat place straight behind you (you step forward so it is clear of the
        /// portal), try a few places further ahead or to the side, then hitch it again. No room anywhere: it goes back
        /// to the start portal.
        /// </summary>
        private static IEnumerator SettleCart(Player player, Vector3 exit, Quaternion rot, ZDOID cart, CartPlan plan)
        {
            float wait = Time.time + 30f; // a big base can take a while to show its objects
            var go = Find(cart);
            while (go == null && Time.time < wait) { yield return null; go = Find(cart); }
            var v = go != null ? go.GetComponent<Vagon>() : null;
            if (v == null)
            {
                var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(cart) : null;
                Plugin.Log.LogWarning(zdo != null ? $"Cart not shown after 30 s; it waits at {zdo.GetPosition()}" : "Cart is gone");
                yield break;
            }
            if (player == null || player.IsDead()) yield break;

            // you walked off (or ride): the cart goes behind you where you are, and you are not moved
            bool movePlayer = Utils.DistanceXZ(player.transform.position, exit) < 3f && !player.IsAttached();
            Vector3 anchor = movePlayer ? exit : player.transform.position;
            Quaternion arot = movePlayer ? rot : Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            float push = movePlayer ? plan.Push : 0f;
            float baseY = movePlayer ? exit.y - 1f : player.transform.position.y; // floor height at the portal / under you

            // first pass: flat and free; second pass: free is enough (slopes and steps are fine for a cart)
            bool placed = false, hitch = false;
            for (int pass = 0; pass < 2 && !placed; pass++)
                foreach (var t in CartTries)
                {
                    Vector3 stand = anchor + arot * new Vector3(t.x, 0f, push + t.y);
                    stand.y = Floor(stand, baseY);
                    CartPose(plan, stand, arot, out var pos, out var crot);
                    pos.y = Floor(pos, stand.y) + 0.1f;
                    if (!Fits(v, plan, pos, crot, pass == 0, out string why))
                    {
                        Plugin.Log.LogInfo($"Cart place {t} (pass {pass + 1}) not used: {why}");
                        continue;
                    }
                    if (movePlayer) MoveCharacter(player, stand + Vector3.up * 0.05f);
                    MoveCart(v, pos, crot);
                    placed = true;
                    hitch = movePlayer || t == Vector2.zero;
                    Plugin.Log.LogInfo($"Cart placed at {pos} (place {t}, pass {pass + 1}), you at {stand}");
                    break;
                }
            if (!placed)
            {
                MoveCart(v, plan.SrcPos, plan.SrcRot);
                player.Message(MessageHud.MessageType.Center, "Your cart stayed at the start portal: no room for it here", 0, null);
                yield break;
            }
            if (!hitch) yield break;

            // hitch again; the joint sometimes needs a few tries while the area settles
            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForSeconds(0.1f);
                if (player == null || v == null || player.IsDead()) yield break;
                if (v.IsAttached(player)) { Plugin.Log.LogInfo($"Cart hitched again (try {i + 1})"); yield break; }
                var nv = CartView(v);
                if (nv != null && nv.IsValid() && !nv.IsOwner()) nv.ClaimOwnership(); // the game only hitches carts it owns
                if (!player.IsTeleporting() && CartCanAttach(v, player.gameObject)) CartAttachTo(v, player.gameObject);
            }
            if (v != null && player != null && !v.IsAttached(player))
            {
                var nv = CartView(v);
                float dist = v.m_attachPoint != null ? Vector3.Distance(player.transform.position + v.m_attachOffset, v.m_attachPoint.position) : -1f;
                Plugin.Log.LogInfo($"Cart placed but not hitched again: hitch {dist:0.00} m away (max {v.m_detachDistance:0.00}), upright {v.transform.up.y:0.00}, " +
                                   $"owner {(nv != null && nv.IsValid() && nv.IsOwner())}, cart at {v.transform.position}, you at {player.transform.position}, offset {v.m_attachOffset}");
            }
        }

        // ---------- cart shape and room ----------

        private static CartPlan PlanCart(Vagon v, Player player)
        {
            try
            {
                var t = v.transform;
                var inv = Quaternion.Inverse(t.rotation);
                bool any = false;
                var b = new Bounds();
                foreach (var col in v.GetComponentsInChildren<Collider>())
                {
                    if (col == null || col.isTrigger || !col.enabled) continue;
                    var wb = col.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                        var lp = inv * (corner - t.position);
                        if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                    }
                }
                if (!any) b = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(2f, 1.6f, 3f));
                var plan = new CartPlan
                {
                    Local = b,
                    HitchLocal = v.m_attachPoint != null ? inv * (v.m_attachPoint.position - t.position) : new Vector3(0f, 0f, b.max.z),
                    AttachOffset = v.m_attachOffset,
                    SrcPos = t.position,
                    SrcRot = t.rotation,
                };
                // length behind the hitch, plus how far the hitch sits behind you
                float back = plan.HitchLocal.z >= b.center.z ? plan.HitchLocal.z - b.min.z : b.max.z - plan.HitchLocal.z;
                plan.Push = CartMargin + Mathf.Max(0f, -v.m_attachOffset.z) + back;
                return plan;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Cart: " + e.Message); return null; }
        }

        /// <summary>Cart straight behind someone standing at stand facing rot, hitch at their back.</summary>
        private static void CartPose(CartPlan plan, Vector3 stand, Quaternion rot, out Vector3 pos, out Quaternion crot)
        {
            float hitchYaw = new Vector2(plan.HitchLocal.x, plan.HitchLocal.z).sqrMagnitude > 0.01f
                ? Mathf.Atan2(plan.HitchLocal.x, plan.HitchLocal.z) * Mathf.Rad2Deg : 0f;
            crot = Quaternion.Euler(0f, rot.eulerAngles.y - hitchYaw, 0f); // hitch points at you
            Vector3 hitch = stand + plan.AttachOffset; // not turned: Vagon.CanAttach measures it this way
            pos = hitch - crot * plan.HitchLocal;
        }

        /// <summary>Flat enough under all four corners, and nothing solid (roof, wall, another cart, the portal) in the way.</summary>
        private static bool Fits(Vagon v, CartPlan plan, Vector3 pos, Quaternion crot, bool flat, out string why)
        {
            var b = plan.Local;
            float pivot = pos.y - 0.1f;
            for (int i = 0; i < 4 && flat; i++)
            {
                var c = pos + crot * new Vector3((i & 1) == 0 ? b.min.x : b.max.x, 0f, (i & 2) == 0 ? b.min.z : b.max.z);
                float h = Floor(c, pivot);
                if (Mathf.Abs(h - pivot) > MaxCorner) { why = $"uneven floor ({h - pivot:0.0} m at a corner)"; return false; }
            }
            if (_solidMask < 0) _solidMask = LayerMask.GetMask("Default", "static_solid", "piece", "vehicle");
            // the cart's box, lifted off the floor and a little slimmer, so ground and grass do not count
            var center = pos + crot * (b.center + Vector3.up * 0.25f);
            var half = new Vector3(Mathf.Max(0.1f, b.extents.x - 0.1f), Mathf.Max(0.1f, b.extents.y - 0.15f), Mathf.Max(0.1f, b.extents.z - 0.1f));
            int n = Physics.OverlapBoxNonAlloc(center, half, Hits, crot, _solidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h == null || h.transform.IsChildOf(v.transform)) continue;
                if (h.GetComponentInParent<Character>() != null) continue; // you and your animals step aside
                why = "blocked by " + h.transform.root.name;
                return false;
            }
            why = null;
            return true;
        }

        private static GameObject Find(ZDOID id) => ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;

        /// <summary>On solid ground near the planned spot (never inside rock or under the floor).</summary>
        private static void Ground(Character c, Vector3 spot)
        {
            Vector3 pos = c.transform.position;
            if (Utils.DistanceXZ(pos, spot) > 6f) pos = spot; // wandered or landed far off: back to its place
            pos.y = Floor(pos, spot.y - 1f) + 0.2f; // spots are 1 m above the portal floor, like the exit
            MoveCharacter(c, pos);
        }

        /// <summary>
        /// Floor under pos, searched from 1.6 m above near (the floor height we expect): a roof or upper storey above that
        /// is never taken for the floor. A floor far from near (deep hole) is ignored.
        /// </summary>
        private static float Floor(Vector3 pos, float near)
        {
            var zs = ZoneSystem.instance;
            if (zs == null) return near;
            var from = new Vector3(pos.x, near + 0.6f, pos.z); // FindFloor casts from 1 m above this
            if (zs.FindFloor(from, out float h) && Mathf.Abs(h - near) < MaxFloorJump) return h;
            float ground = zs.GetGroundHeight(from); // terrain only (GetSolidHeight would find roofs again)
            return Mathf.Abs(ground - near) < MaxFloorJump ? ground : near;
        }
    }
}

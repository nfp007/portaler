using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Portaler
{
    /// <summary>
    /// What portal colours and links look like:
    ///  - the fire around portals loaded near the player burns in their colour (runes and the swirl stay vanilla);
    ///  - a linked portal shows its destination's name floating above it;
    ///  - map pins of coloured portals get a ring in their colour.
    /// Colour 0 (vanilla) restores the game's own look.
    /// </summary>
    public static class PortalVisuals
    {
        // ---------- portals in the world ----------

        private static readonly List<TeleportWorld> Loaded = new List<TeleportWorld>();
        private static readonly Dictionary<TeleportWorld, Orig> Originals = new Dictionary<TeleportWorld, Orig>();
        private static float _nextApply;

        /// <summary>Original look of one particle system (restored for the vanilla colour).</summary>
        private sealed class PsOrig
        {
            public ParticleSystem Ps;
            public ParticleSystem.MinMaxGradient Start;
            public bool OverLifetime;
            public ParticleSystem.MinMaxGradient Life;
        }

        private sealed class Orig
        {
            public List<PsOrig> Fire = new List<PsOrig>();
            public List<KeyValuePair<Light, Color>> Lights = new List<KeyValuePair<Light, Color>>();
            public int Applied = -1;
        }

        private static readonly AccessTools.FieldRef<EffectFade, ParticleSystem[]> FadeParticles =
            AccessTools.FieldRefAccess<EffectFade, ParticleSystem[]>("m_particles");
        private static readonly AccessTools.FieldRef<EffectFade, Light> FadeLight =
            AccessTools.FieldRefAccess<EffectFade, Light>("m_light");

        public static void Register(TeleportWorld tw)
        {
            if (tw != null && !Loaded.Contains(tw)) Loaded.Add(tw);
        }

        /// <summary>Every 0.25 s: give loaded portals the colour the server says they have.</summary>
        public static void Tick()
        {
            if (Time.time < _nextApply) return;
            _nextApply = Time.time + 0.25f;
            for (int i = Loaded.Count - 1; i >= 0; i--)
            {
                var tw = Loaded[i];
                if (tw == null) { Loaded.RemoveAt(i); continue; }
                var nview = tw.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                var p = PortalClient.Get(nview.GetZDO().m_uid);
                Apply(tw, p != null ? p.Color : 0);
            }
            if (Originals.Count > Loaded.Count + 16)
            {
                var dead = new List<TeleportWorld>();
                foreach (var kv in Originals) if (kv.Key == null) dead.Add(kv.Key);
                foreach (var d in dead) Originals.Remove(d);
            }
        }

        /// <summary>
        /// Only the fire around the portal takes the colour. The runes and the swirl inside the ring
        /// (the game's "target found" effect) always keep their vanilla look.
        /// </summary>
        private static void Apply(TeleportWorld tw, int color)
        {
            if (!Originals.TryGetValue(tw, out var o))
            {
                if (color == 0) return; // never touched, nothing to restore
                o = Capture(tw);
                if (o == null) return;
                Originals[tw] = o;
            }
            if (o.Applied == color) return;
            try
            {
                if (color == 0)
                {
                    foreach (var f in o.Fire)
                    {
                        if (f.Ps == null) continue;
                        var m = f.Ps.main; m.startColor = f.Start;
                        var col = f.Ps.colorOverLifetime; col.color = f.Life;
                    }
                    foreach (var l in o.Lights) if (l.Key != null) l.Key.color = l.Value;
                }
                else
                {
                    Color c = PortalColors.Values[PortalColors.Clamp(color)];
                    foreach (var f in o.Fire)
                    {
                        if (f.Ps == null) continue;
                        var m = f.Ps.main; m.startColor = new ParticleSystem.MinMaxGradient(c);
                        // the fire's own colour change over its life would turn any colour red again: keep only its fading
                        if (f.OverLifetime) { var col = f.Ps.colorOverLifetime; col.color = WhiteKeepAlpha(f.Life); }
                    }
                    foreach (var l in o.Lights) if (l.Key != null) l.Key.color = c;
                }
                o.Applied = color;
            }
            catch (System.Exception e)
            {
                o.Applied = color; // do not retry every tick
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
            }
        }

        /// <summary>Same fading (alpha) as the original gradient, but white, so the start colour shows.</summary>
        private static ParticleSystem.MinMaxGradient WhiteKeepAlpha(ParticleSystem.MinMaxGradient g)
        {
            var src = g.mode == ParticleSystemGradientMode.TwoGradients || g.mode == ParticleSystemGradientMode.Gradient ? g.gradientMax ?? g.gradient : null;
            var grad = new Gradient();
            var alpha = src != null ? src.alphaKeys : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alpha);
            return new ParticleSystem.MinMaxGradient(grad);
        }

        private static readonly HashSet<string> Dumped = new HashSet<string>();

        private static Orig Capture(TeleportWorld tw)
        {
            try
            {
                var o = new Orig();
                // the swirl inside the ring and its light stay vanilla
                var inside = new HashSet<ParticleSystem>();
                Light insideLight = null;
                var fade = tw.m_target_found;
                if (fade != null)
                {
                    var parts = FadeParticles(fade);
                    if (parts != null) foreach (var ps in parts) if (ps != null) inside.Add(ps);
                    foreach (var ps in fade.GetComponentsInChildren<ParticleSystem>(true)) inside.Add(ps);
                    insideLight = FadeLight(fade);
                }
                foreach (var ps in tw.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (inside.Contains(ps)) continue;
                    var life = ps.colorOverLifetime;
                    o.Fire.Add(new PsOrig { Ps = ps, Start = ps.main.startColor, OverLifetime = life.enabled, Life = life.color });
                }
                foreach (var l in tw.GetComponentsInChildren<Light>(true))
                    if (l != insideLight && (fade == null || !l.transform.IsChildOf(fade.transform))) o.Lights.Add(new KeyValuePair<Light, Color>(l, l.color));
                if (Perf.On) Dump(tw, inside, o);
                return o;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
                return null;
            }
        }

        /// <summary>Developer log: which effects of this portal type get the colour (once per portal type).</summary>
        private static void Dump(TeleportWorld tw, HashSet<ParticleSystem> inside, Orig o)
        {
            string type = tw.name.Replace("(Clone)", "");
            if (!Dumped.Add(type)) return;
            var sb = new System.Text.StringBuilder($"[Perf] Portal effects of {type}:");
            foreach (var ps in tw.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var mat = r != null ? r.sharedMaterial : null;
                sb.Append($"\n  {(inside.Contains(ps) ? "inside" : "FIRE  ")} {Path(ps.transform, tw.transform)} start={ps.main.startColor.color} life={(ps.colorOverLifetime.enabled ? "on" : "off")}" +
                          $" mat={(mat != null ? mat.name + "/" + mat.shader.name : "-")}");
            }
            foreach (var l in tw.GetComponentsInChildren<Light>(true))
                sb.Append($"\n  light {Path(l.transform, tw.transform)} color={l.color} {(o.Lights.Exists(k => k.Key == l) ? "COLOURED" : "vanilla")}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Path(Transform t, Transform root)
        {
            string s = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        // ---------- floating destination names ----------

        private static GUIStyle _name;
        private static Texture2D _arrow;
        private static readonly GUIContent Content = new GUIContent();

        /// <summary>Above every linked portal near the player: an arrow and the destination's name.</summary>
        public static void DrawFloatingNames()
        {
            if (!Plugin.FloatingNames.Value || Event.current.type != EventType.Repaint) return;
            var player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || Minimap.IsOpen() || Hud.IsUserHidden() || Menu.IsVisible() || InventoryGui.IsVisible()) return;
            var cam = Utils.GetMainCamera();
            if (cam == null) return;
            float maxD = Mathf.Max(2f, Plugin.FloatingNameDistance.Value);
            Vector3 me = player.transform.position;

            foreach (var p in PortalClient.Portals)
            {
                if (!p.IsLinked) continue;
                float d = Vector3.Distance(me, p.Position);
                if (d > maxD) continue;
                var dest = PortalClient.LinkOf(p);
                if (dest == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(p.Position + Vector3.up * 4.3f);
                if (sp.z <= 0f) continue;
                EnsureStyle();

                float t = Mathf.Clamp01(d / maxD);
                int size = Mathf.RoundToInt(Mathf.Lerp(26f, 15f, t));
                float alpha = Mathf.Clamp01((maxD - d) / (maxD * 0.25f)); // fade out over the last quarter
                _name.fontSize = size;
                Content.text = dest.DisplayName;
                Vector2 ts = _name.CalcSize(Content);
                float arrowW = size * 0.9f, gap = size * 0.3f;
                float w = arrowW + gap + ts.x;
                float x = sp.x - w / 2f, y = Screen.height - sp.y - ts.y / 2f;

                var old = GUI.color;
                Color tint = PortalColors.Has(p.Color) ? PortalColors.Values[p.Color] : new Color(1f, 0.75f, 0.3f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
                GUI.DrawTexture(new Rect(x + 1.5f, y + (ts.y - arrowW) / 2f + 1.5f, arrowW, arrowW), _arrow);
                _name.normal.textColor = new Color(0f, 0f, 0f, 0.75f * alpha);
                _name.Draw(new Rect(x + arrowW + gap + 1.5f, y + 1.5f, ts.x, ts.y), Content, false, false, false, false);
                GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
                GUI.DrawTexture(new Rect(x, y + (ts.y - arrowW) / 2f, arrowW, arrowW), _arrow);
                GUI.color = old;
                _name.normal.textColor = new Color(1f, 0.96f, 0.88f, alpha);
                _name.Draw(new Rect(x + arrowW + gap, y, ts.x, ts.y), Content, false, false, false, false);
            }
        }

        private static void EnsureStyle()
        {
            if (_name != null)
            {
                if (_name.font == null && PortalUI.ValheimFont != null) _name.font = PortalUI.ValheimFont;
                return;
            }
            _name = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, richText = false };
            var f = PortalUI.ValheimFont;
            if (f != null) _name.font = f;
            _arrow = Art.Arrow(Color.white, 32);
        }

        // ---------- coloured ring around map pins ----------

        private static readonly Dictionary<Minimap.PinData, Image> Rings = new Dictionary<Minimap.PinData, Image>();
        private static Sprite _ringSprite;

        /// <summary>Called for each Portaler pin every frame: a ring in the portal's colour behind the pin (none for vanilla).</summary>
        public static void PinRing(Minimap.PinData pin, int color)
        {
            if (pin?.m_uiElement == null) return;
            Rings.TryGetValue(pin, out var ring);
            if (!PortalColors.Has(color))
            {
                if (ring != null && ring.enabled) ring.enabled = false;
                return;
            }
            if (ring == null)
            {
                if (_ringSprite == null) _ringSprite = Art.RingSprite();
                var go = new GameObject("PortalerRing", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(pin.m_uiElement, false);
                rt.SetAsFirstSibling(); // behind the pin's icon
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                ring = go.GetComponent<Image>();
                ring.sprite = _ringSprite;
                ring.raycastTarget = false;
                Rings[pin] = ring;
                if (Rings.Count > 512) Prune();
            }
            var size = pin.m_uiElement.rect.size;
            float s = Mathf.Max(size.x, size.y) * 1.32f;
            if (s > 0f && Mathf.Abs(ring.rectTransform.sizeDelta.x - s) > 0.5f) ring.rectTransform.sizeDelta = new Vector2(s, s);
            Color c = PortalColors.Values[color];
            if (ring.color != c) ring.color = c;
            if (!ring.enabled) ring.enabled = true;
        }

        private static void Prune()
        {
            var dead = new List<Minimap.PinData>();
            foreach (var kv in Rings) if (kv.Value == null) dead.Add(kv.Key);
            foreach (var d in dead) Rings.Remove(d);
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Portaler
{
    /// <summary>
    /// What portal colours and links look like:
    ///  - portals loaded near the player glow in their colour (runes, light, particles);
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

        private sealed class Orig
        {
            public Color Emission;
            public Color[] Lights;
            public ParticleSystem.MinMaxGradient[] Particles;
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
            var fade = tw.m_target_found;
            var parts = fade != null ? FadeParticles(fade) : null;
            var light = fade != null ? FadeLight(fade) : null;
            if (fade != null && (parts == null || o.Particles == null || parts.Length != o.Particles.Length))
            {
                // the effect was not set up yet when we looked: take its originals again
                var fresh = Capture(tw);
                if (fresh == null) return;
                o.Lights = fresh.Lights; o.Particles = fresh.Particles;
            }

            if (color == 0)
            {
                tw.m_colorTargetfound = o.Emission;
                if (light != null && o.Lights != null && o.Lights.Length > 0) light.color = o.Lights[0];
                if (parts != null && o.Particles != null)
                    for (int i = 0; i < parts.Length && i < o.Particles.Length; i++)
                        if (parts[i] != null) { var m = parts[i].main; m.startColor = o.Particles[i]; }
            }
            else
            {
                Color c = PortalColors.Values[PortalColors.Clamp(color)];
                // keep the brightness of the original glow (emission colours are stronger than 1)
                float strength = Mathf.Max(1f, Mathf.Max(o.Emission.r, Mathf.Max(o.Emission.g, o.Emission.b)));
                tw.m_colorTargetfound = new Color(c.r * strength, c.g * strength, c.b * strength, o.Emission.a);
                if (light != null) light.color = c;
                if (parts != null)
                    foreach (var ps in parts)
                        if (ps != null) { var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(c); }
            }
            o.Applied = color;
        }

        private static Orig Capture(TeleportWorld tw)
        {
            try
            {
                var o = new Orig { Emission = tw.m_colorTargetfound };
                var fade = tw.m_target_found;
                if (fade != null)
                {
                    var light = FadeLight(fade);
                    o.Lights = light != null ? new[] { light.color } : new Color[0];
                    var parts = FadeParticles(fade);
                    if (parts != null)
                    {
                        o.Particles = new ParticleSystem.MinMaxGradient[parts.Length];
                        for (int i = 0; i < parts.Length; i++) if (parts[i] != null) o.Particles[i] = parts[i].main.startColor;
                    }
                }
                return o;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
                return null;
            }
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

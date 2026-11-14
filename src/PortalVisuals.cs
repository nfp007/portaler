using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Portaler
{
    /// <summary>
    /// What portal colours and links look like:
    ///  - the fire around portals loaded near the player burns in their colour (runes, swirl, sparks and light stay vanilla);
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

        /// <summary>
        /// The portal's fire ("blue flames" in the game's "target found" effect) is drawn with a gradient-mapped
        /// shader: its colours come from a gradient texture in the material, not from the particle colour.
        /// A coloured portal gets its own copy of that material with a gradient in its colour.
        /// </summary>
        private sealed class Orig
        {
            public List<KeyValuePair<Renderer, Material>> Fire = new List<KeyValuePair<Renderer, Material>>();
            public string RampProperty;
            public int Applied = -1;
        }

        private static readonly AccessTools.FieldRef<EffectFade, ParticleSystem[]> FadeParticles =
            AccessTools.FieldRefAccess<EffectFade, ParticleSystem[]>("m_particles");
        private static readonly Dictionary<int, Texture2D> Ramps = new Dictionary<int, Texture2D>();

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

        /// <summary>Only the fire around the portal takes the colour; runes, swirl, sparks and light stay vanilla.</summary>
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
            o.Applied = color;
            if (o.RampProperty == null) return;
            try
            {
                foreach (var kv in o.Fire)
                {
                    var r = kv.Key;
                    if (r == null) continue;
                    if (color == 0)
                    {
                        if (r.sharedMaterial != kv.Value) { var own = r.sharedMaterial; r.sharedMaterial = kv.Value; if (own != null) Object.Destroy(own); }
                    }
                    else
                    {
                        var mat = r.sharedMaterial == kv.Value ? (r.sharedMaterial = new Material(kv.Value)) : r.sharedMaterial;
                        mat.SetTexture(o.RampProperty, Ramp(color));
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
            }
        }

        /// <summary>Gradient for the fire: dark, then the colour, brightening towards white at the hottest part.</summary>
        private static Texture2D Ramp(int color)
        {
            if (Ramps.TryGetValue(color, out var tex) && tex != null) return tex;
            Color c = PortalColors.Values[color];
            const int w = 256;
            tex = new Texture2D(w, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * 4];
            for (int x = 0; x < w; x++)
            {
                float t = x / (w - 1f);
                Color col;
                if (t < 0.3f) col = Color.Lerp(Color.black, c * 0.45f, t / 0.3f);
                else if (t < 0.65f) col = Color.Lerp(c * 0.45f, c, (t - 0.3f) / 0.35f);
                else col = Color.Lerp(c, Color.Lerp(c, Color.white, 0.75f), (t - 0.65f) / 0.35f);
                col.a = Mathf.Clamp01(t * 4f);
                for (int y = 0; y < 4; y++) px[y * w + x] = col;
            }
            tex.SetPixels(px);
            tex.Apply();
            Ramps[color] = tex;
            return tex;
        }

        private static readonly HashSet<string> Dumped = new HashSet<string>();

        private static Orig Capture(TeleportWorld tw)
        {
            try
            {
                var o = new Orig();
                foreach (var r in tw.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var mat = r.sharedMaterial;
                    if (mat == null || mat.shader == null || mat.shader.name.IndexOf("Gradient", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    o.Fire.Add(new KeyValuePair<Renderer, Material>(r, mat));
                    if (o.RampProperty == null) o.RampProperty = FindRamp(mat.shader);
                }
                if (Perf.On) Dump(tw, o);
                return o;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
                return null;
            }
        }

        /// <summary>The gradient texture of a gradient-mapped shader: a texture property named like "gradient" or "ramp".</summary>
        private static string FindRamp(Shader sh)
        {
            string other = null;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string n = sh.GetPropertyName(i);
                string l = n.ToLowerInvariant();
                if (l.Contains("grad") || l.Contains("ramp")) return n;
                if (n != "_MainTex" && other == null) other = n;
            }
            return other;
        }

        /// <summary>Developer log: the fire's material and shader properties (once per portal type).</summary>
        private static void Dump(TeleportWorld tw, Orig o)
        {
            string type = tw.name.Replace("(Clone)", "");
            if (!Dumped.Add(type)) return;
            var sb = new System.Text.StringBuilder($"[Perf] Portal fire of {type}: {o.Fire.Count} renderer(s), gradient property = {o.RampProperty ?? "NOT FOUND"}");
            foreach (var kv in o.Fire)
            {
                var sh = kv.Value.shader;
                sb.Append($"\n  {kv.Key.name}: {kv.Value.name} / {sh.name}");
                for (int i = 0; i < sh.GetPropertyCount(); i++) sb.Append($"\n    {sh.GetPropertyName(i)} ({sh.GetPropertyType(i)})");
            }
            Plugin.Log.LogInfo(sb.ToString());
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

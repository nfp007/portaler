using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Portaler
{
    /// <summary>
    /// What portal colours and links look like:
    ///  - the runes and the fire of portals loaded near the player take their colour (swirl, sparks and light stay vanilla);
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
        /// A coloured portal: the runes on the frame and the fire around the ring take the colour.
        /// The swirl inside the ring, its sparks and its light stay vanilla.
        ///
        /// The fire ("blue flames" in the game's "target found" effect) uses a gradient-mapped shader without a gradient
        /// texture: its colours come from the particle colours (start colour, colour over lifetime, custom data).
        /// Those get the portal's hue and saturation while keeping their own brightness, so the fire keeps its look.
        /// </summary>
        private sealed class FireOrig
        {
            public ParticleSystem Ps;
            public ParticleSystem.MinMaxGradient Start, Life, Speed, Custom1, Custom2;
            public bool HasCustom1, HasCustom2;
            public float Amount = 1f; // 1 = fully in the portal's colour, less = only a hint of it
            public bool Mix;          // blend towards the colour instead of changing the hue (for the dark swirl)
        }

        private sealed class Orig
        {
            public Color Emission;
            public List<FireOrig> Fire = new List<FireOrig>();
            public Light Light;
            public Color LightColor;
            public int Applied = -1;
        }

        private static readonly AccessTools.FieldRef<EffectFade, ParticleSystem[]> FadeParticles =
            AccessTools.FieldRefAccess<EffectFade, ParticleSystem[]>("m_particles");

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
            o.Applied = color;
            try
            {
                bool vanilla = !PortalColors.Has(color);
                Color c = vanilla ? Color.white : PortalColors.Get(color);

                // runes: the frame's glow (keep the strength of the original, emission colours go above 1)
                if (vanilla) tw.m_colorTargetfound = o.Emission;
                else
                {
                    float strength = Mathf.Max(1f, Mathf.Max(o.Emission.r, Mathf.Max(o.Emission.g, o.Emission.b)));
                    tw.m_colorTargetfound = new Color(c.r * strength, c.g * strength, c.b * strength, o.Emission.a);
                }

                // fire
                foreach (var f in o.Fire)
                {
                    if (f.Ps == null) continue;
                    float a = f.Amount;
                    var main = f.Ps.main; main.startColor = vanilla ? f.Start : Recolor(f.Start, c, a, f.Mix);
                    var life = f.Ps.colorOverLifetime; life.color = vanilla ? f.Life : Recolor(f.Life, c, a, f.Mix);
                    var speed = f.Ps.colorBySpeed; speed.color = vanilla ? f.Speed : Recolor(f.Speed, c, a, f.Mix);
                    var cd = f.Ps.customData;
                    if (f.HasCustom1) cd.SetColor(ParticleSystemCustomData.Custom1, vanilla ? f.Custom1 : Recolor(f.Custom1, c, a, f.Mix));
                    if (f.HasCustom2) cd.SetColor(ParticleSystemCustomData.Custom2, vanilla ? f.Custom2 : Recolor(f.Custom2, c, a, f.Mix));
                }
                // the light inside the ring: a hint of the colour, like the glow
                if (o.Light != null && CenterTint > 0f) o.Light.color = vanilla ? o.LightColor : Color.Lerp(o.LightColor, Recolor(o.LightColor, c), CenterTint);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Portal colour: " + e.Message);
            }
        }

        /// <summary>Same brightness and alpha, the portal colour's hue; saturation scaled so white-hot parts stay white.</summary>
        /// <summary>How much of the portal's colour the glow in the middle of the ring takes (the fire takes all of it).</summary>
        private const float CenterTint = 0f; // vanilla centre for now (1.0.18 tried 0.4)

        private static Color Recolor(Color orig, Color target, float amount, bool mix)
        {
            if (mix)
            {
                // the dark swirl is almost black: a hue change would keep it black, so blend it towards a darker portal colour
                var m = Color.Lerp(orig, target * SwirlBrightness, amount);
                m.a = orig.a;
                return m;
            }
            return amount >= 1f ? Recolor(orig, target) : Color.Lerp(orig, Recolor(orig, target), amount);
        }

        /// <summary>The dark swirl in the middle: how far it moves towards the portal colour, and how dark that colour is.</summary>
        private const float SwirlTint = 0.5f, SwirlBrightness = 0.55f;

        private static Color Recolor(Color orig, Color target)
        {
            Color.RGBToHSV(orig, out _, out float s, out float v);
            Color.RGBToHSV(target, out float th, out float ts, out _);
            var n = Color.HSVToRGB(th, Mathf.Clamp01(ts * Mathf.Max(s, 0.15f)), v, true);
            n.a = orig.a;
            return n;
        }

        private static Gradient Recolor(Gradient g, Color target, float amount, bool mix)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Recolor(keys[i].color, target, amount, mix);
            var n = new Gradient { mode = g.mode };
            n.SetKeys(keys, g.alphaKeys);
            return n;
        }

        private static ParticleSystem.MinMaxGradient Recolor(ParticleSystem.MinMaxGradient g, Color target, float amount, bool mix)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.color, target, amount, mix));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.colorMin, target, amount, mix), Recolor(g.colorMax, target, amount, mix));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.gradient, target, amount, mix));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Recolor(g.gradientMin, target, amount, mix), Recolor(g.gradientMax, target, amount, mix));
                case ParticleSystemGradientMode.RandomColor:
                {
                    var r = new ParticleSystem.MinMaxGradient(Recolor(g.gradient, target, amount, mix)) { mode = ParticleSystemGradientMode.RandomColor };
                    return r;
                }
                default:
                    return g;
            }
        }

        private static readonly HashSet<string> Dumped = new HashSet<string>();

        private static Orig Capture(TeleportWorld tw)
        {
            try
            {
                var o = new Orig { Emission = tw.m_colorTargetfound };
                var fade = tw.m_target_found;
                o.Light = fade != null ? fade.GetComponentInChildren<Light>(true) : null;
                if (o.Light != null) o.LightColor = o.Light.color;
                foreach (var r in tw.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var mat = r.sharedMaterial;
                    if (mat == null || mat.shader == null) continue;
                    string shader = mat.shader.name;
                    bool fire = shader.IndexOf("Gradient", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    bool glow = CenterTint > 0f && shader.IndexOf("Alpha Blended", System.StringComparison.OrdinalIgnoreCase) >= 0; // glow in the middle of the ring
                    bool swirl = shader.IndexOf("Surface", System.StringComparison.OrdinalIgnoreCase) >= 0; // the dark swirl in the middle
                    if (!fire && !glow && !swirl) continue; // the sparks stay vanilla
                    var ps = r.GetComponent<ParticleSystem>();
                    if (ps == null) continue;
                    var cd = ps.customData;
                    var f = new FireOrig
                    {
                        Ps = ps, Start = ps.main.startColor, Life = ps.colorOverLifetime.color, Speed = ps.colorBySpeed.color,
                        HasCustom1 = cd.enabled && cd.GetMode(ParticleSystemCustomData.Custom1) == ParticleSystemCustomDataMode.Color,
                        HasCustom2 = cd.enabled && cd.GetMode(ParticleSystemCustomData.Custom2) == ParticleSystemCustomDataMode.Color,
                        Amount = fire ? 1f : swirl ? SwirlTint : CenterTint,
                        Mix = swirl && !fire,
                    };
                    if (f.HasCustom1) f.Custom1 = cd.GetColor(ParticleSystemCustomData.Custom1);
                    if (f.HasCustom2) f.Custom2 = cd.GetColor(ParticleSystemCustomData.Custom2);
                    o.Fire.Add(f);
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

        /// <summary>Developer log: where the fire's colours come from (once per portal type).</summary>
        private static void Dump(TeleportWorld tw, Orig o)
        {
            string type = tw.name.Replace("(Clone)", "");
            if (!Dumped.Add(type)) return;
            var sb = new System.Text.StringBuilder($"[Perf] Portal fire of {type}: {o.Fire.Count} particle system(s), runes {o.Emission}");
            foreach (var f in o.Fire)
            {
                var ps = f.Ps;
                sb.Append($"\n  {ps.name}: start {Describe(f.Start)}; life {(ps.colorOverLifetime.enabled ? "on " + Describe(f.Life) : "off")};" +
                          $" speed {(ps.colorBySpeed.enabled ? "on" : "off")}; custom data {(ps.customData.enabled ? "on" : "off")}" +
                          $"{(f.HasCustom1 ? " c1 " + Describe(f.Custom1) : "")}{(f.HasCustom2 ? " c2 " + Describe(f.Custom2) : "")}");
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var m = r != null ? r.sharedMaterial : null;
                if (m != null && m.HasProperty("_GradientChannel")) sb.Append($"; channel {m.GetFloat("_GradientChannel")}, asAlpha {m.GetFloat("_GradientAsAlpha")}");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Describe(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return "color " + g.color;
                case ParticleSystemGradientMode.TwoColors: return $"two {g.colorMin}..{g.colorMax}";
                case ParticleSystemGradientMode.Gradient: return "gradient " + Keys(g.gradient);
                case ParticleSystemGradientMode.TwoGradients: return $"two gradients {Keys(g.gradientMin)} / {Keys(g.gradientMax)}";
                default: return g.mode + " " + Keys(g.gradient);
            }
        }

        private static string Keys(Gradient g)
        {
            if (g == null) return "-";
            var sb = new System.Text.StringBuilder("[");
            foreach (var k in g.colorKeys) sb.Append($"{k.time:0.00}:{k.color.r:0.00},{k.color.g:0.00},{k.color.b:0.00} ");
            return sb.Append("]").ToString();
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
                Vector3 sp = cam.WorldToScreenPoint(p.Position + Vector3.up * 3.6f);
                if (sp.z <= 0f) continue;
                EnsureStyle();

                float t = Mathf.Clamp01(d / maxD);
                int size = Mathf.RoundToInt(Mathf.Lerp(26f, 15f, t));
                float alpha = Mathf.Clamp01((maxD - d) / (maxD * 0.25f)); // fade out over the last quarter
                Color tint = PortalColors.Has(p.Color) ? PortalColors.Get(p.Color) : new Color(1f, 0.75f, 0.3f);
                string pic = Plugin.FloatingNamePictureFont.Value ? PortalFont.Prepare(dest.DisplayName) : null;
                if (pic != null)
                {
                    // picture font: capitals 30 px near, 16 px far
                    float cap = Mathf.Lerp(30f, 16f, t);
                    float tw = PortalFont.Width(pic, cap);
                    float aw = cap * 0.9f, ag = cap * 0.3f;
                    float px = sp.x - (aw + ag + tw) / 2f, capTop = Screen.height - sp.y - cap / 2f;
                    var keep = GUI.color;
                    GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
                    GUI.DrawTexture(new Rect(px + 1.5f, capTop + (cap - aw) / 2f + 1.5f, aw, aw), _arrow);
                    GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
                    GUI.DrawTexture(new Rect(px, capTop + (cap - aw) / 2f, aw, aw), _arrow);
                    GUI.color = keep;
                    PortalFont.Draw(pic, px + aw + ag, capTop, cap, alpha);
                    continue;
                }
                _name.fontSize = size;
                Content.text = dest.DisplayName;
                Vector2 ts = _name.CalcSize(Content);
                float arrowW = size * 0.9f, gap = size * 0.3f;
                float w = arrowW + gap + ts.x;
                float x = sp.x - w / 2f, y = Screen.height - sp.y - ts.y / 2f;

                var old = GUI.color;
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
            Color c = PortalColors.Get(color);
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

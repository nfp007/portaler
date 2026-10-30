using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Portaler
{
    /// <summary>
    /// Soft fiery line on the large map, from the portal you stand in to the previewed portal.
    /// Built from many small glowing dots (two layers: a wide faint glow and a brighter core) whose
    /// brightness waves flow towards the target, so it looks like a stream of fire rather than a hard line.
    /// </summary>
    public static class FlowLine
    {
        private delegate void WorldToMapPointFn(Minimap m, Vector3 p, ref float mx, ref float my);

        private static readonly WorldToMapPointFn WorldToMapPoint =
            AccessTools.MethodDelegate<WorldToMapPointFn>(AccessTools.Method(typeof(Minimap), "WorldToMapPoint"));

        private static readonly System.Func<Minimap, float, float, RawImage, Vector2> MapPointToLocalGuiPos =
            AccessTools.MethodDelegate<System.Func<Minimap, float, float, RawImage, Vector2>>(
                AccessTools.Method(typeof(Minimap), "MapPointToLocalGuiPos", new[] { typeof(float), typeof(float), typeof(RawImage) }));

        private static RectTransform _root;
        private static readonly List<Image> Glow = new List<Image>();
        private static readonly List<Image> Core = new List<Image>();
        private static Sprite _dot;
        private static Vector2 _dotAnchor;

        private const float Spacing = 4f;     // pixels between dots
        private const int MaxDots = 400;

        // red at the start, orange in the middle, yellow at the target
        private static Color Fire(float t)
        {
            var a = new Color(0.92f, 0.29f, 0.08f);
            var b = new Color(1f, 0.47f, 0.08f);
            var c = new Color(1f, 0.84f, 0.35f);
            return t < 0.5f ? Color.Lerp(a, b, t * 2f) : Color.Lerp(b, c, (t - 0.5f) * 2f);
        }

        public static void Show(Minimap map, Vector3 fromWorld, Vector3 toWorld)
        {
            if (map == null || map.m_pinRootLarge == null || map.m_mapImageLarge == null) { Hide(); return; }
            EnsureRoot(map);

            float ax = 0, ay = 0, bx = 0, by = 0;
            WorldToMapPoint(map, fromWorld, ref ax, ref ay);
            WorldToMapPoint(map, toWorld, ref bx, ref by);
            Vector2 a = MapPointToLocalGuiPos(map, ax, ay, map.m_mapImageLarge);
            Vector2 b = MapPointToLocalGuiPos(map, bx, by, map.m_mapImageLarge);

            // gentle curve: control point beside the middle of the line
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 8f) { Hide(); return; }
            Vector2 normal = new Vector2(-d.y, d.x) / len;
            Vector2 ctrl = (a + b) * 0.5f + normal * len * 0.18f;

            int n = Mathf.Clamp(Mathf.CeilToInt(len * 1.15f / Spacing), 6, MaxDots);
            EnsureDots(n);

            float pin = map.m_pinSizeLarge > 0 ? map.m_pinSizeLarge : 32f;
            float core = Mathf.Clamp(pin * 0.22f, 5f, 12f);
            float time = Time.unscaledTime;

            for (int i = 0; i < Core.Count; i++)
            {
                bool on = i < n;
                Core[i].enabled = on;
                Glow[i].enabled = on && i % 2 == 0; // the wide glow needs fewer dots
                if (!on) continue;

                float t = i / (float)(n - 1);
                float u = 1f - t;
                Vector2 p = u * u * a + 2f * u * t * ctrl + t * t * b;

                // brightness waves travelling towards the target
                float wave = 0.5f + 0.5f * Mathf.Sin((t * 7f - time * 1.6f) * Mathf.PI * 2f);
                // fade in/out at both ends so the line starts and ends softly at the rings
                float ends = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((1f - t) / 0.06f);
                Color c = Fire(t);

                var cr = Core[i].rectTransform;
                cr.anchoredPosition = p;
                float s = core * (0.85f + 0.45f * wave);
                cr.sizeDelta = new Vector2(s, s);
                Core[i].color = new Color(c.r, c.g, c.b, (0.35f + 0.5f * wave) * ends);

                if (Glow[i].enabled)
                {
                    var gr = Glow[i].rectTransform;
                    gr.anchoredPosition = p;
                    float g = core * 3.2f;
                    gr.sizeDelta = new Vector2(g, g);
                    Glow[i].color = new Color(c.r, c.g, c.b, 0.10f * ends);
                }
            }
            _root.gameObject.SetActive(true);
            _root.SetAsFirstSibling();
            PortalClient.SetOnTop(_root.gameObject, 5); // above other mods' pins and labels, below our pins
        }

        public static void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private static void EnsureRoot(Minimap map)
        {
            if (_root != null && _root.parent == map.m_pinRootLarge) return;
            if (_root != null) Object.Destroy(_root.gameObject);
            Glow.Clear();
            Core.Clear();
            var go = new GameObject("PortalerFlowLine", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(map.m_pinRootLarge, false);
            // fill the pin layer; dots are placed exactly like vanilla pins (same anchors as the pin prefab)
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _dotAnchor = Vector2.zero;
            if (map.m_pinPrefab != null)
            {
                var pr = map.m_pinPrefab.GetComponent<RectTransform>();
                if (pr != null) _dotAnchor = pr.anchorMin;
            }
            if (_dot == null) _dot = Art.SoftDot();
        }

        private static void EnsureDots(int n)
        {
            while (Core.Count < n)
            {
                Glow.Add(NewDot("g"));
                Core.Add(NewDot("c"));
            }
        }

        private static Image NewDot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = _dotAnchor;
            var img = go.GetComponent<Image>();
            img.sprite = _dot;
            img.raycastTarget = false; // never block clicks on the map
            return img;
        }
    }
}

using System.IO;
using UnityEngine;

namespace Portaler
{
    /// <summary>Images used by the mod: the ring map pin (embedded PNG) and simple UI textures drawn in code.</summary>
    public static class Art
    {
        private static Sprite _pin;

        /// <summary>
        /// Valheim renders in linear colour space; raw pixel values in our generated UI textures get brightened
        /// on screen. Convert so the colours look as designed.
        /// </summary>
        private static Color Px(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            var l = c.linear;
            l.a = c.a;
            return l;
        }

        /// <summary>Fiery portal ring for map pins, loaded from the embedded pin64.png.</summary>
        public static Sprite PinSprite()
        {
            if (_pin != null) return _pin;
            Texture2D tex = null;
            try
            {
                using (var s = typeof(Art).Assembly.GetManifestResourceStream("Portaler.pin64.png"))
                {
                    if (s != null)
                    {
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(tex, ms.ToArray())) tex = null;
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not load pin image: " + e.Message);
                tex = null;
            }
            if (tex == null) tex = FallbackRing(64);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            _pin = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _pin;
        }

        private static Texture2D FallbackRing(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) / 2f, outer = size / 2f - 1f, inner = outer * 0.55f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Color col = d > outer || d < inner ? Color.clear
                        : Color.Lerp(new Color(1f, 0.7f, 0.15f), new Color(0.8f, 0.1f, 0.05f), (d - inner) / (outer - inner));
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return tex;
        }

        /// <summary>A 9-slice friendly box: solid fill with an outer dark border and a thin inner highlight.</summary>
        public static Texture2D Box(Color fill, Color border, Color highlight, int borderPx = 2)
        {
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                    Color col = edge < borderPx ? border : edge == borderPx ? highlight : fill;
                    // subtle vertical shading like Valheim's wooden panels
                    if (edge > borderPx) col = Color.Lerp(col, Color.black, (1f - y / (float)size) * 0.12f);
                    tex.SetPixel(x, y, Px(col));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // ---------- wooden boards (drawn at their real size so edges stay irregular, never stretched) ----------

        private static float Hash(int ix, int iy, int seed)
        {
            unchecked
            {
                uint n = (uint)(ix * 374761393 + iy * 668265263 + seed * 1442695041);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 0xffff) / 65535f;
            }
        }

        private static float Noise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            float top = a + (b - a) * sx, bottom = c + (d - c) * sx;
            return top + (bottom - top) * sy;
        }

        private static Color Mul(Color c, float r, float g, float b) => new Color(c.r * r, c.g * g, c.b * b, 1f);

        /// <summary>
        /// A wooden board: lighter, grainy frame with uneven edges around a darker wooden field.
        /// Texture y runs bottom-up in Unity, so "top" here is just one side; the look is symmetric.
        /// </summary>
        public static Texture2D Wood(int w, int h, float frame, Color fill, Color frameCol, int seed, float rough)
        {
            w = Mathf.Max(8, w); h = Mathf.Max(8, h);
            var px = new Color32[w * h];
            Color frameLight = new Color(Mathf.Min(1f, frameCol.r * 1.25f), Mathf.Min(1f, frameCol.g * 1.22f), Mathf.Min(1f, frameCol.b * 1.18f), 1f);
            Color frameDark = Mul(frameCol, 0.72f, 0.70f, 0.68f);
            Color fillDark = Mul(fill, 0.8f, 0.78f, 0.76f);
            Color fillShadow = Mul(fill, 0.55f, 0.55f, 0.55f);
            Color seam = new Color(20 / 255f, 12 / 255f, 7 / 255f, 1f);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int dl = x, dr = w - 1 - x, dt = y, db = h - 1 - y;
                    int e = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db));
                    bool horiz = Mathf.Min(dt, db) <= Mathf.Min(dl, dr);
                    int along = horiz ? x : y;
                    int side = horiz ? (dt == e ? 0 : 1) : (dl == e ? 2 : 3);

                    float outer = Noise(along * 0.08f, side * 7.3f, seed) * rough;
                    float inner = frame + (Noise(along * 0.05f, side * 5.1f + 50f, seed) - 0.5f) * rough * 1.6f;
                    if (e < outer - 0.5f) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    float alpha = e >= outer + 0.5f ? 1f : (e - outer + 0.5f);

                    Color col;
                    if (e < inner)
                    {
                        float g = Noise(along * 0.03f, e * 0.9f + side * 11f, seed + 1);
                        float g2 = Noise(along * 0.15f, e * 0.4f + side * 3f, seed + 2);
                        col = Color.Lerp(frameCol, frameDark, Mathf.Clamp01(g * 0.8f + g2 * 0.3f));
                        float t = (e - outer) / Mathf.Max(1f, inner - outer);
                        if (t < 0.25f) col = Color.Lerp(col, frameLight, 0.5f * (1f - t / 0.25f));
                        if (inner - e < 1.6f) col = Color.Lerp(col, seam, 0.65f);
                    }
                    else
                    {
                        float g = Noise(x * 0.012f, y * 0.22f, seed + 3);
                        float g2 = Noise(x * 0.06f, y * 0.6f, seed + 4);
                        col = Color.Lerp(fill, fillDark, Mathf.Clamp01(g * 0.7f + g2 * 0.3f));
                        float d = e - inner;
                        if (d < 5f) col = Color.Lerp(col, fillShadow, (5f - d) / 5f * 0.6f);
                    }
                    col.a = alpha;
                    px[y * w + x] = Px(col);
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>Small right-pointing arrow ("you are here").</summary>
        public static Texture2D Arrow(Color col, int size = 24)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float halfH = (size - fx) * 0.5f;               // triangle narrows to the right
                    float dist = Mathf.Abs(fy - size / 2f) - halfH;  // <0 inside
                    float a = Mathf.Clamp01(0.5f - dist);
                    if (fx < 2f) a = 0f;
                    tex.SetPixel(x, y, Px(new Color(col.r, col.g, col.b, a)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>Left-pointing arrow ("back").</summary>
        public static Texture2D ArrowLeft(Color col, int size = 24)
        {
            var src = Arrow(col, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, src.GetPixel(size - 1 - x, y));
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            Object.Destroy(src);
            return tex;
        }

        /// <summary>Magnifying glass for the search box.</summary>
        public static Texture2D Magnifier(Color col, int size = 32)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 c = new Vector2(size * 0.42f, size * 0.58f);   // lens centre (texture y up)
            float r = size * 0.28f, w = size * 0.07f;
            Vector2 h0 = c + new Vector2(r * 0.72f, -r * 0.72f), h1 = new Vector2(size * 0.9f, size * 0.1f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float ring = Mathf.Abs(Vector2.Distance(p, c) - r) - w;
                    Vector2 ab = h1 - h0;
                    float t = Mathf.Clamp01(Vector2.Dot(p - h0, ab) / ab.sqrMagnitude);
                    float handle = Vector2.Distance(p, h0 + ab * t) - w * 1.5f;
                    float a = Mathf.Clamp01(0.5f - Mathf.Min(ring, handle));
                    tex.SetPixel(x, y, Px(new Color(col.r, col.g, col.b, a)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>Small downward-pointing arrow (open group).</summary>
        public static Texture2D ArrowDown(Color col, int size = 24)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;           // texture y goes up: tip at the bottom
                    float halfW = fy * 0.5f;
                    float dist = Mathf.Abs(fx - size / 2f) - halfW;
                    float a = Mathf.Clamp01(0.5f - dist);
                    if (fy > size - 2f) a = 0f;
                    tex.SetPixel(x, y, Px(new Color(col.r, col.g, col.b, a)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>Five-point star, filled (favorite) or just an outline (not favorite).</summary>
        public static Texture2D Star(bool filled, int size = 32) =>
            Star(filled, new Color(1f, 0.78f, 0.2f, 1f), new Color(1f, 0.95f, 0.6f, 1f), new Color(0.25f, 0.13f, 0.04f, 1f), size);

        /// <summary>Silver star for personal favorites.</summary>
        public static Texture2D SilverStar(int size = 32) =>
            Star(true, new Color(0.72f, 0.74f, 0.78f, 1f), new Color(0.97f, 0.98f, 1f, 1f), new Color(0.15f, 0.15f, 0.18f, 1f), size);

        /// <summary>Soft round golden glow, drawn behind shared-favorite stars.</summary>
        public static Texture2D Glow(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * 0.9f;
                    tex.SetPixel(x, y, Px(new Color(1f, 0.8f, 0.25f, a)));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D Star(bool filled, Color main, Color light, Color edgeCol, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pts = new Vector2[10];
            float c = size / 2f, ro = size * 0.48f, ri = ro * 0.45f;
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? ro : ri;
                pts[i] = new Vector2(c + Mathf.Cos(a) * r, c + Mathf.Sin(a) * r);
            }
            var dim = new Color(0.75f, 0.62f, 0.45f, 0.75f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = Inside(p, pts);
                    float d = DistToEdges(p, pts);
                    Color col = Color.clear;
                    if (filled)
                    {
                        if (inside) col = d < 1.6f ? edgeCol : Color.Lerp(main, light, (y / (float)size) * 0.45f);
                    }
                    else if (d < 1.4f) col = dim;
                    tex.SetPixel(x, y, Px(col));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static float DistToEdges(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}

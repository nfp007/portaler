using System.Collections.Generic;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// "You died" window: on the first portal you walk into after a death, offer the portal nearest to your
    /// newest grave and the portal of your last trip (one merged button when both are the same).
    /// Keys 1 / 2, Enter = first, Esc = close (the list stays open). Personal, nothing goes to the server.
    /// </summary>
    public static partial class PortalUI
    {
        private sealed class GraveChoice { public string Label, Tag; public PortalInfo Portal; }

        private static bool _graveOpen;
        private static int _graveDay;
        private static readonly List<GraveChoice> GraveChoices = new List<GraveChoice>();
        private static GUIStyle _graveName, _graveTag;
        private static Sprite _deathSprite;
        private static bool _deathSpriteTried;

        private const float GraveW = 600f, GraveTop = 84f, GraveBtnH = 76f, GraveGap = 10f, GraveCloseH = 40f;

        /// <summary>Called when the list opens by walking into a portal.</summary>
        public static void CheckGrave()
        {
            _graveOpen = false;
            if (!Plugin.GraveDialog.Value || !PortalClient.NewestGrave(out var grave, out int day)) return;
            string key = Mathf.RoundToInt(grave.x) + ";" + Mathf.RoundToInt(grave.z) + ";" + day;
            if (Prefs.GetMisc("Grave") == key) return; // already asked for this grave

            var src = PortalClient.SourceId;
            PortalInfo near = null;
            float best = float.MaxValue;
            foreach (var p in PortalClient.Portals)
            {
                float d = Utils.DistanceXZ(p.Position, grave);
                if (d < best) { best = d; near = p; }
            }
            var last = PortalClient.PortalAt(Prefs.GetMisc("Dest"));
            if (near != null && near.Id == src) near = null; // you are already there
            if (last != null && last.Id == src) last = null;
            if (near == null && last == null) return;

            GraveChoices.Clear();
            if (near != null && last != null && near.Id == last.Id)
                GraveChoices.Add(new GraveChoice { Label = "Nearest to your grave and last trip", Portal = near, Tag = $"{Meters(best)} from your grave  ·  {Meters(FromHere(near))} from here" });
            else
            {
                if (near != null)
                    GraveChoices.Add(new GraveChoice { Label = "Nearest to your grave", Portal = near, Tag = $"{Meters(best)} from your grave  ·  {Meters(FromHere(near))} from here" });
                if (last != null)
                    GraveChoices.Add(new GraveChoice { Label = "Last trip", Portal = last, Tag = $"where you went before you died  ·  {Meters(Utils.DistanceXZ(last.Position, grave))} from your grave" });
            }
            Prefs.SetMisc("Grave", key);
            _graveDay = day;
            _graveOpen = true;
        }

        private static float FromHere(PortalInfo p) => Utils.DistanceXZ(p.Position, PortalClient.SourcePosition);
        private static string Meters(float d) => d < 1000f ? $"{Mathf.RoundToInt(d / 10f) * 10} m" : $"{d / 1000f:0.0} km";

        private static Rect GraveRect() => Centered(GraveW, GraveTop + GraveChoices.Count * (GraveBtnH + GraveGap) + GraveCloseH + 22f);
        private static Rect GraveButton(Rect gr, int i) => new Rect(gr.x + 22f, gr.y + GraveTop + i * (GraveBtnH + GraveGap), gr.width - 44f, GraveBtnH);
        private static Rect GraveClose(Rect gr) => new Rect(gr.x + 22f, gr.y + GraveTop + GraveChoices.Count * (GraveBtnH + GraveGap), gr.width - 44f, GraveCloseH);

        private static void GraveGo(int i)
        {
            if (i < 0 || i >= GraveChoices.Count) return;
            var p = GraveChoices[i].Portal;
            _graveOpen = false;
            PortalClient.TeleportTo(p);
        }

        private static void GraveInput(Event ev)
        {
            var gr = GraveRect();
            if (ev.type == EventType.KeyDown)
            {
                switch (ev.keyCode)
                {
                    case KeyCode.Escape: _graveOpen = false; ev.Use(); return;
                    case KeyCode.Alpha1: case KeyCode.Keypad1: ev.Use(); GraveGo(0); return;
                    case KeyCode.Alpha2: case KeyCode.Keypad2: ev.Use(); GraveGo(1); return;
                    case KeyCode.Return: case KeyCode.KeypadEnter:
                        ev.Use();
                        if (!PortalPad.KeyNav) GraveGo(0); // with the arrow keys Enter presses the chosen button
                        return;
                }
            }
            if (ev.type == EventType.MouseDown)
            {
                _modalPress = true;
                var mp = ev.mousePosition;
                ev.Use();
                for (int i = 0; i < GraveChoices.Count; i++)
                    if (GraveButton(gr, i).Contains(mp)) { GraveGo(i); return; }
                if (GraveClose(gr).Contains(mp) || !gr.Contains(mp)) _graveOpen = false;
                return;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        private static void DrawGrave(Event ev)
        {
            if (_graveName == null)
            {
                _graveName = new GUIStyle(_rowText) { fontSize = 16, richText = false, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
                _graveTag = new GUIStyle(_placeholder) { fontSize = 14 };
            }
            var gr = GraveRect();
            var mp = Pointer(ev);
            DrawBoard(gr, "picker", 3);

            // the game's own grave icon, then the title
            float tx = gr.x + 22f;
            var icon = DeathSprite();
            if (icon != null)
            {
                var tr = icon.textureRect;
                var t = icon.texture;
                GUI.DrawTextureWithTexCoords(new Rect(gr.x + 18f, gr.y + 12f, 28f, 28f), t,
                    new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height));
                tx += 30f;
            }
            _pickerTitle.Draw(new Rect(tx, gr.y + 12f, gr.width - 44f, 28f), Tmp("You died"), false, false, false, false);
            string sub = GraveChoices.Count > 1
                ? $"Your grave (day {_graveDay}) is waiting. Press 1 or 2 (Enter = first)."
                : $"Your grave (day {_graveDay}) is waiting. Press 1 or Enter to go there.";
            _placeholder.Draw(new Rect(gr.x + 22f, gr.y + 44f, gr.width - 44f, 24f), Tmp(sub), false, false, false, false);

            for (int i = 0; i < GraveChoices.Count; i++)
            {
                var r = GraveButton(gr, i);
                var c = GraveChoices[i];
                DrawBoard(r, r.Contains(mp) ? "row-hover" : "row", i);
                _pickerTitle.Draw(new Rect(r.x + 16f, r.y + 4f, r.width - 70f, 26f), Tmp(c.Label), false, false, false, false);
                _graveName.Draw(new Rect(r.x + 16f, r.y + 28f, r.width - 70f, 22f), Tmp(c.Portal.DisplayName), false, false, false, false);
                _graveTag.Draw(new Rect(r.x + 16f, r.y + 50f, r.width - 70f, 20f), Tmp(c.Tag), false, false, false, false);
                var kr = new Rect(r.xMax - 44f, r.y + (r.height - 30f) / 2f, 30f, 30f);
                DrawBoard(kr, "search", 0);
                _keyText.Draw(kr, Tmp((i + 1).ToString()), false, false, false, false);
            }
            var cl = GraveClose(gr);
            DrawBoard(cl, cl.Contains(mp) ? "sort-hover" : "sort", 4);
            _sortText.Draw(cl, Tmp("Close  (Esc)"), false, false, false, false);
        }

        private static Sprite DeathSprite()
        {
            if (_deathSpriteTried) return _deathSprite;
            _deathSpriteTried = true;
            try
            {
                var m = HarmonyLib.AccessTools.Method(typeof(Minimap), "GetSprite");
                if (m != null && Minimap.instance != null) _deathSprite = m.Invoke(Minimap.instance, new object[] { Minimap.PinType.Death }) as Sprite;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Grave icon not found: " + e.Message); }
            return _deathSprite;
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// "Edit portal" window: name, type (category) and colour in one compact window. Opened with E on a list row
    /// or map pin, "Edit..." in the menu, or E on a portal in the world. "Own..." opens a small window with the
    /// colour picker; nothing is written until Save (Enter), Cancel (Esc) drops everything.
    /// With a gamepad the D-pad moves between the buttons; A on the name opens the game's own text box (on-screen keyboard).
    /// While it is open the game treats it like its own text box (cursor shown, player input blocked).
    /// </summary>
    public static partial class PortalUI
    {
        private const string RenameControl = "PortalerRename";
        private static PortalInfo _renameFor;
        private static string _renameText = "";
        private static int _renameCat, _renameColor;
        private static bool _renameFocus, _ownOpen;
        private static GUIStyle _renameField, _renameCount, _tileName;
        private static Texture2D _rainbow;

        public static bool RenameOpen => _renameFor != null;

        /// <summary>Open the edit window (or the game's text box when playing with a gamepad).</summary>
        public static void OpenRename(PortalInfo p)
        {
            if (p == null) return;
            CloseWindows();
            PortalPad.KeyNav = false;
            _padWin = -1;
            _renameFor = p;
            _renameText = p.Name ?? "";
            _renameCat = Categories.Clamp(p.Category);
            _renameColor = PortalColors.Clamp(p.Color);
            _ownOpen = false;
            _renameFocus = true;
        }

        /// <summary>Gamepad: the name is typed in the game's own text box (it brings up the on-screen keyboard).</summary>
        private sealed class NameReceiver : TextReceiver
        {
            public string GetText() => _renameText ?? "";
            public void SetText(string text)
            {
                text = (text ?? "").Trim();
                if (text.Length > PortalClient.MaxNameLength) text = text.Substring(0, PortalClient.MaxNameLength);
                _renameText = text;
            }
        }

        private static void TypeNameWithPad()
        {
            if (TextInput.instance == null || PortalPad.RealTextInput()) return;
            GUIUtility.keyboardControl = 0;
            TextInput.instance.RequestText(new NameReceiver(), "Portal name", PortalClient.MaxNameLength);
        }

        private static void CloseRename()
        {
            _renameFor = null;
            _ownOpen = false;
            if (GUI.GetNameOfFocusedControl() == RenameControl) GUIUtility.keyboardControl = 0;
        }

        /// <summary>Name, type and colour are written together.</summary>
        private static void SaveRename()
        {
            var p = _renameFor;
            CloseRename();
            if (p == null) return;
            PortalClient.Rename(p, _renameText);
            if (_renameCat != p.Category) PortalClient.SetCategory(p, _renameCat);
            if (_renameColor != p.Color) PortalClient.SetColor(p, _renameColor);
        }

        // ----- layout: name field, 9 type tiles, 10 colour swatches (Vanilla, Own..., the 8 colours), Save / Cancel -----
        private const float RenameW = 600f, RenameH = 270f;
        private static Rect RenameRect() => Centered(RenameW, RenameH);
        private static Rect RenameFieldRect(Rect rr) => new Rect(rr.x + 22f, rr.y + 20f, rr.width - 44f, 40f);
        private static Rect RenameTile(Rect rr, int c)
        {
            float tw = (rr.width - 44f - 8f * 4f) / Categories.Count;
            return new Rect(rr.x + 22f + c * (tw + 4f), rr.y + 72f, tw, 60f);
        }
        private const int ColorSlots = PortalColors.Count + 1; // slot 1 = "Own..."
        private static Rect RenameSwatchCell(Rect rr, int slot)
        {
            float step = (rr.width - 44f) / ColorSlots;
            return new Rect(rr.x + 22f + slot * step, rr.y + 146f, step, 56f);
        }
        private static Rect SwatchIn(Rect cell) => new Rect(cell.center.x - 13f, cell.y + 6f, 26f, 26f);
        /// <summary>Colour of a swatch slot: 0 = Vanilla, 1 = own (-1), 2.. = the ready colours 1..8.</summary>
        private static int SlotColor(int slot) => slot == 0 ? 0 : slot == 1 ? -1 : slot - 1;
        private static Rect RenameButton(Rect rr, int i)
        {
            float bw = (rr.width - 44f - 8f) / 2f;
            return new Rect(rr.x + 22f + i * (bw + 8f), rr.y + 212f, bw, 40f);
        }

        // ----- "Own..." window: the colour picker (shares the picker state of the colour window) -----
        private static Rect OwnRect() => Centered(360f, 270f);
        private static Rect OwnSv(Rect or) => new Rect(or.x + 20f, or.y + 46f, 140f, 140f);
        private static Rect OwnHue(Rect or) => new Rect(or.x + 20f + 152f, or.y + 46f, 22f, 140f);
        private static Rect OwnPreview(Rect or) => new Rect(or.x + 20f + 152f + 40f, or.y + 46f, 90f, 90f);
        private static Rect OwnButton(Rect or, int i)
        {
            float bw = (or.width - 40f - 8f) / 2f;
            return new Rect(or.x + 20f + i * (bw + 8f), or.y + 208f, bw, 40f);
        }

        private static void OpenOwn()
        {
            _pickDrag = 0;
            if (PortalColors.Has(_renameColor)) Color.RGBToHSV(PortalColors.Get(_renameColor), out _pickH, out _pickS, out _pickV);
            else { _pickH = 0.08f; _pickS = 0.8f; _pickV = 1f; }
            _ownOpen = true;
            GUIUtility.keyboardControl = 0; // typing goes nowhere while the picker is open
        }

        private static void CloseOwn(bool use)
        {
            if (use) _renameColor = PortalColors.FromColor(PickColor);
            _ownOpen = false;
            _pickDrag = 0;
            _renameFocus = true;
        }

        private static void OwnPickAt(Rect or, Vector2 mp)
        {
            if (_pickDrag == 1)
            {
                var r = OwnSv(or);
                _pickS = Mathf.Clamp01((mp.x - r.x) / r.width);
                _pickV = Mathf.Clamp01(1f - (mp.y - r.y) / r.height);
            }
            else if (_pickDrag == 2)
            {
                var r = OwnHue(or);
                _pickH = Mathf.Clamp01((mp.y - r.y) / r.height) * 0.999f;
            }
        }

        private static void OwnInput(Event ev)
        {
            var or = OwnRect();
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape) { CloseOwn(false); ev.Use(); return; }
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { CloseOwn(true); ev.Use(); return; }
                if (ev.character != '\0' || ev.keyCode != KeyCode.None) { ev.Use(); return; }
            }
            var mp = ev.mousePosition;
            if (ev.type == EventType.MouseDown)
            {
                _modalPress = true;
                ev.Use();
                if (OwnSv(or).Contains(mp)) { _pickDrag = 1; OwnPickAt(or, mp); return; }
                if (OwnHue(or).Contains(mp)) { _pickDrag = 2; OwnPickAt(or, mp); return; }
                if (OwnButton(or, 0).Contains(mp)) { CloseOwn(true); return; }
                if (OwnButton(or, 1).Contains(mp)) { CloseOwn(false); return; }
                return;
            }
            if (ev.type == EventType.MouseDrag && _pickDrag != 0) { OwnPickAt(or, mp); ev.Use(); return; }
            if (ev.rawType == EventType.MouseUp) _pickDrag = 0;
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        /// <summary>Keys and clicks for the edit window (before anything else sees them).</summary>
        private static void RenameInput(Event ev)
        {
            if (PortalPad.RealTextInput()) return; // the game's text box (gamepad) has the keys for now
            if (_ownOpen) { OwnInput(ev); return; }
            var rr = RenameRect();
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape) { CloseRename(); ev.Use(); return; }
                if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { SaveRename(); ev.Use(); return; }
            }
            if (ev.type == EventType.MouseDown)
            {
                _modalPress = true;
                var mp = ev.mousePosition;
                if (RenameFieldRect(rr).Contains(mp))
                {
                    if (PortalPad.PadInput) { ev.Use(); TypeNameWithPad(); }
                    else _renameFocus = true; // the text field takes the click
                    return;
                }
                ev.Use();
                for (int c = 0; c < Categories.Count; c++)
                    if (RenameTile(rr, c).Contains(mp)) { _renameCat = c; return; }
                for (int s = 0; s < ColorSlots; s++)
                    if (RenameSwatchCell(rr, s).Contains(mp))
                    {
                        int col = SlotColor(s);
                        if (col < 0) OpenOwn(); else _renameColor = col;
                        return;
                    }
                if (RenameButton(rr, 0).Contains(mp)) { SaveRename(); return; }
                if (RenameButton(rr, 1).Contains(mp) || !rr.Contains(mp)) { CloseRename(); return; }
                return;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        /// <summary>Rainbow ring for the "Own..." swatch.</summary>
        private static Texture2D Rainbow()
        {
            if (_rainbow != null) return _rainbow;
            const int s = 64;
            _rainbow = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[s * s];
            float c = (s - 1) / 2f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    float a = Mathf.Clamp01((1f - r) * c) * Mathf.Clamp01((r - 0.52f) * c);
                    var col = Color.HSVToRGB((Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f, 0.85f, 1f);
                    col.a = a;
                    px[y * s + x] = col;
                }
            _rainbow.SetPixels(px);
            _rainbow.Apply();
            return _rainbow;
        }

        /// <summary>The window itself; called for every event (the text field needs them all).</summary>
        private static void RenameWindow(Event ev)
        {
            if (_renameField == null)
            {
                _renameField = new GUIStyle(_searchStyle) { fontSize = 18, padding = new RectOffset(14, 70, 0, 0) };
                _renameCount = new GUIStyle(_placeholder) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 14, 0, 0) };
                _tileName = new GUIStyle(_sortText) { fontSize = 12, padding = new RectOffset(0, 0, 0, 0), wordWrap = false, clipping = TextClipping.Overflow };
            }
            var rr = RenameRect();
            var field = RenameFieldRect(rr);
            var mp = _ownOpen ? new Vector2(-9999f, -9999f) : Pointer(ev);
            if (ev.type == EventType.Repaint)
            {
                DimScreen();
                DrawBoard(rr, "picker", 5);
                DrawBoard(field, "search-on", 1);
                for (int c = 0; c < Categories.Count; c++)
                {
                    var t = RenameTile(rr, c);
                    bool on = _renameCat == c;
                    DrawBoard(t, on ? "sort-on" : t.Contains(mp) ? "sort-hover" : "sort", c);
                    if (c > 0) GUI.DrawTexture(new Rect(t.center.x - 12f, t.y + 7f, 24f, 24f), _catIcons[c], ScaleMode.ScaleToFit, true);
                    else _pickerX.Draw(new Rect(t.x, t.y + 1f, t.width, 32f), Tmp("x"), false, false, false, false);
                    _tileName.Draw(new Rect(t.x, t.y + 36f, t.width, 20f), Tmp(Categories.Names[c]), false, false, false, false);
                }
                bool own = PortalColors.IsCustom(_renameColor);
                for (int s = 0; s < ColorSlots; s++)
                {
                    var cell = RenameSwatchCell(rr, s);
                    var sw = SwatchIn(cell);
                    int col = SlotColor(s);
                    bool hover = cell.Contains(mp);
                    if (col < 0)
                    {
                        if (own || hover)
                        {
                            var old = GUI.color;
                            GUI.color = own ? new Color32(255, 235, 170, 255) : new Color32(220, 200, 160, 160);
                            float g = own ? 12f : 8f;
                            GUI.DrawTexture(new Rect(sw.x - g / 2f, sw.y - g / 2f, sw.width + g, sw.height + g), Art.RingSprite().texture);
                            GUI.color = own ? PortalColors.Get(_renameColor) : new Color32(70, 52, 38, 255);
                            GUI.DrawTexture(new Rect(sw.x + 7f, sw.y + 7f, sw.width - 14f, sw.height - 14f), _disc);
                            GUI.color = old;
                        }
                        GUI.DrawTexture(sw, Rainbow());
                    }
                    else DrawSwatch(sw, col, _renameColor == col, hover);
                    string name = col < 0 ? "Own..." : PortalColors.Names[col];
                    _tileName.Draw(new Rect(cell.x, sw.yMax + 4f, cell.width, 18f), Tmp(name), false, false, false, false);
                }
                for (int i = 0; i < 2; i++)
                {
                    var b = RenameButton(rr, i);
                    DrawBoard(b, i == 0 ? (b.Contains(mp) ? "close-hover" : "close") : (b.Contains(mp) ? "sort-hover" : "sort"), i);
                    _closeText.Draw(b, Tmp(i == 0 ? "Save  (Enter)" : "Cancel  (Esc)"), false, false, false, false);
                }
            }

            GUI.SetNextControlName(RenameControl);
            string typed = GUI.TextField(field, _renameText ?? "", PortalClient.MaxNameLength, _renameField);
            if (!_ownOpen) _renameText = typed;
            if (_renameFocus && !_ownOpen && !PortalPad.PadInput && ev.type == EventType.Repaint)
            {
                GUI.FocusControl(RenameControl);
                _renameFocus = false;
            }
            if (ev.type == EventType.Repaint)
                _renameCount.Draw(field, Tmp($"{(_renameText ?? "").Length} / {PortalClient.MaxNameLength}"), false, false, false, false);

            if (_ownOpen && ev.type == EventType.Repaint) DrawOwn(ev);
            // gamepad: gold frame on the chosen button (drawn here, on top of the window)
            if (ev.type == EventType.Repaint && PortalPad.PadInput && (_padWin == 7 || _padWin == 8) && _padWinFocus < PadTargets.Count && !PortalPad.RealTextInput())
                DrawFocusFrame(PadTargets[_padWinFocus]);
        }

        private static void DrawOwn(Event ev)
        {
            var or = OwnRect();
            var mp = Pointer(ev);
            DimScreen();
            DrawBoard(or, "picker", 6);
            _pickerTitle.Draw(new Rect(or.x + 20f, or.y + 10f, or.width - 40f, 28f), Tmp("Your own colour"), false, false, false, false);
            EnsurePickerTextures();
            var sv = OwnSv(or); var hue = OwnHue(or);
            var old = GUI.color;
            GUI.color = new Color32(30, 20, 14, 255);
            GUI.DrawTexture(new Rect(sv.x - 2f, sv.y - 2f, sv.width + 4f, sv.height + 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(hue.x - 2f, hue.y - 2f, hue.width + 4f, hue.height + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(sv, _svTex);
            GUI.DrawTexture(hue, _hueTex);
            var m = new Vector2(sv.x + _pickS * sv.width, sv.y + (1f - _pickV) * sv.height);
            GUI.color = _pickV > 0.5f ? Color.black : Color.white;
            GUI.DrawTexture(new Rect(m.x - 7f, m.y - 7f, 14f, 14f), Art.RingSprite().texture);
            GUI.color = Color.white;
            float hy = hue.y + _pickH / 0.999f * hue.height;
            GUI.DrawTexture(new Rect(hue.x - 4f, hy - 1.5f, hue.width + 8f, 3f), Texture2D.whiteTexture);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(hue.x - 4f, hy - 2.5f, hue.width + 8f, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(hue.x - 4f, hy + 1.5f, hue.width + 8f, 1f), Texture2D.whiteTexture);
            var pv = OwnPreview(or);
            GUI.color = new Color32(30, 20, 14, 255);
            GUI.DrawTexture(new Rect(pv.x - 2f, pv.y - 2f, pv.width + 4f, pv.height + 4f), _disc);
            GUI.color = PickColor;
            GUI.DrawTexture(pv, _disc);
            GUI.color = old;
            _placeholder.Draw(new Rect(pv.x + 8f, pv.yMax + 6f, 100f, 22f), Tmp("Preview"), false, false, false, false);
            for (int i = 0; i < 2; i++)
            {
                var b = OwnButton(or, i);
                DrawBoard(b, i == 0 ? (b.Contains(mp) ? "close-hover" : "close") : (b.Contains(mp) ? "sort-hover" : "sort"), i + 2);
                _closeText.Draw(b, Tmp(i == 0 ? "Use  (Enter)" : "Back  (Esc)"), false, false, false, false);
            }
        }

        /// <summary>The window when the list is closed (E on a portal in the world).</summary>
        private static void DrawRenameAlone()
        {
            EnsureStyles();
            var ev = Event.current;
            if (ev.type == EventType.Layout && !PortalPad.RealTextInput())
            {
                PortalInfo travel = null, toggle = null;
                PadStep(ref travel, ref toggle); // gamepad in the world: only the window's buttons
            }
            if (_renameFor == null) return;
            RenameInput(ev);
            if (ev.rawType == EventType.MouseUp) _modalPress = false;
            if (_renameFor != null) RenameWindow(ev);
        }

        /// <summary>While the window is open the game sees an open text box: cursor visible, no walking, no hotkeys.</summary>
        [HarmonyPatch(typeof(TextInput), "Update")]
        private static class TextInput_Update
        {
            private static readonly AccessTools.FieldRef<TextInput, bool> Visible = AccessTools.FieldRefAccess<TextInput, bool>("m_visibleFrame");
            private static void Postfix(TextInput __instance) { if (_renameFor != null) Visible(__instance) = true; }
        }

        /// <summary>E on a portal in the world opens Portaler's edit window (wards still apply, as in the game).</summary>
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
        private static class TeleportWorld_Interact
        {
            private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, bool alt, ref bool __result)
            {
                try
                {
                    if (hold || alt || human != Player.m_localPlayer) return true;
                    if (!PrivateArea.CheckAccess(__instance.transform.position)) return true; // the game says why
                    var nview = __instance.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid()) return true;
                    var p = PortalClient.Get(nview.GetZDO().m_uid);
                    if (p == null) return true;
                    OpenRename(p);
                    __result = true;
                    return false;
                }
                catch { return true; }
            }
        }
    }
}

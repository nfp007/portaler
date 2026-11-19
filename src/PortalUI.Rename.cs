using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Rename window: name field + portal type (category) in one window. Opened with E on a list row or map pin,
    /// "Rename" in the menu, or E on a portal in the world. Enter saves both, Esc cancels.
    /// With a gamepad the game's own text box is used instead (it brings up the on-screen keyboard).
    /// While it is open the game treats it like its own text box (cursor shown, player input blocked).
    /// </summary>
    public static partial class PortalUI
    {
        private const string RenameControl = "PortalerRename";
        private static PortalInfo _renameFor;
        private static string _renameText = "";
        private static int _renameCat;
        private static bool _renameFocus;
        private static GUIStyle _renameField, _renameCount, _tileName;

        public static bool RenameOpen => _renameFor != null;

        /// <summary>Open the rename window (or the game's text box when playing with a gamepad).</summary>
        public static void OpenRename(PortalInfo p)
        {
            if (p == null) return;
            if (PortalPad.PadInput || (ZInput.IsGamepadActive() && !PortalClient.Active)) { PortalClient.BeginRename(p); return; }
            CloseWindows();
            _renameFor = p;
            _renameText = p.Name ?? "";
            _renameCat = Categories.Clamp(p.Category);
            _renameFocus = true;
        }

        private static void CloseRename()
        {
            _renameFor = null;
            if (GUI.GetNameOfFocusedControl() == RenameControl) GUIUtility.keyboardControl = 0;
        }

        private static void SaveRename()
        {
            var p = _renameFor;
            CloseRename();
            if (p == null) return;
            PortalClient.Rename(p, _renameText);
            if (_renameCat != p.Category) PortalClient.SetCategory(p, _renameCat);
        }

        private const float RenameW = 560f, RenameH = 300f;
        private static Rect RenameRect() => Centered(RenameW, RenameH);
        private static Rect RenameFieldRect(Rect rr) => new Rect(rr.x + 22f, rr.y + 72f, rr.width - 44f, 40f);
        private static Rect RenameTile(Rect rr, int slot)
        {
            float tw = (rr.width - 44f - 8f * 4f) / Categories.Count;
            return new Rect(rr.x + 22f + slot * (tw + 4f), rr.y + 150f, tw, 64f);
        }
        // tile order: None first, then the categories
        private static int TileCat(int slot) => slot;
        private static Rect RenameButton(Rect rr, int i)
        {
            float bw = (rr.width - 44f - 8f) / 2f;
            return new Rect(rr.x + 22f + i * (bw + 8f), rr.y + 236f, bw, 40f);
        }

        /// <summary>Keys and clicks for the rename window (before anything else sees them).</summary>
        private static void RenameInput(Event ev)
        {
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
                if (RenameFieldRect(rr).Contains(mp)) { _renameFocus = true; return; } // the text field takes it
                ev.Use();
                for (int s = 0; s < Categories.Count; s++)
                    if (RenameTile(rr, s).Contains(mp)) { _renameCat = TileCat(s); return; }
                if (RenameButton(rr, 0).Contains(mp)) { SaveRename(); return; }
                if (RenameButton(rr, 1).Contains(mp) || !rr.Contains(mp)) { CloseRename(); return; }
                return;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        /// <summary>The window itself; called for every event (the text field needs them all).</summary>
        private static void RenameWindow(Event ev)
        {
            if (_renameField == null)
            {
                _renameField = new GUIStyle(_searchStyle) { fontSize = 18, padding = new RectOffset(14, 70, 0, 0) };
                _renameCount = new GUIStyle(_placeholder) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 14, 0, 0) };
                _tileName = new GUIStyle(_sortText) { fontSize = 12, padding = new RectOffset(0, 0, 0, 0) };
            }
            var rr = RenameRect();
            var field = RenameFieldRect(rr);
            var mp = ev.mousePosition;
            if (ev.type == EventType.Repaint)
            {
                DimScreen();
                DrawBoard(rr, "picker", 5);
                _pickerTitle.Draw(new Rect(rr.x + 22f, rr.y + 12f, rr.width - 44f, 28f), Tmp("Rename portal"), false, false, false, false);
                _placeholder.Draw(new Rect(rr.x + 22f, rr.y + 42f, rr.width - 44f, 22f), Tmp("Same for everyone on the server. Enter saves, Esc cancels."), false, false, false, false);
                DrawBoard(field, "search-on", 1);
                _sortLabel.Draw(new Rect(rr.x + 22f, rr.y + 120f, 200f, 26f), Tmp("Type"), false, false, false, false);
                for (int s = 0; s < Categories.Count; s++)
                {
                    var t = RenameTile(rr, s);
                    int c = TileCat(s);
                    bool on = _renameCat == c;
                    DrawBoard(t, on ? "sort-on" : t.Contains(mp) ? "sort-hover" : "sort", s);
                    if (c > 0) GUI.DrawTexture(new Rect(t.center.x - 12f, t.y + 8f, 24f, 24f), _catIcons[c], ScaleMode.ScaleToFit, true);
                    else _pickerX.Draw(new Rect(t.x, t.y + 2f, t.width, 34f), Tmp("x"), false, false, false, false);
                    _tileName.Draw(new Rect(t.x, t.y + 38f, t.width, 22f), Tmp(Categories.Names[c]), false, false, false, false);
                }
                for (int i = 0; i < 2; i++)
                {
                    var b = RenameButton(rr, i);
                    DrawBoard(b, i == 0 ? (b.Contains(mp) ? "close-hover" : "close") : (b.Contains(mp) ? "sort-hover" : "sort"), i);
                    _closeText.Draw(b, Tmp(i == 0 ? "Save  (Enter)" : "Cancel  (Esc)"), false, false, false, false);
                }
            }

            GUI.SetNextControlName(RenameControl);
            _renameText = GUI.TextField(field, _renameText ?? "", PortalClient.MaxNameLength, _renameField);
            if (_renameFocus && ev.type == EventType.Repaint)
            {
                GUI.FocusControl(RenameControl);
                _renameFocus = false;
            }
            if (ev.type == EventType.Repaint)
                _renameCount.Draw(field, Tmp($"{(_renameText ?? "").Length} / {PortalClient.MaxNameLength}"), false, false, false, false);
        }

        /// <summary>The window when the list is closed (E on a portal in the world).</summary>
        private static void DrawRenameAlone()
        {
            EnsureStyles();
            var ev = Event.current;
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

        /// <summary>E on a portal in the world opens Portaler's rename window (wards still apply, as in the game).</summary>
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
        private static class TeleportWorld_Interact
        {
            private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, bool alt, ref bool __result)
            {
                try
                {
                    if (hold || alt || human != Player.m_localPlayer || ZInput.IsGamepadActive()) return true;
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

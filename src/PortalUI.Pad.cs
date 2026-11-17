using System.Collections.Generic;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Gamepad for the list and its windows.
    /// List: D-pad / right stick = move, D-pad left/right = previous/next group, LB/RB = sort, A = travel,
    /// X = favorite, Y = menu, B = close (vanilla). Windows: D-pad moves between buttons, A presses, B goes back;
    /// in the colour window the right stick and LB/RB pick an own colour.
    /// Windows are driven by the same code as the mouse: A sends a click to the middle of the chosen button.
    /// </summary>
    public static partial class PortalUI
    {
        private static int _vis, _visCount, _padFocus, _padWinFocus, _padWin = -1;
        private static bool _padInit, _padScroll, _boardFocus;
        private static PortalInfo _padPortal;
        private static Vector2 _padRowScreen;
        private static readonly Vector2 Nowhere = new Vector2(-9999f, -9999f);
        private static Vector2 _padPoint = Nowhere;
        private static float _viewH = 400f;
        private static readonly List<int> GroupStarts = new List<int>();
        private static readonly List<Rect> PadTargets = new List<Rect>();

        /// <summary>Where buttons are drawn as "hovered": the mouse, or the gamepad's chosen button.</summary>
        private static Vector2 Pointer(Event ev) => PortalPad.Active ? _padPoint : ev.mousePosition;

        /// <summary>Which window gets the input (same order as in Draw).</summary>
        private static int WindowKind => _picker != null ? 1 : _playersOpen ? 2 : _menuFor != null ? 3 : _linkOpen ? 4 : _colorFor != null ? 5 : 0;

        /// <summary>Called once per frame (Layout) before anything is drawn.</summary>
        private static void PadStep(ref PortalInfo travel, ref PortalInfo toggle)
        {
            if (!PortalPad.Active) { _padWin = -1; _padPoint = Nowhere; PortalPad.Clear(); return; }
            int kind = WindowKind;
            if (kind != 0) { WindowPad(kind); PortalPad.Clear(); return; }
            _padWin = 0;
            _padPoint = Nowhere;

            if (PortalPad.B && Picking) { _pickEnd = -1; _linkOpen = true; PortalPad.Clear(); return; }

            int last = Mathf.Max(0, _visCount - 1);
            int before = _padFocus;
            if (PortalPad.Up) _padFocus--;
            if (PortalPad.Down) _padFocus++;
            if (PortalPad.Left || PortalPad.Right)
            {
                int target = -1;
                foreach (int s in GroupStarts)
                {
                    if (s >= _visCount) continue; // empty or folded group at the end
                    if (PortalPad.Left && s < _padFocus) target = s;
                    if (PortalPad.Right && s > _padFocus && target < 0) target = s;
                }
                if (target >= 0) _padFocus = target;
                else if (PortalPad.Left) _padFocus = 0;
            }
            _padFocus = Mathf.Clamp(_padFocus, 0, last);
            if (_padFocus != before) { _padScroll = true; _padInit = false; }

            if (PortalPad.LB || PortalPad.RB)
            {
                string[] modes = { "Biome", "Name", "Distance" };
                int i = System.Array.IndexOf(modes, Prefs.SortMode);
                if (i < 0) i = 0;
                i = (i + (PortalPad.RB ? 1 : modes.Length - 1)) % modes.Length;
                Prefs.SortMode = modes[i];
                Prefs.SortReversed = false;
                _scroll = Vector2.zero;
                _padFocus = 0;
                _padInit = true;
            }

            var p = _padPortal;
            if (p != null)
            {
                bool here = p.Id == PortalClient.SourceId;
                if (PortalPad.A && (!here || Picking)) travel = p;
                else if (PortalPad.X) toggle = p;
                else if (PortalPad.Y) OpenMenu(p, _padRowScreen);
            }
            PortalPad.Clear();
        }

        /// <summary>Clickable buttons of the open window, in reading order. Returns the button chosen first.</summary>
        private static int Targets(int kind, List<Rect> t)
        {
            t.Clear();
            switch (kind)
            {
                case 1:
                {
                    var pr = PickerRect();
                    for (int c = 1; c < Categories.Count; c++) t.Add(PickerTile(pr, c));
                    t.Add(PickerTile(pr, 0)); // "None" is the last tile
                    return _picker.Category > 0 ? _picker.Category - 1 : t.Count - 1;
                }
                case 2:
                {
                    var known = _model != null ? _model.Known : PortalClient.KnownPlayers();
                    var pr = PlayersRect(known.Count);
                    var done = PlayersDone(pr);
                    for (int i = 0; i < known.Count; i++)
                    {
                        var row = PlayerRow(pr, i);
                        if (row.yMax > done.y - 4f) break;
                        t.Add(row);
                    }
                    t.Add(done);
                    return 0;
                }
                case 3:
                {
                    var mr = MenuRect();
                    for (int i = 0; i < MenuItems.Count; i++) t.Add(MenuItemRect(mr, i));
                    return 0;
                }
                case 4:
                {
                    var lr = LinkRect();
                    t.Add(ChangeButton(lr, 0)); t.Add(ChangeButton(lr, 1)); t.Add(SwapButton(lr));
                    t.Add(DirRect(lr, 0)); t.Add(DirRect(lr, 1));
                    for (int i = 0; i < PortalColors.Count; i++) t.Add(SwatchRect(lr, i));
                    t.Add(LinkButton(lr, 0)); t.Add(LinkButton(lr, 1));
                    return LinkReady ? t.Count - 2 : _linkTo == null ? 1 : 0;
                }
                case 5:
                {
                    var cr = ColorRect();
                    for (int i = 0; i < PortalColors.Count; i++) t.Add(ColorTile(cr, i));
                    t.Add(UseRect(cr));
                    int c = _colorFor.Color;
                    return PortalColors.IsCustom(c) || c < 0 || c >= PortalColors.Count ? t.Count - 1 : c;
                }
            }
            return 0;
        }

        private static void WindowPad(int kind)
        {
            int first = Targets(kind, PadTargets);
            if (PadTargets.Count == 0) { _padPoint = Nowhere; return; }
            if (kind != _padWin) { _padWin = kind; _padWinFocus = first; }
            _padWinFocus = Mathf.Clamp(_padWinFocus, 0, PadTargets.Count - 1);

            bool stickColour = false;
            if (kind == 5)
            {
                // own colour: right stick = saturation (left/right) and brightness (up/down), LB/RB = hue
                var st = PortalPad.Stick;
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                if (st != Vector2.zero || PortalPad.Tab != 0)
                {
                    stickColour = true;
                    _pickS = Mathf.Clamp01(_pickS + st.x * dt * 0.8f);
                    _pickV = Mathf.Clamp01(_pickV - st.y * dt * 0.8f);
                    _pickH = Mathf.Repeat(_pickH + PortalPad.Tab * dt * 0.2f, 0.999f);
                    _padWinFocus = PadTargets.Count - 1; // "Use this colour"
                }
            }
            if (!stickColour)
            {
                if (PortalPad.Up) PadMove(0f, -1f);
                if (PortalPad.Down) PadMove(0f, 1f);
                if (PortalPad.Left) PadMove(-1f, 0f);
                if (PortalPad.Right) PadMove(1f, 0f);
            }
            _padPoint = PadTargets[_padWinFocus].center;

            Event fake = null;
            if (PortalPad.B) fake = new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape };
            else if (PortalPad.A) fake = new Event { type = EventType.MouseDown, button = 0, mousePosition = _padPoint };
            if (fake == null) return;
            switch (kind)
            {
                case 1: PickerInput(fake); break;
                case 2: PlayersInput(fake); break;
                case 3: MenuInput(fake); break;
                case 4: LinkInput(fake); break;
                case 5: ColorInput(fake); break;
            }
            _modalPress = false; // no mouse-up will follow
            _pickDrag = 0;
        }

        /// <summary>Nearest button in the pressed direction (sideways distance counts double).</summary>
        private static void PadMove(float dx, float dy)
        {
            var c = PadTargets[_padWinFocus].center;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < PadTargets.Count; i++)
            {
                if (i == _padWinFocus) continue;
                var d = PadTargets[i].center - c;
                float along = d.x * dx + d.y * dy;
                if (along < 1f) continue;
                float score = along + Mathf.Abs(d.x * dy - d.y * dx) * 2f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) _padWinFocus = best;
        }

        /// <summary>Row drawing: the chosen row remembers itself and scrolls into view.</summary>
        private static void PadRow(PortalInfo p, Rect r, int vis)
        {
            _padPortal = p;
            _hovered = p;
            _padRowScreen = GUIUtility.GUIToScreenPoint(new Vector2(r.x + 40f, r.yMax));
            if (!_padScroll) return;
            _padScroll = false;
            float top = GroupStarts.Contains(vis) ? 44f : 6f; // first row of a group: show its title too
            if (r.y - top < _scroll.y) _scroll.y = Mathf.Max(0f, r.y - top);
            else if (r.yMax + 6f > _scroll.y + _viewH) _scroll.y = r.yMax + 6f - _viewH;
        }

        /// <summary>Thin gold frame around the row the gamepad is on.</summary>
        private static void DrawFocusFrame(Rect r)
        {
            var old = GUI.color;
            GUI.color = new Color32(255, 200, 90, 230);
            const float w = 2f;
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - 1f, r.yMax - 1f, r.width + 2f, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, w, r.height + 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y - 1f, w, r.height + 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private const string PadHint = "D-pad: move, left/right = groups.  A = travel, X = favorite, Y = menu, B = close.\nLB / RB = sort.  In windows: D-pad + A, B = back.";
    }
}

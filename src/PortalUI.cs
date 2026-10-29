using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// The portal list shown on the left of the large map while choosing a destination.
    /// Wooden Valheim-style board: Favorites, Last built, then every portal grouped by biome.
    /// Left click a name = travel, right click (or click the star) = favorite on/off.
    /// The portal you stand in is shown with a red frame and an arrow, and cannot be chosen.
    /// </summary>
    public static class PortalUI
    {
        public static bool MouseOverList { get; private set; }

        /// <summary>Mouse is over the list, or a button was pressed on it and is still held (scrollbar drag).</summary>
        public static bool ListHasMouse => MouseOverList || _pressedInList;
        private static bool _pressedInList;

        // search box
        private const string SearchControl = "PortalerSearch";
        private static string _search = "";
        public static bool SearchFocused { get; private set; }
        private static GUIStyle _searchStyle, _placeholder, _clearStyle, _backText, _backLabel;
        private static Texture2D _magnifier, _backIcon;

        private const int NameChars = 25;      // panel must fit names this long
        private const float StarSize = 22f;
        private const float ArrowSize = 16f;
        private const int RowHeight = 36;
        private const int Variants = 3;

        private static Vector2 _scroll;
        private static PortalInfo _hovered;
        private static Rect _rect;
        private static bool _stylesReady;
        private static GUIStyle _headerButton;
        private static Texture2D _foldOpen, _foldClosed;
        private static GUIStyle _title, _hint, _header, _rowText, _hereText, _hereTag, _closeText, _scrollbar, _distTag, _sortText, _sortLabel;
        private const int SortHeight = 30;
        private static Texture2D _starOn, _starOff, _starSilver, _glow, _arrow, _thumb;
        private static float _width;

        private static Texture2D _panelTex;
        private static Vector2Int _panelSize;
        private static readonly Dictionary<string, Texture2D> RowTex = new Dictionary<string, Texture2D>();

        // Valheim-like colours
        private static readonly Color PanelFill = new Color32(70, 48, 33, 255);
        private static readonly Color PanelFrame = new Color32(150, 107, 68, 255);
        private static readonly Color RowFill = new Color32(44, 30, 21, 255);
        private static readonly Color RowFrame = new Color32(126, 90, 58, 255);
        private static readonly Color HoverFill = new Color32(80, 55, 35, 255);
        private static readonly Color HoverFrame = new Color32(160, 115, 70, 255);
        private static readonly Color HereFrame = new Color32(196, 42, 26, 255);
        private static readonly Color HereFill = new Color32(58, 26, 18, 255);
        private static readonly Color CloseFill = new Color32(96, 60, 26, 255);
        private static readonly Color CloseHoverFill = new Color32(130, 82, 34, 255);
        private static readonly Color Orange = new Color32(255, 178, 64, 255);
        private static readonly Color TextLight = new Color32(242, 230, 205, 255);
        private static readonly Color HereText = new Color32(255, 150, 130, 255);

        public static void OnOpened()
        {
            _scroll = Vector2.zero;
            _search = "";
            _wantUnfocus = true;
        }

        public static void Draw()
        {
            if (!PortalClient.Active)
            {
                MouseOverList = false;
                _pressedInList = false;
                if (SearchFocused) { GUIUtility.keyboardControl = 0; SearchFocused = false; }
                return;
            }
            EnsureStyles();

            float top = 80f;
            _rect = new Rect(16f, top, _width, Mathf.Max(300f, Screen.height - top - 70f));
            var ev = Event.current;
            MouseOverList = _rect.Contains(ev.mousePosition);
            if (ev.type == EventType.MouseDown && MouseOverList) _pressedInList = true;
            if (ev.rawType == EventType.MouseUp) _pressedInList = false;

            // While the rename box is open, the list ignores the mouse (keys must stay untouched for the text box).
            bool typing = TextInput.IsVisible();
            if (typing && (ev.isMouse || ev.type == EventType.ScrollWheel)) ev.Use();

            if (_wantUnfocus) { _wantUnfocus = false; GUIUtility.keyboardControl = 0; }
            SearchFocused = GUI.GetNameOfFocusedControl() == SearchControl;
            if (SearchFocused && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.Escape) { _search = ""; GUIUtility.keyboardControl = 0; ev.Use(); }
                else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { GUIUtility.keyboardControl = 0; ev.Use(); }
            }
            // a click anywhere outside the search box ends typing
            if (SearchFocused && ev.type == EventType.MouseDown && !MouseOverList) GUIUtility.keyboardControl = 0;

            // E over a name = rename that portal (the hovered row comes from the last repaint).
            if (!typing && !SearchFocused && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.E && _hovered != null && MouseOverList)
            {
                PortalClient.BeginRename(_hovered); // opened on the next Update, outside OnGUI
            }
            if (ev.type == EventType.Repaint) _hovered = null;

            if (ev.type == EventType.Repaint)
                GUI.DrawTexture(_rect, PanelTexture((int)_rect.width, (int)_rect.height));

            // Our own scrollbar thumb, restored afterwards so other mods' windows are untouched.
            var thumbStyle = GUI.skin.verticalScrollbarThumb;
            var oldThumb = thumbStyle.normal.background;
            var oldThumbBorder = thumbStyle.border;
            thumbStyle.normal.background = _thumb;
            thumbStyle.border = new RectOffset(2, 2, 2, 2);

            PortalInfo travel = null, toggle = null;
            try
            {
                GUILayout.BeginArea(new Rect(_rect.x + 22, _rect.y + 18, _rect.width - 44, _rect.height - 36));
                GUILayout.Label("Portaler", _title);
                GUILayout.Label("Click a name or a map pin to travel. E over a name = rename.\nRight click a name = favorite: mine (silver) > shared (gold) > off.", _hint);
                GUILayout.Space(6);
                SortBar();
                GUILayout.Space(6);
                SearchBox();
                GUILayout.Space(6);

                _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, _scrollbar);

                string q = (_search ?? "").Trim();
                bool searching = q.Length > 0;
                var everything = PortalClient.Portals;
                var all = searching
                    ? everything.Where(p => p.DisplayName.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                    : everything;
                _highlight = searching ? q : null;
                _forceOpen = searching;
                // shared favorites first, then this player's own; each A-Z
                var favs = all.Where(PortalClient.IsFavorite)
                    .OrderBy(p => PortalClient.GetFav(p) == PortalClient.FavState.Shared ? 0 : 1)
                    .ThenBy(p => p.DisplayName, System.StringComparer.OrdinalIgnoreCase).ToList();
                var last = PortalClient.LastBuilt;
                int index = 0;

                if (searching)
                {
                    GUILayout.Label($"{all.Count} of {everything.Count} portals match \"{q}\"", _distTagLeft);
                }
                else
                {
                    var back = PortalClient.BackPortal;
                    if (back != null) { BackRow(back, ref travel, ref toggle); GUILayout.Space(6); }
                    if (favs.Count > 0) Group("FAV", $"Favorites  ({favs.Count})", favs, ref index, ref travel, ref toggle);
                    if (last.Count > 0) Group("LAST", "Last built", last, ref index, ref travel, ref toggle);
                }

                bool rev = Prefs.SortReversed;
                var byName = System.StringComparer.OrdinalIgnoreCase;
                switch (Prefs.SortMode)
                {
                    case "Name":
                    {
                        var list = rev ? all.OrderByDescending(p => p.DisplayName, byName).ToList()
                                       : all.OrderBy(p => p.DisplayName, byName).ToList();
                        Group("ALL", $"All portals  ({list.Count})", list, ref index, ref travel, ref toggle);
                        break;
                    }
                    case "Distance":
                    {
                        Vector3 here = PortalClient.SourcePosition;
                        var list = rev ? all.OrderByDescending(p => Utils.DistanceXZ(p.Position, here)).ToList()
                                       : all.OrderBy(p => Utils.DistanceXZ(p.Position, here)).ToList();
                        Group("ALL", $"All portals  ({list.Count})", list, ref index, ref travel, ref toggle);
                        break;
                    }
                    default:
                    {
                        // Default: Deep North first ... Meadows last ("Other" always at the end). Reversed: Meadows first.
                        var groups = all.GroupBy(p => p.BiomeCode)
                            .OrderBy(g => g.Key == "XX" ? 1 : 0)
                            .ThenBy(g => rev ? Biomes.SortIndex(g.Key) : -Biomes.SortIndex(g.Key));
                        foreach (var g in groups)
                        {
                            var entry = Biomes.Order.FirstOrDefault(b => b.Code == g.Key);
                            string title = string.IsNullOrEmpty(entry.Display) ? g.Key : entry.Display;
                            Group(g.Key, $"{title}  ({g.Count()})", g.OrderBy(p => p.DisplayName, byName).ToList(),
                                ref index, ref travel, ref toggle);
                        }
                        break;
                    }
                }
                if (searching && all.Count == 0) GUILayout.Label("No portal matches. Esc or x clears the search.", _hint);
                else if (!searching && all.Count <= 1) GUILayout.Label("No other portals yet.", _hint);
                GUILayout.EndScrollView();

                GUILayout.Space(8);
                if (Board(GUILayoutUtility.GetRect(GUIContent.none, _closeText, GUILayout.ExpandWidth(true), GUILayout.Height(RowHeight)),
                        "Close", _closeText, "close", 0))
                    PortalClient.Deactivate(true);
                GUILayout.EndArea();
            }
            finally
            {
                thumbStyle.normal.background = oldThumb;
                thumbStyle.border = oldThumbBorder;
            }

            if (toggle != null) PortalClient.ToggleFavorite(toggle);
            else if (travel != null) PortalClient.TeleportTo(travel);
        }

        private static bool _wantUnfocus, _forceOpen;
        private static string _highlight;
        private static GUIStyle _distTagLeft;

        /// <summary>Search box: click to type, list filters as you type. Esc or x clears it.</summary>
        private static void SearchBox()
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, _searchStyle, GUILayout.ExpandWidth(true), GUILayout.Height(34));
            r.width -= 4;
            var ev = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == SearchControl;
            if (ev.type == EventType.Repaint)
            {
                GUI.DrawTexture(r, RowTexture((int)r.width, (int)r.height, focused ? "search-on" : "search", 0));
                GUI.DrawTexture(new Rect(r.x + 9, r.y + 8, 18, 18), _magnifier);
            }
            Rect clear = new Rect(r.xMax - 30, r.y + 4, 26, r.height - 8);
            bool hasText = !string.IsNullOrEmpty(_search);
            // the clear button must get the click before the text field
            if (hasText && ev.type == EventType.MouseDown && clear.Contains(ev.mousePosition))
            {
                _search = "";
                GUIUtility.keyboardControl = 0;
                ev.Use();
            }
            GUI.SetNextControlName(SearchControl);
            _search = GUI.TextField(new Rect(r.x + 32, r.y + 2, r.width - (hasText ? 66 : 36), r.height - 4), _search ?? "", 40, _searchStyle);
            if (ev.type == EventType.Repaint)
            {
                if (!focused && string.IsNullOrEmpty(_search))
                    _placeholder.Draw(new Rect(r.x + 32, r.y, r.width - 36, r.height), new GUIContent("Search...  (click to type)"), false, false, false, false);
                if (hasText) _clearStyle.Draw(clear, new GUIContent("x"), clear.Contains(ev.mousePosition), false, false, false);
            }
        }

        /// <summary>Show the matched part of a name in gold while searching.</summary>
        private static string Highlight(string name)
        {
            if (string.IsNullOrEmpty(_highlight)) return name;
            int i = name.IndexOf(_highlight, System.StringComparison.OrdinalIgnoreCase);
            if (i < 0) return name;
            return name.Substring(0, i) + "<color=#ffbe3c>" + name.Substring(i, _highlight.Length) + "</color>" + name.Substring(i + _highlight.Length);
        }

        /// <summary>"Back to: ..." row: the portal this character last travelled from.</summary>
        private static void BackRow(PortalInfo p, ref PortalInfo travel, ref PortalInfo toggle)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, _backText, GUILayout.ExpandWidth(true), GUILayout.Height(RowHeight));
            r.width -= 4;
            var ev = Event.current;
            if (ev.type == EventType.Repaint && r.Contains(ev.mousePosition)) _hovered = p;
            if (ev.type == EventType.MouseDown && ev.button == 1 && r.Contains(ev.mousePosition)) { toggle = p; ev.Use(); }
            // name drawn by the board itself (padding leaves room for icon + "Back to:"), label drawn separately
            if (Board(r, p.DisplayName, _backText, "back", 0)) travel = p;
            if (ev.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(r.x + 10, r.y + (r.height - 18f) / 2f, 18f, 18f), _backIcon);
                _backLabel.Draw(new Rect(r.x + 36, r.y, 90, r.height), new GUIContent("Back to:"), false, false, false, false);
                _hereTag.Draw(r, new GUIContent("previous portal"), false, false, false, false);
            }
        }

        /// <summary>Three sort buttons. Clicking the active one reverses the order.</summary>
        private static void SortBar()
        {
            string mode = Prefs.SortMode;
            bool rev = Prefs.SortReversed;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort:", _sortLabel, GUILayout.Height(SortHeight));
            SortButton("Biome", mode, rev ? "Biome  ME-DN" : "Biome  DN-ME");
            SortButton("Name", mode, rev ? "Name  Z-A" : "Name  A-Z");
            SortButton("Distance", mode, rev ? "Far first" : "Near first");
            GUILayout.EndHorizontal();
        }

        private static void SortButton(string key, string mode, string activeText)
        {
            bool active = mode == key || (key == "Biome" && mode != "Name" && mode != "Distance");
            string text = active ? activeText : key;
            Rect r = GUILayoutUtility.GetRect(new GUIContent(text), _sortText, GUILayout.Height(SortHeight), GUILayout.ExpandWidth(true));
            if (Board(r, text, _sortText, active ? "sort-on" : "sort", key.Length))
            {
                if (active) Prefs.SortReversed = !Prefs.SortReversed;
                else { Prefs.SortMode = key; Prefs.SortReversed = false; }
                _scroll = Vector2.zero;
            }
        }

        private static string DistanceText(PortalInfo p)
        {
            float d = Utils.DistanceXZ(p.Position, PortalClient.SourcePosition);
            return d < 1000f ? $"{Mathf.RoundToInt(d / 10f) * 10} m" : $"{d / 1000f:0.0} km";
        }

        /// <summary>Group with a clickable header: click to fold/unfold (remembered per character, on this PC).</summary>
        private static void Group(string key, string title, List<PortalInfo> items, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            bool collapsed = !_forceOpen && Prefs.IsCollapsed(key);
            Rect h = GUILayoutUtility.GetRect(new GUIContent(title), _headerButton, GUILayout.ExpandWidth(true));
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                var a = new Rect(h.x + 2, h.y + (h.height - 12f) / 2f, 12f, 12f);
                GUI.DrawTexture(a, collapsed ? _foldClosed : _foldOpen);
            }
            if (GUI.Button(h, title, _headerButton) && !_forceOpen) Prefs.SetCollapsed(key, !collapsed);

            if (!collapsed)
                foreach (var p in items) Row(p, index++, ref travel, ref toggle);
            else
                index += items.Count;
            GUILayout.Space(collapsed ? 2 : 6);
        }

        private static void Row(PortalInfo p, int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, _rowText, GUILayout.ExpandWidth(true), GUILayout.Height(RowHeight));
            r.width -= 4; // gap to the scrollbar
            var ev = Event.current;
            bool here = p.Id == PortalClient.SourceId;
            if (ev.type == EventType.Repaint && r.Contains(ev.mousePosition)) _hovered = p;
            Rect star = new Rect(r.x + 8, r.y + (r.height - StarSize) / 2f, StarSize, StarSize);

            // Right click anywhere on the row, or left click on the star = favorite toggle.
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition) &&
                (ev.button == 1 || (ev.button == 0 && star.Contains(ev.mousePosition))))
            {
                toggle = p;
                ev.Use();
            }

            if (here)
            {
                if (ev.type == EventType.Repaint)
                {
                    GUI.DrawTexture(r, RowTexture((int)r.width, (int)r.height, "here", index));
                    var arrow = new Rect(r.x + 12 + StarSize, r.y + (r.height - ArrowSize) / 2f, ArrowSize, ArrowSize);
                    GUI.DrawTexture(arrow, _arrow);
                    _hereText.Draw(r, new GUIContent(Highlight(p.DisplayName)), false, false, false, false);
                    _hereTag.Draw(r, new GUIContent("you are here"), false, false, false, false);
                }
                // swallow left clicks: you cannot travel to the portal you are standing in
                if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition)) ev.Use();
            }
            else
            {
                if (Board(r, Highlight(p.DisplayName), _rowText, "row", index)) travel = p;
                if (ev.type == EventType.Repaint && Prefs.SortMode == "Distance")
                    _distTag.Draw(r, new GUIContent(DistanceText(p)), false, false, false, false);
            }

            if (ev.type == EventType.Repaint)
                DrawStar(star, PortalClient.GetFav(p));
        }

        /// <summary>
        /// none = outline, personal = silver star, shared with everyone = 20% bigger gold star with a pulsing glow.
        /// </summary>
        private static void DrawStar(Rect star, PortalClient.FavState state)
        {
            switch (state)
            {
                case PortalClient.FavState.Personal:
                    GUI.DrawTexture(star, _starSilver, ScaleMode.ScaleToFit, true);
                    break;
                case PortalClient.FavState.Shared:
                {
                    Vector2 c = star.center;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 3f);
                    float g = StarSize * (1.9f + 0.35f * pulse);
                    var old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.55f + 0.45f * pulse);
                    GUI.DrawTexture(new Rect(c.x - g / 2f, c.y - g / 2f, g, g), _glow, ScaleMode.StretchToFill, true);
                    GUI.color = old;
                    float s = StarSize * 1.2f;
                    GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), _starOn, ScaleMode.ScaleToFit, true);
                    break;
                }
                default:
                    GUI.DrawTexture(star, _starOff, ScaleMode.ScaleToFit, true);
                    break;
            }
        }

        /// <summary>A clickable wooden board (button). Returns true when clicked with the left mouse button.</summary>
        private static bool Board(Rect r, string text, GUIStyle textStyle, string kind, int index)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                bool hover = r.Contains(ev.mousePosition);
                GUI.DrawTexture(r, RowTexture((int)r.width, (int)r.height, hover ? kind + "-hover" : kind, index));
            }
            return GUI.Button(r, text, textStyle);
        }

        // ---------- textures (generated once per size) ----------

        private static Texture2D PanelTexture(int w, int h)
        {
            if (_panelTex == null || _panelSize.x != w || _panelSize.y != h)
            {
                if (_panelTex != null) Object.Destroy(_panelTex);
                _panelTex = Art.Wood(w, h, 14f, PanelFill, PanelFrame, 7, 2.5f);
                _panelSize = new Vector2Int(w, h);
            }
            return _panelTex;
        }

        private static Texture2D RowTexture(int w, int h, string kind, int index)
        {
            int v = Mathf.Abs(index) % Variants;
            string key = $"{kind}|{w}|{h}|{v}";
            if (RowTex.TryGetValue(key, out var tex) && tex != null) return tex;
            if (RowTex.Count > 64) { foreach (var t in RowTex.Values) if (t != null) Object.Destroy(t); RowTex.Clear(); }

            Color fill = RowFill, frame = RowFrame;
            switch (kind)
            {
                case "row-hover": fill = HoverFill; frame = HoverFrame; break;
                case "here": fill = HereFill; frame = HereFrame; break;
                case "close": fill = CloseFill; frame = RowFrame; break;
                case "close-hover": fill = CloseHoverFill; frame = HoverFrame; break;
                case "sort": fill = RowFill; frame = RowFrame; break;
                case "search": fill = new Color32(30, 21, 15, 255); frame = RowFrame; break;
                case "search-on": fill = new Color32(30, 21, 15, 255); frame = new Color32(205, 145, 60, 255); break;
                case "back": fill = new Color32(42, 42, 30, 255); frame = new Color32(150, 135, 80, 255); break;
                case "back-hover": fill = new Color32(66, 64, 40, 255); frame = new Color32(185, 168, 98, 255); break;
                case "sort-hover": fill = HoverFill; frame = HoverFrame; break;
                case "sort-on": case "sort-on-hover": fill = CloseHoverFill; frame = HoverFrame; break;
            }
            tex = Art.Wood(w, h, 5f, fill, frame, 11 + v * 17, 1.6f);
            RowTex[key] = tex;
            return tex;
        }

        private static void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            Font font = FindValheimFont();

            _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = Orange;
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _hint.normal.textColor = new Color32(210, 195, 170, 255);
            _header = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            _header.normal.textColor = Orange;
            _header.margin = new RectOffset(2, 2, 4, 2);

            // Text-only styles: the wooden board underneath is drawn separately.
            _rowText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(16 + (int)StarSize, 10, 0, 0),
                margin = new RectOffset(0, 0, 2, 2),
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _rowText.normal.textColor = TextLight;
            _rowText.hover.textColor = Color.white;
            _rowText.active.textColor = Orange;
            _rowText.normal.background = _rowText.hover.background = _rowText.active.background = null;

            _hereText = new GUIStyle(_rowText) { padding = new RectOffset(20 + (int)StarSize + (int)ArrowSize, 10, 0, 0) };
            _hereText.normal.textColor = HereText;
            _hereTag = new GUIStyle(_rowText) { fontSize = 13, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 12, 0, 0) };
            _hereTag.normal.textColor = new Color32(230, 120, 100, 255);

            _closeText = new GUIStyle(_rowText) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 0, 0) };
            _sortText = new GUIStyle(_closeText) { fontSize = 15, padding = new RectOffset(6, 6, 0, 0), margin = new RectOffset(2, 2, 0, 0) };
            _sortLabel = new GUIStyle(_hint) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _distTag = new GUIStyle(_hereTag) { fontStyle = FontStyle.Normal, fontSize = 14 };
            _distTag.normal.textColor = new Color32(205, 185, 150, 255);

            _scrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = 12 };
            _scrollbar.normal.background = Art.Box(new Color32(36, 24, 16, 255), new Color32(24, 15, 9, 255), new Color32(36, 24, 16, 255), 1);
            _scrollbar.border = new RectOffset(2, 2, 2, 2);
            _thumb = Art.Box(new Color32(150, 107, 68, 255), new Color32(40, 26, 16, 255), new Color32(190, 140, 90, 255), 1);

            if (font != null)
                foreach (var s in new[] { _title, _hint, _header, _rowText, _hereText, _hereTag, _closeText, _sortText, _sortLabel, _distTag }) s.font = font;

            _starOn = Art.Star(true);
            _starOff = Art.Star(false);
            _starSilver = Art.SilverStar();
            _glow = Art.Glow();
            _arrow = Art.Arrow(new Color32(235, 60, 40, 255));
            _foldClosed = Art.Arrow(new Color32(255, 178, 64, 255));
            _foldOpen = Art.ArrowDown(new Color32(255, 178, 64, 255));
            _headerButton = new GUIStyle(_header) { padding = new RectOffset(18, 4, 2, 2) };

            _rowText.richText = true;
            _hereText.richText = true;
            _backLabel = new GUIStyle(_hint) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            _backLabel.normal.textColor = new Color32(220, 200, 150, 255);
            float labelW = _backLabel.CalcSize(new GUIContent("Back to:")).x;
            _backText = new GUIStyle(_rowText) { richText = false, padding = new RectOffset(36 + (int)labelW + 8, 120, 0, 0) };
            _searchStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 0, 0) };
            _searchStyle.normal.background = _searchStyle.hover.background = _searchStyle.focused.background = _searchStyle.active.background = null;
            _searchStyle.normal.textColor = _searchStyle.hover.textColor = _searchStyle.focused.textColor = _searchStyle.active.textColor = new Color32(255, 240, 210, 255);
            _searchStyle.font = _rowText.font;
            _placeholder = new GUIStyle(_hint) { fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _placeholder.normal.textColor = new Color32(150, 130, 105, 255);
            _clearStyle = new GUIStyle(_header) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            _clearStyle.normal.textColor = new Color32(220, 170, 120, 255);
            _clearStyle.hover.textColor = Color.white;
            _distTagLeft = new GUIStyle(_distTag) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(2, 2, 2, 4) };
            _magnifier = Art.Magnifier(new Color32(215, 195, 150, 255));
            _backIcon = Art.ArrowLeft(new Color32(240, 215, 140, 255));
            _headerButton.hover.textColor = new Color32(255, 214, 140, 255);
            _headerButton.active.textColor = Color.white;
            _headerButton.normal.background = _headerButton.hover.background = _headerButton.active.background = null;

            // Wide enough for a 25 character name (+ star, arrow, distance tag, padding, frame, scrollbar).
            float nameWidth = _rowText.CalcSize(new GUIContent(new string('M', NameChars))).x;
            _width = Mathf.Clamp(nameWidth + 150f, 480f, Screen.width * 0.45f);
        }

        private static Font FindValheimFont()
        {
            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<Font>();
                return fonts.FirstOrDefault(f => f.name.Contains("AveriaSerifLibre-Bold"))
                    ?? fonts.FirstOrDefault(f => f.name.Contains("AveriaSerif"))
                    ?? fonts.FirstOrDefault(f => f.name.Contains("Averia"))
                    ?? fonts.FirstOrDefault(f => f.name.Contains("Norse"));
            }
            catch { return null; }
        }
    }
}

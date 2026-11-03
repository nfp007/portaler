using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// The portal list shown on the left of the large map while choosing a destination.
    /// Wooden Valheim-style board. Default order: Back to, Players' last trips, Favorites, Last built, All portals (all movable).
    /// Left click a name = travel, right click (or click the star) = favorite on/off.
    /// The portal you stand in is greyed out ("you are here") and cannot be chosen.
    /// </summary>
    public static class PortalUI
    {
        public static bool MouseOverList { get; private set; }

        /// <summary>Right edge of the list panel on screen (pixels), 0 when closed.</summary>
        public static float PanelRight => PortalClient.Active ? _rect.xMax : 0f;

        /// <summary>Mouse is over the list, or a button was pressed on it and is still held (scrollbar drag).</summary>
        public static bool ListHasMouse => MouseOverList || _pressedInList;
        private static bool _pressedInList;

        // search box
        private const string SearchControl = "PortalerSearch";
        private static string _search = "";
        public static bool SearchFocused { get; private set; }
        private static GUIStyle _searchStyle, _placeholder, _clearStyle;
        private static GUIStyle _headerButtonH, _smallText, _smallLabel, _avatarText, _playerName, _glowStyle;
        private static Texture2D _disc, _check, _checkBox, _foldOpenLight;
        private static Texture2D _magnifier, _backIcon;

        private const int NameChars = 25;      // panel must fit names this long
        private const float StarSize = 22f;
        private const float ArrowSize = 16f;
        private const int RowHeight = 36;
        private const int Variants = 3;

        private static Vector2 _scroll;
        private static PortalInfo _hovered;

        /// <summary>The portal row the mouse rests on (for "show on map"); null when none.</summary>
        public static PortalInfo HoveredPortal { get; private set; }

        /// <summary>Game keys must be ignored (typing in the search box, category window open).</summary>
        public static bool BlocksKeys => SearchFocused || _picker != null || _playersOpen;

        // category filter (0 = all) and category window
        private static int _catFilter;
        private static PortalInfo _picker;
        private static Texture2D[] _catIcons;

        // drag and drop of favorites
        private static PortalInfo _drag;
        private static float _dragMouseY;
        private static readonly List<KeyValuePair<PortalInfo, Rect>> FavRects = new List<KeyValuePair<PortalInfo, Rect>>();
        private static readonly Dictionary<int, GUIStyle> RowStyles = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<int, GUIStyle> HereStyles = new Dictionary<int, GUIStyle>();
        private const float HandleW = 20f, CatSize = 20f;
        private static bool _filtering;
        private static Rect _rect;
        private static bool _stylesReady;
        private static GUIStyle _headerButton;
        private static Texture2D _foldOpen, _foldClosed;
        private static GUIStyle _title, _hint, _header, _rowText, _hereText, _hereTag, _closeText, _scrollbar, _distTag, _sortText, _sortLabel;
        private const int SortHeight = 30;
        private static Texture2D _starOn, _starOff, _starSilver, _glow, _arrow, _thumb;
        private static float _width;


        // Valheim-like colours
        // Palette sampled from Valheim's own panels (crafting, inventory, Tag dialog)
        private static readonly Color PanelFill = new Color32(104, 72, 49, 255);
        private static readonly Color PanelFrame = new Color32(140, 112, 80, 255);
        private static readonly Color RowFill = new Color32(80, 57, 41, 255);
        private static readonly Color RowFrame = new Color32(122, 95, 68, 255);
        private static readonly Color HoverFill = new Color32(112, 80, 54, 255);
        private static readonly Color HoverFrame = new Color32(160, 125, 86, 255);
        // "you are here": greyed out (you cannot travel there)
        private static readonly Color HereFrame = new Color32(108, 100, 90, 255);
        private static readonly Color HereFill = new Color32(74, 66, 58, 255);
        private static readonly Color CloseFill = new Color32(150, 96, 44, 255);
        private static readonly Color CloseHoverFill = new Color32(178, 116, 54, 255);
        private static readonly Color Orange = new Color32(250, 170, 70, 255);
        private static readonly Color TextLight = new Color32(250, 242, 225, 255);
        private static readonly Color HereText = new Color32(158, 150, 138, 255);
        // "Back to": golden, the newest one glows
        private static readonly Color Gold = new Color32(255, 200, 90, 255);

        public static void OnOpened()
        {
            _scroll = Vector2.zero;
            _search = "";
            _wantUnfocus = true;
            _picker = null;
            _drag = null;
            _dragGroup = null;
            _playersOpen = false;
            HoveredPortal = null;
        }

        public static void Draw()
        {
            if (!PortalClient.Active)
            {
                MouseOverList = false;
                _pressedInList = false;
                HoveredPortal = null;
                _picker = null;
                _drag = null;
                _dragGroup = null;
                _playersOpen = false;
                if (SearchFocused) { GUIUtility.keyboardControl = 0; SearchFocused = false; }
                // prepare the look while nobody waits for it, so the first map opening does not freeze
                if (!_warm && Player.m_localPlayer != null) Prewarm();
                return;
            }
            EnsureStyles();

            float top = 80f;
            _rect = new Rect(16f, top, _width, Mathf.Max(300f, Screen.height - top - 70f));
            var ev = Event.current;
            MouseOverList = _rect.Contains(ev.mousePosition);
            if (ev.type == EventType.MouseDown && MouseOverList) _pressedInList = true;
            if (ev.rawType == EventType.MouseUp) _pressedInList = false;

            // Category window: handled before the list so the list never sees these clicks.
            if (_picker != null) PickerInput(ev);
            else if (_playersOpen) PlayersInput(ev);

            // A drag that lost its mouse-up (button released outside the game window) ends here.
            if (ev.type == EventType.MouseMove) { _drag = null; _dragGroup = null; }

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

            // Rename / category key over a name (the hovered row comes from the last repaint).
            if (!typing && !SearchFocused && _picker == null && !_playersOpen && ev.type == EventType.KeyDown && _hovered != null && MouseOverList)
            {
                if (ev.keyCode == Plugin.RenameKey.Value) PortalClient.BeginRename(_hovered); // opened on the next Update
                else if (ev.keyCode == Plugin.CategoryKey.Value) { _picker = _hovered; ev.Use(); }
            }
            if (ev.type == EventType.Repaint) _hovered = null;

            if (ev.type == EventType.Repaint) DrawPanel(_rect);

            // Our own scrollbar thumb, restored afterwards so other mods' windows are untouched.
            var thumbStyle = GUI.skin.verticalScrollbarThumb;
            var oldThumb = thumbStyle.normal.background;
            var oldThumbBorder = thumbStyle.border;
            thumbStyle.normal.background = _thumb;
            thumbStyle.border = ThumbBorder;

            PortalInfo travel = null, toggle = null;
            try
            {
                GUILayout.BeginArea(new Rect(_rect.x + 22, _rect.y + 18, _rect.width - 44, _rect.height - 36));
                GUILayout.Label("Portaler", _title);
                GUILayout.Label(HintText(), _hint);
                GUILayout.Space(6);
                SortBar();
                GUILayout.Space(6);
                SearchBox();
                GUILayout.Space(6);
                FilterBar();
                GUILayout.Space(6);

                _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, _scrollbar);

                var m = GetModel(ev);
                bool filtering = m.Filtering;
                _filtering = filtering;
                _highlight = m.Search.Length > 0 ? m.Search : null;
                _forceOpen = filtering;
                int index = 0;

                if (filtering)
                {
                    GUILayout.Label(m.MatchLine, _distTagLeft);
                    PortalsBlock(m, ref index, ref travel, ref toggle);
                    if (m.All.Count == 0) GUILayout.Label("No portal matches.", _hint);
                }
                else
                {
                    // Back to, Players' last trips, Favorites, Last built and the portal list, in this character's order
                    MovableGroups(m, ref index, ref travel, ref toggle);
                }
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

            if (ev.type == EventType.Repaint)
            {
                HoveredPortal = MouseOverList && _drag == null && _dragGroup == null && _picker == null && !_playersOpen ? _hovered : null;
                if (_picker != null) DrawPicker(ev);
                if (_playersOpen) DrawPlayers(ev);
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
                DrawBoard(r, focused ? "search-on" : "search", 0);
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
                    _placeholder.Draw(new Rect(r.x + 32, r.y, r.width - 36, r.height), Tmp("Search...  (click to type)"), false, false, false, false);
                if (hasText) _clearStyle.Draw(clear, Tmp("x"), clear.Contains(ev.mousePosition), false, false, false);
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
            Rect r = GUILayoutUtility.GetRect(Tmp(text), _sortText, GUILayout.Height(SortHeight), GUILayout.ExpandWidth(true));
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

        // ---------- groups ----------

        /// <summary>Extra look of a row: board kind, a badge in the handle slot (back arrow, player initial) and a tag on the right.</summary>
        private class RowOpt
        {
            public string Kind = "row";
            public Texture2D Lead;
            public Color LeadTint = Color.white;
            public string LeadText;
            public string Tag;
            public float Inset;
            public bool Glow;
        }

        // drag and drop of whole groups (Favorites, Players' last trips, Last built)
        private static string _dragGroup;
        private static float _groupDragY;
        private static readonly List<KeyValuePair<string, Rect>> GroupRects = new List<KeyValuePair<string, Rect>>();
        private static readonly Dictionary<string, string> GroupTitles = new Dictionary<string, string>();

        /// <summary>
        /// Group title: optional drag handle, fold arrow, title (click = fold/unfold, remembered per character)
        /// and <paramref name="rightW"/> free pixels on the right for small controls. Returns true when folded.
        /// </summary>
        private static bool GroupHeader(string key, string title, bool movable, float rightW, out Rect h, out Rect right)
        {
            bool dragged = _dragGroup == key;
            bool collapsed = dragged || (!_forceOpen && Prefs.IsCollapsed(key));
            var style = movable ? _headerButtonH : _headerButton;
            h = GUILayoutUtility.GetRect(Tmp(title), style, GUILayout.ExpandWidth(true));
            right = new Rect(h.xMax - rightW - 6f, h.y, rightW, h.height);
            var ev = Event.current;
            var old = GUI.color;
            if (dragged) GUI.color = new Color(1f, 1f, 1f, 0.35f);

            float x = h.x;
            if (movable)
            {
                var grip = new Rect(h.x, h.y, 18f, h.height);
                if (ev.type == EventType.MouseDown && ev.button == 0 && _drag == null && grip.Contains(ev.mousePosition))
                {
                    _dragGroup = key;
                    _groupDragY = ev.mousePosition.y;
                    ev.Use();
                }
                if (ev.type == EventType.Repaint) DrawGrip(new Rect(h.x, h.y, 16f, h.height));
                x += 18f;
            }
            if (ev.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(x + 2f, h.y + (h.height - 12f) / 2f, 12f, 12f), collapsed ? _foldClosed : _foldOpen);
            var button = new Rect(h.x, h.y, Mathf.Max(40f, h.width - rightW - 8f), h.height);
            if (GUI.Button(button, title, style) && !_forceOpen && !dragged) Prefs.SetCollapsed(key, !collapsed);
            GUI.color = old;
            if (movable) GroupTitles[key] = title;
            return collapsed;
        }

        /// <summary>Three short bars: "grab here".</summary>
        private static void DrawGrip(Rect r)
        {
            var old = GUI.color;
            GUI.color = new Color(0.85f, 0.75f, 0.55f, old.a);
            for (int i = 0; i < 3; i++)
                GUI.DrawTexture(new Rect(r.x + 2f, r.y + r.height / 2f - 6f + i * 5f, 12f, 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Remember where a movable group was drawn (for dropping a dragged group).</summary>
        private static void EndMovable(string key, Rect h)
        {
            if (Event.current.type != EventType.Repaint) return;
            float bottom = Mathf.Max(h.yMax, GUILayoutUtility.GetLastRect().yMax);
            GroupRects.Add(new KeyValuePair<string, Rect>(key, new Rect(h.x, h.y, h.width, bottom - h.y)));
        }

        /// <summary>"show 1 2 3": small number buttons in a group title. Returns the chosen value.</summary>
        private static int CountSelector(Rect right, int value, int max)
        {
            const float box = 22f, gap = 3f;
            float x = right.xMax - max * (box + gap);
            if (Event.current.type == EventType.Repaint)
                _smallLabel.Draw(new Rect(x - 44f, right.y, 40f, right.height), Tmp("show"), false, false, false, false);
            for (int i = 1; i <= max; i++)
            {
                var b = new Rect(x + (i - 1) * (box + gap), right.y + (right.height - box) / 2f, box, box);
                if (Board(b, i.ToString(), _smallText, i == value ? "sort-on" : "sort", i)) value = i;
            }
            return value;
        }

        private static float CountSelectorWidth(int max) => 48f + max * 25f;

        /// <summary>"Back to": the last 1-3 portals this character travelled from. The newest one is golden and glows.</summary>
        private static void BackGroup(Model m, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            bool collapsed = GroupHeader("BACK", "Back to", true, CountSelectorWidth(Prefs.MaxBack), out var h, out var right);
            int n = CountSelector(right, Prefs.BackCount, Prefs.MaxBack);
            if (n != Prefs.BackCount) Prefs.BackCount = n;
            if (!collapsed)
                for (int i = 0; i < m.Backs.Count; i++) Row(m.Backs[i], index++, ref travel, ref toggle, false, m.BackOpts[i]);
            EndMovable("BACK", h);
            GUILayout.Space(collapsed ? 2 : 6);
        }

        /// <summary>
        /// The portal list itself (biome groups, or one list when sorted by name or distance) under one movable title.
        /// </summary>
        private static void PortalsBlock(Model m, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            const string key = "PORTALS";
            bool collapsed = GroupHeader(key, m.AllTitle, !_filtering, 0f, out var h, out _);
            if (!collapsed)
            {
                if (m.Biomes != null)
                    foreach (var g in m.Biomes) Group(g.Key, g.Title, g.Items, ref index, ref travel, ref toggle, 14f);
                else
                    foreach (var p in m.Sorted) Row(p, index++, ref travel, ref toggle);
                if (!_filtering && m.All.Count <= 1) GUILayout.Label("No other portals yet.", _hint);
            }
            else index += m.All.Count;
            EndMovable(key, h);
            GUILayout.Space(collapsed ? 2 : 6);
        }

        /// <summary>Back to, Favorites, Players' last trips, Last built and All portals, in this character's own order.</summary>
        private static void MovableGroups(Model m, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            var ev = Event.current;
            var order = m.Order;
            if (ev.type == EventType.Repaint) GroupRects.Clear();

            foreach (var key in order)
            {
                switch (key)
                {
                    case "BACK": if (m.Backs.Count > 0) BackGroup(m, ref index, ref travel, ref toggle); break;
                    case "PORTALS": PortalsBlock(m, ref index, ref travel, ref toggle); break;
                    case "FAV": if (m.Favs.Count > 0) FavGroup(m.Favs, m.FavTitle, ref index, ref travel, ref toggle); break;
                    case "TRIPS": if (m.Known.Count > 0) TripsGroup(m, ref index, ref travel, ref toggle); break;
                    case "LAST": if (m.Last.Count > 0) LastGroup(m.Last, ref index, ref travel, ref toggle); break;
                }
            }

            if (_dragGroup == null) return;
            if (ev.type == EventType.MouseDrag || ev.type == EventType.Repaint) _groupDragY = ev.mousePosition.y;
            if (ev.type == EventType.MouseDrag) ev.Use();

            var others = GroupRects.Where(kv => kv.Key != _dragGroup).ToList();
            int drop = others.Count(kv => kv.Value.y + Mathf.Min(kv.Value.height / 2f, 60f) < _groupDragY);

            if (ev.type == EventType.Repaint && GroupRects.Count > 0)
            {
                var first = GroupRects[0].Value;
                float lineY = others.Count == 0 ? first.y
                            : drop < others.Count ? others[drop].Value.y - 2f
                            : others[others.Count - 1].Value.yMax + 1f;
                var old = GUI.color;
                GUI.color = new Color(1f, 0.75f, 0.24f, 1f);
                GUI.DrawTexture(new Rect(first.x, lineY - 1.5f, first.width - 4f, 3f), Texture2D.whiteTexture);
                GUI.color = old;
                // the lifted title follows the mouse
                var lifted = new Rect(first.x + 12f, _groupDragY - 15f, first.width - 20f, 30f);
                DrawBoard(lifted, "row-hover", 1);
                DrawGrip(new Rect(lifted.x + 4f, lifted.y, 16f, lifted.height));
                GUI.DrawTexture(new Rect(lifted.x + 24f, lifted.y + (lifted.height - 12f) / 2f, 12f, 12f), _foldOpen);
                GroupTitles.TryGetValue(_dragGroup, out var t);
                _headerButtonH.Draw(new Rect(lifted.x + 6f, lifted.y, lifted.width - 6f, lifted.height), Tmp(t ?? ""), false, false, false, false);
                // where it lands, also when the drop line is scrolled out of sight
                string where = drop < others.Count ? "before " + ShortTitle(others[drop].Key) : "at the end";
                _distTag.Draw(lifted, Tmp("drop: " + where), false, false, false, false);
            }

            if (ev.rawType == EventType.MouseUp)
            {
                var full = order.Where(k => k != _dragGroup).ToList();
                int pos = drop < others.Count ? full.IndexOf(others[drop].Key)
                        : others.Count > 0 ? full.IndexOf(others[others.Count - 1].Key) + 1 : 0;
                full.Insert(Mathf.Clamp(pos, 0, full.Count), _dragGroup);
                Prefs.SetGroupOrder(full);
                _dragGroup = null;
                ev.Use();
            }
        }

        /// <summary>Other players' last trips: where each followed player last went through a portal.</summary>
        private static void TripsGroup(Model m, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            const string key = "TRIPS";
            bool collapsed = GroupHeader(key, "Players' last trips", true, m.PlayersButtonW, out var h, out var right);

            var b = new Rect(right.x, right.y + (right.height - 24f) / 2f, right.width, 24f);
            if (Board(b, m.PlayersButton, _smallText, "sort", 7)) _playersOpen = true;
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(b.xMax - 18f, b.y + 7f, 10f, 10f), _foldOpenLight);

            if (!collapsed)
            {
                if (m.Followed == 0) GUILayout.Label("You follow nobody. Click \"Players\" to choose.", _placeholder);
                else if (m.Trips.Count == 0) GUILayout.Label("No trips yet.", _placeholder);
                for (int i = 0; i < m.Trips.Count; i++) Row(m.Trips[i].Value, index++, ref travel, ref toggle, false, m.TripOpts[i]);
            }
            EndMovable(key, h);
            GUILayout.Space(collapsed ? 2 : 6);
        }

        /// <summary>"Last built" with "show 1-5" in its title.</summary>
        private static void LastGroup(List<PortalInfo> last, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            const string key = "LAST";
            bool collapsed = GroupHeader(key, "Last built", true, CountSelectorWidth(Prefs.MaxLastBuilt), out var h, out var right);
            int n = CountSelector(right, Prefs.LastBuiltCount, Prefs.MaxLastBuilt);
            if (n != Prefs.LastBuiltCount) Prefs.LastBuiltCount = n;
            if (!collapsed)
                foreach (var p in last) Row(p, index++, ref travel, ref toggle);
            else
                index += last.Count;
            EndMovable(key, h);
            GUILayout.Space(collapsed ? 2 : 6);
        }

        private static string ShortTitle(string key)
        {
            switch (key)
            {
                case "BACK": return "Back to";
                case "FAV": return "Favorites";
                case "TRIPS": return "Players' last trips";
                case "LAST": return "Last built";
                default: return "All portals";
            }
        }

        private static string Ago(float seconds)
        {
            if (seconds < 60f) return "just now";
            if (seconds < 3600f) return $"{Mathf.FloorToInt(seconds / 60f)} min ago";
            if (seconds < 86400f) return $"{Mathf.FloorToInt(seconds / 3600f)} h ago";
            return $"{Mathf.FloorToInt(seconds / 86400f)} d ago";
        }

        private static readonly Color32[] PlayerColors =
        {
            new Color32(150, 70, 150, 255), new Color32(60, 110, 175, 255), new Color32(70, 140, 80, 255), new Color32(175, 70, 60, 255),
            new Color32(45, 140, 140, 255), new Color32(190, 120, 40, 255), new Color32(180, 85, 130, 255), new Color32(125, 125, 50, 255),
        };

        /// <summary>Same colour for the same name on every PC.</summary>
        private static Color PlayerColor(string name)
        {
            int h = 0;
            foreach (char c in (name ?? "").ToLowerInvariant()) h = h * 31 + c;
            return PlayerColors[(h & 0x7fffffff) % PlayerColors.Length];
        }

        /// <summary>Plain group (biomes, all portals): clickable title folds/unfolds it.</summary>
        private static void Group(string key, string title, List<PortalInfo> items, ref int index, ref PortalInfo travel, ref PortalInfo toggle, float indent = 0f)
        {
            if (indent > 0f) { GUILayout.BeginHorizontal(); GUILayout.Space(indent); }
            bool collapsed = GroupHeader(key, title, false, 0f, out _, out _);
            if (indent > 0f) GUILayout.EndHorizontal();
            if (!collapsed)
                foreach (var p in items) Row(p, index++, ref travel, ref toggle);
            else
                index += items.Count;
            GUILayout.Space(collapsed ? 2 : 6);
        }

        /// <summary>Favorites group: like any group, but rows have a handle to drag them into a new order.</summary>
        private static void FavGroup(List<PortalInfo> items, string title, ref int index, ref PortalInfo travel, ref PortalInfo toggle)
        {
            const string key = "FAV";
            bool collapsed = GroupHeader(key, title, true, 0f, out var h, out _);
            var ev = Event.current;
            if (collapsed) { index += items.Count; EndMovable(key, h); GUILayout.Space(2); return; }

            if (ev.type == EventType.Repaint) FavRects.Clear();
            foreach (var p in items) Row(p, index++, ref travel, ref toggle, true);

            if (_drag != null)
            {
                if (ev.type == EventType.MouseDrag || ev.type == EventType.Repaint) _dragMouseY = ev.mousePosition.y;
                if (ev.type == EventType.MouseDrag) ev.Use();

                // where would it land? (rects come from the last repaint, dragged row left out)
                var others = FavRects.Where(kv => kv.Key.Id != _drag.Id).ToList();
                int drop = others.Count(kv => kv.Value.center.y < _dragMouseY);

                if (ev.type == EventType.Repaint && FavRects.Count > 0)
                {
                    float lineY = others.Count == 0 ? FavRects[0].Value.y
                                : drop < others.Count ? others[drop].Value.y - 2f
                                : others[others.Count - 1].Value.yMax + 1f;
                    var first = FavRects[0].Value;
                    var old = GUI.color;
                    GUI.color = new Color(1f, 0.75f, 0.24f, 1f);
                    GUI.DrawTexture(new Rect(first.x, lineY - 1.5f, first.width, 3f), Texture2D.whiteTexture);
                    GUI.color = old;
                    // the lifted row follows the mouse
                    var lifted = new Rect(first.x + 12f, _dragMouseY - RowHeight / 2f, first.width - 12f, RowHeight);
                    DrawRowVisual(lifted, _drag, "row-hover", 1, true, null);
                }

                if (ev.rawType == EventType.MouseUp)
                {
                    var order = items.Where(p => p.Id != _drag.Id).ToList();
                    order.Insert(Mathf.Clamp(drop, 0, order.Count), _drag);
                    PortalClient.SetFavoriteOrder(order);
                    _drag = null;
                    ev.Use();
                }
            }
            EndMovable(key, h);
            GUILayout.Space(6);
        }

        private static void Row(PortalInfo p, int index, ref PortalInfo travel, ref PortalInfo toggle, bool favRow = false, RowOpt opt = null)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, _rowText, GUILayout.ExpandWidth(true), GUILayout.Height(RowHeight));
            r.width -= 4; // gap to the scrollbar
            if (opt != null && opt.Inset > 0f) { r.x += opt.Inset; r.width -= opt.Inset * 2f; }
            var ev = Event.current;
            bool here = p.Id == PortalClient.SourceId;
            bool handle = favRow && !_filtering;
            bool slot = handle || opt?.Lead != null;
            if (favRow && ev.type == EventType.Repaint) FavRects.Add(new KeyValuePair<PortalInfo, Rect>(p, r));
            if (ev.type == EventType.Repaint && r.Contains(ev.mousePosition)) _hovered = p;

            float x = r.x + 8f + (slot ? HandleW : 0f);
            Rect star = new Rect(x, r.y + (r.height - StarSize) / 2f, StarSize, StarSize);

            // this row is being dragged: show only a faint ghost in its old place
            if (_drag != null && _drag.Id == p.Id && favRow)
            {
                if (ev.type == EventType.Repaint)
                {
                    var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    DrawRowVisual(r, p, here ? "here" : "row", index, handle, opt);
                    GUI.color = old;
                }
                return;
            }

            // grab the handle to start dragging
            if (handle && _dragGroup == null && ev.type == EventType.MouseDown && ev.button == 0 &&
                new Rect(r.x, r.y, 8f + HandleW, r.height).Contains(ev.mousePosition))
            {
                _drag = p;
                _dragMouseY = ev.mousePosition.y;
                ev.Use();
                return;
            }

            // Right click anywhere on the row, or left click on the star = favorite toggle.
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition) &&
                (ev.button == 1 || (ev.button == 0 && star.Contains(ev.mousePosition))))
            {
                toggle = p;
                ev.Use();
            }

            if (here)
            {
                if (ev.type == EventType.Repaint) DrawRowVisual(r, p, "here", index, handle, opt);
                // swallow left clicks: you cannot travel to the portal you are standing in
                if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition)) ev.Use();
            }
            else
            {
                if (opt != null && opt.Glow && ev.type == EventType.Repaint) DrawGlow(r);
                if (Board(r, Highlight(p.DisplayName), RowStyle(p, slot), opt?.Kind ?? "row", index)) travel = p;
                if (ev.type == EventType.Repaint)
                {
                    DrawRowDecor(r, p, handle, false, opt);
                    if (opt?.Tag != null)
                        _distTag.Draw(r, Tmp(opt.Tag), false, false, false, false);
                    else if (Prefs.SortMode == "Distance")
                        _distTag.Draw(r, Tmp(DistanceOf(p)), false, false, false, false);
                }
            }
        }

        /// <summary>Soft, slowly breathing golden glow around the newest "Back to" row.</summary>
        private static void DrawGlow(Rect r)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 2f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, (0.65f + 0.35f * pulse) * old.a);
            const float g = 8f;
            _glowStyle.Draw(new Rect(r.x - g, r.y - g, r.width + 2f * g, r.height + 2f * g), GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>Left offset of the name: handle or badge, star, category icon.</summary>
        private static int TextLeft(PortalInfo p, bool slot)
        {
            float left = 8f + (slot ? HandleW : 0f) + StarSize + 8f;
            if (p.Category > 0) left += CatSize + 4f;
            return (int)left;
        }

        private static GUIStyle RowStyle(PortalInfo p, bool slot)
        {
            int left = TextLeft(p, slot);
            if (!RowStyles.TryGetValue(left, out var st))
                RowStyles[left] = st = new GUIStyle(_rowText) { padding = new RectOffset(left, 10, 0, 0) };
            return st;
        }

        private static GUIStyle HereStyle(PortalInfo p, bool slot)
        {
            int left = TextLeft(p, slot);
            if (!HereStyles.TryGetValue(left, out var st))
                HereStyles[left] = st = new GUIStyle(_hereText) { padding = new RectOffset(left, 10, 0, 0) };
            return st;
        }

        /// <summary>Handle or badge, star and category icon on a row ("you are here": all faded).</summary>
        private static void DrawRowDecor(Rect r, PortalInfo p, bool handle, bool here, RowOpt opt)
        {
            float x = r.x + 8f;
            var old = GUI.color;
            if (here) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.4f);
            if (handle)
            {
                DrawGrip(new Rect(x, r.y, 16f, r.height));
                x += HandleW;
            }
            else if (opt?.Lead != null)
            {
                const float s = 20f;
                var lr = new Rect(x - 2f, r.y + (r.height - s) / 2f, s, s);
                var keep = GUI.color;
                GUI.color = new Color(opt.LeadTint.r, opt.LeadTint.g, opt.LeadTint.b, opt.LeadTint.a * keep.a);
                GUI.DrawTexture(lr, opt.Lead, ScaleMode.ScaleToFit, true);
                GUI.color = keep;
                if (!string.IsNullOrEmpty(opt.LeadText))
                    _avatarText.Draw(lr, Tmp(opt.LeadText), false, false, false, false);
                x += HandleW;
            }
            DrawStar(new Rect(x, r.y + (r.height - StarSize) / 2f, StarSize, StarSize), PortalClient.GetFav(p));
            x += StarSize + 6f;
            var icon = p.Category > 0 ? _catIcons[p.Category] : null;
            if (icon != null)
                GUI.DrawTexture(new Rect(x, r.y + (r.height - CatSize) / 2f, CatSize, CatSize), icon, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        /// <summary>Draws a complete row without any input handling (ghost, lifted row, "you are here").</summary>
        private static void DrawRowVisual(Rect r, PortalInfo p, string kind, int index, bool handle, RowOpt opt)
        {
            bool here = kind == "here";
            bool slot = handle || opt?.Lead != null;
            DrawBoard(r, kind, index);
            DrawRowDecor(r, p, handle, here, opt);
            var st = here ? HereStyle(p, slot) : RowStyle(p, slot);
            st.Draw(r, Tmp(Highlight(p.DisplayName)), false, false, false, false);
            if (here) _hereTag.Draw(r, Tmp("you are here"), false, false, false, false);
            else if (opt?.Tag != null) _distTag.Draw(r, Tmp(opt.Tag), false, false, false, false);
        }

        // ---------- players window ("Players: n of m" in the trips title) ----------

        private static bool _playersOpen;
        private const float PlayerRowH = 40f;

        private static Rect PlayersRect(int n)
        {
            float w = _rect.width - 40f;
            float h = 70f + Mathf.Max(1, n) * (PlayerRowH + 6f) + 56f;
            h = Mathf.Min(h, _rect.height - 170f);
            return new Rect(_rect.x + 20f, _rect.y + 150f, w, h);
        }

        private static Rect PlayerRow(Rect pr, int i) => new Rect(pr.x + 12f, pr.y + 66f + i * (PlayerRowH + 6f), pr.width - 24f, PlayerRowH);
        private static Rect PlayersDone(Rect pr) => new Rect(pr.x + 12f, pr.yMax - 50f, pr.width - 24f, 38f);

        private static void PlayersInput(Event ev)
        {
            var known = _model != null ? _model.Known : PortalClient.KnownPlayers();
            var pr = PlayersRect(known.Count);
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape) { _playersOpen = false; ev.Use(); return; }
            if (ev.type == EventType.MouseDown)
            {
                if (PlayersDone(pr).Contains(ev.mousePosition) || !pr.Contains(ev.mousePosition)) { _playersOpen = false; ev.Use(); return; }
                for (int i = 0; i < known.Count; i++)
                {
                    var row = PlayerRow(pr, i);
                    if (row.yMax > PlayersDone(pr).y - 4f) break;
                    if (!row.Contains(ev.mousePosition)) continue;
                    // first change: start from "everybody" so only this one name flips
                    var set = new HashSet<string>(known.Where(k => PortalClient.IsTracked(k.Key)).Select(k => k.Key), System.StringComparer.OrdinalIgnoreCase);
                    if (!set.Remove(known[i].Key)) set.Add(known[i].Key);
                    // keep names that are not listed right now (offline, no trip yet)
                    var stored = Prefs.GetTracked();
                    if (stored != null)
                        foreach (var n in stored) if (!known.Any(k => string.Equals(k.Key, n, System.StringComparison.OrdinalIgnoreCase))) set.Add(n);
                    Prefs.SetTracked(set);
                    break;
                }
                ev.Use();
                return;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        private static void DrawPlayers(Event ev)
        {
            var known = _model != null ? _model.Known : PortalClient.KnownPlayers();
            var pr = PlayersRect(known.Count);
            DrawBoard(pr, "picker", 0);
            _pickerTitle.Draw(new Rect(pr.x + 16f, pr.y + 10f, pr.width - 32f, 26f), Tmp("Show trips of"), false, false, false, false);
            _placeholder.Draw(new Rect(pr.x + 16f, pr.y + 34f, pr.width - 32f, 22f), Tmp("Pick the players you want to follow. Only for you."), false, false, false, false);
            var done = PlayersDone(pr);
            for (int i = 0; i < known.Count; i++)
            {
                var row = PlayerRow(pr, i);
                if (row.yMax > done.y - 4f) break;
                bool on = PortalClient.IsTracked(known[i].Key);
                bool hover = row.Contains(ev.mousePosition);
                DrawBoard(row, hover ? "sort-hover" : "sort", i);
                // check box
                var cb = new Rect(row.x + 10f, row.y + (row.height - 22f) / 2f, 22f, 22f);
                GUI.DrawTexture(cb, _checkBox);
                if (on) GUI.DrawTexture(new Rect(cb.x - 1f, cb.y - 3f, 26f, 26f), _check);
                // initial badge
                var badge = new Rect(cb.xMax + 10f, row.y + (row.height - 24f) / 2f, 24f, 24f);
                var old = GUI.color;
                GUI.color = PlayerColor(known[i].Key);
                GUI.DrawTexture(badge, _disc);
                GUI.color = old;
                _avatarText.Draw(badge, Tmp(known[i].Key.Substring(0, 1).ToUpperInvariant()), false, false, false, false);
                _playerName.Draw(new Rect(badge.xMax + 10f, row.y, row.width - 160f, row.height), Tmp(known[i].Key), false, false, false, false);
                // online dot
                bool online = known[i].Value;
                _distTag.Draw(row, Tmp(online ? "online" : "offline"), false, false, false, false);
                float tw = _distTag.CalcSize(Tmp(online ? "online" : "offline")).x;
                GUI.color = online ? new Color32(110, 210, 90, 255) : new Color32(130, 120, 110, 255);
                GUI.DrawTexture(new Rect(row.xMax - tw - 14f, row.center.y - 4f, 9f, 9f), _disc);
                GUI.color = old;
            }
            if (known.Count == 0)
                _placeholder.Draw(new Rect(pr.x + 16f, pr.y + 70f, pr.width - 32f, 30f), Tmp("Nobody else has played here yet."), false, false, false, false);
            DrawBoard(done, done.Contains(ev.mousePosition) ? "close-hover" : "close", 0);
            _closeText.Draw(done, Tmp("Done"), false, false, false, false);
        }

        // ---------- category filter ----------

        private static void FilterBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Show:", _sortLabel, GUILayout.Height(SortHeight));
            for (int c = 0; c < Categories.Count; c++)
            {
                bool on = _catFilter == c;
                Rect r = GUILayoutUtility.GetRect(GUIContent.none, _sortText, GUILayout.Height(SortHeight), GUILayout.ExpandWidth(true));
                if (Board(r, c == 0 ? "All" : "", _sortText, on ? "sort-on" : "sort", c))
                {
                    _catFilter = on || c == 0 ? 0 : c; // click the active one again = show all
                    _scroll = Vector2.zero;
                }
                if (c > 0 && Event.current.type == EventType.Repaint)
                {
                    float s = Mathf.Min(20f, r.height - 8f);
                    GUI.DrawTexture(new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s), _catIcons[c], ScaleMode.ScaleToFit, true);
                }
            }
            GUILayout.EndHorizontal();
        }

        // ---------- category window (key T over a name) ----------

        private static Rect PickerRect()
        {
            float w = _rect.width - 40f;
            float tileH = 84f;
            float h = 70f + 3 * (tileH + 8f) + 10f;
            return new Rect(_rect.x + 20f, _rect.y + 150f, w, h);
        }

        private static Rect PickerTile(Rect pr, int i)
        {
            // 3 x 3 tiles: categories 1..8, then "None" last
            int slot = i == 0 ? 8 : i - 1;
            float tw = (pr.width - 24f - 2 * 8f) / 3f, th = 84f;
            return new Rect(pr.x + 12f + (slot % 3) * (tw + 8f), pr.y + 62f + (slot / 3) * (th + 8f), tw, th);
        }

        private static void PickerInput(Event ev)
        {
            var pr = PickerRect();
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Escape || ev.keyCode == Plugin.CategoryKey.Value))
            {
                _picker = null; ev.Use(); return;
            }
            if (ev.type == EventType.MouseDown)
            {
                for (int c = 0; c < Categories.Count; c++)
                {
                    if (PickerTile(pr, c).Contains(ev.mousePosition))
                    {
                        PortalClient.SetCategory(_picker, c);
                        _picker = null; ev.Use(); return;
                    }
                }
                if (!pr.Contains(ev.mousePosition)) _picker = null; // click outside closes it
                ev.Use();
                return;
            }
            if (ev.isMouse || ev.type == EventType.ScrollWheel) ev.Use();
        }

        private static void DrawPicker(Event ev)
        {
            var pr = PickerRect();
            DrawBoard(pr, "picker", 0);
            _pickerTitle.Draw(new Rect(pr.x + 16f, pr.y + 10f, pr.width - 32f, 26f), Tmp($"Category for \"{_picker.DisplayName}\""), false, false, false, false);
            _placeholder.Draw(new Rect(pr.x + 16f, pr.y + 34f, pr.width - 32f, 22f), Tmp("Same for everyone on the server. Esc closes."), false, false, false, false);
            for (int c = 0; c < Categories.Count; c++)
            {
                var t = PickerTile(pr, c);
                bool on = _picker.Category == c;
                bool hover = t.Contains(ev.mousePosition);
                DrawBoard(t, on ? "sort-on" : hover ? "sort-hover" : "sort", c);
                if (c > 0) GUI.DrawTexture(new Rect(t.center.x - 17f, t.y + 10f, 34f, 34f), _catIcons[c], ScaleMode.ScaleToFit, true);
                else _pickerX.Draw(new Rect(t.x, t.y + 6f, t.width, 40f), Tmp("x"), false, false, false, false);
                _sortText.Draw(new Rect(t.x, t.y + 48f, t.width, 28f), Tmp(Categories.Names[c]), false, false, false, false);
            }
        }

        private static GUIStyle _pickerTitle, _pickerX;

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
                DrawBoard(r, hover ? HoverKind(kind) : kind, index);
            }
            return GUI.Button(r, text, textStyle);
        }

        // ---------- textures (generated once per size) ----------

        // ---------- what the list shows (built once, reused until something changes) ----------

        private sealed class BiomeGroup { public string Key, Title; public List<PortalInfo> Items; }

        private sealed class Model
        {
            public string Search = "";
            public bool Filtering;
            public List<PortalInfo> All, Favs, Sorted, Backs, Last;
            public List<BiomeGroup> Biomes;
            public List<KeyValuePair<string, bool>> Known;
            public List<KeyValuePair<PortalClient.TripInfo, PortalInfo>> Trips;
            public List<RowOpt> BackOpts, TripOpts;
            public List<string> Order;
            public Dictionary<ZDOID, string> Dist;
            public string MatchLine, AllTitle, FavTitle, PlayersButton;
            public float PlayersButtonW;
            public int Followed;
        }

        private static Model _model;
        private static string _mSearch;
        private static int _mCat = -1, _mData = -1, _mPrefs = -1;
        private static string _mSort;
        private static bool _mRev;
        private static ZDOID _mSource;
        private static float _mTime;
        private const float ModelMaxAge = 0.5f; // "x min ago", online dots and distances refresh this often

        /// <summary>
        /// The sorted, grouped and filtered lists for this frame. Rebuilt only at the start of a layout pass
        /// (so layout and repaint always see the same rows) and only when something changed, or every half second.
        /// </summary>
        private static Model GetModel(Event ev)
        {
            string q = (_search ?? "").Trim();
            bool changed = _model == null || q != _mSearch || _catFilter != _mCat || Prefs.SortMode != _mSort || Prefs.SortReversed != _mRev
                           || PortalClient.DataVersion != _mData || Prefs.Version != _mPrefs || PortalClient.SourceId != _mSource
                           || Time.unscaledTime - _mTime > ModelMaxAge;
            if (_model != null && (!changed || ev.type != EventType.Layout)) return _model;

            _mSearch = q; _mCat = _catFilter; _mSort = Prefs.SortMode; _mRev = Prefs.SortReversed;
            _mData = PortalClient.DataVersion; _mPrefs = Prefs.Version; _mSource = PortalClient.SourceId; _mTime = Time.unscaledTime;

            var m = new Model { Search = q };
            var everything = PortalClient.Portals;
            m.Filtering = q.Length > 0 || _catFilter > 0;
            if (m.Filtering)
            {
                m.All = new List<PortalInfo>();
                foreach (var p in everything)
                    if ((q.Length == 0 || p.DisplayName.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        && (_catFilter == 0 || p.Category == _catFilter)) m.All.Add(p);
                string what = q.Length > 0 && _catFilter > 0 ? $"\"{q}\" + {Categories.Names[_catFilter]}"
                            : q.Length > 0 ? $"\"{q}\"" : Categories.Names[_catFilter];
                m.MatchLine = $"{m.All.Count} of {everything.Count} portals match {what}";
            }
            else m.All = everything;
            m.AllTitle = $"All portals  ({m.All.Count})";

            bool rev = Prefs.SortReversed;
            var byName = System.StringComparer.OrdinalIgnoreCase;
            Vector3 here = PortalClient.SourcePosition;
            switch (Prefs.SortMode)
            {
                case "Name":
                    m.Sorted = rev ? m.All.OrderByDescending(p => p.DisplayName, byName).ToList() : m.All.OrderBy(p => p.DisplayName, byName).ToList();
                    break;
                case "Distance":
                    m.Sorted = rev ? m.All.OrderByDescending(p => Utils.DistanceXZ(p.Position, here)).ToList()
                                   : m.All.OrderBy(p => Utils.DistanceXZ(p.Position, here)).ToList();
                    m.Dist = new Dictionary<ZDOID, string>();
                    foreach (var p in m.All) m.Dist[p.Id] = DistanceText(p);
                    break;
                default:
                    // Deep North first ... Meadows last ("Other" always at the end). Reversed: Meadows first.
                    m.Biomes = new List<BiomeGroup>();
                    foreach (var g in m.All.GroupBy(p => p.BiomeCode)
                                 .OrderBy(g => g.Key == "XX" ? 1 : 0)
                                 .ThenBy(g => rev ? Biomes.SortIndex(g.Key) : -Biomes.SortIndex(g.Key)))
                    {
                        var entry = Biomes.Order.FirstOrDefault(b => b.Code == g.Key);
                        string title = string.IsNullOrEmpty(entry.Display) ? g.Key : entry.Display;
                        var items = g.OrderBy(p => p.DisplayName, byName).ToList();
                        m.Biomes.Add(new BiomeGroup { Key = g.Key, Title = $"{title}  ({items.Count})", Items = items });
                    }
                    break;
            }

            m.Favs = PortalClient.OrderedFavorites(m.All);
            m.FavTitle = $"Favorites  ({m.Favs.Count})";
            m.Order = Prefs.GetGroupOrder();
            m.Last = PortalClient.LastBuilt;

            m.Backs = PortalClient.BackPortals;
            m.BackOpts = new List<RowOpt>();
            for (int i = 0; i < m.Backs.Count; i++)
                m.BackOpts.Add(new RowOpt
                {
                    Kind = i == 0 ? "back" : "back2",
                    Lead = _backIcon,
                    LeadTint = i == 0 ? Color.white : new Color(1f, 1f, 1f, 0.7f),
                    Tag = i == 0 ? "previous portal" : $"{i + 1} trips ago",
                    Inset = 4f,
                    Glow = i == 0,
                });

            m.Known = PortalClient.KnownPlayers();
            m.Followed = m.Known.Count(k => PortalClient.IsTracked(k.Key));
            m.PlayersButton = $"Players: {m.Followed} of {m.Known.Count}";
            m.PlayersButtonW = _smallText.CalcSize(Tmp(m.PlayersButton)).x + 34f;
            m.PlayersButton += "     "; // room for the arrow
            m.Trips = PortalClient.TrackedTrips();
            m.TripOpts = new List<RowOpt>();
            foreach (var kv in m.Trips)
            {
                string name = kv.Key.Name ?? "?";
                m.TripOpts.Add(new RowOpt
                {
                    Lead = _disc,
                    LeadTint = PlayerColor(name),
                    LeadText = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?",
                    Tag = $"{name}, {Ago(kv.Key.AgeSeconds)}",
                });
            }
            _model = m;
            return m;
        }

        private static string DistanceOf(PortalInfo p) =>
            _model?.Dist != null && _model.Dist.TryGetValue(p.Id, out var d) ? d : DistanceText(p);

        // ---------- small helpers that avoid garbage every frame ----------

        private static readonly GUIContent TmpContent = new GUIContent();
        /// <summary>One reusable GUIContent for drawing and measuring (used right away, never kept).</summary>
        private static GUIContent Tmp(string text) { TmpContent.text = text; TmpContent.image = null; TmpContent.tooltip = null; return TmpContent; }

        private static readonly RectOffset ThumbBorder = new RectOffset(2, 2, 2, 2);
        private static string _hintText;
        private static KeyCode _hintRename, _hintCategory;

        private static string HintText()
        {
            if (_hintText == null || _hintRename != Plugin.RenameKey.Value || _hintCategory != Plugin.CategoryKey.Value)
            {
                _hintRename = Plugin.RenameKey.Value; _hintCategory = Plugin.CategoryKey.Value;
                _hintText = $"Click a name or a map pin to travel. {_hintRename} = rename, {_hintCategory} = category.\nRight click = favorite. Drag favorites and group titles by their handle.";
            }
            return _hintText;
        }

        private static readonly Dictionary<string, string> HoverKinds = new Dictionary<string, string>();
        private static string HoverKind(string kind)
        {
            if (!HoverKinds.TryGetValue(kind, out var h)) HoverKinds[kind] = h = kind + "-hover";
            return h;
        }

        // ---------- textures: made on a background thread, kept until unused for a while ----------

        private struct TexKey : System.IEquatable<TexKey>
        {
            public string Kind; public int W, H, V;
            public bool Equals(TexKey o) => W == o.W && H == o.H && V == o.V && string.Equals(Kind, o.Kind);
            public override bool Equals(object o) => o is TexKey k && Equals(k);
            public override int GetHashCode() => ((Kind?.GetHashCode() ?? 0) * 397 ^ W * 31 ^ H * 17 ^ V);
        }

        private sealed class TexEntry
        {
            public Texture2D Tex;
            public System.Threading.Tasks.Task<Color32[]> Job;
            public int W, H, LastUsed;
        }

        private static readonly Dictionary<TexKey, TexEntry> RowTex = new Dictionary<TexKey, TexEntry>();
        private const int MaxTextures = 300;          // plenty for every board size in use; the oldest unused go first
        private const int SyncPixels = 6000;          // small boards (buttons) are made right away
        private static TexEntry _panel;
        private static int _panelW, _panelH;

        private static void KindColours(string kind, out Color fill, out Color frame)
        {
            fill = RowFill; frame = RowFrame;
            switch (kind)
            {
                case "row-hover": fill = HoverFill; frame = HoverFrame; break;
                case "here": fill = HereFill; frame = HereFrame; break;
                case "close": fill = CloseFill; frame = RowFrame; break;
                case "close-hover": fill = CloseHoverFill; frame = HoverFrame; break;
                case "sort": fill = RowFill; frame = RowFrame; break;
                case "search": fill = new Color32(66, 48, 35, 255); frame = RowFrame; break;
                case "search-on": fill = new Color32(66, 48, 35, 255); frame = new Color32(215, 160, 80, 255); break;
                case "back": fill = new Color32(118, 90, 46, 255); frame = new Color32(232, 188, 92, 255); break;
                case "back-hover": fill = new Color32(140, 108, 56, 255); frame = new Color32(255, 214, 125, 255); break;
                case "back2": fill = new Color32(92, 72, 46, 255); frame = new Color32(176, 146, 92, 255); break;
                case "back2-hover": fill = new Color32(114, 88, 56, 255); frame = new Color32(205, 172, 110, 255); break;
                case "sort-hover": fill = HoverFill; frame = HoverFrame; break;
                case "picker": fill = new Color32(96, 66, 45, 255); frame = PanelFrame; break;
                case "sort-on": case "sort-on-hover": fill = CloseHoverFill; frame = HoverFrame; break;
            }
        }

        /// <summary>The finished texture, or null while it is still being made.</summary>
        private static Texture2D Finish(TexEntry e)
        {
            e.LastUsed = Time.frameCount;
            if (e.Tex == null && e.Job != null && e.Job.IsCompleted)
            {
                if (e.Job.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) e.Tex = Art.ToTexture(e.Job.Result, e.W, e.H);
                else Plugin.Log.LogWarning("Board texture failed: " + e.Job.Exception?.GetBaseException().Message);
                e.Job = null;
            }
            return e.Tex;
        }

        private static TexEntry Request(int w, int h, float frameW, Color fill, Color frame, int seed, float rough)
        {
            w = Mathf.Max(8, w); h = Mathf.Max(8, h);
            var e = new TexEntry { W = w, H = h, LastUsed = Time.frameCount };
            if (w * h <= SyncPixels) e.Tex = Art.ToTexture(Art.WoodPixels(w, h, frameW, fill, frame, seed, rough), w, h);
            else e.Job = System.Threading.Tasks.Task.Run(() => Art.WoodPixels(w, h, frameW, fill, frame, seed, rough));
            return e;
        }

        private static Texture2D RowTexture(int w, int h, string kind, int index)
        {
            int v = Mathf.Abs(index) % Variants;
            var key = new TexKey { Kind = kind, W = w, H = h, V = v };
            if (!RowTex.TryGetValue(key, out var e))
            {
                if (RowTex.Count >= MaxTextures) EvictOldest();
                KindColours(kind, out var fill, out var frame);
                RowTex[key] = e = Request(w, h, 5f, fill, frame, 11 + v * 17, 1.6f);
            }
            return Finish(e);
        }

        private static void EvictOldest()
        {
            TexKey oldest = default; int best = int.MaxValue; bool found = false;
            foreach (var kv in RowTex)
                if (kv.Value.Job == null && kv.Value.LastUsed < best) { best = kv.Value.LastUsed; oldest = kv.Key; found = true; }
            if (!found) return;
            if (RowTex[oldest].Tex != null) Object.Destroy(RowTex[oldest].Tex);
            RowTex.Remove(oldest);
        }

        /// <summary>Wooden board; a plain board in the same colours for the frame or two its texture is still being made.</summary>
        private static void DrawBoard(Rect r, string kind, int index)
        {
            var tex = RowTexture((int)r.width, (int)r.height, kind, index);
            if (tex != null) { GUI.DrawTexture(r, tex); return; }
            KindColours(kind, out var fill, out var frame);
            DrawPlain(r, fill, frame, 2f);
        }

        private static void DrawPlain(Rect r, Color fill, Color frame, float border)
        {
            var old = GUI.color;
            GUI.color = new Color(frame.r, frame.g, frame.b, old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(fill.r, fill.g, fill.b, old.a);
            GUI.DrawTexture(new Rect(r.x + border, r.y + border, r.width - 2 * border, r.height - 2 * border), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void RequestPanel(int w, int h)
        {
            if (_panel != null && _panelW == w && _panelH == h) return;
            if (_panel?.Tex != null) Object.Destroy(_panel.Tex);
            _panelW = w; _panelH = h;
            _panel = Request(w, h, 14f, PanelFill, PanelFrame, 7, 2.5f);
        }

        private static void DrawPanel(Rect r)
        {
            RequestPanel((int)r.width, (int)r.height);
            var tex = Finish(_panel);
            if (tex != null) GUI.DrawTexture(r, tex);
            else DrawPlain(r, PanelFill, PanelFrame, 6f);
        }

        private static bool _warm;

        /// <summary>
        /// Right after the character spawns: fonts, styles and the big list panel (made on a background thread),
        /// so the first time the map opens at a portal is as quick as every other time.
        /// </summary>
        private static void Prewarm()
        {
            _warm = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EnsureStyles();
            RequestPanel((int)_width, (int)Mathf.Max(300f, Screen.height - 80f - 70f));
            try { FlowLine.Prewarm(Minimap.instance); } catch (System.Exception e) { Plugin.Log.LogWarning("Line prewarm: " + e.Message); }
            if (Perf.On) Plugin.Log.LogInfo($"[Perf] Prewarm after spawn: {sw.Elapsed.TotalMilliseconds:0} ms (panel texture continues in the background)");
        }

        private static void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            Font font = FindValheimFont();

            _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = Orange;
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _hint.normal.textColor = new Color32(235, 222, 200, 255);
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

            _hereText = new GUIStyle(_rowText) { padding = new RectOffset(16 + (int)StarSize, 10, 0, 0) };
            _hereText.normal.textColor = HereText;
            _hereTag = new GUIStyle(_rowText) { fontSize = 13, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 12, 0, 0) };
            _hereTag.normal.textColor = new Color32(150, 142, 130, 255);

            _closeText = new GUIStyle(_rowText) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 0, 0) };
            _sortText = new GUIStyle(_closeText) { fontSize = 15, padding = new RectOffset(6, 6, 0, 0), margin = new RectOffset(2, 2, 0, 0) };
            _sortLabel = new GUIStyle(_hint) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _distTag = new GUIStyle(_hereTag) { fontStyle = FontStyle.Normal, fontSize = 14 };
            _distTag.normal.textColor = new Color32(225, 208, 175, 255);

            _scrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = 12 };
            _scrollbar.normal.background = Art.Box(new Color32(70, 50, 35, 255), new Color32(50, 35, 24, 255), new Color32(70, 50, 35, 255), 1);
            _scrollbar.border = new RectOffset(2, 2, 2, 2);
            _thumb = Art.Box(new Color32(160, 125, 86, 255), new Color32(60, 42, 28, 255), new Color32(200, 160, 110, 255), 1);

            if (font != null)
                foreach (var s in new[] { _title, _hint, _header, _rowText, _hereText, _hereTag, _closeText, _sortText, _sortLabel, _distTag }) s.font = font;

            _starOn = Art.Star(true);
            _starOff = Art.Star(false);
            _starSilver = Art.SilverStar();
            _glow = Art.Glow();
            _arrow = Art.Arrow(new Color32(235, 60, 40, 255));
            _foldClosed = Art.Arrow(new Color32(250, 170, 70, 255));
            _foldOpen = Art.ArrowDown(new Color32(250, 170, 70, 255));
            _headerButton = new GUIStyle(_header) { padding = new RectOffset(18, 4, 2, 2) };

            _rowText.richText = true;
            _hereText.richText = true;
            _searchStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 0, 0) };
            _searchStyle.normal.background = _searchStyle.hover.background = _searchStyle.focused.background = _searchStyle.active.background = null;
            _searchStyle.normal.textColor = _searchStyle.hover.textColor = _searchStyle.focused.textColor = _searchStyle.active.textColor = new Color32(255, 240, 210, 255);
            _searchStyle.font = _rowText.font;
            _placeholder = new GUIStyle(_hint) { fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _placeholder.normal.textColor = new Color32(190, 170, 140, 255);
            _clearStyle = new GUIStyle(_header) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            _clearStyle.normal.textColor = new Color32(220, 170, 120, 255);
            _clearStyle.hover.textColor = Color.white;
            _distTagLeft = new GUIStyle(_distTag) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(2, 2, 2, 4) };
            _magnifier = Art.Magnifier(new Color32(215, 195, 150, 255));
            _pickerTitle = new GUIStyle(_header) { wordWrap = false, clipping = TextClipping.Clip };
            _pickerX = new GUIStyle(_header) { alignment = TextAnchor.MiddleCenter, fontSize = 30 };
            _pickerX.normal.textColor = new Color32(225, 205, 160, 255);
            _catIcons = new Texture2D[Categories.Count];
            for (int c = 1; c < Categories.Count; c++) _catIcons[c] = Art.CategoryIcon(c);
            _backIcon = Art.ArrowLeft(new Color32(255, 215, 120, 255));
            _headerButton.hover.textColor = new Color32(255, 214, 140, 255);
            _headerButton.active.textColor = Color.white;
            _headerButton.normal.background = _headerButton.hover.background = _headerButton.active.background = null;
            _headerButtonH = new GUIStyle(_headerButton) { padding = new RectOffset(36, 4, 2, 2) };

            _smallText = new GUIStyle(_sortText) { fontSize = 14, padding = new RectOffset(4, 4, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            _smallLabel = new GUIStyle(_placeholder) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            _avatarText = new GUIStyle(_rowText) { fontSize = 13, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), richText = false };
            _avatarText.normal.textColor = Color.white;
            _playerName = new GUIStyle(_rowText) { padding = new RectOffset(0, 0, 0, 0), richText = false };
            _disc = Art.Disc();
            _check = Art.Check(Gold);
            _checkBox = Art.Box(new Color32(50, 36, 26, 255), new Color32(30, 21, 15, 255), new Color32(170, 140, 100, 255), 2);
            _foldOpenLight = Art.ArrowDown(new Color32(240, 220, 180, 255));
            _glowStyle = new GUIStyle { border = new RectOffset(8, 8, 8, 8) };
            _glowStyle.normal.background = Art.RectGlow(new Color(1f, 0.78f, 0.3f, 0.9f), 8);

            // Wide enough for a 25 character name (+ star, arrow, distance tag, padding, frame, scrollbar).
            float nameWidth = _rowText.CalcSize(Tmp(new string('M', NameChars))).x;
            _width = Mathf.Clamp(nameWidth + 200f, 520f, Screen.width * 0.45f);
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

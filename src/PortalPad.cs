using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Gamepad and arrow keys for the portal list. Buttons are read once per frame (Update) and handed to the list on its next
    /// Layout event. While the list is open, the buttons it uses are hidden from the map (no pin placing on A, no
    /// pin-type change on the D-pad); B and the sticks keep their vanilla meaning unless a Portaler window is open.
    /// </summary>
    internal static class PortalPad
    {
        /// <summary>The player uses a gamepad right now (Valheim's own switch: moving the mouse turns it off).</summary>
        public static bool PadInput => Plugin.Gamepad.Value && PortalClient.Active && ZInput.IsGamepadActive();

        /// <summary>The list is driven by the arrow keys (from the first arrow press until the mouse moves).</summary>
        public static bool KeyNav;

        /// <summary>Gamepad or arrow keys: the list shows a chosen row instead of following the mouse.</summary>
        public static bool Active => PortalClient.Active && (PadInput || KeyNav);

        // pressed since the list last looked (cleared by Clear)
        public static bool Up, Down, Left, Right, A, B, X, Y, LB, RB;
        /// <summary>Held: right stick (y negative = up, as in Valheim), and LB/RB (-1 / +1) for the colour picker.</summary>
        public static Vector2 Stick;
        public static int Tab;

        private static bool _reading;
        private static float _nextUp, _nextDown, _nextLeft, _nextRight;
        private static bool _heldUp, _heldDown, _heldLeft, _heldRight;
        private const float RepeatDelay = 0.4f, RepeatRate = 0.09f;

        public static void Tick()
        {
            if (!PortalClient.Active) KeyNav = false;
            if (!PortalClient.Active || TextInput.IsVisible() || PortalUI.SearchFocused) { Clear(); Stick = Vector2.zero; Tab = 0; return; }
            bool pad = PadInput;
            _reading = true;
            try
            {
                float sy = 0f, sx = 0f;
                if (pad) { sy = ZInput.GetJoyRightStickY(); sx = ZInput.GetJoyRightStickX(); }
                Stick = new Vector2(Mathf.Abs(sx) > 0.2f ? sx : 0f, Mathf.Abs(sy) > 0.2f ? sy : 0f);

                // arrow keys switch the list to keyboard control
                bool kUp = ZInput.GetKey(KeyCode.UpArrow), kDown = ZInput.GetKey(KeyCode.DownArrow),
                     kLeft = ZInput.GetKey(KeyCode.LeftArrow), kRight = ZInput.GetKey(KeyCode.RightArrow);
                if (!KeyNav && (kUp || kDown || kLeft || kRight)) KeyNav = true;

                // Valheim's stick Y is negative when pushed up
                Up |= Repeat(pad && ZInput.GetButton("JoyDPadUp") || sy < -0.5f || kUp, ref _nextUp, ref _heldUp);
                Down |= Repeat(pad && ZInput.GetButton("JoyDPadDown") || sy > 0.5f || kDown, ref _nextDown, ref _heldDown);
                Left |= Repeat(pad && ZInput.GetButton("JoyDPadLeft") || kLeft, ref _nextLeft, ref _heldLeft);
                Right |= Repeat(pad && ZInput.GetButton("JoyDPadRight") || kRight, ref _nextRight, ref _heldRight);
                if (KeyNav) A |= ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter);
                if (pad)
                {
                    A |= ZInput.GetButtonDown("JoyButtonA");
                    B |= ZInput.GetButtonDown("JoyButtonB");
                    X |= ZInput.GetButtonDown("JoyButtonX");
                    Y |= ZInput.GetButtonDown("JoyButtonY");
                    LB |= ZInput.GetButtonDown("JoyTabLeft");
                    RB |= ZInput.GetButtonDown("JoyTabRight");
                    Tab = (ZInput.GetButton("JoyTabRight") ? 1 : 0) - (ZInput.GetButton("JoyTabLeft") ? 1 : 0);
                }
                else Tab = 0;
            }
            finally { _reading = false; }
        }

        /// <summary>Press = one step; held = more steps after a short pause.</summary>
        private static bool Repeat(bool held, ref float next, ref bool was)
        {
            float now = Time.unscaledTime;
            bool fresh = held && !was;
            was = held;
            if (fresh) { next = now + RepeatDelay; return true; }
            if (held && now >= next) { next = now + RepeatRate; return true; }
            return false;
        }

        public static void Clear() { Up = Down = Left = Right = A = B = X = Y = LB = RB = false; }

        /// <summary>Buttons the open list uses; the rest of the game does not see them meanwhile.</summary>
        private static bool Owns(string name)
        {
            if (_reading || !PadInput || TextInput.IsVisible()) return false;
            switch (name)
            {
                case "JoyDPadUp": case "JoyDPadDown": case "JoyDPadLeft": case "JoyDPadRight":
                case "JoyButtonA": case "JoyButtonX": case "JoyButtonY": case "JoyTabLeft": case "JoyTabRight":
                    return true;
                case "JoyButtonB":
                    return PortalUI.WindowOpen || PortalUI.Picking; // otherwise B closes the map as usual
                default:
                    return false;
            }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
        private static class ZInput_GetButtonDown
        {
            private static bool Prefix(string name, ref bool __result) { if (!Owns(name)) return true; __result = false; return false; }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
        private static class ZInput_GetButton
        {
            private static bool Prefix(string name, ref bool __result) { if (!Owns(name)) return true; __result = false; return false; }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp))]
        private static class ZInput_GetButtonUp
        {
            private static bool Prefix(string name, ref bool __result) { if (!Owns(name)) return true; __result = false; return false; }
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Gamepad for the portal list. Buttons are read once per frame (Update) and handed to the list on its next
    /// Layout event. While the list is open, the buttons it uses are hidden from the map (no pin placing on A, no
    /// pin-type change on the D-pad); B and the sticks keep their vanilla meaning unless a Portaler window is open.
    /// </summary>
    internal static class PortalPad
    {
        /// <summary>The player uses a gamepad right now (Valheim's own switch: moving the mouse turns it off).</summary>
        public static bool Active => Plugin.Gamepad.Value && PortalClient.Active && ZInput.IsGamepadActive();

        // pressed since the list last looked (cleared by Take)
        public static bool Up, Down, Left, Right, A, B, X, Y, LB, RB;
        /// <summary>Held: right stick (y negative = up, as in Valheim), and LB/RB (-1 / +1) for the colour picker.</summary>
        public static Vector2 Stick;
        public static int Tab;

        private static bool _reading;
        private static float _nextUp, _nextDown, _nextLeft, _nextRight;
        private static bool _stickUp, _stickDown, _stickLeft, _stickRight;
        private const float RepeatDelay = 0.4f, RepeatRate = 0.09f;

        public static void Tick()
        {
            if (!Active || TextInput.IsVisible()) { Clear(); Stick = Vector2.zero; Tab = 0; return; }
            _reading = true;
            try
            {
                float sy = ZInput.GetJoyRightStickY(), sx = ZInput.GetJoyRightStickX();
                Stick = new Vector2(Mathf.Abs(sx) > 0.2f ? sx : 0f, Mathf.Abs(sy) > 0.2f ? sy : 0f);
                // Valheim's stick Y is negative when pushed up
                Up |= Repeat("JoyDPadUp", sy < -0.5f, ref _nextUp, ref _stickUp);
                Down |= Repeat("JoyDPadDown", sy > 0.5f, ref _nextDown, ref _stickDown);
                Left |= Repeat("JoyDPadLeft", false, ref _nextLeft, ref _stickLeft);
                Right |= Repeat("JoyDPadRight", false, ref _nextRight, ref _stickRight);
                A |= ZInput.GetButtonDown("JoyButtonA");
                B |= ZInput.GetButtonDown("JoyButtonB");
                X |= ZInput.GetButtonDown("JoyButtonX");
                Y |= ZInput.GetButtonDown("JoyButtonY");
                LB |= ZInput.GetButtonDown("JoyTabLeft");
                RB |= ZInput.GetButtonDown("JoyTabRight");
                Tab = (ZInput.GetButton("JoyTabRight") ? 1 : 0) - (ZInput.GetButton("JoyTabLeft") ? 1 : 0);
            }
            finally { _reading = false; }
        }

        /// <summary>Press = one step; held = more steps after a short pause.</summary>
        private static bool Repeat(string button, bool stick, ref float next, ref bool stickWas)
        {
            float now = Time.unscaledTime;
            bool fresh = ZInput.GetButtonDown(button) || (stick && !stickWas);
            stickWas = stick;
            if (fresh) { next = now + RepeatDelay; return true; }
            if ((ZInput.GetButton(button) || stick) && now >= next) { next = now + RepeatRate; return true; }
            return false;
        }

        public static void Clear() { Up = Down = Left = Right = A = B = X = Y = LB = RB = false; }

        /// <summary>Buttons the open list uses; the rest of the game does not see them meanwhile.</summary>
        private static bool Owns(string name)
        {
            if (_reading || !Active || TextInput.IsVisible()) return false;
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

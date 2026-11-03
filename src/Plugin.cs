using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Portaler – private family portal mod by nfp007.
    /// Step into any portal, pick a destination from the list or the map, travel without restrictions.
    /// The same DLL runs on the server and on every client.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nfp007.Portaler";
        public const string Name = "Portaler";
        public const string Version = "1.0.13";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        /// <summary>Server setting: use the game's own portal rules (item restrictions, world modifiers).</summary>
        internal static BepInEx.Configuration.ConfigEntry<bool> VanillaRestrictions;

        // personal settings (each PC)
        internal static BepInEx.Configuration.ConfigEntry<KeyCode> RenameKey, CategoryKey;
        internal static BepInEx.Configuration.ConfigEntry<bool> HoverShowsMap, HoverReturn, HoverLine;
        internal static BepInEx.Configuration.ConfigEntry<bool> PerfLog, QuickArrival;
        internal static BepInEx.Configuration.ConfigEntry<float> HoverDelay, HoverZoomMeters, HoverReturnDelay, PinSizePreview, PinSizeHover, HoverMaxFitMeters, HoverMoveSeconds;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            VanillaRestrictions = Config.Bind("Server", "VanillaRestrictions", false,
                "false = no restrictions: everything can travel through every portal (default).\n" +
                "true = the game's own rules: no metals/ores etc. through the wooden portal, and the world modifiers " +
                "'no portals' / 'no portals during boss fights' apply.\n" +
                "Only the server's (or the host's) value counts; it is sent to all players.");

            RenameKey = Config.Bind("Controls", "RenameKey", KeyCode.E, "Hover a name in the list and press this key to rename the portal.");
            CategoryKey = Config.Bind("Controls", "CategoryKey", KeyCode.T, "Hover a name in the list and press this key to choose the portal's category.");

            HoverShowsMap = Config.Bind("Map", "HoverShowsMap", true, "Hovering a name in the list moves the map to that portal.");
            HoverDelay = Config.Bind("Map", "HoverDelay", 0.4f, "Seconds the mouse must rest on a name before the map moves.");
            HoverZoomMeters = Config.Bind("Map", "HoverZoomMeters", 1500f, "For far targets: how much of the world the map shows around the target portal (metres).");
            HoverReturn = Config.Bind("Map", "HoverReturn", true, "When the mouse leaves the list, the map glides back to where it was (unless you move or zoom the map yourself).");
            HoverReturnDelay = Config.Bind("Map", "HoverStaySeconds", 10f, "Seconds the map stays on the preview after the mouse leaves the list, before it glides back.");
            HoverMaxFitMeters = Config.Bind("Map", "HoverMaxFitMeters", 2000f, "Portals closer than this (metres) are shown together: the map frames both. Farther targets are shown directly, without gliding across the map.");
            HoverMoveSeconds = Config.Bind("Map", "HoverMoveSeconds", 1f, "How long the calm glide to a preview (and back) takes, in seconds.");
            PinSizePreview = Config.Bind("Map", "PinSizePreview", 1.6f, "Size of the previewed portal's pin on the map (1 = normal). It stays this big as long as the fiery line is shown.");
            PinSizeHover = Config.Bind("Map", "PinSizeHover", 1.3f, "Size of a portal pin while the mouse is over it on the map (1 = normal).");
            HoverLine = Config.Bind("Map", "HoverLine", true, "Draw a fiery line from the portal you stand in to the previewed portal.");
            QuickArrival = Config.Bind("Travel", "QuickArrival", true,
                "Shorter waits for trips through Portaler: you are moved as soon as the screen is dark and arrive as soon as the " +
                "target area is loaded, instead of the game's fixed 2 s + at least 8 s. You still never arrive before the area is ready. " +
                "Switched off automatically when Fast Teleport is installed.");
            PerfLog = Config.Bind("Debug", "PerfLog", false,
                "Write Portaler's own time and memory use per frame, and how long each teleport takes, to the BepInEx log every 10 seconds.");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version}{(Perf.DevBuild ? " (developer build, performance log on)" : "")} loaded");
        }

        private void Update()
        {
            try
            {
                var t = Perf.Begin();
                PortalServer.Tick();
                Perf.End(Perf.S.ServerTick, t);
                t = Perf.Begin();
                PortalClient.Tick();
                Perf.End(Perf.S.ClientTick, t);
            }
            catch (System.Exception e)
            {
                Log.LogError(e);
            }
        }

        // The map only re-places its pins now and then, so pin sizes are applied every frame here.
        private void LateUpdate()
        {
            try
            {
                var t = Perf.Begin();
                PortalClient.HideForeignPins();
                Perf.End(Perf.S.Hide, t);
                t = Perf.Begin();
                PortalClient.ScalePins();
                Perf.End(Perf.S.Scale, t);
                Perf.TeleportTick();
                Perf.Tick();
            }
            catch (System.Exception e)
            {
                Log.LogError(e);
            }
        }

        private void OnGUI()
        {
            try
            {
                var t = Perf.Begin();
                PortalUI.Draw();
                Perf.End(Perf.S.Gui, t);
            }
            catch (System.Exception e)
            {
                Log.LogError(e);
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}

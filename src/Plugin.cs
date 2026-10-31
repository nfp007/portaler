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
        public const string Version = "1.0.6";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        /// <summary>Server setting: use the game's own portal rules (item restrictions, world modifiers).</summary>
        internal static BepInEx.Configuration.ConfigEntry<bool> VanillaRestrictions;

        // personal settings (each PC)
        internal static BepInEx.Configuration.ConfigEntry<KeyCode> RenameKey, CategoryKey;
        internal static BepInEx.Configuration.ConfigEntry<bool> HoverShowsMap, HoverReturn, HoverLine;
        internal static BepInEx.Configuration.ConfigEntry<float> HoverDelay, HoverZoomMeters, HoverReturnDelay, PinSizePreview, PinSizeHover;

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
            HoverZoomMeters = Config.Bind("Map", "HoverZoomMeters", 1500f, "How much of the world the map shows around the portal (metres). The map only zooms in, never out.");
            HoverReturn = Config.Bind("Map", "HoverReturn", true, "When the mouse leaves the list, the map glides back to where it was (unless you move or zoom the map yourself).");
            HoverReturnDelay = Config.Bind("Map", "HoverReturnDelay", 5f, "Seconds the map stays on the previewed portal after the mouse leaves the list.");
            PinSizePreview = Config.Bind("Map", "PinSizePreview", 1.6f, "Size of the previewed portal's pin on the map (1 = normal). It stays this big as long as the fiery line is shown.");
            PinSizeHover = Config.Bind("Map", "PinSizeHover", 1.3f, "Size of a portal pin while the mouse is over it on the map (1 = normal).");
            HoverLine = Config.Bind("Map", "HoverLine", true, "Draw a fiery line from the portal you stand in to the previewed portal.");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            try
            {
                PortalServer.Tick();
                PortalClient.Tick();
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
                PortalClient.HideForeignPins();
                PortalClient.ScalePins();
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
                PortalUI.Draw();
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

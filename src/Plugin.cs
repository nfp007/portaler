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
        public const string Version = "0.5.2";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        /// <summary>Server setting: use the game's own portal rules (item restrictions, world modifiers).</summary>
        internal static BepInEx.Configuration.ConfigEntry<bool> VanillaRestrictions;

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

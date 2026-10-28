using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace TheShack.BetterPaths
{
    // The hoe's Pathen grades the ground instead of leaving it as rough as it was. Each click grades a strip from your
    // feet to where you aim (in a free build camera, from your last click):
    //
    //   - flat from side to side, with an even slope from the height at your feet to the height at the aim point;
    //   - the sides and the far end blend into the ground around them, so there are no ledges or lumps;
    //   - nothing behind your feet changes, so standing at the end of the last strip and aiming on continues the
    //     path from exactly where it ended.
    //
    // Pathen then paints the strip dirt, at full strength so no grass shows through; Shift gives vanilla Pathen (paints
    // dirt only). Paved road only paints stone over a path, along the same kind of strip, and never changes the height;
    // Shift paints a round patch at the aim point. It costs its stone by length (once per 2 m by default).
    //
    // The ground is changed by whoever owns that patch of terrain, like vanilla terrain edits, so every player and the
    // server need this mod (Jotunn turns away anyone without it). The result is ordinary terrain data: graded paths stay
    // as they are if the mod is removed. The settings are admin-only and come from the server (Jotunn config sync).
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "theshack.betterpaths";
        public const string PluginName = "Shack Better Paths";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;
        private bool ready;

        private void Update()
        {
            if (ready)
            {
                FreeCam.Track();
            }
        }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);

            // A mod that throws in Awake takes the chainloader down with it. If the game changed under us, say so and
            // leave the hoe vanilla.
            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                Access.Init();
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Log.LogError($"{PluginName} could not hook into this game version; the hoe stays vanilla. {e}");
                return;
            }

            ready = true;
            Log.LogInfo($"{PluginName} {PluginVersion} ready (path width {Settings.Width.Value} m, " +
                $"Paved road: 1 stone per {Settings.MetersPerStone.Value} m).");
        }
    }

    internal static class Settings
    {
        public static ConfigEntry<bool> PathenGrades;
        public static ConfigEntry<bool> PavedRoadPaints;
        public static ConfigEntry<bool> PaveOnlyPaths;

        public static ConfigEntry<float> Width;
        public static ConfigEntry<float> EdgeBlend;
        public static ConfigEntry<float> MaxDistance;
        public static ConfigEntry<float> PaintMargin;

        public static ConfigEntry<float> MetersPerStone;

        public static void Bind(ConfigFile config)
        {
            int order = 100;
            ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null) =>
                config.Bind(section, key, value, new ConfigDescription(description, range,
                    new ConfigurationManagerAttributes { IsAdminOnly = true, Order = order-- }));

            PathenGrades = Bind("1 - Tools", "Pathen grades", true,
                "Pathen grades an even slope from your feet to where you aim (in the build camera: from your last click), " +
                "then paints it dirt. Off: vanilla Pathen. Holding Shift gives vanilla Pathen either way (paints dirt only).");
            PavedRoadPaints = Bind("1 - Tools", "Paved road paints", true,
                "Paved road only paints stone over the ground, never changing its height: along a strip from your feet to " +
                "where you aim (in the build camera: from your last click). Shift paints a round patch where you aim. " +
                "Off: vanilla Paved road (smooths the ground toward the height of your feet).");
            PaveOnlyPaths = Bind("1 - Tools", "Paved road only on paths", true,
                "Paved road lays stone only where the ground is already dirt or paved (a path), so a click that spills " +
                "onto grass leaves the grass alone. Off: it paves the whole strip.");

            Width = Bind("2 - Shape", "Path width", 4f,
                "Width of the flat part of the path, in meters.", new AcceptableValueRange<float>(1f, 10f));
            EdgeBlend = Bind("2 - Shape", "Edge blend", 1.5f,
                "How far past the sides and the far end the path blends into the ground around it, in meters. 0 = hard edges.",
                new AcceptableValueRange<float>(0f, 5f));
            PaintMargin = Bind("2 - Shape", "Paint margin", 0.25f,
                "How far past each side of the path the dirt or paving goes, in meters, so the edges come out clean. " +
                "The banks beyond keep their grass.", new AcceptableValueRange<float>(0f, 2f));
            MaxDistance = Bind("2 - Shape", "Max distance from feet", 8f,
                "Longest strip, in meters. Walking: a click further than this from your feet is refused. In the build " +
                "camera: a click further than this from your last click starts a new path.", new AcceptableValueRange<float>(2f, 20f));

            MetersPerStone = Bind("3 - Cost", "Paved road meters per stone", 2f,
                "Paved road takes its stone once per this many meters of path, rounded up (vanilla takes 1 stone per click). " +
                "0 = once per click, like vanilla. Pathen is free, like vanilla.",
                new AcceptableValueRange<float>(0f, 20f));
        }
    }
}

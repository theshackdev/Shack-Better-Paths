using UnityEngine;

namespace TheShack.BetterPaths
{
    internal enum Tool
    {
        None,
        Pathen,
        PavedRoad
    }

    internal enum PaintMode
    {
        None = 0,
        Dirt = 1,
        Paved = 2
    }

    // The hoe pieces this mod takes over, by prefab name: Pathen grades and paints dirt, Paved road only paints stone.
    internal static class Tools
    {
        public const string PathenPrefab = "path_v2";
        public const string PavedRoadPrefab = "paved_road_v2";

        public static Tool Of(GameObject go)
        {
            if (go == null)
            {
                return Tool.None;
            }
            switch (Utils.GetPrefabName(go))
            {
                case PathenPrefab: return Tool.Pathen;
                case PavedRoadPrefab: return Tool.PavedRoad;
                default: return Tool.None;
            }
        }

        /// The tool does this mod's thing (its setting is on); otherwise it's vanilla.
        public static bool Handles(Tool tool)
        {
            switch (tool)
            {
                case Tool.Pathen: return Settings.PathenGrades.Value;
                case Tool.PavedRoad: return Settings.PavedRoadPaints.Value;
                default: return false;
            }
        }

        /// Paved road paints over the ground as it is; it never changes the height.
        public static bool PaintOnly(Tool tool) => tool == Tool.PavedRoad;

        public static PaintMode Paint(Tool tool)
        {
            switch (tool)
            {
                case Tool.Pathen: return PaintMode.Dirt;
                case Tool.PavedRoad: return PaintMode.Paved;
                default: return PaintMode.None;
            }
        }

        /// The vanilla prefab whose paint settings (brush curve, strength) the strip is painted with: Paved road's for
        /// paving, Pathen's for dirt.
        public static int PaintPrefabHash(Tool tool)
        {
            switch (Paint(tool))
            {
                case PaintMode.Dirt: return PathenPrefab.GetStableHashCode();
                case PaintMode.Paved: return PavedRoadPrefab.GetStableHashCode();
                default: return 0;
            }
        }

        /// Shift (AltPlace) is held: the vanilla tool. Same test as Player.UpdatePlacementGhost.
        public static bool AltPlaceHeld(Player player)
        {
            if (ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive())
            {
                return Access.AltPlace(player);
            }
            return ZInput.GetButton("AltPlace") || (ZInput.GetButton("JoyAltPlace") && !ZInput.GetButton("JoyRotate"));
        }
    }
}

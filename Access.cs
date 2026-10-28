using System;
using HarmonyLib;
using UnityEngine;

namespace TheShack.BetterPaths
{
    // The game's private members this mod uses, resolved once at startup (Init) so a game update that renames one
    // fails loudly in the log instead of halfway through grading a path.
    internal static class Access
    {
        public static AccessTools.FieldRef<TerrainComp, ZNetView> TerrainView;
        public static AccessTools.FieldRef<TerrainComp, Heightmap> TerrainMap;
        public static AccessTools.FieldRef<TerrainComp, bool> TerrainInitialized;
        public static AccessTools.FieldRef<TerrainComp, int> TerrainWidth;
        public static AccessTools.FieldRef<TerrainComp, float[]> LevelDelta;
        public static AccessTools.FieldRef<TerrainComp, float[]> SmoothDelta;
        public static AccessTools.FieldRef<TerrainComp, bool[]> ModifiedHeight;
        public static AccessTools.FieldRef<TerrainComp, int> Operations;
        public static AccessTools.FieldRef<TerrainComp, Vector3> LastOpPoint;
        public static AccessTools.FieldRef<TerrainComp, float> LastOpRadius;
        public static AccessTools.FieldRef<TerrainComp, Color[]> PaintMask;
        public static AccessTools.FieldRef<TerrainComp, bool[]> ModifiedPaint;
        public static Action<TerrainComp, bool> Save;
        public static Action<TerrainComp, Vector3, Vector3, TerrainOp.Settings> PaintCleared;

        public static Action<TerrainOp> OnPlaced;

        public static AccessTools.FieldRef<Player, GameObject> PlacementGhost;
        public static AccessTools.FieldRef<Player, bool> AltPlace;
        public static Action<Player, bool> UpdatePlacementGhost;

        public static void Init()
        {
            TerrainView = AccessTools.FieldRefAccess<TerrainComp, ZNetView>("m_nview");
            TerrainMap = AccessTools.FieldRefAccess<TerrainComp, Heightmap>("m_hmap");
            TerrainInitialized = AccessTools.FieldRefAccess<TerrainComp, bool>("m_initialized");
            TerrainWidth = AccessTools.FieldRefAccess<TerrainComp, int>("m_width");
            LevelDelta = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_levelDelta");
            SmoothDelta = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_smoothDelta");
            ModifiedHeight = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");
            Operations = AccessTools.FieldRefAccess<TerrainComp, int>("m_operations");
            LastOpPoint = AccessTools.FieldRefAccess<TerrainComp, Vector3>("m_lastOpPoint");
            LastOpRadius = AccessTools.FieldRefAccess<TerrainComp, float>("m_lastOpRadius");
            PaintMask = AccessTools.FieldRefAccess<TerrainComp, Color[]>("m_paintMask");
            ModifiedPaint = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedPaint");
            Save = Method<Action<TerrainComp, bool>>(typeof(TerrainComp), "Save", typeof(bool));
            PaintCleared = Method<Action<TerrainComp, Vector3, Vector3, TerrainOp.Settings>>(typeof(TerrainComp), "PaintCleared",
                typeof(Vector3), typeof(Vector3), typeof(TerrainOp.Settings));

            OnPlaced = Method<Action<TerrainOp>>(typeof(TerrainOp), "OnPlaced");

            PlacementGhost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            AltPlace = AccessTools.FieldRefAccess<Player, bool>("m_altPlace");
            UpdatePlacementGhost = Method<Action<Player, bool>>(typeof(Player), "UpdatePlacementGhost", typeof(bool));
        }

        private static T Method<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
            AccessTools.MethodDelegate<T>(AccessTools.Method(type, name, parameters)
                ?? throw new MissingMethodException(type.Name, name));
    }
}

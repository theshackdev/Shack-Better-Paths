using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheShack.BetterPaths
{
    // A click with Pathen or Paved road goes:
    //   Player.TryPlacePiece (prefix): build the strip to the aim point (from the feet; from the last click in a free
    //     camera; a round patch for Shift + Paved road), check distance, wards and stone, and either refuse with a
    //     message or leave the strip in Pending;
    //   vanilla places the piece, which instantiates the tool's TerrainOp;
    //   TerrainOp.Awake (prefix): send Pending to the terrain instead of the vanilla edit, take the extra stone;
    //   vanilla then takes the piece's own cost and the stamina, as for any hoe click.
    [HarmonyPatch]
    internal static class Patches
    {
        private static Strip? Pending;
        private static bool pendingFromCamera;
        private static bool pendingNewPath;

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece)), HarmonyPrefix]
        private static bool TryPlacePiecePrefix(Player __instance, Piece piece, ref bool __result)
        {
            Pending = null;
            if (__instance != Player.m_localPlayer || piece == null)
            {
                return true;
            }
            Tool tool = Tools.Of(piece.gameObject);
            bool shift = Tools.AltPlaceHeld(__instance);
            if (!Tools.Handles(tool) || (shift && !Tools.PaintOnly(tool)))
            {
                return true; // vanilla tool; Shift + Pathen is vanilla Pathen, which only paints
            }

            // Vanilla updates the ghost to this frame's aim before placing; do it first so the strip ends where the
            // piece goes.
            Access.UpdatePlacementGhost(__instance, false);
            // No valid aim (nothing hit, aimed at a building piece...): let vanilla say why.
            GameObject ghost = Access.PlacementGhost(__instance);
            if (ghost == null || !ghost.activeSelf)
            {
                return true;
            }

            Vector3 aim = ghost.transform.position;
            bool camera = !shift && FreeCam.Active(__instance);
            bool newPath = false;
            Strip strip;
            string error = null;
            bool built = true;
            if (shift)
            {
                strip = Strip.Make(__instance, aim, aim, tool); // Shift + Paved road: a round patch of paving
            }
            else if (camera)
            {
                strip = Strip.Make(__instance, FreeCam.StartFor(tool, aim, out newPath), aim, tool);
            }
            else
            {
                built = Strip.TryFromFeet(__instance, aim, tool, out strip, out error);
            }
            if (!built || !CheckArea(strip, out error) || !CheckCost(__instance, piece, ref strip, out error))
            {
                __instance.Message(MessageHud.MessageType.Center, error);
                __result = false;
                return false;
            }
            Pending = strip;
            pendingFromCamera = camera;
            pendingNewPath = newPath;
            return true;
        }

        // Placement may still be refused by vanilla (blocked, invalid spot); never let a strip outlive its click.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece)), HarmonyPostfix]
        private static void TryPlacePiecePostfix() => Pending = null;

        [HarmonyPatch(typeof(TerrainOp), "Awake"), HarmonyPrefix]
        private static bool TerrainOpAwakePrefix(TerrainOp __instance)
        {
            if (TerrainOp.m_forceDisableTerrainOps || Pending == null)
            {
                return true;
            }
            Strip strip = Pending.Value;
            Pending = null;
            if (Tools.Of(__instance.gameObject) != strip.Tool)
            {
                return true;
            }

            Grading.Send(strip);
            if (pendingFromCamera)
            {
                FreeCam.Remember(strip.Tool, strip.End);
                if (pendingNewPath && Player.m_localPlayer != null)
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                        $"New path: clicks within {Settings.MaxDistance.Value:0} m connect");
                }
            }

            Player player = Player.m_localPlayer;
            Piece piece = __instance.GetComponent<Piece>();
            if (strip.CostMultiplier > 1 && player != null && piece != null && !IsFree(player, piece))
            {
                // Vanilla takes the piece's cost once after this; take the rest.
                player.ConsumeResources(piece.m_resources, 0, -1, strip.CostMultiplier - 1);
            }

            Access.OnPlaced(__instance);
            Object.Destroy(__instance.gameObject);
            return false;
        }

        // Paved road's ghost sits at the height of the player's feet (vanilla levels to it); it only paints now, and in a
        // build camera the feet are far away, so show it on the ground where it's aimed.
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost"), HarmonyPostfix]
        private static void UpdatePlacementGhostPostfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }
            GameObject ghost = Access.PlacementGhost(__instance);
            if (ghost == null || !ghost.activeSelf || Tools.Of(ghost) != Tool.PavedRoad || !Tools.Handles(Tool.PavedRoad))
            {
                return;
            }
            Vector3 at = ghost.transform.position;
            if (ZoneSystem.instance.GetGroundHeight(at, out float ground))
            {
                ghost.transform.position = new Vector3(at.x, ground, at.z);
            }
        }

        [HarmonyPatch(typeof(TerrainComp), "Awake"), HarmonyPostfix]
        private static void TerrainCompAwakePostfix(TerrainComp __instance) => Grading.Register(__instance);

        [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.ApplyToHeightmap)), HarmonyPrefix]
        private static void ApplyToHeightmapPrefix(TerrainComp __instance, List<float> heights, float[] baseHeights) =>
            Grading.RememberGround(__instance, heights, baseHeights);

        // Wards and no-build areas (dungeons, boss altars) along the whole strip, not just at the aim point.
        private static bool CheckArea(Strip strip, out string error)
        {
            error = null;
            int steps = Mathf.Max(1, Mathf.CeilToInt(strip.Length));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(strip.Start, strip.End, i / (float)steps);
                if (!PrivateArea.CheckAccess(at, strip.HalfWidth + strip.Blend))
                {
                    error = "$msg_privatezone";
                    return false;
                }
                if (Location.IsInsideNoBuildLocation(at))
                {
                    error = "$msg_nobuildzone";
                    return false;
                }
            }
            return true;
        }

        // Paved road: its cost once per "meters per stone" of strip, rounded up (a few cm of slack so 2.01 m is 1x).
        // Short of stone, the strip is cut to what the stone covers instead of refused; vanilla already checked that the
        // first stone is there. Counted with the game's own requirement check, so mods that let building use nearby
        // chests (CraftFromContainers) count those too, and charged the same way (ConsumeResources).
        private static bool CheckCost(Player player, Piece piece, ref Strip strip, out string error)
        {
            error = null;
            float perStone = Settings.MetersPerStone.Value;
            if (strip.Tool != Tool.PavedRoad || perStone <= 0f || strip.IsPad)
            {
                return true;
            }
            int need = Mathf.Max(1, Mathf.CeilToInt((strip.Length - 0.05f) / perStone));
            if (need == 1 || IsFree(player, piece))
            {
                strip.CostMultiplier = need;
                return true;
            }
            int affordable = need;
            while (affordable > 1 && !HaveTimes(player, piece, affordable))
            {
                affordable--;
            }
            strip.CostMultiplier = affordable;
            if (affordable < need)
            {
                float length = strip.Length;
                float reach = Mathf.Min(length, affordable * perStone);
                Vector3 dir = strip.Direction;
                strip.End = new Vector3(strip.Start.x + dir.x * reach, strip.End.y, strip.Start.z + dir.z * reach);
                player.Message(MessageHud.MessageType.TopLeft,
                    $"Out of {Localization.instance.Localize(piece.m_resources[0].m_resItem.m_itemData.m_shared.m_name)}: " +
                    $"paved {reach:0.#} of {length:0.#} m");
            }
            return true;
        }

        // Player.HaveRequirements for `times` the piece's cost: the requirement amounts are scaled for the call and put
        // back straight after (they belong to the shared prefab).
        private static bool HaveTimes(Player player, Piece piece, int times)
        {
            int[] amounts = new int[piece.m_resources.Length];
            for (int i = 0; i < amounts.Length; i++)
            {
                amounts[i] = piece.m_resources[i].m_amount;
            }
            try
            {
                foreach (Piece.Requirement requirement in piece.m_resources)
                {
                    requirement.m_amount *= times;
                }
                return player.HaveRequirements(piece, Player.RequirementMode.CanBuild);
            }
            finally
            {
                for (int i = 0; i < amounts.Length; i++)
                {
                    piece.m_resources[i].m_amount = amounts[i];
                }
            }
        }

        // The same exemptions vanilla uses for build costs: the no-cost cheat and the world's free-build key.
        private static bool IsFree(Player player, Piece piece) =>
            player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
    }
}

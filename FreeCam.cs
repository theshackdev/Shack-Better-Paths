using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheShack.BetterPaths
{
    // Building from a free build camera (Shack Magic Cam), the character stays at the workbench, so a strip can't start
    // at the feet. Instead each click connects to the previous click with the same tool: the first click marks the start
    // (a round patch), and every click after it grades or paves from the last one to the aim point. A click further than
    // "Max distance from feet" from the last one starts a new path, and so does leaving or re-entering the camera.
    internal static class FreeCam
    {
        /// Any other build camera: the view this far from the character's eyes counts as a free camera.
        private const float DetachedDistance = 15f;

        private static FieldInfo magicCamActive;
        private static bool lookedForMagicCam;
        private static bool wasActive;
        private static readonly Dictionary<Tool, Vector3> lastPoint = new Dictionary<Tool, Vector3>();

        public static bool Active(Player player)
        {
            if (!lookedForMagicCam)
            {
                lookedForMagicCam = true;
                System.Type plugin = AccessTools.TypeByName("TheShack.MagicCam.Plugin");
                magicCamActive = plugin != null ? AccessTools.Field(plugin, "BuildCameraActive") : null;
            }
            if (magicCamActive != null)
            {
                return (bool)magicCamActive.GetValue(null);
            }
            return player != null && GameCamera.instance != null
                && Vector3.Distance(GameCamera.instance.transform.position, player.GetEyePoint()) > DetachedDistance;
        }

        /// Every frame (Plugin.Update): forget the last clicks whenever the camera is entered or left.
        public static void Track()
        {
            bool active = Player.m_localPlayer != null && Active(Player.m_localPlayer);
            if (active != wasActive)
            {
                wasActive = active;
                lastPoint.Clear();
            }
        }

        /// Where a camera click with this tool starts: the last click, when it's close enough; otherwise the aim point
        /// itself (a new path).
        public static Vector3 StartFor(Tool tool, Vector3 aim, out bool newPath)
        {
            if (lastPoint.TryGetValue(tool, out Vector3 last)
                && new Vector2(aim.x - last.x, aim.z - last.z).magnitude <= Settings.MaxDistance.Value)
            {
                newPath = false;
                return last;
            }
            newPath = true;
            return aim;
        }

        public static void Remember(Tool tool, Vector3 end) => lastPoint[tool] = end;
    }
}

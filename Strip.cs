using UnityEngine;

namespace TheShack.BetterPaths
{
    // One click's worth of path: from Start (the player's feet, or the last click in a free camera) to End (the aim
    // point). Start.y and End.y are the graded heights at each end; in between the height changes evenly. Across the path
    // it is flat. A PaintOnly strip (Paved road) only paints that shape and leaves the heights alone.
    //
    // Shape, seen from above: a rectangle from Start to End, HalfWidth to each side of the line. That part is graded
    // exactly. Past the sides and past End, the change fades out over Blend meters. Nothing behind Start changes, so a
    // strip that starts where the last one ended joins it without a step. (It ends at End rather than running on past
    // it, so the stone a Paved road strip costs matches its length.)
    internal struct Strip
    {
        /// Shorter than this, a click grades a round pad at the feet height instead of a strip.
        public const float MinLength = 0.5f;

        public Vector3 Start;
        public Vector3 End;
        public float HalfWidth;
        public float Blend;
        public PaintMode Paint;
        public int PaintPrefabHash;
        public bool PaintOnly;

        // Local only (not sent): which tool made it and how many times its cost is taken.
        public Tool Tool;
        public int CostMultiplier;

        public float Length => new Vector2(End.x - Start.x, End.z - Start.z).magnitude;
        public bool IsPad => Length < MinLength;
        public Vector3 Center => IsPad ? End : (Start + End) * 0.5f;
        /// Radius around Center that holds everything this strip changes.
        public float Reach => (IsPad ? 0f : Length * 0.5f) + HalfWidth + Blend;

        /// The ground area (x, z) this strip can change.
        public void Bounds(out Vector2 min, out Vector2 max)
        {
            float r = HalfWidth + Blend;
            Vector3 a = IsPad ? End : Start;
            min = new Vector2(Mathf.Min(a.x, End.x) - r, Mathf.Min(a.z, End.z) - r);
            max = new Vector2(Mathf.Max(a.x, End.x) + r, Mathf.Max(a.z, End.z) + r);
        }

        public Vector3 Direction
        {
            get
            {
                Vector3 d = new Vector3(End.x - Start.x, 0f, End.z - Start.z);
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            }
        }

        /// How much the ground at (x, z) moves to this strip (1 = exactly, 0 = untouched) and the height it moves to.
        public bool Weight(float x, float z, out float weight, out float height)
        {
            float outside;
            float t;
            if (IsPad)
            {
                outside = new Vector2(x - End.x, z - End.z).magnitude - HalfWidth;
                t = 1f;
            }
            else
            {
                float length = Length;
                Vector3 dir = Direction;
                float ax = x - Start.x;
                float az = z - Start.z;
                float along = ax * dir.x + az * dir.z;
                if (along < 0f)
                {
                    weight = 0f;
                    height = 0f;
                    return false;
                }
                float side = Mathf.Max(0f, Mathf.Abs(ax * dir.z - az * dir.x) - HalfWidth);
                float beyond = along - length;
                outside = beyond > 0f ? Mathf.Sqrt(beyond * beyond + side * side) : side;
                t = Mathf.Clamp01(along / length);
            }

            height = Mathf.Lerp(Start.y, End.y, t);
            if (outside <= 0f)
            {
                weight = 1f;
                return true;
            }
            if (outside >= Blend)
            {
                weight = 0f;
                return false;
            }
            float u = outside / Blend;
            weight = 1f - u * u * (3f - 2f * u);
            return true;
        }

        public void Write(ZPackage pkg)
        {
            pkg.Write(Start);
            pkg.Write(End);
            pkg.Write(HalfWidth);
            pkg.Write(Blend);
            pkg.Write((int)Paint);
            pkg.Write(PaintPrefabHash);
            pkg.Write(PaintOnly);
        }

        public static Strip Read(ZPackage pkg) => new Strip
        {
            Start = pkg.ReadVector3(),
            End = pkg.ReadVector3(),
            HalfWidth = pkg.ReadSingle(),
            Blend = pkg.ReadSingle(),
            Paint = (PaintMode)pkg.ReadInt(),
            PaintPrefabHash = pkg.ReadInt(),
            PaintOnly = pkg.ReadBool()
        };

        /// Refuses strips no unmodified client would send, so one bad client can't reshape a whole zone or paint with
        /// anything but Pathen's dirt or Paved road's paving. (Heights need no check: the owner keeps every edit within
        /// vanilla's +-8 m.)
        public bool IsSane()
        {
            return Finite(Start) && Finite(End) && HalfWidth > 0f && HalfWidth <= 5f && Blend >= 0f && Blend <= 5f
                && Length <= Settings.MaxDistance.Value + 1f && (!PaintOnly || Paint != PaintMode.None)
                && (Paint == PaintMode.None ? PaintPrefabHash == 0
                    : Paint == PaintMode.Dirt ? PaintPrefabHash == Tools.PathenPrefab.GetStableHashCode()
                    : Paint == PaintMode.Paved && PaintPrefabHash == Tools.PavedRoadPrefab.GetStableHashCode());
        }

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);

        /// The strip from the player's feet to `aim`. `error` says why not, for the player.
        public static bool TryFromFeet(Player player, Vector3 aim, Tool tool, out Strip strip, out string error)
        {
            strip = default;
            error = null;
            Vector3 feet = player.transform.position;
            float distance = new Vector2(aim.x - feet.x, aim.z - feet.z).magnitude;
            if (distance > Settings.MaxDistance.Value)
            {
                error = $"Too far from your feet ({distance:0} m, max {Settings.MaxDistance.Value:0} m). " +
                    "Stand closer, or use the build camera.";
                return false;
            }
            strip = Make(player, feet, aim, tool);
            return true;
        }

        /// The strip from `start` to `aim` for this tool, with heights from the ground. When start is aim, it's a round
        /// patch at the aim point.
        public static Strip Make(Player player, Vector3 start, Vector3 aim, Tool tool)
        {
            Strip strip = new Strip
            {
                Start = start,
                End = aim,
                HalfWidth = Settings.Width.Value * 0.5f,
                Blend = Settings.EdgeBlend.Value,
                Paint = Tools.Paint(tool),
                PaintPrefabHash = Tools.PaintPrefabHash(tool),
                PaintOnly = Tools.PaintOnly(tool),
                Tool = tool,
                CostMultiplier = 1
            };

            // Each end's height is the ground averaged across the path there, so a rock or a dip under one spot
            // doesn't tilt the whole strip. On an already graded path that is just its height.
            Vector3 dir = strip.Direction;
            if (strip.IsPad)
            {
                dir = player.transform.forward;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            }
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            strip.Start.y = GroundAcross(start, side, strip.HalfWidth);
            strip.End.y = strip.IsPad ? strip.Start.y : GroundAcross(aim, side, strip.HalfWidth);
            return strip;
        }

        private static float GroundAcross(Vector3 center, Vector3 side, float halfWidth)
        {
            float sum = 0f;
            int count = 0;
            for (int i = -2; i <= 2; i++)
            {
                if (ZoneSystem.instance.GetGroundHeight(center + side * (halfWidth * i * 0.5f), out float height))
                {
                    sum += height;
                    count++;
                }
            }
            return count > 0 ? sum / count : center.y;
        }
    }
}

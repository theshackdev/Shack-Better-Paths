using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TheShack.BetterPaths
{
    // Grades the terrain under a strip. The placing client sends the strip to every terrain patch (TerrainComp, one per
    // 64 m zone) it reaches; the patch's owner changes its own height data and saves it to the world, as vanilla terrain
    // edits do (TerrainComp.RPC_ApplyOperation). Everyone else loads the change from the saved data.
    internal static class Grading
    {
        private const string RpcName = "theshack.betterpaths.Grade";
        private const int DataVersion = 2;

        /// Vanilla never lets player edits move the ground more than this from its unedited height (ApplyToHeightmap).
        private const float MaxChange = 8f;

        /// Paint dabs along the strip are this far apart.
        private const float PaintSpacing = 0.75f;

        private static bool warnedBadStrip;
        private static bool warnedNoPaint;

        // The ground each patch's edits are added to, as of its last rebuild: the generated terrain plus what locations
        // (trader, villages, altars...) do to it, and the reference vanilla's +-8 m limit is measured from. Both are
        // exactly what TerrainComp.ApplyToHeightmap gets, so grading lands where the game will draw it.
        private sealed class Ground
        {
            public float[] Heights;
            public float[] LimitBase;
        }

        private static readonly ConditionalWeakTable<TerrainComp, Ground> grounds = new ConditionalWeakTable<TerrainComp, Ground>();

        /// From a prefix on TerrainComp.ApplyToHeightmap: `heights` before this patch's edits are added, and the
        /// reference they're limited against.
        public static void RememberGround(TerrainComp comp, List<float> heights, float[] limitBase)
        {
            if (heights == null || limitBase == null || heights.Count != limitBase.Length)
            {
                return;
            }
            Ground ground = grounds.GetValue(comp, _ => new Ground());
            if (ground.Heights == null || ground.Heights.Length != heights.Count)
            {
                ground.Heights = new float[heights.Count];
                ground.LimitBase = new float[heights.Count];
            }
            heights.CopyTo(ground.Heights);
            limitBase.CopyTo(ground.LimitBase, 0);
        }

        public static void Send(Strip strip)
        {
            List<Heightmap> maps = new List<Heightmap>();
            Heightmap.FindHeightmap(strip.Center, strip.Reach, maps);
            strip.Bounds(out Vector2 min, out Vector2 max);
            foreach (Heightmap map in maps)
            {
                // FindHeightmap tests a square around the strip's center; skip zones only that square's corners reach,
                // so a click doesn't leave empty terrain records in neighbouring zones.
                float half = map.m_width * map.m_scale * 0.5f;
                Vector3 at = map.transform.position;
                if (max.x < at.x - half || min.x > at.x + half || max.y < at.z - half || min.y > at.z + half)
                {
                    continue;
                }
                ZNetView view = Access.TerrainView(map.GetAndCreateTerrainCompiler());
                if (view == null || !view.IsValid())
                {
                    continue;
                }
                ZPackage pkg = new ZPackage();
                pkg.Write(DataVersion);
                strip.Write(pkg);
                view.InvokeRPC(RpcName, pkg);
            }
        }

        public static void Register(TerrainComp comp)
        {
            ZNetView view = Access.TerrainView(comp);
            if (view != null)
            {
                view.Register<ZPackage>(RpcName, (sender, pkg) => OnGrade(comp, pkg));
            }
        }

        private static void OnGrade(TerrainComp comp, ZPackage pkg)
        {
            if (comp == null)
            {
                return;
            }
            ZNetView view = Access.TerrainView(comp);
            if (!view.IsValid() || !view.IsOwner() || !Access.TerrainInitialized(comp))
            {
                return;
            }
            Strip strip;
            try
            {
                int version = pkg.ReadInt();
                if (version != DataVersion)
                {
                    Plugin.Log.LogWarning($"Got a path in format {version}, this version reads {DataVersion}; ignored. Everyone needs the same Shack Better Paths.");
                    return;
                }
                strip = Strip.Read(pkg);
            }
            catch (System.Exception)
            {
                Plugin.Log.LogWarning("Ignored a malformed path message.");
                return;
            }
            if (!strip.IsSane())
            {
                if (!warnedBadStrip)
                {
                    warnedBadStrip = true;
                    Plugin.Log.LogWarning($"Ignored a path outside this server's limits ({strip.Length:0.#} m long, {strip.HalfWidth * 2f:0.#} m wide).");
                }
                return;
            }
            Apply(comp, strip);
        }

        private static void Apply(TerrainComp comp, Strip strip)
        {
            Heightmap map = Access.TerrainMap(comp);
            int width = Access.TerrainWidth(comp);
            int pitch = width + 1;
            float scale = map.m_scale;
            float[] level = Access.LevelDelta(comp);
            float[] smooth = Access.SmoothDelta(comp);
            bool[] modified = Access.ModifiedHeight(comp);
            if (!grounds.TryGetValue(comp, out Ground ground) || ground.Heights.Length != pitch * pitch)
            {
                ground = null;
            }
            Vector3 origin = map.transform.position;

            strip.Bounds(out Vector2 min, out Vector2 max);
            map.WorldToVertex(new Vector3(min.x, 0f, min.y), out int x0, out int y0);
            map.WorldToVertex(new Vector3(max.x, 0f, max.y), out int x1, out int y1);
            x0 = Mathf.Max(x0, 0);
            y0 = Mathf.Max(y0, 0);
            x1 = Mathf.Min(x1, width);
            y1 = Mathf.Min(y1, width);

            int changed = 0;
            for (int y = y0; y <= y1 && !strip.PaintOnly; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float worldX = origin.x + (x - width * 0.5f) * scale;
                    float worldZ = origin.z + (y - width * 0.5f) * scale;
                    if (!strip.Weight(worldX, worldZ, out float weight, out float height))
                    {
                        continue;
                    }
                    // Work from the remembered ground plus the stored edits rather than the current mesh, which only
                    // catches up at the end of the frame: two strips in one frame still add up correctly. A patch
                    // created by this click has no edits yet, so its mesh is the ground.
                    int i = y * pitch + x;
                    float bare = ground != null ? ground.Heights[i] : map.GetHeight(x, y) - level[i] - smooth[i];
                    float limitBase = ground != null ? ground.LimitBase[i] : bare;
                    float current = Mathf.Clamp(bare + level[i] + smooth[i], limitBase - MaxChange, limitBase + MaxChange);
                    float graded = Mathf.Clamp(Mathf.Lerp(current, height - origin.y, weight),
                        limitBase - MaxChange, limitBase + MaxChange);
                    level[i] = graded - bare;
                    smooth[i] = 0f;
                    modified[i] = true;
                    changed++;
                }
            }

            int painted = 0;
            if (strip.PaintOnly && Settings.PaveOnlyPaths.Value)
            {
                painted = PaintOverPath(comp, map, strip, Settings.PaintMargin.Value);
            }
            else if (strip.Paint != PaintMode.None && ObjectDB.instance != null
                && ObjectDB.instance.TryGetTerrainOp(strip.PaintPrefabHash, out TerrainOp tool))
            {
                // PaintCleared blends each pixel from the patch's stored paint only while the heightmap has a rebuild
                // queued (vanilla: a second edit in the same frame); otherwise from the drawn texture, which still shows
                // the paint from before this click, so each dab would replace the last instead of adding to it.
                map.m_doLateUpdate = 1;
                TerrainOp.Settings paint = PaintSettings(tool.m_settings, strip.HalfWidth + Settings.PaintMargin.Value);
                int dabs = strip.IsPad ? 0 : Mathf.CeilToInt(strip.Length / PaintSpacing);
                for (int k = 0; k <= dabs; k++)
                {
                    Vector3 at = dabs == 0 ? strip.End : Vector3.Lerp(strip.Start, strip.End, k / (float)dabs);
                    if (map.IsPointInside(at, paint.m_paintRadius + scale))
                    {
                        Access.PaintCleared(comp, at, Vector3.zero, paint);
                        painted++;
                    }
                }
            }
            else if (strip.Paint != PaintMode.None && !warnedNoPaint)
            {
                warnedNoPaint = true;
                Plugin.Log.LogWarning($"No paint settings for prefab hash {strip.PaintPrefabHash}; paths are graded but not painted.");
            }

            if (changed == 0 && painted == 0)
            {
                return;
            }
            Access.Operations(comp)++;
            Access.LastOpPoint(comp) = strip.Center;
            Access.LastOpRadius(comp) = strip.Reach;
            Access.Save(comp, false);
            map.Poke(1);
            if (ClutterSystem.instance != null)
            {
                ClutterSystem.instance.ResetGrass(strip.Center, strip.Reach);
            }
        }

        // Paved road on a path only: paints this patch's own mask pixels under the strip (the same rounded shape and
        // near-full-strength brush as the dabs) but only those already showing as dirt or paved, the way the grass reads
        // them (Heightmap.IsCleared: over 0.5). Grass and farmland are left alone. Each patch under the strip gets the
        // strip and paints its own copy of the shared edge pixels the same way, so zone borders match without the
        // neighbour spreading vanilla's brush does. Returns how many pixels changed.
        private static int PaintOverPath(TerrainComp comp, Heightmap map, Strip strip, float margin)
        {
            Color[] mask = Access.PaintMask(comp);
            bool[] modified = Access.ModifiedPaint(comp);
            int width = Access.TerrainWidth(comp);
            int pitch = width + 1;
            int half = pitch / 2;
            float scale = map.m_scale;
            Vector3 origin = map.transform.position;
            float radius = strip.HalfWidth + margin;
            Color paved = Heightmap.m_paintMaskPaved;
            Vector2 a = new Vector2(strip.IsPad ? strip.End.x : strip.Start.x, strip.IsPad ? strip.End.z : strip.Start.z);
            Vector2 b = new Vector2(strip.End.x, strip.End.z);
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;

            int changed = 0;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int i = y * pitch + x;
                    if (i >= mask.Length)
                    {
                        continue;
                    }
                    // The world point this pixel stands for (the inverse of PaintCleared's half-offset WorldToVertexMask).
                    Vector2 p = new Vector2(origin.x + (x - half + 0.5f) * scale, origin.z + (y - half + 0.5f) * scale);
                    float t = lengthSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
                    float distance = Vector2.Distance(p, a + ab * t);
                    if (distance > radius)
                    {
                        continue;
                    }
                    Color old = mask[i];
                    if (Mathf.Max(old.r, old.b) <= 0.5f)
                    {
                        continue; // not a path
                    }
                    float value = Mathf.Pow(1f - distance / radius, 0.1f);
                    Color now = new Color(Mathf.Lerp(old.r, paved.r, value), Mathf.Lerp(old.g, paved.g, value),
                        Mathf.Lerp(old.b, paved.b, value), old.a);
                    if (now != old)
                    {
                        mask[i] = now;
                        modified[i] = true;
                        changed++;
                    }
                }
            }
            return changed;
        }

        // The vanilla tool's paint type (dirt or paved) at full strength out to the radius. Vanilla's own brush is
        // weaker (and weaker still on fresh grass), so a path builds up over several passes; grass only goes where the
        // paint is over half strength (Heightmap.IsCleared), so one strip painted that way left grass showing through.
        // The exponent keeps the brush near full strength until the very edge (vanilla's default without a curve).
        private static TerrainOp.Settings PaintSettings(TerrainOp.Settings vanilla, float radius) => new TerrainOp.Settings
        {
            m_paintCleared = true,
            m_paintType = vanilla.m_paintType,
            m_paintRadius = radius,
            m_paintStrength = 1f,
            m_paintExp = 0.1f,
            m_paintCurve = null,
            m_paintHeightCheck = false,
            m_addMedianMax = 0f,
            m_centerMultiplicationFactor = 0f,
            m_pointMultiplicationFactor = 0f,
            m_halfOffset = vanilla.m_halfOffset
        };
    }
}

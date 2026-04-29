using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimFloorPlan
{
    /// <summary>
    /// Terrain operations for ValheimFloorPlan.
    ///
    ///  LevelForPlan  – Raises the inner build pad to the HIGHEST terrain point within
    ///                  the footprint.  Because targetY = max height, the level operation
    ///                  only ever RAISES terrain — it never lowers it.  The disc falloff
    ///                  at the pad edge therefore always slopes DOWN to natural terrain,
    ///                  never upward.  Upward falloff is the root cause of all spikes:
    ///                  when a disc lowers terrain, the falloff zone must ramp back up to
    ///                  the original higher ground on the uphill side → spike.
    ///
    ///  DigMoat       – Optional.  Digs a trench ring at a safe distance from the inner
    ///                  pad after pieces are placed.
    /// </summary>
    public static class TerrainLeveler
    {
        // ── inner pad ─────────────────────────────────────────────────────────
        private const float LEVEL_RADIUS  = 3.0f;
        private const float SAMPLE_STEP   = 2.0f;
        private const int   INNER_PAD     = 2;      // cells of buffer around plan bounding box

        // ── moat ──────────────────────────────────────────────────────────────
        private const int   MOAT_OFFSET       = 6;  // gap in cells between inner pad and moat inner edge
        private const int   MOAT_WIDTH        = 4;  // moat width in cells
        private const float MOAT_DEPTH        = 2f; // metres below pad level
        private const float MOAT_LEVEL_RADIUS = 1.0f; // small discs → minimal overlap between neighbours
        private const float MOAT_SAMPLE_STEP  = 1.0f; // dense sampling → no gaps in moat floor

        private const float WARN_RAISE   = 6f;   // warn in log above this height range
        private const int   OPS_PER_FRAME = 10;

        // Set by LevelForPlan after it knows how many chunks were touched.
        // FloorPlanBuilder reads this to decide how long to wait before placing pieces.
        public static float RecommendedPlacementWait { get; private set; } = 2f;

        // ── LevelForPlan ──────────────────────────────────────────────────────
        // Pre-samples the footprint to find min/max height, then raises everything
        // to maxY in TWO passes.  The settle time between passes and the recommended
        // placement wait both scale with the number of terrain chunks modified so
        // that large builds get the time they need.
        // Terrain is ONLY ever raised → disc falloff always slopes DOWN to natural
        // terrain → no upward spikes possible.
        // If the range exceeds WARN_RAISE a warning is logged but the build continues.
        public static IEnumerator LevelForPlan(FloorPlan plan, Vector3 origin)
        {
            GetBounds(plan, origin, INNER_PAD,
                out float innerMinX, out float innerMaxX,
                out float innerMinZ, out float innerMaxZ);

            // Pre-sample: find min and max terrain heights across the footprint.
            float maxY = float.MinValue;
            float minY = float.MaxValue;
            for (float x = innerMinX; x <= innerMaxX + 0.01f; x += SAMPLE_STEP)
                for (float z = innerMinZ; z <= innerMaxZ + 0.01f; z += SAMPLE_STEP)
                {
                    float h = SampleHeight(x, z, origin.y);
                    if (h > maxY) maxY = h;
                    if (h < minY) minY = h;
                }
            if (maxY == float.MinValue) { maxY = origin.y; minY = origin.y; }

            float range   = maxY - minY;
            float targetY = maxY;

            if (range > WARN_RAISE)
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[TerrainLeveler] Footprint height range {range:F1}m — steep ground," +
                    " cliff-edge tears may appear on the downhill side.");

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Raising pad to Y={targetY:F2} (range={range:F1}m)" +
                $"  [{innerMinX:F1}..{innerMaxX:F1}] x [{innerMinZ:F1}..{innerMaxZ:F1}]");

            int ops = 0;
            var modified = new HashSet<TerrainComp>();

            // Pass 1.
            for (float x = innerMinX; x <= innerMaxX + 0.01f; x += SAMPLE_STEP)
                for (float z = innerMinZ; z <= innerMaxZ + 0.01f; z += SAMPLE_STEP)
                {
                    ApplyLevel(x, targetY, z, LEVEL_RADIUS, modified);
                    if (++ops % OPS_PER_FRAME == 0) yield return null;
                }

            // Scale settle time by the number of chunks touched: each Valheim terrain
            // chunk is 64×64m and rebuilds its heightmap mesh independently.  Allow
            // ~0.75s per chunk, min 1s, so large builds don't get a gap in the middle.
            float settleTime = Mathf.Max(1.0f, modified.Count * 0.75f);
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Pass 1 done ({modified.Count} chunks). Settling {settleTime:F1}s before pass 2.");
            yield return new WaitForSeconds(settleTime);

            // Pass 2 — re-levels any points that didn't fully reach targetY in pass 1.
            for (float x = innerMinX; x <= innerMaxX + 0.01f; x += SAMPLE_STEP)
                for (float z = innerMinZ; z <= innerMaxZ + 0.01f; z += SAMPLE_STEP)
                {
                    ApplyLevel(x, targetY, z, LEVEL_RADIUS, modified);
                    if (++ops % OPS_PER_FRAME == 0) yield return null;
                }

            // Recommended wait before placing pieces: same scale, min 2s.
            RecommendedPlacementWait = Mathf.Max(2.0f, modified.Count * 0.75f);
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Pad raised: {ops} ops across {modified.Count} chunks." +
                $" Placement wait: {RecommendedPlacementWait:F1}s.");
        }

        // ── DigMoat ───────────────────────────────────────────────────────────
        // Digs a trench ring MOAT_OFFSET cells beyond the inner pad.
        // Called after PlacePieces so it cannot disturb the flat build pad.
        public static IEnumerator DigMoat(FloorPlan plan, Vector3 origin)
        {
            float targetY = SampleHeight(origin.x, origin.z, origin.y);
            float moatY   = targetY - MOAT_DEPTH;

            // Inner pad boundary (same as LevelForPlan so we know the safe zone).
            GetBounds(plan, origin, INNER_PAD,
                out float innerMinX, out float innerMaxX,
                out float innerMinZ, out float innerMaxZ);

            // Moat inner edge: MOAT_OFFSET cells beyond the inner pad.
            float moatInnerMinX = innerMinX - MOAT_OFFSET * PieceMap.CELL_SIZE;
            float moatInnerMaxX = innerMaxX + MOAT_OFFSET * PieceMap.CELL_SIZE;
            float moatInnerMinZ = innerMinZ - MOAT_OFFSET * PieceMap.CELL_SIZE;
            float moatInnerMaxZ = innerMaxZ + MOAT_OFFSET * PieceMap.CELL_SIZE;

            // Moat outer edge: MOAT_WIDTH cells beyond the moat inner edge.
            float moatOuterMinX = moatInnerMinX - MOAT_WIDTH * PieceMap.CELL_SIZE;
            float moatOuterMaxX = moatInnerMaxX + MOAT_WIDTH * PieceMap.CELL_SIZE;
            float moatOuterMinZ = moatInnerMinZ - MOAT_WIDTH * PieceMap.CELL_SIZE;
            float moatOuterMaxZ = moatInnerMaxZ + MOAT_WIDTH * PieceMap.CELL_SIZE;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Digging moat at Y={moatY:F2}" +
                $"  outer [{moatOuterMinX:F1}..{moatOuterMaxX:F1}] x [{moatOuterMinZ:F1}..{moatOuterMaxZ:F1}]");

            int ops = 0;
            var modified = new HashSet<TerrainComp>();

            for (float x = moatOuterMinX; x <= moatOuterMaxX + 0.01f; x += MOAT_SAMPLE_STEP)
                for (float z = moatOuterMinZ; z <= moatOuterMaxZ + 0.01f; z += MOAT_SAMPLE_STEP)
                {
                    // Only stamp points that fall inside the outer boundary but outside the inner.
                    bool inMoatInner = x >= moatInnerMinX && x <= moatInnerMaxX
                                    && z >= moatInnerMinZ && z <= moatInnerMaxZ;
                    if (!inMoatInner)
                    {
                        ApplyLevel(x, moatY, z, MOAT_LEVEL_RADIUS, modified);
                        if (++ops % OPS_PER_FRAME == 0) yield return null;
                    }
                }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Moat dug: {ops} ops across {modified.Count} chunks.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the world-space bounding rectangle that LevelForPlan will modify.
        /// Used by FloorPlanBuilder to capture a terrain snapshot before leveling.
        /// </summary>
        public static void GetSnapshotBounds(FloorPlan plan, Vector3 origin,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            // Use the moat outer edge as the snapshot boundary so the full
            // modified area is captured, including any future moat ops.
            int pad = INNER_PAD + MOAT_OFFSET + MOAT_WIDTH + 4;
            GetBounds(plan, origin, pad, out minX, out maxX, out minZ, out maxZ);
        }

        private static void GetBounds(FloorPlan plan, Vector3 origin, int pad,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            int minCol = int.MaxValue, maxCol = int.MinValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var p in plan.Pieces)
            {
                if (p.Col < minCol) minCol = p.Col;
                if (p.Col > maxCol) maxCol = p.Col;
                if (p.Row < minRow) minRow = p.Row;
                if (p.Row > maxRow) maxRow = p.Row;
            }
            minX = origin.x + (minCol - pad) * PieceMap.CELL_SIZE;
            maxX = origin.x + (maxCol + pad) * PieceMap.CELL_SIZE;
            minZ = origin.z + (minRow - pad) * PieceMap.CELL_SIZE;
            maxZ = origin.z + (maxRow + pad) * PieceMap.CELL_SIZE;
        }

        private static void ApplyLevel(float x, float y, float z, float radius,
                                        HashSet<TerrainComp> modified)
        {
            var go = new GameObject("VFP_LevelOp");
            go.transform.position = new Vector3(x, y, z);
            var op = go.AddComponent<TerrainOp>();
            op.m_settings.m_level       = true;
            op.m_settings.m_levelRadius = radius;
            op.m_settings.m_smooth      = false;
            var tc = TerrainComp.FindTerrainCompiler(go.transform.position);
            if (tc != null) { tc.ApplyOperation(op); modified.Add(tc); }
            UnityEngine.Object.Destroy(go);
        }

        private static float SampleHeight(float x, float z, float referenceY = 0f)
        {
            if (Physics.Raycast(new Vector3(x, referenceY + 200f, z),
                    Vector3.down, out var hit, 500f, 1 << 11))
                return hit.point.y;
            return referenceY;
        }
    }
}

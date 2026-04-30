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
        private const float LEVEL_RADIUS      = 3.0f;
        private const float PRE_SAMPLE_STEP   = 0.5f;  // denser scan catches narrow local highs
        private const float LEVEL_SAMPLE_STEP = 0.5f;  // denser ops reduce edge misses on slopes
        private const float SPIKE_SCAN_STEP   = 0.5f;
        private const float SPIKE_LEVEL_RADIUS = 1.5f;
        private const float SPIKE_TOLERANCE   = 0.2f;
        private const int   SPIKE_PASSES      = 2;
        private const int   INNER_PAD     = 2;      // cells of buffer around plan bounding box

        // ── moat ──────────────────────────────────────────────────────────────
        private const int   MOAT_OFFSET       = 6;  // gap in cells between inner pad and moat inner edge
        private const int   MOAT_WIDTH        = 4;  // moat width in cells
        private const float MOAT_DEPTH        = 2f; // metres below pad level
        private const float MOAT_LEVEL_RADIUS = 1.0f; // small discs → minimal overlap between neighbours
        private const float MOAT_SAMPLE_STEP  = 1.0f; // dense sampling → no gaps in moat floor

        private const float WARN_RAISE   = 6f;   // warn in log above this height range
        private const int   OPS_PER_FRAME = 10;

        // Set by LevelForPlan after sampling the footprint.
        // FloorPlanBuilder uses this as the Y coordinate for all piece placement,
        // bypassing Physics.Raycast against the terrain collider (which may not have
        // rebuilt its physics mesh yet for large height changes).
        public static float TargetLevelY { get; private set; } = 0f;

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

            // Pre-sample: find min and max terrain heights across the full area that can be
            // affected by leveling ops (inner pad + disc falloff radius). If we sample only
            // the inner pad, uphill terrain just outside the pad can be higher than targetY;
            // boundary discs then cut that ring down, which appears as an unintended trench.
            float sampleMinX = innerMinX - LEVEL_RADIUS;
            float sampleMaxX = innerMaxX + LEVEL_RADIUS;
            float sampleMinZ = innerMinZ - LEVEL_RADIUS;
            float sampleMaxZ = innerMaxZ + LEVEL_RADIUS;

            // Pre-sample: find min and max terrain heights across the affected area.
            // Use count-based loops so the last step always lands exactly on the boundary,
            // regardless of whether (max - min) is an integer multiple of SAMPLE_STEP.
            float maxY = float.MinValue;
            float minY = float.MaxValue;
            int preStepsX = Mathf.CeilToInt((sampleMaxX - sampleMinX) / PRE_SAMPLE_STEP);
            int preStepsZ = Mathf.CeilToInt((sampleMaxZ - sampleMinZ) / PRE_SAMPLE_STEP);
            for (int ix = 0; ix <= preStepsX; ix++)
            {
                float x = (ix == preStepsX) ? sampleMaxX : sampleMinX + ix * PRE_SAMPLE_STEP;
                for (int iz = 0; iz <= preStepsZ; iz++)
                {
                    float z = (iz == preStepsZ) ? sampleMaxZ : sampleMinZ + iz * PRE_SAMPLE_STEP;
                    float h = SampleHeight(x, z, origin.y);
                    if (h > maxY) maxY = h;
                    if (h < minY) minY = h;
                }
            }
            if (maxY == float.MinValue) { maxY = origin.y; minY = origin.y; }

            float range   = maxY - minY;
            float targetY = maxY;
            TargetLevelY  = targetY;

            if (range > WARN_RAISE)
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[TerrainLeveler] Footprint height range {range:F1}m — steep ground," +
                    " cliff-edge tears may appear on the downhill side.");

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Raising pad to Y={targetY:F2}  minY={minY:F2}  range={range:F1}m" +
                $"  [{innerMinX:F1}..{innerMaxX:F1}] x [{innerMinZ:F1}..{innerMaxZ:F1}]" +
                $"  sampled=[{sampleMinX:F1}..{sampleMaxX:F1}] x [{sampleMinZ:F1}..{sampleMaxZ:F1}]");

            // Scale pass count with height range.
            // The disc falloff means terrain-chunk boundary vertices converge at ~67% of
            // the remaining delta per pass (linear falloff: t = 1 − dist/radius; worst
            // case dist ≈ 1m from nearest disc centre in a 3m-radius disc).
            // Residual gap after N passes ≈ 0.33^N × range.  Choose N so the gap stays
            // below ~0.05m:  range ≥ 10m needs 5 passes (0.33^5 × 10 = 0.04m).
            int totalPasses = range >= 10f ? 5 : range >= 6f ? 4 : 3;

            // Count-based loops guarantee the last iteration always lands on innerMaxX /
            // innerMaxZ exactly, so the full boundary is covered regardless of SAMPLE_STEP.
            int stepsX = Mathf.CeilToInt((innerMaxX - innerMinX) / LEVEL_SAMPLE_STEP);
            int stepsZ = Mathf.CeilToInt((innerMaxZ - innerMinZ) / LEVEL_SAMPLE_STEP);

            int ops = 0;
            var modified = new HashSet<TerrainComp>();
            int totalLevelOps = totalPasses * (stepsX + 1) * (stepsZ + 1);
            int nextLevelPct = 10;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Running {totalPasses} leveling passes (range={range:F1}m).");
            ShowProgress($"Leveling terrain... 0% ({totalPasses} pass(es))");

            for (int pass = 1; pass <= totalPasses; pass++)
            {
                for (int ix = 0; ix <= stepsX; ix++)
                {
                    float x = (ix == stepsX) ? innerMaxX : innerMinX + ix * LEVEL_SAMPLE_STEP;
                    for (int iz = 0; iz <= stepsZ; iz++)
                    {
                        float z = (iz == stepsZ) ? innerMaxZ : innerMinZ + iz * LEVEL_SAMPLE_STEP;
                        ApplyLevel(x, targetY, z, LEVEL_RADIUS, modified);
                        ops++;
                        if (totalLevelOps > 0)
                        {
                            int pct = Mathf.FloorToInt((ops * 100f) / totalLevelOps);
                            if (pct >= nextLevelPct)
                            {
                                ShowProgress($"Leveling terrain... {nextLevelPct}%");
                                nextLevelPct += 10;
                            }
                        }
                        if (ops % OPS_PER_FRAME == 0) yield return null;
                    }
                }

                if (pass < totalPasses)
                    yield return new WaitForSeconds(0.1f);
            }

            // Spike suppression: explicitly cut any residual points above target in the
            // full affected area (inner pad + falloff radius). This handles narrow
            // needle outcrops that can survive regular grid-aligned passes.
            int spikeOps = 0;
            int spikeStepsX = Mathf.CeilToInt((sampleMaxX - sampleMinX) / SPIKE_SCAN_STEP);
            int spikeStepsZ = Mathf.CeilToInt((sampleMaxZ - sampleMinZ) / SPIKE_SCAN_STEP);
            for (int pass = 1; pass <= SPIKE_PASSES; pass++)
            {
                for (int ix = 0; ix <= spikeStepsX; ix++)
                {
                    float x = (ix == spikeStepsX) ? sampleMaxX : sampleMinX + ix * SPIKE_SCAN_STEP;
                    for (int iz = 0; iz <= spikeStepsZ; iz++)
                    {
                        float z = (iz == spikeStepsZ) ? sampleMaxZ : sampleMinZ + iz * SPIKE_SCAN_STEP;
                        float h = SampleHeight(x, z, targetY);
                        if (h > targetY + SPIKE_TOLERANCE)
                        {
                            ApplyLevel(x, targetY, z, SPIKE_LEVEL_RADIUS, modified);
                            spikeOps++;
                            if (spikeOps % OPS_PER_FRAME == 0) yield return null;
                        }
                    }
                }

                if (pass < SPIKE_PASSES)
                    yield return new WaitForSeconds(0.05f);
            }

            if (spikeOps > 0)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[TerrainLeveler] Spike suppression applied: {spikeOps} ops across {SPIKE_PASSES} pass(es).");
                ShowProgress("Leveling terrain... final cleanup");
            }
            else
                ValheimFloorPlanPlugin.Log.LogInfo("[TerrainLeveler] Spike suppression: no residual peaks detected.");

            // Placement wait: allow the terrain physics collider to rebuild so that
            // PlacePieces raycasts hit the correct height.  Terrain data is near-instant;
            // 2s is ample for the collision mesh.
            RecommendedPlacementWait = Mathf.Max(2.0f, modified.Count * 0.5f);
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Leveling done: {totalPasses} passes, {ops} ops, " +
                $"{modified.Count} chunks.  Placement wait: {RecommendedPlacementWait:F1}s.");
            ShowProgress("Leveling terrain... done");
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
        /// Returns the inner pad bounding rectangle (the area actually leveled).
        /// Used by FloorPlanBuilder to poll terrain physics readiness.
        /// </summary>
        public static void GetPadBounds(FloorPlan plan, Vector3 origin,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            GetBounds(plan, origin, INNER_PAD, out minX, out maxX, out minZ, out maxZ);
        }

        /// <summary>
        /// Returns the actual outer edge of terrain modification: the leveled pad
        /// boundary (plan + INNER_PAD) expanded by LEVEL_RADIUS (the disc falloff).
        /// Used by FloorPlanBuilder to draw the outer preview rectangle so it matches
        /// the visible terrain change, not the dormant moat extent.
        /// </summary>
        public static void GetLeveledAreaBounds(FloorPlan plan, Vector3 origin,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            GetBounds(plan, origin, INNER_PAD, out minX, out maxX, out minZ, out maxZ);
            minX -= LEVEL_RADIUS;
            maxX += LEVEL_RADIUS;
            minZ -= LEVEL_RADIUS;
            maxZ += LEVEL_RADIUS;
        }

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
                                        HashSet<TerrainComp> modified, bool smooth = false)
        {
            var go = new GameObject("VFP_LevelOp");
            go.transform.position = new Vector3(x, y, z);
            var op = go.AddComponent<TerrainOp>();
            op.m_settings.m_level       = true;
            op.m_settings.m_levelRadius = radius;
            op.m_settings.m_smooth      = smooth;

            // FindTerrainCompiler returns only the chunk containing a single point.
            // A disc of radius r can straddle up to 4 terrain chunks simultaneously
            // when its centre is near a chunk-boundary corner.  Probing only the 4
            // cardinal axis-aligned extremes (N/S/E/W) misses the diagonal chunk that
            // sits in the NE/NW/SE/SW quadrant beyond both boundaries at once.
            // Probing all 8 bounding-box corners + the centre guarantees every chunk
            // the disc physically overlaps is found and receives the operation.
            var probes = new Vector3[]
            {
                new Vector3(x,          y, z         ),   // centre
                new Vector3(x - radius, y, z - radius),   // SW corner
                new Vector3(x + radius, y, z - radius),   // SE corner
                new Vector3(x - radius, y, z + radius),   // NW corner
                new Vector3(x + radius, y, z + radius),   // NE corner
                new Vector3(x - radius, y, z         ),   // W  edge mid
                new Vector3(x + radius, y, z         ),   // E  edge mid
                new Vector3(x,          y, z - radius),   // S  edge mid
                new Vector3(x,          y, z + radius),   // N  edge mid
            };
            var chunks = new HashSet<TerrainComp>();
            foreach (var p in probes)
            {
                var tc = TerrainComp.FindTerrainCompiler(p);
                if (tc != null) chunks.Add(tc);
            }
            foreach (var tc in chunks)
            {
                tc.ApplyOperation(op);
                modified.Add(tc);
            }
            UnityEngine.Object.Destroy(go);
        }

        private static float SampleHeight(float x, float z, float referenceY = 0f)
        {
            // Use the terrain heightmap directly so that rocks, cliffs and other
            // GameObjects sitting on top of the terrain are ignored.  Physics.Raycast
            // on layer 11 hits the top surface of MineRock / cliff meshes, which would
            // push targetY up to boulder-top height and raise all surrounding terrain
            // to that level — creating an artificial rocky outcrop.
            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGroundHeight(new Vector3(x, referenceY, z), out float h))
                return h;
            // Fallback (ZoneSystem not ready): raycast terrain layer only.
            if (Physics.Raycast(new Vector3(x, referenceY + 200f, z),
                    Vector3.down, out var hit, 500f, 1 << 11))
                return hit.point.y;
            return referenceY;
        }

        private static void ShowProgress(string message)
        {
            ValheimFloorPlanPlugin.ShowProgressMessage(message);
        }
    }
}

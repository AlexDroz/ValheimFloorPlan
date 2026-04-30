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
        // to maxY using a configurable number of passes.
        // The settle time between passes and the recommended placement wait both
        // scale with the number of terrain chunks modified so that large builds get
        // the time they need.
        // Terrain is ONLY ever raised → disc falloff always slopes DOWN to natural
        // terrain → no upward spikes possible.
        // If the range exceeds WARN_RAISE a warning is logged but the build continues.
        public static IEnumerator LevelForPlan(FloorPlan plan, Vector3 origin, float rotationDeg = 0f)
        {
            // Always iterate in unrotated (plan-local) space, then rotate each point around
            // the player origin.  This keeps disc stamps aligned with the rotated footprint
            // rather than stamping an axis-aligned AABB.
            GetBounds(plan, origin, INNER_PAD, 0f,
                out float innerMinX, out float innerMaxX,
                out float innerMinZ, out float innerMaxZ);

            float cosR = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            float sinR = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);
            bool axisAligned = Mathf.Approximately(rotationDeg % 90f, 0f);
            float preSampleStep = axisAligned ? PRE_SAMPLE_STEP : 0.25f;
            float levelSampleStep = axisAligned ? LEVEL_SAMPLE_STEP : 0.25f;
            float spikeScanStep = axisAligned ? SPIKE_SCAN_STEP : 0.25f;

            // Pre-sample: find min and max terrain heights across the full area that can be
            // affected by leveling ops (inner pad + disc falloff radius). If we sample only
            // the inner pad, uphill terrain just outside the pad can be higher than targetY;
            // boundary discs then cut that ring down, which appears as an unintended trench.
            float sampleMinX = innerMinX - LEVEL_RADIUS;
            float sampleMaxX = innerMaxX + LEVEL_RADIUS;
            float sampleMinZ = innerMinZ - LEVEL_RADIUS;
            float sampleMaxZ = innerMaxZ + LEVEL_RADIUS;

            float maxY = float.MinValue;
            float minY = float.MaxValue;
            int preStepsX = Mathf.CeilToInt((sampleMaxX - sampleMinX) / preSampleStep);
            int preStepsZ = Mathf.CeilToInt((sampleMaxZ - sampleMinZ) / preSampleStep);
            for (int ix = 0; ix <= preStepsX; ix++)
            {
                float lx = (ix == preStepsX) ? sampleMaxX : sampleMinX + ix * preSampleStep;
                for (int iz = 0; iz <= preStepsZ; iz++)
                {
                    float lz = (iz == preStepsZ) ? sampleMaxZ : sampleMinZ + iz * preSampleStep;
                    float ldx = lx - origin.x, ldz = lz - origin.z;
                    float wx = origin.x + ldx * cosR + ldz * sinR;
                    float wz = origin.z - ldx * sinR + ldz * cosR;
                    float h = SampleHeight(wx, wz, origin.y);
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
                $"  rotation={rotationDeg:F0}°" +
                $"  inner(local)=[{innerMinX:F1}..{innerMaxX:F1}] x [{innerMinZ:F1}..{innerMaxZ:F1}]");

            int totalPasses = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainLevelPasses, 1, 5);

            int stepsX = Mathf.CeilToInt((innerMaxX - innerMinX) / levelSampleStep);
            int stepsZ = Mathf.CeilToInt((innerMaxZ - innerMinZ) / levelSampleStep);

            int ops = 0;
            var modified = new HashSet<TerrainComp>();
            int totalLevelOps = totalPasses * (stepsX + 1) * (stepsZ + 1);
            int nextLevelPct = 10;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Running {totalPasses} leveling passes (configured, range={range:F1}m).");
            ShowProgress($"Leveling terrain... 0% ({totalPasses} pass(es))");

            for (int pass = 1; pass <= totalPasses; pass++)
            {
                for (int ix = 0; ix <= stepsX; ix++)
                {
                    float lx = (ix == stepsX) ? innerMaxX : innerMinX + ix * levelSampleStep;
                    for (int iz = 0; iz <= stepsZ; iz++)
                    {
                        float lz = (iz == stepsZ) ? innerMaxZ : innerMinZ + iz * levelSampleStep;
                        float ldx2 = lx - origin.x, ldz2 = lz - origin.z;
                        float wx = origin.x + ldx2 * cosR + ldz2 * sinR;
                        float wz = origin.z - ldx2 * sinR + ldz2 * cosR;
                        ApplyLevel(wx, targetY, wz, LEVEL_RADIUS, modified);
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

            // Spike suppression: scan the extended area in local space, rotate, check/fix.
            int spikePasses = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainSpikeCleanupPasses, 1, 5);
            int spikeOps = 0;
            int spikeStepsX = Mathf.CeilToInt((sampleMaxX - sampleMinX) / spikeScanStep);
            int spikeStepsZ = Mathf.CeilToInt((sampleMaxZ - sampleMinZ) / spikeScanStep);
            for (int pass = 1; pass <= spikePasses; pass++)
            {
                for (int ix = 0; ix <= spikeStepsX; ix++)
                {
                    float lx = (ix == spikeStepsX) ? sampleMaxX : sampleMinX + ix * spikeScanStep;
                    for (int iz = 0; iz <= spikeStepsZ; iz++)
                    {
                        float lz = (iz == spikeStepsZ) ? sampleMaxZ : sampleMinZ + iz * spikeScanStep;
                        float ldx3 = lx - origin.x, ldz3 = lz - origin.z;
                        float wx = origin.x + ldx3 * cosR + ldz3 * sinR;
                        float wz = origin.z - ldx3 * sinR + ldz3 * cosR;
                        float h = SampleHeight(wx, wz, targetY);
                        if (h > targetY + SPIKE_TOLERANCE)
                        {
                            ApplyLevel(wx, targetY, wz, SPIKE_LEVEL_RADIUS, modified);
                            spikeOps++;
                            if (spikeOps % OPS_PER_FRAME == 0) yield return null;
                        }
                    }
                }

                if (pass < spikePasses)
                    yield return new WaitForSeconds(0.05f);
            }

            if (spikeOps > 0)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[TerrainLeveler] Spike suppression applied: {spikeOps} ops across {spikePasses} pass(es).");
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
            GetBounds(plan, origin, INNER_PAD, 0f,
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
            out float minX, out float maxX, out float minZ, out float maxZ,
            float rotationDeg = 0f)
        {
            GetBounds(plan, origin, INNER_PAD, rotationDeg, out minX, out maxX, out minZ, out maxZ);
        }

        /// <summary>
        /// Returns the actual outer edge of terrain modification: the leveled pad
        /// boundary (plan + INNER_PAD) expanded by LEVEL_RADIUS (the disc falloff).
        /// Used by FloorPlanBuilder to draw the outer preview rectangle so it matches
        /// the visible terrain change, not the dormant moat extent.
        /// </summary>
        public static void GetLeveledAreaBounds(FloorPlan plan, Vector3 origin,
            out float minX, out float maxX, out float minZ, out float maxZ,
            float rotationDeg = 0f)
        {
            GetBounds(plan, origin, INNER_PAD, rotationDeg, out minX, out maxX, out minZ, out maxZ);
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
            out float minX, out float maxX, out float minZ, out float maxZ,
            float rotationDeg = 0f)
        {
            // Use the moat outer edge as the snapshot boundary so the full
            // modified area is captured, including any future moat ops.
            int pad = INNER_PAD + MOAT_OFFSET + MOAT_WIDTH + 4;
            GetBounds(plan, origin, pad, rotationDeg, out minX, out maxX, out minZ, out maxZ);
        }

        private static void GetBounds(FloorPlan plan, Vector3 origin, int pad, float rotationDeg,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            int minCol = int.MaxValue, maxCol = int.MinValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var p in plan.Pieces)
            {
                int effW = 1;
                int effH = 1;
                var def = PieceMap.GetDef(p.Type);
                if (def != null)
                {
                    effW = def.EffW(p.Rotation);
                    effH = def.EffH(p.Rotation);
                }

                if (p.Col < minCol) minCol = p.Col;
                if (p.Col + effW > maxCol) maxCol = p.Col + effW;
                if (p.Row < minRow) minRow = p.Row;
                if (p.Row + effH > maxRow) maxRow = p.Row + effH;
            }

            if (minCol == int.MaxValue)
            {
                minCol = 0;
                minRow = 0;
                maxCol = plan.Cols;
                maxRow = plan.Rows;
            }

            float dx0 = (minCol - pad) * PieceMap.CELL_SIZE;
            float dx1 = (maxCol + pad) * PieceMap.CELL_SIZE;
            float dz0 = (minRow - pad) * PieceMap.CELL_SIZE;
            float dz1 = (maxRow + pad) * PieceMap.CELL_SIZE;

            if (Mathf.Approximately(rotationDeg % 360f, 0f))
            {
                minX = origin.x + dx0;
                maxX = origin.x + dx1;
                minZ = origin.z + dz0;
                maxZ = origin.z + dz1;
                return;
            }

            // Compute axis-aligned bounding box of the rotated rectangle.
            // Unity clockwise Y-rotation: x' = dx*cos + dz*sin,  z' = -dx*sin + dz*cos.
            float rad = rotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float[] cxArr = new float[] { dx0, dx1, dx1, dx0 };
            float[] czArr = new float[] { dz0, dz0, dz1, dz1 };
            float rMinX = float.MaxValue, rMaxX = float.MinValue;
            float rMinZ = float.MaxValue, rMaxZ = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float rx = cxArr[i] * cos + czArr[i] * sin;
                float rz = -cxArr[i] * sin + czArr[i] * cos;
                if (rx < rMinX) rMinX = rx;
                if (rx > rMaxX) rMaxX = rx;
                if (rz < rMinZ) rMinZ = rz;
                if (rz > rMaxZ) rMaxZ = rz;
            }
            minX = origin.x + rMinX;
            maxX = origin.x + rMaxX;
            minZ = origin.z + rMinZ;
            maxZ = origin.z + rMaxZ;
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

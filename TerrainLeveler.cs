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
    /// </summary>
    public static class TerrainLeveler
    {
        // ── inner pad ─────────────────────────────────────────────────────────
        // LEVEL_RADIUS is now read from config (TerrainStampRadius) at runtime.
        private static float LEVEL_RADIUS => Mathf.Clamp(ValheimFloorPlanPlugin.TerrainStampRadius, 3.0f, 6.0f);
        private const float PRE_SAMPLE_STEP   = 0.5f;  // denser scan catches narrow local highs
        private const float LEVEL_SAMPLE_STEP = 0.5f;  // denser ops reduce edge misses on slopes
        private const float EDGE_LEVEL_SAMPLE_STEP = 0.25f;
        private const float EDGE_BAND_WIDTH = 0.6f;
        private const float EDGE_LEVEL_RADIUS = 1.8f;
        private const float EDGE_RAISE_EPSILON = 0.03f;
        private const float SPIKE_SCAN_STEP   = 0.5f;
        private const float SPIKE_LEVEL_RADIUS = 1.5f;
        private const float SPIKE_TOLERANCE   = 0.2f;
        private const float TEAR_REPAIR_SAMPLE_RADIUS = 1.2f;
        private const float TEAR_REPAIR_MAIN_RADIUS = 1.1f;
        private const float TEAR_REPAIR_BLEND_RADIUS = 0.7f;
        private const float TEAR_REPAIR_MAX_RAISE = 0.08f;
        private const float TEAR_REPAIR_SPIKE_THRESHOLD = 0.28f;
        private const int   INNER_PAD     = 2;      // cells of buffer around plan bounding box

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

            // Scale the stamp step proportionally to the configured radius so that
            // adjacent flat zones always overlap regardless of radius setting.
            // Ratio 0.17 matches the proven default (radius 3.0 → step 0.51 → clamped to 0.5).
            // Step only ever gets smaller than the constant (denser), never larger — so
            // quality can only improve; the only cost is slightly more ops at low radii.
            float derivedStep = Mathf.Min(LEVEL_SAMPLE_STEP, LEVEL_RADIUS * 0.17f);

            float preSampleStep  = axisAligned ? Mathf.Min(PRE_SAMPLE_STEP,  LEVEL_RADIUS * 0.17f) : 0.25f;
            float levelSampleStep = axisAligned ? derivedStep : Mathf.Min(0.25f, derivedStep);
            float spikeScanStep  = axisAligned ? SPIKE_SCAN_STEP : 0.25f;

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
            float edgeSampleStep = Mathf.Min(levelSampleStep, EDGE_LEVEL_SAMPLE_STEP);
            int edgeStepsX = Mathf.CeilToInt((innerMaxX - innerMinX) / edgeSampleStep);
            int edgeStepsZ = Mathf.CeilToInt((innerMaxZ - innerMinZ) / edgeSampleStep);

            int edgePointsPerPass = 0;
            for (int ix = 0; ix <= edgeStepsX; ix++)
            {
                float lx = (ix == edgeStepsX) ? innerMaxX : innerMinX + ix * edgeSampleStep;
                for (int iz = 0; iz <= edgeStepsZ; iz++)
                {
                    float lz = (iz == edgeStepsZ) ? innerMaxZ : innerMinZ + iz * edgeSampleStep;
                    if (IsInEdgeBand(lx, lz, innerMinX, innerMaxX, innerMinZ, innerMaxZ, EDGE_BAND_WIDTH))
                        edgePointsPerPass++;
                }
            }

            int ops = 0;
            var modified = new HashSet<TerrainComp>();
            int totalLevelOps = totalPasses * (((stepsX + 1) * (stepsZ + 1)) + edgePointsPerPass);
            int nextLevelPct = 10;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Running {totalPasses} leveling passes (configured, range={range:F1}m).");
            ShowProgress($"Leveling terrain... 0% ({totalPasses} pass(es))");

            for (int pass = 1; pass <= totalPasses; pass++)
            {
                // Edge pass must never use a stronger radius than the main pass,
                // otherwise small configured stamp radii can produce a raised shell.
                float effectiveEdgeRadius = Mathf.Min(EDGE_LEVEL_RADIUS, LEVEL_RADIUS);

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

                // Refine only near the pad perimeter with a tighter sample step to
                // reduce V-shaped trenching artifacts at uphill/downhill edges.
                for (int ix = 0; ix <= edgeStepsX; ix++)
                {
                    float lx = (ix == edgeStepsX) ? innerMaxX : innerMinX + ix * edgeSampleStep;
                    for (int iz = 0; iz <= edgeStepsZ; iz++)
                    {
                        float lz = (iz == edgeStepsZ) ? innerMaxZ : innerMinZ + iz * edgeSampleStep;
                        if (!IsInEdgeBand(lx, lz, innerMinX, innerMaxX, innerMinZ, innerMaxZ, EDGE_BAND_WIDTH))
                            continue;

                        float ldx2 = lx - origin.x, ldz2 = lz - origin.z;
                        float wx = origin.x + ldx2 * cosR + ldz2 * sinR;
                        float wz = origin.z - ldx2 * sinR + ldz2 * cosR;

                        // Edge refinement should fill low pockets; avoid re-leveling
                        // already-high points which can amplify edge artifacts.
                        float h = SampleHeight(wx, wz, targetY);
                        if (h >= targetY - EDGE_RAISE_EPSILON)
                            continue;

                        ApplyLevel(wx, targetY, wz, effectiveEdgeRadius, modified);
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

        /// <summary>
        /// Performs a local blend pass around a user-selected tear point.
        /// This raises/levels immediate geometry without touching a large area.
        /// </summary>
        public static IEnumerator RepairTearAtPoint(Vector3 point)
        {
            var modified = new HashSet<TerrainComp>();

            // Use robust neighborhood statistics so a few outlier heights cannot push
            // the repair target upward into a new spike.
            float centerY = SampleHeight(point.x, point.z, point.y);
            var samples = new List<float>(16);
            var sampleOffsets = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2( TEAR_REPAIR_SAMPLE_RADIUS, 0f),
                new Vector2(-TEAR_REPAIR_SAMPLE_RADIUS, 0f),
                new Vector2(0f,  TEAR_REPAIR_SAMPLE_RADIUS),
                new Vector2(0f, -TEAR_REPAIR_SAMPLE_RADIUS),
                new Vector2( 0.85f,  0.85f),
                new Vector2( 0.85f, -0.85f),
                new Vector2(-0.85f,  0.85f),
                new Vector2(-0.85f, -0.85f),
            };

            foreach (var o in sampleOffsets)
                samples.Add(SampleHeight(point.x + o.x, point.z + o.y, centerY));

            samples.Sort();
            float medianY = samples.Count > 0 ? samples[samples.Count / 2] : centerY;

            // Prefer lowering/softening over raising: small raise cap avoids creating
            // the exact rocky peaks this tool is trying to remove.
            float desiredY = Mathf.Lerp(centerY, medianY, 0.7f);
            float maxAllowedY = centerY + TEAR_REPAIR_MAX_RAISE;
            float easedY = Mathf.Min(desiredY, maxAllowedY);

            // If current point already appears spike-like, force a downward correction.
            if (centerY > medianY + TEAR_REPAIR_SPIKE_THRESHOLD)
                easedY = Mathf.Min(easedY, medianY + 0.05f);

            var passOffsets = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(0.55f, 0f),
                new Vector2(-0.55f, 0f),
                new Vector2(0f, 0.55f),
                new Vector2(0f, -0.55f),
            };

            int opCount = 0;
            foreach (var o in passOffsets)
            {
                ApplyLevel(point.x + o.x, easedY, point.z + o.y, TEAR_REPAIR_MAIN_RADIUS, modified, smooth: true);
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            yield return null;

            foreach (var o in passOffsets)
            {
                ApplySmooth(point.x + o.x, easedY, point.z + o.y, TEAR_REPAIR_BLEND_RADIUS, modified);
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Tear repair at {point} -> centerY={centerY:F2}, medianY={medianY:F2}, targetY={easedY:F2}, ops={opCount}, chunks={modified.Count}.");
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
        /// the visible terrain change boundary.
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
            // Snapshot a generous boundary around the full leveled-area footprint
            // (inner pad + level radius) so Undo can restore all modified chunks.
            int pad = INNER_PAD + Mathf.CeilToInt(LEVEL_RADIUS) + 4;
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

        private static void ApplySmooth(float x, float y, float z, float radius,
                                        HashSet<TerrainComp> modified)
        {
            var go = new GameObject("VFP_SmoothOp");
            go.transform.position = new Vector3(x, y, z);
            var op = go.AddComponent<TerrainOp>();
            op.m_settings.m_level = false;
            op.m_settings.m_smooth = true;
            op.m_settings.m_smoothRadius = radius;

            var probes = new Vector3[]
            {
                new Vector3(x,          y, z         ),
                new Vector3(x - radius, y, z - radius),
                new Vector3(x + radius, y, z - radius),
                new Vector3(x - radius, y, z + radius),
                new Vector3(x + radius, y, z + radius),
                new Vector3(x - radius, y, z         ),
                new Vector3(x + radius, y, z         ),
                new Vector3(x,          y, z - radius),
                new Vector3(x,          y, z + radius),
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

        private static bool IsInEdgeBand(
            float x, float z,
            float minX, float maxX, float minZ, float maxZ,
            float bandWidth)
        {
            return (x - minX) <= bandWidth ||
                   (maxX - x) <= bandWidth ||
                   (z - minZ) <= bandWidth ||
                   (maxZ - z) <= bandWidth;
        }
    }
}

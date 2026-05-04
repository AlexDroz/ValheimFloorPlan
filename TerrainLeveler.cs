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
        public enum EdgeRiskLevel
        {
            Low,
            Medium,
            High,
        }

        private struct EdgeRiskHotspot
        {
            public Vector3 Position;
            public float Score;

            public EdgeRiskHotspot(Vector3 position, float score)
            {
                Position = position;
                Score = score;
            }
        }

        // ── inner pad ─────────────────────────────────────────────────────────
        // LEVEL_RADIUS is now read from config (TerrainStampRadius) at runtime.
        private static float LEVEL_RADIUS => Mathf.Clamp(ValheimFloorPlanPlugin.TerrainStampRadius, 3.0f, 6.0f);
        private const float PRE_SAMPLE_STEP   = 0.5f;  // denser scan catches narrow local highs
        private const float LEVEL_SAMPLE_STEP = 0.5f;  // denser ops reduce edge misses on slopes
        private const float EDGE_LEVEL_SAMPLE_STEP = 0.25f;
        private const float EDGE_BAND_WIDTH = 0.6f;
        private const float EDGE_LEVEL_RADIUS = 1.8f;
        private const float EDGE_RAISE_EPSILON = 0.03f;
        private const float STAGE_RAISE_EPSILON = 0.02f;
        private const float SPIKE_SCAN_STEP   = 0.5f;
        private const float SPIKE_LEVEL_RADIUS = 1.5f;
        private const float SPIKE_TOLERANCE   = 0.2f;
        private const float TEAR_REPAIR_SAMPLE_RADIUS = 1.2f;
        private const float TEAR_REPAIR_MAIN_RADIUS = 1.1f;
        private const float TEAR_REPAIR_BLEND_RADIUS = 0.7f;
        private const float TEAR_REPAIR_SECOND_BLEND_RADIUS = 1.05f;
        private const float TEAR_REPAIR_MAX_LOWER = 0.45f;
        private const float TEAR_REPAIR_SPIKE_THRESHOLD = 0.28f;
        private const float TEAR_REPAIR_CUT_RADIUS = 0.42f;
        private const float TEAR_REPAIR_RAISE_TOLERANCE = 0.01f;
        private const float TEAR_REPAIR_MIN_RELIEF = 0.08f;
        private const float TEAR_REPAIR_EDGE_RADIUS = 0.48f;
        private const float TEAR_REPAIR_EDGE_LENGTH = 1.25f;
        private const float TEAR_REPAIR_EDGE_STEP = 0.32f;
        private const float TEAR_REPAIR_EDGE_MAX_RAISE = 0.14f;
        private const float TEAR_REPAIR_EDGE_MEDIAN_HEADROOM = 0.08f;
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
                    float ldx = lx - origin.x;
                    float ldz = lz - origin.z;
                    float wx = origin.x + ldx * cosR + ldz * sinR;
                    float wz = origin.z - ldx * sinR + ldz * cosR;
                    float h = SampleHeight(wx, wz, origin.y);
                    if (h > maxY) maxY = h;
                    if (h < minY) minY = h;
                }
            }

            if (maxY == float.MinValue)
            {
                maxY = origin.y;
                minY = origin.y;
            }

            float range = maxY - minY;
            float targetY = maxY;
            TargetLevelY = targetY;

            if (range > WARN_RAISE)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[TerrainLeveler] Footprint height range {range:F1}m — steep ground," +
                    " cliff-edge tears may appear on the downhill side.");
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Raising pad to Y={targetY:F2}  minY={minY:F2}  range={range:F1}m" +
                $"  rotation={rotationDeg:F0}°" +
                $"  inner(local)=[{innerMinX:F1}..{innerMaxX:F1}] x [{innerMinZ:F1}..{innerMaxZ:F1}]");

            int totalPasses = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainLevelPasses, 1, 5);
            bool stagedRaise = ValheimFloorPlanPlugin.TerrainUseStagedRaise;
            float raiseStepHeight = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainRaiseStepHeight, 0.15f, 1.5f);
            int maxRaiseStages = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainMaxRaiseStages, 1, 16);
            bool skipSatisfiedCenterStamps = ValheimFloorPlanPlugin.TerrainSkipSatisfiedCenterStamps;
            int stageCount = 1;
            if (stagedRaise && range > STAGE_RAISE_EPSILON)
                stageCount = Mathf.Clamp(Mathf.CeilToInt(range / raiseStepHeight), 1, maxRaiseStages);

            float stageHeight = stageCount > 0 ? range / stageCount : range;

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
            int totalLevelOps = stageCount * totalPasses * (((stepsX + 1) * (stepsZ + 1)) + edgePointsPerPass);
            int nextLevelPct = 10;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Running {totalPasses} leveling pass(es) across {stageCount} raise stage(s) " +
                $"(range={range:F1}m, staged={stagedRaise}, stageStep={raiseStepHeight:F2}m).");
            ShowProgress($"Leveling terrain... 0% ({totalPasses} pass(es), {stageCount} stage(s))");

            for (int stage = 1; stage <= stageCount; stage++)
            {
                float stageTargetY = stage == stageCount
                    ? targetY
                    : Mathf.Min(targetY, minY + (stageHeight * stage));

                for (int pass = 1; pass <= totalPasses; pass++)
                {
                    float effectiveEdgeRadius = Mathf.Min(EDGE_LEVEL_RADIUS, LEVEL_RADIUS);

                    for (int ix = 0; ix <= stepsX; ix++)
                    {
                        float lx = (ix == stepsX) ? innerMaxX : innerMinX + ix * levelSampleStep;
                        for (int iz = 0; iz <= stepsZ; iz++)
                        {
                            float lz = (iz == stepsZ) ? innerMaxZ : innerMinZ + iz * levelSampleStep;
                            float ldx2 = lx - origin.x;
                            float ldz2 = lz - origin.z;
                            float wx = origin.x + ldx2 * cosR + ldz2 * sinR;
                            float wz = origin.z - ldx2 * sinR + ldz2 * cosR;

                            float h = SampleHeight(wx, wz, stageTargetY);
                            if (!skipSatisfiedCenterStamps || h < stageTargetY - STAGE_RAISE_EPSILON)
                            {
                                ApplyLevel(wx, stageTargetY, wz, LEVEL_RADIUS, modified);
                            }

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

                    for (int ix = 0; ix <= edgeStepsX; ix++)
                    {
                        float lx = (ix == edgeStepsX) ? innerMaxX : innerMinX + ix * edgeSampleStep;
                        for (int iz = 0; iz <= edgeStepsZ; iz++)
                        {
                            float lz = (iz == edgeStepsZ) ? innerMaxZ : innerMinZ + iz * edgeSampleStep;
                            if (!IsInEdgeBand(lx, lz, innerMinX, innerMaxX, innerMinZ, innerMaxZ, EDGE_BAND_WIDTH))
                                continue;

                            float ldx2 = lx - origin.x;
                            float ldz2 = lz - origin.z;
                            float wx = origin.x + ldx2 * cosR + ldz2 * sinR;
                            float wz = origin.z - ldx2 * sinR + ldz2 * cosR;

                            float h = SampleHeight(wx, wz, stageTargetY);
                            if (h >= stageTargetY - EDGE_RAISE_EPSILON)
                                continue;

                            ApplyLevel(wx, stageTargetY, wz, effectiveEdgeRadius, modified);
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

                if (stage < stageCount)
                    yield return new WaitForSeconds(0.08f);
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
        /// Checks whether a selected point has a safe tear-like slope profile.
        /// Used by UI to drive valid/invalid target indicator state.
        /// </summary>
        public static bool IsRepairTargetValid(Vector3 point, out string reason)
        {
            float centerY = SampleHeight(point.x, point.z, point.y);
            var samples = CollectRepairSamples(point, centerY, out _, out _, out _);
            if (samples.Count == 0)
            {
                reason = "Unable to sample terrain.";
                return false;
            }

            samples.Sort();
            float minY = samples[0];
            float maxY = samples[samples.Count - 1];
            float relief = maxY - minY;

            // Accept any slope including steep/near-vertical faces — those are the actual tear targets.
            // Only reject completely flat terrain where there is nothing to smooth.
            if (relief < TEAR_REPAIR_MIN_RELIEF)
            {
                reason = "Area is too flat — no tear detected here.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Repairs torn trench faces around a selected point.
        /// Includes a controlled edge-anchored bridge pass for cases where one side has no backing terrain.
        /// </summary>
        public static IEnumerator RepairTearAtPoint(Vector3 point)
        {
            var modified = new HashSet<TerrainComp>();

            float centerY = SampleHeight(point.x, point.z, point.y);
            Vector2 slopeDir;
            float ridgeY;
            float troughY;
            var samples = CollectRepairSamples(point, centerY, out slopeDir, out ridgeY, out troughY);
            samples.Sort();

            float medianY = samples.Count > 0 ? samples[samples.Count / 2] : centerY;
            float lowerTargetY = Mathf.Min(centerY, medianY + 0.03f);
            lowerTargetY = Mathf.Max(centerY - TEAR_REPAIR_MAX_LOWER, lowerTargetY);

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
                ApplySmooth(point.x + o.x, lowerTargetY, point.z + o.y, TEAR_REPAIR_MAIN_RADIUS, modified);
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            yield return null;

            foreach (var o in passOffsets)
            {
                ApplySmooth(point.x + o.x, lowerTargetY, point.z + o.y, TEAR_REPAIR_BLEND_RADIUS, modified);
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            yield return null;

            int bridgeOps = ApplyEdgeBridge(point, lowerTargetY, slopeDir, ridgeY, troughY, modified);
            opCount += bridgeOps;

            if (opCount % OPS_PER_FRAME == 0) yield return null;

            yield return null;

            int cutOps = 0;
            foreach (var o in passOffsets)
            {
                float sx = point.x + o.x;
                float sz = point.z + o.y;
                float currentY = SampleHeight(sx, sz, lowerTargetY);
                float localMedianY = GetLocalMedianHeight(sx, sz, currentY, 0.6f);
                if (currentY <= localMedianY + TEAR_REPAIR_SPIKE_THRESHOLD)
                    continue;

                float cutY = Mathf.Min(currentY - 0.03f, localMedianY + 0.02f);
                cutY = Mathf.Max(lowerTargetY, cutY);

                if (!CanLowerWithoutRaise(sx, sz, cutY, TEAR_REPAIR_CUT_RADIUS, currentY))
                    continue;

                ApplyLevel(sx, cutY, sz, TEAR_REPAIR_CUT_RADIUS, modified, smooth: true);
                cutOps++;
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            yield return null;

            foreach (var o in passOffsets)
            {
                ApplySmooth(point.x + o.x, lowerTargetY, point.z + o.y, TEAR_REPAIR_SECOND_BLEND_RADIUS, modified);
                opCount++;
                if (opCount % OPS_PER_FRAME == 0) yield return null;
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainLeveler] Tear repair at {point} -> centerY={centerY:F2}, medianY={medianY:F2}, lowerTargetY={lowerTargetY:F2}, bridgeOps={bridgeOps}, cutOps={cutOps}, ops={opCount}, chunks={modified.Count}.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Rates terrain edge quality around the modified footprint before building.
        /// Used by preview mode to warn when edge irregularity is likely to cause tears/spikes.
        /// </summary>
        public static EdgeRiskLevel EvaluateEdgeRisk(
            FloorPlan plan,
            Vector3 origin,
            float rotationDeg,
            out float edgeRelief,
            out float irregularity,
            out float maxEdgeStep,
            List<Vector3>? hotspotPoints = null)
        {
            GetBounds(plan, origin, INNER_PAD, 0f,
                out float innerMinX, out float innerMaxX,
                out float innerMinZ, out float innerMaxZ);

            float outerMinX = innerMinX - LEVEL_RADIUS;
            float outerMaxX = innerMaxX + LEVEL_RADIUS;
            float outerMinZ = innerMinZ - LEVEL_RADIUS;
            float outerMaxZ = innerMaxZ + LEVEL_RADIUS;

            float cosR = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            float sinR = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);

            float edgeMinY = float.MaxValue;
            float edgeMaxY = float.MinValue;
            float roughAccum = 0f;
            int roughCount = 0;
            float localMaxEdgeStep = 0f;
            List<EdgeRiskHotspot>? hotspots = hotspotPoints != null ? new List<EdgeRiskHotspot>(64) : null;

            const float edgeSampleStep = 0.75f;
            const float crossProbeDist = 0.8f;

            SampleEdgeRiskLine(outerMinX, outerMinZ, outerMinX, outerMaxZ, 1f, 0f);   // west
            SampleEdgeRiskLine(outerMaxX, outerMinZ, outerMaxX, outerMaxZ, -1f, 0f);  // east
            SampleEdgeRiskLine(outerMinX, outerMinZ, outerMaxX, outerMinZ, 0f, 1f);   // south
            SampleEdgeRiskLine(outerMinX, outerMaxZ, outerMaxX, outerMaxZ, 0f, -1f);  // north

            if (edgeMaxY == float.MinValue || edgeMinY == float.MaxValue || roughCount == 0)
            {
                edgeRelief = 0f;
                irregularity = 0f;
                maxEdgeStep = 0f;
                return EdgeRiskLevel.Low;
            }

            edgeRelief = edgeMaxY - edgeMinY;
            irregularity = roughAccum / roughCount;
            maxEdgeStep = localMaxEdgeStep;

            if (hotspotPoints != null)
            {
                hotspotPoints.Clear();
                if (hotspots != null && hotspots.Count > 0)
                {
                    hotspots.Sort((a, b) => b.Score.CompareTo(a.Score));
                    const float minSpacing = 1.6f;
                    const int maxHotspots = 12;

                    for (int i = 0; i < hotspots.Count && hotspotPoints.Count < maxHotspots; i++)
                    {
                        Vector3 p = hotspots[i].Position;
                        bool tooClose = false;
                        for (int j = 0; j < hotspotPoints.Count; j++)
                        {
                            Vector3 q = hotspotPoints[j];
                            float dx = p.x - q.x;
                            float dz = p.z - q.z;
                            if ((dx * dx + dz * dz) < (minSpacing * minSpacing))
                            {
                                tooClose = true;
                                break;
                            }
                        }

                        if (!tooClose)
                            hotspotPoints.Add(p);
                    }
                }
            }

            if (maxEdgeStep >= 1.4f || irregularity >= 0.55f || edgeRelief >= 5.0f)
                return EdgeRiskLevel.High;
            if (maxEdgeStep >= 0.9f || irregularity >= 0.32f || edgeRelief >= 3.0f)
                return EdgeRiskLevel.Medium;
            return EdgeRiskLevel.Low;

            void SampleEdgeRiskLine(float x0, float z0, float x1, float z1, float inNx, float inNz)
            {
                float dx = x1 - x0;
                float dz = z1 - z0;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / edgeSampleStep));

                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float lx = Mathf.Lerp(x0, x1, t);
                    float lz = Mathf.Lerp(z0, z1, t);

                    float ex = lx - origin.x;
                    float ez = lz - origin.z;
                    float wx = origin.x + ex * cosR + ez * sinR;
                    float wz = origin.z - ex * sinR + ez * cosR;

                    float inLx = lx + inNx * crossProbeDist;
                    float inLz = lz + inNz * crossProbeDist;
                    float inDx = inLx - origin.x;
                    float inDz = inLz - origin.z;
                    float inWx = origin.x + inDx * cosR + inDz * sinR;
                    float inWz = origin.z - inDx * sinR + inDz * cosR;

                    float outLx = lx - inNx * crossProbeDist;
                    float outLz = lz - inNz * crossProbeDist;
                    float outDx = outLx - origin.x;
                    float outDz = outLz - origin.z;
                    float outWx = origin.x + outDx * cosR + outDz * sinR;
                    float outWz = origin.z - outDx * sinR + outDz * cosR;

                    float hEdge = SampleHeight(wx, wz, origin.y);
                    float hIn = SampleHeight(inWx, inWz, hEdge);
                    float hOut = SampleHeight(outWx, outWz, hEdge);

                    if (hEdge < edgeMinY) edgeMinY = hEdge;
                    if (hEdge > edgeMaxY) edgeMaxY = hEdge;

                    float step = Mathf.Abs(hIn - hOut);
                    if (step > localMaxEdgeStep) localMaxEdgeStep = step;

                    float localRough = Mathf.Abs(hEdge - (hIn + hOut) * 0.5f);
                    roughAccum += localRough;
                    roughCount++;

                    if (hotspots != null)
                    {
                        float score = step + localRough * 1.35f;
                        if (score >= 0.62f && (step >= 0.5f || localRough >= 0.24f))
                            hotspots.Add(new EdgeRiskHotspot(new Vector3(wx, hEdge, wz), score));
                    }
                }
            }
        }

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

        private static List<float> CollectRepairSamples(
            Vector3 point,
            float referenceY,
            out Vector2 slopeDir,
            out float ridgeY,
            out float troughY)
        {
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

            slopeDir = Vector2.zero;
            ridgeY = float.MinValue;
            troughY = float.MaxValue;
            Vector2 ridgeOffset = Vector2.zero;
            Vector2 troughOffset = Vector2.zero;

            foreach (var o in sampleOffsets)
            {
                float h = SampleHeight(point.x + o.x, point.z + o.y, referenceY);
                samples.Add(h);

                if (h > ridgeY)
                {
                    ridgeY = h;
                    ridgeOffset = o;
                }

                if (h < troughY)
                {
                    troughY = h;
                    troughOffset = o;
                }
            }

            Vector2 delta = troughOffset - ridgeOffset;
            if (delta.sqrMagnitude > 0.0001f)
                slopeDir = delta.normalized;
            else
                slopeDir = Vector2.right;

            return samples;
        }

        private static int ApplyEdgeBridge(
            Vector3 point,
            float anchorY,
            Vector2 slopeDir,
            float ridgeY,
            float troughY,
            HashSet<TerrainComp> modified)
        {
            float relief = Mathf.Max(0f, ridgeY - troughY);
            if (relief < TEAR_REPAIR_MIN_RELIEF)
                return 0;

            float fillDepth = Mathf.Clamp(relief * 0.45f, 0.14f, 0.55f);
            float endY = anchorY - fillDepth;
            int steps = Mathf.Clamp(Mathf.CeilToInt(TEAR_REPAIR_EDGE_LENGTH / TEAR_REPAIR_EDGE_STEP), 2, 6);
            int ops = 0;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float distance = t * TEAR_REPAIR_EDGE_LENGTH;
                float px = point.x + slopeDir.x * distance;
                float pz = point.z + slopeDir.y * distance;

                float targetY = Mathf.Lerp(anchorY, endY, t);
                float currentY = SampleHeight(px, pz, anchorY);
                float localMedianY = GetLocalMedianHeight(px, pz, currentY, 0.45f);

                // Hard anti-spike clamps: never rise far above local terrain consensus.
                float maxRaiseY = currentY + TEAR_REPAIR_EDGE_MAX_RAISE;
                float maxMedianY = localMedianY + TEAR_REPAIR_EDGE_MEDIAN_HEADROOM;
                targetY = Mathf.Min(targetY, maxRaiseY);
                targetY = Mathf.Min(targetY, maxMedianY);
                targetY = Mathf.Min(targetY, anchorY + 0.04f);

                float radius = Mathf.Lerp(TEAR_REPAIR_EDGE_RADIUS * 0.85f, TEAR_REPAIR_EDGE_RADIUS, t);
                ApplyLevel(px, targetY, pz, radius, modified, smooth: true);
                ops++;
            }

            return ops;
        }

        private static float GetLocalMedianHeight(float x, float z, float referenceY, float radius)
        {
            var vals = new List<float>(9)
            {
                SampleHeight(x, z, referenceY),
                SampleHeight(x + radius, z, referenceY),
                SampleHeight(x - radius, z, referenceY),
                SampleHeight(x, z + radius, referenceY),
                SampleHeight(x, z - radius, referenceY),
                SampleHeight(x + radius, z + radius, referenceY),
                SampleHeight(x + radius, z - radius, referenceY),
                SampleHeight(x - radius, z + radius, referenceY),
                SampleHeight(x - radius, z - radius, referenceY),
            };
            vals.Sort();
            return vals[vals.Count / 2];
        }

        private static bool CanLowerWithoutRaise(float x, float z, float targetY, float radius, float referenceY)
        {
            var probes = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(-radius, -radius),
                new Vector2( radius, -radius),
                new Vector2(-radius,  radius),
                new Vector2( radius,  radius),
                new Vector2(-radius, 0f),
                new Vector2( radius, 0f),
                new Vector2(0f, -radius),
                new Vector2(0f,  radius),
            };

            for (int i = 0; i < probes.Length; i++)
            {
                float h = SampleHeight(x + probes[i].x, z + probes[i].y, referenceY);
                if (targetY > h + TEAR_REPAIR_RAISE_TOLERANCE)
                    return false;
            }

            return true;
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

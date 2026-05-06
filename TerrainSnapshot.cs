using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValheimFloorPlan
{
    /// <summary>
    /// Captures the raw height-delta arrays from every TerrainComp in a region
    /// before any leveling is applied, and can restore them exactly on undo.
    ///
    /// TerrainComp stores modifications as a float[] m_levelDelta and a bool[] m_modifiedHeight.
    /// Chunks that were never modified can have one or both arrays as null; we must
    /// preserve that null state as part of the snapshot so pristine terrain can be restored.
    /// After restoring, we call TerrainComp.Save() / ZNetView.GetZDO() to persist.
    /// </summary>
    public static class TerrainSnapshot
    {
        private static readonly FieldInfo _levelDeltaField =
            typeof(TerrainComp).GetField("m_levelDelta", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _modifiedHeightField =
            typeof(TerrainComp).GetField("m_modifiedHeight", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _nviewField =
            typeof(TerrainComp).GetField("m_nview", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _saveMethod =
            typeof(TerrainComp).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic);
        private const float TERRAIN_CHUNK_SIZE = 64f;
        private const float TERRAIN_CHUNK_HALF = TERRAIN_CHUNK_SIZE * 0.5f;

        // One saved chunk.
        private struct ChunkState
        {
            public TerrainComp Comp;
            public float[]     LevelDelta;
            public bool[]      ModifiedHeight;
            public bool        HadLevelDelta;
            public bool        HadModifiedHeight;
        }

        // The most recent snapshot, keyed by TerrainComp instance ID.
        private static readonly List<ChunkState> _saved = new List<ChunkState>();
        private static readonly HashSet<int> _savedIds = new HashSet<int>();
        private static int _initialCaptureCount = 0;
        private static int _onDemandCaptureCount = 0;

        /// <summary>
        /// Capture a snapshot of all TerrainComp chunks that overlap the given
        /// world-space bounding rectangle.  Call this BEFORE any terrain ops.
        /// </summary>
        public static void Capture(float minX, float maxX, float minZ, float maxZ, float referenceY)
        {
            _saved.Clear();
            _savedIds.Clear();
            _initialCaptureCount = 0;
            _onDemandCaptureCount = 0;

            if (_levelDeltaField == null || _modifiedHeightField == null || _saveMethod == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    "[TerrainSnapshot] Reflection fields not found — undo will only remove pieces.");
                return;
            }

            // Prefer enumerating loaded TerrainComp instances by XZ overlap.
            // This is more robust than single-height probes on extreme cliffs.
            var seen = new HashSet<int>();
            #pragma warning disable CS0618
            var loaded = UnityEngine.Object.FindObjectsOfType<TerrainComp>() ?? System.Array.Empty<TerrainComp>();
            #pragma warning restore CS0618
            int loadedCount = loaded.Length;
            int overlapCount = 0;
            int probeHitCount = 0;
            foreach (var tc in loaded)
            {
                if (tc == null) continue;
                Vector3 p = tc.transform.position;
                if (!OverlapsXZ(p.x - TERRAIN_CHUNK_HALF, p.x + TERRAIN_CHUNK_HALF,
                                p.z - TERRAIN_CHUNK_HALF, p.z + TERRAIN_CHUNK_HALF,
                                minX, maxX, minZ, maxZ))
                    continue;

                overlapCount++;
                TrySaveChunk(tc, seen);
            }

            _initialCaptureCount = _saved.Count;

            // Fallback probe sweep to catch any chunk not represented in loaded list overlap.
            // Use multiple Y levels to avoid vertical edge cases in TerrainComp lookup.
            float step = 8f;
            float[] probeY = new float[] { referenceY, referenceY + 128f, referenceY - 128f };
            for (float x = minX; x <= maxX + 0.01f; x += step)
                for (float z = minZ; z <= maxZ + 0.01f; z += step)
                    for (int i = 0; i < probeY.Length; i++)
                    {
                        var tc = TerrainComp.FindTerrainCompiler(new Vector3(x, probeY[i], z));
                        if (tc == null) continue;
                        probeHitCount++;
                        TrySaveChunk(tc, seen);
                    }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] Captured {_saved.Count} terrain chunk(s).");

            if (_initialCaptureCount > 0 || (_initialCaptureCount == 0 && _saved.Count > 0))
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[TerrainSnapshot] Breakdown: {_initialCaptureCount} from initial broad scan, {_saved.Count - _initialCaptureCount} from fallback probes.");

            if (_saved.Count == 0)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[TerrainSnapshot] Capture found no terrain chunks. loaded={loadedCount}, overlap={overlapCount}, probeHits={probeHitCount}, bounds=([{minX:F1}..{maxX:F1}] x [{minZ:F1}..{maxZ:F1}]), refY={referenceY:F1}");
            }
        }

        /// <summary>Returns true if there is a snapshot available to restore.</summary>
        public static bool HasSnapshot => _saved.Count > 0;

        /// <summary>Returns the number of terrain chunks in the current snapshot.</summary>
        public static int GetSnapshotChunkCount() => _saved.Count;

        /// <summary>
        /// Ensures a specific chunk is captured exactly once in the current snapshot.
        /// Call this immediately before mutating terrain so late-loaded chunks are still restorable.
        /// </summary>
        public static void EnsureCaptured(TerrainComp tc)
        {
            if (tc == null) return;
            if (_levelDeltaField == null || _modifiedHeightField == null) return;

            int id = tc.GetInstanceID();
            if (_savedIds.Contains(id)) return;

            var ld = _levelDeltaField.GetValue(tc) as float[];
            var mh = _modifiedHeightField.GetValue(tc) as bool[];

            _saved.Add(new ChunkState
            {
                Comp = tc,
                LevelDelta = ld != null ? (float[])ld.Clone() : System.Array.Empty<float>(),
                ModifiedHeight = mh != null ? (bool[])mh.Clone() : System.Array.Empty<bool>(),
                HadLevelDelta = ld != null,
                HadModifiedHeight = mh != null,
            });
            _savedIds.Add(id);
            _onDemandCaptureCount++;
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] On-demand capture #{_onDemandCaptureCount}: added chunk #{id} (total now: {_saved.Count})");
        }

        /// <summary>
        /// Restore every captured chunk to its pre-build state and persist the changes.
        /// </summary>
        public static void Restore()
        {
            if (_saved.Count == 0)
            {
                ValheimFloorPlanPlugin.Log.LogWarning("[TerrainSnapshot] Nothing to restore.");
                return;
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] Restore: {_saved.Count} total chunks " +
                $"({_initialCaptureCount} initial + {_onDemandCaptureCount} on-demand)");

            int restored = 0;
            foreach (var state in _saved)
            {
                if (state.Comp == null) continue;
                try
                {
                    _levelDeltaField.SetValue(
                        state.Comp,
                        state.HadLevelDelta && state.LevelDelta != null
                            ? (float[])state.LevelDelta.Clone()
                            : null);
                    _modifiedHeightField.SetValue(
                        state.Comp,
                        state.HadModifiedHeight && state.ModifiedHeight != null
                            ? (bool[])state.ModifiedHeight.Clone()
                            : null);

                    // Call the private Save() via reflection — this writes the restored
                    // arrays back to the ZDO so the change persists on next world save.
                    _saveMethod.Invoke(state.Comp, null);

                    // Force local visuals/physics to rebuild immediately where possible.
                    var hmap = state.Comp.GetComponent<Heightmap>() ?? state.Comp.GetComponentInParent<Heightmap>();
                    if (hmap != null)
                        hmap.Poke(false);

                    restored++;
                }
                catch (System.Exception ex)
                {
                    ValheimFloorPlanPlugin.Log.LogError(
                        $"[TerrainSnapshot] Failed to restore chunk #{state.Comp.GetInstanceID()}: {ex.Message}");
                }
            }

            _saved.Clear();
            _savedIds.Clear();
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] Restored {restored} terrain chunk(s).");
        }

        private static void TrySaveChunk(TerrainComp tc, HashSet<int> seen)
        {
            int id = tc.GetInstanceID();
            if (!seen.Add(id)) return;
            if (_savedIds.Contains(id)) return;

            var ld = _levelDeltaField.GetValue(tc) as float[];
            var mh = _modifiedHeightField.GetValue(tc) as bool[];

            _saved.Add(new ChunkState
            {
                Comp = tc,
                LevelDelta = ld != null ? (float[])ld.Clone() : System.Array.Empty<float>(),
                ModifiedHeight = mh != null ? (bool[])mh.Clone() : System.Array.Empty<bool>(),
                HadLevelDelta = ld != null,
                HadModifiedHeight = mh != null,
            });
            _savedIds.Add(id);
        }

        private static bool OverlapsXZ(
            float aMinX, float aMaxX, float aMinZ, float aMaxZ,
            float bMinX, float bMaxX, float bMinZ, float bMaxZ)
        {
            if (aMaxX < bMinX || aMinX > bMaxX) return false;
            if (aMaxZ < bMinZ || aMinZ > bMaxZ) return false;
            return true;
        }
    }
}

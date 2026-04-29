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
    /// We snapshot copies of those arrays and write them back via reflection on undo.
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

        // One saved chunk.
        private struct ChunkState
        {
            public TerrainComp Comp;
            public float[]     LevelDelta;
            public bool[]      ModifiedHeight;
        }

        // The most recent snapshot, keyed by TerrainComp instance ID.
        private static readonly List<ChunkState> _saved = new List<ChunkState>();

        /// <summary>
        /// Capture a snapshot of all TerrainComp chunks that overlap the given
        /// world-space bounding rectangle.  Call this BEFORE any terrain ops.
        /// </summary>
        public static void Capture(float minX, float maxX, float minZ, float maxZ, float referenceY)
        {
            _saved.Clear();

            if (_levelDeltaField == null || _modifiedHeightField == null || _saveMethod == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    "[TerrainSnapshot] Reflection fields not found — undo will only remove pieces.");
                return;
            }

            // Sample a grid of points across the region; FindTerrainCompiler returns
            // the chunk that owns each point.  Deduplicate by instance ID.
            var seen = new HashSet<int>();
            float step = 8f; // TerrainComp chunks are 64m wide; 8m sampling catches all of them.
            for (float x = minX; x <= maxX + 0.01f; x += step)
                for (float z = minZ; z <= maxZ + 0.01f; z += step)
                {
                    var tc = TerrainComp.FindTerrainCompiler(new Vector3(x, referenceY, z));
                    if (tc == null) continue;
                    int id = tc.GetInstanceID();
                    if (!seen.Add(id)) continue;

                    var ld = _levelDeltaField.GetValue(tc) as float[];
                    var mh = _modifiedHeightField.GetValue(tc) as bool[];
                    if (ld == null || mh == null) continue;

                    _saved.Add(new ChunkState
                    {
                        Comp           = tc,
                        LevelDelta     = (float[])ld.Clone(),
                        ModifiedHeight = (bool[])mh.Clone(),
                    });
                }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] Captured {_saved.Count} terrain chunk(s).");
        }

        /// <summary>Returns true if there is a snapshot available to restore.</summary>
        public static bool HasSnapshot => _saved.Count > 0;

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

            int restored = 0;
            foreach (var state in _saved)
            {
                if (state.Comp == null) continue;

                _levelDeltaField.SetValue(state.Comp, (float[])state.LevelDelta.Clone());
                _modifiedHeightField.SetValue(state.Comp, (bool[])state.ModifiedHeight.Clone());

                // Call the private Save() via reflection — this writes the restored
                // arrays back to the ZDO so the change persists on next world save.
                _saveMethod.Invoke(state.Comp, null);

                restored++;
            }

            _saved.Clear();
            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[TerrainSnapshot] Restored {restored} terrain chunk(s).");
        }
    }
}

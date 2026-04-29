using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimFloorPlan
{
    /// <summary>
    /// Reads a .vfp floor plan file and spawns Valheim build pieces into the world
    /// at the player's current position as the origin (col=0, row=0).
    ///
    /// Coordinate mapping:
    ///   col  -> +X axis (east)
    ///   row  -> +Z axis (north)
    ///   Y    -> terrain height sampled at each position
    ///
    /// Each cell = 2m (CELL_SIZE). Pieces are centred within their cell footprint.
    /// </summary>
    public class FloorPlanBuilder : MonoBehaviour
    {
        private const float PLACE_DELAY = 0.05f; // seconds between spawns to avoid lag spikes

        public static FloorPlanBuilder Instance { get; private set; } = null!;

        // All GameObjects spawned in the last build — used by Undo().
        private readonly List<GameObject> _lastPlaced = new List<GameObject>();
        // Whether an undo snapshot is available.
        public bool CanUndo => _lastPlaced.Count > 0 || TerrainSnapshot.HasSnapshot;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Removes all pieces from the last build and restores terrain to its pre-build state.
        /// </summary>
        public void Undo()
        {
            if (!CanUndo)
            {
                ValheimFloorPlanPlugin.Log.LogWarning("[FloorPlanBuilder] Nothing to undo.");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                    "ValheimFloorPlan: Nothing to undo.");
                return;
            }

            // Destroy all placed pieces.
            int removed = 0;
            foreach (var go in _lastPlaced)
                if (go != null) { ZNetScene.instance.Destroy(go); removed++; }
            _lastPlaced.Clear();

            // Restore terrain.
            TerrainSnapshot.Restore();

            ValheimFloorPlanPlugin.Log.LogInfo($"[FloorPlanBuilder] Undo: removed {removed} pieces.");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                $"ValheimFloorPlan: Undone ({removed} pieces removed).");
        }

        public void BuildFromFile(string path)
        {
            FloorPlan plan;
            try
            {
                plan = FloorPlan.Load(path);
            }
            catch (System.Exception ex)
            {
                ValheimFloorPlanPlugin.Log.LogError($"Failed to load floor plan: {ex.Message}");
                return;
            }

            ValheimFloorPlanPlugin.Log.LogInfo($"Building floor plan: {plan.Pieces.Count} pieces from {path}");
            StartCoroutine(LevelThenPlace(plan));
        }

        private IEnumerator LevelThenPlace(FloorPlan plan)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogError("No local player found.");
                yield break;
            }

            Vector3 origin = player.transform.position;
            ValheimFloorPlanPlugin.Log.LogInfo($"Build origin: {origin}");

            // Clear any previous undo state.
            _lastPlaced.Clear();

            // Snapshot terrain BEFORE any leveling so Undo() can restore it.
            TerrainLeveler.GetSnapshotBounds(plan, origin,
                out float sMinX, out float sMaxX, out float sMinZ, out float sMaxZ);
            TerrainSnapshot.Capture(sMinX, sMaxX, sMinZ, sMaxZ, origin.y);

            player.Message(MessageHud.MessageType.Center, "Leveling terrain...");
            yield return StartCoroutine(TerrainLeveler.LevelForPlan(plan, origin));
            player.Message(MessageHud.MessageType.Center, "Placing floor plan pieces...");

            yield return StartCoroutine(PlacePieces(plan, origin, TerrainLeveler.RecommendedPlacementWait));
        }

        private IEnumerator PlacePieces(FloorPlan plan, Vector3 origin, float settleWait = 2.0f)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogError("No local player found during placement.");
                yield break;
            }

            int placed = 0;
            int skipped = 0;
            Vector3 firstPos = Vector3.zero;

            // Wait for terrain mesh to fully settle after leveling before pieces are placed.
            ValheimFloorPlanPlugin.Log.LogInfo($"[FloorPlanBuilder] Waiting {settleWait:F1}s for terrain to settle...");
            yield return new WaitForSeconds(settleWait);

            foreach (var piece in plan.Pieces)
            {
                var def = PieceMap.GetDef(piece.Type);
                if (def == null)
                {
                    ValheimFloorPlanPlugin.Log.LogWarning($"Unknown piece type '{piece.Type}' — skipped.");
                    skipped++;
                    continue;
                }

                var prefab = ZNetScene.instance?.GetPrefab(def.Prefab);
                if (prefab == null)
                {
                    ValheimFloorPlanPlugin.Log.LogWarning($"Prefab '{def.Prefab}' not found in ZNetScene — skipped.");
                    skipped++;
                    continue;
                }

                // Effective dimensions after applying rotation (90/270 swaps W and H),
                // matching the B4J designer's EffW / EffH logic exactly.
                int effW = def.EffW(piece.Rotation);
                int effH = def.EffH(piece.Rotation);

                // Convert from top-left grid corner (B4J storage) to world centre.
                // col/row + half the effective cell footprint = centre cell position.
                float x = origin.x + (piece.Col + effW * 0.5f) * PieceMap.CELL_SIZE;
                float z = origin.z + (piece.Row + effH * 0.5f) * PieceMap.CELL_SIZE;

                // Sample terrain height at this XZ position (layer 11 = terrain).
                // Cast from well above the pad to ensure we hit the raised surface.
                float terrainY = origin.y;
                if (Physics.Raycast(new Vector3(x, origin.y + 300f, z), Vector3.down, out var hit, 600f,
                    1 << 11))
                    terrainY = hit.point.y;

                // Place centre: floors sit on the ground (YOffset=0),
                // walls / pillars are 2 m tall so their centre is 1 m above ground (YOffset=1).
                float y = terrainY + def.YOffset;

                var pos = new Vector3(x, y, z);
                var rot = Quaternion.Euler(0, piece.Rotation, 0);

                if (placed == 0)
                {
                    firstPos = pos;
                    ValheimFloorPlanPlugin.Log.LogInfo($"First piece: type={piece.Type} prefab={def.Prefab} pos={pos}");
                }

                var go = UnityEngine.Object.Instantiate(prefab, pos, rot);

                // Track for undo.
                _lastPlaced.Add(go);

                // Register owner and creator so the piece is properly tracked
                var zNetView = go.GetComponent<ZNetView>();
                zNetView?.GetZDO()?.SetOwner(ZDOMan.GetSessionID());

                var pieceComp = go.GetComponent<Piece>();
                pieceComp?.SetCreator(player.GetPlayerID());

                placed++;

                // Brief yield every piece to avoid freezing
                if (placed % 10 == 0)
                    yield return new WaitForSeconds(PLACE_DELAY);
            }

            ValheimFloorPlanPlugin.Log.LogInfo($"Floor plan complete: {placed} placed, {skipped} skipped.");
            ValheimFloorPlanPlugin.Log.LogInfo($"First piece was at: {firstPos}  — player was at: {origin}");
            player.Message(MessageHud.MessageType.Center,
                $"Floor plan built: {placed} pieces placed, {skipped} skipped. Check log for position info.");
        }
    }
}

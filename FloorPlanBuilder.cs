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

        // ZDO key written on every piece we place.  Used by Undo() to find VFP pieces
        // across sessions — any ZNetView with this key set to "1" was placed by this mod.
        public const string VFP_TAG = "vfp_build";

        // Search radius (metres) around the player when scanning for VFP pieces.
        // A 30x30 plan has a diagonal of ~42m; 75m gives a safe margin for larger plans.
        private const float UNDO_RADIUS = 75f;

        public static FloorPlanBuilder Instance { get; private set; } = null!;

        // All GameObjects spawned in the last build — fallback for same-session undo.
        private readonly List<GameObject> _lastPlaced = new List<GameObject>();
        // Whether an undo snapshot is available.
        public bool CanUndo => _lastPlaced.Count > 0 || TerrainSnapshot.HasSnapshot;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Removes all VFP-tagged pieces within UNDO_RADIUS of the player and restores
        /// terrain from the in-memory snapshot (if available).  Works across sessions
        /// because the VFP_TAG is persisted in each piece's ZDO.
        /// </summary>
        public void Undo()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning("[FloorPlanBuilder] No local player for Undo.");
                return;
            }

            Vector3 playerPos = player.transform.position;
            int removed = 0;

            // Scan every active ZNetView in the scene for the VFP tag.
            // FindObjectsOfType searches the entire scene, not just a hierarchy subtree,
            // so it finds pieces from previous sessions that are no longer in _lastPlaced.
            foreach (var znv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (znv == null) continue;
                var zdo = znv.GetZDO();
                if (zdo == null) continue;
                if (zdo.GetString(VFP_TAG) != "1") continue;
                if (Vector3.Distance(znv.transform.position, playerPos) > UNDO_RADIUS) continue;

                ZNetScene.instance.Destroy(znv.gameObject);
                removed++;
            }
            _lastPlaced.Clear();

            // Restore terrain snapshot if one exists (same-session only).
            TerrainSnapshot.Restore();

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Undo: removed {removed} VFP pieces within {UNDO_RADIUS}m.");
            player.Message(MessageHud.MessageType.Center,
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

            // Poll until the terrain PHYSICS COLLISION MESH has rebuilt to reflect the
            // leveled height.  ApplyOperation() writes m_levelDelta instantly, but the
            // physics collider rebuilds asynchronously.  Valheim's structural integrity
            // system uses the physics mesh to decide if pieces are supported — pieces
            // placed while the mesh is stale appear floating and get destroyed.
            player.Message(MessageHud.MessageType.Center, "Waiting for terrain physics...");
            TerrainLeveler.GetPadBounds(plan, origin,
                out float padMinX, out float padMaxX, out float padMinZ, out float padMaxZ);
            yield return StartCoroutine(WaitForTerrainPhysics(
                padMinX, padMaxX, padMinZ, padMaxZ, TerrainLeveler.TargetLevelY));

            player.Message(MessageHud.MessageType.Center, "Placing floor plan pieces...");
            yield return StartCoroutine(PlacePieces(plan, origin));
        }

        /// <summary>
        /// Polls Physics.Raycast (layer 11) across a 3x3 grid covering the leveled pad until
        /// all 9 points report terrain height within TOLERANCE of targetY, or MAX_WAIT elapses.
        /// ZoneSystem.GetGroundHeight reads heightmap data which updates instantly — it cannot
        /// detect whether the physics COLLIDER has rebuilt.  WearNTear support checks use the
        /// physics collider, so we must use Physics.Raycast here.
        /// </summary>
        private IEnumerator WaitForTerrainPhysics(
            float minX, float maxX, float minZ, float maxZ, float targetY)
        {
            const float TOLERANCE = 0.3f;
            const float MAX_WAIT  = 30f;
            const float POLL_STEP = 0.25f;

            float midX = (minX + maxX) * 0.5f;
            float midZ = (minZ + maxZ) * 0.5f;
            float rayY = targetY + 300f;

            // 3x3 grid: corners, edge midpoints, and centre of the leveled pad.
            var probes = new Vector3[]
            {
                new Vector3(minX, rayY, minZ), new Vector3(midX, rayY, minZ), new Vector3(maxX, rayY, minZ),
                new Vector3(minX, rayY, midZ), new Vector3(midX, rayY, midZ), new Vector3(maxX, rayY, midZ),
                new Vector3(minX, rayY, maxZ), new Vector3(midX, rayY, maxZ), new Vector3(maxX, rayY, maxZ),
            };

            float elapsed = 0f;
            bool firstLog = true;

            while (elapsed < MAX_WAIT)
            {
                bool allReady = true;
                float worstDelta = 0f;
                var sb = firstLog ? new System.Text.StringBuilder(
                    $"[FloorPlanBuilder] Physics collider probes (targetY={targetY:F2}): ") : null;

                foreach (var p in probes)
                {
                    if (Physics.Raycast(p, Vector3.down, out var hit, 600f, 1 << 11))
                    {
                        float delta = Mathf.Abs(hit.point.y - targetY);
                        if (delta > worstDelta) worstDelta = delta;
                        if (delta > TOLERANCE) allReady = false;
                        sb?.Append($"({p.x:F0},{p.z:F0})={hit.point.y:F2}  ");
                    }
                    else
                    {
                        allReady = false;
                        sb?.Append($"({p.x:F0},{p.z:F0})=MISS  ");
                    }
                }

                if (firstLog)
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(sb!.ToString());
                    firstLog = false;
                }

                if (allReady)
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Physics collider ready after {elapsed:F1}s (worst delta {worstDelta:F2}m).");
                    yield break;
                }

                yield return new WaitForSeconds(POLL_STEP);
                elapsed += POLL_STEP;
            }

            ValheimFloorPlanPlugin.Log.LogWarning(
                $"[FloorPlanBuilder] Physics collider did not settle within {MAX_WAIT:F0}s — placing anyway.");
        }

        private IEnumerator PlacePieces(FloorPlan plan, Vector3 origin)
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

                // Sample the actual physics terrain height at this piece's XZ position.
                // We do NOT use TerrainLeveler.TargetLevelY (a uniform height) because the
                // terrain has tiny residual undulation (<2mm) from disc falloff convergence.
                // A piece placed at TargetLevelY where terrain is 0.1mm lower is technically
                // floating — WearNTear will collapse it.  The per-piece raycast places each
                // piece at the ACTUAL terrain surface, guaranteeing ground contact.
                // The polling above ensures the physics collider is fully rebuilt first.
                float terrainY = TerrainLeveler.TargetLevelY;
                if (Physics.Raycast(new Vector3(x, TerrainLeveler.TargetLevelY + 300f, z),
                        Vector3.down, out var hit, 600f, 1 << 11))
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

                // Register owner and creator so the piece is properly tracked,
                // and write the VFP tag so Undo() can find this piece across sessions.
                var zNetView = go.GetComponent<ZNetView>();
                if (zNetView != null)
                {
                    var zdo = zNetView.GetZDO();
                    if (zdo != null)
                    {
                        zdo.SetOwner(ZDOMan.GetSessionID());
                        zdo.Set(VFP_TAG, "1");
                    }
                }

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

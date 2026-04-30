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
        private const float ORIGIN_MARKER_RADIUS = 0.75f;
        private const float ORIGIN_MARKER_LIFT = 0.2f;

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

        // ── placement-preview state ───────────────────────────────────────────
        private bool          _previewActive   = false;
        private FloorPlan?    _previewPlan     = null;
        private GameObject?   _previewGo       = null;
        private LineRenderer? _previewLinePad  = null;  // white — leveled pad
        private LineRenderer? _previewLineMoat = null;  // green — moat outer edge
        private LineRenderer? _previewOriginMarker = null; // yellow — exact preview origin
        private float         _previewRotationDeg = 0f; // clockwise yaw, degrees
        private Vector3       _previewOrigin   = Vector3.zero; // locked at preview start, not updated per-frame

        private void Awake()
        {
            Instance = this;
        }

        // ── preview mode ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads the floor plan and enters preview mode: a green rectangle follows
        /// the player showing the exact build footprint.
        /// Left-click confirms the build at the current player position.
        /// Right-click or Escape cancels.
        /// </summary>
        public void StartPreview(string path)
        {
            if (_previewActive)
                CancelPreview();

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

            _previewPlan   = plan;
            _previewActive = true;

            // Lock the build origin to the player's position + facing at the moment preview
            // starts.  Moving or turning after this point does NOT shift the rectangle —
            // cancel and re-trigger to pick a new position.
            var previewPlayer = Player.m_localPlayer;
            _previewOrigin = previewPlayer != null
                ? GetBuildOrigin(previewPlayer)
                : Vector3.zero;

            // Two nested rectangles: white = leveled pad, green = moat outer edge.
            _previewGo = new GameObject("VFP_Preview");
            _previewLinePad  = MakeLine(_previewGo, new Color(1f,  1f,  1f,  0.9f), 0.12f);
            _previewLineMoat = MakeLine(_previewGo, new Color(0.2f, 1f, 0.2f, 0.9f), 0.15f);
            _previewOriginMarker = MakeLine(_previewGo, new Color(1f, 0.85f, 0.1f, 0.95f), 0.10f, 7);

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Preview active ({plan.Pieces.Count} pieces, " +
                $"{plan.Cols}×{plan.Rows} cells). Left-click to build, RMB/ESC to cancel.");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                $"ValheimFloorPlan: {ValheimFloorPlanPlugin.PreviewMoveLeftKey}/{ValheimFloorPlanPlugin.PreviewMoveRightKey}/{ValheimFloorPlanPlugin.PreviewMoveForwardKey}/{ValheimFloorPlanPlugin.PreviewMoveBackwardKey} move | {ValheimFloorPlanPlugin.PreviewRotateLeftKey}/{ValheimFloorPlanPlugin.PreviewRotateRightKey} rotate | {ValheimFloorPlanPlugin.PreviewFineAdjustKey} fine | Left-click to place | RMB/{ValheimFloorPlanPlugin.PreviewCancelKey} cancel");
        }

        private static LineRenderer MakeLine(GameObject parent, Color color, float width, int positionCount = 5)
        {
            var child = new GameObject("VFP_Line");
            child.transform.SetParent(parent.transform, false);
            var lr = child.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.loop              = false;
            lr.positionCount     = positionCount;
            lr.widthMultiplier   = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;
            lr.sharedMaterial    = new Material(Shader.Find("Sprites/Default"));
            lr.startColor        = color;
            lr.endColor          = color;
            return lr;
        }

        private void CancelPreview()
        {
            _previewActive      = false;
            _previewPlan        = null;
            _previewLinePad     = null;
            _previewLineMoat    = null;
            _previewOriginMarker = null;
            _previewRotationDeg = 0f;
            _previewOrigin      = Vector3.zero;
            if (_previewGo != null) { Destroy(_previewGo); _previewGo = null; }
        }

        /// <summary>
        /// Called every frame by Unity.  While in preview mode: updates the rectangle
        /// position and handles confirmation (LMB) and cancellation (RMB / Escape).
        /// </summary>
        private void Update()
        {
            if (!_previewActive || _previewPlan == null) return;

            var player = Player.m_localPlayer;
            if (player == null) { CancelPreview(); return; }

            // Keep the rectangle on the locked origin (fixed when preview started).
            UpdatePreviewPosition(_previewOrigin);

            bool fineAdjust = IsFineAdjustHeld();
            float rotateStep = fineAdjust
                ? ValheimFloorPlanPlugin.PreviewFineRotateStepDeg
                : ValheimFloorPlanPlugin.PreviewRotateStepDeg;
            float moveStep = fineAdjust
                ? ValheimFloorPlanPlugin.PreviewFineMoveStep
                : ValheimFloorPlanPlugin.PreviewMoveStep;

            // Configurable rotation controls. Fine-adjust reduces the step size.
            if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewRotateLeftKey))
            {
                _previewRotationDeg = (_previewRotationDeg - rotateStep + 360f) % 360f;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Rotation {_previewRotationDeg:F0}\u00b0");
            }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewRotateRightKey))
            {
                _previewRotationDeg = (_previewRotationDeg + rotateStep) % 360f;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Rotation {_previewRotationDeg:F0}\u00b0");
            }

            // Arrow keys nudge the origin relative to the current camera view.
            // Flatten onto the XZ plane so movement follows terrain positioning.
            Vector3 moveForward = Vector3.forward;
            Vector3 moveRight = Vector3.right;
            Camera movementCamera = Camera.main;
            if (movementCamera != null)
            {
                moveForward = movementCamera.transform.forward;
                moveForward.y = 0f;
                if (moveForward.sqrMagnitude > 0.0001f)
                {
                    moveForward.Normalize();
                    moveRight = new Vector3(moveForward.z, 0f, -moveForward.x);
                }
                else
                {
                    moveForward = Vector3.forward;
                    moveRight = Vector3.right;
                }
            }

            Vector3 nudge = Vector3.zero;
            if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveForwardKey))         nudge =  moveForward * moveStep;
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveBackwardKey))   nudge = -moveForward * moveStep;
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveRightKey))      nudge =  moveRight   * moveStep;
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveLeftKey))       nudge = -moveRight   * moveStep;

            if (nudge != Vector3.zero)
            {
                _previewOrigin += nudge;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Origin ({_previewOrigin.x:F1}, {_previewOrigin.z:F1})");
            }

            // Cancel on right-click or Escape.
            if (UnityEngine.Input.GetMouseButtonDown(1) || IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewCancelKey))
            {
                CancelPreview();
                player.Message(MessageHud.MessageType.Center, "ValheimFloorPlan: Build cancelled.");
                return;
            }

            // Confirm on left-click (skip while any Valheim UI panel has focus).
            bool uiOpen = Chat.instance != null && Chat.instance.HasFocus();
            if (UnityEngine.Input.GetMouseButtonDown(0) && !uiOpen)
            {
                var plan        = _previewPlan;
                float rotation  = _previewRotationDeg;
                Vector3 origin  = _previewOrigin;
                CancelPreview();
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[FloorPlanBuilder] Build confirmed by left-click. Rotation={rotation:F0}\u00b0  origin={origin}");
                StartCoroutine(LevelThenPlace(plan, rotation, origin));
            }
        }

        private static Vector3 GetBuildOrigin(Player player)
        {
            Vector3 origin = player.transform.position;
            float forwardOffset = Mathf.Max(0f, ValheimFloorPlanPlugin.BuildOriginForwardOffset);
            if (forwardOffset <= 0f) return origin;

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return origin;

            forward.Normalize();
            return origin + forward * forwardOffset;
        }

        private static bool IsPreviewKeyDown(KeyCode key)
        {
            return key != KeyCode.None && UnityEngine.Input.GetKeyDown(key);
        }

        private static bool IsFineAdjustHeld()
        {
            KeyCode key = ValheimFloorPlanPlugin.PreviewFineAdjustKey;
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
                return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

            return key != KeyCode.None && UnityEngine.Input.GetKey(key);
        }

        /// <summary>
        /// Repositions both preview rectangles each frame so they track the player.
        /// White = leveled pad boundary.  Green = moat outer edge.
        /// Each corner is Y-sampled via Physics.Raycast so the lines hug the terrain.
        /// </summary>
        private void UpdatePreviewPosition(Vector3 origin)
        {
            if (_previewPlan == null) return;

            // Get axis-aligned (unrotated) bounds, then rotate the 4 corners around the
            // player origin so the LineRenderers show the actual rotated footprint.
            TerrainLeveler.GetPadBounds(_previewPlan, origin,
                out float padMinX, out float padMaxX, out float padMinZ, out float padMaxZ);
            TerrainLeveler.GetLeveledAreaBounds(_previewPlan, origin,
                out float lvlMinX, out float lvlMaxX, out float lvlMinZ, out float lvlMaxZ);

            SetRectangle(_previewLinePad,  origin.y,
                RotateBoundsCorners(origin, padMinX, padMaxX, padMinZ, padMaxZ, _previewRotationDeg));
            SetRectangle(_previewLineMoat, origin.y,
                RotateBoundsCorners(origin, lvlMinX, lvlMaxX, lvlMinZ, lvlMaxZ, _previewRotationDeg));
            SetOriginMarker(_previewOriginMarker, origin.y, origin);
        }

        /// <summary>
        /// Returns the 4 world-space XZ corners of an axis-aligned rectangle, each rotated
        /// clockwise around <paramref name="origin"/> by <paramref name="rotDeg"/> degrees.
        /// </summary>
        private static Vector2[] RotateBoundsCorners(Vector3 origin,
            float minX, float maxX, float minZ, float maxZ, float rotDeg)
        {
            var corners = new Vector2[]
            {
                new Vector2(minX, minZ),  // SW
                new Vector2(maxX, minZ),  // SE
                new Vector2(maxX, maxZ),  // NE
                new Vector2(minX, maxZ),  // NW
            };
            if (Mathf.Approximately(rotDeg % 360f, 0f)) return corners;

            float rad = rotDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float ox = origin.x, oz = origin.z;
            for (int i = 0; i < 4; i++)
            {
                float dx = corners[i].x - ox;
                float dz = corners[i].y - oz;
                // Unity clockwise Y-rotation: x' = dx*cos + dz*sin, z' = -dx*sin + dz*cos
                corners[i] = new Vector2(ox + dx * cos + dz * sin,
                                         oz - dx * sin + dz * cos);
            }
            return corners;
        }

        private static void SetRectangle(LineRenderer? lr,
            float referenceY, Vector2[] corners)
        {
            if (lr == null) return;

            float rayY = referenceY + 300f;
            const int   terrainLayer = 1 << 11;
            const float yLift        = 0.15f;

            var pts = new Vector3[5];
            for (int i = 0; i < 4; i++)
            {
                float y = referenceY;
                if (Physics.Raycast(new Vector3(corners[i].x, rayY, corners[i].y),
                        Vector3.down, out var hit, 600f, terrainLayer))
                    y = hit.point.y;
                pts[i] = new Vector3(corners[i].x, y + yLift, corners[i].y);
            }
            pts[4] = pts[0];
            lr.SetPositions(pts);
        }

        private static void SetOriginMarker(LineRenderer? lr, float referenceY, Vector3 origin)
        {
            if (lr == null) return;

            float y = referenceY;
            float rayY = referenceY + 300f;
            const int terrainLayer = 1 << 11;
            if (Physics.Raycast(new Vector3(origin.x, rayY, origin.z),
                    Vector3.down, out var hit, 600f, terrainLayer))
                y = hit.point.y;

            var pts = new Vector3[7];
            Vector3 center = new Vector3(origin.x, y + ORIGIN_MARKER_LIFT, origin.z);
            pts[0] = center + new Vector3(-ORIGIN_MARKER_RADIUS, 0f, 0f);
            pts[1] = center;
            pts[2] = center + new Vector3(ORIGIN_MARKER_RADIUS, 0f, 0f);
            pts[3] = center;
            pts[4] = center + new Vector3(0f, 0f, ORIGIN_MARKER_RADIUS);
            pts[5] = center;
            pts[6] = center + new Vector3(0f, 0f, -ORIGIN_MARKER_RADIUS);
            lr.SetPositions(pts);
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

            var bfPlayer = Player.m_localPlayer;
            if (bfPlayer == null) { ValheimFloorPlanPlugin.Log.LogError("No local player found."); return; }
            ValheimFloorPlanPlugin.Log.LogInfo($"Building floor plan: {plan.Pieces.Count} pieces from {path}");
            StartCoroutine(LevelThenPlace(plan, 0f, GetBuildOrigin(bfPlayer)));
        }

        private IEnumerator LevelThenPlace(FloorPlan plan, float rotationDeg, Vector3 origin)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogError("No local player found.");
                yield break;
            }

            ValheimFloorPlanPlugin.Log.LogInfo($"Build origin: {origin}  rotation={rotationDeg:F0}\u00b0");

            // Clear any previous undo state.
            _lastPlaced.Clear();

            // Snapshot terrain BEFORE any leveling so Undo() can restore it.
            TerrainLeveler.GetSnapshotBounds(plan, origin,
                out float sMinX, out float sMaxX, out float sMinZ, out float sMaxZ,
                rotationDeg);
            TerrainSnapshot.Capture(sMinX, sMaxX, sMinZ, sMaxZ, origin.y);

            player.Message(MessageHud.MessageType.Center, "Clearing rocks...");
            ClearRocksInPad(plan, origin, rotationDeg);

            player.Message(MessageHud.MessageType.Center, "Leveling terrain...");
            yield return StartCoroutine(TerrainLeveler.LevelForPlan(plan, origin, rotationDeg));

            // Poll until the terrain PHYSICS COLLISION MESH has rebuilt to reflect the
            // leveled height.  ApplyOperation() writes m_levelDelta instantly, but the
            // physics collider rebuilds asynchronously.  Valheim's structural integrity
            // system uses the physics mesh to decide if pieces are supported — pieces
            // placed while the mesh is stale appear floating and get destroyed.
            player.Message(MessageHud.MessageType.Center, "Waiting for terrain physics...");
            ShowBuildProgress("Waiting for terrain physics...");
            TerrainLeveler.GetPadBounds(plan, origin,
                out float padMinX, out float padMaxX, out float padMinZ, out float padMaxZ,
                rotationDeg);
            yield return StartCoroutine(WaitForTerrainPhysics(
                padMinX, padMaxX, padMinZ, padMaxZ, TerrainLeveler.TargetLevelY));

            player.Message(MessageHud.MessageType.Center, "Placing floor plan pieces...");
            ShowBuildProgress($"Placing pieces... 0/{plan.Pieces.Count}");
            yield return StartCoroutine(PlacePieces(plan, origin, rotationDeg));

            // Some spike meshes appear a short time AFTER leveling/placement finalizes.
            // Run a brief post-build guard to detect/remove tall non-build blockers.
            ShowBuildProgress("Final checks...");
            yield return StartCoroutine(PostBuildSpikeGuard(plan, origin, rotationDeg));
        }

        /// <summary>
        /// Destroys MineRock and MineRock5 GameObjects that intersect the leveled area.
        /// We scan colliders (not just object pivots) so rocks whose pivot sits outside
        /// the rectangle but whose mesh protrudes into the pad are still removed.
        /// </summary>
        private void ClearRocksInPad(FloorPlan plan, Vector3 origin, float rotationDeg = 0f)
        {
            TerrainLeveler.GetLeveledAreaBounds(plan, origin,
                out float minX, out float maxX, out float minZ, out float maxZ,
                rotationDeg);

            int cleared = 0;

            var removed = new HashSet<GameObject>();

            // Probe the full leveled rectangle in physics-space so we catch intersecting rocks
            // even when their transform pivot lies outside the target bounds.
            var center = new Vector3((minX + maxX) * 0.5f, origin.y, (minZ + maxZ) * 0.5f);
            var halfExtents = new Vector3((maxX - minX) * 0.5f, 100f, (maxZ - minZ) * 0.5f);
            var areaBounds = new Bounds(center, new Vector3(maxX - minX, 400f, maxZ - minZ));
            foreach (var hit in Physics.OverlapBox(center, halfExtents, Quaternion.identity,
                         Physics.AllLayers, QueryTriggerInteraction.Collide))
            {
                if (hit == null) continue;

                var mr5 = hit.GetComponentInParent<MineRock5>();
                if (mr5 != null && removed.Add(mr5.gameObject))
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Removing MineRock5 '{mr5.name}' at {mr5.transform.position}");
                    ZNetScene.instance.Destroy(mr5.gameObject);
                    cleared++;
                    continue;
                }

                var mr = hit.GetComponentInParent<MineRock>();
                if (mr != null && removed.Add(mr.gameObject))
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Removing MineRock '{mr.name}' at {mr.transform.position}");
                    ZNetScene.instance.Destroy(mr.gameObject);
                    cleared++;
                    continue;
                }

                var des = hit.GetComponentInParent<Destructible>();
                if (des != null && removed.Add(des.gameObject))
                {
                    if (IsRockLikeName(des.name) && des.GetComponent<Piece>() == null)
                    {
                        ValheimFloorPlanPlugin.Log.LogInfo(
                            $"[FloorPlanBuilder] Removing rock-like Destructible '{des.name}' at {des.transform.position}");
                        ZNetScene.instance.Destroy(des.gameObject);
                        cleared++;
                    }
                }
            }

            // Some world spike meshes are renderer-only (or have non-query colliders),
            // so collider overlap alone can miss them. Sweep render bounds as fallback.
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                if (!r.bounds.Intersects(areaBounds)) continue;

                var root = r.transform.root != null ? r.transform.root.gameObject : r.gameObject;
                if (!removed.Add(root)) continue;
                if (root.GetComponentInChildren<Piece>() != null) continue;

                string lower = root.name.ToLowerInvariant();
                bool hasKnownType = HasAnyComponentNamed(root,
                    "MineRock", "MineRock5", "Destructible", "StaticPhysics", "TerrainModifier", "LocationProxy");
                bool rockLike = lower.Contains("rock") || lower.Contains("stone") || lower.Contains("cliff") ||
                                lower.Contains("spike") || lower.Contains("obelisk") || lower.Contains("monolith");
                bool looksPickable = lower.Contains("pickable") || lower.Contains("flint") ||
                                     lower.Contains("branch") || lower.Contains("mushroom") ||
                                     lower.Contains("thistle") || lower.Contains("berry");
                float h = r.bounds.size.y;
                float xz = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                bool tallBlockingMesh = h >= 2.0f && xz >= 0.8f;

                if ((rockLike || (hasKnownType && tallBlockingMesh)) && !looksPickable)
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Removing renderer blocker '{root.name}' at {root.transform.position}");
                    ZNetScene.instance.Destroy(root);
                    cleared++;
                }
            }

            foreach (var mr in Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None))
            {
                if (mr == null) continue;
                if (!removed.Add(mr.gameObject)) continue;
                var p = mr.transform.position;
                if (p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ)
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Removing MineRock5 '{mr.name}' at {p}");
                    ZNetScene.instance.Destroy(mr.gameObject);
                    cleared++;
                }
            }

            foreach (var mr in Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None))
            {
                if (mr == null) continue;
                if (!removed.Add(mr.gameObject)) continue;
                var p = mr.transform.position;
                if (p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ)
                {
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[FloorPlanBuilder] Removing MineRock '{mr.name}' at {p}");
                    ZNetScene.instance.Destroy(mr.gameObject);
                    cleared++;
                }
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] ClearRocksInPad: {cleared} rock(s) removed.");

            if (cleared == 0)
                LogAreaBlockers(center, halfExtents);
        }

        private IEnumerator PostBuildSpikeGuard(FloorPlan plan, Vector3 origin, float rotationDeg = 0f)
        {
            TerrainLeveler.GetLeveledAreaBounds(plan, origin,
                out float minX, out float maxX, out float minZ, out float maxZ,
                rotationDeg);

            const int scans = 4;
            const float scanDelay = 0.75f;
            int totalRemoved = 0;

            for (int i = 0; i < scans; i++)
            {
                totalRemoved += RemoveTallBlockersAboveTerrain(minX, maxX, minZ, maxZ, TerrainLeveler.TargetLevelY);
                if (i < scans - 1)
                    yield return new WaitForSeconds(scanDelay);
            }

            if (totalRemoved > 0)
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[FloorPlanBuilder] PostBuildSpikeGuard removed {totalRemoved} blocker(s).");
            else
                ValheimFloorPlanPlugin.Log.LogInfo("[FloorPlanBuilder] PostBuildSpikeGuard found no blockers.");
        }

        private int RemoveTallBlockersAboveTerrain(
            float minX, float maxX, float minZ, float maxZ, float referenceY)
        {
            const float step = 0.5f;
            const float minProtrusion = 0.8f;

            int removed = 0;
            var toDestroy = new HashSet<GameObject>();

            int stepsX = Mathf.CeilToInt((maxX - minX) / step);
            int stepsZ = Mathf.CeilToInt((maxZ - minZ) / step);
            float rayY = referenceY + 300f;

            for (int ix = 0; ix <= stepsX; ix++)
            {
                float x = (ix == stepsX) ? maxX : minX + ix * step;
                for (int iz = 0; iz <= stepsZ; iz++)
                {
                    float z = (iz == stepsZ) ? maxZ : minZ + iz * step;

                    if (!Physics.Raycast(new Vector3(x, rayY, z), Vector3.down,
                            out var terrainHit, 600f, 1 << 11))
                        continue;

                    var allHits = Physics.RaycastAll(new Vector3(x, rayY, z), Vector3.down, 600f);
                    if (allHits == null || allHits.Length == 0) continue;

                    System.Array.Sort(allHits, (a, b) => b.point.y.CompareTo(a.point.y));
                    foreach (var h in allHits)
                    {
                        if (h.collider == null) continue;
                        var root = h.collider.transform.root != null
                            ? h.collider.transform.root.gameObject
                            : h.collider.gameObject;
                        if (root == null) continue;
                        if (root.GetComponentInChildren<Piece>() != null) continue;

                        float protrusion = h.point.y - terrainHit.point.y;
                        if (protrusion < minProtrusion) break;

                        string n = root.name.ToLowerInvariant();
                        bool looksPickable = n.Contains("pickable") || n.Contains("flint") ||
                                             n.Contains("branch") || n.Contains("mushroom") ||
                                             n.Contains("thistle") || n.Contains("berry");
                        bool rockLike = n.Contains("rock") || n.Contains("stone") || n.Contains("cliff") ||
                                        n.Contains("spike") || n.Contains("obelisk") || n.Contains("monolith");
                        bool hasKnownType = HasAnyComponentNamed(root,
                            "MineRock", "MineRock5", "Destructible", "StaticPhysics", "TerrainModifier", "LocationProxy");

                        if ((rockLike || hasKnownType) && !looksPickable && toDestroy.Add(root))
                        {
                            ValheimFloorPlanPlugin.Log.LogWarning(
                                $"[FloorPlanBuilder] PostBuildSpikeGuard removing '{root.name}' protrusion={protrusion:F2}m at {root.transform.position}");
                        }

                        break;
                    }
                }
            }

            foreach (var go in toDestroy)
            {
                ZNetScene.instance.Destroy(go);
                removed++;
            }

            return removed;
        }

        private static bool IsRockLikeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("rock") || n.Contains("stone") || n.Contains("boulder") || n.Contains("cliff");
        }

        private static bool HasAnyComponentNamed(GameObject root, params string[] names)
        {
            var set = new HashSet<string>(names);
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (set.Contains(c.GetType().Name)) return true;
            }
            return false;
        }

        private static void LogAreaBlockers(Vector3 center, Vector3 halfExtents)
        {
            var roots = new HashSet<GameObject>();
            var interesting = new List<string>();
            var areaBounds = new Bounds(center, new Vector3(halfExtents.x * 2f, halfExtents.y * 2f, halfExtents.z * 2f));

            foreach (var hit in Physics.OverlapBox(center, halfExtents, Quaternion.identity,
                         Physics.AllLayers, QueryTriggerInteraction.Collide))
            {
                if (hit == null) continue;
                var root = hit.transform.root != null ? hit.transform.root.gameObject : hit.gameObject;
                if (!roots.Add(root)) continue;

                bool keep = false;
                string rootName = root.name;
                string lowerName = rootName.ToLowerInvariant();

                if (lowerName.Contains("rock") || lowerName.Contains("stone") ||
                    lowerName.Contains("cliff") || lowerName.Contains("location"))
                    keep = true;

                var comps = root.GetComponentsInChildren<Component>(true);
                var tags = new HashSet<string>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    string t = c.GetType().Name;
                    if (t == "MineRock" || t == "MineRock5" || t == "Destructible" ||
                        t == "StaticPhysics" || t == "TerrainModifier" || t == "LocationProxy")
                    {
                        tags.Add(t);
                        keep = true;
                    }
                }

                if (!keep) continue;
                string tagText = tags.Count > 0 ? string.Join(",", tags) : "none";
                interesting.Add($"{rootName} @ {root.transform.position} layer={root.layer} tags={tagText}");
            }

            // Include renderer-only candidates that may have no collider.
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                if (!r.bounds.Intersects(areaBounds)) continue;

                var root = r.transform.root != null ? r.transform.root.gameObject : r.gameObject;
                if (!roots.Add(root)) continue;

                string lowerName = root.name.ToLowerInvariant();
                bool keep = lowerName.Contains("rock") || lowerName.Contains("stone") ||
                            lowerName.Contains("cliff") || lowerName.Contains("spike") ||
                            lowerName.Contains("obelisk") || lowerName.Contains("monolith");

                var comps = root.GetComponentsInChildren<Component>(true);
                var tags = new HashSet<string>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    string t = c.GetType().Name;
                    if (t == "MineRock" || t == "MineRock5" || t == "Destructible" ||
                        t == "StaticPhysics" || t == "TerrainModifier" || t == "LocationProxy")
                    {
                        tags.Add(t);
                        keep = true;
                    }
                }

                if (!keep) continue;
                string tagText = tags.Count > 0 ? string.Join(",", tags) : "none";
                interesting.Add($"{root.name} @ {root.transform.position} layer={root.layer} tags={tagText} (renderer)");
            }

            if (interesting.Count == 0)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    "[FloorPlanBuilder] ClearRocksInPad diagnostics: no rock/location-like roots found in leveled area.");
                return;
            }

            int limit = Mathf.Min(15, interesting.Count);
            ValheimFloorPlanPlugin.Log.LogWarning(
                $"[FloorPlanBuilder] ClearRocksInPad diagnostics: {interesting.Count} candidate blocker root(s) in leveled area. Showing {limit}:");
            for (int i = 0; i < limit; i++)
                ValheimFloorPlanPlugin.Log.LogWarning($"[FloorPlanBuilder]   {interesting[i]}");
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
            float nextProgressAt = 2f;

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
                    ShowBuildProgress("Waiting for terrain physics... done");
                    yield break;
                }

                if (elapsed >= nextProgressAt)
                {
                    ShowBuildProgress($"Waiting for terrain physics... {elapsed:F0}s");
                    nextProgressAt += 2f;
                }

                yield return new WaitForSeconds(POLL_STEP);
                elapsed += POLL_STEP;
            }

            ValheimFloorPlanPlugin.Log.LogWarning(
                $"[FloorPlanBuilder] Physics collider did not settle within {MAX_WAIT:F0}s — placing anyway.");
            ShowBuildProgress("Waiting for terrain physics... timeout, placing anyway");
        }

        private IEnumerator PlacePieces(FloorPlan plan, Vector3 origin, float rotationDeg = 0f)
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
            int totalPieces = plan.Pieces.Count;
            int nextProgressPct = 10;

            float cosR = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            float sinR = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);

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

                // Convert from top-left grid corner (B4J storage) to world centre,
                // then rotate the offset around the player origin by the plan rotation.
                // Unity clockwise Y-rotation: x' = dx*cos + dz*sin, z' = -dx*sin + dz*cos.
                float dx = (piece.Col + effW * 0.5f) * PieceMap.CELL_SIZE;
                float dz = (piece.Row + effH * 0.5f) * PieceMap.CELL_SIZE;
                float x  = origin.x + dx * cosR + dz * sinR;
                float z  = origin.z - dx * sinR + dz * cosR;

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
                var rot = Quaternion.Euler(0, piece.Rotation + rotationDeg, 0);

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

                if (totalPieces > 0)
                {
                    int pct = Mathf.FloorToInt((placed * 100f) / totalPieces);
                    if (pct >= nextProgressPct)
                    {
                        ShowBuildProgress($"Placing pieces... {placed}/{totalPieces}");
                        nextProgressPct += 10;
                    }
                }

                // Brief yield every piece to avoid freezing
                if (placed % 10 == 0)
                    yield return new WaitForSeconds(PLACE_DELAY);
            }

            ValheimFloorPlanPlugin.Log.LogInfo($"Floor plan complete: {placed} placed, {skipped} skipped.");
            ValheimFloorPlanPlugin.Log.LogInfo($"First piece was at: {firstPos}  — player was at: {origin}");
            ShowBuildProgress($"Placing pieces... done ({placed}/{totalPieces})");
            player.Message(MessageHud.MessageType.Center,
                $"Floor plan built: {placed} pieces placed, {skipped} skipped. Check log for position info.");
        }

        private static void ShowBuildProgress(string message)
        {
            ValheimFloorPlanPlugin.ShowProgressMessage(message);
        }
    }
}

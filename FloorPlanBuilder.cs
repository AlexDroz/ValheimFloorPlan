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
        private const float ORIGIN_MARKER_LIFT = 0.45f;
        private const float PREVIEW_EDGE_RISK_SAMPLE_INTERVAL = 0.45f;
        private const float PREVIEW_EDGE_RISK_HINT_INTERVAL = 2.0f;
        private const float PREVIEW_EDGE_RISK_HINT_START_DELAY = 2.5f;
        private const float PREVIEW_STEEP_RELIEF_WARN = 6.0f;
        private const float PREVIEW_RISK_MARKER_RADIUS = 0.45f;
        private const float PREVIEW_RISK_MARKER_LIFT = 0.18f;

        // ZDO key written on every piece we place.  Used by Undo() to find VFP pieces
        // across sessions — any ZNetView with this key set to "1" was placed by this mod.
        public const string VFP_TAG = "vfp_build";

        // Undo confirmation state: tracks pending confirmation with timeout.
        private float _undoConfirmationExpireAt = 0f; // When the confirmation window closes (0 = no pending confirmation)
        private int _undoConfirmationPieceCount = 0; // Pieces to remove
        private int _undoConfirmationTerrainChunks = 0; // Terrain chunks to restore
        private Coroutine _undoCountdownCoroutine = null!; // Active countdown coroutine
        private Coroutine _undoRefreshCoroutine = null!; // Active post-undo terrain refresh coroutine

        private const float UNDO_REFRESH_RADIUS = 120f;
        private const float UNDO_REFRESH_DURATION = 2.5f;
        private const float UNDO_REFRESH_INTERVAL = 0.25f;

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
        private MeshFilter?   _previewPadWalls   = null;  // white — leveled pad wall ring
        private MeshFilter?   _previewOuterWalls = null;  // green — outer terrain-change wall ring
        private LineRenderer? _previewOriginMarker = null; // yellow — exact preview origin
        private float         _previewRotationDeg = 0f; // clockwise yaw, degrees
        private Vector3       _previewOrigin   = Vector3.zero; // locked at preview start, not updated per-frame
        private TerrainLeveler.EdgeRiskLevel _previewEdgeRisk = TerrainLeveler.EdgeRiskLevel.Low;
        private float         _previewEdgeRelief = 0f;
        private float         _previewEdgeIrregularity = 0f;
        private float         _previewEdgeMaxStep = 0f;
        private float         _previewRiskNextSampleAt = 0f;
        private float         _previewRiskNextHintAt = 0f;
        private float         _previewRiskHintsEnabledAt = 0f;
        private bool          _previewRiskDirty = true;
        private readonly List<Vector3> _previewRiskHotspots = new List<Vector3>();
        private readonly List<LineRenderer> _previewRiskMarkers = new List<LineRenderer>();
        private readonly List<Vector3> _previewRiskRenderPoints = new List<Vector3>();
        private int _previewRiskBottomCount = 0; // how many of _previewRiskRenderPoints are bottom hotspots vs top-edge

        private void Awake()
        {
            Instance = this;
        }

        // ── preview mode ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads the floor plan and enters preview mode: a green rectangle follows
        /// the player showing the exact build footprint.
        /// Confirm key confirms the build at the current player position.
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

            // Two nested vertical wall rings (open-cube style):
            // white = leveled pad, green = outer terrain-change boundary.
            _previewGo = new GameObject("VFP_Preview");
            _previewPadWalls  = MakeWallRing(_previewGo, "VFP_WallsPad",  new Color(1f,  1f,  1f,  0.28f));
            _previewOuterWalls = MakeWallRing(_previewGo, "VFP_WallsOuter", new Color(0.2f, 1f, 0.2f, 0.24f));
            _previewOriginMarker = MakeLine(_previewGo, new Color(0.18f, 0.05f, 0.02f, 0.98f), 0.14f, 7);
            _previewEdgeRisk = TerrainLeveler.EdgeRiskLevel.Low;
            _previewEdgeRelief = 0f;
            _previewEdgeIrregularity = 0f;
            _previewEdgeMaxStep = 0f;
            _previewRiskDirty = true;
            _previewRiskNextSampleAt = 0f;
            _previewRiskNextHintAt = Time.time + PREVIEW_EDGE_RISK_HINT_START_DELAY;
            _previewRiskHintsEnabledAt = Time.time + PREVIEW_EDGE_RISK_HINT_START_DELAY;

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Preview active ({plan.Pieces.Count} pieces, " +
                $"{plan.Cols}×{plan.Rows} cells). {ValheimFloorPlanPlugin.PreviewConfirmKey} to build, RMB/ESC to cancel.");
            ValheimFloorPlanPlugin.ShowWrappedMessage(
                MessageHud.MessageType.Center,
                $"ValheimFloorPlan: {ValheimFloorPlanPlugin.PreviewMoveLeftKey}/{ValheimFloorPlanPlugin.PreviewMoveRightKey}/{ValheimFloorPlanPlugin.PreviewMoveForwardKey}/{ValheimFloorPlanPlugin.PreviewMoveBackwardKey} move | {ValheimFloorPlanPlugin.PreviewRotateLeftKey}/{ValheimFloorPlanPlugin.PreviewRotateRightKey} rotate | {ValheimFloorPlanPlugin.PreviewFineAdjustKey} fine | {ValheimFloorPlanPlugin.PreviewConfirmKey} to place | RMB/{ValheimFloorPlanPlugin.PreviewCancelKey} cancel");
        }

        private static MeshFilter MakeWallRing(GameObject parent, string name, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);

            var mf = child.AddComponent<MeshFilter>();
            var mr = child.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = color;
            mr.sharedMaterial = mat;

            var mesh = new Mesh { name = name + "_Mesh" };
            // 4 sides × 4 verts per side (bottomA, bottomB, topB, topA)
            mesh.vertices = new Vector3[16];
            mesh.uv = new Vector2[16];
            for (int i = 0; i < 16; i++)
            {
                int j = i % 4;
                mesh.uv[i] = j switch
                {
                    0 => new Vector2(0f, 0f),
                    1 => new Vector2(1f, 0f),
                    2 => new Vector2(1f, 1f),
                    _ => new Vector2(0f, 1f)
                };
            }

            // Two-sided triangles for each of the 4 wall faces.
            mesh.triangles = new[]
            {
                 0,  1,  2,   0,  2,  3,   2,  1,  0,   3,  2,  0,
                 4,  5,  6,   4,  6,  7,   6,  5,  4,   7,  6,  4,
                 8,  9, 10,   8, 10, 11,  10,  9,  8,  11, 10,  8,
                12, 13, 14,  12, 14, 15,  14, 13, 12,  15, 14, 12
            };
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;

            return mf;
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
            _previewPadWalls     = null;
            _previewOuterWalls   = null;
            _previewOriginMarker = null;
            _previewRotationDeg = 0f;
            _previewOrigin      = Vector3.zero;
            _previewEdgeRisk = TerrainLeveler.EdgeRiskLevel.Low;
            _previewEdgeRelief = 0f;
            _previewEdgeIrregularity = 0f;
            _previewEdgeMaxStep = 0f;
            _previewRiskDirty = true;
            _previewRiskNextSampleAt = 0f;
            _previewRiskNextHintAt = 0f;
            _previewRiskHintsEnabledAt = 0f;
            _previewRiskHotspots.Clear();
            _previewRiskRenderPoints.Clear();
            _previewRiskMarkers.Clear();
            if (_previewGo != null) { Destroy(_previewGo); _previewGo = null; }
        }

        private void Update()
        {
            if (_previewActive && _previewPlan != null)
                UpdatePreviewMode();
        }

        public void ToggleTearRepairMode()
        {
        }

        public void ToggleTerrainClipMode()
        {
        }

        private void UpdatePreviewMode()
        {
            if (_previewPlan == null) return;

            var player = Player.m_localPlayer;
            if (player == null) { CancelPreview(); return; }

            // Keep the rectangle on the locked origin (fixed when preview started).
            UpdatePreviewPosition(_previewOrigin);

            bool previewChanged = false;

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
                previewChanged = true;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Rotation {_previewRotationDeg:F0}\u00b0");
            }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewRotateRightKey))
            {
                _previewRotationDeg = (_previewRotationDeg + rotateStep) % 360f;
                previewChanged = true;
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
                previewChanged = true;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Origin ({_previewOrigin.x:F1}, {_previewOrigin.z:F1})");
            }

            UpdatePreviewEdgeRisk(player, previewChanged);

            // Cancel on right-click or Escape.
            if (UnityEngine.Input.GetMouseButtonDown(1) || IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewCancelKey))
            {
                CancelPreview();
                player.Message(MessageHud.MessageType.Center, "ValheimFloorPlan: Build cancelled.");
                return;
            }

            // Confirm with configured preview key (skip while any Valheim UI panel has focus).
            bool uiOpen = Chat.instance != null && Chat.instance.HasFocus();
            if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewConfirmKey) && !uiOpen)
            {
                var plan        = _previewPlan;
                float rotation  = _previewRotationDeg;
                Vector3 origin  = _previewOrigin;
                var risk = _previewEdgeRisk;
                float riskRelief = _previewEdgeRelief;
                float riskStep = _previewEdgeMaxStep;
                float riskIrregularity = _previewEdgeIrregularity;

                bool steepRelief = riskRelief >= PREVIEW_STEEP_RELIEF_WARN;
                if (risk == TerrainLeveler.EdgeRiskLevel.High || steepRelief)
                {
                    ValheimFloorPlanPlugin.ShowWrappedMessage(
                        ValheimFloorPlanPlugin.WarningMessageType,
                        $"ValheimFloorPlan: Final warning before build. " +
                        $"Edge risk={risk}, relief={riskRelief:F1}m, step={riskStep:F2}m. " +
                        "Terracing or downhill tears may occur.");
                }

                CancelPreview();
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[FloorPlanBuilder] Build confirmed by key {ValheimFloorPlanPlugin.PreviewConfirmKey}. Rotation={rotation:F0}\u00b0  origin={origin}  edgeRisk={risk}  edgeRelief={riskRelief:F2}  irregularity={riskIrregularity:F2}  maxEdgeStep={riskStep:F2}");
                StartCoroutine(LevelThenPlace(plan, rotation, origin));
            }
        }

        private void UpdatePreviewEdgeRisk(Player player, bool previewChanged)
        {
            if (_previewPlan == null)
                return;

            if (previewChanged)
                _previewRiskDirty = true;

            if (!_previewRiskDirty && Time.time < _previewRiskNextSampleAt)
                return;

            var previous = _previewEdgeRisk;
            _previewEdgeRisk = TerrainLeveler.EvaluateEdgeRisk(
                _previewPlan,
                _previewOrigin,
                _previewRotationDeg,
                out _previewEdgeRelief,
                out _previewEdgeIrregularity,
                out _previewEdgeMaxStep,
                _previewRiskHotspots);

            _previewRiskBottomCount = BuildPreviewRiskRenderPoints(_previewRiskHotspots, _previewRiskRenderPoints);
            UpdatePreviewRiskMarkers(_previewEdgeRisk, _previewRiskRenderPoints, _previewRiskBottomCount);

            _previewRiskDirty = false;
            _previewRiskNextSampleAt = Time.time + PREVIEW_EDGE_RISK_SAMPLE_INTERVAL;

            bool urgentRisk = _previewEdgeRisk != TerrainLeveler.EdgeRiskLevel.Low;
            if (Time.time < _previewRiskHintsEnabledAt && !urgentRisk)
                return;

            bool shouldHint = previewChanged || _previewEdgeRisk != previous || Time.time >= _previewRiskNextHintAt;
            if (!shouldHint)
                return;

            if (_previewEdgeRisk == TerrainLeveler.EdgeRiskLevel.High ||
                _previewEdgeRisk == TerrainLeveler.EdgeRiskLevel.Medium)
            {
                string riskMsg = _previewEdgeRisk == TerrainLeveler.EdgeRiskLevel.High
                    ? $"Edge risk HIGH: uneven boundary terrain may cause tears/spikes. Try nudging or rotating before build. step={_previewEdgeMaxStep:F2}m, relief={_previewEdgeRelief:F1}m"
                    : $"Edge risk MEDIUM: some boundary irregularity detected. Small origin/rotation adjustments may improve results. step={_previewEdgeMaxStep:F2}m, relief={_previewEdgeRelief:F1}m";

                // Use a dedicated HUD lane so warnings are not replaced by origin/rotation status text.
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.WarningMessageType,
                    $"ValheimFloorPlan: {riskMsg}");
            }
            _previewRiskNextHintAt = Time.time + PREVIEW_EDGE_RISK_HINT_INTERVAL;
        }

        // Returns the number of bottom hotspot points added (the rest are top-edge markers).
        private int BuildPreviewRiskRenderPoints(List<Vector3> hotspots, List<Vector3> output)
        {
            output.Clear();
            if (_previewPlan == null)
                return 0;

            if (hotspots.Count == 0)
                return 0;

            // Original hotspot markers (terrain-level, raycasted in UpdatePreviewRiskMarkers).
            for (int i = 0; i < hotspots.Count; i++)
                output.Add(hotspots[i]);
            int bottomCount = output.Count;

            // Fixed markers along the top edge at the height of the green outer face top.
            TerrainLeveler.GetLeveledAreaBounds(_previewPlan, _previewOrigin,
                out float lvlMinX, out float lvlMaxX, out float lvlMinZ, out float lvlMaxZ);

            float rad = _previewRotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            // Raycast the 4 rotated corners to find the highest terrain Y (mirrors SetWallRingRectangle).
            float[] cxs = new float[] { lvlMinX, lvlMaxX, lvlMaxX, lvlMinX };
            float[] czs = new float[] { lvlMinZ, lvlMinZ, lvlMaxZ, lvlMaxZ };
            float terrainHigh = float.MinValue;
            const int terrainLayer = 1 << 11;
            float refY = _previewOrigin.y;
            for (int c = 0; c < 4; c++)
            {
                float dx = cxs[c] - _previewOrigin.x;
                float dz = czs[c] - _previewOrigin.z;
                float wx = _previewOrigin.x + dx * cos + dz * sin;
                float wz = _previewOrigin.z - dx * sin + dz * cos;
                if (Physics.Raycast(new Vector3(wx, refY + 300f, wz), Vector3.down, out var hit, 600f, terrainLayer))
                    if (hit.point.y > terrainHigh) terrainHigh = hit.point.y;
            }
            const float topLift = 0.30f;
            float topY = (terrainHigh == float.MinValue ? refY : terrainHigh) + topLift;

            float topDz = lvlMaxZ - _previewOrigin.z;
            float[] topFracs = new float[] { 0.25f, 0.5f, 0.75f };
            for (int f = 0; f < topFracs.Length; f++)
            {
                float localX = Mathf.Lerp(lvlMinX, lvlMaxX, topFracs[f]);
                float topDx = localX - _previewOrigin.x;
                float topWx = _previewOrigin.x + topDx * cos + topDz * sin;
                float topWz = _previewOrigin.z - topDx * sin + topDz * cos;
                output.Add(new Vector3(topWx, topY, topWz));
            }

            return bottomCount;
        }

        private void UpdatePreviewRiskMarkers(TerrainLeveler.EdgeRiskLevel risk, List<Vector3> hotspots, int bottomCount)
        {
            int desired = (risk == TerrainLeveler.EdgeRiskLevel.Low) ? 0 : Mathf.Min(hotspots.Count, 24);
            EnsureRiskMarkerCount(desired);

            for (int i = 0; i < _previewRiskMarkers.Count; i++)
            {
                var marker = _previewRiskMarkers[i];
                if (i >= desired)
                {
                    marker.enabled = false;
                    continue;
                }

                marker.enabled = true;
                marker.startColor = risk == TerrainLeveler.EdgeRiskLevel.High
                    ? new Color(1f, 0.22f, 0.12f, 0.95f)
                    : new Color(1f, 0.72f, 0.18f, 0.92f);
                marker.endColor = marker.startColor;

                Vector3 p = hotspots[i];
                float y;
                if (i < bottomCount)
                {
                    // Bottom hotspot: raycast to terrain.
                    y = p.y;
                    if (Physics.Raycast(new Vector3(p.x, p.y + 300f, p.z), Vector3.down, out var hit, 600f, 1 << 11))
                        y = hit.point.y;
                    y += PREVIEW_RISK_MARKER_LIFT;
                }
                else
                {
                    // Top-edge marker: Y was already computed at the green face top.
                    y = p.y;
                }

                Vector3 center = new Vector3(p.x, y, p.z);
                float r = PREVIEW_RISK_MARKER_RADIUS;
                marker.positionCount = 5;
                marker.SetPosition(0, center + new Vector3(-r, 0f, 0f));
                marker.SetPosition(1, center + new Vector3(0f, 0f, r));
                marker.SetPosition(2, center + new Vector3(r, 0f, 0f));
                marker.SetPosition(3, center + new Vector3(0f, 0f, -r));
                marker.SetPosition(4, center + new Vector3(-r, 0f, 0f));
            }
        }

        private void EnsureRiskMarkerCount(int count)
        {
            if (_previewGo == null)
                return;

            while (_previewRiskMarkers.Count < count)
            {
                var lr = MakeLine(_previewGo, new Color(1f, 0.72f, 0.18f, 0.92f), 0.06f, 5);
                lr.loop = false;
                _previewRiskMarkers.Add(lr);
            }
        }

        private static Vector3 GetBuildOrigin(Player player)
        {
            return GetForwardOffsetOrigin(player, ValheimFloorPlanPlugin.BuildOriginForwardOffset);
        }

        private static Vector3 GetForwardOffsetOrigin(Player player, float forwardOffset)
        {
            Vector3 origin = player.transform.position;
            forwardOffset = Mathf.Max(0f, forwardOffset);
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
        /// Repositions both preview wall-rings each frame so they track the player.
        /// White = leveled pad volume. Green = outer terrain-change volume.
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

            SetWallRingRectangle(_previewPadWalls, origin.y,
                RotateBoundsCorners(origin, padMinX, padMaxX, padMinZ, padMaxZ, _previewRotationDeg));
            SetWallRingRectangle(_previewOuterWalls, origin.y,
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

        private static void SetWallRingRectangle(MeshFilter? mf,
            float referenceY, Vector2[] corners)
        {
            if (mf == null || mf.sharedMesh == null) return;

            float rayY = referenceY + 300f;
            const int   terrainLayer = 1 << 11;
            const float bottomLift = 0.06f;
            const float topLift    = 0.30f;
            const float minHeight  = 0.75f;

            var terrainY = new float[4];
            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float y = referenceY;
                if (Physics.Raycast(new Vector3(corners[i].x, rayY, corners[i].y),
                        Vector3.down, out var hit, 600f, terrainLayer))
                    y = hit.point.y;

                terrainY[i] = y;
                if (y < low) low = y;
                if (y > high) high = y;
            }

            float bottomY = low + bottomLift;
            float topY = high + topLift;
            if (topY - bottomY < minHeight)
                topY = bottomY + minHeight;

            var mesh = mf.sharedMesh;
            var verts = mesh.vertices;

            for (int side = 0; side < 4; side++)
            {
                int next = (side + 1) % 4;
                int v = side * 4;

                verts[v + 0] = new Vector3(corners[side].x, bottomY, corners[side].y); // bottom A
                verts[v + 1] = new Vector3(corners[next].x, bottomY, corners[next].y); // bottom B
                verts[v + 2] = new Vector3(corners[next].x, topY, corners[next].y);    // top B
                verts[v + 3] = new Vector3(corners[side].x, topY, corners[side].y);    // top A
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
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

        private static void SetLinePositions(LineRenderer? lr, Vector3 from, Vector3 to)
        {
            if (lr == null) return;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
        }

        /// <summary>
        /// Undo is a two-step confirmation:
        /// - First call: Show a preview of what will be removed/restored, then wait for confirmation.
        /// - Second call (within 5 seconds): Actually perform the undo.
        /// This prevents accidental undos and shows the user exactly what will happen.
        /// </summary>
        public void Undo()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning("[FloorPlanBuilder] No local player for Undo.");
                return;
            }

            // Check if a confirmation is pending and still valid.
            bool confirmationPending = _undoConfirmationExpireAt > Time.time;

            if (confirmationPending)
            {
                // Confirmation was already shown and is still valid — perform the undo.
                _undoConfirmationExpireAt = 0f; // Clear the pending state.
                if (_undoCountdownCoroutine != null)
                    StopCoroutine(_undoCountdownCoroutine);
                _undoCountdownCoroutine = null!;
                PerformUndo(player);
            }
            else
            {
                // No pending confirmation — check if there's anything to undo.
                CountUndoStats(player, out int pieces, out int terrainChunks);

                if (pieces == 0 && terrainChunks == 0)
                {
                    ValheimFloorPlanPlugin.ShowWrappedMessage(
                        ValheimFloorPlanPlugin.ProgressMessageType,
                        "ValheimFloorPlan: Nothing to undo.");
                    return;
                }

                // Store for countdown coroutine to use.
                _undoConfirmationPieceCount = pieces;
                _undoConfirmationTerrainChunks = terrainChunks;
                _undoConfirmationExpireAt = Time.time + 5f; // 5-second confirmation window.

                // Stop any previous countdown coroutine.
                if (_undoCountdownCoroutine != null)
                    StopCoroutine(_undoCountdownCoroutine);

                // Start countdown coroutine.
                _undoCountdownCoroutine = StartCoroutine(UndoCountdownCoroutine());

                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[FloorPlanBuilder] Undo confirmation pending: {pieces} pieces, {terrainChunks} terrain chunks.");
            }
        }

        /// <summary>Count how many pieces will be removed and how many terrain chunks will be restored.</summary>
        private void CountUndoStats(Player player, out int pieceCount, out int terrainChunkCount)
        {
            pieceCount = 0;
            Vector3 playerPos = player.transform.position;

            // Count VFP-tagged pieces within undo radius.
            foreach (var znv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (znv == null) continue;
                var zdo = znv.GetZDO();
                if (zdo == null) continue;
                if (zdo.GetString(VFP_TAG) != "1") continue;
                if (Vector3.Distance(znv.transform.position, playerPos) > UNDO_RADIUS) continue;

                pieceCount++;
            }

            // Count terrain chunks in snapshot.
            terrainChunkCount = TerrainSnapshot.GetSnapshotChunkCount();
        }

        /// <summary>Perform the actual undo operation after confirmation.</summary>
        private void PerformUndo(Player player)
        {
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
            bool hadSnapshot = TerrainSnapshot.HasSnapshot;
            int restoredChunks = TerrainSnapshot.GetSnapshotChunkCount();
            TerrainSnapshot.Restore();

            if (hadSnapshot && restoredChunks > 0)
            {
                if (_undoRefreshCoroutine != null)
                    StopCoroutine(_undoRefreshCoroutine);
                _undoRefreshCoroutine = StartCoroutine(PostUndoTerrainRefresh(playerPos, restoredChunks));
            }

            if (!hadSnapshot)
            {
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.WarningMessageType,
                    "ValheimFloorPlan: No terrain snapshot in this session. Undo removed pieces only.");
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Undo: removed {removed} VFP pieces within {UNDO_RADIUS}m, restored {restoredChunks} terrain chunks.");
            player.Message(MessageHud.MessageType.Center,
                $"ValheimFloorPlan: Undone ({removed} pieces removed, {restoredChunks} terrain chunks restored).");
        }

        /// <summary>
        /// Re-pokes nearby heightmaps for a short window after undo restore.
        /// This mimics the visual refresh that usually occurs after zone reload/teleport.
        /// </summary>
        private IEnumerator PostUndoTerrainRefresh(Vector3 center, int restoredChunks)
        {
            float elapsed = 0f;
            int passes = 0;
            int touched = 0;

            while (elapsed < UNDO_REFRESH_DURATION)
            {
                #pragma warning disable CS0618
                var hmaps = UnityEngine.Object.FindObjectsOfType<Heightmap>() ?? System.Array.Empty<Heightmap>();
                #pragma warning restore CS0618

                int passTouched = 0;
                foreach (var hmap in hmaps)
                {
                    if (hmap == null) continue;
                    if (Vector3.Distance(hmap.transform.position, center) > UNDO_REFRESH_RADIUS) continue;
                    hmap.Poke(false);
                    passTouched++;
                }

                passes++;
                touched = passTouched;
                yield return new WaitForSeconds(UNDO_REFRESH_INTERVAL);
                elapsed += UNDO_REFRESH_INTERVAL;
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Post-undo refresh complete: {passes} passes, {touched} nearby heightmaps touched, restoredChunks={restoredChunks}.");

            _undoRefreshCoroutine = null!;
        }

        /// <summary>Countdown coroutine that shows remaining confirmation time, updating every second.</summary>
        private IEnumerator UndoCountdownCoroutine()
        {
            const float UPDATE_INTERVAL = 1.0f;
            float nextUpdateAt = Time.time + UPDATE_INTERVAL;

            while (_undoConfirmationExpireAt > Time.time)
            {
                if (Time.time >= nextUpdateAt)
                {
                    float remainingSeconds = _undoConfirmationExpireAt - Time.time;
                    int secondsLeft = (int)Mathf.Ceil(remainingSeconds);

                    string msg = $"ValheimFloorPlan: Confirm Undo? Will remove {_undoConfirmationPieceCount} piece(s)";
                    if (_undoConfirmationTerrainChunks > 0)
                        msg += $" and restore {_undoConfirmationTerrainChunks} terrain chunk(s)";
                    msg += $". Press Undo again ({secondsLeft}s remaining) to confirm.";

                    ValheimFloorPlanPlugin.ShowWrappedMessage(
                        ValheimFloorPlanPlugin.ProgressMessageType,
                        msg);

                    nextUpdateAt = Time.time + UPDATE_INTERVAL;
                }

                yield return null;
            }

            _undoCountdownCoroutine = null!;
            _undoConfirmationExpireAt = 0f;
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
            if (!TerrainSnapshot.HasSnapshot)
            {
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.WarningMessageType,
                    "ValheimFloorPlan: Warning - terrain snapshot capture failed. Undo may remove pieces without restoring terrain.");
            }

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
            int processed = 0;
            int nextProgressPct = 10;
            int configuredExternalWallHeight = Mathf.Clamp(ValheimFloorPlanPlugin.ExternalWallHeight, 1, 4);
            bool useWoodStructure = ValheimFloorPlanPlugin.WallPillarMaterial == ValheimFloorPlanPlugin.StructuralMaterial.Wood;

            GetPlanPieceBounds(plan,
                out int minCol, out int maxColExclusive,
                out int minRow, out int maxRowExclusive);

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

                int effectivePieceRotation = piece.Rotation;
                if (useWoodStructure && piece.Type == "Wall" && piece.WallFace == WallFaceMode.Inner)
                    effectivePieceRotation = (effectivePieceRotation + 180) % 360;

                string prefabName = ResolvePrefabName(piece.Type, def.Prefab, useWoodStructure);
                var prefab = ZNetScene.instance?.GetPrefab(prefabName);
                if (prefab == null)
                {
                    ValheimFloorPlanPlugin.Log.LogWarning($"Prefab '{prefabName}' not found in ZNetScene — skipped.");
                    skipped++;
                    continue;
                }

                // Effective dimensions after applying rotation (90/270 swaps W and H),
                // matching the B4J designer's EffW / EffH logic exactly.
                int effW = def.EffW(effectivePieceRotation);
                int effH = def.EffH(effectivePieceRotation);

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
                bool isExternal = IsOnPlanOuterPerimeter(
                    piece.Col, piece.Row, effW, effH,
                    minCol, maxColExclusive, minRow, maxRowExclusive);
                bool shouldStack = IsExternalWallOrPillarType(piece.Type) && isExternal;
                int stackCount = shouldStack ? configuredExternalWallHeight : 1;
                float stackStepY = GetStackStepY(piece.Type);
                var rot = Quaternion.Euler(0, effectivePieceRotation + rotationDeg, 0);

                // Wood pieces are narrower/thinner than their stone equivalents; push
                // external pieces outward so their outer face aligns with the floor edge.
                // Direction is derived from which plan edge the piece sits on, not its
                // own rotation (which would give wrong results for south/west walls).
                Vector3 materialOffset = Vector3.zero;
                if (useWoodStructure && (piece.Type == "Wall" || piece.Type == "Pillar") && isExternal)
                    materialOffset = GetWoodPerimeterOffset(
                        piece.Type, piece.Col, piece.Row, effW, effH,
                        minCol, maxColExclusive, minRow, maxRowExclusive,
                        rotationDeg);

                for (int i = 0; i < stackCount; i++)
                {
                    var stackedPos = new Vector3(pos.x, pos.y + stackStepY * i, pos.z) + materialOffset;

                    if (placed == 0)
                    {
                        firstPos = stackedPos;
                        ValheimFloorPlanPlugin.Log.LogInfo($"First piece: type={piece.Type} prefab={prefabName} pos={stackedPos}");
                    }

                    var go = UnityEngine.Object.Instantiate(prefab, stackedPos, rot);

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

                    // Brief yield every 10 spawned objects to avoid freezing
                    if (placed % 10 == 0)
                        yield return new WaitForSeconds(PLACE_DELAY);
                }

                processed++;
                if (totalPieces > 0)
                {
                    int pct = Mathf.FloorToInt((processed * 100f) / totalPieces);
                    if (pct >= nextProgressPct)
                    {
                        ShowBuildProgress($"Placing pieces... {processed}/{totalPieces}");
                        nextProgressPct += 10;
                    }
                }
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

        private static bool IsExternalWallOrPillarType(string type)
        {
            return type == "Wall" || type == "Pillar";
        }

        private static string ResolvePrefabName(string type, string defaultPrefab, bool useWoodStructure)
        {
            if (!useWoodStructure) return defaultPrefab;

            if (type == "Wall") return "wood_wall_half";
            if (type == "Pillar") return "wood_pole_log";
            return defaultPrefab;
        }

        private static float GetStackStepY(string type)
        {
            if (type == "Wall") return 1f;
            if (type == "Pillar") return 2f;
            return 0f;
        }

        private static bool IsOnPlanOuterPerimeter(
            int col, int row, int effW, int effH,
            int minCol, int maxColExclusive, int minRow, int maxRowExclusive)
        {
            return col <= minCol || row <= minRow ||
                   (col + effW) >= maxColExclusive || (row + effH) >= maxRowExclusive;
        }

        /// <summary>
        /// Returns the world-space offset to apply to a wood Wall or Pillar piece so its
        /// outer face aligns with the outer face of the equivalent stone piece.
        ///
        /// Offset is derived from Valheim prefab geometry:
        ///   stone_wall_2x1 depth = 1.0 m  →  half = 0.50 m
        ///   wood_wall_half  depth = 0.3 m  →  half = 0.15 m  →  shift = 0.35 m
        ///   stone_pillar   width = 0.5 m  →  half = 0.25 m
        ///   wood_pole2     width = 0.2 m  →  half = 0.10 m  →  shift = 0.15 m
        ///
        /// Direction is determined by which plan edge the piece sits on (not its own
        /// rotation), so south/west walls are handled correctly.  Corner pillars that
        /// touch two edges are shifted along both axes independently.
        /// touch two edges are shifted along both axes independently.  For walls, only
        /// the axis perpendicular to the wall's face is shifted (so a corner wall that
        /// touches two edges does not get a diagonal shift that would leave gaps).
        /// </summary>
        private static Vector3 GetWoodPerimeterOffset(
            string pieceType,
            int col, int row, int effW, int effH,
            int minCol, int maxColExclusive, int minRow, int maxRowExclusive,
            float planRotationDeg)
        {
            // Per-prefab outward shift (stone half-size minus wood half-size).
            float shift = pieceType == "Pillar" ? 0.15f : 0.35f;

            // Which plan edges does this piece touch?  col→+X axis, row→+Z axis.
            float lx = 0f, lz = 0f;
            if (col <= minCol)                    lx -= 1f;  // left (west) edge  → shift −X
            if ((col + effW) >= maxColExclusive)  lx += 1f;  // right (east) edge → shift +X
            if (row <= minRow)                    lz -= 1f;  // bottom (south) edge → shift −Z
            if ((row + effH) >= maxRowExclusive)  lz += 1f;  // top (north) edge  → shift +Z

            // Walls must only shift perpendicular to their face — never along their length —
            // otherwise a corner wall that touches two edges gets a diagonal shift and leaves gaps.
            //   effH==1  → wall runs E-W, faces N/S: only Z shift is valid, suppress X.
            //   effW==1  → wall runs N-S, faces E/W: only X shift is valid, suppress Z.
            // Pillars are 1×1 so both axes always apply (corner pillars shift diagonally, which is correct).
            if (pieceType == "Wall")
            {
                if (effH == 1) lx = 0f;   // east-west wall: suppress X shift
                else           lz = 0f;   // north-south wall: suppress Z shift
            }

            if (lx == 0f && lz == 0f) return Vector3.zero;

            // Apply per-axis shift (not normalised: corner pillars shift in both axes).
            var localOffset = new Vector3(lx * shift, 0f, lz * shift);

            // Rotate into world space using the plan's rotation.
            return Quaternion.Euler(0, planRotationDeg, 0) * localOffset;
        }

        private static void GetPlanPieceBounds(
            FloorPlan plan,
            out int minCol, out int maxColExclusive,
            out int minRow, out int maxRowExclusive)
        {
            minCol = int.MaxValue;
            maxColExclusive = int.MinValue;
            minRow = int.MaxValue;
            maxRowExclusive = int.MinValue;

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
                if (p.Col + effW > maxColExclusive) maxColExclusive = p.Col + effW;
                if (p.Row < minRow) minRow = p.Row;
                if (p.Row + effH > maxRowExclusive) maxRowExclusive = p.Row + effH;
            }

            if (minCol == int.MaxValue)
            {
                minCol = 0;
                minRow = 0;
                maxColExclusive = plan.Cols;
                maxRowExclusive = plan.Rows;
            }
        }
    }
}

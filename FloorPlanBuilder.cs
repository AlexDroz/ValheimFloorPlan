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
        private const bool TESTING_ONLY = false;
        private const float PLACE_DELAY = 0.05f; // seconds between spawns to avoid lag spikes
        private const float ORIGIN_MARKER_LIFT = 0.3f;
        private const float ORIGIN_MARKER_HEIGHT = 10f;
        private const float PREVIEW_EDGE_RISK_SAMPLE_INTERVAL = 0.45f;
        private const float PREVIEW_EDGE_RISK_HINT_INTERVAL = 2.0f;
        private const float PREVIEW_EDGE_RISK_HINT_START_DELAY = 2.5f;
        private const float PREVIEW_STEEP_RELIEF_WARN = 6.0f;
        private const float PREVIEW_RISK_MARKER_RADIUS = 0.45f;
        private const float PREVIEW_RISK_MARKER_LIFT = 0.18f;
        private static readonly string[] TEST_WORKBENCH_PREFABS = new[]
        {
            "piece_workbench",
            "forge",
            "piece_stonecutter",
            "piece_artisanstation",
            "blackforge",
            "piece_magetable"
        };

        // ZDO key written on every piece we place.  Used by Undo() to find VFP pieces
        // across sessions — any ZNetView with this key set to "1" was placed by this mod.
        public const string VFP_TAG = "vfp_build";

        // Undo confirmation state: tracks pending confirmation with timeout.
        private float _undoConfirmationExpireAt = 0f; // When the confirmation window closes (0 = no pending confirmation)
        private int _undoConfirmationPieceCount = 0; // Pieces to remove
        private int _undoConfirmationTerrainChunks = 0; // Terrain chunks to restore
        private Coroutine _undoCountdownCoroutine = null!; // Active countdown coroutine
        private Coroutine _undoRefreshCoroutine = null!; // Active post-undo terrain refresh coroutine
        private GameObject? _undoHighlightGo = null; // Highlight rings shown during undo confirmation

        private const float UNDO_REFRESH_RADIUS = 120f;
        private const float UNDO_REFRESH_DURATION = 2.5f;
        private const float UNDO_REFRESH_INTERVAL = 0.25f;
        private const float UNDO_HIGHLIGHT_RING_RADIUS = 1.05f;
        private const float UNDO_HIGHLIGHT_RING_LIFT = 0.25f;
        private const int   UNDO_HIGHLIGHT_RING_SEGMENTS = 20;
        private const float UNDO_RADIUS_ADJUST_STEP = 5f;
        private const float UNDO_CONFIRMATION_SECONDS = 5f;
        private const int   UNDO_BOUNDARY_CIRCLE_SEGMENTS = 64;
        private const float UNDO_BOUNDARY_CIRCLE_LIFT = 0.3f;

        // Search radius (metres) around the player when scanning for VFP pieces.
        // Reads from config so the player can tune it; falls back to 15 m.
        private static float UNDO_RADIUS => ValheimFloorPlanPlugin.UndoRadius;

        // Session radius adjusted by +/- during the confirmation window.
        // Reset to UNDO_RADIUS each time a new confirmation starts.
        private float _undoActiveRadius = 15f;

        // Centre of the undo search circle. Starts at the player's position and can
        // be nudged with the configured preview move keys during the confirmation window.
        private Vector3 _undoCenter = Vector3.zero;

        public static FloorPlanBuilder Instance { get; private set; } = null!;

        // All GameObjects spawned in the last build — fallback for same-session undo.
        private readonly List<GameObject> _lastPlaced = new List<GameObject>();
        private readonly List<GameObject> _groundFloorScaffoldVerticals = new List<GameObject>();
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
        private Vector3       _previewCenter   = Vector3.zero; // locked at preview start, rotated around by deriving origin
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
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    MessageHud.MessageType.Center,
                    $"ValheimFloorPlan: Could not load plan '{System.IO.Path.GetFileName(path)}' — {ex.Message}");
                return;
            }

            _previewPlan   = plan;
            _previewActive = true;

            // Lock the build center to the player's position + facing at the moment preview
            // starts.  Moving or turning after this point does NOT shift the rectangle —
            // cancel and re-trigger to pick a new position.
            var previewPlayer = Player.m_localPlayer;
            // Initialise rotation to match the Designer's screen-space convention:
            // the front edge is at the bottom of the canvas, so increasing rows
            // should appear toward the player rather than away from them.
            _previewRotationDeg = GameCamera.instance != null
                ? GameCamera.instance.transform.eulerAngles.y
                : (previewPlayer != null ? previewPlayer.transform.eulerAngles.y : 0f);
            _previewRotationDeg = SnapAngleDeg(_previewRotationDeg + 180f);

            _previewCenter = previewPlayer != null
                ? GetInitialBuildCenter(previewPlayer, plan, _previewRotationDeg)
                : Vector3.zero;
            _previewOrigin = GetPlacementOriginFromCenter(plan, _previewCenter, _previewRotationDeg);

            // Two nested vertical wall rings (open-cube style):
            // white = leveled pad, green = outer terrain-change boundary.
            _previewGo = new GameObject("VFP_Preview");
            _previewPadWalls  = MakeWallRing(_previewGo, "VFP_WallsPad",  new Color(1f,  1f,  1f,  0.28f));
            _previewOuterWalls = MakeWallRing(_previewGo, "VFP_WallsOuter", new Color(0.2f, 1f, 0.2f, 0.24f));
            _previewOriginMarker = MakeLine(_previewGo, new Color(1f, 0.9f, 0f, 0.98f), 0.20f, 2);
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
            _previewCenter      = Vector3.zero;
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

            if (_undoConfirmationExpireAt > Time.time)
                UpdateUndoConfirmationInput();
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

            // Keep the rectangle centered on the locked preview center.
            UpdatePreviewPosition(_previewOrigin, _previewCenter);

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
                if (!fineAdjust) _previewRotationDeg = SnapAngleDeg(_previewRotationDeg);
                _previewOrigin = GetPlacementOriginFromCenter(_previewPlan, _previewCenter, _previewRotationDeg);
                previewChanged = true;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Rotation {_previewRotationDeg:F1}\u00b0");
            }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewRotateRightKey))
            {
                _previewRotationDeg = (_previewRotationDeg + rotateStep) % 360f;
                if (!fineAdjust) _previewRotationDeg = SnapAngleDeg(_previewRotationDeg);
                _previewOrigin = GetPlacementOriginFromCenter(_previewPlan, _previewCenter, _previewRotationDeg);
                previewChanged = true;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Rotation {_previewRotationDeg:F1}\u00b0");
            }

            // Arrow keys nudge the preview center relative to the current camera view.
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
                _previewCenter += nudge;
                _previewOrigin = GetPlacementOriginFromCenter(_previewPlan, _previewCenter, _previewRotationDeg);
                previewChanged = true;
                player.Message(ValheimFloorPlanPlugin.ProgressMessageType,
                    $"ValheimFloorPlan: Center ({_previewCenter.x:F1}, {_previewCenter.z:F1})");
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
                Vector3 center  = _previewCenter;
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
                    $"[FloorPlanBuilder] Build confirmed by key {ValheimFloorPlanPlugin.PreviewConfirmKey}. Rotation={rotation:F0}\u00b0  center={center}  origin={origin}  edgeRisk={risk}  edgeRelief={riskRelief:F2}  irregularity={riskIrregularity:F2}  maxEdgeStep={riskStep:F2}");
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

            float previewRaiseDelta = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainHighPointDelta, 0f, 4f);

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
            float topY = (terrainHigh == float.MinValue ? refY : terrainHigh) + topLift + previewRaiseDelta;

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

        private static Vector3 GetInitialBuildCenter(Player player, FloorPlan? plan, float rotationDeg)
        {
            Vector3 origin = player.transform.position;
            Vector3 forward = GetBuildForward(player);
            if (forward.sqrMagnitude < 0.0001f)
                return origin;

            float autoOffset = GetForwardHalfExtent(plan, rotationDeg, forward);
            float extraOffset = Mathf.Max(0f, ValheimFloorPlanPlugin.BuildOriginForwardOffset);
            return origin + forward * (autoOffset + extraOffset);
        }

        private static Vector3 GetBuildForward(Player player)
        {
            Vector3 forward = GameCamera.instance != null
                ? GameCamera.instance.transform.forward
                : player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude >= 0.0001f)
                forward.Normalize();
            return forward;
        }

        private static float GetForwardHalfExtent(FloorPlan? plan, float rotationDeg, Vector3 worldForward)
        {
            if (plan == null)
                return 0f;

            GetPlanPieceBounds(plan,
                out int minCol, out int maxColExclusive,
                out int minRow, out int maxRowExclusive);

            float outerDelta = TerrainLeveler.GetOuterPerimeterDelta();
            float minX = minCol * PieceMap.CELL_SIZE - outerDelta;
            float maxX = maxColExclusive * PieceMap.CELL_SIZE + outerDelta;
            float minZ = minRow * PieceMap.CELL_SIZE - outerDelta;
            float maxZ = maxRowExclusive * PieceMap.CELL_SIZE + outerDelta;
            float localCenterX = (minX + maxX) * 0.5f;
            float localCenterZ = (minZ + maxZ) * 0.5f;

            float maxProjection = 0f;
            float[] cornerX = new float[] { minX, maxX, maxX, minX };
            float[] cornerZ = new float[] { minZ, minZ, maxZ, maxZ };
            for (int i = 0; i < 4; i++)
            {
                float relX = cornerX[i] - localCenterX;
                float relZ = cornerZ[i] - localCenterZ;
                Vector2 worldOffset = PieceMap.TransformLocalXZ(relX, relZ, rotationDeg);
                float projection = worldOffset.x * worldForward.x + worldOffset.y * worldForward.z;
                if (projection > maxProjection)
                    maxProjection = projection;
            }

            return Mathf.Max(0f, maxProjection);
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
        /// Repositions both preview wall-rings each frame so they track the preview center.
        /// White = leveled pad volume. Green = outer terrain-change volume.
        /// </summary>
        private void UpdatePreviewPosition(Vector3 origin, Vector3 center)
        {
            if (_previewPlan == null) return;

            float previewRaiseDelta = Mathf.Clamp(ValheimFloorPlanPlugin.TerrainHighPointDelta, 0f, 4f);

            TerrainLeveler.GetPadBounds(_previewPlan, origin,
                out float padMinX, out float padMaxX, out float padMinZ, out float padMaxZ,
                _previewRotationDeg);
            TerrainLeveler.GetLeveledAreaBounds(_previewPlan, origin,
                out float lvlMinX, out float lvlMaxX, out float lvlMinZ, out float lvlMaxZ,
                _previewRotationDeg);

            SetWallRingRectangle(_previewPadWalls, origin.y,
                new[]
                {
                    new Vector2(padMinX, padMinZ),
                    new Vector2(padMaxX, padMinZ),
                    new Vector2(padMaxX, padMaxZ),
                    new Vector2(padMinX, padMaxZ),
                },
                previewRaiseDelta);
            SetWallRingRectangle(_previewOuterWalls, origin.y,
                new[]
                {
                    new Vector2(lvlMinX, lvlMinZ),
                    new Vector2(lvlMaxX, lvlMinZ),
                    new Vector2(lvlMaxX, lvlMaxZ),
                    new Vector2(lvlMinX, lvlMaxZ),
                },
                previewRaiseDelta);
            SetOriginMarker(_previewOriginMarker, center.y, center);
        }

        private static Vector3 GetPlacementOriginFromCenter(FloorPlan? plan, Vector3 center, float rotationDeg)
        {
            if (plan == null)
                return center;

            GetPlanPieceBounds(plan,
                out int minCol, out int maxColExclusive,
                out int minRow, out int maxRowExclusive);

            float localCenterX = (minCol + maxColExclusive) * 0.5f * PieceMap.CELL_SIZE;
            float localCenterZ = (minRow + maxRowExclusive) * 0.5f * PieceMap.CELL_SIZE;

            Vector2 centerOffset = PieceMap.TransformLocalXZ(localCenterX, localCenterZ, rotationDeg);
            return new Vector3(center.x - centerOffset.x, center.y, center.z - centerOffset.y);
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

            float ox = origin.x, oz = origin.z;
            for (int i = 0; i < 4; i++)
            {
                float dx = corners[i].x - ox;
                float dz = corners[i].y - oz;
                Vector2 rotated = PieceMap.TransformLocalXZ(dx, dz, rotDeg);
                corners[i] = new Vector2(ox + rotated.x, oz + rotated.y);
            }
            return corners;
        }

        private static void SetWallRingRectangle(MeshFilter? mf,
            float referenceY, Vector2[] corners, float previewRaiseDelta)
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
            float topY = high + topLift + Mathf.Max(0f, previewRaiseDelta);
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

            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(origin.x, y + ORIGIN_MARKER_LIFT, origin.z));
            lr.SetPosition(1, new Vector3(origin.x, y + ORIGIN_MARKER_LIFT + ORIGIN_MARKER_HEIGHT, origin.z));
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
                _undoActiveRadius = UNDO_RADIUS;
                _undoCenter = player.transform.position;
                CountUndoStats(player, _undoActiveRadius, _undoCenter, out int pieces, out int terrainChunks);

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
                _undoConfirmationExpireAt = Time.time + UNDO_CONFIRMATION_SECONDS;

                // Stop any previous countdown coroutine.
                if (_undoCountdownCoroutine != null)
                    StopCoroutine(_undoCountdownCoroutine);

                // Start countdown coroutine.
                _undoCountdownCoroutine = StartCoroutine(UndoCountdownCoroutine());

                // Show per-piece highlight rings so the player can see what will be removed.
                ShowUndoHighlights(player);

                // Show immediate confirmation feedback so the first key press feels responsive.
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.ProgressMessageType,
                    BuildUndoConfirmationMessage(5));

                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[FloorPlanBuilder] Undo confirmation pending: {pieces} pieces, {terrainChunks} terrain chunks.");
            }
        }

        private string BuildUndoConfirmationMessage(int secondsLeft)
        {
            string msg = $"ValheimFloorPlan: Confirm Undo? Will remove {_undoConfirmationPieceCount} piece(s)";
            if (_undoConfirmationTerrainChunks > 0)
                msg += $" and restore {_undoConfirmationTerrainChunks} terrain chunk(s)";
            msg += $" within {_undoActiveRadius:F0}m horizontal radius (+/- to adjust)";
            msg += $". Arrow keys to move circle center | Press Undo again ({secondsLeft}s remaining) to confirm, or RMB/Esc to cancel.";
            return msg;
        }

        /// <summary>Count how many pieces will be removed and how many terrain chunks will be restored.</summary>
        private void CountUndoStats(Player player, float radius, Vector3 center, out int pieceCount, out int terrainChunkCount)
        {
            pieceCount = 0;

            // Count VFP-tagged pieces within undo radius of the search center.
            foreach (var znv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (znv == null) continue;
                var zdo = znv.GetZDO();
                if (zdo == null) continue;
                if (zdo.GetString(VFP_TAG) != "1") continue;
                if (!IsWithinHorizontalRadius(znv.transform.position, center, radius)) continue;

                pieceCount++;
            }

            // Count terrain chunks in snapshot.
            terrainChunkCount = TerrainSnapshot.GetSnapshotChunkCount();
        }

        /// <summary>Perform the actual undo operation after confirmation.</summary>
        private void PerformUndo(Player player)
        {
            ClearUndoHighlights();
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
                if (!IsWithinHorizontalRadius(znv.transform.position, _undoCenter, _undoActiveRadius)) continue;

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
                _undoRefreshCoroutine = StartCoroutine(PostUndoTerrainRefresh(_undoCenter, restoredChunks));
            }

            if (!hadSnapshot)
            {
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.WarningMessageType,
                    "ValheimFloorPlan: No terrain snapshot in this session. Undo removed pieces only.");
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[FloorPlanBuilder] Undo: removed {removed} VFP pieces within {_undoActiveRadius:F0}m from center {_undoCenter}, restored {restoredChunks} terrain chunks.");
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

                    ValheimFloorPlanPlugin.ShowWrappedMessage(
                        ValheimFloorPlanPlugin.ProgressMessageType,
                        BuildUndoConfirmationMessage(secondsLeft));

                    nextUpdateAt = Time.time + UPDATE_INTERVAL;
                }

                yield return null;
            }

            _undoCountdownCoroutine = null!;
            _undoConfirmationExpireAt = 0f;
            ClearUndoHighlights();
        }

        /// <summary>
        /// Spawns a glowing ring above every VFP piece within undo range so the
        /// player can see exactly which pieces will be removed during the
        /// 5-second confirmation window.
        /// </summary>
        private void ShowUndoHighlights(Player player)
        {
            ClearUndoHighlights();
            _undoHighlightGo = new GameObject("VFP_UndoHighlight");

            var color = new Color(1f, 0.18f, 0.12f, 0.95f);
            const int terrainLayer = 1 << 11;

            foreach (var znv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (znv == null) continue;
                var zdo = znv.GetZDO();
                if (zdo == null) continue;
                if (zdo.GetString(VFP_TAG) != "1") continue;
                if (!IsWithinHorizontalRadius(znv.transform.position, _undoCenter, _undoActiveRadius)) continue;

                Vector3 pos = znv.transform.position;

                // Raycast downward to land the ring on the terrain surface.
                float ringY = pos.y;
                if (Physics.Raycast(new Vector3(pos.x, pos.y + 20f, pos.z), Vector3.down, out var hit, 40f, terrainLayer))
                    ringY = hit.point.y;
                ringY += UNDO_HIGHLIGHT_RING_LIFT;

                var lr = MakeLine(_undoHighlightGo, color, 0.09f, UNDO_HIGHLIGHT_RING_SEGMENTS);
                lr.loop = true;
                for (int i = 0; i < UNDO_HIGHLIGHT_RING_SEGMENTS; i++)
                {
                    float angle = i * Mathf.PI * 2f / UNDO_HIGHLIGHT_RING_SEGMENTS;
                    lr.SetPosition(i, new Vector3(
                        pos.x + Mathf.Cos(angle) * UNDO_HIGHLIGHT_RING_RADIUS,
                        ringY,
                        pos.z + Mathf.Sin(angle) * UNDO_HIGHLIGHT_RING_RADIUS));
                }
            }

            // Draw the outer boundary circle showing the full undo search radius.
            var boundary = MakeLine(_undoHighlightGo, new Color(1f, 0.65f, 0f, 0.92f), 0.15f, UNDO_BOUNDARY_CIRCLE_SEGMENTS);
            boundary.loop = true;
            for (int i = 0; i < UNDO_BOUNDARY_CIRCLE_SEGMENTS; i++)
            {
                float angle = i * Mathf.PI * 2f / UNDO_BOUNDARY_CIRCLE_SEGMENTS;
                float bx = _undoCenter.x + Mathf.Cos(angle) * _undoActiveRadius;
                float bz = _undoCenter.z + Mathf.Sin(angle) * _undoActiveRadius;
                float by = _undoCenter.y;
                if (Physics.Raycast(new Vector3(bx, _undoCenter.y + 300f, bz), Vector3.down, out var bHit, 600f, terrainLayer))
                    by = bHit.point.y;
                boundary.SetPosition(i, new Vector3(bx, by + UNDO_BOUNDARY_CIRCLE_LIFT, bz));
            }
        }

        private void ClearUndoHighlights()
        {
            if (_undoHighlightGo != null)
            {
                Destroy(_undoHighlightGo);
                _undoHighlightGo = null;
            }
        }

        private void CancelUndoConfirmation(Player player)
        {
            if (_undoCountdownCoroutine != null)
            {
                StopCoroutine(_undoCountdownCoroutine);
                _undoCountdownCoroutine = null!;
            }
            _undoConfirmationExpireAt = 0f;
            ClearUndoHighlights();
            player.Message(MessageHud.MessageType.Center, "ValheimFloorPlan: Undo cancelled.");
        }

        /// <summary>
        /// Polls +/- input while the undo confirmation window is open to adjust radius.
        /// Also handles arrow key input to nudge the circle center, allowing the player
        /// to move the search circle and target different sets of pieces.
        /// Refreshes highlight rings, recounts affected pieces, and restarts the countdown timer.
        /// </summary>
        private void UpdateUndoConfirmationInput()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Cancel on right-click or Escape.
            if (UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CancelUndoConfirmation(player);
                return;
            }

            bool increase = UnityEngine.Input.GetKeyDown(KeyCode.Plus)
                         || UnityEngine.Input.GetKeyDown(KeyCode.Equals)  // same physical key, unshifted
                         || UnityEngine.Input.GetKeyDown(KeyCode.KeypadPlus);
            bool decrease = UnityEngine.Input.GetKeyDown(KeyCode.Minus)
                         || UnityEngine.Input.GetKeyDown(KeyCode.KeypadMinus);

            // Handle radius adjustment with +/-.
            if (increase || decrease)
            {
                float newRadius = _undoActiveRadius + (increase ? UNDO_RADIUS_ADJUST_STEP : -UNDO_RADIUS_ADJUST_STEP);
                _undoActiveRadius = Mathf.Clamp(newRadius, 5f, 150f);

                // Persist the adjusted radius back to the config file.
                ValheimFloorPlanPlugin.SetUndoRadius(_undoActiveRadius);

                // Recount pieces at the new radius.
                CountUndoStats(player, _undoActiveRadius, _undoCenter, out int pieces, out int terrainChunks);
                _undoConfirmationPieceCount = pieces;
                _undoConfirmationTerrainChunks = terrainChunks;

                // Refresh highlight rings to match the new radius.
                ShowUndoHighlights(player);

                // Restart the confirmation timer so the player has a full window after adjusting.
                _undoConfirmationExpireAt = Time.time + UNDO_CONFIRMATION_SECONDS;
                if (_undoCountdownCoroutine != null)
                    StopCoroutine(_undoCountdownCoroutine);
                _undoCountdownCoroutine = StartCoroutine(UndoCountdownCoroutine());

                // Immediate HUD update.
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.ProgressMessageType,
                    BuildUndoConfirmationMessage((int)UNDO_CONFIRMATION_SECONDS));
                return;
            }

            // Handle circle center movement with arrow keys.
            // Compute step size (fine-adjust reduces the step).
            bool fineAdjust = IsFineAdjustHeld();
            float moveStep = fineAdjust
                ? ValheimFloorPlanPlugin.PreviewFineMoveStep
                : ValheimFloorPlanPlugin.PreviewMoveStep;

            // Get the camera-relative movement directions (same as preview mode).
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

            // Check for arrow key input (map to move directions like the preview does).
            Vector3 nudge = Vector3.zero;
            bool centerMoved = false;

            if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveForwardKey))         { nudge =  moveForward * moveStep; centerMoved = true; }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveBackwardKey))   { nudge = -moveForward * moveStep; centerMoved = true; }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveRightKey))      { nudge =  moveRight   * moveStep; centerMoved = true; }
            else if (IsPreviewKeyDown(ValheimFloorPlanPlugin.PreviewMoveLeftKey))       { nudge = -moveRight   * moveStep; centerMoved = true; }

            if (centerMoved)
            {
                _undoCenter += nudge;

                // Recount pieces at the new center.
                CountUndoStats(player, _undoActiveRadius, _undoCenter, out int pieces, out int terrainChunks);
                _undoConfirmationPieceCount = pieces;
                _undoConfirmationTerrainChunks = terrainChunks;

                // Refresh highlight rings to show the new center.
                ShowUndoHighlights(player);

                // Restart the confirmation timer so the player has a full window after moving.
                _undoConfirmationExpireAt = Time.time + UNDO_CONFIRMATION_SECONDS;
                if (_undoCountdownCoroutine != null)
                    StopCoroutine(_undoCountdownCoroutine);
                _undoCountdownCoroutine = StartCoroutine(UndoCountdownCoroutine());

                // Immediate HUD update showing the new count.
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    ValheimFloorPlanPlugin.ProgressMessageType,
                    BuildUndoConfirmationMessage((int)UNDO_CONFIRMATION_SECONDS));
            }
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
                ValheimFloorPlanPlugin.ShowWrappedMessage(
                    MessageHud.MessageType.Center,
                    $"ValheimFloorPlan: Could not load plan '{System.IO.Path.GetFileName(path)}' — {ex.Message}");
                return;
            }

            var bfPlayer = Player.m_localPlayer;
            if (bfPlayer == null) { ValheimFloorPlanPlugin.Log.LogError("No local player found."); return; }
            ValheimFloorPlanPlugin.Log.LogInfo($"Building floor plan: {plan.Pieces.Count} pieces from {path}");
            float buildYaw = GameCamera.instance != null
                ? GameCamera.instance.transform.eulerAngles.y
                : bfPlayer.transform.eulerAngles.y;
            buildYaw = SnapAngleDeg(buildYaw + 180f);
            Vector3 center = GetInitialBuildCenter(bfPlayer, plan, buildYaw);
            Vector3 origin = GetPlacementOriginFromCenter(plan, center, buildYaw);
            StartCoroutine(LevelThenPlace(plan, buildYaw, origin));
        }

        private IEnumerator LevelThenPlace(FloorPlan plan, float rotationDeg, Vector3 origin)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                ValheimFloorPlanPlugin.Log.LogError("No local player found.");
                yield break;
            }

            float snappedRotationDeg = SnapAngleDeg(rotationDeg);
            if (Mathf.Abs(Mathf.DeltaAngle(rotationDeg, snappedRotationDeg)) > 0.01f)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"Build rotation snapped: {rotationDeg:F1}\u00b0 -> {snappedRotationDeg:F1}\u00b0 (step={ValheimFloorPlanPlugin.BuildRotationSnapDegrees:F1}\u00b0)");
            }
            rotationDeg = snappedRotationDeg;

            ValheimFloorPlanPlugin.Log.LogInfo($"Build origin: {origin}  rotation={rotationDeg:F1}\u00b0");

            // Clear any previous undo state.
            _lastPlaced.Clear();
            _groundFloorScaffoldVerticals.Clear();

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

            if (ValheimFloorPlanPlugin.RoofScaffolding)
            {
                ShowBuildProgress("Placing roof scaffolding...");
                yield return StartCoroutine(PlaceRoofScaffolding(plan, origin, rotationDeg));
            }

            // Some spike meshes appear a short time AFTER leveling/placement finalizes.
            // Run a brief post-build guard to detect/remove tall non-build blockers.
            ShowBuildProgress("Final checks...");
            yield return StartCoroutine(PostBuildSpikeGuard(plan, origin, rotationDeg));

            if (!ValheimFloorPlanPlugin.DisableWelcomePost)
                yield return StartCoroutine(PlaceCenterSignage(plan, origin, rotationDeg));
        }

        private static float SnapAngleDeg(float angleDeg)
        {
            float step = Mathf.Clamp(ValheimFloorPlanPlugin.BuildRotationSnapDegrees, 0f, 90f);
            angleDeg = NormalizeAngleDeg(angleDeg);
            if (step <= 0.001f) return angleDeg;

            return NormalizeAngleDeg(Mathf.Round(angleDeg / step) * step);
        }

        private static float NormalizeAngleDeg(float angleDeg)
        {
            angleDeg %= 360f;
            if (angleDeg < 0f) angleDeg += 360f;
            return angleDeg;
        }

        /// <summary>
        /// Places a vertical 4m log pole at the centre of the build area with four stacked
        /// informational signs on its south face.
        /// </summary>
        private IEnumerator PlaceCenterSignage(FloorPlan plan, Vector3 origin, float rotationDeg)
        {
            const string POLE_PREFAB  = "wood_pole_log_4";
            const string SIGN_PREFAB  = "sign";
            const float  POLE_HEIGHT  = 4f;
            const float  SIGN_SPACING = 0.6f;
            float signageRotationDeg = rotationDeg - 180f;

            var player = Player.m_localPlayer;
            if (player == null) yield break;

            var polePrefab = ZNetScene.instance?.GetPrefab(POLE_PREFAB);
            var signPrefab = ZNetScene.instance?.GetPrefab(SIGN_PREFAB);
            if (polePrefab == null || signPrefab == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[Signage] Prefab '{POLE_PREFAB}' or '{SIGN_PREFAB}' not found — signage skipped.");
                yield break;
            }

            // Determine the true centre of the plan from its piece bounds.
            int minCol = int.MaxValue, maxColExcl = int.MinValue;
            int minRow = int.MaxValue, maxRowExcl = int.MinValue;
            foreach (var piece in plan.Pieces)
            {
                var def  = PieceMap.GetDef(piece.Type);
                int effW = def != null ? def.EffW(piece.Rotation) : 1;
                int effH = def != null ? def.EffH(piece.Rotation) : 1;
                if (piece.Col          < minCol)   minCol   = piece.Col;
                if (piece.Col + effW   > maxColExcl) maxColExcl = piece.Col + effW;
                if (piece.Row          < minRow)   minRow   = piece.Row;
                if (piece.Row + effH   > maxRowExcl) maxRowExcl = piece.Row + effH;
            }
            if (minCol == int.MaxValue)
            {
                minCol = 0; maxColExcl = plan.Cols;
                minRow = 0; maxRowExcl = plan.Rows;
            }

            float localCX = (minCol + maxColExcl) * 0.5f * PieceMap.CELL_SIZE;
            float localCZ = (minRow + maxRowExcl) * 0.5f * PieceMap.CELL_SIZE;

            Vector3 signCenter = PieceMap.TransformPlanPoint(origin, localCX, localCZ, origin.y, rotationDeg);
            float wx   = signCenter.x;
            float wz   = signCenter.z;

            float terrainY = TerrainLeveler.TargetLevelY;
            if (Physics.Raycast(new Vector3(wx, terrainY + 300f, wz), Vector3.down, out var hit, 600f, 1 << 11))
                terrainY = hit.point.y;

            float signageRad  = signageRotationDeg * Mathf.Deg2Rad;
            float signageSinR = Mathf.Sin(signageRad);
            float signageCosR = Mathf.Cos(signageRad);

            // Centre pole.
            SpawnScaffoldPole(polePrefab,
                new Vector3(wx, terrainY + POLE_HEIGHT * 0.5f, wz),
                Quaternion.Euler(0f, signageRotationDeg, 0f),
                player);
            yield return new WaitForSeconds(PLACE_DELAY);

            // Four signs stacked on the south face of the pole (facing toward the player).
            // "South in plan space" in world: direction (-sinR, 0, -cosR).
            var signTexts = new[]
            {
                "<color=white>Welcome</color>",
                "<color=white>If you like this mod please</color>",
                "<color=white>give it a 👍 at the</color>",
                "<color=white>Thunderstore Mods Site. Thx</color>"
            };

            float      signTopY  = terrainY + POLE_HEIGHT - 0.4f;
            // 0.3 m keeps the sign face clear of the pole log visually while still
            // overlapping the pole collider so Valheim treats it as attached.
            float      signOX    = -signageSinR * 0.3f;
            float      signOZ    = -signageCosR * 0.3f;
            Quaternion signRot   = Quaternion.Euler(0f, signageRotationDeg + 180f, 0f);

            for (int i = 0; i < signTexts.Length; i++)
            {
                float signY   = signTopY - i * SIGN_SPACING;
                var   signPos = new Vector3(wx + signOX, signY, wz + signOZ);

                var signGo = UnityEngine.Object.Instantiate(signPrefab, signPos, signRot);
                _lastPlaced.Add(signGo);

                // Disable support-wear so the sign never collapses from lack of attachment.
                var wnt = signGo.GetComponent<WearNTear>();
                if (wnt != null) wnt.m_noSupportWear = true;

                var znv = signGo.GetComponent<ZNetView>();
                if (znv != null)
                {
                    var zdo = znv.GetZDO();
                    if (zdo != null)
                    {
                        zdo.SetOwner(ZDOMan.GetSessionID());
                        zdo.Set(VFP_TAG, "1");
                        zdo.Set("text", signTexts[i]);
                    }
                }
                signGo.GetComponent<Piece>()?.SetCreator(player.GetPlayerID());
                yield return new WaitForSeconds(PLACE_DELAY);
            }

            ValheimFloorPlanPlugin.Log.LogInfo("[FloorPlanBuilder] Placed centre signage pole + 4 signs.");
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
            int workbenchVariantIndex = 0;
            int configuredExternalWallHeight = Mathf.Clamp(
                ValheimFloorPlanPlugin.ExternalWallHeight,
                1,
                ValheimFloorPlanPlugin.GetMaxExternalWallHeight(
                    ValheimFloorPlanPlugin.ScaffoldingLevels));
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

                string prefabName = ResolvePrefabName(piece.Type, def.Prefab, useWoodStructure);
                if (TESTING_ONLY && piece.Type == "Workbench")
                {
                    int workbenchTestSlot = workbenchVariantIndex;
                    prefabName = ResolveWorkbenchTestPrefab(workbenchVariantIndex);
                    workbenchVariantIndex++;
                    ValheimFloorPlanPlugin.Log.LogInfo(
                        $"[WorkbenchTest] slot={workbenchTestSlot} prefab={prefabName} col={piece.Col} row={piece.Row} rot={piece.Rotation}");
                }

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
                bool isExternal = IsOnPlanOuterPerimeter(
                    piece.Col, piece.Row, effW, effH,
                    minCol, maxColExclusive, minRow, maxRowExclusive);

                if (useWoodStructure && piece.Type == "Wall" && isExternal)
                {
                    effectivePieceRotation = GetExteriorWallRotation(
                        piece.Col, piece.Row, effW, effH,
                        minCol, maxColExclusive, minRow, maxRowExclusive,
                        effectivePieceRotation);
                    effW = def.EffW(effectivePieceRotation);
                    effH = def.EffH(effectivePieceRotation);
                }

                // Keep perimeter wood walls facing outward even if a plan carries an
                // Inner face flag there; only interior wood walls should flip inward.
                if (useWoodStructure && piece.Type == "Wall" && piece.WallFace == WallFaceMode.Inner && !isExternal)
                {
                    effectivePieceRotation = (effectivePieceRotation + 180) % 360;
                    effW = def.EffW(effectivePieceRotation);
                    effH = def.EffH(effectivePieceRotation);
                }

                // Convert from top-left grid corner (B4J storage) to world centre,
                // then rotate the offset around the player origin by the plan rotation.
                // Unity clockwise Y-rotation: x' = dx*cos + dz*sin, z' = -dx*sin + dz*cos.
                float dx = (piece.Col + effW * 0.5f) * PieceMap.CELL_SIZE;
                float dz = (piece.Row + effH * 0.5f) * PieceMap.CELL_SIZE;
                Vector3 pieceCenter = PieceMap.TransformPlanPoint(origin, dx, dz, origin.y, rotationDeg);
                float x  = pieceCenter.x;
                float z  = pieceCenter.z;

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
                bool shouldStack = IsExternalWallOrPillarType(piece.Type) && isExternal;
                int stackCount = shouldStack ? configuredExternalWallHeight : 1;
                float stackStepY = GetStackStepY(piece.Type);
                float pieceYaw = PieceMap.TransformLocalYaw(effectivePieceRotation + def.RotationOffset, rotationDeg);
                var rot = Quaternion.Euler(0, pieceYaw, 0);

                // Wood pieces are narrower/thinner than their stone equivalents; push
                // external pieces outward so their outer face aligns with the floor edge.
                // Direction is derived from which plan edge the piece sits on, not its
                // own rotation (which would give wrong results for south/west walls).
                Vector3 materialOffset = Vector3.zero;
                if (((useWoodStructure && (piece.Type == "Wall" || piece.Type == "Pillar")) || piece.Type == "Doorway") && isExternal)
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

                    bool centerWorkbench = piece.Type == "Workbench";
                    SpawnRegisteredPiece(prefab, stackedPos, rot, player, centerWorkbench);

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

        private static void CenterPieceOnRenderedBoundsXZ(GameObject go, Vector3 desiredCenter)
        {
            if (!TryGetBoundsCenterXZ(go, includeColliders: true, includeRenderers: false, out Vector3 actualCenter, out string source) &&
                !TryGetBoundsCenterXZ(go, includeColliders: false, includeRenderers: true, out actualCenter, out source))
                return;

            const float MAX_RECENTER_DISTANCE = 5f;
            float sourceDeltaX = actualCenter.x - go.transform.position.x;
            float sourceDeltaZ = actualCenter.z - go.transform.position.z;
            float sourceDistance = Mathf.Sqrt(sourceDeltaX * sourceDeltaX + sourceDeltaZ * sourceDeltaZ);
            if (sourceDistance > MAX_RECENTER_DISTANCE)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[WorkbenchTest] skipped recenter prefab={go.name} source={source} sourceDistance={sourceDistance:F3} actual=({actualCenter.x:F2}, {actualCenter.z:F2}) spawn=({go.transform.position.x:F2}, {go.transform.position.z:F2})");
                return;
            }

            Vector3 adjusted = go.transform.position;
            adjusted.x += desiredCenter.x - actualCenter.x;
            adjusted.z += desiredCenter.z - actualCenter.z;

            float deltaX = adjusted.x - go.transform.position.x;
            float deltaZ = adjusted.z - go.transform.position.z;
            if (Mathf.Abs(deltaX) > 0.001f || Mathf.Abs(deltaZ) > 0.001f)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[WorkbenchTest] recentered prefab={go.name} source={source} dx={deltaX:F3} dz={deltaZ:F3} desired=({desiredCenter.x:F2}, {desiredCenter.z:F2}) actual=({actualCenter.x:F2}, {actualCenter.z:F2})");
            }

            go.transform.position = adjusted;
        }

        private static bool TryGetBoundsCenterXZ(GameObject go, bool includeColliders, bool includeRenderers, out Vector3 center, out string source)
        {
            bool haveBounds = false;
            Bounds combined = default;

            if (includeColliders)
            {
                var colliders = go.GetComponentsInChildren<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i];
                    if (collider == null || !collider.enabled)
                        continue;

                    if (!haveBounds)
                    {
                        combined = collider.bounds;
                        haveBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(collider.bounds);
                    }
                }

                if (haveBounds)
                {
                    center = combined.center;
                    source = "collider";
                    return true;
                }
            }

            if (includeRenderers)
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null || !renderer.enabled)
                        continue;

                    if (!haveBounds)
                    {
                        combined = renderer.bounds;
                        haveBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderer.bounds);
                    }
                }

                if (haveBounds)
                {
                    center = combined.center;
                    source = "renderer";
                    return true;
                }
            }

            center = go.transform.position;
            source = "none";
            return false;
        }

        private static void ShowBuildProgress(string message)
        {
            ValheimFloorPlanPlugin.ShowProgressMessage(message);
        }

        private static string ResolveWorkbenchTestPrefab(int variantIndex)
        {
            if (TEST_WORKBENCH_PREFABS.Length == 0)
                return "piece_workbench";

            int index = Mathf.Abs(variantIndex) % TEST_WORKBENCH_PREFABS.Length;
            return TEST_WORKBENCH_PREFABS[index];
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

        private static int GetExteriorWallRotation(
            int col, int row, int effW, int effH,
            int minCol, int maxColExclusive, int minRow, int maxRowExclusive,
            int fallbackRotation)
        {
            if (effW == 1)
            {
                if (col <= minCol) return 270;
                if ((col + effW) >= maxColExclusive) return 90;
            }
            else if (effH == 1)
            {
                if (row <= minRow) return 180;
                if ((row + effH) >= maxRowExclusive) return 0;
            }

            return fallbackRotation;
        }

        /// <summary>
        /// Returns the world-space offset to apply to a perimeter Wall, Doorway, or Pillar so its
        /// outer face aligns with the outer face of the equivalent stone piece.
        ///
        /// Offset is derived from Valheim prefab geometry:
        ///   wood_wall_half depth = 0.3 m  → half = 0.15 m  → shift = 0.35 m to sit on a 1 m tile edge
        ///   wood_pole_log  width = 0.2 m  → half = 0.10 m  → shift = 0.40 m to sit on a 1 m tile edge
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
            // Per-prefab outward shift so the wood piece's outer face sits on the tile edge.
            float shift = pieceType == "Pillar" ? 0.40f : 0.35f;

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
            if (pieceType == "Wall" || pieceType == "Doorway")
            {
                if (effH == 1) lx = 0f;   // east-west wall: suppress X shift
                else           lz = 0f;   // north-south wall: suppress Z shift
            }

            if (lx == 0f && lz == 0f) return Vector3.zero;

            // Apply per-axis shift (not normalised: corner pillars shift in both axes).
            var localOffset = new Vector3(lx * shift, 0f, lz * shift);

            // Convert through the shared mirrored plan transform so outward edge
            // shifts stay consistent with the build-space handedness fix.
            Vector2 worldOffset = PieceMap.TransformLocalXZ(localOffset.x, localOffset.z, planRotationDeg);
            return new Vector3(worldOffset.x, 0f, worldOffset.y);
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

        // ── Roof Scaffolding ──────────────────────────────────────────────────

        /// <summary>
        /// Places a ring of vertical 4m log poles at every corner of the build area,
        /// on the left and right adjacent cells of every perimeter doorway, and at
        /// actual 4m edge beam joins. Once all vertical poles are placed the tops are
        /// connected with horizontal 4m log beams running clockwise around the
        /// perimeter. No horizontal beam extends beyond the boundary corners.
        /// </summary>
        private IEnumerator PlaceRoofScaffolding(FloorPlan plan, Vector3 origin, float rotationDeg)
        {
            ValheimFloorPlanPlugin.RefreshScaffoldingRules();

            const string VERT_PREFAB    = "woodiron_pole";
            const string HORIZ_PREFAB   = "woodiron_beam";
            const string FLOOR2_PREFAB  = "wood_floor";
            const string FLOOR1_PREFAB  = "wood_floor_1x1";
            const string ROOF_TOP_PREFAB = "wood_roof_top";
            const string TOP_ROOF_LOWER_PREFAB = "wood_roof";
            const string TOP_SUPPORT_LOWER_PREFAB = "woodiron_beam_26";
            const string CHIMNEY_WALL2_PREFAB = "wood_wall_half";
            const string CHIMNEY_WALL1_PREFAB = "wood_wall_1x1";
            const string CHIMNEY_ROOF_PREFAB = "wood_roof";
            const float  HEARTH_ACCESS_CLEARANCE = 3f;
            const float  CHIMNEY_CAP_EXTRA_HEIGHT = 2f;
            const float  POLE_SEGMENT_HEIGHT = 2f;
            const float  POLE_SPACING   = 4f;
            const float  HORIZ_LEN      = 2f;
            const float  HORIZ_HALF     = HORIZ_LEN * 0.5f;
            const float  FLOOR_DECK_DROP = 0.05f;

            var player = Player.m_localPlayer;
            if (player == null) yield break;

            var vertPrefab = ZNetScene.instance?.GetPrefab(VERT_PREFAB);
            var horizPrefab = ZNetScene.instance?.GetPrefab(HORIZ_PREFAB);
            var floor2Prefab = ZNetScene.instance?.GetPrefab(FLOOR2_PREFAB);
            var floor1Prefab = ZNetScene.instance?.GetPrefab(FLOOR1_PREFAB);
            var roofTopPrefab = ZNetScene.instance?.GetPrefab(ROOF_TOP_PREFAB);
            var topLowerRoofPrefab = ZNetScene.instance?.GetPrefab(TOP_ROOF_LOWER_PREFAB);
            var topLowerSupportPrefab = ZNetScene.instance?.GetPrefab(TOP_SUPPORT_LOWER_PREFAB);
            var chimneyWall2Prefab = ZNetScene.instance?.GetPrefab(CHIMNEY_WALL2_PREFAB);
            var chimneyWall1Prefab = ZNetScene.instance?.GetPrefab(CHIMNEY_WALL1_PREFAB);
            var chimneyRoofPrefab = ZNetScene.instance?.GetPrefab(CHIMNEY_ROOF_PREFAB);
            if (vertPrefab == null || horizPrefab == null || floor2Prefab == null || floor1Prefab == null)
            {
                ValheimFloorPlanPlugin.Log.LogWarning(
                    $"[Scaffolding] Prefab '{VERT_PREFAB}', '{HORIZ_PREFAB}', '{FLOOR2_PREFAB}', or '{FLOOR1_PREFAB}' not found in ZNetScene — roof scaffolding skipped.");
                yield break;
            }

            // ── Plan bounds in local (unrotated) space ────────────────────────
            GetPlanPieceBounds(plan,
                out int minCol, out int maxColExclusive,
                out int minRow, out int maxRowExclusive);

            float lMinX = minCol          * PieceMap.CELL_SIZE;
            float lMaxX = maxColExclusive * PieceMap.CELL_SIZE;
            float lMinZ = minRow          * PieceMap.CELL_SIZE;
            float lMaxZ = maxRowExclusive * PieceMap.CELL_SIZE;

            float width     = lMaxX - lMinX;
            float depth     = lMaxZ - lMinZ;
            float perimeter = 2f * (width + depth);
            int scaffoldLevels = Mathf.Clamp(ValheimFloorPlanPlugin.ScaffoldingLevels, 1, 3);
            bool useTransverseScaffoldingBeams = ValheimFloorPlanPlugin.GetEffectiveTransverseScaffoldingBeams(scaffoldLevels);
            bool useLongitudinalScaffoldingBeams = ValheimFloorPlanPlugin.GetEffectiveLongitudinalScaffoldingBeams(scaffoldLevels);
            var scaffoldFloorHeights = new float[scaffoldLevels];
            for (int level = 0; level < scaffoldLevels; level++)
            {
                scaffoldFloorHeights[level] = Mathf.Max(2f, ValheimFloorPlanPlugin.GetScaffoldingFloorHeightForLevel(level));
            }

            float scaffoldBaseY = TerrainLeveler.TargetLevelY;
            float localCenterX = (lMinX + lMaxX) * 0.5f;
            float localCenterZ = (lMinZ + lMaxZ) * 0.5f;
            Vector3 centerWorld = PieceMap.TransformPlanPoint(origin, localCenterX, localCenterZ, scaffoldBaseY + scaffoldFloorHeights[0] * 0.5f, rotationDeg);

            // ── Collect perimeter parameters for all poles ────────────────────
            // The perimeter is parameterised as clockwise distance from the SW corner:
            //   0            → SW corner (minX, minZ)
            //   width        → SE corner (maxX, minZ)
            //   width+depth  → NE corner (maxX, maxZ)
            //   2*width+depth→ NW corner (minX, maxZ)
            var poleParams = new List<float>
            {
                0f,
                width,
                width + depth,
                2f * width + depth
            };
            var doorJambParams = new List<float>();
            var doorEdgeSpans = new List<ScaffoldDoorSpan>();
            var blockedLongitudinalLocalXs = new List<ScaffoldDoorSpan>();
            var blockedTransverseLocalZs = new List<ScaffoldDoorSpan>();
            var doorCenters = new List<Vector2>();

            // Add poles on the left and right adjacent cells of each perimeter doorway.
            foreach (var piece in plan.Pieces)
            {
                if (piece.Type != "Doorway") continue;
                var def = PieceMap.GetDef(piece.Type);
                if (def == null) continue;

                int effW = def.EffW(piece.Rotation);
                int effH = def.EffH(piece.Rotation);

                bool onSouth = piece.Row <= minRow;
                bool onNorth = piece.Row + effH >= maxRowExclusive;
                bool onEast  = piece.Col + effW >= maxColExclusive;
                bool onWest  = piece.Col <= minCol;

                // Only place poles if doorway is on exactly one edge (not a corner).
                int edgeCount = (onSouth ? 1 : 0) + (onNorth ? 1 : 0) + (onEast ? 1 : 0) + (onWest ? 1 : 0);
                if (edgeCount != 1) continue;

                doorCenters.Add(new Vector2(
                    (piece.Col + effW * 0.5f) * PieceMap.CELL_SIZE,
                    (piece.Row + effH * 0.5f) * PieceMap.CELL_SIZE));

                float tLeft = -1f, tRight = -1f;

                if (onSouth)
                {
                    // Doorway on south edge running east-west.
                    // Left = west cell, Right = east cell.
                    tLeft = (piece.Col - minCol) * PieceMap.CELL_SIZE;
                    tRight = (piece.Col + effW - minCol) * PieceMap.CELL_SIZE;
                    blockedLongitudinalLocalXs.Add(new ScaffoldDoorSpan(
                        (piece.Col - minCol) * PieceMap.CELL_SIZE,
                        (piece.Col + effW - minCol) * PieceMap.CELL_SIZE));
                }
                else if (onNorth)
                {
                    // Doorway on north edge running east-west.
                    // Left = west cell, Right = east cell.
                    tLeft = width + depth + (maxColExclusive - piece.Col - effW) * PieceMap.CELL_SIZE;
                    tRight = width + depth + (maxColExclusive - piece.Col) * PieceMap.CELL_SIZE;
                    blockedLongitudinalLocalXs.Add(new ScaffoldDoorSpan(
                        (piece.Col - minCol) * PieceMap.CELL_SIZE,
                        (piece.Col + effW - minCol) * PieceMap.CELL_SIZE));
                }
                else if (onEast)
                {
                    // Doorway on east edge running north-south.
                    // Left = north cell, Right = south cell.
                    tLeft = width + (piece.Row + effH - minRow) * PieceMap.CELL_SIZE;
                    tRight = width + (piece.Row - minRow) * PieceMap.CELL_SIZE;
                    blockedTransverseLocalZs.Add(new ScaffoldDoorSpan(
                        (piece.Row - minRow) * PieceMap.CELL_SIZE,
                        (piece.Row + effH - minRow) * PieceMap.CELL_SIZE));
                }
                else if (onWest)
                {
                    // Doorway on west edge running north-south.
                    // Left = north cell, Right = south cell.
                    tLeft = 2f * width + depth + (maxRowExclusive - piece.Row - effH) * PieceMap.CELL_SIZE;
                    tRight = 2f * width + depth + (maxRowExclusive - piece.Row) * PieceMap.CELL_SIZE;
                    blockedTransverseLocalZs.Add(new ScaffoldDoorSpan(
                        (piece.Row - minRow) * PieceMap.CELL_SIZE,
                        (piece.Row + effH - minRow) * PieceMap.CELL_SIZE));
                }

                if (tLeft >= 0f && tLeft <= perimeter)
                {
                    poleParams.Add(tLeft);
                    doorJambParams.Add(tLeft);
                }
                if (tRight >= 0f && tRight <= perimeter)
                {
                    poleParams.Add(tRight);
                    doorJambParams.Add(tRight);
                }

                if (tLeft >= 0f && tLeft <= perimeter && tRight >= 0f && tRight <= perimeter)
                {
                    float spanMin = Mathf.Min(tLeft, tRight);
                    float spanMax = Mathf.Max(tLeft, tRight);
                    doorEdgeSpans.Add(new ScaffoldDoorSpan(spanMin, spanMax));
                }
            }

            poleParams.Sort();
            ScaffoldDedup(poleParams, 0.5f);
            doorJambParams.Sort();
            ScaffoldDedup(doorJambParams, 0.5f);
            poleParams.Sort();

            var supportFurnitureExclusions = BuildScaffoldFurnitureExclusions(plan);
            var hearthOpenings = BuildHearthOpenings(plan);

            // Add intermediate edge-join poles once to establish full anchor set used by
            // all levels. The same anchor topology is then repeated upward every 4m.
            var edgeJoinParams = new List<float>();
            float[] cornerT = { 0f, width, width + depth, 2f * width + depth };
            int[] edgeFrom = { 0, 1, 2, 3 };
            int[] edgeTo = { 1, 2, 3, 0 };
            for (int e = 0; e < 4; e++)
            {
                Vector2 aLocal = ScaffoldParamToLocal(cornerT[edgeFrom[e]], lMinX, lMaxX, lMinZ, lMaxZ, perimeter);
                Vector2 bLocal = ScaffoldParamToLocal(cornerT[edgeTo[e]], lMinX, lMaxX, lMinZ, lMaxZ, perimeter);
                float edgeDist = Vector2.Distance(aLocal, bLocal);
                for (float joinDist = POLE_SPACING; joinDist < edgeDist - 0.05f; joinDist += POLE_SPACING)
                {
                    float joinT = cornerT[edgeFrom[e]] + joinDist;
                    if (joinT >= perimeter) joinT -= perimeter;

                    if (IsNearAnyParam(joinT, poleParams, 0.25f) ||
                        IsNearAnyParam(joinT, edgeJoinParams, 0.25f) ||
                        IsWithinAnyDoorSpan(joinT, doorEdgeSpans, 0.25f))
                        continue;

                    edgeJoinParams.Add(joinT);
                }
            }

            if (edgeJoinParams.Count > 0)
            {
                poleParams.AddRange(edgeJoinParams);
                poleParams.Sort();
                ScaffoldDedup(poleParams, 0.5f);
            }

            int placed = 0;
            float currentLevelBaseY = scaffoldBaseY;

            for (int level = 0; level < scaffoldLevels; level++)
            {
                float scaffoldFloorHeight = scaffoldFloorHeights[level];
                float levelTopY = currentLevelBaseY + scaffoldFloorHeight;
                var occupiedPoleLocals = new List<Vector2>(poleParams.Count + 32);
                for (int pi = 0; pi < poleParams.Count; pi++)
                {
                    occupiedPoleLocals.Add(ScaffoldParamToLocal(poleParams[pi], lMinX, lMaxX, lMinZ, lMaxZ, perimeter));
                }
                occupiedPoleLocals.Add(new Vector2(localCenterX, localCenterZ));

                // ── Place vertical poles for this level ──────────────────────
                foreach (float t in poleParams)
                {
                    Vector2 local = ScaffoldParamToLocal(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter);
                    Vector3 polePos = PieceMap.TransformPlanPoint(origin, local.x, local.y, currentLevelBaseY + scaffoldFloorHeight * 0.5f, rotationDeg);

                    placed += SpawnScaffoldColumn(vertPrefab,
                        polePos,
                        Quaternion.Euler(0, rotationDeg, 0),
                        player,
                        scaffoldFloorHeight,
                        POLE_SEGMENT_HEIGHT);
                    if (placed % 10 == 0)
                        yield return new WaitForSeconds(PLACE_DELAY);
                }

                    Vector3 centerPolePos = PieceMap.TransformPlanPoint(origin, localCenterX, localCenterZ, currentLevelBaseY + scaffoldFloorHeight * 0.5f, rotationDeg);
                placed += SpawnScaffoldColumn(
                    vertPrefab,
                    centerPolePos,
                    Quaternion.Euler(0, rotationDeg, 0),
                    player,
                    scaffoldFloorHeight,
                    POLE_SEGMENT_HEIGHT);
                if (placed % 10 == 0)
                    yield return new WaitForSeconds(PLACE_DELAY);

                // ── Place horizontal perimeter beams for this level ──────────
                // Corners clockwise: SW(0) → SE(1) → NE(2) → NW(3) → SW(0)
                var cornerTops = new Vector3[4];
                for (int ci = 0; ci < 4; ci++)
                {
                    Vector2 cl = ScaffoldParamToLocal(cornerT[ci], lMinX, lMaxX, lMinZ, lMaxZ, perimeter);
                    cornerTops[ci] = PieceMap.TransformPlanPoint(origin, cl.x, cl.y, levelTopY, rotationDeg);
                }

                for (int e = 0; e < 4; e++)
                {
                    Vector3 cA = cornerTops[edgeFrom[e]];
                    Vector3 cB = cornerTops[edgeTo[e]];
                    float edgeDx = cB.x - cA.x;
                    float edgeDz = cB.z - cA.z;
                    float edgeDist = Mathf.Sqrt(edgeDx * edgeDx + edgeDz * edgeDz);
                    if (edgeDist < 0.1f) continue;

                    Vector3 dir = new Vector3(edgeDx / edgeDist, 0f, edgeDz / edgeDist);
                    float beamY = (cA.y + cB.y) * 0.5f;
                    // woodiron_beam has its beam length along local X at 0°.
                    // Unity CW Y-rotation θ maps local X → (cosθ, 0, −sinθ), so θ = atan2(−dz, dx).
                    Quaternion beamRot = Quaternion.Euler(0f, Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg, 0f);

                    int nFull = Mathf.FloorToInt(edgeDist / HORIZ_LEN);
                    float remainder = edgeDist - nFull * HORIZ_LEN;

                    for (int b = 0; b < nFull; b++)
                    {
                        Vector3 center = cA + dir * (b * HORIZ_LEN + HORIZ_HALF);
                        center.y = beamY;
                        SpawnScaffoldPole(horizPrefab, center, beamRot, player);
                        placed++;
                        if (placed % 10 == 0) yield return new WaitForSeconds(PLACE_DELAY);
                    }

                    if (remainder > 0.05f)
                    {
                        Vector3 center = cB - dir * HORIZ_HALF;
                        center.y = beamY;
                        SpawnScaffoldPole(horizPrefab, center, beamRot, player);
                        placed++;
                        if (placed % 10 == 0) yield return new WaitForSeconds(PLACE_DELAY);
                    }
                }

                // ── Place transverse beams (West to East) for this level ─────
                if (useTransverseScaffoldingBeams)
                {
                    placed += PlaceTransverseBeams(
                        poleParams, width, depth, lMinX, lMaxX, lMinZ, lMaxZ, perimeter,
                        doorJambParams, blockedTransverseLocalZs, doorCenters, supportFurnitureExclusions, occupiedPoleLocals, origin, rotationDeg,
                        levelTopY, horizPrefab, vertPrefab, player);
                }

                // ── Place longitudinal beams (South to North) for this level ─
                if (useLongitudinalScaffoldingBeams)
                {
                    placed += PlaceLongitudinalBeams(
                        poleParams, width, depth, lMinX, lMaxX, lMinZ, lMaxZ, perimeter,
                        doorJambParams, blockedLongitudinalLocalXs, doorCenters, supportFurnitureExclusions, occupiedPoleLocals, origin, rotationDeg,
                        levelTopY, horizPrefab, vertPrefab, player);
                }

                if (ValheimFloorPlanPlugin.ScaffoldingFloors)
                {
                    bool isTopmostLevel = level == scaffoldLevels - 1;
                    placed += PlaceScaffoldLevelFloorDeck(
                        minCol, maxColExclusive, minRow, maxRowExclusive,
                        origin, rotationDeg,
                        levelTopY - FLOOR_DECK_DROP,
                        floor2Prefab, floor1Prefab, roofTopPrefab,
                        topLowerRoofPrefab, topLowerSupportPrefab,
                        isTopmostLevel, hearthOpenings, player);
                }

                if (hearthOpenings.Count > 0 && chimneyWall2Prefab != null)
                {
                    placed += PlaceHearthChimneyLevel(
                        hearthOpenings,
                        origin,
                        rotationDeg,
                        currentLevelBaseY,
                        levelTopY,
                        scaffoldBaseY + HEARTH_ACCESS_CLEARANCE,
                        chimneyWall2Prefab,
                        chimneyWall1Prefab,
                        player);
                }

                currentLevelBaseY = levelTopY;
            }

            if (hearthOpenings.Count > 0 && chimneyWall2Prefab != null)
            {
                placed += PlaceHearthChimneyTop(
                    hearthOpenings,
                    origin,
                    rotationDeg,
                    currentLevelBaseY,
                    currentLevelBaseY + CHIMNEY_CAP_EXTRA_HEIGHT,
                    chimneyWall2Prefab,
                    chimneyWall1Prefab,
                    chimneyRoofPrefab,
                    player);
            }

            PruneGroundFloorScaffoldVerticals(doorCenters, centerWorld, origin, rotationDeg);

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[Scaffolding] Placed {placed} roof scaffolding pieces ({poleParams.Count + 1} vertical columns per level across {scaffoldLevels} scaffold levels).");
        }

        private int PlaceScaffoldLevelFloorDeck(
            int minCol, int maxColExclusive, int minRow, int maxRowExclusive,
            Vector3 origin, float rotationDeg, float deckY,
            GameObject floor2Prefab, GameObject floor1Prefab, GameObject? roofTopPrefab,
            GameObject? topLowerRoofPrefab, GameObject? topLowerSupportPrefab,
            bool useRoofTop,
            List<HearthOpening> hearthOpenings, Player player)
        {
            if (useRoofTop && topLowerRoofPrefab != null)
            {
                return PlaceTopScaffoldGableRoof(
                    minCol,
                    maxColExclusive,
                    minRow,
                    maxRowExclusive,
                    origin,
                    rotationDeg,
                    deckY,
                    topLowerRoofPrefab,
                    topLowerSupportPrefab,
                    hearthOpenings,
                    player);
            }

            int placed = 0;
            Quaternion deckRot = Quaternion.Euler(0f, rotationDeg, 0f);

            for (int row = minRow; row < maxRowExclusive; row++)
            {
                for (int col = minCol; col < maxColExclusive; )
                {
                    if (IsBlockedByHearthOpening(col, row, hearthOpenings))
                    {
                        col++;
                        continue;
                    }

                    bool useFloor2 =
                        col + 1 < maxColExclusive &&
                        row + 1 < maxRowExclusive &&
                        !IsBlockedByHearthOpening(col + 1, row, hearthOpenings) &&
                        !IsBlockedByHearthOpening(col, row + 1, hearthOpenings) &&
                        !IsBlockedByHearthOpening(col + 1, row + 1, hearthOpenings);

                    int tileWidth = useFloor2 ? 2 : 1;
                    int tileDepth = useFloor2 ? 2 : 1;
                    var prefab = useFloor2
                        ? (useRoofTop && roofTopPrefab != null ? roofTopPrefab : floor2Prefab)
                        : floor1Prefab;

                    float dx = (col + tileWidth * 0.5f) * PieceMap.CELL_SIZE;
                    float dz = (row + tileDepth * 0.5f) * PieceMap.CELL_SIZE;
                    Vector3 deckPos = PieceMap.TransformPlanPoint(origin, dx, dz, deckY, rotationDeg);

                    SpawnRegisteredPiece(prefab, deckPos, deckRot, player);
                    placed++;
                    col += tileWidth;
                }

            }

            return placed;
        }

        private int PlaceTopScaffoldGableRoof(
            int minCol,
            int maxColExclusive,
            int minRow,
            int maxRowExclusive,
            Vector3 origin,
            float rotationDeg,
            float roofBaseY,
            GameObject roofPrefab,
            GameObject? supportPrefab,
            List<HearthOpening> hearthOpenings,
            Player player)
        {
            int placed = 0;
            bool ridgeRunsAlongX = (maxColExclusive - minCol) >= (maxRowExclusive - minRow);
            const float GABLE_PITCH_DEGREES = 26f;
            const float SLOPED_SEGMENT_LENGTH = 2f;
            float pitchRadians = GABLE_PITCH_DEGREES * Mathf.Deg2Rad;

            if (ridgeRunsAlongX)
            {
                float centerZ = (minRow + maxRowExclusive) * 0.5f;
                float southEdgeZ = minRow;
                float northEdgeZ = maxRowExclusive;
                float halfSpan = Mathf.Abs(centerZ - southEdgeZ);
                float ridgeY = roofBaseY + Mathf.Tan(pitchRadians) * halfSpan;

                for (int col = minCol; col < maxColExclusive; col += 2)
                {
                    int stripWidth = Mathf.Min(2, maxColExclusive - col);
                    float localX = col + stripWidth * 0.5f;

                    placed += PlaceSlopedRoofRun(
                        new Vector2(localX, southEdgeZ),
                        new Vector2(localX, centerZ),
                        roofBaseY,
                        ridgeY,
                        180f,
                        SLOPED_SEGMENT_LENGTH,
                        supportPrefab,
                        roofPrefab,
                        origin,
                        rotationDeg,
                        hearthOpenings,
                        player);
                    placed += PlaceSlopedRoofRun(
                        new Vector2(localX, northEdgeZ),
                        new Vector2(localX, centerZ),
                        roofBaseY,
                        ridgeY,
                        0f,
                        SLOPED_SEGMENT_LENGTH,
                        supportPrefab,
                        roofPrefab,
                        origin,
                        rotationDeg,
                        hearthOpenings,
                        player);
                }
            }
            else
            {
                float centerX = (minCol + maxColExclusive) * 0.5f;
                float westEdgeX = minCol;
                float eastEdgeX = maxColExclusive;
                float halfSpan = Mathf.Abs(centerX - westEdgeX);
                float ridgeY = roofBaseY + Mathf.Tan(pitchRadians) * halfSpan;

                for (int row = minRow; row < maxRowExclusive; row += 2)
                {
                    int stripDepth = Mathf.Min(2, maxRowExclusive - row);
                    float localZ = row + stripDepth * 0.5f;

                    placed += PlaceSlopedRoofRun(
                        new Vector2(westEdgeX, localZ),
                        new Vector2(centerX, localZ),
                        roofBaseY,
                        ridgeY,
                        270f,
                        SLOPED_SEGMENT_LENGTH,
                        supportPrefab,
                        roofPrefab,
                        origin,
                        rotationDeg,
                        hearthOpenings,
                        player);
                    placed += PlaceSlopedRoofRun(
                        new Vector2(eastEdgeX, localZ),
                        new Vector2(centerX, localZ),
                        roofBaseY,
                        ridgeY,
                        90f,
                        SLOPED_SEGMENT_LENGTH,
                        supportPrefab,
                        roofPrefab,
                        origin,
                        rotationDeg,
                        hearthOpenings,
                        player);
                }
            }

            return placed;
        }

        private int PlaceSlopedRoofRun(
            Vector2 startLocal,
            Vector2 ridgeLocal,
            float startY,
            float ridgeY,
            float localYaw,
            float segmentLength,
            GameObject? supportPrefab,
            GameObject roofPrefab,
            Vector3 origin,
            float rotationDeg,
            List<HearthOpening> hearthOpenings,
            Player player)
        {
            const float ROOF_SUPPORT_VERTICAL_OFFSET = -0.08f;
            const float ROOF_SUPPORT_INWARD_OFFSET = 0.12f;

            Vector2 delta = ridgeLocal - startLocal;
            float horizontalLength = delta.magnitude;
            if (horizontalLength < 0.01f)
                return 0;

            float verticalRise = ridgeY - startY;
            float slopeLength = Mathf.Sqrt(horizontalLength * horizontalLength + verticalRise * verticalRise);
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(slopeLength / segmentLength));
            int placed = 0;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                float t = (segment + 0.5f) / segmentCount;
                Vector2 local = Vector2.Lerp(startLocal, ridgeLocal, t);
                float localY = Mathf.Lerp(startY, ridgeY, t);
                Vector2 inwardDir = delta.normalized;
                Vector2 roofLocal = local + inwardDir * ROOF_SUPPORT_INWARD_OFFSET;
                float roofY = localY + ROOF_SUPPORT_VERTICAL_OFFSET;

                placed += PlaceTopSupportPieceIfClear(local.x, local.y, localY, localYaw, origin, rotationDeg, supportPrefab, hearthOpenings, player);
                placed += PlaceTopRoofPieceIfClear(roofLocal.x, roofLocal.y, roofY, localYaw, origin, rotationDeg, roofPrefab, hearthOpenings, player);
            }

            return placed;
        }

        private int PlaceTopRoofPieceIfClear(
            float localX,
            float localZ,
            float localY,
            float localYaw,
            Vector3 origin,
            float rotationDeg,
            GameObject? prefab,
            List<HearthOpening> hearthOpenings,
            Player player)
        {
            if (prefab == null || IsInsideAnyHearthOpening(localX, localZ, hearthOpenings))
                return 0;

            return PlaceChimneyRoofPiece(localX, localZ, localY, localYaw, origin, rotationDeg, prefab, player);
        }

        private int PlaceTopSupportPieceIfClear(
            float localX,
            float localZ,
            float localY,
            float localYaw,
            Vector3 origin,
            float rotationDeg,
            GameObject? prefab,
            List<HearthOpening> hearthOpenings,
            Player player)
        {
            if (prefab == null || IsInsideAnyHearthOpening(localX, localZ, hearthOpenings))
                return 0;

            return PlaceChimneyRoofPiece(localX, localZ, localY, localYaw + 270f, origin, rotationDeg, prefab, player);
        }

        private int PlaceHearthChimneyLevel(
            List<HearthOpening> hearthOpenings,
            Vector3 origin,
            float rotationDeg,
            float levelBaseY,
            float levelTopY,
            float chimneyStartY,
            GameObject wall2Prefab,
            GameObject? wall1Prefab,
            Player player)
        {
            int placed = 0;
            float enclosedBaseY = Mathf.Max(levelBaseY, chimneyStartY);
            if (enclosedBaseY >= levelTopY - 0.01f)
                return 0;

            int wallLayers = Mathf.Max(1, Mathf.RoundToInt(levelTopY - enclosedBaseY));

            for (int i = 0; i < hearthOpenings.Count; i++)
            {
                var opening = hearthOpenings[i];
                for (int layer = 0; layer < wallLayers; layer++)
                {
                    float wallY = enclosedBaseY + 0.5f + layer;
                    placed += PlaceChimneyWallRun(opening.MinCol, opening.Width, opening.MinRow, true, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                    placed += PlaceChimneyWallRun(opening.MinCol, opening.Width, opening.MaxRowExclusive, true, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                    placed += PlaceChimneyWallRun(opening.MinRow, opening.Height, opening.MinCol, false, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                    placed += PlaceChimneyWallRun(opening.MinRow, opening.Height, opening.MaxColExclusive, false, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                }
            }

            return placed;
        }

        private int PlaceChimneyWallRun(
            int startCell,
            int spanCells,
            int boundaryCell,
            bool alongX,
            float wallY,
            Vector3 origin,
            float rotationDeg,
            GameObject wall2Prefab,
            GameObject? wall1Prefab,
            Player player)
        {
            int placed = 0;
            int consumed = 0;

            while (consumed < spanCells)
            {
                bool useOneMeterPiece = spanCells - consumed == 1 && wall1Prefab != null;
                int desiredSpan = useOneMeterPiece ? 1 : Mathf.Min(2, spanCells - consumed);
                float centerPrimary = startCell + consumed + desiredSpan * 0.5f;
                GameObject prefab = useOneMeterPiece ? wall1Prefab! : wall2Prefab;
                float localX = alongX ? centerPrimary : boundaryCell;
                float localZ = alongX ? boundaryCell : centerPrimary;
                float localYaw = alongX ? 0f : 90f;
                Vector3 wallPos = PieceMap.TransformPlanPoint(origin, localX, localZ, wallY, rotationDeg);
                Quaternion wallRot = Quaternion.Euler(0f, PieceMap.TransformLocalYaw(localYaw, rotationDeg), 0f);

                SpawnRegisteredPiece(prefab, wallPos, wallRot, player);
                placed++;
                consumed += desiredSpan;
            }

            return placed;
        }

        private int PlaceHearthChimneyTop(
            List<HearthOpening> hearthOpenings,
            Vector3 origin,
            float rotationDeg,
            float chimneyBaseY,
            float chimneyTopY,
            GameObject wall2Prefab,
            GameObject? wall1Prefab,
            GameObject? roofPrefab,
            Player player)
        {
            int placed = 0;
            int wallLayers = Mathf.Max(1, Mathf.RoundToInt(chimneyTopY - chimneyBaseY));

            for (int i = 0; i < hearthOpenings.Count; i++)
            {
                var opening = hearthOpenings[i];
                bool closeWestEastSides = opening.Width >= opening.Height;

                for (int layer = 0; layer < wallLayers; layer++)
                {
                    float wallY = chimneyBaseY + 0.5f + layer;
                    if (closeWestEastSides)
                    {
                        placed += PlaceChimneyWallRun(opening.MinRow, opening.Height, opening.MinCol, false, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                        placed += PlaceChimneyWallRun(opening.MinRow, opening.Height, opening.MaxColExclusive, false, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                    }
                    else
                    {
                        placed += PlaceChimneyWallRun(opening.MinCol, opening.Width, opening.MinRow, true, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                        placed += PlaceChimneyWallRun(opening.MinCol, opening.Width, opening.MaxRowExclusive, true, wallY, origin, rotationDeg, wall2Prefab, wall1Prefab, player);
                    }
                }

                placed += PlaceHearthChimneyRoofCap(
                    opening,
                    closeWestEastSides,
                    origin,
                    rotationDeg,
                    chimneyTopY,
                    roofPrefab,
                    player);
            }

            return placed;
        }

        private int PlaceHearthChimneyRoofCap(
            HearthOpening opening,
            bool closeWestEastSides,
            Vector3 origin,
            float rotationDeg,
            float roofBaseY,
            GameObject? roofPrefab,
            Player player)
        {
            int placed = 0;

            if (roofPrefab != null)
            {
                if (closeWestEastSides)
                {
                    float westRoofX = opening.MinCol + 1f;
                    float eastRoofX = opening.MaxColExclusive - 1f;
                    for (int row = opening.MinRow; row < opening.MaxRowExclusive; row += 2)
                    {
                        int stripDepth = Mathf.Min(2, opening.MaxRowExclusive - row);
                        float localZ = row + stripDepth * 0.5f;

                        placed += PlaceChimneyRoofPiece(westRoofX, localZ, roofBaseY, 270f, origin, rotationDeg, roofPrefab, player);
                        placed += PlaceChimneyRoofPiece(eastRoofX, localZ, roofBaseY, 90f, origin, rotationDeg, roofPrefab, player);
                    }
                }
                else
                {
                    float southRoofZ = opening.MinRow + 1f;
                    float northRoofZ = opening.MaxRowExclusive - 1f;
                    for (int col = opening.MinCol; col < opening.MaxColExclusive; col += 2)
                    {
                        int stripWidth = Mathf.Min(2, opening.MaxColExclusive - col);
                        float localX = col + stripWidth * 0.5f;

                        placed += PlaceChimneyRoofPiece(localX, southRoofZ, roofBaseY, 180f, origin, rotationDeg, roofPrefab, player);
                        placed += PlaceChimneyRoofPiece(localX, northRoofZ, roofBaseY, 0f, origin, rotationDeg, roofPrefab, player);
                    }
                }
            }

            return placed;
        }

        private int PlaceChimneyRoofPiece(
            float localX,
            float localZ,
            float roofY,
            float localYaw,
            Vector3 origin,
            float rotationDeg,
            GameObject roofPrefab,
            Player player)
        {
            Vector3 roofPos = PieceMap.TransformPlanPoint(origin, localX, localZ, roofY, rotationDeg);
            Quaternion roofRot = Quaternion.Euler(0f, PieceMap.TransformLocalYaw(localYaw, rotationDeg), 0f);
            SpawnRegisteredPiece(roofPrefab, roofPos, roofRot, player);
            return 1;
        }

        private static List<HearthOpening> BuildHearthOpenings(FloorPlan plan)
        {
            var openings = new List<HearthOpening>();

            foreach (var piece in plan.Pieces)
            {
                if (piece.Type != "Hearth")
                    continue;

                var def = PieceMap.GetDef(piece.Type);
                if (def == null)
                    continue;

                int effW = def.EffW(piece.Rotation);
                int effH = def.EffH(piece.Rotation);
                openings.Add(new HearthOpening(
                    piece.Col,
                    piece.Row,
                    piece.Col + effW,
                    piece.Row + effH));
            }

            return openings;
        }

        private static bool IsBlockedByHearthOpening(int col, int row, List<HearthOpening> hearthOpenings)
        {
            for (int i = 0; i < hearthOpenings.Count; i++)
            {
                var opening = hearthOpenings[i];
                if (col >= opening.MinCol && col < opening.MaxColExclusive &&
                    row >= opening.MinRow && row < opening.MaxRowExclusive)
                    return true;
            }

            return false;
        }

        private static bool IsInsideAnyHearthOpening(float localX, float localZ, List<HearthOpening> hearthOpenings)
        {
            for (int i = 0; i < hearthOpenings.Count; i++)
            {
                var opening = hearthOpenings[i];
                if (localX >= opening.MinCol && localX <= opening.MaxColExclusive &&
                    localZ >= opening.MinRow && localZ <= opening.MaxRowExclusive)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Places horizontal beams connecting vertical poles from the West edge to the East edge.
        /// Each beam runs at the Z coordinate of a pole on the South or North edges.
        /// Returns the number of pieces placed.
        /// </summary>
        private int PlaceTransverseBeams(
            List<float> poleParams, float width, float depth, float lMinX, float lMaxX, float lMinZ, float lMaxZ, float perimeter,
            List<float> doorJambParams, List<ScaffoldDoorSpan> blockedTransverseLocalZs, List<Vector2> doorCenters,
            List<ScaffoldFurnitureExclusion> supportFurnitureExclusions, List<Vector2> occupiedPoleLocals, Vector3 origin, float rotationDeg,
            float levelTopY, GameObject horizPrefab, GameObject vertPrefab, Player player)
        {
            int placed = 0;

            // Transverse = West -> East at each interior edge pole row (local Z).
            var eastEdge = new List<ScaffoldPolePoint>();
            var westEdge = new List<ScaffoldPolePoint>();

            for (int i = 0; i < poleParams.Count; i++)
            {
                float t = poleParams[i];

                // Exclude corners so we only target intermediate edge poles.
                bool isDoorJamb = IsNearAnyParam(t, doorJambParams, 0.25f);
                if (isDoorJamb) continue;

                if (t > width && t < width + depth)
                {
                    var point = BuildScaffoldPolePoint(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter, origin, rotationDeg, levelTopY);
                    if (!IsWithinAnyDoorSpan(point.Local.y, blockedTransverseLocalZs, 0.25f))
                        eastEdge.Add(point);
                }
                else if (t > 2f * width + depth && t < perimeter)
                {
                    var point = BuildScaffoldPolePoint(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter, origin, rotationDeg, levelTopY);
                    if (!IsWithinAnyDoorSpan(point.Local.y, blockedTransverseLocalZs, 0.25f))
                        westEdge.Add(point);
                }
            }

            bool[] usedEast = new bool[eastEdge.Count];

            for (int wi = 0; wi < westEdge.Count; wi++)
            {
                float targetLocalZ = westEdge[wi].Local.y;
                int bestEi = -1;
                float bestDelta = float.MaxValue;

                for (int ei = 0; ei < eastEdge.Count; ei++)
                {
                    if (usedEast[ei]) continue;
                    float delta = Mathf.Abs(eastEdge[ei].Local.y - targetLocalZ);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestEi = ei;
                    }
                }

                if (bestEi < 0 || bestDelta > 0.25f) continue;

                usedEast[bestEi] = true;
                placed += PlaceScaffoldBeamSpan(
                    westEdge[wi].Local, eastEdge[bestEi].Local,
                    westEdge[wi].Pos, eastEdge[bestEi].Pos,
                    horizPrefab, vertPrefab, player, doorCenters, supportFurnitureExclusions, occupiedPoleLocals);
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[Scaffolding] Transverse beams: connected {placed} pieces across {westEdge.Count} west and {eastEdge.Count} east intermediate poles.");

            return placed;
        }

        /// <summary>
        /// Places horizontal beams connecting vertical poles from the South edge to the North edge.
        /// Each beam runs at the X coordinate of a pole on the West or East edges.
        /// Returns the number of pieces placed.
        /// </summary>
        private int PlaceLongitudinalBeams(
            List<float> poleParams, float width, float depth, float lMinX, float lMaxX, float lMinZ, float lMaxZ, float perimeter,
            List<float> doorJambParams, List<ScaffoldDoorSpan> blockedLongitudinalLocalXs, List<Vector2> doorCenters,
            List<ScaffoldFurnitureExclusion> supportFurnitureExclusions, List<Vector2> occupiedPoleLocals, Vector3 origin, float rotationDeg,
            float levelTopY, GameObject horizPrefab, GameObject vertPrefab, Player player)
        {
            int placed = 0;

            // Longitudinal = South -> North at each interior edge pole column (local X).
            var southEdge = new List<ScaffoldPolePoint>();
            var northEdge = new List<ScaffoldPolePoint>();

            for (int i = 0; i < poleParams.Count; i++)
            {
                float t = poleParams[i];

                // Exclude corners so we only target intermediate edge poles.
                bool isDoorJamb = IsNearAnyParam(t, doorJambParams, 0.25f);
                if (isDoorJamb) continue;

                if (t > 0f && t < width)
                {
                    var point = BuildScaffoldPolePoint(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter, origin, rotationDeg, levelTopY);
                    if (!IsWithinAnyDoorSpan(point.Local.x, blockedLongitudinalLocalXs, 0.25f))
                        southEdge.Add(point);
                }
                else if (t > width + depth && t < 2f * width + depth)
                {
                    var point = BuildScaffoldPolePoint(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter, origin, rotationDeg, levelTopY);
                    if (!IsWithinAnyDoorSpan(point.Local.x, blockedLongitudinalLocalXs, 0.25f))
                        northEdge.Add(point);
                }
            }

            bool[] usedNorth = new bool[northEdge.Count];

            for (int si = 0; si < southEdge.Count; si++)
            {
                float targetLocalX = southEdge[si].Local.x;
                int bestNi = -1;
                float bestDelta = float.MaxValue;

                for (int ni = 0; ni < northEdge.Count; ni++)
                {
                    if (usedNorth[ni]) continue;
                    float delta = Mathf.Abs(northEdge[ni].Local.x - targetLocalX);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestNi = ni;
                    }
                }

                if (bestNi < 0 || bestDelta > 0.25f) continue;

                usedNorth[bestNi] = true;
                placed += PlaceScaffoldBeamSpan(
                    southEdge[si].Local, northEdge[bestNi].Local,
                    southEdge[si].Pos, northEdge[bestNi].Pos,
                    horizPrefab, vertPrefab, player, doorCenters, supportFurnitureExclusions, occupiedPoleLocals);
            }

            ValheimFloorPlanPlugin.Log.LogInfo(
                $"[Scaffolding] Longitudinal beams: connected {placed} pieces across {southEdge.Count} south and {northEdge.Count} north intermediate poles.");

            return placed;
        }

        private ScaffoldPolePoint BuildScaffoldPolePoint(
            float t, float lMinX, float lMaxX, float lMinZ, float lMaxZ, float perimeter,
            Vector3 origin, float rotationDeg, float levelTopY)
        {
            Vector2 local = ScaffoldParamToLocal(t, lMinX, lMaxX, lMinZ, lMaxZ, perimeter);
            Vector3 worldPos = PieceMap.TransformPlanPoint(
                origin,
                local.x,
                local.y,
                levelTopY,
                rotationDeg);

            return new ScaffoldPolePoint(t, local, worldPos);
        }

        private int PlaceScaffoldBeamSpan(
            Vector2 localA, Vector2 localB, Vector3 pA, Vector3 pB,
            GameObject horizPrefab, GameObject vertPrefab, Player player, List<Vector2> doorCenters,
            List<ScaffoldFurnitureExclusion> supportFurnitureExclusions, List<Vector2> occupiedPoleLocals)
        {
            const float HORIZ_LEN  = 2f;
            const float HORIZ_HALF = HORIZ_LEN * 0.5f;

            float dx = pB.x - pA.x;
            float dz = pB.z - pA.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 0.1f) return 0;

            int placed = 0;
            Vector3 dir = new Vector3(dx / dist, 0f, dz / dist);
            float beamY = (pA.y + pB.y) * 0.5f;
            Quaternion beamRot = Quaternion.Euler(0f, Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg, 0f);

            int nFull = Mathf.FloorToInt(dist / HORIZ_LEN);
            float remainder = dist - nFull * HORIZ_LEN;

            for (int b = 0; b < nFull; b++)
            {
                Vector3 center = pA + dir * (b * HORIZ_LEN + HORIZ_HALF);
                center.y = beamY;
                SpawnScaffoldPole(horizPrefab, center, beamRot, player);
                placed++;
            }

            if (remainder > 0.05f)
            {
                Vector3 center = pB - dir * HORIZ_HALF;
                center.y = beamY;
                SpawnScaffoldPole(horizPrefab, center, beamRot, player);
                placed++;
            }

            return placed;
        }

        private sealed class ScaffoldPolePoint
        {
            public readonly float T;
            public readonly Vector2 Local;
            public readonly Vector3 Pos;

            public ScaffoldPolePoint(float t, Vector2 local, Vector3 pos)
            {
                T = t;
                Local = local;
                Pos = pos;
            }
        }

        private sealed class ScaffoldFurnitureExclusion
        {
            public readonly Vector2 Center;
            public readonly Vector2 Forward;
            public readonly Vector2 Side;
            public readonly float ForwardHalfExtent;
            public readonly float SideHalfExtent;
            public readonly float FrontClearance;

            public ScaffoldFurnitureExclusion(
                Vector2 center,
                Vector2 forward,
                Vector2 side,
                float forwardHalfExtent,
                float sideHalfExtent,
                float frontClearance)
            {
                Center = center;
                Forward = forward;
                Side = side;
                ForwardHalfExtent = forwardHalfExtent;
                SideHalfExtent = sideHalfExtent;
                FrontClearance = frontClearance;
            }
        }

        private sealed class ScaffoldDoorSpan
        {
            public readonly float Min;
            public readonly float Max;

            public ScaffoldDoorSpan(float min, float max)
            {
                Min = min;
                Max = max;
            }
        }

        private sealed class HearthOpening
        {
            public readonly int MinCol;
            public readonly int MinRow;
            public readonly int MaxColExclusive;
            public readonly int MaxRowExclusive;

            public int Width => MaxColExclusive - MinCol;
            public int Height => MaxRowExclusive - MinRow;

            public HearthOpening(int minCol, int minRow, int maxColExclusive, int maxRowExclusive)
            {
                MinCol = minCol;
                MinRow = minRow;
                MaxColExclusive = maxColExclusive;
                MaxRowExclusive = maxRowExclusive;
            }
        }

        /// <summary>
        /// Converts a clockwise perimeter distance parameter to a local (unrotated) XZ position.
        /// Edges in order: south (SW→SE), east (SE→NE), north (NE→NW), west (NW→SW).
        /// </summary>
        private static Vector2 ScaffoldParamToLocal(
            float t, float minX, float maxX, float minZ, float maxZ, float perimeter)
        {
            float width = maxX - minX;
            float depth = maxZ - minZ;
            t = ((t % perimeter) + perimeter) % perimeter;

            if (t <= width)              return new Vector2(minX + t,       minZ);  // south
            t -= width;
            if (t <= depth)              return new Vector2(maxX,            minZ + t); // east
            t -= depth;
            if (t <= width)              return new Vector2(maxX - t,        maxZ);  // north
            t -= width;
            return                              new Vector2(minX,            maxZ - t); // west
        }

        /// <summary>Removes duplicate-or-near-duplicate pole parameters (within <paramref name="minDist"/> metres).</summary>
        private static void ScaffoldDedup(List<float> poles, float minDist)
        {
            for (int i = poles.Count - 1; i > 0; i--)
                if (poles[i] - poles[i - 1] < minDist)
                    poles.RemoveAt(i);
        }

        private static bool IsNearAnyParam(float t, List<float> values, float tolerance)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Mathf.Abs(values[i] - t) <= tolerance)
                    return true;
            }
            return false;
        }

        private static bool IsNearAnyDoor(Vector2 local, List<Vector2> doorCenters, float radius)
        {
            float radiusSqr = radius * radius;
            for (int i = 0; i < doorCenters.Count; i++)
            {
                Vector2 delta = local - doorCenters[i];
                if (delta.sqrMagnitude <= radiusSqr)
                    return true;
            }
            return false;
        }

        private static bool IsNearAnyPole(Vector2 local, List<Vector2> poleLocals, float minDistance)
        {
            float minDistanceSqr = minDistance * minDistance;
            for (int i = 0; i < poleLocals.Count; i++)
            {
                Vector2 delta = local - poleLocals[i];
                if (delta.sqrMagnitude < minDistanceSqr)
                    return true;
            }
            return false;
        }

        private static List<ScaffoldFurnitureExclusion> BuildScaffoldFurnitureExclusions(FloorPlan plan)
        {
            const float FRONT_CLEARANCE = 4f;
            const float SIDE_MARGIN = 0.35f;
            var exclusions = new List<ScaffoldFurnitureExclusion>();

            foreach (var piece in plan.Pieces)
            {
                if (piece.Type != "Workbench" && piece.Type != "Bed" && piece.Type != "Hearth")
                    continue;

                var def = PieceMap.GetDef(piece.Type);
                if (def == null)
                    continue;

                int effW = def.EffW(piece.Rotation);
                int effH = def.EffH(piece.Rotation);
                Vector2 center = new Vector2(
                    (piece.Col + effW * 0.5f) * PieceMap.CELL_SIZE,
                    (piece.Row + effH * 0.5f) * PieceMap.CELL_SIZE);
                Vector2 forward = GetScaffoldFrontDirection(piece.Rotation);
                Vector2 side = new Vector2(-forward.y, forward.x);
                bool forwardAlongX = Mathf.Abs(forward.x) > 0.5f;
                float forwardHalfExtent = (forwardAlongX ? effW : effH) * 0.5f * PieceMap.CELL_SIZE;
                float sideHalfExtent = (forwardAlongX ? effH : effW) * 0.5f * PieceMap.CELL_SIZE + SIDE_MARGIN;

                exclusions.Add(new ScaffoldFurnitureExclusion(
                    center,
                    forward,
                    side,
                    forwardHalfExtent,
                    sideHalfExtent,
                    FRONT_CLEARANCE));
            }

            return exclusions;
        }

        private static Vector2 GetScaffoldFrontDirection(int rotation)
        {
            int normalized = ((rotation % 360) + 360) % 360;
            switch (normalized)
            {
                case 90: return new Vector2(-1f, 0f);
                case 180: return new Vector2(0f, -1f);
                case 270: return new Vector2(1f, 0f);
                default: return new Vector2(0f, 1f);
            }
        }

        private static bool IsInsideAnyScaffoldFurnitureExclusion(Vector2 local, List<ScaffoldFurnitureExclusion> exclusions)
        {
            for (int i = 0; i < exclusions.Count; i++)
            {
                var exclusion = exclusions[i];
                Vector2 rel = local - exclusion.Center;
                float forwardDist = Vector2.Dot(rel, exclusion.Forward);
                float sideDist = Mathf.Abs(Vector2.Dot(rel, exclusion.Side));

                bool insideFootprint = Mathf.Abs(forwardDist) <= exclusion.ForwardHalfExtent &&
                    sideDist <= exclusion.SideHalfExtent;
                bool insideFrontBand = forwardDist > exclusion.ForwardHalfExtent &&
                    forwardDist <= exclusion.ForwardHalfExtent + exclusion.FrontClearance &&
                    sideDist <= exclusion.SideHalfExtent;

                if (insideFootprint || insideFrontBand)
                    return true;
            }

            return false;
        }

        private static bool IsWithinHorizontalRadius(Vector3 position, Vector3 center, float radius)
        {
            float dx = position.x - center.x;
            float dz = position.z - center.z;
            float r = Mathf.Max(0f, radius);
            return (dx * dx + dz * dz) <= (r * r);
        }

        private static bool IsWithinAnyDoorSpan(float t, List<ScaffoldDoorSpan> spans, float tolerance)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                if (t >= spans[i].Min - tolerance && t <= spans[i].Max + tolerance)
                    return true;
            }
            return false;
        }

        private void PruneGroundFloorScaffoldVerticals(List<Vector2> doorCenters, Vector3 centerWorld, Vector3 origin, float rotationDeg)
        {
            const float DOOR_RADIUS = 4.25f;

            if (_groundFloorScaffoldVerticals.Count == 0)
                return;

            float cosR = Mathf.Cos(rotationDeg * Mathf.Deg2Rad);
            float sinR = Mathf.Sin(rotationDeg * Mathf.Deg2Rad);
            var worldDoorCenters = new List<Vector3>(doorCenters.Count);
            for (int i = 0; i < doorCenters.Count; i++)
            {
                Vector2 local = doorCenters[i];
                float wx = origin.x + local.x * cosR + local.y * sinR;
                float wz = origin.z - local.x * sinR + local.y * cosR;
                worldDoorCenters.Add(new Vector3(wx, centerWorld.y, wz));
            }

            int removed = 0;
            for (int i = _groundFloorScaffoldVerticals.Count - 1; i >= 0; i--)
            {
                var poleGo = _groundFloorScaffoldVerticals[i];
                if (poleGo == null)
                {
                    _groundFloorScaffoldVerticals.RemoveAt(i);
                    continue;
                }

                bool remove = false;
                if (!remove)
                {
                    for (int d = 0; d < worldDoorCenters.Count; d++)
                    {
                        if (IsWithinHorizontalRadius(poleGo.transform.position, worldDoorCenters[d], DOOR_RADIUS))
                        {
                            remove = true;
                            break;
                        }
                    }
                }

                if (!remove)
                    continue;

                _lastPlaced.Remove(poleGo);
                _groundFloorScaffoldVerticals.RemoveAt(i);
                if (ZNetScene.instance != null)
                    ZNetScene.instance.Destroy(poleGo);
                else
                    Destroy(poleGo);
                removed++;
            }

            if (removed > 0)
            {
                ValheimFloorPlanPlugin.Log.LogInfo(
                    $"[Scaffolding] Pruned {removed} ground-floor vertical poles near doors after scaffolding completed.");
            }
        }

        private int SpawnScaffoldColumn(GameObject prefab, Vector3 center, Quaternion rot, Player player, float columnHeight, float segmentHeight)
        {
            int segmentCount = Mathf.Max(1, Mathf.RoundToInt(columnHeight / segmentHeight));
            float startCenterY = center.y - columnHeight * 0.5f + segmentHeight * 0.5f;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 segmentPos = new Vector3(center.x, startCenterY + i * segmentHeight, center.z);
                SpawnScaffoldPole(prefab, segmentPos, rot, player);
            }

            return segmentCount;
        }

        private GameObject SpawnRegisteredPiece(GameObject prefab, Vector3 pos, Quaternion rot, Player player, bool centerOnRenderedBoundsXZ = false)
        {
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);

            var znv = go.GetComponent<ZNetView>();
            ZDO? zdo = null;
            if (znv != null)
            {
                zdo = znv.GetZDO();
                if (centerOnRenderedBoundsXZ)
                {
                    CenterPieceOnRenderedBoundsXZ(go, pos);
                    zdo?.SetPosition(go.transform.position);
                }

                if (zdo != null)
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    zdo.Set(VFP_TAG, "1");
                }
            }

            _lastPlaced.Add(go);
            go.GetComponent<Piece>()?.SetCreator(player.GetPlayerID());
            return go;
        }

        /// <summary>Spawns a scaffold pole, registers it with ZDOMan and tags it for Undo.</summary>
        private GameObject SpawnScaffoldPole(GameObject prefab, Vector3 pos, Quaternion rot, Player player)
        {
            return SpawnRegisteredPiece(prefab, pos, rot, player);
        }
    }
}

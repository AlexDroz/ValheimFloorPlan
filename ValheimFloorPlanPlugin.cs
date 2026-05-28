using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ValheimFloorPlan
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ValheimFloorPlanPlugin : BaseUnityPlugin
    {
        internal enum StructuralMaterial
        {
            Stone,
            Wood
        }

        internal enum RoofScaffoldingTypeOption
        {
            Gable,
            Flat
        }

        internal enum RoofScaffoldingGableFlooringOption
        {
            RoofWithFloorUnderlay,
            RoofOnly
        }

        internal enum StaircaseReachModeOption
        {
            ToTheNextLevelOnly,
            AllTheWay
        }

        public const string PluginGUID = "com.alexdroz.valheimfloorplan";
        public const string PluginName = "ValheimFloorPlan";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Log = null!;
        internal static ValheimFloorPlanPlugin Instance { get; private set; } = null!;
        internal static MessageHud.MessageType ProgressMessageType { get; private set; } = MessageHud.MessageType.Center;
        internal static MessageHud.MessageType WarningMessageType { get; private set; } = MessageHud.MessageType.TopLeft;
        internal static int TerrainLevelPasses { get; private set; } = 2;
        internal static int TerrainSpikeCleanupPasses { get; private set; } = 2;
        internal static float TerrainStampRadius { get; private set; } = 3.0f;
        internal static float TerrainHighPointDelta { get; private set; } = 0.0f;
        internal static bool TerrainUseStagedRaise { get; private set; } = false;
        internal static float TerrainRaiseStepHeight { get; private set; } = 0.5f;
        internal static int TerrainMaxRaiseStages { get; private set; } = 1;
        internal static bool TerrainSkipSatisfiedCenterStamps { get; private set; } = true;
        internal static int ExternalWallHeightLevel1 { get; private set; } = 1;
        internal static int ExternalWallHeightLevel2 { get; private set; } = 1;
        internal static int ExternalWallHeightLevel3 { get; private set; } = 1;
        internal static StructuralMaterial WallPillarMaterial { get; private set; } = StructuralMaterial.Stone;
        internal static StaircaseReachModeOption StaircaseReachMode { get; private set; } = StaircaseReachModeOption.ToTheNextLevelOnly;
        internal static bool RoofScaffolding { get; private set; } = false;
        internal static RoofScaffoldingTypeOption RoofScaffoldingType { get; private set; } = RoofScaffoldingTypeOption.Gable;
        internal static RoofScaffoldingGableFlooringOption RoofScaffoldingGableFlooring { get; private set; } = RoofScaffoldingGableFlooringOption.RoofWithFloorUnderlay;
        internal static int ScaffoldingLevels { get; private set; } = 1;
        internal static int ScaffoldingFloorHeight { get; private set; } = 4;
        internal static int ScaffoldingFloorHeight2 { get; private set; } = 4;
        internal static int ScaffoldingFloorHeight3 { get; private set; } = 4;
        internal static bool ScaffoldingFloors { get; private set; } = false;
        internal static bool TransverseScaffoldingBeams { get; private set; } = false;
        internal static bool LongitudinalScaffoldingBeams { get; private set; } = false;
        internal static bool DisableWelcomePost { get; private set; } = false;
        internal static float BuildOriginForwardOffset { get; private set; } = 0f;
        internal static float PreviewMoveStep { get; private set; } = 2f;
        internal static float PreviewFineMoveStep { get; private set; } = 0.5f;
        internal static float BuildRotationSnapDegrees { get; private set; } = 90f;
        internal static float PreviewRotateStepDeg { get; private set; } = 15f;
        internal static float PreviewFineRotateStepDeg { get; private set; } = 5f;
        internal static KeyCode PreviewMoveForwardKey { get; private set; } = KeyCode.UpArrow;
        internal static KeyCode PreviewMoveBackwardKey { get; private set; } = KeyCode.DownArrow;
        internal static KeyCode PreviewMoveLeftKey { get; private set; } = KeyCode.LeftArrow;
        internal static KeyCode PreviewMoveRightKey { get; private set; } = KeyCode.RightArrow;
        internal static KeyCode PreviewConfirmKey { get; private set; } = KeyCode.E;
        internal static KeyCode PreviewRotateLeftKey { get; private set; } = KeyCode.Q;
        internal static KeyCode PreviewRotateRightKey { get; private set; } = KeyCode.G;
        internal static KeyCode PreviewCancelKey { get; private set; } = KeyCode.Escape;
        internal static KeyCode PreviewFineAdjustKey { get; private set; } = KeyCode.LeftShift;
        internal static float UndoRadius { get; private set; } = 15f;
        internal static int FloorPlanLevels { get; private set; } = 1;
        internal static string FloorPlanFileLevel2 { get; private set; } = string.Empty;
        internal static string FloorPlanFileLevel3 { get; private set; } = string.Empty;

        private ConfigEntry<string> _vfpFilePath = null!;
        private ConfigEntry<string> _vfpFilePathLevel2 = null!;
        private ConfigEntry<string> _vfpFilePathLevel3 = null!;
        private ConfigEntry<int> _floorPlanLevels = null!;
        private ConfigEntry<KeyboardShortcut> _buildHotkey = null!;
        private ConfigEntry<KeyboardShortcut> _undoHotkey = null!;
        private ConfigEntry<float> _undoRadius = null!;
        private ConfigEntry<string> _progressMessagePosition = null!;
        private ConfigEntry<string> _warningMessagePosition = null!;
        private ConfigEntry<int> _terrainLevelPasses = null!;
        private ConfigEntry<int> _terrainSpikeCleanupPasses = null!;
        private ConfigEntry<float> _terrainStampRadius = null!;
        private ConfigEntry<float> _terrainHighPointDelta = null!;
        private ConfigEntry<bool> _terrainUseStagedRaise = null!;
        private ConfigEntry<float> _terrainRaiseStepHeight = null!;
        private ConfigEntry<int> _terrainMaxRaiseStages = null!;
        private ConfigEntry<bool> _terrainSkipSatisfiedCenterStamps = null!;
        private ConfigEntry<int> _externalWallHeightLevel1 = null!;
        private ConfigEntry<int> _externalWallHeightLevel2 = null!;
        private ConfigEntry<int> _externalWallHeightLevel3 = null!;
        private ConfigEntry<string> _wallPillarMaterial = null!;
        private ConfigEntry<string> _staircaseReachMode = null!;
        private ConfigEntry<bool> _roofScaffolding = null!;
        private ConfigEntry<string> _roofScaffoldingType = null!;
        private ConfigEntry<string> _roofScaffoldingGableFlooring = null!;
        private ConfigEntry<int> _scaffoldingLevels = null!;
        private ConfigEntry<int> _scaffoldingFloorHeight = null!;
        private ConfigEntry<int> _scaffoldingFloorHeight2 = null!;
        private ConfigEntry<int> _scaffoldingFloorHeight3 = null!;
        private ConfigEntry<bool> _scaffoldingFloors = null!;
        private ConfigEntry<bool> _transverseScaffoldingBeams = null!;
        private ConfigEntry<bool> _longitudinalScaffoldingBeams = null!;
        private ConfigEntry<bool> _disableWelcomePost = null!;
        private ConfigEntry<float> _buildOriginForwardOffset = null!;
        private ConfigEntry<float> _previewMoveStep = null!;
        private ConfigEntry<float> _previewFineMoveStep = null!;
        private ConfigEntry<float> _buildRotationSnapDeg = null!;
        private ConfigEntry<float> _previewRotateStepDeg = null!;
        private ConfigEntry<float> _previewFineRotateStepDeg = null!;
        private ConfigEntry<KeyCode> _previewMoveForwardKey = null!;
        private ConfigEntry<KeyCode> _previewMoveBackwardKey = null!;
        private ConfigEntry<KeyCode> _previewMoveLeftKey = null!;
        private ConfigEntry<KeyCode> _previewMoveRightKey = null!;
        private ConfigEntry<KeyCode> _previewConfirmKey = null!;
        private ConfigEntry<KeyCode> _previewRotateLeftKey = null!;
        private ConfigEntry<KeyCode> _previewRotateRightKey = null!;
        private ConfigEntry<KeyCode> _previewCancelKey = null!;
        private ConfigEntry<KeyCode> _previewFineAdjustKey = null!;
        private bool _applyingScaffoldingRules;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _vfpFilePath = Config.Bind(
                "General", "FloorPlanFile", "",
                "Full path to the .vfp floor plan file exported from Valheim Floor Plan Designer.");

            _vfpFilePathLevel2 = Config.Bind(
                "General", "FloorPlanFileLevel2", "",
                "Optional full path to the Level 2 .vfp floor plan file. When set, its footprint must fit within the Level 1 plan footprint.");
            _vfpFilePathLevel2.SettingChanged += (_, _) =>
                FloorPlanFileLevel2 = (_vfpFilePathLevel2.Value ?? string.Empty).Trim();
            FloorPlanFileLevel2 = (_vfpFilePathLevel2.Value ?? string.Empty).Trim();

            _vfpFilePathLevel3 = Config.Bind(
                "General", "FloorPlanFileLevel3", "",
                "Optional full path to the Level 3 .vfp floor plan file. When set, its footprint must fit within the Level 1 plan footprint.");
            _vfpFilePathLevel3.SettingChanged += (_, _) =>
                FloorPlanFileLevel3 = (_vfpFilePathLevel3.Value ?? string.Empty).Trim();
            FloorPlanFileLevel3 = (_vfpFilePathLevel3.Value ?? string.Empty).Trim();

            _floorPlanLevels = Config.Bind(
                "General", "FloorPlanLevels", 1,
                new ConfigDescription(
                    "How many layout levels to build (1-3). Level 1 is FloorPlanFile, Level 2 adds FloorPlanFileLevel2, Level 3 adds FloorPlanFileLevel3. Values above 1 enforce multi-level scaffolding requirements.",
                    new AcceptableValueRange<int>(1, 3)));
            _floorPlanLevels.SettingChanged += (_, _) => ApplyScaffoldingRules();
            FloorPlanLevels = Mathf.Clamp(_floorPlanLevels.Value, 1, 3);

            _buildHotkey = Config.Bind(
                "General", "BuildHotkey", new KeyboardShortcut(KeyCode.F8),
                "Hotkey to build the floor plan at your current position.");

            _undoHotkey = Config.Bind(
                "General", "UndoHotkey", new KeyboardShortcut(KeyCode.F9),
                "Hotkey to undo the last floor plan build (removes pieces and restores terrain).");

            _undoRadius = Config.Bind(
                "General", "UndoRadius", 15f,
                new ConfigDescription(
                    "Search radius in metres around the player when scanning for VFP pieces to remove on Undo.",
                    new AcceptableValueRange<float>(5f, 150f)));
            _undoRadius.SettingChanged += (_, _) =>
                UndoRadius = Mathf.Clamp(_undoRadius.Value, 5f, 150f);
            UndoRadius = Mathf.Clamp(_undoRadius.Value, 5f, 150f);

            _progressMessagePosition = Config.Bind(
                "General", "ProgressMessagePosition", "CenterLeft",
                "HUD slot for build-progress messages. Uses Valheim MessageHud positions. Examples: Center, TopLeft, TopRight. 'CenterLeft' is accepted as an alias and maps to Center.");
            _progressMessagePosition.SettingChanged += (_, _) =>
                ProgressMessageType = ParseProgressMessageType(_progressMessagePosition.Value);
            ProgressMessageType = ParseProgressMessageType(_progressMessagePosition.Value);

            _warningMessagePosition = Config.Bind(
                "General", "WarningMessagePosition", "TopLeft",
                "HUD slot for slope/build warning messages. Uses Valheim MessageHud positions. Examples: Center, TopLeft, TopRight. 'CenterLeft' is accepted as an alias and maps to Center.");
            _warningMessagePosition.SettingChanged += (_, _) =>
                WarningMessageType = ParseProgressMessageType(_warningMessagePosition.Value);
            WarningMessageType = ParseProgressMessageType(_warningMessagePosition.Value);

            _scaffoldingLevels = Config.Bind(
                "Scaffolding", "ScaffoldingLevels", 1,
                new ConfigDescription(
                    "How many stacked 4m scaffolding levels to build when RoofScaffolding is enabled. 1 builds only the ground level. Higher values repeat the same scaffold pattern every +4m, up to 3 levels to stay within core-wood height limits.",
                    new AcceptableValueRange<int>(1, 3)));
            _scaffoldingLevels.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ScaffoldingLevels = Mathf.Clamp(_scaffoldingLevels.Value, 1, 3);

            var validScaffoldHeights = new AcceptableValueList<int>(2, 4, 6);
            _scaffoldingFloorHeight = Config.Bind(
                "Scaffolding", "ScaffoldingFloorHeight", 4,
                new ConfigDescription(
                    "Vertical height in metres for the first scaffold level. Must be a multiple of 2m so stacked wood-iron support segments line up correctly.",
                    validScaffoldHeights));
            _scaffoldingFloorHeight.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ScaffoldingFloorHeight = _scaffoldingFloorHeight.Value;

            _scaffoldingFloorHeight2 = Config.Bind(
                "Scaffolding", "ScaffoldingFloorHeight#2", 4,
                new ConfigDescription(
                    "Vertical height in metres for the second scaffold level. Used when ScaffoldingLevels is 2 or 3.",
                    validScaffoldHeights));
            _scaffoldingFloorHeight2.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ScaffoldingFloorHeight2 = _scaffoldingFloorHeight2.Value;

            _scaffoldingFloorHeight3 = Config.Bind(
                "Scaffolding", "ScaffoldingFloorHeight#3", 4,
                new ConfigDescription(
                    "Vertical height in metres for the third scaffold level. Used when ScaffoldingLevels is 3.",
                    validScaffoldHeights));
            _scaffoldingFloorHeight3.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ScaffoldingFloorHeight3 = _scaffoldingFloorHeight3.Value;

            _roofScaffolding = Config.Bind(
                "Scaffolding", "RoofScaffolding", false,
                "When enabled, places a ring of vertical 4m log poles at each corner, adjacent to each door, and at midpoints where spacing exceeds 8m, then connects their tops with horizontal 4m log poles forming a rectangular scaffold frame.");
            _roofScaffolding.SettingChanged += (_, _) => ApplyScaffoldingRules();
            RoofScaffolding = _roofScaffolding.Value;

            _roofScaffoldingType = Config.Bind(
                "Scaffolding", "RoofScaffoldingType", "Gable",
                new ConfigDescription(
                    "Topmost scaffold deck shape when ScaffoldingFloors is enabled. Gable builds the current pitched roof. Flat tiles the topmost level with ridge roof pieces aligned edge-to-edge along their long edges.",
                    new AcceptableValueList<string>("Gable", "Flat")));
            _roofScaffoldingType.SettingChanged += (_, _) =>
                RoofScaffoldingType = ParseRoofScaffoldingType(_roofScaffoldingType.Value);
            RoofScaffoldingType = ParseRoofScaffoldingType(_roofScaffoldingType.Value);

            _roofScaffoldingGableFlooring = Config.Bind(
                "Scaffolding", "RoofScaffoldingGableFlooring", "RoofWithFloorUnderlay",
                new ConfigDescription(
                    "Top-surface behavior for Gable mode only. RoofWithFloorUnderlay places floor tiles under the top gable roof. RoofOnly places only gable roof pieces.",
                    new AcceptableValueList<string>("RoofWithFloorUnderlay", "RoofOnly")));
            _roofScaffoldingGableFlooring.SettingChanged += (_, _) =>
                RoofScaffoldingGableFlooring = ParseRoofScaffoldingGableFlooring(_roofScaffoldingGableFlooring.Value);
            RoofScaffoldingGableFlooring = ParseRoofScaffoldingGableFlooring(_roofScaffoldingGableFlooring.Value);

            _scaffoldingFloors = Config.Bind(
                "Scaffolding", "ScaffoldingFloors", false,
                "When enabled (requires RoofScaffolding), builds wood floor decks across each scaffolding level.");
            _scaffoldingFloors.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ScaffoldingFloors = _scaffoldingFloors.Value;

            _transverseScaffoldingBeams = Config.Bind(
                "Scaffolding", "TransverseScaffoldingBeams", false,
                "When enabled (requires RoofScaffolding), places horizontal 4m log beams connecting vertical poles from the West edge to the East edge. Beams are placed at each vertical pole's Z position.");
            _transverseScaffoldingBeams.SettingChanged += (_, _) => ApplyScaffoldingRules();
            TransverseScaffoldingBeams = _transverseScaffoldingBeams.Value;

            _longitudinalScaffoldingBeams = Config.Bind(
                "Scaffolding", "LongitudinalScaffoldingBeams", false,
                "When enabled (requires RoofScaffolding), places horizontal 4m log beams connecting vertical poles from the North edge to the South edge. Beams are placed at each vertical pole's X position.");
            _longitudinalScaffoldingBeams.SettingChanged += (_, _) => ApplyScaffoldingRules();
            LongitudinalScaffoldingBeams = _longitudinalScaffoldingBeams.Value;

            ApplyScaffoldingRules();

            _externalWallHeightLevel1 = Config.Bind(
                "Building", "ExternalWallHeightLevel1", 1,
                new ConfigDescription(
                    "How many levels high external Wall/Pillar pieces should be stacked for Level 1. Dynamic cap: ScaffoldingFloorHeight x 2.",
                    new AcceptableValueRange<int>(1, 6)));
            _externalWallHeightLevel1.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ExternalWallHeightLevel1 = Mathf.Clamp(_externalWallHeightLevel1.Value, 1, 6);

            _externalWallHeightLevel2 = Config.Bind(
                "Building", "ExternalWallHeightLevel2", 1,
                new ConfigDescription(
                    "How many levels high external Wall/Pillar pieces should be stacked for Level 2. Dynamic cap: ScaffoldingFloorHeight#2 x 2.",
                    new AcceptableValueRange<int>(1, 6)));
            _externalWallHeightLevel2.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ExternalWallHeightLevel2 = Mathf.Clamp(_externalWallHeightLevel2.Value, 1, 6);

            _externalWallHeightLevel3 = Config.Bind(
                "Building", "ExternalWallHeightLevel3", 1,
                new ConfigDescription(
                    "How many levels high external Wall/Pillar pieces should be stacked for Level 3. Dynamic cap: ScaffoldingFloorHeight#3 x 2.",
                    new AcceptableValueRange<int>(1, 6)));
            _externalWallHeightLevel3.SettingChanged += (_, _) => ApplyScaffoldingRules();
            ExternalWallHeightLevel3 = Mathf.Clamp(_externalWallHeightLevel3.Value, 1, 6);

            ApplyScaffoldingRules();

            _wallPillarMaterial = Config.Bind(
                "Building", "WallPillarMaterial", "Stone",
                new ConfigDescription(
                    "Material used for Wall and Pillar types. Allowed: Stone, Wood.",
                    new AcceptableValueList<string>("Stone", "Wood")));
            _wallPillarMaterial.SettingChanged += (_, _) =>
                WallPillarMaterial = ParseStructuralMaterial(_wallPillarMaterial.Value);
            WallPillarMaterial = ParseStructuralMaterial(_wallPillarMaterial.Value);

            _disableWelcomePost = Config.Bind(
                "Building", "DisableWelcomePost", false,
                "When enabled, skips creation of the center Welcome post and signs after a build completes.");
            _disableWelcomePost.SettingChanged += (_, _) => DisableWelcomePost = _disableWelcomePost.Value;
            DisableWelcomePost = _disableWelcomePost.Value;

            _staircaseReachMode = Config.Bind(
                "Building", "StaircaseReachMode", "ToTheNextLevelOnly",
                new ConfigDescription(
                    "Controls staircase vertical reach. ToTheNextLevelOnly climbs one level at a time. AllTheWay climbs toward the top available scaffold level.",
                    new AcceptableValueList<string>("ToTheNextLevelOnly", "AllTheWay")));
            _staircaseReachMode.SettingChanged += (_, _) =>
                StaircaseReachMode = ParseStaircaseReachMode(_staircaseReachMode.Value);
            StaircaseReachMode = ParseStaircaseReachMode(_staircaseReachMode.Value);

            _buildOriginForwardOffset = Config.Bind(
                "Preview", "BuildOriginForwardOffset", 0f,
                new ConfigDescription(
                    "Extra distance in metres added beyond the auto-computed player-to-build-center placement when preview starts. Usually 0 is sufficient.",
                    new AcceptableValueRange<float>(0f, 20f)));
            _buildOriginForwardOffset.SettingChanged += (_, _) =>
                BuildOriginForwardOffset = Mathf.Clamp(_buildOriginForwardOffset.Value, 0f, 20f);
            BuildOriginForwardOffset = Mathf.Clamp(_buildOriginForwardOffset.Value, 0f, 20f);

            _previewMoveStep = Config.Bind(
                "Preview", "MoveStep", 2f,
                new ConfigDescription(
                    "How far the preview origin moves per nudge key press, in meters.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            _previewMoveStep.SettingChanged += (_, _) =>
                PreviewMoveStep = Mathf.Clamp(_previewMoveStep.Value, 0.25f, 10f);
            PreviewMoveStep = Mathf.Clamp(_previewMoveStep.Value, 0.25f, 10f);

            _previewFineMoveStep = Config.Bind(
                "Preview", "FineMoveStep", 0.5f,
                new ConfigDescription(
                    "How far the preview origin moves per nudge key press while the fine-adjust key is held, in meters.",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            _previewFineMoveStep.SettingChanged += (_, _) =>
                PreviewFineMoveStep = Mathf.Clamp(_previewFineMoveStep.Value, 0.05f, 5f);
            PreviewFineMoveStep = Mathf.Clamp(_previewFineMoveStep.Value, 0.05f, 5f);

            // Rotation settings are placed in their own section so they appear as a
            // distinct group in the in-game config manager. All values are restricted
            // to multiples of 22.5° so that any combination of step + snap stays on
            // the same grid and builds remain compatible with Valheim's default
            // piece-rotation increments.
            var validRotationSteps = new AcceptableValueList<float>(22.5f, 45f, 90f);

            _buildRotationSnapDeg = Config.Bind(
                "Preview - Rotation", "BuildRotationSnapDegrees", 90f,
                new ConfigDescription(
                    "Snaps the final build rotation to the nearest multiple of this value at preview start and build confirm. Allowed: 22.5, 45, 90.",
                    validRotationSteps));
            _buildRotationSnapDeg.SettingChanged += (_, _) =>
                BuildRotationSnapDegrees = _buildRotationSnapDeg.Value;
            BuildRotationSnapDegrees = _buildRotationSnapDeg.Value;

            _previewRotateStepDeg = Config.Bind(
                "Preview - Rotation", "RotateStepDegrees", 90f,
                new ConfigDescription(
                    "How far the preview rotates per coarse rotate key press (Q / G). Allowed: 22.5, 45, 90.",
                    validRotationSteps));
            _previewRotateStepDeg.SettingChanged += (_, _) =>
                PreviewRotateStepDeg = _previewRotateStepDeg.Value;
            PreviewRotateStepDeg = _previewRotateStepDeg.Value;

            _previewFineRotateStepDeg = Config.Bind(
                "Preview - Rotation", "FineRotateStepDegrees", 22.5f,
                new ConfigDescription(
                    "How far the preview rotates per key press while the fine-adjust key (Left Shift) is held. Allowed: 22.5, 45, 90.",
                    validRotationSteps));
            _previewFineRotateStepDeg.SettingChanged += (_, _) =>
                PreviewFineRotateStepDeg = _previewFineRotateStepDeg.Value;
            PreviewFineRotateStepDeg = _previewFineRotateStepDeg.Value;

            _previewMoveForwardKey = Config.Bind(
                "Preview", "MoveForwardKey", KeyCode.UpArrow,
                "Preview nudge key for moving the origin forward relative to the camera.");
            _previewMoveBackwardKey = Config.Bind(
                "Preview", "MoveBackwardKey", KeyCode.DownArrow,
                "Preview nudge key for moving the origin backward relative to the camera.");
            _previewMoveLeftKey = Config.Bind(
                "Preview", "MoveLeftKey", KeyCode.LeftArrow,
                "Preview nudge key for moving the origin left relative to the camera.");
            _previewMoveRightKey = Config.Bind(
                "Preview", "MoveRightKey", KeyCode.RightArrow,
                "Preview nudge key for moving the origin right relative to the camera.");
            _previewConfirmKey = Config.Bind(
                "Preview", "ConfirmKey", KeyCode.E,
                "Preview key that confirms build placement at the current preview position.");
            _previewRotateLeftKey = Config.Bind(
                "Preview", "RotateLeftKey", KeyCode.Q,
                "Preview rotation key for rotating counter-clockwise.");
            _previewRotateRightKey = Config.Bind(
                "Preview", "RotateRightKey", KeyCode.G,
                "Preview rotation key for rotating clockwise.");
            if (_previewRotateRightKey.Value == KeyCode.T)
                _previewRotateRightKey.Value = KeyCode.G;
            _previewCancelKey = Config.Bind(
                "Preview", "CancelKey", KeyCode.Escape,
                "Preview keyboard cancel key. Right-click always cancels too.");
            _previewFineAdjustKey = Config.Bind(
                "Preview", "FineAdjustKey", KeyCode.LeftShift,
                "Hold this key for fine movement and fine rotation while previewing.");

            _previewMoveForwardKey.SettingChanged += (_, _) => PreviewMoveForwardKey = _previewMoveForwardKey.Value;
            _previewMoveBackwardKey.SettingChanged += (_, _) => PreviewMoveBackwardKey = _previewMoveBackwardKey.Value;
            _previewMoveLeftKey.SettingChanged += (_, _) => PreviewMoveLeftKey = _previewMoveLeftKey.Value;
            _previewMoveRightKey.SettingChanged += (_, _) => PreviewMoveRightKey = _previewMoveRightKey.Value;
            _previewConfirmKey.SettingChanged += (_, _) => PreviewConfirmKey = _previewConfirmKey.Value;
            _previewRotateLeftKey.SettingChanged += (_, _) => PreviewRotateLeftKey = _previewRotateLeftKey.Value;
            _previewRotateRightKey.SettingChanged += (_, _) => PreviewRotateRightKey = _previewRotateRightKey.Value;
            _previewCancelKey.SettingChanged += (_, _) => PreviewCancelKey = _previewCancelKey.Value;
            _previewFineAdjustKey.SettingChanged += (_, _) => PreviewFineAdjustKey = _previewFineAdjustKey.Value;

            PreviewMoveForwardKey = _previewMoveForwardKey.Value;
            PreviewMoveBackwardKey = _previewMoveBackwardKey.Value;
            PreviewMoveLeftKey = _previewMoveLeftKey.Value;
            PreviewMoveRightKey = _previewMoveRightKey.Value;
            PreviewConfirmKey = _previewConfirmKey.Value;
            PreviewRotateLeftKey = _previewRotateLeftKey.Value;
            PreviewRotateRightKey = _previewRotateRightKey.Value;
            PreviewCancelKey = _previewCancelKey.Value;
            PreviewFineAdjustKey = _previewFineAdjustKey.Value;

            _terrainLevelPasses = Config.Bind(
                "Terrain", "TerrainLevelPasses", 2,
                new ConfigDescription(
                    "Number of terrain leveling passes to run before spike cleanup. Lower is faster; higher can smooth stubborn areas.",
                    new AcceptableValueRange<int>(1, 5)));
            _terrainLevelPasses.SettingChanged += (_, _) =>
                TerrainLevelPasses = Mathf.Clamp(_terrainLevelPasses.Value, 1, 5);
            TerrainLevelPasses = Mathf.Clamp(_terrainLevelPasses.Value, 1, 5);

            _terrainSpikeCleanupPasses = Config.Bind(
                "Terrain", "TerrainSpikeCleanupPasses", 2,
                new ConfigDescription(
                    "Number of spike cleanup passes after leveling. Lower is faster; higher can reduce edge peaks on rough terrain.",
                    new AcceptableValueRange<int>(1, 5)));
            _terrainSpikeCleanupPasses.SettingChanged += (_, _) =>
                TerrainSpikeCleanupPasses = Mathf.Clamp(_terrainSpikeCleanupPasses.Value, 1, 5);
            TerrainSpikeCleanupPasses = Mathf.Clamp(_terrainSpikeCleanupPasses.Value, 1, 5);

            _terrainStampRadius = Config.Bind(
                "Terrain", "TerrainStampRadius", 3.0f,
                new ConfigDescription(
                    "Radius of each terrain leveling disc stamp in metres. Controls how wide the green preview border is and how far terrain is affected beyond the build pad edge. Larger = smoother blending but wider terrain disturbance; smaller = tighter edge but may leave small gaps.",
                    new AcceptableValueRange<float>(3.0f, 6.0f)));
            _terrainStampRadius.SettingChanged += (_, _) =>
                TerrainStampRadius = Mathf.Clamp(_terrainStampRadius.Value, 3.0f, 6.0f);
            TerrainStampRadius = Mathf.Clamp(_terrainStampRadius.Value, 3.0f, 6.0f);

            _terrainHighPointDelta = Config.Bind(
                "Terrain", "TerrainHighPointDelta", 0.0f,
                new ConfigDescription(
                    "Extra height in metres added to the sampled highest point for the terrain leveling target. Final target is HighestPoint + Delta.",
                    new AcceptableValueRange<float>(0.0f, 4.0f)));
            _terrainHighPointDelta.SettingChanged += (_, _) =>
                TerrainHighPointDelta = Mathf.Clamp(_terrainHighPointDelta.Value, 0.0f, 4.0f);
            TerrainHighPointDelta = Mathf.Clamp(_terrainHighPointDelta.Value, 0.0f, 4.0f);

            _terrainUseStagedRaise = Config.Bind(
                "Terrain", "TerrainUseStagedRaise", false,
                "When enabled, leveling raises terrain in multiple vertical stages instead of a single full-height jump. Experimental: may help on some slopes but can worsen spikes on irregular edges.");
            _terrainUseStagedRaise.SettingChanged += (_, _) =>
                TerrainUseStagedRaise = _terrainUseStagedRaise.Value;
            TerrainUseStagedRaise = _terrainUseStagedRaise.Value;

            _terrainRaiseStepHeight = Config.Bind(
                "Terrain", "TerrainRaiseStepHeight", 0.5f,
                new ConfigDescription(
                    "Maximum vertical raise per stage in metres when TerrainUseStagedRaise is enabled. Smaller values create more stages and gentler terrain transitions.",
                    new AcceptableValueRange<float>(0.15f, 1.5f)));
            _terrainRaiseStepHeight.SettingChanged += (_, _) =>
                TerrainRaiseStepHeight = Mathf.Clamp(_terrainRaiseStepHeight.Value, 0.15f, 1.5f);
            TerrainRaiseStepHeight = Mathf.Clamp(_terrainRaiseStepHeight.Value, 0.15f, 1.5f);

            _terrainMaxRaiseStages = Config.Bind(
                "Terrain", "TerrainMaxRaiseStages", 1,
                new ConfigDescription(
                    "Upper limit on the number of vertical raise stages when TerrainUseStagedRaise is enabled.",
                    new AcceptableValueRange<int>(1, 16)));
            _terrainMaxRaiseStages.SettingChanged += (_, _) =>
                TerrainMaxRaiseStages = Mathf.Clamp(_terrainMaxRaiseStages.Value, 1, 16);
            TerrainMaxRaiseStages = Mathf.Clamp(_terrainMaxRaiseStages.Value, 1, 16);

            _terrainSkipSatisfiedCenterStamps = Config.Bind(
                "Terrain", "TerrainSkipSatisfiedCenterStamps", true,
                "When enabled, center leveling stamps are skipped if sampled terrain is already at/above target. Disable for exact legacy behavior (always stamp every center point each pass).");
            _terrainSkipSatisfiedCenterStamps.SettingChanged += (_, _) =>
                TerrainSkipSatisfiedCenterStamps = _terrainSkipSatisfiedCenterStamps.Value;
            TerrainSkipSatisfiedCenterStamps = _terrainSkipSatisfiedCenterStamps.Value;

            gameObject.AddComponent<FloorPlanBuilder>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded! " +
                $"Build: {_buildHotkey.Value}  Undo: {_undoHotkey.Value}  Progress HUD: {ProgressMessageType}  Terrain passes: {TerrainLevelPasses}  Spike cleanup passes: {TerrainSpikeCleanupPasses}  High-point delta: {TerrainHighPointDelta:F2}m  Staged raise: {TerrainUseStagedRaise} ({TerrainRaiseStepHeight:F2}m, max {TerrainMaxRaiseStages})  Skip satisfied center stamps: {TerrainSkipSatisfiedCenterStamps}  External wall heights: L1={ExternalWallHeightLevel1}, L2={ExternalWallHeightLevel2}, L3={ExternalWallHeightLevel3}  Wall/Pillar material: {WallPillarMaterial}  Staircase reach: {StaircaseReachMode}  Roof scaffolding: {RoofScaffolding} ({RoofScaffoldingType}/{RoofScaffoldingGableFlooring})  Scaffolding levels: {ScaffoldingLevels}  Scaffolding floor height: {ScaffoldingFloorHeight}m  Scaffolding floors: {ScaffoldingFloors}  Transverse beams: {TransverseScaffoldingBeams}  Longitudinal beams: {LongitudinalScaffoldingBeams}  Origin extra offset: {BuildOriginForwardOffset:F1}m  Preview move: {PreviewMoveStep:F2}/{PreviewFineMoveStep:F2}m  Preview rotate: {PreviewRotateStepDeg:F0}/{PreviewFineRotateStepDeg:F0}°  Build snap: {BuildRotationSnapDegrees:F1}°");
        }

        private void Update()
        {
            if (_buildHotkey.Value.IsDown())
            {
                var path = _vfpFilePath.Value.Trim();
                if (string.IsNullOrEmpty(path))
                {
                    Log.LogWarning("No floor plan file configured. Set 'FloorPlanFile' in the BepInEx config.");
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                        "ValheimFloorPlan: No .vfp file set in config!");
                    return;
                }
                FloorPlanBuilder.Instance.StartPreview(path);
            }

            if (_undoHotkey.Value.IsDown())
                FloorPlanBuilder.Instance.Undo();
        }

        /// <summary>Persists a new undo radius to the config and updates the in-memory property.</summary>
        internal static void SetUndoRadius(float radius)
        {
            if (Instance == null) return;
            Instance._undoRadius.Value = Mathf.Clamp(radius, 5f, 150f);
            // SettingChanged fires and updates UndoRadius automatically.
        }

        internal static void ShowProgressMessage(string message)
        {
            ShowWrappedMessage(ProgressMessageType, $"ValheimFloorPlan: {message}");
        }

        internal static void ShowWrappedMessage(MessageHud.MessageType messageType, string text, int maxLineLength = 72)
        {
            var wrapped = WrapHudText(text, maxLineLength);
            var player = Player.m_localPlayer;
            if (player == null)
                return;

            // Send the entire wrapped text (including newlines) as a single message
            // so the HUD displays all lines at once instead of overwriting them.
            player.Message(messageType, wrapped);
        }

        internal static string WrapHudText(string text, int maxLineLength = 72)
        {
            if (string.IsNullOrEmpty(text) || maxLineLength < 16)
                return text;

            var src = text.Replace("\r", string.Empty).Replace("\n", " ");
            var words = src.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return src;

            var sb = new System.Text.StringBuilder(src.Length + 16);
            int lineLen = 0;
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                int addLen = (lineLen == 0 ? 0 : 1) + w.Length;

                if (lineLen > 0 && lineLen + addLen > maxLineLength)
                {
                    sb.Append('\n');
                    lineLen = 0;
                }
                else if (lineLen > 0)
                {
                    sb.Append(' ');
                    lineLen++;
                }

                sb.Append(w);
                lineLen += w.Length;
            }

            return sb.ToString();
        }

        private static MessageHud.MessageType ParseProgressMessageType(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "CenterLeft", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "MiddleLeft", System.StringComparison.OrdinalIgnoreCase))
                return MessageHud.MessageType.Center;

            if (System.Enum.TryParse(normalized, true, out MessageHud.MessageType parsed))
                return parsed;

            Log?.LogWarning(
                $"Unknown ProgressMessagePosition '{value}'. Falling back to Center.");
            return MessageHud.MessageType.Center;
        }

        private void ApplyScaffoldingRules()
        {
            if (_applyingScaffoldingRules)
                return;

            _applyingScaffoldingRules = true;
            try
            {
                int clampedFloorPlanLevels = Mathf.Clamp(_floorPlanLevels.Value, 1, 3);
                if (_floorPlanLevels.Value != clampedFloorPlanLevels)
                    _floorPlanLevels.Value = clampedFloorPlanLevels;

                int minScaffoldingLevels = clampedFloorPlanLevels;
                int clampedLevels = Mathf.Clamp(_scaffoldingLevels.Value, minScaffoldingLevels, 3);
                if (_scaffoldingLevels.Value != clampedLevels)
                    _scaffoldingLevels.Value = clampedLevels;

                int floorHeight = _scaffoldingFloorHeight.Value;
                int floorHeight2 = _scaffoldingFloorHeight2.Value;
                int floorHeight3 = _scaffoldingFloorHeight3.Value;

                if (clampedFloorPlanLevels > 1)
                {
                    if (!_roofScaffolding.Value)
                        _roofScaffolding.Value = true;

                    if (!_scaffoldingFloors.Value)
                        _scaffoldingFloors.Value = true;

                    if (!_transverseScaffoldingBeams.Value)
                        _transverseScaffoldingBeams.Value = true;

                    if (!_longitudinalScaffoldingBeams.Value)
                        _longitudinalScaffoldingBeams.Value = true;
                }

                FloorPlanLevels = clampedFloorPlanLevels;
                ScaffoldingLevels = clampedLevels;
                ScaffoldingFloorHeight = floorHeight;
                ScaffoldingFloorHeight2 = floorHeight2;
                ScaffoldingFloorHeight3 = floorHeight3;
                RoofScaffolding = _roofScaffolding.Value;
                ScaffoldingFloors = _scaffoldingFloors.Value;
                TransverseScaffoldingBeams = _transverseScaffoldingBeams.Value;
                LongitudinalScaffoldingBeams = _longitudinalScaffoldingBeams.Value;

                if (_externalWallHeightLevel1 != null && _externalWallHeightLevel2 != null && _externalWallHeightLevel3 != null)
                {
                    int clampedExternalWallHeightLevel1 = Mathf.Clamp(
                        _externalWallHeightLevel1.Value,
                        1,
                        GetMaxExternalWallHeightForLevel(0, floorHeight, floorHeight2, floorHeight3));
                    int clampedExternalWallHeightLevel2 = Mathf.Clamp(
                        _externalWallHeightLevel2.Value,
                        1,
                        GetMaxExternalWallHeightForLevel(1, floorHeight, floorHeight2, floorHeight3));
                    int clampedExternalWallHeightLevel3 = Mathf.Clamp(
                        _externalWallHeightLevel3.Value,
                        1,
                        GetMaxExternalWallHeightForLevel(2, floorHeight, floorHeight2, floorHeight3));

                    if (_externalWallHeightLevel1.Value != clampedExternalWallHeightLevel1)
                        _externalWallHeightLevel1.Value = clampedExternalWallHeightLevel1;
                    if (_externalWallHeightLevel2.Value != clampedExternalWallHeightLevel2)
                        _externalWallHeightLevel2.Value = clampedExternalWallHeightLevel2;
                    if (_externalWallHeightLevel3.Value != clampedExternalWallHeightLevel3)
                        _externalWallHeightLevel3.Value = clampedExternalWallHeightLevel3;

                    ExternalWallHeightLevel1 = clampedExternalWallHeightLevel1;
                    ExternalWallHeightLevel2 = clampedExternalWallHeightLevel2;
                    ExternalWallHeightLevel3 = clampedExternalWallHeightLevel3;
                }
            }
            finally
            {
                _applyingScaffoldingRules = false;
            }
        }

        internal static int GetScaffoldingFloorHeightForLevel(int levelIndex)
        {
            switch (levelIndex)
            {
                case 0:
                    return ScaffoldingFloorHeight;
                case 1:
                    return ScaffoldingFloorHeight2;
                default:
                    return ScaffoldingFloorHeight3;
            }
        }

        internal static int GetExternalWallHeightForLevel(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 2))
            {
                case 0:
                    return ExternalWallHeightLevel1;
                case 1:
                    return ExternalWallHeightLevel2;
                default:
                    return ExternalWallHeightLevel3;
            }
        }

        internal static int GetMaxExternalWallHeightForLevel(int levelIndex)
        {
            return GetMaxExternalWallHeightForLevel(
                levelIndex,
                ScaffoldingFloorHeight,
                ScaffoldingFloorHeight2,
                ScaffoldingFloorHeight3);
        }

        internal static int GetMaxExternalWallHeightForLevel(int levelIndex, int scaffoldingFloorHeight, int scaffoldingFloorHeight2, int scaffoldingFloorHeight3)
        {
            int levelHeight;
            switch (Mathf.Clamp(levelIndex, 0, 2))
            {
                case 0:
                    levelHeight = scaffoldingFloorHeight;
                    break;
                case 1:
                    levelHeight = scaffoldingFloorHeight2;
                    break;
                default:
                    levelHeight = scaffoldingFloorHeight3;
                    break;
            }

            return Mathf.Max(1, levelHeight * 2);
        }

        internal static bool GetEffectiveTransverseScaffoldingBeams(int scaffoldingLevels)
        {
            return Mathf.Clamp(scaffoldingLevels, 1, 3) > 1 || TransverseScaffoldingBeams;
        }

        internal static bool GetEffectiveLongitudinalScaffoldingBeams(int scaffoldingLevels)
        {
            return Mathf.Clamp(scaffoldingLevels, 1, 3) > 1 || LongitudinalScaffoldingBeams;
        }

        internal static bool UseGableFloorUnderlay()
        {
            return RoofScaffoldingType == RoofScaffoldingTypeOption.Gable &&
                   RoofScaffoldingGableFlooring == RoofScaffoldingGableFlooringOption.RoofWithFloorUnderlay;
        }

        internal static void RefreshScaffoldingRules()
        {
            Instance?.ApplyScaffoldingRules();
        }

        private static StructuralMaterial ParseStructuralMaterial(string value)
        {
            if (string.Equals(value?.Trim(), "Wood", System.StringComparison.OrdinalIgnoreCase))
                return StructuralMaterial.Wood;

            return StructuralMaterial.Stone;
        }

        private static RoofScaffoldingTypeOption ParseRoofScaffoldingType(string value)
        {
            if (string.Equals(value?.Trim(), "Flat", System.StringComparison.OrdinalIgnoreCase))
                return RoofScaffoldingTypeOption.Flat;

            return RoofScaffoldingTypeOption.Gable;
        }

        private static RoofScaffoldingGableFlooringOption ParseRoofScaffoldingGableFlooring(string value)
        {
            if (string.Equals(value?.Trim(), "RoofOnly", System.StringComparison.OrdinalIgnoreCase))
                return RoofScaffoldingGableFlooringOption.RoofOnly;

            return RoofScaffoldingGableFlooringOption.RoofWithFloorUnderlay;
        }

        private static StaircaseReachModeOption ParseStaircaseReachMode(string value)
        {
            if (string.Equals(value?.Trim(), "AllTheWay", System.StringComparison.OrdinalIgnoreCase))
                return StaircaseReachModeOption.AllTheWay;

            return StaircaseReachModeOption.ToTheNextLevelOnly;
        }
    }
}

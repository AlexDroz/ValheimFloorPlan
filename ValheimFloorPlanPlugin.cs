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

        public const string PluginGUID = "com.alexdroz.valheimfloorplan";
        public const string PluginName = "ValheimFloorPlan";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log = null!;
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
        internal static int ExternalWallHeight { get; private set; } = 1;
        internal static StructuralMaterial WallPillarMaterial { get; private set; } = StructuralMaterial.Stone;
        internal static float BuildOriginForwardOffset { get; private set; } = 12f;
        internal static float PreviewMoveStep { get; private set; } = 2f;
        internal static float PreviewFineMoveStep { get; private set; } = 0.5f;
        internal static float PreviewRotateStepDeg { get; private set; } = 15f;
        internal static float PreviewFineRotateStepDeg { get; private set; } = 5f;
        internal static KeyCode PreviewMoveForwardKey { get; private set; } = KeyCode.UpArrow;
        internal static KeyCode PreviewMoveBackwardKey { get; private set; } = KeyCode.DownArrow;
        internal static KeyCode PreviewMoveLeftKey { get; private set; } = KeyCode.LeftArrow;
        internal static KeyCode PreviewMoveRightKey { get; private set; } = KeyCode.RightArrow;
        internal static KeyCode PreviewConfirmKey { get; private set; } = KeyCode.E;
        internal static KeyCode PreviewRotateLeftKey { get; private set; } = KeyCode.Q;
        internal static KeyCode PreviewRotateRightKey { get; private set; } = KeyCode.R;
        internal static KeyCode PreviewCancelKey { get; private set; } = KeyCode.Escape;
        internal static KeyCode PreviewFineAdjustKey { get; private set; } = KeyCode.LeftShift;

        private ConfigEntry<string> _vfpFilePath = null!;
        private ConfigEntry<KeyboardShortcut> _buildHotkey = null!;
        private ConfigEntry<KeyboardShortcut> _undoHotkey = null!;
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
        private ConfigEntry<int> _externalWallHeight = null!;
        private ConfigEntry<string> _wallPillarMaterial = null!;
        private ConfigEntry<float> _buildOriginForwardOffset = null!;
        private ConfigEntry<float> _previewMoveStep = null!;
        private ConfigEntry<float> _previewFineMoveStep = null!;
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

        private void Awake()
        {
            Log = Logger;

            _vfpFilePath = Config.Bind(
                "General", "FloorPlanFile", "",
                "Full path to the .vfp floor plan file exported from Valheim Floor Plan Designer.");

            _buildHotkey = Config.Bind(
                "General", "BuildHotkey", new KeyboardShortcut(KeyCode.F8),
                "Hotkey to build the floor plan at your current position.");

            _undoHotkey = Config.Bind(
                "General", "UndoHotkey", new KeyboardShortcut(KeyCode.F9),
                "Hotkey to undo the last floor plan build (removes pieces and restores terrain).");

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

            _externalWallHeight = Config.Bind(
                "Building", "ExternalWallHeight", 1,
                new ConfigDescription(
                    "How many levels high external Wall/Pillar pieces should be stacked.",
                    new AcceptableValueRange<int>(1, 4)));
            _externalWallHeight.SettingChanged += (_, _) =>
                ExternalWallHeight = Mathf.Clamp(_externalWallHeight.Value, 1, 4);
            ExternalWallHeight = Mathf.Clamp(_externalWallHeight.Value, 1, 4);

            _wallPillarMaterial = Config.Bind(
                "Building", "WallPillarMaterial", "Stone",
                new ConfigDescription(
                    "Material used for Wall and Pillar types. Allowed: Stone, Wood.",
                    new AcceptableValueList<string>("Stone", "Wood")));
            _wallPillarMaterial.SettingChanged += (_, _) =>
                WallPillarMaterial = ParseStructuralMaterial(_wallPillarMaterial.Value);
            WallPillarMaterial = ParseStructuralMaterial(_wallPillarMaterial.Value);

            _buildOriginForwardOffset = Config.Bind(
                "General", "BuildOriginForwardOffset", 12f,
                new ConfigDescription(
                    "How far in front of the player (in meters) the plan origin is placed for preview/build.",
                    new AcceptableValueRange<float>(10f, 20f)));
            _buildOriginForwardOffset.SettingChanged += (_, _) =>
                BuildOriginForwardOffset = Mathf.Clamp(_buildOriginForwardOffset.Value, 10f, 20f);
            BuildOriginForwardOffset = Mathf.Clamp(_buildOriginForwardOffset.Value, 10f, 20f);

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

            _previewRotateStepDeg = Config.Bind(
                "Preview", "RotateStepDegrees", 15f,
                new ConfigDescription(
                    "How far the preview rotates per rotate key press, in degrees.",
                    new AcceptableValueRange<float>(1f, 90f)));
            _previewRotateStepDeg.SettingChanged += (_, _) =>
                PreviewRotateStepDeg = Mathf.Clamp(_previewRotateStepDeg.Value, 1f, 90f);
            PreviewRotateStepDeg = Mathf.Clamp(_previewRotateStepDeg.Value, 1f, 90f);

            _previewFineRotateStepDeg = Config.Bind(
                "Preview", "FineRotateStepDegrees", 5f,
                new ConfigDescription(
                    "How far the preview rotates per key press while the fine-adjust key is held, in degrees.",
                    new AcceptableValueRange<float>(1f, 45f)));
            _previewFineRotateStepDeg.SettingChanged += (_, _) =>
                PreviewFineRotateStepDeg = Mathf.Clamp(_previewFineRotateStepDeg.Value, 1f, 45f);
            PreviewFineRotateStepDeg = Mathf.Clamp(_previewFineRotateStepDeg.Value, 1f, 45f);

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
                "Preview", "RotateRightKey", KeyCode.R,
                "Preview rotation key for rotating clockwise.");
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

            gameObject.AddComponent<FloorPlanBuilder>();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded! " +
                $"Build: {_buildHotkey.Value}  Undo: {_undoHotkey.Value}  Progress HUD: {ProgressMessageType}  Terrain passes: {TerrainLevelPasses}  Spike cleanup passes: {TerrainSpikeCleanupPasses}  High-point delta: {TerrainHighPointDelta:F2}m  Staged raise: {TerrainUseStagedRaise} ({TerrainRaiseStepHeight:F2}m, max {TerrainMaxRaiseStages})  Skip satisfied center stamps: {TerrainSkipSatisfiedCenterStamps}  External wall height: {ExternalWallHeight}  Wall/Pillar material: {WallPillarMaterial}  Origin offset: {BuildOriginForwardOffset:F1}m  Preview move: {PreviewMoveStep:F2}/{PreviewFineMoveStep:F2}m  Preview rotate: {PreviewRotateStepDeg:F0}/{PreviewFineRotateStepDeg:F0}°");
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

        private static StructuralMaterial ParseStructuralMaterial(string value)
        {
            if (string.Equals(value?.Trim(), "Wood", System.StringComparison.OrdinalIgnoreCase))
                return StructuralMaterial.Wood;

            return StructuralMaterial.Stone;
        }
    }
}

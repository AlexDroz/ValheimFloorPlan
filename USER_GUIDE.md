# ValheimFloorPlan - User Guide

## What This Mod Does
ValheimFloorPlan lets you place a pre-made floor plan from a `.vfp` file directly in game.

From a player perspective, the flow is simple:
1. Choose a `.vfp` file in config.
2. Press the build hotkey to open preview.
3. Move/rotate preview until it looks right.
4. Press the preview confirm key to build (default `E`).
5. Press undo hotkey to remove placed pieces and restore terrain snapshot.

## Requirements
- Valheim
- BepInEx for Valheim
- ValheimFloorPlan installed in your BepInEx plugins folder (or via Thunderstore Mod Manager)

## Installation
### Option 1: Thunderstore Mod Manager
1. Install the mod with Thunderstore Mod Manager.
2. Launch the game once.
3. Close the game and edit config if needed.

### Option 2: Manual Install
1. Install BepInEx for Valheim.
2. Copy the mod DLL into `BepInEx/plugins`.
3. Launch the game once to generate the config file.

## Configuration
Config file path (default):
- `BepInEx/config/com.yourname.valheimfloorplan.cfg`

Important settings:
- `FloorPlanFile`: Full path to your `.vfp` file.
- `BuildHotkey`: Starts preview mode (default `F8`).
- `UndoHotkey`: Removes nearby VFP pieces and restores terrain snapshot (default `F9`).
- `BuildOriginForwardOffset`: Initial preview origin in front of your character (default `12`, range `10-20`).
- `ProgressMessagePosition`: HUD slot for status text (default `CenterLeft`, mapped to `Center`).
- `TerrainLevelPasses`: Main leveling pass count (default `2`, range `1-5`).
- `TerrainSpikeCleanupPasses`: Cleanup pass count after leveling (default `2`, range `1-5`).
- `TerrainStampRadius`: Radius (meters) of each leveling disc stamp and preview outer wall width (default `3.0`, range `3.0-6.0`).
- `TerrainSkipSatisfiedCenterStamps`: Skips center stamps already at/above target terrain height (default `true`).
- `TerrainUseStagedRaise`: Experimental staged vertical raise mode (default `false`).
- `TerrainRaiseStepHeight`: Max raise per stage when staged raise is enabled (default `0.5`, range `0.15-1.5`).
- `TerrainMaxRaiseStages`: Max number of raise stages when staged raise is enabled (default `1`, range `1-16`).
- `ExternalWallHeight`: Stacks external `Wall` and `Pillar` objects to this many levels (default `1`, range `1-4`).
- `WallPillarMaterial`: Choose `Stone` or `Wood` for `Wall` and `Pillar` types (default `Stone`).

Preview input settings (all configurable):
- `MoveForwardKey` / `MoveBackwardKey` / `MoveLeftKey` / `MoveRightKey` (defaults: arrow keys)
- `RotateLeftKey` / `RotateRightKey` (defaults: `Q` / `R`)
- `ConfirmKey` (default `E`)
- `CancelKey` (default `Escape`, right-click also cancels)
- `FineAdjustKey` (default `LeftShift`)
- `MoveStep`, `FineMoveStep`, `RotateStepDegrees`, `FineRotateStepDegrees`


Optional `.vfp` wall-face field:
- Piece lines can include a sixth field: `piece,col,row,type,rotation,wallFace`
- `wallFace` supports `outer` (default behavior) or `inner`
- This is used when `WallPillarMaterial=Wood` to orient wood wall inner/outer faces correctly

Preview controls are also configurable in the same file.

## Quick Start
1. Export your design as a `.vfp` file.
2. Set `FloorPlanFile` to that file path.
3. Enter world and stand near your target build area.
4. Press `F8` to start preview.
5. Adjust placement:
   - Arrow keys move preview origin.
   - `Q` / `R` rotate.
   - Hold `LeftShift` for fine movement/rotation.
   - `Esc` or right-click cancels preview.
6. Press `E` to confirm and start build.
7. Wait for terrain prep and piece placement to complete.

## Typical In-Game Workflow
1. Scout and clear your area.
2. Open preview with `F8`.
3. Nudge/rotate until the footprint is exactly where you want it.
4. Confirm with the preview confirm key (default `E`).
5. Let the mod:
   - Snapshot terrain
   - Clear major rock-like blockers
   - Level terrain
   - Wait for terrain physics to settle
   - Place pieces
   - Run post-build spike cleanup
6. If placement is wrong, use `F9` and try again.

## Controls (Default)
- Build Preview: `F8`
- Undo: `F9`
- Move Preview: `UpArrow`, `DownArrow`, `LeftArrow`, `RightArrow`
- Rotate Preview: `Q` / `R`
- Confirm Build: `E`
- Fine Adjust Modifier: `LeftShift`
- Cancel Preview: `Esc` or right-click

## Terrain Risk Preview Markers
While in preview, the mod evaluates edge terrain risk and can show orange risk markers when risk is `MEDIUM` or `HIGH`:
- Terrain-level markers: hotspot positions near troublesome edge terrain.
- Top-edge markers: three fixed markers on the top rim of the green outer preview wall so risk is visible from downhill camera angles.

Risk text includes:
- `step`: strongest local cross-edge height jump.
- `relief`: total edge height range around the footprint.

Practical note:
- Marker/hint visibility updates while you nudge/rotate preview; there is a short delay before repeated hint spam to keep HUD readable.



Important notes:
- The clip tool is lowering-only. It will not raise low ground to meet the disc.
- Terrain clip captures a fresh terrain snapshot before applying, so `Undo` can restore the clip in the current session.
- Like other terrain restores, undoing a clip may not become visible immediately; move away and return to refresh terrain chunk visuals.

## Undo Behavior and Limits
Undo does two things:
1. Removes placed VFP-tagged pieces within about 75 meters of the player.
2. Restores terrain from the captured snapshot.

Important notes:
- Piece removal works across sessions because pieces are tagged in ZDO data.
- Terrain restore is session-bound: it requires a snapshot captured in the current game session.
- Terrain restore is based on the most recently captured snapshot.
- Terrain restore can be visually delayed. If the ground still looks unchanged right after undo, move away from the area and return so the terrain chunk visuals refresh.

## Performance and Quality Tuning
If builds are slow:
- Reduce `TerrainLevelPasses`.
- Reduce `TerrainSpikeCleanupPasses`.

If terrain still looks rough or has edge artifacts:
- Increase `TerrainLevelPasses`.
- Increase `TerrainSpikeCleanupPasses`.

Suggested starting presets:
- Fast test builds: `1` / `1`
- Balanced (default): `2` / `2`
- Difficult terrain: `4` / `3` or higher

## Troubleshooting
### Nothing happens when pressing build hotkey
- Verify `FloorPlanFile` is set and points to a real `.vfp` file.
- Check keybind conflicts.
- Check BepInEx console/log for errors.

### Build appears in wrong spot
- Use preview controls before confirming.
- Tune `BuildOriginForwardOffset`.
- Rebuild after canceling preview.

### Pieces collapse or float
- Let the build process finish completely before interacting.
- Increase terrain pass settings on difficult terrain.
- Retry build after undo.

### Undo did not restore expected terrain
- Undo restores the latest captured snapshot only.
- Terrain restore only works for snapshots captured in the current session.
- If snapshot capture failed, piece removal may still work while terrain restore does not.
- Terrain visuals may not refresh instantly; move to another area, then come back to force a visible refresh.

## FAQ
### Can I rotate the whole plan before building?
Yes. Use `Q` / `R` in preview mode.

### Can I fine-adjust placement?
Yes. Hold `LeftShift` while moving/rotating.

## Best Practices
- Always verify preview alignment and risk markers before confirming (`E`).
- Keep one backup world save while testing new pass settings.
- Start with balanced pass values, then tune based on terrain complexity.
- Keep `TerrainSkipSatisfiedCenterStamps=true` as a first-choice setting for rough terrain edge stability.

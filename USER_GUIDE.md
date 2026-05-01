# ValheimFloorPlan - User Guide

## What This Mod Does
ValheimFloorPlan lets you place a pre-made floor plan from a `.vfp` file directly in game.

From a player perspective, the flow is simple:
1. Choose a `.vfp` file in config.
2. Press the build hotkey to open preview.
3. Move/rotate preview until it looks right.
4. Left-click to build.
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
- `TerrainLevelPasses`: Main leveling pass count (default `2`, range `1-5`).
- `TerrainSpikeCleanupPasses`: Cleanup pass count after leveling (default `2`, range `1-5`).
- `ExternalWallHeight`: Stacks external `Wall` and `Pillar` objects to this many levels (default `1`, range `1-4`).
- `WallPillarMaterial`: Choose `Stone` or `Wood` for `Wall` and `Pillar` types (default `Stone`).
- `WoodWallOuterOffset`: Outward shift for external wood walls so they align to floor edges (default `0.2`, range `0.0-0.5`).

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
   - `Q` / `E` rotate.
   - Hold `LeftShift` for fine movement/rotation.
   - `Esc` or right-click cancels preview.
6. Left-click to confirm and start build.
7. Wait for terrain prep and piece placement to complete.

## Typical In-Game Workflow
1. Scout and clear your area.
2. Open preview with `F8`.
3. Nudge/rotate until the footprint is exactly where you want it.
4. Confirm with left-click.
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
- Rotate Preview: `Q` / `E`
- Fine Adjust Modifier: `LeftShift`
- Cancel Preview: `Esc` or right-click
- Confirm Build: left-click

## Undo Behavior and Limits
Undo does two things:
1. Removes placed VFP-tagged pieces within about 75 meters of the player.
2. Restores terrain from the captured snapshot.

Important notes:
- Piece removal works across sessions because pieces are tagged in ZDO data.
- Terrain restore requires a snapshot from the current run/session.
- Terrain restore is based on the most recently captured snapshot.

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
- If snapshot capture failed, piece removal may still work while terrain restore does not.

## FAQ
### Can I rotate the whole plan before building?
Yes. Use `Q` / `E` in preview mode.

### Can I fine-adjust placement?
Yes. Hold `LeftShift` while moving/rotating.

### Is moat digging enabled by default?
No. Moat logic exists in code but is not part of the default build sequence.

## Best Practices
- Always verify preview alignment before left-click confirm.
- Keep one backup world save while testing new pass settings.
- Start with balanced pass values, then tune based on terrain complexity.

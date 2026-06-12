# Changelog

## Unreleased

- **New `OpenTop` config option:** when `true`, leaves the topmost scaffold level open to the sky — no roof deck, no beams, and no vertical scaffolding on the external walls of the topmost level, regardless of `ScaffoldingFloors` or `RoofStyle`. Hearth chimneys still rise through the open level. Staircases are clamped to the highest real floor deck. Works consistently for 2-level and 3-level builds.
- **`RoofScaffoldingType` replaced by `RoofStyle` + `OpenTop`:** `RoofStyle` controls the deck shape (`Gable` / `Flat`); `OpenTop` is a separate boolean that overrides all topmost-level placement. **Automatic migration:** on first load the mod reads any existing `RoofScaffoldingType` value, writes the equivalent `RoofStyle` / `OpenTop` values, removes the old key, and saves the config. No manual config editing required. Legacy `.vpfset` preset files are also migrated in memory when loaded.
- **Hearth chimney corners now have vertical support poles.** Four `woodiron_pole` columns are placed at the corners of each Hearth chimney shaft, one column per scaffold level, spanning the full configured floor height for that level. This anchors the chimney walls to the floor at every level and is applied regardless of `OpenTop` or `RoofStyle`.
- **Beam spans no longer skip entirely when a chimney or staircase is nearby.** Previously, a transverse or longitudinal beam was omitted entirely if any part of its span overlapped a Hearth or Staircase footprint. Each 2m beam piece is now checked individually, so only the pieces whose centres fall inside the opening are skipped and the rest of the beam is placed normally.
- **Tight-radius FlexiWalls now place proportionally more wall pieces.** On curves tighter than ~3m radius, the rotation angle between adjacent wall pieces now scales down in proportion to the radius instead of growing as the curve tightens, closing the wedge gaps that used to open up on the outer face of small arcs and circles. Gentler curves are unchanged.

## 2.1.3

- **New FlexiWall tool in the Designer:** draw curved or straight walls of any shape — arcs, circles, sweeping curves — that are not constrained to the rectangular grid. Select the tool, click a start cell, click an end cell, then drag the midpoint handle to bend the arc into shape. Endpoints snap automatically to the correct cell-edge boundary so adjacent FlexiWalls meet without gaps.
- **Full circle support:** placing start and end in adjacent cells and dragging the midpoint to the opposite side of the circle produces a closed ring wall — both in the Designer preview and the in-game build.


- **New `InternalWallHeightLevel1/2/3` config parameters:** FlexiWalls whose control points lie strictly inside the layout perimeter are now treated as interior walls and stacked to the new `InternalWallHeightLevel` height instead of the external wall height. Set it lower than `ExternalWallHeightLevel` for half-height room dividers, or match it for uniform wall height throughout.


- **Fixed: Gable roofs no longer have gaps caused by openings below.** Staircases, Hearths, and other upper-level openings could previously block or punch holes through Gable roof panels, leaving the roof incomplete. The Gable roof now always builds as a full, continuous surface across the entire footprint regardless of what's underneath it.
- **Fixed: Staircases no longer leave holes in the roof above them.** Roof tiles directly over a staircase shaft were being destroyed during cleanup even though staircases don't need an opening through the roof — they're now left intact, so the roof stays solid above stairwells.
- **Fixed: Hearth chimneys now properly clear Gable roofs.** Chimney stacks — including those on the ground floor — now measure their height against the actual roof apex and clear away any surrounding roof and deck tiles, so the chimney extends cleanly above the roofline instead of being blocked or clipped by it.

- **Designer: Excel-style row/column reference labels.** Numeric grid references now run along all four edges of the canvas (top/bottom for columns, left/right for rows), matching the same 0-based `col`/`row` numbering used internally and in `.vfp` files, making it much easier to line up placements by eye or by coordinate.
- **Designer: furniture pieces are now selectable, movable, and rotatable after placement.** `Bed`, `Staircase`, `Hearth`, and `Workbench` pieces show a small handle at their centre once placed (or loaded from a file). Drag the handle to move the piece, click it to select it, then use the arrow keys or mouse wheel to rotate it in place — no need to delete and re-place a piece just to nudge or turn it.
- **Designer: reworked right-click and deletion to prevent accidental removals.** Right-click (and `Escape`) now simply cancels whatever's active — an in-progress FlexiWall draw, a piece selection/drag, or the current tool — without deleting anything. To delete a piece, hover it and press `Delete`/`Backspace`; this also removes the currently selected piece if one is selected. `Escape` also now reliably clears tool selection and any in-progress interaction, returning the cursor to normal.
- **Designer: FlexiWalls use the same select-then-delete flow as furniture pieces.** Placed FlexiWalls show a midpoint handle that can be clicked at any time (no need to have the FlexiWall tool active) to select the wall — shown with the same dashed yellow outline used for selected furniture — and then removed with `Delete`/`Backspace`. Right-click on a FlexiWall handle no longer deletes it; right-click is now purely a cancel action everywhere, matching `Escape`. While the FlexiWall tool is active, the handle still drags to reshape the curve as before.
- **Designer: selecting a placed piece via its handle also activates the matching tool.** Clicking a furniture piece's or FlexiWall's handle switches the active tool radio to that piece's type — for FlexiWalls, this is what re-enables the handle's drag-to-reshape behavior immediately after selecting it.
- **Designer: `Delete`/`Backspace` removal is now scoped to the active tool's piece type.** With the `Hearth` tool selected, for example, `Delete`/`Backspace` can only remove `Hearth` pieces — Floors, Walls, and other types under the cursor are left alone, preventing accidental removal of the wrong piece type while focused on one kind. With no tool selected, any piece type can still be removed.
- **Designer: the piece `Delete`/`Backspace` would remove is now highlighted as you hover.** When a tool is active (or a piece/FlexiWall is selected), the exact piece that the next `Delete`/`Backspace` press would remove is outlined and tinted in red on the canvas, so you can see precisely what's targeted before committing to the deletion.

## 2.0.3

- **New `FloorPlanDirectory` config option:** Set this to a folder path and the `FloorPlanFile`, `FloorPlanFileLevel2`, and `FloorPlanFileLevel3` fields can then be bare filenames instead of full paths. Preset bundles are also read from and written to this folder when set. Existing deployments with full paths in the file fields continue to work unchanged — full absolute paths are always used as-is regardless of this setting.
- **Minor Staircase Tweaks:** Minor changes to Step Height and Rotation Angle to make the climb up the steps smoother. Step parameters are now exposed in the config.
- **Support for Upper Level Hearths:** An extra horizontal scaffolding support is added underneath the Hearth to prevent it from collapsing when they are on upper levels. May not work in every case but if you place the Hearth near a wall (wood or stone) it should be stable now.

- **Designer Graphics Improved:** Improved graphics to help with placement including better overlap detection.
- **Designer Help Expanded:** An attempt to build a Valheim Complete Workbench Matrix describing Workbenches, their build material requirements and related Boss info. All intended to help with layout design.


## 2.0.2

- **Documentation updates:** Example images showing Three Level Floor Plan.
- **Cosmectic :** Log Beams surround Hearth to make it look better.
- **Hearth Upper Floor Limits:** If you place a Hearth on an upper level it may break unless it is supported properly. Best to place it so it connects/touches to a Stone outer edge wall which has scaffolding support
- **Workench Placement Angles:** Workbenches can be rotated in increments of 22.5 degreee angles. See StarBase.vpf


## 2.0.1

- **Upper-level preview bands:** when `FloorPlanLevels` is `2` or `3`, the in-world build preview now shows a red floating rectangle at the height of each upper floor (based on `ScaffoldingFloorHeight` / `ScaffoldingFloorHeight#2`), so you can see the full multi-storey footprint before confirming placement.
- **Keep-terrain undo mode (`Ctrl+F9`):** a new hotkey (configurable via `UndoKeepTerrainHotkey`, default `Ctrl+F9`) starts a piece-only undo that removes VFP pieces but discards the terrain snapshot, so leveled ground is preserved. Confirmation works the same as normal undo — the mode is latched so you can confirm with either `Ctrl+F9` or `F9` within the 5-second window.
- **In-game preset bundle manager (`.vpfset`):** added timestamped export (`Ctrl+F8`) and interactive timed import selection (`Alt+F8`) for sharing/reloading full build presets (layout files + non-key config settings). Export file names now use `<BundleName>-YYYYMMDD-HHMMSS.vpfset`, and after import the config `BundleName` is normalized back to the imported preset prefix (timestamp removed). Import selection shows bundle name plus index (`X of N`), supports `Right/Left` navigation, `Enter` to confirm import, `Escape` to cancel, and auto-cancels on timeout.


- **3 Level Build Demo video published:** https://youtu.be/jz39KSSfhJ0


## 2.0.0

- **Critical bug fix — terrain levelling restored:** A long-standing coordinate offset error in `TerrainLeveler` caused all stamp positions to be calculated hundreds of metres away from the actual build site whenever the player was not near world origin. As a result, terrain levelling was silently skipped on virtually all real-world builds. This has been corrected; levelling now works correctly at any world position.

- Designer now supports 3 in-memory level layouts with quick switching (Level 1/2/3), per-level file/dirty state, and overlay checkboxes for cross-level visual planning.
- Overlay clash hints are now directional and content-aware: bright red dotted outlines flag overlap candidates across visible levels, with lower-level Staircase/Hearth projecting upward while non-vertical tools do not.
- Bed pieces now show a head-direction arrow in the Designer, and are placed in-game with the correct head orientation matching the Designer layout.
- Expanded multi-floor support for 2nd and 3rd floor plans, including upper-level staircase placement.
- Multi-level builds now place pieces level-by-level with route-critical parts first (staircases/hearths), then remaining pieces.
- Stair and chimney shafts are now treated as protected vertical routes so higher-level pieces do not block those paths.
- Upper-level floor tiles from plan files are skipped when scaffold decks already provide those floors, avoiding duplicate overlap.
- Improved clash feedback for upper levels:
	- clearer warning categories in HUD/logs,
	- per-level clash signposts with level-specific detail signs.
- Improved upper-level wall behavior:
	- walls/pillars/doorways can be placed on upper-level perimeter cells,
	- perimeter wall/pillar height now uses per-level settings (`ExternalWallHeightLevel1/2/3`) with dynamic per-level caps.
- Added `StaircaseReachMode` config (`ToTheNextLevelOnly` / `AllTheWay`) to control whether staircases climb only to the next level or continue to the highest available level.
- Fixed staircase shaft punch-through so deck/beam openings and higher-level staircase-shaft blocking now respect `StaircaseReachMode` (no extra holes above the configured reach).
- Improved validation and warnings for invalid upper-floor layout files and bounds mismatches.
- Updated and expanded user/developer documentation to describe current multi-level behavior.

## 1.1.0

- **New Spiral Staircase build feature:** added a compact center-pole spiral staircase that climbs from ground floor toward upper scaffold floors/roof with improved step flow and safer outer-edge guarding.
- **Scaffolding beam options now auto-enable for multi-level scaffolds:** when `ScaffoldingLevels` is set above `1`, `TransverseScaffoldingBeams` and `LongitudinalScaffoldingBeams` are automatically forced to `true` and written back to config so the saved settings match the required build behavior.
- **External wall height now follows scaffold capacity:** `ExternalWallHeight` is now capped at `ScaffoldingLevels x ScaffoldingFloorHeight`, and oversized values are clamped back to the maximum allowed by the current scaffold setup.
- **Scaffold levels can now use different heights:** added `ScaffoldingFloorHeight#2` and `ScaffoldingFloorHeight#3` so each scaffold story can use its own validated height, and `ExternalWallHeight` now caps against the sum of the active scaffold floor heights.
- **Hearths now cut scaffold floor openings and vent through wood chimneys:** scaffold decks leave open space above Hearth footprints, and a wood chimney stack starts about 3m above the Hearth so the fire remains accessible while smoke can exit above the top scaffold level.
- **Top scaffold roof mode is now configurable:** added `RoofScaffoldingType` with `Gable` (default) and `Flat` options for the topmost scaffold level when `ScaffoldingFloors` is enabled.
- **Gable top surface is now configurable:** added `RoofScaffoldingGableFlooring` for `RoofScaffoldingType=Gable`, with `RoofWithFloorUnderlay` (default) and `RoofOnly` options.

## 1.0.9

- **Added Workbench support in Designer and Builder:** new `Workbench` tool can be placed in the Designer with rotation support and is built in-game using the mapped workstation footprint.
- **Added Hearth and Bed tools:** the Designer and builder now support `Hearth` (`4x3`) and basic `Bed` (`2x4`) footprints.
- **Designer/build orientation now match:** preview rotation, final build rotation, and piece placement now follow the Designer layout orientation consistently.
- **Roof scaffolding now uses wood-iron members:** perimeter scaffold columns and scaffold beams now use `woodiron_pole` / `woodiron_beam`, with 4m columns built from stacked 2m vertical segments.
- **Scaffold floor height is now configurable in validated 2m steps:** `ScaffoldingFloorHeight` now allows `2`, `4`, or `6` metres between scaffold levels so wood-iron support segments stack cleanly.
- **Internal scaffold support simplified:** extra interior vertical support poles were removed and replaced with a single center support column, while transverse and longitudinal beams remain enabled.
- **Top scaffold deck can use roof tiles:** when scaffold floors are enabled, the topmost full deck tiles now use `wood_roof_top` instead of floor pieces.
- **Initial build offset is now automatic:** startup placement now computes the player-to-build-center stand-off from the plan footprint plus the outer perimeter delta, while `BuildOriginForwardOffset` is now only an optional extra clearance.
- **External wall height range increased:** `ExternalWallHeight` can now be configured up to `18` levels.
- **Added `DisableWelcomePost` config option:** center welcome signage can now be disabled when it is not wanted.

## 1.0.8

- **F8 placement pivot moved to plan center:** preview/build placement now uses the plan center as the user-facing pivot (instead of corner-origin), making rotation and alignment more intuitive.
- **New `ScaffoldingLevels` option (1-3):** when roof scaffolding is enabled, scaffold generation now repeats the full vertical/perimeter/transverse/longitudinal pattern per level at +4m increments.
- **New `ScaffoldingFloors` option:** when roof scaffolding is enabled, wood floor decks can now be toggled on or off for scaffolding levels. Default is `false`.
- **Undo now includes elevated scaffold stories:** F9 piece selection/removal/highlighting now use horizontal (XZ) radius instead of 3D distance, so upper levels inside the undo circle are not missed.

## 1.0.7

- **Rotation preview/build flow hardened for manual placement:** preview rotation now stays aligned to safe 22.5° increments, and confirmed builds snap back onto a configurable safe grid so Valheim's follow-up piece snapping remains usable.
- **Rotation settings simplified:** `BuildRotationSnapDegrees`, `RotateStepDegrees`, and `FineRotateStepDegrees` are now restricted to `22.5`, `45`, or `90` degrees to prevent broken off-axis combinations.
- **Fine rotation now works correctly:** holding the fine-adjust modifier applies the configured fine rotation step during preview instead of being immediately cancelled by coarse snap logic.
- **Preview rotation bindings updated:** default preview keys are now `Q` rotate left, `G` rotate right, `E` confirm, `Esc` cancel, with `LeftShift` as the fine-adjust modifier.
- **New interior roof-scaffolding beam options:** added `TransverseScaffoldingBeams` and `LongitudinalScaffoldingBeams` for horizontal beam runs between interior scaffold poles.


## 1.0.6

- **Build orientation now follows camera view:** The plan is oriented based on the game camera's facing direction rather than the character's body rotation, so the origin is always at the bottom-left of the player's screen view regardless of how the character model is facing.
- **New Roof Scaffolding option:** When enabled, automatically places wooden poles at plan corners and door jambs (with gap-fill poles on long spans), connected by horizontal log beams along all four edges, to guide roof placement. Disabled by default; toggle via the `RoofScaffolding` config option.
- **File-not-found error feedback:** If a `.vfp` file cannot be loaded (preview or direct build), an on-screen centre message now explains the failure instead of silently doing nothing.

## 1.0.5

- **Undo confirmation circle is now movable:** During the undo confirmation window, press arrow keys (configurable preview move keys) to move the search circle centre and reselect which pieces to target for removal.
- Arrow key movement in undo confirmation respects camera angle (like preview mode) and supports fine-adjust modifier (default `LeftShift`) for precise positioning.
- All undo adjustments (radius or circle centre movement) now restart the 5-second confirmation timer, giving the player a full window after each change.
- Updated undo confirmation HUD message to indicate "Arrow keys to move circle center" as a control hint.
- Documentation: correction to Desiger path
- New user guide and example images

## 1.0.4

- Replaced the flat X origin marker with a tall vertical flagpole (10 m, bright yellow) so the build origin is visible above terrain, water, and underground surfaces during preview.
- Undo confirmation now shows per-piece red highlight rings around every VFP piece within the undo radius so the player can see exactly what will be removed.
- Undo confirmation now shows an orange boundary circle on the terrain at the full undo search radius edge.
- Reduced the default undo search radius from 75 m to 15 m.
- Added `UndoRadius` config option (range 5–150 m, default 15 m) to control the undo search radius.
- During the undo confirmation window, pressing `+`/`-` (or numpad equivalents) adjusts the radius by 5 m; the new value is saved to config and highlights/boundary circle refresh immediately.
- Pressing RMB or Escape during the undo confirmation window cancels the undo and clears all highlights.
- Undo confirmation HUD message now shows the current radius, `+/-` adjustment hint, and RMB/Esc cancel reminder.
- Added `ValheimFloorPlanPlugin.Instance` static property and `SetUndoRadius()` helper to support live config write-back from `FloorPlanBuilder`.

## 1.0.3

- Bumped mod, manifest, and Designer app version numbers from 1.0.2 to 1.0.3.
- Updated Thunderstore dependency to `denikson-BepInExPack_Valheim-5.4.2333`.
- Added a comprehensive README Config Options section documenting all BepInEx settings, defaults, ranges, and preview keybinds.
- Updated README callout formatting by replacing blockquote notes (`>`) with plain bold "Note/IMPORTANT" lines for better Thunderstore dark-theme readability.
- Rebuilt and repackaged Thunderstore release (`ValheimFloorPlan-1.0.3.zip`) including mod DLL + Designer app.

## 1.0.2

- Expanded the README package description/introduction for clearer context and feature overview.
- Added additional README notes for in-game build placement controls (preview movement/rotation/confirm/cancel keys).
- Updated README with a new **Examples** section using three new screenshots from `images/`.
- Fixed Designer `Shell` layout generation so doorways are placed first and walls no longer overlap doorway footprints.
- Improved Shell edge placement behavior on odd-sized grids by skipping wall segments that intersect doorway area.
- Rebuilt and repackaged Thunderstore release (`ValheimFloorPlan-1.0.2.zip`) including mod DLL + Designer app.

## 1.0.1

- Documentation change only; fixed broken README image links.

## 1.0.0

- First stable release.
- Added configurable terrain target offset: `TerrainHighPointDelta` (`Highest + Delta`, `0.0` to `4.0`).
- Preview walls and risk markers now reflect adjusted target height.
- Undo confirmation feedback now appears immediately on first key press.
- Finalized stable plugin GUID: `com.alexdroz.valheimfloorplan`.
- Added and documented partner Designer app workflow.
- Included Designer app in Thunderstore package contents.
- Development used Visual Studio Code, GitHub Copilot, and various auto-selected AI models.

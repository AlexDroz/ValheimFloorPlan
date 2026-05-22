# Changelog

## Unreleased

- **Scaffolding beam options now auto-enable for multi-level scaffolds:** when `ScaffoldingLevels` is set above `1`, `TransverseScaffoldingBeams` and `LongitudinalScaffoldingBeams` are automatically forced to `true` and written back to config so the saved settings match the required build behavior.
- **External wall height now follows scaffold capacity:** `ExternalWallHeight` is now capped at `ScaffoldingLevels x ScaffoldingFloorHeight`, and oversized values are clamped back to the maximum allowed by the current scaffold setup.
- **Scaffold levels can now use different heights:** added `ScaffoldingFloorHeight#2` and `ScaffoldingFloorHeight#3` so each scaffold story can use its own validated height, and `ExternalWallHeight` now caps against the sum of the active scaffold floor heights.
- **Hearths now cut scaffold floor openings and vent through wood chimneys:** scaffold decks leave open space above Hearth footprints, and a wood chimney stack starts about 3m above the Hearth so the fire remains accessible while smoke can exit above the top scaffold level.
- **Top scaffold levels now build as gable roofs:** when scaffold floors are enabled, the uppermost scaffold level now uses a pitched gable roof layout instead of a flat top deck.

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

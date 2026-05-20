# Changelog

## Unreleased

- Placeholder: **F8 placement pivot moved to plan center:** preview/build placement now uses the plan center as the user-facing pivot (instead of corner-origin), making rotation and alignment more intuitive.
- Placeholder: **Preview marker and controls wording updated:** in-game preview messaging now reports center movement, and technical logs include both center and internal placement anchor for easier troubleshooting.
- Placeholder: **Documentation synced to center-pivot behavior:** README, user guide, and implementation notes now describe center-based placement/rotation and the updated meaning of `BuildOriginForwardOffset`.
- Placeholder: **New `ScaffoldingLevels` option (1-6):** when roof scaffolding is enabled, scaffold generation now repeats the full vertical/perimeter/transverse/longitudinal pattern per level at +4m increments.
- Placeholder: **Undo now includes elevated scaffold stories:** F9 piece selection/removal/highlighting now use horizontal (XZ) radius instead of 3D distance, so upper levels inside the undo circle are not missed.

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

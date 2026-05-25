# ValheimFloorPlan Draft Notes

These are provisional notes for the next release and focus on workstation support, Designer/build alignment, and placement cleanup after the center-pivot refactor.

## Highlights

- Added `Workbench` support in both the Designer and the Builder, including Designer rotation handling and in-game workstation placement.
- Added `Hearth` (`4x3`) and basic `Bed` (`2x4`) tools so those pieces can now be laid out directly in the Designer and built in-game.
- Build orientation now matches the Designer layout orientation more closely across preview, terrain leveling, and final piece placement.
- Fixed left/right mirrored placement issues so rotated walls, doors, and other directional pieces now face the expected way again.
- Corrected outer-edge wall and doorway offsets so pieces on the left and right perimeter sit on the outside edge of the tile instead of the inside edge.
- Fixed scaffold-only left/right drift so internal poles and scaffold floor levels now line up with the corrected build transform.
- Roof scaffolding now uses stronger wood-iron members for perimeter columns and scaffold beams, with each 4m column built from two joined 2m vertical pieces.
- `ScaffoldingFloorHeight` is now configurable with validated `2`, `4`, or `6` metre level spacing so scaffold decks and stacked wood-iron supports stay aligned.
- Internal vertical scaffold support is now simplified to a single center support column rather than multiple extra interior poles.
- Added `RoofScaffoldingType` (`Gable` / `Flat`) for the topmost scaffold level when `ScaffoldingFloors` is enabled.
- `RoofScaffoldingType=Flat` now uses ridge roof pieces with corrected non-overlapping edge-to-edge tiling density.
- `RoofScaffoldingType=Gable` now extends front-mid/back-mid/center scaffold supports up to the roof apex.
- Changed initial preview/build placement to auto-compute the player-to-build-center distance from the actual plan footprint plus the outer terrain-change perimeter. `BuildOriginForwardOffset` is now an optional extra offset rather than the full placement distance.
- Increased the `ExternalWallHeight` range to allow much taller stacked perimeter walls, up to `18` levels.
- Added a config option to disable the welcome post/signage after build completion.
- Improved workstation placement reliability by centering workstation prefabs within their allocated footprint and hardening the recenter logic for awkward crafting-station prefabs.

## Additional Notes

- Terrain leveling, preview bounds, and final placement now use the same corrected footprint alignment, reducing cases where terrain edits happened beside the selected build area.
- Placement math now relies more heavily on shared plan-to-world transform helpers, which should make future orientation and offset fixes less brittle.
- The automatic preview start distance now accounts for the outer leveled-area boundary, so the full selected area starts clear of the player more reliably even with zero extra offset.

## Previous 1.0.8 Notes

This release focuses on rotation safety, improved preview controls, and stronger roof-scaffolding behavior.

## Highlights

- Preview rotation now stays on safe increments, and confirmed builds snap back onto a configurable grid so Valheim's normal post-build snapping remains usable.
- Rotation settings are limited to `22.5`, `45`, or `90` degrees to prevent off-axis placement problems.
- Fine-adjust rotation now works during preview instead of being overridden by the final snap.
- Default preview rotation keys are now `Q` for rotate left and `G` for rotate right. Confirm remains `E`, cancel remains `Esc`, and `LeftShift` still enables fine adjust.
- Added optional interior scaffold beam runs with `TransverseScaffoldingBeams` and `LongitudinalScaffoldingBeams`.
- Fixed rotated scaffolding beam pairing so beam runs place correctly even when the footprint is previewed at a non-zero angle.
## Configuration Notes

- Rotation settings live in the `Preview - Rotation` config section.
- Rotation-related defaults are now:
  - `BuildRotationSnapDegrees = 90`
  - `RotateStepDegrees = 90`
  - `FineRotateStepDegrees = 22.5`
- Allowed values for all three rotation settings are now:
  - `22.5`
  - `45`
  - `90`
- Updated default preview keys:
  - `RotateLeftKey = Q`
  - `RotateRightKey = G`
  - `ConfirmKey = E`
  - `FineAdjustKey = LeftShift`

## Player Impact

- If a plan is previewed at a fine angle, the final build still lands on the configured snap grid.
- This avoids the previous situation where a completed structure could be built at an awkward angle that made later manual hammer placement difficult.
- Preview fine rotation remains available so you can inspect alignment before committing the snapped final build.

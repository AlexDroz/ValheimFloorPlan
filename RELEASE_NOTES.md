# ValheimFloorPlan 1.0.8

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

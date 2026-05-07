# ValheimFloorPlan 1.0.0

This release marks the first stable milestone for ValheimFloorPlan.

## Highlights

- Terrain leveling target now supports a configurable high-point offset:
  - `TerrainHighPointDelta` (range `0.0` to `4.0`, default `0.0`)
  - Effective target is `HighestPoint + Delta`
- Preview visuals now account for the adjusted terrain target height.
- Undo/restore confirmation now shows immediately on first key press (no initial HUD delay).
- Terrain-leveling and preview behavior/documentation updated for consistency.

## Configuration Notes

- New/updated terrain setting:
  - `TerrainHighPointDelta = 0.0` (default)
- Existing defaults remain tuned for balanced behavior:
  - `TerrainLevelPasses = 2`
  - `TerrainSpikeCleanupPasses = 2`
  - `TerrainStampRadius = 3.0`

## Designer Companion App

- Repository now includes the partner Designer web app under `Designer/` for creating `.vfp` plans.
- Added monorepo workflow docs:
  - `README.md` (project layout and end-to-end flow)
  - `DESIGNER_WORKFLOW.md` (Designer -> Mod usage)
- Added VS Code task support for both sides of the workflow:
  - `Build & Deploy ValheimFloorPlan`
  - `Run Designer Local Server`

## Pre-release Sanity Checklist

- Build succeeds locally with zero compile errors.
- Mod loads in-game and logs `ValheimFloorPlan v1.0.0 loaded!`.
- Preview can move/rotate/cancel/confirm with configured keys.
- Terrain leveling uses `Highest + Delta` as configured.
- Preview top walls/markers visually reflect delta height.
- Undo confirmation appears immediately on first key press.
- Undo second press performs piece removal and terrain snapshot restore.
- User guides/config docs reflect current settings.

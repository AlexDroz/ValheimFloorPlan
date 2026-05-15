# Changelog

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

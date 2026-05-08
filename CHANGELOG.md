# Changelog

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

# ValheimFloorPlan Monorepo

This repository contains two related projects:

1. ValheimFloorPlan mod (BepInEx C# plugin): consumes `.vfp` plans in-game and builds them.
2. Designer web app: creates and edits `.vfp` plans for the mod.

## Project Layout

- `ValheimFloorPlanPlugin.cs`, `FloorPlanBuilder.cs`, `TerrainLeveler.cs`, and other C# files: game mod source.
- `Designer/`: browser-based floor plan Designer.
  - `Designer/index.html`
  - `Designer/app.js`
  - `Designer/Plans/`
- `samples/`: example `.vfp` files you can load directly in the mod.

## End-to-End Workflow

1. Open the Designer and create or edit a plan.
2. Save/export a `.vfp` file.
3. Point the mod config `FloorPlanFile` to that `.vfp` file.
4. In game, press build hotkey (default F8) and place the design.

Detailed guide: see `DESIGNER_WORKFLOW.md`.

## Sample Plans

The `samples/` folder contains ready-to-use `.vfp` examples:

- `samples/myfloorplan4x4.vfp`
- `samples/myfloorplan8x8.vfp`
- `samples/myfloorplan12x12.vfp`
- `samples/myfloorplan16x16.vfp`
- `samples/myfloorplan20x20.vfp`
- `samples/myfloorplan24x24.vfp`
- `samples/myfloorplan28x28.vfp`

## Screenshots

![Valheim Floor Plan screenshot 1](images/2026-05-07%2015_51_47-Valheim.png)
![Valheim Floor Plan screenshot 2](images/2026-05-07%2015_52_16-Valheim.png)
![Valheim Floor Plan screenshot 3](images/2026-05-07%2015_52_28-Valheim.png)
![Valheim Floor Plan screenshot 4](images/2026-05-07%2015_53_00-Valheim.png)
![Valheim Floor Plan screenshot 5](images/2026-05-07%2015_53_17-Valheim.png)
![Valheim Floor Plan screenshot 6](images/2026-05-07%2015_53_49-Valheim.png)
![Valheim Floor Plan screenshot 7](images/2026-05-07%2015_54_06-Valheim.png)
![Valheim Floor Plan screenshot 8](images/2026-05-07%2016_14_04-.png)
![Valheim Floor Plan screenshot 9](images/2026-05-07%2016_14_27-.png)

## VS Code Tasks

The workspace includes tasks for both projects:

1. Build and deploy mod:
   - `Build & Deploy ValheimFloorPlan`
2. Run Designer local server:
   - `Run Designer Local Server`
3. Package for Thunderstore (includes mod + Designer):
   - `Package Thunderstore (Mod + Designer)`

The Designer task launches `Designer/StartLocalhost.bat`, which opens the app on localhost.

## Thunderstore Packaging

This repo includes Thunderstore metadata and a packaging script:

- `manifest.json`
- `CHANGELOG.md`
- `scripts/Create-ThunderstorePackage.ps1`

The package task/script creates:

- `artifacts/thunderstore/ValheimFloorPlan-1.0.0.zip`

Package contents include:

- `BepInEx/plugins/ValheimFloorPlan/ValheimFloorPlan.dll`
- `BepInEx/plugins/ValheimFloorPlan/Designer/...`
- `images/...` (used by `README.md` screenshot links)
- `samples/...` (example `.vfp` plans)

Before packaging, place a valid `icon.png` (256x256) at the repository root.

## Notes

- `.vfp` is the contract between the Designer and the mod.
- Keep the format stable when changing either project.
- If you evolve the format, update both parser and exporter together.

## Acknowledgements

Development of the Valheim Floor Plan mod and the Valheim Floor Plan Designer web app used:

- Visual Studio Code
- GitHub Copilot
- Various auto-selected AI models during implementation and refinement

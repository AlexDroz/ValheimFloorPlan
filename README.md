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

## End-to-End Workflow

1. Open the Designer and create or edit a plan.
2. Save/export a `.vfp` file.
3. Point the mod config `FloorPlanFile` to that `.vfp` file.
4. In game, press build hotkey (default F8) and place the design.

Detailed guide: see `DESIGNER_WORKFLOW.md`.

## VS Code Tasks

The workspace includes tasks for both projects:

1. Build and deploy mod:
   - `Build & Deploy ValheimFloorPlan`
2. Run Designer local server:
   - `Run Designer Local Server`

The Designer task launches `Designer/StartLocalhost.bat`, which opens the app on localhost.

## Notes

- `.vfp` is the contract between the Designer and the mod.
- Keep the format stable when changing either project.
- If you evolve the format, update both parser and exporter together.

# Valheim Floor Plan

Build structures in Valheim from pre-designed floor plans.

This package includes two components:

1. **ValheimFloorPlan mod** — a BepInEx plugin that reads `.vfp` plan files and builds them in-game.
2. **Valheim Floor Plan Designer** — a browser-based app (included) for creating and editing `.vfp` plan files.

**Included in this package:**
- The mod DLL (installed automatically under `BepInEx/plugins`)
- The Designer web app (`BepInEx/plugins/ValheimFloorPlan/Designer/`)
- Sample `.vfp` plans to get started immediately

## Creating Floor Plans

1. Open the Designer and create or edit a plan.
2. Save/export a `.vfp` file.
3. Point the mod config `FloorPlanFile` to that `.vfp` file.
4. In game, press the build hotkey (default F8) and place the design. A terrain snapshot is taken automatically before placement.
5. To undo, press the undo hotkey (default F9). This removes all placed building pieces and restores the terrain to the snapshot.

> **IMPORTANT:** Terrain can only be restored within the **current session** — if you leave the area or reload, the terrain snapshot is lost. Building pieces can be undone across sessions because they are tagged as built by Valheim Floor Plan.


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

## Package contents include:

- `BepInEx/plugins/ValheimFloorPlan/ValheimFloorPlan.dll`
- `BepInEx/plugins/ValheimFloorPlan/Designer/...`
- `images/...` (used by `README.md` screenshot links)
- `samples/...` (example `.vfp` plans)

## Notes

- `.vfp` is the contract between the Designer and the mod.
- Keep the format stable when changing either project.
- If you evolve the format, update both parser and exporter together.

## Acknowledgements

Development of the Valheim Floor Plan mod and the Valheim Floor Plan Designer web app used:

- Visual Studio Code
- GitHub Copilot
- Various auto-selected AI models during implementation and refinement

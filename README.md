# Valheim Floor Plan

Build structures in Valheim from pre-designed floor plans.

Anyone who has spent time building in Valheim knows the real work starts before the first wall goes up — clearing uneven ground, wrestling with terrain, nudging pieces into alignment one agonising centimetre at a time. **Valheim Floor Plan** takes that grind away. Design your layout once in the included browser-based Designer, save it as a `.vfp` plan file, and the mod handles the rest: it levels the terrain, snaps a snapshot for easy undo, and constructs your entire foundation in seconds. No more tedious ground prep or painstaking piece-by-piece placement — just load your plan, pick your spot, and build.

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

   While the preview is active, use these keys to adjust placement:

   | Key | Action |
   |-----|--------|
   | `↑` `↓` `←` `→` | Move the plan forward / backward / left / right |
   | `Q` | Rotate left (45°) |
   | `R` | Rotate right (45°) |
   | `Left Shift` *(hold)* | Fine-adjust mode — smaller move/rotate steps |
   | `E` | Confirm and build at current position |
   | `RMB` / `Escape` | Cancel placement |

   > All keys are configurable in the BepInEx config file.

   While positioning the plan, watch for these visual markers in the world:

   - **White rectangle** — the inner leveled pad: the exact area of ground that will be raised and flattened to sit your foundation on.
   - **Green rectangle** — the outer terrain-change boundary: terrain blending extends to this edge, giving a smooth transition rather than a hard cliff.
   - **Dark red/brown cross marker** — the exact placement origin point at the centre of the plan.
   - **Orange diamond markers** — terrain edge risk warnings. These appear when the surrounding terrain is uneven enough that the leveled edge may produce visible tears or spikes. Move or rotate the plan until they disappear (or reduce) for the cleanest result. Markers turn **red** when risk is high.

   > A HUD message also reports the current risk level (`LOW` / `MEDIUM` / `HIGH`) along with `step` (the steepest cross-edge height jump) and `relief` (total height range around the footprint) to help you judge whether to nudge the plan before building.

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

## Examples

![Valheim Floor Plan example 1](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-08%2013_40_47-Valheim.png)
![Valheim Floor Plan example 2](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-08%2013_42_20-Valheim.png)
![Valheim Floor Plan example 3](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-08%2013_43_13-Valheim.png)

## Screenshots

![Valheim Floor Plan screenshot 1](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_51_47-Valheim.png)
![Valheim Floor Plan screenshot 2](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_52_16-Valheim.png)
![Valheim Floor Plan screenshot 3](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_52_28-Valheim.png)
![Valheim Floor Plan screenshot 4](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_53_00-Valheim.png)
![Valheim Floor Plan screenshot 5](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_53_17-Valheim.png)
![Valheim Floor Plan screenshot 6](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_53_49-Valheim.png)
![Valheim Floor Plan screenshot 7](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2015_54_06-Valheim.png)
![Valheim Floor Plan screenshot 8](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2016_14_04-.png)
![Valheim Floor Plan screenshot 9](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-07%2016_14_27-.png)

## Package contents include:

- `BepInEx/plugins/ValheimFloorPlan/ValheimFloorPlan.dll`
- `BepInEx/plugins/ValheimFloorPlan/Designer/...`
- `images/...` (included in the package archive)
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

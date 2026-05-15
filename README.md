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

1. Open the **Designer** to create or edit a plan. It is a local web page installed by Thunderstore Mod Manager. Copy the path below and paste it into your browser address bar to open it (replace `v1.0.3` if a newer version is installed):

   ```
   %APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\cache\RetiredCoders-ValheimFloorPlan\v1.0.3\BepInEx\plugins\ValheimFloorPlan\Designer\index.html

   e.g. C:\Users\{Username}\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\RetiredCoders-ValheimFloorPlan\v1.0.3\BepInEx\plugins\ValheimFloorPlan\Designer\index.html
   ```
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

   **Note:** All keys are configurable in the BepInEx config file.

   While positioning the plan, watch for these visual markers in the world:

   - **White rectangle** — the inner leveled pad: the exact area of ground that will be raised and flattened to sit your foundation on.
   - **Green rectangle** — the outer terrain-change boundary: terrain blending extends to this edge, giving a smooth transition rather than a hard cliff.
   - **Tall yellow flagpole** — the exact placement origin point at the centre of the plan. The pole rises 10 m above the terrain surface so it remains visible even when the ground is underwater or underground.
   - **Orange diamond markers** — terrain edge risk warnings. These appear when the surrounding terrain is uneven enough that the leveled edge may produce visible tears or spikes. Move or rotate the plan until they disappear (or reduce) for the cleanest result. Markers turn **red** when risk is high.

   **Note:** A HUD message also reports the current risk level (`LOW` / `MEDIUM` / `HIGH`) along with `step` (the steepest cross-edge height jump) and `relief` (total height range around the footprint) to help you judge whether to nudge the plan before building.

5. To undo, press the undo hotkey (default F9). A 5-second confirmation window opens showing:
   - **Red rings** around every VFP piece within the search radius so you can see exactly what will be removed.
   - **Orange boundary circle** on the ground marking the edge of the search radius.

   During the confirmation window you can:

   | Key | Action |
   |-----|--------|
   | `+` / `-` *(or numpad)* | Increase / decrease the search radius by 5 m. The new radius is saved to config. |
   | `F9` *(undo hotkey again)* | Confirm — removes all marked pieces and restores terrain. |
   | `RMB` / `Escape` | Cancel — clears all highlights without removing anything. |

   The HUD message shows the current radius and remaining time throughout the window.

**IMPORTANT:** Terrain can only be restored within the **current session** — if you leave the area or reload, the terrain snapshot is lost. Building pieces can be undone across sessions because they are tagged as built by Valheim Floor Plan.

## Config Options

Config file path:

- `BepInEx/config/com.alexdroz.valheimfloorplan.cfg`

All values below are configurable in that file.

### General

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `FloorPlanFile` | *(empty)* | Any valid file path | Full path to the `.vfp` file exported by the Designer. |
| `BuildHotkey` | `F8` | Any valid `KeyboardShortcut` | Starts plan preview/build flow. |
| `UndoHotkey` | `F9` | Any valid `KeyboardShortcut` | Removes placed VFP-tagged pieces and restores terrain snapshot. |
| `UndoRadius` | `15` | `5` to `150` | Search radius in metres around the player when scanning for VFP pieces to remove on Undo. Adjustable live during the confirmation window with `+`/`-`. |
| `ProgressMessagePosition` | `CenterLeft` | Valheim `MessageHud` positions (`Center`, `TopLeft`, `TopRight`, etc.) | HUD slot for build-progress messages. `CenterLeft` is accepted as an alias and maps to `Center`. |
| `WarningMessagePosition` | `TopLeft` | Valheim `MessageHud` positions (`Center`, `TopLeft`, `TopRight`, etc.) | HUD slot for warnings/risk messages. `CenterLeft` is accepted as an alias and maps to `Center`. |
| `BuildOriginForwardOffset` | `12` | `10` to `20` | Initial preview origin distance in front of the player (meters). |

### Terrain

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `TerrainLevelPasses` | `2` | `1` to `5` | Main leveling pass count before spike cleanup. Lower is faster; higher can smooth stubborn terrain. |
| `TerrainSpikeCleanupPasses` | `2` | `1` to `5` | Post-leveling spike cleanup pass count. |
| `TerrainStampRadius` | `3.0` | `3.0` to `6.0` | Radius of each terrain stamp disc (meters). Also controls outer terrain blend reach. |
| `TerrainHighPointDelta` | `0.0` | `0.0` to `4.0` | Extra height added to sampled highest point (`targetY = highest + delta`). |
| `TerrainUseStagedRaise` | `false` | `true` / `false` | Experimental staged vertical raise mode instead of single full-height raise. |
| `TerrainRaiseStepHeight` | `0.5` | `0.15` to `1.5` | Max vertical raise per stage when staged raise is enabled (meters). |
| `TerrainMaxRaiseStages` | `1` | `1` to `16` | Hard cap on number of staged raises when staged mode is enabled. |
| `TerrainSkipSatisfiedCenterStamps` | `true` | `true` / `false` | Skips center stamps where sampled terrain is already at/above target height. |

### Building

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `ExternalWallHeight` | `1` | `1` to `4` | Stacks outer-perimeter `Wall`/`Pillar` pieces vertically to this many levels. |
| `WallPillarMaterial` | `Stone` | `Stone`, `Wood` | Chooses material set used for `Wall` and `Pillar` types. |

### Preview Movement/Rotation

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `MoveStep` | `2.0` | `0.25` to `10.0` | Nudge distance per move key press (meters). |
| `FineMoveStep` | `0.5` | `0.05` to `5.0` | Nudge distance per move key press while fine-adjust key is held (meters). |
| `RotateStepDegrees` | `15` | `1` to `90` | Rotation applied per rotate key press (degrees). |
| `FineRotateStepDegrees` | `5` | `1` to `45` | Rotation applied per rotate key press while fine-adjust key is held (degrees). |

### Preview Keys

| Option | Default | What it does |
|---|---|---|
| `MoveForwardKey` | `UpArrow` | Move preview origin forward relative to camera. |
| `MoveBackwardKey` | `DownArrow` | Move preview origin backward relative to camera. |
| `MoveLeftKey` | `LeftArrow` | Move preview origin left relative to camera. |
| `MoveRightKey` | `RightArrow` | Move preview origin right relative to camera. |
| `RotateLeftKey` | `Q` | Rotate preview counter-clockwise. |
| `RotateRightKey` | `R` | Rotate preview clockwise. |
| `ConfirmKey` | `E` | Confirm current preview placement and start build. |
| `CancelKey` | `Escape` | Cancel preview. (Right-click cancels too.) |
| `FineAdjustKey` | `LeftShift` | Hold for fine movement and fine rotation. |


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
![Valheim Floor Plan screenshot 7](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/2026-05-15%2016_45_51-Valheim.png)
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

# Valheim Floor Plan

Build complete multi-level structures in Valheim from pre-designed floor plans.

Anyone who has spent time building in Valheim knows the real effort starts before the first wall goes up — clearing uneven ground, wrestling with terrain spikes, and nudging pieces into alignment one agonising centimetre at a time. **Valheim Floor Plan** takes all of that away. Design your layout in the included browser-based Designer, save it as a `.vfp` plan file, and the mod handles the rest: it levels and blends the terrain, captures a snapshot for instant undo, and places the entire foundation in seconds.

The Designer supports up to three in-memory level layouts with quick switching, per-level overlay rendering, and cross-level clash hints so you can plan multi-storey builds before committing a single piece. Orientation-sensitive tools — including Beds, Workbenches, and Staircases — show directional arrows so what you see in the Designer matches exactly what gets built in-game. The **FlexiWall** tool lets you draw curved stone walls of any shape — arcs, sweeping curves, and full circles — that are not constrained to the rectangular grid.

Excel-style row/column reference labels run along all four edges of the grid to help with precise placement, and furniture pieces (`Bed`, `Staircase`, `Hearth`, `Workbench`) show a small centre handle once placed — drag it to move the piece, click it to select it, then rotate in place with the arrow keys or mouse wheel. Right-click and `Escape` simply cancel whatever's active (tool, selection, or in-progress draw) without deleting anything; to remove a piece, hover it and press `Delete`/`Backspace`.

Once you're happy with the design, press the build hotkey, position the preview in-world, and confirm. The mod levels the terrain, places every piece in the right order, and handles scaffold framing for upper floors automatically. For multi-level builds, additional red floating rectangles appear in the preview at each upper-floor height so you can see the full vertical footprint before committing. If something is off, undo restores both pieces and terrain in one keypress — or use the keep-terrain undo (`Ctrl+F9`) to remove pieces while leaving the leveled ground intact.

This package includes two components:

1. **ValheimFloorPlan mod** — a BepInEx plugin that reads `.vfp` plan files and builds them in-game.
2. **Valheim Floor Plan Designer** — a browser-based app (included) for creating and editing `.vfp` plan files.

**Demo video:** https://youtu.be/jz39KSSfhJ0

**Included in this package:**
- The mod DLL (installed automatically under `BepInEx/plugins`)
- The Designer web app (`BepInEx/plugins/ValheimFloorPlan/Designer/`)
- Sample `.vfp` plans to get started immediately

## Creating Floor Plans

1. Open the **Designer** to create or edit a plan. It is a local web page installed by Thunderstore Mod Manager. Copy the path below and paste it into your browser address bar to open it :

   ```
   %APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\plugins\RetiredCoders-ValheimFloorPlan\ValheimFloorPlan\Designer\index.html

   e.g.    C:\Users\{Username}\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\plugins\RetiredCoders-ValheimFloorPlan\ValheimFloorPlan\Designer\index.html
   ```
2. Save/export a `.vfp` file.
3. Point the mod config `FloorPlanFile` to that `.vfp` file.
4. In game, press the build hotkey (default F8) and place the design. A terrain snapshot is taken automatically before placement.

   While the preview is active, use these keys to adjust placement:

   | Key | Action |
   |-----|--------|
   | `↑` `↓` `←` `→` | Move the plan forward / backward / left / right |
   | `Q` | Rotate left (default coarse step: 90°) |
   | `G` | Rotate right (default coarse step: 90°) |
   | `Left Shift` *(hold)* | Fine-adjust mode — smaller move/rotate steps |
   | `E` | Confirm and build at current position |
   | `RMB` / `Escape` | Cancel placement |

   **Note:** All keys are configurable in the BepInEx config file.

   **Rotation note:** confirmed builds snap to `BuildRotationSnapDegrees` (default `90°`) so the finished structure stays aligned to angles that still work with Valheim's normal manual piece snapping. Fine preview rotation uses `22.5°` steps by default.

   The rotation-related config entries live together in the `Preview - Rotation` section.

   While positioning the plan, watch for these visual markers in the world:

   - **White rectangle** — the inner leveled pad: the exact area of ground that will be raised and flattened to sit your foundation on.
   - **Green rectangle** — the outer terrain-change boundary: terrain blending extends to this edge, giving a smooth transition rather than a hard cliff.
   - **Red floating rectangle(s)** — upper-floor level bands (multi-level builds only). One red band appears per upper floor at its expected scaffold height, so you can see the full vertical footprint before confirming. Only shown when `FloorPlanLevels` is `2` or `3`.
   - **Tall yellow flagpole** — the exact placement origin point at the centre of the plan. The pole rises 10 m above the terrain surface so it remains visible even when the ground is underwater or underground.
   - **Orange diamond markers** — terrain edge risk warnings. These appear when the surrounding terrain is uneven enough that the leveled edge may produce visible tears or spikes. Move or rotate the plan until they disappear (or reduce) for the cleanest result. Markers turn **red** when risk is high.

   **Note:** A HUD message also reports the current risk level (`LOW` / `MEDIUM` / `HIGH`) along with `step` (the steepest cross-edge height jump) and `relief` (total height range around the footprint) to help you judge whether to nudge the plan before building.

5. To undo, press the undo hotkey (default F9). A 5-second confirmation window opens showing:
   - **Red rings** around every VFP piece within the search radius so you can see exactly what will be removed.
   - **Orange boundary circle** on the ground marking the edge of the search radius.

   Optional mode: press **Ctrl+F9** (configurable via `UndoKeepTerrainHotkey`) to start piece-only undo that keeps the leveled terrain. In this mode the terrain snapshot is discarded instead of restored.

   During the confirmation window you can:

   | Key | Action |
   |-----|--------|
   | `↑` `↓` `←` `→` | Move the search circle centre to target different pieces. Movement respects camera angle. Hold `Left Shift` for fine movement. |
   | `+` / `-` *(or numpad)* | Increase / decrease the search radius by 5 m. The new radius is saved to config. |
   | `F9` *(undo hotkey again)* | Confirm normal undo — removes all marked pieces and restores terrain. |
   | `Ctrl+F9` *(UndoKeepTerrainHotkey)* | Start or confirm keep-terrain undo — removes marked pieces and discards the current terrain snapshot. If started with `Ctrl+F9`, confirmation can be done with either `Ctrl+F9` or `F9`. |
   | `RMB` / `Escape` | Cancel — clears all highlights without removing anything. |

   The HUD message shows the current radius and remaining time throughout the window. All adjustments (movement or radius) restart the 5-second confirmation timer.

**IMPORTANT:** Terrain can only be restored within the **current session** — if you leave the area or reload, the terrain snapshot is lost. Building pieces can be undone across sessions because they are tagged as built by Valheim Floor Plan.

**Tip — keep the leveled terrain:** if you want to remove your build but keep the flat ground (e.g. to iterate on a design), use **Ctrl+F9** (`UndoKeepTerrainHotkey`) instead of F9. Pieces are removed as normal, but the terrain snapshot is discarded rather than restored.

## FlexiWall Tool

The FlexiWall tool draws curved or straight walls along any path in the Designer. Walls are built in-game as stacked stone pieces following the arc, with the same brick-bond row offset as regular walls.

| Designer layout | In-game result |
|---|---|
| ![Shamrock layout in the Designer](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_6a.png) | ![Completed shamrock build in-game](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_6b.png) |


**FlexiWall Examples video:** https://youtu.be/N8CvcGpX-BQ

### Drawing a FlexiWall

1. Select the **FlexiWall** tool from the piece selector.
2. Click a **start cell** — a green dot appears to confirm the start position.
3. Click an **end cell** — a straight wall preview appears between the two cells.
4. Drag the **midpoint handle** (white circle) to bend the wall into a curve. The arc updates live as you drag.

To remove a placed FlexiWall, click its midpoint handle to select it (shown with a dashed outline, just like furniture pieces), then press `Delete` or `Backspace` — you don't need the FlexiWall tool active to do this.

### Endpoint snapping

FlexiWall endpoints snap to the cell-edge boundary that is tangential to the arc at that point, rather than to the cell centre. This means two FlexiWalls that share a cell boundary will meet at exactly the same world coordinate — no gap in the build.

Green and red dots on placed walls show the exact snapped start and end positions so you can see where each wall begins and ends.

### Closing a circle

To build a complete ring, place the start and end cells adjacent to each other (one cell apart), then drag the midpoint handle to the opposite side of the circle. The Designer will draw a closed ring and the builder will produce a full circle of wall pieces.

For very large circles, or if you want separate arcs with a doorway gap, you can use two FlexiWalls end-to-end.

### FlexiWalls in multi-level builds

FlexiWalls work in Level 1, Level 2, and Level 3 plan files. Upper-level FlexiWalls are placed at the correct scaffold-deck height alongside other upper-level pieces. The footprint validation and terrain levelling area both include FlexiWall extent, so a plan containing only FlexiWalls (no regular grid pieces) still produces a correct build preview and levelled pad.

## Preset Bundle Workflow (In Game)

Preset bundles let you share/reload a complete setup: Level 1/2/3 `.vfp` files plus non-key build/config values.

- Bundle format: `.vpfset` (zip container)
- Bundle output folder: `BepInEx/plugins/RetiredCoders-ValheimFloorPlan/ValheimFloorPlan/PresetBundles/` by default. If `FloorPlanDirectory` is set, bundles are exported to and imported from that folder instead.
- Key mappings are intentionally **not** included in exported/imported bundle settings.

**Note:** when a bundle is imported, the extracted `.vfp` files are written into a subfolder named after the bundle inside the bundle output folder, and `FloorPlanFile`/`FloorPlanFileLevel2`/`FloorPlanFileLevel3` are updated to those full extracted paths. If you are using `FloorPlanDirectory`, you may want to move the extracted files into your directory and update the file fields to bare filenames afterwards.

Default controls:

| Key | Action |
|---|---|
| `Ctrl+F8` (`ExportBundleHotkey`) | Export current setup as a new timestamped bundle named `<BundleName>-YYYYMMDD-HHMMSS.vpfset`. |
| `Alt+F8` (`ImportBundleHotkey`) | Start timed import selection mode. |
| `Right Arrow` / `Left Arrow` | While import selection is active, move to next/previous bundle. |
| `Enter` | Import currently selected bundle. |
| `Escape` | Cancel import selection. |

While import selection is active, the HUD shows:

- Selected bundle name
- Current position as `bundle X of N`
- Control hints (`Right`, `Left`, `Enter`, `Escape`)
- Remaining time

If the timer expires, import selection is canceled automatically.

After a successful import, `BundleName` is automatically normalized to the imported preset prefix (timestamp suffix removed).

## How Multi-Level Builds Work (Plain Language)

When `FloorPlanLevels` is set to `2` or `3`, the mod builds one level at a time.

For each upper level, it follows this order:

1. Place route-critical pieces first (staircases and hearths).
2. Cut/preserve needed vertical paths (stair shafts and chimney shafts).
3. Place the rest of that level's pieces around those paths.

This helps avoid the "nothing gets built" situation where pieces can block each other if placed in the wrong order.

### What gets blocked

- Higher-level pieces are blocked from occupying protected stair/chimney shaft space.
- Clash checks are primarily local to the current level, but shaft routes are kept clear across levels.
- Upper-level duplicate floor tiles from plan files are skipped where scaffold decks already provide flooring.

### If something is skipped

- You get HUD/log warning categories that explain why.
- If a level has clashes, the build creates a clash signpost for that level with detail signs (plus log details for overflow).

## Config Options

Config file path:

- `BepInEx/config/com.alexdroz.valheimfloorplan.cfg`

All values below are configurable in that file.

### General

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `FloorPlanDirectory` | *(empty)* | Any valid folder path | Optional base folder for `.vfp` files and preset bundles. When set, `FloorPlanFile`, `FloorPlanFileLevel2`, and `FloorPlanFileLevel3` can be bare filenames instead of full paths, and bundle export/import uses this folder instead of the default `PresetBundles` subfolder. Existing configs with full absolute paths in the file fields continue to work unchanged. |
| `FloorPlanFile` | *(empty)* | File path or bare filename | Path to the `.vfp` file exported by the Designer. A bare filename (e.g. `myhouse.vfp`) is sufficient when `FloorPlanDirectory` is set; otherwise provide the full path. |
| `FloorPlanLevels` | `1` | `1` to `3` | Number of layout levels to build. `1` uses only `FloorPlanFile`, `2` also uses `FloorPlanFileLevel2`, `3` also uses `FloorPlanFileLevel3`. Values above `1` enforce multi-level scaffolding requirements. |
| `FloorPlanFileLevel2` | *(empty)* | File path or bare filename | Optional Level 2 `.vfp` file. A bare filename is sufficient when `FloorPlanDirectory` is set. If set, its footprint must fit within the Level 1 (`FloorPlanFile`) footprint. |
| `FloorPlanFileLevel3` | *(empty)* | File path or bare filename | Optional Level 3 `.vfp` file. A bare filename is sufficient when `FloorPlanDirectory` is set. If set, its footprint must fit within the Level 1 (`FloorPlanFile`) footprint. |
| `BuildHotkey` | `F8` | Any valid `KeyboardShortcut` | Starts plan preview/build flow. |
| `UndoHotkey` | `F9` | Any valid `KeyboardShortcut` | Removes placed VFP-tagged pieces and restores terrain snapshot. |
| `UndoKeepTerrainHotkey` | `LeftControl + F9` | Any valid `KeyboardShortcut` | Starts piece-only undo mode: removes VFP pieces and discards the current terrain snapshot so leveled terrain remains. |
| `BundleName` | `MyBuild` | Any valid file-name text | Preset name prefix used when exporting bundles. Export file name format is `<BundleName>-YYYYMMDD-HHMMSS.vpfset`. After import, this is reset to the imported prefix (timestamp removed). |
| `ExportBundleHotkey` | `LeftControl + F8` | Any valid `KeyboardShortcut` | Exports a preset bundle to `PresetBundles` as `<BundleName>-YYYYMMDD-HHMMSS.vpfset` (layout files + non-key settings). |
| `ImportBundleHotkey` | `LeftAlt + F8` | Any valid `KeyboardShortcut` | Starts timed bundle import selection mode (`Right/Left` choose, `Enter` import, `Escape` cancel, auto-timeout cancel). |
| `UndoRadius` | `15` | `5` to `150` | Search radius in metres around the undo circle centre when scanning for VFP pieces to remove on Undo. Adjustable live during the confirmation window with `+`/`-`, and circle centre is adjustable with arrow keys. |
| `ProgressMessagePosition` | `CenterLeft` | Valheim `MessageHud` positions (`Center`, `TopLeft`, `TopRight`, etc.) | HUD slot for build-progress messages. `CenterLeft` is accepted as an alias and maps to `Center`. |
| `WarningMessagePosition` | `TopLeft` | Valheim `MessageHud` positions (`Center`, `TopLeft`, `TopRight`, etc.) | HUD slot for warnings/risk messages. `CenterLeft` is accepted as an alias and maps to `Center`. |

### Building

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `ExternalWallHeightLevel1` | `1` | `1` to `6` | Stacks Level 1 outer-perimeter `Wall`/`Pillar` pieces vertically. Dynamic cap: `ScaffoldingFloorHeight x 2`. |
| `ExternalWallHeightLevel2` | `1` | `1` to `6` | Stacks Level 2 outer-perimeter `Wall`/`Pillar` pieces vertically. Dynamic cap: `ScaffoldingFloorHeight#2 x 2`. |
| `ExternalWallHeightLevel3` | `1` | `1` to `6` | Stacks Level 3 outer-perimeter `Wall`/`Pillar` pieces vertically. Dynamic cap: `ScaffoldingFloorHeight#3 x 2`. |
| `InternalWallHeightLevel1` | `1` | `1` to `6` | Stacks Level 1 interior FlexiWall pieces vertically (walls whose control points do not touch the layout perimeter). |
| `InternalWallHeightLevel2` | `1` | `1` to `6` | Stacks Level 2 interior FlexiWall pieces vertically. |
| `InternalWallHeightLevel3` | `1` | `1` to `6` | Stacks Level 3 interior FlexiWall pieces vertically. |
| `WallPillarMaterial` | `Stone` | `Stone`, `Wood` | Chooses material set used for `Wall` and `Pillar` types. |
| `StaircaseReachMode` | `ToTheNextLevelOnly` | `ToTheNextLevelOnly`, `AllTheWay` | Controls staircase vertical reach. `ToTheNextLevelOnly` climbs one level at a time. `AllTheWay` climbs toward the highest available scaffold level. |
| `StaircaseStepRise` | `0.16` | `0.10` to `0.40` | Vertical height in metres between staircase treads. Lower values produce shallower, easier-to-climb steps but increase total tread count. |
| `StaircaseStepAngleDeg` | `15` | `10` to `30` | Degrees of rotation between staircase treads. Lower values produce a wider, gentler spiral. |
| `StaircaseStepRadius` | `0.75` | `0.50` to `1.00` | Distance in metres from the center pole to the midpoint of each tread. Keep at or below `1.0` to stay within the 4×4 staircase footprint. |

### Scaffolding

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `RoofScaffolding` | `false` | `true` / `false` | Adds scaffold poles and perimeter beams around the plan during the build. Automatically forced to `true` when `FloorPlanLevels > 1`. |
| `RoofScaffoldingType` | `Gable` | `Gable`, `Flat` | Controls the topmost scaffold deck shape when `ScaffoldingFloors` is enabled. `Gable` builds a pitched roof layout and extends front-mid/back-mid/center support columns up to the apex. `Flat` tiles the topmost level with ridge roof pieces edge-to-edge (one piece per 2x2 tile area). |
| `RoofScaffoldingGableFlooring` | `RoofWithFloorUnderlay` | `RoofWithFloorUnderlay`, `RoofOnly` | Gable-only top-surface behavior (used only when `RoofScaffoldingType=Gable`). `RoofWithFloorUnderlay` places floor tiles under the top gable roof. `RoofOnly` places only gable roof pieces. |
| `ScaffoldingLevels` | `1` | `1` to `3` | Number of stacked scaffolding levels when `RoofScaffolding` is enabled. Each extra level repeats the same scaffold pattern +4m above the previous level. |
| `ScaffoldingFloorHeight` | `4` | `2`, `4`, `6` | Vertical height in metres between scaffold levels. Must be a multiple of 2m so stacked wood-iron support segments line up correctly. |
| `ScaffoldingFloorHeight#2` | `4` | `2`, `4`, `6` | Vertical height in metres for the second scaffold level. Used when `ScaffoldingLevels` is `2` or `3`. |
| `ScaffoldingFloorHeight#3` | `4` | `2`, `4`, `6` | Vertical height in metres for the third scaffold level. Used when `ScaffoldingLevels` is `3`. |
| `ScaffoldingFloors` | `false` | `true` / `false` | Builds wood floor decks across each scaffolding level when `RoofScaffolding` is enabled. Automatically forced to `true` when `FloorPlanLevels > 1`. |
| `TransverseScaffoldingBeams` | `false` | `true` / `false` | Adds west-to-east horizontal scaffold beams at each scaffold pole row. Automatically forced to `true` when `FloorPlanLevels > 1`. |
| `LongitudinalScaffoldingBeams` | `false` | `true` / `false` | Adds south-to-north horizontal scaffold beams at each scaffold pole column. Automatically forced to `true` when `FloorPlanLevels > 1`. |


### Preview

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `BuildOriginForwardOffset` | `0` | `0` to `20` | Extra distance added to the auto-computed player-to-build-center preview offset (meters). Usually not needed. |
| `MoveStep` | `2.0` | `0.25` to `10.0` | Nudge distance per move key press (meters). |
| `FineMoveStep` | `0.5` | `0.05` to `5.0` | Nudge distance per move key press while fine-adjust key is held (meters). |

### Preview Keys

| Option | Default | What it does |
|---|---|---|
| `MoveForwardKey` | `UpArrow` | Move preview center forward relative to camera. |
| `MoveBackwardKey` | `DownArrow` | Move preview center backward relative to camera. |
| `MoveLeftKey` | `LeftArrow` | Move preview center left relative to camera. |
| `MoveRightKey` | `RightArrow` | Move preview center right relative to camera. |
| `RotateLeftKey` | `Q` | Rotate preview counter-clockwise. |
| `RotateRightKey` | `G` | Rotate preview clockwise. |
| `ConfirmKey` | `E` | Confirm current preview placement and start build. |
| `CancelKey` | `Escape` | Cancel preview. (Right-click cancels too.) |
| `FineAdjustKey` | `LeftShift` | Hold for fine movement and fine rotation. |

### Preview - Rotation

| Option | Default | Allowed values | What it does |
|---|---|---|---|
| `RotateStepDegrees` | `90` | `22.5`, `45`, `90` | Rotation applied per coarse rotate key press (degrees). |
| `FineRotateStepDegrees` | `22.5` | `22.5`, `45`, `90` | Rotation applied per rotate key press while fine-adjust key is held (degrees). |
| `BuildRotationSnapDegrees` | `90` | `22.5`, `45`, `90` | Snaps the final build rotation to the nearest allowed angle when preview starts and when build is confirmed. |

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


## Sample Plans

The `samples/` folder contains ready-to-use examples.

**Blank starter grids:**

- `samples/myfloorplan4x4.vfp`, `myfloorplan8x8.vfp`, `myfloorplan12x12.vfp`, `myfloorplan16x16.vfp`, `myfloorplan16x20.vfp`, `myfloorplan20x20.vfp`, `myfloorplan20x20_double.vfp`, `myfloorplan24x24.vfp`, `myfloorplan28x28.vfp` — empty grids in common sizes

**Preset bundles (`.vpfset`) — load with `Alt+F8` in-game:**

- `samples/ExampleRoundHouse.vpfset` — three-level FlexiWall round house (20×20); level files: `RoundHouse_Level_1/2/3_20x20.vfp`
- `samples/ExampleCirclular.vpfset` — three-level circular FlexiWall build (20×20); level files: `Circle_Level_1/2/3_20x20.vfp`
- `samples/ExampleShamrock.vpfset` — three-level shamrock multi-arc FlexiWall build (20×20); level files: `Shamrock_Level_1/2/3_20x20.vfp`
- `samples/ExampleShamrockGroundFloorOnly.vpfset` — ground-floor-only shamrock layout
- `samples/ExampleCosyHouse.vpfset` — cosy home with beds, hearth, and workbenches; level files: `CosyHome_Level_1_20x20.vfp`
- `samples/ExampleHeart.vpfset` — three-level heart-shaped FlexiWall build (20×20); level files: `Heart_Level_1/2/3_20x20.vfp`
- `samples/ExampleTurrets.vpfset` — three-level build with FlexiWall turret towers (20×20); level files: `Turrets_level_1/2/3_20x20.vfp`
- `samples/ExampleThreeFloorBuild.vpfset` — three-level rectangular build (16×20); level files: `ThreeFloorBuild_Level_1/2/3_16x20.vfp`

**Standalone single-level plans:**

- `samples/StarBase20x20.vfp` — star-shaped base layout
- `samples/RoundHouse.vfp` — single-level FlexiWall round house
- `samples/Target.vfp` — concentric-ring FlexiWall target layout
- `samples/Turrets.vfp` — single-level turret layout
- `samples/Workbenches_16x20.vfp` — 16×20 plan with workbenches
- `samples/Workbenches_and_Staircase_16x20.vfp` — 16×20 with workbenches and staircase
- `samples/Workbenches_and_Staircase_ALT_16x20.vfp` — alternate staircase placement

## Tips

- **Test away from world spawn.** Terrain levelling and piece placement both depend on world-space coordinates. Builds near the origin (0, 0) can mask position-dependent bugs — always test in a realistic location.
- **Building on already-levelled terrain is much quicker.** Create a simple floor plan covering the area you want to build on. Run it once to level the ground, then portal away and back (this clears the terrain snapshot). Press F9 to remove the temporary build — the levelled terrain stays. Subsequent builds in the same area skip most of the levelling work and place pieces much faster.
- **Watch the edge risk markers before confirming.** Orange/red diamond markers appear when surrounding terrain is steep enough to cause tears or spikes. Nudging or rotating the plan a few metres is often enough to clear them.
- **Rotate to align with the terrain slope.** If one side of the pad is significantly higher, try rotating so the plan runs along the contour rather than across it — this reduces the height range the leveller has to span.
- **Use the overlay in the Designer before building.** Enable L2/L3 overlays to check for piece clashes across levels before saving any of the `.vfp` files. Red dashed outlines flag conflicts, with directional rules for Staircases and Hearths.
- **Bed orientation matters.** The arrow shown on a Bed in the Designer points toward the head. The in-game placement will match — players sleep with their head toward the arrow.
- **Undo is session-only for terrain.** The terrain snapshot is held in memory. Move to a different area or reload the world and it is gone. Piece removal still works across sessions because pieces are tagged.
- **Use Ctrl+F9 to keep your leveled ground.** If you want to remove the build and try again without re-leveling, press `Ctrl+F9` instead of `F9` to start undo. Pieces are removed but the leveled terrain stays. This is especially useful when iterating on a design in the same spot.
- **The preview shows all floor levels.** When `FloorPlanLevels` is 2 or 3, floating red bands appear during preview at the height of each upper floor. Use them to check clearance and surroundings before confirming.
- **Adjust the undo radius before confirming.** During the undo confirmation window, `+`/`-` resizes the search circle and arrow keys reposition it. This is useful when two builds are close together and you only want to remove one.
- **FlexiWalls meet cleanly at shared cell boundaries.** When two FlexiWalls share a cell boundary, their endpoints snap to the same edge midpoint, so there is no gap at the join. Use the green and red endpoint dots in the Designer to confirm the snap positions before saving.
- **Use adjacent cells for a full circle.** Select start and end cells that are one cell apart, then drag the midpoint handle to the far side of the ring. The Designer preview and the in-game build will both produce a closed circle.
- **Use the row/column labels to place pieces precisely.** The Designer prints numeric reference labels along all four grid edges, using the same 0-based numbering as the underlying `col`/`row` values and `.vfp` file contents — handy for lining up pieces or cross-checking coordinates.
- **Reposition furniture without deleting it.** `Bed`, `Staircase`, `Hearth`, and `Workbench` pieces show a small handle at their centre once placed (or reloaded from a file). Drag the handle to move the piece, click it to select it, then use the arrow keys or mouse wheel to rotate it in place. Press `Escape` or click elsewhere to deselect.
- **Right-click and Escape only cancel — they never delete.** Both simply back out of whatever's active (an in-progress FlexiWall draw, a piece selection/drag, or the current tool) without removing any pieces, including FlexiWalls. To delete a piece, hover it and press `Delete` or `Backspace` — this also removes the currently selected piece (furniture or FlexiWall) if one is selected.
- **Deletion is scoped to the active tool's piece type.** With the `Hearth` tool selected, `Delete`/`Backspace` can only remove `Hearth` pieces — Floors, Walls, and other types under the cursor are left untouched. Switch tools (or select a placed piece via its handle, which switches the tool to match) to delete a different type. With no tool selected, any piece type can be removed.
- **`Delete`/`Backspace` works as soon as you hover the grid — even right after picking a tool from the menu.** Clicking a tool button no longer "traps" the shortcut; you can select a tool, hover over a placed piece of that type, and delete it immediately without first placing anything.
- **Hovering shows you exactly what `Delete`/`Backspace` would remove.** The targeted piece — whether it's the top-most match under the cursor or your current selection — is outlined and tinted in red on the canvas, so there's never any guessing about what the next press of the key will do.
- **FlexiWalls are select-and-delete just like furniture.** Click a placed FlexiWall's midpoint handle to select it (it gets the same dashed yellow outline as a selected furniture piece), then press `Delete`/`Backspace` to remove it. This works with any tool active — you don't need to switch to the FlexiWall tool first. While the FlexiWall tool *is* active, that same handle still drags to reshape the curve as before.
- **Multi-level plans must nest.** Level 2 and Level 3 footprints must fit within the Level 1 footprint. If a level file is rejected at build time, check that all its pieces fall inside the Level 1 grid boundary.
- **Scaffolding settings are forced on for multi-level builds.** When `FloorPlanLevels > 1`, `RoofScaffolding`, `ScaffoldingFloors`, `TransverseScaffoldingBeams`, and `LongitudinalScaffoldingBeams` are all automatically enabled. You do not need to set them manually.

## Feature Highlights


![Valheim Floor Plan screenshot 1](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/ValheimFloorPlan.png)
![Valheim Floor Plan screenshot 2](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_1.png)
![Valheim Floor Plan screenshot 3](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_2.png)
![Valheim Floor Plan screenshot 4](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_3.png)
![Valheim Floor Plan screenshot 5](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_4.png)
![Valheim Floor Plan screenshot 6](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_5.png)
![Valheim Floor Plan screenshot 7](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex1_6.png)


## Spiral Staircase Showcase

![Valheim Floor Plan screenshot 8](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_1.png)
![Valheim Floor Plan screenshot 9](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_2.png)
![Valheim Floor Plan screenshot 10](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_3.png)
![Valheim Floor Plan screenshot 11](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_4.png)
![Valheim Floor Plan screenshot 12](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_5.png)
![Valheim Floor Plan screenshot 13](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_6.png)
![Valheim Floor Plan screenshot 14](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_7.png)
![Valheim Floor Plan screenshot 15](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex2_8.png)
                                  

## Example Layouts

![Valheim Floor Plan screenshot 16](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex3.png)
![Valheim Floor Plan screenshot 17](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex4.png)


## FlexiWall Showcase

Round houses, multi-arc layouts, and multi-level curved stone builds using the FlexiWall tool.

![Config settings for a FlexiWall round house build](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_1.png)
![Designer: round house layout with a single FlexiWall circle](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_2a.png)
![In-game: completed single-level round house exterior](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_2b.png)
![In-game: round house interior](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_3.png)
![In-game: round house extended to two levels](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_5.png)
![Designer: shamrock layout with three overlapping FlexiWall arcs](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_6a.png)
![In-game: aerial view of the completed shamrock build](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_6b.png)
![Designer: large circle FlexiWall layout on a 20x20 grid](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_7a.png)
![In-game: roof scaffolding placed over the circular footprint](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_7b.png)
![In-game: level 2 roof pieces being placed on the round house](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_8.png)
![Designer: cosy home rectangular layout with beds, staircase, hearth, and workbenches](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_9a.png)
![In-game: multi-level rectangular build under construction with scaffolding](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_9b.png)
![In-game: clash signpost showing undo/clash detail signs](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_9c.png)
![Designer: rounded-corner square layout with FlexiWall arcs at each corner](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_10a.png)
![In-game: completed two-level curved stone building](https://raw.githubusercontent.com/AlexDroz/ValheimFloorPlan/master/images/Ex7_10b.png)

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

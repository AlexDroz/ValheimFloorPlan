# DEV Notes

Project-specific implementation notes for ValheimFloorPlan.

## Source Of Truth
- Edit `Designer/app.js` for Designer behavior.
- Edit `PieceMap.cs` and `FloorPlanBuilder.cs` for runtime placement behavior.
- Do not treat `artifacts/thunderstore/stage` as source; packaging copies from the root `Designer` folder.

## System Overview
ValheimFloorPlan is a BepInEx mod that reads a `.vfp` design, previews placement in-world, levels terrain under the footprint, and places Valheim building prefabs.

### Core Files
- `ValheimFloorPlanPlugin.cs`: plugin entry point, config registration, hotkey handling.
- `FloorPlan.cs`: `.vfp` parser.
- `PieceMap.cs`: piece-type to prefab mapping and footprint metadata.
- `TerrainLeveler.cs`: terrain raise/level pipeline.
- `TerrainSnapshot.cs`: terrain capture/restore for undo.
- `FloorPlanBuilder.cs`: preview, build coroutine, scaffolding, undo orchestration.

## Runtime Flow

### 1) Configuration And Hotkeys
- Config is registered in `Awake` and hotkeys are polled in `Update`.
- Build starts with `BuildHotkey` (default `F8`) and undo with `UndoHotkey` (default `F9`).
- Important build/terrain knobs include:
	- `TerrainLevelPasses` (1-5), `TerrainSpikeCleanupPasses` (1-5)
	- `TerrainStampRadius` (3.0-6.0)
	- `TerrainHighPointDelta` (0.0-4.0)
	- `TerrainUseStagedRaise`, `TerrainRaiseStepHeight`, `TerrainMaxRaiseStages`
	- `ExternalWallHeight`, `ScaffoldingLevels`, `ScaffoldingFloorHeight`, `ScaffoldingFloorHeight#2`, `ScaffoldingFloorHeight#3`, `WallPillarMaterial`
	- `BuildOriginForwardOffset`

### 2) `.vfp` Parsing (`FloorPlan.Load`)
- Expected records:
	- `cols=<n>`
	- `rows=<n>`
	- `piece,col,row,type[,rotation][,wallFace]`
- Grid is 1m cells (`CELL_SIZE = 1f`).
- Piece coordinates are top-left grid anchors in file-space.

### 3) Piece Definitions (`PieceMap`)
- Maps design types to prefab names, dimensions, and Y offset.
- Effective footprint swaps W/H at 90 and 270 degrees (`EffW`/`EffH`).
- `YOffset` is placement-center offset above terrain (for correct prefab seating).

### 4) Build Coroutine (`FloorPlanBuilder`)
- `StartPreview` enters placement preview around a center pivot derived from player position + half-extent + `BuildOriginForwardOffset`.
- During preview, `EvaluateEdgeRisk` computes risk and can render markers.
- Confirm key (default `E`) launches `LevelThenPlace`:
	1. Capture terrain snapshot (`TerrainSnapshot.Capture`).
	2. Clear blockers in pad area (`ClearRocksInPad`).
	3. Level terrain (`TerrainLeveler.LevelForPlan`).
	4. Wait for terrain physics mesh convergence (`WaitForTerrainPhysics`).
	5. Place pieces (`PlacePieces`) with per-piece terrain raycast to settle Y.
	6. Run post-build spike cleanup (`PostBuildSpikeGuard`).

### 5) Undo (`FloorPlanBuilder.Undo`)
- First press shows undo preview/confirmation window.
- Second press removes placed pieces tagged with `vfp_build = "1"` within radius.
- Terrain arrays are restored from snapshot and chunks are saved/refreshed.

## Terrain Leveling Details
- Pre-sampling uses `ZoneSystem.GetGroundHeight` over footprint+buffer expanded by stamp radius.
- `targetY = maxSampledY + TerrainHighPointDelta`.
- Design intent: raise-only behavior to avoid upward edge-falloff spikes.
- Multi-pass level and spike cleanup pass counts are config-driven.
- `ApplyLevel` probes center/corners/edge-midpoints so overlapped terrain chunks are all modified.
- Recommended post-level delay scales with touched chunks: `max(2.0s, modifiedChunkCount * 0.5s)`.

## Placement Details
- Placement anchor is corner-based internally, derived from preview center+rotation.
- World placement uses center-based offsets from piece footprint:

```
dx = (col + EffW * 0.5) * CELL_SIZE
dz = (row + EffH * 0.5) * CELL_SIZE

x = origin.x - dx * cos(rotation) + dz * sin(rotation)
z = origin.z + dx * sin(rotation) + dz * cos(rotation)
```

- Placement Y is sampled by physics raycast and then adjusted with `def.YOffset`.
- After instantiate, set ZDO owner, set `vfp_build = "1"`, and set piece creator to player ID.
- External walls/pillars are stacked to `ExternalWallHeight`.
- For `WallPillarMaterial=Wood`: `Wall -> wood_wall_half`, `Pillar -> wood_pole_log`, with outward alignment shift for perimeter pieces.
- `wallFace=inner` rotates wood walls by 180 degrees only for interior walls (perimeter walls are forced outward).

## Coordinate System
- Local `col`/`row` offsets are converted through `PieceMap.TransformPlanPoint` (mirrored X transform + rotation).
- In local plan space, `row` increases toward `+Z` before transform.
- 1 grid cell = 1 meter.
- `.vfp` uses top-left piece anchors; runtime placement uses world-space centers.
- Preview rotation is clockwise around selected center pivot.

## Verified Constraints
- Staircase footprint is 4m x 4m (4x4 cells) in both `Designer/app.js` and `PieceMap.cs`.
- Staircase spiral steps advance in 20-degree increments.
- First staircase step starts at the same rotation chosen in the Designer.
- Staircase openings must remain clear through scaffold floors and beams.

## Scaffold Notes
- Scaffolding beams are handled in `FloorPlanBuilder.cs`.
- Perimeter beams, top gable support beams, and ridge beams must respect staircase deck openings.
- `PlaceScaffoldBeamSpan` should keep blocked-opening checks so supports do not fill the staircase shaft.

### Vertical Pole Placement Logic
- Main entrypoint: `PlaceRoofScaffolding(plan, origin, rotationDeg)`.
- Prefab defaults: vertical uses `woodiron_pole` and horizontal uses `woodiron_beam`.
- Pole anchors are built from a clockwise perimeter parameter `t` over the plan bounds:
	- `0` = SW corner, `width` = SE, `width+depth` = NE, `2*width+depth` = NW.
	- `ScaffoldParamToLocal` converts `t` to local XZ.
- Initial pole anchor set includes:
	- All four perimeter corners.
	- Door-jamb-adjacent anchors for perimeter doorways (left/right adjacent cells on that edge).
	- Extra edge-join anchors every 4m (`POLE_SPACING`) along each edge, skipping near-duplicates and blocked doorway spans.
- Dedupe behavior:
	- `ScaffoldDedup(..., 0.5f)` removes near-duplicate anchors.
	- `IsNearAnyParam(..., 0.25f)` avoids adding anchors too close to existing ones.
- Multi-level behavior:
	- Levels are controlled by `ScaffoldingLevels` and per-level heights (`ScaffoldingFloorHeight`, `#2`, `#3`).
	- Each level places a vertical column at every perimeter anchor plus one center column.
	- `SpawnScaffoldColumn` segments columns in 2m pole segments (`POLE_SEGMENT_HEIGHT = 2f`).

```
Local Plan Space (before world transform)

							 +Z (row)
								 ^
								 |
	 NW corner     |      NE corner
 t=2w+d o--------+--------o t=w+d
				 |                 |
				 |   longitudinal  |   (S -> N, match local X)
				 |   beam spans    |
				 |       ^         |
				 |       |         |
				 |   [center pole] |
				 |       |         |
				 |       v         |
				 |                 |
 t=0     o--------+--------o t=w
 SW corner        |        SE corner
									+-------> +X (col)

Perimeter t path (clockwise):
	south edge: SW -> SE   (0 .. w)
	east edge:  SE -> NE   (w .. w+d)
	north edge: NE -> NW   (w+d .. 2w+d)
	west edge:  NW -> SW   (2w+d .. 2w+2d)

Horizontal interior beams:
	transverse   = West -> East (match local Z rows)
	longitudinal = South -> North (match local X columns)

Vertical columns per level:
	- all perimeter anchor poles (corners + doorway-adjacent + 4m edge joins)
	- one center column
```

### Horizontal Beam Logic
- Perimeter ring beams:
	- For each level, corners are connected clockwise using 2m beam segments with a small overlap (`BEAM_JOINT_OVERLAP`).
	- Candidate segment centers are skipped when they fall inside blocked openings (`IsInsideAnyHearthOpening` with scaffold deck openings).
- Transverse beams (West -> East):
	- Built by `PlaceTransverseBeams`.
	- Candidate endpoints come from west/east intermediate edge poles (corners and door-jamb anchors excluded).
	- West rows are paired to nearest east rows by local Z with tolerance.
	- Door span gating uses `blockedTransverseLocalZs` via `IsWithinAnyDoorSpan`.
- Longitudinal beams (South -> North):
	- Built by `PlaceLongitudinalBeams`.
	- Candidate endpoints come from south/north intermediate edge poles (corners and door-jamb anchors excluded).
	- South columns are paired to nearest north columns by local X with tolerance.
	- Door span gating uses `blockedLongitudinalLocalXs` via `IsWithinAnyDoorSpan`.
- Shared beam placement:
	- `PlaceScaffoldBeamSpan` places the actual 2m beam chain between two endpoints.
	- Entire span is rejected if its AABB overlaps any blocked opening (`IsBeamSpanBlockedByOpenings`).

### Opening And Support Constraints
- Openings used for deck/beam blocking are built by `BuildScaffoldDeckOpenings`:
	- Includes hearth footprints.
	- Includes staircase footprints (stair shafts stay open through decks/beams).
- Furniture exclusion model:
	- `BuildScaffoldFurnitureExclusions` creates exclusion volumes for `Workbench`, `Bed`, and `Hearth` plus front-clearance bands.
	- These exclusions are currently generated and passed through beam routines, but beam placement currently relies on opening checks and door-span gating for hard blocking.
- Post-pass cleanup:
	- After scaffolding placement, `PruneGroundFloorScaffoldVerticals` removes ground-floor poles within door proximity radius (`DOOR_RADIUS = 4.25f`) to keep entry paths clear.

### Roof Modes (Flat And Gable)
- Top-level roof/deck choice is handled by `PlaceScaffoldLevelFloorDeck` and depends on:
	- `RoofScaffoldingType` (`Flat` or `Gable`)
	- `RoofScaffoldingGableFlooring` (`RoofWithFloorUnderlay` or `RoofOnly`)
	- Topmost-level check (`isTopmostLevel`)
- Non-top levels place flat scaffold floor decks only.

#### Flat Mode
- Path: `PlaceTopScaffoldFlatRidgeRoof`.
- One ridge roof cap piece is placed per clear 2x2 tile (prevents dense overlap and keeps long edges touching).
- Ridge orientation follows the dominant plan axis:
	- If width >= depth, ridge runs along X.
	- Otherwise ridge runs along Z.
- If a 2x2 tile overlaps an opening, fallback placement uses 1x1 floor tiles only in clear cells.

```
Flat Ridge Top (top view, conceptual)

 +-------------------------------+
 | [R] [R] [R] [R] [R] [R] [R]  |  R = ridge cap tile (2x2 region)
 | [R] [R] [R] [R] [R] [R] [R]  |  orientation follows dominant axis
 |                               |
 |       (open shaft area)       |  shaft/opening cells stay empty
 |                               |
 +-------------------------------+
```

#### Gable Mode
- Path: `PlaceTopScaffoldGableRoof`.
- Two sloped roof runs are generated from opposite edges toward a center ridge.
- Per-segment roof/support pieces are placed by `PlaceSlopedRoofRun`:
	- roof piece offset slightly inward
	- support piece under roof path
- Optional floor underlay:
	- `RoofWithFloorUnderlay`: lay deck first, then place gable roof.
	- `RoofOnly`: skip underlay deck.
- Additional apex structure:
	- `PlaceGableApexSupportColumnIfClear` adds support columns at ridge-aligned key points.
	- `PlaceGableApexRidgePoleSpan` lays a ridge beam chain along the apex.
- All roof/support/apex placements are opening-aware and skip blocked hearth/stair shaft regions.

```
Gable Top (cross-section, conceptual)

      ridge beam span
           =======
          /  ^   \
         /   |    \   <- sloped roof runs
        /    |     \
   ----+-----+------+----  top scaffold level
        \  support  /
         \ columns /

   (hearth/stair openings punch through; blocked cells are skipped)
```

### Hearth And Chimney Stack Through Upper Levels
- Hearth openings originate from `BuildHearthOpenings(plan)` and are reused throughout scaffold deck, beam, and roof-support placement checks.
- Deck and top-surface placement avoid hearth cells via `IsBlockedByHearthOpening` (tile-level checks) and `IsInsideAnyHearthOpening` (point-in-opening checks).
- Per scaffold level, chimney walls are extended by `PlaceHearthChimneyLevel`:
	- Chimney walling starts at `max(levelBaseY, chimneyStartY)` where `chimneyStartY = scaffoldBaseY + HEARTH_ACCESS_CLEARANCE`.
	- This preserves a clear lower access zone before enclosure begins.
	- Walls are generated around the opening perimeter each meter layer (mixing 2m and optional 1m wall segments).
- After all scaffold levels, `PlaceHearthChimneyTop` extends the stack an additional cap height (`CHIMNEY_CAP_EXTRA_HEIGHT`) and then applies `PlaceHearthChimneyRoofCap` orientation logic based on opening aspect ratio.
- Result: hearth shafts stay open through floors/beams, while the chimney shell continues upward through higher levels and receives a top cap above the last scaffold deck.

### Staircase Shaft Through Upper Levels
- Staircase footprint is treated as an opening in scaffold systems via `BuildScaffoldDeckOpenings` (staircase pieces are added to the same opening list used by hearth blocking).
- This opening list is consumed by:
	- Floor deck tiling (`PlaceScaffoldFlatDeckTiles` and top deck/roof variants).
	- Perimeter/interior beam placement checks (`IsInsideAnyHearthOpening` and `IsBeamSpanBlockedByOpenings`).
	- Ridge and top-support placement checks (`PlaceGableApexRidgePoleSpan`, `PlaceTopSupportPieceIfClear`, `PlaceTopRoofPieceIfClear`).
- Stair geometry itself is built by `PlaceStaircaseComposite`:
	- Central pole stacks from terrain to a computed top target.
	- Target rise uses `GetStaircaseTargetRise`: sum of active scaffold floor heights when scaffold floors are enabled, otherwise default 4m.
	- Each tread is a single 2m beam (`STEP_HALF_LENGTH = 1f`) placed around the center pole at `STEP_RADIUS = 0.75f`.
	- Vertical progression is `STEP_RISE = 0.25f` per tread.
	- Angular progression is `STEP_ANGLE_DEG = 20f` per tread.
	- Start angle is derived from plan rotation and corrected to match Designer orientation:
		- `startAngleDeg = (270 - piece.Rotation + STAIR_START_ANGLE_CORRECTION_DEG) mod 360`
	- First tread starts slightly below center terrain (`STEP_START_OFFSET = -0.15f`) but is clamped by local terrain sampling (`FIRST_STEP_MAX_SINK`) so it does not bury too deeply.
	- Tread yaw is computed from radial direction so the inner end points toward the center pole:
		- `localStepYaw = atan2(offsetX, offsetZ) + 90`
	- When scaffold floors are enabled, step Y snaps to each deck height (`levelTop + FLOOR_DECK_LIFT`) so steps land cleanly at each upper floor.
	- Guard signs are attached on alternating treads (and always the roof/attic step), with floor labels and biome labels stacked as a pair.
- Result: upper floors do not seal the staircase column; the stair shaft remains punched through and the staircase aligns vertically with all enabled scaffold levels.

```
Top View (local staircase footprint: 4m x 4m)

	+-----------------------+
	|                       |
	|        o center       |
	|       /               |
	|      *  tread n       |  radius from center to tread center = 0.75m
	|     /                 |  each next tread rotates +20 deg
	|    *  tread n+1       |
	|                       |
	+-----------------------+

Side View (vertical progression)

	y
	^         deck snap levels (if enabled)
	|        ---------  <- levelTop + FLOOR_DECK_LIFT
	|      * tread k
	|    * tread k-1      deltaY per step = 0.25m
	|  * tread k-2
	|o center pole stack
	+----------------------------> step index
```

## Compatibility Notes
- Target runtime is Valheim/.NET 4.6.2.
- Avoid C# tuple syntax in runtime code paths.
- Prefer explicit loops and small helper types over tuple-heavy LINQ for compatibility.

## Build And Packaging
- Build task: `Build & Deploy ValheimFloorPlan`.
- Thunderstore packaging recreates `artifacts/thunderstore/stage` and copies from root `Designer`.

## Planning References
- Yard strategy options and tradeoffs are tracked in `YARD_STRATEGY_NOTES.md`.

## Last Verified
- 2026-05-27
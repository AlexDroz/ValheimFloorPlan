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
	- `ExternalWallHeightLevel1`, `ExternalWallHeightLevel2`, `ExternalWallHeightLevel3`
	- `ScaffoldingLevels`, `ScaffoldingFloorHeight`, `ScaffoldingFloorHeight#2`, `ScaffoldingFloorHeight#3`
	- `WallPillarMaterial`, `StaircaseReachMode`
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

## FlexiWall Placement Details

FlexiWall segments are computed by `ComputeFlexiWallSegments` (straight or arc path) and placed in a stacked loop in `FloorPlanBuilder.cs`.

### Brick-Bond Row Offset

Every other row is shifted -0.5m along the wall tangent to create a brick-bond pattern. **Rows are counted 1-indexed from the ground** (s=0 = row 1):

- **1-indexed odd rows** (s=0, 2, 4... → `s % 2 == 0`): no shift — standard position.
- **1-indexed even rows** (s=1, 3, 5... → `s % 2 != 0`): shifted -0.5m along the tangent for all segments except segment 0 (the start anchor).

```
Row 1 (s=0):  [seg0]  [seg1]  [seg2]  ...  [segN-1]      ← normal positions
Row 2 (s=1):  [seg0]   [s1]    [s2]   ...  [sN-1]  [cap]  ← shifted -0.5m + cap
Row 3 (s=2):  [seg0]  [seg1]  [seg2]  ...  [segN-1]      ← normal
...
```

### Wall-End Closing Brick

On shifted rows, the last segment's brick is pulled back 0.5m, leaving a gap at the wall end. A closing brick is placed to fill it:

- Cap loop: `for (int s = 1; s < fwStackCount; s += 2)` — matches the shifted rows only.
- Cap position: **`segCenter`** (no extra tangent offset beyond the last segment center).

This makes the cap's right edge (`segCenter + 0.5m`) flush with the unshifted rows' right edge, so the wall end is flat on both row types.

**Common mistake:** placing the cap at `segCenter + 0.5f` overshoots the wall end by 0.5m, causing shifted rows to extend further than unshifted rows and producing a ragged staircase-like end. The cap must be at `segCenter + 0f`.

**Other common mistake:** using `s % 2 == 0` for the shift condition puts the offset on 1-indexed *odd* rows (ground, row 3, etc.) instead of even rows — visually swaps which rows have the brick-bond offset.

## Coordinate System
- Local `col`/`row` offsets are converted through `PieceMap.TransformPlanPoint` (mirrored X transform + rotation).
- In local plan space, `row` increases toward `+Z` before transform.
- 1 grid cell = 1 meter.
- `.vfp` uses top-left piece anchors; runtime placement uses world-space centers.
- Preview rotation is clockwise around selected center pivot.

### Designer vs In-Game Axis Warning
**The Designer canvas and the in-game world use opposite north/south orientations.**
- In the Designer, rows increase upward on screen = plan `+Z` = world `+Z`.
- `minRow` is the **top edge in the Designer** but the **back/north end of the building in-game**.
- `maxRow` is the **bottom edge in the Designer** (player-facing "front") and the **south/front of the building in-game**.
- Consequently `maxRow` = front of building = toward the player after a normal build.

**Canonical formula for world directions** (copy from `PlaceCenterSignage` — known correct):
```csharp
float signageRotationDeg = rotationDeg - 180f;
float signageRad  = signageRotationDeg * Mathf.Deg2Rad;
float signageSinR = Mathf.Sin(signageRad);
float signageCosR = Mathf.Cos(signageRad);
// "toward player / south / front of building"
float southX = -signageSinR;  float southZ = -signageCosR;
// "east" (perpendicular)
float eastX  = -signageCosR;  float eastZ  =  signageSinR;
// pole rotation, sign rotation, sign face offset
Quaternion poleRot = Quaternion.Euler(0f, signageRotationDeg, 0f);
Quaternion signRot = Quaternion.Euler(0f, signageRotationDeg + 180f, 0f);
float signOX = -signageSinR * 0.3f;  float signOZ = -signageCosR * 0.3f;
```
Do **not** derive south/east by differencing `TransformPlanPoint` outputs — that approach consistently produces the wrong end of the building.

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
- **The gable roof panels themselves are never opening-aware (fixed in 2.0.4).** `PlaceTopScaffoldGableRoof` is always called with an empty opening list (`new List<HearthOpening>()`), so the sloped roof surface always covers the full footprint regardless of what staircase/hearth openings exist in the deck below it. Earlier versions passed the real opening list straight through, which let stair/hearth openings punch gaps or skip tiles in the roof surface itself — see `PlaceScaffoldLevelFloorDeck` around the `// Gable roof panels are never blocked by deck openings...` comment for the call site. Only the floor underlay beneath the roof (when `RoofWithFloorUnderlay` is selected) still respects openings.

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

#### Chimney Height Vs. Roof Apex (fixed in 2.0.4)
- A flat-roof assumption is not enough once `RoofScaffoldingType=Gable` — the gable ridge can sit well above the top deck, and a chimney sized only against deck height could end up capped *inside* the sloped roof surface.
- Both the ground-floor hearth path (`BuildScaffoldingForGroundHearths`, ~`FloorPlanBuilder.cs:2277`) and the upper-level scaffold path (`PlaceRoofScaffolding`, ~`FloorPlanBuilder.cs:4969`) compute a `roofApexY`:
	- Defaults to `topDeckY` for `Flat` roofs.
	- For `Gable` roofs: `roofApexY = topDeckY + tan(26°) * halfSpan`, where `halfSpan` is half of the shorter footprint dimension (mirrors the slope math in `PlaceTopScaffoldGableRoof`).
- The final chimney top is then `max(levelBaseY + CHIMNEY_CAP_EXTRA_HEIGHT, roofApexY + CHIMNEY_APEX_CLEARANCE)` — i.e. tall enough for a normal cap, but never shorter than "clears the actual ridge plus clearance".
- Once the chimney height is known, `RemoveInterferingUpperRoofPieces` and `RemoveInterferingUpperDeckPieces` are run against `[chimneyBaseY .. roofApexY + 0.6f]` (roof) and `[chimneyStartY .. chimneyTopY + 0.75f]` (deck) so any roof/deck tiles the gable surface placed across the hearth opening are cleared away — this is what makes the chimney "punch through" a roof that, per the rule above, was deliberately built as a solid, opening-agnostic surface.
- **Why this matters:** before this fix, chimneys were sized against `topDeckY`/flat-cap heights only, so on a Gable roof the stack could terminate below or right at the sloped surface — the chimney looked "blocked" or clipped by the roof, especially for ground-floor hearths under multi-level gable roofs.

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

#### Staircases Do Not Punch The Roof (fixed in 2.0.4)
- Unlike hearth chimneys, a staircase shaft only needs to stay open through **decks and beams** — it terminates at the top deck/attic step and never needs a vertical channel through the roof above it.
- When an upper-level staircase is placed, cleanup calls `RemoveInterferingUpperDeckPieces` and `RemoveInterferingUpperScaffoldBeamPieces` for the shaft volume, but **deliberately does not** call `RemoveInterferingUpperRoofPieces` — see the comment block at `FloorPlanBuilder.cs:1873`: *"Do NOT remove roof pieces for staircase shafts — the gable (or flat) roof spans the full footprint and should not be punched through above a staircase. Only hearth/chimney shafts need a clear vertical channel through the roof."*
- **Why this matters:** an earlier version removed roof tiles directly above the stair shaft during cleanup, leaving a hole in an otherwise-solid roof even though nothing needed to pass through it. Combined with the Gable-roof full-coverage rule above, the roof now always stays solid over a staircase.

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

## Unreleased Internal Notes

### Multi-Level Layout Controls
- Added `FloorPlanLevels` config (`1..3`) in `ValheimFloorPlanPlugin` as the primary selector for active plan count.
- `FloorPlanFileLevel2` and `FloorPlanFileLevel3` remain path-based sources for optional upper layouts.
- Upper-level placement now gates on `FloorPlanLevels`:
	- `1` = no upper-level placement pass.
	- `2` = requires a valid `FloorPlanFileLevel2`.
	- `3` = requires valid `FloorPlanFileLevel2` and `FloorPlanFileLevel3`.

### Enforced Scaffolding Rules For Multi-Floor Layouts
- Centralized in `ApplyScaffoldingRules`.
- When `FloorPlanLevels > 1`:
	- `RoofScaffolding` is forced `true`.
	- `ScaffoldingFloors` is forced `true`.
	- `TransverseScaffoldingBeams` is forced `true`.
	- `LongitudinalScaffoldingBeams` is forced `true`.
	- Minimum `ScaffoldingLevels` is forced to `FloorPlanLevels`.
- `RoofScaffolding` and `ScaffoldingFloors` now route through the same rule-application path on setting change.

### Upper-Level Placement And Clash Logic (Current)
- Upper-level plans are footprint-validated against Level 1 bounds before placement.
- Placement runs level-by-level (`Level 2`, then `Level 3` if enabled).
- Within each level, piece ordering is route-first to avoid mutual blocking:
	- place `Staircase` first,
	- then `Hearth`,
	- then all other piece types.

#### Per-Level Placement Rules
- Upper-level `Floor2x2` and `Floor1x1` are skipped (scaffold decks provide those floors).
- `Wall`, `Pillar`, and `Doorway` are allowed on upper-level perimeter cells.
- Upper-level perimeter `Wall` and `Pillar` stacking uses the general `ExternalWallHeight` setting.
- Upper-level piece Y is based on scaffold deck height (`GetDeckYForScaffoldLevel`).

#### Clash Detection Scope
- Most clashes are level-local (checked only against pieces/footprints established in the same upper level pass).
- Lower-level furniture does not block higher-level placement.
- Two cross-level blockers are intentionally global across levels:
	- Hearth chimney shafts,
	- Staircase shafts.
- Any higher-level piece that overlaps one of those shaft footprints is skipped.

#### Hearth Rules (Upper Levels)
- Must be at least 1 cell away from that level plan perimeter.
- Must not overlap same-level shaft footprints.
- Must keep at least 1-cell spacing from other same-level Hearth footprints.
- On successful placement, Hearth footprints are added to chimney-shaft blockers for higher levels.

#### Staircase Rules (Upper Levels)
- Built with `PlaceStaircaseComposite`, with target determined by `StaircaseReachMode`:
	- `ToTheNextLevelOnly`: climb only from current deck to the next deck.
	- `AllTheWay`: climb from current deck toward the top available scaffold deck.
- Staircase shaft footprint is registered as a blocker only up to the configured reach (next level only vs all higher levels).
- After placement, cleanup removes obstructing upper decks, roof pieces, and horizontal scaffold beams in shaft space above the current deck.

#### Clash Reporting
- Level summary HUD reports skip categories.
- If a level has clashes, one clash pole is spawned for that level with stacked detail signs (capped with overflow note), and detailed lines remain in logs.

## Manual Test Matrix (Regression)

### Staircase Reach Mode

1. Base staircase, `ToTheNextLevelOnly`
	- Setup: `ScaffoldingLevels=3`, `ScaffoldingFloors=true`, `StaircaseReachMode=ToTheNextLevelOnly`; place one Level 1 staircase.
	- Expected: staircase terminates at Level 2 deck height, not Level 3.

2. Base staircase, `AllTheWay`
	- Setup: same as above but `StaircaseReachMode=AllTheWay`.
	- Expected: staircase reaches top available deck (Level 3 in this setup).

3. Upper staircase, `ToTheNextLevelOnly`
	- Setup: staircase in Level 2 plan with `FloorPlanLevels=3`, `ScaffoldingLevels=3`, `StaircaseReachMode=ToTheNextLevelOnly`.
	- Expected: Level 2 staircase climbs only to Level 3 deck.

4. Top-level staircase skipped by mode
	- Setup: staircase in highest active plan level with `StaircaseReachMode=ToTheNextLevelOnly`.
	- Expected: staircase skipped with explicit reach-mode reason in summary/log and clash detail.

### Multi-Clash And Signage

5. Mixed clashes on one upper level
	- Setup: include overlaps against chimney shaft and staircase shaft plus one same-level route clash.
	- Expected: one clash pole for that level, multiple detail signs (up to cap), overflow note if needed, and matching log lines.

6. Cross-level blocker scope
	- Setup: create lower-level furniture under an upper-level piece path plus separate hearth/stair shafts.
	- Expected: upper pieces are blocked by shafts only; lower-level furniture alone does not block upper-level placement.

### Routing And Punch-Through

7. Stair shaft punch-through integrity
	- Setup: staircase path crossing decks, horizontal scaffold beams, and top roof coverage.
	- Expected: no sealed shaft segments; interfering deck/beam/roof pieces are removed in shaft volume.

8. Hearth chimney route integrity
	- Setup: upper-level hearth under additional decks/roof with nearby non-overlapping furniture.
	- Expected: chimney path remains open vertically; non-overlapping nearby pieces remain.

### Duplicate Decking Fix
- Upper-level `Floor2x2` and `Floor1x1` pieces are skipped in `PlaceUpperLevelPieces`.
- Rationale: scaffold decks already provide the walkable floor surface for those levels.
- Result: avoids duplicate overlapping floor tiles on Level 2/3.

## Planning References
- Yard strategy options and tradeoffs are tracked in `YARD_STRATEGY_NOTES.md`.

## Last Verified
- 2026-06-04
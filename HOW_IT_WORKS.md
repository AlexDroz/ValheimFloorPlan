# ValheimFloorPlan — How It Works

ValheimFloorPlan is a BepInEx mod that reads a design file (`.vfp`), previews placement in-world, levels the terrain under the build site, and spawns Valheim building pieces to construct a floor plan automatically.

---

## Components

| File | Purpose |
|---|---|
| `ValheimFloorPlanPlugin.cs` | BepInEx plugin entry point; wires hotkeys and configuration |
| `FloorPlan.cs` | Parses the `.vfp` design file |
| `PieceMap.cs` | Maps design-file piece names to Valheim prefab names and dimensions |
| `TerrainLeveler.cs` | Raises and levels the terrain under the build footprint |
| `TerrainSnapshot.cs` | Captures/restores terrain state for the undo feature |
| `FloorPlanBuilder.cs` | Orchestrates the full build sequence as a Unity coroutine |

---

## Step 1 — Configuration and Hotkeys

On startup (`Awake`), the plugin registers BepInEx configuration entries for file path, hotkeys, terrain passes, preview controls, and HUD behavior. Key entries are:

- **`FloorPlanFile`** — full path to the `.vfp` file to load.
- **`BuildHotkey`** (default `F8`) — starts placement preview for the configured `.vfp`.
- **`UndoHotkey`** (default `F9`) — removes all placed pieces and restores terrain.
- **`TerrainLevelPasses`** (default `2`, range `1–5`) — number of leveling passes run on the build pad.
- **`TerrainSpikeCleanupPasses`** (default `2`, range `1–5`) — number of post-leveling spike cleanup scans.
- **`TerrainStampRadius`** (default `3.0 m`, range `3.0–6.0`) — radius of each terrain stamp disc and outer preview footprint reach.
- **`TerrainSkipSatisfiedCenterStamps`** (default `true`) — skips center stamps when sampled terrain is already at/above target.
- **`TerrainUseStagedRaise`** (default `false`) — experimental multi-stage raise mode.
- **`TerrainRaiseStepHeight`** (default `0.5 m`, range `0.15–1.5`) — max vertical raise per stage when staged mode is enabled.
- **`TerrainMaxRaiseStages`** (default `1`, range `1–16`) — cap on number of raise stages.
- **`ExternalWallHeight`** (default `1`, range `1–4`) — stacks external `Wall`/`Pillar` pieces to this many levels.
- **`WallPillarMaterial`** (`Stone` or `Wood`, default `Stone`) — chooses wall/pillar prefab set.
- **`BuildOriginForwardOffset`** (default `12 m`, range `10–20`) — initial preview/build origin in front of the player.
- **`ProgressMessagePosition`** — HUD slot used for progress messages.
- **Preview movement/rotation settings and keys** — step sizes, fine-adjust key, movement keys, rotation keys (`Q`/`R` by default), confirm key (`E` by default), and cancel key.

`Update()` polls those hotkeys every frame and calls `FloorPlanBuilder.StartPreview` or `FloorPlanBuilder.Undo` accordingly.

---

## Step 2 — Parsing the `.vfp` File

`FloorPlan.Load(path)` reads the file line by line and populates a `FloorPlan` object:

```
cols=20
rows=15
piece,0,0,Floor2x2
piece,2,0,Wall,90
piece,4,2,Pillar
```

| Field | Meaning |
|---|---|
| `cols` / `rows` | Grid dimensions of the design |
| `piece,col,row,type[,rotation][,wallFace]` | One building piece at grid position (col, row) with optional rotation and optional wall face (`outer` / `inner`) |

The grid origin (col=0, row=0) maps to the selected preview origin (default: player position plus forward offset when preview starts). Each grid cell is **1 Valheim metre** (`CELL_SIZE = 1f`).

---

## Step 3 — Piece Definitions (`PieceMap`)

`PieceMap` translates a `.vfp` piece type string into a `PieceDef`:

| .vfp type | Prefab | W×H cells | Y offset |
|---|---|---|---|
| `Floor2x2` | `wood_floor` | 2×2 | 0 m |
| `Floor1x1` | `wood_floor_1x1` | 1×1 | 0 m |
| `Wall` | `stone_wall_2x1` | 2×1 | 0.5 m |
| `Doorway` | `wood_door` | 2×1 | 1 m |
| `Pillar` | `stone_pillar` | 1×1 | 1 m |
| `Hearth` | `hearth` | 3×2 | 0 m |

`BaseW` / `BaseH` are the footprint at rotation 0. `EffW` / `EffH` swap width and height for 90° and 270° rotations, matching the designer tool's coordinate logic exactly.

`YOffset` is the height above terrain at which the piece's centre is placed — floors (flat, on the ground) use 0, while walls and pillars (2 m tall) use 0.5–1 m so the geometry sits correctly on the surface.

---

## Step 4 — The Build Sequence (Coroutine)

When `F8` is pressed, `FloorPlanBuilder.StartPreview` enters placement preview mode. The initial preview origin is the player's position plus `BuildOriginForwardOffset` in the facing direction. In preview, you can nudge or rotate the plan, then confirm with the configured preview confirm key (default `E`) to launch `LevelThenPlace`. All heavy steps are spread across Unity frames to avoid freezing the game.

During preview, `EvaluateEdgeRisk` runs on a timed cadence and computes:
- Risk level (`Low`, `Medium`, `High`)
- Edge relief
- Edge irregularity
- Max cross-edge step

If risk is `Medium` or `High`, risk markers are rendered in two sets:
- Terrain hotspot markers at sampled edge trouble locations
- Three fixed top-edge markers along the outer preview wall rim for visibility from downhill camera angles

### 4a. Snapshot terrain
Before any changes, `TerrainSnapshot.Capture` samples a generous bounding area around the leveled footprint and clones the raw `m_levelDelta` / `m_modifiedHeight` arrays from every `TerrainComp` chunk in the region via reflection. This snapshot is what `Undo` uses to restore the ground later.

### 4b. Clear blockers in the leveled area
Before leveling, `ClearRocksInPad` removes rock-like blockers (for example `MineRock` / `MineRock5`) intersecting the leveled footprint. It combines collider overlap scans with renderer-bounds fallback so protruding meshes are caught even when pivots sit outside bounds.

### 4c. Level the terrain (`TerrainLeveler.LevelForPlan`)

1. **Pre-sample heights.** Heights are sampled from `ZoneSystem.GetGroundHeight` (terrain heightmap) across the area that may be modified: inner pad (plan bounds + 2-cell buffer) expanded by level-disc radius. This avoids rock/mesh tops biasing `targetY`. Sampling density is `0.5 m` on axis-aligned rotations and `0.25 m` on non-right-angle rotations.

2. **Target height = maxY.** The leveling target is always the *highest* point in the footprint. This means every disc operation only ever *raises* terrain — it never lowers it. When terrain is only raised, the disc falloff at the pad edge slopes *down* to natural terrain, never up. Upward falloff causes spikes at chunk boundaries; by design this approach avoids them entirely.

3. **Multi-pass leveling (configurable).** The leveler runs the number of passes set by **`TerrainLevelPasses`** (clamped to `1–5`, default `2`). This makes convergence quality vs. speed a user-tunable choice instead of being auto-selected from the sampled height range.

4. **Spike cleanup passes (separate config).** After main leveling, the leveler runs a residual peak scan and stamps down any points above `targetY + 0.2 m` using a smaller disc. The number of cleanup scans is controlled independently by **`TerrainSpikeCleanupPasses`** (clamped to `1–5`, default `2`).

5. **`ApplyLevel` stamping.** For each sample point, a temporary `TerrainOp` GameObject is created at that XZ position at `targetY`. Instead of probing only the centre chunk, 9 points (centre + 4 corners + 4 edge midpoints of the disc's bounding box) are checked with `TerrainComp.FindTerrainCompiler` to find every chunk the disc overlaps, including diagonal neighbours. The operation is applied to all found chunks.

6. **Recommended placement wait is derived from touched chunks.** After leveling, `RecommendedPlacementWait` is computed as `max(2.0 s, modifiedChunkCount × 0.5 s)` and logged for visibility.

### 4d. Wait for terrain physics
After leveling, the heightmap data is updated, but the physics collision mesh rebuilds asynchronously. `WaitForTerrainPhysics` polls a 3×3 grid of `Physics.Raycast` probes across the leveled pad every 0.25 s. Once all 9 probes report heights within 0.3 m of `targetY`, or 30 s elapse, the coroutine proceeds. This step is critical: Valheim's structural integrity check uses the physics collider, and pieces placed while the mesh is stale can float and collapse.

### 4e. Place pieces (`PlacePieces`)

For each piece in the plan:

1. Look up its `PieceDef` in `PieceMap`. Unknown types are logged and skipped.
2. Fetch the prefab from `ZNetScene`.
3. Convert the top-left grid cell (col, row) to a local world-space centre offset, then rotate around the chosen build origin by preview rotation:
   ```
   dx = (col + EffW × 0.5) × CELL_SIZE
   dz = (row + EffH × 0.5) × CELL_SIZE

   x = origin.x + dx × cos(rotation) + dz × sin(rotation)
   z = origin.z - dx × sin(rotation) + dz × cos(rotation)
   ```
4. Raycast down from 300 m above `targetY` to get the actual physics terrain height at that XZ. This handles tiny residual undulation left after leveling so each piece lands exactly on the surface.
5. Set `y = terrainY + def.YOffset`.
6. `Instantiate` the prefab, then:
   - Set ZDO owner to the current session ID.
   - Write `vfp_build = "1"` into the piece's ZDO so it can be found by `Undo` in future sessions.
   - Set the `Piece` creator to the player's ID.
7. Yield every 10 pieces (`PLACE_DELAY = 0.05 s`) to keep the game responsive.

External wall stacking: pieces of type `Wall` or `Pillar` whose footprint touches the outer perimeter of the plan are treated as external and stacked vertically to `ExternalWallHeight` levels.

When `WallPillarMaterial=Wood`, `Wall` maps to `wood_wall_half` and `Pillar` maps to `wood_pole2`. External wood walls and pillars are automatically shifted outward to match the outer edge alignment used by stone pieces. For wood walls, `.vfp` `wallFace=inner` flips wall rotation by 180° so inner/outer faces can be controlled from the plan file.

### 4f. Post-build spike guard
After placement, `PostBuildSpikeGuard` runs several delayed scans over the leveled area and removes tall non-piece blockers still protruding above terrain. This catches late-appearing spike meshes that can show up after leveling and placement complete.

---

## Step 5 — Undo

Pressing `F9` calls `FloorPlanBuilder.Undo`:

1. **Remove pieces.** Iterates every active `ZNetView` in the scene. Any ZNetView whose ZDO has `vfp_build = "1"` and is within 75 m of the player is destroyed via `ZNetScene.instance.Destroy`. This works across sessions because the tag is stored in the ZDO (persisted with the world save).

2. **Restore terrain.** `TerrainSnapshot.Restore` writes the saved `m_levelDelta` and `m_modifiedHeight` arrays back into each `TerrainComp` via reflection, then calls the private `Save()` method on each chunk so the change is persisted to the world ZDO.

---

## Coordinate System Summary

```
              +Z (north / row direction)
              ^
              |
  +-----------+-----------> +X (east / col direction)
   origin = preview origin (player position + forward offset by default)
```

- `col` → `+X` world axis
- `row` → `+Z` world axis
- Each cell = 1 m
- Piece positions are **centre-based** in world space, **top-left corner-based** in the `.vfp` file
- Preview rotation is applied clockwise around the selected origin before placement

---


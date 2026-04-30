# ValheimFloorPlan — How It Works

ValheimFloorPlan is a BepInEx mod that reads a design file (`.vfp`), levels the terrain under the build site, and spawns Valheim building pieces to construct a floor plan automatically.

---

## Components

| File | Purpose |
|---|---|
| `ValheimFloorPlanPlugin.cs` | BepInEx plugin entry point; wires hotkeys and config |
| `FloorPlan.cs` | Parses the `.vfp` design file |
| `PieceMap.cs` | Maps design-file piece names to Valheim prefab names and dimensions |
| `TerrainLeveler.cs` | Raises and levels the terrain under the build footprint |
| `TerrainSnapshot.cs` | Captures/restores terrain state for the undo feature |
| `FloorPlanBuilder.cs` | Orchestrates the full build sequence as a Unity coroutine |

---

## Step 1 — Configuration and Hotkeys

On startup (`Awake`), the plugin registers two BepInEx config entries:

- **`FloorPlanFile`** — full path to the `.vfp` file to load.
- **`BuildHotkey`** (default `F8`) — triggers a build at the player's current position.
- **`UndoHotkey`** (default `F9`) — removes all placed pieces and restores terrain.

`Update()` polls those hotkeys every frame and calls `FloorPlanBuilder.BuildFromFile` or `FloorPlanBuilder.Undo` accordingly.

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
| `piece,col,row,type[,rotation]` | One building piece at grid position (col, row) with an optional rotation (0 / 90 / 180 / 270°) |

The grid origin (col=0, row=0) maps to the player's world position when the build hotkey is pressed. Each grid cell is **1 Valheim metre** (`CELL_SIZE = 1f`).

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

When `F8` is pressed, `FloorPlanBuilder.BuildFromFile` launches the coroutine `LevelThenPlace`. All steps are spread across Unity frames to avoid freezing the game.

### 4a. Snapshot terrain
Before any changes, `TerrainSnapshot.Capture` samples the bounding area of the entire plan (including a generous margin for the optional moat) and clones the raw `m_levelDelta` / `m_modifiedHeight` arrays from every `TerrainComp` chunk in the region via reflection. This snapshot is what `Undo` uses to restore the ground later.

### 4b. Level the terrain (`TerrainLeveler.LevelForPlan`)

1. **Pre-sample heights.** The footprint (plan bounding box + 2-cell inner pad buffer) is sampled at 1 m intervals using `Physics.Raycast` against layer 11 (the terrain physics layer) to find the minimum and maximum terrain height (`minY` / `maxY`).

2. **Target height = maxY.** The leveling target is always the *highest* point in the footprint. This means every disc operation only ever *raises* terrain — it never lowers it. When terrain is only raised, the disc falloff at the pad edge slopes *down* to natural terrain, never up. Upward falloff causes spikes at chunk boundaries; by design this approach avoids them entirely.

3. **Multi-pass leveling.** Because Valheim's terrain disc falloff only converges ~67% of the remaining delta per pass, the leveler runs multiple passes. The number of passes scales with the height range:
   - range < 3 m → 2 passes
   - range 3–6 m → 3 passes
   - range 6–10 m → 4 passes
   - range ≥ 10 m → 5 passes

4. **`ApplyLevel` stamping.** For each sample point, a temporary `TerrainOp` GameObject is created at that XZ position at `targetY`. Instead of probing only the centre chunk, 9 points (centre + 4 corners + 4 edge midpoints of the disc's bounding box) are checked with `TerrainComp.FindTerrainCompiler` to find every chunk the disc overlaps — including diagonal neighbours at chunk corners. The operation is applied to all found chunks.

### 4c. Wait for terrain physics
After leveling, the heightmap data is updated but the physics collision mesh rebuilds asynchronously. `WaitForTerrainPhysics` polls a 3×3 grid of `Physics.Raycast` probes across the leveled pad every 0.25 s. Once all 9 probes report heights within 0.3 m of `targetY`, or 30 s elapse, the coroutine proceeds. This step is critical: Valheim's structural integrity check uses the physics collider, and pieces placed while the mesh is stale float and collapse.

### 4d. Place pieces (`PlacePieces`)

For each piece in the plan:

1. Look up its `PieceDef` in `PieceMap`. Unknown types are logged and skipped.
2. Fetch the prefab from `ZNetScene`.
3. Convert the top-left grid cell (col, row) to world-space centre:
   ```
   x = origin.x + (col + EffW × 0.5) × CELL_SIZE
   z = origin.z + (row + EffH × 0.5) × CELL_SIZE
   ```
4. Raycast down from 300 m above `targetY` to get the actual physics terrain height at that XZ. This handles the tiny residual undulation left after leveling so each piece lands exactly on the surface.
5. Set `y = terrainY + def.YOffset`.
6. `Instantiate` the prefab, then:
   - Set ZDO owner to the current session ID.
   - Write `vfp_build = "1"` into the piece's ZDO so it can be found by `Undo` in future sessions.
   - Set the `Piece` creator to the player's ID.
7. Yield every 10 pieces (`PLACE_DELAY = 0.05 s`) to keep the game responsive.

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
  origin = player position when F8 is pressed
```

- `col` → `+X` world axis
- `row` → `+Z` world axis
- Each cell = 1 m
- Piece positions are **centre-based** in world space, **top-left corner-based** in the `.vfp` file

---

## Optional: Moat

`TerrainLeveler.DigMoat` is defined but not called in the default build sequence. It digs a trench ring around the leveled pad: 6-cell gap from the inner pad, 4 cells wide, 2 m below pad level. It uses the same `ApplyLevel` stamp approach but stamps to `targetY − 2 m` and skips points inside the inner boundary.

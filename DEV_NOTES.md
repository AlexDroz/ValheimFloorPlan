# DEV Notes

Project-specific reminders for ValheimFloorPlan.

## Source Of Truth
- Edit Designer/app.js for Designer behavior.
- Edit PieceMap.cs and FloorPlanBuilder.cs for runtime placement behavior.
- Do not treat artifacts/thunderstore/stage as source; packaging copies from the root Designer folder.

## Verified Constraints
- The staircase footprint is 4m x 4m, represented as 4x4 grid cells in both Designer/app.js and PieceMap.cs.
- Staircase spiral steps advance in 20 degree increments.
- The first staircase step starts at the same rotation chosen in the Designer.
- Staircase openings must stay clear through all scaffold floors and beams.

## Scaffold Notes
- Scaffolding beams are handled in FloorPlanBuilder.cs.
- Perimeter beams, top gable support beams, and ridge beams should respect staircase deck openings.
- PlaceScaffoldBeamSpan should keep using the blocked-opening checks so support pieces do not fill the staircase shaft.

## Compatibility Notes
- Target runtime is Valheim/.NET 4.6.2.
- Avoid C# tuple syntax in runtime code paths.
- Prefer explicit loops and small helper types over tuple-heavy LINQ for compatibility.

## Build And Packaging
- The build task is Build & Deploy ValheimFloorPlan.
- Thunderstore packaging recreates artifacts/thunderstore/stage and copies the root Designer folder into the package.

## Last Verified
- 2026-05-26
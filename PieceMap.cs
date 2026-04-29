using System.Collections.Generic;

namespace ValheimFloorPlan
{
    /// <summary>
    /// Defines a Valheim prefab and its B4J grid footprint so the builder can
    /// convert from the top-left grid corner (as stored in .vfp) to the piece
    /// centre (as required by Instantiate).
    ///
    /// Coordinate rules (match the B4J designer exactly):
    ///   - Each grid cell = 1 Valheim metre (CELL_SIZE = 1f).
    ///   - BaseW / BaseH = size in cells at rotation 0.
    ///   - For rotation 90 or 270 the two dimensions are swapped (EffW / EffH).
    ///   - Centre offset = (col + EffW/2) * CELL_SIZE  and  (row + EffH/2) * CELL_SIZE.
    ///   - YOffset = metres above terrain to place the piece centre.
    ///     Floors sit on the ground (0f). Walls/pillars are 2m tall so centre = +1m.
    /// </summary>
    public sealed class PieceDef
    {
        public readonly string Prefab;
        public readonly int    BaseW;    // cell width  at rotation 0
        public readonly int    BaseH;    // cell depth  at rotation 0
        public readonly float  YOffset;  // metres above terrain for piece centre

        public PieceDef(string prefab, int baseW, int baseH, float yOffset = 0f)
        {
            Prefab = prefab; BaseW = baseW; BaseH = baseH; YOffset = yOffset;
        }

        // Apply the same rotation swap the B4J designer uses.
        public int EffW(int rotation) => (rotation == 90 || rotation == 270) ? BaseH : BaseW;
        public int EffH(int rotation) => (rotation == 90 || rotation == 270) ? BaseW : BaseH;
    }

    public static class PieceMap
    {
        // Each B4J grid cell = 1 Valheim metre.
        // (A Floor2x2 is 2 cells × 2 cells = 2 m × 2 m — matches the in-game piece.)
        public const float CELL_SIZE = 1f;

        private static readonly Dictionary<string, PieceDef> Map = new()
        {
            //              vfp type      prefab                  W  H  Yoff
            { "Floor2x2", new PieceDef("wood_floor",             2, 2, 0f) },
            { "Floor1x1", new PieceDef("wood_floor_1x1",         1, 1, 0f) },
            { "Wall",     new PieceDef("stone_wall_2x1",         2, 1, 0.5f) }, // 1 m tall → centre +0.5 m
            { "Doorway",  new PieceDef("wood_door",               2, 1, 1f) },
            { "Pillar",   new PieceDef("stone_pillar",            1, 1, 1f) }, // 2 m tall → centre +1 m
            { "Hearth",   new PieceDef("hearth",                  3, 2, 0f) },
        };

        public static PieceDef? GetDef(string vfpType) =>
            Map.TryGetValue(vfpType, out var def) ? def : null;

        // Kept for backward compat with any callers that only need the name.
        public static string? GetPrefab(string vfpType) => GetDef(vfpType)?.Prefab;
    }
}

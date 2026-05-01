using System.Collections.Generic;
using System.IO;

namespace ValheimFloorPlan
{
    public enum WallFaceMode
    {
        Default,
        Outer,
        Inner
    }

    public class FloorPlanPiece
    {
        public int Col { get; set; }
        public int Row { get; set; }
        public string Type { get; set; } = "";
        public int Rotation { get; set; }
        public WallFaceMode WallFace { get; set; } = WallFaceMode.Default;
    }

    public class FloorPlan
    {
        public int Cols { get; set; }
        public int Rows { get; set; }
        public List<FloorPlanPiece> Pieces { get; } = new();

        public static FloorPlan Load(string path)
        {
            var plan = new FloorPlan();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("cols="))
                    plan.Cols = int.Parse(line.Substring(5));
                else if (line.StartsWith("rows="))
                    plan.Rows = int.Parse(line.Substring(5));
                else if (line.StartsWith("piece,"))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 4) continue;

                    int rotation = 0;
                    int faceIndex = -1;
                    if (parts.Length > 4)
                    {
                        if (int.TryParse(parts[4], out int parsedRotation))
                        {
                            rotation = parsedRotation;
                            faceIndex = parts.Length > 5 ? 5 : -1;
                        }
                        else
                        {
                            // Backward-compatible: allow piece,col,row,type,wallFace
                            faceIndex = 4;
                        }
                    }

                    plan.Pieces.Add(new FloorPlanPiece
                    {
                        Col = int.Parse(parts[1]),
                        Row = int.Parse(parts[2]),
                        Type = parts[3],
                        Rotation = rotation,
                        WallFace = faceIndex >= 0 ? ParseWallFace(parts[faceIndex]) : WallFaceMode.Default
                    });
                }
            }
            return plan;
        }

        private static WallFaceMode ParseWallFace(string raw)
        {
            string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "outer" || value == "out" || value == "o")
                return WallFaceMode.Outer;
            if (value == "inner" || value == "in" || value == "i")
                return WallFaceMode.Inner;
            return WallFaceMode.Default;
        }
    }
}

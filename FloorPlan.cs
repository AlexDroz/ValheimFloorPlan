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

    public class FlexiWallPiece
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float Mx { get; set; }
        public float My { get; set; }
    }

    public class FloorPlan
    {
        public int Cols { get; set; }
        public int Rows { get; set; }
        public List<FloorPlanPiece> Pieces { get; } = new();
        public List<FlexiWallPiece> FlexiWalls { get; } = new();

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
                else if (line.StartsWith("flexiwall,") || line.StartsWith("arcwall,"))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 7) continue;
                    if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x1)) continue;
                    if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y1)) continue;
                    if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x2)) continue;
                    if (!float.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y2)) continue;
                    if (!float.TryParse(parts[5], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float mx)) continue;
                    if (!float.TryParse(parts[6], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float my)) continue;
                    plan.FlexiWalls.Add(new FlexiWallPiece { X1=x1, Y1=y1, X2=x2, Y2=y2, Mx=mx, My=my });
                }
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

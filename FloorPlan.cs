using System.Collections.Generic;
using System.IO;

namespace ValheimFloorPlan
{
    public class FloorPlanPiece
    {
        public int Col { get; set; }
        public int Row { get; set; }
        public string Type { get; set; } = "";
        public int Rotation { get; set; }
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
                    plan.Pieces.Add(new FloorPlanPiece
                    {
                        Col = int.Parse(parts[1]),
                        Row = int.Parse(parts[2]),
                        Type = parts[3],
                        Rotation = parts.Length > 4 ? int.Parse(parts[4]) : 0
                    });
                }
            }
            return plan;
        }
    }
}

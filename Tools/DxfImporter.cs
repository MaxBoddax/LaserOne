using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using LaserOne.Models;

namespace LaserOne.Tools;

public static class DxfImporter
{
    private struct CodePair
    {
        public int Code;
        public string Value;
        public CodePair(int code, string value)
        {
            Code = code;
            Value = value;
        }
    }

    public static List<ShapeModel> Import(string filePath)
    {
        var shapes = new List<ShapeModel>();
        try
        {
            if (!File.Exists(filePath)) return shapes;

            var pairs = ReadCodePairs(filePath);
            shapes = ParseEntities(pairs);

            if (shapes.Count > 0)
            {
                NormalizeShapes(shapes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore importazione DXF custom: {ex.Message}");
        }

        return shapes;
    }

    private static List<CodePair> ReadCodePairs(string filePath)
    {
        var pairs = new List<CodePair>();
        using var reader = new StreamReader(filePath);

        while (!reader.EndOfStream)
        {
            string? codeLine = reader.ReadLine();
            if (codeLine == null) break;

            string? valueLine = reader.ReadLine();
            if (valueLine == null) break;

            // Corretto: rimossa la redundanza e corretti gli argomenti di TryParse
            if (int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                pairs.Add(new CodePair(code, valueLine.Trim()));
            }
        }

        return pairs;
    }

    private static List<ShapeModel> ParseEntities(List<CodePair> pairs)
    {
        var shapes = new List<ShapeModel>();
        int i = 0;
        string currentEntityType = "";
        var currentEntityPairs = new List<CodePair>();

        void FlushEntity()
        {
            if (string.IsNullOrEmpty(currentEntityType)) return;

            if (currentEntityType == "LINE")
            {
                double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                foreach (var p in currentEntityPairs)
                {
                    if (p.Code == 10) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x1);
                    if (p.Code == 20) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y1);
                    if (p.Code == 11) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x2);
                    if (p.Code == 21) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y2);
                }
                shapes.Add(new LineShape { X = x1, Y = y1, X2 = x2, Y2 = y2 });
            }
            else if (currentEntityType == "CIRCLE")
            {
                double cx = 0, cy = 0, r = 0;
                foreach (var p in currentEntityPairs)
                {
                    if (p.Code == 10) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx);
                    if (p.Code == 20) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy);
                    if (p.Code == 40) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
                }
                if (r > 0 && r <= 200.0)
                {
                    shapes.Add(new CircleShape { X = cx, Y = cy, Radius = r });
                }
            }
            else if (currentEntityType == "ARC")
            {
                double cx = 0, cy = 0, r = 0, startAng = 0, endAng = 0;
                foreach (var p in currentEntityPairs)
                {
                    if (p.Code == 10) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out cx);
                    if (p.Code == 20) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out cy);
                    if (p.Code == 40) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
                    if (p.Code == 50) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out startAng);
                    if (p.Code == 51) double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out endAng);
                }

                if (r > 0)
                {
                    double startRad = startAng * Math.PI / 180.0;
                    double endRad = endAng * Math.PI / 180.0;
                    if (endRad <= startRad) endRad += 2 * Math.PI;
                    int steps = 24;
                    double stepAngle = (endRad - startRad) / steps;
                    for (int s = 0; s < steps; s++)
                    {
                        double a1 = startRad + s * stepAngle;
                        double a2 = startRad + (s + 1) * stepAngle;
                        shapes.Add(new LineShape
                        {
                            X = cx + r * Math.Cos(a1),
                            Y = cy + r * Math.Sin(a1),
                            X2 = cx + r * Math.Cos(a2),
                            Y2 = cy + r * Math.Sin(a2)
                        });
                    }
                }
            }
            else if (currentEntityType == "LWPOLYLINE")
            {
                var pts = new List<(double X, double Y)>();
                double? curX = null;
                bool isClosed = false;

                foreach (var p in currentEntityPairs)
                {
                    if (p.Code == 70)
                    {
                        if (int.TryParse(p.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
                        {
                            isClosed = (flags & 1) == 1;
                        }
                    }
                    else if (p.Code == 10)
                    {
                        curX = null;
                        if (double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                        {
                            curX = x;
                        }
                    }
                    else if (p.Code == 20)
                    {
                        if (curX.HasValue && double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                        {
                            pts.Add((curX.Value, y));
                            curX = null;
                        }
                    }
                }

                if (pts.Count >= 2)
                {
                    for (int idx = 0; idx < pts.Count - 1; idx++)
                    {
                        shapes.Add(new LineShape
                        {
                            X = pts[idx].X,
                            Y = pts[idx].Y,
                            X2 = pts[idx + 1].X,
                            Y2 = pts[idx + 1].Y
                        });
                    }

                    if (isClosed && pts.Count > 2)
                    {
                        shapes.Add(new LineShape
                        {
                            X = pts[^1].X,
                            Y = pts[^1].Y,
                            X2 = pts[0].X,
                            Y2 = pts[0].Y
                        });
                    }
                }
            }

            currentEntityType = "";
            currentEntityPairs.Clear();
        }

        while (i < pairs.Count)
        {
            var p = pairs[i];
            if (p.Code == 0)
            {
                FlushEntity();
                currentEntityType = p.Value.ToUpperInvariant();
            }
            else if (!string.IsNullOrEmpty(currentEntityType))
            {
                currentEntityPairs.Add(p);
            }
            i++;
        }
        FlushEntity();

        return shapes;
    }

    private static void NormalizeShapes(List<ShapeModel> shapes)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;

        foreach (var shape in shapes)
        {
            if (shape is LineShape line)
            {
                minX = Math.Min(minX, Math.Min(line.X, line.X2));
                minY = Math.Min(minY, Math.Min(line.Y, line.Y2));
            }
            else if (shape is CircleShape circle)
            {
                minX = Math.Min(minX, circle.X - circle.Radius);
                minY = Math.Min(minY, circle.Y - circle.Radius);
            }
        }

        double offsetX = -minX + 5.0;
        double offsetY = -minY + 5.0;

        foreach (var shape in shapes)
        {
            if (shape is LineShape line)
            {
                line.X += offsetX;
                line.Y += offsetY;
                line.X2 += offsetX;
                line.Y2 += offsetY;
            }
            else if (shape is CircleShape circle)
            {
                circle.X += offsetX;
                circle.Y += offsetY;
            }
        }
    }
}
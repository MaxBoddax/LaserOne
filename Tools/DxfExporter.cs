using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LaserOne.Models;

namespace LaserOne.Tools;

public static class DxfExporter
{
    public static void Export(string filePath, IEnumerable<ShapeModel> shapes)
    {
        using var writer = new StreamWriter(filePath);

        // Sezione Header DXF standard
        writer.WriteLine("  0");
        writer.WriteLine("SECTION");
        writer.WriteLine("  2");
        writer.WriteLine("HEADER");
        writer.WriteLine("  0");
        writer.WriteLine("ENDSEC");

        // Sezione Tables DXF standard
        writer.WriteLine("  0");
        writer.WriteLine("SECTION");
        writer.WriteLine("  2");
        writer.WriteLine("TABLES");
        writer.WriteLine("  0");
        writer.WriteLine("ENDSEC");

        // Sezione Entities DXF (Contiene le geometrie)
        writer.WriteLine("  0");
        writer.WriteLine("SECTION");
        writer.WriteLine("  2");
        writer.WriteLine("ENTITIES");

        CultureInfo inv = CultureInfo.InvariantCulture;

        foreach (var shape in shapes)
        {
            if (shape is LineShape line)
            {
                writer.WriteLine("  0");
                writer.WriteLine("LINE");
                writer.WriteLine("  8");
                writer.WriteLine("LaserOne");
                writer.WriteLine($" 10\n{line.X.ToString(inv)}");
                writer.WriteLine($" 20\n{line.Y.ToString(inv)}");
                writer.WriteLine($" 30\n0.0");
                writer.WriteLine($" 11\n{line.X2.ToString(inv)}");
                writer.WriteLine($" 21\n{line.Y2.ToString(inv)}");
                writer.WriteLine($" 31\n0.0");
            }
            else if (shape is RectangleShape rect)
            {
                // Esportiamo il rettangolo come 4 linee perimetrali
                double x1 = rect.X;
                double y1 = rect.Y;
                double x2 = rect.X + rect.Width;
                double y2 = rect.Y + rect.Height;

                WriteDxfLine(writer, x1, y1, x2, y1, inv); // Top
                WriteDxfLine(writer, x2, y1, x2, y2, inv); // Right
                WriteDxfLine(writer, x2, y2, x1, y2, inv); // Bottom
                WriteDxfLine(writer, x1, y2, x1, y1, inv); // Left
            }
            else if (shape is CircleShape circle)
            {
                writer.WriteLine("  0");
                writer.WriteLine("CIRCLE");
                writer.WriteLine("  8");
                writer.WriteLine("LaserOne");
                writer.WriteLine($" 10\n{circle.X.ToString(inv)}");
                writer.WriteLine($" 20\n{circle.Y.ToString(inv)}");
                writer.WriteLine($" 30\n0.0");
                writer.WriteLine($" 40\n{circle.Radius.ToString(inv)}");
            }
        }

        writer.WriteLine("  0");
        writer.WriteLine("ENDSEC");
        writer.WriteLine("  0");
        writer.WriteLine("EOF");
    }

    private static void WriteDxfLine(StreamWriter writer, double x1, double y1, double x2, double y2, CultureInfo inv)
    {
        writer.WriteLine("  0");
        writer.WriteLine("LINE");
        writer.WriteLine("  8");
        writer.WriteLine("LaserOne");
        writer.WriteLine($" 10\n{x1.ToString(inv)}");
        writer.WriteLine($" 20\n{y1.ToString(inv)}");
        writer.WriteLine($" 30\n0.0");
        writer.WriteLine($" 11\n{x2.ToString(inv)}");
        writer.WriteLine($" 21\n{y2.ToString(inv)}");
        writer.WriteLine($" 31\n0.0");
    }
}
using System.Collections.Generic;
using System.Globalization;
using LaserOne.Models;

namespace LaserOne.Tools;

public static class GCodeGenerator
{
    public static List<string> Generate(IEnumerable<ShapeModel> shapes, double laserPower = 1000, double feedRate = 1000)
    {
        var gcode = new List<string>
        {
            "; --- Generato da LaserOne v0.5 ---",
            "G21", // Unità in millimetri
            "G90", // Posizionamento assoluto
            "M4 S0" // Laser armato / spento
        };

        foreach (var shape in shapes)
        {
            if (shape is RectangleShape rect)
            {
                gcode.Add($"; Rettangolo X:{rect.X} Y:{rect.Y} W:{rect.Width} H:{rect.Height}");
                gcode.Add($"G0 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)}");
                gcode.Add($"M3 S{laserPower}");
                gcode.Add($"G1 X{(rect.X + rect.Width).ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)} F{feedRate}");
                gcode.Add($"G1 X{(rect.X + rect.Width).ToString(CultureInfo.InvariantCulture)} Y{(rect.Y + rect.Height).ToString(CultureInfo.InvariantCulture)}");
                gcode.Add($"G1 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{(rect.Y + rect.Height).ToString(CultureInfo.InvariantCulture)}");
                gcode.Add($"G1 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)}");
                gcode.Add("M5");
            }
            else if (shape is LineShape line)
            {
                gcode.Add($"; Linea");
                gcode.Add($"G0 X{line.X.ToString(CultureInfo.InvariantCulture)} Y{line.Y.ToString(CultureInfo.InvariantCulture)}");
                gcode.Add($"M3 S{laserPower}");
                gcode.Add($"G1 X{line.X2.ToString(CultureInfo.InvariantCulture)} Y{line.Y2.ToString(CultureInfo.InvariantCulture)} F{feedRate}");
                gcode.Add("M5");
            }
            else if (shape is CircleShape circle)
            {
                gcode.Add($"; Cerchio X:{circle.X} Y:{circle.Y} R:{circle.Radius}");
                int segments = 36; // Approssimazione del cerchio a segmenti
                bool first = true;
                for (int i = 0; i <= segments; i++)
                {
                    double angle = i * (2 * System.Math.PI / segments);
                    double cx = circle.X + circle.Radius * System.Math.Cos(angle);
                    double cy = circle.Y + circle.Radius * System.Math.Sin(angle);

                    if (first)
                    {
                        gcode.Add($"G0 X{cx.ToString(CultureInfo.InvariantCulture)} Y{cy.ToString(CultureInfo.InvariantCulture)}");
                        gcode.Add($"M3 S{laserPower}");
                        first = false;
                    }
                    else
                    {
                        gcode.Add($"G1 X{cx.ToString(CultureInfo.InvariantCulture)} Y{cy.ToString(CultureInfo.InvariantCulture)} F{feedRate}");
                    }
                }
                gcode.Add("M5");
            }
        }

        gcode.Add("G0 X0 Y0"); // Ritorno in origine
        gcode.Add("M30");      // Fine programma
        return gcode;
    }
}
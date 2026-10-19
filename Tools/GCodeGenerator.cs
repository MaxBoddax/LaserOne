using System.Collections.Generic;
using System.Globalization;
using LaserOne.Models;

namespace LaserOne.Tools;

public static class GCodeGenerator
{
    public static List<string> Generate(IEnumerable<ShapeModel> shapes)
    {
        var gcode = new List<string>
        {
            "; --- Generato da LaserOne v1.0 ---",
            "G21", // Unità in millimetri
            "G90", // Posizionamento assoluto
            "M4 S0" // Laser armato / spento
        };

        foreach (var shape in shapes)
        {
            string modeStr = shape.Mode == LaserMode.Cut ? "Taglio" : "Incisione";
            gcode.Add($"; Formato: {modeStr} | Potenza: {shape.Power} | Velocità: {shape.Speed} | Passate: {shape.Passes}");

            // Gestione delle passate multiple (Passes)
            for (int p = 0; p < shape.Passes; p++)
            {
                if (shape.Passes > 1)
                {
                    gcode.Add($"; -- Passata {p + 1} di {shape.Passes} --");
                }

                if (shape is RectangleShape rect)
                {
                    gcode.Add($"G0 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"M3 S{shape.Power.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"G1 X{(rect.X + rect.Width).ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)} F{shape.Speed.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"G1 X{(rect.X + rect.Width).ToString(CultureInfo.InvariantCulture)} Y{(rect.Y + rect.Height).ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"G1 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{(rect.Y + rect.Height).ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"G1 X{rect.X.ToString(CultureInfo.InvariantCulture)} Y{rect.Y.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add("M5");
                }
                else if (shape is LineShape line)
                {
                    gcode.Add($"G0 X{line.X.ToString(CultureInfo.InvariantCulture)} Y{line.Y.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"M3 S{shape.Power.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add($"G1 X{line.X2.ToString(CultureInfo.InvariantCulture)} Y{line.Y2.ToString(CultureInfo.InvariantCulture)} F{shape.Speed.ToString(CultureInfo.InvariantCulture)}");
                    gcode.Add("M5");
                }
                else if (shape is CircleShape circle)
                {
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
                            gcode.Add($"M3 S{shape.Power.ToString(CultureInfo.InvariantCulture)}");
                            first = false;
                        }
                        else
                        {
                            gcode.Add($"G1 X{cx.ToString(CultureInfo.InvariantCulture)} Y{cy.ToString(CultureInfo.InvariantCulture)} F{shape.Speed.ToString(CultureInfo.InvariantCulture)}");
                        }
                    }
                    gcode.Add("M5");
                }
            }
        }

        gcode.Add("G0 X0 Y0"); // Ritorno in origine
        gcode.Add("M30");      // Fine programma
        return gcode;
    }
}
using System;
using System.IO;

namespace LaserOne.Models;

public static class CncConfig
{
    private static readonly string IniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

    public static string SelectedBrand { get; set; } = "Sculpfun";
    public static string SelectedModel { get; set; } = "Sculpfun S30 Pro Max";
    public static double WorkAreaWidth { get; set; } = 400.0;
    public static double WorkAreaHeight { get; set; } = 400.0;

    public static void Load()
    {
        if (!File.Exists(IniPath)) return;

        foreach (var line in File.ReadAllLines(IniPath))
        {
            var parts = line.Split('=');
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var val = parts[1].Trim();

            if (key == "Brand") SelectedBrand = val;
            if (key == "Model") SelectedModel = val;
            if (key == "WorkAreaWidth" && double.TryParse(val, out var w)) WorkAreaWidth = w;
            if (key == "WorkAreaHeight" && double.TryParse(val, out var h)) WorkAreaHeight = h;
        }
    }

    public static void Save()
    {
        var lines = new[]
        {
            $"Brand={SelectedBrand}",
            $"Model={SelectedModel}",
            $"WorkAreaWidth={WorkAreaWidth}",
            $"WorkAreaHeight={WorkAreaHeight}"
        };
        File.WriteAllLines(IniPath, lines);
    }
}
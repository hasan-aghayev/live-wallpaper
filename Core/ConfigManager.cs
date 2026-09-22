using System;
using System.IO;
using System.Text.Json;

namespace LiveWallpaper.Core;

public class WallpaperConfig
{
    public string? LastVideoPath { get; set; }
    public double Volume { get; set; } = 0.0;
    public bool IsMuted { get; set; } = true;
    public string StretchMode { get; set; } = "Fill"; // Fill, Fit, Stretch
    public bool AutoStart { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoPlayOnLaunch { get; set; } = true;
    public bool EnableOverlay { get; set; } = false;
    public double OverlayOpacity { get; set; } = 0.35;
    public string OverlayColor { get; set; } = "#000000";
}

public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public static WallpaperConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<WallpaperConfig>(json);
                if (config != null)
                    return config;
            }
        }
        catch (Exception)
        {
            // Fallback to default
        }

        return new WallpaperConfig();
    }

    public static void Save(WallpaperConfig config)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception)
        {
            // Ignore write errors if permission denied
        }
    }
}

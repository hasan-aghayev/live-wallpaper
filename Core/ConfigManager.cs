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
    private static readonly object _saveLock = new();
    private static System.Threading.Timer? _debounceTimer;
    private static WallpaperConfig? _pendingConfig;

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
        lock (_saveLock)
        {
            _pendingConfig = config;
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                SaveImmediately();
            }, null, 400, System.Threading.Timeout.Infinite);
        }
    }

    public static void SaveImmediately()
    {
        lock (_saveLock)
        {
            if (_pendingConfig == null)
                return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_pendingConfig, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception)
            {
                // Ignore write errors if permission denied
            }
        }
    }
}

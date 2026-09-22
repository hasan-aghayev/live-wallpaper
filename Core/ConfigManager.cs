using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace LiveWallpaper.Core;

public class WallpaperConfig
{
    public int SchemaVersion { get; set; } = 1;
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
    private static readonly object _saveLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static System.Threading.Timer? _debounceTimer;
    private static string? _pendingJson;

    public static WallpaperConfig Load()
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            if (File.Exists(AppPaths.ConfigFilePath))
            {
                string json = File.ReadAllText(AppPaths.ConfigFilePath);
                var config = JsonSerializer.Deserialize<WallpaperConfig>(json, JsonOptions);
                if (config != null)
                    return Normalize(config);
            }
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"ConfigManager.Load failed: {ex.Message}. Using defaults.");
        }

        return new WallpaperConfig();
    }

    public static void Save(WallpaperConfig config)
    {
        string json = JsonSerializer.Serialize(Normalize(config), JsonOptions);
        lock (_saveLock)
        {
            _pendingJson = json;
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
            if (_pendingJson == null)
                return;

            string json = _pendingJson;
            _pendingJson = null;

            try
            {
                AppPaths.EnsureDataDirectories();
                string tempPath = AppPaths.ConfigFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, AppPaths.ConfigFilePath, true);
            }
            catch (Exception ex)
            {
                DesktopManager.Log($"ConfigManager.Save failed: {ex.Message}");
            }
        }
    }

    private static WallpaperConfig Normalize(WallpaperConfig config)
    {
        config.SchemaVersion = 1;
        config.Volume = Math.Clamp(double.IsFinite(config.Volume) ? config.Volume : 0.0, 0.0, 1.0);
        config.OverlayOpacity = Math.Clamp(
            double.IsFinite(config.OverlayOpacity) ? config.OverlayOpacity : 0.35,
            0.0,
            0.9);
        config.StretchMode = config.StretchMode is "Fill" or "Fit" or "Stretch"
            ? config.StretchMode
            : "Fill";
        if (!string.IsNullOrWhiteSpace(config.LastVideoPath))
        {
            try { config.LastVideoPath = Path.GetFullPath(config.LastVideoPath); }
            catch { config.LastVideoPath = null; }
        }

        if (string.IsNullOrWhiteSpace(config.OverlayColor)
            || !config.OverlayColor.StartsWith("#", StringComparison.Ordinal)
            || config.OverlayColor.Length != 7)
        {
            config.OverlayColor = "#000000";
        }

        return config;
    }
}

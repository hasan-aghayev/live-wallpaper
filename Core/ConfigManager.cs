using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LiveWallpaper.Core;

public class WallpaperConfig
{
    public int SchemaVersion { get; set; } = 2;
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
    public bool PauseWhenHidden { get; set; } = true;
    public int HiddenResourceReleaseSeconds { get; set; } = 60;
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
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (_pendingJson == null)
                return;

            string json = _pendingJson;
            string tempPath = AppPaths.ConfigFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                AppPaths.EnsureDataDirectories();
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, AppPaths.ConfigFilePath, true);
                _pendingJson = null;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                // Preserve the latest settings so a later save or app
                // shutdown can retry instead of silently losing the changes.
                DesktopManager.Log($"ConfigManager.Save failed: {ex.Message}");
            }
        }
    }

    private static WallpaperConfig Normalize(WallpaperConfig config)
    {
        config.SchemaVersion = 2;
        config.Volume = Math.Clamp(double.IsFinite(config.Volume) ? config.Volume : 0.0, 0.0, 1.0);
        config.OverlayOpacity = Math.Clamp(
            double.IsFinite(config.OverlayOpacity) ? config.OverlayOpacity : 0.35,
            0.0,
            0.9);
        config.StretchMode = config.StretchMode is "Fill" or "Fit" or "Stretch"
            ? config.StretchMode
            : "Fill";
        config.HiddenResourceReleaseSeconds = config.HiddenResourceReleaseSeconds is 0 or 30 or 60 or 300
            ? config.HiddenResourceReleaseSeconds
            : 60;
        if (!string.IsNullOrWhiteSpace(config.LastVideoPath))
        {
            try { config.LastVideoPath = Path.GetFullPath(config.LastVideoPath); }
            catch { config.LastVideoPath = null; }
        }

        // Overlay color is intentionally fixed to black. Keep the property in
        // the config model for backwards compatibility with older releases.
        config.OverlayColor = "#000000";

        return config;
    }
}

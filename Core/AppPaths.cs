using System;
using System.IO;

namespace LiveWallpaper.Core;

/// <summary>
/// Centralized locations used by the application at runtime.
/// Keeping user data outside the installation folder makes the app work from
/// Program Files and from a read-only portable folder.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "HaS Live Wallpaper";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaS Studio",
        "Live Wallpaper");

    public static string ConfigFilePath => Path.Combine(DataDirectory, "config.json");
    public static string LogFilePath => Path.Combine(DataDirectory, "debug.log");
    public static string ShaderDirectory => Path.Combine(DataDirectory, "shaders");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ShaderDirectory);
    }
}

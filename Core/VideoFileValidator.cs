using System;
using System.Collections.Generic;
using System.IO;

namespace LiveWallpaper.Core;

public static class VideoFileValidator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".flv", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg",
        ".ogv", ".ts", ".webm", ".wmv"
    };

    public static bool IsSupported(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static string SupportedExtensionsDescription =>
        string.Join(", ", SupportedExtensions);
}

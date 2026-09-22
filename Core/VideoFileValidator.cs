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
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return File.Exists(path)
                && SupportedExtensions.Contains(Path.GetExtension(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    public static string SupportedExtensionsDescription =>
        string.Join(", ", SupportedExtensions);
}

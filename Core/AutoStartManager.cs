using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LiveWallpaper.Core;

public static class AutoStartManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LiveWallpaperLauncher";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            if (key != null)
            {
                var val = key.GetValue(AppName);
                return val != null;
            }
        }
        catch (Exception)
        {
            // Ignored
        }
        return false;
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
                return;

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                                 ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiveWallpaper.exe");
                key.SetValue(AppName, $"\"{exePath}\" --background");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception)
        {
            // Ignored
        }
    }
}

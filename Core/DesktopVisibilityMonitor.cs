using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LiveWallpaper.Core;

/// <summary>
/// Detects whether the desktop is currently visible enough to keep rendering
/// the wallpaper. The check is intentionally conservative on multi-monitor
/// systems: a fullscreen window on one monitor does not pause the wallpaper
/// shown on another monitor.
/// </summary>
public static class DesktopVisibilityMonitor
{
    private const int MonitorDefaultToNearest = 2;
    private const int SystemMetricVirtualScreenLeft = 76;
    private const int SystemMetricVirtualScreenTop = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;
    private const int SystemMetricMonitorCount = 80;
    private const int DwmWindowAttributeCloaked = 14;

    private static readonly IntPtr HResultOk = IntPtr.Zero;

    public static bool IsDesktopVisible()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return true;

        if (IsDesktopShellWindow(foregroundWindow) || IsWindowCloaked(foregroundWindow))
            return true;

        return !CoversEntireVirtualDesktop(foregroundWindow);
    }

    private static bool IsDesktopShellWindow(IntPtr windowHandle)
    {
        if (windowHandle == GetShellWindow() || windowHandle == GetDesktopWindow())
            return true;

        var className = new StringBuilder(128);
        _ = GetClassName(windowHandle, className, className.Capacity);

        return className.ToString() switch
        {
            "Progman" => true,
            "WorkerW" => true,
            "Shell_TrayWnd" => true,
            "Shell_SecondaryTrayWnd" => true,
            "NotifyIconOverflowWindow" => true,
            _ => false
        };
    }

    private static bool CoversEntireVirtualDesktop(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out Rect windowRect))
            return false;

        int monitorCount = GetSystemMetrics(SystemMetricMonitorCount);
        if (monitorCount > 1)
        {
            var virtualDesktop = new Rect
            {
                Left = GetSystemMetrics(SystemMetricVirtualScreenLeft),
                Top = GetSystemMetrics(SystemMetricVirtualScreenTop),
                Right = GetSystemMetrics(SystemMetricVirtualScreenLeft)
                    + GetSystemMetrics(SystemMetricVirtualScreenWidth),
                Bottom = GetSystemMetrics(SystemMetricVirtualScreenTop)
                    + GetSystemMetrics(SystemMetricVirtualScreenHeight)
            };

            return Contains(virtualDesktop, windowRect);
        }

        IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
            return false;

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitorHandle, ref monitorInfo)
            && Contains(monitorInfo.WorkArea, windowRect);
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        const int tolerance = 2;
        return inner.Left <= outer.Left + tolerance
            && inner.Top <= outer.Top + tolerance
            && inner.Right >= outer.Right - tolerance
            && inner.Bottom >= outer.Bottom - tolerance;
    }

    private static bool IsWindowCloaked(IntPtr windowHandle)
    {
        int cloaked = 0;
        int result = DwmGetWindowAttribute(
            windowHandle,
            DwmWindowAttributeCloaked,
            ref cloaked,
            Marshal.SizeOf<int>());

        return result == HResultOk.ToInt32() && cloaked != 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;
    }
}

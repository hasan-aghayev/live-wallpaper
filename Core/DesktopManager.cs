using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace LiveWallpaper.Core;

public static class DesktopManager
{
    private const uint WM_SPAWN_WORKER = 0x052C;
    private const int SMTO_NORMAL = 0x0000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public static (int X, int Y, int Width, int Height) GetDesktopBounds()
    {
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            x = 0;
            y = 0;
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
        }

        if (width <= 0 || height <= 0)
        {
            x = (int)SystemParameters.VirtualScreenLeft;
            y = (int)SystemParameters.VirtualScreenTop;
            width = (int)SystemParameters.VirtualScreenWidth;
            height = (int)SystemParameters.VirtualScreenHeight;
        }

        return (x, y, width, height);
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private const uint SPI_SETDESKWALLPAPER = 20;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;

    private static readonly BlockingCollection<string> _logQueue = new(new ConcurrentQueue<string>());
    private static readonly Task _logWriterTask;

    static DesktopManager()
    {
        _logWriterTask = Task.Run(() =>
        {
            try
            {
                AppPaths.EnsureDataDirectories();
                foreach (var line in _logQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        File.AppendAllText(AppPaths.LogFilePath, line + Environment.NewLine);
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    public static void Log(string message)
    {
        try
        {
            if (!_logQueue.IsAddingCompleted)
            {
                _logQueue.TryAdd($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            }
        }
        catch { }
    }

    public static void FlushLogs()
    {
        try
        {
            if (!_logQueue.IsAddingCompleted)
            {
                _logQueue.CompleteAdding();
            }

            _logWriterTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
    }

    public static bool AttachToDesktop(IntPtr windowHwnd, out IntPtr parentHwnd)
    {
        parentHwnd = IntPtr.Zero;
        Log($"=== AttachToDesktop started for HWND 0x{windowHwnd.ToInt64():X} ===");

        IntPtr hDesk = OpenDesktop("default", 0, false, 0x01FF);
        Log($"OpenDesktop('default') returned 0x{hDesk.ToInt64():X}");

        try
        {
            IntPtr progman = IntPtr.Zero;

            // 1. Locate Progman
            if (hDesk != IntPtr.Zero)
            {
                EnumDesktopWindows(hDesk, (hwnd, lparam) =>
                {
                    var sb = new StringBuilder(256);
                    GetClassName(hwnd, sb, 256);
                    if (sb.ToString() == "Progman")
                    {
                        progman = hwnd;
                    }
                    return true;
                }, IntPtr.Zero);
            }

            if (progman == IntPtr.Zero)
            {
                progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero)
                    progman = FindWindow("Progman", "Program Manager");
            }

            Log($"Resolved Progman HWND: 0x{progman.ToInt64():X}");

            if (progman == IntPtr.Zero)
            {
                Log("ERROR: Progman could not be located!");
                return false;
            }

            // 2. Send 0x052C to Progman
            IntPtr res;
            SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out res);
            SendMessageTimeout(progman, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out res);
            Log($"Sent 0x052C to Progman");

            // 3. Find SHELLDLL_DefView and WorkerW inside Progman (Windows 11 24H2)
            IntPtr defView = IntPtr.Zero;
            IntPtr workerW = IntPtr.Zero;

            EnumChildWindows(progman, (hwnd, lparam) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(hwnd, sb, 256);
                string cls = sb.ToString();
                if (cls == "SHELLDLL_DefView")
                    defView = hwnd;
                else if (cls == "WorkerW")
                    workerW = hwnd;
                return true;
            }, IntPtr.Zero);

            Log($"Progman children scan -> SHELLDLL_DefView: 0x{defView.ToInt64():X}, WorkerW: 0x{workerW.ToInt64():X}");

            var bounds = GetDesktopBounds();
            int x = bounds.X;
            int y = bounds.Y;
            int width = bounds.Width;
            int height = bounds.Height;

            if (defView != IntPtr.Zero)
            {
                // *** Windows 11 24H2 MODE ***
                Log($"Activating Windows 11 24H2 attachment mode. Screen: ({x}, {y}, {width}x{height})");

                // Style adjustments
                int style = GetWindowLong(windowHwnd, GWL_STYLE);
                style = (style & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME) | WS_CHILD | WS_VISIBLE;
                SetWindowLong(windowHwnd, GWL_STYLE, style);

                int exStyle = GetWindowLong(windowHwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                SetWindowLong(windowHwnd, GWL_EXSTYLE, exStyle);
                SetLayeredWindowAttributes(windowHwnd, 0, 255, LWA_ALPHA);

                // Parent directly to Progman
                SetParent(windowHwnd, progman);
                parentHwnd = progman;
                Log($"Window parented to Progman (0x{progman.ToInt64():X})");

                // Position right below SHELLDLL_DefView (icon layer)
                SetWindowPos(windowHwnd, defView, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                Log($"Window positioned below SHELLDLL_DefView");

                return true;
            }

            // *** Classic Windows 10 / pre-24H2 MODE ***
            Log("Falling back to classic WorkerW scan");
            IntPtr classicWorkerW = IntPtr.Zero;

            if (hDesk != IntPtr.Zero)
            {
                EnumDesktopWindows(hDesk, (tophandle, topparamhandle) =>
                {
                    IntPtr dv = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero)
                    {
                        classicWorkerW = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            if (classicWorkerW == IntPtr.Zero)
            {
                EnumWindows((tophandle, topparamhandle) =>
                {
                    IntPtr dv = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero)
                    {
                        classicWorkerW = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            Log($"Classic WorkerW handle: 0x{classicWorkerW.ToInt64():X}");

            if (classicWorkerW != IntPtr.Zero)
            {
                SetParent(windowHwnd, classicWorkerW);
                parentHwnd = classicWorkerW;

                int style = GetWindowLong(windowHwnd, GWL_STYLE);
                style = (style & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME) | WS_CHILD | WS_VISIBLE;
                SetWindowLong(windowHwnd, GWL_STYLE, style);

                int exStyle = GetWindowLong(windowHwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                SetWindowLong(windowHwnd, GWL_EXSTYLE, exStyle);

                SetWindowPos(windowHwnd, HWND_BOTTOM, x, y, width, height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
                Log("Successfully attached to classic WorkerW");
                return true;
            }

            // Emergency Fallback
            SetParent(windowHwnd, progman);
            parentHwnd = progman;
            SetWindowPos(windowHwnd, HWND_BOTTOM, x, y, width, height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
            Log("Attached to Progman via emergency fallback");
            return true;
        }
        finally
        {
            if (hDesk != IntPtr.Zero)
            {
                CloseDesktop(hDesk);
            }
        }
    }

    public static void DetachFromDesktop(IntPtr windowHwnd)
    {
        if (windowHwnd == IntPtr.Zero)
            return;

        Log($"Detaching window 0x{windowHwnd.ToInt64():X} from desktop");
        SetParent(windowHwnd, IntPtr.Zero);
    }

    public static void RefreshDesktop()
    {
        Log("Refreshing Windows desktop wallpaper");
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }
    }
}

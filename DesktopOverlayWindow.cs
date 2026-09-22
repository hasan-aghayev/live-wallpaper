using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LiveWallpaper.Core;

namespace LiveWallpaper;

public class DesktopOverlayWindow : Form
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    public DesktopOverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopLevel = false;

        var bounds = DesktopManager.GetDesktopBounds();
        Location = new Point(bounds.X, bounds.Y);
        Size = new Size(bounds.Width, bounds.Height);
        BackColor = Color.Black;
    }

    public bool AttachToDesktop(IntPtr wallpaperHwnd)
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
        }

        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return false;

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
            return false;

        var bounds = DesktopManager.GetDesktopBounds();

        // 1. Style overlay as layered transparent child
        int style = GetWindowLong(Handle, GWL_STYLE);
        style = (style & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME) | WS_CHILD | WS_VISIBLE;
        SetWindowLong(Handle, GWL_STYLE, style);

        int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(Handle, GWL_EXSTYLE, exStyle);

        // 2. Parent overlay to Progman
        SetParent(Handle, progman);

        // 3. Position overlay directly behind defView
        SetWindowPos(Handle, defView, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        // 4. Position wallpaper directly behind overlay
        if (wallpaperHwnd != IntPtr.Zero)
        {
            SetWindowPos(wallpaperHwnd, Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        DesktopManager.Log($"DesktopOverlayWindow attached under DefView. Wallpaper HWND 0x{wallpaperHwnd.ToInt64():X} placed behind overlay.");
        return true;
    }

    public void SetOpacity(double opacity)
    {
        if (IsHandleCreated && Handle != IntPtr.Zero)
        {
            byte alpha = (byte)Math.Clamp((int)(opacity * 255.0), 0, 255);
            SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
        }
    }

    public void SetColor(Color color)
    {
        BackColor = color;
    }

    public void UpdateOverlay(bool isEnabled, Color color, double opacity, IntPtr wallpaperHwnd)
    {
        if (!isEnabled || opacity <= 0.001)
        {
            if (IsHandleCreated && Handle != IntPtr.Zero)
            {
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOMOVE | SWP_NOSIZE);
            }
            if (wallpaperHwnd != IntPtr.Zero)
            {
                IntPtr progman = FindWindow("Progman", null);
                IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    var bounds = DesktopManager.GetDesktopBounds();
                    SetWindowPos(wallpaperHwnd, defView, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            }
            return;
        }

        BackColor = color;
        AttachToDesktop(wallpaperHwnd);
        SetOpacity(opacity);
    }
}

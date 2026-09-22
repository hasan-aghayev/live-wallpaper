using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiveWallpaper.Core;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _togglePlayMenuItem;
    private readonly ToolStripMenuItem _toggleMuteMenuItem;

    public event Action? OnOpenRequested;
    public event Action? OnTogglePlayRequested;
    public event Action? OnToggleMuteRequested;
    public event Action? OnStopRequested;
    public event Action? OnExitRequested;

    public TrayManager()
    {
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("🖥 Open Launcher");
        openItem.Click += (s, e) => OnOpenRequested?.Invoke();
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        _togglePlayMenuItem = new ToolStripMenuItem("⏸ Pause");
        _togglePlayMenuItem.Click += (s, e) => OnTogglePlayRequested?.Invoke();

        _toggleMuteMenuItem = new ToolStripMenuItem("🔊 Unmute");
        _toggleMuteMenuItem.Click += (s, e) => OnToggleMuteRequested?.Invoke();

        var stopItem = new ToolStripMenuItem("⏹ Stop Wallpaper");
        stopItem.Click += (s, e) => OnStopRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("❌ Exit");
        exitItem.Click += (s, e) => OnExitRequested?.Invoke();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_togglePlayMenuItem);
        contextMenu.Items.Add(_toggleMuteMenuItem);
        contextMenu.Items.Add(stopItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        Icon appIcon = SystemIcons.Application;
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appIcon = new Icon(iconPath, SystemInformation.SmallIconSize);
            }
            else if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                var exeIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (exeIcon != null)
                {
                    appIcon = exeIcon;
                }
            }
        }
        catch { }

        _notifyIcon = new NotifyIcon
        {
            Text = "Live Wallpaper Launcher",
            Icon = appIcon,
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => OnOpenRequested?.Invoke();
    }

    public void SetPlayState(bool isPlaying)
    {
        _togglePlayMenuItem.Text = isPlaying ? "⏸ Pause" : "▶ Resume";
    }

    public void SetMuteState(bool isMuted)
    {
        _toggleMuteMenuItem.Text = isMuted ? "🔊 Unmute" : "🔇 Mute";
    }

    public void ShowNotification(string title, string text)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

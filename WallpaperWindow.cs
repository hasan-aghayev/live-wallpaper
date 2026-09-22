using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using LiveWallpaper.Core;
using Color = System.Drawing.Color;
using Stretch = System.Windows.Media.Stretch;

namespace LiveWallpaper;

public class WallpaperWindow : Form
{
    private MpvPlayer? _mpvPlayer;
    private bool _isOverlayEnabled;
    private Color _overlayColor = Color.Black;
    private double _overlayOpacity = 0.35;

    public bool IsPlaying { get; private set; }
    public string? CurrentVideoPath { get; private set; }

    public event Action<string>? OnPlaybackError;

    public WallpaperWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        ShowInTaskbar = false;

        var bounds = DesktopManager.GetDesktopBounds();
        Location = new System.Drawing.Point(bounds.X, bounds.Y);
        Size = new System.Drawing.Size(bounds.Width, bounds.Height);
    }

    public bool AttachToDesktop()
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
        }
        return DesktopManager.AttachToDesktop(Handle, out _);
    }

    public void SetOverlay(bool isEnabled, Color color, double opacity)
    {
        _isOverlayEnabled = isEnabled;
        _overlayColor = color;
        _overlayOpacity = opacity;

        _mpvPlayer?.SetOverlay(isEnabled, color, opacity);
    }

    public void DetachFromDesktop()
    {
        if (IsHandleCreated && Handle != IntPtr.Zero)
        {
            DesktopManager.DetachFromDesktop(Handle);
        }
    }

    public void LoadAndPlay(string filePath, double volume, bool isMuted, Stretch stretchMode)
    {
        DesktopManager.Log($"WallpaperWindow.LoadAndPlay: {filePath}, vol={volume}, muted={isMuted}, stretch={stretchMode}, overlay={_isOverlayEnabled}, opacity={_overlayOpacity}");
        if (!File.Exists(filePath))
        {
            DesktopManager.Log($"Error: file does not exist: {filePath}");
            OnPlaybackError?.Invoke($"File not found: {filePath}");
            return;
        }

        try
        {
            CurrentVideoPath = filePath;
            if (_mpvPlayer == null)
            {
                _mpvPlayer = new MpvPlayer(Handle);
            }

            _mpvPlayer.Start(filePath, volume, isMuted, stretchMode, _isOverlayEnabled, _overlayColor, _overlayOpacity);
            IsPlaying = true;
            DesktopManager.Log("WallpaperWindow: MpvPlayer started playback successfully with GPU overlay");
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"Exception in LoadAndPlay: {ex}");
            OnPlaybackError?.Invoke($"Playback error: {ex.Message}");
        }
    }

    public void Play()
    {
        DesktopManager.Log("WallpaperWindow.Play called");
        _mpvPlayer?.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        DesktopManager.Log("WallpaperWindow.Pause called");
        _mpvPlayer?.Pause();
        IsPlaying = false;
    }

    public void Stop()
    {
        DesktopManager.Log("WallpaperWindow.Stop called");
        _mpvPlayer?.Stop();
        _mpvPlayer?.Dispose();
        _mpvPlayer = null;
        IsPlaying = false;
        CurrentVideoPath = null;
    }

    public void SetVolume(double volume)
    {
        _mpvPlayer?.SetVolume(volume);
    }

    public void SetMuted(bool isMuted)
    {
        _mpvPlayer?.SetMute(isMuted);
    }

    public void SetStretch(Stretch stretch)
    {
        _mpvPlayer?.SetStretch(stretch);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mpvPlayer?.Dispose();
            _mpvPlayer = null;
        }
        base.Dispose(disposing);
    }
}

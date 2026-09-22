using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using LiveWallpaper.Core;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Application = System.Windows.Application;

namespace LiveWallpaper;

public partial class MainWindow : Window
{
    private readonly WallpaperConfig _config;
    private readonly TrayManager _trayManager;
    private WallpaperWindow? _wallpaperWindow;
    private string? _selectedVideoPath;
    private bool _isExiting;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();
        DesktopManager.Log("=== MainWindow initialized ===");

        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
            }
        }
        catch { }

        _config = ConfigManager.Load();
        _trayManager = new TrayManager();
        // Setup Tray callbacks
        _trayManager.OnOpenRequested += () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _trayManager.OnTogglePlayRequested += () => Dispatcher.Invoke(TogglePlayPause);
        _trayManager.OnToggleMuteRequested += () => Dispatcher.Invoke(ToggleMute);
        _trayManager.OnStopRequested += () => Dispatcher.Invoke(StopWallpaper);
        _trayManager.OnExitRequested += () => Dispatcher.Invoke(ExitApplication);

        // Apply saved config to UI
        SliderVolume.Value = _config.Volume * 100.0;
        TxtVolumeValue.Text = $"{(int)SliderVolume.Value}%";
        ChkMute.IsChecked = _config.IsMuted;
        ChkMinimizeToTray.IsChecked = _config.MinimizeToTray;
        bool autoStartEnabled = AutoStartManager.IsAutoStartEnabled();
        ChkAutoStart.IsChecked = autoStartEnabled;
        _config.AutoStart = autoStartEnabled;

        // Select stretch mode in combo
        foreach (ComboBoxItem item in CmbStretch.Items)
        {
            if (item.Tag?.ToString() == _config.StretchMode)
            {
                CmbStretch.SelectedItem = item;
                break;
            }
        }

        // Apply Overlay config to UI
        ChkEnableOverlay.IsChecked = _config.EnableOverlay;
        SliderOverlayOpacity.Value = _config.OverlayOpacity * 100.0;
        TxtOverlayOpacityValue.Text = $"{(int)SliderOverlayOpacity.Value}%";
        UpdateOverlayPreviewColor(_config.OverlayColor);
        _trayManager.SetPlayState(false);
        _trayManager.SetMuteState(_config.IsMuted);

        // Restore last video if available. Playback starts after the WPF window
        // has been rendered, otherwise the desktop HWND may not exist yet.
        if (!string.IsNullOrEmpty(_config.LastVideoPath) && File.Exists(_config.LastVideoPath))
        {
            SelectVideoFile(_config.LastVideoPath);
        }

        _isInitializing = false;
        ContentRendered += MainWindow_ContentRendered;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        bool hasValidVideo = VideoFileValidator.IsSupported(_selectedVideoPath);
        if (_config.AutoPlayOnLaunch && hasValidVideo)
        {
            ApplyWallpaper();
        }

        if (App.IsBackgroundLaunch && _config.MinimizeToTray)
        {
            if (!hasValidVideo)
            {
                _trayManager.ShowNotification(
                    "HaS Live Wallpaper",
                    "Автозапуск выполнен, но видео не найдено. Выберите файл в окне приложения.");
            }

            Hide();
        }
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => _wallpaperWindow?.RefreshDesktopBounds());
    }

    private void SelectVideoFile(string path)
    {
        _selectedVideoPath = path;
        TxtSelectedFileName.Text = Path.GetFileName(path);
        TxtSelectedFilePath.Text = path;
        TxtFooterMessage.Text = $"Selected: {Path.GetFileName(path)}";

        _config.LastVideoPath = path;
        ConfigManager.Save(_config);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Video File for Live Wallpaper",
            Filter = "Video Files|*.avi;*.flv;*.m2ts;*.m4v;*.mkv;*.mov;*.mp4;*.mpeg;*.mpg;*.ogv;*.ts;*.webm;*.wmv|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectVideoFile(dialog.FileName);
        }
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x43, 0x38, 0xCA));

        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files.Length > 0)
            {
                if (VideoFileValidator.IsSupported(files[0]))
                {
                    SelectVideoFile(files[0]);
                }
                else
                {
                    MessageBox.Show($"Please select a supported video file ({VideoFileValidator.SupportedExtensionsDescription}).",
                                    "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void DropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8));
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
    }

    private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x43, 0x38, 0xCA));
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        ApplyWallpaper();
    }

    private void ApplyWallpaper()
    {
        if (!VideoFileValidator.IsSupported(_selectedVideoPath))
        {
            MessageBox.Show("Please select a supported video file first.", "No File Selected",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string selectedVideoPath = _selectedVideoPath!;

        try
        {
            if (_wallpaperWindow == null)
            {
                _wallpaperWindow = new WallpaperWindow();
                _wallpaperWindow.OnPlaybackError += (err) =>
                {
                    Dispatcher.BeginInvoke(() => HandlePlaybackError(err));
                };
                _wallpaperWindow.Show();
                bool attached = _wallpaperWindow.AttachToDesktop();
                if (!attached)
                {
                    TxtFooterMessage.Text = "Desktop integration unavailable. Running in background mode.";
                }
            }

            // Apply overlay
            _wallpaperWindow.SetOverlay(ChkEnableOverlay.IsChecked == true, GetDrawingOverlayColor(), SliderOverlayOpacity.Value / 100.0);

            Stretch stretch = GetSelectedStretch();
            bool started = _wallpaperWindow.LoadAndPlay(selectedVideoPath, SliderVolume.Value / 100.0, ChkMute.IsChecked == true, stretch);
            if (!started)
            {
                HandlePlaybackError("Воспроизведение не запустилось. Проверьте наличие mpv.exe и доступность видеофайла.");
                return;
            }

            UpdateStatus(true, "Active");
            BtnPauseResume.IsEnabled = true;
            BtnPauseResume.Content = "Pause";
            BtnStop.IsEnabled = true;
            _trayManager.SetPlayState(true);

            TxtFooterMessage.Text = "Wallpaper active";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply live wallpaper:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HandlePlaybackError(string message)
    {
        UpdateStatus(false, "Error");
        BtnPauseResume.IsEnabled = false;
        BtnStop.IsEnabled = _wallpaperWindow != null;
        TxtFooterMessage.Text = $"Error: {message}";
        _trayManager.ShowNotification("HaS Live Wallpaper", message);
    }

    private void BtnPauseResume_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_wallpaperWindow == null)
            return;

        if (_wallpaperWindow.IsPlaying)
        {
            _wallpaperWindow.Pause();
            BtnPauseResume.Content = "Resume";
            UpdateStatus(false, "Paused", true);
            _trayManager.SetPlayState(false);
            TxtFooterMessage.Text = "Playback paused";
        }
        else
        {
            _wallpaperWindow.Play();
            BtnPauseResume.Content = "Pause";
            UpdateStatus(true, "Active");
            _trayManager.SetPlayState(true);
            TxtFooterMessage.Text = "Wallpaper active";
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        StopWallpaper();
    }

    private void StopWallpaper()
    {
        if (_wallpaperWindow != null)
        {
            _wallpaperWindow.Stop();
            _wallpaperWindow.DetachFromDesktop();
            _wallpaperWindow.Close();
            _wallpaperWindow = null;
        }

        Task.Run(() => DesktopManager.RefreshDesktop());

        UpdateStatus(false, "Inactive");
        BtnPauseResume.IsEnabled = false;
        BtnStop.IsEnabled = false;
        TxtFooterMessage.Text = "Wallpaper stopped";
    }

    private void ToggleMute()
    {
        ChkMute.IsChecked = !(ChkMute.IsChecked == true);
    }

    private void UpdateStatus(bool isActive, string text, bool isPaused = false)
    {
        StatusText.Text = text;
        if (isActive)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
        }
        else if (isPaused)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F));
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)); // Gray
            StatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
        }
    }

    private Stretch GetSelectedStretch()
    {
        if (CmbStretch.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "Fit" => Stretch.Uniform,
                "Stretch" => Stretch.Fill,
                _ => Stretch.UniformToFill
            };
        }
        return Stretch.UniformToFill;
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtVolumeValue != null)
        {
            TxtVolumeValue.Text = $"{(int)SliderVolume.Value}%";
        }

        if (_isInitializing)
            return;

        double vol = SliderVolume.Value / 100.0;
        _config.Volume = vol;
        ConfigManager.Save(_config);
        _wallpaperWindow?.SetVolume(vol);
    }

    private void ChkMute_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        bool isMuted = ChkMute.IsChecked == true;
        if (_config != null)
        {
            _config.IsMuted = isMuted;
            ConfigManager.Save(_config);
        }

        _wallpaperWindow?.SetMuted(isMuted);
        _trayManager?.SetMuteState(isMuted);
    }

    private void CmbStretch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (CmbStretch.SelectedItem is ComboBoxItem item && item.Tag != null && _config != null)
        {
            _config.StretchMode = item.Tag.ToString()!;
            ConfigManager.Save(_config);
            _wallpaperWindow?.SetStretch(GetSelectedStretch());
        }
    }

    private void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (!AutoStartManager.SetAutoStart(true))
        {
            _isInitializing = true;
            ChkAutoStart.IsChecked = false;
            _isInitializing = false;
            TxtFooterMessage.Text = "Не удалось включить автозапуск Windows. Проверьте разрешения пользователя.";
            MessageBox.Show(
                "Windows не разрешила изменить автозапуск для текущего пользователя.",
                "Автозапуск не включён", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_config != null)
        {
            _config.AutoStart = true;
            ConfigManager.Save(_config);
        }
    }

    private void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (!AutoStartManager.SetAutoStart(false))
        {
            _isInitializing = true;
            ChkAutoStart.IsChecked = true;
            _isInitializing = false;
            TxtFooterMessage.Text = "Не удалось отключить автозапуск Windows.";
            MessageBox.Show(
                "Windows не разрешила удалить запись автозапуска для текущего пользователя.",
                "Автозапуск не изменён", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_config != null)
        {
            _config.AutoStart = false;
            ConfigManager.Save(_config);
        }
    }

    private void ChkMinimizeToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        if (_config != null)
        {
            _config.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            ConfigManager.Save(_config);
        }
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayManager.ShowNotification("Live Wallpaper Launcher", "Application minimized to system tray. Live wallpaper is still running.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
            return;

        if (!_isExiting && ChkMinimizeToTray.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowNotification("Live Wallpaper Launcher", "Launcher minimized to tray. Double click the tray icon to restore.");
        }
        else
        {
            ExitApplication();
        }
    }

    private void ExitApplication()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        ConfigManager.SaveImmediately();
        StopWallpaper();
        _trayManager.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void UpdateOverlayPreviewColor(string hexColor)
    {
        try
        {
            var mediaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            BorderCurrentColorPreview.Background = new SolidColorBrush(mediaColor);
        }
        catch { }
    }

    private System.Drawing.Color GetDrawingOverlayColor()
    {
        try
        {
            return System.Drawing.ColorTranslator.FromHtml(_config.OverlayColor);
        }
        catch
        {
            return System.Drawing.Color.Black;
        }
    }

    private void ChkEnableOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _config == null)
            return;

        bool isEnabled = ChkEnableOverlay.IsChecked == true;
        _config.EnableOverlay = isEnabled;
        ConfigManager.Save(_config);

        _wallpaperWindow?.SetOverlay(isEnabled, GetDrawingOverlayColor(), SliderOverlayOpacity.Value / 100.0, immediate: true);
    }

    private void SliderOverlayOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtOverlayOpacityValue != null)
        {
            TxtOverlayOpacityValue.Text = $"{(int)SliderOverlayOpacity.Value}%";
        }

        if (_isInitializing || _config == null)
            return;

        double opacity = SliderOverlayOpacity.Value / 100.0;
        _config.OverlayOpacity = opacity;
        ConfigManager.Save(_config);

        _wallpaperWindow?.SetOverlay(ChkEnableOverlay.IsChecked == true, GetDrawingOverlayColor(), opacity, immediate: false);
    }

    private void BtnColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string hex)
        {
            SetOverlayColorHex(hex);
        }
    }

    private void BtnCustomColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.FullOpen = true;
        dialog.Color = GetDrawingOverlayColor();

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            SetOverlayColorHex(hex);
        }
    }

    private void SetOverlayColorHex(string hex)
    {
        _config.OverlayColor = hex;
        ConfigManager.Save(_config);
        UpdateOverlayPreviewColor(hex);

        if (ChkEnableOverlay.IsChecked != true)
        {
            ChkEnableOverlay.IsChecked = true;
        }

        _wallpaperWindow?.SetOverlay(true, GetDrawingOverlayColor(), SliderOverlayOpacity.Value / 100.0, immediate: true);
    }
}

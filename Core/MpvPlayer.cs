using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace LiveWallpaper.Core;

/// <summary>
/// Owns one mpv process and its IPC connection. No global process cleanup is
/// used, so closing this app cannot terminate another user's mpv instance.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private readonly IntPtr _targetHwnd;
    private readonly BlockingCollection<string> _commandQueue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _commandWorker;
    private readonly object _pipeLock = new();
    private readonly object _processLock = new();
    private readonly ManualResetEventSlim _pipeReady = new(false);

    private Process? _process;
    private string? _pipeName;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _pipeWriter;

    private readonly object _shaderThrottleLock = new();
    private System.Threading.Timer? _shaderThrottleTimer;
    private bool _pendingOverlayEnabled;
    private System.Drawing.Color _pendingColor = System.Drawing.Color.Black;
    private double _pendingOpacity = 0.35;

    private bool _currentOverlayEnabled;
    private System.Drawing.Color _currentOverlayColor = System.Drawing.Color.Black;
    private double _currentOverlayOpacity = 0.35;
    private bool _isDisposed;

    public event Action<string>? PlaybackError;

    public bool IsRunning
    {
        get
        {
            lock (_processLock)
            {
                try { return _process is { HasExited: false }; }
                catch { return false; }
            }
        }
    }

    public MpvPlayer(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        _commandWorker = Task.Run(() => ProcessCommands(_cts.Token));
    }

    public static string? FindMpvExecutable()
    {
        string[] candidates =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "mpv.exe"),
            Path.Combine(Environment.CurrentDirectory, "mpv.exe")
        ];

        foreach (string path in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch { }
        }

        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnvironment))
        {
            foreach (string directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "mpv.exe");
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch { }
            }
        }

        return null;
    }

    public void Start(
        string videoPath,
        double volume,
        bool isMuted,
        Stretch stretch,
        bool enableOverlay = false,
        System.Drawing.Color overlayColor = default,
        double overlayOpacity = 0.35)
    {
        ThrowIfDisposed();
        Stop();
        DrainCommandQueue();
        _pipeReady.Reset();

        string? mpvExe = FindMpvExecutable();
        if (mpvExe == null)
        {
            throw new FileNotFoundException(
                "mpv.exe не найден. Поместите его рядом с LiveWallpaper.exe или запустите scripts\\setup-mpv.ps1.");
        }

        string pipeName = $"livewallpaper_mpv_{Guid.NewGuid():N}";
        _pipeName = pipeName;

        int volumePercent = Math.Clamp((int)Math.Round(volume * 100), 0, 100);
        string keepAspect = stretch == Stretch.Fill ? "no" : "yes";
        string panscan = stretch == Stretch.UniformToFill ? "1.0" : "0.0";

        _currentOverlayEnabled = enableOverlay;
        _currentOverlayColor = NormalizeColor(overlayColor);
        _currentOverlayOpacity = Math.Clamp(overlayOpacity, 0.0, 0.9);

        lock (_shaderThrottleLock)
        {
            _shaderThrottleTimer?.Dispose();
            _shaderThrottleTimer = null;
            _pendingOverlayEnabled = _currentOverlayEnabled;
            _pendingColor = _currentOverlayColor;
            _pendingOpacity = _currentOverlayOpacity;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = mpvExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(mpvExe) ?? AppDomain.CurrentDomain.BaseDirectory
        };

        AddArgument(startInfo, $"--wid={_targetHwnd.ToInt64()}");
        AddArgument(startInfo, $"--input-ipc-server=\\\\.\\pipe\\{pipeName}");
        AddArgument(startInfo, "--idle=yes");
        AddArgument(startInfo, "--loop-file=inf");
        AddArgument(startInfo, "--no-border");
        AddArgument(startInfo, "--no-osc");
        AddArgument(startInfo, "--no-osd-bar");
        AddArgument(startInfo, "--force-window=immediate");
        AddArgument(startInfo, "--profile=high-quality");
        AddArgument(startInfo, "--vo=gpu-next");
        AddArgument(startInfo, "--gpu-api=d3d11");
        AddArgument(startInfo, "--hwdec=auto-safe");
        AddArgument(startInfo, "--scale=spline36");
        AddArgument(startInfo, "--cscale=spline36");
        AddArgument(startInfo, "--dscale=mitchell");
        AddArgument(startInfo, "--dither-depth=auto");
        AddArgument(startInfo, "--correct-pts=yes");
        AddArgument(startInfo, "--load-scripts=no");
        AddArgument(startInfo, $"--keepaspect={keepAspect}");
        AddArgument(startInfo, $"--panscan={panscan}");
        AddArgument(startInfo, $"--volume={volumePercent}");
        AddArgument(startInfo, $"--mute={(isMuted ? "yes" : "no")}");

        if (_currentOverlayEnabled && _currentOverlayOpacity > 0.005)
        {
            AddArgument(startInfo, $"--glsl-shader={EnsureOverlayShaderFile(_currentOverlayColor, _currentOverlayOpacity)}");
        }

        AddArgument(startInfo, videoPath);

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить mpv.exe.");

        lock (_processLock)
        {
            _process = process;
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            bool isCurrent;
            lock (_processLock) { isCurrent = ReferenceEquals(_process, process); }
            if (isCurrent && !_isDisposed)
            {
                PlaybackError?.Invoke($"mpv завершил работу с кодом {process.ExitCode}.");
            }
        };

        DesktopManager.Log($"MpvPlayer.Start: mpv PID={process.Id}, file='{videoPath}'");
        _ = Task.Run(() => ConnectPipe(process, pipeName), _cts.Token);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string value)
    {
        startInfo.ArgumentList.Add(value);
    }

    private void ConnectPipe(Process process, string pipeName)
    {
        try
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (_isDisposed || !IsCurrentProcess(process) || process.HasExited)
                    return;

                try
                {
                    var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    client.Connect(500);

                    lock (_pipeLock)
                    {
                        if (!IsCurrentProcess(process) || _isDisposed)
                        {
                            client.Dispose();
                            return;
                        }

                        _pipeClient = client;
                        _pipeWriter = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                        SignalPipeReady();
                    }

                    DesktopManager.Log("MpvPlayer: IPC pipe connected");
                    if (_currentOverlayEnabled && _currentOverlayOpacity > 0.005)
                    {
                        SetOverlay(true, _currentOverlayColor, _currentOverlayOpacity, immediate: true);
                    }
                    return;
                }
                catch (TimeoutException) { Thread.Sleep(100); }
                catch (IOException) { Thread.Sleep(100); }
            }

            DesktopManager.Log("MpvPlayer: IPC pipe connection timed out");
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"MpvPlayer.ConnectPipe error: {ex.Message}");
        }
    }

    private void ProcessCommands(CancellationToken cancellationToken)
    {
        try
        {
            foreach (string json in _commandQueue.GetConsumingEnumerable(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (WaitForPipe(cancellationToken))
                    TryWriteJson(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void SendCommand(params object[] commandArgs)
    {
        if (_isDisposed)
            return;

        try
        {
            string json = JsonSerializer.Serialize(new { command = commandArgs });
            _commandQueue.TryAdd(json);
        }
        catch (InvalidOperationException) { }
    }

    private bool WaitForPipe(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsRunning)
            {
                lock (_pipeLock)
                {
                    if (_pipeWriter != null && _pipeClient?.IsConnected == true)
                        return true;
                }

                _pipeReady.Wait(250, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        return false;
    }

    private bool TryWriteJson(string json)
    {
        lock (_pipeLock)
        {
            try
            {
                if (_pipeWriter != null && _pipeClient?.IsConnected == true)
                {
                    _pipeWriter.WriteLine(json);
                    return true;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        return false;
    }

    private void TryWriteCommand(params object[] commandArgs)
    {
        try
        {
            TryWriteJson(JsonSerializer.Serialize(new { command = commandArgs }));
        }
        catch { }
    }

    public void LoadVideo(string videoPath)
    {
        if (!IsRunning)
        {
            Start(videoPath, 0, true, Stretch.UniformToFill);
            return;
        }

        SendCommand("loadfile", videoPath, "replace");
        SendCommand("set_property", "pause", false);
    }

    public void Play() => SendCommand("set_property", "pause", false);
    public void Pause() => SendCommand("set_property", "pause", true);

    public void SetVolume(double volume)
    {
        SendCommand("set_property", "volume", Math.Clamp((int)Math.Round(volume * 100), 0, 100));
    }

    public void SetMute(bool isMuted) => SendCommand("set_property", "mute", isMuted);

    public void SetStretch(Stretch stretch)
    {
        switch (stretch)
        {
            case Stretch.UniformToFill:
                SendCommand("set_property", "keepaspect", true);
                SendCommand("set_property", "panscan", 1.0);
                break;
            case Stretch.Uniform:
                SendCommand("set_property", "keepaspect", true);
                SendCommand("set_property", "panscan", 0.0);
                break;
            case Stretch.Fill:
                SendCommand("set_property", "keepaspect", false);
                SendCommand("set_property", "panscan", 0.0);
                break;
        }
    }

    public void SetOverlay(System.Drawing.Color color, double opacity, bool immediate = false)
    {
        SetOverlay(true, color, opacity, immediate);
    }

    public void SetOverlay(bool isEnabled, System.Drawing.Color color, double opacity, bool immediate = false)
    {
        lock (_shaderThrottleLock)
        {
            _pendingOverlayEnabled = isEnabled;
            _pendingColor = NormalizeColor(color);
            _pendingOpacity = Math.Clamp(opacity, 0.0, 0.9);

            if (immediate)
            {
                _shaderThrottleTimer?.Dispose();
                _shaderThrottleTimer = null;
                ApplyOverlayInternal();
            }
            else if (_shaderThrottleTimer == null)
            {
                _shaderThrottleTimer = new System.Threading.Timer(_ =>
                {
                    lock (_shaderThrottleLock)
                    {
                        _shaderThrottleTimer?.Dispose();
                        _shaderThrottleTimer = null;
                        ApplyOverlayInternal();
                    }
                }, null, 40, Timeout.Infinite);
            }
        }
    }

    private void ApplyOverlayInternal()
    {
        _currentOverlayEnabled = _pendingOverlayEnabled;
        _currentOverlayColor = _pendingColor;
        _currentOverlayOpacity = _pendingOpacity;

        if (!_currentOverlayEnabled || _currentOverlayOpacity <= 0.005)
        {
            SendCommand("change-list", "glsl-shaders", "clr", "");
            return;
        }

        try
        {
            string shaderPath = EnsureOverlayShaderFile(_currentOverlayColor, _currentOverlayOpacity);
            // The shader path is stable between changes. Clear the current
            // shader first so mpv reloads the updated opacity immediately.
            SendCommand("change-list", "glsl-shaders", "clr", "");
            SendCommand("change-list", "glsl-shaders", "set", shaderPath.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"MpvPlayer.SetOverlay error: {ex.Message}");
        }
    }

    public static string EnsureOverlayShaderFile(System.Drawing.Color color, double opacity)
    {
        color = NormalizeColor(color);
        string r = (color.R / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string g = (color.G / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string b = (color.B / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string mix = Math.Clamp(opacity, 0.0, 0.9).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        string shaderContent = $"//!HOOK MAIN\n//!BIND HOOKED\n//!DESC HaS Live Wallpaper Overlay\n\nvec4 hook() {{\n    vec4 color = HOOKED_tex(HOOKED_pos);\n    vec4 tint = vec4({r}, {g}, {b}, 1.0);\n    return mix(color, tint, {mix});\n}}\n";

        AppPaths.EnsureDataDirectories();
        File.WriteAllText(AppPaths.OverlayShaderPath, shaderContent, new UTF8Encoding(false));
        return AppPaths.OverlayShaderPath;
    }

    public void Stop()
    {
        Process? process;
        lock (_processLock)
        {
            process = _process;
            _process = null;
            _pipeName = null;
        }

        SignalPipeReady();

        lock (_shaderThrottleLock)
        {
            _shaderThrottleTimer?.Dispose();
            _shaderThrottleTimer = null;
        }

        TryWriteCommand("quit");
        lock (_pipeLock)
        {
            try { _pipeWriter?.Dispose(); } catch { }
            try { _pipeClient?.Dispose(); } catch { }
            _pipeWriter = null;
            _pipeClient = null;
        }

        DrainCommandQueue();

        if (process != null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (!process.HasExited && !process.WaitForExit(500))
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            });
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        Stop();
        _isDisposed = true;

        lock (_shaderThrottleLock)
        {
            _shaderThrottleTimer?.Dispose();
            _shaderThrottleTimer = null;
        }

        try
        {
            SignalPipeReady();
            _cts.Cancel();
            _commandQueue.CompleteAdding();
            _commandWorker.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }
        finally
        {
            _cts.Dispose();
            _commandQueue.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private bool IsCurrentProcess(Process process)
    {
        lock (_processLock)
        {
            return ReferenceEquals(_process, process);
        }
    }

    private void DrainCommandQueue()
    {
        while (_commandQueue.TryTake(out _)) { }
    }

    private void SignalPipeReady()
    {
        try { _pipeReady.Set(); }
        catch (ObjectDisposedException) { }
    }

    private static System.Drawing.Color NormalizeColor(System.Drawing.Color color)
    {
        return color.IsEmpty || color.A == 0 ? System.Drawing.Color.Black : color;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

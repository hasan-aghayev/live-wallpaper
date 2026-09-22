using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace LiveWallpaper.Core;

public class MpvPlayer : IDisposable
{
    private readonly IntPtr _targetHwnd;
    private readonly string _pipeName;
    private Process? _process;
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _pipeWriter;
    private readonly object _lock = new();
    private bool _isDisposed;
    private bool _currentOverlayEnabled;
    private System.Drawing.Color _currentOverlayColor = System.Drawing.Color.Black;
    private double _currentOverlayOpacity = 0.35;

    public bool IsRunning => _process != null && !_process.HasExited;

    public MpvPlayer(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        _pipeName = $"livewallpaper_mpv_{Guid.NewGuid():N}";
    }

    public static string? FindMpvExecutable()
    {
        string[] candidates =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mpv.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "mpv.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "mpv.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "mpv.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "mpv.exe"),
            Path.Combine(Environment.CurrentDirectory, "mpv.exe")
        ];

        foreach (var path in candidates)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch { }
        }

        // Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "mpv.exe");
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch { }
            }
        }

        return null;
    }

    public void Start(string videoPath, double volume, bool isMuted, Stretch stretch, bool enableOverlay = false, System.Drawing.Color overlayColor = default, double overlayOpacity = 0.35)
    {
        lock (_lock)
        {
            Stop();

            string? mpvExe = FindMpvExecutable();
            if (mpvExe == null)
            {
                throw new FileNotFoundException("mpv.exe was not found. Please place mpv.exe in the application folder or run scripts/setup-mpv.ps1.");
            }

            DesktopManager.Log($"MpvPlayer.Start: Using mpv at '{mpvExe}', targeting HWND 0x{_targetHwnd.ToInt64():X}");

            int vol = Math.Clamp((int)(volume * 100), 0, 100);
            string muteStr = isMuted ? "yes" : "no";

            string keepAspect = stretch == Stretch.Fill ? "no" : "yes";
            string panscan = stretch == Stretch.UniformToFill ? "1.0" : "0.0";

            var args = new StringBuilder();
            args.Append($"--wid={_targetHwnd.ToInt64()} ");
            args.Append($"--input-ipc-server=\\\\.\\pipe\\{_pipeName} ");
            args.Append("--idle=yes ");
            args.Append("--loop-file=inf ");
            args.Append("--no-border ");
            args.Append("--no-osc ");
            args.Append("--no-osd-bar ");
            args.Append("--force-window=immediate ");
            args.Append("--profile=high-quality ");
            args.Append("--vo=gpu-next ");
            args.Append("--gpu-api=d3d11 ");
            args.Append("--hwdec=auto-safe ");
            args.Append("--scale=spline36 ");
            args.Append("--cscale=spline36 ");
            args.Append("--dscale=mitchell ");
            args.Append("--dither-depth=auto ");
            args.Append("--correct-pts=yes ");
            args.Append("--load-scripts=no ");
            args.Append($"--keepaspect={keepAspect} ");
            args.Append($"--panscan={panscan} ");
            args.Append($"--volume={vol} ");
            args.Append($"--mute={muteStr} ");

            _currentOverlayEnabled = enableOverlay;
            _currentOverlayColor = (overlayColor.IsEmpty || overlayColor.A == 0) ? System.Drawing.Color.Black : overlayColor;
            _currentOverlayOpacity = overlayOpacity;

            if (_currentOverlayEnabled && _currentOverlayOpacity > 0.005)
            {
                string shaderPath = EnsureOverlayShaderFile(_currentOverlayColor, _currentOverlayOpacity);
                string normalized = shaderPath.Replace('\\', '/');
                args.Append($"--glsl-shader=\"{normalized}\" ");
            }

            args.Append($"\"{videoPath}\"");

            var psi = new ProcessStartInfo
            {
                FileName = mpvExe,
                Arguments = args.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Path.GetDirectoryName(mpvExe) ?? AppDomain.CurrentDomain.BaseDirectory
            };

            _process = Process.Start(psi);
            DesktopManager.Log($"mpv.exe started with PID: {_process?.Id}");

            Task.Run(() => ConnectPipe());
        }
    }

    private void ConnectPipe()
    {
        try
        {
            for (int i = 0; i < 25; i++)
            {
                if (_isDisposed || _process == null || _process.HasExited)
                    return;

                try
                {
                    var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                    client.Connect(250);
                    lock (_lock)
                    {
                        _pipeClient = client;
                        _pipeWriter = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                    }
                    DesktopManager.Log("MpvPlayer: Named pipe connected successfully");

                    // Drain pipe responses in background so the pipe buffer never blocks
                    Task.Run(() =>
                    {
                        try
                        {
                            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
                            while (!_isDisposed && client.IsConnected)
                            {
                                string? line = reader.ReadLine();
                                if (line == null) break;
                            }
                        }
                        catch { }
                    });

                    // Sync overlay state once pipe is active
                    if (_currentOverlayEnabled && _currentOverlayOpacity > 0.005)
                    {
                        SetOverlay(true, _currentOverlayColor, _currentOverlayOpacity);
                    }

                    return;
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
            }
            DesktopManager.Log("MpvPlayer: Pipe connection timed out (continuing without IPC)");
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"MpvPlayer.ConnectPipe error: {ex.Message}");
        }
    }

    public void SendCommand(params object[] commandArgs)
    {
        lock (_lock)
        {
            if (_pipeWriter == null || _pipeClient == null || !_pipeClient.IsConnected)
                return;

            try
            {
                string json = JsonSerializer.Serialize(new { command = commandArgs });
                _pipeWriter.WriteLine(json);
                DesktopManager.Log($"MpvPlayer IPC -> {json}");
            }
            catch (Exception ex)
            {
                DesktopManager.Log($"MpvPlayer.SendCommand exception: {ex.Message}");
            }
        }
    }

    public void LoadVideo(string videoPath)
    {
        if (!IsRunning)
        {
            Start(videoPath, 0, true, Stretch.UniformToFill);
            return;
        }

        SendCommand("loadfile", videoPath);
        SendCommand("set_property", "pause", false);
    }

    public void Play()
    {
        SendCommand("set_property", "pause", false);
    }

    public void Pause()
    {
        SendCommand("set_property", "pause", true);
    }

    public void SetVolume(double volume)
    {
        int vol = Math.Clamp((int)(volume * 100), 0, 100);
        SendCommand("set_property", "volume", vol);
    }

    public void SetMute(bool isMuted)
    {
        SendCommand("set_property", "mute", isMuted);
    }

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

    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                if (_pipeWriter != null)
                {
                    SendCommand("quit");
                }
            }
            catch { }

            try
            {
                _pipeWriter?.Dispose();
                _pipeWriter = null;
                _pipeClient?.Dispose();
                _pipeClient = null;
            }
            catch { }

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        if (!_process.WaitForExit(800))
                        {
                            _process.Kill();
                        }
                    }
                }
                catch { }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }
            DesktopManager.Log("MpvPlayer stopped");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    public static string EnsureOverlayShaderFile(System.Drawing.Color color, double opacity)
    {
        if (color.IsEmpty || color.A == 0)
        {
            color = System.Drawing.Color.Black;
        }

        string r = (color.R / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string g = (color.G / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string b = (color.B / 255.0).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string op = Math.Clamp(opacity, 0.0, 0.95).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        string shaderContent = $@"//!HOOK MAIN
//!BIND HOOKED
//!DESC Live Wallpaper Dimming & Color Overlay

vec4 hook() {{
    vec4 color = HOOKED_tex(HOOKED_pos);
    vec4 tint = vec4({r}, {g}, {b}, 1.0);
    return mix(color, tint, {op});
}}
";
        string shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay.hook");
        using (var fs = new FileStream(shaderPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
        {
            sw.Write(shaderContent);
        }
        return shaderPath;
    }

    public void SetOverlay(bool isEnabled, System.Drawing.Color color, double opacity)
    {
        lock (_lock)
        {
            if (color.IsEmpty || color.A == 0)
            {
                color = System.Drawing.Color.Black;
            }

            _currentOverlayEnabled = isEnabled;
            _currentOverlayColor = color;
            _currentOverlayOpacity = opacity;

            if (_pipeWriter == null || _pipeClient == null || !_pipeClient.IsConnected)
                return;

            if (!isEnabled || opacity <= 0.005)
            {
                SendCommand("change-list", "glsl-shaders", "clr", "");
                DesktopManager.Log("MpvPlayer: Cleared overlay shader");
                return;
            }

            try
            {
                string shaderPath = EnsureOverlayShaderFile(color, opacity);
                string normalizedPath = shaderPath.Replace('\\', '/');
                SendCommand("change-list", "glsl-shaders", "set", normalizedPath);
                DesktopManager.Log($"MpvPlayer: Applied overlay shader (Color: {color.Name}, Opacity: {opacity:P0})");
            }
            catch (Exception ex)
            {
                DesktopManager.Log($"MpvPlayer.SetOverlay error: {ex.Message}");
            }
        }
    }

    public static void KillAllOrphanInstances()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("mpv"))
            {
                try
                {
                    p.Kill();
                }
                catch { }
            }
        }
        catch { }
    }
}

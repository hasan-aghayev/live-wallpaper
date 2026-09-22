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

    public void Start(string videoPath, double volume, bool isMuted, Stretch stretch)
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
            args.Append($"--keepaspect={keepAspect} ");
            args.Append($"--panscan={panscan} ");
            args.Append($"--volume={vol} ");
            args.Append($"--mute={muteStr} ");
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

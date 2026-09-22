using System;
using System.Windows;
using LiveWallpaper.Core;

namespace LiveWallpaper;

public partial class App : System.Windows.Application
{
    private static System.Threading.Mutex? _mutex;

    public static bool IsBackgroundLaunch { get; private set; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    protected override void OnStartup(StartupEventArgs e)
    {
        IsBackgroundLaunch = e.Args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        const string mutexName = "Local\\LiveWallpaper_SingleInstance_Mutex";
        try
        {
            _mutex = new System.Threading.Mutex(true, mutexName, out bool createdNew);
            DesktopManager.Log($"App.OnStartup: createdNew = {createdNew}");

            if (!createdNew)
            {
                DesktopManager.Log("App.OnStartup: Another instance detected. Activating and exiting.");
                var current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            DesktopManager.Log($"Mutex check exception: {ex.Message}");
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            DesktopManager.Log($"CRITICAL AppDomain UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            DesktopManager.Log($"CRITICAL DispatcherUnhandledException: {args.Exception}");
            args.Handled = true; // Prevent crash
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            ConfigManager.SaveImmediately();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }

        DesktopManager.FlushLogs();

        base.OnExit(e);
    }
}

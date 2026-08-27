using System.Windows;
using League_Account_Manager.Misc;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string[]? StartupArgs { get; private set; }
    internal static AuthRouteLauncher AuthLauncher { get; } = new();
    internal static OfflineLauncher OfflineLauncher { get; } = new();
    internal static DebugTrafficCaptureSession DebugTrafficCapture { get; } = new();
    internal static DebugClientTrafficLauncher DebugClientTrafficLauncher { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;

        base.OnStartup(e);
        ProxyLoginTokenManager.RegisterLoginUriScheme();
        DebugTrafficCapture.Start();
        LcuWebSocketMonitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LcuWebSocketMonitor.Stop();
        DebugClientTrafficLauncher.Dispose();
        DebugTrafficCapture.Dispose();
        base.OnExit(e);
    }
}
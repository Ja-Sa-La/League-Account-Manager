using System.Windows;
using System.IO;
using League_Account_Manager.Misc;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CancellationTokenSource? profileSettingsCts;
    private CancellationTokenSource? runeImportCts;
    public static string[]? StartupArgs { get; private set; }
    internal static AuthRouteLauncher AuthLauncher { get; } = new();
    internal static OfflineLauncher OfflineLauncher { get; } = new();
    internal static DebugTrafficCaptureSession DebugTrafficCapture { get; } = new();
    internal static DebugClientTrafficLauncher DebugClientTrafficLauncher { get; } = new();
    internal static PluginManager Plugins { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;

        base.OnStartup(e);
        ProxyLoginTokenManager.RegisterLoginUriScheme();
        DebugTrafficCapture.Start();
        // The LCU WebSocket monitor is only needed by the request tracker page and debug login,
        // which start it themselves; running it from startup recorded every client event.
    }

    /// <summary>
    ///     Closes LAM once a login has handed off to the game client, so it does not sit in
    ///     the background holding memory.
    /// </summary>
    internal static void ShutdownAfterLogin()
    {
        var dispatcher = Current?.Dispatcher;
        if (!Settings.settingsloaded.CloseAfterLogin || dispatcher == null || dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
            return;

        DebugConsole.WriteLine("[Login] Login finished, closing League Account Manager.");
        dispatcher.BeginInvoke(() => Current?.Shutdown());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Task.Run(async () => await Plugins.DisposeAsync()).GetAwaiter().GetResult();
        profileSettingsCts?.Cancel();
        profileSettingsCts?.Dispose();
        runeImportCts?.Cancel();
        runeImportCts?.Dispose();
        LcuWebSocketMonitor.Stop();
        DebugClientTrafficLauncher.Dispose();
        DebugTrafficCapture.Dispose();
        base.OnExit(e);
    }

    internal void StartProfileSettingsLoop()
    {
        profileSettingsCts ??= new CancellationTokenSource();
        _ = ProfileSettingsService.RunAsync(profileSettingsCts.Token);
    }

    internal void StartRuneImportLoop()
    {
        runeImportCts ??= new CancellationTokenSource();
        _ = RuneAutoImportService.RunAsync(runeImportCts.Token);
    }
}
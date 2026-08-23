using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using League_Account_Manager.Misc;
using NLog;
using NLog.Config;
using NLog.Targets;
using Notification.Wpf;
using LogLevel = NLog.LogLevel;

namespace League_Account_Manager;

public class Notif
{
    public static NotificationManager notificationManager = new();

    public static void donothing()
    {
    }
}

public partial class MainWindow : Window
{
    private const long MaxLogFileSize = 10 * 1024 * 1024;
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private TaskCompletionSource<MessageBoxResult>? _updateModalCompletion;
    private Action? _updateModalReleaseAction;
    private Action? _updateModalUpdateAction;

    public MainWindow()
    {
        InitializeComponent();
        ContentRendered += (_, __) => DebugConsole.Initialize(this);
        PreviewKeyDown += MainWindowOnPreviewKeyDown;
        InitializeLogging();
        InitializeUI();
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    dynamic data = await Lcu.GetClientInfo();
                    Dispatcher.Invoke(() =>
                    {
                        leaguedata.Text = $"League port: {data.Item3} password: {data.Item4}";
                        riotdata.Text = $"Riot port: {data.Item1} password: {data.Item2}";
                    });
                }
                catch (Exception e)
                {
                    DebugConsole.WriteLine(e.ToString(), ConsoleColor.Red);
                }

                Thread.Sleep(30000);
            }
        });
    }

    private void InitializeLogging()
    {
        LogFileMaintenance.TrimToNewestBytes("Log.txt", MaxLogFileSize);
        foreach (var archivePath in Directory.GetFiles(AppContext.BaseDirectory, "Log.*.txt"))
            LogFileMaintenance.TrimToNewestBytes(archivePath, MaxLogFileSize);

        var config = new LoggingConfiguration();
        var fileTarget = new FileTarget("logfile")
        {
            FileName = "Log.txt",
            ArchiveAboveSize = MaxLogFileSize,
            ArchiveNumbering = ArchiveNumberingMode.Rolling,
            ArchiveFileName = "Log.{#}.txt",
            MaxArchiveFiles = 1
        };
        config.AddRule(LogLevel.Debug, LogLevel.Error, fileTarget);
        LogManager.Configuration = config;

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = (Exception)args.ExceptionObject;
            logger.Fatal(exception, "Unhandled Exception");
        };
    }

    private async void InitializeUI()
    {
        try
        {
            // Check for updates if required
            if (UpdateArguments.TryGetTarget(App.StartupArgs, Environment.ProcessPath, out var updateTarget))
                Updates.FinishUpdate(updateTarget);

            // Load settings
            await Settings.loadsettings();

            // Perform update check if enabled in settings
            if (Settings.settingsloaded.updates)
                await Updates.UpdateCheckAsync();
            else
                DebugConsole.WriteLine("[Updates] Automatic update checks are disabled in settings.",
                    ConsoleColor.Yellow);

            DebugConsole.WriteLine($"[Startup] League client path: {Settings.settingsloaded.LeaguePath}");
            var releaseChannel = string.Equals(Settings.settingsloaded.ReleaseChannel, "Beta",
                StringComparison.OrdinalIgnoreCase) ? "Beta" : "Stable";
            version.Content = $"Version {Assembly.GetExecutingAssembly().GetName().Version} ({releaseChannel})";
            installloc.Content = Settings.settingsloaded.riotPath;
            installloclea.Content = Settings.settingsloaded.LeaguePath;

            await ProxyLoginTokenManager.TryHandleLoginUriAsync(App.StartupArgs);
        }
        catch (Exception e)
        {
            logger.Error(e, "An error occurred during initialization");
            Notif.notificationManager.Show(new NotificationContent
            {
                Title = "Error",
                Message = "An error occurred during initialization",
                Type = NotificationType.Error
            });
            Environment.Exit(1); // Exit the application on critical error
        }
    }

    private void MainWindowOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            DebugConsole.ToggleVisibility();
            e.Handled = true;
        }
    }

    private void RootNavigation_OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate("home");
        Dispatcher.BeginInvoke(DisableNavigationScrollViewers, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(DisableNavigationScrollViewers, DispatcherPriority.ContextIdle);
    }

    private void DisableNavigationScrollViewers()
    {
        foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(RootNavigation))
        {
            if (FindVisualParent<DataGrid>(scrollViewer) != null) continue;

            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is T matchingChild) yield return matchingChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T matchingParent) return matchingParent;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void Discord_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://discord.gg/tjQVcc9SGP") { UseShellExecute = true });
    }

    public Task<MessageBoxResult> ShowUpdateModalAsync(string version, string channel, string patchNotes,
        bool dimBackground = true, Action? updateAction = null, Action? releaseAction = null)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(() => ShowUpdateModalAsync(version, channel, patchNotes, dimBackground,
                updateAction, releaseAction));

        _updateModalCompletion?.TrySetResult(MessageBoxResult.Cancel);
        _updateModalUpdateAction = updateAction;
        _updateModalReleaseAction = releaseAction;
        UpdateModalTitle.Text = "Update available";
        UpdateModalVersion.Text = $"A new {channel.ToLowerInvariant()} release is available: {version}";
        UpdateModalPatchNotes.Text = $"Patch notes{Environment.NewLine}{Environment.NewLine}{NormalizePatchNotes(patchNotes)}";
        UpdateModalLater.Content = "Later";
        UpdateModalLater.Visibility = Visibility.Visible;
        UpdateModalRelease.Visibility = Visibility.Visible;
        UpdateModalUpdate.Visibility = Visibility.Visible;
        UpdateModalBackdrop.Visibility = dimBackground ? Visibility.Visible : Visibility.Collapsed;
        _updateModalCompletion = new TaskCompletionSource<MessageBoxResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateModalOverlay.Visibility = Visibility.Visible;
        UpdateModalOverlay.Focus();
        return _updateModalCompletion.Task;
    }

    public Task<MessageBoxResult> ShowUpdatedModalAsync(string version, string channel, string patchNotes)
    {
        var completion = ShowUpdateModalAsync(version, channel, patchNotes);
        UpdateModalTitle.Text = "Update complete";
        UpdateModalVersion.Text = $"League Account Manager was updated to {version} ({channel})";
        UpdateModalLater.Content = "Close";
        UpdateModalRelease.Visibility = Visibility.Collapsed;
        UpdateModalUpdate.Visibility = Visibility.Collapsed;
        return completion;
    }

    private static string NormalizePatchNotes(string patchNotes)
    {
        return patchNotes
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\r", Environment.NewLine, StringComparison.Ordinal)
            .Trim();
    }

    private void CloseUpdateModal(MessageBoxResult result, Action? action = null)
    {
        if (UpdateModalOverlay.Visibility != Visibility.Visible)
            return;

        UpdateModalOverlay.Visibility = Visibility.Collapsed;
        var completion = _updateModalCompletion;
        _updateModalCompletion = null;
        var selectedAction = action;
        _updateModalUpdateAction = null;
        _updateModalReleaseAction = null;
        selectedAction?.Invoke();
        completion?.TrySetResult(result);
    }

    private void UpdateModalLater_Click(object sender, RoutedEventArgs e)
    {
        CloseUpdateModal(MessageBoxResult.Cancel);
    }

    private void UpdateModalRelease_Click(object sender, RoutedEventArgs e)
    {
        CloseUpdateModal(MessageBoxResult.No, _updateModalReleaseAction);
    }

    private void UpdateModalUpdate_Click(object sender, RoutedEventArgs e)
    {
        CloseUpdateModal(MessageBoxResult.Yes, _updateModalUpdateAction);
    }

}
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
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
    private readonly double _aspectRatio;
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private bool _isResizing;

    public MainWindow()
    {
        InitializeComponent();
        _aspectRatio = Width / Height;
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
        var config = new LoggingConfiguration();
        var fileTarget = new FileTarget("logfile") { FileName = "Log.txt" };
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
            if (IsUpdateProcess())
                Updates.FinishUpdate();

            // Load settings
            await Settings.loadsettings();

            // Perform update check if enabled in settings
            if (Settings.settingsloaded.updates)
                Updates.updatecheck();

            DebugConsole.WriteLine(Settings.settingsloaded.LeaguePath);
            version.Content = "Version " + Assembly.GetExecutingAssembly().GetName().Version;
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

    private bool IsUpdateProcess()
    {
        return Process.GetCurrentProcess().MainModule.FileName.Contains("temp_update.exe");
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
    }

    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isResizing)
            return;

        try
        {
            _isResizing = true;

            if (e.WidthChanged && !e.HeightChanged)
            {
                Height = Math.Max(MinHeight, e.NewSize.Width / _aspectRatio);
            }
            else if (e.HeightChanged && !e.WidthChanged)
            {
                Width = Math.Max(MinWidth, e.NewSize.Height * _aspectRatio);
            }
            else
            {
                var targetHeight = e.NewSize.Width / _aspectRatio;
                var targetWidth = e.NewSize.Height * _aspectRatio;

                if (targetHeight > e.NewSize.Height)
                    Height = Math.Max(MinHeight, targetHeight);
                else
                    Width = Math.Max(MinWidth, targetWidth);
            }
        }
        finally
        {
            _isResizing = false;
        }
    }
}
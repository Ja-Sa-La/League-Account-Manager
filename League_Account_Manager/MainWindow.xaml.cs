using System.Diagnostics;
using System.Reflection;
using System.Windows;
using NLog;
using NLog.Config;
using NLog.Targets;
using Notification.Wpf;
using LogLevel = NLog.LogLevel;

namespace League_Account_Manager;

public class notif
{
    public static NotificationManager notificationManager = new();

    public static void donothing()
    {
    }
}

public partial class MainWindow : Window
{
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public MainWindow()
    {
        InitializeComponent();
        InitializeLogging();
        InitializeUI();
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

    private void InitializeUI()
    {
        try
        {
            // Check for updates if required
            if (IsUpdateProcess())
                Updates.FinishUpdate();

            // Load settings
            Settings.loadsettings();
            installloc.Content = Settings.settingsloaded.riotPath;

            // Perform update check if enabled in settings
            if (Settings.settingsloaded.updates)
                Updates.updatecheck();
            installloc.Content = Settings.settingsloaded.riotPath;
            version.Content = "Version " + Assembly.GetExecutingAssembly().GetName().Version;
        }
        catch (Exception e)
        {
            logger.Error(e, "An error occurred during initialization");
            notif.notificationManager.Show(new NotificationContent
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

    private void RootNavigation_OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate("home");
    }
}
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public MainWindow()
    {
        InitializeComponent();
        //AllocConsole();
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
                    Console.WriteLine(e);
                }

                Thread.Sleep(30000);
            }
        });
    }

    [DllImport("kernel32.dll", EntryPoint = "AllocConsole", SetLastError = true, CharSet = CharSet.Auto,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int AllocConsole();

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

            Console.WriteLine(Settings.settingsloaded.LeaguePath);
            version.Content = "Version " + Assembly.GetExecutingAssembly().GetName().Version;
            installloc.Content = Settings.settingsloaded.riotPath;
            installloclea.Content = Settings.settingsloaded.LeaguePath;
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

    private void RootNavigation_OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate("home");
    }
}
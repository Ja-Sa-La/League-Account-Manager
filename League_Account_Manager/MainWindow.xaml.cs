using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {

        try
        {


        var config = new LoggingConfiguration();

        var fileTarget = new FileTarget("logfile") { FileName = "Log.txt" };

        config.AddRule(LogLevel.Debug, LogLevel.Error, fileTarget);

        LogManager.Configuration = config;
        var logger = LogManager.GetCurrentClassLogger();
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = (Exception)args.ExceptionObject;
            LogManager.GetCurrentClassLogger().Fatal(exception, "Unhandled Exception");
        };
        InitializeComponent();
        // AllocConsole();
         Console.WriteLine(Process.GetCurrentProcess().MainModule.FileName);
         if (Process.GetCurrentProcess().MainModule.FileName.Contains("temp_update.exe"))
         {
             Updates.FinishUpdate();

         }

         version.Content = "Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
        Settings.loadsettings();
        installloc.Content = Settings.settingsloaded.riotPath;
        
        if (Settings.settingsloaded.updates) Updates.updatecheck();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);

        }
    }

    public static event EventHandler<string> UpdateLabelText;

    // [DllImport("kernel32.dll", EntryPoint = "AllocConsole", SetLastError = true, CharSet = CharSet.Auto,
    //    CallingConvention = CallingConvention.StdCall)]
    // private static extern int AllocConsole();

    private void NavigationItem_Click_1(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page2());
    }

    private void NavigationItem_Click_2(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page1());
    }

    private void NavigationItem_Click(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page3());
    }

    private void NavigationItem_Click_3(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page4());
    }

    private void NavigationItem_Click_4(object sender, RoutedEventArgs e)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void NavigationItem_Click_5(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page5());
    }

    private void NavigationItem_Click_6(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page6());
    }

    private void NavigationItem_Click_7(object sender, RoutedEventArgs e)
    {
        // RootFrame.Navigate(new Page7());
    }

    private void NavigationItem_Click_8(object sender, RoutedEventArgs e)
    {
        //  RootFrame.Navigate(new Page8());
    }

    private void RootNavigation_OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate("home");
    }

    private void WindowArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
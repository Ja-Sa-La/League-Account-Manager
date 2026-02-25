using System.Windows;
using League_Account_Manager.Misc;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string[]? StartupArgs { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupArgs = e.Args;
        base.OnStartup(e);
        ProxyLoginTokenManager.RegisterLoginUriScheme();
    }
}
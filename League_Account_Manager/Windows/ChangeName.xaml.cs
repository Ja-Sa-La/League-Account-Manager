using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for ChangeName.xaml
/// </summary>
public partial class ChangeName : Window
{
    public ChangeName()
    {
        InitializeComponent();
    }

    private void Window_MouseDownDatadisplay(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }


    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = Nameholder.Text;
            var tag = Tagline.Text;
            HttpResponseMessage resp = null;
            JObject body = null;
            Process.Start(Settings.settingsloaded.riotPath);
            if (tag == null)
            {
                resp = await lcu.Connector("riot", "post", "/player-account/aliases/v1/aliases",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            else
            {
                resp = await lcu.Connector("riot", "post", "/player-account/aliases/v1/aliases",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"" + tag + "\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if ((bool)body["isSuccess"])
            {
                errormessage.Content = "Namechange was succesful!";
                errormessage.Visibility = Visibility.Visible;
            }
            else
            {
                errormessage.Content = $"{body["errorCode"]} {body["errorMessage"]}";
                errormessage.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
                notif.notificationManager.Show("Error", "Riot Client not running",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Button_Click_2(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = Nameholder.Text;
            var tag = Tagline.Text;
            HttpResponseMessage resp = null;
            JObject body = null;
            Process.Start(Settings.settingsloaded.riotPath);
            if (tag == null)
            {
                resp = await lcu.Connector("riot", "post", "/player-account/aliases/v2/validity",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            else
            {
                resp = await lcu.Connector("riot", "post", "/player-account/aliases/v2/validity",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"" + tag + "\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if ((bool)body["isValid"])
            {
                errormessage.Content = "Namechange name is valid";
                errormessage.Visibility = Visibility.Visible;
            }
            else
            {
                errormessage.Content = $"{body["invalidReason"]}";
                errormessage.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
                notif.notificationManager.Show("Error", "Riot Client not running",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }
}
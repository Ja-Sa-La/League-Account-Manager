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


    private async void ChangeName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NameTextBox.Text;
            var tag = TaglineTextBox.Text;
            HttpResponseMessage resp = null;
            JObject body = null;
            Process.Start(Settings.settingsloaded.riotPath);
            if (tag == null)
            {
                resp = await Lcu.Connector("riot", "post", "/player-account/aliases/v1/aliases",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            else
            {
                resp = await Lcu.Connector("riot", "post", "/player-account/aliases/v1/aliases",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"" + tag + "\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if ((bool)body["isSuccess"])
            {
                ErrorMessageLabel.Content = "Namechange was succesful!";
                ErrorMessageLabel.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorMessageLabel.Content = $"{body["errorCode"]} {body["errorMessage"]}";
                ErrorMessageLabel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
                Notif.notificationManager.Show("Error", "Riot Client not running",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void CheckNameValidity_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = NameTextBox.Text;
            var tag = TaglineTextBox.Text;
            HttpResponseMessage resp = null;
            JObject body = null;
            Process.Start(Settings.settingsloaded.riotPath);
            if (tag == null)
            {
                resp = await Lcu.Connector("riot", "post", "/player-account/aliases/v2/validity",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
            else
            {
                resp = await Lcu.Connector("riot", "post", "/player-account/aliases/v2/validity",
                    "{\"gameName\":\"" + name + "\",\"tagLine\":\"" + tag + "\"}");
                body = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if ((bool)body["isValid"])
            {
                ErrorMessageLabel.Content = "Namechange name is valid";
                ErrorMessageLabel.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorMessageLabel.Content = $"{body["invalidReason"]}";
                ErrorMessageLabel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
                Notif.notificationManager.Show("Error", "Riot Client not running",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }
}
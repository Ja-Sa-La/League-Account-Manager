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
            var body = await SendAliasRequestAsync("/player-account/aliases/v1/aliases", name, tag);

            if (body["isSuccess"]?.Value<bool>() == true)
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
            var body = await SendAliasRequestAsync("/player-account/aliases/v2/validity", name, tag);

            if (body["isValid"]?.Value<bool>() == true)
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

    private static async Task<JObject> SendAliasRequestAsync(string endpoint, string name, string tag)
    {
        var riotPath = Settings.settingsloaded.riotPath;
        if (string.IsNullOrWhiteSpace(riotPath))
            throw new InvalidOperationException("Riot client path is not configured.");

        Process.Start(new ProcessStartInfo { FileName = riotPath, UseShellExecute = true });
        var payload = new JObject
        {
            ["gameName"] = name,
            ["tagLine"] = tag
        };
        var response = await Lcu.Connector("riot", "post", endpoint,
            payload.ToString(Newtonsoft.Json.Formatting.None)) as HttpResponseMessage;
        if (response == null)
            throw new InvalidOperationException("Riot client did not return a response.");

        return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
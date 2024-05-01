using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for Window5.xaml
/// </summary>
public partial class Window5 : Window
{
    public Window5()
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
}
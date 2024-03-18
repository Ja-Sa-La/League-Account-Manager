using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using Wpf.Ui.Controls;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page8.xaml
/// </summary>
public partial class Page8 : Page
{
    private readonly List<IconData> list = new();
    private readonly List<IconData> listSkins = new();
    private IconData SelectedIcon = new();
    private IconData SelectedSkin = new();

    public Page8()
    {
        InitializeComponent();
        LoadIcons();
        LoadSkins();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var resp = await lcu.Connector("riot", "post", "/chat/v1/suspend", "{\"config\":\"disable\"}");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Button_Click_1(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var resp = await lcu.Connector("riot", "post", "/chat/v1/resume", "");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Button_Click_2(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var resp = await lcu.Connector("riot", "get", "/chat/v2/session/state", "");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Status(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            HttpResponseMessage resp = await lcu.Connector("league", "put", "/lol-chat/v1/me",
                "{\"statusMessage\": \"" +
                StatusMessageContainer.Text.ReplaceLineEndings().Replace(Environment.NewLine, " ") + "\"}");
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
            resp = await lcu.Connector("riot", "patch", "/chat/v1/settings",
                "{\"chat-status-message\": \"" +
                StatusMessageContainer.Text.ReplaceLineEndings().Replace(Environment.NewLine, " ") + "\"}");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void ChatOnline(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"availability\":\"online\"}";

            var resp = await lcu.Connector("league", "put", "/lol-chat/v1/me", jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void ChatOffline(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"availability\":\"offline\"}";

            var resp = await lcu.Connector("league", "put", "/lol-chat/v1/me", jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void ChatRiotMobile(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"availability\":\"mobile\"}";

            var resp = await lcu.Connector("league", "put", "/lol-chat/v1/me", jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void ChatAway(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"availability\":\"away\"}";

            var resp = await lcu.Connector("league", "put", "/lol-chat/v1/me", jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void SetIcon(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"profileIconId\": " + SelectedIcon.ID + "}";
            var resp = await lcu.Connector("league", "put", "/lol-summoner/v1/current-summoner/icon", jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
            jsonString = "{\"icon\": " + SelectedIcon.ID + "}";
            resp = await lcu.Connector("league", "put", "/lol-chat/v1/me/", jsonString);
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void LoadIcons()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            Task.Run(async () =>
            {
                var resp = await lcu.Connector("league", "get", "/lol-store/v1/catalog",
                    "inventoryType=[%22SUMMONER_ICON%22]");
                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);


                JArray tmp = JArray.Parse(responseBody2);

                foreach (var item in tmp)
                    list.Add(new IconData
                        { Name = item["localizations"]["en_US"]["name"].ToString(), ID = item["itemId"].ToString() });

                Dispatcher.Invoke(() =>
                {
                    IconList.OriginalItemsSource = list;
                    IconList.PlaceholderText = "start typing to search";
                    IconList.IsEnabled = true;
                });
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void LoadSkins()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            Task.Run(async () =>
            {
                var resp = await lcu.Connector("league", "get", "/lol-store/v1/catalog",
                    "inventoryType=[%22CHAMPION_SKIN%22]");
                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JArray tmp = JArray.Parse(responseBody2);
                foreach (var item in tmp)
                {
                    if (item["subInventoryType"].ToString() == "RECOLOR")
                        continue;
                    listSkins.Add(new IconData
                        { Name = item["localizations"]["en_US"]["name"].ToString(), ID = item["itemId"].ToString() });
                }

                Dispatcher.Invoke(() =>
                {
                    SkinList.OriginalItemsSource = listSkins;
                    SkinList.PlaceholderText = "start typing to search";
                    SkinList.IsEnabled = true;
                });
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void SetSkin(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var jsonString = "{\"key\": \"backgroundSkinId\",\"value\": " + SelectedSkin.ID + "}";
            var resp = await lcu.Connector("league", "post", "/lol-summoner/v1/current-summoner/summoner-profile/",
                jsonString);
            var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private void IconList_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SelectedIcon = args.SelectedItem as IconData;
    }


    private void IconList_OnGotFocus(object sender, RoutedEventArgs e)
    {
        SkinList.Text = "";
        IconList.Text = "";
        SendKeys.SendWait(" ");
    }

    private void SetBackground(object sender, RoutedEventArgs e)
    {
    }

    private void SkinList_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SelectedSkin = args.SelectedItem as IconData;
    }

    public class IconData
    {
        public string Name { get; set; }
        public string ID { get; set; }
    }
}
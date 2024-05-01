using System.Diagnostics;
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
    private readonly List<IconData>? list = new();
    private readonly List<IconData>? listSkins = new();
    private bool loaded;
    private IconData? SelectedIcon = new();
    private IconData? SelectedSkin = new();

    public Page8()
    {
        InitializeComponent();
        LoadDataAsync();
    }

    public List<string>? QueueList { get; set; }
    public List<string>? RankList { get; set; }
    public List<string>? TierList { get; set; }

    private async void LoadDataAsync()
    {
        while (true)
            try
            {
                if (!loaded) break;
                await LoadIcons();
                await LoadSkins();
                loadranks();
                break; // If all methods complete successfully, break the loop
            }
            catch (Exception e)
            {
                notif.notificationManager.Show("Error",
                    "League of Legends client is not running! waiting 5 seconds to try again",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                await Task.Delay(5000); // Use Task.Delay instead of Thread.Sleep in async methods
            }
    }


    private async Task<bool> CheckLeagueClientProcess()
    {
        var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
        if (leagueclientprocess.Length == 0)
        {
            notif.notificationManager.Show("Error", "League of Legends client is not running!",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            return false;
        }

        return true;
    }

    private async Task<string> ExecuteCommand(string module, string method, string endpoint, string data)
    {
        if (!await CheckLeagueClientProcess())
            return "";
        var resp = await lcu.Connector(module, method, endpoint, data);
        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var responseBody2 = await ExecuteCommand("riot", "post", "/chat/v1/suspend", "{\"config\":\"disable\"}");
            Console.WriteLine(responseBody2);
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Button_Click_1(object sender, RoutedEventArgs e)
    {
        try
        {
            var responseBody2 = await ExecuteCommand("riot", "post", "/chat/v1/resume", "");
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
            var responseBody2 = await ExecuteCommand("riot", "get", "/chat/v2/session/state", "");
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
            var responseBody2 = await ExecuteCommand("league", "put", "/lol-chat/v1/me",
                "{\"statusMessage\": \"" +
                StatusMessageContainer.Text.ReplaceLineEndings().Replace(Environment.NewLine, " ") + "\"}");
            responseBody2 = await ExecuteCommand("riot", "patch", "/chat/v1/settings",
                "{\"chat-status-message\": \"" +
                StatusMessageContainer.Text.ReplaceLineEndings().Replace(Environment.NewLine, " ") + "\"}");
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
            var responseBody2 =
                await ExecuteCommand("league", "put", "/lol-chat/v1/me", "{\"availability\":\"online\"}");
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
            var responseBody2 =
                await ExecuteCommand("league", "put", "/lol-chat/v1/me", "{\"availability\":\"offline\"}");
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
            var responseBody2 =
                await ExecuteCommand("league", "put", "/lol-chat/v1/me", "{\"availability\":\"mobile\"}");
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void SetRank(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedQueue = Mode.SelectedItem.ToString();
            var selectedRank = Rank.SelectedItem.ToString();
            var selectedTier = Division.SelectedItem.ToString();

            var responseBody2 = await ExecuteCommand("league", "put", "/lol-chat/v1/me",
                "{\"lol\":{\"rankedLeagueQueue\":\"" + selectedQueue + "\",\"rankedLeagueTier\":\"" + selectedRank +
                "\",\"rankedLeagueDivision\":\"" + selectedTier + "\"}}");
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
            var responseBody2 = await ExecuteCommand("league", "put", "/lol-chat/v1/me", "{\"availability\":\"away\"}");
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
            var responseBody2 = await ExecuteCommand("league", "put", "/lol-summoner/v1/current-summoner/icon",
                "{\"profileIconId\": " + SelectedIcon.ID + "}");
            //Console.Writeline(responseBody2);
            responseBody2 =
                await ExecuteCommand("league", "put", "/lol-chat/v1/me/", "{\"icon\": " + SelectedIcon.ID + "}");
            //Console.Writeline(responseBody2);
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
            var responseBody2 = await ExecuteCommand("league", "post",
                "/lol-summoner/v1/current-summoner/summoner-profile/",
                "{\"key\": \"backgroundSkinId\",\"value\": " + SelectedSkin.ID + "}");
            //Console.Writeline(responseBody2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void loadranks()
    {
        QueueList = new List<string>
        {
            "RANKED_SOLO_5x5", "RANKED_FLEX_SR", "RANKED_FLEX_TT", "RANKED_TFT", "RANKED_TFT_TURBO",
            "RANKED_TFT_DOUBLE_UP", "CHERRY", ""
        };
        RankList = new List<string>
        {
            "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER",
            ""
        };
        TierList = new List<string> { "I", "II", "III", "IV", "" };
        DataContext = this;
    }

    private async Task LoadIcons()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;

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


    private async Task LoadSkins()
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0) return;

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
            throw new Exception("aa");
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
        SkinList.Text = "";
        IconList.Text = "";
    }

    private void SetBackground(object sender, RoutedEventArgs e)
    {
    }

    private void SkinList_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SelectedSkin = args.SelectedItem as IconData;
    }


    private void Page8_OnLoaded(object sender, RoutedEventArgs e)
    {
        loaded = true;
        LoadDataAsync();
    }

    private void Page8_OnUnloaded(object sender, RoutedEventArgs e)
    {
        loaded = false;
    }


    public class IconData
    {
        public string Name { get; set; }
        public string ID { get; set; }
    }
}
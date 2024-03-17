using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CsvHelper;
using CsvHelper.Configuration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using Application = FlaUI.Core.Application;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page1.xaml
/// </summary>
public partial class Page1 : Page
{
    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    public DataTable dt = new();
    private bool Executing;
    private double running;


    public Page1()
    {
        InitializeComponent();
    }

    public List<accountlist> Jotain { get; }
    public static List<accountlist> ActualAccountlists { get; set; }

    public async Task loaddata()
    {
        try
        {
            await Task.Run(() =>
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{Settings.settingsloaded.filename}.csv");

                if (File.Exists(filePath))
                {
                    ActualAccountlists = LoadCSV(filePath);
                }
                else
                {
                    File.Create(filePath).Dispose();
                    ActualAccountlists = new List<accountlist>();
                }

                ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
                RemoveDoubleQuotesFromList(ActualAccountlists);
            });

            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                Championlist.ItemsSource = null;
                Championlist.ItemsSource = ActualAccountlists;
                Championlist.Items.SortDescriptions.Add(new SortDescription("Champions", ListSortDirection.Descending));
                Championlist.Columns[12].Visibility = Visibility.Hidden;
                Championlist.Columns[8].Visibility = Visibility.Hidden;
                Championlist.Columns[9].Visibility = Visibility.Hidden;
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }


    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedrow = Championlist.SelectedItem as accountlist;
            if (selectedrow != null)
            {
                var itemToRemove = ActualAccountlists.SingleOrDefault(r =>
                    r.username == selectedrow.username && r.password == selectedrow.password &&
                    r.server == selectedrow.server);
                if (itemToRemove != null)
                    ActualAccountlists.Remove(itemToRemove);


                ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");

                using (var writer =
                       new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename +
                                        ".csv"))
                using (var csv = new CsvWriter(writer, config))
                {
                    csv.WriteRecords(ActualAccountlists);
                }

                await loaddata();
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    public async void ring()
    {
        running += 7;
        edistyy.Progress = running;
    }

    private async void PullData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Error,
                    "WindowArea", onClick: () => notif.donothing());
                return;
            }

            if (SelectedUsername == null || SelectedPassword == null) new Window1().ShowDialog();

            Progressgrid.Visibility = Visibility.Visible;
            ring();

            var summonerInfo = await GetSummonerInfoAsync();
            ring();
            var skinInfo = await GetSkinInfoAsync();
            ring();
            var champInfo = await GetChampionInfoAsync((string)summonerInfo["summonerId"]);
            ring();
            var lootInfo = await GetLootInfoAsync();
            ring();
            var rankedInfo = await GetRankedInfoAsync();
            ring();
            var wallet = await GetWalletAsync();
            ring();
            var region = await GetRegionAsync();
            ring();
            var skinlist = "";
            var skincount = 0;
            var champlist = "";
            var champcount = 0;
            var Lootlist = "";
            var Lootcount = 0;
            var Rank = " Rank: " + rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " With: " +
                       rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["wins"] + " Wins and " +
                       rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["losses"] + " Losses";

            foreach (var item in skinInfo)
                if ((bool)item["owned"])
                {
                    skinlist += ":" + item["name"];
                    skincount++;
                }

            ring();
            foreach (var item in champInfo)
                if ((bool)item["ownership"]["owned"])
                {
                    champlist += ":" + item["name"];
                    champcount++;
                }

            ring();
            foreach (var item in lootInfo)
            foreach (var thing in item)
                if ((int)thing["count"] > 0)
                {
                    var resp = await lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + thing["lootId"], "");
                    var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        var Loot = JObject.Parse(responseBody);
                        if (Loot["itemDesc"] != "")
                            Lootlist += ":" + Loot["itemDesc"] + " x " + Loot["count"];
                        else if (Loot["localizedName"] != "")
                            Lootlist += ":" + Loot["localizedName"] + " x " + Loot["count"];
                        else
                            Lootlist += ":" + Loot["asset"] + " x " + Loot["count"];
                    }
                    catch (Exception ex)
                    {
                        // Handle exception
                    }

                    Lootcount++;
                }

            ring();
            ActualAccountlists.RemoveAll(x => x.username == SelectedUsername);
            ring();
            ActualAccountlists.Add(new accountlist
            {
                username = SelectedUsername,
                password = SelectedPassword,
                riotID = summonerInfo["gameName"] + "#" + summonerInfo["tagLine"],
                level = (string)summonerInfo["summonerLevel"],
                server = (string)region["region"],
                be = wallet.be,
                rp = wallet.rp,
                rank = Rank,
                champions = champlist,
                Champions = Convert.ToInt32(champcount),
                skins = skinlist,
                Skins = Convert.ToInt32(skincount),
                Loot = Lootlist,
                Loots = Convert.ToInt32(Lootcount)
            });
            ring();
            using (var writer =
                   new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename +
                                    ".csv"))
            using (var csv2 = new CsvWriter(writer, config))
            {
                csv2.WriteRecords(ActualAccountlists);
            }

            ring();
            Progressgrid.Visibility = Visibility.Hidden;
            Championlist.ItemsSource = ActualAccountlists;
            Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
            Championlist.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async Task<JObject> GetSummonerInfoAsync()
    {
        var resp = await lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JObject.Parse(responseBody);
    }

    private async Task<JArray> GetSkinInfoAsync()
    {
        var resp = await lcu.Connector("league", "get", "/lol-catalog/v1/items/CHAMPION_SKIN", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JArray.Parse(responseBody);
    }

    private async Task<JArray> GetChampionInfoAsync(string summonerId)
    {
        while (true)
        {
            dynamic responseBody = "";
            try
            {
                var resp = await lcu.Connector("league", "get",
                    $"/lol-champions/v1/inventories/{summonerId}/champions-minimal", "");
                responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JArray.Parse(responseBody);
            }
            catch (Exception ex)
            {
                var jotain = JToken.Parse(responseBody);
                if (jotain["errorCode"] != "RPC_ERROR")
                    Environment.Exit(1);
                else
                    await Task.Delay(2000);
            }
        }
    }

    private async Task<JToken> GetLootInfoAsync()
    {
        var resp = await lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JToken.Parse(responseBody);
    }

    private async Task<JToken> GetRankedInfoAsync()
    {
        var resp = await lcu.Connector("league", "get", "/lol-ranked/v1/current-ranked-stats", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JToken.Parse(responseBody);
    }

    private async Task<wallet> GetWalletAsync()
    {
        var resp = await lcu.Connector("league", "get", "/lol-inventory/v1/wallet/lol_blue_essence", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var be = JToken.Parse(responseBody)["lol_blue_essence"];

        resp = await lcu.Connector("league", "get", "/lol-inventory/v1/wallet/RP", "");
        responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var rp = JToken.Parse(responseBody)["RP"];

        return new wallet { be = be, rp = rp };
    }

    private async Task<JToken> GetRegionAsync()
    {
        var resp = await lcu.Connector("league", "get", "/riotclient/region-locale", "");
        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JToken.Parse(responseBody);
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckLeague()) throw new Exception("League not installed");
            var i = 0;
            DataGridCellInfo cellinfo;
            foreach (var row in Championlist.SelectedCells)
            {
                if (i == 0)
                    SelectedUsername = (row.Column.GetCellContent(row.Item) as TextBlock).Text;
                else if (i == 1) SelectedPassword = (row.Column.GetCellContent(row.Item) as TextBlock).Text;
                i++;
            }


            killleaguefunc();
            Process[] leagueProcess;
            var num = 0;
            var RiotClient = Process.Start(Settings.settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            var riotval = string.Empty;
            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0)
                {
                    riotval = "Riot Client";
                    break;
                }

                if (Process.GetProcessesByName("RiotClientUx").Length != 0)
                {
                    riotval = "RiotClientUx";
                    break;
                }


                Thread.Sleep(2000);
                num++;
                if (num == 5) return;
            }

            while (true)
                try
                {
                    var app = Application.Attach(riotval);

                    using (var automation = new UIA3Automation())
                    {
                        AutomationElement window = app.GetMainWindow(automation);
                        var riotcontent =
                            window.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND"));


                        var usernameField = riotcontent.FindFirstDescendant(cf => cf.ByAutomationId("username"))
                            .AsTextBox();
                        if (usernameField == null) throw new Exception("Username field not found");


                        // Find the password field
                        var passwordField = riotcontent.FindFirstDescendant(cf => cf.ByAutomationId("password"))
                            .AsTextBox();
                        if (passwordField == null) throw new Exception("Password field not found");


                        var checkbox = riotcontent.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox));
                        if (riotcontent == null) throw new Exception("Riot content not found");
                        if (checkbox == null) throw new Exception("Checkbox field not found");

                        var siblings = riotcontent.FindAllChildren();
                        if (checkbox.Parent == null) throw new Exception("Checkbox parent not found");
                        //Console.Writeline(siblings.Length);
                        var count = Array.IndexOf(siblings, checkbox) + 1;
                        if (siblings.Length <= count) throw new Exception("Not enough siblings found for the checkbox");
                        dynamic signInElement = null;
                        while (siblings.Length >= count)
                        {
                            signInElement = siblings[count++].AsButton();

                            //Console.Writeline($"Found checkbox: {checkbox.Name}");
                            //Console.Writeline($"Found siblings count: {siblings.Length}");

                            if (signInElement.ControlType != ControlType.Button) continue;
                            break;
                        }

                        usernameField.Text = SelectedUsername;
                        passwordField.Text = SelectedPassword;
                        if (signInElement != null)
                        {
                            while (!signInElement.IsEnabled) Thread.Sleep(200);
                            signInElement.Invoke();
                            Thread.Sleep(1000);
                            await lcu.Connector("riot", "post",
                                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
                            break;
                        }

                        Thread.Sleep(1000);
                    }
                }
                catch (Exception ex)
                {
                    //Console.Writeline(ex);
                    Thread.Sleep(200);
                }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void Championlist_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            var selectedrow = Championlist.SelectedItem as accountlist;
            if (selectedrow != null)
            {
                var itemToRemove = ActualAccountlists.FindAll(r =>
                    r.username == selectedrow.username && r.password == selectedrow.password &&
                    r.server == selectedrow.server);
                if (itemToRemove != null)
                    foreach (var VARIABLE in itemToRemove)
                        ActualAccountlists.Remove(VARIABLE);


                using (var writer = new StreamWriter(Directory.GetCurrentDirectory() + "\\" +
                                                     Settings.settingsloaded.filename + ".csv"))
                using (var csv = new CsvWriter(writer, config))
                {
                    csv.WriteRecords(ActualAccountlists);
                }

                await loaddata();
            }
        }
    }

    public async Task<bool> CheckLeague()
    {
        if (File.Exists(Settings.settingsloaded.riotPath))
            return true;
        return false;
    }

    public static void killleaguefunc()
    {
        var source = new[]
        {
            "RiotClientUxRender", "RiotClientUx", "RiotClientServices", "RiotClientCrashHandler",
            "LeagueCrashHandler",
            "LeagueClientUxRender", "LeagueClientUx", "LeagueClient"
        };

        var allProcessesKilled = false;

        while (!allProcessesKilled)
        {
            allProcessesKilled = true;

            foreach (var processName in source)
            {
                var processes = Process.GetProcessesByName(processName);

                foreach (var process in processes)
                {
                    process.Kill();
                    allProcessesKilled = false;
                }
            }

            if (!allProcessesKilled)
                // Wait for a moment before checking again
                Thread.Sleep(1000); // You can adjust the time interval if needed
        }
    }

    private void killleague_Click(object sender, RoutedEventArgs e)
    {
        killleaguefunc();
    }

    private async void openleague1_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var processesByName = Process.GetProcessesByName("Riot Client");
            var processesByName2 = Process.GetProcessesByName("LeagueClientUx");
            killleaguefunc();
            if (!await CheckLeague()) throw new Exception("League not installed");

            openleague();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error Opening league");
        }
    }

    private void openleague()
    {
        Process.Start(Settings.settingsloaded.riotPath,
            "--launch-product=league_of_legends --launch-patchline=live");
    }

    private async void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (champfilter.Text != "")
        {
            var filteredList = ActualAccountlists.Where(word =>
                    word.champions.IndexOf(champfilter.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    word.skins.IndexOf(champfilter.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    word.Loot.IndexOf(champfilter.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    word.server.IndexOf(champfilter.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            ;
            Championlist.ItemsSource = filteredList;
        }
        else
        {
            Championlist.ItemsSource = ActualAccountlists;
        }

        Championlist.Columns[12].Visibility = Visibility.Hidden;
        Championlist.Columns[8].Visibility = Visibility.Hidden;
        Championlist.Columns[9].Visibility = Visibility.Hidden;
        Championlist.UpdateLayout();
        Championlist.Items.Refresh();
    }

    public List<accountlist> LoadCSV(string filePath)
    {
        var records = new List<accountlist>();

        try
        {
            using (var reader = new StreamReader(filePath))
            {
                var isFirstLine = true;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();

                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    var values = line.Split(';');

                    var record = new accountlist
                    {
                        username = values.Length > 0 && !string.IsNullOrEmpty(values[0]) ? values[0] : "",
                        password = values.Length > 1 && !string.IsNullOrEmpty(values[1]) ? values[1] : "",
                        riotID = values.Length > 2 && !string.IsNullOrEmpty(values[2]) ? values[2] : "",
                        level = values.Length > 3 && !string.IsNullOrEmpty(values[3]) ? values[3] : "",
                        server = values.Length > 4 && !string.IsNullOrEmpty(values[4]) ? values[4] : "",
                        be = values.Length > 5 && !string.IsNullOrEmpty(values[5]) ? values[5] : "",
                        rp = values.Length > 6 && !string.IsNullOrEmpty(values[6]) ? values[6] : "",
                        rank = values.Length > 7 && !string.IsNullOrEmpty(values[7]) ? values[7] : "",
                        champions = values.Length > 8 && !string.IsNullOrEmpty(values[8]) ? values[8] : "",
                        skins = values.Length > 9 && !string.IsNullOrEmpty(values[9]) ? values[9] : "",
                        Champions = values.Length > 10 && !string.IsNullOrEmpty(values[10])
                            ? Convert.ToInt32(values[10].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        Skins = values.Length > 11 && !string.IsNullOrEmpty(values[11])
                            ? Convert.ToInt32(values[11].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        Loot = values.Length > 12 && !string.IsNullOrEmpty(values[12]) ? values[12] : "",
                        Loots = values.Length > 13 && !string.IsNullOrEmpty(values[13])
                            ? Convert.ToInt32(values[13].Replace("\"", "").Replace("\'", ""))
                            : 0
                    };

                    records.Add(record);
                }
            }
        }
        catch (Exception exception)
        {
            //Console.Writeline(exception);
            notif.notificationManager.Show("Error", "An error occurred while loading the CSV file",
                NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
            {
                LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
            }
        }


        return records;
    }

    public static void RemoveDoubleQuotesFromList(List<accountlist> accountList)
    {
        foreach (var account in accountList)
        {
            account.username = RemoveDoubleQuotes(account.username);
            account.password = RemoveDoubleQuotes(account.password);
            account.riotID = RemoveDoubleQuotes(account.riotID);
            account.level = RemoveDoubleQuotes(account.level);
            account.server = RemoveDoubleQuotes(account.server);
            account.be = RemoveDoubleQuotes(account.be);
            account.rp = RemoveDoubleQuotes(account.rp);
            account.rank = RemoveDoubleQuotes(account.rank);
            account.champions = RemoveDoubleQuotes(account.champions);
            account.skins = RemoveDoubleQuotes(account.skins);
            account.Loot = RemoveDoubleQuotes(account.Loot);
        }
    }

    public static string? RemoveDoubleQuotes(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input.Replace("\"", "");
    }

    private async void Login_Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var i = 0;
            DataGridCellInfo cellinfo;
            foreach (var row in Championlist.SelectedCells)
            {
                if (i == 0)
                    SelectedUsername = (row.Column.GetCellContent(row.Item) as TextBlock).Text;
                else if (i == 1) SelectedPassword = (row.Column.GetCellContent(row.Item) as TextBlock).Text;
                i++;
            }

            await Task.Run(async () =>
            {
                killleaguefunc();
                Process[] leagueProcess;
                var num = 0;
                var RiotClient = Process.Start(Settings.settingsloaded.riotPath,
                    "--launch-product=league_of_legends --launch-patchline=live");

                while (true)
                {
                    if (Process.GetProcessesByName("Riot Client").Length != 0 ||
                        Process.GetProcessesByName("RiotClientUx").Length != 0)
                        break;
                    Thread.Sleep(2000);
                    num++;
                    if (num == 5) return;
                }

                var resp = await lcu.Connector("riot", "post", "/rso-auth/v2/authorizations",
                    "{\"clientId\":\"riot-client\",\"trustLevels\":[\"always_trusted\"]}");
                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                resp = await lcu.Connector("riot", "put", "/rso-auth/v1/session/credentials",
                    "{\"username\":\"" + SelectedUsername + "\",\"password\":\"" + SelectedPassword +
                    "\", \"persistLogin\":\"false\"}");
                var responseBody1 = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (responseBody1["error"] == "auth_failure")
                    Dispatcher.Invoke(() =>
                    {
                        notif.notificationManager.Show("Error", "Account details are invalid", NotificationType.Error,
                            "WindowArea", onClick: () => notif.donothing());
                    });
                resp = await lcu.Connector("riot", "post",
                    "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
                responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error logging in");
        }
    }

    private async void Championlist_Loaded(object sender, RoutedEventArgs e)
    {
        await loaddata();
    }

    private async void Championlist_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dataGrid = sender as DataGrid;
        if (!Executing)
        {
            Executing = true;
            try
            {
                if (dataGrid != null && dataGrid.CurrentCell != null)
                {
                    var selectedColumn = dataGrid.CurrentCell.Column;

                    if (selectedColumn != null)
                    {
                        var header = selectedColumn.Header?.ToString();
                        var selectedrow = Championlist.SelectedItem as accountlist;
                        if (selectedrow == null) return;
                        if (header == null) return;
                        Window4? secondWindow = null;

                        switch (header)
                        {
                            case "Champions":
                                secondWindow = new Window4(selectedrow.champions);
                                break;
                            case "Skins":
                                secondWindow = new Window4(selectedrow.skins);
                                break;
                            case "Loots":
                                secondWindow = new Window4(selectedrow.Loot);
                                break;
                        }

                        if (secondWindow != null)
                        {
                            await secondWindow.Dispatcher.InvokeAsync(() => { secondWindow.Show(); });

                            while (secondWindow.IsLoaded) await Task.Delay(100);
                        }
                    }

                    dataGrid.UnselectAllCells();
                    dataGrid.SelectedItem = null;
                }
            }
            catch (Exception exception)
            {
                LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
            }

            finally
            {
                Executing = false;
            }
        }

        dataGrid.UnselectAllCells();
        dataGrid.SelectedItem = null;
    }

    private async void openleague1_Copy_Click(object sender, RoutedEventArgs e)
    {
        var namechanger = new Window5();
        namechanger.Show();
    }

    private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    public class accountlist
    {
        public string? username { get; set; }
        public string? password { get; set; }
        public string? riotID { get; set; }
        public string? level { get; set; }
        public string? server { get; set; }
        public string? be { get; set; }
        public string? rp { get; set; }
        public string? rank { get; set; }
        public string? champions { get; set; }
        public string? skins { get; set; }
        public int Champions { get; set; }
        public int Skins { get; set; }
        public string? Loot { get; set; }
        public int Loots { get; set; }
    }

    public class wallet
    {
        public string? be { get; set; }
        public string? rp { get; set; }
    }
}
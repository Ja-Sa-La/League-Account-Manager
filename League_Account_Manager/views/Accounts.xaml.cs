﻿using System.ComponentModel;
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
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using Application = FlaUI.Core.Application;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Accounts.xaml
/// </summary>
public partial class Accounts : Page
{
    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private readonly FileSystemWatcher? fileWatcher;
    private bool Executing;
    private double running;

    public Accounts()
    {
        InitializeComponent();
        loaddata();
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{Misc.Settings.settingsloaded.filename}.csv");

        fileWatcher = new FileSystemWatcher
        {
            Path = Path.GetDirectoryName(filePath) ?? string.Empty,
            Filter = Path.GetFileName(filePath) ?? string.Empty,
            NotifyFilter = NotifyFilters.LastWrite
        };

        fileWatcher.Changed += OnChanged;
        fileWatcher.EnableRaisingEvents = true;
    }

    public static List<Utils.AccountList>? ActualAccountlists { get; set; }

    private async void OnChanged(object source, FileSystemEventArgs e)
    {
        await loaddata();
        Dispatcher.Invoke(() =>
        {
            Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
        });
    }

    public async Task loaddata()
    {
        try
        {
            await Task.Run(async () =>
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(),
                    $"{Misc.Settings.settingsloaded.filename}.csv");

                if (File.Exists(filePath))
                {
                    ActualAccountlists = await LoadCSV(filePath);
                }
                else
                {
                    File.Create(filePath).Dispose();
                    ActualAccountlists = new List<Utils.AccountList>();
                }

                ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");
                Utils.RemoveDoubleQuotesFromList(ActualAccountlists);
            });

            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                Championlist.ItemsSource = null;
                Championlist.ItemsSource = ActualAccountlists;
                Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
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
            var selectedrow = Championlist.SelectedItem as Utils.AccountList;
            if (selectedrow != null)
            {
                var itemToRemove = ActualAccountlists?.SingleOrDefault(r =>
                    r.username == selectedrow.username && r.password == selectedrow.password &&
                    r.server == selectedrow.server);
                if (itemToRemove != null)
                    ActualAccountlists?.Remove(itemToRemove);

                ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");

                using (var writer =
                       new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Misc.Settings.settingsloaded.filename +
                                        ".csv"))
                using (var csv = new CsvWriter(writer, config))
                {
                    csv.WriteRecords(ActualAccountlists);
                }
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
                Notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            if (SelectedUsername == null || SelectedPassword == null) new MissingInfo().ShowDialog();

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
            var Rank = string.IsNullOrEmpty(rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["tier"]?.ToString())
                ? "Unranked"
                : rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                  rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["division"] + " " +
                  rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["leaguePoints"] + " LP, " +
                  rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["wins"] + " Wins, " +
                  rankedInfo["queueMap"]["RANKED_SOLO_5x5"]["losses"] + " Losses";
            var Rank2 = string.IsNullOrEmpty(rankedInfo["queueMap"]["RANKED_FLEX_SR"]["tier"]?.ToString())
                ? "Unranked"
                : rankedInfo["queueMap"]["RANKED_FLEX_SR"]["tier"] + " " +
                  rankedInfo["queueMap"]["RANKED_FLEX_SR"]["division"] + " " +
                  rankedInfo["queueMap"]["RANKED_FLEX_SR"]["leaguePoints"] + " LP, " +
                  rankedInfo["queueMap"]["RANKED_FLEX_SR"]["wins"] + " Wins, " +
                  rankedInfo["queueMap"]["RANKED_FLEX_SR"]["losses"] + " Losses";

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
                    var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + thing["lootId"], "");
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
            var note = ActualAccountlists?.FindLast(x => x.username == SelectedUsername);
            ActualAccountlists?.RemoveAll(x => x.username == SelectedUsername);
            ring();
            ActualAccountlists?.Add(new Utils.AccountList
            {
                username = SelectedUsername,
                password = SelectedPassword,
                riotID = summonerInfo["gameName"] + "#" + summonerInfo["tagLine"],
                level = (int)summonerInfo["summonerLevel"],
                server = (string)region["region"],
                be = Convert.ToInt32(wallet.be),
                rp = Convert.ToInt32(wallet.rp),
                rank = Rank,
                champions = champlist,
                Champions = Convert.ToInt32(champcount),
                skins = skinlist,
                Skins = Convert.ToInt32(skincount),
                Loot = Lootlist,
                Loots = Convert.ToInt32(Lootcount),
                rank2 = Rank2,
                note = note?.note
            });
            ring();
            using (var writer =
                   new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Misc.Settings.settingsloaded.filename +
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


    private async Task<JObject?> GetSummonerInfoAsync()
    {
        var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JObject.Parse(responseBody);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JArray?> GetSkinInfoAsync()
    {
        var resp = await Lcu.Connector("league", "get", "/lol-catalog/v1/items/CHAMPION_SKIN", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JArray.Parse(responseBody);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JArray?> GetChampionInfoAsync(string summonerId)
    {
        while (true)
        {
            var responseBody = "";
            try
            {
                var resp = await Lcu.Connector("league", "get",
                    $"/lol-champions/v1/inventories/{summonerId}/champions-minimal", "");
                if (resp == null) return null;

                responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JArray.Parse(responseBody);
            }
            catch (Exception)
            {
                if (!string.IsNullOrEmpty(responseBody))
                    try
                    {
                        var jotain = JToken.Parse(responseBody);
                        if (jotain["errorCode"]?.ToString() != "RPC_ERROR")
                            Environment.Exit(1);
                    }
                    catch (JsonException)
                    {
                        /* Ignore invalid JSON */
                    }

                await Task.Delay(2000);
            }
        }
    }

    private async Task<JToken?> GetLootInfoAsync()
    {
        var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JToken.Parse(responseBody);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JToken?> GetRankedInfoAsync()
    {
        var resp = await Lcu.Connector("league", "get", "/lol-ranked/v1/current-ranked-stats", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            var parsed = JToken.Parse(responseBody);
            Console.WriteLine(parsed);
            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Utils.Wallet?> GetWalletAsync()
    {
        var resp = await Lcu.Connector("league", "get",
            "/lol-inventory/v1/wallet?currencyTypes=[%22RP%22,%22lol_blue_essence%22]", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            var json = JToken.Parse(responseBody);
            var be = json["lol_blue_essence"]?.ToObject<int>() ?? 0;
            var rp = json["RP"]?.ToObject<int>() ?? 0;

            return new Utils.Wallet { be = be, rp = rp };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JToken?> GetRegionAsync()
    {
        var resp = await Lcu.Connector("league", "get", "/riotclient/region-locale", "");
        if (resp == null) return null;

        var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JToken.Parse(responseBody);
        }
        catch (JsonException)
        {
            return null;
        }
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


            Utils.killleaguefunc();
            Process[] leagueProcess;
            var num = 0;
            var RiotClient = Process.Start(Misc.Settings.settingsloaded.riotPath,
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
                            while (true)
                            {
                                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                                string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                Console.WriteLine(status);
                                if (status == "\"Accepted\"") break;
                                if (status == "\"AcceptanceRequired\"")
                                {
                                    await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                                    Thread.Sleep(200);
                                }
                                else
                                {
                                    Thread.Sleep(500);
                                }
                            }

                            await Lcu.Connector("riot", "post",
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

    private void Championlist_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            var selectedrow = Championlist.SelectedItem as Utils.AccountList;
            if (selectedrow != null)
            {
                var itemToRemove = ActualAccountlists.FindAll(r =>
                    r.username == selectedrow.username && r.password == selectedrow.password &&
                    r.server == selectedrow.server);
                if (itemToRemove != null)
                    foreach (var VARIABLE in itemToRemove)
                        ActualAccountlists.Remove(VARIABLE);


                using (var writer = new StreamWriter(Directory.GetCurrentDirectory() + "\\" +
                                                     Misc.Settings.settingsloaded.filename + ".csv"))
                using (var csv = new CsvWriter(writer, config))
                {
                    csv.WriteRecords(ActualAccountlists);
                }
            }
        }
    }

    public async Task<bool> CheckLeague()
    {
        if (File.Exists(Misc.Settings.settingsloaded.riotPath))
            return true;
        return false;
    }


    private void killleague_Click(object sender, RoutedEventArgs e)
    {
        Utils.killleaguefunc();
    }

    private async void openleague1_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var processesByName = Process.GetProcessesByName("Riot Client");
            var processesByName2 = Process.GetProcessesByName("LeagueClientUx");
            Utils.killleaguefunc();
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
        Process.Start(Misc.Settings.settingsloaded.riotPath,
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

        Championlist.UpdateLayout();
        Championlist.Items.Refresh();
    }

    public async Task<List<Utils.AccountList>> LoadCSV(string filePath)
    {
        var records = new List<Utils.AccountList>();

        try
        {
            FileStream? fileStream = null;
            while (fileStream == null)
                try
                {
                    fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    fileStream.Close();
                }
                catch (IOException)
                {
                    // The file is in use by another process. Wait and try again.
                    await Task.Delay(1000);
                }

            using (var reader = new StreamReader(filePath))
            {
                var isFirstLine = true;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line == null) continue;

                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    var values = line.Split(';');

                    var record = new Utils.AccountList
                    {
                        username = values.Length > 0 ? values[0] : "",
                        password = values.Length > 1 ? values[1] : "",
                        riotID = values.Length > 2 ? values[2] : "",
                        level = values.Length > 3 && !string.IsNullOrEmpty(values[3])
                            ? Convert.ToInt32(values[3].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        server = values.Length > 4 ? values[4] : "",
                        be = values.Length > 5 && !string.IsNullOrEmpty(values[5])
                            ? Convert.ToInt32(values[5].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        rp = values.Length > 6 && !string.IsNullOrEmpty(values[6])
                            ? Convert.ToInt32(values[6].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        rank = values.Length > 7 ? values[7] : "",
                        rank2 = values.Length > 14 ? values[14] : "",
                        champions = values.Length > 8 ? values[8] : "",
                        skins = values.Length > 9 ? values[9] : "",
                        Champions = values.Length > 10 && !string.IsNullOrEmpty(values[10])
                            ? Convert.ToInt32(values[10].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        Skins = values.Length > 11 && !string.IsNullOrEmpty(values[11])
                            ? Convert.ToInt32(values[11].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        Loot = values.Length > 12 ? values[12] : "",
                        Loots = values.Length > 13 && !string.IsNullOrEmpty(values[13])
                            ? Convert.ToInt32(values[13].Replace("\"", "").Replace("\'", ""))
                            : 0,
                        note = values.Length > 15 ? values[15] : ""
                    };

                    records.Add(record);
                }
            }
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(7))
                Notif.notificationManager.Show("Error", "An error occurred while loading the CSV file",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return records;
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
                        var selectedrow = Championlist.SelectedItem as Utils.AccountList;
                        if (selectedrow == null) return;
                        if (header == null) return;
                        DisplayDataWithSearch? secondWindow = null;
                        NoteDisplay? noteWindow = null;

                        switch (header)
                        {
                            case "Champions":
                                secondWindow = new DisplayDataWithSearch(selectedrow.champions);
                                break;
                            case "Skins":
                                secondWindow = new DisplayDataWithSearch(selectedrow.skins);
                                break;
                            case "Notes":
                                noteWindow = new NoteDisplay(selectedrow);
                                break;
                            case "Loot":
                                secondWindow = new DisplayDataWithSearch(selectedrow.Loot);
                                break;
                            case "RiotID"
                                : //otherwise will open op.gg could add this functionality only to "rank" or "riot id" column alternatively 
                                var url =
                                    $"https:/www.op.gg/summoners/{RegionHelperUtil.RegionParser(selectedrow.server)}/{selectedrow.riotID.Replace("#", "-")}";
                                OpenUrl(url);
                                break;
                        }

                        if (secondWindow != null)
                        {
                            await secondWindow.Dispatcher.InvokeAsync(() => { secondWindow.Show(); });

                            while (secondWindow.IsLoaded) await Task.Delay(100);
                        }
                        else if (noteWindow != null)
                        {
                            await noteWindow.Dispatcher.InvokeAsync(() => { noteWindow.Show(); });

                            while (noteWindow.IsLoaded) await Task.Delay(100);
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

    private void openleague1_Copy_Click(object sender, RoutedEventArgs e)
    {
        var namechanger = new ChangeName();
        namechanger.Show();
    }

    private void SecondaryClient_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(Misc.Settings.settingsloaded.riotPath,
            "--launch-product=league_of_legends --launch-patchline=live --allow-multiple-clients");
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
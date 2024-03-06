using CsvHelper;
using CsvHelper.Configuration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using System;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private double running;
    private bool Executing;

    string riotpath = string.Empty;

    public Page1()
    {
        InitializeComponent();
        loaddata();
    }

    public List<accountlist> jotain { get; }
    public static List<accountlist> ActualAccountlists { get; set; }

    public void loaddata()
    {
        try
        {
            string installPath = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game Riot_Client.", "UninstallString", null);
            if (installPath != null && !File.Exists(riotpath))
            {
                notif.notificationManager.Show("Error", "League of legends is not installed or missing registry keys",
                    NotificationType.Error, "WindowArea", onClick: notif.donothing);
            }
            string pattern = "\"(.*?)\"";
            Match match = Regex.Match(installPath, pattern);
            if (match.Success)
            {
                riotpath = match.Groups[1].Value;
            }else
            {
                riotpath = riotpath;
            }
            if (File.Exists(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv"))
            {
                ActualAccountlists =
                    LoadCSV(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv");
            }
            else
            {
                File.Create(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv");
                loaddata();
                return;
            }

            ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
            RemoveDoubleQuotesFromList(ActualAccountlists);
            Championlist.ItemsSource = ActualAccountlists;
            Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
        }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data"); }
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        try { 
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
                   new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv"))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteRecords(ActualAccountlists);
            }
            loaddata();
            Championlist.UpdateLayout();
            Championlist.Items.Refresh();
        }
    }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
}
    }

    public async void ring()
    {
        running += 7;
        edistyy.Progress = running;
    }

    private async void PullData_Click(object sender, RoutedEventArgs e)
    {
        try { 

        var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
        if (leagueclientprocess.Length == 0)
        {
            notif.notificationManager.Show("Error", "League of legends client is not running!", NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
            return;
        }

        if (SelectedUsername == null || SelectedPassword == null) new Window1().ShowDialog();
        Progressgrid.Visibility = Visibility.Visible;
        ring();
        var resp = await lcu.Connector("league", "get", "/lol-service-status/v1/lcu-status", "");
        var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (resp.StatusCode.ToString() == "OK" || 200)
        {
            ring();
            resp = await lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var summonerinfo = JObject.Parse(responseBody2);
            Console.WriteLine(summonerinfo.ToString());
            ring();
            resp = await lcu.Connector("league", "get", "/lol-catalog/v1/items/CHAMPION_SKIN", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var Skininfo = JArray.Parse(responseBody2);
            ring();
            dynamic Champinfo;
            while (true)
                try
                {
                    resp = await lcu.Connector("league", "get",
                        "/lol-champions/v1/inventories/" + summonerinfo["summonerId"] + "/champions-minimal", "");
                    responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Champinfo = JArray.Parse(responseBody2);
                    break;
                }
                catch (Exception ex)
                {
                    var jotain = JToken.Parse(responseBody2);
                    if (jotain["errorCode"] != "RPC_ERROR")
                        Environment.Exit(1);
                    else
                        Thread.Sleep(2000);
                }

            ring();
            resp = await lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var LootInfo = JToken.Parse(responseBody2);
            ring();
            resp = await lcu.Connector("league", "get", "/lol-ranked/v1/current-ranked-stats", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var Rankedinfo = JToken.Parse(responseBody2);
            ring();
            resp = await lcu.Connector("league", "get", "/lol-inventory/v1/wallet/lol_blue_essence", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var Wallet = new wallet();
            Wallet.be = JToken.Parse(responseBody2)["lol_blue_essence"];
            resp = await lcu.Connector("league", "get", "/lol-inventory/v1/wallet/RP", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            Wallet.rp = JToken.Parse(responseBody2)["RP"];
            ring();
            resp = await lcu.Connector("league", "get", "/riotclient/region-locale", "");
            responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var region = JToken.Parse(responseBody2);
            ring();
            var skinlist = "";
            var skincount = 0;
            var champlist = "";
            var champcount = 0;
            var Lootlist = "";
            var Lootcount = 0;
            string Rank = " Rank: " + Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " With: " +
                          Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["wins"] + " Wins and " +
                          Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["losses"] + " Losses";
            foreach (var item in Skininfo)
                if (item["owned"] != "false")
                {
                    skinlist = skinlist + ":" + item["name"];
                    skincount++;
                }

            ring();
            foreach (var item in Champinfo)
                if (item["ownership"]["owned"] != "false")
                {
                    champlist = champlist + ":" + item["name"];
                    champcount++;
                }

            ring();
            foreach (var item in LootInfo)
                foreach (var thing in item)
                    if (thing["count"] > 0)
                    {
                        resp = await lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + thing["lootId"], "");
                        responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        try
                        {
                            var Loot = JObject.Parse(responseBody2);
                            if (Loot["itemDesc"] != "")
                                Lootlist = Lootlist + ":" + Loot["itemDesc"] + " x " + Loot["count"];
                            else if (Loot["localizedName"] != "")
                                Lootlist = Lootlist + ":" + Loot["localizedName"] + " x " + Loot["count"];
                            else
                                Lootlist = Lootlist + ":" + Loot["asset"] + " x " + Loot["count"];
                        }
                        catch (Exception ex)
                        {
                        }
                        Lootcount++;
                    }

            ring();
            ring();
             
            ActualAccountlists.RemoveAll(x => x.username == SelectedUsername);
            ActualAccountlists.Add(new accountlist
            {
                username = SelectedUsername,
                password = SelectedPassword,
                riotID = summonerinfo["gameName"] + "#" + summonerinfo["tagLine"],
                level = summonerinfo["summonerLevel"],
                server = region["region"],
                be = Wallet.be,
                rp = Wallet.rp,
                rank = Rank,
                champions = champlist,
                Champions = champcount.ToString(),
                skins = skinlist,
                Skins = skincount.ToString(),
                Loot = Lootlist,
                Loots = Lootcount.ToString(),
            });

            ring();
            using (var writer =
                   new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv"))
            using (var csv2 = new CsvWriter(writer, config))
            {
                csv2.WriteRecords(ActualAccountlists);
            }
        }

        running = 0;
        Progressgrid.Visibility = Visibility.Hidden;
        Championlist.ItemsSource = ActualAccountlists;
        Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
        NavigationService.Refresh();
        Championlist.Items.Refresh();
        }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data"); }
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try {
            if (!await CheckLeague())  throw new Exception("League not installed"); 
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
        var RiotClient = Process.Start(riotpath,
            "--launch-product=league_of_legends --launch-patchline=live");
        string riotval = string.Empty;
        while (true)
        {
            if (Process.GetProcessesByName("Riot Client").Length != 0)
            {
                riotval = "Riot Client";
                break;
            }
            else if (Process.GetProcessesByName("RiotClientUx").Length != 0)
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
                        Console.WriteLine(siblings.Length);
                        if (siblings.Length <= Array.IndexOf(siblings, checkbox) + 1)
                        {
                            throw new Exception("Not enough siblings found for the checkbox");
                        }

                        int checkBoxIndex = Array.IndexOf(siblings, checkbox);
                        var signInElement = siblings[checkBoxIndex + 1].AsButton();

                        Console.WriteLine($"Found checkbox: {checkbox.Name}");
                        Console.WriteLine($"Found siblings count: {siblings.Length}");

                        if (signInElement.ControlType != ControlType.Button)
                        {
                            throw new Exception("The element following the checkbox is not a button");
                        }

                        var signInButton = signInElement.AsButton();
                        if (signInButton == null) throw new Exception("Login button not found");

                        usernameField.Text = SelectedUsername;
                    passwordField.Text = SelectedPassword;
                    if (signInElement != null)
                    {
                        while (!signInElement.IsEnabled) Thread.Sleep(100);
                        signInElement.Invoke();
                        break;
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Thread.Sleep(100);
            }
        }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data"); }
    }

    private void Championlist_OnKeyDown(object sender, KeyEventArgs e)
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

                Championlist.UpdateLayout();
                Championlist.Items.Refresh();
            }
        }
    }
    public async Task<bool>  CheckLeague()
    {
         if(File.Exists(riotpath))
        {
            return true;
        }
         else { return false; }
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
        try { 
        var processesByName = Process.GetProcessesByName("Riot Client");
        var processesByName2 = Process.GetProcessesByName("LeagueClientUx");
        killleaguefunc();
            if (!await CheckLeague()) throw new Exception("League not installed");

            openleague();
        }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error Opening league"); }
    }

    private  void openleague()
    {
        

        Process.Start(riotpath,
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
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var values = line.Split(';');

                    var record = new accountlist
                    {
                        username = values.Length > 0 ? values[0] : "",
                        password = values.Length > 1 ? values[1] : "",
                        riotID = values.Length > 2 ? values[2] : "",
                        level = values.Length > 3 ? values[3] : "",
                        server = values.Length > 4 ? values[4] : "",
                        be = values.Length > 5 ? values[5] : "",
                        rp = values.Length > 6 ? values[6] : "",
                        rank = values.Length > 7 ? values[7] : "",
                        champions = values.Length > 8 ? values[8] : "",
                        skins = values.Length > 9 ? values[9] : "",
                        Champions = values.Length > 10 ? values[10] : "",
                        Skins = values.Length > 11 ? values[11] : "",
                        Loot = values.Length > 12 ? values[12] : "",
                        Loots = values.Length > 13 ? values[13] : ""
                    };

                    records.Add(record);
                }
            }
        }
        catch (Exception exception)
        {
            notif.notificationManager.Show("Error", "An error occurred while loading the CSV file",
                NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
         { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data"); }
    }


        return records;
    }

    //Hacky fix for now
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
            account.Champions = RemoveDoubleQuotes(account.Champions);
            account.Skins = RemoveDoubleQuotes(account.Skins);
            account.Loots = RemoveDoubleQuotes(account.Loots);
        }
    }

    public static string? RemoveDoubleQuotes(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input.Replace("\"", "");
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
        public string? Champions { get; set; }
        public string? Skins { get; set; }
        public string? Loot { get; set; }
        public string? Loots { get; set; }
    }

    public class wallet
    {
        public string? be { get; set; }
        public string? rp { get; set; }
    }

    private async void Login_Copy_Click(object sender, RoutedEventArgs e)
    {
        try { 
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
            var RiotClient = Process.Start(riotpath,
                "--launch-product=league_of_legends --launch-patchline=live");

            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0 || Process.GetProcessesByName("RiotClientUx").Length != 0)
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
            {
                Dispatcher.Invoke(() =>
                {
                    notif.notificationManager.Show("Error", "Account details are invalid", NotificationType.Error,
                        "WindowArea", onClick: () => notif.donothing());
                });
            }

        });
        }
        catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error logging in"); }
    }

    private void Championlist_Loaded(object sender, RoutedEventArgs e)
    {
        Championlist.Columns[12].Visibility = Visibility.Hidden;
        Championlist.Columns[8].Visibility = Visibility.Hidden;
        Championlist.Columns[9].Visibility = Visibility.Hidden;
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
                        accountlist selectedrow = Championlist.SelectedItem as accountlist;
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
                            await secondWindow.Dispatcher.InvokeAsync(() =>
                            {
                                secondWindow.Show();
                            });

                            while (secondWindow.IsLoaded)
                            {
                                await Task.Delay(100);
                            }
                        }

                    }

                    dataGrid.UnselectAllCells();
                    dataGrid.SelectedItem = null;
                }
          
            }
            catch (Exception exception) { LogManager.GetCurrentClassLogger().Error(exception, "Error loading data"); }
  
            finally
            {
                Executing = false;
            }
        }

        dataGrid.UnselectAllCells();
        dataGrid.SelectedItem = null;

    }
}
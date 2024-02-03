using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CsvHelper;
using CsvHelper.Configuration;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Newtonsoft.Json.Linq;
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
    private double running;


    public Page1()
    {
        InitializeComponent();
        loaddata();
    }

    public List<accountlist> jotain { get; }
    public static List<accountlist> ActualAccountlists { get; set; }

    public void loaddata()
    {
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

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
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
                   new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv"))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteRecords(ActualAccountlists);
            }

            Championlist.UpdateLayout();
            Championlist.Items.Refresh();
        }
    }

    public async void ring()
    {
        running += 7;
        edistyy.Progress = running;
    }

    private async void PullData_Click(object sender, RoutedEventArgs e)
    {
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
            string Rank = " Rank: " + Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " With: " +
                          Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["wins"] + "Wins and " +
                          Rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["losses"] + " Losses";
            foreach (var item in Skininfo)
                if (item["owned"] != "false")
                {
                    skinlist = skinlist + " : " + item["name"];
                    skincount++;
                }

            ring();
            foreach (var item in Champinfo)
                if (item["ownership"]["owned"] != "false")
                {
                    champlist = champlist + " : " + item["name"];
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
                            Lootlist = Lootlist + " : " + Loot["itemDesc"] + "x" + Loot["count"];
                        else if (Loot["localizedName"] != "")
                            Lootlist = Lootlist + " : " + Loot["localizedName"] + "x" + Loot["count"];
                        else
                            Lootlist = Lootlist + " : " + Loot["asset"] + "x" + Loot["count"];
                    }
                    catch (Exception ex)
                    {
                    }
                }

            ring();
            skinlist = skincount + " " + skinlist;
            champlist = champcount + " " + champlist;
            ring();

            ActualAccountlists.RemoveAll(x => x.username == SelectedUsername);
            ActualAccountlists.Add(new accountlist
            {
                username = SelectedUsername, password = SelectedPassword,
                riotID = summonerinfo["gameName"] + "#" + summonerinfo["tagLine"],
                level = summonerinfo["summonerLevel"],
                server = region["region"], be = Wallet.be, rp = Wallet.rp, rank = Rank, champions = champlist,
                skins = skinlist, Loot = Lootlist
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

    private async void Button_Click(object sender, RoutedEventArgs e)
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


        killleaguefunc();
        Process[] leagueProcess;
        var num = 0;
        var RiotClient = Process.Start("C:\\Riot Games\\Riot Client\\RiotClientServices.exe",
            "--launch-product=league_of_legends --launch-patchline=live");

        while (true)
        {
            if (Process.GetProcessesByName("RiotClientUx").Length != 0)
                break;

            Thread.Sleep(2000);
            num++;
            if (num == 5) return;
        }

        while (true)
            try
            {
                var app = Application.Attach("RiotClientUx");

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


                    // Find the login button
                    AutomationElement[] Buttons =
                        riotcontent.FindAllChildren(cf => cf.ByControlType(ControlType.Button));
                    if (Buttons == null) throw new Exception("Login button not found");
                    var signInElement = Buttons.FirstOrDefault(element => element.Name == "Sign in").AsButton();
                    usernameField.Text = SelectedUsername;
                    passwordField.Text = SelectedPassword;

                    while (!signInElement.IsEnabled) Thread.Sleep(100);
                    signInElement.Invoke();


                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Thread.Sleep(100);
            }
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

    private void openleague1_Click(object sender, RoutedEventArgs e)
    {
        var processesByName = Process.GetProcessesByName("RiotClientUx");
        var processesByName2 = Process.GetProcessesByName("LeagueClientUx");
        killleaguefunc();
        openleague();
    }

    private void openleague()
    {
        Process.Start("C:\\Riot Games\\Riot Client\\RiotClientServices.exe",
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
                        username = values[0],
                        password = values[1],
                        riotID = values[2],
                        level = values[3],
                        server = values[4],
                        be = values[5],
                        rp = values[6],
                        rank = values[7],
                        champions = values[8],
                        skins = values[9],
                        Loot = values[10]
                    };

                    records.Add(record);
                }
            }
        }
        catch (Exception ex)
        {
            notif.notificationManager.Show("Error", "An error occurred while loading the CSV file",
                NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
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
        public string? Loot { get; set; }
    }

    public class wallet
    {
        public string? be { get; set; }
        public string? rp { get; set; }
    }

    private async void Login_Copy_Click(object sender, RoutedEventArgs e)
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


        killleaguefunc();
        Process[] leagueProcess;
        var num = 0;
        var RiotClient = Process.Start("C:\\Riot Games\\Riot Client\\RiotClientServices.exe",
            "--launch-product=league_of_legends --launch-patchline=live");

        while (true)
        {
            if (Process.GetProcessesByName("RiotClientUx").Length != 0)
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
            notif.notificationManager.Show("Error", "Account details are invalid", NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
        }

        ;
    }
}
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
using System.Windows;   
using System.Windows.Threading; 
using NLog;
using System.Net.Http;
using HtmlAgilityPack;
using Notification.Wpf;
using Wpf.Ui.Controls;
using Application = FlaUI.Core.Application;
using Button = Wpf.Ui.Controls.Button;
using DataGrid = System.Windows.Controls.DataGrid;
using TextBlock = System.Windows.Controls.TextBlock;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = FlaUI.Core.AutomationElements.TextBox;



namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Accounts.xaml
/// </summary>
public partial class Accounts : Page
{
    private OfflineLauncher offlineLauncher = new OfflineLauncher();
    private DateTime _lastFileChange = DateTime.MinValue;
    private readonly object _fileChangeLock = new();
    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private  FileSystemWatcher? fileWatcher;
    private bool Executing;
    private double running;
    private INotification? _progressNotification;
    public Accounts()
    {

        InitializeComponent();
        Loaded += Accounts_Loaded;
       
    }
    private async void Accounts_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Console.WriteLine("Accounts page loaded.");

            await loaddata();

            Console.WriteLine("LoadData completed. Starting rank update...");

            if (Misc.Settings.settingsloaded.UpdateRanks)
            {
                await UpdateAllRanks();
            }
            

            Console.WriteLine("Rank update finished.");

            // Now setup watcher AFTER everything is loaded
            var filePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                $"{Misc.Settings.settingsloaded.filename}.csv");

            fileWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(filePath) ?? string.Empty,
                Filter = Path.GetFileName(filePath) ?? string.Empty,
                NotifyFilter = NotifyFilters.LastWrite
            };

            fileWatcher.Changed += OnChanged;
            fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR during Accounts_Loaded: {ex.Message}");
        }
    }




    public static List<Utils.AccountList>? ActualAccountlists { get; set; }

    private async void OnChanged(object source, FileSystemEventArgs e)
    {
        lock (_fileChangeLock)
        {
            // debounce spam events
            if ((DateTime.Now - _lastFileChange).TotalMilliseconds < 500)
                return;

            _lastFileChange = DateTime.Now;
        }

        await loaddata();

        Dispatcher.Invoke(() =>
        {
            Championlist.Items.SortDescriptions.Clear();
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

            Dispatcher.Invoke(() =>
            {
                Championlist.ItemsSource = null;
                Championlist.ItemsSource = ActualAccountlists;

                Championlist.Items.SortDescriptions.Clear();
                Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));

                if (Misc.Settings.settingsloaded.DisplayPasswords == false && Championlist.Columns.Count > 1)
                    Championlist.Columns[1].Visibility = Visibility.Hidden;
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
            if (selectedrow == null) return;

            ActualAccountlists?.RemoveAll(r =>
                r.username == selectedrow.username &&
                r.password == selectedrow.password &&
                r.server == selectedrow.server);

            ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");

            using (var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(),
                       $"{Misc.Settings.settingsloaded.filename}.csv")))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteRecords(ActualAccountlists);
            }

            Championlist.ItemsSource = null;
            Championlist.ItemsSource = ActualAccountlists;

            Championlist.Items.SortDescriptions.Clear();
            Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));

            Championlist.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error deleting account");
        }
    }


    public void ring()
    {
        running += 7;
        if (running > 100) running = 100;
        edistyy.Progress = running;
    }

    private async void PullData_Click(object sender, RoutedEventArgs e)
    {
        // Fire-and-forget wrapper for async Task
        _ = PullDataAsync();
    }

    private async Task PullDataAsync()
    {
        try
        {
            running = 0;
            edistyy.Progress = 0;

            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                Notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            if (SelectedUsername == null || SelectedPassword == null)
            {
                new MissingInfo().ShowDialog();
                return;
            }


            var (isBanned, banNote) = await CheckPermanentBanAsync();
            if (isBanned)
            {
                // Kill client
                foreach (var proc in leagueclientprocess)
                {
                    try { proc.Kill(); }
                    catch { }
                }

                // Update account as banned
                ActualAccountlists?.RemoveAll(x => x.username == SelectedUsername && x.password == SelectedPassword);
                ActualAccountlists?.Add(new Utils.AccountList
                {
                    username = SelectedUsername,
                    password = SelectedPassword,
                    riotID = "Banned",
                    level = 0,
                    server = "BANNED",
                    be = 0,
                    rp = 0,
                    rank = "Banned",
                    champions = "",
                    Champions = 0,
                    skins = "",
                    Skins = 0,
                    Loot = "",
                    Loots = 0,
                    rank2 = "Banned",
                    note = banNote
                });

                // Write CSV immediately
                using (var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(),
                           $"{Misc.Settings.settingsloaded.filename}.csv")))
                using (var csv2 = new CsvWriter(writer, config))
                {
                    csv2.WriteRecords(ActualAccountlists);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Progressgrid.Visibility = Visibility.Hidden;
                    Championlist.ItemsSource = null;
                    Championlist.ItemsSource = ActualAccountlists;
                    Championlist.Items.Refresh();
                });

                return; 
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Progressgrid.Visibility = Visibility.Visible;
                ring();
            });
            while (true)
            {
                try
                {
                    var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/summoner-requests-ready", "");
                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    Console.WriteLine("Summoner ready status: " + content);

                    if (content.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                        break;
                }
                catch
                {
                    // LCU not ready yet
                }

                await Task.Delay(1000);
            }
            // Fetch all API data in parallel
            var summonerTask = GetSummonerInfoAsync();
            var skinTask = GetSkinInfoAsync();
            var rankedTask = GetRankedInfoAsync();
            var lootTask = GetLootInfoAsync();
            var walletTask = GetWalletAsync();
            var regionTask = GetRegionAsync();

            await Task.WhenAll(summonerTask, skinTask, rankedTask, lootTask, walletTask, regionTask);

            var summonerInfo = summonerTask.Result;
            if (summonerInfo == null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Progressgrid.Visibility = Visibility.Hidden;
                    Notif.notificationManager.Show("Error", "Could not load summoner info (account banned or not logged in).",
                        NotificationType.Notification,
                        "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                        NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                });
                return;
            }

            var summonerId = summonerInfo["summonerId"]?.ToString();
            if (string.IsNullOrEmpty(summonerId))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Progressgrid.Visibility = Visibility.Hidden;
                    Notif.notificationManager.Show("Error", "SummonerId missing (account banned or invalid response).",
                        NotificationType.Notification,
                        "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                        NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                });
                return;
            }

            // Now get champion info (depends on summonerId)
            var champInfo = await GetChampionInfoAsync(summonerId);

            // Loot info may need async per item
            var lootInfo = lootTask.Result;
            var lootList = new List<string>();
            int lootCount = 0;

            if (lootInfo != null)
            {
                foreach (var item in lootInfo)
                {
                    foreach (var thing in item)
                    {
                        if (thing["count"]?.ToObject<int>() > 0)
                        {
                            try
                            {
                                var lootId = thing["lootId"]?.ToString();
                                if (string.IsNullOrEmpty(lootId)) continue;

                                var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + lootId, "");
                                if (resp == null) continue;

                                var responseBody = await resp.Content.ReadAsStringAsync();
                                var Loot = JObject.Parse(responseBody);

                                string lootText = !string.IsNullOrEmpty(Loot["itemDesc"]?.ToString())
                                    ? Loot["itemDesc"]
                                    : !string.IsNullOrEmpty(Loot["localizedName"]?.ToString())
                                        ? Loot["localizedName"]
                                        : Loot["asset"]?.ToString();

                                lootList.Add($"{lootText} x {Loot["count"]}");
                                lootCount++;
                            }
                            catch { }
                        }
                    }
                }
            }

            // Build ranks
            string BuildRankString(JToken? token, string queueName)
            {
                try
                {
                    if (token == null) return "Unranked";

                    var tier = token["queueMap"]?[queueName]?["tier"]?.ToString();
                    if (string.IsNullOrEmpty(tier)) return "Unranked";

                    var division = token["queueMap"]?[queueName]?["division"]?.ToString();
                    var lp = token["queueMap"]?[queueName]?["leaguePoints"]?.ToString() ?? "0";
                    var wins = token["queueMap"]?[queueName]?["wins"]?.ToString() ?? "0";
                    var losses = token["queueMap"]?[queueName]?["losses"]?.ToString() ?? "0";

                    if (tier == "MASTER" || tier == "GRANDMASTER" || tier == "CHALLENGER")
                        return $"{tier} {lp} LP, {wins} Wins, {losses} Losses";

                    return $"{tier} {division} {lp} LP, {wins} Wins, {losses} Losses";
                }
                catch { return "Unranked"; }
            }

            var rankedInfo = rankedTask.Result;
            var Rank = BuildRankString(rankedInfo, "RANKED_SOLO_5x5");
            var Rank2 = BuildRankString(rankedInfo, "RANKED_FLEX_SR");

            var skinInfo = skinTask.Result;
            var skinList = skinInfo?.Where(i => i["owned"]?.ToObject<bool>() == true)
                                     .Select(i => i["name"].ToString())
                                     .ToList() ?? new List<string>();

            var champList = champInfo?.Where(i => i["ownership"]?["owned"]?.ToObject<bool>() == true)
                                       .Select(i => i["name"].ToString())
                                       .ToList() ?? new List<string>();

            var wallet = walletTask.Result ?? new Utils.Wallet { be = 0, rp = 0 };
            var region = regionTask.Result ?? JObject.Parse("{\"region\":\"UNKNOWN\"}");

            // Update ActualAccountlists
            var note = ActualAccountlists?.FindLast(x => x.username == SelectedUsername && x.password == SelectedPassword);
            ActualAccountlists?.RemoveAll(x => x.username == SelectedUsername && x.password == SelectedPassword);

            ActualAccountlists?.Add(new Utils.AccountList
            {
                username = SelectedUsername,
                password = SelectedPassword,
                riotID = summonerInfo["gameName"] + "#" + summonerInfo["tagLine"],
                level = summonerInfo["summonerLevel"]?.ToObject<int>() ?? 0,
                server = region["region"]?.ToString() ?? "UNKNOWN",
                be = wallet.be,
                rp = wallet.rp,
                rank = Rank,
                champions = string.Join(":", champList),
                Champions = champList.Count,
                skins = string.Join(":", skinList),
                Skins = skinList.Count,
                Loot = string.Join(":", lootList),
                Loots = lootCount,
                rank2 = Rank2,
                note = note?.note
            });

            // Write CSV **outside Dispatcher** (no UI thread required)
            using (var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(),
                       $"{Misc.Settings.settingsloaded.filename}.csv")))
            using (var csv2 = new CsvWriter(writer, config))
            {
                csv2.WriteRecords(ActualAccountlists);
            }

            // Update UI last
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Progressgrid.Visibility = Visibility.Hidden;
                Championlist.ItemsSource = null;
                Championlist.ItemsSource = ActualAccountlists;
                Championlist.Items.SortDescriptions.Clear();
                Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
                Championlist.Items.Refresh();
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Progressgrid.Visibility = Visibility.Hidden;
            });
            LogManager.GetCurrentClassLogger().Error(ex, "Error pulling account data");
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
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var resp = await Lcu.Connector("league", "get",
                    $"/lol-champions/v1/inventories/{summonerId}/champions-minimal", "");

                if (resp == null) return null;

                var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Sometimes returns an error JSON object instead of array
                if (responseBody.TrimStart().StartsWith("{"))
                {
                    var token = JToken.Parse(responseBody);
                    var errorCode = token["errorCode"]?.ToString();

                    if (!string.IsNullOrEmpty(errorCode))
                        return null;
                }

                return JArray.Parse(responseBody);
            }
            catch
            {
                await Task.Delay(1500);
            }
        }

        return null;
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

    private async Task<(bool isBanned, string note)> CheckPermanentBanAsync()
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", "/lol-player-behavior/v3/reform-cards", "");
            if (resp == null) return (false, "");

            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var parsed = JObject.Parse(responseBody);

            foreach (var property in parsed.Properties())
            {
                var data = property.Value["data"];
                if (data == null) continue;

                var penalties = data["penalties"] as JArray;
                if (penalties == null) continue;

                bool permanentBanFound = false;
                var banDetails = new List<string>();

                foreach (var penalty in penalties)
                {
                    var type = penalty["penaltyType"]?.ToString() ?? "";
                    var permanent = penalty["isPermanent"]?.ToObject<bool>() ?? false;

                    if (type == "PERMANENT_BAN" || permanent)
                    {
                        permanentBanFound = true;
                    }

                    banDetails.Add($"{type} (Permanent: {permanent})");
                }

                if (permanentBanFound)
                {
                    // Collect localized info
                    var localized = data["localizedTexts"];
                    string reason = localized?["body"]?.ToString() ?? data["transgressionCategory"]?.ToString() ?? "Unknown ban reason";
                    string title = localized?["title"]?.ToString() ?? "Permanent Ban";
                    string penaltiesText = string.Join("; ", banDetails);

                    string note = $"{title}: {reason} | Penalties: {penaltiesText}";
                    return (true, note);
                }
            }
        }
        catch { }

        return (false, "");
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckLeague()) throw new Exception("League not installed");

            if (Championlist.SelectedCells.Count == 0) throw new Exception("Account not selected");
            var selectedColumn = Championlist.SelectedCells[0].Column;

            if (selectedColumn != null)
            {
                var header = selectedColumn.Header?.ToString();
                var selectedRow = Championlist.SelectedItem as Utils.AccountList;
                if (selectedRow == null || header == null) throw new Exception("Account not selected");
                SelectedUsername = selectedRow.username;
                SelectedPassword = selectedRow.password;
            }

            Console.WriteLine("Username: " + SelectedUsername);
            Console.WriteLine("Password: " + SelectedPassword);

            Utils.killleaguefunc();
            Process[] leagueProcess;
            Process riotProcess;
            var num = 0;
            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            switch (clickedButton.Name)
            {
                case "Login":
                    riotProcess = Process.Start(Misc.Settings.settingsloaded.riotPath, "--launch-product=league_of_legends --launch-patchline=live");
                    break;

                case "Stealthlogin":
                    riotProcess = await offlineLauncher.LaunchRiotOrLeagueOfflineAsync(Misc.Settings.settingsloaded.riotPath);
                    break;
            }

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


                Thread.Sleep(200);
                num++;
                if (num == 20) return;
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
                            WaitForSummonerReadyAsync();
                            break;
                        }

                        Thread.Sleep(500);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Thread.Sleep(200);
                }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }


    private async Task WaitForSummonerReadyAsync()
    {
        while (true)
        {
            try
            {
                var resp = await Lcu.Connector("league", "get", "/lol-player-behavior/v3/reform-cards", "");

                if (resp != null && resp.IsSuccessStatusCode) // Ensure HTTP 200
                {
                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Optional: you can parse content here or just call PullDataAsync
                    await PullDataAsync();
                    return; // exit the loop
                }
            }
            catch
            {
                // LCU not ready yet, ignore and retry
            }

            await Task.Delay(1000); // retry every 1 second
        }
    }
    public async Task UpdateAllRanks()
    {
        if (ActualAccountlists == null || ActualAccountlists.Count == 0)
            return;

        int total = ActualAccountlists.Count;
        int processed = 0;
        bool anyChanges = false;

        ProgressWindow progressWindow = null!;

        // Show progress window
        Dispatcher.Invoke(() =>
        {
            progressWindow = new ProgressWindow(total)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            progressWindow.Show();
            progressWindow.FollowOwner();
        });

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

        foreach (var account in ActualAccountlists)
        {
            processed++;

            // Keep window following main window
            Dispatcher.Invoke(() => progressWindow.FollowOwner());

            try
            {
                if (string.IsNullOrWhiteSpace(account.riotID) || string.IsNullOrWhiteSpace(account.server))
                    continue;

                var formattedRiotId = account.riotID.Replace("#", "-");
                var url = $"https://www.leagueofgraphs.com/summoner/{account.server}/{formattedRiotId}";

                string html = await http.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var rankingBox = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'summoner-rankings')]");
                if (rankingBox == null) continue;

                string? soloRank = null;
                string? flexRank = null;

                // Parse Soloqueue
                var soloNode = rankingBox.SelectSingleNode(".//span[contains(@class,'queue') and contains(text(),'Soloqueue')]");
                if (soloNode != null)
                {
                    var tier = rankingBox.SelectSingleNode(".//div[contains(@class,'leagueTier')]")?.InnerText.Trim();
                    var lp = rankingBox.SelectSingleNode(".//span[contains(@class,'leaguePoints')]")?.InnerText.Trim();
                    var wins = rankingBox.SelectSingleNode(".//span[contains(@class,'winsNumber')]")?.InnerText.Trim();
                    var losses = rankingBox.SelectSingleNode(".//span[contains(@class,'lossesNumber')]")?.InnerText.Trim();

                    if (!string.IsNullOrWhiteSpace(tier))
                        soloRank = $"{tier} {lp} LP, {wins}W / {losses}L";
                }
                Dispatcher.Invoke(() => progressWindow.FollowOwner());
                // Parse Flex queue
                var flexNode = rankingBox.SelectSingleNode(".//div[contains(@class,'queueName') and contains(text(),'Flex')]");
                if (flexNode != null)
                {
                    var container = flexNode.SelectSingleNode("./ancestor::div[contains(@class,'img-align-block')]");
                    var tier = container?.SelectSingleNode(".//div[contains(@class,'leagueTier')]")?.InnerText.Trim();
                    var lp = container?.SelectSingleNode(".//span[contains(@class,'leaguePoints')]")?.InnerText.Trim();
                    var wins = container?.SelectSingleNode(".//span[contains(@class,'winsNumber')]")?.InnerText.Trim();
                    var losses = container?.SelectSingleNode(".//span[contains(@class,'lossesNumber')]")?.InnerText.Trim();

                    if (!string.IsNullOrWhiteSpace(tier))
                        flexRank = $"{tier} {lp} LP, {wins}W / {losses}L";
                }

                // Update ranks if parsed
                if (!string.IsNullOrWhiteSpace(soloRank))
                {
                    account.rank = soloRank;
                    anyChanges = true;
                }

                if (!string.IsNullOrWhiteSpace(flexRank))
                {
                    account.rank2 = flexRank;
                    anyChanges = true;
                }

                await Task.Delay(800); // prevent rate limiting
            }
            catch
            {
                // ignore individual account errors
            }

            // Update progress bar
            Dispatcher.Invoke(() => progressWindow.UpdateProgress(processed));
        }

        // Save CSV if any changes
        if (anyChanges)
        {
            using var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), $"{Misc.Settings.settingsloaded.filename}.csv"));
            using var csv = new CsvWriter(writer, config);
            csv.WriteRecords(ActualAccountlists);

            Dispatcher.Invoke(() => Championlist.Items.Refresh());
        }

        // Close progress window
        Dispatcher.Invoke(() => progressWindow.Close());
    }


    private void Championlist_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;

        try
        {
            var selectedrow = Championlist.SelectedItem as Utils.AccountList;
            if (selectedrow == null) return;

            ActualAccountlists?.RemoveAll(r =>
                r.username == selectedrow.username &&
                r.password == selectedrow.password &&
                r.server == selectedrow.server);

            using (var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(),
                       $"{Misc.Settings.settingsloaded.filename}.csv")))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteRecords(ActualAccountlists);
            }

            Championlist.ItemsSource = null;
            Championlist.ItemsSource = ActualAccountlists;

            Championlist.Items.SortDescriptions.Clear();
            Championlist.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));

            Championlist.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error deleting account with delete key");
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
        try
        {
            if (!string.IsNullOrEmpty(champfilter.Text))
            {
                var search = champfilter.Text;

                var filteredList = ActualAccountlists?
                    .Where(word =>
                        (word.champions ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.skins ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.Loot ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.server ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.riotID ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    .ToList();

                Championlist.ItemsSource = filteredList;
            }
            else
            {
                Championlist.ItemsSource = ActualAccountlists;
            }

            Championlist.UpdateLayout();
            Championlist.Items.Refresh();
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error filtering accounts");
        }
    }


    public async Task<List<Utils.AccountList>> LoadCSV(string filePath)
    {
        var records = new List<Utils.AccountList>();

        try
        {
            // Wait until file is not locked
            while (true)
            {
                try
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        break;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(300);
                }
            }

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, config))
            {
                // If file is empty, return empty list
                if (!csv.Read())
                    return records;

                // Read header if exists
                csv.ReadHeader();

                while (true)
                {
                    try
                    {
                        if (!csv.Read())
                            break;

                        var record = new Utils.AccountList
                        {
                            username = csv.GetField(0) ?? "",
                            password = csv.GetField(1) ?? "",
                            riotID = csv.GetField(2) ?? "",
                            level = TryParseInt(csv.GetField(3)),
                            server = csv.GetField(4) ?? "",
                            be = TryParseInt(csv.GetField(5)),
                            rp = TryParseInt(csv.GetField(6)),
                            rank = csv.GetField(7) ?? "",
                            champions = csv.GetField(8) ?? "",
                            skins = csv.GetField(9) ?? "",
                            Champions = TryParseInt(csv.GetField(10)),
                            Skins = TryParseInt(csv.GetField(11)),
                            Loot = csv.GetField(12) ?? "",
                            Loots = TryParseInt(csv.GetField(13)),
                            rank2 = csv.GetField(14) ?? "",
                            note = csv.GetField(15) ?? ""
                        };

                        records.Add(record);
                    }
                    catch
                    {
                        // skip broken row
                    }
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

            LogManager.GetCurrentClassLogger().Error(exception, "Error loading CSV");
        }

        return records;
    }


    private int TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        value = value.Replace("\"", "").Replace("\'", "").Trim();

        return int.TryParse(value, out var result) ? result : 0;
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

    private  void Accounts_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.C)
        {
            var dataGrid = Championlist;
            if (dataGrid == null) return;
            if (dataGrid != null && dataGrid.CurrentCell != null)
            {
            var selectedColumn = dataGrid.CurrentCell.Column;

            if (selectedColumn != null)
            {
                Console.WriteLine(2);
                var header = selectedColumn.Header?.ToString();
                var selectedRow = Championlist.SelectedItem as Utils.AccountList;
                if (selectedRow == null || header == null) return;

                Clipboard.SetText(selectedRow.username + ":" + selectedRow.password +
                                  " Server: " + selectedRow.server +
                                  " RiotID: " + selectedRow.riotID +
                                  " Champions: " + selectedRow.Champions +
                                  " Skins: " + selectedRow.Skins +
                                  " BE: " + selectedRow.be +
                                  " RP: " + selectedRow.rp);

                e.Handled = true;
                Notif.notificationManager.Show("Info", "Account " + selectedRow.riotID + " has been copied to clipboard",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null,
                    () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            }
            }
        }
    }

    private void DuplicateRemover_OnClick(object sender, RoutedEventArgs e)
    {

            if (ActualAccountlists == null) return;

            ActualAccountlists = ActualAccountlists
                .GroupBy(x => (x.username ?? "").Trim().ToLower() + "|" + (x.password ?? "").Trim())
                .Select(g => g.First())
                .ToList();
            using (var writer = new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(),
                       $"{Misc.Settings.settingsloaded.filename}.csv")))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.WriteRecords(ActualAccountlists);
            }
        
}
}
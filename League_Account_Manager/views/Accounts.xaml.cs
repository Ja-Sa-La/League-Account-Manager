using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
// Added for MessageBox
using Application = FlaUI.Core.Application;
using Button = Wpf.Ui.Controls.Button;
using DataGrid = System.Windows.Controls.DataGrid;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;


namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Accounts.xaml
/// </summary>
public partial class Accounts : Page
{
    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private readonly Dictionary<string, ListSortDirection?> _columnSortState = new();
    private readonly object _fileChangeLock = new();
    private readonly AuthRouteLauncher _launcher = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private readonly OfflineLauncher offlineLauncher = new();
    private bool _initialized;
    private DateTime _lastFileChange = DateTime.MinValue;
    private DateTime _lastKnownFileWrite = DateTime.MinValue;
    private bool _pendingReload;
    private INotification? _progressNotification;
    private bool Executing;
    private FileSystemWatcher? fileWatcher;

    public Accounts()
    {
        InitializeComponent();
        Loaded += Accounts_Loaded;
        Unloaded += Accounts_Unloaded;
        IsVisibleChanged += Accounts_IsVisibleChanged;
        Misc.Settings.AccountPasswordSupplied += OnAccountPasswordSupplied;
        AccountFileStore.AccountsFileUpdated += OnAccountsFileUpdated;
    }

    public static List<Utils.AccountList>? ActualAccountlists { get; set; }
    public static event Action? PullDataCompleted;

    private async void Accounts_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        var filePath = AccountFileStore.GetAccountsFilePath();
        if (File.Exists(filePath))
        {
            var lastWrite = File.GetLastWriteTimeUtc(filePath);
            if (lastWrite <= _lastKnownFileWrite && !_pendingReload) return;
        }

        _pendingReload = false;
        await LoadDataAsync();
    }

    private void Accounts_Unloaded(object sender, RoutedEventArgs e)
    {
        if (fileWatcher != null)
        {
            fileWatcher.EnableRaisingEvents = false;
            fileWatcher.Changed -= OnChanged;
            fileWatcher.Dispose();
            fileWatcher = null;
        }
    }

    private async void OnAccountsFileUpdated(object? sender, EventArgs e)
    {
        _pendingReload = true;
        if (!IsLoaded) return;
        try
        {
            _pendingReload = false;
            await Dispatcher.InvokeAsync(async () => { await LoadDataAsync(); });
        }
        catch
        {
        }
    }

    private async void Accounts_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            DebugConsole.WriteLine("[Accounts] Page loaded");

            await LoadDataAsync();
            var accountsFilePath = AccountFileStore.GetAccountsFilePath();
            if (File.Exists(accountsFilePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(accountsFilePath);
                if (lastWrite > _lastKnownFileWrite)
                    await LoadDataAsync();
            }

            if (_pendingReload)
            {
                _pendingReload = false;
                await LoadDataAsync();
            }

            DebugConsole.WriteLine("[Accounts] LoadData completed. Starting rank update...");

            if (Misc.Settings.settingsloaded.UpdateRanks) await UpdateAllRanks();

            DebugConsole.WriteLine("[Accounts] Rank update finished");

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
            _logger.Error(ex, "Error during Accounts_Loaded");
            DebugConsole.WriteLine($"[Accounts] ERROR during Accounts_Loaded: {ex.Message}", ConsoleColor.Red);
        }
    }

    private async void OnAccountPasswordSupplied()
    {
        try
        {
            // Refresh account list when password is entered at startup
            await Dispatcher.InvokeAsync(async () => { await LoadDataAsync(); });
        }
        catch
        {
        }
    }

    private void OnAccountsDataGridSorting(object? sender, DataGridSortingEventArgs e)
    {
        try
        {
            if (e.Column == null) return;
            var header = e.Column.Header?.ToString();
            if (header != "SoloQ" && header != "FlexQ") return;

            e.Handled = true; // we'll provide custom sort

            var list = AccountsDataGrid.ItemsSource as IEnumerable<Utils.AccountList>;
            if (list == null) return;

            _columnSortState.TryGetValue(header, out var current);
            var newDirection = current == ListSortDirection.Descending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            var keys = _columnSortState.Keys.ToList();
            foreach (var k in keys)
                if (k != header)
                    _columnSortState.Remove(k);
            _columnSortState[header] = newDirection;

            Func<Utils.AccountList, string?> getRank = x => header == "SoloQ" ? x.rank : x.rank2;

            IOrderedEnumerable<Utils.AccountList> sorted;
            if (newDirection == ListSortDirection.Descending)
                sorted = list.OrderBy(x => string.IsNullOrWhiteSpace(getRank(x)) ? 1 : 0)
                    .ThenByDescending(x => ParseRankValue(getRank(x)));
            else
                sorted = list.OrderBy(x => string.IsNullOrWhiteSpace(getRank(x)) ? 1 : 0)
                    .ThenBy(x => ParseRankValue(getRank(x)));

            foreach (var col in AccountsDataGrid.Columns) col.SortDirection = col == e.Column ? newDirection : null;

            AccountsDataGrid.ItemsSource = sorted.ToList();
            AccountsDataGrid.Items.Refresh();
        }
        catch
        {
        }
    }

    private double ParseRankValue(string? rankText)
    {
        if (string.IsNullOrWhiteSpace(rankText)) return 0;

        try
        {
            var text = rankText.ToUpperInvariant();

            var special = new[] { "CHALLENGER", "GRANDMASTER", "MASTER" };
            foreach (var s in special)
                if (text.StartsWith(s))
                {
                    var lp = ExtractNumberAfter(text, "LP") ?? 0;
                    return 100000 + Array.IndexOf(special, s) * 1000 + lp;
                }

            var tiers = new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "DIAMOND", "EMERALD", "MASTER" };

            foreach (var tier in tiers.Reverse())
                if (text.Contains(tier))
                {
                    var tierIndex = Array.IndexOf(tiers, tier);
                    var divisionValue = 0;
                    if (text.Contains(" I ") || text.EndsWith(" I")) divisionValue = 4;
                    else if (text.Contains(" II ") || text.EndsWith(" II")) divisionValue = 3;
                    else if (text.Contains(" III ") || text.EndsWith(" III")) divisionValue = 2;
                    else if (text.Contains(" IV ") || text.EndsWith(" IV")) divisionValue = 1;
                    else divisionValue = 0;

                    var lp = ExtractNumberAfter(text, "LP") ?? 0;

                    return (tierIndex + 1) * 10000 + divisionValue * 100 + lp;
                }


            if (text.Contains("UNRANKED") || text.Contains("UNRANKED"))
            {
                var ironIndex = Array.IndexOf(tiers, "IRON");
                return (ironIndex + 1) * 10000 - 50;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private int? ExtractNumberAfter(string text, string marker)
    {
        try
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var sub = text.Substring(0, idx);
            var parts = sub.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
                if (int.TryParse(parts[i].Replace("LP", ""), out var v))
                    return v;
        }
        catch
        {
        }

        return null;
    }

    private async void OnChanged(object source, FileSystemEventArgs e)
    {
        lock (_fileChangeLock)
        {
            if ((DateTime.Now - _lastFileChange).TotalMilliseconds < 500) return;
            _lastFileChange = DateTime.Now;
        }

        await LoadDataAsync();

        if (Dispatcher?.HasShutdownStarted == true || Dispatcher?.HasShutdownFinished == true) return;
        await Dispatcher.InvokeAsync(() =>
        {
            AccountsDataGrid.Items.SortDescriptions.Clear();
            AccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
        }, DispatcherPriority.Background, CancellationToken.None);
    }


    public async Task LoadDataAsync()
    {
        try
        {
            await Task.Run(async () =>
            {
                var filePath = AccountFileStore.GetAccountsFilePath();

                if (File.Exists(filePath))
                {
                    ActualAccountlists = await AccountFileStore.LoadAsync(filePath, config);
                    _lastKnownFileWrite = File.GetLastWriteTimeUtc(filePath);
                }
                else
                {
                    ActualAccountlists = new List<Utils.AccountList>();
                    await AccountFileStore.SaveAsync(filePath, ActualAccountlists, config);
                    _lastKnownFileWrite = File.GetLastWriteTimeUtc(filePath);
                }

                ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");
                Utils.RemoveDoubleQuotesFromList(ActualAccountlists);
            });

            Dispatcher.Invoke(() =>
            {
                AccountsDataGrid.ItemsSource = null;
                AccountsDataGrid.ItemsSource = ActualAccountlists;

                AccountsDataGrid.Items.SortDescriptions.Clear();
                AccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));

                if (!Misc.Settings.settingsloaded.DisplayPasswords && AccountsDataGrid.Columns.Count > 1)
                    AccountsDataGrid.Columns[1].Visibility = Visibility.Hidden;
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
            try
            {
                DebugConsole.WriteLine($"[Accounts] Error loading data: {exception.Message}", ConsoleColor.Red);
            }
            catch
            {
                // ignore debug console errors
            }
        }
    }


    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedAccountAsync();
    }


    // Initialize the task list UI and provide helper to mark tasks complete
    private void InitializeProgressTasks(IEnumerable<string> tasks)
    {
        Dispatcher.Invoke(() =>
        {
            var box = FindName("TaskListBox") as ListBox;
            if (box == null) return;
            box.Items.Clear();
            foreach (var t in tasks)
            {
                var item = new ListBoxItem { Tag = t, Content = $"◻ {t}", Foreground = Brushes.White };
                box.Items.Add(item);
            }

            // Resize box height to fit all items so no scrolling is necessary.
            try
            {
                const double approxItemHeight = 18.0; // matches ListBoxItem Height in XAML
                var desired = box.Items.Count * approxItemHeight + box.Padding.Top + box.Padding.Bottom + 8;
                box.Height = desired;
            }
            catch
            {
                // ignore resizing errors
            }
        });
    }

    private void MarkTaskCompleted(string taskName)
    {
        Dispatcher.Invoke(() =>
        {
            var box = FindName("TaskListBox") as ListBox;
            if (box == null) return;
            foreach (ListBoxItem item in box.Items)
                if (item.Tag as string == taskName)
                {
                    item.Content = $"✔ {taskName}";
                    break;
                }
        });
    }

    private async void OnPullDataClick(object sender, RoutedEventArgs e)
    {
        // Fire-and-forget wrapper for async Task
        _ = PullDataAsync();
    }

    private async Task PullDataAsync()
    {
        try
        {
            // show spinner and task list

            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                Notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            // Try to select an account from any id token provided by the Riot client
            try
            {
                var authResp =
                    await Lcu.Connector("riot", "get", "/riot-client-auth/v1/authorization", "") as HttpResponseMessage;
                if (authResp != null)
                {
                    var authBody = await authResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        var authJson = JObject.Parse(authBody);
                        var idToken = authJson["idToken"]?["token"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(idToken)) TrySelectAccountFromIdToken(idToken);
                    }
                    catch
                    {
                        // ignore parse errors
                    }
                }
            }
            catch
            {
                // ignore errors contacting riot client for id token
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
                    try
                    {
                        proc.Kill();
                    }
                    catch
                    {
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
                    AccountsDataGrid.ItemsSource = null;
                    AccountsDataGrid.ItemsSource = ActualAccountlists;
                    AccountsDataGrid.Items.Refresh();
                });

                return;
            }

            Dispatcher.Invoke(() =>
            {
                Progressgrid.Visibility = Visibility.Visible;
                InitializeProgressTasks(new[]
                {
                    "Waiting for summoner readiness",
                    "Fetch summoner info",
                    "Fetch skins",
                    "Fetch ranked info",
                    "Fetch loot",
                    "Fetch wallet",
                    "Fetch region",
                    "Fetch champions"
                });
            });
            while (true)
            {
                try
                {
                    var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/summoner-requests-ready", "");
                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    DebugConsole.WriteLine($"[Accounts] Summoner ready status: {content}");

                    if (content.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        MarkTaskCompleted("Waiting for summoner readiness");
                        break;
                    }
                }
                catch
                {
                    // LCU not ready yet
                }

                await Task.Delay(1000);
            }

            // Fetch all API data in parallel and mark tasks as they complete
            var summonerTask = Task.Run(async () =>
            {
                var res = await GetSummonerInfoAsync();
                if (res != null) MarkTaskCompleted("Fetch summoner info");
                return res;
            });

            var skinTask = Task.Run(async () =>
            {
                var res = await GetSkinInfoAsync();
                if (res != null) MarkTaskCompleted("Fetch skins");
                return res;
            });

            var rankedTask = Task.Run(async () =>
            {
                var res = await GetRankedInfoAsync();
                if (res != null) MarkTaskCompleted("Fetch ranked info");
                return res;
            });

            var lootTask = Task.Run(async () =>
            {
                var res = await GetLootInfoAsync();
                if (res != null) MarkTaskCompleted("Fetch loot");
                return res;
            });

            var walletTask = Task.Run(async () =>
            {
                var res = await GetWalletAsync();
                if (res != null) MarkTaskCompleted("Fetch wallet");
                return res;
            });

            var regionTask = Task.Run(async () =>
            {
                var res = await GetRegionAsync();
                if (res != null) MarkTaskCompleted("Fetch region");
                return res;
            });

            await Task.WhenAll(summonerTask, skinTask, rankedTask, lootTask, walletTask, regionTask);

            var summonerInfo = summonerTask.Result;
            if (summonerInfo == null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Progressgrid.Visibility = Visibility.Hidden;
                    Notif.notificationManager.Show("Error",
                        "Could not load summoner info (account banned or not logged in).",
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
            if (champInfo != null) MarkTaskCompleted("Fetch champions");

            // Loot info may need async per item
            var lootInfo = lootTask.Result;
            var lootList = new List<string>();
            var lootCount = 0;

            if (lootInfo != null)
                foreach (var item in lootInfo)
                foreach (var thing in item)
                    if (thing["count"]?.ToObject<int>() > 0)
                        try
                        {
                            var lootId = thing["lootId"]?.ToString();
                            if (string.IsNullOrEmpty(lootId)) continue;

                            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + lootId, "");
                            if (resp == null) continue;

                            var responseBody = await resp.Content.ReadAsStringAsync();
                            var Loot = JObject.Parse(responseBody);

                            string lootText = !string.IsNullOrEmpty(Loot["itemDesc"]?.ToString())
                                ? Loot["itemDesc"]?.ToString()
                                : !string.IsNullOrEmpty(Loot["localizedName"]?.ToString())
                                    ? Loot["localizedName"]?.ToString()
                                    : Loot["asset"]?.ToString() ?? string.Empty;

                            // Map tilePath/imagePath to raw.communitydragon.org paths
                            string? iconUrl = null;
                            var tilePath = Loot["tilePath"]?.ToString() ??
                                           Loot["imagePath"]?.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(tilePath))
                                try
                                {
                                    var tail = tilePath.TrimStart('/');

                                    // Special handling for /fe/ frontend assets (e.g. /fe/lol-loot/...)
                                    var parts = tail.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2 && parts[0].Equals("fe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // e.g. fe/lol-loot/assets/loot_item_icons/...
                                        // map to plugins/rcp-fe-lol-loot/global/default/<rest after fe/lol-loot/>
                                        if (parts.Length >= 2 && parts[1].Equals("lol-loot",
                                                StringComparison.OrdinalIgnoreCase))
                                        {
                                            var rest = string.Join('/', parts.Skip(2));
                                            var mapped = $"plugins/rcp-fe-lol-loot/global/default/{rest}"
                                                .Replace('\\', '/').ToLowerInvariant();
                                            while (mapped.Contains("//")) mapped = mapped.Replace("//", "/");
                                            iconUrl = $"raw.communitydragon.org/latest/{mapped}";
                                        }
                                        else
                                        {
                                            // generic fe mapping fallback
                                            var rest = string.Join('/', parts.Skip(1));
                                            var mapped = $"plugins/rcp-fe-lol-loot/global/default/{rest}"
                                                .Replace('\\', '/').ToLowerInvariant();
                                            while (mapped.Contains("//")) mapped = mapped.Replace("//", "/");
                                            iconUrl = $"raw.communitydragon.org/latest/{mapped}";
                                        }
                                    }
                                    else
                                    {
                                        // Remove ASSETS/ or lol-game-data/ prefix if present
                                        var idx = tail.IndexOf("ASSETS/", StringComparison.OrdinalIgnoreCase);
                                        if (idx >= 0)
                                        {
                                            tail = tail.Substring(idx + "ASSETS/".Length);
                                        }
                                        else
                                        {
                                            var lgd = "lol-game-data/";
                                            var lgdIdx = tail.IndexOf(lgd, StringComparison.OrdinalIgnoreCase);
                                            if (lgdIdx >= 0)
                                                tail = tail.Substring(lgdIdx + lgd.Length);
                                        }

                                        // Remove any leading "assets/" segments to avoid duplication
                                        while (tail.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                                            tail = tail.Substring("assets/".Length);

                                        tail = tail.TrimStart('/');

                                        string mapped;
                                        // v1/v2 or content paths should not get an extra assets/ prefix
                                        if (tail.StartsWith("v1/", StringComparison.OrdinalIgnoreCase) ||
                                            tail.StartsWith("v2/", StringComparison.OrdinalIgnoreCase) ||
                                            tail.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                                            mapped = $"plugins/rcp-be-lol-game-data/global/default/{tail}"
                                                .Replace('\\', '/').ToLowerInvariant();
                                        else
                                            mapped = $"plugins/rcp-be-lol-game-data/global/default/assets/{tail}"
                                                .Replace('\\', '/').ToLowerInvariant();

                                        while (mapped.Contains("//")) mapped = mapped.Replace("//", "/");
                                        iconUrl = $"raw.communitydragon.org/latest/{mapped}";
                                    }
                                }
                                catch
                                {
                                    iconUrl = null;
                                }

                            var countVal = Loot["count"]?.ToString() ?? thing["count"]?.ToString() ?? "1";

                            // Log the parsed loot info for debugging
                            try
                            {
                                var msg =
                                    $"[Accounts] Loot parsed: LootId='{lootId}', Name='{lootText}', TilePath='{tilePath}', IconUrl='{iconUrl}', Count='{countVal}'";
                                _logger.Debug(msg);
                                DebugConsole.WriteLine(msg);
                                if (iconUrl == null)
                                {
                                    var noIcon =
                                        $"[Accounts] Loot mapping produced no icon for LootId='{lootId}', TilePath='{tilePath}'";
                                    _logger.Debug(noIcon);
                                    DebugConsole.WriteLine(noIcon);
                                }
                            }
                            catch
                            {
                                // ignore logging errors
                            }

                            // Format as name|url|count
                            var entryParts = new List<string> { lootText ?? string.Empty };
                            entryParts.Add(iconUrl ?? string.Empty);
                            entryParts.Add(countVal);
                            lootList.Add(string.Join("|", entryParts));
                            lootCount++;
                        }
                        catch
                        {
                            try
                            {
                                var warn = $"[Accounts] Exception parsing loot item LootId='{thing?["lootId"]}'";
                                _logger.Warn(warn);
                                DebugConsole.WriteLine(warn, ConsoleColor.Yellow);
                            }
                            catch
                            {
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
                catch
                {
                    return "Unranked";
                }
            }

            var rankedInfo = rankedTask.Result;
            var Rank = BuildRankString(rankedInfo, "RANKED_SOLO_5x5");
            var Rank2 = BuildRankString(rankedInfo, "RANKED_FLEX_SR");

            var skinInfo = skinTask.Result;
            var skinList = new List<string>();
            if (skinInfo != null)
                foreach (var item in skinInfo)
                    try
                    {
                        var owned = item["owned"]?.ToObject<bool>() ?? false;
                        if (!owned) continue;

                        var name = item["name"]?.ToString() ?? string.Empty;
                        var itemId = item["itemId"]?.ToString() ?? string.Empty;

                        // Find champion id from tags like "champions_30" or fallback to parse imagePath/tilePath
                        int? champId = null;
                        var tags = item["tags"] as JArray;
                        if (tags != null)
                            foreach (var t in tags)
                            {
                                var ts = t.ToString();
                                if (ts.StartsWith("champions_", StringComparison.OrdinalIgnoreCase))
                                    if (int.TryParse(ts.Substring("champions_".Length), out var parsed))
                                    {
                                        champId = parsed;
                                        break;
                                    }
                            }

                        // Fallback: try to extract champion and skin ids from imagePath or tilePath
                        if (champId == null)
                        {
                            var imagePath = item["imagePath"]?.ToString() ??
                                            item["tilePath"]?.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(imagePath))
                                try
                                {
                                    // look for "/<champId>/<skinId>" segments, e.g. /.../30/30016.png
                                    var pathParts = imagePath.Split(new[] { '/' },
                                        StringSplitOptions.RemoveEmptyEntries);
                                    for (var pi = 0; pi + 1 < pathParts.Length; pi++)
                                        if (int.TryParse(pathParts[pi], out var a) &&
                                            int.TryParse(Path.GetFileNameWithoutExtension(pathParts[pi + 1]),
                                                out var b))
                                        {
                                            // heuristics: champion ids are usually < 2000 while skin ids can be larger
                                            champId = a;
                                            // if itemId is empty, we can set itemId = b? but we already have itemId from JSON
                                            break;
                                        }
                                }
                                catch
                                {
                                    // ignore fallback parse errors
                                }
                        }

                        // Build icon url (prefer mapping from tilePath/imagePath to raw.communitydragon.org and store without scheme)
                        string? iconUrl = null;
                        var tilePath = item["tilePath"]?.ToString() ?? item["imagePath"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(tilePath))
                            try
                            {
                                // Normalize tilePath and strip known prefixes so we don't end up with
                                // duplicate "assets" segments in the mapped URL.
                                var tail = tilePath.TrimStart('/');

                                // If tilePath contains an ASSETS/ marker, take the remainder after it
                                var idx = tail.IndexOf("ASSETS/", StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    tail = tail.Substring(idx + "ASSETS/".Length);
                                }
                                else
                                {
                                    // Remove leading "lol-game-data/" if present
                                    var lgd = "lol-game-data/";
                                    var lgdIdx = tail.IndexOf(lgd, StringComparison.OrdinalIgnoreCase);
                                    if (lgdIdx >= 0)
                                        tail = tail.Substring(lgdIdx + lgd.Length);
                                }

                                // Remove any leading "assets/" segments to avoid assets/assets
                                while (tail.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                                    tail = tail.Substring("assets/".Length);

                                tail = tail.TrimStart('/');

                                // Construct the communitydragon raw path and normalize
                                string mapped;
                                if (tail.StartsWith("v1/", StringComparison.OrdinalIgnoreCase) ||
                                    tail.StartsWith("v2/", StringComparison.OrdinalIgnoreCase) ||
                                    tail.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                                    mapped = $"plugins/rcp-be-lol-game-data/global/default/{tail}".Replace('\\', '/')
                                        .ToLowerInvariant();
                                else
                                    mapped = $"plugins/rcp-be-lol-game-data/global/default/assets/{tail}"
                                        .Replace('\\', '/').ToLowerInvariant();

                                while (mapped.Contains("//")) mapped = mapped.Replace("//", "/");
                                iconUrl = $"raw.communitydragon.org/latest/{mapped}";
                            }
                            catch
                            {
                                iconUrl = null;
                            }

                        // No fallback to cdn.communitydragon.org: rely on tilePath/imagePath mapping only
                        // Extract price (first price entry)
                        string? price = null;
                        var prices = item["prices"] as JArray;
                        if (prices != null && prices.Count > 0)
                        {
                            var p = prices.First;
                            var cost = p["cost"]?.ToString();
                            var currency = p["currency"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(cost))
                                price = !string.IsNullOrWhiteSpace(currency) ? $"{cost} {currency}" : cost;
                        }

                        // Format as name|url|price (price optional)
                        var parts = new List<string> { name };
                        parts.Add(iconUrl ?? string.Empty);
                        parts.Add(price ?? string.Empty);
                        var entry = string.Join("|", parts);
                        skinList.Add(entry);
                        DebugConsole.WriteLine(
                            $"[Accounts] Parsed skin entry: Name='{name}', ItemId='{itemId}', ChampId='{champId?.ToString() ?? "null"}', IconUrl='{iconUrl}', Price='{price}' -> '{entry}'");
                    }
                    catch
                    {
                        // skip problematic skin
                    }

            // Build champion entries as name|iconUrl|roles (roles comma-separated). Map squarePortraitPath to raw.communitydragon.org
            var champList = new List<string>();
            if (champInfo != null)
                foreach (var c in champInfo)
                    try
                    {
                        var owned = c["ownership"]?["owned"]?.ToObject<bool>() ?? false;
                        if (!owned) continue;

                        var name = c["name"]?.ToString() ?? string.Empty;
                        // roles array -> comma separated
                        var rolesArr = c["roles"] as JArray;
                        var roles = rolesArr != null && rolesArr.Count > 0
                            ? string.Join(",", rolesArr.Select(r => r.ToString()))
                            : string.Empty;

                        string? iconUrl = null;
                        var portrait = c["squarePortraitPath"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(portrait))
                            try
                            {
                                var tail = portrait.TrimStart('/');
                                // If contains ASSETS/, take remainder after it
                                var idx = tail.IndexOf("ASSETS/", StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    tail = tail.Substring(idx + "ASSETS/".Length);
                                }
                                else
                                {
                                    var lgd = "lol-game-data/";
                                    var lgdIdx = tail.IndexOf(lgd, StringComparison.OrdinalIgnoreCase);
                                    if (lgdIdx >= 0)
                                        tail = tail.Substring(lgdIdx + lgd.Length);
                                }

                                // Remove any leading "assets/" segments
                                while (tail.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                                    tail = tail.Substring("assets/".Length);

                                tail = tail.TrimStart('/');

                                // For champion icons the path usually starts with v1/ or content/, map without extra assets/ prefix
                                string mapped;
                                if (tail.StartsWith("v1/", StringComparison.OrdinalIgnoreCase) ||
                                    tail.StartsWith("v2/", StringComparison.OrdinalIgnoreCase) ||
                                    tail.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                                    mapped = $"plugins/rcp-be-lol-game-data/global/default/{tail}".Replace('\\', '/')
                                        .ToLowerInvariant();
                                else
                                    mapped = $"plugins/rcp-be-lol-game-data/global/default/assets/{tail}"
                                        .Replace('\\', '/').ToLowerInvariant();

                                while (mapped.Contains("//")) mapped = mapped.Replace("//", "/");
                                iconUrl = $"raw.communitydragon.org/latest/{mapped}";
                            }
                            catch
                            {
                                iconUrl = null;
                            }

                        var parts = new List<string> { name };
                        parts.Add(iconUrl ?? string.Empty);
                        parts.Add(roles ?? string.Empty);
                        champList.Add(string.Join("|", parts));
                        DebugConsole.WriteLine(
                            $"[Accounts] Parsed champ entry: Name='{name}', IconUrl='{iconUrl}', Roles='{roles}' -> '{string.Join("|", parts)}'");
                    }
                    catch
                    {
                        // skip
                    }

            var wallet = walletTask.Result ?? new Utils.Wallet { be = 0, rp = 0 };
            var region = regionTask.Result ?? JObject.Parse("{\"region\":\"UNKNOWN\"}");

            // Update ActualAccountlists
            var note = ActualAccountlists?.FindLast(x =>
                x.username == SelectedUsername && x.password == SelectedPassword);
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
                AccountsDataGrid.ItemsSource = null;
                AccountsDataGrid.ItemsSource = ActualAccountlists;
                AccountsDataGrid.Items.SortDescriptions.Clear();
                AccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));
                AccountsDataGrid.Items.Refresh();
            });

            PullDataCompleted?.Invoke();
            ValorantAccounts.RunPullDataInBackground();
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Progressgrid.Visibility = Visibility.Hidden;
            });
            LogManager.GetCurrentClassLogger().Error(ex, "Error pulling account data");
            try
            {
                DebugConsole.WriteLine($"[Accounts] Error pulling account data: {ex.Message}", ConsoleColor.Red);
            }
            catch
            {
                // ignore debug console errors
            }
        }
    }

    private async Task<T?> RetryAsync<T>(Func<Task<T?>> action, int maxRetries = 5, int delayMs = 1500)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var result = await action();
                if (result != null)
                    return result;
            }
            catch
            {
            }

            await Task.Delay(delayMs);
        }

        return default;
    }

    private Task<JObject?> GetSummonerInfoAsync()
    {
        return RetryAsync<JObject>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JObject.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<JArray?> GetSkinInfoAsync()
    {
        return RetryAsync<JArray>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-catalog/v1/items/CHAMPION_SKIN", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JArray.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<JToken?> GetLootInfoAsync()
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JToken.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<Utils.Wallet?> GetWalletAsync()
    {
        return RetryAsync(async () =>
        {
            var resp = await Lcu.Connector("league", "get",
                "/lol-inventory/v1/wallet?currencyTypes=[%22RP%22,%22lol_blue_essence%22]", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                var json = JToken.Parse(body);
                return new Utils.Wallet
                {
                    be = json["lol_blue_essence"]?.ToObject<int>() ?? 0,
                    rp = json["RP"]?.ToObject<int>() ?? 0
                };
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<JToken?> GetRankedInfoAsync()
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-ranked/v1/current-ranked-stats", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                var parsed = JToken.Parse(body);
                DebugConsole.WriteLine("[Accounts] Ranked stats fetched");
                return parsed;
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<JToken?> GetRegionAsync()
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/riotclient/region-locale", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JToken.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    private Task<JArray?> GetChampionInfoAsync(string summonerId)
    {
        return RetryAsync<JArray>(async () =>
        {
            var resp = await Lcu.Connector("league", "get",
                $"/lol-champions/v1/inventories/{summonerId}/champions-minimal", "");
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            if (body.TrimStart().StartsWith("{"))
            {
                var token = JToken.Parse(body);
                if (!string.IsNullOrEmpty(token["errorCode"]?.ToString()))
                    return null;
            }

            try
            {
                return JArray.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        });
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

                var permanentBanFound = false;
                var relevantBanTypeFound = false;
                var banDetails = new List<string>();

                foreach (var penalty in penalties)
                {
                    var type = penalty["penaltyType"]?.ToString() ?? "";
                    var permanent = penalty["isPermanent"]?.ToObject<bool>() ?? false;

                    if (type == "PERMANENT_BAN" || permanent) permanentBanFound = true;
                    if (type.Equals("AC_SCRIPTING", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("RANKED_MANIPULATION", StringComparison.OrdinalIgnoreCase))
                        relevantBanTypeFound = true;

                    banDetails.Add($"{type} (Permanent: {permanent})");
                }

                if (permanentBanFound && relevantBanTypeFound)
                {
                    // Collect localized info
                    var localized = data["localizedTexts"];
                    string reason = localized?["body"]?.ToString() ??
                                    data["transgressionCategory"]?.ToString() ?? "Unknown ban reason";
                    string title = localized?["title"]?.ToString() ?? "Permanent Ban";
                    var penaltiesText = string.Join("; ", banDetails);

                    var note = $"{title}: {reason} | Penalties: {penaltiesText}";
                    return (true, note);
                }
            }
        }
        catch
        {
        }

        return (false, "");
    }


    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckLeague()) throw new Exception("League not installed");

            if (AccountsDataGrid.SelectedCells.Count == 0) throw new Exception("Account not selected");
            var selectedColumn = AccountsDataGrid.SelectedCells[0].Column;

            if (selectedColumn != null)
            {
                var header = selectedColumn.Header?.ToString();
                var selectedRow = AccountsDataGrid.SelectedItem as Utils.AccountList;
                if (selectedRow == null || header == null) throw new Exception("Account not selected");
                SelectedUsername = selectedRow.username;
                SelectedPassword = selectedRow.password;
            }

            DebugConsole.WriteLine($"[Accounts] Username selected: {SelectedUsername}");

            Utils.KillLeagueFunc();
            Process[] leagueProcess;
            Process riotProcess;
            var num = 0;
            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            var loginAttempts = 0;


            switch (clickedButton.Name)
            {
                case "Login":
                    riotProcess = Process.Start(Misc.Settings.settingsloaded.riotPath,
                        "--launch-product=league_of_legends --launch-patchline=live");
                    break;

                case "Stealthlogin":
                    riotProcess =
                        await offlineLauncher.LaunchRiotOrLeagueOfflineAsync(Misc.Settings.settingsloaded.riotPath);
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
                    var restartLogin = false;
                    var cancelLogin = false;
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
                        DebugConsole.WriteLine(siblings.Length.ToString());
                        var count = Array.IndexOf(siblings, checkbox) + 1;
                        if (siblings.Length <= count) throw new Exception("Not enough siblings found for the checkbox");
                        dynamic signInElement = null;
                        while (siblings.Length >= count)
                        {
                            signInElement = siblings[count++].AsButton();

                            DebugConsole.WriteLine($"Found checkbox: {checkbox.Name}");
                            DebugConsole.WriteLine($"Found siblings count: {siblings.Length}");

                            if (signInElement.ControlType != ControlType.Button) continue;
                            break;
                        }

                        usernameField.Text = SelectedUsername;
                        passwordField.Text = SelectedPassword;
                        if (signInElement != null)
                        {
                            while (!signInElement.IsEnabled) Thread.Sleep(200);
                            signInElement.Invoke();

                            // brief pause to allow any login error tooltip to appear
                            await Task.Delay(500);

                            while (true)
                            {
                                try
                                {
                                    // look for a Tooltip with name "Login error" in the same window
                                    var loginError = window.FindFirstDescendant(cf =>
                                        cf.ByControlType(ControlType.ToolTip).And(cf.ByName("Login error")));
                                    if (loginError != null)
                                    {
                                        loginAttempts++;

                                        var errorText = string.Empty;
                                        try
                                        {
                                            errorText = loginError
                                                .FindFirstDescendant(cf => cf.ByControlType(ControlType.Text)
                                                    .And(cf.ByName(
                                                        "Your login credentials don't match an account in our system.")))
                                                ?.Name;
                                        }
                                        catch
                                        {
                                        }

                                        if (string.IsNullOrWhiteSpace(errorText))
                                        {
                                            try
                                            {
                                                errorText = loginError.Name;
                                            }
                                            catch
                                            {
                                            }

                                            if (string.IsNullOrWhiteSpace(errorText))
                                                try
                                                {
                                                    errorText = loginError.Properties.Name.Value;
                                                }
                                                catch
                                                {
                                                }
                                        }

                                        var invalidCreds = !string.IsNullOrWhiteSpace(errorText) &&
                                                           errorText.Contains(
                                                               "Your login credentials don't match an account in our system.",
                                                               StringComparison.OrdinalIgnoreCase);

                                        if (invalidCreds)
                                        {
                                            // Mark account as invalid login
                                            var existingNote = ActualAccountlists?.FindLast(x =>
                                                x.username == SelectedUsername && x.password == SelectedPassword)?.note;
                                            ActualAccountlists?.RemoveAll(x =>
                                                x.username == SelectedUsername && x.password == SelectedPassword);
                                            ActualAccountlists?.Add(new Utils.AccountList
                                            {
                                                username = SelectedUsername,
                                                password = SelectedPassword,
                                                riotID = "Invalid Login",
                                                level = 0,
                                                server = "INVALID",
                                                be = 0,
                                                rp = 0,
                                                rank = "Invalid Login",
                                                champions = "",
                                                Champions = 0,
                                                skins = "",
                                                Skins = 0,
                                                Loot = "",
                                                Loots = 0,
                                                rank2 = "Invalid Login",
                                                note = existingNote
                                            });

                                            // persist immediately
                                            await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(),
                                                ActualAccountlists, config);

                                            // update UI and stop login flow
                                            Dispatcher.Invoke(() =>
                                            {
                                                AccountsDataGrid.ItemsSource = null;
                                                AccountsDataGrid.ItemsSource = ActualAccountlists;
                                                AccountsDataGrid.Items.Refresh();
                                            });

                                            return; // pause/stop login processing
                                        }

                                        if (loginAttempts >= 3)
                                        {
                                            cancelLogin = true;
                                            break;
                                        }

                                        restartLogin = true;
                                        break;
                                    }
                                }
                                catch
                                {
                                }

                                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                                string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                DebugConsole.WriteLine($"[Accounts] EULA status: {status}");
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

                            if (cancelLogin) return;

                            if (restartLogin)
                            {
                                await Task.Delay(500);
                                continue;
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
                    _logger.Warn(ex, "Transient error during login automation");
                    DebugConsole.WriteLine($"[Accounts] Login automation retry: {ex.Message}", ConsoleColor.Yellow);
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

        var total = ActualAccountlists.Count;
        var processed = 0;
        var anyChanges = false;

        ProgressWindow progressWindow = null!;
        DispatcherTimer? followTimer = null;

        // Show progress window
        Dispatcher.Invoke(() =>
        {
            progressWindow = new ProgressWindow(total)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            progressWindow.Show();
            progressWindow.FollowOwner();

            followTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            followTimer.Tick += (_, _) => progressWindow.FollowOwner();
            followTimer.Start();
        });

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
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

                    var html = await http.GetStringAsync(url);
                    if (string.IsNullOrWhiteSpace(html)) continue;

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var rankingBox = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'summoner-rankings')]");
                    if (rankingBox == null) continue;

                    string? soloRank = null;
                    string? flexRank = null;

                    // Parse Soloqueue
                    var soloNode =
                        rankingBox.SelectSingleNode(
                            ".//span[contains(@class,'queue') and contains(text(),'Soloqueue')]");
                    if (soloNode != null)
                    {
                        var tier = rankingBox.SelectSingleNode(".//div[contains(@class,'leagueTier')]")?.InnerText
                            .Trim();
                        var lp = rankingBox.SelectSingleNode(".//span[contains(@class,'leaguePoints')]")?.InnerText
                            .Trim();
                        var wins = rankingBox.SelectSingleNode(".//span[contains(@class,'winsNumber')]")?.InnerText
                            .Trim();
                        var losses = rankingBox.SelectSingleNode(".//span[contains(@class,'lossesNumber')]")?.InnerText
                            .Trim();

                        if (!string.IsNullOrWhiteSpace(tier))
                            soloRank = $"{tier} {lp} LP, {wins}W / {losses}L";
                    }

                    Dispatcher.Invoke(() => progressWindow.FollowOwner());
                    // Parse Flex queue
                    var flexNode =
                        rankingBox.SelectSingleNode(".//div[contains(@class,'queueName') and contains(text(),'Flex')]");
                    if (flexNode != null)
                    {
                        var container =
                            flexNode.SelectSingleNode("./ancestor::div[contains(@class,'img-align-block')]");
                        var tier = container?.SelectSingleNode(".//div[contains(@class,'leagueTier')]")?.InnerText
                            .Trim();
                        var lp = container?.SelectSingleNode(".//span[contains(@class,'leaguePoints')]")?.InnerText
                            .Trim();
                        var wins = container?.SelectSingleNode(".//span[contains(@class,'winsNumber')]")?.InnerText
                            .Trim();
                        var losses = container?.SelectSingleNode(".//span[contains(@class,'lossesNumber')]")?.InnerText
                            .Trim();

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
                await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

                Dispatcher.Invoke(() => AccountsDataGrid.Items.Refresh());
            }
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                followTimer?.Stop();
                progressWindow?.Close();
            });
        }
    }


    private async void Accounts_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        await DeleteSelectedAccountAsync();
    }

    private async Task DeleteSelectedAccountAsync()
    {
        try
        {
            var selectedrow = AccountsDataGrid.SelectedItem as Utils.AccountList;
            if (selectedrow == null) return;

            var confirm = MessageBox.Show("Delete the selected account?", "Confirm delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            ActualAccountlists?.RemoveAll(r =>
                r.username == selectedrow.username &&
                r.password == selectedrow.password &&
                r.server == selectedrow.server);

            ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");

            await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

            AccountsDataGrid.ItemsSource = null;
            AccountsDataGrid.ItemsSource = ActualAccountlists;

            AccountsDataGrid.Items.SortDescriptions.Clear();
            AccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("level", ListSortDirection.Descending));

            AccountsDataGrid.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error deleting account");
        }
    }


    public async Task<bool> CheckLeague()
    {
        if (File.Exists(Misc.Settings.settingsloaded.riotPath))
            return true;
        return false;
    }


    private void OnKillClientClick(object sender, RoutedEventArgs e)
    {
        Utils.KillLeagueFunc();
    }

    private async void OnOpenLeagueClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Utils.KillLeagueFunc();
            if (!await CheckLeague()) throw new Exception("League not installed");
            OpenLeague();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error Opening league");
        }
    }

    private void OpenLeague()
    {
        Process.Start(Misc.Settings.settingsloaded.riotPath,
            "--launch-product=league_of_legends --launch-patchline=live");
    }

    private async void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(FilterTextBox.Text))
            {
                var search = FilterTextBox.Text;

                var filteredList = ActualAccountlists?
                    .Where(word =>
                        (word.champions ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.skins ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.Loot ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.server ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.riotID ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    .ToList();

                AccountsDataGrid.ItemsSource = filteredList;
            }
            else
            {
                AccountsDataGrid.ItemsSource = ActualAccountlists;
            }

            AccountsDataGrid.UpdateLayout();
            AccountsDataGrid.Items.Refresh();
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

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, config))
            {
                // If file is empty, return empty list
                if (!csv.Read())
                    return records;

                // Read header if exists
                csv.ReadHeader();
                var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (csv.HeaderRecord != null)
                    for (var i = 0; i < csv.HeaderRecord.Length; i++)
                        if (!string.IsNullOrWhiteSpace(csv.HeaderRecord[i]) &&
                            !headerMap.ContainsKey(csv.HeaderRecord[i]))
                            headerMap[csv.HeaderRecord[i]] = i;

                string? GetField(string headerName, int fallbackIndex)
                {
                    if (headerMap.TryGetValue(headerName, out var index))
                        return csv.TryGetField(index, out string? value) ? value : null;

                    if (fallbackIndex < 0)
                        return null;

                    return csv.TryGetField(fallbackIndex, out string? fallbackValue) ? fallbackValue : null;
                }

                string? GetFieldAny(int fallbackIndex, params string[] headerNames)
                {
                    foreach (var headerName in headerNames)
                        if (headerMap.TryGetValue(headerName, out var index))
                            return csv.TryGetField(index, out string? value) ? value : null;

                    if (fallbackIndex < 0)
                        return null;

                    return csv.TryGetField(fallbackIndex, out string? fallbackValue) ? fallbackValue : null;
                }

                while (true)
                    try
                    {
                        if (!csv.Read())
                            break;

                        var record = new Utils.AccountList
                        {
                            username = GetField("username", 0) ?? "",
                            password = GetField("password", 1) ?? "",
                            riotID = GetField("riotID", 2) ?? "",
                            level = TryParseInt(GetField("level", 3)),
                            server = GetField("server", 4) ?? "",
                            be = TryParseInt(GetField("be", 5)),
                            rp = TryParseInt(GetField("rp", 6)),
                            rank = GetField("rank", 7) ?? "",
                            champions = GetField("champions", 8) ?? "",
                            skins = GetField("skins", 9) ?? "",
                            Champions = TryParseInt(GetField("Champions", 10)),
                            Skins = TryParseInt(GetField("Skins", 11)),
                            Loot = GetField("Loot", 12) ?? "",
                            Loots = TryParseInt(GetField("Loots", 13)),
                            rank2 = GetField("rank2", 14) ?? "",
                            note = GetField("note", 15) ?? "",
                            valorantAgents = GetField("valorantAgents", 16) ?? "",
                            valorantContracts = GetField("valorantContracts", 17) ?? "",
                            valorantSprays = GetField("valorantSprays", 18) ?? "",
                            valorantGunBuddies = GetField("valorantGunBuddies", 19) ?? "",
                            valorantCards = GetField("valorantCards", 20) ?? "",
                            valorantSkins = GetField("valorantSkins", 21) ?? "",
                            valorantSkinVariants = GetField("valorantSkinVariants", 22) ?? "",
                            valorantTitles = GetField("valorantTitles", 23) ?? "",
                            valorantVp = TryParseInt(GetField("valorantVp", 24)),
                            valorantRp = TryParseInt(GetFieldAny(25, "valorantRp", "valorantRpKc")),
                            valorantKc = TryParseInt(GetFieldAny(-1, "valorantKc")),
                            valorantLevel = TryParseInt(GetField("valorantLevel", 27)),
                            valorantRank = GetField("valorantRank", 28) ?? "",
                            valorantServer = GetField("valorantServer", -1) ?? "",
                            valorantXp = TryParseInt(GetField("valorantXp", -1))
                        };

                        records.Add(record);
                    }
                    catch
                    {
                        // skip broken row
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


    private async void OnAccountsMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
                        var selectedrow = AccountsDataGrid.SelectedItem as Utils.AccountList;
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

    private void OnNameChangeClick(object sender, RoutedEventArgs e)
    {
        var namechanger = new ChangeName();
        namechanger.Show();
    }

    private void OnSecondaryClientClick(object sender, RoutedEventArgs e)
    {
        Process.Start(Misc.Settings.settingsloaded.riotPath,
            "--launch-product=league_of_legends --launch-patchline=live --allow-multiple-clients");
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private void Accounts_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.C)
        {
            var dataGrid = AccountsDataGrid;
            if (dataGrid == null) return;
            if (dataGrid != null && dataGrid.CurrentCell != null)
            {
                var selectedColumn = dataGrid.CurrentCell.Column;

                if (selectedColumn != null)
                {
                    var header = selectedColumn.Header?.ToString();
                    var selectedRow = AccountsDataGrid.SelectedItem as Utils.AccountList;
                    if (selectedRow == null || header == null) return;

                    Clipboard.SetText(selectedRow.username + ":" + selectedRow.password +
                                      " Server: " + selectedRow.server +
                                      " RiotID: " + selectedRow.riotID +
                                      " Champions: " + selectedRow.Champions +
                                      " Skins: " + selectedRow.Skins +
                                      " BE: " + selectedRow.be +
                                      " RP: " + selectedRow.rp);

                    e.Handled = true;
                    Notif.notificationManager.Show("Info",
                        "Account " + selectedRow.riotID + " has been copied to clipboard",
                        NotificationType.Notification,
                        "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null,
                        () => Notif.donothing(), "OK",
                        NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                }
            }
        }
    }

    private void OnRemoveDuplicatesClick(object sender, RoutedEventArgs e)
    {
        if (ActualAccountlists == null) return;

        ActualAccountlists = ActualAccountlists
            .GroupBy(x => (x.username ?? "").Trim().ToLower() + "|" + (x.password ?? "").Trim())
            .Select(g => g.First())
            .ToList();
        AccountFileStore.Save(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);
    }

    private async void GenerateLoginToken_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckLeague()) throw new Exception("League not installed");

            if (AccountsDataGrid.SelectedCells.Count == 0) throw new Exception("Account not selected");
            var selectedColumn = AccountsDataGrid.SelectedCells[0].Column;

            if (selectedColumn != null)
            {
                var header = selectedColumn.Header?.ToString();
                var selectedRow = AccountsDataGrid.SelectedItem as Utils.AccountList;
                if (selectedRow == null || header == null) throw new Exception("Account not selected");
                SelectedUsername = selectedRow.username;
                SelectedPassword = selectedRow.password;
            }

            DebugConsole.WriteLine($"[Accounts] Username selected: {SelectedUsername}");
            var persist = await ProxyLoginTokenManager.PromptPersistLoginAsync();
            ProxyLoginTokenManager.ResetCaptureSignal();

            Utils.KillLeagueFunc();
            Process[] leagueProcess;
            Process riotProcess;
            var num = 0;
            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            var loginAttempts = 0;

            await _launcher.LaunchRiotClientWithTokenCapture(Misc.Settings.settingsloaded.riotPath, persist);

            var captureTask = ProxyLoginTokenManager.WaitForCaptureAsync();
            var tokenDetectedTask = ProxyLoginTokenManager.WaitForTokenDetectedAsync();

            var automationTask = Task.Run(async () =>
            {
                var riotval = string.Empty;
                var attempts = 0;

                while (string.IsNullOrEmpty(riotval))
                {
                    if (Process.GetProcessesByName("Riot Client").Length != 0)
                        riotval = "Riot Client";
                    else if (Process.GetProcessesByName("RiotClientUx").Length != 0)
                        riotval = "RiotClientUx";

                    if (!string.IsNullOrEmpty(riotval) || attempts++ >= 20)
                        break;

                    await Task.Delay(200);
                }

                if (string.IsNullOrEmpty(riotval))
                    return;

                while (!tokenDetectedTask.IsCompleted)
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
                            var passwordField = riotcontent.FindFirstDescendant(cf => cf.ByAutomationId("password"))
                                .AsTextBox();
                            var checkbox =
                                riotcontent.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox));

                            if (usernameField == null || passwordField == null || checkbox == null)
                            {
                                await Task.Delay(200);
                                continue;
                            }

                            var siblings = riotcontent.FindAllChildren();
                            var count = Array.IndexOf(siblings, checkbox) + 1;
                            dynamic signInElement = null;
                            while (siblings.Length >= count)
                            {
                                signInElement = siblings[count++].AsButton();
                                if (signInElement != null && signInElement.ControlType == ControlType.Button)
                                    break;
                            }

                            usernameField.Text = SelectedUsername;
                            passwordField.Text = SelectedPassword;

                            if (signInElement != null)
                            {
                                while (!signInElement.IsEnabled && !tokenDetectedTask.IsCompleted)
                                    await Task.Delay(200);

                                if (!tokenDetectedTask.IsCompleted)
                                    signInElement.Invoke();
                            }

                            await Task.Delay(500);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Transient error during login automation");
                        DebugConsole.WriteLine($"[Accounts] Login automation retry: {ex.Message}", ConsoleColor.Yellow);
                        await Task.Delay(200);
                    }
            });


            try
            {
                await captureTask;
                DebugConsole.WriteLine("[Accounts] Token capture completed.");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[Accounts] Token capture failed or canceled: {ex.Message}");
            }

            await automationTask;
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error generating login token");
            Notif.notificationManager.Show("Error", "An error occurred while generating the login token",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
    }

    private async void UseLoginToken_OnClick(object sender, RoutedEventArgs e)
    {
        _ = ProxyLoginTokenManager.UseLoginTokenAsync();
    }

    private static JObject? DecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
            return null;

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2:
                payload += "==";
                break;
            case 3:
                payload += "=";
                break;
        }

        var bytes = Convert.FromBase64String(payload);
        var json = Encoding.UTF8.GetString(bytes);
        return JObject.Parse(json);
    }

    private static void TrySelectAccountFromIdToken(string idToken)
    {
        try
        {
            var payloadJson = DecodeJwtPayload(idToken);
            var uname = payloadJson?["lol"]?.FirstOrDefault()?["uname"]?.ToString();
            if (string.IsNullOrWhiteSpace(uname))
                return;

            if (!string.IsNullOrWhiteSpace(uname) &&
                string.Equals(SelectedUsername, uname, StringComparison.OrdinalIgnoreCase))
                return;

            var accounts = ActualAccountlists;
            if (accounts == null)
                return;

            var match = accounts.FirstOrDefault(a =>
                string.Equals(a.username, uname, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return;

            SelectedUsername = match.username;
            SelectedPassword = match.password;
            DebugConsole.WriteLine($"[Accounts] Switched selected account to {match.username} from id token.");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[Accounts] Failed to decode id token: {ex.Message}", ConsoleColor.Red);
        }
    }
}
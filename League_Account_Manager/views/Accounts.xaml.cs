using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
using HtmlNode = HtmlAgilityPack.HtmlNode;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;


namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Accounts.xaml
/// </summary>
public partial class Accounts : Page
{
    private static readonly Regex LastPlayedScriptRegex = new(
        @"newTooltipData\s*=\s*\{""[^""\r\n]+"":\s*\(new\s+Date\((?<timestamp>\d+)\)\.toLocaleDateString\(\)\s*\+\s*""\s*""\s*\+\s*new\s+Date\((?<timestamp2>\d+)\)\.toLocaleTimeString\(\)\)\s*\+\s*""\s*-\s*(?<duration>[^""\r\n]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private readonly Dictionary<string, ListSortDirection?> _columnSortState = new();
    private readonly object _fileChangeLock = new();
    private readonly SemaphoreSlim _accountDataGate = new(1, 1);
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private bool _initialized;
    private DateTime _lastFileChange = DateTime.MinValue;
    private DateTime _lastKnownFileWrite = DateTime.MinValue;
    private bool _pendingReload;
    private bool Executing;
    private CancellationTokenSource? _accountOperationCancellation;
    private bool _accountOperationRunning;
    private CancellationTokenSource? _rankUpdateCancellation;
    private FileSystemWatcher? fileWatcher;
    private ScrollViewer? _accountsScrollViewer;
    private readonly Stopwatch _accountsScrollAnimationClock = new();
    private double _accountsScrollAnimationStart;
    private double _accountsScrollAnimationTarget;
    private bool _accountsScrollAnimationRunning;

    public Accounts()
    {
        InitializeComponent();
        Loaded += Accounts_Loaded;
        Unloaded += Accounts_Unloaded;
        IsVisibleChanged += Accounts_IsVisibleChanged;
        Misc.Settings.AccountPasswordSupplied += OnAccountPasswordSupplied;
        AccountFileStore.AccountsFileUpdated += OnAccountsFileUpdated;
    }

    public static List<Utils.AccountList> ActualAccountlists { get; set; } = new();
    public static event Action? PullDataCompleted;

    private void Accounts_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAccountsGridHeight();
    }

    private void Accounts_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAccountsGridHeight();
    }

    private void UpdateAccountsGridHeight()
    {
        var window = System.Windows.Window.GetWindow(this);
        if (window == null || window.ActualHeight <= 0) return;

        AccountsDataGrid.Height = Math.Max(120, window.ActualHeight - 325);
    }

    private void AccountsDataGrid_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
        if (scrollViewer == null) return;

        _accountsScrollViewer = scrollViewer;
        if (!_accountsScrollAnimationRunning)
        {
            _accountsScrollAnimationStart = scrollViewer.VerticalOffset;
            _accountsScrollAnimationTarget = scrollViewer.VerticalOffset;
            _accountsScrollAnimationClock.Restart();
            _accountsScrollAnimationRunning = true;
            CompositionTarget.Rendering += AccountsScrollAnimation_OnRendering;
        }

        _accountsScrollAnimationTarget = Math.Clamp(
            _accountsScrollAnimationTarget - e.Delta / 6.0,
            0,
            scrollViewer.ScrollableHeight);
        e.Handled = true;
    }

    private void AccountsScrollAnimation_OnRendering(object? sender, EventArgs e)
    {
        if (_accountsScrollViewer == null)
        {
            StopAccountsScrollAnimation();
            return;
        }

        var progress = Math.Min(1, _accountsScrollAnimationClock.Elapsed.TotalSeconds / 0.16);
        var easedProgress = 1 - Math.Pow(1 - progress, 3);
        var offset = _accountsScrollAnimationStart +
                     (_accountsScrollAnimationTarget - _accountsScrollAnimationStart) * easedProgress;
        _accountsScrollViewer.ScrollToVerticalOffset(offset);

        if (progress >= 1) StopAccountsScrollAnimation();
    }

    private void StopAccountsScrollAnimation()
    {
        CompositionTarget.Rendering -= AccountsScrollAnimation_OnRendering;
        _accountsScrollAnimationClock.Stop();
        _accountsScrollAnimationRunning = false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is T matchingChild) return matchingChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }

        return null;
    }

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
        _accountOperationCancellation?.Cancel();
        StopAccountsScrollAnimation();
    }

    private async void OnAccountsFileUpdated(object? sender, EventArgs e)
    {
        _pendingReload = true;
        try
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (!IsLoaded) return;
                _pendingReload = false;
                await LoadDataAsync();
            }).Task.Unwrap();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error reloading accounts after file update");
        }
    }

    private async void Accounts_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        Misc.Settings.AccountPasswordSupplied -= OnAccountPasswordSupplied;
        Misc.Settings.AccountPasswordSupplied += OnAccountPasswordSupplied;
        AccountFileStore.AccountsFileUpdated -= OnAccountsFileUpdated;
        AccountFileStore.AccountsFileUpdated += OnAccountsFileUpdated;

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
            var filePath = AccountFileStore.GetAccountsFilePath();

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
            await Dispatcher.InvokeAsync(LoadDataAsync).Task.Unwrap();
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
            var sortMemberPath = GetSortMemberPath(e.Column);
            var newDirection = e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            if (sortMemberPath == "rank" || sortMemberPath == "rank2")
            {
                e.Handled = true;
                var list = AccountsDataGrid.ItemsSource as IEnumerable<Utils.AccountList> ??
                           ActualAccountlists ?? new List<Utils.AccountList>();
                AccountsDataGrid.ItemsSource = SortLeagueRankList(list, sortMemberPath, newDirection).ToList();
                SetLeagueSortDirectionIndicators(sortMemberPath, newDirection);
                AccountsDataGrid.Items.Refresh();
            }
            else if (sortMemberPath == "lastPlayed")
            {
                e.Handled = true;
                var list = AccountsDataGrid.ItemsSource as IEnumerable<Utils.AccountList> ??
                           ActualAccountlists ?? new List<Utils.AccountList>();
                AccountsDataGrid.ItemsSource = SortLeagueLastPlayedList(list, newDirection).ToList();
                SetLeagueSortDirectionIndicators(sortMemberPath, newDirection);
                AccountsDataGrid.Items.Refresh();
            }

            SaveLeagueSortPreference(sortMemberPath, newDirection);
        }
        catch
        {
        }
    }

    private string GetSortMemberPath(DataGridColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            return column.SortMemberPath;

        return column.Header?.ToString() switch
        {
            "SoloQ" => "rank",
            "FlexQ" => "rank2",
            _ => "level"
        };
    }

    private IEnumerable<Utils.AccountList> SortLeagueRankList(IEnumerable<Utils.AccountList> list, string sortMemberPath,
        ListSortDirection direction)
    {
        Func<Utils.AccountList, string?> getRank = x =>
            string.Equals(sortMemberPath, "rank2", StringComparison.OrdinalIgnoreCase) ? x.rank2 : x.rank;

        if (direction == ListSortDirection.Descending)
            return list.OrderBy(x => string.IsNullOrWhiteSpace(getRank(x)) ? 1 : 0)
                .ThenByDescending(x => ParseRankValue(getRank(x)));

        return list.OrderBy(x => string.IsNullOrWhiteSpace(getRank(x)) ? 1 : 0)
            .ThenBy(x => ParseRankValue(getRank(x)));
    }

    private IEnumerable<Utils.AccountList> SortLeagueLastPlayedList(IEnumerable<Utils.AccountList> list,
        ListSortDirection direction)
    {
        static DateTime? ParseLastPlayed(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
                return parsed;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                return parsed;

            return null;
        }

        if (direction == ListSortDirection.Descending)
            return list.OrderBy(x => ParseLastPlayed(x.lastPlayed).HasValue ? 0 : 1)
                .ThenByDescending(x => ParseLastPlayed(x.lastPlayed));

        return list.OrderBy(x => ParseLastPlayed(x.lastPlayed).HasValue ? 0 : 1)
            .ThenBy(x => ParseLastPlayed(x.lastPlayed));
    }

    private (string SortMemberPath, ListSortDirection Direction) GetLeagueSortPreference()
    {
        var sortMemberPath = Misc.Settings.settingsloaded.LeagueDefaultSortColumn;
        if (string.IsNullOrWhiteSpace(sortMemberPath))
            sortMemberPath = "level";

        var direction = Misc.Settings.settingsloaded.LeagueDefaultSortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        return (sortMemberPath, direction);
    }

    private void SaveLeagueSortPreference(string sortMemberPath, ListSortDirection direction)
    {
        Misc.Settings.settingsloaded.LeagueDefaultSortColumn = sortMemberPath;
        Misc.Settings.settingsloaded.LeagueDefaultSortDescending = direction == ListSortDirection.Descending;
        Misc.Settings.Save();
    }

    private void ApplyLeagueSortToGrid()
    {
        var (sortMemberPath, direction) = GetLeagueSortPreference();

        if (sortMemberPath == "rank" || sortMemberPath == "rank2")
        {
            var list = AccountsDataGrid.ItemsSource as IEnumerable<Utils.AccountList> ??
                       ActualAccountlists ?? new List<Utils.AccountList>();
            AccountsDataGrid.ItemsSource = SortLeagueRankList(list, sortMemberPath, direction).ToList();
            AccountsDataGrid.Items.SortDescriptions.Clear();
            SetLeagueSortDirectionIndicators(sortMemberPath, direction);
            return;
        }

        if (sortMemberPath == "lastPlayed")
        {
            var list = AccountsDataGrid.ItemsSource as IEnumerable<Utils.AccountList> ??
                       ActualAccountlists ?? new List<Utils.AccountList>();
            AccountsDataGrid.ItemsSource = SortLeagueLastPlayedList(list, direction).ToList();
            AccountsDataGrid.Items.SortDescriptions.Clear();
            SetLeagueSortDirectionIndicators(sortMemberPath, direction);
            return;
        }

        AccountsDataGrid.Items.SortDescriptions.Clear();
        AccountsDataGrid.Items.SortDescriptions.Add(new SortDescription(sortMemberPath, direction));
        SetLeagueSortDirectionIndicators(sortMemberPath, direction);
    }

    private void SetLeagueSortDirectionIndicators(string sortMemberPath, ListSortDirection direction)
    {
        foreach (var col in AccountsDataGrid.Columns)
            col.SortDirection = string.Equals(GetSortMemberPath(col), sortMemberPath, StringComparison.OrdinalIgnoreCase)
                ? direction
                : null;
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

        var dispatcher = Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        await dispatcher.InvokeAsync(() =>
        {
            ApplyLeagueSortToGrid();
        }, DispatcherPriority.Background, CancellationToken.None);
    }


    public async Task LoadDataAsync()
    {
        await _accountDataGate.WaitAsync();
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

                ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
                Utils.RemoveDoubleQuotesFromList(ActualAccountlists);
            });

            Dispatcher.Invoke(() =>
            {
                AccountsDataGrid.ItemsSource = null;
                AccountsDataGrid.ItemsSource = ActualAccountlists;
                ApplyLeagueSortToGrid();

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
        finally
        {
            _accountDataGate.Release();
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
                var item = new ListBoxItem
                {
                    Tag = t,
                    Content = $"◻ {t}",
                    Foreground = (Brush)System.Windows.Application.Current.FindResource("TextPrimaryBrush")
                };
                box.Items.Add(item);
            }

            try
            {
                const double itemHeight = 24.0;
                const double itemVerticalMargin = 2.0;
                var desired = box.Items.Count * (itemHeight + itemVerticalMargin) +
                              box.Padding.Top + box.Padding.Bottom;
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

    private void OnPullDataClick(object sender, RoutedEventArgs e)
    {
        StartAccountOperation("Pulling account data", GetPullTasks(), PullDataAsync);
    }

    private static string[] GetPullTasks() => new[]
    {
        "Waiting for summoner readiness",
        "Fetch summoner info",
        "Fetch skins",
        "Fetch ranked info",
        "Fetch loot",
        "Fetch wallet",
        "Fetch region",
        "Fetch champions"
    };

    private TextBlock? GetAccountOperationStatusControl() => FindName("AccountOperationStatus") as TextBlock;
    private TextBlock? GetAccountOperationTitleControl() => FindName("AccountOperationTitle") as TextBlock;
    private Button? GetAccountOperationButton() => FindName("CancelAccountOperationButton") as Button;

    private void ShowRankProgress(int total, CancellationTokenSource cancellation)
    {
        _rankUpdateCancellation = cancellation;
        RankProgressBar.Maximum = total;
        RankProgressBar.Value = 0;
        RankProgressPercent.Text = "0%";
        RankProgressStatus.Text = $"Preparing 0 of {total} accounts...";
        RankProgressCancel.IsEnabled = true;
        RankProgressPanel.Visibility = Visibility.Visible;
    }

    private void UpdateRankProgress(int current, int total, string accountLabel)
    {
        RankProgressBar.Value = current;
        RankProgressPercent.Text = $"{current / (double)total:P0}";
        RankProgressStatus.Text = $"{accountLabel}  |  {current} of {total} completed";
    }

    private void HideRankProgress()
    {
        _rankUpdateCancellation?.Dispose();
        _rankUpdateCancellation = null;
        RankProgressPanel.Visibility = Visibility.Collapsed;
    }

    private CancellationTokenSource? BeginAccountOperation(string title, IEnumerable<string> tasks)
    {
        if (_accountOperationRunning)
            return null;

        var cancellation = new CancellationTokenSource();
        _accountOperationCancellation = cancellation;
        _accountOperationRunning = true;
        var titleControl = GetAccountOperationTitleControl();
        var statusControl = GetAccountOperationStatusControl();
        var operationButton = GetAccountOperationButton();
        Dispatcher.Invoke(() =>
        {
            if (titleControl != null) titleControl.Text = title;
            if (statusControl != null) statusControl.Text = "Preparing...";
            if (operationButton != null)
            {
                operationButton.Content = "Cancel";
                operationButton.IsEnabled = true;
            }

            Progressgrid.Visibility = Visibility.Visible;
            InitializeProgressTasks(tasks);
        });

        return cancellation;
    }

    private void StartAccountOperation(string title, IEnumerable<string> tasks,
        Func<CancellationToken, Task<bool>> operation)
    {
        var cancellation = BeginAccountOperation(title, tasks);
        if (cancellation == null)
            return;

        _ = RunAccountOperationAsync(operation, cancellation);
    }

    private async Task RunAccountOperationAsync(Func<CancellationToken, Task<bool>> operation,
        CancellationTokenSource cancellation)
    {
        var closeOnFinish = false;
        try
        {
            var completed = await operation(cancellation.Token);
            if (completed && !cancellation.IsCancellationRequested)
            {
                SetAccountOperationStatus("Finished successfully.");
                closeOnFinish = true;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetAccountOperationStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Account operation failed");
            SetAccountOperationStatus("The operation could not be completed.");
        }
        finally
        {
            FinishAccountOperation(cancellation, closeOnFinish);
        }
    }

    private void FinishAccountOperation(CancellationTokenSource cancellation, bool closeOnFinish)
    {
        _accountOperationRunning = false;
        if (ReferenceEquals(_accountOperationCancellation, cancellation))
            _accountOperationCancellation = null;
        cancellation.Dispose();
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.Invoke(() =>
        {
            if (closeOnFinish)
            {
                Progressgrid.Visibility = Visibility.Collapsed;
                return;
            }

            var operationButton = GetAccountOperationButton();
            if (operationButton != null)
            {
                operationButton.Content = "Close";
                operationButton.IsEnabled = true;
            }
        });
    }

    private void SetAccountOperationStatus(string status)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        void UpdateStatus()
        {
            var statusControl = GetAccountOperationStatusControl();
            if (statusControl != null) statusControl.Text = status;
        }

        if (Dispatcher.CheckAccess())
            UpdateStatus();
        else
            Dispatcher.Invoke(UpdateStatus);
    }

    private void CancelAccountOperation_Click(object sender, RoutedEventArgs e)
    {
        if (_rankUpdateCancellation != null)
        {
            RankProgressStatus.Text = "Cancelling rank update...";
            RankProgressCancel.IsEnabled = false;
            _rankUpdateCancellation.Cancel();
            return;
        }

        if (!_accountOperationRunning)
        {
            Progressgrid.Visibility = Visibility.Collapsed;
            return;
        }

        SetAccountOperationStatus("Cancelling...");
        var operationButton = GetAccountOperationButton();
        if (operationButton != null) operationButton.IsEnabled = false;
        _accountOperationCancellation?.Cancel();
    }

    private async Task<bool> PullDataAsync(CancellationToken cancellationToken)
    {
        await _accountDataGate.WaitAsync(cancellationToken);
        try
        {
            return await PullDataCoreAsync(cancellationToken);
        }
        finally
        {
            _accountDataGate.Release();
        }
    }

    private async Task<bool> PullDataCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetAccountOperationStatus("Checking the League client...");

            var leagueclientprocess = Process.GetProcessesByName("LeagueClientUx");
            if (leagueclientprocess.Length == 0)
            {
                SetAccountOperationStatus("League of Legends is not running.");
                Notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return false;
            }

            // Try to select an account from any id token provided by the Riot client
            SetAccountOperationStatus("Reading the active Riot account...");
            try
            {
                var authResp =
                    await Lcu.Connector("riot", "get", "/riot-client-auth/v1/authorization", "", cancellationToken)
                        as HttpResponseMessage;
                if (authResp != null)
                {
                    var authBody = await authResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // ignore errors contacting riot client for id token
            }

            if (SelectedUsername == null || SelectedPassword == null)
            {
                SetAccountOperationStatus("Select an account before continuing.");
                new MissingInfo().ShowDialog();
                return false;
            }


            SetAccountOperationStatus("Checking account status...");
            var (isBanned, banNote) = await CheckPermanentBanAsync(cancellationToken);
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
                ActualAccountlists.RemoveAll(x => x.username == SelectedUsername && x.password == SelectedPassword);
                ActualAccountlists.Add(new Utils.AccountList
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

                cancellationToken.ThrowIfCancellationRequested();
                await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AccountsDataGrid.ItemsSource = null;
                    AccountsDataGrid.ItemsSource = ActualAccountlists;
                    AccountsDataGrid.Items.Refresh();
                });

                return true;
            }

            SetAccountOperationStatus("Waiting for summoner readiness...");
            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/summoner-requests-ready", "",
                        cancellationToken);
                    var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (content.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        DebugConsole.WriteLine("[Accounts] Summoner requests are ready");
                        MarkTaskCompleted("Waiting for summoner readiness");
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // LCU not ready yet
                }

                await Task.Delay(1000, cancellationToken);
            }

            // Fetch all API data in parallel and mark tasks as they complete
            var summonerTask = Task.Run(async () =>
            {
                var res = await GetSummonerInfoAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch summoner info");
                return res;
            }, cancellationToken);

            var skinTask = Task.Run(async () =>
            {
                var res = await GetSkinInfoAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch skins");
                return res;
            }, cancellationToken);

            var rankedTask = Task.Run(async () =>
            {
                var res = await GetRankedInfoAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch ranked info");
                return res;
            }, cancellationToken);

            var lootTask = Task.Run(async () =>
            {
                var res = await GetLootInfoAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch loot");
                return res;
            }, cancellationToken);

            var walletTask = Task.Run(async () =>
            {
                var res = await GetWalletAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch wallet");
                return res;
            }, cancellationToken);

            var regionTask = Task.Run(async () =>
            {
                var res = await GetRegionAsync(cancellationToken);
                if (res != null) MarkTaskCompleted("Fetch region");
                return res;
            }, cancellationToken);

            await Task.WhenAll(summonerTask, skinTask, rankedTask, lootTask, walletTask, regionTask);

            var summonerInfo = summonerTask.Result;
            if (summonerInfo == null)
            {
                SetAccountOperationStatus("Could not load summoner information.");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Notif.notificationManager.Show("Error",
                        "Could not load summoner info (account banned or not logged in).",
                        NotificationType.Notification,
                        "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                        NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                });
                    return false;
            }

            var summonerId = summonerInfo["summonerId"]?.ToString();
            if (string.IsNullOrEmpty(summonerId))
            {
                SetAccountOperationStatus("Summoner information was incomplete.");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Notif.notificationManager.Show("Error", "SummonerId missing (account banned or invalid response).",
                        NotificationType.Notification,
                        "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                        NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                });
                return false;
            }

            SetAccountOperationStatus("Fetching match history...");
            var matchHistoryData = await GetCurrentSummonerMatchHistoryAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(matchHistoryData.LastPlayed))
            {
                _logger.Info($"[Accounts] Last played from match history endpoint: {matchHistoryData.LastPlayed}");
                DebugConsole.WriteLine($"[Accounts] Last played from match history endpoint: {matchHistoryData.LastPlayed}");
            }

            // Now get champion info (depends on summonerId)
            SetAccountOperationStatus("Fetching champion inventory...");
            var champInfo = await GetChampionInfoAsync(summonerId, cancellationToken);
            if (champInfo != null) MarkTaskCompleted("Fetch champions");

            // Loot info may need async per item
            var lootInfo = lootTask.Result;
            var lootList = new List<string>();
            var lootStructured = new List<Utils.StructuredDataEntry>();
            var lootCount = 0;
            var lootMissingIconCount = 0;
            var lootParseFailureCount = 0;

            if (lootInfo != null)
                foreach (var item in lootInfo)
                foreach (var thing in item)
                    if (thing["count"]?.ToObject<int>() > 0)
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var lootId = thing["lootId"]?.ToString();
                            if (string.IsNullOrEmpty(lootId)) continue;

                            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot/" + lootId, "",
                                    cancellationToken)
                                as HttpResponseMessage;
                            if (resp == null) continue;

                            var responseBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                            var Loot = JObject.Parse(responseBody);

                            var itemDescription = Loot["itemDesc"]?.ToString();
                            var localizedName = Loot["localizedName"]?.ToString();
                            var lootText = !string.IsNullOrEmpty(itemDescription)
                                ? itemDescription
                                : !string.IsNullOrEmpty(localizedName)
                                    ? localizedName
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

                            if (iconUrl == null)
                                lootMissingIconCount++;

                            // Format as name|url|count
                            var entryParts = new List<string> { lootText ?? string.Empty };
                            entryParts.Add(iconUrl ?? string.Empty);
                            entryParts.Add(countVal);
                            lootList.Add(string.Join("|", entryParts));
                            lootStructured.Add(new Utils.StructuredDataEntry
                            {
                                name = lootText,
                                icon = iconUrl,
                                value = countVal
                            });
                            lootCount++;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            lootParseFailureCount++;
                        }

            DebugConsole.WriteLine(
                $"[Accounts] Loot parsed: {lootCount} items, {lootMissingIconCount} missing icons, " +
                $"{lootParseFailureCount} failures");
            if (lootParseFailureCount > 0)
                _logger.Warn("Loot parsing completed with {FailureCount} failures out of {LootCount} parsed items",
                    lootParseFailureCount, lootCount);

            var rankedInfo = rankedTask.Result;
            var Rank = ApiResponseParser.BuildRankString(rankedInfo, "RANKED_SOLO_5x5");
            var Rank2 = ApiResponseParser.BuildRankString(rankedInfo, "RANKED_FLEX_SR");

            var skinInfo = skinTask.Result;
            var skinList = new List<string>();
            var skinStructured = new List<Utils.StructuredDataEntry>();
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
                            var cost = p?["cost"]?.ToString();
                            var currency = p?["currency"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(cost))
                                price = !string.IsNullOrWhiteSpace(currency) ? $"{cost} {currency}" : cost;
                        }

                        // Format as name|url|price (price optional)
                        var parts = new List<string> { name };
                        parts.Add(iconUrl ?? string.Empty);
                        parts.Add(price ?? string.Empty);
                        var entry = string.Join("|", parts);
                        skinList.Add(entry);
                        skinStructured.Add(new Utils.StructuredDataEntry
                        {
                            name = name,
                            icon = iconUrl,
                            value = price,
                            extra = new Dictionary<string, string>
                            {
                                ["itemId"] = itemId,
                                ["championId"] = champId?.ToString() ?? string.Empty
                            }
                        });
                        DebugConsole.WriteLine(
                            $"[Accounts] Parsed skin entry: Name='{name}', ItemId='{itemId}', ChampId='{champId?.ToString() ?? "null"}', IconUrl='{iconUrl}', Price='{price}' -> '{entry}'");
                    }
                    catch
                    {
                        // skip problematic skin
                    }

            // Build champion entries as name|iconUrl|roles (roles comma-separated). Map squarePortraitPath to raw.communitydragon.org
            var champList = new List<string>();
            var champStructured = new List<Utils.StructuredDataEntry>();
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
                        champStructured.Add(new Utils.StructuredDataEntry
                        {
                            name = name,
                            icon = iconUrl,
                            value = roles
                        });
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
            var existingAccount = ActualAccountlists.FindLast(x =>
                x.username == SelectedUsername && x.password == SelectedPassword);
            var preservedLastPlayed = existingAccount?.lastPlayed;
            if (!string.IsNullOrWhiteSpace(matchHistoryData.LastPlayed))
                preservedLastPlayed = matchHistoryData.LastPlayed;
            ActualAccountlists.RemoveAll(x => x.username == SelectedUsername && x.password == SelectedPassword);

            ActualAccountlists.Add(new Utils.AccountList
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
                championsData = champStructured,
                Champions = champList.Count,
                skins = string.Join(":", skinList),
                skinsData = skinStructured,
                Skins = skinList.Count,
                Loot = string.Join(":", lootList),
                lootData = lootStructured,
                Loots = lootCount,
                rank2 = Rank2,
                lastPlayed = preservedLastPlayed,
                leagueMatchHistory = !string.IsNullOrWhiteSpace(matchHistoryData.SerializedEntries)
                    ? matchHistoryData.SerializedEntries
                    : existingAccount?.leagueMatchHistory,
                note = existingAccount?.note
            });

            cancellationToken.ThrowIfCancellationRequested();
            await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

            // Update UI last
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AccountsDataGrid.ItemsSource = null;
                AccountsDataGrid.ItemsSource = ActualAccountlists;
                ApplyLeagueSortToGrid();
                AccountsDataGrid.Items.Refresh();
            });

            PullDataCompleted?.Invoke();
            ValorantAccounts.RunPullDataInBackground();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SetAccountOperationStatus("The account data could not be loaded.");
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

            return false;
        }
    }

    private async Task<T?> RetryAsync<T>(Func<Task<T?>> action, CancellationToken cancellationToken,
        int maxRetries = 5, int delayMs = 1500)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await action();
                if (result != null)
                    return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        return default;
    }

    private Task<JObject?> GetSummonerInfoAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<JObject>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-summoner/v1/current-summoner", "",
                cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            return ApiResponseParser.ParseSummoner(body);
        }, cancellationToken);
    }

    private Task<(string? LastPlayed, string? SerializedEntries)> GetCurrentSummonerMatchHistoryAsync(
        CancellationToken cancellationToken)
    {
        return RetryAsync<(string? LastPlayed, string? SerializedEntries)>(async () =>
        {
            var resp = await Lcu.Connector("league", "get",
                "/lol-match-history/v1/products/lol/current-summoner/matches", "", cancellationToken);
            if (resp == null) return default;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return default;

            return ApiResponseParser.ParseMatchHistory(body);
        }, cancellationToken);
    }

    private Task<JArray?> GetSkinInfoAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<JArray>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-catalog/v1/items/CHAMPION_SKIN", "",
                cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JArray.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }, cancellationToken);
    }

    private Task<JToken?> GetLootInfoAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-loot/v1/player-loot-map", "", cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JToken.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }, cancellationToken);
    }

    private Task<Utils.Wallet?> GetWalletAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<Utils.Wallet>(async () =>
        {
            var resp = await Lcu.Connector("league", "get",
                "/lol-inventory/v1/wallet?currencyTypes=[%22RP%22,%22lol_blue_essence%22]", "", cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            return ApiResponseParser.ParseWallet(body);
        }, cancellationToken);
    }

    private Task<JToken?> GetRankedInfoAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/lol-ranked/v1/current-ranked-stats", "", cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            var parsed = ApiResponseParser.ParseRankedStats(body);
            if (parsed != null)
                DebugConsole.WriteLine("[Accounts] Ranked stats fetched");
            return parsed;
        }, cancellationToken);
    }

    private Task<JToken?> GetRegionAsync(CancellationToken cancellationToken)
    {
        return RetryAsync<JToken>(async () =>
        {
            var resp = await Lcu.Connector("league", "get", "/riotclient/region-locale", "", cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                return JToken.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }, cancellationToken);
    }

    private Task<JArray?> GetChampionInfoAsync(string summonerId, CancellationToken cancellationToken)
    {
        return RetryAsync<JArray>(async () =>
        {
            var resp = await Lcu.Connector("league", "get",
                $"/lol-champions/v1/inventories/{summonerId}/champions-minimal", "", cancellationToken);
            if (resp == null) return null;

            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken);
    }

    private async Task<(bool isBanned, string note)> CheckPermanentBanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", "/lol-player-behavior/v3/reform-cards", "",
                cancellationToken);
            if (resp == null) return (false, "");

            var responseBody = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            var operationTitle = clickedButton.Name switch
            {
                "Stealthlogin" => "Stealth login",
                "DebugLogin" => "Debug mode login",
                _ => "Logging in"
            };
            StartAccountOperation(operationTitle,
                new[]
                {
                    "Start Riot client",
                    "Find login window",
                    "Submit credentials",
                    "Wait for authentication",
                    "Open League client",
                    "Waiting for summoner readiness",
                    "Fetch account data"
                },
                cancellationToken => LoginAsync(clickedButton.Name, cancellationToken));
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error starting login");
            Notif.notificationManager.Show("Error", "An error occurred while starting login",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
    }

    private async Task<bool> LoginAsync(string loginButtonName, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            Utils.KillLeagueFunc();
            var num = 0;
            var loginAttempts = 0;

            SetAccountOperationStatus("Starting Riot client...");
            switch (loginButtonName)
            {
                case "Login":
                    StartRiotClient("--launch-product=league_of_legends --launch-patchline=live");
                    break;

                case "DebugLogin":
                    LcuWebSocketMonitor.Start();
                    await App.DebugClientTrafficLauncher.LaunchAsync(Misc.Settings.settingsloaded.riotPath,
                        "--launch-product=league_of_legends --launch-patchline=live --allow-multiple-clients");
                    DebugConsole.WriteLine("[Accounts] Started Riot client in native debug mode; LCU traffic capture is active.");
                    break;

                case "Stealthlogin":
                    await App.OfflineLauncher.LaunchRiotOrLeagueOfflineAsync(Misc.Settings.settingsloaded.riotPath,
                        cancellationToken: cancellationToken);
                    break;

                default:
                    return false;
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

                cancellationToken.ThrowIfCancellationRequested();
                SetAccountOperationStatus("Waiting for Riot client...");
                DebugConsole.WriteLine($"[Accounts] Waiting for riot process");

                await Task.Delay(200, cancellationToken);
                num++;
                if (num == 80) return false;
            }
            MarkTaskCompleted("Start Riot client");

            while (true)
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var restartLogin = false;
                    var cancelLogin = false;
                    var app = Application.Attach(riotval);

                    using (var automation = new UIA3Automation())
                    {
                        var window = app.GetMainWindow(automation) ??
                                     throw new Exception("Riot window not found");
                        var riotcontent =
                            window.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND")) ??
                            throw new Exception("Riot content not found");
                        MarkTaskCompleted("Find login window");


                        SetAccountOperationStatus("Finding login controls...");
                        var usernameField = riotcontent.FindFirstDescendant(cf => cf.ByAutomationId("username"))
                            .AsTextBox();
                        if (usernameField == null) throw new Exception("Username field not found");


                        // Find the password field
                        var passwordField = riotcontent.FindFirstDescendant(cf => cf.ByAutomationId("password"))
                            .AsTextBox();
                        if (passwordField == null) throw new Exception("Password field not found");


                        var checkbox = riotcontent.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox));
                        if (checkbox == null) throw new Exception("Checkbox field not found");

                        var siblings = riotcontent.FindAllChildren();
                        if (checkbox.Parent == null) throw new Exception("Checkbox parent not found");
                        DebugConsole.WriteLine(siblings.Length.ToString());
                        var count = Array.IndexOf(siblings, checkbox) + 1;
                        if (siblings.Length <= count) throw new Exception("Not enough siblings found for the checkbox");
                        dynamic? signInElement = null;
                        while (count < siblings.Length)
                        {
                            signInElement = siblings[count++].AsButton();

                            DebugConsole.WriteLine($"Found checkbox: {checkbox.Name}");
                            DebugConsole.WriteLine($"Found siblings count: {siblings.Length}");

                            if (signInElement.ControlType != ControlType.Button) continue;
                            break;
                        }

                        usernameField.Text = SelectedUsername ?? throw new Exception("Username not selected");
                        passwordField.Text = SelectedPassword ?? throw new Exception("Password not selected");
                        if (signInElement != null)
                        {
                            SetAccountOperationStatus("Submitting credentials...");
                            while (!signInElement.IsEnabled)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await Task.Delay(200, cancellationToken);
                            }
                            signInElement.Invoke();
                            MarkTaskCompleted("Submit credentials");

                            // brief pause to allow any login error tooltip to appear
                            await Task.Delay(500, cancellationToken);

                            SetAccountOperationStatus("Waiting for Riot authentication...");
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
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
                                            var existingNote = ActualAccountlists.FindLast(x =>
                                                x.username == SelectedUsername && x.password == SelectedPassword)?.note;
                                            ActualAccountlists.RemoveAll(x =>
                                                x.username == SelectedUsername && x.password == SelectedPassword);
                                            ActualAccountlists.Add(new Utils.AccountList
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

                                            return false; // pause/stop login processing
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

                                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "",
                                    cancellationToken);
                                string status = await resp.Content.ReadAsStringAsync(cancellationToken)
                                    .ConfigureAwait(false);
                                if (status == "\"Accepted\"") break;
                                if (status == "\"AcceptanceRequired\"")
                                {
                                    await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "",
                                        cancellationToken);
                                    await Task.Delay(200, cancellationToken);
                                }
                                else
                                {
                                    await Task.Delay(500, cancellationToken);
                                }
                            }

                            MarkTaskCompleted("Wait for authentication");
                            if (cancelLogin) return false;

                            if (restartLogin)
                            {
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }

                            SetAccountOperationStatus("Opening League client...");
                            await Lcu.Connector("riot", "post",
                                "/product-launcher/v1/products/league_of_legends/patchlines/live", "",
                                cancellationToken);
                            MarkTaskCompleted("Open League client");
                            return await WaitForSummonerReadyAsync(cancellationToken);
                        }

                        await Task.Delay(500, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Transient error during login automation");
                    DebugConsole.WriteLine($"[Accounts] Login automation retry: {ex.Message}", ConsoleColor.Yellow);
                    await Task.Delay(200, cancellationToken);
                }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Utils.KillLeagueFunc();
            throw;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error logging in");
            return false;
        }
    }


    private async Task<bool> WaitForSummonerReadyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resp = await Lcu.Connector("league", "get", "/lol-player-behavior/v3/reform-cards", "",
                        cancellationToken)
                    as HttpResponseMessage;

                if (resp != null && resp.IsSuccessStatusCode) // Ensure HTTP 200
                {
                    var content = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    // Optional: you can parse content here or just call PullDataAsync
                    SetAccountOperationStatus("Pulling account data...");
                    InitializeProgressTasks(GetPullTasks());
                    return await PullDataAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // LCU not ready yet, ignore and retry
            }

            await Task.Delay(1000, cancellationToken); // retry every 1 second
        }
    }

    public async Task UpdateAllRanks()
    {
        await _accountDataGate.WaitAsync();
        try
        {
            await UpdateAllRanksCoreAsync();
        }
        finally
        {
            _accountDataGate.Release();
        }
    }

    private async Task UpdateAllRanksCoreAsync()
    {
        if (ActualAccountlists == null || ActualAccountlists.Count == 0)
            return;

        DebugConsole.WriteLine($"[Accounts] Starting rank update for {ActualAccountlists.Count} account(s).");
        var total = ActualAccountlists.Count;
        var processed = 0;
        var anyChanges = false;
        var rankCancellation = new CancellationTokenSource();
        _rankUpdateCancellation = rankCancellation;
        var cancellationToken = rankCancellation.Token;

        Dispatcher.Invoke(() =>
        {
            ShowRankProgress(total, rankCancellation);
        });

        try
        {
            var userAgents = new[]
            {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36 Edg/123.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_4) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15"
        };

            var random = new Random();

            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                UseProxy = false
            };

            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            foreach (var account in ActualAccountlists)
            {
                processed++;
                var accountLabel = !string.IsNullOrWhiteSpace(account.riotID)
                    ? account.riotID
                    : !string.IsNullOrWhiteSpace(account.username)
                        ? account.username
                        : $"account #{processed}";

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(account.riotID))
                    {
                        DebugConsole.WriteLine(
                            $"[Accounts] Skipping rank update for {accountLabel}: missing Riot ID.",
                            ConsoleColor.Yellow);
                        continue;
                    }

                    var formattedRiotId = account.riotID.Replace("#", "-").Trim();
                    var encodedRiotId = Uri.EscapeDataString(formattedRiotId);
                    var url = $"https://p1.xdx.gg/rid/1/{encodedRiotId}";
                    DebugConsole.WriteLine($"[Accounts] Updating rank for {accountLabel} from {url}");

                    // Rotate user agent
                    http.DefaultRequestHeaders.UserAgent.Clear();
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgents[random.Next(userAgents.Length)]);

                    // Clear and set realistic headers for each request
                    http.DefaultRequestHeaders.Accept.Clear();
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                    http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
                    http.DefaultRequestHeaders.AcceptLanguage.Clear();
                    http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
                    http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en;q=0.9");
                    http.DefaultRequestHeaders.AcceptEncoding.Clear();
                    http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
                    http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
                    http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("br");
                    http.DefaultRequestHeaders.Connection.Clear();
                    http.DefaultRequestHeaders.Connection.Add("keep-alive");
                    http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                    {
                        NoCache = true
                    };

                    // Add common browser headers
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\"");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
                    http.DefaultRequestHeaders.Referrer = new Uri("https://xdx.gg/");

                    var response = await http.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var payload = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        DebugConsole.WriteLine($"[Accounts] Rank update returned empty payload for {accountLabel}.",
                            ConsoleColor.Yellow);
                        continue;
                    }

                    var data = JObject.Parse(payload);

                    var soloRank = BuildRankFromXdxApi(data, "solo");
                    if (!string.IsNullOrWhiteSpace(soloRank))
                    {
                        account.rank = soloRank;
                        anyChanges = true;
                    }

                    var flexRank = BuildRankFromXdxApi(data, "flex");
                    if (!string.IsNullOrWhiteSpace(flexRank))
                    {
                        account.rank2 = flexRank;
                        anyChanges = true;
                    }

                    var (lastPlayed, latestMatchSummary) = ParseLatestMatchFromXdxApi(data);
                    if (!string.IsNullOrWhiteSpace(lastPlayed))
                    {
                        account.lastPlayed = lastPlayed;
                        anyChanges = true;
                    }

                    if (!string.IsNullOrWhiteSpace(latestMatchSummary))
                    {
                        account.leagueMatchHistory = latestMatchSummary;
                        anyChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(soloRank) && string.IsNullOrWhiteSpace(flexRank))
                    {
                        DebugConsole.WriteLine(
                            $"[Accounts] Rank update could not find xdx API ranking data for {accountLabel}.",
                            ConsoleColor.Yellow);
                    }

                    if (string.IsNullOrWhiteSpace(soloRank) && string.IsNullOrWhiteSpace(flexRank))
                        DebugConsole.WriteLine(
                            $"[Accounts] Rank update found page data for {accountLabel} but no solo or flex rank values.",
                            ConsoleColor.Yellow);
                    else
                        DebugConsole.WriteLine(
                            $"[Accounts] Rank update succeeded for {accountLabel}. Solo: {soloRank ?? "N/A"}, Flex: {flexRank ?? "N/A"}");

                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "UpdateAllRanks failed for {Account}", accountLabel);
                    DebugConsole.WriteLine(
                        $"[Accounts] Rank update failed for {accountLabel}: {ex.GetType().Name} - {ex.Message}",
                        ConsoleColor.Red);
                }

                await Task.Delay(2000, cancellationToken);

                // Update progress bar
                Dispatcher.Invoke(() => UpdateRankProgress(processed, total, accountLabel));
            }

            // Save CSV if any changes
            if (anyChanges)
            {
                await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

                Dispatcher.Invoke(() => AccountsDataGrid.Items.Refresh());
                DebugConsole.WriteLine("[Accounts] Rank update completed with changes saved.");
            }
            else
            {
                DebugConsole.WriteLine("[Accounts] Rank update completed with no changes.");
            }
        }
        finally
        {
            Dispatcher.Invoke(HideRankProgress);
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

            var confirm = AppMessageBox.Show("Delete the selected account?", "Confirm delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            ActualAccountlists.RemoveAll(r =>
                r.username == selectedrow.username &&
                r.password == selectedrow.password &&
                r.server == selectedrow.server);

            ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");

            await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

            AccountsDataGrid.ItemsSource = null;
            AccountsDataGrid.ItemsSource = ActualAccountlists;
            ApplyLeagueSortToGrid();

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

    private string? BuildRankFromXdxApi(JObject data, string prefix)
    {
        var tier = NormalizeLeagueTier(data[$"{prefix}-tier"]?.ToString());
        if (string.IsNullOrWhiteSpace(tier))
            return null;

        if (tier.Equals("Unranked", StringComparison.OrdinalIgnoreCase))
            return tier;

        var division = NormalizeHtmlText(data[$"{prefix}-division"]?.ToString());
        var lp = NormalizeHtmlText(data[$"{prefix}-lp"]?.ToString());
        var wins = NormalizeHtmlText(data[$"{prefix}-wins"]?.ToString());
        var losses = NormalizeHtmlText(data[$"{prefix}-losses"]?.ToString());

        var rankParts = new List<string> { tier };
        if (!string.IsNullOrWhiteSpace(division) && !tier.Equals("Master", StringComparison.OrdinalIgnoreCase) &&
            !tier.Equals("Grandmaster", StringComparison.OrdinalIgnoreCase) &&
            !tier.Equals("Challenger", StringComparison.OrdinalIgnoreCase))
            rankParts.Add(division);

        if (!string.IsNullOrWhiteSpace(lp))
            rankParts.Add($"{lp} LP");

        var rankText = string.Join(" ", rankParts.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(wins) && !string.IsNullOrWhiteSpace(losses))
            rankText = $"{rankText}, {wins}W / {losses}L";

        return rankText;
    }

    private (string? LastPlayed, string? Summary) ParseLatestMatchFromXdxApi(JObject data)
    {
        try
        {
            var matches = data["matches"] as JArray;
            if (matches == null || matches.Count == 0 || matches[0] is not JArray latestMatch || latestMatch.Count < 4)
            {
                _logger.Info("[Accounts] xdx latest match parse skipped: no match data found.");
                DebugConsole.WriteLine("[Accounts] xdx latest match parse skipped: no match data found.",
                    ConsoleColor.Yellow);
                return (null, null);
            }

            var championId = latestMatch[1]?.ToObject<int?>();
            var queueId = latestMatch[2]?.ToObject<int?>();
            var playedAtUnix = latestMatch[3]?.ToObject<long?>();

            var queue = GetQueueNameFromId(queueId);
            var playedAt = playedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(playedAtUnix.Value).LocalDateTime : (DateTime?)null;
            var timeAgo = playedAt.HasValue ? GetRelativeTimeText(playedAt.Value) : null;

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(queue)) summaryParts.Add(queue);
            if (!string.IsNullOrWhiteSpace(timeAgo)) summaryParts.Add(timeAgo);

            var summary = summaryParts.Count > 0 ? string.Join(" | ", summaryParts) : null;
            var lastPlayed = playedAt?.ToString("g");
            return (lastPlayed, summary);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "[Accounts] xdx latest match parse failed.");
            DebugConsole.WriteLine($"[Accounts] xdx latest match parse failed: {ex.Message}", ConsoleColor.Red);
            return (null, null);
        }
    }

  
    private string GetQueueNameFromId(int? queueId)
    {
        return queueId switch
        {
            420 => "Solo/Duo",
            440 => "Flex 5v5",
            450 => "ARAM",
            1700 => "Arena",
            _ when queueId.HasValue => $"Queue {queueId.Value}",
            _ => string.Empty
        };
    }

    private string GetRelativeTimeText(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;
        if (elapsed.TotalMinutes < 1)
            return "just now";

        if (elapsed.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            return $"{minutes} minute{(minutes == 1 ? string.Empty : "s")} ago";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return $"{hours} hour{(hours == 1 ? string.Empty : "s")} ago";
        }

        var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
        return $"{days} day{(days == 1 ? string.Empty : "s")} ago";
    }

    private string NormalizeHtmlText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decoded = System.Net.WebUtility.HtmlDecode(value);
        return Regex.Replace(decoded ?? string.Empty, @"\s+", " ").Trim();
    }

    private string NormalizeLeagueTier(string? value)
    {
        var normalized = NormalizeHtmlText(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private string? ConvertRelativeTimeToLocalString(string? relativeTime)
    {
        var normalized = NormalizeHtmlText(relativeTime);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsedLocal))
            return parsedLocal.ToString("g");

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedLocal))
            return parsedLocal.ToString("g");

        var match = Regex.Match(normalized,
            @"^(?<value>\d+|a|an|one)\s+(?<unit>minute|minutes|hour|hours|day|days|week|weeks|month|months|year|years)\s+ago$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var amountText = match.Groups["value"].Value;
        var amount = amountText.Equals("a", StringComparison.OrdinalIgnoreCase) ||
                     amountText.Equals("an", StringComparison.OrdinalIgnoreCase) ||
                     amountText.Equals("one", StringComparison.OrdinalIgnoreCase)
            ? 1
            : int.TryParse(amountText, out var parsedAmount)
                ? parsedAmount
                : 0;

        if (amount <= 0)
            return null;

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var timestamp = unit switch
        {
            "minute" or "minutes" => DateTime.Now.AddMinutes(-amount),
            "hour" or "hours" => DateTime.Now.AddHours(-amount),
            "day" or "days" => DateTime.Now.AddDays(-amount),
            "week" or "weeks" => DateTime.Now.AddDays(-7 * amount),
            "month" or "months" => DateTime.Now.AddMonths(-amount),
            "year" or "years" => DateTime.Now.AddYears(-amount),
            _ => DateTime.Now
        };

        return timestamp.ToString("g");
    }


    private void OnKillClientClick(object sender, RoutedEventArgs e)
    {
        Utils.KillLeagueFunc();
    }
    private void Accounts_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            e.Handled = true;
            return;
        }

        var row = ItemsControl.ContainerFromElement(AccountsDataGrid, source) as DataGridRow;
        if (row?.Item is not Utils.AccountList account)
        {
            e.Handled = true;
            return;
        }

        AccountsDataGrid.SelectedItem = account;
        AccountsDataGrid.ScrollIntoView(account);
    }

    private void AccountsContextKillClient_Click(object sender, RoutedEventArgs e) => OnKillClientClick(sender, e);
    private void CopySelectedAccount(string text)
    {
        if (AccountsDataGrid.SelectedItem is Utils.AccountList account)
            System.Windows.Clipboard.SetText(text);
    }

    private Utils.AccountList? GetSelectedAccount() => AccountsDataGrid.SelectedItem as Utils.AccountList;
    private void AccountsContextCopyUsername_Click(object sender, RoutedEventArgs e) => CopySelectedAccount(GetSelectedAccount()?.username ?? string.Empty);
    private void AccountsContextCopyPassword_Click(object sender, RoutedEventArgs e) => CopySelectedAccount(GetSelectedAccount()?.password ?? string.Empty);
    private void AccountsContextCopyCredentials_Click(object sender, RoutedEventArgs e)
    {
        var account = GetSelectedAccount();
        CopySelectedAccount(account == null ? string.Empty : $"{account.username}:{account.password}");
    }
    private void AccountsContextCopyBasicLeagueFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.League);
    private void AccountsContextCopyBasicLeagueSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.League);
    private void AccountsContextCopyBasicValorantFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.Valorant);
    private void AccountsContextCopyBasicValorantSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.Valorant);
    private void AccountsContextCopyBasicBothFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.Both);
    private void AccountsContextCopyBasicBothSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(false, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.Both);
    private void AccountsContextCopyFullLeagueFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.League);
    private void AccountsContextCopyFullLeagueSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.League);
    private void AccountsContextCopyFullValorantFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.Valorant);
    private void AccountsContextCopyFullValorantSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.Valorant);
    private void AccountsContextCopyFullBothFormatted_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Formatted, Utils.AccountCopySection.Both);
    private void AccountsContextCopyFullBothSimple_Click(object sender, RoutedEventArgs e) => CopyFormattedAccount(true, Utils.AccountCopyFormat.Simple, Utils.AccountCopySection.Both);
    private void CopyFormattedAccount(bool fullDetails, Utils.AccountCopyFormat format, Utils.AccountCopySection section)
    {
        var account = GetSelectedAccount();
        if (account != null)
            CopySelectedAccount(Utils.FormatAccountForCopy(account, fullDetails, format, section));
    }

    private void AccountsContextOpenOpGg_Click(object sender, RoutedEventArgs e) => OpenLeagueLookup("https://www.op.gg/summoners/{0}/{1}");
    private void AccountsContextOpenLeagueOfGraphs_Click(object sender, RoutedEventArgs e) => OpenLeagueLookup("https://www.leagueofgraphs.com/summoner/{0}/{1}");
    private void AccountsContextOpenValorantTracker_Click(object sender, RoutedEventArgs e) => OpenValorantTrackerLookup();

    private void OpenLeagueLookup(string urlTemplate)
    {
        var account = GetSelectedAccount();
        if (account == null || string.IsNullOrWhiteSpace(account.riotID) || string.IsNullOrWhiteSpace(account.server))
            return;

        var region = Uri.EscapeDataString(account.server.Trim().ToLowerInvariant());
        var riotId = Uri.EscapeDataString(account.riotID.Trim().Replace("#", "-"));
        OpenExternalUrl(string.Format(urlTemplate, region, riotId));
    }

    private void OpenValorantTrackerLookup()
    {
        var account = GetSelectedAccount();
        if (account == null || string.IsNullOrWhiteSpace(account.riotID))
            return;

        var riotId = Uri.EscapeDataString(account.riotID.Trim());
        OpenExternalUrl($"https://tracker.gg/valorant/profile/riot/{riotId}");
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Accounts] Failed to open lookup URL: {exception.Message}", ConsoleColor.Yellow);
        }
    }

    private void AccountsContextLogin_Click(object sender, RoutedEventArgs e) => OnLoginClick(new Button { Name = "Login" }, e);
    private void AccountsContextDebugLogin_Click(object sender, RoutedEventArgs e) => OnLoginClick(new Button { Name = "DebugLogin" }, e);
    private void AccountsContextStealthLogin_Click(object sender, RoutedEventArgs e) => OnLoginClick(new Button { Name = "Stealthlogin" }, e);
    private void AccountsContextSecondClient_Click(object sender, RoutedEventArgs e) => OnSecondaryClientClick(sender, e);
    private void AccountsContextGenerateToken_Click(object sender, RoutedEventArgs e) => GenerateLoginToken_OnClick(sender, e);
    private void AccountsContextUseToken_Click(object sender, RoutedEventArgs e) => UseLoginToken_OnClick(sender, e);
    private void AccountsContextNameChange_Click(object sender, RoutedEventArgs e) => OnNameChangeClick(sender, e);
    private void AccountsContextRemoveDuplicates_Click(object sender, RoutedEventArgs e) => OnRemoveDuplicatesClick(sender, e);
    private void AccountsContextDelete_Click(object sender, RoutedEventArgs e) => OnDeleteClick(sender, e);

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
        StartRiotClient("--launch-product=league_of_legends --launch-patchline=live");
    }

    private static void StartRiotClient(string arguments)
    {
        Utils.StartRiotClient(Misc.Settings.settingsloaded.riotPath, arguments);
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
                            lastPlayed = GetField("lastPlayed", -1) ?? "",
                            leagueMatchHistory = GetField("leagueMatchHistory", -1) ?? "",
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
        if (sender is not DataGrid dataGrid || !dataGrid.CurrentCell.IsValid)
            return;

        if (!Executing)
        {
            Executing = true;
            try
            {
                if (dataGrid.CurrentCell.IsValid)
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
                            case "Last Played":
                                if (e.ClickCount >= 2 && !string.IsNullOrWhiteSpace(selectedrow.leagueMatchHistory))
                                    secondWindow = new DisplayDataWithSearch(selectedrow.leagueMatchHistory);
                                break;
                            case "RiotID"
                                : //otherwise will open op.gg could add this functionality only to "rank" or "riot id" column alternatively 
                                if (string.IsNullOrWhiteSpace(selectedrow.riotID)) break;
                                var url =
                                    $"https://www.leagueofgraphs.com/summoner/{selectedrow.server}/{selectedrow.riotID.Replace("#", "-")}";
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
        StartRiotClient(
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
            if (dataGrid?.CurrentCell.IsValid == true)
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
            var clickedButton = sender as Button;
            if (clickedButton == null) return;

            await App.AuthLauncher.LaunchRiotClientWithTokenCapture(Misc.Settings.settingsloaded.riotPath,
                persistLogin: persist,
                tokenProduct: "league");

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

                    if (!string.IsNullOrEmpty(riotval) || attempts++ >= 80)
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
                            var window = app.GetMainWindow(automation);
                            if (window == null)
                            {
                                await Task.Delay(200);
                                continue;
                            }

                            var riotcontent =
                                window.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND"));
                            if (riotcontent == null)
                            {
                                await Task.Delay(200);
                                continue;
                            }

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
                            FlaUI.Core.AutomationElements.Button? signInElement = null;
                            while (count < siblings.Length)
                            {
                                var candidate = siblings[count++].AsButton();
                                if (candidate != null && candidate.ControlType == ControlType.Button)
                                {
                                    signInElement = candidate;
                                    break;
                                }
                            }

                            usernameField.Text = SelectedUsername ?? throw new Exception("Username not selected");
                            passwordField.Text = SelectedPassword ?? throw new Exception("Password not selected");

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
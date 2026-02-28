using CsvHelper.Configuration;
using FlaUI.UIA3;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Button = Wpf.Ui.Controls.Button;
using Application = FlaUI.Core.Application;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for ValorantAccounts.xaml
/// </summary>
public partial class ValorantAccounts : Page
{
    public static string? SelectedUsername;
    public static string? SelectedPassword;
    private bool Executing;
    private readonly object _fileChangeLock = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };
    private bool _initialized;
    private bool _pendingReload;
    private DateTime _lastKnownFileWrite = DateTime.MinValue;
    private DateTime _lastFileChange = DateTime.MinValue;
    private FileSystemWatcher? fileWatcher;

    public ValorantAccounts()
    {
        InitializeComponent();
        Loaded += ValorantAccounts_Loaded;
        Unloaded += ValorantAccounts_Unloaded;
        IsVisibleChanged += ValorantAccounts_IsVisibleChanged;
        Misc.Settings.AccountPasswordSupplied += OnAccountPasswordSupplied;
        AccountFileStore.AccountsFileUpdated += OnAccountsFileUpdated;
    }

    private async void ValorantAccounts_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
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

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedValorantAccountAsync();
    }

    private async void OnAccountsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        await DeleteSelectedValorantAccountAsync();
        e.Handled = true;
    }

    private async Task DeleteSelectedValorantAccountAsync()
    {
        try
        {
            var selectedrow = ValorantAccountsDataGrid.SelectedItem as Utils.AccountList;
            if (selectedrow == null) return;

            var confirm = MessageBox.Show("Delete the selected Valorant account?", "Confirm delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            ActualAccountlists?.RemoveAll(r =>
                r.username == selectedrow.username &&
                r.password == selectedrow.password &&
                r.valorantServer == selectedrow.valorantServer);

            ActualAccountlists?.RemoveAll(r => r.username == "username" && r.password == "password");

            await AccountFileStore.SaveAsync(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

            ValorantAccountsDataGrid.ItemsSource = null;
            ValorantAccountsDataGrid.ItemsSource = ActualAccountlists;

            ValorantAccountsDataGrid.Items.SortDescriptions.Clear();
            ValorantAccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("valorantLevel", ListSortDirection.Descending));

            ValorantAccountsDataGrid.Items.Refresh();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error deleting Valorant account");
        }
    }

    public static List<Utils.AccountList>? ActualAccountlists { get; set; }

    private void ValorantAccounts_Unloaded(object sender, RoutedEventArgs e)
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

    private void OnRemoveDuplicatesClick(object sender, RoutedEventArgs e)
    {
        if (ActualAccountlists == null) return;

        ActualAccountlists = ActualAccountlists
            .GroupBy(x => (x.username ?? "").Trim().ToLower() + "|" + (x.password ?? "").Trim())
            .Select(g => g.First())
            .ToList();
        AccountFileStore.Save(AccountFileStore.GetAccountsFilePath(), ActualAccountlists, config);

        ValorantAccountsDataGrid.ItemsSource = null;
        ValorantAccountsDataGrid.ItemsSource = ActualAccountlists;
        ValorantAccountsDataGrid.Items.Refresh();
    }

    private void OnKillClientClick(object sender, RoutedEventArgs e)
    {
        Utils.KillLeagueFunc();
    }

    private async void OnOpenValorantClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await CheckValorant()) throw new Exception("Valorant not installed");

            OpenValorant();
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error opening Valorant flow");
        }
    }

    private void OpenValorant()
    {
        Process.Start(Misc.Settings.settingsloaded.riotPath,
            "--launch-product=Valorant --launch-patchline=live");
    }

    public async Task<bool> CheckValorant()
    {
        if (File.Exists(Misc.Settings.settingsloaded.riotPath))
            return true;
        return false;
    }

    private void OnNameChangeClick(object sender, RoutedEventArgs e)
    {
        var namechanger = new ChangeName();
        namechanger.Show();
    }

    private async void OnPullDataClick(object sender, RoutedEventArgs e)
    {
        try
        {


            
            if (Process.GetProcessesByName("Riot Client").Length == 0 && Process.GetProcessesByName("RiotClientUx").Length == 0)
            {
                Notif.notificationManager.Show("Error", "League of Legends client is not running!",
                    NotificationType.Notification,
                    "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                    NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
                return;
            }

            var (client, _, _, puuid, idToken) = await Lcu.CreateValorantClientAsync();
            TrySelectAccountFromIdToken(idToken);

            if (string.IsNullOrWhiteSpace(SelectedUsername))
                SelectedUsername = GetUnameFromIdToken(idToken);

            if (string.IsNullOrWhiteSpace(SelectedPassword))
            {
                var password = PromptForPassword("Enter account password.");
                if (string.IsNullOrWhiteSpace(password))
                    return;

                SelectedPassword = password;
            }

            using (client)
            {
                var payload = $"{{\"id_token\":\"{idToken}\"}}";
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await client.PutAsync(
                    "https://riot-geo.pas.si.riotgames.com/pas/v1/product/valorant", content);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                DebugConsole.WriteLine($"[ValorantAccounts] Geo response: {body}");

                var geoJson = JObject.Parse(body);
                var liveRegion = geoJson["affinities"]?["live"]?.ToString();
                if (!string.IsNullOrWhiteSpace(liveRegion))
                {
                    var valorantData = await CollectValorantAccountDataAsync(client, liveRegion, puuid);
                    var filePath = AccountFileStore.GetAccountsFilePath();
                    var records = await AccountFileStore.LoadAsync(filePath, config);
                    var updated = false;
                    foreach (var record in records)
                    {
                        if (!string.Equals(record.username, SelectedUsername, StringComparison.Ordinal) ||
                            !string.Equals(record.password, SelectedPassword, StringComparison.Ordinal))
                            continue;

                        record.valorantServer = liveRegion;
                        record.valorantAgents = string.Join(":", valorantData.Agents);
                        record.valorantContracts = string.Join(":", valorantData.Contracts);
                        record.valorantSprays = string.Join(":", valorantData.Sprays);
                        record.valorantGunBuddies = string.Join(":", valorantData.GunBuddies);
                        record.valorantCards = string.Join(":", valorantData.Cards);
                        record.valorantSkins = string.Join(":", valorantData.Skins);
                        record.valorantSkinVariants = string.Join(":", valorantData.SkinVariants);
                        record.valorantTitles = string.Join(":", valorantData.Titles);
                        record.valorantVp = valorantData.ValorantVp;
                        record.valorantKc = valorantData.ValorantKc;
                        record.valorantRp = valorantData.ValorantRp;
                        record.valorantRank = valorantData.ValorantRank ?? "Unranked";
                        record.valorantLevel = valorantData.ValorantLevel;
                        record.valorantXp = valorantData.ValorantXp;
                        updated = true;
                        break;
                    }

                    if (!updated)
                    {
                        records.Add(new Utils.AccountList
                        {
                            username = SelectedUsername,
                            password = SelectedPassword,
                            valorantServer = liveRegion,
                            valorantAgents = string.Join(":", valorantData.Agents),
                            valorantContracts = string.Join(":", valorantData.Contracts),
                            valorantSprays = string.Join(":", valorantData.Sprays),
                            valorantGunBuddies = string.Join(":", valorantData.GunBuddies),
                            valorantCards = string.Join(":", valorantData.Cards),
                            valorantSkins = string.Join(":", valorantData.Skins),
                            valorantSkinVariants = string.Join(":", valorantData.SkinVariants),
                            valorantTitles = string.Join(":", valorantData.Titles),
                            valorantVp = valorantData.ValorantVp,
                            valorantKc = valorantData.ValorantKc,
                            valorantRp = valorantData.ValorantRp,
                            valorantRank = valorantData.ValorantRank ?? "Unranked",
                            valorantLevel = valorantData.ValorantLevel,
                            valorantXp = valorantData.ValorantXp
                        });
                    }

                    await AccountFileStore.SaveAsync(filePath, records, config);
                }
            }

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
            Notif.notificationManager.Show("Info", "Valorant account data refreshed",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(5), null, null, null, null, () => Notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error pulling Valorant data");
            DebugConsole.WriteLine($"[ValorantAccounts] Geo request failed: {ex.Message}", ConsoleColor.Red);
        }
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {

        try
        {

            if (ValorantAccountsDataGrid.SelectedCells.Count == 0) throw new Exception("Account not selected");
            var selectedColumn = ValorantAccountsDataGrid.SelectedCells[0].Column;

            if (selectedColumn != null)
            {
                var header = selectedColumn.Header?.ToString();
                var selectedRow = ValorantAccountsDataGrid.SelectedItem as Utils.AccountList;
                if (selectedRow == null || header == null) throw new Exception("Account not selected");
                SelectedUsername = selectedRow.username;
                SelectedPassword = selectedRow.password;
            }

            DebugConsole.WriteLine($"[Accounts] Username selected: {SelectedUsername}");

            DebugConsole.WriteLine("[ValorantAccounts] OpenLeague1_Click triggered");
            Utils.KillLeagueFunc();
            if (!await CheckValorant()) throw new Exception("Valorant not installed");
            OpenValorant();
            DebugConsole.WriteLine("[ValorantAccounts] Valorant launch requested");
            var num = 0;
            var clickedButton = sender as Button;
            if (clickedButton == null) return;
            var riotval = string.Empty;
            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0)
                {
                    riotval = "Riot Client";
                    DebugConsole.WriteLine("[ValorantAccounts] Riot Client process detected");
                    break;
                }

                if (Process.GetProcessesByName("RiotClientUx").Length != 0)
                {
                    riotval = "RiotClientUx";
                    DebugConsole.WriteLine("[ValorantAccounts] RiotClientUx process detected");
                    break;
                }


                Thread.Sleep(200);
                num++;
                if (num == 20) return;
            }
            var loginAttempts = 0;

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
                            DebugConsole.WriteLine("[ValorantAccounts] Submitting login form");
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

                                            DebugConsole.WriteLine("[ValorantAccounts] Invalid login detected, account updated");
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
                                "/product-launcher/v1/products/valorant/patchlines/live", "");
                            DebugConsole.WriteLine("[ValorantAccounts] Valorant patchline launch requested");
                            OnPullDataClick(this, new RoutedEventArgs());
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
            LogManager.GetCurrentClassLogger().Error(exception, "Error Logging in to Valorant");
            DebugConsole.WriteLine($"[ValorantAccounts]Login failed: {exception.Message}", ConsoleColor.Red);
        }
    }

    private async void ValorantAccounts_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            DebugConsole.WriteLine("[ValorantAccounts] Page loaded");

            await LoadDataAsync();

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
            _logger.Error(ex, "Error during ValorantAccounts_Loaded");
            DebugConsole.WriteLine($"[ValorantAccounts] ERROR during load: {ex.Message}", ConsoleColor.Red);
        }
    }

    private async void OnAccountPasswordSupplied()
    {
        try
        {
            await Dispatcher.InvokeAsync(async () => { await LoadDataAsync(); });
        }
        catch
        {
        }
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
            ValorantAccountsDataGrid.Items.SortDescriptions.Clear();
            ValorantAccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("valorantLevel", ListSortDirection.Descending));
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
                ValorantAccountsDataGrid.ItemsSource = null;
                ValorantAccountsDataGrid.ItemsSource = ActualAccountlists;

                ValorantAccountsDataGrid.Items.SortDescriptions.Clear();
                ValorantAccountsDataGrid.Items.SortDescriptions.Add(new SortDescription("valorantLevel", ListSortDirection.Descending));

                if (!Misc.Settings.settingsloaded.DisplayPasswords && ValorantAccountsDataGrid.Columns.Count > 1)
                    ValorantAccountsDataGrid.Columns[1].Visibility = Visibility.Hidden;
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading Valorant data");
        }
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(FilterTextBox.Text))
            {
                var search = FilterTextBox.Text;

                var filteredList = ActualAccountlists?
                    .Where(word =>
                        (word.valorantAgents ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantContracts ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantSprays ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantGunBuddies ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantCards ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantSkins ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantSkinVariants ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantTitles ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantRank ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.valorantServer ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (word.riotID ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                ValorantAccountsDataGrid.ItemsSource = filteredList;
            }
            else
            {
                ValorantAccountsDataGrid.ItemsSource = ActualAccountlists;
            }

            ValorantAccountsDataGrid.UpdateLayout();
            ValorantAccountsDataGrid.Items.Refresh();
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error filtering Valorant accounts");
        }
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
                        var selectedrow = ValorantAccountsDataGrid.SelectedItem as Utils.AccountList;
                        if (selectedrow == null) return;
                        if (header == null) return;
                        DisplayDataWithSearch? secondWindow = null;
                        NoteDisplay? noteWindow = null;

                        switch (header)
                        {
                            case "Agents":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantAgents);
                                break;
                            case "Contracts":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantContracts);
                                break;
                            case "Sprays":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantSprays);
                                break;
                            case "Gun Buddies":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantGunBuddies);
                                break;
                            case "Cards":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantCards);
                                break;
                            case "Skins":
                                secondWindow = new DisplayDataWithSearch(CombineSkins(selectedrow));
                                break;
                            case "Titles":
                                secondWindow = new DisplayDataWithSearch(selectedrow.valorantTitles);
                                break;
                            case "Notes":
                                noteWindow = new NoteDisplay(selectedrow);
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
                LogManager.GetCurrentClassLogger().Error(exception, "Error loading Valorant data");
            }
            finally
            {
                Executing = false;
            }
        }

        if (dataGrid != null)
        {
            dataGrid.UnselectAllCells();
            dataGrid.SelectedItem = null;
        }
    }

    private static string? CombineSkins(Utils.AccountList account)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(account.valorantSkins))
            parts.Add(account.valorantSkins);
        if (!string.IsNullOrWhiteSpace(account.valorantSkinVariants))
            parts.Add(account.valorantSkinVariants);

        return parts.Count == 0 ? null : string.Join(":", parts);
    }

    private sealed class ValorantAccountData
    {
        public List<string> Agents { get; set; } = new();
        public List<string> Contracts { get; set; } = new();
        public List<string> Sprays { get; set; } = new();
        public List<string> GunBuddies { get; set; } = new();
        public List<string> Cards { get; set; } = new();
        public List<string> Skins { get; set; } = new();
        public List<string> SkinVariants { get; set; } = new();
        public List<string> Titles { get; set; } = new();
        public int ValorantVp { get; set; }
        public int ValorantKc { get; set; }
        public int ValorantRp { get; set; }
        public string? ValorantRank { get; set; }
        public int ValorantLevel { get; set; }
        public int ValorantXp { get; set; }
    }

    private async Task<ValorantAccountData> CollectValorantAccountDataAsync(HttpClient client, string region, string puuid)
    {
        var data = new ValorantAccountData();
        var pdBase = $"https://pd.{region}.a.pvp.net";

        Dictionary<string, string> agentsMap;
        Dictionary<string, string> contractsMap;
        Dictionary<string, string> spraysMap;
        Dictionary<string, string> cardsMap;
        Dictionary<string, string> titlesMap;
        Dictionary<string, string> gunBuddyMap;
        Dictionary<string, string> skinMap;
        Dictionary<string, string> skinVariantMap;

        try
        {
            agentsMap = await GetValorantApiMapAsync("https://valorant-api.com/v1/agents?isPlayableCharacter=true",
                item => item["uuid"]?.ToString(), item => item["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Agents map: {agentsMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Agents map failed: {ex.Message}", ConsoleColor.Red);
            agentsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            contractsMap = await GetValorantApiMapAsync("https://valorant-api.com/v1/contracts",
                item => item["uuid"]?.ToString(), item => item["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Contracts map: {contractsMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Contracts map failed: {ex.Message}", ConsoleColor.Red);
            contractsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            spraysMap = await GetValorantApiMapAsync("https://valorant-api.com/v1/sprays",
                item => item["uuid"]?.ToString(), item => item["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Sprays map: {spraysMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Sprays map failed: {ex.Message}", ConsoleColor.Red);
            spraysMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            cardsMap = await GetValorantApiMapAsync("https://valorant-api.com/v1/playercards",
                item => item["uuid"]?.ToString(), item => item["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Cards map: {cardsMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Cards map failed: {ex.Message}", ConsoleColor.Red);
            cardsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            titlesMap = await GetValorantApiMapAsync("https://valorant-api.com/v1/playertitles",
                item => item["uuid"]?.ToString(), item => item["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Titles map: {titlesMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Titles map failed: {ex.Message}", ConsoleColor.Red);
            titlesMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            gunBuddyMap = await GetValorantApiNestedMapAsync("https://valorant-api.com/v1/buddies", "levels",
                item => item["uuid"]?.ToString(),
                item => item["displayName"]?.ToString(),
                parent => parent["displayName"]?.ToString());
            DebugConsole.WriteLine($"[ValorantAccounts] Gun buddies map: {gunBuddyMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Gun buddies map failed: {ex.Message}", ConsoleColor.Red);
            gunBuddyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            (skinMap, skinVariantMap) = await GetValorantWeaponSkinMapsAsync();
            DebugConsole.WriteLine($"[ValorantAccounts] Skins map: {skinMap.Count}, variants: {skinVariantMap.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Skins map failed: {ex.Message}", ConsoleColor.Red);
            skinMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            skinVariantMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            data.Agents = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/01bb38e1-da47-4e6a-9b3d-945fe4655707", agentsMap, "Agents");
            DebugConsole.WriteLine($"[ValorantAccounts] Agents entitlements: {data.Agents.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Agents entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.Contracts = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/f85cb6f7-33e5-4dc8-b609-ec7212301948", contractsMap,
                "Contracts");
            DebugConsole.WriteLine($"[ValorantAccounts] Contracts entitlements: {data.Contracts.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Contracts entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.Sprays = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/d5f120f8-ff8c-4aac-92ea-f2b5acbe9475", spraysMap,
                "Sprays");
            DebugConsole.WriteLine($"[ValorantAccounts] Sprays entitlements: {data.Sprays.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Sprays entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.GunBuddies = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/dd3bf334-87f3-40bd-b043-682a57a8dc3a", gunBuddyMap,
                "GunBuddies");
            DebugConsole.WriteLine($"[ValorantAccounts] Gun buddies entitlements: {data.GunBuddies.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Gun buddies entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.Cards = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/3f296c07-64c3-494c-923b-fe692a4fa1bd", cardsMap,
                "Cards");
            DebugConsole.WriteLine($"[ValorantAccounts] Cards entitlements: {data.Cards.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Cards entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.Skins = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/e7c63390-eda7-46e0-bb7a-a6abdacd2433", skinMap,
                "Skins");
            DebugConsole.WriteLine($"[ValorantAccounts] Skins entitlements: {data.Skins.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Skins entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.SkinVariants = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/3ad1b2b2-acdb-4524-852f-954a76ddae0a", skinVariantMap,
                "SkinVariants");
            DebugConsole.WriteLine($"[ValorantAccounts] Skin variants entitlements: {data.SkinVariants.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Skin variants entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.Titles = await GetEntitlementNamesAsync(client,
                $"{pdBase}/store/v1/entitlements/{puuid}/de7caa6b-adf7-4588-bbd1-143831e786c6", titlesMap,
                "Titles");
            DebugConsole.WriteLine($"[ValorantAccounts] Titles entitlements: {data.Titles.Count}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Titles entitlements failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            (data.ValorantVp, data.ValorantKc, data.ValorantRp) =
                await GetValorantWalletAsync(client, $"{pdBase}/store/v1/wallet/{puuid}");
            DebugConsole.WriteLine($"[ValorantAccounts] Wallet VP={data.ValorantVp}, KC={data.ValorantKc}, RP={data.ValorantRp}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Wallet fetch failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            data.ValorantRank = await GetValorantRankAsync(client, region, puuid);
            DebugConsole.WriteLine($"[ValorantAccounts] Rank: {data.ValorantRank}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Rank fetch failed: {ex.Message}", ConsoleColor.Red);
        }

        try
        {
            (data.ValorantLevel, data.ValorantXp) =
                await GetValorantLevelAsync(client, $"{pdBase}/account-xp/v1/players/{puuid}");
            DebugConsole.WriteLine($"[ValorantAccounts] Level={data.ValorantLevel}, XP={data.ValorantXp}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Level fetch failed: {ex.Message}", ConsoleColor.Red);
        }

        return data;
    }

    private static async Task<Dictionary<string, string>> GetValorantApiMapAsync(
        string url,
        Func<JToken, string?> idSelector,
        Func<JToken, string?> nameSelector)
    {
        using var apiClient = new HttpClient();
        var body = await apiClient.GetStringAsync(url).ConfigureAwait(false);
        var dataArray = JObject.Parse(body)["data"] as JArray ?? new JArray();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dataArray)
        {
            var id = idSelector(item);
            var name = nameSelector(item);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                continue;
            if (!map.ContainsKey(id))
                map[id] = name;
        }

        return map;
    }

    private static async Task<Dictionary<string, string>> GetValorantApiNestedMapAsync(
        string url,
        string nestedArrayName,
        Func<JToken, string?> idSelector,
        Func<JToken, string?> nameSelector,
        Func<JToken, string?> fallbackNameSelector)
    {
        using var apiClient = new HttpClient();
        var body = await apiClient.GetStringAsync(url).ConfigureAwait(false);
        var dataArray = JObject.Parse(body)["data"] as JArray ?? new JArray();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dataArray)
        {
            var fallbackName = fallbackNameSelector(item);
            var nested = item[nestedArrayName] as JArray;
            if (nested == null) continue;

            foreach (var nestedItem in nested)
            {
                var id = idSelector(nestedItem);
                var name = nameSelector(nestedItem) ?? fallbackName;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;
                if (!map.ContainsKey(id))
                    map[id] = name;
            }
        }

        return map;
    }

    private static async Task<(Dictionary<string, string> Skins, Dictionary<string, string> Variants)>
        GetValorantWeaponSkinMapsAsync()
    {
        using var apiClient = new HttpClient();
        var body = await apiClient.GetStringAsync("https://valorant-api.com/v1/weapons/skins").ConfigureAwait(false);
        var dataArray = JObject.Parse(body)["data"] as JArray ?? new JArray();
        var skins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var variants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dataArray)
        {
            var skinId = item["uuid"]?.ToString();
            var skinName = item["displayName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(skinId) && !string.IsNullOrWhiteSpace(skinName) && !skins.ContainsKey(skinId))
                skins[skinId] = skinName;

            var chromas = item["chromas"] as JArray;
            if (chromas == null) continue;
            foreach (var chroma in chromas)
            {
                var chromaId = chroma["uuid"]?.ToString();
                var chromaName = chroma["displayName"]?.ToString() ?? skinName;
                if (string.IsNullOrWhiteSpace(chromaId) || string.IsNullOrWhiteSpace(chromaName))
                    continue;
                if (!variants.ContainsKey(chromaId))
                    variants[chromaId] = chromaName;
            }
        }

        return (skins, variants);
    }

    private static async Task<List<string>> GetEntitlementNamesAsync(
        HttpClient client,
        string url,
        Dictionary<string, string> nameMap,
        string label)
    {
        LogRequest($"[ValorantAccounts] Entitlements request ({label})", url, client, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantAccounts] Entitlements response ({label}, {(int)response.StatusCode}): {body}");
        if (!response.IsSuccessStatusCode)
            return new List<string>();
        var json = JObject.Parse(body);
        var itemIds = new List<string>();

        var entitlements = json["Entitlements"] as JArray;
        if (entitlements != null)
        {
            foreach (var entitlement in entitlements)
            {
                var itemId = entitlement["ItemID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(itemId))
                    itemIds.Add(itemId);
            }
        }

        var entitlementsByTypes = json["EntitlementsByTypes"] as JArray;
        if (entitlementsByTypes != null)
        {
            foreach (var type in entitlementsByTypes)
            {
                var typeEntitlements = type["Entitlements"] as JArray;
                if (typeEntitlements == null) continue;
                foreach (var entitlement in typeEntitlements)
                {
                    var itemId = entitlement["ItemID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(itemId))
                        itemIds.Add(itemId);
                }
            }
        }

        var names = itemIds
            .Select(id => nameMap.TryGetValue(id, out var name) ? name : id)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names;
    }

    private static async Task<(int Vp, int Kc, int Rp)> GetValorantWalletAsync(HttpClient client, string url)
    {
        LogRequest("[ValorantAccounts] Wallet request", url, client, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantAccounts] Wallet response ({(int)response.StatusCode}): {body}");
        if (!response.IsSuccessStatusCode)
            return (0, 0, 0);

        var json = JObject.Parse(body);
        var balances = json["Balances"] as JObject;
        var vp = balances?["85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741"]?.ToObject<int>() ?? 0;
        var kc = balances?["85ca954a-41f2-ce94-9b45-8ca3dd39a00d"]?.ToObject<int>() ?? 0;
        var rp = balances?["e59aa87c-4cbf-517a-5983-6e81511be9b7"]?.ToObject<int>() ?? 0;
        return (vp, kc, rp);
    }

    private static async Task<string> GetValorantRankAsync(HttpClient client, string region, string puuid)
    {
        var url = $"https://pd.{region}.a.pvp.net/mmr/v1/players/{puuid}";
        LogRequest("[ValorantAccounts] Rank request", url, client, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantAccounts] Rank response ({(int)response.StatusCode}): {body}");
        if ((int)response.StatusCode >= 500)
            return "Unranked";
        if (!response.IsSuccessStatusCode)
            return "Unranked";

        var json = JObject.Parse(body);
        var tierAfterUpdate = json["LatestCompetitiveUpdate"]?["TierAfterUpdate"]?.ToObject<int?>() ?? 0;
        var rankedRating = json["LatestCompetitiveUpdate"]?["RankedRatingAfterUpdate"]?.ToObject<int?>() ?? 0;
        var tierMap = await GetCompetitiveTierMapAsync();
        var tierName = tierMap.TryGetValue(tierAfterUpdate, out var name) ? name : "UNRANKED";
        return rankedRating > 0 ? $"{tierName} {rankedRating} RR" : tierName;
    }

    private static async Task<Dictionary<int, string>> GetCompetitiveTierMapAsync()
    {
        using var apiClient = new HttpClient();
        var body = await apiClient.GetStringAsync("https://valorant-api.com/v1/competitivetiers")
            .ConfigureAwait(false);
        var dataArray = JObject.Parse(body)["data"] as JArray ?? new JArray();
        var latest = dataArray.LastOrDefault();
        var tiers = latest?["tiers"] as JArray ?? new JArray();
        var map = new Dictionary<int, string>();
        foreach (var tier in tiers)
        {
            var id = tier["tier"]?.ToObject<int?>();
            var name = tier["tierName"]?.ToString();
            if (id == null || string.IsNullOrWhiteSpace(name))
                continue;
            if (!map.ContainsKey(id.Value))
                map[id.Value] = name;
        }

        return map;
    }

    private static async Task<(int Level, int Xp)> GetValorantLevelAsync(HttpClient client, string url)
    {
        LogRequest("[ValorantAccounts] Level request", url, client, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantAccounts] Level response ({(int)response.StatusCode}): {body}");
        if (!response.IsSuccessStatusCode)
            return (0, 0);

        var json = JObject.Parse(body);
        var level = json["Progress"]?["Level"]?.ToObject<int?>() ?? 0;
        var xp = json["Progress"]?["XP"]?.ToObject<int?>() ?? 0;
        return (level, xp);
    }

    private static void LogRequest(string prefix, string url, HttpClient client, string? body)
    {
        var headers = string.Join("; ", client.DefaultRequestHeaders.Select(h =>
            $"{h.Key}={string.Join(",", h.Value)}"));
        DebugConsole.WriteLine($"{prefix} URL: {url}");
        DebugConsole.WriteLine($"{prefix} Headers: {headers}");
        if (body != null)
            DebugConsole.WriteLine($"{prefix} Body: {body}");
    }

    private static string? GetUnameFromIdToken(string idToken)
    {
        var payloadJson = DecodeJwtPayload(idToken);
        return payloadJson?["lol"]?.FirstOrDefault()?["uname"]?.ToString();
    }

    private static string? PromptForPassword(string message)
    {
        string? password = null;

        void ShowPrompt()
        {
            var prompt = new PasswordPrompt(message);
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
                prompt.Owner = owner;

            var result = prompt.ShowDialog();
            if (result == true)
                password = prompt.Password;
        }

        if (System.Windows.Application.Current?.Dispatcher != null)
            System.Windows.Application.Current.Dispatcher.Invoke(ShowPrompt);
        else
            ShowPrompt();

        return password;
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
            DebugConsole.WriteLine($"[ValorantAccounts] Switched selected account to {match.username} from id token.");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ValorantAccounts] Failed to decode id token: {ex.Message}", ConsoleColor.Red);
        }
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
}

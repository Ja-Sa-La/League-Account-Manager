using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class RuneManager : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly List<ComboBox> primarySelectors = [];
    private readonly List<ComboBox> secondarySelectors = [];
    private readonly List<ComboBox> shardSelectors = [];
    private bool suppressSelectionEvents;
    private RunePage workingPage = RuneCatalog.DefaultPage();

    public RuneManager()
    {
        InitializeComponent();
        PrimaryStyle.ItemsSource = RuneCatalog.Styles;
        SecondaryStyle.ItemsSource = RuneCatalog.Styles;
        ImportSource.ItemsSource = RunePageService.Sources;
        ImportRole.ItemsSource = RunePageService.Roles;
        ImportSource.SelectedValue = Normalize(Misc.Settings.settingsloaded.RuneImportSource, "ugg");
        ImportRole.SelectedValue = Normalize(Misc.Settings.settingsloaded.RuneImportRole, "automatic");
        AutoImport.IsChecked = Misc.Settings.settingsloaded.AutoImportRunes;
        BuildShardSelectors();
        Loaded += async (_, _) => await RefreshCurrentAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshCurrentAsync();

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var page = ReadPage();
            await RunePageService.ApplyAsync(page);
            workingPage = page;
            SetStatus($"Applied {page.Name}.");
            Notify("Runes applied", page.Describe(), NotificationType.Success);
        }
        catch (Exception exception)
        {
            Report("The rune page could not be applied.", exception);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var champion = ChampionName.Text.Trim();
            if (string.IsNullOrWhiteSpace(champion))
            {
                SetStatus("Enter a champion name to import runes.");
                return;
            }

            SetStatus($"Importing {champion} runes...");
            var key = await RunePageService.ResolveChampionKeyAsync(champion);
            await ImportChampionAsync(key);
        }
        catch (Exception exception)
        {
            Report("Runes could not be imported.", exception);
        }
    }

    private async void OnImportSelectedChampionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var championId = await SelectedChampionAsync();
            if (championId == 0)
            {
                SetStatus("Select a champion in champion select first.");
                return;
            }

            await ImportChampionAsync(championId);
        }
        catch (Exception exception)
        {
            Report("The selected champion's runes could not be imported.", exception);
        }
    }

    private void OnAutoImportChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SaveImportSettings();
        SetStatus(AutoImport.IsChecked == true
            ? "Automatic import is enabled. Runes are imported once after each champion selection."
            : "Automatic import is disabled.");
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionEvents || !IsLoaded)
            return;

        RebuildStyleSelectors(preserveSelection: true);
    }

    private async Task RefreshCurrentAsync()
    {
        try
        {
            SetStatus("Loading the current rune page...");
            var page = await RunePageService.LoadCurrentAsync();
            ShowPage(page);
            CurrentPageSummary.Text = $"Currently applied: {page.Name} — {page.Describe()}";
            SetStatus("Showing the rune page currently applied in the League client.");
        }
        catch (Exception exception)
        {
            ShowPage(workingPage);
            CurrentPageSummary.Text = "League client is not available. Showing an editable rune page.";
            SetStatus(exception.Message);
            Logger.Info(exception, "Current rune page could not be loaded");
        }
    }

    private async Task ImportChampionAsync(int championId)
    {
        SaveImportSettings();
        var source = Selected(ImportSource, "ugg");
        var role = Selected(ImportRole, "automatic");
        var page = await RunePageService.ImportAsync(source, championId, role);
        ShowPage(page);
        SetStatus($"Imported {page.Name} from {SourceLabel(source)}. Apply it or enable automatic import.");
    }

    private void ShowPage(RunePage page)
    {
        workingPage = page.Clone();
        suppressSelectionEvents = true;
        PageName.Text = page.Name;
        PrimaryStyle.SelectedItem = RuneCatalog.FindStyle(page.PrimaryStyleId) ?? RuneCatalog.Styles[0];
        SecondaryStyle.SelectedItem = RuneCatalog.FindStyle(page.SubStyleId) ?? RuneCatalog.Styles[1];
        RebuildStyleSelectors(preserveSelection: false);
        SelectValues(primarySelectors, page.PerkIds);
        SelectValues(secondarySelectors, page.SecondaryPerkIds);
        SelectValues(shardSelectors, page.StatShardIds);
        suppressSelectionEvents = false;
    }

    private void RebuildStyleSelectors(bool preserveSelection)
    {
        var primary = SelectedStyle(PrimaryStyle, 0);
        var secondary = SelectedStyle(SecondaryStyle, primary.Id == RuneCatalog.Styles[0].Id ? 1 : 0);
        if (secondary.Id == primary.Id)
        {
            secondary = RuneCatalog.Styles.First(style => style.Id != primary.Id);
            suppressSelectionEvents = true;
            SecondaryStyle.SelectedItem = secondary;
            suppressSelectionEvents = false;
        }

        var savedPrimary = preserveSelection ? SelectedIds(primarySelectors) : workingPage.PerkIds;
        var savedSecondary = preserveSelection ? SelectedIds(secondarySelectors) : workingPage.SecondaryPerkIds;
        BuildSelectors(PrimarySlots, primarySelectors, primary.Slots, savedPrimary, singleChoice: false);
        BuildSecondarySelectors(secondary, savedSecondary);
    }

    private void BuildShardSelectors()
    {
        ShardSlots.Children.Clear();
        shardSelectors.Clear();
        for (var index = 0; index < RuneCatalog.ShardRows.Length; index++)
        {
            var row = RuneCatalog.ShardRows[index];
            var selector = CreateSelector($"{row.Name} shard",
                row.Shards.Select(shard => new RunePerk(shard.Id, shard.Name)).ToArray(),
                workingPage.StatShardIds[index]);
            shardSelectors.Add(selector);
        }
    }

    private void BuildSecondarySelectors(RuneStyle style, IReadOnlyList<int> selected)
    {
        SecondarySlots.Children.Clear();
        secondarySelectors.Clear();
        var usedRows = new HashSet<int>();
        foreach (var perkId in selected)
        {
            var row = Array.FindIndex(style.Slots, 1, slot => slot.Any(perk => perk.Id == perkId));
            if (row > 0)
                usedRows.Add(row);
        }

        for (var index = 1; index < style.Slots.Length && secondarySelectors.Count < 2; index++)
        {
            var selectedId = selected.FirstOrDefault(id => style.Slots[index].Any(perk => perk.Id == id));
            if (selectedId == 0 && usedRows.Contains(index))
                continue;
            if (selectedId == 0 && usedRows.Count >= 2)
                continue;

            usedRows.Add(index);
            var selector = CreateSelector($"Row {index + 1}", style.Slots[index], selectedId, SecondarySlots);
            secondarySelectors.Add(selector);
        }
    }

    private void BuildSelectors(Panel panel, List<ComboBox> selectors, RunePerk[][] slots, IReadOnlyList<int> selected,
        bool singleChoice)
    {
        panel.Children.Clear();
        selectors.Clear();
        for (var index = 0; index < slots.Length; index++)
        {
            var selectedId = index < selected.Count ? selected[index] : 0;
            selectors.Add(CreateSelector($"Row {index + 1}", slots[index], selectedId, panel));
            if (singleChoice)
                break;
        }
    }

    private ComboBox CreateSelector(string label, IReadOnlyList<RunePerk> perks, int selectedId, Panel? panel = null)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        container.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 5),
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
        });
        var selector = new ComboBox { DisplayMemberPath = nameof(RunePerk.Name), ItemsSource = perks };
        selector.SelectedItem = perks.FirstOrDefault(perk => perk.Id == selectedId) ?? perks.FirstOrDefault();
        container.Children.Add(selector);
        (panel ?? ShardSlots).Children.Add(container);
        return selector;
    }

    private RunePage ReadPage()
    {
        var primary = SelectedStyle(PrimaryStyle, 0);
        var secondary = SelectedStyle(SecondaryStyle, 1);
        return new RunePage
        {
            Name = string.IsNullOrWhiteSpace(PageName.Text) ? "League Account Manager" : PageName.Text.Trim(),
            PrimaryStyleId = primary.Id,
            SubStyleId = secondary.Id,
            PerkIds = SelectedIds(primarySelectors),
            SecondaryPerkIds = SelectedIds(secondarySelectors),
            StatShardIds = SelectedIds(shardSelectors)
        };
    }

    private static async Task<int> SelectedChampionAsync()
    {
        var result = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
        if (result is not HttpResponseMessage response || !response.IsSuccessStatusCode)
            return 0;

        using (response)
        {
            var session = JObject.Parse(await response.Content.ReadAsStringAsync());
            var localCell = session["localPlayerCellId"]?.Value<int>() ?? -1;
            var player = (session["myTeam"] as JArray)?.FirstOrDefault(member =>
                member?["cellId"]?.Value<int>() == localCell);
            return player?["championId"]?.Value<int>() ?? 0;
        }
    }

    private void SaveImportSettings()
    {
        var saved = Misc.Settings.settingsloaded;
        saved.AutoImportRunes = AutoImport.IsChecked == true;
        saved.RuneImportSource = Selected(ImportSource, "ugg");
        saved.RuneImportRole = Selected(ImportRole, "automatic");
        Misc.Settings.settingsloaded = saved;
        Misc.Settings.Save();
    }

    private static RuneStyle SelectedStyle(ComboBox selector, int fallback) =>
        selector.SelectedItem as RuneStyle ?? RuneCatalog.Styles[fallback];

    private static int[] SelectedIds(IEnumerable<ComboBox> selectors) =>
        selectors.Select(selector => (selector.SelectedItem as RunePerk)?.Id ?? 0).ToArray();

    private static void SelectValues(IReadOnlyList<ComboBox> selectors, IReadOnlyList<int> values)
    {
        for (var index = 0; index < selectors.Count && index < values.Count; index++)
        {
            var perks = (IEnumerable<RunePerk>)selectors[index].ItemsSource;
            selectors[index].SelectedItem = perks.FirstOrDefault(perk => perk.Id == values[index]) ??
                                            perks.FirstOrDefault();
        }
    }

    private static string Selected(ComboBox selector, string fallback) =>
        selector.SelectedValue as string ?? fallback;

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string SourceLabel(string source) =>
        RunePageService.Sources.FirstOrDefault(item => item.Id == source) is { Label: var label } &&
        !string.IsNullOrEmpty(label)
            ? label
            : source;

    private void SetStatus(string message) => StatusText.Text = message;

    private void Report(string message, Exception exception)
    {
        Logger.Error(exception, message);
        DebugConsole.WriteLine($"[Runes] {message} {exception.Message}");
        SetStatus($"{message} {exception.Message}");
        Notify("Runes", $"{message} {exception.Message}", NotificationType.Error);
    }

    private static void Notify(string title, string message, NotificationType type) =>
        Notif.notificationManager.Show(title, message, type);
}

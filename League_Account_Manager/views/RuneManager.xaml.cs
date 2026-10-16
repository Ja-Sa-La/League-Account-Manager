using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class RuneManager : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly List<ListBox> primarySelectors = [];
    private readonly List<ListBox> secondarySelectors = [];
    private readonly List<ListBox> shardSelectors = [];
    private bool suppressSelectionEvents;
    private RunePage workingPage = RuneCatalog.DefaultPage();

    public RuneManager()
    {
        InitializeComponent();
        PrimaryStyle.ItemTemplate = CreateRuneTemplate();
        SecondaryStyle.ItemTemplate = CreateRuneTemplate();
        PrimaryStyle.ItemsPanel = CreateWrapPanel();
        SecondaryStyle.ItemsPanel = CreateWrapPanel();
        AttachDetails(PrimaryStyle, PrimaryStyleName, PrimaryStyleDescription);
        AttachDetails(SecondaryStyle, SecondaryStyleName, SecondaryStyleDescription);
        PrimaryStyle.ItemsSource = RuneCatalog.Styles;
        SecondaryStyle.ItemsSource = RuneCatalog.Styles;
        ImportSource.ItemsSource = RunePageService.Sources;
        ImportRole.ItemsSource = RunePageService.Roles;
        ImportSource.SelectedValue = Normalize(Misc.Settings.settingsloaded.RuneImportSource, "ugg");
        ImportRole.SelectedValue = Normalize(Misc.Settings.settingsloaded.RuneImportRole, "automatic");
        AutoImport.IsChecked = Misc.Settings.settingsloaded.AutoImportRunes;
        BuildShardSelectors();
        Loaded += async (_, _) =>
        {
            try
            {
                await LoadChampionsAsync();
                await RuneIcons.EnsureLoadedAsync();
                RuneCatalog.RefreshIcons();
                RefreshDetails();
                await RefreshCurrentAsync();
            }
            catch (Exception exception)
            {
                Report("The rune editor could not be loaded.", exception);
            }
        };
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
                SetStatus("Select a champion to import runes.");
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

    private async Task LoadChampionsAsync()
    {
        try
        {
            ChampionName.ItemsSource = await RunePageService.ChampionNamesAsync();
        }
        catch (Exception exception)
        {
            Logger.Info(exception, "Champion list could not be loaded");
        }
    }

    private async Task RefreshCurrentAsync()
    {
        try
        {
            SetStatus("Loading the current rune page...");
            var page = await RunePageService.LoadCurrentAsync();
            ShowPage(page);
            SetStatus($"Showing the rune page currently applied: {page.Name}.");
        }
        catch (Exception exception)
        {
            ShowPage(workingPage);
            SetStatus("League client is not available. Showing an editable rune page.");
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
        RefreshDetails();
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
        BuildSelectors(PrimarySlots, primarySelectors, primary.SlotNames, primary.Slots, savedPrimary);
        BuildSecondarySelectors(secondary, savedSecondary);
    }

    private void BuildShardSelectors()
    {
        ShardSlots.Children.Clear();
        shardSelectors.Clear();
        for (var index = 0; index < RuneCatalog.ShardRows.Length; index++)
        {
            var row = RuneCatalog.ShardRows[index];
            var selector = CreateSelector(row.Name, row.Shards, workingPage.StatShardIds[index], iconSize: 22,
                descriptionLines: 2);
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
            var selector = CreateSelector(style.SlotNames[index], style.Slots[index], selectedId, SecondarySlots,
                iconSize: 24);
            secondarySelectors.Add(selector);
        }
    }

    private void BuildSelectors(Panel panel, List<ListBox> selectors, IReadOnlyList<string> names, RunePerk[][] slots,
        IReadOnlyList<int> selected)
    {
        panel.Children.Clear();
        selectors.Clear();
        for (var index = 0; index < slots.Length; index++)
        {
            var selectedId = index < selected.Count ? selected[index] : 0;
            var name = index < names.Count ? names[index] : $"Row {index + 1}";
            selectors.Add(CreateSelector(name, slots[index], selectedId, panel, iconSize: 26));
        }
    }

    private ListBox CreateSelector(string label, IReadOnlyList<RuneVisual> perks, int selectedId, Panel? panel = null,
        double iconSize = 32, int descriptionLines = 5)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        container.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 1),
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        var selector = new ListBox
        {
            ItemsSource = perks,
            ItemTemplate = CreateRuneTemplate(iconSize),
            Style = (Style)FindResource("RuneChoiceListStyle"),
            ItemContainerStyle = (Style)FindResource("RuneChoiceItemStyle"),
            ItemsPanel = CreateWrapPanel()
        };
        selector.SelectedItem = perks.FirstOrDefault(perk => perk.Id == selectedId) ?? perks.FirstOrDefault();
        var nameText = new TextBlock
        {
            Height = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var descriptionHeight = descriptionLines * 16;
        var descriptionText = new TextBlock
        {
            Height = descriptionHeight,
            FontSize = 12,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap
        };
        AttachDetails(selector, nameText, descriptionText);
        var details = new StackPanel
        {
            Height = 20 + descriptionHeight,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { nameText, descriptionText }
        };
        var row = new Grid { Height = Math.Max(iconSize + 8, 24 + descriptionHeight) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(selector);
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        container.Children.Add(row);
        (panel ?? ShardSlots).Children.Add(container);
        return selector;
    }

    private void AttachDetails(ListBox selector, TextBlock name, TextBlock description)
    {
        ShowDetails(name, description, selector.SelectedItem as RuneVisual);
        selector.SelectionChanged += (_, _) => ShowDetails(name, description, selector.SelectedItem as RuneVisual);
        selector.PreviewMouseMove += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(selector, source) is ListBoxItem { DataContext: RuneVisual rune })
                ShowDetails(name, description, rune);
        };
        selector.MouseLeave += (_, _) => ShowDetails(name, description, selector.SelectedItem as RuneVisual);
    }

    private void RefreshDetails()
    {
        ShowDetails(PrimaryStyleName, PrimaryStyleDescription, PrimaryStyle.SelectedItem as RuneVisual);
        ShowDetails(SecondaryStyleName, SecondaryStyleDescription, SecondaryStyle.SelectedItem as RuneVisual);
        foreach (var selector in primarySelectors.Concat(secondarySelectors).Concat(shardSelectors))
        {
            if (selector.Parent is not Grid { Children: var children })
                continue;
            var details = children.OfType<StackPanel>().FirstOrDefault();
            var texts = details?.Children.OfType<TextBlock>().ToArray();
            if (texts is { Length: 2 })
                ShowDetails(texts[0], texts[1], selector.SelectedItem as RuneVisual);
        }
    }

    private static void ShowDetails(TextBlock name, TextBlock description, RuneVisual? rune)
    {
        name.Text = rune?.Name ?? string.Empty;
        description.Text = rune == null
            ? string.Empty
            : string.IsNullOrWhiteSpace(rune.Description) ? "No description is available yet." : rune.Description;
    }

    private static ItemsPanelTemplate CreateWrapPanel()
    {
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        return new ItemsPanelTemplate(panel);
    }

    private static DataTemplate CreateRuneTemplate(double iconSize = 34)
    {
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(RuneVisual.Icon)));
        image.SetValue(FrameworkElement.WidthProperty, iconSize);
        image.SetValue(FrameworkElement.HeightProperty, iconSize);
        image.SetValue(Image.StretchProperty, Stretch.Uniform);

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.AppendChild(image);

        return new DataTemplate { VisualTree = panel };
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

    private static RuneStyle SelectedStyle(ListBox selector, int fallback) =>
        selector.SelectedItem as RuneStyle ?? RuneCatalog.Styles[fallback];

    private static int[] SelectedIds(IEnumerable<ListBox> selectors) =>
        selectors.Select(selector => (selector.SelectedItem as RuneVisual)?.Id ?? 0).ToArray();

    private static void SelectValues(IReadOnlyList<ListBox> selectors, IReadOnlyList<int> values)
    {
        for (var index = 0; index < selectors.Count && index < values.Count; index++)
        {
            var perks = (IEnumerable<RuneVisual>)selectors[index].ItemsSource;
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

using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using League_Account_Manager.Windows;
using League_Account_Manager.Misc;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class ValorantLoadout : Page
{
    private static readonly HttpClient ImageClient = new();
    private static readonly ConcurrentDictionary<string, BitmapImage?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WeaponRow> rows = [];
    private readonly List<AgentChoice> agents = [];
    private bool suppressChanges;
    private ValorantLoadoutService.ValorantSession? session;
    private ValorantLoadoutService.PlayerLoadout? loadout;
    private ValorantLoadoutService.WeaponCatalog? catalog;
    private WeaponRow? editingWeapon;

    public ValorantLoadout()
    {
        InitializeComponent();
        AutoApply.IsChecked = Misc.Settings.settingsloaded.AutoApplyValorantLoadout;
        Loaded += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => CloseSession();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnSaveForAgentClick(object sender, RoutedEventArgs e)
    {
        if (session == null || loadout == null || catalog == null)
        {
            SetStatus("Refresh the loadout before saving it for an agent.");
            return;
        }

        try
        {
            var selected = AgentList.SelectedItem as AgentChoice;
            var agentId = selected?.Id;
            if (string.IsNullOrWhiteSpace(agentId))
                agentId = await ValorantLoadoutService.SelectedAgentIdAsync(session);
            if (string.IsNullOrWhiteSpace(agentId))
            {
                SetStatus("Choose an agent above, or select one in Valorant first.");
                return;
            }

            await ValorantLoadoutService.SaveAgentLoadoutAsync(session.Puuid, agentId, DisplayedGuns());
            var agentName = catalog.AgentName(agentId);
            SelectAgent(agentId);
            AgentText.Text = $"Saved the current skins for {agentName}.";
            SetStatus($"Saved the current loadout for {agentName}.");
            Notify("Loadout saved", $"These skins will be equipped when you select {agentName}.",
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Report("The loadout could not be saved for the selected agent.", exception);
        }
    }

    private async void OnResetAgentClick(object sender, RoutedEventArgs e)
    {
        if (session == null || catalog == null || AgentList.SelectedItem is not AgentChoice agent ||
            string.IsNullOrWhiteSpace(agent.Id))
        {
            SetStatus("Choose an agent before resetting its loadout.");
            return;
        }

        try
        {
            await ValorantLoadoutService.DeleteAgentLoadoutAsync(session.Puuid, agent.Id);
            ShowSavedGuns(catalog.StandardGuns());
            AgentText.Text = $"{agent.Name} uses the standard skins until a loadout is saved.";
            SetStatus($"Reset the loadout for {agent.Name}.");
            Notify("Loadout reset", $"{agent.Name} will use the standard skins.", NotificationType.Success);
        }
        catch (Exception exception)
        {
            Report($"Could not reset the loadout for {agent.Name}.", exception);
        }
    }

    private async void OnSaveSetClick(object sender, RoutedEventArgs e)
    {
        if (session == null || loadout == null)
        {
            SetStatus("Refresh the loadout before saving a set.");
            return;
        }

        var prompt = new TextPrompt("Save loadout set", "Name this loadout set.", "My set");
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Value))
            return;
        var name = prompt.Value;

        try
        {
            await ValorantLoadoutService.SaveNamedLoadoutAsync(session.Puuid, name.Trim(), DisplayedGuns());
            await LoadSetsAsync(name.Trim());
            AgentText.Text = $"Saved the set {name.Trim()}. Apply equips it.";
            SetStatus($"Saved the set {name.Trim()}.");
        }
        catch (Exception exception)
        {
            Report("The loadout set could not be saved.", exception);
        }
    }

    private async void OnUpdateSetClick(object sender, RoutedEventArgs e)
    {
        if (session == null || loadout == null || SetList.SelectedItem is not ValorantLoadoutService.NamedLoadout set)
        {
            SetStatus("Choose a set before updating it.");
            return;
        }

        try
        {
            await ValorantLoadoutService.SaveNamedLoadoutAsync(session.Puuid, set.Name, DisplayedGuns());
            await LoadSetsAsync(set.Name);
            AgentText.Text = $"Updated the set {set.Name}. Nothing was equipped.";
            SetStatus($"Updated the set {set.Name}.");
        }
        catch (Exception exception)
        {
            Report($"Could not update {set.Name}.", exception);
        }
    }

    private async void OnRemoveSetClick(object sender, RoutedEventArgs e)
    {
        if (session == null || SetList.SelectedItem is not ValorantLoadoutService.NamedLoadout set)
        {
            SetStatus("Choose a set before removing it.");
            return;
        }

        if (AppMessageBox.Show($"Remove the set {set.Name}?", "Remove set",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await ValorantLoadoutService.DeleteNamedLoadoutAsync(session.Puuid, set.Name);
            await LoadSetsAsync(null);
            AgentText.Text = $"Removed the set {set.Name}.";
            SetStatus($"Removed the set {set.Name}.");
        }
        catch (Exception exception)
        {
            Report($"Could not remove {set.Name}.", exception);
        }
    }

    private async void OnSetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressChanges || SetList.SelectedItem is not ValorantLoadoutService.NamedLoadout set)
            return;

        ShowSavedGuns(set.Guns);
        AgentText.Text = $"Showing the set {set.Name}. Nothing was equipped.";
        SetStatus($"Loaded the set {set.Name}.");
        await Task.CompletedTask;
    }

    private async void OnApplySetClick(object sender, RoutedEventArgs e)
    {
        if (session == null || SetList.SelectedItem is not ValorantLoadoutService.NamedLoadout set)
        {
            SetStatus("Choose a set before applying it.");
            return;
        }

        try
        {
            SetStatus($"Applying {set.Name}...");
            var applied = await ValorantLoadoutService.ApplyNamedLoadoutAsync(session, set);
            if (applied)
                loadout = await ValorantLoadoutService.GetLoadoutAsync(session);
            ShowSavedGuns(set.Guns);
            AgentText.Text = applied
                ? $"Applied the set {set.Name}."
                : $"{set.Name} was already equipped.";
            SetStatus(applied ? $"Applied the set {set.Name}." : $"{set.Name} was already equipped.");
            Notify(applied ? "Loadout applied" : "Loadout unchanged",
                applied ? $"{set.Name} is now equipped." : "Valorant already had these skins.",
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Report($"Could not apply {set.Name}.", exception);
        }
    }

    private async void OnAgentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressChanges || session == null || AgentList.SelectedItem is not AgentChoice agent)
            return;

        if (string.IsNullOrWhiteSpace(agent.Id))
        {
            ShowEquippedLoadout();
            AgentText.Text = "Showing the skins currently equipped. Choose an agent to view a saved loadout.";
            return;
        }

        try
        {
            var saved = await ValorantLoadoutService.FindAgentLoadoutAsync(session.Puuid, agent.Id);
            if (saved == null)
            {
                ShowSavedGuns(catalog?.StandardGuns() ?? []);
                AgentText.Text = $"{agent.Name} uses the standard skins until a loadout is saved.";
                SetStatus($"{agent.Name} uses the standard skins.");
                return;
            }

            ShowSavedGuns(saved.Guns);
            AgentText.Text = $"Showing the saved loadout for {agent.Name}. Nothing was equipped.";
            SetStatus($"Loaded the saved loadout for {agent.Name}.");
        }
        catch (Exception exception)
        {
            Report($"The saved loadout for {agent.Name} could not be loaded.", exception);
        }
    }

    private void OnWeaponClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WeaponRow row })
            return;

        editingWeapon = row;
        SkinPopupTitle.Text = row.WeaponName;
        SkinGrid.ItemsSource = row.Options;
        SkinPopup.IsOpen = true;
    }

    private void OnCloseSkinsClick(object sender, RoutedEventArgs e) => SkinPopup.IsOpen = false;

    private void OnSkinCardClick(object sender, RoutedEventArgs e)
    {
        if (editingWeapon == null || sender is not Button { Tag: SkinChoice option })
        {
            SkinPopup.IsOpen = false;
            return;
        }

        editingWeapon.SelectedOption = option;
        SkinPopup.IsOpen = false;
        SetStatus($"Selected {option.Name}. Apply changes equips the skins shown.");
    }

    private async void OnApplyChangesClick(object sender, RoutedEventArgs e)
    {
        if (session == null || loadout == null)
        {
            SetStatus("Refresh the loadout before applying changes.");
            return;
        }

        try
        {
            SetStatus("Applying the skins shown...");
            var applied = await ValorantLoadoutService.ApplyGunsAsync(session, DisplayedGuns());
            if (applied)
                loadout = await ValorantLoadoutService.GetLoadoutAsync(session);
            ShowEquippedLoadout();
            AgentText.Text = applied
                ? "Applied the skins shown."
                : "Those skins were already equipped.";
            SetStatus(applied ? "Applied the skins shown." : "Nothing changed.");
            Notify(applied ? "Loadout applied" : "Loadout unchanged",
                applied ? "The skins shown are now equipped." : "Valorant already had these skins.",
                NotificationType.Success);
        }
        catch (Exception exception)
        {
            Report("The skins shown could not be equipped.", exception);
        }
    }

    private void OnAutoApplyChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        Misc.Settings.settingsloaded.AutoApplyValorantLoadout = AutoApply.IsChecked == true;
        Misc.Settings.Save();
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetStatus("Reading the Valorant loadout...");
            session = null;
            loadout = null;
            catalog = await ValorantLoadoutService.GetCatalogAsync();
            session = await ValorantLoadoutService.OpenSessionAsync();
            loadout = await ValorantLoadoutService.GetLoadoutAsync(session);
            var owned = await ValorantLoadoutService.GetOwnedSkinIdsAsync(session);
            var choices = loadout.Guns
                .Select(gun => (Gun: gun, Weapon: catalog.Weapon(gun.WeaponId)))
                .Where(item => item.Weapon != null)
                .Select(item => (
                    item.Gun,
                    Weapon: item.Weapon!,
                    Options: catalog.OwnedOptions(item.Gun.WeaponId, owned).ToList()))
                .ToList();
            var imageUrls = choices.SelectMany(item => item.Options)
                .SelectMany(option => new[] { option.IconUrl, option.ChromaIconUrl })
                .Concat(catalog.Agents.SelectMany(agent => new[] { agent.PortraitUrl, agent.IconUrl }))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(imageUrls.Select(LoadImageAsync));

            suppressChanges = true;
            rows.Clear();
            foreach (var (gun, weapon, options) in choices)
            {
                var skinChoices = options.Select(option => new SkinChoice(option)).ToList();
                var selected = skinChoices.FirstOrDefault(option =>
                                   string.Equals(option.ChromaId, gun.ChromaId, StringComparison.OrdinalIgnoreCase)) ??
                               skinChoices.FirstOrDefault(option =>
                                   string.Equals(option.SkinId, gun.SkinId, StringComparison.OrdinalIgnoreCase));
                rows.Add(new WeaponRow(weapon.Id, weapon.Name, weapon.Category, gun.ChromaId, skinChoices, selected));
            }

            ColumnList.ItemsSource = null;
            ColumnList.ItemsSource = CollectionColumn.Group(rows);
            agents.Clear();
            agents.Add(AgentChoice.Random());
            agents.AddRange(catalog.Agents.Select(agent => new AgentChoice(agent)));
            AgentList.ItemsSource = null;
            AgentList.ItemsSource = agents;
            await LoadSetsAsync(null);
            AccountText.Text = $"Signed in · {session.Region.ToUpperInvariant()}";
            var agentId = await ValorantLoadoutService.SelectedAgentIdAsync(session);
            SelectAgent(agentId);
            if (!string.IsNullOrWhiteSpace(agentId))
                await ShowAgentLoadoutAsync(agentId);
            else
                AgentText.Text = "Choose an agent to view a saved loadout. It is not equipped until agent select.";
            SetStatus($"Loaded {rows.Count} weapons.");
        }
        catch (Exception exception)
        {
            AccountText.Text = "Valorant is not signed in";
            ColumnList.ItemsSource = null;
            Report("The Valorant loadout could not be loaded. Sign in with the Riot Client and open Valorant.",
                exception);
        }
        finally
        {
            suppressChanges = false;
        }
    }
    private IReadOnlyList<ValorantLoadoutService.EquippedGun> DisplayedGuns() => rows.Select(row =>
    {
        var skin = row.SelectedOption?.Source;
        var equipped = loadout?.Guns.FirstOrDefault(gun =>
            string.Equals(gun.WeaponId, row.WeaponId, StringComparison.OrdinalIgnoreCase));
        return skin == null
            ? equipped ?? new ValorantLoadoutService.EquippedGun(
                row.WeaponId, string.Empty, string.Empty, string.Empty, null, null, null)
            : new ValorantLoadoutService.EquippedGun(
                row.WeaponId, skin.SkinId, skin.LevelId, skin.ChromaId,
                equipped?.CharmId, equipped?.CharmLevelId, equipped?.CharmInstanceId);
    }).ToList();

    private async Task LoadSetsAsync(string? selectedName)
    {
        if (session == null)
            return;

        var sets = (await ValorantLoadoutService.GetNamedLoadoutsAsync(session.Puuid))
            .Where(set => !set.Name.Equals(ValorantLoadoutService.DefaultSetName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        suppressChanges = true;
        SetList.ItemsSource = sets;
        SetList.SelectedItem = sets.FirstOrDefault(set =>
            string.Equals(set.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        suppressChanges = false;
    }

    private async Task ShowAgentLoadoutAsync(string agentId)
    {
        if (session == null)
            return;

        var saved = await ValorantLoadoutService.FindAgentLoadoutAsync(session.Puuid, agentId);
        var name = catalog?.AgentName(agentId) ?? "this agent";
        if (saved == null)
        {
            ShowSavedGuns(catalog?.StandardGuns() ?? []);
            AgentText.Text = $"{name} uses the standard skins until a loadout is saved.";
            return;
        }

        ShowSavedGuns(saved.Guns);
        AgentText.Text = $"Showing the saved loadout for {name}. Nothing was equipped.";
    }

    private void ShowEquippedLoadout()
    {
        if (loadout == null)
            return;

        suppressChanges = true;
        foreach (var row in rows)
        {
            var gun = loadout.Guns.FirstOrDefault(item =>
                string.Equals(item.WeaponId, row.WeaponId, StringComparison.OrdinalIgnoreCase));
            row.EquippedChromaId = gun?.ChromaId ?? string.Empty;
            row.SelectedOption = MatchOption(row, gun?.ChromaId, gun?.SkinId);
        }

        suppressChanges = false;
    }

    private void ShowSavedGuns(IEnumerable<ValorantLoadoutService.SavedGun> guns)
    {
        var saved = guns.ToList();
        suppressChanges = true;
        foreach (var row in rows)
        {
            var gun = saved.FirstOrDefault(item =>
                string.Equals(item.WeaponId, row.WeaponId, StringComparison.OrdinalIgnoreCase));
            var option = MatchOption(row, gun?.ChromaId, gun?.SkinId);
            if (option == null)
                continue;

            row.EquippedChromaId = option.ChromaId;
            row.SelectedOption = option;
        }

        suppressChanges = false;
    }

    private static SkinChoice? MatchOption(WeaponRow row, string? chromaId, string? skinId) =>
        row.Options.FirstOrDefault(option =>
            string.Equals(option.ChromaId, chromaId, StringComparison.OrdinalIgnoreCase)) ??
        row.Options.FirstOrDefault(option =>
            string.Equals(option.SkinId, skinId, StringComparison.OrdinalIgnoreCase));

    private void SelectAgent(string? agentId)
    {
        suppressChanges = true;
        AgentList.SelectedItem = string.IsNullOrWhiteSpace(agentId)
            ? null
            : agents.FirstOrDefault(agent => string.Equals(agent.Id, agentId, StringComparison.OrdinalIgnoreCase));
        if (AgentList.SelectedItem is AgentChoice selected)
            AgentList.ScrollIntoView(selected);
        suppressChanges = false;
    }

    private void CloseSession()
    {
        session = null;
        loadout = null;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void Report(string message, Exception exception)
    {
        DebugConsole.WriteLine($"[ValorantLoadout] {message} {exception.Message}", ConsoleColor.Red);
        SetStatus(message);
        Notify("Valorant loadout", message, NotificationType.Error);
    }

    private static void Notify(string title, string message, NotificationType type) =>
        Notif.notificationManager.Show(title, message, type);

    private sealed class WeaponRow : INotifyPropertyChanged
    {
        private SkinChoice? selectedOption;

        public WeaponRow(string weaponId, string weaponName, string category, string chromaId,
            IReadOnlyList<SkinChoice> options, SkinChoice? selected)
        {
            WeaponId = weaponId;
            WeaponName = weaponName;
            Category = category;
            EquippedChromaId = chromaId;
            Options = options;
            selectedOption = selected;
        }

        public string WeaponId { get; }
        public string WeaponName { get; }
        public string Category { get; }
        public BitmapImage? PreviewIcon => SelectedOption?.Icon;
        public BitmapImage? ChromaIcon => SelectedOption?.ChromaSwatch;
        public Visibility EmptyIconVisibility =>
            PreviewIcon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChromaVisibility => SelectedOption?.ChromaSwatchVisibility ?? Visibility.Collapsed;
        public string EquippedChromaId { get; set; }
        public IReadOnlyList<SkinChoice> Options { get; }

        public SkinChoice? SelectedOption
        {
            get => selectedOption;
            set
            {
                if (ReferenceEquals(selectedOption, value))
                    return;
                selectedOption = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewIcon));
                OnPropertyChanged(nameof(ChromaIcon));
                OnPropertyChanged(nameof(EmptyIconVisibility));
                OnPropertyChanged(nameof(ChromaVisibility));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class CollectionColumn
    {
        private static readonly (string Title, string[] Weapons)[][] Layout =
        [
            [
                ("SIDEARMS", ["Classic", "Shorty", "Frenzy", "Ghost", "Sheriff", "Bandit"])
            ],
            [
                ("SMGS", ["Stinger", "Spectre"]),
                ("SHOTGUNS", ["Bucky", "Judge"]),
                ("MELEE", ["Melee"])
            ],
            [
                ("RIFLES", ["Bulldog", "Guardian", "Phantom", "Vandal"])
            ],
            [
                ("SNIPER RIFLES", ["Marshal", "Outlaw", "Operator", "Warden"]),
                ("MACHINE GUNS", ["Ares", "Odin"])
            ]
        ];

        private CollectionColumn(IReadOnlyList<WeaponSection> sections) => Sections = sections;

        public IReadOnlyList<WeaponSection> Sections { get; }

        public static IReadOnlyList<CollectionColumn> Group(IReadOnlyList<WeaponRow> rows)
        {
            var remaining = rows.ToList();
            var columns = Layout.Select(column => new CollectionColumn(column.Select(section =>
            {
                var weapons = section.Weapons.Select(name => Take(remaining, name)).OfType<WeaponRow>().ToList();
                return new WeaponSection(section.Title, weapons);
            }).Where(section => section.Weapons.Count > 0).ToList())).Where(column => column.Sections.Count > 0).ToList();

            if (remaining.Count > 0 && columns.Count > 0)
            {
                var last = columns[^1].Sections.ToList();
                last.Add(new WeaponSection("OTHER", remaining));
                columns[^1] = new CollectionColumn(last);
            }
            else if (remaining.Count > 0)
            {
                columns.Add(new CollectionColumn([new WeaponSection("OTHER", remaining)]));
            }
            return columns;
        }

        private static WeaponRow? Take(List<WeaponRow> rows, string name)
        {
            var row = rows.FirstOrDefault(item =>
                item.WeaponName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (row != null)
                rows.Remove(row);
            return row;
        }
    }

    private sealed class WeaponSection(string title, IReadOnlyList<WeaponRow> weapons)
    {
        public string Title { get; } = title;
        public IReadOnlyList<WeaponRow> Weapons { get; } = weapons;
    }

    private sealed class AgentChoice
    {
        private AgentChoice(string? id, string name, string role, BitmapImage? portrait, bool random)
        {
            Id = id;
            Name = name;
            Role = role;
            Portrait = portrait;
            RandomMarkVisibility = random ? Visibility.Visible : Visibility.Collapsed;
        }

        public AgentChoice(ValorantLoadoutService.AgentDefinition source)
            : this(source.Id, source.Name, source.Role ?? "Agent",
                CreateImage(source.PortraitUrl) ?? CreateImage(source.IconUrl), false)
        {
        }

        public static AgentChoice Random() => new(null, "Random", "Any agent", null, true);

        public string? Id { get; }
        public string Name { get; }
        public string Role { get; }
        public BitmapImage? Portrait { get; }
        public Visibility RandomMarkVisibility { get; }
    }

    private sealed class SkinChoice
    {
        public SkinChoice(ValorantLoadoutService.SkinOption source)
        {
            Source = source;
            Icon = CreateImage(source.IconUrl);
            ChromaSwatch = CreateImage(source.ChromaIconUrl);
        }

        public ValorantLoadoutService.SkinOption Source { get; }
        public string SkinId => Source.SkinId;
        public string ChromaId => Source.ChromaId;
        public string Name => Source.Name.Split('\n')[0];
        public string Variant =>
            Source.Name.Contains('\n') ? Source.Name[(Source.Name.IndexOf('\n') + 1)..] : string.Empty;
        public BitmapImage? Icon { get; }
        public BitmapImage? ChromaSwatch { get; }
        public Visibility EmptyIconVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChromaSwatchVisibility =>
            ChromaSwatch == null || SameImage(Icon, ChromaSwatch) ? Visibility.Collapsed : Visibility.Visible;

        private static bool SameImage(BitmapImage? left, BitmapImage? right) =>
            left?.UriSource != null && left.UriSource == right?.UriSource;
    }

    private static async Task LoadImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || images.ContainsKey(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            // Download before creating the bitmap. BitmapImage.UriSource stays busy while the
            // file downloads, and Freeze then throws "This Freezable cannot be frozen."
            var bytes = await ImageClient.GetByteArrayAsync(uri).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            images[url] = image;
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[ValorantLoadout] Image could not be loaded from {url}: {exception.Message}");
        }
    }

    private static BitmapImage? CreateImage(string? url) =>
        url != null && images.TryGetValue(url, out var image) ? image : null;
}

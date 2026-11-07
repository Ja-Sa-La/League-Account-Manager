using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace League_Account_Manager.views;

public partial class DiscordPresence : Page
{
    private bool initializing = true;

    public DiscordPresence()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSettings();
        LoadSettings();
    }

    private void LoadSettings()
    {
        initializing = true;
        var settings = Misc.Settings.settingsloaded;
        Enabled.IsChecked = settings.DiscordRichPresence;
        ShowMatch.IsChecked = settings.DiscordShowMatch;
        ShowScore.IsChecked = settings.DiscordShowScore;
        ShowKd.IsChecked = settings.DiscordShowKd;
        ShowPartyJoin.IsChecked = settings.DiscordShowPartyJoin;
        ShowTracker.IsChecked = settings.DiscordShowTracker;
        ShowGitHub.IsChecked = settings.DiscordShowGitHub;
        ShowTeam.IsChecked = settings.DiscordShowTeam;
        ShowPartySize.IsChecked = settings.DiscordShowPartySize;
        ShowPlayers.IsChecked = settings.DiscordShowPlayers;
        ShowAlongsideGame.IsChecked = settings.DiscordShowAlongsideGame;
        ShowRiotId.IsChecked = settings.DiscordShowRiotId;
        ShowLevel.IsChecked = settings.DiscordShowLevel;
        ShowRegion.IsChecked = settings.DiscordShowRegion;
        ShowRank.IsChecked = settings.DiscordShowRank;
        ShowCurrency.IsChecked = settings.DiscordShowCurrency;
        ShowCollection.IsChecked = settings.DiscordShowCollection;
        initializing = false;
        UpdatePreview();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (initializing)
            return;

        var settings = Misc.Settings.settingsloaded;
        settings.DiscordRichPresence = Enabled.IsChecked == true;
        settings.DiscordShowMatch = ShowMatch.IsChecked == true;
        settings.DiscordShowScore = ShowScore.IsChecked == true;
        settings.DiscordShowKd = ShowKd.IsChecked == true;
        settings.DiscordShowPartyJoin = ShowPartyJoin.IsChecked == true;
        settings.DiscordShowTracker = ShowTracker.IsChecked == true;
        settings.DiscordShowGitHub = ShowGitHub.IsChecked == true;
        settings.DiscordShowTeam = ShowTeam.IsChecked == true;
        settings.DiscordShowPartySize = ShowPartySize.IsChecked == true;
        settings.DiscordShowPlayers = ShowPlayers.IsChecked == true;
        settings.DiscordShowAlongsideGame = ShowAlongsideGame.IsChecked == true;
        settings.DiscordShowRiotId = ShowRiotId.IsChecked == true;
        settings.DiscordShowLevel = ShowLevel.IsChecked == true;
        settings.DiscordShowRegion = ShowRegion.IsChecked == true;
        settings.DiscordShowRank = ShowRank.IsChecked == true;
        settings.DiscordShowCurrency = ShowCurrency.IsChecked == true;
        settings.DiscordShowCollection = ShowCollection.IsChecked == true;
        Misc.Settings.settingsloaded = settings;
        Misc.Settings.Save();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        ShowPreview(LeagueDetails, LeagueState, LeagueButtons, Misc.DiscordPresenceService.Preview("In game",
            "Ranked Solo · Summoner's Rift · Ahri", "12-8", "4/2", "Player#EUW", 300, "EUW", "Diamond II",
            "12,400 BE · 350 RP", "167 champs", "Blue", "10 players", null), false);
        ShowPreview(ValorantDetails, ValorantState, ValorantButtons, Misc.DiscordPresenceService.Preview("In lobby",
            "Swiftplay", null, null, "Player#EU", 176, "EU", "Immortal 2",
            "776 VP · 1,888 KC", "20 agents", null, null, "1/5"), true);
    }

    private void ShowPreview(TextBlock details, TextBlock state, Panel buttons, string preview, bool canJoin)
    {
        var lines = preview.Split('\n', 2);
        details.Text = lines[0];
        state.Text = lines.Length == 2 ? lines[1] : string.Empty;
        buttons.Children.Clear();
        foreach (var label in PreviewButtons(canJoin))
            buttons.Children.Add(PreviewButton(label));
    }

    private IEnumerable<string> PreviewButtons(bool canJoin)
    {
        var labels = new List<string>(2);
        if (canJoin && ShowPartyJoin.IsChecked == true)
            labels.Add("Join party");
        if (ShowTracker.IsChecked == true)
            labels.Add("Tracker");
        if (labels.Count < 2 && ShowGitHub.IsChecked == true)
            labels.Add("GitHub");
        return labels.Take(2);
    }

    private static Border PreviewButton(string label) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2D, 0x31)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 4, 10, 4),
        Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)),
            FontSize = 12
        }
    };
}

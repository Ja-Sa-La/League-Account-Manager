using System.Windows;
using System.Windows.Controls;

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
        LeaguePreview.Text = Misc.DiscordPresenceService.Preview("In lobby",
            "Ranked Solo · Summoner's Rift · 3/5", null, null, "Player#EUW", 300, "EUW", "Diamond II",
            "12,400 BE · 350 RP", "167 champs");
        ValorantPreview.Text = Misc.DiscordPresenceService.Preview("In lobby",
            "Swiftplay · 1/5", null, null, "Player#EU", 176, "EU", "Immortal 2",
            "776 VP · 1,888 KC", "20 agents");
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using CsvHelper.Configuration.Attributes;
using NLog;

namespace League_Account_Manager.Misc;

public class Utils
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void StartRiotClient(string riotPath, string arguments)
    {
        var workingDirectory = Path.GetDirectoryName(riotPath) ?? AppContext.BaseDirectory;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            StartRiotClientDirectly(riotPath, arguments, workingDirectory);
            return;
        }

        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application") ??
                            throw new InvalidOperationException("Windows Explorer shell is unavailable.");
            shell = Activator.CreateInstance(shellType) ??
                    throw new InvalidOperationException("Unable to access the Windows Explorer shell.");

            dynamic explorerShell = shell;
            explorerShell.ShellExecute(riotPath, arguments, workingDirectory, null, 1);
            DebugConsole.WriteLine("[RiotLauncher] Started Riot Client through the unelevated Explorer shell.");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine(
                $"[RiotLauncher] Unelevated launch failed; using current process token: {ex.Message}",
                ConsoleColor.Yellow);
            StartRiotClientDirectly(riotPath, arguments, workingDirectory);
        }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void StartRiotClientDirectly(string riotPath, string arguments, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = riotPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        });
    }

    public static void RemoveDoubleQuotesFromList(List<AccountList> accountList)
    {
        foreach (var account in accountList)
        {
            account.username = RemoveDoubleQuotes(account.username);
            account.password = RemoveDoubleQuotes(account.password);
            account.riotID = RemoveDoubleQuotes(account.riotID);
            account.server = RemoveDoubleQuotes(account.server);
            account.rank = RemoveDoubleQuotes(account.rank);
            account.champions = RemoveDoubleQuotes(account.champions);
            account.skins = RemoveDoubleQuotes(account.skins);
            account.Loot = RemoveDoubleQuotes(account.Loot);
            account.rank2 = RemoveDoubleQuotes(account.rank2);
            account.lastPlayed = RemoveDoubleQuotes(account.lastPlayed);
            account.leagueMatchHistory = RemoveDoubleQuotes(account.leagueMatchHistory);
            account.note = RemoveDoubleQuotes(account.note);
            SanitizeStructuredEntries(account.championsData);
            SanitizeStructuredEntries(account.skinsData);
            SanitizeStructuredEntries(account.lootData);
            SanitizeStructuredEntries(account.valorantAgentsData);
            SanitizeStructuredEntries(account.valorantContractsData);
            SanitizeStructuredEntries(account.valorantSpraysData);
            SanitizeStructuredEntries(account.valorantGunBuddiesData);
            SanitizeStructuredEntries(account.valorantCardsData);
            SanitizeStructuredEntries(account.valorantSkinsData);
            SanitizeStructuredEntries(account.valorantSkinVariantsData);
            SanitizeStructuredEntries(account.valorantTitlesData);
            account.valorantAgents = RemoveDoubleQuotes(account.valorantAgents);
            account.valorantContracts = RemoveDoubleQuotes(account.valorantContracts);
            account.valorantSprays = RemoveDoubleQuotes(account.valorantSprays);
            account.valorantGunBuddies = RemoveDoubleQuotes(account.valorantGunBuddies);
            account.valorantCards = RemoveDoubleQuotes(account.valorantCards);
            account.valorantSkins = RemoveDoubleQuotes(account.valorantSkins);
            account.valorantSkinVariants = RemoveDoubleQuotes(account.valorantSkinVariants);
            account.valorantTitles = RemoveDoubleQuotes(account.valorantTitles);
            account.valorantRank = RemoveDoubleQuotes(account.valorantRank);
            account.valorantServer = RemoveDoubleQuotes(account.valorantServer);
        }
    }

    public static string? RemoveDoubleQuotes(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input.Replace("\"", "");
    }

    private static void SanitizeStructuredEntries(List<StructuredDataEntry>? entries)
    {
        if (entries == null)
            return;

        foreach (var entry in entries)
        {
            entry.name = RemoveDoubleQuotes(entry.name);
            entry.icon = RemoveDoubleQuotes(entry.icon);
            entry.value = RemoveDoubleQuotes(entry.value);

            if (entry.extra == null)
                continue;

            foreach (var key in entry.extra.Keys.ToList())
                entry.extra[key] = RemoveDoubleQuotes(entry.extra[key]) ?? string.Empty;
        }
    }

    public static void KillLeagueFunc()
    {
        try
        {
            var source = new[]
            {
                "RiotClientUxRender", "RiotClientUx", "RiotClientServices", "RiotClientCrashHandler",
                "LeagueCrashHandler",
                "LeagueClientUxRender", "LeagueClientUx", "LeagueClient", "VALORANT-Win64-Shipping", "VALORANT"
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
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to kill Riot/League processes");
        }
    }

    public static void KillLeagueFunc2()
    {
        try
        {
            var source = new[]
            {
                "LeagueClient"
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
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to kill LeagueClient processes");
        }
    }

    public class AccountList
    {
        public string? username { get; set; }
        public string? password { get; set; }
        public string? riotID { get; set; }
        public int? level { get; set; }
        public string? server { get; set; }
        public int? be { get; set; }
        public int? rp { get; set; }
        public string? rank { get; set; }
        public string? champions { get; set; }
        public string? skins { get; set; }
        public List<StructuredDataEntry>? championsData { get; set; }
        public List<StructuredDataEntry>? skinsData { get; set; }

        [Ignore] public int Champions { get; set; }

        [Ignore] public int Skins { get; set; }

        public string? Loot { get; set; }
        public List<StructuredDataEntry>? lootData { get; set; }

        [Ignore] public int Loots { get; set; }

        public string? rank2 { get; set; }
        public string? lastPlayed { get; set; }
        public string? leagueMatchHistory { get; set; }
        public string? note { get; set; }
        public string? valorantAgents { get; set; }
        public List<StructuredDataEntry>? valorantAgentsData { get; set; }
        public string? valorantContracts { get; set; }
        public List<StructuredDataEntry>? valorantContractsData { get; set; }
        public string? valorantSprays { get; set; }
        public List<StructuredDataEntry>? valorantSpraysData { get; set; }
        public string? valorantGunBuddies { get; set; }
        public List<StructuredDataEntry>? valorantGunBuddiesData { get; set; }
        public string? valorantCards { get; set; }
        public List<StructuredDataEntry>? valorantCardsData { get; set; }
        public string? valorantSkins { get; set; }
        public List<StructuredDataEntry>? valorantSkinsData { get; set; }
        public string? valorantSkinVariants { get; set; }
        public List<StructuredDataEntry>? valorantSkinVariantsData { get; set; }
        public string? valorantTitles { get; set; }
        public List<StructuredDataEntry>? valorantTitlesData { get; set; }
        public int? valorantVp { get; set; }
        public int? valorantRp { get; set; }
        public int? valorantKc { get; set; }
        public int? valorantLevel { get; set; }
        public string? valorantRank { get; set; }
        public string? valorantServer { get; set; }
        public int? valorantXp { get; set; }

        [Ignore] public int ValorantAgentsCount => CountTokens(valorantAgents);

        [Ignore] public int ValorantContractsCount => CountTokens(valorantContracts);

        [Ignore] public int ValorantSpraysCount => CountTokens(valorantSprays);

        [Ignore] public int ValorantGunBuddiesCount => CountTokens(valorantGunBuddies);

        [Ignore] public int ValorantCardsCount => CountTokens(valorantCards);

        [Ignore] public int ValorantSkinsCount => CountTokens(valorantSkins) + CountTokens(valorantSkinVariants);

        [Ignore] public int ValorantTitlesCount => CountTokens(valorantTitles);

        private static int CountTokens(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return value.Split(':', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    public enum AccountCopyFormat
    {
        Formatted,
        Simple
    }

    public enum AccountCopySection
    {
        League,
        Valorant,
        Both
    }

    public static string FormatAccountForCopy(AccountList account, bool fullDetails, AccountCopyFormat format)
    {
        return FormatAccountForCopy(account, fullDetails, format, AccountCopySection.Both);
    }

    public static string FormatAccountForCopy(AccountList account, bool fullDetails, AccountCopyFormat format,
        AccountCopySection section)
    {
        var fields = GetCopyFields(account, fullDetails, section).ToList();
        if (format == AccountCopyFormat.Simple)
            return string.Join(" | ", fields.Select(field => field.Value));

        var builder = new StringBuilder();
        foreach (var group in fields.GroupBy(field => field.Group))
        {
            if (group.Key != "Account")
                builder.Append("**").Append(group.Key).AppendLine("**");
            foreach (var field in group)
                AppendField(builder, field.Name, field.Value);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<(string Group, string Name, string Value)> GetCopyFields(AccountList account,
        bool fullDetails, AccountCopySection section)
    {
        var fields = new List<(string, string, string)>
        {
            ("Account", "Username", account.username ?? string.Empty),
            ("Account", "Password", account.password ?? string.Empty),
            ("Account", "Riot ID", account.riotID ?? string.Empty)
        };

        if (section is AccountCopySection.League or AccountCopySection.Both)
        {
            fields.AddRange([
                ("League", "Level", account.level?.ToString() ?? string.Empty),
                ("League", "Server", account.server ?? string.Empty),
                ("League", "Solo rank", account.rank ?? string.Empty),
                ("League", "Flex rank", account.rank2 ?? string.Empty),
                ("League", "Blue Essence", account.be?.ToString() ?? string.Empty),
                ("League", "Riot Points", account.rp?.ToString() ?? string.Empty)
            ]);
            if (fullDetails)
                fields.AddRange([
                    ("League", "Last played", account.lastPlayed ?? string.Empty),
                    ("League", "Note", account.note ?? string.Empty),
                    ("League", "Champions", FormatItems(account.championsData, account.champions)),
                    ("League", "Skins", FormatItems(account.skinsData, account.skins)),
                    ("League", "Loot", FormatItems(account.lootData, account.Loot))
                ]);
        }

        if (section is AccountCopySection.Valorant or AccountCopySection.Both)
        {
            fields.AddRange([
                ("VALORANT", "Server", account.valorantServer ?? string.Empty),
                ("VALORANT", "Level", account.valorantLevel?.ToString() ?? string.Empty),
                ("VALORANT", "Rank", account.valorantRank ?? string.Empty),
                ("VALORANT", "VP", account.valorantVp?.ToString() ?? string.Empty),
                ("VALORANT", "RP", account.valorantRp?.ToString() ?? string.Empty),
                ("VALORANT", "Kingdom Credits", account.valorantKc?.ToString() ?? string.Empty)
            ]);
            if (fullDetails)
                fields.AddRange([
                    ("VALORANT", "XP", account.valorantXp?.ToString() ?? string.Empty),
                    ("VALORANT", "Agents", FormatItems(account.valorantAgentsData, account.valorantAgents)),
                    ("VALORANT", "Contracts", FormatItems(account.valorantContractsData, account.valorantContracts)),
                    ("VALORANT", "Sprays", FormatItems(account.valorantSpraysData, account.valorantSprays)),
                    ("VALORANT", "Gun buddies", FormatItems(account.valorantGunBuddiesData, account.valorantGunBuddies)),
                    ("VALORANT", "Cards", FormatItems(account.valorantCardsData, account.valorantCards)),
                    ("VALORANT", "Skins", FormatItems(account.valorantSkinsData, account.valorantSkins)),
                    ("VALORANT", "Skin variants", FormatItems(account.valorantSkinVariantsData, account.valorantSkinVariants)),
                    ("VALORANT", "Titles", FormatItems(account.valorantTitlesData, account.valorantTitles))
                ]);
        }

        return fields.Where(field => !string.IsNullOrWhiteSpace(field.Item3));
    }

    private static string Summarize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var count = value.Split(':', StringSplitOptions.RemoveEmptyEntries).Length;
        return $"{count} item{(count == 1 ? string.Empty : "s")}";
    }

    private static string FormatItems(List<StructuredDataEntry>? entries, string? value)
    {
        var names = entries?
            .Select(entry => entry.name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToList();

        if (names is { Count: > 0 })
            return string.Join(", ", names);

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(", ", value.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0));
    }

    private static void AppendField(StringBuilder builder, string name, string? value)
    {
        builder.Append("**").Append(name).Append(":** ")
            .AppendLine(SanitizeDiscordValue(value));
    }

    private static string SanitizeDiscordValue(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    public class StructuredDataEntry
    {
        public string? name { get; set; }
        public string? icon { get; set; }
        public string? value { get; set; }
        public Dictionary<string, string>? extra { get; set; }
    }

    public class Wallet
    {
        public int? be { get; set; }
        public int? rp { get; set; }
    }

    public class SettingsIngame
    {
        public SettingsIngame()
        {
            FloatingText = new FloatingTextSettings();
            General = new GeneralSettings();
            HUD = new HudSettings();
            LossOfControl = new LossOfControlSettings();
            Performance = new PerformanceSettings();
            Voice = new VoiceSettings();
            Volume = new VolumeSettings();
            MapSkinOptions = new MapSkinOptionsSettings();
            TFT = new TFTSettings();
            Replay = new ReplaySettings();
            Mobile = new MobileSettings();
            Swarm = new SwarmSettings();
            Highlights = new HighlightsSettings();
            ItemShop = new ItemShopSettings();
            Chat = new ChatSettings();
        }

        public FloatingTextSettings FloatingText { get; set; }
        public GeneralSettings General { get; set; }
        public HudSettings HUD { get; set; }
        public LossOfControlSettings LossOfControl { get; set; }
        public PerformanceSettings Performance { get; set; }
        public VoiceSettings Voice { get; set; }
        public VolumeSettings Volume { get; set; }
        public MapSkinOptionsSettings MapSkinOptions { get; set; }
        public TFTSettings TFT { get; set; }
        public ReplaySettings Replay { get; set; }
        public MobileSettings Mobile { get; set; }
        public SwarmSettings Swarm { get; set; }
        public HighlightsSettings Highlights { get; set; }
        public ItemShopSettings ItemShop { get; set; }
        public ChatSettings Chat { get; set; }
    }

    public class FloatingTextSettings
    {
        public bool Special_Enabled { get; set; }
        public bool Score_Enabled { get; set; }
        public bool QuestReceived_Enabled { get; set; }
        public bool ManaDamage_Enabled { get; set; }
        public bool Level_Enabled { get; set; }
        public bool Invulnerable_Enabled { get; set; }
        public bool Heal_Enabled { get; set; }
        public bool Gold_Enabled { get; set; }
        public bool Experience_Enabled { get; set; }
        public bool EnemyDamage_Enabled { get; set; }
        public bool Dodge_Enabled { get; set; }
        public bool Damage_Enabled { get; set; }
    }

    public class GeneralSettings
    {
        public int SystemMouseSpeed { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public bool MinimizeCameraMotion { get; set; }
        public bool HideExternalBrowserPrompt { get; set; }
        public bool EnableLightFx { get; set; }
        public bool EnableGlobalSpellCastNotifications { get; set; }
        public bool EnableCustomAnnouncer { get; set; }
        public bool EnableCosmeticArenaAudioTFT { get; set; }
        public bool EnableChampionSpellPreview { get; set; }
        public bool AlwaysShowExtendedTooltip { get; set; }
        public string CfgVersion { get; set; } = string.Empty;
        public int WindowMode { get; set; }
        public bool WaitForVerticalSync { get; set; }
        public bool ThemeMusic { get; set; }
        public bool TargetChampionsOnlyAsToggle { get; set; }
        public bool SnapCameraOnRespawn { get; set; }
        public bool ShowTurretRangeIndicators { get; set; }
        public bool ShowGodray { get; set; }
        public bool ShowCursorLocator { get; set; }
        public bool RelativeTeamColors { get; set; }
        public bool RecommendJunglePaths { get; set; }
        public bool PreferOpenGLLegacyMode { get; set; }
        public bool PredictMovement { get; set; }
        public bool OSXMouseAcceleration { get; set; }
        public bool HideEyeCandy { get; set; }
        public int GameMouseSpeed { get; set; }
        public bool EnableTargetedAttackMove { get; set; }
        public bool EnableAudio { get; set; }
        public double CursorScale { get; set; }
        public bool CursorOverride { get; set; }
        public bool ClampCastTargetLocationWithinMaxRange { get; set; }
        public bool BindSysKeys { get; set; }
        public bool AutoAcquireTarget { get; set; }
        public bool UserSetResolution { get; set; }
    }

    public class HudSettings
    {
        public bool HideEnemySummonerEmotes { get; set; }
        public bool ShowPlayerPerks { get; set; }
        public bool ShowPlayerStats { get; set; }
        public bool HidePlayerNames { get; set; }
        public bool EnableItemComponentPurchasing { get; set; }
        public bool SmartCastWithIndicator_CastWhenNewSpellSelected { get; set; }
        public bool SmartCastOnKeyRelease { get; set; }
        public bool ShowTimestamps { get; set; }
        public bool ShowTeamFramesOnLeft { get; set; }
        public bool ShowSummonerNamesInScoreboard { get; set; }
        public bool ShowSummonerNames { get; set; }
        public bool ShowSpellRecommendations { get; set; }
        public bool ShowSpellCosts { get; set; }
        public bool ShowOffScreenPointsOfInterest { get; set; }
        public bool ShowNeutralCamps { get; set; }
        public bool ShowAttackRadius { get; set; }
        public bool ShowAlliedChat { get; set; }
        public bool ShowAllChannelChat { get; set; }
        public bool ScrollSmoothingEnabled { get; set; }
        public double ObjectiveVoteScale { get; set; }
        public int NumericCooldownFormat { get; set; }
        public bool MirroredScoreboard { get; set; }
        public double MinimapScale { get; set; }
        public bool MinimapMoveSelf { get; set; }
        public bool MiddleClickDragScrollEnabled { get; set; }
        public double MapScrollSpeed { get; set; }
        public double KeyboardScrollSpeed { get; set; }
        public bool HideReciprocityFist { get; set; }
        public double GlobalScale { get; set; }
        public bool FlipMiniMap { get; set; }
        public bool FlashScreenWhenStunned { get; set; }
        public bool FlashScreenWhenDamaged { get; set; }
        public int EternalsMilestoneDisplayMode { get; set; }
        public bool EnableLineMissileVis { get; set; }
        public double EmoteSize { get; set; }
        public int EmotePopupUIDisplayMode { get; set; }
        public bool DrawHealthBars { get; set; }
        public bool DisableHudSpellClick { get; set; }
        public double ChatScale { get; set; }
        public int ChatChannelVisibility { get; set; }
        public bool CameraLockMode { get; set; }
        public bool AutoDisplayTarget { get; set; }
    }

    public class LossOfControlSettings
    {
        public bool ShowSlows { get; set; }
        public bool LossOfControlEnabled { get; set; }
    }

    public class PerformanceSettings
    {
        public int ShadowQuality { get; set; }
        public int FrameCapType { get; set; }
        public int EnvironmentQuality { get; set; }
        public int EffectsQuality { get; set; }
        public int CharacterQuality { get; set; }
        public bool EnableFXAA { get; set; }
        public bool EnableHUDAnimations { get; set; }
        public bool AutoPerformanceSettings { get; set; }
    }

    public class VoiceSettings
    {
        public string InputDevice { get; set; } = string.Empty;
        public double InputVolume { get; set; }
        public double ActivationSensitivity { get; set; }
        public int InputMode { get; set; }
        public bool ShowVoicePanelWithScoreboard { get; set; }
        public bool ShowVoiceChatHalos { get; set; }
    }

    public class VolumeSettings
    {
        public double VoiceVolume { get; set; }
        public bool VoiceMute { get; set; }
        public double SfxVolume { get; set; }
        public bool SfxMute { get; set; }
        public double PingsVolume { get; set; }
        public bool PingsMute { get; set; }
        public double MusicVolume { get; set; }
        public bool MusicMute { get; set; }
        public double MasterVolume { get; set; }
        public bool MasterMute { get; set; }
        public double AnnouncerVolume { get; set; }
        public bool AnnouncerMute { get; set; }
        public double AmbienceVolume { get; set; }
        public bool AmbienceMute { get; set; }
    }

    public class MapSkinOptionsSettings
    {
        public bool MapSkinOptionDisableWorlds { get; set; }
        public bool MapSkinOptionDisableURF { get; set; }
        public bool MapSkinOptionDisableStarGuardian { get; set; }
        public bool MapSkinOptionDisableSnowdown { get; set; }
        public bool MapSkinOptionDisableProject { get; set; }
        public bool MapSkinOptionDisablePopstar { get; set; }
        public bool MapSkinOptionDisablePoolParty { get; set; }
        public bool MapSkinOptionDisableOdyssey { get; set; }
        public bool MapSkinOptionDisableMSI { get; set; }
        public bool MapSkinOptionDisableLunarRevel { get; set; }
        public bool MapSkinOptionDisableArcade { get; set; }
    }

    public class TFTSettings
    {
        public bool TFTDisableMusicSourceInfo { get; set; }
        public bool TFTEnablePushNotifications { get; set; }
    }

    public class ReplaySettings
    {
        public bool EnableDirectedCamera { get; set; }
    }

    public class MobileSettings
    {
        public string LastTickerTime { get; set; } = string.Empty;
        public string AppRegion { get; set; } = string.Empty;
        public int SelectedQueue { get; set; }
        public int iOSMetalUserId { get; set; }
        public bool iOSMetalPercentEnabled { get; set; }
        public int CameraHeight { get; set; }
        public bool OfferedTutorial { get; set; }
    }

    public class SwarmSettings
    {
        public bool CursorAimEnabled { get; set; }
    }

    public class HighlightsSettings
    {
        public int VideoQuality { get; set; }
        public int VideoFrameRate { get; set; }
        public int ScaleVideo { get; set; }
        public int AudioQuality { get; set; }
    }

    public class ItemShopSettings
    {
        public double NativeOffsetY { get; set; }
        public double NativeOffsetX { get; set; }
        public int CurrentTab { get; set; }
    }

    public class ChatSettings
    {
        public bool EnableChatFilter { get; set; }
    }
}
using System.Diagnostics;
using NLog;
using static League_Account_Manager.views.Page1;

namespace League_Account_Manager;

public class Utils
{
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
            account.note = RemoveDoubleQuotes(account.note);
        }
    }
    public static string? RemoveDoubleQuotes(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input.Replace("\"", "");
    }

    public static void killleaguefunc()
    {
        try
        {
            var source = new[]
            {
                "RiotClientUxRender", "RiotClientUx", "RiotClientServices", "RiotClientCrashHandler",
                "LeagueCrashHandler",
                "LeagueClientUxRender", "LeagueClientUx", "LeagueClient"
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
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
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
        public int Champions { get; set; }
        public int Skins { get; set; }
        public string? Loot { get; set; }
        public int Loots { get; set; }
        public string? rank2 { get; set; }
        public string? note { get; set; }
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
        public string CfgVersion { get; set; }
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
        public string InputDevice { get; set; }
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
        public string LastTickerTime { get; set; }
        public string AppRegion { get; set; }
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
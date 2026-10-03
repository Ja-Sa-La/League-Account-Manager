using League_Account_Manager.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using JsonSerializer = System.Text.Json.JsonSerializer;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace League_Account_Manager.Misc;

public enum PersistentLoginMode
{
    Ask = 0,
    Always = 1,
    Never = 2
}

public static class Settings
{
    public static settings1 settingsloaded;
    public static event Action? AccountPasswordSupplied;

    public static void Save()
    {
        var copy = settingsloaded;
        copy.AccountFileEncryptionPassword = null;
        var json = JsonSerializer.Serialize(copy);
        var settingsPath = GetSettingsPath();
        var temporaryPath = settingsPath + ".tmp";
        var backupPath = settingsPath + ".bak";

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(settingsPath))
                File.Replace(temporaryPath, settingsPath, backupPath, true);
            else
                File.Move(temporaryPath, settingsPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string CreateSyncDocument()
    {
        var copy = settingsloaded;
        copy.AccountFileEncryptionPassword = null;
        var node = JsonSerializer.SerializeToNode(copy) as JsonObject ?? new JsonObject();
        foreach (var property in new[]
                 {
                     "LeaguePath", "riotPath", "settingsLocation", "AccountFileEncryptionEnabled",
                     "AccountFileEncryptionPassword", "CloudSyncPromptShown", "DisabledPluginPaths"
                 })
            node.Remove(property);
        return node.ToJsonString();
    }

    public static void ApplySyncDocument(string document)
    {
        var incoming = JsonSerializer.Deserialize<settings1>(document);
        if (incoming.Equals(default(settings1)))
            throw new InvalidDataException("The cloud settings document is invalid.");

        settingsloaded.updates = incoming.updates;
        settingsloaded.ReleaseChannel = incoming.ReleaseChannel;
        settingsloaded.DisplayPasswords = incoming.DisplayPasswords;
        settingsloaded.UpdateRanks = incoming.UpdateRanks;
        settingsloaded.PersistentLoginMode = incoming.PersistentLoginMode;
        settingsloaded.LeagueDefaultSortColumn = incoming.LeagueDefaultSortColumn;
        settingsloaded.LeagueDefaultSortDescending = incoming.LeagueDefaultSortDescending;
        settingsloaded.ValorantDefaultSortColumn = incoming.ValorantDefaultSortColumn;
        settingsloaded.ValorantDefaultSortDescending = incoming.ValorantDefaultSortDescending;
        Save();
    }

    public static async Task LoadAsync()
    {
        var settingsPath = GetSettingsPath();
        settingsloaded = File.Exists(settingsPath)
            ? LoadFromDisk(settingsPath)
            : CreateDefaults();

        NormalizeAndMigrateAccountFileName();
        settingsloaded.LeagueDefaultSortColumn ??= "level";
        settingsloaded.ValorantDefaultSortColumn ??= "valorantLevel";

        await LoadAccountFilePasswordAsync();
        await DiscoverMissingPathsAsync();
        Save();
    }

    private static settings1 LoadFromDisk(string settingsPath)
    {
        try
        {
            return MergeWithDefaults(File.ReadAllText(settingsPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var backupPath = settingsPath + ".bak";
            if (!File.Exists(backupPath))
                throw;

            DebugConsole.WriteLine($"[Settings] Recovering settings backup: {ex.Message}");
            var recovered = MergeWithDefaults(File.ReadAllText(backupPath));
            File.Copy(backupPath, settingsPath, true);
            return recovered;
        }
    }

    private static async Task LoadAccountFilePasswordAsync()
    {
        if (!settingsloaded.AccountFileEncryptionEnabled)
            return;

        AccountFileStore.SetPassword(null);
        string? password = null;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
            await dispatcher.InvokeAsync(() => password = PromptForAccountFilePassword(
                "Enter the password to decrypt your account list."));
        else
            password = PromptForAccountFilePassword("Enter the password to decrypt your account list.");

        if (string.IsNullOrWhiteSpace(password))
        {
            AppMessageBox.Show(
                "Account file password is required to load encrypted accounts. The application will now close.",
                "Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            Application.Current?.Shutdown();
            Environment.Exit(0);
            return;
        }

        AccountFileStore.SetPassword(password);
        AccountPasswordSupplied?.Invoke();
    }

    private static async Task DiscoverMissingPathsAsync()
    {
        if (string.IsNullOrWhiteSpace(settingsloaded.riotPath) || !File.Exists(settingsloaded.riotPath))
            settingsloaded.riotPath = FindRiot();

        if (string.IsNullOrWhiteSpace(settingsloaded.LeaguePath) || !File.Exists(settingsloaded.LeaguePath))
            settingsloaded.LeaguePath = await FindLeagueAsync();

        if (string.IsNullOrWhiteSpace(settingsloaded.settingsLocation) ||
            !File.Exists(settingsloaded.settingsLocation))
            settingsloaded.settingsLocation = FindSettings();
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Settings.json");
    }

    private static string? PromptForAccountFilePassword(string message)
    {
        var prompt = new PasswordPrompt(message);
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsLoaded)
            prompt.Owner = owner;
        var result = prompt.ShowDialog();
        return result == true ? prompt.Password : null;
    }

    private static void NormalizeAndMigrateAccountFileName()
    {
        var rawName = settingsloaded.filename;
        var oldBaseName = string.IsNullOrWhiteSpace(rawName) ? "Accounts" : Path.GetFileNameWithoutExtension(rawName);
        if (string.IsNullOrWhiteSpace(oldBaseName))
            oldBaseName = "Accounts";

        var normalized = oldBaseName.Trim();
        if (string.Equals(normalized, "List", StringComparison.OrdinalIgnoreCase))
            normalized = "Accounts";

        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "Accounts";

        settingsloaded.filename = normalized;

        var baseDirectory = AppContext.BaseDirectory;
        var targetLamPath = Path.Combine(baseDirectory, $"{normalized}.LAM");
        if (File.Exists(targetLamPath))
            return;

        var oldCandidates = new[]
        {
            Path.Combine(baseDirectory, $"{oldBaseName}.LAM"),
            Path.Combine(baseDirectory, $"{oldBaseName}.csv"),
            Path.Combine(baseDirectory, "List.LAM"),
            Path.Combine(baseDirectory, "List.csv")
        };

        foreach (var candidate in oldCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
                continue;

            if (string.Equals(candidate, targetLamPath, StringComparison.OrdinalIgnoreCase))
                return;

            File.Move(candidate, targetLamPath);
            return;
        }
    }

    private static string FindRiot()
    {
        DebugConsole.WriteLine("[Settings] Finding Riot client path...");

        var found = FindRiotClientExecutable();
        if (found != null)
        {
            DebugConsole.WriteLine($"[Settings] Riot client found: {found}");
            return found;
        }

        DebugConsole.WriteLine("[Settings] Riot client was not found automatically. Prompting for RiotClientServices.exe.");
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
        openFileDialog.FileName = "RiotClientServices.exe";
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "RiotClientServices.exe")
                {
                    AppMessageBox.Show("Please select a file with the name RiotClientServices.exe.", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                DebugConsole.WriteLine($"[Settings] Riot client selected manually: {openFileDialog.FileName}");
                return openFileDialog.FileName;
            }
            else
            {
                DebugConsole.WriteLine("[Settings] Riot client selection was cancelled. Closing application.");
                Environment.Exit(0);
            }
    }

    /// <summary>
    ///     Locates RiotClientServices.exe from the installer's own registry entries and the usual
    ///     install folders. Returns null when nothing on the machine points at it.
    /// </summary>
    private static string? FindRiotClientExecutable()
    {
        foreach (var candidate in RiotClientCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> RiotClientCandidates()
    {
        // The Riot installer registers every product under the per-user uninstall key, and each
        // product's UninstallString starts with the quoted path of the one shared
        // RiotClientServices.exe. Enumerating beats the old fixed "Riot Game Riot_Client." key,
        // which is missed entirely when the client was installed under a different name.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null)
                continue;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith("Riot Game ", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var subKey = uninstall.OpenSubKey(subKeyName);
                var executable = ExtractExecutablePath(subKey?.GetValue("UninstallString") as string);
                if (executable != null)
                    yield return executable;

                // InstallLocation points at the product folder (e.g. "C:/Riot Games/Riot Client"),
                // so the client executable only lives directly inside the Riot Client one.
                var location = subKey?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(location))
                    yield return Path.Combine(location, "RiotClientServices.exe");
            }
        }

        // The protocol handler is registered machine-wide and survives even when the per-user
        // uninstall entries are missing. Its command is `"<exe>" --app-command="%1"`.
        foreach (var commandKey in new[]
                 {
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\riotclient\shell\open\command",
                     @"HKEY_CURRENT_USER\SOFTWARE\Classes\riotclient\shell\open\command",
                     @"HKEY_CLASSES_ROOT\riotclient\shell\open\command"
                 })
        {
            var executable = ExtractExecutablePath(Registry.GetValue(commandKey, string.Empty, null) as string);
            if (executable != null)
                yield return executable;
        }

        // The default install directory, on whichever drive Windows itself is installed on.
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrEmpty(systemDrive))
            yield return Path.Combine(systemDrive, "Riot Games", "Riot Client", "RiotClientServices.exe");
    }

    /// <summary>
    ///     Pulls the executable out of a registry command line. Riot quotes the path
    ///     ("C:\Riot Games\Riot Client\RiotClientServices.exe" --uninstall-product=...), but an
    ///     unquoted path with no arguments is accepted too.
    /// </summary>
    private static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var match = Regex.Match(command, "^\\s*\"(?<path>[^\"]+)\"|^\\s*(?<path>\\S+)");
        if (!match.Success)
            return null;

        var path = match.Groups["path"].Value;
        return path.EndsWith("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    internal static settings1 CreateDefaults()
    {
        return new settings1
        {
            filename = "Accounts",
            updates = true,
            ReleaseChannel = UpdateReleaseChannel.Stable.ToString(),
            DisplayPasswords = true,
            UpdateRanks = true,
            AccountFileEncryptionEnabled = false,
            AccountFileEncryptionPassword = null,
            PersistentLoginMode = PersistentLoginMode.Ask,
            CloudSyncPromptShown = false,
            DisabledPluginPaths = Array.Empty<string>(),
            LeagueDefaultSortColumn = "level",
            LeagueDefaultSortDescending = true,
            ValorantDefaultSortColumn = "valorantLevel",
            ValorantDefaultSortDescending = true,
            UseLegacyLogin = false,
            AutoImportRunes = false,
            RuneImportSource = "ugg",
            RuneImportRole = "automatic"
        };
    }

    internal static settings1 MergeWithDefaults(string json)
    {
        var mergedSettings = JObject.FromObject(CreateDefaults());
        mergedSettings.Merge(JObject.Parse(json), new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });
        var result = mergedSettings.ToObject<settings1>();

        // Migrate the old boolean PersistentLogin setting to the 3-state mode.
        var parsed = JObject.Parse(json);
        if (parsed.ContainsKey("PersistentLogin") && !parsed.ContainsKey("PersistentLoginMode"))
            result.PersistentLoginMode = parsed["PersistentLogin"]?.Value<bool>() == true
                ? PersistentLoginMode.Always
                : PersistentLoginMode.Never;

        return result;
    }

    private static string FindSettings()
    {
        DebugConsole.WriteLine("[Settings] Finding League settings file...");
        var leagueDirectory = Path.GetDirectoryName(settingsloaded.LeaguePath);
        var settingsPath = string.IsNullOrWhiteSpace(leagueDirectory)
            ? null
            : Path.Combine(leagueDirectory, "Config", "game.cfg");
        DebugConsole.WriteLine(settingsPath ?? "[Settings] League client directory is unavailable.");
        if (settingsPath != null && File.Exists(settingsPath))
        {
            DebugConsole.WriteLine($"[Settings] League settings found automatically: {settingsPath}");
            return settingsPath;
        }

        DebugConsole.WriteLine("[Settings] League settings file was not found automatically. Prompting for game.cfg.");
        var openFileDialog = new OpenFileDialog();
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "game.cfg")
                {
                    AppMessageBox.Show("Please select a file with the name game.cfg", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                DebugConsole.WriteLine($"[Settings] League settings selected manually: {openFileDialog.FileName}");
                return openFileDialog.FileName;
            }
            else
            {
                DebugConsole.WriteLine("[Settings] League settings selection was cancelled.");
                return string.Empty;
                //Environment.Exit(0);
            }
    }

    private static async Task<string> FindLeagueAsync()
    {
        DebugConsole.WriteLine("[Settings] Finding League client path...");
        var startedClient = false;
        if (Process.GetProcessesByName("Riot Client").Length == 0 &&
            Process.GetProcessesByName("RiotClientUx").Length == 0)
        {
            DebugConsole.WriteLine("[Settings] Riot client is not running. Launching it to detect League installation.");
            Process.Start(settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            startedClient = true;
        }

        try
        {
            var clientDetected = false;
            for (var attempt = 0; attempt < 25; attempt++)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0 ||
                    Process.GetProcessesByName("RiotClientUx").Length != 0)
                {
                    clientDetected = true;
                    break;
                }

                await Task.Delay(2000).ConfigureAwait(true);
            }

            if (clientDetected)
            {
                for (var attempt = 0; attempt < 150; attempt++)
                {
                    var readyResp = await Lcu.Connector("riot", "get", "/rso-auth/configuration/v3/ready-state", "")
                        as System.Net.Http.HttpResponseMessage;
                    if (readyResp != null)
                    {
                        using (readyResp)
                        {
                            var readyBody = await readyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            try
                            {
                                var node = JsonNode.Parse(readyBody);
                                if (node?["ready"]?.GetValue<bool>() == true)
                                    break;
                            }
                            catch (JsonException)
                            {
                            }
                        }
                    }

                    await Task.Delay(200).ConfigureAwait(true);
                }
            }

            DebugConsole.WriteLine("[Settings] Querying Riot client for League installation path.");
            JObject? responseBody = null;
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                DebugConsole.WriteLine($"[Settings] League install lookup attempt {attempt}/10.");
                var resp = await Lcu.Connector("riot", "get", "/patch/v1/installs/league_of_legends.live", "")
                    as System.Net.Http.HttpResponseMessage;
                if (resp == null)
                {
                    if (attempt < 10)
                        await Task.Delay(1000);
                    continue;
                }

                using (resp)
                {
                    try
                    {
                        responseBody = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (responseBody.ContainsKey("path"))
                            break;
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (attempt < 10)
                    await Task.Delay(1000);
            }

            if (responseBody?.ContainsKey("path") == true)
            {
                var installPath = responseBody["path"]?.ToString();
                if (!string.IsNullOrWhiteSpace(installPath))
                {
                    var leaguePath = Path.Combine(installPath.Replace('/', '\\'), "LeagueClient.exe");
                    DebugConsole.WriteLine($"[Settings] League client found automatically: {leaguePath}");
                    return leaguePath;
                }
            }

            DebugConsole.WriteLine(responseBody?.ToString() ?? "[Settings] No install response received");
            DebugConsole.WriteLine("[Settings] League client was not found automatically. Prompting for LeagueClient.exe.");
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                FileName = "LeagueClient.exe"
            };
            while (true)
            {
                if (openFileDialog.ShowDialog() == true)
                {
                    if (!string.Equals(Path.GetFileName(openFileDialog.FileName), "LeagueClient.exe",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AppMessageBox.Show("Please select a file with the name LeagueClient.exe", "Invalid Filename",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }

                    DebugConsole.WriteLine($"[Settings] League client selected manually: {openFileDialog.FileName}");
                    return openFileDialog.FileName;
                }

                DebugConsole.WriteLine("[Settings] League client selection was cancelled.");
                return string.Empty;
            }
        }
        finally
        {
            if (startedClient)
                Utils.KillLeagueFunc();
        }
    }

    public struct settings1
    {
        public string[]? DisabledPluginPaths { get; set; }
        public string LeaguePath { get; set; }
        public string riotPath { get; set; }
        public string filename { get; set; }
        public bool updates { get; set; }
        public string ReleaseChannel { get; set; }
        public bool DisplayPasswords { get; set; }
        public string settingsLocation { get; set; }
        public bool UpdateRanks { get; set; }
        public bool AccountFileEncryptionEnabled { get; set; }
        public string? AccountFileEncryptionPassword { get; set; }
        public PersistentLoginMode PersistentLoginMode { get; set; }
        public bool CloudSyncPromptShown { get; set; }
        public string LeagueDefaultSortColumn { get; set; }
        public bool LeagueDefaultSortDescending { get; set; }
        public string ValorantDefaultSortColumn { get; set; }
        public bool ValorantDefaultSortDescending { get; set; }
        public string ProfileStatusMessage { get; set; }
        public string ProfileQueue { get; set; }
        public string ProfileRank { get; set; }
        public string ProfileDivision { get; set; }
        public string ProfileIconId { get; set; }
        public string ProfileBackgroundId { get; set; }
        public bool AutoLobbyAcceptQueue { get; set; }
        public bool AutoLobbyPick { get; set; }
        public bool AutoLobbyBan { get; set; }
        public bool AutoLobbyMessage { get; set; }
        public bool AutoLobbyMute { get; set; }
        public bool UseLegacyLogin { get; set; }
        public bool AutoImportRunes { get; set; }
        public string RuneImportSource { get; set; }
        public string RuneImportRole { get; set; }
    }
}
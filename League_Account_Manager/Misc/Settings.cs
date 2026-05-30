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

public class Settings
{
    public static settings1 settingsloaded;
    public static event Action? AccountPasswordSupplied;

    public static void Save()
    {
        var copy = settingsloaded;
        copy.AccountFileEncryptionPassword = null;
        var json = JsonSerializer.Serialize(copy);
        File.WriteAllText(GetSettingsPath(), json);
    }

    public static async
        Task
        loadsettings()
    {
        var settingsPath = GetSettingsPath();
        if (File.Exists(settingsPath))
        {
            var settingstemp = File.ReadAllText(settingsPath);
            settingsloaded.filename = "Accounts";
            settingsloaded.updates = true;
            settingsloaded.DisplayPasswords = true;
            settingsloaded.UpdateRanks = true;
            settingsloaded.AccountFileEncryptionEnabled = false;
            settingsloaded.AccountFileEncryptionPassword = null;
            settingsloaded.LeagueDefaultSortColumn = "level";
            settingsloaded.LeagueDefaultSortDescending = true;
            settingsloaded.ValorantDefaultSortColumn = "valorantLevel";
            settingsloaded.ValorantDefaultSortDescending = true;
            settingsloaded = JsonConvert.DeserializeObject<settings1>(settingstemp);
            NormalizeAndMigrateAccountFileName();
            if (string.IsNullOrWhiteSpace(settingsloaded.LeagueDefaultSortColumn))
                settingsloaded.LeagueDefaultSortColumn = "level";
            if (string.IsNullOrWhiteSpace(settingsloaded.ValorantDefaultSortColumn))
                settingsloaded.ValorantDefaultSortColumn = "valorantLevel";
            if (settingsloaded.AccountFileEncryptionEnabled)
            {
                AccountFileStore.SetPassword(null);
                string? password = null;

                // Always prompt on startup when encryption is enabled.
                if (Application.Current?.Dispatcher != null)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        password = PromptForAccountFilePassword(
                            "Enter the password to decrypt your account list.");
                    });
                else
                    password = PromptForAccountFilePassword(
                        "Enter the password to decrypt your account list.");

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

            if (settingsloaded.riotPath == null)
            {
                settingsloaded.riotPath = findriot();
                Save();
            }

            if (settingsloaded.riotPath != null &&
                (settingsloaded.LeaguePath == null || settingsloaded.LeaguePath == ""))
            {
                settingsloaded.LeaguePath = await findleague();
                Save();
            }

            if (settingsloaded.settingsLocation == null)
            {
                settingsloaded.settingsLocation = await findSettings();
                Save();
            }

            Save();
        }
        else
        {
            settingsloaded.UpdateRanks = true;
            settingsloaded.filename = "Accounts";
            settingsloaded.updates = true;
            settingsloaded.DisplayPasswords = true;
            settingsloaded.AccountFileEncryptionEnabled = false;
            settingsloaded.AccountFileEncryptionPassword = null;
            settingsloaded.LeagueDefaultSortColumn = "level";
            settingsloaded.LeagueDefaultSortDescending = true;
            settingsloaded.ValorantDefaultSortColumn = "valorantLevel";
            settingsloaded.ValorantDefaultSortDescending = true;
            NormalizeAndMigrateAccountFileName();
            settingsloaded.riotPath = findriot();
            settingsloaded.LeaguePath = await findleague();
            settingsloaded.settingsLocation = await findSettings();
            Save();
        }
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

    private static string findriot()
    {
        DebugConsole.WriteLine("[Settings] Finding Riot client path...");
        string[] registryEntries =
        {
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game Riot_Client.",
            "UninstallString",

            @"HKEY_CLASSES_ROOT\riotclient\DefaultIcon",
            "(Default)",

            @"HKEY_CLASSES_ROOT\riotclient\shell\open\command",
            "(Default)",

            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            "RiotClient",

            @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\riotclient\DefaultIcon",
            "(Default)"
        };

        string? installPath = null;

        for (var i = 0; i < registryEntries.Length; i += 2)
        {
            var key = registryEntries[i];
            var valueName = registryEntries[i + 1];

            installPath = (string?)Registry.GetValue(key, valueName, null);

            if (installPath != null)
            {
                var pattern = "\"(.*?)\"";
                var match = Regex.Match(installPath, pattern);
                if (match.Success)
                    if (File.Exists(match.Groups[1].Value))
                    {
                        DebugConsole.WriteLine($"[Settings] Riot client found in registry: {match.Groups[1].Value}");
                        return match.Groups[1].Value;
                    }
            }
        }

        if (File.Exists("C:\\Riot Games\\Riot Client\\RiotClientServices.exe"))
        {
            DebugConsole.WriteLine("[Settings] Riot client found in default install path: C:\\Riot Games\\Riot Client\\RiotClientServices.exe");
            return "C:\\Riot Games\\Riot Client\\RiotClientServices.exe";
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

    private static async Task<string> findSettings()
    {
        DebugConsole.WriteLine("[Settings] Finding League settings file...");
        DebugConsole.WriteLine(Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg");
        if (File.Exists(Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg"))
        {
            DebugConsole.WriteLine($"[Settings] League settings found automatically: {Path.GetDirectoryName(settingsloaded.LeaguePath)}//Config//game.cfg");
            return Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg";
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
                return null;
                //Environment.Exit(0);
            }
    }

    private static async Task<string> findleague()
    {
        DebugConsole.WriteLine("[Settings] Finding League client path...");
        Process? riotclient = null;
        var startedclient = 0;
        if (Process.GetProcessesByName("Riot Client").Length == 0 &&
            Process.GetProcessesByName("RiotClientUx").Length == 0)
        {
            DebugConsole.WriteLine("[Settings] Riot client is not running. Launching it to detect League installation.");
            riotclient = Process.Start(settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            startedclient = 1;
        }

        var num = 0;
        while (true)
        {
            if (Process.GetProcessesByName("Riot Client").Length != 0 ||
                Process.GetProcessesByName("RiotClientUx").Length != 0) break;
            Thread.Sleep(2000);
            num++;
            if (num == 5) break;
        }

        while (true)
        {
            var readyResp = await Lcu.Connector("riot", "get", "/rso-auth/configuration/v3/ready-state", "");
            if (readyResp != null)
            {
                var readyBody = await readyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    var node = JsonNode.Parse(readyBody);
                    var ready = node?["ready"]?.GetValue<bool>() ?? false;
                    if (ready)
                        break;
                }
                catch
                {
                }
            }

            await Task.Delay(200);
        }

        DebugConsole.WriteLine("[Settings] Querying Riot client for League installation path.");
        JObject? responseBody = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            DebugConsole.WriteLine($"[Settings] League install lookup attempt {attempt}/10.");
            var resp = await Lcu.Connector("riot", "get", "/patch/v1/installs/league_of_legends.live", "");
            var responseContent = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            try
            {
                responseBody = JObject.Parse(responseContent);
                if (responseBody.ContainsKey("path"))
                    break;
            }
            catch
            {
            }

            if (attempt < 10)
                await Task.Delay(1000);
        }
        if (startedclient == 1) Utils.KillLeagueFunc();

        if (responseBody != null && responseBody.ContainsKey("path"))
        {
            var leaguePath = responseBody["path"].ToString().Replace("/", "\\") + "\\LeagueClient.exe";
            DebugConsole.WriteLine($"[Settings] League client found automatically: {leaguePath}");
            return leaguePath;
        }
        DebugConsole.WriteLine(responseBody.ToString());
        DebugConsole.WriteLine("[Settings] League client was not found automatically. Prompting for LeagueClient.exe.");
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
        openFileDialog.FileName = "LeagueClient.exe";
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "LeagueClient.exe")
                {
                    AppMessageBox.Show("Please select a file with the name LeagueClient.exe", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                DebugConsole.WriteLine($"[Settings] League client selected manually: {openFileDialog.FileName}");
                return openFileDialog.FileName;
            }
            else
            {
                DebugConsole.WriteLine("[Settings] League client selection was cancelled.");
                return null;
                //Environment.Exit(0);
            }
    }

    public struct settings1
    {
        public string LeaguePath { get; set; }
        public string riotPath { get; set; }
        public string filename { get; set; }
        public bool updates { get; set; }
        public bool DisplayPasswords { get; set; }
        public string settingsLocation { get; set; }
        public bool UpdateRanks { get; set; }
        public bool AccountFileEncryptionEnabled { get; set; }
        public string? AccountFileEncryptionPassword { get; set; }
        public string LeagueDefaultSortColumn { get; set; }
        public bool LeagueDefaultSortDescending { get; set; }
        public string ValorantDefaultSortColumn { get; set; }
        public bool ValorantDefaultSortDescending { get; set; }
    }
}
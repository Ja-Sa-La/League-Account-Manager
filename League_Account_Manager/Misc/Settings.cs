using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using League_Account_Manager.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;
using MessageBox = System.Windows.MessageBox;
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
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            settingsloaded.DisplayPasswords = true;
            settingsloaded.UpdateRanks = true;
            settingsloaded.AccountFileEncryptionEnabled = false;
            settingsloaded.AccountFileEncryptionPassword = null;
            settingsloaded = JsonConvert.DeserializeObject<settings1>(settingstemp);
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
                {
                    password = PromptForAccountFilePassword(
                        "Enter the password to decrypt your account list.");
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Account file password is required to load encrypted accounts. The application will now close.",
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
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            settingsloaded.DisplayPasswords = true;
            settingsloaded.AccountFileEncryptionEnabled = false;
            settingsloaded.AccountFileEncryptionPassword = null;
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

    private static string findriot()
    {
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
                        return match.Groups[1].Value;
            }
        }

        if (File.Exists("C:\\Riot Games\\Riot Client\\RiotClientServices.exe"))
            return "C:\\Riot Games\\Riot Client\\RiotClientServices.exe";
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
        openFileDialog.FileName = "RiotClientServices.exe";
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "RiotClientServices.exe")
                {
                    MessageBox.Show("Please select a file with the name RiotClientServices.exe.", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                return openFileDialog.FileName;
            }
            else
            {
                Environment.Exit(0);
            }
    }

    private static async Task<string> findSettings()
    {
        Console.WriteLine(Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg");
        if (File.Exists(Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg"))
            return Path.GetDirectoryName(settingsloaded.LeaguePath) + "//Config//game.cfg";
        var openFileDialog = new OpenFileDialog();
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "game.cfg")
                {
                    MessageBox.Show("Please select a file with the name game.cfg", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                return openFileDialog.FileName;
            }
            else
            {
                Environment.Exit(0);
            }
    }

    private static async Task<string> findleague()
    {
        Process? riotclient = null;
        var startedclient = 0;
        if (Process.GetProcessesByName("Riot Client").Length == 0 &&
            Process.GetProcessesByName("RiotClientUx").Length == 0)
        {
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

        var resp = await Lcu.Connector("riot", "get", "/patch/v1/installs/league_of_legends.live", "");
        JObject responseBody = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (startedclient == 1) Utils.KillLeagueFunc();

        if (responseBody != null && responseBody.ContainsKey("path"))
            return responseBody["path"].ToString().Replace("/", "\\") + "\\LeagueClient.exe";

        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
        openFileDialog.FileName = "LeagueClient.exe";
        while (true)
            if (openFileDialog.ShowDialog() == true)
            {
                if (Path.GetFileName(openFileDialog.FileName) != "LeagueClient.exe")
                {
                    MessageBox.Show("Please select a file with the name LeagueClient.exe", "Invalid Filename",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                return openFileDialog.FileName;
            }
            else
            {
                Environment.Exit(0);
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
    }
}
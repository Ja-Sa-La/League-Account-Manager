using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace League_Account_Manager;

public class Settings
{
    public static settings1 settingsloaded;

    public static async
        Task
        loadsettings()
    {
        if (File.Exists(Directory.GetCurrentDirectory() + "/Settings.json"))
        {
            var settingstemp = File.ReadAllText(Directory.GetCurrentDirectory() + "/Settings.json");
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            settingsloaded = JsonConvert.DeserializeObject<settings1>(settingstemp);
            if (settingsloaded.riotPath == null)
            {
                settingsloaded.riotPath = findriot();
                var json = JsonSerializer.Serialize(settingsloaded);
                File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
            }

            if (settingsloaded.riotPath != null && settingsloaded.LeaguePath == null)
            {
                settingsloaded.LeaguePath = await findleague();
                var json = JsonSerializer.Serialize(settingsloaded);
                File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
            }
        }
        else
        {
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            settingsloaded.riotPath = findriot();
            settingsloaded.LeaguePath = await findleague();
            var json = JsonSerializer.Serialize(settingsloaded);
            File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
        }
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

        string installPath = null;

        for (var i = 0; i < registryEntries.Length; i += 2)
        {
            var key = registryEntries[i];
            var valueName = registryEntries[i + 1];

            installPath = (string)Registry.GetValue(key, valueName, null);

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

    private static async Task<string> findleague()
    {
        Process riotclient = null;
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

        var resp = await lcu.Connector("riot", "get", "/patch/v1/installs/league_of_legends.live", "");
        JObject responseBody = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (startedclient == 1 && riotclient != null) riotclient.Kill();

        if (responseBody.ContainsKey("path"))
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
    }
}
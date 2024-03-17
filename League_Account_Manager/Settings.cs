using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace League_Account_Manager;

public class Settings
{
    public static settings1 settingsloaded;

    public static void loadsettings()
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
        }
        else
        {
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            settingsloaded.riotPath = findriot();
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

    public struct settings1
    {
        public string riotPath { get; set; }
        public string filename { get; set; }
        public bool updates { get; set; }
    }
}
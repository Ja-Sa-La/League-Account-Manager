using System.IO;
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
        }
        else
        {
            settingsloaded.filename = "List";
            settingsloaded.updates = true;
            var json = JsonSerializer.Serialize(settingsloaded);
            File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
        }
    }

    public struct settings1
    {
        public string filename { get; set; }
        public bool updates { get; set; }
    }
}
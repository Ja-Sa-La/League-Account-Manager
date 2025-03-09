using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IniParser;
using IniParser.Model;
using League_Account_Manager.Misc;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using static League_Account_Manager.Misc.Utils;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for SettingsEditor.xaml
/// </summary>
public partial class SettingsEditor : Page
{
    public SettingsEditor()
    {
        InitializeComponent();
        try
        {
            settings = LoadSettings(Misc.Settings.settingsloaded.settingsLocation);
            originalSettings = CloneSettings(settings); // Clone to ensure it's a separate copy
            DataContext = settings;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    public SettingsIngame settings { get; set; }
    public SettingsIngame originalSettings { get; set; }

    // Method to create a deep copy of the settings object
    public static SettingsIngame CloneSettings(SettingsIngame settingsToClone)
    {
        try
        {
            var json = JsonSerializer.Serialize(settingsToClone);
            return JsonSerializer.Deserialize<SettingsIngame>(json);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return new SettingsIngame();

    }

    public static SettingsIngame LoadSettings(string iniFilePath)
    {
        try
        {
            var parser = new FileIniDataParser();
            var data = parser.ReadFile(iniFilePath);

            var settings = new SettingsIngame();

            // Get all properties of the Settings class
            foreach (var property in settings.GetType().GetProperties())
            {
                // Check if property has a matching section in the INI file
                var section = data[property.Name];
                if (section != null)
                    // Iterate through all keys in the section and map them to the object's properties
                    foreach (var key in section)
                    {
                        var keyName = key.KeyName;
                        var prop = property.PropertyType.GetProperty(keyName);
                        if (prop != null)
                        {
                            object value = null;
                            if (prop.PropertyType == typeof(bool))
                                value = key.Value == "1" || key.Value.ToLower() == "true";
                            else if (prop.PropertyType == typeof(float))
                                value = float.Parse(key.Value, CultureInfo.InvariantCulture);
                            else if (prop.PropertyType == typeof(double))
                                value = double.Parse(key.Value, CultureInfo.InvariantCulture);
                            else
                                value = Convert.ChangeType(key.Value, prop.PropertyType);

                            if (value != null) prop.SetValue(property.GetValue(settings), value);
                        }
                    }
            }

            return settings;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return new SettingsIngame();

    }

    public  void SaveSettings(SettingsIngame settings, string iniFilePath, string PersistedSettings)
    {
        try
        {
            UpdateSettings(PersistedSettings, JsonSerializer.Serialize(settings));

            var parser = new FileIniDataParser();
            var data = new IniData();

            foreach (var property in settings.GetType().GetProperties())
            {
                var sectionName = property.Name;

                if (!data.Sections.ContainsSection(sectionName)) data.Sections.AddSection(sectionName);

                var section = data[sectionName];

                var subProperties = property.PropertyType.GetProperties();
                if (subProperties.Length > 0)
                {
                    var sectionInstance = property.GetValue(settings);
                    foreach (var subProp in subProperties)
                        if (sectionInstance != null)
                        {
                            var keyName = subProp.Name;
                            var value = subProp.GetValue(sectionInstance);

                            // Convert values properly
                            var stringValue = value switch
                            {
                                bool boolValue => boolValue ? "1" : "0", // Convert bools to 1/0
                                float floatValue => floatValue.ToString(CultureInfo
                                    .InvariantCulture), // Ensure "." for decimals
                                double doubleValue => doubleValue.ToString(CultureInfo
                                    .InvariantCulture), // Ensure "." for decimals
                                _ => value?.ToString() ?? ""
                            };

                            section.AddKey(keyName, stringValue);
                        }
                }
                else
                {
                    var value = property.GetValue(settings);
                    var stringValue = value switch
                    {
                        bool boolValue => boolValue ? "1" : "0",
                        float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
                        double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
                        _ => value?.ToString() ?? ""
                    };

                    section.AddKey("Value", stringValue);
                }
            }

            parser.WriteFile(iniFilePath, data);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }


    // Reset settings to original values
    private void ResetSettings()
    {
        try
        {
            settings = CloneSettings(originalSettings); // Use the clone for reset
            DataContext = null; // Reset DataContext first
            DataContext = settings; // Re-assign the reset settings
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    // Export settings to a JSON file
    private void ExportSettings()
    {
        try
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = "settings_export.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var filePath = saveFileDialog.FileName;

                // Serialize settings to JSON
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

                // Write the JSON data to the selected file path
                File.WriteAllText(filePath, json);

                MessageBox.Show("Settings exported as JSON.");
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    // Import settings from a JSON file
    private void ImportSettings()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Select the settings file to import"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;

                // Read the JSON file contents
                var json = File.ReadAllText(filePath);

                // Deserialize the settings from the JSON string
                settings = JsonSerializer.Deserialize<SettingsIngame>(json);

                // Update the DataContext to reflect the imported settings
                DataContext = settings;

                MessageBox.Show("Settings imported from JSON.");
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    // Event handlers (you can bind them to buttons, for example)
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetSettings();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportSettings();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        ImportSettings();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try

        {
            
            
            var settFile = new FileInfo(Misc.Settings.settingsloaded.settingsLocation);
                settFile.IsReadOnly = false;
                var settFile2 = new FileInfo(Path.GetDirectoryName(Misc.Settings.settingsloaded.settingsLocation) + "//PersistedSettings.json");
                settFile2.IsReadOnly = false;
            SaveSettings(settings, Misc.Settings.settingsloaded.settingsLocation, Path.GetDirectoryName(Misc.Settings.settingsloaded.settingsLocation) + "//PersistedSettings.json");
            settFile.IsReadOnly = true;
            settFile2.IsReadOnly = true;
            Process.Start(Misc.Settings.settingsloaded.riotPath,
                    "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");
            Thread.Sleep(1000);
                killleaguefunc2();
                await lcu.Connector("riot", "post",
                    "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
            
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    private async void ApplyButton2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string payload = JsonSerializer.Serialize(settings);
            string payload2 = JsonSerializer.Serialize(settings);
            string payload3 = JsonSerializer.Serialize(settings);

            dynamic resp = await lcu.Connector("league", "PATCH", "/lol-game-settings/v1/game-settings", payload);
            resp = await lcu.Connector("league", "PATCH", "/lol-settings/v1/account/game-settings", payload2);
            resp = await lcu.Connector("league", "PATCH", "/lol-settings/v2/account/GamePreferences/game-settings",
                payload3);
            Process.Start(Misc.Settings.settingsloaded.riotPath,
                "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");
            Thread.Sleep(1000);
            killleaguefunc2();
            await lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settFile = new FileInfo(Misc.Settings.settingsloaded.settingsLocation);
            if (!settFile.IsReadOnly) settFile.IsReadOnly = true;
            var settFile2 = new FileInfo(Path.GetDirectoryName(Misc.Settings.settingsloaded.settingsLocation) + "//PersistedSettings.json");
            if (!settFile2.IsReadOnly) settFile2.IsReadOnly = true;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settFile = new FileInfo(Misc.Settings.settingsloaded.settingsLocation);
            if (settFile.IsReadOnly) settFile.IsReadOnly = false;
            var settFile2 = new FileInfo(Path.GetDirectoryName(Misc.Settings.settingsloaded.settingsLocation) + "//PersistedSettings.json");
            if (settFile2.IsReadOnly) settFile2.IsReadOnly = false;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }

     void UpdateSettings(string filePath, string newSettingsJson)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("Settings file not found.");
            return;
        }

        // Load existing JSON
        string existingJson = File.ReadAllText(filePath);
        JObject existingSettings = JObject.Parse(existingJson);
        JObject newSettings = JObject.Parse(newSettingsJson);

        EditKeyValuePairs(existingSettings);
        Console.WriteLine(existingSettings);
        // Save the updated settings back to the file
        File.WriteAllText(filePath, existingSettings.ToString());

        Console.WriteLine("Settings updated successfully.");
    }

    // Recursively update the template JSON with values from the input JSON
    public void EditKeyValuePairs(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (property.Name == "files" && property.Value is JArray files)
                {
                    // Process each file
                    foreach (var file in files)
                    {
                        // Iterate over the sections within each file
                        var sections = file["sections"] as JArray;
                        foreach (var section in sections)
                        {
                            string sectionName = section["name"]?.ToString();
                            // Now edit settings under the section
                            var settings = section["settings"];
                            EditSettingsWithSection(settings, sectionName);
                        }
                    }
                }
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
            {
                EditKeyValuePairs(item);
            }
        }
    }

    private void EditSettingsWithSection(JToken settingsher, string sectionName)
    {
        foreach (var setting in settingsher)
        {
            string name = setting["name"]?.ToString();
            string newValue = GetSettingValueBySectionName(settings, sectionName, name);

            if (!string.IsNullOrWhiteSpace(newValue))
            {
                setting["value"] = newValue;  // Update the value with user input
            }
        }
    }

    private string GetSettingValueBySectionName(SettingsIngame settings, string sectionName, string name)
    {
        switch (sectionName)
        {
            case nameof(SettingsIngame.FloatingText):
                return settings.FloatingText.GetType().GetProperty(name)?.GetValue(settings.FloatingText)?.ToString();
            case nameof(SettingsIngame.General):
                return settings.General.GetType().GetProperty(name)?.GetValue(settings.General)?.ToString();
            case nameof(SettingsIngame.HUD):
                return settings.HUD.GetType().GetProperty(name)?.GetValue(settings.HUD)?.ToString();
            case nameof(SettingsIngame.LossOfControl):
                return settings.LossOfControl.GetType().GetProperty(name)?.GetValue(settings.LossOfControl)?.ToString();
            case nameof(SettingsIngame.Performance):
                return settings.Performance.GetType().GetProperty(name)?.GetValue(settings.Performance)?.ToString();
            case nameof(SettingsIngame.Voice):
                return settings.Voice.GetType().GetProperty(name)?.GetValue(settings.Voice)?.ToString();
            case nameof(SettingsIngame.Volume):
                return settings.Volume.GetType().GetProperty(name)?.GetValue(settings.Volume)?.ToString();
            case nameof(SettingsIngame.MapSkinOptions):
                return settings.MapSkinOptions.GetType().GetProperty(name)?.GetValue(settings.MapSkinOptions)?.ToString();
            case nameof(SettingsIngame.TFT):
                return settings.TFT.GetType().GetProperty(name)?.GetValue(settings.TFT)?.ToString();
            case nameof(SettingsIngame.Replay):
                return settings.Replay.GetType().GetProperty(name)?.GetValue(settings.Replay)?.ToString();
            case nameof(SettingsIngame.Mobile):
                return settings.Mobile.GetType().GetProperty(name)?.GetValue(settings.Mobile)?.ToString();
            case nameof(SettingsIngame.Swarm):
                return settings.Swarm.GetType().GetProperty(name)?.GetValue(settings.Swarm)?.ToString();
            case nameof(SettingsIngame.Highlights):
                return settings.Highlights.GetType().GetProperty(name)?.GetValue(settings.Highlights)?.ToString();
            case nameof(SettingsIngame.ItemShop):
                return settings.ItemShop.GetType().GetProperty(name)?.GetValue(settings.ItemShop)?.ToString();
            case nameof(SettingsIngame.Chat):
                return settings.Chat.GetType().GetProperty(name)?.GetValue(settings.Chat)?.ToString();
            default:
                return null;
        }
    }
}
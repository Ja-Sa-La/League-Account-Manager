using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IniParser;
using IniParser.Model;
using Microsoft.Win32;
using NLog;
using static League_Account_Manager.Utils;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page11.xaml
/// </summary>
public partial class Page11 : Page
{
    public Page11()
    {
        InitializeComponent();
        try
        {
            settings = LoadSettings(Settings.settingsloaded.settingsLocation);
            Console.WriteLine(JsonSerializer.Serialize(settings));
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

    public static void SaveSettings(SettingsIngame settings, string iniFilePath)
    {
        try
        {
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
            var settFile = new FileInfo(Settings.settingsloaded.settingsLocation);
                settFile.IsReadOnly = false;
                SaveSettings(settings, Settings.settingsloaded.settingsLocation);
                settFile.IsReadOnly = true;
                Process.Start(Settings.settingsloaded.riotPath,
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
                resp = await lcu.Connector("league", "PATCH", "/lol-settings/v2/account/GamePreferences/game-settings", payload3);
                Process.Start(Settings.settingsloaded.riotPath,
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
            var settFile = new FileInfo(Settings.settingsloaded.settingsLocation);
            if (!settFile.IsReadOnly) settFile.IsReadOnly = true;

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
            var settFile = new FileInfo(Settings.settingsloaded.settingsLocation);
            if (settFile.IsReadOnly) settFile.IsReadOnly = false;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

    }
}
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IniParser;
using IniParser.Model;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using static League_Account_Manager.Misc.Utils;

namespace League_Account_Manager.views;

public partial class SettingsEditor : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly List<ExtraSection> _extraSections = new();

    public SettingsEditor()
    {
        InitializeComponent();
        LoadInitialSettings();
        GenerateDynamicUI();
    }

    public SettingsIngame settings { get; set; } = new();
    private SettingsIngame originalSettings { get; set; } = new();

    private void GenerateDynamicUI()
    {
        // Clear existing tabs
        DynamicTabControl.Items.Clear();

        // Iterate through all top-level sections in SettingsIngame
        foreach (var sectionProp in settings.GetType().GetProperties())
        {
            var sectionName = sectionProp.Name;
            var sectionValue = sectionProp.GetValue(settings);

            var tab = new TabItem { Header = sectionName };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(5) };

            foreach (var prop in sectionProp.PropertyType.GetProperties())
            {
                // Create label
                var label = new TextBlock
                {
                    Text = prop.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                UIElement? control = null;

                // Boolean
                if (prop.PropertyType == typeof(bool))
                {
                    var cb = new CheckBox
                    {
                        Content = prop.Name,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(prop.Name)
                    {
                        Source = sectionValue,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    control = cb;
                }
                // Numeric
                else if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(float) ||
                         prop.PropertyType == typeof(double))
                {
                    var slider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 10,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    slider.SetBinding(Slider.ValueProperty, new Binding(prop.Name)
                    {
                        Source = sectionValue,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });

                    var valText = new TextBlock
                    {
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    valText.SetBinding(TextBlock.TextProperty, new Binding(prop.Name)
                    {
                        Source = sectionValue,
                        Mode = BindingMode.OneWay,
                        StringFormat = "0.##"
                    });

                    stack.Children.Add(label);
                    stack.Children.Add(slider);
                    stack.Children.Add(valText);
                    continue; // Already added control
                }
                // Enum
                else if (prop.PropertyType.IsEnum)
                {
                    var combo = new ComboBox
                    {
                        ItemsSource = Enum.GetNames(prop.PropertyType),
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(prop.Name)
                    {
                        Source = sectionValue,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    control = combo;
                }
                // String / other
                else
                {
                    var tb = new TextBox
                    {
                        Width = 200,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    tb.SetBinding(TextBox.TextProperty, new Binding(prop.Name)
                    {
                        Source = sectionValue,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    control = tb;
                }

                // Add to stack
                stack.Children.Add(label);
                stack.Children.Add(control);
            }

            scroll.Content = stack;
            tab.Content = scroll;
            DynamicTabControl.Items.Add(tab);
        }

        // Add dynamic tabs for any extra sections not mapped to SettingsIngame
        foreach (var extra in _extraSections)
        {
            var tab = new TabItem { Header = extra.Name };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(5) };

            foreach (var setting in extra.Settings)
            {
                var label = new TextBlock
                {
                    Text = setting.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                var tb = new TextBox
                {
                    Width = 200,
                    Margin = new Thickness(0, 2, 0, 2),
                    Text = setting.Value
                };
                tb.TextChanged += (s, e) => setting.Value = tb.Text;

                stack.Children.Add(label);
                stack.Children.Add(tb);
            }

            scroll.Content = stack;
            tab.Content = scroll;
            DynamicTabControl.Items.Add(tab);
        }
    }

    private void LoadInitialSettings()
    {
        try
        {
            var iniPath = Misc.Settings.settingsloaded.settingsLocation;
            var persistedPath = Path.Combine(Path.GetDirectoryName(iniPath) ?? Directory.GetCurrentDirectory(),
                "PersistedSettings.json");
            var inputPath = Path.Combine(Path.GetDirectoryName(iniPath) ?? Directory.GetCurrentDirectory(),
                "Input.ini");

            SettingsIngame? loaded = null;
            _extraSections.Clear();

            if (File.Exists(iniPath))
            {
                loaded = LoadSettings(iniPath);
                AppendExtraSectionsFromIni(iniPath, "Game.cfg");
            }
            else
            {
                Logger.Warn("Game.cfg not found at {0}", iniPath);
            }

            if (File.Exists(inputPath))
                AppendExtraSectionsFromIni(inputPath, "Input.ini");
            else
                Logger.Warn("Input.ini not found at {0}", inputPath);

            if (File.Exists(persistedPath))
                try
                {
                    var persisted = LoadPersistedSettingsFromJson(persistedPath);
                    if (loaded == null)
                        loaded = persisted;
                    Logger.Info("Loaded persisted settings JSON at {0}", persistedPath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to load persisted settings JSON");
                }
            else
                Logger.Warn("PersistedSettings.json not found at {0}", persistedPath);

            settings = loaded ?? new SettingsIngame();
            originalSettings = CloneSettings(settings);
            DataContext = settings;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load initial settings");
            AppMessageBox.Show("Error loading settings: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AppendExtraSectionsFromIni(string iniPath, string fileName)
    {
        var parser = new FileIniDataParser();
        var data = parser.ReadFile(iniPath);
        var knownSections = new HashSet<string>(
            typeof(SettingsIngame).GetProperties().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var section in data.Sections)
        {
            if (fileName.Equals("Game.cfg", StringComparison.OrdinalIgnoreCase) &&
                knownSections.Contains(section.SectionName))
                continue;

            var extra = _extraSections.FirstOrDefault(x =>
                x.Name.Equals(section.SectionName, StringComparison.OrdinalIgnoreCase) &&
                x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (extra == null)
            {
                extra = new ExtraSection { Name = section.SectionName, FileName = fileName };
                _extraSections.Add(extra);
            }

            foreach (var key in section.Keys)
                extra.Settings.Add(new ExtraSetting { Name = key.KeyName, Value = key.Value ?? string.Empty });
        }
    }

    public class MySection : INotifyPropertyChanged
    {
        private bool _someBool;

        public bool SomeBool
        {
            get => _someBool;
            set
            {
                _someBool = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
        }
    }

    #region Settings Load / Save / Clone

    public static SettingsIngame CloneSettings(SettingsIngame s)
    {
        try
        {
            var json = JsonSerializer.Serialize(s);
            return JsonSerializer.Deserialize<SettingsIngame>(json) ?? new SettingsIngame();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to clone settings");
            return new SettingsIngame();
        }
    }

    public static SettingsIngame LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new SettingsIngame();

            var parser = new FileIniDataParser();
            var data = parser.ReadFile(path);
            var s = new SettingsIngame();

            foreach (var property in s.GetType().GetProperties())
            {
                var section = data[property.Name];
                if (section == null) continue;

                foreach (var key in section)
                {
                    var prop = property.PropertyType.GetProperty(
                        key.KeyName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null) continue;

                    var keyValue = key.Value ?? string.Empty;
                    var value = keyValue switch
                    {
                        "1" => true,
                        "0" => false,
                        _ when prop.PropertyType == typeof(bool) => bool.Parse(keyValue),
                        _ when prop.PropertyType == typeof(float) => float.Parse(keyValue,
                            CultureInfo.InvariantCulture),
                        _ when prop.PropertyType == typeof(double) => double.Parse(keyValue,
                            CultureInfo.InvariantCulture),
                        _ when prop.PropertyType.IsEnum => Enum.Parse(prop.PropertyType, keyValue, true),
                        _ => Convert.ChangeType(keyValue, prop.PropertyType, CultureInfo.InvariantCulture)
                    };

                    prop.SetValue(property.GetValue(s), value);
                }
            }

            return s;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load settings from INI");
            return new SettingsIngame();
        }
    }

    private SettingsIngame LoadPersistedSettingsFromJson(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var s = new SettingsIngame();

        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return s;

        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
                continue;
            var fileName = file.TryGetProperty("name", out var fileNameProp) &&
                           fileNameProp.ValueKind == JsonValueKind.String
                ? fileNameProp.GetString()
                : "Game.cfg";

            foreach (var section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("name", out var sectionNameProp) ||
                    sectionNameProp.ValueKind != JsonValueKind.String)
                    continue;

                var sectionName = sectionNameProp.GetString();
                if (string.IsNullOrWhiteSpace(sectionName)) continue;

                var sectionProp = s.GetType().GetProperty(sectionName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var sectionInstance = sectionProp?.GetValue(s);

                if (!section.TryGetProperty("settings", out var settingsArr) ||
                    settingsArr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var setting in settingsArr.EnumerateArray())
                {
                    if (!setting.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
                        continue;
                    if (!setting.TryGetProperty("value", out var valueProp))
                        continue;

                    var settingName = nameProp.GetString();
                    var valueStr = valueProp.GetString();

                    if (string.IsNullOrWhiteSpace(settingName)) continue;

                    if (sectionProp != null && sectionInstance != null)
                    {
                        var prop = sectionProp.PropertyType.GetProperty(settingName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (prop != null)
                            try
                            {
                                var converted = ConvertStringToType(valueStr, prop.PropertyType);
                                prop.SetValue(sectionInstance, converted);
                                continue;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn(ex, "Failed to convert setting {0}.{1} with value {2}", sectionName,
                                    settingName, valueStr);
                            }
                    }

                    // Fallback: store unmatched section/settings for display
                    var extra = _extraSections.FirstOrDefault(x =>
                        x.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase) &&
                        x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (extra == null)
                    {
                        extra = new ExtraSection { Name = sectionName, FileName = fileName ?? "Game.cfg" };
                        _extraSections.Add(extra);
                    }

                    extra.Settings.Add(new ExtraSetting { Name = settingName, Value = valueStr ?? string.Empty });
                }
            }
        }

        return s;
    }

    private object ConvertStringToType(string? value, Type targetType)
    {
        value ??= string.Empty;
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(bool))
        {
            if (value == "1") return true;
            if (value == "0") return false;
            if (bool.TryParse(value, out var b)) return b;
            return false;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var i)) return i;
            return 0;
        }

        if (targetType == typeof(float))
        {
            if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var f)) return f;
            return 0f;
        }

        if (targetType == typeof(double))
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            return 0d;
        }

        if (targetType.IsEnum)
            try
            {
                return Enum.Parse(targetType, value, true);
            }
            catch
            {
                return Activator.CreateInstance(targetType);
            }

        // fallback
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    public void SaveSettings(SettingsIngame s, string iniPath, string persistedPath)
    {
        try
        {
            // Persist JSON in the same structure as the example file
            var persistedJson = BuildPersistedSettingsJson(s, _extraSections);
            WriteTextWithReadOnlyToggle(persistedPath, persistedJson);

            // Also write INI for compatibility
            var parser = new FileIniDataParser();
            var data = new IniData();

            foreach (var property in s.GetType().GetProperties())
            {
                if (!data.Sections.ContainsSection(property.Name))
                    data.Sections.AddSection(property.Name);

                var section = data[property.Name];
                var subProps = property.PropertyType.GetProperties();

                if (subProps.Length > 0)
                {
                    var sectionInstance = property.GetValue(s);
                    foreach (var sub in subProps)
                    {
                        if (sectionInstance == null) continue;
                        section.AddKey(sub.Name, FormatValue(sub.GetValue(sectionInstance)));
                    }
                }
                else
                {
                    section.AddKey("Value", FormatValue(property.GetValue(s)));
                }
            }

            WriteIniWithReadOnlyToggle(iniPath, parser, data);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save settings");
        }
    }

    private static string FormatValue(object? val)
    {
        return val switch
        {
            bool b => b ? "1" : "0",
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => val?.ToString() ?? ""
        };
    }

    private string BuildPersistedSettingsJson(SettingsIngame s, List<ExtraSection> extras)
    {
        var files = new List<object>();

        // Primary file Game.cfg with known sections
        var gameSections = new List<object>();
        foreach (var sectionProp in s.GetType().GetProperties())
        {
            var sectionInstance = sectionProp.GetValue(s);
            if (sectionInstance == null) continue;

            var settingsList = new List<object>();
            foreach (var prop in sectionProp.PropertyType.GetProperties())
            {
                var valObj = prop.GetValue(sectionInstance);
                settingsList.Add(new
                {
                    name = prop.Name,
                    value = FormatValueJson(valObj)
                });
            }

            gameSections.Add(new
            {
                name = sectionProp.Name,
                settings = settingsList
            });
        }

        // Include extra sections grouped by file
        var extrasByFile = extras.GroupBy(x => string.IsNullOrWhiteSpace(x.FileName) ? "Game.cfg" : x.FileName);

        // Add known game sections to Game.cfg file
        files.Add(new
        {
            name = "Game.cfg",
            sections = gameSections
        });

        foreach (var group in extrasByFile)
        {
            // If extras belong to Game.cfg and it's already created, append to that sections list instead of new file
            if (string.Equals(group.Key, "Game.cfg", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var extra in group)
                    gameSections.Add(new
                    {
                        name = extra.Name,
                        settings = extra.Settings.Select(es => new { name = es.Name, value = es.Value }).ToList()
                    });
                continue;
            }

            var sections = group.Select(ex => new
            {
                name = ex.Name,
                settings = ex.Settings.Select(es => new { name = es.Name, value = es.Value }).ToList()
            }).ToList();

            files.Add(new
            {
                name = group.Key, sections
            });
        }

        var root = new
        {
            description =
                "The settings in this file are persisted server-side. This file is generated automatically. Editing it is not recommended. Modify SettingsToPersist.json to specify which settings are persisted.",
            files
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private string FormatValueJson(object? val)
    {
        if (val == null) return "";
        return val switch
        {
            bool b => b ? "1" : "0",
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => val.ToString() ?? ""
        };
    }

    private void WriteTextWithReadOnlyToggle(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());

            var fi = new FileInfo(path);
            var wasReadOnly = fi.Exists && fi.IsReadOnly;
            if (wasReadOnly) fi.IsReadOnly = false;

            File.WriteAllText(path, content);

            if (wasReadOnly) fi.IsReadOnly = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to write file {0}", path);
            throw;
        }
    }

    private void WriteIniWithReadOnlyToggle(string path, FileIniDataParser parser, IniData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());

            var fi = new FileInfo(path);
            var wasReadOnly = fi.Exists && fi.IsReadOnly;
            if (wasReadOnly) fi.IsReadOnly = false;

            parser.WriteFile(path, data);

            if (wasReadOnly) fi.IsReadOnly = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to write INI file {0}", path);
            throw;
        }
    }

    #endregion

    #region Extra section models

    private class ExtraSection
    {
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = "Game.cfg";
        public List<ExtraSetting> Settings { get; } = new();
    }

    private class ExtraSetting
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    #endregion

    #region PersistedSettings JSON

    private void UpdatePersistedSettings(string filePath, string newJson)
    {
        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, newJson);
            return;
        }

        var existing = JObject.Parse(File.ReadAllText(filePath));
        var updated = JObject.Parse(newJson);

        ApplySettingsToJToken(existing, updated);

        File.WriteAllText(filePath, existing.ToString());
    }

    private void ApplySettingsToJToken(JToken existing, JToken updated)
    {
        if (existing is JObject obj && updated is JObject upd)
            foreach (var prop in obj.Properties())
                if (upd[prop.Name] != null)
                    prop.Value = upd[prop.Name];
    }

    #endregion

    #region Button Handlers

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        settings = CloneSettings(originalSettings);
        DataContext = null;
        DataContext = settings;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON Files|*.json",
            FileName = "PersistedSettings.json"
        };
        if (sfd.ShowDialog() == true)
            File.WriteAllText(sfd.FileName,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "JSON Files|*.json",
            Title = "Select settings file to import"
        };
        if (ofd.ShowDialog() != true) return;

        settings = JsonSerializer.Deserialize<SettingsIngame>(File.ReadAllText(ofd.FileName)) ?? new SettingsIngame();
        DataContext = null;
        DataContext = settings;
    }

    private async void OnApplyToClientClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var iniPath = Misc.Settings.settingsloaded.settingsLocation;
            var persistedPath = Path.Combine(Path.GetDirectoryName(iniPath) ?? Directory.GetCurrentDirectory(),
                "PersistedSettings.json");

            SaveSettings(settings, iniPath, persistedPath);

            Process.Start(Misc.Settings.settingsloaded.riotPath,
                "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");

            Thread.Sleep(1000);
            KillLeagueFunc2();

            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to apply settings to client");
        }
    }

    private async void OnApplyToAccountClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var payload = JsonSerializer.Serialize(settings);

            await Lcu.Connector("league", "PATCH", "/lol-game-settings/v1/game-settings", payload);
            await Lcu.Connector("league", "PATCH", "/lol-settings/v1/account/game-settings", payload);
            await Lcu.Connector("league", "PATCH", "/lol-settings/v2/account/GamePreferences/game-settings", payload);

            Process.Start(Misc.Settings.settingsloaded.riotPath,
                "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");

            Thread.Sleep(1000);
            KillLeagueFunc2();

            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to apply settings to account");
        }
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        SetReadOnly(true);
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        SetReadOnly(false);
    }

    private void SetReadOnly(bool readOnly)
    {
        try
        {
            var iniPath = Misc.Settings.settingsloaded.settingsLocation;
            var persistedPath = Path.Combine(Path.GetDirectoryName(iniPath) ?? Directory.GetCurrentDirectory(),
                "PersistedSettings.json");

            new FileInfo(iniPath).IsReadOnly = readOnly;
            new FileInfo(persistedPath).IsReadOnly = readOnly;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to change read-only state");
        }
    }

    #endregion
}
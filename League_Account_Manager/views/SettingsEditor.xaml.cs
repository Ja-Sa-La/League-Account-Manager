using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IniParser;
using IniParser.Model;
using League_Account_Manager.Misc;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NLog;
using static League_Account_Manager.Misc.Utils;

namespace League_Account_Manager.views
{
    public partial class SettingsEditor : Page
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public SettingsIngame settings { get; set; }
        private SettingsIngame originalSettings { get; set; }

        public SettingsEditor()
        {
            InitializeComponent();
            LoadInitialSettings();
            GenerateDynamicUI();
        }

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

                    UIElement control = null;

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
                    else if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double))
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
        }
        public class MySection : INotifyPropertyChanged
        {
            private bool _someBool;
            public bool SomeBool
            {
                get => _someBool;
                set { _someBool = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        private void LoadInitialSettings()
        {
            try
            {
                settings = LoadSettings(Misc.Settings.settingsloaded.settingsLocation);
                originalSettings = CloneSettings(settings);
                DataContext = settings;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load initial settings");
                MessageBox.Show("Error loading settings: " + ex.Message);
            }
        }

        #region Settings Load / Save / Clone

        public static SettingsIngame CloneSettings(SettingsIngame s)
        {
            try
            {
                var json = JsonSerializer.Serialize(s);
                return JsonSerializer.Deserialize<SettingsIngame>(json);
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
                        var prop = property.PropertyType.GetProperty(key.KeyName);
                        if (prop == null) continue;

                        object value = key.Value switch
                        {
                            "1" => true,
                            "0" => false,
                            _ when prop.PropertyType == typeof(bool) => bool.Parse(key.Value),
                            _ when prop.PropertyType == typeof(float) => float.Parse(key.Value, CultureInfo.InvariantCulture),
                            _ when prop.PropertyType == typeof(double) => double.Parse(key.Value, CultureInfo.InvariantCulture),
                            _ => Convert.ChangeType(key.Value, prop.PropertyType)
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

        public void SaveSettings(SettingsIngame s, string iniPath, string persistedPath)
        {
            try
            {
                UpdatePersistedSettings(persistedPath, JsonSerializer.Serialize(s));

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

                parser.WriteFile(iniPath, data);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save settings");
            }
        }

        private static string FormatValue(object val) => val switch
        {
            bool b => b ? "1" : "0",
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => val?.ToString() ?? ""
        };

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
            {
                foreach (var prop in obj.Properties())
                {
                    if (upd[prop.Name] != null)
                        prop.Value = upd[prop.Name];
                }
            }
        }

        #endregion

        #region Button Handlers

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            settings = CloneSettings(originalSettings);
            DataContext = null;
            DataContext = settings;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON Files|*.json",
                FileName = "PersistedSettings.json"
            };
            if (sfd.ShowDialog() == true)
                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Files|*.json",
                Title = "Select settings file to import"
            };
            if (ofd.ShowDialog() != true) return;

            settings = JsonSerializer.Deserialize<SettingsIngame>(File.ReadAllText(ofd.FileName));
            DataContext = null;
            DataContext = settings;
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string iniPath = Misc.Settings.settingsloaded.settingsLocation;
                string persistedPath = Path.Combine(Path.GetDirectoryName(iniPath), "PersistedSettings.json");

                SaveSettings(settings, iniPath, persistedPath);

                Process.Start(Misc.Settings.settingsloaded.riotPath,
                    "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");

                Thread.Sleep(1000);
                killleaguefunc2();

                await Lcu.Connector("riot", "post",
                    "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply settings to client");
            }
        }

        private async void ApplyButton2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string payload = JsonSerializer.Serialize(settings);

                await Lcu.Connector("league", "PATCH", "/lol-game-settings/v1/game-settings", payload);
                await Lcu.Connector("league", "PATCH", "/lol-settings/v1/account/game-settings", payload);
                await Lcu.Connector("league", "PATCH", "/lol-settings/v2/account/GamePreferences/game-settings", payload);

                Process.Start(Misc.Settings.settingsloaded.riotPath,
                    "--launch-product=Riot Client --launch-patchline=KeystoneFoundationLiveWin");

                Thread.Sleep(1000);
                killleaguefunc2();

                await Lcu.Connector("riot", "post",
                    "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply settings to account");
            }
        }

        private void LockButton_Click(object sender, RoutedEventArgs e) => SetReadOnly(true);
        private void UnlockButton_Click(object sender, RoutedEventArgs e) => SetReadOnly(false);

        private void SetReadOnly(bool readOnly)
        {
            try
            {
                var iniPath = Misc.Settings.settingsloaded.settingsLocation;
                var persistedPath = Path.Combine(Path.GetDirectoryName(iniPath), "PersistedSettings.json");

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
}
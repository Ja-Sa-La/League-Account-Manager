using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : Page
{
    public Settings()
    {
        InitializeComponent();
        settingssaveinfobox.Text = Misc.Settings.settingsloaded.filename;
        savesettingsupdates.IsChecked = Misc.Settings.settingsloaded.updates;
        DisplayPasswords.IsChecked = Misc.Settings.settingsloaded.DisplayPasswords;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Misc.Settings.settingsloaded.filename = settingssaveinfobox.Text;
        if (savesettingsupdates.IsChecked != false)
            Misc.Settings.settingsloaded.updates = true;
        else
            Misc.Settings.settingsloaded.updates = false;
        if (DisplayPasswords.IsChecked != false)
            Misc.Settings.settingsloaded.DisplayPasswords = true;
        else
            Misc.Settings.settingsloaded.DisplayPasswords = false;
        var json = JsonSerializer.Serialize(Misc.Settings.settingsloaded);
        File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
        Process.Start(Process.GetCurrentProcess().MainModule.FileName);
        Application.Current.Shutdown();
    }

}
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page3.xaml
/// </summary>
public partial class Page3 : Page
{
    public Page3()
    {
        InitializeComponent();
        settingssaveinfobox.Text = Settings.settingsloaded.filename;
        savesettingsupdates.IsChecked = Settings.settingsloaded.updates;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Settings.settingsloaded.filename = settingssaveinfobox.Text;
        if (savesettingsupdates.IsChecked != false)
            Settings.settingsloaded.updates = true;
        else
            Settings.settingsloaded.updates = false;
        var json = JsonSerializer.Serialize(Settings.settingsloaded);
        File.WriteAllText(Directory.GetCurrentDirectory() + "/Settings.json", json);
        Process.Start(Process.GetCurrentProcess().MainModule.FileName);
        Application.Current.Shutdown();
    }
}
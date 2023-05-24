using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using Notification.Wpf;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page2.xaml
/// </summary>
public partial class Page2 : Page
{
    public static List<usernamelist> bulkadd = new();
    private readonly CsvConfiguration config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };

    public Page2()
    {
        InitializeComponent();
    }


    public List<Page1.accountlist> jotain { get; }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (Password.Password == "" || Username.Text == "")
        {
            notif.notificationManager.Show("Error", "No username or password set!", NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
            return;
        }

        Page1.ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
        Page1.ActualAccountlists.Add(new Page1.accountlist
            { username = Username.Text, password = Password.Password });
        Page1.RemoveDoubleQuotesFromList(Page1.ActualAccountlists);
        using (var writer =
               new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv"))
        using (var csv2 = new CsvWriter(writer, config))
        {
            csv2.WriteRecords(Page1.ActualAccountlists);
        }
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        new Window2().ShowDialog();
        Page1.ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
        foreach (var item in bulkadd)
            Page1.ActualAccountlists.Add(new Page1.accountlist
            {
                username = item.username,
                password = item.password
            });
        using var writer =
            new StreamWriter(Directory.GetCurrentDirectory() + "\\" + Settings.settingsloaded.filename + ".csv");
        using var csvWriter = new CsvWriter(writer, config);
        Page1.RemoveDoubleQuotesFromList(Page1.ActualAccountlists);
        csvWriter.WriteRecords(Page1.ActualAccountlists);
    }

    public class usernamelist
    {
        public string username { get; set; }

        public string password { get; set; }
    }
}
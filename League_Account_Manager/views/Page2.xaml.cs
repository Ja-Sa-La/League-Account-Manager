using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class Page2 : Page
{
    public static List<UserNameList> BulkAdd = new();
    private readonly CsvConfiguration _config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };

    public Page2()
    {
        InitializeComponent();
    }

    public List<Page1.accountlist> AccountLists { get; }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Password.Password) || string.IsNullOrWhiteSpace(Username.Text))
        {
            notif.notificationManager.Show("Error", "No username or password set!", NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null,null, null, () =>notif.donothing() , "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            return;
        }

        UpdateAccountList(Username.Text, Password.Password);
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        new Window2().ShowDialog();

        if (BulkAdd.Count < 1)
            return;

        foreach (var item in BulkAdd) UpdateAccountList(item.Username, item.Password);
    }

    private void UpdateAccountList(string username, string password)
    {
        Page1.ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
        Page1.ActualAccountlists.Add(new Page1.accountlist { username = username, password = password });
        Page1.RemoveDoubleQuotesFromList(Page1.ActualAccountlists);

        using var writer =
            new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), $"{Settings.settingsloaded.filename}.csv"));
        using var csvWriter = new CsvWriter(writer, _config);
        csvWriter.WriteRecords(Page1.ActualAccountlists);
    }

    public class UserNameList
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
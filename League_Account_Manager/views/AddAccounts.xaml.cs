using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class AddAccounts : Page
{
    public static List<UserNameList> BulkAdd = new();
    private readonly CsvConfiguration _config = new(CultureInfo.CurrentCulture) { Delimiter = ";" };

    public AddAccounts()
    {
        InitializeComponent();
    }

    public List<Utils.AccountList> AccountLists { get; }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Password.Password) || string.IsNullOrWhiteSpace(Username.Text))
        {
            notif.notificationManager.Show("Error", "No username or password set!", NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
            return;
        }

        UpdateAccountList(Username.Text, Password.Password);
        Username.Text = "";
        Password.Password = "";
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        new BulkAdd().ShowDialog();

        if (BulkAdd.Count < 1)
            return;

        foreach (var item in BulkAdd) UpdateAccountList(item.Username, item.Password);
    }

    private async void UpdateAccountList(string username, string password)
    {
        Accounts.ActualAccountlists.RemoveAll(r => r.username == "username" && r.password == "password");
        Accounts.ActualAccountlists.Add(new Utils.AccountList { username = username, password = password });
        Utils.RemoveDoubleQuotesFromList(Accounts.ActualAccountlists);
        FileStream? fileStream = null;
        while (fileStream == null)
            try
            {
                fileStream =
                    File.Open(Path.Combine(Directory.GetCurrentDirectory(), $"{Misc.Settings.settingsloaded.filename}.csv"),
                        FileMode.Open, FileAccess.Read, FileShare.None);
                fileStream.Close();
            }
            catch (IOException)
            {
                // The file is in use by another process. Wait and try again.
                await Task.Delay(1000);
            }

        using var writer =
            new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), $"{Misc.Settings.settingsloaded.filename}.csv"));
        using var csvWriter = new CsvWriter(writer, _config);
        csvWriter.WriteRecords(Accounts.ActualAccountlists);
    }

    public class UserNameList
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
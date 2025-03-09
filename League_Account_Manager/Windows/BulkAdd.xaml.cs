using System.Windows;
using League_Account_Manager.views;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for BulkAdd.xaml
/// </summary>
public partial class BulkAdd : Window
{
    public BulkAdd()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        var lines = accountlogins.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        AddAccounts.BulkAdd.Clear();

        if (lines.Length < 1 || string.IsNullOrWhiteSpace(lines[0]))
        {
            Close();
            return;
        }

        foreach (var line in lines)
        {
            var credentials = line.Split(":");
            if (credentials.Length >= 2)
                AddAccounts.BulkAdd.Add(new AddAccounts.UserNameList
                {
                    Username = credentials[0],
                    Password = credentials[1]
                });
        }

        Close();
    }
}
using System.Windows;
using League_Account_Manager.views;
using NLog;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for MissingInfo.xaml
/// </summary>
public partial class MissingInfo : Window
{
    public MissingInfo()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (username.Text.Length > 0 && password.Text.Length > 0)
            {
                Accounts.SelectedUsername = username.Text;
                Accounts.SelectedPassword = password.Password;
                Close();
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error adding logins");
        }
    }
}
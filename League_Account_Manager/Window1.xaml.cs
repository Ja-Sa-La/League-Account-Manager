using System.Windows;
using League_Account_Manager.views;
using NLog;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for Window1.xaml
/// </summary>
public partial class Window1 : Window
{
    public Window1()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (username.Text.Length > 0 && password.Text.Length > 0)
            {
                Page1.SelectedUsername = username.Text;
                Page1.SelectedPassword = password.Password;
                Close();
            }
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error adding logins");
        }
    }
}
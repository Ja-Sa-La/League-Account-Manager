using System.Windows;
using System.Windows.Input;

namespace League_Account_Manager.Windows;

public partial class PasswordPrompt : Window
{
    public PasswordPrompt(string? message = null)
    {
        InitializeComponent();
        var main = Application.Current?.MainWindow;
        if (main != null && main.IsVisible)
        {
            Owner = main;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (!string.IsNullOrWhiteSpace(message))
            PromptText.Text = message;
    }

    public string Password => PasswordBox.Password;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}

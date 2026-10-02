using System.Windows;
using System.Windows.Input;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Windows;

public partial class PersistLoginPromptWindow : Window
{
    public PersistLoginPromptWindow(string? message = null, bool allowRememberChoice = true)
    {
        InitializeComponent();
        var main = Application.Current?.MainWindow;
        if (main != null && main.IsVisible)
            Owner = main;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!string.IsNullOrWhiteSpace(message))
            PromptText.Text = message;
        // Generated tokens are shared with other users, so the "remember my choice" shortcut
        // is only offered for personal login flows.
        RememberChoice.Visibility = allowRememberChoice ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool PersistLogin { get; private set; }
    public bool RememberChoiceSelected => RememberChoice.IsChecked == true;

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        PersistLogin = true;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        PersistLogin = false;
        DialogResult = true;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}

using System.Windows;
using System.Windows.Input;

namespace League_Account_Manager.Windows;

public partial class TextPrompt : Window
{
    public TextPrompt(string title, string message, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PromptText.Text = message;
        ValueBox.Text = initialValue;
        ValueBox.SelectAll();
        var main = Application.Current?.MainWindow;
        if (main != null && main.IsVisible)
            Owner = main;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public string Value => ValueBox.Text;

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

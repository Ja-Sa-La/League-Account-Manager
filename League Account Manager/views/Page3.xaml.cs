using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page3.xaml
/// </summary>
public partial class Page3 : Page
{
    public Page3()
    {
        InitializeComponent();
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        Wpf.Ui.Appearance.Theme.Apply(
            ThemeType.Dark // Whether to change accents automatically
        );
    }

    private void RadioButton_Checked_1(object sender, RoutedEventArgs e)
    {
        Wpf.Ui.Appearance.Theme.Apply(
            ThemeType.Light // Whether to change accents automatically
        );
    }
}
using System.Windows;
using League_Account_Manager.views;

namespace League_Account_Manager.Windows;

/// <summary>
///     Interaction logic for RemoveFriendsConfirmation.xaml
/// </summary>
public partial class RemoveFriendsConfirmation : Window
{
    public RemoveFriendsConfirmation()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        MiscTools.yayornay = 1;
        Close();
    }

    private void Button_Click1(object sender, RoutedEventArgs e)
    {
        MiscTools.yayornay = 2;
        Close();
    }
}
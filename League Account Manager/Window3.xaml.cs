using System.Windows;
using League_Account_Manager.views;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for Window3.xaml
/// </summary>
public partial class Window3 : Window
{
    public Window3()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Page7.yayornay = 1;
        Close();
    }

    private void Button_Click1(object sender, RoutedEventArgs e)
    {
        Page7.yayornay = 2;
        Close();
    }
}
using System;
using System.Windows;
using League_Account_Manager.views;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for Window2.xaml
/// </summary>
public partial class Window2 : Window
{
    public Window2()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        var array = accountlogins.Text.Split(new string[1] { Environment.NewLine }, StringSplitOptions.None);
        Page2.bulkadd.Clear();
        var array2 = array;
        for (var i = 0; i < array2.Length; i++)
        {
            var array3 = array2[i].Split(":");
            Page2.bulkadd.Add(new Page2.usernamelist
            {
                username = array3[0],
                password = array3[1]
            });
        }

        Close();
    }
}
using System;
using System.Runtime.InteropServices;
using System.Windows;
using League_Account_Manager.views;
using Wpf.Ui.Controls;

namespace League_Account_Manager;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : UiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        //AllocConsole();
        RootFrame.Navigate(new Page1());
    }

    // [DllImport("kernel32.dll", EntryPoint = "AllocConsole", SetLastError = true, CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    // private static extern int AllocConsole();

    private void NavigationItem_Click_1(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page2());
    }

    private void NavigationItem_Click_2(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page1());
    }

    private void NavigationItem_Click(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page3());
    }

    private void NavigationItem_Click_3(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page4());
    }

    private void NavigationItem_Click_4(object sender, RoutedEventArgs e)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void NavigationItem_Click_5(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page5());
    }

    private void NavigationItem_Click_6(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page6());
    }

    private void NavigationItem_Click_7(object sender, RoutedEventArgs e)
    {
        RootFrame.Navigate(new Page7());
    }
}
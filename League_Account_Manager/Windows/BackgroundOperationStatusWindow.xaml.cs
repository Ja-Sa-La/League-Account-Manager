using System.Windows;

namespace League_Account_Manager.Windows;

/// <summary>
///     Small always-on-top status shown at the desktop's left edge while a login keeps running
///     after the accounts page has been navigated away from.
/// </summary>
public partial class BackgroundOperationStatusWindow : Window
{
    public BackgroundOperationStatusWindow()
    {
        InitializeComponent();
    }
}

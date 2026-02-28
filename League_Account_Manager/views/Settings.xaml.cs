using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : Page
{
    private bool _initializing;
    private string? _pendingEncryptionPassword;

    public Settings()
    {
        InitializeComponent();
        _initializing = true;
        settingssaveinfobox.Text = Misc.Settings.settingsloaded.filename;
        savesettingsupdates.IsChecked = Misc.Settings.settingsloaded.updates;
        DisplayPasswords.IsChecked = Misc.Settings.settingsloaded.DisplayPasswords;
        AutoUpdateRanks.IsChecked = Misc.Settings.settingsloaded.UpdateRanks;
        AccountFileEncryption.IsChecked = Misc.Settings.settingsloaded.AccountFileEncryptionEnabled;
        _initializing = false;
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var currentPassword = AccountFileStore.GetPassword();
        var encryptionEnabled = AccountFileEncryption.IsChecked == true;
        var newPassword = currentPassword;

        if (encryptionEnabled)
        {
            if (!string.IsNullOrWhiteSpace(_pendingEncryptionPassword))
                newPassword = _pendingEncryptionPassword;

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBox.Show("Please set an account file password before enabling encryption.",
                    "Missing Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Misc.Settings.settingsloaded.filename = settingssaveinfobox.Text;
        if (savesettingsupdates.IsChecked != false)
            Misc.Settings.settingsloaded.updates = true;
        else
            Misc.Settings.settingsloaded.updates = false;
        if (DisplayPasswords.IsChecked != false)
            Misc.Settings.settingsloaded.DisplayPasswords = true;
        else
            Misc.Settings.settingsloaded.DisplayPasswords = false;
        if (AutoUpdateRanks.IsChecked != false)
            Misc.Settings.settingsloaded.UpdateRanks = true;
        else
            Misc.Settings.settingsloaded.UpdateRanks = false;

        Misc.Settings.settingsloaded.AccountFileEncryptionEnabled = encryptionEnabled;
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var filePath = AccountFileStore.GetAccountsFilePath();

        try
        {
            if (encryptionEnabled)
            {
                await AccountFileStore.RewriteForEncryptionStateAsync(filePath, config, true, currentPassword,
                    newPassword);
                AccountFileStore.SetPassword(newPassword);
            }
            else
            {
                await AccountFileStore.RewriteForEncryptionStateAsync(filePath, config, false, currentPassword, null);
                AccountFileStore.SetPassword(null);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Failed to update account file encryption: {exception.Message}", "Encryption Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Misc.Settings.Save();
        Process.Start(Process.GetCurrentProcess().MainModule.FileName);
        Application.Current.Shutdown();
    }

    private void OnAccountFileEncryptionChecked(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        if (AccountFileEncryption.IsChecked == true)
        {
            var prompt = new PasswordPrompt("Enter a password to encrypt your account file:")
            {
                Owner = Application.Current?.MainWindow
            };

            var result = prompt.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(prompt.Password))
            {
                _pendingEncryptionPassword = prompt.Password;
            }
            else
            {
                AccountFileEncryption.IsChecked = false;
                _pendingEncryptionPassword = null;
            }
        }
        else
        {
            _pendingEncryptionPassword = null;
        }
    }
}
using System.Windows;
using System.Globalization;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Windows;

public partial class CloudAccountDialog : Window
{
    internal CloudAccountDialog()
    {
        InitializeComponent();
        Owner = Application.Current?.MainWindow;
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var registering = RegisterMode.IsChecked == true;
        ConfirmLabel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPasswordBox.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = registering ? "Create account" : "Sign in";
        StatusText.Text = registering
            ? "Use 12+ characters with uppercase, lowercase, a number, and a symbol. Do not include your username."
            : string.Empty;
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var registering = RegisterMode.IsChecked == true;
        var validation = AccountSyncService.ValidateCredentials(username, password, registering);
        if (validation != null)
        {
            StatusText.Text = validation;
            return;
        }
        if (registering && password != ConfirmPasswordBox.Password)
        {
            StatusText.Text = "The passwords do not match.";
            return;
        }

        SubmitButton.IsEnabled = false;
        try
        {
            await AccountSyncService.Instance.AuthenticateAsync(username, password, registering);
            try
            {
                await AccountSyncService.Instance.RunWithoutAutomaticSyncAsync(ReconcileCloudFilesAsync);
            }
            catch (Exception exception)
            {
                AppMessageBox.Show($"Signed in, but file synchronization could not finish: {exception.Message}\n\nYou can retry the transfer in Settings.",
                    "Cloud synchronization", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally { SubmitButton.IsEnabled = true; }
    }

    private async Task ReconcileCloudFilesAsync()
    {
        var service = AccountSyncService.Instance;
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var localAccounts = await AccountFileStore.CreateSyncDocumentAsync(config);
        var cloudAccounts = await service.TryDownloadAsync(false);
        if (cloudAccounts != null)
        {
            var localCount = AccountFileStore.CountCredentialsInSyncDocument(localAccounts);
            var cloudCount = AccountFileStore.CountCredentialsInSyncDocument(cloudAccounts);
            var choice = AppMessageBox.ShowChoices(
                $"Cloud account file: {cloudCount} credentials\nThis computer: {localCount} credentials\n\nChoose which account file to keep.",
                "Sync accounts", new Dictionary<MessageBoxResult, string>
                {
                    [MessageBoxResult.Yes] = "Import cloud",
                    [MessageBoxResult.No] = "Upload local",
                    [MessageBoxResult.Cancel] = "Skip"
                }, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
                await service.RunWithoutAutomaticSyncAsync(() => AccountFileStore.ReplaceFromSyncDocumentAsync(cloudAccounts, config));
            else if (choice == MessageBoxResult.No)
                await service.TransferAsync(true, localAccounts);
        }
        else
        {
            var localCount = AccountFileStore.CountCredentialsInSyncDocument(localAccounts);
            var choice = AppMessageBox.Show(
                $"No cloud account file exists yet. This computer has {localCount} credentials.\n\nUpload this account file now?",
                "Sync accounts", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
                await service.TransferAsync(true, localAccounts);
        }

        var localSettings = Settings.CreateSyncDocument();
        var cloudSettings = await service.TryDownloadAsync(true);
        if (cloudSettings != null)
        {
            var choice = AppMessageBox.ShowChoices(
                $"Cloud settings: {cloudSettings.Length:N0} characters\nThis computer: {localSettings.Length:N0} characters\n\nChoose which settings to keep.",
                "Sync settings", new Dictionary<MessageBoxResult, string>
                {
                    [MessageBoxResult.Yes] = "Import cloud",
                    [MessageBoxResult.No] = "Upload local",
                    [MessageBoxResult.Cancel] = "Skip"
                }, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes)
                await service.RunWithoutAutomaticSyncAsync(() =>
                {
                    Settings.ApplySyncDocument(cloudSettings);
                    return Task.CompletedTask;
                });
            else if (choice == MessageBoxResult.No)
                await service.TransferSettingsAsync(true, localSettings);
        }
        else if (AppMessageBox.Show(
                     "No cloud settings file exists yet. Upload this computer's settings now?",
                     "Sync settings", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await service.TransferSettingsAsync(true, localSettings);
        }
    }
}
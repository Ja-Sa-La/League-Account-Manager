using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Microsoft.Win32;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : Page
{
    private readonly bool _initializing;
    private string? _pendingEncryptionPassword;

    public Settings()
    {
        InitializeComponent();
        _initializing = true;
        settingssaveinfobox.Text = Misc.Settings.settingsloaded.filename;
        savesettingsupdates.IsChecked = Misc.Settings.settingsloaded.updates;
        ReleaseChannel.SelectedIndex = string.Equals(Misc.Settings.settingsloaded.ReleaseChannel, "Beta",
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        DisplayPasswords.IsChecked = Misc.Settings.settingsloaded.DisplayPasswords;
        AutoUpdateRanks.IsChecked = Misc.Settings.settingsloaded.UpdateRanks;
        AccountFileEncryption.IsChecked = Misc.Settings.settingsloaded.AccountFileEncryptionEnabled;
        CurrentInstallLocation.Text = Path.GetFullPath(AppContext.BaseDirectory);
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
                AppMessageBox.Show("Please set an account file password before enabling encryption.",
                    "Missing Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var oldBaseName = Misc.Settings.settingsloaded.filename;
        var oldEncryptionEnabled = Misc.Settings.settingsloaded.AccountFileEncryptionEnabled;
        var sourceFilePath = AccountFileStore.GetAccountsFilePath();
        var newBaseName = Path.GetFileNameWithoutExtension(settingssaveinfobox.Text?.Trim());
        if (string.IsNullOrWhiteSpace(newBaseName))
            newBaseName = "Accounts";
        if (savesettingsupdates.IsChecked != false)
            Misc.Settings.settingsloaded.updates = true;
        else
            Misc.Settings.settingsloaded.updates = false;
        Misc.Settings.settingsloaded.ReleaseChannel = ReleaseChannel.SelectedIndex == 1 ? "Beta" : "Stable";
        if (DisplayPasswords.IsChecked != false)
            Misc.Settings.settingsloaded.DisplayPasswords = true;
        else
            Misc.Settings.settingsloaded.DisplayPasswords = false;
        if (AutoUpdateRanks.IsChecked != false)
            Misc.Settings.settingsloaded.UpdateRanks = true;
        else
            Misc.Settings.settingsloaded.UpdateRanks = false;

        // Persist update preferences before account-file migration can fail.
        Misc.Settings.Save();

        Misc.Settings.settingsloaded.filename = newBaseName;
        Misc.Settings.settingsloaded.AccountFileEncryptionEnabled = encryptionEnabled;
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var destinationFilePath = AccountFileStore.GetAccountsFilePath();

        try
        {
            if (encryptionEnabled)
            {
                await AccountFileStore.RewriteForEncryptionStateAsync(sourceFilePath, destinationFilePath, config,
                    true, currentPassword, newPassword);
                AccountFileStore.SetPassword(newPassword);
            }
            else
            {
                await AccountFileStore.RewriteForEncryptionStateAsync(sourceFilePath, destinationFilePath, config,
                    false, currentPassword, null);
                AccountFileStore.SetPassword(null);
            }
        }
        catch (Exception exception)
        {
            Misc.Settings.settingsloaded.filename = oldBaseName;
            Misc.Settings.settingsloaded.AccountFileEncryptionEnabled = oldEncryptionEnabled;
            Misc.Settings.Save();
            AppMessageBox.Show($"Failed to update account file encryption: {exception.Message}", "Encryption Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Misc.Settings.Save();
        var applicationPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(applicationPath))
            Process.Start(new ProcessStartInfo { FileName = applicationPath, UseShellExecute = true });
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

    private async void OnImportAccountsClick(object sender, RoutedEventArgs e)
    {
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var dialog = new OpenFileDialog
        {
            Filter = AccountFileStore.GetTransferFileDialogFilter(),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        var importMode = AppMessageBox.Show(
            "Choose import mode:\n\nYes = Replace existing accounts\nNo = Combine lists and dedupe\nCancel = Abort import",
            "Import mode", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (importMode == MessageBoxResult.Cancel)
            return;

        var combineAndDedupe = importMode == MessageBoxResult.No;

        try
        {
            await AccountFileStore.ImportIntoCurrentStoreAsync(dialog.FileName, config, combineAndDedupe);
            AppMessageBox.Show("Accounts imported successfully.", "Import complete", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"Import failed: {ex.Message}", "Import error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnExportAccountsClick(object sender, RoutedEventArgs e)
    {
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var dialog = new SaveFileDialog
        {
            Filter = AccountFileStore.GetTransferFileDialogFilter(),
            AddExtension = true,
            DefaultExt = ".lamjson",
            FileName = $"{Misc.Settings.settingsloaded.filename}_export"
        };

        if (dialog.ShowDialog() != true)
            return;

        var encryptExport = AppMessageBox.Show("Encrypt exported file?", "Export encryption",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        string? exportPassword = null;
        if (encryptExport)
        {
            exportPassword = PromptForPassword("Enter a password to encrypt the exported file:");
            if (string.IsNullOrWhiteSpace(exportPassword))
            {
                AppMessageBox.Show("Export canceled. Password is required for encrypted export.", "Export canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            var records = await AccountFileStore.LoadAsync(AccountFileStore.GetAccountsFilePath(), config);
            await AccountFileStore.ExportAsync(dialog.FileName, records, config, encryptExport, exportPassword);
            AppMessageBox.Show("Accounts exported successfully.", "Export complete", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"Export failed: {ex.Message}", "Export error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string? PromptForPassword(string message)
    {
        var prompt = new PasswordPrompt(message)
        {
            Owner = Application.Current?.MainWindow
        };
        var result = prompt.ShowDialog();
        return result == true ? prompt.Password : null;
    }

    private void OnMoveInstallationClick(object sender, RoutedEventArgs e)
    {
        var sourceDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var folderDialog = new OpenFolderDialog
        {
            Title = "Select a new installation folder",
            InitialDirectory = Directory.GetParent(sourceDirectory)?.FullName ?? sourceDirectory,
            FolderName = sourceDirectory
        };

        if (folderDialog.ShowDialog() != true)
            return;

        var destinationDirectory = Path.GetFullPath(folderDialog.FolderName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
        {
            AppMessageBox.Show("Please select a different folder.", "Move installation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsPathNestedWithin(sourceDirectory, destinationDirectory) ||
            IsPathNestedWithin(destinationDirectory, sourceDirectory))
        {
            AppMessageBox.Show("Please select a folder outside the current installation path.",
                "Move installation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            AppMessageBox.Show("Unable to determine the current executable path.", "Move installation",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var filesToMove = GetInstallationFiles(sourceDirectory, currentExePath).ToList();
            if (filesToMove.Count == 0)
            {
                AppMessageBox.Show("No installation files were found to move.", "Move installation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var conflicts = filesToMove
                .Select(relativePath => Path.Combine(destinationDirectory, relativePath))
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Take(5)
                .ToList();

            if (conflicts.Count != 0)
            {
                AppMessageBox.Show(
                    "The selected folder already contains required application files:\n\n" +
                    string.Join("\n", conflicts) +
                    "\n\nPlease select another folder or remove the conflicting files first.",
                    "Move installation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Misc.Settings.Save();
            LaunchInstallationMoveScript(sourceDirectory, destinationDirectory, Path.GetFileName(currentExePath),
                filesToMove);
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"Failed to move installation: {ex.Message}", "Move installation",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsPathNestedWithin(string basePath, string candidatePath)
    {
        var normalizedBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> GetInstallationFiles(string sourceDirectory, string currentExePath)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRelativeFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            relativePath = relativePath.Replace('/', '\\').TrimStart('\\');
            var fullPath = Path.Combine(sourceDirectory, relativePath);
            if (File.Exists(fullPath))
                files.Add(relativePath);
        }

        void AddFileIfExists(string fileName)
        {
            if (!string.IsNullOrWhiteSpace(fileName))
                AddRelativeFile(fileName);
        }

        var executableName = Path.GetFileName(currentExePath);
        var executableBaseName = Path.GetFileNameWithoutExtension(currentExePath);
        AddFileIfExists(executableName);
        AddFileIfExists($"{executableBaseName}.dll");
        AddFileIfExists($"{executableBaseName}.deps.json");
        AddFileIfExists($"{executableBaseName}.runtimeconfig.json");
        AddFileIfExists($"{executableBaseName}.pdb");
        AddFileIfExists($"{executableBaseName}.xml");
        AddFileIfExists($"{executableBaseName}.config");

        AddFileIfExists("Settings.json");
        AddFileIfExists("Log.txt");
        AddFileIfExists("temp_update.exe");

        foreach (var lamFile in Directory.EnumerateFiles(sourceDirectory, "*.LAM", SearchOption.TopDirectoryOnly))
            AddFileIfExists(Path.GetFileName(lamFile));

        foreach (var exportFile in Directory.EnumerateFiles(sourceDirectory, "*.lamjson", SearchOption.TopDirectoryOnly))
            AddFileIfExists(Path.GetFileName(exportFile));

        var depsFilePath = Path.Combine(sourceDirectory, $"{executableBaseName}.deps.json");
        if (File.Exists(depsFilePath))
        {
            using var depsDocument = JsonDocument.Parse(File.ReadAllText(depsFilePath));
            foreach (var dependencyPath in EnumerateDependencyFiles(depsDocument.RootElement))
                AddRelativeFile(dependencyPath);
        }

        return files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateDependencyFiles(JsonElement root)
    {
        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var sectionName in new[] { "runtime", "native", "resources", "runtimeTargets" })
                {
                    if (!library.Value.TryGetProperty(sectionName, out var section) ||
                        section.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var entry in section.EnumerateObject())
                        yield return entry.Name;
                }
            }
        }
    }

    private static void LaunchInstallationMoveScript(string sourceDirectory, string destinationDirectory,
        string executableName, IReadOnlyCollection<string> filesToMove)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"lam_move_{Guid.NewGuid():N}.ps1");
        var escapedSource = EscapePowerShellString(sourceDirectory);
        var escapedDestination = EscapePowerShellString(destinationDirectory);
        var escapedExecutableName = EscapePowerShellString(executableName);
        var escapedScriptPath = EscapePowerShellString(scriptPath);
        var escapedFiles = string.Join(", ", filesToMove.Select(x => $"'{EscapePowerShellString(x)}'"));
        var script = $@"
$source = '{escapedSource}'
$destination = '{escapedDestination}'
$exeName = '{escapedExecutableName}'
$scriptPath = '{escapedScriptPath}'
$processId = {Environment.ProcessId}
$files = @({escapedFiles})

while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {{
    Start-Sleep -Milliseconds 500
}}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($relativePath in $files) {{
    $sourcePath = Join-Path $source $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {{
        continue
    }}

    $destinationPath = Join-Path $destination $relativePath
    $destinationParent = Split-Path -Path $destinationPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {{
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }}

    Move-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}}

Start-Process -FilePath (Join-Path $destination $exeName) -WorkingDirectory $destination

Get-ChildItem -LiteralPath $source -Directory -Recurse | Sort-Object FullName -Descending | ForEach-Object {{
    if (-not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)) {{
        Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
    }}
}}

Remove-Item -LiteralPath $scriptPath -Force
";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''");
    }
}
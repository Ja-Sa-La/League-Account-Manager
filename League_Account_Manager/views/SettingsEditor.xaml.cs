using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using League_Account_Manager.Windows;
using Microsoft.Win32;

namespace League_Account_Manager.views;

public partial class SettingsEditor : Page
{
    private const string GameCfgFileName = "game.cfg";
    private const string PersistedSettingsFileName = "PersistedSettings.json";

    private static readonly JsonSerializerOptions BackupSerializerOptions = new() { WriteIndented = true };

    private string? _gameCfgPath;
    private string? _persistedSettingsPath;

    public SettingsEditor()
    {
        InitializeComponent();
        RefreshPathsAndStates();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshPathsAndStates();
        SetStatus("File status refreshed.");
    }

    private void OnApplyReadOnlyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var gameCfgReadOnlySelected = GameCfgReadOnly.IsChecked == true;
            var persistedSettingsReadOnlySelected = PersistedSettingsReadOnly.IsChecked == true;

            RefreshPathsAndStates();

            if (!ApplyReadOnlyState(_gameCfgPath, gameCfgReadOnlySelected, GameCfgFileName))
                return;

            if (!ApplyReadOnlyState(_persistedSettingsPath, persistedSettingsReadOnlySelected,
                    PersistedSettingsFileName))
                return;

            RefreshPathsAndStates();
            SetStatus("Selected file setting applied.");
        }
        catch (Exception exception)
        {
            AppMessageBox.Show($"Failed to apply selected file setting: {exception.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshPathsAndStates();
            if (!ValidateRequiredFileExists(_gameCfgPath, GameCfgFileName) ||
                !ValidateRequiredFileExists(_persistedSettingsPath, PersistedSettingsFileName))
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "Settings Backup (*.lamsettings)|*.lamsettings|JSON File (*.json)|*.json|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".lamsettings",
                FileName = "LeagueSettingsBackup"
            };

            if (dialog.ShowDialog() != true)
                return;

            var backup = new SettingsBackup
            {
                CreatedUtc = DateTime.UtcNow,
                GameCfg = CreateBackupEntry(_gameCfgPath!, GameCfgFileName),
                PersistedSettings = CreateBackupEntry(_persistedSettingsPath!, PersistedSettingsFileName)
            };

            var content = JsonSerializer.Serialize(backup, BackupSerializerOptions);
            File.WriteAllText(dialog.FileName, content);
            SetStatus($"Backup exported to {dialog.FileName}");
        }
        catch (Exception exception)
        {
            AppMessageBox.Show($"Failed to export backup: {exception.Message}", "Export error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImportBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Settings Backup (*.lamsettings;*.json)|*.lamsettings;*.json|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            var backup = JsonSerializer.Deserialize<SettingsBackup>(File.ReadAllText(dialog.FileName));
            if (backup?.GameCfg == null || backup.PersistedSettings == null)
            {
                AppMessageBox.Show("The selected backup file is invalid.", "Import error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            RefreshPathsAndStates();

            _gameCfgPath = ResolveOrPromptTargetFile(_gameCfgPath, GameCfgFileName);
            if (string.IsNullOrWhiteSpace(_gameCfgPath))
                return;

            _persistedSettingsPath = ResolveOrPromptTargetFile(_persistedSettingsPath, PersistedSettingsFileName);
            if (string.IsNullOrWhiteSpace(_persistedSettingsPath))
                return;

            RestoreBackupEntry(_gameCfgPath, backup.GameCfg);
            RestoreBackupEntry(_persistedSettingsPath, backup.PersistedSettings);

            RefreshPathsAndStates();
            SetStatus($"Backup imported from {dialog.FileName}");
        }
        catch (Exception exception)
        {
            AppMessageBox.Show($"Failed to import backup: {exception.Message}", "Import error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPathsAndStates()
    {
        _gameCfgPath = ResolveGameCfgPath();
        _persistedSettingsPath = ResolvePersistedSettingsPath(_gameCfgPath);

        GameCfgPathBox.Text = _gameCfgPath ?? "Not found";
        PersistedSettingsPathBox.Text = _persistedSettingsPath ?? "Not found";

        var gameCfgExists = File.Exists(_gameCfgPath);
        var persistedSettingsExists = File.Exists(_persistedSettingsPath);

        GameCfgReadOnly.IsEnabled = gameCfgExists;
        PersistedSettingsReadOnly.IsEnabled = persistedSettingsExists;

        GameCfgReadOnly.IsChecked = gameCfgExists && IsFileReadOnly(_gameCfgPath!);
        PersistedSettingsReadOnly.IsChecked = persistedSettingsExists && IsFileReadOnly(_persistedSettingsPath!);
    }

    private static BackupEntry CreateBackupEntry(string path, string fileName)
    {
        return new BackupEntry
        {
            FileName = fileName,
            IsReadOnly = IsFileReadOnly(path),
            ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(path))
        };
    }

    private static void RestoreBackupEntry(string targetPath, BackupEntry entry)
    {
        if (File.Exists(targetPath) && IsFileReadOnly(targetPath))
            SetFileReadOnly(targetPath, false);

        var bytes = Convert.FromBase64String(entry.ContentBase64);
        File.WriteAllBytes(targetPath, bytes);
        SetFileReadOnly(targetPath, entry.IsReadOnly);
    }

    private static string? ResolveOrPromptTargetFile(string? currentPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            string.Equals(Path.GetFileName(currentPath), fileName, StringComparison.OrdinalIgnoreCase))
            return NormalizeWindowsPath(currentPath);

        var dialog = new OpenFileDialog
        {
            Title = $"Select target {fileName}",
            CheckFileExists = true,
            Multiselect = false,
            Filter = $"{fileName}|{fileName}|All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? NormalizeWindowsPath(dialog.FileName) : null;
    }

    private string? ResolveGameCfgPath()
    {
        if (!string.IsNullOrWhiteSpace(Misc.Settings.settingsloaded.settingsLocation) &&
            File.Exists(Misc.Settings.settingsloaded.settingsLocation))
            return NormalizeWindowsPath(Misc.Settings.settingsloaded.settingsLocation);

        var leagueDirectory = Path.GetDirectoryName(Misc.Settings.settingsloaded.LeaguePath);
        if (string.IsNullOrWhiteSpace(leagueDirectory))
            return null;

        var candidate = Path.Combine(leagueDirectory, "Config", GameCfgFileName);
        return File.Exists(candidate) ? NormalizeWindowsPath(candidate) : null;
    }

    private string? ResolvePersistedSettingsPath(string? gameCfgPath)
    {
        if (!string.IsNullOrWhiteSpace(gameCfgPath))
        {
            var candidateFromGameCfgFolder = Path.Combine(Path.GetDirectoryName(gameCfgPath)!, PersistedSettingsFileName);
            if (File.Exists(candidateFromGameCfgFolder))
                return NormalizeWindowsPath(candidateFromGameCfgFolder);
        }

        var leagueDirectory = Path.GetDirectoryName(Misc.Settings.settingsloaded.LeaguePath);
        if (string.IsNullOrWhiteSpace(leagueDirectory))
            return null;

        var candidate = Path.Combine(leagueDirectory, "Config", PersistedSettingsFileName);
        return File.Exists(candidate) ? NormalizeWindowsPath(candidate) : null;
    }

    private static string NormalizeWindowsPath(string path)
    {
        var normalized = path.Replace('/', '\\');

        if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
            return "\\\\" + Regex.Replace(normalized[2..], "\\\\+", "\\");

        return Regex.Replace(normalized, "\\\\+", "\\");
    }

    private static bool ApplyReadOnlyState(string? path, bool readOnly, string fileName)
    {
        if (!ValidateRequiredFileExists(path, fileName))
            return false;

        SetFileReadOnly(path!, readOnly);
        return true;
    }

    private static bool ValidateRequiredFileExists(string? path, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return true;

        AppMessageBox.Show($"Could not find {fileName}. Please close the League client and refresh file status.",
            "File not found", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static bool IsFileReadOnly(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    }

    private static void SetFileReadOnly(string path, bool readOnly)
    {
        var attributes = File.GetAttributes(path);
        if (readOnly)
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        else
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private sealed class SettingsBackup
    {
        public string Version { get; set; } = "1";
        public DateTime CreatedUtc { get; set; }
        public BackupEntry GameCfg { get; set; } = new();
        public BackupEntry PersistedSettings { get; set; } = new();
    }

    private sealed class BackupEntry
    {
        public string FileName { get; set; } = string.Empty;
        public bool IsReadOnly { get; set; }
        public string ContentBase64 { get; set; } = string.Empty;
    }
}
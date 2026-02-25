using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using League_Account_Manager.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace League_Account_Manager.Misc;

public static class LoginTokenManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly string RiotSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "Riot Client", "Data", "RiotGamesPrivateSettings.yaml");
    private static readonly string LolSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "League of Legends", "Data", "RiotGamesPrivateSettings.yaml");
    private static readonly string RiotConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "Riot Client", "Config");
    private static readonly string RiotMetadataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Riot Games", "Metadata", "Riot Client");

    public static async Task<bool> GenerateLoginToken(string username, string password, string riotClientPath, bool stealth = false)
    {
        string? backupPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                DebugConsole.WriteLine("[Token] Username or password missing, aborting generation");
                return false;
            }
            if (!File.Exists(riotClientPath))
            {
                DebugConsole.WriteLine("[Token] Riot client path invalid, aborting generation");
                return false;
            }

            DebugConsole.WriteLine("[Token] Starting token generation");
            backupPath = BackupExistingSettings();
            Utils.KillLeagueFunc();

            if (stealth)
            {
                var launcher = new OfflineLauncher();
                _ = await launcher.LaunchRiotOrLeagueOfflineAsync(riotClientPath);
                DebugConsole.WriteLine("[Token] Launched Riot in stealth mode");
            }
            else
            {
                _ = Process.Start(riotClientPath,
                    "--launch-product=league_of_legends --launch-patchline=live");
                DebugConsole.WriteLine("[Token] Launched Riot client");
            }

            var riotProcessName = await WaitForRiotProcessAsync();
            if (string.IsNullOrEmpty(riotProcessName))
            {
                DebugConsole.WriteLine("[Token] Riot process not found after launch");
                return false;
            }

            var loginCompleted = await AutomateLoginAsync(riotProcessName, username, password);
            if (!loginCompleted)
            {
                DebugConsole.WriteLine("[Token] Login automation did not complete successfully");
                return false;
            }

            var loggedIn = await WaitForPersistedLoginAsync();
            if (!loggedIn)
            {
                DebugConsole.WriteLine("[Token] Persisted login check failed");
                return false;
            }

            Utils.KillLeagueFunc();
            DebugConsole.WriteLine("[Token] Persisted login confirmed; client killed for token read");

            if (!File.Exists(RiotSettingsPath))
                return false;

            var packageBytes = await BuildTokenPackageAsync();
            var passwordPrompt = new PasswordPrompt("Enter a password to encrypt the Riot login token file.");
            var promptResult = passwordPrompt.ShowDialog();
            if (promptResult != true || string.IsNullOrWhiteSpace(passwordPrompt.Password))
                return false;

            var encrypted = Encrypt(packageBytes, passwordPrompt.Password);
            var dialog = new SaveFileDialog
            {
                Filter = "Encrypted token (*.enc)|*.enc|All Files (*.*)|*.*",
                FileName = "RiotLoginToken.enc"
            };

            if (dialog.ShowDialog() != true)
                return false;

            await File.WriteAllBytesAsync(dialog.FileName, encrypted);
            DebugConsole.WriteLine($"[Token] Encrypted token saved to {dialog.FileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to generate login token");
            return false;
        }
        finally
        {
            await RestoreBackupAsync(backupPath);
        }
    }

    public static async Task<bool> UseLoginToken(string riotClientPath)
    {
        string? originalsBackup = null;
        try
        {
            if (!File.Exists(riotClientPath))
            {
                DebugConsole.WriteLine("[Token] Riot client path invalid, aborting import");
                return false;
            }

            DebugConsole.WriteLine("[Token] Starting token import");
            var openDialog = new OpenFileDialog
            {
                Filter = "Encrypted token (*.enc)|*.enc|All Files (*.*)|*.*"
            };

            if (openDialog.ShowDialog() != true)
                return false;

            var encrypted = await File.ReadAllBytesAsync(openDialog.FileName);

            var passwordPrompt = new PasswordPrompt("Enter the password to decrypt the Riot login token file.");
            var promptResult = passwordPrompt.ShowDialog();
            if (promptResult != true || string.IsNullOrWhiteSpace(passwordPrompt.Password))
                return false;

            var decrypted = Decrypt(encrypted, passwordPrompt.Password);
            if (decrypted == null)
            {
                DebugConsole.WriteLine("[Token] Failed to decrypt token file");
                return false;
            }

            originalsBackup = await BackupOriginalFilesAsync();

            var restored = await RestoreTokenPackageAsync(decrypted);
            if (!restored)
            {
                DebugConsole.WriteLine("[Token] Failed to restore token package");
                return false;
            }

            Utils.KillLeagueFunc();
            Process.Start(riotClientPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            DebugConsole.WriteLine("[Token] Riot client relaunched for token login");

            var loggedIn = await WaitForPersistedLoginAsync();
            return loggedIn;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import login token");
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(originalsBackup))
            {
                await Task.Delay(5000);
                await RestoreOriginalFilesAsync(originalsBackup);
            }
        }
    }

    private static async Task<string?> WaitForRiotProcessAsync()
    {
        var attempts = 0;
        while (attempts < 40)
        {
            if (Process.GetProcessesByName("Riot Client").Length != 0)
            {
                DebugConsole.WriteLine("[Token] Found Riot Client process");
                return "Riot Client";
            }
            if (Process.GetProcessesByName("RiotClientUx").Length != 0)
            {
                DebugConsole.WriteLine("[Token] Found RiotClientUx process");
                return "RiotClientUx";
            }

            await Task.Delay(200);
            attempts++;
        }

        DebugConsole.WriteLine("[Token] Riot process not detected after waiting");

        return null;
    }

    private static async Task<byte[]> BuildTokenPackageAsync()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
        {
            // settings file
            var settingsEntry = archive.CreateEntry("settings/RiotGamesPrivateSettings.yaml", CompressionLevel.Optimal);
            await using (var entryStream = settingsEntry.Open())
            await using (var fileStream = File.OpenRead(RiotSettingsPath))
            {
                await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
            }

            // config files
            if (Directory.Exists(RiotConfigPath))
            {
                var files = Directory.EnumerateFiles(RiotConfigPath, "*", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFileName(f), "lockfile", StringComparison.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(RiotConfigPath, file).Replace('\\', '/');
                    var entry = archive.CreateEntry($"config/{relativePath}", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(file);
                    await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
                }
            }

            if (Directory.Exists(RiotMetadataPath))
            {
                var files = Directory.EnumerateFiles(RiotMetadataPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(RiotMetadataPath, file).Replace('\\', '/');
                    var entry = archive.CreateEntry($"metadata/{relativePath}", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(file);
                    await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
                }
            }
        }

        return ms.ToArray();
    }

    private static async Task<bool> RestoreTokenPackageAsync(byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RiotSettingsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(LolSettingsPath)!);
            Directory.CreateDirectory(RiotConfigPath);
            Directory.CreateDirectory(RiotMetadataPath);

            var restoredSettings = false;
            try
            {
                using var ms = new MemoryStream(data);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read, false, Encoding.UTF8);
                foreach (var entry in archive.Entries)
                {
                    var entryPath = entry.FullName.Replace('\\', '/');
                    if (entryPath.StartsWith("settings/", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPaths = new[] { RiotSettingsPath, LolSettingsPath };
                        foreach (var dest in destPaths)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            await using var entryStream = entry.Open();
                            using var mem = new MemoryStream();
                            await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(dest, mem.ToArray()).ConfigureAwait(false);
                        }

                        restoredSettings = true;
                    }
                    else if (entryPath.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
                    {
                        var relative = entryPath.Substring("config/".Length).Replace('/', Path.DirectorySeparatorChar);
                        if (string.Equals(Path.GetFileName(relative), "lockfile", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var destPath = Path.Combine(RiotConfigPath, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        try
                        {
                            await using var entryStream = entry.Open();
                            using var mem = new MemoryStream();
                            await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(destPath, mem.ToArray()).ConfigureAwait(false);
                        }
                        catch (IOException ioex)
                        {
                            Logger.Warn(ioex, $"[Token] Skipping config entry due to IO lock: {entry.FullName}");
                        }
                    }
                    else if (entryPath.StartsWith("metadata/", StringComparison.OrdinalIgnoreCase))
                    {
                        var relative = entryPath.Substring("metadata/".Length).Replace('/', Path.DirectorySeparatorChar);
                        var destPath = Path.Combine(RiotMetadataPath, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        try
                        {
                            await using var entryStream = entry.Open();
                            using var mem = new MemoryStream();
                            await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(destPath, mem.ToArray()).ConfigureAwait(false);
                        }
                        catch (IOException ioex)
                        {
                            Logger.Warn(ioex, $"[Token] Skipping metadata entry due to IO lock: {entry.FullName}");
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Not a zip package; will try raw settings fallback
            }

            if (!restoredSettings)
            {
                // Fallback: treat data as raw settings content
                await File.WriteAllBytesAsync(RiotSettingsPath, data).ConfigureAwait(false);
                await File.WriteAllBytesAsync(LolSettingsPath, data).ConfigureAwait(false);
                restoredSettings = true;
            }

            return restoredSettings;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to restore token package");
            return false;
        }
    }

    private static async Task<string?> BackupOriginalFilesAsync()
    {
        try
        {
            var backupPath = Path.Combine(Path.GetTempPath(), $"lam_token_backup_{Guid.NewGuid():N}.zip");
            using var fs = new FileStream(backupPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);

            void AddFileIfExists(string sourcePath, string entryName)
            {
                if (!File.Exists(sourcePath)) return;
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(sourcePath);
                fileStream.CopyTo(entryStream);
            }

            AddFileIfExists(RiotSettingsPath, "settings/riot.yaml");
            AddFileIfExists(LolSettingsPath, "settings/lol.yaml");

            if (Directory.Exists(RiotConfigPath))
            {
                var files = Directory.EnumerateFiles(RiotConfigPath, "*", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFileName(f), "lockfile", StringComparison.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(RiotConfigPath, file).Replace('\\', '/');
                    var entry = archive.CreateEntry($"config/{relativePath}", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file);
                    fileStream.CopyTo(entryStream);
                }
            }

            if (Directory.Exists(RiotMetadataPath))
            {
                var files = Directory.EnumerateFiles(RiotMetadataPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(RiotMetadataPath, file).Replace('\\', '/');
                    var entry = archive.CreateEntry($"metadata/{relativePath}", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file);
                    fileStream.CopyTo(entryStream);
                }
            }

            return backupPath;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "[Token] Failed to backup original files before token restore");
            return null;
        }
    }

    private static async Task RestoreOriginalFilesAsync(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath))
                return;

            using var fs = File.OpenRead(backupPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
            foreach (var entry in archive.Entries)
            {
                var entryPath = entry.FullName.Replace('\\', '/');
                if (entryPath.StartsWith("settings/", StringComparison.OrdinalIgnoreCase))
                {
                    var target = entryPath.EndsWith("riot.yaml", StringComparison.OrdinalIgnoreCase)
                        ? RiotSettingsPath
                        : LolSettingsPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var entryStream = entry.Open();
                    using var mem = new MemoryStream();
                    await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(target, mem.ToArray()).ConfigureAwait(false);
                }
                else if (entryPath.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = entryPath.Substring("config/".Length).Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(RiotConfigPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var entryStream = entry.Open();
                    using var mem = new MemoryStream();
                    await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(destPath, mem.ToArray()).ConfigureAwait(false);
                }
                else if (entryPath.StartsWith("metadata/", StringComparison.OrdinalIgnoreCase))
                {
                    var relative = entryPath.Substring("metadata/".Length).Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(RiotMetadataPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var entryStream = entry.Open();
                    using var mem = new MemoryStream();
                    await entryStream.CopyToAsync(mem).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(destPath, mem.ToArray()).ConfigureAwait(false);
                }
                DebugConsole.WriteLine("[Token] Files restored");
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine( $"[Token] Failed to restore original files after token use {ex} ");
        }
        finally
        {
            try
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch
            {
            }
        }
    }

    private static async Task<bool> AutomateLoginAsync(string riotProcessName, string username, string password)
    {
        var attempts = 0;
        while (attempts < 500)
        {
            attempts++;
            DebugConsole.WriteLine($"[Token] Login automation attempt {attempts}");
            try
            {
                var app = Application.Attach(riotProcessName);
                using var automation = new UIA3Automation();
                var window = app.GetMainWindow(automation);
                var content = window.FindFirstDescendant(cf => cf.ByClassName("Chrome_RenderWidgetHostHWND"));
                if (content == null)
                {
                    DebugConsole.WriteLine("[Token] Could not find content window");
                    await Task.Delay(300);
                    continue;
                }

                var usernameField = content.FindFirstDescendant(cf => cf.ByAutomationId("username"))?.AsTextBox();
                var passwordField = content.FindFirstDescendant(cf => cf.ByAutomationId("password"))?.AsTextBox();
                var rememberBox = content.FindFirstDescendant(cf => cf.ByAutomationId("remember-me"))?.AsCheckBox() ??
                                  content.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox))?.AsCheckBox();
                if (rememberBox == null)
                    DebugConsole.WriteLine("[Token] Remember-me checkbox not found by automationId; using first checkbox");

                var siblings = content.FindAllChildren();
                AutomationElement? signInElement = null;
                if (rememberBox != null)
                {
                    var count = Array.IndexOf(siblings, rememberBox) + 1;
                    while (siblings.Length >= count)
                    {
                        var maybeButton = siblings[count++].AsButton();
                        if (maybeButton != null && maybeButton.ControlType == ControlType.Button)
                        {
                            signInElement = maybeButton;
                            break;
                        }
                    }
                }

                signInElement ??= siblings
                    .Select(x => x.AsButton())
                    .FirstOrDefault(b => b != null && b.ControlType == ControlType.Button);

                if (usernameField == null || passwordField == null || signInElement == null)
                {
                    DebugConsole.WriteLine("[Token] Missing username/password/sign-in controls");
                    await Task.Delay(300);
                    continue;
                }

                usernameField.Text = username;
                passwordField.Text = password;

                if (rememberBox != null && rememberBox.IsChecked != true)
                {
                    try
                    {
                        rememberBox.Focus();
                        rememberBox.Patterns.Toggle.Pattern.Toggle();
                        await Task.Delay(100);
                        rememberBox.IsChecked = true;
                        DebugConsole.WriteLine("[Token] Enabled remember-me checkbox via toggle");
                    }
                    catch
                    {
                        try
                        {
                            rememberBox.IsChecked = true;
                            DebugConsole.WriteLine("[Token] Enabled remember-me checkbox via IsChecked");
                        }
                        catch
                        {
                        }
                    }
                }

                while (!signInElement.IsEnabled)
                {
                    DebugConsole.WriteLine("[Token] Waiting for sign-in button to enable");
                    await Task.Delay(200);
                }
                await Task.Delay(1000);
                signInElement.AsButton()?.Invoke();
                DebugConsole.WriteLine("[Token] Sign-in invoked");

                await Task.Delay(500);

                var restartLogin = false;
                var cancelLogin = false;
                var loginErrorChecks = 0;

                while (true)
                {
                    try
                    {
                        var loginError = window.FindFirstDescendant(cf =>
                            cf.ByControlType(ControlType.ToolTip).And(cf.ByName("Login error")));
                        if (loginError != null)
                        {
                            loginErrorChecks++;
                            DebugConsole.WriteLine($"[Token] Login error tooltip detected (check {loginErrorChecks})");

                            if (loginErrorChecks >= 2)
                            {
                                cancelLogin = true;
                                break;
                            }

                            restartLogin = true;
                            break;
                        }
                    }
                    catch
                    {
                    }

                    var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                    string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    DebugConsole.WriteLine($"[Token] EULA status: {status}");
                    if (status == "\"Accepted\"")
                        break;
                    if (status == "\"AcceptanceRequired\"")
                    {
                        DebugConsole.WriteLine("[Token] Accepting EULA");
                        await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                        await Task.Delay(200);
                    }
                    else
                    {
                        await Task.Delay(500);
                    }
                }

                if (cancelLogin)
                {
                    DebugConsole.WriteLine("[Token] Canceling login after repeated errors");
                    return false;
                }

                if (restartLogin)
                {
                    DebugConsole.WriteLine("[Token] Restarting login flow after error");
                    await Task.Delay(500);
                    continue;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Retrying login automation for token generation");
                DebugConsole.WriteLine($"[Token] Login automation error: {ex.Message}");
                await Task.Delay(500);
            }
        }

        DebugConsole.WriteLine("[Token] Exhausted login automation attempts");

        return false;
    }

    private static string? BackupExistingSettings()
    {
        try
        {
            if (!File.Exists(RiotSettingsPath))
                return null;

            var dir = Path.GetDirectoryName(RiotSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var backupPath = RiotSettingsPath + ".bak";
            File.Copy(RiotSettingsPath, backupPath, true);
            DebugConsole.WriteLine("[Token] Backed up existing RiotGamesPrivateSettings.yaml (kept original)");
            return backupPath;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to backup RiotGamesPrivateSettings.yaml");
            return null;
        }
    }

    private static async Task RestoreBackupAsync(string? backupPath)
    {
        if (string.IsNullOrEmpty(backupPath))
            return;

        try
        {
            if (!File.Exists(backupPath))
                return;

            var dir = Path.GetDirectoryName(RiotSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.Copy(backupPath, RiotSettingsPath, true);
            DebugConsole.WriteLine("[Token] Restored original RiotGamesPrivateSettings.yaml from backup");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to restore RiotGamesPrivateSettings.yaml from backup");
        }
        finally
        {
            try
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch
            {
            }

            await Task.CompletedTask;
        }
    }


    private static async Task<bool> WaitForPersistedLoginAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var resp = await Lcu.Connector("riot", "get", "/riot-login/v1/status", "");
                if (resp != null)
                {
                    var content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    DebugConsole.WriteLine($"[Token] Login status {(int)resp.StatusCode}: {content}");
                    if (content.Contains("\"persist\":true", StringComparison.OrdinalIgnoreCase) &&
                        content.Contains("\"phase\":\"logged_in\"", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

            }
            catch (Exception e)
            {
                DebugConsole.WriteLine($"[Token] Login status {e}");
            }

            await Task.Delay(2000);
        }

        return false;
    }

    private static byte[] Encrypt(byte[] data, string password)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var salt = RandomNumberGenerator.GetBytes(16);
        using var key = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        aes.Key = key.GetBytes(32);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

        var result = new byte[salt.Length + aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(aes.IV, 0, result, salt.Length, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, salt.Length + aes.IV.Length, cipher.Length);
        return result;
    }

    private static byte[]? Decrypt(byte[] data, string password)
    {
        if (data.Length < 32)
            return null;

        var salt = new byte[16];
        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, salt, 0, salt.Length);
        Buffer.BlockCopy(data, salt.Length, iv, 0, iv.Length);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var key = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        aes.Key = key.GetBytes(32);
        aes.IV = iv;

        var cipher = new byte[data.Length - salt.Length - iv.Length];
        Buffer.BlockCopy(data, salt.Length + iv.Length, cipher, 0, cipher.Length);

        using var decryptor = aes.CreateDecryptor();
        try
        {
            return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        catch
        {
            return null;
        }
    }
}

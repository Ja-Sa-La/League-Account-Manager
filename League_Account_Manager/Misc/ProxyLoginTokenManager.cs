using System;
using System.IO;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using League_Account_Manager.Windows;
using Microsoft.Win32;

namespace League_Account_Manager.Misc;

internal static class ProxyLoginTokenManager
{
    private static int _captureInProgress;
    private static TaskCompletionSource<bool> _captureTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<bool> _tokenDetectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void ResetCaptureSignal()
    {
        if (_captureTcs.Task.IsCompleted)
            _captureTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_tokenDetectedTcs.Task.IsCompleted)
            _tokenDetectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static Task WaitForTokenDetectedAsync(CancellationToken cancellationToken = default)
    {
        return _tokenDetectedTcs.Task.WaitAsync(cancellationToken);
    }

    public static Task WaitForCaptureAsync(CancellationToken cancellationToken = default)
    {
        return _captureTcs.Task.WaitAsync(cancellationToken);
    }

    public static async Task CaptureLoginTokenAsync(string responseText)
    {
        if (Interlocked.Exchange(ref _captureInProgress, 1) == 1)
            return;

        try
        {
            var loginToken = ExtractLoginToken(responseText);
            if (string.IsNullOrWhiteSpace(loginToken))
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Login token not found in response.");
                return;
            }

            _tokenDetectedTcs.TrySetResult(true);

            var persistLogin = await PromptPersistLoginAsync();
            if (persistLogin == null)
                return;

            var password = await PromptPasswordAsync("Enter a password to encrypt the login token file.");
            if (string.IsNullOrWhiteSpace(password))
                return;

            var savePath = await PromptSavePathAsync();
            if (string.IsNullOrWhiteSpace(savePath))
                return;

            var payload = new LoginTokenPayload
            {
                AuthenticationType = "RiotAuth",
                LoginToken = loginToken,
                PersistLogin = persistLogin.Value
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var encrypted = Encrypt(Encoding.UTF8.GetBytes(json), password);
            await File.WriteAllBytesAsync(savePath, encrypted);
            DebugConsole.WriteLine($"[ProxyLoginToken] Encrypted login token saved to {savePath}");
            _captureTcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] Failed to capture login token: {ex.Message}");
            _captureTcs.TrySetException(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _captureInProgress, 0);
        }
    }

    public static async Task<bool> UseLoginTokenAsync()
    {
        try
        {
            var riotRunning = Process.GetProcessesByName("Riot Client").Length != 0 ||
                              Process.GetProcessesByName("RiotClientUx").Length != 0;
            if (!riotRunning)
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Riot client is not running.");
                return false;
            }

            var openPath = await PromptOpenPathAsync();
            if (string.IsNullOrWhiteSpace(openPath))
                return false;

            var password = await PromptPasswordAsync("Enter the password to decrypt the login token file.");
            if (string.IsNullOrWhiteSpace(password))
                return false;

            var encrypted = await File.ReadAllBytesAsync(openPath);
            var decrypted = Decrypt(encrypted, password);
            if (decrypted == null)
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Failed to decrypt login token file.");
                return false;
            }

            var payload = JsonSerializer.Deserialize<LoginTokenPayload>(decrypted, JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.LoginToken))
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Login token payload missing or invalid.");
                return false;
            }

            DebugConsole.WriteLine($"[ProxyLoginToken] Decrypted payload: {JsonSerializer.Serialize(payload, JsonOptions)}");

            var loginPayload = JsonSerializer.Serialize(payload, JsonOptions);
            DebugConsole.WriteLine("[ProxyLoginToken] Sending /rso-auth/v1/session/login-token payload.");
            dynamic? credentialsResponse;
            try
            {
                credentialsResponse = await Lcu.Connector("riot", "put", "/rso-auth/v1/session/login-token", loginPayload);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ProxyLoginToken] /rso-auth/v1/session/login-token failed: {ex}");
                return false;
            }

            await LogResponseAsync("/rso-auth/v1/session/login-token", credentialsResponse);
            var authorizationPayload = JsonSerializer.Serialize(new
            {
                clientId = "riot-client",
                trustLevels = new[] { "always_trusted" }
            }, JsonOptions);

            DebugConsole.WriteLine("[ProxyLoginToken] Sending /rso-auth/v2/authorizations payload.");
            dynamic? authorizationResponse;
            try
            {
                authorizationResponse = await Lcu.Connector("riot", "post", "/rso-auth/v2/authorizations", authorizationPayload);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ProxyLoginToken] /rso-auth/v2/authorizations failed: {ex}");
                return false;
            }

            await LogResponseAsync("/rso-auth/v2/authorizations", authorizationResponse);

            var success = credentialsResponse != null && authorizationResponse != null;
            DebugConsole.WriteLine($"[ProxyLoginToken] Token login completed: {success}");
            return success;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] Failed to use login token: {ex}");
            return false;
        }
    }

    private static async Task LogResponseAsync(string endpoint, dynamic response)
    {
        if (response == null)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response: <null>");
            return;
        }

        try
        {
            var status = response.StatusCode;
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response: {(int)status} {status} body={content}");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response logging failed: {ex.Message}");
        }
    }

    private static string? ExtractLoginToken(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        try
        {
            var node = JsonNode.Parse(responseText);
            if (node is JsonObject obj && obj["success"] is JsonObject success)
            {
                var token = success["login_token"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }

            return FindLoginToken(node);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLoginToken(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, "login_token", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "loginToken", StringComparison.OrdinalIgnoreCase))
                    return kvp.Value?.GetValue<string>();

                var nested = FindLoginToken(kvp.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var nested = FindLoginToken(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static async Task<bool?> PromptPersistLoginAsync()
    {
        if (Application.Current?.Dispatcher == null)
            return false;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show("Keep this token logged in on this PC?", "Persist Login",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    }

    private static async Task<string?> PromptPasswordAsync(string message)
    {
        if (Application.Current?.Dispatcher == null)
            return null;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var prompt = new PasswordPrompt(message);
            var result = prompt.ShowDialog();
            return result == true ? prompt.Password : null;
        });
    }

    private static async Task<string?> PromptSavePathAsync()
    {
        if (Application.Current?.Dispatcher == null)
            return null;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Encrypted token (*.enc)|*.enc|All Files (*.*)|*.*",
                FileName = "RiotLoginToken.enc"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
    }

    private static async Task<string?> PromptOpenPathAsync()
    {
        if (Application.Current?.Dispatcher == null)
            return null;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Encrypted token (*.enc)|*.enc|All Files (*.*)|*.*"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

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

    private sealed class LoginTokenPayload
    {
        [JsonPropertyName("authentication_type")]
        public string AuthenticationType { get; set; } = "RiotAuth";

        [JsonPropertyName("login_token")]
        public string LoginToken { get; set; } = string.Empty;

        [JsonPropertyName("persist_login")]
        public bool PersistLogin { get; set; }
    }
}

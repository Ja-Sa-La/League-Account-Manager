using Microsoft.Win32;
using Notification.Wpf;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;

namespace League_Account_Manager.Misc;

internal static class ProxyLoginTokenManager
{
    private const string LoginUriScheme = "leagueaccountmanager";
    private const string LoginUriHost = "login";
    private const string LoginRedirectBaseUrl = "https://redirect.leagueaccountmanager.xyz/login";
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
    public static async Task<bool?> PromptPersistLoginAsync()
    {
        if (Application.Current?.Dispatcher == null)
            return false;

        return await Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show("Allow user to stay logged in?", "Persist Login",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    }
    public static void RegisterLoginUriScheme()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{LoginUriScheme}");
            if (key == null)
                return;

            key.SetValue(string.Empty, "URL:League Account Manager Login");
            key.SetValue("URL Protocol", string.Empty);

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(string.Empty, $"\"{exePath}\",1");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] Failed to register URI scheme: {ex.Message}");
        }
    }

    public static async Task TryHandleLoginUriAsync(string[]? args)
    {
        DebugConsole.WriteLine("[ProxyLoginToken] Handling Uri");
        if (args == null || args.Length == 0)
            return;

        var uriArg = args.FirstOrDefault(arg =>
            arg.StartsWith($"{LoginUriScheme}://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("https://redirect.leagueaccountmanager.xyz/", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(uriArg))
            return;

        var token = ExtractTokenFromText(uriArg);
        if (string.IsNullOrWhiteSpace(token))
        {
            DebugConsole.WriteLine("[ProxyLoginToken] Login URI missing token.");
            return;
        }

        await UseLoginTokenAsync(token);
    }

    public static async Task CaptureLoginTokenAsync(string responseText, bool? persistLogin = false)
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

            var payload = new LoginTokenPayload
            {
                AuthenticationType = "RiotAuth",
                LoginToken = loginToken,
                PersistLogin = persistLogin.Value
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var loginUri = BuildLoginUri(encodedToken);
            var clipboardText = loginUri == null ? encodedToken : FormatDiscordLoginLink(loginUri);
            if (!await TrySetClipboardTextAsync(clipboardText))
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Failed to copy login token to clipboard.");
                return;
            }

            DebugConsole.WriteLine("[ProxyLoginToken] Encrypted login token copied to clipboard.");
            if (loginUri != null)
                DebugConsole.WriteLine($"[ProxyLoginToken] Login link: {loginUri}");
            Notif.notificationManager.Show("Token Ready",
                "Token copied to clipboard and can be pasted to Discord.",
                NotificationType.Notification);
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
        var encodedToken = await TryGetLoginTokenFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            DebugConsole.WriteLine("[ProxyLoginToken] Clipboard does not contain a login token.");
            return false;
        }

        return await UseLoginTokenAsync(encodedToken);
    }

    private static async Task<bool> UseLoginTokenAsync(string encodedToken)
    {
        try
        {
            if (!await CheckLeague()) throw new Exception("League not installed");
            Utils.KillLeagueFunc2();
            Process riotProcess = Process.Start(Misc.Settings.settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            int num = 0;
            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0)
                {
                    break;
                }

                if (Process.GetProcessesByName("RiotClientUx").Length != 0)
                {
                    break;
                }


                Thread.Sleep(200);
                num++;
                if (num == 20)
                {
                    DebugConsole.WriteLine("[ProxyLoginToken] Riot client is not running.");
                    return false;
                }
            }

            while (true)
            {
                var readyResp = await Lcu.Connector("riot", "get", "/rso-auth/configuration/v3/ready-state", "");
                if (readyResp != null)
                {
                    var readyBody = await readyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        var node = JsonNode.Parse(readyBody);
                        var ready = node?["ready"]?.GetValue<bool>() ?? false;
                        if (ready)
                            break;
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(200);
            }

            byte[] encrypted;
            try
            {
                encrypted = Convert.FromBase64String(encodedToken);
            }
            catch (FormatException)
            {
                DebugConsole.WriteLine("[ProxyLoginToken] Login token is not valid base64.");
                return false;
            }

            var payload = JsonSerializer.Deserialize<LoginTokenPayload>(encrypted, JsonOptions);
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

            if (credentialsResponse != null && (int)credentialsResponse.StatusCode == 400)
            {
                var errorBody = await credentialsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(errorBody) &&
                    errorBody.Contains("auth_failure", StringComparison.OrdinalIgnoreCase))
                {
                    Notif.notificationManager.Show("Invalid Token",
                        "The login token is not valid.",
                        NotificationType.Error);
                    return false;
                }
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
            while (true){
            var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
            string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            DebugConsole.WriteLine($"[Accounts] EULA status: {status}");
            if (status == "\"Accepted\"") break;
            if (status == "\"AcceptanceRequired\"")
            {
                await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                Thread.Sleep(200);
            }
            else
            {
                Thread.Sleep(500);
            }
            }
            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");

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
    public static async Task<bool> CheckLeague()
    {
        if (File.Exists(Misc.Settings.settingsloaded.riotPath))
            return true;
        return false;
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

    private static async Task<string?> TryGetLoginTokenFromClipboardAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var tokenFromText = ExtractTokenFromText(text.Trim());
        return string.IsNullOrWhiteSpace(tokenFromText) ? text.Trim() : tokenFromText;
    }

    private static string? ExtractTokenFromText(string text)
    {
        var tokenFromMarkdown = ExtractTokenFromMarkdown(text);
        if (!string.IsNullOrWhiteSpace(tokenFromMarkdown))
            return tokenFromMarkdown;

        return ExtractTokenFromUri(text);
    }

    private static string? ExtractTokenFromMarkdown(string text)
    {
        var openParen = text.IndexOf('(');
        if (openParen < 0)
            return null;

        var closeParen = text.LastIndexOf(')');
        if (closeParen <= openParen)
            return null;

        var url = text.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return ExtractTokenFromUri(url);
    }

    private static string? ExtractTokenFromUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme.Equals(LoginUriScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.Host.Equals(LoginUriHost, StringComparison.OrdinalIgnoreCase))
                return null;
        }
        else if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.Host.Equals("redirect.leagueaccountmanager.xyz", StringComparison.OrdinalIgnoreCase))
                return null;
        }
        else
        {
            return null;
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return null;

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (!parts[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = parts.Length > 1 ? parts[1] : string.Empty;
            return Uri.UnescapeDataString(value);
        }

        return null;
    }

    private static string? BuildLoginUri(string encodedToken)
    {
        if (string.IsNullOrWhiteSpace(encodedToken))
            return null;

        var escapedToken = Uri.EscapeDataString(encodedToken);
        return $"{LoginRedirectBaseUrl}?token={escapedToken}";
    }

    private static string FormatDiscordLoginLink(string loginUri)
    {
        return $"[Click to login to account]({loginUri})";
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




    private static async Task<bool> TrySetClipboardTextAsync(string text)
    {
        if (Application.Current?.Dispatcher != null)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Clipboard.SetText(text);
                return true;
            });
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            return await tcs.Task;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryGetClipboardTextAsync()
    {
        if (Application.Current?.Dispatcher != null)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
                Clipboard.ContainsText() ? Clipboard.GetText() : null);
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                tcs.TrySetResult(text);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            return await tcs.Task;
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };


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

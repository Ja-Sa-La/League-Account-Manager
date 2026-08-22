using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using League_Account_Manager.Windows;
using Microsoft.Win32;
using Notification.Wpf;

namespace League_Account_Manager.Misc;

internal static class ProxyLoginTokenManager
{
    private const string LoginUriScheme = "leagueaccountmanager";
    private const string LoginUriHost = "login";
    private const string LoginRedirectBaseUrl = "https://redirect.leagueaccountmanager.xyz/login";
    private const string ProductLeague = "league";
    private const string ProductValorant = "valorant";
    private static int _captureInProgress;
    private static TaskCompletionSource<bool> _captureTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> _tokenDetectedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

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
            AppMessageBox.Show("Allow user to stay logged in?", "Persist Login", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);
    }

    public static void RegisterLoginUriScheme()
    {
        LogFlow("URI", "RegisterLoginUriScheme invoked.");
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                LogFlow("URI", "Skipping URI scheme registration: executable path is empty.", ConsoleColor.Yellow);
                return;
            }

            LogFlow("URI", $"Registering URI scheme '{LoginUriScheme}' for executable '{exePath}'.");

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{LoginUriScheme}");
            if (key == null)
            {
                LogFlow("URI", "Failed to create URI scheme registry key.", ConsoleColor.Red);
                return;
            }

            key.SetValue(string.Empty, "URL:League Account Manager Login");
            key.SetValue("URL Protocol", string.Empty);

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(string.Empty, $"\"{exePath}\",1");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");

            LogFlow("URI", $"URI scheme '{LoginUriScheme}' registered successfully.");
        }
        catch (Exception ex)
        {
            LogFlow("URI", $"Failed to register URI scheme: {ex.Message}", ConsoleColor.Red);
        }
    }

    public static async Task TryHandleLoginUriAsync(string[]? args)
    {
        LogFlow("URI", "TryHandleLoginUriAsync invoked.");
        if (args == null || args.Length == 0)
        {
            LogFlow("URI", "No startup args provided; URI handling skipped.", ConsoleColor.Yellow);
            return;
        }

        LogFlow("URI", $"Startup args count: {args.Length}");

        var uriArg = args.FirstOrDefault(arg =>
            arg.StartsWith($"{LoginUriScheme}://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("https://redirect.leagueaccountmanager.xyz/", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(uriArg))
        {
            LogFlow("URI", "No supported login URI found in startup args.", ConsoleColor.Yellow);
            return;
        }

        LogFlow("URI", $"Matched startup URI: {uriArg}");

        var token = ExtractTokenFromText(uriArg);
        if (string.IsNullOrWhiteSpace(token))
        {
            LogFlow("URI", "Login URI missing token.", ConsoleColor.Red);
            return;
        }

        LogFlow("URI", $"Token extracted from URI successfully. Length={token.Length}");

        var product = GetProductFromEncodedTokenOrDefault(token);
        LogFlow("URI", $"Token product detected: {product}");

        if (product == ProductValorant)
        {
            LogFlow("URI", "Dispatching token login to Valorant handler.");
            await UseLoginTokenValorantAsync(token);
        }
        else
        {
            LogFlow("URI", "Dispatching token login to League handler.");
            await UseLoginTokenAsync(token);
        }

        LogFlow("URI", "TryHandleLoginUriAsync completed.");
    }

    public static async Task CaptureLoginTokenAsync(string responseText, bool? persistLogin = false,
        string product = ProductLeague)
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
                PersistLogin = persistLogin ?? false,
                Product = NormalizeProduct(product)
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
        LogFlow("League", "UseLoginTokenAsync invoked (clipboard source).");
        var encodedToken = await TryGetLoginTokenFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            LogFlow("League", "Clipboard does not contain a login token.", ConsoleColor.Yellow);
            return false;
        }

        LogFlow("League", "Token extracted from clipboard successfully.");
        return await UseLoginTokenAsync(encodedToken);
    }

    private static async Task<bool> UseLoginTokenAsync(string encodedToken)
    {
        try
        {
            LogFlow("League", "Token login flow started.");
            if (!await CheckLeague()) throw new Exception("League not installed");
            LogFlow("League", "Riot executable path validated.");

            LogFlow("League", "Killing existing client processes (KillLeagueFunc2).");
            Utils.KillLeagueFunc2();
            var riotProcess = Process.Start(Settings.settingsloaded.riotPath,
                "--launch-product=league_of_legends --launch-patchline=live");
            LogFlow("League", riotProcess != null
                ? $"Riot launch command executed. PID={riotProcess.Id}"
                : "Riot launch command executed. Process object is null.");

            LogFlow("League", "Waiting for Riot client process to appear...");
            var num = 0;
            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0) break;

                if (Process.GetProcessesByName("RiotClientUx").Length != 0) break;


                Thread.Sleep(200);
                num++;
                if (num == 200)
                {
                    LogFlow("League", "Riot client process did not appear in time.", ConsoleColor.Red);
                    return false;
                }
            }
            LogFlow("League", "Riot client process detected.");

            LogFlow("League", "Waiting for /rso-auth ready state...");
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
                        LogFlow("League", "Ready-state payload parse failed; retrying.", ConsoleColor.Yellow);
                    }
                }

                await Task.Delay(200);
            }
            LogFlow("League", "Riot ready state reached.");

            byte[] encrypted;
            try
            {
                encrypted = Convert.FromBase64String(encodedToken);
                LogFlow("League", "Base64 token decode successful.");
            }
            catch (FormatException)
            {
                LogFlow("League", "Login token is not valid base64.", ConsoleColor.Red);
                return false;
            }

            var payload = JsonSerializer.Deserialize<LoginTokenPayload>(encrypted, JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.LoginToken))
            {
                LogFlow("League", "Login token payload missing or invalid.", ConsoleColor.Red);
                return false;
            }
            LogFlow("League", "Token payload deserialized successfully.");

            DebugConsole.WriteLine("[ProxyLoginToken] Login token payload validated.");

            var loginPayload = JsonSerializer.Serialize(payload, JsonOptions);
            LogFlow("League", "Sending /rso-auth/v1/session/login-token payload.");
            dynamic? credentialsResponse;
            try
            {
                credentialsResponse =
                    await Lcu.Connector("riot", "put", "/rso-auth/v1/session/login-token", loginPayload);
                LogFlow("League", "/rso-auth/v1/session/login-token request completed.");
            }
            catch (Exception ex)
            {
                LogFlow("League", $"/rso-auth/v1/session/login-token failed: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            if (credentialsResponse is HttpResponseMessage credentialsHttp &&
                credentialsHttp.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await credentialsHttp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(errorBody) &&
                    errorBody.Contains("auth_failure", StringComparison.OrdinalIgnoreCase))
                {
                    LogFlow("League", "Riot API rejected login token (auth_failure).", ConsoleColor.Red);
                    Notif.notificationManager.Show("Invalid Token",
                        "The login token is not valid.",
                        NotificationType.Error);
                    return false;
                }
            }

            await LogResponseAsync("/rso-auth/v1/session/login-token", credentialsResponse);
            if (!IsSuccessfulResponse(credentialsResponse))
            {
                LogFlow("League", "Riot API rejected the login-token request.", ConsoleColor.Red);
                return false;
            }
            LogFlow("League", "Preparing /rso-auth/v2/authorizations payload.");
            var authorizationPayload = JsonSerializer.Serialize(new
            {
                clientId = "riot-client",
                trustLevels = new[] { "always_trusted" }
            }, JsonOptions);

            LogFlow("League", "Sending /rso-auth/v2/authorizations payload.");
            dynamic? authorizationResponse;
            try
            {
                authorizationResponse =
                    await Lcu.Connector("riot", "post", "/rso-auth/v2/authorizations", authorizationPayload);
                LogFlow("League", "/rso-auth/v2/authorizations request completed.");
            }
            catch (Exception ex)
            {
                LogFlow("League", $"/rso-auth/v2/authorizations failed: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            await LogResponseAsync("/rso-auth/v2/authorizations", authorizationResponse);

            var success = IsSuccessfulResponse(authorizationResponse);
            LogFlow("League", $"Token authentication stage completed: {success}");
            if (!success)
                return false;

            LogFlow("League", "Checking EULA acceptance state...");
            string? lastEulaStatus = null;
            while (true)
            {
                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.Equals(status, lastEulaStatus, StringComparison.Ordinal))
                {
                    lastEulaStatus = status;
                    DebugConsole.WriteLine($"[ProxyLoginToken][League] EULA status changed: {status}");
                }
                if (status == "\"Accepted\"") break;
                if (status == "\"AcceptanceRequired\"")
                {
                    LogFlow("League", "EULA acceptance required; sending acceptance request.");
                    await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                    Thread.Sleep(200);
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
            LogFlow("League", "EULA accepted.");

            LogFlow("League", "Launching League product patchline.");
            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/league_of_legends/patchlines/live", "");
            LogFlow("League", "League product launch request sent successfully.");
            LogFlow("League", $"UseLoginTokenAsync finished. Success={success}");

            return success;
        }
        catch (Exception ex)
        {
            LogFlow("League", $"Failed to use login token: {ex}", ConsoleColor.Red);
            return false;
        }
    }

    public static async Task<bool> UseLoginTokenValorantAsync()
    {
        LogFlow("Valorant", "UseLoginTokenValorantAsync invoked (clipboard source).");
        var encodedToken = await TryGetLoginTokenFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            LogFlow("Valorant", "Clipboard does not contain a login token.", ConsoleColor.Yellow);
            return false;
        }

        LogFlow("Valorant", "Token extracted from clipboard successfully.");
        return await UseLoginTokenValorantAsync(encodedToken);
    }
    private static async Task<bool> UseLoginTokenValorantAsync(string encodedToken)
    {
        try
        {
            LogFlow("Valorant", "Token login flow started.");
            if (!await CheckLeague()) throw new Exception("valorant not installed");
            LogFlow("Valorant", "Riot executable path validated.");

            LogFlow("Valorant", "Killing existing client processes (KillLeagueFunc2).");
            Utils.KillLeagueFunc2();
            var riotProcess = Process.Start(Settings.settingsloaded.riotPath,
                "--launch-product=valorant --launch-patchline=live");
            LogFlow("Valorant", riotProcess != null
                ? $"Riot launch command executed. PID={riotProcess.Id}"
                : "Riot launch command executed. Process object is null.");

            LogFlow("Valorant", "Waiting for Riot client process to appear...");
            var num = 0;
            while (true)
            {
                if (Process.GetProcessesByName("Riot Client").Length != 0) break;

                if (Process.GetProcessesByName("RiotClientUx").Length != 0) break;


                Thread.Sleep(200);
                num++;
                if (num == 20)
                {
                    LogFlow("Valorant", "Riot client process did not appear in time.", ConsoleColor.Red);
                    return false;
                }
            }
            LogFlow("Valorant", "Riot client process detected.");

            LogFlow("Valorant", "Waiting for /rso-auth ready state...");
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
                        LogFlow("Valorant", "Ready-state payload parse failed; retrying.", ConsoleColor.Yellow);
                    }
                }

                await Task.Delay(200);
            }
            LogFlow("Valorant", "Riot ready state reached.");

            byte[] encrypted;
            try
            {
                encrypted = Convert.FromBase64String(encodedToken);
                LogFlow("Valorant", "Base64 token decode successful.");
            }
            catch (FormatException)
            {
                LogFlow("Valorant", "Login token is not valid base64.", ConsoleColor.Red);
                return false;
            }

            var payload = JsonSerializer.Deserialize<LoginTokenPayload>(encrypted, JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.LoginToken))
            {
                LogFlow("Valorant", "Login token payload missing or invalid.", ConsoleColor.Red);
                return false;
            }
            LogFlow("Valorant", "Token payload deserialized successfully.");

            DebugConsole.WriteLine("[ProxyLoginToken] Login token payload validated.");

            var loginPayload = JsonSerializer.Serialize(payload, JsonOptions);
            LogFlow("Valorant", "Sending /rso-auth/v1/session/login-token payload.");
            dynamic? credentialsResponse;
            try
            {
                credentialsResponse =
                    await Lcu.Connector("riot", "put", "/rso-auth/v1/session/login-token", loginPayload);
                LogFlow("Valorant", "/rso-auth/v1/session/login-token request completed.");
            }
            catch (Exception ex)
            {
                LogFlow("Valorant", $"/rso-auth/v1/session/login-token failed: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            if (credentialsResponse is HttpResponseMessage credentialsHttp &&
                credentialsHttp.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await credentialsHttp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(errorBody) &&
                    errorBody.Contains("auth_failure", StringComparison.OrdinalIgnoreCase))
                {
                    LogFlow("Valorant", "Riot API rejected login token (auth_failure).", ConsoleColor.Red);
                    Notif.notificationManager.Show("Invalid Token",
                        "The login token is not valid.",
                        NotificationType.Error);
                    return false;
                }
            }

            await LogResponseAsync("/rso-auth/v1/session/login-token", credentialsResponse);
            if (!IsSuccessfulResponse(credentialsResponse))
            {
                LogFlow("Valorant", "Riot API rejected the login-token request.", ConsoleColor.Red);
                return false;
            }
            LogFlow("Valorant", "Preparing /rso-auth/v2/authorizations payload.");
            var authorizationPayload = JsonSerializer.Serialize(new
            {
                clientId = "riot-client",
                trustLevels = new[] { "always_trusted" }
            }, JsonOptions);

            LogFlow("Valorant", "Sending /rso-auth/v2/authorizations payload.");
            dynamic? authorizationResponse;
            try
            {
                authorizationResponse =
                    await Lcu.Connector("riot", "post", "/rso-auth/v2/authorizations", authorizationPayload);
                LogFlow("Valorant", "/rso-auth/v2/authorizations request completed.");
            }
            catch (Exception ex)
            {
                LogFlow("Valorant", $"/rso-auth/v2/authorizations failed: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            await LogResponseAsync("/rso-auth/v2/authorizations", authorizationResponse);

            var success = IsSuccessfulResponse(authorizationResponse);
            LogFlow("Valorant", $"Token authentication stage completed: {success}");
            if (!success)
                return false;
            LogFlow("Valorant", "Checking EULA acceptance state...");
            string? lastEulaStatus = null;
            while (true)
            {
                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                string status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.Equals(status, lastEulaStatus, StringComparison.Ordinal))
                {
                    lastEulaStatus = status;
                    DebugConsole.WriteLine($"[ProxyLoginToken][Valorant] EULA status changed: {status}");
                }
                if (status == "\"Accepted\"") break;
                if (status == "\"AcceptanceRequired\"")
                {
                    LogFlow("Valorant", "EULA acceptance required; sending acceptance request.");
                    await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                    Thread.Sleep(200);
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
            LogFlow("Valorant", "EULA accepted.");

            LogFlow("Valorant", "Launching Valorant product patchline.");
            await Lcu.Connector("riot", "post",
                "/product-launcher/v1/products/valorant/patchlines/live", "");
            LogFlow("Valorant", "Valorant product launch request sent successfully.");
            LogFlow("Valorant", $"UseLoginTokenValorantAsync finished. Success={success}");

            return success;
        }
        catch (Exception ex)
        {
            LogFlow("Valorant", $"Failed to use login token: {ex}", ConsoleColor.Red);
            return false;
        }
    }

    private static void LogFlow(string flow, string message, ConsoleColor color = ConsoleColor.White)
    {
        DebugConsole.WriteLine($"[ProxyLoginToken][{flow}] {message}", color);
    }

    private static async Task LogResponseAsync(string endpoint, object? response)
    {
        if (response is not HttpResponseMessage httpResponse)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response: <null>");
            return;
        }

        try
        {
            var status = httpResponse.StatusCode;
            var contentLength = httpResponse.Content.Headers.ContentLength;
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response: {(int)status} {status}" +
                                   (contentLength.HasValue ? $" bytes={contentLength.Value}" : string.Empty));
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[ProxyLoginToken] {endpoint} response logging failed: {ex.Message}");
        }
    }

    internal static bool IsSuccessfulResponse(object? response)
    {
        return response is HttpResponseMessage { IsSuccessStatusCode: true };
    }

    public static async Task<bool> CheckLeague()
    {
        if (File.Exists(Settings.settingsloaded.riotPath))
            return true;
        return false;
    }

    internal static string GetProductFromEncodedTokenOrDefault(string encodedToken)
    {
        try
        {
            var bytes = Convert.FromBase64String(encodedToken);
            var payload = JsonSerializer.Deserialize<LoginTokenPayload>(bytes, JsonOptions);
            return NormalizeProduct(payload?.Product);
        }
        catch
        {
            return ProductLeague;
        }
    }

    private static string NormalizeProduct(string? product)
    {
        if (string.Equals(product, ProductValorant, StringComparison.OrdinalIgnoreCase))
            return ProductValorant;

        return ProductLeague;
    }

    internal static string? ExtractLoginToken(string responseText)
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

    internal static string? ExtractTokenFromText(string text)
    {
        LogFlow("URI", "ExtractTokenFromText started.");
        var tokenFromMarkdown = ExtractTokenFromMarkdown(text);
        if (!string.IsNullOrWhiteSpace(tokenFromMarkdown))
        {
            LogFlow("URI", "Token extracted from markdown wrapper.");
            return tokenFromMarkdown;
        }

        var tokenFromUri = ExtractTokenFromUri(text);
        if (!string.IsNullOrWhiteSpace(tokenFromUri))
            LogFlow("URI", "Token extracted from direct URI.");
        else
            LogFlow("URI", "Token extraction failed from provided text.", ConsoleColor.Yellow);
        return tokenFromUri;
    }

    private static string? ExtractTokenFromMarkdown(string text)
    {
        LogFlow("URI", "Attempting markdown token extraction.");
        var openParen = text.IndexOf('(');
        if (openParen < 0)
            return null;

        var closeParen = text.LastIndexOf(')');
        if (closeParen <= openParen)
            return null;

        var url = text.Substring(openParen + 1, closeParen - openParen - 1).Trim();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        LogFlow("URI", "Markdown URL found, attempting URI token extraction.");

        return ExtractTokenFromUri(url);
    }

    private static string? ExtractTokenFromUri(string uriText)
    {
        LogFlow("URI", "Attempting URI token extraction.");
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            LogFlow("URI", "Invalid URI text; cannot parse absolute URI.", ConsoleColor.Yellow);
            return null;
        }

        if (uri.Scheme.Equals(LoginUriScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.Host.Equals(LoginUriHost, StringComparison.OrdinalIgnoreCase))
            {
                LogFlow("URI", $"Unsupported custom URI host '{uri.Host}'.", ConsoleColor.Yellow);
                return null;
            }
        }
        else if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.Host.Equals("redirect.leagueaccountmanager.xyz", StringComparison.OrdinalIgnoreCase))
            {
                LogFlow("URI", $"Unsupported HTTPS host '{uri.Host}'.", ConsoleColor.Yellow);
                return null;
            }
        }
        else
        {
            LogFlow("URI", $"Unsupported URI scheme '{uri.Scheme}'.", ConsoleColor.Yellow);
            return null;
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            LogFlow("URI", "URI query string is empty.", ConsoleColor.Yellow);
            return null;
        }

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (!parts[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = parts.Length > 1 ? parts[1] : string.Empty;
            LogFlow("URI", "Token parameter found in URI query.");
            return Uri.UnescapeDataString(value);
        }

        LogFlow("URI", "Token parameter not found in URI query.", ConsoleColor.Yellow);
        return null;
    }

    internal static string? BuildLoginUri(string encodedToken)
    {
        if (string.IsNullOrWhiteSpace(encodedToken))
            return null;

        var escapedToken = Uri.EscapeDataString(encodedToken);
        return $"{LoginRedirectBaseUrl}?token={escapedToken}";
    }

    internal static string FormatDiscordLoginLink(string loginUri)
    {
        return $"[Click to login to account]({loginUri})";
    }

    private static string? FindLoginToken(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, "login_token", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, "loginToken", StringComparison.OrdinalIgnoreCase))
                    return kvp.Value?.GetValue<string>();

                var nested = FindLoginToken(kvp.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        else if (node is JsonArray array)
            foreach (var item in array)
            {
                var nested = FindLoginToken(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }

        return null;
    }


    private static async Task<bool> TrySetClipboardTextAsync(string text)
    {
        if (Application.Current?.Dispatcher != null)
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Clipboard.SetText(text);
                return true;
            });

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
            return await Application.Current.Dispatcher.InvokeAsync(() =>
                Clipboard.ContainsText() ? Clipboard.GetText() : null);

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


    private sealed class LoginTokenPayload
    {
        [JsonPropertyName("authentication_type")]
        public string AuthenticationType { get; set; } = "RiotAuth";

        [JsonPropertyName("login_token")] public string LoginToken { get; set; } = string.Empty;

        [JsonPropertyName("persist_login")] public bool PersistLogin { get; set; }

        [JsonPropertyName("product")] public string Product { get; set; } = ProductLeague;
    }
}
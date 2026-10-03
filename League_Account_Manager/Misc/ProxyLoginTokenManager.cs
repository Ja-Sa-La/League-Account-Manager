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
    private const string LoginRedirectBaseUrl = "https://lam.monster/login";
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

    /// <summary>
    ///     Asks whether a shared session should stay logged in. Sharing flows always ask
    ///     (they must never silently apply the personal Always/Never login preference), so this
    ///     delegates to <see cref="RiotLoginManager.PromptPersistLoginAsync"/> with
    ///     <c>alwaysPrompt: true</c>. Normal-login prompt calls go to RiotLoginManager directly.
    /// </summary>
    public static Task<bool?> PromptPersistLoginForShareAsync()
    {
        return RiotLoginManager.PromptPersistLoginAsync(alwaysPrompt: true);
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
            arg.StartsWith("https://lam.monster/", StringComparison.OrdinalIgnoreCase));
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

        var payload = DecodeLoginTokenPayload(token);
        if (payload == null || string.IsNullOrWhiteSpace(payload.LoginToken))
        {
            LogFlow("URI", "Token payload did not contain a login token.", ConsoleColor.Red);
            return;
        }

        if (product == ProductValorant)
        {
            LogFlow("URI", "Dispatching token login to Valorant handler.");
            await RiotLoginManager.UseLoginTokenValorantAsync(payload.LoginToken, payload.PersistLogin);
        }
        else
        {
            LogFlow("URI", "Dispatching token login to League handler.");
            await RiotLoginManager.UseLoginTokenAsync(payload.LoginToken, payload.PersistLogin);
        }

        LogFlow("URI", "TryHandleLoginUriAsync completed.");
    }

    /// <summary>
    ///     Mints a fresh login token from the currently authenticated Riot Client session by calling
    ///     the client's own <c>POST /rso-authenticator/v1/authentication/redirect</c> endpoint
    ///     (empty JSON object body is the only accepted request shape) and copies it to the
    ///     clipboard in the standard shareable format. On another PC that has never been
    ///     authenticated, <see cref="UseLoginTokenAsync"/> / <see cref="UseLoginTokenValorantAsync"/>
    ///     redeem the token in a freshly started Riot Client — no credentials required.
    /// </summary>
    public static async Task GenerateTokenFromCurrentSessionAsync(string product = ProductLeague)
    {
        product = NormalizeProduct(product);
        LogFlow("Session", $"Exporting login token from the running Riot Client session ({product}).");

        if (Process.GetProcessesByName("Riot Client").Length == 0 &&
            Process.GetProcessesByName("RiotClientUx").Length == 0)
        {
            LogFlow("Session", "Riot Client is not running; nothing to export.", ConsoleColor.Yellow);
            Notif.notificationManager.Show("Token from session",
                "The Riot Client is not running. Log in first, then export the session.",
                NotificationType.Error);
            return;
        }

        HttpResponseMessage? response;
        try
        {
            response = await Lcu.Connector("riot", "post",
                "/rso-authenticator/v1/authentication/redirect", "{}");
        }
        catch (Exception ex)
        {
            LogFlow("Session", $"Session export request failed: {ex.Message}", ConsoleColor.Red);
            Notif.notificationManager.Show("Token from session",
                "Could not talk to the Riot Client.", NotificationType.Error);
            return;
        }

        if (response is not HttpResponseMessage { IsSuccessStatusCode: true } http)
        {
            LogFlow("Session", "Session export rejected; the client may not be logged in.", ConsoleColor.Red);
            Notif.notificationManager.Show("Token from session",
                "Could not mint a login token. Make sure the Riot Client is logged in.",
                NotificationType.Error);
            return;
        }

        var body = await http.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(ExtractLoginToken(body)))
        {
            LogFlow("Session", "Response did not contain a login token.", ConsoleColor.Red);
            Notif.notificationManager.Show("Token from session",
                "The Riot Client has no active session to export.", NotificationType.Error);
            return;
        }

        // The token is handed to other users, so always ask about persist login (same policy as
        // the clientless Generate token flow) before copying to the clipboard.
        var persist = await PromptPersistLoginForShareAsync();
        await CaptureLoginTokenAsync(body, persistLogin: persist, product: product);
    }

    /// <summary>
    ///     Mints a fresh login token from the Riot Client session that is signed in right now and
    ///     returns the raw token. Unlike <see cref="GenerateTokenFromCurrentSessionAsync"/> this
    ///     does not prompt or touch the clipboard — the default login uses it to persist a session
    ///     it just created, and the token the client was signed in with is already spent.
    /// </summary>
    public static async Task<string?> MintLoginTokenFromSessionAsync()
    {
        HttpResponseMessage? response;
        try
        {
            response = await Lcu.Connector("riot", "post",
                "/rso-authenticator/v1/authentication/redirect", "{}");
        }
        catch (Exception ex)
        {
            LogFlow("Session", $"Minting a session token failed: {ex.Message}", ConsoleColor.Red);
            return null;
        }

        if (response is not HttpResponseMessage { IsSuccessStatusCode: true } http)
        {
            LogFlow("Session", "The client refused to mint a login token from the session.",
                ConsoleColor.Red);
            return null;
        }

        var body = await http.Content.ReadAsStringAsync().ConfigureAwait(false);
        var token = ExtractLoginToken(body);
        if (string.IsNullOrWhiteSpace(token))
        {
            LogFlow("Session", "The session response did not contain a login token.", ConsoleColor.Red);
            return null;
        }

        LogFlow("Session", "Minted a fresh login token from the signed-in session.");
        return token;
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
        LogFlow("Token", "UseLoginTokenAsync invoked (clipboard source).");
        var encodedToken = await TryGetLoginTokenFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            LogFlow("Token", "Clipboard does not contain a login token.", ConsoleColor.Yellow);
            return false;
        }

        LogFlow("Token", "Token extracted from clipboard successfully.");
        return await RedeemSharedTokenAsync(encodedToken, source: "clipboard");
    }

    /// <summary>
    ///     Submits a raw login token (e.g. one obtained via the RSO authenticator flow) to a freshly
    ///     started Riot Client and launches League.
    /// </summary>
    public static Task<bool> SubmitRawLoginTokenAsync(string rawLoginToken, bool persistLogin,
        string product = ProductLeague)
    {
        // Persist is a property of the minted payload: bake it in so redemption always honours
        // the payload data. Fresh-client login mechanics live in RiotLoginManager.
        return NormalizeProduct(product) == ProductValorant
            ? RiotLoginManager.UseLoginTokenValorantAsync(rawLoginToken, persistLogin)
            : RiotLoginManager.UseLoginTokenAsync(rawLoginToken, persistLogin);
    }

    public static async Task<bool> UseLoginTokenValorantAsync()
    {
        LogFlow("Token", "UseLoginTokenValorantAsync invoked (clipboard source).");
        var encodedToken = await TryGetLoginTokenFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            LogFlow("Token", "Clipboard does not contain a login token.", ConsoleColor.Yellow);
            return false;
        }

        LogFlow("Token", "Token extracted from clipboard successfully.");
        // Product routing is decided by the payload, not by which button was clicked.
        return await RedeemSharedTokenAsync(encodedToken, source: "clipboard");
    }

    /// <summary>
    ///     Redeems a shared login token strictly according to its payload data: the embedded
    ///     <c>product</c> selects League/Valorant and the embedded <c>persist_login</c> decides
    ///     whether the session is trusted. Never falls back to the calling context.
    /// </summary>
    private static async Task<bool> RedeemSharedTokenAsync(string encodedToken, string source)
    {
        var payload = DecodeLoginTokenPayload(encodedToken);
        if (payload == null || string.IsNullOrWhiteSpace(payload.LoginToken))
        {
            LogFlow("Token", $"Token payload missing or invalid ({source}).", ConsoleColor.Red);
            Notif.notificationManager.Show("Token login failed",
                "The login token is not in a recognized format.",
                NotificationType.Error);
            return false;
        }

        var product = NormalizeProduct(payload.Product);
        LogFlow("Token",
            $"Redeeming shared token ({source}): product={product} persist_login={payload.PersistLogin}");

        // Fresh-client login mechanics live in RiotLoginManager (normal-login side).
        return product == ProductValorant
            ? await RiotLoginManager.UseLoginTokenValorantAsync(payload.LoginToken, payload.PersistLogin)
            : await RiotLoginManager.UseLoginTokenAsync(payload.LoginToken, payload.PersistLogin);
    }

    /// <summary>
    ///     Decodes a base64 <see cref="LoginTokenPayload"/> and returns the raw login token, or
    ///     null when the payload is missing/invalid. Used when a shared token is redeemed.
    /// </summary>
    internal static string? DecodeLoginToken(string encodedToken)
    {
        var payload = DecodeLoginTokenPayload(encodedToken);
        return string.IsNullOrWhiteSpace(payload?.LoginToken) ? null : payload.LoginToken;
    }

    /// <summary>
    ///     Decodes a base64 shared-token payload. Redemption always honours the payload data
    ///     (product + persist_login) exactly as it was minted — never the calling context.
    /// </summary>
    internal static LoginTokenPayload? DecodeLoginTokenPayload(string encodedToken)
    {
        try
        {
            var bytes = Convert.FromBase64String(encodedToken);
            return JsonSerializer.Deserialize<LoginTokenPayload>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void LogFlow(string flow, string message, ConsoleColor color = ConsoleColor.White)
    {
        DebugConsole.WriteLine($"[ProxyLoginToken][{flow}] {message}", color);
    }

    internal static bool IsSuccessfulResponse(object? response)
    {
        return response is HttpResponseMessage { IsSuccessStatusCode: true };
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
            if (!uri.Host.Equals("lam.monster", StringComparison.OrdinalIgnoreCase))
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


    internal sealed class LoginTokenPayload
    {
        [JsonPropertyName("authentication_type")]
        public string AuthenticationType { get; set; } = "RiotAuth";

        [JsonPropertyName("login_token")] public string LoginToken { get; set; } = string.Empty;

        [JsonPropertyName("persist_login")] public bool PersistLogin { get; set; }

        [JsonPropertyName("product")] public string Product { get; set; } = ProductLeague;
    }
}

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace League_Account_Manager.Misc;

/// <summary>
///     Performs the RSO authenticator flow directly against the running Riot Client's local API
///     (which proxies to the real authenticator service with the correct TLS + Basic auth).
///     Start → [Tabasco via WebView2] → Complete → (optional MFA) → login token.
/// </summary>
internal static class RsoLoginService
{
    private const string StartEndpoint = "/rso-authenticator/v1/authentication/riot-identity/start";
    private const string CompleteEndpoint = "/rso-authenticator/v1/authentication/riot-identity/complete";
    private const string MfaEndpoint = "/rso-authenticator/v1/authentication/multifactor";

    private const string AuthBaseUrl = "https://auth.riotgames.com";
    private const string AuthenticatorBaseUrl = "https://authenticate.riotgames.com";
    private const string RiotAuthUserAgent = "RiotClient/70.0.0.247.1382 rso-auth (Windows;10;;Professional, x64)";
    private const string RiotAuthenticatorUserAgent =
        "RiotClient/70.0.0.247.1382 rso-authenticator (Windows;10;;Professional, x64)";

    /// <summary>
    ///     Origin the Tabasco widget is bound to in the clientless flow (the public web
    ///     authenticator host used as the widget 'host' render option / websiteurl).
    /// </summary>
    public const string ClientlessHost = "authenticate.riotgames.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public sealed class ChallengeInfo
    {
        public string Type { get; init; } = "none";
        public string? SiteKey { get; init; }
        public string? RqData { get; init; }

        public bool RequiresChallenge =>
            !string.Equals(Type, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(SiteKey);
    }

    public sealed class AuthResult
    {
        public string Type { get; init; } = string.Empty;
        public string? Error { get; init; }
        public string? LoginToken { get; init; }
        public ChallengeInfo Challenge { get; init; } = new();
    }

    /// <summary>
    ///     Starts the RSO identity authentication flow and returns the challenge requirements.
    /// </summary>
    public static async Task<ChallengeInfo> StartAuthAsync(CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            language = "en_US",
            productId = "riot-client",
            state = "auth"
        }, JsonOptions);

        var response = await Lcu.Connector("riot", "post", StartEndpoint, body, cancellationToken)
            .ConfigureAwait(false);
        var text = await ReadBodyAsync(response).ConfigureAwait(false);
        DebugConsole.WriteLine($"[RsoLogin] start response: {Truncate(text)}");

        var node = TryParse(text);
        var challenge = GetCaptchaNode(node) as JsonObject;

        var type = challenge?["type"]?.GetValue<string>() ?? "none";
        string? siteKey = null;
        string? rqData = null;
        if (challenge?["hcaptcha"] is JsonObject hc)
        {
            siteKey = hc["key"]?.GetValue<string>();
            rqData = hc["data"]?.GetValue<string>();
        }

        return new ChallengeInfo { Type = type, SiteKey = siteKey, RqData = rqData };
    }

    /// <summary>
    ///     Fetches the authenticator service_url from the running Riot Client's config
    ///     (/rso-authenticator/v1/config). Its hostname is used as the Tabasco 'host' render
    ///     option so the token matches the sitekey's domain binding exactly like the real client.
    /// </summary>
    public static async Task<string?> GetServiceUrlHostAsync(CancellationToken cancellationToken = default)
    {
        var response = await Lcu.Connector("riot", "get", "/rso-authenticator/v1/config", "", cancellationToken)
            .ConfigureAwait(false);
        var text = await ReadBodyAsync(response).ConfigureAwait(false);
        DebugConsole.WriteLine($"[RsoLogin] config response: {Truncate(text)}");

        var node = TryParse(text);
        var serviceUrl = node?["service_url"]?.GetValue<string>();
        Uri? parsedUri = null;
        if (string.IsNullOrWhiteSpace(serviceUrl) ||
            !Uri.TryCreate(serviceUrl, UriKind.Absolute, out parsedUri) ||
            parsedUri is null)
        {
            DebugConsole.WriteLine("[RsoLogin] No valid service_url in authenticator config.", ConsoleColor.Yellow);
            return null;
        }

        var serviceHost = parsedUri!.Host;
        if (string.IsNullOrWhiteSpace(serviceHost))
        {
            DebugConsole.WriteLine("[RsoLogin] Authenticator service_url has no host.", ConsoleColor.Yellow);
            return null;
        }

        DebugConsole.WriteLine($"[RsoLogin] Authenticator service host: {serviceHost}");
        return serviceHost;
    }

    // ------------------------------------------------------------------
    // Clientless flow (no Riot Client required)
    // ------------------------------------------------------------------

    private static readonly CookieContainer ClientlessCookies = new();
    private static readonly HttpClient ClientlessClient = CreateClientlessClient();

    private static HttpClient CreateClientlessClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = ClientlessCookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task<string> ClientlessRequestAsync(HttpMethod method, string path, string? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, AuthenticatorBaseUrl + path);
        request.Headers.TryAddWithoutValidation("User-Agent", RiotAuthenticatorUserAgent);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Pragma.ParseAdd("no-cache");
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // Surface clientless authenticator traffic in the LCU traffic view alongside the rest.
        var requestHeaders = string.Join(Environment.NewLine,
            request.Headers.Concat(request.Content?.Headers ??
                                   Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
        var requestRecord = LcuRequestLog.Add(
            "rso-authenticator",
            method.Method,
            AuthenticatorBaseUrl + path,
            body ?? string.Empty,
            null,
            "Pending",
            string.Empty,
            0,
            trafficType: "HTTP",
            requestHeaders: requestHeaders,
            direction: "Outgoing");

        var stopwatch = Stopwatch.StartNew();
        using var response = await ClientlessClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        LcuRequestLog.Update(
            requestRecord.Id,
            (int)response.StatusCode,
            response.ReasonPhrase ?? response.StatusCode.ToString(),
            text,
            stopwatch.ElapsedMilliseconds,
            responseHeaders: FormatClientlessResponseHeaders(response));
        DebugConsole.WriteLine(
            $"[RsoLogin][Clientless] {method} {path} -> HTTP {(int)response.StatusCode}: {Truncate(text)}");
        return text;
    }

    private static string FormatClientlessResponseHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content?.Headers ??
                    Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {string.Join(", ", group.SelectMany(header => header.Value))}");
        return string.Join(Environment.NewLine, headers);
    }

    /// <summary>
    ///     Starts a clientless authenticator session directly against the public web authenticator:
    ///     DELETE resets any lingering session, POST starts a fresh one and returns the Tabasco
    ///     sitekey + rqdata. Requires no running Riot Client.
    ///     <paramref name="remember"/> is the remember-me choice and is sent on this start request
    ///     as well as the later complete PUT, matching the real client.
    /// </summary>
    public static Task<ChallengeInfo> ClientlessStartSessionAsync(CancellationToken cancellationToken = default)
        => ClientlessStartSessionAsync(remember: false, cancellationToken);

    /// <inheritdoc cref="ClientlessStartSessionAsync(bool, CancellationToken)"/>
    public static async Task<ChallengeInfo> ClientlessStartSessionAsync(bool remember,
        CancellationToken cancellationToken = default)
    {
        await ClientlessRequestAsync(HttpMethod.Delete, "/api/v1/login", null, cancellationToken)
            .ConfigureAwait(false);

        var body = JsonSerializer.Serialize(new
        {
            apple = (object?)null,
            campaign = (object?)null,
            clientId = "riot-client",
            code = (object?)null,
            facebook = (object?)null,
            gamecenter = (object?)null,
            google = (object?)null,
            language = "",
            multifactor = (object?)null,
            nintendo = (object?)null,
            platform = "windows",
            playstation = (object?)null,
            remember = (bool?)remember,
            riot_identity = new
            {
                campaign = (object?)null,
                captcha = (string?)null,
                language = "en_US",
                password = (string?)null,
                remember = (bool?)remember,
                state = "auth",
                username = (string?)null
            },
            riot_identity_signup = (object?)null,
            rso = (object?)null,
            sdkVersion = "",
            type = "auth",
            xbox = (object?)null
        }, JsonOptions);

        var text = await ClientlessRequestAsync(HttpMethod.Post, "/api/v1/login", body, cancellationToken)
            .ConfigureAwait(false);
        return ParseChallenge(GetCaptchaNode(TryParse(text)));
    }

    /// <summary>
    ///     Completes the clientless login with credentials (and captcha token if required).
    ///     On success the response carries <c>success.login_token</c> — the same token type the
    ///     Riot Client mints, usable with the existing token login/clipboard flows.
    /// </summary>
    public static async Task<AuthResult> ClientlessCompleteAuthAsync(string username, string password, bool remember,
        string? captchaToken, CancellationToken cancellationToken = default)
    {
        var captchaField = string.IsNullOrWhiteSpace(captchaToken) ? null : $"hcaptcha {captchaToken}";
        // Wire shape mirrors the real client exactly (captured): campaign/language/remember/type
        // live at the TOP level; riot_identity carries only captcha/password/state/username.
        var body = JsonSerializer.Serialize(new
        {
            campaign = (object?)null,
            language = "en_US",
            remember,
            riot_identity = new
            {
                captcha = captchaField,
                password,
                state = (string?)null,
                username
            },
            type = "auth"
        }, JsonOptions);

        var text = await ClientlessRequestAsync(HttpMethod.Put, "/api/v1/login", body, cancellationToken)
            .ConfigureAwait(false);
        return ParseAuthResult(text);
    }

    /// <summary>
    ///     Submits a multi-factor auth (2FA) code in the clientless flow.
    /// </summary>
    public static async Task<AuthResult> ClientlessSubmitMfaAsync(string otp, bool rememberDevice,
        CancellationToken cancellationToken = default)
    {
        // Wire shape mirrors the real client's reduced envelope (same pattern as the captured
        // complete request): top-level campaign/language/remember/type + the active method block.
        var body = JsonSerializer.Serialize(new
        {
            campaign = (object?)null,
            language = "en_US",
            remember = rememberDevice,
            multifactor = new
            {
                action = (string?)null,
                otp,
                rememberDevice
            },
            riot_identity = (object?)null,
            type = "multifactor"
        }, JsonOptions);

        var text = await ClientlessRequestAsync(HttpMethod.Put, "/api/v1/login", body, cancellationToken)
            .ConfigureAwait(false);
        return ParseAuthResult(text);
    }

    /// <summary>
    ///     Turns a clientless login into a session the Riot Client restores on its own. The
    ///     authenticator's <c>remember</c> flag only marks the returned login token; what the client
    ///     actually restores is the refresh token it keeps in
    ///     <c>RiotGamesPrivateSettings.yaml</c>. This exchanges the login token for that refresh
    ///     token the same way the client does (RFC 8693 token exchange at
    ///     <c>auth.riotgames.com/token</c>) and writes it into the file. The client must be shut
    ///     down first, because it rewrites the file from memory while it runs and would discard the
    ///     change.
    /// </summary>
    public static async Task<bool> PersistLoginTokenAsync(string loginToken,
        CancellationToken cancellationToken = default)
    {
        var exchanged = await ExchangeLoginTokenAsync(loginToken, cancellationToken).ConfigureAwait(false);
        if (exchanged == null)
            return false;

        return WritePersistedAuthorization(exchanged.Value.RefreshToken, exchanged.Value.IdToken);
    }

    /// <summary>
    ///     Exchanges an authenticator login token for RSO tokens. Mirrors the client's own token
    ///     request: <c>grant_type=urn:ietf:params:oauth:grant-type:token-exchange</c> with the login
    ///     token as the subject. The <c>scope</c> is required: without it the server issues a short
    ///     refresh token that it later rejects with <c>invalid_grant</c>, so the client restores the
    ///     record and then drops it. This is the scope a remembered session refreshes with.
    /// </summary>
    private static async Task<(string RefreshToken, string IdToken)?> ExchangeLoginTokenAsync(string loginToken,
        CancellationToken cancellationToken)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "riot-client",
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = loginToken,
            ["subject_token_type"] = "urn:riot:params:oauth:token-type:authentication-token",
            ["scope"] = "openid link ban lol_region lol account summoner offline_access"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthBaseUrl + "/token") { Content = body };
        request.Headers.TryAddWithoutValidation("User-Agent", RiotAuthUserAgent);

        using var response = await ClientlessClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine(
            $"[RsoLogin][Clientless] POST /token -> HTTP {(int)response.StatusCode}: {Truncate(text)}");

        if (!response.IsSuccessStatusCode)
            return null;

        var node = TryParse(text);
        var refreshToken = node?["refresh_token"]?.GetValue<string>();
        var idToken = node?["id_token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(idToken))
        {
            DebugConsole.WriteLine("[RsoLogin][Clientless] Token exchange returned no refresh token.",
                ConsoleColor.Yellow);
            return null;
        }

        return (refreshToken, idToken);
    }

    /// <summary>
    ///     Scopes the client requests for itself, so the restored session authorizes the same set.
    /// </summary>
    private static readonly string[] PersistedScopes =
        ["openid", "link", "ban", "lol_region", "lol", "account"];

    /// <summary>
    ///     Writes the exchanged tokens into the client's private settings, replacing only the
    ///     <c>psl.authorization.riot-client</c> record and leaving the rest of the file untouched.
    /// </summary>
    private static bool WritePersistedAuthorization(string refreshToken, string idToken)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Riot Games", "Riot Client", "Data", "RiotGamesPrivateSettings.yaml");

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var yaml = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var record = BuildAuthorizationRecord(refreshToken, idToken);
            var updated = ReplaceAuthorizationRecord(yaml, record);
            File.WriteAllText(path, updated);
            DebugConsole.WriteLine($"[RsoLogin][Clientless] Wrote persisted authorization to {path}");
            return true;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[RsoLogin][Clientless] Failed to write persisted authorization: {ex.Message}",
                ConsoleColor.Red);
            return false;
        }
    }

    private static string BuildAuthorizationRecord(string refreshToken, string idToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var builder = new StringBuilder();
        builder.AppendLine("psl:");
        builder.AppendLine("    authorization:");
        builder.AppendLine("        riot-client:");
        builder.AppendLine("            claims: []");
        builder.AppendLine($"            id_token: \"{idToken}\"");
        builder.AppendLine("            is_dpop_bound: false");
        builder.AppendLine($"            last_token_creation_time: {now}");
        builder.AppendLine($"            original_token_creation_time: {now}");
        builder.AppendLine($"            refresh_token: \"{refreshToken}\"");
        builder.AppendLine("            refresh_token_write_count: 1");
        builder.AppendLine($"            refresh_tokens_session_id: \"{Guid.NewGuid()}\"");
        builder.AppendLine("            scopes:");
        foreach (var scope in PersistedScopes)
            builder.AppendLine($"            - \"{scope}\"");
        return builder.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    ///     Swaps the <c>psl</c> block of an existing private-settings file for <paramref name="record"/>.
    ///     The block runs until the next top-level key, so the <c>riot-login</c> and
    ///     <c>rso-authenticator</c> sections survive. A file without the block gets one prepended.
    /// </summary>
    private static string ReplaceAuthorizationRecord(string yaml, string record)
    {
        var start = yaml.IndexOf("\npsl:", StringComparison.Ordinal);
        if (start < 0 && yaml.StartsWith("psl:", StringComparison.Ordinal))
            start = 0;
        else if (start >= 0)
            start += 1;

        if (start < 0)
            return string.IsNullOrWhiteSpace(yaml) ? record + "\n" : record + "\n" + yaml;

        var end = yaml.IndexOf("\nriot-login:", start, StringComparison.Ordinal);
        if (end < 0)
            return yaml[..start] + record + "\n";

        return yaml[..start] + record + yaml[end..];
    }

    /// <summary>
    ///     Completes the RSO identity authentication with credentials (and captcha token if required).
    /// </summary>
    public static async Task<AuthResult> CompleteAuthAsync(string username, string password, bool remember,
        string? captchaToken, CancellationToken cancellationToken = default)
    {
        var captchaField = string.IsNullOrWhiteSpace(captchaToken) ? string.Empty : $"hcaptcha {captchaToken}";
        var body = JsonSerializer.Serialize(new
        {
            username,
            password,
            remember,
            language = "en_US",
            captcha = captchaField
        }, JsonOptions);

        var response = await Lcu.Connector("riot", "post", CompleteEndpoint, body, cancellationToken)
            .ConfigureAwait(false);
        var text = await ReadBodyAsync(response).ConfigureAwait(false);
        DebugConsole.WriteLine($"[RsoLogin] complete response: {Truncate(text)}");

        return ParseAuthResult(text);
    }

    /// <summary>
    ///     Submits a multi-factor auth (2FA) code.
    /// </summary>
    public static async Task<AuthResult> SubmitMfaAsync(string otp, bool rememberDevice,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            multifactor = new { otp, rememberDevice }
        }, JsonOptions);

        var response = await Lcu.Connector("riot", "post", MfaEndpoint, body, cancellationToken)
            .ConfigureAwait(false);
        var text = await ReadBodyAsync(response).ConfigureAwait(false);
        DebugConsole.WriteLine($"[RsoLogin] mfa response: {Truncate(text)}");

        return ParseAuthResult(text);
    }

    private static AuthResult ParseAuthResult(string text)
    {
        var node = TryParse(text);
        var type = node?["type"]?.GetValue<string>() ?? string.Empty;
        var error = node?["error"]?.GetValue<string>();

        string? loginToken = null;
        if (node?["success"] is JsonObject success)
            loginToken = success["login_token"]?.GetValue<string>();
        loginToken ??= FindLoginToken(node);

        return new AuthResult
        {
            Type = type,
            Error = error,
            LoginToken = loginToken,
            Challenge = ParseChallenge(GetCaptchaNode(node))
        };
    }

    /// <summary>
    ///     The web authenticator returns challenge requirements under the <c>captcha</c> key
    ///     (see real-client captures). Older/other variants may use <c>challenge</c>, so accept
    ///     both to be safe when reading responses.
    /// </summary>
    private static JsonNode? GetCaptchaNode(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        var captcha = obj["captcha"];
        return captcha ?? obj["challenge"];
    }

    private static ChallengeInfo ParseChallenge(JsonNode? challengeNode)
    {
        if (challengeNode is not JsonObject challenge)
            return new ChallengeInfo();

        var type = challenge["type"]?.GetValue<string>() ?? "none";
        string? siteKey = null;
        string? rqData = null;
        if (challenge["hcaptcha"] is JsonObject hc)
        {
            siteKey = hc["key"]?.GetValue<string>();
            rqData = hc["data"]?.GetValue<string>();
        }

        return new ChallengeInfo { Type = type, SiteKey = siteKey, RqData = rqData };
    }

    private static string? FindLoginToken(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (value == null)
                    continue;

                if ((key.Equals("login_token", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("loginToken", StringComparison.OrdinalIgnoreCase)) &&
                    value is JsonValue)
                    return value.GetValue<string>();

                var nested = FindLoginToken(value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var nested = FindLoginToken(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static async Task<string> ReadBodyAsync(dynamic? response)
    {
        if (response is HttpResponseMessage http)
        {
            var text = await http.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!http.IsSuccessStatusCode && string.IsNullOrWhiteSpace(text))
                DebugConsole.WriteLine($"[RsoLogin] HTTP {(int)http.StatusCode} with empty body.", ConsoleColor.Red);
            return text ?? string.Empty;
        }

        DebugConsole.WriteLine("[RsoLogin] No HTTP response (client not running or in game).", ConsoleColor.Red);
        return string.Empty;
    }

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 800 ? text : text[..800] + "…";
}

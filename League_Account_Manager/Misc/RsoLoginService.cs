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
        var challenge = node?["challenge"] as JsonObject;

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

        using var response = await ClientlessClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine(
            $"[RsoLogin][Clientless] {method} {path} -> HTTP {(int)response.StatusCode}: {Truncate(text)}");
        return text;
    }

    /// <summary>
    ///     Starts a clientless authenticator session directly against the public web authenticator:
    ///     DELETE resets any lingering session, POST starts a fresh one and returns the Tabasco
    ///     sitekey + rqdata. Requires no running Riot Client.
    /// </summary>
    public static async Task<ChallengeInfo> ClientlessStartSessionAsync(CancellationToken cancellationToken = default)
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
            remember = (bool?)null,
            riot_identity = new
            {
                campaign = (object?)null,
                challenge = (string?)null,
                language = "en_US",
                password = (string?)null,
                remember = (bool?)null,
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
        return ParseChallenge(TryParse(text)?["challenge"]);
    }

    /// <summary>
    ///     Completes the clientless login with credentials (and challenge token if required).
    ///     On success the response carries <c>success.login_token</c> — the same token type the
    ///     Riot Client mints, usable with the existing token login/clipboard flows.
    /// </summary>
    public static async Task<AuthResult> ClientlessCompleteAuthAsync(string username, string password, bool remember,
        string? challengeToken, CancellationToken cancellationToken = default)
    {
        var challengeField = string.IsNullOrWhiteSpace(challengeToken) ? null : $"hcaptcha {challengeToken}";
        var body = JsonSerializer.Serialize(new
        {
            type = "auth",
            riot_identity = new
            {
                campaign = (object?)null,
                challenge = challengeField,
                language = "en_US",
                password,
                remember,
                state = (string?)null,
                username
            }
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
            multifactor = new
            {
                action = (string?)null,
                otp,
                rememberDevice
            },
            nintendo = (object?)null,
            platform = "windows",
            playstation = (object?)null,
            remember = (bool?)null,
            riot_identity = (object?)null,
            riot_identity_signup = (object?)null,
            rso = (object?)null,
            sdkVersion = "",
            type = "multifactor",
            xbox = (object?)null
        }, JsonOptions);

        var text = await ClientlessRequestAsync(HttpMethod.Put, "/api/v1/login", body, cancellationToken)
            .ConfigureAwait(false);
        return ParseAuthResult(text);
    }

    /// <summary>
    ///     Completes the RSO identity authentication with credentials (and challenge token if required).
    /// </summary>
    public static async Task<AuthResult> CompleteAuthAsync(string username, string password, bool remember,
        string? challengeToken, CancellationToken cancellationToken = default)
    {
        var challengeField = string.IsNullOrWhiteSpace(challengeToken) ? string.Empty : $"hcaptcha {challengeToken}";
        var body = JsonSerializer.Serialize(new
        {
            username,
            password,
            remember,
            language = "en_US",
            challenge = challengeField
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
            Challenge = ParseChallenge(node?["challenge"])
        };
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
        if (node == null)
            return null;

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

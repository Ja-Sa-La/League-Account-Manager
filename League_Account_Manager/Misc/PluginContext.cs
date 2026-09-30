using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using League_Account_Manager.PluginContract;

namespace League_Account_Manager.Misc;

internal sealed class PluginLcuClient : ILcuClient
{
    public async Task<LcuResponse> RequestAsync(
        LcuTarget target,
        HttpMethod method,
        string endpoint,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        var response = await Lcu.Connector(
                target == LcuTarget.League ? "league" : "riot",
                method.Method,
                endpoint,
                body ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false) as HttpResponseMessage;

        if (response == null)
            return new LcuResponse
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                ReasonPhrase = "The client is unavailable.",
                Body = string.Empty
            };

        using (response)
        {
            return new LcuResponse
            {
                StatusCode = response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? string.Empty,
                Body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            };
        }
    }
}

internal sealed class PluginContext : IPluginContext, IPluginHostInfo
{
    public PluginContext(string pluginId)
    {
        Lcu = new PluginLcuClient();
        Storefront = new PluginStorefrontClient(Lcu);
        Logger = new PluginLogger(pluginId);
        Notifications = new PluginNotifications();
    }

    public ILcuClient Lcu { get; }
    public IStorefrontClient Storefront { get; }
    public IPluginLogger Logger { get; }
    public IPluginNotifications Notifications { get; }
    public Version ApiVersion => PluginManager.ApiVersion;
    public IReadOnlyCollection<string> Capabilities => PluginManager.Capabilities;
}

internal sealed class PluginStorefrontClient : IStorefrontClient
{
    private static readonly HttpClient Client = CreateClient();
    private readonly ILcuClient lcu;

    public PluginStorefrontClient(ILcuClient lcu)
    {
        this.lcu = lcu;
    }

    public async Task<LcuResponse> RequestAsync(
        HttpMethod method,
        string endpoint,
        string? body = null,
        CancellationToken cancellationToken = default)
    {
        var storeUrlResponse = await lcu.RequestAsync(
            LcuTarget.League,
            HttpMethod.Get,
            "/lol-store/v1/getStoreUrl",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!storeUrlResponse.IsSuccessStatusCode)
            return storeUrlResponse;

        var authorizationResponse = await lcu.RequestAsync(
            LcuTarget.League,
            HttpMethod.Get,
            "/lol-rso-auth/v1/authorization/access-token",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!authorizationResponse.IsSuccessStatusCode)
            return authorizationResponse;

        var storeUrl = JsonNode.Parse(storeUrlResponse.Body)?.GetValue<string>();
        var accessToken = JsonNode.Parse(authorizationResponse.Body)?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(storeUrl) || string.IsNullOrWhiteSpace(accessToken))
            return new LcuResponse
            {
                StatusCode = HttpStatusCode.BadGateway,
                ReasonPhrase = "The League storefront authorization response was incomplete.",
                Body = string.Empty
            };

        if (!TryResolveEndpoint(storeUrl, endpoint, out var requestUri))
            return new LcuResponse
            {
                StatusCode = HttpStatusCode.BadRequest,
                ReasonPhrase = "The storefront endpoint must be relative.",
                Body = string.Empty
            };

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return new LcuResponse
        {
            StatusCode = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase ?? string.Empty,
            Body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    private static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                 System.Net.DecompressionMethods.Deflate |
                                 System.Net.DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    internal static bool TryResolveEndpoint(string storeUrl, string endpoint, out Uri? requestUri)
    {
        requestUri = null;
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Contains('\\') || endpoint.StartsWith("//", StringComparison.Ordinal) ||
            !Uri.TryCreate(storeUrl, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(origin.UserInfo) || Uri.TryCreate(endpoint, UriKind.Absolute, out _) && !endpoint.StartsWith('/'))
            return false;
        return Uri.TryCreate(new Uri(origin.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/"), endpoint, out requestUri) &&
            requestUri.Scheme == origin.Scheme && requestUri.Host == origin.Host && requestUri.Port == origin.Port &&
            string.IsNullOrEmpty(requestUri.UserInfo);
    }
}

internal sealed class PluginLogger : IPluginLogger
{
    private readonly string pluginId;

    public PluginLogger(string pluginId) => this.pluginId = pluginId;

    public void Debug(string message) => DebugConsole.WriteLine($"[Plugin:{pluginId}] {message}");

    public void Error(string message, Exception? exception = null) =>
        DebugConsole.WriteLine($"[Plugin:{pluginId}] {message}{(exception == null ? string.Empty : $": {exception}")}",
            ConsoleColor.Red);
}

internal sealed class PluginNotifications : IPluginNotifications
{
    public void Show(string title, string message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            Notif.notificationManager.Show(title, message, Notification.Wpf.NotificationType.Notification));
    }
}

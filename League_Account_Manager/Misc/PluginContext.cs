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

internal sealed class PluginContext : IPluginContext
{
    public PluginContext()
    {
        Lcu = new PluginLcuClient();
        Storefront = new PluginStorefrontClient(Lcu);
        Logger = new PluginLogger();
        Notifications = new PluginNotifications();
    }

    public ILcuClient Lcu { get; }
    public IStorefrontClient Storefront { get; }
    public IPluginLogger Logger { get; }
    public IPluginNotifications Notifications { get; }
}

internal sealed class PluginStorefrontClient : IStorefrontClient
{
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

        if (!Uri.TryCreate(new Uri(storeUrl.TrimEnd('/') + "/"), endpoint.TrimStart('/'), out var requestUri))
            return new LcuResponse
            {
                StatusCode = HttpStatusCode.BadRequest,
                ReasonPhrase = "The storefront endpoint is invalid.",
                Body = string.Empty
            };

        using var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate |
                                     System.Net.DecompressionMethods.Brotli
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var request = new HttpRequestMessage(method, requestUri);
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return new LcuResponse
        {
            StatusCode = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase ?? string.Empty,
            Body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
        };
    }
}

internal sealed class PluginLogger : IPluginLogger
{
    public void Debug(string message) => DebugConsole.WriteLine($"[Plugin] {message}");

    public void Error(string message, Exception? exception = null) =>
        DebugConsole.WriteLine($"[Plugin] {message}{(exception == null ? string.Empty : $": {exception}")}",
            ConsoleColor.Red);
}

internal sealed class PluginNotifications : IPluginNotifications
{
    public void Show(string title, string message)
    {
        Notif.notificationManager.Show(title, message, Notification.Wpf.NotificationType.Notification);
    }
}

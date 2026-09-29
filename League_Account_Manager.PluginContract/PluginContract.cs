using System.Net;
using System.Net.Http;
using System.Windows.Controls;

namespace League_Account_Manager.PluginContract;

public interface ILamPlugin
{
    string Id { get; }
    string Name { get; }
    Version ApiVersion { get; }

    void Initialize(IPluginContext context);
    IEnumerable<PluginPage> GetPages();
}

public interface IPluginContext
{
    ILcuClient Lcu { get; }
    IStorefrontClient Storefront { get; }
    IPluginLogger Logger { get; }
    IPluginNotifications Notifications { get; }
}

public interface ILcuClient
{
    Task<LcuResponse> RequestAsync(
        LcuTarget target,
        HttpMethod method,
        string endpoint,
        string? body = null,
        CancellationToken cancellationToken = default);
}

    public interface IStorefrontClient
    {
        Task<LcuResponse> RequestAsync(
        HttpMethod method,
        string endpoint,
        string? body = null,
        CancellationToken cancellationToken = default);
    }

public interface IPluginLogger
{
    void Debug(string message);
    void Error(string message, Exception? exception = null);
}

public interface IPluginNotifications
{
    void Show(string title, string message);
}

public sealed record PluginPage(string Id, string Title, Type PageType, string? IconGlyph = null)
{
    public PluginPage(string id, string title, Type pageType)
        : this(id, title, pageType, null)
    {
    }
}

public sealed class LcuResponse
{
    public required HttpStatusCode StatusCode { get; init; }
    public required string ReasonPhrase { get; init; }
    public required string Body { get; init; }
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public enum LcuTarget
{
    League,
    Riot
}

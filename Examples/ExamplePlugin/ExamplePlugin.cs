using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.PluginContract;

namespace ExamplePlugin;

public sealed class ExampleLamPlugin : ILamPlugin
{
    internal static IPluginContext? Context { get; private set; }

    public string Id => "example.plugin";

    public string Name => "Example Plugin";

    public Version ApiVersion => new(1, 0);

    public void Initialize(IPluginContext context)
    {
        Context = context;
        context.Logger.Debug("Example plugin initialized.");
    }

    public IEnumerable<PluginPage> GetPages()
    {
        return [new PluginPage(
            "example-page",
            "Example",
            typeof(ExamplePage),
            "\uE716")];
    }
}

public partial class ExamplePage : Page
{
    private readonly IPluginContext context;
    private readonly Button currentSummonerButton;
    private readonly Button storefrontButton;
    private readonly TextBlock responseText;
    private readonly TextBlock statusText;

    public ExamplePage()
    {
        context = ExampleLamPlugin.Context
            ?? throw new InvalidOperationException("The plugin has not been initialized.");

        InitializeComponent();
        currentSummonerButton = CurrentSummonerButton;
        storefrontButton = StorefrontButton;
        statusText = StatusText;
        responseText = ResponseText;
    }

    private void CurrentSummonerButton_Click(object sender, RoutedEventArgs e) =>
        _ = RequestCurrentSummonerAsync();

    private void StorefrontButton_Click(object sender, RoutedEventArgs e) =>
        _ = RequestStorefrontAsync();

    private async Task RequestCurrentSummonerAsync() => await RequestAsync(
        "Requesting current summoner...",
        () => context.Lcu.RequestAsync(
            LcuTarget.League,
            HttpMethod.Get,
            "/lol-summoner/v1/current-summoner"));

    private async Task RequestStorefrontAsync() => await RequestAsync(
        "Requesting storefront champions...",
        () => context.Storefront.RequestAsync(
            HttpMethod.Get,
            "/storefront/v3/view/champions?language=en_US"));

    private async Task RequestAsync(string loadingMessage, Func<Task<LcuResponse>> request)
    {
        currentSummonerButton.IsEnabled = false;
        storefrontButton.IsEnabled = false;
        statusText.Text = loadingMessage;
        responseText.Text = string.Empty;

        try
        {
            var response = await request();

            statusText.Text = response.IsSuccessStatusCode ? "Request completed" : "Request failed";
            responseText.Text = $"{response.StatusCode} {response.ReasonPhrase}\n\n{response.Body}";

            context.Logger.Debug($"Plugin request returned {response.StatusCode}.");
        }
        catch (Exception exception)
        {
            statusText.Text = "Request error";
            responseText.Text = exception.Message;
            context.Logger.Error("Plugin request failed.", exception);
        }
        finally
        {
            currentSummonerButton.IsEnabled = true;
            storefrontButton.IsEnabled = true;
        }
    }
}

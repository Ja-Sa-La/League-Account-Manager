# Plugin Development

League Account Manager plugins are trusted, in-process .NET assemblies. A
plugin can add WPF navigation pages and use host-mediated League Client (LCU)
and storefront requests without implementing client authentication itself.

> Plugins are not sandboxed. An installed plugin runs with the same Windows
> permissions as League Account Manager. Only install plugins you trust.

## Quick start

1. Create a .NET 10 WPF class library.
2. Reference `League_Account_Manager.PluginContract`.
3. Add a public, parameterless class implementing `ILamPlugin`.
4. Add one or more public, parameterless WPF `Page` types.
5. Build the project and copy its plugin DLL and private dependencies into the
   application's `Plugins` folder.
6. Restart League Account Manager. Plugins are discovered at startup.

The repository contains a working reference plugin at
`Examples/ExamplePlugin`. It demonstrates a XAML page, a page icon, an LCU
request, and a storefront request.

## Project setup

Use a WPF class library. `UseWPF` is required for XAML compilation and the
target framework must be compatible with the host:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\League_Account_Manager.PluginContract\League_Account_Manager.PluginContract.csproj" />
  </ItemGroup>
</Project>
```

For a plugin outside this repository, reference the contract assembly shipped
with the same application release. Do not bundle a replacement contract DLL
with the plugin deployment.

## Plugin entry point

The entry point is deliberately small. The host calls `Initialize` once before
it calls `GetPages`, so capture the context there for use by page instances:

```csharp
using League_Account_Manager.PluginContract;

public sealed class DashboardPlugin : ILamPlugin
{
    internal static IPluginContext? Context { get; private set; }

    public string Id => "acme.dashboard";
    public string Name => "Acme Dashboard";
    public Version ApiVersion => new(1, 0);

    public void Initialize(IPluginContext context)
    {
        Context = context;
        context.Logger.Debug("Acme Dashboard initialized.");
    }

    public IEnumerable<PluginPage> GetPages()
    {
        return [new PluginPage(
            "dashboard",
            "Dashboard",
            typeof(DashboardPage),
            "\uE80F")];
    }
}
```

`Id` must remain stable across releases. It is used to identify the plugin and
namespace its navigation pages. `ApiVersion` currently supports major version
`1`; a plugin with another major version is rejected during startup.

## XAML pages

Pages use the normal WPF XAML and code-behind workflow. The page must derive
from `System.Windows.Controls.Page` and have a public parameterless constructor.
The host creates the page when the user navigates to it.

`DashboardPage.xaml`:

```xml
<Page x:Class="Acme.DashboardPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Background="Transparent">
  <ScrollViewer Padding="24" VerticalScrollBarVisibility="Auto">
    <StackPanel MaxWidth="860">
      <TextBlock FontSize="26" FontWeight="SemiBold" Text="Acme Dashboard" />
      <TextBlock Margin="0,4,0,20" Opacity="0.75"
                 Text="Data supplied by the running League Client" />
      <Button x:Name="RefreshButton"
              Width="140"
              HorizontalAlignment="Left"
              Content="Refresh"
              Click="RefreshButton_Click" />
      <TextBlock x:Name="StatusText" Margin="0,18,0,6"
                 FontWeight="SemiBold" Text="Ready" />
      <TextBlock x:Name="ResponseText" TextWrapping="Wrap" />
    </StackPanel>
  </ScrollViewer>
</Page>
```

`DashboardPage.xaml.cs`:

```csharp
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.PluginContract;

namespace Acme;

public partial class DashboardPage : Page
{
    private readonly IPluginContext context;

    public DashboardPage()
    {
        context = DashboardPlugin.Context
            ?? throw new InvalidOperationException("The plugin has not been initialized.");

        InitializeComponent();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Loading...";

        try
        {
            var response = await context.Lcu.RequestAsync(
                LcuTarget.League,
                HttpMethod.Get,
                "/lol-summoner/v1/current-summoner");

            StatusText.Text = response.IsSuccessStatusCode
                ? "Request completed"
                : "Request failed";
            ResponseText.Text = $"{response.StatusCode} {response.ReasonPhrase}\n\n{response.Body}";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Request error";
            ResponseText.Text = exception.Message;
            context.Logger.Error("Dashboard request failed.", exception);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
```

Keep the page constructor limited to `InitializeComponent` and local control
setup. Start network work from an event handler or another async method, not
from the constructor.

## Requests

### LCU

Use `IPluginContext.Lcu` for endpoints served by the League or Riot client.
The host resolves the client connection and authentication details:

```csharp
var response = await context.Lcu.RequestAsync(
    LcuTarget.League,
    HttpMethod.Get,
    "/lol-summoner/v1/current-summoner",
    cancellationToken: cancellationToken);
```

`LcuTarget.Riot` is available for Riot client endpoints. Use the HTTP method,
relative endpoint, optional request body, and cancellation token supplied by
the operation. The response contains `StatusCode`, `ReasonPhrase`, `Body`, and
`IsSuccessStatusCode`.

### Storefront

Use `IPluginContext.Storefront` for League storefront APIs. The host obtains
the storefront URL and League access token, adds the required authorization
headers, and sends the request:

```csharp
var response = await context.Storefront.RequestAsync(
    HttpMethod.Get,
    "/storefront/v3/view/champions?language=en_US",
    cancellationToken: cancellationToken);
```

Plugins should provide only the storefront-relative endpoint. Do not try to
discover ports, read lockfiles, or manage access tokens in plugin code.

## Host services

`IPluginContext` exposes the smallest useful host surface:

| Service | Purpose |
| --- | --- |
| `Lcu` | League and Riot client requests |
| `Storefront` | Authenticated storefront requests |
| `Logger` | Debug and error logging |
| `Notifications` | User-facing host notifications |

The contract does not expose account storage, settings, process management,
or file-system APIs. This is an API boundary, not a security sandbox: code in a
trusted in-process assembly can still call regular .NET APIs.

## Build and deploy

Build the plugin in the configuration used by the host:

```powershell
dotnet build .\MyPlugin\MyPlugin.csproj --configuration Debug
```

Copy the plugin output DLL and any dependencies that are private to the plugin
to:

```text
<League Account Manager installation>\Plugins\
```

The host creates `Plugins` automatically if it does not exist. Restart the
application after changing a plugin. Assemblies remain loaded for the life of
the process, so hot reload is not supported.

### Deployment checklist

- Target `net10.0-windows7.0` and enable `UseWPF`.
- Reference the matching `League_Account_Manager.PluginContract` version.
- Include the plugin DLL and private dependencies.
- Do not overwrite the host's contract DLL.
- Confirm every page has a public parameterless constructor.
- Close the application before rebuilding or replacing deployed DLLs.
- Restart the application after deployment.

## Troubleshooting

### Plugin is not listed

Check that the DLL is directly inside the application's `Plugins` folder, not
inside a nested build directory. Review the application log for a
`[Plugins]` message and confirm the assembly contains a public type implementing
`ILamPlugin`.

### Page is skipped

The page type must derive from WPF `Page` and expose a public parameterless
constructor. XAML pages satisfy this when their code-behind constructor is
declared `public` and calls `InitializeComponent`.

### `MissingMethodException` mentions `PluginPage`

The plugin and host are using incompatible contract binaries. Rebuild the
plugin against the contract from the current source or application release,
remove stale copies from `Plugins`, and ensure an old contract DLL was not
deployed beside the plugin.

### Build says a DLL is locked

Close League Account Manager before rebuilding. The host loads plugins in
process, so Windows keeps their assemblies locked until the application exits.

## Reference

The complete sample is available in
`Examples/ExamplePlugin/ExamplePlugin.csproj`,
`Examples/ExamplePlugin/ExamplePlugin.cs`, and
`Examples/ExamplePlugin/ExamplePage.xaml`.

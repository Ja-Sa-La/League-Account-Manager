using System.Reflection;
using System.IO;
using System.Runtime.Loader;
using System.Windows.Controls;
using League_Account_Manager.PluginContract;

namespace League_Account_Manager.Misc;

internal sealed class PluginManager
{
    internal static readonly Version ApiVersion = new(1, 1);
    internal static readonly IReadOnlyCollection<string> Capabilities = Array.AsReadOnly(new[]
        { "lcu", "storefront", "logging", "notifications", "lifecycle" });
    private readonly List<LoadedPlugin> loadedPlugins = new();
    private readonly List<PluginFailure> failures = new();
    private readonly List<AssemblyLoadContext> loadContexts = new();
    private readonly List<PluginStatus> statuses = new();
    private bool discoveryStarted;

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => loadedPlugins;
    public IReadOnlyList<PluginFailure> Failures => failures;
    public IReadOnlyList<PluginStatus> Statuses => statuses;

    public void LoadPlugins(string pluginDirectory, IEnumerable<string>? disabledPaths = null)
    {
        if (discoveryStarted)
            return;
        discoveryStarted = true;
        var disabled = new HashSet<string>(disabledPaths ?? [], StringComparer.OrdinalIgnoreCase);
        string[] paths;
        try
        {
            Directory.CreateDirectory(pluginDirectory);
            paths = Directory.GetFiles(pluginDirectory, "*.dll")
                .Concat(Directory.GetDirectories(pluginDirectory).Select(directory =>
                    Path.Combine(directory, Path.GetFileName(directory) + ".dll")).Where(File.Exists))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception exception)
        {
            failures.Add(new PluginFailure(pluginDirectory, exception.Message));
            statuses.Add(new PluginStatus("", "Plugin directory", "", "Failed", exception.Message, false));
            DebugConsole.WriteLine($"[Plugins] Failed to create plugin directory: {exception}", ConsoleColor.Yellow);
            return;
        }

        foreach (var path in paths)
        {
            if (Path.GetFileNameWithoutExtension(path) == typeof(ILamPlugin).Assembly.GetName().Name)
                continue;
            var relativePath = Path.GetRelativePath(pluginDirectory, path);
            if (disabled.Contains(relativePath))
            {
                statuses.Add(new PluginStatus(relativePath, relativePath, "", "Disabled", "", false));
                continue;
            }
            try
            {
                LoadPlugin(Path.GetFullPath(path), relativePath);
            }
            catch (Exception exception)
            {
                failures.Add(new PluginFailure(Path.GetFileName(path), exception.Message));
                statuses.Add(new PluginStatus(relativePath, relativePath, "", "Failed", exception.Message, true));
                DebugConsole.WriteLine($"[Plugins] Failed to load '{Path.GetFileName(path)}': {exception}",
                    ConsoleColor.Yellow);
            }
        }
    }

    private void LoadPlugin(string path, string relativePath)
    {
        var loadContext = new PluginLoadContext(path);
        loadContexts.Add(loadContext);
        var assembly = loadContext.LoadFromAssemblyPath(path);
        Type[] pluginTypes;
        try
        {
            pluginTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            pluginTypes = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            foreach (var loaderException in exception.LoaderExceptions.OfType<Exception>())
            {
                failures.Add(new PluginFailure(relativePath, loaderException.Message));
                statuses.Add(new PluginStatus(relativePath, relativePath, "", "Dependency error", loaderException.Message, true));
                DebugConsole.WriteLine($"[Plugins] {Path.GetFileName(path)} dependency warning: {loaderException.Message}", ConsoleColor.Yellow);
            }
        }

        foreach (var pluginType in pluginTypes.Where(type => !type.IsAbstract && typeof(ILamPlugin).IsAssignableFrom(type)))
        {
            ILamPlugin? plugin = null;
            try
            {
                if (!pluginType.IsVisible || pluginType.ContainsGenericParameters || pluginType.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException("Plugin entry points must be public, concrete and have a public parameterless constructor.");
                plugin = Activator.CreateInstance(pluginType) as ILamPlugin;
                if (plugin == null)
                    continue;

                ValidatePlugin(plugin);
                var context = new PluginContext(plugin.Id);
                plugin.Initialize(context);
                var pages = plugin.GetPages()?.ToArray() ?? Array.Empty<PluginPage>();
                var validPages = new List<PluginPage>();
                var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var page in pages)
                {
                    try
                    {
                        ValidatePage(page, pageIds);
                        validPages.Add(page);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new PluginFailure(plugin.Id, exception.Message));
                        statuses.Add(new PluginStatus(relativePath, plugin.Name, plugin.ApiVersion.ToString(), "Page skipped", exception.Message, true));
                        context.Logger.Error("Page skipped", exception);
                    }
                }
                loadedPlugins.Add(new LoadedPlugin(plugin, validPages, path));
                statuses.Add(new PluginStatus(relativePath, plugin.Name,
                    assembly.GetName().Version?.ToString() ?? "", "Loaded", $"{plugin.Id}; API {plugin.ApiVersion}", true));
                DebugConsole.WriteLine($"[Plugins:{plugin.Id}] Loaded {plugin.Name} ({plugin.ApiVersion})");
            }
            catch (Exception exception)
            {
                failures.Add(new PluginFailure($"{Path.GetFileName(path)}:{pluginType.FullName}", exception.Message));
                statuses.Add(new PluginStatus(relativePath, pluginType.Name, "", "Failed", exception.Message, true));
                DebugConsole.WriteLine($"[Plugins] Failed to initialize '{pluginType.FullName}': {exception}", ConsoleColor.Yellow);
                if (plugin != null)
                    _ = Task.Run(() => StopPluginAsync(plugin));
            }
        }
    }

    internal void ValidatePlugin(ILamPlugin plugin)
    {
        if (!IsValidId(plugin.Id) || string.IsNullOrWhiteSpace(plugin.Name))
            throw new InvalidOperationException("Plugin ID must be non-empty, contain no whitespace, and be at most 100 characters.");
        if (loadedPlugins.Any(item => string.Equals(item.Plugin.Id, plugin.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Plugin ID '{plugin.Id}' is already loaded.");
        if (plugin.ApiVersion == null || plugin.ApiVersion.Major != ApiVersion.Major || plugin.ApiVersion > ApiVersion)
            throw new InvalidOperationException($"Plugin '{plugin.Id}' requires unsupported API version {plugin.ApiVersion}.");
        if (plugin is IPluginRequirements requirements &&
            (requirements.MinimumHostApiVersion == null || requirements.MinimumHostApiVersion > ApiVersion ||
             requirements.MinimumHostApiVersion.Major != ApiVersion.Major || requirements.RequiredCapabilities == null ||
             requirements.RequiredCapabilities.Except(Capabilities, StringComparer.Ordinal).Any()))
            throw new InvalidOperationException($"Plugin '{plugin.Id}' requires unsupported host capabilities or API version.");
    }

    private static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100 &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    internal static void ValidatePage(PluginPage? page, HashSet<string> pageIds)
    {
        if (page == null || !IsValidId(page.Id) || string.IsNullOrWhiteSpace(page.Title) || page.PageType == null ||
            !typeof(Page).IsAssignableFrom(page.PageType) || !page.PageType.IsVisible || page.PageType.IsAbstract ||
            page.PageType.ContainsGenericParameters || page.PageType.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidOperationException("Invalid plugin page: use a unique ID and a public concrete WPF Page with a public parameterless constructor.");
        if (!pageIds.Add(page.Id))
            throw new InvalidOperationException($"Duplicate plugin page ID '{page.Id}'.");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(loadedPlugins.Select(loaded => Task.Run(() => StopPluginAsync(loaded.Plugin))));
        loadedPlugins.Clear();
    }

    internal static async Task StopPluginAsync(ILamPlugin plugin)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(async () =>
            {
                if (plugin is IPluginLifecycle lifecycle)
                    await lifecycle.StopAsync(cancellation.Token).ConfigureAwait(false);
                if (plugin is IAsyncDisposable asynchronous)
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (plugin is IDisposable disposable)
                    disposable.Dispose();
            }).WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Plugins] Shutdown failed for {plugin.GetType().FullName}: {exception}", ConsoleColor.Yellow);
        }
    }

    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath), false)
    {
        private readonly AssemblyDependencyResolver resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ILamPlugin).Assembly.GetName().Name)
                return typeof(ILamPlugin).Assembly;
            if (assemblyName.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" or "Wpf.Ui")
                return Default.LoadFromAssemblyName(assemblyName);
            var path = resolver.ResolveAssemblyToPath(assemblyName);
            if (path == null && assemblyName.Name != null)
            {
                var candidate = Path.Combine(Path.GetDirectoryName(pluginPath)!, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                    path = candidate;
            }
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}

internal sealed record LoadedPlugin(ILamPlugin Plugin, IReadOnlyList<PluginPage> Pages, string AssemblyPath);
internal sealed record PluginFailure(string Source, string Message);
internal sealed class PluginStatus
{
    public PluginStatus(string path, string name, string version, string state, string details, bool enabled)
    {
        Path = path;
        Name = name;
        Version = version;
        State = state;
        Details = details;
        Enabled = enabled;
    }

    public string Path { get; }
    public string Name { get; }
    public string Version { get; }
    public string State { get; }
    public string Details { get; }
    public bool Enabled { get; set; }
    public bool CanToggle => !string.IsNullOrEmpty(Path);
}

using System.Reflection;
using System.IO;
using League_Account_Manager.PluginContract;

namespace League_Account_Manager.Misc;

internal sealed class PluginManager
{
    private const int SupportedApiMajorVersion = 1;
    private readonly IPluginContext context = new PluginContext();
    private readonly List<LoadedPlugin> loadedPlugins = new();

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => loadedPlugins;

    public void LoadPlugins(string pluginDirectory)
    {
        Directory.CreateDirectory(pluginDirectory);

        foreach (var path in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                LoadPlugin(path);
            }
            catch (Exception exception)
            {
                DebugConsole.WriteLine($"[Plugins] Failed to load '{Path.GetFileName(path)}': {exception}",
                    ConsoleColor.Yellow);
            }
        }
    }

    private void LoadPlugin(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var pluginTypes = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ILamPlugin).IsAssignableFrom(type));

        foreach (var pluginType in pluginTypes)
        {
            if (Activator.CreateInstance(pluginType) is not ILamPlugin plugin)
                continue;

            if (plugin.ApiVersion.Major != SupportedApiMajorVersion)
                throw new InvalidOperationException(
                    $"Plugin '{plugin.Id}' requires unsupported API version {plugin.ApiVersion}.");

            plugin.Initialize(context);
            var pages = plugin.GetPages()?.ToArray() ?? Array.Empty<PluginPage>();
            loadedPlugins.Add(new LoadedPlugin(plugin, pages));
            DebugConsole.WriteLine($"[Plugins] Loaded {plugin.Name} ({plugin.Id})");
        }
    }
}

internal sealed record LoadedPlugin(ILamPlugin Plugin, IReadOnlyList<PluginPage> Pages);

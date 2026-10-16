using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace League_Account_Manager.Misc;

internal static class RuneIcons
{
    private const string DataUrl =
        "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<int, ImageSource?> Images = new();
    private static readonly Regex Markup = new("<[^>]+>", RegexOptions.Compiled);
    private static Dictionary<int, RuneInfo>? details;

    internal static async Task EnsureLoadedAsync()
    {
        if (details != null)
            return;

        await Gate.WaitAsync();
        try
        {
            if (details != null)
                return;

            var loaded = new Dictionary<int, RuneInfo>();
            await ReadAsync($"{DataUrl}/v1/perks.json", loaded);
            await ReadAsync($"{DataUrl}/v1/perkstyles.json", loaded);
            details = loaded;
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Runes] Rune icons could not be loaded: {exception.Message}");
            details = [];
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static ImageSource? Image(int id)
    {
        if (Images.TryGetValue(id, out var image))
            return image;
        if (details == null || !details.TryGetValue(id, out var info) || info.IconUrl == null)
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(info.IconUrl);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            if (bitmap.CanFreeze)
                bitmap.Freeze();
            Images[id] = bitmap;
            return bitmap;
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Runes] Icon {id} could not be loaded: {exception.Message}");
            Images[id] = null;
            return null;
        }
    }

    internal static string Description(int id)
    {
        if (details == null || !details.TryGetValue(id, out var info))
            return string.Empty;

        return string.IsNullOrWhiteSpace(info.LongDescription) ? info.ShortDescription : info.LongDescription;
    }

    private static async Task ReadAsync(string address, Dictionary<int, RuneInfo> loaded)
    {
        using var document = JsonDocument.Parse(await Client.GetStringAsync(address));
        Walk(document.RootElement, loaded);
    }

    private static void Walk(JsonElement element, Dictionary<int, RuneInfo> loaded)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetId(element, out var id) &&
                    element.TryGetProperty("iconPath", out var pathElement) &&
                    pathElement.GetString() is { Length: > 0 } path)
                {
                    loaded[id] = new RuneInfo(
                        ToUrl(path),
                        Text(element, "shortDesc"),
                        Text(element, "longDesc", "tooltip"));
                }

                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, loaded);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, loaded);
                break;
        }
    }

    private static bool TryGetId(JsonElement element, out int id)
    {
        id = 0;
        if (!element.TryGetProperty("id", out var idElement))
            return false;

        return idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.TryGetInt32(out id),
            JsonValueKind.String => int.TryParse(idElement.GetString(), out id),
            _ => false
        };
    }

    private static string Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } text)
                return Clean(text);
        }

        return string.Empty;
    }

    private static string Clean(string text)
    {
        var plain = Markup.Replace(text, string.Empty);
        return System.Net.WebUtility.HtmlDecode(plain).Replace('\u00A0', ' ').Trim();
    }

    private static string ToUrl(string path)
    {
        const string marker = "/lol-game-data/assets/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var relative = index >= 0 ? path[(index + marker.Length)..] : path.TrimStart('/');
        return $"{DataUrl}/{relative.ToLowerInvariant()}";
    }

    private sealed record RuneInfo(string? IconUrl, string ShortDescription, string LongDescription);
}

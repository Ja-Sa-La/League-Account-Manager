using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Misc;

internal static class RunePageService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim ChampionCacheGate = new(1, 1);
    private static Dictionary<string, int>? championKeys;

    internal static readonly RuneImportSource[] Sources =
    [
        new("ugg", "u.gg"),
        new("opgg", "op.gg"),
        new("lolalytics", "Lolalytics")
    ];

    internal static readonly RuneImportRole[] Roles =
    [
        new("automatic", "Automatic"),
        new("top", "Top"),
        new("jungle", "Jungle"),
        new("middle", "Middle"),
        new("bottom", "Bottom"),
        new("support", "Support")
    ];

    internal static async Task<RunePage> LoadCurrentAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("get", "/lol-perks/v1/currentpage", "", cancellationToken);
        var page = JObject.Parse(response);
        return new RunePage
        {
            Name = page["name"]?.ToString() ?? "Current page",
            PrimaryStyleId = page["primaryStyleId"]?.Value<int>() ?? 0,
            SubStyleId = page["subStyleId"]?.Value<int>() ?? 0,
            PerkIds = ReadIds(page["selectedPerkIds"], 4),
            SecondaryPerkIds = ReadSecondary(page["selectedPerkIds"]),
            StatShardIds = ReadStatShards(page)
        };
    }

    internal static async Task ApplyAsync(RunePage page, CancellationToken cancellationToken = default)
    {
        var current = await SendAsync("get", "/lol-perks/v1/currentpage", "", cancellationToken);
        var pageId = JObject.Parse(current)["id"]?.Value<int>()
                     ?? throw new InvalidOperationException("The League client has no editable rune page.");

        var payload = new JObject
        {
            ["name"] = TrimName(page.Name),
            ["primaryStyleId"] = page.PrimaryStyleId,
            ["subStyleId"] = page.SubStyleId,
            ["selectedPerkIds"] = new JArray(page.PerkIds.Concat(page.SecondaryPerkIds).Concat(page.StatShardIds)),
            ["current"] = true
        };

        await SendAsync("put", $"/lol-perks/v1/pages/{pageId}", payload.ToString(Newtonsoft.Json.Formatting.None),
            cancellationToken);
    }

    internal static async Task<RunePage> ImportAsync(string source, int championKey, string role,
        CancellationToken cancellationToken = default)
    {
        var page = source switch
        {
            "opgg" => await ImportOpGgAsync(championKey, role, cancellationToken),
            "lolalytics" => await ImportLolalyticsAsync(championKey, role, cancellationToken),
            _ => await ImportUggAsync(championKey, role, cancellationToken)
        };
        page.Name = $"{ChampionName(championKey)} {RoleLabel(role)}";
        return page;
    }

    internal static async Task<int> ResolveChampionKeyAsync(string championName,
        CancellationToken cancellationToken = default)
    {
        var champions = await ChampionKeysAsync(cancellationToken);
        var normalized = NormalizeChampion(championName);
        if (champions.TryGetValue(normalized, out var key))
            return key;

        throw new InvalidOperationException($"Champion '{championName}' was not found.");
    }

    internal static string ChampionName(int championKey)
    {
        if (championKeys == null)
            return $"Champion {championKey}";

        foreach (var champion in championKeys)
        {
            if (champion.Value == championKey)
                return champion.Key;
        }

        return $"Champion {championKey}";
    }

    internal static RunePage ParseUgg(string json, string role) =>
        ParseUggDocument(JObject.Parse(json), role);

    internal static RunePage ParseOpGg(string html) => ParseOpGgFragment(html);

    internal static RunePage ParseLolalytics(string html) => ParseLolalyticsFragment(html);

    private static async Task<RunePage> ImportUggAsync(int championKey, string role,
        CancellationToken cancellationToken)
    {
        var patch = await CurrentPatchAsync(cancellationToken);
        var uggPatch = patch.Replace('.', '_');
        var url =
            $"https://stats2.u.gg/lol/1.5/overview/{uggPatch}/ranked_solo_5x5/{championKey}/1.5.0.json";
        var document = JObject.Parse(await GetTextAsync(url, cancellationToken));
        return ParseUggDocument(document, role);
    }

    private static RunePage ParseUggDocument(JObject document, string role)
    {
        var roleKey = UggRoleKey(role, document);
        var overview = UggOverview(document[roleKey]?["1"])
                       ?? throw new InvalidOperationException("u.gg did not return a rune page for this champion.");
        var build = overview[0]?[0] as JArray
                    ?? throw new InvalidOperationException("u.gg rune data has an unexpected format.");
        var runes = (build.Count > 4 ? build[4] : null) as JArray;
        if (runes == null)
            throw new InvalidOperationException("u.gg rune data has an unexpected format.");
        var shards = build.Count > 8 ? build[8]?[2] as JArray : null;

        return new RunePage
        {
            PrimaryStyleId = build[2]?.Value<int>() ?? 0,
            SubStyleId = build[3]?.Value<int>() ?? 0,
            PerkIds = ReadIds(runes, 4),
            SecondaryPerkIds = [runes[4]?.Value<int>() ?? 0, runes[5]?.Value<int>() ?? 0],
            StatShardIds = shards == null ? [5008, 5008, 5001] : shards.Select(shard => shard.Value<int>()).Take(3).ToArray()
        };
    }

    private static async Task<RunePage> ImportOpGgAsync(int championKey, string role,
        CancellationToken cancellationToken)
    {
        var champion = ChampionSlug(championKey);
        var position = OpGgPosition(role);
        var url = $"https://op.gg/lol/champions/{champion}/build/{position}";
        var html = await GetTextAsync(url, cancellationToken);
        return ParseOpGgFragment(html);
    }

    private static RunePage ParseOpGgFragment(string html)
    {
        var marker = html.LastIndexOf("\\\"primary_perk_style\\\":{\\\"id\\\":", StringComparison.Ordinal);
        if (marker < 0)
            marker = html.IndexOf("primary_perk_style", StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidOperationException("op.gg did not include rune data for this champion.");

        var fragment = html.Substring(marker, Math.Min(30000, html.Length - marker))
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        var primary = MatchInt(fragment, "\"primary_perk_style\":{\"id\":(\\d+)");
        var secondary = MatchInt(fragment, "\"perk_sub_style\":{\"id\":(\\d+)");
        var active = Regex.Matches(fragment,
                "\"id\":(\\d+),\"name\":\"(?:\\\\.|[^\"\\\\]*)\",\"image_url\":\"[^\"]+\",\"isActive\":true")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToList();

        var perks = active.Where(id => id is < 5000 or > 6000).Take(6).ToArray();
        var shards = active.Where(id => id is >= 5000 and < 6000).Take(3).ToArray();
        if (perks.Length < 6 || shards.Length < 3)
            throw new InvalidOperationException("op.gg rune data was incomplete.");

        return new RunePage
        {
            PrimaryStyleId = primary,
            SubStyleId = secondary,
            PerkIds = perks.Take(4).ToArray(),
            SecondaryPerkIds = perks.Skip(4).Take(2).ToArray(),
            StatShardIds = shards
        };
    }

    private static async Task<RunePage> ImportLolalyticsAsync(int championKey, string role,
        CancellationToken cancellationToken)
    {
        var champion = ChampionSlug(championKey);
        var lane = LolalyticsLane(role);
        var html = await GetTextAsync($"https://lolalytics.com/lol/{champion}/build/?lane={lane}", cancellationToken);
        return ParseLolalyticsFragment(html);
    }

    private static RunePage ParseLolalyticsFragment(string html)
    {
        var start = html.IndexOf("Primary Runes", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("Lolalytics did not include a rune build.");

        var section = html.Substring(start, Math.Min(30000, html.Length - start));
        var runes = SelectedImages(section, "rune68/(\\d+)\\.webp");
        var shards = SelectedImages(section, "statmod32/(\\d+)\\.webp");
        if (runes.Count < 6 || shards.Count < 3)
            throw new InvalidOperationException("Lolalytics rune data was incomplete.");

        var primary = RuneCatalog.Styles.FirstOrDefault(style => RuneCatalog.ContainsPerk(style.Id, runes[0]))?.Id ?? 0;
        var secondary = RuneCatalog.Styles.FirstOrDefault(style =>
            style.Id != primary && RuneCatalog.ContainsPerk(style.Id, runes[4]))?.Id ?? 0;

        return new RunePage
        {
            PrimaryStyleId = primary,
            SubStyleId = secondary,
            PerkIds = runes.Take(4).ToArray(),
            SecondaryPerkIds = runes.Skip(4).Take(2).ToArray(),
            StatShardIds = shards.Take(3).ToArray()
        };
    }

    private static List<int> SelectedImages(string html, string pattern)
    {
        var selected = new List<int>();
        foreach (Match match in Regex.Matches(html, $"<img[^>]*{pattern}[^>]*>"))
        {
            if (!match.Value.Contains("grayscale", StringComparison.Ordinal))
                selected.Add(int.Parse(match.Groups[1].Value));
        }

        return selected;
    }

    private static async Task<Dictionary<string, int>> ChampionKeysAsync(CancellationToken cancellationToken)
    {
        if (championKeys != null)
            return championKeys;

        await ChampionCacheGate.WaitAsync(cancellationToken);
        try
        {
            if (championKeys != null)
                return championKeys;

            var patch = await CurrentPatchAsync(cancellationToken);
            var document = JObject.Parse(await GetTextAsync(
                $"https://ddragon.leagueoflegends.com/cdn/{patch}/data/en_US/champion.json", cancellationToken));
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var champion in document["data"]?.Children<JProperty>() ?? [])
            {
                var name = champion.Value?["name"]?.ToString() ?? champion.Name;
                var key = champion.Value?["key"]?.Value<int>() ?? 0;
                result[NormalizeChampion(name)] = key;
                result[NormalizeChampion(champion.Name)] = key;
            }

            championKeys = result;
            return result;
        }
        finally
        {
            ChampionCacheGate.Release();
        }
    }

    private static async Task<string> CurrentPatchAsync(CancellationToken cancellationToken)
    {
        var versions = JArray.Parse(await GetTextAsync("https://ddragon.leagueoflegends.com/api/versions.json",
            cancellationToken));
        return versions[0]?.ToString()
               ?? throw new InvalidOperationException("Data Dragon did not return a patch version.");
    }

    private static string UggRoleKey(string role, JObject document)
    {
        // u.gg position ids: 1 jungle, 2 support, 3 bottom, 4 top, 5 middle.
        // Only positions the champion is actually played in contain an overview.
        var requested = role switch
        {
            "top" => "4",
            "jungle" => "1",
            "middle" => "5",
            "bottom" => "3",
            "support" => "2",
            _ => null
        };
        if (requested != null && UggOverview(document[requested]?["1"]) != null)
            return requested;

        return document.Properties()
                   .Where(property => property.Name is "1" or "2" or "3" or "4" or "5")
                   .Where(property => UggOverview(property.Value?["1"]) != null)
                   .OrderByDescending(property => UggGames(property.Value?["1"]))
                   .Select(property => property.Name)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException("u.gg did not return a role for this champion.");
    }

    private static JArray? UggOverview(JToken? rank)
    {
        foreach (var property in rank?.Children<JProperty>() ?? [])
        {
            if (property.Value is JArray overview && overview.Count > 0)
                return overview;
        }

        return null;
    }

    private static double UggGames(JToken? rank)
    {
        var build = UggOverview(rank)?[0]?[0] as JArray;
        var games = build != null && build.Count > 11 ? build[11] : null;
        if (games is JArray gameList)
            return gameList.First?.Value<double>() ?? 0;
        return games?.Value<double>() ?? 0;
    }

    private static string OpGgPosition(string role) => role switch
    {
        "top" => "top",
        "jungle" => "jungle",
        "bottom" => "adc",
        "support" => "support",
        _ => "mid"
    };

    private static string LolalyticsLane(string role) => role switch
    {
        "top" => "top",
        "jungle" => "jungle",
        "bottom" => "bottom",
        "support" => "support",
        _ => "middle"
    };

    private static string ChampionSlug(int championKey)
    {
        var name = ChampionName(championKey);
        return NormalizeChampion(name).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeChampion(string name) =>
        name.Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string RoleLabel(string role) =>
        Roles.FirstOrDefault(item => item.Id == role) is { Label: var label } && !string.IsNullOrEmpty(label)
            ? label
            : "Automatic";

    private static int MatchInt(string value, string pattern)
    {
        var match = Regex.Match(value, pattern);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static int[] ReadIds(JToken? token, int count)
    {
        var values = token?.Select(item => item.Value<int>()).Take(count).ToArray() ?? [];
        return values.Length == count ? values : new int[count];
    }

    private static int[] ReadSecondary(JToken? token)
    {
        var values = token?.Select(item => item.Value<int>()).Skip(4).Take(2).ToArray() ?? [];
        return values.Length == 2 ? values : new int[2];
    }

    private static int[] ReadStatShards(JObject page)
    {
        var perks = page["selectedPerkIds"]?.Select(item => item.Value<int>()).Skip(6).Take(3).ToArray();
        if (perks is { Length: 3 } && perks.All(id => id > 0))
            return perks;

        return
        [
            page["statPerks"]?["offense"]?.Value<int>() ?? 5008,
            page["statPerks"]?["flex"]?.Value<int>() ?? 5008,
            page["statPerks"]?["defense"]?.Value<int>() ?? 5001
        ];
    }

    private static string TrimName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "League Account Manager" : name.Trim();
        return trimmed.Length <= 25 ? trimmed : trimmed[..25];
    }

    private static async Task<string> SendAsync(string method, string endpoint, string data,
        CancellationToken cancellationToken)
    {
        var result = await Lcu.Connector("league", method, endpoint, data, cancellationToken);
        if (result is not HttpResponseMessage response)
            throw new InvalidOperationException("League client is not running.");

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"League client returned {(int)response.StatusCode} for {endpoint}.");
            return body;
        }
    }

    private static async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
        return client;
    }
}

internal readonly record struct RuneImportSource(string Id, string Label);

internal readonly record struct RuneImportRole(string Id, string Label);

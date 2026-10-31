using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.Misc;

/// <summary>
///     Reads and writes the signed-in Valorant player's weapon loadout, and keeps a local
///     per-agent copy of the skins so they can be restored when that agent is selected.
/// </summary>
internal static class ValorantLoadoutService
{
    internal const string SkinItemTypeId = "e7c63390-eda7-46e0-bb7a-a6abdacd2433";
    internal const string SkinVariantItemTypeId = "3ad1b2b2-acdb-4524-852f-954a76ddae0a";
    internal const string DefaultSetName = "Default";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient CatalogClient = new();
    private static readonly SemaphoreSlim CatalogLock = new(1, 1);
    private static readonly SemaphoreSlim StoreLock = new(1, 1);
    private static readonly SemaphoreSlim SessionLock = new(1, 1);

    private static WeaponCatalog? catalog;
    private static ValorantLoadoutStore? store;
    private static ValorantSession? sharedSession;

    internal static string ShardForRegion(string? region)
    {
        return (region ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "latam" or "br" or "na" or "pbe" => "na",
            "eu" => "eu",
            "ap" => "ap",
            "kr" => "kr",
            "" => "na",
            var other => other
        };
    }

    /// <summary>
    ///     Reads the Valorant affinity from an entitlements access token. <c>pp.c</c> is the
    ///     Valorant region (eu, na, ap, kr). <c>dat.r</c> is the League region and must not be used.
    /// </summary>
    internal static string? RegionFromAccessToken(string? accessToken)
    {
        var payload = DecodeJwtPayload(accessToken);
        var region = payload?["pp"]?["c"]?.ToString();
        return string.IsNullOrWhiteSpace(region) ? null : region;
    }

    private static JObject? DecodeJwtPayload(string? token)
    {
        var payload = token?.Split('.') is { Length: >= 2 } parts ? parts[1] : null;
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    internal static async Task<ValorantSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        if (sharedSession != null)
            return sharedSession;

        await SessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            sharedSession ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            return sharedSession;
        }
        finally
        {
            SessionLock.Release();
        }
    }

    private static async Task<ValorantSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var (client, accessToken, _, puuid, idToken) = await Lcu.CreateValorantClientAsync().ConfigureAwait(false);
        var region = await ResolveRegionAsync(client, accessToken, idToken, cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine("[ValorantLoadout] Stored the Riot Client token and authorization.");
        return new ValorantSession(client, puuid, ShardForRegion(region), region);
    }

    private static async Task<ValorantSession> RefreshSessionAsync(CancellationToken cancellationToken)
    {
        await SessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            sharedSession = await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            DebugConsole.WriteLine("[ValorantLoadout] Refreshed the Riot Client token after a failed request.");
            return sharedSession;
        }
        finally
        {
            SessionLock.Release();
        }
    }

    internal static async Task<WeaponCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (catalog != null)
            return catalog;

        await CatalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (catalog != null)
                return catalog;

            var weaponsBody = await CatalogClient
                .GetStringAsync("https://valorant-api.com/v1/weapons", cancellationToken).ConfigureAwait(false);
            var skinsBody = await CatalogClient
                .GetStringAsync("https://valorant-api.com/v1/weapons/skins", cancellationToken).ConfigureAwait(false);
            var agentsBody = await CatalogClient
                .GetStringAsync("https://valorant-api.com/v1/agents?isPlayableCharacter=true", cancellationToken)
                .ConfigureAwait(false);

            catalog = WeaponCatalog.Parse(weaponsBody, skinsBody, agentsBody);
            return catalog;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    internal static async Task<PlayerLoadout> GetLoadoutAsync(ValorantSession session,
        CancellationToken cancellationToken = default)
    {
        var body = await GetStringAsync(session,
            $"https://pd.{session.Shard}.a.pvp.net/personalization/v3/players/{session.Puuid}/playerloadout",
            cancellationToken).ConfigureAwait(false);
        return ParseLoadout(body);
    }

    internal static async Task<HashSet<string>> GetOwnedSkinIdsAsync(ValorantSession session,
        CancellationToken cancellationToken = default)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await AddOwnedAsync(session, SkinItemTypeId, owned, cancellationToken).ConfigureAwait(false);
        await AddOwnedAsync(session, SkinVariantItemTypeId, owned, cancellationToken).ConfigureAwait(false);
        return owned;
    }

    internal static async Task<PlayerLoadout> ApplySkinAsync(ValorantSession session, PlayerLoadout current,
        string weaponId, SkinOption skin, CancellationToken cancellationToken = default)
    {
        var guns = current.Document["Guns"] as JArray ??
                   throw new InvalidOperationException("The current loadout has no weapons.");
        var gun = guns.FirstOrDefault(item =>
            string.Equals(item?["ID"]?.ToString(), weaponId, StringComparison.OrdinalIgnoreCase)) ??
                  throw new InvalidOperationException("That weapon is not in the current loadout.");

        var owned = await GetOwnedSkinIdsAsync(session, cancellationToken).ConfigureAwait(false);
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        gun["SkinID"] = skin.SkinId;
        gun["SkinLevelID"] = catalog.HighestOwnedLevel(skin.SkinId, owned) ?? skin.LevelId;
        gun["ChromaID"] = skin.ChromaId;

        var payload = current.Document.DeepClone();
        payload["Subject"]?.Parent?.Remove();
        payload["Version"]?.Parent?.Remove();
        var body = payload.ToString(Formatting.None);
        var url = $"https://pd.{session.Shard}.a.pvp.net/personalization/v3/players/{session.Puuid}/playerloadout";

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsync(session, HttpMethod.Put, url, content, body, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Valorant rejected the loadout ({(int)response.StatusCode}).");

        DebugConsole.WriteLine($"[ValorantLoadout] Equipped {skin.Name}.");
        return await GetLoadoutAsync(session, cancellationToken).ConfigureAwait(false);
    }

    internal enum GamePhase
    {
        Unknown,
        Menu,
        AgentSelect,
        InGame
    }

    /// <summary>
    ///     Reads the latest Valorant flow state from the local log. Agent select is Pregame,
    ///     and the match itself is InGame.
    /// </summary>
    internal static GamePhase ReadGamePhase()
    {
        try
        {
            var tail = ReadLogTail();
            if (tail == null)
                return GamePhase.Unknown;

            var pregame = LastIndexOfState(tail, "Pregame");
            var inGame = LastIndexOfState(tail, "InGame");
            var menu = LastIndexOfState(tail, "MainMenu");
            // A new pregame is still agent select until a later InGame or MainMenu line appears.
            if (pregame >= 0 && pregame >= inGame && pregame >= menu)
                return GamePhase.AgentSelect;

            var latest = Math.Max(pregame, Math.Max(inGame, menu));
            if (latest < 0)
                return GamePhase.Unknown;
            if (latest == inGame)
                return GamePhase.InGame;
            if (latest == pregame)
                return GamePhase.AgentSelect;
            return GamePhase.Menu;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return GamePhase.Unknown;
        }
    }

    /// <summary>
    ///     True when the local Valorant log says the player is in agent select. The pregame
    ///     service is only called while this is true, so menus do not generate a request every poll.
    /// </summary>
    internal static bool IsInAgentSelect() => ReadGamePhase() == GamePhase.AgentSelect;

    private static string? ReadLogTail()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VALORANT", "Saved", "Logs", "ShooterGame.log");
            if (!File.Exists(path))
                return null;

            var info = new FileInfo(path);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(2))
                return null;

            var length = info.Length;
            var read = (int)Math.Min(length, 256 * 1024);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(length - read, SeekOrigin.Begin);
            var buffer = new byte[read];
            var offset = 0;
            while (offset < read)
            {
                var count = stream.Read(buffer, offset, read - offset);
                if (count == 0)
                    break;
                offset += count;
            }

            return Encoding.UTF8.GetString(buffer, 0, offset);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int LastIndexOfState(string text, string state, int before = -1)
    {
        var limit = before < 0 ? text.Length : before;
        if (limit <= 0)
            return -1;
        limit = Math.Min(limit, text.Length);

        var broadcast = text.LastIndexOf($"Broadcasting state changed to {state}", limit, StringComparison.Ordinal);
        var current = text.LastIndexOf($"Current state {state} ", limit, StringComparison.Ordinal);
        return Math.Max(broadcast, current);
    }

    internal static async Task<string?> SelectedAgentIdAsync(ValorantSession session,
        CancellationToken cancellationToken = default)
    {
        var playerBody = await TryGetStringAsync(session,
            $"https://glz-{session.Region}-1.{session.Shard}.a.pvp.net/pregame/v1/players/{session.Puuid}",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerBody))
            return null;

        var matchId = JObject.Parse(playerBody)["MatchID"]?.ToString();
        if (string.IsNullOrWhiteSpace(matchId))
            return null;

        var matchBody = await TryGetStringAsync(session,
            $"https://glz-{session.Region}-1.{session.Shard}.a.pvp.net/pregame/v1/matches/{matchId}",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(matchBody))
            return null;

        return ReadSelectedCharacter(matchBody, session.Puuid);
    }

    internal static async Task<ValorantLoadoutStore> LoadStoreAsync(CancellationToken cancellationToken = default)
    {
        if (store != null)
            return store;

        await StoreLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (store != null)
                return store;

            var path = StorePath();
            if (!File.Exists(path))
            {
                store = new ValorantLoadoutStore();
                return store;
            }

            store = JsonConvert.DeserializeObject<ValorantLoadoutStore>(await File.ReadAllTextAsync(path, cancellationToken)
                       .ConfigureAwait(false)) ?? new ValorantLoadoutStore();
            return store;
        }
        finally
        {
            StoreLock.Release();
        }
    }

    internal static async Task SaveAgentLoadoutAsync(string puuid, string agentId, IEnumerable<EquippedGun> guns,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        var account = saved.Accounts.FirstOrDefault(item =>
            string.Equals(item.Puuid, puuid, StringComparison.OrdinalIgnoreCase));
        if (account == null)
        {
            account = new AccountLoadouts { Puuid = puuid };
            saved.Accounts.Add(account);
        }

        var agent = account.Agents.FirstOrDefault(item =>
            string.Equals(item.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
        if (agent == null)
        {
            agent = new AgentLoadout { AgentId = agentId };
            account.Agents.Add(agent);
        }

        agent.Guns = ToSavedGuns(guns);

        await WriteStoreAsync(saved, cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantLoadout] Saved a loadout for agent {agentId}.");
    }

    internal static async Task DeleteAgentLoadoutAsync(string puuid, string agentId,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        var account = saved.Accounts.FirstOrDefault(item =>
            string.Equals(item.Puuid, puuid, StringComparison.OrdinalIgnoreCase));
        account?.Agents.RemoveAll(agent => string.Equals(agent.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
        await WriteStoreAsync(saved, cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantLoadout] Reset the loadout for agent {agentId}.");
    }

    internal static async Task SaveNamedLoadoutAsync(string puuid, string name, IEnumerable<EquippedGun> guns,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        var account = saved.Accounts.FirstOrDefault(item =>
            string.Equals(item.Puuid, puuid, StringComparison.OrdinalIgnoreCase));
        if (account == null)
        {
            account = new AccountLoadouts { Puuid = puuid };
            saved.Accounts.Add(account);
        }

        var set = account.Sets.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (set == null)
        {
            set = new NamedLoadout { Name = name };
            account.Sets.Add(set);
        }

        set.Guns = ToSavedGuns(guns);
        await WriteStoreAsync(saved, cancellationToken).ConfigureAwait(false);
        DebugConsole.WriteLine($"[ValorantLoadout] Saved the loadout set {name}.");
    }

    internal static async Task<IReadOnlyList<NamedLoadout>> GetNamedLoadoutsAsync(string puuid,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        return saved.Accounts
            .FirstOrDefault(account => string.Equals(account.Puuid, puuid, StringComparison.OrdinalIgnoreCase))
            ?.Sets.OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    internal static async Task DeleteNamedLoadoutAsync(string puuid, string name,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        var account = saved.Accounts.FirstOrDefault(item =>
            string.Equals(item.Puuid, puuid, StringComparison.OrdinalIgnoreCase));
        account?.Sets.RemoveAll(set => string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase));
        await WriteStoreAsync(saved, cancellationToken).ConfigureAwait(false);
    }

    private static List<SavedGun> ToSavedGuns(IEnumerable<EquippedGun> guns) => guns.Select(gun => new SavedGun
    {
        WeaponId = gun.WeaponId,
        SkinId = gun.SkinId,
        SkinLevelId = gun.SkinLevelId,
        ChromaId = gun.ChromaId
    }).ToList();

    internal static async Task<AgentLoadout?> FindAgentLoadoutAsync(string puuid, string agentId,
        CancellationToken cancellationToken = default)
    {
        var saved = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
        return saved.Accounts
            .FirstOrDefault(account => string.Equals(account.Puuid, puuid, StringComparison.OrdinalIgnoreCase))
            ?.Agents.FirstOrDefault(agent => string.Equals(agent.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
    }

    internal static Task<bool> ApplyAgentLoadoutAsync(ValorantSession session, AgentLoadout saved,
        CancellationToken cancellationToken = default) =>
        ApplyGunsAsync(session, saved.Guns, requireOwned: true, cancellationToken);

    internal static Task<bool> ApplyGunsAsync(ValorantSession session, IEnumerable<SavedGun> guns,
        CancellationToken cancellationToken = default) =>
        ApplyGunsAsync(session, guns, requireOwned: true, cancellationToken);

    internal static Task<bool> ApplyGunsAsync(ValorantSession session, IEnumerable<EquippedGun> guns,
        CancellationToken cancellationToken = default) =>
        ApplyGunsAsync(session, ToSavedGuns(guns), requireOwned: true, cancellationToken);

    internal static Task<bool> ApplyNamedLoadoutAsync(ValorantSession session, NamedLoadout saved,
        CancellationToken cancellationToken = default) =>
        ApplyGunsAsync(session, saved.Guns, requireOwned: true, cancellationToken);

    private static async Task<bool> ApplyGunsAsync(ValorantSession session, IEnumerable<SavedGun> guns,
        bool requireOwned, CancellationToken cancellationToken)
    {
        var current = await GetLoadoutAsync(session, cancellationToken).ConfigureAwait(false);
        var owned = requireOwned
            ? await GetOwnedSkinIdsAsync(session, cancellationToken).ConfigureAwait(false)
            : null;
        var catalog = requireOwned
            ? await GetCatalogAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var changed = false;
        foreach (var savedGun in guns)
        {
            if (string.IsNullOrWhiteSpace(savedGun.SkinId))
                continue;
            // Entitlements store skin level ids, not the skin id saved on a loadout.
            if (owned != null && catalog != null && !catalog.OwnsSkin(savedGun.SkinId, owned) &&
                !owned.Contains(savedGun.ChromaId))
                continue;

            var gun = (current.Document["Guns"] as JArray)?.FirstOrDefault(item =>
                string.Equals(item?["ID"]?.ToString(), savedGun.WeaponId, StringComparison.OrdinalIgnoreCase));
            if (gun == null)
                continue;

            var levelId = owned != null && catalog != null
                ? catalog.HighestOwnedLevel(savedGun.SkinId, owned) ?? savedGun.SkinLevelId
                : savedGun.SkinLevelId;
            if (string.Equals(gun["SkinID"]?.ToString(), savedGun.SkinId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(gun["SkinLevelID"]?.ToString(), levelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(gun["ChromaID"]?.ToString(), savedGun.ChromaId, StringComparison.OrdinalIgnoreCase))
                continue;

            gun["SkinID"] = savedGun.SkinId;
            gun["SkinLevelID"] = levelId;
            gun["ChromaID"] = savedGun.ChromaId;
            changed = true;
        }

        if (!changed)
            return false;

        var payload = current.Document.DeepClone();
        payload["Subject"]?.Parent?.Remove();
        payload["Version"]?.Parent?.Remove();
        var body = payload.ToString(Formatting.None);
        var url = $"https://pd.{session.Shard}.a.pvp.net/personalization/v3/players/{session.Puuid}/playerloadout";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsync(session, HttpMethod.Put, url, content, body, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Valorant rejected the loadout ({(int)response.StatusCode}).");

        DebugConsole.WriteLine("[ValorantLoadout] Applied the saved agent loadout.");
        return true;
    }

    internal static PlayerLoadout ParseLoadout(string body) =>
        GetLoadoutFromJson(JObject.Parse(body));

    internal static string? ReadSelectedCharacter(string matchBody, string puuid)
    {
        var match = JObject.Parse(matchBody);
        var teams = ExpandTeams(match["AllyTeam"]).Concat(ExpandTeams(match["Teams"]))
            .Where(team => team is JObject);
        foreach (var team in teams)
        {
            var self = (team?["Players"] as JArray)?.FirstOrDefault(player =>
                string.Equals(player?["Subject"]?.ToString(), puuid, StringComparison.OrdinalIgnoreCase));
            var characterId = self?["CharacterID"]?.ToString();
            if (!string.IsNullOrWhiteSpace(characterId))
                return characterId;
        }

        return null;
    }

    private static PlayerLoadout GetLoadoutFromJson(JObject json)
    {
        var guns = (json["Guns"] as JArray ?? [])
            .Select(gun => new EquippedGun(
                gun["ID"]?.ToString() ?? string.Empty,
                gun["SkinID"]?.ToString() ?? string.Empty,
                gun["SkinLevelID"]?.ToString() ?? string.Empty,
                gun["ChromaID"]?.ToString() ?? string.Empty,
                gun["CharmID"]?.ToString(),
                gun["CharmLevelID"]?.ToString(),
                gun["CharmInstanceID"]?.ToString()))
            .Where(gun => !string.IsNullOrWhiteSpace(gun.WeaponId))
            .ToList();
        return new PlayerLoadout(json, guns);
    }

    private static async Task<string> ResolveRegionAsync(HttpClient client, string accessToken, string idToken,
        CancellationToken cancellationToken)
    {
        // The Riot Client locale is the League region (for example NA / NA1) and is wrong for
        // Valorant whenever the two games are on different shards. The access token from
        // /entitlements/v1/token carries the Valorant affinity in pp.c.
        var tokenRegion = RegionFromAccessToken(accessToken);
        if (!string.IsNullOrWhiteSpace(tokenRegion))
        {
            DebugConsole.WriteLine($"[ValorantLoadout] Region {tokenRegion} from access token.");
            return tokenRegion;
        }

        var geoBody = $"{{\"id_token\":\"{idToken}\"}}";
        using var content = new StringContent(geoBody, Encoding.UTF8, "application/json");
        using var geo = await SendAsync(client, HttpMethod.Put,
            "https://riot-geo.pas.si.riotgames.com/pas/v1/product/valorant", content, geoBody, cancellationToken)
            .ConfigureAwait(false);
        var body = await geo.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!geo.IsSuccessStatusCode)
            throw new InvalidOperationException("Valorant region could not be resolved.");

        return JObject.Parse(body)["affinities"]?["live"]?.ToString()
               ?? throw new InvalidOperationException("Valorant region could not be resolved.");
    }

    private static async Task AddOwnedAsync(ValorantSession session, string itemTypeId, HashSet<string> owned,
        CancellationToken cancellationToken)
    {
        var body = await TryGetStringAsync(session,
            $"https://pd.{session.Shard}.a.pvp.net/store/v1/entitlements/{session.Puuid}/{itemTypeId}",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
            return;

        foreach (var entitlement in JObject.Parse(body)["Entitlements"] as JArray ?? [])
        {
            var itemId = entitlement?["ItemID"]?.ToString();
            if (!string.IsNullOrWhiteSpace(itemId))
                owned.Add(itemId);
        }
    }

    private static IEnumerable<JToken?> ExpandTeams(JToken? teams) =>
        teams is JArray array ? array : teams == null ? [] : [teams];

    private static async Task<string> GetStringAsync(ValorantSession session, string url,
        CancellationToken cancellationToken)
    {
        var body = await TryGetStringAsync(session, url, cancellationToken).ConfigureAwait(false);
        return body ?? throw new InvalidOperationException("Valorant did not return the loadout.");
    }

    private static async Task<string?> TryGetStringAsync(ValorantSession session, string url,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(session, HttpMethod.Get, url, null, null, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? body : null;
    }

    private static async Task<HttpResponseMessage> SendAsync(ValorantSession session, HttpMethod method, string url,
        HttpContent? content, string? requestBody, CancellationToken cancellationToken)
    {
        var response = await SendAsync(session.Client, method, url, content, requestBody, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode || !ShouldRefresh(response.StatusCode))
            return response;

        response.Dispose();
        var previous = session.Client;
        var refreshed = await RefreshSessionAsync(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(previous, refreshed.Client))
            previous.Dispose();
        if (!ReferenceEquals(session, refreshed))
        {
            session.Client = refreshed.Client;
            session.Puuid = refreshed.Puuid;
            session.Shard = refreshed.Shard;
            session.Region = refreshed.Region;
        }

        var retry = content == null
            ? null
            : new StringContent(requestBody ?? string.Empty, Encoding.UTF8, "application/json");
        return await SendAsync(session.Client, method, url, retry, requestBody, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldRefresh(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>
    ///     Sends a Valorant request and records it in the LCU traffic view, including requests
    ///     that fail before a response comes back.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url,
        HttpContent? content, string? requestBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (content != null)
            request.Content = content;

        var record = LcuRequestLog.Add(
            "valorant",
            method.Method,
            url,
            requestBody ?? string.Empty,
            null,
            "Pending",
            string.Empty,
            0,
            trafficType: "HTTP",
            requestHeaders: FormatHeaders(client, request),
            direction: "Outgoing");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Content = new StringContent(responseBody, Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
            stopwatch.Stop();
            LcuRequestLog.Update(
                record.Id,
                (int)response.StatusCode,
                response.ReasonPhrase ?? response.StatusCode.ToString(),
                responseBody,
                stopwatch.ElapsedMilliseconds,
                responseHeaders: FormatHeaders(response));
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            LcuRequestLog.Update(record.Id, null, "Failed", string.Empty, stopwatch.ElapsedMilliseconds,
                exception.Message);
            throw;
        }
    }

    private static string FormatHeaders(HttpClient client, HttpRequestMessage request)
    {
        var headers = client.DefaultRequestHeaders
            .Concat(request.Headers)
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>());
        return string.Join(Environment.NewLine,
            headers.Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
    }

    private static string FormatHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content?.Headers ??
                                              Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>());
        return string.Join(Environment.NewLine,
            headers.Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
    }

    private static async Task WriteStoreAsync(ValorantLoadoutStore saved, CancellationToken cancellationToken)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonConvert.SerializeObject(saved, Formatting.Indented),
            cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }

    private static string StorePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "League Account Manager", "valorant-loadouts.json");

    internal sealed class ValorantSession(HttpClient client, string puuid, string shard, string region) : IDisposable
    {
        public HttpClient Client { get; set; } = client;
        public string Puuid { get; set; } = puuid;
        public string Shard { get; set; } = shard;
        public string Region { get; set; } = region.ToLowerInvariant();

        public void Dispose() => Client.Dispose();
    }

    internal sealed record EquippedGun(string WeaponId, string SkinId, string SkinLevelId, string ChromaId,
        string? CharmId, string? CharmLevelId, string? CharmInstanceId);

    internal sealed class PlayerLoadout(JObject document, IReadOnlyList<EquippedGun> guns)
    {
        public JObject Document { get; } = document;
        public IReadOnlyList<EquippedGun> Guns { get; } = guns;
    }

    internal sealed class WeaponCatalog
    {
        private readonly Dictionary<string, WeaponDefinition> weapons;
        private readonly Dictionary<string, SkinDefinition> skinsById;
        private readonly Dictionary<string, AgentDefinition> agents;

        private WeaponCatalog(Dictionary<string, WeaponDefinition> weapons,
            Dictionary<string, SkinDefinition> skinsById, Dictionary<string, AgentDefinition> agents)
        {
            this.weapons = weapons;
            this.skinsById = skinsById;
            this.agents = agents;
        }

        public IReadOnlyList<WeaponDefinition> Weapons =>
            weapons.Values.OrderBy(weapon => weapon.Category).ThenBy(weapon => weapon.Name).ToList();

        public IReadOnlyList<AgentDefinition> Agents =>
            agents.Values.OrderBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase).ToList();

        public string AgentName(string agentId) =>
            agents.TryGetValue(agentId, out var agent) ? agent.Name : "Unknown agent";

        public AgentDefinition? Agent(string agentId) =>
            agents.TryGetValue(agentId, out var agent) ? agent : null;

        public WeaponDefinition? Weapon(string weaponId) =>
            weapons.TryGetValue(weaponId, out var weapon) ? weapon : null;

        public SkinDefinition? Skin(string skinId) =>
            skinsById.TryGetValue(skinId, out var skin) ? skin : null;

        public bool OwnsSkin(string skinId, IReadOnlySet<string> ownedIds) =>
            HighestOwnedLevel(skinId, ownedIds) != null || ownedIds.Contains(skinId);

        /// <summary>
        ///     The last owned level in catalog order. Valorant lists levels from the base skin
        ///     up to the fully upgraded variant, and entitlements store those level ids.
        /// </summary>
        public string? HighestOwnedLevel(string skinId, IReadOnlySet<string> ownedIds)
        {
            if (!skinsById.TryGetValue(skinId, out var skin))
                return null;

            var levels = skin.Options.FirstOrDefault()?.LevelIds;
            return levels?.LastOrDefault(ownedIds.Contains);
        }

        public IReadOnlyList<SavedGun> StandardGuns() => Weapons
            .Select(weapon => weapon.Skins.FirstOrDefault(skin => skin.IsStandard)?.Options.FirstOrDefault())
            .Where(option => option != null)
            .Select(option => new SavedGun
            {
                WeaponId = option!.WeaponId,
                SkinId = option.SkinId,
                SkinLevelId = option.LevelId,
                ChromaId = option.ChromaId
            })
            .ToList();

        public IReadOnlyList<SkinOption> OwnedOptions(string weaponId, IReadOnlySet<string> ownedIds)
        {
            if (!weapons.TryGetValue(weaponId, out var weapon))
                return [];

            return weapon.Skins
                .SelectMany(skin => skin.Options)
                .Where(option => option.IsStandard || option.IsRandomFavorite ||
                                 ownedIds.Contains(option.ChromaId) ||
                                 (option.IsBaseChroma && option.LevelIds.Any(ownedIds.Contains)))
                .GroupBy(option => option.ChromaId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(option => option.IsStandard ? 0 : option.IsRandomFavorite ? 1 : 2)
                .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static WeaponCatalog Parse(string weaponsBody, string skinsBody, string agentsBody)
        {
            var skinDocuments = ((JObject.Parse(skinsBody)["data"] as JArray) ?? [])
                .ToDictionary(item => item["uuid"]?.ToString() ?? string.Empty, item => item,
                    StringComparer.OrdinalIgnoreCase);

            var weapons = new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var weapon in JObject.Parse(weaponsBody)["data"] as JArray ?? [])
            {
                var weaponId = weapon["uuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(weaponId))
                    continue;

                var skins = new List<SkinDefinition>();
                foreach (var skinRef in weapon["skins"] as JArray ?? [])
                {
                    var skinId = skinRef["uuid"]?.ToString();
                    if (string.IsNullOrWhiteSpace(skinId) || !skinDocuments.TryGetValue(skinId, out var skin))
                        continue;

                    var parsed = SkinDefinition.Parse(weaponId, skin);
                    if (parsed != null)
                        skins.Add(parsed);
                }

                weapons[weaponId] = new WeaponDefinition(
                    weaponId,
                    weapon["displayName"]?.ToString() ?? "Weapon",
                    weapon["category"]?.ToString()?.Replace("EEquippableCategory::", string.Empty) ?? "Weapon",
                    weapon["displayIcon"]?.ToString(),
                    skins);
            }

            var skinsById = weapons.Values.SelectMany(weapon => weapon.Skins)
                .ToDictionary(skin => skin.SkinId, skin => skin, StringComparer.OrdinalIgnoreCase);
            var agents = ((JObject.Parse(agentsBody)["data"] as JArray) ?? [])
                .Select(AgentDefinition.Parse)
                .Where(agent => agent != null)
                .ToDictionary(agent => agent!.Id, agent => agent!, StringComparer.OrdinalIgnoreCase);

            return new WeaponCatalog(weapons, skinsById, agents);
        }
    }

    internal sealed record AgentDefinition(string Id, string Name, string? Role, string? PortraitUrl, string? IconUrl)
    {
        public static AgentDefinition? Parse(JToken agent)
        {
            var id = agent["uuid"]?.ToString();
            var name = agent["displayName"]?.ToString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                return null;

            return new AgentDefinition(
                id,
                name,
                agent["role"]?["displayName"]?.ToString(),
                FirstUrl(agent["fullPortrait"], agent["bustPortrait"], agent["displayIcon"]),
                FirstUrl(agent["displayIcon"], agent["bustPortrait"]));
        }

        private static string? FirstUrl(params JToken?[] values) =>
            values.Select(value => value?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    internal sealed record WeaponDefinition(string Id, string Name, string Category, string? IconUrl,
        IReadOnlyList<SkinDefinition> Skins);

    internal sealed class SkinDefinition
    {
        private SkinDefinition(string skinId, string name, bool isStandard, bool isRandomFavorite,
            IReadOnlyList<SkinOption> options)
        {
            SkinId = skinId;
            Name = name;
            IsStandard = isStandard;
            IsRandomFavorite = isRandomFavorite;
            Options = options;
        }

        public string SkinId { get; }
        public string Name { get; }
        public bool IsStandard { get; }
        public bool IsRandomFavorite { get; }
        public IReadOnlyList<SkinOption> Options { get; }

        public static SkinDefinition? Parse(string weaponId, JToken skin)
        {
            var skinId = skin["uuid"]?.ToString();
            var name = skin["displayName"]?.ToString();
            var levels = skin["levels"] as JArray;
            var chromas = skin["chromas"] as JArray;
            if (string.IsNullOrWhiteSpace(skinId) || string.IsNullOrWhiteSpace(name) || levels == null ||
                chromas == null || levels.Count == 0 || chromas.Count == 0)
                return null;

            var levelIds = levels.Select(level => level?["uuid"]?.ToString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();
            if (levelIds.Count == 0)
                return null;
            var levelId = levelIds[^1];

            var isStandard = name.Contains("Standard", StringComparison.OrdinalIgnoreCase);
            var isRandomFavorite = name.Contains("Random Favorite", StringComparison.OrdinalIgnoreCase);
            // The card shows the chroma render, matching Valorant's collection. Random Favorite
            // chromas have no render, so those cards use the skin image instead.
            var skinIcon = FirstUrl(skin["displayIcon"], levels.Last()?["displayIcon"], levels.First()?["displayIcon"]);
            var firstChroma = true;
            var options = chromas.Select(chroma =>
            {
                var chromaId = chroma["uuid"]?.ToString() ?? string.Empty;
                var chromaName = chroma["displayName"]?.ToString();
                var variant = chromaName?.Replace(name, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                var label = string.IsNullOrWhiteSpace(variant) ||
                            variant.Equals("Standard", StringComparison.OrdinalIgnoreCase)
                    ? name
                    : $"{name}\n({variant})";
                var render = FirstUrl(chroma["fullRender"], chroma["displayIcon"]);
                var swatch = FirstUrl(chroma["swatch"], chroma["displayIcon"]);
                // The first chroma is the skin itself. Later chromas are the extra colors.
                var baseChroma = firstChroma;
                firstChroma = false;
                return new SkinOption(weaponId, skinId, levelId, levelIds, chromaId, label,
                    isRandomFavorite ? skinIcon ?? render : render ?? skinIcon,
                    swatch, isStandard, isRandomFavorite, baseChroma);
            }).Where(option => !string.IsNullOrWhiteSpace(option.ChromaId)).ToList();

            return options.Count == 0
                ? null
                : new SkinDefinition(skinId, name, isStandard, isRandomFavorite, options);
        }

        private static string? FirstUrl(params JToken?[] values) =>
            values.Select(value => value?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    internal sealed record SkinOption(string WeaponId, string SkinId, string LevelId,
        IReadOnlyList<string> LevelIds, string ChromaId, string Name, string? IconUrl, string? ChromaIconUrl,
        bool IsStandard, bool IsRandomFavorite, bool IsBaseChroma);

    internal sealed class ValorantLoadoutStore
    {
        public List<AccountLoadouts> Accounts { get; set; } = [];
    }

    internal sealed class AccountLoadouts
    {
        public string Puuid { get; set; } = string.Empty;
        public List<AgentLoadout> Agents { get; set; } = [];
        public List<NamedLoadout> Sets { get; set; } = [];
    }

    internal sealed class NamedLoadout
    {
        public string Name { get; set; } = string.Empty;
        public List<SavedGun> Guns { get; set; } = [];
    }

    internal sealed class AgentLoadout
    {
        public string AgentId { get; set; } = string.Empty;
        public List<SavedGun> Guns { get; set; } = [];
    }

    internal sealed class SavedGun
    {
        public string WeaponId { get; set; } = string.Empty;
        public string SkinId { get; set; } = string.Empty;
        public string SkinLevelId { get; set; } = string.Empty;
        public string ChromaId { get; set; } = string.Empty;
    }
}

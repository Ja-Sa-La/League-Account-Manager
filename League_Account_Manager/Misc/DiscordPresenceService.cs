using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Misc;

/// <summary>
///     Publishes the signed-in League and Valorant accounts, including live matches, to the
///     local Discord clients. Discord allows one activity per application, so each game uses its
///     own application identity. The same activity is sent to every running Discord client.
/// </summary>
internal static class DiscordPresenceService
{
    internal const int DetailsLimit = 128;
    internal const int StateLimit = 128;
    internal const int ButtonLabelLimit = 32;

    /// <summary>
    ///     Public application ID for the League Account Manager Discord application. This is not
    ///     a secret; the client secret and bot token must never be added here.
    /// </summary>
    internal const string ApplicationId = "1566553109856256032";

    /// <summary>
    ///     Public application ID used for the second simultaneous activity. Discord permits only
    ///     one activity per application, so Valorant cannot share the League identity.
    /// </summary>
    internal const string ValorantApplicationId = "1566566585706086400";
    internal const string LeagueImage = "league";
    internal const string ValorantImage = "valorant";

    private static readonly HttpClient CatalogClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly SemaphoreSlim CatalogLock = new(1, 1);
    private static Dictionary<int, string>? championNames;
    private static Dictionary<string, string>? mapNames;
    private static Dictionary<string, (string Name, string ImageKey)>? agents;
    private const int DiscordClientSlots = 10;
    private static readonly object ConnectionLock = new();
    private static readonly Dictionary<string, List<DiscordClient>> Connections = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Published = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Presence> LastPresences = new(StringComparer.Ordinal);
    private static string? lastValorantJoinUrl;

    /// <summary>
    ///     Client for Riot's smart-url resolve service (the endpoint behind gg.riotgames.com).
    ///     The service is global and unauthenticated, so any shard can validate any join code.
    /// </summary>
    private static readonly HttpClient ResolveClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly string[] ResolveHosts = ["euc-prod.pp.sgp.pvp.net", "usw-prod.pp.sgp.pvp.net"];

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!Settings.settingsloaded.DiscordRichPresence)
                {
                    Clear();
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var presences = await BuildPresencesAsync(cancellationToken).ConfigureAwait(false);
                PublishAll(presences);

                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DebugConsole.WriteLine($"[Discord] Presence update failed: {exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            }
        }

        Clear();
    }

    internal static void Clear()
    {
        lock (ConnectionLock)
        {
            LastPresences.Clear();
            foreach (var applicationId in Connections.Keys.ToArray())
                ClearApplication(applicationId);
        }
    }

    internal static async Task<IReadOnlyList<Presence>> BuildPresencesAsync(CancellationToken cancellationToken)
    {
        var presences = new List<Presence>(2);
        var leagueRunning = Process.GetProcessesByName("LeagueClientUx").Length != 0 ||
                            Process.GetProcessesByName("League of Legends").Length != 0;
        var valorantRunning = Process.GetProcessesByName("VALORANT-Win64-Shipping").Length != 0 ||
                              Process.GetProcessesByName("Riot Client").Length != 0 ||
                              Process.GetProcessesByName("RiotClientUx").Length != 0;

        if (leagueRunning)
            await AddPresenceAsync(presences, ApplicationId, () => ReadLeagueAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

        if (valorantRunning)
            await AddPresenceAsync(presences, ValorantApplicationId, () => ReadValorantAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

        return presences;
    }

    private static async Task AddPresenceAsync(List<Presence> presences, string applicationId,
        Func<Task<Presence?>> read, CancellationToken cancellationToken)
    {
        var presence = await ReadOrReuseAsync(applicationId, read, cancellationToken).ConfigureAwait(false);
        if (presence != null)
            presences.Add(presence);
    }

    private static async Task<Presence?> ReadOrReuseAsync(string applicationId, Func<Task<Presence?>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            var presence = await read().ConfigureAwait(false);
            if (presence == null)
                return Reuse(applicationId);

            lock (ConnectionLock)
                LastPresences[applicationId] = presence;
            return presence;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Discord] {applicationId} update failed: {exception.Message}");
            return Reuse(applicationId);
        }
    }

    private static Presence? Reuse(string applicationId)
    {
        lock (ConnectionLock)
            return LastPresences.TryGetValue(applicationId, out var previous) ? previous : null;
    }

    private static async Task<Presence?> ReadLeagueAsync(CancellationToken cancellationToken)
    {
        var inGame = Process.GetProcessesByName("League of Legends").Length != 0;
        var summoner = inGame
            ? null
            : await GetLeagueAsync("/lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
        if (!inGame && summoner?["gameName"] == null)
            return null;

        var settings = Settings.settingsloaded;
        var needsAccount = settings.DiscordShowRiotId || settings.DiscordShowLevel || settings.DiscordShowRegion ||
                           settings.DiscordShowRank || settings.DiscordShowCurrency || settings.DiscordShowCollection;
        var account = needsAccount && summoner != null
            ? FindAccount(summoner["gameName"] + "#" + summoner["tagLine"], summoner["puuid"]?.ToString())
            : null;
        var ranked = settings.DiscordShowRank
            ? await GetLeagueAsync("/lol-ranked/v1/current-ranked-stats", cancellationToken).ConfigureAwait(false)
            : null;
        var wallet = settings.DiscordShowCurrency
            ? await GetLeagueAsync("/lol-inventory/v1/wallet?currencyTypes=[%22RP%22,%22lol_blue_essence%22]",
                cancellationToken).ConfigureAwait(false)
            : null;
        var region = settings.DiscordShowRegion
            ? await GetLeagueAsync("/riotclient/region-locale", cancellationToken).ConfigureAwait(false)
            : null;
        var phase = inGame
            ? "InProgress"
            : (await GetLeagueAsync("/lol-gameflow/v1/gameflow-phase", cancellationToken)
                .ConfigureAwait(false))?.ToString().Trim('"') ?? "None";
        var session = phase is "None" or "None."
            ? null
            : await GetLeagueAsync("/lol-gameflow/v1/session", cancellationToken).ConfigureAwait(false);
        if (session == null && phase is "Lobby" or "Matchmaking" or "ReadyCheck")
            session = await GetLeagueAsync("/lol-lobby/v2/lobby", cancellationToken).ConfigureAwait(false);
        var score = settings.DiscordShowScore && phase is "InProgress"
            ? await ReadLeagueScoreAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var kd = settings.DiscordShowKd && phase is "InProgress"
            ? await ReadLeagueKdAsync(summoner?["puuid"]?.ToString(), summoner?["summonerId"]?.ToObject<long?>(),
                cancellationToken).ConfigureAwait(false)
            : null;

        var described = DescribeLeague(phase, session, summoner?["summonerId"]?.ToObject<long?>());
        var activity = inGame && session == null ? LiveLeagueActivity(described) : described;
        var riotId = First(account?.riotID, summoner?["gameName"] + "#" + summoner?["tagLine"]) ?? "League";
        var solo = First(account?.rank, ranked == null ? null : ApiResponseParser.BuildRankString(ranked, "RANKED_SOLO_5x5"));
        var blueEssence = account?.be ?? wallet?["lol_blue_essence"]?.ToObject<int?>();
        var riotPoints = account?.rp ?? wallet?["RP"]?.ToObject<int?>();
        var collection = Join(" · ",
            account?.Champions > 0 ? $"{account.Champions} champs" : null,
            account?.Skins > 0 ? $"{account.Skins} skins" : null);
        return new Presence(ApplicationId, "League of Legends", activity.Image, LeagueImage,
            Preview(activity.Label, activity.Details, score, kd, riotId,
                account?.level ?? summoner?["summonerLevel"]?.ToObject<int?>(),
                First(account?.server, region?["region"]?.ToString()), ShortRank(solo),
                Join(" · ", blueEssence == null ? null : $"{blueEssence:N0} BE",
                    riotPoints == null ? null : $"{riotPoints:N0} RP"), collection, activity.Team,
                activity.Players, activity.Party),
            settings.DiscordShowTracker ? TrackerUrl(riotId, "lol") : null);
    }

    private static async Task<Presence?> ReadValorantAsync(CancellationToken cancellationToken)
    {
        ValorantLoadoutService.ValorantSession session;
        try
        {
            session = await ValorantLoadoutService.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or
                                              IOException or Newtonsoft.Json.JsonException)
        {
            return null;
        }

        var settings = Settings.settingsloaded;
        var needsAccount = settings.DiscordShowRiotId || settings.DiscordShowLevel || settings.DiscordShowRegion ||
                           settings.DiscordShowRank || settings.DiscordShowCurrency || settings.DiscordShowCollection;
        var riotId = await ReadValorantRiotIdAsync(session, cancellationToken).ConfigureAwait(false);
        var account = needsAccount ? FindAccount(riotId, session.Puuid) : null;
        riotId = First(riotId, account?.riotID);
        var mmrBody = settings.DiscordShowRank
            ? await TryValorantAsync(session,
                $"https://pd.{session.Shard}.a.pvp.net/mmr/v1/players/{session.Puuid}/competitiveupdates?startIndex=0&endIndex=1&queue=competitive",
                null, cancellationToken).ConfigureAwait(false)
            : null;
        var xpBody = settings.DiscordShowLevel
            ? await TryValorantAsync(session,
                $"https://pd.{session.Shard}.a.pvp.net/account-xp/v1/players/{session.Puuid}", null, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var walletBody = settings.DiscordShowCurrency
            ? await TryValorantAsync(session,
                $"https://pd.{session.Shard}.a.pvp.net/store/v1/wallet/{session.Puuid}", null, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var balances = ParseJson(walletBody)?["Balances"];
        var vp = balances?["85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741"]?.ToObject<int?>() ?? account?.valorantVp;
        var kingdomCredits = balances?["85ca954a-41f2-ce94-9b45-8ca3dd39a00d"]?.ToObject<int?>() ?? account?.valorantKc;
        var phase = ValorantLoadoutService.ReadGamePhase();
        var party = await ReadValorantPartyAsync(session.Puuid, cancellationToken).ConfigureAwait(false);
        var match = await ReadValorantMatchAsync(session, phase, party, cancellationToken).ConfigureAwait(false);
        var joinUrl = settings.DiscordShowPartyJoin
            ? await ReadValorantJoinUrlAsync(party?.PartyId, party?.QueueId, cancellationToken).ConfigureAwait(false)
            : null;
        return new Presence(ValorantApplicationId, "VALORANT", match.Image, ValorantImage,
            Preview(match.Label, match.Details, match.Score, null, riotId,
                ParseJson(xpBody)?["Progress"]?["Level"]?.ToObject<int?>() ?? account?.valorantLevel,
                settings.DiscordShowRegion ? session.Region : null,
                ShortRank(DescribeValorantRank(mmrBody) ?? account?.valorantRank),
                Join(" · ", vp == null ? null : $"{vp:N0} VP",
                    kingdomCredits == null ? null : $"{kingdomCredits:N0} KC"),
                Join(" · ",
                    account?.ValorantAgentsCount > 0 ? $"{account.ValorantAgentsCount} agents" : null,
                    account?.ValorantSkinsCount > 0 ? $"{account.ValorantSkinsCount} skins" : null),
                match.Team, match.Players, match.Party),
            settings.DiscordShowTracker ? TrackerUrl(riotId, "valorant") : null, joinUrl);
    }

    private static async Task<(string Label, string? Details, string? Score, string Image, string? Team,
        string? Players, string? Party)> ReadValorantMatchAsync(
        ValorantLoadoutService.ValorantSession session, ValorantLoadoutService.GamePhase phase,
        ValorantParty? party, CancellationToken cancellationToken)
    {
        if (phase is ValorantLoadoutService.GamePhase.Menu or ValorantLoadoutService.GamePhase.Unknown)
                {
                    if (party?.Label is "In game" or "Agent select")
                        phase = party.Label == "In game"
                            ? ValorantLoadoutService.GamePhase.InGame
                            : ValorantLoadoutService.GamePhase.AgentSelect;
                    else
                        return party == null
                            ? ("Client open", null, null, ValorantImage, null, null, null)
                            : (party.Label, party.Details, null, ValorantImage, null, null, party.Size);
                }

        var root = phase == ValorantLoadoutService.GamePhase.InGame ? "core-game" : "pregame";
        var playerBody = await TryValorantAsync(session,
            $"https://glz-{session.Region}-1.{session.Shard}.a.pvp.net/{root}/v1/players/{session.Puuid}", null,
            cancellationToken).ConfigureAwait(false);
        var matchId = ParseJson(playerBody)?["MatchID"]?.ToString();
        if (string.IsNullOrWhiteSpace(matchId))
            return (phase == ValorantLoadoutService.GamePhase.InGame ? "In game" : "Agent select",
                        phase == ValorantLoadoutService.GamePhase.InGame
                            ? Join(" · ", party?.Queue, party?.Map) is { Length: > 0 } live ? live : "Live match"
                            : "Selecting an agent",
                        Settings.settingsloaded.DiscordShowScore ? party?.Score : null, ValorantImage, null, null,
                        phase == ValorantLoadoutService.GamePhase.InGame ? null : party?.Size);

        var matchBody = await TryValorantAsync(session,
            $"https://glz-{session.Region}-1.{session.Shard}.a.pvp.net/{root}/v1/matches/{matchId}", null,
            cancellationToken).ConfigureAwait(false);
        var match = ParseJson(matchBody);
        var modePath = match?["ModeID"]?.ToString();
        var mapPath = match?["MapID"]?.ToString();
        var mode = ValorantModeName(modePath) ??
                   await MapNameAsync(modePath?.Split('/').LastOrDefault(), cancellationToken).ConfigureAwait(false);
        var map = ValorantMapName(mapPath) ??
                  await MapNameAsync(mapPath?.Split('/').LastOrDefault(), cancellationToken).ConfigureAwait(false);
        var queue = ValorantQueueName(match?["QueueID"]?.ToString());
        var players = match?["Players"] as JArray ?? new JArray(ExpandTeams(match?["Teams"])
            .SelectMany(team => (team?["Players"] as JArray)?.ToList() ?? new List<JToken>()));
        var me = players?.FirstOrDefault(player =>
            string.Equals(player?["Subject"]?.ToString(), session.Puuid, StringComparison.OrdinalIgnoreCase));
        var agent = await AgentAsync(me?["CharacterID"]?.ToString(), cancellationToken).ConfigureAwait(false);
        var label = phase == ValorantLoadoutService.GamePhase.InGame ? "In game" : "Agent select";
                var image = string.IsNullOrWhiteSpace(agent?.ImageKey) ? ValorantImage : agent.Value.ImageKey;
                var details = Settings.settingsloaded.DiscordShowMatch
                    ? Join(" · ", First(queue, mode, party?.Queue), First(map, party?.Map), agent?.Name)
                    : null;
                var score = Settings.settingsloaded.DiscordShowScore ? party?.Score : null;
                var inMatch = phase == ValorantLoadoutService.GamePhase.InGame;
                return (label, string.IsNullOrWhiteSpace(details) ? null : details, score, image,
                    ValorantTeamName(me?["TeamID"]?.ToString()),
                    inMatch ? PlayerCount(players) : null,
                    inMatch ? null : party?.Size);
    }

    private static (string Label, string? Details, string Image, string? Team, string? Players, string? Party)
        LiveLeagueActivity(
        (string Label, string? Details, string Image, string? Team, string? Players, string? Party) described)
    {
        lock (ConnectionLock)
        {
            if (!LastPresences.TryGetValue(ApplicationId, out var previous))
                return ("In game", null, LeagueImage, described.Team, described.Players, null);

            var lines = previous.Text.Split('\n', 2);
            var details = lines.Length == 2 ? lines[1] : null;
            foreach (var marker in new[] { " · Score ", " · K/D " })
            {
                var index = details?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
                if (index >= 0)
                    details = details![..index];
            }
            return ("In game", string.IsNullOrWhiteSpace(details) || details == lines[0] ? null : details,
                previous.Image, described.Team, described.Players, null);
        }
    }

    private static (string Label, string? Details, string Image, string? Team, string? Players, string? Party)
        DescribeLeague(
        string phase, JToken? session, long? summonerId)
    {
        var game = session?["gameData"] ?? session;
        var queueId = game?["queue"]?["id"]?.ToObject<int?>() ?? game?["gameConfig"]?["queueId"]?.ToObject<int?>();
        var queue = QueueName(queueId) ?? game?["queue"]?["description"]?.ToString() ??
            game?["queue"]?["gameMode"]?.ToString() ?? game?["gameConfig"]?["gameMode"]?.ToString();
        var map = First(MapIdToName(game?["mapId"]?.ToObject<int?>() ?? game?["gameConfig"]?["mapId"]?.ToObject<int?>()),
            game?["mapId"]?.ToString());
        var selections = game?["playerChampionSelections"] as JArray ?? new JArray();
        var teamOne = game?["teamOne"] as JArray ?? new JArray();
        var teamTwo = game?["teamTwo"] as JArray ?? new JArray();
        var player = selections.FirstOrDefault(item =>
                         summonerId != null && item?["summonerId"]?.ToObject<long?>() == summonerId) ??
                     teamOne.Concat(teamTwo).FirstOrDefault(item =>
                         summonerId != null && item?["summonerId"]?.ToObject<long?>() == summonerId);
        var championId = player?["championId"]?.ToObject<int?>() ??
                         (session?["myTeam"] as JArray)?.FirstOrDefault(item =>
                             item?["summonerId"]?.ToObject<long?>() == summonerId)?["championId"]?.ToObject<int?>();
        var champion = championId is > 0 ? ChampionName(championId.Value) : null;
        var members = session?["members"] as JArray ?? game?["members"] as JArray;
        var partySize = members?.Count;
        var partyCapacity = game?["gameConfig"]?["maxLobbySize"]?.ToObject<int?>() ??
                            game?["maxLobbySize"]?.ToObject<int?>();
        var party = partySize is > 0 && partyCapacity is > 0 ? $"{partySize}/{partyCapacity}" : null;
        var teamSize = teamOne.Count;
        var enemySize = teamTwo.Count;
        var players = teamSize > 0 && enemySize > 0 ? $"{teamSize + enemySize} players" : null;
        var team = LeagueTeamName(player, teamOne, teamTwo, session?["myTeam"] as JArray, summonerId);
        var image = championId is > 0 ? ChampionImageKey(championId.Value) : LeagueImage;

        var showMatch = Settings.settingsloaded.DiscordShowMatch;
        return phase switch
        {
            "ChampSelect" => ("Champion select", showMatch ? Join(" · ", queue, map,
                champion ?? "Picking a champion", session?["timer"]?["phase"]?.ToString()) : null, image, team,
                null, party),
            "InProgress" or "PreEndOfGame" or "WaitingForStats" or "EndOfGame" => (
                "In game", showMatch ? Join(" · ", queue, map, champion) : null, image, team, players, null),
            "Lobby" => ("In lobby", showMatch ? Join(" · ", queue, map) : null, LeagueImage, null, null, party),
            "Matchmaking" or "ReadyCheck" or "CheckedIntoTournament" => (
                phase == "ReadyCheck" ? "Ready check" : "In queue",
                showMatch ? Join(" · ", queue, map) : null, LeagueImage, null, null, party),
            _ => ("Client open", null, LeagueImage, null, null, null)
        };
    }

    private static string? LeagueTeamName(JToken? player, JArray teamOne, JArray teamTwo, JArray? myTeam,
        long? summonerId)
    {
        if (summonerId == null)
            return null;

        if (teamOne.Any(item => item?["summonerId"]?.ToObject<long?>() == summonerId))
            return "Blue";
        if (teamTwo.Any(item => item?["summonerId"]?.ToObject<long?>() == summonerId))
            return "Red";

        var teamId = player?["teamId"]?.ToString() ??
                     myTeam?.FirstOrDefault(item => item?["summonerId"]?.ToObject<long?>() == summonerId)?["teamId"]
                         ?.ToString();
        return teamId switch
        {
            "100" => "Blue",
            "200" => "Red",
            _ => null
        };
    }

    internal static string? ValorantModeName(string? modePath)
    {
        if (string.IsNullOrWhiteSpace(modePath))
            return null;
        var path = modePath.ToLowerInvariant();
        return path switch
        {
            _ when path.Contains("swiftplay") => "Swiftplay",
            _ when path.Contains("deathmatch") => "Deathmatch",
            _ when path.Contains("escalation") => "Escalation",
            _ when path.Contains("teamdeathmatch") => "Team Deathmatch",
            _ when path.Contains("spikerush") => "Spike Rush",
            _ when path.Contains("replication") => "Replication",
            _ when path.Contains("snowball") => "Snowball Fight",
            _ when path.Contains("hurm") => "Team Deathmatch",
            _ when path.Contains("bomb") || path.Contains("quickbomb") => "Standard",
            _ => null
        };
    }

    internal static string? ValorantMapName(string? mapPath)
    {
        if (string.IsNullOrWhiteSpace(mapPath))
            return null;
        var key = mapPath.Split('/').Last();
        return key.ToLowerInvariant() switch
        {
            "ascent" or "ascent_art" => "Ascent",
            "bonsai" => "Split",
            "canyon" => "Fracture",
            "duality" => "Bind",
            "foxtrot" => "Breeze",
            "infinity" => "Abyss",
            "jam" => "Lotus",
            "juliett" => "Sunset",
            "pitt" => "Pearl",
            "port" => "Icebox",
            "range" or "poveglia" => "The Range",
            "rook" => "Corrode",
            "triad" => "Haven",
            _ => null
        };
    }

    internal static int? ValorantRound(string? logTail)
    {
        if (string.IsNullOrWhiteSpace(logTail))
            return null;

        var matchStart = Math.Max(
            logTail.LastIndexOf("TravelManager", StringComparison.Ordinal),
            logTail.LastIndexOf("Match Setup: TRUE", StringComparison.Ordinal));
        if (matchStart >= 0)
            logTail = logTail[matchStart..];

        var marker = "OnRoundEnded for round '";
        var index = logTail.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0 || !int.TryParse(
                logTail[(index + marker.Length)..].TakeWhile(char.IsDigit).ToArray(), out var ended))
            return null;

        var endedAt = logTail.LastIndexOf('\n', index);
        var startedAt = logTail.LastIndexOf("Gameplay started", StringComparison.Ordinal);
        return startedAt > endedAt ? ended + 2 : ended + 1;
    }

    internal static string? ValorantQueueName(string? queueId) => queueId?.ToLowerInvariant() switch
    {
        null or "" => null,
        "competitive" => "Competitive",
        "unrated" => "Unrated",
        "swiftplay" => "Swiftplay",
        "spikerush" => "Spike Rush",
        "deathmatch" => "Deathmatch",
        "ggteam" => "Escalation",
        "hurm" => "Team Deathmatch",
        "premier" => "Premier",
        "newmap" => "New Map",
        "custom" or "customgame" => "Custom",
        _ => null
    };

    internal static string? QueueName(int? queueId) => queueId switch
    {
        400 => "Normal Draft",
        420 => "Ranked Solo",
        430 => "Normal Blind",
        440 => "Ranked Flex",
        450 => "ARAM",
        480 => "Swiftplay",
        490 => "Quickplay",
        700 => "Clash",
        900 => "ARURF",
        1020 => "One for All",
        1300 => "Nexus Blitz",
        1400 => "Ultimate Spellbook",
        1700 => "Arena",
        1900 => "URF",
        _ => null
    };

    internal sealed record ValorantParty(string Label, string? Details, string? Size, string? PartyId,
        string? Score = null, string? Queue = null, string? Map = null, string? QueueId = null);

    private static async Task<ValorantParty?> ReadValorantPartyAsync(string? selfPuuid,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await Lcu.Connector("riot", "get", "/chat/v4/presences", "", cancellationToken).ConfigureAwait(false)
                is not HttpResponseMessage response)
                return null;

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ValorantPartyFromPresences(body, selfPuuid);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                              Newtonsoft.Json.JsonException or InvalidOperationException or
                                              FormatException)
        {
            return null;
        }
    }

    internal static ValorantParty? ValorantPartyFromPresences(string? body, string? selfPuuid = null)
    {
        var presences = ParseJson(body)?["presences"] as JArray;
        if (presences == null)
            return null;

        foreach (var presence in presences)
        {
            if (!string.IsNullOrWhiteSpace(selfPuuid) &&
                !string.Equals(presence?["puuid"]?.ToString(), selfPuuid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(presence?["product"]?.ToString(), "valorant", StringComparison.OrdinalIgnoreCase))
                continue;
            var encoded = presence?["private"]?.ToString();
            if (string.IsNullOrWhiteSpace(encoded))
                continue;

            var token = ParseJson(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            if (token?["isValid"]?.ToObject<bool?>() == false)
                continue;

            var queue = ValorantQueueName(token?["queueId"]?.ToString() ??
                                                  token?.SelectToken("matchPresenceData.queueId")?.ToString());
                        var size = PartySize(token?["partySize"]?.ToObject<int?>(), token?["maxPartySize"]?.ToObject<int?>());
                        var state = token?.SelectToken("matchPresenceData.sessionLoopState")?.ToString() ??
                                    token?.SelectToken("partyPresenceData.partyOwnerSessionLoopState")?.ToString();
                        var partyState = token?.SelectToken("partyPresenceData.partyState")?.ToString();
                        var partyId = token?["partyId"]?.ToString();
                        var map = ValorantMapName(token?.SelectToken("partyPresenceData.partyOwnerMatchMap")?.ToString());
                        var score = ValorantTeamScore(token?["partyOwnerMatchScoreAllyTeam"]?.ToObject<int?>(),
                            token?["partyOwnerMatchScoreEnemyTeam"]?.ToObject<int?>());
                        var label = state?.ToUpperInvariant() switch
                        {
                            "INGAME" => "In game",
                            "PREGAME" => "Agent select",
                            _ when partyState?.Equals("MATCHMAKING", StringComparison.OrdinalIgnoreCase) == true => "In queue",
                            _ => "In lobby"
                        };
                        return new ValorantParty(label, Settings.settingsloaded.DiscordShowMatch
                            ? Join(" · ", queue, label == "In lobby" || label == "In queue" ? null : map)
                            : null, size, Guid.TryParse(partyId, out _) ? partyId : null, score, queue, map,
                            token?["queueId"]?.ToString());
        }

        return null;
    }

    internal static string? PartyJoinUrl(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z0-9]{4,8}(?:-[A-Za-z0-9]{4}){0,2}$"))
            return null;
        return "https://gg.riotgames.com/VAL?joinCode=" + Uri.EscapeDataString(code);
    }

    private static async Task<string?> ReadValorantJoinUrlAsync(string? partyId, string? queueId,
        CancellationToken cancellationToken)
    {
        string? activitiesBody = null;
        try
        {
            if (await Lcu.Connector("riot", "get", "/activity-gateway/v1/activities", "", cancellationToken)
                    .ConfigureAwait(false) is not HttpResponseMessage response)
            {
                DebugConsole.WriteLine("[Discord] Valorant activities request was unavailable (Riot Client not reachable).");
                return lastValorantJoinUrl;
            }

            using (response)
            {
                activitiesBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    DebugConsole.WriteLine(
                        $"[Discord] Valorant activities request returned {(int)response.StatusCode}.");
                var joinUrl = PartyJoinUrlFromActivities(activitiesBody, partyId, queueId);
                if (joinUrl != null)
                {
                    // The activity list can keep serving join codes that Riot already invalidated,
                    // so ask the smart-url service (the endpoint behind gg.riotgames.com) whether
                    // the link still resolves before publishing it as the Discord join button.
                    if (await ValidateJoinUrlViaResolveAsync(joinUrl, cancellationToken).ConfigureAwait(false) !=
                        JoinCodeResolveResult.NotFound)
                    {
                        if (!string.Equals(lastValorantJoinUrl, joinUrl, StringComparison.Ordinal))
                            DebugConsole.WriteLine("[Discord] Added the Valorant party link.");
                        lastValorantJoinUrl = joinUrl;
                        return joinUrl;
                    }

                    DebugConsole.WriteLine("[Discord] The Valorant party link no longer resolves; replacing it.");
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                              Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            DebugConsole.WriteLine($"[Discord] Valorant party link request failed: {exception.Message}");
            return lastValorantJoinUrl;
        }

        if (lastValorantJoinUrl != null)
            DebugConsole.WriteLine("[Discord] The Valorant party link is no longer active; creating a new one.");

        var createdJoinUrl = await CreateValorantJoinUrlAsync(activitiesBody, partyId, queueId, cancellationToken)
            .ConfigureAwait(false);
        if (createdJoinUrl == null)
        {
            lastValorantJoinUrl = null;
            return null;
        }

        lastValorantJoinUrl = createdJoinUrl;
        return createdJoinUrl;
    }

    internal enum JoinCodeResolveResult
    {
        /// <summary>The resolve service confirmed the join code is usable.</summary>
        Valid,

        /// <summary>The resolve service answered and reported the code as gone (404 CODE_NOT_FOUND).</summary>
        NotFound,

        /// <summary>The resolve service could not be reached, so the code's status is unknown.</summary>
        Unknown
    }

    /// <summary>
    ///     Asks Riot's smart-url resolve service whether the party link still works. The service
    ///     is global, so either shard can validate a code from any region; a 404 means the code
    ///     is dead even when the activity list still shows it as active.
    /// </summary>
    private static async Task<JoinCodeResolveResult> ValidateJoinUrlViaResolveAsync(string joinUrl,
        CancellationToken cancellationToken)
    {
        var payload = new JObject { ["url"] = joinUrl }.ToString(Newtonsoft.Json.Formatting.None);
        foreach (var host in ResolveHosts)
        {
            var result = await TryResolveAsync($"https://{host}/smart-url/v1/resolve", payload, cancellationToken)
                .ConfigureAwait(false);
            if (result != JoinCodeResolveResult.Unknown)
                return result;
        }

        return JoinCodeResolveResult.Unknown;
    }

    private static async Task<JoinCodeResolveResult> TryResolveAsync(string url, string payload,
        CancellationToken cancellationToken)
    {
        var record = LcuRequestLog.Add("riot", "POST", url, payload, null, "Pending", string.Empty, 0,
            trafficType: "Discord", direction: "Outgoing");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await ResolveClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var resolved = ParseJson(body)?["data"]?["joinCode"]?.ToString();
                LcuRequestLog.Update(record.Id, (int)response.StatusCode, "OK", body, stopwatch.ElapsedMilliseconds);
                return string.IsNullOrWhiteSpace(resolved) ? JoinCodeResolveResult.Unknown : JoinCodeResolveResult.Valid;
            }

            LcuRequestLog.Update(record.Id, (int)response.StatusCode, response.ReasonPhrase ?? "Error", body,
                stopwatch.ElapsedMilliseconds);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
                return JoinCodeResolveResult.NotFound;
            return JoinCodeResolveResult.Unknown;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            LcuRequestLog.Update(record.Id, null, "Failed", string.Empty, stopwatch.ElapsedMilliseconds,
                exception.Message);
            return JoinCodeResolveResult.Unknown;
        }
    }

    internal static string? PartyJoinUrlFromActivities(string? body, string? partyId = null, string? queueId = null)
    {
        var hasSelectors = !string.IsNullOrWhiteSpace(partyId) || !string.IsNullOrWhiteSpace(queueId);
        foreach (var (_, activity) in ValorantActivities(body))
        {
            if (activity == null || (hasSelectors && !ActivityMatchesParty(activity, partyId, queueId)))
                continue;

            var joinUrl = ValidPartyJoinUrl(activity["joinCode"]);
            if (joinUrl != null)
                return joinUrl;
        }

        return null;
    }

    internal static bool HasStaleValorantJoinCode(string? body, string? partyId = null, string? queueId = null)
    {
        var hasSelectors = !string.IsNullOrWhiteSpace(partyId) || !string.IsNullOrWhiteSpace(queueId);
        return ValorantActivities(body).Any(entry =>
            entry.Activity != null &&
            (!hasSelectors || ActivityMatchesParty(entry.Activity, partyId, queueId)) &&
            entry.Activity["joinCode"]?["joinCode"]?.ToString() is { Length: > 0 });
    }

    /// <summary>
    ///     The activity gateway can list several parties at once (stale lobbies linger), so the
    ///     current lobby is matched by its party id, or by game mode when no party id is known.
    /// </summary>
    internal static bool ActivityMatchesParty(JToken? activity, string? partyId, string? queueId)
    {
        var id = activity?["id"]?.ToString();
        if (!string.IsNullOrWhiteSpace(partyId) &&
            string.Equals(id, partyId, StringComparison.OrdinalIgnoreCase))
            return true;

        var gameMode = activity?["gameMode"]?.ToString();
        return !string.IsNullOrWhiteSpace(queueId) &&
               string.Equals(gameMode, "social_mode_" + queueId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Key, JToken? Activity)> ValorantActivities(string? body)
    {
        var activities = ParseJson(body)?["activities"] as JObject;
        foreach (var activity in activities?.Properties() ?? Enumerable.Empty<JProperty>())
        {
            if (string.Equals(activity.Value?["product"]?.ToString(), "valorant", StringComparison.OrdinalIgnoreCase))
                yield return (activity.Name, activity.Value);
        }
    }

    internal static string? ValidPartyJoinUrl(JToken? joinCode)
    {
        if (joinCode is not JObject code)
            return null;

        var isActive = code["isActive"]?.ToObject<bool?>();
        if (isActive == false)
            return null;

        var expiresAt = code["expiresAt"]?.ToObject<long?>();
        if (expiresAt != null)
        {
            if (expiresAt == 0)
            {
                // Riot lists dead codes with expiresAt 0, so only an explicit isActive can save it.
                if (isActive != true)
                    return null;
            }
            else
            {
                var expiryMs = expiresAt >= 1_000_000_000_000L ? expiresAt.Value : expiresAt.Value * 1000;
                if (expiryMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    return null;
            }
        }

        var smartUrl = code["smartUrl"]?.ToString();
        if (Uri.TryCreate(smartUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            return smartUrl;

        return PartyJoinUrl(code["joinCode"]?.ToString());
    }

    internal static string? ValidPartyJoinUrlFromCreateResponse(string? body)
    {
        var root = ParseJson(body);
        if (root == null)
            return null;

        if (root["joinCode"] is JObject nested)
            return ValidPartyJoinUrl(nested);

        var wrapper = new JObject
        {
            ["joinCode"] = root["joinCode"],
            ["isActive"] = root["isActive"],
            ["expiresAt"] = root["expiresAt"],
            ["smartUrl"] = root["smartUrl"]
        };
        return ValidPartyJoinUrl(wrapper);
    }

    private static async Task<string?> CreateValorantJoinUrlAsync(string? activitiesBody, string? partyId,
        string? queueId, CancellationToken cancellationToken)
    {
        var activityId = ValorantActivityId(activitiesBody, partyId, queueId);
        if (string.IsNullOrWhiteSpace(activityId))
            return null;

        if (HasStaleValorantJoinCode(activitiesBody, partyId, queueId))
        {
            DebugConsole.WriteLine("[Discord] Revoking the expired Valorant join code.");
            await RevokeValorantJoinCodeAsync(activityId, cancellationToken).ConfigureAwait(false);
        }

        var joinUrl = await PostValorantJoinCodeAsync(activityId, cancellationToken).ConfigureAwait(false);
        if (joinUrl != null &&
            await ValidateJoinUrlViaResolveAsync(joinUrl, cancellationToken).ConfigureAwait(false) !=
            JoinCodeResolveResult.NotFound)
            return joinUrl;

        DebugConsole.WriteLine("[Discord] Retrying Valorant join code creation after revoking.");
        await RevokeValorantJoinCodeAsync(activityId, cancellationToken).ConfigureAwait(false);
        joinUrl = await PostValorantJoinCodeAsync(activityId, cancellationToken).ConfigureAwait(false);
        return joinUrl != null &&
               await ValidateJoinUrlViaResolveAsync(joinUrl, cancellationToken).ConfigureAwait(false) !=
               JoinCodeResolveResult.NotFound
            ? joinUrl
            : null;
    }

    private static async Task RevokeValorantJoinCodeAsync(string activityId, CancellationToken cancellationToken)
    {
        try
        {
            if (await Lcu.Connector("riot", "delete",
                    $"/activity-gateway/v1/activities/{Uri.EscapeDataString(activityId)}/join-code/shared", "",
                    cancellationToken).ConfigureAwait(false) is HttpResponseMessage response)
            {
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                        DebugConsole.WriteLine(
                            $"[Discord] Valorant join code revoke returned {(int)response.StatusCode}.");
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            DebugConsole.WriteLine($"[Discord] Valorant join code revoke failed: {exception.Message}");
        }
    }

    private static async Task<string?> PostValorantJoinCodeAsync(string activityId,
        CancellationToken cancellationToken)
    {
        if (await Lcu.Connector("riot", "post",
                $"/activity-gateway/v1/activities/{Uri.EscapeDataString(activityId)}/join-code/shared", "{}",
                cancellationToken).ConfigureAwait(false) is not HttpResponseMessage response)
        {
            DebugConsole.WriteLine("[Discord] Valorant join code create request was unavailable (Riot Client not reachable).");
            return null;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                DebugConsole.WriteLine(
                    $"[Discord] Valorant join code create returned {(int)response.StatusCode}.");
            var joinUrl = ValidPartyJoinUrlFromCreateResponse(body);
            if (joinUrl == null)
                DebugConsole.WriteLine("[Discord] Valorant join code create response had no active joinCode.");
            return joinUrl;
        }
    }

    internal static string? ValorantActivityId(string? body, string? partyId = null, string? queueId = null)
    {
        var hasSelectors = !string.IsNullOrWhiteSpace(partyId) || !string.IsNullOrWhiteSpace(queueId);
        var match = ValorantActivities(body).FirstOrDefault(entry =>
            !hasSelectors || ActivityMatchesParty(entry.Activity, partyId, queueId));
        return match.Activity == null
            ? null
            : First(match.Activity.SelectToken("activityId")?.ToString(), match.Key);
    }

    internal static string? PartySize(int? current, int? capacity) =>
        current is > 0 && capacity is > 0 ? $"{current}/{capacity}" : null;

        internal static string? ValorantTeamScore(int? ally, int? enemy) =>
                ally is >= 0 && enemy is >= 0 ? $"{ally}-{enemy}" : null;

    internal static string? LeagueKd(JArray? players, string? puuid, long? summonerId)
    {
        var me = players?.FirstOrDefault(player =>
                     !string.IsNullOrWhiteSpace(puuid) &&
                     string.Equals(player?["puuid"]?.ToString(), puuid, StringComparison.OrdinalIgnoreCase)) ??
                 players?.FirstOrDefault(player =>
                     summonerId != null && player?["summonerId"]?.ToObject<long?>() == summonerId) ??
                 players?.FirstOrDefault(player =>
                     !string.IsNullOrWhiteSpace(puuid) &&
                     string.Equals(player?["riotId"]?.ToString(), puuid, StringComparison.OrdinalIgnoreCase));
        var scores = me?["scores"];
        var kills = scores?["kills"]?.ToObject<int?>();
        var deaths = scores?["deaths"]?.ToObject<int?>();
        return kills == null || deaths == null ? null : $"{kills}/{deaths}";
    }

    private static async Task<string?> ReadLeagueKdAsync(string? puuid, long? summonerId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
            var body = await LoggedGetStringAsync(client, "live",
                "https://127.0.0.1:2999/liveclientdata/playerlist", "HTTP", cancellationToken).ConfigureAwait(false);
            return body == null ? null : LeagueKd(JArray.Parse(body), puuid, summonerId);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or
                                              Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Sends a GET through the given client and records it in the LCU traffic view, so the
    ///     Discord presence requests that bypass <c>Lcu.Connector</c> still show up there.
    /// </summary>
    private static async Task<string?> LoggedGetStringAsync(HttpClient client, string target, string url,
        string trafficType, CancellationToken cancellationToken)
    {
        var record = LcuRequestLog.Add(target, "GET", url, string.Empty, null, "Pending", string.Empty, 0,
            trafficType: trafficType, direction: "Outgoing");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            LcuRequestLog.Update(record.Id, 200, "OK", body, stopwatch.ElapsedMilliseconds);
            return body;
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            LcuRequestLog.Update(record.Id, null, "Failed", string.Empty, stopwatch.ElapsedMilliseconds,
                exception.Message);
            return null;
        }
    }

    private static async Task<string?> ReadLeagueScoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
            var body = await LoggedGetStringAsync(client, "live",
                "https://127.0.0.1:2999/liveclientdata/gamestats", "HTTP", cancellationToken).ConfigureAwait(false);
            var stats = ParseJson(body);
            var ally = stats?["allyTeamScore"]?.ToObject<int?>() ?? stats?["teams"]?["order"]?["score"]?.ToObject<int?>();
            var enemy = stats?["enemyTeamScore"]?.ToObject<int?>() ?? stats?["teams"]?["chaos"]?["score"]?.ToObject<int?>();
            return ally == null || enemy == null ? null : $"{ally}-{enemy}";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or
                                              Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<JToken?> GetLeagueAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            if (await Lcu.Connector("league", "get", endpoint, "", cancellationToken).ConfigureAwait(false)
                is not HttpResponseMessage response)
                return null;

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(body) ? null : JToken.Parse(body);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                              Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string?> TryValorantAsync(ValorantLoadoutService.ValorantSession session, string url,
        string? body, CancellationToken cancellationToken)
    {
        try
        {
            return await ValorantLoadoutService.GetRawAsync(session, url, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                              Newtonsoft.Json.JsonException or InvalidOperationException)
        {
                if (url.Contains("/invitecode", StringComparison.OrdinalIgnoreCase))
                    DebugConsole.WriteLine($"[Discord] Valorant party invite request failed: {exception.Message}");
                return null;
            }
        }

    private static Utils.AccountList? FindAccount(string? riotId, string? puuid)
    {
        var accounts = views.Accounts.ActualAccountlists;
        return accounts.LastOrDefault(account =>
                   !string.IsNullOrWhiteSpace(riotId) &&
                   string.Equals(account.riotID, riotId, StringComparison.OrdinalIgnoreCase)) ??
               accounts.LastOrDefault(account =>
                   !string.IsNullOrWhiteSpace(puuid) &&
                   string.Equals(account.username, puuid, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadValorantRiotIdAsync(ValorantLoadoutService.ValorantSession session,
        CancellationToken cancellationToken)
    {
        var body = await TryValorantAsync(session,
            $"https://pd.{session.Shard}.a.pvp.net/name-service/v2/players",
            new JArray(session.Puuid).ToString(Newtonsoft.Json.Formatting.None), cancellationToken)
            .ConfigureAwait(false);
        var riotId = RiotIdFromNameService(body);
        if (riotId != null)
            return riotId;

        if (await Lcu.Connector("riot", "get", "/chat/v1/session", "", cancellationToken).ConfigureAwait(false)
            is not HttpResponseMessage response)
            return null;

        using (response)
        {
            var sessionBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? RiotIdFromChatSession(sessionBody) : null;
        }
    }

    internal static string? RiotIdFromNameService(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var player = JArray.Parse(body).FirstOrDefault();
            return RiotId(player?["GameName"]?.ToString() ?? player?["gameName"]?.ToString(),
                player?["TagLine"]?.ToString() ?? player?["tagLine"]?.ToString());
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    internal static string? RiotIdFromChatSession(string? body)
    {
        var session = ParseJson(body);
        return RiotId(session?["game_name"]?.ToString(), session?["game_tag"]?.ToString());
    }

    private static string? RiotId(string? name, string? tag) =>
        string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tag) ? null : $"{name.Trim()}#{tag.Trim()}";

    internal static string? DescribeValorantRank(string? body)
    {
        var root = ParseJson(body);
        var update = root?["LatestCompetitiveUpdate"] ??
                     (root?["Matches"] as JArray)?.FirstOrDefault();
        var tier = update?["TierAfterUpdate"]?.ToObject<int?>() ?? 0;
        var rating = update?["RankedRatingAfterUpdate"]?.ToObject<int?>() ?? 0;
        if (tier <= 0)
            return null;
        var name = CompetitiveTier(tier);
        return rating > 0 ? $"{name} {rating} RR" : name;
    }

    private static string CompetitiveTier(int tier) => tier switch
    {
        >= 3 and <= 5 => $"Iron {tier - 2}",
        >= 6 and <= 8 => $"Bronze {tier - 5}",
        >= 9 and <= 11 => $"Silver {tier - 8}",
        >= 12 and <= 14 => $"Gold {tier - 11}",
        >= 15 and <= 17 => $"Platinum {tier - 14}",
        >= 18 and <= 20 => $"Diamond {tier - 17}",
        >= 21 and <= 23 => $"Ascendant {tier - 20}",
        >= 24 and <= 26 => $"Immortal {tier - 23}",
        27 => "Radiant",
        _ => "Unranked"
    };

    private static string ChampionName(int championId)
    {
        if (championNames != null && championNames.TryGetValue(championId, out var known))
            return known;

        _ = LoadChampionsAsync();
        return $"Champion {championId}";
    }

    private static string ChampionImageKey(int championId) => $"champion-{championId}";

    private static async Task LoadChampionsAsync(CancellationToken cancellationToken = default)
    {
        if (championNames != null || !await CatalogLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            var body = await LoggedGetStringAsync(CatalogClient, "cdn",
                "https://ddragon.leagueoflegends.com/cdn/15.1.1/data/en_US/champion.json", "CDN", cancellationToken)
                .ConfigureAwait(false);
            if (body == null)
                return; // Leaves championNames null so the next tick retries the catalog.

            championNames = (ParseJson(body)?["data"] as JObject)?.Properties()
                .Select(property => property.Value)
                .Where(champion => champion["key"]?.ToObject<int?>() != null)
                .ToDictionary(champion => champion["key"]!.ToObject<int>(), champion => champion["name"]?.ToString() ??
                    "Champion") ?? new Dictionary<int, string>();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                              Newtonsoft.Json.JsonException)
        {
            championNames = null;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    private static async Task<(string Name, string ImageKey)?> AgentAsync(string? agentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return null;
        if (agents == null)
        {
            try
            {
                var body = await LoggedGetStringAsync(CatalogClient, "cdn",
                    "https://valorant-api.com/v1/agents?isPlayableCharacter=true", "CDN", cancellationToken)
                    .ConfigureAwait(false);
                agents = (ParseJson(body)?["data"] as JArray ?? [])
                    .Select(item => (Id: item["uuid"]?.ToString(), Name: item["displayName"]?.ToString()))
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                    .ToDictionary(item => item.Id!, item => (item.Name!, AgentImageKey(item.Name!)),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                                  Newtonsoft.Json.JsonException)
            {
                agents = new Dictionary<string, (string Name, string ImageKey)>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return agents.TryGetValue(agentId, out var agent) ? agent : null;
    }

    internal static string AgentImageKey(string agentName) =>
        new string(agentName.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character)
            ? character
            : '-').ToArray()).Trim('-');

    private static async Task<string?> MapNameAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        mapNames ??= await LoadMapAsync("https://valorant-api.com/v1/maps", "mapUrl", "displayName", cancellationToken)
            .ConfigureAwait(false);
        return mapNames.TryGetValue(path, out var name) || mapNames.TryGetValue(path.Split('/').Last(), out name)
            ? name
            : path.Split('/').Last();
    }

    private static async Task<Dictionary<string, string>> LoadMapAsync(string url, string idField, string nameField,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await LoggedGetStringAsync(CatalogClient, "cdn", url, "CDN", cancellationToken)
                .ConfigureAwait(false);
            return (ParseJson(body)?["data"] as JArray ?? [])
                .Select(item => (Id: item[idField]?.ToString(), Name: item[nameField]?.ToString()))
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id!.Split('/').Last(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name!, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                              Newtonsoft.Json.JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? PlayerCount(JArray? players) =>
        players == null || players.Count == 0 ? null : $"{players.Count} players";

    internal static string? ValorantTeamName(string? teamId) => teamId?.ToLowerInvariant() switch
    {
        null or "" => null,
        "blue" => "Blue",
        "red" => "Red",
        _ => null
    };

    private static IEnumerable<JToken> ExpandTeams(JToken? teams) =>
        teams is JArray array ? array : teams == null ? [] : [teams];

    private static string? MapIdToName(int? mapId) => mapId switch
    {
        11 => "Summoner's Rift",
        12 => "Howling Abyss",
        21 => "Nexus Blitz",
        22 => "Teamfight Tactics",
        30 => "Arena",
        _ => null
    };

    internal static string? TrackerUrl(string? riotId, string game)
    {
        if (string.IsNullOrWhiteSpace(riotId))
            return null;

        var parts = riotId.Split('#', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return null;

        var name = Uri.EscapeDataString(parts[0].Trim());
        var tag = Uri.EscapeDataString(parts[1].Trim());
        return game == "lol"
            ? $"https://tracker.gg/lol/profile/riot/{name}%23{tag}"
            : $"https://tracker.gg/valorant/profile/riot/{name}%23{tag}";
    }

    private static void PublishAll(IReadOnlyList<Presence> presences)
    {
        var active = presences.Select(presence => presence.ApplicationId).ToHashSet(StringComparer.Ordinal);
        lock (ConnectionLock)
        {
            foreach (var applicationId in Connections.Keys.Where(id => !active.Contains(id)).ToArray())
                ClearApplication(applicationId);
        }

        foreach (var presence in presences)
            Publish(presence);
    }

    private static void Publish(Presence presence)
    {
        var payload = presence.ToPayload();
        var serialized = payload.ToString(Newtonsoft.Json.Formatting.None);
        lock (ConnectionLock)
        {
            if (Published.TryGetValue(presence.ApplicationId, out var previous) && previous == serialized &&
                ConnectedCount(presence.ApplicationId) == OpenDiscordPipeNames().Count)
                return;
        }

        Send(payload, presence.ApplicationId);
        lock (ConnectionLock)
            Published[presence.ApplicationId] = serialized;
        var buttons = payload["args"]?["activity"]?["buttons"] as JArray;
        var labels = buttons == null || buttons.Count == 0
            ? "no buttons"
            : string.Join(", ", buttons.Select(button => button["label"]?.ToString()));
        DebugConsole.WriteLine($"[Discord] Updated {presence.Name} presence ({labels}).");
        LcuRequestLog.Add("discord", "SET_ACTIVITY", "discord-ipc-" + presence.ApplicationId, serialized, null,
            "Sent", string.Empty, 0, trafficType: "Discord", direction: "Outgoing");
    }

    private static void ClearApplication(string applicationId)
    {
        if (Connections.TryGetValue(applicationId, out var pipes))
        {
            foreach (var client in pipes.ToArray())
            {
                try
                {
                    if (client.Pipe.IsConnected)
                        WriteActivity(client.Pipe, new JObject
                        {
                            ["cmd"] = "SET_ACTIVITY",
                            ["args"] = new JObject { ["pid"] = Environment.ProcessId }
                        });
                }
                catch (Exception exception) when (exception is IOException or TimeoutException or
                                                      UnauthorizedAccessException or InvalidOperationException)
                {
                    DebugConsole.WriteLine($"[Discord] Presence could not be cleared: {exception.Message}");
                }
            }
        }

        CloseConnection(applicationId);
    }

    private static void Send(JObject payload, string applicationId)
    {
        lock (ConnectionLock)
        {
            var sent = 0;
            foreach (var client in EnsureConnected(applicationId))
            {
                try
                {
                    WriteActivity(client.Pipe, (JObject)payload.DeepClone());
                    sent++;
                }
                catch (Exception exception) when (exception is IOException or TimeoutException or
                                                      UnauthorizedAccessException or InvalidOperationException)
                {
                    DebugConsole.WriteLine(
                        $"[Discord] {applicationId} update failed on {client.PipeName}: {exception.Message}");
                    client.Pipe.Dispose();
                }
            }

            if (Connections.TryGetValue(applicationId, out var clients))
                clients.RemoveAll(client => !client.Pipe.IsConnected);
            if (sent == 0)
                throw new IOException("Discord is not running.");
        }
    }

    private static void WriteActivity(NamedPipeClientStream pipe, JObject payload)
    {
        var command = payload["cmd"]?.ToString() == "SET_ACTIVITY"
            ? payload
            : new JObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = payload["args"] ?? new JObject { ["pid"] = Environment.ProcessId }
            };
        command["nonce"] = Guid.NewGuid().ToString("N");
        Write(pipe, 1, command);
        var response = Read(pipe);
        if (IsError(response))
            throw new InvalidOperationException(ErrorMessage(response));
    }

    private static IReadOnlyList<DiscordClient> EnsureConnected(string applicationId)
    {
        var open = OpenDiscordPipeNames();
        if (!Connections.TryGetValue(applicationId, out var clients))
        {
            clients = [];
            Connections[applicationId] = clients;
        }

        foreach (var client in clients.Where(client => !client.Pipe.IsConnected || !open.Contains(client.PipeName))
                     .ToArray())
        {
            clients.Remove(client);
            client.Pipe.Dispose();
        }

        Exception? lastError = null;
        foreach (var name in open.Where(name => clients.All(client =>
                     !string.Equals(client.PipeName, name, StringComparison.OrdinalIgnoreCase))))
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(750);
                Write(pipe, 0, new JObject { ["v"] = 1, ["client_id"] = applicationId });
                var handshake = Read(pipe);
                if (IsError(handshake))
                    throw new InvalidOperationException(ErrorMessage(handshake));

                clients.Add(new DiscordClient(name, pipe));
                DebugConsole.WriteLine($"[Discord] Connected {applicationId} on {name}.");
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or
                                                  UnauthorizedAccessException or InvalidOperationException)
            {
                lastError = exception;
                pipe.Dispose();
            }
        }

        if (clients.Count == 0)
            throw lastError ?? new IOException("Discord is not running.");
        return clients;
    }

    private static int ConnectedCount(string applicationId) =>
        Connections.TryGetValue(applicationId, out var clients)
            ? clients.Count(client => client.Pipe.IsConnected)
            : 0;

    /// <summary>
    ///     Every running Discord client owns one of discord-ipc-0 through discord-ipc-9. A user can
    ///     have several clients open, and each one only shows presence sent on its own pipe.
    /// </summary>
    private static HashSet<string> OpenDiscordPipeNames() =>
        DiscordPipeNames(EnumeratePipeNames());

    /// <summary>
    ///     The pipe root only exists to <see cref="Directory" /> when the path ends with a separator.
    ///     <c>\\.\pipe</c> reports as missing, which used to skip every running Discord client.
    /// </summary>
    private static IEnumerable<string> EnumeratePipeNames()
    {
        const string pipeRoot = @"\\.\pipe\";
        if (!Directory.Exists(pipeRoot))
            return [];

        try
        {
            return Directory.EnumerateFiles(pipeRoot).Select(Path.GetFileName)!;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static HashSet<string> DiscordPipeNames(IEnumerable<string?> names)
    {
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in names)
        {
            var name = string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFileName(candidate);
            if (string.IsNullOrWhiteSpace(name) ||
                !name.StartsWith("discord-ipc-", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(name["discord-ipc-".Length..], out var index) ||
                index is < 0 or >= DiscordClientSlots)
                continue;

            open.Add(name);
        }

        return open;
    }

    private static void CloseConnection(string applicationId)
    {
        if (!Connections.Remove(applicationId, out var clients))
            return;

        foreach (var client in clients)
            client.Pipe.Dispose();
        Published.Remove(applicationId);
    }

    private sealed record DiscordClient(string PipeName, NamedPipeClientStream Pipe);

    private static bool IsError(string response)
    {
        var payload = ParseJson(response);
        return payload?["evt"]?.ToString() == "ERROR" || payload?["code"] != null;
    }

    private static string ErrorMessage(string response)
    {
        var payload = ParseJson(response);
        return payload?["data"]?["message"]?.ToString() ?? payload?["message"]?.ToString() ??
            "Discord rejected the presence update.";
    }

    private static void Write(Stream stream, int operation, JObject payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
        var header = new byte[8];
        BitConverter.GetBytes(operation).CopyTo(header, 0);
        BitConverter.GetBytes(body.Length).CopyTo(header, 4);
        stream.Write(header);
        stream.Write(body);
    }

    private static string Read(Stream stream)
    {
        var header = new byte[8];
        stream.ReadExactly(header);
        var length = BitConverter.ToInt32(header, 4);
        var body = new byte[Math.Clamp(length, 0, 1024 * 1024)];
        stream.ReadExactly(body);
        return Encoding.UTF8.GetString(body);
    }

    internal static string Preview(string label, string? match, string? score, string? kd, string? riotId,
        int? level, string? region, string? rank, string? currency, string? collection, string? team = null,
        string? players = null, string? party = null)
    {
        var settings = Settings.settingsloaded;
        var line = Join(" · ",
            settings.DiscordShowMatch ? match : null,
            settings.DiscordShowTeam ? team : null,
            settings.DiscordShowPartySize ? party : null,
            settings.DiscordShowPlayers ? players : null,
            settings.DiscordShowScore && !string.IsNullOrWhiteSpace(score) ? $"Score {score}" : null,
            settings.DiscordShowKd && !string.IsNullOrWhiteSpace(kd) ? $"K/D {kd}" : null,
            settings.DiscordShowRiotId ? riotId : null,
            settings.DiscordShowLevel && level != null ? $"Lv {level}" : null,
            settings.DiscordShowRegion && !string.IsNullOrWhiteSpace(region) ? region.ToUpperInvariant() : null,
            settings.DiscordShowRank ? rank : null,
            settings.DiscordShowCurrency ? currency : null,
            settings.DiscordShowCollection ? collection : null);
        return Fit(label, DetailsLimit) + "\n" + Fit(string.IsNullOrWhiteSpace(line) ? label : line, StateLimit);
    }

    internal static string Fit(string? value, int limit)
    {
        var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= limit ? text : text[..(limit - 1)].TrimEnd() + "…";
    }

    internal static string Join(string separator, params string?[] values) =>
        string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ShortRank(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank))
            return null;
        var lp = rank.IndexOf(" LP", StringComparison.OrdinalIgnoreCase);
        return lp > 0 ? rank[..lp] : rank;
    }

    private static JObject? ParseJson(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JObject.Parse(body);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    internal sealed record Presence(string ApplicationId, string Name, string Image, string? SmallImage, string Text,
        string? AccountUrl, string? JoinUrl = null)
    {
        public JObject ToPayload()
        {
            var lines = Text.Split('\n', 2);
            var assets = new JObject
            {
                ["large_image"] = Image,
                ["large_text"] = Fit(Name, ButtonLabelLimit)
            };
            if (!string.IsNullOrWhiteSpace(SmallImage))
            {
                assets["small_image"] = SmallImage;
                assets["small_text"] = Fit(Name, ButtonLabelLimit);
            }

            var activity = new JObject
            {
                ["type"] = Settings.settingsloaded.DiscordShowAlongsideGame ? 3 : 0,
                ["name"] = Fit(Name, DetailsLimit),
                ["details"] = Fit(lines[0], DetailsLimit),
                ["state"] = Fit(lines.Length == 2 ? lines[1] : Name, StateLimit),
                ["timestamps"] = new JObject { ["start"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                ["assets"] = assets,
                ["instance"] = false
            };
            var settings = Settings.settingsloaded;
            var buttons = new JArray();
            if (settings.DiscordShowPartyJoin &&
                Uri.TryCreate(JoinUrl, UriKind.Absolute, out var joinUrl) && joinUrl.Scheme == Uri.UriSchemeHttps)
                buttons.Add(new JObject { ["label"] = "Join party", ["url"] = joinUrl.ToString() });
            if (settings.DiscordShowTracker &&
                Uri.TryCreate(AccountUrl, UriKind.Absolute, out var accountUrl) &&
                accountUrl.Scheme == Uri.UriSchemeHttps)
                buttons.Add(new JObject { ["label"] = "Tracker", ["url"] = accountUrl.ToString() });
            if (settings.DiscordShowGitHub && buttons.Count < 2)
                buttons.Add(new JObject
                {
                    ["label"] = "GitHub",
                    ["url"] = "https://github.com/Ja-Sa-La/League-Account-Manager"
                });
            if (buttons.Count > 0)
                activity["buttons"] = new JArray(buttons.Take(2));

            return new JObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JObject
                {
                    ["pid"] = Environment.ProcessId,
                    ["activity"] = activity
                }
            };
        }
    }
}

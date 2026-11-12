using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Misc;

internal static class ApiResponseParser
{
    public static JObject? ParseSummoner(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            var summoner = JObject.Parse(content);
            return summoner["summonerId"] != null && summoner["puuid"] != null ? summoner : null;
        }
        catch
        {
            return null;
        }
    }

    public static Utils.Wallet? ParseWallet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            var wallet = JObject.Parse(content);
            return new Utils.Wallet
            {
                be = wallet["lol_blue_essence"]?.ToObject<int>() ?? 0,
                rp = wallet["RP"]?.ToObject<int>() ?? 0
            };
        }
        catch
        {
            return null;
        }
    }

    public static JToken? ParseRankedStats(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            var rankedStats = JToken.Parse(content);
            return rankedStats["queueMap"] != null ? rankedStats : null;
        }
        catch
        {
            return null;
        }
    }

    public static string BuildRankString(JToken? rankedStats, string queueName)
    {
        var queue = rankedStats?["queueMap"]?[queueName];
        var tier = queue?["tier"]?.ToString();
        if (string.IsNullOrWhiteSpace(tier))
            return "Unranked";

        var division = queue?["division"]?.ToString();
        var leaguePoints = queue?["leaguePoints"]?.ToString() ?? "0";
        var wins = queue?["wins"]?.ToString() ?? "0";
        var losses = queue?["losses"]?.ToString() ?? "0";

        if (tier is "MASTER" or "GRANDMASTER" or "CHALLENGER")
            return $"{tier} {leaguePoints} LP, {wins} Wins, {losses} Losses";

        return $"{tier} {division} {leaguePoints} LP, {wins} Wins, {losses} Losses";
    }

    public static (string? LastPlayed, string? SerializedEntries) ParseMatchHistory(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return default;

        try
        {
            var games = JObject.Parse(content)["games"]?["games"] as JArray;
            if (games == null || games.Count == 0)
                return default;

            DateTimeOffset? latestMatchDate = null;
            var historyEntries = new List<string>();
            foreach (var game in games)
            {
                if (!DateTimeOffset.TryParse(game["gameCreationDate"]?.ToString(), out var gameDate))
                    continue;

                if (latestMatchDate == null || gameDate > latestMatchDate.Value)
                    latestMatchDate = gameDate;

                var localDate = gameDate.LocalDateTime.ToString("g");
                var gameMode = game["gameMode"]?.ToString() ?? "Unknown";
                var queueId = game["queueId"]?.ToString() ?? "?";
                var durationSeconds = game["gameDuration"]?.ToObject<int?>() ?? 0;
                var durationText = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)).ToString(@"mm\:ss");
                var participant = game["participants"]?.FirstOrDefault();
                var championId = participant?["championId"]?.ToString() ?? "?";
                var win = participant?["stats"]?["win"]?.ToObject<bool?>();
                var kills = participant?["stats"]?["kills"]?.ToObject<int?>() ?? 0;
                var deaths = participant?["stats"]?["deaths"]?.ToObject<int?>() ?? 0;
                var assists = participant?["stats"]?["assists"]?.ToObject<int?>() ?? 0;
                var result = win == true ? "Win" : win == false ? "Loss" : "Unknown";

                historyEntries.Add(
                    $"{localDate}||{result} | Q:{queueId} | {gameMode} | Champ:{championId} | KDA {kills}/{deaths}/{assists} | {durationText}");
            }

            return (latestMatchDate?.LocalDateTime.ToString("g"), string.Join(":", historyEntries));
        }
        catch
        {
            return default;
        }
    }

    public static bool IsRsoReady(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            return JObject.Parse(content)["ready"]?.ToObject<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    public static string? ParseEulaAcceptance(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            return JToken.Parse(content).Value<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     A product restriction the login queue returns instead of a session. Riot sign-in can
    ///     succeed and the client still stays on the unknown-player screen because this refusal
    ///     happens afterwards.
    /// </summary>
    public readonly record struct AccountRestriction(string Status, string Reason, string Duration, string Detail);

    /// <summary>
    ///     Reads a ban or suspension from a League login session or a login-queue error. The queue
    ///     answers <c>{"errorCode":"Player Banned","restrictions":[{"type":"PERMANENT_BAN",
    ///     "reason":"GENERIC","scope":"lol","expirationMillis":...}]}</c>. A live session carries
    ///     the same restriction under <c>restrictions</c> or <c>ban</c>.
    /// </summary>
    public static AccountRestriction? ParseAccountRestriction(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            var node = JToken.Parse(content);
            var restriction = FindRestriction(node);
            if (restriction == null)
                return null;

            var type = restriction["type"]?.ToString()
                       ?? restriction["restrictionType"]?.ToString()
                       ?? node["errorCode"]?.ToString();
            if (string.IsNullOrWhiteSpace(type) ||
                !type.Contains("ban", StringComparison.OrdinalIgnoreCase) &&
                !type.Contains("suspend", StringComparison.OrdinalIgnoreCase))
                return null;

            var reason = HumanizeToken(FirstText(restriction, "reason", "restrictionReason", "description"));
            var scope = FirstText(restriction, "scope");
            var duration = FormatDuration(restriction);
            var detail = string.Join(", ",
                new[] { reason, scope, duration }.Where(part => !string.IsNullOrWhiteSpace(part)));

            return new AccountRestriction(HumanizeToken(type), reason, duration, detail);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JToken? FindRestriction(JToken node)
    {
        if (node is JArray array)
            return array.Select(FindRestriction).FirstOrDefault(item => item != null);

        if (node is not JObject obj)
            return null;

        foreach (var name in new[] { "restrictions", "ban", "penalty" })
        {
            if (obj[name] is JArray list && list.Count > 0)
                return list[0];
            if (obj[name] is JObject child)
                return child;
        }

        if (obj["errorCode"]?.ToString()?.Contains("ban", StringComparison.OrdinalIgnoreCase) == true ||
            obj["errorCode"]?.ToString()?.Contains("suspend", StringComparison.OrdinalIgnoreCase) == true)
            return obj;

        return null;
    }

    private static string? FirstText(JToken node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = node[name]?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string FormatDuration(JToken restriction)
    {
        var type = restriction["type"]?.ToString() ?? restriction["restrictionType"]?.ToString() ?? string.Empty;
        if (type.Contains("PERMANENT", StringComparison.OrdinalIgnoreCase))
            return "Permanent";

        var expiry = restriction["expirationMillis"] ?? restriction["expirationDate"] ?? restriction["expiry"];
        if (expiry != null && expiry.Type != JTokenType.Null &&
            long.TryParse(expiry.ToString(), out var millis) && millis > 0)
        {
            var until = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime.ToString("g");
            var remaining = DateTimeOffset.FromUnixTimeMilliseconds(millis) - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
                return "until " + until;

            var days = (int)remaining.TotalDays;
            var span = days >= 1 ? $"{days}d {remaining.Hours}h" : $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"{span} (until {until})";
        }

        var gameData = restriction["dat"]?["gameData"] ?? restriction["gameData"];
        var length = gameData?["banLength"] ?? gameData?["duration"] ?? restriction["duration"];
        if (length != null && long.TryParse(length.ToString(), out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds).ToString(@"d\d\ h\h");

        return string.Empty;
    }

    private static string HumanizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var words = token.Replace('_', ' ').Trim().ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
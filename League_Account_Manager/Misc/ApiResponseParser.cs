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
}
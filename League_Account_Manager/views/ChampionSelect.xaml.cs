using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class ChampionSelect : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private JObject? region;


    public ChampionSelect()
    {
        InitializeComponent();
    }

    private static string BuildRankString(JToken? queue, bool highest)
    {
        if (queue == null) return "Unranked";

        var tierKey = highest ? "highestTier" : "tier";
        var divKey = highest ? "highestDivision" : "division";

        var tier = queue[tierKey]?.ToString() ?? string.Empty;
        var div = queue[divKey]?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tier) || tier.Equals("NA", StringComparison.OrdinalIgnoreCase))
            return "Unranked";

        if (string.IsNullOrWhiteSpace(div) || div.Equals("NA", StringComparison.OrdinalIgnoreCase))
            return tier;

        return $"{tier} {div}";
    }


    private async void OnPullTeamInfoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            if (!resp.IsSuccessStatusCode)
            {
                DebugConsole.WriteLine($"Champ select not available: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return;
            }

            var players = JObject.Parse(await GetResponseBody(resp));
            var team = players["myTeam"] as JArray;
            if (team == null)
            {
                DebugConsole.WriteLine("No team data in session.");
                return;
            }

            for (var i = 0; i < team.Count; i++)
            {
                var player = team[i];
                var playerText = FindName($"Player{i + 1}") as TextBox;
                if (playerText == null)
                    continue;

                var puuid = player?["puuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(puuid))
                {
                    Logger.Warn("Missing puuid for team member at index {Index}", i);
                    playerText.Text = "Error loading data";
                    continue;
                }

                playerText.Text = await PullRankedInfo(puuid, i);
            }
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine(exception.ToString());
            Logger.Error(exception, "Error loading data");
        }
    }

    private async Task<string> PullRankedInfo(string puuid, int I)
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", $"/lol-ranked/v1/ranked-stats/{puuid}", "");
            var rankedinfo = JObject.Parse(await GetResponseBody(resp));
            DebugConsole.WriteLine(rankedinfo.ToString());
            resp = await Lcu.Connector("league", "get",
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=40", "");
            var rankedinfo2 = JObject.Parse(await GetResponseBody(resp));

            var games = rankedinfo2["games"]?["games"];
            if (games == null)
            {
                var warn = $"No match history returned for puuid {puuid} at index {I}: payload={rankedinfo2}";
                Logger.Warn(warn);
                DebugConsole.WriteLine(warn);
            }

            Gamestats gameStats = CalculateGameStats(games);
            var wr = gameStats.Wins + gameStats.Losses > 0 ? gameStats.Wins / (gameStats.Wins + gameStats.Losses) : 0;
            var kdaDen = gameStats.Deaths <= 0 ? 1 : gameStats.Deaths;
            var kda = (gameStats.Kills + gameStats.Assists) / kdaDen;

            var solo = rankedinfo["queueMap"]?["RANKED_SOLO_5x5"];
            if (solo == null)
            {
                var warn = $"Solo queue data missing for puuid {puuid} at index {I}: queueMap={rankedinfo["queueMap"]}";
                Logger.Warn(warn);
                DebugConsole.WriteLine(warn);
            }

            var border = RanksList.ItemContainerGenerator.ContainerFromIndex(I) as FrameworkElement;

            var playerPeak = border?.FindName($"player{I + 1}peak") as TextBlock;
            var playerRank = border?.FindName($"player{I + 1}rank") as TextBlock;
            var playerWr = border?.FindName($"player{I + 1}wr") as TextBlock;

            if (playerPeak != null) playerPeak.Text = BuildRankString(solo, true);
            if (playerRank != null) playerRank.Text = BuildRankString(solo, false);
            if (playerWr != null) playerWr.Text = $"{gameStats.Wins} / {gameStats.Losses} / {wr:P2} kda {kda:F2}";
            resp = await Lcu.Connector("league", "get", $"/lol-summoner/v2/summoners/puuid/{puuid}", "");
            var playerinfo = JObject.Parse(await GetResponseBody(resp));
            return playerinfo["gameName"] + "#" + playerinfo["tagLine"];
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "PullRankedInfo failed for puuid {Puuid} at index {Index}", puuid, I);
            DebugConsole.WriteLine(exception.ToString());
        }

        return "Error loading data";
    }

    private async Task<JObject?> PullRankedInfo2(string puuid, int I)
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", $"/lol-ranked/v1/ranked-stats/{puuid}", "");
            var rankedinfo = JObject.Parse(await GetResponseBody(resp));
            resp = await Lcu.Connector("league", "get",
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=40", "");
            var rankedinfo2 = JObject.Parse(await GetResponseBody(resp));

            var solo = rankedinfo["queueMap"]?["RANKED_SOLO_5x5"];
            if (solo == null)
            {
                var warn = $"Solo queue data missing for puuid {puuid} at index {I}: queueMap={rankedinfo["queueMap"]}";
                Logger.Warn(warn);
                DebugConsole.WriteLine(warn);
            }

            var games = rankedinfo2["games"]?["games"];
            if (games == null)
            {
                var warn = $"No match history returned for puuid {puuid} at index {I}: payload={rankedinfo2}";
                Logger.Warn(warn);
                DebugConsole.WriteLine(warn);
            }

            Gamestats gameStats = CalculateGameStats(games);
            var wins = gameStats.Wins;
            var losses = gameStats.Losses;
            var kills = gameStats.Kills;
            var deaths = gameStats.Deaths;
            var assists = gameStats.Assists;

            var wr = wins + losses > 0 ? wins / (wins + losses) : 0;
            var kdaDen = deaths <= 0 ? 1 : deaths;
            var kda = (kills + assists) / kdaDen;

            var border = RanksList.ItemContainerGenerator.ContainerFromIndex(I) as FrameworkElement;

            var playerPeak = border?.FindName($"player{I + 1}peak") as TextBlock;
            var playerRank = border?.FindName($"player{I + 1}rank") as TextBlock;
            var playerWr = border?.FindName($"player{I + 1}wr") as TextBlock;
            if (playerPeak != null) playerPeak.Text = BuildRankString(solo, true);
            if (playerRank != null) playerRank.Text = BuildRankString(solo, false);
            if (playerWr != null) playerWr.Text = $"{wins} / {losses} / {wr:P2} kda {kda:F2}";
            resp = await Lcu.Connector("league", "get", $"/lol-summoner/v2/summoners/puuid/{puuid}", "");
            var playerinfo = JObject.Parse(await GetResponseBody(resp));
            return playerinfo;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "PullRankedInfo2 failed for puuid {Puuid} at index {Index}", puuid, I);
            DebugConsole.WriteLine(exception.ToString());
        }

        return null;
    }


    private async Task<string> GetResponseBody(dynamic resp)
    {
        var data = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return data;
    }

    private Gamestats CalculateGameStats(JToken? games)
    {
        try
        {
            if (games == null)
            {
                Logger.Warn("CalculateGameStats called with null games list");
                return new Gamestats();
            }

            var tmp = new Gamestats { Assists = 0, Deaths = 0, Kills = 0, Losses = 0, Wins = 0 };

            foreach (var game in games)
                if ((game["mapId"]?.ToObject<int>() ?? 0) == 11 &&
                    string.Equals(game["gameType"]?.ToString(), "MATCHED_GAME", StringComparison.Ordinal) &&
                    string.Equals(game["queueId"]?.ToString(), "420", StringComparison.Ordinal))
                {
                    var win = game["participants"]?[0]?["stats"]?["win"];
                    if (win == null)
                        continue;

                    if (Convert.ToBoolean(win)) tmp.Wins++;
                    else if (!Convert.ToBoolean(win)) tmp.Losses++;

                    try
                    {
                        tmp.Kills += game["participants"]?[0]?["stats"]?["kills"]?.ToObject<int>() ?? 0;
                        tmp.Deaths += game["participants"]?[0]?["stats"]?["deaths"]?.ToObject<int>() ?? 0;
                        tmp.Assists += game["participants"]?[0]?["stats"]?["assists"]?.ToObject<int>() ?? 0;
                    }
                    catch (Exception exception)
                    {
                        LogManager.GetCurrentClassLogger().Error(exception, "Error");
                    }
                }

            return tmp;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return new Gamestats();
    }

    private void OnOpenOpGgClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // Ensure region is loaded before building the URL
            EnsureRegionAsync().GetAwaiter().GetResult();
            if (region == null) throw new InvalidOperationException("Region not available");

            var playerNumber = ((Button)sender).Name.Last();
            var playerName = FindName($"Player{playerNumber}") as TextBox;
            var reg = region["region"]?.ToString();
            if (string.IsNullOrWhiteSpace(reg) || playerName == null) return;
            OpenUrl($"https://www.op.gg/summoners/{reg}/{playerName.Text.Replace("#", "-")}");
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine(exception.ToString());
            Logger.Error(exception, "Error");
            Notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => Notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
    }

    private async Task EnsureRegionAsync()
    {
        if (region != null) return;
        var resp = await Lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        region = JObject.Parse(await GetResponseBody(resp));
    }

    private async void OnOpenLeagueOfGraphsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureRegionAsync();
            if (region == null) throw new InvalidOperationException("Region not available");
            var playerNumber = ((Button)sender).Name.Last();
            var playerName = FindName($"Player{playerNumber}") as TextBox;
            var reg = region["region"]?.ToString()?.ToLower();
            if (string.IsNullOrWhiteSpace(reg) || playerName == null) return;
            OpenUrl($"https://www.leagueofgraphs.com/summoner/{reg}/{playerName.Text.Replace("#", "-")}");
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine(exception.ToString());
            Logger.Error(exception, "Error");
            Notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data",
                NotificationType.Notification, "WindowArea", TimeSpan.FromSeconds(10));
        }
    }

    private async void OnOpenMultiOpGgClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            var players = JObject.Parse(await GetResponseBody(resp));
            DebugConsole.WriteLine(players);
            var i = 0;
            resp = await Lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
            region = JObject.Parse(await GetResponseBody(resp));

            var regionValue = region["region"]?.ToString();
            if (string.IsNullOrWhiteSpace(regionValue))
                return;

            var region_parsed = RegionHelperUtil.RegionParser(regionValue);

            var url = $"https://www.op.gg/multisearch/{region_parsed}?summoners=";

            var team = players["myTeam"] as JArray;
            if (team == null)
                return;

            foreach (var player in team)
            {
                var puuid = player?["puuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(puuid))
                    continue;

                var playerTex = await PullRankedInfo2(puuid, i);
                i++;
                if (playerTex == null)
                    continue;

                var gameName = playerTex["gameName"]?.ToString();
                var tagLine = playerTex["tagLine"]?.ToString();
                if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine))
                    continue;

                url += $"{gameName}%23{tagLine},";
            }

            OpenUrl(url);
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine(exception.ToString());
            Logger.Error(exception, "PullTeamInfo failed while loading team info");
        }
    }

    private async void OnOpenPoroProfessorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
            region = JObject.Parse(await GetResponseBody(resp));
            resp = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            var players = JObject.Parse(await GetResponseBody(resp));
            DebugConsole.WriteLine(players);
            var i = 0;
            var regionValue = region["region"]?.ToString()?.ToLower();
            if (string.IsNullOrWhiteSpace(regionValue))
                return;

            var url = $"https://porofessor.gg/pregame/{regionValue}/";

            var team = players["myTeam"] as JArray;
            if (team == null)
                return;

            foreach (var player in team)
            {
                var puuid = player?["puuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(puuid))
                    continue;

                var playerText = await PullRankedInfo2(puuid, i);
                i++;
                if (playerText == null)
                    continue;

                var gameName = playerText["gameName"]?.ToString();
                var tagLine = playerText["tagLine"]?.ToString();
                if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine))
                    continue;

                url += $"{gameName} -{tagLine},";
            }

            if (url.EndsWith(",", StringComparison.Ordinal))
                url = url.Remove(url.Length - 1, 1);
            OpenUrl(url);
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine(exception.ToString());
            Logger.Error(exception, "OpenPoroProfessor failed while building url");
        }
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private async void OnDodgeClick(object sender, RoutedEventArgs e)
    {
        var resp = await Lcu.Connector("league", "post",
            "/lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]",
            "");
        DebugConsole.WriteLine(await GetResponseBody(resp));
    }
}

public static class RegionHelperUtil
{
    public static string RegionParser(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return string.Empty;

        var region_parsed = region.ToLower();

        switch (region_parsed)
        {
            case "euw1": region_parsed = "euw"; break;
            case "na1": region_parsed = "na"; break;
            case "kr1": region_parsed = "kr"; break;
            case "oc1": region_parsed = "oce"; break;
            case "eun1": region_parsed = "eune"; break;
            case "la1": region_parsed = "lan"; break;
            case "la2": region_parsed = "las"; break;
            case "ru1": region_parsed = "ru"; break;
            case "tr1": region_parsed = "tr"; break;
            case "jp1": region_parsed = "jp"; break;
        }

        return region_parsed;
    }
}

public class Gamestats
{
    public double? Wins { get; set; }
    public double? Losses { get; set; }
    public double? Kills { get; set; }
    public double? Deaths { get; set; }
    public double? Assists { get; set; }
}
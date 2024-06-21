using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FlaUI.Core.Input;
using Newtonsoft.Json.Linq;
using NLog;
using Notification.Wpf;

namespace League_Account_Manager.views;

public partial class Page4 : Page
{
    private JObject region;


    public Page4()
    {
        InitializeComponent();
    }

    
    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            var players = JObject.Parse(await GetResponseBody(resp));
            Console.WriteLine(players);
            var i = 0;
            foreach (var player in players["myTeam"])
            {
                var playerText = FindName($"Player{i + 1}") as TextBox;
                playerText.Text = await pullrankedinfo(player["puuid"], i);
                i++;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }
    
        private async Task<string> pullrankedinfo(dynamic puuid, int I)
    {
        try
        {
            var resp = await lcu.Connector("league", "get", $"/lol-ranked/v1/ranked-stats/{puuid}", "");
            var rankedinfo = JObject.Parse(await GetResponseBody(resp));
            resp = await lcu.Connector("league", "get",
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=40", "");
            var rankedinfo2 = JObject.Parse(await GetResponseBody(resp));

            Gamestats gameStats = CalculateGameStats(rankedinfo2["games"]["games"]);
            var wr = (double)(gameStats.Wins / (gameStats.Wins + gameStats.Losses));
            var kda = (double)((gameStats.Kills + gameStats.Assists) / gameStats.Deaths);

            var playerPeak = FindName($"player{I + 1}peak") as ContentControl;
            var playerRank = FindName($"player{I + 1}rank") as ContentControl;
            var playerWr = FindName($"player{I + 1}wr") as ContentControl;

            playerPeak.Content =
                $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"]}";
            playerRank.Content =
                $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"]}";
            playerWr.Content = $"{gameStats.Wins} / {gameStats.Losses} / {wr:P2} kda {kda:F2}";
             resp = await lcu.Connector("league", "get", $"/lol-summoner/v2/summoners/puuid/{puuid}", "");
             dynamic playerinfo = JObject.Parse(await GetResponseBody(resp));
             return playerinfo["gameName"] + "#" + playerinfo["tagLine"];
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return "Error loading data";
    }
    private async Task<dynamic> pullrankedinfo2(dynamic puuid, int I)
    {
        try
        {
            var resp = await lcu.Connector("league", "get", $"/lol-ranked/v1/ranked-stats/{puuid}", "");
            var rankedinfo = JObject.Parse(await GetResponseBody(resp));
            resp = await lcu.Connector("league", "get",
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=40", "");
            var rankedinfo2 = JObject.Parse(await GetResponseBody(resp));

            Gamestats gameStats = CalculateGameStats(rankedinfo2["games"]["games"]);
            var wr = (double)(gameStats.Wins / (gameStats.Wins + gameStats.Losses));
            var kda = (double)((gameStats.Kills + gameStats.Assists) / gameStats.Deaths);

            var playerPeak = FindName($"player{I + 1}peak") as ContentControl;
            var playerRank = FindName($"player{I + 1}rank") as ContentControl;
            var playerWr = FindName($"player{I + 1}wr") as ContentControl;

            playerPeak.Content =
                $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"]}";
            playerRank.Content =
                $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"]}";
            playerWr.Content = $"{gameStats.Wins} / {gameStats.Losses} / {wr:P2} kda {kda:F2}";
            resp = await lcu.Connector("league", "get", $"/lol-summoner/v2/summoners/puuid/{puuid}", "");
            dynamic playerinfo = JObject.Parse(await GetResponseBody(resp));
            return playerinfo;
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }

        return "Error loading data";
    }


    private async Task<string> GetResponseBody(dynamic resp)
    {
        var data = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return data;
    }

    private Gamestats CalculateGameStats(JToken games)
    {
        try
        {
            int wins = 0, losses = 0, kills = 0, deaths = 0, assists = 0;
            var tmp = new Gamestats { Assists = 0, Deaths = 0, Kills = 0, Losses = 0, Wins = 0 };

            foreach (var game in games)
                if (Convert.ToInt32(game["mapId"]) == 11 && game["gameType"].ToString() == "MATCHED_GAME" &&
                    game["queueId"].ToString() == "420")
                {
                    var win = game["participants"][0]["stats"]["win"];
                    if (Convert.ToBoolean(win)) tmp.Wins++;
                    else if (Convert.ToBoolean(win) == false) tmp.Losses++;

                    try
                    {
                        tmp.Kills += int.Parse(game["participants"][0]["stats"]["kills"].ToString());
                        tmp.Deaths += int.Parse(game["participants"][0]["stats"]["deaths"].ToString());
                        tmp.Assists += int.Parse(game["participants"][0]["stats"]["assists"].ToString());
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

        return null;
    }

    private void Button_Click_OpenUrl(object sender, RoutedEventArgs e)
    {
        try
        {
            var playerNumber = ((Button)sender).Name.Last();
            var playerName = FindName($"Player{playerNumber}") as TextBox;
            OpenUrl($"https://www.op.gg/summoners/{region["region"]}/{playerName.Text.Replace("#", "-")}");
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error");
            notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data",
                NotificationType.Notification,
                "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null, () => notif.donothing(), "OK",
                NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
    }

    private void Button_Click_OpenUrlLeagueOfGraphs(object sender, RoutedEventArgs e)
    {
        try
        {
            var playerNumber = ((Button)sender).Name.Last();
            //Console.Writeline($"Player{playerNumber}");
            var playerName = FindName($"Player{playerNumber}") as TextBox;
            OpenUrl(
                $"https://www.leagueofgraphs.com/summoner/{region["region"].ToString().ToLower()}/{playerName.Text.Replace("#", "-")}");
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error");
            notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data",
                NotificationType.Notification, "WindowArea", TimeSpan.FromSeconds(10), null, null, null, null,
                () => notif.donothing(), "OK", NotificationTextTrimType.NoTrim, 2U, true, null, null, false);
        }
    }

    private async void Button_Click_6(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            var players = JObject.Parse(await GetResponseBody(resp));
            Console.WriteLine(players);
            var i = 0;
            resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
            region = JObject.Parse(await GetResponseBody(resp));

            string region_parsed = RegionHelperUtil.RegionParser(region["region"].ToString());

            var url = $"https://www.op.gg/multisearch/{region_parsed}?summoners=";

            foreach (var player in players["myTeam"])
            {
                dynamic playerTex = await pullrankedinfo2(player["puuid"], i);
                i++;
                url += $"{playerTex["gameName"]}%23{playerTex["tagLine"]},";
            }

            OpenUrl(url);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async void OpenPoroProfessor(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
            region = JObject.Parse(await GetResponseBody(resp));
             resp = await lcu.Connector("league", "get", "/lol-champ-select/v1/session", "");
            var players = JObject.Parse(await GetResponseBody(resp));
            Console.WriteLine(players);
            var i = 0;
            var url = $"https://porofessor.gg/pregame/{region["region"].ToString().ToLower()}/";

            foreach (var player in players["myTeam"])
            {
                dynamic playerText = await pullrankedinfo2(player["puuid"], i);
                i++;
                url += $"{playerText["gameName"]} -{playerText["tagLine"]},";
            }

            url = url.Remove(url.Length - 1, 1);
            OpenUrl(url);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private async void DODGE(object sender, RoutedEventArgs e)
    {
        var resp = await lcu.Connector("league", "post",
            "/lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]",
            "");
    }
}

public static class RegionHelperUtil
{
    public static string RegionParser(string region)
    {
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
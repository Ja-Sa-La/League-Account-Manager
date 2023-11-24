using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
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
        var resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        if (resp.ToString() == "0")
        {
            notif.notificationManager.Show("Error", "League of legends client is not running!", NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
            return;
        }

        region = JObject.Parse(await GetResponseBody(resp));
        resp = await lcu.Connector("riot", "get", "/chat/v5/participants/champ-select", "");
        var players = JObject.Parse(await GetResponseBody(resp));
        Console.WriteLine(players);
        var i = 0;

        foreach (var player in players["participants"])
        {
            var playerText = FindName($"Player{i + 1}") as TextBox;
            playerText.Text = player["game_name"] + "#" + player["game_tag"];
            pullrankedinfo(player["puuid"], i);
            i++;
        }
    }

    private async void pullrankedinfo(dynamic puuid, int I)
    {
        var resp = await lcu.Connector("league", "get", $"/lol-ranked/v1/ranked-stats/{puuid}", "");
        var rankedinfo = JObject.Parse(await GetResponseBody(resp));
        resp = await lcu.Connector("league", "get",
            $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=40", "");
        var rankedinfo2 = JObject.Parse(await GetResponseBody(resp));

        var gameStats = CalculateGameStats(rankedinfo2["games"]["games"]);
        var wr = gameStats.Wins / (gameStats.Wins + gameStats.Losses);
        var kda = (gameStats.Kills + gameStats.Assists) / gameStats.Deaths;

        var playerPeak = FindName($"player{I + 1}peak") as ContentControl;
        var playerRank = FindName($"player{I + 1}rank") as ContentControl;
        var playerWr = FindName($"player{I + 1}wr") as ContentControl;

        playerPeak.Content =
            $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"]}";
        playerRank.Content =
            $"{rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"]} {rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"]}";
        playerWr.Content = $"{gameStats.Wins} / {gameStats.Losses} / {wr:P2} kda {kda:F2}";
    }

    private async Task<string> GetResponseBody(dynamic resp)
    {
        var data = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.WriteLine(data);
        return data;
    }

    private Gamestats CalculateGameStats(JToken games)
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
                catch (Exception ex)
                {
                }
            }

        return tmp;
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
            notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data", NotificationType.Error,
                "WindowArea", onClick: () => notif.donothing());
        }
    }

    private void Button_Click_OpenUrlLeagueOfGraphs(object sender, RoutedEventArgs e)
    {
        try
        {
            var playerNumber = ((Button)sender).Name.Last();
            Console.WriteLine($"Player{playerNumber}");
            var playerName = FindName($"Player{playerNumber}") as TextBox;
            OpenUrl(
                $"https://www.leagueofgraphs.com/summoner/{region["region"].ToString().ToLower()}/{playerName.Text.Replace("#", "-")}");
        }
        catch (Exception exception)
        {
            notif.notificationManager.Show("Error", "Error occurred! make sure you pulled data",
                NotificationType.Error, "WindowArea", onClick: () => notif.donothing());
        }
    }

    private async void Button_Click_6(object sender, RoutedEventArgs e)
    {
        var resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        region = JObject.Parse(await GetResponseBody(resp));
        resp = await lcu.Connector("riot", "get", "/chat/v5/participants/champ-select", "");
        var players = JObject.Parse(await GetResponseBody(resp));
        var url = $"https://www.op.gg/multisearch/{region["region"]}?summoners=";

        foreach (var player in players["participants"])
            url += $"{player["name"]},";

        OpenUrl(url);
    }

    private async void OpenPoroProfessor(object sender, RoutedEventArgs e)
    {
        var resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        region = JObject.Parse(await GetResponseBody(resp));
        resp = await lcu.Connector("riot", "get", "/chat/v5/participants/champ-select", "");
        var players = JObject.Parse(await GetResponseBody(resp));
        var url = $"https://porofessor.gg/pregame/{region["region"].ToString().ToLower()}/";

        foreach (var player in players["participants"])
            url += $"{player["game_name"]}-{player["game_tag"]},";

        OpenUrl(url);
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
        Console.WriteLine(resp);
    }
}

public class Gamestats
{
    public int? Wins { get; set; }
    public int? Losses { get; set; }
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
}
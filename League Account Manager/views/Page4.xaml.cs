using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page4.xaml
/// </summary>
public partial class Page4 : Page
{
    public dynamic region;

    public Page4()
    {
        InitializeComponent();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        var resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        region = JObject.Parse(responseBody2);
        resp = await lcu.Connector("riot", "get", "/chat/v5/participants/champ-select", "");
        responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var players = JObject.Parse(responseBody2);
        var i = 0;
        foreach (var VARIABLE in players["participants"])
        {
            switch (i)
            {
                case 0:
                {
                    Player1.Text = VARIABLE["name"];
                    pullrankedinfo(VARIABLE["puuid"], i);
                    break;
                }
                case 1:
                {
                    Player2.Text = VARIABLE["name"];
                    pullrankedinfo(VARIABLE["puuid"], i);
                    break;
                }
                case 2:
                {
                    Player3.Text = VARIABLE["name"];
                    pullrankedinfo(VARIABLE["puuid"], i);
                    break;
                }
                case 3:
                {
                    Player4.Text = VARIABLE["name"];
                    pullrankedinfo(VARIABLE["puuid"], i);
                    break;
                }
                case 4:
                {
                    Player5.Text = VARIABLE["name"];
                    pullrankedinfo(VARIABLE["puuid"], i);
                    break;
                }
            }

            i++;
        }
    }

    private async void pullrankedinfo(dynamic puuid, int i)
    {
        var resp = await lcu.Connector("league", "get", "/lol-ranked/v1/ranked-stats/" + puuid.ToString(), "");
        var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var rankedinfo = JObject.Parse(responseBody2);
        resp = await lcu.Connector("league", "get",
            "/lol-match-history/v1/products/lol/" + puuid.ToString() + "/matches?begIndex=0&endIndex=40", "");
        responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var rankedinfo2 = JObject.Parse(responseBody2);
        var wins = 0;
        var losses = 0;
        var kills = 0;
        var death = 0;
        var assists = 0;
        foreach (var item in rankedinfo2["games"]["games"])
            if (item["mapId"] == 11 && item["gameType"].ToString() == "MATCHED_GAME" &&
                item["queueId"].ToString() == "420")
            {
                var jotain = item["participants"][0]["stats"]["win"];
                if (jotain == true)
                    wins++;
                else if (jotain == false)
                    losses++;
                try
                {
                    kills = kills + int.Parse(item["participants"][0]["stats"]["kills"].ToString());
                    death = death + int.Parse(item["participants"][0]["stats"]["deaths"].ToString());
                    assists = assists + int.Parse(item["participants"][0]["stats"]["assists"].ToString());
                }
                catch (Exception ex)
                {
                }
            }

        double wr = wins + losses;
        wr = wins / wr;
        wr = double.Round(wr, 4) * 100;
        kills += assists;

        var kda = (double)kills / death;
        kda = double.Round(kda, 2);

        switch (i)
        {
            case 0:
            {
                player1peak.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"];
                player1rank.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"];
                player1wr.Content = wins + " / " + losses + " / " + wr.ToString("F2") + "% " + kda.ToString("F2");
                break;
            }
            case 1:
            {
                player2peak.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"];
                player2rank.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"];
                player2wr.Content = wins + " / " + losses + " / " + wr.ToString("F2") + "% kda " + kda.ToString("F2");
                break;
            }
            case 2:
            {
                player3peak.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"];
                player3rank.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"];
                player3wr.Content = wins + " / " + losses + " / " + wr.ToString("F2") + "% kda " + kda.ToString("F2");
                break;
            }
            case 3:
            {
                player4peak.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"];
                player4rank.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"];
                player4wr.Content = wins + " / " + losses + " / " + wr.ToString("F2") + "% kda " + kda.ToString("F2");
                break;
            }
            case 4:
            {
                player5peak.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestTier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["highestDivision"];
                player5rank.Content = rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["tier"] + " " +
                                      rankedinfo["queueMap"]["RANKED_SOLO_5x5"]["division"];
                player5wr.Content = wins + " / " + losses + " / " + wr.ToString("F2") + "% kda " + kda.ToString("F2");
                break;
            }
        }

        i++;
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.op.gg/summoners/" + region["region"].ToString() + "/" + Player1.Text,
            UseShellExecute = true
        });
    }

    private void Button_Click_2(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.op.gg/summoners/" + region["region"].ToString() + "/" + Player2.Text,
            UseShellExecute = true
        });
    }

    private void Button_Click_3(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.op.gg/summoners/" + region["region"].ToString() + "/" + Player3.Text,
            UseShellExecute = true
        });
    }

    private void Button_Click_4(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.op.gg/summoners/" + region["region"].ToString() + "/" + Player4.Text,
            UseShellExecute = true
        });
    }

    private void Button_Click_5(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.op.gg/summoners/" + region["region"].ToString() + "/" + Player5.Text,
            UseShellExecute = true
        });
    }

    private async void Button_Click_6(object sender, RoutedEventArgs e)
    {
        var resp = await lcu.Connector("riot", "get", "/riotclient/get_region_locale", "");
        var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        region = JObject.Parse(responseBody2);
        resp = await lcu.Connector("riot", "get", "/chat/v5/participants/champ-select", "");
        responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var players = JObject.Parse(responseBody2);
        string url = "https://www.op.gg/multisearch/" + region["region"].ToString() + "?summoners=";
        foreach (var VARIABLE in players["participants"]) url = url + VARIABLE["name"] + ",";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
using System;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using Notification.Wpf;
using static League_Account_Manager.lcu;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page6.xaml
/// </summary>
public partial class Page6 : Page
{
    public Page6()
    {
        InitializeComponent();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var resp = await Connector("league", "get", "/lol-summoner/v1/summoners?name=" + playername.Text, "");
            if (resp.ToString() == "0")
            {
                notif.notificationManager.Show("Error", "League of legends client is not running!",
                    NotificationType.Error, "WindowArea", onClick: () => notif.donothing());
                return;
            }

            var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var rankedinfo = JObject.Parse(responseBody);
            var resp2 = await Connector("league", "get",
                "/lol-match-history/v1/products/lol/" + rankedinfo["puuid"].ToString() +
                "/matches?begIndex=0&endIndex=0", "");
            var responseBody2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
            var rankedinfo2 = JObject.Parse(responseBody2);
            //Console.WriteLine(rankedinfo2);
            foreach (var item in rankedinfo2["games"]["games"])
            {
                string reportstring = "{\"gameId\":" + item["gameId"].ToString() +
                                      ",\"categories\":[\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"HATE_SPEECH\"],\"offenderSummonerId\":" +
                                      rankedinfo["accountId"].ToString() + ",\"offenderPuuid\":\"" +
                                      rankedinfo["puuid"].ToString() + "\"}";
                resp = await Connector("league", "post", "/lol-end-of-game/v2/player-reports", reportstring);
                var responseBody3 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            reportstatus.Content = "Reported player " + playername.Text;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            throw;
        }
    }

    private void playername_TextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private class Data
    {
    }
}
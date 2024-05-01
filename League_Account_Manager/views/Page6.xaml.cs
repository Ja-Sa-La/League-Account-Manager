using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using NLog;
using static League_Account_Manager.lcu;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for Page6.xaml
/// </summary>
public partial class Page6 : Page
{
    private readonly ObservableCollection<PlayersData> plaList = new();
    private bool selected;

    public Page6()
    {
        InitializeComponent();
        Reportable.ItemsSource = plaList;
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            plaList.Clear();
            await Task.Run(async () =>
            {
                var resp = await Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine(responseBody2);
                var summonerinfo = JObject.Parse(responseBody2);

                var resp2 = await Connector("league", "get",
                    "/lol-match-history/v1/products/lol/" + summonerinfo["puuid"].ToString() +
                    "/matches?begIndex=0&endIndex=19", "");
                var currentDateTimeOffset = DateTimeOffset.UtcNow;
                var sevenDaysAgo = currentDateTimeOffset.AddDays(-7);
                responseBody2 = await resp2.Content.ReadAsStringAsync().ConfigureAwait(false);
                JObject rankedinfo2 = JObject.Parse(responseBody2);
                var i = 0;
                Dispatcher.Invoke(() =>
                {
                    Status.Content = $"pulling data from {rankedinfo2["games"]["games"].Count()} games";
                });

                foreach (JObject jToken in rankedinfo2["games"]["games"])
                {
                    var GameplayDate = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(jToken["gameCreation"]));
                    if (GameplayDate >= sevenDaysAgo)
                    {
                        resp = await Connector("league", "get", "/lol-match-history/v1/games/" + jToken["gameId"], "");
                        responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        JObject Gameinfo = JObject.Parse(responseBody2);
                        foreach (JObject player in Gameinfo["participantIdentities"])
                        {
                            if (player["player"]["puuid"].ToString() != summonerinfo["puuid"].ToString())
                                Dispatcher.Invoke(() =>
                                {
                                    plaList.Add(new PlayersData
                                    {
                                        gameId = jToken["gameId"].ToString(),
                                        riotID = player["player"]["gameName"] + "#" + player["player"]["tagLine"],
                                        puuId = player["player"]["puuid"].ToString(),
                                        summonerId = player["player"]["summonerId"].ToString()
                                    });
                                });
                            Thread.Sleep(0);
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        Status.Content = $"{++i} / {rankedinfo2["games"]["games"].Count()} games parsed";
                    });
                }
            });
            Status.Content = $"Total {plaList.Count()} players available to report";
            ////Console.Writeline(rankedinfo2);
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error");
        }
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        if (selected)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var item in plaList) item.report = false;
            });
            selected = false;
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var item in plaList) item.report = true;
            });
            selected = true;
        }
    }

    private void sendReports(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalreport = plaList.Count(item => item.report);
            var currentReports = 0;
            Status.Content = $"{currentReports} / {totalreport} reports created";
            var tmp = plaList;
            Task.Run(async () =>
            {
                foreach (var item in tmp)
                    if (item.report)
                    {
                        var success = await RetryOperation(async () =>
                        {
                            var reportstring = "{\"gameId\":" + item.gameId +
                                               ",\"categories\":[\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"HATE_SPEECH\"],\"offenderSummonerId\":" +
                                               item.summonerId + ",\"offenderPuuid\":\"" +
                                               item.puuId + "\"}";

                            HttpResponseMessage resp = await Connector("league", "post",
                                "/lol-player-report-sender/v1/match-history-reports", reportstring);

                            var responseBody3 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            //Console.Writeline(responseBody3);

                            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    Status.Content =
                                        $"Currently rate limited waiting! {currentReports} / {totalreport} reports created";
                                });
                                Thread.Sleep(50000);
                                return false; // Indicate failure to retry
                            }

                            return true; // Indicate success
                        }, 10);

                        if (success)
                            Dispatcher.Invoke(() =>
                            {
                                plaList.Where(thing =>
                                        thing.gameId == item.gameId && thing.summonerId == item.summonerId)
                                    .ToList()
                                    .ForEach(thing => thing.reported = "yes");
                                Reportable.ItemsSource = null;
                                Reportable.ItemsSource = plaList;
                                Status.Content = $"{++currentReports} / {totalreport} reports created";
                            });

                        Thread.Sleep(1000);
                    }
            });
        }
        catch (Exception exception)
        {
            LogManager.GetCurrentClassLogger().Error(exception, "Error loading data");
        }
    }

    private async Task<bool> RetryOperation(Func<Task<bool>> operation, int maxRetries)
    {
        var currentRetry = 0;

        while (currentRetry < maxRetries)
            try
            {
                if (await operation())
                    // If the operation succeeds, return true
                    return true;
            }
            catch (Exception ex)
            {
                // Log or handle the exception as needed
                // Increment the retry count
                currentRetry++;
                // You might want to introduce a delay between retries
                // e.g., await Task.Delay(TimeSpan.FromSeconds(1));
            }

        // If the operation consistently fails after retries, return false
        return false;
    }

    private class PlayersData : INotifyPropertyChanged
    {
        private bool _report;
        public string gameId { get; set; }
        public string puuId { get; set; }
        public string summonerId { get; set; }
        public string riotID { get; set; }
        public string reported { get; set; } = "no";

        public bool report
        {
            get => _report;
            set
            {
                if (_report != value)
                {
                    _report = value;
                    OnPropertyChanged(nameof(report));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
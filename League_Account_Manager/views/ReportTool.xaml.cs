using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using NLog;
using static League_Account_Manager.Misc.Lcu;

namespace League_Account_Manager.views;

/// <summary>
///     Interaction logic for ReportTool.xaml
/// </summary>
public partial class ReportTool : Page
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly ObservableCollection<PlayersData> plaList = new();
    private bool selected;

    public ReportTool()
    {
        InitializeComponent();
        Reportable.ItemsSource = plaList;
    }

    private async void OnLoadReportablePlayersClick(object sender, RoutedEventArgs e)
    {
        try
        {
            plaList.Clear();
            _logger.Info("Loading reportable players for current summoner");
            await Task.Run(async () =>
            {
                var resp = await Connector("league", "get", "/lol-summoner/v1/current-summoner", "");
                var responseBody2 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.Debug("Current summoner info: {Response}", responseBody2);
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
                    Status.Text = $"pulling data from {rankedinfo2["games"]["games"].Count()} games";
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
                        Status.Text = $"{++i} / {rankedinfo2["games"]["games"].Count()} games parsed";
                    });
                }
            });
            _logger.Info("Loaded {Count} potential report targets", plaList.Count);
            Status.Text = $"Total {plaList.Count()} players available to report";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load reportable players");
        }
    }

    private void OnToggleSelectAllClick(object sender, RoutedEventArgs e)
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

    private void OnSendReportsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalreport = plaList.Count(item => item.report);
            var currentReports = 0;
            Status.Text = $"{currentReports} / {totalreport} reports created";
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
                            _logger.Debug("Report response for {GameId}/{SummonerId}: {Response}", item.gameId,
                                item.summonerId, responseBody3);

                            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                _logger.Warn("Rate limited while sending reports. Processed {Processed} of {Total}",
                                    currentReports, totalreport);
                                Dispatcher.Invoke(() =>
                                {
                                    Status.Text =
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
                                Status.Text = $"{++currentReports} / {totalreport} reports created";
                                _logger.Info("Report submitted for {GameId}/{SummonerId} ({Current}/{Total})",
                                    item.gameId, item.summonerId, currentReports, totalreport);
                            });

                        Thread.Sleep(1000);
                    }
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed while queuing report submissions");
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
                _logger.Warn(ex, "Retry {Retry} of {MaxRetries} failed", currentRetry + 1, maxRetries);
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
        public string gameId { get; set; } = string.Empty;
        public string puuId { get; set; } = string.Empty;
        public string summonerId { get; set; } = string.Empty;
        public string riotID { get; set; } = string.Empty;
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
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
            using var summonerResponse = await Connector("league", "get", "/lol-summoner/v1/current-summoner", "")
                as HttpResponseMessage;
            if (summonerResponse == null)
                throw new InvalidOperationException("Unable to retrieve the current summoner.");

            var responseBody = await summonerResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.Debug("Current summoner info: {Response}", responseBody);
            var summonerInfo = JObject.Parse(responseBody);
            var currentPuuid = summonerInfo["puuid"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(currentPuuid))
                throw new InvalidOperationException("The current summoner response did not include a PUUID.");

            using var matchHistoryResponse = await Connector("league", "get",
                                           "/lol-match-history/v1/products/lol/" + currentPuuid +
                                           "/matches?begIndex=0&endIndex=19", "") as HttpResponseMessage;
            if (matchHistoryResponse == null)
                throw new InvalidOperationException("Unable to retrieve match history.");

            responseBody = await matchHistoryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            var rankedInfo = JObject.Parse(responseBody);
            var games = rankedInfo["games"]?["games"] as JArray ?? new JArray();
            var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);
            var parsedGames = 0;
            Status.Text = $"pulling data from {games.Count} games";

            foreach (var game in games.OfType<JObject>())
            {
                var gameCreation = game["gameCreation"]?.Value<long?>();
                var gameId = game["gameId"]?.ToString();
                if (gameCreation.HasValue && !string.IsNullOrWhiteSpace(gameId) &&
                    DateTimeOffset.FromUnixTimeMilliseconds(gameCreation.Value) >= sevenDaysAgo)
                {
                    using var gameResponse = await Connector("league", "get", "/lol-match-history/v1/games/" + gameId, "")
                        as HttpResponseMessage;
                    if (gameResponse != null)
                    {
                        responseBody = await gameResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var gameInfo = JObject.Parse(responseBody);
                        var participants = gameInfo["participantIdentities"] as JArray ?? new JArray();
                        foreach (var participant in participants.OfType<JObject>())
                        {
                            var player = participant["player"] as JObject;
                            var playerPuuid = player?["puuid"]?.Value<string>();
                            if (player == null || string.IsNullOrWhiteSpace(playerPuuid) || playerPuuid == currentPuuid)
                                continue;

                            plaList.Add(new PlayersData
                            {
                                gameId = gameId,
                                riotID = player["gameName"] + "#" + player["tagLine"],
                                puuId = playerPuuid,
                                summonerId = player["summonerId"]?.ToString() ?? string.Empty
                            });
                        }
                    }
                }

                Status.Text = $"{++parsedGames} / {games.Count} games parsed";
            }

            _logger.Info("Loaded {Count} potential report targets", plaList.Count);
            Status.Text = $"Total {plaList.Count} players available to report";
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

    private async void OnSendReportsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalreport = plaList.Count(item => item.report);
            var currentReports = 0;
            Status.Text = $"{currentReports} / {totalreport} reports created";
            var tmp = plaList;
            foreach (var item in tmp)
                if (item.report)
                {
                    var success = await RetryOperation(async () =>
                    {
                        var reportstring = "{\"gameId\":" + item.gameId +
                                           ",\"categories\":[\"NEGATIVE_ATTITUDE\",\"VERBAL_ABUSE\",\"HATE_SPEECH\"],\"offenderSummonerId\":" +
                                           item.summonerId + ",\"offenderPuuid\":\"" +
                                           item.puuId + "\"}";

                        using var resp = await Connector("league", "post",
                            "/lol-player-report-sender/v1/match-history-reports", reportstring);

                        var responseBody3 = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        _logger.Debug("Report response for {GameId}/{SummonerId}: {Response}", item.gameId,
                            item.summonerId, responseBody3);

                        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            _logger.Warn("Rate limited while sending reports. Processed {Processed} of {Total}",
                                currentReports, totalreport);
                            Status.Text =
                                $"Currently rate limited waiting! {currentReports} / {totalreport} reports created";
                            await Task.Delay(50000);
                            return false;
                        }

                        return true;
                    }, 10);

                    if (success)
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
                    }

                    await Task.Delay(1000);
                }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed while sending reports");
        }
    }

    private async Task<bool> RetryOperation(Func<Task<bool>> operation, int maxRetries)
    {
        var currentRetry = 0;

        while (currentRetry < maxRetries)
            try
            {
                if (await operation())
                    return true;

                currentRetry++;
                await Task.Delay(TimeSpan.FromSeconds(1));
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
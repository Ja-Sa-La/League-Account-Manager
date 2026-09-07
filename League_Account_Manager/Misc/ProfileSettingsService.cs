using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using NLog;

namespace League_Account_Manager.Misc;

internal static class ProfileSettingsService
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (Process.GetProcessesByName("LeagueClientUx").Length > 0)
                    await ApplyAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogManager.GetCurrentClassLogger().Error(exception, "Error applying saved profile settings");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        }
    }

    private static async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var saved = Settings.settingsloaded;
        if (!string.IsNullOrWhiteSpace(saved.ProfileStatusMessage))
            await SendAsync("league", "put", "/lol-chat/v1/me",
                JsonSerializer.Serialize(new { statusMessage = saved.ProfileStatusMessage }), cancellationToken);

        if (!string.IsNullOrWhiteSpace(saved.ProfileQueue) && !string.IsNullOrWhiteSpace(saved.ProfileRank) &&
            !string.IsNullOrWhiteSpace(saved.ProfileDivision))
            await SendAsync("league", "put", "/lol-chat/v1/me", JsonSerializer.Serialize(new
            {
                lol = new
                {
                    rankedLeagueQueue = saved.ProfileQueue,
                    rankedLeagueTier = saved.ProfileRank,
                    rankedLeagueDivision = saved.ProfileDivision
                }
            }), cancellationToken);

        if (!string.IsNullOrWhiteSpace(saved.ProfileIconId))
            await SendAsync("league", "put", "/lol-summoner/v1/current-summoner/icon/",
                $"{{\"profileIconId\": {saved.ProfileIconId}}}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(saved.ProfileBackgroundId))
            await SendAsync("league", "post", "/lol-summoner/v1/current-summoner/summoner-profile/",
                $"{{\"key\": \"backgroundSkinId\",\"value\": {saved.ProfileBackgroundId}}}", cancellationToken);
    }

    private static async Task SendAsync(string module, string method, string endpoint, string data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Lcu.Connector(module, method, endpoint, data, cancellationToken);
        if (result is not HttpResponseMessage response)
            throw new InvalidOperationException("LCU is not ready.");

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"LCU returned {(int)response.StatusCode} for {endpoint}.");
        }
    }
}
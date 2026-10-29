using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using NLog;

namespace League_Account_Manager.Misc;

internal static class ProfileSettingsService
{
    // The client resets the profile when it starts, so the saved settings are
    // reapplied once per client launch and again only when they change.
    private static int appliedProcessId;
    private static string? appliedSignature;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var processes = Process.GetProcessesByName("LeagueClientUx");
                try
                {
                    if (Settings.settingsloaded.AutoApplyProfile)
                        await ApplyIfNeededAsync(processes, cancellationToken);
                }
                finally
                {
                    foreach (var process in processes)
                        process.Dispose();
                }

                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A rejected request must not be retried immediately: the client
                // answers a repeated profile write with 429, and retrying at once
                // turns the two requests into a flood.
                LogManager.GetCurrentClassLogger().Error(exception, "Error applying saved profile settings");
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
        }
    }

    private static async Task ApplyIfNeededAsync(Process[] processes, CancellationToken cancellationToken)
    {
        var processId = processes.Length > 0 ? processes[0].Id : 0;
        var signature = ProfileSignature(Settings.settingsloaded);
        if (processId != 0 && (processId != appliedProcessId || signature != appliedSignature))
        {
            await ApplyAsync(cancellationToken);
            appliedProcessId = processId;
            appliedSignature = signature;
        }
    }

    private static string ProfileSignature(Settings.settings1 saved) =>
        string.Join('\u001f', saved.ProfileStatusMessage, saved.ProfileQueue, saved.ProfileRank,
            saved.ProfileDivision, saved.ProfileIconId, saved.ProfileBackgroundId);

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
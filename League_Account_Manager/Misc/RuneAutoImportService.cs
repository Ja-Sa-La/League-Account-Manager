using System.Net.Http;
using Newtonsoft.Json.Linq;
using NLog;

namespace League_Account_Manager.Misc;

/// <summary>
///     Watches champion select and, when enabled, imports the selected champion's
///     recommended runes from the configured website exactly once per selection.
/// </summary>
internal static class RuneAutoImportService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var appliedChampion = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!Settings.settingsloaded.AutoImportRunes)
                {
                    appliedChampion = 0;
                }
                else
                {
                    var championId = await SelectedChampionAsync(cancellationToken);
                    if (championId == 0)
                        appliedChampion = 0;
                    else if (championId != appliedChampion)
                    {
                        await ImportSelectedChampionAsync(championId, cancellationToken);
                        appliedChampion = championId;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Automatic rune import is waiting for champion select");
                await DelayAfterErrorAsync(cancellationToken);
            }
        }
    }

    private static async Task ImportSelectedChampionAsync(int championId, CancellationToken cancellationToken)
    {
        var saved = Settings.settingsloaded;
        var page = await RunePageService.ImportAsync(saved.RuneImportSource, championId,
            saved.RuneImportRole, cancellationToken);
        await RunePageService.ApplyAsync(page, cancellationToken);
        DebugConsole.WriteLine($"[Runes] Automatically imported {page.Name} from {saved.RuneImportSource}.");
        Logger.Info("Automatically imported rune page {PageName}", page.Name);
    }

    private static async Task<int> SelectedChampionAsync(CancellationToken cancellationToken)
    {
        var result = await Lcu.Connector("league", "get", "/lol-champ-select/v1/session", "", cancellationToken);
        if (result is not HttpResponseMessage response)
            return 0;

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return 0;

            var session = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var localCell = session["localPlayerCellId"]?.Value<int>() ?? -1;
            var team = session["myTeam"] as JArray;
            var player = team?.FirstOrDefault(member => member?["cellId"]?.Value<int>() == localCell);
            return player?["championId"]?.Value<int>() ?? 0;
        }
    }

    private static async Task DelayAfterErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

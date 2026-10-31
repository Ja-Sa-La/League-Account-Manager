using NLog;

namespace League_Account_Manager.Misc;

/// <summary>
///     Watches Valorant agent select and equips the skins saved for the agent the local
///     player selects, once per selection.
/// </summary>
internal static class ValorantLoadoutAutoApplyService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        string? appliedAgent = null;
        var restoreDefault = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!Settings.settingsloaded.AutoApplyValorantLoadout)
                {
                    appliedAgent = null;
                    restoreDefault = false;
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (ValorantLoadoutService.IsInAgentSelect())
                {
                    appliedAgent = await WatchOnceAsync(appliedAgent, () => restoreDefault = true,
                        cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                appliedAgent = null;
                if (restoreDefault && ValorantLoadoutService.ReadGamePhase() == ValorantLoadoutService.GamePhase.InGame)
                {
                    await RestoreDefaultAsync(cancellationToken).ConfigureAwait(false);
                    restoreDefault = false;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Automatic Valorant loadout apply is waiting for agent select");
                appliedAgent = null;
                await DelayAfterErrorAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> WatchOnceAsync(string? appliedAgent, Action markDefaultSaved,
        CancellationToken cancellationToken)
    {
        var session = await ValorantLoadoutService.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        var agentId = await ValorantLoadoutService.SelectedAgentIdAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(agentId))
            return null;
        if (string.Equals(agentId, appliedAgent, StringComparison.OrdinalIgnoreCase))
            return appliedAgent;

        var catalog = await ValorantLoadoutService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var saved = await ValorantLoadoutService.FindAgentLoadoutAsync(session.Puuid, agentId, cancellationToken)
            .ConfigureAwait(false);
        var guns = saved is { Guns.Count: > 0 } ? saved.Guns : catalog.StandardGuns();
        if (guns.Count == 0)
        {
            DebugConsole.WriteLine($"[ValorantLoadout] No saved loadout for agent {agentId}.");
            return agentId;
        }

        if (appliedAgent == null)
        {
            var current = await ValorantLoadoutService.GetLoadoutAsync(session, cancellationToken)
                .ConfigureAwait(false);
            await ValorantLoadoutService.SaveNamedLoadoutAsync(session.Puuid,
                ValorantLoadoutService.DefaultSetName, current.Guns, cancellationToken).ConfigureAwait(false);
            markDefaultSaved();
        }

        var applied = await ValorantLoadoutService.ApplyGunsAsync(session, guns, cancellationToken)
            .ConfigureAwait(false);
        var usingStandard = saved is not { Guns.Count: > 0 };
        DebugConsole.WriteLine(applied
            ? $"[ValorantLoadout] Equipped the {(usingStandard ? "standard" : "saved")} loadout for {catalog.AgentName(agentId)}."
            : $"[ValorantLoadout] The {(usingStandard ? "standard" : "saved")} loadout for {catalog.AgentName(agentId)} was already equipped.");

        return agentId;
    }

    private static async Task RestoreDefaultAsync(CancellationToken cancellationToken)
    {
        var session = await ValorantLoadoutService.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        var sets = await ValorantLoadoutService.GetNamedLoadoutsAsync(session.Puuid, cancellationToken)
            .ConfigureAwait(false);
        var fallback = sets.FirstOrDefault(set =>
            set.Name.Equals(ValorantLoadoutService.DefaultSetName, StringComparison.OrdinalIgnoreCase));
        if (fallback == null)
            return;

        await ValorantLoadoutService.ApplyNamedLoadoutAsync(session, fallback, cancellationToken)
            .ConfigureAwait(false);
        DebugConsole.WriteLine("[ValorantLoadout] Restored the default loadout.");
    }

    private static async Task DelayAfterErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

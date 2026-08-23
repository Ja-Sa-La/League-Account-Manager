using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Misc;

internal static class ChampSelectActionTiming
{
    internal static bool IsChampionAvailable(IReadOnlySet<int> availableChampionIds, int championId)
    {
        return availableChampionIds.Contains(-1) || availableChampionIds.Contains(championId);
    }

    internal static bool ShouldComplete(JToken? timer, int thresholdMs)
    {
        var remainingMs = GetRemainingMilliseconds(timer);
        return remainingMs.HasValue && remainingMs.Value <= thresholdMs;
    }

    internal static DateTimeOffset? CreateDeadline(JToken? timer, DateTimeOffset now)
    {
        var remainingMs = GetRemainingMilliseconds(timer);
        return remainingMs.HasValue ? now.AddMilliseconds(remainingMs.Value) : null;
    }

    internal static bool ShouldComplete(JToken? timer, DateTimeOffset? localDeadline, DateTimeOffset now,
        int thresholdMs)
    {
        if (localDeadline.HasValue && localDeadline.Value - now <= TimeSpan.FromMilliseconds(thresholdMs))
            return true;

        return ShouldComplete(timer, thresholdMs);
    }

    internal static bool ShouldCompleteAction(string actionType, string? timerPhase, bool completeImmediately,
        JToken? timer, DateTimeOffset? localDeadline, DateTimeOffset now, int thresholdMs)
    {
        if (string.Equals(timerPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
            return false;

        if (completeImmediately)
            return true;

        return ShouldComplete(timer, localDeadline, now, thresholdMs);
    }

    internal static int? GetRemainingMilliseconds(JToken? timer)
    {
        if (timer == null)
            return null;

        var adjustedTimeLeft = timer["adjustedTimeLeftInPhase"]?.Value<int?>();
        if (adjustedTimeLeft.HasValue)
            return adjustedTimeLeft.Value;

        var internalNow = timer["internalNowInEpochMs"]?.Value<long?>();
        var phaseEnd = timer["phaseEndTimeInEpochMs"]?.Value<long?>();
        if (internalNow.HasValue && phaseEnd.HasValue)
            return (int)Math.Clamp(phaseEnd.Value - internalNow.Value, int.MinValue, int.MaxValue);

        return timer["timeLeftInPhase"]?.Value<int?>();
    }
}
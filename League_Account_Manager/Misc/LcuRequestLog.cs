namespace League_Account_Manager.Misc;

internal sealed record LcuRequestRecord(
    long Id,
    DateTimeOffset Timestamp,
    string Target,
    string Method,
    string Endpoint,
    string RequestBody,
    int? StatusCode,
    string Status,
    string ResponseBody,
    long DurationMilliseconds,
    string? Error,
    string TrafficType = "REST",
    string EventType = "",
    string RequestHeaders = "",
    string Direction = "",
    string ResponseHeaders = "");

internal static partial class LcuRequestLog
{
    // Every REST call and every LCU WebSocket event used to be kept forever with its full body,
    // which grew the process to gigabytes. Unless the user turns the limit off, keep a bounded,
    // truncated window instead. The size of that window comes from settings.
    private const int UnlimitedBodyLength = 200_000;
    private static readonly Lock Sync = new();
    private static readonly List<LcuRequestRecord> Entries = [];
    private static long _nextId;

    internal static event EventHandler<LcuRequestRecord>? RequestCompleted;

    internal static IReadOnlyList<LcuRequestRecord> Snapshot()
    {
        lock (Sync)
            return Entries.ToArray();
    }

    internal static void Clear()
    {
        lock (Sync)
            Entries.Clear();
    }

    internal static LcuRequestRecord Add(string target, string method, string endpoint, string requestBody,
        int? statusCode, string status, string responseBody, long durationMilliseconds, string? error = null,
        string trafficType = "REST", string eventType = "", string requestHeaders = "", string direction = "",
        string responseHeaders = "")
    {
        var entry = new LcuRequestRecord(
            Interlocked.Increment(ref _nextId),
            DateTimeOffset.Now,
            target,
            method.ToUpperInvariant(),
            endpoint,
            Truncate(requestBody),
            statusCode,
            status,
            Truncate(responseBody),
            durationMilliseconds,
            error,
            trafficType,
            eventType,
            requestHeaders ?? string.Empty,
            direction,
            responseHeaders ?? string.Empty);

        lock (Sync)
        {
            Entries.Add(entry);
            TrimEntries();
        }

        var handlers = RequestCompleted;
        if (handlers != null)
            foreach (EventHandler<LcuRequestRecord> handler in handlers.GetInvocationList())
                try
                {
                    handler(null, entry);
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[LCU Tracker] Listener failed: {ex.Message}", ConsoleColor.Yellow);
                }

        return entry;
    }

    internal static LcuRequestRecord? Update(long id, int? statusCode, string status, string responseBody,
        long durationMilliseconds, string? error = null, string responseHeaders = "", string? direction = null)
    {
        LcuRequestRecord entry;
        lock (Sync)
        {
            var index = Entries.FindIndex(record => record.Id == id);
            if (index < 0)
                return null;

            entry = Entries[index] with
            {
                StatusCode = statusCode,
                Status = status,
                ResponseBody = Truncate(responseBody),
                DurationMilliseconds = durationMilliseconds,
                Error = error,
                ResponseHeaders = responseHeaders ?? string.Empty,
                Direction = direction ?? Entries[index].Direction
            };
            Entries[index] = entry;
        }

        var handlers = RequestCompleted;
        if (handlers != null)
            foreach (EventHandler<LcuRequestRecord> handler in handlers.GetInvocationList())
                try
                {
                    handler(null, entry);
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[LCU Tracker] Listener failed: {ex.Message}", ConsoleColor.Yellow);
                }

        return entry;
    }

    private static void TrimEntries()
    {
        if (!Settings.settingsloaded.LimitLcuTrafficLog)
            return;

        var maximumEntries = Settings.settingsloaded.LcuTrafficLogEntryLimit > 0
            ? Settings.settingsloaded.LcuTrafficLogEntryLimit
            : 300;
        if (Entries.Count > maximumEntries)
            Entries.RemoveRange(0, Entries.Count - maximumEntries);
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var bodyLimitKb = Settings.settingsloaded.LcuTrafficBodyLimitKb > 0
            ? Settings.settingsloaded.LcuTrafficBodyLimitKb
            : 16;
        var maximumBodyLength = Settings.settingsloaded.LimitLcuTrafficLog
            ? bodyLimitKb * 1024
            : UnlimitedBodyLength;
        return value.Length <= maximumBodyLength ? value : value[..maximumBodyLength] + "… (truncated)";
    }
}
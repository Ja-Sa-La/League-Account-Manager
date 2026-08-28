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
    private const int MaximumEntries = 1000;
    private const int MaximumBodyLength = 200_000;
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
            requestBody ?? string.Empty,
            statusCode,
            status,
            responseBody ?? string.Empty,
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
            if (Entries.Count > MaximumEntries)
                Entries.RemoveRange(0, Entries.Count - MaximumEntries);
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

}
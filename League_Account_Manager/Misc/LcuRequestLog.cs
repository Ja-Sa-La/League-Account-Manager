using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    string Direction = "");

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
        string trafficType = "REST", string eventType = "", string requestHeaders = "", string direction = "")
    {
        var entry = new LcuRequestRecord(
            Interlocked.Increment(ref _nextId),
            DateTimeOffset.Now,
            target,
            method.ToUpperInvariant(),
            RedactEndpoint(endpoint),
            RedactBody(requestBody),
            statusCode,
            status,
            RedactBody(responseBody),
            durationMilliseconds,
            RedactBody(error),
            trafficType,
            eventType,
            RedactHeaders(requestHeaders),
            direction);

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

    internal static string RedactBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value;
        try
        {
            var token = JToken.Parse(text);
            RedactToken(token);
            text = token.ToString(Formatting.Indented);
        }
        catch (JsonReaderException)
        {
            text = SensitiveValueRegex().Replace(text, "$1[REDACTED]");
        }

        return Truncate(text);
    }

    internal static string RedactEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        return Truncate(SensitiveQueryRegex().Replace(endpoint, "$1[REDACTED]"));
    }

    internal static string RedactHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
            return string.Empty;

        var redacted = headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line =>
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    return line;

                var name = line[..separator].Trim();
                return IsSensitiveName(name) || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
                    ? $"{name}: [REDACTED]"
                    : line;
            });

        return Truncate(string.Join(Environment.NewLine, redacted));
    }

    private static void RedactToken(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (IsSensitiveName(property.Name))
                    property.Value = "[REDACTED]";
                else
                    RedactToken(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
                RedactToken(item);
        }
    }

    private static bool IsSensitiveName(string name)
    {
        return name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("sessionkey", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaximumBodyLength
            ? value
            : value[..MaximumBodyLength] + Environment.NewLine + "[TRUNCATED]";
    }

    [GeneratedRegex("(?i)([?&](?:access_token|token|password|authorization|credential|secret)=)[^&]*")]
    private static partial Regex SensitiveQueryRegex();

    [GeneratedRegex("""(?i)("?(?:access[_-]?token|id[_-]?token|token|password|authorization|credential|secret)"?\s*[:=]\s*"?)[^"&\s,}]+""")]
    private static partial Regex SensitiveValueRegex();
}
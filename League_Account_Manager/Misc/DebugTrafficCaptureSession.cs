using System.Diagnostics;
using System.Text;

namespace League_Account_Manager.Misc;

internal sealed class DebugTrafficCaptureSession : IDisposable
{
    private readonly object _sync = new();
    private bool _started;
    private bool _disposed;

    internal void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;

            LcuRequestLog.RequestCompleted += OnRequestCompleted;
            _started = true;
        }

        DebugConsole.WriteLine("[Debug Capture] Session started. LCU REST and WebSocket capture is active.");
    }

    internal void CaptureHttp(string target, string method, string endpoint, string requestBody,
        int? statusCode, string status, string responseBody, long durationMilliseconds,
        string? error = null, string requestHeaders = "", string direction = "Incoming")
    {
        Capture("HTTP", target, method, endpoint, requestBody, statusCode, status, responseBody,
            durationMilliseconds, error, requestHeaders, direction);
    }

    internal void CaptureXmpp(string target, string direction, string payload, string endpoint = "chat")
    {
        Capture("XMPP", target, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? "SEND" : "RECEIVE",
            endpoint, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? payload : string.Empty,
            null, direction, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? string.Empty : payload,
            0, direction: direction);
    }

    internal void CaptureRms(string target, string direction, string endpoint, string payload)
    {
        Capture("RMS", target, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? "SEND" : "RECEIVE",
            endpoint, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? payload : string.Empty,
            null, direction, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? string.Empty : payload,
            0, direction: direction);
    }

    internal void CaptureRtmp(string target, string direction, string endpoint, ReadOnlySpan<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        Capture("RTMP", target, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? "SEND" : "RECEIVE",
            endpoint, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? text : string.Empty,
            null, direction, direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase) ? string.Empty : text,
            0, direction: direction);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_started)
                LcuRequestLog.RequestCompleted -= OnRequestCompleted;
            _started = false;
        }

        DebugConsole.WriteLine("[Debug Capture] Session stopped.");
    }

    private void Capture(string trafficType, string target, string method, string endpoint, string requestBody,
        int? statusCode, string status, string responseBody, long durationMilliseconds, string? error = null,
        string requestHeaders = "", string direction = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LcuRequestLog.Add(target, method, endpoint, requestBody, statusCode, status, responseBody,
            durationMilliseconds, error, trafficType, requestHeaders: requestHeaders, direction: direction);
    }

    private static void OnRequestCompleted(object? sender, LcuRequestRecord record)
    {
        if (record.TrafficType is "REST" or "WebSocket")
            return;

        DebugConsole.WriteLine(
            $"[Debug Capture] {record.TrafficType} {record.Direction} {record.Method} {record.Endpoint}");
    }
}

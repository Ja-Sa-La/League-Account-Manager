using System.IO;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Misc;

internal static class LcuWebSocketMonitor
{
    private static readonly Lock Sync = new();
    private static CancellationTokenSource? _lifetimeCancellation;
    private static Task? _monitorTask;

    internal static void Start()
    {
        lock (Sync)
        {
            if (_monitorTask is { IsCompleted: false })
                return;

            _lifetimeCancellation = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorAsync(_lifetimeCancellation.Token));
        }
    }

    internal static void Stop()
    {
        lock (Sync)
            _lifetimeCancellation?.Cancel();
    }

    private static async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var clientInfo = await Lcu.GetClientInfo().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clientInfo.LeaguePort) || clientInfo.LeaguePort == "0" ||
                    string.IsNullOrWhiteSpace(clientInfo.LeagueToken) || clientInfo.LeagueToken == "0")
                {
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ListenAsync(clientInfo.LeaguePort, clientInfo.LeagueToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[LCU Tracker] WebSocket connection failed: {ex.Message}", ConsoleColor.Yellow);
            }

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ListenAsync(string port, string authToken, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{authToken}"));
        socket.Options.SetRequestHeader("Authorization", $"Basic {credentials}");

        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/"), cancellationToken)
            .ConfigureAwait(false);

        const string subscriptionFrame = "[5,\"OnJsonApiEvent\"]";
        var subscription = Encoding.UTF8.GetBytes(subscriptionFrame);
        await socket.SendAsync(subscription, WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
        LcuRequestLog.Add("league", "SEND", "OnJsonApiEvent", subscriptionFrame, null, "Sent", string.Empty, 0,
            trafficType: "WebSocket", eventType: "Subscribe", direction: "Outgoing");

        var buffer = new byte[32 * 1024];
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (message.Length == 0)
                continue;

            ProcessMessage(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    internal static void ProcessMessage(string message)
    {
        try
        {
            var frame = JArray.Parse(message);
            if (frame.Count < 3 || frame[2] is not JObject payload)
                return;

            var uri = payload["uri"]?.ToString() ?? string.Empty;
            var eventType = payload["eventType"]?.ToString() ?? "Event";
            var data = payload["data"]?.ToString(Formatting.None) ?? string.Empty;
            LcuRequestLog.Add("league", "RECEIVE", uri, string.Empty, null, eventType, message, 0,
                trafficType: "WebSocket", eventType: eventType, direction: "Incoming");
        }
        catch (JsonReaderException)
        {
        }
    }
}
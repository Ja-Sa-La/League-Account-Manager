using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace League_Account_Manager.Misc;

internal sealed class DebugRmsTrafficProxy : IDisposable
{
    private HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<WebSocket, byte> _connections = new();
    private readonly Uri _upstream;
    private bool _disposed;

    internal DebugRmsTrafficProxy(string origin)
    {
        _upstream = new Uri(origin, UriKind.Absolute);
        if (_upstream.Scheme is not ("ws" or "wss"))
            throw new ArgumentException("RMS origin must be a WebSocket URI.", nameof(origin));
    }

    internal int Port { get; private set; }

    internal void Start()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                _listener = listener;
                Port = port;
                break;
            }
            catch (HttpListenerException) when (attempt < 4)
            {
                listener.Close();
            }
        }

        if (!_listener.IsListening)
            throw new HttpListenerException((int)HttpStatusCode.ServiceUnavailable, "Unable to start RMS proxy listener.");

        _ = Task.Run(RunAsync);
        DebugConsole.WriteLine($"[Debug Capture] RMS WebSocket proxy 127.0.0.1:{Port} -> {_upstream}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        foreach (var connection in _connections.Keys)
            try { connection.Abort(); } catch { }
        _listener.Close();
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = HandleAsync(context);
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[Debug Capture] RMS listener error: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 426;
            context.Response.Close();
            return;
        }

        WebSocketContext clientContext;
        try
        {
            clientContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[Debug Capture] RMS upgrade failed: {ex.Message}", ConsoleColor.Yellow);
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        using var client = clientContext.WebSocket;
        _connections.TryAdd(client, 0);
        using var server = new ClientWebSocket();
        foreach (var header in context.Request.Headers.AllKeys)
            if (header is not null && !IsManagedHeader(header))
                try { server.Options.SetRequestHeader(header, context.Request.Headers[header]); } catch { }

        var target = new UriBuilder(_upstream) { Path = context.Request.Url?.AbsolutePath ?? "/", Query = context.Request.Url?.Query ?? "" }.Uri;
        try
        {
            await server.ConnectAsync(target, _stop.Token).ConfigureAwait(false);
            using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var outgoing = PumpAsync(client, server, true, connectionStop.Token);
            var incoming = PumpAsync(server, client, false, connectionStop.Token);
            await Task.WhenAny(outgoing, incoming).ConfigureAwait(false);
            connectionStop.Cancel();
            try { await Task.WhenAll(outgoing, incoming).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugConsole.WriteLine($"[Debug Capture] RMS connection failed: {ex.Message}", ConsoleColor.Yellow);
        }
        finally
        {
            try { await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); } catch { }
            try { server.Abort(); } catch { }
            _connections.TryRemove(client, out _);
        }
    }

    private static async Task PumpAsync(WebSocket source, WebSocket destination, bool outgoing, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (source.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await source.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var payload = message.ToArray();
            var display = result.MessageType == WebSocketMessageType.Text
                ? Encoding.UTF8.GetString(payload)
                : TrafficPayloadDecoder.Decode(payload, null);
            LcuRequestLog.Add("riot", outgoing ? "SEND" : "RECEIVE", "/rms", outgoing ? display : string.Empty,
                null, outgoing ? "Outgoing" : "Incoming", outgoing ? string.Empty : display, 0,
                trafficType: "RMS", direction: outgoing ? "Outgoing" : "Incoming");
            await destination.SendAsync(payload, result.MessageType, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsManagedHeader(string name) => name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
}
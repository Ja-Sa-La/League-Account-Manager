using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace League_Account_Manager.Misc;

internal sealed class DebugRtmpTrafficProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _host;
    private readonly int _port;
    private readonly bool _upstreamTls;
    private bool _disposed;

    internal DebugRtmpTrafficProxy(string host, int port, bool upstreamTls = true)
    {
        _host = host;
        _port = port;
        _upstreamTls = upstreamTls;
    }

    internal int LocalPort { get; private set; }

    internal void Start()
    {
        _listener.Start();
        LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
        DebugConsole.WriteLine($"[Debug Capture] RTMP proxy 127.0.0.1:{LocalPort} -> {_host}:{_port}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(client), _stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[Debug Capture] RTMP listener error: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var upstream = new TcpClient())
        {
            try
            {
                await upstream.ConnectAsync(_host, _port, _stop.Token).ConfigureAwait(false);
                await using var clientStream = client.GetStream();
                await using var upstreamNetworkStream = upstream.GetStream();
                Stream upstreamStream = upstreamNetworkStream;
                if (_upstreamTls)
                {
                    var ssl = new SslStream(upstreamNetworkStream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(_host, null,
                        SslProtocols.Tls12 | SslProtocols.Tls13, false).ConfigureAwait(false);
                    upstreamStream = ssl;
                }

                using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                var outgoing = PumpAsync(clientStream, upstreamStream, true, connectionStop.Token);
                var incoming = PumpAsync(upstreamStream, clientStream, false, connectionStop.Token);
                await Task.WhenAny(outgoing, incoming).ConfigureAwait(false);
                connectionStop.Cancel();
                try { await Task.WhenAll(outgoing, incoming).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DebugConsole.WriteLine($"[Debug Capture] RTMP connection failed: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    private static async Task PumpAsync(Stream source, Stream destination, bool outgoing, CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) return;
            var payload = buffer[..count];
            var decoded = TrafficPayloadDecoder.Decode(payload, null);
            LcuRequestLog.Add("riot", outgoing ? "SEND" : "RECEIVE", "/rtmp", outgoing ? decoded : string.Empty,
                null, outgoing ? "Outgoing" : "Incoming", outgoing ? string.Empty : decoded, 0,
                trafficType: "RTMP", direction: outgoing ? "Outgoing" : "Incoming");
            await destination.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
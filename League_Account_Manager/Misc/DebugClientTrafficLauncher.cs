using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace League_Account_Manager.Misc;

internal sealed partial class DebugClientTrafficLauncher : IDisposable
{
    private const string ClientConfigBaseUrl = "https://clientconfig.rpg.riotgames.com";
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    });
    private readonly ConcurrentDictionary<string, ForwardProxy> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private ConfigProxy? _configProxy;
    private bool _disposed;

    internal async Task<Process> LaunchAsync(string riotClientPath, string arguments,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var configProxy = new ConfigProxy(this, _httpClient);
        await configProxy.StartAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
            _configProxy = configProxy;

        var startInfo = new ProcessStartInfo
        {
            FileName = riotClientPath,
            Arguments = $"--client-config-url=\"{configProxy.Url}\" {arguments}",
            WorkingDirectory = Path.GetDirectoryName(riotClientPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false
        };

        DebugConsole.WriteLine($"[Debug Capture] Launch args: {startInfo.Arguments}");
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Riot Client.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Stop();
        DebugConsole.WriteLine($"[Debug Capture] Riot Client started with PID {process.Id}; HTTP proxies are active.");
        return process;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _httpClient.Dispose();
    }

    private void Stop()
    {
        lock (_sync)
        {
            _configProxy?.Dispose();
            _configProxy = null;
        }

        foreach (var proxy in _proxies.Values)
            proxy.Dispose();
        _proxies.Clear();
    }

    private string RewriteConfig(string content)
    {
        return ServiceUrlRegex().Replace(content, match =>
        {
            var origin = match.Value.TrimEnd('/');
            if (origin.Equals(ClientConfigBaseUrl, StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("riotcdn", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            var proxy = _proxies.GetOrAdd(origin, key =>
            {
                var created = new ForwardProxy(key, _httpClient);
                created.Start();
                return created;
            });
            return $"http://127.0.0.1:{proxy.Port}";
        });
    }

    private sealed class ConfigProxy : IDisposable
    {
        private readonly DebugClientTrafficLauncher _owner;
        private readonly HttpClient _httpClient;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        internal ConfigProxy(DebugClientTrafficLauncher owner, HttpClient httpClient)
        {
            _owner = owner;
            _httpClient = httpClient;
        }

        internal string Url { get; private set; } = string.Empty;

        internal Task StartAsync(CancellationToken cancellationToken)
        {
            var port = GetFreePort();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
            DebugConsole.WriteLine($"[Debug Capture] Client config proxy listening at {Url}");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _stop.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (_listener.IsListening && !cancellationToken.IsCancellationRequested && !_stop.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                    var rawUrl = context.Request.RawUrl ?? "/";
                    using var response = await _httpClient.GetAsync(ClientConfigBaseUrl + rawUrl, cancellationToken)
                        .ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var rewritten = _owner.RewriteConfig(content);
                    var bytes = Encoding.UTF8.GetBytes(rewritten);
                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                    context.Response.ContentLength64 = bytes.LongLength;
                    await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    context.Response.Close();
                    DebugConsole.WriteLine($"[Debug Capture] CONFIG GET {rawUrl} -> {(int)response.StatusCode}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DebugConsole.WriteLine($"[Debug Capture] Config proxy error: {ex.Message}", ConsoleColor.Yellow);
                    if (context is not null)
                        try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
                }
            }
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class ForwardProxy : IDisposable
    {
        private readonly string _origin;
        private readonly HttpClient _httpClient;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        internal ForwardProxy(string origin, HttpClient httpClient)
        {
            _origin = origin;
            _httpClient = httpClient;
        }

        internal int Port { get; private set; }

        internal void Start()
        {
            Port = GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(RunAsync);
            DebugConsole.WriteLine($"[Debug Capture] HTTP proxy 127.0.0.1:{Port} -> {_origin}");
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _stop.Dispose();
        }

        private async Task RunAsync()
        {
            while (_listener.IsListening && !_stop.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                    await ForwardAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DebugConsole.WriteLine($"[Debug Capture] HTTP proxy error: {ex.Message}", ConsoleColor.Yellow);
                    if (context is not null)
                        try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
                }
            }
        }

        private async Task ForwardAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var body = await ReadBodyAsync(request).ConfigureAwait(false);
            var endpoint = request.RawUrl ?? "/";
            var url = _origin + (endpoint.StartsWith('/') ? endpoint : "/" + endpoint);
            using var outgoing = new HttpRequestMessage(new HttpMethod(request.HttpMethod), url);
            CopyHeaders(request, outgoing);
            if (body.Length > 0)
                outgoing.Content = new ByteArrayContent(body);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var headers = string.Join(Environment.NewLine,
                    request.Headers.AllKeys.Where(key => key is not null).Select(key => $"{key}: {request.Headers[key]}"));
                LcuRequestLog.Add("league", request.HttpMethod, endpoint, Encoding.UTF8.GetString(body), null,
                    "Pending", string.Empty, 0, trafficType: "HTTP", requestHeaders: headers, direction: "Outgoing");
                using var response = await _httpClient.SendAsync(outgoing, HttpCompletionOption.ResponseContentRead)
                    .ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                stopwatch.Stop();
                LcuRequestLog.Add("league", request.HttpMethod, endpoint, Encoding.UTF8.GetString(body),
                    (int)response.StatusCode, response.ReasonPhrase ?? response.StatusCode.ToString(),
                    Encoding.UTF8.GetString(responseBody), stopwatch.ElapsedMilliseconds,
                    trafficType: "HTTP", direction: "Incoming");
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                context.Response.ContentLength64 = responseBody.LongLength;
                await context.Response.OutputStream.WriteAsync(responseBody).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LcuRequestLog.Add("league", request.HttpMethod, endpoint, Encoding.UTF8.GetString(body), null,
                    "Failed", string.Empty, stopwatch.ElapsedMilliseconds, ex.Message,
                    trafficType: "HTTP", direction: "Incoming");
                context.Response.StatusCode = 502;
                context.Response.Close();
            }
        }

        private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
        {
            using var stream = new MemoryStream();
            await request.InputStream.CopyToAsync(stream).ConfigureAwait(false);
            return stream.ToArray();
        }

        private static void CopyHeaders(HttpListenerRequest source, HttpRequestMessage destination)
        {
            foreach (var key in source.Headers.AllKeys)
                if (key is not null && !key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    destination.Headers.TryAddWithoutValidation(key, source.Headers[key]);
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    [GeneratedRegex("https://[A-Za-z0-9.-]+(?::\\d+)?")]
    private static partial Regex ServiceUrlRegex();
}
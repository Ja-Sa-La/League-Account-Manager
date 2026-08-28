using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace League_Account_Manager.Misc;

internal sealed partial class DebugClientTrafficLauncher : IDisposable
{
    private const string ClientConfigBaseUrl = "https://clientconfig.rpg.riotgames.com";
    private readonly HttpClient _configHttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    });
    private readonly HttpClient _forwardHttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None
    });
    private readonly ConcurrentDictionary<string, ForwardProxy> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DebugRmsTrafficProxy> _rmsProxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DebugRtmpTrafficProxy> _rtmpProxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private ConfigProxy? _configProxy;
    private DebugXmppTrafficProxy? _xmppProxy;
    private bool _disposed;

    internal async Task<Process> LaunchAsync(string riotClientPath, string arguments,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();
        try
        {
            _xmppProxy = await DebugXmppTrafficProxy.CreateAsync(cancellationToken).ConfigureAwait(false);
            var configProxy = new ConfigProxy(this, _configHttpClient);
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
            DebugConsole.WriteLine($"[Debug Capture] Riot Client started with PID {process.Id}; HTTP, XMPP, RMS, and RTMP proxies are active.");
            return process;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _configHttpClient.Dispose();
        _forwardHttpClient.Dispose();
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
        foreach (var proxy in _rmsProxies.Values)
            proxy.Dispose();
        _rmsProxies.Clear();
        foreach (var proxy in _rtmpProxies.Values)
            proxy.Dispose();
        _rtmpProxies.Clear();
        _xmppProxy?.Dispose();
        _xmppProxy = null;
    }

    internal string RewriteConfig(string content)
    {
        content = RmsServiceUrlRegex().Replace(content, match =>
        {
            var origin = match.Value.TrimEnd('/');
            if (!origin.Contains("rms", StringComparison.OrdinalIgnoreCase))
                return match.Value;
            var proxy = _rmsProxies.GetOrAdd(origin, key =>
            {
                var created = new DebugRmsTrafficProxy(key);
                created.Start();
                return created;
            });
            return $"ws://127.0.0.1:{proxy.Port}";
        });
        content = RewriteRtmpSettings(content);
        content = RewriteXmppSettings(content);
        return ServiceUrlRegex().Replace(content, match =>
        {
            var origin = match.Value.TrimEnd('/');
            if (IsExcludedOrigin(origin))
                return match.Value;

            var proxy = _proxies.GetOrAdd(origin, key =>
            {
                var created = new ForwardProxy(key, _forwardHttpClient);
                created.Start();
                return created;
            });
            return $"http://127.0.0.1:{proxy.Port}";
        });
    }

    private string RewriteXmppSettings(string content)
    {
        if (_xmppProxy is null)
            return content;

        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch { return content; }

        if (root is not JsonObject config || config["chat.host"] is not JsonValue hostValue ||
            !hostValue.TryGetValue<string>(out var host) || string.IsNullOrWhiteSpace(host) ||
            config["chat.port"] is not JsonValue portValue || !portValue.TryGetValue<int>(out var port) || port <= 0)
            return content;

        if (config["chat.affinity.enabled"]?.GetValue<bool>() == true && config["chat.affinities"] is JsonObject affinities)
        {
            foreach (var affinity in affinities.ToList())
                affinities[affinity.Key] = DebugXmppTrafficProxy.LocalhostDomain;
        }

        _xmppProxy.SetUpstream(host, port);
        config["chat.host"] = DebugXmppTrafficProxy.LocalhostDomain;
        config["chat.port"] = _xmppProxy.Port;
        config["chat.use_tls.enabled"] = true;
        return config.ToJsonString();
    }

    private string RewriteRtmpSettings(string content)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch { return content; }

        if (root is not JsonObject config || config["lcds.lcds_host"] is not JsonValue hostValue ||
            !hostValue.TryGetValue<string>(out var host) || string.IsNullOrWhiteSpace(host) ||
            config["lcds.lcds_port"] is not JsonValue portValue || !portValue.TryGetValue<int>(out var port) || port <= 0)
            return content;

        var key = $"{host}:{port}";
        var proxy = _rtmpProxies.GetOrAdd(key, _ =>
        {
            var created = new DebugRtmpTrafficProxy(host, port);
            created.Start();
            return created;
        });
        config["lcds.lcds_host"] = "127.0.0.1";
        config["lcds.lcds_port"] = proxy.LocalPort;
        config["lcds.use_tls"] = false;
        return config.ToJsonString();
    }

    private static bool IsExcludedOrigin(string origin)
    {
        return origin.Equals(ClientConfigBaseUrl, StringComparison.OrdinalIgnoreCase) ||
               origin.Equals("https://auth.riotgames.com", StringComparison.OrdinalIgnoreCase) ||
               origin.Equals("https://authenticate.riotgames.com", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("riotcdn", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               origin.Contains("%1", StringComparison.OrdinalIgnoreCase);
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
                    _ = HandleAsync(context, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && _listener.IsListening)
                {
                    DebugConsole.WriteLine($"[Debug Capture] Config proxy error: {ex.Message}", ConsoleColor.Yellow);
                    if (context is not null)
                        try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                    var rawUrl = context.Request.RawUrl ?? "/";
                    using var configRequest = new HttpRequestMessage(HttpMethod.Get, ClientConfigBaseUrl + rawUrl);
                    CopyHeaderIfPresent(context.Request, configRequest, "User-Agent");
                    CopyHeaderIfPresent(context.Request, configRequest, "Authorization");
                    CopyHeaderIfPresent(context.Request, configRequest, "X-Riot-Entitlements-JWT");
                    CopyHeaderIfPresent(context.Request, configRequest, "X-Riot-ClientPlatform");
                    CopyHeaderIfPresent(context.Request, configRequest, "X-Riot-ClientVersion");
                    using var response = await _httpClient.SendAsync(configRequest, cancellationToken)
                        .ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var rewritten = _owner.RewriteConfig(content);
                    var bytes = Encoding.UTF8.GetBytes(rewritten);
                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                    CopyResponseHeaders(response, context.Response);
                    context.Response.ContentLength64 = bytes.LongLength;
                    await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    context.Response.Close();
                    DebugConsole.WriteLine($"[Debug Capture] CONFIG GET {rawUrl} -> {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DebugConsole.WriteLine($"[Debug Capture] Config proxy error: {ex.Message}", ConsoleColor.Yellow);
                try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
            }
        }

        private static void CopyHeaderIfPresent(HttpListenerRequest source, HttpRequestMessage destination,
            string name)
        {
            var value = source.Headers[name];
            if (!string.IsNullOrWhiteSpace(value))
                destination.Headers.TryAddWithoutValidation(name, value);
        }

        private static void CopyResponseHeaders(HttpResponseMessage source, HttpListenerResponse destination)
        {
            foreach (var header in source.Headers.Concat(source.Content.Headers))
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Content-MD5", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("ETag", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Server", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    destination.Headers[header.Key] = string.Join(", ", header.Value);
                }
                catch (ArgumentException)
                {
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
                    _ = ForwardAsync(context);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && _listener.IsListening)
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
            if (request.HasEntityBody || HasContentHeaders(request))
            {
                outgoing.Content = new ByteArrayContent(body);
                CopyContentHeaders(request, outgoing.Content);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var headers = string.Join(Environment.NewLine,
                    request.Headers.AllKeys.Where(key => key is not null).Select(key => $"{key}: {request.Headers[key]}"));
                var requestBody = TrafficPayloadDecoder.Decode(body, outgoing.Content?.Headers);
                LcuRequestLog.Add("league", request.HttpMethod, endpoint, requestBody, null,
                    "Pending", string.Empty, 0, trafficType: "HTTP", requestHeaders: headers, direction: "Outgoing");
                using var response = await _httpClient.SendAsync(outgoing, HttpCompletionOption.ResponseContentRead)
                    .ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                stopwatch.Stop();
                var responseHeaders = FormatHeaders(response);
                var decodedResponseBody = TrafficPayloadDecoder.Decode(responseBody, response.Content.Headers);
                LcuRequestLog.Add("league", request.HttpMethod, endpoint, requestBody,
                    (int)response.StatusCode, response.ReasonPhrase ?? response.StatusCode.ToString(),
                    decodedResponseBody, stopwatch.ElapsedMilliseconds,
                    trafficType: "HTTP", responseHeaders: responseHeaders, direction: "Incoming");
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.StatusDescription = response.ReasonPhrase ?? response.StatusCode.ToString();
                CopyResponseHeaders(response, context.Response);
                var suppressBody = request.HttpMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
                                   response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified;
                if (!suppressBody)
                    context.Response.ContentLength64 = responseBody.LongLength;
                if (!suppressBody)
                    await context.Response.OutputStream.WriteAsync(responseBody).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LcuRequestLog.Add("league", request.HttpMethod, endpoint,
                    TrafficPayloadDecoder.Decode(body, outgoing.Content?.Headers), null,
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
                    !IsHopByHopHeader(key) && !key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    destination.Headers.TryAddWithoutValidation(key, source.Headers[key]);
        }

        private static bool HasContentHeaders(HttpListenerRequest request)
        {
            return request.Headers.AllKeys.Any(key =>
                key is not null && key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase));
        }

        private static void CopyContentHeaders(HttpListenerRequest source, HttpContent destination)
        {
            foreach (var key in source.Headers.AllKeys)
                if (key is not null && key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    destination.Headers.TryAddWithoutValidation(key, source.Headers[key]);
        }

        private static void CopyResponseHeaders(HttpResponseMessage source, HttpListenerResponse destination)
        {
            foreach (var header in source.Headers.Concat(source.Content.Headers))
            {
                if (IsHopByHopHeader(header.Key) || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var value in header.Value)
                {
                    try
                    {
                        if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            destination.ContentType = value;
                        else if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
                            destination.RedirectLocation = value;
                        else
                            destination.Headers.Add(header.Key, value);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                    }
                }
            }
        }

        private static string FormatHeaders(HttpResponseMessage response)
        {
            return string.Join(Environment.NewLine, response.Headers.Concat(response.Content.Headers)
                .SelectMany(header => header.Value.Select(value => $"{header.Key}: {value}")));
        }

        private static bool IsHopByHopHeader(string name)
        {
            return name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    [GeneratedRegex("https?://[A-Za-z0-9.%_-]+(?::\\d+)?")]
    private static partial Regex ServiceUrlRegex();

    [GeneratedRegex("wss?://[A-Za-z0-9.%_-]+(?::\\d+)?")]
    private static partial Regex RmsServiceUrlRegex();
}
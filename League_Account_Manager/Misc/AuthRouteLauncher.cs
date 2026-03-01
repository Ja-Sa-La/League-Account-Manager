using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace League_Account_Manager.Misc;

internal sealed class AuthRouteLauncher
{
    private const string RiotClientConfigBaseUrl = "https://clientconfig.rpg.riotgames.com";
    private const int RsoAuthenticatorPort = 58573;
    private const string RsoAuthenticatorUrl = "http://localhost:58573";

    private readonly ConcurrentDictionary<int, ClientConfigProxy> ActiveProxies = new();
    private readonly HttpClient SharedHttpClient = new();
    private long _configRequestCounter;

    public async Task<Process> LaunchRiotClientWithTokenCapture(string riotClientPath,
        bool? persistLogin = false,
        bool launchLeague = true,
        string patchline = "live",
        string? extraRiotClientArgs = null,
        CancellationToken cancellationToken = default)
    {
        var rsoAuthenticatorProxy = new AuthProxy(RsoAuthenticatorPort, "rso-authenticator");
        await rsoAuthenticatorProxy.StartAsync(cancellationToken, persistLogin);

        var configProxy = await StartClientConfigProxyAsync(SharedHttpClient, rsoAuthenticatorProxy, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = riotClientPath,
            UseShellExecute = false,
            Arguments = $"--client-config-url=\"{configProxy.ProxyUrl}\""
        };

        if (launchLeague)
            startInfo.Arguments += $" --launch-product=league_of_legends --launch-patchline={patchline}";

        if (!string.IsNullOrWhiteSpace(extraRiotClientArgs))
            startInfo.Arguments += $" {extraRiotClientArgs}";

        DebugConsole.WriteLine($"[AuthRouteLauncher] Launch args: {startInfo.Arguments}");

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Riot Client.");

        ActiveProxies[process.Id] = configProxy;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (ActiveProxies.TryRemove(process.Id, out var proxy))
            {
                DebugConsole.WriteLine($"[AuthRouteLauncher] Riot client exited ({process.Id}). Disposing proxies.");
                proxy.Dispose();
            }

            rsoAuthenticatorProxy.Dispose();
        };

        DebugConsole.WriteLine(
            $"[AuthRouteLauncher] Riot client started with PID {process.Id}. Config proxy at {configProxy.ProxyUrl}");
        return process;
    }

    private Task<ClientConfigProxy> StartClientConfigProxyAsync(HttpClient httpClient, AuthProxy authProxy,
        CancellationToken cancellationToken)
    {
        var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        DebugConsole.WriteLine($"[AuthRouteLauncher] Config proxy listening on {prefix}");

        var proxy = new ClientConfigProxy(listener, prefix);

        _ = Task.Run(async () =>
        {
            while (listener.IsListening && !cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync();
                    var requestId = Interlocked.Increment(ref _configRequestCounter);
                    var rawUrl = ctx.Request.RawUrl ?? "/";
                    var upstreamUrl = RiotClientConfigBaseUrl + rawUrl;
                    DebugConsole.WriteLine(
                        $"[AuthRouteLauncher] Config request #{requestId}: {ctx.Request.HttpMethod} {rawUrl}");

                    using var req = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
                    CopyHeaderIfPresent(ctx.Request, req, "user-agent", "User-Agent");
                    CopyHeaderIfPresent(ctx.Request, req, "x-riot-entitlements-jwt", "X-Riot-Entitlements-JWT");
                    CopyHeaderIfPresent(ctx.Request, req, "authorization", "Authorization");

                    using var res = await httpClient.SendAsync(req, cancellationToken);
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    DebugConsole.WriteLine($"[AuthRouteLauncher] Config response #{requestId}: {(int)res.StatusCode}");

                    var patchedContent = TryPatchConfigForAuthRoute(content, authProxy);

                    ctx.Response.StatusCode = (int)res.StatusCode;
                    ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";

                    var payload = Encoding.UTF8.GetBytes(patchedContent);
                    ctx.Response.ContentLength64 = payload.LongLength;
                    await ctx.Response.OutputStream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                    ctx.Response.Close();
                    DebugConsole.WriteLine(
                        $"[AuthRouteLauncher] Config request #{requestId} completed, bytes={payload.Length}");
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[AuthRouteLauncher] Config proxy error: {ex.Message}");
                    if (ctx is not null)
                    {
                        ctx.Response.StatusCode = 502;
                        ctx.Response.Close();
                    }
                }
            }
        }, cancellationToken);

        return Task.FromResult(proxy);
    }

    private void CopyHeaderIfPresent(HttpListenerRequest src, HttpRequestMessage dst, string srcName, string dstName)
    {
        var value = src.Headers[srcName];
        if (!string.IsNullOrWhiteSpace(value))
            dst.Headers.TryAddWithoutValidation(dstName, value);
    }

    private string TryPatchConfigForAuthRoute(string content, AuthProxy authProxy)
    {
        if (string.IsNullOrWhiteSpace(content) || !LooksLikeJsonObject(content))
            return content;

        JsonNode? config;
        try
        {
            config = JsonNode.Parse(content);
        }
        catch
        {
            return content;
        }

        if (config is null)
            return content;

        var upstream = config["keystone.rso-authenticator.service_url"]?.GetValue<string>();
        var rsoAuthUrl = config["keystone.rso_auth.url"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(upstream))
            authProxy.SetUpstream(upstream);

        config["keystone.rso-authenticator.service_url"] = RsoAuthenticatorUrl;

        DebugConsole.WriteLine(
            $"[AuthRouteLauncher] Patched keystone auth routes -> {RsoAuthenticatorUrl}. rso_auth={rsoAuthUrl}");
        return config.ToJsonString();
    }

    private bool LooksLikeJsonObject(string content)
    {
        foreach (var ch in content)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            return ch == '{';
        }

        return false;
    }

    private int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class AuthProxy : IDisposable
    {
        private readonly HttpClient _httpClient = new();
        private readonly HttpListener _listener = new();
        private readonly string _name;
        private Uri? _upstreamBase;

        public AuthProxy(int port, string name)
        {
            _name = name;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Dispose()
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
            _httpClient.Dispose();
        }

        public void SetUpstream(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                _upstreamBase ??= uri;
        }

        public Task StartAsync(CancellationToken token, bool? persistLogin = false)
        {
            _listener.Start();
            DebugConsole.WriteLine($"[AuthRouteLauncher] {_name} proxy listening on {_listener.Prefixes.First()}");

            _ = Task.Run(async () =>
            {
                while (_listener.IsListening && !token.IsCancellationRequested)
                {
                    HttpListenerContext? ctx = null;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                        var rawUrl = ctx.Request.RawUrl ?? "/";
                        var bodyBytes = await ReadBodyBytesAsync(ctx.Request, token);
                        var bodyText = GetBodyPreview(bodyBytes, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                        var headers = FormatHeaders(ctx.Request.Headers);
                        DebugConsole.WriteLine(
                            $"[AuthRouteLauncher] {_name} request: {ctx.Request.HttpMethod} {rawUrl} headers={headers} body={bodyText}");

                        if (_upstreamBase is null)
                        {
                            ctx.Response.StatusCode = 502;
                            ctx.Response.Close();
                            DebugConsole.WriteLine(
                                $"[AuthRouteLauncher] {_name} response: status=502 upstream not configured.");
                            continue;
                        }

                        var upstreamUrl = new Uri(_upstreamBase, rawUrl);
                        using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), upstreamUrl);
                        CopyRequestHeaders(ctx.Request, req);
                        if (bodyBytes.Length > 0)
                        {
                            req.Content = new ByteArrayContent(bodyBytes);
                            if (!string.IsNullOrWhiteSpace(ctx.Request.ContentType))
                                req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                        }

                        using var res = await _httpClient.SendAsync(req, token);
                        var responseBytes = await res.Content.ReadAsByteArrayAsync(token);
                        var responseHeaders = FormatHeaders(res);
                        var responseText = GetDecodedResponseText(responseBytes, res);
                        DebugConsole.WriteLine(
                            $"[AuthRouteLauncher] {_name} response: status={(int)res.StatusCode} content-type={ctx.Response.ContentType} headers={responseHeaders} bytes={responseBytes.Length} body={responseText}");
                        if (ctx.Request.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase) &&
                            rawUrl.StartsWith("/api/v1/login", StringComparison.OrdinalIgnoreCase))
                        {
                            Utils.KillLeagueFunc();
                            DebugConsole.WriteLine(
                                $"[AuthRouteLauncher] {_name} /api/v1/login response decoded: {responseText}");
                            _ = ProxyLoginTokenManager.CaptureLoginTokenAsync(responseText, persistLogin);
                            Utils.KillLeagueFunc();
                            ctx.Response.Close();
                            continue;
                        }

                        ctx.Response.StatusCode = (int)res.StatusCode;
                        ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString()
                                                   ?? "application/json";
                        CopyResponseHeaders(res, ctx.Response);
                        ctx.Response.ContentLength64 = responseBytes.LongLength;
                        await ctx.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                        ctx.Response.Close();
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteLine($"[AuthRouteLauncher] {_name} proxy error: {ex.Message}");
                        if (ctx is not null)
                        {
                            ctx.Response.StatusCode = 500;
                            ctx.Response.Close();
                        }
                    }
                }
            }, token);

            return Task.CompletedTask;
        }

        private static async Task<byte[]> ReadBodyBytesAsync(HttpListenerRequest request, CancellationToken token)
        {
            if (!request.HasEntityBody)
                return Array.Empty<byte>();

            using var ms = new MemoryStream();
            await request.InputStream.CopyToAsync(ms, token);
            return ms.ToArray();
        }

        private static string GetBodyPreview(byte[] body, Encoding encoding)
        {
            if (body.Length == 0)
                return string.Empty;

            try
            {
                return encoding.GetString(body);
            }
            catch
            {
                return Convert.ToBase64String(body);
            }
        }

        private static string GetDecodedResponseText(byte[] body, HttpResponseMessage response)
        {
            if (body.Length == 0)
                return string.Empty;

            var encoding = GetEncodingFromContentType(response.Content.Headers.ContentType?.CharSet);
            var decodedBytes = TryDecodeContent(body, response.Content.Headers.ContentEncoding);
            return GetBodyPreview(decodedBytes, encoding);
        }

        private static Encoding GetEncodingFromContentType(string? charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
                return Encoding.UTF8;

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private static byte[] TryDecodeContent(byte[] body, IEnumerable<string> encodings)
        {
            if (!encodings.Any())
                return body;

            var decoded = body;
            foreach (var encoding in encodings) decoded = DecodeWithEncoding(decoded, encoding);

            return decoded;
        }

        private static byte[] DecodeWithEncoding(byte[] body, string encoding)
        {
            if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                return Decompress(body, stream => new GZipStream(stream, CompressionMode.Decompress));

            if (encoding.Equals("deflate", StringComparison.OrdinalIgnoreCase))
                return Decompress(body, stream => new DeflateStream(stream, CompressionMode.Decompress));

            if (encoding.Equals("br", StringComparison.OrdinalIgnoreCase))
                return Decompress(body, stream => new BrotliStream(stream, CompressionMode.Decompress));

            return body;
        }

        private static byte[] Decompress(byte[] body, Func<Stream, Stream> streamFactory)
        {
            try
            {
                using var input = new MemoryStream(body);
                using var decompressor = streamFactory(input);
                using var output = new MemoryStream();
                decompressor.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return body;
            }
        }

        private static void CopyRequestHeaders(HttpListenerRequest src, HttpRequestMessage dst)
        {
            foreach (var key in src.Headers.AllKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = src.Headers[key];
                if (key.Equals("baggage", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                    value = SanitizeBaggageHeader(value);

                dst.Headers.TryAddWithoutValidation(key, value);
            }
        }

        private static void CopyResponseHeaders(HttpResponseMessage src, HttpListenerResponse dst)
        {
            foreach (var header in src.Headers)
            {
                if (IsRestrictedResponseHeader(header.Key))
                    continue;

                dst.Headers[header.Key] = string.Join(",", header.Value);
            }

            foreach (var header in src.Content.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsRestrictedResponseHeader(header.Key))
                    continue;

                dst.Headers[header.Key] = string.Join(",", header.Value);
            }
        }

        private static bool IsRestrictedResponseHeader(string header)
        {
            return header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("TE", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
                   || header.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatHeaders(NameValueCollection headers)
        {
            if (headers.Count == 0)
                return "{}";

            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var key in headers.AllKeys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!first)
                    sb.Append(", ");

                sb.Append(key);
                sb.Append('=');
                sb.Append(headers[key]);
                first = false;
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string FormatHeaders(HttpResponseMessage response)
        {
            var entries = new List<string>();
            foreach (var header in response.Headers)
                entries.Add($"{header.Key}={string.Join(",", header.Value)}");

            foreach (var header in response.Content.Headers)
                entries.Add($"{header.Key}={string.Join(",", header.Value)}");

            if (entries.Count == 0)
                return "{}";

            return "{" + string.Join(", ", entries) + "}";
        }

        private static string SanitizeBaggageHeader(string value)
        {
            var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var replaced = false;
            for (var i = 0; i < items.Length; i++)
            {
                if (!items[i].StartsWith("rgapi.machineid=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var randomValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                items[i] = $"rgapi.machineid={randomValue}";
                replaced = true;
            }

            if (!replaced)
                return value;

            return string.Join(", ", items);
        }
    }

    private sealed class ClientConfigProxy : IDisposable
    {
        private readonly HttpListener _listener;

        public ClientConfigProxy(HttpListener listener, string proxyUrl)
        {
            _listener = listener;
            ProxyUrl = proxyUrl;
        }

        public string ProxyUrl { get; }

        public void Dispose()
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
    }
}
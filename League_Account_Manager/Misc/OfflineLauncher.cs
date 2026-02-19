using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml;


namespace League_Account_Manager.Misc
{

    internal  class OfflineLauncher
    {
        private const string RiotClientConfigBaseUrl = "https://clientconfig.rpg.riotgames.com";
        private const string GeoPasUrl = "https://riot-geo.pas.si.riotgames.com/pas/v1/service/chat";

        private  readonly HttpClient SharedHttpClient = new();
        private  readonly ConcurrentDictionary<int, ClientConfigProxy> ActiveProxies = new();
        private  long _configRequestCounter;

        public  async Task<Process> LaunchRiotOrLeagueOfflineAsync(string riotClientPath,
            bool launchLeague = true,
            string patchline = "live",
            string? extraRiotClientArgs = null,
            CancellationToken cancellationToken = default)
        {

            var chatProxy = new ChatProxy();
            await chatProxy.StartAsync(cancellationToken);
            var configProxy = await StartClientConfigProxyAsync(SharedHttpClient, chatProxy, cancellationToken);

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

            Console.WriteLine($"[OfflineLauncher] Launch args: {startInfo.Arguments}");

            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Riot Client.");

            ActiveProxies[process.Id] = configProxy;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (ActiveProxies.TryRemove(process.Id, out var proxy))
                {
                    Console.WriteLine($"[OfflineLauncher] Riot client exited ({process.Id}). Disposing proxies.");
                    proxy.Dispose();
                }

                chatProxy.Dispose();
            };

            Console.WriteLine($"[OfflineLauncher] Riot client started with PID {process.Id}. Config proxy at {configProxy.ProxyUrl}");
            return process;
        }

        private  Task<ClientConfigProxy> StartClientConfigProxyAsync(HttpClient httpClient, ChatProxy chatProxy, CancellationToken cancellationToken)
        {
            var listener = new HttpListener();
            var port = GetFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();

            Console.WriteLine($"[OfflineLauncher] Config proxy listening on {prefix}");

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
                        Console.WriteLine($"[OfflineLauncher] Config request #{requestId}: {ctx.Request.HttpMethod} {rawUrl}");

                        using var req = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
                        CopyHeaderIfPresent(ctx.Request, req, "user-agent", "User-Agent");
                        CopyHeaderIfPresent(ctx.Request, req, "x-riot-entitlements-jwt", "X-Riot-Entitlements-JWT");
                        CopyHeaderIfPresent(ctx.Request, req, "authorization", "Authorization");

                        using var res = await httpClient.SendAsync(req, cancellationToken);
                        var content = await res.Content.ReadAsStringAsync(cancellationToken);
                        Console.WriteLine($"[OfflineLauncher] Config response #{requestId}: {(int)res.StatusCode}");

                        var patchedContent = await TryPatchConfigForOfflineAsync(content, ctx.Request.Headers["authorization"], httpClient, chatProxy, rawUrl, cancellationToken);

                        ctx.Response.StatusCode = (int)res.StatusCode;
                        ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";

                        var payload = Encoding.UTF8.GetBytes(patchedContent);
                        ctx.Response.ContentLength64 = payload.LongLength;
                        await ctx.Response.OutputStream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                        ctx.Response.Close();
                        Console.WriteLine($"[OfflineLauncher] Config request #{requestId} completed, bytes={payload.Length}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OfflineLauncher] Config proxy error: {ex.Message}");
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

        private  void CopyHeaderIfPresent(HttpListenerRequest src, HttpRequestMessage dst, string srcName, string dstName)
        {
            var value = src.Headers[srcName];
            if (!string.IsNullOrWhiteSpace(value))
                dst.Headers.TryAddWithoutValidation(dstName, value);
        }

        private  async Task<string> TryPatchConfigForOfflineAsync(
            string content,
            string? authorizationHeader,
            HttpClient httpClient,
            ChatProxy chatProxy,
            string rawUrl,
            CancellationToken cancellationToken)
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

            var hasChatKeys = config["chat.host"] is not null || config["chat.port"] is not null || config["chat.affinities"] is not null;
            if (!hasChatKeys)
                return content;

            var chatHost = config["chat.host"]?.GetValue<string>();
            var chatPort = config["chat.port"]?.GetValue<int>() ?? 0;

            if ((config["chat.affinity.enabled"]?.GetValue<bool>() ?? false) && config["chat.affinities"] is not null && !string.IsNullOrWhiteSpace(authorizationHeader))
            {
                try
                {
                    using var pasRequest = new HttpRequestMessage(HttpMethod.Get, GeoPasUrl);
                    pasRequest.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
                    var pasJwt = await (await httpClient.SendAsync(pasRequest, cancellationToken)).Content.ReadAsStringAsync(cancellationToken);
                    var payload = pasJwt.Split('.')[1];
                    var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                    var affinity = JsonNode.Parse(json)?["affinity"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(affinity))
                    {
                        var affHost = config["chat.affinities"]?[affinity]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(affHost))
                            chatHost = affHost;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OfflineLauncher] Affinity lookup failed, using fallback host: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(chatHost) && chatPort > 0)
            {
                chatProxy.SetUpstream(chatHost, chatPort);
                config["chat.host"] = "127.0.0.1";
                config["chat.port"] = chatProxy.Port;
                if (config["chat.affinities"] is JsonObject affinities)
                {
                    foreach (var key in affinities.ToList())
                        affinities[key.Key] = "127.0.0.1";
                }

                if (config["chat.allow_bad_cert.enabled"] is not null)
                    config["chat.allow_bad_cert.enabled"] = true;

                Console.WriteLine($"[OfflineLauncher] Patched chat route {chatHost}:{chatPort} -> 127.0.0.1:{chatProxy.Port} for {rawUrl}");
            }

            return config.ToJsonString();
        }

        private  bool LooksLikeJsonObject(string content)
        {
            foreach (var ch in content)
            {
                if (char.IsWhiteSpace(ch))
                    continue;
                return ch == '{';
            }

            return false;
        }

        private  int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class ChatProxy : IDisposable
        {
            private readonly X509Certificate2 _certificate = CreateTemporaryCertificate();
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private string? _upstreamHost;
            private int _upstreamPort;
            private long _chatConnectionCounter;

            public int Port { get; private set; }

            public Task StartAsync(CancellationToken token)
            {
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Console.WriteLine($"[OfflineLauncher] Chat proxy listening on 127.0.0.1:{Port}");

                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        TcpClient? incoming = null;
                        try
                        {
                            incoming = await _listener.AcceptTcpClientAsync(token);
                            var connectionId = Interlocked.Increment(ref _chatConnectionCounter);
                            Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: accepted TCP client from {incoming.Client.RemoteEndPoint}");
                            _ = Task.Run(() => HandleConnectionAsync(incoming, connectionId, token), token);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[OfflineLauncher] Chat accept error: {ex.Message}");
                            incoming?.Dispose();
                        }
                    }
                }, token);

                return Task.CompletedTask;
            }

            public void SetUpstream(string host, int port)
            {
                _upstreamHost = host;
                _upstreamPort = port;
            }

            private async Task HandleConnectionAsync(TcpClient incoming, long connectionId, CancellationToken token)
            {
                if (string.IsNullOrWhiteSpace(_upstreamHost) || _upstreamPort <= 0)
                {
                    Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: upstream not initialized; dropping.");
                    incoming.Dispose();
                    return;
                }

                using (incoming){
                    using var incomingSsl = new SslStream(incoming.GetStream(), false);
                await incomingSsl.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
                Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: TLS accepted from client.");

                using var outgoing = new TcpClient(_upstreamHost, _upstreamPort);
                using var outgoingSsl = new SslStream(outgoing.GetStream(), false);
                await outgoingSsl.AuthenticateAsClientAsync(_upstreamHost);
                Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: connected upstream {_upstreamHost}:{_upstreamPort}.");

                var c2s = PumpClientToServerAsync(incomingSsl, outgoingSsl, connectionId, token);
                var s2c = PumpServerToClientAsync(outgoingSsl, incomingSsl, connectionId, token);
                await Task.WhenAny(c2s, s2c);
                }
                Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: connection finished.");
            }

            private  async Task PumpClientToServerAsync(SslStream incomingSsl, SslStream outgoingSsl, long connectionId, CancellationToken token)
            {
                var bytes = new byte[16384];
                while (!token.IsCancellationRequested)
                {
                    var read = await incomingSsl.ReadAsync(bytes.AsMemory(0, bytes.Length), token);
                    if (read <= 0)
                        break;

                    Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: C->S bytes={read}");

                    var text = Encoding.UTF8.GetString(bytes, 0, read);
                    // Rewrite outbound self presence updates to offline while preserving other chat traffic.
                    if (text.Contains("<presence", StringComparison.OrdinalIgnoreCase) && text.Contains("</presence>", StringComparison.OrdinalIgnoreCase))
                    {
                        var rewritten = RewritePresenceToOffline(text);
                        if (!ReferenceEquals(rewritten, text))
                        {
                            Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: rewrote outbound presence set to offline.");
                            var patched = Encoding.UTF8.GetBytes(rewritten);
                            await outgoingSsl.WriteAsync(patched.AsMemory(0, patched.Length), token);
                            continue;
                        }
                    }

                    await outgoingSsl.WriteAsync(bytes.AsMemory(0, read), token);
                }
            }


            private  async Task PumpServerToClientAsync(SslStream serverSsl, SslStream clientSsl, long connectionId, CancellationToken token)
            {
                var bytes = new byte[16384];
                var insertedStealthUser = false;
                const string rosterMarker = "<query xmlns='jabber:iq:riotgames:roster'>";

                while (!token.IsCancellationRequested)
                {
                    var read = await serverSsl.ReadAsync(bytes.AsMemory(0, bytes.Length), token);
                    if (read <= 0)
                        break;

                    var content = Encoding.UTF8.GetString(bytes, 0, read);
                    Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: S->C bytes={read}");

                    if (!insertedStealthUser && content.Contains(rosterMarker, StringComparison.Ordinal))
                    {
                        insertedStealthUser = true;
                        var stealthUser =
                            "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Stealth Mode Active' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                            "<group priority='9999'>System</group>" +
                            "<state>online</state>" +
                            "<id name='&#9;Stealth Mode Active' tagline='...'/>" +
                            "<lol name='&#9;Stealth Mode Active'/>" +
                            "<platforms><riot name='&#9;Stealth Mode Active' tagline='...'/></platforms>" +
                            "</item>";

                        content = content.Insert(content.IndexOf(rosterMarker, StringComparison.Ordinal) + rosterMarker.Length, stealthUser);
                        var patched = Encoding.UTF8.GetBytes(content);
                        await clientSsl.WriteAsync(patched.AsMemory(0, patched.Length), token);
                        Console.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: inserted 'Stealth Mode Active' roster user.");
                        continue;
                    }

                    await clientSsl.WriteAsync(bytes.AsMemory(0, read), token);
                }
            }

            private  string RewritePresenceToOffline(string content)
            {
                try
                {
                    var xml = XDocument.Load(new StringReader("<xml>" + content + "</xml>"));
                    var changed = false;

                    if (xml.Root is null)
                        return content;

                    foreach (var presence in xml.Root.Elements().Where(e => e.Name.LocalName == "presence"))
                    {
                        if (presence.Attribute("to") is not null)
                            continue;

                        var show = presence.Elements().FirstOrDefault(e => e.Name.LocalName == "show");
                        if (show is null)
                        {
                            presence.Add(new XElement("show", "offline"));
                        }
                        else
                        {
                            show.ReplaceNodes("offline");
                        }

                        var status = presence.Elements().FirstOrDefault(e => e.Name.LocalName == "status");
                        status?.Remove();

                        var games = presence.Elements().FirstOrDefault(e => e.Name.LocalName == "games");
                        if (games is not null)
                        {
                            foreach (var node in games.Elements().Where(e =>
                                         e.Name.LocalName is "league_of_legends" or "valorant" or "bacon" or "lion" or "keystone" or "riot_client").ToList())
                            {
                                node.Remove();
                            }

                            var lol = games.Elements().FirstOrDefault(e => e.Name.LocalName == "league_of_legends");
                            lol?.Element("st")?.ReplaceNodes("offline");
                        }

                        changed = true;
                    }

                    if (!changed)
                        return content;

                    var sb = new StringBuilder();
                    var xws = new XmlWriterSettings
                    {
                        OmitXmlDeclaration = true,
                        Encoding = Encoding.UTF8,
                        ConformanceLevel = ConformanceLevel.Fragment
                    };

                    using var xw = XmlWriter.Create(sb, xws);
                    foreach (var element in xml.Root.Elements())
                        element.WriteTo(xw);

                    xw.Flush();
                    return sb.ToString();
                }
                catch
                {
                    // Fallback for unexpected chunking/format: best-effort textual rewrite.
                    if (content.Contains("<presence", StringComparison.OrdinalIgnoreCase))
                        return content.Replace("<show>chat</show>", "<show>offline</show>", StringComparison.OrdinalIgnoreCase);

                    return content;
                }
            }

            public void Dispose()
            {
                _listener.Stop();
                _certificate.Dispose();
            }

            private static X509Certificate2 CreateTemporaryCertificate()
            {
                using var rsa = RSA.Create(2048);
                var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

                var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
                return new X509Certificate2(cert.Export(X509ContentType.Pfx));
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
}

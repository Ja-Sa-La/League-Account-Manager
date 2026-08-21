using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace League_Account_Manager.Misc;

internal class OfflineLauncher
{
    private const string RiotClientConfigBaseUrl = "https://clientconfig.rpg.riotgames.com";
    private const string GeoPasUrl = "https://riot-geo.pas.si.riotgames.com/pas/v1/service/chat";
    private const string LocalhostDomain = "localhost.leagueaccountmanager.xyz";
    private const string HostsEntryIp = "127.0.0.1";
    private const string HostsEntryComment = "# Localhost mapping used by League Account Manager offline launcher";
    private static readonly string CachedCertificatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "League Account Manager",
        "OfflineLauncher",
        "localhostCert.pfx");
    private static readonly string HostsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers/etc/hosts");
    private readonly ConcurrentDictionary<int, ClientConfigProxy> ActiveProxies = new();

    private readonly HttpClient SharedHttpClient = new();
    private long _configRequestCounter;

    public async Task<Process> LaunchRiotOrLeagueOfflineAsync(string riotClientPath,
        bool launchLeague = true,
        bool LaunchValo = false,
        string patchline = "live",
        string? extraRiotClientArgs = null,
        CancellationToken cancellationToken = default)
    {
        EnsureHostsEntry();

        var serverCertificate = await GetProxyCertificateAsync(cancellationToken);
        if (serverCertificate is null)
            throw new InvalidOperationException(
                "Offline launcher was unable to obtain the certificate required to proxy the chat connection.");

        var chatProxy = new ChatProxy(serverCertificate);
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
        else if (LaunchValo)
            startInfo.Arguments += $" --launch-product=valorant --launch-patchline={patchline}";

        if (!string.IsNullOrWhiteSpace(extraRiotClientArgs))
            startInfo.Arguments += $" {extraRiotClientArgs}";

        DebugConsole.WriteLine($"[OfflineLauncher] Launch args: {startInfo.Arguments}");

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Riot Client.");

        ActiveProxies[process.Id] = configProxy;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (ActiveProxies.TryRemove(process.Id, out var proxy))
            {
                DebugConsole.WriteLine($"[OfflineLauncher] Riot client exited ({process.Id}). Disposing proxies.");
                proxy.Dispose();
            }

            chatProxy.Dispose();
        };

        DebugConsole.WriteLine(
            $"[OfflineLauncher] Riot client started with PID {process.Id}. Config proxy at {configProxy.ProxyUrl}");
        return process;
    }

    private Task<ClientConfigProxy> StartClientConfigProxyAsync(HttpClient httpClient, ChatProxy chatProxy,
        CancellationToken cancellationToken)
    {
        var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        DebugConsole.WriteLine($"[OfflineLauncher] Config proxy listening on {prefix}");

        var proxy = new ClientConfigProxy(listener, prefix.TrimEnd('/'));

        _ = Task.Run(async () =>
        {
            while (listener.IsListening && !cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync();
                    var requestId = Interlocked.Increment(ref _configRequestCounter);
                    var rawUrl = NormalizeConfigPath(ctx.Request.RawUrl);
                    var upstreamUrl = RiotClientConfigBaseUrl + rawUrl;
                    DebugConsole.WriteLine(
                        $"[OfflineLauncher] Config request #{requestId}: {ctx.Request.HttpMethod} {rawUrl}");

                    using var req = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
                    CopyHeaderIfPresent(ctx.Request, req, "user-agent", "User-Agent");
                    CopyHeaderIfPresent(ctx.Request, req, "x-riot-entitlements-jwt", "X-Riot-Entitlements-JWT");
                    CopyHeaderIfPresent(ctx.Request, req, "authorization", "Authorization");

                    using var res = await httpClient.SendAsync(req, cancellationToken);
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    DebugConsole.WriteLine($"[OfflineLauncher] Config response #{requestId}: {(int)res.StatusCode}");

                    var patchedContent = await TryPatchConfigForOfflineAsync(content,
                        ctx.Request.Headers["authorization"], httpClient, chatProxy, rawUrl, cancellationToken);

                    ctx.Response.StatusCode = (int)res.StatusCode;
                    ctx.Response.ContentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";

                    var payload = Encoding.UTF8.GetBytes(patchedContent);
                    ctx.Response.ContentLength64 = payload.LongLength;
                    await ctx.Response.OutputStream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                    ctx.Response.Close();
                    DebugConsole.WriteLine(
                        $"[OfflineLauncher] Config request #{requestId} completed, bytes={payload.Length}");
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteLine($"[OfflineLauncher] Config proxy error: {ex.Message}");
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

    private static string NormalizeConfigPath(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return "/";

        return "/" + rawUrl.TrimStart('/');
    }

    private async Task<string> TryPatchConfigForOfflineAsync(
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

        var hasChatKeys = config["chat.host"] is not null || config["chat.port"] is not null ||
                          config["chat.affinities"] is not null;
        if (!hasChatKeys)
            return content;

        var chatHost = config["chat.host"]?.GetValue<string>();
        var chatPort = config["chat.port"]?.GetValue<int>() ?? 0;

        if ((config["chat.affinity.enabled"]?.GetValue<bool>() ?? false) && config["chat.affinities"] is not null &&
            !string.IsNullOrWhiteSpace(authorizationHeader))
            try
            {
                using var pasRequest = new HttpRequestMessage(HttpMethod.Get, GeoPasUrl);
                pasRequest.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
                var pasJwt =
                    await (await httpClient.SendAsync(pasRequest, cancellationToken)).Content.ReadAsStringAsync(
                        cancellationToken);
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
                DebugConsole.WriteLine($"[OfflineLauncher] Affinity lookup failed, using fallback host: {ex.Message}");
            }

        if (!string.IsNullOrWhiteSpace(chatHost) && chatPort > 0)
        {
            chatProxy.SetUpstream(chatHost, chatPort);
            config["chat.host"] = LocalhostDomain;
            config["chat.port"] = chatProxy.Port;
            if (config["chat.affinities"] is JsonObject affinities)
                foreach (var key in affinities.ToList())
                    affinities[key.Key] = LocalhostDomain;

            DebugConsole.WriteLine(
                $"[OfflineLauncher] Patched chat route {chatHost}:{chatPort} -> {LocalhostDomain}:{chatProxy.Port} for {rawUrl}");
        }

        return config.ToJsonString();
    }

    private async Task<X509Certificate2?> GetProxyCertificateAsync(CancellationToken cancellationToken)
    {
        var cachedCert = GetCachedCertificate();
        if (cachedCert is not null && cachedCert.NotAfter > DateTime.Now.AddDays(20))
        {
            DebugConsole.WriteLine(
                $"[OfflineLauncher] Using cached localhost certificate valid until {cachedCert.NotAfter:u}.");
            return cachedCert;
        }

        try
        {
            DebugConsole.WriteLine("[OfflineLauncher] Downloading updated localhost certificate.");
            using var httpClient = new HttpClient();
            var certBytes = await httpClient.GetByteArrayAsync("https://redirect.leagueaccountmanager.xyz/cert.pfx", cancellationToken);
            var certificate = new X509Certificate2(certBytes);

            if (!CertificateMatchesDomain(certificate, LocalhostDomain))
            {
                DebugConsole.WriteLine(
                    $"[OfflineLauncher] Downloaded certificate does not match expected domain {LocalhostDomain}.");
                certificate.Dispose();
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CachedCertificatePath)!);
            File.WriteAllBytes(CachedCertificatePath, certBytes);
            return certificate;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OfflineLauncher] Failed to obtain localhost certificate: {ex}");
            return null;
        }
    }

    private X509Certificate2? GetCachedCertificate()
    {
        if (!File.Exists(CachedCertificatePath))
            return null;

        try
        {
            var certificate = new X509Certificate2(File.ReadAllBytes(CachedCertificatePath));
            if (CertificateMatchesDomain(certificate, LocalhostDomain))
                return certificate;

            DebugConsole.WriteLine(
                $"[OfflineLauncher] Cached localhost certificate does not match expected domain {LocalhostDomain}; deleting cache.");
            certificate.Dispose();

            try
            {
                File.Delete(CachedCertificatePath);
            }
            catch (Exception deleteEx)
            {
                DebugConsole.WriteLine(
                    $"[OfflineLauncher] Failed to delete invalid cached localhost certificate: {deleteEx.Message}");
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OfflineLauncher] Failed to load cached localhost certificate: {ex.Message}");
            return null;
        }
    }

    private static bool CertificateMatchesDomain(X509Certificate2 certificate, string expectedDomain)
    {
        try
        {
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17")
                    continue;

                var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();

                while (sequence.HasData)
                {
                    var tag = sequence.PeekTag();
                    if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
                    {
                        var dnsName = sequence.ReadCharacterString(UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 2));
                        if (string.Equals(dnsName, expectedDomain, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    else
                    {
                        sequence.ReadEncodedValue();
                    }
                }

                return false;
            }

            var dnsNameFromCertificate = certificate.GetNameInfo(X509NameType.DnsName, false);
            if (string.Equals(dnsNameFromCertificate, expectedDomain, StringComparison.OrdinalIgnoreCase))
                return true;

            return certificate.Subject.Contains($"CN={expectedDomain}", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OfflineLauncher] Failed to validate certificate domain: {ex.Message}");
            return false;
        }
    }

    private void EnsureHostsEntry()
    {
        if (HostsEntryExists())
            return;

        var result = System.Windows.MessageBox.Show(
            $"Stealth login requires a hosts file entry for {LocalhostDomain}. If you press Yes, League Account Manager will try to add `{HostsEntryIp} {LocalhostDomain}` to your hosts file and may request administrator permissions. If you press No, stealth login will be canceled.",
            "League Account Manager",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Yes);

        if (result != System.Windows.MessageBoxResult.Yes)
            throw new InvalidOperationException("Offline launcher canceled because the hosts file entry is missing.");

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"$hostsPath = '{EscapePowerShellSingleQuotedString(HostsFilePath)}'; $entry = '{EscapePowerShellSingleQuotedString(HostsEntryIp)} {EscapePowerShellSingleQuotedString(LocalhostDomain)}'; $comment = '{EscapePowerShellSingleQuotedString(HostsEntryComment)}'; if (-not (Test-Path -LiteralPath $hostsPath)) {{ throw 'Hosts file not found.' }}; $content = Get-Content -LiteralPath $hostsPath -Raw; if ($content -notmatch '(?im)^\\s*127\\.0\\.0\\.1\\s+localhost\\.leagueaccountmanager\\.xyz(?:\\s|$)') {{ Add-Content -LiteralPath $hostsPath -Value \"`r`n$comment`r`n$entry\" }}\"",
                UseShellExecute = true,
                Verb = "runas"
            });

            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OfflineLauncher] Failed to elevate for hosts update: {ex.Message}");
        }

        if (HostsEntryExists())
            return;

        throw new InvalidOperationException(
            "Offline launcher was unable to add the required hosts file entry.");
    }

    private bool HostsEntryExists()
    {
        try
        {
            if (!File.Exists(HostsFilePath))
                return false;

            foreach (var line in File.ReadLines(HostsFilePath))
            {
                var content = line.Split('#', 2)[0].Trim();
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (!string.Equals(parts[0], HostsEntryIp, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (parts.Skip(1).Any(host => string.Equals(host, LocalhostDomain, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OfflineLauncher] Failed to read hosts file: {ex.Message}");
            return false;
        }
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''");
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

    private sealed class ChatProxy : IDisposable
    {
        private const string StealthUserJid = "41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net";

        private sealed class PresenceInjectionState
        {
            public bool InsertedStealthUser;
            public bool SentStealthPresence;
            public string? ValorantVersion;
        }

        private readonly X509Certificate2 _certificate;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private long _chatConnectionCounter;
        private volatile bool _disposed;
        private string? _upstreamHost;
        private int _upstreamPort;

        public ChatProxy(X509Certificate2 certificate)
        {
            _certificate = certificate;
        }

        public int Port { get; private set; }

        public void Dispose()
        {
            _disposed = true;
            _listener.Stop();
            _certificate.Dispose();
        }

        public Task StartAsync(CancellationToken token)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            DebugConsole.WriteLine($"[OfflineLauncher] Chat proxy listening on 127.0.0.1:{Port}");

            _ = Task.Run(async () =>
            {
                while (!_disposed && !token.IsCancellationRequested)
                {
                    TcpClient? incoming = null;
                    try
                    {
                        incoming = await _listener.AcceptTcpClientAsync(token);
                        var connectionId = Interlocked.Increment(ref _chatConnectionCounter);
                        DebugConsole.WriteLine(
                            $"[OfflineLauncher] Chat request #{connectionId}: accepted TCP client from {incoming.Client.RemoteEndPoint}");
                        _ = Task.Run(() => HandleConnectionAsync(incoming, connectionId, token), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException ex)
                        when (ex.Message.Contains("Not listening", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_disposed || token.IsCancellationRequested)
                            break;

                        DebugConsole.WriteLine($"[OfflineLauncher] Chat accept error: {ex.Message}");
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
                DebugConsole.WriteLine(
                    $"[OfflineLauncher] Chat request #{connectionId}: upstream not initialized; dropping.");
                incoming.Dispose();
                return;
            }

            using (incoming)
            {
                using var incomingSsl = new SslStream(incoming.GetStream(), false);
                await incomingSsl.AuthenticateAsServerAsync(_certificate, false,
                    SslProtocols.Tls12 | SslProtocols.Tls13, false);
                DebugConsole.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: TLS accepted from client.");

                using var outgoing = new TcpClient(_upstreamHost, _upstreamPort);
                using var outgoingSsl = new SslStream(outgoing.GetStream(), false);
                await outgoingSsl.AuthenticateAsClientAsync(_upstreamHost);
                DebugConsole.WriteLine(
                    $"[OfflineLauncher] Chat request #{connectionId}: connected upstream {_upstreamHost}:{_upstreamPort}.");

                var state = new PresenceInjectionState();
                var c2s = PumpClientToServerAsync(incomingSsl, outgoingSsl, connectionId, state, token);
                var s2c = PumpServerToClientAsync(outgoingSsl, incomingSsl, connectionId, state, token);
                await Task.WhenAny(c2s, s2c);
            }

            DebugConsole.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: connection finished.");
        }

        private async Task PumpClientToServerAsync(SslStream incomingSsl, SslStream outgoingSsl, long connectionId,
            PresenceInjectionState state, CancellationToken token)
        {
            var bytes = new byte[16384];
            while (!token.IsCancellationRequested)
            {
                var read = await incomingSsl.ReadAsync(bytes.AsMemory(0, bytes.Length), token);
                if (read <= 0)
                    break;

                DebugConsole.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: C->S bytes={read}");

                var text = Encoding.UTF8.GetString(bytes, 0, read);

                if (text.Contains(StealthUserJid, StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.WriteLine(
                        $"[OfflineLauncher] Chat request #{connectionId}: removed C->S payload targeting stealth user.");

                    if (state.InsertedStealthUser && !state.SentStealthPresence)
                        await SendStealthPresenceAsync(incomingSsl, connectionId, state, token);

                    continue;
                }

                // Rewrite outbound self presence updates to offline while preserving other chat traffic.
                if (text.Contains("<presence", StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("</presence>", StringComparison.OrdinalIgnoreCase))
                {
                    await TryCaptureValorantVersionAsync(text, state, connectionId, incomingSsl, token);
                    var rewritten = RewritePresenceToOffline(text);
                    if (!ReferenceEquals(rewritten, text))
                    {
                        DebugConsole.WriteLine(
                            $"[OfflineLauncher] Chat request #{connectionId}: rewrote outbound presence set to offline.");
                        var patched = Encoding.UTF8.GetBytes(rewritten);
                        await outgoingSsl.WriteAsync(patched.AsMemory(0, patched.Length), token);

                        if (state.InsertedStealthUser && !state.SentStealthPresence)
                            await SendStealthPresenceAsync(incomingSsl, connectionId, state, token);

                        continue;
                    }
                }

                await outgoingSsl.WriteAsync(bytes.AsMemory(0, read), token);

                if (state.InsertedStealthUser && !state.SentStealthPresence)
                    await SendStealthPresenceAsync(incomingSsl, connectionId, state, token);
            }
        }


        private async Task PumpServerToClientAsync(SslStream serverSsl, SslStream clientSsl, long connectionId,
            PresenceInjectionState state, CancellationToken token)
        {
            var bytes = new byte[16384];
            const string rosterMarker = "<query xmlns='jabber:iq:riotgames:roster'>";

            while (!token.IsCancellationRequested)
            {
                var read = await serverSsl.ReadAsync(bytes.AsMemory(0, bytes.Length), token);
                if (read <= 0)
                    break;

                var content = Encoding.UTF8.GetString(bytes, 0, read);
                DebugConsole.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: S->C bytes={read}");

                if (!state.InsertedStealthUser && content.Contains(rosterMarker, StringComparison.Ordinal))
                {
                    state.InsertedStealthUser = true;
                    var stealthUser =
                        "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Stealth Mode Active' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                        "<group priority='9999'>System</group>" +
                        "<state>online</state>" +
                        "<id name='&#9;Stealth Mode Active' tagline='...'/>" +
                        "<lol name='&#9;Stealth Mode Active'/>" +
                        "<platforms><riot name='&#9;Stealth Mode Active' tagline='...'/></platforms>" +
                        "</item>";

                    content = content.Insert(
                        content.IndexOf(rosterMarker, StringComparison.Ordinal) + rosterMarker.Length, stealthUser);
                    var patched = Encoding.UTF8.GetBytes(content);
                    await clientSsl.WriteAsync(patched.AsMemory(0, patched.Length), token);
                    DebugConsole.WriteLine(
                        $"[OfflineLauncher] Chat request #{connectionId}: inserted 'Stealth Mode Active' roster user.");

                    await SendStealthPresenceAsync(clientSsl, connectionId, state, token);
                    continue;
                }

                await clientSsl.WriteAsync(bytes.AsMemory(0, read), token);

                if (state.InsertedStealthUser && !state.SentStealthPresence)
                    await SendStealthPresenceAsync(clientSsl, connectionId, state, token);
            }
        }

        private async Task TryCaptureValorantVersionAsync(string content, PresenceInjectionState state, long connectionId,
            SslStream clientSsl, CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(state.ValorantVersion))
                return;

            var version = TryExtractValorantVersion(content);
            if (string.IsNullOrWhiteSpace(version))
                return;

            state.ValorantVersion = version;
            DebugConsole.WriteLine($"[OfflineLauncher] Chat request #{connectionId}: extracted VALORANT version '{version}'.");

            if (state.InsertedStealthUser)
                await SendStealthPresenceAsync(clientSsl, connectionId, state, token, force: true);
        }

        private static string? TryExtractValorantVersion(string content)
        {
            try
            {
                var xml = XDocument.Load(new StringReader("<xml>" + content + "</xml>"));
                if (xml.Root is null)
                    return null;

                foreach (var presence in xml.Root.Elements().Where(e => e.Name.LocalName == "presence"))
                {
                    var payload = presence
                        .Elements().FirstOrDefault(e => e.Name.LocalName == "games")?
                        .Elements().FirstOrDefault(e => e.Name.LocalName == "valorant")?
                        .Elements().FirstOrDefault(e => e.Name.LocalName == "p")?
                        .Value;

                    if (string.IsNullOrWhiteSpace(payload))
                        continue;

                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                    var json = JsonNode.Parse(decoded);
                    var version = json?["partyPresenceData"]?["partyClientVersion"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task SendStealthPresenceAsync(SslStream clientSsl, long connectionId, PresenceInjectionState state,
            CancellationToken token, bool force = false)
        {
            if (state.SentStealthPresence && !force)
                return;

            state.SentStealthPresence = true;

            var stanzaId = Guid.NewGuid();
            var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var valorantPresence = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                """
                {
                 "isValid": true,
                 "isIdle": false,
                 "queueId": "competitive",
                 "provisioningFlow": "Invalid",
                 "partyId": "00000000-0000-0000-0000-000000000000",
                 "partySize": 1,
                 "maxPartySize": 5,
                 "partyOwnerMatchScoreAllyTeam": 0,
                 "partyOwnerMatchScoreEnemyTeam": 0,
                  "premierPresenceData":
                  {
                      "rosterId": "",
                      "rosterName": "Stealth mode is active. Ignore any version mismatch warnings.",
                      "rosterTag": "Stealth Mode Active",
                      "rosterType": "VCT",
                      "division": 0,
                      "score": 0,
                      "plating": 0,
                      "showAura": false,
                      "showTag": true,
                      "showPlating": false
                  },
                  "matchPresenceData":
                  {
                      "sessionLoopState": "MENUS",
                      "provisioningFlow": "Invalid",
                      "matchMap": "",
                      "partyOwnerMatchScoreAllyTeam": 0,
                      "partyOwnerMatchScoreEnemyTeam": 0,
                      "isIdle": false
                  },
                 "partyPresenceData":
                 {
                     "partyId": "00000000-0000-0000-0000-000000000000",
                     "isPartyOwner": true,
                     "partyState": "DEFAULT",
                     "partyAccessibility": "CLOSED",
                     "partyLFM": false,
                     "partyClientVersion": "{VERSION}",
                     "partyVersion": 1768830115681,
                     "partySize": 1,
                     "queueEntryTime": "0001.01.01-00.00.00",
                     "isPartyCrossPlayEnabled": false,
                     "isPlayerCrossPlayEnabled": false,
                     "partyPrecisePlatformTypes": 1,
                     "customGameName": "Stealth Mode Active",
                     "customGameTeam": "",
                     "maxPartySize": 5,
                     "tournamentId": "",
                     "rosterId": "",
                     "partyOwnerSessionLoopState": "MENUS",
                     "partyOwnerMatchMap": "",
                     "partyOwnerProvisioningFlow": "Invalid",
                     "partyOwnerMatchScoreAllyTeam": 0,
                     "partyOwnerMatchScoreEnemyTeam": 0
                 },
                 "playerPresenceData":
                 {
                     "playerCardId": "83958320-4a43-27d4-d497-6ea181bef1aa",
                     "playerTitleId": "e3ca05a4-4e44-9afe-3791-7d96ca8f71fa",
                     "accountLevel": 999,
                     "competitiveTier": 0,
                     "leaderboardPosition": 0
                 }
                }
                """.Replace("{VERSION}", state.ValorantVersion ?? "unknown")));

            var presenceMessage =
                $"<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Stealth' id='b-{stanzaId}'>" +
                "<games>" +
                $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p><pty/></keystone>" +
                $"<riot_client><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>riot_client</s.p></riot_client>" +
                $"<league_of_legends><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><s.c>live</s.c><p>{{&quot;pty&quot;:true}}</p></league_of_legends>" +
                $"<valorant><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><s.r>PC</s.r><p>{valorantPresence}</p><pty/></valorant>" +
                $"<bacon><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
                "</games>" +
                "<show>chat</show>" +
                "<platform>riot</platform>" +
                "<status/>" +
                "</presence>";

            var payload = Encoding.UTF8.GetBytes(presenceMessage);
            await clientSsl.WriteAsync(payload.AsMemory(0, payload.Length), token);
            DebugConsole.WriteLine(
                $"[OfflineLauncher] Chat request #{connectionId}: {(force ? "resent" : "sent")} stealth fake presence.");
        }

        private string RewritePresenceToOffline(string content)
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
                        presence.Add(new XElement("show", "offline"));
                    else
                        show.ReplaceNodes("offline");

                    var status = presence.Elements().FirstOrDefault(e => e.Name.LocalName == "status");
                    status?.Remove();

                    var games = presence.Elements().FirstOrDefault(e => e.Name.LocalName == "games");
                    if (games is not null)
                    {
                        foreach (var node in games.Elements().ToList())
                            node.Remove();
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
                    return content.Replace("<show>chat</show>", "<show>offline</show>",
                        StringComparison.OrdinalIgnoreCase);

                return content;
            }
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
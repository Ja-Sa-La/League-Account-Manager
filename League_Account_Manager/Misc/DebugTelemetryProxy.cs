using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace League_Account_Manager.Misc;

/// <summary>
///     Captures the telemetry the Riot client sends to hosts that are pinned and therefore never
///     appear in the client config the other proxies rewrite. It points those names at 127.0.0.1 in
///     the hosts file and answers TLS on port 443 with a certificate minted per host by a local root
///     the client is told to trust, then forwards the request to the host's real address and decodes
///     the protobuf body into the traffic log.
/// </summary>
internal sealed class DebugTelemetryProxy : IDisposable
{
    internal static readonly string[] Hosts = ["data.riotgames.com", "telemetry.sgp.pvp.net", "ekg.riotgames.com"];

    private const int ListenPort = 443;
    private const string RootName = "CN=League Account Manager Telemetry Capture";

    private readonly string _certificateDirectory;
    private readonly Dictionary<string, IPAddress> _realAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _dnsClient = new();
    private X509Certificate2? _root;
    private TcpListener? _listener;
    private Task? _acceptLoop;

    internal DebugTelemetryProxy()
    {
        _certificateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Account Manager", "TelemetryCapture");
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_certificateDirectory);
        _root = LoadOrCreateRoot();
        TrustRoot(_root);

        foreach (var host in Hosts)
        {
            var address = await ResolveAsync(host, cancellationToken).ConfigureAwait(false);
            if (address is not null)
                _realAddresses[host] = address;
            else
                DebugConsole.WriteLine($"[Debug Capture] Could not resolve {host}; its telemetry will not be captured.",
                    ConsoleColor.Yellow);
        }

        if (_realAddresses.Count == 0)
            throw new InvalidOperationException("None of the telemetry hosts could be resolved.");

        WriteHostsEntries();
        _listener = new TcpListener(IPAddress.Loopback, ListenPort);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        DebugConsole.WriteLine(
            $"[Debug Capture] Telemetry proxy listening on 127.0.0.1:{ListenPort} for {string.Join(", ", _realAddresses.Keys)}.");
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        RemoveHostsEntries();
        _dnsClient.Dispose();
        _root?.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            { break; }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            string? host = null;
            try
            {
                using var ssl = new SslStream(client.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, name) => LeafFor(name),
                    ClientCertificateRequired = false
                }, _stop.Token).ConfigureAwait(false);
                host = ssl.TargetHostName;

                var request = await ReadRequestAsync(ssl).ConfigureAwait(false);
                if (request is null || string.IsNullOrEmpty(host))
                    return;

                var upstream = await ForwardAsync(host, request).ConfigureAwait(false);
                await WriteResponseAsync(ssl, upstream).ConfigureAwait(false);
                Log(host, request, upstream);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DebugConsole.WriteLine($"[Debug Capture] Telemetry proxy error ({host ?? "unknown host"}): {ex.Message}",
                    ConsoleColor.Yellow);
            }
        }
    }

    private async Task<UpstreamResponse> ForwardAsync(string host, HttpRequest request)
    {
        if (!_realAddresses.TryGetValue(host, out var address))
            return UpstreamResponse.Failed(502, "Telemetry host was not resolved.");

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address, 443, _stop.Token).ConfigureAwait(false);
        using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                  System.Security.Authentication.SslProtocols.Tls13
        }, _stop.Token).ConfigureAwait(false);

        var header = new StringBuilder();
        header.Append(request.Method).Append(' ').Append(request.Target).Append(" HTTP/1.1\r\n");
        header.Append("Host: ").Append(host).Append("\r\n");
        foreach (var (name, value) in request.Headers)
            if (!name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                header.Append(name).Append(": ").Append(value).Append("\r\n");
        header.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        await ssl.WriteAsync(headerBytes, _stop.Token).ConfigureAwait(false);
        if (request.Body.Length > 0)
            await ssl.WriteAsync(request.Body, _stop.Token).ConfigureAwait(false);

        return await ReadUpstreamAsync(ssl).ConfigureAwait(false);
    }

    private void Log(string host, HttpRequest request, UpstreamResponse response)
    {
        var url = $"https://{host}{request.Target}";
        var requestHeaders = string.Join(Environment.NewLine, request.Headers.Select(pair => $"{pair.Key}: {pair.Value}"));
        var requestBody = DecodeBody(request.Body, Header(request.Headers, "Content-Type"),
            Header(request.Headers, "Content-Encoding"), request.Target);
        var responseBody = DecodeBody(response.Body, Header(response.Headers, "Content-Type"),
            Header(response.Headers, "Content-Encoding"), request.Target);
        LcuRequestLog.Add("riot", request.Method, url, requestBody, response.StatusCode, response.Reason,
            responseBody, response.DurationMilliseconds, response.Error, trafficType: "Telemetry",
            requestHeaders: requestHeaders, direction: "Outgoing",
            responseHeaders: string.Join(Environment.NewLine,
                response.Headers.Select(pair => $"{pair.Key}: {pair.Value}")));
    }

    private static string DecodeBody(byte[] body, string? contentType, string? contentEncoding, string path)
    {
        if (body.Length == 0)
            return string.Empty;

        if (contentEncoding is not null && contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) &&
            body.Length > 2 && body[0] == 0x1F && body[1] == 0x8B)
            try
            {
                using var input = new MemoryStream(body);
                using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                body = output.ToArray();
            }
            catch (InvalidDataException) { }

        if (TelemetryProtobufDecoder.IsProtobuf(contentType))
        {
            var decoded = TelemetryProtobufDecoder.TryDecode(body, path);
            if (decoded is not null)
                return decoded;
        }

        return TrafficPayloadDecoder.Decode(body);
    }

    private static string? Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value : null;

    private X509Certificate2 LeafFor(string? host)
    {
        host = string.IsNullOrWhiteSpace(host) ? Hosts[0] : host;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var usage = new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false);
        request.CertificateExtensions.Add(usage);

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        using var issued = request.Create(_root!, DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30), serial);
        return issued.CopyWithPrivateKey(key);
    }

    private X509Certificate2 LoadOrCreateRoot()
    {
        var path = Path.Combine(_certificateDirectory, "telemetry-root.pfx");
        if (File.Exists(path))
            try { return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable); }
            catch (CryptographicException) { }

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(RootName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var usage = new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true);
        request.CertificateExtensions.Add(usage);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var exportable = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
        File.WriteAllBytes(path, exportable.Export(X509ContentType.Pfx));
        return exportable;
    }

    private static void TrustRoot(X509Certificate2 root)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        if (!store.Certificates.Find(X509FindType.FindByThumbprint, root.Thumbprint, false).Any())
        {
            store.Add(root);
            DebugConsole.WriteLine("[Debug Capture] Installed the telemetry capture root certificate for the current user.");
        }
    }

    private async Task<IPAddress?> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://1.1.1.1/dns-query?name={Uri.EscapeDataString(host)}&type=A");
            request.Headers.TryAddWithoutValidation("accept", "application/dns-json");
            using var response = await _dnsClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Answer", out var answers))
                foreach (var answer in answers.EnumerateArray())
                    if (answer.TryGetProperty("data", out var data) &&
                        IPAddress.TryParse(data.GetString(), out var address))
                        return address;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            DebugConsole.WriteLine($"[Debug Capture] DNS lookup for {host} failed: {ex.Message}", ConsoleColor.Yellow);
        }

        return null;
    }

    private void WriteHostsEntries()
    {
        var path = HostsFile();
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        foreach (var host in _realAddresses.Keys)
        {
            var entry = $"127.0.0.1 {host}";
            if (!lines.Any(line => line.Trim().Equals(entry, StringComparison.OrdinalIgnoreCase)))
                lines.Add(entry);
        }

        File.WriteAllLines(path, lines);
    }

    private void RemoveHostsEntries()
    {
        try
        {
            var path = HostsFile();
            if (!File.Exists(path))
                return;
            var kept = File.ReadAllLines(path)
                .Where(line => !_realAddresses.Keys.Any(host =>
                    line.Trim().Equals($"127.0.0.1 {host}", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            File.WriteAllLines(path, kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.WriteLine($"[Debug Capture] Could not remove the telemetry hosts entries: {ex.Message}",
                ConsoleColor.Yellow);
        }
    }

    private static string HostsFile() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    private static async Task<HttpRequest?> ReadRequestAsync(Stream stream)
    {
        var header = await ReadUntilAsync(stream, Encoding.ASCII.GetBytes("\r\n\r\n"), 64 * 1024).ConfigureAwait(false);
        if (header is null)
            return null;

        var text = Encoding.ASCII.GetString(header);
        var lines = text.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) &&
            length > 0)
            body = await ReadExactAsync(stream, length).ConfigureAwait(false);

        return new HttpRequest(first[0], first[1], headers, body);
    }

    private static async Task<UpstreamResponse> ReadUpstreamAsync(Stream stream)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var header = await ReadUntilAsync(stream, Encoding.ASCII.GetBytes("\r\n\r\n"), 64 * 1024).ConfigureAwait(false);
        if (header is null)
            return UpstreamResponse.Failed(502, "The telemetry host closed the connection.");

        var text = Encoding.ASCII.GetString(header);
        var lines = text.Split("\r\n");
        var status = lines[0].Split(' ');
        var code = status.Length > 1 && int.TryParse(status[1], out var parsed) ? parsed : 0;
        var reason = status.Length > 2 ? string.Join(' ', status.Skip(2)) : string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transfer) &&
            transfer.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            body = await ReadChunkedAsync(stream).ConfigureAwait(false);
        else if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length))
            body = await ReadExactAsync(stream, length).ConfigureAwait(false);
        else
            body = [];

        started.Stop();
        return new UpstreamResponse(code, reason, headers, body, body, started.ElapsedMilliseconds, null);
    }

    private static async Task WriteResponseAsync(Stream stream, UpstreamResponse response)
    {
        var header = new StringBuilder();
        header.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(response.Reason).Append("\r\n");
        foreach (var (name, value) in response.Headers)
            if (!name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                header.Append(name).Append(": ").Append(value).Append("\r\n");
        header.Append("Content-Length: ").Append(response.RawBody.Length).Append("\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString())).ConfigureAwait(false);
        if (response.RawBody.Length > 0)
            await stream.WriteAsync(response.RawBody).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadUntilAsync(Stream stream, byte[] marker, int limit)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < limit)
        {
            if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
                return null;
            buffer.WriteByte(one[0]);
            if (buffer.Length >= marker.Length && buffer.GetBuffer()
                    .AsSpan((int)buffer.Length - marker.Length, marker.Length).SequenceEqual(marker))
                return buffer.ToArray();
        }

        return null;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read, length - read)).ConfigureAwait(false);
            if (got == 0)
                break;
            read += got;
        }

        return read == length ? buffer : buffer[..read];
    }

    private static async Task<byte[]> ReadChunkedAsync(Stream stream)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadUntilAsync(stream, Encoding.ASCII.GetBytes("\r\n"), 32).ConfigureAwait(false);
            if (sizeLine is null)
                break;
            var sizeText = Encoding.ASCII.GetString(sizeLine).Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size == 0)
                break;
            var chunk = await ReadExactAsync(stream, size).ConfigureAwait(false);
            body.Write(chunk);
            await ReadExactAsync(stream, 2).ConfigureAwait(false);
        }

        return body.ToArray();
    }

    private sealed record HttpRequest(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);

    private sealed record UpstreamResponse(int StatusCode, string Reason, Dictionary<string, string> Headers,
        byte[] Body, byte[] RawBody, long DurationMilliseconds, string? Error)
    {

        internal static UpstreamResponse Failed(int code, string error) =>
            new(code, "Bad Gateway", new Dictionary<string, string>(), Encoding.UTF8.GetBytes(error), [], 0, error);
    }
}

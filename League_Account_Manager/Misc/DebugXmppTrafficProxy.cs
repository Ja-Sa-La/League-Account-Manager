using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace League_Account_Manager.Misc;

internal sealed class DebugXmppTrafficProxy : IDisposable
{
    internal const string LocalhostDomain = "localhost.leagueaccountmanager.xyz";
    private const string CertificateUrl = "https://redirect.leagueaccountmanager.xyz/cert.pfx";
    private static readonly string CertificatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "League Account Manager", "OfflineLauncher", "localhostCert.pfx");
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers/etc/hosts");

    private readonly X509Certificate2 _certificate;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private string? _upstreamHost;
    private int _upstreamPort;
    private bool _disposed;

    private DebugXmppTrafficProxy(X509Certificate2 certificate)
    {
        _certificate = certificate;
    }

    internal int Port { get; private set; }

    internal static async Task<DebugXmppTrafficProxy> CreateAsync(CancellationToken cancellationToken)
    {
        EnsureHostsEntry();
        var certificate = await LoadCertificateAsync(cancellationToken).ConfigureAwait(false)
                          ?? throw new InvalidOperationException("The localhost certificate for XMPP proxying could not be loaded.");
        var proxy = new DebugXmppTrafficProxy(certificate);
        proxy.Start();
        return proxy;
    }

    internal void SetUpstream(string host, int port)
    {
        _upstreamHost = host;
        _upstreamPort = port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _listener.Stop();
        _certificate.Dispose();
        _stop.Dispose();
    }

    private void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
        DebugConsole.WriteLine($"[Debug Capture] XMPP proxy 127.0.0.1:{Port} -> TLS upstream");
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
                DebugConsole.WriteLine($"[Debug Capture] XMPP listener error: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            if (string.IsNullOrWhiteSpace(_upstreamHost) || _upstreamPort <= 0)
            {
                DebugConsole.WriteLine("[Debug Capture] XMPP upstream was not found in client config.", ConsoleColor.Yellow);
                return;
            }

            try
            {
                using var clientTls = new SslStream(client.GetStream(), false);
                await clientTls.AuthenticateAsServerAsync(_certificate, false,
                    SslProtocols.Tls12 | SslProtocols.Tls13, false).ConfigureAwait(false);
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(_upstreamHost, _upstreamPort, _stop.Token).ConfigureAwait(false);
                using var upstreamTls = new SslStream(upstream.GetStream(), false,
                    (_, _, _, _) => true);
                await upstreamTls.AuthenticateAsClientAsync(_upstreamHost, null,
                    SslProtocols.Tls12 | SslProtocols.Tls13, false).ConfigureAwait(false);

                using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                var outgoing = PumpAsync(clientTls, upstreamTls, true, connectionStop.Token);
                var incoming = PumpAsync(upstreamTls, clientTls, false, connectionStop.Token);
                await Task.WhenAny(outgoing, incoming).ConfigureAwait(false);
                connectionStop.Cancel();
                try { await Task.WhenAll(outgoing, incoming).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DebugConsole.WriteLine($"[Debug Capture] XMPP connection failed: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    private static async Task PumpAsync(Stream source, Stream destination, bool outgoing, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) return;
            var payload = buffer[..count];
            var text = Encoding.UTF8.GetString(payload);
            LcuRequestLog.Add("riot", outgoing ? "SEND" : "RECEIVE", "/chat",
                outgoing ? text : string.Empty, null, outgoing ? "Outgoing" : "Incoming",
                outgoing ? string.Empty : text, 0, trafficType: "XMPP",
                direction: outgoing ? "Outgoing" : "Incoming");
            await destination.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<X509Certificate2?> LoadCertificateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(CertificatePath))
            {
                var cached = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(CertificatePath), null,
                    X509KeyStorageFlags.DefaultKeySet);
                if (CertificateMatchesDomain(cached))
                    return cached;
                cached.Dispose();
            }

            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(CertificateUrl, cancellationToken).ConfigureAwait(false);
            var certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet);
            if (!CertificateMatchesDomain(certificate))
            {
                certificate.Dispose();
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CertificatePath)!);
            File.WriteAllBytes(CertificatePath, bytes);
            return certificate;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[Debug Capture] Failed to load XMPP certificate: {ex.Message}", ConsoleColor.Yellow);
            return null;
        }
    }

    private static bool CertificateMatchesDomain(X509Certificate2 certificate)
    {
        try
        {
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17") continue;
                var sequence = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
                while (sequence.HasData)
                {
                    var tag = sequence.PeekTag();
                    if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)) &&
                        string.Equals(sequence.ReadCharacterString(UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 2)), LocalhostDomain,
                            StringComparison.OrdinalIgnoreCase)) return true;
                    sequence.ReadEncodedValue();
                }
            }

            return string.Equals(certificate.GetNameInfo(X509NameType.DnsName, false), LocalhostDomain,
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void EnsureHostsEntry()
    {
        if (File.Exists(HostsPath) && File.ReadLines(HostsPath).Any(line =>
                line.Split('#', 2)[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).Any(host => host.Equals(LocalhostDomain, StringComparison.OrdinalIgnoreCase)))) return;

        var result = System.Windows.MessageBox.Show(
            $"Debug login needs a hosts entry for {LocalhostDomain}. Add it now?", "League Account Manager",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
            throw new InvalidOperationException("Debug login canceled because the XMPP hosts entry is missing.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-Content -LiteralPath '{HostsPath}' -Value '`r`n127.0.0.1 {LocalhostDomain}'\"",
            UseShellExecute = true,
            Verb = "runas"
        });
        process?.WaitForExit();
    }
}
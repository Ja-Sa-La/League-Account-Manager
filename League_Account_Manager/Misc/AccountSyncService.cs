using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Opaque;
using System.Text.RegularExpressions;
using CsvHelper.Configuration;

namespace League_Account_Manager.Misc;

internal sealed class AccountSyncService
{
    internal const int MaxDocumentBytes = 10 * 1024 * 1024;
    internal const int MaxSettingsBytes = 2 * 1024 * 1024;
    internal bool IsAuthenticated => _session != null;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] ServerStaticPublicKey = DecodeBase64Url("EuN3tg0PAvwVxorLOz8fCu0GBVvTMGuL0nn59B6D9HA");
    private static readonly CipherConfiguration OpaqueConfiguration = CreateOpaqueConfiguration();
    internal static AccountSyncService Instance { get; } = new();
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _sessionPath;
    private CancellationTokenSource? _autoSyncCancellation;
    private bool _automaticSyncPaused;
    private Session? _session;
    internal string? Username => _session?.User.Username;

    internal AccountSyncService()
        : this(null, null, true)
    {
    }

    internal AccountSyncService(HttpMessageHandler? handler, string? sessionPath)
        : this(handler, sessionPath, false)
    {
    }

    private AccountSyncService(HttpMessageHandler? handler, string? sessionPath, bool subscribeToAccountChanges)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri("https://lam.monster/"),
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = ((MaxDocumentBytes + 2L) / 3 * 4) + 4096
        };
        var version = typeof(AccountSyncService).Assembly.GetName().Version?.ToString() ?? "1.0";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"League-Account-Manager/{version}");
        _client.DefaultRequestHeaders.Add("X-LAM-Client", "League-Account-Manager");
        _client.DefaultRequestHeaders.Add("X-LAM-Version", version);
        _client.DefaultRequestHeaders.Add("X-LAM-Platform", "windows");
        _client.DefaultRequestHeaders.Add("X-LAM-Protocol", "1");
        _sessionPath = sessionPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "League Account Manager", "cloud-session.bin");
        try
        {
            if (File.Exists(_sessionPath))
                _session = JsonSerializer.Deserialize<Session>(ProtectedData.Unprotect(File.ReadAllBytes(_sessionPath),
                    null, DataProtectionScope.CurrentUser), JsonOptions);
            if (_session != null && string.IsNullOrEmpty(_session.ExportKey))
                _session = null;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or JsonException)
        {
            DebugConsole.WriteLine("[Cloud Sync] Saved session could not be loaded. Sign in again.");
        }

        if (subscribeToAccountChanges)
            AccountFileStore.AccountsFileUpdated += OnAccountsFileUpdated;
    }

    internal async Task<string> GetCurrentUserAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureSessionAsync();
            return await SendAsync(HttpMethod.Get, "api/auth/me", null, true);
        }
        finally { _gate.Release(); }
    }

    internal static string? ValidateCredentials(string username, string password, bool register)
    {
        if (!Regex.IsMatch(username, "\\A[a-z0-9_]{3,64}\\z"))
            return "Username must be 3-64 lowercase letters, numbers, or underscores.";
        if (string.IsNullOrEmpty(password)) return "Enter your password.";
        if (register && (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
                         !password.Any(char.IsDigit) || !password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)) ||
                         password.Contains(username, StringComparison.OrdinalIgnoreCase)))
            return "Use at least 12 characters with uppercase, lowercase, a number, and a symbol. Do not include your username.";
        return null;
    }

    internal async Task AuthenticateAsync(string username, string password, bool register)
    {
        var error = ValidateCredentials(username, password, register);
        if (error != null) throw new InvalidOperationException(error);
        await _gate.WaitAsync();
        try
        {
            var opaque = new BitwardenOpaqueClient();
            if (register)
            {
                var start = opaque.StartRegistration(OpaqueConfiguration, password);
                var startJson = await SendAsync(HttpMethod.Post, "api/auth/opaque/register/start",
                    JsonSerializer.Serialize(new { username, registrationRequest = EncodeBase64Url(start.registrationRequest) }), false);
                using var startDocument = JsonDocument.Parse(startJson);
                var finish = opaque.FinishRegistration(OpaqueConfiguration, start.state,
                    DecodeBase64Url(startDocument.RootElement.GetProperty("registrationResponse").GetString()!), password);
                VerifyServerStaticPublicKey(finish.serverSPKey);
                var finishJson = await SendAsync(HttpMethod.Post, "api/auth/opaque/register/finish",
                    JsonSerializer.Serialize(new
                    {
                        username,
                        challengeId = startDocument.RootElement.GetProperty("challengeId").GetString(),
                        registrationRecord = EncodeBase64Url(finish.registrationUpload)
                    }), false);
                SaveSession(finishJson, finish.exportKey);
            }
            else
            {
                var start = opaque.StartLogin(OpaqueConfiguration, password);
                var startJson = await SendAsync(HttpMethod.Post, "api/auth/opaque/login/start",
                    JsonSerializer.Serialize(new { username, startLoginRequest = EncodeBase64Url(start.credentialRequest) }), false);
                using var startDocument = JsonDocument.Parse(startJson);
                var finish = opaque.FinishLogin(OpaqueConfiguration, start.state,
                    DecodeBase64Url(startDocument.RootElement.GetProperty("loginResponse").GetString()!), password);
                VerifyServerStaticPublicKey(finish.serverSPKey);
                var finishJson = await SendAsync(HttpMethod.Post, "api/auth/opaque/login/finish",
                    JsonSerializer.Serialize(new
                    {
                        username,
                        challengeId = startDocument.RootElement.GetProperty("challengeId").GetString(),
                        finishLoginRequest = EncodeBase64Url(finish.credentialFinalization)
                    }), false);
                SaveSession(finishJson, finish.exportKey);
            }
        }
        finally { _gate.Release(); }
    }

    internal async Task<string?> TryDownloadAsync(bool settings)
    {
        var path = settings ? "api/settings" : "api/accounts";
        await _gate.WaitAsync();
        try
        {
            await EnsureSessionAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session?.Token);
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearSession();
                throw new InvalidOperationException("Sign-in failed or your cloud session expired. Sign in again.");
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Cloud request failed (HTTP {(int)response.StatusCode}). Please try again later.");
            return DecryptEnvelope(await response.Content.ReadAsStringAsync(), settings);
        }
        finally { _gate.Release(); }
    }

    internal async Task RunWithoutAutomaticSyncAsync(Func<Task> action)
    {
        var previouslyPaused = _automaticSyncPaused;
        _automaticSyncPaused = true;
        _autoSyncCancellation?.Cancel();
        try { await action(); }
        finally { _automaticSyncPaused = previouslyPaused; }
    }

    internal async Task<string> TransferAsync(bool upload, string? document = null)
    {
        return await TransferAsync("api/accounts", MaxDocumentBytes, upload, document);
    }

    internal async Task<string> TransferSettingsAsync(bool upload, string? document = null)
    {
        return await TransferAsync("api/settings", MaxSettingsBytes, upload, document);
    }

    internal async Task UploadCurrentAccountsAsync()
    {
        var config = new CsvConfiguration(CultureInfo.CurrentCulture) { Delimiter = ";" };
        var document = await AccountFileStore.CreateSyncDocumentAsync(config);
        await TransferAsync(true, document);
    }

    private async void OnAccountsFileUpdated(object? sender, EventArgs e)
    {
        if (!IsAuthenticated || _automaticSyncPaused)
            return;

        _autoSyncCancellation?.Cancel();
        _autoSyncCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _autoSyncCancellation = cancellation;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            if (_automaticSyncPaused || !IsAuthenticated)
                return;
            await UploadCurrentAccountsAsync();
            DebugConsole.WriteLine("[Cloud Sync] Account file changes uploaded.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DebugConsole.WriteLine($"[Cloud Sync] Automatic account upload failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_autoSyncCancellation, cancellation))
                _autoSyncCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task<string> TransferAsync(string path, int maxBytes, bool upload, string? document)
    {
        if (upload && (document == null || Encoding.UTF8.GetByteCount(document) > maxBytes))
            throw new InvalidOperationException(path == "api/settings"
                ? "Settings document exceeds the 2 MB limit."
                : "Account document exceeds the 10 MB limit.");
        await _gate.WaitAsync();
        try
        {
            await EnsureSessionAsync();
            if (upload)
            {
                var envelope = EncryptEnvelope(document!, path == "api/settings");
                await SendAsync(HttpMethod.Put, path, envelope, true);
                return document!;
            }
            return DecryptEnvelope(await SendAsync(HttpMethod.Get, path, null, true), path == "api/settings");
        }
        finally { _gate.Release(); }
    }

    internal async Task LogoutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_session != null) await SendAsync(HttpMethod.Post, "api/auth/logout", null, true);
            ClearSession();
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureSessionAsync()
    {
        if (_session == null) throw new InvalidOperationException("Sign in to cloud sync first.");
        if (_session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            ClearSession();
            throw new InvalidOperationException("Your cloud session expired. Sign in again.");
        }
        if (_session.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds())
            SaveSession(await SendAsync(HttpMethod.Post, "api/auth/refresh", null, true), _session.ExportKeyBytes);
    }

    private async Task<string> SendAsync(HttpMethod method, string path, string? document, bool authenticated)
    {
        using var request = new HttpRequestMessage(method, path);
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session?.Token);
        if (document != null) request.Content = new StringContent(document, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (authenticated) ClearSession();
            throw new InvalidOperationException("Sign-in failed or your cloud session expired. Sign in again.");
        }
        if (response.StatusCode == HttpStatusCode.NotFound && path == "api/accounts")
            throw new InvalidOperationException("No cloud account file exists yet. Upload from your other computer first.");
        if (response.StatusCode == HttpStatusCode.NotFound && path == "api/settings")
            throw new InvalidOperationException("No cloud settings file exists yet. Upload from your other computer first.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("That username is already registered.");
        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new InvalidOperationException(retryAfter.HasValue
                ? $"Cloud sync is temporarily rate-limited. Try again in {(int)Math.Ceiling(retryAfter.Value.TotalSeconds)} seconds."
                : "Cloud sync is temporarily rate-limited. Please try again later.");
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cloud request failed (HTTP {(int)response.StatusCode}). Please try again later.");
        return await response.Content.ReadAsStringAsync();
    }

    private void SaveSession(string json, byte[] exportKey)
    {
        var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
        if (session == null || string.IsNullOrWhiteSpace(session.Token) || string.IsNullOrWhiteSpace(session.User?.Username))
            throw new InvalidOperationException("The server returned an invalid session.");
        if (session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            if (session.ExpiresIn <= 0)
                throw new InvalidOperationException("The server returned an invalid session expiry.");
            session = session with
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(session.ExpiresIn).ToUnixTimeMilliseconds()
            };
        }
        if (exportKey.Length == 0)
            throw new InvalidOperationException("The server returned an empty OPAQUE export key.");
        session = session with { ExportKey = EncodeBase64Url(exportKey) };
        var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions), null,
            DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        var temporaryPath = _sessionPath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _sessionPath, true);
        _session = session;
    }

    private void ClearSession()
    {
        _session = null;
        if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
    }

    private static CipherConfiguration CreateOpaqueConfiguration()
    {
        var configuration = CipherConfiguration.Default;
        configuration.OpaqueVersion = 3;
        configuration.OprfCs = OprfCs.Ristretto255;
        configuration.KeGroup = KeGroup.Ristretto255;
        configuration.KeyExchange = KeyExchange.TripleDH;
        configuration.Ksf.Algorithm = KsfAlgorithm.Argon2id;
        configuration.Ksf.Parameters.Memory = 65536;
        configuration.Ksf.Parameters.Iterations = 3;
        configuration.Ksf.Parameters.Parallelism = 4;
        return configuration;
    }

    private static void VerifyServerStaticPublicKey(byte[] serverStaticPublicKey)
    {
        if (!CryptographicOperations.FixedTimeEquals(serverStaticPublicKey, ServerStaticPublicKey))
            throw new CryptographicException("The cloud server public-key pin does not match.");
    }

    private string EncryptEnvelope(string document, bool settings)
    {
        var key = DeriveSyncKey(settings);
        var iv = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(document);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(iv, plaintext, ciphertext, tag);
        return JsonSerializer.Serialize(new
        {
            version = 1,
            algorithm = "AES-256-GCM",
            kdf = "HKDF-SHA-256",
            keyId = settings ? "settings-v1" : "account-v1",
            iv = EncodeBase64Url(iv),
            ciphertext = EncodeBase64Url(ciphertext),
            tag = EncodeBase64Url(tag)
        }, JsonOptions);
    }

    private string DecryptEnvelope(string json, bool settings)
    {
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions); }
        catch (JsonException exception) { throw new InvalidOperationException("The cloud returned an invalid encrypted document.", exception); }
        if (envelope == null || envelope.Version != 1 || envelope.Algorithm != "AES-256-GCM" ||
            envelope.Kdf != "HKDF-SHA-256" || envelope.KeyId != (settings ? "settings-v1" : "account-v1"))
            throw new InvalidOperationException("The cloud returned an unsupported encrypted document.");
        var maxBytes = settings ? MaxSettingsBytes : MaxDocumentBytes;
        if (envelope.Iv == null || envelope.Iv.Length != 16 ||
            envelope.Tag == null || envelope.Tag.Length != 22 ||
            string.IsNullOrEmpty(envelope.Ciphertext) || envelope.Ciphertext.Length > (maxBytes + 2L) / 3 * 4)
            throw new InvalidOperationException("The cloud returned an invalid encrypted document.");
        var iv = DecodeBase64Url(envelope.Iv);
        var ciphertext = DecodeBase64Url(envelope.Ciphertext);
        var tag = DecodeBase64Url(envelope.Tag);
        if (iv.Length != 12 || tag.Length != 16 || ciphertext.Length > maxBytes)
            throw new InvalidOperationException("The cloud returned an invalid encrypted document.");
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(DeriveSyncKey(settings), tag.Length);
            aes.Decrypt(iv, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception) { throw new InvalidOperationException("The cloud document could not be authenticated.", exception); }
    }

    private byte[] DeriveSyncKey(bool settings)
    {
        EnsureSessionExportKey();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _session!.ExportKeyBytes, 32, null,
            Encoding.UTF8.GetBytes(settings ? "LAM settings encryption v1" : "LAM account encryption v1"));
    }

    private void EnsureSessionExportKey()
    {
        if (_session == null || _session.ExportKeyBytes.Length == 0)
            throw new InvalidOperationException("Sign in again to enable encrypted cloud sync.");
    }

    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length % 4 == 1 ||
            value.Any(character => !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new FormatException("Expected an unpadded base64url value.");
        var decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (EncodeBase64Url(decoded) != value)
            throw new FormatException("Expected a canonical base64url value.");
        return decoded;
    }

    private sealed record CloudUser(string Id, string Username);
    private sealed record Session(CloudUser User, string Token, long ExpiresAt, long ExpiresIn, string ExportKey = "")
    {
        [JsonIgnore] public byte[] ExportKeyBytes => DecodeBase64Url(ExportKey);
    }
    private sealed record Envelope(int Version, string Algorithm, string Kdf, string KeyId, string Iv, string Ciphertext, string Tag);
}
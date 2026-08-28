using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using CsvHelper;
using CsvHelper.Configuration;
using League_Account_Manager.Windows;

namespace League_Account_Manager.Misc;

internal static class AccountFileStore
{
    private const string EncryptionHeader = "LAMENC1";
    private const string StorageSchema = "LAM.Accounts";
    private const int StorageVersion = 2;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 100_000;
    private static readonly SemaphoreSlim StoreGate = new(1, 1);
    private static readonly TimeSpan FileUnlockTimeout = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyList<IAccountTransferFormat> TransferFormats = new List<IAccountTransferFormat>
    {
        new StructuredJsonTransferFormat(),
        new CsvTransferFormat()
    };

    public static bool IsEncryptionEnabled => Settings.settingsloaded.AccountFileEncryptionEnabled;

    public static event EventHandler? AccountsFileUpdated;

    public static string GetAccountsFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, $"{Settings.settingsloaded.filename}.LAM");
    }

    public static string? GetPassword()
    {
        var stored = Settings.settingsloaded.AccountFileEncryptionPassword;
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(stored);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static void SetPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            Settings.settingsloaded.AccountFileEncryptionPassword = null;
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        Settings.settingsloaded.AccountFileEncryptionPassword = Convert.ToBase64String(protectedBytes);
    }

    public static async Task<List<Utils.AccountList>> LoadAsync(string filePath, CsvConfiguration config)
    {
        await StoreGate.WaitAsync();
        try
        {
            return await LoadCoreAsync(filePath, config);
        }
        finally
        {
            StoreGate.Release();
        }
    }

    private static async Task<List<Utils.AccountList>> LoadCoreAsync(string filePath, CsvConfiguration config)
    {
        if (!File.Exists(filePath)) return new List<Utils.AccountList>();

        List<Utils.AccountList> records;
        var rewritten = false;

        try
        {
            var loadResult = await LoadForMigrationAsync(filePath, config, GetPassword());
            records = NormalizeStructuredCollections(loadResult.Records);
        }
        catch (CryptographicException)
        {
            records = NormalizeStructuredCollections(await PromptPasswordUntilSuccessAsync(filePath, config));
        }

        var data = await File.ReadAllBytesAsync(filePath);
        var normalizedContent = WriteStorageToString(records);

        if (IsEncryptionEnabled)
        {
            var password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            var shouldRewrite = true;
            if (IsEncrypted(data))
                try
                {
                    var currentContent = Decrypt(data, password);
                    shouldRewrite = !string.Equals(currentContent, normalizedContent, StringComparison.Ordinal);
                }
                catch (CryptographicException)
                {
                    shouldRewrite = true;
                }

            if (shouldRewrite)
            {
                await SaveEncryptedAsync(filePath, records, config, password);
                rewritten = true;
            }
        }
        else
        {
            var currentContent = IsEncrypted(data) ? null : Encoding.UTF8.GetString(data);
            if (currentContent == null || !string.Equals(currentContent, normalizedContent, StringComparison.Ordinal))
            {
                await SavePlaintextAsync(filePath, records, config);
                rewritten = true;
            }
        }

        if (rewritten)
        {
            try
            {
                var reloaded = await LoadForMigrationAsync(filePath, config, GetPassword());
                records = NormalizeStructuredCollections(reloaded.Records);
            }
            catch
            {
            }
        }

        return records;
    }

    public static async Task SaveAsync(string filePath, IEnumerable<Utils.AccountList> records, CsvConfiguration config)
    {
        await StoreGate.WaitAsync();
        try
        {
            if (IsEncryptionEnabled)
            {
                var password = GetPassword();
                if (string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Account file encryption password not set.");

                await SaveEncryptedAsync(filePath, records, config, password);
            }
            else
            {
                await SavePlaintextAsync(filePath, records, config);
            }

            AccountsFileUpdated?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            StoreGate.Release();
        }
    }

    public static void Save(string filePath, IEnumerable<Utils.AccountList> records, CsvConfiguration config)
    {
        SaveAsync(filePath, records, config).GetAwaiter().GetResult();
    }

    public static string GetTransferFileDialogFilter()
    {
        var formatEntries = TransferFormats
            .Select(f =>
                $"{f.DisplayName} ({string.Join(';', f.Extensions.Select(e => $"*{e}"))})|{string.Join(';', f.Extensions.Select(e => $"*{e}"))}");

        var allPatterns = string.Join(';', TransferFormats.SelectMany(f => f.Extensions).Distinct().Select(e => $"*{e}"));
        return string.Join("|", formatEntries.Concat(new[] { $"All supported files|{allPatterns}" }));
    }

    public static async Task<List<Utils.AccountList>> ImportAsync(string sourcePath, CsvConfiguration config)
    {
        await StoreGate.WaitAsync();
        try
        {
            await WaitForFileUnlockAsync(sourcePath);
            var data = await File.ReadAllBytesAsync(sourcePath);
            if (data.Length == 0)
                return new List<Utils.AccountList>();

            return await ImportCoreAsync(sourcePath, data, config);
        }
        finally
        {
            StoreGate.Release();
        }
    }

    private static async Task<List<Utils.AccountList>> ImportCoreAsync(string sourcePath, byte[] data,
        CsvConfiguration config)
    {
        if (data.Length == 0)
            return new List<Utils.AccountList>();

        string content;
        if (IsEncrypted(data))
        {
            while (true)
            {
                var password = PromptForPassword("Enter the password to decrypt the import file.");
                if (string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Import canceled. Password is required for encrypted files.");

                try
                {
                    content = Decrypt(data, password);
                    break;
                }
                catch (CryptographicException)
                {
                }
            }
        }
        else
        {
            content = Encoding.UTF8.GetString(data);
        }

        return NormalizeStructuredCollections(ParseTransferContent(sourcePath, content, config));
    }

    public static async Task ExportAsync(string destinationPath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config, bool encryptExport, string? encryptionPassword)
    {
        await StoreGate.WaitAsync();
        try
        {
            var format = ResolveTransferFormatByPath(destinationPath);
            var content = format.Serialize(records.ToList(), config);

            await WaitForFileUnlockAsync(destinationPath);

            if (encryptExport)
            {
                if (string.IsNullOrWhiteSpace(encryptionPassword))
                    throw new InvalidOperationException("Export password is required when encryption is enabled.");

                await AtomicWriteBytesAsync(destinationPath, Encrypt(content, encryptionPassword));
            }
            else
            {
                await AtomicWriteTextAsync(destinationPath, content);
            }
        }
        finally
        {
            StoreGate.Release();
        }
    }

    public static async Task ImportIntoCurrentStoreAsync(string sourcePath, CsvConfiguration config,
        bool combineAndDedupe)
    {
        await StoreGate.WaitAsync();
        try
        {
            await WaitForFileUnlockAsync(sourcePath);
            var importedRecords = await ImportCoreAsync(sourcePath, await File.ReadAllBytesAsync(sourcePath), config);
            if (!combineAndDedupe)
            {
                await SaveCoreAsync(GetAccountsFilePath(), importedRecords, config);
                AccountsFileUpdated?.Invoke(null, EventArgs.Empty);
                return;
            }

            var existingRecords = await LoadCoreAsync(GetAccountsFilePath(), config);
            var merged = existingRecords
                .Concat(importedRecords)
                .GroupBy(x => ((x.username ?? string.Empty).Trim().ToLowerInvariant(),
                    (x.password ?? string.Empty).Trim()))
                .Select(g => g.Last())
                .ToList();

            await SaveCoreAsync(GetAccountsFilePath(), merged, config);
            AccountsFileUpdated?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            StoreGate.Release();
        }
    }

    private static string? PromptForPassword(string message)
    {
        string? password = null;

        void ShowPrompt()
        {
            var prompt = new PasswordPrompt(message);
            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
                prompt.Owner = owner;

            var result = prompt.ShowDialog();
            if (result == true)
                password = prompt.Password;
        }

        if (Application.Current?.Dispatcher != null)
            Application.Current.Dispatcher.Invoke(ShowPrompt);
        else
            ShowPrompt();

        return password;
    }


    private static async Task<List<Utils.AccountList>> PromptPasswordUntilSuccessAsync(string filePath,
        CsvConfiguration config)
    {
        while (true)
        {
            var password = PromptForPassword("Incorrect password. Please try again.");
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            try
            {
                var records = (await LoadForMigrationAsync(filePath, config, password)).Records;
                SetPassword(password);
                return records;
            }
            catch (CryptographicException)
            {
            }
        }
    }

    public static async Task RewriteForEncryptionStateAsync(string sourceFilePath, string destinationFilePath,
        CsvConfiguration config, bool encrypt, string? currentPassword, string? newPassword)
    {
        await StoreGate.WaitAsync();
        try
        {
            await RewriteForEncryptionStateCoreAsync(sourceFilePath, destinationFilePath, config, encrypt,
                currentPassword, newPassword);
        }
        finally
        {
            StoreGate.Release();
        }
    }

    private static async Task RewriteForEncryptionStateCoreAsync(string sourceFilePath, string destinationFilePath,
        CsvConfiguration config, bool encrypt, string? currentPassword, string? newPassword)
    {
        var isRename = !string.Equals(sourceFilePath, destinationFilePath, StringComparison.OrdinalIgnoreCase);
        if (isRename && File.Exists(destinationFilePath))
            throw new IOException($"An account file named '{Path.GetFileName(destinationFilePath)}' already exists.");

        if (!File.Exists(sourceFilePath))
        {
            if (encrypt && !string.IsNullOrWhiteSpace(newPassword))
                await SaveEncryptedAsync(destinationFilePath, new List<Utils.AccountList>(), config, newPassword);
            return;
        }

        var records = (await LoadForMigrationAsync(sourceFilePath, config, currentPassword)).Records;

        if (encrypt)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new InvalidOperationException("Account file encryption password not set.");

            await SaveEncryptedAsync(destinationFilePath, records, config, newPassword);
        }
        else
        {
            await SavePlaintextAsync(destinationFilePath, records, config);
        }

        if (isRename)
            File.Delete(sourceFilePath);
    }

    private static async Task<StorageReadResult> LoadForMigrationAsync(string filePath, CsvConfiguration config,
        string? password)
    {
        await WaitForFileUnlockAsync(filePath);
        var data = await File.ReadAllBytesAsync(filePath);
        if (data.Length == 0) return new StorageReadResult(new List<Utils.AccountList>(), false);

        if (IsEncrypted(data))
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            var csvText = Decrypt(data, password);
            return ReadStorageFromString(csvText, config);
        }

        var plainText = Encoding.UTF8.GetString(data);
        return ReadStorageFromString(plainText, config);
    }

    private static async Task SavePlaintextAsync(string filePath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config)
    {
        await WaitForFileUnlockAsync(filePath);
        var csvText = WriteStorageToString(records);
        await AtomicWriteTextAsync(filePath, csvText);
    }

    private static async Task SaveEncryptedAsync(string filePath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Account file encryption password not set.");

        await WaitForFileUnlockAsync(filePath);
        var csvText = WriteStorageToString(records);
        var encrypted = Encrypt(csvText, password);
        await AtomicWriteBytesAsync(filePath, encrypted);
    }

    private static StorageReadResult ReadStorageFromString(string content, CsvConfiguration config)
    {
        if (TryReadStructuredStorage(content, out var records))
            return new StorageReadResult(records, false);

        if (TryReadLegacyJsonStorage(content, out records))
            return new StorageReadResult(records, true);

        return new StorageReadResult(ReadCsvFromString(content, config), true);
    }

    private static List<Utils.AccountList> ParseTransferContent(string sourcePath, string content, CsvConfiguration config)
    {
        var detected = ReadStorageFromString(content, config);
        if (detected.Records.Count > 0)
            return detected.Records;

        var format = ResolveTransferFormatByPath(sourcePath);
        return format.Deserialize(content, config);
    }

    private static IAccountTransferFormat ResolveTransferFormatByPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return TransferFormats[0];

        var format = TransferFormats.FirstOrDefault(f =>
            f.Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)));

        if (format != null)
            return format;

        throw new InvalidOperationException($"Unsupported file format: '{extension}'.");
    }

    private static bool TryReadStructuredStorage(string content, out List<Utils.AccountList> records)
    {
        records = new List<Utils.AccountList>();
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            var envelope = JsonSerializer.Deserialize<StorageEnvelope>(content);
            if (envelope == null ||
                !string.Equals(envelope.Schema, StorageSchema, StringComparison.Ordinal) ||
                envelope.Version <= 0)
                return false;

            if (envelope.Version > StorageVersion)
                throw new InvalidDataException(
                    $"Account file version {envelope.Version} is newer than supported version {StorageVersion}.");

            records = envelope.Accounts ?? new List<Utils.AccountList>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadLegacyJsonStorage(string content, out List<Utils.AccountList> records)
    {
        records = new List<Utils.AccountList>();
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            var node = JsonNode.Parse(content);
            if (node == null)
                return false;

            if (node is JsonArray array)
            {
                records = array.Deserialize<List<Utils.AccountList>>() ?? new List<Utils.AccountList>();
                return true;
            }

            if (node is not JsonObject obj)
                return false;

            foreach (var key in new[] { "accounts", "Accounts", "records", "Records", "data", "Data" })
            {
                var listNode = obj[key];
                if (listNode is not JsonArray listArray)
                    continue;

                records = listArray.Deserialize<List<Utils.AccountList>>() ?? new List<Utils.AccountList>();
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WaitForFileUnlockAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FileUnlockTimeout);

        while (true)
            try
            {
                using (File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None))
                {
                    break;
                }
            }
            catch (IOException)
            {
                await Task.Delay(300, timeout.Token);
            }
    }

    private static async Task SaveCoreAsync(string filePath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config)
    {
        if (IsEncryptionEnabled)
        {
            var password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            await SaveEncryptedAsync(filePath, records, config, password);
        }
        else
        {
            await SavePlaintextAsync(filePath, records, config);
        }
    }

    private static async Task AtomicWriteTextAsync(string filePath, string content)
    {
        var temporaryPath = GetTemporaryPath(filePath);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false));
            ReplaceFile(temporaryPath, filePath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task AtomicWriteBytesAsync(string filePath, byte[] content)
    {
        var temporaryPath = GetTemporaryPath(filePath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content);
            ReplaceFile(temporaryPath, filePath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static string GetTemporaryPath(string filePath)
    {
        return Path.Combine(Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Replace(temporaryPath, destinationPath, null);
        else
            File.Move(temporaryPath, destinationPath);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static List<Utils.AccountList> ReadCsvFromString(string csvText, CsvConfiguration config)
    {
        var records = new List<Utils.AccountList>();
        if (string.IsNullOrWhiteSpace(csvText)) return records;

        using var reader = new StringReader(csvText);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read()) return records;

        csv.ReadHeader();

        var headerMap = new Dictionary<string, int>(StringComparer.Ordinal);
        if (csv.HeaderRecord != null)
            for (var i = 0; i < csv.HeaderRecord.Length; i++)
                if (!string.IsNullOrWhiteSpace(csv.HeaderRecord[i]) && !headerMap.ContainsKey(csv.HeaderRecord[i]))
                    headerMap[csv.HeaderRecord[i]] = i;

        string? GetField(string headerName, int fallbackIndex)
        {
            if (headerMap.TryGetValue(headerName, out var index))
                return csv.TryGetField(index, out string? value) ? value : null;

            if (fallbackIndex < 0)
                return null;

            return csv.TryGetField(fallbackIndex, out string? fallbackValue) ? fallbackValue : null;
        }

        string? GetFieldAny(int fallbackIndex, params string[] headerNames)
        {
            foreach (var headerName in headerNames)
                if (headerMap.TryGetValue(headerName, out var index))
                    return csv.TryGetField(index, out string? value) ? value : null;

            if (fallbackIndex < 0)
                return null;

            return csv.TryGetField(fallbackIndex, out string? fallbackValue) ? fallbackValue : null;
        }

        while (true)
            try
            {
                if (!csv.Read())
                    break;

                var record = new Utils.AccountList
                {
                    username = GetField("username", 0) ?? "",
                    password = GetField("password", 1) ?? "",
                    riotID = GetField("riotID", 2) ?? "",
                    level = TryParseInt(GetField("level", 3)),
                    server = GetField("server", 4) ?? "",
                    be = TryParseInt(GetField("be", 5)),
                    rp = TryParseInt(GetField("rp", 6)),
                    rank = GetField("rank", 7) ?? "",
                    champions = GetField("champions", 8) ?? "",
                    skins = GetField("skins", 9) ?? "",
                    Loot = GetField("Loot", 12) ?? "",
                    rank2 = GetField("rank2", 14) ?? "",
                    lastPlayed = GetField("lastPlayed", -1) ?? "",
                    leagueMatchHistory = GetField("leagueMatchHistory", -1) ?? "",
                    note = GetField("note", 15) ?? "",
                    valorantAgents = GetField("valorantAgents", 16) ?? "",
                    valorantContracts = GetField("valorantContracts", 17) ?? "",
                    valorantSprays = GetField("valorantSprays", 18) ?? "",
                    valorantGunBuddies = GetField("valorantGunBuddies", 19) ?? "",
                    valorantCards = GetField("valorantCards", 20) ?? "",
                    valorantSkins = GetField("valorantSkins", 21) ?? "",
                    valorantSkinVariants = GetField("valorantSkinVariants", 22) ?? "",
                    valorantTitles = GetField("valorantTitles", 23) ?? "",
                    valorantVp = TryParseInt(GetField("valorantVp", 24)),
                    valorantRp = TryParseInt(GetFieldAny(25, "valorantRp", "valorantRpKc")),
                    valorantKc = TryParseInt(GetFieldAny(-1, "valorantKc")),
                    valorantLevel = TryParseInt(GetField("valorantLevel", 27)),
                    valorantRank = GetField("valorantRank", 28) ?? "",
                    valorantServer = GetField("valorantServer", -1) ?? "",
                    valorantXp = TryParseInt(GetField("valorantXp", -1))
                };

                int CountTokensLocal(string? v)
                {
                    if (string.IsNullOrWhiteSpace(v)) return 0;
                    return v.Split(':', StringSplitOptions.RemoveEmptyEntries).Length;
                }

                record.Champions = CountTokensLocal(record.champions);
                record.Skins = CountTokensLocal(record.skins);
                record.Loots = CountTokensLocal(record.Loot);

                records.Add(record);
            }
            catch
            {
                // skip broken row
            }

        return records;
    }

    private static string WriteCsvToString(IEnumerable<Utils.AccountList> records, CsvConfiguration config)
    {
        using var writer = new StringWriter(CultureInfo.CurrentCulture);
        using var csv = new CsvWriter(writer, config);

        var headers = new[]
        {
            "username", "password", "riotID", "level", "server", "be", "rp", "rank",
            "champions", "skins", "Champions", "Skins", "Loot", "Loots", "rank2", "lastPlayed", "leagueMatchHistory", "note",
            "valorantAgents", "valorantContracts", "valorantSprays", "valorantGunBuddies",
            "valorantCards", "valorantSkins", "valorantSkinVariants", "valorantTitles",
            "valorantVp", "valorantRp", "valorantKc", "valorantLevel", "valorantRank", "valorantServer", "valorantXp"
        };

        foreach (var h in headers)
            csv.WriteField(h);
        csv.NextRecord();

        foreach (var r in records)
        {
            int CountTokensLocal(string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return 0;
                return v.Split(':', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            var championsCount = CountTokensLocal(r.champions);
            var skinsCount = CountTokensLocal(r.skins);
            var lootsCount = CountTokensLocal(r.Loot);

            if (championsCount == 0 && r.Champions > 0) championsCount = r.Champions;
            if (skinsCount == 0 && r.Skins > 0) skinsCount = r.Skins;
            if (lootsCount == 0 && r.Loots > 0) lootsCount = r.Loots;

            csv.WriteField(r.username ?? "");
            csv.WriteField(r.password ?? "");
            csv.WriteField(r.riotID ?? "");
            csv.WriteField(r.level?.ToString() ?? "0");
            csv.WriteField(r.server ?? "");
            csv.WriteField(r.be?.ToString() ?? "0");
            csv.WriteField(r.rp?.ToString() ?? "0");
            csv.WriteField(r.rank ?? "");
            csv.WriteField(r.champions ?? "");
            csv.WriteField(r.skins ?? "");
            csv.WriteField(championsCount.ToString());
            csv.WriteField(skinsCount.ToString());
            csv.WriteField(r.Loot ?? "");
            csv.WriteField(lootsCount.ToString());
            csv.WriteField(r.rank2 ?? "");
            csv.WriteField(r.lastPlayed ?? "");
            csv.WriteField(r.leagueMatchHistory ?? "");
            csv.WriteField(r.note ?? "");
            csv.WriteField(r.valorantAgents ?? "");
            csv.WriteField(r.valorantContracts ?? "");
            csv.WriteField(r.valorantSprays ?? "");
            csv.WriteField(r.valorantGunBuddies ?? "");
            csv.WriteField(r.valorantCards ?? "");
            csv.WriteField(r.valorantSkins ?? "");
            csv.WriteField(r.valorantSkinVariants ?? "");
            csv.WriteField(r.valorantTitles ?? "");
            csv.WriteField(r.valorantVp?.ToString() ?? "0");
            csv.WriteField(r.valorantRp?.ToString() ?? "0");
            csv.WriteField(r.valorantKc?.ToString() ?? "0");
            csv.WriteField(r.valorantLevel?.ToString() ?? "0");
            csv.WriteField(r.valorantRank ?? "");
            csv.WriteField(r.valorantServer ?? "");
            csv.WriteField(r.valorantXp?.ToString() ?? "0");
            csv.NextRecord();
        }

        writer.Flush();
        return writer.ToString();
    }

    private static string WriteStorageToString(IEnumerable<Utils.AccountList> records)
    {
        var normalizedRecords = NormalizeStructuredCollections(records.ToList());
        var envelope = new StorageEnvelope
        {
            Schema = StorageSchema,
            Version = StorageVersion,
            Accounts = normalizedRecords
        };

        var node = JsonSerializer.SerializeToNode(envelope) as JsonObject;
        if (node == null)
            return JsonSerializer.Serialize(envelope);

        if (node["Accounts"] is JsonArray accountsArray)
            foreach (var accountNode in accountsArray.OfType<JsonObject>())
            {
                accountNode.Remove("champions");
                accountNode.Remove("skins");
                accountNode.Remove("Loot");
                accountNode.Remove("valorantAgents");
                accountNode.Remove("valorantContracts");
                accountNode.Remove("valorantSprays");
                accountNode.Remove("valorantGunBuddies");
                accountNode.Remove("valorantCards");
                accountNode.Remove("valorantSkins");
                accountNode.Remove("valorantSkinVariants");
                accountNode.Remove("valorantTitles");
            }

        return node.ToJsonString();
    }

    private static List<Utils.AccountList> NormalizeStructuredCollections(List<Utils.AccountList> records)
    {
        foreach (var record in records)
        {
            record.championsData = NormalizeStructuredEntries(record.championsData, record.champions);
            record.skinsData = NormalizeStructuredEntries(record.skinsData, record.skins);
            record.lootData = NormalizeStructuredEntries(record.lootData, record.Loot);

            if (string.IsNullOrWhiteSpace(record.champions))
                record.champions = SerializeDelimitedEntries(record.championsData);
            if (string.IsNullOrWhiteSpace(record.skins))
                record.skins = SerializeDelimitedEntries(record.skinsData);
            if (string.IsNullOrWhiteSpace(record.Loot))
                record.Loot = SerializeDelimitedEntries(record.lootData);

            record.valorantAgentsData = NormalizeStructuredEntries(record.valorantAgentsData, record.valorantAgents);
            record.valorantContractsData = NormalizeStructuredEntries(record.valorantContractsData, record.valorantContracts);
            record.valorantSpraysData = NormalizeStructuredEntries(record.valorantSpraysData, record.valorantSprays);
            record.valorantGunBuddiesData = NormalizeStructuredEntries(record.valorantGunBuddiesData, record.valorantGunBuddies);
            record.valorantCardsData = NormalizeStructuredEntries(record.valorantCardsData, record.valorantCards);
            record.valorantSkinsData = NormalizeStructuredEntries(record.valorantSkinsData, record.valorantSkins);
            record.valorantSkinVariantsData = NormalizeStructuredEntries(record.valorantSkinVariantsData, record.valorantSkinVariants);
            record.valorantTitlesData = NormalizeStructuredEntries(record.valorantTitlesData, record.valorantTitles);

            if (string.IsNullOrWhiteSpace(record.valorantAgents))
                record.valorantAgents = SerializeDelimitedEntries(record.valorantAgentsData);
            if (string.IsNullOrWhiteSpace(record.valorantContracts))
                record.valorantContracts = SerializeDelimitedEntries(record.valorantContractsData);
            if (string.IsNullOrWhiteSpace(record.valorantSprays))
                record.valorantSprays = SerializeDelimitedEntries(record.valorantSpraysData);
            if (string.IsNullOrWhiteSpace(record.valorantGunBuddies))
                record.valorantGunBuddies = SerializeDelimitedEntries(record.valorantGunBuddiesData);
            if (string.IsNullOrWhiteSpace(record.valorantCards))
                record.valorantCards = SerializeDelimitedEntries(record.valorantCardsData);
            if (string.IsNullOrWhiteSpace(record.valorantSkins))
                record.valorantSkins = SerializeDelimitedEntries(record.valorantSkinsData);
            if (string.IsNullOrWhiteSpace(record.valorantSkinVariants))
                record.valorantSkinVariants = SerializeDelimitedEntries(record.valorantSkinVariantsData);
            if (string.IsNullOrWhiteSpace(record.valorantTitles))
                record.valorantTitles = SerializeDelimitedEntries(record.valorantTitlesData);

            if (record.Champions == 0)
                record.Champions = record.championsData?.Count ?? 0;
            if (record.Skins == 0)
                record.Skins = record.skinsData?.Count ?? 0;
            if (record.Loots == 0)
                record.Loots = record.lootData?.Count ?? 0;
        }

        return records;
    }

    private static string SerializeDelimitedEntries(List<Utils.StructuredDataEntry>? entries)
    {
        if (entries == null || entries.Count == 0)
            return string.Empty;

        return string.Join(":", entries.Select(e =>
            string.Join("|", e.name ?? string.Empty, e.icon ?? string.Empty, e.value ?? string.Empty)));
    }

    private static List<Utils.StructuredDataEntry> ParseDelimitedEntries(string? value)
    {
        var entries = new List<Utils.StructuredDataEntry>();
        if (string.IsNullOrWhiteSpace(value))
            return entries;

        value = value.Trim();

        if (!value.Contains('|'))
        {
            foreach (var token in value.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                entries.Add(new Utils.StructuredDataEntry
                {
                    name = name,
                    icon = string.Empty,
                    value = string.Empty
                });
            }

            return entries;
        }

        var rawTokens = value.Split(new[] { ':' }, StringSplitOptions.None);
        var lines = new List<string>();
        for (var i = 0; i < rawTokens.Length; i++)
        {
            var current = rawTokens[i];
            var pipeCount = current.Count(c => c == '|');
            while (pipeCount < 2 && i + 1 < rawTokens.Length)
            {
                i++;
                current = current + ":" + rawTokens[i];
                pipeCount = current.Count(c => c == '|');
            }

            current = current.Trim();
            if (!string.IsNullOrEmpty(current))
                lines.Add(current);
        }

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            var entry = new Utils.StructuredDataEntry
            {
                name = parts.Length >= 1 ? parts[0].Trim() : string.Empty,
                icon = parts.Length >= 2 ? parts[1].Trim() : string.Empty,
                value = parts.Length >= 3 ? parts[2].Trim() : string.Empty
            };
            entries.Add(entry);
        }

        return entries;
    }

    private static List<Utils.StructuredDataEntry> NormalizeStructuredEntries(List<Utils.StructuredDataEntry>? existing,
        string? legacy)
    {
        var parsedFromLegacy = ParseDelimitedEntries(legacy);
        if (existing == null || existing.Count == 0)
            return parsedFromLegacy;

        var normalized = existing
            .Select(e => new Utils.StructuredDataEntry
            {
                name = (e.name ?? string.Empty).TrimStart(':').Trim(),
                icon = e.icon ?? string.Empty,
                value = e.value ?? string.Empty,
                extra = e.extra
            })
            .Where(e => !string.IsNullOrWhiteSpace(e.name) || !string.IsNullOrWhiteSpace(e.icon) ||
                        !string.IsNullOrWhiteSpace(e.value))
            .ToList();

        if (normalized.Count == 0)
            return parsedFromLegacy;

        var single = normalized.Count == 1 ? normalized[0] : null;
        if (single != null && string.IsNullOrWhiteSpace(single.icon) && string.IsNullOrWhiteSpace(single.value) &&
            (single.name?.Contains(':') == true))
        {
            var reparsed = ParseDelimitedEntries(single.name);
            if (reparsed.Count > 0)
                return reparsed;
        }

        return normalized;
    }

    private static int TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        value = value.Replace("\"", "").Replace("\'", "").Trim();

        return int.TryParse(value, out var result) ? result : 0;
    }

    private static bool IsEncrypted(byte[] data)
    {
        if (data.Length < EncryptionHeader.Length) return false;
        var header = Encoding.ASCII.GetBytes(EncryptionHeader);
        return data.AsSpan(0, header.Length).SequenceEqual(header);
    }

    private static byte[] Encrypt(string plainText, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(EncryptionHeader));
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipherBytes);
        return ms.ToArray();
    }

    private static string Decrypt(byte[] data, string password)
    {
        var headerBytes = Encoding.ASCII.GetBytes(EncryptionHeader);
        if (data.Length < headerBytes.Length + SaltSize + NonceSize + TagSize)
            throw new CryptographicException("Encrypted account data is incomplete.");

        var offset = headerBytes.Length;

        var salt = data.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;

        var nonce = data.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;

        var tag = data.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;

        var cipherText = data.AsSpan(offset).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
        var plainBytes = new byte[cipherText.Length];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(nonce, cipherText, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    private sealed class StorageEnvelope
    {
        public string Schema { get; set; } = string.Empty;
        public int Version { get; set; }
        public List<Utils.AccountList>? Accounts { get; set; }
    }

    private interface IAccountTransferFormat
    {
        string DisplayName { get; }
        IReadOnlyList<string> Extensions { get; }
        string Serialize(List<Utils.AccountList> records, CsvConfiguration config);
        List<Utils.AccountList> Deserialize(string content, CsvConfiguration config);
    }

    private sealed class StructuredJsonTransferFormat : IAccountTransferFormat
    {
        public string DisplayName => "LAM Structured JSON";
        public IReadOnlyList<string> Extensions => new[] { ".lam", ".lamjson", ".json" };

        public string Serialize(List<Utils.AccountList> records, CsvConfiguration config)
        {
            _ = config;
            var envelope = new StorageEnvelope
            {
                Schema = StorageSchema,
                Version = StorageVersion,
                Accounts = records
            };
            return JsonSerializer.Serialize(envelope);
        }

        public List<Utils.AccountList> Deserialize(string content, CsvConfiguration config)
        {
            return ReadStorageFromString(content, config).Records;
        }
    }

    private sealed class CsvTransferFormat : IAccountTransferFormat
    {
        public string DisplayName => "Legacy CSV";
        public IReadOnlyList<string> Extensions => new[] { ".csv" };

        public string Serialize(List<Utils.AccountList> records, CsvConfiguration config)
        {
            return WriteCsvToString(records, config);
        }

        public List<Utils.AccountList> Deserialize(string content, CsvConfiguration config)
        {
            return ReadCsvFromString(content, config);
        }
    }

    private sealed class StorageReadResult
    {
        public StorageReadResult(List<Utils.AccountList> records, bool usedLegacyFormat)
        {
            Records = records;
            UsedLegacyFormat = usedLegacyFormat;
        }

        public List<Utils.AccountList> Records { get; }
        public bool UsedLegacyFormat { get; }
    }
}
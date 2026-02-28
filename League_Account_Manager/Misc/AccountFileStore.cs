using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CsvHelper;
using CsvHelper.Configuration;
using League_Account_Manager.Windows;

namespace League_Account_Manager.Misc;

internal static class AccountFileStore
{
    private const string EncryptionHeader = "LAMENC1";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Pbkdf2Iterations = 100_000;

    public static event EventHandler? AccountsFileUpdated;

    public static string GetAccountsFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, $"{Settings.settingsloaded.filename}.csv");
    }

    public static bool IsEncryptionEnabled => Settings.settingsloaded.AccountFileEncryptionEnabled;

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
        if (!File.Exists(filePath)) return new List<Utils.AccountList>();

        List<Utils.AccountList> records;

        try
        {
            records = await LoadForMigrationAsync(filePath, config, GetPassword());
        }
        catch (CryptographicException)
        {
            records = await PromptPasswordUntilSuccessAsync(filePath, config);
        }

        var data = await File.ReadAllBytesAsync(filePath);
        if (IsEncryptionEnabled && !IsEncrypted(data))
            await SaveEncryptedAsync(filePath, records, config, GetPassword());
        else if (!IsEncryptionEnabled && IsEncrypted(data))
            await SavePlaintextAsync(filePath, records, config);

        return records;
    }

    public static async Task SaveAsync(string filePath, IEnumerable<Utils.AccountList> records, CsvConfiguration config)
    {
        if (IsEncryptionEnabled)
        {
            var password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            await SaveEncryptedAsync(filePath, records, config, password);
            AccountsFileUpdated?.Invoke(null, EventArgs.Empty);
            return;
        }

        await SavePlaintextAsync(filePath, records, config);
        AccountsFileUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static void Save(string filePath, IEnumerable<Utils.AccountList> records, CsvConfiguration config)
    {
        SaveAsync(filePath, records, config).GetAwaiter().GetResult();
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
        {
            Application.Current.Dispatcher.Invoke(ShowPrompt);
        }
        else
        {
            ShowPrompt();
        }

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
                var records = await LoadForMigrationAsync(filePath, config, password);
                SetPassword(password);
                return records;
            }
            catch (CryptographicException)
            {
            }
        }
    }

    public static async Task RewriteForEncryptionStateAsync(string filePath, CsvConfiguration config, bool encrypt,
        string? currentPassword, string? newPassword)
    {
        if (!File.Exists(filePath))
        {
            if (encrypt && !string.IsNullOrWhiteSpace(newPassword))
                await SaveEncryptedAsync(filePath, new List<Utils.AccountList>(), config, newPassword);
            return;
        }

        var records = await LoadForMigrationAsync(filePath, config, currentPassword);

        if (encrypt)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new InvalidOperationException("Account file encryption password not set.");

            await SaveEncryptedAsync(filePath, records, config, newPassword);
            return;
        }

        await SavePlaintextAsync(filePath, records, config);
    }

    private static async Task<List<Utils.AccountList>> LoadForMigrationAsync(string filePath, CsvConfiguration config,
        string? password)
    {
        await WaitForFileUnlockAsync(filePath);
        var data = await File.ReadAllBytesAsync(filePath);
        if (data.Length == 0) return new List<Utils.AccountList>();

        if (IsEncrypted(data))
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Account file encryption password not set.");

            var csvText = Decrypt(data, password);
            return ReadCsvFromString(csvText, config);
        }

        var plainText = Encoding.UTF8.GetString(data);
        return ReadCsvFromString(plainText, config);
    }

    private static async Task SavePlaintextAsync(string filePath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config)
    {
        await WaitForFileUnlockAsync(filePath);
        var csvText = WriteCsvToString(records, config);
        await File.WriteAllTextAsync(filePath, csvText);
    }

    private static async Task SaveEncryptedAsync(string filePath, IEnumerable<Utils.AccountList> records,
        CsvConfiguration config, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Account file encryption password not set.");

        await WaitForFileUnlockAsync(filePath);
        var csvText = WriteCsvToString(records, config);
        var encrypted = Encrypt(csvText, password);
        await File.WriteAllBytesAsync(filePath, encrypted);
    }

    private static async Task WaitForFileUnlockAsync(string filePath)
    {
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
                await Task.Delay(300);
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
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                    Champions = TryParseInt(GetField("Champions", 10)),
                    Skins = TryParseInt(GetField("Skins", 11)),
                    Loot = GetField("Loot", 12) ?? "",
                    Loots = TryParseInt(GetField("Loots", 13)),
                    rank2 = GetField("rank2", 14) ?? "",
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
        csv.WriteRecords(records);
        writer.Flush();
        return writer.ToString();
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

        using (var aes = new AesGcm(key))
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

        using (var aes = new AesGcm(key))
        {
            aes.Decrypt(nonce, cipherText, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}

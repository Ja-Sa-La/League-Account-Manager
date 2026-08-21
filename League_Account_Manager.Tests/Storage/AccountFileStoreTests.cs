using System.Globalization;
using CsvHelper.Configuration;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Storage;

[TestClass]
public class AccountFileStoreTests
{
    private readonly CsvConfiguration _configuration = new(CultureInfo.InvariantCulture) { Delimiter = ";" };
    private string _temporaryDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"lam-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
        global::League_Account_Manager.Misc.Settings.settingsloaded =
            global::League_Account_Manager.Misc.Settings.CreateDefaults();
        global::League_Account_Manager.Misc.Settings.settingsloaded.AccountFileEncryptionEnabled = false;
        AccountFileStore.SetPassword(null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, true);
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsStructuredAccountData()
    {
        var path = Path.Combine(_temporaryDirectory, "Accounts.LAM");
        var records = new List<Utils.AccountList>
        {
            new()
            {
                username = "player",
                password = "secret",
                riotID = "Player#EUW",
                championsData = [new Utils.StructuredDataEntry { name = "Ahri", value = "Mage" }]
            }
        };

        await AccountFileStore.SaveAsync(path, records, _configuration);
        var loaded = await AccountFileStore.LoadAsync(path, _configuration);

        Assert.HasCount(1, loaded);
        Assert.AreEqual("player", loaded[0].username);
        Assert.AreEqual("Player#EUW", loaded[0].riotID);
        Assert.AreEqual("Ahri", loaded[0].championsData?[0].name);
    }

    [TestMethod]
    public async Task RewriteForEncryptionState_RenamesWithoutLosingAccounts()
    {
        var source = Path.Combine(_temporaryDirectory, "Accounts.LAM");
        var destination = Path.Combine(_temporaryDirectory, "Personal.LAM");
        await AccountFileStore.SaveAsync(source,
            [new Utils.AccountList { username = "player", password = "secret" }], _configuration);

        await AccountFileStore.RewriteForEncryptionStateAsync(source, destination, _configuration, false, null, null);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(destination));
        var loaded = await AccountFileStore.LoadAsync(destination, _configuration);
        Assert.HasCount(1, loaded);
        Assert.AreEqual("player", loaded[0].username);
    }

    [TestMethod]
    public async Task RewriteForEncryptionState_DoesNotOverwriteExistingDestination()
    {
        var source = Path.Combine(_temporaryDirectory, "Accounts.LAM");
        var destination = Path.Combine(_temporaryDirectory, "Personal.LAM");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(destination, "destination");

        await Assert.ThrowsExactlyAsync<IOException>(() => AccountFileStore.RewriteForEncryptionStateAsync(
            source, destination, _configuration, false, null, null));

        Assert.AreEqual("source", await File.ReadAllTextAsync(source));
        Assert.AreEqual("destination", await File.ReadAllTextAsync(destination));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsFutureStorageVersionWithoutRewritingFile()
    {
        var path = Path.Combine(_temporaryDirectory, "Future.LAM");
        const string content = """
                               {
                                 "Schema": "LAM.Accounts",
                                 "Version": 3,
                                 "Accounts": [{ "username": "future-user", "futureField": "keep-me" }]
                               }
                               """;
        await File.WriteAllTextAsync(path, content);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => AccountFileStore.LoadAsync(path, _configuration));

        Assert.AreEqual(content, await File.ReadAllTextAsync(path));
    }
}
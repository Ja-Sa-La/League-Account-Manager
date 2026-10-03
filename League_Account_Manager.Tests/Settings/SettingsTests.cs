using System.Text.Json;
using System.Text.Json.Nodes;
using League_Account_Manager.Misc;
using Newtonsoft.Json;

namespace League_Account_Manager.Tests.Configuration;

[TestClass]
public class SettingsTests
{
    [TestMethod]
    public void CreateDefaults_ReturnsExpectedFirstRunSettings()
    {
        var result = Settings.CreateDefaults();

        Assert.AreEqual("Accounts", result.filename);
        Assert.IsTrue(result.updates);
        Assert.AreEqual(UpdateReleaseChannel.Stable.ToString(), result.ReleaseChannel);
        Assert.IsTrue(result.DisplayPasswords);
        Assert.IsTrue(result.UpdateRanks);
        Assert.IsFalse(result.AccountFileEncryptionEnabled);
        Assert.IsNull(result.AccountFileEncryptionPassword);
        Assert.AreEqual(PersistentLoginMode.Ask, result.PersistentLoginMode);
        Assert.IsFalse(result.CloudSyncPromptShown);
        CollectionAssert.AreEqual(Array.Empty<string>(), result.DisabledPluginPaths);
        Assert.AreEqual("level", result.LeagueDefaultSortColumn);
        Assert.IsTrue(result.LeagueDefaultSortDescending);
        Assert.AreEqual("valorantLevel", result.ValorantDefaultSortColumn);
        Assert.IsTrue(result.ValorantDefaultSortDescending);
        Assert.IsFalse(result.UseLegacyLogin);
    }

    [TestMethod]
    public void MergeWithDefaults_PreservesDefaultsMissingFromLegacyJson()
    {
        var result = Settings.MergeWithDefaults("{\"filename\":\"LegacyAccounts\"}");

        Assert.AreEqual("LegacyAccounts", result.filename);
        Assert.IsTrue(result.updates);
        Assert.AreEqual("Stable", result.ReleaseChannel);
        Assert.IsTrue(result.DisplayPasswords);
        Assert.IsTrue(result.UpdateRanks);
        Assert.AreEqual("level", result.LeagueDefaultSortColumn);
        Assert.IsTrue(result.LeagueDefaultSortDescending);
        Assert.AreEqual("valorantLevel", result.ValorantDefaultSortColumn);
        Assert.IsTrue(result.ValorantDefaultSortDescending);
        Assert.AreEqual(PersistentLoginMode.Ask, result.PersistentLoginMode);
        CollectionAssert.AreEqual(Array.Empty<string>(), result.DisabledPluginPaths);
    }

    [TestMethod]
    public void MergeWithDefaults_HonorsExplicitStoredValues()
    {
        var result = Settings.MergeWithDefaults("""
            {
              "updates": false,
              "ReleaseChannel": "Beta",
              "DisplayPasswords": false,
              "UpdateRanks": false,
              "LeagueDefaultSortDescending": false,
              "DisabledPluginPaths": ["Plugins\\\"disabled.dll"]
            }
            """);

        Assert.IsFalse(result.updates);
        Assert.AreEqual("Beta", result.ReleaseChannel);
        Assert.IsFalse(result.DisplayPasswords);
        Assert.IsFalse(result.UpdateRanks);
        Assert.IsFalse(result.LeagueDefaultSortDescending);
        CollectionAssert.AreEqual(new[] { "Plugins\\\"disabled.dll" }, result.DisabledPluginPaths);
    }

    [TestMethod]
    [DataRow(true, PersistentLoginMode.Always)]
    [DataRow(false, PersistentLoginMode.Never)]
    public void MergeWithDefaults_MigratesLegacyPersistentLogin(bool legacyValue, PersistentLoginMode expectedMode)
    {
        var result = Settings.MergeWithDefaults($"{{\"PersistentLogin\":{legacyValue.ToString().ToLowerInvariant()}}}");

        Assert.AreEqual(expectedMode, result.PersistentLoginMode);
    }

    [TestMethod]
    public void MergeWithDefaults_PrefersPersistentLoginModeOverLegacyValue()
    {
        var result = Settings.MergeWithDefaults("{\"PersistentLogin\":true,\"PersistentLoginMode\":2}");

        Assert.AreEqual(PersistentLoginMode.Never, result.PersistentLoginMode);
    }

    [TestMethod]
    public void MergeWithDefaults_RejectsInvalidJson()
    {
        Assert.ThrowsExactly<JsonReaderException>(() => Settings.MergeWithDefaults("not-json"));
    }

    [TestMethod]
    public void ApplySyncDocument_UpdatesSupportedValuesAndPreservesLocalValues()
    {
        var previous = Settings.settingsloaded;
        try
        {
            Settings.settingsloaded = Settings.CreateDefaults();
            Settings.settingsloaded.LeaguePath = "local-league";
            Settings.settingsloaded.DisabledPluginPaths = new[] { "local-plugin.dll" };

            Settings.ApplySyncDocument("""
                {
                  "updates": false,
                  "ReleaseChannel": "Beta",
                  "DisplayPasswords": false,
                  "UpdateRanks": false,
                  "PersistentLoginMode": 2,
                  "LeagueDefaultSortColumn": "name",
                  "LeagueDefaultSortDescending": false,
                  "ValorantDefaultSortColumn": "rank",
                  "ValorantDefaultSortDescending": false
                }
                """);

            Assert.IsFalse(Settings.settingsloaded.updates);
            Assert.AreEqual("Beta", Settings.settingsloaded.ReleaseChannel);
            Assert.AreEqual(PersistentLoginMode.Never, Settings.settingsloaded.PersistentLoginMode);
            Assert.AreEqual("name", Settings.settingsloaded.LeagueDefaultSortColumn);
            Assert.AreEqual("rank", Settings.settingsloaded.ValorantDefaultSortColumn);
            Assert.AreEqual("local-league", Settings.settingsloaded.LeaguePath);
            CollectionAssert.AreEqual(new[] { "local-plugin.dll" }, Settings.settingsloaded.DisabledPluginPaths);
        }
        finally
        {
            Settings.settingsloaded = previous;
        }
    }

    [TestMethod]
    public void CreateSyncDocument_ExcludesMachineSpecificAndSensitiveValues()
    {
        var previous = Settings.settingsloaded;
        try
        {
            Settings.settingsloaded = Settings.CreateDefaults();
            Settings.settingsloaded.LeaguePath = "C:\\Games\\LeagueClient.exe";
            Settings.settingsloaded.riotPath = "C:\\Riot\\RiotClientServices.exe";
            Settings.settingsloaded.settingsLocation = "C:\\Games\\game.cfg";
            Settings.settingsloaded.AccountFileEncryptionEnabled = true;
            Settings.settingsloaded.AccountFileEncryptionPassword = "secret";
            Settings.settingsloaded.CloudSyncPromptShown = true;
            Settings.settingsloaded.DisabledPluginPaths = new[] { "local.dll" };
            Settings.settingsloaded.ReleaseChannel = "Beta";

            var document = JsonNode.Parse(Settings.CreateSyncDocument())!.AsObject();

            Assert.IsFalse(document.ContainsKey("LeaguePath"));
            Assert.IsFalse(document.ContainsKey("riotPath"));
            Assert.IsFalse(document.ContainsKey("settingsLocation"));
            Assert.IsFalse(document.ContainsKey("AccountFileEncryptionEnabled"));
            Assert.IsFalse(document.ContainsKey("AccountFileEncryptionPassword"));
            Assert.IsFalse(document.ContainsKey("CloudSyncPromptShown"));
            Assert.IsFalse(document.ContainsKey("DisabledPluginPaths"));
            Assert.AreEqual("Beta", document["ReleaseChannel"]!.GetValue<string>());
        }
        finally
        {
            Settings.settingsloaded = previous;
        }
    }
}

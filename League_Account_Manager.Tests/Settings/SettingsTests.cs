using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Configuration;

[TestClass]
public class SettingsTests
{
    [TestMethod]
    public void MergeWithDefaults_PreservesDefaultsMissingFromLegacyJson()
    {
        var result = global::League_Account_Manager.Misc.Settings.MergeWithDefaults(
            "{\"filename\":\"LegacyAccounts\"}");

        Assert.AreEqual("LegacyAccounts", result.filename);
        Assert.IsTrue(result.updates);
        Assert.AreEqual("Stable", result.ReleaseChannel);
        Assert.IsTrue(result.DisplayPasswords);
        Assert.IsTrue(result.UpdateRanks);
        Assert.AreEqual("level", result.LeagueDefaultSortColumn);
        Assert.IsTrue(result.LeagueDefaultSortDescending);
        Assert.AreEqual("valorantLevel", result.ValorantDefaultSortColumn);
        Assert.IsTrue(result.ValorantDefaultSortDescending);
    }

    [TestMethod]
    public void MergeWithDefaults_HonorsExplicitStoredValues()
    {
        var result = global::League_Account_Manager.Misc.Settings.MergeWithDefaults("""
                                                     {
                                                       "updates": false,
                                                       "ReleaseChannel": "Beta",
                                                       "DisplayPasswords": false,
                                                       "UpdateRanks": false,
                                                       "LeagueDefaultSortDescending": false
                                                     }
                                                     """);

        Assert.IsFalse(result.updates);
        Assert.AreEqual("Beta", result.ReleaseChannel);
        Assert.IsFalse(result.DisplayPasswords);
        Assert.IsFalse(result.UpdateRanks);
        Assert.IsFalse(result.LeagueDefaultSortDescending);
    }
}
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class UtilsTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", "")]
    [DataRow("plain", "plain")]
    [DataRow("\"quoted\"", "quoted")]
    [DataRow("a\"b\"c", "abc")]
    public void RemoveDoubleQuotes_RemovesAllQuoteCharacters(string? input, string? expected)
    {
        Assert.AreEqual(expected, Utils.RemoveDoubleQuotes(input));
    }

    [TestMethod]
    public void RemoveDoubleQuotesFromList_SanitizesScalarAndStructuredValues()
    {
        var account = new Utils.AccountList
        {
            username = "\"user\"",
            password = "p\"ass",
            championsData =
            [
                new Utils.StructuredDataEntry
                {
                    name = "\"Ahri\"",
                    icon = "\"icon.png\"",
                    value = "\"owned\"",
                    extra = new Dictionary<string, string> { ["skin"] = "\"Classic\"" }
                }
            ]
        };

        Utils.RemoveDoubleQuotesFromList([account]);

        Assert.AreEqual("user", account.username);
        Assert.AreEqual("pass", account.password);
        Assert.AreEqual("Ahri", account.championsData[0].name);
        Assert.AreEqual("icon.png", account.championsData[0].icon);
        Assert.AreEqual("owned", account.championsData[0].value);
        Assert.AreEqual("Classic", account.championsData[0].extra!["skin"]);
    }

    [TestMethod]
    public void FormatAccountForCopy_BasicSimpleUsesCredentialFields()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            password = "pass",
            riotID = "Player#123"
        };

        var result = Utils.FormatAccountForCopy(account, false, Utils.AccountCopyFormat.Simple);

        Assert.AreEqual("user | pass | Player#123", result);
    }

    [TestMethod]
    public void FormatAccountForCopy_BasicFormattedUsesLabeledLines()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            password = "pass",
            riotID = "Player#123"
        };

        var result = Utils.FormatAccountForCopy(account, false, Utils.AccountCopyFormat.Formatted);

        StringAssert.Contains(result, "**Username:** user");
        StringAssert.Contains(result, "**Password:** pass");
        StringAssert.Contains(result, "**Riot ID:** Player#123");
    }

    [TestMethod]
    public void FormatAccountForCopy_FullSimpleIncludesLeagueAndValorantDetailsOnce()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            password = "pass",
            riotID = "Player#123",
            level = 30,
            valorantRank = "Gold"
        };

        var result = Utils.FormatAccountForCopy(account, true, Utils.AccountCopyFormat.Simple);

        Assert.AreEqual(1, result.Split("user", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(result, "30");
        StringAssert.Contains(result, "Gold");
    }

    [TestMethod]
    public void FormatAccountForCopy_DiscordFormatNormalizesMultilineValues()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            password = "pass",
            note = "first line\nsecond line"
        };

        var result = Utils.FormatAccountForCopy(account, true, Utils.AccountCopyFormat.Formatted);

        StringAssert.Contains(result, "**Note:** first line second line");
    }

    [TestMethod]
    public void FormatAccountForCopy_LeagueSectionExcludesValorantFields()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            level = 30,
            valorantLevel = 50,
            valorantRank = "Gold",
            valorantAgents = "one:two:three"
        };

        var result = Utils.FormatAccountForCopy(account, true, Utils.AccountCopyFormat.Formatted,
            Utils.AccountCopySection.League);

        StringAssert.Contains(result, "**Level:** 30");
        Assert.IsFalse(result.Contains("Gold"));
        Assert.IsFalse(result.Contains("Agents"));
    }

    [TestMethod]
    public void FormatAccountForCopy_ValorantSectionDisplaysFullInventory()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            valorantLevel = 50,
            valorantRank = "Gold",
            valorantAgents = "one:two:three",
            valorantAgentsData =
            [
                new Utils.StructuredDataEntry { name = "Jett" },
                new Utils.StructuredDataEntry { name = "Sage" }
            ]
        };

        var result = Utils.FormatAccountForCopy(account, true, Utils.AccountCopyFormat.Formatted,
            Utils.AccountCopySection.Valorant);

        StringAssert.Contains(result, "**VALORANT**");
        StringAssert.Contains(result, "**Agents:** Jett, Sage");
        Assert.IsFalse(result.Contains("3 items"));
    }

    [TestMethod]
    public void FormatAccountForCopy_BothSimpleIncludesBothGameSections()
    {
        var account = new Utils.AccountList
        {
            username = "user",
            level = 30,
            valorantLevel = 50
        };

        var result = Utils.FormatAccountForCopy(account, false, Utils.AccountCopyFormat.Simple,
            Utils.AccountCopySection.Both);

        StringAssert.Contains(result, "30");
        StringAssert.Contains(result, "50");
    }
}
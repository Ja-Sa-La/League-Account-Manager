using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class AccountListTests
{
    [TestMethod]
    public void ValorantCounts_CountNonEmptyColonSeparatedEntries()
    {
        var account = new Utils.AccountList
        {
            valorantAgents = "Jett:Sage:",
            valorantContracts = null,
            valorantSprays = "Spray One",
            valorantGunBuddies = "",
            valorantCards = "Card One:Card Two",
            valorantSkins = "Skin One:Skin Two",
            valorantSkinVariants = "Variant One",
            valorantTitles = "Title One"
        };

        Assert.AreEqual(2, account.ValorantAgentsCount);
        Assert.AreEqual(0, account.ValorantContractsCount);
        Assert.AreEqual(1, account.ValorantSpraysCount);
        Assert.AreEqual(0, account.ValorantGunBuddiesCount);
        Assert.AreEqual(2, account.ValorantCardsCount);
        Assert.AreEqual(3, account.ValorantSkinsCount);
        Assert.AreEqual(1, account.ValorantTitlesCount);
    }
}
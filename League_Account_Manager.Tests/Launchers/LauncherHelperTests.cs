using System.Text;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Launchers;

[TestClass]
public class LauncherHelperTests
{
    [TestMethod]
    public void ExtractLoginToken_FindsSuccessAndNestedTokenFields()
    {
        Assert.AreEqual("success-token",
            ProxyLoginTokenManager.ExtractLoginToken("{\"success\":{\"login_token\":\"success-token\"}}"));
        Assert.AreEqual("nested-token",
            ProxyLoginTokenManager.ExtractLoginToken("{\"data\":[{\"loginToken\":\"nested-token\"}]}"));
        Assert.IsNull(ProxyLoginTokenManager.ExtractLoginToken("{\"success\":{}}"));
        Assert.IsNull(ProxyLoginTokenManager.ExtractLoginToken("invalid"));
    }

    [TestMethod]
    public void ExtractTokenFromText_AcceptsSupportedUrisAndMarkdown()
    {
        Assert.AreEqual("abc+/=", ProxyLoginTokenManager.ExtractTokenFromText(
            "leagueaccountmanager://login?token=abc%2B%2F%3D"));
        Assert.AreEqual("https-token", ProxyLoginTokenManager.ExtractTokenFromText(
            "https://redirect.leagueaccountmanager.xyz/?token=https-token"));
        Assert.AreEqual("markdown-token", ProxyLoginTokenManager.ExtractTokenFromText(
            "[Click to login](https://redirect.leagueaccountmanager.xyz/?token=markdown-token)"));
    }

    [TestMethod]
    public void ExtractTokenFromText_RejectsUnsupportedHostsAndSchemes()
    {
        Assert.IsNull(ProxyLoginTokenManager.ExtractTokenFromText("https://example.com/?token=stolen"));
        Assert.IsNull(ProxyLoginTokenManager.ExtractTokenFromText(
            "leagueaccountmanager://other?token=stolen"));
        Assert.IsNull(ProxyLoginTokenManager.ExtractTokenFromText("file:///tmp/token?token=stolen"));
    }

    [TestMethod]
    public void BuildLoginUriAndDiscordLink_EscapeTokenSafely()
    {
        var uri = ProxyLoginTokenManager.BuildLoginUri("abc+/=");

        Assert.AreEqual("https://redirect.leagueaccountmanager.xyz/login?token=abc%2B%2F%3D", uri);
        Assert.AreEqual($"[Click to login to account]({uri})",
            ProxyLoginTokenManager.FormatDiscordLoginLink(uri!));
        Assert.IsNull(ProxyLoginTokenManager.BuildLoginUri(" "));
    }

    [TestMethod]
    public void GetProductFromEncodedTokenOrDefault_NormalizesSupportedProducts()
    {
        var valorant = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "{\"loginToken\":\"token\",\"authenticationType\":\"Riot Auth\",\"persistLogin\":false,\"product\":\"valorant\"}"));
        var unknown = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "{\"loginToken\":\"token\",\"product\":\"unknown\"}"));

        Assert.AreEqual("valorant", ProxyLoginTokenManager.GetProductFromEncodedTokenOrDefault(valorant));
        Assert.AreEqual("league", ProxyLoginTokenManager.GetProductFromEncodedTokenOrDefault(unknown));
        Assert.AreEqual("league", ProxyLoginTokenManager.GetProductFromEncodedTokenOrDefault("not-base64"));
    }

    [TestMethod]
    public void OfflineLauncherHelpers_NormalizePathsAndPowerShellQuotes()
    {
        Assert.AreEqual("/", OfflineLauncher.NormalizeConfigPath(null));
        Assert.AreEqual("/", OfflineLauncher.NormalizeConfigPath(""));
        Assert.AreEqual("/path/to/config", OfflineLauncher.NormalizeConfigPath("///path/to/config"));
        Assert.AreEqual("C:\\Users\\O''Brien", OfflineLauncher.EscapePowerShellSingleQuotedString(
            "C:\\Users\\O'Brien"));
    }
}
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class DebugClientTrafficLauncherTests
{
    [TestMethod]
    public void RewriteConfig_LeavesSharedAndAuthenticationOriginsDirect()
    {
        using var launcher = new DebugClientTrafficLauncher();
        const string config = """
                              {
                                "config": "https://clientconfig.rpg.riotgames.com/api/v1/configuration",
                                "auth": "https://auth.riotgames.com/api/v1/login",
                                "authenticate": "https://authenticate.riotgames.com/api/v1/login",
                                "cdn": "https://riot-client.secure.dyn.riotcdn.net/channel",
                                "template": "https://edge.%1.pmc.pay.riotgames.com"
                              }
                              """;

        var rewritten = launcher.RewriteConfig(config);

        Assert.AreEqual(config, rewritten);
    }

    [TestMethod]
    public void RewriteConfig_UsesOneLoopbackProxyPerServiceOrigin()
    {
        using var launcher = new DebugClientTrafficLauncher();
        const string config = """
                              {
                                "first": "https://euw-red.lol.sgp.pvp.net/path-a",
                                "second": "https://euw-red.lol.sgp.pvp.net/path-b",
                                "other": "https://usw2-red.pp.sgp.pvp.net/path"
                              }
                              """;

        var rewritten = launcher.RewriteConfig(config);
        var matches = System.Text.RegularExpressions.Regex.Matches(rewritten, "http://127\\.0\\.0\\.1:(\\d+)");

        Assert.HasCount(3, matches);
        Assert.AreEqual(matches[0].Groups[1].Value, matches[1].Groups[1].Value);
        Assert.AreNotEqual(matches[0].Groups[1].Value, matches[2].Groups[1].Value);
    }
}
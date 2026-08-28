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

      [TestMethod]
      public void RewriteConfig_RoutesRmsWebSocketOriginAndPreservesPath()
      {
        using var launcher = new DebugClientTrafficLauncher();
        const string config = "{\"rms\":\"wss://eu.edge.rms.si.riotgames.com:443/v1/events\"}";

        var rewritten = launcher.RewriteConfig(config);

        StringAssert.Matches(rewritten,
          new System.Text.RegularExpressions.Regex("ws://127\\.0\\.0\\.1:\\d+/v1/events"));
      }

      [TestMethod]
      public void RewriteConfig_RoutesRtmpLcdsSettings()
      {
        using var launcher = new DebugClientTrafficLauncher();
        const string config = "{\"lcds.lcds_host\":\"feapp.euw1.lol.pvp.net\",\"lcds.lcds_port\":2099,\"lcds.use_tls\":true}";

        var rewritten = launcher.RewriteConfig(config);
        var json = Newtonsoft.Json.Linq.JObject.Parse(rewritten);

        Assert.AreEqual("127.0.0.1", json["lcds.lcds_host"]?.ToString());
        Assert.AreNotEqual(2099, int.Parse(json["lcds.lcds_port"]?.ToString() ?? "0"));
        Assert.AreEqual("False", json["lcds.use_tls"]?.ToString());
      }
}
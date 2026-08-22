using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Tests.Updater;

[TestClass]
public class UpdatesTests
{
    [TestMethod]
    public void SelectRelease_UsesLegacyVersionForStableChannel()
    {
        var manifest = JObject.Parse("""{"Version":"2.5.0.0"}""");

        var release = Updates.SelectRelease(manifest, "Stable", "2.4.0.11");

        Assert.IsNotNull(release);
        Assert.AreEqual("Stable", release.Channel);
        Assert.AreEqual("2.5.0.0", release.Version);
        StringAssert.Contains(release.DownloadUrl, "/releases/latest/download/");
    }

    [TestMethod]
    public void SelectRelease_UsesBetaVersionAndUrlsForBetaChannel()
    {
        var manifest = JObject.Parse("""
                                     {
                                       "Stable": { "Version": "2.5.0.0" },
                                       "Beta": {
                                         "Version": "2.6.0.0",
                                         "DownloadUrl": "https://example.test/beta.exe",
                                         "ReleaseUrl": "https://example.test/beta"
                                       }
                                     }
                                     """);

        var release = Updates.SelectRelease(manifest, "Beta", "2.5.0.0");

        Assert.IsNotNull(release);
        Assert.AreEqual("Beta", release.Channel);
        Assert.AreEqual("2.6.0.0", release.Version);
        Assert.AreEqual("https://example.test/beta.exe", release.DownloadUrl);
        Assert.AreEqual("https://example.test/beta", release.ReleaseUrl);
    }

    [TestMethod]
    public void SelectRelease_DoesNotOfferCurrentOrOlderVersion()
    {
        var manifest = JObject.Parse("""{"Version":"2.4.0.10"}""");

        var release = Updates.SelectRelease(manifest, "Stable", "2.4.0.11");

        Assert.IsNull(release);
    }

    [TestMethod]
    public void SelectRelease_RejectsBetaWithoutExplicitReleaseUrls()
    {
        var manifest = JObject.Parse("""{"Beta":{"Version":"2.6.0.0"}}""");

        var release = Updates.SelectRelease(manifest, "Beta", "2.5.0.0");

        Assert.IsNull(release);
    }

        [TestMethod]
        public void SelectRelease_UsesBetaReleaseWhenBothChannelsAreNewer()
        {
                var manifest = JObject.Parse("""
                                                                         {
                                                                             "Stable": { "Version": "2.7.0.0" },
                                                                             "Beta": {
                                                                                 "Version": "2.6.0.0",
                                                                                 "DownloadUrl": "https://example.test/beta.exe",
                                                                                 "ReleaseUrl": "https://example.test/beta"
                                                                             }
                                                                         }
                                                                         """);

                var release = Updates.SelectRelease(manifest, "Beta", "2.5.0.0");

                Assert.IsNotNull(release);
                Assert.AreEqual("Beta", release.Channel);
                Assert.AreEqual("2.6.0.0", release.Version);
        }

    [TestMethod]
    public void SelectRelease_FallsBackToStableForUnknownChannel()
    {
        var manifest = JObject.Parse("""{"Version":"2.5.0.0"}""");

        var release = Updates.SelectRelease(manifest, "Canary", "2.4.0.11");

        Assert.IsNotNull(release);
        Assert.AreEqual("Stable", release.Channel);
    }
}
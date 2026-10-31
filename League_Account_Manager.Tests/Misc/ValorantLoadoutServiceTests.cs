using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class ValorantLoadoutServiceTests
{
    [TestMethod]
    [DataRow("latam", "na")]
    [DataRow("br", "na")]
    [DataRow("na", "na")]
    [DataRow("pbe", "na")]
    [DataRow("EU", "eu")]
    [DataRow("ap", "ap")]
    [DataRow("kr", "kr")]
    [DataRow(null, "na")]
    public void ShardForRegion_MapsLiveRegionsToPdShards(string? region, string expected)
    {
        Assert.AreEqual(expected, ValorantLoadoutService.ShardForRegion(region));
    }

    [TestMethod]
    public void RegionFromAccessToken_UsesValorantAffinityNotLeagueRegion()
    {
        const string payload = """
            {"pp":{"c":"eu","p":"playerplatform"},"dat":{"r":"NA1","c":"ue1"}}
            """;
        var token = "header." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

        Assert.AreEqual("eu", ValorantLoadoutService.RegionFromAccessToken(token));
        Assert.IsNull(ValorantLoadoutService.RegionFromAccessToken("not-a-jwt"));
        Assert.IsNull(ValorantLoadoutService.RegionFromAccessToken(null));
    }

    [TestMethod]
    public void ParseLoadout_ReadsEquippedWeaponSkins()
    {
        const string body = """
            {
              "Subject": "player",
              "Version": 4,
              "Guns": [
                {
                  "ID": "weapon-1",
                  "SkinID": "skin-1",
                  "SkinLevelID": "level-1",
                  "ChromaID": "chroma-1",
                  "CharmID": "charm-1"
                },
                { "ID": "" }
              ]
            }
            """;

        var loadout = ValorantLoadoutService.ParseLoadout(body);

        Assert.AreEqual(1, loadout.Guns.Count);
        Assert.AreEqual("weapon-1", loadout.Guns[0].WeaponId);
        Assert.AreEqual("skin-1", loadout.Guns[0].SkinId);
        Assert.AreEqual("chroma-1", loadout.Guns[0].ChromaId);
        Assert.AreEqual("charm-1", loadout.Guns[0].CharmId);
    }

    [TestMethod]
    public void ReadSelectedCharacter_FindsTheLocalPlayerOnEitherTeam()
    {
        const string match = """
            {
              "AllyTeam": null,
              "Teams": [
                {
                  "TeamID": "Blue",
                  "Players": [ { "Subject": "someone-else", "CharacterID": "jett" } ]
                },
                {
                  "TeamID": "Red",
                  "Players": [ { "Subject": "local-player", "CharacterID": "sage" } ]
                }
              ]
            }
            """;

        Assert.AreEqual("sage", ValorantLoadoutService.ReadSelectedCharacter(match, "local-player"));
        Assert.IsNull(ValorantLoadoutService.ReadSelectedCharacter(match, "missing"));
    }

    [TestMethod]
    public void OwnedOptions_KeepsStandardAndOwnedSkinsOnly()
    {
        const string weapons = """
            {
              "data": [
                {
                  "uuid": "vandal",
                  "displayName": "Vandal",
                  "category": "EEquippableCategory::Rifle",
                  "displayIcon": "https://example.test/vandal.png",
                  "skins": [ { "uuid": "standard" }, { "uuid": "prime" }, { "uuid": "random" } ]
                }
              ]
            }
            """;
        const string skins = """
            {
              "data": [
                {
                  "uuid": "standard",
                  "displayName": "Standard Vandal",
                  "levels": [ { "uuid": "standard-level" } ],
                  "chromas": [ { "uuid": "standard-chroma", "displayName": "Standard Vandal" } ]
                },
                {
                  "uuid": "prime",
                  "displayName": "Prime Vandal",
                  "levels": [
                    { "uuid": "prime-level-1" },
                    { "uuid": "prime-level-2" },
                    { "uuid": "prime-level-3" }
                  ],
                  "chromas": [
                    { "uuid": "prime-chroma", "displayName": "Prime Vandal" },
                    { "uuid": "prime-gold", "displayName": "Prime Vandal Gold" }
                  ]
                },
                {
                  "uuid": "random",
                  "displayName": "Random Favorite Skin",
                  "displayIcon": "https://example.test/random.png",
                  "levels": [ { "uuid": "random-level" } ],
                  "chromas": [ { "uuid": "random-chroma", "displayName": "Standard" } ]
                }
              ]
            }
            """;
        const string agents = """
            {
              "data": [
                {
                  "uuid": "sage",
                  "displayName": "Sage",
                  "displayIcon": "https://example.test/sage-icon.png",
                  "fullPortrait": "https://example.test/sage.png",
                  "role": { "displayName": "Sentinel" }
                }
              ]
            }
            """;

        var catalog = ValorantLoadoutService.WeaponCatalog.Parse(weapons, skins, agents);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "prime-level-1", "prime-level-2" };

        Assert.IsTrue(catalog.IsStandardSkin("standard"));
        Assert.IsFalse(catalog.IsStandardSkin("prime"));
        Assert.IsFalse(catalog.IsStandardSkin("random"));

        var options = catalog.OwnedOptions("vandal", owned);

        CollectionAssert.AreEquivalent(
            new[] { "standard-chroma", "prime-chroma", "random-chroma" },
            options.Select(option => option.ChromaId).ToArray());
        Assert.AreEqual("https://example.test/random.png",
            options.Single(option => option.ChromaId == "random-chroma").IconUrl);
        Assert.AreEqual("Sage", catalog.AgentName("sage"));
        Assert.AreEqual("https://example.test/sage.png", catalog.Agent("sage")?.PortraitUrl);
        Assert.AreEqual("Sentinel", catalog.Agent("sage")?.Role);
        Assert.AreEqual("Vandal", catalog.Weapon("vandal")?.Name);
        Assert.AreEqual("prime-level-2", catalog.HighestOwnedLevel("prime", owned));
    }

    [TestMethod]
    public void Parse_UsesSkinArtAndKeepsTheChromaSwatch()
    {
        const string weapons = """
            {
              "data": [
                {
                  "uuid": "judge",
                  "displayName": "Judge",
                  "category": "EEquippableCategory::Shotgun",
                  "skins": [ { "uuid": "holo" } ]
                }
              ]
            }
            """;
        const string skins = """
            {
              "data": [
                {
                  "uuid": "holo",
                  "displayName": "Holo Meridian Judge",
                  "displayIcon": "https://example.test/holo.png",
                  "levels": [ { "uuid": "holo-level", "displayIcon": "https://example.test/holo-level.png" } ],
                  "chromas": [
                    {
                      "uuid": "holo-base",
                      "displayName": "Holo Meridian Judge",
                      "swatch": "https://example.test/base-swatch.png"
                    },
                    {
                      "uuid": "holo-red",
                      "displayName": "Holo Meridian Judge Red",
                      "displayIcon": "https://example.test/red.png",
                      "swatch": "https://example.test/red-swatch.png"
                    }
                  ]
                }
              ]
            }
            """;

        var options = ValorantLoadoutService.WeaponCatalog
            .Parse(weapons, skins, """{ "data": [] }""")
            .OwnedOptions("judge", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "holo-level" });
        var option = options.Single(item => item.ChromaId == "holo-base");

        Assert.AreEqual("https://example.test/holo.png", option.IconUrl);
        Assert.AreEqual("https://example.test/base-swatch.png", option.ChromaIconUrl);
        Assert.IsFalse(options.Any(item => item.ChromaId == "holo-red"));
    }

    [TestMethod]
    public void Parse_UsesTheSkinImageWhenTheChromaRenderIsTheOnlyChromaArt()
    {
        const string weapons = """
            {
              "data": [
                {
                  "uuid": "vandal",
                  "displayName": "Vandal",
                  "category": "EEquippableCategory::Rifle",
                  "skins": [ { "uuid": "random" } ]
                }
              ]
            }
            """;
        const string skins = """
            {
              "data": [
                {
                  "uuid": "random",
                  "displayName": "Random Favorite Skin",
                  "displayIcon": "https://example.test/random.png",
                  "levels": [ { "uuid": "random-level", "displayIcon": "https://example.test/random-level.png" } ],
                  "chromas": [
                    {
                      "uuid": "random-chroma",
                      "displayName": "Standard",
                      "fullRender": "https://example.test/blank-render.png"
                    }
                  ]
                }
              ]
            }
            """;

        var option = ValorantLoadoutService.WeaponCatalog
            .Parse(weapons, skins, """{ "data": [] }""")
            .OwnedOptions("vandal", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "random" })
            .Single();

        Assert.AreEqual("https://example.test/random.png", option.IconUrl);
        Assert.IsTrue(option.IsRandomFavorite);
    }
}

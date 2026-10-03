using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class RunePageServiceTests
{
    [TestMethod]
    public void ParseUgg_UsesMostPlayedRoleWhenRoleIsAutomatic()
    {
        var json = $$"""
            {
              "1": {"1": {"3": [[{{UggBuild(8100, 8300, [8112, 8139, 8137, 8135, 8345, 8347], [5005, 5008, 5001], 10)}}]], "4": [[{{UggBuild(8400, 8200, [8437, 8446, 8444, 8451, 8226, 8210], [5008, 5008, 5001], 1000)}}]] } },
              "5": {"1": {"1": [[{{UggBuild(8200, 8100, [8992, 8226, 8210, 8237, 8139, 8106], [5005, 5008, 5001], 500)}}]] } }
            }
            """;

        var page = RunePageService.ParseUgg(json, "automatic");

        Assert.AreEqual(8100, page.PrimaryStyleId);
        CollectionAssert.AreEqual(new[] { 8112, 8139, 8137, 8135 }, page.PerkIds);
        CollectionAssert.AreEqual(new[] { 8345, 8347 }, page.SecondaryPerkIds);
        CollectionAssert.AreEqual(new[] { 5005, 5008, 5001 }, page.StatShardIds);
    }

    [TestMethod]
    public void ParseUgg_UsesRequestedRole()
    {
        var json = $$"""
            { "5": {"1": {"1": [[{{UggBuild(8200, 8100, [8992, 8226, 8210, 8237, 8139, 8106], [5008, 5010, 5011], 500)}}]] } } }
            """;

        var page = RunePageService.ParseUgg(json, "middle");

        Assert.AreEqual(8200, page.PrimaryStyleId);
        Assert.AreEqual(8100, page.SubStyleId);
        Assert.AreEqual(8992, page.PerkIds[0]);
        CollectionAssert.AreEqual(new[] { 5008, 5010, 5011 }, page.StatShardIds);
    }

    private static string UggBuild(int primary, int secondary, int[] runes, int[] shards, int games) =>
        $"[0,0,{primary},{secondary},[{string.Join(",", runes)}],0,0,0,[0,0,[\"{shards[0]}\",\"{shards[1]}\",\"{shards[2]}\"]],0,0,0,{games}]";

    [TestMethod]
    public void ParseOpGg_ReadsActiveRunesAndShards()
    {
        var html = string.Join(",",
            "\\\"primary_perk_style\\\":{\\\"id\\\":8200,\"name\\\":\\\"Sorcery\\\"}",
            "\\\"perk_sub_style\\\":{\\\"id\\\":8100}",
            Active(8992, "Deathfire Touch", "perk"),
            Active(8226, "Manaflow Band", "perk"),
            Active(8210, "Transcendence", "perk"),
            Active(8237, "Scorch", "perk"),
            Active(8139, "Taste of Blood", "perk"),
            Active(8106, "Ultimate Hunter", "perk"),
            Active(5005, "Attack Speed", "perkShard"),
            Active(5008, "Adaptive Force", "perkShard"),
            Active(5001, "Health", "perkShard"));

        var page = RunePageService.ParseOpGg(html);

        Assert.AreEqual(8200, page.PrimaryStyleId);
        Assert.AreEqual(8100, page.SubStyleId);
        CollectionAssert.AreEqual(new[] { 8992, 8226, 8210, 8237 }, page.PerkIds);
        CollectionAssert.AreEqual(new[] { 8139, 8106 }, page.SecondaryPerkIds);
        CollectionAssert.AreEqual(new[] { 5005, 5008, 5001 }, page.StatShardIds);
    }

    [TestMethod]
    public void ParseLolalytics_IgnoresGrayedRunes()
    {
        const string html = """
            Primary Runes
            <img src="https://cdn5.lolalytics.com/rune68/8214.webp" class="grayscale opacity-70">
            <img src="https://cdn5.lolalytics.com/rune68/8992.webp">
            <img src="https://cdn5.lolalytics.com/rune68/8226.webp">
            <img src="https://cdn5.lolalytics.com/rune68/8210.webp">
            <img src="https://cdn5.lolalytics.com/rune68/8237.webp">
            <img src="https://cdn5.lolalytics.com/rune68/8139.webp" class="grayscale">
            <img src="https://cdn5.lolalytics.com/rune68/8106.webp">
            <img src="https://cdn5.lolalytics.com/rune68/8135.webp">
            <img src="https://cdn5.lolalytics.com/statmod32/5005.webp" class="grayscale">
            <img src="https://cdn5.lolalytics.com/statmod32/5008.webp">
            <img src="https://cdn5.lolalytics.com/statmod32/5010.webp">
            <img src="https://cdn5.lolalytics.com/statmod32/5001.webp">
            """;

        var page = RunePageService.ParseLolalytics(html);

        Assert.AreEqual(8200, page.PrimaryStyleId);
        Assert.AreEqual(8100, page.SubStyleId);
        CollectionAssert.AreEqual(new[] { 8992, 8226, 8210, 8237 }, page.PerkIds);
        CollectionAssert.AreEqual(new[] { 8106, 8135 }, page.SecondaryPerkIds);
        CollectionAssert.AreEqual(new[] { 5008, 5010, 5001 }, page.StatShardIds);
    }

    private static string Active(int id, string name, string imageFolder) =>
        $"{{\\\"id\\\":{id},\\\"name\\\":\\\"{name}\\\",\\\"image_url\\\":\\\"https://example/{imageFolder}/{id}.png\\\",\\\"isActive\\\":true}}";
}

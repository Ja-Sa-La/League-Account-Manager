using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class DiscordPresenceTests
{
    [TestMethod]
    public void Presence_KeepsStatsInsideDiscordFieldLimits()
    {
        Settings.settingsloaded.DiscordShowTracker = true;
        Settings.settingsloaded.DiscordShowGitHub = true;
        Settings.settingsloaded.DiscordShowPartyJoin = true;
        Settings.settingsloaded.DiscordShowAlongsideGame = false;
        var presence = new DiscordPresenceService.Presence(
            DiscordPresenceService.ApplicationId,
            "League of Legends",
            DiscordPresenceService.LeagueImage,
            null,
            new string('D', 200) + "\n" + new string('S', 200),
            "https://tracker.gg/lol/profile/riot/Player%23TAG");

        var payload = presence.ToPayload();
        var activity = payload["args"]!["activity"]!;
        var buttons = (JArray)activity["buttons"]!;

        Assert.AreEqual(0, (int)activity["type"]!);
        Assert.AreEqual("League of Legends", (string)activity["name"]!);
        Assert.AreEqual(DiscordPresenceService.LeagueImage, (string)activity["assets"]!["large_image"]!);
        Assert.IsTrue((long)activity["timestamps"]!["start"]! > 0);
        Assert.AreEqual(DiscordPresenceService.DetailsLimit, ((string)activity["details"]!).Length);
        Assert.AreEqual(DiscordPresenceService.StateLimit, ((string)activity["state"]!).Length);
        Assert.IsTrue(((string)activity["details"]!).StartsWith("DDD", StringComparison.Ordinal));
        Assert.IsTrue(((string)activity["state"]!).StartsWith("SSS", StringComparison.Ordinal));
        Assert.AreEqual("Tracker", (string)buttons[0]!["label"]!);
        Assert.AreEqual("GitHub", (string)buttons[1]!["label"]!);
        Assert.IsTrue(((string)buttons[0]!["label"]!).Length <= DiscordPresenceService.ButtonLabelLimit);
        Assert.AreEqual("https://tracker.gg/lol/profile/riot/Player%23TAG", (string)buttons[0]!["url"]!);
        Assert.AreEqual("https://github.com/Ja-Sa-La/League-Account-Manager", (string)buttons[1]!["url"]!);

        var joinable = new DiscordPresenceService.Presence(
            DiscordPresenceService.ValorantApplicationId, "VALORANT", DiscordPresenceService.ValorantImage, null,
            "In lobby", "https://tracker.gg/valorant/profile/riot/Player%23TAG",
            "https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh");
        var joinButtons = (JArray)joinable.ToPayload()["args"]!["activity"]!["buttons"]!;
        Assert.AreEqual(2, joinButtons.Count);
        Assert.AreEqual("Join party", (string)joinButtons[0]!["label"]!);
        Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh", (string)joinButtons[0]!["url"]!);
        Assert.AreEqual("Tracker", (string)joinButtons[1]!["label"]!);

        Settings.settingsloaded.DiscordShowTracker = false;
        Settings.settingsloaded.DiscordShowGitHub = false;
        Settings.settingsloaded.DiscordShowPartyJoin = false;
        try
        {
            var hidden = new DiscordPresenceService.Presence(
                DiscordPresenceService.ApplicationId, "League of Legends", DiscordPresenceService.LeagueImage, null,
                "In lobby", "https://tracker.gg/lol/profile/riot/Player%23TAG",
                "https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh");
            Assert.IsNull(hidden.ToPayload()["args"]!["activity"]!["buttons"]);

            Settings.settingsloaded.DiscordShowGitHub = true;
            var githubOnly = (JArray)hidden.ToPayload()["args"]!["activity"]!["buttons"]!;
            Assert.AreEqual(1, githubOnly.Count);
            Assert.AreEqual("GitHub", (string)githubOnly[0]!["label"]!);
        }
        finally
        {
            Settings.settingsloaded.DiscordShowTracker = true;
            Settings.settingsloaded.DiscordShowGitHub = true;
            Settings.settingsloaded.DiscordShowPartyJoin = true;
        }

        Settings.settingsloaded.DiscordShowAlongsideGame = true;
        Assert.AreEqual(3, (int)presence.ToPayload()["args"]!["activity"]!["type"]!);
        Settings.settingsloaded.DiscordShowAlongsideGame = false;
    }

    [TestMethod]
    public void Presence_UsesSeparateApplicationIdentities()
    {
        var league = new DiscordPresenceService.Presence(
            DiscordPresenceService.ApplicationId, "League of Legends", DiscordPresenceService.LeagueImage, null,
            "In lobby\nLeague", "https://tracker.gg/lol/profile/riot/Player%23TAG");
        var valorant = new DiscordPresenceService.Presence(
            DiscordPresenceService.ValorantApplicationId, "VALORANT",
            "jett",
            DiscordPresenceService.ValorantImage,
            "Agent select\nVALORANT", "https://tracker.gg/valorant/profile/riot/Player%23TAG");

        Assert.AreNotEqual(league.ApplicationId, valorant.ApplicationId);
        Assert.AreEqual("League of Legends", (string)league.ToPayload()["args"]!["activity"]!["name"]!);
        Assert.AreEqual("VALORANT", (string)valorant.ToPayload()["args"]!["activity"]!["name"]!);
        Assert.AreEqual("jett", (string)valorant.ToPayload()["args"]!["activity"]!["assets"]!["large_image"]!);
        Assert.AreEqual(DiscordPresenceService.ValorantImage,
            (string)valorant.ToPayload()["args"]!["activity"]!["assets"]!["small_image"]!);
        Assert.AreEqual("kay-o", DiscordPresenceService.AgentImageKey("KAY/O"));
    }

    [TestMethod]
    public void DiscordPipeNames_KeepsOneSlotPerRunningClient()
    {
        var names = DiscordPresenceService.DiscordPipeNames([
            "discord-ipc-0",
            @"\\.\pipe\discord-ipc-1",
            "discord-ipc-9",
            "discord-ipc-10",
            "discord-ipc-",
            "discord-ipc-a",
            "Discord-IPC-0",
            "discord-system-helper-keybinds-sender-1",
            null,
            string.Empty
        ]);

        CollectionAssert.AreEquivalent(new[] { "discord-ipc-0", "discord-ipc-1", "discord-ipc-9" },
            names.ToArray());
    }

    [TestMethod]
    public void ValorantRound_UsesTheCurrentRoundAfterTheLastCompletedRound()
    {
        const string betweenRounds = """
            [22.29.46]LogShooterGameState: Warning: AShooterGameState::OnRoundEnded for round '6'
            [22.29.47]LogMapLoadModel: Display: UpdateMapReady
            """;
        const string playing = betweenRounds + """

            [22.30.17]LogShooterGameState: Warning: Gameplay started at local time 30.039062
            """;

        Assert.AreEqual(7, DiscordPresenceService.ValorantRound(betweenRounds));
        Assert.AreEqual(8, DiscordPresenceService.ValorantRound(playing));
        Assert.IsNull(DiscordPresenceService.ValorantRound("Client open"));
        Assert.IsNull(DiscordPresenceService.ValorantRound(playing + "\nTravelManager: loading the next match"));
                Assert.AreEqual("0-1", DiscordPresenceService.ValorantTeamScore(0, 1));
                Assert.AreEqual("2-2", DiscordPresenceService.ValorantTeamScore(2, 2));
                Assert.IsNull(DiscordPresenceService.ValorantTeamScore(null, 1));
    }

    [TestMethod]
    public void Preview_RespectsSelectedStatsAndKeepsSituationFirst()
    {
        var previous = Settings.settingsloaded;
        try
        {
            Settings.settingsloaded = Settings.CreateDefaults();
            Settings.settingsloaded.DiscordShowMatch = true;
            Settings.settingsloaded.DiscordShowScore = true;
            Settings.settingsloaded.DiscordShowLevel = true;
            Settings.settingsloaded.DiscordShowRank = false;

            Settings.settingsloaded.DiscordShowKd = true;
            Settings.settingsloaded.DiscordShowTeam = true;
            Settings.settingsloaded.DiscordShowPlayers = true;
            Settings.settingsloaded.DiscordShowPartySize = true;
            var preview = DiscordPresenceService.Preview("In game", "Swiftplay · Pearl · Tejo", "4-3", "12/4",
                "Player#EU", 176, "EU", "Immortal 2", "776 VP", "20 agents", "Red", "10 players", "1/5");

            Assert.IsTrue(preview.StartsWith("In game\n", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("Swiftplay · Pearl · Tejo", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("Red", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("10 players", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("1/5", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("Score 4-3", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("K/D 12/4", StringComparison.Ordinal));
            Assert.IsTrue(preview.Contains("Lv 176", StringComparison.Ordinal));
            Assert.IsFalse(preview.Contains("Player#EU", StringComparison.Ordinal));
            Assert.IsFalse(preview.Contains("Immortal", StringComparison.Ordinal));
            Assert.IsFalse(preview.Contains("776 VP", StringComparison.Ordinal));

            Settings.settingsloaded.DiscordShowTeam = false;
            Settings.settingsloaded.DiscordShowPlayers = false;
            Settings.settingsloaded.DiscordShowPartySize = false;
            var hiddenStats = DiscordPresenceService.Preview("In game", "Swiftplay · Pearl · Tejo", null, null,
                null, null, null, null, null, null, "Red", "10 players", "1/5");
            Assert.IsFalse(hiddenStats.Contains("Red", StringComparison.Ordinal));
            Assert.IsFalse(hiddenStats.Contains("10 players", StringComparison.Ordinal));
            Assert.IsFalse(hiddenStats.Contains("1/5", StringComparison.Ordinal));

            Settings.settingsloaded.DiscordShowPartySize = true;
            var lobbyOnly = DiscordPresenceService.Preview("In lobby", "Swiftplay", null, null, null, null, null,
                null, null, null, null, "10 players", "1/5");
            Assert.IsTrue(lobbyOnly.Contains("1/5", StringComparison.Ordinal));
            Assert.IsFalse(lobbyOnly.Contains("10 players", StringComparison.Ordinal));
            Assert.AreEqual("Blue", DiscordPresenceService.ValorantTeamName("Blue"));
            Assert.AreEqual("Red", DiscordPresenceService.ValorantTeamName("red"));
            Assert.IsNull(DiscordPresenceService.ValorantTeamName("TeamOne"));
        }
        finally
        {
            Settings.settingsloaded = previous;
        }
    }

    [TestMethod]
    public void QueueName_UsesLiveMatchNames()
    {
        Assert.AreEqual("Ranked Solo", DiscordPresenceService.QueueName(420));
        Assert.AreEqual("ARAM", DiscordPresenceService.QueueName(450));
        Assert.IsNull(DiscordPresenceService.QueueName(null));
        Assert.AreEqual("Swiftplay", DiscordPresenceService.ValorantModeName(
            "/Game/GameModes/_Development/Swiftplay_EndOfRoundCredits/Swiftplay_EoRCredits_GameMode"));
        Assert.AreEqual("Pearl", DiscordPresenceService.ValorantMapName("/Game/Maps/Pitt/Pitt"));
        Assert.AreEqual("Competitive", DiscordPresenceService.ValorantQueueName("competitive"));
        Assert.IsNull(DiscordPresenceService.ValorantQueueName("IN_PROGRESS"));
    }

    [TestMethod]
    public void ValorantParty_ShowsQueueAndPartySize()
    {
        var previous = Settings.settingsloaded;
        try
        {
            Settings.settingsloaded = Settings.CreateDefaults();
            string Encode(string json) =>
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var friendInGame = Encode("""
                {
                  "isValid": true,
                  "queueId": "competitive",
                  "partyId": "22222222-2222-2222-2222-222222222222",
                  "partySize": 1,
                  "maxPartySize": 5,
                  "partyOwnerMatchScoreAllyTeam": 2,
                  "partyOwnerMatchScoreEnemyTeam": 3,
                  "matchPresenceData": { "sessionLoopState": "INGAME" },
                  "partyPresenceData": { "partyState": "DEFAULT" }
                }
                """);
            var selfInLobby = Encode("""
                {
                  "isValid": true,
                  "queueId": "swiftplay",
                  "partyId": "11111111-1111-1111-1111-111111111111",
                  "partySize": 1,
                  "maxPartySize": 5,
                  "matchPresenceData": { "sessionLoopState": "MENUS" },
                  "partyPresenceData": { "partyState": "DEFAULT" }
                }
                """);
            var body = $$"""
                {"presences":[
                    {"puuid":"friend-puuid","product":"valorant","private":"{{friendInGame}}"},
                    {"puuid":"self-puuid","product":"valorant","private":"{{selfInLobby}}"}
                ]}
                """;

            var party = DiscordPresenceService.ValorantPartyFromPresences(body, "self-puuid");

            Assert.IsNotNull(party);
            Assert.AreEqual("In lobby", party.Label);
            Assert.AreEqual("Swiftplay", party.Details);
            Assert.AreEqual("1/5", party.Size);
            Assert.AreEqual("11111111-1111-1111-1111-111111111111", party.PartyId);
            Assert.IsNull(party.Score);
            Assert.AreEqual("In game", DiscordPresenceService.ValorantPartyFromPresences(body)!.Label);
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.PartyJoinUrl("HdGR-GWyw-xUAh"));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=ABC123",
                DiscordPresenceService.PartyJoinUrl("ABC123"));
            Assert.IsNull(DiscordPresenceService.PartyJoinUrl("not a code"));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.PartyJoinUrlFromActivities("""
                {"activities":{"activity":{"product":"valorant","joinCode":{"isActive":true,"joinCode":"HdGR-GWyw-xUAh"}}}}
                """));
            Assert.IsNull(DiscordPresenceService.PartyJoinUrlFromActivities("""
                {"activities":{"activity":{"product":"valorant","joinCode":{"isActive":false,"joinCode":"HdGR-GWyw-xUAh"}}}}
                """));
            Assert.AreEqual("valorant-activity", DiscordPresenceService.ValorantActivityId("""
                {"activities":{"league-activity":{"product":"league"},"valorant-activity":{"product":"valorant"}}}
                """));
            Assert.AreEqual("12/4", DiscordPresenceService.LeagueKd(JArray.Parse("""
                [{"puuid":"me","scores":{"kills":12,"deaths":4}}]
                """), "me", null));
            Assert.IsNull(DiscordPresenceService.LeagueKd(JArray.Parse("""[{"scores":{}}]"""), "me", null));
        }
        finally
        {
            Settings.settingsloaded = previous;
        }
    }

    [TestMethod]
    public void ValorantPartyLink_RejectsStaleJoinCodes()
    {
            Assert.IsNull(DiscordPresenceService.PartyJoinUrlFromActivities("""
                {"activities":{"activity":{"product":"valorant","joinCode":{"isActive":false,"expiresAt":0,"joinCode":"HdGR-GWyw-xUAh"}}}}
                """));
            var pastExpiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60000;
            var futureExpiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600000;
            Assert.IsNull(DiscordPresenceService.PartyJoinUrlFromActivities(
                "{\"activities\":{\"activity\":{\"product\":\"valorant\",\"joinCode\":{\"isActive\":true,\"expiresAt\":" +
                pastExpiry + ",\"joinCode\":\"HdGR-GWyw-xUAh\"}}}}"));
            Assert.IsNull(DiscordPresenceService.PartyJoinUrlFromActivities("""
                {"activities":{"activity":{"product":"valorant","joinCode":{"expiresAt":0,"joinCode":"HdGR-GWyw-xUAh"}}}}
                """));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.PartyJoinUrlFromActivities(
                    "{\"activities\":{\"activity\":{\"product\":\"valorant\",\"joinCode\":{\"isActive\":true,\"expiresAt\":" +
                    futureExpiry + ",\"joinCode\":\"HdGR-GWyw-xUAh\"}}}}"));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.PartyJoinUrlFromActivities("""
                    {"activities":{"activity":{"product":"valorant","joinCode":{"joinCode":"HdGR-GWyw-xUAh"}}}}
                    """));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=SMART",
                DiscordPresenceService.PartyJoinUrlFromActivities("""
                    {"activities":{"activity":{"product":"valorant","joinCode":{"isActive":true,"joinCode":"HdGR-GWyw-xUAh","smartUrl":"https://gg.riotgames.com/VAL?joinCode=SMART"}}}}
                    """));

            Assert.IsTrue(DiscordPresenceService.HasStaleValorantJoinCode("""
                {"activities":{"activity":{"product":"valorant","joinCode":{"isActive":false,"expiresAt":0,"joinCode":"HdGR-GWyw-xUAh"}}}}
                """));
            Assert.IsFalse(DiscordPresenceService.HasStaleValorantJoinCode("""
                {"activities":{"activity":{"product":"valorant"}}}
                """));

            var futureExpiryMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600000;
            var multiPartyActivities =
                "{\"activities\":{" +
                "\"2HULMfF3cBXF\":{\"activityId\":\"2HULMfF3cBXF\",\"gameMode\":\"social_mode_unrated\",\"id\":\"8df675fe-0e35-4a9f-9148-3e6327a65a05\",\"joinCode\":{\"expiresAt\":" + futureExpiryMs + ",\"isActive\":true,\"joinCode\":\"NEW1-cOdE-live\"},\"product\":\"valorant\",\"type\":\"party\"}," +
                "\"2gMGWtgCkM3P\":{\"activityId\":\"2gMGWtgCkM3P\",\"gameMode\":\"social_mode_swiftplay\",\"id\":\"a3b8c9d8-f345-4cf5-8605-3faa1682b0bb\",\"joinCode\":{\"expiresAt\":" + futureExpiryMs + ",\"isActive\":true,\"joinCode\":\"OLD1-cOdE-dead\"},\"product\":\"valorant\",\"type\":\"party\"}" +
                "}}";
            Assert.AreEqual("2gMGWtgCkM3P",
                DiscordPresenceService.ValorantActivityId(multiPartyActivities,
                    "a3b8c9d8-f345-4cf5-8605-3faa1682b0bb", "swiftplay"));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=OLD1-cOdE-dead",
                DiscordPresenceService.PartyJoinUrlFromActivities(multiPartyActivities,
                    "a3b8c9d8-f345-4cf5-8605-3faa1682b0bb", "swiftplay"));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=OLD1-cOdE-dead",
                DiscordPresenceService.PartyJoinUrlFromActivities(multiPartyActivities, null, "swiftplay"));
            Assert.IsNull(DiscordPresenceService.PartyJoinUrlFromActivities(multiPartyActivities,
                "11111111-1111-1111-1111-111111111111", "competitive"));
            Assert.IsFalse(DiscordPresenceService.HasStaleValorantJoinCode(multiPartyActivities,
                "11111111-1111-1111-1111-111111111111", "competitive"));
            Assert.AreEqual("2HULMfF3cBXF", DiscordPresenceService.ValorantActivityId(multiPartyActivities));

            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.ValidPartyJoinUrlFromCreateResponse("""
                    {"joinCode":"HdGR-GWyw-xUAh"}
                    """));
            Assert.AreEqual("https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh",
                DiscordPresenceService.ValidPartyJoinUrlFromCreateResponse("""
                    {"joinCode":{"isActive":true,"joinCode":"HdGR-GWyw-xUAh","expiresAt":0}}
                    """));
            Assert.IsNull(DiscordPresenceService.ValidPartyJoinUrlFromCreateResponse("""
                {"joinCode":{"isActive":false,"expiresAt":0,"joinCode":"HdGR-GWyw-xUAh"}}
                """));
            Assert.IsNull(DiscordPresenceService.ValidPartyJoinUrlFromCreateResponse("not json"));
    }

    [TestMethod]
    public void TrackerUrl_RequiresARealRiotId()
    {
        Assert.AreEqual("https://tracker.gg/valorant/profile/riot/Player%23EUW",
            DiscordPresenceService.TrackerUrl("Player#EUW", "valorant"));
        Assert.AreEqual("https://tracker.gg/lol/profile/riot/Player%23EUW",
            DiscordPresenceService.TrackerUrl(" Player # EUW ", "lol"));
        Assert.IsNull(DiscordPresenceService.TrackerUrl("VALORANT", "valorant"));
        Assert.IsNull(DiscordPresenceService.TrackerUrl("VALORANT#", "valorant"));
        Assert.IsNull(DiscordPresenceService.TrackerUrl(null, "valorant"));

        var missing = new DiscordPresenceService.Presence(
            DiscordPresenceService.ValorantApplicationId, "VALORANT", DiscordPresenceService.ValorantImage, null,
            "In lobby", null, "https://gg.riotgames.com/VAL?joinCode=HdGR-GWyw-xUAh");
        var buttons = (JArray)missing.ToPayload()["args"]!["activity"]!["buttons"]!;
        Assert.AreEqual(2, buttons.Count);
        Assert.AreEqual("Join party", (string)buttons[0]!["label"]!);
        Assert.AreEqual("GitHub", (string)buttons[1]!["label"]!);
    }

    [TestMethod]
    public void ValorantIdentity_ReadsRiotIdAndCompetitiveUpdates()
    {
        Assert.AreEqual("Player#EUW", DiscordPresenceService.RiotIdFromNameService("""
            [{"GameName":"Player","TagLine":"EUW"}]
            """));
        Assert.AreEqual("Player#EUW", DiscordPresenceService.RiotIdFromChatSession("""
            {"game_name":"Player","game_tag":"EUW"}
            """));
        Assert.IsNull(DiscordPresenceService.RiotIdFromNameService("[]"));
        Assert.AreEqual("Diamond 3 45 RR", DiscordPresenceService.DescribeValorantRank("""
            {"Matches":[{"TierAfterUpdate":20,"RankedRatingAfterUpdate":45}]}
            """));
        Assert.AreEqual("Diamond 3 45 RR", DiscordPresenceService.DescribeValorantRank("""
            {"LatestCompetitiveUpdate":{"TierAfterUpdate":20,"RankedRatingAfterUpdate":45}}
            """));
    }

    [TestMethod]
    public void Join_SkipsMissingStats()
    {
        Assert.AreEqual("Player#EUW · Level 30 · 12 champions",
            DiscordPresenceService.Join(" · ", "Player#EUW", null, "Level 30", " ", "12 champions"));
    }
}

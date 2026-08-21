using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Api;

// Payload shapes follow KebsCS/lcu-and-riotclient-api data_info.json metadata.
[TestClass]
public class ApiResponseParserTests
{
    [TestMethod]
    public void ParseSummoner_ReadsDocumentedIdentityFields()
    {
        const string json = """
                            {
                              "summonerId": 123456789,
                              "accountId": 987654321,
                              "displayName": "Legacy Name",
                              "internalName": "internal-name",
                              "profileIconId": 29,
                              "summonerLevel": 314,
                              "puuid": "sample-puuid",
                              "gameName": "Player One",
                              "tagLine": "EUW"
                            }
                            """;

        var result = ApiResponseParser.ParseSummoner(json);

        Assert.IsNotNull(result);
        Assert.AreEqual("123456789", result["summonerId"]?.ToString());
        Assert.AreEqual("sample-puuid", result["puuid"]?.ToString());
        Assert.AreEqual("Player One", result["gameName"]?.ToString());
        Assert.AreEqual("EUW", result["tagLine"]?.ToString());
        Assert.AreEqual(314, result["summonerLevel"]?.ToObject<int>());
    }

    [TestMethod]
    public void ParseSummoner_RejectsPayloadWithoutRequiredIdentityFields()
    {
        Assert.IsNull(ApiResponseParser.ParseSummoner("{\"gameName\":\"Player\"}"));
        Assert.IsNull(ApiResponseParser.ParseSummoner("not-json"));
    }

    [TestMethod]
    public void ParseWallet_ReadsDocumentedCurrencyMap()
    {
        var result = ApiResponseParser.ParseWallet("{\"RP\":1380,\"lol_blue_essence\":42000}");

        Assert.IsNotNull(result);
        Assert.AreEqual(1380, result.rp);
        Assert.AreEqual(42000, result.be);
    }

    [TestMethod]
    public void RankedStats_FormatsStandardAndApexQueues()
    {
        const string json = """
                            {
                              "queues": [],
                              "queueMap": {
                                "RANKED_SOLO_5x5": {
                                  "tier": "GOLD",
                                  "division": "II",
                                  "leaguePoints": 64,
                                  "wins": 20,
                                  "losses": 15
                                },
                                "RANKED_FLEX_SR": {
                                  "tier": "MASTER",
                                  "division": "I",
                                  "leaguePoints": 112,
                                  "wins": 41,
                                  "losses": 30
                                }
                              }
                            }
                            """;
        var ranked = ApiResponseParser.ParseRankedStats(json);

        Assert.IsNotNull(ranked);
        Assert.AreEqual("GOLD II 64 LP, 20 Wins, 15 Losses",
            ApiResponseParser.BuildRankString(ranked, "RANKED_SOLO_5x5"));
        Assert.AreEqual("MASTER 112 LP, 41 Wins, 30 Losses",
            ApiResponseParser.BuildRankString(ranked, "RANKED_FLEX_SR"));
        Assert.AreEqual("Unranked", ApiResponseParser.BuildRankString(ranked, "RANKED_TFT"));
    }

    [TestMethod]
    public void ParseMatchHistory_ReadsDocumentedNestedGamesShape()
    {
        const string json = """
                            {
                              "platformId": "EUW1",
                              "accountId": 987654321,
                              "games": {
                                "gameBeginDate": "2026-08-19T12:00:00Z",
                                "gameCount": 1,
                                "games": [
                                  {
                                    "gameCreationDate": "2026-08-19T12:34:56Z",
                                    "gameDuration": 185,
                                    "gameMode": "CLASSIC",
                                    "queueId": 420,
                                    "participants": [
                                      {
                                        "championId": 103,
                                        "stats": { "win": true, "kills": 8, "deaths": 2, "assists": 11 }
                                      }
                                    ]
                                  }
                                ]
                              }
                            }
                            """;

        var result = ApiResponseParser.ParseMatchHistory(json);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.LastPlayed));
        StringAssert.Contains(result.SerializedEntries!, "Win | Q:420 | CLASSIC | Champ:103");
        StringAssert.Contains(result.SerializedEntries!, "KDA 8/2/11 | 03:05");
    }

    [TestMethod]
    public void RiotClientState_ParsesReadyAndDocumentedEulaValues()
    {
        Assert.IsTrue(ApiResponseParser.IsRsoReady("{\"ready\":true}"));
        Assert.IsFalse(ApiResponseParser.IsRsoReady("{\"ready\":false}"));
        Assert.IsFalse(ApiResponseParser.IsRsoReady("invalid"));

        Assert.AreEqual("WaitingForAllServiceData",
            ApiResponseParser.ParseEulaAcceptance("\"WaitingForAllServiceData\""));
        Assert.AreEqual("AcceptanceRequired", ApiResponseParser.ParseEulaAcceptance("\"AcceptanceRequired\""));
        Assert.AreEqual("Accepted", ApiResponseParser.ParseEulaAcceptance("\"Accepted\""));
    }
}
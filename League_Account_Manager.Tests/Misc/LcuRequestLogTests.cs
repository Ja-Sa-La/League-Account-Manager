using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class LcuRequestLogTests
{
    [TestInitialize]
    public void Initialize()
    {
        LcuRequestLog.Clear();
    }

    [TestMethod]
    public void Add_PreservesSensitiveJsonAndQueryValues()
    {
        var record = LcuRequestLog.Add(
            "riot",
            "post",
            "/login?access_token=visible",
            "{\"username\":\"player\",\"password\":\"visible\"}",
            200,
            "OK",
            "{\"token\":\"visible\",\"value\":1}",
            12,
            requestHeaders: "Accept: application/json\nAuthorization: Basic visible\nCookie: session=visible");

        StringAssert.Contains(record.Endpoint, "access_token=visible");
        StringAssert.Contains(record.RequestBody, "\"password\":\"visible\"");
        StringAssert.Contains(record.ResponseBody, "\"token\":\"visible\"");
        StringAssert.Contains(record.RequestHeaders, "Authorization: Basic visible");
        StringAssert.Contains(record.RequestHeaders, "Cookie: session=visible");
    }

    [TestMethod]
    public void Update_MergesResponseIntoExistingOutgoingRecord()
    {
        var pending = LcuRequestLog.Add("league", "GET", "/resource", "", null, "Pending", "", 0,
            requestHeaders: "Accept: application/json", direction: "Outgoing", trafficType: "HTTP");

        var completed = LcuRequestLog.Update(pending.Id, 200, "OK", "{\"value\":1}", 42,
            responseHeaders: "Content-Type: application/json");

        var records = LcuRequestLog.Snapshot();
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual(pending.Id, completed.Id);
        Assert.AreEqual("Outgoing", records[0].Direction);
        Assert.AreEqual(200, records[0].StatusCode);
        Assert.AreEqual("Accept: application/json", records[0].RequestHeaders);
        Assert.AreEqual("Content-Type: application/json", records[0].ResponseHeaders);
    }

    [TestMethod]
    public void ProcessMessage_AddsWebSocketEventRecord()
    {
        LcuWebSocketMonitor.ProcessMessage(
            "[8,\"OnJsonApiEvent\",{\"uri\":\"/lol-gameflow/v1/gameflow-phase\",\"eventType\":\"Update\",\"data\":{\"token\":\"visible\",\"phase\":\"Lobby\"}}]");

        var record = LcuRequestLog.Snapshot().Single();
        Assert.AreEqual("WebSocket", record.TrafficType);
        Assert.AreEqual("Incoming", record.Direction);
        Assert.AreEqual("RECEIVE", record.Method);
        Assert.AreEqual("Update", record.EventType);
        Assert.AreEqual("/lol-gameflow/v1/gameflow-phase", record.Endpoint);
        StringAssert.Contains(record.ResponseBody, "OnJsonApiEvent");
        StringAssert.Contains(record.ResponseBody, "eventType");
        StringAssert.Contains(record.ResponseBody, "Lobby");
        StringAssert.Contains(record.ResponseBody, "\"token\":\"visible\"");
    }

    [TestMethod]
    public void ProcessMessage_IgnoresInvalidFrames()
    {
        LcuWebSocketMonitor.ProcessMessage("not-json");
        LcuWebSocketMonitor.ProcessMessage("[1,2]");

        Assert.AreEqual(0, LcuRequestLog.Snapshot().Count);
    }
}
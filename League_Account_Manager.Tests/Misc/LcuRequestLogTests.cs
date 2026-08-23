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
    public void Add_RedactsSensitiveJsonAndQueryValues()
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

        Assert.IsFalse(record.Endpoint.Contains("visible", StringComparison.Ordinal));
        Assert.IsFalse(record.RequestBody.Contains("visible", StringComparison.Ordinal));
        Assert.IsFalse(record.ResponseBody.Contains("visible", StringComparison.Ordinal));
        StringAssert.Contains(record.RequestBody, "[REDACTED]");
        StringAssert.Contains(record.ResponseBody, "[REDACTED]");
        StringAssert.Contains(record.RequestHeaders, "Accept: application/json");
        StringAssert.Contains(record.RequestHeaders, "Authorization: [REDACTED]");
        StringAssert.Contains(record.RequestHeaders, "Cookie: [REDACTED]");
        Assert.IsFalse(record.RequestHeaders.Contains("visible", StringComparison.Ordinal));
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
        Assert.IsFalse(record.ResponseBody.Contains("visible", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProcessMessage_IgnoresInvalidFrames()
    {
        LcuWebSocketMonitor.ProcessMessage("not-json");
        LcuWebSocketMonitor.ProcessMessage("[1,2]");

        Assert.AreEqual(0, LcuRequestLog.Snapshot().Count);
    }
}
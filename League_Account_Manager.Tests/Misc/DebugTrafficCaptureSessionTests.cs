using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class DebugTrafficCaptureSessionTests
{
    [TestInitialize]
    public void Initialize()
    {
        LcuRequestLog.Clear();
    }

    [TestMethod]
    public void CaptureHttp_PreservesHttpRecord()
    {
        using var session = new DebugTrafficCaptureSession();

        session.CaptureHttp("riot", "POST", "/login", "{\"password\":\"visible\"}", 200, "OK",
            "{\"token\":\"visible\"}", 15,
            requestHeaders: "Authorization: Basic visible", direction: "Outgoing");

        var record = LcuRequestLog.Snapshot().Single();
        Assert.AreEqual("HTTP", record.TrafficType);
        Assert.AreEqual("Outgoing", record.Direction);
        Assert.AreEqual("POST", record.Method);
        StringAssert.Contains(record.RequestBody, "\"password\":\"visible\"");
        StringAssert.Contains(record.ResponseBody, "\"token\":\"visible\"");
        StringAssert.Contains(record.RequestHeaders, "Authorization: Basic visible");
    }

    [TestMethod]
    public void CaptureXmpp_AddsIncomingAndOutgoingRecords()
    {
        using var session = new DebugTrafficCaptureSession();

        session.CaptureXmpp("riot", "Outgoing", "<message token=\"visible\" />");
        session.CaptureXmpp("riot", "Incoming", "<message>hello</message>");

        var records = LcuRequestLog.Snapshot();
        Assert.AreEqual(2, records.Count);
        Assert.AreEqual("XMPP", records[0].TrafficType);
        Assert.AreEqual("Outgoing", records[0].Direction);
        Assert.AreEqual("Incoming", records[1].Direction);
        StringAssert.Contains(records[0].RequestBody, "token=\"visible\"");
    }

    [TestMethod]
    public void CaptureRmsAndRtmp_AddProtocolRecords()
    {
        using var session = new DebugTrafficCaptureSession();

        session.CaptureRms("riot", "Incoming", "/rms/events", "{\"authorization\":\"visible\"}");
        session.CaptureRtmp("riot", "Outgoing", "/rtmp", "hello"u8);

        var records = LcuRequestLog.Snapshot();
        Assert.AreEqual(2, records.Count);
        Assert.AreEqual("RMS", records[0].TrafficType);
        Assert.AreEqual("RTMP", records[1].TrafficType);
        Assert.AreEqual("Incoming", records[0].Direction);
        Assert.AreEqual("Outgoing", records[1].Direction);
        StringAssert.Contains(records[0].ResponseBody, "\"authorization\":\"visible\"");
    }
}

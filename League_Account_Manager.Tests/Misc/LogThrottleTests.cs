using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class LogThrottleTests
{
    [TestMethod]
    public void ShouldLog_LimitsEachKeyToConfiguredInterval()
    {
        var throttle = new LogThrottle(TimeSpan.FromMinutes(1));
        var start = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        Assert.IsTrue(throttle.ShouldLog("worker-one", start));
        Assert.IsFalse(throttle.ShouldLog("worker-one", start.AddSeconds(59)));
        Assert.IsTrue(throttle.ShouldLog("worker-two", start.AddSeconds(59)));
        Assert.IsTrue(throttle.ShouldLog("worker-one", start.AddMinutes(1)));
    }
}
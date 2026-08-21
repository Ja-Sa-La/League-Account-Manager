using League_Account_Manager.Misc;
using Newtonsoft.Json.Linq;

namespace League_Account_Manager.Tests.Api;

[TestClass]
public class ChampSelectActionTimingTests
{
    [TestMethod]
    public void ShouldComplete_UsesAdjustedTimeLeftWhenAvailable()
    {
        var timer = JObject.Parse("{\"adjustedTimeLeftInPhase\":1000,\"timeLeftInPhase\":5000}");

        Assert.IsTrue(ChampSelectActionTiming.ShouldComplete(timer, 1250));
    }

    [TestMethod]
    public void ShouldComplete_WaitsWhileMoreThanThresholdRemains()
    {
        var timer = JObject.Parse("{\"adjustedTimeLeftInPhase\":1251}");

        Assert.IsFalse(ChampSelectActionTiming.ShouldComplete(timer, 1250));
    }

    [TestMethod]
    public void ShouldComplete_UsesTimeLeftFallbackAndRejectsMissingTimer()
    {
        Assert.IsTrue(ChampSelectActionTiming.ShouldComplete(
            JObject.Parse("{\"timeLeftInPhase\":750}"), 1250));
        Assert.IsFalse(ChampSelectActionTiming.ShouldComplete(null, 1250));
    }

    [TestMethod]
    public void GetRemainingMilliseconds_UsesEpochFieldsWhenAvailable()
    {
        var timer = JObject.Parse(
            "{\"internalNowInEpochMs\":10000,\"phaseEndTimeInEpochMs\":11200}");

        Assert.AreEqual(1200, ChampSelectActionTiming.GetRemainingMilliseconds(timer));
    }

    [TestMethod]
    public void IsChampionAvailable_TreatsMinusOneAsWildcard()
    {
        Assert.IsTrue(ChampSelectActionTiming.IsChampionAvailable(new HashSet<int> { -1 }, 99));
        Assert.IsTrue(ChampSelectActionTiming.IsChampionAvailable(new HashSet<int> { 99 }, 99));
        Assert.IsFalse(ChampSelectActionTiming.IsChampionAvailable(new HashSet<int> { 1 }, 99));
    }

    [TestMethod]
    public void ShouldComplete_UsesLocalDeadlineWhenServerTimerSnapshotIsFrozen()
    {
        var now = DateTimeOffset.UtcNow;
        var frozenTimer = JObject.Parse("{\"adjustedTimeLeftInPhase\":30000}");

        Assert.IsTrue(ChampSelectActionTiming.ShouldComplete(
            frozenTimer, now.AddMilliseconds(1000), now, 1250));
    }
}
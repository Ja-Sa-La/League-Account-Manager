using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Updater;

[TestClass]
public class UpdateArgumentsTests
{
    [TestMethod]
    public void TryGetTarget_ParsesInstalledExecutablePath()
    {
        var target = Path.GetFullPath(Path.Combine("install", "League_Account_Manager.exe"));

        var result = UpdateArguments.TryGetTarget(["--finish-update", target],
            Path.GetFullPath("temp_update.exe"), out var parsedTarget);

        Assert.IsTrue(result);
        Assert.AreEqual(target, parsedTarget);
    }

    [TestMethod]
    public void TryGetTarget_RejectsMissingSwitchAndSelfReplacement()
    {
        Assert.IsFalse(UpdateArguments.TryGetTarget([], "temp_update.exe", out _));
        Assert.IsFalse(UpdateArguments.TryGetTarget(["--other", "app.exe"], "temp_update.exe", out _));

        var current = Path.GetFullPath("temp_update.exe");
        Assert.IsFalse(UpdateArguments.TryGetTarget(["--finish-update", current], current, out _));
    }
}
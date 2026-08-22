using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class LogFileMaintenanceTests
{
    [TestMethod]
    public void TrimToNewestBytes_KeepsNewestContentWithinLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lam-log-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "0123456789");

            LogFileMaintenance.TrimToNewestBytes(path, 4);

            Assert.AreEqual("6789", File.ReadAllText(path));
            Assert.AreEqual(4, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TrimToNewestBytes_LeavesSmallFileUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lam-log-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "small");

            LogFileMaintenance.TrimToNewestBytes(path, 10);

            Assert.AreEqual("small", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
using System.IO;

namespace League_Account_Manager.Misc;

internal static class LogFileMaintenance
{
    public static void TrimToNewestBytes(string path, long maximumBytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= maximumBytes)
            return;

        var temporaryPath = path + ".trim";
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.Seek(-maximumBytes, SeekOrigin.End);
                source.CopyTo(destination);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
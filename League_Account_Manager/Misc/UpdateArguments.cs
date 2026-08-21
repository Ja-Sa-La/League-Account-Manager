using System.IO;

namespace League_Account_Manager.Misc;

internal static class UpdateArguments
{
    public static bool TryGetTarget(string[]? args, string? currentProcessPath, out string applicationPath)
    {
        applicationPath = string.Empty;
        if (args is not { Length: >= 2 } ||
            !string.Equals(args[0], "--finish-update", StringComparison.OrdinalIgnoreCase))
            return false;

        applicationPath = Path.GetFullPath(args[1]);
        return !string.Equals(applicationPath, currentProcessPath, StringComparison.OrdinalIgnoreCase);
    }
}
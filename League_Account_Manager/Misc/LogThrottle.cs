using System.Collections.Concurrent;

namespace League_Account_Manager.Misc;

internal sealed class LogThrottle
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLogged = new();
    private readonly TimeSpan _minimumInterval;

    public LogThrottle(TimeSpan minimumInterval)
    {
        _minimumInterval = minimumInterval;
    }

    public bool ShouldLog(string key, DateTimeOffset now)
    {
        while (true)
        {
            if (!_lastLogged.TryGetValue(key, out var lastLogged))
            {
                if (_lastLogged.TryAdd(key, now))
                    return true;

                continue;
            }

            if (now - lastLogged < _minimumInterval)
                return false;

            if (_lastLogged.TryUpdate(key, now, lastLogged))
                return true;
        }
    }
}
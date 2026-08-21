# Testing

The automated suite is in `League_Account_Manager.Tests` and uses MSTest. Run it with:

```powershell
dotnet test League_Account_Manager.Tests/League_Account_Manager.Tests.csproj
```

Collect Cobertura coverage with:

```powershell
dotnet test League_Account_Manager.Tests/League_Account_Manager.Tests.csproj --collect:"XPlat Code Coverage" --results-directory TestResults
```

The same commands are available as the VS Code tasks `test League_Account_Manager` and `test League_Account_Manager with coverage`.

## Covered Behavior

- Account file loading, migration, encryption, rename, collision, and future-schema safeguards
- Settings defaults and legacy-settings merge behavior
- Updater argument parsing
- Login-token archive path validation and proxy response classification
- Login-token response, URI, Markdown-link, product, and redirect formatting helpers
- LCU summoner, wallet, ranked stats, match history, ready-state, and EULA response parsing
- Utility formatting, region conversion, queue conversion, sorting, and derived account counts
- Offline launcher path normalization and PowerShell escaping

API fixtures follow the response metadata documented by `KebsCS/lcu-and-riotclient-api`.

## Integration Boundaries

The following behavior requires an installed Riot client, operating-system changes, a live network endpoint, or an interactive WPF desktop session. It is intentionally outside the deterministic unit suite:

- Live LCU and Riot Client authentication and HTTP/WebSocket calls
- Launching, stopping, or replacing Riot and application processes
- Self-update replacement of the running executable
- Hosts-file elevation and certificate download/cache behavior
- TLS chat proxying and live presence injection
- File pickers, message boxes, clipboard access, and full WPF navigation/event lifecycles

These areas should be exercised by a Windows integration smoke test before publishing a release.
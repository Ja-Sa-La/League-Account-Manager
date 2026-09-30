# League Account Manager

[![Latest Stable Release](https://img.shields.io/github/v/release/Ja-Sa-La/League-Account-Manager?label=stable)](https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest)
[![Latest Beta Release](https://img.shields.io/github/v/release/Ja-Sa-La/League-Account-Manager?include_prereleases&label=beta)](https://github.com/Ja-Sa-La/League-Account-Manager/releases)

A Windows WPF utility for managing League of Legends accounts, client data,
loot, profiles, friends, and in-client workflows from one place. The
application uses the League Client API (LCU) and does not use autoclickers or
game exploits.

For support, join the [Discord server](https://discord.gg/tjQVcc9SGP).

## Download

| Channel | Version | Download |
| --- | --- | --- |
| Stable | [2.7.0.14](https://github.com/Ja-Sa-La/League-Account-Manager/releases/tag/v2.7.0.14) | [Download latest stable](https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest) |
| Beta | [2.7.0.16-beta](https://github.com/Ja-Sa-La/League-Account-Manager/releases/tag/v2.7.0.16-beta) | [Download latest beta](https://github.com/Ja-Sa-La/League-Account-Manager/releases/tag/v2.7.0.16-beta) |

Beta builds contain the newest features and may be less stable. The current
beta adds the plugin platform described below.

## Features

- **Account management:** Store accounts locally in CSV format, optionally encrypt
  data with AES-GCM, share account files, and log in with a click.
- **Account data:** Save and search rank, level, champions, skins, loot, notes,
  regions, and calculated loot value.
- **Client tools:** Champion buyer, loot manager, disenchanter, queue helpers,
  configurable auto-accept, and stealth login.
- **Profile and social tools:** Profile editor, Riot ID changer, friend manager,
  bulk friend removal, and log cleanup.
- **Game workflow tools:** Champion select assistance, player stats where Riot
  permits them, and an improved report tool.
- **Diagnostics:** LCU traffic viewer, request logging, debug mode, and utility
  tools for inspecting client data.
- **Extensibility:** Trusted plugins can add navigation pages, XAML-based WPF
  views, page icons, LCU requests, and authenticated storefront requests.

See the [full feature documentation](docs/index.md) for screenshots and
individual tool guides.

## Plugin platform

The beta release introduces a plugin system for developers who want to extend
the application with native WPF pages. Plugins are regular .NET class
libraries, can use normal XAML and code-behind, and receive host-mediated LCU
and storefront clients through `IPluginContext`.

- [Plugin development guide](docs/pages/plugins.md)
- [Example plugin source](Examples/ExamplePlugin)
- [Plugin contract](League_Account_Manager.PluginContract)
- [Beta release notes](docs/releases/2.7.0.16-beta.md)

Plugins are trusted, in-process assemblies and are **not sandboxed**. Only
install plugins from sources you trust. The host creates the `Plugins` folder
automatically; restart the application after adding or updating a plugin.

## Screenshots

### Dashboard
<img src="https://github.com/user-attachments/assets/887ab552-7969-4eed-92d8-0537e7634a7e" width="100%" />

### Add Accounts
<img src="https://github.com/user-attachments/assets/94bb1a3f-4a85-4b20-8865-670b8f5841a5" width="100%" />

### Champion Select
<img src="https://github.com/user-attachments/assets/c08c7d11-c83d-4e8f-99d7-925a77bf7382" width="100%" />

### Auto Champion Select
<img src="https://github.com/user-attachments/assets/811edfbf-7416-4f84-8832-c02bc475642f" width="100%" />

### Champion Buyer
<img src="https://github.com/user-attachments/assets/b0ebfad5-9201-4c9d-a269-18406e18b959" width="100%" />

### Report Manager
<img src="https://github.com/user-attachments/assets/50584ce8-41bb-4eb7-8dd1-985d4d7d6955" width="100%" />

### Misc Tools
<img src="https://github.com/user-attachments/assets/59061b09-a74a-4e21-ac21-cf97dce94d98" width="100%" />

### Profile Editor
<img src="https://github.com/user-attachments/assets/fdde3f73-743c-4e11-8a6f-1ffb139c1627" width="100%" />

### Disenchanter
<img src="https://github.com/user-attachments/assets/0c016f75-086b-4542-9c2f-efd0d2f0cdaa" width="100%" />

### Settings Manager
<img src="https://github.com/user-attachments/assets/2903b900-9881-43f3-a21e-f37db8920b4e" width="100%" />

### Change Riot ID
<img src="https://github.com/user-attachments/assets/357fdd3b-59d7-46f1-88fa-c4369778df0b" width="600" />


## UI Pages / Tools

- Dashboard / Home
- Accounts list and search
- Champion Buyer
- Disenchanter / Loot Manager
- Misc Tools (log cleanup, queue helpers, loot value, etc.)
- Profile Editor (icon/status/background)
- Change Riot ID
- Friend Manager (Display inactive ones and bulk remove)
- Report Tool
- Settings

## Requirements

- Windows x64
- .NET 10 Desktop Runtime for the packaged app
- .NET 10 SDK for building and testing from source

## Install & Run (binary)

1. [Download the latest stable release](https://github.com/Ja-Sa-La/League-Account-Manager/releases/latest)
	or [latest beta](https://github.com/Ja-Sa-La/League-Account-Manager/releases).
2. Install the .NET 10 Desktop Runtime.
3. Run `League_Account_Manager.exe`.
4. If League permissions block an operation, start the app as Administrator.

## Build from Source

```powershell
git clone https://github.com/Ja-Sa-La/League-Account-Manager.git
cd League-Account-Manager
dotnet restore
dotnet build League_Account_Manager/League_Account_Manager.csproj -c Release
dotnet run --project League_Account_Manager/League_Account_Manager.csproj
```

Windows batch scripts are also available from the repository root:

```bat
build.bat
test.bat
publish.bat
```

`build.bat` and `test.bat` default to Release. Pass `Debug` as the first argument to use the Debug configuration. `publish.bat` creates a framework-dependent, single-file `win-x64` build in `artifacts\win-x64`.

## Tests

```powershell
dotnet test League_Account_Manager.Tests/League_Account_Manager.Tests.csproj
```

See [Testing](docs/testing.md) for coverage commands and integration-test boundaries.

## Documentation

- [Documentation home](docs/index.md)
- [Plugin development](docs/pages/plugins.md)
- [Testing and coverage](docs/testing.md)
- [Main window](docs/pages/main-window.md)
- [Accounts](docs/pages/accounts.md)
- [Champion buyer](docs/pages/champion-buyer.md)
- [Champion select](docs/pages/champion-select.md)
- [LCU traffic](docs/pages/lcu-traffic.md)
- [Profile editor](docs/pages/profile-editor.md)
- [Settings](docs/pages/settings.md)
- [Release notes](docs/releases)

## Privacy & Safety

- All account data stays on your machine (local CSV)
- Uses LCU endpoints and host-mediated storefront requests; no automation via autoclickers
- Account credentials, exported files, and debug logs may contain sensitive data

## Troubleshooting

- Run as Administrator if file access or League permissions fail.
- Ensure the League Client is running when using LCU-dependent features.
- Close the application before rebuilding or replacing plugin DLLs.
- See the [plugin troubleshooting guide](docs/pages/plugins.md#troubleshooting)
	for plugin loading, XAML page, contract, and locked-DLL issues.

## Contributing

PRs and issues are welcome. Please read the relevant [developer
documentation](docs/index.md), run the test suite, and keep changes within
Riot's ToS and LCU guidelines.
